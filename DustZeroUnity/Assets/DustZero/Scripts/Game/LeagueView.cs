// 리그 판 그리기 — 7x6 칸 위의 가전과 먼지.
//
// 원본 `lgRender` 를 옮긴 것이다. 캔버스에 매 프레임 다시 그리는 대신, 단위마다
// EntityView 를 하나씩 빌려 자리를 옮긴다(디펜스·대청소와 같은 방식).
//
// 칸 좌표는 원본과 같다: x 는 0~7, y 는 0~6 이고 **아래로 갈수록 커진다.**
// 월드는 Y 가 위로 커지므로 ToWorld 에서 뒤집는다.
using System.Collections.Generic;
using DustZero.Core.League;
using UnityEngine;
using UnityEngine.Pool;

namespace DustZero.Game
{
    public sealed class LeagueView : MonoBehaviour
    {
        [SerializeField] Transform entityRoot;
        [SerializeField] EntityView entityPrefab;
        [SerializeField] SpriteLibrary sprites;

        /// <summary>판 바닥 한 장. 우리 쪽 세 줄만 밝게 칠해 둔 그림이다.</summary>
        [SerializeField] SpriteRenderer floor;
        /// <summary>고른 칸을 표시하는 네모.</summary>
        [SerializeField] SpriteRenderer selection;

        /// <summary>아군 ★2 이상에 깔리는 바닥 고리.</summary>
        [SerializeField] Sprite starRing;
        /// <summary>놓을 수 있는 칸을 덮는 옅은 네모.</summary>
        [SerializeField] Sprite cellFill;

        const float AllyScale = 0.82f, EnemyScale = 0.72f;

        ObjectPool<EntityView> _pool;
        readonly Dictionary<LeagueUnit, EntityView> _units = new();
        readonly List<SpriteRenderer> _rings = new();
        readonly List<SpriteRenderer> _marks = new();
        readonly List<EntityView> _ghosts = new();

        void Awake()
        {
            _pool = new ObjectPool<EntityView>(
                createFunc: () =>
                {
                    var v = Instantiate(entityPrefab, entityRoot);
                    SetLayerDeep(v.gameObject, entityRoot.gameObject.layer);
                    return v;
                },
                actionOnGet: v => v.gameObject.SetActive(true),
                actionOnRelease: v => v.gameObject.SetActive(false),
                actionOnDestroy: v => { if (v != null) Destroy(v.gameObject); },
                collectionCheck: false, defaultCapacity: 32, maxSize: 256);
        }

        static void SetLayerDeep(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayerDeep(c.gameObject, layer);
        }

        /// <summary>칸 좌표(아래로 +y)를 월드로. 판 왼쪽 위가 월드 원점이다.</summary>
        public static Vector3 ToWorld(float x, float y) => new(x, -y, 0f);

        public void Build()
        {
            ClearAll();
            if (floor != null)
            {
                floor.transform.localPosition = ToWorld(LeagueSim.Cols / 2f, LeagueSim.Rows / 2f);
                floor.transform.localScale = Vector3.one;
            }
            if (selection != null) selection.gameObject.SetActive(false);
        }

        public void ClearAll()
        {
            foreach (var kv in _units) _pool.Release(kv.Value);
            _units.Clear();
            foreach (var r in _rings) if (r != null) Destroy(r.gameObject);
            _rings.Clear();
            foreach (var m in _marks) if (m != null) Destroy(m.gameObject);
            _marks.Clear();
            foreach (var g in _ghosts) if (g != null) _pool.Release(g);
            _ghosts.Clear();
        }

