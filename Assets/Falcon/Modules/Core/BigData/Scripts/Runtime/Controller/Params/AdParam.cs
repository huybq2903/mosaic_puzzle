/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-01-08
 */
using System;
using System.Collections.Generic;
using Falcon.Helpers.Devkit;
using JetBrains.Annotations;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Tham số của mốc IMPRESSION — mốc thứ ba trong vòng đời một lần xem ad. Kế thừa
    /// <see cref="AdViewParam"/> nên <c>type</c> / <c>adWhere</c> / <c>adWhen</c>
    /// / <c>adMediation</c> chỉ khai MỘT chỗ cho cả họ; thêm field mới cho vòng đời thì khỏi phải
    /// nhớ sửa hai nơi.
    /// <br/>Khác ba mốc kia ở chỗ <c>adWhere</c>/<c>adWhen</c> là BẮT BUỘC (ràng buộc đặt ở
    /// <see cref="CorrectValues"/> của riêng lớp này, không đẩy lên cha) — impression mà không
    /// biết chiếu ở đâu thì doanh thu không quy được về chỗ nào.
    /// </summary>
    [Serializable]
    public class AdParam : AdViewParam
    {
        public AdParam()
        {
            // Giữ nếp cũ của mốc impression: chưa nhập thì là "Unknown" chứ không phải vắng mặt
            adWhere = UNKNOWN;
            adWhen = UNKNOWN;
        }

        public double adRev;

        [FKey(RemoveIfNull = true)] public string adPrecision;
        [FKey(RemoveIfNull = true)] public string adCountry;
        [FKey(RemoveIfNull = true)] public string adNetwork;
        [FKey(RemoveIfNull = true)] public int? currentLevel;
        [FKey(RemoveIfNull = true)] public bool? hasClick;
        [FKey(RemoveIfNull = true)] public double? timeShow;

        /// <summary>Tầng waterfall mà ad này thắng (§D2) — mediation adapter điền.</summary>
        [FKey(RemoveIfNull = true)] public int? waterfallPosition;

        /// <summary>
        ///     Ad unit id của mediation. Dữ liệu này đang được gửi Firebase/MMP/game-server nhưng
        ///     <b>chưa từng vào DWH</b> — thiếu nó thì không đối chiếu được doanh thu với bảng kê của
        ///     nhà cung cấp (họ báo tiền theo ad unit), và game chạy nhiều unit cùng format thì hai
        ///     unit trộn thành một con số.
        ///     <br/>Đã xin Mediation điền ở mốc impression (việc <b>V7</b> của
        ///     <c>Request-Mediation-AdEventV2.md</c>). Phía này dọn sẵn từ 19/10: <see cref="BannerKey"/>
        ///     đã có <c>adUnitId</c> trong khoá, nên ngày giá trị lên wire là dòng gộp mang được luôn —
        ///     thêm khoá SAU mới là thứ sinh ra ô trống im lặng (§4i).
        /// </summary>
        [FKey(RemoveIfNull = true)] public string adUnitId;

        /// <summary>
        ///     Quảng cáo TOÀN MÀN vừa đóng trước dòng này, và bao lâu kể từ lúc đóng — chỉ có trên
        ///     dòng của họ DÍNH MÀN (native/MREC/collapsible), do SDK tự đóng dấu, game không truyền.
        ///     <br/>Trả lời câu của owner 19/10: *"native diễn sau loại ad nào thì hiệu quả hơn"* —
        ///     break-by theo cột này, nên nó là CỘT THẬT chứ không đẩy vào blob.
        ///     <br/>⚠ Phải đọc KÈM <see cref="prevFullScreenAdSec"/>: một mình cái format thì native
        ///     hiện 40 phút sau vẫn bị gán cho quảng cáo từ đời nào. Client KHÔNG cắt ngưỡng —
        ///     ngưỡng là câu hỏi phân tích.
        ///     <br/>Vắng mặt = phiên này chưa có quảng cáo toàn màn nào đóng (sổ không sống qua phiên).
        ///     <br/>KHÔNG có trên dòng gộp cụm (banner): không cộng được, không nằm trong khoá — §4i.
        /// </summary>
        [FKey(RemoveIfNull = true)] public AdType? prevFullScreenAdFormat;

        /// <inheritdoc cref="prevFullScreenAdFormat"/>
        [FKey(RemoveIfNull = true)] public int? prevFullScreenAdSec;

        /// <summary>Tên instance của network trong waterfall (IronSource: adInfo.InstanceName).</summary>
        [FKey(RemoveIfNull = true)] public string adInstanceName;

        /// <summary>
        ///     Chuỗi format THÔ do mediation trả về, giữ nguyên không map.
        ///     Lý do: mediation map format → AdType bằng switch có nhánh default, và nhánh đó
        ///     <b>đoán</b>: IronSource gán mọi format lạ thành <c>AppOpen</c>
        ///     (<c>FalconIronSourceService</c> + bản sao ở <c>MultiCallIronSourceProviderParent</c>,
        ///     chỉ nhận đúng ba chuỗi <c>banner</c>/<c>interstitial</c>/<c>rewarded_video</c>). Rác
        ///     vì thế không rơi vào rổ "không biết" mà rơi vào một format THẬT có doanh thu thật.
        ///     Có chuỗi gốc thì server phát hiện được lệch thay vì tin nhầm số liệu.
        ///     <br/>⚠ Rà 19/10: field này hiện CHỈ được điền trên đường <b>Max</b> và <b>Admob
        ///     bridge</b> — đường IronSource không điền, nên đúng chỗ dễ sai nhất lại là chỗ không
        ///     đo được. Chủ động HOÃN (owner chốt 19/10: chưa thấy vấn đề thật, đội Mediation đang
        ///     việc khác); lúc làm thì hai việc: nhánh default thôi đoán, và mọi provider điền field
        ///     này.
        /// </summary>
        [FKey(RemoveIfNull = true)] public string adFormatRaw;

        /// <summary>
        /// Giá sàn eCPM (USD/1000 impression) của request đã mang về ad đang show — SDK lấy từ sổ
        /// theo unit. null = không biết.
        /// <br/>⚠ <b>Lịch sử ba nhịp, đọc kỹ trước khi đổi</b>: 1.5.5 mở lên dây → 26/10 <b>rút lại</b>
        /// cùng lúc với <see cref="estimatedEcpm"/> ("dòng impression đã có <see cref="adRev"/>, chở
        /// thêm eCPM là hai lần một thứ") → <b>1.5.7 mở lại</b> vì lý do rút **không áp dụng cho giá
        /// sàn**: sàn là CẤU HÌNH của request, không phải trục tiền, nên nó không trùng
        /// <see cref="adRev"/> cái gì. Bên đọc số xin đúng cột này trên dòng impression
        /// (<c>ecpm_floor</c> = *"floor của candidate được chọn để show"*).
        /// <br/><see cref="estimatedEcpm"/> thì VẪN không lên dây — cái đó mới là trục tiền, và nó
        /// bằng <see cref="adRev"/> × 1000 nên phía đọc tự nhân được (owner chốt lại 29/10).
        /// <br/>Khác <see cref="floor"/>: <c>floor</c> là sàn thật ĐÃ ĐẶT của unit (cấu hình của
        /// unit), còn cái này là sàn ĐÃ GỬI KÈM request đã thắng — multicall đặt sàn động nên hai số
        /// lệch nhau.
        /// </summary>
        [FKey(RemoveIfNull = true)] public double? ecpmFloor;

        /// <summary>
        /// eCPM ước tính (USD/1000 impression) của ad đang show, theo giá mediation trả lúc load —
        /// chỉ cho Databuckets (<c>estimated_ecpm</c> của <c>ad_impression</c>), KHÔNG lên
        /// f_sdk_ads_data.
        /// <br/>⚠ <b>Cố ý không lên dây, chốt lại 29/10</b> khi bên đọc số xin lần hai: trên dòng
        /// impression nó <b>bằng <see cref="adRev"/> × 1000</b>, nên phía đọc tự nhân là xong — chở
        /// thêm một cột chỉ để nhân 1000 là mời một bản ghi lệch đơn vị vào kho. Số có giá trị riêng
        /// ở mốc LOAD (nơi chưa có đồng doanh thu nào): xem <c>FAdLoadLog.estimatedEcpm</c>.
        /// </summary>
        [FKey(Ignore = true)] public double? estimatedEcpm;

        /// <summary>
        /// BẬC giá sàn trong config của unit đang chiếu (hệ số 1.5 / 3.0 / 5.0) — SDK lấy từ sổ theo
        /// unit, vì mediation chỉ nói bậc ở mốc load. Vắng = không đặt giá sàn, KHÁC "đặt sàn 0".
        /// <br/>Đây KHÔNG phải trục tiền (nên không trùng <see cref="adRev"/>): nó là cấu hình, dùng
        /// để hỏi *"bậc nào fill tốt / đáng tiền"*.
        /// </summary>
        [FKey(RemoveIfNull = true)] public double? tier;

        /// <summary>
        /// GIÁ SÀN THẬT đã đặt của unit đang chiếu (USD/1000 impression) — SDK lấy từ sổ theo unit.
        /// Cấu hình, không phải doanh thu.
        /// </summary>
        [FKey(RemoveIfNull = true)] public double? floor;

        /// <summary>
        /// ID của nhát thử load đã mang về CHÍNH quảng cáo này (<c>ad_candidate_id</c>) — SDK lấy từ
        /// sổ theo unit, caller tự khai thì lời khai thắng.
        /// <br/>Có nó trên cả dòng load và dòng impression là join được <i>từng nhát thử</i> với
        /// <i>tiền thật nó mang về</i>, nên KHÔNG cần chở eCPM ước tính sang dòng impression (chốt
        /// 26/10: một trục tiền mỗi dòng).
        /// <br/>Vắng khi lần load của quảng cáo này không đi qua <see cref="FAdApi.OnLoadResult"/>
        /// (nhánh mediation chưa khai, hoặc ad còn trong kho từ phiên trước).
        /// </summary>
        [FKey(RemoveIfNull = true)] public string adCandidateId;

        /// <summary>
        /// VAI của giá sàn mà candidate được chiếu đã dùng — SDK lấy từ sổ theo unit.
        /// Xem <see cref="AdLoadResultParam.ecpmFloorType"/>.
        /// </summary>
        [FKey(RemoveIfNull = true)] public string ecpmFloorType;

        /// <summary>
        /// Percentile nguồn của giá sàn đó — SDK lấy từ sổ theo unit.
        /// Xem <see cref="AdLoadResultParam.ecpmFloorPercentile"/>.
        /// </summary>
        [FKey(RemoveIfNull = true)] public string ecpmFloorPercentile;

        public override void CorrectValues()
        {
            base.CorrectValues();
            adWhere = CheckNonBlank(adWhere, nameof(adWhere));
            adWhen = CheckNonBlank(adWhen, nameof(adWhen));
            currentLevel = CheckNumberNonNegative(currentLevel, nameof(currentLevel));
            timeShow = CheckNumberNonNegative(timeShow, nameof(timeShow));
            adRev = CheckNumberNonNegative(adRev, nameof(adRev));

            // Ba cột GIÁ: số không dương = KHÔNG BIẾT, về null để biến mất khỏi payload. SDK tự lấy
            // từ sổ theo unit thì đã lọc rồi, nhưng field là public nên caller set 0 được — mà 0 ở đây
            // phá đúng câu hợp đồng "không có cách nào khai sàn 0" (§5.11f-1), và module mediation còn
            // dùng -100 làm cờ không-biết. Chặn ở cửa, đừng trông vào caller.
            ecpmFloor = Floor(ecpmFloor);
            tier = Floor(tier);
            floor = Floor(floor);
        }

        /// <inheritdoc cref="AdUnitFactsState.Positive"/>
        private static double? Floor(double? value)
        {
            return AdUnitFactsState.Positive(value) ? value : null;
        }
    }

    [Serializable]
    public class OldCodeSupportAdParam : AdParam
    {
        // extraMeta hoisted lên AdViewParam (base cả vòng đời ad) khi Ad.OnRequested/Shown/Closed
        // mở tham số này — con không khai lại (bài học FKeyService hai field cùng tên)
    }
}