// Rebuilds every generated asset: TMP fonts, tiles, prefabs, the sprite library and the scene.
//
// The whole playable setup is reproducible from source. Running
//   DustZero > Rebuild Project Assets
// (or -executeMethod DustZero.Editor.ProjectBuilder.RebuildAll in batch mode) regenerates
// it, so nothing depends on inspector work that only exists on one machine.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DustZero.Game;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

namespace DustZero.Editor
{
    public static class ProjectBuilder
    {
        const string Root = "Assets/DustZero";
        const string PrefabFolder = Root + "/Prefabs";
        const string SceneFolder = Root + "/Scenes";
        const string SettingsFolder = Root + "/Settings";
        const string FontFolder = Root + "/Fonts";
        const string ScenePath = SceneFolder + "/Main.unity";

        // Palette lifted from the original's CSS custom properties.
        static readonly Color Ink = new(0.114f, 0.133f, 0.149f);
        static readonly Color Surface = new(1f, 1f, 1f);
        static readonly Color PageBg = new(0.964f, 0.945f, 0.914f);
        static readonly Color Accent = new(1f, 0.604f, 0.235f);
        static readonly Color Blue = new(0.239f, 0.545f, 0.851f);
        static readonly Color Danger = new(0.878f, 0.322f, 0.290f);
        static readonly Color Muted = new(0.42f, 0.45f, 0.48f);

        [MenuItem("DustZero/Rebuild Project Assets")]
        public static void RebuildAll()
        {
            EnsureFolders();
            var fonts = BuildFonts();
            var art = BuildShapes();
            var tiles = BuildTiles(art);
            var library = BuildSpriteLibrary();
            var prefabs = BuildPrefabs(art, fonts, library);
            BuildScene(art, tiles, fonts, library, prefabs);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[DustZero] 프로젝트 에셋을 다시 생성했습니다.");
        }

        static void EnsureFolders()
        {
            foreach (var f in new[] { "Prefabs", "Scenes", "Settings", "Fonts", "Art" })
                if (!AssetDatabase.IsValidFolder($"{Root}/{f}"))
                    AssetDatabase.CreateFolder(Root, f);
            GeneratedArt.EnsureFolder();
        }

        // ------------------------------------------------------------------ fonts

        public sealed class Fonts
        {
            public TMP_FontAsset Title;
            public TMP_FontAsset Body;
            public TMP_FontAsset Number;
        }

        /// <summary>
        /// Creates dynamic TMP assets from the bundled OFL fonts. Dynamic atlases matter for
        /// Korean: the glyph set is far too large to pre-bake, so characters are rasterized on
        /// demand as the tables supply them.
        /// </summary>
        static Fonts BuildFonts()
        {
            var fonts = new Fonts
            {
                Title = MakeFont("DoHyeon-Regular", "DustZero-Title"),
                Body = MakeFont("IBMPlexSansKR-Regular", "DustZero-Body"),
                Number = MakeFont("IBMPlexSansKR-SemiBold", "DustZero-Number"),
            };

            // Fall back to the body face so a missing file can never leave text invisible.
            fonts.Title ??= fonts.Body;
            fonts.Number ??= fonts.Body;
            return fonts;
        }

        static TMP_FontAsset MakeFont(string ttfName, string assetName)
        {
            var ttfPath = $"{FontFolder}/{ttfName}.ttf";
            var source = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
            if (source == null)
            {
                Debug.LogWarning($"[DustZero] 폰트 파일이 없습니다: {ttfPath}");
                return null;
            }

            var assetPath = $"{FontFolder}/{assetName} SDF.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null) return existing;

            var font = TMP_FontAsset.CreateFontAsset(
                source,
                samplingPointSize: 72,
                atlasPadding: 7,
                renderMode: UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                atlasWidth: 1024, atlasHeight: 1024,
                atlasPopulationMode: AtlasPopulationMode.Dynamic,
                enableMultiAtlasSupport: true);

            font.name = assetName + " SDF";
            AssetDatabase.CreateAsset(font, assetPath);

            // The atlas texture and material are sub-assets of the font.
            if (font.atlasTextures != null)
                foreach (var tex in font.atlasTextures)
                    if (tex != null && !AssetDatabase.IsSubAsset(tex)) AssetDatabase.AddObjectToAsset(tex, font);
            if (font.material != null && !AssetDatabase.IsSubAsset(font.material))
                AssetDatabase.AddObjectToAsset(font.material, font);

            AssetDatabase.SaveAssets();
            return font;
        }

        // ------------------------------------------------------------------ shapes & tiles

        public sealed class Shapes
        {
            public Sprite Square, Circle, Outline, RangeRing;
        }

        static Shapes BuildShapes() => new()
        {
            Square = GeneratedArt.Square("shape_square"),
            Circle = GeneratedArt.Circle("shape_circle"),
            Outline = GeneratedArt.RoundedOutline("shape_outline"),
            RangeRing = GeneratedArt.DashedRing("shape_range_ring"),
        };

        public sealed class Tiles
        {
            /// <summary>Plank and checker variants, indexed the way TilePainter computes them.</summary>
            public TileBase[] Wood, Kitchen;
            public TileBase Bath, Wall, Stove, Wet, Vent;
            public TileBase Furniture, FurnitureKitchen;
            public TileBase GoalWood, GoalKitchen, GoalBath;
        }

        /// <summary>
        /// Bakes the original's painted floors into tile assets. The tint stays white because
        /// the colour now lives in the sprite itself rather than in a flat Tile.color.
        /// </summary>
        static Tiles BuildTiles(Shapes art)
        {
            var wood = new TileBase[TilePainter.WoodVariants];
            for (int i = 0; i < wood.Length; i++)
                wood[i] = MakeTile($"tile_wood_{i}", TilePainter.Floor("wood", i), Color.white);
            var kitchen = new TileBase[TilePainter.KitchenVariants];
            for (int i = 0; i < kitchen.Length; i++)
                kitchen[i] = MakeTile($"tile_kitchen_{i}", TilePainter.Floor("kitchen", i), Color.white);

            return new Tiles
            {
                Wood = wood,
                Kitchen = kitchen,
                Bath = MakeTile("tile_bath", TilePainter.Floor("bath", 0), Color.white),
                Wall = MakeTile("tile_wall", TilePainter.Wall(), Color.white),
                Furniture = MakeTile("tile_furniture", TilePainter.Furniture("wood"), Color.white),
                FurnitureKitchen = MakeTile("tile_furniture_kitchen", TilePainter.Furniture("kitchen"), Color.white),
                Stove = MakeTile("tile_stove", TilePainter.Stove(), Color.white),
                Wet = MakeTile("tile_wet", TilePainter.Wet(), Color.white),
                Vent = MakeTile("tile_vent", TilePainter.Vent(), Color.white),
                GoalWood = MakeTile("tile_goal_wood", TilePainter.Goal("wood"), Color.white),
                GoalKitchen = MakeTile("tile_goal_kitchen", TilePainter.Goal("kitchen"), Color.white),
                GoalBath = MakeTile("tile_goal_bath", TilePainter.Goal("bath"), Color.white),
            };
        }

