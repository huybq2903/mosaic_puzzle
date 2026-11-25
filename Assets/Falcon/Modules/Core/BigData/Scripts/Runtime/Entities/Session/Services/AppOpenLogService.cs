/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-07-31
 */

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Falcon.Helpers.Devkit;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Bắn log mốc mở app (hợp đồng §B): Cold lúc khởi tạo, Hot mỗi lần quay lại từ background
    /// nếu nền đủ lâu (ngưỡng lấy từ remote config). Dev game không phải gọi gì — trừ nguồn mở app
    /// (<see cref="ReportOpenSource"/>) vì Unity không nhìn thấy intent/launchOptions.
    /// </summary>
    [NoLazy]
    public class AppOpenLogService : MySingleton<AppOpenLogService>, IInit, IPioneer, ITerminal
    {
        private readonly AppOpenState _state = new();

        // Bên ghi vòng đời (Init / OnPreContinue / OnPostStop) chạy trên main thread, nhưng
        // ReportOpenSource do module push/deeplink gọi — callback notification không đảm bảo cùng
        // thread. Hai bên đụng chung mấy field state nên khoá cho chắc.
        private readonly object _lock = new();
        private readonly ITimeRepository _timeRepository;
        private readonly LogScheduleService _logScheduleService;
        private readonly AnalyticConfigService _configService;
        private readonly FullscreenAdPresence _fullscreenAdPresence;
        private readonly PerfScreenLogService _perfScreenLogService;
        private readonly IDataPool _dataPool;
        private readonly TurnLedgerService _turnLedger;
        private readonly BatteryLogService _batteryLogService;
        private readonly SettingLogService _settingLogService;
        private readonly ConsentAccountService _consentAccountService;
        private readonly DeviceSignalService _deviceSignalService;
        private readonly PermissionAutoReadService _permissionAutoRead;
        private const string GRAPHICS_QUALITY_KEY = "falcon.bigdata.app_open.graphics_quality";

        public AppOpenLogService(
            ITimeRepository timeRepository, LogScheduleService logScheduleService,
            AnalyticConfigService configService, FullscreenAdPresence fullscreenAdPresence,
            PerfScreenLogService perfScreenLogService, IDataPool dataPool, TurnLedgerService turnLedger,
            BatteryLogService batteryLogService, SettingLogService settingLogService,
            ConsentAccountService consentAccountService, DeviceSignalService deviceSignalService,
            PermissionAutoReadService permissionAutoRead)
        {
            _permissionAutoRead = permissionAutoRead;
            _deviceSignalService = deviceSignalService;
            _consentAccountService = consentAccountService;
            _batteryLogService = batteryLogService;
            _settingLogService = settingLogService;
            _turnLedger = turnLedger;
            _perfScreenLogService = perfScreenLogService;
            _dataPool = dataPool;
            _timeRepository = timeRepository;
            _logScheduleService = logScheduleService;
            _configService = configService;
            _fullscreenAdPresence = fullscreenAdPresence;
        }

        public Task Init(CancellationToken cancellationToken = default)
        {
            LogOpen();
            return Task.CompletedTask;
        }

        public void OnPreContinue()
        {
            LogOpen();
        }

        public void OnPostStop()
        {
            // Hỏi NGAY lúc pause: ad toàn màn vừa lên hình thì lần quay lại sau nó là đóng ad, không
            // phải người chơi quay lại app.
            var byAd = _fullscreenAdPresence.IsAdOnset();
            lock (_lock) _state.MarkPause(_timeRepository.CurrentTimeMillis, byAd);
        }

        /// <summary>
        /// Module push/deeplink báo lần mở app này đến từ đâu (§B4) — Unity không đọc được
        /// intent/launchOptions nên SDK-core không tự biết. Phải báo TRƯỚC khi bản tin app_open
        /// bắn: lần Cold thì bản tin bắn ngay trong pha init, nên chỗ gọi đúng là
        /// <c>RuntimeInitializeOnLoadMethod</c> hoặc <c>IPioneer</c> của module đó.
        /// </summary>
        public void ReportOpenSource(
            OpenSource source, string pushCampaignId = null,
            Dictionary<string, object> extraMeta = null)
        {
            bool late;
            lock (_lock)
                late = _state.ReportSource(source, pushCampaignId, _timeRepository.CurrentTimeMillis, extraMeta);
            if (late)
                AnalyticLogger.Instance.Warning(
                    $"ReportOpenSource({source}) gọi SAU khi bản tin app_open đã bắn — lần mở này " +
                    "không mang được nguồn (bản tin đã gửi thì không sửa được). Hãy gọi sớm hơn: " +
                    "RuntimeInitializeOnLoadMethod cho ca Cold, ngay trong callback nhận notification cho ca Hot.");
        }

        /// <summary>Game khai mức đồ hoạ đang chọn (hợp đồng đợt 3 B1(c)) — null để gỡ (game bỏ tính năng chỉnh đồ hoạ).</summary>
        public void SetGraphicsQuality(string quality)
        {
            _dataPool.Compute<string>(GRAPHICS_QUALITY_KEY, _ => string.IsNullOrEmpty(quality) ? null : quality);
        }

        /// <summary>Main thread (Init / OnPreContinue) — đọc được API Unity.</summary>
        private void ApplyDisplay(AppOpenParam param)
        {
            param.perfTargetFps = UnityEngine.Application.targetFrameRate;
            _perfScreenLogService.NoteTargetFpsAtOpen(param.perfTargetFps.Value);
            var hz = (float)UnityEngine.Screen.currentResolution.refreshRateRatio.value;
            if (hz > 0 && !float.IsNaN(hz) && !float.IsInfinity(hz)) param.displayRefreshHz = (float)System.Math.Round(hz, 1);
            param.graphicsQuality = _dataPool.GetOrDefault<string>(GRAPHICS_QUALITY_KEY, null);

            var battery = _batteryLogService.ReadNow();
            param.batteryPct = battery.Pct;
            param.batteryCharging = battery.Charging;
            param.batteryPlug = battery.Plug;
            param.batteryTempC = battery.TempC;

            // Đợt 2 (native, Devkit): vắng ở Editor / nền tảng không có
            var audio = _deviceSignalService.ReadAudioNow();
            param.deviceMediaVolumePct = audio?.VolumePct;
            param.deviceRingerMode = audio?.Ringer;
            param.audioOutput = audio?.Output;
            var display = _deviceSignalService.Enabled ? DeviceSignals.Display() : null;
            param.deviceFontScale = display?.FontScale;
            param.deviceBoldText = display?.BoldText;
            param.deviceReduceMotion = display?.ReduceMotion;
            param.deviceStorageFreeBucket = display?.StorageFreeBucket;
            // iOS: quyền thông báo đọc bất đồng bộ — lần mở đầu có thể vắng (không đoán)
            if (_deviceSignalService.Enabled) (param.permPushStatus, param.permAttStatus) = _permissionAutoRead.ReadNow();

            var settings = _settingLogService.Snapshot();
            param.settingSound = settings.CurrentSound;
            param.settingMusic = settings.CurrentMusic;
            param.settingHaptic = settings.CurrentHaptic;
            param.settingIsDefault = settings.IsDefault();
            param.settingLanguage = settings.language;
            param.linkedProviders = _consentAccountService.LinkedProvidersWire();

            var caps = _turnLedger.Caps;
            if (caps == null) return;
            param.capBusy = caps.busy;
            param.capActions = caps.actions;
            param.capIneffectiveTap = caps.ineffectiveTap;
            param.capSpeed = caps.speed;
            param.capScreen = caps.screen;
        }

        private void LogOpen()
        {
            AppOpenParam param;
            // Số đo khởi động chỉ Cold mới dùng (Hot là quay lại foreground, không có khởi động nào để
            // đo); state quyết có gắn hay không — vắng nếu app từng xuống nền trước lần Cold.
            lock (_lock)
                param = _state.TryOpen(_timeRepository.CurrentTimeMillis, _configService.AppOpenMinBackgroundSec,
                    StartupClock.ElapsedMillis);
            if (param == null) return;
            param.networkType = NetworkTypeExtensions.Current();
            ApplyDisplay(param);

            // Gọi được ngay cả trong Init: IInit chạy theo DependencyOrder mà service này phụ thuộc
            // LogScheduleService → LogDecorService → BaseLogDecorService → FCentralUserParamService
            // → các player repository, nên chúng đã init xong trước. (Central user params được chốt
            // vào DataWrapper ngay lúc Enqueue, không phải lúc flush — nên thứ tự này mới quan trọng.)
            _logScheduleService.Enqueue(new FAppOpenLog(param));
        }
    }
}
