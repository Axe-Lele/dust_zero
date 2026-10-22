// 협동 청소 — a ring that never ends, shared with a bot partner.
//
// 원본 index.html 의 coop* 31개 함수를 옮긴 것이다. 전투 자체는 BattleSim 이 그대로
// 돌린다(원본도 `deploy`/`newGame`/`bfs` 를 그대로 썼다). 이 클래스가 보태는 것은
// 고리 위 먼지 세기, 웨이브 잇기, 소환·합치기·강화·옮기기·바꾸기, 그리고 봇 파트너다.
//
// 본편 디펜스와 결정적으로 다른 점:
//  - **새는 곳이 없다.** 먼지는 고리를 영원히 돈다. 지는 조건은 청결도가 아니라
//    고리 위 먼지 수(보스는 5마리로 센다)가 coopLoopMax 에 닿는 것이다.
//  - **가전은 길을 막지 않는다.** 곁을 지나는 적을 정원만큼 붙잡아 늦출 뿐이다.
//  - **편성에서 고르는 게 아니라 뽑는다.** 소환은 그 칸에 설 수 있는 편성 가전 중 무작위다.
using System;
using System.Collections.Generic;
using DustZero.Core.Defense;
using DustZero.Data;

namespace DustZero.Core.Coop
{
    /// <summary>Who owns a placed appliance — the player's half or the partner's.</summary>
    public enum CoopSide { Player, Partner }

    /// <summary>What a tap on the board did, so the screen can say the right thing.</summary>
    public enum CoopTapResult
    {
        Nothing, Selected, Deselected, Moved, Merged, SpotPicked, SpotCleared,
        NotYourHalf, WrongTile, MoveOnCooldown, RingFull, PartnersUnit,
    }

    /// <summary>One run of 협동 청소.</summary>
    public sealed class CoopSim
    {
        /// <summary>A boss crowds the ring as much as five ordinary dust.</summary>
        public const int BossRingWeight = 5;

        /// <summary>Appliance kinds join the pool as the waves climb. (wave, enemy id)</summary>
        static readonly (int Wave, string Id)[] Ladder =
        {
            (1, "dust"), (2, "mite"), (3, "fly"), (4, "mold"), (6, "grease"),
            (8, "spore"), (10, "hair"), (12, "moth"), (14, "scale"), (16, "rust"),
        };

        static readonly string[] Bosses = { "boss_dust", "boss_grease", "boss_mold" };

        readonly GameDatabase _db;
        readonly ConfigRow _cfg;
        readonly IRandom _rng;
        readonly CoopPowerSource _power;
        readonly List<string> _deck;

        /// <summary>Which side placed each unit. BattleUnit has no owner field of its own.</summary>
        readonly Dictionary<int, CoopSide> _owner = new();

        /// <summary>Star per unit, 1 on summon and +1 per merge.</summary>
        readonly Dictionary<int, int> _star = new();

        /// <summary>Seconds left before a moved appliance may move again.</summary>
        readonly Dictionary<int, float> _moveCd = new();

        /// <summary>Type-upgrade levels bought, per side.</summary>
        readonly Dictionary<string, int>[] _upgrades =
            { new Dictionary<string, int>(), new Dictionary<string, int>() };

        /// <summary>How many appliances each side has summoned — the cost ramp.</summary>
        readonly int[] _summoned = new int[2];

        public BattleSim Battle { get; }
        public CoopPartnerRow Partner { get; }

        /// <summary>The partner's own power pool. Separate from the player's.</summary>
        public float PartnerPower { get; private set; }

        /// <summary>Dust currently on the ring, bosses counting five.</summary>
        public int RingLoad { get; private set; }

        public int PeakRingLoad { get; private set; }

        /// <summary>Waves fully survived. This is the score.</summary>
        public int WavesCleared { get; private set; }

        /// <summary>The wave now coming in.</summary>
        public int Wave { get; private set; }

        /// <summary>Daily-log points earned this run.</summary>
        public int PoolPoints { get; private set; }

        public int Coin { get; private set; }

        /// <summary>The unit the player has selected, if any.</summary>
        public BattleUnit Selected { get; private set; }

        /// <summary>The empty cell the player has lined up for the next summon.</summary>
        public (int Col, int Row)? Spot { get; private set; }

        public bool Finished => Battle.Finished;
        public bool Lost => Battle.Outcome == BattleOutcome.Lost;

