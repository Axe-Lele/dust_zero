// 협동 전투 화면의 두 줄 — 파트너 말풍선(coBar)과 조작 줄(rdBar).
//
// 수치는 원본 10.19-e 의 CSS 를 그대로 옮겼다. 430×932 에서 잰 값이므로
// 가로·글자는 UiTheme.Px, 세로는 Py 로 환산한다.
//
//   .co-bar   padding 5/9 · radius 12 · border 1.5 · #EAF3FC / #9CC3EA
//   .co-face  40×40 radius 10
//   .co-top b 15 (#1F4E80) · small 11 (#3D6A99) · .co-st 12
//   .co-say   12 · line-height 1.35
//   .rd-row   1fr 92px · gap 6
//   .rd-sum   52 높이 · radius 13 · b 21 · span 16
//   .rd-re    52 높이 · radius 13 · b 17 · span 10.5
//   .rd-merge 11.5 가운데
//   .rd-ups   6열 · gap 5 · .rd-up radius 10 · .lv 10.5 · .c 10.5
using System.Collections.Generic;
using DustZero.Core.Coop;
using DustZero.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    /// <summary>The two coop strips under the battle HUD.</summary>
    public sealed class CoopBars : MonoBehaviour
    {
        [SerializeField] Image partnerBar;
        [SerializeField] Image partnerFace;
        [SerializeField] TMP_Text partnerName;
        [SerializeField] TMP_Text partnerStyle;
        [SerializeField] TMP_Text partnerStats;
        [SerializeField] TMP_Text partnerSay;

        [SerializeField] UiButton summonButton;
        [SerializeField] TMP_Text summonCost;
        [SerializeField] UiButton rerollButton;
        [SerializeField] TMP_Text rerollCost;
        [SerializeField] TMP_Text mergeLine;
        [SerializeField] RectTransform upgradeRow;

        [SerializeField] CoopUpgradeCell upgradeCellPrefab;
        [SerializeField] SpriteLibrary sprites;

        GameServices _svc;
        CoopSim _coop;
        readonly List<CoopUpgradeCell> _cells = new();

        float _sayLeft;

        /// <summary>Raised when the player summons, so the screen can flash the new unit.</summary>
        public event System.Action<DustZero.Core.Defense.BattleUnit> Summoned;

        /// <summary>Fills the strips in for one run. Called once per 협동 판.</summary>
        public void Build(GameServices services, CoopSim coop)
        {
            _svc = services;
            _coop = coop;

            if (partnerName != null) partnerName.text = services.Db.Text(coop.Partner.nameKey);
            if (partnerStyle != null) partnerStyle.text = services.Db.Text(coop.Partner.styleKey);
            if (partnerFace != null)
            {
                var sprite = sprites != null ? sprites.Unit(coop.Partner.unit) : null;
                partnerFace.sprite = sprite;
                partnerFace.enabled = sprite != null;
            }

            BuildUpgradeCells();
            _sayLeft = 0f;
            Refresh();
        }

        /// <summary>
        /// 강화 버튼은 내 편성 한 칸에 하나씩. 그 **종류 전체**를 올리는 것이라
        /// 판에 놓인 가전 수와 무관하게 편성 수만큼만 있다.
        /// </summary>
        void BuildUpgradeCells()
        {
            if (upgradeRow == null) return;
            var deck = _coop.Battle.Deck;

            if (upgradeCellPrefab == null) return;
            for (int i = _cells.Count; i < deck.Count; i++)
                _cells.Add(Instantiate(upgradeCellPrefab, upgradeRow));

            for (int i = 0; i < _cells.Count; i++)
            {
                bool used = i < deck.Count;
                _cells[i].gameObject.SetActive(used);
                if (!used) continue;
                string type = deck[i];
                _cells[i].Bind(_svc, type, () =>
                {
                    if (_coop.Upgrade(CoopSide.Player, type)) Refresh();
                    else
                    {
                        var cost = _coop.UpgradeCost(CoopSide.Player, type);
                        Hint(cost == null
                                 ? "최고 단계까지 강화했어요."
                                 : $"전력이 모자라요. {_svc.Db.Unit(type).Name} 강화는 {UiTheme.Compact(cost.Value * 100f)}W예요.");
                    }
                });
            }
        }

        /// <summary>The partner says a line. It holds for 4.5s, as in the original.</summary>
        public void Say(string line)
        {
            if (string.IsNullOrEmpty(line) || partnerSay == null) return;
            partnerSay.text = line;
            _sayLeft = 4.5f;
            SetTalking(true);
        }

        [Header("말할 때 / 평소")]
        [SerializeField] Color barIdle = new(0.918f, 0.953f, 0.988f);   // #EAF3FC
        [SerializeField] Color barTalking = new(0.839f, 0.910f, 0.976f);
        [SerializeField] Color sayIdle = new(0.44f, 0.47f, 0.50f);
        [SerializeField] Color sayTalking = new(0.114f, 0.133f, 0.149f);  // --ink

        void SetTalking(bool talking)
        {
            // .co-bar.talk 은 테두리를 진하게 하고 글자를 또렷하게 한다.
            if (partnerBar != null) partnerBar.color = talking ? barTalking : barIdle;
            if (partnerSay != null) partnerSay.color = talking ? sayTalking : sayIdle;
        }

        void Update()
        {
            if (_coop == null) return;
            if (_sayLeft > 0f)
            {
                _sayLeft -= Time.unscaledDeltaTime;
                if (_sayLeft <= 0f) SetTalking(false);
            }
            Refresh();
        }

        /// <summary>Repaints both strips from the simulation.</summary>
        public void Refresh()
        {
            if (_coop == null || _svc == null) return;
            var cfg = _svc.Db.Config;
            var P = CoopSide.Player;

            int theirs = 0;
            foreach (var u in _coop.Battle.Units) if (!u.Dead && _coop.SideOf(u) != P) theirs++;
            if (partnerStats != null)
                partnerStats.text = $"{UiTheme.Compact(Mathf.Floor(_coop.PartnerPower) * 100f)}W · 가전 {theirs}";

            if (partnerSay != null && _sayLeft <= 0f)
                partnerSay.text = _coop.Wave > 0
                    ? $"{_coop.Wave}웨이브 · 위쪽은 파트너 자리예요"
                    : "준비 중 · 위쪽은 파트너 자리예요";

            // 소환
            float cost = _coop.SummonCost(P);
            bool room = _coop.Spot.HasValue
                ? _coop.SummonableTypes(P, _coop.TileAt(_coop.Spot.Value.Col, _coop.Spot.Value.Row)).Count > 0
                : _coop.HasRoom(P);
            if (summonCost != null) summonCost.text = room ? $"{UiTheme.Compact(cost * 100f)}W" : "자리 없음";
            Dim(summonButton, _coop.PowerOf(P) >= cost && room);

            // 바꾸기
            var sel = _coop.Selected;
            if (rerollCost != null)
                rerollCost.text = sel != null ? $"{UiTheme.Compact(_coop.RerollCost(sel) * 100f)}W" : "가전 고르기";
            // `.rd-sum.off` / `.rd-re.off` 는 opacity .5 — 눌리기는 한다. 눌러야 이유를 말해 준다.
            Dim(rerollButton, sel != null && _coop.PowerOf(P) >= _coop.RerollCost(sel)
                              && _coop.RerollTypes(sel).Count > 0);
            if (rerollButton != null) rerollButton.SetKind(sel != null ? UiButtonKind.Primary : UiButtonKind.Secondary);

            if (mergeLine != null)
            {
                int pairs = _coop.Pairs(P).Count;
                mergeLine.text =
                    $"고리 위 {_coop.RingCount(P)}/{cfg.coopLineMax} · " +
                    (_coop.Spot.HasValue ? "고른 칸에 소환" : "빈칸을 누르면 소환 자리") + " · " +
                    (pairs > 0 ? $"합칠 짝 {pairs}" : "같은 ★ 두 대를 누르면 합쳐요");
            }

            foreach (var cell in _cells)
                if (cell.gameObject.activeSelf) cell.Refresh(_coop);
        }

        // ------------------------------------------------------------------ 버튼

        /// <summary>[가전 소환] — 고른 칸이 있으면 거기, 없으면 고리에서 가까운 빈칸.</summary>
        public void OnSummon()
        {
            if (_coop == null || _coop.Finished) return;
            var P = CoopSide.Player;
            var spot = _coop.Spot;
            var tile = spot.HasValue ? _coop.TileAt(spot.Value.Col, spot.Value.Row) : null;

            var u = _coop.Summon(P, spot);
            if (u != null)
            {
                Summoned?.Invoke(u);
                Hint(null);
                Refresh();
                return;
            }

            var cfg = _svc.Db.Config;
            if (_coop.PowerOf(P) < _coop.SummonCost(P))
                Hint($"전력이 모자라요. 소환은 {UiTheme.Compact(_coop.SummonCost(P) * 100f)}W예요.");
            else if (tile == TileKind.Ground && !_coop.RingHasRoom(P))
                Hint($"고리 위에는 {cfg.coopLineMax}대까지만 세울 수 있어요. 합치거나 원거리 칸을 골라요.");
            else if (tile.HasValue && _coop.SummonableTypes(P, tile).Count == 0)
                Hint(tile == TileKind.Ground ? "편성에 근접 가전이 없어요." : "편성에 원거리 가전이 없어요.");
            else
                Hint("자리가 없어요. 같은 ★ 두 대를 합쳐 자리를 비워요.");
        }

        /// <summary>[바꾸기] — ★ 은 그대로, 같은 자리에 설 수 있는 다른 가전으로.</summary>
        public void OnReroll()
        {
            if (_coop == null || _coop.Finished) return;
            var sel = _coop.Selected;
            if (sel == null)
            {
                Hint("바꿀 내 가전을 먼저 눌러요. ★은 그대로, 같은 자리에 설 수 있는 다른 가전으로 바뀌어요.");
                return;
            }
            if (_coop.RerollTypes(sel).Count == 0)
            {
                Hint($"편성에 {(_svc.Db.Unit(sel.Type).Tile == TileKind.Ground ? "근접" : "원거리")} 가전이 " +
                     "이것 하나라 바꿀 수 없어요.");
                return;
            }

            var next = _coop.Reroll(sel);
            if (next != null) Hint($"{next.Def.Name} ★{_coop.StarOf(next)}(으)로 바꿨어요.");
            else Hint($"전력이 모자라요. 바꾸기는 {UiTheme.Compact(_coop.RerollCost(sel) * 100f)}W예요.");
            Refresh();
        }

        static void Dim(UiButton button, bool on)
        {
            if (button == null) return;
            var group = button.GetComponent<CanvasGroup>();
            if (group != null) group.alpha = on ? 1f : 0.5f;
        }

        /// <summary>Raised with a line for the battle screen's hint row.</summary>
        public event System.Action<string> HintRequested;

        void Hint(string line) => HintRequested?.Invoke(line);
    }
}
