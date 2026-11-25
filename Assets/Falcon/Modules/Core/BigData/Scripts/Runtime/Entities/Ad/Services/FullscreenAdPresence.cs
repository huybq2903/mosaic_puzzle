/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-12
 */

using Falcon.Helpers.Devkit;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Vỏ singleton của <see cref="FullscreenAdPresenceState"/>: entity ad GHI (show / close / show
    /// hỏng), entity app-open ĐỌC lúc app pause. Tách khỏi <see cref="AdViewCache"/> vì câu hỏi
    /// khác hẳn ("có ad toàn màn vừa lên hình không", không theo format) và để app-open không phải
    /// phụ thuộc cả cụm dịch vụ ad.
    /// <br/>Đồng hồ đơn điệu: chỉ đo khoảng ngắn ngay trước pause, chỉnh giờ máy không được làm lệch.
    /// </summary>
    public class FullscreenAdPresence : MySingleton<FullscreenAdPresence>
    {
        private readonly FullscreenAdPresenceState _state = new();
        private readonly object _lock = new();
        private readonly PerfPackRepository _perfPackRepository;

        public FullscreenAdPresence(PerfPackRepository perfPackRepository)
        {
            _perfPackRepository = perfPackRepository;
        }

        public void MarkShown(AdType type)
        {
            if (!FullscreenAdPresenceState.IsFullscreen(type)) return;
            lock (_lock) _state.MarkShown(MonotonicClock.ElapsedMillis);
            // perf_screen = "ad" tới khi QC đóng / hỏng, hoặc app quay lại từ nền (QC toàn màn làm app pause — quay lại
            // là QC đã đóng; mediation quên OnClosed thì màn không kẹt ở "ad").
            _perfPackRepository.SetAdOverlay(true);
        }

        public void MarkEnded(AdType type)
        {
            if (!FullscreenAdPresenceState.IsFullscreen(type)) return;
            lock (_lock) _state.MarkEnded();
            _perfPackRepository.SetAdOverlay(false);
        }

        /// <inheritdoc cref="FullscreenAdPresenceState.IsAdOnset"/>
        public bool IsAdOnset()
        {
            lock (_lock) return _state.IsAdOnset(MonotonicClock.ElapsedMillis);
        }
    }
}
