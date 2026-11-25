/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 */

using System.Collections.Generic;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Popup che bàn chơi thuộc khối thời gian 🟡 nào của sổ lượt chơi (hợp đồng đợt 3 B6 §6.5). Danh sách ĐÓNG.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum PopupKind
    {
        /// <summary>Lời mời: hồi sinh, booster, offer IAP. SDK tự xếp <c>popup:revive_offer</c> / <c>popup:booster_offer</c>.</summary>
        [EnumMember(Value = "offer")] Offer,

        /// <summary>Hướng dẫn — người chơi đang đọc. Game phải khai, SDK không đoán.</summary>
        [EnumMember(Value = "tutorial")] Tutorial,

        /// <summary>Khác: tạm dừng, cài đặt… Mặc định khi game không truyền.</summary>
        [EnumMember(Value = "other")] Other
    }

    /// <summary>Một đoạn ở MỘT màn đã kết thúc — dữ liệu của một dòng <c>f_sdk_perf_screen</c>.</summary>
    public readonly struct SurfaceSegment
    {
        public readonly string Screen;
        public readonly PerfSummary Perf;

        /// <summary>Mức nhiệt cao nhất trong đoạn (thứ tự 0–3, đợt 2); null nếu không đọc được lần nào.</summary>
        public readonly int? ThermalMax;

        /// <summary>Có lúc nào trong đoạn bật tiết kiệm pin (đợt 2); null nếu không đọc được lần nào.</summary>
        public readonly bool? LowPower;

        public SurfaceSegment(string screen, PerfSummary perf, int? thermalMax = null, bool? lowPower = null)
        {
            Screen = screen;
            Perf = perf;
            ThermalMax = thermalMax;
            LowPower = lowPower;
        }
    }

    /// <summary>
    /// Màn hiện tại cho <c>perf_screen</c> (hợp đồng đợt 3 B1): màn nền (<c>home</c>, <c>level</c>, <c>shop</c>…) + chồng
    /// popup (<c>popup:&lt;surface_id&gt;</c>) + lớp QC toàn màn (<c>ad</c>, SDK tự đặt).
    /// <list type="bullet">
    /// <item><c>Enter("popup:…")</c> đẩy popup lên chồng; <c>Enter</c> tên khác thay màn nền và bỏ cả chồng popup.</item>
    /// <item><c>Exit()</c> gỡ popup trên cùng; không còn popup thì gỡ luôn màn nền (màn hiện tại thành không biết).</item>
    /// <item>QC toàn màn đang chiếu thì màn hiện tại là <c>ad</c>, chồng bên dưới giữ nguyên.</item>
    /// </list>
    /// Mỗi lần màn hiện tại ĐỔI, đoạn của màn vừa rời được đóng thành một <see cref="SurfaceSegment"/> (số đo chỉ của
    /// lúc app ở trước); lúc xuống nền cũng đóng đoạn đang dở. Màn không biết (null) thì không có dòng.
    /// <br/>Pure class — caller bơm khung hình và tự khoá.
    /// </summary>
    public class SurfaceState
    {
        public const string POPUP_PREFIX = "popup:";
        public const string AD = "ad";

        private readonly List<(string name, PopupKind kind)> _popups = new();
        private readonly List<SurfaceSegment> _closed = new();
        private string _base;
        private bool _adOverlay;
        private string _segmentScreen;
        private PerfSampleState _segment = new();
        private int? _thermal, _segThermalMax;
        private bool? _lowPower, _segLowPower;

        /// <summary>Màn hiện tại (giá trị <c>perf_screen</c>), null nếu game chưa báo màn nào.</summary>
        public string Current => _adOverlay ? AD : _popups.Count > 0 ? _popups[_popups.Count - 1].name : _base;

        /// <summary>Loại của popup trên cùng (null nếu màn hiện tại không phải popup, kể cả khi đang chiếu QC).</summary>
        public PopupKind? CurrentPopupKind => !_adOverlay && _popups.Count > 0 ? _popups[_popups.Count - 1].kind : null;

        public void Enter(string name, PopupKind? kind = null)
        {
            if (string.IsNullOrEmpty(name)) return;
            Mutate(() =>
            {
                if (name.StartsWith(POPUP_PREFIX))
                {
                    _popups.Add((name, kind ?? DefaultKind(name)));
                    return;
                }

                _popups.Clear();
                _base = name;
            });
        }

        public void Exit()
        {
            Mutate(() =>
            {
                if (_popups.Count > 0) _popups.RemoveAt(_popups.Count - 1);
                else _base = null;
            });
        }

        /// <summary>QC toàn màn bắt đầu / kết thúc chiếu.</summary>
        public void SetAdOverlay(bool showing)
        {
            Mutate(() => _adOverlay = showing);
        }

        public void AddFrame(float deltaSec) => _segment.AddFrame(deltaSec);

        public void AddMemoryWarning() => _segment.AddMemoryWarning();

        /// <summary>Số đọc mới nhất về nhiệt độ máy / tiết kiệm pin (đợt 2) — đoạn đang dở giữ mức cao nhất / "có lúc bật".</summary>
        public void NoteDevice(int? thermal, bool? lowPower)
        {
            _thermal = thermal;
            _lowPower = lowPower;
            if (thermal.HasValue) _segThermalMax = _segThermalMax.HasValue ? System.Math.Max(_segThermalMax.Value, thermal.Value) : thermal;
            if (lowPower.HasValue) _segLowPower = _segLowPower == true || lowPower.Value;
        }

        /// <summary>App vừa quay lại từ nền: QC toàn màn (nếu có) đã đóng; khung đầu sau resume không đếm.</summary>
        public void MarkResumed()
        {
            SetAdOverlay(false);
            _segment.MarkResumed();
        }

        /// <summary>
        /// App xuống nền: đóng đoạn đang dở của màn hiện tại; khi quay lại, màn đó mở đoạn mới.
        /// <br/>Đang chiếu QC toàn màn thì KHÔNG đóng: chính QC làm app pause, đoạn <c>ad</c> kéo qua lúc nằm nền và đóng
        /// lúc quay lại — đóng cả hai lần là hai dòng <c>ad</c> cho một lần chiếu.
        /// </summary>
        public void CloseForBackground()
        {
            if (_adOverlay) return;
            CloseSegment();
            _segmentScreen = Current;
        }

        /// <summary>Lấy các đoạn đã đóng (để gửi) và xoá khỏi hàng.</summary>
        public List<SurfaceSegment> DrainClosed()
        {
            var drained = new List<SurfaceSegment>(_closed);
            _closed.Clear();
            return drained;
        }

        /// <summary>Popup lời mời đã ký — SDK tự xếp vào <see cref="PopupKind.Offer"/>.</summary>
        public static PopupKind DefaultKind(string popupName)
        {
            return popupName == POPUP_PREFIX + FUiSurface.ReviveOffer || popupName == POPUP_PREFIX + FUiSurface.BoosterOffer
                ? PopupKind.Offer
                : PopupKind.Other;
        }

        private void Mutate(System.Action change)
        {
            var before = Current;
            change();
            if (Current == before) return;
            CloseSegment();
            _segmentScreen = Current;
        }

        private void CloseSegment()
        {
            var perf = _segment.TakeAndReset();
            if (_segmentScreen != null) _closed.Add(new SurfaceSegment(_segmentScreen, perf, _segThermalMax, _segLowPower));
            // Đoạn mới bắt đầu từ trạng thái máy hiện tại
            _segThermalMax = _thermal;
            _segLowPower = _lowPower;
        }
    }
}
