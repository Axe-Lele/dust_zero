// 협동 파트너 고르기 — 원본 10.19-e 의 `coopEnter`.
//
// 두 장짜리 카드 줄(.co-pick) 에서 하나를 고르면 아래 설명(.co-desc) 이 그 파트너로
// 바뀌고, 맨 아래 버튼이 "{이름}와 출동" 이 된다. 고른 사람은 세이브에 남아
// 다음에 열 때 먼저 떠 있다.
//
//   .co-card  border 1.5 · radius 14 · padding 10/6 · 고른 것은 #F3F8FD + 파란 테두리
//   .cf 52×52 radius 12 · .ct b 17 · .ct small 11 (cool, bold)
//   .cm 11.5 muted · em #E8842A · .cb 11 muted
//   .co-desc  bg card · radius 12 · padding 9/11 · p 12.5 · small 11
using System.Collections.Generic;
using DustZero.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    /// <summary>The partner sheet shown before a 협동 run.</summary>
    public sealed class CoopPickScreen : MonoBehaviour
    {
        /// <summary>How many partners the sheet lays out. The table ships two.</summary>
        public const int Slots = 2;

        [SerializeField] UiButton backButton;
        [SerializeField] UiButton goButton;
        [SerializeField] TextMeshProUGUI goLabel;
        [SerializeField] TextMeshProUGUI noteLabel;

        [Header("카드")]
        [SerializeField] UiButton[] cardButtons = new UiButton[Slots];
        [SerializeField] Image[] cardFaces = new Image[Slots];
        [SerializeField] Image[] cardPlates = new Image[Slots];
        [SerializeField] TextMeshProUGUI[] cardNames = new TextMeshProUGUI[Slots];
        [SerializeField] TextMeshProUGUI[] cardStyles = new TextMeshProUGUI[Slots];
        [SerializeField] TextMeshProUGUI[] cardEarly = new TextMeshProUGUI[Slots];
        [SerializeField] TextMeshProUGUI[] cardLate = new TextMeshProUGUI[Slots];
        [SerializeField] TextMeshProUGUI[] cardBest = new TextMeshProUGUI[Slots];

        [Header("설명")]
        [SerializeField] TextMeshProUGUI descLabel;
        [SerializeField] RectTransform deckRow;
        [SerializeField] Image[] deckIcons = new Image[6];
        [SerializeField] TextMeshProUGUI deckNoteLabel;
        [SerializeField] TextMeshProUGUI rewardLabel;
        [SerializeField] TextMeshProUGUI poolLabel;

        [SerializeField] SpriteLibrary sprites;

        [Header("고른 카드 / 안 고른 카드")]
        [SerializeField] Color selectedPlate = new(0.953f, 0.973f, 0.992f);   // #F3F8FD
        [SerializeField] Color normalPlate = Color.white;

        GameServices _svc;
        string _selected;

        public event System.Action Closed;
        /// <summary>Raised with the chosen partner id when the player heads out.</summary>
        public event System.Action<string> Chosen;

        void Awake()
        {
            if (backButton != null) backButton.SetAction(Close);
            if (goButton != null) goButton.SetAction(Go);

            for (int i = 0; i < cardButtons.Length; i++)
            {
                int slot = i;
                cardButtons[i]?.SetAction(() => Select(slot));
            }
            // 여기서 SetActive(false) 를 부르면 안 된다. Awake 는 **켜질 때** 돌기 때문에
            // Open() 이 켠 그 프레임에 스스로 다시 꺼져 버린다. 처음 꺼 두는 일은
            // ProjectBuilder 가 지을 때 한 번만 한다.
        }

        public void Open(GameServices services)
        {
            _svc = services;
            gameObject.SetActive(true);
            _selected = services.Profile.Coop.last;
            if (services.Db.CoopPartner(_selected) == null && services.Db.CoopPartners.Count > 0)
                _selected = services.Db.CoopPartners[0].id;
            Refresh();
        }

        void Close()
        {
            gameObject.SetActive(false);
            Closed?.Invoke();
        }

        void Select(int slot)
        {
            var partners = _svc.Db.CoopPartners;
            if (slot < 0 || slot >= partners.Count) return;
            _selected = partners[slot].id;
            Refresh();
        }

        void Go()
        {
            if (string.IsNullOrEmpty(_selected)) return;
            gameObject.SetActive(false);
            Chosen?.Invoke(_selected);
        }

        /// <summary>원본 `coStars` — 채운 동그라미와 빈 동그라미로 3단계를 보인다.</summary>
        static string Dots(int n)
        {
            n = Mathf.Clamp(n, 0, 3);
            return new string('●', n) + new string('○', 3 - n);
        }

        void Refresh()
        {
            if (_svc == null) return;
            var db = _svc.Db;
            var cfg = db.Config;
            var profile = _svc.Profile;
            var partners = db.CoopPartners;

            if (noteLabel != null)
                noteLabel.text =
                    "먼지가 거실 러그를 빙글빙글 돌아요. 빈칸을 골라 [가전 소환]하면 그 칸에 편성 가전이 " +
                    "무작위로 나와요(고리 칸 = 근접, 가구 칸 = 원거리). 같은 ★ 두 대를 합쳐 키우고, " +
                    $"옮기기·바꾸기로 다듬어요. 위쪽은 파트너, 아래쪽은 내 자리. 고리 위 먼지가 {cfg.coopLoopMax}마리가 되면 끝.";

            for (int i = 0; i < Slots; i++)
            {
                bool used = i < partners.Count;
                if (cardButtons[i] != null) cardButtons[i].gameObject.SetActive(used);
                if (!used) continue;

                var p = partners[i];
                bool on = p.id == _selected;

                if (cardPlates[i] != null) cardPlates[i].color = on ? selectedPlate : normalPlate;
                if (cardButtons[i] != null)
                    cardButtons[i].State = on ? UiButtonState.Selected : UiButtonState.Normal;

                if (cardFaces[i] != null)
                {
                    cardFaces[i].sprite = sprites != null ? sprites.Unit(p.unit) : null;
                    cardFaces[i].enabled = cardFaces[i].sprite != null;
                }
                if (cardNames[i] != null) cardNames[i].text = db.Text(p.nameKey);
                if (cardStyles[i] != null) cardStyles[i].text = db.Text(p.styleKey);
                if (cardEarly[i] != null) cardEarly[i].text = $"초반 {Dots(p.early)}";
                if (cardLate[i] != null) cardLate[i].text = $"후반 {Dots(p.late)}";
                if (cardBest[i] != null) cardBest[i].text = $"최고 {profile.CoopBest(p.id)}웨이브";
            }

            var chosen = db.CoopPartner(_selected);
            if (chosen == null) return;

            if (descLabel != null) descLabel.text = db.Text(chosen.descKey);

            // 파트너 덱 미리보기. 표의 순서를 그대로 보인다.
            var deck = new List<string>();
            foreach (var raw in (chosen.deck ?? string.Empty).Split(','))
            {
                var id = raw.Trim();
                if (id.Length > 0 && db.Units.ContainsKey(id)) deck.Add(id);
            }
            for (int i = 0; i < deckIcons.Length; i++)
            {
                bool used = i < deck.Count;
                if (deckIcons[i] == null) continue;
                deckIcons[i].transform.parent.gameObject.SetActive(used);
                if (!used) continue;
                deckIcons[i].sprite = sprites != null ? sprites.Unit(deck[i]) : null;
                deckIcons[i].enabled = deckIcons[i].sprite != null;
            }

            var (level, star) = profile.CoopPartnerStrength();
            if (deckNoteLabel != null)
                deckNoteLabel.text =
                    $"파트너 가전 Lv.{level}{(star > 0 ? $" ★{star}" : string.Empty)} · " +
                    "내 보유 가전 상위 6대 평균 · 내 소환은 지금 편성에서 나와요";

            if (rewardLabel != null)
                rewardLabel.text = $"버틴 웨이브마다 청소 일지 +{cfg.poolCoopWave} · 코인 {UiTheme.Compact(cfg.coopCoinPerWave)}";

            var (pts, max) = profile.SurvivorPoolToday();
            if (poolLabel != null) poolLabel.text = $"청소 일지 {pts}/{max}";

            if (goLabel != null) goLabel.text = $"{db.Text(chosen.nameKey)}와 출동";
        }
    }
}
