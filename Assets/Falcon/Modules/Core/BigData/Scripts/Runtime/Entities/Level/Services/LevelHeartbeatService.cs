/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Falcon.Helpers.Devkit;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// SDK tự bắn heartbeat của lượt đang mở (hợp đồng đợt 3 B5, ký 23/11 — "khi nào ghi log là việc của SDK, game
    /// chỉ khai tham số"). Ba lúc bắn:
    /// <list type="number">
    /// <item>app <b>xuống nền</b> khi có lượt mở — qua <see cref="IAppPauseLogGenerator"/>, chắc lên đĩa;</item>
    /// <item><b><c>Level.OnPauseExit()</c></b> — bắn RỒI mới đóng đoạn, nên lần xuống nền sau không bắn trùng;</item>
    /// <item>app <b>sắp chết</b> — đường khẩn của <c>f_sdk_app_issue</c>, chép từ bản chụp
    /// (<see cref="EmergencySnapshotService"/>; lớp này chỉ cấp tham số qua <see cref="BuildForSnapshot"/>).</item>
    /// </list>
    /// Không có heartbeat định kỳ. Tham số: bản game đẩy qua <c>Level.OnHeartbeat</c> + các
    /// <see cref="ILevelTurnParamsProvider"/> (APS) gọi tại lúc bắn — xem <see cref="LevelTurnParamsState"/>. Giữa lượt,
    /// APS được gọi khoảng mỗi 10 giây để bản chụp có tham số mới (<see cref="TryRefresh"/>, §5.3).
    /// </summary>
    public class LevelHeartbeatService : MySingleton<LevelHeartbeatService>, IAppPauseLogGenerator
    {
        private readonly LevelTurnService _turnService;
        private readonly LogScheduleService _logScheduleService;
        private readonly ITimeRepository _timeRepository;
        private readonly LevelTurnParamsState _state = new();
        private readonly object _lock = new();
        private readonly ApsRefreshState _refresh = new();
        private IReadOnlyList<ITurnParamsFiller> _fillers = Array.Empty<ITurnParamsFiller>();
        private int _version;

        public LevelHeartbeatService(
            LevelTurnService turnService, LogScheduleService logScheduleService, ITimeRepository timeRepository)
        {
            _turnService = turnService;
            _logScheduleService = logScheduleService;
            _timeRepository = timeRepository;
        }

        /// <summary>Nối các nguồn tham số (APS) — <see cref="LevelTurnParamsBinder"/> gọi lúc khởi động.</summary>
        public void SetFillers(IEnumerable<ITurnParamsFiller> fillers)
        {
            var list = new List<ITurnParamsFiller>();
            if (fillers != null)
                foreach (var filler in fillers)
                    if (filler != null) list.Add(filler);
            lock (_lock) _fillers = list;
        }

        /// <summary>
        /// Game cập nhật tham số của lượt đang mở (<c>Level.OnHeartbeat</c>) — KHÔNG sinh dòng. Không có lượt mở thì
        /// cảnh báo và bỏ.
        /// </summary>
        public void UpdateParams(LevelHeartBeatParamV2 param)
        {
            if (param == null) return;
            bool accepted;
            lock (_lock) accepted = _state.Push(param, _turnService.OpenPlayTurnId, _timeRepository.CurrentTimeMillis);
            if (accepted) Interlocked.Increment(ref _version);
            if (!accepted)
                AnalyticLogger.Instance.Warning(
                    "Level.OnHeartbeat lúc không có lượt chơi nào đang mở — bỏ qua. Từ hợp đồng đợt 3 B5, heartbeat là " +
                    "CẬP NHẬT tham số của lượt đang mở; SDK tự bắn dòng lúc xuống nền / OnPauseExit.");
        }

        /// <summary>Tăng mỗi lần tham số lượt đổi (game đẩy / APS làm mới) — bản chụp khẩn dựng lại khi số này đổi.</summary>
        public int Version => Volatile.Read(ref _version);

        /// <summary><c>aps_refresh_disabled</c> của <c>f_sdk_session_data</c> (vắng nếu không ngừng / không dùng APS).</summary>
        public bool? ApsRefreshDisabled
        {
            get
            {
                lock (_lock) return _refresh.Disabled;
            }
        }

        /// <summary><c>aps_refresh_max_ms</c> của <c>f_sdk_session_data</c> (vắng nếu chưa làm mới lần nào).</summary>
        public int? ApsRefreshMaxMs
        {
            get
            {
                lock (_lock) return _refresh.MaxMs;
            }
        }

        /// <summary>
        /// Tham số của lượt đang mở cho bản chụp khẩn — <b>không</b> gọi nguồn tham số (APS): bản chụp dựng thường xuyên,
        /// gọi vào game ở đây là đúng thứ §5.3 muốn giới hạn. Null nếu không có lượt mở.
        /// </summary>
        public LevelHeartBeatParamV2 BuildForSnapshot()
        {
            var openTurnId = _turnService.OpenPlayTurnId;
            if (openTurnId == null) return null;
            lock (_lock) return _state.Build(openTurnId, null, _timeRepository.CurrentTimeMillis);
        }

        /// <summary>
        /// Main thread, mỗi khung: tới nhịp thì gọi APS làm mới tham số (§5.3). <paramref name="eligible"/> = app ở
        /// trước, không có QC toàn màn (caller biết). Bấm giờ từng lời gọi; quá 16 ms thì ngừng tới hết phiên.
        /// </summary>
        /// <returns>true nếu vừa làm mới (bản chụp nên dựng lại).</returns>
        public bool TryRefresh(bool eligible)
        {
            IReadOnlyList<ITurnParamsFiller> fillers;
            lock (_lock) fillers = _fillers;
            var openTurnId = _turnService.OpenPlayTurnId;
            var now = MonotonicClock.ElapsedMillis;
            lock (_lock)
                if (!_refresh.Due(now, eligible && fillers.Count > 0 && openTurnId != null))
                    return false;

            var timed = new List<TimedFiller>(fillers.Count);
            foreach (var filler in fillers) timed.Add(new TimedFiller(new GuardedFiller(filler)));
            lock (_lock)
            {
                _state.Refresh(openTurnId, timed, _timeRepository.CurrentTimeMillis);
                var slowest = 0.0;
                foreach (var filler in timed) slowest = Math.Max(slowest, filler.ElapsedMs);
                _refresh.Record(now, slowest);
                if (_refresh.Disabled == true)
                    AnalyticLogger.Instance.Warning(
                        $"Nguồn tham số heartbeat (APS) chạy {slowest:0.#} ms > {ApsRefreshState.MAX_CALL_MS} ms — ngừng làm " +
                        "mới giữa lượt tới hết phiên (lúc xuống nền vẫn gọi). Property APS phải rẻ, không side effect.");
            }

            Interlocked.Increment(ref _version);
            return true;
        }

        /// <summary>Bắn heartbeat ngay (dùng cho <c>Level.OnPauseExit</c>, gọi TRƯỚC khi đóng đoạn).</summary>
        public void FireNow()
        {
            var param = Build();
            if (param != null) _logScheduleService.Enqueue(new FLevelLog(param));
        }

        public IEnumerable<IDataLog> GetLogsOnAppPause()
        {
            var param = Build();
            if (param != null) yield return new FLevelLog(param);
        }

        private LevelHeartBeatParamV2 Build()
        {
            var openTurnId = _turnService.OpenPlayTurnId;
            if (openTurnId == null) return null;

            IReadOnlyList<ITurnParamsFiller> fillers;
            lock (_lock) fillers = _fillers;

            // Một nguồn hỏng không được giết cả batch log lúc xuống nền (án totalScore = 0 của APS 1.1.4).
            var safeFillers = new List<ITurnParamsFiller>(fillers.Count);
            foreach (var filler in fillers) safeFillers.Add(new GuardedFiller(filler));

            lock (_lock) return _state.Build(openTurnId, safeFillers, _timeRepository.CurrentTimeMillis);
        }

        private sealed class TimedFiller : ITurnParamsFiller
        {
            private readonly ITurnParamsFiller _inner;

            public TimedFiller(ITurnParamsFiller inner) => _inner = inner;

            public double ElapsedMs { get; private set; }

            public bool FillTurnParams(LevelHeartBeatParamV2 param)
            {
                var watch = Stopwatch.StartNew();
                try
                {
                    return _inner.FillTurnParams(param);
                }
                finally
                {
                    ElapsedMs = watch.Elapsed.TotalMilliseconds;
                }
            }
        }

        private sealed class GuardedFiller : ITurnParamsFiller
        {
            private readonly ITurnParamsFiller _inner;

            public GuardedFiller(ITurnParamsFiller inner) => _inner = inner;

            public bool FillTurnParams(LevelHeartBeatParamV2 param)
            {
                try
                {
                    return _inner.FillTurnParams(param);
                }
                catch (Exception e)
                {
                    AnalyticLogger.Instance.Warning(
                        $"{_inner.GetType().Name}.FillTurnParams ném lỗi — heartbeat vẫn bắn, thiếu tham số của nguồn này: {e}");
                    return false;
                }
            }
        }
    }

    /// <summary>
    /// Nối mọi <see cref="ILevelTurnParamsProvider"/> (APS) vào <see cref="LevelHeartbeatService"/> lúc khởi động.
    /// Tách thành lớp riêng có chủ đích: controller phụ thuộc <see cref="LevelHeartbeatService"/>, còn nguồn tham số
    /// (lớp game kế thừa APS) phụ thuộc Level Core / game — để controller phụ thuộc thẳng danh sách nguồn là mở cửa
    /// cho vòng phụ thuộc. Lớp này là lá, không ai phụ thuộc nó; <c>[NoLazy]</c> để chắc được tạo.
    /// </summary>
    [NoLazy]
    public class LevelTurnParamsBinder : MySingleton<LevelTurnParamsBinder>
    {
        public LevelTurnParamsBinder(LevelHeartbeatService heartbeatService, List<ILevelTurnParamsProvider> providers)
        {
            heartbeatService.SetFillers(providers);
        }
    }
}
