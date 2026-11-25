#if UNITY_IOS
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace RealtimeEvents.Editor
{
    internal static class IosBuild
    {
        [PostProcessBuild(100)]
        private static void Configure(BuildTarget target, string buildPath)
        {
            if (target != BuildTarget.iOS) return;
            var package = PackageInfo.FindForAssembly(typeof(IosBuild).Assembly);
            // UPM resolves to its physical cache directory; a .unitypackage lives under Assets.
            // The source GUID also survives moving the imported SDK folder within Assets.
            var source = package == null ? AssetDatabase.GUIDToAssetPath("27ee6df5f0e14f019d14249295428c92") : null;
            ConfigureProject(buildPath, ResolvePackageRoot(package?.resolvedPath, source));
        }

        internal static string ResolvePackageRoot(string packagePath, string sourcePath)
        {
            if (!string.IsNullOrEmpty(packagePath)) return packagePath;
            if (string.IsNullOrEmpty(sourcePath))
                throw new System.IO.DirectoryNotFoundException("Could not locate the Databuckets SDK. Reimport its Editor folder and .meta files.");
            return Path.GetDirectoryName(Path.GetDirectoryName(sourcePath));
        }

        internal static void ConfigureProject(string buildPath, string packagePath)
        {
            const string relativePath = "Libraries/RealtimeEvents/RealtimeEventsFirstOpen.mm";
            var destination = Path.Combine(buildPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.Copy(Path.Combine(packagePath, "Editor/Native/RealtimeEventsFirstOpen.mm.txt"), destination, true);

            var projectPath = PBXProject.GetPBXProjectPath(buildPath);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            var framework = project.GetUnityFrameworkTargetGuid();
            var file = project.FindFileGuidByProjectPath(relativePath);
            if (string.IsNullOrEmpty(file))
                file = project.AddFile(relativePath, relativePath, PBXSourceTree.Source);
            // A file reference alone does not mean it belongs to this target.
            project.AddFileToBuild(framework, file);
            const string devicePath = "Libraries/RealtimeEvents/RealtimeEventsDeviceProperties.mm";
            File.Copy(Path.Combine(packagePath, "Editor/Native/RealtimeEventsDeviceProperties.mm.txt"),
                Path.Combine(buildPath, devicePath), true);
            var deviceFile = project.FindFileGuidByProjectPath(devicePath);
            if (string.IsNullOrEmpty(deviceFile)) deviceFile = project.AddFile(devicePath, devicePath, PBXSourceTree.Source);
            project.AddFileToBuild(framework, deviceFile);
            // Remove only this SDK's previous bridge from an appended Xcode export.
            const string oldPath = "Libraries/RealtimeEvents/RealtimeEventsFirstOpen.swift";
            var oldFile = project.FindFileGuidByProjectPath(oldPath);
            if (!string.IsNullOrEmpty(oldFile)) project.RemoveFile(oldFile);
            File.Delete(Path.Combine(buildPath, oldPath));
            AddPrivacyReason(project, framework, buildPath);
            project.WriteToFile(projectPath);
        }

        private static void AddPrivacyReason(PBXProject project, string framework, string buildPath)
        {
            const string relativePath = "UnityFramework/PrivacyInfo.xcprivacy";
            var path = Path.Combine(buildPath, relativePath);
            var manifest = new PlistDocument();
            if (File.Exists(path)) manifest.ReadFromFile(path);
            var apis = manifest.root.values.TryGetValue("NSPrivacyAccessedAPITypes", out var value)
                ? value.AsArray() : manifest.root.CreateArray("NSPrivacyAccessedAPITypes");
            // C617.1: metadata for files inside the app's container.
            AddReason(apis, "NSPrivacyAccessedAPICategoryFileTimestamp", "C617.1");
            // CA92.1: preferences belonging to this app (legacy SDK migration).
            AddReason(apis, "NSPrivacyAccessedAPICategoryUserDefaults", "CA92.1");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            manifest.WriteToFile(path);
            var file = project.FindFileGuidByProjectPath(relativePath);
            if (string.IsNullOrEmpty(file)) file = project.AddFile(relativePath, relativePath, PBXSourceTree.Source);
            // Explicitly attach the manifest to Resources, including on repeated exports.
            // AddFileToBuildSection does not deduplicate existing build entries.
            project.RemoveFileFromBuild(framework, file);
            project.AddFileToBuildSection(framework, project.GetResourcesBuildPhaseByTarget(framework), file);
        }

        private static void AddReason(PlistElementArray apis, string category, string reason)
        {
            var api = apis.values.Select(entry => entry.AsDict()).FirstOrDefault(entry =>
                entry.values.TryGetValue("NSPrivacyAccessedAPIType", out var type) && type.AsString() == category);
            if (api == null)
            {
                api = apis.AddDict();
                api.SetString("NSPrivacyAccessedAPIType", category);
            }
            var reasons = api.values.TryGetValue("NSPrivacyAccessedAPITypeReasons", out var value)
                ? value.AsArray() : api.CreateArray("NSPrivacyAccessedAPITypeReasons");
            if (!reasons.values.Any(item => item.AsString() == reason)) reasons.AddString(reason);
        }
    }
}
#endif
