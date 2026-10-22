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

        // Pt() is gone. It converted a 390pt iOS mockup and was applied to both axes, so
        // anything built with it came out 9% wide and 26% tall against the screens measured
        // off the original's own 430x932 page. docs/qa/DESIGN-AUDIT-10.18-c.md has the
        // measurements.

        /// <summary>The 10.16-n page every screen was measured against.</summary>
        public const float PageW = 430f, PageH = 932f;

        /// <summary>
        /// 원본 페이지 1px 이 캔버스에서 몇 px 인가. **두 축이 같은 값을 쓴다.**
        ///
        /// 전에는 가로 1080/430 = 2.512, 세로 1920/932 = 2.060 으로 따로 늘렸다. 두 축을
        /// 따로 늘리면 원본의 정사각형이 22% 퍼지고, 전투판처럼 **모양이 정해진 것**은
        /// 상자를 채울 수가 없다(7x10 판을 0.866 상자에 넣으면 양옆이 남는다).
        /// 세로에 맞춰 통일하면 원본과 같은 비율이 되고, 남는 가로는 좌우 여백이 된다 —
        /// 좁은 디자인을 넓은 화면에 올릴 때의 보통 결과다.
        /// </summary>
        public static float Scale => Reference.y / PageH;

        /// <summary>디자인 가로 전체가 캔버스에서 차지하는 폭. 화면 내용은 이 기둥에 담는다.</summary>
        public static float ColumnWidth => PageW * Scale;

        /// <summary>기둥이 가운데 오도록 한쪽에 두는 여백.</summary>
        public static float ColumnMargin => (Reference.x - ColumnWidth) * 0.5f;

        /// <summary>A horizontal measurement from that page, in canvas pixels.</summary>
        public static float Px(float pageX) => pageX * Scale;

        /// <summary>A vertical measurement from that page, in canvas pixels.</summary>
        public static float Py(float pageY) => pageY * Scale;

        /// <summary>
        /// 원본 페이지 1px 이 **화면**에서 몇 px 인가. <see cref="Px"/>/<see cref="Py"/> 는
        /// *캔버스* 단위다 — 캔버스 스케일러가 세로를 1920 에 맞추므로 창 비율이 바뀌면
        /// 캔버스와 화면의 px 는 서로 다른 값이 된다. 카메라 뷰포트와 띠 계산처럼
        /// **화면 픽셀**로 재야 하는 곳은 이걸 쓴다.
        /// </summary>
        public static float ScreenPerPage => Scale * CanvasToScreen;

        /// <summary>
        /// 캔버스 1px 이 화면에서 몇 px 인가. 스케일러가 Expand 라 두 축 중 작은 쪽에
        /// 맞춰지므로 가로·세로가 같은 값이다.
        /// </summary>
        public static float CanvasToScreen => UnityEngine.Mathf.Min(
            UnityEngine.Mathf.Max(1f, UnityEngine.Screen.width) / Reference.x,
            UnityEngine.Mathf.Max(1f, UnityEngine.Screen.height) / Reference.y);

        /// <summary>디자인 기둥이 화면에서 차지하는 폭(화면 px).</summary>
        public static float ColumnScreenWidth => PageW * ScreenPerPage;

        /// <summary>기둥 왼쪽 여백(화면 px).</summary>
        public static float ColumnScreenMargin =>
            UnityEngine.Mathf.Max(0f, (UnityEngine.Screen.width - ColumnScreenWidth) * 0.5f);

        /// <summary>페이지가 화면에서 차지하는 높이(화면 px).</summary>
        public static float PageScreenHeight => PageH * ScreenPerPage;

        /// <summary>페이지 위쪽 여백(화면 px). 화면이 페이지보다 길면 위아래로 남는다.</summary>
        public static float PageScreenTop =>
            UnityEngine.Mathf.Max(0f, (UnityEngine.Screen.height - PageScreenHeight) * 0.5f);

        /// <summary>
        /// 원본 페이지 좌표(위에서 잰 x,y,w,h)를 카메라 뷰포트로 옮긴다. 뷰포트는 화면
        /// 기준의 0~1 값이라 캔버스 단위를 그대로 쓰면 창 비율이 바뀔 때 어긋난다.
        /// </summary>
        public static UnityEngine.Rect PageViewport(float x, float y, float w, float h)
        {
            float s = ScreenPerPage;
            float sw = UnityEngine.Mathf.Max(1f, UnityEngine.Screen.width);
            float sh = UnityEngine.Mathf.Max(1f, UnityEngine.Screen.height);
            return new UnityEngine.Rect(
                (ColumnScreenMargin + x * s) / sw,
                (sh - PageScreenTop - (y + h) * s) / sh,
                w * s / sw,
                h * s / sh);
        }

        /// <summary>
        /// 정사각이어야 하는 치수. 이제 두 축의 배율이 같으므로 <see cref="Px"/> 와 같지만,
        /// 뜻을 드러내는 이름이라 남겨 둔다.
        /// </summary>
        public static float Sq(float pagePx) => Px(pagePx);

        // ------------------------------------------------------------------ colour

        static Color Hex(string rgb)
        {
            ColorUtility.TryParseHtmlString(rgb, out var c);
            return c;
        }

        // Values below are the 10.16-n build's own CSS custom properties, read out of the
        // running page with getComputedStyle rather than sampled from a screenshot, so they
        // are the source values and not something re-derived from a lossy capture.
        //
        // The palette is cool grey-blue, not the warm cream of the earlier redesign, and it
        // is NOT a dark theme: the page is light (#E6EAED) and only the top bar, the tab bar
        // and a few feature cards are dark plates. Those use the Dark* tokens below.

        /// <summary>Main action. The build puts <see cref="Ink"/> on this, never white.</summary>
        public static readonly Color Primary = Hex("#FF9A3C");          // --led
        /// <summary>Only still used where a pressed/!pressed pair is needed.</summary>
        public static readonly Color PrimaryEdge = Hex("#E8842A");      // --ssr, the darker orange
        /// <summary>Secondary action - the survivor mode entry.</summary>
        public static readonly Color Navy = Hex("#3D8BD9");             // --cool
        public static readonly Color NavyEdge = Hex("#2F6FAE");
        /// <summary>Claimable, placeable.</summary>
        public static readonly Color Success = Hex("#3FAE7A");          // --ok
        public static readonly Color SuccessEdge = Hex("#2E8559");
        /// <summary>Cannot place, overheated, notification dot.</summary>
        public static readonly Color Danger = Hex("#E0524A");           // --danger
        /// <summary>Power and coins. Text on it must be <see cref="PowerInk"/>, not white.</summary>
        public static readonly Color Power = Hex("#F2C14E");            // --heat
        public static readonly Color PowerInk = Hex("#7A5A00");
        /// <summary>Crystals, season pass.</summary>
        public static readonly Color Premium = Hex("#3D8BD9");

        /// <summary>Body and headings.</summary>
        public static readonly Color Ink = Hex("#1D2226");              // --ink
        /// <summary>Secondary text.</summary>
        public static readonly Color Ink2 = Hex("#5D6871");             // --muted
        public static readonly Color Ink3 = Hex("#5D6871");

        /// <summary>Cards and sheets.</summary>
        public static readonly Color Panel = Hex("#F7F9FA");            // --card
        /// <summary>Secondary buttons and input areas.</summary>
        public static readonly Color Panel2 = Hex("#E6EAED");
        public static readonly Color PanelLine = Hex("#C7D0D6");        // --line
        /// <summary>Page background behind everything.</summary>
        public static readonly Color PageBg = Hex("#E6EAED");           // --bg

        /// <summary>Top bar, bottom tab bar, and the feature cards drawn as dark plates.</summary>
        public static readonly Color DarkPanel = Hex("#1D2226");        // --panel
        /// <summary>A raised row inside a dark plate - the selected tab, for one.</summary>
        public static readonly Color DarkPanel2 = Hex("#2A3136");       // --panel-2
        /// <summary>Text on a dark plate.</summary>
        public static readonly Color DarkInk = Hex("#E8EDF0");          // --panel-ink
        public static readonly Color DarkInk2 = Hex("#8D99A2");         // --panel-muted

        /// <summary>
        /// The battle read-outs. 10.16-n prints them in a monospace LED face on the dark
        /// plate: amber for power and enemies left, red for cleanliness, which is the one
        /// number that means you are losing.
        /// </summary>
        public static readonly Color Led = Hex("#FF9A3C");              // --led
        public static readonly Color LedLife = Hex("#FF6B5E");          // .led.life
        /// <summary>Unfilled part of the power meter: the LED colour burnt down into the plate.</summary>
        public static readonly Color LedTrack = Hex("#5A3A1E");

        /// <summary>
        /// Widens a read-out column to fit its own number, like the build's `auto` grid
        /// column. Fixed widths looked fine at "10" and ellipsised the moment durability
        /// reached three digits - the HUD then read "..." where the number should be.
        /// </summary>
        /// <param name="caption">
        /// 같은 칸에 들어가는 **머리글**. 숫자만 보고 칸을 재면 머리글이 더 길 때 잘린다 —
        /// 협동의 '고리 먼지 / 40' 이 매 프레임 숫자('0') 폭으로 눌려 '고리 …' 가 됐다.
        /// </param>
        public static void FitToText(UnityEngine.UI.LayoutElement slot,
                                     TMPro.TextMeshProUGUI label, float minWidth,
                                     TMPro.TextMeshProUGUI caption = null)
        {
            if (slot == null || label == null) return;
            float want = label.GetPreferredValues(label.text).x;
            if (caption != null)
                want = UnityEngine.Mathf.Max(want, caption.GetPreferredValues(caption.text).x);
            slot.minWidth = slot.preferredWidth = UnityEngine.Mathf.Max(minWidth, want);
        }

        /// <summary>Inactive and locked; its text is <see cref="Ink3"/>.</summary>
        public static readonly Color Disabled = Hex("#E3E8EC");

        /// <summary>Panel drop shadow: a 2pt solid band below, Ink at 12%.</summary>
        public static Color PanelShadow => new(Ink.r, Ink.g, Ink.b, 0.12f);

        /// <summary>Rarity. Shown with the letter as well as the colour, for colour blindness.</summary>
        public static readonly Color RarityN = Hex("#7D8B96");          // --r
        public static readonly Color RarityR = Hex("#7D8B96");          // --r
        public static readonly Color RaritySR = Hex("#3D8BD9");         // --sr
        public static readonly Color RaritySSR = Hex("#E8842A");        // --ssr

        /// <summary>The tinted card ground behind a unit of that rarity.</summary>
        public static readonly Color RarityBgR = Hex("#E3E8EC");        // --r-bg
        public static readonly Color RarityBgSR = Hex("#DCE9F7");       // --sr-bg
        public static readonly Color RarityBgSSR = Hex("#FFE6CF");      // --ssr-bg

        /// <summary>Card ground by rarity index, 0 = N through 3 = SSR.</summary>
        // ------------------------------------------------------------------ 먼지 타워
        // 전부 10.19-e 의 `.tw-*` 규칙에서 읽은 값이다.
        // 자세한 자리는 production/qa/audit/original/TOWER-MEASUREMENTS.md.

        /// <summary>층 줄 바탕 — 기본 · 완료 · 현재 · 보스.</summary>
        public static readonly Color TowerFloorOpen = Hex("#6E7B84");
        public static readonly Color TowerFloorDone = Hex("#DCE7EE");
        public static readonly Color TowerFloorCurrent = Hex("#FFF3E6");
        public static readonly Color TowerFloorBoss = Hex("#4A2A2A");
        public static readonly Color TowerBossRing = Hex("#A8483E");
        public static readonly Color TowerBossNumber = Hex("#FF9A8E");
        public static readonly Color TowerFloorNumber = Hex("#C9D2D8");
        public static readonly Color TowerFloorReward = Hex("#D5DCE1");

        /// <summary>탑 바탕과 지붕(`.tw-bld` · `.tw-roof`).</summary>
        public static readonly Color TowerBuilding = Hex("#5B6770");
        public static readonly Color TowerBuildingRing = Hex("#4A555D");
        public static readonly Color TowerRoofInk = Hex("#AEB9C1");

        /// <summary>창 그림(`twWindow`): 보통 방 · 깬 방 · 보스 방과 창틀.</summary>
        public static readonly Color TowerWindowRoom = Hex("#B9A58A");
        public static readonly Color TowerWindowDone = Hex("#EAF4FA");
        public static readonly Color TowerWindowBoss = Hex("#5A3A36");
        public static readonly Color TowerWindowFrame = Hex("#3E4850");
        public static readonly Color TowerWindowFrameDone = Hex("#9FB4C2");

        /// <summary>보너스 칸(`.tw-ovb` · `.tw-goals .bonus`) — 아직 못 받음 · 받음.</summary>
        public static readonly Color TowerBonusBg = Hex("#FFFBEF");
        public static readonly Color TowerBonusLine = Hex("#E8A82E");
        public static readonly Color TowerBonusInk = Hex("#8A5A10");
        public static readonly Color TowerBonusOkBg = Hex("#E2F4EA");
        public static readonly Color TowerBonusOkLine = Hex("#8FD3AE");
        public static readonly Color TowerBonusOkInk = Hex("#2E6A4A");

        /// <summary>
        /// 층 규칙 종류별 색 — a 판 · b 목표 · c 편성. 바탕 · 테두리 · 글자 차례다.
        /// 규칙이 없는 기본 층이면 흰 바탕에 평소 선 색이다.
        /// </summary>
        public static (Color Fill, Color Line, Color Ink) TowerRuleKind(string kind) => kind switch
        {
            "a" => (Hex("#FFF4E8"), Hex("#F4B27A"), Hex("#8A3A10")),
            "b" => (Hex("#FFEDEB"), Hex("#EBA39C"), Hex("#A3342C")),
            "c" => (Hex("#EAF3FC"), Hex("#9CC3EA"), Hex("#1F4E80")),
            _ => (Color.white, PanelLine, Ink),
        };

        /// <summary>층 목록 꼬리표의 색(`.tw-fl .tag`). 보스는 따로 붉다.</summary>
        public static Color TowerTag(string kind, bool boss) => boss ? Hex("#A8483E") : kind switch
        {
            "a" => Hex("#E8842A"),
            "b" => Hex("#E0524A"),
            "c" => Hex("#3D8BD9"),
            _ => Hex("#8A949C"),
        };

        /// <summary>Gear grade colours, measured from the build's GRADE_COL.</summary>
        public static readonly Color Grade1 = Hex("#8A949C");
        public static readonly Color Grade2 = Hex("#3FAE7A");
        public static readonly Color Grade3 = Hex("#8B6BD9");
        public static readonly Color Grade4 = Hex("#E8A23A");

        /// <summary>The colour for a gear grade; grades run 1-4.</summary>
        public static Color GearGrade(int grade) => grade switch
        {
            4 => Grade4,
            3 => Grade3,
            2 => Grade2,
            _ => Grade1,
        };

        /// <summary>
        /// The palette's tier for a rarity. The table enum counts from zero (R = 0) while the
        /// swatch helpers below count from one, so passing a cast enum straight into them
        /// silently paints every appliance one grade too low. Go through here instead.
        /// </summary>
        public static int Tier(DustZero.Data.Rarity rarity) => (int)rarity + 1;

        public static Color RarityBg(int tier) => tier switch
        {
            3 => RarityBgSSR,
            2 => RarityBgSR,
            _ => RarityBgR,
        };

        /// <summary>An unearned star.</summary>
        public static readonly Color StarOff = Hex("#C7D0D6");

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

        /// <summary>
        /// Text that reads on a filled rarity swatch. 10.16-n itself draws rarity as coloured
        /// text on a transparent ground, so this only applies while the badge is still a
        /// filled chip; SSR keeps <see cref="Ink"/> because white on #E8842A is about 2.9:1,
        /// under the 4.5:1 floor, where Ink clears 7:1.
        /// </summary>
        public static Color RarityInk(int tier) => tier == 3 ? Ink : Color.white;

        // ------------------------------------------------------------------ button paint

        /// <summary>
        /// The ground a button draws. 10.16-n tints one flat rounded shape per kind instead
        /// of swapping a pre-painted face, so state is a colour change here rather than a
        /// different sprite.
        /// </summary>
        public static Color ButtonFill(UiButtonKind kind, UiButtonState state)
        {
            // The dark bar draws its own selected pill, so the item itself stays clear.
            if (kind == UiButtonKind.Nav) return new Color(0f, 0f, 0f, 0f);
            // Glass hands null to Bind, so this value is never applied - the caller's own
            // translucent plate stays put through Repaint.
            if (kind == UiButtonKind.Glass) return new Color(1f, 1f, 1f, 0f);
            if (kind == UiButtonKind.Power && state is not (UiButtonState.Disabled or UiButtonState.Locked))
                return Power;
            // Dark plates keep their ground in every state: 10.16-n signals "ready" with the
            // orange status line and a dot, never by turning the pill green.
            if (kind == UiButtonKind.Dark)
                return new Color(DarkPanel.r, DarkPanel.g, DarkPanel.b, 0.84f);
            if (kind == UiButtonKind.DarkSolid) return DarkPanel;
            if (kind == UiButtonKind.DarkRaised) return DarkPanel2;
            if (state is UiButtonState.Disabled or UiButtonState.Locked) return Disabled;
            if (state == UiButtonState.Claimable) return Success;
            if (state == UiButtonState.Selected && kind != UiButtonKind.Icon) return Primary;
            return kind switch
            {
                UiButtonKind.Primary => Primary,
                UiButtonKind.Navy => Navy,
                UiButtonKind.Success => Success,
                // Ghost and icon buttons sit on the page as a pale card with a hairline.
                UiButtonKind.Secondary => Panel,
                UiButtonKind.Icon => Panel,
                _ => new Color(0f, 0f, 0f, 0f),
            };
        }

        /// <summary>
        /// Text on that ground. The orange is light enough that the build puts <see cref="Ink"/>
        /// on it, not white - white on #FF9A3C fails contrast and the original does not use it.
        /// </summary>
        public static Color ButtonInk(UiButtonKind kind, UiButtonState state)
        {
            // Measured on the build's own tab bar: the chosen tab is #E8EDF0, the rest
            // #8D99A2. Neither is the accent - the orange belongs to the icon art.
            if (kind == UiButtonKind.Nav)
                return state == UiButtonState.Selected ? DarkInk : DarkInk2;
            if (kind is UiButtonKind.Dark or UiButtonKind.DarkSolid or UiButtonKind.DarkRaised)
                return state is UiButtonState.Disabled or UiButtonState.Locked ? DarkInk2 : DarkInk;
            if (state is UiButtonState.Disabled or UiButtonState.Locked) return Ink3;
            if (kind == UiButtonKind.Glass) return Ink;
            if (kind == UiButtonKind.Power) return PowerInk;
            if (kind == UiButtonKind.TextDanger) return Danger;
            if (state == UiButtonState.Claimable) return Color.white;
            if (state == UiButtonState.Selected && kind != UiButtonKind.Icon) return Ink;
            return kind switch
            {
                UiButtonKind.Primary => Ink,
                UiButtonKind.Navy => Color.white,
                UiButtonKind.Success => Color.white,
                _ => Ink,
            };
        }

        /// <summary>Ghost-style buttons are the only ones that carry a border.</summary>
        public static bool ButtonHasOutline(UiButtonKind kind)
            => kind is UiButtonKind.Secondary or UiButtonKind.Icon;

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

        // Every number below is a measurement of the ORIGINAL at its 430x932 page, passed
        // through Px (horizontal) or Py (vertical). The old Pt helper assumed a 390pt iOS
        // mockup and was applied to both axes, which made these four screens 9% wide and
        // 26% tall against their neighbours - see docs/qa/DESIGN-AUDIT-10.18-c.md.
        // "실측" marks a value read off the original's CSS; the rest are page px kept as-is.

        /// <summary>The original's gap scale, in page px: 4 6 8 10 12 16 24.</summary>
        public static readonly float S4 = Px(4), S6 = Px(6), S8 = Px(8), S10 = Px(10),
                                     S12 = Px(12), S16 = Px(16), S24 = Px(24);

        /// <summary>실측 .modal padding:16px - also the lobby's own side margin.</summary>
        public static readonly float ScreenPad = Px(16);
        /// <summary>실측 .sheet padding:12px.</summary>
        public static readonly float PanelPad = Px(12);

        /// <summary>Minimum touch target. No original equivalent; 44 page px.</summary>
        public static readonly float Touch = Px(44);

        /// <summary>실측 .primary height 46px.</summary>
        public static readonly float ButtonTall = Py(46);
        /// <summary>실측 .seg button height 33px.</summary>
        public static readonly float ButtonShort = Py(33);
        public static readonly float ButtonIcon = Px(44);

        // 10.16-n draws everything flat: measured box-shadow is "none" on buttons, cards,
        // the top bar and the tab bar, and the only border is a 1px line on ghost buttons.

        /// <summary>실측 .primary border-radius:10px.</summary>
        public static readonly float ButtonCorner = Px(10);
        /// <summary>Kept for call sites that ask for a radius by height.</summary>
        public static float CornerFor(float height) => ButtonCorner;

        /// <summary>No bevel band in 10.16-n. Zero keeps the builders' arithmetic intact.</summary>
        public static readonly float ButtonEdge = 0f;
        /// <summary>A press still moves, it just has no band to shrink into.</summary>
        public static readonly float PressDrop = Py(2);
        public static readonly float PressedEdge = 0f;
        /// <summary>Press animation length.</summary>
        public const float PressSeconds = 0.06f;

        /// <summary>실측 .sheet border-radius:12px.</summary>
        public static readonly float PanelCorner = Px(12);
        /// <summary>실측 .mbox border-radius:16px - the full-screen modal box.</summary>
        public static readonly float ModalCorner = Px(16);
        /// <summary>실측 .ov-card border-radius:14px - the centred overlay card.</summary>
        public static readonly float OverlayCorner = Px(14);
        /// <summary>Flat: no shadow band under a panel.</summary>
        public static readonly float PanelShadowDrop = 0f;
        /// <summary>실측 border:1.5px on .card and .secondary.</summary>
        public static readonly float HairLine = Mathf.Max(1f, Px(1.5f));

        /// <summary>Notification dot, with a 2px white ring.</summary>
        public static readonly float BadgeDot = Px(18);
        public static readonly float BadgeRing = Px(2);
        /// <summary>Info badge ("Lv.7", "무료") height.</summary>
        public static readonly float BadgeInfo = Py(16);

        /// <summary>실측 .tabbar height 78px on the 932px page.</summary>
        public static readonly float BottomNav = Py(78f);
        /// <summary>Home indicator strip. No button may sit inside it.</summary>
        public static readonly float HomeIndicator = Py(34);
        /// <summary>실측 the lobby header is 64px tall.</summary>
        public static readonly float TopBar = Py(64);
        /// <summary>Battle bottom tray height. No original measurement; page px.</summary>
        public static readonly float Tray = Py(142);

        /// <summary>
        /// The rect height a single line of <paramref name="fontSize"/> needs, including the
        /// ascender and descender. A rect shorter than this drops the line *entirely* under
        /// Ellipsis overflow - the label goes silently blank rather than showing clipped text,
        /// which is a nasty way to lose a caption. Measured against the project's own font:
        /// TMP reports preferredHeight 57 at size 38 and 45 at size 30, i.e. 1.5x.
        /// </summary>
        public static float Line(float fontSize) => Mathf.Ceil(fontSize * 1.5f);

        /// <summary>
        /// 글자가 들어갈 줄의 높이. 원하는 높이가 글자에 모자라면 글자 쪽을 따른다.
        ///
        /// 왜 필요한가: 글자 크기는 <see cref="Px"/>(가로 2.512), 줄 높이는
        /// <see cref="Py"/>(세로 2.060) 로 적는 자리가 많다. 두 배율이 1.22배 다르므로
        /// "같은 숫자" 로 적어도 줄이 글자보다 낮아진다. 그러면 TMP 는 줄을 자르지 않고
        /// **아예 안 그린다** — 빈 자리만 남고 오류도 없다. 가전 상세에서 라벨 7개가
        /// 이렇게 사라져 있었다(Lv. 가 49 인데 60 이 필요했다).
        /// </summary>
        public static float Row(float fontSize, float wanted) =>
            Mathf.Max(wanted, Line(fontSize));

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
