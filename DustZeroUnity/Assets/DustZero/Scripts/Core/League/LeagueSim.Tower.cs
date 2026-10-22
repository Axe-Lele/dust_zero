// 먼지 타워 — 층마다 한 번만 주는 보상 던전.
//
// 원본 `tw*` 함수를 옮긴 것이다. 전투는 **리그 판을 그대로 쓴다**(원본도 `LG.mode='tower'`
// 로 같은 객체를 돌린다). 다른 것은 준비 단계와 층 규칙이다:
//
//  - 상점도 코인도 합체도 없다. **보유 가전을 골라 놓고 한 판**이다.
//  - 라운드가 없다. 한 번 이기면 그 층은 끝이고, 보상은 첫 클리어와 보너스 목표에 한 번씩.
//  - 계정 성장이 **상한 없이** 그대로 들어간다(리그는 config.leagueGrowthMax 까지).
//  - 층마다 규칙이 하나 붙는다: 열기/정전/거울/물바닥/지키기/황금/번식/손님/좁은 복도/단독/보스.
using System;
using System.Collections.Generic;
using System.Linq;
using DustZero.Core.Meta;
using DustZero.Data;

namespace DustZero.Core.League
{
    /// <summary>한 층을 도는 동안의 타워 쪽 상태.</summary>
    public sealed class TowerRun
    {
        public int Floor;
        public TowerFloorRow Row;
        public TowerRuleRow Rule;
        /// <summary>규칙 id. 없으면 빈 문자열이다.</summary>
        public string RuleId = string.Empty;

        /// <summary>이 층에서만 빌려 주는 가전. 없으면 null.</summary>
        public string Guest;
        /// <summary>손님의 성장 배수 — 보유 가전 상위 여섯의 평균이다.</summary>
        public float GuestPower = 1f;

        /// <summary>물바닥 규칙이 깎는 몫.</summary>
        public float WaterMove, WaterAtk;

        /// <summary>지키기 층의 공기청정기.</summary>
        public LeagueUnit Purifier;
        /// <summary>황금 먼지.</summary>
        public LeagueUnit Gold;

        /// <summary>쓰러진 우리 가전 수 — 보너스 목표가 센다.</summary>
        public int Down;
        /// <summary>이 판에 내보낸 가전 수.</summary>
        public int Used;
        /// <summary>황금 먼지를 잡았다.</summary>
        public bool GoldCaught;
        /// <summary>졌을 때 보여 줄 까닭.</summary>
        public string Why;

        public float GoalTime;      // 지키기: 버텨야 하는 시간
        public float WaveEvery, WaveNext;
        public string WaveSquad = string.Empty;
        public float FleeAt;        // 황금: 이때 도망간다
        public float BreedEvery, BreedNext;
        public float BossEvery, BossNext;
    }

    public sealed partial class LeagueSim
    {
        /// <summary>타워로 도는 판이면 그 상태. 리그면 null.</summary>
        public TowerRun Tower { get; private set; }
        public bool IsTower => Tower != null;

        /// <summary>물바닥 자리. 원본이 코드에 적어 둔 세 군데다.</summary>
        static readonly (float X, float Y, float R)[] Pools =
            { (1.5f, 2.75f, 0.85f), (5.5f, 2.75f, 0.85f), (3.5f, 3.95f, 0.8f) };

        /// <summary>화면이 웅덩이를 같은 자리에 그릴 수 있도록 내보낸다.</summary>
        public static IReadOnlyList<(float X, float Y, float R)> WaterPools => Pools;

        /// <summary>자동 배치 순서 — 앞줄·가운데·뒷줄, 각 줄은 가운데부터.</summary>
        static readonly int[] Front = { 3, 2, 4, 1, 5, 0, 6 };
        static readonly int[] Mid = { 10, 9, 11, 8, 12, 7, 13 };
        static readonly int[] Back = { 17, 16, 18, 15, 19, 14, 20 };

        /// <summary>지키기 층에서 공기청정기가 서는 칸.</summary>
        const int PurifierCell = 17;

        // ------------------------------------------------------------------ 한 층 열기