        /// <summary>Raised when the partner says something, with the string_ko key suffix.</summary>
        public event Action<string> PartnerSpoke;

        int _nextWaveToQueue = 1;
        float _botThinkIn = 1f;
        bool _saidCrowded;

        public CoopSim(GameDatabase db, CoopPartnerRow partner, IReadOnlyList<string> deck,
                       IUnitPowerSource playerPower, IRandom rng, PresentationBus bus,
                       int clearedStages, int partnerLevel, int partnerStar)
        {
            _db = db;
            _cfg = db.Config;
            _rng = rng;
            _deck = new List<string>(deck);
            Partner = partner;
            _power = new CoopPowerSource(playerPower, _cfg, partnerLevel, partnerStar);

            Battle = new BattleSim(db, BuildStage(db, clearedStages), _deck,
                                   _power, rng, bus, loop: true);
            PartnerPower = _cfg.coopStartPow;
            QueueWaves();
        }

        // ------------------------------------------------------------------ 판 만들기

        /// <summary>
        /// 협동 전용 스테이지. 맵·길은 고정이고, 적 체력만 **그 계정이 어디까지 왔는지**로
        /// 정한다. 10.19-d 에서 바뀐 부분이다 — 전에는 마지막 스테이지의 hpMul 을 썼는데
        /// 그 값이 판마다 들쭉날쭉해서 협동 난이도가 같이 튀었다.
        /// </summary>
        static StageDef BuildStage(GameDatabase db, int clearedStages)
        {
            var cfg = db.Config;
            var stages = db.Stages;
            var reference = stages[Math.Max(0, Math.Min(clearedStages, stages.Count) - 1)];

            float accountPower = 1f
                                 + cfg.lvBonus * Math.Max(0, reference.RefLevel - 1)
                                 + cfg.starBonus * reference.RefStar;

            var paths = new PathDef[GameDatabase.CoopPaths.Length];
            for (int i = 0; i < paths.Length; i++) paths[i] = PathDef.Build(GameDatabase.CoopPaths[i]);

            return new StageDef
            {
                Id = 0,
                No = "협동",
                Name = "협동 청소",
                MapId = "coop",
                Tiles = GameDatabase.CoopMap,
                Paths = paths,
                SpawnNames = new[] { "창문", "" },
                GoalName = string.Empty,
                Gimmick = string.Empty,
                Desc = string.Empty,
                EnemyIds = Array.Empty<string>(),
                Schedule = Array.Empty<SpawnEvent>(),
                HpMul = cfg.coopHp0 * accountPower * (1f + reference.RefGear),
                StartPower = cfg.coopStartPow,
                // 고리판에는 골이 없다. 입구 바로 안쪽을 가리켜 두고 아무도 안 쓴다.
                GoalX = 1,
                GoalY = 3,
            };
        }

        // ------------------------------------------------------------------ 웨이브

        /// <summary>
        /// Keeps the schedule one wave ahead of the clock. 끝이 없으므로 미리 다 만들지 않고
        /// 필요한 만큼만 잇는다.
        /// </summary>
        void QueueWaves()
        {
            while (WaveStartTime(_nextWaveToQueue) < Battle.Time + _cfg.coopWaveGap * 1.6f)
            {
                BuildWave(_nextWaveToQueue);
                _nextWaveToQueue++;
            }
        }

        float WaveStartTime(int wave) => 4f + (wave - 1) * _cfg.coopWaveGap;

        void BuildWave(int wave)
        {
            float t0 = WaveStartTime(wave);
            float hpMul = (float)Math.Pow(_cfg.coopHpG, wave - 1);

            var pool = new List<string>();
            foreach (var (w, id) in Ladder) if (wave >= w) pool.Add(id);

            int count = Math.Min(_cfg.coopCntMax, _cfg.coopCnt0 + (int)(wave * _cfg.coopCntG));
            for (int i = 0; i < count; i++)
                Battle.AppendSpawn(new SpawnEvent(t0 + i * 0.6f, pool[(i + wave * 3) % pool.Count],
                                                  BattleSim.CoopEntryPath, hpMul, wave));

            if (wave % 5 != 0) return;
            Battle.AppendSpawn(new SpawnEvent(t0 + count * 0.6f + 0.6f,
                                              Bosses[(wave / 5 - 1) % Bosses.Length],
                                              BattleSim.CoopEntryPath, hpMul * _cfg.coopBossHp, wave));
        }

