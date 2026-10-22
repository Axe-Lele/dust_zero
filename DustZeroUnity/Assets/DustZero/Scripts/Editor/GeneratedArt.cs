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

        /// <summary>
        /// A hard-edged disc with one pixel of antialiasing. The rug and other solid ellipses
        /// need this rather than <see cref="Circle"/>: that one fades from 82% of the radius
        /// out, which on a stretched ellipse leaves most of the shape in the falloff and makes
        /// a measured fill read at roughly a third of its intended strength.
        /// </summary>
        public static Sprite Disc(string name, int size = 128)
            => Make(name, size, (x, y, s) =>
            {
                float c = (s - 1) * 0.5f;
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / (s * 0.5f);
                return Mathf.Clamp01((1f - d) * s * 0.5f);
            });

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

        /// <summary>
        /// 대청소 리더(청소기)의 흡입 소용돌이 — 원본은 매 프레임 호 세 개를 그린다
        /// (반지름 R*0.55/0.70/0.85, 길이 1.2 라디안, 선 굵기 2px). 세 호가 같은 속도로 돌기
        /// 때문에 한 장으로 구워 두고 통째로 돌리면 움직임이 원본과 같다.
        ///
        /// 선 굵기는 화면 픽셀 고정값이라 반지름이 커지면 상대적으로 가늘어진다. 구운 그림은
        /// 그 비를 기준 반지름 하나로 고정하므로, 레벨이 올라 범위가 넓어지면 선이 원본보다
        /// 약간 굵게 보인다.
        /// </summary>
        public static Sprite AuraArcs(string name, int size = 256, float thickness = 0.014f,
                                      float sweep = 1.2f, int arcs = 3)
            => Make(name, size, (x, y, s) =>
            {
                float cx = (s - 1) * 0.5f, cy = (s - 1) * 0.5f;
                float dx = x - cx, dy = y - cy;
                float r = Mathf.Sqrt(dx * dx + dy * dy) / (s * 0.5f);
                float angle = Mathf.Atan2(dy, dx);
                float best = 0f;
                for (int i = 0; i < arcs; i++)
                {
                    float ring = 0.55f + 0.15f * i;
                    float band = 1f - Mathf.Clamp01(Mathf.Abs(r - ring) / thickness);
                    if (band <= 0f) continue;
                    // 호의 시작각은 i*2.1 라디안, 길이는 sweep.
                    float from = Mathf.Repeat(angle - i * 2.1f, Mathf.PI * 2f);
                    if (from > sweep) continue;
                    // 끝을 둥글게: 호의 양 끝 0.12 라디안을 부드럽게 줄인다.
                    float cap = Mathf.Min(1f, Mathf.Min(from, sweep - from) / 0.12f);
                    best = Mathf.Max(best, band * cap);
                }
                return best;
            });

        /// <summary>
        /// A thin solid ring at the stove tile's burner radius. BoardView tints and pulses this
        /// at runtime to reproduce the original's per-frame arc (ctx.arc r=k*.26, alpha
        /// oscillating with sin) — unlike DashedRing this one is a single continuous stroke.
        /// </summary>
        public static Sprite StoveRing(string name, int size = 128, float radius = 0.26f, float thickness = 0.035f)
            => Make(name, size, (x, y, s) =>
            {
                float cx = (s - 1) * 0.5f, cy = (s - 1) * 0.5f;
                float dx = x - cx, dy = y - cy;
                float r = Mathf.Sqrt(dx * dx + dy * dy) / s;
                float d = Mathf.Abs(r - radius);
                return Mathf.Clamp01(1f - d / thickness);
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
        /// A solid triangle with its apex at the bottom edge - the pointer on 10.17-e's regen
        /// warning bubble. Rotating the renderer aims it left or right when the bubble has to
        /// sit beside a top-row entrance instead of above it.
        /// </summary>
        public static Sprite Triangle(string name, int size = 64)
            => Make(name, size, (x, y, s) =>
            {
                // Apex at (s/2, 0), base along the top edge. Half-width shrinks with height.
                float v = y / (float)(s - 1);                 // 0 at the apex row
                float half = v * (s * 0.5f);
                return Mathf.Abs(x + 0.5f - s * 0.5f) <= half ? 1f : 0f;
            });

        /// <summary>
        /// The flyer marker: a filled dot with two wing-shaped holes punched out of it. The
        /// holes rather than white paint, so one tinted sprite works on any bubble colour -
        /// what shows through is the bubble's own background, as in the original.
        /// </summary>
        public static Sprite AirDot(string name, int size = 48)
            => Make(name, size, (x, y, s) =>
            {
                float cx = s * 0.5f, cy = s * 0.5f, r = s * 0.5f - 0.5f;
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                if (dx * dx + dy * dy > r * r) return 0f;
                // Two flat ellipses either side of centre, as drawEnemy marks a flyer.
                float ex = s * 0.2f, rw = s * 0.24f, rh = s * 0.14f;
                float lx = (dx + ex) / rw, ly = dy / rh;
                float rx = (dx - ex) / rw, ry = dy / rh;
                if (lx * lx + ly * ly <= 1f || rx * rx + ry * ry <= 1f) return 0f;
                return 1f;
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

        /// <summary>
        /// 리그 판 바닥. 원본 `lgRender` 첫 줄을 그대로 구운 것이다 —
        /// 칸마다 1.5 들여 칠한 **체크무늬**이고, 위 세 줄(먼지 쪽)은 따뜻한 베이지,
        /// 아래 세 줄(우리 쪽)은 차가운 파랑이다. 가운데에는 점선 한 줄이 지나간다.
        /// 한 칸이 월드 1 단위가 되도록 ppu 를 칸 크기로 맞춰 둔다.
        /// </summary>
        public static Sprite LeagueFloor(int cols, int rows, int ourRows, int pixelsPerCell = 48)
        {
            EnsureFolder();
            string name = $"lg_floor_{cols}x{rows}";
            int w = cols * pixelsPerCell, h = rows * pixelsPerCell;
            var px = new Color32[w * h];

            var oursA = Hex32("#DCE9F2");
            var oursB = Hex32("#D3E3EE");
            var foeA = Hex32("#EEDFD6");
            var foeB = Hex32("#E8D5CA");
            var gap = new Color32(0, 0, 0, 0);

            float inset = 1.5f * pixelsPerCell / 56f;   // 원본은 56px 칸에서 1.5 를 들인다
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int top = h - 1 - y;                 // 그림은 아래가 0, 칸은 위에서 센다
                    int row = top / pixelsPerCell, col = x / pixelsPerCell;
                    float inX = x % pixelsPerCell, inY = top % pixelsPerCell;
                    bool edge = inX < inset || inX > pixelsPerCell - 1 - inset
                             || inY < inset || inY > pixelsPerCell - 1 - inset;
                    if (edge) { px[y * w + x] = gap; continue; }

                    bool mine = row >= rows - ourRows;
                    bool odd = (row + col) % 2 != 0;
                    px[y * w + x] = mine ? (odd ? oursA : oursB) : (odd ? foeA : foeB);
                }

            // 가운데 점선 — 원본은 [6,6] 간격에 두께 2 다.
            int mid = h / 2;
            float dash = 6f * pixelsPerCell / 56f, thick = Mathf.Max(1f, 2f * pixelsPerCell / 56f);
            var line = new Color32(29, 34, 38, 77);     // rgba(29,34,38,.3)
            for (int y = mid - (int)(thick / 2f); y <= mid + (int)(thick / 2f); y++)
            {
                if (y < 0 || y >= h) continue;
                for (int x = 0; x < w; x++)
                    if ((int)(x / dash) % 2 == 0) px[y * w + x] = line;
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            System.IO.File.WriteAllBytes($"{Folder}/{name}.png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset($"{Folder}/{name}.png");

            var importer = (TextureImporter)AssetImporter.GetAtPath($"{Folder}/{name}.png");
            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = pixelsPerCell;   // 한 칸 = 월드 1
            importer.filterMode = FilterMode.Bilinear;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>($"{Folder}/{name}.png");
        }

        /// <summary>`#RRGGBB` 를 색으로. 원본 CSS 값을 그대로 적기 위한 것이다.</summary>
        static Color32 Hex32(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        /// <summary>
        /// 모서리만 덮는 가면. 원본 `#svCv { border-radius: 14px }` 처럼 아레나 판의 네 귀를
        /// 둥글게 보이게 한다 — 카메라 뷰포트는 늘 각진 사각형이라 그림으로 덮는 수밖에 없다.
        /// 네 귀퉁이의 **둥근 선 바깥쪽만** 불투명하고 나머지는 완전히 투명해서, 9-slice 로
        /// 어떤 크기에 늘려도 가장자리는 가리지 않는다. 색은 Image 가 정한다(페이지 배경색).
        /// </summary>
        public static Sprite RoundedCornerMask(string name, int radius)
        {
            int r = Mathf.Max(1, radius);
            int size = r * 2 + 2;
            string full = $"{name}_{r}";
            Make(full, size, (x, y, s) =>
            {
                // 가운데 2px 십자(가장자리로 늘어나는 부분)는 가리지 않는다.
                bool midX = x >= r && x < r + 2, midY = y >= r && y < r + 2;
                if (midX || midY) return 0f;
                float cx = x < r ? r - 0.5f : r + 1.5f;
                float cy = y < r ? r - 0.5f : r + 1.5f;
                // 둥근 선 **바깥**이 덮을 자리다.
                return Mathf.Clamp01(Dist(x + 0.5f, y + 0.5f, cx, cy) - r + 0.5f);
            });
            return ApplyNineSlice(full, r);
        }

        /// <summary>
        /// A white-to-clear vertical fade, opaque at the top. Tinted by the Image colour it
        /// becomes 10.16-n's rarity wash - linear-gradient(--sr-bg, --card 60%) - which the
        /// old build faked with a 4px stripe along the card's top edge.
        /// </summary>
        public static Sprite VerticalFade(string name, int size = 64)
        {
            // Opaque for the top 40%, then easing out; the card face shows through below.
            return Make(name, size, (x, y, s) =>
            {
                float v = y / (float)(s - 1);          // 0 at the bottom, 1 at the top
                if (v >= 0.6f) return 1f;
                return Mathf.Clamp01(v / 0.6f);
            });
        }

        /// <summary>
        /// The hairline version of <see cref="RoundedNineSlice"/>: a rounded ring that is
        /// transparent inside, so it can be laid over a filled shape to draw 10.16-n's 1px
        /// border without needing a second colour channel in one sprite.
        /// </summary>
        public static Sprite RoundedOutlineNineSlice(string name, int radius, int thickness)
        {
            int size = Mathf.Max(8, radius * 4);
            string full = $"{name}_{radius}_{thickness}";
            Make(full, size, (x, y, s) =>
            {
                float d = SignedRoundedBox(x + 0.5f, y + 0.5f, s, 0f, radius);
                // Keep only the band just inside the edge.
                float outer = Mathf.Clamp01(0.5f - d);
                float inner = Mathf.Clamp01(0.5f - (d + thickness));
                return outer - inner;
            });
            return ApplyNineSlice(full, radius);
        }

        /// <summary>
        /// Rounded at the top, square at the bottom. 10.16-n's card clips its picture area
        /// against the card's 12px radius, so the picture's top corners are rounded while its
        /// bottom edge - which falls in the middle of the card - stays straight.
        /// </summary>
        public static Sprite RoundedTopNineSlice(string name, int radius)
        {
            int size = Mathf.Max(8, radius * 4);
            string full = name + "_" + radius;
            Make(full, size, (x, y, s) =>
            {
                // y grows upward in texture space, so the rounded end is the high rows.
                float px = x + 0.5f, py = y + 0.5f;
                float hi = s - 1;
                float dx = Mathf.Max(-px, px - hi);
                float dyTop = py - hi;                       // only the top edge is rounded
                float d;
                if (dx <= -radius || dyTop <= -radius) d = Mathf.Max(dx, dyTop);
                else
                {
                    float ox = Mathf.Max(dx + radius, 0f), oy = Mathf.Max(dyTop + radius, 0f);
                    d = Mathf.Sqrt(ox * ox + oy * oy) - radius;
                }
                d = Mathf.Max(d, -py);                       // hard bottom edge
                return Mathf.Clamp01(0.5f - d);
            });
            // Bottom border stays 0 so the square edge is never stretched into a curve.
            return ApplyBorder(full, new Vector4(radius, 0f, radius, radius));
        }

        /// <summary>
        /// A dashed rounded outline at an exact pixel size. Dashes cannot survive 9-slicing,
        /// so this is drawn for one rect size - which is all the deck slots need, since every
        /// empty slot is the same box.
        /// </summary>
        public static Sprite DashedRoundedOutline(string name, int w, int h, int radius,
                                                  float thickness, float dash, float gap)
        {
            EnsureFolder();
            string full = name + "_" + w + "x" + h;
            var px = new Color32[w * h];

            float r = Mathf.Min(radius, Mathf.Min(w, h) * 0.5f);
            float half = thickness * 0.5f;
            // Walk the perimeter and stamp a round cap wherever a dash is on. Straight runs
            // and quarter arcs share one arc-length parameter, so the dash rhythm carries
            // around the corners instead of restarting at each one.
            float sx = w - 2f * r, sy = h - 2f * r, arc = Mathf.PI * r * 0.5f;
            float total = 2f * sx + 2f * sy + 4f * arc;
            float step = Mathf.Max(0.25f, half * 0.5f);

            for (float t = 0f; t < total; t += step)
            {
                if (Mathf.Repeat(t, dash + gap) >= dash) continue;

                float u = t, cx, cy, a;
                if (u < sx) { cx = r + u; cy = half; }
                else if ((u -= sx) < arc)
                { a = u / arc * Mathf.PI * 0.5f; cx = w - r + Mathf.Sin(a) * r; cy = r - Mathf.Cos(a) * r; }
                else if ((u -= arc) < sy) { cx = w - half; cy = r + u; }
                else if ((u -= sy) < arc)
                { a = u / arc * Mathf.PI * 0.5f; cx = w - r + Mathf.Cos(a) * r; cy = h - r + Mathf.Sin(a) * r; }
                else if ((u -= arc) < sx) { cx = w - r - u; cy = h - half; }
                else if ((u -= sx) < arc)
                { a = u / arc * Mathf.PI * 0.5f; cx = r - Mathf.Sin(a) * r; cy = h - r + Mathf.Cos(a) * r; }
                else if ((u -= arc) < sy) { cx = half; cy = h - r - u; }
                else { a = (u - sy) / arc * Mathf.PI * 0.5f; cx = r - Mathf.Cos(a) * r; cy = r - Mathf.Sin(a) * r; }

                int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - half - 1));
                int x1 = Mathf.Min(w - 1, Mathf.CeilToInt(cx + half + 1));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - half - 1));
                int y1 = Mathf.Min(h - 1, Mathf.CeilToInt(cy + half + 1));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float ddx = x + 0.5f - cx, ddy = y + 0.5f - cy;
                        float av = Mathf.Clamp01(half - Mathf.Sqrt(ddx * ddx + ddy * ddy) + 0.5f);
                        byte prev = px[y * w + x].a;
                        byte now = (byte)Mathf.RoundToInt(av * 255f);
                        px[y * w + x] = new Color32(255, 255, 255, now > prev ? now : prev);
                    }
            }

            var path = Folder + "/" + full + ".png";
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>
        /// The three common-gear icons the build draws on a canvas: a spoked wheel for 공격,
        /// radiator fins for 과열 한도, a battery for 스킬 충전. White shapes, tinted by the
        /// grade colour at runtime - the same way the build picks GRADE_COL per piece.
        /// </summary>
        /// <summary>
        /// 구역 카드의 집 단면도 — 원본 `drawHouse`.
        ///
        /// 위에서부터 꼭대기(4) · 2층(2) · 1층(1) · 지하(3) · 먼지 왕국(5)
        /// 다섯 칸을 쌓고, 보고 있는 구역만 밝게 둔다. 원본은 매번 캔버스에 다시
        /// 그리지만 우리는 구역마다 한 장씩 구워 둔다 — 다섯 장이면 끝이다.
        /// </summary>
        public static Sprite HouseCutaway(int selectedArea, int w = 158, int h = 190)
        {
            string full = $"house_{selectedArea}";
            var path = Folder + "/" + full + ".png";

            Color32 sky   = Rgb(0xDD, 0xEF, 0xF8);
            Color32 soil  = Rgb(0xB5, 0x9A, 0x78);
            Color32 realm = Rgb(0x4E, 0x3B, 0x5C);
            Color32 grass = Rgb(0x7F, 0xC4, 0x6A);
            Color32 roofOn= Rgb(0xB8, 0x5A, 0x3A);
            Color32 roof  = Rgb(0xC9, 0xA2, 0x8A);
            Color32 wood  = Rgb(0xDC, 0xC3, 0x9A);

            var px = new Color32[w * h];

            // 원본과 같은 비율: 위 20% 는 지붕 자리, 나머지를 다섯 칸으로.
            float top = h * 0.2f;
            float bh = (h - top - 4f) / 5f;
            float x0 = w * 0.1f, x1 = w * 0.9f;
            int[] order = { 4, 2, 1, 3, 5 };

            // 텍스처는 아래가 0 이라 위에서 재는 y 는 뒤집어 쓴다.
            for (int yTop = 0; yTop < h; yTop++)
            {
                Color32 band = yTop < top + bh * 3f ? sky
                             : yTop < top + bh * 4f ? soil : realm;
                int y = h - 1 - yTop;
                for (int x = 0; x < w; x++) px[y * w + x] = band;
            }

            void Fill(float fx, float fyTop, float fw, float fh, Color32 col, float alpha)
            {
                int ax = Mathf.Clamp(Mathf.RoundToInt(fx), 0, w);
                int bx = Mathf.Clamp(Mathf.RoundToInt(fx + fw), 0, w);
                int ay = Mathf.Clamp(Mathf.RoundToInt(fyTop), 0, h);
                int by = Mathf.Clamp(Mathf.RoundToInt(fyTop + fh), 0, h);
                for (int yTop = ay; yTop < by; yTop++)
                {
                    int y = h - 1 - yTop;
                    if (y < 0 || y >= h) continue;
                    for (int x = ax; x < bx; x++)
                    {
                        var d = px[y * w + x];
                        px[y * w + x] = new Color32(
                            (byte)Mathf.RoundToInt(Mathf.Lerp(d.r, col.r, alpha)),
                            (byte)Mathf.RoundToInt(Mathf.Lerp(d.g, col.g, alpha)),
                            (byte)Mathf.RoundToInt(Mathf.Lerp(d.b, col.b, alpha)),
                            255);
                    }
                }
            }

            Fill(0f, top + bh * 3f - 3f, w, 4f, grass, 1f);

            // 지붕 — 가운데가 솟은 삼각형
            float peak = top - h * 0.17f;
            var roofCol = selectedArea == 4 ? roofOn : roof;
            for (int yTop = Mathf.Max(0, Mathf.RoundToInt(peak)); yTop < top + 2f; yTop++)
            {
                float t = (yTop - peak) / Mathf.Max(1f, top + 2f - peak);
                float half = Mathf.Lerp(0f, (x1 + 8f - (x0 - 8f)) * 0.5f, t);
                Fill(w * 0.5f - half, yTop, half * 2f, 1f, roofCol, 1f);
            }

            // 다섯 칸. 한 구역은 챕터 셋이라 가로로 세 칸이다.
            float rw = (x1 - x0) / 3f;
            for (int row = 0; row < order.Length; row++)
            {
                float y = top + row * bh;
                for (int i = 0; i < 3; i++)
                    Fill(x0 + i * rw + 1f, y + 1f, rw - 2f, bh - 2f, wood, 1f);

                // 보고 있는 구역이 아니면 덮어서 가라앉힌다.
                if (order[row] != selectedArea)
                    Fill(x0, y, x1 - x0, bh, Rgb(0x1D, 0x22, 0x26), 0.42f);
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static Color32 Rgb(int r, int g, int b) => new Color32((byte)r, (byte)g, (byte)b, 255);

        /// <summary>
        /// 대청소 바닥 — 원본 `survRender` 의 바닥 그리기를 그대로 옮긴 것이다.
        ///
        /// 아레나 전체(`survArena` 칸)를 한 장으로 굽는다. 원본은 매 프레임 캔버스에
        /// 다시 그리지만 바닥은 고정이라 한 장이면 끝난다. 색과 치수는 전부 원본 값이다:
        /// 바탕 `#DCC39A`, 격자 `rgba(120,84,40,.14)` — 가로줄은 한 칸마다, 세로 금은
        /// 세 칸마다 긋되 홀수 줄은 1.5칸 밀린다. 러그 넷과 벽 테두리도 같은 자리.
        /// </summary>
        public static Sprite ArenaFloor(int arenaTiles, int pixelsPerTile = 48)
        {
            string full = $"surv_floor_{arenaTiles}";
            var path = Folder + "/" + full + ".png";

            int k = pixelsPerTile;
            int n = Mathf.Max(1, arenaTiles);
            int w = n * k, h = w;
            var px = new Color32[w * h];

            Color32 plank = Rgb(0xDC, 0xC3, 0x9A);
            for (int i = 0; i < px.Length; i++) px[i] = plank;

            // 텍스처는 아래가 0 이라 위에서 잰 y 는 뒤집어 쓴다.
            void Put(int x, int yTop, Color32 col, float a)
            {
                if (x < 0 || x >= w || yTop < 0 || yTop >= h) return;
                int y = h - 1 - yTop;
                var d = px[y * w + x];
                px[y * w + x] = new Color32(
                    (byte)Mathf.RoundToInt(Mathf.Lerp(d.r, col.r, a)),
                    (byte)Mathf.RoundToInt(Mathf.Lerp(d.g, col.g, a)),
                    (byte)Mathf.RoundToInt(Mathf.Lerp(d.b, col.b, a)), 255);
            }

            void Rect(float fx, float fy, float fw, float fh, Color32 col, float a)
            {
                int x0 = Mathf.RoundToInt(fx), x1 = Mathf.RoundToInt(fx + fw);
                int y0 = Mathf.RoundToInt(fy), y1 = Mathf.RoundToInt(fy + fh);
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++) Put(x, y, col, a);
            }

            // 러그 — 원본과 같은 칸 좌표/색
            Rect(4f * k, 4f * k, 5f * k, 3.5f * k, Rgb(0x5B, 0x7C, 0x99), 0.25f);
            Rect(13f * k, 6f * k, 4f * k, 4f * k, Rgb(0xE8, 0x84, 0x2A), 0.18f);
            Rect(6f * k, 14f * k, 6f * k, 4f * k, Rgb(0x6E, 0x9B, 0x4C), 0.18f);
            Rect(15f * k, 15f * k, 4.5f * k, 3f * k, Rgb(0x5B, 0x7C, 0x99), 0.22f);

            // 판자 격자
            Color32 line = Rgb(120, 84, 40);
            const float LineA = 0.14f;
            for (int ty = 0; ty <= n; ty++)
            {
                int y = Mathf.Min(h - 1, ty * k);
                for (int x = 0; x < w; x++) Put(x, y, line, LineA);

                for (float tx = (ty % 2) * 1.5f - 3f; tx <= n + 1; tx += 3f)
                {
                    int x = Mathf.RoundToInt(tx * k);
                    for (int y2 = y; y2 < y + k && y2 < h; y2++) Put(x, y2, line, LineA);
                }
            }

            // 벽 — 원본은 lineWidth k*.35 로 아레나 바깥을 두른다.
            int wall = Mathf.Max(1, Mathf.RoundToInt(k * 0.35f));
            Color32 wallCol = Rgb(0xA8, 0xB4, 0xBD);
            Rect(0f, 0f, w, wall, wallCol, 1f);
            Rect(0f, h - wall, w, wall, wallCol, 1f);
            Rect(0f, 0f, wall, h, wallCol, 1f);
            Rect(w - wall, 0f, wall, h, wallCol, 1f);

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            // 한 칸이 월드 1 단위가 되도록 굽는다.
            importer.spritePixelsPerUnit = k;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        public static Sprite GearIcon(string type, int size = 96)
            => Make("ui_gear_" + type, size, (x, y, s) =>
            {
                float px = x + 0.5f, py = y + 0.5f, m = s * 0.5f;
                switch (type)
                {
                    case "atk":
                    {
                        // Hub, six spokes, and a bite out of the centre.
                        float d = Dist(px, py, m, m);
                        float wheel = Band(d, s * 0.30f, s * 0.07f);
                        float hub = Mathf.Clamp01(s * 0.08f - d);
                        float spokes = 0f;
                        for (int i = 0; i < 6; i++)
                        {
                            float a = i / 6f * Mathf.PI * 2f;
                            spokes = Mathf.Max(spokes, Segment(px, py, m, m,
                                m + Mathf.Cos(a) * s * 0.26f, m + Mathf.Sin(a) * s * 0.26f, s * 0.035f));
                        }
                        float disc = Mathf.Clamp01(s * 0.30f - d);
                        return Mathf.Clamp01(Mathf.Max(Mathf.Max(wheel, spokes), disc) - hub);
                    }
                    case "heat":
                    {
                        // Five upright fins on a base rail.
                        float v = Rect(px, py, s * 0.16f, s * 0.70f, s * 0.68f, s * 0.10f);
                        for (int i = 0; i < 5; i++)
                            v = Mathf.Max(v, Rect(px, py, s * 0.20f + i * s * 0.14f, s * 0.22f,
                                                  s * 0.08f, s * 0.56f));
                        return v;
                    }
                    default:
                    {
                        // Battery shell with a terminal on top.
                        float body = Rect(px, py, s * 0.26f, s * 0.24f, s * 0.48f, s * 0.56f);
                        float cap = Rect(px, py, s * 0.40f, s * 0.16f, s * 0.20f, s * 0.08f);
                        return Mathf.Max(body, cap);
                    }
                }
            });

        static float Dist(float x, float y, float cx, float cy)
            => Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));

        /// <summary>A soft ring of a given thickness at a radius.</summary>
        static float Band(float d, float radius, float half)
            => Mathf.Clamp01(half - Mathf.Abs(d - radius) + 0.5f);

        static float Rect(float x, float y, float rx, float ry, float w, float h)
            => x >= rx && x <= rx + w && y >= ry && y <= ry + h ? 1f : 0f;

        /// <summary>Distance-to-segment, as coverage for a stroke of the given half width.</summary>
        static float Segment(float px, float py, float ax, float ay, float bx, float by, float half)
        {
            float dx = bx - ax, dy = by - ay;
            float len2 = dx * dx + dy * dy;
            float t = len2 <= 0f ? 0f : Mathf.Clamp01(((px - ax) * dx + (py - ay) * dy) / len2);
            float cx = ax + dx * t, cy = ay + dy * t;
            return Mathf.Clamp01(half - Dist(px, py, cx, cy) + 0.5f);
        }

        static Sprite MakeRounded(string name, int size, int radius)
            => Make(name, size, (x, y, s) =>
            {
                float d = SignedRoundedBox(x + 0.5f, y + 0.5f, s, 0f, radius);
                return Mathf.Clamp01(0.5f - d);
            });

        /// <summary>Marks an already-written sprite as 9-sliced with a radius-sized border.</summary>
        static Sprite ApplyNineSlice(string name, int radius)
            => ApplyBorder(name, new Vector4(radius, radius, radius, radius));

        /// <summary>9-slices an already-written sprite with an explicit L/B/R/T border.</summary>
        static Sprite ApplyBorder(string name, Vector4 border)
        {
            var path = $"{Folder}/{name}.png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteBorder = border;
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            importer.SetTextureSettings(settings);
            importer.spritePixelsPerUnit = 100f;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

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
