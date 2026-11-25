/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-10-14
 */

using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Editor
{
    /// <summary>
    ///     Giữ <c>BigDataVersion.VALUE</c> luôn khớp <c>package.json</c> — SINH TỰ ĐỘNG, không ai
    ///     phải nhớ sửa hai chỗ khi bump version.
    ///     <br/>Chạy ở hai mốc: khi <c>package.json</c> được import (sửa trong Unity), và một lần
    ///     mỗi lần nạp domain (bắt ca sửa file ngoài Unity rồi mở lại / <c>git pull</c> / máy CI).
    ///     <br/>Vì sao vẫn phải SINH RA HẰNG SỐ thay vì đọc file lúc chạy: <c>package.json</c> không
    ///     nằm trong <c>Resources</c> nên bản build không thấy nó; và đường log không nên có IO.
    ///     <br/>⚠ Đây là code EDITOR (asmdef chỉ-Editor) — không đi vào bản build.
    ///     <br/>⚠ Lưới an toàn cuối: <c>SdkVersionTests.ConstMatchesPackageJson</c>. Nếu ai tắt
    ///     domain reload hoặc sửa file sinh ra bằng tay thì test đỏ.
    /// </summary>
    public class BigDataVersionSync : AssetPostprocessor
    {
        private const string PACKAGE_SUFFIX = "/Modules/Core/BigData/package.json";
        private const string SCRIPT_SUFFIX = "/Modules/Core/BigData/Scripts/Runtime/BigDataVersion.cs";

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
            Debug.Log($"[BigData] sdkVersion đồng bộ theo package.json: " +
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
