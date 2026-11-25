/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 */

using System.Collections.Generic;
using Falcon.Helpers.Devkit;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Nơi giữ số đo hiệu năng theo khoảnh khắc (cửa sổ 5 giây) và theo màn (hợp đồng đợt 3 B1). Không phụ thuộc gì, để
    /// pipeline dựng log (decor), presence của QC và API màn cùng dùng mà không mở vòng phụ thuộc.
    /// <br/><see cref="PerfSampleService"/> (main thread) bơm khung hình; luồng dựng log chỉ ĐỌC giá trị đã giữ — không
    /// có lời gọi API Unity nào ở đây.
    /// </summary>
    public class PerfPackRepository : MySingleton<PerfPackRepository>
    {
        public const string FPS_AVG_5S = "perf_fps_avg_5s";
        public const string FRAME_WORST_MS_5S = "perf_frame_worst_ms_5s";
        public const string FRAME_DROPS_SINCE_LAST = "perf_frame_drops_since_last";
        public const string MEM_WARNINGS_SINCE_LAST = "perf_mem_warnings_since_last";
        public const string SCREEN = "perf_screen";
        public const string THERMAL_STATE = "perf_thermal_state";
        public const string LOW_POWER = "perf_low_power";

        // Số đọc native (main thread ghi, luồng dựng log đọc) — -1 = không biết
        private volatile int _thermal = -1;
        private volatile int _lowPower = -1;

        private readonly PerfWindowState _window = new();
        private readonly SurfaceState _surface = new();
        private readonly object _surfaceLock = new();

        public void AddFrame(float deltaSec)
        {
            _window.AddFrame(deltaSec);
            lock (_surfaceLock) _surface.AddFrame(deltaSec);
        }

        public void AddMemoryWarning()
        {
            _window.AddMemoryWarning();
            lock (_surfaceLock) _surface.AddMemoryWarning();
        }

        public void MarkResumed()
        {
            _window.MarkResumed();
            lock (_surfaceLock) _surface.MarkResumed();
        }

        /// <summary>Main thread: số đọc mới nhất về nhiệt độ máy (thứ tự 0–3) / tiết kiệm pin (đợt 2, <c>DeviceSignalService</c>).</summary>
        public void SetDeviceState(int? thermalOrdinal, bool? lowPower)
        {
            _thermal = thermalOrdinal ?? -1;
            _lowPower = lowPower.HasValue ? lowPower.Value ? 1 : 0 : -1;
            lock (_surfaceLock) _surface.NoteDevice(thermalOrdinal, lowPower);
        }

        private void PutDevice(Dictionary<string, object> dictionary)
        {
            var thermal = DeviceSignalMaps.ThermalName(_thermal < 0 ? null : _thermal);
            if (thermal != null) dictionary[THERMAL_STATE] = thermal;
            var lowPower = _lowPower;
            if (lowPower >= 0) dictionary[LOW_POWER] = lowPower == 1;
        }

        public void SetAdOverlay(bool showing)
        {
            lock (_surfaceLock) _surface.SetAdOverlay(showing);
        }

        public void Enter(string name, PopupKind? kind)
        {
            lock (_surfaceLock) _surface.Enter(name, kind);
        }

        public void Exit()
        {
            lock (_surfaceLock) _surface.Exit();
        }

        public void CloseForBackground()
        {
            lock (_surfaceLock) _surface.CloseForBackground();
        }

        public List<SurfaceSegment> DrainClosed()
        {
            lock (_surfaceLock) return _surface.DrainClosed();
        }

        /// <summary>Màn hiện tại (<c>perf_screen</c>), null nếu chưa biết.</summary>
        public string CurrentScreen
        {
            get
            {
                lock (_surfaceLock) return _surface.Current;
            }
        }

        /// <summary>Loại popup trên cùng (null nếu màn hiện tại không phải popup) — sổ lượt chơi B6 dùng.</summary>
        public PopupKind? CurrentPopupKind
        {
            get
            {
                lock (_surfaceLock) return _surface.CurrentPopupKind;
            }
        }

        /// <summary>Đính gói hiệu năng vào payload — đặt lại bộ đếm "kể từ bản tin trước".</summary>
        public void PutPack(Dictionary<string, object> dictionary)
        {
            var pack = _window.TakePack();
            PutScreen(dictionary);
            PutDevice(dictionary); // trạng thái máy không phụ thuộc khung hình — có cả trước khung đầu
            // Chưa có khung hình nào (log bắn trước khung đầu tiên) = chưa đo: cả gói vắng, không gửi 0 (vắng ≠ 0).
            if (!pack.FpsAvg5s.HasValue) return;
            dictionary[FPS_AVG_5S] = System.Math.Round(pack.FpsAvg5s.Value, 1);
            if (pack.FrameWorstMs5s.HasValue) dictionary[FRAME_WORST_MS_5S] = pack.FrameWorstMs5s.Value;
            dictionary[FRAME_DROPS_SINCE_LAST] = pack.FrameDropsSinceLast;
            dictionary[MEM_WARNINGS_SINCE_LAST] = pack.MemWarningsSinceLast;
        }

        /// <summary>
        /// Như <see cref="PutPack"/> nhưng KHÔNG đặt lại gì (bản chụp khẩn S2): chỉ <c>perf_screen</c> + fps / khung chậm
        /// nhất 5 giây. Hai bộ đếm "kể từ bản tin trước" vắng — đếm ở đây rồi log thường kế tiếp đếm lại là đếm đôi.
        /// </summary>
        public void PeekPack(Dictionary<string, object> dictionary)
        {
            var pack = _window.PeekPack();
            PutScreen(dictionary);
            PutDevice(dictionary);
            if (!pack.FpsAvg5s.HasValue) return;
            dictionary[FPS_AVG_5S] = System.Math.Round(pack.FpsAvg5s.Value, 1);
            if (pack.FrameWorstMs5s.HasValue) dictionary[FRAME_WORST_MS_5S] = pack.FrameWorstMs5s.Value;
        }

        /// <summary>Chỉ đính <c>perf_screen</c> (vắng nếu chưa biết màn).</summary>
        public void PutScreen(Dictionary<string, object> dictionary)
        {
            var screen = CurrentScreen;
            if (screen != null) dictionary[SCREEN] = screen;
        }
    }
}
