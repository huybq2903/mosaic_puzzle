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
    /// Chọn HÌNH DẠNG của telemetry load (<see cref="FAdApi.OnLoadResult"/>) — hai chế độ loại trừ
    /// nhau, mỗi lần gọi đi đúng MỘT đường nên không có chuyện đếm đôi:
    /// <list type="bullet">
    /// <item><b>tách</b> (<c>GroupAdLoadLog == false</c>, MẶC ĐỊNH): mỗi lần gọi load một dòng
    /// <c>f_sdk_ad_load</c> mang <c>adViewId</c> → nối được load ↔ show của cùng đợt xin ad.</item>
    /// <item><b>gộp</b> (<c>true</c>): giữ nguyên đường cũ — gộp cụm trong
    /// <see cref="AdLoadStatsService"/> rồi xả thành <c>f_sdk_ad_load_stats</c>, chỉ để thống kê.</item>
    /// </list>
    /// <b>Giá của chế độ tách</b> (đo 22/09): bản per-attempt cũ đẻ ~158 dòng/user/ngày, bản gộp kéo
    /// về ~20. Mặc định tách tức là stream này ×8 — đó là lựa chọn của owner để không phải cấu hình
    /// gì trên server trước khi bật, và cái van tắt bên dưới là đường lùi.
    /// <br/><b>Volume phụ thuộc game nào đang chạy SDK này, không phải phụ thuộc chế độ.</b> Số đo
    /// fleet nằm ở phía kho (nội bộ, không ghi vào đây). Luật vận hành rút ra:
    /// <b>⚠ TRƯỚC khi một game lớn nâng lên bản có chế độ tách, phải tắt tách cho game đó</b>
    /// (<c>fAdLoadLogSplit = false</c> qua <c>Core.RemoteConfig</c>) rồi mới bật lại nếu thật cần
    /// nối load ↔ show. Mặc định tách là để game nhỏ/đang tích hợp có dữ liệu ngay mà không phải
    /// cấu hình gì; nó KHÔNG phải lời khẳng định "mọi game bật tách đều chịu được".
    /// <br/>⚠ <b>KHÔNG có chuyện "thiếu AnalyticParamsSync thì tự thành gộp"</b> (hiểu nhầm thật,
    /// 26/10). Khoá <c>fAdLoadLogSplit</c> nằm trong <b>BigData</b> và đọc qua <c>Core.RemoteConfig</c>,
    /// còn <see cref="ReserveAdLoadLogConfig"/> thì LUÔN có cùng BigData — nên game không cài
    /// AnalyticParamsSync (không dùng CMS) vẫn <b>TÁCH</b>, chỉ là mất bớt một cửa để tắt. Muốn một
    /// game chạy gộp thì <b>gửi <c>fAdLoadLogSplit = false</c> qua <c>Core.RemoteConfig</c> của chính
    /// game đó</b> — đường này không cần CMS. Đó là việc CẤU HÌNH, không phải hệ quả tự nhiên.
    /// <br/><b>Luật OR của hai service remote config</b> (chốt 26/10): CHỈ CẦN MỘT bên gửi
    /// <c>false</c> là về gộp ngay; khoá VẮNG MẶT tính là <c>true</c> (tách). Bản cơ bản
    /// <see cref="ReserveAdLoadLogConfig"/> chỉ biết service <c>Core.RemoteConfig</c>; module
    /// <c>AnalyticParamsSync</c> gắn <c>[Primary]</c> để thêm nửa <c>Core.RemoteConfigCms</c>.
    /// <br/>⚠ Luật DI (xem <c>DirectParamRequirement</c>): bản cơ bản KHÔNG được gắn
    /// <c>[Primary]</c>, và nhiều impl mà không cái nào <c>[Primary]</c> thì container NÉM. Game
    /// muốn tự ghi đè phải dùng <c>[Primary(1)]</c> (ưu tiên cao hơn AnalyticParamsSync).
    /// </summary>
    public interface IAdLoadLogConfig : IMySingleton
    {
        /// <summary>true = gộp cụm như cũ · false = tách mỗi lần gọi một dòng.</summary>
        bool GroupAdLoadLog { get; }
    }
}
