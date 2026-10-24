// Growth, currency, rewards, gacha and idle accrual over a SaveData.
//
// This is the port of the original's save-mutating helpers (addItem, addUnit, grant, powOf,
// condOk, lvCost, idlePending, claimIdle, pull). They live together because every one of them
// writes the profile, and keeping the writes in one type is what makes "did this reward
// already pay out?" answerable in a single place.
using System;
using System.Collections.Generic;
using System.Linq;
using DustZero.Core.Defense;
using DustZero.Data;

namespace DustZero.Core.Meta
{
    /// <summary>What a duplicate pull turned into.</summary>
    public enum DuplicateResult { New, LimitBreak, Converted }

    public readonly struct GrantedReward
    {
        public readonly int ItemId;
        public readonly string UnitId;
        public readonly int Count;
        public readonly DuplicateResult Duplicate;

        public bool IsUnit => UnitId != null;

        public GrantedReward(int itemId, int count)
        {
            ItemId = itemId; Count = count; UnitId = null; Duplicate = DuplicateResult.New;
        }
        public GrantedReward(string unitId, DuplicateResult dup)
        {
            ItemId = 0; Count = 1; UnitId = unitId; Duplicate = dup;
        }
    }

    public readonly struct LevelCost
    {
        public readonly int Coin;
        public readonly int Part;
        public LevelCost(int coin, int part) { Coin = coin; Part = part; }
    }

    public readonly struct IdlePending
    {
        public readonly double Hours;
        public readonly double Cap;
        public readonly double Coin, Part, Crystal;

        public IdlePending(double hours, double cap, double coin, double part, double crystal)
        {
            Hours = hours; Cap = cap; Coin = coin; Part = part; Crystal = crystal;
        }
    }

    public sealed partial class PlayerProfile : IUnitPowerSource
    {
        public const int CoinItemId = 1;
        public const int CrystalItemId = 2;

        public SaveData Data { get; private set; }
        public GameDatabase Db { get; }

        readonly IRandom _rng;
        readonly ConfigRow _cfg;

        /// <summary>Raised whenever the profile changed and should be written to disk.</summary>
        public event Action Changed;

        public PlayerProfile(GameDatabase db, SaveData data, IRandom rng)
        {
            Db = db;
            Data = data;
            _rng = rng;
            _cfg = db.Config;
        }

        public void MarkDirty() => Changed?.Invoke();

        // ------------------------------------------------------------------ currency

        public long Coin => (long)Math.Floor(Data.Item(CoinItemId));
        public long Crystal => (long)Math.Floor(Data.Item(CrystalItemId));
        public long ItemCount(int id) => (long)Math.Floor(Data.Item(id));

        public int PartItemOf(int family) => Db.PartOfFamily.TryGetValue(family, out var id) ? id : 0;

        // ------------------------------------------------------------------ roster

        public float PowerOf(string unitId)
        {
            var o = Data.Own(unitId);
            return o == null ? 1f : 1f + _cfg.lvBonus * (o.lv - 1) + _cfg.starBonus * o.star;
        }

        public int LevelOf(string unitId) => Data.Own(unitId)?.lv ?? 1;

        /// <summary>
        /// 10.17-b support growth. Only appliances with their own config keys grow this way;
        /// everything else returns 1, so the multiplier is safe to apply unconditionally.
        /// </summary>
        public float SupportPowerOf(string unitId)
        {
            var o = Data.Own(unitId);
            if (o == null || !_cfg.TryNum(unitId + "LvBonus", out float lvBonus)) return 1f;
            _cfg.TryNum(unitId + "StarBonus", out float starBonus);
            return 1f + lvBonus * (o.lv - 1) + starBonus * o.star;
        }

        /// <summary>
        /// Adds a unit. A first copy joins the roster (and the deck if there is room); a
        /// duplicate becomes a limit-break; past the cap it converts into the fallback reward.
        /// </summary>
        public DuplicateResult AddUnit(string unitId)
        {
            if (!Data.Owns(unitId))
            {
                Data.owned[unitId] = new OwnedUnit { lv = 1, star = 0 };
                if (Data.deck.Count < _cfg.deckMax) Data.deck.Add(unitId);

                if (_cfg.repRarityAutoSwap != 0 && Data.rep != null &&
                    Db.Units.TryGetValue(Data.rep, out var cur) &&
                    (int)Db.Units[unitId].Rarity > (int)cur.Rarity)
                    Data.rep = unitId;

                return DuplicateResult.New;
            }

            var owned = Data.owned[unitId];
            if (owned.star < _cfg.starMax)
            {
                owned.star++;
                return DuplicateResult.LimitBreak;
            }

            Grant(_cfg.dupeOverflowRewardGroup);
            return DuplicateResult.Converted;
        }

