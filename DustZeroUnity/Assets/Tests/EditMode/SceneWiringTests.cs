// Guards the generated scene: every serialized reference must be filled in.
//
// An empty reference built by ProjectBuilder would otherwise only surface as a
// NullReferenceException partway through a battle, so it is checked up front.
using System.Linq;
using System.Reflection;
using DustZero.Game;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DustZero.Tests
{
    public class SceneWiringTests
    {
        const string ScenePath = "Assets/DustZero/Scenes/Main.unity";

        /// <summary>References that are legitimately optional.</summary>
        static readonly string[] Optional = { "m_Script" };

        [Test]
        public void SceneExistsAndIsInBuildSettings()
        {
            Assert.IsTrue(System.IO.File.Exists(ScenePath),
                "Main.unity 가 없습니다. DustZero > Rebuild Project Assets 를 실행하세요.");
            Assert.IsTrue(EditorBuildSettings.scenes.Any(s => s.path == ScenePath && s.enabled),
                "Main.unity 가 빌드 설정에 등록되어 있지 않습니다.");
        }

        [Test]
        public void EveryComponentReferenceIsWired()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            try
            {
                var roots = scene.GetRootGameObjects();
                Assert.Greater(roots.Length, 0, "씬이 비어 있습니다.");

                int checkedRefs = 0;
                foreach (var root in roots)
                    foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (mb == null) continue;
                        // Only the port's own components; package scripts are not our concern.
                        var ns = mb.GetType().Namespace ?? string.Empty;
                        if (!ns.StartsWith("DustZero")) continue;

                        var type = mb.GetType();
                        var so = new SerializedObject(mb);
                        var prop = so.GetIterator();
                        while (prop.NextVisible(true))
                        {
                            if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;
                            if (Optional.Contains(prop.name)) continue;
                            // A field the component declares optional is skipped by declaration,
                            // not by name, so the guard stays strict for everything else.
                            if (IsOptional(type, prop.name)) continue;
                            checkedRefs++;
                            Assert.IsNotNull(prop.objectReferenceValue,
                                $"{mb.GetType().Name}.{prop.name} 참조가 비어 있습니다 ({mb.gameObject.name}).");
                        }
                    }

                Assert.Greater(checkedRefs, 20, $"검사한 참조가 {checkedRefs}개뿐입니다. 씬 구성이 비정상입니다.");
                Debug.Log($"[wiring] 참조 {checkedRefs}개 확인 완료");
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        /// <summary>True when the field carries <see cref="OptionalReferenceAttribute"/>.</summary>
        static bool IsOptional(System.Type type, string fieldName)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (var t = type; t != null; t = t.BaseType)
            {
                var field = t.GetField(fieldName, Flags);
                if (field != null) return field.GetCustomAttribute<OptionalReferenceAttribute>() != null;
            }
            return false;
        }

        [Test]
        public void GeneratedAssetsExist()
        {
            foreach (var path in new[]
            {
                "Assets/DustZero/Prefabs/Entity.prefab",
                "Assets/DustZero/Prefabs/DeckCard.prefab",
                "Assets/DustZero/Prefabs/StageRow.prefab",
                "Assets/DustZero/Prefabs/FxParticle.prefab",
                "Assets/DustZero/Prefabs/FxLabel.prefab",
                "Assets/DustZero/Prefabs/TileHighlight.prefab",
                "Assets/DustZero/Settings/SpriteLibrary.asset",
                "Assets/DustZero/Fonts/DustZero-Body SDF.asset",
                "Assets/DustZero/Fonts/DustZero-Title SDF.asset",
            })
                Assert.IsTrue(System.IO.File.Exists(path), $"{path} 가 없습니다.");
        }

        [Test]
        public void SpriteLibraryCoversEveryUnitAndEnemy()
        {
            var lib = AssetDatabase.LoadAssetAtPath<SpriteLibrary>("Assets/DustZero/Settings/SpriteLibrary.asset");
            Assert.IsNotNull(lib, "SpriteLibrary 에셋이 없습니다.");

            var db = TestData.Db;
            foreach (var id in db.UnitOrder)
                Assert.IsNotNull(lib.Unit(id), $"가전 '{id}' 의 스프라이트가 없습니다.");

            foreach (var e in db.Enemies.Values)
                Assert.IsNotNull(lib.Enemy(e.Id, e.Look, e.Boss),
                    $"적 '{e.Id}' (look={e.Look}, boss={e.Boss}) 의 스프라이트가 없습니다.");
        }
    }
}
