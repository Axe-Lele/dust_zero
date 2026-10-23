// 판 한 장을 원본 buildBoard 그대로 굽는다.
//
// 왜 타일맵이 아니라 한 장인가
//   원본 paintFloor 의 무늬 아홉 가운데 넷(concrete · grass · royal, 그리고 mat)은 칸의
//   (열, 행)을 **그대로** 식에 넣는다. 예를 들어 콘크리트의 얼룩 자리는
//   `((col*37 + r*17 + i*29) % 97) / 97 * T` 다 — 주기가 97과 89라 7x10 판에서 칸마다
//   전부 다르다. 타일 스프라이트로 구우려면 바닥 하나에 70장이 필요하다.
//   원본은 애초에 판 전체를 오프스크린 캔버스 한 장에 그린다(buildBoard). 그래서
//   여기서도 판 하나를 한 장으로 굽는다. 식이 원본과 같으니 얼룩 자리까지 같다.
//
// 좌표는 원본과 같다 — 원점 왼쪽 위, y 는 아래로. 칸 (col, r) 은 (col*T, r*T) 에서 시작한다.
using System.Collections.Generic;
using UnityEngine;

namespace DustZero.Editor
{
    /// <summary>원본의 바닥·벽·가구·장판 그림. 색과 수치는 모두 원본 것이다.</summary>
    public static class BoardPainter
    {
        /// <summary>한 칸을 몇 픽셀로 굽는가. 스프라이트 PPU 와 같아 한 칸이 1 월드 단위다.</summary>
        public const int Tile = 64;

        static Color Hex(string rgb, float a = 1f)
        {
            ColorUtility.TryParseHtmlString(rgb, out var c);
            c.a = a;
            return c;
        }

        static Color Rgba(int r, int g, int b, float a) => new(r / 255f, g / 255f, b / 255f, a);

        // ---------------------------------------------------------------- 원본 표

        /// <summary>FLOOR — 바닥 바탕색.</summary>
        static readonly Dictionary<string, string> FloorColor = new()
        {
            ["wood"] = "#DCC39A", ["kitchen"] = "#E4E7E1", ["bath"] = "#CDE2E9",
            ["bedroom"] = "#EAD9E2", ["kids"] = "#F2E2B4", ["laundry"] = "#D6E5EC",
            ["boiler"] = "#CFC7BC", ["storage"] = "#C2AA8A", ["garage"] = "#BEC2C6",
            ["attic"] = "#CDB18B", ["veranda"] = "#D9D2C3", ["rooftop"] = "#B9D69C",
            ["duct"] = "#B9C1C9", ["sewer"] = "#93A298", ["throne"] = "#B9A6C8",
        };

        /// <summary>FLOOR_STYLE — 바닥 무늬.</summary>
        static readonly Dictionary<string, string> FloorStyle = new()
        {
            ["wood"] = "plank", ["bedroom"] = "plank", ["storage"] = "plank", ["attic"] = "plank",
            ["kitchen"] = "tile", ["veranda"] = "tile",
            ["bath"] = "tile2", ["laundry"] = "tile2",
            ["kids"] = "mat",
            ["boiler"] = "concrete", ["garage"] = "concrete",
            ["rooftop"] = "grass", ["duct"] = "metal", ["sewer"] = "brick", ["throne"] = "royal",
        };

        /// <summary>WALL_COL — 벽/가구 바탕. 없으면 #C3CBD1.</summary>
        static readonly Dictionary<string, string> WallColor = new()
        {
            ["sewer"] = "#7E8A83", ["duct"] = "#9AA3AB", ["throne"] = "#8E7CA0",
            ["garage"] = "#A9AFB5", ["boiler"] = "#B5ADA3", ["storage"] = "#A3917A",
            ["attic"] = "#AC9574",
        };

        /// <summary>FURN_COL — 가구 윗면. 없으면 #F3F5F6.</summary>
        static readonly Dictionary<string, string> FurnitureColor = new()
        {
            ["kitchen"] = "#EEF0EC", ["garage"] = "#DDE1E4", ["boiler"] = "#ECE6DC",
            ["sewer"] = "#D3DCD6", ["throne"] = "#EDE3F3", ["storage"] = "#EFE6D8",
            ["attic"] = "#F1E7D6", ["duct"] = "#E8ECEF",
        };

        static Color Wall(string floor) => Hex(WallColor.TryGetValue(floor ?? "", out var h) ? h : "#C3CBD1");
        static Color Furn(string floor) => Hex(FurnitureColor.TryGetValue(floor ?? "", out var h) ? h : "#F3F5F6");

        // ---------------------------------------------------------------- 판 한 장

