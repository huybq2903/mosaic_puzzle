/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-09-23
 */

using System;
using System.Collections.Generic;
using Falcon.Helpers.Devkit;
using Newtonsoft.Json;
using UnityEngine.Scripting;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Telemetry load GỘP — một dòng cho mỗi (adType × mediation) mỗi lần flush, chi tiết từng ad
    /// unit nằm trong mảng <see cref="units"/>.
    /// <br/>Thay <c>f_sdk_ad_load_success</c> + <c>f_sdk_ad_load_fail</c> (loader xác nhận 23/09:
    /// không mapping/cook/counter/probe nào đọc hai event cũ nên bỏ là miễn phí phía kho).
    /// <br/>Vì sao MẢNG BẢN GHI chứ không phải nhiều map song song: map cùng khoá dễ lệch nhau
    /// (unit có ở map này, thiếu ở map kia, không có gì báo) và bên đọc phải tự ghép.
    /// <br/>Vì sao khoá là <c>u</c> (adUnitId) chứ không phải bậc: bậc là số thực, làm khoá thì
    /// dính định dạng ("1.2" vs "1.20", dấu thập phân theo culture) và hai unit cùng bậc bị gộp mất.
    /// <br/><c>tier</c> = BẬC trong config (rời rạc → LÀ khoá gộp), <c>floor</c> = GIÁ SÀN THẬT
    /// bằng USD (số đo liên tục → KHÔNG làm khoá, báo trung bình của bản ghi). Hai trục khác nhau,
    /// đừng trộn (chốt owner 23/09).
    /// <br/>⚠ <b>Không mang <c>adViewId</c></b>: đây là telemetry per-attempt của từng unit, không
    /// thuộc vòng đời một lần xem ad. Nối với vòng đời qua <c>adUnitId</c>. Cần nối load ↔ show thì
    /// dùng chế độ TÁCH (<see cref="FAdLoadLog"/>, mặc định từ 1.5.5).
    /// <br/>1.5.5 thêm <c>ecpm</c> (trung bình, trong <c>units[]</c>) và <c>adAlgorithm</c> (khoá
    /// nhóm) — hai khoá MỚI trên một event đã đăng ký, cần loader ký.
    /// <br/>1.5.7 thêm <c>floorType</c> (khoá gộp) + <c>floorPct</c> trong <c>units[]</c>.
    /// ⚠ <b>Bốn cột mới của bản TÁCH không có ở đây và sẽ không bao giờ có</b>:
    /// <c>ad_candidate_id</c> (mã riêng từng attempt — làm khoá thì cụm nở bằng số attempt, đúng vụ
    /// <c>floor</c> liên tục ở 1.4.0; làm "bản cuối" thì là một mã sai),
    /// <c>ad_load_attempt</c> (cụm đã có <c>ok</c>/<c>fail</c>), <c>first_ad_ecpm</c> và
    /// <c>previous_ad_ecpm</c> (số của PHIÊN, trung bình hoá là vô nghĩa). Muốn đo flow chọn sàn thì
    /// phải chạy chế độ TÁCH — <c>fAdLoadLogSplit = true</c>.
    /// </summary>
    [Serializable]
    public class FAdLoadStatsLog : AFalconLog
    {
        /// <summary>Một ad unit trong mảng — tên khoá VIẾT TẮT để gói tin không phình (32 unit ≈ 2,5 KB).</summary>
        [Serializable]
        public class UnitEntry
        {
            /// <summary><c>adUnitId</c>.</summary>
            [JsonProperty("u")] public string u;

            /// <summary>
            /// BẬC giá sàn trong config (hệ số 1.5 / 3.0 / 5.0) — mediation nào cũng biết thật.
            /// <b>Null thì key biến mất</b> — hợp đồng với loader 23/09: vắng = KHÔNG đặt giá sàn,
            /// khác hẳn "đặt sàn 0". Điền 0 cho gọn là xoá vĩnh viễn phân biệt này.
            /// </summary>
            [JsonProperty("tier", NullValueHandling = NullValueHandling.Ignore)]
            public double? tier;

            /// <summary>
            /// Giá sàn THẬT, USD/1000 impression — <b>TRUNG BÌNH</b> các lần load trong bản ghi
            /// (bản tin gộp, cùng khuôn với <c>okMs</c>/<c>failMs</c>; làm tròn 4 chữ số thập phân).
            /// Chỉ mediation biết con số thật mới gửi (AdMob); vắng = KHÔNG BIẾT giá thật, khác với
            /// <see cref="tier"/> vắng nghĩa là không đặt sàn.
            /// <br/>KHÔNG làm khoá gộp (sửa 1.4.1): giá này là <c>revenue × hệ số</c> nên liên tục,
            /// mỗi lần load một giá trị — làm khoá thì nhánh AdMob không gộp được dòng nào.
            /// </summary>
            [JsonProperty("floor", NullValueHandling = NullValueHandling.Ignore)]
            public double? floor;

            /// <summary>Số lần load THÀNH CÔNG đã gộp.</summary>
            [JsonProperty("ok")] public int ok;

            /// <summary>Số lần load THẤT BẠI đã gộp.</summary>
            [JsonProperty("fail")] public int fail;

            /// <summary>Tổng thời gian của các lần thành công (ms) — chia <c>ok</c> ra trung bình.</summary>
            [JsonProperty("okMs")] public long okMs;

            /// <summary>Tổng thời gian của các lần thất bại (ms) — chia <c>fail</c> ra trung bình.</summary>
            [JsonProperty("failMs")] public long failMs;

            /// <summary>Network thắng — chỉ có khi <c>ok &gt; 0</c>.</summary>
            [JsonProperty("net", NullValueHandling = NullValueHandling.Ignore)]
            public string net;

            /// <summary>Mã lỗi CUỐI của nhóm fail — không làm khoá gộp.</summary>
            [JsonProperty("err", NullValueHandling = NullValueHandling.Ignore)]
            public string err;

            /// <summary>
            /// eCPM ước tính TRUNG BÌNH của các lần thành công có số (USD/1000 impression, làm tròn
            /// 4 chữ số) — thêm 1.5.5 theo yêu cầu *"bản tin nào lấy được eCPM thì phải log eCPM"*.
            /// Vắng = không lần nào mediation báo eCPM; số không dương không vào mẫu.
            /// </summary>
            [JsonProperty("ecpm", NullValueHandling = NullValueHandling.Ignore)]
            public double? ecpm;

            /// <inheritdoc cref="AdLoadStatsState.UnitStat.ecpmFloorType"/>
            [JsonProperty("floorType", NullValueHandling = NullValueHandling.Ignore)]
            public string floorType;

            /// <inheritdoc cref="AdLoadStatsState.UnitStat.ecpmFloorPercentile"/>
            [JsonProperty("floorPct", NullValueHandling = NullValueHandling.Ignore)]
            public string floorPct;
        }

        public AdType adType;

        [FKey(RemoveIfNull = true)] public string adMediation;

        /// <summary>
        /// Thuật toán load của nhóm này — NẰM TRONG KHOÁ NHÓM (xem <see cref="AdLoadStatsState.Batch"/>),
        /// nên một dòng chỉ chứa một thuật toán.
        /// </summary>
        [FKey(RemoveIfNull = true)] public string adAlgorithm;

        public List<UnitEntry> units = new();

        [Preserve]
        public FAdLoadStatsLog()
        {
        }

        public FAdLoadStatsLog(AdLoadStatsState.Batch batch)
        {
            adType = batch.adType;
            adMediation = batch.adMediation;
            adAlgorithm = batch.adAlgorithm;
            foreach (var unit in batch.units)
                units.Add(new UnitEntry
                {
                    u = unit.adUnitId,
                    tier = unit.tier,
                    floor = unit.FloorAverage is { } avg ? Math.Round(avg, 4) : null,
                    ok = unit.ok,
                    fail = unit.fail,
                    okMs = unit.okMs,
                    failMs = unit.failMs,
                    net = unit.networkName,
                    err = unit.lastErrorMess,
                    ecpm = unit.EcpmAverage is { } ecpmAvg ? Math.Round(ecpmAvg, 4) : null,
                    floorType = unit.ecpmFloorType,
                    floorPct = unit.ecpmFloorPercentile
                });
        }

        public override string Event => "f_sdk_ad_load_stats";

        /// <summary>
        /// Bản tin TỔNG HỢP nhiều khoảnh khắc → pipeline không đóng dấu các định danh
        /// của-một-khoảnh-khắc (playTurnId, adViewId…) lên nó.
        /// </summary>
        [JsonIgnore] public override bool IsSpanRecord => true;
    }
}