        // ------------------------------------------------------------------ 시간

        public void Tick(float dt)
        {
            if (Finished) return;

            PartnerPower = Mathf2.Min(_cfg.powerMax, PartnerPower + dt * Battle.PowerRegen());
            QueueWaves();
            AdvanceWave();

            Battle.Tick(dt);

            RingLoad = 0;
            foreach (var e in Battle.Enemies)
                if (!e.Dead) RingLoad += e.Def.Boss ? BossRingWeight : 1;
            if (RingLoad > PeakRingLoad) PeakRingLoad = RingLoad;

            if (RingLoad >= _cfg.coopLoopWarn) Say("leak");
            if (RingLoad >= _cfg.coopLoopMax) { Battle.ForceLose(); return; }

            if (Selected != null && Selected.Dead) Selected = null;
            if (Spot.HasValue && Battle.UnitAt(Spot.Value.Col, Spot.Value.Row) != null) Spot = null;

            TickMoveCooldowns(dt);

            _botThinkIn -= dt;
            if (_botThinkIn > 0f) return;
            _botThinkIn = 0.4f;
            BotAct();
        }

        /// <summary>
        /// 새 웨이브가 들어오면 앞 웨이브는 **버틴 것**이다. 둘 다 전력을 받고, 일지 점수와
        /// 코인이 그때 들어온다. 새는 곳이 없으니 이것 말고는 판이 넘어갔다는 신호가 없다.
        /// </summary>
        void AdvanceWave()
        {
            int spawned = Battle.SpawnedWave;
            if (spawned <= Wave) return;
            Wave = spawned;

            float bonus = _cfg.coopWavePow0 + _cfg.coopWavePowStep * (Wave - 1);
            Battle.AddPower(bonus);
            PartnerPower = Mathf2.Min(_cfg.powerMax, PartnerPower + bonus);

            if (Wave - 1 > WavesCleared)
            {
                WavesCleared = Wave - 1;
                PoolPoints += _cfg.poolCoopWave;
                Coin += _cfg.coopCoinPerWave;
                Battle.Bus.Banner($"{WavesCleared}웨이브 버팀! 전력 +{bonus * 100:0}W", 1.3f);
            }

            if (Wave % 5 == 0) Say("boss", force: true);
            if (Wave == 8) Say("late");
        }

        /// <summary>Scratch buffer so the per-frame cooldown sweep allocates nothing.</summary>
        readonly List<int> _expired = new();

        void TickMoveCooldowns(float dt)
        {
            if (_moveCd.Count == 0) return;
            _expired.Clear();
            // 사전을 돌면서 지울 수 없으므로 만료된 것만 모아 두었다 지운다.
            foreach (var id in _moveCd.Keys) if (_moveCd[id] - dt <= 0f) _expired.Add(id);
            foreach (var id in _expired) _moveCd.Remove(id);
            if (_moveCd.Count == 0) return;
            _cooling.Clear();
            foreach (var id in _moveCd.Keys) _cooling.Add(id);
            foreach (var id in _cooling) _moveCd[id] -= dt;
        }

        readonly List<int> _cooling = new();

        readonly Dictionary<string, float> _saidAt = new();

        void Say(string key, bool force = false)
        {
            if (!force && _saidAt.TryGetValue(key, out float t) && Battle.Time - t < 14f) return;
            _saidAt[key] = Battle.Time;
            PartnerSpoke?.Invoke(key);
        }

        // ------------------------------------------------------------------ 칸과 자리

        public CoopSide SideOf(BattleUnit u)
            => _owner.TryGetValue(u.Id, out var s) ? s : CoopSide.Player;

        public int StarOf(BattleUnit u) => _star.TryGetValue(u.Id, out int s) ? s : 1;

        public float MoveCooldownOf(BattleUnit u) => _moveCd.TryGetValue(u.Id, out float t) ? t : 0f;

        /// <summary>Rows a side may build on. 위 5줄이 파트너, 아래 5줄이 나다.</summary>
        public static bool OwnsRow(CoopSide side, int row)
            => side == CoopSide.Partner ? row <= GameDatabase.CoopSplit : row > GameDatabase.CoopSplit;