        /// <summary>
        /// 준비 단계: 판에 놓인 말과, 이번 라운드에 올 먼지의 **흐린 미리보기**를 세운다.
        /// 고른 말이 있으면 놓을 수 있는 칸을 옅게 덮는다(원본 `lgRender` 의 prep 가지).
        /// </summary>
        public void ShowBoard(LeagueSim sim, int selectedCell = -1, bool showPlaceable = false)
        {
            var seen = new HashSet<LeagueUnit>();
            ClearUnits(seen);

            int ring = 0, mark = 0;

            if (showPlaceable)
                for (int i = 0; i < sim.Board.Length; i++)
                {
                    bool can = sim.Board[i] != null || sim.OnBoard < sim.Level;
                    if (!can) continue;
                    PlaceMark(ref mark, i % LeagueSim.Cols + 0.5f,
                              LeagueSim.OurRows + i / LeagueSim.Cols + 0.5f);
                }
            TrimMarks(mark);

            for (int i = 0; i < sim.Board.Length; i++)
            {
                var p = sim.Board[i];
                if (p == null) continue;

                var v = _pool.Get();
                v.transform.localScale = Vector3.one * AllyScale;
                if (v.BarRoot != null) v.BarRoot.gameObject.SetActive(false);
                v.Body.sprite = sprites != null ? sprites.Unit(p.Type) : null;
                v.Body.flipX = false;
                v.Body.sortingOrder = 500;
                v.Tint(Color.white);

                float cx = i % LeagueSim.Cols + 0.5f;
                float cy = LeagueSim.OurRows + i / LeagueSim.Cols + 0.55f;
                v.transform.localPosition = ToWorld(cx, cy);
                ShowStars(v, p.Star);
                _prep.Add(v);

                if (p.Star >= 2) PlaceRing(ref ring, cx, cy, p.Star);
            }
            TrimRings(ring);
            ShowPreview(sim);
            ShowSelection(selectedCell);
        }

        /// <summary>다음 라운드 먼지를 38% 로 흐리게 세워 둔다.</summary>
        void ShowPreview(LeagueSim sim)
        {
            int n = 0;
            foreach (var (id, x, y) in sim.NextSquad())
            {
                while (_ghosts.Count <= n)
                {
                    var g = _pool.Get();
                    g.transform.localScale = Vector3.one * EnemyScale;
                    if (g.BarRoot != null) g.BarRoot.gameObject.SetActive(false);
                    _ghosts.Add(g);
                }
                var view = _ghosts[n++];
                view.gameObject.SetActive(true);
                view.Body.sprite = sprites != null ? sprites.Enemy(id, null, false) : null;
                view.Body.sortingOrder = 350;
                view.Tint(new Color(1f, 1f, 1f, 0.38f));
                view.transform.localPosition = ToWorld(x, y);
                if (view.TierBadge != null) view.TierBadge.text = string.Empty;
            }
            for (int i = n; i < _ghosts.Count; i++)
                if (_ghosts[i] != null) _ghosts[i].gameObject.SetActive(false);
        }

        /// <summary>말 아래의 ★. EntityView 의 뱃지 글자를 빌려 쓴다.</summary>
        static void ShowStars(EntityView v, int star)
        {
            if (v.TierBadge == null) return;
            v.TierBadge.text = new string('\u2605', star);
            v.TierBadge.color = star >= 3 ? new Color(0.949f, 0.694f, 0.180f)
                              : star == 2 ? new Color(0.549f, 0.769f, 0.961f)
                                          : new Color(0.902f, 0.914f, 0.925f);
        }

        void PlaceMark(ref int index, float x, float y)
        {
            while (_marks.Count <= index)
            {
                var go = new GameObject("Placeable");
                go.transform.SetParent(entityRoot, false);
                go.layer = entityRoot.gameObject.layer;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = cellFill;
                sr.sortingOrder = 250;
                sr.color = new Color(0.239f, 0.545f, 0.851f, 0.14f);
                _marks.Add(sr);
            }
            var m = _marks[index++];
            m.gameObject.SetActive(true);
            m.transform.localPosition = ToWorld(x, y);
            m.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
        }

        void TrimMarks(int used)
        {
            for (int i = used; i < _marks.Count; i++)
                if (_marks[i] != null) _marks[i].gameObject.SetActive(false);
        }

        readonly List<EntityView> _prep = new();

        void ClearUnits(HashSet<LeagueUnit> keep)
        {
            foreach (var v in _prep) _pool.Release(v);
            _prep.Clear();
            var stale = new List<LeagueUnit>();
            foreach (var kv in _units) if (!keep.Contains(kv.Key)) stale.Add(kv.Key);
            foreach (var u in stale) { _pool.Release(_units[u]); _units.Remove(u); }
        }

