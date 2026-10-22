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
        [SerializeField] TextMeshProUGUI nameLabel;
        [SerializeField] TextMeshProUGUI detailLabel;
        [SerializeField] TextMeshProUGUI starsLabel;
        [SerializeField] UiButton playButton;
        [SerializeField] GameObject lockMark;
        [SerializeField] Image bossMark;
        [SerializeField] LayoutElement layout;

        /// <summary>Row heights, in points: the current stage carries a button and is taller.</summary>
        /// <summary>
        /// 실측 .srow { padding:9px 0 } 위아래로, 이름(15px)과 요약(LabelSize) 두 줄이 들어갈
        /// 만큼. 높이를 손으로 적어 두면 글자 크기를 바꿀 때마다 줄이 잘린다 - 430 기준으로
        /// 옮기면서 정확히 그 일이 났다(필요 57px, 자리 54px).
        /// </summary>
        public static float RowPad => UiTheme.Py(9f);
        public static float NameLine => UiTheme.Line(UiTheme.Px(15f));
        public static float DetailLine => UiTheme.Line(UiTheme.LabelSize);
        public static float NormalHeight => RowPad * 2f + NameLine + DetailLine;
        /// <summary>The stage you are on sits taller, as the original's .srow.now does.</summary>
        public static float CurrentHeight => NormalHeight + UiTheme.Py(16f);

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
            if (nameLabel != null) nameLabel.text = stage.Name;
            if (bossMark != null) bossMark.enabled = stage.Boss;
            if (detailLabel != null)
                detailLabel.text = $"웨이브 {WaveCount(stage)} · 코인 {stage.IdleCoin:N0}";

            if (playButton != null) playButton.SetAction(() => onPlay?.Invoke());
            Sync();
        }

        /// <summary>Waves are not a column; they are how many the schedule actually runs.</summary>
        static int WaveCount(StageDef stage)
        {
            int max = -1;
            if (stage.Schedule != null)
                foreach (var e in stage.Schedule) if (e.Wave > max) max = e.Wave;
            return max + 1;
        }

        public void Sync()
        {
            if (_stage == null || _svc == null) return;

            bool open = _svc.Profile.ConditionMet(_stage.UnlockCond);
            bool hasDeck = _svc.Profile.Data.deck.Count > 0;
            int stars = _svc.Profile.Data.StarsOf(_stage.Id);
            bool current = Current && open;

            if (layout != null)
            {
                float h = current ? CurrentHeight : NormalHeight;
                layout.minHeight = h;
                layout.preferredHeight = h;
            }

            if (face != null) face.color = open ? UiTheme.Panel : UiTheme.Panel2;
            if (numberFace != null)
                numberFace.color = current ? UiTheme.Primary
                                 : open ? UiTheme.Panel2
                                 : UiTheme.Disabled;
            if (noLabel != null)
                noLabel.color = current ? UiTheme.Panel : open ? UiTheme.Ink : UiTheme.Ink3;
            if (nameLabel != null) nameLabel.color = open ? UiTheme.Ink : UiTheme.Ink3;
            if (detailLabel != null) detailLabel.color = open ? UiTheme.Ink2 : UiTheme.Ink3;

            // Stars only once the stage is beaten; an unplayed row shows nothing rather than
            // three empty outlines, which the mockup reserves for the cleared state.
            if (starsLabel != null)
            {
                starsLabel.gameObject.SetActive(open && !current && stars > 0);
                starsLabel.text = $"<color=#{ColorUtility.ToHtmlStringRGB(UiTheme.RaritySSR)}>"
                                + new string('★', stars) + "</color>"
                                + $"<color=#{ColorUtility.ToHtmlStringRGB(UiTheme.StarOff)}>"
                                + new string('★', 3 - stars) + "</color>";
            }

            if (playButton != null)
            {
                playButton.gameObject.SetActive(current);
                playButton.State = hasDeck ? UiButtonState.Normal : UiButtonState.Disabled;
                playButton.Text = hasDeck ? "도전" : "편성 없음";
            }
            if (lockMark != null) lockMark.SetActive(!open);
        }
    }
}
