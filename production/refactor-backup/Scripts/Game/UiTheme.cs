// The redesign's design tokens, in one place.
//
// Transcribed from design/redesign-2026-10-16/구현 가이드.dc.html. Every colour, size and
// spacing value the UI uses comes from here, so a token change is one edit rather than a
// hunt through screen builders. Nothing in this file is invented: where the guide gave a
// range the midpoint is used and the range is named in the comment.
//
// Units: the guide quotes points against a 390pt mockup and pixels against the 1080x1920
// canvas it maps onto, so 1pt = 1080/390 = 2.769px. Sizes here are canvas pixels; call
// <see cref="Pt"/> to convert a point value the guide quotes.
using UnityEngine;

namespace DustZero.Game
{
    /// <summary>Colours, type scale and metrics from the UI redesign guide.</summary>
    public static class UiTheme
    {
        // ------------------------------------------------------------------ units

        /// <summary>Canvas reference resolution the Canvas Scaler is set to.</summary>
        public static readonly Vector2 Reference = new(1080f, 1920f);

        /// <summary>Mockup points to canvas pixels. The guide's conversion: 1pt = 2.77px.</summary>
        public const float PxPerPt = 1080f / 390f;

        /// <summary>Converts a point value quoted in the guide to canvas pixels.</summary>
        public static float Pt(float points) => points * PxPerPt;

        // ------------------------------------------------------------------ colour

        static Color Hex(string rgb)
        {
            ColorUtility.TryParseHtmlString(rgb, out var c);
            return c;
        }

        /// <summary>Main action, selection, range. White text on it only at 18px+ bold.</summary>
        public static readonly Color Primary = Hex("#E8701A");
        /// <summary>The solid thickness under a primary button, and its pressed state.</summary>
        public static readonly Color PrimaryEdge = Hex("#B9560F");
        /// <summary>Secondary action - the survivor mode entry.</summary>
        public static readonly Color Navy = Hex("#2F4A7A");
        public static readonly Color NavyEdge = Hex("#223758");
        /// <summary>Claimable, placeable.</summary>
        public static readonly Color Success = Hex("#3FA66B");
        public static readonly Color SuccessEdge = Hex("#2E7D4F");
        /// <summary>Cannot place, overheated, notification dot.</summary>
        public static readonly Color Danger = Hex("#E0533D");
        /// <summary>Power and coins. Text on it must be <see cref="PowerInk"/>, not white.</summary>
        public static readonly Color Power = Hex("#F5C028");
        public static readonly Color PowerInk = Hex("#7A5A00");
        /// <summary>Crystals, season pass, SR.</summary>
        public static readonly Color Premium = Hex("#8E5BD6");

        /// <summary>Body and headings.</summary>
        public static readonly Color Ink = Hex("#2B2420");
        /// <summary>Secondary text; both clear 4.5:1 on white.</summary>
        public static readonly Color Ink2 = Hex("#5E5046");
        public static readonly Color Ink3 = Hex("#6B5D51");

        /// <summary>Panels, tiles, the bottom bar.</summary>
        public static readonly Color Panel = Hex("#FFFDF8");
        /// <summary>Secondary buttons and input areas.</summary>
        public static readonly Color Panel2 = Hex("#F1E8DB");
        public static readonly Color PanelLine = Hex("#E8DCCB");
        /// <summary>Page background behind everything.</summary>
        public static readonly Color PageBg = Hex("#EDE6DB");

        /// <summary>Inactive and locked; its text is <see cref="Ink3"/>.</summary>
        public static readonly Color Disabled = Hex("#E4DCD1");

        /// <summary>Panel drop shadow: a 2pt solid band below, Ink at 12%.</summary>
        public static Color PanelShadow => new(Ink.r, Ink.g, Ink.b, 0.12f);

        /// <summary>Rarity. Shown with the letter as well as the colour, for colour blindness.</summary>
        public static readonly Color RarityN = Hex("#9AA0A6");
        public static readonly Color RarityR = Hex("#3E8ED0");
        public static readonly Color RaritySR = Hex("#8E5BD6");
        /// <summary>SSR's badge text stays Ink - white would not carry on this yellow.</summary>
        public static readonly Color RaritySSR = Hex("#E6A817");

        /// <summary>Rarity colour by index, 0 = N through 3 = SSR.</summary>
        public static Color Rarity(int tier) => tier switch
        {
            3 => RaritySSR,
            2 => RaritySR,
            1 => RarityR,
            _ => RarityN,
        };

        public static string RarityName(int tier) => tier switch
        {
            3 => "SSR", 2 => "SR", 1 => "R", _ => "N",
        };

        /// <summary>Text that reads on a rarity swatch.</summary>
        public static Color RarityInk(int tier) => tier == 3 ? Ink : Color.white;

        // Battlefield.
        /// <summary>The route the stains walk.</summary>
        public static readonly Color FieldPath = Hex("#DDB98A");
        /// <summary>A tile an appliance can take.</summary>
        public static readonly Color FieldBuildable = Hex("#FFFDF8");
        public static readonly Color FieldBlocked = Hex("#CDBFAE");
        public static readonly Color FieldSpawn = Hex("#8DBBE6");
        public static readonly Color FieldGoal = Hex("#F2B3A3");

