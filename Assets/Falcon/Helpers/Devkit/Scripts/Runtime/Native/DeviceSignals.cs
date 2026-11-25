/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 */

using System;
using System.Collections.Generic;

// ReSharper disable once CheckNamespace
namespace Falcon.Helpers.Devkit
{
    /// <summary>Âm thanh máy (B4(c)). Field null = nền tảng không có / không đọc được.</summary>
    public readonly struct AudioReading
    {
        public readonly int? VolumePct;
        public readonly string Ringer;
        public readonly string Output;

        public AudioReading(int? volumePct, string ringer, string output)
        {
            VolumePct = volumePct;
            Ringer = ringer;
            Output = output;
        }
    }

    /// <summary>Cấu hình máy (B14(a)).</summary>
    public readonly struct DisplayReading
    {
        public readonly float? FontScale;
        public readonly bool? BoldText;
        public readonly bool? ReduceMotion;
        public readonly string StorageFreeBucket;

        public DisplayReading(float? fontScale, bool? boldText, bool? reduceMotion, string storageFreeBucket)
        {
            FontScale = fontScale;
            BoldText = boldText;
            ReduceMotion = reduceMotion;
            StorageFreeBucket = storageFreeBucket;
        }
    }

    /// <summary>Số đọc thô về quyền thông báo — BigData quy ra giá trị (bảng B14(b) cần cờ của BigData).</summary>
    public readonly struct PushPermissionReading
    {
        /// <summary>Android: <c>Build.VERSION.SDK_INT</c>; null trên iOS.</summary>
        public readonly int? AndroidSdk;

        /// <summary>Android 13+: <c>checkSelfPermission(POST_NOTIFICATIONS) == GRANTED</c>.</summary>
        public readonly bool? Granted;

        /// <summary>Android 13+: <c>shouldShowRequestPermissionRationale(POST_NOTIFICATIONS)</c>.</summary>
        public readonly bool? Rationale;

        /// <summary>Android &lt; 13: <c>NotificationManager.areNotificationsEnabled()</c> (API 24).</summary>
        public readonly bool? NotificationsEnabled;

        /// <summary>iOS: <c>UNAuthorizationStatus</c> (0 notDetermined · 1 denied · 2 authorized · 3 provisional · 4 ephemeral).</summary>
        public readonly int? IosAuthorizationStatus;

        public PushPermissionReading(int? androidSdk, bool? granted, bool? rationale, bool? notificationsEnabled,
            int? iosAuthorizationStatus)
        {
            AndroidSdk = androidSdk;
            Granted = granted;
            Rationale = rationale;
            NotificationsEnabled = notificationsEnabled;
            IosAuthorizationStatus = iosAuthorizationStatus;
        }
    }

    /// <summary>Một mục <c>ApplicationExitInfo</c> (Android 11+, API 30).</summary>
    public readonly struct ExitRecord
    {
        public readonly int Pid;
        public readonly long TimestampMs;
        public readonly int Reason;
        public readonly int Importance;

        public ExitRecord(int pid, long timestampMs, int reason, int importance)
        {
            Pid = pid;
            TimestampMs = timestampMs;
            Reason = reason;
            Importance = importance;
        }
    }

    /// <summary>Báo cáo MetricKit gần nhất (iOS 14+): <c>crash</c> / <c>hang</c> + mốc giờ cuối khoảng báo cáo (epoch ms).</summary>
    public readonly struct IosDiagnostic
    {
        public readonly string Kind;
        public readonly long TimestampMs;

        public IosDiagnostic(string kind, long timestampMs)
        {
            Kind = kind;
            TimestampMs = timestampMs;
        }
    }

    /// <summary>Nguồn đọc native của một nền tảng. Mọi hàm trả null khi không có / lỗi — không bao giờ ném.</summary>
    internal interface IDeviceSignalSource
    {
        int? ThermalOrdinal();
        bool? LowPowerMode();
        (string plug, float? tempC) Battery();
        AudioReading? Audio();
        DisplayReading? Display();
        long? MsSinceProcessStart();
        int? ProcessId();
        bool? SystemLowMemory();
        PushPermissionReading? PushPermission();
        int? IosAttStatus();
        List<ExitRecord> HistoricalExits(int max);
        IosDiagnostic? LatestIosDiagnostic();
        bool InstallJavaUncaughtHandler(Action<string, string> onFatal);
    }

    internal sealed class NullDeviceSignalSource : IDeviceSignalSource
    {
        public int? ThermalOrdinal() => null;
        public bool? LowPowerMode() => null;
        public (string plug, float? tempC) Battery() => (null, null);
        public AudioReading? Audio() => null;
        public DisplayReading? Display() => null;
        public long? MsSinceProcessStart() => null;
        public int? ProcessId() => null;
        public bool? SystemLowMemory() => null;
        public PushPermissionReading? PushPermission() => null;
        public int? IosAttStatus() => null;
        public List<ExitRecord> HistoricalExits(int max) => null;
        public IosDiagnostic? LatestIosDiagnostic() => null;
        public bool InstallJavaUncaughtHandler(Action<string, string> onFatal) => false;
    }

