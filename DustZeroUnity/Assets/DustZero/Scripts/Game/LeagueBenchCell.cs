// 대기석 한 칸 — 원본 `.lg-bs`.
//
// 45.4x48, 모서리 10. 비면 점선 테두리, 말이 있으면 실선. 고르면 테두리가 파랗게 된다.
// 왼쪽 위에 ★, 오른쪽 아래에 '체험' 꼬리표.
using System;
using DustZero.Core.League;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class LeagueBenchCell : MonoBehaviour
    {
        [SerializeField] Image face;
        [SerializeField] Image outline;
        [SerializeField] Image portrait;
        [SerializeField] TextMeshProUGUI starLabel;
        [SerializeField] TextMeshProUGUI trialLabel;
        [SerializeField] Button button;

        /// <summary>비었을 때의 점선 테두리와 말이 있을 때의 실선.</summary>
        [SerializeField] Sprite dashedOutline;
        [SerializeField] Sprite solidOutline;

        public event Action Tapped;

        void Awake()
        {
            if (button != null) button.onClick.AddListener(() => Tapped?.Invoke());
        }

        public void Show(LeaguePiece piece, SpriteLibrary sprites, bool selected, bool trial)
        {
            bool has = piece != null;

            if (outline != null)
            {
                outline.sprite = has ? solidOutline : dashedOutline;
                outline.color = selected ? UiTheme.Navy : UiTheme.PanelLine;
            }
            if (portrait != null)
            {
                portrait.sprite = has && sprites != null ? sprites.Unit(piece.Type) : null;
                portrait.enabled = portrait.sprite != null;
            }
            if (starLabel != null)
            {
                starLabel.gameObject.SetActive(has);
                if (has) starLabel.text = new string('★', piece.Star);
            }
            if (trialLabel != null) trialLabel.gameObject.SetActive(has && trial);
        }
    }
}
