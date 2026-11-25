/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Falcon.Helpers.Devkit;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Scripting;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>Dấu hiệu app gặp sự cố — <c>f_sdk_app_issue</c> (hợp đồng đợt 3 S2, ký 23/11). Không gửi stack trace.</summary>
    [Serializable]
    public class FAppIssueLog : AFalconLog, IPerfScreenCarrier
    {
        public AppIssueKind kind;

        [FKey(RemoveIfNull = true)] public string exceptionType;
        [FKey(RemoveIfNull = true)] public string exceptionTopFrame;
        [FKey(RemoveIfNull = true)] public string exceptionHash;
        [FKey(RemoveIfNull = true)] public bool? isFatal;
        [FKey(RemoveIfNull = true)] public int? freezeMs;
        [FKey(RemoveIfNull = true)] public bool? isLoading;
        [FKey(RemoveIfNull = true)] public bool? heartbeatSkipped;
        [FKey(RemoveIfNull = true)] public int? suppressedCount;
        [FKey(RemoveIfNull = true)] public MemorySignalSource? source;

        [FKey(RemoveIfNull = true)] public PrevEndType? prevEndType;
        [FKey(RemoveIfNull = true)] public string prevExitReason;
        [FKey(RemoveIfNull = true)] public string prevSessionUid;
        [FKey(RemoveIfNull = true)] public string prevPlayTurnId;
        [FKey(RemoveIfNull = true)] public long? prevLastAliveAt;
        [FKey(RemoveIfNull = true)] public bool? prevFreezeLong;
        [FKey(RemoveIfNull = true)] public string prevFatalExceptionType;
        [FKey(RemoveIfNull = true)] public string prevFatalExceptionTopFrame;
        [FKey(RemoveIfNull = true)] public string prevFatalExceptionHash;

        [Preserve]
        public FAppIssueLog()
        {
        }

        public override string Event => "f_sdk_app_issue";
    }

    /// <summary>
    /// <c>f_sdk_app_issue</c> phần C# (hợp đồng đợt 3 S2): <c>exception</c> (kể cả <c>is_fatal</c>), <c>freeze</c>,
    /// <c>freeze_long</c>, <c>memory_critical</c>, <c>prev_exit</c> loại <c>abnormal</c>. Phần native (lý do thoát của OS,
    /// nguồn <c>activity_manager</c>) là đợt 2.
    /// <list type="bullet">
    /// <item>Lỗi C# đến từ <c>Application.logMessageReceivedThreaded</c> — MỌI luồng. Callback chỉ xếp hàng (có trần, chặn
    /// đệ quy); dựng log ở main thread (decorator đụng API Unity).</item>
    /// <item><c>freeze</c>: main thread thấy một khung ≥ 2 giây lúc app ở trước (không tính khung đầu sau resume / lấy lại
    /// focus — đó là vắng mặt).</item>
    /// <item><c>freeze_long</c>: <b>luồng canh</b> (thread C# thường, ngủ 0,5 giây) thấy biến đếm khung đứng tới mốc 5 giây
    /// lúc app ở trước, có focus, không có QC toàn màn ⇒ bắn ngay qua đường khẩn + ghi cờ lên dấu. Khung chạy lại thì gỡ cờ.</item>
    /// <item><c>is_fatal</c>: <c>AppDomain.UnhandledException</c> với <c>IsTerminating</c> ⇒ ghi dấu đồng bộ trước
    /// (<c>prev_fatal_*</c> ở lần mở sau), rồi mới gửi khẩn.</item>
    /// <item>Dấu vòng đời: file nhỏ riêng (không qua pool chung), ghi mỗi ~15 giây, lúc xuống nền / quay lại / thoát, và lúc
    /// sự cố. Hai luồng có thể cùng ghi ⇒ ghi file tạm riêng rồi thay.</item>
    /// </list>
    /// <b>Luồng canh và handler lỗi chết không khoá chung với main thread</b> (main thread có thể đang đứng giữa lúc giữ
    /// khoá): mọi thứ chúng cần được main thread phản chiếu sẵn vào field <c>volatile</c> hoặc nằm trong bản chụp.
    /// <br/>Không chạy trong Editor: breakpoint / dừng Play sinh đơ và "văng" giả.
    /// </summary>
    public class AppIssueLogService : MySingleton<AppIssueLogService>, IInit, ITerminal, IPioneer
    {
        public const float FREEZE_SEC = 2f;
        private const float MARK_EVERY_SEC = 15f;
        private const int MAX_PENDING = 32;
        private const int WATCH_SLEEP_MS = 500;
        private const string MARK_FILE = "falcon_bigdata_lifecycle.json";
        private const string LAST_EXIT_REPORTED_KEY = "falcon.bigdata.app_issue.last_exit_reported_ms";
        private const int EXIT_RECORDS_MAX = 16;
        private const float SYSTEM_MEMORY_POLL_SEC = 5f;
        private const long IOS_DIAGNOSTIC_WAIT_MS = 10_000;
        private const string PENDING_SUFFIX = ".pending";
        private static readonly TimeSpan kFatalWait = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan kFreezeLongWait = TimeSpan.FromSeconds(5);

        private readonly LogScheduleService _logScheduleService;
        private readonly IFPlayerSessionService _sessionService;
        private readonly LevelTurnService _turnService;
        private readonly PerfPackRepository _perfPackRepository;
        private readonly EmergencySnapshotService _snapshotService;
        private readonly EmergencySendService _emergencySendService;
        private readonly ITimeRepository _timeRepository;
        private readonly AnalyticConfigService _configService;
        private readonly IDataPool _dataPool;
        private readonly AppIssueRateLimiter _limiter = new(); // main thread
        private readonly AppIssueRateLimiter _watchLimiter = new(); // luồng canh
        private readonly FreezeWatchState _watch = new(); // luồng canh
        private readonly ConcurrentQueue<(string condition, string stack)> _pending = new();
        private readonly object _markLock = new();

        [ThreadStatic] private static bool tInCallback;

        private bool _enabled;
        private string _markPath;
        private LifecycleMark _mark;
        private bool _skipNextFrame = true;
        private float _sinceMarkSec;

        // ---- Phản chiếu cho luồng canh / handler lỗi chết (main thread ghi) ----
        private long _frameCount;
        private volatile bool _background;
        private volatile bool _unfocused;
        private volatile bool _adShowing;
        private volatile string _openTurnId;
        private volatile bool _freezeLongMarked;
        private volatile bool _emergencyOn;
        private bool _watchStarted;
        private bool _javaHandlerTried;
        private float _sinceMemoryPollSec;
        private bool _systemLowMemory;
        private LifecycleMark _pendingPrevious; // iOS: chờ báo cáo MetricKit
        private long _pendingPreviousUntil;
        private string _emergencyHeartbeatTurn;
        private int _fatalHandled;

        public AppIssueLogService(
            LogScheduleService logScheduleService, IFPlayerSessionService sessionService, LevelTurnService turnService,
            PerfPackRepository perfPackRepository, EmergencySnapshotService snapshotService,
            EmergencySendService emergencySendService, ITimeRepository timeRepository,
            AnalyticConfigService configService, IDataPool dataPool)
        {
            _configService = configService;
            _dataPool = dataPool;
            _logScheduleService = logScheduleService;
            _sessionService = sessionService;
            _turnService = turnService;
            _perfPackRepository = perfPackRepository;
            _snapshotService = snapshotService;
            _emergencySendService = emergencySendService;
            _timeRepository = timeRepository;
        }

        public Task Init(CancellationToken cancellationToken = default)
        {
            _enabled = !Application.isEditor;
            if (!_enabled) return Task.CompletedTask;

            _markPath = Path.Combine(Application.persistentDataPath, MARK_FILE);
            var previous = ReadMark();
            // Dấu đang chờ của lần chạy TRƯỚC NỮA (app chết trong lúc chờ báo cáo MetricKit) — quyết ngay, không chờ nữa
            var stalePending = ReadMark(_markPath + PENDING_SUFFIX);
            if (stalePending != null)
            {
                DecidePrevExit(stalePending);
                DeletePending();
            }

            _mark = new LifecycleMark { sessionUid = _sessionService.SessionUid, pid = DeviceSignals.ProcessId() ?? 0 };
            _openTurnId = _turnService.OpenPlayTurnId;
            WriteMark();
            if (DeviceSignals.DiagnosticsAsync && previous != null)
            {
                // MetricKit giao báo cáo của lần chạy trước bất đồng bộ sau khi mở app — chờ một lúc rồi mới quyết. Dấu cũ
                // đã bị dấu mới đè ⇒ cất riêng: app chết trong lúc chờ thì lần mở sau vẫn quyết được.
                _pendingPrevious = previous;
                _pendingPreviousUntil = MonotonicClock.ElapsedMillis + IOS_DIAGNOSTIC_WAIT_MS;
                WriteFile(_markPath + PENDING_SUFFIX, JsonConvert.SerializeObject(previous));
            }
            else DecidePrevExit(previous);

            Application.logMessageReceivedThreaded += OnLogMessage;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            return Task.CompletedTask;
        }

        public void OnPostStop()
        {
            if (!_enabled) return;
            _background = true;
            FlushPendingPrevious(); // iOS: xuống nền trước khi hết giờ chờ — quyết luôn với những gì đã có
            lock (_markLock)
            {
                _mark.backgrounded = true;
                _mark.backgroundAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }

            WriteMark();
        }

        public void OnPreContinue()
        {
            if (!_enabled) return;
            _skipNextFrame = true;
            _background = false;
            lock (_markLock)
            {
                _mark.backgrounded = false;
                _mark.backgroundAt = 0;
            }

            WriteMark();
        }

        /// <summary><c>OnApplicationFocus</c> — hộp thoại hệ thống (xin quyền, ATT, cuộc gọi) có thể làm khung đứng mà không pause.</summary>
        public void OnFocusChanged(bool hasFocus)
        {
            if (!_enabled) return;
            _unfocused = !hasFocus;
            if (hasFocus) _skipNextFrame = true;
        }

        /// <summary>App thoát sạch (<c>OnApplicationQuit</c> — hiếm trên mobile).</summary>
        public void MarkClean()
        {
            if (!_enabled) return;
            lock (_markLock) _mark.clean = true;
            WriteMark();
        }

        /// <summary>Main thread, mỗi khung (<c>PerfSampleService.Update</c>).</summary>
        public void Tick(float unscaledDeltaSec)
        {
            if (!_enabled) return;

            Interlocked.Increment(ref _frameCount);
            var screen = _perfPackRepository.CurrentScreen;
            var openTurnId = _turnService.OpenPlayTurnId;
            _adShowing = screen == SurfaceState.AD;
            _openTurnId = openTurnId;

            // Van S2 phần 2 (mặc định tắt, bật thử máy test trước): luồng canh, bản chụp + đường khẩn, làm mới APS
            var emergencyOn = _configService.AppIssueEmergencyEnabled;
            _emergencyOn = emergencyOn;
            if (emergencyOn)
            {
                if (!_watchStarted)
                {
                    _watchStarted = true;
                    new Thread(WatchLoop) { IsBackground = true, Name = "FalconBigData.FreezeWatch" }.Start();
                }

                // Ngoại lệ Java không ai bắt (Android, đợt 2) — cùng van; handler cũ (Crashlytics) luôn được gọi tiếp
                if (!_javaHandlerTried)
                {
                    _javaHandlerTried = true;
                    DeviceSignals.InstallJavaUncaughtHandler(OnJavaFatal);
                }

                _snapshotService.Tick(openTurnId, screen);
            }

            if (_skipNextFrame) _skipNextFrame = false;
            else if (unscaledDeltaSec >= FREEZE_SEC) SendFreeze(unscaledDeltaSec);

            // Khung chạy lại sau freeze_long: phiên này "chạy lại" ⇒ gỡ cờ trên dấu (prev_freeze_long = đơ tới chết)
            if (_freezeLongMarked)
            {
                _freezeLongMarked = false;
                lock (_markLock) _mark.freezeLong = false;
                WriteMark();
            }

            while (_pending.TryDequeue(out var item)) SendException(item.condition, item.stack);

            if (_pendingPrevious != null &&
                (DeviceSignals.LatestIosDiagnostic().HasValue || MonotonicClock.ElapsedMillis >= _pendingPreviousUntil))
                FlushPendingPrevious();

            _sinceMemoryPollSec += unscaledDeltaSec;
            if (_sinceMemoryPollSec >= SYSTEM_MEMORY_POLL_SEC)
            {
                _sinceMemoryPollSec = 0;
                PollSystemMemory();
            }

            _sinceMarkSec += unscaledDeltaSec;
            if (_sinceMarkSec < MARK_EVERY_SEC) return;
            _sinceMarkSec = 0;
            WriteMark();
        }

        /// <summary><c>Application.lowMemory</c> (main thread).</summary>
        public void OnLowMemory()
        {
            if (!_enabled) return;
            SendMemoryCritical(MemorySignalSource.UnityLowMemory);
        }

        /// <summary>Android <c>ActivityManager.MemoryInfo.lowMemory</c> mỗi ~5 giây (đợt 2): bắn khi chuyển sang true.</summary>
        private void PollSystemMemory()
        {
            if (!_configService.DeviceSignalsEnabled) return;
            var low = DeviceSignals.SystemLowMemory();
            if (!low.HasValue) return;
            var rising = low.Value && !_systemLowMemory;
            _systemLowMemory = low.Value;
            if (rising) SendMemoryCritical(MemorySignalSource.ActivityManager);
        }

        private void SendMemoryCritical(MemorySignalSource source)
        {
            // Khoá chống ngập theo kind + nguồn: một nguồn không được chặn nguồn kia (so qua mốc đợt 2 cần cả hai)
            var (allowed, suppressed) =
                _limiter.TryPass(nameof(AppIssueKind.MemoryCritical) + ":" + source, MonotonicClock.ElapsedMillis);
            if (!allowed) return;
            _logScheduleService.Enqueue(new FAppIssueLog
            {
                kind = AppIssueKind.MemoryCritical, source = source, suppressedCount = suppressed
            });
        }

        private void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception || tInCallback) return;
            tInCallback = true;
            try
            {
                if (_pending.Count < MAX_PENDING) _pending.Enqueue((condition, stackTrace));
            }
            finally
            {
                tInCallback = false;
            }
        }

        private void SendException(string condition, string stackTrace)
        {
            var (exceptionType, topFrame, hash) = ExceptionFingerprint.From(condition, stackTrace);
            var (allowed, suppressed) =
                _limiter.TryPass(nameof(AppIssueKind.Exception) + ":" + hash, MonotonicClock.ElapsedMillis);
            if (!allowed) return;
            _logScheduleService.Enqueue(new FAppIssueLog
            {
                kind = AppIssueKind.Exception,
                exceptionType = exceptionType,
                exceptionTopFrame = topFrame,
                exceptionHash = hash,
                suppressedCount = suppressed
            });
        }

        private void SendFreeze(float seconds)
        {
            var (allowed, suppressed) = _limiter.TryPass(nameof(AppIssueKind.Freeze), MonotonicClock.ElapsedMillis);
            if (!allowed) return;
            var screen = _perfPackRepository.CurrentScreen;
            _logScheduleService.Enqueue(new FAppIssueLog
            {
                kind = AppIssueKind.Freeze,
                freezeMs = (int)Math.Round(seconds * 1000),
                isLoading = IsLoading(screen),
                suppressedCount = suppressed
            });
        }

        private void FlushPendingPrevious()
        {
            var previous = _pendingPrevious;
            if (previous == null) return;
            _pendingPrevious = null;
            DecidePrevExit(previous);
            DeletePending();
        }

        private void DeletePending()
        {
            try
            {
                File.Delete(_markPath + PENDING_SUFFIX);
            }
            catch (Exception)
            {
                // còn file thì lần mở sau quyết lại — dòng prev_exit có thể lặp một lần
            }
        }

        /// <summary>
        /// <c>prev_exit</c>: dấu vòng đời (đợt 1) + lý do thoát của OS (đợt 2 — Android 11+ <c>ApplicationExitInfo</c>, iOS
        /// MetricKit). Mỗi mục / báo cáo của OS báo một lần (mốc giờ đã báo lưu ở pool).
        /// </summary>
        private void DecidePrevExit(LifecycleMark previous)
        {
            if (previous == null) return;
            var lastReported = _dataPool.GetOrDefault(LAST_EXIT_REPORTED_KEY, 0L);
            string reason = null;
            long? exitAt = null;

            var exit = PrevExitDecision.Match(DeviceSignals.HistoricalExits(EXIT_RECORDS_MAX), previous, lastReported);
            if (exit.HasValue)
            {
                reason = PrevExitDecision.Reason(exit.Value);
                exitAt = exit.Value.TimestampMs;
            }
            else
            {
                var diagnostic = DeviceSignals.LatestIosDiagnostic();
                reason = PrevExitDecision.IosReason(diagnostic, previous, lastReported);
                if (reason != null) exitAt = diagnostic?.TimestampMs;
            }

            if (exitAt.HasValue) _dataPool.Save(LAST_EXIT_REPORTED_KEY, exitAt.Value);
            SendPrevExit(previous, PrevExitDecision.EndType(previous, reason, exitAt), reason);
        }

        private void SendPrevExit(LifecycleMark previous, PrevEndType? endType, string reason)
        {
            if (endType == null) return;
            _logScheduleService.Enqueue(new FAppIssueLog
            {
                kind = AppIssueKind.PrevExit,
                prevEndType = endType,
                prevExitReason = reason,
                prevSessionUid = previous.sessionUid,
                prevPlayTurnId = string.IsNullOrEmpty(previous.playTurnId) ? null : previous.playTurnId,
                prevLastAliveAt = previous.lastAliveAt > 0 ? previous.lastAliveAt : null,
                prevFreezeLong = previous.freezeLong ? true : null,
                prevFatalExceptionType = previous.HasFatal ? previous.fatalExceptionType : null,
                prevFatalExceptionTopFrame = previous.HasFatal ? previous.fatalExceptionTopFrame : null,
                prevFatalExceptionHash = previous.HasFatal ? previous.fatalExceptionHash : null
            });
        }

        private static bool? IsLoading(string screen) => screen == null ? null : screen == "loading";

        // ---- Luồng canh: freeze_long ----

        private void WatchLoop()
        {
            while (true)
            {
                // Không bao giờ để lọt exception: exception chưa bắt trên thread riêng giết cả tiến trình
                try
                {
                    Thread.Sleep(WATCH_SLEEP_MS);
                    var suspended = !_emergencyOn || _background || _unfocused || _adShowing;
                    if (_watch.Observe(MonotonicClock.ElapsedMillis, Interlocked.Read(ref _frameCount), suspended))
                        FireFreezeLong();
                }
                catch (ThreadAbortException)
                {
                    return;
                }
                catch (Exception)
                {
                    // bỏ lượt quan sát này
                }
            }
        }

        private void FireFreezeLong()
        {
            var openTurnId = _openTurnId;
            WriteMarkFromAnyThread(mark => mark.freezeLong = true, openTurnId);
            // Đặt cờ SAU khi ghi dấu: main thread hồi đúng lúc giữa hai bước thì vẫn thấy cờ và gỡ lại trên dấu
            _freezeLongMarked = true;

            var (allowed, suppressed) = _watchLimiter.TryPass(nameof(AppIssueKind.FreezeLong), MonotonicClock.ElapsedMillis);
            if (!allowed) return;
            var snapshot = _snapshotService.Current;
            if (snapshot == null) return;
            DispatchEmergency(snapshot, openTurnId, new Dictionary<string, object>
            {
                ["kind"] = AppIssueKind.FreezeLong,
                ["isLoading"] = IsLoading(snapshot.Screen),
                ["suppressedCount"] = suppressed
            }, kFreezeLongWait);
        }

        // ---- Lỗi C# làm chết tiến trình ----

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
        {
            if (!args.IsTerminating) return;
            var exception = args.ExceptionObject as Exception;
            HandleFatal(exception?.GetType().Name ?? args.ExceptionObject?.GetType().Name, exception?.StackTrace);
        }

        /// <summary>Ngoại lệ Java không ai bắt (Android) — luồng Java đang chết; handler cũ được Devkit gọi tiếp sau đây.</summary>
        private void OnJavaFatal(string type, string frames) => HandleFatal(type, frames);

        /// <summary>Lỗi làm chết tiến trình (C# <c>IsTerminating</c> / Java): ghi dấu trước, rồi gửi khẩn. Một lần mỗi tiến trình.</summary>
        private void HandleFatal(string type, string stack)
        {
            if (Interlocked.Exchange(ref _fatalHandled, 1) == 1) return;
            try
            {
                var (exceptionType, topFrame, hash) = ExceptionFingerprint.From(type, stack);
                var openTurnId = _openTurnId;

                // (1) ghi dấu đồng bộ TRƯỚC — gửi hỏng / không kịp thì lần mở sau prev_exit mang lại
                WriteMarkFromAnyThread(mark =>
                {
                    mark.fatalExceptionType = exceptionType;
                    mark.fatalExceptionTopFrame = topFrame;
                    mark.fatalExceptionHash = hash;
                }, openTurnId);

                // (2) rồi mới thử gửi khẩn (van tắt thì chỉ còn dấu)
                if (!_emergencyOn) return;
                var snapshot = _snapshotService.Current;
                if (snapshot == null) return;
                DispatchEmergency(snapshot, openTurnId, new Dictionary<string, object>
                {
                    ["kind"] = AppIssueKind.Exception,
                    ["isFatal"] = true,
                    ["exceptionType"] = exceptionType,
                    ["exceptionTopFrame"] = topFrame,
                    ["exceptionHash"] = hash
                }, kFatalWait);
            }
            catch (Exception)
            {
                // đang chết — không được ném thêm
            }
        }

        private void DispatchEmergency(
            EmergencySnapshot snapshot, string openTurnId, Dictionary<string, object> issueFields, TimeSpan wait)
        {
            var rows = EmergencyRows.Compose(
                snapshot, openTurnId, issueFields, () => ClaimEmergencyHeartbeat(openTurnId), MonotonicClock.ElapsedMillis,
                MyTime.DateToString(_timeRepository.LocalNow()), () => Guid.NewGuid().ToString());
            _emergencySendService.Dispatch(snapshot, rows, wait);
        }

        /// <summary>Heartbeat kèm sự cố tối đa một lần mỗi lượt (B5 §5.2 lúc 3).</summary>
        private bool ClaimEmergencyHeartbeat(string turnId)
        {
            var previous = Volatile.Read(ref _emergencyHeartbeatTurn);
            if (previous == turnId) return false;
            return Interlocked.CompareExchange(ref _emergencyHeartbeatTurn, turnId, previous) == previous;
        }

        // ---- Dấu vòng đời ----

        private LifecycleMark ReadMark() => ReadMark(_markPath);

        private static LifecycleMark ReadMark(string path)
        {
            try
            {
                return File.Exists(path) ? JsonConvert.DeserializeObject<LifecycleMark>(File.ReadAllText(path)) : null;
            }
            catch (Exception)
            {
                return null; // dấu hỏng = không biết, không bịa "abnormal"
            }
        }

        /// <summary>Main thread.</summary>
        private void WriteMark()
        {
            // Đọc lượt mở NGOÀI khoá dấu: khoá dấu cũng được luồng canh / handler lỗi chết xin
            var openTurnId = _turnService.OpenPlayTurnId;
            _openTurnId = openTurnId;
            string json;
            lock (_markLock) json = SerializeMark(openTurnId);
            WriteMarkFile(json);
        }

        /// <summary>
        /// Luồng canh / handler lỗi chết: chỉ XIN khoá dấu có hạn (giữ khoá chỉ là gán vài field + chuỗi hoá, nhưng main
        /// thread có thể đứng đúng lúc đó); không xin được thì vẫn ghi từ một bản chép — dấu sai một nhịp còn hơn không có.
        /// </summary>
        private void WriteMarkFromAnyThread(Action<LifecycleMark> change, string openTurnId)
        {
            if (_mark == null) return;
            string json;
            if (Monitor.TryEnter(_markLock, 100))
                try
                {
                    change(_mark);
                    json = SerializeMark(openTurnId);
                }
                finally
                {
                    Monitor.Exit(_markLock);
                }
            else
            {
                var copy = JsonConvert.DeserializeObject<LifecycleMark>(JsonConvert.SerializeObject(_mark));
                change(copy);
                copy.lastAliveAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                copy.playTurnId = openTurnId;
                json = JsonConvert.SerializeObject(copy);
            }

            WriteMarkFile(json);
        }

        private string SerializeMark(string openTurnId)
        {
            _mark.lastAliveAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _mark.playTurnId = openTurnId;
            return JsonConvert.SerializeObject(_mark);
        }

        private void WriteMarkFile(string json) => WriteFile(_markPath, json);

        private static void WriteFile(string path, string json)
        {
            // Mỗi lần ghi một file tạm riêng rồi thay: hai luồng ghi cùng lúc không làm hỏng file (người sau thắng)
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, json);
                if (!File.Exists(path)) File.Move(temp, path);
                else
                    try
                    {
                        File.Replace(temp, path, null);
                    }
                    catch (Exception)
                    {
                        // File.Replace chưa kiểm trên IL2CPP: hỏng thì chép đè (không nguyên tử, nhưng dấu không được đứng
                        // yên — dấu cũ "đang ở trước" làm mọi lần mở sau thành prev_exit abnormal giả)
                        File.Copy(temp, path, true);
                        File.Delete(temp);
                    }
            }
            catch (Exception)
            {
                // Đĩa đầy / không ghi được: lần sau không có dấu đúng ⇒ không gửi prev_exit (vắng ≠ bịa)
                try
                {
                    if (File.Exists(temp)) File.Delete(temp);
                }
                catch (Exception)
                {
                    // bỏ
                }
            }
        }
    }
}
