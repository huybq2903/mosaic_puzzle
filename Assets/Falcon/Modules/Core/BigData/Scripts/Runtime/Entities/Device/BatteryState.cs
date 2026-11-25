/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 */

using System;
using System.Collections.Generic;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>Một lần đọc pin. Field null = nền tảng không trả ⇒ vắng.</summary>
    public readonly struct BatteryReading
    {
        public readonly int? Pct;
        public readonly bool? Charging;

        /// <summary>Kiểu cắm sạc (đợt 2, chỉ Android): <c>ac</c> · <c>usb</c> · <c>wireless</c> · <c>none</c>.</summary>
        public readonly string Plug;

        /// <summary>Nhiệt độ pin °C (đợt 2, chỉ Android).</summary>
        public readonly float? TempC;

        public BatteryReading(int? pct, bool? charging, string plug = null, float? tempC = null)
        {
            Pct = pct;
            Charging = charging;
            Plug = plug;
            TempC = tempC;
        }

        public BatteryReading WithNative((string plug, float? tempC) native) =>
            new(Pct, Charging, native.plug, native.tempC);

        /// <summary>
        /// Quy giá trị thô của Unity: <c>SystemInfo.batteryLevel</c> (0–1, -1 = không biết) và trạng thái
        /// (<c>Charging</c> / <c>Full</c> = đang cắm sạc; <c>Discharging</c> / <c>NotCharging</c> = không; <c>Unknown</c> = vắng).
        /// </summary>
        public static BatteryReading FromUnity(float level, string status)
        {
            int? pct = level >= 0 && level <= 1 && !float.IsNaN(level) ? (int)Math.Round(level * 100) : null;
            bool? charging = status switch
            {
                "Charging" or "Full" => true,
                "Discharging" or "NotCharging" => false,
                _ => null
            };
            return new BatteryReading(pct, charging);
        }

        /// <summary>Field hợp đồng B3 (đợt 1). <c>battery_plug</c> / <c>battery_temp_c</c> đến ở đợt 2 (native).</summary>
        public void PutInto(Dictionary<string, object> dictionary)
        {
            if (Pct.HasValue) dictionary["batteryPct"] = Pct.Value;
            if (Charging.HasValue) dictionary["batteryCharging"] = Charging.Value;
            if (Plug != null) dictionary["batteryPlug"] = Plug;
            if (TempC.HasValue) dictionary["batteryTempC"] = TempC.Value;
        }
    }

    /// <summary>
    /// Giữ lần đọc pin gần nhất và quyết khi nào bắn <c>f_sdk_battery_charging_change</c> (hợp đồng đợt 3 B3): SDK đọc lại
    /// khoảng mỗi <see cref="POLL_SEC"/> giây lúc app ở trước, bắn khi <c>battery_charging</c> ĐỔI so với lần đọc trước.
    /// Lần đọc đầu không phải "đổi". Đổi lúc app nằm nền thì chỉ thấy khi quay lại — đúng ý "lúc app ở trước".
    /// <br/>Pure class; caller khoá.
    /// </summary>
    public class BatteryState
    {
        public const float POLL_SEC = 10f;

        private BatteryReading? _last;

        public BatteryReading? Last => _last;

        /// <returns>true nếu lần đọc này đổi trạng thái sạc (cả hai lần đều biết).</returns>
        public bool Record(BatteryReading reading)
        {
            var changed = _last?.Charging is { } before && reading.Charging is { } after && before != after;
            _last = reading;
            return changed;
        }
    }
}
