/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 */

using Falcon.Helpers.Devkit;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Luật bắn <c>f_sdk_device_audio_change</c> (hợp đồng đợt 3 B4(c)): chỉ khi <b>qua mốc</b> — âm lượng về 0 / rời 0, đổi
    /// chế độ chuông, đổi đầu ra. Lần đọc đầu không phải "đổi". Một field không đọc được (null) ở lần trước hoặc lần này thì
    /// field đó không tính là đổi (không đoán).
    /// <br/>Pure; caller tự khoá.
    /// </summary>
    public class AudioChangeState
    {
        private AudioReading? _last;

        public AudioReading? Last => _last;

        public bool Record(AudioReading reading)
        {
            var before = _last;
            _last = reading;
            if (!before.HasValue) return false;
            var b = before.Value;

            var muteChanged = b.VolumePct.HasValue && reading.VolumePct.HasValue &&
                              b.VolumePct.Value == 0 != (reading.VolumePct.Value == 0);
            var ringerChanged = b.Ringer != null && reading.Ringer != null && b.Ringer != reading.Ringer;
            var outputChanged = b.Output != null && reading.Output != null && b.Output != reading.Output;
            return muteChanged || ringerChanged || outputChanged;
        }
    }

    /// <summary>
    /// Trạng thái quyền thông báo từ số đọc native (hợp đồng đợt 3 B14(b), bảng "tách chưa hỏi với đã từ chối"). Giá trị
    /// cùng bộ với <c>f_sdk_permission_push</c>: <c>granted</c> · <c>denied</c> · <c>not_determined</c> · (iOS)
    /// <c>provisional</c>. null = vắng.
    /// </summary>
    public static class PushStatusResolver
    {
        public const string Provisional = "provisional";

        /// <param name="promptSeen">SDK đã thấy popup OS xin quyền push (hàm bọc / game báo dòng <c>system</c>).</param>
        /// <param name="newInstall">Máy cài mới từ bản SDK có dấu FTUE (true), cập nhật từ bản cũ (false), chưa biết (null).</param>
        public static string Resolve(PushPermissionReading reading, bool promptSeen, bool? newInstall)
        {
            if (reading.IosAuthorizationStatus is { } ios)
                return ios switch
                {
                    0 => FPermissionValue.NotDetermined,
                    1 => FPermissionValue.Denied,
                    2 => FPermissionValue.Granted,
                    3 => Provisional,
                    4 => FPermissionValue.Granted, // ephemeral (App Clip) — được gửi như cấp quyền
                    _ => null
                };

            if (reading.AndroidSdk is not { } sdk) return null;
            if (sdk < 33)
                return reading.NotificationsEnabled is { } enabled
                    ? enabled ? FPermissionValue.Granted : FPermissionValue.Denied
                    : null;

            if (reading.Granted is not { } granted) return null;
            if (granted) return FPermissionValue.Granted;
            if (reading.Rationale == true) return FPermissionValue.Denied; // còn hỏi lại được
            if (promptSeen) return FPermissionValue.Denied; // từ chối hẳn
            return newInstall == true ? FPermissionValue.NotDetermined : null; // cập nhật từ bản cũ / chưa biết: vắng
        }

        /// <summary>iOS <c>ATTrackingAuthorizationStatus</c>: 0 notDetermined · 1 restricted · 2 denied · 3 authorized.</summary>
        public static string Att(int status) => status switch
        {
            0 => FPermissionValue.NotDetermined,
            1 => FPermissionValue.Restricted,
            2 => FPermissionValue.Denied,
            3 => FPermissionValue.Authorized,
            _ => null
        };
    }

    /// <summary>
    /// SDK tự đọc trạng thái quyền (B14(b)): chỉ báo khi giá trị <b>native</b> đổi so với lần đọc native trước — không so
    /// với giá trị cuối đã gửi (có thể do game báo): game báo <c>denied</c> mà native đọc <c>not_determined</c> thì so với
    /// giá trị đã gửi sẽ lật qua lại mỗi lần mở app.
    /// <br/>Dòng <c>system</c> tự bắn (iOS push / ATT): khi native rời <c>not_determined</c> — trừ khi game vừa tự báo dòng
    /// <c>system</c> cùng quyền trong <see cref="GAME_REPORT_WINDOW_MS"/>.
    /// </summary>
    public static class PermissionAutoRead
    {
        public const long GAME_REPORT_WINDOW_MS = 10_000;

        /// <returns>(có báo giá trị mới không, có tự bắn dòng system không)</returns>
        public static (bool report, bool autoPrompt) Decide(
            string lastNative, string current, bool autoPromptPlatform, long? sinceGameSystemReportMs)
        {
            if (current == null || current == lastNative) return (false, false);
            // provisional (iOS) có được mà KHÔNG có popup — không phải "popup OS vừa đóng"
            var autoPrompt = autoPromptPlatform && lastNative == FPermissionValue.NotDetermined &&
                             current != PushStatusResolver.Provisional &&
                             !(sinceGameSystemReportMs is { } since && since <= GAME_REPORT_WINDOW_MS);
            return (true, autoPrompt);
        }
    }
}
