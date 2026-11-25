/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 */

using System;
using System.Collections.Generic;
using Falcon.Helpers.Devkit;
using UnityEngine;
using UnityEngine.Scripting;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>Trạng thái sạc đổi lúc app ở trước — <c>f_sdk_battery_charging_change</c> (hợp đồng đợt 3 B3).</summary>
    [Serializable]
    public class FBatteryChargingChangeLog : AFalconLog
    {
        [FKey(Ignore = true)] public BatteryReading reading;

        [Preserve]
        public FBatteryChargingChangeLog()
        {
        }

        public override string Event => "f_sdk_battery_charging_change";

        public override Dictionary<string, object> ToDictionary()
        {
            var result = base.ToDictionary();
            reading.PutInto(result);
            return result;
        }
    }

    /// <summary>
    /// Đọc pin bằng API có sẵn của Unity (không cần quyền, không native) và gửi theo hợp đồng đợt 3 B3: field trên
    /// <c>app_open</c> / <c>f_sdk_perf_screen</c>, dòng <c>f_sdk_battery_charging_change</c> khi đổi trạng thái sạc.
    /// <br/>Chỉ đọc trên main thread (<see cref="Poll"/> gọi từ Update của <c>PerfSampleService</c>, <see cref="ReadNow"/> từ
    /// vòng đời app); luồng dựng log chỉ đọc <see cref="Last"/> đã giữ.
    /// </summary>
    public class BatteryLogService : MySingleton<BatteryLogService>
    {
        private readonly BatteryState _state = new();
        private readonly object _lock = new();
        private readonly LogScheduleService _logScheduleService;
        private readonly AnalyticConfigService _configService;
        private float _sinceLastPollSec;

        public BatteryLogService(LogScheduleService logScheduleService, AnalyticConfigService configService)
        {
            _logScheduleService = logScheduleService;
            _configService = configService;
        }

        /// <summary>Lần đọc gần nhất (null nếu chưa đọc lần nào).</summary>
        public BatteryReading? Last
        {
            get
            {
                lock (_lock) return _state.Last;
            }
        }

        /// <summary>Main thread, mỗi khung: đọc lại sau mỗi ~10 giây lúc app ở trước.</summary>
        public void Poll(float unscaledDeltaSec)
        {
            _sinceLastPollSec += unscaledDeltaSec;
            if (_sinceLastPollSec < BatteryState.POLL_SEC) return;
            _sinceLastPollSec = 0;
            ReadNow();
        }

        /// <summary>Main thread: đọc ngay (lúc mở app / quay lại). Đổi trạng thái sạc thì bắn dòng.</summary>
        public BatteryReading ReadNow()
        {
            var reading = BatteryReading.FromUnity(SystemInfo.batteryLevel, SystemInfo.batteryStatus.ToString());
            if (_configService.DeviceSignalsEnabled) reading = reading.WithNative(DeviceSignals.Battery());
            bool changed;
            lock (_lock) changed = _state.Record(reading);
            if (changed) _logScheduleService.Enqueue(new FBatteryChargingChangeLog { reading = reading });
            return reading;
        }
    }
}