        /// <summary>
        /// What may stand here, or null. 'P' is a ring cell (melee), 'H' furniture (ranged).
        /// 창문 칸은 적이 들어오는 입구라 비워 둔다.
        /// </summary>
        public TileKind? TileAt(int c, int r)
        {
            if (r < 0 || r >= GameDatabase.CoopMap.Length) return null;
            var row = GameDatabase.CoopMap[r];
            if (c < 0 || c >= row.Length) return null;
            if (c == GameDatabase.CoopEntryCell[0] && r == GameDatabase.CoopEntryCell[1]) return null;
            return row[c] switch
            {
                'P' => TileKind.Ground,
                'H' => TileKind.Furniture,
                _ => (TileKind?)null,
            };
        }

        /// <summary>Free cells of this kind on a side.</summary>
        public List<(int Col, int Row)> FreeCells(CoopSide side, TileKind tile)
        {
            var found = new List<(int, int)>();
            for (int r = 0; r < GameDatabase.CoopMap.Length; r++)
            {
                if (!OwnsRow(side, r)) continue;
                for (int c = 0; c < GameDatabase.CoopMap[r].Length; c++)
                {
                    if (TileAt(c, r) != tile) continue;
                    if (Battle.UnitAt(c, r) != null) continue;
                    found.Add((c, r));
                }
            }
            return found;
        }

        /// <summary>Melee appliances this side has on the ring. Capped at coopLineMax.</summary>
        public int RingCount(CoopSide side)
        {
            int n = 0;
            foreach (var u in Battle.Units)
                if (!u.Dead && SideOf(u) == side && u.Def.Tile == TileKind.Ground) n++;
            return n;
        }

        public bool RingHasRoom(CoopSide side) => RingCount(side) < _cfg.coopLineMax;

        static readonly List<(int, int)> RingCells = BuildRingCells();

        static List<(int, int)> BuildRingCells()
        {
            var cells = new List<(int, int)>();
            for (int r = 0; r < GameDatabase.CoopMap.Length; r++)
                for (int c = 0; c < GameDatabase.CoopMap[r].Length; c++)
                    if (GameDatabase.CoopMap[r][c] == 'P' &&
                        !(c == GameDatabase.CoopEntryCell[0] && r == GameDatabase.CoopEntryCell[1]))
                        cells.Add((c, r));
            return cells;
        }

        /// <summary>How far this cell is from the nearest ring cell. Ranged units want to be close.</summary>
        public static float DistanceToRing(int c, int r)
        {
            float best = float.MaxValue;
            foreach (var (x, y) in RingCells)
            {
                float d = Mathf2.Hypot(x - c, y - r);
                if (d < best) best = d;
            }
            return best;
        }

        // ------------------------------------------------------------------ 소환

        public float SummonCost(CoopSide side)
            => (float)Math.Round((_cfg.coopSum0 + _cfg.coopSumStep * _summoned[(int)side]) * 10d) / 10f;

        List<string> DeckOf(CoopSide side)
            => side == CoopSide.Partner ? PartnerDeck() : _deck;

        List<string> _partnerDeck;

        List<string> PartnerDeck()
        {
            if (_partnerDeck != null) return _partnerDeck;
            _partnerDeck = new List<string>();
            foreach (var raw in (Partner.deck ?? string.Empty).Split(','))
            {
                var id = raw.Trim();
                if (id.Length > 0 && _db.Units.ContainsKey(id)) _partnerDeck.Add(id);
            }
            return _partnerDeck;
        }

        /// <summary>
        /// Which appliances could come out — of this kind if a cell is picked, any otherwise.
        /// 설 자리가 없는 종류는 빼므로, 목록이 비면 "자리 없음" 이다.
        /// </summary>
        public List<string> SummonableTypes(CoopSide side, TileKind? tile)
        {
            bool ringOk = RingHasRoom(side);
            var found = new List<string>();
            foreach (var t in DeckOf(side))
            {
                var def = _db.Unit(t);
                if (def == null) continue;
                if (tile.HasValue && def.Tile != tile.Value) continue;
                if (def.Tile == TileKind.Ground && !ringOk) continue;
                if (FreeCells(side, def.Tile).Count == 0) continue;
                found.Add(t);
            }
            return found;
        }

        public bool HasRoom(CoopSide side) => SummonableTypes(side, null).Count > 0;

