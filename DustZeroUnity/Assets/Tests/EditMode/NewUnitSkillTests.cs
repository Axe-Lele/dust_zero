// 10.21-f 신규 가전 4종의 전투 동작, 그리고 그것들이 기대는 방어·기절.
//
// 방어는 표에 처음부터 있었지만 EnemyDef 로 옮겨지지 않아 **전투에서 통째로 무시되고
// 있었다**(비늘먼지 0.4). 스마트TV 의 광고 시간이 깎을 대상이 바로 그 방어라, 이 넷을
// 넣으면서 같이 살렸다. 그래서 방어 검사도 여기 둔다.
using System.Linq;
using DustZero.Core;
using DustZero.Core.Defense;
using DustZero.Data;
using NUnit.Framework;

namespace DustZero.Tests
{
    public class NewUnitSkillTests
    {
        GameDatabase _db;
        StageDef _stage;

        [SetUp]
        public void SetUp()
        {
            _db = TestData.Db;
            _stage = _db.Stages[0];
        }

        BattleSim NewSim(params string[] deck)
            => new(_db, _stage, deck, new FlatPower(), new FixedRandom(0.5f), new PresentationBus());

        /// <summary>
        /// 그 판이 스스로 내보내는 적 중 원하는 종류 하나를 받아 가전 옆에 세운다.
        ///
        /// `AppendSpawn` 으로 넣으면 안 된다 — 일정은 **순서대로** 소비되므로 뒤에 붙인
        /// 0초짜리는 앞선 일정이 다 끝나기 전에는 영영 나오지 않는다(협동이 웨이브를
        /// 이어 붙이는 용도라 그렇게 되어 있다). 그래서 판이 내보낼 때까지 기다린다.
        /// </summary>
        static BattleEnemy Summon(BattleSim sim, string enemyId, BattleUnit u, float dx = 0.3f)
        {
            BattleEnemy e = null;
            for (int i = 0; i < 6000 && e == null; i++)
            {
                e = sim.Enemies.FirstOrDefault(x => x.Type == enemyId && !x.Dead && x.Hp > 0f);
                if (e == null) sim.Tick(0.05f);
            }
            Assert.IsNotNull(e, $"이 판에서 '{enemyId}' 가 나오지 않았습니다.");
            Assert.IsFalse(sim.Finished, "적을 기다리는 동안 판이 끝났습니다 — 이후 Tick 은 아무 일도 하지 않습니다.");

            e.X = u.X + dx; e.Y = u.Y;
            e.Cx = u.Col; e.Cy = u.Row;
            e.TargetSet = false;
            e.Hp = e.MaxHp;
            return e;
        }

        /// <summary>
        /// 고른 적만 남기고 나머지는 멀리 치워 얼려 둔다. 같은 판의 다른 적이 사거리에
        /// 들어오면 가전이 누구를 때렸는지 알 수 없어, 피해 비교가 뜻을 잃는다.
        /// </summary>
        /// <summary>같은 종류 적이 둘 살아 있을 때까지 기다렸다가 둘째를 돌려준다.</summary>
        static BattleEnemy SummonSecond(BattleSim sim, string enemyId, BattleUnit u, float dx,
                                        BattleEnemy keepAlive = null)
        {
            // 둘째를 기다리는 동안 가전이 첫 적을 계속 때려 죽여 버린다. 잠깐 치워 둔다 —
            // 이걸 안 해서 "첫 적을 때리지 않았습니다" 가 떴다(죽은 적은 목록에서 빠진다).
            if (keepAlive != null) { keepAlive.X = 50f; keepAlive.Y = 50f; keepAlive.FrozenT = 9999f; }
            BattleEnemy e = null;
            for (int i = 0; i < 6000 && e == null; i++)
            {
                e = sim.Enemies.LastOrDefault(x => x.Type == enemyId && !x.Dead && x.Hp > 0f &&
                                                   Mathf2.Hypot(x.X - u.X, x.Y - u.Y) > 1f);
                if (e == null) sim.Tick(0.05f);
            }
            Assert.IsNotNull(e, $"'{enemyId}' 가 둘 이상 나오지 않았습니다.");
            Assert.IsFalse(sim.Finished, "둘째 적을 기다리는 동안 판이 끝났습니다.");
            if (keepAlive != null)
            {
                Assert.IsFalse(keepAlive.Dead, "치워 둔 첫 적이 죽었습니다.");
                keepAlive.FrozenT = 0f;
            }
            e.X = u.X + dx; e.Y = u.Y;
            e.Cx = u.Col; e.Cy = u.Row;
            e.Hp = e.MaxHp;
            return e;
        }