        /// <summary>
        /// 층 하나를 연다. 지난 편성을 되살리고, 모자라면 성장이 높은 순으로 채운다.
        /// </summary>
        public LeagueSim(GameDatabase db, PlayerProfile profile, TowerFloorRow floor, IRandom rng,
                         bool autoFill = true)
            : this(db, profile, 0, rng, towerFloor: floor)
        {
            if (autoFill) TowerRestoreFormation();
        }

        void TowerInit(TowerFloorRow floor)
        {
            var rule = !string.IsNullOrEmpty(floor.rule)
                       && _db.TowerRules.TryGetValue(floor.rule, out var r) ? r : null;

            Tower = new TowerRun
            {
                Floor = floor.floor,
                Row = floor,
                Rule = rule,
                RuleId = floor.rule ?? string.Empty,
            };

            // 손님 규칙: 아직 없는 가전을 이 층에서만 빌려 준다.
            if (Tower.RuleId == "guest" && !string.IsNullOrEmpty(floor.extra)
                && _db.LeagueUnits.ContainsKey(floor.extra)
                && _profile.Data.Own(floor.extra) == null)
            {
                Tower.Guest = floor.extra;
                Tower.GuestPower = TowerGuestPower();
            }

            Life = 1;
            Gold = 0;
            Level = TowerSlots();       // 타워는 레벨이 곧 출전 수다
            Round = floor.floor;
        }

        /// <summary>손님의 성장 = 보유 가전 성장 상위 여섯의 평균 × config.towerGuestPow.</summary>
        float TowerGuestPower()
        {
            var powers = _db.UnitOrder
                .Where(t => _db.LeagueUnits.ContainsKey(t) && _profile.Data.Own(t) != null)
                .Select(t => _profile.PowerOf(t))
                .OrderByDescending(v => v)
                .Take(6)
                .ToList();
            if (powers.Count == 0) return 1f;
            return Math.Max(1f, powers.Sum() / powers.Count * Math.Max(0.0001f, _cfg.towerGuestPow));
        }

        /// <summary>이 층에 내보낼 수 있는 가전 — 보유분과(있으면) 손님.</summary>
        public List<string> TowerRoster()
        {
            var list = _db.UnitOrder
                .Where(t => _profile.Data.Own(t) != null && _db.LeagueUnits.ContainsKey(t))
                .ToList();
            if (Tower.Guest != null) list.Insert(0, Tower.Guest);
            return list;
        }

        /// <summary>출전 수. '단독' 규칙은 층의 칸 수 대신 규칙 값을 쓴다.</summary>
        int TowerSlots()
            => Tower.RuleId == "solo" && Tower.Rule != null
                ? (int)Tower.Rule.p1 : Math.Max(1, Tower.Row.slots);

        /// <summary>놓을 수 있는 칸인지. 좁은 복도는 가운데 열만, 지키기는 청정기 자리를 비운다.</summary>
        public bool TowerCellOk(int cell)
        {
            if (Tower == null) return true;
            int col = cell % Cols;
            if (Tower.RuleId == "narrow" && Tower.Rule != null
                && (col < Tower.Rule.p1 || col > Tower.Rule.p2)) return false;
            if (Tower.RuleId == "guard" && cell == PurifierCell) return false;
            return true;
        }

        /// <summary>근접은 앞줄부터, 원거리는 뒷줄부터 빈 칸을 찾는다.</summary>
        public int TowerAutoCell(string unitId)
        {
            bool melee = _db.Units.TryGetValue(unitId, out var u) && u.Tile == TileKind.Ground;
            var order = melee
                ? Front.Concat(Mid).Concat(Back)
                : Back.Concat(Mid).Concat(Front);
            foreach (var i in order)
                if (Board[i] == null && TowerCellOk(i)) return i;
            return -1;
        }

