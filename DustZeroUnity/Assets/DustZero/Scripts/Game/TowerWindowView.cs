// 층 줄 왼쪽의 작은 창 그림 — 원본 `twWindow` 가 캔버스로 그리던 132x40.
//
// 원본은 캔버스에 직접 그린다. 여기서는 같은 구성을 uGUI 로 쌓았다: 바탕색 · 바닥 띠 ·
// 먼지 그림 최대 셋 · 규칙에 따른 덧칠(열기 주황, 물바닥 웅덩이, 좁은 복도 양 옆 기둥) ·
// 테두리와 가운데 창살. 깬 층은 밝은 하늘색 바탕, 잠긴 층은 어둡게 덮는다.
//
// 원본과 다른 점: 원본은 깬 층에 **그때 쓴 가전**을 그리고 손님·거울·황금 먼지는
// 색 필터로 바꿔 그린다. 여기서는 먼지 그림과 덧칠까지만 같고 그 셋은 아직 없다.
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
        [SerializeField] Image[] foes = new Image[3];

        /// <summary>
        /// 창 하나를 그린다.
        /// </summary>
        /// <param name="cleared">이미 깬 층.</param>
        /// <param name="locked">아직 못 가는 층.</param>
        /// <param name="boss">보스 층.</param>
        /// <param name="rule">층 규칙 id — 덧칠을 고른다.</param>
        /// <param name="foeSprites">보여 줄 먼지 그림(최대 셋).</param>
        public void Show(bool cleared, bool locked, bool boss, string rule,
                         IReadOnlyList<Sprite> foeSprites)
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
            if (mullion != null)
                mullion.color = cleared ? UiTheme.TowerWindowFrameDone : UiTheme.TowerWindowFrame;
            if (veil != null) veil.gameObject.SetActive(locked);

            // 물바닥은 바닥 띠를 파랗게 바꿔 웅덩이를 대신한다.
            if (water && floorStrip != null)
                floorStrip.color = new Color(90f / 255f, 170f / 255f, 220f / 255f, 0.75f);

            for (int i = 0; i < foes.Length; i++)
            {
                var img = foes[i];
                if (img == null) continue;
                var sprite = foeSprites != null && i < foeSprites.Count ? foeSprites[i] : null;
                // 깬 층은 먼지를 치운 방이므로 아무도 서 있지 않다.
                img.gameObject.SetActive(!cleared && sprite != null);
                if (sprite != null) img.sprite = sprite;
            }
        }
    }
}
