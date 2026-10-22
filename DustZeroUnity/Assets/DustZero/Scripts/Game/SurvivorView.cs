// Renders one 대청소 run: the leader, companions, enemies and the small pooled dots for
// gems, shots and enemy shots. Reuses BoardView's EntityView prefab and pooling idea, but the
// arena has no tile grid - entities are free-floating, so positions map 1:1 from sim units to
// world units with Y flipped to match the original's canvas-down convention.
using System.Collections.Generic;
using DustZero.Core.Survivor;
using UnityEngine;
using UnityEngine.Pool;

namespace DustZero.Game
{
    public sealed class SurvivorView : MonoBehaviour
    {
        [SerializeField] Transform entityRoot;
        [SerializeField] EntityView entityPrefab;
        [SerializeField] SpriteLibrary sprites;
        [SerializeField] Sprite dotSprite;
        [SerializeField] Sprite ringSprite;

        const float LeaderScale = 1.25f;
        const float CompanionScale = 1.0f;
        const float EnemyScale = 1.1f;

        /// <summary>
        /// 아레나 바닥 한 장. 원본은 매 프레임 캔버스에 판자와 러그를 다시 그리지만
        /// 바닥은 고정이라 구워 둔 그림 한 장이면 된다. <see cref="FloorBakedTiles"/> 칸
        /// 크기로 구워져 있으므로 실제 아레나 크기에 맞춰 늘린다.
        /// </summary>
        [SerializeField] SpriteRenderer floor;

        /// <summary>
        /// 흡입 소용돌이 범위. 원본은 청소기 리더에게만, `L.auraR` 이 있을 때만 그린다 —
        /// 옅은 원판 위에 도는 호 세 개다. 호는 통째로 돌고(원본 `t*4`), 회오리 협동기가
        /// 켜지면 색이 청록으로 바뀐다.
        /// </summary>
        [SerializeField] SpriteRenderer auraFill;
        [SerializeField] SpriteRenderer auraArcs;

        static readonly Color AuraFillPlain = new(0.788f, 0.804f, 0.820f, 0.14f);
        static readonly Color AuraArcPlain = new(0.588f, 0.627f, 0.667f, 0.50f);
        static readonly Color AuraFillCombo = new(0.498f, 0.827f, 0.765f, 0.12f);
        static readonly Color AuraArcCombo = new(0.498f, 0.827f, 0.765f, 0.60f);

        /// <summary>원본 `a = t*4 + i*2.1` — 세 호가 같은 속도로 돈다.</summary>
        const float AuraSpin = 4f;
        float _auraAngle;

        /// <summary>바닥 그림을 구울 때 쓴 칸 수 — `config.survArena` 의 현재 값.</summary>
        public const float FloorBakedTiles = 22f;

        ObjectPool<EntityView> _entityPool;
        EntityView _leaderView;
        readonly Dictionary<SurvCompanion, EntityView> _companions = new();
        readonly Dictionary<SurvEnemy, EntityView> _enemies = new();

        readonly List<SpriteRenderer> _gemDots = new();
        readonly List<SpriteRenderer> _shotDots = new();
        readonly List<SpriteRenderer> _enemyShotDots = new();
        readonly List<SpriteRenderer> _markRings = new();

        void Awake()
        {
            _entityPool = new ObjectPool<EntityView>(
                // 아레나는 전용 레이어로 그린다 — 프리팹을 그대로 쓰면 전투 레이어로
                // 태어나 전투 카메라에도 겹쳐 그려진다.
                createFunc: () =>
                {
                    var v = Instantiate(entityPrefab, entityRoot);
                    SetLayerDeep(v.gameObject, entityRoot.gameObject.layer);
                    return v;
                },
                actionOnGet: v => v.gameObject.SetActive(true),
                actionOnRelease: v => v.gameObject.SetActive(false),
                actionOnDestroy: v => { if (v != null) Destroy(v.gameObject); },
                collectionCheck: false, defaultCapacity: 64, maxSize: 512);
        }

        static void SetLayerDeep(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayerDeep(c.gameObject, layer);
        }

        /// <summary>Arena coordinates (x right, y down, as the original's canvas) to world space.</summary>
        public static Vector3 ToWorld(float x, float y) => new(x, -y, 0f);

        public void Build(float arenaTiles = FloorBakedTiles)
        {
            ClearAll();
            if (floor != null)
            {
                // 그림은 한 칸 = 월드 1 단위로 구워져 있다. 가운데를 아레나 한가운데에 둔다.
                floor.transform.localScale = Vector3.one * (arenaTiles / FloorBakedTiles);
                floor.transform.localPosition = ToWorld(arenaTiles / 2f, arenaTiles / 2f);
            }
            _leaderView = _entityPool.Get();
            _leaderView.transform.localScale = Vector3.one * LeaderScale;
            // The HUD carries the leader's HP bar; the floating one is for battle units only.
            if (_leaderView.BarRoot != null) _leaderView.BarRoot.gameObject.SetActive(false);
        }