        /// <summary>지난 편성을 되살리고 모자라면 채운다.</summary>
        void TowerRestoreFormation()
        {
            var roster = TowerRoster();
            foreach (var (cell, type) in _profile.TowerFormation())
            {
                if (OnBoard >= Level) break;
                if (!roster.Contains(type)) continue;
                if (Board.Any(p => p != null && p.Type == type)) continue;
                if (cell < 0 || cell >= Board.Length || Board[cell] != null) continue;
                if (!TowerCellOk(cell)) continue;
                Board[cell] = new LeaguePiece { Type = type, Star = 1, Uid = ++_uid };
            }

            // 손님은 반드시 자리를 얻는다 — 자리가 차 있으면 성장이 가장 낮은 가전을 뺀다.
            if (Tower.Guest != null && !Board.Any(p => p != null && p.Type == Tower.Guest))
            {
                if (OnBoard >= Level)
                {
                    int worst = -1;
                    float worstScore = float.MaxValue;
                    for (int i = 0; i < Board.Length; i++)
                    {
                        if (Board[i] == null) continue;
                        float score = _profile.PowerOf(Board[i].Type)
                                    + (_db.Units[Board[i].Type].Tile == TileKind.Ground ? 0.01f : 0f);
                        if (score < worstScore) { worstScore = score; worst = i; }
                    }
                    if (worst >= 0) Board[worst] = null;
                }
                int c = TowerAutoCell(Tower.Guest);
                if (c >= 0) Board[c] = new LeaguePiece { Type = Tower.Guest, Star = 1, Uid = ++_uid };
            }

            if (OnBoard < Level) TowerAutoFill();
        }

        /// <summary>빈자리를 성장이 높은 순으로 채우되 근접을 둘까지 섞는다.</summary>
        public void TowerAutoFill()
        {
            var roster = TowerRoster()
                .OrderByDescending(t => t == Tower.Guest)
                .ThenByDescending(t => TowerFavoured(t))
                .ThenByDescending(t => _profile.PowerOf(t))
                .ToList();

            var picked = Board.Where(p => p != null).Select(p => p.Type).ToList();
            int had = picked.Count;

            int meleeNow = picked.Count(t => _db.Units[t].Tile == TileKind.Ground);
            int needMelee = Math.Max(0, Math.Min(2, Level) - meleeNow);
            foreach (var t in roster.Where(t => _db.Units[t].Tile == TileKind.Ground
                                                && !picked.Contains(t)).Take(needMelee))
                if (picked.Count < Level) picked.Add(t);

            foreach (var t in roster)
                if (picked.Count < Level && !picked.Contains(t)) picked.Add(t);

            foreach (var t in picked.Skip(had))
            {
                int c = TowerAutoCell(t);
                if (c >= 0) Board[c] = new LeaguePiece { Type = t, Star = 1, Uid = ++_uid };
            }
        }

        /// <summary>이 층의 규칙이 밀어 주는 가전인지 — 열기 층의 계절 가전, 정전 층의 발전기.</summary>
        bool TowerFavoured(string unitId)
        {
            if (Tower.RuleId == "heat") return _db.Units[unitId].Family == 4;
            if (Tower.RuleId == "blackout") return unitId == "gen";
            return unitId == Tower.Guest;
        }

        /// <summary>가전을 넣고 뺀다. 'cap' 은 출전 수가 찬 것이다.</summary>
        public string TowerToggle(string unitId)
        {
            if (Tower == null || Phase != LeaguePhase.Prep) return "no";

            int at = Array.FindIndex(Board, p => p != null && p.Type == unitId);
            if (at >= 0) { Board[at] = null; return "out"; }
            if (OnBoard >= Level) return "cap";

            int c = TowerAutoCell(unitId);
            if (c < 0) return "full";
            Board[c] = new LeaguePiece { Type = unitId, Star = 1, Uid = ++_uid };
            return "in";
        }

        /// <summary>판 안에서 자리를 바꾼다.</summary>
        public bool TowerMove(int from, int to)
        {
            if (Tower == null || Phase != LeaguePhase.Prep || !TowerCellOk(to)) return false;
            (Board[from], Board[to]) = (Board[to], Board[from]);
            return true;
        }

        // ------------------------------------------------------------------ 능력치

        string TowerSquad() => Tower.Row.squad;

