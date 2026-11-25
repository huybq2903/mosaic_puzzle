/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 */

using System;
using Falcon.Helpers.Devkit;
using UnityEngine.Scripting;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Tóm tắt hiệu năng của MỘT đoạn ở một màn (hợp đồng đợt 3 B1(b), ký 23/11): một dòng mỗi lần rời một màn, và lúc
    /// app xuống nền (đóng đoạn đang dở). Số đo chỉ của lúc app ở trước; không có khung nào thì các số vắng.
    /// Thời gian ở màn: server tự trừ giờ với dòng trước, SDK không gửi.
    /// </summary>
    [Serializable]
    public class FPerfScreenLog : AFalconLog
    {
        /// <summary>Màn vừa rời — cùng bộ giá trị với <c>perf_screen</c> của gói hiệu năng.</summary>
        public string perfScreen;

        [FKey(RemoveIfNull = true)] public float? fpsAvg;
        [FKey(RemoveIfNull = true)] public int? frameWorstMs;
        [FKey(RemoveIfNull = true)] public int? frameDrops;
        [FKey(RemoveIfNull = true)] public int? memWarnings;

        /// <summary>Trần fps game đặt — chỉ gửi khi game ĐỔI trần so với lúc mở app (giá trị lúc mở nằm trên <c>app_open</c>).</summary>
        [FKey(RemoveIfNull = true)] public int? perfTargetFps;

        /// <summary>Pin lần đọc gần nhất (B3, đọc mỗi ~10 giây) — xem <see cref="BatteryReading"/>.</summary>
        [FKey(RemoveIfNull = true)] public int? batteryPct;
        [FKey(RemoveIfNull = true)] public bool? batteryCharging;
        [FKey(RemoveIfNull = true)] public string batteryPlug;
        [FKey(RemoveIfNull = true)] public float? batteryTempC;

        /// <summary>Mức nhiệt cao nhất trong đoạn (đợt 2): <c>nominal</c> · <c>fair</c> · <c>serious</c> · <c>critical</c>.</summary>
        [FKey(RemoveIfNull = true)] public string thermalStateMax;

        /// <summary>Có lúc nào trong đoạn bật tiết kiệm pin (đợt 2).</summary>
        [FKey(RemoveIfNull = true)] public bool? lowPower;

        [Preserve]
        public FPerfScreenLog()
        {
        }

        public static FPerfScreenLog From(SurfaceSegment segment, int? changedTargetFps, BatteryReading? battery = null)
        {
            return new FPerfScreenLog
            {
                batteryPct = battery?.Pct,
                batteryCharging = battery?.Charging,
                batteryPlug = battery?.Plug,
                batteryTempC = battery?.TempC,
                thermalStateMax = DeviceSignalMaps.ThermalName(segment.ThermalMax),
                lowPower = segment.LowPower,
                perfScreen = segment.Screen,
                fpsAvg = segment.Perf.AvgFps.HasValue ? (float)Math.Round(segment.Perf.AvgFps.Value, 1) : null,
                frameWorstMs = segment.Perf.WorstFrameMs,
                frameDrops = segment.Perf.FrameDropCount,
                memWarnings = segment.Perf.MemoryWarningCount,
                perfTargetFps = changedTargetFps
            };
        }

        public override string Event => "f_sdk_perf_screen";
    }
}
