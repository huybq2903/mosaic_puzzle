/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 */

using System;
using System.Collections.Generic;
using Falcon.Helpers.Devkit;
using UnityEngine.Scripting;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Âm thanh máy qua mốc (hợp đồng đợt 3 B4(c)): âm lượng về 0 / rời 0, đổi chế độ chuông, đổi đầu ra. Mang trạng thái
    /// SAU khi đổi (cùng ba field của <c>app_open</c>); trạng thái trước là dòng trước / <c>app_open</c>.
    /// </summary>
    [Serializable]
    public class FDeviceAudioChangeLog : AFalconLog
    {
        [FKey(RemoveIfNull = true)] public int? deviceMediaVolumePct;
        [FKey(RemoveIfNull = true)] public string deviceRingerMode;
        [FKey(RemoveIfNull = true)] public string audioOutput;

        [Preserve]
        public FDeviceAudioChangeLog()
        {
        }

        public override string Event => "f_sdk_device_audio_change";
    }

    /// <summary>
    /// Đọc tín hiệu máy native (Devkit <see cref="DeviceSignals"/>, hợp đồng đợt 3 phần "(2)") theo nhịp: mỗi
    /// <see cref="POLL_SEC"/> giây lúc app ở trước, lúc quay lại từ nền và lúc lấy lại focus — không bao giờ mỗi khung.
    /// Nhiệt độ / tiết kiệm pin đẩy vào gói hiệu năng (<see cref="PerfPackRepository"/>, đọc cache — luồng dựng log không
    /// gọi native); âm thanh qua mốc thì bắn <see cref="FDeviceAudioChangeLog"/>; trạng thái quyền tự đọc
    /// (<see cref="PermissionAutoReadService"/>).
    /// <br/>Main thread.
    /// </summary>
    public class DeviceSignalService : MySingleton<DeviceSignalService>
    {
        public const float POLL_SEC = 10f;

        private readonly LogScheduleService _logScheduleService;
        private readonly PerfPackRepository _perfPackRepository;
        private readonly PermissionAutoReadService _permissionAutoRead;
        private readonly AnalyticConfigService _configService;
        private readonly AudioChangeState _audio = new();
        private float _sinceLastPollSec = POLL_SEC; // khung đầu đọc ngay

        public DeviceSignalService(LogScheduleService logScheduleService, PerfPackRepository perfPackRepository,
            PermissionAutoReadService permissionAutoRead, AnalyticConfigService configService)
        {
            _configService = configService;
            _permissionAutoRead = permissionAutoRead;
            _logScheduleService = logScheduleService;
            _perfPackRepository = perfPackRepository;
        }

        /// <summary>Âm thanh máy đọc gần nhất (cho <c>app_open</c> Hot — Cold thì đọc mới qua <see cref="ReadAudioNow"/>).</summary>
        public AudioReading? LastAudio => _audio.Last;

        /// <summary>Mỗi khung (PerfSampleService.Update) — tới nhịp mới đọc native.</summary>
        public void Poll(float unscaledDeltaSec)
        {
            _sinceLastPollSec += unscaledDeltaSec;
            if (_sinceLastPollSec < POLL_SEC) return;
            Refresh();
        }

        /// <summary>Đọc ngay (quay lại từ nền, lấy lại focus).</summary>
        /// <summary>Van <c>fDeviceSignalsEnabled</c> (mặc định bật).</summary>
        public bool Enabled => _configService.DeviceSignalsEnabled;

        public void Refresh()
        {
            _sinceLastPollSec = 0;
            if (!Enabled) return;
            try
            {
                _perfPackRepository.SetDeviceState(DeviceSignals.ThermalOrdinal(), DeviceSignals.LowPowerMode());
                ReadAudioNow();
                _permissionAutoRead.ReadNow(); // B14(b): mở app / quay lại / lấy lại focus / mỗi nhịp
            }
            catch (Exception e)
            {
                AnalyticLogger.Instance.Warning("Đọc tín hiệu máy native lỗi: " + e.Message);
            }
        }

        /// <summary>Đọc âm thanh máy ngay; qua mốc so với lần trước thì bắn dòng đổi.</summary>
        public AudioReading? ReadAudioNow()
        {
            if (!Enabled) return null;
            var reading = DeviceSignals.Audio();
            if (!reading.HasValue) return null;
            if (_audio.Record(reading.Value))
                _logScheduleService.Enqueue(new FDeviceAudioChangeLog
                {
                    deviceMediaVolumePct = reading.Value.VolumePct,
                    deviceRingerMode = reading.Value.Ringer,
                    audioOutput = reading.Value.Output
                });
            return reading;
        }
    }
}
