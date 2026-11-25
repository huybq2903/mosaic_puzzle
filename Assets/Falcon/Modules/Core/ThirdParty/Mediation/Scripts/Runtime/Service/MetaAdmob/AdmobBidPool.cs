using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Falcon.Admob
{
    /// <summary>
    /// Tập các <see cref="AdmobBidAd"/> của CÙNG một format (interstitial hoặc rewarded):
    ///
    ///  - 1 slot "default" (BƯỚC B): ad AdMob xin không set giá sàn -> giá AdMob tự nhiên. Ad unit lấy từ
    ///    <see cref="AdmobMediationConfig"/> — ĐÂY LÀ THỨ DUY NHẤT NGƯỜI DÙNG PHẢI ĐIỀN.
    ///  - Các slot "floor" (BƯỚC C), nhóm theo BẬC của mediation ngoài: bậc k (ad unit MAX thứ k) có
    ///    nhóm ad unit AdMob riêng trong remote config, slot thứ i của bậc đó mang key
    ///    <c>floor#k#i</c> và xin ad với giá sàn = revenue mà ad MAX bậc k vừa trả về. Nhờ tách key
    ///    theo bậc, max2 load xong CHỈ đụng nhóm của bậc 2 — nhóm bậc khác giữ nguyên ad lẫn mức sàn.
    ///    Config rỗng -> đúng 1 slot floor dùng lại ad unit bước B, hệ số 1.0.
    ///
    /// Mỗi slot là một KHO ad của cùng một ad unit: ad load về được cất thêm chứ không thay ad cũ, lúc
    /// show mới chọn ad đắt nhất (xem <see cref="AdmobBidAd"/>). Slot KHÔNG bao giờ bỏ qua một lần xin:
    /// giá sàn chỉ là mức tối thiểu, nên bậc nào cũng có thể mang về ad đắt hơn ad đang giữ.
    ///
    /// Pool không tự biết ad unit: mọi ad unit id đều do tầng trên truyền xuống theo từng lời gọi, nhờ
    /// vậy remote config đổi giữa phiên vẫn có hiệu lực ngay.
    ///
    /// Lúc show, <see cref="Best"/> trả slot có giá cao nhất trong các slot ĐANG có ad sẵn sàng.
    /// </summary>
    public class AdmobBidPool
    {
        /// <summary>Key của slot bước B. Không trùng được với key slot bước C (có tiền tố riêng).</summary>
        public const string DefaultKey = "__default__";

        private const string _FloorKeyPrefix = "floor#";

        private readonly Dictionary<string, AdmobBidAd> _slots = new Dictionary<string, AdmobBidAd>();
        private readonly Func<string, string, bool, AdmobBidAd> _factory;

        // Số slot đã từng tạo cho mỗi BẬC mediation ngoài — để dọn slot mồ côi khi remote config rút
        // ngắn nhóm ad unit của bậc đó.
        private readonly Dictionary<int, int> _floorSlotCountByTier = new Dictionary<int, int>();

        // Thác giá bước C đang chạy của mỗi BẬC mediation ngoài. Mỗi bậc tối đa một thác.
        private readonly Dictionary<int, FloorCascade> _cascadeByTier = new Dictionary<int, FloorCascade>();
        private int _nextCascadeId = 1;

        private sealed class FloorCascade
        {
            public int id;                          // mã lượt, gắn vào mọi request của thác
            public double revenueUsd;               // revenue MAX làm gốc giá sàn
            public List<AdmobFloorVariant> variants;
            public List<int> steps;                 // index variant theo multiplier giảm dần, mỗi bước 1 ad unit
            public int step;                        // bước đang chạy
        }

        /// <param name="factory">(key, adUnitId, isDefaultSlot) -> slot mới.</param>
        public AdmobBidPool(Func<string, string, bool, AdmobBidAd> factory)
        {
            _factory = factory;
        }

        /// <summary>BƯỚC B: nạp ad AdMob "giá mặc định", không set sàn.</summary>
        public void LoadDefault(string adUnitId)
        {
            if (string.IsNullOrEmpty(adUnitId)) return;

            GetOrCreate(DefaultKey, adUnitId, true).Request(0d);
        }

        /// <summary>
        /// BƯỚC C: ad unit MAX của bậc <paramref name="tierIndex"/> vừa load xong với giá
        /// <paramref name="externalRevenueUsd"/> -> xin AdMob theo THÁC GIÁ, giá sàn =
        /// <paramref name="externalRevenueUsd"/> × hệ số của biến thể:
        ///  - xin LẦN LƯỢT từng ad unit, multiplier CAO NHẤT trước (cùng multiplier thì theo thứ tự config),
        ///    mỗi lúc chỉ một request bay;
        ///  - load thành công -> DỪNG;
        ///  - fail -> xin ad unit kế tiếp (multiplier bằng hoặc thấp hơn);
        ///  - ad unit cuối vẫn fail -> dừng, chờ lần MAX load kế tiếp.
        ///
        /// CHỈ đụng tới các slot của ĐÚNG bậc này: bậc khác giữ nguyên ad và mức sàn của chúng. Lần gọi
        /// mới cho cùng bậc THAY thác giá cũ đang chạy (giá MAX mới hơn) — kết quả muộn của thác cũ bị bỏ.
        /// </summary>
        /// <param name="tierIndex">
        /// Bậc của ad unit mediation ngoài vừa load (0 = mức default của MAX). Slot của bậc này mang key
        /// riêng nên hai bậc không bao giờ ghi đè lên nhau.
        /// </param>
        /// <param name="externalRevenueUsd">
        /// Doanh thu MỘT impression theo USD, KHÔNG phải eCPM. Giữ nguyên thang này để so trực tiếp với
        /// <see cref="AdmobBidAd.Revenue"/>; việc quy sang eCPM/cent cho AdMob nằm trong AdmobBidAd.
        /// </param>
        /// <param name="variants">
        /// Nhóm ad unit của bậc này, xem <see cref="AdmobMetaAdsUnitConfig.BuildFloorVariants"/>. Thứ tự
        /// của list quyết định key slot -> đọc lại config mỗi lần gọi là an toàn.
        /// </param>
        public void RequestFloor(int tierIndex, double externalRevenueUsd, IList<AdmobFloorVariant> variants)
        {
            if (externalRevenueUsd <= 0d || variants == null) return;

            PruneFloorSlots(tierIndex, variants.Count);

            // Giữ index gốc trong variants làm key slot (floor#k#i) để key ổn định giữa các lần gọi.
            var indices = new List<int>();
            for (var i = 0; i < variants.Count; i++)
            {
                if (!string.IsNullOrEmpty(variants[i].adUnitId)) indices.Add(i);
            }

            // Sắp multiplier giảm dần; OrderBy của LINQ ổn định nên cùng mức thì giữ thứ tự config.
            var steps = indices.OrderByDescending(i => variants[i].multiplier).ToList();

            if (steps.Count == 0)
            {
                _cascadeByTier.Remove(tierIndex);
                return;
            }

            var cascade = new FloorCascade
            {
                id = _nextCascadeId++,
                revenueUsd = externalRevenueUsd,
                variants = new List<AdmobFloorVariant>(variants),
                steps = steps
            };
            _cascadeByTier[tierIndex] = cascade;
            RunCascadeStep(tierIndex, cascade);
        }

        private void RunCascadeStep(int tierIndex, FloorCascade cascade)
        {
            var i = cascade.steps[cascade.step];
            var variant = cascade.variants[i];
            //tier đi qua decimal chứ không cast thẳng float->double: 1.5f giãn thành
            //1.5 nhưng 1.2f thành 1.2000000476837158, mà hợp đồng cấm đúng ca đó
            //("float box lên double… tier tự tách đôi trên wire"). Decimal lấy đúng 7 chữ số
            //có nghĩa đã ghi trong remote config nên số in lại khớp config.
            GetOrCreate(FloorKey(tierIndex, i), variant.adUnitId, false)
                .Request(cascade.revenueUsd * variant.multiplier, (double)(decimal)variant.multiplier,
                    cascade.id);
        }

        private void OnSlotLoadFinished(AdmobBidAd slot, int requestId, bool success)
        {
            if (requestId == 0) return;

            // Tìm thác đang chạy sở hữu lượt này; không thấy = thác đã xong hoặc đã bị thác mới thay.
            var tierIndex = -1;
            FloorCascade cascade = null;
            foreach (var pair in _cascadeByTier)
            {
                if (pair.Value.id != requestId) continue;
                tierIndex = pair.Key;
                cascade = pair.Value;
                break;
            }

            if (cascade == null) return;

            if (success)
            {
                _cascadeByTier.Remove(tierIndex);
                return;
            }

            cascade.step++;
            if (cascade.step >= cascade.steps.Count)
            {
                // Ad unit cuối (multiplier thấp nhất) cũng fail -> dừng.
                _cascadeByTier.Remove(tierIndex);
                return;
            }

            RunCascadeStep(tierIndex, cascade);
        }

        /// <summary>Key slot bước C thứ <paramref name="variantIndex"/> trong nhóm của bậc <paramref name="tierIndex"/>.</summary>
        public static string FloorKey(int tierIndex, int variantIndex)
            => _FloorKeyPrefix + tierIndex + "#" + variantIndex;

        /// <summary>Slot có giá cao nhất trong các slot đang có ad sẵn sàng; null nếu không có.</summary>
        public AdmobBidAd Best()
        {
            AdmobBidAd best = null;
            foreach (var slot in _slots.Values)
            {
                if (!slot.IsReady) continue;
                if (best == null || slot.Revenue > best.Revenue) best = slot;
            }

            return best;
        }

        /// <summary>Giá tốt nhất AdMob đang cầm; <see cref="AdmobBidAd.NoAd"/> nếu không có ad nào.</summary>
        public double BestRevenue()
        {
            var best = Best();
            return best == null ? AdmobBidAd.NoAd : best.Revenue;
        }

        public bool HasAd() => Best() != null;

        /// <summary>
        /// Gom response id của MỌI ad đang nằm trong kho của MỌI slot mà chưa từng khai báo thua, rồi đánh
        /// dấu chúng — xem <see cref="AdmobBidAd.ClaimLostResponseIds"/>.
        /// </summary>
        /// <param name="loadedBefore">Chỉ lấy ad load xong TRƯỚC mốc này (null = lấy hết).</param>
        public void ClaimLostResponseIds(List<string> into, DateTime? loadedBefore = null)
        {
            foreach (var slot in _slots.Values) slot.ClaimLostResponseIds(into, loadedBefore);
        }

        /// <summary>
        /// Dump toàn bộ slot đang có (kể cả slot rỗng) kèm revenue đang lưu trong cache, đánh dấu slot
        /// thắng giá. Chỉ phục vụ log lúc show.
        /// </summary>
        public string Describe()
        {
            if (_slots.Count == 0) return "    (chưa có slot nào — AdMob chưa load lần nào)";

            var best = Best();
            var sb = new StringBuilder();
            foreach (var slot in _slots.Values)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("    ").Append(slot.Describe());
                if (ReferenceEquals(slot, best)) sb.Append("   <== ADMOB BEST");
            }

            return sb.ToString();
        }

        public void Dispose()
        {
            foreach (var slot in _slots.Values) slot.Dispose();
            _slots.Clear();
            _floorSlotCountByTier.Clear();
            _cascadeByTier.Clear();
        }

        // Remote config rút ngắn nhóm ad unit của một bậc -> các slot không còn tồn tại phải biến mất,
        // nếu không slot mồ côi vẫn tham gia so giá trên ad unit đã bị gỡ khỏi config.
        private void PruneFloorSlots(int tierIndex, int activeCount)
        {
            _floorSlotCountByTier.TryGetValue(tierIndex, out var previousCount);
            var keep = activeCount;

            for (var i = activeCount; i < previousCount; i++)
            {
                var key = FloorKey(tierIndex, i);
                if (!_slots.TryGetValue(key, out var slot)) continue;

                // Đang load / đang show: huỷ lúc này là mất callback SDK -> để lần dọn sau.
                if (slot.IsLoading || slot.IsShowing)
                {
                    keep = i + 1;
                    continue;
                }

                slot.Dispose();
                _slots.Remove(key);
            }

            _floorSlotCountByTier[tierIndex] = keep;
        }

        private AdmobBidAd GetOrCreate(string key, string adUnitId, bool isDefaultSlot)
        {
            if (_slots.TryGetValue(key, out var slot))
            {
                if (slot.AdUnitId == adUnitId) return slot;

                // Remote config vừa đổi ad unit của biến thể này giữa phiên. Slot đang load/đang show thì
                // KHÔNG được huỷ: callback của SDK còn treo, ad object sẽ không bao giờ Destroy và lượt
                // show đang chạy mất callback -> giữ slot cũ, lần Request sau đổi cũng chưa muộn.
                if (slot.IsLoading || slot.IsShowing) return slot;

                slot.Dispose();
                _slots.Remove(key);
            }

            slot = _factory(key, adUnitId, isDefaultSlot);
            slot.loadFinished = OnSlotLoadFinished;
            _slots.Add(key, slot);
            return slot;
        }
    }
}
