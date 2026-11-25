/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-10-26
 */

using Falcon.Helpers.Devkit;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Vai giữ-state cho <see cref="AdSessionCounterState"/> — cùng khuôn (và cùng lý do) với
    /// <see cref="AdViewCache"/>: bên ghi là callback mediation, bên đọc là decorator lúc gửi, và
    /// class này ZERO đường tới <c>LogScheduleService</c> nên decorator ctor-inject được.
    /// </summary>
    public class AdSessionCounterCache : MySingleton<AdSessionCounterCache>
    {
        private readonly AdSessionCounterState _state = new();
        private readonly object _lock = new();
        private readonly IFPlayerSessionService _sessionService;

        public AdSessionCounterCache(IFPlayerSessionService sessionService)
        {
            _sessionService = sessionService;
        }

        /// <summary>Lần XIN AD (đợt load) thứ mấy của format này trong phiên — đếm từ 1.</summary>
        public int NextRequestIndex(AdType type)
        {
            lock (_lock) return _state.NextRequest(type, _sessionService.SessionId);
        }

        /// <summary>Lần GỌI LOAD thứ mấy của format này trong phiên (kể cả fail) — đếm từ 1.</summary>
        public int NextLoadIndex(AdType type)
        {
            lock (_lock) return _state.NextLoad(type, _sessionService.SessionId);
        }

        /// <summary>Lần LOAD THÀNH CÔNG thứ mấy của format này trong phiên — đếm từ 1.</summary>
        public int NextLoadSuccessIndex(AdType type)
        {
            lock (_lock) return _state.NextLoadSuccess(type, _sessionService.SessionId);
        }

        /// <summary>Lần HIỂN THỊ thứ mấy của format này trong phiên — đếm từ 1.</summary>
        public int NextImpressionIndex(AdType type)
        {
            lock (_lock) return _state.NextImpression(type, _sessionService.SessionId);
        }

        /// <inheritdoc cref="AdSessionCounterState.RecordImpressionRevenue"/>
        public void RecordImpressionRevenue(double adRev)
        {
            lock (_lock) _state.RecordImpressionRevenue(adRev, _sessionService.SessionId);
        }

        /// <summary>
        /// Ba số của PHIÊN mà dòng load cần: eCPM lượt chiếu đầu, eCPM lượt chiếu ngay trước, và số
        /// lượt đã chiếu. Đọc MỘT lần trong cùng một khoá để ba cột trên cùng dòng log là ảnh của
        /// cùng một thời điểm.
        /// </summary>
        public (double? first, double? previous, int shown) ImpressionSnapshot(AdType type)
        {
            lock (_lock)
            {
                var session = _sessionService.SessionId;
                return (_state.FirstAdEcpm(session), _state.PreviousAdEcpm(session),
                    _state.ImpressionsSoFar(type, session));
            }
        }
    }
}
