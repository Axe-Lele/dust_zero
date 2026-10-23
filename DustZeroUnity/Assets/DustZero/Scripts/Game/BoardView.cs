// Paints the stage onto a Tilemap and keeps pooled sprites in step with the simulation.
//
// The board is laid out so tile (0,0) sits at the top-left and y grows downward, matching the
// table's row-major grids and the original's canvas coordinates. Converting once here means
// no other file has to think about the flip.
using System.Collections.Generic;
using System.Linq;
using DustZero.Core.Defense;
using DustZero.Data;
using TMPro;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Tilemaps;

namespace DustZero.Game
{
    public sealed class BoardView : MonoBehaviour
    {
        [SerializeField] Tilemap floorMap;
        [SerializeField] Tilemap overlayMap;
        [SerializeField] Transform entityRoot;
        [SerializeField] EntityView entityPrefab;
        [SerializeField] SpriteLibrary sprites;
        [SerializeField] SpriteRenderer highlightPrefab;
        [SerializeField] Transform highlightRoot;
        [SerializeField] Transform labelRoot;

        [Header("타일")]
        /// <summary>
        /// The redesign's semantic tiles. A square says what it is for - walked on, built on,
        /// blocked - rather than what it is made of, so the original's per-floor plank and
        /// grout variants collapse to one set shared by every map.
        /// </summary>
        [SerializeField] TileBase pathTile;
        [SerializeField] TileBase buildableTile;
        [SerializeField] TileBase blockedTile;
        /// <summary>Hazards the pack has no tile for; drawn over the path.</summary>
        [SerializeField] TileBase stoveTile;
        [SerializeField] TileBase wetTile;

        [Header("강조")]
        [SerializeField] Sprite validHighlight;
        [SerializeField] Sprite invalidHighlight;
        [SerializeField] Sprite briefHighlight;
        [SerializeField] Sprite selectRingSprite;
        [SerializeField] Sprite overheatRingSprite;
        [SerializeField] Sprite stoveRingSprite;

        /// <summary>Flat square used for the tutorial shade and marker; the highlight prefab's
        /// rounded outline is for placeable tiles and reads as a blur when stretched.</summary>
        [SerializeField] Sprite solidSprite;
        /// <summary>Dashed ring drawn around a selected unit to show its reach.</summary>
        [SerializeField] Sprite rangeRingSprite;

        [Header("보드 글자")]
        [SerializeField] TMP_FontAsset boardFont;

        [Header("리젠 예고")]
        [SerializeField] SpawnWarnView spawnWarnPrefab;
        [SerializeField] Transform spawnWarnRoot;

        /// <summary>World units per board tile. 1 keeps simulation coordinates and world units aligned.</summary>
        public const float TileSize = 1f;

        // 적 그림은 여전히 one.html 이 TS = px/2.6 으로 떠 준 것이라 2.6칸을 차지한다.
        const float EnemySpriteScale = 2.6f;

        // 가전은 10.22-f 부터 **그려진 SD 시트**다. 예전 치비는 one.html 의 포장 상자가
        // 크기를 정해 줘서 고정 배율(1.35 x 0.92)로 맞았지만, 새 시트는 가전마다 그림
        // 크기가 다르다(576x640 · 537x640 · 412x640 …). 고정 배율을 그대로 두었더니
        // 가전이 칸 서넛을 덮었다 — 사용자가 화면을 보고 바로 알아봤다.
        // 지금은 FitSprite 가 원본 drawSpriteSD 의 규칙(0.82 x 0.88칸, 발은 0.33칸 아래)
        // 으로 **그림마다** 계산한다.

        ObjectPool<EntityView> _pool;
        readonly Dictionary<BattleUnit, EntityView> _units = new();
        readonly Dictionary<BattleEnemy, EntityView> _enemies = new();
        readonly List<SpriteRenderer> _highlights = new();
        readonly List<SpriteRenderer> _briefTiles = new();
        readonly List<GameObject> _labels = new();

        // Tutorial spotlight: a pulsing marker on the tile the step insists on, plus four
        // shade bands that darken everything around it. Kept apart from _highlights so the
        // marker is visible before the player has picked any card.
        readonly List<SpawnWarnView> _warnViews = new();
        readonly SpawnWarningBoard _warnings = new();
        SpriteRenderer _bakedFloor;
        SpriteRenderer _tutMarker;
        SpriteRenderer _rangeRing;
        SpriteRenderer _selectRing;
        readonly Dictionary<BattleUnit, SpriteRenderer> _overheatRings = new();