        static void Isolate(BattleSim sim, params BattleEnemy[] keep)
        {
            foreach (var e in sim.Enemies)
            {
                if (keep.Contains(e)) continue;
                e.X = 100f; e.Y = 100f;
                e.Cx = 0; e.Cy = 0;
                e.FrozenT = 9999f;
            }
        }

        static BattleUnit Place(BattleSim sim, string type)
        {
            for (int r = 0; r < sim.Field.Rows; r++)
                for (int c = 0; c < sim.Field.Cols; c++)
                    if (sim.CanPlace(type, c, r)) return sim.Deploy(type, c, r);
            Assert.Fail($"{type} 를 놓을 칸이 없습니다.");
            return null;
        }

        // ------------------------------------------------------------------ 방어

        /// <summary>
        /// enemy.json 의 armor 가 EnemyDef 까지 와야 한다. 예전에는 DTO 에서 멈춰 있었고,
        /// 그래서 방어가 있는 적 6종이 전투에서 맨몸으로 맞고 있었다.
        /// </summary>
        [Test]
        public void EnemyArmorReachesTheSimulation()
        {
            var armoured = _db.Enemies.Values.Where(e => e.Armor > 0f).ToList();
            Assert.IsNotEmpty(armoured, "방어를 가진 적이 하나도 없습니다 — 표에서 넘어오지 않았습니다.");
            Assert.Greater(_db.Enemy("scale").Armor, 0f, "비늘먼지의 방어가 0 입니다.");
        }

        /// <summary>원본 `dmgE` 와 같은 셈: 받는 피해 = 피해 × (1 − 방어 × (1 − 관통)).</summary>
        [Test]
        public void ArmorReducesDamageAndPierceIgnoresIt()
        {
            var sim = NewSim("vac");
            var u = Place(sim, "vac");
            var e = Summon(sim, "dust", u);
            // 1-1 에 비늘먼지가 없을 수 있으니 방어는 표에서 직접 읽어 적에게 심는다.
            float armor = _db.Enemy("scale").Armor;
            e.Def.Armor = armor;

            Assert.AreEqual(100f * (1f - armor), sim.AfterArmor(e, 100f, 0f), 0.01f,
                "방어가 피해를 깎지 않습니다.");
            Assert.AreEqual(100f, sim.AfterArmor(e, 100f, 1f), 0.01f,
                "관통 1 인데도 방어가 적용됐습니다.");
        }

        // ------------------------------------------------------------------ 믹서기

        /// <summary>
        /// 믹서기는 체력이 execHp 아래인 적에게 execBonus 만큼 더 아프다. 같은 적을
        /// 체력만 바꿔 두 번 때려 비교한다 — 다른 조건은 모두 같다.
        /// </summary>
        [Test]
        public void BlenderFinishesWoundedTargetsHarder()
        {
            float execHp = _db.Skp("blender", "execHp");
            float bonus = _db.Skp("blender", "execBonus");
            Assert.Greater(bonus, 0f, "믹서기 마무리 보너스가 표에 없습니다.");

            float Hit(float hpFraction)
            {
                var sim = NewSim("blender");
                var u = Place(sim, "blender");
                var e = Summon(sim, "dust", u);
                Isolate(sim, e);
                e.Hp = e.MaxHp * hpFraction;
                e.BlockedBy = u;
                float before = e.Hp;
                for (int i = 0; i < 200 && e.Hp == before; i++) sim.Tick(0.05f);
                return before - e.Hp;
            }

            float healthy = Hit(1f);
            float wounded = Hit(execHp * 0.5f);
            Assert.Greater(healthy, 0f, "믹서기가 때리지 않았습니다.");
            Assert.Greater(wounded, healthy * 1.01f,
                $"체력이 {execHp:P0} 아래인 적에게 마무리 피해가 붙지 않았습니다.");
        }

