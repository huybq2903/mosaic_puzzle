/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-10-26
 */

using System;
using Falcon.Helpers.Devkit;
using UnityEngine.Scripting;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Telemetry load TÁCH — một dòng cho MỖI lần gọi load của một unit (chế độ mặc định, xem
    /// <see cref="IAdLoadLogConfig"/>). Bản gộp <see cref="FAdLoadStatsLog"/> vẫn còn nguyên cho
    /// chế độ gộp; hai chế độ loại trừ nhau theo từng lời gọi nên tổng không bị đếm đôi.
    /// <br/>Khác bản gộp ở ĐÚNG một điều có giá trị: dòng này mang <see cref="AAdLog.adViewId"/>,
    /// nên nối được load ↔ show. Van <c>AdViewRequestGate</c> phía mediation giữ MỘT view cho mỗi
    /// ĐỢT xin ad (N unit song song + mọi retry dùng chung một id), nên mọi dòng load của một đợt
    /// mang cùng id với dòng impression mà đợt ấy sinh ra.
    /// <br/>⚠ <c>adViewId</c> CÓ THỂ VẮNG: ô view của format giữ "view xin gần nhất" và không ai xoá
    /// khi ad chiếu xong, nên lần load rơi vào khoảng "ad vừa chiếu xong – đợt sau chưa mở van"
    /// (callback về muộn, mediation tự refill, banner tự refresh) sẽ KHÔNG được đóng dấu — thà vắng
    /// còn hơn dán id của ad vừa chiếu. Lúc đó nối bằng <c>adUnitId</c> như bản gộp.
    /// <br/><c>tier</c> = BẬC trong config, <c>floor</c> = GIÁ SÀN THẬT (USD) — hai trục khác nhau,
    /// cùng hợp đồng với bản gộp, đừng trộn.
    /// <br/><b>eCPM ở mốc LOAD</b> (yêu cầu phía đọc số 26/10): <c>estimatedEcpm</c> là USD/1000
    /// impression mà mediation báo lúc load — <b>MAX đưa sẵn trong <c>adInfo</c> của callback
    /// loaded</b>, nên trước bản này nó bị bỏ chứ không phải không có. Đơn vị lệch 1000 lần với
    /// <c>adRev</c> của dòng impression (<c>estimatedEcpm ≈ adRev × 1000</c>).
    /// <br/>⚠ Số không dương = KHÔNG BIẾT, không gửi (không ghi 0 thành một giá trị thật).
    /// </summary>
    [Serializable]
    public class FAdLoadLog : AAdLog
    {
        public AdType adType;

        [FKey(RemoveIfNull = true)] public string adMediation;

        [FKey(RemoveIfNull = true)] public string adUnitId;

        /// <summary>Lần gọi này có ad về hay không.</summary>
        public bool success;

        /// <summary>
        /// Thời gian của ĐÚNG lần gọi này (ms) — mediation đo, SDK không tự tính. Tên trên dây là
        /// <c>time_loading_ms</c> theo yêu cầu phía đọc số 26/10, và CHỈ có ở dòng load.
        /// </summary>
        public long timeLoadingMs;

        /// <summary>BẬC giá sàn trong config. Vắng = KHÔNG đặt giá sàn, khác hẳn "đặt sàn 0".</summary>
        [FKey(RemoveIfNull = true)] public double? tier;

        /// <summary>Giá sàn THẬT của lần gọi này (USD/1000 impression). Vắng = mediation không biết.</summary>
        [FKey(RemoveIfNull = true)] public double? floor;

        [FKey(RemoveIfNull = true)] public double? ecpmFloor;

        [FKey(RemoveIfNull = true)] public double? estimatedEcpm;

        /// <summary>
        /// Network trả ad về (applovin · unity · inmobi · admob...) — chỉ có khi
        /// <see cref="success"/>. Cùng tên với <c>AdParam.adNetwork</c> của dòng impression để
        /// join/so trực tiếp được.
        /// </summary>
        [FKey(RemoveIfNull = true)] public string adNetwork;

        /// <summary>Thuật toán load của đợt — xem <see cref="AdViewParam.adAlgorithm"/>.</summary>
        [FKey(RemoveIfNull = true)] public string adAlgorithm;

        /// <summary>
        /// Lần GỌI LOAD thứ mấy của format này TRONG PHIÊN, kể cả lần fail (đếm từ 1) — SDK đếm.
        /// </summary>
        [FKey(RemoveIfNull = true)] public int? sessionAdLoadIndex;

        /// <summary>
        /// Lần LOAD THÀNH CÔNG thứ mấy của format này TRONG PHIÊN (đếm từ 1) — SDK đếm, và
        /// <b>VẮNG trên dòng fail</b> (lần fail không phải lần thành công thứ mấy cả).
        /// <br/>Hai sổ tách nhau vì <c>sessionAdLoadIndex</c> trả lời *"gọi bao nhiêu lần"* còn
        /// <c>sessionAdLoadSuccessIndex</c> trả lời *"ad thứ mấy vào kho"* — eCPM đi theo cái thứ hai.
        /// </summary>
        [FKey(RemoveIfNull = true)] public int? sessionAdLoadSuccessIndex;

        /// <summary>Error của đúng lần gọi này — không phải "bản cuối của cụm" như bản gộp.</summary>
        [FKey(RemoveIfNull = true)] public string errorMess;

        /// <inheritdoc cref="AdLoadResultParam.adCandidateId"/>
        [FKey(RemoveIfNull = true)] public string adCandidateId;

        /// <inheritdoc cref="AdLoadResultParam.ecpmFloorType"/>
        [FKey(RemoveIfNull = true)] public string ecpmFloorType;

        /// <inheritdoc cref="AdLoadResultParam.ecpmFloorPercentile"/>
        [FKey(RemoveIfNull = true)] public string ecpmFloorPercentile;

        /// <inheritdoc cref="AdLoadResultParam.adLoadAttempt"/>
        [FKey(RemoveIfNull = true)] public int? adLoadAttempt;

        /// <summary>
        /// Số quảng cáo của format này ĐÃ CHIẾU trong phiên, tính TRƯỚC lần load này. Đây là một
        /// <b>SỐ ĐẾM</b>, nên <c>0</c> là một sự thật ("chưa chiếu lượt nào") và luôn được gửi.
        /// <br/>Bên quyết sàn dùng nó cùng <see cref="firstAdEcpm"/>/<see cref="previousAdEcpm"/> để
        /// tính sàn cho lượt sau.
        /// <br/>⚠ <b>CỐ Ý không mang tên <c>ad_index_session</c></b> như cột cùng tên của bảng bên đọc
        /// số, và cũng không mang tên <c>sessionAdImpressionIndex</c> của dòng impression: hai thứ
        /// LỆCH NHAU MỘT NHỊP. Trên dòng impression, số đó là <i>"lượt chiếu thứ mấy của CHÍNH lượt
        /// này"</i>; ở đây là <i>"đã chiếu mấy lượt TRƯỚC lần load này"</i> — cùng một quảng cáo thì
        /// dòng load mang 2 mà dòng impression mang 3. Dùng chung một tên là mọi phép
        /// <c>JOIN</c>/<c>GROUP BY</c> giữa hai bảng ghép lệch một nhịp, im lặng — đúng họ lỗi V6.
        /// <br/>Và <b>không chữa được bằng cách +1</b>: multicall giữ ad trong kho, nên unit load
        /// trước lượt N−1 vẫn có thể là cái được chiếu ở lượt N — lúc load thì KHÔNG biết mình sẽ là
        /// lượt thứ mấy. Muốn "slot kế tiếp" thì phía đọc tự cộng 1 và tự chịu sai số đó.
        /// </summary>
        public int sessionAdShownBefore;

        /// <summary>
        /// eCPM THẬT của quảng cáo được chiếu ĐẦU TIÊN trong phiên (USD/1000 impression) — bên quyết
        /// sàn dùng nó làm nền cho những lần sau. <b>SDK tự nhớ</b>, caller không truyền.
        /// <br/>Vắng khi phiên này chưa chiếu quảng cáo nào (đang load quảng cáo đầu), hoặc khi lượt
        /// đầu tiên đó không có doanh thu — giữ nguyên luật <i>không dương = không biết</i>, và KHÔNG
        /// vì thế mà đẩy lượt thứ hai lên làm "lượt đầu".
        /// </summary>
        [FKey(RemoveIfNull = true)] public double? firstAdEcpm;

        /// <summary>
        /// eCPM THẬT của quảng cáo chiếu NGAY TRƯỚC lần load này (USD/1000 impression).
        /// <b>SDK tự nhớ</b>, caller không truyền.
        /// <br/>Lượt chiếu mới luôn ĐÈ, kể cả đè bằng "không biết": giữ lại số của một lượt cũ hơn là
        /// khai sai — cột này nói <i>"lượt ngay trước"</i>, không phải <i>"lượt có giá gần nhất"</i>.
        /// </summary>
        [FKey(RemoveIfNull = true)] public double? previousAdEcpm;

        [Preserve]
        public FAdLoadLog()
        {
        }

        public FAdLoadLog(AdLoadResultParam param)
        {
            if (param == null) return;

            adType = param.adType;
            adMediation = param.adMediation;
            adUnitId = param.adUnitId;
            adAlgorithm = param.adAlgorithm;
            success = param.success;
            timeLoadingMs = param.loadingMs;
            // tier/floor không dương = KHÔNG BIẾT, không gửi. Caller điền 0 cho unit không đặt sàn
            // là chuyện ĐÃ XẢY RA (3 unit trên máy test 26/10 gửi tier = 0 mà không có ecpm_floor),
            // và 0 ở đây xoá mất phân biệt "không đặt sàn" vs "đặt sàn 0" — luật ký với loader
            // 23/09. SDK tự vệ chứ không trông vào hợp đồng (cùng lẽ như vụ floor liên tục ở 1.4.1).
            if (AdUnitFactsState.Positive(param.tier)) tier = param.tier;
            if (AdUnitFactsState.Positive(param.floor)) floor = param.floor;
            adNetwork = param.networkName;
            errorMess = param.errorMess;

            // eCPM/giá sàn KHÔNG DƯƠNG là "không biết", không phải 0: mediation trả 0 (hoặc -1) cho
            // ca không có số liệu, mà 0 trên dây là một giá trị giả — cùng luật "tier vắng ≠ 0".
            if (AdUnitFactsState.Positive(param.ecpmFloor)) ecpmFloor = param.ecpmFloor;
            if (AdUnitFactsState.Positive(param.estimatedEcpm)) estimatedEcpm = param.estimatedEcpm;

            // Chuỗi RỖNG cũng là "không biết": RemoveIfNull chỉ bỏ null, nên "" sẽ lên dây thành một
            // giá trị giả mà mọi phép group theo vai sàn đều phải tự lọc. Chặn tại cửa.
            adCandidateId = Text(param.adCandidateId);
            ecpmFloorType = Text(param.ecpmFloorType);
            ecpmFloorPercentile = Text(param.ecpmFloorPercentile);
            if (param.adLoadAttempt is > 0) adLoadAttempt = param.adLoadAttempt;
        }

        /// <summary>Chuỗi rỗng/trắng về null để <c>RemoveIfNull</c> bỏ được khỏi payload.</summary>
        private static string Text(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        public override string Event => "f_sdk_ad_load";
    }
}