        /// <summary>전투 중: 살아 있는 단위를 따라간다.</summary>
        public void Sync(LeagueSim sim)
        {
            var alive = new HashSet<LeagueUnit>();
            foreach (var u in sim.Units) if (u.Alive) alive.Add(u);
            ClearUnits(alive);
            TrimMarks(0);
            foreach (var g in _ghosts) if (g != null) g.gameObject.SetActive(false);
            ShowSelection(-1);

            int ring = 0;
            foreach (var u in sim.Units)
            {
                if (!u.Alive) continue;
                if (!_units.TryGetValue(u, out var v))
                {
                    v = _pool.Get();
                    v.transform.localScale = Vector3.one * (u.Side == 0 ? AllyScale : EnemyScale);
                    if (v.BarRoot != null) v.BarRoot.gameObject.SetActive(true);
                    v.Body.sprite = u.Side == 0
                        ? sprites?.Unit(u.Type)
                        : sprites?.Enemy(u.Type, null, u.Boss);
                    if (u.Side == 0) ShowStars(v, u.Star);
                    else if (v.TierBadge != null) v.TierBadge.text = string.Empty;
                    _units[u] = v;
                }

                v.transform.localPosition = ToWorld(u.X, u.Y);
                v.Body.flipX = u.Face < 0;
                // 먼지보다 가전을 위에, 아래쪽 줄일수록 앞에 그린다.
                v.Body.sortingOrder = 400 + (int)(u.Y * 10f) + (u.Side == 0 ? 100 : 0);
                v.Tint(u.HitT > 0f ? new Color(1f, 0.55f, 0.5f) : Color.white);
                v.StepFlash(Time.unscaledDeltaTime);

                if (v.HpFill != null)
                {
                    float hp = u.MaxHp > 0f ? Mathf.Clamp01(u.Hp / u.MaxHp) : 0f;
                    var s = v.HpFill.transform.localScale;
                    v.HpFill.transform.localScale = new Vector3(hp, s.y, s.z);
                    v.HpFill.color = u.Side == 0 ? new Color(0.25f, 0.68f, 0.48f)
                                                 : new Color(0.88f, 0.32f, 0.29f);
                }
                if (v.SkillFill != null)
                {
                    float mana = u.MaxMana > 0f ? Mathf.Clamp01(u.Mana / u.MaxMana) : 0f;
                    var s = v.SkillFill.transform.localScale;
                    v.SkillFill.transform.localScale = new Vector3(mana, s.y, s.z);
                }

                if (u.Side == 0 && u.Star >= 2) PlaceRing(ref ring, u.X, u.Y, u.Star);
            }
            TrimRings(ring);
        }

        /// <summary>★2 는 파랑, ★3 은 금빛 고리를 바닥에 깐다 — 원본 `LG_STAR_COL`.</summary>
        void PlaceRing(ref int index, float x, float y, int star)
        {
            while (_rings.Count <= index)
            {
                var go = new GameObject("StarRing");
                go.transform.SetParent(entityRoot, false);
                go.layer = entityRoot.gameObject.layer;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = starRing;
                sr.sortingOrder = 300;
                _rings.Add(sr);
            }
            var ring = _rings[index++];
            ring.gameObject.SetActive(true);
            ring.transform.localPosition = ToWorld(x, y - 0.33f);
            ring.transform.localScale = new Vector3(0.6f, 0.2f, 1f);
            ring.color = star >= 3 ? new Color(0.949f, 0.694f, 0.180f, 0.9f)
                                   : new Color(0.239f, 0.545f, 0.851f, 0.9f);
        }

        void TrimRings(int used)
        {
            for (int i = used; i < _rings.Count; i++)
                if (_rings[i] != null) _rings[i].gameObject.SetActive(false);
        }

        /// <summary>고른 칸을 네모로 표시한다. index 가 음수면 감춘다.</summary>
        public void ShowSelection(int boardIndex)
        {
            if (selection == null) return;
            bool on = boardIndex >= 0;
            selection.gameObject.SetActive(on);
            if (!on) return;
            float cx = boardIndex % LeagueSim.Cols + 0.5f;
            float cy = LeagueSim.OurRows + boardIndex / LeagueSim.Cols + 0.5f;
            selection.transform.localPosition = ToWorld(cx, cy);
        }
    }
}