        /// <summary>
        /// 가전마다 하나씩 도는 동작 시계. 시뮬에는 없는 값이라 뷰가 들고 있는다 —
        /// 연출 전용이고 전투 결과에 영향을 주지 않는다는 원본의 약속을 그대로 지킨다.
        /// </summary>
        readonly Dictionary<BattleUnit, MotionState> _motions = new();
        readonly SpriteRenderer[] _shade = new SpriteRenderer[4];

        // The original's render() redraws a pulsing ring over every stove tile each frame,
        // independent of simulation pause/speed (G.rt advances unconditionally). Tracking the
        // tile's (c,r) lets each ring reuse the original's per-tile phase offset.
        readonly List<(SpriteRenderer ring, int c, int r)> _stoveRings = new();
        float _stoveClock;

        BattleSim _sim;

        public Vector2 Origin { get; private set; }

        void Awake()
        {
            _pool = new ObjectPool<EntityView>(
                createFunc: () => Instantiate(entityPrefab, entityRoot),
                actionOnGet: v => v.gameObject.SetActive(true),
                actionOnRelease: v => v.gameObject.SetActive(false),
                actionOnDestroy: v => { if (v != null) Destroy(v.gameObject); },
                collectionCheck: false, defaultCapacity: 64, maxSize: 512);
        }

        /// <summary>Board coordinates (x right, y down) to world space.</summary>
        public Vector3 ToWorld(float x, float y)
            => new(Origin.x + x * TileSize, Origin.y - y * TileSize, 0f);

        /// <summary>
        /// Where the whole board lands on screen. The board is framed by moving a camera, so
        /// no layout rule prevents it from sliding under the HUD or the card tray; this is
        /// what a test has to measure to notice that it did.
        /// </summary>
        public Rect ScreenBounds(Camera cam)
        {
            if (_sim == null || cam == null) return Rect.zero;
            // **그려진** 판의 테두리를 돌려준다. `ToWorld` 로 재면 안 된다 — 칠해진 바닥은
            // 셀 모서리에 걸리고 가전도 그 셀 중심에 놓이는데(실측: 칸(3,2) 가전과 타일이
            // 모두 (0.5, 2.0)), `ToWorld(3,2)` 는 (0, 2.5) 라 **반 칸** 어긋난다.
            // 그 차이로 판이 화면 가운데에서 반 칸 오른쪽·아래로 밀려 보였다.
            // 칸 (0,0) 의 왼쪽 위 모서리가 Origin 이고, 거기서 칸 수만큼 간다.
            var tl = cam.WorldToScreenPoint(new Vector3(Origin.x, Origin.y, 0f));
            var br = cam.WorldToScreenPoint(new Vector3(Origin.x + _sim.Stage.Cols * TileSize,
                                                        Origin.y - _sim.Stage.Rows * TileSize, 0f));
            float x0 = Mathf.Min(tl.x, br.x), x1 = Mathf.Max(tl.x, br.x);
            float y0 = Mathf.Min(tl.y, br.y), y1 = Mathf.Max(tl.y, br.y);
            return new Rect(x0, y0, x1 - x0, y1 - y0);
        }

        /// <summary>World space back to board coordinates, for tapping tiles.</summary>
        public Vector2 ToBoard(Vector3 world)
            => new((world.x - Origin.x) / TileSize, (Origin.y - world.y) / TileSize);

