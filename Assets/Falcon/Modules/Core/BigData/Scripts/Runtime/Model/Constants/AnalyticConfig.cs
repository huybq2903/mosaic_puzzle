/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2025-06-12
 */

using System;
using Falcon.Modules.Core.RemoteConfig;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    [Serializable]
    public class AnalyticConfig : IFalconConfig
    {
        public bool fCoreAnalyticShouldNotSendLogToServer;
        public bool fCoreAnalyticTesting;
        public string fCoreAnalyticTestingSingleUrl = "https://dwhapi-v2.data4game.com/fake/event-log-v2";
        public string fCoreAnalyticTestingBatchUrl = "https://dwhapi-v2.data4game.com/batch/fake/event-log-v2";
        public float fBatchLogSendSec = 15;

        /// <summary>
        /// Quay lại từ background mà nền ngắn hơn ngưỡng này (giây) thì KHÔNG log app_open —
        /// lọc nhiễu kiểu thoát ra 2 giây copy OTP / nghe điện thoại. Đặt 0 để log mọi lần quay lại.
        /// </summary>
        public int fAppOpenMinBackgroundSec = 30;

        /// <summary>
        ///     Sau khi offer đóng, giao dịch khớp sản phẩm của offer đó trong bao nhiêu giây nữa
        ///     thì vẫn được quy về offer (luồng bấm mua → popup đóng → callback giao dịch về muộn).
        ///     Đặt 0 để tắt cửa sổ ân hạn.
        /// </summary>
        public int fOfferAttributionGraceSec = 120;

        /// <summary>
        ///     Telemetry load ad (<c>Ad.OnLoadResult</c>): <c>true</c> = TÁCH mỗi lần gọi thành một
        ///     dòng <c>f_sdk_ad_load</c> mang <c>ad_view_id</c> · <c>false</c> = GỘP cụm như cũ
        ///     (<c>f_sdk_ad_load_stats</c>).
        ///     <br/>Mặc định <c>true</c> để bật được mà KHÔNG phải cấu hình gì trên server. Gửi
        ///     <c>false</c> là van tắt: đo 22/09 thì bản per-attempt ~158 dòng/user/ngày so với
        ///     ~20 của bản gộp.
        ///     <br/>Service remote config thứ hai (<c>Core.RemoteConfigCms</c>) có khoá cùng tên,
        ///     đọc ở <c>AnalyticParamsSync</c>; luật là OR — MỘT bên <c>false</c> là gộp.
        ///     <br/>⚠ Khoá này là <b>của BigData</b>, đi qua <c>Core.RemoteConfig</c>: game không dùng
        ///     CMS (không cài <c>AnalyticParamsSync</c>) <b>vẫn tắt được</b> bằng chính khoá này, và
        ///     ngược lại — không cấu hình gì thì nó <b>TÁCH</b>, chứ không tự thành gộp.
        /// </summary>
        public bool fAdLoadLogSplit = true;

        /// <summary>
        ///     Van của phần S2 chạy ngoài main thread / lúc app sắp chết (hợp đồng đợt 3 S2 phần 2): luồng canh
        ///     <c>freeze_long</c>, bản chụp + đường gửi khẩn (cả heartbeat kèm sự cố và dòng <c>is_fatal</c>), làm mới tham số
        ///     APS giữa lượt (B5 §5.3).
        ///     <br/>Mặc định <c>false</c>: hợp đồng §11.1 mục 4 yêu cầu bật thử trên máy test (<c>is_test</c>) trước khi bật cho
        ///     mọi game. Tắt thì vẫn còn: <c>exception</c> / <c>freeze</c> / <c>memory_critical</c> / <c>prev_exit</c> thường,
        ///     ghi dấu lỗi chết (<c>prev_fatal_*</c> ở lần mở sau), heartbeat lúc xuống nền (APS vẫn được gọi lúc đó).
        /// </summary>
        public bool fAppIssueEmergencyEnabled;

        /// <summary>
        ///     Van của việc đọc tín hiệu máy native định kỳ (hợp đồng đợt 3, field "(2)" — Devkit <c>DeviceSignals</c>): nhiệt
        ///     độ / tiết kiệm pin, âm thanh máy, cấu hình máy, SDK tự đọc trạng thái quyền, bộ nhớ hệ thống. Mặc định <c>true</c>
        ///     (thư era 5 tính từ bản 1.8.0); gửi <c>false</c> để tắt không cần ra bản nếu việc đọc native (vài lời gọi IPC mỗi
        ///     ~10 giây trên main thread) gây vấn đề trên máy thật. Tắt thì các field "(2)" vắng.
        /// </summary>
        public bool fDeviceSignalsEnabled = true;
    }
}