        // ------------------------------------------------------------------ 공기청정기

        /// <summary>공중에는 airMul 배로 들어간다.</summary>
        [Test]
        public void PurifierHitsFlyersHarder()
        {
            float airMul = _db.Skp("purifier", "airMul");
            Assert.Greater(airMul, 1f, "공기청정기의 공중 배율이 표에 없습니다.");

            float Hit(string enemyId)
            {
                var sim = NewSim("purifier");
                var u = Place(sim, "purifier");
                var e = Summon(sim, enemyId, u);
                Isolate(sim, e);
                float before = e.Hp;
                for (int i = 0; i < 300 && e.Hp == before; i++) sim.Tick(0.05f);
                return before - e.Hp;
            }

            var fly = _db.Enemy("fly");
            Assert.IsTrue(fly.Fly, "초파리가 공중이 아닙니다 — 픽스처가 틀렸습니다.");

            float onGround = Hit("dust");
            float onAir = Hit("fly");
            Assert.Greater(onGround, 0f, "공기청정기가 지상 적을 때리지 않았습니다.");
            // 방어가 서로 달라 배수까지 같을 수는 없다. 더 아프다는 것만 본다.
            Assert.Greater(onAir, onGround, "공중 적이 더 아프게 맞지 않습니다.");
        }

        // ------------------------------------------------------------------ 공유기

        /// <summary>
        /// 평타가 옆의 적에게 **튄다**. 한 번의 공격으로 둘이 함께 줄어드는지로 본다.
        ///
        /// 여기서 더 들어가지 못한 것(감쇠 배율·연쇄 거리 밖은 안 맞는다)은 이 하네스의
        /// 한계다. 적을 가전 옆에 손으로 놓아도 **다음 틱에 길 진행도로 자리가 다시
        /// 계산되어** 밀려난다. 그래서 '한 틱 = 한 번의 공격' 으로 가둘 수 없고, 여러 틱을
        /// 돌리면 두 번 때려 둘 다 만피해가 나온다(실제로 그렇게 나왔다). 진행도(D)를
        /// 직접 세울 수 있게 되면 그때 감쇠까지 잠근다. 핸드오프에 적어 두었다.
        /// </summary>
        [Test]
        public void RouterChainsToANeighbour()
        {
            var sim = NewSim("router");
            var u = Place(sim, "router");
            var first = Summon(sim, "dust", u, 0.3f);
            var second = SummonSecond(sim, "dust", u, 0.6f, first);
            first.X = u.X + 0.3f; first.Y = u.Y; first.Cx = u.Col; first.Cy = u.Row;
            first.Hp = first.MaxHp;
            Isolate(sim, first, second);

            float h1 = first.Hp, h2 = second.Hp;
            for (int i = 0; i < 300 && (first.Hp == h1 || second.Hp == h2); i++) sim.Tick(0.05f);

            Assert.Less(first.Hp, h1, "공유기가 첫 적을 때리지 않았습니다.");
            Assert.Less(second.Hp, h2, "번개가 옆 적으로 튀지 않았습니다 — 연쇄가 동작하지 않습니다.");
        }

        // ------------------------------------------------------------------ 스마트TV

