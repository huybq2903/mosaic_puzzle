/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-09-23
 */

using System.Collections.Generic;
using Falcon.Helpers.Devkit;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Kho gộp telemetry load — v2 (chốt với loader 23/09, thay hai event
    /// <c>f_sdk_ad_load_success</c>/<c>_fail</c> cũ).
    /// <br/>Khác bản cũ ở HÌNH DẠNG ĐẦU RA chứ không ở triết lý: vẫn gộp per (unit × bậc × giá sàn), nhưng
    /// thành-công và thất-bại nằm CHUNG một bản ghi, và cả nhóm unit của một
    /// (adType × mediation) ra MỘT dòng mang mảng <c>units[]</c>. Đo 22/09: bản cũ đẻ ~158
    /// dòng/user/ngày và <c>ad_load_success</c> nén được 1,02 lần/dòng (tức không nén gì) vì mỗi
    /// tổ hợp là một dòng; gộp mảng đưa về ~20 dòng/user/ngày.
    /// <br/><b>network/lastError KHÔNG làm khoá</b> — chúng là "bản cuối của cụm". Đưa vào khoá là
    /// message lạ tách cụm vô hạn, đúng cái bẫy đã tránh ở bản cũ.
    /// <br/><b>tier vắng ≠ tier 0</b> (điều kiện loader đặt): unit không đặt giá sàn thì
    /// <c>tier</c> để null và key biến mất khỏi payload — đó là thứ duy nhất phân biệt được
    /// "không đặt sàn" với "đặt sàn 0", và là căn cứ của chẩn đoán nhóm AdMob-không-đặt-floor.
    /// <br/><b>Khoá gộp chỉ chứa thứ RỜI RẠC</b> (loại ad × mediation × unit × <c>tier</c>).
    /// <c>floor</c> (giá sàn thật, USD) là SỐ ĐO nên nằm trong ruột bản ghi, cộng dồn rồi báo
    /// TRUNG BÌNH — cùng khuôn với <c>okMs</c>/<c>failMs</c>.
    /// <br/>1.4.1 sửa đúng chỗ này (owner bắt 23/09): 1.4.0 để <c>floor</c> trong khoá, mà AdMob
    /// tính <c>floor = revenue × hệ số × 1000</c> với <c>revenue</c> là giá của ad vừa load — số
    /// thực LIÊN TỤC, mỗi attempt một giá trị. Nhánh AdMob vì thế KHÔNG gộp được dòng nào (mất
    /// đúng cái hố volume mà v2 sinh ra để lấp), lại còn làm đầy trần 256 khoá nên cụm của MAX bị
    /// xả sớm theo. SDK không được sập chỉ vì caller truyền vào một số đo thay đổi liên tục — tự
    /// vệ ở đây, đừng trông vào hợp đồng.
    /// <br/>Pure class — không IO/DI/khoá; <see cref="AdLoadStatsService"/> lo thread-safety.
    /// </summary>
    public class AdLoadStatsState
    {
        /// <summary>Một unit đã gộp — thành một phần tử của mảng <c>units[]</c>.</summary>
        public class UnitStat
        {
            public AdType adType;
            public string adMediation;
            public string adUnitId;
            public string adAlgorithm;
            public double? tier;

            /// <summary>
            /// VAI của giá sàn — <b>LÀ khoá gộp</b> (rời rạc, số lượng có hạn). Không làm khoá thì
            /// bậc <c>base</c> và <c>upgrade_higher</c> của cùng một unit gộp vào một dòng, và chính
            /// câu hỏi mà cột này sinh ra để trả lời — <i>vai nào fill tốt hơn</i> — không còn trả lời
            /// được: đúng vết sẹo của <c>adAlgorithm</c> ở 1.5.5.
            /// </summary>
            public string ecpmFloorType;

            /// <summary>
            /// Percentile nguồn của giá sàn — <b>KHÔNG</b> làm khoá, giữ bản cuối của cụm. Nó gần như
            /// được quyết bởi <see cref="ecpmFloorType"/> (vai nào lấy percentile nào là cấu hình của
            /// bên quyết sàn), nên trong một cụm đã cùng vai thì nó là hằng — thêm vào khoá chỉ nhân
            /// số khoá lên mà không chia nhỏ thêm được gì.
            /// </summary>
            public string ecpmFloorPercentile;

            public int ok;
            public int fail;
            public long okMs;
            public long failMs;
            public string networkName;
            public string lastErrorMess;

            /// <summary>Tổng giá sàn thật của những lần CÓ giá — chia <see cref="floorSamples"/> ra trung bình.</summary>
            public double floorSum;

            /// <summary>Số lần có giá sàn thật (mediation không biết giá thì không tính vào mẫu).</summary>
            public int floorSamples;

            /// <summary>Giá sàn thật TRUNG BÌNH của bản ghi; null khi không lần nào có giá.</summary>
            public double? FloorAverage => floorSamples > 0 ? floorSum / floorSamples : (double?)null;

            /// <summary>
            /// Tổng eCPM ước tính của những lần CÓ giá (yêu cầu phía đọc số 26/10: bản tin nào lấy
            /// được eCPM thì phải log) — chia <see cref="ecpmSamples"/> ra trung bình.
            /// </summary>
            public double ecpmSum;

            /// <summary>Số lần có eCPM; mediation không báo (hoặc báo ≤ 0) thì không vào mẫu.</summary>
            public int ecpmSamples;

            /// <summary>eCPM ước tính TRUNG BÌNH (USD/1000 impression); null khi không lần nào có.</summary>
            public double? EcpmAverage => ecpmSamples > 0 ? ecpmSum / ecpmSamples : (double?)null;
        }

        /// <summary>
        /// Một dòng log: nhóm unit cùng (adType × mediation × adAlgorithm), đã chia lô theo trần.
        /// <br/><c>adAlgorithm</c> VÀO KHOÁ NHÓM chứ không phải "bản cuối thắng": nó là thuộc tính
        /// của đợt, hai thuật toán khác nhau mà gộp một dòng thì chính câu hỏi *"thuật toán nào load
        /// tốt hơn"* không trả lời được — đúng vết sẹo `network/lastError` không được làm khoá.
        /// </summary>
        public class Batch
        {
            public AdType adType;
            public string adMediation;
            public string adAlgorithm;
            public List<UnitStat> units = new();
        }

        private readonly Dictionary<
                (AdType type, string mediation, string algorithm, string unit, double? tier, string floorType),
                UnitStat> _entries
            = new();

        public int Count => _entries.Count;

        /// <inheritdoc cref="AdUnitFactsState.Positive"/>
        private static bool Positive(double? value) => AdUnitFactsState.Positive(value);

        public void Record(AdLoadResultParam param)
        {
            if (param == null) return;

            // tier không dương = KHÔNG ĐẶT SÀN ⇒ chuẩn hoá về null TRƯỚC khi làm khoá, nếu không thì
            // "không đặt sàn" (null) và "caller điền 0" thành hai nhóm khác nhau cho cùng một unit,
            // và khoá `tier` lại lên dây với giá trị 0 (án máy test 26/10).
            var tier = Positive(param.tier) ? param.tier : null;
            // Chuỗi rỗng và null phải RA CÙNG MỘT KHOÁ: hai nhánh mediation, một nhánh để null một
            // nhánh để "", là cùng một sự thật mà thành hai dòng không cộng được với nhau.
            var floorType = string.IsNullOrWhiteSpace(param.ecpmFloorType) ? null : param.ecpmFloorType;
            var key = (param.adType, param.adMediation, param.adAlgorithm, param.adUnitId, tier, floorType);
            if (!_entries.TryGetValue(key, out var entry))
                _entries[key] = entry = new UnitStat
                {
                    adType = param.adType,
                    adMediation = param.adMediation,
                    adAlgorithm = param.adAlgorithm,
                    adUnitId = param.adUnitId,
                    tier = tier,
                    ecpmFloorType = floorType
                };

            if (!string.IsNullOrWhiteSpace(param.ecpmFloorPercentile))
                entry.ecpmFloorPercentile = param.ecpmFloorPercentile;

            if (Positive(param.floor))
            {
                entry.floorSum += param.floor.Value;
                entry.floorSamples++;
            }

            // eCPM: chỉ cộng số DƯƠNG — 0/-1 của mediation nghĩa là "không biết", cộng vào là kéo
            // trung bình xuống bằng một giá trị giả.
            if (Positive(param.estimatedEcpm))
            {
                entry.ecpmSum += param.estimatedEcpm.Value;
                entry.ecpmSamples++;
            }

            if (param.success)
            {
                entry.ok++;
                entry.okMs += param.loadingMs;
                // network chỉ biết khi thắng; giữ bản cuối, không ghi đè bằng null của lần fail
                if (param.networkName != null) entry.networkName = param.networkName;
            }
            else
            {
                entry.fail++;
                entry.failMs += param.loadingMs;
                if (param.errorMess != null) entry.lastErrorMess = param.errorMess;
            }
        }

        /// <summary>
        /// Rút toàn bộ và làm sạch kho, chia thành các lô ≤ <paramref name="maxUnitsPerBatch"/>
        /// phần tử.
        /// <br/>Vượt trần thì CHIA LÔ chứ không cắt bỏ (chốt owner 23/09): mọi số trong bản tin đều
        /// cộng được, nên tách làm nhiều dòng cho tổng y hệt — cắt bỏ là mất dữ liệu thật.
        /// </summary>
        public List<Batch> TakeAll(int maxUnitsPerBatch)
        {
            if (maxUnitsPerBatch <= 0) maxUnitsPerBatch = int.MaxValue;

            var batches = new List<Batch>();
            var openByGroup = new Dictionary<(AdType, string, string), Batch>();

            foreach (var entry in _entries.Values)
            {
                var groupKey = (entry.adType, entry.adMediation, entry.adAlgorithm);
                if (!openByGroup.TryGetValue(groupKey, out var batch) || batch.units.Count >= maxUnitsPerBatch)
                {
                    batch = new Batch
                    {
                        adType = entry.adType, adMediation = entry.adMediation, adAlgorithm = entry.adAlgorithm
                    };
                    openByGroup[groupKey] = batch;
                    batches.Add(batch);
                }

                batch.units.Add(entry);
            }

            _entries.Clear();
            return batches;
        }
    }
}