        /// <summary>
        /// 소환. 칸을 골랐으면 그 칸에, 아니면 편성 중 무작위로 고른 가전이 설 수 있는 빈칸
        /// 가운데 **고리에서 가장 가까운 곳**에 세운다. 나오는 가전은 늘 무작위다.
        /// </summary>
        public BattleUnit Summon(CoopSide side, (int Col, int Row)? at = null)
        {
            float cost = SummonCost(side);
            if (PowerOf(side) < cost) return null;

            List<string> choices;
            (int Col, int Row) cell;

            if (at.HasValue)
            {
                var tile = TileAt(at.Value.Col, at.Value.Row);
                if (tile == null) return null;
                if (!OwnsRow(side, at.Value.Row)) return null;
                if (Battle.UnitAt(at.Value.Col, at.Value.Row) != null) return null;
                choices = SummonableTypes(side, tile);
                cell = at.Value;
            }
            else
            {
                choices = SummonableTypes(side, null);
                cell = default;
            }
            if (choices.Count == 0) return null;

            var type = choices[(int)(_rng.Value * choices.Count) % choices.Count];

            if (!at.HasValue)
            {
                var free = FreeCells(side, _db.Unit(type).Tile);
                if (free.Count == 0) return null;
                float best = float.MaxValue;
                foreach (var f in free)
                {
                    // 작은 흔들림을 섞어 같은 거리의 칸이 늘 같은 순서로 차지 않게 한다.
                    float d = DistanceToRing(f.Col, f.Row) + _rng.Value * 0.3f;
                    if (d >= best) continue;
                    best = d;
                    cell = f;
                }
            }

            var u = Place(side, type, cell.Col, cell.Row, cost);
            _summoned[(int)side]++;
            return u;
        }

        /// <summary>
        /// Puts one down and charges the summon price instead of the appliance's own cost.
        /// Deploy 가 제 값을 빼므로 그만큼 돌려주고 소환값을 받는 식이다 — 원본과 같다.
        /// </summary>
        BattleUnit Place(CoopSide side, string type, int c, int r, float cost)
        {
            bool bot = side == CoopSide.Partner;
            float saved = Battle.Power;
            if (bot)
            {
                _power.BotTurn = true;
                Battle.SetPower(PartnerPower);
            }

            var u = Battle.Deploy(type, c, r);
            Battle.AddPower(_db.Unit(type).Cost - cost);

            if (bot)
            {
                PartnerPower = Battle.Power;
                Battle.SetPower(saved);
                _power.BotTurn = false;
            }

            _owner[u.Id] = side;
            SetStar(u, 1);
            return u;
        }

        /// <summary>Power a side is holding right now.</summary>
        public float PowerOf(CoopSide side) => side == CoopSide.Partner ? PartnerPower : Battle.Power;

        /// <summary>
        /// Hands a side extra power. 개발 빌드의 테스트 조작과 검사용이다 — 정규 플레이에서
        /// 전력은 먼지를 잡을 때와 웨이브가 넘어갈 때만 들어온다.
        /// </summary>
        public void GrantPower(CoopSide side, float amount)
        {
            if (side == CoopSide.Partner)
                PartnerPower = Mathf2.Min(_cfg.powerMax, PartnerPower + amount);
            else
                Battle.AddPower(amount);
        }

        void SpendPower(CoopSide side, float amount)
        {
            if (side == CoopSide.Partner) PartnerPower -= amount;
            else Battle.AddPower(-amount);
        }

        // ------------------------------------------------------------------ ★

        /// <summary>
        /// ★ 적용. 소환이 ★1, 합칠 때마다 +1. 능력은 coopStarMul 배, 근접은 ★3·★5 에서
        /// 저지 정원이 하나 는다. 과열 비율은 지키고 다시 계산한다.
        /// </summary>
        void SetStar(BattleUnit u, int star)
        {
            var baseDef = _db.Unit(u.Type);
            float mul = star - 1 < _cfg.coopStarMul.Length ? _cfg.coopStarMul[star - 1] : 1f;
            float ratio = u.MaxHeat > 0f ? u.Heat / u.MaxHeat : 0f;

            _star[u.Id] = star;
            u.Tier = star;
            u.BoostMul = mul;
            u.Power = u.BasePower * mul * u.GearAtk;

            u.Atk = baseDef.Atk * u.Power;
            u.CoolRate = baseDef.Cool > 0f ? baseDef.Cool * u.BasePower * mul : 0f;
            u.Block = baseDef.Block + (star - 1) / 2;

            if (u.MaxHeat <= 0f) return;
            u.MaxHeat = baseDef.Heat * u.BasePower * mul * u.GearHeat;
            if (!u.Overheated) u.Heat = ratio * u.MaxHeat;
        }

