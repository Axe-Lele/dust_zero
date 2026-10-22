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
        [SerializeField] TileBase spawnTile;
        [SerializeField] TileBase goalTile;
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

        // The sprites were captured by running the original's own draw functions, so how big
        // they are on screen is fixed by the capture box, not by taste. GameData/spriteexport
        // /one.html draws a unit with k = px/PAD (PAD 1.35) while the game draws it at k =
        // TS*0.92, so one captured image spans 1.35 x 0.92 tiles. Enemies are captured with
        // TS = px/2.6, so their image spans 2.6 tiles. Rendering both at scale 1 shrank every
        // appliance and left the stains as specks.
        const float UnitSpriteScale = 1.35f * 0.92f;
        const float EnemySpriteScale = 2.6f;

        /// <summary>
        /// one.html draws a unit about its origin after translating to (px/2, px/2 + k*0.19),
        /// so the point the original treats as the unit's position sits k*0.19 BELOW the
        /// image's middle - which is where a centre-pivot sprite puts the transform. Lifting
        /// the body by the same distance puts that origin back on the tile centre. Measured
        /// against the exported art: unit_vac's opaque bounds centre on 0.633 of the image
        /// height, and the predicted origin is 0.641.
        /// </summary>
        const float UnitSpriteLift = 0.19f * 0.92f;

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
        SpriteRenderer _tutMarker;
        SpriteRenderer _rangeRing;
        SpriteRenderer _selectRing;
        readonly Dictionary<BattleUnit, SpriteRenderer> _overheatRings = new();
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

            for (int r = 0; r < stage.Rows; r++)
                for (int c = 0; c < stage.Cols; c++)
                {
                    var cell = new Vector3Int(c, -r, 0);
                    char ch = stage.Tiles[r][c];

                    floorMap.SetTile(cell, ch switch
                    {
                        BattleField.Wall => blockedTile,
                        BattleField.Furniture => buildableTile,
                        BattleField.Goal => goalTile,
                        _ => pathTile,
                    });

                    TileBase overlay = ch switch
                    {
                        BattleField.Stove => stoveTile,
                        BattleField.Wet => wetTile,
                        _ => null,
                    };
                    if (overlay != null) overlayMap.SetTile(cell, overlay);

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

            // Entrances: the spawn tile, numbered the way the waves are.
            for (int i = 0; i < stage.Paths.Length; i++)
            {
                var path = stage.Paths[i];
                floorMap.SetTile(new Vector3Int(path.StartX, -path.StartY, 0), spawnTile);
                string name = i < stage.SpawnNames.Length ? stage.SpawnNames[i] : string.Empty;
                AddLabel($"{i + 1} {name}", path.StartX + 0.5f, path.StartY + 0.7f, 0.2f,
                         new Color(0.29f, 0.35f, 0.40f));
            }

            BuildSpawnWarnViews(stage);

            AddLabel(stage.GoalName, stage.GoalX + 0.5f, stage.GoalY + 0.55f, 0.24f,
                     new Color(0.114f, 0.133f, 0.149f));

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
                    v.Body.transform.localScale = Vector3.one * UnitSpriteScale;
                    v.Body.transform.localPosition = new Vector3(0f, UnitSpriteLift, 0f);
                    v.transform.localScale = Vector3.one;
                    _units[u] = v;
                }

                v.transform.localPosition = ToWorld(u.X, u.Y);
                // Lower rows draw in front, so a unit never hides the one below it.
                v.Body.sortingOrder = 100 + u.Row * 4;

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