        public void ClearAll()
        {
            if (_leaderView != null) { _entityPool.Release(_leaderView); _leaderView = null; }
            foreach (var v in _companions.Values) _entityPool.Release(v);
            _companions.Clear();
            foreach (var v in _enemies.Values) _entityPool.Release(v);
            _enemies.Clear();
            foreach (var d in _gemDots) Destroy(d.gameObject);
            _gemDots.Clear();
            foreach (var d in _shotDots) Destroy(d.gameObject);
            _shotDots.Clear();
            foreach (var d in _enemyShotDots) Destroy(d.gameObject);
            _enemyShotDots.Clear();
            foreach (var d in _markRings) Destroy(d.gameObject);
            _markRings.Clear();
        }

        public void Sync(SurvivorSim sim)
        {
            SyncLeader(sim.Leader);
            SyncAura(sim);
            SyncCompanions(sim.Companions);
            SyncEnemies(sim.Enemies);
            SyncDots(_gemDots, sim.Gems.Count, new Color(0.56f, 0.86f, 0.75f));
            for (int i = 0; i < sim.Gems.Count; i++)
            {
                var g = sim.Gems[i];
                _gemDots[i].transform.localPosition = ToWorld(g.X, g.Y);
                _gemDots[i].transform.localScale = Vector3.one * (g.Big ? 0.28f : 0.18f);
                _gemDots[i].color = g.Big ? new Color(0.95f, 0.76f, 0.31f) : new Color(0.56f, 0.86f, 0.75f);
            }

            SyncDots(_shotDots, sim.Shots.Count, Color.white);
            for (int i = 0; i < sim.Shots.Count; i++)
            {
                var s = sim.Shots[i];
                _shotDots[i].transform.localPosition = ToWorld(s.X, s.Y);
                _shotDots[i].transform.localScale = Vector3.one * (s.Radius * 2f);
                _shotDots[i].color = ParseColor(s.Color, Color.white);
            }

            SyncDots(_enemyShotDots, sim.EnemyShots.Count, new Color(0.61f, 0.75f, 0.42f));
            for (int i = 0; i < sim.EnemyShots.Count; i++)
            {
                var s = sim.EnemyShots[i];
                _enemyShotDots[i].transform.localPosition = ToWorld(s.X, s.Y);
                _enemyShotDots[i].transform.localScale = Vector3.one * (s.Radius * 2f);
            }

            SyncMarks(sim.Marks);
        }

        void SyncLeader(SurvLeader leader)
        {
            if (_leaderView == null) return;
            _leaderView.Body.sprite = sprites != null ? sprites.Unit(leader.Type) : null;
            _leaderView.transform.localPosition = ToWorld(leader.X, leader.Y);
            _leaderView.Body.flipX = leader.Face < 0;
            _leaderView.Body.sortingOrder = 500;
            var tint = leader.HurtT > 0f ? new Color(1f, 0.55f, 0.5f) : Color.white;
            _leaderView.Tint(tint);
            _leaderView.StepFlash(Time.unscaledDeltaTime);
        }

        /// <summary>
        /// 원본 `if (L.type==='vac' && L.auraR)` — 흡입 범위가 있을 때만 보인다.
        /// 반지름은 시뮬레이션이 알려 주는 값(레벨·협동기 반영)을 그대로 쓴다.
        /// </summary>
        void SyncAura(SurvivorSim sim)
        {
            float r = sim.Leader != null ? sim.Leader.AuraRadius : 0f;
            bool on = r > 0f;
            if (auraFill != null) auraFill.gameObject.SetActive(on);
            if (auraArcs != null) auraArcs.gameObject.SetActive(on);
            if (!on) return;

            bool combo = sim.Combos != null && sim.Combos.ContainsKey("tornado");
            var at = ToWorld(sim.Leader.X, sim.Leader.Y);
            var scale = Vector3.one * (r * 2f);

            if (auraFill != null)
            {
                auraFill.transform.localPosition = at;
                auraFill.transform.localScale = scale;
                auraFill.color = combo ? AuraFillCombo : AuraFillPlain;
            }
            if (auraArcs != null)
            {
                _auraAngle = Mathf.Repeat(_auraAngle + AuraSpin * Time.unscaledDeltaTime,
                                          Mathf.PI * 2f);
                auraArcs.transform.localPosition = at;
                auraArcs.transform.localScale = scale;
                // 원본은 캔버스 각도(시계 방향)로 돈다. 월드는 Y 가 뒤집혀 있어 부호가 반대다.
                auraArcs.transform.localRotation =
                    Quaternion.Euler(0f, 0f, -_auraAngle * Mathf.Rad2Deg);
                auraArcs.color = combo ? AuraArcCombo : AuraArcPlain;
            }
        }