        public bool CanMerge(BattleUnit a, BattleUnit b)
            => a != null && b != null && a != b && !a.Dead && !b.Dead
               && SideOf(a) == SideOf(b)
               && StarOf(a) == StarOf(b)
               && StarOf(a) < _cfg.coopStarMax;

        /// <summary>
        /// 합치기: a 를 b 자리로. 같은 가전이면 그대로 ★+1, 다른 가전이면 그 칸에 설 수 있는
        /// 편성 가전 중 무작위로 바뀐다 — 그래서 아무거나 합쳐도 손해는 아니다.
        /// </summary>
        public BattleUnit Merge(BattleUnit a, BattleUnit b)
        {
            if (!CanMerge(a, b)) return null;
            var side = SideOf(b);
            int star = StarOf(b) + 1;

            Drop(a);

            BattleUnit result;
            if (a.Type == b.Type)
            {
                SetStar(b, star);
                result = b;
            }
            else
            {
                var tile = _db.Unit(b.Type).Tile;
                var pick = new List<string>();
                foreach (var t in DeckOf(side)) if (_db.Unit(t)?.Tile == tile) pick.Add(t);
                var type = pick.Count > 0 ? pick[(int)(_rng.Value * pick.Count) % pick.Count] : b.Type;
                result = Replace(b, type, star);
            }

            if (Selected == a) Selected = null;
            Battle.Bus.Burst(result.X, result.Y, 16, "#FFD54A", 2.4f, 0.07f, 0.6f);
            Battle.Bus.Text(result.X, result.Y - 0.62f,
                            $"{result.Def.Name} ★{StarOf(result)}", "#F2C14E", true, true);
            return result;
        }

        /// <summary>Swaps one appliance for another on the same cell, keeping the star.</summary>
        BattleUnit Replace(BattleUnit old, string type, int star)
        {
            var side = SideOf(old);
            int c = old.Col, r = old.Row;
            Drop(old);

            bool bot = side == CoopSide.Partner;
            float saved = Battle.Power;
            if (bot) { _power.BotTurn = true; Battle.SetPower(PartnerPower); }

            var u = Battle.Deploy(type, c, r);
            // 갈아 끼우는 값은 따로 받았으므로 가전 제값은 돌려준다.
            Battle.AddPower(_db.Unit(type).Cost);
            Battle.ClearCooldown(type);

            if (bot) { PartnerPower = Battle.Power; Battle.SetPower(saved); _power.BotTurn = false; }

            _owner[u.Id] = side;
            SetStar(u, star);
            if (Selected == old) Selected = null;
            return u;
        }

        void Drop(BattleUnit u)
        {
            _owner.Remove(u.Id);
            _star.Remove(u.Id);
            _moveCd.Remove(u.Id);
            Battle.Remove(u);
        }

        // ------------------------------------------------------------------ 바꾸기 · 옮기기 · 강화

        public float RerollCost(BattleUnit u) => _cfg.coopRerollCost * StarOf(u);

        public List<string> RerollTypes(BattleUnit u)
        {
            var tile = _db.Unit(u.Type).Tile;
            var found = new List<string>();
            foreach (var t in DeckOf(SideOf(u)))
                if (t != u.Type && _db.Unit(t)?.Tile == tile) found.Add(t);
            return found;
        }

        /// <summary>★ 은 그대로, 같은 자리에 설 수 있는 다른 가전으로 바꾼다.</summary>
        public BattleUnit Reroll(BattleUnit u)
        {
            var side = SideOf(u);
            float cost = RerollCost(u);
            var choices = RerollTypes(u);
            if (choices.Count == 0 || PowerOf(side) < cost) return null;

            SpendPower(side, cost);
            var next = Replace(u, choices[(int)(_rng.Value * choices.Count) % choices.Count], StarOf(u));
            Battle.Bus.Burst(next.X, next.Y, 14, "#7FC8F8", 2.2f, 0.07f, 0.5f);
            Battle.Bus.Text(next.X, next.Y - 0.62f, $"{next.Def.Name}!", "#7FC8F8", true, true);
            return next;
        }

