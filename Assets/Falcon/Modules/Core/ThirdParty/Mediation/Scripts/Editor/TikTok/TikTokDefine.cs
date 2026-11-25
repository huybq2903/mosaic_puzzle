using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Editor
{
    [InitializeOnLoad]
    public static class TikTokDefine
    {
        public const string WATCHED_FOLDER = "Assets/Falcon/Modules/Core/ThirdParty/Mediation/Scripts/Runtime/TikTokBusinessSDK";
        public const string DEFINE_SYMBOL = "TIKTOK_ENABLE";
        private const string _SDK_ENTRY_FILE = "TikTokBusinessSDK.cs";
        private const char _SEPARATOR = ';';

        static TikTokDefine()
        {
            EditorApplication.delayCall += SyncDefineWithFolder;
        }

        public static bool IsSdkPresent()
        {
            return Directory.Exists(WATCHED_FOLDER) &&
                   Directory.EnumerateFiles(WATCHED_FOLDER, _SDK_ENTRY_FILE, SearchOption.AllDirectories).Any();
        }

        [MenuItem("Falcon/Modules/ThirdParty/TikTok Define Sync")]
        public static void SyncDefineWithFolder()
        {
            var sdkPresent = IsSdkPresent();
            var changedTargets = TargetsToSync().Where(target => ApplyDefine(target, sdkPresent)).ToList();
            if (changedTargets.Count == 0) return;

            Debug.Log($"[TikTokDefine] {(sdkPresent ? "thêm" : "gỡ")} {DEFINE_SYMBOL} " +
                      $"({string.Join(", ", changedTargets.Select(t => t.TargetName))}) — " +
                      $"{WATCHED_FOLDER} {(sdkPresent ? "có" : "không có")} TikTok SDK.");
        }

        private static IEnumerable<NamedBuildTarget> TargetsToSync()
        {
            var targets = new List<NamedBuildTarget> { NamedBuildTarget.Android, NamedBuildTarget.iOS };
            var selectedGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            if (selectedGroup != BuildTargetGroup.Unknown)
                targets.Add(NamedBuildTarget.FromBuildTargetGroup(selectedGroup));
            return targets.Distinct();
        }

        private static bool ApplyDefine(NamedBuildTarget target, bool enable)
        {
            var symbols = PlayerSettings.GetScriptingDefineSymbols(target)
                .Split(_SEPARATOR)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();

            if (symbols.Contains(DEFINE_SYMBOL) == enable) return false;

            if (enable) symbols.Add(DEFINE_SYMBOL);
            else symbols.RemoveAll(s => s == DEFINE_SYMBOL);

            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(_SEPARATOR.ToString(), symbols));
            return true;
        }
    }

    public class TikTokFolderWatcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            var touchesSdk = importedAssets.Concat(deletedAssets).Concat(movedAssets).Concat(movedFromAssetPaths)
                .Any(IsInWatchedFolder);
            if (touchesSdk) TikTokDefine.SyncDefineWithFolder();
        }

        private static bool IsInWatchedFolder(string assetPath)
        {
            return assetPath.StartsWith(TikTokDefine.WATCHED_FOLDER, StringComparison.OrdinalIgnoreCase);
        }
    }
}
