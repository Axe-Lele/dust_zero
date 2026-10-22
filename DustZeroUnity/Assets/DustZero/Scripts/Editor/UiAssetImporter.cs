// Import settings for the supplied UI art, driven by the pack's own manifest.
//
// The pack ships manifest.json listing each PNG's size, 9-slice border and whether it is
// meant to be tinted. Reading it here means the borders are never typed in by hand and never
// drift: re-importing the folder reproduces exactly what the pack specifies.
//
// Border order differs between the two sides. The manifest uses CSS order
// [left, right, top, bottom]; Unity's spriteBorder is Vector4(left, bottom, right, top).
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DustZero.Editor
{
    public sealed class UiAssetImporter : AssetPostprocessor
    {
        public const string Root = "Assets/DustZero/Art/UI_DZ/";
        const string ManifestPath = Root + "manifest.json";

        /// <summary>Pixels per unit for the pack, as its prompt specifies.</summary>
        const float PixelsPerUnit = 100f;

        /// <summary>
        /// World-space board art is sized so one image is exactly one tile, which is what the
        /// Tilemap's one-unit cell and the ring scaling both assume. UI art keeps the pack's
        /// 100 PPU, where the number only sets the default rect size.
        /// </summary>
        static float PixelsFor(string path)
        {
            if (path.Contains("/battle/tile_")) return 144f;
            if (path.EndsWith("/range_ring.png")) return 512f;
            if (path.EndsWith("/select_ring.png") || path.EndsWith("/overheat_ring.png")) return 192f;
            return PixelsPerUnit;
        }

        [System.Serializable]
        sealed class Entry
        {
            public int w, h;
            public int[] border;
            public bool tint;
            public string note;
        }

        static Dictionary<string, Entry> _manifest;

        /// <summary>
        /// Parses manifest.json once per domain reload. JsonUtility cannot read a bare object
        /// map, so the file is walked with the project's existing Newtonsoft dependency.
        /// </summary>
        static Dictionary<string, Entry> Manifest()
        {
            if (_manifest != null) return _manifest;
            _manifest = new Dictionary<string, Entry>();

            var text = System.IO.File.Exists(ManifestPath)
                ? System.IO.File.ReadAllText(ManifestPath) : null;
            if (string.IsNullOrEmpty(text)) return _manifest;

            try
            {
                var parsed = Newtonsoft.Json.JsonConvert
                    .DeserializeObject<Dictionary<string, Entry>>(text);
                if (parsed != null) _manifest = parsed;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[DustZero] UI 에셋 manifest 를 읽지 못했습니다: {e.Message}");
            }
            return _manifest;
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root)) return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsFor(assetPath);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            // Uncompressed in the editor keeps the 9-slice edges clean; the shipped build sets
            // ASTC per platform, which is a build-settings decision rather than an import one.
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 1024;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            // Full Rect: a tight mesh breaks sliced stretching and sub-pixel UI alignment.
            settings.spriteMeshType = SpriteMeshType.FullRect;

            var key = assetPath.Substring(Root.Length).Replace('\\', '/');
            if (Manifest().TryGetValue(key, out var entry) && entry?.border != null && entry.border.Length == 4)
            {
                // manifest [left, right, top, bottom] -> Unity (left, bottom, right, top)
                settings.spriteBorder = new Vector4(entry.border[0], entry.border[3],
                                                    entry.border[1], entry.border[2]);
            }
            importer.SetTextureSettings(settings);
        }

        /// <summary>Re-imports the whole pack, for when the manifest or these rules change.</summary>
        [MenuItem("DustZero/Reimport UI Art")]
        public static void ReimportAll()
        {
            _manifest = null;
            AssetDatabase.ImportAsset(Root.TrimEnd('/'),
                ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate |
                ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[DustZero] UI 아트를 manifest 설정으로 다시 임포트했습니다.");
        }
    }
}
