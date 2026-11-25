/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 */

#if UNITY_IOS && FALCON_DEVKIT_IOS_NATIVE && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

// ReSharper disable once CheckNamespace
namespace Falcon.Helpers.Devkit
{
    /// <summary>
    /// Đọc native iOS qua <c>iOS/FalconDeviceSignals.mm</c>. Chỉ biên dịch khi project bật define
    /// <c>FALCON_DEVKIT_IOS_NATIVE</c> (file <c>.mm</c> cùng điều kiện) — chưa build trên Mac ở bản này.
    /// Không có: kiểu cắm / nhiệt độ pin, chế độ chuông, dung lượng trống (API "required reason"), ms từ lúc khởi tiến
    /// trình (chờ soát), pid, bộ nhớ hệ thống, lý do thoát kiểu Android.
    /// </summary>
    internal sealed class IosDeviceSignalSource : IDeviceSignalSource
    {
        [DllImport("__Internal")] private static extern int FalconDS_ThermalState();
        [DllImport("__Internal")] private static extern int FalconDS_LowPowerMode();
        [DllImport("__Internal")] private static extern float FalconDS_MediaVolume();
        [DllImport("__Internal")] private static extern string FalconDS_AudioOutputPort();
        [DllImport("__Internal")] private static extern string FalconDS_ContentSizeCategory();
        [DllImport("__Internal")] private static extern int FalconDS_BoldText();
        [DllImport("__Internal")] private static extern int FalconDS_ReduceMotion();
        [DllImport("__Internal")] private static extern void FalconDS_RefreshPushStatus();
        [DllImport("__Internal")] private static extern int FalconDS_PushStatus();
        [DllImport("__Internal")] private static extern int FalconDS_AttStatus();
        [DllImport("__Internal")] private static extern void FalconDS_StartMetricKit();
        [DllImport("__Internal")] private static extern int FalconDS_LastDiagnosticKind();
        [DllImport("__Internal")] private static extern long FalconDS_LastDiagnosticTimestampMs();

        public IosDeviceSignalSource()
        {
            // Bản tin MetricKit giao ở lần mở sau — đăng ký sớm nhất có thể
            Safe(FalconDS_StartMetricKit);
            Safe(FalconDS_RefreshPushStatus);
        }

        public int? ThermalOrdinal() => Read(() => DeviceSignalMaps.ThermalOrdinalFromIos(FalconDS_ThermalState()));

        public bool? LowPowerMode() => Read(() => Bool(FalconDS_LowPowerMode()));

        public (string plug, float? tempC) Battery() => (null, null);

        public AudioReading? Audio() => Read<AudioReading?>(() =>
        {
            var volume = FalconDS_MediaVolume();
            return new AudioReading(volume < 0 ? null : DeviceSignalMaps.VolumeBucket(volume), null,
                DeviceSignalMaps.OutputFromIos(FalconDS_AudioOutputPort()));
        });

        public DisplayReading? Display() => Read<DisplayReading?>(() => new DisplayReading(
            DeviceSignalMaps.FontScaleFromIos(FalconDS_ContentSizeCategory()), Bool(FalconDS_BoldText()),
            Bool(FalconDS_ReduceMotion()), null));

        public long? MsSinceProcessStart() => null;
        public int? ProcessId() => null;
        public bool? SystemLowMemory() => null;

        /// <summary>Trạng thái đọc bất đồng bộ: lần gọi này trả kết quả lần đọc trước và kích lần đọc mới.</summary>
        public PushPermissionReading? PushPermission() => Read<PushPermissionReading?>(() =>
        {
            var status = FalconDS_PushStatus();
            FalconDS_RefreshPushStatus();
            return status < 0 ? null : new PushPermissionReading(null, null, null, null, status);
        });

        public int? IosAttStatus() => Read<int?>(() =>
        {
            var status = FalconDS_AttStatus();
            return status < 0 ? null : status;
        });

        public List<ExitRecord> HistoricalExits(int max) => null;

        public IosDiagnostic? LatestIosDiagnostic() => Read<IosDiagnostic?>(() =>
        {
            var kind = FalconDS_LastDiagnosticKind() switch { 1 => "crash", 2 => "hang", _ => null };
            return kind == null ? null : new IosDiagnostic(kind, FalconDS_LastDiagnosticTimestampMs());
        });

        public bool InstallJavaUncaughtHandler(Action<string, string> onFatal) => false;

        private static bool? Bool(int value) => value < 0 ? null : value != 0;

        private static T Read<T>(Func<T> read)
        {
            try
            {
                return read();
            }
            catch (Exception)
            {
                return default;
            }
        }

        private static void Safe(Action action)
        {
            try
            {
                action();
            }
            catch (Exception)
            {
                // bỏ
            }
        }
    }
}
#endif