        void SyncCompanions(IReadOnlyList<SurvCompanion> comps)
        {
            var stale = new List<SurvCompanion>();
            foreach (var kv in _companions) if (!Contains(comps, kv.Key)) stale.Add(kv.Key);
            foreach (var c in stale) { _entityPool.Release(_companions[c]); _companions.Remove(c); }

            for (int i = 0; i < comps.Count; i++)
            {
                var c = comps[i];
                if (!_companions.TryGetValue(c, out var v))
                {
                    v = _entityPool.Get();
                    v.transform.localScale = Vector3.one * CompanionScale;
                    if (v.BarRoot != null) v.BarRoot.gameObject.SetActive(false);
                    _companions[c] = v;
                }
                v.Body.sprite = sprites != null ? sprites.Unit(c.Type) : null;
                v.transform.localPosition = ToWorld(c.X, c.Y);
                v.Body.sortingOrder = 300 + i;
                v.Tint(Color.white);
            }
        }

        void SyncEnemies(IReadOnlyList<SurvEnemy> enemies)
        {
            var stale = new List<SurvEnemy>();
            foreach (var kv in _enemies) if (!Contains(enemies, kv.Key)) stale.Add(kv.Key);
            foreach (var e in stale) { _entityPool.Release(_enemies[e]); _enemies.Remove(e); }

            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (!_enemies.TryGetValue(e, out var v))
                {
                    v = _entityPool.Get();
                    v.transform.localScale = Vector3.one * (e.Def != null && e.Def.Boss ? EnemyScale * 1.8f : EnemyScale);
                    // 원본 `survRender` 는 먼지 위에 체력바를 그리지 않는다 — 체력은 리더 것만
                    // HUD 에 나온다. 전투에서 쓰는 프리팹을 그대로 가져와 바가 켜져 있었다.
                    if (v.BarRoot != null) v.BarRoot.gameObject.SetActive(false);
                    _enemies[e] = v;
                }
                v.Body.sprite = sprites != null && e.Def != null ? sprites.Enemy(e.Type, e.Def.Look, e.Def.Boss) : null;
                v.transform.localPosition = ToWorld(e.X, e.Y);
                v.Body.sortingOrder = 200 + (int)(e.Y * 4f);

                var tint = e.FrozenT > 0f ? new Color(0.75f, 0.9f, 1f)
                         : e.SlowT > 0f ? new Color(0.82f, 0.92f, 1f) : Color.white;
                v.SetFlash(e.HitT);
                v.StepFlash(Time.unscaledDeltaTime);
                v.Tint(tint);

                if (v.HeatFill != null) v.HeatFill.enabled = false;
                if (v.SkillFill != null) v.SkillFill.enabled = false;
                if (v.HpFill != null)
                {
                    bool hurt = e.Hp < e.MaxHp;
                    v.HpFill.enabled = hurt;
                    if (hurt)
                    {
                        v.HpFill.color = new Color(0.88f, 0.32f, 0.29f);
                        var s = v.HpFill.transform.localScale;
                        v.HpFill.transform.localScale = new Vector3(Mathf.Clamp01(e.Hp / Mathf.Max(1f, e.MaxHp)), s.y, s.z);
                    }
                }
            }
        }

        void SyncMarks(IReadOnlyList<SurvMark> marks)
        {
            while (_markRings.Count < marks.Count) _markRings.Add(MakeDot(ringSprite));
            for (int i = 0; i < _markRings.Count; i++)
            {
                bool active = i < marks.Count;
                _markRings[i].enabled = active;
                if (!active) continue;
                var m = marks[i];
                float t = Mathf.Clamp01(m.Life / Mathf.Max(0.01f, m.MaxLife));
                _markRings[i].transform.localPosition = ToWorld(m.X, m.Y);
                _markRings[i].transform.localScale = Vector3.one * (m.Radius * 2f);
                _markRings[i].color = new Color(1f, 0.6f, 0.24f, 0.25f + 0.55f * t);
            }
        }

        void SyncDots(List<SpriteRenderer> pool, int count, Color defaultColor)
        {
            while (pool.Count < count) pool.Add(MakeDot(dotSprite, defaultColor));
            for (int i = 0; i < pool.Count; i++) pool[i].enabled = i < count;
        }

        SpriteRenderer MakeDot(Sprite sprite, Color? color = null)
        {
            var go = new GameObject("Dot");
            go.transform.SetParent(entityRoot, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color ?? Color.white;
            sr.sortingOrder = 150;
            return sr;
        }

        static bool Contains(IReadOnlyList<SurvCompanion> list, SurvCompanion c)
        {
            for (int i = 0; i < list.Count; i++) if (ReferenceEquals(list[i], c)) return true;
            return false;
        }

        static bool Contains(IReadOnlyList<SurvEnemy> list, SurvEnemy e)
        {
            for (int i = 0; i < list.Count; i++) if (ReferenceEquals(list[i], e)) return true;
            return false;
        }

        static Color ParseColor(string hex, Color fallback)
            => !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;
    }
}