        public void Build(BattleSim sim)
        {
            _sim = sim;
            var stage = sim.Stage;

            // Centre the board on the origin so the camera needs no per-stage framing.
            Origin = new Vector2(-(stage.Cols - 1) * TileSize * 0.5f,
                                  (stage.Rows - 1) * TileSize * 0.5f);

            floorMap.ClearAllTiles();
            overlayMap.ClearAllTiles();
            ClearLabels();
            ClearStoveRings();

            // 원본은 판 전체를 캔버스 한 장에 그린다. 우리도 지도마다 한 장을 구워 두고
            // 그것을 깐다. 구운 장이 없는 판(협동 고리판)만 아래의 타일맵으로 되돌아간다.
            var baked = sprites != null ? sprites.Board($"board_{stage.MapId}") : null;
            ShowBakedFloor(baked, stage.Cols, stage.Rows);

            for (int r = 0; r < stage.Rows; r++)
                for (int c = 0; c < stage.Cols; c++)
                {
                    var cell = new Vector3Int(c, -r, 0);
                    char ch = stage.Tiles[r][c];

                    if (baked == null)
                    {
                        floorMap.SetTile(cell, ch switch
                        {
                            BattleField.Wall => blockedTile,
                            BattleField.Furniture => buildableTile,
                            _ => pathTile,
                        });

                        TileBase overlay = ch switch
                        {
                            BattleField.Stove => stoveTile,
                            BattleField.Wet => wetTile,
                            _ => null,
                        };
                        if (overlay != null) overlayMap.SetTile(cell, overlay);
                    }

                    if (ch == BattleField.Stove && stoveRingSprite != null)
                    {
                        var ringGo = new GameObject("StoveRing");
                        ringGo.transform.SetParent(highlightRoot != null ? highlightRoot : transform, false);
                        var ring = ringGo.AddComponent<SpriteRenderer>();
                        ring.sprite = stoveRingSprite;
                        ring.sortingOrder = 15;
                        ring.transform.localPosition = ToWorld(c + 0.5f, r + 0.5f);
                        _stoveRings.Add((ring, c, r));
                    }
                }
            _stoveClock = 0f;

            // 입구. 원본 buildBoard 는 칸을 칠하지 않고 **위쪽 0.34칸**에 환기구를 얹고,
            // 번호와 이름을 그 아래(0.7칸)에 적는다. 칸 전체를 파랗게 칠하면 바닥이 사라져
            // 어느 길에서 나오는지 보이지 않는다 — 사용자가 화면을 보고 짚은 차이다.
            for (int i = 0; i < stage.Paths.Length; i++)
            {
                var path = stage.Paths[i];
                AddBoardArt("vent", path.StartX, path.StartY);
                string name = i < stage.SpawnNames.Length ? stage.SpawnNames[i] : string.Empty;
                AddLabel($"{i + 1} {name}", path.StartX + 0.5f, path.StartY + 0.7f, 0.2f,
                         new Color(0.29f, 0.35f, 0.40f));
            }

            BuildSpawnWarnViews(stage);

            // 목표물도 마찬가지로 바닥 위에 그린다. 그림은 바닥 종류마다 다르다(paintGoal).
            AddBoardArt($"goal_{stage.Floor}", stage.GoalX, stage.GoalY);
            // 글자 색은 원본 DARK_GOAL 이 정한다 — 어두운 목표물 위에서는 흰 글씨다.
            bool dark = DarkGoal.Contains(stage.Floor);
            AddLabel(stage.GoalName, stage.GoalX + 0.5f, stage.GoalY + 0.55f,
                     stage.GoalName != null && stage.GoalName.Length > 3 ? 0.19f : 0.24f,
                     dark ? Color.white : new Color(0.114f, 0.133f, 0.149f));

            // A tile sits at its cell's centre, so cell (c,-r) lands half a unit ABOVE the
            // cell corner while ToWorld puts the same tile's centre half a unit BELOW it.
            // Dropping the grid one unit makes the painted floor line up with the simulation.
            floorMap.transform.parent.localPosition = new Vector3(Origin.x, Origin.y - TileSize, 0f);
        }

        /// <summary>
        /// 10.17-e's entrance warnings. One view per entrance, created on Build and reused;
        /// the board asks for this every frame so nothing here may allocate.
        ///
        /// <example><code>
        /// board.SyncSpawnWarnings(db, db.Config.spawnWarnSec, presentationTime);
        /// </code></example>
        /// </summary>
        public void SyncSpawnWarnings(GameDatabase db, float window, float time)
        {
            if (_sim == null || _warnViews.Count == 0) return;
            // A finished battle has nothing left to announce, and 0 turns the feature off.
            bool live = window > 0f && !_sim.Finished;
            for (int i = 0; i < _warnViews.Count; i++)
            {
                var view = _warnViews[i];
                if (view == null) continue;
                if (live && _warnings.TryBuild(db, _sim, i, window, out var w))
                    view.Bind(this, w, db, sprites, _sim.Stage.Cols,
                              _sim.Stage.Paths[i].StartX, _sim.Stage.Paths[i].StartY, time);
                else view.Hide();
            }
        }

        /// <summary>How many entrances are currently warning. Used by tests.</summary>
        public int ActiveSpawnWarnings()
        {
            int n = 0;
            foreach (var v in _warnViews) if (v != null && v.gameObject.activeSelf) n++;
            return n;
        }

