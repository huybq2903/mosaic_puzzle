/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 */

using System;
using System.Collections.Generic;
using System.Threading;
using Falcon.Helpers.Devkit;
using Newtonsoft.Json;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Giữ sổ thời gian lượt chơi (<see cref="TurnLedgerState"/>, hợp đồng đợt 3 B6) và nối nó với các nguồn sự kiện:
    /// vòng đời lượt (decor dòng level), nền / quay lại (<see cref="ITerminal"/> / <see cref="IPioneer"/>), QC toàn màn,
    /// popup (API màn), và các lời gọi của game (<c>Level.Declare</c>, thao tác, bận, chạm vô tác dụng, x2).
    /// <br/>Đồng hồ: đơn điệu cho lúc app ở trước. Lúc nằm nền máy có thể ngủ (đồng hồ đơn điệu đứng) nên phần nằm nền
    /// được bù bằng hiệu giờ thật — tổng mới khớp khoảng giờ server tự trừ.
    /// <br/>Pause do QC toàn màn vừa lên hình KHÔNG phải nằm nền: QC làm app pause trên cả hai nền tảng, đếm là nền thì
    /// khối QC luôn bằng 0.
    /// </summary>
    public class TurnLedgerService : MySingleton<TurnLedgerService>, ITerminal, IPioneer
    {
        private const string CAPS_KEY = "falcon.bigdata.level.turn_caps";

        private readonly TurnLedgerState _state = new();
        private readonly object _lock = new();
        private readonly FullscreenAdPresence _fullscreenAdPresence;
        private readonly IDataPool _dataPool;
        private TurnCaps _caps;
        private bool _capsLoaded;
        private long? _backgroundAtWallMs;
        private long _backgroundAtMonoMs;
        private int _version;

        public TurnLedgerService(FullscreenAdPresence fullscreenAdPresence, IDataPool dataPool)
        {
            _fullscreenAdPresence = fullscreenAdPresence;
            _dataPool = dataPool;
        }

        private static long Now => MonotonicClock.ElapsedMillis;

        /// <summary>Tăng mỗi lần sổ đổi trạng thái — bản chụp khẩn (S2) dựng lại khi số này đổi.</summary>
        public int Version => Volatile.Read(ref _version);

        /// <summary>
        /// Field sổ của <paramref name="turnId"/> kèm khối đang chạy (cho bản chụp khẩn S2), cùng một lần khoá.
        /// Null nếu sổ không mở cho đúng lượt đó.
        /// </summary>
        public (Dictionary<string, object> wire, string runningKey, bool speedRunning)? SnapshotFor(string turnId)
        {
            lock (_lock)
            {
                if (turnId == null || _state.TurnId != turnId) return null;
                var wire = _state.Snapshot(Now)?.ToWire(LoadCaps());
                if (wire == null) return null;
                return (wire, _state.RunningKey, _state.SpeedRunning);
            }
        }

        private void Touch() => Interlocked.Increment(ref _version);
        private static long WallNow => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // ---- Khai khả năng ----

        /// <summary>Khả năng game đã khai gần nhất (lưu qua restart); null nếu chưa từng khai.</summary>
        public TurnCaps Caps
        {
            get
            {
                lock (_lock) return LoadCaps();
            }
        }

        public void Declare(TurnCaps caps)
        {
            Touch();
            if (caps == null) return;
            lock (_lock)
            {
                _caps = caps;
                _capsLoaded = true;
            }

            _dataPool.Compute<string>(CAPS_KEY, _ => JsonConvert.SerializeObject(caps));
        }

        // ---- Vòng đời đoạn ----

        public void Begin(string turnId, int segmentNo)
        {
            Touch();
            lock (_lock) _state.Begin(turnId, segmentNo, Now);
        }

        public void End()
        {
            Touch();
            lock (_lock) _state.End(Now);
        }

        /// <summary>Field B6 cho dòng lượt của <paramref name="turnId"/>; null nếu sổ không mở cho đúng lượt đó.</summary>
        public Dictionary<string, object> WireFor(string turnId)
        {
            lock (_lock)
            {
                if (turnId == null || _state.TurnId != turnId) return null;
                return _state.Snapshot(Now)?.ToWire(LoadCaps());
            }
        }

        // ---- Nguồn sự kiện ----

        public void OnPostStop()
        {
            Touch();
            // Hỏi NGAY lúc pause (cùng luật app_open): QC toàn màn vừa lên hình thì đây không phải nằm nền
            if (_fullscreenAdPresence.IsAdOnset()) return;
            lock (_lock)
            {
                _state.SetBackground(true, Now);
                _backgroundAtWallMs = WallNow;
                _backgroundAtMonoMs = Now;
            }
        }

        public void OnPreContinue()
        {
            Touch();
            lock (_lock)
            {
                var now = Now;
                _state.SetBackground(false, now);
                if (_backgroundAtWallMs.HasValue)
                {
                    var wall = WallNow - _backgroundAtWallMs.Value;
                    var mono = now - _backgroundAtMonoMs;
                    if (wall > mono) _state.AddBackgroundCorrection(wall - mono);
                }

                _backgroundAtWallMs = null;
                // Quay lại từ nền = QC toàn màn (nếu có) đã đóng — mediation quên OnClosed thì đồng hồ không kẹt
                _state.AdEnded(now);
            }
        }

        public void OnFullscreenAdShown(string adWhen, string triggerTurnId)
        {
            Touch();
            lock (_lock) _state.AdShown(adWhen, triggerTurnId, Now);
        }

        public void OnFullscreenAdEnded()
        {
            Touch();
            lock (_lock) _state.AdEnded(Now);
        }

        public void OnPopupChanged(PopupKind? kind)
        {
            Touch();
            lock (_lock)
            {
                // cap_screen chưa khai thì 3 khối popup vắng — thời gian không được rơi vào khối bị giấu
                if (LoadCaps()?.screen != true) kind = null;
                _state.SetPopup(kind, Now);
            }
        }

        public void OnOfferShown()
        {
            Touch();
            lock (_lock) _state.OfferShown();
        }

        public void SetBusy(bool busy)
        {
            Touch();
            lock (_lock)
            {
                if (LoadCaps()?.busy != true) return;
                _state.SetBusy(busy, Now);
            }
        }

        public void SetSpeed(bool on)
        {
            Touch();
            lock (_lock) _state.SetSpeed(on, Now);
        }

        public void OnPlayerAction()
        {
            Touch();
            lock (_lock) _state.Action(Now);
        }

        public void OnIneffectiveTap()
        {
            Touch();
            lock (_lock) _state.IneffectiveTap(Now);
        }

        private TurnCaps LoadCaps()
        {
            if (_capsLoaded) return _caps;
            _capsLoaded = true;
            var json = _dataPool.GetOrDefault<string>(CAPS_KEY, null);
            if (string.IsNullOrEmpty(json)) return _caps = null;
            try
            {
                _caps = JsonConvert.DeserializeObject<TurnCaps>(json);
            }
            catch (JsonException)
            {
                _caps = null;
            }

            return _caps;
        }
    }
}
