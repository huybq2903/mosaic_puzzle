using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Falcon.InGame.LevelEditor
{
    /// <summary>Đọc ảnh từ clipboard của Windows qua PowerShell (ảnh được copy, hoặc file ảnh vừa copy trong Explorer).</summary>
    public static class ClipboardImage
    {
        private const int TimeoutMs = 10000;
        private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg" };

        public static bool TryRead(out byte[] data, out string name)
        {
            data = null;
            name = "paste";
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            var temp = Path.Combine(Path.GetTempPath(), "levelEditorPaste_" + Guid.NewGuid().ToString("N") + ".png");
            var script =
                "$ProgressPreference='SilentlyContinue';Add-Type -AssemblyName System.Windows.Forms,System.Drawing;" +
                "$c=[System.Windows.Forms.Clipboard];" +
                $"if($c::ContainsImage()){{$c::GetImage().Save('{temp.Replace("'", "''")}',[System.Drawing.Imaging.ImageFormat]::Png);'IMAGE'}}" +
                "elseif($c::ContainsFileDropList()){$c::GetFileDropList()[0]}";
            try
            {
                var info = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -STA -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)))
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.UTF8,
                };
                using var process = Process.Start(info);
                if (process == null) return false;
                var output = process.StandardOutput.ReadToEnd().Trim().Split('\n').Last().Trim();
                if (!process.WaitForExit(TimeoutMs)) { process.Kill(); return false; }
                if (output == "IMAGE" && File.Exists(temp))
                {
                    data = File.ReadAllBytes(temp);
                    return data.Length > 0;
                }
                if (output.Length > 0 && File.Exists(output) && Array.IndexOf(Extensions, Path.GetExtension(output).ToLowerInvariant()) >= 0)
                {
                    data = File.ReadAllBytes(output);
                    name = Path.GetFileNameWithoutExtension(output);
                    return true;
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("Không đọc được clipboard: " + e.Message);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { /* file tạm, bỏ qua */ }
            }
#endif
            return false;
        }
    }
}
