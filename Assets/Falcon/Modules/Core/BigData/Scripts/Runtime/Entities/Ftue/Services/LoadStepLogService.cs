/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 */

using System;
using System.Threading;
using System.Threading.Tasks;
using Falcon.Helpers.Devkit;
using Falcon.Modules.Core.RemoteConfig;
using UnityEngine.Scripting;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>Một trạm tải ở lần mở đầu / lần vào level đầu — <c>f_sdk_ftue_load_step</c> (hợp đồng đợt 3 B2, ký 23/11).</summary>
    [Serializable]
    public class FFtueLoadStepLog : AFalconLog
    {
        public LoadKind loadKind;
        public LoadStep step;

        /// <summary>Từ lúc bắt đầu tải app tới trạm này, app có bị đẩy xuống nền không (chỉ loại app).</summary>
        [FKey(RemoveIfNull = true)] public bool? wentBackground;

        [FKey(RemoveIfNull = true)] public NetworkType? networkType;

        /// <summary>Chỉ <see cref="LoadStep.AssetDownloadDone"/>.</summary>
        [FKey(RemoveIfNull = true)] public long? assetBytes;

        /// <summary>Chỉ <see cref="LoadStep.LoadError"/>: timeout / lỗi mạng / lỗi khác (game đặt).</summary>
        [FKey(RemoveIfNull = true)] public string errorCode;

        /// <summary>
        /// Chỉ <c>load_start</c> (đợt 2, chỉ Android 7+): ms từ lúc tiến trình bắt đầu tới lúc C# sống dậy (= giờ của dòng).
        /// iOS vắng — chờ soát "required reason API".
        /// </summary>
        [FKey(RemoveIfNull = true)] public long? msSinceProcessStart;

        [Preserve]
        public FFtueLoadStepLog()
        {
        }

        public override string Event => "f_sdk_ftue_load_step";
    }

    /// <summary>
    /// Trạm tải của họ FTUE (hợp đồng đợt 3 B2). SDK tự báo <c>load_start</c> (giờ lùi về lúc C# sống dậy —
    /// <see cref="StartupClock"/>), <c>first_frame</c>, <c>config_ready</c>; game báo các trạm của game; Ump báo
    /// <c>consent_shown</c>. Sổ <see cref="LoadStepLedger"/> giữ luật một lần mỗi lần cài.
    /// </summary>
    public class LoadStepLogService : MySingleton<LoadStepLogService>, IInit, ITerminal
    {
        private readonly LogScheduleService _logScheduleService;
        private readonly IFPlayerSessionRepository _sessionRepository;
        private readonly LoadStepLedger _ledger;
        private readonly object _lock = new();
        private bool _wentBackground;
        private bool _initialized;
        private long? _pendingFirstFrameAt;

        public LoadStepLogService(
            LogScheduleService logScheduleService, IDataPool dataPool, IFPlayerSessionRepository sessionRepository)
        {
            _logScheduleService = logScheduleService;
            _sessionRepository = sessionRepository;
            _ledger = new LoadStepLedger(dataPool);
        }

        public Task Init(CancellationToken cancellationToken = default)
        {
            lock (_lock) _ledger.OnLaunch(_sessionRepository.SessionId);

            // Giờ của dòng = lúc C# sống dậy, không phải lúc SDK khởi tạo xong (log "đệm" tới khi hàng đợi sẵn sàng)
            var sinceStart = StartupClock.ElapsedMillis;
            // Lúc dòng mang (C# sống dậy) cách lúc tiến trình bắt đầu bao lâu: (bây giờ − tiến trình) − (bây giờ − C#)
            var sinceProcess = DeviceSignals.MsSinceProcessStart();
            long? processToLoadStart = sinceProcess.HasValue
                ? Math.Max(0, sinceProcess.Value - (sinceStart ?? 0))
                : null;
            Report(LoadKind.App, LoadStep.LoadStart, null, null,
                sinceStart.HasValue ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - sinceStart.Value : null,
                processToLoadStart);

            long? pendingFirstFrame;
            lock (_lock)
            {
                _initialized = true;
                pendingFirstFrame = _pendingFirstFrameAt;
                _pendingFirstFrameAt = null;
            }

            if (pendingFirstFrame.HasValue) Report(LoadKind.App, LoadStep.FirstFrame, null, null, pendingFirstFrame);

            // Giới hạn đã biết: remote config lấy xong TRƯỚC lúc này (hiếm — fetch mạng thường lâu hơn khởi tạo SDK) thì
            // không có trạm config_ready.
            FConfig.OnUpdateFromNet += OnConfigReady;
            return Task.CompletedTask;
        }

        public void OnPostStop()
        {
            lock (_lock) _wentBackground = true;
        }

        /// <summary>
        /// Khung hình đầu tiên — <c>PerfSampleService</c> gọi ở Update đầu. Update đầu có thể chạy TRƯỚC <see cref="Init"/>
        /// (sổ chưa biết có phải cài mới): giữ mốc giờ lại, <see cref="Init"/> gửi sau với đúng giờ đó.
        /// </summary>
        public void FirstFrame()
        {
            var at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lock (_lock)
            {
                if (!_initialized)
                {
                    _pendingFirstFrameAt = at;
                    return;
                }
            }

            Report(LoadKind.App, LoadStep.FirstFrame, null, null, at);
        }

        public void Report(LoadKind kind, LoadStep step, long? assetBytes, string errorCode, long? atMillis = null,
            long? msSinceProcessStart = null)
        {
            if (assetBytes.HasValue && step != LoadStep.AssetDownloadDone) assetBytes = null;
            if (assetBytes is < 0) assetBytes = null;
            if (!string.IsNullOrEmpty(errorCode) && step != LoadStep.LoadError) errorCode = null;

            bool? wentBackground;
            lock (_lock)
            {
                if (!_ledger.Accepts(kind, step)) return;
                _ledger.Mark(kind, step);
                wentBackground = kind == LoadKind.App ? _wentBackground : null;
            }

            var log = new FFtueLoadStepLog
            {
                loadKind = kind,
                step = step,
                wentBackground = wentBackground,
                networkType = NetworkTypeExtensions.Current(),
                assetBytes = assetBytes,
                errorCode = string.IsNullOrEmpty(errorCode) ? null : errorCode,
                msSinceProcessStart = step == LoadStep.LoadStart ? msSinceProcessStart : null
            };
            if (atMillis.HasValue) log.clientCreateDate = atMillis.Value;
            _logScheduleService.Enqueue(log);
        }

        private void OnConfigReady()
        {
            FConfig.OnUpdateFromNet -= OnConfigReady;
            Report(LoadKind.App, LoadStep.ConfigReady, null, null);
        }
    }
}