        static TileBase MakeTile(string name, Sprite sprite, Color color)
        {
            var path = $"{SettingsFolder}/{name}.asset";
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(tile, path);
            }
            tile.sprite = sprite;
            tile.color = color;
            tile.colliderType = Tile.ColliderType.None;
            EditorUtility.SetDirty(tile);
            return tile;
        }

        // ------------------------------------------------------------------ sprite library

        /// <summary>
        /// Reads the shipped tables so editor tooling derives ids from data rather than from
        /// lists kept in code. Throws with the table problems if the data is inconsistent.
        /// </summary>
        static DustZero.Data.GameDatabase LoadDatabaseForEditor()
        {
            var dir = Path.Combine(Application.streamingAssetsPath, "data");
            var json = new Dictionary<string, string>();
            foreach (var name in DustZero.Data.RawTables.Names)
            {
                var p = Path.Combine(dir, name + ".json");
                if (File.Exists(p)) json[name] = File.ReadAllText(p);
            }
            return DustZero.Data.TableLoader.LoadAndBuild(new DustZero.Data.DictionaryTableSource(json));
        }

        static SpriteLibrary BuildSpriteLibrary()
        {
            const string path = SettingsFolder + "/SpriteLibrary.asset";
            var lib = AssetDatabase.LoadAssetAtPath<SpriteLibrary>(path);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<SpriteLibrary>();
                AssetDatabase.CreateAsset(lib, path);
            }

            Sprite Load(string file)
                => AssetDatabase.LoadAssetAtPath<Sprite>($"{Root}/Art/Sprites/{file}.png");

            // Ids come from the shipped tables, never a list kept in this file: adding an
            // appliance or an enemy to the spreadsheet must not need an edit here as well.
            var db = LoadDatabaseForEditor();

            lib.units = db.UnitOrder
                .Select(id => new SpriteEntry { id = id, sprite = Load($"unit_{id}") })
                .Where(e => e.sprite != null).ToArray();

            // Per-enemy art where it exists, with look/boss variants as the fallback chain.
            lib.enemies = db.Enemies.Keys
                .Select(id => new SpriteEntry { id = id, sprite = Load($"enemy_{id}") })
                .Where(e => e.sprite != null).ToArray();

            var looks = db.Enemies.Values.Select(e => e.Look).Where(l => !string.IsNullOrEmpty(l)).Distinct();
            lib.enemyLooks = looks
                .Select(look => new SpriteEntry { id = look, sprite = Load($"enemy_{look}") })
                .Where(e => e.sprite != null).ToArray();

            var bossLooks = db.Enemies.Values.Where(e => e.Boss)
                .Select(e => e.Look).Where(l => !string.IsNullOrEmpty(l)).Distinct();
            lib.bossLooks = bossLooks
                .Select(look => new SpriteEntry { id = look, sprite = Load($"enemy_boss_{look}") })
                .Where(e => e.sprite != null).ToArray();

            var missingUnits = db.UnitOrder.Where(id => Load($"unit_{id}") == null).ToList();
            var missingEnemies = db.Enemies.Keys
                .Where(id => Load($"enemy_{id}") == null &&
                             Load($"enemy_{db.Enemies[id].Look}") == null).ToList();
            if (missingUnits.Count > 0 || missingEnemies.Count > 0)
                Debug.LogWarning("스프라이트 없음 — 가전: " + string.Join(", ", missingUnits) +
                                 " / 적: " + string.Join(", ", missingEnemies) +
                                 "  (GameData/spriteexport/one.html 로 추출하세요)");