        /// <summary>
        /// 타워의 가전 수치. 리그와 달리 **계정 성장이 상한 없이** 들어가고 장비 공격력도 붙는다.
        /// 열기 층은 계절 가전을 올리고 나머지를 늦추며, 단독 층은 체력을 배로 준다.
        /// </summary>
        (float Hp, float Atk, float Itv, float Range, float Spd, float Mana,
         float StarMul, float Growth, float ExclMul) TowerUnitStats(LeaguePiece p, LeagueUnitRow b)
        {
            bool guest = p.Type == Tower.Guest;
            float pow = guest ? Tower.GuestPower : _profile.PowerOf(p.Type);
            float gearAtk = _profile.Data.Own(p.Type) != null ? _profile.ModsFor(p.Type).Atk : 0f;

            var excl = _profile.ExclusiveState(p.Type);
            float exm = excl != null
                ? 1f + _cfg.leagueExclSkill * (1f + _cfg.exclStarBonus * excl.star)
                : 1f;

            float hp = b.hp * pow, atk = b.atk * pow * (1f + gearAtk), itv = b.itv;

            if (Tower.RuleId == "heat" && Tower.Rule != null)
            {
                if (_db.Units[p.Type].Family == 4) atk *= 1f + Tower.Rule.p1;
                else itv *= 1f + Tower.Rule.p2;
            }
            if (Tower.RuleId == "solo" && Tower.Rule != null) hp *= Tower.Rule.p2;

            return (hp, atk, itv, b.range, b.spd, b.mana, pow, pow - 1f, exm);
        }

        // ------------------------------------------------------------------ 전투 준비

        /// <summary>층 규칙이 전투를 시작할 때 거는 것들.</summary>
        void TowerSetupBattle()
        {
            var rule = Tower.Rule;
            Tower.Down = 0;
            Tower.Used = OnBoard;
            Tower.GoldCaught = false;
            Tower.Why = null;
            Tower.Purifier = null;
            Tower.Gold = null;
            Tower.WaterMove = Tower.WaterAtk = 0f;
            _limit = _cfg.towerTime;

            switch (Tower.RuleId)
            {
                case "blackout":
                    // 발전기를 데려오면 잠금이 짧아진다.
                    _manaLock = Board.Any(p => p != null && p.Type == "gen") ? rule.p2 : rule.p1;
                    break;

                case "water":
                    Tower.WaterMove = rule.p1;
                    Tower.WaterAtk = rule.p2;
                    break;

                case "guard":
                {
                    float hp = _cfg.towerPurHp * Tower.Row.atkMul;
                    Tower.Purifier = new LeagueUnit
                    {
                        Id = ++_battleUid, Side = 0, Type = "purifier",
                        X = 3.5f, Y = 5.45f, Hp = hp, MaxHp = hp,
                        Interval = 99f, AtkTimer = 99f, Radius = 0.35f, Immobile = true,
                    };
                    Units.Add(Tower.Purifier);
                    Tower.GoalTime = rule.p1;
                    _limit = rule.p1 + 1f;
                    Tower.WaveEvery = Tower.WaveNext = rule.p2;
                    Tower.WaveSquad = Tower.Row.extra ?? string.Empty;
                    break;
                }

                case "golden":
                {
                    SpawnEnemy("dust", 3.5f, 0.45f, null);
                    var g = Units[^1];
                    g.Hp = g.MaxHp = g.Hp * rule.p2;
                    g.Atk = 0f;
                    g.Speed = 1.6f;
                    g.Flees = true;
                    g.Seed = _rng.Value * 10f;
                    Tower.Gold = g;
                    Tower.FleeAt = rule.p1;
                    break;
                }

                case "breed":
                    Tower.BreedEvery = Tower.BreedNext = rule.p1;
                    break;

                case "mirror":
                {
                    // 앞 칸부터 p1 대를 위쪽에 베껴 세운다. 능력은 p2 배, 스킬은 없다.
                    var mine = Units.Where(u => u.Side == 0 && !u.Immobile)
                                    .OrderBy(u => u.Cell).Take((int)rule.p1).ToList();
                    foreach (var a in mine)
                        Units.Add(new LeagueUnit
                        {
                            Id = ++_battleUid, Side = 1, Type = a.Type, Mirror = true,
                            X = a.X, Y = Rows - a.Y,
                            Hp = a.MaxHp * rule.p2, MaxHp = a.MaxHp * rule.p2,
                            Atk = a.Atk * rule.p2, Interval = a.Interval,
                            Range = a.Range, Speed = a.Speed,
                            MaxMana = float.MaxValue, Radius = 0.3f, Face = -1,
                            AtkTimer = 0.4f + _rng.Value * 0.4f, Seed = _rng.Value * 10f,
                        });
                    break;
                }

                case "boss":
                    Tower.BossEvery = Tower.BossNext = _cfg.towerBossEvery;
                    break;
            }
        }