        /// <summary>
        /// 옮기기: 내 자리 안, 같은 종류 칸으로만. 옮긴 가전은 coopMoveCd 초 동안 못 움직인다 —
        /// 안 그러면 적을 따라다니며 계속 붙잡을 수 있다.
        /// </summary>
        public bool Move(BattleUnit u, int c, int r)
        {
            if (MoveCooldownOf(u) > 0f) return false;
            var side = SideOf(u);
            if (!OwnsRow(side, r)) return false;
            if (TileAt(c, r) != _db.Unit(u.Type).Tile) return false;
            if (Battle.UnitAt(c, r) != null) return false;

            Battle.ReleaseHolds(u);
            u.Load = 0;
            u.Col = c;
            u.Row = r;
            u.X = c + 0.5f;
            u.Y = r + 0.5f;
            _moveCd[u.Id] = _cfg.coopMoveCd;

            Battle.Bus.Emit(FxKind.Drop, u.X, u.Y, 0.4f);
            Battle.Bus.Burst(u.X, u.Y + 0.3f, 10, "#E2D2B5", 1.6f, 0.08f, 0.45f);
            return true;
        }

        public int UpgradeLevel(CoopSide side, string type)
            => _upgrades[(int)side].TryGetValue(type, out int v) ? v : 0;

        /// <summary>null once the type is fully upgraded.</summary>
        public float? UpgradeCost(CoopSide side, string type)
        {
            int lv = UpgradeLevel(side, type);
            return lv >= _cfg.coopUpCost.Length ? (float?)null : _cfg.coopUpCost[lv];
        }

        /// <summary>Attack multiplier this side's type-upgrades give.</summary>
        public float UpgradeMultiplier(CoopSide side, string type)
            => 1f + _cfg.coopUpStep * UpgradeLevel(side, type);

        /// <summary>강화는 **그 종류 전부**에 걸린다. 한 대가 아니라 편성 칸 하나를 올리는 것이다.</summary>
        public bool Upgrade(CoopSide side, string type)
        {
            var cost = UpgradeCost(side, type);
            if (!cost.HasValue || PowerOf(side) < cost.Value) return false;
            SpendPower(side, cost.Value);
            _upgrades[(int)side][type] = UpgradeLevel(side, type) + 1;
            return true;
        }

        // ------------------------------------------------------------------ 입력

        /// <summary>
        /// 판을 한 번 누른 것. 빈칸이면 소환 자리를 잡거나 고른 가전을 옮기고, 가전이면
        /// 고르거나 합친다. 화면은 돌아온 값으로 안내문만 정하면 된다.
        /// </summary>
        public CoopTapResult Tap(int c, int r)
        {
            var here = Battle.UnitAt(c, r);
            bool mine = OwnsRow(CoopSide.Player, r);
            var tile = TileAt(c, r);

            if (here != null && SideOf(here) == CoopSide.Partner)
            {
                Selected = null;
                return CoopTapResult.PartnersUnit;
            }

            if (here == null)
            {
                if (Selected != null)
                {
                    var u = Selected;
                    if (!mine || tile == null) { Selected = null; return CoopTapResult.Nothing; }
                    if (tile != _db.Unit(u.Type).Tile) return CoopTapResult.WrongTile;
                    if (MoveCooldownOf(u) > 0f) return CoopTapResult.MoveOnCooldown;
                    if (!Move(u, c, r)) return CoopTapResult.Nothing;
                    Selected = null;
                    return CoopTapResult.Moved;
                }

                if (!mine || tile == null) { Spot = null; return CoopTapResult.Nothing; }
                if (Spot.HasValue && Spot.Value.Col == c && Spot.Value.Row == r)
                {
                    Spot = null;
                    return CoopTapResult.SpotCleared;
                }
                if (tile == TileKind.Ground && !RingHasRoom(CoopSide.Player))
                {
                    Spot = null;
                    return CoopTapResult.RingFull;
                }
                Spot = (c, r);
                return CoopTapResult.SpotPicked;
            }

            Spot = null;

            if (Selected != null && Selected != here && Merge(Selected, here) != null)
            {
                Selected = null;
                return CoopTapResult.Merged;
            }
            if (Selected == here) { Selected = null; return CoopTapResult.Deselected; }

            Selected = here;
            return CoopTapResult.Selected;
        }

        /// <summary>How many of the player's units could pair with this one right now.</summary>
        public int MergeMatesFor(BattleUnit u)
        {
            int n = 0;
            foreach (var other in Battle.Units) if (!other.Dead && CanMerge(u, other)) n++;
            return n;
        }