        /// <summary>평타가 맞은 자리 둘레(splashRadius)도 함께 때린다.</summary>
        [Test]
        public void TvBeamSplashesAroundItsTarget()
        {
            var sim = NewSim("tv");
            var u = Place(sim, "tv");
            float r = _db.Skp("tv", "splashRadius");
            Assert.Greater(r, 0f, "스마트TV 의 광역 반경이 표에 없습니다.");

            var a = Summon(sim, "dust", u, 0.3f);
            var b = SummonSecond(sim, "dust", u, 0.3f + r * 0.5f, a);
            a.X = u.X + 0.3f; a.Y = u.Y; a.Cx = u.Col; a.Cy = u.Row;
            a.Hp = a.MaxHp;
            Isolate(sim, a, b);

            float ha = a.Hp, hb = b.Hp;
            for (int i = 0; i < 300 && a.Hp == ha; i++) sim.Tick(0.05f);

            Assert.Less(a.Hp, ha, "스마트TV 가 때리지 않았습니다.");
            Assert.Less(b.Hp, hb, "반경 안의 옆 적이 빔에 맞지 않았습니다.");
        }

        /// <summary>
        /// 광고 시간은 판 위의 **모든** 적을 멈추고 받는 피해를 늘린다. 보스는 절반만
        /// 멈춘다는 규칙이 있어 일반 적으로 본다.
        /// </summary>
        [Test]
        public void TvAdTimeStunsEveryoneAndLowersTheirArmor()
        {
            // 광고는 adMin(6) 마리가 몰려야 켜진다. 1-1 은 튜토리얼 판이라 그만큼 모이지
            // 않는다 — 일정이 가장 긴 판을 골라 쓴다.
            _stage = _db.Stages.OrderByDescending(x => x.Schedule.Length).First();
            var sim = NewSim("tv");
            var u = Place(sim, "tv");

            // 광고는 적이 adMin 만큼 몰렸을 때만 켠다. 그 조건까지 같이 본다 —
            // 억지로 발동시키면 '언제 쓰는가' 가 검사에서 빠진다.
            int adMin = (int)_db.Skp("tv", "adMin");
            // 판마다 나오는 적이 다르다. 종류를 고르지 말고 **먼저 나오는 것**을 쓴다.
            BattleEnemy near = null;
            for (int i = 0; i < 6000 && near == null; i++)
            {
                near = sim.Enemies.FirstOrDefault(x => !x.Dead && x.Hp > 0f);
                if (near == null) sim.Tick(0.05f);
            }
            Assert.IsNotNull(near, "이 판에서 적이 나오지 않았습니다.");
            near.X = u.X + 0.3f; near.Y = u.Y; near.Cx = u.Col; near.Cy = u.Row;
            // 판 위에 adMin 만큼 쌓일 때까지 그대로 둔다 — 억지로 꺼내지 않는다.
            for (int i = 0; i < 6000 && sim.Enemies.Count(x => !x.Dead) < adMin; i++) sim.Tick(0.05f);
            var far = sim.Enemies.FirstOrDefault(x => x != near && !x.Dead);
            Assert.IsNotNull(far, "멀리 둘 적이 없습니다.");
            far.X = u.X + 20f; far.Y = u.Y + 20f;

            float armorBefore = sim.ArmorOf(far);
            // 게이지는 매 틱 깎였다 찼다 하므로 한 번만 채워 두면 놓친다.
            for (int i = 0; i < 400 && far.StunT <= 0f; i++)
            {
                u.Sp = u.SkillChargeTime;
                sim.Tick(0.016f);
            }

            int alive = sim.Enemies.Count(x => !x.Dead);
            // 가까운 적은 빔에 먼저 죽을 수 있다. 사거리 **밖**에서 살아남은 적으로 본다 —
            // 광고가 판 전체에 걸린다는 것이 여기서 증명된다.
            Assert.Greater(far.StunT, 0f,
                $"사거리 밖 적이 멈추지 않았습니다 (판 위 {alive}마리, 필요 {adMin}, 끝났나 {sim.Finished}).");
            Assert.Less(sim.ArmorOf(far), armorBefore, "받는 피해가 늘지 않았습니다(방어가 그대로).");
        }
    }
}