        /// <summary>Rolls a reward group. prob is in ten-thousandths, so 10000 is a guaranteed line.</summary>
        public List<GrantedReward> Grant(int groupId)
        {
            var got = new List<GrantedReward>();
            if (!Db.RewardGroups.TryGetValue(groupId, out var rows)) return got;

            int kindItem = Db.Enum("RewardKind", "Item");
            int kindUnit = Db.Enum("RewardKind", "Unit");

            foreach (var r in rows)
            {
                if (_rng.Value * 10000f >= r.prob) continue;
                int n = r.min + (int)Math.Floor(_rng.Value * (r.max - r.min + 1));

                if (r.kind == kindItem)
                {
                    if (!int.TryParse(r.refId.AsString(), out var itemId)) continue;
                    Data.AddItem(itemId, n);
                    got.Add(new GrantedReward(itemId, n));
                }
                else if (r.kind == kindUnit)
                {
                    var unitId = r.refId.AsString();
                    var dup = AddUnit(unitId);
                    got.Add(new GrantedReward(unitId, dup));
                }
            }
            return got;
        }

        public string ItemName(int itemId)
            => Db.Items.TryGetValue(itemId, out var it) ? Db.Text(it.nameKey) : itemId.ToString();

        /// <summary>
        /// Writes out one or more grants for a toast. A single purchase or battle result often
        /// grants the same item twice (e.g. a shop product's base reward plus its first-buy
        /// bonus, both in crystal) - those are summed into one line rather than shown as two,
        /// which otherwise read like "크리스탈 60 · 크리스탈 60" instead of "크리스탈 120".
        /// </summary>
        public string DescribeRewards(IEnumerable<GrantedReward> got)
        {
            var list = got as IList<GrantedReward> ?? got.ToList();

            var itemTotal = new Dictionary<int, int>();
            foreach (var g in list)
                if (!g.IsUnit) itemTotal[g.ItemId] = (itemTotal.TryGetValue(g.ItemId, out var v) ? v : 0) + g.Count;

            var seenItem = new HashSet<int>();
            var parts = new List<string>();
            foreach (var g in list)
            {
                if (g.IsUnit) { parts.Add(Db.Units[g.UnitId].Name); continue; }
                if (!seenItem.Add(g.ItemId)) continue;
                parts.Add($"{ItemName(g.ItemId)} {itemTotal[g.ItemId]:N0}");
            }
            return string.Join(" · ", parts);
        }

        // ------------------------------------------------------------------ conditions

        public bool ConditionMet(int conditionId)
        {
            if (conditionId == 0) return true;
            if (!Db.Conditions.TryGetValue(conditionId, out var c)) return true;

            if (c.kind == Db.Enum("CondKind", "StageClear"))
            {
                if (!int.TryParse(c.p1.AsString(), out var stageId)) return false;
                var st = Db.StageById(stageId);
                return st != null && Data.best >= st.Order;
            }
            return false;
        }

        public bool IdleUnlocked => Db.Unlocks.TryGetValue("idle", out var u) && ConditionMet(u.cond);
        public bool SurvivorUnlocked => !Db.Unlocks.TryGetValue("survivor", out var u) || ConditionMet(u.cond);

        /// <summary>
        /// 대청소가 잠긴 까닭. 타워·리그와 같이 <see cref="LockMessage"/> 로 표에서 읽는다 —
        /// 해금 스테이지는 빌드마다 움직이므로(10.19-e 1-1→1-2, 10.22-g 1-5) 화면에 적으면 틀린다.
        /// </summary>
        /// <example><code>
        /// // "1-5를 클리어하면 대청소 모드가 열려요." (string_ko 의 unlock.survivor)
        /// label.text = profile.SurvivorUnlocked ? "리더를 골라 출동" : profile.SurvivorLockMessage;
        /// </code></example>
        public string SurvivorLockMessage => LockMessage("survivor");

        /// <summary>2-5 를 깨야 열린다. 표에 조건이 없으면 열어 둔다.</summary>
        public bool TowerUnlocked => !Db.Unlocks.TryGetValue("tower", out var u) || ConditionMet(u.cond);
        public string TowerLockMessage => LockMessage("tower");

