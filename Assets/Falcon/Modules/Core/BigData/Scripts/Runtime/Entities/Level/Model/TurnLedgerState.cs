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
    /// <summary>Game khai một lần những gì nó báo cho sổ lượt chơi (hợp đồng đợt 3 B6, <c>Level.Declare</c>).</summary>
    [Serializable]
    public class TurnCaps
    {
        public bool busy;
        public bool actions;
        public bool ineffectiveTap;
        public bool speed;
        public bool screen;
    }

    /// <summary>
    /// Sổ thời gian của MỘT đoạn lượt chơi (hợp đồng đợt 3 B6, phương án A — SDK giữ sổ, ký 23/11). Đoạn = từ
    /// <c>OnStart</c> hoặc từ lúc chơi tiếp (<c>OnResume</c>) tới lúc gửi; sổ đặt lại mỗi đoạn.
    /// <br/><b>Năm khối không chồng nhau</b>, giây nào chồng lấy theo ưu tiên
    /// <b>nền &gt; QC được tính &gt; popup &gt; game bận &gt; làm được</b>. QC KHÔNG được tính vào lượt (§6.4) thì đồng hồ
    /// DỪNG hẳn: không vào khối nào, không vào tổng. x2 là lớp phủ, chỉ đếm trong khối bận / làm được.
    /// <br/>Các khoảng không thao tác (mốc 10 / 30 / 120 giây) đo bằng thời gian 🟢, tính cả khoảng (không chỉ phần
    /// vượt), gồm cả khoảng đang dở lúc gửi.
    /// <br/>Pure class — không IO/DI/time: caller truyền mốc giờ (ms, đồng hồ đơn điệu) và tự khoá.
    /// </summary>
    public class TurnLedgerState
    {
        public const long GAP10_MS = 10_000;
        public const long GAP30_MS = 30_000;
        public const long GAP120_MS = 120_000;

        private bool _open;
        private string _turnId;
        private int _segmentNo;
        private long _last;

        private bool _background;
        private bool _adCounted;
        private bool _adExcluded;
        private PopupKind? _popup;
        private bool _busy;
        private bool _speed;

        private long _bg, _ad, _popupOffer, _popupTutorial, _popupOther, _busyMs, _ready, _speedOn;
        private long _gap10, _gap30, _gap120, _readySinceLastAction;
        private long? _firstActionReady;
        private long _countedAtLastAction;
        private int _actions, _tapsWhileBusy, _ineffectiveTaps, _offersShown;

        public bool IsOpen => _open;
        public string TurnId => _open ? _turnId : null;
        public int SegmentNo => _segmentNo;

        /// <summary>Mở đoạn mới (lượt mới: đoạn 1; chơi tiếp: số đoạn mới). Mọi số đặt lại; trạng thái popup / bận / x2 cũng.</summary>
        public void Begin(string turnId, int segmentNo, long now)
        {
            _open = !string.IsNullOrEmpty(turnId);
            _turnId = turnId;
            _segmentNo = Math.Max(1, segmentNo);
            _last = now;
            _background = _adCounted = _adExcluded = _busy = _speed = false;
            _popup = null;
            _bg = _ad = _popupOffer = _popupTutorial = _popupOther = _busyMs = _ready = _speedOn = 0;
            _gap10 = _gap30 = _gap120 = _readySinceLastAction = 0;
            _firstActionReady = null;
            _countedAtLastAction = 0;
            _actions = _tapsWhileBusy = _ineffectiveTaps = _offersShown = 0;
        }

        /// <summary>Đoạn kết thúc (lượt kết thúc / rời lượt) — các báo sau đó bị bỏ tới đoạn kế.</summary>
        public void End(long now)
        {
            Accrue(now);
            _open = false;
        }

        public void SetBackground(bool background, long now)
        {
            Accrue(now);
            _background = background;
        }

        /// <summary>
        /// Thời gian nằm nền mà đồng hồ đơn điệu bỏ sót (máy ngủ lúc app nằm nền) — caller bù bằng hiệu giờ thật.
        /// </summary>
        public void AddBackgroundCorrection(long extraMs)
        {
            if (_open && extraMs > 0) _bg += extraMs;
        }

        /// <summary>
        /// QC toàn màn bắt đầu chiếu. Được tính vào lượt khi CẢ HAI đúng (§6.4): <paramref name="adWhen"/> ∈
        /// <c>revive</c> · <c>booster</c> · <c>Unknown</c> · trống, và <paramref name="triggerTurnId"/> trống hoặc là lượt này.
        /// </summary>
        public void AdShown(string adWhen, string triggerTurnId, long now)
        {
            Accrue(now);
            var counted = IsCountedAd(adWhen, triggerTurnId, _turnId);
            _adCounted = counted;
            _adExcluded = !counted;
        }

        public void AdEnded(long now)
        {
            Accrue(now);
            _adCounted = _adExcluded = false;
        }

        public void SetPopup(PopupKind? kind, long now)
        {
            Accrue(now);
            _popup = kind;
        }

        public void SetBusy(bool busy, long now)
        {
            Accrue(now);
            _busy = busy;
        }

        public void SetSpeed(bool on, long now)
        {
            Accrue(now);
            _speed = on;
        }

        public void Action(long now)
        {
            Accrue(now);
            if (!_open) return;
            _actions++;
            _firstActionReady ??= _ready;
            CloseGap();
            _countedAtLastAction = Counted;
        }

        public void IneffectiveTap(long now)
        {
            Accrue(now);
            if (!_open) return;
            // "Đang bận" theo khối đang tính lúc chạm: popup / QC che bàn chơi thì không phải chạm lúc chờ hoạt hình
            if (CurrentBlockIsBusy) _tapsWhileBusy++;
            else _ineffectiveTaps++;
        }

        public void OfferShown()
        {
            if (_open) _offersShown++;
        }

        /// <summary>Số tích luỹ từ đầu đoạn tới <paramref name="now"/>. Null nếu không có đoạn mở.</summary>
        public TurnLedgerSnapshot Snapshot(long now)
        {
            Accrue(now);
            if (!_open) return null;
            var openGap = _readySinceLastAction;
            return new TurnLedgerSnapshot
            {
                segmentNo = _segmentNo,
                totalMs = Counted,
                backgroundMs = _bg,
                adMs = _ad,
                popupOfferMs = _popupOffer,
                popupTutorialMs = _popupTutorial,
                popupOtherMs = _popupOther,
                busyMs = _busyMs,
                readyMs = _ready,
                readyGap10Ms = _gap10 + (openGap > GAP10_MS ? openGap : 0),
                readyGap30Ms = _gap30 + (openGap > GAP30_MS ? openGap : 0),
                readyGap120Ms = _gap120 + (openGap > GAP120_MS ? openGap : 0),
                firstActionMs = _firstActionReady,
                sinceLastActionMs = _actions > 0 ? Counted - _countedAtLastAction : null,
                speedOnMs = _speedOn,
                actions = _actions,
                tapsWhileBusy = _tapsWhileBusy,
                ineffectiveTaps = _ineffectiveTaps,
                offersShown = _offersShown
            };
        }

        /// <summary>
        /// Field wire của khối đang được tính lúc này (đường khẩn S2 cộng nốt từ bản chụp); null nếu không có đoạn mở
        /// hoặc đồng hồ lượt đang dừng (QC bị loại, §6.4).
        /// </summary>
        public string RunningKey
        {
            get
            {
                if (!_open) return null;
                if (_background) return "t_background_sec";
                if (_adExcluded) return null;
                if (_adCounted) return "t_ad_sec";
                if (_popup.HasValue)
                    return _popup.Value switch
                    {
                        PopupKind.Offer => "t_popup_offer_sec",
                        PopupKind.Tutorial => "t_popup_tutorial_sec",
                        _ => "t_popup_other_sec"
                    };
                return _busy ? "t_busy_sec" : "t_ready_sec";
            }
        }

        /// <summary>x2 đang được cộng (chỉ trong khối bận / làm được).</summary>
        public bool SpeedRunning
        {
            get
            {
                var key = RunningKey;
                return _speed && (key == "t_busy_sec" || key == "t_ready_sec");
            }
        }

        public static bool IsCountedAd(string adWhen, string triggerTurnId, string turnId)
        {
            var whenOk = string.IsNullOrEmpty(adWhen) || adWhen == FAdWhen.Revive || adWhen == FAdWhen.Booster ||
                         adWhen == FAdWhen.Unknown;
            var triggerOk = string.IsNullOrEmpty(triggerTurnId) || triggerTurnId == turnId;
            return whenOk && triggerOk;
        }

        private long Counted => _bg + _ad + _popupOffer + _popupTutorial + _popupOther + _busyMs + _ready;

        private bool CurrentBlockIsBusy => !_background && !_adCounted && !_adExcluded && _popup == null && _busy;

        private void Accrue(long now)
        {
            var delta = now - _last;
            _last = now;
            if (!_open || delta <= 0) return;

            if (_background) _bg += delta;
            else if (_adExcluded) return; // §6.4: QC bị loại — đồng hồ lượt dừng hẳn
            else if (_adCounted) _ad += delta;
            else if (_popup.HasValue) AddPopup(_popup.Value, delta);
            else if (_busy)
            {
                _busyMs += delta;
                if (_speed) _speedOn += delta;
            }
            else
            {
                _ready += delta;
                _readySinceLastAction += delta;
                if (_speed) _speedOn += delta;
            }
        }

        private void AddPopup(PopupKind kind, long delta)
        {
            switch (kind)
            {
                case PopupKind.Offer: _popupOffer += delta; break;
                case PopupKind.Tutorial: _popupTutorial += delta; break;
                default: _popupOther += delta; break;
            }
        }

        private void CloseGap()
        {
            var gap = _readySinceLastAction;
            if (gap > GAP10_MS) _gap10 += gap;
            if (gap > GAP30_MS) _gap30 += gap;
            if (gap > GAP120_MS) _gap120 += gap;
            _readySinceLastAction = 0;
        }
    }

    /// <summary>Số tích luỹ của một đoạn (ms / số đếm) — <see cref="TurnLedgerSnapshot.ToWire"/> dựng field hợp đồng.</summary>
    public class TurnLedgerSnapshot
    {
        public int segmentNo;
        public long totalMs, backgroundMs, adMs, popupOfferMs, popupTutorialMs, popupOtherMs, busyMs, readyMs;
        public long readyGap10Ms, readyGap30Ms, readyGap120Ms, speedOnMs;
        public long? firstActionMs, sinceLastActionMs;
        public int actions, tapsWhileBusy, ineffectiveTaps, offersShown;

        /// <summary>
        /// Field hợp đồng B6 (tên snake gửi thẳng — có chữ số). Thời gian là giây số thực (giữ lẻ), số đếm là số nguyên.
        /// Vắng theo <paramref name="caps"/> (§6.3): khả năng chưa khai thì field vắng; đã khai mà không xảy ra thì 0.
        /// Không khai gì (<paramref name="caps"/> null) thì không có cờ <c>cap_*</c> nào và field của mọi khả năng vắng.
        /// </summary>
        public Dictionary<string, object> ToWire(TurnCaps caps)
        {
            var wire = new Dictionary<string, object>
            {
                ["turn_segment_no"] = segmentNo,
                ["t_total_sec"] = Sec(totalMs),
                ["t_background_sec"] = Sec(backgroundMs),
                ["t_ad_sec"] = Sec(adMs),
                ["t_ready_sec"] = Sec(readyMs)
            };

            var c = caps ?? new TurnCaps();
            if (caps != null)
            {
                wire["cap_busy"] = c.busy;
                wire["cap_actions"] = c.actions;
                wire["cap_ineffective_tap"] = c.ineffectiveTap;
                wire["cap_speed"] = c.speed;
                wire["cap_screen"] = c.screen;
            }

            if (c.busy) wire["t_busy_sec"] = Sec(busyMs);
            if (c.screen)
            {
                wire["t_popup_offer_sec"] = Sec(popupOfferMs);
                wire["t_popup_tutorial_sec"] = Sec(popupTutorialMs);
                wire["t_popup_other_sec"] = Sec(popupOtherMs);
                wire["n_offer_shown"] = offersShown;
            }

            if (c.actions)
            {
                wire["n_actions"] = actions;
                wire["t_ready_gap10_sec"] = Sec(readyGap10Ms);
                wire["t_ready_gap30_sec"] = Sec(readyGap30Ms);
                wire["t_ready_gap120_sec"] = Sec(readyGap120Ms);
                if (firstActionMs.HasValue) wire["first_action_sec"] = Sec(firstActionMs.Value);
                if (sinceLastActionMs.HasValue) wire["sec_since_last_action"] = Sec(sinceLastActionMs.Value);
            }

            if (c.ineffectiveTap)
            {
                wire["n_taps_while_busy"] = tapsWhileBusy;
                wire["n_ineffective_taps"] = ineffectiveTaps;
            }

            if (c.speed) wire["t_speed_on_sec"] = Sec(speedOnMs);
            return wire;
        }

        private static double Sec(long ms) => Math.Round(ms / 1000.0, 2);
    }
}
