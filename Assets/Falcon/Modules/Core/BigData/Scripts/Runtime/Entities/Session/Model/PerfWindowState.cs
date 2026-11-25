/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 */

using System;
using System.Collections.Generic;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>Gói hiệu năng đính lên một dòng log (hợp đồng đợt 3 B1(a)). Field null = không đo được ⇒ vắng.</summary>
    public readonly struct PerfPack
    {
        public readonly float? FpsAvg5s;
        public readonly int? FrameWorstMs5s;
        public readonly int FrameDropsSinceLast;
        public readonly int MemWarningsSinceLast;

        public PerfPack(float? fpsAvg5s, int? frameWorstMs5s, int frameDropsSinceLast, int memWarningsSinceLast)
        {
            FpsAvg5s = fpsAvg5s;
            FrameWorstMs5s = frameWorstMs5s;
            FrameDropsSinceLast = frameDropsSinceLast;
            MemWarningsSinceLast = memWarningsSinceLast;
        }
    }

    /// <summary>
    /// Cửa sổ trượt 5 giây của khung hình + bộ đếm "kể từ bản tin trước" cho gói hiệu năng (B1(a)).
    /// <br/>Cửa sổ tính theo thời gian KHUNG (tổng delta), không theo đồng hồ: chỉ khung lúc app ở trước mới vào, nên 5
    /// giây là 5 giây người chơi thật sự nhìn thấy. Cùng luật bỏ khung với <see cref="PerfSampleState"/>: khung &gt; 2 s
    /// và khung đầu sau resume không vào.
    /// <br/>Cập nhật O(1) khấu hao mỗi khung (tổng chạy + hàng đợi đơn điệu cho khung chậm nhất); vài trăm số float.
    /// Bên ghi là main thread, bên đọc là luồng dựng log — có khoá.
    /// </summary>
    public class PerfWindowState
    {
        public const float WINDOW_SEC = 5f;

        private readonly object _lock = new();
        private readonly Queue<float> _frames = new();
        private readonly LinkedList<float> _maxCandidates = new(); // giảm dần từ đầu tới cuối
        private float _windowSum;
        private int _dropsSinceLast;
        private int _memWarningsSinceLast;
        private bool _skipNextFrame;

        public void MarkResumed()
        {
            lock (_lock) _skipNextFrame = true;
        }

        public void AddMemoryWarning()
        {
            lock (_lock) _memWarningsSinceLast++;
        }

        public void AddFrame(float deltaSec)
        {
            lock (_lock)
            {
                if (_skipNextFrame)
                {
                    _skipNextFrame = false;
                    return;
                }

                if (deltaSec <= 0 || deltaSec > PerfSampleState.NOT_RENDERING_SEC) return;
                if (deltaSec > PerfSampleState.FRAME_DROP_SEC) _dropsSinceLast++;

                _frames.Enqueue(deltaSec);
                _windowSum += deltaSec;
                while (_maxCandidates.Count > 0 && _maxCandidates.Last.Value < deltaSec) _maxCandidates.RemoveLast();
                _maxCandidates.AddLast(deltaSec);

                // Bỏ khung cũ tới khi cửa sổ còn ≤ 5 s (luôn giữ ít nhất khung vừa vào)
                while (_frames.Count > 1 && _windowSum - _frames.Peek() >= WINDOW_SEC) Evict();
            }
        }

        /// <summary>Đọc gói cho một dòng log rồi đặt lại hai bộ đếm "kể từ bản tin trước". Cửa sổ thì giữ nguyên.</summary>
        public PerfPack TakePack()
        {
            lock (_lock)
            {
                var pack = Read();
                _dropsSinceLast = 0;
                _memWarningsSinceLast = 0;
                return pack;
            }
        }

        /// <summary>Đọc gói mà KHÔNG đặt lại gì (bản chụp khẩn S2).</summary>
        public PerfPack PeekPack()
        {
            lock (_lock) return Read();
        }

        private PerfPack Read()
        {
            float? fps = _frames.Count > 0 && _windowSum > 0 ? _frames.Count / _windowSum : null;
            int? worstMs = _maxCandidates.Count > 0 ? (int)Math.Round(_maxCandidates.First.Value * 1000) : null;
            return new PerfPack(fps, worstMs, _dropsSinceLast, _memWarningsSinceLast);
        }

        private void Evict()
        {
            var old = _frames.Dequeue();
            _windowSum -= old;
            // Khung cũ nhất ra khỏi cửa sổ: nếu nó đang là ứng viên lớn nhất (đầu hàng đợi) thì bỏ. So bằng giá trị là
            // đủ vì ứng viên giữ đúng thứ tự vào và bằng nhau thì bỏ cái cũ nhất trước — vẫn đúng max.
            if (_maxCandidates.Count > 0 && _maxCandidates.First.Value == old) _maxCandidates.RemoveFirst();
        }
    }
}