        // ------------------------------------------------------------------ type

        /// <summary>Screen titles and main buttons. Guide: 66-72px.</summary>
        public const float Display = 69f;
        /// <summary>Popup and sheet titles. Guide: 55-61px.</summary>
        public const float Title = 58f;
        /// <summary>Currency, power, wave. Guide: 44-50px.</summary>
        public const float NumberL = 47f;
        /// <summary>Descriptions and button text. Guide: 36-39px.</summary>
        public const float Body = 38f;
        /// <summary>Tab names and chips.</summary>
        public const float LabelSize = 33f;
        /// <summary>Floor. Nothing is smaller except a 10pt badge numeral.</summary>
        public const float Caption = 30f;

        // ------------------------------------------------------------------ metrics

        /// <summary>The 4pt spacing scale, in canvas pixels.</summary>
        public static readonly float S4 = Pt(4), S6 = Pt(6), S8 = Pt(8), S10 = Pt(10),
                                     S12 = Pt(12), S16 = Pt(16), S24 = Pt(24);

        /// <summary>Screen side margin: 12pt.</summary>
        public static readonly float ScreenPad = Pt(12);
        /// <summary>Inside a panel: 14-16pt.</summary>
        public static readonly float PanelPad = Pt(15);

        /// <summary>Minimum touch target. Never shrink a tappable rect below this.</summary>
        public static readonly float Touch = Pt(44);

        /// <summary>Main button height. Guide: 56-72pt.</summary>
        public static readonly float ButtonTall = Pt(64);
        /// <summary>Secondary button height. Guide: 44-56pt.</summary>
        public static readonly float ButtonShort = Pt(50);
        public static readonly float ButtonIcon = Pt(44);

        /// <summary>Corner radius is about 30% of the height.</summary>
        public static float CornerFor(float height) => height * 0.30f;

        /// <summary>The solid band under a button that stands in for a bevel. Guide: 3-5pt.</summary>
        public static readonly float ButtonEdge = Pt(4);
        /// <summary>Pressed: the face drops this far and the band shrinks to 2pt.</summary>
        public static readonly float PressDrop = Pt(3);
        public static readonly float PressedEdge = Pt(2);
        /// <summary>Press animation length.</summary>
        public const float PressSeconds = 0.06f;

        /// <summary>Panel corner. Guide: 16-28pt.</summary>
        public static readonly float PanelCorner = Pt(22);
        /// <summary>Panel shadow band height.</summary>
        public static readonly float PanelShadowDrop = Pt(2);

        /// <summary>Notification dot, with a 2pt white ring.</summary>
        public static readonly float BadgeDot = Pt(18);
        public static readonly float BadgeRing = Pt(2);
        /// <summary>Info badge ("Lv.7", "무료") height.</summary>
        public static readonly float BadgeInfo = Pt(16);

        /// <summary>Bottom navigation height, before the home-indicator inset.</summary>
        public static readonly float BottomNav = Pt(64);
        /// <summary>Home indicator strip. No button may sit inside it.</summary>
        public static readonly float HomeIndicator = Pt(34);
        /// <summary>Top bar height.</summary>
        public static readonly float TopBar = Pt(44);
        /// <summary>Battle bottom tray height.</summary>
        public static readonly float Tray = Pt(142);

        /// <summary>
        /// The rect height a single line of <paramref name="fontSize"/> needs, including the
        /// ascender and descender. A rect shorter than this drops the line *entirely* under
        /// Ellipsis overflow - the label goes silently blank rather than showing clipped text,
        /// which is a nasty way to lose a caption. Measured against the project's own font:
        /// TMP reports preferredHeight 57 at size 38 and 45 at size 30, i.e. 1.5x.
        /// </summary>
        public static float Line(float fontSize) => Mathf.Ceil(fontSize * 1.5f);

        /// <summary>Toast dwell time.</summary>
        public const float ToastSeconds = 1.5f;

        // ------------------------------------------------------------------ numbers

        /// <summary>
        /// The guide's number rule: everything up to 9,999 in full, then K/M/B to three
        /// significant figures. The full value is still available on a long press.
        /// </summary>
        public static string Compact(double value)
        {
            double abs = System.Math.Abs(value);
            if (abs < 10_000d) return value.ToString("N0");
            (double div, string suffix) = abs >= 1_000_000_000d ? (1_000_000_000d, "B")
                                        : abs >= 1_000_000d ? (1_000_000d, "M")
                                        : (1_000d, "K");
            double scaled = value / div;
            // Three significant figures: 12.3K, 1.24M, 999K.
            string text = System.Math.Abs(scaled) >= 100d ? scaled.ToString("0")
                        : System.Math.Abs(scaled) >= 10d ? scaled.ToString("0.0")
                        : scaled.ToString("0.00");
            return text + suffix;
        }

        /// <summary>Power is quoted in watts: the simulation's unit times 100.</summary>
        public static string Watts(float power) => Compact(Mathf.Floor(power) * 100f) + "W";
    }
}
