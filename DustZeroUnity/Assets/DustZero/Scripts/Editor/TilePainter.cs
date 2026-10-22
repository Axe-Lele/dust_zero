// Paints the board tiles the way the original's canvas does.
//
// The web build repaints the whole field into an offscreen canvas (buildBoard) every time the
// stage loads, so its floors carry plank seams, tile grout, wall hatching and furniture
// shadows. Porting that to a Tilemap means baking the same strokes into sprites once, in the
// editor: the drawing code below is a transcription of paintFloor/buildBoard, with the cell
// offsets that vary per column and row turned into the handful of variants they can produce.
//
// Coordinates are canvas coordinates - origin top-left, y growing downward - and are flipped
// when the pixels are written out, so the numbers here can be read against the original.
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DustZero.Editor
{
    /// <summary>A small software rasteriser: enough of the 2D canvas API to repaint the board.</summary>
    public sealed class TileCanvas
    {
        /// <summary>Edge length in pixels. Tiles use 64; icons are drawn larger.</summary>
        public int Size { get; }

        readonly Color[] _px;

        /// <summary>Canvas edge in pixels, the original's T.</summary>
        public float T => Size;

        public TileCanvas(Color? background = null, int size = 64)
        {
            Size = size;
            _px = new Color[size * size];
            var bg = background ?? new Color(0f, 0f, 0f, 0f);
            for (int i = 0; i < _px.Length; i++) _px[i] = bg;
        }

        /// <summary>Source-over blend of one pixel, with <paramref name="cov"/> scaling alpha.</summary>
        void Blend(int x, int y, Color c, float cov)
        {
            if (x < 0 || y < 0 || x >= Size || y >= Size) return;
            float a = c.a * Mathf.Clamp01(cov);
            if (a <= 0f) return;
            var d = _px[y * Size + x];
            float outA = a + d.a * (1f - a);
            if (outA <= 0f) { _px[y * Size + x] = new Color(0, 0, 0, 0); return; }
            _px[y * Size + x] = new Color(
                (c.r * a + d.r * d.a * (1f - a)) / outA,
                (c.g * a + d.g * d.a * (1f - a)) / outA,
                (c.b * a + d.b * d.a * (1f - a)) / outA,
                outA);
        }

        /// <summary>Coverage from a signed distance, antialiased across one pixel.</summary>
        static float Cover(float sd) => Mathf.Clamp01(0.5f - sd);

        delegate float Sdf(float x, float y);

        void Paint(Color c, Sdf sdf)
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float cov = Cover(sdf(x + 0.5f, y + 0.5f));
                    if (cov > 0f) Blend(x, y, c, cov);
                }
        }

        /// <summary>
        /// Removes coverage instead of adding it. Source-over blending ignores a transparent
        /// colour, so cutting a doorway out of a shape needs its own operation.
        /// </summary>
        void Cut(Sdf sdf)
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float cov = Cover(sdf(x + 0.5f, y + 0.5f));
                    if (cov <= 0f) continue;
                    var d = _px[y * Size + x];
                    _px[y * Size + x] = new Color(d.r, d.g, d.b, d.a * (1f - Mathf.Clamp01(cov)));
                }
        }

        public void EraseRect(float x0, float y0, float w, float h)
            => Cut((x, y) => Mathf.Max(Mathf.Max(x0 - x, x - (x0 + w)), Mathf.Max(y0 - y, y - (y0 + h))));

        public void EraseRoundRect(float x0, float y0, float w, float h, float r)
            => Cut((x, y) => BoxSdf(x, y, x0, y0, w, h, r));

        public void EraseEllipse(float cx, float cy, float rx, float ry)
            => Cut((x, y) =>
            {
                float nx = (x - cx) / Mathf.Max(0.001f, rx), ny = (y - cy) / Mathf.Max(0.001f, ry);
                return (Mathf.Sqrt(nx * nx + ny * ny) - 1f) * Mathf.Min(rx, ry);
            });

        public void EraseLine(float x1, float y1, float x2, float y2, float lw)
            => Cut((x, y) =>
            {
                float vx = x2 - x1, vy = y2 - y1;
                float len2 = vx * vx + vy * vy;
                float t = len2 <= 0f ? 0f : Mathf.Clamp01(((x - x1) * vx + (y - y1) * vy) / len2);
                float dx = x - (x1 + vx * t), dy = y - (y1 + vy * t);
                return Mathf.Sqrt(dx * dx + dy * dy) - lw * 0.5f;
            });

        public void Fill(Color c) => Paint(c, (x, y) => -1f);

        public void Rect(float x0, float y0, float w, float h, Color c)
            => Paint(c, (x, y) => Mathf.Max(Mathf.Max(x0 - x, x - (x0 + w)), Mathf.Max(y0 - y, y - (y0 + h))));

        static float BoxSdf(float x, float y, float x0, float y0, float w, float h, float r)
        {
            float cx = x0 + w * 0.5f, cy = y0 + h * 0.5f;
            r = Mathf.Min(r, Mathf.Min(w, h) * 0.5f);
            float dx = Mathf.Abs(x - cx) - (w * 0.5f - r);
            float dy = Mathf.Abs(y - cy) - (h * 0.5f - r);
            float ox = Mathf.Max(dx, 0f), oy = Mathf.Max(dy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(dx, dy), 0f) - r;
        }

        /// <summary>The original's rr(): rounded rectangle with an optional outline.</summary>
        public void RoundRect(float x0, float y0, float w, float h, float r,
                              Color? fill = null, Color? stroke = null, float lw = 1f)
        {
            if (fill.HasValue) Paint(fill.Value, (x, y) => BoxSdf(x, y, x0, y0, w, h, r));
            if (stroke.HasValue)
                Paint(stroke.Value, (x, y) => Mathf.Abs(BoxSdf(x, y, x0, y0, w, h, r)) - lw * 0.5f);
        }

        public void StrokeRect(float x0, float y0, float w, float h, Color c, float lw = 1f)
            => RoundRect(x0, y0, w, h, 0f, null, c, lw);

        public void Line(float x1, float y1, float x2, float y2, Color c, float lw = 1f)
            => Paint(c, (x, y) =>
            {
                float vx = x2 - x1, vy = y2 - y1;
                float len2 = vx * vx + vy * vy;
                float t = len2 <= 0f ? 0f : Mathf.Clamp01(((x - x1) * vx + (y - y1) * vy) / len2);
                float dx = x - (x1 + vx * t), dy = y - (y1 + vy * t);
                return Mathf.Sqrt(dx * dx + dy * dy) - lw * 0.5f;
            });

        /// <summary>The original's ell(): a filled ellipse.</summary>
        public void Ellipse(float cx, float cy, float rx, float ry, Color c)
            => Paint(c, (x, y) =>
            {
                float nx = (x - cx) / Mathf.Max(0.001f, rx), ny = (y - cy) / Mathf.Max(0.001f, ry);
                // Scale the normalised distance back to pixels so the edge stays one pixel wide.
                float d = Mathf.Sqrt(nx * nx + ny * ny);
                return (d - 1f) * Mathf.Min(rx, ry);
            });

        public void CircleStroke(float cx, float cy, float r, Color c, float lw)
            => Paint(c, (x, y) =>
            {
                float dx = x - cx, dy = y - cy;
                return Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - r) - lw * 0.5f;
            });

        /// <summary>Writes the canvas out as a sprite asset, one tile to one world unit.</summary>
        public Sprite Save(string name)
        {
            GeneratedArt.EnsureFolder();
            var path = $"{GeneratedArt.Folder}/{name}.png";

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var flipped = new Color[_px.Length];
            // Canvas y grows downward; texture rows grow upward.
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    flipped[(Size - 1 - y) * Size + x] = _px[y * Size + x];
            tex.SetPixels(flipped);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = Size;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }

    /// <summary>Builds every board sprite the original paints. All colours are its own.</summary>
    public static class TilePainter
    {
        static Color Hex(string rgb, float a = 1f)
        {
            ColorUtility.TryParseHtmlString(rgb, out var c);
            c.a = a;
            return c;
        }

        static readonly Color WoodFloor = Hex("#DCC39A");
        static readonly Color KitchenFloor = Hex("#E4E7E1");
        static readonly Color BathFloor = Hex("#CDE2E9");
        static readonly Color Structure = Hex("#C3CBD1");

        /// <summary>Plank seams repeat every three columns, so the floor needs three variants.</summary>
        public const int WoodVariants = 3;
        /// <summary>The kitchen checker alternates, so it needs two.</summary>
        public const int KitchenVariants = 2;

        /// <summary>The variant index a cell uses, matching the original's offset maths.</summary>
        public static int WoodVariant(int col, int row) => ((col * 7 + row * 3) % 3 + 3) % 3;
        public static int KitchenVariant(int col, int row) => (col + row) % 2 == 0 ? 0 : 1;

        /// <summary>paintFloor(), for the floor named by <paramref name="floor"/>.</summary>
        public static void PaintFloor(TileCanvas c, string floor, int variant)
        {
            float T = c.T;
            if (floor == "wood")
            {
                c.Fill(WoodFloor);
                var seam = new Color(120 / 255f, 84 / 255f, 40 / 255f, 0.18f);
                for (int i = 1; i < 3; i++) c.Line(0, i * T / 3f, T, i * T / 3f, seam);
                float o = variant * T / 3f;
                c.Line(o, 0, o, T / 3f, seam);
                float o2 = (o + T / 2f) % T;
                c.Line(o2, T / 3f, o2, 2 * T / 3f, seam);
                return;
            }

            c.Fill(floor == "bath" ? BathFloor : KitchenFloor);
            if (floor == "kitchen" && variant == 0)
                c.Rect(0, 0, T, T, new Color(29 / 255f, 34 / 255f, 38 / 255f, 0.04f));
            var grout = floor == "bath"
                ? new Color(40 / 255f, 90 / 255f, 110 / 255f, 0.15f)
                : new Color(29 / 255f, 34 / 255f, 38 / 255f, 0.10f);
            c.StrokeRect(0.5f, 0.5f, T - 1f, T - 1f, grout);
            if (floor == "bath")
            {
                c.Line(T / 2f, 0, T / 2f, T, grout);
                c.Line(0, T / 2f, T, T / 2f, grout);
            }
        }

        public static Sprite Floor(string floor, int variant)
        {
            var c = new TileCanvas();
            PaintFloor(c, floor, variant);
            return c.Save($"tile_{floor}_{variant}");
        }

        /// <summary>'X' - structural wall, hatched diagonally.</summary>
        public static Sprite Wall()
        {
            var c = new TileCanvas();
            float T = c.T;
            c.Fill(Structure);
            var hatch = new Color(29 / 255f, 34 / 255f, 38 / 255f, 0.07f);
            for (float i = -T; i < T * 2f; i += 7f) c.Line(i, 0, i - T, T, hatch);
            return c.Save("tile_wall");
        }

        /// <summary>'H' - furniture the ranged appliances stand on.</summary>
        public static Sprite Furniture(string floor)
        {
            var c = new TileCanvas();
            float T = c.T;
            c.Fill(Structure);
            c.RoundRect(3, 3, T - 6, T - 6, 6,
                        floor == "kitchen" ? Hex("#EEF0EC") : Hex("#F3F5F6"), Hex("#AEB9C1"), 1f);
            c.Rect(4, T - 9, T - 8, 5, new Color(29 / 255f, 34 / 255f, 38 / 255f, 0.06f));
            return c.Save($"tile_furniture_{floor}");
        }

        /// <summary>'F' - the burner that heats whatever stands on it. Drawn over the floor.</summary>
        public static Sprite Stove()
        {
            var c = new TileCanvas();
            float T = c.T;
            c.RoundRect(4, 4, T - 8, T - 8, 6, Hex("#2F3438"));
            c.CircleStroke(T / 2f, T / 2f, T * 0.26f, Hex("#5A6167"), 2f);
            c.CircleStroke(T / 2f, T / 2f, T * 0.14f, Hex("#5A6167"), 2f);
            return c.Save("tile_stove");
        }

        /// <summary>'W' - a puddle that cools. Drawn over the floor.</summary>
        public static Sprite Wet()
        {
            var c = new TileCanvas();
            float T = c.T;
            c.Ellipse(T * 0.5f, T * 0.55f, T * 0.42f, T * 0.3f,
                      new Color(61 / 255f, 139 / 255f, 217 / 255f, 0.3f));
            c.Ellipse(T * 0.38f, T * 0.45f, T * 0.12f, T * 0.05f, new Color(1f, 1f, 1f, 0.7f));
            return c.Save("tile_wet");
        }

        /// <summary>The thing the stains are heading for, drawn per floor. Over the floor.</summary>
        public static Sprite Goal(string floor)
        {
            var c = new TileCanvas();
            float T = c.T;
            if (floor == "wood")
            {
                c.RoundRect(3, T * 0.38f, T - 6, T * 0.5f, 6, Hex("#5B7C99"));
                c.RoundRect(3, T * 0.2f, T - 6, T * 0.28f, 6, Hex("#4A6884"));
                c.RoundRect(1, T * 0.36f, T * 0.16f, T * 0.5f, 4, Hex("#3E5A74"));
                c.RoundRect(T * 0.84f - 1f, T * 0.36f, T * 0.16f, T * 0.5f, 4, Hex("#3E5A74"));
            }
            else if (floor == "kitchen")
            {
                c.RoundRect(3, T * 0.25f, T - 6, T * 0.5f, 5, Hex("#B98A57"));
                c.Rect(7, T * 0.75f, 4, T * 0.2f, Hex("#8C6238"));
                c.Rect(T - 11, T * 0.75f, 4, T * 0.2f, Hex("#8C6238"));
            }
            else
            {
                c.RoundRect(2, T * 0.2f, T - 4, T * 0.7f, T * 0.25f, Hex("#F7FAFB"), Hex("#9AAEBD"), 1.5f);
                c.RoundRect(7, T * 0.3f, T - 14, T * 0.45f, T * 0.18f, Hex("#9ED0EC"));
            }
            return c.Save($"tile_goal_{floor}");
        }

        /// <summary>The slatted vent each wave walks out of. Over the floor, at a path start.</summary>
        public static Sprite Vent()
        {
            var c = new TileCanvas();
            float T = c.T;
            c.RoundRect(4, 4, T - 8, T * 0.34f, 4, Hex("#BFE0F2"), Hex("#7F96A6"), 1.5f);
            for (int i = 1; i < 4; i++)
            {
                float x = 4f + i * (T - 8f) / 4f;
                c.Line(x, 6, x, T * 0.34f + 2f, Hex("#5D6871"), 1.5f);
            }
            return c.Save("tile_vent");
        }
    }
}
