/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-07-31
 */

using System;
using System.Collections.Generic;
using Falcon.Helpers.Devkit;
using JetBrains.Annotations;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Tham số của một lần app vào foreground. Toàn bộ do SDK-core tự đo (luật §H3) —
    /// dev game không phải điền gì.
    /// </summary>
    [Serializable]
    public class AppOpenParam : FParam
    {
        /// <summary>Cold = process vừa khởi tạo (mốc mở phiên); Warm = quay lại từ background.</summary>
        public LaunchType launchType;

        /// <summary>
        /// App nằm dưới background bao lâu trước khi quay lại. Null khi Cold (không có nền để đo).
        /// </summary>
        [FKey(RemoveIfNull = true)] public int? backgroundDurationSec;

        /// <summary>
        /// Số thứ tự lần vào foreground trong phiên (bắt đầu từ 1). Đếm CẢ những lần bị lọc vì
        /// nền quá ngắn — nên khoảng nhảy của openIndex cho biết có bao nhiêu lần quay lại ngắn
        /// không được log (giữ được thông tin mà không tốn event).
        /// </summary>
        public int openIndex;

        /// <summary>Loại kết nối lúc mở app — SDK-core tự đo (giải thích config fetch fail đầu phiên).</summary>
        [FKey(RemoveIfNull = true)] public NetworkType? networkType;

        /// <summary>
        ///     Mở app từ đâu (§B4). Unity không đọc được intent/launchOptions nên module
        ///     push/deeplink phải báo qua <c>FalconBigDataController.App.ReportOpenSource</c>; không ai báo thì vắng mặt.
        /// </summary>
        [FKey(RemoveIfNull = true)] public OpenSource? openSource;

        /// <summary>Campaign của push đã kéo người chơi vào — chỉ có khi <see cref="openSource"/> = push.</summary>
        [FKey(RemoveIfNull = true)] public string pushCampaignId;

        /// <summary>
        ///     Thời gian khởi động (ms) — CHỈ có ở lần Cold. Mốc đo: C# sống dậy → lúc dựng bản tin
        ///     này. THIẾU phần native bootstrap nên là số tương đối (xem <see cref="StartupClock"/>).
        /// </summary>
        [FKey(RemoveIfNull = true)] public int? startupDurationMs;

        /// <summary>
        ///     Trần fps game đặt (<c>Application.targetFrameRate</c>) — gửi NGUYÊN <c>-1</c> khi là mặc định nền tảng, SDK không
        ///     đoán con số thật (hợp đồng đợt 3 B1(c)). Game đổi trần giữa chừng thì dòng <c>f_sdk_perf_screen</c> kế tiếp mang lại.
        /// </summary>
        [FKey(RemoveIfNull = true)] public int? perfTargetFps;

        /// <summary>Tần số quét màn hình (Hz) lúc mở app. Vắng nếu nền tảng không trả.</summary>
        [FKey(RemoveIfNull = true)] public float? displayRefreshHz;

        /// <summary>
        ///     Mức đồ hoạ người chơi đang chọn, do game khai qua <c>App.SetGraphicsQuality</c> (lưu qua restart — lần mở Cold
        ///     mang giá trị lần khai trước). Vắng nếu game không có chỉnh đồ hoạ / chưa khai.
        /// </summary>
        [FKey(RemoveIfNull = true)] public string graphicsQuality;

        /// <summary>Pin lúc mở app, 0–100 (hợp đồng đợt 3 B3; iOS có thể ra theo bậc — kho coi là số thô). Vắng nếu không biết.</summary>
        [FKey(RemoveIfNull = true)] public int? batteryPct;

        /// <summary>Đang cắm sạc lúc mở app (gồm cả đã đầy mà vẫn cắm). Vắng nếu không biết.</summary>
        [FKey(RemoveIfNull = true)] public bool? batteryCharging;

        /// <summary>Đợt 2 (chỉ Android): kiểu cắm sạc <c>ac</c> · <c>usb</c> · <c>wireless</c> · <c>none</c>; nhiệt độ pin °C.</summary>
        [FKey(RemoveIfNull = true)] public string batteryPlug;
        [FKey(RemoveIfNull = true)] public float? batteryTempC;

        /// <summary>
        ///     Âm thanh máy (hợp đồng đợt 3 B4(c), đợt 2): âm lượng nhạc bậc 10 % (0 = tắt tiếng), chế độ chuông
        ///     <c>normal</c> · <c>vibrate</c> · <c>silent</c> (iOS vắng), đầu ra <c>speaker</c> · <c>wired</c> · <c>bluetooth</c>.
        /// </summary>
        [FKey(RemoveIfNull = true)] public int? deviceMediaVolumePct;
        [FKey(RemoveIfNull = true)] public string deviceRingerMode;
        [FKey(RemoveIfNull = true)] public string audioOutput;

        /// <summary>
        ///     Cấu hình máy (B14(a), đợt 2): hệ số cỡ chữ, chữ đậm (Android 12+ / iOS), giảm chuyển động, dung lượng trống
        ///     <c>lt_1gb</c> · <c>1_5gb</c> · <c>5_20gb</c> · <c>gt_20gb</c> (iOS vắng).
        /// </summary>
        [FKey(RemoveIfNull = true)] public float? deviceFontScale;
        [FKey(RemoveIfNull = true)] public bool? deviceBoldText;
        [FKey(RemoveIfNull = true)] public bool? deviceReduceMotion;
        [FKey(RemoveIfNull = true)] public string deviceStorageFreeBucket;

        /// <summary>Ảnh chụp trạng thái quyền SDK tự đọc (B14(b), đợt 2) — cùng bộ giá trị với <c>f_sdk_permission_*</c>.</summary>
        [FKey(RemoveIfNull = true)] public string permPushStatus;
        [FKey(RemoveIfNull = true)] public string permAttStatus;

        /// <summary>
        ///     Cài đặt của game (hợp đồng đợt 3 B4) — SDK tự giữ qua restart. Vắng nếu game chưa từng khai / báo.
        ///     <c>settingIsDefault</c> = cả ba công tắc còn ở mặc định game khai (cần <c>Setting.DeclareDefaults</c>).
        /// </summary>
        [FKey(RemoveIfNull = true)] public bool? settingSound;
        [FKey(RemoveIfNull = true)] public bool? settingMusic;
        [FKey(RemoveIfNull = true)] public bool? settingHaptic;
        [FKey(RemoveIfNull = true)] public bool? settingIsDefault;

        /// <summary>Mã ISO ngôn ngữ game ĐANG hiển thị — khác <c>language</c> của máy.</summary>
        [FKey(RemoveIfNull = true)] public string settingLanguage;

        /// <summary>
        ///     Loại tài khoản đang liên kết (hợp đồng đợt 3 B14(c), <c>Account.SetLinkedProviders</c>). Rỗng = chưa liên kết; vắng =
        ///     game không khai (không có tính năng liên kết). Lần mở Cold mang giá trị lần khai trước.
        /// </summary>
        [FKey(RemoveIfNull = true)] public List<string> linkedProviders;

        /// <summary>
        ///     Năm cờ khả năng của sổ lượt chơi (hợp đồng đợt 3 B6) — lần khai <c>Level.Declare</c> gần nhất. Lần mở Cold bắn
        ///     trước khi game kịp khai nên mang giá trị lần trước; máy cài mới, lần mở đầu thì vắng.
        /// </summary>
        [FKey(RemoveIfNull = true)] public bool? capBusy;
        [FKey(RemoveIfNull = true)] public bool? capActions;
        [FKey(RemoveIfNull = true)] public bool? capIneffectiveTap;
        [FKey(RemoveIfNull = true)] public bool? capSpeed;
        [FKey(RemoveIfNull = true)] public bool? capScreen;

        /// <summary>
        ///     Key-value tuỳ ý đi kèm nguồn mở app (metadata push: message id, deep link...) —
        ///     flatten vào payload, server lưu <c>event_extra_props</c>; cột hợp đồng thắng khi
        ///     trùng key. Vào qua <c>App.ReportOpenSource</c>, cùng TTL với nguồn.
        /// </summary>
        [FKey(RemoveIfNull = true)] [CanBeNull]
        public Dictionary<string, object> extraMeta;

        public override Dictionary<string, object> ToDictionary()
        {
            var dictionary = base.ToDictionary();
            dictionary.Remove(nameof(extraMeta));
            return dictionary.MergeExtras(extraMeta, this);
        }
    }
}
