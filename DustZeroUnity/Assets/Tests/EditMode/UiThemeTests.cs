// The redesign's number rule, which the guide states precisely enough to test.
//
// "9,999까지는 전체, 1만 이상은 K/M/B로 유효숫자 3자리." Currency appears on nearly every
// screen, so getting this wrong is visible everywhere at once.
using DustZero.Game;
using NUnit.Framework;
using UnityEngine;

namespace DustZero.Tests
{
    public class UiThemeTests
    {
        [TestCase(0, "0")]
        [TestCase(7, "7")]
        [TestCase(999, "999")]
        [TestCase(9999, "9,999")]
        public void UnderTenThousandIsWrittenOutInFull(double value, string expected)
            => Assert.AreEqual(expected, UiTheme.Compact(value));

        [TestCase(10000, "10.0K")]
        [TestCase(12345, "12.3K")]
        [TestCase(999000, "999K")]
        [TestCase(1240000, "1.24M")]
        [TestCase(1240512, "1.24M")]
        [TestCase(2500000000, "2.50B")]
        public void TenThousandAndAboveUsesThreeSignificantFigures(double value, string expected)
            => Assert.AreEqual(expected, UiTheme.Compact(value));

        [Test]
        public void NegativesKeepTheirSign()
        {
            Assert.AreEqual("-12.3K", UiTheme.Compact(-12345));
            Assert.AreEqual("-500", UiTheme.Compact(-500));
        }

        [Test]
        public void PowerIsQuotedInWattsAtTheOriginalsScale()
        {
            // The simulation counts power in units of 100W, as the original's fmt(cost*100) does,
            // and the compacting rule only kicks in at 10,000 - so a full battle's power reads
            // out in full rather than as "1.20KW".
            Assert.AreEqual("600W", UiTheme.Watts(6f));
            Assert.AreEqual("1,200W", UiTheme.Watts(12f));
            Assert.AreEqual("12.0KW", UiTheme.Watts(120f));
        }

        [Test]
        public void EveryMetricIsOnTheOriginalsOwn430x932Page()
        {
            // Pt() is gone: it converted a 390pt iOS mockup and was applied to both axes, so
            // whatever used it came out 9% wide and 26% tall against the screens measured off
            // the original. One basis now - Px for horizontal, Py for vertical.
            Assert.AreEqual(430f, UiTheme.PageW, 0.01f);
            Assert.AreEqual(932f, UiTheme.PageH, 0.01f);
            Assert.AreEqual(1080f, UiTheme.Reference.x, 0.01f);
            Assert.AreEqual(1920f, UiTheme.Reference.y, 0.01f);

            // 실측값이 그대로 들어왔는지. 숫자를 바꾸면 원본과 어긋난다.
            Assert.AreEqual(UiTheme.Px(10f), UiTheme.ButtonCorner, 0.01f, ".primary 반지름 10px");
            Assert.AreEqual(UiTheme.Px(12f), UiTheme.PanelCorner, 0.01f, ".sheet 반지름 12px");
            Assert.AreEqual(UiTheme.Px(16f), UiTheme.ModalCorner, 0.01f, ".mbox 반지름 16px");
            Assert.AreEqual(UiTheme.Py(46f), UiTheme.ButtonTall, 0.01f, ".primary 높이 46px");
            Assert.AreEqual(UiTheme.Py(33f), UiTheme.ButtonShort, 0.01f, ".seg button 높이 33px");
            Assert.AreEqual(UiTheme.Py(78f), UiTheme.BottomNav, 0.01f, ".tabbar 높이 78px");
            Assert.AreEqual(UiTheme.Px(16f), UiTheme.ScreenPad, 0.01f, ".modal 여백 16px");

            // 간격 사다리는 원본의 gap 값 그대로다.
            Assert.AreEqual(UiTheme.Px(8f), UiTheme.S8, 0.01f);
            Assert.AreEqual(UiTheme.Px(16f), UiTheme.S16, 0.01f);
        }

        [Test]
        public void EveryRarityHasAReadableLabelAndSwatch()
        {
            for (int tier = 0; tier <= 3; tier++)
            {
                Assert.IsNotEmpty(UiTheme.RarityName(tier));
                // SSR's yellow needs dark text; the rest carry white.
                var ink = UiTheme.RarityInk(tier);
                Assert.AreEqual(tier == 3, ink == UiTheme.Ink,
                    $"{UiTheme.RarityName(tier)} 배지 글자색 규칙이 가이드와 다릅니다.");
            }
        }

        /// <summary>
        /// The rarity enum counts from zero and the palette tiers from one, so a cast enum
        /// lands one grade low: SSR would be painted SR blue and SR painted R grey, with no
        /// error anywhere. These are the swatches the 10.16-n build actually uses.
        /// </summary>
        [Test]
        public void RarityTierMatchesTheBuildsSwatches()
        {
            Assert.AreEqual(1, UiTheme.Tier(DustZero.Data.Rarity.R));
            Assert.AreEqual(2, UiTheme.Tier(DustZero.Data.Rarity.SR));
            Assert.AreEqual(3, UiTheme.Tier(DustZero.Data.Rarity.SSR));

            var cases = new (DustZero.Data.Rarity rarity, string ink, string bg)[]
            {
                (DustZero.Data.Rarity.R,   "7D8B96", "E3E8EC"),
                (DustZero.Data.Rarity.SR,  "3D8BD9", "DCE9F7"),
                (DustZero.Data.Rarity.SSR, "E8842A", "FFE6CF"),
            };
            foreach (var (rarity, ink, bg) in cases)
            {
                int tier = UiTheme.Tier(rarity);
                Assert.AreEqual(ink, ColorUtility.ToHtmlStringRGB(UiTheme.Rarity(tier)),
                    $"{rarity} 등급 글자색이 원본과 다릅니다.");
                Assert.AreEqual(bg, ColorUtility.ToHtmlStringRGB(UiTheme.RarityBg(tier)),
                    $"{rarity} 등급 배경색이 원본과 다릅니다.");
            }
        }
    }
}
