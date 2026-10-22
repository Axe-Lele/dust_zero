// Rebuilds every generated asset: TMP fonts, tiles, prefabs, the sprite library and the scene.
//
// The whole playable setup is reproducible from source. Running
//   DustZero > Rebuild Project Assets
// (or -executeMethod DustZero.Editor.ProjectBuilder.RebuildAll in batch mode) regenerates
// it, so nothing depends on inspector work that only exists on one machine.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DustZero.Core.League;
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
        /// <summary>대청소 아레나 전용 레이어 — 전투 카메라가 아레나를 겹쳐 그리지 않게 가른다.</summary>
        const int ArenaLayer = 8;
        /// <summary>리그 판 전용 레이어. 같은 이유로 가른다.</summary>
        const int LeagueLayer = 9;

        /// <summary>
        /// 글자 칸을 부모 가운데에 두되 **줄 높이만큼** 잡는다. 부모(알약·띠)는 원본 치수를
        /// 지키고 글자만 넘치게 두는 방법이다 — 칸이 줄보다 낮으면 TMP 는 아무것도 안 그린다.
        /// </summary>
        static void CentreLine(RectTransform rt, float lineHeight)
        {
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.offsetMin = new Vector2(0f, -lineHeight * 0.5f);
            rt.offsetMax = new Vector2(0f, lineHeight * 0.5f);
        }

        /// <summary>가지 전체를 한 레이어에 올린다.</summary>
        static void SetLayerDeep(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayerDeep(c.gameObject, layer);
        }

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

        const string IconFolder = "Assets/DustZero/Art/Icon";

        /// <summary>
        /// One of the 10.16-n icons converted by tools/convert_art.py. Named after the source
        /// file so the mapping back to art/icon/&lt;name&gt;.webp stays obvious.
        /// </summary>
        static Sprite LoadIcon(string name)
        {
            var path = $"{IconFolder}/{name}.png";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) Debug.LogWarning($"[DustZero] 아이콘이 없습니다: {path}");
            return sprite;
        }

        const string BgmFolder = "Assets/DustZero/Audio/BGM";

        /// <summary>
        /// A missing track leaves that screen silent rather than failing the build, so the
        /// scene still rebuilds on a machine where the audio has not been copied in yet.
        /// </summary>
        static AudioClip LoadBgm(string name)
        {
            var path = $"{BgmFolder}/{name}.mp3";
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) Debug.LogWarning($"[DustZero] BGM 파일이 없습니다: {path}");
            return clip;
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
            if (existing != null)
            {
                // **어떤 ttf 로 만든 것인지 확인한다.** 예전엔 있으면 그냥 돌려줬는데,
                // 세 폰트를 전부 DoHyeon 으로 만들던 시절의 자산이 그대로 남아서
                // Body/Number 를 IBM Plex 로 바꾼 뒤에도 **게임 전체가 계속 DoHyeon 으로
                // 그려졌다.** 빌드는 매번 성공했고 아무도 알려주지 않았다.
                var made = existing.sourceFontFile != null ? existing.sourceFontFile.name : null;
                if (made == source.name) return existing;
                Debug.LogWarning($"[DustZero] {assetName} SDF 가 {made ?? "알 수 없는 폰트"} 로 "
                                 + $"만들어져 있습니다. {ttfName} 로 다시 만듭니다.");
                AssetDatabase.DeleteAsset(assetPath);
            }

            var font = TMP_FontAsset.CreateFontAsset(
                source,
                samplingPointSize: 72,
                atlasPadding: 7,
                renderMode: UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                atlasWidth: 1024, atlasHeight: 1024,
                atlasPopulationMode: AtlasPopulationMode.Dynamic,
                enableMultiAtlasSupport: true);

            font.name = assetName + " SDF";
            // sourceFontFile 은 읽기 전용이고 CreateFontAsset 이 이미 채운다 —
            // 위의 검사는 그 값을 읽는다.
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

        /// <summary>The icons the lobby and HUD use, resolved from the supplied art pack.</summary>
        public sealed class IconSet
        {
            public Sprite Home, Flag, Appliance, Sparkle, Shop;
            public Sprite Calendar, Pass, Gear;
            public Sprite Coin, Crystal, Plus, Broom, Infinity;
        }

        public sealed class Shapes
        {
            public Sprite Square, Circle, Outline, RangeRing, StoveRing;
            public IconSet Icons;
        }

        static Shapes BuildShapes() => new()
        {
            Square = GeneratedArt.Square("shape_square"),
            Circle = GeneratedArt.Circle("shape_circle"),
            Outline = GeneratedArt.RoundedOutline("shape_outline"),
            RangeRing = GeneratedArt.DashedRing("shape_range_ring"),
            StoveRing = GeneratedArt.StoveRing("shape_stove_ring"),
            Icons = new IconSet
            {
                Home = UiArt.Icon("home"), Flag = UiArt.Icon("flag"),
                Appliance = UiArt.Icon("kitchen"), Sparkle = UiArt.Icon("auto_awesome"),
                Shop = UiArt.Icon("storefront"),
                Calendar = UiArt.Icon("event_available"), Pass = UiArt.Icon("workspace_premium"),
                Gear = UiArt.Icon("settings"),
                Coin = UiArt.Currency("coin"), Crystal = UiArt.Currency("crystal"),
                Plus = UiArt.Icon("add"), Broom = UiArt.Icon("cleaning_services"),
                Infinity = UiArt.Icon("all_inclusive"),
            },
        };

        /// <summary>
        /// The board tiles. The redesign replaces the original's textured floors with a flat
        /// semantic set - path, buildable, blocked, spawn, goal - so a glance tells the player
        /// what a square is for rather than what it is made of. Stove and puddle stay as
        /// overlays on top of the path, since the pack has no tile for either.
        /// </summary>
        public sealed class Tiles
        {
            public TileBase Path, Buildable, Blocked, Spawn, Goal;
            public TileBase Stove, Wet;
        }

        static Tiles BuildTiles(Shapes art) => new()
        {
            Path = MakeTile("tile_path", UiArt.Battle("tile_path"), Color.white),
            Buildable = MakeTile("tile_buildable", UiArt.Battle("tile_buildable"), Color.white),
            Blocked = MakeTile("tile_blocked", UiArt.Battle("tile_blocked"), Color.white),
            Spawn = MakeTile("tile_spawn", UiArt.Battle("tile_spawn"), Color.white),
            Goal = MakeTile("tile_goal", UiArt.Battle("tile_goal"), Color.white),
            // Kept from the original's own art: the pack ships no burner or puddle.
            Stove = MakeTile("tile_stove", TilePainter.Stove(), Color.white),
            Wet = MakeTile("tile_wet", TilePainter.Wet(), Color.white),
        };

        /// <summary>
        /// 피해 숫자용 외곽선 머티리얼. 원본은 숫자를 `lineWidth 3` · `rgba(29,34,38,…)` 로
        /// 두르고 그 위에 색을 칠한다 — 밝은 바닥 위에서도 숫자가 서로 구분되는 건 이것 덕이다.
        ///
        /// 글꼴 머티리얼을 **한 장 구워 공유**한다. `TextMeshPro.fontMaterial` 에 손대면
        /// 인스턴스가 객체마다 생겨 배칭이 깨지고, 공용 머티리얼을 직접 고치면 게임의 모든
        /// 숫자에 외곽선이 생긴다.
        /// </summary>
        static Material OutlinedNumberMaterial(TMP_FontAsset font)
        {
            var path = $"{SettingsFolder}/NumberOutlined.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(font.material);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = font.material.shader;
            mat.CopyPropertiesFromMaterial(font.material);
            mat.EnableKeyword(ShaderUtilities.Keyword_Outline);
            // 두께는 **재서** 정했다. 0.15 로는 글자 둘레 최저 밝기가 94 밖에 안 떨어져
            // (외곽선 색은 34) 거의 보이지 않았다. 0.35 에서 33.7 — 선이 제 색으로
            // 또렷해지고, 원본의 3px 스트로크와 같은 굵기로 읽힌다.
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.35f);
            mat.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0.114f, 0.133f, 0.149f, 1f));
            EditorUtility.SetDirty(mat);
            return mat;
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
                => string.IsNullOrEmpty(file) ? null
                 : AssetDatabase.LoadAssetAtPath<Sprite>($"{Root}/Art/Sprites/{file}.png");
            Sprite Illust(string file)
                => string.IsNullOrEmpty(file) ? null
                 : AssetDatabase.LoadAssetAtPath<Sprite>($"{Root}/Art/Illust/{file}.png");

            // Ids come from the shipped tables, never a list kept in this file: adding an
            // appliance or an enemy to the spreadsheet must not need an edit here as well.
            var db = LoadDatabaseForEditor();

            // File names come from unit_art, so an appliance can point at any art it likes
            // without a rename; the unit_<id> convention is only the fallback.
            string Chibi(string id) => db.Units.TryGetValue(id, out var d) && d.Art != null &&
                                       !string.IsNullOrEmpty(d.Art.chibi) ? d.Art.chibi : $"unit_{id}";

            lib.units = db.UnitOrder
                .Select(id => new SpriteEntry { id = id, sprite = Load(Chibi(id)) })
                .Where(e => e.sprite != null).ToArray();

            lib.illusts = db.UnitOrder
                .Select(id =>
                {
                    var a = db.Units.TryGetValue(id, out var d) ? d.Art : null;
                    return a == null ? new UnitIllust { id = id } : new UnitIllust
                    {
                        id = id,
                        full = Illust(a.full), turn = Illust(a.turn), keys = Illust(a.keys),
                        face1 = Illust(a.face1), face2 = Illust(a.face2), face3 = Illust(a.face3),
                    };
                }).ToArray();

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

            var missingUnits = db.UnitOrder.Where(id => Load(Chibi(id)) == null).ToList();
            var missingIllust = lib.illusts.Where(i => i.full == null).Select(i => i.id).ToList();
            if (missingIllust.Count > 0)
                Debug.LogWarning("일러스트 없음 — " + string.Join(", ", missingIllust) +
                                 "  (unit_art 의 full/turn/keys/face1~3 이름과 Art/Illust 파일명을 맞추세요)");
            var missingEnemies = db.Enemies.Keys
                .Where(id => Load($"enemy_{id}") == null &&
                             Load($"enemy_{db.Enemies[id].Look}") == null).ToList();
            if (missingUnits.Count > 0 || missingEnemies.Count > 0)
                Debug.LogWarning("스프라이트 없음 — 가전: " + string.Join(", ", missingUnits) +
                                 " / 적: " + string.Join(", ", missingEnemies) +
                                 "  (GameData/spriteexport/one.html 로 추출하세요)");

            // Collection art from the redesign pack, indexed by Rarity (R, SR, SSR). The
            // pack also ships an N frame, which this game has no rarity for.
            lib.rarityFrames = new[]
            {
                UiArt.Collection("frame_r"), UiArt.Collection("frame_sr"), UiArt.Collection("frame_ssr"),
            };
            lib.notOwnedFrame = UiArt.Collection("frame_notowned");
            lib.gradeBadges = new[]
            {
                UiArt.Collection("grade_r"), UiArt.Collection("grade_sr"), UiArt.Collection("grade_ssr"),
            };

            // 낱개 아이콘은 폴더에 있는 것을 전부 담는다. 어떤 보상이 어떤 아이콘을 쓰는지는
            // 표(item.family / item.unitId)가 정하므로 여기서 골라 담을 수 없다.
            var icons = new List<SpriteEntry>();
            foreach (var guid in AssetDatabase.FindAssets("t:Sprite", new[] { IconFolder }))
            {
                var iconPath = AssetDatabase.GUIDToAssetPath(guid);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
                if (sprite == null) continue;
                icons.Add(new SpriteEntry
                {
                    id = System.IO.Path.GetFileNameWithoutExtension(iconPath),
                    sprite = sprite,
                });
            }
            lib.icons = icons.ToArray();

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
            public UnitCard UnitCard;
            public DeckSlot DeckSlot;
            public GearCell GearCell;
            public GearExchangeRow GearExchangeRow;
            public ShopCard ShopCard;
            public CoopUpgradeCell CoopUpgradeCell;
            public PassRow PassRow;
            public UiToggleChip DarkChip;
            public SurvivorOptionCard SurvivorOption;
            public SpawnWarnView SpawnWarn;
            public LeagueBenchCell LeagueBench;
            public LeagueShopCard LeagueShop;
            public LeagueTierCard LeagueTier;
            /// <summary>리그 판 위에 겹치는 투명 단추.</summary>
            public UiButton LeagueCell;
            /// <summary>먼지 타워 출전 목록의 한 칸.</summary>
            public TowerOwnCell TowerOwn;
            /// <summary>먼지 타워 층 목록의 한 줄.</summary>
            public TowerFloorCard TowerFloor;
        }

        static Prefabs BuildPrefabs(Shapes art, Fonts fonts, SpriteLibrary library)
        {
            var p = new Prefabs
            {
                Entity = BuildEntityPrefab(art, fonts),
                Particle = BuildParticlePrefab(art),
                Label = BuildLabelPrefab(fonts),
                Highlight = BuildHighlightPrefab(art),
            };
            p.DeckCard = BuildDeckCardPrefab(art, fonts, library);
            p.StageRow = BuildStageRowPrefab(art, fonts);
            p.UnitCard = BuildUnitCardPrefab(fonts, library);
            p.DeckSlot = BuildDeckSlotPrefab(fonts, library);
            p.GearCell = BuildGearCellPrefab(fonts);
            p.GearExchangeRow = BuildGearExchangeRowPrefab(fonts);
            p.ShopCard = BuildShopCardPrefab(fonts);
            p.CoopUpgradeCell = BuildCoopUpgradeCellPrefab(fonts, library);
            p.PassRow = BuildPassRowPrefab(fonts, library);
            p.DarkChip = BuildChapterChipPrefab(fonts);
            p.SurvivorOption = BuildSurvivorOptionPrefab(fonts);
            p.SpawnWarn = BuildSpawnWarnPrefab(art, fonts);
            p.LeagueBench = BuildLeagueBenchPrefab(fonts);
            p.LeagueShop = BuildLeagueShopPrefab(fonts);
            p.LeagueCell = BuildLeagueCellPrefab(fonts);
            p.LeagueTier = BuildLeagueTierPrefab(fonts);
            p.TowerOwn = BuildTowerOwnPrefab(fonts);
            p.TowerFloor = BuildTowerFloorPrefab(fonts);
            AssertAllSaved(p);
            return p;
        }

        /// <summary>
        /// Every prefab builder must end in <see cref="SavePrefab{T}"/>. One that forgets
        /// returns the scene object it just built: the rebuild then looks like it worked
        /// while the stale asset stays on disk and keeps being instantiated. That happened
        /// to DeckCard, and nothing noticed until a screenshot looked wrong.
        /// </summary>
        static void AssertAllSaved(Prefabs p)
        {
            var stale = new List<string>();
            foreach (var f in typeof(Prefabs).GetFields())
            {
                var c = f.GetValue(p) as Component;
                if (c == null) { stale.Add(f.Name + " (null)"); continue; }
                if (!PrefabUtility.IsPartOfPrefabAsset(c.gameObject))
                    stale.Add(f.Name + " (SavePrefab 누락)");
            }
            if (stale.Count > 0)
                throw new System.InvalidOperationException(
                    "프리팹이 디스크에 저장되지 않았습니다: " + string.Join(", ", stale));

            // A builder that saves under a new filename leaves the old asset behind, and
            // ReloadPrefabs keeps handing the scene that old one. The rewritten card then
            // compiles, saves and is never instantiated. Every prefab asset on disk must be
            // one this build just wrote.
            var written = new HashSet<string>();
            foreach (var f in typeof(Prefabs).GetFields())
                if (f.GetValue(p) is Component c) written.Add(AssetDatabase.GetAssetPath(c.gameObject));
            var orphans = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!written.Contains(path)) orphans.Add(path);
            }
            if (orphans.Count > 0)
                throw new System.InvalidOperationException(
                    "이번 빌드가 쓰지 않은 프리팹이 남아 있습니다(이름을 바꿨나요?): "
                    + string.Join(", ", orphans));
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
            var reloaded = new Prefabs
            {
                Entity = Load<EntityView>("Entity"),
                Particle = Load<FxParticle>("FxParticle"),
                Label = Load<FxLabel>("FxLabel"),
                DeckCard = Load<DeckCard>("DeckCard"),
                StageRow = Load<StageRow>("StageRow"),
                Highlight = Load<SpriteRenderer>("TileHighlight"),
                UnitCard = Load<UnitCard>("UnitCard"),
                DeckSlot = Load<DeckSlot>("DeckSlot"),
                GearCell = Load<GearCell>("GearCell"),
                GearExchangeRow = Load<GearExchangeRow>("GearExchangeRow"),
                ShopCard = Load<ShopCard>("ShopCard"),
                CoopUpgradeCell = Load<CoopUpgradeCell>("CoopUpgradeCell"),
                PassRow = Load<PassRow>("PassRow"),
                DarkChip = Load<UiToggleChip>("ChapterChip"),
                SurvivorOption = Load<SurvivorOptionCard>("SurvivorOptionCard"),
                SpawnWarn = Load<SpawnWarnView>("SpawnWarn"),
                LeagueBench = Load<LeagueBenchCell>("LeagueBenchCell"),
                LeagueShop = Load<LeagueShopCard>("LeagueShopCard"),
                LeagueCell = Load<UiButton>("LeagueCell"),
                LeagueTier = Load<LeagueTierCard>("LeagueTierCard"),
                TowerOwn = Load<TowerOwnCell>("TowerOwnCell"),
                TowerFloor = Load<TowerFloorCard>("TowerFloorCard"),
            };
            AssertAllLoaded(reloaded);
            return reloaded;
        }

        /// <summary>
        /// ReloadPrefabs keeps its own hand-written list, so a prefab added to
        /// <see cref="Prefabs"/> and built correctly still arrives here as null - and the
        /// scene wires that null without complaint. SpawnWarn did exactly that. Every field
        /// must come back filled.
        /// </summary>
        static void AssertAllLoaded(Prefabs p)
        {
            var missing = new List<string>();
            foreach (var f in typeof(Prefabs).GetFields())
                if (f.GetValue(p) as Component == null) missing.Add(f.Name);
            if (missing.Count > 0)
                throw new System.InvalidOperationException(
                    "ReloadPrefabs 가 불러오지 못한 프리팹이 있습니다(목록에 추가했나요?): "
                    + string.Join(", ", missing));
        }

        static EntityView BuildEntityPrefab(Shapes art, Fonts fonts)
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

            // Boost stars, top-right of the tile, gold on a dark outline so they stay legible
            // over both floor and furniture. Right-aligned from the tile's right edge, which
            // is where the original writes them (tx = c*k + k - 3).
            var badgeGo = new GameObject("TierBadge");
            badgeGo.transform.SetParent(go.transform, false);
            var badge = badgeGo.AddComponent<TextMeshPro>();
            badge.font = fonts.Body;
            badge.fontSize = 2.2f;
            badge.fontStyle = FontStyles.Bold;
            badge.color = new Color(0.949f, 0.757f, 0.306f);     // #F2C14E
            // 원본과 다른 점: the build strokes the stars with #1D2226. Setting outlineWidth
            // here does nothing - it needs a material instance, and reaching for the shared
            // font material would outline every label in the game. Gold alone reads fine on
            // these light floors; revisit if a dark map makes it disappear.
            badge.alignment = TextAlignmentOptions.TopRight;
            badge.enableWordWrapping = false;
            badge.sortingOrder = 520;
            badge.rectTransform.sizeDelta = new Vector2(1.4f, 0.4f);
            badgeGo.transform.localPosition = new Vector3(0.47f - 0.7f, 0.48f - 0.2f, 0f);
            view.TierBadge = badge;

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

            // 라벨 뒤 어두운 알약. 원본은 글자 폭 + 12px, 높이 20px, 반지름 10 으로
            // `rgba(29,34,38,.85)` 를 깐다. 겹쳐 떠도 읽히는 건 이 배경 덕이다.
            var plateGo = new GameObject("Plate");
            plateGo.transform.SetParent(go.transform, false);
            var plate = plateGo.AddComponent<SpriteRenderer>();
            plate.sprite = GeneratedArt.RoundedNineSlice("ui_pill", 10);
            plate.drawMode = SpriteDrawMode.Sliced;
            plate.size = new Vector2(1f, 1f);
            plate.color = new Color(0.114f, 0.133f, 0.149f, 0.85f);
            plate.sortingOrder = 899;
            l.Plate = plate;

            var tmp = go.AddComponent<TextMeshPro>();
            tmp.font = fonts.Number;
            tmp.fontSize = 2.2f;
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

        /// <summary>
        /// 10.17-e's regen warning. Built in world space because it has to land on a specific
        /// entrance tile; everything is positioned at bind time in tile units, so nothing here
        /// sets a layout, only the pieces and their draw order.
        /// </summary>
        static SpawnWarnView BuildSpawnWarnPrefab(Shapes art, Fonts fonts)
        {
            var panel = GeneratedArt.RoundedNineSlice("warn_panel", 9);
            var edge = GeneratedArt.RoundedOutlineNineSlice("warn_edge", 9, 3);
            var tri = GeneratedArt.Triangle("warn_tail");
            var dot = GeneratedArt.AirDot("warn_airdot");

            var go = new GameObject("SpawnWarn");
            var view = go.AddComponent<SpawnWarnView>();

            SpriteRenderer Sr(string name, Sprite sprite, int order, bool sliced = false)
            {
                var child = new GameObject(name);
                child.transform.SetParent(go.transform, false);
                var sr = child.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.sortingOrder = order;
                if (sliced)
                {
                    sr.drawMode = SpriteDrawMode.Sliced;
                    sr.size = new Vector2(1f, 1f);
                }
                return sr;
            }
            TextMeshPro Tmp(string name, float size, int order, Color colour)
            {
                var child = new GameObject(name);
                child.transform.SetParent(go.transform, false);
                var tmp = child.AddComponent<TextMeshPro>();
                tmp.font = fonts.Body;
                tmp.fontSize = size;
                tmp.color = colour;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.enableWordWrapping = false;
                tmp.rectTransform.sizeDelta = new Vector2(1.6f, 0.5f);
                tmp.sortingOrder = order;
                return tmp;
            }

            // The pulsing square on the entrance sits under the bubble; the bubble and its
            // contents climb from 600 so they clear flyers, which draw at 400.
            var frame = Sr("Frame", art.Outline, 560);
            var fill = Sr("BubbleFill", panel, 600, sliced: true);
            var tail = Sr("Tail", tri, 601);
            var border = Sr("BubbleEdge", edge, 602, sliced: true);

            var icons = new SpriteRenderer[2];
            var counts = new TextMeshPro[2];
            var airs = new SpriteRenderer[2];
            var dividers = new SpriteRenderer[2];
            for (int i = 0; i < 2; i++)
            {
                icons[i] = Sr($"Icon{i}", null, 603);
                counts[i] = Tmp($"Count{i}", 2.0f, 605, UiTheme.Ink);
                airs[i] = Sr($"Air{i}", dot, 606);
                dividers[i] = Sr($"Divider{i}", art.Square, 604);
            }

            var more = Tmp("More", 1.7f, 605, UiTheme.Ink);
            // Disc, not art.Circle: Circle fades out from 82% of its radius, which at badge
            // size is a pale smudge - the white countdown on it was unreadable.
            var badge = Sr("Badge", GeneratedArt.Disc("warn_badge", 64), 607);
            var badgeText = Tmp("BadgeText", 1.9f, 608, Color.white);

            Wire(view, ("frame", frame), ("bubbleFill", fill), ("bubbleEdge", border),
                 ("tail", tail), ("badge", badge), ("badgeText", badgeText),
                 ("moreText", more));

            // slots is an array of a nested [Serializable] class, so Wire/WireArray - which
            // only know object references - cannot set it. Walk the property by hand.
            var so = new SerializedObject(view);
            var arr = so.FindProperty("slots");
            arr.arraySize = 2;
            for (int i = 0; i < 2; i++)
            {
                var el = arr.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("Icon").objectReferenceValue = icons[i];
                el.FindPropertyRelative("Count").objectReferenceValue = counts[i];
                el.FindPropertyRelative("AirDot").objectReferenceValue = airs[i];
                el.FindPropertyRelative("Divider").objectReferenceValue = dividers[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return SavePrefab<SpawnWarnView>(go, "SpawnWarn");
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
        /// <summary>
        /// One hand card, measured off 10.16-n: 86x104, 11px corners, a 1.5px cool-grey
        /// border on a near-white plate. Flat - the pack's painted card_normal frame is the
        /// old build's look and reads as beige against this palette.
        /// </summary>
        static DeckCard BuildDeckCardPrefab(Shapes art, Fonts fonts, SpriteLibrary library)
        {
            var go = UI("DeckCard", null, out var rt);
            // 원본 카드는 95.8x87.9 — 안기운 글은 이미 위에서 88 기준으로 잡혀 있었고
            // 카드만 104 라 아래가 16px 비어 있었다.
            rt.sizeDelta = new Vector2(UiTheme.Px(95.8f), UiTheme.Py(87.9f));

            var frame = go.AddComponent<Image>();
            frame.sprite = GeneratedArt.RoundedNineSlice("ui_card11",
                Mathf.RoundToInt(UiTheme.Px(11f)));
            frame.type = Image.Type.Sliced;
            frame.color = UiTheme.Panel;
            frame.raycastTarget = true;

            // The 1.5px cool-grey rule is its own ring: one sprite cannot carry both a fill
            // and a differently-coloured border.
            var edgeGo = UI("Edge", go.transform, out var ert);
            Stretch(ert);
            var edge = edgeGo.AddComponent<Image>();
            edge.sprite = GeneratedArt.RoundedOutlineNineSlice("ui_cardline11",
                Mathf.RoundToInt(UiTheme.Px(11f)), Mathf.RoundToInt(UiTheme.Px(1.5f)));
            edge.type = Image.Type.Sliced;
            edge.color = UiTheme.PanelLine;
            edge.raycastTarget = false;

            var group = go.AddComponent<CanvasGroup>();
            var button = go.AddComponent<Button>();
            button.targetGraphic = frame;
            button.transition = Selectable.Transition.None;

            // Rarity is a wash down the top 60%, not a stripe: the build paints
            // linear-gradient(--sr-bg, --card 60%) across the whole card face.
            var wash = UI("Rarity", go.transform, out var wrt);
            Anchor(wrt, new Vector2(0f, 0.4f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(1.5f), 0f),
                   new Vector2(-UiTheme.Px(1.5f), -UiTheme.Px(1.5f)));
            var rarityWash = wash.AddComponent<Image>();
            rarityWash.sprite = GeneratedArt.VerticalFade("ui_rarity_wash");
            rarityWash.type = Image.Type.Simple;
            rarityWash.raycastTarget = false;

            // 42px of art, 7px down from the top edge.
            var portraitGo = UI("Portrait", go.transform, out var prt);
            Anchor(prt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                   new Vector2(-UiTheme.Px(21f), -UiTheme.Py(7f) - UiTheme.Px(42f)),
                   new Vector2(UiTheme.Px(21f), -UiTheme.Py(7f)));
            var portrait = portraitGo.AddComponent<Image>();
            portrait.preserveAspect = true;
            portrait.raycastTarget = false;

            var name = UiKit.Label("Name", go.transform, fonts.Title, UiTheme.Py(13f), UiTheme.Ink,
                                   TextAlignmentOptions.Center, out var nrt, "가전");
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.overflowMode = TextOverflowModes.Ellipsis;
            // 18px of box for a 13pt line: at 15 TMP drops it.
            Anchor(nrt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(2f), -UiTheme.Py(50f) - UiTheme.Py(18f)),
                   new Vector2(-UiTheme.Px(2f), -UiTheme.Py(50f)));

            var cost = UiKit.Label("Cost", go.transform, fonts.Number, UiTheme.Py(12f), UiTheme.Ink,
                                   TextAlignmentOptions.Center, out var cort, "0W");
            cost.textWrappingMode = TextWrappingModes.NoWrap;
            // Centred on the build's y=76.5 with room to spare: a 12pt line needs 37px and
            // the build's own 17px row left TMP two short, which blanks the whole label.
            Anchor(cort, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(2f), -UiTheme.Py(76.5f) - UiTheme.Py(11f)),
                   new Vector2(-UiTheme.Px(2f), -UiTheme.Py(76.5f) + UiTheme.Py(11f)));

            // The charge bar is 3px along the very bottom edge, full width, no track.
            var charge = UiKit.Surface("Charge", go.transform, UiTheme.Led, 0f, out var crt);
            Anchor(crt, Vector2.zero, new Vector2(1f, 0f),
                   new Vector2(UiTheme.Px(1.5f), UiTheme.Px(1.5f)),
                   new Vector2(-UiTheme.Px(1.5f), UiTheme.Px(1.5f) + UiTheme.Py(3f)));
            charge.type = Image.Type.Filled;
            charge.fillMethod = Image.FillMethod.Horizontal;
            charge.raycastTarget = false;

            // Cooldown veil: the whole face at 72% ink with the countdown in the LED face.
            var cdVeilGo = UI("CooldownVeil", go.transform, out var cvrt);
            Stretch(cvrt);
            cvrt.SetAsLastSibling();
            var cdVeil = cdVeilGo.AddComponent<Image>();
            cdVeil.sprite = GeneratedArt.RoundedNineSlice("ui_card11",
                Mathf.RoundToInt(UiTheme.Px(11f)));
            cdVeil.type = Image.Type.Sliced;
            cdVeil.color = new Color(UiTheme.Ink.r, UiTheme.Ink.g, UiTheme.Ink.b, 0.72f);
            cdVeil.raycastTarget = false;

            var cd = UiKit.Label("Cooldown", cvrt, fonts.Number, UiTheme.Py(15f), UiTheme.DarkInk,
                                 TextAlignmentOptions.Center, out var cdrt, "");
            Stretch(cdrt);

            // Selection is a blue ring drawn outside the plate, matching the build's
            // 2px inset shadow on a --cool border.
            var outlineGo = UI("Selected", go.transform, out var ort);
            Anchor(ort, Vector2.zero, Vector2.one,
                   new Vector2(-UiTheme.Px(2f), -UiTheme.Px(2f)),
                   new Vector2(UiTheme.Px(2f), UiTheme.Px(2f)));
            var outline = outlineGo.AddComponent<Image>();
            outline.sprite = GeneratedArt.RoundedOutlineNineSlice("ui_ring13",
                Mathf.RoundToInt(UiTheme.Px(13f)), Mathf.RoundToInt(UiTheme.Px(2.5f)));
            outline.type = Image.Type.Sliced;
            outline.color = UiTheme.Navy;   // --cool
            outline.raycastTarget = false;
            outline.enabled = false;
            ort.SetAsLastSibling();

            var card = go.AddComponent<DeckCard>();
            Wire(card,
                 ("button", button), ("frame", frame), ("group", group),
                 ("rarityWash", rarityWash), ("portrait", portrait),
                 ("nameLabel", name), ("costLabel", cost),
                 ("chargeFill", charge),
                 ("cooldownRoot", cdVeilGo), ("cooldownLabel", cd),
                 ("selectedOutline", outline), ("sprites", library));
            return SavePrefab<DeckCard>(go, "DeckCard");
        }


        /// <summary>
        /// Redesign 2a's stage row: number tile, name over a one-line summary, and then
        /// whichever trailing element the row's state calls for - stars, a 도전 button, or a
        /// padlock. The row grows for the button, so its height is a LayoutElement the
        /// component drives rather than a constant baked in here.
        /// </summary>
        static StageRow BuildStageRowPrefab(Shapes art, Fonts fonts)
        {
            var go = UI("StageRow", null, out var rt);
            rt.sizeDelta = new Vector2(0f, StageRow.NormalHeight);

            var face = go.AddComponent<Image>();
            face.sprite = UiArt.Panel;
            face.type = Image.Type.Sliced;
            face.color = UiTheme.Panel;

            var le = go.AddComponent<LayoutElement>();
            le.minHeight = StageRow.NormalHeight;
            le.preferredHeight = StageRow.NormalHeight;

            // Boss stages wear a stripe down the leading edge.
            var boss = UiKit.Surface("Boss", rt, UiTheme.Danger, UiTheme.Px(3f), out var brt);
            Anchor(brt, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(UiTheme.Px(5f), 0f));

            // ---- number tile
            float tile = UiTheme.Py(44f);
            var numberFace = UiKit.Surface("Number", rt, UiTheme.Panel2, UiTheme.Px(12f), out var nfrt);
            Anchor(nfrt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                   new Vector2(UiTheme.Px(12f), -tile * 0.5f),
                   new Vector2(UiTheme.Px(12f) + tile, tile * 0.5f));
            var no = UiKit.Label("No", nfrt, fonts.Body, UiTheme.Px(14f), UiTheme.Ink,
                                 TextAlignmentOptions.Center, out var nort, "1-1");
            Stretch(nort);

            // ---- name over summary. Both sit inside one band so the pair stays centred
            // whichever height the row takes.
            float textLeft = UiTheme.Px(12f) + tile + UiTheme.Px(12f);
            float trailing = UiTheme.Px(130f);
            var text = UiKit.Node("Text", rt);
            Anchor(text, Vector2.zero, Vector2.one,
                   new Vector2(textLeft, StageRow.RowPad), new Vector2(-trailing, -StageRow.RowPad));

            // Each line gets its own line box rather than half the band. Splitting 50/50 left
            // the name 3px short of what its 15px face needs and TMP drew nothing at all.
            var name = UiKit.Label("Name", text, fonts.Body, UiTheme.Px(15f), UiTheme.Ink,
                                   TextAlignmentOptions.BottomLeft, out var nrt, "거실");
            Anchor(nrt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(0f, -StageRow.NameLine), Vector2.zero);
            var detail = UiKit.Label("Detail", text, fonts.Body, UiTheme.LabelSize, UiTheme.Ink2,
                                     TextAlignmentOptions.TopLeft, out var drt, "");
            Anchor(drt, Vector2.zero, new Vector2(1f, 0f), Vector2.zero,
                   new Vector2(0f, StageRow.DetailLine));

            // ---- trailing: exactly one of these is visible at a time
            var stars = UiKit.Label("Stars", rt, fonts.Body, UiTheme.Px(17f), UiTheme.RaritySSR,
                                    TextAlignmentOptions.Right, out var strt, "★★★");
            stars.richText = true;
            Anchor(strt, new Vector2(1f, 0f), new Vector2(1f, 1f),
                   new Vector2(-trailing, 0f), new Vector2(-UiTheme.Px(12f), 0f));

            var play = UiKit.Button("Play", rt, fonts.Title, "도전", UiButtonKind.Primary,
                                    out var playRt, UiTheme.Px(56f), UiTheme.Px(22f));
            Anchor(playRt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                   new Vector2(-UiTheme.Px(12f) - UiTheme.Px(104f), -UiTheme.Py(28f)),
                   new Vector2(-UiTheme.Px(12f), UiTheme.Py(28f)));

            var lockRt = UiKit.Node("Lock", rt);
            Anchor(lockRt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                   new Vector2(-UiTheme.Px(12f) - tile, -tile * 0.5f),
                   new Vector2(-UiTheme.Px(12f), tile * 0.5f));
            var padlock = lockRt.gameObject.AddComponent<Image>();
            padlock.sprite = UiArt.Icon("lock");
            padlock.color = UiTheme.Ink3;
            padlock.preserveAspect = true;
            padlock.raycastTarget = false;

            var row = go.AddComponent<StageRow>();
            Wire(row, ("face", face), ("numberFace", numberFace), ("noLabel", no),
                 ("nameLabel", name), ("detailLabel", detail), ("starsLabel", stars),
                 ("playButton", play), ("lockMark", lockRt.gameObject), ("bossMark", boss),
                 ("layout", le));

            return SavePrefab<StageRow>(go, "StageRow");
        }

        /// <summary>
        /// A roster tile: the pack's rarity frame, the portrait, a grade badge, and the two
        /// markers the redesign puts on it - a deck check and a shard count.
        /// </summary>
        /// <summary>
        /// The 10.16-n `.ut` tile, measured off the running build: 94x137 on --card with a
        /// 1.5px --line outline at a 12px radius, a 92x92 rarity-tinted picture area whose top
        /// corners follow the card, then the name and the level line. The rarity initials are
        /// bare coloured text in the top-left; the two corner tags live in the top-right.
        /// </summary>
        static UnitCard BuildUnitCardPrefab(Fonts fonts, SpriteLibrary library)
        {
            int radius = Mathf.RoundToInt(UiTheme.Px(12f));
            var go = UI("UnitCard", null, out var rt);
            rt.sizeDelta = new Vector2(UiTheme.Px(94f), UiTheme.Py(137f));

            var fill = go.AddComponent<Image>();
            fill.sprite = GeneratedArt.RoundedNineSlice("ui_card", radius);
            fill.type = Image.Type.Sliced;
            fill.color = UiTheme.Panel;
            fill.raycastTarget = true;

            var button = go.AddComponent<Button>();
            button.targetGraphic = fill;
            button.transition = Selectable.Transition.None;

            // ---- picture: 92x92 inset 1px, tinted by rarity, portrait 80x80 centred in it.
            var picRt = UiKit.Node("Pic", rt);
            // Page-square, not screen-square: the two axes scale differently, so the picture
            // keeps its 92/137 share of the card's height and 92/94 of its width.
            Anchor(picRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(1f), -UiTheme.Py(93f)),
                   new Vector2(-UiTheme.Sq(1f), -UiTheme.Sq(1f)));
            var picBg = picRt.gameObject.AddComponent<Image>();
            picBg.sprite = GeneratedArt.RoundedTopNineSlice("ui_card_pic", radius);
            picBg.type = Image.Type.Sliced;
            picBg.raycastTarget = false;

            var portraitRt = UiKit.Node("Portrait", picRt);
            portraitRt.anchorMin = portraitRt.anchorMax = new Vector2(0.5f, 0.5f);
            // The portrait itself must stay square, so it takes the smaller of the two scales.
            float portrait80 = UiTheme.Sq(80f);
            portraitRt.sizeDelta = new Vector2(portrait80, portrait80);
            var portrait = portraitRt.gameObject.AddComponent<Image>();
            portrait.preserveAspect = true;
            portrait.raycastTarget = false;

            // ---- outline over the fill, so the 1.5px line is never covered by the picture.
            var lineRt = UiKit.Node("Outline", rt);
            Stretch(lineRt);
            var outline = lineRt.gameObject.AddComponent<Image>();
            outline.sprite = GeneratedArt.RoundedOutlineNineSlice("ui_card_line", radius,
                Mathf.Max(1, Mathf.RoundToInt(UiTheme.Px(1.5f))));
            outline.type = Image.Type.Sliced;
            outline.color = UiTheme.PanelLine;
            outline.raycastTarget = false;

            // ---- rarity initials, top-left, no plate.
            var rarity = UiKit.Label("Rarity", rt, fonts.Number, UiTheme.Py(11f), UiTheme.RarityR,
                                     TextAlignmentOptions.TopLeft, out var rarRt, "");
            Anchor(rarRt, new Vector2(0f, 1f), new Vector2(0f, 1f),
                   new Vector2(UiTheme.Px(6f), -UiTheme.Py(21f)),
                   new Vector2(UiTheme.Px(46f), -UiTheme.Py(3f)));

            // ---- name and level line at the foot of the card.
            var name = UiKit.Label("Name", rt, fonts.Title, UiTheme.Py(14f), UiTheme.Ink,
                                   TextAlignmentOptions.Center, out var nameRt, "");
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.overflowMode = TextOverflowModes.Ellipsis;
            Anchor(nameRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(2f), -UiTheme.Py(113f)),
                   new Vector2(-UiTheme.Px(2f), -UiTheme.Py(94f)));

            var sub = UiKit.Label("Sub", rt, fonts.Number, UiTheme.Py(10.5f), UiTheme.Ink2,
                                  TextAlignmentOptions.Center, out var subRt, "");
            sub.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(subRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(2f), -UiTheme.Py(131f)),
                   new Vector2(-UiTheme.Px(2f), -UiTheme.Py(112f)));

            var deckTag = CornerTag("DeckTag", rt, fonts.Body, UiTheme.Ink, UiTheme.Panel,
                                    "편성", 4f, out var deckLabel, out _);
            var hotTag = CornerTag("HotTag", rt, fonts.Body, UiTheme.Primary, UiTheme.Ink,
                                   "영입 가능", 4f, out var hotLabel, out var hotRect);

            var card = go.AddComponent<UnitCard>();
            Wire(card, ("cardFill", fill), ("cardOutline", outline), ("picBackground", picBg),
                 ("portrait", portrait), ("rarityLabel", rarity), ("nameLabel", name),
                 ("subLabel", sub), ("deckTag", deckTag.gameObject), ("deckLabel", deckLabel),
                 ("hotTag", hotTag.gameObject),
                 ("hotLabel", hotLabel), ("hotRect", hotRect),
                 ("button", button), ("sprites", library));
            return SavePrefab<UnitCard>(go, "UnitCard");
        }

        /// <summary>
        /// One of the card's top-right pills: a tinted 5px-radius plate that hugs its text,
        /// 10px semibold. Returns the plate so the caller can move or hide it.
        /// </summary>
        static RectTransform CornerTag(string id, RectTransform parent, TMP_FontAsset font,
                                       Color plate, Color ink, string text, float topPage,
                                       out TextMeshProUGUI label, out RectTransform rect)
        {
            var tagRt = UiKit.Node(id, parent);
            tagRt.anchorMin = tagRt.anchorMax = new Vector2(1f, 1f);
            tagRt.pivot = new Vector2(1f, 1f);
            tagRt.sizeDelta = new Vector2(0f, UiTheme.Py(17f));
            tagRt.anchoredPosition = new Vector2(-UiTheme.Px(4f), -UiTheme.Py(topPage));
            var bg = tagRt.gameObject.AddComponent<Image>();
            bg.sprite = GeneratedArt.RoundedNineSlice("ui_tag", Mathf.RoundToInt(UiTheme.Px(5f)));
            bg.type = Image.Type.Sliced;
            bg.color = plate;
            bg.raycastTarget = false;

            label = UiKit.Label("Text", tagRt, font, UiTheme.Py(10f), ink,
                                TextAlignmentOptions.Center, out var lRt, text);
            label.fontStyle = FontStyles.Bold;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            Stretch(lRt);

            // The plate is sized to its text, the way the build's inline-block pill is.
            var fitter = tagRt.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var layout = tagRt.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset((int)UiTheme.Px(5f), (int)UiTheme.Px(5f),
                                            (int)UiTheme.Py(1f), (int)UiTheme.Py(1f));
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            rect = tagRt;
            tagRt.gameObject.SetActive(false);
            return tagRt;
        }

        /// <summary>
        /// A 편성 slot: a 3:4 box that is a dashed --line outline over nothing while empty and
        /// a solid outline over the rarity tint once filled, with a 34px portrait and the
        /// appliance's short name.
        /// </summary>
        static DeckSlot BuildDeckSlotPrefab(Fonts fonts, SpriteLibrary library)
        {
            int radius = Mathf.RoundToInt(UiTheme.Px(10f));
            int w = Mathf.RoundToInt(UiTheme.Px(40f)), h = Mathf.RoundToInt(UiTheme.Py(62f));
            var go = UI("DeckSlot", null, out var rt);
            rt.sizeDelta = new Vector2(w, h);

            var fill = go.AddComponent<Image>();
            fill.sprite = GeneratedArt.RoundedNineSlice("ui_slot", radius);
            fill.type = Image.Type.Sliced;
            fill.color = UiTheme.Panel;
            fill.raycastTarget = true;

            var button = go.AddComponent<Button>();
            button.targetGraphic = fill;
            button.transition = Selectable.Transition.None;

            var lineRt = UiKit.Node("Outline", rt);
            Stretch(lineRt);
            var outline = lineRt.gameObject.AddComponent<Image>();
            var solid = GeneratedArt.RoundedOutlineNineSlice("ui_slot_line", radius,
                Mathf.Max(1, Mathf.RoundToInt(UiTheme.Px(1.5f))));
            // Dashes cannot be 9-sliced, so the empty outline is drawn at the slot's own size.
            // Drawn at the empty slot's own size, which is the only place it is used.
            var dashed = GeneratedArt.DashedRoundedOutline("ui_slot_dash",
                Mathf.RoundToInt(UiTheme.Px(38f)), Mathf.RoundToInt(UiTheme.Py(50.7f)), radius,
                UiTheme.Px(1.5f), UiTheme.Px(4f), UiTheme.Px(3f));
            outline.sprite = solid;
            outline.type = Image.Type.Sliced;
            outline.color = UiTheme.PanelLine;
            outline.raycastTarget = false;

            var portraitRt = UiKit.Node("Portrait", rt);
            Anchor(portraitRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                   new Vector2(-UiTheme.Px(17f), -UiTheme.Py(39f)),
                   new Vector2(UiTheme.Px(17f), -UiTheme.Py(5f)));
            var portrait = portraitRt.gameObject.AddComponent<Image>();
            portrait.preserveAspect = true;
            portrait.raycastTarget = false;

            var name = UiKit.Label("Name", rt, fonts.Title, UiTheme.Py(12.5f), UiTheme.Ink,
                                   TextAlignmentOptions.Center, out var nameRt, "");
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.overflowMode = TextOverflowModes.Ellipsis;
            // '빈 슬롯' 은 40px 칸보다 넓어 점선 밖으로 비어져 나왔다. 줄여서 담는다.
            name.enableAutoSizing = true;
            name.fontSizeMin = UiTheme.Py(12.5f) * 0.7f;
            name.fontSizeMax = UiTheme.Py(12.5f);
            Anchor(nameRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(1f), -UiTheme.Py(58f)),
                   new Vector2(-UiTheme.Px(1f), -UiTheme.Py(39f)));

            var slot = go.AddComponent<DeckSlot>();
            Wire(slot, ("fill", fill), ("outline", outline), ("solidOutline", solid),
                 ("dashedOutline", dashed), ("portrait", portrait), ("nameLabel", name),
                 ("button", button), ("sprites", library));
            return SavePrefab<DeckSlot>(go, "DeckSlot");
        }
        /// <summary>
        /// One 상점 tile, measured off the running build: a 14px card on --card holding a
        /// badge row, the reward art with its amount, the name, a description, a meta line
        /// and the buy button. 174 wide in the grid; the ad product spans both columns.
        /// </summary>
        /// <summary>
        /// 협동 조작 줄의 강화 버튼 한 칸 — 원본 `.rd-up`.
        /// border 1.5 · radius 10 · padding 3/1/4 · 아이콘 30 · 라벨 10.5 두 줄.
        /// </summary>
        static CoopUpgradeCell BuildCoopUpgradeCellPrefab(Fonts fonts, SpriteLibrary library)
        {
            var go = UI("CoopUpgradeCell", null, out var rt);
            float radius = UiTheme.Px(10f);

            var face = go.AddComponent<Image>();
            face.sprite = UiKit.Rounded(radius);
            face.type = Image.Type.Sliced;
            face.color = Color.white;

            var group = go.AddComponent<CanvasGroup>();

            var line = UiKit.Node("Line", rt);
            UiKit.Stretch(line);
            var lineImage = line.gameObject.AddComponent<Image>();
            lineImage.sprite = UiKit.Rounded(radius);
            lineImage.type = Image.Type.Sliced;
            lineImage.color = UiTheme.PanelLine;
            lineImage.raycastTarget = false;

            var inner = UiKit.Node("Inner", rt);
            UiKit.Stretch(inner, UiTheme.HairLine);
            var innerImage = inner.gameObject.AddComponent<Image>();
            innerImage.sprite = UiKit.Rounded(radius);
            innerImage.type = Image.Type.Sliced;
            innerImage.color = Color.white;
            innerImage.raycastTarget = false;

            float iconSize = UiTheme.Sq(30f);
            float lvLine = UiTheme.Line(UiTheme.Px(10.5f));
            float costLine = UiTheme.Line(UiTheme.Px(10.5f));
            float padTop = UiTheme.Py(3f), padBottom = UiTheme.Py(4f);

            var icon = UiKit.Node("Icon", rt);
            Anchor(icon, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                   new Vector2(-iconSize * 0.5f, -padTop - iconSize),
                   new Vector2(iconSize * 0.5f, -padTop));
            var iconImage = icon.gameObject.AddComponent<Image>();
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;

            var level = UiKit.Label("Level", rt, fonts.Body, UiTheme.Px(10.5f), UiTheme.Ink,
                                    TextAlignmentOptions.Center, out var lvRt,
                                    "\uac15\ud654 0");
            level.fontStyle = FontStyles.Bold;
            Anchor(lvRt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(0f, padBottom + costLine), new Vector2(0f, padBottom + costLine + lvLine));

            var cost = UiKit.Label("Cost", rt, fonts.Number, UiTheme.Px(10.5f), UiTheme.Ink2,
                                   TextAlignmentOptions.Center, out var costRt, "");
            Anchor(costRt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(0f, padBottom), new Vector2(0f, padBottom + costLine));

            rt.sizeDelta = new Vector2(UiTheme.Px(56f), padTop + iconSize + lvLine + costLine + padBottom);

            var button = go.AddComponent<UiButton>();
            button.SetKind(UiButtonKind.Glass);

            var cell = go.AddComponent<CoopUpgradeCell>();
            Wire(cell,
                 ("face", innerImage), ("icon", iconImage),
                 ("levelLabel", level), ("costLabel", cost),
                 ("button", button), ("group", group), ("sprites", library));

            return SavePrefab<CoopUpgradeCell>(go, "CoopUpgradeCell");
        }

        /// <summary>
        /// 상품 카드 — 원본 `.sp`.
        ///
        /// 세로 흐름이다: 배지 → 그림 → 이름 → 설명 → 메타 → (빈 공간) → 사기 버튼.
        /// 버튼은 `margin-top:auto` 라 언제나 바닥에 붙고, 설명이 있으면 카드가 그만큼
        /// 길어진다. 전에는 전부 절대 앵커여서 설명과 메타가 같은 자리에 겹치고
        /// 둘 다 버튼 뒤로 숨었다 — 데이터에는 있는데 화면에 없던 이유다.
        /// </summary>
        static ShopCard BuildShopCardPrefab(Fonts fonts)
        {
            var go = UI("ShopCard", null, out var rt);
            float pad = UiTheme.Px(12f);
            float gap = UiTheme.Py(6f);

            var face = go.AddComponent<Image>();
            face.sprite = GeneratedArt.RoundedNineSlice("ui_shopcard", Mathf.RoundToInt(UiTheme.Px(14f)));
            face.type = Image.Type.Sliced;
            face.color = UiTheme.Panel;
            face.raycastTarget = false;
            var group = go.AddComponent<CanvasGroup>();

            // 한 행 안에서 폭을 나눠 갖고, 높이는 내용이 정한다.
            var cardLe = go.AddComponent<LayoutElement>();
            cardLe.minHeight = UiTheme.Py(176f);
            cardLe.flexibleWidth = 1f;

            var column = go.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset((int)pad, (int)pad, (int)UiTheme.Py(12f), (int)UiTheme.Py(12f));
            column.spacing = gap;
            column.childForceExpandWidth = true;
            column.childControlWidth = true;
            column.childForceExpandHeight = false;
            column.childControlHeight = true;
            var cardFit = go.AddComponent<ContentSizeFitter>();
            cardFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // ---- 배지 줄. 원본은 최대 둘까지 왼쪽부터 늘어놓는다.
            var badgeRow = UiKit.Node("Badges", rt);
            var badgeLayout = badgeRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            badgeLayout.spacing = UiTheme.Px(4f);
            badgeLayout.childAlignment = TextAnchor.MiddleLeft;
            badgeLayout.childForceExpandWidth = false;
            badgeLayout.childControlWidth = true;
            badgeLayout.childForceExpandHeight = false;
            badgeLayout.childControlHeight = true;
            var badgeLe = badgeRow.gameObject.AddComponent<LayoutElement>();
            badgeLe.minHeight = UiTheme.Line(UiTheme.Py(10.5f));

            (Image plate, TextMeshProUGUI text, GameObject root) Badge(string id)
            {
                var b = UiKit.Surface(id, badgeRow, UiTheme.Panel2, UiTheme.Px(999f), out var brt);
                var ble = brt.gameObject.AddComponent<LayoutElement>();
                ble.minHeight = UiTheme.Line(UiTheme.Py(10.5f));
                var fit = brt.gameObject.AddComponent<ContentSizeFitter>();
                fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                var h = brt.gameObject.AddComponent<HorizontalLayoutGroup>();
                h.padding = new RectOffset((int)UiTheme.Px(8f), (int)UiTheme.Px(8f),
                                           (int)UiTheme.Py(2f), (int)UiTheme.Py(2f));
                h.childControlWidth = true;
                h.childControlHeight = true;
                var t = UiKit.Label("Text", brt, fonts.Body, UiTheme.Py(10.5f), UiTheme.Ink,
                                    TextAlignmentOptions.Center, out var trt, "");
                t.fontStyle = FontStyles.Bold;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                return (b, t, brt.gameObject);
            }

            var (firstPlate, firstLabel, firstRoot) = Badge("First");
            var (badgePlate, badgeLabel, badgeRoot) = Badge("Badge");

            // ---- 그림. 원본 .art 는 54 높이에 아이콘과 숫자를 가로로 놓는다.
            var artRow = UiKit.Node("Art", rt);
            var artLe = artRow.gameObject.AddComponent<LayoutElement>();
            artLe.minHeight = artLe.preferredHeight = UiTheme.Py(54f);
            var artLayout = artRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            artLayout.childAlignment = TextAnchor.MiddleCenter;
            artLayout.spacing = UiTheme.Px(4f);
            artLayout.childForceExpandWidth = false;
            artLayout.childControlWidth = true;
            artLayout.childForceExpandHeight = false;
            artLayout.childControlHeight = true;

            var artIconRt = UiKit.Node("Icon", artRow);
            var artIconLe = artIconRt.gameObject.AddComponent<LayoutElement>();
            artIconLe.minWidth = artIconLe.preferredWidth = UiTheme.Sq(40f);
            artIconLe.minHeight = artIconLe.preferredHeight = UiTheme.Sq(40f);
            var art = artIconRt.gameObject.AddComponent<Image>();
            art.preserveAspect = true;
            art.raycastTarget = false;

            var amount = UiKit.Label("Amount", artRow, fonts.Number, UiTheme.Py(24f), UiTheme.Navy,
                                     TextAlignmentOptions.Left, out var amRt, "");
            amount.textWrappingMode = TextWrappingModes.NoWrap;
            var amLe = amRt.gameObject.AddComponent<LayoutElement>();
            amLe.minHeight = UiTheme.Line(UiTheme.Py(24f));

            // ---- 글줄
            TextMeshProUGUI Line(string id, float size, Color ink, bool bold)
            {
                var t = UiKit.Label(id, rt, bold ? fonts.Title : fonts.Body, UiTheme.Py(size),
                                    ink, TextAlignmentOptions.TopLeft, out var lrt, "");
                t.textWrappingMode = TextWrappingModes.Normal;
                var le = lrt.gameObject.AddComponent<LayoutElement>();
                le.minHeight = UiTheme.Line(UiTheme.Py(size));
                var fit = lrt.gameObject.AddComponent<ContentSizeFitter>();
                fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                return t;
            }

            var name = Line("Name", 14f, UiTheme.Ink, true);
            var desc = Line("Desc", 11.5f, UiTheme.Ink2, false);
            var meta = Line("Meta", 11f, UiTheme.Ink2, false);

            // ---- 빈 공간. 원본 .buy 의 margin-top:auto 가 하는 일이다.
            var spacer = UiKit.Node("Spacer", rt);
            var spacerLe = spacer.gameObject.AddComponent<LayoutElement>();
            spacerLe.flexibleHeight = 1f;
            spacerLe.minHeight = 0f;

            // ---- 사기 버튼
            var buy = UiKit.Button("Buy", rt, fonts.Body, "", UiButtonKind.Primary,
                                   out var buyRt, UiTheme.Py(41f), UiTheme.Py(14f));
            var buyLe = buyRt.gameObject.AddComponent<LayoutElement>();
            buyLe.minHeight = buyLe.preferredHeight = UiTheme.Py(41f);

            var currencyRt = UiKit.Node("Currency", buyRt);
            Anchor(currencyRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                   new Vector2(UiTheme.Px(10f), -UiTheme.Sq(8f)),
                   new Vector2(UiTheme.Px(10f) + UiTheme.Sq(16f), UiTheme.Sq(8f)));
            var currency = currencyRt.gameObject.AddComponent<Image>();
            currency.preserveAspect = true;
            currency.raycastTarget = false;

            var card = go.AddComponent<ShopCard>();
            Wire(card, ("face", face), ("art", art), ("nameLabel", name),
                 ("amountLabel", amount),
                 ("badgeRoot", badgeRoot), ("badgePlate", badgePlate), ("badgeLabel", badgeLabel),
                 ("firstBadgeRoot", firstRoot), ("firstBadgePlate", firstPlate),
                 ("firstBadgeLabel", firstLabel),
                 ("descLabel", desc), ("metaLabel", meta),
                 ("priceButton", buy), ("priceCurrencyIcon", currency), ("group", group),
                 ("crystalIcon", LoadIcon("crystal")), ("coinIcon", LoadIcon("coin")));
            return SavePrefab<ShopCard>(go, "ShopCard");
        }



        /// <summary>One pass level: free cell, level knob, premium cell.</summary>
        static PassRow BuildPassRowPrefab(Fonts fonts, SpriteLibrary library)
        {
            var go = UI("PassRow", null, out var rt);
            float h = UiTheme.Py(68f);
            rt.sizeDelta = new Vector2(0f, h);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = h;
            le.preferredHeight = h;

            float knob = UiTheme.Py(36f);

            (Image face, RewardChipRow chips, TextMeshProUGUI state, GameObject check,
             GameObject padlock, Button button, CanvasGroup group) Cell(string id, bool left)
            {
                var cellRt = UiKit.Node(id, rt);
                Anchor(cellRt, new Vector2(left ? 0f : 0.5f, 0.5f), new Vector2(left ? 0.5f : 1f, 0.5f),
                       new Vector2(left ? 0f : knob * 0.5f + UiTheme.S4, -UiTheme.Py(32f)),
                       new Vector2(left ? -knob * 0.5f - UiTheme.S4 : 0f, UiTheme.Py(32f)));
                var cellFace = cellRt.gameObject.AddComponent<Image>();
                cellFace.sprite = UiArt.Panel;
                cellFace.type = Image.Type.Sliced;
                cellFace.color = UiTheme.Panel;
                var cellGroup = cellRt.gameObject.AddComponent<CanvasGroup>();
                var cellButton = cellRt.gameObject.AddComponent<Button>();
                cellButton.targetGraphic = cellFace;
                cellButton.transition = Selectable.Transition.None;

                // 원본 `.rwc` — 보상은 아이콘+숫자 칩으로 보여 준다. 글자 나열은 좁은 칸에서
                // '코인 3,200 · 필터 5 …' 로 잘려 무엇을 주는지 알 수 없었다.
                var chipsRt = UiKit.Node("Chips", cellRt);
                Anchor(chipsRt, new Vector2(0f, 0.5f), new Vector2(1f, 1f),
                       new Vector2(UiTheme.S10, 0f), new Vector2(-UiTheme.Px(32f), -UiTheme.S8));
                var chipRow = UiKit.Row(chipsRt, UiTheme.Px(6f), TextAnchor.MiddleLeft);
                chipRow.childControlWidth = true;
                chipRow.childControlHeight = true;
                chipRow.childForceExpandWidth = false;
                chipRow.childForceExpandHeight = false;
                var chips = chipsRt.gameObject.AddComponent<RewardChipRow>();
                Wire(chips, ("font", fonts.Number), ("sprites", library));
                chips.Ink = UiTheme.Ink;

                var cellState = UiKit.Label("State", cellRt, fonts.Body, UiTheme.Caption, UiTheme.Ink2,
                                            TextAlignmentOptions.Left, out var sRt, "");
                Anchor(sRt, new Vector2(0f, 0f), new Vector2(1f, 0.5f),
                       new Vector2(UiTheme.S10, UiTheme.S8), new Vector2(-UiTheme.Px(32f), 0f));

                var markRt = UiKit.Node("Check", cellRt);
                Anchor(markRt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                       new Vector2(-UiTheme.S8 - UiTheme.Px(22f), -UiTheme.Py(11f)),
                       new Vector2(-UiTheme.S8, UiTheme.Py(11f)));
                var mark = markRt.gameObject.AddComponent<Image>();
                mark.sprite = UiArt.Icon("check_circle");
                mark.color = UiTheme.Success;
                mark.preserveAspect = true;
                mark.raycastTarget = false;
                markRt.gameObject.SetActive(false);

                var lockRt = UiKit.Node("Lock", cellRt);
                Anchor(lockRt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                       new Vector2(-UiTheme.S8 - UiTheme.Px(20f), -UiTheme.Py(10f)),
                       new Vector2(-UiTheme.S8, UiTheme.Py(10f)));
                var padlockImage = lockRt.gameObject.AddComponent<Image>();
                padlockImage.sprite = UiArt.Icon("lock");
                padlockImage.color = UiTheme.Premium;
                padlockImage.preserveAspect = true;
                padlockImage.raycastTarget = false;
                lockRt.gameObject.SetActive(false);

                return (cellFace, chips, cellState, markRt.gameObject, lockRt.gameObject,
                        cellButton, cellGroup);
            }

            var free = Cell("Free", true);
            var paid = Cell("Paid", false);

            var knobRt = UiKit.Node("Knob", rt);
            Anchor(knobRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                   new Vector2(-knob * 0.5f, -knob * 0.5f), new Vector2(knob * 0.5f, knob * 0.5f));
            var knobFace = knobRt.gameObject.AddComponent<Image>();
            knobFace.sprite = UiArt.Pill;
            knobFace.type = Image.Type.Sliced;
            knobFace.color = UiTheme.Premium;
            knobFace.raycastTarget = false;
            var knobLabel = UiKit.Label("Text", knobRt, fonts.Number, UiTheme.Caption,
                                        UiTheme.Panel, TextAlignmentOptions.Center, out var klRt, "1");
            Stretch(klRt);

            var row = go.AddComponent<PassRow>();
            Wire(row,
                 ("freeFace", free.face), ("freeChips", free.chips), ("freeStateLabel", free.state),
                 ("freeCheck", free.check), ("freeButton", free.button), ("freeGroup", free.group),
                 ("knobFace", knobFace), ("knobLabel", knobLabel),
                 ("paidFace", paid.face), ("paidChips", paid.chips), ("paidStateLabel", paid.state),
                 ("paidLock", paid.padlock), ("paidCheck", paid.check),
                 ("paidButton", paid.button), ("paidGroup", paid.group));
            return SavePrefab<PassRow>(go, "PassRow");
        }

        // ------------------------------------------------------------------ 가전 상세 (2b)

        /// <summary>
        /// Appliance detail: a character stage over a sheet carrying 능력치 / 스킬 / 이야기 and
        /// the two actions. Sits above the whole lobby, so it covers the bottom bar too.
        /// </summary>
        /// <summary>
        /// 일러스트 정보창, measured off the running build: the illustration fills the top 56%
        /// over the appliance's own pastel page colour, and a 18px-rounded sheet below carries
        /// the three tabs. Every colour here is a placeholder - ArtViewerScreen repaints the
        /// page, the fact cells and the underline from unit_profile when it opens.
        /// </summary>
        static ArtViewerScreen BuildArtViewer(RectTransform parent, Fonts fonts,
                                              SpriteLibrary library)
        {
            var root = UiKit.Node("ArtViewer", parent);
            Stretch(root);
            var page = root.gameObject.AddComponent<Image>();
            page.color = UiTheme.Panel2;

            const float StageShare = 521.9f / 932f;   // the build's split, as a fraction
            float side = UiTheme.Px(16f);

            // ---------------------------------------------------------------- stage
            var stage = UiKit.Node("Stage", root);
            Anchor(stage, new Vector2(0f, 1f - StageShare), new Vector2(1f, 1f),
                   Vector2.zero, Vector2.zero);
            stage.gameObject.AddComponent<RectMask2D>();

            var illustRt = UiKit.Node("Illustration", stage);
            Stretch(illustRt);
            var illustration = illustRt.gameObject.AddComponent<Image>();
            illustration.preserveAspect = true;
            illustration.raycastTarget = false;

            // Shown instead when an appliance has no illustration yet: its battle chibi and a
            // plain label, so a missing drawing reads as missing rather than as a blank screen.
            var missingRt = UiKit.Node("MissingArt", stage);
            Stretch(missingRt);
            var missingChibiRt = UiKit.Node("Chibi", missingRt);
            Anchor(missingChibiRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                   new Vector2(-UiTheme.Px(90f), -UiTheme.Py(100f)),
                   new Vector2(UiTheme.Px(90f), UiTheme.Py(100f)));
            var missingChibi = missingChibiRt.gameObject.AddComponent<Image>();
            missingChibi.preserveAspect = true;
            missingChibi.raycastTarget = false;
            var missingLabel = UiKit.Label("Label", missingRt, fonts.Body, UiTheme.Py(13f),
                                           UiTheme.Ink2, TextAlignmentOptions.Center,
                                           out var mlRt, "일러스트 준비 중");
            Anchor(mlRt, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                   new Vector2(side, -UiTheme.Py(140f)), new Vector2(-side, -UiTheme.Py(114f)));
            missingRt.gameObject.SetActive(false);

            // ---- title plate: rarity pill, name, tagline.
            var title = UiKit.Node("Title", stage);
            Anchor(title, new Vector2(0f, 1f), new Vector2(0f, 1f),
                   new Vector2(UiTheme.Px(12f), -UiTheme.Py(69.4f)),
                   new Vector2(UiTheme.Px(212f), -UiTheme.Py(10f)));
            var titleBg = title.gameObject.AddComponent<Image>();
            titleBg.sprite = GeneratedArt.RoundedNineSlice("ui_card", Mathf.RoundToInt(UiTheme.Px(12f)));
            titleBg.type = Image.Type.Sliced;
            titleBg.color = new Color(1f, 1f, 1f, 0.78f);
            titleBg.raycastTarget = false;

            var rarityRt = UiKit.Node("RarityPill", title);
            Anchor(rarityRt, new Vector2(0f, 1f), new Vector2(0f, 1f),
                   new Vector2(UiTheme.Px(12f), -UiTheme.Py(28.6f)),
                   new Vector2(UiTheme.Px(30.5f), -UiTheme.Py(12.6f)));
            var rarityPill = rarityRt.gameObject.AddComponent<Image>();
            rarityPill.sprite = GeneratedArt.RoundedNineSlice("ui_tag", Mathf.RoundToInt(UiTheme.Px(5f)));
            rarityPill.type = Image.Type.Sliced;
            rarityPill.color = UiTheme.RarityR;
            rarityPill.raycastTarget = false;
            var rarityLabel = UiKit.Label("Text", rarityRt, fonts.Number, UiTheme.Py(12f),
                                          Color.white, TextAlignmentOptions.Center,
                                          out var rlRt, "R");
            // A 16pt plate cannot contain a 12pt line, so the label overhangs it vertically
            // rather than being clipped to nothing - TMP drops the whole line when the rect is
            // too short. Only the vertical axis is outset; widening it would push the glyph
            // out from under the name.
            Anchor(rlRt, Vector2.zero, Vector2.one,
                   new Vector2(0f, -UiTheme.Py(6f)), new Vector2(0f, UiTheme.Py(6f)));

            var nameLabel = UiKit.Label("Name", title, fonts.Title, UiTheme.Py(24f), UiTheme.Ink,
                                        TextAlignmentOptions.Left, out var nmRt, "");
            nameLabel.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(nmRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(36.5f), -UiTheme.Py(34f)),
                   new Vector2(-UiTheme.Px(4f), -UiTheme.Py(6f)));

            var titleTag = UiKit.Label("Tagline", title, fonts.Body, UiTheme.Py(12f),
                                       UiTheme.Ink2, TextAlignmentOptions.Left, out var ttRt, "");
            titleTag.textWrappingMode = TextWrappingModes.NoWrap;
            titleTag.overflowMode = TextOverflowModes.Ellipsis;
            Anchor(ttRt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(UiTheme.Px(12f), UiTheme.Py(6f)),
                   new Vector2(-UiTheme.Px(12f), UiTheme.Py(25f)));

            // ---- close, arrows, index.
            var close = UiKit.Button("Close", stage, fonts.Title, "×", UiButtonKind.Glass,
                                     out var closeRt, UiTheme.Py(40f), UiTheme.Py(22f));
            Anchor(closeRt, new Vector2(1f, 1f), new Vector2(1f, 1f),
                   new Vector2(-UiTheme.Sq(50f), -UiTheme.Sq(50f)),
                   new Vector2(-UiTheme.Sq(10f), -UiTheme.Sq(10f)));
            Circle(closeRt, 0.85f);

            UiButton Arrow(string id, string glyph, bool left)
            {
                var b = UiKit.Button(id, stage, fonts.Title, glyph, UiButtonKind.Glass,
                                     out var rt, UiTheme.Py(52f), UiTheme.Py(28f));
                float x = left ? UiTheme.Px(8f) : -UiTheme.Px(44f);
                Anchor(rt, new Vector2(left ? 0f : 1f, 0.5f), new Vector2(left ? 0f : 1f, 0.5f),
                       new Vector2(x, -UiTheme.Py(26f)),
                       new Vector2(x + UiTheme.Px(36f), UiTheme.Py(26f)));
                Plate(rt, GeneratedArt.RoundedNineSlice("ui_slot", Mathf.RoundToInt(UiTheme.Px(10f))), 0.7f);
                return b;
            }
            var prev = Arrow("Prev", "‹", true);
            var next = Arrow("Next", "›", false);

            var idxRt = UiKit.Node("Index", stage);
            Anchor(idxRt, new Vector2(1f, 0f), new Vector2(1f, 0f),
                   new Vector2(-UiTheme.Px(59.6f), UiTheme.Py(12f)),
                   new Vector2(-UiTheme.Px(12f), UiTheme.Py(29f)));
            Plate(idxRt, GeneratedArt.RoundedNineSlice("ui_tag6", Mathf.RoundToInt(UiTheme.Px(6f))), 0.75f);
            var indexLabel = UiKit.Label("Text", idxRt, fonts.Number, UiTheme.Py(11f),
                                         UiTheme.Ink2, TextAlignmentOptions.Center,
                                         out var ixRt, "1 / 12");
            Stretch(ixRt);

            var ownRt = UiKit.Node("NotOwned", stage);
            Anchor(ownRt, new Vector2(0f, 0f), new Vector2(0f, 0f),
                   new Vector2(UiTheme.Sq(12f), UiTheme.Sq(12f)),
                   new Vector2(UiTheme.Px(70f), UiTheme.Py(31f)));
            Plate(ownRt, GeneratedArt.RoundedNineSlice("ui_tag6", Mathf.RoundToInt(UiTheme.Px(6f))), 0.75f);
            UiKit.Label("Text", ownRt, fonts.Body, UiTheme.Py(11f), UiTheme.Ink2,
                        TextAlignmentOptions.Center, out var owRt, "미보유");
            Stretch(owRt);
            ownRt.gameObject.SetActive(false);

            // ---------------------------------------------------------------- sheet
            var sheet = UiKit.Node("Sheet", root);
            Anchor(sheet, Vector2.zero, new Vector2(1f, 1f - StageShare), Vector2.zero, Vector2.zero);
            var sheetBg = sheet.gameObject.AddComponent<Image>();
            sheetBg.sprite = GeneratedArt.RoundedTopNineSlice("ui_sheet", Mathf.RoundToInt(UiTheme.Px(18f)));
            sheetBg.type = Image.Type.Sliced;
            sheetBg.color = UiTheme.Panel;

            // ---- three tabs across the top of the sheet.
            var tabs = UiKit.Node("Tabs", sheet);
            Anchor(tabs, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(0f, -UiTheme.Py(49f)), Vector2.zero);

            UiButton Tab(string id, string text, int index,
                         out RectTransform underline, out TextMeshProUGUI label)
            {
                float w = UiTheme.Px(132.7f), gap = UiTheme.Px(4f);
                float x = UiTheme.Px(12f) + index * (w + gap);
                var b = UiKit.Button(id, tabs, fonts.Body, "", UiButtonKind.Glass,
                                     out var rt, UiTheme.Py(39f), UiTheme.Py(14f));
                Anchor(rt, new Vector2(0f, 0f), new Vector2(0f, 0f),
                       new Vector2(x, UiTheme.Py(10f)), new Vector2(x + w, UiTheme.Py(49f)));
                var img = rt.GetComponent<Image>();
                if (img != null) { img.color = new Color(0f, 0f, 0f, 0f); img.sprite = null; }

                label = UiKit.Label("TabText", rt, fonts.Body, UiTheme.Py(14f), UiTheme.Ink2,
                                    TextAlignmentOptions.Center, out var lrt, text);
                label.fontStyle = FontStyles.Bold;
                Anchor(lrt, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -UiTheme.Py(2f)));

                underline = UiKit.Node("Underline", rt);
                Anchor(underline, new Vector2(0f, 0f), new Vector2(1f, 0f),
                       Vector2.zero, new Vector2(0f, UiTheme.Py(2f)));
                var line = underline.gameObject.AddComponent<Image>();
                line.color = new Color(0f, 0f, 0f, 0f);
                line.raycastTarget = false;
                return b;
            }
            var profileTab = Tab("ProfileTab", "프로필", 0, out var profileUnderline, out var profileTabLabel);
            var faceTab = Tab("FaceTab", "표정", 1, out var faceUnderline, out var faceTabLabel);
            var sheetTab = Tab("SheetTab", "설정화", 2, out var sheetUnderline, out var sheetTabLabel);

            // ---- the body area the three tabs share.
            var bodyArea = UiKit.Node("Body", sheet);
            Anchor(bodyArea, Vector2.zero, new Vector2(1f, 1f),
                   Vector2.zero, new Vector2(0f, -UiTheme.Py(49f)));
            bodyArea.gameObject.AddComponent<RectMask2D>();

            RectTransform Scroller(string id, out RectTransform column)
            {
                var view = UiKit.Node(id, bodyArea);
                Stretch(view);
                var scroll = view.gameObject.AddComponent<ScrollRect>();
                // 뷰포트에 그래픽이 없으면 빈 자리 드래그와 휠이 ScrollRect 까지 닿지 않는다.
                UiKit.ScrollCatcher(view);
                scroll.horizontal = false;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.scrollSensitivity = 40f;

                column = UiKit.Node("Column", view);
                Anchor(column, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
                column.pivot = new Vector2(0.5f, 1f);
                var col = column.gameObject.AddComponent<VerticalLayoutGroup>();
                col.spacing = UiTheme.Py(10f);
                col.padding = new RectOffset((int)side, (int)side,
                                             (int)UiTheme.Py(12f), (int)UiTheme.Py(16f));
                col.childControlWidth = true;
                col.childControlHeight = true;
                col.childForceExpandWidth = true;
                col.childForceExpandHeight = false;
                var fit = column.gameObject.AddComponent<ContentSizeFitter>();
                fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                scroll.viewport = view;
                scroll.content = column;
                return view;
            }

            RectTransform Row(RectTransform column, string id, float pageHeight)
            {
                var rt = UiKit.Node(id, column);
                var le = rt.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = le.minHeight = UiTheme.Py(pageHeight);
                return rt;
            }

            // ---------------------------------------------------------------- 프로필
            var profileBody = Scroller("ProfileBody", out var profileColumn);

            var tagRow = Row(profileColumn, "Tagline", 23f);
            var taglineLabel = UiKit.Label("Text", tagRow, fonts.Title, UiTheme.Py(18f),
                                           UiTheme.Primary, TextAlignmentOptions.TopLeft,
                                           out var tgRt, "");
            taglineLabel.textWrappingMode = TextWrappingModes.NoWrap;
            taglineLabel.overflowMode = TextOverflowModes.Ellipsis;
            Stretch(tgRt);

            var profRow = Row(profileColumn, "Profile", 66f);
            var profileLabel = UiKit.Label("Text", profRow, fonts.Body, UiTheme.Py(13.5f),
                                           UiTheme.Ink, TextAlignmentOptions.TopLeft,
                                           out var pfRt, "");
            profileLabel.textWrappingMode = TextWrappingModes.Normal;
            profileLabel.lineSpacing = 12f;
            Stretch(pfRt);

            var facts = Row(profileColumn, "Facts", 150f);
            var factGrid = facts.gameObject.AddComponent<GridLayoutGroup>();
            factGrid.cellSize = new Vector2(UiTheme.Px(196f), UiTheme.Py(46f));
            factGrid.spacing = new Vector2(UiTheme.Sq(6f), UiTheme.Sq(6f));
            factGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            factGrid.constraintCount = 2;

            var factCells = new Object[ArtViewerScreen.FactCount];
            var factKeys = new Object[ArtViewerScreen.FactCount];
            var factValues = new Object[ArtViewerScreen.FactCount];
            for (int i = 0; i < ArtViewerScreen.FactCount; i++)
            {
                var cell = UiKit.Node("Fact" + i, facts);
                var cellBg = cell.gameObject.AddComponent<Image>();
                cellBg.sprite = GeneratedArt.RoundedNineSlice("ui_fact", Mathf.RoundToInt(UiTheme.Px(8f)));
                cellBg.type = Image.Type.Sliced;
                cellBg.color = UiTheme.Panel2;
                cellBg.raycastTarget = false;
                factCells[i] = cellBg;

                var k = UiKit.Label("Key", cell, fonts.Body, UiTheme.Py(10.5f), UiTheme.Ink2,
                                    TextAlignmentOptions.TopLeft, out var kRt, "");
                k.textWrappingMode = TextWrappingModes.NoWrap;
                Anchor(kRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(UiTheme.Px(9f), -UiTheme.Py(23f)),
                       new Vector2(-UiTheme.Px(9f), -UiTheme.Py(5f)));
                factKeys[i] = k;

                var v = UiKit.Label("Value", cell, fonts.Body, UiTheme.Py(12.5f), UiTheme.Ink,
                                    TextAlignmentOptions.TopLeft, out var vRt, "");
                v.fontStyle = FontStyles.Bold;
                v.textWrappingMode = TextWrappingModes.NoWrap;
                v.overflowMode = TextOverflowModes.Ellipsis;
                Anchor(vRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(UiTheme.Px(9f), -UiTheme.Py(40f)),
                       new Vector2(-UiTheme.Px(9f), -UiTheme.Py(21f)));
                factValues[i] = v;
            }

            var linesRoot = UiKit.Node("Lines", profileColumn);
            var linesFlow = linesRoot.gameObject.AddComponent<GridLayoutGroup>();
            linesFlow.cellSize = new Vector2(UiTheme.Px(194f), UiTheme.Py(30f));
            linesFlow.spacing = new Vector2(UiTheme.Sq(6f), UiTheme.Sq(6f));
            linesFlow.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            linesFlow.constraintCount = 2;
            var linesFit = linesRoot.gameObject.AddComponent<ContentSizeFitter>();
            linesFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var detailRow = Row(profileColumn, "DetailRow", 45f);
            var detailButton = UiKit.Button("Detail", detailRow, fonts.Body, "성장·장비 보기",
                                            UiButtonKind.Secondary, out var dbRt,
                                            UiTheme.Py(45f), UiTheme.Py(14f));
            Stretch(dbRt);
            var detailLabel = dbRt.GetComponentInChildren<TextMeshProUGUI>();

            // ---------------------------------------------------------------- 표정
            var faceBody = Scroller("FaceBody", out var faceColumn);
            var faceGridRow = UiKit.Node("Faces", faceColumn);
            var faceGrid = faceGridRow.gameObject.AddComponent<GridLayoutGroup>();
            faceGrid.cellSize = new Vector2(UiTheme.Px(127.3f), UiTheme.Py(160.7f));
            faceGrid.spacing = new Vector2(UiTheme.Sq(8f), UiTheme.Sq(8f));
            faceGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            faceGrid.constraintCount = 3;
            var faceFit = faceGridRow.gameObject.AddComponent<ContentSizeFitter>();
            faceFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var faceFrames = new Object[3];
            var faceImages = new Object[3];
            var faceCaptions = new Object[3];
            for (int i = 0; i < 3; i++)
            {
                var fig = UiKit.Node("Face" + i, faceGridRow);
                var figBg = fig.gameObject.AddComponent<Image>();
                figBg.sprite = GeneratedArt.RoundedNineSlice("ui_card", Mathf.RoundToInt(UiTheme.Px(12f)));
                figBg.type = Image.Type.Sliced;
                figBg.color = UiTheme.Panel2;
                figBg.raycastTarget = false;
                faceFrames[i] = figBg;

                var imgRt = UiKit.Node("Image", fig);
                Anchor(imgRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(0f, -UiTheme.Py(133.7f)), Vector2.zero);
                var img = imgRt.gameObject.AddComponent<Image>();
                img.preserveAspect = true;
                img.raycastTarget = false;
                faceImages[i] = img;

                var cap = UiKit.Label("Caption", fig, fonts.Body, UiTheme.Py(11.5f), UiTheme.Ink2,
                                      TextAlignmentOptions.Center, out var cRt, "");
                Anchor(cRt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                       Vector2.zero, new Vector2(0f, UiTheme.Py(27f)));
                faceCaptions[i] = cap;
            }

            var noteRow = Row(faceColumn, "Note", 34f);
            var faceNote = UiKit.Label("Text", noteRow, fonts.Body, UiTheme.Py(11.5f),
                                       UiTheme.Ink2, TextAlignmentOptions.TopLeft,
                                       out var fnRt, "");
            faceNote.textWrappingMode = TextWrappingModes.Normal;
            Stretch(fnRt);

            // ---------------------------------------------------------------- 설정화
            var sheetBody = Scroller("SheetBody", out var sheetColumn);

            RectTransform Heading(string id, string text, out TextMeshProUGUI small)
            {
                var rt = Row(sheetColumn, id, 19f);
                UiKit.Label("Text", rt, fonts.Title, UiTheme.Py(13f), UiTheme.Ink,
                            TextAlignmentOptions.Left, out var hRt, text);
                Anchor(hRt, Vector2.zero, new Vector2(0f, 1f), Vector2.zero,
                       new Vector2(UiTheme.Px(120f), 0f));
                small = UiKit.Label("Small", rt, fonts.Body, UiTheme.Py(11.5f), UiTheme.Ink2,
                                    TextAlignmentOptions.Left, out var sRt, "");
                small.textWrappingMode = TextWrappingModes.NoWrap;
                small.overflowMode = TextOverflowModes.Ellipsis;
                Anchor(sRt, Vector2.zero, new Vector2(1f, 1f),
                       new Vector2(UiTheme.Px(124f), 0f), Vector2.zero);
                return rt;
            }

            Heading("TurnHead", "정면 · 측면 · 후면", out _);
            var turnRow = Row(sheetColumn, "Turn", 150f);
            var turnImage = turnRow.gameObject.AddComponent<Image>();
            turnImage.preserveAspect = true;
            turnImage.raycastTarget = false;
            var turnMissing = UiKit.Node("Missing", turnRow);
            Stretch(turnMissing);
            UiKit.Label("Text", turnMissing, fonts.Body, UiTheme.Py(12f), UiTheme.Ink2,
                        TextAlignmentOptions.Center, out var tmRt, "3면도는 준비 중이에요");
            Stretch(tmRt);
            turnMissing.gameObject.SetActive(false);

            Heading("KeysHead", "주요 요소", out var motifLabel);
            var keysRow = Row(sheetColumn, "Keys", 150f);
            var keysImage = keysRow.gameObject.AddComponent<Image>();
            keysImage.preserveAspect = true;
            keysImage.raycastTarget = false;
            var keysMissing = UiKit.Node("Missing", keysRow);
            Stretch(keysMissing);
            UiKit.Label("Text", keysMissing, fonts.Body, UiTheme.Py(12f), UiTheme.Ink2,
                        TextAlignmentOptions.Center, out var kmRt, "주요 요소는 준비 중이에요");
            Stretch(kmRt);
            keysMissing.gameObject.SetActive(false);

            Heading("SdHead", "전투 SD", out _);
            var sdRow = Row(sheetColumn, "Sd", 112f);
            var sdGrid = sdRow.gameObject.AddComponent<GridLayoutGroup>();
            sdGrid.cellSize = new Vector2(UiTheme.Px(96f), UiTheme.Py(104f));
            sdGrid.spacing = new Vector2(UiTheme.Px(16f), 0f);
            sdGrid.childAlignment = TextAnchor.MiddleCenter;
            sdGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            sdGrid.constraintCount = 3;

            var sdImages = new Object[3];
            var sdFrames = new Object[3];
            string[] sdCaptions = { "기본", "스킬", "과열" };
            for (int i = 0; i < 3; i++)
            {
                var cell = UiKit.Node("Sd" + i, sdRow);
                var cellBg = cell.gameObject.AddComponent<Image>();
                cellBg.sprite = GeneratedArt.RoundedNineSlice("ui_fact", Mathf.RoundToInt(UiTheme.Px(8f)));
                cellBg.type = Image.Type.Sliced;
                cellBg.color = UiTheme.Panel2;
                cellBg.raycastTarget = false;
                sdFrames[i] = cellBg;

                var imgRt = UiKit.Node("Image", cell);
                Anchor(imgRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(UiTheme.Px(6f), -UiTheme.Py(84f)),
                       new Vector2(-UiTheme.Sq(6f), -UiTheme.Sq(6f)));
                var img = imgRt.gameObject.AddComponent<Image>();
                img.preserveAspect = true;
                img.raycastTarget = false;
                sdImages[i] = img;

                UiKit.Label("Caption", cell, fonts.Body, UiTheme.Py(11f), UiTheme.Ink2,
                            TextAlignmentOptions.Center, out var cRt, sdCaptions[i]);
                Anchor(cRt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                       Vector2.zero, new Vector2(0f, UiTheme.Py(20f)));
            }

            // ---------------------------------------------------------------- wire
            var screen = root.gameObject.AddComponent<ArtViewerScreen>();
            Wire(screen,
                 ("pageBackground", page), ("sheetBackground", sheetBg),
                 ("illustration", illustration), ("missingArtRoot", missingRt.gameObject),
                 ("missingArtChibi", missingChibi), ("missingArtLabel", missingLabel),
                 ("rarityPill", rarityPill), ("rarityLabel", rarityLabel),
                 ("nameLabel", nameLabel), ("titleTagline", titleTag),
                 ("notOwnedBadge", ownRt.gameObject), ("indexLabel", indexLabel),
                 ("closeButton", close), ("prevButton", prev), ("nextButton", next),
                 ("profileTab", profileTab), ("faceTab", faceTab), ("sheetTab", sheetTab),
                 ("profileUnderline", profileUnderline), ("faceUnderline", faceUnderline),
                 ("sheetUnderline", sheetUnderline),
                 ("profileTabLabel", profileTabLabel), ("faceTabLabel", faceTabLabel),
                 ("sheetTabLabel", sheetTabLabel),
                 ("profileBody", profileBody.gameObject), ("taglineLabel", taglineLabel),
                 ("profileLabel", profileLabel), ("linesRoot", linesRoot),
                 ("detailButton", detailButton), ("detailLabel", detailLabel),
                 ("lineChipSprite", GeneratedArt.RoundedNineSlice("ui_card", Mathf.RoundToInt(UiTheme.Px(12f)))),
                 ("lineChipOutline", GeneratedArt.RoundedOutlineNineSlice("ui_card_line",
                     Mathf.RoundToInt(UiTheme.Px(12f)), Mathf.Max(1, Mathf.RoundToInt(UiTheme.Px(1.5f))))),
                 ("bodyFont", fonts.Body),
                 ("faceBody", faceBody.gameObject), ("faceNote", faceNote),
                 ("sheetBody", sheetBody.gameObject),
                 ("turnImage", turnImage), ("turnMissing", turnMissing.gameObject),
                 ("keysImage", keysImage), ("keysMissing", keysMissing.gameObject),
                 ("motifLabel", motifLabel), ("sprites", library));
            WireArray(screen, "factCells", factCells);
            WireArray(screen, "factKeys", factKeys);
            WireArray(screen, "factValues", factValues);
            WireArray(screen, "faceFrames", faceFrames);
            WireArray(screen, "faceImages", faceImages);
            WireArray(screen, "faceCaptions", faceCaptions);
            WireArray(screen, "sdImages", sdImages);
            WireArray(screen, "sdFrames", sdFrames);

            root.gameObject.SetActive(false);
            return screen;
        }

        /// <summary>Makes a button's own background a translucent white disc.</summary>
        static void Circle(RectTransform rt, float alpha)
        {
            var img = rt.GetComponent<Image>();
            if (img == null) return;
            img.sprite = GeneratedArt.Disc("ui_disc");
            img.type = Image.Type.Simple;
            img.color = new Color(1f, 1f, 1f, alpha);
        }

        /// <summary>Puts a translucent white plate behind a node, under its contents.</summary>
        static void Plate(RectTransform rt, Sprite sprite, float alpha)
        {
            var img = rt.GetComponent<Image>() ?? rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.color = new Color(1f, 1f, 1f, alpha);
        }

        /// <summary>
        /// 장비 가방, measured off the running build. It is a modal there - a dimmed page with
        /// a 16px-radius card inset 16 - so it is built the same way here, with the detail
        /// swapped in over the bag rather than stacked as a second overlay.
        /// </summary>
        static GearBagScreen BuildGearBag(RectTransform parent, Fonts fonts, SpriteLibrary library,
                                          Prefabs prefabs)
        {
            var root = UiKit.Node("GearBag", parent);
            Stretch(root);
            var dim = root.gameObject.AddComponent<Image>();
            dim.color = new Color(UiTheme.Ink.r, UiTheme.Ink.g, UiTheme.Ink.b, 0.55f);

            float pad = UiTheme.Px(16f);
            var card = UiKit.Node("Card", root);
            // Centred with an explicit height: GearBagScreen resizes it to its content, the
            // way the build's `height:auto; max-height:min(88vh,760px)` card does.
            card.anchorMin = new Vector2(0f, 0.5f);
            card.anchorMax = new Vector2(1f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.offsetMin = new Vector2(pad, 0f);
            card.offsetMax = new Vector2(-pad, 0f);
            card.sizeDelta = new Vector2(card.sizeDelta.x, UiTheme.Py(680f));
            var cardBg = card.gameObject.AddComponent<Image>();
            cardBg.sprite = GeneratedArt.RoundedNineSlice("ui_modal", Mathf.RoundToInt(UiTheme.Px(16f)));
            cardBg.type = Image.Type.Sliced;
            cardBg.color = UiTheme.Panel;

            // One scrolling column per panel, both inside the card.
            RectTransform Panel(string id, out RectTransform column)
            {
                var view = UiKit.Node(id, card);
                Stretch(view, UiTheme.Px(2f));
                view.gameObject.AddComponent<RectMask2D>();
                var scroll = view.gameObject.AddComponent<ScrollRect>();
                // 뷰포트에 그래픽이 없으면 빈 자리 드래그와 휠이 ScrollRect 까지 닿지 않는다.
                UiKit.ScrollCatcher(view);
                scroll.horizontal = false;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.scrollSensitivity = 40f;

                column = UiKit.Node("Column", view);
                Anchor(column, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
                column.pivot = new Vector2(0.5f, 1f);
                var col = column.gameObject.AddComponent<VerticalLayoutGroup>();
                col.spacing = UiTheme.Py(12f);
                col.padding = new RectOffset((int)pad, (int)pad, (int)UiTheme.Py(16f), (int)UiTheme.Py(16f));
                col.childControlWidth = true;
                col.childControlHeight = true;
                col.childForceExpandWidth = true;
                col.childForceExpandHeight = false;
                var fit = column.gameObject.AddComponent<ContentSizeFitter>();
                fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                scroll.viewport = view;
                scroll.content = column;
                return view;
            }

            RectTransform Row(RectTransform column, string id, float pageHeight)
            {
                var rt = UiKit.Node(id, column);
                var le = rt.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = le.minHeight = UiTheme.Py(pageHeight);
                return rt;
            }

            // ---------------------------------------------------------------- 가방
            var bag = Panel("Bag", out var bagCol);

            var head = Row(bagCol, "Head", 30f);
            UiKit.Label("Title", head, fonts.Title, UiTheme.Py(24f), UiTheme.Ink,
                        TextAlignmentOptions.BottomLeft, out var htRt, "장비 가방");
            Anchor(htRt, Vector2.zero, new Vector2(0f, 1f), Vector2.zero,
                   new Vector2(UiTheme.Px(92f), 0f));
            var countLabel = UiKit.Label("Count", head, fonts.Body, UiTheme.Py(12f), UiTheme.Ink2,
                                         TextAlignmentOptions.BottomLeft, out var hcRt, "");
            Anchor(hcRt, Vector2.zero, new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(96f), 0f), new Vector2(0f, -UiTheme.Py(3f)));

            // ---- the two currencies, as the same pale pills the 가전 tab uses.
            var purse = Row(bagCol, "Purse", 32f);
            var purseRow = UiKit.Row(purse, UiTheme.Px(6f), TextAnchor.MiddleLeft);
            purseRow.childControlWidth = false;
            purseRow.childControlHeight = false;
            purseRow.childForceExpandWidth = false;

            TextMeshProUGUI Purse(string id, string name, Sprite iconSprite)
            {
                var pill = UiKit.Node(id, purse);
                pill.sizeDelta = new Vector2(UiTheme.Px(158f), UiTheme.Py(32f));
                var bg = pill.gameObject.AddComponent<Image>();
                bg.sprite = GeneratedArt.RoundedNineSlice("ui_pill9", Mathf.RoundToInt(UiTheme.Px(9f)));
                bg.type = Image.Type.Sliced;
                bg.color = UiTheme.Panel;
                bg.raycastTarget = false;
                var line = UiKit.Node("Outline", pill);
                Stretch(line);
                var lineImg = line.gameObject.AddComponent<Image>();
                lineImg.sprite = GeneratedArt.RoundedOutlineNineSlice("ui_pill9_line",
                    Mathf.RoundToInt(UiTheme.Px(9f)), Mathf.Max(1, Mathf.RoundToInt(UiTheme.Px(1f))));
                lineImg.type = Image.Type.Sliced;
                lineImg.color = UiTheme.PanelLine;
                lineImg.raycastTarget = false;

                var icoRt = UiKit.Node("Icon", pill);
                icoRt.anchorMin = icoRt.anchorMax = new Vector2(0f, 0.5f);
                icoRt.sizeDelta = new Vector2(UiTheme.Py(18f), UiTheme.Py(18f));
                icoRt.anchoredPosition = new Vector2(UiTheme.Px(20f), 0f);
                var ico = icoRt.gameObject.AddComponent<Image>();
                ico.sprite = iconSprite;
                ico.enabled = iconSprite != null;
                ico.preserveAspect = true;
                ico.raycastTarget = false;

                UiKit.Label("Name", pill, fonts.Body, UiTheme.Py(12f), UiTheme.Ink2,
                            TextAlignmentOptions.Left, out var nRt, name);
                Anchor(nRt, Vector2.zero, Vector2.one,
                       new Vector2(UiTheme.Px(32f), 0f), new Vector2(-UiTheme.Px(8f), 0f));
                var value = UiKit.Label("Value", pill, fonts.Number, UiTheme.Py(14f), UiTheme.Ink,
                                        TextAlignmentOptions.Right, out var vRt, "0");
                Anchor(vRt, Vector2.zero, Vector2.one,
                       new Vector2(UiTheme.Px(32f), 0f), new Vector2(-UiTheme.Px(10f), 0f));
                return value;
            }
            var stoneLabel = Purse("Stone", "장비 강화석", LoadIcon("stone"));
            var essenceLabel = Purse("Essence", "전용 정수", LoadIcon("essence"));

            // ---- the four bulk actions, 2x2.
            var tools = Row(bagCol, "Tools", 116f);
            var toolGrid = tools.gameObject.AddComponent<GridLayoutGroup>();
            toolGrid.cellSize = new Vector2(UiTheme.Px(160f), UiTheme.Py(55f));
            toolGrid.spacing = new Vector2(UiTheme.Sq(6f), UiTheme.Sq(6f));
            toolGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            toolGrid.constraintCount = 2;

            UiButton Tool(string id, string text, out TextMeshProUGUI sub)
            {
                var b = UiKit.Button(id, tools, fonts.Body, text, UiButtonKind.Secondary,
                                     out var rt, UiTheme.Py(55f), UiTheme.Py(12.5f));
                // The label sits high so the subtitle has its own line beneath it.
                var label = rt.GetComponentInChildren<TextMeshProUGUI>();
                var lrt = (RectTransform)label.transform;
                Anchor(lrt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(UiTheme.Px(6f), -UiTheme.Py(30f)),
                       new Vector2(-UiTheme.Px(6f), -UiTheme.Py(7f)));
                sub = UiKit.Label("Sub2", rt, fonts.Body, UiTheme.Py(12f), UiTheme.Ink2,
                                  TextAlignmentOptions.Center, out var srt, "");
                sub.textWrappingMode = TextWrappingModes.NoWrap;
                sub.overflowMode = TextOverflowModes.Ellipsis;
                Anchor(srt, Vector2.zero, new Vector2(1f, 0f),
                       new Vector2(UiTheme.Sq(6f), UiTheme.Sq(6f)),
                       new Vector2(-UiTheme.Px(6f), UiTheme.Py(28f)));
                return b;
            }
            var dismantleButton = Tool("Dismantle", "1등급 일괄 분해", out var dismantleSub);
            var fuse1Button = Tool("Fuse1", "합성 1→2등급", out var fuse1Sub);
            var fuse2Button = Tool("Fuse2", "합성 2→3등급", out var fuse2Sub);
            var exchangeButton = Tool("Exchange", "전용장비 교환", out var exchangeSub);

            RectTransform Heading(RectTransform column, string id, string text)
            {
                var rt = Row(column, id, 21f);
                UiKit.Label("Text", rt, fonts.Title, UiTheme.Py(17f), UiTheme.Ink,
                            TextAlignmentOptions.Left, out var hRt, text);
                Stretch(hRt);
                return rt;
            }

            var exclusiveHeading = Heading(bagCol, "ExclusiveHead", "전용장비");
            var exclusiveGrid = UiKit.Node("ExclusiveGrid", bagCol);
            GridLayoutGroup Grid(RectTransform rt)
            {
                var g = rt.gameObject.AddComponent<GridLayoutGroup>();
                g.cellSize = new Vector2(UiTheme.Px(60.4f), UiTheme.Py(74f));
                g.spacing = new Vector2(UiTheme.Sq(6f), UiTheme.Sq(6f));
                g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                g.constraintCount = 5;
                var fit = rt.gameObject.AddComponent<ContentSizeFitter>();
                fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                return g;
            }
            Grid(exclusiveGrid);

            Heading(bagCol, "CommonHead", "공용장비");
            var commonGrid = UiKit.Node("CommonGrid", bagCol);
            Grid(commonGrid);

            var emptyRow = Row(bagCol, "Empty", 38f);
            var emptyLabel = UiKit.Label("Text", emptyRow, fonts.Body, UiTheme.Py(12f), UiTheme.Ink2,
                                         TextAlignmentOptions.TopLeft, out var emRt, "");
            emptyLabel.textWrappingMode = TextWrappingModes.Normal;
            Stretch(emRt);

            var noteRow = Row(bagCol, "Note", 40f);
            var note = UiKit.Label("Text", noteRow, fonts.Body, UiTheme.Py(12f), UiTheme.Ink2,
                                   TextAlignmentOptions.TopLeft, out var nRt2,
                                   "합성은 장착하지 않은 것 중 레벨이 낮은 것부터 써요. " +
                                   "분해·합성 때 그 장비에 쓴 강화석의 50%를 돌려받아요.");
            note.textWrappingMode = TextWrappingModes.Normal;
            Stretch(nRt2);

            var closeRow = Row(bagCol, "CloseRow", 45f);
            var closeButton = UiKit.Button("Close", closeRow, fonts.Body, "닫기",
                                           UiButtonKind.Secondary, out var clRt,
                                           UiTheme.Py(45f), UiTheme.Py(14f));
            Stretch(clRt);

            // ---------------------------------------------------------------- 상세
            var detail = Panel("Detail", out var detCol);

            var hero = Row(detCol, "Hero", 76f);
            var heroIconRt = UiKit.Node("IconPlate", hero);
            Anchor(heroIconRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                   new Vector2(0f, -UiTheme.Py(34f)),
                   new Vector2(UiTheme.Px(68f), UiTheme.Py(34f)));
            var heroPlate = heroIconRt.gameObject.AddComponent<Image>();
            heroPlate.sprite = GeneratedArt.RoundedNineSlice("ui_fact", Mathf.RoundToInt(UiTheme.Px(8f)));
            heroPlate.type = Image.Type.Sliced;
            heroPlate.color = UiTheme.Panel2;
            heroPlate.raycastTarget = false;
            var heroIconInner = UiKit.Node("Icon", heroIconRt);
            Stretch(heroIconInner, UiTheme.Px(6f));
            var heroIcon = heroIconInner.gameObject.AddComponent<Image>();
            heroIcon.preserveAspect = true;
            heroIcon.raycastTarget = false;

            var kicker = UiKit.Label("Kicker", hero, fonts.Body, UiTheme.Py(11.5f), UiTheme.Ink2,
                                     TextAlignmentOptions.TopLeft, out var kRt, "");
            kicker.textWrappingMode = TextWrappingModes.NoWrap;
            kicker.overflowMode = TextOverflowModes.Ellipsis;
            Anchor(kRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(78f), -UiTheme.Py(24f)), new Vector2(0f, -UiTheme.Py(4f)));
            var detailName = UiKit.Label("Name", hero, fonts.Title, UiTheme.Py(19f), UiTheme.Ink,
                                         TextAlignmentOptions.TopLeft, out var dnRt, "");
            detailName.textWrappingMode = TextWrappingModes.NoWrap;
            detailName.overflowMode = TextOverflowModes.Ellipsis;
            Anchor(dnRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(78f), -UiTheme.Py(50f)), new Vector2(0f, -UiTheme.Py(24f)));
            var detailWorn = UiKit.Label("Worn", hero, fonts.Body, UiTheme.Py(11.5f), UiTheme.Primary,
                                         TextAlignmentOptions.TopLeft, out var dwRt, "");
            Anchor(dwRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(78f), -UiTheme.Py(72f)), new Vector2(0f, -UiTheme.Py(50f)));

            RectTransform StatBlock(string id, string title,
                                    out TextMeshProUGUI heading,
                                    out TextMeshProUGUI now, out TextMeshProUGUI next)
            {
                var rt = Row(detCol, id, 74f);
                var bg = rt.gameObject.AddComponent<Image>();
                bg.sprite = GeneratedArt.RoundedNineSlice("ui_fact", Mathf.RoundToInt(UiTheme.Px(8f)));
                bg.type = Image.Type.Sliced;
                bg.color = UiTheme.Panel2;
                bg.raycastTarget = false;

                heading = UiKit.Label("Head", rt, fonts.Body, UiTheme.Py(10.5f), UiTheme.Ink2,
                                      TextAlignmentOptions.TopLeft, out var hRt, title);
                heading.textWrappingMode = TextWrappingModes.NoWrap;
                heading.overflowMode = TextOverflowModes.Ellipsis;
                Anchor(hRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(UiTheme.Px(10f), -UiTheme.Py(23f)),
                       new Vector2(-UiTheme.Px(10f), -UiTheme.Py(5f)));

                now = UiKit.Label("Now", rt, fonts.Body, UiTheme.Py(13f), UiTheme.Ink,
                                  TextAlignmentOptions.TopLeft, out var nwRt, "");
                now.fontStyle = FontStyles.Bold;
                now.textWrappingMode = TextWrappingModes.Normal;
                Anchor(nwRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(UiTheme.Px(10f), -UiTheme.Py(46f)),
                       new Vector2(-UiTheme.Px(10f), -UiTheme.Py(23f)));

                next = UiKit.Label("Next", rt, fonts.Body, UiTheme.Py(11.5f), UiTheme.Ink2,
                                   TextAlignmentOptions.TopLeft, out var nxRt, "");
                next.textWrappingMode = TextWrappingModes.Normal;
                Anchor(nxRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(UiTheme.Px(10f), -UiTheme.Py(70f)),
                       new Vector2(-UiTheme.Px(10f), -UiTheme.Py(46f)));
                return rt;
            }

            StatBlock("Stat", "능력", out _, out var statNow, out var statNext);
            var skillBlock = StatBlock("Skill", "스킬 강화", out var skillHeading,
                                       out var skillNow, out var skillNext);

            var actions = Row(detCol, "Actions", 55f);
            var actionGrid = actions.gameObject.AddComponent<GridLayoutGroup>();
            actionGrid.cellSize = new Vector2(UiTheme.Px(160f), UiTheme.Py(55f));
            actionGrid.spacing = new Vector2(UiTheme.Px(6f), 0f);
            actionGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            actionGrid.constraintCount = 2;

            UiButton Action(string id, string text, UiButtonKind kind,
                            out TextMeshProUGUI label, out TextMeshProUGUI sub)
            {
                var b = UiKit.Button(id, actions, fonts.Body, text, kind,
                                     out var rt, UiTheme.Py(55f), UiTheme.Py(13f));
                label = rt.GetComponentInChildren<TextMeshProUGUI>();
                var lrt = (RectTransform)label.transform;
                Anchor(lrt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(UiTheme.Px(6f), -UiTheme.Py(30f)),
                       new Vector2(-UiTheme.Px(6f), -UiTheme.Py(7f)));
                sub = UiKit.Label("Sub2", rt, fonts.Body, UiTheme.Py(11f), UiTheme.Ink2,
                                  TextAlignmentOptions.Center, out var srt, "");
                sub.textWrappingMode = TextWrappingModes.NoWrap;
                sub.overflowMode = TextOverflowModes.Ellipsis;
                Anchor(srt, Vector2.zero, new Vector2(1f, 0f),
                       new Vector2(UiTheme.Px(4f), UiTheme.Py(6f)),
                       new Vector2(-UiTheme.Px(4f), UiTheme.Py(28f)));
                return b;
            }
            var enhanceButton = Action("Enhance", "강화", UiButtonKind.Primary,
                                       out var enhanceLabel, out var enhanceSub);
            var sideButton = Action("Side", "분해", UiButtonKind.Secondary,
                                    out var sideLabel, out var sideSub);

            var backRow = Row(detCol, "BackRow", 45f);
            var backButton = UiKit.Button("Back", backRow, fonts.Body, "돌아가기",
                                          UiButtonKind.Secondary, out var bkRt,
                                          UiTheme.Py(45f), UiTheme.Py(14f));
            Stretch(bkRt);

            // ---------------------------------------------------------------- 전용장비 교환
            var exchange = Panel("Exchange", out var exCol);

            Heading(exCol, "ExchangeHead", "전용장비 교환");
            var exNoteRow = Row(exCol, "ExchangeNote", 54f);
            var exchangeNote = UiKit.Label("Text", exNoteRow, fonts.Body, UiTheme.Py(12f),
                                           UiTheme.Ink2, TextAlignmentOptions.TopLeft,
                                           out var exnRt, "");
            exchangeNote.textWrappingMode = TextWrappingModes.Normal;
            Stretch(exnRt);

            var exchangeList = UiKit.Node("List", exCol);
            var exListCol = exchangeList.gameObject.AddComponent<VerticalLayoutGroup>();
            exListCol.spacing = UiTheme.Py(6f);
            exListCol.childControlWidth = true;
            exListCol.childControlHeight = false;
            exListCol.childForceExpandWidth = true;
            exListCol.childForceExpandHeight = false;
            var exListFit = exchangeList.gameObject.AddComponent<ContentSizeFitter>();
            exListFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var exBackRow = Row(exCol, "ExchangeBack", 45f);
            var exchangeBack = UiKit.Button("ExchangeBack", exBackRow, fonts.Body, "돌아가기",
                                            UiButtonKind.Secondary, out var exbRt,
                                            UiTheme.Py(45f), UiTheme.Py(14f));
            Stretch(exbRt);

            // ---------------------------------------------------------------- wire
            var screen = root.gameObject.AddComponent<GearBagScreen>();
            Wire(screen,
                 ("card", card), ("bagColumn", bagCol), ("detailColumn", detCol),
                 ("exchangeColumn", exCol), ("exchangePanel", exchange.gameObject),
                 ("exchangeNote", exchangeNote), ("exchangeList", exchangeList),
                 ("exchangeRowPrefab", prefabs.GearExchangeRow),
                 ("exchangeBackButton", exchangeBack),
                 ("bagPanel", bag.gameObject), ("countLabel", countLabel),
                 ("stoneLabel", stoneLabel), ("essenceLabel", essenceLabel),
                 ("dismantleButton", dismantleButton), ("dismantleSub", dismantleSub),
                 ("fuse1Button", fuse1Button), ("fuse1Sub", fuse1Sub),
                 ("fuse2Button", fuse2Button), ("fuse2Sub", fuse2Sub),
                 ("exchangeButton", exchangeButton), ("exchangeSub", exchangeSub),
                 ("exclusiveHeading", exclusiveHeading.gameObject),
                 ("exclusiveGrid", exclusiveGrid), ("commonGrid", commonGrid),
                 ("emptyLabel", emptyLabel), ("closeButton", closeButton),
                 ("detailPanel", detail.gameObject),
                 ("detailIcon", heroIcon), ("detailIconPlate", heroPlate),
                 ("detailKicker", kicker), ("detailName", detailName), ("detailWorn", detailWorn),
                 ("statNow", statNow), ("statNext", statNext),
                 ("skillBlock", skillBlock.gameObject), ("skillHeading", skillHeading),
                 ("skillNow", skillNow), ("skillNext", skillNext),
                 ("enhanceButton", enhanceButton), ("enhanceLabel", enhanceLabel),
                 ("enhanceSub", enhanceSub),
                 ("sideButton", sideButton), ("sideLabel", sideLabel), ("sideSub", sideSub),
                 ("backButton", backButton),
                 ("cellPrefab", prefabs.GearCell), ("sprites", library),
                 ("gearAtkIcon", GeneratedArt.GearIcon("atk")),
                 ("gearHeatIcon", GeneratedArt.GearIcon("heat")),
                 ("gearSpIcon", GeneratedArt.GearIcon("sp")));

            root.gameObject.SetActive(false);
            return screen;
        }

        /// <summary>
        /// One 전용장비 교환 row: a 40px icon on a grade-tinted plate, then the ownership
        /// line, the name, and what the next star would give. 67 tall, as the build's
        /// 8px padding around three stacked lines works out to.
        /// </summary>
        static GearExchangeRow BuildGearExchangeRowPrefab(Fonts fonts)
        {
            var go = UI("GearExchangeRow", null, out var rt);
            rt.sizeDelta = new Vector2(UiTheme.Px(326f), UiTheme.Py(67f));

            var plate = go.AddComponent<Image>();
            plate.sprite = GeneratedArt.RoundedNineSlice("ui_slot", Mathf.RoundToInt(UiTheme.Px(10f)));
            plate.type = Image.Type.Sliced;
            plate.color = UiTheme.Panel;
            plate.raycastTarget = true;

            var lineRt = UiKit.Node("Outline", rt);
            Stretch(lineRt);
            var outline = lineRt.gameObject.AddComponent<Image>();
            outline.sprite = GeneratedArt.RoundedOutlineNineSlice("ui_slot_line",
                Mathf.RoundToInt(UiTheme.Px(10f)), Mathf.Max(1, Mathf.RoundToInt(UiTheme.Px(1.5f))));
            outline.type = Image.Type.Sliced;
            outline.color = UiTheme.PanelLine;
            outline.raycastTarget = false;

            var button = go.AddComponent<Button>();
            button.targetGraphic = plate;
            button.transition = Selectable.Transition.None;
            var group = go.AddComponent<CanvasGroup>();

            var iconPlateRt = UiKit.Node("IconPlate", rt);
            Anchor(iconPlateRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                   new Vector2(UiTheme.Px(10f), -UiTheme.Py(21f)),
                   new Vector2(UiTheme.Px(50f), UiTheme.Py(21f)));
            var iconPlate = iconPlateRt.gameObject.AddComponent<Image>();
            iconPlate.sprite = GeneratedArt.RoundedNineSlice("ui_fact", Mathf.RoundToInt(UiTheme.Px(8f)));
            iconPlate.type = Image.Type.Sliced;
            iconPlate.color = UiTheme.Panel2;
            iconPlate.raycastTarget = false;

            var iconRt = UiKit.Node("Icon", iconPlateRt);
            Stretch(iconRt, UiTheme.Px(3f));
            var icon = iconRt.gameObject.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            float textLeft = UiTheme.Px(60f), textRight = -UiTheme.Px(10f);

            var ownership = UiKit.Label("Ownership", rt, fonts.Body, UiTheme.Py(11f), UiTheme.Ink2,
                                        TextAlignmentOptions.TopLeft, out var oRt, "");
            ownership.fontStyle = FontStyles.Bold;
            ownership.textWrappingMode = TextWrappingModes.NoWrap;
            ownership.overflowMode = TextOverflowModes.Ellipsis;
            Anchor(oRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(textLeft, -UiTheme.Py(24f)), new Vector2(textRight, -UiTheme.Py(7f)));

            var gearName = UiKit.Label("Name", rt, fonts.Title, UiTheme.Py(14f), UiTheme.Ink,
                                       TextAlignmentOptions.TopLeft, out var nRt, "");
            gearName.textWrappingMode = TextWrappingModes.NoWrap;
            gearName.overflowMode = TextOverflowModes.Ellipsis;
            Anchor(nRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(textLeft, -UiTheme.Py(44f)), new Vector2(textRight, -UiTheme.Py(24f)));

            var effect = UiKit.Label("Effect", rt, fonts.Body, UiTheme.Py(11.5f), UiTheme.Ink2,
                                     TextAlignmentOptions.TopLeft, out var eRt, "");
            effect.textWrappingMode = TextWrappingModes.NoWrap;
            effect.overflowMode = TextOverflowModes.Ellipsis;
            Anchor(eRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(textLeft, -UiTheme.Py(62f)), new Vector2(textRight, -UiTheme.Py(44f)));

            var row = go.AddComponent<GearExchangeRow>();
            Wire(row, ("plate", plate), ("iconPlate", iconPlate), ("icon", icon),
                 ("ownership", ownership), ("gearName", gearName), ("effect", effect),
                 ("button", button), ("group", group));
            return SavePrefab<GearExchangeRow>(go, "GearExchangeRow");
        }

        /// <summary>One bag cell: a tinted plate, the type icon, and a caption under it.</summary>
        static GearCell BuildGearCellPrefab(Fonts fonts)
        {
            var go = UI("GearCell", null, out var rt);
            rt.sizeDelta = new Vector2(UiTheme.Px(60.4f), UiTheme.Py(74f));

            var plateRt = UiKit.Node("Plate", rt);
            Anchor(plateRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                   new Vector2(-UiTheme.Px(24f), -UiTheme.Py(50f)),
                   new Vector2(UiTheme.Px(24f), -UiTheme.Py(2f)));
            var plate = plateRt.gameObject.AddComponent<Image>();
            plate.sprite = GeneratedArt.RoundedNineSlice("ui_fact", Mathf.RoundToInt(UiTheme.Px(8f)));
            plate.type = Image.Type.Sliced;
            plate.color = UiTheme.Panel2;
            plate.raycastTarget = true;

            var button = go.AddComponent<Button>();
            button.targetGraphic = plate;
            button.transition = Selectable.Transition.None;

            var iconRt = UiKit.Node("Icon", plateRt);
            Stretch(iconRt, UiTheme.Px(6f));
            var icon = iconRt.gameObject.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            var caption = UiKit.Label("Caption", rt, fonts.Body, UiTheme.Py(10.5f), UiTheme.Ink2,
                                      TextAlignmentOptions.Top, out var cRt, "");
            caption.fontStyle = FontStyles.Bold;
            caption.textWrappingMode = TextWrappingModes.NoWrap;
            caption.overflowMode = TextOverflowModes.Ellipsis;
            Anchor(cRt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   Vector2.zero, new Vector2(0f, UiTheme.Py(22f)));

            var cell = go.AddComponent<GearCell>();
            Wire(cell, ("plate", plate), ("icon", icon), ("caption", caption), ("button", button),
                 ("gearAtkIcon", GeneratedArt.GearIcon("atk")),
                 ("gearHeatIcon", GeneratedArt.GearIcon("heat")),
                 ("gearSpIcon", GeneratedArt.GearIcon("sp")));
            return SavePrefab<GearCell>(go, "GearCell");
        }

        static UnitDetailScreen BuildUnitDetail(RectTransform parent, Fonts fonts,
                                                SpriteLibrary library)
        {
            var root = UiKit.Node("UnitDetail", parent);
            Stretch(root);
            var bg = root.gameObject.AddComponent<Image>();
            bg.color = UiTheme.PageBg;

            float pad = UiTheme.ScreenPad;
            float topBand = UiTheme.Touch + UiTheme.S12;

            // ---- stage band behind the portrait.
            // The mockup is drawn on an 844pt phone; this canvas is 693pt tall, so copying
            // its absolute tops would leave the sheet below with no room at all. The split is
            // a fraction instead, and everything inside each half is placed within that half.
            const float SheetShare = 0.52f;
            var band = UiKit.Surface("Band", root, new Color(0.949f, 0.886f, 0.796f), 0f, out var bandRt);
            bandRt.gameObject.GetComponent<Image>().sprite = null;
            Anchor(bandRt, new Vector2(0f, SheetShare), Vector2.one, Vector2.zero, Vector2.zero);

            var back = UiKit.Button("Back", bandRt, fonts.Body, "", UiButtonKind.Secondary,
                                    out var backRt, UiTheme.Touch, UiTheme.LabelSize);
            Anchor(backRt, new Vector2(0f, 1f), new Vector2(0f, 1f),
                   new Vector2(pad, -UiTheme.S12 - UiTheme.Touch), new Vector2(pad + UiTheme.Touch, -UiTheme.S12));
            var backIconRt = UiKit.Node("Icon", backRt);
            Stretch(backIconRt, UiTheme.Px(10f));
            var backIcon = backIconRt.gameObject.AddComponent<Image>();
            backIcon.sprite = UiArt.Icon("arrow_back");
            backIcon.color = UiTheme.Ink;
            backIcon.preserveAspect = true;
            backIcon.raycastTarget = false;

            // Centred in the band, clear of the name row beneath it.
            var portraitRt = UiKit.Node("Portrait", bandRt);
            Anchor(portraitRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                   new Vector2(-UiTheme.Px(110f), -UiTheme.Py(100f)),
                   new Vector2(UiTheme.Px(110f), UiTheme.Py(150f)));
            var portrait = portraitRt.gameObject.AddComponent<Image>();
            portrait.preserveAspect = true;
            portrait.raycastTarget = false;

            UiButton Arrow(string id, string icon, bool left)
            {
                var b = UiKit.Button(id, bandRt, fonts.Body, "", UiButtonKind.Secondary,
                                     out var arrowRt, UiTheme.Touch, UiTheme.LabelSize);
                Anchor(arrowRt, new Vector2(left ? 0f : 1f, 0.5f), new Vector2(left ? 0f : 1f, 0.5f),
                       new Vector2(left ? pad : -pad - UiTheme.Touch, -UiTheme.Touch * 0.5f),
                       new Vector2(left ? pad + UiTheme.Touch : -pad, UiTheme.Touch * 0.5f));
                var iconRt = UiKit.Node("Icon", arrowRt);
                Stretch(iconRt, UiTheme.Px(10f));
                var image = iconRt.gameObject.AddComponent<Image>();
                image.sprite = UiArt.Icon(icon);
                image.color = UiTheme.Ink;
                image.preserveAspect = true;
                image.raycastTarget = false;
                return b;
            }
            var prev = Arrow("Prev", "chevron_left", true);
            var next = Arrow("Next", "chevron_right", false);

            // ---- name row, pinned to the bottom of the band
            // 별 칸. 글자는 Px(15) 인데 높이는 Py(26) 이라 54 < 57 로 모자랐다.
            float chipH = UiTheme.Row(UiTheme.Px(15f), UiTheme.Py(26f));
            float nameLine = UiTheme.Line(UiTheme.Px(26f));
            float rowBottom = UiTheme.Py(12f);
            float nameBottom = rowBottom + chipH + UiTheme.S6;

            var gradeRt = UiKit.Node("Grade", bandRt);
            Anchor(gradeRt, Vector2.zero, Vector2.zero,
                   new Vector2(pad, nameBottom + (nameLine - UiTheme.Px(22f)) * 0.5f),
                   new Vector2(pad + UiTheme.Px(34f), nameBottom + (nameLine + UiTheme.Px(22f)) * 0.5f));
            var gradeBadge = gradeRt.gameObject.AddComponent<Image>();
            gradeBadge.sprite = UiArt.Collection("grade_sr");
            gradeBadge.preserveAspect = true;
            gradeBadge.raycastTarget = false;

            var nameLabel = UiKit.Label("Name", bandRt, fonts.Title, UiTheme.Px(26f), UiTheme.Ink,
                                        TextAlignmentOptions.Left, out var nameRt, "");
            Anchor(nameRt, Vector2.zero, new Vector2(1f, 0f),
                   new Vector2(pad + UiTheme.Px(42f), nameBottom),
                   new Vector2(-pad, nameBottom + nameLine));

            var roleChip = UiKit.Chip("Role", bandRt, fonts.Body, "", out var roleRt,
                                      UiTheme.Panel, UiTheme.Ink, chipH, UiTheme.Caption);
            Anchor(roleRt, Vector2.zero, Vector2.zero,
                   new Vector2(pad, rowBottom), new Vector2(pad + UiTheme.Px(110f), rowBottom + chipH));
            var tileChip = UiKit.Chip("Tile", bandRt, fonts.Body, "", out var tileRt,
                                      UiTheme.Panel, UiTheme.Ink, chipH, UiTheme.Caption);
            Anchor(tileRt, Vector2.zero, Vector2.zero,
                   new Vector2(pad + UiTheme.Px(116f), rowBottom),
                   new Vector2(pad + UiTheme.Px(190f), rowBottom + chipH));
            var starLabel = UiKit.Label("Stars", bandRt, fonts.Body, UiTheme.Px(15f),
                                        UiTheme.RaritySSR, TextAlignmentOptions.Right, out var starRt, "");
            starLabel.richText = true;
            Anchor(starRt, new Vector2(0.5f, 0f), new Vector2(1f, 0f),
                   new Vector2(0f, rowBottom), new Vector2(-pad, rowBottom + chipH));

            // ---- sheet
            var sheet = UiKit.Panel("Sheet", root, out var sheetRt, UiTheme.Panel, UiTheme.Px(26f));
            Anchor(sheetRt, Vector2.zero, new Vector2(1f, SheetShare), Vector2.zero, Vector2.zero);

            float sheetPad = UiTheme.Px(12f);
            // 능력치·스킬·이야기 탭. 55 < 57 이라 셋 다 글자가 안 그려졌다.
            // 실측 .seg { padding:3px; height 39px } · .seg button { height 33px }. The row's
            // own padding comes off the top and bottom before the label sees any of it, so
            // the band has to be the button's line box PLUS that padding - sizing it to the
            // line alone left the three sub-tabs 2px short and TMP drew nothing.
            float segPad = UiTheme.Py(3f);
            float segH = UiTheme.Row(UiTheme.LabelSize, UiTheme.Py(33f)) + segPad * 2f;
            var segs = UiKit.Surface("SubTabs", sheetRt, UiTheme.Panel2, UiTheme.Px(14f), out var segRt);
            segs.raycastTarget = false;
            Anchor(segRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(sheetPad, -UiTheme.Py(14f) - segH - UiTheme.Py(8f)),
                   new Vector2(-sheetPad, -UiTheme.Py(14f)));
            var segRow = UiKit.Row(segRt, UiTheme.Px(4f), TextAnchor.MiddleCenter);
            segRow.padding = new RectOffset((int)segPad, (int)segPad, (int)segPad, (int)segPad);
            segRow.childForceExpandWidth = true;
            segRow.childForceExpandHeight = true;
            var statSeg = BuildSegment("Stat", segRt, fonts, "\ub2a5\ub825\uce58");
            var skillSeg = BuildSegment("Skill", segRt, fonts, "\uc2a4\ud0ac");
            var storySeg = BuildSegment("Story", segRt, fonts, "\uc774\uc57c\uae30");

            // ---- level row, shared by every sub-tab
            float levelTop = UiTheme.Py(14f) + segH + UiTheme.Py(8f) + UiTheme.Py(12f);
            // Lv. 표시. 49 < 60 — 가장 많이 모자랐다.
            float levelH = UiTheme.Row(UiTheme.Px(16f), UiTheme.Py(24f));
            var levelLabel = UiKit.Label("Level", sheetRt, fonts.Number, UiTheme.Px(16f), UiTheme.Ink,
                                         TextAlignmentOptions.Left, out var lvRt, "Lv.1");
            Anchor(lvRt, new Vector2(0f, 1f), new Vector2(0f, 1f),
                   new Vector2(sheetPad, -levelTop - levelH), new Vector2(sheetPad + UiTheme.Px(72f), -levelTop));
            var levelCapLabel = UiKit.Label("Cap", sheetRt, fonts.Body, UiTheme.Caption, UiTheme.Ink3,
                                            TextAlignmentOptions.Left, out var capRt, "");
            Anchor(capRt, new Vector2(0f, 1f), new Vector2(0f, 1f),
                   new Vector2(sheetPad + UiTheme.Px(74f), -levelTop - levelH),
                   new Vector2(sheetPad + UiTheme.Px(130f), -levelTop));
            var levelBar = UiKit.Bar("LevelBar", sheetRt, UiTheme.Panel2, UiTheme.Success,
                                     UiTheme.Py(8f), out var levelFill);
            Anchor(levelBar, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(sheetPad + UiTheme.Px(134f), -levelTop - levelH * 0.5f - UiTheme.Py(4f)),
                   new Vector2(-sheetPad - UiTheme.Px(84f), -levelTop - levelH * 0.5f + UiTheme.Py(4f)));
            var levelCostLabel = UiKit.Label("Next", sheetRt, fonts.Body, UiTheme.Caption, UiTheme.Ink2,
                                             TextAlignmentOptions.Right, out var lcRt, "");
            Anchor(lcRt, new Vector2(1f, 1f), new Vector2(1f, 1f),
                   new Vector2(-sheetPad - UiTheme.Px(80f), -levelTop - levelH),
                   new Vector2(-sheetPad, -levelTop));

            float bodyTop = levelTop + levelH + UiTheme.Py(12f);
            float actionH = UiTheme.Py(64f);
            float bodyBottom = actionH + UiTheme.S12 * 2f + UiTheme.Py(52f);

            RectTransform Body(string id)
            {
                var rt = UiKit.Node(id, sheetRt);
                Anchor(rt, Vector2.zero, Vector2.one,
                       new Vector2(sheetPad, bodyBottom), new Vector2(-sheetPad, -bodyTop));
                return rt;
            }

            // 능력치. 원본 .stats: a two-column grid of 1fr columns, gap 6px/14px, each row
            // a flex line with the label left (muted 12.5px) and the value right (LED 14px),
            // closed by a 1px hairline. No tile, no background, no fixed cell height - the
            // beige rounded tiles here were a port invention, sized 170x50 of nothing.
            var statBody = Body("StatBody");
            var statGrid = statBody.gameObject.AddComponent<GridLayoutGroup>();
            statGrid.cellSize = new Vector2(
                // 화면 전체(1080) 가 아니라 **디자인 기둥 폭**에서 재야 한다 —
                // 1080 으로 재면 두 칸이 기둥을 넘어 사거리·재배치 값이 화면 밖으로 나간다.
                (UiTheme.ColumnWidth - UiTheme.ScreenPad * 2f - UiTheme.Px(14f)) * 0.5f,
                UiTheme.Line(UiTheme.Px(14f)) + UiTheme.Py(5f) + UiTheme.HairLine);
            statGrid.spacing = new Vector2(UiTheme.Px(14f), UiTheme.Py(6f));
            statGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            statGrid.constraintCount = 2;

            TextMeshProUGUI StatCell(string id, string caption)
            {
                var cellRt = UiKit.Node("Cell" + id, statBody);
                // 실측 .stats div { border-bottom:1px solid var(--line); padding-bottom:5px }
                var rule = UiKit.Surface("Rule", cellRt, UiTheme.PanelLine, 0f, out var ruleRt);
                rule.raycastTarget = false;
                Anchor(ruleRt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                       Vector2.zero, new Vector2(0f, UiTheme.HairLine));

                float line = UiTheme.Line(UiTheme.Px(14f));
                float top = UiTheme.Py(5f) + UiTheme.HairLine;
                // 실측 .stats dt { color:var(--muted); font-size:12.5px } — 왼쪽
                UiKit.Label("Caption", cellRt, fonts.Body, UiTheme.Px(12.5f), UiTheme.Ink2,
                            TextAlignmentOptions.Left, out var capRt, caption);
                Anchor(capRt, new Vector2(0f, 0f), new Vector2(0.5f, 0f),
                       new Vector2(0f, top), new Vector2(0f, top + line));
                // 실측 .stats dd { font-family:var(--f-led); font-size:14px } — 오른쪽
                var value = UiKit.Label("Value", cellRt, fonts.Number, UiTheme.Px(14f), UiTheme.Ink,
                                        TextAlignmentOptions.Right, out var valRt, "");
                Anchor(valRt, new Vector2(0.5f, 0f), new Vector2(1f, 0f),
                       new Vector2(UiTheme.Px(8f), top), new Vector2(0f, top + line));
                return value;
            }
            var powerValue = StatCell("Power", "\uccad\uc18c\ub825");
            var rangeValue = StatCell("Range", "\uc0ac\uac70\ub9ac");
            var costValue = StatCell("Cost", "\uc18c\ube44 \uc804\ub825");
            var coolValue = StatCell("Cool", "\uc7ac\ubc30\uce58 \ub300\uae30");

            // 10.17-b. A grid cell was the obvious place, but 4 cells fill two rows exactly
            // and a fifth starts a third that runs off the bottom of the sheet. One
            // full-width line under the grid instead - a child of the body but outside the
            // grid, so the GridLayoutGroup does not claim it, and anchored under the two
            // rows rather than to the sheet's floor: pinned to the floor it sat half a
            // screen away from the stats it belongs to.
            float statRowsH = statGrid.cellSize.y * 2f + statGrid.spacing.y;
            var supportRow = UiKit.Label("SupportRow", statBody, fonts.Body, UiTheme.Caption,
                                         UiTheme.Ink2, TextAlignmentOptions.Left,
                                         out var supRt, string.Empty);
            supportRow.textWrappingMode = TextWrappingModes.NoWrap;
            var supIgnore = supRt.gameObject.AddComponent<LayoutElement>();
            supIgnore.ignoreLayout = true;
            Anchor(supRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(0f, -statRowsH - UiTheme.Py(6f) - UiTheme.Line(UiTheme.Caption)),
                   new Vector2(0f, -statRowsH - UiTheme.Py(6f)));
            supportRow.gameObject.SetActive(false);

            // 스킬
            var skillBody = Body("SkillBody");
            var skillIconRt = UiKit.Node("Icon", skillBody);
            Anchor(skillIconRt, new Vector2(0f, 1f), new Vector2(0f, 1f),
                   new Vector2(0f, -UiTheme.Sq(44f)), new Vector2(UiTheme.Sq(44f), 0f));
            var skillIconBg = skillIconRt.gameObject.AddComponent<Image>();
            skillIconBg.sprite = UiArt.Panel;
            skillIconBg.type = Image.Type.Sliced;
            skillIconBg.color = UiTheme.Navy;
            skillIconBg.raycastTarget = false;
            var skillGlyphRt = UiKit.Node("Glyph", skillIconRt);
            Stretch(skillGlyphRt, UiTheme.Px(10f));
            var skillIcon = skillGlyphRt.gameObject.AddComponent<Image>();
            skillIcon.sprite = UiArt.Icon("cyclone");
            skillIcon.color = Color.white;
            skillIcon.preserveAspect = true;
            skillIcon.raycastTarget = false;

            var skillName = UiKit.Label("Name", skillBody, fonts.Body, UiTheme.LabelSize, UiTheme.Ink,
                                        TextAlignmentOptions.Left, out var skillNameRt, "");
            Anchor(skillNameRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(54f), -UiTheme.Line(UiTheme.LabelSize)), Vector2.zero);
            var skillDesc = UiKit.Label("Desc", skillBody, fonts.Body, UiTheme.Caption, UiTheme.Ink2,
                                        TextAlignmentOptions.TopLeft, out var skillDescRt, "");
            skillDesc.textWrappingMode = TextWrappingModes.Normal;
            Anchor(skillDescRt, new Vector2(0f, 0f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(54f), 0f),
                   new Vector2(0f, -UiTheme.Line(UiTheme.LabelSize) - UiTheme.S4));

            // 이야기
            var storyBody = Body("StoryBody");
            var storyLabel = UiKit.Label("Text", storyBody, fonts.Body, UiTheme.Caption, UiTheme.Ink2,
                                         TextAlignmentOptions.TopLeft, out var storyRt, "");
            storyLabel.textWrappingMode = TextWrappingModes.Normal;
            Stretch(storyRt);

            // ---- limit break box
            var starRoot = UiKit.Surface("StarBox", sheetRt, UiTheme.Panel2, UiTheme.Px(14f), out var starBoxRt);
            starRoot.raycastTarget = false;
            Anchor(starBoxRt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(sheetPad, actionH + UiTheme.S12 * 2f),
                   new Vector2(-sheetPad, actionH + UiTheme.S12 * 2f + UiTheme.Py(44f)));
            var starNeed = UiKit.Label("Need", starBoxRt, fonts.Body, UiTheme.Caption, UiTheme.Ink2,
                                       TextAlignmentOptions.Left, out var starNeedRt, "");
            Anchor(starNeedRt, new Vector2(0f, 0f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(12f), 0f), new Vector2(-UiTheme.Px(96f), 0f));
            // UiKit.Button sizes its label padding from the height it is given, so the
            // anchor below has to use that same number. This was built at Px(32)=80 and
            // then anchored to 2xPy(16)=66, leaving the label 31 tall against a 45 line
            // box - and TMP draws nothing at all when the box is short. Only shows on a
            // profile that still has a limit-break left, which is why a fixed starting
            // state found it and an accumulated save did not.
            float starBtnH = UiTheme.Row(UiTheme.Caption, UiTheme.Py(32f));
            var starButton = UiKit.Button("StarUp", starBoxRt, fonts.Body, "\ub3cc\ud30c",
                                          UiButtonKind.Success, out var starBtnRt,
                                          starBtnH, UiTheme.Caption);
            Anchor(starBtnRt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                   new Vector2(-UiTheme.Px(8f) - UiTheme.Px(80f), -starBtnH * 0.5f),
                   new Vector2(-UiTheme.Px(8f), starBtnH * 0.5f));

            // ---- actions
            float toggleW = UiTheme.Px(118f);
            var deckToggle = BuildChip("DeckToggle", sheetRt, fonts, UiTheme.Px(18f),
                                       UiTheme.Panel2, UiTheme.Success, UiTheme.Disabled,
                                       UiTheme.Ink, UiTheme.Panel, UiTheme.Ink3, false);
            var deckToggleRt = (RectTransform)deckToggle.transform;
            Anchor(deckToggleRt, Vector2.zero, new Vector2(0f, 0f),
                   new Vector2(sheetPad, UiTheme.S12),
                   new Vector2(sheetPad + toggleW, UiTheme.S12 + actionH));

            var upgrade = UiKit.Button("Upgrade", sheetRt, fonts.Title, "\uac15\ud654",
                                       UiButtonKind.Primary, out var upgradeRt, actionH, UiTheme.Px(22f));
            Anchor(upgradeRt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(sheetPad + toggleW + UiTheme.S8, UiTheme.S12),
                   new Vector2(-sheetPad, UiTheme.S12 + actionH));
            upgrade.SetLabelInset(UiTheme.Px(190f));

            (TextMeshProUGUI label, Image face) CostChip(string id, float right)
            {
                var chipFace = UiKit.Surface(id, upgradeRt, UiTheme.Panel, UiTheme.Px(9f), out var chipRt);
                chipFace.raycastTarget = false;
                // 칩 안의 글자는 S4 만큼 안쪽에 들어간다. 그 여백까지 더해서 높이를
                // 잡지 않으면 칩은 보이는데 숫자만 사라진다 — 38 인데 45 가 필요했다.
                float chipHalf = Mathf.Max(UiTheme.Py(14f),
                                           UiTheme.Line(UiTheme.Caption) * 0.5f + UiTheme.S4);
                Anchor(chipRt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                       new Vector2(-right - UiTheme.Px(84f), -chipHalf),
                       new Vector2(-right, chipHalf));
                var label = UiKit.Label("Text", chipRt, fonts.Number, UiTheme.Caption, UiTheme.Ink,
                                        TextAlignmentOptions.Center, out var labelRt, "");
                Stretch(labelRt, UiTheme.S4);
                return (label, chipFace);
            }
            var partChip = CostChip("PartCost", UiTheme.Px(96f));
            var coinChip = CostChip("CoinCost", UiTheme.Px(8f));

            var screen = root.gameObject.AddComponent<UnitDetailScreen>();
            Wire(screen,
                 ("portrait", portrait), ("gradeBadge", gradeBadge), ("nameLabel", nameLabel),
                 ("roleChip", roleChip), ("tileChip", tileChip), ("starLabel", starLabel),
                 ("backButton", back), ("prevButton", prev), ("nextButton", next),
                 ("sprites", library),
                 ("statSubTab", statSeg), ("skillSubTab", skillSeg), ("storySubTab", storySeg),
                 ("statBody", statBody.gameObject), ("skillBody", skillBody.gameObject),
                 ("storyBody", storyBody.gameObject),
                 ("levelLabel", levelLabel), ("levelCapLabel", levelCapLabel),
                 ("levelFill", levelFill), ("levelCostLabel", levelCostLabel),
                 ("powerValue", powerValue), ("rangeValue", rangeValue),
                 ("costValue", costValue), ("coolValue", coolValue),
                 ("supportRow", supportRow),
                 ("skillNameLabel", skillName), ("skillDescLabel", skillDesc), ("skillIcon", skillIcon),
                 ("storyLabel", storyLabel),
                 ("starRoot", starBoxRt.gameObject), ("starNeedLabel", starNeed),
                 ("starButton", starButton),
                 ("deckToggle", deckToggle), ("upgradeButton", upgrade),
                 ("partCostLabel", partChip.label), ("coinCostLabel", coinChip.label),
                 ("partCostFace", partChip.face), ("coinCostFace", coinChip.face));
            root.gameObject.SetActive(false);
            return screen;
        }

        // ------------------------------------------------------------------ 시즌 패스 (2f)

        static PassScreen BuildPassScreen(RectTransform parent, Fonts fonts, Prefabs prefabs)
        {
            var root = UiKit.Node("Pass", parent);
            Stretch(root);
            var bg = root.gameObject.AddComponent<Image>();
            bg.color = UiTheme.PageBg;

            float pad = UiTheme.ScreenPad;
            float gap = UiTheme.S12;

            var back = UiKit.Button("Back", root, fonts.Body, "", UiButtonKind.Secondary,
                                    out var backRt, UiTheme.Touch, UiTheme.LabelSize);
            Anchor(backRt, new Vector2(0f, 1f), new Vector2(0f, 1f),
                   new Vector2(pad, -gap - UiTheme.Touch), new Vector2(pad + UiTheme.Touch, -gap));
            var backIconRt = UiKit.Node("Icon", backRt);
            Stretch(backIconRt, UiTheme.Px(10f));
            var backIcon = backIconRt.gameObject.AddComponent<Image>();
            backIcon.sprite = UiArt.Icon("arrow_back");
            backIcon.color = UiTheme.Ink;
            backIcon.preserveAspect = true;
            backIcon.raycastTarget = false;

            UiKit.Label("Title", root, fonts.Title, UiTheme.Px(24f), UiTheme.Ink,
                        TextAlignmentOptions.Left, out var titleRt, "\uc2dc\uc98c \ud328\uc2a4");
            Anchor(titleRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(pad + UiTheme.Touch + UiTheme.S8, -gap - UiTheme.Touch),
                   new Vector2(-pad - UiTheme.Px(70f), -gap));
            var daysLabel = UiKit.Chip("Days", root, fonts.Number, "", out var daysRt,
                                       UiTheme.Panel2, UiTheme.Ink, UiTheme.Px(28f), UiTheme.Caption);
            Anchor(daysRt, new Vector2(1f, 1f), new Vector2(1f, 1f),
                   new Vector2(-pad - UiTheme.Px(64f), -gap - UiTheme.Py(36f)),
                   new Vector2(-pad, -gap - UiTheme.Py(8f)));

            // ---- level card
            float cardTop = gap + UiTheme.Touch + gap;
            float cardH = UiTheme.Py(84f);
            UiKit.Panel("LevelCard", root, out var cardRt, UiTheme.Panel, UiTheme.Px(20f));
            Anchor(cardRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(pad, -cardTop - cardH), new Vector2(-pad, -cardTop));

            float knob = UiTheme.Py(56f);
            var knobFace = UiKit.Surface("Knob", cardRt, UiTheme.Premium, knob * 0.5f, out var knobRt);
            knobFace.sprite = UiArt.Pill;
            knobFace.raycastTarget = false;
            Anchor(knobRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                   new Vector2(UiTheme.Px(14f), -knob * 0.5f),
                   new Vector2(UiTheme.Px(14f) + knob, knob * 0.5f));
            var levelLabel = UiKit.Label("Level", knobRt, fonts.Number, UiTheme.Px(20f),
                                         UiTheme.Panel, TextAlignmentOptions.Center, out var llRt, "1");
            Stretch(llRt);

            float bodyLeft = UiTheme.Px(14f) + knob + gap;
            var seasonLabel = UiKit.Label("Season", cardRt, fonts.Body, UiTheme.LabelSize, UiTheme.Ink,
                                          TextAlignmentOptions.Left, out var seasonRt, "");
            Anchor(seasonRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(bodyLeft, -UiTheme.Py(16f) - UiTheme.Line(UiTheme.LabelSize)),
                   new Vector2(-UiTheme.Px(14f), -UiTheme.Py(16f)));
            var xpBar = UiKit.Bar("XpBar", cardRt, UiTheme.Panel2, UiTheme.Premium,
                                  UiTheme.Py(8f), out var xpFill);
            Anchor(xpBar, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                   new Vector2(bodyLeft, -UiTheme.Py(4f)), new Vector2(-UiTheme.Px(14f), UiTheme.Py(4f)));
            var xpLabel = UiKit.Label("Xp", cardRt, fonts.Body, UiTheme.Caption, UiTheme.Ink2,
                                      TextAlignmentOptions.Left, out var xpRt, "");
            Anchor(xpRt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(bodyLeft, UiTheme.Py(14f)),
                   new Vector2(-UiTheme.Px(14f), UiTheme.Py(14f) + UiTheme.Line(UiTheme.Caption)));

            // ---- premium strip
            float premiumTop = cardTop + cardH + UiTheme.S8;
            float premiumH = UiTheme.Py(60f);
            var premium = UiKit.Surface("Premium", root, UiTheme.Premium, UiTheme.Px(18f), out var premRt);
            premium.raycastTarget = false;
            Anchor(premRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(pad, -premiumTop - premiumH), new Vector2(-pad, -premiumTop));
            // Two stacked lines: each gets its own line box rather than half the strip.
            // Splitting a Py(60) strip in half leaves 42 per line, and these need 50 and 45
            // - TMP then draws neither. Row() is what keeps a line from being squeezed.
            float pnLine = UiTheme.Row(UiTheme.LabelSize, 0f);
            float psLine = UiTheme.Row(UiTheme.Caption, 0f);
            UiKit.Label("Name", premRt, fonts.Body, UiTheme.LabelSize, Color.white,
                        TextAlignmentOptions.Left, out var pnRt, "\ud504\ub9ac\ubbf8\uc5c4 \ud328\uc2a4");
            Anchor(pnRt, new Vector2(0f, 0.5f), new Vector2(0.6f, 0.5f),
                   new Vector2(UiTheme.Px(16f), 0f), new Vector2(0f, pnLine));
            var premiumSub = UiKit.Label("Sub", premRt, fonts.Body, UiTheme.Caption,
                                         new Color(0.918f, 0.863f, 0.984f),
                                         TextAlignmentOptions.Left, out var psRt, "");
            Anchor(psRt, new Vector2(0f, 0.5f), new Vector2(0.6f, 0.5f),
                   new Vector2(UiTheme.Px(16f), -psLine), Vector2.zero);

            // Same build-height / anchor-height rule as the 돌파 button: UiKit.Button pads
            // its label from the height it is handed, so the anchor must reuse that number.
            float buyH = UiTheme.Row(UiTheme.LabelSize, UiTheme.Py(44f));
            var premiumButton = UiKit.Button("Buy", premRt, fonts.Body, "\uad6c\ub9e4",
                                             UiButtonKind.Secondary, out var pbRt,
                                             buyH, UiTheme.LabelSize);
            Anchor(pbRt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                   new Vector2(-UiTheme.Px(8f) - UiTheme.Px(110f), -buyH * 0.5f),
                   new Vector2(-UiTheme.Px(8f), buyH * 0.5f));

            // ---- track headers
            float headTop = premiumTop + premiumH + gap;
            UiKit.Label("FreeHead", root, fonts.Body, UiTheme.Caption, UiTheme.Ink2,
                        TextAlignmentOptions.Center, out var fhRt, "\ubb34\ub8cc");
            Anchor(fhRt, new Vector2(0f, 1f), new Vector2(0.5f, 1f),
                   new Vector2(pad, -headTop - UiTheme.Line(UiTheme.Caption)),
                   new Vector2(-UiTheme.Px(20f), -headTop));
            UiKit.Label("PaidHead", root, fonts.Body, UiTheme.Caption, UiTheme.Premium,
                        TextAlignmentOptions.Center, out var phRt, "\ud504\ub9ac\ubbf8\uc5c4");
            Anchor(phRt, new Vector2(0.5f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(20f), -headTop - UiTheme.Line(UiTheme.Caption)),
                   new Vector2(-pad, -headTop));

            // ---- list with the level pillar behind it
            float listTop = headTop + UiTheme.Line(UiTheme.Caption) + UiTheme.S6;
            float claimH = UiTheme.Py(60f);
            var pillarBg = UiKit.Surface("Pillar", root, UiTheme.Disabled, UiTheme.Px(2f), out var pillarRt);
            pillarBg.raycastTarget = false;
            Anchor(pillarRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
                   new Vector2(-UiTheme.Px(2f), claimH + gap * 2f), new Vector2(UiTheme.Px(2f), -listTop));
            var pillarFill = UiKit.Node("Fill", pillarRt);
            Anchor(pillarFill, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, Vector2.zero);
            pillarFill.pivot = new Vector2(0.5f, 0f);
            var pillarFillImage = pillarFill.gameObject.AddComponent<Image>();
            pillarFillImage.sprite = UiArt.Panel;
            pillarFillImage.type = Image.Type.Sliced;
            pillarFillImage.color = UiTheme.Premium;
            pillarFillImage.raycastTarget = false;

            var viewport = UiKit.Node("Viewport", root);
            Anchor(viewport, Vector2.zero, Vector2.one,
                   new Vector2(pad, claimH + gap * 2f), new Vector2(-pad, -listTop));
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            // 뷰포트에 그래픽이 없으면 빈 자리 드래그와 휠이 ScrollRect 까지 닿지 않는다.
            UiKit.ScrollCatcher(viewport);
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            var content = UiKit.Node("Content", viewport);
            Anchor(content, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            content.pivot = new Vector2(0.5f, 1f);
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = UiTheme.S6;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;

            var claimAll = UiKit.Button("ClaimAll", root, fonts.Title, "\ubaa8\ub450 \ubc1b\uae30",
                                        UiButtonKind.Success, out var claimRt, claimH, UiTheme.Px(19f));
            Anchor(claimRt, Vector2.zero, new Vector2(1f, 0f),
                   new Vector2(pad, gap), new Vector2(-pad, gap + claimH));
            var claimBadge = UiKit.Chip("Badge", claimRt, fonts.Number, "", out var cbRt,
                                        UiTheme.Panel, UiTheme.Success, UiTheme.Px(26f), UiTheme.Caption);
            Anchor(cbRt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                   new Vector2(-UiTheme.Px(12f) - UiTheme.Px(44f), -UiTheme.Py(13f)),
                   new Vector2(-UiTheme.Px(12f), UiTheme.Py(13f)));

            var screen = root.gameObject.AddComponent<PassScreen>();
            Wire(screen, ("backButton", back), ("daysLabel", daysLabel), ("levelLabel", levelLabel),
                 ("seasonLabel", seasonLabel), ("xpLabel", xpLabel), ("xpFill", xpFill),
                 ("premiumRoot", premRt.gameObject), ("premiumButton", premiumButton),
                 ("premiumSubLabel", premiumSub),
                 ("listRoot", content), ("rowPrefab", prefabs.PassRow), ("pillarFill", pillarFill),
                 ("claimAllButton", claimAll), ("claimAllBadge", claimBadge));
            root.gameObject.SetActive(false);
            return screen;
        }

        // ------------------------------------------------------------------ 출석부

        /// <summary>
        /// The seven-day attendance wheel as its own popup, so the player sees every square
        /// at once instead of only the one LobbyScreen's rail button would have claimed.
        /// </summary>
        /// <summary>
        /// 협동 파트너 고르기 — 원본 `coopEnter`.
        ///
        /// .co-pick 2열 · .co-card radius 14 padding 10/6 · .cf 52 · .ct b17 small11
        /// .cm 11.5 · .cb 11 · .co-desc radius 12 padding 9/11 p12.5 small11
        /// </summary>
        static CoopPickScreen BuildCoopPickScreen(RectTransform parent, Fonts fonts,
                                                  SpriteLibrary library)
        {
            var root = UiKit.Node("CoopPick", parent);
            Stretch(root);
            var bg = root.gameObject.AddComponent<Image>();
            bg.color = UiTheme.PageBg;

            float pad = UiTheme.ScreenPad;
            float gap = UiTheme.S12;

            var back = UiKit.Button("Back", root, fonts.Body, "", UiButtonKind.Secondary,
                                    out var backRt, UiTheme.Touch, UiTheme.LabelSize);
            Anchor(backRt, new Vector2(0f, 1f), new Vector2(0f, 1f),
                   new Vector2(pad, -gap - UiTheme.Touch), new Vector2(pad + UiTheme.Touch, -gap));
            var backIconRt = UiKit.Node("Icon", backRt);
            Stretch(backIconRt, UiTheme.Px(10f));
            var backIcon = backIconRt.gameObject.AddComponent<Image>();
            backIcon.sprite = UiArt.Icon("arrow_back");
            backIcon.color = UiTheme.Ink;
            backIcon.preserveAspect = true;
            backIcon.raycastTarget = false;

            UiKit.Label("Title", root, fonts.Title, UiTheme.Px(24f), UiTheme.Ink,
                        TextAlignmentOptions.Left, out var titleRt, "\ud611\ub3d9 \uccad\uc18c");
            Anchor(titleRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(pad + UiTheme.Touch + UiTheme.S8, -gap - UiTheme.Touch),
                   new Vector2(-pad, -gap));

            float top = gap + UiTheme.Touch + gap;

            // ---- 안내문
            float noteH = UiTheme.Line(UiTheme.Px(11.5f)) * 4f;
            var note = UiKit.Label("Note", root, fonts.Body, UiTheme.Px(11.5f), UiTheme.Ink2,
                                   TextAlignmentOptions.TopLeft, out var noteRt, "");
            note.textWrappingMode = TextWrappingModes.Normal;
            Anchor(noteRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(pad, -top - noteH), new Vector2(-pad, -top));

            // ---- .co-pick: 두 장짜리 카드 줄
            float cardTop = top + noteH + gap;
            // 카드 높이는 안에 쌓는 줄의 합이다. 눈대중으로 잡으면 마지막 줄이
            // 바깥으로 밀려 바닥 기준 줄과 포개진다 — 실제로 그렇게 겹쳤다.
            float faceTop = UiTheme.Py(10f), faceBox = UiTheme.Sq(52f);
            float nameLine = UiTheme.Line(UiTheme.Px(17f));
            float styleLine = UiTheme.Line(UiTheme.Px(11f));
            float metaLine = UiTheme.Line(UiTheme.Px(11.5f));
            float bestLine = UiTheme.Line(UiTheme.Px(11f));
            float cardH = faceTop + faceBox + UiTheme.Py(3f) + nameLine + styleLine
                          + UiTheme.Py(3f) + metaLine * 2f
                          + UiTheme.Py(3f) + bestLine + UiTheme.Py(10f);
            var pick = UiKit.Node("Pick", root);
            Anchor(pick, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(pad, -cardTop - cardH), new Vector2(-pad, -cardTop));

            var buttons = new UiButton[CoopPickScreen.Slots];
            var plates = new Image[CoopPickScreen.Slots];
            var faces = new Image[CoopPickScreen.Slots];
            var names = new TextMeshProUGUI[CoopPickScreen.Slots];
            var styles = new TextMeshProUGUI[CoopPickScreen.Slots];
            var earlies = new TextMeshProUGUI[CoopPickScreen.Slots];
            var lates = new TextMeshProUGUI[CoopPickScreen.Slots];
            var bests = new TextMeshProUGUI[CoopPickScreen.Slots];

            float faceSize = UiTheme.Sq(52f);
            for (int i = 0; i < CoopPickScreen.Slots; i++)
            {
                var card = UiKit.Button($"Card{i}", pick, fonts.Body, "", UiButtonKind.Secondary,
                                        out var cardRt, cardH, UiTheme.Px(12f));
                // 레이아웃 그룹을 쓰지 않는다. 화면이 꺼진 채 지어지므로 그룹이 돌 기회가
                // 없고, 자식이 기본 100px 로 남아 글자가 전부 잘린다.
                float half = 0.5f, cgap = UiTheme.Px(4f);
                Anchor(cardRt, new Vector2(i * half, 0f), new Vector2((i + 1) * half, 1f),
                       new Vector2(i == 0 ? 0f : cgap, 0f),
                       new Vector2(i == 0 ? -cgap : 0f, 0f));
                buttons[i] = card;
                plates[i] = cardRt.GetComponent<Image>();

                var faceBg = UiKit.Surface("Face", cardRt, UiTheme.Panel, UiTheme.Px(12f), out var faceRt);
                Anchor(faceRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                       new Vector2(-faceBox * 0.5f, -faceTop - faceBox),
                       new Vector2(faceBox * 0.5f, -faceTop));
                var iconRt = UiKit.Node("Icon", faceRt);
                Stretch(iconRt, UiTheme.Px(4f));
                faces[i] = iconRt.gameObject.AddComponent<Image>();
                faces[i].preserveAspect = true;
                faces[i].raycastTarget = false;

                float y = faceTop + faceBox + UiTheme.Py(3f);
                names[i] = UiKit.Label("Name", cardRt, fonts.Title, UiTheme.Px(17f), UiTheme.Ink,
                                       TextAlignmentOptions.Center, out var nmRt, "");
                Anchor(nmRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(0f, -y - nameLine), new Vector2(0f, -y));

                y += nameLine;
                styles[i] = UiKit.Label("Style", cardRt, fonts.Body, UiTheme.Px(11f), UiTheme.Navy,
                                        TextAlignmentOptions.Center, out var stRt, "");
                styles[i].fontStyle = FontStyles.Bold;
                Anchor(stRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(0f, -y - styleLine), new Vector2(0f, -y));

                y += styleLine + UiTheme.Py(3f);
                earlies[i] = UiKit.Label("Early", cardRt, fonts.Body, UiTheme.Px(11.5f), UiTheme.Ink2,
                                         TextAlignmentOptions.Center, out var erRt, "");
                Anchor(erRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(0f, -y - metaLine), new Vector2(0f, -y));

                y += metaLine;
                lates[i] = UiKit.Label("Late", cardRt, fonts.Body, UiTheme.Px(11.5f), UiTheme.Ink2,
                                       TextAlignmentOptions.Center, out var laRt, "");
                Anchor(laRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(0f, -y - metaLine), new Vector2(0f, -y));

                // 최고 기록도 위에서 이어 쌓는다. 바닥 기준으로 두면 카드 높이가
                // 조금만 어긋나도 바로 윗줄과 겹친다.
                y += metaLine + UiTheme.Py(3f);
                bests[i] = UiKit.Label("Best", cardRt, fonts.Body, UiTheme.Px(11f), UiTheme.Ink2,
                                       TextAlignmentOptions.Center, out var beRt, "");
                Anchor(beRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(0f, -y - bestLine), new Vector2(0f, -y));
            }

            // ---- .co-desc
            float goH = UiTheme.Py(52f);
            float rewardLine = UiTheme.Line(UiTheme.Px(12f));
            float descTop = cardTop + cardH + gap;
            // 설명 칸은 내용 높이에 맞춘다. 화면 끝까지 늘이면 아래가 텅 빈다 —
            // 원본 .co-desc 도 내용만큼만 차지한다.
            float descH = UiTheme.Py(9f) * 2f + UiTheme.Line(UiTheme.Px(12.5f)) * 3f
                          + UiTheme.Py(6f) + UiTheme.Sq(32f)
                          + UiTheme.Py(6f) + UiTheme.Line(UiTheme.Px(11f)) * 2f;
            var desc = UiKit.Surface("Desc", root, UiTheme.Panel, UiTheme.Px(12f), out var descRt);
            Anchor(descRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(pad, -descTop - descH), new Vector2(-pad, -descTop));

            float dpad = UiTheme.Px(11f);
            float descLine = UiTheme.Line(UiTheme.Px(12.5f));
            var descText = UiKit.Label("Text", descRt, fonts.Body, UiTheme.Px(12.5f), UiTheme.Ink,
                                       TextAlignmentOptions.TopLeft, out var dtRt, "");
            descText.textWrappingMode = TextWrappingModes.Normal;
            Anchor(dtRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(dpad, -UiTheme.Py(9f) - descLine * 3f), new Vector2(-dpad, -UiTheme.Py(9f)));

            float miniSize = UiTheme.Sq(32f);
            var minis = UiKit.Node("Deck", descRt);
            Anchor(minis, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(dpad, -UiTheme.Py(9f) - descLine * 3f - UiTheme.Py(6f) - miniSize),
                   new Vector2(-dpad, -UiTheme.Py(9f) - descLine * 3f - UiTheme.Py(6f)));
            var miniRow = UiKit.Row(minis, UiTheme.Px(5f), TextAnchor.MiddleLeft);
            miniRow.childForceExpandWidth = false;
            miniRow.childControlWidth = false;

            var deckIcons = new Image[6];
            for (int i = 0; i < deckIcons.Length; i++)
            {
                var cell = UiKit.Surface($"Mini{i}", minis, Color.white, UiTheme.Px(8f), out var cellRt);
                var le = cellRt.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = miniSize;
                le.preferredHeight = miniSize;
                var icRt = UiKit.Node("Icon", cellRt);
                Stretch(icRt, UiTheme.Px(3f));
                deckIcons[i] = icRt.gameObject.AddComponent<Image>();
                deckIcons[i].preserveAspect = true;
                deckIcons[i].raycastTarget = false;
            }

            float noteLine = UiTheme.Line(UiTheme.Px(11f));
            var deckNote = UiKit.Label("DeckNote", descRt, fonts.Body, UiTheme.Px(11f), UiTheme.Ink2,
                                       TextAlignmentOptions.TopLeft, out var dnRt, "");
            deckNote.textWrappingMode = TextWrappingModes.Normal;
            Anchor(dnRt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(dpad, UiTheme.Py(9f)),
                   new Vector2(-dpad, UiTheme.Py(9f) + noteLine * 2f));

            // ---- 보상 줄 (.sv-rw)
            var reward = UiKit.Label("Reward", root, fonts.Body, UiTheme.Px(12f), UiTheme.Ink2,
                                     TextAlignmentOptions.Left, out var rwRt, "");
            reward.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(rwRt, new Vector2(0f, 0f), new Vector2(0.62f, 0f),
                   new Vector2(pad, goH + gap), new Vector2(0f, goH + gap + rewardLine));

            var pool = UiKit.Label("Pool", root, fonts.Body, UiTheme.Px(12f), UiTheme.Ink2,
                                   TextAlignmentOptions.Right, out var plRt, "");
            pool.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(plRt, new Vector2(0.62f, 0f), new Vector2(1f, 0f),
                   new Vector2(0f, goH + gap), new Vector2(-pad, goH + gap + rewardLine));

            // ---- 출동
            var go = UiKit.Button("Go", root, fonts.Title, "", UiButtonKind.Primary,
                                  out var goRt, goH, UiTheme.Px(18f));
            Anchor(goRt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(pad, gap), new Vector2(-pad, gap + goH));
            var goLabel = goRt.GetComponentInChildren<TextMeshProUGUI>();

            var screen = root.gameObject.AddComponent<CoopPickScreen>();
            Wire(screen, ("backButton", back), ("goButton", go), ("goLabel", goLabel),
                 ("noteLabel", note), ("descLabel", descText), ("deckRow", minis),
                 ("deckNoteLabel", deckNote), ("rewardLabel", reward), ("poolLabel", pool),
                 ("sprites", library));
            WireArray(screen, "cardButtons", buttons);
            WireArray(screen, "cardPlates", plates);
            WireArray(screen, "cardFaces", faces);
            WireArray(screen, "cardNames", names);
            WireArray(screen, "cardStyles", styles);
            WireArray(screen, "cardEarly", earlies);
            WireArray(screen, "cardLate", lates);
            WireArray(screen, "cardBest", bests);
            WireArray(screen, "deckIcons", deckIcons);

            root.gameObject.SetActive(false);
            return screen;
        }

        static AttendanceScreen BuildAttendanceScreen(RectTransform parent, Fonts fonts)
        {
            var root = UiKit.Node("Attendance", parent);
            Stretch(root);
            var bg = root.gameObject.AddComponent<Image>();
            bg.color = UiTheme.PageBg;

            float pad = UiTheme.ScreenPad;
            float gap = UiTheme.S12;

            var back = UiKit.Button("Back", root, fonts.Body, "", UiButtonKind.Secondary,
                                    out var backRt, UiTheme.Touch, UiTheme.LabelSize);
            Anchor(backRt, new Vector2(0f, 1f), new Vector2(0f, 1f),
                   new Vector2(pad, -gap - UiTheme.Touch), new Vector2(pad + UiTheme.Touch, -gap));
            var backIconRt = UiKit.Node("Icon", backRt);
            Stretch(backIconRt, UiTheme.Px(10f));
            var backIcon = backIconRt.gameObject.AddComponent<Image>();
            backIcon.sprite = UiArt.Icon("arrow_back");
            backIcon.color = UiTheme.Ink;
            backIcon.preserveAspect = true;
            backIcon.raycastTarget = false;

            UiKit.Label("Title", root, fonts.Title, UiTheme.Px(24f), UiTheme.Ink,
                        TextAlignmentOptions.Left, out var titleRt, "출석부");
            Anchor(titleRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(pad + UiTheme.Touch + UiTheme.S8, -gap - UiTheme.Touch),
                   new Vector2(-pad, -gap));

            // ---- the seven squares, laid out 4-then-3 so a glance reads as two short rows
            // rather than one cramped strip.
            float headerBottom = gap + UiTheme.Touch + gap;
            float claimH = UiTheme.Py(60f);
            var gridRt = UiKit.Node("Grid", root);
            Anchor(gridRt, Vector2.zero, Vector2.one,
                   new Vector2(pad, claimH + gap * 2f), new Vector2(-pad, -headerBottom));
            var grid = gridRt.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(UiTheme.Px(85f), UiTheme.Py(104f));
            grid.spacing = new Vector2(UiTheme.S8, UiTheme.S8);
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 4;

            var faces = new Image[AttendanceScreen.Days];
            var dayLabels = new TextMeshProUGUI[AttendanceScreen.Days];
            var rewardLabels = new TextMeshProUGUI[AttendanceScreen.Days];
            var checks = new GameObject[AttendanceScreen.Days];
            var locks = new GameObject[AttendanceScreen.Days];

            for (int i = 0; i < AttendanceScreen.Days; i++)
            {
                faces[i] = UiKit.Surface("Cell" + i, gridRt, UiTheme.Panel2, UiTheme.Px(14f), out var cellRt);

                var day = UiKit.Label("Day", cellRt, fonts.Body, UiTheme.LabelSize, UiTheme.Ink,
                                     TextAlignmentOptions.Center, out var dayRt, (i + 1) + "일차");
                Anchor(dayRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(UiTheme.S4, -UiTheme.S8 - UiTheme.Line(UiTheme.LabelSize)),
                       new Vector2(-UiTheme.S4, -UiTheme.S8));
                dayLabels[i] = day;

                var reward = UiKit.Label("Reward", cellRt, fonts.Number, UiTheme.Caption, UiTheme.Ink2,
                                         TextAlignmentOptions.Center, out var rewardRt, "");
                reward.textWrappingMode = TextWrappingModes.Normal;
                Anchor(rewardRt, new Vector2(0f, 0f), new Vector2(1f, 1f),
                       new Vector2(UiTheme.S4, UiTheme.Py(22f)),
                       new Vector2(-UiTheme.S4, -UiTheme.S8 - UiTheme.Line(UiTheme.LabelSize)));
                rewardLabels[i] = reward;

                var checkRt = UiKit.Node("Check", cellRt);
                Anchor(checkRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                       new Vector2(-UiTheme.Px(9f), UiTheme.S4), new Vector2(UiTheme.Px(9f), UiTheme.S4 + UiTheme.Py(18f)));
                var checkIcon = checkRt.gameObject.AddComponent<Image>();
                checkIcon.sprite = UiArt.Icon("check_circle");
                checkIcon.color = UiTheme.Success;
                checkIcon.preserveAspect = true;
                checkIcon.raycastTarget = false;
                checks[i] = checkRt.gameObject;

                var lockRt = UiKit.Node("Lock", cellRt);
                Anchor(lockRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                       new Vector2(-UiTheme.Px(9f), UiTheme.S4), new Vector2(UiTheme.Px(9f), UiTheme.S4 + UiTheme.Py(18f)));
                var lockIcon = lockRt.gameObject.AddComponent<Image>();
                lockIcon.sprite = UiArt.Icon("lock");
                lockIcon.color = UiTheme.Ink3;
                lockIcon.preserveAspect = true;
                lockIcon.raycastTarget = false;
                locks[i] = lockRt.gameObject;
            }

            var claim = UiKit.Button("Claim", root, fonts.Title, "",
                                     UiButtonKind.Success, out var claimRt, claimH, UiTheme.Px(19f));
            Anchor(claimRt, Vector2.zero, new Vector2(1f, 0f),
                   new Vector2(pad, gap), new Vector2(-pad, gap + claimH));

            var screen = root.gameObject.AddComponent<AttendanceScreen>();
            Wire(screen, ("backButton", back), ("claimButton", claim));
            WireArray(screen, "faces", faces);
            WireArray(screen, "dayLabels", dayLabels);
            WireArray(screen, "rewardLabels", rewardLabels);
            WireArray(screen, "checks", checks);
            WireArray(screen, "locks", locks);
            root.gameObject.SetActive(false);
            return screen;
        }

        // ------------------------------------------------------------------ 결과 (2g)

        /// <summary>
        /// The result screen: stars, the star checklist with what the run actually scored,
        /// three run numbers, the rewards, the MVP, and the three ways out.
        /// </summary>
        /// <summary>
        /// 10.16-n's .ov-card: a 330-wide sheet centred over whatever is behind it, 14px
        /// corners, 18px padding, 10px between rows, sized to its content. Both result
        /// screens are this same card - the build does not have a full-screen result.
        /// </summary>
        static RectTransform BuildOverlayCard(RectTransform parent, out RectTransform shade,
                                              out VerticalLayoutGroup column)
        {
            var root = UiKit.Node("Shade", parent);
            Stretch(root);
            var dim = root.gameObject.AddComponent<Image>();
            dim.color = new Color(UiTheme.Ink.r, UiTheme.Ink.g, UiTheme.Ink.b, 0.55f);
            shade = root;

            var card = UiKit.Panel("Card", root, out var crt, UiTheme.Panel, UiTheme.Px(14f));
            Anchor(crt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                   new Vector2(-UiTheme.Px(165f), 0f), new Vector2(UiTheme.Px(165f), 0f));
            crt.pivot = new Vector2(0.5f, 0.5f);

            column = crt.gameObject.AddComponent<VerticalLayoutGroup>();
            column.spacing = UiTheme.Py(10f);
            column.padding = new RectOffset((int)UiTheme.Px(18f), (int)UiTheme.Px(18f),
                                            (int)UiTheme.Py(18f), (int)UiTheme.Py(16f));
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            var fit = crt.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return crt;
        }

        /// <summary>One row of the card, with a height the layout will honour.</summary>
        static RectTransform CardRow(RectTransform card, string id, float height)
        {
            var rt = UiKit.Node(id, card);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = UiTheme.Py(height);
            return rt;
        }

        /// <summary>
        /// 10.16-n's .ov-stats: the number on top in the LED face, the label under it in
        /// muted grey. Not a tile - just two lines, laid side by side with a 16px gap.
        /// </summary>
        static TextMeshProUGUI OverlayStat(RectTransform row, Fonts fonts, string caption,
                                           out TextMeshProUGUI captionLabel)
        {
            var cell = UiKit.Node(caption, row);
            var le = cell.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;

            // 20pt needs ~30px of box; the build's own 20px row would blank the line.
            var value = UiKit.Label("Value", cell, fonts.Number, UiTheme.Py(20f), UiTheme.Ink,
                                    TextAlignmentOptions.Left, out var vrt, "0");
            value.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(vrt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(0f, -UiTheme.Py(30f)), Vector2.zero);

            captionLabel = UiKit.Label("Caption", cell, fonts.Body, UiTheme.Py(13f),
                                       UiTheme.Ink2, TextAlignmentOptions.Left,
                                       out var crt2, caption);
            captionLabel.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(crt2, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(0f, -UiTheme.Py(52f)), new Vector2(0f, -UiTheme.Py(30f)));
            return value;
        }
        /// <summary>
        /// 전투 결과, measured off 10.16-n. The build shows a 330-wide card over the board -
        /// a title, the three stars, a paragraph that explains what just happened, three
        /// read-outs, the reward chips and two buttons. Not the old full-screen layout with
        /// a star checklist and stat tiles; 10.16-n has neither.
        /// </summary>
        static ResultScreen BuildResultScreen(RectTransform parent, Fonts fonts, SpriteLibrary library)
        {
            var go = UI("Result", parent, out var rt);
            Stretch(rt);
            var card = BuildOverlayCard(rt, out _, out _);

            var title = UiKit.Label("Title", CardRow(card, "TitleRow", 42f), fonts.Title,
                                    UiTheme.Py(26f), UiTheme.Ink,
                                    TextAlignmentOptions.Left, out var trt, "");
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.overflowMode = TextOverflowModes.Ellipsis;
            Stretch(trt);

            // 별 세 개. 켜진 것은 앰버, 꺼진 것은 선 색 — 글자가 아니라 그림으로 그린다.
            var starRow = CardRow(card, "Stars", 34f);
            var starLayout = UiKit.Row(starRow, UiTheme.Px(4f), TextAnchor.MiddleLeft);
            starLayout.childControlWidth = false;
            starLayout.childControlHeight = false;
            starLayout.childForceExpandWidth = false;
            var stars = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                var srt = UiKit.Node("Star" + i, starRow);
                srt.sizeDelta = new Vector2(UiTheme.Py(30f), UiTheme.Py(30f));
                stars[i] = srt.gameObject.AddComponent<Image>();
                stars[i].sprite = UiArt.Currency("star_on");
                stars[i].preserveAspect = true;
                stars[i].raycastTarget = false;
            }

            var body = UiKit.Label("Body", CardRow(card, "BodyRow", 76f), fonts.Body,
                                   UiTheme.Py(13f), UiTheme.Ink2,
                                   TextAlignmentOptions.TopLeft, out var brt, "");
            body.textWrappingMode = TextWrappingModes.Normal;
            body.lineSpacing = 12f;                 // 13px * 1.6 행간
            Stretch(brt);

            var statRow = CardRow(card, "Stats", 52f);
            var statLayout = UiKit.Row(statRow, UiTheme.Px(16f), TextAnchor.UpperLeft);
            statLayout.childControlWidth = true;
            statLayout.childControlHeight = true;
            statLayout.childForceExpandWidth = true;
            var kills = OverlayStat(statRow, fonts, "처치", out _);
            var life = OverlayStat(statRow, fonts, "청결도", out _);
            var time = OverlayStat(statRow, fonts, "시간", out _);

            var rewardRow = CardRow(card, "Rewards", 30f);
            var rewardLayout = UiKit.Row(rewardRow, UiTheme.Px(8f), TextAnchor.MiddleLeft);
            rewardLayout.childControlWidth = true;
            rewardLayout.childControlHeight = true;
            rewardLayout.childForceExpandWidth = false;
            var rewardLabel = UiKit.Label("Rewards", rewardRow, fonts.Body, UiTheme.Py(12.5f),
                                          UiTheme.Ink, TextAlignmentOptions.Left,
                                          out var rwrt, "");
            rewardLabel.textWrappingMode = TextWrappingModes.Normal;
            Stretch(rwrt);

            // 전력 스킬 해금과 패스 경험치가 겹치면 두 줄이 된다.
            var note = UiKit.Label("Note", CardRow(card, "NoteRow", 46f), fonts.Body,
                                   UiTheme.Py(12f), UiTheme.Led,
                                   TextAlignmentOptions.TopLeft, out var nrt, "");
            note.textWrappingMode = TextWrappingModes.Normal;
            Stretch(nrt);

            // MVP 는 **원본에 없다.** 이미 만들어 둔 것을 버리지 않고 카드 맨 아래 한 줄로
            // 남긴다. 원본과 다른 점으로 기록해 둔다.
            var mvpRow = CardRow(card, "Mvp", 44f);
            var mvpBg = mvpRow.gameObject.AddComponent<Image>();
            mvpBg.sprite = GeneratedArt.RoundedNineSlice("ui_chip8",
                Mathf.RoundToInt(UiTheme.Px(8f)));
            mvpBg.type = Image.Type.Sliced;
            mvpBg.color = UiTheme.PageBg;
            var mvpPicRt = UiKit.Node("Pic", mvpRow);
            Anchor(mvpPicRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                   new Vector2(UiTheme.Px(6f), -UiTheme.Py(16f)),
                   new Vector2(UiTheme.Px(6f) + UiTheme.Py(32f), UiTheme.Py(16f)));
            var mvpPic = mvpPicRt.gameObject.AddComponent<Image>();
            mvpPic.preserveAspect = true;
            mvpPic.raycastTarget = false;
            var mvpName = UiKit.Label("Name", mvpRow, fonts.Body, UiTheme.Py(12.5f),
                                      UiTheme.Ink, TextAlignmentOptions.Left,
                                      out var mnrt, "");
            mvpName.textWrappingMode = TextWrappingModes.NoWrap;
            mvpName.overflowMode = TextOverflowModes.Ellipsis;
            Anchor(mnrt, new Vector2(0f, 0f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(12f) + UiTheme.Py(32f), 0f),
                   new Vector2(-UiTheme.Px(8f), 0f));

            var btnRow = CardRow(card, "Buttons", 48f);
            var btnGrid = btnRow.gameObject.AddComponent<GridLayoutGroup>();
            btnGrid.spacing = new Vector2(UiTheme.Px(8f), 0f);
            btnGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            btnGrid.constraintCount = 2;
            btnGrid.cellSize = new Vector2(UiTheme.Px(143f), UiTheme.Py(48f));

            var home = UiKit.Button("Home", btnRow, fonts.Body, "로비로",
                                    UiButtonKind.Secondary, out _, UiTheme.Py(48f),
                                    UiTheme.Py(15f));
            var next = UiKit.Button("Next", btnRow, fonts.Body, "다시 하기",
                                    UiButtonKind.DarkSolid, out var nxrt, UiTheme.Py(48f),
                                    UiTheme.Py(15f));
            var nextLabel = nxrt.GetComponentInChildren<TextMeshProUGUI>();
            nextLabel.textWrappingMode = TextWrappingModes.NoWrap;

            var screen = go.AddComponent<ResultScreen>();
            Wire(screen,
                 ("titleLabel", title), ("bodyLabel", body),
                 ("killsValue", kills), ("lifeValue", life), ("timeValue", time),
                 ("rewardLabel", rewardLabel), ("noteLabel", note),
                 ("mvpRoot", mvpRow.gameObject), ("mvpPortrait", mvpPic),
                 ("mvpNameLabel", mvpName),
                 ("homeButton", home), ("nextButton", next), ("nextLabel", nextLabel),
                 ("starOn", UiArt.Currency("star_on")),
                 ("starOff", UiArt.Currency("star_off")),
                 ("sprites", library));
            WireArray(screen, "starImages", stars);
            go.SetActive(false);
            return screen;
        }


        /// <summary>
        /// Fills an array of serializable structs, which Wire cannot reach: each element has
        /// its own set of object fields rather than being one reference.
        /// </summary>
        static void WireStructArray(Object target, string field, string[] names, object[][] rows)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) { Debug.LogWarning($"[DustZero] {target.GetType().Name}.{field} 를 찾지 못했습니다."); return; }
            prop.arraySize = rows.Length;
            for (int i = 0; i < rows.Length; i++)
            {
                var element = prop.GetArrayElementAtIndex(i);
                for (int f = 0; f < names.Length; f++)
                {
                    var sub = element.FindPropertyRelative(names[f]);
                    if (sub == null) continue;
                    sub.objectReferenceValue = (Object)rows[i][f];
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Assigns private [SerializeField] references without making them public.</summary>
        /// <summary>Sets a private serialized colour or number, which Wire cannot carry.</summary>
        static void SetPrivate(Object target, string field, Color value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) { Debug.LogWarning($"[DustZero] {target.GetType().Name}.{field} 를 찾지 못했습니다."); return; }
            prop.colorValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

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

        /// <summary>
        /// Assigns plain-value fields - colours and floats. Wire only carries object
        /// references, and a chip's palette is data rather than a reference.
        /// </summary>
        static void WireValues(Object target, params (string field, object value)[] pairs)
        {
            var so = new SerializedObject(target);
            foreach (var (field, value) in pairs)
            {
                var prop = so.FindProperty(field);
                if (prop == null) { Debug.LogWarning($"[DustZero] {target.GetType().Name}.{field} 를 찾지 못했습니다."); continue; }
                switch (value)
                {
                    case Color c: prop.colorValue = c; break;
                    case float f: prop.floatValue = f; break;
                    case int i: prop.intValue = i; break;
                    case bool b: prop.boolValue = b; break;
                    case string str: prop.stringValue = str; break;
                    default:
                        Debug.LogWarning($"[DustZero] {field}: {value?.GetType().Name} 는 WireValues 가 모르는 형식입니다.");
                        break;
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Assigns an object array field, for the tile variants the floors cycle through.</summary>
        /// <summary>문자열 배열 필드. WireArray 는 오브젝트 참조만 나른다.</summary>
        static void WireStrings(Object target, string field, string[] values)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) { Debug.LogWarning($"[DustZero] {target.GetType().Name}.{field} 를 찾지 못했습니다."); return; }
            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                prop.GetArrayElementAtIndex(i).stringValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

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

            // 아레나는 대청소 카메라만 그린다.
            cam.cullingMask = ~((1 << ArenaLayer) | (1 << LeagueLayer));

            // 화면 내용은 가운데 기둥에만 있다. 판 카메라도 그 안에서만 그리게 하지 않으면
            // 좌우 여백에 **지난 화면의 월드**가 그대로 남아 비친다(로비에서 전투판 타일이
            // 가장자리에 보였다). 여백은 아래 Backdrop 이 바탕색으로 칠한다.
            var backdropGo = new GameObject("Backdrop");
            var backdrop = backdropGo.AddComponent<Camera>();
            backdrop.orthographic = true;
            backdrop.clearFlags = CameraClearFlags.SolidColor;
            backdrop.backgroundColor = PageBg;
            backdrop.cullingMask = 0;
            backdrop.depth = -100f;
            backdrop.transform.position = new Vector3(0f, 0f, -20f);

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
            var warnRoot = new GameObject("SpawnWarnings").transform;
            warnRoot.SetParent(boardGo.transform, false);

            var board = boardGo.AddComponent<BoardView>();
            Wire(board, ("floorMap", floorMap), ("overlayMap", overlayMap),
                 ("entityRoot", entityRoot), ("entityPrefab", prefabs.Entity),
                 ("sprites", library), ("highlightPrefab", prefabs.Highlight),
                 ("highlightRoot", highlightRoot), ("labelRoot", labelRoot),
                 ("solidSprite", art.Square), ("rangeRingSprite", UiArt.Battle("range_ring")),
                 ("pathTile", tiles.Path), ("buildableTile", tiles.Buildable),
                 ("blockedTile", tiles.Blocked), ("spawnTile", tiles.Spawn),
                 ("goalTile", tiles.Goal), ("stoveTile", tiles.Stove), ("wetTile", tiles.Wet),
                 ("boardFont", fonts.Body),
                 ("validHighlight", UiArt.Battle("tile_hl_valid")),
                 ("invalidHighlight", UiArt.Battle("tile_hl_invalid")),
                 ("briefHighlight", UiArt.Battle("tile_hl_brief")),
                 ("selectRingSprite", UiArt.Battle("select_ring")),
                 ("overheatRingSprite", UiArt.Battle("overheat_ring")),
                 ("stoveRingSprite", art.StoveRing),
                 ("spawnWarnPrefab", prefabs.SpawnWarn), ("spawnWarnRoot", warnRoot));

            // ---- fx
            var fxGo = new GameObject("FxDirector");
            var fxRoot = new GameObject("FxRoot").transform;
            fxRoot.SetParent(fxGo.transform, false);
            var fx = fxGo.AddComponent<FxDirector>();
            Wire(fx, ("root", fxRoot), ("particlePrefab", prefabs.Particle), ("labelPrefab", prefabs.Label),
                 ("roundSprite", art.Circle), ("squareSprite", art.Square),
                 ("numberFont", fonts.Number), ("labelFont", fonts.Body), ("numberOutline", OutlinedNumberMaterial(fonts.Number)), ("shakeCamera", cam));

            // ---- canvas
            var canvasGo = new GameObject("UI");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            // **맞춰 넣는다(Expand).** 원본 페이지(430x932)를 한 배율로 올려 가운데 기둥에
            // 담으므로, 두 축 중 **작은 쪽**에 맞춰야 기둥이 잘리지 않는다.
            //
            // 가로에 맞추면(예전 방식) 긴 화면에서 캔버스 세로가 1920 을 넘어 932 로 잰
            // 세로 치수가 어긋났다 — 좁은 창에서 전투판이 카드 줄을 넘어 흐른 증상이다.
            // 세로에 맞추면 그 문제는 사라지지만, 원본보다 **좁은** 화면(720x1600 = 0.45)
            // 에서는 886 기둥이 캔버스 폭(864)을 넘어 잘린다. Expand 는 둘 다 막는다.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            canvasGo.AddComponent<GraphicRaycaster>();

            var safe = UI("SafeArea", canvasGo.transform, out var safeRt);
            Stretch(safeRt);
            safe.AddComponent<SafeAreaFitter>();

            // 원본은 430 폭 페이지다. 두 축을 같은 배율로 올리면 886 폭이 되고 1080 화면에
            // 좌우 97px 씩 여백이 남는다. 화면 내용은 **그 기둥 안**에 담는다 — 그래야
            // 가장자리에 붙인 것들이 1080 이 아니라 886 을 기준으로 자리를 잡고,
            // 원본의 비율(전투판 7x10 이 상자를 꽉 채우는 것 포함)이 그대로 산다.
            var column = UI("Column", safe.transform, out var columnRt);
            columnRt.anchorMin = columnRt.anchorMax = new Vector2(0.5f, 0.5f);
            columnRt.pivot = new Vector2(0.5f, 0.5f);
            // **세로도 페이지 크기로 고정**한다. 캔버스를 세로로 꽉 채우면 화면이 페이지보다
            // 길 때(720x1600 같은 좁고 긴 화면) 아래에 붙인 것들이 페이지 밖으로 내려가,
            // 같은 페이지 좌표로 잡은 카메라와 어긋난다.
            columnRt.sizeDelta = new Vector2(UiTheme.ColumnWidth, UiTheme.Py(UiTheme.PageH));
            columnRt.anchoredPosition = Vector2.zero;
            safe = column;

            var lobby = BuildLobbyUI(safe.transform, art, fonts, library, prefabs);
            var battle = BuildBattleUI(safe.transform, art, fonts, prefabs, board, fx, cam, library);

            // ---- 대청소: its own always-alive camera/world, same arrangement as the board.
            var (survCam, survView) = BuildSurvivorWorld(art, fonts, prefabs, library);
            var survFxGo = new GameObject("SurvivorFx");
            var survFxRoot = new GameObject("FxRoot").transform;
            survFxRoot.SetParent(survFxGo.transform, false);
            var survFx = survFxGo.AddComponent<FxDirector>();
            Wire(survFx, ("root", survFxRoot), ("particlePrefab", prefabs.Particle), ("labelPrefab", prefabs.Label),
                 ("roundSprite", art.Circle), ("squareSprite", art.Square),
                 ("numberFont", fonts.Number), ("labelFont", fonts.Body), ("numberOutline", OutlinedNumberMaterial(fonts.Number)), ("shakeCamera", survCam));
            SetLayerDeep(survFxGo, ArenaLayer);
            var survivor = BuildSurvivorUI(safe.transform, art, fonts, prefabs, survView, survFx, survCam, library);

            // ---- 가전 대청소 리그: 같은 방식으로 자기 카메라와 월드를 들고 있다.
            var (lgCam, lgView) = BuildLeagueWorld(art, prefabs, library);
            var league = BuildLeagueUI(safe.transform, fonts, prefabs, lgView, lgCam, library);

            // ---- lobby guide: built last so it renders above every screen above it in the
            // hierarchy, since its flows span the lobby, the battle brief and the result screen.
            var guideOverlay = BuildGuideOverlay(safe.transform, fonts);

            // ---- boot overlay
            var bootGo = UI("Boot", safe.transform, out var bootRt);
            Stretch(bootRt);
            var bootBg = bootGo.AddComponent<Image>();
            bootBg.color = PageBg;
            var bootText = Label("BootText", bootGo.transform, fonts.Body, 24f, Ink,
                                 TextAlignmentOptions.Center, out var btrt, "데이터 테이블을 불러오는 중…");
            Stretch(btrt, 48f);
            bootText.enableWordWrapping = true;

            // ---- music
            var audioGo = new GameObject("AudioDirector");
            var audio = audioGo.AddComponent<AudioDirector>();
            Wire(audio,
                 ("lobbyClip", LoadBgm("lobby")),
                 ("battleClip", LoadBgm("battle")),
                 ("survivalClip", LoadBgm("survival")));
            // Nothing else in the scene carries a listener - the cameras here are built by
            // hand, and AddComponent<Camera> does not bring one along - so without this the
            // sources would play silently.
            if (Object.FindAnyObjectByType<AudioListener>() == null) audioGo.AddComponent<AudioListener>();

            // ---- app root
            var appGo = new GameObject("AppRoot");
            var app = appGo.AddComponent<AppRoot>();
            Wire(app, ("lobby", lobby), ("battle", battle), ("survivor", survivor), ("league", league),
                 ("guideOverlay", guideOverlay), ("audioDirector", audio),
                 ("bootRoot", bootGo), ("bootLabel", bootText));

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        /// <summary>
        /// The lobby from redesign 1a: wallet on top, a side rail, the room filling the
        /// middle, and five tabs along the bottom. The seven menus of 10.15-h are preserved -
        /// \ucf58\ud150\uce20 and \ud3b8\uc131 become sub-tabs rather than disappearing.
        /// </summary>
        static LobbyScreen BuildLobbyUI(Transform parent, Shapes art, Fonts fonts,
                                        SpriteLibrary library, Prefabs prefabs)
        {
            var go = UI("Lobby", parent, out var rt);
            Stretch(rt);
            var bg = go.AddComponent<Image>();
            bg.color = UiTheme.PageBg;

            float pad = UiTheme.ScreenPad;
            // 10.16-n's tab bar is 78px tall and carries no separate home-indicator strip;
            // the page handles that inset itself. Adding one here pushed every bottom-anchored
            // measurement up by ~94px, which is what put the room note in the middle of the room.
            float navH = UiTheme.BottomNav;

            // ---- top bar. Measured on the build's own page: the plate is 398x64 at
            // (16,12), the wordmark sits left, and the purse is two stacked label/value
            // columns with a round top-up button hanging off the crystal one.
            var topRt = UiKit.Node("TopBar", rt);
            Anchor(topRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(16f), -UiTheme.Py(12f) - UiTheme.Py(64f)),
                   new Vector2(-UiTheme.Px(16f), -UiTheme.Py(12f)));
            UiKit.Panel("Plate", topRt, out var topPlate, UiTheme.DarkPanel, UiTheme.Py(14f));
            Stretch(topPlate);
            topPlate.SetAsFirstSibling();

            // Wordmark: "OPERATION" in small spaced orange caps over the title.
            float brandX = UiTheme.Px(14f);
            var brandKicker = UiKit.Label("Kicker", topRt, fonts.Body, UiTheme.Py(10f),
                                          UiTheme.Primary, TextAlignmentOptions.Left,
                                          out var bkRt, "O P E R A T I O N");
            Anchor(bkRt, new Vector2(0f, 1f), new Vector2(0f, 1f),
                   new Vector2(brandX, -UiTheme.Py(13f) - UiTheme.Line(UiTheme.Py(10f))),
                   new Vector2(brandX + UiTheme.Px(150f), -UiTheme.Py(13f)));

            var brandTitle = UiKit.Label("Brand", topRt, fonts.Title, UiTheme.Py(24f),
                                         UiTheme.DarkInk, TextAlignmentOptions.Left,
                                         out var btRt, "더스트 제로");
            // 원본은 글자가 **찍히는 자리**를 화면 y=39 에 둔다(430 기준에서 직접 쟀다).
            // TMP 는 ink 를 줄 상자 가운데에 놓으므로, 상자 위를 그만큼 올려 잡아야
            // ink 가 같은 자리에 앉는다. 상자 위에서 바로 Py(26) 을 쓰면 5.2px 내려간다.
            float brandInk = UiTheme.Py(24f);
            float brandBox = UiTheme.Line(brandInk);
            float brandTop = UiTheme.Py(39f) - UiTheme.Py(12f) - (brandBox - brandInk) * 0.5f;
            Anchor(btRt, new Vector2(0f, 1f), new Vector2(0f, 1f),
                   new Vector2(brandX, -brandTop - brandBox),
                   new Vector2(brandX + UiTheme.Px(170f), -brandTop));

            // One purse column: a muted caption over an icon and a coloured readout.
            TextMeshProUGUI Purse(string id, string caption, Sprite icon, Color numberInk,
                                  float left, float width, out RectTransform colRt)
            {
                colRt = UiKit.Node(id, topRt);
                Anchor(colRt, new Vector2(0f, 1f), new Vector2(0f, 1f),
                       new Vector2(left, -UiTheme.Py(12f) - UiTheme.Py(40f)),
                       new Vector2(left + width, -UiTheme.Py(12f)));

                UiKit.Label("Caption", colRt, fonts.Body, UiTheme.Py(10f), UiTheme.DarkInk2,
                            TextAlignmentOptions.Left, out var capRt, caption);
                Anchor(capRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(0f, -UiTheme.Line(UiTheme.Py(10f))), Vector2.zero);

                float ic = UiTheme.Py(20f);
                var icoRt = UiKit.Node("Icon", colRt);
                Anchor(icoRt, new Vector2(0f, 0f), new Vector2(0f, 0f),
                       new Vector2(0f, 0f), new Vector2(ic, ic));
                var ico = icoRt.gameObject.AddComponent<Image>();
                ico.sprite = icon;
                ico.preserveAspect = true;
                ico.raycastTarget = false;

                var value = UiKit.Label("Value", colRt, fonts.Number, UiTheme.Py(20f), numberInk,
                                        TextAlignmentOptions.Left, out var valRt, "0");
                Anchor(valRt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                       new Vector2(UiTheme.Px(24f), 0f),
                       new Vector2(0f, UiTheme.Line(UiTheme.Py(20f))));
                return value;
            }

            var coinLabel = Purse("Coin", "코인", LoadIcon("coin"), UiTheme.Primary,
                                  UiTheme.Px(182f), UiTheme.Px(78f), out _);
            var crystalLabel = Purse("Crystal", "크리스탈", LoadIcon("crystal"), Hex("#7FC4F5"),
                                     UiTheme.Px(276f), UiTheme.Px(108f), out _);

            // The top-up button belongs to the crystal readout - 22px across at page x378,
            // right after the number - and it is crystal blue, not the action orange.
            float plusD = UiTheme.Py(22f);
            var plusRt = UiKit.Node("TopUp", topRt);
            Anchor(plusRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                   new Vector2(UiTheme.Px(362f), -plusD * 0.5f),
                   new Vector2(UiTheme.Px(362f) + plusD, plusD * 0.5f));
            var plusBg = plusRt.gameObject.AddComponent<Image>();
            plusBg.sprite = UiKit.Rounded(plusD * 0.5f);
            plusBg.type = Image.Type.Sliced;
            plusBg.color = Hex("#7FC4F5");
            plusBg.raycastTarget = false;
            // 15px would need a 46px line box in a 45px button and TMP would drop the glyph
            // entirely, leaving a bare blue disc. Sized against the rect instead.
            UiKit.Label("Plus", plusRt, fonts.Body, plusD * 0.52f, UiTheme.Ink,
                        TextAlignmentOptions.Center, out var plusTxt, "+");
            Stretch(plusTxt);

            // The per-tab heading sits under the bar on the page, not inside it.
            var titleLabel = UiKit.Label("TabTitle", rt, fonts.Title, UiTheme.Py(24f), UiTheme.Ink,
                                         TextAlignmentOptions.Left, out var titleRt, "");
            Anchor(titleRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(20f), -UiTheme.Py(88f) - UiTheme.Line(UiTheme.Py(24f))),
                   new Vector2(-UiTheme.Px(20f), -UiTheme.Py(88f)));

            // ---- tab panels fill everything between the top bar and the nav
            var panelsRt = UiKit.Node("Panels", rt);
            // Starts under the measured 12+64 top bar, ends at the top of the tab bar.
            Anchor(panelsRt, Vector2.zero, Vector2.one,
                   new Vector2(0f, navH), new Vector2(0f, -UiTheme.Py(76f)));

            var homePanel = BuildHomePanel(panelsRt, fonts, library, art, out var homeTab);
            var stagePanel = BuildStagePanel(panelsRt, fonts, prefabs, library, out var stageTab);
            var contentPanel = BuildContentPanel(panelsRt, fonts, library, out var contentTab);
            var unitsPanel = BuildUnitsPanel(panelsRt, fonts, prefabs, library, out var unitsTab);
            var deckPanel = BuildDeckPanel(panelsRt, fonts, prefabs, library, out var deckTab);
            var gachaPanel = BuildGachaPanel(panelsRt, fonts, prefabs, library, out var gachaTab);
            var shopPanel = BuildShopPanel(panelsRt, fonts, prefabs, out var shopTab);

            // ---- home clusters. All measured on the 430x932 page: 출석부 86x54 at (16,86),
            // 자동 청소 86x61 at (16,148), 게임 초기화 83x34 and 설정 65x34 on one row at y86,
            // and the 시즌 패스 card 86x82 at (328,128). Each pill is a title row with a small
            // icon, an orange status line, and on two of them a 4px progress bar.
            var rail = UiKit.Node("HomeRail", rt);
            Stretch(rail);

            UiButton HomePill(string id, string title, Sprite icon,
                              float px, float py, float pw, float ph,
                              bool withBar, float titlePx,
                              out RectTransform outRt, out TextMeshProUGUI sub,
                              out RectTransform fill)
            {
                var b2 = UiKit.Button(id, rail, fonts.Body, string.Empty, UiButtonKind.Dark,
                                      out outRt, UiTheme.Py(ph), UiTheme.Py(titlePx));
                Anchor(outRt, new Vector2(0f, 1f), new Vector2(0f, 1f),
                       new Vector2(UiTheme.Px(px), -UiTheme.Py(py) - UiTheme.Py(ph)),
                       new Vector2(UiTheme.Px(px + pw), -UiTheme.Py(py)));

                float titleH = UiTheme.Line(UiTheme.Py(titlePx));
                float rowTop = -UiTheme.Py(9f);

                // Icon and word share the title row, centred together.
                float ic = UiTheme.Py(15f);
                var row = UiKit.Node("TitleRow", outRt);
                Anchor(row, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(UiTheme.Px(4f), rowTop - titleH),
                       new Vector2(-UiTheme.Px(4f), rowTop));
                var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                layout.spacing = UiTheme.Px(4f);
                layout.childAlignment = TextAnchor.MiddleCenter;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;
                layout.childControlWidth = true;
                layout.childControlHeight = true;

                if (icon != null)
                {
                    var icoRt = UiKit.Node("Icon", row);
                    var ico = icoRt.gameObject.AddComponent<Image>();
                    ico.sprite = icon;
                    ico.preserveAspect = true;
                    ico.raycastTarget = false;
                    var le = icoRt.gameObject.AddComponent<LayoutElement>();
                    le.preferredWidth = ic; le.preferredHeight = ic;
                    le.minWidth = ic; le.minHeight = ic;
                }

                var t = UiKit.Label("Title", row, fonts.Body, UiTheme.Py(titlePx), UiTheme.DarkInk,
                                    TextAlignmentOptions.Left, out var trt, title);
                t.textWrappingMode = TextWrappingModes.NoWrap;
                // 알약은 86px 폭에 아이콘과 글자를 같이 넣는다. 우리 글꼴은 원본 웹 글꼴보다
                // 조금 넓어 '자동 청소' 가 2px 넘쳐 말줄임으로 잘렸다. 잘라 내기보다
                // 살짝 줄여 다 보여 준다.
                t.enableAutoSizing = true;
                t.fontSizeMin = UiTheme.Py(titlePx) * 0.8f;
                t.fontSizeMax = UiTheme.Py(titlePx);
                var tle = trt.gameObject.AddComponent<LayoutElement>();
                tle.preferredHeight = titleH;
                tle.minHeight = titleH;

                sub = UiKit.Label("Sub", outRt, fonts.Body, UiTheme.Py(11f), UiTheme.Primary,
                                  TextAlignmentOptions.Center, out var srt, string.Empty);
                sub.textWrappingMode = TextWrappingModes.NoWrap;
                float subH = UiTheme.Line(UiTheme.Py(11f));
                Anchor(srt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(UiTheme.Px(2f), rowTop - titleH - subH),
                       new Vector2(-UiTheme.Px(2f), rowTop - titleH));

                fill = null;
                if (withBar)
                {
                    var barRt = UiKit.Bar("Bar", outRt, UiTheme.DarkPanel2, UiTheme.Primary,
                                          UiTheme.Py(4f), out fill);
                    Anchor(barRt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                           new Vector2(UiTheme.Px(8f), UiTheme.Py(10f)),
                           new Vector2(-UiTheme.Px(8f), UiTheme.Py(14f)));
                }
                return b2;
            }

            var attendanceButton = HomePill("Attendance", "출석부", LoadIcon("attend"),
                                            16f, 86f, 86f, 54f, false, 16f,
                                            out var attRt, out var attSub, out _);
            var idleButton = HomePill("AutoClean", "자동 청소", LoadIcon("idle"),
                                      16f, 148f, 86f, 61f, true, 16f,
                                      out var idleRt, out var idleSub, out var idleFill);
            var homeResetButton = HomePill("Reset", "게임 초기화", null,
                                           261f, 86f, 83f, 34f, false, 12f,
                                           out var resetRt, out _, out _);
            var settingsButton = HomePill("Settings", "설정", LoadIcon("settings"),
                                          349f, 86f, 65f, 34f, false, 12f,
                                          out var homeSetRt, out _, out _);

            // 시즌 패스: the one card the build rings in the accent colour. Its rows are
            // caption / level / bar / status, so the shared status slot moves to the bottom.
            var passButton = HomePill("Pass", "시즌 패스", LoadIcon("pass"),
                                      328f, 128f, 86f, 82f, false, 11f,
                                      out var passRt, out var passSub, out _);
            {
                var ring = UiKit.Node("Ring", passRt);
                Stretch(ring);
                var ringImg = ring.gameObject.AddComponent<Image>();
                ringImg.sprite = UiKit.FlatButtonOutline;
                ringImg.type = Image.Type.Sliced;
                ringImg.color = UiTheme.Primary;
                ringImg.raycastTarget = false;
                ring.SetAsFirstSibling();

                float subH = UiTheme.Line(UiTheme.Py(11f));
                var psRt = (RectTransform)passSub.transform;
                Anchor(psRt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                       new Vector2(UiTheme.Px(4f), UiTheme.Py(6f)),
                       new Vector2(-UiTheme.Px(4f), UiTheme.Py(6f) + subH));

                var passBar = UiKit.Bar("Bar", passRt, UiTheme.DarkPanel2, UiTheme.Primary,
                                        UiTheme.Py(4f), out _);
                Anchor(passBar, new Vector2(0f, 0f), new Vector2(1f, 0f),
                       new Vector2(UiTheme.Px(8f), UiTheme.Py(6f) + subH + UiTheme.Py(4f)),
                       new Vector2(-UiTheme.Px(8f), UiTheme.Py(6f) + subH + UiTheme.Py(8f)));
            }

            float lvSize = UiTheme.Py(16f);
            float lvBottom = UiTheme.Py(6f) + UiTheme.Line(UiTheme.Py(11f)) + UiTheme.Py(10f);
            var passBadge = UiKit.Label("Level", passRt, fonts.Number, lvSize,
                                        UiTheme.DarkInk, TextAlignmentOptions.Center,
                                        out var pbRt, "Lv.1");
            Anchor(pbRt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(UiTheme.Px(4f), lvBottom),
                   new Vector2(-UiTheme.Px(4f), lvBottom + UiTheme.Line(lvSize)));

            var attDot = UiKit.Dot("Dot", attRt);
            Anchor(attDot, Vector2.one, Vector2.one,
                   new Vector2(-UiTheme.BadgeDot * 0.7f, -UiTheme.BadgeDot * 0.7f),
                   new Vector2(UiTheme.BadgeDot * 0.5f, UiTheme.BadgeDot * 0.5f));

            // ---- bottom navigation: the dark strip 10.16-n puts flush to the bottom edge
            var navBg = UiKit.Node("BottomNav", rt);
            Anchor(navBg, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, navH));
            var navFace = navBg.gameObject.AddComponent<Image>();
            navFace.color = UiTheme.DarkPanel;
            navFace.raycastTarget = false;

            var navRow = UiKit.Node("Tabs", navBg);
            // The row fills the bar with the build's own 8px padding. Reserving a separate
            // home-indicator strip here is what left a dead band under the icons once the
            // bar was shortened to the measured 78px.
            float navPad = UiTheme.Py(7f);
            // 원본은 탭 줄을 **좌우 16px 안쪽**에 두고 2px 간격으로 7칸을 나눈다.
            // 재 보면 칸 55.1, 간격(pitch) 57.1, 첫 칸 x=16, 마지막 끝 413.9 — 좌우 대칭이다.
            // 우리는 0~430 을 그냥 7등분해서 칸이 61.4 였고, 그 바람에 탭 글자가
            // 왼쪽 -5px 에서 오른쪽 +15px 까지 점점 밀렸다. 한 칸씩 보면 안 보이고
            // 여섯 칸을 늘어놓고 재야 보이는 종류의 어긋남이다.
            float navSide = UiTheme.Px(16f);
            Anchor(navRow, Vector2.zero, Vector2.one,
                   new Vector2(navSide, navPad), new Vector2(-navSide, -navPad));
            var navLayout = UiKit.Row(navRow, UiTheme.Px(2f));
            navLayout.childForceExpandWidth = true;
            navLayout.childForceExpandHeight = true;

            // Seven tabs in the build's own order. The icons are the converted 10.16-n art,
            // not the old pack's glyphs, so the names here match art/icon/tab_*.webp.
            var tabNames = new[] { "\ud648", "\uc2a4\ud14c\uc774\uc9c0", "\ucf58\ud150\uce20", "\uac00\uc804", "\ud3b8\uc131", "\uc18c\ud658", "\uc0c1\uc810" };
            var tabIcons = new[]
            {
                LoadIcon("tab_home"), LoadIcon("tab_stage"), LoadIcon("tab_content"),
                LoadIcon("tab_roster"), LoadIcon("tab_deck"), LoadIcon("tab_gacha"),
                LoadIcon("tab_shop"),
            };
            var tabButtons = new UiButton[tabNames.Length];
            var tabIndicators = new GameObject[tabNames.Length];
            for (int i = 0; i < tabNames.Length; i++)
            {
                tabButtons[i] = UiKit.NavItem("Tab" + i, navRow, fonts.Body, tabIcons[i],
                                              tabNames[i], out var tbrt, out _, out _,
                                              out var indicator);
                tabIndicators[i] = indicator.gameObject;
                var le = tbrt.gameObject.AddComponent<LayoutElement>();
                le.flexibleWidth = 1f;
                le.minHeight = UiTheme.Touch;
            }

            // The room fills its panel edge to edge, so the chrome is lifted above it here
            // rather than relying on the order the pieces happened to be created in.
            topRt.SetAsLastSibling();
            rail.SetAsLastSibling();
            navBg.SetAsLastSibling();

            // ---- full-screen overlays, drawn above the bottom bar
            var unitDetail = BuildUnitDetail(rt, fonts, library);
            var artViewer = BuildArtViewer(rt, fonts, library);
            var gearBag = BuildGearBag(rt, fonts, library, prefabs);
            var passScreen = BuildPassScreen(rt, fonts, prefabs);
            var attendanceScreen = BuildAttendanceScreen(rt, fonts);
            var coopPickScreen = BuildCoopPickScreen(rt, fonts, library);

            // ---- settings popup
            var settings = UiKit.Panel("Settings", rt, out var setRt,
                                       new Color(0.05f, 0.06f, 0.07f, 0.72f), 1f);
            Stretch(setRt);
            UiKit.Panel("Card", setRt, out var setCard, UiTheme.Panel, UiTheme.PanelCorner);

            // Laid out top-down from a running cursor rather than outward from the middle:
            // the rows stack under the title, the close button stays pinned to the bottom,
            // and the card is exactly as tall as the sum below, so adding a row later moves
            // the floor instead of opening a hole above it.
            float setBody = UiTheme.Line(UiTheme.Body) + UiTheme.S6 + UiTheme.ButtonShort
                            + UiTheme.S16 + UiTheme.ButtonShort
                            + UiTheme.S6 + UiTheme.ButtonShort;
            float setFoot = UiTheme.S24 + UiTheme.Line(UiTheme.Caption) + UiTheme.S6
                            + UiTheme.ButtonTall + UiTheme.S16;
            float setH = UiTheme.S24 + UiTheme.Line(UiTheme.Title) + UiTheme.S24 + setBody + setFoot;
            Anchor(setCard, new Vector2(0.1f, 0.5f), new Vector2(0.9f, 0.5f),
                   new Vector2(0f, -setH * 0.5f), new Vector2(0f, setH * 0.5f));

            float cur = UiTheme.S24;
            System.Func<float, (float top, float bottom)> rowAt = h =>
            {
                var r = (-(cur + h), -cur);
                cur += h;
                return r;
            };

            UiKit.Label("Title", setCard, fonts.Title, UiTheme.Title, UiTheme.Ink,
                        TextAlignmentOptions.Center, out var strt, "\uc124\uc815");
            var titleRow = rowAt(UiTheme.Line(UiTheme.Title));
            Anchor(strt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.S16, titleRow.top), new Vector2(-UiTheme.S16, titleRow.bottom));
            cur += UiTheme.S24;

            var bgmLabel = UiKit.Label("BgmValue", setCard, fonts.Body, UiTheme.Body, UiTheme.Ink,
                                       TextAlignmentOptions.Left, out var bglrt, "\ubc30\uacbd\uc74c");
            var bgmLabelRow = rowAt(UiTheme.Line(UiTheme.Body));
            Anchor(bglrt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.S16, bgmLabelRow.top), new Vector2(-UiTheme.S16, bgmLabelRow.bottom));
            cur += UiTheme.S6;

            var bgmRow = rowAt(UiTheme.ButtonShort);
            float stepW = UiTheme.Px(96f);
            var bgmDown = UiKit.Button("BgmDown", setCard, fonts.Body, "\u2212",
                                       UiButtonKind.Secondary, out var bdrt, UiTheme.ButtonShort);
            Anchor(bdrt, new Vector2(0f, 1f), new Vector2(0f, 1f),
                   new Vector2(UiTheme.S16, bgmRow.top), new Vector2(UiTheme.S16 + stepW, bgmRow.bottom));
            var bgmUp = UiKit.Button("BgmUp", setCard, fonts.Body, "+",
                                     UiButtonKind.Secondary, out var burt, UiTheme.ButtonShort);
            Anchor(burt, new Vector2(0f, 1f), new Vector2(0f, 1f),
                   new Vector2(UiTheme.S16 + stepW + UiTheme.S6, bgmRow.top),
                   new Vector2(UiTheme.S16 + stepW * 2f + UiTheme.S6, bgmRow.bottom));
            var bgmMute = UiKit.Button("BgmMute", setCard, fonts.Body, "\uc74c\uc18c\uac70",
                                       UiButtonKind.Secondary, out var bmrt, UiTheme.ButtonShort);
            Anchor(bmrt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.S16 + stepW * 2f + UiTheme.S12, bgmRow.top),
                   new Vector2(-UiTheme.S16, bgmRow.bottom));
            cur += UiTheme.S16;

            var tutorialReplay = UiKit.Button("TutorialReplay", setCard, fonts.Body,
                                              "\ud29c\ud1a0\ub9ac\uc5bc \ub2e4\uc2dc \ubcf4\uae30",
                                              UiButtonKind.Secondary, out var trrt, UiTheme.ButtonShort);
            var tutRow = rowAt(UiTheme.ButtonShort);
            Anchor(trrt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.S16, tutRow.top), new Vector2(-UiTheme.S16, tutRow.bottom));
            cur += UiTheme.S6;

            var resetButton = UiKit.Button("Reset", setCard, fonts.Body, "\uac8c\uc784 \ucd08\uae30\ud654",
                                           UiButtonKind.TextDanger, out var rsrt, UiTheme.ButtonShort);
            var resetRow = rowAt(UiTheme.ButtonShort);
            Anchor(rsrt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.S16, resetRow.top), new Vector2(-UiTheme.S16, resetRow.bottom));
            var settingsClose = UiKit.Button("Close", setCard, fonts.Body, "\ub2eb\uae30",
                                             UiButtonKind.Primary, out var scrt, UiTheme.ButtonTall);
            Anchor(scrt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(UiTheme.S16, UiTheme.S16), new Vector2(-UiTheme.S16, UiTheme.S16 + UiTheme.ButtonTall));
            var buildLabel = UiKit.Label("Build", setCard, fonts.Body, UiTheme.Caption, UiTheme.Ink3,
                                         TextAlignmentOptions.Center, out var blrt, "");
            Anchor(blrt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(UiTheme.S16, UiTheme.S16 + UiTheme.ButtonTall + UiTheme.S6),
                   new Vector2(-UiTheme.S16, UiTheme.S16 + UiTheme.ButtonTall + UiTheme.S6 + UiTheme.Line(UiTheme.Caption)));
            setRt.gameObject.SetActive(false);

            // ---- toast
            var toast = UiKit.Panel("Toast", rt, out var toastRt, new Color(0.11f, 0.10f, 0.09f, 0.92f),
                                    UiTheme.PanelCorner);
            Anchor(toastRt, new Vector2(0.06f, 0f), new Vector2(0.94f, 0f),
                   new Vector2(0f, navH + UiTheme.S16),
                   new Vector2(0f, navH + UiTheme.S16 + UiTheme.Py(60f)));
            var toastLabel = UiKit.Label("Text", toastRt, fonts.Body, UiTheme.Caption, Color.white,
                                         TextAlignmentOptions.Center, out var tlrt, "");
            Stretch(tlrt, UiTheme.S12);
            toastLabel.textWrappingMode = TextWrappingModes.Normal;
            toastRt.gameObject.SetActive(false);

            var lobby = go.AddComponent<LobbyScreen>();
            Wire(lobby,
                 ("coinLabel", coinLabel), ("crystalLabel", crystalLabel),
                 ("sprites", library),
                 ("passBadgeLabel", passBadge), ("passSubLabel", passSub),
                 ("attendanceSubLabel", attSub), ("attendanceDot", attDot.gameObject),
                 ("idleButton", idleButton), ("idleSubLabel", idleSub), ("idleFill", idleFill),
                 ("homeResetButton", homeResetButton),
                 ("titleLabel", titleLabel),
                 ("railRoot", rail.gameObject),
                 ("units", unitsTab), ("deck", deckTab), ("gacha", gachaTab), ("shop", shopTab),
                 ("unitDetail", unitDetail), ("artViewer", artViewer),
                 ("gearBag", gearBag),
                 ("pass", passScreen), ("attendance", attendanceScreen),
                 ("coopPick", coopPickScreen),
                 ("home", homeTab), ("stage", stageTab),
                 ("attendanceButton", attendanceButton), ("passButton", passButton),
                 ("settingsButton", settingsButton),
                 ("settingsRoot", setRt.gameObject), ("settingsCloseButton", settingsClose),
                 ("tutorialReplayButton", tutorialReplay), ("resetButton", resetButton),
                 ("buildLabel", buildLabel),
                 ("bgmValueLabel", bgmLabel), ("bgmDownButton", bgmDown),
                 ("bgmUpButton", bgmUp), ("bgmMuteButton", bgmMute),
                 ("content", contentTab),
                 ("toastRoot", toastRt.gameObject), ("toastLabel", toastLabel));
            WireArray(lobby, "tabButtons", tabButtons);
            WireArray(lobby, "tabIndicators", tabIndicators);
            // Seven entries to match LobbyTab. 편성 re-uses the 가전 panel rather than leaving
            // a hole here: a null in a wired array is what SceneWiringTests exists to catch,
            // and ShowTab handles the repeated entry by hiding all panels before showing one.
            WireArray(lobby, "tabPanels", new Object[]
            {
                homePanel, stagePanel, contentPanel, unitsPanel, deckPanel, gachaPanel, shopPanel,
            });
            return lobby;
        }

        /// <summary>Home: the room, the idle card and the two run entries.</summary>
        /// <summary>
        /// 콘텐츠: the modes that sit beside the stage run. 10.16-n lists 대청소 on a dark
        /// plate with its rules, then a pale "next mode" card so the tab is not half empty.
        /// </summary>
        /// <summary>
        /// The lobby room, drawn rather than imported: the build paints it on a canvas, so
        /// there is no room art to load. Every rectangle is anchored as a fraction of the
        /// panel, which is what keeps it right on any aspect.
        /// </summary>
        static RoomView BuildRoomScene(RectTransform host, Shapes art, SpriteLibrary library,
                                       Fonts fonts, out RectTransform floorBand,
                                       out RectTransform agentRoot, out GameObject bubbleRoot,
                                       out TextMeshProUGUI bubbleLabel)
        {
            const float FloorTop = 0.735f;      // where the wall ends and the floor begins

            Image Fill(string id, RectTransform parent, float x0, float y0, float x1, float y1,
                       Color color, Sprite sprite)
            {
                var rt = UiKit.Node(id, parent);
                Anchor(rt, new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero);
                var img = rt.gameObject.AddComponent<Image>();
                if (sprite != null)
                {
                    img.sprite = sprite;
                    // The rounded panel is a 9-slice; the disc is a plain stamp.
                    img.type = sprite == UiArt.Panel ? Image.Type.Sliced : Image.Type.Simple;
                }
                img.color = color;
                img.raycastTarget = false;
                return img;
            }

            // ---- wall, its pinstripes, and the skirting board that caps it.
            Fill("Wall", host, 0f, FloorTop, 1f, 1f, Hex("#DCE4E9"), null);
            var stripes = UiKit.Node("Stripes", host);
            Anchor(stripes, new Vector2(0f, FloorTop), Vector2.one, Vector2.zero, Vector2.zero);
            for (int i = 0; i < 12; i++)
            {
                float x = 0.012f + i * 0.08335f;
                Fill("S" + i, stripes, x, 0.02f, x + 0.005f, 1f,
                     new Color(1f, 1f, 1f, 0.35f), null);
            }
            Fill("Skirting", host, 0f, FloorTop, 1f, 0.748f, Hex("#CFD9DF"), null);

            // ---- floor boards: a row line every 1/26, with the verticals offset every other
            // row so the planks stagger the way laid flooring does.
            Fill("Floor", host, 0f, 0f, 1f, FloorTop, Hex("#DCC39A"), null);
            var planks = UiKit.Node("Planks", host);
            Anchor(planks, Vector2.zero, new Vector2(1f, FloorTop), Vector2.zero, Vector2.zero);
            var grain = new Color(0.47f, 0.33f, 0.16f, 0.14f);
            for (int r = 1; r < 26; r++)      // 25 seams divide 26 boards
            {
                float y = r / 26f;
                Fill("H" + r, planks, 0f, y, 1f, y + 0.0015f, grain, null);
                float offset = r % 2 == 1 ? 0.128f : 0f;
                for (int c = 0; c < 4; c++)
                {
                    float x = offset + c * 0.256f;
                    Fill($"V{r}_{c}", planks, x, y, x + 0.001f, y + 0.0385f, grain, null);
                }
            }

            // ---- window, with a sun behind the glass and two mullions over it.
            Fill("WindowFrame", host, 0.303f, 0.771f, 0.605f, 0.888f, Hex("#F4F7F9"), UiArt.Panel);
            Fill("WindowGlass", host, 0.315f, 0.776f, 0.593f, 0.883f, Hex("#BFE0F2"), UiArt.Panel);
            Fill("Sun", host, 0.509f, 0.837f, 0.521f, 0.865f, Hex("#FFF4D6"),
                 GeneratedArt.Disc("shape_disc"));
            Fill("MullionV", host, 0.45f, 0.776f, 0.458f, 0.883f, Hex("#F4F7F9"), null);
            Fill("MullionH", host, 0.315f, 0.828f, 0.593f, 0.832f, Hex("#F4F7F9"), null);

            Fill("PictureFrame", host, 0.707f, 0.815f, 0.87f, 0.873f, Hex("#F4F7F9"), UiArt.Panel);
            Fill("Picture", host, 0.721f, 0.828f, 0.856f, 0.86f, Hex("#9ED0B8"), UiArt.Panel);

            // ---- rug. One filled ellipse: the build strokes a second, smaller one, and
            // drawing both as fills made it read as a dark blob.
            Fill("Rug", host, 0.167f, 0.383f, 0.833f, 0.595f,
                 new Color(0.357f, 0.486f, 0.6f, 0.22f), GeneratedArt.Disc("shape_disc"));

            // ---- sofa against the back wall, and a potted plant in the corner.
            Fill("SofaBack", host, 0.555f, 0.736f, 0.944f, 0.772f, Hex("#4A6884"), UiArt.Panel);
            Fill("SofaSeat", host, 0.555f, 0.714f, 0.944f, 0.746f, Hex("#5B7C99"), UiArt.Panel);
            Fill("SofaArmL", host, 0.541f, 0.71f, 0.578f, 0.751f, Hex("#3E5A74"), UiArt.Panel);
            Fill("SofaArmR", host, 0.921f, 0.71f, 0.958f, 0.751f, Hex("#3E5A74"), UiArt.Panel);

            Fill("Pot", host, 0.03f, 0.712f, 0.09f, 0.738f, Hex("#B98A57"), UiArt.Panel);
            float[] leafY = { 0.741f, 0.75f, 0.753f, 0.75f, 0.741f };
            for (int i = 0; i < leafY.Length; i++)
            {
                float x = 0.015f + i * 0.0145f;
                Fill("Leaf" + i, host, x, leafY[i], x + 0.026f, leafY[i] + 0.03f,
                     Hex("#6E9B4C"), GeneratedArt.Disc("shape_disc"));
            }

            // ---- the band the appliances actually walk in. Narrower than the floor so they
            // cluster near the middle the way the original's cast does.
            floorBand = UiKit.Node("Floor", host);
            Anchor(floorBand, new Vector2(0.120f, 0.242f), new Vector2(0.880f, 0.728f),
                   Vector2.zero, Vector2.zero);

            agentRoot = UiKit.Node("Agents", host);
            Stretch(agentRoot);

            // ---- chatter bubble, parked centre until a line is spoken.
            var bubbleRt = UiKit.Node("Bubble", host);
            bubbleRt.anchorMin = bubbleRt.anchorMax = new Vector2(0.5f, 0.5f);
            bubbleRt.sizeDelta = new Vector2(UiTheme.Px(187.5f), UiTheme.Py(38f));
            var bubbleBg = bubbleRt.gameObject.AddComponent<Image>();
            bubbleBg.sprite = UiKit.FlatPanelSprite;
            bubbleBg.type = Image.Type.Sliced;
            bubbleBg.color = UiTheme.Panel;
            bubbleBg.raycastTarget = false;
            bubbleLabel = UiKit.Label("Text", bubbleRt, fonts.Body, UiTheme.Py(14.6f), UiTheme.Ink,
                                      TextAlignmentOptions.Center, out var blRt, "");
            Stretch(blRt, UiTheme.Px(6.6f));
            bubbleRoot = bubbleRt.gameObject;
            bubbleRoot.SetActive(false);

            var view = host.gameObject.AddComponent<RoomView>();
            Wire(view, ("floor", floorBand), ("agentRoot", agentRoot), ("sprites", library),
                 ("dustSprite", art.Circle), ("bubbleRoot", bubbleRoot),
                 ("bubbleLabel", bubbleLabel));
            return view;
        }

        /// <summary>
        /// 홈: the lobby room with the appliances walking in it, the 대청소 and next-stage
        /// cards, and the idle test button. The room is drawn procedurally - the build has no
        /// room art, it paints wall, floor, window, sofa and plant on a canvas.
        /// </summary>
        static GameObject BuildHomePanel(RectTransform parent, Fonts fonts, SpriteLibrary library,
                                         Shapes art, out HomeTab tab)
        {
            var panel = UiKit.Node("Home", parent);
            Stretch(panel);

            var roomRt = UiKit.Node("Room", panel);
            Stretch(roomRt);
            var room = BuildRoomScene(roomRt, art, library, fonts, out var floorRt,
                                      out var agentRoot, out var bubbleRoot, out var bubbleLabel);

            // ---- the translucent note that floats over the floor.
            var noteRt = UiKit.Node("RoomNote", panel);
            Anchor(noteRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                   new Vector2(-UiTheme.Px(129.5f), UiTheme.Py(157f)),
                   new Vector2(UiTheme.Px(129.5f), UiTheme.Py(182f)));
            var noteBg = noteRt.gameObject.AddComponent<Image>();
            noteBg.sprite = UiArt.Panel;
            noteBg.type = Image.Type.Sliced;
            noteBg.color = new Color(UiTheme.Ink.r, UiTheme.Ink.g, UiTheme.Ink.b, 0.6f);
            noteBg.raycastTarget = false;
            // 원본 `.roomnote` 는 259.2 폭에 **11.5px** 이다. 14.1 로 넣어 두었던 탓에
            // 줄이 캡슐을 넘쳐 말줄임으로 잘렸다.
            var noteLabel = UiKit.Label("Text", noteRt, fonts.Body, UiTheme.Py(11.5f),
                                        UiTheme.DarkInk, TextAlignmentOptions.Center,
                                        out var nlRt, "");
            noteLabel.textWrappingMode = TextWrappingModes.NoWrap;
            // No vertical padding: a rect shorter than the line height makes TMP drop the
            // whole line, which is how this capsule once came out blank.
            Anchor(nlRt, Vector2.zero, Vector2.one,
                   new Vector2(UiTheme.Px(8.8f), 0f), new Vector2(-UiTheme.Px(8.8f), 0f));

            // ---- 대청소 card.
            var survivor = UiKit.Button("Survivor", panel, fonts.Body, "", UiButtonKind.DarkSolid,
                                        out var svRt, UiTheme.Py(57f), UiTheme.Py(14.6f));
            Anchor(svRt, Vector2.zero, new Vector2(1f, 0f),
                   new Vector2(UiTheme.Px(16f), UiTheme.Py(92f)),
                   new Vector2(-UiTheme.Px(16f), UiTheme.Py(149f)));

            var svIconRt = UiKit.Node("Icon", svRt);
            Anchor(svIconRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                   new Vector2(UiTheme.Px(17.6f), -UiTheme.Py(18.2f)),
                   new Vector2(UiTheme.Px(47.4f), UiTheme.Py(18.2f)));
            var svIcon = svIconRt.gameObject.AddComponent<Image>();
            svIcon.sprite = LoadIcon("survival");
            svIcon.enabled = svIcon.sprite != null;
            svIcon.preserveAspect = true;
            svIcon.raycastTarget = false;

            UiKit.Label("Kicker", svRt, fonts.Body, UiTheme.Py(13.4f), UiTheme.DarkInk2,
                        TextAlignmentOptions.Left, out var svkRt, "대청소 모드");
            Anchor(svkRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(60.6f), -UiTheme.Py(31.2f)),
                   new Vector2(-UiTheme.Px(37.5f), -UiTheme.Py(10.8f)));
            var svTitle = UiKit.Label("Title", svRt, fonts.Title, UiTheme.Py(21.5f), UiTheme.DarkInk,
                                      TextAlignmentOptions.Left, out var svtRt, "리더를 골라 출동");
            svTitle.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(svtRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(60.6f), -UiTheme.Py(63.7f)),
                   new Vector2(-UiTheme.Px(37.5f), -UiTheme.Py(31.2f)));
            UiKit.Label("Arrow", svRt, fonts.Body, UiTheme.Py(26.9f), UiTheme.DarkInk,
                        TextAlignmentOptions.Right, out var svaRt, "▶");
            // 글상자는 글자(26.9pt)가 들어갈 만큼 넓혀 둔다. 원본에서는 24.3px 자리에
            // 글자가 넘쳐 그려지지만 TMP 는 상자가 모자라면 **아무것도 안 그린다.**
            // 오른쪽 정렬이라 넓혀도 화면에 보이는 자리는 그대로다.
            Anchor(svaRt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                   new Vector2(-UiTheme.Px(13.2f) - UiTheme.Py(32f), -UiTheme.Py(20.4f)),
                   new Vector2(-UiTheme.Px(13.2f), UiTheme.Py(20.4f)));

            // ---- next-stage CTA.
            var next = UiKit.Button("NextStage", panel, fonts.Body, "", UiButtonKind.Primary,
                                    out var nxRt, UiTheme.Py(74f), UiTheme.Py(14.6f));
            Anchor(nxRt, Vector2.zero, new Vector2(1f, 0f),
                   new Vector2(UiTheme.Px(16f), UiTheme.Py(10f)),
                   new Vector2(-UiTheme.Px(16f), UiTheme.Py(84f)));
            UiKit.Label("Kicker", nxRt, fonts.Body, UiTheme.Py(13.4f), UiTheme.Ink,
                        TextAlignmentOptions.Left, out var nxkRt, "다음 스테이지 도전");
            Anchor(nxkRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(17.6f), -UiTheme.Py(36.5f)),
                   new Vector2(-UiTheme.Px(37.5f), -UiTheme.Py(16.1f)));
            var nextChip = UiKit.Label("Stage", nxRt, fonts.Title, UiTheme.Py(26.9f), UiTheme.Ink,
                                       TextAlignmentOptions.Left, out var nxsRt, "1-1");
            Anchor(nxsRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(17.6f), -UiTheme.Py(77.3f)),
                   new Vector2(-UiTheme.Px(37.5f), -UiTheme.Py(36.5f)));
            UiKit.Label("Arrow", nxRt, fonts.Body, UiTheme.Py(26.9f), UiTheme.Ink,
                        TextAlignmentOptions.Right, out var nxaRt, "▶");
            Anchor(nxaRt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                   new Vector2(-UiTheme.Px(13.2f) - UiTheme.Py(32f), -UiTheme.Py(20.4f)),
                   new Vector2(-UiTheme.Px(13.2f), UiTheme.Py(20.4f)));

            // ---- development-only idle skip, kept out of the way at the top.
            var testHour = UiKit.Button("TestHour", panel, fonts.Body, "+1h",
                                        UiButtonKind.Secondary, out var thRt,
                                        UiTheme.Py(37.6f), UiTheme.Py(14.6f));
            Anchor(thRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                   new Vector2(-UiTheme.Px(28.7f), -UiTheme.Py(142.5f)),
                   new Vector2(UiTheme.Px(28.7f), -UiTheme.Py(104.9f)));

            tab = panel.gameObject.AddComponent<HomeTab>();
            Wire(tab, ("room", room), ("idleTestButton", testHour),
                 ("survivorButton", survivor), ("survivorTitleLabel", svTitle),
                 ("nextStageButton", next), ("nextStageChip", nextChip),
                 ("roomNoteLabel", noteLabel));
            return panel.gameObject;
        }

        /// <summary>
        /// 스테이지, measured off the running build: a scrolling column of fixed blocks -
        /// heading, the dark progress plate, the 전력 스킬 card, the sortie strip, the area
        /// tabs, the area card, then the chapter list.
        ///
        /// childControlHeight must stay on: with it off the LayoutElement heights below are
        /// ignored and every row draws over the whole tab.
        /// </summary>
        static GameObject BuildStagePanel(RectTransform parent, Fonts fonts, Prefabs prefabs,
                                          SpriteLibrary library, out StageTab tab)
        {
            var panel = UiKit.Node("Stage", parent);
            Stretch(panel);
            float side = UiTheme.Px(14f);

            var viewport = UiKit.Node("Viewport", panel);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            // 뷰포트에 그래픽이 없으면 빈 자리 드래그와 휠이 ScrollRect 까지 닿지 않는다.
            UiKit.ScrollCatcher(viewport);
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            var column = UiKit.Node("Column", viewport);
            Anchor(column, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            column.pivot = new Vector2(0.5f, 1f);
            var col = column.gameObject.AddComponent<VerticalLayoutGroup>();
            col.spacing = UiTheme.Py(12f);
            col.padding = new RectOffset((int)side, (int)side, (int)UiTheme.Py(14f), (int)UiTheme.Py(20f));
            col.childControlWidth = true;
            col.childControlHeight = true;
            col.childForceExpandWidth = true;
            col.childForceExpandHeight = false;
            var colFit = column.gameObject.AddComponent<ContentSizeFitter>();
            colFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = column;

            RectTransform Block(string id, float pageHeight)
            {
                var rt = UiKit.Node(id, column);
                var le = rt.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = le.minHeight = UiTheme.Py(pageHeight);
                return rt;
            }

            // ---- heading
            var head = Block("Head", 30f);
            UiKit.Label("Title", head, fonts.Title, UiTheme.Py(24f), UiTheme.Ink,
                        TextAlignmentOptions.BottomLeft, out var htRt, "우리 집 지키기");
            Anchor(htRt, Vector2.zero, new Vector2(0f, 1f), Vector2.zero,
                   new Vector2(UiTheme.Px(140f), 0f));
            var headCount = UiKit.Label("Count", head, fonts.Body, UiTheme.Py(12f), UiTheme.Ink2,
                                        TextAlignmentOptions.BottomLeft, out var hcRt, "");
            Anchor(hcRt, Vector2.zero, Vector2.one,
                   new Vector2(UiTheme.Px(146f), 0f), new Vector2(0f, -UiTheme.Py(3f)));

            // ---- dark progress plate
            var progress = Block("Progress", 68f);
            var plate = UiKit.Node("Plate", progress);
            Stretch(plate);
            var plateBg = plate.gameObject.AddComponent<Image>();
            plateBg.sprite = UiKit.FlatPanelSprite;
            plateBg.type = Image.Type.Sliced;
            plateBg.color = UiTheme.DarkPanel;
            plateBg.raycastTarget = false;

            UiKit.Label("Caption", progress, fonts.Body, UiTheme.Py(10f), UiTheme.DarkInk2,
                        TextAlignmentOptions.Left, out var pcRt, "최고 스테이지");
            Anchor(pcRt, new Vector2(0f, 1f), new Vector2(0f, 1f),
                   new Vector2(UiTheme.Px(14f), -UiTheme.Py(27f)),
                   new Vector2(UiTheme.Px(100f), -UiTheme.Py(12f)));
            var bestStage = UiKit.Label("Best", progress, fonts.Number, UiTheme.Py(26f),
                                        UiTheme.Primary, TextAlignmentOptions.Left,
                                        out var bsRt, "—");
            Anchor(bsRt, new Vector2(0f, 1f), new Vector2(0f, 1f),
                   new Vector2(UiTheme.Px(14f), -UiTheme.Py(69.3f)),
                   new Vector2(UiTheme.Px(100f), -UiTheme.Py(30f)));
            var nextReward = UiKit.Label("Next", progress, fonts.Body, UiTheme.Py(12.5f),
                                         UiTheme.DarkInk, TextAlignmentOptions.Left,
                                         out var nrRt, "");
            nextReward.textWrappingMode = TextWrappingModes.Normal;
            Anchor(nrRt, Vector2.zero, Vector2.one,
                   new Vector2(UiTheme.Px(104f), UiTheme.Py(10f)),
                   new Vector2(-UiTheme.Px(14f), -UiTheme.Py(10f)));

            // ---- 전력 스킬 card
            var skills = Block("PowerSkills", 171f);
            var skillCard = UiKit.Node("Card", skills);
            Stretch(skillCard);
            var skillBg = skillCard.gameObject.AddComponent<Image>();
            skillBg.sprite = UiKit.FlatPanelSprite;
            skillBg.type = Image.Type.Sliced;
            skillBg.color = UiTheme.Panel;
            skillBg.raycastTarget = false;

            UiKit.Label("Title", skills, fonts.Title, UiTheme.Py(15f), UiTheme.Ink,
                        TextAlignmentOptions.Left, out var skRt, "전력 스킬");
            Anchor(skRt, new Vector2(0f, 1f), new Vector2(0f, 1f),
                   new Vector2(UiTheme.Px(14f), -UiTheme.Py(33.8f)),
                   new Vector2(UiTheme.Px(90f), -UiTheme.Py(11f)));
            UiKit.Label("Sub", skills, fonts.Body, UiTheme.Py(12f), UiTheme.Ink2,
                        TextAlignmentOptions.Left, out var ssRt,
                        "전투 중 남는 전력으로 쓰는 기술");
            Anchor(ssRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(92f), -UiTheme.Py(30.4f)),
                   new Vector2(-UiTheme.Px(14f), -UiTheme.Py(12f)));

            var skillRows = UiKit.Node("Rows", skills);
            Anchor(skillRows, Vector2.zero, Vector2.one,
                   new Vector2(UiTheme.Px(14f), UiTheme.Py(10f)),
                   new Vector2(-UiTheme.Px(14f), -UiTheme.Py(43f)));
            var skillCol = skillRows.gameObject.AddComponent<VerticalLayoutGroup>();
            skillCol.spacing = UiTheme.Py(6f);
            skillCol.childControlWidth = true;
            skillCol.childControlHeight = true;
            skillCol.childForceExpandWidth = true;
            skillCol.childForceExpandHeight = false;

            // ---- sortie strip
            var sortie = Block("Sortie", 56f);
            var sortieCard = UiKit.Node("Card", sortie);
            Stretch(sortieCard);
            var sortieBg = sortieCard.gameObject.AddComponent<Image>();
            sortieBg.sprite = UiKit.FlatPanelSprite;
            sortieBg.type = Image.Type.Sliced;
            sortieBg.color = UiTheme.Panel;
            sortieBg.raycastTarget = false;

            UiKit.Label("Label", sortie, fonts.Body, UiTheme.Py(11f), UiTheme.Ink2,
                        TextAlignmentOptions.Left, out var slRt, "출전 편성");
            Anchor(slRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                   new Vector2(UiTheme.Px(14f), -UiTheme.Py(8.3f)),
                   new Vector2(UiTheme.Px(68f), UiTheme.Py(8.3f)));

            var minis = UiKit.Node("Minis", sortie);
            Anchor(minis, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                   new Vector2(UiTheme.Px(68f), -UiTheme.Py(17f)),
                   new Vector2(-UiTheme.Px(58f), UiTheme.Py(17f)));
            var miniRow = UiKit.Row(minis, UiTheme.Px(4f), TextAnchor.MiddleLeft);
            miniRow.childControlWidth = false;
            miniRow.childControlHeight = false;
            miniRow.childForceExpandWidth = false;

            var change = UiKit.Button("Change", sortie, fonts.Body, "변경",
                                      UiButtonKind.TextDanger, out var chRt,
                                      UiTheme.Py(26f), UiTheme.Py(12f));
            Anchor(chRt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                   new Vector2(-UiTheme.Px(52f), -UiTheme.Py(13f)),
                   new Vector2(-UiTheme.Px(8f), UiTheme.Py(13f)));

            // ---- area tabs
            // 원본 #areaTabs 는 398x55 이고 칩이 76x47 이다. 칩이 세 줄이라 47 이 필요하다.
            var areaTabs = Block("AreaTabs", 55f);
            // 원본 칩 자리: 16 / 96.6 / 177.2 … 간격 5.
            var areaRow = UiKit.Row(areaTabs, UiTheme.Px(5f), TextAnchor.MiddleLeft);
            areaRow.childControlWidth = false;
            areaRow.childControlHeight = false;
            areaRow.childForceExpandWidth = false;

            // ---- area card
            var areaCard = Block("AreaCard", 212f);
            var areaBg = UiKit.Node("Card", areaCard);
            Stretch(areaBg);
            var areaBgImg = areaBg.gameObject.AddComponent<Image>();
            areaBgImg.sprite = UiKit.FlatPanelSprite;
            areaBgImg.type = Image.Type.Sliced;
            areaBgImg.color = Hex("#FFF1E3");
            areaBgImg.raycastTarget = false;

            var areaArtRt = UiKit.Node("Art", areaCard);
            Anchor(areaArtRt, Vector2.zero, new Vector2(0f, 1f),
                   new Vector2(UiTheme.Sq(11f), UiTheme.Sq(11f)),
                   new Vector2(UiTheme.Px(169f), -UiTheme.Py(11f)));
            var areaArt = areaArtRt.gameObject.AddComponent<Image>();
            areaArt.sprite = UiKit.FlatPanelSprite;
            areaArt.type = Image.Type.Sliced;
            areaArt.color = UiTheme.Panel;
            areaArt.raycastTarget = false;

            float textLeft = UiTheme.Px(181f), textRight = -UiTheme.Px(11f);
            var areaKicker = UiKit.Label("Kicker", areaCard, fonts.Body, UiTheme.Py(12f),
                                         UiTheme.RaritySSR, TextAlignmentOptions.Left,
                                         out var akRt, "");
            Anchor(akRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(textLeft, -UiTheme.Py(57.4f)), new Vector2(textRight, -UiTheme.Py(39f)));
            var areaTitle = UiKit.Label("Title", areaCard, fonts.Title, UiTheme.Py(22f),
                                        UiTheme.Ink, TextAlignmentOptions.Left, out var atRt, "");
            areaTitle.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(atRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(textLeft, -UiTheme.Py(91f)), new Vector2(textRight, -UiTheme.Py(58f)));
            var areaSub = UiKit.Label("Sub", areaCard, fonts.Body, UiTheme.Py(12f), UiTheme.Ink2,
                                      TextAlignmentOptions.Left, out var asRt, "");
            Anchor(asRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(textLeft, -UiTheme.Py(103.4f)), new Vector2(textRight, -UiTheme.Py(85f)));
            var areaDesc = UiKit.Label("Desc", areaCard, fonts.Body, UiTheme.Py(12.5f),
                                       UiTheme.Ink, TextAlignmentOptions.TopLeft, out var adRt, "");
            areaDesc.textWrappingMode = TextWrappingModes.Normal;
            Anchor(adRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(textLeft, -UiTheme.Py(154f)), new Vector2(textRight, -UiTheme.Py(110f)));
            var areaStat = UiKit.Label("Stat", areaCard, fonts.Body, UiTheme.Py(12f), UiTheme.Ink2,
                                       TextAlignmentOptions.Left, out var astRt, "");
            Anchor(astRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(textLeft, -UiTheme.Py(173.4f)), new Vector2(textRight, -UiTheme.Py(155f)));

            // ---- chapters
            var chapters = UiKit.Node("Chapters", column);
            var chapterCol = chapters.gameObject.AddComponent<VerticalLayoutGroup>();
            chapterCol.spacing = UiTheme.Py(8f);
            chapterCol.childControlWidth = true;
            chapterCol.childControlHeight = true;
            chapterCol.childForceExpandWidth = true;
            chapterCol.childForceExpandHeight = false;
            var chapterFit = chapters.gameObject.AddComponent<ContentSizeFitter>();
            chapterFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            tab = panel.gameObject.AddComponent<StageTab>();
            Wire(tab, ("sprites", library),
                 ("headCountLabel", headCount), ("bestStageLabel", bestStage),
                 ("nextRewardLabel", nextReward), ("powerSkillRoot", skillRows),
                 ("deckMiniRoot", minis), ("deckChangeButton", change),
                 ("areaTabRoot", areaTabs), ("areaChipPrefab", prefabs.DarkChip),
                 ("areaKickerLabel", areaKicker), ("areaTitleLabel", areaTitle),
                 ("areaSubLabel", areaSub), ("areaDescLabel", areaDesc),
                 ("areaStatLabel", areaStat), ("areaArtImage", areaArt),
                 ("listRoot", chapters), ("rowPrefab", prefabs.StageRow),
                 ("miniSprite", UiKit.FlatPanelSprite),
                 ("bodyFont", fonts.Body), ("titleFont", fonts.Title));

            // 전력 스킬 아이콘. 표의 id 와 같은 순서로 넣는다 — StageTab 이 id 로 고른다.
            WireArray(tab, "powerSkillIcons", new Object[] { LoadIcon("ps_storm"), LoadIcon("ps_boost") });
            WireStrings(tab, "powerSkillIconIds", new[] { "storm", "boost" });

            // 구역 다섯 곳의 집 단면도를 한 번 구워 둔다.
            WireArray(tab, "houseArt", new Object[]
            {
                GeneratedArt.HouseCutaway(1), GeneratedArt.HouseCutaway(2),
                GeneratedArt.HouseCutaway(3), GeneratedArt.HouseCutaway(4),
                GeneratedArt.HouseCutaway(5),
            });
            return panel.gameObject;
        }

        /// <summary>A literal colour, for the few places a one-off shade is clearer inline.</summary>
        static Color Hex(string rgb)
            => ColorUtility.TryParseHtmlString(rgb, out var c) ? c : Color.magenta;

        /// <summary>
        /// A toggle pill with an explicit palette. UiButton bakes its faces, which cannot
        /// express the two selectable-pill looks the redesign uses, so the colours are
        /// passed in and UiToggleChip paints from them.
        /// </summary>
        static UiToggleChip BuildChip(string id, RectTransform parent, Fonts fonts, float corner,
                                      Color faceNormal, Color faceSelected, Color faceLocked,
                                      Color inkNormal, Color inkSelected, Color inkLocked,
                                      bool showLock)
        {
            var rt = UiKit.Node(id, parent);
            var face = rt.gameObject.AddComponent<Image>();
            face.sprite = GeneratedArt.RoundedNineSlice("ui_chip", Mathf.Max(2, Mathf.RoundToInt(corner)));
            face.type = Image.Type.Sliced;
            face.color = faceNormal;
            face.raycastTarget = true;

            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            button.transition = Selectable.Transition.None;

            var label = UiKit.Label("Label", rt, fonts.Body, UiTheme.Body, inkNormal,
                                    TextAlignmentOptions.Center, out var labelRt, "");
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            Stretch(labelRt, UiTheme.S4);

            // 원본 `i.lk` 는 칩 **오른쪽 위 모서리의 8x7 배지**다(75.6 폭 칩에서 x 61.6, y 5).
            // 전에는 글자 앞에 세워 두고 글자를 34 밀어 놨는데, 칩을 원본 폭(75.6)으로
            // 줄이자 남는 자리가 38 밖에 안 돼 '2구역 2층' 이 세로로 깨졌다.
            var lockRt = UiKit.Node("Lock", rt);
            Anchor(lockRt, new Vector2(1f, 1f), new Vector2(1f, 1f),
                   new Vector2(-UiTheme.Px(14f), -UiTheme.Py(12f)),
                   new Vector2(-UiTheme.Px(6f), -UiTheme.Py(5f)));
            var padlock = lockRt.gameObject.AddComponent<Image>();
            padlock.sprite = UiArt.Icon("lock");
            padlock.color = inkLocked;
            padlock.preserveAspect = true;
            padlock.raycastTarget = false;
            // 칩은 가로 레이아웃이라 자물쇠를 그냥 두면 **글자 자리를 빼앗는다.**
            // 원본처럼 모서리에 얹히도록 흐름에서 뺀다.
            lockRt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            var chip = rt.gameObject.AddComponent<UiToggleChip>();
            // The padlock is always wired and simply inactive: a null reference would read as
            // a wiring mistake, and SceneWiringTests rightly refuses one.
            lockRt.gameObject.SetActive(showLock);
            Wire(chip, ("face", face), ("label", label), ("lockIcon", lockRt.gameObject),
                 ("button", button), ("labelRect", labelRt));
            WireValues(chip,
                       ("faceNormal", faceNormal), ("faceSelected", faceSelected),
                       ("faceLocked", faceLocked), ("inkNormal", inkNormal),
                       ("inkSelected", inkSelected), ("inkLocked", inkLocked),
                       // 자물쇠가 모서리 배지라 잠겨도 글자를 밀지 않는다.
                       ("textInset", UiTheme.S8), ("lockedTextInset", UiTheme.S8));
            return chip;
        }

        /// <summary>
        /// One half of a segmented control: the active segment goes white on the sunken
        /// ground the row draws behind it.
        /// </summary>
        static UiToggleChip BuildSegment(string id, RectTransform parent, Fonts fonts, string text)
        {
            var chip = BuildChip(id, parent, fonts, UiTheme.Px(8f),
                                 new Color(0f, 0f, 0f, 0f), UiTheme.Panel, UiTheme.Panel2,
                                 UiTheme.Ink2, UiTheme.Ink, UiTheme.Ink3, false);
            chip.Bind(text, null);
            return chip;
        }

        /// <summary>
        /// The stage tab's chapter pill: the open chapter turns into a dark solid, and a
        /// locked one keeps the pale ground with a padlock ahead of its text.
        /// </summary>
        static UiToggleChip BuildChapterChipPrefab(Fonts fonts)
        {
            var chip = BuildChip("ChapterChip", null, fonts, UiTheme.Px(10f),
                                 UiTheme.Panel, UiTheme.DarkPanel, UiTheme.Panel2,
                                 UiTheme.Ink, UiTheme.DarkInk, UiTheme.Ink3, true);
            var rt = (RectTransform)chip.transform;
            rt.sizeDelta = new Vector2(UiTheme.Px(96f), UiTheme.Py(38f));

            // Shop categories and stage areas drop this prefab straight into a row that does
            // not size its children, so the chip has to hug its own text - without this it
            // collapses to a sliver and the label vanishes.
            var label = rt.Find("Label");
            if (label != null)
            {
                var lrt = (RectTransform)label;
                lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
                lrt.offsetMin = lrt.offsetMax = Vector2.zero;
                var le = label.gameObject.AddComponent<LayoutElement>();
                le.minHeight = UiTheme.Py(38f);
            }
            var layout = chip.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset((int)UiTheme.S12, (int)UiTheme.S12,
                                            (int)UiTheme.S4, (int)UiTheme.S4);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.childAlignment = TextAnchor.MiddleCenter;
            var fitter = chip.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            return SavePrefab<UiToggleChip>(chip.gameObject, "ChapterChip");
        }
        /// <summary>
        /// One level-up choice, measured off 10.16-n: a wide row - 52px picture, then tag,
        /// name and description stacked beside it - on a near-white plate with a 2px rule.
        /// Not a portrait tile: three of these stack vertically inside the arena, they do
        /// not sit side by side, and the old side-by-side row ran off the screen edge.
        /// </summary>
        static SurvivorOptionCard BuildSurvivorOptionPrefab(Fonts fonts)
        {
            var go = UI("SurvivorOptionCard", null, out var rt);
            // 92 rather than the build's 78: TMP needs more box per line than CSS does,
            // and squeezing the three rows into 78 made the tag line vanish entirely.
            rt.sizeDelta = new Vector2(UiTheme.Px(330f), UiTheme.Py(92f));

            var frame = go.AddComponent<Image>();
            frame.sprite = GeneratedArt.RoundedNineSlice("ui_card12",
                Mathf.RoundToInt(UiTheme.Px(12f)));
            frame.type = Image.Type.Sliced;
            frame.color = UiTheme.Panel;
            frame.raycastTarget = true;

            // The 2px rule is a separate ring: one sprite cannot carry fill and border in
            // two colours, and "hot" recolours only the rule.
            var edgeGo = UI("Edge", go.transform, out var ert);
            Stretch(ert);
            var edge = edgeGo.AddComponent<Image>();
            edge.sprite = GeneratedArt.RoundedOutlineNineSlice("ui_cardline12",
                Mathf.RoundToInt(UiTheme.Px(12f)), Mathf.RoundToInt(UiTheme.Px(2f)));
            edge.type = Image.Type.Sliced;
            edge.color = UiTheme.PanelLine;
            edge.raycastTarget = false;

            var button = go.AddComponent<Button>();
            button.targetGraphic = frame;
            button.transition = Selectable.Transition.None;

            // 52px picture plate on the left, 10px in.
            float picSize = UiTheme.Px(52f);
            var picRt = UiKit.Node("Pic", rt);
            Anchor(picRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                   new Vector2(UiTheme.Px(10f), -picSize * 0.5f),
                   new Vector2(UiTheme.Px(10f) + picSize, picSize * 0.5f));
            var picBg = picRt.gameObject.AddComponent<Image>();
            picBg.sprite = GeneratedArt.RoundedNineSlice("ui_slot10",
                Mathf.RoundToInt(UiTheme.Px(10f)));
            picBg.type = Image.Type.Sliced;
            picBg.color = UiTheme.PageBg;
            picBg.raycastTarget = false;

            var iconRt = UiKit.Node("Icon", picRt);
            Stretch(iconRt);
            var icon = iconRt.gameObject.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            // A passive has no portrait, so it shows a glyph in the display face instead.
            var glyph = UiKit.Label("Glyph", picRt, fonts.Title, UiTheme.Px(26f), UiTheme.Led,
                                    TextAlignmentOptions.Center, out var glyphRt, "");
            Stretch(glyphRt);

            // The second icon hangs off the bottom-right corner, as the build's .i2 does.
            var icon2Rt = UiKit.Node("Icon2", picRt);
            Anchor(icon2Rt, new Vector2(1f, 0f), new Vector2(1f, 0f),
                   new Vector2(-UiTheme.Px(16f), -UiTheme.Px(6f)),
                   new Vector2(UiTheme.Px(6f), UiTheme.Px(16f)));
            var icon2 = icon2Rt.gameObject.AddComponent<Image>();
            icon2.preserveAspect = true;
            icon2.raycastTarget = false;
            icon2Rt.gameObject.SetActive(false);

            // Text column: 11pt amber tag, 14pt name, 12pt description.
            //
            // Laid out by a VerticalLayoutGroup with explicit minimums rather than by three
            // rows anchored at fixed offsets. The fixed-offset version measured correctly in
            // the prefab and then came out 10% shorter once instantiated inside the level-up
            // list, which was enough for TMP to drop the tag line entirely.
            float textLeft = UiTheme.Px(10f) + picSize + UiTheme.Px(10f);
            var column = UiKit.Node("Text", rt);
            Anchor(column, new Vector2(0f, 0f), new Vector2(1f, 1f),
                   new Vector2(textLeft, UiTheme.Py(8f)),
                   new Vector2(-UiTheme.Px(10f), -UiTheme.Py(8f)));
            var columnLayout = column.gameObject.AddComponent<VerticalLayoutGroup>();
            columnLayout.spacing = 0f;
            columnLayout.childAlignment = TextAnchor.MiddleLeft;
            columnLayout.childControlWidth = true;
            columnLayout.childControlHeight = true;
            columnLayout.childForceExpandWidth = true;
            columnLayout.childForceExpandHeight = false;

            RectTransform Row(string id, float top, float height)
            {
                var r = UiKit.Node(id, column);
                var le = r.gameObject.AddComponent<LayoutElement>();
                le.minHeight = le.preferredHeight = UiTheme.Py(height);
                return r;
            }

            var tag = UiKit.Label("Tag", Row("TagRow", 0f, 24f), fonts.Body, UiTheme.Py(11f),
                                  UiTheme.Led, TextAlignmentOptions.Left, out var tagRt, "");
            tag.fontStyle = FontStyles.Bold;
            tag.textWrappingMode = TextWrappingModes.NoWrap;
            Stretch(tagRt);

            var name = UiKit.Label("Name", Row("NameRow", 0f, 26f), fonts.Title, UiTheme.Py(14f),
                                   UiTheme.Ink, TextAlignmentOptions.Left, out var nameRt, "");
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.overflowMode = TextOverflowModes.Ellipsis;
            Stretch(nameRt);

            // 원본 카드는 설명이 두 줄이면 94.8 까지 키운다(한 줄짜리는 77.4). 34 로는
            // 두 번째 줄이 잘려 '경험치 +15%(레' 에서 끊겼다.
            var desc = UiKit.Label("Desc", Row("DescRow", 0f, 44f), fonts.Body, UiTheme.Py(12f),
                                   UiTheme.Ink2, TextAlignmentOptions.TopLeft, out var descRt, "");
            desc.textWrappingMode = TextWrappingModes.Normal;
            desc.overflowMode = TextOverflowModes.Truncate;
            desc.lineSpacing = 8f;
            Stretch(descRt);

            var card = go.AddComponent<SurvivorOptionCard>();
            Wire(card,
                 ("frame", edge), ("icon", icon), ("icon2", icon2), ("glyphLabel", glyph),
                 ("tagLabel", tag), ("nameLabel", name), ("descLabel", desc),
                 ("button", button));
            return SavePrefab<SurvivorOptionCard>(go, "SurvivorOptionCard");
        }


        /// <summary>
        /// 콘텐츠, measured off the running build: a dark 대청소 card (358x413 on the page,
        /// 16px radius, 14 padding) holding the kicker, title, leader portraits, rules, three
        /// stat cells, the time-bracket strip and the CTA - then a pale 준비 중 card.
        /// </summary>
        static GameObject BuildContentPanel(RectTransform parent, Fonts fonts,
                                            SpriteLibrary library, out ContentTab tab)
        {
            var panel = UiKit.Node("Content", parent);
            Stretch(panel);
            float side = UiTheme.Px(16f);

            var viewport = UiKit.Node("Viewport", panel);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            // 뷰포트에 그래픽이 없으면 빈 자리 드래그와 휠이 ScrollRect 까지 닿지 않는다.
            UiKit.ScrollCatcher(viewport);
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            var column = UiKit.Node("Column", viewport);
            Anchor(column, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            column.pivot = new Vector2(0.5f, 1f);
            var col = column.gameObject.AddComponent<VerticalLayoutGroup>();
            col.spacing = UiTheme.Py(12f);
            col.padding = new RectOffset((int)side, (int)side, (int)UiTheme.Py(14f), (int)UiTheme.Py(20f));
            col.childControlWidth = true;
            col.childControlHeight = true;
            col.childForceExpandWidth = true;
            col.childForceExpandHeight = false;
            var colFit = column.gameObject.AddComponent<ContentSizeFitter>();
            colFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = column;

            RectTransform Block(RectTransform host, string id, float pageHeight)
            {
                var rt = UiKit.Node(id, host);
                var le = rt.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = le.minHeight = UiTheme.Py(pageHeight);
                return rt;
            }

            // ---- heading
            var head = Block(column, "Head", 36f);
            UiKit.Label("Title", head, fonts.Title, UiTheme.Py(24f), UiTheme.Ink,
                        TextAlignmentOptions.Left, out var htRt, "콘텐츠");
            Anchor(htRt, Vector2.zero, new Vector2(0f, 1f), Vector2.zero,
                   new Vector2(UiTheme.Px(70f), 0f));
            UiKit.Label("Sub", head, fonts.Body, UiTheme.Py(12f), UiTheme.Ink2,
                        TextAlignmentOptions.Left, out var hsRt, "스테이지와 따로 즐기는 모드");
            Anchor(hsRt, Vector2.zero, new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(73f), 0f), new Vector2(0f, -UiTheme.Py(4f)));

            // ---- the dark 대청소 card. 원본 `#modeSurv` 은 다른 모드 카드와 같은 틀이고
            // 높이는 336.7 이다 — 위 14 / 47.6, 소개 71.6 / 20.9, 규칙 102.5 / 42,
            // 통계 154.5 / 55, 보충 두 줄 219.5 · 248.1 / 18.6, CTA 276.7 / 46.
            var card = Block(column, "SurvivorCard", 336.7f);
            var cardBg = card.gameObject.AddComponent<Image>();
            cardBg.sprite = GeneratedArt.RoundedNineSlice("ui_modal", Mathf.RoundToInt(UiTheme.Px(16f)));
            cardBg.type = Image.Type.Sliced;
            cardBg.color = UiTheme.DarkPanel;

            float inset = UiTheme.Px(14f);
            RectTransform CardRow(string id, float top, float height)
            {
                var rt = UiKit.Node(id, card);
                Anchor(rt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(inset, -UiTheme.Py(top + height)),
                       new Vector2(-inset, -UiTheme.Py(top)));
                return rt;
            }

            // top row: icon, title block, leaders
            var top = CardRow("Top", 14f, 47.6f);
            var modeIconRt = UiKit.Node("Icon", top);
            Anchor(modeIconRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                   new Vector2(0f, -UiTheme.Py(22f)), new Vector2(UiTheme.Px(44f), UiTheme.Py(22f)));
            var modeIcon = modeIconRt.gameObject.AddComponent<Image>();
            modeIcon.sprite = LoadIcon("survival");
            modeIcon.enabled = modeIcon.sprite != null;
            modeIcon.preserveAspect = true;
            modeIcon.raycastTarget = false;

            var kicker = UiKit.Label("Kicker", top, fonts.Body, UiTheme.Py(11f), UiTheme.Primary,
                                     TextAlignmentOptions.TopLeft, out var kRt, "");
            Anchor(kRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(54f), -UiTheme.Py(17f)), Vector2.zero);
            var title = UiKit.Label("Title", top, fonts.Title, UiTheme.Py(26f), UiTheme.DarkInk,
                                    TextAlignmentOptions.TopLeft, out var tRt, "");
            title.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(tRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(54f), -UiTheme.Py(48f)), new Vector2(0f, -UiTheme.Py(17f)));

            var leaderRow = UiKit.Node("Leaders", top);
            Anchor(leaderRow, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                   new Vector2(-UiTheme.Px(132f), -UiTheme.Py(20f)),
                   new Vector2(0f, UiTheme.Py(20f)));
            var leaderLayout = UiKit.Row(leaderRow, UiTheme.Px(6f), TextAnchor.MiddleRight);
            leaderLayout.childControlWidth = false;
            leaderLayout.childControlHeight = false;
            leaderLayout.childForceExpandWidth = false;

            var lead = UiKit.Label("Lead", CardRow("Lead", 71.6f, 20.9f), fonts.Body,
                                   UiTheme.Py(13.5f), UiTheme.DarkInk,
                                   TextAlignmentOptions.TopLeft, out var ldRt, "");
            lead.textWrappingMode = TextWrappingModes.Normal;
            Stretch(ldRt);

            var rules = CardRow("Rules", 102.5f, 42f);
            var ruleLabels = new Object[2];
            for (int i = 0; i < 2; i++)
            {
                var r = UiKit.Label("Rule" + i, rules, fonts.Body, UiTheme.Py(12.5f),
                                    UiTheme.DarkInk2, TextAlignmentOptions.TopLeft,
                                    out var rRt, "");
                r.textWrappingMode = TextWrappingModes.NoWrap;
                r.overflowMode = TextOverflowModes.Ellipsis;
                Anchor(rRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(0f, -UiTheme.Py(19f * (i + 1) + 4f * i)),
                       new Vector2(0f, -UiTheme.Py(23f * i)));
                ruleLabels[i] = r;
            }

            // three stat cells
            var stats = CardRow("Stats", 154.5f, 55f);
            var statGrid = stats.gameObject.AddComponent<GridLayoutGroup>();
            statGrid.cellSize = new Vector2(UiTheme.Px(104.7f), UiTheme.Py(55f));
            statGrid.spacing = new Vector2(UiTheme.Px(8f), 0f);
            statGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            statGrid.constraintCount = 3;

            var statKeys = new Object[ContentTab.StatCount];
            var statValues = new Object[ContentTab.StatCount];
            for (int i = 0; i < ContentTab.StatCount; i++)
            {
                var cell = UiKit.Node("Stat" + i, stats);
                var cellBg = cell.gameObject.AddComponent<Image>();
                cellBg.sprite = GeneratedArt.RoundedNineSlice("ui_slot", Mathf.RoundToInt(UiTheme.Px(10f)));
                cellBg.type = Image.Type.Sliced;
                cellBg.color = UiTheme.DarkPanel2;
                cellBg.raycastTarget = false;

                var k = UiKit.Label("Key", cell, fonts.Body, UiTheme.Py(11f), UiTheme.DarkInk2,
                                    TextAlignmentOptions.TopLeft, out var kkRt, "");
                k.textWrappingMode = TextWrappingModes.NoWrap;
                Anchor(kkRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(UiTheme.Px(10f), -UiTheme.Py(25f)),
                       new Vector2(-UiTheme.Px(10f), -UiTheme.Py(8f)));
                statKeys[i] = k;

                var v = UiKit.Label("Value", cell, fonts.Number, UiTheme.Py(18f), UiTheme.Primary,
                                    TextAlignmentOptions.TopLeft, out var vvRt, "");
                v.textWrappingMode = TextWrappingModes.NoWrap;
                // 29pt of rect for an 18pt line: a 25pt box is one pixel short and TMP
                // drops the whole number rather than clipping it.
                Anchor(vvRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(UiTheme.Px(10f), -UiTheme.Py(53f)),
                       new Vector2(-UiTheme.Px(10f), -UiTheme.Py(24f)));
                statValues[i] = v;
            }

            // 원본 `.mc-sub` 두 줄. 예전에는 여기에 시간 구간 보상 띠가 있었는데 원본에
            // 없는 것이고, 채우는 코드도 불려 본 적이 없어 74px 빈 자리로만 남아 있었다.
            var rewardLabel = UiKit.Label("Sub", CardRow("Sub", 219.5f, 18.6f), fonts.Body,
                                          UiTheme.Py(12f), UiTheme.DarkInk2,
                                          TextAlignmentOptions.Left, out var mlRt, "");
            rewardLabel.textWrappingMode = TextWrappingModes.NoWrap;
            rewardLabel.overflowMode = TextOverflowModes.Ellipsis;
            Stretch(mlRt);

            var runsLabel = UiKit.Label("Sub2", CardRow("Sub2", 248.1f, 18.6f), fonts.Body,
                                        UiTheme.Py(12f), UiTheme.DarkInk2,
                                        TextAlignmentOptions.Left, out var ruRt, "");
            runsLabel.textWrappingMode = TextWrappingModes.NoWrap;
            runsLabel.overflowMode = TextOverflowModes.Ellipsis;
            Stretch(ruRt);

            var goRow = CardRow("Go", 276.7f, 46f);
            var goButton = UiKit.Button("Go", goRow, fonts.Body, "", UiButtonKind.DarkSolid,
                                        out var goRt, UiTheme.Py(46f), UiTheme.Py(15f));
            Stretch(goRt);
            var goLabel = goRt.GetComponentInChildren<TextMeshProUGUI>();

            // ---- 오늘의 청소 일지. 원본 `#modePool` — 콘텐츠 탭 **맨 위** 카드다.
            // 전에는 이 내용을 대청소 카드 안에 작은 띄로 넣었는데,
            // 일지는 대청소·협동·리그가 **같이 채우는** 것이라 모드보다 위에 온다.
            var poolCard = Block(column, "Pool", 209f);
            // 원본은 일지 카드가 맨 위다. 코드 순서상 대청소 뒤에 만들어지므로
            // 형제 순서를 직접 올린다 — 세로 배치는 자식 순서를 그대로 따른다.
            poolCard.SetSiblingIndex(head.GetSiblingIndex() + 1);
            var poolBg = poolCard.gameObject.AddComponent<Image>();
            poolBg.sprite = GeneratedArt.RoundedNineSlice("ui_modal", Mathf.RoundToInt(UiTheme.Px(16f)));
            poolBg.type = Image.Type.Sliced;
            poolBg.color = UiTheme.Panel;

            UiKit.Label("Kicker", poolCard, fonts.Body, UiTheme.Py(11f), UiTheme.Primary,
                        TextAlignmentOptions.TopLeft, out var pkRt, "선택형 일일 숙제");
            Anchor(pkRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(inset, -UiTheme.Py(30f)), new Vector2(-inset, -UiTheme.Py(13f)));

            UiKit.Label("Title", poolCard, fonts.Title, UiTheme.Py(20f), UiTheme.Ink,
                        TextAlignmentOptions.TopLeft, out var ptRt, "오늘의 청소 일지");
            Anchor(ptRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(inset, -UiTheme.Py(53f)), new Vector2(-inset, -UiTheme.Py(31f)));

            var poolPts = UiKit.Label("Pts", poolCard, fonts.Number, UiTheme.Py(17f), UiTheme.Ink,
                                      TextAlignmentOptions.Right, out var ppRt, "0/100점");
            poolPts.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(ppRt, new Vector2(1f, 1f), new Vector2(1f, 1f),
                   new Vector2(-inset - UiTheme.Px(120f), -UiTheme.Py(55f)),
                   new Vector2(-inset, -UiTheme.Py(28f)));

            // 진행바 — 원본 .pc-bar 368x10, 눈금 세 개
            var poolBarRt = UiKit.Bar("Bar", poolCard, UiTheme.Panel2, UiTheme.Primary,
                                      UiTheme.Py(10f), out var poolFill);
            Anchor(poolBarRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(inset, -UiTheme.Py(74f)), new Vector2(-inset, -UiTheme.Py(64f)));

            // 상자 세 칸
            var boxRow = UiKit.Node("Boxes", poolCard);
            Anchor(boxRow, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(inset, -UiTheme.Py(151f)), new Vector2(-inset, -UiTheme.Py(83f)));
            var boxLayout = UiKit.Row(boxRow, UiTheme.Px(6f), TextAnchor.MiddleCenter);
            boxLayout.childForceExpandWidth = true;
            boxLayout.childControlWidth = true;
            boxLayout.childForceExpandHeight = true;
            boxLayout.childControlHeight = true;

            var poolBoxes = new UiButton[ContentTab.PoolBoxCount];
            var poolBoxPts = new Object[ContentTab.PoolBoxCount];
            var poolBoxCry = new Object[ContentTab.PoolBoxCount];
            var poolBoxState = new Object[ContentTab.PoolBoxCount];
            for (int i = 0; i < ContentTab.PoolBoxCount; i++)
            {
                var b = UiKit.Button($"Box{i}", boxRow, fonts.Body, "", UiButtonKind.Secondary,
                                     out var brt, UiTheme.Py(68f), UiTheme.Py(11f));
                poolBoxes[i] = b;

                var pts = UiKit.Label("Pts", brt, fonts.Body, UiTheme.Py(11f), UiTheme.Ink2,
                                      TextAlignmentOptions.Center, out var bpRt, "");
                Anchor(bpRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(0f, -UiTheme.Py(24f)), new Vector2(0f, -UiTheme.Py(7f)));
                poolBoxPts[i] = pts;

                var cry = UiKit.Label("Crystal", brt, fonts.Number, UiTheme.Py(14f), UiTheme.Ink,
                                      TextAlignmentOptions.Center, out var bcRt, "");
                Anchor(bcRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(0f, -UiTheme.Py(45f)), new Vector2(0f, -UiTheme.Py(24f)));
                poolBoxCry[i] = cry;

                var st = UiKit.Label("State", brt, fonts.Body, UiTheme.Py(11f), UiTheme.Ink2,
                                     TextAlignmentOptions.Center, out var bsRt, "—");
                Anchor(bsRt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                       new Vector2(0f, UiTheme.Py(6f)), new Vector2(0f, UiTheme.Py(23f)));
                poolBoxState[i] = st;
            }

            var poolNote = UiKit.Label("Note", poolCard, fonts.Body, UiTheme.Py(11.5f), UiTheme.Ink2,
                                       TextAlignmentOptions.TopLeft, out var pnRt, "");
            poolNote.textWrappingMode = TextWrappingModes.Normal;
            Anchor(pnRt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(inset, UiTheme.Py(8f)), new Vector2(-inset, UiTheme.Py(50f)));

            // ---- 협동 청소. 원본 `#modeCoop` 도 같은 모드 카드이고 높이는 300.4 다
            // (보충 줄 없음, 소개 두 줄). 예전에는 132 짜리 손수 만든 카드라 규칙·통계가
            // 통째로 빠져 있었고, 아래 카드들이 그만큼 위로 올라와 있었다.
            var coopCard = BuildModeCard("Coop", library != null ? library.Unit("fridge") : null,
                                         300.4f, 47.6f, 41.8f, 55f, hasSub: false);

            // ---- 먼지 타워 · 가전 대청소 리그. 원본 `.mode-card` 를 430x932 에서 재서
            // 그대로 짰다: 안쪽 여백 14, 위 50 / 소개 41.8 / 규칙 42 / 통계 59 / 보충 18.6 / CTA 46.
            // 두 모드의 전투는 아직 없다 — 카드만 원본대로 보여 준다.
            // 줄 높이만 카드마다 다르다 — 원본에서 잰 값을 그대로 넘긴다.
            ModeCard BuildModeCard(string id, Sprite cardIcon, float cardH = 321.4f,
                                   float topH = 50f, float leadH = 41.8f, float statsH = 59f,
                                   bool hasSub = true)
            {
                var mc = Block(column, id, cardH);
                var mcHeight = mc.GetComponent<LayoutElement>();
                var mcBg = mc.gameObject.AddComponent<Image>();
                mcBg.sprite = GeneratedArt.RoundedNineSlice("ui_modal",
                    Mathf.RoundToInt(UiTheme.Px(16f)));
                mcBg.type = Image.Type.Sliced;
                mcBg.color = UiTheme.DarkPanel;

                RectTransform Row(string rowId, float rowTop, float rowHeight)
                {
                    var rt = UiKit.Node(rowId, mc);
                    Anchor(rt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                           new Vector2(inset, -UiTheme.Py(rowTop + rowHeight)),
                           new Vector2(-inset, -UiTheme.Py(rowTop)));
                    return rt;
                }

                float rowY = 14f;
                var mcTop = Row("Top", rowY, topH);
                rowY += topH + 10f;
                var artRt = UiKit.Node("Icon", mcTop);
                Anchor(artRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                       new Vector2(0f, -UiTheme.Py(22f)),
                       new Vector2(UiTheme.Px(44f), UiTheme.Py(22f)));
                var art = artRt.gameObject.AddComponent<Image>();
                art.sprite = cardIcon;
                art.enabled = art.sprite != null;
                art.preserveAspect = true;
                art.raycastTarget = false;

                var mcKicker = UiKit.Label("Kicker", mcTop, fonts.Body, UiTheme.Py(11f),
                                           UiTheme.Primary, TextAlignmentOptions.TopLeft,
                                           out var mkRt, "");
                mcKicker.textWrappingMode = TextWrappingModes.NoWrap;
                Anchor(mkRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(UiTheme.Px(54f), -UiTheme.Py(18f)), Vector2.zero);

                var mcTitle = UiKit.Label("Title", mcTop, fonts.Title, UiTheme.Py(26f),
                                          UiTheme.DarkInk, TextAlignmentOptions.TopLeft,
                                          out var mtRt, "");
                mcTitle.textWrappingMode = TextWrappingModes.NoWrap;
                Anchor(mtRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(UiTheme.Px(54f), -UiTheme.Py(50f)),
                       new Vector2(0f, -UiTheme.Py(19f)));

                var mcLeadRow = Row("Lead", rowY, leadH);
                rowY += leadH + 10f;
                var mcLead = UiKit.Label("Lead", mcLeadRow, fonts.Body,
                                         UiTheme.Py(13.5f), UiTheme.DarkInk,
                                         TextAlignmentOptions.TopLeft, out var mlRt2, "");
                mcLead.textWrappingMode = TextWrappingModes.Normal;
                Stretch(mlRt2);

                var mcRules = Row("Rules", rowY, 42f);
                rowY += 42f + 10f;
                var mcRuleLabels = new Object[ModeCard.RuleCount];
                for (int i = 0; i < ModeCard.RuleCount; i++)
                {
                    var r = UiKit.Label("Rule" + i, mcRules, fonts.Body, UiTheme.Py(12.5f),
                                        UiTheme.DarkInk2, TextAlignmentOptions.TopLeft,
                                        out var rRt2, "");
                    r.textWrappingMode = TextWrappingModes.NoWrap;
                    r.overflowMode = TextOverflowModes.Ellipsis;
                    Anchor(rRt2, new Vector2(0f, 1f), new Vector2(1f, 1f),
                           new Vector2(0f, -UiTheme.Py(19f * (i + 1) + 4f * i)),
                           new Vector2(0f, -UiTheme.Py(23f * i)));
                    mcRuleLabels[i] = r;
                }

                var mcStats = Row("Stats", rowY, statsH);
                rowY += statsH + 10f;
                var mcGrid = mcStats.gameObject.AddComponent<GridLayoutGroup>();
                mcGrid.cellSize = new Vector2(UiTheme.Px(118f), UiTheme.Py(statsH));
                mcGrid.spacing = new Vector2(UiTheme.Px(8f), 0f);
                mcGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                mcGrid.constraintCount = ModeCard.StatCount;

                var mcKeys = new Object[ModeCard.StatCount];
                var mcValues = new Object[ModeCard.StatCount];
                for (int i = 0; i < ModeCard.StatCount; i++)
                {
                    var cell = UiKit.Node("Stat" + i, mcStats);
                    var cellBg = cell.gameObject.AddComponent<Image>();
                    cellBg.sprite = GeneratedArt.RoundedNineSlice("ui_slot",
                        Mathf.RoundToInt(UiTheme.Px(10f)));
                    cellBg.type = Image.Type.Sliced;
                    cellBg.color = UiTheme.DarkPanel2;
                    cellBg.raycastTarget = false;

                    var k = UiKit.Label("Key", cell, fonts.Body, UiTheme.Py(11f), UiTheme.DarkInk2,
                                        TextAlignmentOptions.TopLeft, out var kkRt2, "");
                    k.textWrappingMode = TextWrappingModes.NoWrap;
                    k.overflowMode = TextOverflowModes.Ellipsis;
                    Anchor(kkRt2, new Vector2(0f, 1f), new Vector2(1f, 1f),
                           new Vector2(UiTheme.Px(10f), -UiTheme.Py(25f)),
                           new Vector2(-UiTheme.Px(10f), -UiTheme.Py(8f)));
                    mcKeys[i] = k;

                    var v = UiKit.Label("Value", cell, fonts.Number, UiTheme.Py(18f),
                                        UiTheme.Primary, TextAlignmentOptions.TopLeft,
                                        out var vvRt2, "");
                    v.textWrappingMode = TextWrappingModes.NoWrap;
                    Anchor(vvRt2, new Vector2(0f, 1f), new Vector2(1f, 1f),
                           new Vector2(UiTheme.Px(10f), -UiTheme.Py(56f)),
                           new Vector2(-UiTheme.Px(10f), -UiTheme.Py(27f)));
                    mcValues[i] = v;
                }

                TextMeshProUGUI mcSub = null;
                RectTransform mcSubRow = null;
                if (hasSub)
                {
                    mcSubRow = Row("Sub", rowY, 18.6f);
                    mcSub = UiKit.Label("Sub", mcSubRow, fonts.Body,
                                        UiTheme.Py(12f), UiTheme.DarkInk2,
                                        TextAlignmentOptions.Left, out var msRt, "");
                    mcSub.textWrappingMode = TextWrappingModes.NoWrap;
                    mcSub.overflowMode = TextOverflowModes.Ellipsis;
                    Stretch(msRt);
                    rowY += 18.6f + 10f;
                }

                var mcGoRow = Row("Go", rowY, 46f);
                mcHeight.minHeight = mcHeight.preferredHeight = UiTheme.Py(rowY + 46f + 14f);
                var mcGo = UiKit.Button("Go", mcGoRow, fonts.Body, "", UiButtonKind.DarkRaised,
                                        out var mgRt, UiTheme.Py(46f), UiTheme.Py(15f));
                Stretch(mgRt);
                var mcGoLabel = mgRt.GetComponentInChildren<TextMeshProUGUI>();

                var card = mc.gameObject.AddComponent<ModeCard>();
                Wire(card, ("kickerLabel", mcKicker), ("titleLabel", mcTitle),
                     ("leadLabel", mcLead), ("goButton", mcGo), ("goLabel", mcGoLabel));
                if (mcSub != null)
                    Wire(card, ("subLabel", mcSub), ("subRow", mcSubRow),
                         ("goRow", mcGoRow), ("cardHeight", mcHeight));
                WireArray(card, "ruleLabels", mcRuleLabels);
                WireArray(card, "statKeys", mcKeys);
                WireArray(card, "statValues", mcValues);
                return card;
            }

            var towerCard = BuildModeCard("Tower", library != null ? library.Unit("boiler") : null);
            var leagueCard = BuildModeCard("League", library != null ? library.Unit("vac") : null,
                                           leadH: 20.9f);

            // ---- 원본 `.mode-card.soon` — 점선 테두리의 '준비 중' 자리표시 카드.
            var soon = Block(column, "Soon", 101.1f);
            var soonBg = soon.gameObject.AddComponent<Image>();
            soonBg.sprite = GeneratedArt.RoundedNineSlice("ui_modal",
                Mathf.RoundToInt(UiTheme.Px(16f)));
            soonBg.type = Image.Type.Sliced;
            soonBg.color = UiTheme.Panel;
            var soonLineRt = UiKit.Node("Line", soon);
            Stretch(soonLineRt);
            var soonLine = soonLineRt.gameObject.AddComponent<Image>();
            soonLine.sprite = GeneratedArt.DashedRoundedOutline("ui_soon_line",
                Mathf.RoundToInt(UiTheme.ColumnWidth - UiTheme.Px(32f)),
                Mathf.RoundToInt(UiTheme.Py(101.1f)), Mathf.RoundToInt(UiTheme.Px(16f)),
                UiTheme.Px(1f), UiTheme.Px(5f), UiTheme.Px(4f));
            soonLine.color = UiTheme.PanelLine;
            soonLine.raycastTarget = false;

            UiKit.Label("Kicker", soon, fonts.Body, UiTheme.Py(11f), UiTheme.Primary,
                        TextAlignmentOptions.TopLeft, out var skRt, "다음 콘텐츠");
            Anchor(skRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(inset, -UiTheme.Py(32f)), new Vector2(-inset, -UiTheme.Py(15f)));
            UiKit.Label("Title", soon, fonts.Title, UiTheme.Py(20f), UiTheme.Ink2,
                        TextAlignmentOptions.TopLeft, out var stRt, "준비 중");
            Anchor(stRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(inset, -UiTheme.Py(58f)), new Vector2(-inset, -UiTheme.Py(34f)));
            var soonNote = UiKit.Label("Note", soon, fonts.Body, UiTheme.Py(13f), UiTheme.Ink2,
                                       TextAlignmentOptions.TopLeft, out var snRt2,
                                       "새 모드가 열리면 여기에 추가돼요.");
            soonNote.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(snRt2, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(inset, -UiTheme.Py(87f)), new Vector2(-inset, -UiTheme.Py(66f)));

            tab = panel.gameObject.AddComponent<ContentTab>();
            Wire(tab, ("kickerLabel", kicker), ("titleLabel", title), ("leaderRow", leaderRow),
                 ("leadLabel", lead), ("rewardLabel", rewardLabel),
                 ("bodyFont", fonts.Body),
                 ("runsLabel", runsLabel), ("goButton", goButton), ("goLabel", goLabel),
                 ("poolPtsLabel", poolPts), ("poolFill", poolFill.GetComponent<Image>()), ("poolNoteLabel", poolNote),
                 ("coopCard", coopCard),
                 ("towerCard", towerCard), ("leagueCard", leagueCard),
                 ("sprites", library));
            WireArray(tab, "poolBoxButtons", poolBoxes);
            WireArray(tab, "poolBoxPtsLabels", poolBoxPts);
            WireArray(tab, "poolBoxCrystalLabels", poolBoxCry);
            WireArray(tab, "poolBoxStateLabels", poolBoxState);
            WireArray(tab, "ruleLabels", ruleLabels);
            WireArray(tab, "statKeys", statKeys);
            WireArray(tab, "statValues", statValues);
            return panel.gameObject;
        }

        // ------------------------------------------------------------------ 가전 대청소 리그

        /// <summary>
        /// 리그 판을 찍는 카메라와 월드. 전투판·아레나와 같은 방식이고, 리그를 하지 않는
        /// 동안에는 꺼 둔다(켜 두면 화면 여백에 판 바닥이 비친다).
        /// </summary>
        static (Camera cam, LeagueView view) BuildLeagueWorld(Shapes art, Prefabs prefabs,
                                                              SpriteLibrary library)
        {
            var camGo = new GameObject("LeagueCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 3f;
            cam.transform.position = new Vector3(3.5f, -3f, -10f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = UiTheme.PageBg;
            cam.cullingMask = 1 << LeagueLayer;
            cam.enabled = false;

            var worldGo = new GameObject("LeagueWorld");

            var floorGo = new GameObject("Floor");
            floorGo.transform.SetParent(worldGo.transform, false);
            var floor = floorGo.AddComponent<SpriteRenderer>();
            floor.sprite = GeneratedArt.LeagueFloor(LeagueSim.Cols, LeagueSim.Rows,
                                                    LeagueSim.OurRows);
            floor.sortingOrder = -1000;

            var selGo = new GameObject("Selection");
            selGo.transform.SetParent(worldGo.transform, false);
            var sel = selGo.AddComponent<SpriteRenderer>();
            sel.sprite = GeneratedArt.RoundedOutlineNineSlice("lg_cell_sel", 8, 3);
            sel.drawMode = SpriteDrawMode.Sliced;
            sel.size = Vector2.one;
            sel.color = UiTheme.Navy;
            sel.sortingOrder = 200;
            selGo.SetActive(false);

            var entityRoot = new GameObject("Entities").transform;
            entityRoot.SetParent(worldGo.transform, false);

            var view = worldGo.AddComponent<LeagueView>();
            Wire(view, ("entityRoot", entityRoot), ("entityPrefab", prefabs.Entity),
                 ("sprites", library), ("floor", floor), ("selection", sel),
                 ("starRing", art.Circle), ("cellFill", art.Square),
                 // 먼지 타워 층 규칙 연출 — 물바닥·아지랑이·막힌 칸·반짝임·공기청정기.
                 ("waterPoolSprite", GeneratedArt.WaterPool()),
                 ("heatWaveSprite", GeneratedArt.HeatWave()),
                 ("blockedCellSprite", GeneratedArt.BlockedCell()),
                 ("sparkleSprite", GeneratedArt.Sparkle()),
                 ("purifierSprite", GeneratedArt.Purifier()));

            SetLayerDeep(worldGo, LeagueLayer);
            return (cam, view);
        }

        /// <summary>
        /// 판 한 칸에 겹치는 투명 단추. 카메라가 그린 그림은 입력을 받지 못하므로,
        /// 누를 자리는 UI 쪽에 따로 둔다.
        /// </summary>
        static UiButton BuildLeagueCellPrefab(Fonts fonts)
        {
            var go = UI("LeagueCell", null, out var rt);
            rt.sizeDelta = new Vector2(UiTheme.Px(56f), UiTheme.Py(56f));
            var b = go.AddComponent<UiButton>();
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0f);   // 보이지 않지만 눌린다
            img.raycastTarget = true;
            // background 를 **끼우지 않는다.** 끼우면 UiButton 이 제 색(주황)으로 다시 칠해
            // 판 아래 세 줄이 통째로 덮인다 — 실제로 그렇게 나왔다.
            return SavePrefab<UiButton>(go, "LeagueCell");
        }

        static LeagueBenchCell BuildLeagueBenchPrefab(Fonts fonts)
        {
            var go = UI("LeagueBenchCell", null, out var rt);
            rt.sizeDelta = new Vector2(UiTheme.Px(45.4f), UiTheme.Py(48f));

            var face = go.AddComponent<Image>();
            face.sprite = GeneratedArt.RoundedNineSlice("lg_bs", Mathf.RoundToInt(UiTheme.Px(10f)));
            face.type = Image.Type.Sliced;
            face.color = Color.white;

            var button = go.AddComponent<Button>();
            button.targetGraphic = face;
            button.transition = Selectable.Transition.None;

            var lineRt = UiKit.Node("Outline", rt);
            Stretch(lineRt);
            var outline = lineRt.gameObject.AddComponent<Image>();
            outline.type = Image.Type.Sliced;
            outline.raycastTarget = false;

            var picRt = UiKit.Node("Portrait", rt);
            picRt.anchorMin = picRt.anchorMax = new Vector2(0.5f, 0.5f);
            picRt.sizeDelta = new Vector2(UiTheme.Sq(40f), UiTheme.Sq(40f));
            var portrait = picRt.gameObject.AddComponent<Image>();
            portrait.preserveAspect = true;
            portrait.raycastTarget = false;

            var star = UiKit.Label("Star", rt, fonts.Number, UiTheme.Py(9f), UiTheme.Led,
                                   TextAlignmentOptions.TopLeft, out var starRt, "");
            star.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(starRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(3f), -UiTheme.Py(1f) - UiTheme.Line(UiTheme.Py(9f))),
                   new Vector2(0f, -UiTheme.Py(1f)));

            var trial = UiKit.Label("Trial", rt, fonts.Body, UiTheme.Py(8.5f), UiTheme.Ink2,
                                    TextAlignmentOptions.BottomRight, out var trRt, "체험");
            trial.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(trRt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(0f, UiTheme.Py(1f)),
                   new Vector2(-UiTheme.Px(2f), UiTheme.Py(1f) + UiTheme.Line(UiTheme.Py(8.5f))));

            var cell = go.AddComponent<LeagueBenchCell>();
            Wire(cell, ("face", face), ("outline", outline), ("portrait", portrait),
                 ("starLabel", star), ("trialLabel", trial), ("button", button),
                 ("dashedOutline", GeneratedArt.DashedRoundedOutline("lg_bs_dash",
                     Mathf.RoundToInt(UiTheme.Px(45.4f)), Mathf.RoundToInt(UiTheme.Py(48f)),
                     Mathf.RoundToInt(UiTheme.Px(10f)), UiTheme.Px(1.5f), UiTheme.Px(4f), UiTheme.Px(3f))),
                 ("solidOutline", GeneratedArt.RoundedOutlineNineSlice("lg_bs_line",
                     Mathf.RoundToInt(UiTheme.Px(10f)), Mathf.Max(1, Mathf.RoundToInt(UiTheme.Px(1.5f))))));
            return SavePrefab<LeagueBenchCell>(go, "LeagueBenchCell");
        }

        /// <summary>단계 한 장. 시트 안에서 세로로 쌓이므로 높이만 정하고 폭은 늘린다.</summary>
        static LeagueTierCard BuildLeagueTierPrefab(Fonts fonts)
        {
            int radius = Mathf.RoundToInt(UiTheme.Px(12f));
            var go = UI("LeagueTierCard", null, out var rt);
            rt.sizeDelta = new Vector2(UiTheme.Px(366f), UiTheme.Py(98f));
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = UiTheme.Py(98f);

            var face = go.AddComponent<Image>();
            face.sprite = GeneratedArt.RoundedNineSlice("lg_tier", radius);
            face.type = Image.Type.Sliced;
            face.color = UiTheme.Panel;

            var button = go.AddComponent<Button>();
            button.targetGraphic = face;
            button.transition = Selectable.Transition.None;

            var lineRt = UiKit.Node("Outline", rt);
            Stretch(lineRt);
            var outline = lineRt.gameObject.AddComponent<Image>();
            outline.sprite = GeneratedArt.RoundedOutlineNineSlice("lg_tier_line", radius,
                Mathf.Max(1, Mathf.RoundToInt(UiTheme.Px(1.5f))));
            outline.type = Image.Type.Sliced;
            outline.color = UiTheme.PanelLine;
            outline.raycastTarget = false;

            var name = UiKit.Label("Name", rt, fonts.Title, UiTheme.Py(17f), UiTheme.Ink,
                                   TextAlignmentOptions.Left, out var nameRt, "");
            name.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(nameRt, new Vector2(0f, 1f), new Vector2(0.6f, 1f),
                   new Vector2(UiTheme.Px(12f), -UiTheme.Py(10f) - UiTheme.Line(UiTheme.Py(17f))),
                   new Vector2(0f, -UiTheme.Py(10f)));

            var state = UiKit.Label("State", rt, fonts.Body, UiTheme.Py(12f), UiTheme.Led,
                                    TextAlignmentOptions.Right, out var stRt, "");
            state.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(stRt, new Vector2(0.6f, 1f), new Vector2(1f, 1f),
                   new Vector2(0f, -UiTheme.Py(12f) - UiTheme.Line(UiTheme.Py(12f))),
                   new Vector2(-UiTheme.Px(12f), -UiTheme.Py(12f)));

            var stat = UiKit.Label("Stat", rt, fonts.Body, UiTheme.Py(11.5f), UiTheme.Ink2,
                                   TextAlignmentOptions.TopLeft, out var statRt, "");
            stat.textWrappingMode = TextWrappingModes.NoWrap;
            stat.overflowMode = TextOverflowModes.Ellipsis;
            Anchor(statRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(12f), -UiTheme.Py(36f) - UiTheme.Line(UiTheme.Py(11.5f))),
                   new Vector2(-UiTheme.Px(12f), -UiTheme.Py(36f)));

            var reward = UiKit.Label("Reward", rt, fonts.Body, UiTheme.Py(11.5f), UiTheme.Ink2,
                                     TextAlignmentOptions.TopLeft, out var rewRt, "");
            reward.textWrappingMode = TextWrappingModes.Normal;
            Anchor(rewRt, new Vector2(0f, 0f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(12f), UiTheme.Py(8f)),
                   new Vector2(-UiTheme.Px(12f), -UiTheme.Py(54f)));

            var group = go.AddComponent<CanvasGroup>();

            var card = go.AddComponent<LeagueTierCard>();
            Wire(card, ("face", face), ("outline", outline), ("nameLabel", name),
                 ("stateLabel", state), ("statLabel", stat), ("rewardLabel", reward),
                 ("group", group), ("button", button));
            return SavePrefab<LeagueTierCard>(go, "LeagueTierCard");
        }

        static LeagueShopCard BuildLeagueShopPrefab(Fonts fonts)
        {
            int radius = Mathf.RoundToInt(UiTheme.Px(12f));
            var go = UI("LeagueShopCard", null, out var rt);
            rt.sizeDelta = new Vector2(UiTheme.Px(74.8f), UiTheme.Py(85f));

            var face = go.AddComponent<Image>();
            face.sprite = GeneratedArt.RoundedNineSlice("lg_sc", radius);
            face.type = Image.Type.Sliced;
            face.color = UiTheme.Panel;

            var button = go.AddComponent<Button>();
            button.targetGraphic = face;
            button.transition = Selectable.Transition.None;

            // 등급 물듦: 위쪽만 옅게 깔리는 띠다(원본은 62% 지점까지의 그라데이션).
            var washRt = UiKit.Node("Wash", rt);
            Anchor(washRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(0f, -UiTheme.Py(53f)), Vector2.zero);
            var wash = washRt.gameObject.AddComponent<Image>();
            wash.sprite = GeneratedArt.VerticalFade("lg_sc_wash");
            wash.raycastTarget = false;

            var lineRt = UiKit.Node("Outline", rt);
            Stretch(lineRt);
            var outline = lineRt.gameObject.AddComponent<Image>();
            outline.sprite = GeneratedArt.RoundedOutlineNineSlice("lg_sc_line", radius,
                Mathf.Max(1, Mathf.RoundToInt(UiTheme.Px(1.5f))));
            outline.type = Image.Type.Sliced;
            outline.color = UiTheme.PanelLine;
            outline.raycastTarget = false;

            var picRt = UiKit.Node("Portrait", rt);
            picRt.anchorMin = picRt.anchorMax = new Vector2(0.5f, 1f);
            picRt.pivot = new Vector2(0.5f, 1f);
            picRt.anchoredPosition = new Vector2(0f, -UiTheme.Py(5f));
            picRt.sizeDelta = new Vector2(UiTheme.Sq(40f), UiTheme.Sq(40f));
            var portrait = picRt.gameObject.AddComponent<Image>();
            portrait.preserveAspect = true;
            portrait.raycastTarget = false;

            var name = UiKit.Label("Name", rt, fonts.Title, UiTheme.Py(13f), UiTheme.Ink,
                                   TextAlignmentOptions.Center, out var nameRt, "");
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.overflowMode = TextOverflowModes.Ellipsis;
            Anchor(nameRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(2f), -UiTheme.Py(67f)), new Vector2(-UiTheme.Px(2f), -UiTheme.Py(47f)));

            var family = UiKit.Label("Family", rt, fonts.Body, UiTheme.Py(9.5f), UiTheme.Ink2,
                                     TextAlignmentOptions.Center, out var famRt, "");
            family.textWrappingMode = TextWrappingModes.NoWrap;
            family.overflowMode = TextOverflowModes.Ellipsis;
            Anchor(famRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(2f), -UiTheme.Py(81f)), new Vector2(-UiTheme.Px(2f), -UiTheme.Py(66f)));

            // 값표: 오른쪽 위 흰 알약.
            var costRt = UiKit.Node("Cost", rt);
            Anchor(costRt, new Vector2(1f, 1f), new Vector2(1f, 1f),
                   new Vector2(-UiTheme.Px(30f), -UiTheme.Py(19f)),
                   new Vector2(-UiTheme.Px(3f), -UiTheme.Py(3f)));
            var costBg = costRt.gameObject.AddComponent<Image>();
            costBg.sprite = GeneratedArt.RoundedNineSlice("lg_cost", Mathf.RoundToInt(UiTheme.Px(7f)));
            costBg.type = Image.Type.Sliced;
            costBg.color = Color.white;
            costBg.raycastTarget = false;
            var cost = UiKit.Label("Text", costRt, fonts.Number, UiTheme.Py(12f), UiTheme.Ink,
                                   TextAlignmentOptions.Center, out var costTxtRt, "0");
            cost.textWrappingMode = TextWrappingModes.NoWrap;
            // 알약은 원본 치수(16 높이)를 지키되, 글자 칸은 줄 높이만큼 잡아 가운데에 둔다.
            CentreLine(costTxtRt, UiTheme.Line(UiTheme.Py(12f)));

            // 꼬리표는 판과 글자를 따로 둔다 — 한 칸에 Image 와 TMP 를 같이 넣을 수 없다.
            var trPlate = UiKit.Node("Trial", rt);
            Anchor(trPlate, new Vector2(0f, 1f), new Vector2(0f, 1f),
                   new Vector2(UiTheme.Px(3f), -UiTheme.Py(16f)),
                   new Vector2(UiTheme.Px(31f), -UiTheme.Py(3f)));
            var trialBg = trPlate.gameObject.AddComponent<Image>();
            trialBg.sprite = GeneratedArt.RoundedNineSlice("lg_trial", Mathf.RoundToInt(UiTheme.Px(5f)));
            trialBg.type = Image.Type.Sliced;
            trialBg.color = UiTheme.Ink2;
            trialBg.raycastTarget = false;
            var trial = UiKit.Label("Text", trPlate, fonts.Body, UiTheme.Py(8.5f), Color.white,
                                    TextAlignmentOptions.Center, out var trRt, "체험");
            trial.textWrappingMode = TextWrappingModes.NoWrap;
            CentreLine(trRt, UiTheme.Line(UiTheme.Py(8.5f)));

            var ribRt = UiKit.Node("Ribbon", rt);
            Anchor(ribRt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   Vector2.zero, new Vector2(0f, UiTheme.Py(14f)));
            var ribBg = ribRt.gameObject.AddComponent<Image>();
            ribBg.color = UiTheme.Led;
            ribBg.raycastTarget = false;
            var rib = UiKit.Label("Text", ribRt, fonts.Body, UiTheme.Py(9.5f), Color.white,
                                  TextAlignmentOptions.Center, out var ribTxtRt, "사면 ★2!");
            rib.textWrappingMode = TextWrappingModes.NoWrap;
            CentreLine(ribTxtRt, UiTheme.Line(UiTheme.Py(9.5f)));

            var group = go.AddComponent<CanvasGroup>();

            var card = go.AddComponent<LeagueShopCard>();
            Wire(card, ("face", face), ("outline", outline), ("wash", wash),
                 ("portrait", portrait), ("nameLabel", name), ("familyLabel", family),
                 ("costLabel", cost), ("costPlate", costRt.gameObject),
                 ("trialTag", trPlate.gameObject), ("ribbon", ribRt.gameObject), ("ribbonLabel", rib),
                 ("group", group), ("button", button));
            return SavePrefab<LeagueShopCard>(go, "LeagueShopCard");
        }

        /// <summary>
        /// 먼지 타워 출전 목록의 한 칸(원본 `.tw-oc`). 실측 62.16x54 · 모서리 10 ·
        /// 테두리 1.5 · 아이콘 34 · `Lv` 10.5 LED(아래 2) · ★ 왼쪽 위 8.5.
        /// </summary>
        static TowerOwnCell BuildTowerOwnPrefab(Fonts fonts)
        {
            int radius = Mathf.RoundToInt(UiTheme.Px(10f));
            var go = UI("TowerOwnCell", null, out var rt);
            rt.sizeDelta = new Vector2(UiTheme.Px(62.16f), UiTheme.Py(54f));

            var face = go.AddComponent<Image>();
            face.sprite = GeneratedArt.RoundedNineSlice("tw_oc", radius);
            face.type = Image.Type.Sliced;
            face.color = Color.white;

            var button = go.AddComponent<UiButton>();

            var lineRt = UiKit.Node("Outline", rt);
            Stretch(lineRt);
            var outline = lineRt.gameObject.AddComponent<Image>();
            outline.sprite = GeneratedArt.RoundedOutlineNineSlice("tw_oc_line", radius,
                Mathf.Max(1, Mathf.RoundToInt(UiTheme.Px(1.5f))));
            outline.type = Image.Type.Sliced;
            outline.color = UiTheme.PanelLine;
            outline.raycastTarget = false;

            var picRt = UiKit.Node("Portrait", rt);
            picRt.anchorMin = picRt.anchorMax = new Vector2(0.5f, 1f);
            picRt.pivot = new Vector2(0.5f, 1f);
            picRt.anchoredPosition = new Vector2(0f, -UiTheme.Py(3f));
            picRt.sizeDelta = new Vector2(UiTheme.Sq(34f), UiTheme.Sq(34f));
            var portrait = picRt.gameObject.AddComponent<Image>();
            portrait.preserveAspect = true;
            portrait.raycastTarget = false;

            var level = UiKit.Label("Level", rt, fonts.Number, UiTheme.Py(10.5f), UiTheme.Ink,
                                    TextAlignmentOptions.Center, out var lvRt, "Lv1");
            level.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(lvRt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(0f, UiTheme.Py(2f)),
                   new Vector2(0f, UiTheme.Py(2f) + UiTheme.Line(UiTheme.Py(10.5f))));

            var star = UiKit.Label("Star", rt, fonts.Body, UiTheme.Py(8.5f),
                                   UiTheme.TowerBonusLine, TextAlignmentOptions.TopLeft,
                                   out var stRt, "");
            star.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(stRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(3f), -UiTheme.Py(2f) - UiTheme.Line(UiTheme.Py(8.5f))),
                   new Vector2(0f, -UiTheme.Py(2f)));

            // '출전' 띠 — 원본은 칸 위에서 16 떨어진 자리에 파란 알약으로 붙인다.
            var inRt = UiKit.Node("InBadge", rt);
            Anchor(inRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                   new Vector2(-UiTheme.Px(18f),
                               -UiTheme.Py(16f) - UiTheme.Row(UiTheme.Py(10f), UiTheme.Py(14f))),
                   new Vector2(UiTheme.Px(18f), -UiTheme.Py(16f)));
            var inFace = inRt.gameObject.AddComponent<Image>();
            inFace.sprite = GeneratedArt.RoundedNineSlice("tw_oc_in", Mathf.RoundToInt(UiTheme.Px(6f)));
            inFace.type = Image.Type.Sliced;
            inFace.color = UiTheme.Navy;
            inFace.raycastTarget = false;
            var inLabel = UiKit.Label("Text", inRt, fonts.Body, UiTheme.Py(10f), Color.white,
                                      TextAlignmentOptions.Center, out var inLblRt, "출전");
            inLabel.textWrappingMode = TextWrappingModes.NoWrap;
            Stretch(inLblRt);
            inRt.gameObject.SetActive(false);

            // '추천' 띠 — 오른쪽 위.
            var hotRt = UiKit.Node("HotBadge", rt);
            Anchor(hotRt, new Vector2(1f, 1f), new Vector2(1f, 1f),
                   new Vector2(-UiTheme.Px(26f),
                               -UiTheme.Py(2f) - UiTheme.Row(UiTheme.Py(8.5f), UiTheme.Py(12f))),
                   new Vector2(-UiTheme.Px(2f), -UiTheme.Py(2f)));
            var hotFace = hotRt.gameObject.AddComponent<Image>();
            hotFace.sprite = GeneratedArt.RoundedNineSlice("tw_oc_hot", Mathf.RoundToInt(UiTheme.Px(5f)));
            hotFace.type = Image.Type.Sliced;
            hotFace.color = UiTheme.Danger;
            hotFace.raycastTarget = false;
            var hotLabel = UiKit.Label("Text", hotRt, fonts.Body, UiTheme.Py(8.5f), Color.white,
                                       TextAlignmentOptions.Center, out var hotLblRt, "추천");
            hotLabel.textWrappingMode = TextWrappingModes.NoWrap;
            Stretch(hotLblRt);
            hotRt.gameObject.SetActive(false);

            var cell = go.AddComponent<TowerOwnCell>();
            Wire(cell, ("face", face), ("outline", outline), ("portrait", portrait),
                 ("levelLabel", level), ("starLabel", star),
                 ("inBadge", inRt.gameObject), ("hotBadge", hotRt.gameObject),
                 ("button", button));
            return SavePrefab<TowerOwnCell>(go, "TowerOwnCell");
        }

        /// <summary>
        /// 층 목록의 한 줄(원본 `.tw-fl`). 실측 346.9x47 · 30/132/1fr 격자에 간격 7 ·
        /// 번호 16 LED · 창 그림 129.4x39.2 · 이름 12.5 · 보상 11.
        /// </summary>
        static TowerFloorCard BuildTowerFloorPrefab(Fonts fonts)
        {
            int radius = Mathf.RoundToInt(UiTheme.Px(10f));
            var go = UI("TowerFloorCard", null, out var rt);
            rt.sizeDelta = new Vector2(UiTheme.Px(346.9f), UiTheme.Py(47f));
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = UiTheme.Py(47f);

            var face = go.AddComponent<Image>();
            face.sprite = GeneratedArt.RoundedNineSlice("tw_fl", radius);
            face.type = Image.Type.Sliced;
            face.color = UiTheme.TowerFloorOpen;

            var button = go.AddComponent<UiButton>();

            var ringRt = UiKit.Node("Ring", rt);
            Stretch(ringRt);
            var ring = ringRt.gameObject.AddComponent<Image>();
            ring.sprite = GeneratedArt.RoundedOutlineNineSlice("tw_fl_ring", radius,
                Mathf.Max(1, Mathf.RoundToInt(UiTheme.Px(2.5f))));
            ring.type = Image.Type.Sliced;
            ring.color = UiTheme.Led;
            ring.raycastTarget = false;
            ringRt.gameObject.SetActive(false);

            float half = UiTheme.Row(UiTheme.Py(16f), UiTheme.Py(20f)) * 0.5f;
            var number = UiKit.Label("Number", rt, fonts.Number, UiTheme.Py(16f),
                                     UiTheme.TowerFloorNumber, TextAlignmentOptions.Center,
                                     out var noRt, "1");
            number.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(noRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                   new Vector2(UiTheme.Px(2f), -half), new Vector2(UiTheme.Px(31.4f), half));

            var window = BuildTowerWindow(rt);

            // 이름줄과 보상줄 — 격자의 1fr 칸.
            float infoLeft = UiTheme.Px(174.5f);
            var nameRow = UiKit.Node("NameRow", rt);
            Anchor(nameRow, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(infoLeft,
                               -UiTheme.Py(5f) - UiTheme.Row(UiTheme.Py(12.5f), UiTheme.Py(18.6f))),
                   new Vector2(-UiTheme.Px(7f), -UiTheme.Py(5f)));
            var nameLayout = UiKit.Row(nameRow, UiTheme.Px(4f), TextAnchor.MiddleLeft);
            nameLayout.childControlWidth = true;
            nameLayout.childForceExpandWidth = false;

            var tagRt = UiKit.Node("Tag", nameRow);
            var tagFace = tagRt.gameObject.AddComponent<Image>();
            tagFace.sprite = GeneratedArt.RoundedNineSlice("tw_tag", Mathf.RoundToInt(UiTheme.Px(6f)));
            tagFace.type = Image.Type.Sliced;
            tagFace.color = UiTheme.TowerTag(string.Empty, false);
            tagFace.raycastTarget = false;
            var tagLe = tagRt.gameObject.AddComponent<LayoutElement>();
            tagLe.minHeight = tagLe.preferredHeight = UiTheme.Row(UiTheme.Py(10f), UiTheme.Py(14f));
            tagLe.preferredWidth = UiTheme.Px(40f);
            var tag = UiKit.Label("Text", tagRt, fonts.Body, UiTheme.Py(10f), Color.white,
                                  TextAlignmentOptions.Center, out var tagLblRt, "");
            tag.textWrappingMode = TextWrappingModes.NoWrap;
            Stretch(tagLblRt);

            var name = UiKit.Label("Name", nameRow, fonts.Body, UiTheme.Py(12.5f),
                                   UiTheme.DarkInk, TextAlignmentOptions.Left, out var nameRt, "");
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.overflowMode = TextOverflowModes.Ellipsis;
            var nameLe = nameRt.gameObject.AddComponent<LayoutElement>();
            nameLe.flexibleWidth = 1f;
            nameLe.minHeight = UiTheme.Row(UiTheme.Py(12.5f), UiTheme.Py(18.6f));

            var reward = UiKit.Label("Reward", rt, fonts.Body, UiTheme.Py(11f),
                                     UiTheme.TowerFloorReward, TextAlignmentOptions.Left,
                                     out var rwRt, "");
            reward.textWrappingMode = TextWrappingModes.NoWrap;
            reward.overflowMode = TextOverflowModes.Ellipsis;
            Anchor(rwRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(infoLeft,
                               -UiTheme.Py(26.7f) - UiTheme.Row(UiTheme.Py(11f), UiTheme.Py(16.7f))),
                   new Vector2(-UiTheme.Px(7f), -UiTheme.Py(26.7f)));

            // '도전' 띠 — 오른쪽 7, 38.7x25.5.
            var goRt = UiKit.Node("GoBadge", rt);
            Anchor(goRt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                   new Vector2(-UiTheme.Px(45.7f), -UiTheme.Py(12.75f)),
                   new Vector2(-UiTheme.Px(7f), UiTheme.Py(12.75f)));
            var goFace = goRt.gameObject.AddComponent<Image>();
            goFace.sprite = GeneratedArt.RoundedNineSlice("tw_fl_go", Mathf.RoundToInt(UiTheme.Px(9f)));
            goFace.type = Image.Type.Sliced;
            goFace.color = UiTheme.Led;
            goFace.raycastTarget = false;
            var goLabel = UiKit.Label("Text", goRt, fonts.Title, UiTheme.Py(14f), UiTheme.Ink,
                                      TextAlignmentOptions.Center, out var goLblRt, "도전");
            goLabel.textWrappingMode = TextWrappingModes.NoWrap;
            Stretch(goLblRt);
            goRt.gameObject.SetActive(false);

            var row = go.AddComponent<TowerFloorCard>();
            Wire(row, ("face", face), ("ring", ring), ("numberLabel", number),
                 ("window", window), ("tagLabel", tag), ("tagFace", tagFace),
                 ("nameLabel", name), ("rewardLabel", reward), ("nameRow", nameRow),
                 ("goBadge", goRt.gameObject), ("button", button));
            return SavePrefab<TowerFloorCard>(go, "TowerFloorCard");
        }

        /// <summary>
        /// 층 줄의 작은 창(원본 `twWindow` 가 캔버스로 그리던 132x40). 바탕 · 바닥 띠 ·
        /// 먼지 셋 · 덧칠 · 창틀과 가운데 창살을 uGUI 로 쌓는다.
        /// </summary>
        static TowerWindowView BuildTowerWindow(RectTransform parent)
        {
            var rt = UiKit.Node("Window", parent);
            Anchor(rt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                   new Vector2(UiTheme.Px(38.3f), -UiTheme.Py(19.6f)),
                   new Vector2(UiTheme.Px(167.7f), UiTheme.Py(19.6f)));

            var face = rt.gameObject.AddComponent<Image>();
            face.sprite = GeneratedArt.RoundedNineSlice("tw_win", Mathf.RoundToInt(UiTheme.Px(6f)));
            face.type = Image.Type.Sliced;
            face.color = UiTheme.TowerWindowRoom;
            face.raycastTarget = false;

            Image Strip(string id, Vector2 min, Vector2 max, Color color)
            {
                var n = UiKit.Node(id, rt);
                Anchor(n, min, max, Vector2.zero, Vector2.zero);
                var img = n.gameObject.AddComponent<Image>();
                img.color = color;
                img.raycastTarget = false;
                return img;
            }

            var floorStrip = Strip("Floor", new Vector2(0f, 0f), new Vector2(1f, 0.28f),
                                   new Color(90f / 255f, 70f / 255f, 50f / 255f, 0.25f));

            var foes = new Image[3];
            for (int i = 0; i < foes.Length; i++)
            {
                var n = UiKit.Node("Foe" + i, rt);
                // 원본은 .16 · .41 · .66 자리에 세운다.
                float cx = 0.16f + i * 0.25f;
                Anchor(n, new Vector2(cx - 0.1f, 0.18f), new Vector2(cx + 0.1f, 0.86f),
                       Vector2.zero, Vector2.zero);
                var img = n.gameObject.AddComponent<Image>();
                img.preserveAspect = true;
                img.raycastTarget = false;
                n.gameObject.SetActive(false);
                foes[i] = img;
            }

            var leftWall = Strip("WallL", new Vector2(0f, 0f), new Vector2(0.26f, 1f),
                                 new Color(60f / 255f, 50f / 255f, 40f / 255f, 0.4f));
            var rightWall = Strip("WallR", new Vector2(0.74f, 0f), new Vector2(1f, 1f),
                                  new Color(60f / 255f, 50f / 255f, 40f / 255f, 0.4f));
            leftWall.gameObject.SetActive(false);
            rightWall.gameObject.SetActive(false);

            var tint = Strip("Tint", Vector2.zero, Vector2.one, Color.clear);
            tint.gameObject.SetActive(false);

            var mullionRt = UiKit.Node("Mullion", rt);
            Anchor(mullionRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
                   new Vector2(-UiTheme.Px(1f), 0f), new Vector2(UiTheme.Px(1f), 0f));
            var mullion = mullionRt.gameObject.AddComponent<Image>();
            mullion.color = UiTheme.TowerWindowFrame;
            mullion.raycastTarget = false;

            var frameRt = UiKit.Node("Frame", rt);
            Stretch(frameRt);
            var frame = frameRt.gameObject.AddComponent<Image>();
            frame.sprite = GeneratedArt.RoundedOutlineNineSlice("tw_win_line",
                Mathf.RoundToInt(UiTheme.Px(6f)), Mathf.Max(1, Mathf.RoundToInt(UiTheme.Px(3f))));
            frame.type = Image.Type.Sliced;
            frame.color = UiTheme.TowerWindowFrame;
            frame.raycastTarget = false;

            var veil = Strip("Veil", Vector2.zero, Vector2.one,
                             new Color(40f / 255f, 46f / 255f, 52f / 255f, 0.35f));
            veil.gameObject.SetActive(false);

            var view = rt.gameObject.AddComponent<TowerWindowView>();
            Wire(view, ("face", face), ("floorStrip", floorStrip), ("tint", tint),
                 ("leftWall", leftWall), ("rightWall", rightWall), ("veil", veil),
                 ("mullion", mullion));
            WireArray(view, "foes", foes);
            return view;
        }

        /// <summary>
        /// 리그 화면. 원본 `#league` 을 430x932 에서 재서 그대로 쌓는다.
        /// </summary>
        static LeagueScreen BuildLeagueUI(Transform parent, Fonts fonts, Prefabs prefabs,
                                          LeagueView view, Camera cam, SpriteLibrary library)
        {
            var go = UI("League", parent, out var rt);
            Stretch(rt);
            float pad = UiTheme.Px(16f);

            RectTransform Row(string id, float top, float height)
            {
                var r = UiKit.Node(id, rt);
                Anchor(r, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(pad, -UiTheme.Py(top + height)), new Vector2(-pad, -UiTheme.Py(top)));
                return r;
            }

            // ---- HUD 12 / 106
            var hud = UiKit.Panel("Hud", rt, out var hudRt, UiTheme.DarkPanel);
            hud.sprite = GeneratedArt.RoundedNineSlice("ui_modal14", Mathf.RoundToInt(UiTheme.Px(14f)));
            hud.type = Image.Type.Sliced;
            Anchor(hudRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(pad, -UiTheme.Py(118f)), new Vector2(-pad, -UiTheme.Py(12f)));

            float inset = UiTheme.Px(12f);
            var top = UiKit.Node("Top", hudRt);
            Anchor(top, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(inset, -UiTheme.Py(40f)), new Vector2(-inset, -UiTheme.Py(10f)));

            var kicker = UiKit.Label("Kicker", top, fonts.Number, UiTheme.Py(11f), UiTheme.DarkInk2,
                                     TextAlignmentOptions.TopLeft, out var kRt, "LEAGUE");
            kicker.textWrappingMode = TextWrappingModes.NoWrap;
            kicker.characterSpacing = 6f;
            // 글자 칸이 줄 높이보다 낮으면 TMP 는 글자를 **아예 그리지 않는다.**
            Anchor(kRt, new Vector2(0f, 1f), new Vector2(0.6f, 1f),
                   new Vector2(0f, -UiTheme.Row(UiTheme.Py(11f), UiTheme.Py(14f))), Vector2.zero);

            var chip = UiKit.Label("Chip", top, fonts.Title, UiTheme.Py(17f), UiTheme.DarkInk,
                                   TextAlignmentOptions.TopLeft, out var chipRt, "");
            chip.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(chipRt, new Vector2(0f, 1f), new Vector2(0.6f, 1f),
                   new Vector2(0f, -UiTheme.Py(36f)), new Vector2(0f, -UiTheme.Py(14f)));

            var ctrls = UiKit.Node("Ctrls", top);
            Anchor(ctrls, new Vector2(0.5f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            var ctrlRow = UiKit.Row(ctrls, UiTheme.Px(6f), TextAnchor.MiddleRight);
            ctrlRow.childControlWidth = true;
            ctrlRow.childControlHeight = false;
            ctrlRow.childForceExpandWidth = false;

            UiButton Ctrl(string id, string text)
            {
                var b = UiKit.Button(id, ctrls, fonts.Body, text, UiButtonKind.DarkRaised,
                                     out var crt, UiTheme.Py(30f), UiTheme.Py(12f));
                var le = crt.gameObject.AddComponent<LayoutElement>();
                le.minHeight = le.preferredHeight = UiTheme.Py(30f);
                le.preferredWidth = UiTheme.Px(text.Length <= 2 ? 44f : 60f);
                return b;
            }
            var speed = Ctrl("Speed", "2×");
            var exit = Ctrl("Exit", "나가기");

            // 읽는 칸 셋: 청결도 · 코인 · 레벨
            var readouts = UiKit.Node("Readouts", hudRt);
            Anchor(readouts, new Vector2(0f, 0f), new Vector2(1f, 1f),
                   new Vector2(inset, UiTheme.Py(10f)), new Vector2(-inset, -UiTheme.Py(52f)));

            TextMeshProUGUI Readout(string id, string caption, float x0, float x1,
                                    Color ink, TextAlignmentOptions align)
            {
                var r = UiKit.Node(id, readouts);
                Anchor(r, new Vector2(x0, 0f), new Vector2(x1, 1f), Vector2.zero, Vector2.zero);

                var cap = UiKit.Label("Label", r, fonts.Body, UiTheme.Py(10f), UiTheme.DarkInk2,
                                      align, out var capRt, caption);
                cap.textWrappingMode = TextWrappingModes.NoWrap;
                cap.characterSpacing = 8f;
                Anchor(capRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(0f, -UiTheme.Py(15f)), Vector2.zero);

                var value = UiKit.Label("Value", r, fonts.Number, UiTheme.Py(24f), ink,
                                        align, out var vRt, "0");
                value.textWrappingMode = TextWrappingModes.NoWrap;
                Anchor(vRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(0f, -UiTheme.Py(53f)), new Vector2(0f, -UiTheme.Py(17f)));
                return value;
            }

            var life = Readout("Life", "청결도", 0f, 0.3f, UiTheme.LedLife, TextAlignmentOptions.Left);

            var gold = Readout("Gold", "코인", 0.3f, 0.6f, UiTheme.Led, TextAlignmentOptions.Left);

            var lvRoot = UiKit.Node("Level", readouts);
            Anchor(lvRoot, new Vector2(0.6f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            var lvLabel = UiKit.Label("Label", lvRoot, fonts.Body, UiTheme.Py(10f), UiTheme.DarkInk2,
                                      TextAlignmentOptions.Right, out var lvRt, "레벨 3");
            lvLabel.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(lvRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(0f, -UiTheme.Py(15f)), Vector2.zero);
            var xp = UiKit.Label("Xp", lvRoot, fonts.Number, UiTheme.Py(18f), UiTheme.Led,
                                 TextAlignmentOptions.Right, out var xpRt, "0/4");
            xp.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(xpRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(0f, -UiTheme.Py(45f)), new Vector2(0f, -UiTheme.Py(17f)));
            var xpBar = UiKit.Bar("Meter", lvRoot, UiTheme.LedTrack, UiTheme.Led,
                                  UiTheme.Py(4f), out var xpFill);
            Anchor(xpBar, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   Vector2.zero, new Vector2(0f, UiTheme.Py(4f)));

            // ---- 시너지 136 / 29
            var synRow = Row("Synergy", 136f, 29f);
            var synLayout = UiKit.Row(synRow, UiTheme.Px(5f), TextAnchor.MiddleLeft);
            synLayout.childControlWidth = true;
            synLayout.childControlHeight = true;
            synLayout.childForceExpandWidth = false;
            synLayout.childForceExpandHeight = false;

            // ---- 판 175 / 336 — 카메라가 그리고, 우리 자리 21칸에 투명 단추를 겹친다.
            var cellRoot = UiKit.Node("Cells", rt);
            Anchor(cellRoot, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(pad, -UiTheme.Py(511f)), new Vector2(-pad, -UiTheme.Py(343f)));
            var cellGrid = cellRoot.gameObject.AddComponent<GridLayoutGroup>();
            cellGrid.cellSize = new Vector2(UiTheme.Px(398f / LeagueSim.Cols),
                                            UiTheme.Py(168f / LeagueSim.OurRows));
            cellGrid.spacing = Vector2.zero;
            cellGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            cellGrid.constraintCount = LeagueSim.Cols;

            // ---- 안내 줄 529 / 18
            var infoRow = Row("Info", 529f, 18f);
            var infoLeft = UiKit.Label("Left", infoRow, fonts.Body, UiTheme.Py(12f), UiTheme.Ink2,
                                       TextAlignmentOptions.Left, out var ilRt, "");
            infoLeft.textWrappingMode = TextWrappingModes.NoWrap;
            infoLeft.overflowMode = TextOverflowModes.Ellipsis;
            Anchor(ilRt, new Vector2(0f, 0f), new Vector2(0.65f, 1f), Vector2.zero, Vector2.zero);
            var infoRight = UiKit.Label("Right", infoRow, fonts.Body, UiTheme.Py(12f), UiTheme.Ink2,
                                        TextAlignmentOptions.Right, out var irRt, "");
            infoRight.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(irRt, new Vector2(0.65f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);

            // ---- 대기석 561 / 48
            var benchRow = Row("Bench", 561f, 48f);
            var benchGrid = benchRow.gameObject.AddComponent<GridLayoutGroup>();
            benchGrid.cellSize = new Vector2(UiTheme.Px(45.4f), UiTheme.Py(48f));
            benchGrid.spacing = new Vector2(UiTheme.Px(5f), 0f);
            benchGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            benchGrid.constraintCount = 8;

            // ---- 버튼 줄 627 / 36
            var barRow = Row("Bar", 627f, 36f);
            var barLayout = UiKit.Row(barRow, UiTheme.Px(6f), TextAnchor.MiddleCenter);
            barLayout.childControlWidth = true;
            barLayout.childControlHeight = true;
            barLayout.childForceExpandWidth = true;
            barLayout.childForceExpandHeight = true;

            UiButton Bar(string id, string text, UiButtonKind kind, out TextMeshProUGUI label)
            {
                var b = UiKit.Button(id, barRow, fonts.Body, text, kind, out var brt,
                                     UiTheme.Py(36f), UiTheme.Py(12.5f));
                label = brt.GetComponentInChildren<TextMeshProUGUI>();
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.overflowMode = TextOverflowModes.Ellipsis;
                return b;
            }
            var reroll = Bar("Reroll", "새로고침", UiButtonKind.DarkSolid, out var rerollLabel);
            var xpBtn = Bar("Xp", "경험치", UiButtonKind.DarkSolid, out var xpBtnLabel);
            var sell = Bar("Sell", "상점 잠금", UiButtonKind.Secondary, out var sellLabel);

            // ---- 상점 679 / 85
            var shopRow = Row("Shop", 679f, 85f);
            var shopGrid = shopRow.gameObject.AddComponent<GridLayoutGroup>();
            shopGrid.cellSize = new Vector2(UiTheme.Px(74.8f), UiTheme.Py(85f));
            shopGrid.spacing = new Vector2(UiTheme.Px(6f), 0f);
            shopGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            shopGrid.constraintCount = 5;

            // ---- 출발 782 / 46
            var goRow = Row("Go", 782f, 46f);
            var goBtn = UiKit.Button("Button", goRow, fonts.Title, "전투 시작", UiButtonKind.Primary,
                                     out var goRt, UiTheme.Py(46f), UiTheme.Py(19f));
            Stretch(goRt);
            var goLabel = goRt.GetComponentInChildren<TextMeshProUGUI>();

            // ---- 힌트 838 / 19.2. 타워는 두 줄이라 자리를 다시 잡는다.
            var hintRow = Row("Hint", 838f, 19.2f);
            var hint = UiKit.Label("Hint", hintRow, fonts.Body, UiTheme.Py(12f),
                                   UiTheme.Ink2, TextAlignmentOptions.Center, out var hintRt, "");
            hint.textWrappingMode = TextWrappingModes.NoWrap;
            hint.overflowMode = TextOverflowModes.Ellipsis;
            Stretch(hintRt);

            // ================================================================ 먼지 타워
            // 원본은 `#league` 에 `tw` 클래스를 붙여 같은 화면을 쓴다. 여기서도 같은
            // 화면에 타워 쪽 줄을 더 쌓고, 들어갈 때 리그 줄과 바꿔 단다.
            // 자리는 전부 430x932 실측 — TOWER-MEASUREMENTS.md 1절.

            // ---- 층 · 남은 시간 · 출전 (원본 `.tw-ro`, HUD 안 62/44)
            var twRo = UiKit.Node("TowerReadouts", hudRt);
            Anchor(twRo, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(inset, -UiTheme.Py(94f)), new Vector2(-inset, -UiTheme.Py(50f)));

            TextMeshProUGUI TowerRo(string id, string caption, float x0, float x1,
                                    TextAlignmentOptions align, out TextMeshProUGUI capOut)
            {
                var r = UiKit.Node(id, twRo);
                Anchor(r, new Vector2(x0, 0f), new Vector2(x1, 1f), Vector2.zero, Vector2.zero);

                capOut = UiKit.Label("Label", r, fonts.Body, UiTheme.Py(10f), UiTheme.DarkInk2,
                                     align, out var capRt, caption);
                capOut.textWrappingMode = TextWrappingModes.NoWrap;
                capOut.characterSpacing = 8f;
                Anchor(capRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(0f, -UiTheme.Row(UiTheme.Py(10f), UiTheme.Py(15f))), Vector2.zero);

                var value = UiKit.Label("Value", r, fonts.Number, UiTheme.Py(26f), UiTheme.Led,
                                        align, out var vRt, "");
                value.textWrappingMode = TextWrappingModes.NoWrap;
                // 26px LED 한 줄은 칸이 줄 높이보다 낮으면 **아예 안 그려진다**.
                // 글자가 앉는 띠(실측 18~44)의 한가운데에 줄 높이만큼의 칸을 둔다.
                float band = UiTheme.Row(UiTheme.Py(26f), UiTheme.Py(26f)) * 0.5f;
                Anchor(vRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(0f, -UiTheme.Py(31f) - band),
                       new Vector2(0f, -UiTheme.Py(31f) + band));
                return value;
            }

            // 세 칸: 374 를 셋으로 나누고 사이를 12 둔다(실측 x 28 / 156.7 / 285.3, 폭 116.7).
            var twFloorValue = TowerRo("Floor", "층", 0f, 0.312f,
                                       TextAlignmentOptions.Left, out _);
            var twGoalValue = TowerRo("Goal", "남은 시간", 0.3439f, 0.6559f,
                                      TextAlignmentOptions.Left, out var twGoalCaption);
            var twUseValue = TowerRo("Use", "출전", 0.688f, 1f,
                                     TextAlignmentOptions.Right, out _);
            twRo.gameObject.SetActive(false);

            // ---- 층 규칙 줄 182 / 54.4 (원본 `.tw-rule`)
            var ruleRow = Row("TowerRule", 182f, 54.4f);
            var ruleFace = ruleRow.gameObject.AddComponent<Image>();
            ruleFace.sprite = GeneratedArt.RoundedNineSlice("tw_rule", Mathf.RoundToInt(UiTheme.Px(12f)));
            ruleFace.type = Image.Type.Sliced;
            ruleFace.color = Color.white;
            ruleFace.raycastTarget = false;

            var ruleLineRt = UiKit.Node("Line", ruleRow);
            Stretch(ruleLineRt);
            var ruleLine = ruleLineRt.gameObject.AddComponent<Image>();
            ruleLine.sprite = GeneratedArt.RoundedOutlineNineSlice("tw_rule_line",
                Mathf.RoundToInt(UiTheme.Px(12f)), Mathf.Max(1, Mathf.RoundToInt(UiTheme.Px(1.5f))));
            ruleLine.type = Image.Type.Sliced;
            ruleLine.color = UiTheme.PanelLine;
            ruleLine.raycastTarget = false;

            // 첫 줄: 규칙 이름 + 설명(원본은 auto 1fr 격자에 간격 8).
            var ruleTop = UiKit.Node("Top", ruleRow);
            Anchor(ruleTop, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(10f), -UiTheme.Py(8f) - UiTheme.Row(UiTheme.Py(15f), UiTheme.Py(21f))),
                   new Vector2(-UiTheme.Px(10f), -UiTheme.Py(8f)));
            // 원본은 `align-items:baseline` — 이름과 설명이 첫 줄에서 만난다.
            var ruleTopRow = UiKit.Row(ruleTop, UiTheme.Px(8f), TextAnchor.UpperLeft);
            ruleTopRow.childControlWidth = true;
            ruleTopRow.childForceExpandWidth = false;

            var ruleName = UiKit.Label("Name", ruleTop, fonts.Title, UiTheme.Py(15f), UiTheme.Ink,
                                       TextAlignmentOptions.TopLeft, out var ruleNameRt, "");
            ruleName.textWrappingMode = TextWrappingModes.NoWrap;
            var ruleNameLe = ruleNameRt.gameObject.AddComponent<LayoutElement>();
            ruleNameLe.minHeight = UiTheme.Row(UiTheme.Py(15f), UiTheme.Py(21f));

            var ruleDesc = UiKit.Label("Desc", ruleTop, fonts.Body, UiTheme.Py(12f), UiTheme.Ink,
                                       TextAlignmentOptions.TopLeft, out var ruleDescRt, "");
            // 설명은 길면 두 줄이 된다 — 그러면 줄 전체가 54.4 에서 70 으로 커진다(실측).
            ruleDesc.textWrappingMode = TextWrappingModes.Normal;
            ruleDesc.overflowMode = TextOverflowModes.Truncate;
            var ruleDescLe = ruleDescRt.gameObject.AddComponent<LayoutElement>();
            ruleDescLe.flexibleWidth = 1f;
            ruleDescLe.minHeight = UiTheme.Row(UiTheme.Py(12f), UiTheme.Py(16.8f));

            var ruleBonus = UiKit.Label("Bonus", ruleRow, fonts.Body, UiTheme.Py(11f),
                                        UiTheme.TowerBonusInk, TextAlignmentOptions.Left,
                                        out var ruleBonusRt, "");
            ruleBonus.textWrappingMode = TextWrappingModes.NoWrap;
            ruleBonus.overflowMode = TextOverflowModes.Ellipsis;
            Anchor(ruleBonusRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(10f), -UiTheme.Py(31f) - UiTheme.Row(UiTheme.Py(11f), UiTheme.Py(15.4f))),
                   new Vector2(-UiTheme.Px(10f), -UiTheme.Py(31f)));
            ruleRow.gameObject.SetActive(false);

            // ---- 자동 편성 단추 — 안내 줄 오른쪽 끝(실측 67.7x26).
            var autoBtn = UiKit.Button("TowerAuto", infoRow, fonts.Body, "자동 편성",
                                       UiButtonKind.Secondary, out var autoRt,
                                       UiTheme.Py(26f), UiTheme.Py(12f));
            Anchor(autoRt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                   new Vector2(-UiTheme.Px(67.7f), -UiTheme.Py(13f)),
                   new Vector2(0f, UiTheme.Py(13f)));
            autoBtn.gameObject.SetActive(false);

            // ---- 출전 목록 640.4 / 54 (원본 `.tw-own`) — 줄 수는 보유 수가 정한다.
            var ownRow = Row("TowerOwn", 640.4f, 54f);
            var ownGrid = ownRow.gameObject.AddComponent<GridLayoutGroup>();
            ownGrid.cellSize = new Vector2(UiTheme.Px(62.16f), UiTheme.Py(54f));
            ownGrid.spacing = new Vector2(UiTheme.Px(5f), UiTheme.Py(5f));
            ownGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            ownGrid.constraintCount = 6;
            ownRow.gameObject.SetActive(false);

            // ---- 층 목록 시트(원본 `twEnter`). 실측 상자 20/87.9 390x776.2.
            var floorSheet = UiKit.Panel("TowerFloors", rt, out var fsRt,
                                         new Color(UiTheme.Ink.r, UiTheme.Ink.g, UiTheme.Ink.b, 0.55f), 1f);
            Stretch(fsRt);
            var floorBox = UiKit.Panel("Box", fsRt, out var fbRt, UiTheme.Panel);
            floorBox.sprite = GeneratedArt.RoundedNineSlice("ui_modal16", Mathf.RoundToInt(UiTheme.Px(16f)));
            floorBox.type = Image.Type.Sliced;
            Anchor(fbRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(20f), -UiTheme.Py(864.1f)),
                   new Vector2(-UiTheme.Px(20f), -UiTheme.Py(87.9f)));

            var floorHead = UiKit.Label("Title", fbRt, fonts.Title, UiTheme.Py(24f), UiTheme.Ink,
                                        TextAlignmentOptions.Left, out var fhRt, "먼지 타워");
            floorHead.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(fhRt, new Vector2(0f, 1f), new Vector2(0.6f, 1f),
                   new Vector2(UiTheme.Px(16f), -UiTheme.Py(16f) - UiTheme.Row(UiTheme.Py(24f), UiTheme.Py(29.4f))),
                   new Vector2(0f, -UiTheme.Py(16f)));

            var floorCount = UiKit.Label("Count", fbRt, fonts.Body, UiTheme.Py(12f), UiTheme.Ink2,
                                         TextAlignmentOptions.Right, out var fcRt, "");
            floorCount.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(fcRt, new Vector2(0.4f, 1f), new Vector2(1f, 1f),
                   new Vector2(0f, -UiTheme.Py(16f) - UiTheme.Row(UiTheme.Py(12f), UiTheme.Py(29.4f))),
                   new Vector2(-UiTheme.Px(16f), -UiTheme.Py(16f)));

            var floorNote = UiKit.Label("Note", fbRt, fonts.Body, UiTheme.Py(12f), UiTheme.Ink2,
                                        TextAlignmentOptions.TopLeft, out var fnRt, "");
            floorNote.textWrappingMode = TextWrappingModes.Normal;
            Anchor(fnRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(16f), -UiTheme.Py(128.7f)),
                   new Vector2(-UiTheme.Px(16f), -UiTheme.Py(56.9f)));

            // 탑: 지붕 + 층 줄 목록. 상자 좌표 기준이라 실측에서 상자 위(87.9)를 뺀다.
            var building = UiKit.Panel("Building", fbRt, out var bldRt, UiTheme.TowerBuilding);
            building.sprite = GeneratedArt.RoundedNineSlice("tw_bld", Mathf.RoundToInt(UiTheme.Px(14f)));
            building.type = Image.Type.Sliced;
            Anchor(bldRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(16f), -UiTheme.Py(712f)),
                   new Vector2(-UiTheme.Px(16f), -UiTheme.Py(140.5f)));

            var roof = UiKit.Label("Roof", bldRt, fonts.Body, UiTheme.Py(11f), UiTheme.TowerRoofInk,
                                   TextAlignmentOptions.Center, out var roofRt, "");
            roof.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(roofRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(0f, -UiTheme.Py(28.4f)), Vector2.zero);

            var floorViewport = UiKit.Node("Viewport", bldRt);
            Anchor(floorViewport, new Vector2(0f, 0f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(6f), UiTheme.Py(4f)),
                   new Vector2(-UiTheme.Px(6f), -UiTheme.Py(28.4f)));
            floorViewport.gameObject.AddComponent<RectMask2D>();
            var floorScroll = floorViewport.gameObject.AddComponent<ScrollRect>();
            UiKit.ScrollCatcher(floorViewport);
            floorScroll.horizontal = false;
            floorScroll.movementType = ScrollRect.MovementType.Clamped;
            floorScroll.scrollSensitivity = 40f;

            var floorList = UiKit.Node("List", floorViewport);
            Anchor(floorList, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            floorList.pivot = new Vector2(0.5f, 1f);
            var floorCol = floorList.gameObject.AddComponent<VerticalLayoutGroup>();
            floorCol.spacing = UiTheme.Py(4f);
            floorCol.childControlWidth = true;
            floorCol.childControlHeight = false;
            floorCol.childForceExpandWidth = true;
            floorCol.childForceExpandHeight = false;
            var floorFit = floorList.gameObject.AddComponent<ContentSizeFitter>();
            floorFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            floorScroll.viewport = floorViewport;
            floorScroll.content = floorList;

            var floorBtns = UiKit.Node("Buttons", fbRt);
            Anchor(floorBtns, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(UiTheme.Px(16f), UiTheme.Py(16f)),
                   new Vector2(-UiTheme.Px(16f), UiTheme.Py(64f)));
            var floorClose = UiKit.Button("Close", floorBtns, fonts.Body, "닫기",
                                          UiButtonKind.Secondary, out var fclRt, UiTheme.Py(48f));
            Anchor(fclRt, new Vector2(0f, 0f), new Vector2(0.48f, 1f), Vector2.zero, Vector2.zero);
            var floorGo = UiKit.Button("Go", floorBtns, fonts.Body, "도전", UiButtonKind.Primary,
                                       out var fgoRt, UiTheme.Py(48f));
            Anchor(fgoRt, new Vector2(0.52f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            var floorGoLabel = fgoRt.GetComponentInChildren<TextMeshProUGUI>();
            floorSheet.gameObject.SetActive(false);

            // ---- 층 상세 시트(원본 `twFloorInfo`). 실측 상자 16/248.5 398x434.9.
            var infoSheet = UiKit.Panel("TowerFloorInfo", rt, out var isRt,
                                        new Color(UiTheme.Ink.r, UiTheme.Ink.g, UiTheme.Ink.b, 0.55f), 1f);
            Stretch(isRt);
            var infoBox = UiKit.Panel("Box", isRt, out var ibRt, UiTheme.Panel);
            infoBox.sprite = GeneratedArt.RoundedNineSlice("ui_modal16", Mathf.RoundToInt(UiTheme.Px(16f)));
            infoBox.type = Image.Type.Sliced;
            Anchor(ibRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(pad, -UiTheme.Py(683.4f)), new Vector2(-pad, -UiTheme.Py(248.5f)));

            var infoTitle = UiKit.Label("Title", ibRt, fonts.Title, UiTheme.Py(22f), UiTheme.Ink,
                                        TextAlignmentOptions.Left, out var itRt, "");
            infoTitle.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(itRt, new Vector2(0f, 1f), new Vector2(0.75f, 1f),
                   new Vector2(UiTheme.Px(16f), -UiTheme.Py(16f) - UiTheme.Row(UiTheme.Py(22f), UiTheme.Py(36f))),
                   new Vector2(0f, -UiTheme.Py(16f)));

            var infoState = UiKit.Label("State", ibRt, fonts.Body, UiTheme.Py(12f), UiTheme.Ink2,
                                        TextAlignmentOptions.Right, out var isLblRt, "");
            infoState.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(isLblRt, new Vector2(0.55f, 1f), new Vector2(1f, 1f),
                   new Vector2(0f, -UiTheme.Py(16f) - UiTheme.Row(UiTheme.Py(12f), UiTheme.Py(36f))),
                   new Vector2(-UiTheme.Px(16f), -UiTheme.Py(16f)));

            // 규칙 카드 64 / 62.8 (상자 기준)
            var infoRule = UiKit.Panel("Rule", ibRt, out var ruleCardRt, Color.white);
            infoRule.sprite = GeneratedArt.RoundedNineSlice("tw_rc", Mathf.RoundToInt(UiTheme.Px(12f)));
            infoRule.type = Image.Type.Sliced;
            Anchor(ruleCardRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(16f), -UiTheme.Py(126.8f)),
                   new Vector2(-UiTheme.Px(16f), -UiTheme.Py(64f)));
            var infoRuleLineRt = UiKit.Node("Line", ruleCardRt);
            Stretch(infoRuleLineRt);
            var infoRuleLine = infoRuleLineRt.gameObject.AddComponent<Image>();
            infoRuleLine.sprite = GeneratedArt.RoundedOutlineNineSlice("tw_rc_line",
                Mathf.RoundToInt(UiTheme.Px(12f)), Mathf.Max(1, Mathf.RoundToInt(UiTheme.Px(1.5f))));
            infoRuleLine.type = Image.Type.Sliced;
            infoRuleLine.color = UiTheme.PanelLine;
            infoRuleLine.raycastTarget = false;

            var infoRuleName = UiKit.Label("Name", ruleCardRt, fonts.Title, UiTheme.Py(17f), UiTheme.Ink,
                                           TextAlignmentOptions.Left, out var irnRt, "");
            infoRuleName.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(irnRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(11f), -UiTheme.Py(10f) - UiTheme.Row(UiTheme.Py(17f), UiTheme.Py(21f))),
                   new Vector2(-UiTheme.Px(11f), -UiTheme.Py(10f)));

            var infoRuleDesc = UiKit.Label("Desc", ruleCardRt, fonts.Body, UiTheme.Py(12.5f), UiTheme.Ink,
                                           TextAlignmentOptions.TopLeft, out var irdRt, "");
            infoRuleDesc.textWrappingMode = TextWrappingModes.Normal;
            Anchor(irdRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(11f), -UiTheme.Py(56f)),
                   new Vector2(-UiTheme.Px(11f), -UiTheme.Py(34f)));

            // 목표 · 보너스 두 칸 138.8 / 66
            Image GoalBox(string id, float x0, float x1, Color fill, out RectTransform boxRt,
                          out Image lineOut)
            {
                var img = UiKit.Panel(id, ibRt, out boxRt, fill);
                img.sprite = GeneratedArt.RoundedNineSlice("tw_goal", Mathf.RoundToInt(UiTheme.Px(12f)));
                img.type = Image.Type.Sliced;
                Anchor(boxRt, new Vector2(x0, 1f), new Vector2(x1, 1f),
                       new Vector2(x0 == 0f ? UiTheme.Px(16f) : UiTheme.Px(3f), -UiTheme.Py(204.8f)),
                       new Vector2(x1 == 1f ? -UiTheme.Px(16f) : -UiTheme.Px(3f), -UiTheme.Py(138.8f)));
                var ln = UiKit.Node("Line", boxRt);
                Stretch(ln);
                lineOut = ln.gameObject.AddComponent<Image>();
                lineOut.sprite = GeneratedArt.RoundedOutlineNineSlice("tw_goal_line",
                    Mathf.RoundToInt(UiTheme.Px(12f)), Mathf.Max(1, Mathf.RoundToInt(UiTheme.Px(1.5f))));
                lineOut.type = Image.Type.Sliced;
                lineOut.color = UiTheme.PanelLine;
                lineOut.raycastTarget = false;
                return img;
            }

            GoalBox("Goal", 0f, 0.5f, Color.white, out var goalRt, out _);
            var goalCap = UiKit.Label("Caption", goalRt, fonts.Body, UiTheme.Py(11f), UiTheme.Ink2,
                                      TextAlignmentOptions.TopLeft, out var gcRt, "목표");
            goalCap.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(gcRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(9f), -UiTheme.Py(7f) - UiTheme.Row(UiTheme.Py(11f), UiTheme.Py(15f))),
                   new Vector2(-UiTheme.Px(9f), -UiTheme.Py(7f)));
            var infoGoal = UiKit.Label("Value", goalRt, fonts.Body, UiTheme.Py(12f), UiTheme.Ink,
                                       TextAlignmentOptions.TopLeft, out var igRt, "");
            infoGoal.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(igRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(9f), -UiTheme.Py(24f) - UiTheme.Row(UiTheme.Py(12f), UiTheme.Py(17f))),
                   new Vector2(-UiTheme.Px(9f), -UiTheme.Py(24f)));
            var infoGoalSub = UiKit.Label("Sub", goalRt, fonts.Body, UiTheme.Py(10.5f), UiTheme.Ink2,
                                          TextAlignmentOptions.TopLeft, out var igsRt, "");
            infoGoalSub.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(igsRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(9f), -UiTheme.Py(43f) - UiTheme.Row(UiTheme.Py(10.5f), UiTheme.Py(15f))),
                   new Vector2(-UiTheme.Px(9f), -UiTheme.Py(43f)));

            var infoBonusFace = GoalBox("Bonus", 0.5f, 1f, UiTheme.TowerBonusBg,
                                        out var bonusRt, out var infoBonusLine);
            infoBonusLine.color = UiTheme.TowerBonusLine;
            var infoBonus = UiKit.Label("Value", bonusRt, fonts.Body, UiTheme.Py(12f), UiTheme.Ink,
                                        TextAlignmentOptions.TopLeft, out var ibLblRt, "");
            infoBonus.textWrappingMode = TextWrappingModes.Normal;
            Anchor(ibLblRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(9f), -UiTheme.Py(43f)),
                   new Vector2(-UiTheme.Px(9f), -UiTheme.Py(7f)));
            var infoBonusSub = UiKit.Label("Sub", bonusRt, fonts.Body, UiTheme.Py(10.5f),
                                           UiTheme.Ink2, TextAlignmentOptions.TopLeft,
                                           out var ibsRt, "");
            infoBonusSub.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(ibsRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(9f), -UiTheme.Py(43f) - UiTheme.Row(UiTheme.Py(10.5f), UiTheme.Py(15f))),
                   new Vector2(-UiTheme.Px(9f), -UiTheme.Py(43f)));

            // 먼지 칸 줄(원본 `.tw-foes`): 72x81 칸이 왼쪽부터 늘어선다.
            var infoFoes = UiKit.Node("Foes", ibRt);
            Anchor(infoFoes, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(16f), -UiTheme.Py(297.8f)),
                   new Vector2(-UiTheme.Px(16f), -UiTheme.Py(216.8f)));
            var foesLayout = UiKit.Row(infoFoes, UiTheme.Px(6f), TextAnchor.MiddleLeft);
            foesLayout.childControlWidth = false;
            foesLayout.childControlHeight = false;
            foesLayout.childForceExpandWidth = false;

            var infoReward = UiKit.Label("Reward", ibRt, fonts.Body, UiTheme.Py(12f), UiTheme.Ink,
                                         TextAlignmentOptions.TopLeft, out var irwRt, "");
            infoReward.textWrappingMode = TextWrappingModes.Normal;
            Anchor(irwRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(16f), -UiTheme.Py(348f)),
                   new Vector2(-UiTheme.Px(16f), -UiTheme.Py(309.8f)));

            var infoBtns = UiKit.Node("Buttons", ibRt);
            Anchor(infoBtns, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(UiTheme.Px(16f), UiTheme.Py(15.4f)),
                   new Vector2(-UiTheme.Px(16f), UiTheme.Py(63.4f)));
            var infoBack = UiKit.Button("Back", infoBtns, fonts.Body, "층 목록",
                                        UiButtonKind.Secondary, out var ibkRt, UiTheme.Py(48f));
            Anchor(ibkRt, new Vector2(0f, 0f), new Vector2(0.48f, 1f), Vector2.zero, Vector2.zero);
            var infoStart = UiKit.Button("Start", infoBtns, fonts.Body, "편성하기",
                                         UiButtonKind.Primary, out var istRt, UiTheme.Py(48f));
            Anchor(istRt, new Vector2(0.52f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            infoSheet.gameObject.SetActive(false);

            // ---- 알림: 판 가운데 떠오르는 글
            var bannerRt = UiKit.Node("Banner", rt);
            Anchor(bannerRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                   new Vector2(-UiTheme.Px(130f), -UiTheme.Py(360f)),
                   new Vector2(UiTheme.Px(130f), -UiTheme.Py(290f)));
            var bannerBg = bannerRt.gameObject.AddComponent<Image>();
            bannerBg.sprite = GeneratedArt.RoundedNineSlice("ui_modal14", Mathf.RoundToInt(UiTheme.Px(14f)));
            bannerBg.type = Image.Type.Sliced;
            bannerBg.color = new Color(UiTheme.Ink.r, UiTheme.Ink.g, UiTheme.Ink.b, 0.88f);
            bannerBg.raycastTarget = false;
            var bannerTitle = UiKit.Label("Title", bannerRt, fonts.Title, UiTheme.Py(26f),
                                          Color.white, TextAlignmentOptions.Center,
                                          out var btRt, "");
            bannerTitle.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(btRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(0f, -UiTheme.Py(44f)), new Vector2(0f, -UiTheme.Py(8f)));
            var bannerNote = UiKit.Label("Note", bannerRt, fonts.Body, UiTheme.Py(12f),
                                         UiTheme.DarkInk2, TextAlignmentOptions.Center,
                                         out var bnRt, "");
            bannerNote.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(bnRt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(0f, UiTheme.Py(6f)), new Vector2(0f, UiTheme.Py(24f)));
            bannerRt.gameObject.SetActive(false);

            // ---- 결과 판
            var result = UiKit.Panel("Result", rt, out var resRt,
                                     new Color(UiTheme.Ink.r, UiTheme.Ink.g, UiTheme.Ink.b, 0.55f), 1f);
            Stretch(resRt);
            UiKit.Panel("Card", resRt, out var cardRt, UiTheme.Panel, UiTheme.Px(14f));
            Anchor(cardRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                   new Vector2(-UiTheme.Px(165f), -UiTheme.Py(120f)),
                   new Vector2(UiTheme.Px(165f), UiTheme.Py(120f)));

            var resTitle = UiKit.Label("Title", cardRt, fonts.Title, UiTheme.Py(22f), UiTheme.Ink,
                                       TextAlignmentOptions.Center, out var rtRt, "");
            resTitle.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(rtRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(18f), -UiTheme.Py(52f)), new Vector2(-UiTheme.Px(18f), -UiTheme.Py(18f)));

            var resStats = UiKit.Label("Stats", cardRt, fonts.Number, UiTheme.Py(14f), UiTheme.Ink2,
                                       TextAlignmentOptions.Center, out var rsRt, "");
            resStats.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(rsRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(18f), -UiTheme.Py(78f)), new Vector2(-UiTheme.Px(18f), -UiTheme.Py(54f)));

            var resRewards = UiKit.Label("Rewards", cardRt, fonts.Body, UiTheme.Py(12.5f), UiTheme.Ink,
                                         TextAlignmentOptions.TopLeft, out var rwRt, "");
            resRewards.textWrappingMode = TextWrappingModes.Normal;
            Anchor(rwRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(18f), -UiTheme.Py(136f)), new Vector2(-UiTheme.Px(18f), -UiTheme.Py(84f)));

            var resNote = UiKit.Label("Note", cardRt, fonts.Body, UiTheme.Py(11.5f), UiTheme.Ink2,
                                      TextAlignmentOptions.TopLeft, out var rnRt, "");
            resNote.textWrappingMode = TextWrappingModes.Normal;
            Anchor(rnRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(18f), -UiTheme.Py(176f)), new Vector2(-UiTheme.Px(18f), -UiTheme.Py(140f)));

            var resBtns = UiKit.Node("Buttons", cardRt);
            Anchor(resBtns, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(UiTheme.Px(18f), UiTheme.Py(16f)),
                   new Vector2(-UiTheme.Px(18f), UiTheme.Py(64f)));
            var resHome = UiKit.Button("Home", resBtns, fonts.Body, "로비로", UiButtonKind.Secondary,
                                       out var rhRt, UiTheme.Py(48f));
            Anchor(rhRt, new Vector2(0f, 0f), new Vector2(0.48f, 1f), Vector2.zero, Vector2.zero);
            var resAgain = UiKit.Button("Again", resBtns, fonts.Body, "다시 하기", UiButtonKind.Primary,
                                        out var raRt, UiTheme.Py(48f));
            Anchor(raRt, new Vector2(0.52f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            result.gameObject.SetActive(false);

            // ---- 단계 고르기 시트. 원본 `lgEnter` 는 로비 위 모달이고, 우리는 리그 화면
            // 안에 두어 '다시 하기' 에서도 같은 자리로 돌아오게 했다.
            var tierSheet = UiKit.Panel("TierPick", rt, out var tierRt,
                                        new Color(UiTheme.Ink.r, UiTheme.Ink.g, UiTheme.Ink.b, 0.55f), 1f);
            Stretch(tierRt);
            var tierBox = UiKit.Panel("Box", tierRt, out var tbRt, UiTheme.Panel);
            tierBox.sprite = GeneratedArt.RoundedNineSlice("ui_modal16",
                Mathf.RoundToInt(UiTheme.Px(16f)));
            tierBox.type = Image.Type.Sliced;
            Anchor(tbRt, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                   new Vector2(pad, -UiTheme.Py(290f)), new Vector2(-pad, UiTheme.Py(290f)));

            var tierHead = UiKit.Label("Title", tbRt, fonts.Title, UiTheme.Py(24f), UiTheme.Ink,
                                       TextAlignmentOptions.Left, out var thRt, "가전 대청소 리그");
            tierHead.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(thRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(16f), -UiTheme.Py(16f) - UiTheme.Line(UiTheme.Py(24f))),
                   new Vector2(-UiTheme.Px(16f), -UiTheme.Py(16f)));

            var tierLead = UiKit.Label("Lead", tbRt, fonts.Body, UiTheme.Py(12f), UiTheme.Ink2,
                                       TextAlignmentOptions.TopLeft, out var tlRt,
                                       "상점에서 가전을 사서 아래 칸에 놓으면, 전투는 자동이에요. " +
                                       "같은 가전 3장이면 ★ 승급. 청결도가 0이 되면 탈락, 8라운드를 버티면 완주예요.");
            tierLead.textWrappingMode = TextWrappingModes.Normal;
            Anchor(tlRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(16f), -UiTheme.Py(106f)),
                   new Vector2(-UiTheme.Px(16f), -UiTheme.Py(52f)));

            var tierList = UiKit.Node("List", tbRt);
            Anchor(tierList, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(16f), -UiTheme.Py(394f)),
                   new Vector2(-UiTheme.Px(16f), -UiTheme.Py(112f)));
            var tierCol = tierList.gameObject.AddComponent<VerticalLayoutGroup>();
            tierCol.spacing = UiTheme.Py(8f);
            tierCol.childControlWidth = true;
            tierCol.childControlHeight = false;
            tierCol.childForceExpandWidth = true;
            tierCol.childForceExpandHeight = false;

            var tierNote = UiKit.Label("Note", tbRt, fonts.Body, UiTheme.Py(11.5f), UiTheme.Ink2,
                                       TextAlignmentOptions.TopLeft, out var tnRt, "");
            tierNote.textWrappingMode = TextWrappingModes.Normal;
            Anchor(tnRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(16f), -UiTheme.Py(458f)),
                   new Vector2(-UiTheme.Px(16f), -UiTheme.Py(402f)));

            var tierBtns = UiKit.Node("Buttons", tbRt);
            Anchor(tierBtns, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(UiTheme.Px(16f), UiTheme.Py(16f)),
                   new Vector2(-UiTheme.Px(16f), UiTheme.Py(64f)));
            var tierClose = UiKit.Button("Close", tierBtns, fonts.Body, "닫기", UiButtonKind.Secondary,
                                         out var tcRt, UiTheme.Py(48f));
            Anchor(tcRt, new Vector2(0f, 0f), new Vector2(0.48f, 1f), Vector2.zero, Vector2.zero);
            var tierGo = UiKit.Button("Go", tierBtns, fonts.Body, "시작", UiButtonKind.Primary,
                                      out var tgRt, UiTheme.Py(48f));
            Anchor(tgRt, new Vector2(0.52f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            var tierGoLabel = tgRt.GetComponentInChildren<TextMeshProUGUI>();
            tierSheet.gameObject.SetActive(false);

            var screen = go.AddComponent<LeagueScreen>();
            Wire(screen,
                 ("tierRoot", tierSheet.gameObject), ("tierListRoot", tierList),
                 ("tierPrefab", prefabs.LeagueTier), ("tierNoteLabel", tierNote),
                 ("tierCloseButton", tierClose), ("tierGoButton", tierGo),
                 ("tierGoLabel", tierGoLabel),
                 ("view", view), ("boardCamera", cam), ("cellRoot", cellRoot),
                 ("cellPrefab", prefabs.LeagueCell),
                 ("chipLabel", chip), ("lifeLabel", life), ("goldLabel", gold),
                 ("levelLabel", lvLabel), ("xpLabel", xp), ("xpFill", xpFill),
                 ("speedButton", speed), ("exitButton", exit),
                 ("synergyRoot", synRow), ("bodyFont", fonts.Body),
                 ("chipSprite", GeneratedArt.RoundedNineSlice("ui_chip10",
                     Mathf.RoundToInt(UiTheme.Px(10f)))),
                 ("infoLeftLabel", infoLeft), ("infoRightLabel", infoRight), ("hintLabel", hint),
                 ("benchRoot", benchRow), ("benchPrefab", prefabs.LeagueBench),
                 ("rerollButton", reroll), ("rerollLabel", rerollLabel),
                 ("xpButton", xpBtn), ("xpButtonLabel", xpBtnLabel),
                 ("sellButton", sell), ("sellLabel", sellLabel),
                 ("shopRoot", shopRow), ("shopPrefab", prefabs.LeagueShop),
                 ("goButton", goBtn), ("goLabel", goLabel),
                 ("bannerRoot", bannerRt.gameObject), ("bannerTitle", bannerTitle),
                 ("bannerNote", bannerNote),
                 ("resultRoot", result.gameObject), ("resultTitle", resTitle),
                 ("resultStats", resStats), ("resultRewards", resRewards), ("resultNote", resNote),
                 ("resultHomeButton", resHome), ("resultAgainButton", resAgain),
                 ("sprites", library),
                 // ---- 먼지 타워
                 ("kickerLabel", kicker),
                 ("leagueReadoutRoot", readouts.gameObject),
                 ("towerReadoutRoot", twRo.gameObject),
                 ("towerFloorValue", twFloorValue), ("towerGoalCaption", twGoalCaption),
                 ("towerGoalValue", twGoalValue), ("towerUseValue", twUseValue),
                 ("ruleRoot", ruleRow.gameObject), ("ruleFace", ruleFace), ("ruleLine", ruleLine),
                 ("ruleNameLabel", ruleName), ("ruleDescLabel", ruleDesc),
                 ("ruleBonusLabel", ruleBonus), ("ruleTopRow", ruleTop),
                 ("ruleNameLayout", ruleNameLe),
                 ("ownRoot", ownRow), ("ownPrefab", prefabs.TowerOwn), ("autoButton", autoBtn),
                 ("floorSheetRoot", floorSheet.gameObject), ("floorListRoot", floorList),
                 ("floorPrefab", prefabs.TowerFloor), ("floorScroll", floorScroll),
                 ("floorHeadLabel", floorHead), ("floorCountLabel", floorCount),
                 ("floorNoteLabel", floorNote), ("floorRoofLabel", roof),
                 ("floorCloseButton", floorClose), ("floorGoButton", floorGo),
                 ("floorGoLabel", floorGoLabel),
                 ("infoSheetRoot", infoSheet.gameObject), ("infoTitleLabel", infoTitle),
                 ("infoStateLabel", infoState), ("infoRuleFace", infoRule),
                 ("infoRuleLine", infoRuleLine), ("infoRuleNameLabel", infoRuleName),
                 ("infoRuleDescLabel", infoRuleDesc), ("infoGoalLabel", infoGoal),
                 ("infoGoalSubLabel", infoGoalSub), ("infoBonusFace", infoBonusFace),
                 ("infoBonusLine", infoBonusLine), ("infoBonusLabel", infoBonus),
                 ("infoBonusSubLabel", infoBonusSub), ("infoFoesRoot", infoFoes),
                 ("infoRewardLabel", infoReward), ("infoBackButton", infoBack),
                 ("infoStartButton", infoStart),
                 ("hudRoot", hudRt),
                 ("foeCardSprite", GeneratedArt.RoundedNineSlice("tw_foe",
                     Mathf.RoundToInt(UiTheme.Px(10f)))),
                 ("barRoot", barRow), ("infoRoot", infoRow), ("goRoot", goRow),
                 ("hintRoot", hintRow));
            go.SetActive(false);
            return screen;
        }

        static GameObject BuildUnitsPanel(RectTransform parent, Fonts fonts, Prefabs prefabs,
                                          SpriteLibrary library, out UnitsTab tab)
        {
            var panel = UiKit.Node("Units", parent);
            Stretch(panel);
            float side = UiTheme.Px(16f);

            var viewport = UiKit.Node("Viewport", panel);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            // 뷰포트에 그래픽이 없으면 빈 자리 드래그와 휠이 ScrollRect 까지 닿지 않는다.
            UiKit.ScrollCatcher(viewport);
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            var column = UiKit.Node("Column", viewport);
            Anchor(column, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            column.pivot = new Vector2(0.5f, 1f);
            var col = column.gameObject.AddComponent<VerticalLayoutGroup>();
            col.spacing = UiTheme.Py(12f);
            col.padding = new RectOffset((int)side, (int)side, (int)UiTheme.Py(14f), (int)UiTheme.Py(20f));
            col.childControlWidth = true;
            col.childControlHeight = true;
            col.childForceExpandWidth = true;
            col.childForceExpandHeight = false;
            var colFit = column.gameObject.AddComponent<ContentSizeFitter>();
            colFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = column;

            RectTransform Block(string id, float pageHeight)
            {
                var rt = UiKit.Node(id, column);
                var le = rt.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = UiTheme.Py(pageHeight);
                le.minHeight = le.preferredHeight;
                return rt;
            }

            // ---- heading: title, owned count, and the two dark shortcuts.
            var head = Block("Head", 33f);
            UiKit.Label("Title", head, fonts.Title, UiTheme.Py(24f), UiTheme.Ink,
                        TextAlignmentOptions.BottomLeft, out var htRt, "가전 도감");
            Anchor(htRt, Vector2.zero, new Vector2(0f, 1f),
                   Vector2.zero, new Vector2(UiTheme.Px(86f), 0f));
            var countLabel = UiKit.Label("Count", head, fonts.Body, UiTheme.Py(12f), UiTheme.Ink2,
                                         TextAlignmentOptions.BottomLeft, out var hcRt, "");
            Anchor(hcRt, Vector2.zero, new Vector2(0f, 1f),
                   new Vector2(UiTheme.Px(89f), 0f),
                   new Vector2(UiTheme.Px(180f), -UiTheme.Py(3f)));

            var galleryButton = UiKit.Button("Gallery", head, fonts.Body, "일러스트",
                                             UiButtonKind.DarkSolid, out var gbRt,
                                             UiTheme.Py(30f), UiTheme.Py(12f));
            Anchor(gbRt, new Vector2(1f, 1f), new Vector2(1f, 1f),
                   new Vector2(-UiTheme.Px(150f), -UiTheme.Py(33f)),
                   new Vector2(-UiTheme.Px(83f), -UiTheme.Py(3f)));
            var gearBagButton = UiKit.Button("GearBag", head, fonts.Body, "장비 가방",
                                             UiButtonKind.DarkSolid, out var gbagRt,
                                             UiTheme.Py(30f), UiTheme.Py(12f));
            Anchor(gbagRt, new Vector2(1f, 1f), new Vector2(1f, 1f),
                   new Vector2(-UiTheme.Px(70f), -UiTheme.Py(33f)),
                   new Vector2(0f, -UiTheme.Py(3f)));

            // ---- parts: four upgrade parts and the gear stone, each a pale pill.
            var parts = Block("Parts", 70f);
            var partsGrid = parts.gameObject.AddComponent<GridLayoutGroup>();
            partsGrid.cellSize = new Vector2(UiTheme.Px(83f), UiTheme.Py(32f));
            partsGrid.spacing = new Vector2(UiTheme.Sq(6f), UiTheme.Sq(6f));
            partsGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            partsGrid.constraintCount = 4;

            // ---- catalogue: every appliance, four to a row.
            var grid = UiKit.Node("Grid", column);
            var gridLayout = grid.gameObject.AddComponent<GridLayoutGroup>();
            gridLayout.cellSize = new Vector2(UiTheme.Px(94f), UiTheme.Py(137f));
            gridLayout.spacing = new Vector2(UiTheme.Px(7.5f), UiTheme.Py(8f));
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = 4;
            var gridFit = grid.gameObject.AddComponent<ContentSizeFitter>();
            gridFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var note = Block("Note", 44f);
            var noteLabel = UiKit.Label("Text", note, fonts.Body, UiTheme.Py(12f), UiTheme.Ink2,
                                        TextAlignmentOptions.TopLeft, out var nlRt,
                                        "가전을 누르면 레벨 업 · 돌파 · 장비를 관리할 수 있어요. " +
                                        "같은 가전을 다시 뽑으면 그 가전 조각이 쌓여요.");
            noteLabel.textWrappingMode = TextWrappingModes.Normal;
            Stretch(nlRt);

            tab = panel.gameObject.AddComponent<UnitsTab>();
            Wire(tab, ("countLabel", countLabel), ("partsRoot", parts),
                 ("galleryButton", galleryButton), ("gearBagButton", gearBagButton),
                 ("rosterRoot", grid), ("cardPrefab", prefabs.UnitCard),
                 ("sprites", library), ("bodyFont", fonts.Body),
                 ("partPillSprite", GeneratedArt.RoundedNineSlice("ui_pill9",
                     Mathf.RoundToInt(UiTheme.Px(9f)))),
                 ("partOutlineSprite", GeneratedArt.RoundedOutlineNineSlice("ui_pill9_line",
                     Mathf.RoundToInt(UiTheme.Px(9f)),
                     Mathf.Max(1, Mathf.RoundToInt(UiTheme.Px(1f))))));
            WireArray(tab, "partIcons", new Object[]
            {
                LoadIcon("part_filter"), LoadIcon("part_coil"), LoadIcon("part_chip"),
                LoadIcon("part_motor"), LoadIcon("stone"),
            });
            return panel.gameObject;
        }

        /// <summary>
        /// 편성: six slots over the owned appliances. Tapping a slot drops that appliance,
        /// tapping one below adds it - the note under the slots says exactly that.
        /// </summary>
        static GameObject BuildDeckPanel(RectTransform parent, Fonts fonts, Prefabs prefabs,
                                         SpriteLibrary library, out DeckTab tab)
        {
            var panel = UiKit.Node("Deck", parent);
            Stretch(panel);
            float side = UiTheme.Px(16f);

            var viewport = UiKit.Node("Viewport", panel);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            // 뷰포트에 그래픽이 없으면 빈 자리 드래그와 휠이 ScrollRect 까지 닿지 않는다.
            UiKit.ScrollCatcher(viewport);
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            var column = UiKit.Node("Column", viewport);
            Anchor(column, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            column.pivot = new Vector2(0.5f, 1f);
            var col = column.gameObject.AddComponent<VerticalLayoutGroup>();
            col.spacing = UiTheme.Py(12f);
            col.padding = new RectOffset((int)side, (int)side, (int)UiTheme.Py(14f), (int)UiTheme.Py(20f));
            col.childControlWidth = true;
            col.childControlHeight = true;
            col.childForceExpandWidth = true;
            col.childForceExpandHeight = false;
            var colFit = column.gameObject.AddComponent<ContentSizeFitter>();
            colFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = column;

            RectTransform Block(string id, float pageHeight)
            {
                var rt = UiKit.Node(id, column);
                var le = rt.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = UiTheme.Py(pageHeight);
                le.minHeight = le.preferredHeight;
                return rt;
            }

            // Measured gaps on this page are 20 / 10 / 12, so the blocks carry the extra
            // rather than the column using one spacing for all three.
            var head = Block("Head", 38f);
            UiKit.Label("Title", head, fonts.Title, UiTheme.Py(24f), UiTheme.Ink,
                        TextAlignmentOptions.BottomLeft, out var dhRt, "편성");
            Anchor(dhRt, Vector2.zero, new Vector2(0f, 1f),
                   Vector2.zero, new Vector2(UiTheme.Px(42f), 0f));
            var deckCount = UiKit.Label("Count", head, fonts.Body, UiTheme.Py(12f), UiTheme.Ink2,
                                        TextAlignmentOptions.BottomLeft, out var dcRt, "");
            Anchor(dcRt, Vector2.zero, new Vector2(0f, 1f),
                   new Vector2(UiTheme.Px(45f), 0f),
                   new Vector2(UiTheme.Px(120f), -UiTheme.Py(3f)));

            var slots = Block("Slots", 70f);
            // Six 40-wide slots spread across 398 leaves 31.6 between them (space-between).
            var slotRow = UiKit.Row(slots, UiTheme.Px(31.6f), TextAnchor.MiddleLeft);
            slotRow.childForceExpandWidth = false;
            slotRow.childForceExpandHeight = false;
            slotRow.childControlWidth = false;
            slotRow.childControlHeight = false;

            var deckNote = Block("Note", 38f);
            var dnLabel = UiKit.Label("Text", deckNote, fonts.Body, UiTheme.Py(12f), UiTheme.Ink2,
                                      TextAlignmentOptions.TopLeft, out var dnRt,
                                      "슬롯을 누르면 빠지고, 아래 가전을 누르면 들어가요. " +
                                      "바닥에 서는 근접 가전과 가구 위에 서는 원거리·지원 가전을 섞어 편성하세요.");
            dnLabel.textWrappingMode = TextWrappingModes.Normal;
            Stretch(dnRt);

            var pool = UiKit.Node("Pool", column);
            var poolGrid = pool.gameObject.AddComponent<GridLayoutGroup>();
            poolGrid.cellSize = new Vector2(UiTheme.Px(94f), UiTheme.Py(137f));
            poolGrid.spacing = new Vector2(UiTheme.Px(7.5f), UiTheme.Py(8f));
            poolGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            poolGrid.constraintCount = 4;
            var poolFit = pool.gameObject.AddComponent<ContentSizeFitter>();
            poolFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            tab = panel.gameObject.AddComponent<DeckTab>();
            Wire(tab, ("countLabel", deckCount), ("slotRoot", slots),
                 ("slotPrefab", prefabs.DeckSlot), ("poolRoot", pool),
                 ("cardPrefab", prefabs.UnitCard), ("sprites", library));
            panel.gameObject.SetActive(false);
            return panel.gameObject;
        }
        /// <summary>
        /// 소환, measured off the running build: a 가전/장비 segmented control over a dark
        /// 16px banner card - copy, three featured appliances, the rates, and the two pull
        /// buttons - then the 소환 목록 that spells out every entry's exact chance.
        /// </summary>
        static GameObject BuildGachaPanel(RectTransform parent, Fonts fonts, Prefabs prefabs,
                                          SpriteLibrary library, out GachaTab tab)
        {
            var panel = UiKit.Node("Gacha", parent);
            Stretch(panel);
            float side = UiTheme.Px(16f);

            var viewport = UiKit.Node("Viewport", panel);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            // 뷰포트에 그래픽이 없으면 빈 자리 드래그와 휠이 ScrollRect 까지 닿지 않는다.
            UiKit.ScrollCatcher(viewport);
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            var column = UiKit.Node("Column", viewport);
            Anchor(column, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            column.pivot = new Vector2(0.5f, 1f);
            var col = column.gameObject.AddComponent<VerticalLayoutGroup>();
            col.spacing = UiTheme.Py(12f);
            col.padding = new RectOffset((int)side, (int)side, (int)UiTheme.Py(14f), (int)UiTheme.Py(20f));
            col.childControlWidth = true;
            col.childControlHeight = true;
            col.childForceExpandWidth = true;
            col.childForceExpandHeight = false;
            var colFit = column.gameObject.AddComponent<ContentSizeFitter>();
            colFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = column;

            RectTransform Block(string id, float pageHeight)
            {
                var rt = UiKit.Node(id, column);
                var le = rt.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = le.minHeight = UiTheme.Py(pageHeight);
                return rt;
            }

            // ---- 가전 소환 / 장비 소환, on a sunken ground.
            var seg = Block("Mode", 47f);
            var segBg = seg.gameObject.AddComponent<Image>();
            segBg.sprite = GeneratedArt.RoundedNineSlice("ui_slot", Mathf.RoundToInt(UiTheme.Px(10f)));
            segBg.type = Image.Type.Sliced;
            segBg.color = UiTheme.Panel2;
            segBg.raycastTarget = false;

            UiButton Half(string id, bool left, out TextMeshProUGUI label, out Image plate)
            {
                var b = UiKit.Button(id, seg, fonts.Body, "", UiButtonKind.Glass,
                                     out var rt, UiTheme.Py(41f), UiTheme.Py(14f));
                float x0 = left ? UiTheme.Px(3f) : UiTheme.Px(181f);
                Anchor(rt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                       new Vector2(x0, -UiTheme.Py(20.5f)),
                       new Vector2(x0 + UiTheme.Px(174f), UiTheme.Py(20.5f)));
                plate = rt.GetComponent<Image>();
                if (plate != null)
                {
                    plate.sprite = GeneratedArt.RoundedNineSlice("ui_chip8",
                        Mathf.RoundToInt(UiTheme.Px(8f)));
                    plate.type = Image.Type.Sliced;
                    plate.color = new Color(0f, 0f, 0f, 0f);
                }
                label = rt.GetComponentInChildren<TextMeshProUGUI>();
                return b;
            }
            var unitMode = Half("UnitMode", true, out var unitModeLabel, out var unitModePlate);
            var gearMode = Half("GearMode", false, out var gearModeLabel, out var gearModePlate);

            // ---- the dark banner.
            var banner = Block("Banner", 309f);
            var bannerBg = banner.gameObject.AddComponent<Image>();
            bannerBg.sprite = GeneratedArt.RoundedNineSlice("ui_modal", Mathf.RoundToInt(UiTheme.Px(16f)));
            bannerBg.type = Image.Type.Sliced;
            bannerBg.color = UiTheme.DarkPanel;

            float inset = UiTheme.Px(16f);
            RectTransform BannerRow(string id, float top, float height)
            {
                var rt = UiKit.Node(id, banner);
                Anchor(rt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(inset, -UiTheme.Py(top + height)),
                       new Vector2(-inset, -UiTheme.Py(top)));
                return rt;
            }

            var bannerTitle = UiKit.Label("Title", BannerRow("Title", 16f, 38f), fonts.Title,
                                          UiTheme.Py(21f), UiTheme.DarkInk,
                                          TextAlignmentOptions.Left, out var btRt, "");
            bannerTitle.textWrappingMode = TextWrappingModes.NoWrap;
            Stretch(btRt);

            var bannerDesc = UiKit.Label("Desc", BannerRow("Desc", 54f, 40f), fonts.Body,
                                         UiTheme.Py(12.5f), UiTheme.DarkInk2,
                                         TextAlignmentOptions.TopLeft, out var bdRt, "");
            bannerDesc.textWrappingMode = TextWrappingModes.Normal;
            Stretch(bdRt);

            var featured = BannerRow("Featured", 106f, 85f);
            var featRow = UiKit.Row(featured, UiTheme.Px(10f), TextAnchor.UpperLeft);
            featRow.childControlWidth = false;
            featRow.childControlHeight = false;
            featRow.childForceExpandWidth = false;

            var rates = BannerRow("Rates", 203f, 16f);
            var rateRow = UiKit.Row(rates, UiTheme.Px(12f), TextAnchor.MiddleLeft);
            rateRow.childControlWidth = false;
            rateRow.childControlHeight = false;
            rateRow.childForceExpandWidth = false;

            var pulls = BannerRow("Pulls", 230f, 63f);
            var pullGrid = pulls.gameObject.AddComponent<GridLayoutGroup>();
            pullGrid.cellSize = new Vector2(UiTheme.Px(159f), UiTheme.Py(63f));
            pullGrid.spacing = new Vector2(UiTheme.Px(8f), 0f);
            pullGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            pullGrid.constraintCount = 2;

            UiButton PullButton(string id, UiButtonKind kind,
                                out TextMeshProUGUI label, out TextMeshProUGUI cost)
            {
                var b = UiKit.Button(id, pulls, fonts.Body, "", kind, out var rt,
                                     UiTheme.Py(63f), UiTheme.Py(15f));
                label = rt.GetComponentInChildren<TextMeshProUGUI>();
                label.fontStyle = FontStyles.Bold;
                var lrt = (RectTransform)label.transform;
                Anchor(lrt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(UiTheme.Px(8f), -UiTheme.Py(38f)),
                       new Vector2(-UiTheme.Px(8f), -UiTheme.Py(10f)));
                cost = UiKit.Label("Cost", rt, fonts.Body, UiTheme.Py(12f),
                                   kind == UiButtonKind.Primary ? UiTheme.Ink : UiTheme.DarkInk,
                                   TextAlignmentOptions.Center, out var crt, "");
                cost.textWrappingMode = TextWrappingModes.NoWrap;
                cost.overflowMode = TextOverflowModes.Ellipsis;
                Anchor(crt, Vector2.zero, new Vector2(1f, 0f),
                       new Vector2(UiTheme.Px(6f), UiTheme.Py(8f)),
                       new Vector2(-UiTheme.Px(6f), UiTheme.Py(32f)));
                return b;
            }
            // 원본 `.pull.one` 은 `--panel-2`(#2A3136) — 배너 카드보다 밝다.
            var single = PullButton("Single", UiButtonKind.DarkRaised,
                                    out var singleLabel, out var singleCost);
            var multi = PullButton("Multi", UiButtonKind.Primary,
                                   out var multiLabel, out var multiCost);

            // ---- 소환 목록
            var poolHead = Block("PoolHead", 30f);
            var poolHeading = UiKit.Label("Title", poolHead, fonts.Title, UiTheme.Py(24f),
                                          UiTheme.Ink, TextAlignmentOptions.BottomLeft,
                                          out var phRt, "소환 목록");
            Anchor(phRt, Vector2.zero, new Vector2(1f, 1f), Vector2.zero, Vector2.zero);

            var pool = UiKit.Node("Pool", column);
            var poolCol = pool.gameObject.AddComponent<VerticalLayoutGroup>();
            poolCol.spacing = UiTheme.Py(8f);
            poolCol.childControlWidth = true;
            poolCol.childControlHeight = true;
            poolCol.childForceExpandWidth = true;
            poolCol.childForceExpandHeight = false;
            var poolFit = pool.gameObject.AddComponent<ContentSizeFitter>();
            poolFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            tab = panel.gameObject.AddComponent<GachaTab>();
            Wire(tab,
                 ("unitModeButton", unitMode), ("unitModeLabel", unitModeLabel),
                 ("unitModePlate", unitModePlate),
                 ("gearModeButton", gearMode), ("gearModeLabel", gearModeLabel),
                 ("gearModePlate", gearModePlate),
                 ("bannerTitleLabel", bannerTitle), ("bannerDescLabel", bannerDesc),
                 ("featuredRoot", featured), ("rateRoot", rates),
                 ("singleButton", single), ("singleLabel", singleLabel),
                 ("singleCostLabel", singleCost),
                 ("multiButton", multi), ("multiLabel", multiLabel),
                 ("multiCostLabel", multiCost),
                 ("poolHeadingLabel", poolHeading), ("poolRoot", pool),
                 ("cellSprite", GeneratedArt.RoundedNineSlice("ui_card",
                     Mathf.RoundToInt(UiTheme.Px(12f)))),
                 ("chipSprite", GeneratedArt.RoundedNineSlice("ui_chip8",
                     Mathf.RoundToInt(UiTheme.Px(8f)))),
                 ("bodyFont", fonts.Body), ("numberFont", fonts.Number),
                 ("sprites", library));
            return panel.gameObject;
        }


        // ------------------------------------------------------------------ 상점 (2e)
        /// <summary>
        /// 상점: a sunken three-way category bar over a two-column product grid. The ad
        /// product spans both columns, which is what the build's `.sp.ad.wide` does.
        /// </summary>
        static GameObject BuildShopPanel(RectTransform parent, Fonts fonts, Prefabs prefabs,
                                         out ShopTab tab)
        {
            var panel = UiKit.Node("Shop", parent);
            Stretch(panel);
            float side = UiTheme.Px(16f);

            var viewport = UiKit.Node("Viewport", panel);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            // 뷰포트에 그래픽이 없으면 빈 자리 드래그와 휠이 ScrollRect 까지 닿지 않는다.
            UiKit.ScrollCatcher(viewport);
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            var column = UiKit.Node("Column", viewport);
            Anchor(column, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            column.pivot = new Vector2(0.5f, 1f);
            var col = column.gameObject.AddComponent<VerticalLayoutGroup>();
            col.spacing = UiTheme.Py(12f);
            col.padding = new RectOffset((int)side, (int)side, (int)UiTheme.Py(14f), (int)UiTheme.Py(20f));
            col.childControlWidth = true;
            col.childControlHeight = true;
            col.childForceExpandWidth = true;
            col.childForceExpandHeight = false;
            var colFit = column.gameObject.AddComponent<ContentSizeFitter>();
            colFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = column;

            // ---- category bar: a pale ground with three chips in it.
            var tabs = UiKit.Node("Tabs", column);
            var tabsLe = tabs.gameObject.AddComponent<LayoutElement>();
            tabsLe.preferredHeight = tabsLe.minHeight = UiTheme.Py(47f);
            var tabsBg = tabs.gameObject.AddComponent<Image>();
            tabsBg.sprite = GeneratedArt.RoundedNineSlice("ui_shopcard", Mathf.RoundToInt(UiTheme.Px(12f)));
            tabsBg.type = Image.Type.Sliced;
            tabsBg.color = UiTheme.Panel;
            tabsBg.raycastTarget = false;

            var tabRow = UiKit.Node("Row", tabs);
            Stretch(tabRow, UiTheme.Px(4f));
            var tabGrid = tabRow.gameObject.AddComponent<GridLayoutGroup>();
            tabGrid.cellSize = new Vector2(UiTheme.Px(112f), UiTheme.Py(37f));
            tabGrid.spacing = new Vector2(UiTheme.Px(6f), 0f);
            tabGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            tabGrid.constraintCount = 3;

            // ---- product grid. Two columns; ShopTab widens the ad card to span both.
            var grid = UiKit.Node("Grid", column);
            // 원본 `.shop-grid` 는 2열 CSS 그리드지만 `.sp.wide` 가 두 칸을 통째로 먹는다.
            // GridLayoutGroup 으로는 그 폭이 안 나오므로 **행을 쌓는 세로 배치**로 둔다.
            // 행은 ShopTab 이 만들고, 한 행 안에서 카드가 폭을 나눠 갖는다.
            var gridLayout = grid.gameObject.AddComponent<VerticalLayoutGroup>();
            gridLayout.spacing = UiTheme.Sq(10f);
            gridLayout.childForceExpandWidth = true;
            gridLayout.childControlWidth = true;
            gridLayout.childForceExpandHeight = false;
            gridLayout.childControlHeight = true;
            var gridFit = grid.gameObject.AddComponent<ContentSizeFitter>();
            gridFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var emptyRow = UiKit.Node("Empty", column);
            var emptyLe = emptyRow.gameObject.AddComponent<LayoutElement>();
            emptyLe.preferredHeight = emptyLe.minHeight = UiTheme.Py(38f);
            var emptyLabel = UiKit.Label("Text", emptyRow, fonts.Body, UiTheme.Py(12f),
                                         UiTheme.Ink2, TextAlignmentOptions.TopLeft,
                                         out var emRt, "");
            emptyLabel.textWrappingMode = TextWrappingModes.Normal;
            Stretch(emRt);

            var noteRow = UiKit.Node("Note", column);
            var noteLe = noteRow.gameObject.AddComponent<LayoutElement>();
            noteLe.preferredHeight = noteLe.minHeight = UiTheme.Py(50f);
            var note = UiKit.Label("Text", noteRow, fonts.Body, UiTheme.Py(11f), UiTheme.Ink2,
                                   TextAlignmentOptions.TopLeft, out var ntRt,
                                   "현금 결제와 광고는 테스트 공급자로만 동작해요. " +
                                   "실제 스토어 결제와 광고 SDK 는 아직 연결되어 있지 않아요.");
            note.textWrappingMode = TextWrappingModes.Normal;
            Stretch(ntRt);

            tab = panel.gameObject.AddComponent<ShopTab>();
            Wire(tab, ("tabRoot", tabRow), ("tabChipPrefab", prefabs.DarkChip),
                 ("gridRoot", grid), ("cardPrefab", prefabs.ShopCard),
                 ("emptyLabel", emptyLabel));
            // Wire 는 UnityEngine.Object 만 나른다. 숫자는 직접 넣는다.
            WireValues(tab, ("rowSpacing", UiTheme.Sq(10f)));
            return panel.gameObject;
        }


        /// <summary>The two-segment control shared by the stage and 가전 tabs.</summary>
        static (RectTransform, UiToggleChip, UiToggleChip) BuildSegmentPair(
            RectTransform panel, Fonts fonts, float top, string left, string right)
        {
            float segH = UiTheme.Py(40f);
            float band = segH + UiTheme.Py(8f);
            var segs = UiKit.Surface("SubTabs", panel, UiTheme.Panel2, UiTheme.Px(14f), out var segRt);
            segs.raycastTarget = false;
            Anchor(segRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.ScreenPad, -top - band), new Vector2(-UiTheme.ScreenPad, -top));
            var row = UiKit.Row(segRt, UiTheme.Px(4f), TextAnchor.MiddleCenter);
            row.padding = new RectOffset((int)UiTheme.Px(4f), (int)UiTheme.Px(4f),
                                         (int)UiTheme.Py(4f), (int)UiTheme.Py(4f));
            row.childForceExpandWidth = true;
            row.childForceExpandHeight = true;
            return (segRt, BuildSegment("Left", segRt, fonts, left),
                    BuildSegment("Right", segRt, fonts, right));
        }

        /// <summary>
        /// A tab that has no screen yet. It says so plainly rather than showing an empty
        /// panel, so an unfinished area is never mistaken for a broken one.
        /// </summary>
        static GameObject BuildPlaceholderPanel(RectTransform parent, Fonts fonts, string id,
                                                string title, string body)
        {
            var panel = UiKit.Node(id, parent);
            Stretch(panel);

            UiKit.Panel("Card", panel, out var card, UiTheme.Panel, UiTheme.PanelCorner);
            Anchor(card, new Vector2(0.08f, 0.5f), new Vector2(0.92f, 0.5f),
                   new Vector2(0f, -UiTheme.Py(90f)), new Vector2(0f, UiTheme.Py(90f)));

            UiKit.Label("Title", card, fonts.Title, UiTheme.Title, UiTheme.Ink,
                        TextAlignmentOptions.Center, out var trt, title);
            Anchor(trt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.S16, -UiTheme.S24 - UiTheme.Line(UiTheme.Title)),
                   new Vector2(-UiTheme.S16, -UiTheme.S24));

            var text = UiKit.Label("Body", card, fonts.Body, UiTheme.Body, UiTheme.Ink2,
                                   TextAlignmentOptions.Center, out var brt, body);
            Anchor(brt, new Vector2(0f, 0f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.S16, UiTheme.S24),
                   new Vector2(-UiTheme.S16, -UiTheme.S24 - UiTheme.Line(UiTheme.Title) - UiTheme.S8));
            text.textWrappingMode = TextWrappingModes.Normal;
            return panel.gameObject;
        }

        /// <summary>
        /// The selected-unit tray from redesign 1c: what is selected on the left, then boost,
        /// multitap and retreat, then a close button. Sits in the same band as the card row
        /// and is toggled against it, so the battle is never covered by a modal.
        /// </summary>
        static UnitActionBar BuildUnitActionBar(Transform parent, Fonts fonts)
        {
            var root = UiKit.Node("UnitActionBar", parent);
            // Uses the card row and the hint band: the hint tells the player how to place a
            // card, which is not what they are doing while a unit is selected. 배치 카운터
            // 줄까지 먹지는 않는다 — 그 줄은 가전을 고른 동안에도 남는다.
            Anchor(root, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(UiTheme.S10, UiTheme.Py(932f - 870.1f)),
                   new Vector2(-UiTheme.S10, UiTheme.Py(932f - 753f)));
            var bar = root.gameObject.AddComponent<UnitActionBar>();

            var layout = UiKit.Row(root, UiTheme.S8);
            layout.childForceExpandHeight = true;

            // Left read-out.
            UiKit.Panel("Info", root, out var inforRt, UiTheme.Panel2, UiTheme.CornerFor(UiTheme.ButtonTall));
            var infoFitter = inforRt.gameObject.AddComponent<LayoutElement>();
            // 환산 안 된 300 이었다. 기둥이 좁아지며 글자가 더 빨리 잘렸다.
            infoFitter.preferredWidth = UiTheme.Px(150f);
            infoFitter.flexibleWidth = 1f;

            // Stacked down from the top on real line heights, so none of the three is ever
            // shorter than the glyphs it has to draw.
            float cursor = UiTheme.S8;
            TextMeshProUGUI InfoLine(string id, float size, Color ink, string placeholder)
            {
                float h = UiTheme.Line(size);
                var tmp = UiKit.Label(id, inforRt, fonts.Body, size, ink,
                                      TextAlignmentOptions.Left, out var lrt, placeholder);
                // 가전 이름과 '레벨 · 자리' 는 칸보다 길어지기 쉽다. 잘라 내기보다
                // 줄여서 다 보여 준다 — '발전기 …' 로는 어느 가전인지 알 수 없다.
                tmp.enableAutoSizing = true;
                tmp.fontSizeMin = size * 0.75f;
                tmp.fontSizeMax = size;
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

        /// <summary>
        /// 협동 전투의 두 줄 — 파트너 말풍선과 조작 줄. 원본 10.19-e 의 `.co-bar` / `.rd-bar` 다.
        ///
        /// 트레이 바로 위에 올라앉고, 스테이지 판에서는 꺼져 있다. 원본도 `coopHud(on)` 으로
        /// 같은 전투 화면에서 켰다 껐다 한다.
        /// </summary>
        static CoopBars BuildCoopBars(Transform parent, Fonts fonts, Prefabs prefabs,
                                      SpriteLibrary library)
        {
            float faceSize = UiTheme.Sq(40f);
            float barPadX = UiTheme.Px(9f), barPadY = UiTheme.Py(5f);
            float sayLine = UiTheme.Line(UiTheme.Px(12f));
            float topLine = UiTheme.Line(UiTheme.Px(15f));
            float coH = barPadY * 2f + topLine + sayLine;

            float sumH = UiTheme.Py(52f);
            float mergeH = UiTheme.Line(UiTheme.Px(11.5f));
            float upH = UiTheme.Py(62f);
            float gap = UiTheme.Py(6f);
            float rdH = sumH + mergeH + upH + gap * 2f;

            var root = UiKit.Node("CoopBars", parent);
            // 협동에서는 카드 트레이가 숨으므로 그 자리를 그대로 쓴다.
            Anchor(root, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(UiTheme.Px(12f), UiTheme.Py(10f)),
                   new Vector2(-UiTheme.Px(12f), UiTheme.Py(10f) + coH + rdH + gap));

            // ---------------------------------------------------------- 파트너 줄
            var coBar = UiKit.Surface("PartnerBar", root, new Color(0.918f, 0.953f, 0.988f),
                                      UiTheme.Px(12f), out var coRt);
            Anchor(coRt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -coH), Vector2.zero);

            var faceBg = UiKit.Surface("Face", coRt, Color.white, UiTheme.Px(10f), out var faceRt);
            Anchor(faceRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                   new Vector2(barPadX, -faceSize * 0.5f),
                   new Vector2(barPadX + faceSize, faceSize * 0.5f));
            var faceIcon = UiKit.Node("Icon", faceRt);
            UiKit.Stretch(faceIcon, UiTheme.Px(4f));
            var faceImage = faceIcon.gameObject.AddComponent<Image>();
            faceImage.preserveAspect = true;

            float textLeft = barPadX * 2f + faceSize;

            var nameLabel = UiKit.Label("Name", coRt, fonts.Title, UiTheme.Px(15f),
                                        new Color(0.122f, 0.306f, 0.502f),
                                        TextAlignmentOptions.BottomLeft, out var nameRt, "\ud30c\ud2b8\ub108");
            Anchor(nameRt, new Vector2(0f, 1f), new Vector2(0f, 1f),
                   new Vector2(textLeft, -barPadY - topLine),
                   new Vector2(textLeft + UiTheme.Px(100f), -barPadY));

            var styleLabel = UiKit.Label("Style", coRt, fonts.Body, UiTheme.Px(11f),
                                         new Color(0.239f, 0.416f, 0.600f),
                                         TextAlignmentOptions.BottomLeft, out var styleRt, "");
            styleLabel.fontStyle = FontStyles.Bold;
            Anchor(styleRt, new Vector2(0f, 1f), new Vector2(0f, 1f),
                   new Vector2(textLeft + UiTheme.Px(104f), -barPadY - topLine),
                   new Vector2(textLeft + UiTheme.Px(190f), -barPadY));

            var statsLabel = UiKit.Label("Stats", coRt, fonts.Number, UiTheme.Px(12f),
                                         new Color(0.122f, 0.306f, 0.502f),
                                         TextAlignmentOptions.BottomRight, out var statsRt, "");
            statsLabel.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(statsRt, new Vector2(1f, 1f), new Vector2(1f, 1f),
                   new Vector2(-UiTheme.Px(150f) - barPadX, -barPadY - topLine),
                   new Vector2(-barPadX, -barPadY));

            var sayLabel = UiKit.Label("Say", coRt, fonts.Body, UiTheme.Px(12f), UiTheme.Ink2,
                                       TextAlignmentOptions.Left, out var sayRt, "");
            sayLabel.textWrappingMode = TextWrappingModes.NoWrap;
            sayLabel.overflowMode = TextOverflowModes.Ellipsis;
            Anchor(sayRt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(textLeft, barPadY), new Vector2(-barPadX, barPadY + sayLine));

            // ---------------------------------------------------------- 조작 줄
            var rdRoot = UiKit.Node("ControlBar", root);
            Anchor(rdRoot, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, rdH));

            // .rd-row 는 1fr 92px 두 칸이다.
            float reW = UiTheme.Px(92f), colGap = UiTheme.Px(6f);

            var summon = UiKit.Button("Summon", rdRoot, fonts.Title, "\uac00\uc804 \uc18c\ud658",
                                      UiButtonKind.Primary, out var sumRt, sumH, UiTheme.Px(21f));
            Anchor(sumRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(0f, -sumH), new Vector2(-reW - colGap, 0f));
            var sumCost = UiKit.Label("Cost", sumRt, fonts.Number, UiTheme.Px(16f), UiTheme.Ink,
                                      TextAlignmentOptions.Right, out var sumCostRt, "100W");
            float sumCostLine = UiTheme.Line(UiTheme.Px(16f));
            Anchor(sumCostRt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                   new Vector2(-UiTheme.Px(84f), -sumCostLine * 0.5f),
                   new Vector2(-UiTheme.Px(10f), sumCostLine * 0.5f));
            sumRt.gameObject.AddComponent<CanvasGroup>();

            var reroll = UiKit.Button("Reroll", rdRoot, fonts.Title, "\ubc14\uafb8\uae30",
                                      UiButtonKind.Secondary, out var reRt, sumH, UiTheme.Px(17f));
            Anchor(reRt, new Vector2(1f, 1f), new Vector2(1f, 1f),
                   new Vector2(-reW, -sumH), Vector2.zero);
            var reCost = UiKit.Label("Cost", reRt, fonts.Body, UiTheme.Px(10.5f), UiTheme.Ink2,
                                     TextAlignmentOptions.Center, out var reCostRt,
                                     "\uac00\uc804 \uace0\ub974\uae30");
            float reCostLine = UiTheme.Line(UiTheme.Px(10.5f));
            Anchor(reCostRt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(UiTheme.Px(4f), UiTheme.Py(5f)),
                   new Vector2(-UiTheme.Px(4f), UiTheme.Py(5f) + reCostLine));
            reRt.gameObject.AddComponent<CanvasGroup>();

            var merge = UiKit.Label("Merge", rdRoot, fonts.Body, UiTheme.Px(11.5f), UiTheme.Ink2,
                                    TextAlignmentOptions.Center, out var mergeRt, "");
            Anchor(mergeRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(0f, -sumH - gap - mergeH), new Vector2(0f, -sumH - gap));

            var ups = UiKit.Node("Upgrades", rdRoot);
            Anchor(ups, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, upH));
            var upRow = UiKit.Row(ups, UiTheme.Px(5f), TextAnchor.MiddleCenter);
            upRow.childForceExpandWidth = true;
            upRow.childControlWidth = true;
            upRow.childForceExpandHeight = true;
            upRow.childControlHeight = true;

            var bars = root.gameObject.AddComponent<CoopBars>();
            Wire(bars,
                 ("partnerBar", coBar), ("partnerFace", faceImage),
                 ("partnerName", nameLabel), ("partnerStyle", styleLabel),
                 ("partnerStats", statsLabel), ("partnerSay", sayLabel),
                 ("summonButton", summon), ("summonCost", sumCost),
                 ("rerollButton", reroll), ("rerollCost", reCost),
                 ("mergeLine", merge), ("upgradeRow", ups),
                 ("upgradeCellPrefab", prefabs.CoopUpgradeCell), ("sprites", library));

            summon.SetAction(bars.OnSummon);
            reroll.SetAction(bars.OnReroll);

            root.gameObject.SetActive(false);
            return bars;
        }

        static BattleScreen BuildBattleUI(Transform parent, Shapes art, Fonts fonts, Prefabs prefabs,
                                          BoardView board, FxDirector fx, Camera cam,
                                          SpriteLibrary library)
        {
            var go = UI("Battle", parent, out var rt);
            Stretch(rt);

            // ---- HUD, measured off the running 10.16-n build: one dark 14px plate that
            // carries the stage name and three controls on top, and three LED read-outs
            // under it. There is no wave counter and no cleanliness bar - 10.16-n prints
            // cleanliness as a life COUNT and shows wave progress on the board instead.
            float pad = UiTheme.Px(16f);
            float hudH = UiTheme.Py(113f);
            var hud = UiKit.Panel("Hud", go.transform, out var hudrt, UiTheme.DarkPanel,
                                  UiTheme.Px(14f));
            Anchor(hudrt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(pad, -UiTheme.Py(12f) - hudH), new Vector2(-pad, -UiTheme.Py(12f)));

            float inset = UiTheme.Px(12f);

            // Row 1: "1-1 거실" with the number in the small LED face, then the controls.
            var topRow = UiKit.Node("Top", hudrt);
            Anchor(topRow, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(inset, -UiTheme.Py(10f) - UiTheme.Py(30f)),
                   new Vector2(-inset, -UiTheme.Py(10f)));

            var stageNo = UiKit.Label("StageNo", topRow, fonts.Number, UiTheme.Py(13f),
                                      UiTheme.DarkInk2, TextAlignmentOptions.Left,
                                      out var snrt, "1-1");
            stageNo.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(snrt, new Vector2(0f, 0f), new Vector2(0f, 1f),
                   Vector2.zero, new Vector2(UiTheme.Px(34f), 0f));

            var stageName = UiKit.Label("StageName", topRow, fonts.Title, UiTheme.Py(19f),
                                        UiTheme.DarkInk, TextAlignmentOptions.Left,
                                        out var sgrt, "거실");
            stageName.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(sgrt, new Vector2(0f, 0f), new Vector2(0.5f, 1f),
                   new Vector2(UiTheme.Px(40f), 0f), Vector2.zero);

            var ctrls = UiKit.Node("Ctrls", topRow);
            Anchor(ctrls, new Vector2(0.4f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            var ctrlRow = UiKit.Row(ctrls, UiTheme.Px(6f), TextAnchor.MiddleRight);
            ctrlRow.childControlWidth = true;
            ctrlRow.childControlHeight = false;
            ctrlRow.childForceExpandWidth = false;

            UiButton Ctrl(string id, string text, out TextMeshProUGUI label)
            {
                var btn = UiKit.Button(id, ctrls, fonts.Body, text, UiButtonKind.DarkSolid,
                                       out var crt, UiTheme.Py(30f), UiTheme.Py(12f));
                var plate = crt.GetComponent<Image>();
                if (plate != null)
                {
                    plate.sprite = GeneratedArt.RoundedNineSlice("ui_chip8",
                        Mathf.RoundToInt(UiTheme.Px(8f)));
                    plate.type = Image.Type.Sliced;
                    plate.color = UiTheme.DarkPanel2;
                }
                label = crt.GetComponentInChildren<TextMeshProUGUI>();
                label.fontStyle = FontStyles.Bold;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                var le = crt.gameObject.AddComponent<LayoutElement>();
                le.minHeight = le.preferredHeight = UiTheme.Py(30f);
                // 18px of padding either side is what the build's 6px/9px gives at this scale.
                le.preferredWidth = label.GetPreferredValues(text).x + UiTheme.Px(18f);
                return btn;
            }
            var speed = Ctrl("Speed", "1×", out var speedLabel);
            var pause = Ctrl("Pause", "일시정지", out var pauseLabel);
            var exitBtn = Ctrl("Exit", "나가기", out _);

            // Row 2: 청결도 | 전력 | 남은 적. The middle one is elastic; the outer two hug
            // their numbers, which is what the build's grid-template-columns resolves to.
            var readouts = UiKit.Node("Readouts", hudrt);
            Anchor(readouts, new Vector2(0f, 0f), new Vector2(1f, 1f),
                   new Vector2(inset, UiTheme.Py(12f)),
                   new Vector2(-inset, -UiTheme.Py(10f) - UiTheme.Py(30f) - UiTheme.Py(10f)));
            var roRow = UiKit.Row(readouts, UiTheme.Px(12f), TextAnchor.LowerLeft);
            roRow.childControlWidth = true;
            roRow.childControlHeight = true;
            roRow.childForceExpandWidth = false;
            roRow.childForceExpandHeight = true;

            RectTransform Readout(string id, string caption, float minWidth, bool flexible,
                                  Color ledColor, TextAlignmentOptions align,
                                  out TextMeshProUGUI label, out TextMeshProUGUI value)
            {
                var rt = UiKit.Node(id, readouts);
                var le = rt.gameObject.AddComponent<LayoutElement>();
                le.minWidth = minWidth;
                le.preferredWidth = minWidth;
                le.flexibleWidth = flexible ? 1f : 0f;

                label = UiKit.Label("Label", rt, fonts.Body, UiTheme.Py(10f), UiTheme.DarkInk2,
                                    align, out var lrt, caption);
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.characterSpacing = 8f;   // the build's 0.8px tracking at 10px
                // 협동은 캡션이 '고리 위 가전' 처럼 길어 칸을 넘친다. 말줄임으로 '고리…'
                // 라고 적느니 살짝 줄여 다 보여 준다.
                label.enableAutoSizing = true;
                label.fontSizeMin = UiTheme.Py(10f) * 0.75f;
                label.fontSizeMax = UiTheme.Py(10f);
                // 원본은 캡션을 읽기칸 맨 위가 아니라 7px 내려 놓는다(430 기준에서 쟀다:
                // 캡션 ink 가 y=69, 우리는 62 였다). 숫자는 이미 맞으므로 캡션만 내린다.
                float capTop = UiTheme.Py(7f);
                Anchor(lrt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(0f, -capTop - UiTheme.Py(15f)), new Vector2(0f, -capTop));

                // CSS crops a 26px line to a 26px box; TMP refuses to and drops the whole
                // line instead. So the box is 44px - comfortably over the ~80px TMP asks
                // for - and is CENTRED on where the build puts the digits (18 + 26/2 = 31
                // below the read-out's top). The extra box overlaps the caption's box, but
                // neither draws a background so nothing shows.
                float digitMid = UiTheme.Py(31f), digitBox = UiTheme.Py(44f);
                value = UiKit.Label("Value", rt, fonts.Number, UiTheme.Py(26f), ledColor,
                                    align, out var vrt, "0");
                value.textWrappingMode = TextWrappingModes.NoWrap;
                Anchor(vrt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(0f, -digitMid - digitBox * 0.5f),
                       new Vector2(0f, -digitMid + digitBox * 0.5f));
                return rt;
            }

            var lifeRo = Readout("Life", "청결도", UiTheme.Px(30f), false, UiTheme.LedLife,
                    TextAlignmentOptions.Left, out var cleanCaption, out var cleanLabel);

            var powerRo = Readout("Power", "전력 · 초당 +100W",
                                  UiTheme.Px(120f), true, UiTheme.Led,
                                  TextAlignmentOptions.Left, out var regen, out var powerValue);
            // The meter sits under the number, 4px tall, in a track the colour of burnt amber.
            var powerBarRt = UiKit.Bar("Meter", powerRo, UiTheme.LedTrack, UiTheme.Led,
                                       UiTheme.Py(4f), out var powerFill);
            Anchor(powerBarRt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   Vector2.zero, new Vector2(0f, UiTheme.Py(4f)));

            var remainRo = Readout("Remain", "남은 적", UiTheme.Px(34f), false, UiTheme.Led,
                    TextAlignmentOptions.Right, out var remainCaption, out var remainValue);

            // ---- 협동 두 줄. 평소엔 꺼져 있고 협동 판에서만 켠다.
            var coopBars = BuildCoopBars(go.transform, fonts, prefabs, library);

            // ---- tray: the deploy counter and multitap offer, then the card row, then the hint.
            // 원본 전투 화면의 아래 줄(430x932 실측, 화면 위에서 잰 값):
            //   배치/멀티탭 713~745 · 카드 753~840.9 · 안내문 850.9~870.1
            // 여기서는 화면 **아래에서** 재므로 932 에서 뺀 값을 쓴다.
            const float SlotsTop = 932f - 713f;      // 219
            const float SlotsBottom = 932f - 745f;   // 187
            const float CardsTop = 932f - 753f;      // 179
            const float CardsBottom = 932f - 840.9f; // 91.1
            const float HintTop = 932f - 850.9f;     // 81.1
            const float HintBottom = 932f - 870.1f;  // 61.9

            var deckBg = UiKit.Node("DeckBar", go.transform);
            Anchor(deckBg, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero,
                   new Vector2(0f, UiTheme.Py(SlotsTop)));
            var dbrt = deckBg;

            float hintH = UiTheme.Py(HintTop - HintBottom);
            float cardH = UiTheme.Py(CardsTop - CardsBottom);
            float slotH = UiTheme.Py(SlotsTop - SlotsBottom);

            var slotsRow = UiKit.Node("Slots", dbrt);
            Anchor(slotsRow, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(pad, UiTheme.Py(SlotsBottom)),
                   new Vector2(-pad, UiTheme.Py(SlotsTop)));

            var slotCount = UiKit.Label("Count", slotsRow, fonts.Body, UiTheme.Py(12f),
                                        UiTheme.Ink2, TextAlignmentOptions.Left,
                                        out var slcrt, "배치 0/8");
            slotCount.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(slcrt, new Vector2(0f, 0f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);

            var multitap = UiKit.Button("Multitap", slotsRow, fonts.Body, "멀티탭",
                                        UiButtonKind.Secondary, out var mtrt,
                                        slotH, UiTheme.Py(12f));
            var mtPlate = mtrt.GetComponent<Image>();
            if (mtPlate != null)
            {
                mtPlate.sprite = GeneratedArt.RoundedNineSlice("ui_pill",
                    Mathf.RoundToInt(slotH * 0.5f));
                mtPlate.type = Image.Type.Sliced;
            }
            var multitapLabel = mtrt.GetComponentInChildren<TextMeshProUGUI>();
            multitapLabel.fontStyle = FontStyles.Bold;
            multitapLabel.textWrappingMode = TextWrappingModes.NoWrap;
            // 원본 `#btnTap` 은 255.5,713 에 158.5x32 — 오른쪽 끝에 붙는다.
            Anchor(mtrt, new Vector2(1f, 0f), new Vector2(1f, 1f),
                   new Vector2(-UiTheme.Px(158.5f), 0f), Vector2.zero);

            var deckRoot = UiKit.Node("DeckRoot", dbrt);
            Anchor(deckRoot, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(pad, UiTheme.Py(CardsBottom)),
                   new Vector2(-pad, UiTheme.Py(CardsTop)));
            // A grid, not a row: 10.16-n lays the hand out in equal columns and wraps to two
            // rows past six cards rather than letting the strip scroll off the screen.
            var deckGrid = deckRoot.gameObject.AddComponent<GridLayoutGroup>();
            deckGrid.spacing = new Vector2(UiTheme.Px(5f), UiTheme.Px(5f));
            deckGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            deckGrid.constraintCount = 4;
            // 원본 카드: 16 / 116.8 / 217.5 / 318.3 에 95.8 폭, 사이 5.
            deckGrid.cellSize = new Vector2(UiTheme.Px(95.8f), cardH);

            // ---- unit action bar (redesign 1c): replaces the card row while a unit is selected
            var actionBar = BuildUnitActionBar(dbrt, fonts);

            var hint = UiKit.Label("Hint", dbrt, fonts.Body, UiTheme.Py(12f), UiTheme.Ink2,
                                   TextAlignmentOptions.Center, out var hrt, "");
            Anchor(hrt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(pad, UiTheme.Py(HintBottom)),
                   new Vector2(-pad, UiTheme.Py(HintTop)));

            // ---- tutorial bubble
            // 말풍선은 **판 아래, 배치 줄 위**에 띄운다. 전에는 212~352 라는 환산 안 된
            // 원시 픽셀이라 카드 트레이를 덮었다 — '청소기 카드를 누르세요' 라면서
            // 그 카드를 가리고 있었다.
            var tut = Panel("Tutorial", go.transform, new Color(0.1f, 0.12f, 0.14f, 0.95f), out var turt);
            Anchor(turt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(pad, UiTheme.Py(SlotsTop) + UiTheme.Py(8f)),
                   new Vector2(-pad, UiTheme.Py(SlotsTop) + UiTheme.Py(8f) + UiTheme.Py(74f)));
            var tutText = Label("TutText", tut.transform, fonts.Body, UiTheme.Py(13f), Color.white,
                                TextAlignmentOptions.TopLeft, out var ttrt, "");
            Anchor(ttrt, new Vector2(0f, 0f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(10f), UiTheme.Py(26f)),
                   new Vector2(-UiTheme.Px(10f), -UiTheme.Py(7f)));
            tutText.enableWordWrapping = true;

            // 이 버튼들도 원시 픽셀이었다. 말풍선 안에서 재도록 바꾼다.
            var tutOk = Btn("TutOk", tut.transform, art, fonts.Body, "확인", Accent, Color.white,
                            UiTheme.Py(13f), out var tokrt, out _);
            Anchor(tokrt, new Vector2(1f, 0f), new Vector2(1f, 0f),
                   new Vector2(-UiTheme.Px(72f), UiTheme.Py(5f)),
                   new Vector2(-UiTheme.Px(8f), UiTheme.Py(23f)));
            var tutOff = Btn("TutOff", tut.transform, art, fonts.Body, "튜토리얼 끄기",
                             new Color(1f, 1f, 1f, 0.18f), Color.white, UiTheme.Py(11f),
                             out var toffrt, out _);
            Anchor(toffrt, new Vector2(0f, 0f), new Vector2(0f, 0f),
                   new Vector2(UiTheme.Px(8f), UiTheme.Py(5f)),
                   new Vector2(UiTheme.Px(90f), UiTheme.Py(23f)));
            tut.gameObject.SetActive(false);

            // ---- overlay
            var overlay = Panel("Overlay", go.transform, new Color(0.05f, 0.06f, 0.07f, 0.72f), out var ovrt);
            Stretch(ovrt);
            // 원본 `.ov-card`: 폭 **330**(max-width), 안쪽 여백 18/18/16, 모서리 **14**,
            // 바탕 #F7F9FA, 그리고 **내용 높이**(flex column)다. 전에는 환산 안 된 원시
            // 픽셀(±280 등)에 각진 모서리라 카드가 네모나고 아래가 크게 비었다.
            var card = UiKit.Panel("Card", (RectTransform)overlay.transform, out var cdrt,
                                   UiTheme.Panel, UiTheme.Px(14f));
            cdrt.anchorMin = cdrt.anchorMax = new Vector2(0.5f, 0.5f);
            cdrt.pivot = new Vector2(0.5f, 0.5f);
            cdrt.sizeDelta = new Vector2(UiTheme.Px(330f), UiTheme.Py(220f));
            cdrt.anchoredPosition = Vector2.zero;

            float ovPad = UiTheme.Px(18f);
            var ovTitle = UiKit.Label("Title", cdrt, fonts.Title, UiTheme.Py(20f), UiTheme.Ink,
                                      TextAlignmentOptions.Center, out var otrt, "");
            Anchor(otrt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(ovPad, -UiTheme.Py(18f) - UiTheme.Line(UiTheme.Py(20f))),
                   new Vector2(-ovPad, -UiTheme.Py(18f)));

            var ovBody = UiKit.Label("Body", cdrt, fonts.Body, UiTheme.Py(13f), UiTheme.Ink2,
                                     TextAlignmentOptions.Top, out var obrt, "");
            ovBody.textWrappingMode = TextWrappingModes.Normal;
            Anchor(obrt, new Vector2(0f, 0f), new Vector2(1f, 1f),
                   new Vector2(ovPad, UiTheme.Py(16f) + UiTheme.ButtonTall + UiTheme.Py(12f)),
                   new Vector2(-ovPad, -UiTheme.Py(18f) - UiTheme.Line(UiTheme.Py(20f)) - UiTheme.Py(10f)));

            // 버튼은 `Button` 으로 배선돼 있으므로 기존 헬퍼를 그대로 쓴다. 자리만 환산한다.
            var ovPrimary = Btn("Primary", cdrt, art, fonts.Body, "시작", Accent, Color.white,
                                UiTheme.Py(15f), out var oprt, out var ovPrimaryLabel);
            Anchor(oprt, new Vector2(0.5f, 0f), new Vector2(1f, 0f),
                   new Vector2(UiTheme.S4, UiTheme.Py(16f)),
                   new Vector2(-ovPad, UiTheme.Py(16f) + UiTheme.ButtonTall));

            var ovSecondary = Btn("Secondary", cdrt, art, fonts.Body, "로비로", Muted, Color.white,
                                  UiTheme.Py(15f), out var osrt, out var ovSecondaryLabel);
            Anchor(osrt, new Vector2(0f, 0f), new Vector2(0.5f, 0f),
                   new Vector2(ovPad, UiTheme.Py(16f)),
                   new Vector2(-UiTheme.S4, UiTheme.Py(16f) + UiTheme.ButtonTall));
            overlay.gameObject.SetActive(false);

            // ---- brief panel (redesign 1c, first phase): sits where the HUD does, with its
            // own actions in the tray. The battle has not started, so neither is on screen.
            var briefRoot = UiKit.Node("Brief", go.transform);
            Stretch(briefRoot);

            var briefRt = UiKit.Node("Panel", briefRoot);
            Anchor(briefRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(0f, -UiTheme.Py(230f)), Vector2.zero);
            var briefBg = briefRt.gameObject.AddComponent<Image>();
            briefBg.sprite = UiArt.Panel;
            briefBg.type = Image.Type.Sliced;
            briefBg.color = UiTheme.Panel;
            briefBg.raycastTarget = false;

            float bpad = UiTheme.ScreenPad;
            float titleH = UiTheme.Line(UiTheme.Display);
            var briefTitle = UiKit.Label("Title", briefRt, fonts.Title, UiTheme.Display, UiTheme.Ink,
                                         TextAlignmentOptions.Left, out var btrt, "1-1 \uac70\uc2e4");
            Anchor(btrt, new Vector2(0f, 1f), new Vector2(0.55f, 1f),
                   new Vector2(bpad, -UiTheme.S12 - titleH), new Vector2(0f, -UiTheme.S12));
            var briefSub = UiKit.Label("Sub", briefRt, fonts.Body, UiTheme.Caption, UiTheme.Ink2,
                                       TextAlignmentOptions.Right, out var bsrt, "");
            Anchor(bsrt, new Vector2(0.55f, 1f), new Vector2(1f, 1f),
                   new Vector2(0f, -UiTheme.S12 - titleH), new Vector2(-bpad, -UiTheme.S12));

            float descTop = UiTheme.S12 + titleH + UiTheme.S6;
            float descH = UiTheme.Line(UiTheme.Caption) * 2f;
            var briefDesc = UiKit.Label("Desc", briefRt, fonts.Body, UiTheme.Caption, UiTheme.Ink2,
                                        TextAlignmentOptions.TopLeft, out var bdrt, "");
            Anchor(bdrt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(bpad, -descTop - descH), new Vector2(-bpad, -descTop));
            briefDesc.textWrappingMode = TextWrappingModes.Normal;

            // Star conditions, one chip each.
            float starTop = descTop + descH + UiTheme.S8;
            float starH = UiTheme.Py(34f);
            var starRow = UiKit.Node("Stars", briefRt);
            Anchor(starRow, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(bpad, -starTop - starH), new Vector2(-bpad, -starTop));
            var starLayout = UiKit.Row(starRow, UiTheme.S6);
            starLayout.childForceExpandWidth = true;
            starLayout.childForceExpandHeight = true;

            var starLabels = new TextMeshProUGUI[3];
            for (int i = 0; i < starLabels.Length; i++)
            {
                starLabels[i] = UiKit.Chip("Star" + i, starRow, fonts.Body, "", out var scrt,
                                           UiTheme.Panel2, UiTheme.Ink, starH, UiTheme.Caption);
                var le = scrt.gameObject.AddComponent<LayoutElement>();
                le.flexibleWidth = 1f;
                le.preferredHeight = starH;
            }

            float enemyTop = starTop + starH + UiTheme.S8;
            var briefEnemies = UiKit.Label("Enemies", briefRt, fonts.Body, UiTheme.Caption, UiTheme.Ink2,
                                           TextAlignmentOptions.Left, out var bert, "");
            Anchor(bert, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(bpad, -enemyTop - UiTheme.Line(UiTheme.Caption)),
                   new Vector2(-bpad, -enemyTop));

            // Brief actions live in the tray band, where the cards will be.
            var briefActions = UiKit.Node("BriefActions", briefRoot);
            Anchor(briefActions, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(0f, 0f), new Vector2(0f, UiTheme.ButtonTall + UiTheme.S16 * 2f));
            var briefActionsBg = briefActions.gameObject.AddComponent<Image>();
            briefActionsBg.color = UiTheme.Panel;
            briefActionsBg.raycastTarget = false;

            float deckW = UiTheme.Px(120f);
            var briefDeck = UiKit.Button("ChangeDeck", briefActions, fonts.Body,
                                         "\ud3b8\uc131 \ubcc0\uacbd", UiButtonKind.Secondary,
                                         out var bdkrt, UiTheme.ButtonTall, UiTheme.LabelSize);
            Anchor(bdkrt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                   new Vector2(bpad, -UiTheme.ButtonTall * 0.5f),
                   new Vector2(bpad + deckW, UiTheme.ButtonTall * 0.5f));

            var briefStart = UiKit.Button("Start", briefActions, fonts.Title,
                                          "\uc791\uc804 \uc2dc\uc791", UiButtonKind.Primary,
                                          out var bstrt, UiTheme.ButtonTall, UiTheme.Px(22f));
            Anchor(bstrt, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                   new Vector2(bpad + deckW + UiTheme.S8, -UiTheme.ButtonTall * 0.5f),
                   new Vector2(-bpad, UiTheme.ButtonTall * 0.5f));

            // 10.17-c pause menu. 나가기 during a battle opens this instead of arming a
            // second tap: a title, a line saying what leaving costs, then 계속하기 on its own
            // and 다시 하기 / 나가기 side by side under it - the original's button order.
            var pauseOverlay = UiKit.Panel("PauseOverlay", go.transform, out var port,
                                           new Color(0.05f, 0.06f, 0.07f, 0.72f), 1f);
            Stretch(port);
            UiKit.Panel("Card", port, out var pcrt, UiTheme.Panel, UiTheme.PanelCorner);
            // Height comes from the contents, not a guessed number: with a fixed card the
            // description landed UNDER the buttons and was invisible while every test still
            // passed - the label had characters, it was just covered.
            float pPad = UiTheme.S24;
            float pTitleH = UiTheme.Line(UiTheme.Title);
            float pDescH = UiTheme.Line(UiTheme.Body) * 2f;
            float pCardH = pPad + pTitleH + UiTheme.S8 + pDescH + UiTheme.S16
                           + UiTheme.ButtonTall + UiTheme.S8 + UiTheme.ButtonTall + pPad;
            Anchor(pcrt, new Vector2(0.1f, 0.5f), new Vector2(0.9f, 0.5f),
                   new Vector2(0f, -pCardH * 0.5f), new Vector2(0f, pCardH * 0.5f));
            var pauseTitle = UiKit.Label("Title", pcrt, fonts.Title, UiTheme.Title, UiTheme.Ink,
                        TextAlignmentOptions.Center, out var ptrt, "전투를 멈추었어요");
            Anchor(ptrt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.S16, -pPad - pTitleH), new Vector2(-UiTheme.S16, -pPad));
            var pauseDesc = UiKit.Label("Desc", pcrt, fonts.Body, UiTheme.Body, UiTheme.Ink2,
                        TextAlignmentOptions.Top, out var pdrt, string.Empty);
            pauseDesc.enableWordWrapping = true;
            Anchor(pdrt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.S16, -pPad - pTitleH - UiTheme.S8 - pDescH),
                   new Vector2(-UiTheme.S16, -pPad - pTitleH - UiTheme.S8));

            float prow2 = pPad, prow1 = prow2 + UiTheme.ButtonTall + UiTheme.S8;
            var pauseResume = UiKit.Button("Resume", pcrt, fonts.Body, "계속하기",
                                           UiButtonKind.Primary, out var prrt, UiTheme.ButtonTall);
            Anchor(prrt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(UiTheme.S16, prow1), new Vector2(-UiTheme.S16, prow1 + UiTheme.ButtonTall));
            var pauseRetry = UiKit.Button("Retry", pcrt, fonts.Body, "다시 하기",
                                          UiButtonKind.Secondary, out var prtrt, UiTheme.ButtonTall);
            Anchor(prtrt, new Vector2(0f, 0f), new Vector2(0.5f, 0f),
                   new Vector2(UiTheme.S16, prow2), new Vector2(-UiTheme.S6, prow2 + UiTheme.ButtonTall));
            var pauseExit = UiKit.Button("Exit", pcrt, fonts.Body, "나가기",
                                         UiButtonKind.Secondary, out var pxrt, UiTheme.ButtonTall);
            Anchor(pxrt, new Vector2(0.5f, 0f), new Vector2(1f, 0f),
                   new Vector2(UiTheme.S6, prow2), new Vector2(-UiTheme.S16, prow2 + UiTheme.ButtonTall));
            port.gameObject.SetActive(false);

            var battle = go.AddComponent<BattleScreen>();
            Wire(battle,
                 ("board", board), ("fx", fx), ("boardCamera", cam),
                 ("stageNoLabel", stageNo), ("stageNameLabel", stageName),
                 ("remainLabel", remainValue),
                 ("pauseButton", pause), ("pauseButtonLabel", pauseLabel),
                 ("speedButton", speed), ("speedButtonLabel", speedLabel),
                 ("exitButton", exitBtn),
                 ("cleanLabel", cleanLabel),
                 ("cleanCaptionLabel", cleanCaption), ("remainCaptionLabel", remainCaption),
                 ("coopBars", coopBars),
                 ("trayRoot", deckBg), ("slotsRow", slotsRow),
                 ("lifeReadout", lifeRo.GetComponent<LayoutElement>()),
                 ("remainReadout", remainRo.GetComponent<LayoutElement>()),
                 ("powerValueLabel", powerValue),
                 ("powerFill", powerFill), ("regenLabel", regen), ("hintLabel", hint),
                 ("slotCountLabel", slotCount), ("multitapButton", multitap),
                 ("multitapLabel", multitapLabel),
                 ("deckRoot", deckRoot), ("deckCardPrefab", prefabs.DeckCard),
                 ("unitActionBar", actionBar),
                 ("overlayRoot", overlay.gameObject), ("overlayTitle", ovTitle), ("overlayBody", ovBody),
                 ("overlayCard", cdrt),
                 ("overlayPrimary", ovPrimary), ("overlayPrimaryLabel", ovPrimaryLabel),
                 ("overlaySecondary", ovSecondary), ("overlaySecondaryLabel", ovSecondaryLabel),
                 ("tutorialRoot", tut.gameObject), ("tutorialText", tutText),
                 ("tutorialOk", tutOk), ("tutorialOff", tutOff));
            Wire(battle,
                 ("briefRoot", briefRoot.gameObject), ("briefPanelRect", briefRt),
                 ("briefActionsRect", briefActions), ("briefTitleLabel", briefTitle),
                 ("briefSubLabel", briefSub), ("briefDescLabel", briefDesc),
                 ("briefEnemyLabel", briefEnemies), ("briefStartButton", briefStart),
                 ("briefDeckButton", briefDeck));
            WireArray(battle, "briefStarLabels", starLabels);
            // The tray dims as a whole while the menu is up, the way .battle.pmenu greys the
            // slots, the power bar and the deck in the original.
            // TryGetComponent, not `GetComponent() ?? AddComponent()`: ?? compares with real
            // null and skips Unity's == overload, so the missing component came back as a
            // fake-null that satisfied ?? - nothing was added and the field wired to nothing.
            if (!dbrt.TryGetComponent<CanvasGroup>(out var trayGroup))
                trayGroup = dbrt.gameObject.AddComponent<CanvasGroup>();
            Wire(battle, ("hudRect", hudrt), ("trayRect", dbrt),
                 ("pauseOverlay", port.gameObject), ("pauseResumeButton", pauseResume),
                 ("pauseRetryButton", pauseRetry), ("pauseExitButton", pauseExit),
                 ("pauseTitleLabel", pauseTitle), ("pauseDescLabel", pauseDesc),
                 ("trayGroup", trayGroup));

            var resultScreen = BuildResultScreen((RectTransform)go.transform, fonts, library);
            Wire(battle, ("result", resultScreen));

            go.SetActive(false);
            return battle;
        }

        // ------------------------------------------------------------------ 대청소 (survival)

        /// <summary>
        /// Builds the survival camera and world root at scene level, same as the battle board:
        /// always alive, drawn under whichever screen's opaque panel is currently on top.
        /// </summary>
        static (Camera cam, SurvivorView view) BuildSurvivorWorld(Shapes art, Fonts fonts, Prefabs prefabs, SpriteLibrary library)
        {
            var camGo = new GameObject("SurvivorCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 4.6f;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.937f, 0.914f, 0.878f);

            // 전투판과 아레나는 둘 다 켜져 있다. 레이어로 갈라 두지 않으면 전투 카메라가
            // 아레나를 제 자리에서 한 번 더 그려 화면에 사본이 겹친다.
            cam.cullingMask = 1 << ArenaLayer;
            // 대청소를 하지 않는 동안에는 꺼 둔다. 켜 둔 채로는 화면 전체를 제 배경색으로
            // 칠하고 아레나 바닥까지 그려, 로비·전투의 좌우 여백에 그 무늬가 비쳤다.
            cam.enabled = false;

            var worldGo = new GameObject("SurvivorWorld");

            // 바닥이 먼저다 — 원본은 판자/격자/러그를 깔고 그 위에 가전과 먼지를 그린다.
            var floorGo = new GameObject("Floor");
            floorGo.transform.SetParent(worldGo.transform, false);
            var floor = floorGo.AddComponent<SpriteRenderer>();
            floor.sprite = GeneratedArt.ArenaFloor(Mathf.RoundToInt(SurvivorView.FloorBakedTiles));
            floor.sortingOrder = -1000;

            // 흡입 소용돌이 범위 — 바닥 위, 적·리더 아래다(원본도 그 순서로 그린다).
            SpriteRenderer Aura(string id, Sprite sprite, int order)
            {
                var go = new GameObject(id);
                go.transform.SetParent(worldGo.transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.sortingOrder = order;
                go.SetActive(false);
                return sr;
            }
            var auraFill = Aura("AuraFill", GeneratedArt.Disc("sv_aura_fill"), -900);
            var auraArcs = Aura("AuraArcs", GeneratedArt.AuraArcs("sv_aura_arcs"), -899);

            var entityRoot = new GameObject("Entities").transform;
            entityRoot.SetParent(worldGo.transform, false);

            var view = worldGo.AddComponent<SurvivorView>();
            Wire(view, ("entityRoot", entityRoot), ("entityPrefab", prefabs.Entity),
                 ("sprites", library), ("dotSprite", art.Circle), ("ringSprite", art.Circle),
                 ("floor", floor), ("auraFill", auraFill), ("auraArcs", auraArcs));

            SetLayerDeep(worldGo, ArenaLayer);

            return (cam, view);
        }
        /// <summary>
        /// 대청소, measured off 10.16-n. It is the battle screen's chassis with the board
        /// swapped for an open arena: the same dark 113px HUD plate, the same three LED
        /// read-outs, then the arena, a five-slot team strip and the hint line. The old
        /// build's beige top bar, level pill, separate HP and XP bars and milestone line are
        /// all gone - they fold into the read-outs.
        /// </summary>
        static SurvivorScreen BuildSurvivorUI(Transform parent, Shapes art, Fonts fonts, Prefabs prefabs,
                                              SurvivorView view, FxDirector fx, Camera cam, SpriteLibrary library)
        {
            var go = UI("Survivor", parent, out var rt);
            Stretch(rt);
            float pad = UiTheme.Px(16f);

            // ---- leader pick: the build opens a sheet over the lobby before the run starts.
            var leaderRoot = UiKit.Node("LeaderSelect", rt);
            Stretch(leaderRoot);
            var leaderBg = leaderRoot.gameObject.AddComponent<Image>();
            leaderBg.color = new Color(UiTheme.Ink.r, UiTheme.Ink.g, UiTheme.Ink.b, 0.55f);

            // 원본 `.mbox` 를 430x932 에서 직접 쟀다: x16 w398, 안쪽 여백 16, 줄 간격 12.
            // 높이는 **내용에 따라 변한다** — 청소기(설명 1줄) 504.7, 에어컨(2줄) 522.7 이고
            // 상자는 그때마다 세로 가운데로 다시 놓인다. 그래서 고정 좌표가 아니라
            // 세로 레이아웃 + ContentSizeFitter 로 짰다.
            const float SheetPad = 16f, SheetGap = 12f;
            var leaderBox = UiKit.Panel("Box", leaderRoot, out var lbRt, UiTheme.Panel);
            leaderBox.sprite = GeneratedArt.RoundedNineSlice("ui_modal16",
                Mathf.RoundToInt(UiTheme.Px(16f)));
            leaderBox.type = Image.Type.Sliced;
            Anchor(lbRt, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                   new Vector2(pad, -UiTheme.Py(260f)), new Vector2(-pad, UiTheme.Py(260f)));
            var sheetCol = lbRt.gameObject.AddComponent<VerticalLayoutGroup>();
            sheetCol.padding = new RectOffset(
                Mathf.RoundToInt(UiTheme.Px(SheetPad)), Mathf.RoundToInt(UiTheme.Px(SheetPad)),
                Mathf.RoundToInt(UiTheme.Py(SheetPad)), Mathf.RoundToInt(UiTheme.Py(SheetPad)));
            sheetCol.spacing = UiTheme.Py(SheetGap);
            sheetCol.childControlWidth = true;
            sheetCol.childControlHeight = true;
            sheetCol.childForceExpandWidth = true;
            sheetCol.childForceExpandHeight = false;
            var sheetFit = lbRt.gameObject.AddComponent<ContentSizeFitter>();
            sheetFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // 높이가 정해진 줄 하나.
            RectTransform SheetRow(string id, float height)
            {
                var r = UiKit.Node(id, lbRt);
                var le = r.gameObject.AddComponent<LayoutElement>();
                le.minHeight = le.preferredHeight = UiTheme.Py(height);
                return r;
            }

            // 높이가 글에 따라 변하는 줄.
            TextMeshProUGUI SheetText(string id, float size, Color ink, string text)
            {
                var r = UiKit.Node(id, lbRt);
                var col = r.gameObject.AddComponent<VerticalLayoutGroup>();
                col.childControlWidth = col.childControlHeight = true;
                col.childForceExpandWidth = true;
                col.childForceExpandHeight = false;
                var fit = r.gameObject.AddComponent<ContentSizeFitter>();
                fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                var t = UiKit.Label("Text", r, fonts.Body, UiTheme.Py(size), ink,
                                    TextAlignmentOptions.TopLeft, out _, text);
                t.textWrappingMode = TextWrappingModes.Normal;
                t.overflowMode = TextOverflowModes.Overflow;
                return t;
            }

            var headRow = SheetRow("Head", 30f);
            var leaderTitle = UiKit.Label("Title", headRow, fonts.Title, UiTheme.Py(24f), UiTheme.Ink,
                                          TextAlignmentOptions.Left, out var ltRt, "대청소 모드");
            leaderTitle.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(ltRt, new Vector2(0f, 0f), new Vector2(0f, 1f),
                   Vector2.zero, new Vector2(UiTheme.Px(130f), 0f));
            var headSub = UiKit.Label("Sub", headRow, fonts.Body, UiTheme.Py(12f), UiTheme.Ink2,
                                      TextAlignmentOptions.Left, out var hsRt, "쓰러질 때까지 버티기");
            headSub.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(hsRt, new Vector2(0f, 0f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(136f), 0f), Vector2.zero);

            SheetText("Note", 12f, UiTheme.Ink2,
                      "리더를 고르세요. 화면을 끌거나 WASD·방향키로 움직이고, 공격은 자동이에요.");

            var leaderList = SheetRow("List", 129.3f);
            var leaderGrid = leaderList.gameObject.AddComponent<GridLayoutGroup>();
            leaderGrid.spacing = new Vector2(UiTheme.Px(8f), UiTheme.Px(8f));
            leaderGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            leaderGrid.constraintCount = 4;
            leaderGrid.cellSize = new Vector2(UiTheme.Px(85.5f), UiTheme.Py(129.3f));

            // ---- `.sv-sel`: 고른 리더의 생존 스킬. --bg 바탕에 radius 10, padding 8/10.
            // 설명이 한 줄이면 57, 두 줄이면 75 — 원본처럼 글에 맞춰 늘어난다.
            var selRow = UiKit.Node("Skill", lbRt);
            var selBg = selRow.gameObject.AddComponent<Image>();
            selBg.sprite = GeneratedArt.RoundedNineSlice("ui_chip10",
                Mathf.RoundToInt(UiTheme.Px(10f)));
            selBg.type = Image.Type.Sliced;
            selBg.color = UiTheme.PageBg;
            selBg.raycastTarget = false;
            var selCol = selRow.gameObject.AddComponent<VerticalLayoutGroup>();
            selCol.padding = new RectOffset(
                Mathf.RoundToInt(UiTheme.Px(10f)), Mathf.RoundToInt(UiTheme.Px(10f)),
                Mathf.RoundToInt(UiTheme.Py(8f)), Mathf.RoundToInt(UiTheme.Py(8f)));
            selCol.spacing = UiTheme.Py(2f);
            selCol.childControlWidth = selCol.childControlHeight = true;
            selCol.childForceExpandWidth = true;
            selCol.childForceExpandHeight = false;
            var selFit = selRow.gameObject.AddComponent<ContentSizeFitter>();
            selFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var skillName = UiKit.Label("Name", selRow, fonts.Title, UiTheme.Py(14f), UiTheme.Ink,
                                        TextAlignmentOptions.Left, out var snRt, "");
            skillName.textWrappingMode = TextWrappingModes.NoWrap;
            var snLe = snRt.gameObject.AddComponent<LayoutElement>();
            snLe.minHeight = snLe.preferredHeight = UiTheme.Row(UiTheme.Py(14f), UiTheme.Py(21f));

            var skillDesc = UiKit.Label("Desc", selRow, fonts.Body, UiTheme.Py(12f), UiTheme.Ink2,
                                        TextAlignmentOptions.TopLeft, out var sdRt, "");
            skillDesc.textWrappingMode = TextWrappingModes.Normal;
            skillDesc.overflowMode = TextOverflowModes.Overflow;
            var sdFit = sdRt.gameObject.AddComponent<ContentSizeFitter>();
            sdFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // ---- `.deckstrip`: 동료 후보. --card 바탕 + 1.5 테두리, radius 14, 미니 34x34.
            var stripRow = SheetRow("Comps", 56f);
            var stripBg = stripRow.gameObject.AddComponent<Image>();
            stripBg.sprite = GeneratedArt.RoundedNineSlice("ui_modal14",
                Mathf.RoundToInt(UiTheme.Px(14f)));
            stripBg.type = Image.Type.Sliced;
            stripBg.color = UiTheme.Panel;
            stripBg.raycastTarget = false;
            var stripLine = UiKit.Node("Line", stripRow);
            Stretch(stripLine);
            var stripLineImg = stripLine.gameObject.AddComponent<Image>();
            stripLineImg.sprite = GeneratedArt.RoundedOutlineNineSlice("ui_modal14_line",
                Mathf.RoundToInt(UiTheme.Px(14f)), Mathf.Max(1, Mathf.RoundToInt(UiTheme.Px(1.5f))));
            stripLineImg.type = Image.Type.Sliced;
            stripLineImg.color = UiTheme.PanelLine;
            stripLineImg.raycastTarget = false;

            var compLbl = UiKit.Label("Label", stripRow, fonts.Body, UiTheme.Py(11f), UiTheme.Ink2,
                                      TextAlignmentOptions.Left, out var clRt, "동료 후보");
            compLbl.textWrappingMode = TextWrappingModes.NoWrap;
            compLbl.characterSpacing = 6f;
            Anchor(clRt, new Vector2(0f, 0f), new Vector2(0f, 1f),
                   new Vector2(UiTheme.Px(12f), 0f), new Vector2(UiTheme.Px(68f), 0f));

            var compRoot = UiKit.Node("Minis", stripRow);
            Anchor(compRoot, new Vector2(0f, 0f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(68f), 0f), new Vector2(-UiTheme.Px(12f), 0f));
            var compRow = UiKit.Row(compRoot, UiTheme.Px(4f), TextAnchor.MiddleLeft);
            compRow.childControlWidth = false;
            compRow.childControlHeight = false;
            compRow.childForceExpandWidth = false;

            var compEmpty = UiKit.Label("Empty", stripRow, fonts.Body, UiTheme.Py(11f), UiTheme.Ink2,
                                        TextAlignmentOptions.Left, out var ceRt,
                                        "편성에 다른 가전이 없어요");
            compEmpty.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(ceRt, new Vector2(0f, 0f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(68f), 0f), new Vector2(-UiTheme.Px(12f), 0f));

            // ---- `.sv-rw`: 오늘 남은 보상. 원본은 세 칸이 두 줄로 접힌다.
            var rewardLabel = SheetText("Rewards", 12f, UiTheme.Ink2, "");
            rewardLabel.richText = true;

            // ---- `.ov-btns`: 닫기 / 출동, 둘 다 179 폭.
            var btnRow = SheetRow("Buttons", 48f);
            var closeBtn = UiKit.Button("Close", btnRow, fonts.Body, "닫기", UiButtonKind.Secondary,
                                        out var cbRt, UiTheme.Py(48f));
            Anchor(cbRt, new Vector2(0f, 0f), new Vector2(0f, 1f),
                   Vector2.zero, new Vector2(UiTheme.Px(179f), 0f));
            var goBtn = UiKit.Button("Go", btnRow, fonts.Body, "출동", UiButtonKind.Primary,
                                     out var gbRt, UiTheme.Py(48f));
            Anchor(gbRt, new Vector2(1f, 0f), new Vector2(1f, 1f),
                   new Vector2(-UiTheme.Px(179f), 0f), Vector2.zero);

            // ---- HUD: the same plate the defense battle uses.
            var hudRoot = UiKit.Node("Hud", rt);
            Stretch(hudRoot);

            // 원본 `#svCv { border-radius: 14px }` — 아레나 판의 네 귀가 둥글다.
            // 카메라 뷰포트는 늘 각진 사각형이라, 귀퉁이만 배경색으로 덮어 둥글게 보이게 한다.
            var arenaMask = UiKit.Node("ArenaCorners", hudRoot);
            Anchor(arenaMask, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(16f), -UiTheme.Py(135f) - UiTheme.Py(486f)),
                   new Vector2(-UiTheme.Px(16f), -UiTheme.Py(135f)));
            var arenaCorners = arenaMask.gameObject.AddComponent<Image>();
            arenaCorners.sprite = GeneratedArt.RoundedCornerMask("ui_corner_mask",
                Mathf.RoundToInt(UiTheme.Px(14f)));
            arenaCorners.type = Image.Type.Sliced;
            arenaCorners.color = UiTheme.PageBg;
            arenaCorners.raycastTarget = false;

            float hudH = UiTheme.Py(113f);
            var hud = UiKit.Panel("Plate", hudRoot, out var plateRt, UiTheme.DarkPanel,
                                  UiTheme.Px(14f));
            Anchor(plateRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(pad, -UiTheme.Py(12f) - hudH), new Vector2(-pad, -UiTheme.Py(12f)));

            float inset = UiTheme.Px(12f);
            var topRow = UiKit.Node("Top", plateRt);
            Anchor(topRow, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(inset, -UiTheme.Py(10f) - UiTheme.Py(30f)),
                   new Vector2(-inset, -UiTheme.Py(10f)));

            var modeTag = UiKit.Label("Mode", topRow, fonts.Number, UiTheme.Py(13f),
                                      UiTheme.DarkInk2, TextAlignmentOptions.Left,
                                      out var mtRt, "MODE");
            modeTag.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(mtRt, new Vector2(0f, 0f), new Vector2(0f, 1f),
                   Vector2.zero, new Vector2(UiTheme.Px(46f), 0f));

            var modeName = UiKit.Label("Name", topRow, fonts.Title, UiTheme.Py(19f),
                                       UiTheme.DarkInk, TextAlignmentOptions.Left,
                                       out var mnRt, "대청소");
            modeName.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(mnRt, new Vector2(0f, 0f), new Vector2(0.5f, 1f),
                   new Vector2(UiTheme.Px(52f), 0f), Vector2.zero);

            var ctrls = UiKit.Node("Ctrls", topRow);
            Anchor(ctrls, new Vector2(0.4f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            var ctrlRow = UiKit.Row(ctrls, UiTheme.Px(6f), TextAnchor.MiddleRight);
            ctrlRow.childControlWidth = true;
            ctrlRow.childControlHeight = false;
            ctrlRow.childForceExpandWidth = false;

            UiButton Ctrl(string id, string text, out TextMeshProUGUI label)
            {
                var b = UiKit.Button(id, ctrls, fonts.Body, text, UiButtonKind.DarkSolid,
                                     out var crt, UiTheme.Py(30f), UiTheme.Py(12f));
                var plate = crt.GetComponent<Image>();
                if (plate != null)
                {
                    plate.sprite = GeneratedArt.RoundedNineSlice("ui_chip8",
                        Mathf.RoundToInt(UiTheme.Px(8f)));
                    plate.type = Image.Type.Sliced;
                    plate.color = UiTheme.DarkPanel2;
                }
                label = crt.GetComponentInChildren<TextMeshProUGUI>();
                label.fontStyle = FontStyles.Bold;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                var le = crt.gameObject.AddComponent<LayoutElement>();
                le.minHeight = le.preferredHeight = UiTheme.Py(30f);
                le.preferredWidth = label.GetPreferredValues(text).x + UiTheme.Px(18f);
                return b;
            }
            var pause = Ctrl("Pause", "일시정지", out var pauseLabel);
            var exitBtn = Ctrl("Exit", "나가기", out _);

            // ---- the three read-outs: 내구도 | Lv · 버틴 시간 | 처치
            var readouts = UiKit.Node("Readouts", plateRt);
            Anchor(readouts, new Vector2(0f, 0f), new Vector2(1f, 1f),
                   new Vector2(inset, UiTheme.Py(12f)),
                   new Vector2(-inset, -UiTheme.Py(10f) - UiTheme.Py(30f) - UiTheme.Py(10f)));
            var roRow = UiKit.Row(readouts, UiTheme.Px(12f), TextAnchor.LowerLeft);
            roRow.childControlWidth = true;
            roRow.childControlHeight = true;
            roRow.childForceExpandWidth = false;
            roRow.childForceExpandHeight = true;

            RectTransform Readout(string id, string caption, float minWidth, bool flexible,
                                  Color ledColor, TextAlignmentOptions align,
                                  out TextMeshProUGUI label, out TextMeshProUGUI value)
            {
                var r = UiKit.Node(id, readouts);
                var le = r.gameObject.AddComponent<LayoutElement>();
                le.minWidth = le.preferredWidth = minWidth;
                le.flexibleWidth = flexible ? 1f : 0f;

                label = UiKit.Label("Label", r, fonts.Body, UiTheme.Py(10f), UiTheme.DarkInk2,
                                    align, out var lrt, caption);
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.characterSpacing = 8f;
                Anchor(lrt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(0f, -UiTheme.Py(15f)), Vector2.zero);

                // Same trap as the battle HUD: a 26pt line needs ~80px of box or TMP drops it,
                // so the box is 44px centred on the build's y=31.
                float digitMid = UiTheme.Py(31f), digitBox = UiTheme.Py(44f);
                value = UiKit.Label("Value", r, fonts.Number, UiTheme.Py(26f), ledColor,
                                    align, out var vrt, "0");
                value.textWrappingMode = TextWrappingModes.NoWrap;
                Anchor(vrt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(0f, -digitMid - digitBox * 0.5f),
                       new Vector2(0f, -digitMid + digitBox * 0.5f));
                return r;
            }

            var hpRo = Readout("Hp", "내구도", UiTheme.Px(30f), false, UiTheme.LedLife,
                               TextAlignmentOptions.Left, out _, out var hpLabel);
            var hpBarRt = UiKit.Bar("Meter", hpRo, UiTheme.LedTrack, UiTheme.LedLife,
                                    UiTheme.Py(4f), out var hpFill);
            Anchor(hpBarRt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   Vector2.zero, new Vector2(0f, UiTheme.Py(4f)));

            var timeRo = Readout("Time", "Lv.1 · 버틴 시간", UiTheme.Px(120f), true, UiTheme.Led,
                                 TextAlignmentOptions.Left, out var levelLabel, out var timeLabel);
            var xpBarRt = UiKit.Bar("Meter", timeRo, UiTheme.LedTrack, UiTheme.Led,
                                    UiTheme.Py(4f), out var xpFill);
            Anchor(xpBarRt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   Vector2.zero, new Vector2(0f, UiTheme.Py(4f)));

            Readout("Kills", "처치", UiTheme.Px(34f), false, UiTheme.Led,
                    TextAlignmentOptions.Right, out _, out var killLabel);

            // ---- team strip: the leader plus four companion slots, under the arena.
            float teamH = UiTheme.Py(57f);
            float hintH = UiTheme.Line(UiTheme.Caption);
            // 원본 `#svTeam` 은 16,631 에 398x57 — 아레나(135~621) **바로 아래**다.
            // 화면 바닥에 붙여 두면 아레나와 팀 줄 사이가 통째로 빈다.
            var teamRoot = UiKit.Node("Team", hudRoot);
            Anchor(teamRoot, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(pad, -UiTheme.Py(631f) - teamH),
                   new Vector2(-pad, -UiTheme.Py(631f)));
            var teamRow = UiKit.Row(teamRoot, UiTheme.Px(6f), TextAnchor.MiddleLeft);
            teamRow.childControlWidth = false;
            teamRow.childControlHeight = false;
            teamRow.childForceExpandWidth = false;

            var hint = UiKit.Label("Hint", hudRoot, fonts.Body, UiTheme.Py(12f), UiTheme.Ink2,
                                   TextAlignmentOptions.Center, out var hintRt,
                                   "화면을 끌어서 움직여요(키보드는 WASD·방향키). 공격은 자동이에요.");
            // 원본 `.hint` 는 16,698 에 398x19.2 — 팀 줄 바로 밑이다.
            Anchor(hintRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(pad, -UiTheme.Py(698f) - hintH),
                   new Vector2(-pad, -UiTheme.Py(698f)));

            // ---- boss strip: shown only while a boss is up, just under the HUD plate.
            float bossTop = UiTheme.Py(12f) + hudH + UiTheme.Py(8f);
            float bossH = UiTheme.Py(52f);
            var bossRoot = UiKit.Panel("Boss", hudRoot, out var bossRt,
                                       new Color(UiTheme.Ink.r, UiTheme.Ink.g, UiTheme.Ink.b, 0.82f),
                                       UiTheme.Px(12f));
            Anchor(bossRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(pad, -bossTop - bossH), new Vector2(-pad, -bossTop));
            var bossName = UiKit.Label("Name", bossRt, fonts.Body, UiTheme.Py(12f), UiTheme.DarkInk,
                                       TextAlignmentOptions.Center, out var bnRt, "");
            bossName.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(bnRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(10f), -UiTheme.Py(8f) - UiTheme.Py(20f)),
                   new Vector2(-UiTheme.Px(10f), -UiTheme.Py(8f)));
            var bossBar = UiKit.Bar("HpBar", bossRt, UiTheme.LedTrack, UiTheme.LedLife,
                                    UiTheme.Py(8f), out var bossFill);
            Anchor(bossBar, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(UiTheme.Sq(10f), UiTheme.Sq(10f)),
                   new Vector2(-UiTheme.Px(10f), UiTheme.Py(18f)));
            bossRoot.gameObject.SetActive(false);

            // ---- pause overlay
            var pauseOverlay = UiKit.Panel("PauseOverlay", rt, out var poRt,
                                           new Color(0.05f, 0.06f, 0.07f, 0.72f), 1f);
            Stretch(poRt);
            UiKit.Panel("Card", poRt, out var poCard, UiTheme.Panel, UiTheme.PanelCorner);
            Anchor(poCard, new Vector2(0.1f, 0.5f), new Vector2(0.9f, 0.5f),
                   new Vector2(0f, -UiTheme.Py(90f)), new Vector2(0f, UiTheme.Py(90f)));
            UiKit.Label("Title", poCard, fonts.Title, UiTheme.Title, UiTheme.Ink,
                        TextAlignmentOptions.Center, out var potRt, "일시정지");
            Anchor(potRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.S16, -UiTheme.S24 - UiTheme.Line(UiTheme.Title)),
                   new Vector2(-UiTheme.S16, -UiTheme.S24));
            var resumeBtn = UiKit.Button("Resume", poCard, fonts.Body, "계속", UiButtonKind.Primary,
                                         out var resRt, UiTheme.ButtonTall);
            Anchor(resRt, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                   new Vector2(UiTheme.S16, -UiTheme.S6), new Vector2(-UiTheme.S16, -UiTheme.S6 + UiTheme.ButtonTall));
            var pauseExit = UiKit.Button("Exit", poCard, fonts.Body, "나가기", UiButtonKind.TextDanger,
                                         out var exRt, UiTheme.ButtonShort);
            Anchor(exRt, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                   new Vector2(UiTheme.S16, -UiTheme.S6 - UiTheme.ButtonTall - UiTheme.ButtonShort),
                   new Vector2(-UiTheme.S16, -UiTheme.S6 - UiTheme.ButtonTall));
            pauseOverlay.gameObject.SetActive(false);

            // ---- level-up: a translucent sheet INSIDE the arena rect with the choices
            // stacked down it. The old build laid them out in a row and the third card ran
            // off the right edge of the screen.
            // 원본 `.sv-pick` 은 아레나 캔버스와 **같은 자리**(16,135 398x486)다. 전에는
            // 팀 줄 위까지 내려와, 그늘이 아레나보다 아래로 더 번졌다.
            var optionRoot = UiKit.Node("Options", rt);
            Anchor(optionRoot, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(16f), -UiTheme.Py(135f) - UiTheme.Py(486f)),
                   new Vector2(-UiTheme.Px(16f), -UiTheme.Py(135f)));
            var optBg = optionRoot.gameObject.AddComponent<Image>();
            optBg.sprite = GeneratedArt.RoundedNineSlice("ui_modal14",
                Mathf.RoundToInt(UiTheme.Px(14f)));
            optBg.type = Image.Type.Sliced;
            optBg.color = new Color(UiTheme.Ink.r, UiTheme.Ink.g, UiTheme.Ink.b, 0.55f);

            var optTitle = UiKit.Label("Title", optionRoot, fonts.Title, UiTheme.Py(22f),
                                       UiTheme.DarkInk, TextAlignmentOptions.Center,
                                       out var optTitleRt, "레벨 업!");
            optTitle.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(optTitleRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Px(14f), -UiTheme.Py(14f) - UiTheme.Py(32f)),
                   new Vector2(-UiTheme.Sq(14f), -UiTheme.Sq(14f)));

            var optionList = UiKit.Node("List", optionRoot);
            Anchor(optionList, new Vector2(0f, 0f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.Sq(14f), UiTheme.Sq(14f)),
                   new Vector2(-UiTheme.Px(14f), -UiTheme.Py(54f)));
            var optionLayout = optionList.gameObject.AddComponent<VerticalLayoutGroup>();
            optionLayout.spacing = UiTheme.Py(8f);
            optionLayout.childAlignment = TextAnchor.MiddleCenter;
            optionLayout.childControlWidth = true;
            optionLayout.childControlHeight = false;
            optionLayout.childForceExpandWidth = true;
            optionLayout.childForceExpandHeight = false;
            optionRoot.gameObject.SetActive(false);

            var screen = go.AddComponent<SurvivorScreen>();
            Wire(screen,
                 ("view", view), ("fx", fx), ("arenaCamera", cam),
                 ("leaderSelectRoot", leaderRoot.gameObject), ("leaderListRoot", leaderList),
                 ("leaderCardPrefab", prefabs.UnitCard),
                 ("leaderSkillName", skillName), ("leaderSkillDesc", skillDesc),
                 ("leaderCompRoot", compRoot), ("leaderCompEmpty", compEmpty),
                 ("leaderRewardLabel", rewardLabel),
                 ("leaderCloseButton", closeBtn), ("leaderGoButton", goBtn),
                 ("miniSprite", GeneratedArt.RoundedNineSlice("ui_mini8",
                     Mathf.RoundToInt(UiTheme.Px(8f)))),
                 ("hudRoot", hudRoot.gameObject), ("hudRect", plateRt),
                 ("timeLabel", timeLabel), ("killLabel", killLabel),
                 ("pauseButton", pause), ("pauseButtonLabel", pauseLabel), ("exitButton", exitBtn),
                 ("levelLabel", levelLabel), ("xpFill", xpFill),
                 ("hpFill", hpFill), ("hpLabel", hpLabel),
                 ("teamRoot", teamRoot), ("teamRect", teamRoot), ("hintLabel", hint),
                 ("bossRoot", bossRoot.gameObject), ("bossNameLabel", bossName), ("bossHpFill", bossFill),
                 ("pauseOverlay", pauseOverlay.gameObject), ("pauseResumeButton", resumeBtn),
                 ("pauseExitButton", pauseExit),
                 ("optionRoot", optionRoot.gameObject), ("optionListRoot", optionList),
                 ("optionCardPrefab", prefabs.SurvivorOption),
                 ("bodyFont", fonts.Body),
                 ("slotSprite", GeneratedArt.RoundedNineSlice("ui_slot9",
                     Mathf.RoundToInt(UiTheme.Px(9f)))),
                 ("slotOutlineSprite", GeneratedArt.RoundedOutlineNineSlice("ui_slotline9",
                     Mathf.RoundToInt(UiTheme.Px(9f)), Mathf.RoundToInt(UiTheme.Px(1.5f)))),
                 ("sprites", library));

            var resultScreen = BuildSurvivorResultUI((RectTransform)go.transform, fonts);
            Wire(screen, ("result", resultScreen));

            go.SetActive(false);
            return screen;
        }
        /// <summary>
        /// 대청소 결과. 전투 결과와 **같은 카드**다 — 원본도 같은 .ov-card 를 쓴다.
        /// 제목 한 줄, 읽는 값 셋(버틴 시간 · 처치 · 레벨), 보상 한 줄, 오늘 기록 한 줄,
        /// 버튼 둘. 별도 레이아웃을 만들지 않는 편이 원본과도 맞고 손도 덜 간다.
        /// </summary>
        static SurvivorResultScreen BuildSurvivorResultUI(RectTransform parent, Fonts fonts)
        {
            var go = UI("SurvivorResult", parent, out var rt);
            Stretch(rt);
            var card = BuildOverlayCard(rt, out _, out _);

            var title = UiKit.Label("Title", CardRow(card, "TitleRow", 42f), fonts.Title,
                                    UiTheme.Py(26f), UiTheme.Ink,
                                    TextAlignmentOptions.Left, out var trt, "");
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.overflowMode = TextOverflowModes.Ellipsis;
            Stretch(trt);

            var statRow = CardRow(card, "Stats", 52f);
            var statLayout = UiKit.Row(statRow, UiTheme.Px(16f), TextAnchor.UpperLeft);
            statLayout.childControlWidth = true;
            statLayout.childControlHeight = true;
            statLayout.childForceExpandWidth = true;
            var time = OverlayStat(statRow, fonts, "버틴 시간", out _);
            var kills = OverlayStat(statRow, fonts, "처치", out _);
            var level = OverlayStat(statRow, fonts, "레벨", out _);

            // 보상 줄도 두 줄이 된다 — '30초 미만이라 보상 없이 끝났어요. 오늘 판 수는
            // 그대로예요.' 가 40 에서는 둘째 줄이 잘렸다.
            var reward = UiKit.Label("Reward", CardRow(card, "RewardRow", 56f), fonts.Body,
                                     UiTheme.Py(13f), UiTheme.Ink2,
                                     TextAlignmentOptions.TopLeft, out var rrt, "");
            reward.textWrappingMode = TextWrappingModes.Normal;
            reward.lineSpacing = 12f;
            Stretch(rrt);

            // 네 가지를 한 줄에 쓰면 330px 카드에서 두 줄이 된다. 자리를 두 줄로 잡는다.
            var note = UiKit.Label("Note", CardRow(card, "NoteRow", 56f), fonts.Body,
                                   UiTheme.Py(12f), UiTheme.Ink2,
                                   TextAlignmentOptions.TopLeft, out var nrt, "");
            note.textWrappingMode = TextWrappingModes.Normal;
            Stretch(nrt);

            var btnRow = CardRow(card, "Buttons", 48f);
            var btnGrid = btnRow.gameObject.AddComponent<GridLayoutGroup>();
            btnGrid.spacing = new Vector2(UiTheme.Px(8f), 0f);
            btnGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            btnGrid.constraintCount = 2;
            btnGrid.cellSize = new Vector2(UiTheme.Px(143f), UiTheme.Py(48f));
            var home = UiKit.Button("Home", btnRow, fonts.Body, "로비로",
                                    UiButtonKind.Secondary, out _, UiTheme.Py(48f),
                                    UiTheme.Py(15f));
            var retry = UiKit.Button("Retry", btnRow, fonts.Body, "다시 하기",
                                     UiButtonKind.DarkSolid, out _, UiTheme.Py(48f),
                                     UiTheme.Py(15f));

            var screen = go.AddComponent<SurvivorResultScreen>();
            Wire(screen,
                 ("titleLabel", title),
                 ("timeValue", time), ("killsValue", kills), ("levelValue", level),
                 ("rewardLabel", reward), ("bestLabel", note),
                 ("homeButton", home), ("retryButton", retry));
            go.SetActive(false);
            return screen;
        }


        // ------------------------------------------------------------------ 로비 강제 안내

        /// <summary>
        /// The lobby guide's spotlight: four dimming bands framing whichever element the
        /// current step names, plus a speech bubble. Built as a sibling of every screen rather
        /// than inside one of them, since its two flows span the lobby, the battle brief and
        /// the result screen. GuideOverlay repositions the bands/bubble at runtime, so their
        /// build-time anchors are only placeholders.
        /// </summary>
        static GuideOverlay BuildGuideOverlay(Transform parent, Fonts fonts)
        {
            var root = UiKit.Node("GuideOverlay", parent);
            Stretch(root);

            Image Band(string name)
            {
                var img = UiKit.Surface(name, root, new Color(0.05f, 0.06f, 0.07f, 0.68f), 0f, out var bandRt);
                Stretch(bandRt);
                img.raycastTarget = true;
                return img;
            }
            var bandTop = Band("BandTop");
            var bandBottom = Band("BandBottom");
            var bandLeft = Band("BandLeft");
            var bandRight = Band("BandRight");

            float pad = UiTheme.ScreenPad;
            UiKit.Panel("Bubble", root, out var bubbleRt, UiTheme.Panel, UiTheme.PanelCorner);
            Stretch(bubbleRt);

            var text = UiKit.Label("Text", bubbleRt, fonts.Body, UiTheme.LabelSize, UiTheme.Ink,
                                   TextAlignmentOptions.TopLeft, out var textRt, "");
            text.textWrappingMode = TextWrappingModes.Normal;
            Anchor(textRt, Vector2.zero, new Vector2(1f, 1f),
                   new Vector2(UiTheme.S16, UiTheme.S16 + UiTheme.ButtonShort),
                   new Vector2(-UiTheme.S16, -UiTheme.S16));

            var skip = UiKit.Button("Skip", bubbleRt, fonts.Body, "안내 건너뛰기", UiButtonKind.TextDanger,
                                    out var skipRt, UiTheme.ButtonShort, UiTheme.Caption);
            Anchor(skipRt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(UiTheme.S16, UiTheme.S8), new Vector2(-UiTheme.S16, UiTheme.S8 + UiTheme.ButtonShort));

            var overlay = root.gameObject.AddComponent<GuideOverlay>();
            Wire(overlay, ("bandTop", bandTop.rectTransform), ("bandBottom", bandBottom.rectTransform),
                 ("bandLeft", bandLeft.rectTransform), ("bandRight", bandRight.rectTransform),
                 ("bubbleRoot", bubbleRt), ("bubbleText", text), ("skipButton", skip));
            root.gameObject.SetActive(false);
            return overlay;
        }
    }
}