        /// <summary>
        /// 지도 하나를 한 장으로 굽는다. <paramref name="tiles"/> 는 행 단위 문자열이고
        /// 글자는 X 벽 · H 가구 · F 화구 · W 물 · R 러그 · O 기름 · 그 밖은 맨바닥이다.
        /// </summary>
        /// <example><code>
        /// var sprite = BoardPainter.Board("living", "wood", map.tiles);
        /// </code></example>
        public static Sprite Board(string mapId, string floor, string[] tiles)
        {
            int rows = tiles.Length, cols = tiles[0].Length;
            var c = new TileCanvas(cols, rows, Tile);
            float T = c.T;

            for (int r = 0; r < rows; r++)
                for (int col = 0; col < cols; col++)
                {
                    char ch = tiles[r][col];
                    float x = col * T, y = r * T;

                    if (ch == 'X')
                    {
                        c.Rect(x, y, T, T, Wall(floor));
                        c.Clip(x, y, T, T);
                        for (float i = -T; i < T * 2f; i += 7f)
                            c.Line(x + i, y, x + i - T, y + T, Rgba(29, 34, 38, 0.07f));
                        c.NoClip();
                        continue;
                    }

                    if (ch == 'H')
                    {
                        c.Rect(x, y, T, T, Wall(floor));
                        c.RoundRect(x + 3, y + 3, T - 6, T - 6, 6, Furn(floor), Hex("#AEB9C1"), 1f);
                        c.Rect(x + 4, y + T - 9, T - 8, 5, Rgba(29, 34, 38, 0.06f));
                        continue;
                    }

                    PaintFloor(c, x, y, T, floor, col, r);

                    if (ch == 'F' && (floor == "boiler" || floor == "throne"))
                    {
                        // 뜨거운 배관
                        c.RoundRect(x + 2, y + T * 0.3f, T - 4, T * 0.4f, T * 0.18f, Hex("#C46A3A"), Hex("#7E3E1C"), 1.5f);
                        c.RoundRect(x + T * 0.4f, y + T * 0.22f, T * 0.2f, T * 0.56f, 3, Hex("#A9552B"), Hex("#7E3E1C"), 1.2f);
                        c.Line(x + 5, y + T * 0.38f, x + T - 5, y + T * 0.38f, Rgba(255, 230, 200, 0.5f), 1.5f);
                    }
                    else if (ch == 'F')
                    {
                        c.RoundRect(x + 4, y + 4, T - 8, T - 8, 6, Hex("#2F3438"));
                        c.CircleStroke(x + T / 2f, y + T / 2f, T * 0.26f, Hex("#5A6167"), 2f);
                        c.CircleStroke(x + T / 2f, y + T / 2f, T * 0.14f, Hex("#5A6167"), 2f);
                    }
                    else if (ch == 'W')
                    {
                        c.Ellipse(x + T * 0.5f, y + T * 0.55f, T * 0.42f, T * 0.3f, Rgba(61, 139, 217, 0.3f));
                        c.Ellipse(x + T * 0.38f, y + T * 0.45f, T * 0.12f, T * 0.05f, new Color(1f, 1f, 1f, 0.7f));
                    }
                    else if (ch == 'R' || ch == 'O')
                    {
                        PaintSpecial(c, x, y, T, ch, floor);
                    }
                }

            return c.Save($"board_{mapId}");
        }

        // ---------------------------------------------------------------- paintFloor

