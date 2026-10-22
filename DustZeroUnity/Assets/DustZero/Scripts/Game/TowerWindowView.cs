// 층 줄 왼쪽의 작은 창 그림 — 원본 `twWindow` 가 캔버스로 그리던 132x40.
//
// 원본은 캔버스에 직접 그린다. 여기서는 같은 구성을 uGUI 로 쌓았다:
//   · 바탕 — 깬 층은 밝은 하늘색, 보스 층은 어두운 적갈색, 그 밖은 흙빛
//   · 바닥 띠와 먼지 티끌 여섯
//   · 규칙에 따른 덧칠 — 열기 주황, 물바닥 웅덩이 둘, 좁은 복도 양 옆 기둥
//   · 먼지 그림 최대 셋(보스 층은 보스 하나)과 오른쪽 자리의 손님
//     (지키기 청정기 · 손님 가전 · 황금 먼지 · 거울 가전)
//   · 깬 층은 그때 데리고 간 가전과 체크 표시, 잠긴 층은 어두운 덮개
//   · 창틀과 가운데 창살
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class TowerWindowView : MonoBehaviour
    {
        [SerializeField] Image face;
        [SerializeField] Image floorStrip;
        [SerializeField] Image tint;
        [SerializeField] Image leftWall;
        [SerializeField] Image rightWall;
        [SerializeField] Image veil;
        [SerializeField] Image mullion;
        [SerializeField] Image check;
        [SerializeField] Image[] foes = new Image[3];
        [SerializeField] Image[] who = new Image[3];
        [SerializeField] Image[] pools = new Image[2];
        [SerializeField] Image[] specks = new Image[6];
        [SerializeField] Image extra;

        /// <summary>깬 층에 세우는 가전의 가로 자리 — 원본은 하나면 가운데, 여럿이면 벌린다.</summary>
        static readonly float[] OneWho = { 0.5f };
        static readonly float[] TwoWho = { 0.22f, 0.78f };
        static readonly float[] ThreeWho = { 0.22f, 0.5f, 0.78f };

        /// <summary>
        /// 창 하나를 그린다.
        /// </summary>
        /// <param name="cleared">이미 깬 층 — 먼지 대신 그때 쓴 가전이 선다.</param>
        /// <param name="locked">아직 못 가는 층.</param>
        /// <param name="boss">보스 층.</param>
        /// <param name="rule">층 규칙 id — 덧칠과 오른쪽 손님을 고른다.</param>
        /// <param name="foeSprites">보여 줄 먼지 그림(최대 셋).</param>
        /// <param name="whoSprites">깬 층에 세울 가전 그림(최대 셋).</param>
        /// <param name="guest">오른쪽 자리에 설 것 — 청정기·손님 가전·황금 먼지·거울.</param>
        /// <param name="guestTint">그 손님의 칠(황금은 금빛, 거울은 회색).</param>
        public void Show(bool cleared, bool locked, bool boss, string rule,
                         IReadOnlyList<Sprite> foeSprites, IReadOnlyList<Sprite> whoSprites,
                         Sprite guest, Color guestTint)
        {
            if (face != null)
                face.color = cleared ? UiTheme.TowerWindowDone
                           : boss ? UiTheme.TowerWindowBoss
                           : UiTheme.TowerWindowRoom;
            if (floorStrip != null)
                floorStrip.color = cleared ? new Color(1f, 1f, 1f, 0.6f)
                                           : new Color(90f / 255f, 70f / 255f, 50f / 255f, 0.25f);

            bool heat = !cleared && rule == "heat";
            bool water = !cleared && rule == "water";
            bool narrow = !cleared && rule == "narrow";

            if (tint != null)
            {
                bool on = heat || (boss && !cleared);
                tint.gameObject.SetActive(on);
                if (on)
                    tint.color = heat ? new Color(1f, 140f / 255f, 60f / 255f, 0.25f)
                                      : new Color(224f / 255f, 82f / 255f, 74f / 255f, 0.22f);
            }
            if (leftWall != null) leftWall.gameObject.SetActive(narrow);
            if (rightWall != null) rightWall.gameObject.SetActive(narrow);
            foreach (var pool in pools)
                if (pool != null) pool.gameObject.SetActive(water);

            if (mullion != null)
                mullion.color = cleared ? UiTheme.TowerWindowFrameDone : UiTheme.TowerWindowFrame;
            if (veil != null) veil.gameObject.SetActive(locked);
            if (check != null) check.gameObject.SetActive(cleared);

            // 먼지는 아직 안 깬 층에만, 깬 층에는 그때 데리고 간 가전이 선다.
            for (int i = 0; i < foes.Length; i++)
            {
                var img = foes[i];
                if (img == null) continue;
                var sprite = !cleared && foeSprites != null && i < foeSprites.Count
                    ? foeSprites[i] : null;
                img.gameObject.SetActive(sprite != null);
                if (sprite == null) continue;
                img.sprite = sprite;

                // 보스 층은 원본도 보스 하나를 가운데에 크게(44) 그린다.
                var rt = (RectTransform)img.transform;
                float cx = boss ? 0.5f : 0.16f + i * 0.25f;
                float half = boss ? 0.17f : 0.1f;
                rt.anchorMin = new Vector2(cx - half, boss ? 0.1f : 0.18f);
                rt.anchorMax = new Vector2(cx + half, boss ? 0.95f : 0.86f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }

            int whoCount = cleared && whoSprites != null ? Mathf.Min(whoSprites.Count, who.Length) : 0;
            var spots = whoCount >= 3 ? ThreeWho : whoCount == 2 ? TwoWho : OneWho;
            for (int i = 0; i < who.Length; i++)
            {
                var img = who[i];
                if (img == null) continue;
                bool on = i < whoCount;
                img.gameObject.SetActive(on);
                if (!on) continue;
                img.sprite = whoSprites[i];
                var rt = (RectTransform)img.transform;
                float cx = spots[Mathf.Min(i, spots.Length - 1)];
                rt.anchorMin = new Vector2(cx - 0.12f, 0.14f);
                rt.anchorMax = new Vector2(cx + 0.12f, 0.9f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }

            if (extra != null)
            {
                bool on = !cleared && guest != null;
                extra.gameObject.SetActive(on);
                if (on) { extra.sprite = guest; extra.color = guestTint; }
            }

            // 티끌은 방이 비어 보이지 않게 깔아 두는 것이라 깬 층에는 없다.
            foreach (var speck in specks)
                if (speck != null) speck.gameObject.SetActive(!cleared);
        }
    }
}
