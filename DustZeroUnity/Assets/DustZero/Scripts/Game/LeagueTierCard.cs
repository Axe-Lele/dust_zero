// 티어 고르기 시트의 단계 한 장 — 원본 `.lg-tier`.
//
// 이름과 상태(도전/완주함/잠김), 난이도와 최고 기록, 보상 두 줄. 잠긴 단계는 누를 수 없다.
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class LeagueTierCard : MonoBehaviour
    {
        [SerializeField] Image face;
        [SerializeField] Image outline;
        [SerializeField] TextMeshProUGUI nameLabel;
        [SerializeField] TextMeshProUGUI stateLabel;
        [SerializeField] TextMeshProUGUI statLabel;
        [SerializeField] TextMeshProUGUI rewardLabel;
        [SerializeField] CanvasGroup group;
        [SerializeField] Button button;

        public event Action Tapped;

        void Awake()
        {
            if (button != null) button.onClick.AddListener(() => Tapped?.Invoke());
        }

        public void Show(string name, string state, string stats, string reward,
                         bool locked, bool selected)
        {
            if (nameLabel != null) nameLabel.text = name;
            if (stateLabel != null) stateLabel.text = state;
            if (statLabel != null) statLabel.text = stats;
            if (rewardLabel != null) rewardLabel.text = reward;
            if (group != null) group.alpha = locked ? 0.45f : 1f;
            if (button != null) button.interactable = !locked;
            if (outline != null)
                outline.color = selected ? UiTheme.Led : UiTheme.PanelLine;
            if (face != null)
                face.color = selected ? UiTheme.Panel2 : UiTheme.Panel;
        }
    }
}