        /// <summary>
        /// 원본 paintFloor. <paramref name="col"/> 과 <paramref name="r"/> 이 무늬를 흔드는
        /// 씨앗이다 — 그대로 넣어야 얼룩과 이음매가 원본 자리에 선다.
        /// </summary>
        static void PaintFloor(TileCanvas c, float x, float y, float T, string floor, int col, int r)
        {
            string st = FloorStyle.TryGetValue(floor ?? "", out var s) ? s : "tile";
            c.Rect(x, y, T, T, Hex(FloorColor.TryGetValue(floor ?? "", out var f) ? f : "#DCC39A"));

            switch (st)
            {
                case "mat":
                {
                    string[] pal = { "#F2E2B4", "#F6C9C0", "#C9E3F2", "#CFE8C3" };
                    float h = T / 2f;
                    for (int i = 0; i < 4; i++)
                        c.Rect(x + (i % 2) * h, y + (i >> 1) * h, h, h, Hex(pal[Mod(col + r + i, 4)]));
                    c.StrokeRect(x + 0.5f, y + 0.5f, T - 1, T - 1, Rgba(120, 90, 40, 0.12f));
                    return;
                }

                case "concrete":
                {
                    var speck = Rgba(60, 60, 60, 0.07f);
                    for (int i = 0; i < 6; i++)
                    {
                        float px = Mod(col * 37 + r * 17 + i * 29, 97) / 97f * T;
                        float py = Mod(col * 13 + r * 41 + i * 53, 89) / 89f * T;
                        c.Rect(x + px, y + py, 2, 2, speck);
                    }
                    var crack = Rgba(60, 60, 60, 0.12f);
                    if (Mod(col + r, 3) == 0) c.Line(x, y + T * 0.3f, x + T * 0.4f, y + T * 0.45f, crack);
                    c.StrokeRect(x + 0.5f, y + 0.5f, T - 1, T - 1, crack);
                    return;
                }

                case "grass":
                {
                    var blade = Rgba(60, 110, 40, 0.35f);
                    for (int i = 0; i < 5; i++)
                    {
                        float px = x + Mod(col * 31 + r * 7 + i * 19, 23) / 23f * T;
                        float py = y + Mod(col * 11 + r * 29 + i * 7, 19) / 19f * T;
                        c.Line(px, py + 4, px - 1.5f, py, blade, 1.2f);
                        c.Line(px, py + 4, px + 1.5f, py, blade, 1.2f);
                    }
                    return;
                }

                case "metal":
                {
                    c.StrokeRect(x + 1.5f, y + 1.5f, T - 3, T - 3, Rgba(40, 50, 60, 0.18f));
                    var rivet = Rgba(40, 50, 60, 0.25f);
                    foreach (var (a, b) in new[] { (0.15f, 0.15f), (0.85f, 0.15f), (0.15f, 0.85f), (0.85f, 0.85f) })
                        c.Circle(x + a * T, y + b * T, 1.6f, rivet);
                    c.Line(x + T * 0.25f, y + T * 0.6f, x + T * 0.6f, y + T * 0.25f, new Color(1f, 1f, 1f, 0.25f));
                    return;
                }

                case "brick":
                {
                    var mortar = Rgba(30, 40, 35, 0.22f);
                    for (int i = 0; i < 3; i++)
                    {
                        float yy = y + i * T / 3f;
                        c.Line(x, yy, x + T, yy, mortar);
                        float o = Mod(i + r, 2) != 0 ? T / 2f : 0f;
                        c.Line(x + o, yy, x + o, yy + T / 3f, mortar);
                    }
                    return;
                }

                case "royal":
                {
                    if (Mod(col + r, 2) == 0) c.Rect(x, y, T, T, Rgba(110, 60, 120, 0.12f));
                    c.Circle(x + T / 2f, y + T / 2f, T * 0.06f, Rgba(255, 215, 120, 0.35f));
                    c.Circle(x + Mod(col * 29 + r * 13, 17) / 17f * T,
                             y + Mod(col * 7 + r * 23, 13) / 13f * T,
                             T * 0.12f, Rgba(90, 80, 90, 0.14f));
                    return;
                }

                case "plank":
                {
                    var seam = Rgba(120, 84, 40, 0.18f);
                    for (int i = 1; i < 3; i++) c.Line(x, y + i * T / 3f, x + T, y + i * T / 3f, seam);
                    float o = Mod(col * 7 + r * 3, 3) * T / 3f;
                    c.Line(x + o, y, x + o, y + T / 3f, seam);
                    float o2 = (o + T / 2f) % T;
                    c.Line(x + o2, y + T / 3f, x + o2, y + 2 * T / 3f, seam);
                    return;
                }

                default:
                {
                    if (st == "tile" && Mod(col + r, 2) == 0) c.Rect(x, y, T, T, Rgba(29, 34, 38, 0.04f));
                    var grout = st == "tile2" ? Rgba(40, 90, 110, 0.15f) : Rgba(29, 34, 38, 0.10f);
                    c.StrokeRect(x + 0.5f, y + 0.5f, T - 1, T - 1, grout);
                    if (st == "tile2")
                    {
                        c.Line(x + T / 2f, y, x + T / 2f, y + T, grout);
                        c.Line(x, y + T / 2f, x + T, y + T / 2f, grout);
                    }
                    return;
                }
            }
        }

        /// <summary>러그(R)와 기름 웅덩이(O). 원본 paintSpecial.</summary>
        static void PaintSpecial(TileCanvas c, float x, float y, float T, char ch, string floor)
        {
            if (ch == 'R')
            {
                bool roof = floor == "rooftop";
                c.RoundRect(x + 3, y + 3, T - 6, T - 6, 5,
                            roof ? Rgba(70, 140, 60, 0.38f) : Rgba(205, 110, 130, 0.32f));
                c.Clip(x + 3, y + 3, T - 6, T - 6);
                var stripe = roof ? Rgba(50, 110, 40, 0.35f) : Rgba(170, 70, 95, 0.35f);
                for (float i = -T; i < T * 2f; i += 8f) c.Line(x + i, y, x + i - T, y + T, stripe, 2f);
                c.NoClip();
                c.DashedRect(x + 5.5f, y + 5.5f, T - 11, T - 11, new Color(1f, 1f, 1f, 0.6f), 1f, 2f, 3f);
                return;
            }

            c.Ellipse(x + T * 0.5f, y + T * 0.55f, T * 0.42f, T * 0.3f, Rgba(25, 25, 30, 0.78f));
            c.Ellipse(x + T * 0.62f, y + T * 0.42f, T * 0.16f, T * 0.08f, Rgba(140, 90, 200, 0.45f));
            c.Ellipse(x + T * 0.42f, y + T * 0.5f, T * 0.12f, T * 0.05f, Rgba(80, 200, 200, 0.4f));
            c.Ellipse(x + T * 0.35f, y + T * 0.62f, T * 0.1f, T * 0.04f, new Color(1f, 1f, 1f, 0.35f));
        }

        /// <summary>자바스크립트 % 와 달리 C# 의 % 는 음수를 낸다. 씨앗은 늘 0 이상이어야 한다.</summary>
        static int Mod(int a, int m) => ((a % m) + m) % m;
    }
}