    /// <summary>
    /// Đọc tín hiệu máy native (hợp đồng BigData đợt 3, các field "(2)"). Lớp mỏng: chỉ đọc + quy đổi chung
    /// (<see cref="DeviceSignalMaps"/>); nghĩa log là việc của BigData.
    /// <list type="bullet">
    /// <item><b>Chỉ gọi trên main thread</b> (JNI / UIKit). Caller giữ kết quả nếu luồng khác cần.</item>
    /// <item>Không có / không đọc được ⇒ null (vắng ≠ 0). Editor và nền tảng khác ⇒ luôn null.</item>
    /// <item>iOS chỉ bật khi project có define <c>FALCON_DEVKIT_IOS_NATIVE</c> (file <c>.mm</c> chưa build trên Mac ở bản
    /// này — tắt mặc định để không làm hỏng build iOS của game).</item>
    /// </list>
    /// </summary>
    public static class DeviceSignals
    {
        private static readonly IDeviceSignalSource kNull = new NullDeviceSignalSource();
        private const long RETRY_CREATE_MS = 10_000;

        private static IDeviceSignalSource _source;
        private static long _lastFailedAt = long.MinValue;
        private static bool _warned;

        /// <summary>
        /// Nguồn của nền tảng. Dựng hỏng (JNI chưa sẵn…) thì KHÔNG ghi nhớ thất bại — thử lại sau
        /// <see cref="RETRY_CREATE_MS"/>, trong lúc đó trả nguồn rỗng; cảnh báo một lần.
        /// </summary>
        private static IDeviceSignalSource Source
        {
            get
            {
                if (_source != null) return _source;
                var now = MonotonicClock.ElapsedMillis;
                if (_lastFailedAt != long.MinValue && now - _lastFailedAt < RETRY_CREATE_MS) return kNull;
                try
                {
                    return _source = Create();
                }
                catch (Exception e)
                {
                    _lastFailedAt = now;
                    if (!_warned)
                    {
                        _warned = true;
                        BaseSystemLogger.Instance.Warning("DeviceSignals: không dựng được nguồn native — thử lại sau: " + e.Message);
                    }

                    return kNull;
                }
            }
        }

        private static IDeviceSignalSource Create()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidDeviceSignalSource();
#elif UNITY_IOS && FALCON_DEVKIT_IOS_NATIVE && !UNITY_EDITOR
            return new IosDeviceSignalSource();
#else
            return kNull;
#endif
        }

        /// <summary>
        /// Có nguồn báo cáo chẩn đoán của OS giao bất đồng bộ (iOS MetricKit, chỉ khi bật define). Caller dùng để quyết có
        /// chờ báo cáo hay không — không có thì đừng chờ.
        /// </summary>
        public static bool DiagnosticsAsync
        {
            get
            {
#if UNITY_IOS && FALCON_DEVKIT_IOS_NATIVE && !UNITY_EDITOR
                return Source is IosDeviceSignalSource;
#else
                return false;
#endif
            }
        }

        /// <summary>Thứ tự mức nhiệt 0–3 (<see cref="DeviceSignalMaps.ThermalLevels"/>). Android 10+ / iOS.</summary>
        public static int? ThermalOrdinal() => Source.ThermalOrdinal();

        public static bool? LowPowerMode() => Source.LowPowerMode();

        /// <summary>Kiểu cắm sạc + nhiệt độ pin (°C). Chỉ Android.</summary>
        public static (string plug, float? tempC) Battery() => Source.Battery();

        public static AudioReading? Audio() => Source.Audio();

        public static DisplayReading? Display() => Source.Display();

        /// <summary>ms từ lúc tiến trình bắt đầu. Chỉ Android 7+ (iOS chờ soát "required reason API").</summary>
        public static long? MsSinceProcessStart() => Source.MsSinceProcessStart();

        /// <summary>pid của tiến trình (Android <c>Process.myPid()</c>).</summary>
        public static int? ProcessId() => Source.ProcessId();

        /// <summary>Android <c>ActivityManager.MemoryInfo.lowMemory</c>.</summary>
        public static bool? SystemLowMemory() => Source.SystemLowMemory();

        public static PushPermissionReading? PushPermission() => Source.PushPermission();

        /// <summary>iOS <c>ATTrackingAuthorizationStatus</c> (0 notDetermined · 1 restricted · 2 denied · 3 authorized). iOS 14+.</summary>
        public static int? IosAttStatus() => Source.IosAttStatus();

        /// <summary>Lý do thoát lịch sử của app (Android 11+), mới nhất trước.</summary>
        public static List<ExitRecord> HistoricalExits(int max) => Source.HistoricalExits(max);

        public static IosDiagnostic? LatestIosDiagnostic() => Source.LatestIosDiagnostic();

        /// <summary>
        /// Cài handler ngoại lệ Java không ai bắt (Android). <paramref name="onFatal"/> nhận (tên lớp lỗi, các khung stack
        /// mỗi dòng một khung) trên luồng đang chết; handler cũ (Crashlytics, mặc định của hệ thống) LUÔN được gọi tiếp sau đó.
        /// </summary>
        public static bool InstallJavaUncaughtHandler(Action<string, string> onFatal) =>
            Source.InstallJavaUncaughtHandler(onFatal);
    }
}