        /// <summary>3-5 를 깨야 열린다.</summary>
        public bool LeagueUnlocked => !Db.Unlocks.TryGetValue("league", out var u) || ConditionMet(u.cond);
        public string LeagueLockMessage => LockMessage("league");

        /// <summary>content_unlock 의 안내 문구. 표에 없으면 빈 문자열이다.</summary>
        public string LockMessage(string id)
            => Db.Unlocks.TryGetValue(id, out var u) ? Db.Text(u.lockKey) : string.Empty;

        // ------------------------------------------------------------------ stars

        public int ChapterStars(int chapter)
            => Db.Stages.Where(s => s.Chapter == chapter).Sum(s => Data.StarsOf(s.Id));

        public List<StarRewardRow> ClaimableStarRewards(int chapter)
        {
            if (!Db.StarRewards.TryGetValue(chapter, out var rows)) return new List<StarRewardRow>();
            int have = ChapterStars(chapter);
            return rows.Where(r => !Data.IsStarRewardClaimed(r.id) && have >= r.needStars).ToList();
        }

        public bool AnyStarRewardClaimable()
            => Db.StarRewards.Keys.Any(ch => ClaimableStarRewards(ch).Count > 0);

        /// <summary>
        /// Pays a star milestone exactly once. The claimed flag is written before the grant so
        /// a reward group that itself mutates the profile cannot re-enter and pay twice.
        /// </summary>
        public List<GrantedReward> ClaimStarReward(StarRewardRow row)
        {
            if (Data.IsStarRewardClaimed(row.id)) return new List<GrantedReward>();
            if (ChapterStars(row.chapter) < row.needStars) return new List<GrantedReward>();

            Data.MarkStarRewardClaimed(row.id);
            var got = Grant(row.rewardGroup);
            MarkDirty();
            return got;
        }

        // ------------------------------------------------------------------ level up

        public LevelCost? LevelUpCost(int level)
        {
            if (level >= _cfg.lvMax) return null;
            var band = Db.Raw.unit_levelup.FirstOrDefault(x => level >= x.lvMin && level <= x.lvMax);
            if (band == null) return null;
            return new LevelCost(
                band.coinBase + band.coinStep * (level - band.lvMin),
                band.partBase + band.partStep * (level - band.lvMin));
        }

        public bool CanLevelUp(string unitId)
        {
            var o = Data.Own(unitId);
            if (o == null) return false;
            var cost = LevelUpCost(o.lv);
            if (cost == null) return false;
            int part = PartItemOf(Db.Units[unitId].Family);
            return Coin >= cost.Value.Coin && ItemCount(part) >= cost.Value.Part;
        }

        public bool LevelUp(string unitId)
        {
            if (!CanLevelUp(unitId)) return false;
            var o = Data.owned[unitId];
            var cost = LevelUpCost(o.lv).Value;
            Data.AddItem(CoinItemId, -cost.Coin);
            Data.AddItem(PartItemOf(Db.Units[unitId].Family), -cost.Part);
            o.lv++;
            MarkDirty();
            return true;
        }

        // ------------------------------------------------------------------ deck

        public bool ToggleDeck(string unitId)
        {
            if (Data.deck.Contains(unitId)) Data.deck.Remove(unitId);
            else if (Data.deck.Count < _cfg.deckMax) Data.deck.Add(unitId);
            else return false;
            MarkDirty();
            return true;
        }

        // ------------------------------------------------------------------ idle

        /// <summary>Hourly rate from the highest stage cleared, or null before the first clear.</summary>
        public StageDef IdleRateStage => Data.best > 0 ? Db.StageByOrder(Data.best) : null;

        public IdlePending? IdlePendingNow()
        {
            if (!IdleUnlocked || Data.idleSince == 0) return null;
            var st = IdleRateStage;
            if (st == null) return null;

            double cap = _cfg.idleCapHours;
            double elapsedMs = SaveClock.NowUnixMs() - Data.idleSince;
            double hrs = Math.Min(cap, Math.Max(0d, elapsedMs / 3_600_000d));
            var b = Data.idleBank;

            return new IdlePending(hrs, cap,
                b.coin + st.IdleCoin * hrs,
                b.part + st.IdlePart * hrs,
                b.crystal + st.IdleCrystal * hrs);
        }

        /// <summary>
        /// Freezes what has accrued so far at the current rate. Called just before the rate
        /// changes on a first clear, so earlier hours are not retroactively repriced.
        /// </summary>
        public void BankIdle()
        {
            var p = IdlePendingNow();
            if (p == null) return;
            Data.idleBank = new IdleBank { coin = p.Value.Coin, part = p.Value.Part, crystal = p.Value.Crystal };
            Data.idleSince = SaveClock.NowUnixMs();
        }

