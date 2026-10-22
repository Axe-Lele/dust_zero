// One level of the season pass: free cell, level knob, premium cell.
using System;
using DustZero.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class PassRow : MonoBehaviour
    {
        [Header("무료")]
        [SerializeField] Image freeFace;
        [SerializeField] RewardChipRow freeChips;
        [SerializeField] TextMeshProUGUI freeStateLabel;
        [SerializeField] GameObject freeCheck;
        [SerializeField] Button freeButton;
        [SerializeField] CanvasGroup freeGroup;

        [Header("레벨")]
        [SerializeField] Image knobFace;
        [SerializeField] TextMeshProUGUI knobLabel;

        [Header("프리미엄")]
        [SerializeField] Image paidFace;
        [SerializeField] RewardChipRow paidChips;
        [SerializeField] TextMeshProUGUI paidStateLabel;
        [SerializeField] GameObject paidLock;
        [SerializeField] GameObject paidCheck;
        [SerializeField] Button paidButton;
        [SerializeField] CanvasGroup paidGroup;

        PassLevelRow _level;
        GameServices _svc;

        public void Bind(PassLevelRow level, GameServices services, Action<PassLevelRow, bool> onClaim)
        {
            _level = level;
            _svc = services;
            if (knobLabel != null) knobLabel.text = level.level.ToString();
            // 원본 `passCell` 은 `rgChips` 로 아이콘+숫자를 찍는다. 글자 나열이 아니다.
            if (freeChips != null) freeChips.Show(services.Profile.GroupChips(level.freeGroup));
            if (paidChips != null) paidChips.Show(services.Profile.GroupChips(level.paidGroup));

            if (freeButton != null)
            {
                freeButton.onClick.RemoveAllListeners();
                freeButton.onClick.AddListener(() => onClaim?.Invoke(_level, false));
            }
            if (paidButton != null)
            {
                paidButton.onClick.RemoveAllListeners();
                paidButton.onClick.AddListener(() => onClaim?.Invoke(_level, true));
            }
            Sync();
        }

        public void Sync()
        {
            if (_level == null || _svc == null) return;
            var profile = _svc.Profile;
            bool reached = _level.level <= profile.PassLevel;

            bool freeTaken = !profile.PassFreeClaimable(_level) && reached;
            bool freeReady = profile.PassFreeClaimable(_level);
            if (freeGroup != null) freeGroup.alpha = reached ? 1f : 0.45f;
            if (freeCheck != null) freeCheck.SetActive(freeTaken);
            if (freeButton != null) freeButton.interactable = freeReady;
            if (freeFace != null)
                freeFace.color = freeReady ? new Color(0.90f, 0.96f, 0.92f) : UiTheme.Panel;
            if (freeStateLabel != null)
                freeStateLabel.text = freeReady ? "받기" : freeTaken ? "받음" : "미달";

            bool paidReady = profile.PassPaidClaimable(_level);
            bool paidTaken = profile.PassPaid && reached && !paidReady;
            if (paidGroup != null) paidGroup.alpha = reached ? 1f : 0.45f;
            if (paidLock != null) paidLock.SetActive(!profile.PassPaid);
            if (paidCheck != null) paidCheck.SetActive(paidTaken);
            if (paidButton != null) paidButton.interactable = paidReady;
            if (paidFace != null)
                paidFace.color = paidReady ? new Color(0.90f, 0.96f, 0.92f) : new Color(0.95f, 0.93f, 0.98f);
            if (paidStateLabel != null)
                paidStateLabel.text = !profile.PassPaid ? "프리미엄 필요"
                                    : paidReady ? "받기" : paidTaken ? "받음" : "미달";

            if (knobFace != null) knobFace.color = reached ? UiTheme.Premium : UiTheme.Disabled;
            if (knobLabel != null) knobLabel.color = reached ? UiTheme.Panel : UiTheme.Ink3;
        }
    }
}
