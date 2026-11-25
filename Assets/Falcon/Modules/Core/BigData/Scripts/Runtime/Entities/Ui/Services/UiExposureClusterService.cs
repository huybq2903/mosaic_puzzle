/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-09-05
 */

using System.Collections.Generic;
using System.Linq;
using Falcon.Helpers.Devkit;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Van volume cho exposure UI: nhận per-lần-hiện từ <see cref="FUiApi"/>, gộp cụm trong
    /// <see cref="UiExposureState"/>, flush lúc app pause qua <see cref="IAppPauseLogGenerator"/>
    /// — trăm lần hiện thành vài dòng, mỗi dòng một cụm với bốn cột đếm (khuôn cụm banner/multi-floor).
    /// <br/>KHÔNG persist qua kill: telemetry exposure, doanh thu/phễu không đứng trên nó —
    /// mất cụm dở của stretch bị kill cứng là cái giá chấp nhận, đổi lấy zero IO trên đường nóng
    /// (cùng phán quyết với cụm multi-floor). <b>Nhưng từ bản ký 09/11 cái mất ĐƯỢC ĐẾM</b> —
    /// xem <see cref="UiFlushLossMarker"/>, nơi ghi cả hai cái bẫy của đường đo đó.
    /// <br/><see cref="MAX_CLUSTERS"/> là van xả sớm: số cụm bùng nổ ngoài dự kiến
    /// thì flush ngay thay vì ôm RAM tới pause.
    /// </summary>
    public class UiExposureClusterService : MySingleton<UiExposureClusterService>, IAppPauseLogGenerator
    {
        private const int MAX_CLUSTERS = 32;

        private readonly UiExposureState _state = new();
        private readonly object _lock = new();
        private readonly LogScheduleService _logScheduleService;
        private readonly UiFlushLossMarker _marker;

        private readonly TurnLedgerService _turnLedger;

        public UiExposureClusterService(LogScheduleService logScheduleService, IDataPool dataPool,
            TurnLedgerService turnLedger)
        {
            _turnLedger = turnLedger;
            _logScheduleService = logScheduleService;
            _marker = new UiFlushLossMarker(dataPool);
        }

        public void Record(
            string surfaceId, UiTally tally, Dictionary<string, object> extraMeta,
            LiveOpsRef liveOps = null, string trigger = null)
        {
            // Sổ lượt B6: lời mời hồi sinh / booster hiện trong lượt đang mở (n_offer_shown)
            if (tally.impressions > 0 &&
                (surfaceId == FUiSurface.ReviveOffer || surfaceId == FUiSurface.BoosterOffer))
                _turnLedger.OnOfferShown();

            List<UiExposureState.Entry> overflow = null;
            bool lost;
            lock (_lock)
            {
                // Soi dấu cũ TRƯỚC khi đặt dấu mới, không thì dấu vừa đặt bị đọc thành dấu của
                // phiên trước.
                lost = _marker.TakeStaleOnce();

                // Lần ghi bị từ chối (thiếu surface) thì KHÔNG đặt dấu: dấu nói "có cụm chưa
                // xả", mà ở đây chẳng có cụm nào vào kho. Đặt bừa là bắn một dòng flush_lost cho
                // một lượt xả chưa từng tồn tại.
                if (_state.Record(surfaceId, tally, extraMeta, liveOps, trigger))
                {
                    _marker.MarkOpen();
                    if (_state.Count >= MAX_CLUSTERS) overflow = TakeAllAndClearMark();
                }
            }

            // Bắn NGOÀI lock: EnqueueAll chạy qua pipeline nên giữ nó trong lock là mời một thế
            // deadlock vào đường nóng.
            Emit(overflow, lost);
        }

        public IEnumerable<IDataLog> GetLogsOnAppPause()
        {
            List<UiExposureState.Entry> entries;
            bool lost;
            lock (_lock)
            {
                lost = _marker.TakeStaleOnce();
                entries = TakeAllAndClearMark();
            }

            // Đường này là lưới cuối của phép đo: AppPauseLogCollector gọi MỌI generator ở mỗi lần
            // pause, nên dù phiên sau không hiện bề mặt nào thì chậm nhất là pause đầu tiên cũng
            // báo được phần mất của phiên trước.
            if (lost) yield return new FUiFlushLostLog();
            foreach (var entry in entries) yield return LogOf(entry);
        }

        private void Emit(List<UiExposureState.Entry> overflow, bool lost)
        {
            if (lost) _logScheduleService.EnqueueAll(new IDataLog[] { new FUiFlushLostLog() });
            if (overflow != null) _logScheduleService.EnqueueAll(overflow.Select(LogOf));
        }

        private List<UiExposureState.Entry> TakeAllAndClearMark()
        {
            var entries = _state.TakeAll();
            _marker.ClearMark();
            return entries;
        }

        private static IDataLog LogOf(UiExposureState.Entry entry)
        {
            return new FUiExposureLog(entry);
        }
    }
}
