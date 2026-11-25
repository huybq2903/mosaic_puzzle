using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Editor
{
    public class MediationVersionSync : AssetPostprocessor
    {
        private const string PACKAGE_SUFFIX = "/Modules/Core/ThirdParty/Mediation/package.json";
        private const string SCRIPT_SUFFIX = "/Modules/Core/ThirdParty/Mediation/Scripts/Runtime/MediationVersion.cs";

        private static void OnPostprocessAllAssets(
            string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Any(path => Norm(path).EndsWith(PACKAGE_SUFFIX))) Sync();
        }

        [InitializeOnLoadMethod]
        private static void OnLoad()
        {
            Sync();
        }

        private static void Sync()
        {
            var packagePath = Find(PACKAGE_SUFFIX);
            var scriptPath = Find(SCRIPT_SUFFIX);
            if (packagePath == null || scriptPath == null) return;

            var version = Regex.Match(File.ReadAllText(packagePath), "\"version\"\\s*:\\s*\"([^\"]+)\"");
            if (!version.Success) return;

            var script = File.ReadAllText(scriptPath);
            var current = Regex.Match(script, "VALUE\\s*=\\s*\"([^\"]+)\"");
            if (!current.Success || current.Groups[1].Value == version.Groups[1].Value) return;

            File.WriteAllText(scriptPath, script.Replace(
                $"VALUE = \"{current.Groups[1].Value}\"", $"VALUE = \"{version.Groups[1].Value}\""));
            AssetDatabase.ImportAsset(ToAssetPath(scriptPath));
            Debug.Log("[Mediation] MediationVersion đồng bộ theo package.json: " +
                      $"{current.Groups[1].Value} → {version.Groups[1].Value}");
        }

        private static string Find(string suffix)
        {
            var name = Path.GetFileName(suffix);
            return Directory.GetFiles(Application.dataPath, name, SearchOption.AllDirectories)
                .Select(Norm)
                .FirstOrDefault(path => path.EndsWith(suffix));
        }

        private static string Norm(string path)
        {
            return path.Replace('\\', '/');
        }

        private static string ToAssetPath(string absolute)
        {
            var assets = Norm(Application.dataPath);
            return "Assets" + absolute.Substring(assets.Length);
        }
    }
}
