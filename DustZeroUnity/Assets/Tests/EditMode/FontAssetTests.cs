// 글꼴 자산이 **의도한 ttf 로** 만들어졌는지 본다.
//
// 왜 필요한가: ProjectBuilder 는 자산이 이미 있으면 그냥 돌려줬다. 그래서 셋 다
// DoHyeon 으로 만들던 시절의 자산이 남아, Body/Number 를 IBM Plex 로 바꾼 뒤에도
// **게임 전체가 계속 DoHyeon 으로 그려졌다.** 빌드는 매번 성공했다. 화면도 멀쩡해
// 보였다 — 글자가 원본보다 20% 좁다는 것만 빼면. 치수를 재기 전까지 아무도 몰랐다.
using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEditor;

namespace DustZero.Tests
{
    public class FontAssetTests
    {
        const string Folder = "Assets/DustZero/Fonts";

        // 원본 10.16-n 의 배정. index.html 의 computed style 에서 읽었다.
        //   본문·탭    IBM Plex Sans KR   (body 기본값, .tab)
        //   제목·버튼  Do Hyeon           (span.brand, #btnAttend > b)
        //   숫자·LED   Share Tech Mono    (--f-led; .led, .srow .no, .parts b …)
        static readonly Dictionary<string, string> Expected = new()
        {
            { "DustZero-Title SDF", "DoHyeon-Regular" },
            { "DustZero-Body SDF", "IBMPlexSansKR-Regular" },
            { "DustZero-Number SDF", "ShareTechMono-Regular" },
        };

        [Test]
        public void EveryFontAsset_WrapsItsIntendedTtf()
        {
            foreach (var (assetName, ttfName) in Expected)
            {
                var path = $"{Folder}/{assetName}.asset";
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                Assert.IsNotNull(font, $"{path} 가 없습니다. ProjectBuilder 를 돌리세요.");
                Assert.IsNotNull(font.sourceFontFile,
                    $"{assetName} 에 원본 ttf 기록이 없습니다 — 어떤 글꼴인지 확인할 수 없습니다.");
                Assert.AreEqual(ttfName, font.sourceFontFile.name,
                    $"{assetName} 가 엉뚱한 ttf 로 만들어져 있습니다.");
            }
        }

        [Test]
        public void Number_FallsBackToTheBodyFaceForHangul()
        {
            // Share Tech Mono 는 라틴 전용이다. 숫자 칸에도 한글과 ★ 가 섞여 들어오므로
            // 본문 글꼴이 뒤에 달려 있어야 한다. 빠지면 두부가 나오는데, 빈 라벨 검사는
            // **빈 문자열만** 보기 때문에 잡히지 않는다.
            var number = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"{Folder}/DustZero-Number SDF.asset");
            var body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"{Folder}/DustZero-Body SDF.asset");
            Assert.IsNotNull(number); Assert.IsNotNull(body);
            Assert.IsNotNull(number.fallbackFontAssetTable, "숫자 글꼴에 떨어질 글꼴이 없습니다.");
            CollectionAssert.Contains(number.fallbackFontAssetTable, body,
                "숫자 글꼴이 본문 글꼴로 떨어지지 않습니다 — 한글이 두부로 나옵니다.");
        }

        [Test]
        public void BodyAndTitle_UseDifferentTypefaces()
        {
            // 원본은 본문과 제목에 서로 다른 글꼴을 쓴다. 하나로 합치면 이 테스트가 잡는다.
            var body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"{Folder}/DustZero-Body SDF.asset");
            var title = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"{Folder}/DustZero-Title SDF.asset");
            Assert.IsNotNull(body); Assert.IsNotNull(title);
            Assert.AreNotEqual(title.sourceFontFile?.name, body.sourceFontFile?.name,
                "본문과 제목이 같은 글꼴입니다. 원본은 본문 IBM Plex Sans KR, 제목 Do Hyeon 입니다.");
        }
    }
}