            EditorUtility.SetDirty(lib);
            return lib;
        }

        // ------------------------------------------------------------------ prefabs

        public sealed class Prefabs
        {
            public EntityView Entity;
            public FxParticle Particle;
            public FxLabel Label;
            public DeckCard DeckCard;
            public StageRow StageRow;
            public SpriteRenderer Highlight;
        }

        static Prefabs BuildPrefabs(Shapes art, Fonts fonts, SpriteLibrary library)
        {
            var p = new Prefabs
            {
                Entity = BuildEntityPrefab(art),
                Particle = BuildParticlePrefab(art),
                Label = BuildLabelPrefab(fonts),
                Highlight = BuildHighlightPrefab(art),
            };
            p.DeckCard = BuildDeckCardPrefab(art, fonts, library);
            p.StageRow = BuildStageRowPrefab(art, fonts);
            return p;
        }

        static T SavePrefab<T>(GameObject go, string name) where T : Component
        {
            var path = $"{PrefabFolder}/{name}.prefab";
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);

            // Re-load from disk rather than keeping the in-memory result: opening a new scene
            // later invalidates the reference returned by SaveAsPrefabAsset, which would wire
            // a null into the scene.
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) { Debug.LogError($"[DustZero] 프리팹 저장 실패: {path}"); return null; }
            return asset.GetComponent<T>();
        }

        /// <summary>Re-resolves every prefab from disk, for use after the scene is swapped.</summary>
        static Prefabs ReloadPrefabs()
        {
            T Load<T>(string name) where T : Component
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{name}.prefab");
                if (go == null) { Debug.LogError($"[DustZero] 프리팹을 찾지 못했습니다: {name}"); return null; }
                return go.GetComponent<T>();
            }
            return new Prefabs
            {
                Entity = Load<EntityView>("Entity"),
                Particle = Load<FxParticle>("FxParticle"),
                Label = Load<FxLabel>("FxLabel"),
                DeckCard = Load<DeckCard>("DeckCard"),
                StageRow = Load<StageRow>("StageRow"),
                Highlight = Load<SpriteRenderer>("TileHighlight"),
            };
        }

        static EntityView BuildEntityPrefab(Shapes art)
        {
            var go = new GameObject("Entity");
            var view = go.AddComponent<EntityView>();

            var body = new GameObject("Body").transform;
            body.SetParent(go.transform, false);
            view.Body = body.gameObject.AddComponent<SpriteRenderer>();
            view.Body.sortingOrder = 100;

            var bars = new GameObject("Bars").transform;
            bars.SetParent(go.transform, false);
            view.BarRoot = bars;

            // Bars scale on X from a left anchor, so fill grows rightwards.
            // Offsets are the original's, converted from canvas pixels to tiles: a unit's heat
            // and skill bars sit near the BOTTOM edge of its tile (by = r*k + k - 7), while an
            // enemy's health floats above its head (y - r*1.35). Putting them all overhead made
            // a deployed appliance read as if it stood one row higher than it does.
            SpriteRenderer Bar(string name, float y, float width, Color c, out SpriteRenderer fill)
            {
                var track = new GameObject(name).transform;
                track.SetParent(bars, false);
                track.localPosition = new Vector3(0f, y, 0f);

                var bg = track.gameObject.AddComponent<SpriteRenderer>();
                bg.sprite = art.Square;
                bg.color = new Color(0f, 0f, 0f, 0.35f);
                bg.sortingOrder = 500;
                track.localScale = new Vector3(width, 0.05f, 1f);

                var anchor = new GameObject("Fill").transform;
                anchor.SetParent(track, false);
                anchor.localPosition = new Vector3(-0.5f, 0f, 0f);

                var pivot = new GameObject("Pivot").transform;
                pivot.SetParent(anchor, false);
                pivot.localPosition = new Vector3(0.5f, 0f, 0f);

                fill = pivot.gameObject.AddComponent<SpriteRenderer>();
                fill.sprite = art.Square;
                fill.color = c;
                fill.sortingOrder = 501;
                return fill;
            }

            Bar("Heat", -0.31f, 0.70f, Accent, out var heat);
            Bar("Skill", -0.39f, 0.70f, Blue, out var skill);
            Bar("Hp", 0.28f, 0.38f, Danger, out var hp);
            view.HeatFill = heat;
            view.SkillFill = skill;
            view.HpFill = hp;

            return SavePrefab<EntityView>(go, "Entity");
        }

        static FxParticle BuildParticlePrefab(Shapes art)
        {
            var go = new GameObject("FxParticle");
            var p = go.AddComponent<FxParticle>();
            p.Renderer = go.AddComponent<SpriteRenderer>();
            p.Renderer.sprite = art.Circle;
            p.Renderer.sortingOrder = 800;
            return SavePrefab<FxParticle>(go, "FxParticle");
        }

        static FxLabel BuildLabelPrefab(Fonts fonts)
        {
            var go = new GameObject("FxLabel");
            var l = go.AddComponent<FxLabel>();
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.font = fonts.Number;
            tmp.fontSize = 3.2f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.sortingOrder = 900;
            tmp.rectTransform.sizeDelta = new Vector2(4f, 1f);
            l.Text = tmp;
            return SavePrefab<FxLabel>(go, "FxLabel");
        }

        static SpriteRenderer BuildHighlightPrefab(Shapes art)
        {
            var go = new GameObject("TileHighlight");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = art.Outline;
            sr.color = new Color(Blue.r, Blue.g, Blue.b, 0.42f);
            sr.sortingOrder = 50;
            return SavePrefab<SpriteRenderer>(go, "TileHighlight");
        }

        // ------------------------------------------------------------------ UI helpers

        static GameObject UI(string name, Transform parent, out RectTransform rt)
        {
            var go = new GameObject(name, typeof(RectTransform));
            rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            return go;
        }

        static Image Panel(string name, Transform parent, Color color, out RectTransform rt)
        {
            var go = UI(name, parent, out rt);
            var img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        static TextMeshProUGUI Label(string name, Transform parent, TMP_FontAsset font,
                                     float size, Color color, TextAlignmentOptions align,
                                     out RectTransform rt, string text = "")
        {
            var go = UI(name, parent, out rt);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.text = text;
            t.raycastTarget = false;
            return t;
        }

        static Button Btn(string name, Transform parent, Shapes art, TMP_FontAsset font,
                          string caption, Color bg, Color fg, float fontSize,
                          out RectTransform rt, out TextMeshProUGUI label)
        {
            var go = UI(name, parent, out rt);
            var img = go.AddComponent<Image>();
            img.sprite = art.Square;
            img.type = Image.Type.Sliced;
            img.color = bg;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.9f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.disabledColor = new Color(1f, 1f, 1f, 0.35f);
            colors.fadeDuration = 0.08f;
            btn.colors = colors;

            label = Label("Label", go.transform, font, fontSize, fg, TextAlignmentOptions.Center, out var lrt, caption);
            Stretch(lrt);
            return btn;
        }

        static void Stretch(RectTransform rt, float pad = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pad, pad);
            rt.offsetMax = new Vector2(-pad, -pad);
        }

        static void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
        {
            rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = offMin; rt.offsetMax = offMax;
        }

        // ------------------------------------------------------------------ item prefabs

        static DeckCard BuildDeckCardPrefab(Shapes art, Fonts fonts, SpriteLibrary library)
        {
            var go = UI("DeckCard", null, out var rt);
            rt.sizeDelta = new Vector2(104f, 132f);

            var bg = go.AddComponent<Image>();
            bg.sprite = art.Square;
            bg.color = Surface;

            var group = go.AddComponent<CanvasGroup>();
            var button = go.AddComponent<Button>();
            button.targetGraphic = bg;

            var stripe = Panel("Rarity", go.transform, Blue, out var srt);
            Anchor(srt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -5f), Vector2.zero);

            var portraitGo = UI("Portrait", go.transform, out var prt);
            Anchor(prt, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(6f, 26f), new Vector2(-6f, -8f));
            var portrait = portraitGo.AddComponent<Image>();
            portrait.preserveAspect = true;
            portrait.raycastTarget = false;

            var charge = Panel("Charge", go.transform, new Color(Accent.r, Accent.g, Accent.b, 0.30f), out var crt);
            Anchor(crt, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 24f));
            charge.type = Image.Type.Filled;
            charge.fillMethod = Image.FillMethod.Horizontal;
            charge.raycastTarget = false;

            var name = Label("Name", go.transform, fonts.Body, 17f, Ink, TextAlignmentOptions.Center, out var nrt, "가전");
            Anchor(nrt, Vector2.zero, new Vector2(1f, 0f), new Vector2(2f, 12f), new Vector2(-2f, 32f));

            var cost = Label("Cost", go.transform, fonts.Number, 16f, Muted, TextAlignmentOptions.Center, out var cort, "0W");
            Anchor(cort, Vector2.zero, new Vector2(1f, 0f), new Vector2(2f, 0f), new Vector2(-2f, 16f));

            // The veil and its text must be separate objects: a GameObject can hold only one Graphic.
            var cdVeil = Panel("CooldownVeil", go.transform, new Color(0f, 0f, 0f, 0.55f), out var cvrt);
            cdVeil.raycastTarget = false;
            Stretch(cvrt);
            var cd = Label("Cooldown", cdVeil.transform, fonts.Number, 26f, Color.white,
                           TextAlignmentOptions.Center, out var cdrt, "");
            Stretch(cdrt);

            var outline = Panel("Selected", go.transform, new Color(Accent.r, Accent.g, Accent.b, 0.85f), out var ort);
            outline.sprite = art.Outline;
            outline.type = Image.Type.Simple;
            outline.raycastTarget = false;
            Stretch(ort);

            var card = go.AddComponent<DeckCard>();
            Wire(card, ("button", button), ("portrait", portrait), ("rarityStripe", stripe),
                 ("nameLabel", name), ("costLabel", cost),
                 ("cooldownRoot", cdVeil.gameObject), ("cooldownLabel", cd),
                 ("chargeFill", charge), ("selectedOutline", outline), ("group", group),
                 ("sprites", library));

            return SavePrefab<DeckCard>(go, "DeckCard");
        }

        static StageRow BuildStageRowPrefab(Shapes art, Fonts fonts)
        {
            var go = UI("StageRow", null, out var rt);
            rt.sizeDelta = new Vector2(0f, 104f);

            var bg = go.AddComponent<Image>();
            bg.sprite = art.Square;
            bg.color = Surface;
            go.AddComponent<CanvasGroup>();

            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 104f;
            le.preferredHeight = 104f;

            // Anchor each field to a fixed band of the row. Mixing half-height anchors with
            // pixel offsets made the top and bottom rows overlap once the layout group set
            // the final height, so every field here is pinned to the full row height instead.
            var no = Label("No", go.transform, fonts.Title, 26f, Accent, TextAlignmentOptions.Left, out var nort, "1-1");
            Anchor(nort, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(16f, 46f), new Vector2(110f, -8f));

            var name = Label("Name", go.transform, fonts.Body, 21f, Ink, TextAlignmentOptions.Left, out var nrt, "거실");
            Anchor(nrt, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(110f, 46f), new Vector2(-290f, -8f));

            var detail = Label("Detail", go.transform, fonts.Body, 16f, Muted, TextAlignmentOptions.Left, out var drt, "");
            Anchor(drt, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(16f, 12f), new Vector2(-290f, 44f));

            var stars = Label("Stars", go.transform, fonts.Body, 22f, Accent, TextAlignmentOptions.Right, out var strt, "☆☆☆");
            Anchor(strt, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-288f, 46f), new Vector2(-132f, -8f));

            var boss = Panel("Boss", go.transform, Danger, out var brt);
            boss.sprite = art.Square;
            Anchor(brt, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(5f, 0f));

            var play = Btn("Play", go.transform, art, fonts.Body, "도전", Accent, Color.white, 19f,
                           out var playRt, out var playLabel);
            Anchor(playRt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-124f, -30f), new Vector2(-16f, 30f));

            var row = go.AddComponent<StageRow>();
            Wire(row, ("noLabel", no), ("nameLabel", name), ("starsLabel", stars), ("detailLabel", detail),
                 ("playButton", play), ("playLabel", playLabel), ("bossMark", boss));

            return SavePrefab<StageRow>(go, "StageRow");
        }

        /// <summary>Assigns private [SerializeField] references without making them public.</summary>
        static void Wire(Object target, params (string field, Object value)[] pairs)
        {
            var so = new SerializedObject(target);
            foreach (var (field, value) in pairs)
            {
                var prop = so.FindProperty(field);
                if (prop == null) { Debug.LogWarning($"[DustZero] {target.GetType().Name}.{field} 를 찾지 못했습니다."); continue; }
                prop.objectReferenceValue = value;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Assigns an object array field, for the tile variants the floors cycle through.</summary>
        static void WireArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) { Debug.LogWarning($"[DustZero] {target.GetType().Name}.{field} 를 찾지 못했습니다."); return; }
            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ scene

        static void BuildScene(Shapes art, Tiles tiles, Fonts fonts, SpriteLibrary library, Prefabs prefabs)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Prefab references obtained before the scene swap are no longer valid.
            prefabs = ReloadPrefabs();
            Debug.Log($"[DustZero] prefab resolve: Entity={prefabs.Entity != null} Particle={prefabs.Particle != null} " +
                      $"Label={prefabs.Label != null} DeckCard={prefabs.DeckCard != null} " +
                      $"StageRow={prefabs.StageRow != null} Highlight={prefabs.Highlight != null}");

            // ---- camera
            var camGo = new GameObject("BoardCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 7.2f;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = PageBg;
            camGo.tag = "MainCamera";

            // ---- event system
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();

            // ---- board
            var boardGo = new GameObject("Board");
            var gridGo = new GameObject("Grid");
            gridGo.transform.SetParent(boardGo.transform, false);
            var grid = gridGo.AddComponent<Grid>();
            grid.cellSize = new Vector3(BoardView.TileSize, BoardView.TileSize, 0f);

            Tilemap MakeMap(string name, int order)
            {
                var go = new GameObject(name);
                go.transform.SetParent(gridGo.transform, false);
                var map = go.AddComponent<Tilemap>();
                var rend = go.AddComponent<TilemapRenderer>();
                rend.sortingOrder = order;
                return map;
            }
            var floorMap = MakeMap("Floor", 0);
            var overlayMap = MakeMap("Overlay", 10);

            var entityRoot = new GameObject("Entities").transform;
            entityRoot.SetParent(boardGo.transform, false);
            var highlightRoot = new GameObject("Highlights").transform;
            highlightRoot.SetParent(boardGo.transform, false);
            var labelRoot = new GameObject("Labels").transform;
            labelRoot.SetParent(boardGo.transform, false);

            var board = boardGo.AddComponent<BoardView>();
            Wire(board, ("floorMap", floorMap), ("overlayMap", overlayMap),
                 ("entityRoot", entityRoot), ("entityPrefab", prefabs.Entity),
                 ("sprites", library), ("highlightPrefab", prefabs.Highlight),
                 ("highlightRoot", highlightRoot), ("labelRoot", labelRoot),
                 ("solidSprite", art.Square), ("rangeRingSprite", art.RangeRing),
                 ("floorBathTile", tiles.Bath), ("wallTile", tiles.Wall),
                 ("furnitureTile", tiles.Furniture), ("furnitureKitchenTile", tiles.FurnitureKitchen),
                 ("stoveTile", tiles.Stove), ("wetTile", tiles.Wet), ("ventTile", tiles.Vent),
                 ("goalWoodTile", tiles.GoalWood), ("goalKitchenTile", tiles.GoalKitchen),
                 ("goalBathTile", tiles.GoalBath), ("boardFont", fonts.Body));
            WireArray(board, "floorWoodTiles", tiles.Wood);
            WireArray(board, "floorKitchenTiles", tiles.Kitchen);

            // ---- fx
            var fxGo = new GameObject("FxDirector");
            var fxRoot = new GameObject("FxRoot").transform;
            fxRoot.SetParent(fxGo.transform, false);
            var fx = fxGo.AddComponent<FxDirector>();
            Wire(fx, ("root", fxRoot), ("particlePrefab", prefabs.Particle), ("labelPrefab", prefabs.Label),
                 ("roundSprite", art.Circle), ("squareSprite", art.Square),
                 ("numberFont", fonts.Number), ("shakeCamera", cam));

            // ---- canvas
            var canvasGo = new GameObject("UI");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            // Match height: the board is framed by height, so UI bands keep their proportion.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var safe = UI("SafeArea", canvasGo.transform, out var safeRt);
            Stretch(safeRt);
            safe.AddComponent<SafeAreaFitter>();

            var lobby = BuildLobbyUI(safe.transform, art, fonts, library, prefabs);
            var battle = BuildBattleUI(safe.transform, art, fonts, prefabs, board, fx, cam);

            // ---- boot overlay
            var bootGo = UI("Boot", safe.transform, out var bootRt);
            Stretch(bootRt);
            var bootBg = bootGo.AddComponent<Image>();
            bootBg.color = PageBg;
            var bootText = Label("BootText", bootGo.transform, fonts.Body, 24f, Ink,
                                 TextAlignmentOptions.Center, out var btrt, "데이터 테이블을 불러오는 중…");
            Stretch(btrt, 48f);
            bootText.enableWordWrapping = true;

            // ---- app root
            var appGo = new GameObject("AppRoot");
            var app = appGo.AddComponent<AppRoot>();
            Wire(app, ("lobby", lobby), ("battle", battle),
                 ("bootRoot", bootGo), ("bootLabel", bootText));

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static LobbyScreen BuildLobbyUI(Transform parent, Shapes art, Fonts fonts,
                                        SpriteLibrary library, Prefabs prefabs)
        {
            var go = UI("Lobby", parent, out var rt);
            Stretch(rt);
            var bg = go.AddComponent<Image>();
            bg.color = PageBg;

            // ---- top bar
            var top = Panel("TopBar", go.transform, Surface, out var toprt);
            Anchor(toprt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -96f), Vector2.zero);

            Label("Brand", top.transform, fonts.Title, 30f, Ink, TextAlignmentOptions.Left, out var brt, "더스트 제로");
            Anchor(brt, new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(20f, 0f), Vector2.zero);

            var coin = Label("Coin", top.transform, fonts.Number, 24f, Ink, TextAlignmentOptions.Right, out var crt, "0");
            Anchor(crt, new Vector2(0.4f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(-250f, 0f));
            Label("CoinTag", top.transform, fonts.Body, 15f, Muted, TextAlignmentOptions.Left, out var ctrt, "코인");
            Anchor(ctrt, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-246f, 0f), new Vector2(-196f, 0f));

            var crystal = Label("Crystal", top.transform, fonts.Number, 24f, Blue, TextAlignmentOptions.Right, out var xrt, "0");
            Anchor(xrt, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-192f, 0f), new Vector2(-86f, 0f));
            Label("CrystalTag", top.transform, fonts.Body, 15f, Muted, TextAlignmentOptions.Left, out var xtrt, "크리스탈");
            Anchor(xtrt, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-82f, 0f), new Vector2(-10f, 0f));

            // ---- representative strip
            var repPanel = Panel("Rep", go.transform, Surface, out var rprt);
            Anchor(rprt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -236f), new Vector2(-12f, -104f));

            var repGo = UI("RepPortrait", repPanel.transform, out var rpprt);
            Anchor(rpprt, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(10f, 8f), new Vector2(130f, -8f));
            var repImg = repGo.AddComponent<Image>();
            repImg.preserveAspect = true;
            repImg.raycastTarget = false;

            var repName = Label("RepName", repPanel.transform, fonts.Body, 22f, Ink, TextAlignmentOptions.Left, out var rnrt, "");
            Anchor(rnrt, new Vector2(0f, 0.5f), new Vector2(1f, 1f), new Vector2(142f, -8f), new Vector2(-12f, -16f));

            var attendance = Btn("Attendance", repPanel.transform, art, fonts.Body, "출석 보상 받기", Blue, Color.white, 19f,
                                 out var art2, out var attLabel);
            Anchor(art2, new Vector2(0f, 0f), new Vector2(1f, 0.5f), new Vector2(142f, 12f), new Vector2(-12f, -8f));

            // ---- idle panel
            var idle = Panel("Idle", go.transform, Surface, out var irt);
            Anchor(irt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -404f), new Vector2(-12f, -244f));

            Label("IdleTitle", idle.transform, fonts.Body, 21f, Ink, TextAlignmentOptions.Left, out var itrt, "자동 청소");
            Anchor(itrt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(14f, -34f), new Vector2(-14f, -6f));

            var idleSub = Label("IdleSub", idle.transform, fonts.Body, 15f, Muted, TextAlignmentOptions.Left, out var isrt, "");
            Anchor(isrt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(14f, -58f), new Vector2(-14f, -34f));

            var idleAmount = Label("IdleAmount", idle.transform, fonts.Number, 19f, Ink, TextAlignmentOptions.Left, out var iart, "");
            Anchor(iart, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(14f, 70f), new Vector2(-14f, 98f));

            var barBg = Panel("BarBg", idle.transform, new Color(0f, 0f, 0f, 0.10f), out var bbrt);
            Anchor(bbrt, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(14f, 52f), new Vector2(-14f, 64f));
            var idleBar = Panel("BarFill", barBg.transform, Accent, out var bfrt);
            Stretch(bfrt);
            idleBar.type = Image.Type.Filled;
            idleBar.fillMethod = Image.FillMethod.Horizontal;

            var idleClaim = Btn("IdleClaim", idle.transform, art, fonts.Body, "받기", Accent, Color.white, 19f,
                                out var icrt, out _);
            Anchor(icrt, new Vector2(0f, 0f), new Vector2(0.62f, 0f), new Vector2(14f, 8f), new Vector2(-6f, 46f));

            var idleTest = Btn("IdleTest", idle.transform, art, fonts.Body, "테스트 +1시간", Muted, Color.white, 15f,
                               out var itrt2, out _);
            Anchor(itrt2, new Vector2(0.62f, 0f), new Vector2(1f, 0f), new Vector2(6f, 8f), new Vector2(-14f, 46f));

            // ---- stage list
            var scrollGo = UI("StageScroll", go.transform, out var scrollRt);
            Anchor(scrollRt, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(12f, 128f), new Vector2(-12f, -412f));
            var scroll = scrollGo.AddComponent<ScrollRect>();
            scrollGo.AddComponent<RectMask2D>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 30f;

            var content = UI("Content", scrollGo.transform, out var contentRt);
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            // Zero the horizontal offsets so content width tracks the viewport exactly.
            contentRt.offsetMin = new Vector2(0f, contentRt.offsetMin.y);
            contentRt.offsetMax = new Vector2(0f, contentRt.offsetMax.y);
            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f;
            vlg.childControlHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = contentRt;
            scroll.viewport = scrollRt;

            // ---- system row
            var tutReplay = Btn("TutorialReplay", go.transform, art, fonts.Body, "튜토리얼 다시 보기", Muted, Color.white, 17f,
                                out var trrt, out _);
            Anchor(trrt, new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(12f, 72f), new Vector2(-6f, 118f));

            var reset = Btn("Reset", go.transform, art, fonts.Body, "게임 초기화", Danger, Color.white, 17f,
                            out var rsrt, out var resetLabel);
            Anchor(rsrt, new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(6f, 72f), new Vector2(-12f, 118f));

            var build = Label("Build", go.transform, fonts.Body, 14f, Muted, TextAlignmentOptions.Center, out var brt2, "");
            Anchor(brt2, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(12f, 44f), new Vector2(-12f, 68f));

            // ---- toast
            var toastBg = Panel("Toast", go.transform, new Color(0.1f, 0.12f, 0.14f, 0.92f), out var tsrt);
            toastBg.raycastTarget = false;
            Anchor(tsrt, new Vector2(0.08f, 0f), new Vector2(0.92f, 0f), new Vector2(0f, 140f), new Vector2(0f, 196f));
            var toast = Label("ToastText", toastBg.transform, fonts.Body, 19f, Color.white,
                              TextAlignmentOptions.Center, out var ttrt2, "");
            Stretch(ttrt2, 12f);
            toast.enableWordWrapping = true;
            toastBg.gameObject.SetActive(false);

            var lobby = go.AddComponent<LobbyScreen>();
            Wire(lobby, ("coinLabel", coin), ("crystalLabel", crystal), ("repNameLabel", repName),
                 ("repPortrait", repImg), ("sprites", library),
                 ("idleRoot", idle.gameObject), ("idleSubLabel", idleSub), ("idleAmountLabel", idleAmount),
                 ("idleBar", idleBar), ("idleClaimButton", idleClaim), ("idleTestButton", idleTest),
                 ("attendanceButton", attendance), ("attendanceLabel", attLabel),
                 ("stageListRoot", contentRt), ("stageRowPrefab", prefabs.StageRow),
                 ("tutorialReplayButton", tutReplay), ("resetButton", reset), ("resetLabel", resetLabel),
                 ("toastRoot", toastBg.gameObject), ("toastLabel", toast), ("buildLabel", build));
            return lobby;
        }

        /// <summary>
        /// The selected-unit tray from redesign 1c: what is selected on the left, then boost,
        /// multitap and retreat, then a close button. Sits in the same band as the card row
        /// and is toggled against it, so the battle is never covered by a modal.
        /// </summary>
        static UnitActionBar BuildUnitActionBar(Transform parent, Fonts fonts)
        {
            var root = UiKit.Node("UnitActionBar", parent);
            // Uses the whole tray, including the hint band: the hint tells the player how to
            // place a card, which is not what they are doing while a unit is selected.
            Anchor(root, new Vector2(0f, 0f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.S10, UiTheme.S8), new Vector2(-UiTheme.S10, -UiTheme.S8));
            var bar = root.gameObject.AddComponent<UnitActionBar>();

            var layout = UiKit.Row(root, UiTheme.S8);
            layout.childForceExpandHeight = true;

            // Left read-out.
            UiKit.Panel("Info", root, out var inforRt, UiTheme.Panel2, UiTheme.CornerFor(UiTheme.ButtonTall));
            var infoFitter = inforRt.gameObject.AddComponent<LayoutElement>();
            infoFitter.preferredWidth = 300f;
            infoFitter.flexibleWidth = 1f;

            // Stacked down from the top on real line heights, so none of the three is ever
            // shorter than the glyphs it has to draw.
            float cursor = UiTheme.S8;
            TextMeshProUGUI InfoLine(string id, float size, Color ink, string placeholder)
            {
                float h = UiTheme.Line(size);
                var tmp = UiKit.Label(id, inforRt, fonts.Body, size, ink,
                                      TextAlignmentOptions.Left, out var lrt, placeholder);
                Anchor(lrt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(UiTheme.S12, -(cursor + h)), new Vector2(-UiTheme.S8, -cursor));
                cursor += h;
                return tmp;
            }

            var name = InfoLine("Name", UiTheme.Body, UiTheme.Ink, "가전");
            var meta = InfoLine("Meta", UiTheme.Caption, UiTheme.Ink2, "Lv.1");
            var status = InfoLine("Status", UiTheme.Caption, UiTheme.Ink2, "정상 작동");

            UiButton Action(string id, string text, UiButtonKind kind, float width)
            {
                var b = UiKit.Button(id, root, fonts.Body, text, kind, out var brt, UiTheme.ButtonTall);
                var le = brt.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = width;
                le.minWidth = UiTheme.Touch;
                return b;
            }

            // Wide enough for the longest cost the tables can produce ("2,000W") at caption
            // size: a truncated price is worse than a cramped layout.
            const float ActionWidth = 180f;
            var boost = Action("Boost", "강화", UiButtonKind.Primary, ActionWidth);
            var multitap = Action("Multitap", "멀티탭", UiButtonKind.Secondary, ActionWidth);
            var retreat = Action("Retreat", "철수", UiButtonKind.Secondary, ActionWidth);
            var close = Action("Close", "×", UiButtonKind.Secondary, UiTheme.Touch);

            bar.Bind(name, meta, status, boost, multitap, retreat, close);
            root.gameObject.SetActive(false);
            return bar;
        }

        static BattleScreen BuildBattleUI(Transform parent, Shapes art, Fonts fonts, Prefabs prefabs,
                                          BoardView board, FxDirector fx, Camera cam)
        {
            var go = UI("Battle", parent, out var rt);
            Stretch(rt);

            // ---- HUD top (redesign 1c): pause | wave + progress | remaining | speed,
            // with the cleanliness bar as its own row underneath.
            float hudH = UiTheme.Touch + UiTheme.Line(UiTheme.Display) + UiTheme.Pt(44f) + UiTheme.S24;
            var hud = UiKit.Panel("Hud", go.transform, out var hudrt, UiTheme.Panel, 1f);
            Anchor(hudrt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -hudH), Vector2.zero);

            float row1Top = -UiTheme.S12;
            float row1H = UiTheme.Touch;
            float pad = UiTheme.ScreenPad;
            float speedW = UiTheme.Pt(70f), remainW = UiTheme.Pt(62f);

            var pause = UiKit.Button("Pause", hudrt, fonts.Body, "\u23F8", UiButtonKind.Secondary,
                                     out var pzrt, UiTheme.Touch);
            Anchor(pzrt, new Vector2(0f, 1f), new Vector2(0f, 1f),
                   new Vector2(pad, row1Top - row1H), new Vector2(pad + UiTheme.Touch, row1Top));

            // No exit button up here: the design keeps the top row to pause, wave,
            // remaining and speed, and puts leaving behind the pause overlay.
            var speed = UiKit.Button("Speed", hudrt, fonts.Body, "\u00D71", UiButtonKind.Secondary,
                                     out var sprt, UiTheme.Touch, UiTheme.LabelSize);
            float speedRight = -pad;
            Anchor(sprt, new Vector2(1f, 1f), new Vector2(1f, 1f),
                   new Vector2(speedRight - speedW, row1Top - row1H), new Vector2(speedRight, row1Top));

            var remainChip = UiKit.Chip("Remain", hudrt, fonts.Number, "0", out var rcrt,
                                        UiTheme.Panel2, UiTheme.Ink, UiTheme.Touch, UiTheme.LabelSize);
            float remainRight = speedRight - speedW - UiTheme.S8;
            Anchor(rcrt, new Vector2(1f, 1f), new Vector2(1f, 1f),
                   new Vector2(remainRight - remainW, row1Top - row1H), new Vector2(remainRight, row1Top));

            // Wave line.
            float row2Top = row1Top - row1H - UiTheme.S6;
            float row2H = UiTheme.Line(UiTheme.Display);
            var waveLabel = UiKit.Label("Wave", hudrt, fonts.Title, UiTheme.Display, UiTheme.Ink,
                                        TextAlignmentOptions.Left, out var wlrt, "\uC6E8\uC774\uBE0C 1");
            Anchor(wlrt, new Vector2(0f, 1f), new Vector2(0.55f, 1f),
                   new Vector2(pad, row2Top - row2H), new Vector2(0f, row2Top));
            var waveSub = UiKit.Label("WaveSub", hudrt, fonts.Body, UiTheme.Caption, UiTheme.Ink2,
                                      TextAlignmentOptions.Left, out var wsrt, "/ 10");
            Anchor(wsrt, new Vector2(0.55f, 1f), new Vector2(1f, 1f),
                   new Vector2(0f, row2Top - row2H), new Vector2(-pad, row2Top));

            // Ten wave cells.
            float segTop = row2Top - row2H - UiTheme.S4;
            float segH = UiTheme.Pt(6f);
            var segRoot = UiKit.Node("WaveSegments", hudrt);
            Anchor(segRoot, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(pad, segTop - segH), new Vector2(-pad, segTop));
            var segLayout = UiKit.Row(segRoot, UiTheme.S4);
            segLayout.childForceExpandWidth = true;
            segLayout.childForceExpandHeight = true;
            const int WaveCells = 10;
            var segments = new Image[WaveCells];
            for (int i = 0; i < WaveCells; i++)
            {
                segments[i] = UiKit.Surface("Seg" + i, segRoot, new Color(0.89f, 0.84f, 0.78f),
                                            segH * 0.5f, out var segrt);
                var segLe = segrt.gameObject.AddComponent<LayoutElement>();
                segLe.flexibleWidth = 1f;
                segLe.preferredHeight = segH;
            }

            // Cleanliness row.
            float cleanMid = segTop - segH - UiTheme.S8 - UiTheme.Pt(15f);
            float barHalf = UiTheme.Pt(9f);
            var cleanTag = UiKit.Label("CleanTag", hudrt, fonts.Body, UiTheme.LabelSize, UiTheme.Ink2,
                                       TextAlignmentOptions.Left, out var ctrt, "\uCCAD\uACB0\uB3C4");
            Anchor(ctrt, new Vector2(0f, 1f), new Vector2(0f, 1f),
                   new Vector2(pad, cleanMid - UiTheme.Line(UiTheme.LabelSize) * 0.5f),
                   new Vector2(pad + UiTheme.Pt(60f), cleanMid + UiTheme.Line(UiTheme.LabelSize) * 0.5f));
            var cleanBar = UiKit.Bar("CleanBar", hudrt, new Color(0.89f, 0.84f, 0.78f),
                                     UiTheme.Success, UiTheme.Pt(18f), out var cleanFill);
            Anchor(cleanBar, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(pad + UiTheme.Pt(66f), cleanMid - barHalf),
                   new Vector2(-pad - UiTheme.Pt(80f), cleanMid + barHalf));
            var cleanLabel = UiKit.Label("Clean", hudrt, fonts.Number, UiTheme.LabelSize, UiTheme.Ink2,
                                         TextAlignmentOptions.Right, out var clrt, "100%");
            Anchor(clrt, new Vector2(1f, 1f), new Vector2(1f, 1f),
                   new Vector2(-pad - UiTheme.Pt(74f), cleanMid - UiTheme.Line(UiTheme.LabelSize) * 0.5f),
                   new Vector2(-pad, cleanMid + UiTheme.Line(UiTheme.LabelSize) * 0.5f));

            // ---- tray: power read-out above the card row
            var deckBg = UiKit.Panel("DeckBar", go.transform, out var dbrt, UiTheme.Panel, 1f);
            Anchor(dbrt, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero,
                   new Vector2(0f, UiTheme.Tray));

            float powerRowH = UiTheme.Line(UiTheme.NumberL);
            float powerMid = -UiTheme.S8 - powerRowH * 0.5f;
            var powerValue = UiKit.Label("PowerValue", dbrt, fonts.Number, UiTheme.NumberL, UiTheme.Ink,
                                         TextAlignmentOptions.Left, out var pvrt, "0");
            Anchor(pvrt, new Vector2(0f, 1f), new Vector2(0f, 1f),
                   new Vector2(pad, -UiTheme.S8 - powerRowH),
                   new Vector2(pad + UiTheme.Pt(104f), -UiTheme.S8));
            var powerMaxLabel = UiKit.Label("PowerMax", dbrt, fonts.Body, UiTheme.Caption, UiTheme.Ink2,
                                            TextAlignmentOptions.Left, out var pmrt, "/ 0W");
            Anchor(pmrt, new Vector2(0f, 1f), new Vector2(0f, 1f),
                   new Vector2(pad + UiTheme.Pt(108f), -UiTheme.S8 - powerRowH),
                   new Vector2(pad + UiTheme.Pt(226f), -UiTheme.S8));

            var powerBarRt = UiKit.Bar("PowerBar", dbrt, new Color(0.89f, 0.84f, 0.78f),
                                       UiTheme.Power, UiTheme.Pt(14f), out var powerFill);
            Anchor(powerBarRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(pad + UiTheme.Pt(232f), powerMid - UiTheme.Pt(7f)),
                   new Vector2(-pad - UiTheme.Pt(92f), powerMid + UiTheme.Pt(7f)));

            var regen = UiKit.Chip("Regen", dbrt, fonts.Body, "+0/s", out var rgrt,
                                   UiTheme.Power, UiTheme.PowerInk, UiTheme.Pt(26f), UiTheme.Caption);
            Anchor(rgrt, new Vector2(1f, 1f), new Vector2(1f, 1f),
                   new Vector2(-pad - UiTheme.Pt(84f), powerMid - UiTheme.Pt(13f)),
                   new Vector2(-pad, powerMid + UiTheme.Pt(13f)));

            float hintH = UiTheme.Line(UiTheme.Caption);
            var deckRoot = UiKit.Node("DeckRoot", dbrt);
            Anchor(deckRoot, new Vector2(0f, 0f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.S8, hintH + UiTheme.S4),
                   new Vector2(-UiTheme.S8, -UiTheme.S8 - powerRowH - UiTheme.S6));
            var hlg = deckRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = UiTheme.S8;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;
            hlg.childForceExpandWidth = false;

            // ---- unit action bar (redesign 1c): replaces the card row while a unit is selected
            var actionBar = BuildUnitActionBar(dbrt, fonts);

            var hint = UiKit.Label("Hint", dbrt, fonts.Body, UiTheme.Caption, UiTheme.Ink2,
                                   TextAlignmentOptions.Center, out var hrt, "");
            Anchor(hrt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(UiTheme.S12, UiTheme.S4), new Vector2(-UiTheme.S12, UiTheme.S4 + hintH));

            // ---- tutorial bubble
            var tut = Panel("Tutorial", go.transform, new Color(0.1f, 0.12f, 0.14f, 0.95f), out var turt);
            Anchor(turt, new Vector2(0.06f, 0f), new Vector2(0.94f, 0f), new Vector2(0f, 212f), new Vector2(0f, 352f));
            var tutText = Label("TutText", tut.transform, fonts.Body, 20f, Color.white, TextAlignmentOptions.TopLeft, out var ttrt, "");
            Anchor(ttrt, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(18f, 54f), new Vector2(-18f, -14f));
            tutText.enableWordWrapping = true;

            var tutOk = Btn("TutOk", tut.transform, art, fonts.Body, "확인", Accent, Color.white, 18f, out var tokrt, out _);
            Anchor(tokrt, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-120f, 10f), new Vector2(-14f, 48f));
            var tutOff = Btn("TutOff", tut.transform, art, fonts.Body, "튜토리얼 끄기", new Color(1f, 1f, 1f, 0.18f), Color.white, 15f,
                             out var toffrt, out _);
            Anchor(toffrt, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(14f, 10f), new Vector2(190f, 48f));
            tut.gameObject.SetActive(false);

            // ---- overlay
            var overlay = Panel("Overlay", go.transform, new Color(0.05f, 0.06f, 0.07f, 0.72f), out var ovrt);
            Stretch(ovrt);
            var card = Panel("Card", overlay.transform, Surface, out var cdrt);
            Anchor(cdrt, new Vector2(0.08f, 0.5f), new Vector2(0.92f, 0.5f), new Vector2(0f, -280f), new Vector2(0f, 280f));

            var ovTitle = Label("Title", card.transform, fonts.Title, 32f, Ink, TextAlignmentOptions.Center, out var otrt, "");
            Anchor(otrt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -92f), new Vector2(-20f, -24f));

            var ovBody = Label("Body", card.transform, fonts.Body, 20f, Ink, TextAlignmentOptions.Top, out var obrt, "");
            Anchor(obrt, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(24f, 110f), new Vector2(-24f, -100f));
            ovBody.enableWordWrapping = true;

            var ovPrimary = Btn("Primary", card.transform, art, fonts.Body, "시작", Accent, Color.white, 22f,
                                out var oprt, out var ovPrimaryLabel);
            Anchor(oprt, new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(24f, 28f), new Vector2(-8f, 94f));

            var ovSecondary = Btn("Secondary", card.transform, art, fonts.Body, "로비로", Muted, Color.white, 22f,
                                  out var osrt, out var ovSecondaryLabel);
            Anchor(osrt, new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(8f, 28f), new Vector2(-24f, 94f));
            overlay.gameObject.SetActive(false);

            // Pause overlay: the redesign's hierarchy lists it, and it is now the only way
            // out of a battle since the top row no longer carries an exit button.
            var pauseOverlay = UiKit.Panel("PauseOverlay", go.transform, out var port,
                                           new Color(0.05f, 0.06f, 0.07f, 0.72f), 1f);
            Stretch(port);
            UiKit.Panel("Card", port, out var pcrt, UiTheme.Panel, UiTheme.PanelCorner);
            Anchor(pcrt, new Vector2(0.12f, 0.5f), new Vector2(0.88f, 0.5f),
                   new Vector2(0f, -UiTheme.Pt(96f)), new Vector2(0f, UiTheme.Pt(96f)));
            UiKit.Label("Title", pcrt, fonts.Title, UiTheme.Title, UiTheme.Ink,
                        TextAlignmentOptions.Center, out var ptrt, "\uc77c\uc2dc\uc815\uc9c0");
            Anchor(ptrt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.S16, -UiTheme.S24 - UiTheme.Line(UiTheme.Title)),
                   new Vector2(-UiTheme.S16, -UiTheme.S24));
            var pauseResume = UiKit.Button("Resume", pcrt, fonts.Body, "\uacc4\uc18d",
                                           UiButtonKind.Primary, out var prrt, UiTheme.ButtonTall);
            Anchor(prrt, new Vector2(0.5f, 0f), new Vector2(1f, 0f),
                   new Vector2(UiTheme.S6, UiTheme.S24),
                   new Vector2(-UiTheme.S16, UiTheme.S24 + UiTheme.ButtonTall));
            var pauseExit = UiKit.Button("Exit", pcrt, fonts.Body, "\ub098\uac00\uae30",
                                         UiButtonKind.Secondary, out var pxrt, UiTheme.ButtonTall);
            Anchor(pxrt, new Vector2(0f, 0f), new Vector2(0.5f, 0f),
                   new Vector2(UiTheme.S16, UiTheme.S24),
                   new Vector2(-UiTheme.S6, UiTheme.S24 + UiTheme.ButtonTall));
            port.gameObject.SetActive(false);

            var battle = go.AddComponent<BattleScreen>();
            Wire(battle,
                 ("board", board), ("fx", fx), ("boardCamera", cam),
                 ("waveLabel", waveLabel), ("waveSubLabel", waveSub), ("remainLabel", remainChip),
                 ("pauseButton", pause), ("speedButton", speed), ("exitButton", pauseExit),
                 ("cleanFill", cleanFill), ("cleanLabel", cleanLabel),
                 ("powerValueLabel", powerValue), ("powerMaxLabel", powerMaxLabel),
                 ("powerFill", powerFill), ("regenLabel", regen), ("hintLabel", hint),
                 ("deckRoot", deckRoot), ("deckCardPrefab", prefabs.DeckCard),
                 ("unitActionBar", actionBar),
                 ("overlayRoot", overlay.gameObject), ("overlayTitle", ovTitle), ("overlayBody", ovBody),
                 ("overlayPrimary", ovPrimary), ("overlayPrimaryLabel", ovPrimaryLabel),
                 ("overlaySecondary", ovSecondary), ("overlaySecondaryLabel", ovSecondaryLabel),
                 ("tutorialRoot", tut.gameObject), ("tutorialText", tutText),
                 ("tutorialOk", tutOk), ("tutorialOff", tutOff));
            WireArray(battle, "waveSegments", segments);
            Wire(battle, ("hudRect", hudrt), ("trayRect", dbrt),
                 ("pauseOverlay", port.gameObject), ("pauseResumeButton", pauseResume));

            go.SetActive(false);
            return battle;
        }
    }
}
