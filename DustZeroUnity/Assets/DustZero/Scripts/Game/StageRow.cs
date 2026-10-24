// One stage entry in the stage tab.
//
// Redesign 2a gives a row three looks: cleared rows show their stars, the one stage the
// player is actually on grows to carry a 도전 button, and a locked row is flat with a padlock
// and no affordance at all. The row is told which of the three it is; it does not guess.
using System;
using DustZero.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class StageRow : MonoBehaviour
    {
        [SerializeField] Image face;
        [SerializeField] Image numberFace;
        [SerializeField] TextMeshProUGUI noLabel;
        /// <summary>원본 `.info` 의 세 조각. 한 줄에 간격 10 으로 흐른다.</summary>
        [SerializeField] TextMeshProUGUI starsLabel;
        [SerializeField] TextMeshProUGUI recLabel;
        [SerializeField] TextMeshProUGUI idleLabel;
        /// <summary>줄 맨 위의 1px 구분선 (`border-top`).</summary>
        [SerializeField] Image divider;
        [SerializeField] UiButton playButton;
        [SerializeField] GameObject lockMark;
        [SerializeField] Image bossMark;
        [SerializeField] LayoutElement layout;

        /// <summary>
        /// 원본 `.srow` 는 **한 줄**이다 — `1-1` · `★★★ 권장 Lv.N 시급 코인 X` · 버튼.
        /// 높이는 가장 큰 자식인 버튼(35)이 정한다: `9 + 35 + 9` 에 위 구분선 1 을 더해 54.
        /// 숫자를 직접 적지 않고 버튼 높이에서 끌어오므로, 버튼이 바뀌면 줄도 같이 간다.
        /// </summary>
        public static float RowPad => UiTheme.Py(9f);
        public static float ButtonHeight => UiTheme.Py(35f);
        public static float DividerHeight => UiTheme.Py(1f);
        public static float NormalHeight => RowPad * 2f + ButtonHeight + DividerHeight;
        /// <summary>원본은 지금 도전할 줄도 같은 높이다(실측 52 vs 54, 1px 안쪽).</summary>
        public static float CurrentHeight => NormalHeight;

        /// <summary>원본 `.srow` 글자·선 색. 전부 CSS 변수에서 읽은 값이다.</summary>
        static readonly Color NoDone = Hex(0x3F, 0xAE, 0x7A);     // --ok
        static readonly Color NoCurrent = Hex(0xE8, 0x84, 0x2A);  // --ssr
        static readonly Color StarOn = Hex(0xFF, 0x9A, 0x3C);     // --led
        static readonly Color StarOff = Hex(0xC7, 0xD0, 0xD6);    // --line

        static Color Hex(byte r, byte g, byte b) => new Color32(r, g, b, 0xFF);

        StageDef _stage;
        GameServices _svc;

        /// <summary>The stage this row shows, so the tab can filter by chapter.</summary>
        public StageDef Stage => _stage;

        /// <summary>Set by the tab: this is the one stage the player should play next.</summary>
        public bool Current { get; set; }

        public void Bind(StageDef stage, GameServices services, Action onPlay)
        {
            _stage = stage;
            _svc = services;

            if (noLabel != null) noLabel.text = stage.No;
            if (bossMark != null) bossMark.enabled = stage.Boss;
            // 원본 `.info` 의 두 조각. 숫자만 진하게(`.info b { color: --ink; 600 }`).
            if (recLabel != null)
                recLabel.text = $"권장 <b>Lv.{stage.RefLevel}</b>";
            if (idleLabel != null)
                idleLabel.text = $"시급 코인 <b>{stage.IdleCoin:N0}</b>";

            if (playButton != null) playButton.SetAction(() => onPlay?.Invoke());
            Sync();
        }

        public void Sync()
        {
            if (_stage == null || _svc == null) return;

            bool open = _svc.Profile.ConditionMet(_stage.UnlockCond);
            bool hasDeck = _svc.Profile.Data.deck.Count > 0;
            int stars = _svc.Profile.Data.StarsOf(_stage.Id);
            bool current = Current && open;

            bool done = open && _svc.Profile.Data.best >= _stage.Order;

            if (layout != null)
            {
                layout.minHeight = NormalHeight;
                layout.preferredHeight = NormalHeight;
            }

            // 원본 줄에는 바탕이 없다 — 구분선 하나로만 나뉜다.
            if (face != null) face.enabled = false;
            if (numberFace != null) numberFace.enabled = false;
            if (divider != null) divider.color = StarOff;

            // `.srow.done .no { --ok }` · `.srow.cur .no { --ssr }`, 그 밖에는 본문색.
            if (noLabel != null)
                noLabel.color = done ? NoDone : current ? NoCurrent : open ? UiTheme.Ink : UiTheme.Ink3;

            // `.rstars` 는 **항상 세 개**를 그리고 얻은 것만 --led 로 칠한다.
            if (starsLabel != null)
            {
                starsLabel.gameObject.SetActive(open);
                starsLabel.text = $"<color=#{ColorUtility.ToHtmlStringRGB(StarOn)}>"
                                + new string('★', stars) + "</color>"
                                + $"<color=#{ColorUtility.ToHtmlStringRGB(StarOff)}>"
                                + new string('★', 3 - stars) + "</color>";
            }
            if (recLabel != null) recLabel.color = open ? UiTheme.Ink2 : UiTheme.Ink3;
            if (idleLabel != null) idleLabel.color = open ? UiTheme.Ink2 : UiTheme.Ink3;

            // 원본은 깬 줄에도 `재도전` 이 남는다. 잠긴 줄만 버튼이 죽는다.
            if (playButton != null)
            {
                playButton.gameObject.SetActive(open);
                playButton.State = hasDeck ? UiButtonState.Normal : UiButtonState.Disabled;
                playButton.Text = !hasDeck ? "편성 없음" : done ? "재도전" : "도전";
            }
            if (lockMark != null) lockMark.SetActive(!open);
        }
    }
}
