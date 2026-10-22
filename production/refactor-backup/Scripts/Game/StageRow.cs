// One stage entry in the lobby list.
using System;
using DustZero.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class StageRow : MonoBehaviour
    {
        [SerializeField] TextMeshProUGUI noLabel;
        [SerializeField] TextMeshProUGUI nameLabel;
        [SerializeField] TextMeshProUGUI starsLabel;
        [SerializeField] TextMeshProUGUI detailLabel;
        [SerializeField] Button playButton;
        [SerializeField] TextMeshProUGUI playLabel;
        [SerializeField] Image bossMark;

        StageDef _stage;
        GameServices _svc;

        public void Bind(StageDef stage, GameServices services, Action onPlay)
        {
            _stage = stage;
            _svc = services;

            if (noLabel != null) noLabel.text = stage.No;
            if (nameLabel != null) nameLabel.text = stage.Name;
            if (bossMark != null) bossMark.enabled = stage.Boss;
            if (detailLabel != null)
                detailLabel.text = $"적 HP ×{stage.HpMul:0.00}  ·  시급 코인 {stage.IdleCoin:N0}";

            if (playButton != null)
            {
                playButton.onClick.RemoveAllListeners();
                playButton.onClick.AddListener(() => onPlay?.Invoke());
            }
            Sync();
        }

        public void Sync()
        {
            if (_stage == null || _svc == null) return;

            bool open = _svc.Profile.ConditionMet(_stage.UnlockCond);
            bool cleared = _svc.Profile.Data.best >= _stage.Order;
            bool hasDeck = _svc.Profile.Data.deck.Count > 0;

            int stars = _svc.Profile.Data.StarsOf(_stage.Id);
            if (starsLabel != null)
                starsLabel.text = new string('★', stars) + new string('☆', 3 - stars);

            if (playButton != null) playButton.interactable = open && hasDeck;
            if (playLabel != null)
                playLabel.text = !open ? "잠김" : !hasDeck ? "편성 없음" : cleared ? "재도전" : "도전";

            var cg = GetComponent<CanvasGroup>();
            if (cg != null) cg.alpha = open ? 1f : 0.5f;
        }
    }
}
