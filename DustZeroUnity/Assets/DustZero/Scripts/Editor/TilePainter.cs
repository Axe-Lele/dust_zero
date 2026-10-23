// 원본 캔버스 그림을 스프라이트로 굽는 소형 래스터라이저와, 칸 하나짜리 그림들.
//
// 판 바닥 전체는 여기가 아니라 BoardPainter 가 **지도 한 장**으로 굽는다(원본 buildBoard
// 와 같은 방식). 여기 남은 것은 칸 하나로 떨어지는 그림뿐이다 — 화구, 물웅덩이,
// 입구 환기구, 바닥 종류별 목표물.
//
// 좌표는 캔버스 좌표다 — 원점 왼쪽 위, y 는 아래로. 픽셀을 쓸 때 뒤집으므로 여기 숫자는
// 원본과 그대로 견줄 수 있다.
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DustZero.Editor
{
    /// <summary>A small software rasteriser: enough of the 2D canvas API to repaint the board.</summary>
    public sealed class TileCanvas
    {
        /// <summary>Canvas size in pixels. A single tile is square; a whole board is not.</summary>
        public int Width { get; }
        public int Height { get; }

        readonly Color[] _px;
        // 클립 사각형(픽셀). 원본이 c.save()/c.clip() 으로 한 칸 안에 가두는 빗금 따위가 있다.
        float _clipX0, _clipY0, _clipX1, _clipY1;
        bool _clipped;

        /// <summary>One tile's edge in pixels — the original's T. 64 unless a painter says otherwise.</summary>
        public float T { get; }

        public TileCanvas(Color? background = null, int size = 64)
            : this(1, 1, size, background) { }

        /// <summary>A canvas <paramref name="cols"/> x <paramref name="rows"/> tiles across.</summary>
        public TileCanvas(int cols, int rows, int tile, Color? background = null)
        {
            Width = cols * tile;
            Height = rows * tile;
            T = tile;
            _px = new Color[Width * Height];
            var bg = background ?? new Color(0f, 0f, 0f, 0f);
            for (int i = 0; i < _px.Length; i++) _px[i] = bg;
        }

        /// <summary>The original's c.save()+c.clip(): later paint lands only inside this box.</summary>
        public void Clip(float x, float y, float w, float h)
        {
            _clipped = true;
            _clipX0 = x; _clipY0 = y; _clipX1 = x + w; _clipY1 = y + h;
        }

        /// <summary>The matching c.restore().</summary>
        public void NoClip() => _clipped = false;

        /// <summary>Source-over blend of one pixel, with <paramref name="cov"/> scaling alpha.</summary>
        void Blend(int x, int y, Color c, float cov)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return;
            if (_clipped && (x + 0.5f < _clipX0 || x + 0.5f > _clipX1 ||
                             y + 0.5f < _clipY0 || y + 0.5f > _clipY1)) return;
            float a = c.a * Mathf.Clamp01(cov);
            if (a <= 0f) return;
            var d = _px[y * Width + x];
            float outA = a + d.a * (1f - a);
            if (outA <= 0f) { _px[y * Width + x] = new Color(0, 0, 0, 0); return; }
            _px[y * Width + x] = new Color(
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
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
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
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    float cov = Cover(sdf(x + 0.5f, y + 0.5f));
                    if (cov <= 0f) continue;
                    var d = _px[y * Width + x];
                    _px[y * Width + x] = new Color(d.r, d.g, d.b, d.a * (1f - Mathf.Clamp01(cov)));
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

        /// <summary>
        /// 점선 테두리. 원본은 setLineDash 로 테두리 한 줄을 끊어 긋는다. 여기서는 네 변을
        /// 따로 끊으므로 모서리에서 무늬가 한 번 다시 시작한다 — 2px 무늬라 눈에 띄지 않는다.
        /// </summary>
        public void DashedRect(float x0, float y0, float w, float h, Color c, float lw, float on, float off)
        {
            void Seg(float ax, float ay, float bx, float by)
            {
                float len = Mathf.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                float ux = (bx - ax) / len, uy = (by - ay) / len;
                for (float t = 0f; t < len; t += on + off)
                {
                    float e = Mathf.Min(t + on, len);
                    Line(ax + ux * t, ay + uy * t, ax + ux * e, ay + uy * e, c, lw);
                }
            }
            Seg(x0, y0, x0 + w, y0);
            Seg(x0 + w, y0, x0 + w, y0 + h);
            Seg(x0 + w, y0 + h, x0, y0 + h);
            Seg(x0, y0 + h, x0, y0);
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

        /// <summary>The original's circ(): a filled circle.</summary>
        public void Circle(float cx, float cy, float r, Color c) => Ellipse(cx, cy, r, r, c);

        /// <summary>
        /// A filled (and optionally outlined) polygon. The original draws three of the goal
        /// props as paths — the veranda pot, the throne crystal — and a scanline fill would
        /// alias badly at 64px, so this uses the usual signed distance to the outline.
        /// </summary>
        public void Polygon(float[] xs, float[] ys, Color? fill = null, Color? stroke = null, float lw = 1f)
        {
            float Sdf(float px, float py)
            {
                int n = xs.Length;
                float d = (px - xs[0]) * (px - xs[0]) + (py - ys[0]) * (py - ys[0]);
                float sign = 1f;
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    float ex = xs[j] - xs[i], ey = ys[j] - ys[i];
                    float wx = px - xs[i], wy = py - ys[i];
                    float t = Mathf.Clamp01((wx * ex + wy * ey) / Mathf.Max(1e-6f, ex * ex + ey * ey));
                    float bx = wx - ex * t, by = wy - ey * t;
                    d = Mathf.Min(d, bx * bx + by * by);
                    // Winding test: three comparisons that flip the sign once per crossing.
                    bool c1 = py >= ys[i], c2 = py < ys[j], c3 = ex * wy > ey * wx;
                    if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) sign = -sign;
                }
                return sign * Mathf.Sqrt(d);
            }

            if (fill.HasValue) Paint(fill.Value, (x, y) => Sdf(x, y));
            if (stroke.HasValue) Paint(stroke.Value, (x, y) => Mathf.Abs(Sdf(x, y)) - lw * 0.5f);
        }

        /// <summary>
        /// The throne's glow: createRadialGradient(cx,cy,r0 .. cx,cy,r1) painted over the
        /// whole tile. Canvas interpolates the colour stops in straight RGBA, so this does too.
        /// </summary>
        public void RadialGradient(float cx, float cy, float r0, float r1, Color inner, Color outer)
        {
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float t = Mathf.Clamp01((Mathf.Sqrt(dx * dx + dy * dy) - r0) / Mathf.Max(1e-6f, r1 - r0));
                    Blend(x, y, Color.Lerp(inner, outer, t), 1f);
                }
        }

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

            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            var flipped = new Color[_px.Length];
            // Canvas y grows downward; texture rows grow upward.
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    flipped[(Height - 1 - y) * Width + x] = _px[y * Width + x];
            tex.SetPixels(flipped);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = T;
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

        /// <summary>
        /// 목표물. 원본 paintGoal 의 열다섯 바닥을 그대로 옮겼다 — 예전에는 거실·주방·욕실
        /// 셋만 알고 나머지 열둘이 전부 욕조로 그려졌다. 치수와 색은 모두 원본 것이다.
        /// </summary>
        public static Sprite Goal(string floor)
        {
            var c = new TileCanvas();
            float T = c.T;
            switch (floor)
            {
                case "wood":        // 소파
                    c.RoundRect(3, T * 0.38f, T - 6, T * 0.5f, 6, Hex("#5B7C99"));
                    c.RoundRect(3, T * 0.2f, T - 6, T * 0.28f, 6, Hex("#4A6884"));
                    c.RoundRect(1, T * 0.36f, T * 0.16f, T * 0.5f, 4, Hex("#3E5A74"));
                    c.RoundRect(T * 0.84f - 1f, T * 0.36f, T * 0.16f, T * 0.5f, 4, Hex("#3E5A74"));
                    break;

                case "kitchen":     // 식탁
                    c.RoundRect(3, T * 0.25f, T - 6, T * 0.5f, 5, Hex("#B98A57"));
                    c.Rect(7, T * 0.75f, 4, T * 0.2f, Hex("#8C6238"));
                    c.Rect(T - 11, T * 0.75f, 4, T * 0.2f, Hex("#8C6238"));
                    break;

                case "bath":        // 욕조
                    c.RoundRect(2, T * 0.2f, T - 4, T * 0.7f, T * 0.25f, Hex("#F7FAFB"), Hex("#9AAEBD"), 1.5f);
                    c.RoundRect(7, T * 0.3f, T - 14, T * 0.45f, T * 0.18f, Hex("#9ED0EC"));
                    break;

                case "bedroom":     // 침대
                    c.RoundRect(2, T * 0.12f, T - 4, T * 0.82f, 6, Hex("#F4F7F9"), Hex("#B9A3C2"), 1.5f);
                    c.RoundRect(6, T * 0.18f, T - 12, T * 0.22f, 5, Hex("#FFFFFF"), Hex("#C9B8D1"), 1f);
                    c.RoundRect(2, T * 0.44f, T - 4, T * 0.5f, 6, Hex("#E7A6C0"));
                    c.Line(5, T * 0.6f, T - 5, T * 0.6f, new Color(1f, 1f, 1f, 0.6f), 1.5f);
                    break;

                case "kids":        // 장난감 상자
                    c.RoundRect(3, T * 0.3f, T - 6, T * 0.6f, 5, Hex("#E0524A"), Hex("#9E2E28"), 1.5f);
                    c.RoundRect(1, T * 0.22f, T - 2, T * 0.16f, 4, Hex("#F2C14E"), Hex("#B7791F"), 1.2f);
                    // 원본은 별 글자를 T*.3 크기로 찍는다. 타일 스프라이트에는 글꼴이 없어
                    // 같은 자리·같은 크기의 오각 별 도형으로 그린다(모양만 대체).
                    Star(c, T / 2f, T * 0.62f, T * 0.16f, Hex("#FFFFFF"));
                    break;

                case "laundry":     // 빨래 바구니
                    c.RoundRect(4, T * 0.34f, T - 8, T * 0.56f, 7, Hex("#D9B47E"), Hex("#9C7A4A"), 1.5f);
                    for (int i = 1; i < 4; i++)
                        c.Line(4, T * (0.34f + i * 0.13f), T - 4, T * (0.34f + i * 0.13f),
                               new Color(120 / 255f, 90 / 255f, 50 / 255f, 0.5f));
                    c.Ellipse(T * 0.35f, T * 0.32f, T * 0.18f, T * 0.1f, Hex("#8FC7F5"));
                    c.Ellipse(T * 0.62f, T * 0.3f, T * 0.16f, T * 0.09f, Hex("#F5A3B5"));
                    break;

                case "boiler":      // 보일러
                    c.RoundRect(T * 0.2f, T * 0.08f, T * 0.6f, T * 0.86f, T * 0.25f, Hex("#D07A44"), Hex("#8E4A22"), 1.5f);
                    c.Circle(T / 2f, T * 0.38f, T * 0.14f, Hex("#F4F7F9"));
                    c.Line(T / 2f, T * 0.38f, T * 0.58f, T * 0.31f, Hex("#1D2226"), 1.5f);
                    break;

                case "storage":     // 수납장
                    c.RoundRect(3, T * 0.28f, T - 6, T * 0.62f, 3, Hex("#C79A5E"), Hex("#8C6A3A"), 1.5f);
                    c.Rect(T * 0.42f, T * 0.28f, T * 0.16f, T * 0.62f, Hex("#E9D9B8"));
                    break;

                case "garage":      // 자동차
                    c.RoundRect(2, T * 0.3f, T - 4, T * 0.5f, T * 0.14f, Hex("#E0524A"), Hex("#9E2E28"), 1.5f);
                    c.RoundRect(T * 0.18f, T * 0.16f, T * 0.64f, T * 0.26f, T * 0.1f, Hex("#F08A82"), Hex("#9E2E28"), 1.2f);
                    c.RoundRect(T * 0.24f, T * 0.2f, T * 0.52f, T * 0.16f, T * 0.06f, Hex("#BFE0F2"));
                    c.Circle(T * 0.25f, T * 0.82f, T * 0.1f, Hex("#1D2226"));
                    c.Circle(T * 0.75f, T * 0.82f, T * 0.1f, Hex("#1D2226"));
                    break;

                case "attic":       // 낡은 상자
                    c.RoundRect(3, T * 0.3f, T - 6, T * 0.6f, 5, Hex("#7A5636"), Hex("#4E3420"), 1.5f);
                    c.RoundRect(3, T * 0.22f, T - 6, T * 0.18f, T * 0.08f, Hex("#8E6644"), Hex("#4E3420"), 1.2f);
                    c.Circle(T / 2f, T * 0.52f, T * 0.06f, Hex("#E8B23A"));
                    break;

                case "veranda":     // 화분
                    c.Polygon(new[] { T * 0.22f, T * 0.78f, T * 0.68f, T * 0.32f },
                              new[] { T * 0.5f, T * 0.5f, T * 0.92f, T * 0.92f }, Hex("#C9764A"));
                    for (int i = 0; i < 5; i++)
                    {
                        float an = -Mathf.PI / 2f + (i - 2) * 0.5f;
                        c.Ellipse(T / 2f + Mathf.Cos(an) * T * 0.2f, T * 0.34f + Mathf.Sin(an) * T * 0.18f,
                                  T * 0.08f, T * 0.17f, Hex("#6E9B4C"));
                    }
                    break;

                case "rooftop":     // 텃밭 상자
                    c.RoundRect(2, T * 0.4f, T - 4, T * 0.5f, 5, Hex("#8C6238"), Hex("#5E3E22"), 1.5f);
                    c.Rect(5, T * 0.46f, T - 10, T * 0.38f, Hex("#6B4A2E"));
                    for (int i = 0; i < 3; i++)
                    {
                        float px = T * (0.25f + i * 0.25f);
                        c.Ellipse(px - 3f, T * 0.4f, 4f, 7f, Hex("#7FC46A"));
                        c.Ellipse(px + 3f, T * 0.4f, 4f, 7f, Hex("#6EAF5A"));
                    }
                    break;

                case "duct":        // 환기구
                    c.RoundRect(3, T * 0.15f, T - 6, T * 0.75f, 4, Hex("#E8ECEF"), Hex("#7E8A93"), 1.5f);
                    for (int i = 1; i < 5; i++)
                    {
                        float x = 3f + i * (T - 6f) / 5f;
                        c.Line(x, T * 0.15f, x, T * 0.9f, Hex("#9AA3AB"));
                    }
                    break;

                case "sewer":       // 밸브
                    c.Circle(T / 2f, T * 0.55f, T * 0.36f, Hex("#C0392B"));
                    c.Circle(T / 2f, T * 0.55f, T * 0.24f, Hex("#93A298"));
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * Mathf.PI / 2f + 0.4f;
                        c.Line(T / 2f, T * 0.55f, T / 2f + Mathf.Cos(a) * T * 0.3f,
                               T * 0.55f + Mathf.Sin(a) * T * 0.3f, Hex("#C0392B"), 3f);
                    }
                    c.Circle(T / 2f, T * 0.55f, T * 0.07f, Hex("#7E1F16"));
                    break;

                case "throne":      // 결정
                    c.RadialGradient(T / 2f, T * 0.55f, 2f, T * 0.45f,
                                     new Color(200 / 255f, 245 / 255f, 1f, 0.95f),
                                     new Color(120 / 255f, 200 / 255f, 240 / 255f, 0f));
                    c.Polygon(new[] { T / 2f, T * 0.76f, T / 2f, T * 0.24f },
                              new[] { T * 0.14f, T * 0.5f, T * 0.92f, T * 0.5f },
                              Hex("#8FE0F5"), Hex("#3D8BD9"), 1.5f);
                    break;

                default:
                    Debug.LogWarning($"목표물 그림이 없는 바닥: {floor}");
                    break;
            }
            return c.Save($"tile_goal_{floor}");
        }

        /// <summary>오각 별. kids 목표물의 별 글자 자리에만 쓴다.</summary>
        static void Star(TileCanvas c, float cx, float cy, float r, Color col)
        {
            var xs = new float[10];
            var ys = new float[10];
            for (int i = 0; i < 10; i++)
            {
                float rad = i % 2 == 0 ? r : r * 0.44f;
                float a = -Mathf.PI / 2f + i * Mathf.PI / 5f;
                xs[i] = cx + Mathf.Cos(a) * rad;
                ys[i] = cy + Mathf.Sin(a) * rad;
            }
            c.Polygon(xs, ys, col);
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
