/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-08-18
 */

using System;
using Falcon.Helpers.Devkit;
using JetBrains.Annotations;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Kết quả MỘT lần gọi load của MỘT ad unit — input cho <see cref="FAdApi.OnLoadResult"/>.
    /// KHÔNG lên wire trực tiếp: SDK gộp cụm (<see cref="AdLoadStatsService"/>) rồi bắn
    /// bản tin tổng <c>f_sdk_ad_load_success</c>/<c>f_sdk_ad_load_fail</c> — mediation cứ báo
    /// từng attempt, volume SDK lo.
    /// <br/>Game KHÔNG chạy multi-floor (máy yếu/3G tắt MultiCall, hoặc chỉ 1 unit) vẫn dùng
    /// param này — <see cref="floor"/> để null là xong; kết quả load của một unit đơn vẫn là
    /// kết quả load, và chính nhóm máy yếu/3G là nơi telemetry fill/timeout giá trị nhất.
    /// <br/>Thay thế MAdLog/f_sdk_mo_multiple_floor_adunit cũ (đã bị CHẶN trên server vì spam
    /// per-attempt). Bản mới cũng cắt các field double-coverage: revenue/displayed/paid đã có
    /// f_sdk_ads_data lo, display-fail đi <see cref="FAdApi.OnShowFailed"/>.
    /// </summary>
    [Serializable]
    public class AdLoadResultParam
    {
        public AdType adType;

        /// <summary>
        /// Load thành công hay thất bại — quyết định EVENT tại client (success/fail là hai id
        /// riêng nên field này không lên wire). Default <c>false</c>: quên set ở callback loaded
        /// là success bị đếm nhầm thành fail — set TƯỜNG MINH ở cả hai nhánh như doc mẫu.
        /// </summary>
        public bool success;

        [CanBeNull] public string adUnitId;

        /// <summary>
        /// BẬC giá sàn của unit — hệ số/nhãn thứ tự trong config (vd 1.5 / 3.0 / 5.0), MỘT nghĩa
        /// cho mọi mediation. Đây là thứ mediation nào cũng BIẾT THẬT, nên là trục chính để so
        /// bậc nào fill tốt hơn.
        /// <br/>⚠ ĐỪNG nhân bậc với doanh thu đo được rồi nhét vào <see cref="floor"/>: nền nhân
        /// là giá của ad vừa xem nên cùng một bậc ra số khác nhau theo máy/theo ngày, và đầu phiên
        /// ra 0 vì chưa có ad nào làm nền (đúng bài học của công thức cũ bên MAX, loader đo 22/09).
        /// <br/>Kiểu <c>double</c> chứ không float để "1.2" in lại đúng "1.2" (float box lên double
        /// là 1.2000000476… — bậc tự tách đôi trên wire). Parse từ config bằng
        /// <c>CultureInfo.InvariantCulture</c> — culture vi-VN đảo dấu thập phân, "1.2" thành 12.
        /// <br/><b>Unit KHÔNG đặt giá sàn thì để null</b> — key biến mất khỏi payload. ĐỪNG điền
        /// <c>0</c> cho gọn: vắng-key là thứ DUY NHẤT phân biệt "không đặt sàn" với "đặt sàn 0"
        /// (điều kiện loader đặt 23/09; chẩn đoán nhóm AdMob-không-đặt-floor đứng trên đúng nó).
        /// </summary>
        public double? tier;

        /// <summary>
        /// Giá sàn THẬT đã đặt cho unit — USD trên 1000 lượt hiển thị (eCPM).
        /// <br/>CHỈ mediation nào biết con số thật mới điền (AdMob tự đặt sàn từng slot nên biết);
        /// mediation không biết (MAX — giá sàn nằm trên dashboard, client chỉ thấy bậc) thì
        /// <b>để null</b>. Null ở đây nghĩa là "KHÔNG BIẾT", khác với null của <see cref="tier"/>
        /// nghĩa là "không đặt sàn".
        /// <br/>⚠ TUYỆT ĐỐI không suy ra bằng cách nhân <see cref="tier"/> với doanh thu đo được —
        /// đó là số bịa (xem ghi chú ở <see cref="tier"/>).
        /// </summary>
        public double? floor;

        /// <summary>
        /// Giá sàn eCPM (USD/1000 impression) THỰC SỰ gửi kèm request này — cột <c>ecpm_floor</c> của
        /// Databuckets <c>ad_request</c>, và từ 1.5.5 CÓ trên dòng tách <c>f_sdk_ad_load</c>.
        /// KHÔNG vào bản GỘP <c>f_sdk_ad_load_stats</c> (số đổi theo phiên, không gộp được).
        /// <br/>Khác <see cref="floor"/>: MAX multicall đặt sàn động (bậc × doanh thu nền), số này
        /// đổi theo session nên KHÔNG được làm khoá gộp cụm — nhưng đó đúng là con số đã xin.
        /// <br/>⚠ <b>KHÔNG có cách nào khai "sàn 0" lên dây BigData</b> (đính chính 28/10 — câu cũ
        /// ở đây nói "0 = unit không set sàn" là SAI với chính hành vi của SDK): số không dương bị
        /// <see cref="FAdLoadLog"/> bỏ khỏi payload từ 1.5.5, nên 0 và null ra cùng một thứ là
        /// VẮNG KEY. Muốn phân biệt "không áp sàn" với "không biết" thì đừng trông vào giá trị —
        /// chỉ gửi <see cref="tier"/> cho request THỰC SỰ được áp sàn (việc V7 phiếu Mediation).
        /// </summary>
        public double? ecpmFloor;

        /// <summary>
        /// eCPM ước tính (USD/1000 impression) mà mediation trả về sau khi load thành công, trước khi
        /// show — cột <c>estimated_ecpm</c> của Databuckets <c>ad_request</c>. null khi load fail hoặc
        /// mediation không trả giá. KHÔNG vào cụm BigData.
        /// </summary>
        public double? estimatedEcpm;

        /// <summary>Network thắng — chỉ biết khi <see cref="success"/>.</summary>
        [CanBeNull] public string networkName;

        /// <summary>
        /// Mediation của unit này ("Max" / "Admob"…). Loader 23/09 gọi đây là nút thắt: thiếu nó
        /// họ phải suy mediation bằng prefix của <c>adUnitId</c>, chỉ tách được AdMob với "phần
        /// còn lại" — mà đọc <see cref="floor"/> thì BẮT BUỘC group theo mediation.
        /// </summary>
        [CanBeNull] public string adMediation;

        /// <summary>
        /// Tên thuật toán load đang chạy — xem <see cref="AdViewParam.adAlgorithm"/>. Thuộc tính của
        /// cả đợt; truyền ở đây để dòng load nào cũng có, kể cả khi đợt chưa có mốc request.
        /// </summary>
        [CanBeNull] public string adAlgorithm;

        /// <summary>Error message của lần fail này — cụm giữ bản CUỐI, không làm khoá gộp.</summary>
        [CanBeNull] public string errorMess;

        /// <summary>
        /// Thời gian load của CHÍNH attempt này (ms) — cụm cộng dồn thành <c>totalLoadingMs</c>
        /// (cũng <c>long</c>), server chia <c>count</c> ra thời gian load trung bình. MAX đo hộ:
        /// <c>adInfo.LatencyMillis</c> (loaded) / <c>errorInfo.WaterfallInfo?.LatencyMillis</c>
        /// (fail) — hai cái đó là <c>long</c> nên gán thẳng, khỏi cast; không có số thì để 0 —
        /// cộng dồn 0 không làm bẩn tổng.
        /// </summary>
        public long loadingMs;

        /// <summary>
        /// ID của CHÍNH nhát thử load này (<c>ad_candidate_id</c> — phía personalization xin
        /// 28/10). Một đợt xin ad (<c>adViewId</c>) có NHIỀU nhát thử song song ở nhiều giá sàn, nên
        /// mã này là thứ duy nhất nối <i>"nhát thử ở sàn X"</i> với <i>"nhát đó thắng và mang về bao
        /// nhiêu"</i> khi hai nhát trùng <see cref="adUnitId"/>.
        /// <br/><b>Để null thì SDK tự cấp</b> (Guid) rồi dán luôn lên dòng impression của unit đó qua
        /// <see cref="AdUnitFactsState"/>. Caller truyền thì <b>giá trị của caller THẮNG</b> — để mọi
        /// bảng của tầng mediation dùng chung MỘT mã (vd <c>ad_uuid</c> của
        /// <c>f_sdk_ad_full_data</c>); hai bên tự cấp mã riêng là không join được dòng nào.
        /// </summary>
        [CanBeNull] public string adCandidateId;

        /// <summary>
        /// VAI của giá sàn trong flow chọn sàn — <c>base</c> · <c>base_half</c> ·
        /// <c>upgrade_initial</c> · <c>upgrade_higher</c> · <c>upgrade_fallback</c> · <c>decay</c> ·
        /// <c>decay_half</c> · <c>no_floor</c>. Kiểu CHUỖI và SDK không kiểm danh sách: bên quyết sàn
        /// thêm vai mới thì không phải chờ bản SDK.
        /// <br/>⚠ <b>Phải là vai của LÚC GỬI request này</b>, không phải vai mà Rule vừa trả về:
        /// response trả sau một kết quả load là vai của lần <b>SAU</b>. Đọc "state hiện tại" ở mốc
        /// kết quả là lệch một nhịp — đúng lỗi V6 của phiếu Mediation, chỉ đổi cột.
        /// <br/>Đây cũng là thứ duy nhất phân biệt <i>không áp sàn</i> (<c>no_floor</c>) với
        /// <i>không biết</i> (vắng key): <see cref="ecpmFloor"/> không có cách nào khai số 0.
        /// </summary>
        [CanBeNull] public string ecpmFloorType;

        /// <summary>
        /// Percentile mà giá sàn được tính ra từ đó (<c>P10</c>/<c>P25</c>/<c>P50</c>/<c>P75</c>…);
        /// vai <c>no_floor</c>/<c>decay*</c> thì để null.
        /// <br/>Không suy được từ <see cref="ecpmFloorType"/> nên phải chở riêng: <c>base</c> và
        /// <c>base_half</c> dùng CÙNG một percentile (cái sau là ×0.5), còn ngưỡng của từng vai thì
        /// bên quyết sàn cấu hình được mà không đổi tên vai.
        /// </summary>
        [CanBeNull] public string ecpmFloorPercentile;

        /// <summary>
        /// Nhát thử thứ mấy TẠI CÙNG một giá sàn — đếm từ 1, nên retry lần 2 là <c>3</c>.
        /// <br/>Vì sao caller phải khai: SDK thấy từng lời gọi <see cref="FAdApi.OnLoadResult"/> nhưng
        /// KHÔNG biết giá sàn vừa đổi hay chưa, nên không tự đếm được. Không dương = không biết, bỏ
        /// khỏi payload.
        /// </summary>
        public int? adLoadAttempt;

    }
}