        void BuildSpawnWarnViews(StageDef stage)
        {
            foreach (var v in _warnViews) if (v != null) Destroy(v.gameObject);
            _warnViews.Clear();
            if (spawnWarnPrefab == null) return;
            var root = spawnWarnRoot != null ? spawnWarnRoot : transform;
            for (int i = 0; i < stage.Paths.Length; i++)
            {
                var v = Instantiate(spawnWarnPrefab, root);
                v.name = $"SpawnWarn{i + 1}";
                v.Hide();
                _warnViews.Add(v);
            }
        }

        /// <summary>
        /// 원본 DARK_GOAL. 이 바닥들의 목표물은 어두워서 글자를 **흰색**으로 뒤집는다.
        /// </summary>
        static readonly HashSet<string> DarkGoal = new()
        { "wood", "garage", "sewer", "duct", "throne", "storage", "boiler" };

        /// <summary>
        /// 구워 둔 판 바닥 한 장을 깐다. 스프라이트는 가로 <paramref name="cols"/>칸 ·
        /// 세로 <paramref name="rows"/>칸 크기(PPU 가 한 칸)라 가운데만 맞추면 된다.
        /// null 이면 꺼서 타일맵이 보이게 둔다.
        /// </summary>
        void ShowBakedFloor(Sprite baked, int cols, int rows)
        {
            if (baked == null)
            {
                if (_bakedFloor != null) _bakedFloor.gameObject.SetActive(false);
                return;
            }
            if (_bakedFloor == null)
            {
                var go = new GameObject("BakedFloor");
                go.transform.SetParent(transform, false);
                _bakedFloor = go.AddComponent<SpriteRenderer>();
                _bakedFloor.sortingOrder = 0;
            }
            _bakedFloor.gameObject.SetActive(true);
            _bakedFloor.sprite = baked;
            _bakedFloor.transform.localPosition = ToWorld(cols * 0.5f, rows * 0.5f);
        }

        /// <summary>구워 둔 판 바닥. 테스트가 자리를 재려고 찾는다.</summary>
        public SpriteRenderer BakedFloor => _bakedFloor;

