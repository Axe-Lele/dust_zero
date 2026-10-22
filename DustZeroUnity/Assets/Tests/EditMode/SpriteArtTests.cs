using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DustZero.Tests
{
    /// <summary>
    /// The exported art must fit inside its own frame.
    ///
    /// Every one of the 19 enemy sprites once shipped clipped to the full 256x256 canvas - a
    /// dust mote drawn so large that only two eyes and a grey field survived - and it drew
    /// that way in battle for weeks without anything noticing. Nothing was checking, because
    /// a sprite that is wrong is still a sprite that loads. This checks.
    /// </summary>
    public sealed class SpriteArtTests
    {
        const string Dir = "Assets/DustZero/Art/Sprites";

        static IEnumerable<string> Pngs(string prefix)
        {
            foreach (var p in Directory.GetFiles(Dir, prefix + "*.png"))
                yield return p.Replace('\\', '/');
        }

        /// <summary>Opaque bounds of a texture, or null when it is fully transparent.</summary>
        static RectInt? OpaqueBounds(Texture2D tex)
        {
            var px = tex.GetPixels32();
            int w = tex.width, h = tex.height;
            int x0 = w, y0 = h, x1 = -1, y1 = -1;
            for (int i = 0; i < px.Length; i++)
            {
                if (px[i].a <= 8) continue;
                int x = i % w, y = i / w;
                if (x < x0) x0 = x;
                if (x > x1) x1 = x;
                if (y < y0) y0 = y;
                if (y > y1) y1 = y;
            }
            if (x1 < 0) return null;
            return new RectInt(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        }

        static Texture2D Readable(string path)
        {
            // The imported texture is usually not CPU-readable, so decode the file itself.
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(path));
            return tex;
        }

        [Test]
        public void EveryEnemySpriteFitsInsideItsFrame()
        {
            var clipped = new List<string>();
            int seen = 0;
            foreach (var path in Pngs("enemy_"))
            {
                seen++;
                var tex = Readable(path);
                var b = OpaqueBounds(tex);
                Assert.IsNotNull(b, $"{Path.GetFileName(path)} 가 완전히 투명합니다.");
                var r = b.Value;
                // Touching any edge means the drawing ran past the capture box.
                if (r.xMin <= 0 || r.yMin <= 0 || r.xMax >= tex.width || r.yMax >= tex.height)
                    clipped.Add($"{Path.GetFileName(path)} {r} / {tex.width}x{tex.height}");
                Object.DestroyImmediate(tex);
            }
            Assert.Greater(seen, 15, "적 스프라이트를 찾지 못했습니다. 경로가 바뀌었는지 확인하세요.");
            CollectionAssert.IsEmpty(clipped,
                "캔버스 밖으로 넘친 적 스프라이트입니다. GameData/tools/make_enemy_exporter.py 로 다시 내보내세요:\n"
                + string.Join("\n", clipped));
        }

        [Test]
        public void EveryEnemySpriteActuallyDrawsSomething()
        {
            // The mirror of the test above: a frame can also be wrong by being nearly empty,
            // which is what a failed draw call leaves behind.
            var tiny = new List<string>();
            foreach (var path in Pngs("enemy_"))
            {
                var tex = Readable(path);
                var b = OpaqueBounds(tex);
                if (b.HasValue && b.Value.width < tex.width * 0.10f)
                    tiny.Add($"{Path.GetFileName(path)} 가로 {b.Value.width}px");
                Object.DestroyImmediate(tex);
            }
            CollectionAssert.IsEmpty(tiny, "거의 빈 적 스프라이트입니다:\n" + string.Join("\n", tiny));
        }

        [Test]
        public void EveryEnemyInTheTableHasASprite()
        {
            // 11 looks and 19 ids: the old exporter hard-coded 8 rows and silently skipped the
            // rest, so mite / hair / scale / moth / rust / emperor had no art at all.
            var db = TestData.Db;
            var missing = new List<string>();
            foreach (var id in db.Enemies.Keys)
                if (!File.Exists($"{Dir}/enemy_{id}.png")) missing.Add(id);
            CollectionAssert.IsEmpty(missing, "스프라이트가 없는 적:\n" + string.Join(", ", missing));
        }
    }
}