        public (long coin, long part, long crystal)? ClaimIdle()
        {
            var p = IdlePendingNow();
            if (p == null) return null;

            long coin = (long)Math.Floor(p.Value.Coin);
            long part = (long)Math.Floor(p.Value.Part);
            long crystal = (long)Math.Floor(p.Value.Crystal);

            Data.AddItem(CoinItemId, coin);
            Data.AddItem(CrystalItemId, crystal);
            foreach (var partId in Db.PartOfFamily.Values) Data.AddItem(partId, part);

            // Keep the fractional remainder so repeated small claims do not shave value.
            Data.idleBank = new IdleBank
            {
                coin = p.Value.Coin - coin,
                part = p.Value.Part - part,
                crystal = p.Value.Crystal - crystal,
            };
            Data.idleSince = SaveClock.NowUnixMs();
            MarkDirty();
            return (coin, part, crystal);
        }

        // ------------------------------------------------------------------ attendance

        public bool AttendanceAvailable => Data.att.last != SaveClock.TodayKey();

        /// <summary>How many squares of the wheel are already taken.</summary>
        public int AttendanceTaken => Data.att.n;

        /// <summary>The square a claim would turn next, 1-based, wrapping at the table's end.</summary>
        public int AttendanceNextDay
        {
            get
            {
                int days = Db.Attendance.Count;
                return days <= 0 ? 0 : (Data.att.n % days) + 1;
            }
        }

        public AttendanceRow AttendanceRowFor(int day)
        {
            foreach (var r in Db.Attendance) if (r.day == day) return r;
            return null;
        }

        /// <summary>
        /// Turns the next square of the seven-day wheel. The reward comes from the
        /// attendance table rather than one fixed group, which is what the new build added.
        /// </summary>
        public List<GrantedReward> ClaimAttendance()
        {
            if (!AttendanceAvailable) return new List<GrantedReward>();

            var row = AttendanceRowFor(AttendanceNextDay);
            Data.att.last = SaveClock.TodayKey();
            Data.att.n++;
            Data.daily = Data.att.last;

            var got = Grant(row != null ? row.rewardGroup : _cfg.attendanceRewardGroup);
            AddPassXp(_cfg.passXpAttend);
            MarkDirty();
            return got;
        }

        // ------------------------------------------------------------------ gacha

        public bool CanPull(GachaRow banner)
            => ItemCount(banner.costItem) >= banner.costCount && ConditionMet(banner.unlockCond);

        string RollFrom(IReadOnlyList<GachaPoolRow> pool)
        {
            float total = pool.Sum(p => p.weight);
            float r = _rng.Value * total;
            foreach (var p in pool)
            {
                r -= p.weight;
                if (r < 0f) return p.unitId;
            }
            return pool[^1].unitId;
        }

        int RarityRank(int enumValue)
        {
            if (enumValue == Db.Enum("Rarity", "SSR")) return (int)Rarity.SSR;
            if (enumValue == Db.Enum("Rarity", "SR")) return (int)Rarity.SR;
            return (int)Rarity.R;
        }

        /// <summary>
        /// Performs a pull. The guarantee applies only to the final slot, and only if nothing
        /// of that rarity already came up, matching the original's rule exactly.
        /// </summary>
        public List<(string unitId, DuplicateResult dup)> Pull(GachaRow banner)
        {
            var result = new List<(string, DuplicateResult)>();
            if (!CanPull(banner)) return result;

            Data.AddItem(banner.costItem, -banner.costCount);
            var pool = Db.Pools[banner.poolId];

            int pity = GachaPity;
            for (int i = 0; i < banner.pulls; i++)
            {
                var t = RollFrom(pool);

                // 10.22-d 천장. 원본 순서를 그대로 지킨다 — 먼저 뽑고, 천장이 걸리면
                // SSR 풀에서 다시 뽑고, **그 다음에** 10연 확정을 본다.
                if (pity > 0 && Data.gpity >= pity - 1 && Db.Units[t].Rarity != Rarity.SSR)
                {
                    var ssr = pool.Where(p => Db.Units[p.unitId].Rarity == Rarity.SSR).ToList();
                    if (ssr.Count > 0) t = RollFrom(ssr);
                }

                if (banner.guaranteeRarity != 0 && i == banner.pulls - 1)
                {
                    int need = RarityRank(banner.guaranteeRarity);
                    bool already = result.Any(x => (int)Db.Units[x.Item1].Rarity >= need);
                    if (!already && (int)Db.Units[t].Rarity < need)
                    {
                        var upgraded = pool.Where(p => (int)Db.Units[p.unitId].Rarity >= need).ToList();
                        if (upgraded.Count > 0) t = RollFrom(upgraded);
                    }
                }
                result.Add((t, AddUnit(t)));
                // SSR 이 나오면 쌓인 횟수가 0 으로 돌아간다.
                Data.gpity = Db.Units[t].Rarity == Rarity.SSR ? 0 : Data.gpity + 1;
            }

            MarkDirty();
            return result;
        }