        /// <summary>바닥 위에 얹는 그림 한 장(입구 환기구·목표물). 칸 하나를 꽉 채운다.</summary>
        void AddBoardArt(string key, int x, int y)
        {
            var sprite = sprites != null ? sprites.Board(key) : null;
            if (sprite == null) return;
            var go = new GameObject(key);
            go.transform.SetParent(labelRoot != null ? labelRoot : transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = 10;
            go.transform.localPosition = ToWorld(x + 0.5f, y + 0.5f);
            _labels.Add(go);
        }

        /// <summary>Drops a world-space caption on the board, as the original labels its canvas.</summary>
        void AddLabel(string text, float x, float y, float size, Color color)
        {
            if (boardFont == null || string.IsNullOrEmpty(text)) return;
            var go = new GameObject("Label");
            go.transform.SetParent(labelRoot != null ? labelRoot : transform, false);
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.font = boardFont;
            tmp.text = text;
            tmp.fontSize = size * 10f;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.rectTransform.sizeDelta = new Vector2(2f, 0.6f);
            tmp.sortingOrder = 60;
            go.transform.localPosition = ToWorld(x, y);
            _labels.Add(go);
        }

        void ClearLabels()
        {
            foreach (var l in _labels) if (l != null) Destroy(l);
            _labels.Clear();
        }

        void ClearStoveRings()
        {
            foreach (var (ring, _, _) in _stoveRings) if (ring != null) Destroy(ring.gameObject);
            _stoveRings.Clear();
        }

        // ------------------------------------------------------------------ entities

        public void Sync(float presentationTime)
        {
            if (_sim == null) return;
            SyncUnits();
            SyncEnemies(presentationTime);
            SyncStoveRings();
        }

        /// <summary>
        /// Ported from the original's render loop: `a=.35+.25*Math.sin(t*5+c+r)` on an orange
        /// stroke at every stove tile. G.rt (the clock the original uses) advances every frame
        /// regardless of pause or battle speed, so this uses unscaled time rather than the
        /// simulation's own clock - pausing the battle must not freeze the burner.
        /// </summary>
        void SyncStoveRings()
        {
            if (_stoveRings.Count == 0) return;
            _stoveClock += Time.unscaledDeltaTime;
            for (int i = 0; i < _stoveRings.Count; i++)
            {
                var (ring, c, r) = _stoveRings[i];
                float a = 0.35f + 0.25f * Mathf.Sin(_stoveClock * 5f + c + r);
                ring.color = new Color(1f, 0.47f, 0.157f, a);
            }
        }

        /// <summary>
        /// 동작 칸을 골라 몸통 그림을 바꾼다. 고를 것이 없으면(가만히 있거나 과열)
        /// SD 한 장으로 되돌린다. 시간은 `unscaledDeltaTime` 이 아니라 전투 시간과 같이
        /// 가야 일시정지·배속이 그대로 먹는다 — 원본도 시뮬 시계로 돌린다.
        /// </summary>
        void SyncMotion(BattleUnit u, EntityView v)
        {
            if (sprites == null || !_motions.TryGetValue(u, out var m)) return;

            // 때리지 않는 지원 가전은 스스로 움직인다(원본 `opt.pulse`).
            bool support = !u.IsBlocker && u.Def != null && u.Def.Atk <= 0f;
            // Kick 은 **근접 공격만** 세운다 — 그걸 보면 원거리 가전이 영원히 SD 로 서 있다
            // (PlayMode 가 그걸 잡았다). AtkT 는 쏘는 순간 간격으로 되돌아가므로 모든 가전이
            // 같은 신호를 낸다.
            m.Step(_sim.LastDt, u.AtkT, u.SkillActive, support);

            int frame = m.Pick(u.Overheated, u.SkillT);
            var sprite = frame < 0 ? sprites.Unit(u.Type) : sprites.MotionFrame(u.Type, frame);
            // 모션 칸이 없으면 SD 로. 한 칸 빠졌다고 가전이 사라지면 안 된다.
            bool motion = sprite != null;
            v.Body.sprite = motion ? sprite : sprites.Unit(u.Type);
            FitSprite(v.Body, motion ? sprites.MotionScale(u.Type) : 0f);
        }

        /// <summary>
        /// 원본 `drawSpriteSD` 의 크기·자리 규칙. 고정 배율을 쓰면 안 된다 — SD 는 가전마다
        /// 그림 크기가 달라(576x640 · 537x640 …) 한 값으로는 누구는 크고 누구는 작다.
        /// 실제로 고정 1.24 배를 쓰다가 가전이 칸 서넛을 덮었다.
        ///
        ///   SD   : 가로 0.82칸 · 세로 0.88칸 **안에** 비율 그대로 넣고, 발을 칸 중심에서
        ///          0.33칸 아래에 둔다.
        ///   모션 : 칸(320x320)이라 한 변이 1.02칸 × 보정배율이고, 그 칸의 아래 6% 가
        ///          발밑으로 내려간다(원본의 -sz*.94 자리).
        /// </summary>
        static void FitSprite(SpriteRenderer body, float motionScale)
        {
            var sp = body.sprite;
            if (sp == null) return;
            Vector2 size = sp.bounds.size;      // 배율 1 일 때의 월드 크기(PPU 반영)
            if (size.x <= 0f || size.y <= 0f) return;

            float scale, centreY;
            if (motionScale > 0f)
            {
                float sz = 1.02f * TileSize * motionScale;
                scale = sz / size.y;
                centreY = -0.33f * TileSize + 0.44f * sz;
            }
            else
            {
                scale = Mathf.Min(0.82f * TileSize / size.x, 0.88f * TileSize / size.y);
                centreY = -0.33f * TileSize + size.y * scale * 0.5f;
            }

            body.transform.localScale = Vector3.one * scale;
            var p = body.transform.localPosition;
            body.transform.localPosition = new Vector3(p.x, centreY, p.z);
        }

        void SyncUnits()
        {
            foreach (var u in _sim.Units)
            {
                if (!_units.TryGetValue(u, out var v))
                {
                    v = _pool.Get();
                    v.Body.sprite = sprites != null ? sprites.Unit(u.Type) : null;
                    // Scale the body, not the view: the bars hang off the view and are already
                    // sized in tiles.
                    _motions[u] = new MotionState();
                    v.Body.transform.localPosition = Vector3.zero;
                    FitSprite(v.Body, 0f);
                    v.transform.localScale = Vector3.one;
                    _units[u] = v;
                }

                v.transform.localPosition = ToWorld(u.X, u.Y);
                // Lower rows draw in front, so a unit never hides the one below it.
                v.Body.sortingOrder = 100 + u.Row * 4;

                SyncMotion(u, v);

                var tint = u.Overheated ? new Color(1f, 0.55f, 0.5f) : Color.white;
                v.StepFlash(Time.unscaledDeltaTime);
                v.Tint(tint);
                SyncOverheatRing(u);

                if (v.BarRoot != null) v.BarRoot.gameObject.SetActive(true);
                SetBar(v.HeatFill, u.IsBlocker ? u.HeatRatio : 0f,
                       u.Overheated || u.HeatRatio > 0.75f
                           ? new Color(0.88f, 0.32f, 0.29f)
                           : new Color(0.95f, 0.76f, 0.31f));
                SetBar(v.SkillFill,
                       u.SkillActive ? u.SkillT / Mathf.Max(0.001f, u.Def.SkillDuration)
                                     : u.Sp / Mathf.Max(0.001f, u.Def.SkillCharge),
                       u.SkillActive ? new Color(0.24f, 0.55f, 0.85f) : new Color(1f, 0.6f, 0.24f));
                if (v.HpFill != null) v.HpFill.enabled = false;

                // 10.17-d: Tier stars, and none at tier 1. Writing Tier-1 stars here is the
                // bug the build fixed - the field then disagreed with the sheet and the
                // boost button, which both count from Tier.
                if (v.TierBadge != null)
                {
                    bool boosted = u.Tier > 1;
                    v.TierBadge.enabled = boosted;
                    if (boosted) v.TierBadge.text = new string('★', u.Tier);
                    v.TierBadge.sortingOrder = 520 + u.Row * 4;
                }
            }

            // Recycle views whose unit was retreated or removed.
            if (_units.Count == _sim.Units.Count) return;
            var stale = new List<BattleUnit>();
            foreach (var kv in _units)
                if (!_sim.Units.Contains(kv.Key)) stale.Add(kv.Key);
            foreach (var k in stale) { _pool.Release(_units[k]); _units.Remove(k); }
        }

        void SyncEnemies(float t)
        {
            foreach (var e in _sim.Enemies)
            {
                if (!_enemies.TryGetValue(e, out var v))
                {
                    v = _pool.Get();
                    v.Body.sprite = sprites != null ? sprites.Enemy(e.Type, e.Def.Look, e.Def.Boss) : null;
                    // A boss needs no extra multiplier: its larger radius is already baked into
                    // the captured image, and multiplying again drew it at twice its size.
                    v.Body.transform.localScale = Vector3.one * EnemySpriteScale;
                    v.Body.transform.localPosition = Vector3.zero;
                    v.transform.localScale = Vector3.one;
                    _enemies[e] = v;
                }

                var p = _sim.PositionOf(e);
                // No lift here: drawEnemy raises a flyer inside the captured image already.
                v.transform.localPosition = ToWorld(p.X, p.Y);
                v.Body.sortingOrder = (e.IsFlying ? 400 : 110) + Mathf.RoundToInt(p.Y * 4f);

                if (e.HitT > 0f) v.SetFlash(e.HitT);
                v.StepFlash(Time.unscaledDeltaTime);

                var tint = Color.white;
                if (e.FrozenT > 0f) tint = new Color(0.75f, 0.9f, 1f);
                else if (e.SlowT > 0f) tint = new Color(0.82f, 0.92f, 1f);
                v.Tint(tint);

                if (v.BarRoot != null) v.BarRoot.gameObject.SetActive(true);
                if (v.HeatFill != null) v.HeatFill.enabled = false;
                if (v.SkillFill != null) v.SkillFill.enabled = false;
                // Only show the bar once the enemy has actually taken a hit.
                SetBar(v.HpFill, e.Hp < e.MaxHp ? Mathf.Clamp01(e.Hp / e.MaxHp) : 0f,
                       new Color(0.88f, 0.32f, 0.29f));
            }

            if (_enemies.Count == _sim.Enemies.Count) return;
            var stale = new List<BattleEnemy>();
            foreach (var kv in _enemies)
                if (!_sim.Enemies.Contains(kv.Key)) stale.Add(kv.Key);
            foreach (var k in stale) { _pool.Release(_enemies[k]); _enemies.Remove(k); }
        }

        /// <summary>Rings an overheated blocker, which the design marks on the board itself.</summary>
        void SyncOverheatRing(BattleUnit u)
        {
            if (overheatRingSprite == null) return;
            if (!_overheatRings.TryGetValue(u, out var ring) || ring == null)
            {
                if (!u.Overheated) return;
                var go = new GameObject("Overheat");
                go.transform.SetParent(highlightRoot != null ? highlightRoot : transform, false);
                ring = go.AddComponent<SpriteRenderer>();
                ring.sprite = overheatRingSprite;
                ring.sortingOrder = 94;
                _overheatRings[u] = ring;
            }
            ring.enabled = u.Overheated;
            if (!u.Overheated) return;
            ring.transform.localPosition = ToWorld(u.X, u.Y);
            ring.transform.localScale = Vector3.one;
        }

        static void SetBar(SpriteRenderer fill, float ratio, Color color)
        {
            if (fill == null) return;
            if (ratio <= 0f) { fill.enabled = false; return; }
            fill.enabled = true;
            fill.color = color;
            var s = fill.transform.localScale;
            fill.transform.localScale = new Vector3(Mathf.Clamp01(ratio), s.y, s.z);
        }

        // ------------------------------------------------------------------ placement hints

        /// <summary>
        /// Lights the tiles where the selected card may be placed. A tutorial step may pin
        /// the choice to one tile, in which case only that tile lights up.
        /// </summary>
        public void ShowPlacement(string unitType, (int X, int Y)? forcedTile = null)
        {
            ClearHighlights();
            if (_sim == null || string.IsNullOrEmpty(unitType) || highlightPrefab == null) return;

            var def = _sim.Db.Unit(unitType);
            for (int r = 0; r < _sim.Field.Rows; r++)
                for (int c = 0; c < _sim.Field.Cols; c++)
                {
                    if (forcedTile.HasValue && (forcedTile.Value.X != c || forcedTile.Value.Y != r)) continue;
                    bool legal = _sim.CanPlace(unitType, c, r);
                    bool excluded = def.Tile == TileKind.Ground &&
                                    BattleField.IsGroundTile(_sim.Field.TileAt(c, r)) &&
                                    _sim.Field.NoDeploy(c, r);
                    if (!legal && !excluded) continue;

                    var h = Instantiate(highlightPrefab, highlightRoot);
                    h.transform.localPosition = ToWorld(c + 0.5f, r + 0.5f);
                    h.transform.localScale = Vector3.one;
                    // The pack draws both states, so no tinting is involved.
                    h.sprite = legal ? validHighlight : invalidHighlight;
                    h.color = Color.white;
                    h.sortingOrder = 50;
                    _highlights.Add(h);
                }
        }

        /// <summary>
        /// Lights the tile a tutorial step insists on and darkens the rest of the board.
        /// <paramref name="marker"/> is the "place here" pulse; <paramref name="hole"/> is the
        /// cell the shade leaves uncovered, which for a heat step is the unit being pointed at.
        /// </summary>
        public void SetTutorialFocus((int X, int Y)? marker, (int X, int Y)? hole, bool dim, float time)
        {
            if (highlightPrefab == null || _sim == null) return;
            var tile = hole ?? marker;

            if (marker.HasValue)
            {
                if (_tutMarker == null)
                {
                    _tutMarker = Instantiate(highlightPrefab, highlightRoot);
                    _tutMarker.sortingOrder = 605;
                    if (solidSprite != null) _tutMarker.sprite = solidSprite;
                    _tutMarker.transform.localScale = Vector3.one;
                }
                _tutMarker.enabled = true;
                _tutMarker.transform.localPosition = ToWorld(marker.Value.X + 0.5f, marker.Value.Y + 0.5f);
                float pulse = 0.5f + 0.5f * Mathf.Sin(time * 6f);
                _tutMarker.color = new Color(1f, 0.60f, 0.24f, 0.18f + 0.30f * pulse);
            }
            else if (_tutMarker != null) _tutMarker.enabled = false;

            if (!dim) { foreach (var b in _shade) if (b != null) b.enabled = false; return; }

            // Four bands around the hole; with no hole the first band covers the whole board.
            float cols = _sim.Field.Cols, rows = _sim.Field.Rows;
            float hx = tile?.X ?? 0f, hy = tile?.Y ?? 0f, hw = tile.HasValue ? 1f : 0f;
            Span4(0, 0f, 0f, cols, tile.HasValue ? hy : rows);                 // above
            Span4(1, 0f, hy + hw, cols, tile.HasValue ? rows - hy - hw : 0f);  // below
            Span4(2, 0f, hy, hx, hw);                                          // left
            Span4(3, hx + hw, hy, cols - hx - hw, hw);                         // right
        }

        /// <summary>Places shade band <paramref name="i"/> over the given tile-space rectangle.</summary>
        void Span4(int i, float x, float y, float w, float h)
        {
            if (w <= 0f || h <= 0f) { if (_shade[i] != null) _shade[i].enabled = false; return; }
            if (_shade[i] == null)
            {
                _shade[i] = Instantiate(highlightPrefab, highlightRoot);
                _shade[i].sortingOrder = 600;
                if (solidSprite != null) _shade[i].sprite = solidSprite;
            }
            var b = _shade[i];
            b.enabled = true;
            b.color = new Color(0.07f, 0.08f, 0.10f, 0.55f);
            b.transform.localPosition = ToWorld(x + w * 0.5f, y + h * 0.5f);
            b.transform.localScale = new Vector3(w, h, 1f);
        }

        /// <summary>
        /// Rings the selected unit's reach, or clears it when <paramref name="u"/> is null.
        /// The radius comes from the simulation so a boosted unit's bonus shows.
        /// </summary>
        public void ShowUnitRange(BattleUnit u)
        {
            if (_sim == null || rangeRingSprite == null) return;
            if (u == null || u.Dead)
            {
                if (_rangeRing != null) _rangeRing.enabled = false;
                if (_selectRing != null) _selectRing.enabled = false;
                return;
            }
            if (_rangeRing == null)
            {
                var go = new GameObject("RangeRing");
                go.transform.SetParent(highlightRoot != null ? highlightRoot : transform, false);
                _rangeRing = go.AddComponent<SpriteRenderer>();
                _rangeRing.sprite = rangeRingSprite;
                _rangeRing.sortingOrder = 90;
            }
            if (_selectRing == null)
            {
                var go = new GameObject("SelectRing");
                go.transform.SetParent(highlightRoot != null ? highlightRoot : transform, false);
                _selectRing = go.AddComponent<SpriteRenderer>();
                _selectRing.sprite = selectRingSprite;
                _selectRing.sortingOrder = 95;
            }

            float radius = _sim.DisplayRange(u);
            _rangeRing.enabled = radius > 0f;
            _rangeRing.color = Color.white;
            _rangeRing.transform.localPosition = ToWorld(u.X, u.Y);
            // The sprite is a unit circle, so the diameter is twice the radius in tiles.
            _rangeRing.transform.localScale = Vector3.one * (radius * 2f);

            _selectRing.enabled = true;
            _selectRing.transform.localPosition = ToWorld(u.X, u.Y);
            _selectRing.transform.localScale = Vector3.one;
        }

        /// <summary>
        /// Lays the brief highlight along every entrance route, so the player can see where
        /// the dust will come from before committing to a layout.
        /// </summary>
        public void ShowBriefRoutes(bool on)
        {
            foreach (var h in _briefTiles) if (h != null) Destroy(h.gameObject);
            _briefTiles.Clear();
            if (!on || _sim == null || briefHighlight == null || highlightPrefab == null) return;

            var seen = new HashSet<(int, int)>();
            foreach (var path in _sim.Stage.Paths)
                foreach (var seg in path.Segments)
                {
                    int sx = Mathf.Min(seg.X1, seg.X2), ex = Mathf.Max(seg.X1, seg.X2);
                    int sy = Mathf.Min(seg.Y1, seg.Y2), ey = Mathf.Max(seg.Y1, seg.Y2);
                    for (int x = sx; x <= ex; x++)
                        for (int y = sy; y <= ey; y++)
                        {
                            if (!seen.Add((x, y))) continue;
                            var h = Instantiate(highlightPrefab, highlightRoot);
                            h.sprite = briefHighlight;
                            h.color = Color.white;
                            h.transform.localPosition = ToWorld(x + 0.5f, y + 0.5f);
                            h.transform.localScale = Vector3.one;
                            h.sortingOrder = 45;
                            _briefTiles.Add(h);
                        }
                }
        }

        public void ClearHighlights()
        {
            foreach (var h in _highlights) if (h != null) Destroy(h.gameObject);
            _highlights.Clear();
        }

        public void ClearAll()
        {
            foreach (var v in _units.Values) _pool.Release(v);
            foreach (var v in _enemies.Values) _pool.Release(v);
            _units.Clear();
            _enemies.Clear();
            ClearHighlights();
            ShowBriefRoutes(false);
            ClearLabels();
            foreach (var v in _warnViews) if (v != null) v.Hide();
            if (_tutMarker != null) _tutMarker.enabled = false;
            if (_rangeRing != null) _rangeRing.enabled = false;
            if (_selectRing != null) _selectRing.enabled = false;
            foreach (var ring in _overheatRings.Values) if (ring != null) ring.enabled = false;
            foreach (var b in _shade) if (b != null) b.enabled = false;
            _sim = null;
        }
    }
}
