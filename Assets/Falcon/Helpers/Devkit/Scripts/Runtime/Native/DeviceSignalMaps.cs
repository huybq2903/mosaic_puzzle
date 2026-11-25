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
    /// <summary>
    /// Quy đổi số đọc native thô (Android / iOS) về giá trị chung giữa hai nền tảng — hợp đồng BigData đợt 3, phần "(2)".
    /// Pure: không JNI, không API Unity — test được ngoài máy. Hằng số Android chép từ developer.android.com/reference
    /// (đối chiếu 23/11/2026), kèm API level.
    /// </summary>
    public static class DeviceSignalMaps
    {
        // ---- Nhiệt độ máy (B1 perf_thermal_state) ----

        /// <summary>Bốn mức chung (theo tên iOS <c>NSProcessInfoThermalState</c>); số thứ tự dùng cho <c>thermal_state_max</c>.</summary>
        public static readonly string[] ThermalLevels = { "nominal", "fair", "serious", "critical" };

        // PowerManager.THERMAL_STATUS_* (API 29): NONE 0 · LIGHT 1 · MODERATE 2 · SEVERE 3 · CRITICAL 4 · EMERGENCY 5 · SHUTDOWN 6
        public const int ANDROID_THERMAL_NONE = 0;
        public const int ANDROID_THERMAL_LIGHT = 1;
        public const int ANDROID_THERMAL_MODERATE = 2;
        public const int ANDROID_THERMAL_SEVERE = 3;
        public const int ANDROID_THERMAL_CRITICAL = 4;
        public const int ANDROID_THERMAL_SHUTDOWN = 6;

        /// <summary>
        /// Bảy mức Android gộp về bốn: NONE → nominal · LIGHT / MODERATE → fair · SEVERE → serious · CRITICAL / EMERGENCY /
        /// SHUTDOWN → critical. Ngoài dải ⇒ null.
        /// </summary>
        public static int? ThermalOrdinalFromAndroid(int status)
        {
            if (status < ANDROID_THERMAL_NONE || status > ANDROID_THERMAL_SHUTDOWN) return null;
            if (status == ANDROID_THERMAL_NONE) return 0;
            if (status <= ANDROID_THERMAL_MODERATE) return 1;
            if (status == ANDROID_THERMAL_SEVERE) return 2;
            return 3;
        }

        /// <summary>iOS <c>NSProcessInfoThermalState</c>: nominal 0 · fair 1 · serious 2 · critical 3.</summary>
        public static int? ThermalOrdinalFromIos(int state) => state is >= 0 and <= 3 ? state : null;

        public static string ThermalName(int? ordinal) =>
            ordinal is >= 0 and <= 3 ? ThermalLevels[ordinal.Value] : null;

        // ---- Pin (B3 battery_plug / battery_temp_c) ----

        // BatteryManager.BATTERY_PLUGGED_* : AC 1 · USB 2 · WIRELESS 4 (API 17) · DOCK 8 (API 33)
        public const int ANDROID_PLUGGED_AC = 1;
        public const int ANDROID_PLUGGED_USB = 2;
        public const int ANDROID_PLUGGED_WIRELESS = 4;
        public const int ANDROID_PLUGGED_DOCK = 8;

        /// <summary>
        /// <c>EXTRA_PLUGGED</c> → <c>ac</c> · <c>usb</c> · <c>wireless</c> · <c>none</c>. Dock (API 33) xếp <c>ac</c> (đế sạc cắm
        /// điện). Số lạ ⇒ null.
        /// </summary>
        public static string PlugFromAndroid(int plugged) => plugged switch
        {
            0 => "none",
            ANDROID_PLUGGED_AC => "ac",
            ANDROID_PLUGGED_USB => "usb",
            ANDROID_PLUGGED_WIRELESS => "wireless",
            ANDROID_PLUGGED_DOCK => "ac",
            _ => null
        };

        /// <summary><c>EXTRA_TEMPERATURE</c> là phần mười độ C. Ngoài dải hợp lý (−40…100 °C) ⇒ null.</summary>
        public static float? BatteryTempFromAndroid(int tenths)
        {
            var celsius = tenths / 10f;
            return celsius is >= -40f and <= 100f ? (float)Math.Round(celsius, 1) : null;
        }

        // ---- Âm thanh máy (B4(c)) ----

        /// <summary>
        /// Âm lượng nhạc theo bậc 10 %: <b>0 nghĩa đúng là tắt tiếng</b>; khác 0 thì làm tròn LÊN tới bậc kế (1 % → 10) —
        /// "về 0 / rời 0" của <c>f_sdk_device_audio_change</c> không được bắn oan vì làm tròn.
        /// </summary>
        public static int? VolumeBucket(double fraction)
        {
            if (double.IsNaN(fraction) || fraction < 0) return null;
            if (fraction <= 0) return 0;
            return (int)Math.Min(100, Math.Ceiling(Math.Round(fraction * 10, 6)) * 10);
        }

        public static int? VolumeBucket(int current, int max) =>
            max <= 0 || current < 0 ? null : VolumeBucket((double)current / max);

        /// <summary>AudioManager.RINGER_MODE_*: SILENT 0 · VIBRATE 1 · NORMAL 2 (đọc lại hằng số qua JNI lúc chạy).</summary>
        public static string RingerFromAndroid(int mode, int silent, int vibrate, int normal)
        {
            if (mode == silent) return "silent";
            if (mode == vibrate) return "vibrate";
            return mode == normal ? "normal" : null;
        }

        // AudioDeviceInfo.TYPE_* (API 23 trừ khi ghi khác)
        private static readonly HashSet<int> kAndroidBluetooth = new()
        {
            7, // BLUETOOTH_SCO
            8, // BLUETOOTH_A2DP
            23, // HEARING_AID (API 28)
            26, // BLE_HEADSET (API 31)
            27 // BLE_SPEAKER (API 31)
        };

        private static readonly HashSet<int> kAndroidWired = new()
        {
            3, // WIRED_HEADSET
            4, // WIRED_HEADPHONES
            5, // LINE_ANALOG
            11, // USB_DEVICE
            19, // AUX_LINE
            22 // USB_HEADSET (API 26)
        };

        /// <summary>
        /// Đầu ra âm thanh từ danh sách thiết bị ra đang có (<c>getDevices(GET_DEVICES_OUTPUTS)</c>, API 23). Có nhiều thì
        /// <b>bluetooth &gt; wired &gt; speaker</b> — gần đúng thứ tự chọn đường của Android cho nhạc; không đọc được đường
        /// thật đang phát. Danh sách rỗng ⇒ null.
        /// </summary>
        public static string OutputFromAndroid(IEnumerable<int> deviceTypes)
        {
            if (deviceTypes == null) return null;
            bool any = false, wired = false;
            foreach (var type in deviceTypes)
            {
                any = true;
                if (kAndroidBluetooth.Contains(type)) return "bluetooth";
                if (kAndroidWired.Contains(type)) wired = true;
            }

            return !any ? null : wired ? "wired" : "speaker";
        }

        /// <summary>
        /// iOS <c>AVAudioSessionPort*</c> của đầu ra đang phát (đường thật). AirPlay / HDMI / CarPlay… không thuộc ba giá trị
        /// ⇒ null (vắng).
        /// </summary>
        public static string OutputFromIos(string portType) => portType switch
        {
            "BluetoothA2DPOutput" or "BluetoothHFP" or "BluetoothLE" => "bluetooth",
            "Headphones" or "LineOut" or "USBAudio" => "wired",
            "Speaker" or "Receiver" => "speaker",
            _ => null
        };

        // ---- Cấu hình máy (B14(a)) ----

        /// <summary>
        /// iOS <c>UIContentSizeCategory</c> → hệ số cỡ chữ = cỡ chữ Body của mức đó / cỡ mặc định (Large, 17 pt): XS 14 · S
        /// 15 · M 16 · L 17 · XL 19 · XXL 21 · XXXL 23 · AX1 28 · AX2 33 · AX3 40 · AX4 47 · AX5 53 pt.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, float> IosFontScale = new Dictionary<string, float>
        {
            ["UICTContentSizeCategoryXS"] = 0.82f,
            ["UICTContentSizeCategoryS"] = 0.88f,
            ["UICTContentSizeCategoryM"] = 0.94f,
            ["UICTContentSizeCategoryL"] = 1.00f,
            ["UICTContentSizeCategoryXL"] = 1.12f,
            ["UICTContentSizeCategoryXXL"] = 1.24f,
            ["UICTContentSizeCategoryXXXL"] = 1.35f,
            ["UICTContentSizeCategoryAccessibilityM"] = 1.65f,
            ["UICTContentSizeCategoryAccessibilityL"] = 1.94f,
            ["UICTContentSizeCategoryAccessibilityXL"] = 2.35f,
            ["UICTContentSizeCategoryAccessibilityXXL"] = 2.76f,
            ["UICTContentSizeCategoryAccessibilityXXXL"] = 3.12f
        };

        public static float? FontScaleFromIos(string category) =>
            category != null && IosFontScale.TryGetValue(category, out var scale) ? scale : null;

        /// <summary>Configuration.FONT_WEIGHT_ADJUSTMENT_UNDEFINED (API 31) = Integer.MAX_VALUE.</summary>
        public const int ANDROID_FONT_WEIGHT_UNDEFINED = int.MaxValue;

        /// <summary><c>fontWeightAdjustment</c> (API 31): &gt; 0 ⇒ chữ đậm; UNDEFINED ⇒ null.</summary>
        public static bool? BoldFromAndroid(int adjustment) =>
            adjustment == ANDROID_FONT_WEIGHT_UNDEFINED ? null : adjustment > 0;

        /// <summary>Dung lượng trống: <c>lt_1gb</c> · <c>1_5gb</c> · <c>5_20gb</c> · <c>gt_20gb</c> (GiB = 1024³ byte).</summary>
        public static string StorageBucket(long freeBytes)
        {
            if (freeBytes < 0) return null;
            const double gib = 1024d * 1024 * 1024;
            var gb = freeBytes / gib;
            if (gb < 1) return "lt_1gb";
            if (gb < 5) return "1_5gb";
            return gb < 20 ? "5_20gb" : "gt_20gb";
        }
    }
}