        // ------------------------------------------------------------------ 파트너

        /// <summary>Pairs that could merge, same-type first and lowest star first.</summary>
        public List<(BattleUnit A, BattleUnit B)> Pairs(CoopSide side)
        {
            var mine = new List<BattleUnit>();
            foreach (var u in Battle.Units) if (!u.Dead && SideOf(u) == side) mine.Add(u);

            var pairs = new List<(BattleUnit, BattleUnit)>();
            for (int i = 0; i < mine.Count; i++)
                for (int j = i + 1; j < mine.Count; j++)
                    if (CanMerge(mine[i], mine[j])) pairs.Add((mine[i], mine[j]));

            pairs.Sort((x, y) =>
            {
                int sx = x.Item1.Type == x.Item2.Type ? 0 : 1;
                int sy = y.Item1.Type == y.Item2.Type ? 0 : 1;
                return sx != sy ? sx - sy : StarOf(x.Item1) - StarOf(y.Item1);
            });
            return pairs;
        }

        /// <summary>
        /// 파트너 한 수. rush 는 소환에 몰빵하고 자리가 차야 합치며, greedy 는 같은 가전부터
        /// 높은 ★ 로 합쳐 올린다. 둘 다 남는 전력은 가장 많이 가진 종류 강화에 쓴다.
        /// </summary>
        void BotAct()
        {
            const CoopSide Bot = CoopSide.Partner;
            if (Finished) return;

            var mine = new List<BattleUnit>();
            foreach (var u in Battle.Units) if (!u.Dead && SideOf(u) == Bot) mine.Add(u);

            bool full = !HasRoom(Bot);
            var pairs = Pairs(Bot);

            if (Partner.style == "greedy")
            {
                var same = pairs.FindAll(p => p.A.Type == p.B.Type);
                if (same.Count > 0)
                {
                    same.Sort((x, y) => StarOf(y.A) - StarOf(x.A));
                    var merged = Merge(same[0].A, same[0].B);
                    if (merged != null && StarOf(merged) >= 3 && !_saidCrowded)
                    {
                        _saidCrowded = true;
                        Say("big", force: true);
                    }
                    return;
                }
                if (full && pairs.Count > 0) { Merge(pairs[0].A, pairs[0].B); return; }
                // 초반엔 다섯 대만 세우고 전력을 모은다. 6웨이브부터 자리를 채운다.
                if ((mine.Count < 5 || (Wave >= 6 && !full)) && Summon(Bot) != null) return;
                if (UpgradeBest(Bot, mine)) return;
                if (PartnerPower >= SummonCost(Bot) + 3f) Summon(Bot);
                return;
            }

            // rush
            if (!full && Summon(Bot) != null) return;
            if (full && !_saidCrowded) { _saidCrowded = true; Say("big", force: true); }
            if (pairs.Count > 0 && (full || (mine.Count >= 8 && PartnerPower < SummonCost(Bot) - 1f)))
            {
                Merge(pairs[0].A, pairs[0].B);
                return;
            }
            float? cheapest = CheapestUpgrade(Bot, mine);
            if (cheapest.HasValue && (full || cheapest.Value < SummonCost(Bot))) UpgradeBest(Bot, mine);
        }

        float? CheapestUpgrade(CoopSide side, List<BattleUnit> mine)
        {
            float? best = null;
            var seen = new HashSet<string>();
            foreach (var u in mine)
            {
                if (!seen.Add(u.Type)) continue;
                var c = UpgradeCost(side, u.Type);
                if (c.HasValue && (!best.HasValue || c.Value < best.Value)) best = c;
            }
            return best;
        }

        /// <summary>Upgrades whichever type the side has most invested in and can afford.</summary>
        bool UpgradeBest(CoopSide side, List<BattleUnit> mine)
        {
            var weight = new Dictionary<string, int>();
            foreach (var u in mine)
                weight[u.Type] = (weight.TryGetValue(u.Type, out int w) ? w : 0) + StarOf(u);

            string pick = null;
            int bestWeight = -1;
            foreach (var kv in weight)
            {
                var cost = UpgradeCost(side, kv.Key);
                if (!cost.HasValue || PowerOf(side) < cost.Value) continue;
                if (kv.Value <= bestWeight) continue;
                bestWeight = kv.Value;
                pick = kv.Key;
            }
            return pick != null && Upgrade(side, pick);
        }
    }
}