        /// <summary>config 의 천장 횟수. 0 이면 천장이 없다(표가 정한다).</summary>
        public int GachaPity
            => Db.Config.TryNum("gachaPity", out float n) ? (int)System.Math.Round(n) : 0;

        /// <summary>
        /// 원본 `pityLeft` — SSR 확정까지 남은 횟수. 천장이 없으면 0.
        /// </summary>
        /// <example><code>
        /// // gachaPity 80, gpity 0 -> 80회 남음
        /// label.text = $"SSR 확정까지 {profile.PityLeft}회";
        /// </code></example>
        public int PityLeft
        {
            get
            {
                int pity = GachaPity;
                return pity <= 0 ? 0 : System.Math.Max(1, pity - Data.gpity);
            }
        }

        /// <summary>
        /// 10.18-d 첫 소환(무료 · SSR 확정)을 아직 쓸 수 있는가.
        /// 원본 조건은 `!S.tutPull && S.best>=2 && UNITS[tutPullUnit]` 이다.
        /// </summary>
        public bool FreeFirstPullReady
            => Data.tutPull == 0 && Data.best >= 2
               && Db.Config.TryStr("tutPullUnit", out string id) && Db.Units.ContainsKey(id);

        /// <summary>첫 소환으로 받는 가전. 쓸 수 없으면 null.</summary>
        public string FreeFirstPullUnit
            => FreeFirstPullReady && Db.Config.TryStr("tutPullUnit", out string id) ? id : null;

        /// <summary>
        /// 첫 소환을 쓴다. 원본 `tutPull` 과 같이 **편성을 건드리지 않고** 가전만 더한다.
        /// </summary>
        public DuplicateResult? ClaimFreeFirstPull()
        {
            string id = FreeFirstPullUnit;
            if (id == null) return null;
            var keep = new List<string>(Data.deck);
            var tag = AddUnit(id);
            Data.deck.Clear();
            Data.deck.AddRange(keep);
            Data.tutPull = 1;
            MarkDirty();
            return tag;
        }

        // ------------------------------------------------------------------ battle result

        /// <summary>Outcome of applying a finished battle to the profile.</summary>
        public readonly struct StageResult
        {
            public readonly bool FirstClear;
            public readonly int Stars;
            public readonly int PreviousStars;
            public readonly bool NewRecord;
            public readonly bool UnlockedStarReward;
            public readonly List<GrantedReward> Rewards;
            public readonly StageDef PreviousIdleStage;
            public readonly StageDef NewIdleStage;
            /// <summary>시즌 패스 경험치. 첫 클리어와 재도전의 값이 다르다.</summary>
            public readonly int PassXp;
            /// <summary>이 판으로 패스 레벨이 올랐으면 새 레벨, 아니면 0.</summary>
            public readonly int PassLevel;
            /// <summary>이 판을 깨서 새로 열리거나 강화된 전력 스킬 (id, 레벨).</summary>
            public readonly List<(string Id, int Level)> PowerSkills;

            public StageResult(bool firstClear, int stars, int prev, bool newRecord, bool unlockedStar,
                               List<GrantedReward> rewards, StageDef prevIdle, StageDef newIdle,
                               int passXp, int passLevel, List<(string, int)> powerSkills)
            {
                FirstClear = firstClear; Stars = stars; PreviousStars = prev; NewRecord = newRecord;
                UnlockedStarReward = unlockedStar; Rewards = rewards;
                PreviousIdleStage = prevIdle; NewIdleStage = newIdle;
                PassXp = passXp; PassLevel = passLevel;
                PowerSkills = powerSkills ?? new List<(string, int)>();
            }
        }

