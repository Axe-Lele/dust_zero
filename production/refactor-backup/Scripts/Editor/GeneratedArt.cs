// Procedurally generated helper textures (solid shapes used for tiles, bars and particles).
//
// These are deliberately generated rather than hand-authored so the whole project can be
// rebuilt from source with no binary assets to keep in sync.
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DustZero.Editor
{
    public static class GeneratedArt
    {
        public const string Folder = "Assets/DustZero/Art/Generated";

        public static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/DustZero/Art"))
                AssetDatabase.CreateFolder("Assets/DustZero", "Art");
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/DustZero/Art", "Generated");
        }

        /// <summary>A flat square, used for tiles, bars, panels and confetti.</summary>
        public static Sprite Square(string name, int size = 64)
            => Make(name, size, (x, y, s) => 1f);

        /// <summary>A soft-edged disc, used for round particles.</summary>
        public static Sprite Circle(string name, int size = 64)
            => Make(name, size, (x, y, s) =>
            {
                float cx = (s - 1) * 0.5f, cy = (s - 1) * 0.5f;
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / (s * 0.5f);
                return Mathf.Clamp01(1f - Mathf.SmoothStep(0.82f, 1f, d));
            });

        /// <summary>
        /// A dashed ring, used for the selected unit's range. Drawn as a texture rather than a
        /// line renderer so it scales with the unit's radius for free and costs one quad.
        /// </summary>
        public static Sprite DashedRing(string name, int size = 256, float bandFraction = 0.035f,
                                        int dashes = 28)
            => Make(name, size, (x, y, s) =>
            {
                float cx = (s - 1) * 0.5f, cy = (s - 1) * 0.5f;
                float dx = x - cx, dy = y - cy;
                float r = Mathf.Sqrt(dx * dx + dy * dy) / (s * 0.5f);
                float band = bandFraction;
                // Radial falloff keeps the ring one band wide with a soft edge.
                float ring = 1f - Mathf.Clamp01(Mathf.Abs(r - (1f - band)) / band);
                if (ring <= 0f) return 0f;
                // Angular duty cycle cuts the ring into dashes.
                float angle = Mathf.Atan2(dy, dx) / (2f * Mathf.PI) + 0.5f;
                float phase = Mathf.Repeat(angle * dashes, 1f);
                float dash = phase < 0.58f ? 1f : 0f;
                return ring * dash;
            });

        /// <summary>A rounded square outline, used for the placement highlight.</summary>
        public static Sprite RoundedOutline(string name, int size = 64, int inset = 3, int thickness = 4)
            => Make(name, size, (x, y, s) =>
            {
                float r = 10f;
                float inner = SignedRoundedBox(x, y, s, inset + thickness, r);
                float outer = SignedRoundedBox(x, y, s, inset, r);
                // Keep the fill faint and the border solid so the grid stays readable.
                if (outer > 0f) return 0f;
                return inner > 0f ? 1f : 0.28f;
            });

        /// <summary>
        /// A white rounded rectangle set up for 9-slice stretching, so one sprite serves every
        /// panel and button of a given corner radius. The border is the radius itself: the
        /// corners stay crisp and only the flat middle stretches.
        /// </summary>
        public static Sprite RoundedNineSlice(string name, int radius)
        {
            int size = Mathf.Max(8, radius * 4);
            var sprite = MakeRounded($"{name}_{radius}", size, radius);
            var path = $"{Folder}/{name}_{radius}.png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var border = new Vector4(radius, radius, radius, radius);
            // The border is in pixels, so it survives any rect size the layout asks for.
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteBorder = border;
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            importer.SetTextureSettings(settings);
            importer.spritePixelsPerUnit = 100f;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static Sprite MakeRounded(string name, int size, int radius)
            => Make(name, size, (x, y, s) =>
            {
                float d = SignedRoundedBox(x + 0.5f, y + 0.5f, s, 0f, radius);
                return Mathf.Clamp01(0.5f - d);
            });

        static float SignedRoundedBox(float x, float y, float s, float inset, float radius)
        {
            float lo = inset, hi = s - 1 - inset;
            float dx = Mathf.Max(lo - x, x - hi);
            float dy = Mathf.Max(lo - y, y - hi);
            if (dx <= -radius || dy <= -radius) return Mathf.Max(dx, dy);
            float ox = Mathf.Max(dx + radius, 0f), oy = Mathf.Max(dy + radius, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) - radius;
        }

        delegate float AlphaAt(int x, int y, int size);

        static Sprite Make(string name, int size, AlphaAt alpha)
        {
            EnsureFolder();
            var path = $"{Folder}/{name}.png";

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float a = Mathf.Clamp01(alpha(x, y, size));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();

            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = size;      // one sprite spans one world unit
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
