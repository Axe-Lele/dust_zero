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
        /// <summary>Plank-seam variants; the cell picks one the way the original offsets them.</summary>
        [SerializeField] TileBase[] floorWoodTiles;
        /// <summary>Checker variants for the kitchen floor.</summary>
        [SerializeField] TileBase[] floorKitchenTiles;
        [SerializeField] TileBase floorBathTile;
        [SerializeField] TileBase wallTile;
        [SerializeField] TileBase furnitureTile;
        [SerializeField] TileBase furnitureKitchenTile;
        [SerializeField] TileBase stoveTile;
        [SerializeField] TileBase wetTile;
        [SerializeField] TileBase ventTile;
        [SerializeField] TileBase goalWoodTile;
        [SerializeField] TileBase goalKitchenTile;
        [SerializeField] TileBase goalBathTile;

        /// <summary>Flat square used for the tutorial shade and marker; the highlight prefab's
        /// rounded outline is for placeable tiles and reads as a blur when stretched.</summary>
        [SerializeField] Sprite solidSprite;
        /// <summary>Dashed ring drawn around a selected unit to show its reach.</summary>
        [SerializeField] Sprite rangeRingSprite;

        [Header("보드 글자")]
        [SerializeField] TMP_FontAsset boardFont;

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
        readonly List<GameObject> _labels = new();

        // Tutorial spotlight: a pulsing marker on the tile the step insists on, plus four
        // shade bands that darken everything around it. Kept apart from _highlights so the
        // marker is visible before the player has picked any card.
        SpriteRenderer _tutMarker;
        SpriteRenderer _rangeRing;
        readonly SpriteRenderer[] _shade = new SpriteRenderer[4];

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

        /// <summary>World space back to board coordinates, for tapping tiles.</summary>
        public Vector2 ToBoard(Vector3 world)
            => new((world.x - Origin.x) / TileSize, (Origin.y - world.y) / TileSize);

        public void Build(BattleSim sim)
        {
            _sim = sim;
            var stage = sim.Stage;
            string floor = stage.Floor;

            // Centre the board on the origin so the camera needs no per-stage framing.
            Origin = new Vector2(-(stage.Cols - 1) * TileSize * 0.5f,
                                  (stage.Rows - 1) * TileSize * 0.5f);

            floorMap.ClearAllTiles();
            overlayMap.ClearAllTiles();
            ClearLabels();

            for (int r = 0; r < stage.Rows; r++)
                for (int c = 0; c < stage.Cols; c++)
                {
                    var cell = new Vector3Int(c, -r, 0);
                    char ch = stage.Tiles[r][c];

                    floorMap.SetTile(cell,
                        ch == BattleField.Wall ? wallTile :
                        ch == BattleField.Furniture ? (floor == "kitchen" ? furnitureKitchenTile : furnitureTile) :
                        FloorTile(floor, c, r));

                    TileBase overlay = ch switch
                    {
                        BattleField.Stove => stoveTile,
                        BattleField.Wet => wetTile,
                        BattleField.Goal => GoalTile(floor),
                        _ => null,
                    };
                    if (overlay != null) overlayMap.SetTile(cell, overlay);
                }

            // Entrances: a vent on each path start, numbered the way the waves are.
            for (int i = 0; i < stage.Paths.Length; i++)
            {
                var path = stage.Paths[i];
                overlayMap.SetTile(new Vector3Int(path.StartX, -path.StartY, 0), ventTile);
                string name = i < stage.SpawnNames.Length ? stage.SpawnNames[i] : string.Empty;
                AddLabel($"{i + 1} {name}", path.StartX + 0.5f, path.StartY + 0.7f, 0.2f,
                         new Color(0.29f, 0.35f, 0.40f));
            }

            AddLabel(stage.GoalName, stage.GoalX + 0.5f, stage.GoalY + 0.55f, 0.24f,
                     floor == "wood" ? Color.white : new Color(0.114f, 0.133f, 0.149f));

            // A tile sits at its cell's centre, so cell (c,-r) lands half a unit ABOVE the
            // cell corner while ToWorld puts the same tile's centre half a unit BELOW it.
            // Dropping the grid one unit makes the painted floor line up with the simulation;
            // without it every unit and enemy draws one row off its tile.
            floorMap.transform.parent.localPosition = new Vector3(Origin.x, Origin.y - TileSize, 0f);
        }

        /// <summary>Picks the floor sprite for a cell, reproducing the original's per-cell offsets.</summary>
        TileBase FloorTile(string floor, int col, int row)
        {
            if (floor == "kitchen" && floorKitchenTiles is { Length: > 0 })
                return floorKitchenTiles[((col + row) % 2 == 0 ? 0 : 1) % floorKitchenTiles.Length];
            if (floor == "bath") return floorBathTile;
            if (floorWoodTiles is { Length: > 0 })
                return floorWoodTiles[(((col * 7 + row * 3) % 3) + 3) % 3 % floorWoodTiles.Length];
            return floorBathTile;
        }

        TileBase GoalTile(string floor) => floor switch
        {
            "kitchen" => goalKitchenTile,
            "bath" => goalBathTile,
            _ => goalWoodTile,
        };

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

        // ------------------------------------------------------------------ entities

        public void Sync(float presentationTime)
        {
            if (_sim == null) return;
            SyncUnits();
            SyncEnemies(presentationTime);
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
                    h.color = legal
                        ? new Color(0.24f, 0.55f, 0.85f, 0.42f)   // placeable
                        : new Color(0.11f, 0.13f, 0.15f, 0.30f);  // exclusion zone
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
            float radius = _sim.DisplayRange(u);
            _rangeRing.enabled = radius > 0f;
            _rangeRing.color = new Color(0.910f, 0.439f, 0.102f, 0.85f);   // UiTheme.Primary
            _rangeRing.transform.localPosition = ToWorld(u.X, u.Y);
            // The sprite is a unit circle, so the diameter is twice the radius in tiles.
            _rangeRing.transform.localScale = Vector3.one * (radius * 2f);
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
            ClearLabels();
            if (_tutMarker != null) _tutMarker.enabled = false;
            if (_rangeRing != null) _rangeRing.enabled = false;
            foreach (var b in _shade) if (b != null) b.enabled = false;
            _sim = null;
        }
    }
}