        /// <summary>
        /// Records a finished battle. The first-clear reward is keyed on stage order exceeding
        /// best, so replaying a cleared stage never pays the first-clear bundle again.
        /// </summary>
        public StageResult ApplyStageResult(StageDef stage, bool won, int life)
        {
            int stars = 0;
            if (won) foreach (var v in _cfg.starLifeMin) if (life >= v) stars++;

            bool firstClear = won && stage.Order == Data.best + 1;
            int prevStars = Data.StarsOf(stage.Id);
            bool newRecord = stars > prevStars;

            int claimableBefore = ClaimableStarRewards(stage.Chapter).Count;
            if (newRecord) Data.SetStars(stage.Id, stars);
            bool unlockedStar = ClaimableStarRewards(stage.Chapter).Count > claimableBefore;

            // 전력 스킬은 스테이지 클리어로 열린다. 이 판 전후를 비교해야 "방금 열렸다" 를
            // 알 수 있으므로 진행도를 올리기 **전에** 찍어 둔다.
            var psBefore = new Dictionary<string, int>();
            foreach (var id in Db.PowerSkills.Keys) psBefore[id] = PowerSkillLevel(id);

            var rewards = new List<GrantedReward>();
            StageDef prevIdle = null, newIdle = null;

            if (firstClear)
            {
                prevIdle = IdleRateStage;
                BankIdle();
                rewards = Grant(stage.FirstRewardGroup);
                Data.best = stage.Order;
                if (Data.idleSince == 0 && IdleUnlocked) Data.idleSince = SaveClock.NowUnixMs();
                newIdle = IdleRateStage;
            }

            // 시즌 패스 경험치. 첫 클리어 100, 재도전 30 (config).
            int passXp = won ? (firstClear ? _cfg.passXpStageFirst : _cfg.passXpStageRepeat) : 0;
            int passLevel = 0;
            if (passXp > 0)
            {
                int beforeLv = PassLevel;
                AddPassXp(passXp);
                if (PassLevel > beforeLv) passLevel = PassLevel;
            }

            var psUps = new List<(string, int)>();
            foreach (var id in Db.PowerSkills.Keys)
            {
                int now = PowerSkillLevel(id);
                if (now > psBefore[id]) psUps.Add((id, now));
            }

            MarkDirty();
            return new StageResult(firstClear, stars, prevStars, newRecord, unlockedStar,
                                   rewards, prevIdle, newIdle, passXp, passLevel, psUps);
        }

        /// <summary>
        /// 지금 쓸 수 있는 전력 스킬 레벨. 0이면 아직 안 열렸다.
        ///
        /// 전력 스킬은 **스테이지 클리어**로 열린다 — 각 줄의 unlockStage 가 그 줄을 여는
        /// 판이고, 깬 판(order ≤ best)까지의 줄 중 가장 높은 레벨이 현재 레벨이다.
        /// </summary>
        public int PowerSkillLevel(string id)
        {
            if (id == null || !Db.PowerSkills.TryGetValue(id, out var rows)) return 0;
            int lv = 0;
            foreach (var r in rows)
            {
                if (r.unlockCond != 0 && !ConditionMet(r.unlockCond)) continue;
                if (r.unlockStage != 0)
                {
                    var stage = Db.StageById(r.unlockStage);
                    if (stage == null || stage.Order > Data.best) continue;
                }
                if (r.lv > lv) lv = r.lv;
            }
            return lv;
        }

        // ------------------------------------------------------------------ survivor daily

        /// <summary>
        /// Rolls the daily allowance over at local midnight and returns the record.
        /// 10.15-h splits the two bests apart: bestT is today's, bestEver is lifetime, and
        /// the paid survival marks reset with the day so the crystal cap is per-day.
        /// </summary>
        public SurvivorRecord SurvivorToday()
        {
            var key = SaveClock.TodayKey();
            if (Data.surv == null || Data.surv.day != key)
            {
                Data.surv = new SurvivorRecord
                {
                    day = key,
                    runs = 0,
                    bestT = 0,
                    bestKills = 0,
                    bestEver = Data.surv?.bestEver ?? 0,
                    leader = Data.surv?.leader,
                    marks = new List<int>(),
                };
            }
            Data.surv.marks ??= new List<int>();
            return Data.surv;
        }

        /// <summary>
        /// 오늘 청소 일지 점수와 오늘의 최대치. 10.18-h 전에는 생존 구간별 크리스탈이었다.
        /// </summary>
        public (int got, int total) SurvivorPoolToday() => (Pool.pts, PoolMaxPoints);

        public int SurvivorRunsLeft() => Math.Max(0, _cfg.survDailyRewards - SurvivorToday().runs);
    }
}