        // ------------------------------------------------------------------ 전투 진행

        void TowerTick(float dt)
        {
            switch (Tower.RuleId)
            {
                case "guard":
                    if (BattleTime >= Tower.WaveNext && BattleTime < Tower.GoalTime - 3f)
                    {
                        Tower.WaveNext += Tower.WaveEvery;
                        TowerSpawnTop(Tower.WaveSquad);
                    }
                    break;

                case "golden":
                    if (Tower.Gold != null && Tower.Gold.Alive && BattleTime >= Tower.FleeAt)
                    {
                        Tower.Gold.Alive = false;
                        Tower.Gold.Fled = true;
                    }
                    break;

                case "breed":
                    if (BattleTime >= Tower.BreedNext)
                    {
                        Tower.BreedNext += Tower.BreedEvery;
                        var molds = Units.Where(u => u.Alive && u.Side == 1
                                                     && (u.Type == "mold" || u.Type == "boss_mold")).ToList();
                        int n = Units.Count(u => u.Alive && u.Side == 1);
                        foreach (var m in molds)
                        {
                            if (n >= 16) break;
                            SpawnEnemy("spore",
                                       Mathf2.Clamp(m.X + (_rng.Value - 0.5f) * 0.9f, 0.3f, 6.7f),
                                       Math.Max(0.3f, m.Y - 0.35f), null);
                            n++;
                        }
                    }
                    break;

                case "boss":
                    if (BattleTime >= Tower.BossNext)
                    {
                        Tower.BossNext += Tower.BossEvery;
                        var boss = Units.FirstOrDefault(u => u.Alive && u.Boss && u.Side == 1);
                        if (boss != null) TowerBossAct(boss);
                    }
                    break;
            }
        }

        /// <summary>윗줄에 한 줄 더 들여보낸다 — 지키기 층의 추가 웨이브.</summary>
        void TowerSpawnTop(string squad)
        {
            var ids = new List<string>();
            foreach (var (id, n) in ParseSquad(squad))
                for (int i = 0; i < n; i++) ids.Add(id);

            for (int i = 0; i < ids.Count; i++)
            {
                float x = ids.Count == 1 ? 3.5f : 0.6f + i * (5.8f / Math.Max(1, ids.Count - 1));
                SpawnEnemy(ids[i], Math.Min(6.5f, x), 0.4f, null);
            }
        }

        /// <summary>보스마다 다른 한 수. 원본 `twBossAct` 그대로다.</summary>
        void TowerBossAct(LeagueUnit boss)
        {
            switch (boss.Type)
            {
                case "boss_dust":
                    foreach (var o in Near(boss.X, boss.Y, 2.6f, 0).ToList())
                        Hit(boss, o, boss.Atk * 1.4f);
                    break;

                case "boss_mold":
                    foreach (float dx in new[] { -0.7f, 0.7f })
                        SpawnEnemy("spore", Mathf2.Clamp(boss.X + dx, 0.3f, 6.7f),
                                   Math.Min(2.6f, boss.Y + 0.3f), null);
                    break;

                case "boss_grease":
                {
                    var tg = Units.Where(u => u.Alive && u.Side == 0)
                                  .OrderByDescending(u => u.Hp).FirstOrDefault();
                    if (tg != null)
                        _zones.Add(new Zone
                        {
                            X = tg.X, Y = tg.Y, R = 0.95f, Dps = boss.Atk * 0.7f,
                            T = 4f, Src = boss, Side = 0, Slow = 0.45f,
                        });
                    break;
                }
            }
        }

