// Imports TextMesh Pro's essential resources.
//
// The editor normally offers this in a dialog the first time a TMP component is created, so
// it never runs in batch mode - and without TMP Settings in the project, creating a font
// asset throws. Importing it from the package keeps the setup reproducible on a clean clone.
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DustZero.Editor
{
    public static class TmpEssentials
    {
        const string SettingsAsset = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        public static bool IsInstalled => File.Exists(SettingsAsset);

        [MenuItem("DustZero/Import TMP Essentials")]
        public static void Import()
        {
            if (IsInstalled)
            {
                Debug.Log("[DustZero] TMP 필수 리소스가 이미 있습니다.");
                return;
            }

            var package = FindPackage();
            if (package == null)
            {
                Debug.LogError("[DustZero] TMP Essential Resources.unitypackage 를 찾지 못했습니다.");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"[DustZero] TMP 필수 리소스를 가져옵니다: {package}");
            AssetDatabase.ImportPackage(package, interactive: false);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        static string FindPackage()
        {
            // Resolve through the package cache, whose folder name carries a content hash.
            var root = Path.GetFullPath("Library/PackageCache");
            if (!Directory.Exists(root)) return null;

            return Directory
                .EnumerateDirectories(root, "com.unity.ugui@*")
                .Select(d => Path.Combine(d, "Package Resources", "TMP Essential Resources.unitypackage"))
                .FirstOrDefault(File.Exists);
        }
    }
}
