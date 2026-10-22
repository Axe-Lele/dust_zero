// One-click builds, so the output always lands somewhere findable.
//
// The project's own `build/` folder at the repo root is scratch space for compile checks,
// which is why a plain File > Build there produced no executable anyone could find. These
// menu items write to `Builds/<platform>/` instead and log the full path.
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DustZero.Editor
{
    public static class GameBuilder
    {
        const string ScenePath = "Assets/DustZero/Scenes/Main.unity";

        /// <summary>Repo root, one level above the Unity project folder.</summary>
        static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));

        static string OutputDir(string platform)
        {
            var dir = Path.Combine(RepoRoot, "Builds", platform);
            Directory.CreateDirectory(dir);
            return dir;
        }

        [MenuItem("DustZero/Build/Windows (windowed 540x960)")]
        public static void BuildWindows()
        {
            // A portrait game on a landscape monitor has to run windowed, or the player
            // stretches across the whole screen and the board is unreadable.
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 540;
            PlayerSettings.defaultScreenHeight = 960;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.defaultIsNativeResolution = false;

            var dir = OutputDir("Windows");
            Run(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(dir, "DustZero.exe"),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            });
        }

        [MenuItem("DustZero/Build/Android (portrait APK)")]
        public static void BuildAndroid()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            {
                Debug.LogError("[DustZero] Android 빌드 지원이 설치되어 있지 않습니다. " +
                               "Unity Hub > Installs > 6000.4.5f1 > Add modules 에서 " +
                               "Android Build Support 를 추가하세요.");
                return;
            }

            // The phone decides the resolution; the game only insists on portrait.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;

            var dir = OutputDir("Android");
            Run(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(dir, "DustZero.apk"),
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            });
        }

        static void Run(BuildPlayerOptions options)
        {
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[DustZero] 빌드 실패: {summary.result} · 오류 {summary.totalErrors}개");
                return;
            }

            Debug.Log($"[DustZero] 빌드 완료 — {options.locationPathName}\n" +
                      $"크기 {summary.totalSize / (1024f * 1024f):0.0} MB · " +
                      $"시간 {summary.totalTime.TotalSeconds:0}초");
            EditorUtility.RevealInFinder(options.locationPathName);
        }
    }
}