        /// <summary>황금 먼지는 싸우지 않고 가전에게서 달아난다.</summary>
        void TowerFlee(LeagueUnit u, float dt)
        {
            LeagueUnit near = null;
            float best = float.MaxValue;
            foreach (var o in Units)
            {
                if (!o.Alive || o.Side != 0 || o.Immobile) continue;
                float d = Dist(u, o);
                if (d < best) { best = d; near = o; }
            }

            float sp = u.Speed * (u.SlowT > 0f ? 1f - u.SlowAmt : 1f) * dt;
            float dx = (float)Math.Sin(BattleTime * 1.7f + u.Seed) * 0.6f;
            float dy = (float)Math.Cos(BattleTime * 1.3f + u.Seed) * 0.4f - 0.2f;
            if (near != null && best < 3f)
            {
                dx += (u.X - near.X) / best * 1.5f;
                dy += (u.Y - near.Y) / best * 1.5f;
            }
            float len = Mathf2.Hypot(dx, dy);
            if (len <= 0f) len = 1f;
            u.X += dx / len * sp;
            u.Y = Mathf2.Clamp(u.Y + dy / len * sp, 0.7f, 2.7f);   // 먼지 쪽 가운데에서만
            u.Face = dx < 0f ? -1 : 1;
        }

        bool TowerWet(LeagueUnit u)
            => Tower.WaterMove > 0f
               && Pools.Any(p => Mathf2.Hypot(u.X - p.X, u.Y - p.Y) < p.R);

        void TowerOnDeath(LeagueUnit u)
        {
            if (u.Side == 0 && !u.Immobile) Tower.Down++;
            if (u.Flees && !u.Fled) Tower.GoldCaught = true;
        }

        /// <summary>지키기 층만 끝내는 조건이 다르다. 처리했으면 true.</summary>
        bool TowerSettle(bool oursAlive)
        {
            if (Tower.RuleId != "guard") return false;

            if (Tower.Purifier != null && !Tower.Purifier.Alive)
            {
                BattleOver = true; BattleWin = false;
                Tower.Why = "공기청정기가 꺼졌어요";
            }
            else if (BattleTime >= Tower.GoalTime) { BattleOver = true; BattleWin = true; }
            else if (!oursAlive) { BattleOver = true; BattleWin = false; }
            return true;
        }

        /// <summary>졌을 때 보여 줄 까닭.</summary>
        public string TowerWhy()
            => Tower?.Why
               ?? (BattleTimeout ? $"시간 초과({_limit:0}초)" : "가전이 모두 쓰러졌어요");

        // ------------------------------------------------------------------ 보상

        /// <summary>보너스 목표를 채웠는지.</summary>
        public bool TowerBonusOk()
        {
            if (Tower == null || BattleWin != true) return false;
            int n = Tower.Row.bonusN;
            return Tower.Row.bonus switch
            {
                "noDown" => Tower.Down == 0,
                "fast" => BattleTime <= n,
                "few" => Tower.Used <= n,
                "gold" => Tower.GoldCaught,
                "pur" => Tower.Purifier != null
                         && Tower.Purifier.Hp / Tower.Purifier.MaxHp * 100f >= n,
                _ => false,
            };
        }

        /// <summary>
        /// 층을 깼을 때의 보상. 첫 클리어 한 번, 보너스 목표 한 번 — 다시 깨도 또 주지 않는다.
        /// </summary>
        public TowerRewardResult TowerReward()
        {
            var result = new TowerRewardResult();
            if (Tower == null || BattleWin != true) return result;

            result.BonusOk = TowerBonusOk();
            result.First = _profile.ClaimTowerFloor(Tower.Row,
                                                    Board.Where(p => p != null).Take(3)
                                                         .Select(p => p.Type).ToList(),
                                                    result.Got);
            result.Bonus = _profile.ClaimTowerBonus(Tower.Row, result.BonusOk, result.Got);
            return result;
        }
    }

    /// <summary>한 층을 깬 뒤 실제로 받은 것.</summary>
    public sealed class TowerRewardResult
    {
        /// <summary>이번에 처음 깼다.</summary>
        public bool First;
        /// <summary>보너스 목표를 이번에 처음 채웠다.</summary>
        public bool Bonus;
        /// <summary>목표 자체를 채웠는지(이미 받았더라도 true).</summary>
        public bool BonusOk;
        public List<string> Got = new();
    }
}
