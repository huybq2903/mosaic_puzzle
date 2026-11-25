using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Falcon.Admob
{
    /// <summary>
    /// Kho config BƯỚC C (thang giá sàn) lấy TỪ REMOTE CONFIG. Static vì mọi tầng đọc nó tại đúng thời
    /// điểm xin ad — không phụ thuộc thứ tự khởi tạo, và luôn khác null nên đọc sớm cũng không NRE.
    ///
    /// Config về muộn là chuyện bình thường (remote config và MobileAds init song song): project chỉ cần
    /// gọi <see cref="AdmobMediation.RefreshRemoteConfig"/> mỗi khi config đổi, mọi lần xin ad sau đó
    /// đọc lại giá trị mới.
    /// </summary>
    public static class AdmobRemoteConfigStore
    {
        /// <summary>Tiền tố log của mọi dòng "quyết định" — grep một phát ra toàn bộ đường đi của config.</summary>
        internal const string LOG_TAG = "[AdmobMediation][config]";

        private static AdmobMetaAdsConfigsData _data = new AdmobMetaAdsConfigsData();

        /// <summary>Đã nhận được ít nhất một lần config khác rỗng từ project chưa (chỉ để log).</summary>
        public static bool HasConfig { get; private set; }

        /// <summary>
        /// Config của một format (ad unit bước B + thang giá bước C). KHÔNG bao giờ null — chưa có
        /// config thì trả object rỗng, người gọi kiểm tra bằng HasDefaultAdUnit / HasFloorAdUnits.
        /// </summary>
        public static AdmobMetaAdsUnitConfig GetConfig(AdmobAdFormat format) => _data.GetConfig(format);

        /// <summary>
        /// Ad unit BƯỚC B của format. Rỗng = remote config chưa về hoặc MO
        /// cố ý bỏ bước B của format đó -> format đó chỉ chạy bước C.
        /// </summary>
        public static string GetDefaultAdUnitId(AdmobAdFormat format) => GetConfig(format).DefaultAdUnitId;

        /// <summary>Remote config có cấp ad unit BƯỚC B cho format này không. Không có = bỏ qua bước B.</summary>
        public static bool HasDefaultAdUnit(AdmobAdFormat format) => GetConfig(format).HasDefaultAdUnit;

        /// <summary>
        /// Remote config có cấp ad unit BƯỚC C nào cho format này không (bậc nào cũng tính).
        /// Không có = bỏ qua bước C — KHÔNG mượn tạm ad unit bước B.
        /// </summary>
        public static bool HasFloorAdUnits(AdmobAdFormat format) => GetConfig(format).HasFloorAdUnits;

        /// <summary>
        /// Có ĐÚNG MỘT thứ đáng để init AdMob không: bất kỳ format fullscreen nào có ad unit bước B
        /// hoặc bước C. Sai = remote config không cấp gì cả -> đừng init MobileAds, để game chạy mỗi
        /// bước A của MultiCall.
        /// </summary>
        public static bool HasAnyFullscreenAdUnit
            => HasAnyAdUnit(AdmobAdFormat.Interstitial) || HasAnyAdUnit(AdmobAdFormat.Rewarded);

        /// <summary>Format này có chạy được bước nào không (B hoặc C).</summary>
        public static bool HasAnyAdUnit(AdmobAdFormat format)
            => HasDefaultAdUnit(format) || HasFloorAdUnits(format);

        /// <summary>
        /// Publisher id suy từ ad unit id bước B: id có dạng <c>ca-app-pub-XXXX/YYYY</c> nên phần trước
        /// dấu '/' CHÍNH LÀ publisher id. Chuỗi rỗng nếu chưa suy được (config chưa về).
        /// </summary>
        public static string PublisherId
        {
            get
            {
                var fromInterstitial = PublisherOf(GetDefaultAdUnitId(AdmobAdFormat.Interstitial));
                return !string.IsNullOrEmpty(fromInterstitial)
                    ? fromInterstitial
                    : PublisherOf(GetDefaultAdUnitId(AdmobAdFormat.Rewarded));
            }
        }

        private static string PublisherOf(string adUnitId)
        {
            if (string.IsNullOrEmpty(adUnitId)) return string.Empty;

            var slash = adUnitId.IndexOf('/');
            return slash > 0 ? adUnitId.Substring(0, slash) : string.Empty;
        }

        /// <summary>
        /// Đọc lại config qua <see cref="AdmobMediationHooks.remoteConfigProvider"/>. JSON hỏng / thiếu
        /// key KHÔNG được phép làm chết luồng ads: rơi về config rỗng -> không có ad unit nào, tầng
        /// AdMob im lặng còn mediation ngoài chạy bình thường.
        /// </summary>
        public static void Reload()
        {
            var settings = AdmobMediationHooks.ReadRemoteSettings();
            var json = settings?.moMulAdmobMetaConfig;

            _data = Parse(json);
            HasConfig = !string.IsNullOrEmpty(json);

            // Log VÔ ĐIỀU KIỆN (không gate sau enableLog): đây là dữ liệu quyết định module chạy hay
            // không, thiếu nó thì mọi triệu chứng phía sau đều là "AdMob im lặng" không rõ lý do.
            // Chỉ chạy 1-2 lần mỗi phiên nên không có chuyện spam.
            if (settings == null)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogWarning($"{LOG_TAG} remoteConfigProvider trả về NULL -> không có ad unit nào, " +
                                 "tầng AdMob sẽ không chạy. Project đã gọi AdmobMediationFalconBridge.Install() chưa?");
#endif
                return;
            }

            if (!HasConfig)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogWarning($"{LOG_TAG} sc_ad_bid_floor không có id mediation=admob nào (hoặc phiên LUA) " +
                                 "-> không có ad unit nào, tầng meta AdMob sẽ không chạy.");
#endif
                return;
            }

#if ENABLE_DEBUG_LOG
            Debug.Log($"{LOG_TAG} nhận id AdMob từ sc_ad_bid_floor:\n{json}\n-> sau khi parse: {Describe()}");
#endif
        }

        /// <summary>Một khối mô tả toàn bộ config hiện tại, chỉ phục vụ log.</summary>
        public static string Describe()
        {
            return $"hasConfig={HasConfig}\n" +
                   $"  interstitial: {GetConfig(AdmobAdFormat.Interstitial).Describe()}\n" +
                   $"  rewarded    : {GetConfig(AdmobAdFormat.Rewarded).Describe()}";
        }

        private static AdmobMetaAdsConfigsData Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return new AdmobMetaAdsConfigsData();

            try
            {
                return JsonUtility.FromJson<AdmobMetaAdsConfigsData>(json) ?? new AdmobMetaAdsConfigsData();
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError($"[AdmobMediation] remote config bước C không parse được -> dùng config rỗng. {e}");
#endif
                return new AdmobMetaAdsConfigsData();
            }
        }
    }

    /// <summary>Thang giá bước C của cả hai format fullscreen, đúng cấu trúc JSON trên remote config.</summary>
    [Serializable]
    public class AdmobMetaAdsConfigsData
    {
        public AdmobMetaAdsUnitConfig interstitial = new AdmobMetaAdsUnitConfig();
        public AdmobMetaAdsUnitConfig rewarded = new AdmobMetaAdsUnitConfig();

        // JsonUtility gán null cho field khi JSON ghi "interstitial": null -> luôn trả object rỗng thay
        // vì null để người gọi khỏi phải kiểm tra.
        public AdmobMetaAdsUnitConfig GetConfig(AdmobAdFormat format)
        {
            var config = format switch
            {
                AdmobAdFormat.Interstitial => interstitial,
                AdmobAdFormat.Rewarded => rewarded,
                _ => rewarded
            };

            return config ?? new AdmobMetaAdsUnitConfig();
        }
    }

    /// <summary>
    /// Config của MỘT format, lấy TOÀN BỘ từ remote config — module không còn asset settings nào trong
    /// Resources, ad unit id bước B lẫn bước C đều nằm ở đây.
    ///
    ///  - BƯỚC B: <see cref="defaultAdId"/> — ad unit mức default, xin không set giá sàn. Để trống là
    ///    bỏ qua bước B của format đó (bước C vẫn chạy).
    ///  - BƯỚC C: <see cref="adIds"/> + <see cref="multipliers"/> — thang giá sàn. Ad unit
    ///    <c>adIds[i]</c> được xin với giá sàn = revenue của ad MAX vừa load × <c>multipliers[i]</c>.
    ///
    /// <code>
    /// {
    ///   "interstitial": {
    ///     "defaultAdId": "ca-app-pub-3046520253844875/1111111111",
    ///     "adIds": ["ca-app-pub-3046520253844875/1364369313",
    ///               "ca-app-pub-3046520253844875/8385203119",
    ///               "ca-app-pub-3046520253844875/2342393155"],
    ///     "multipliers": [1, 1.2, 1.5]
    ///   },
    ///   "rewarded": { ... y hệt ... }
    /// }
    /// </code>
    ///
    /// Thang giá này chạy lại cho TỪNG BẬC ad unit MAX: max thứ k load xong revenue R thì xin
    /// <c>adIds[i]</c> ở giá sàn R × <c>multipliers[i]</c>. Cùng một ad unit AdMob nhưng mỗi bậc giữ
    /// kho riêng (key slot <c>floor#k#i</c>) nên bậc này show xong không đụng gì tới bậc khác — bậc do
    /// <see cref="AdmobMediationHooks.externalTierProvider"/> quyết định.
    ///
    /// Số ad unit và số hệ số do remote config quyết định, module không giả định gì. Hệ số thiếu hoặc
    /// &lt;= 0 tính là 1.0 (giá sàn ĐÚNG BẰNG revenue của ad MAX).
    ///
    /// <see cref="levels"/> là biến thể TUỲ CHỌN cho trường hợp MO muốn mỗi bậc MAX đi một bộ ad unit
    /// KHÁC nhau; khai <c>levels</c> thì nó thắng, còn bỏ trống (mặc định) thì dùng thang phẳng ở trên.
    /// </summary>
    [Serializable]
    public class AdmobMetaAdsUnitConfig
    {
        /// <summary>
        /// Ad unit BƯỚC B (mức default, xin ad không set giá sàn). Rỗng = bỏ qua bước B của format này.
        ///
        /// KHÔNG tách Android/iOS: remote config đã trả về theo đúng app đang chạy nên chỉ cần một
        /// trường; MO khai id nào thì platform đó dùng id ấy.
        /// </summary>
        public string defaultAdId;

        /// <summary>BƯỚC C — danh sách ad unit của thang giá sàn, dùng chung cho mọi bậc MAX.</summary>
        public List<string> adIds = new List<string>();

        /// <summary>Hệ số nhân lên revenue của ad MAX, theo đúng chỉ số của <see cref="adIds"/>.</summary>
        public List<float> multipliers = new List<float>();

        /// <summary>
        /// TUỲ CHỌN — mỗi bậc MAX một bộ ad unit riêng. Khai thì thắng <see cref="adIds"/>; để trống
        /// (mặc định) là dùng thang phẳng.
        /// </summary>
        public List<AdmobMetaAdsLevelConfig> levels = new List<AdmobMetaAdsLevelConfig>();

        /// <summary>Ad unit BƯỚC B; rỗng = bỏ qua bước B của format này.</summary>
        public string DefaultAdUnitId => defaultAdId ?? string.Empty;

        /// <summary>Có ad unit bước B không.</summary>
        public bool HasDefaultAdUnit => !string.IsNullOrEmpty(DefaultAdUnitId);

        /// <summary>
        /// Có ad unit bước C nào không — bậc nào cũng tính, kể cả thang phẳng lẫn levels. Sai = MO không
        /// khai mức floor nào cho format này -> bỏ qua hẳn bước C.
        /// </summary>
        public bool HasFloorAdUnits
        {
            get
            {
                if (levels != null)
                {
                    foreach (var level in levels)
                    {
                        if (level?.adIds == null) continue;

                        foreach (var adUnitId in level.adIds)
                            if (!string.IsNullOrEmpty(adUnitId))
                                return true;
                    }
                }

                if (adIds == null) return false;

                foreach (var adUnitId in adIds)
                    if (!string.IsNullOrEmpty(adUnitId))
                        return true;

                return false;
            }
        }

        /// <summary>
        /// Nhóm biến thể bước C của bậc <paramref name="tierIndex"/>. Ad unit rỗng bị bỏ qua.
        ///
        /// Bậc nằm ngoài danh sách <see cref="levels"/> -> KHÔNG có nhóm nào (trả list rỗng): remote
        /// config chỉ khai 3 bậc mà MAX có 4 ad unit thì bậc thứ 4 đơn giản là không chạy bước C, chứ
        /// không mượn nhóm của bậc khác.
        ///
        /// Config KHÔNG khai mức floor nào -> cũng trả list rỗng: bỏ qua hẳn bước C. Cố ý KHÔNG mượn
        /// ad unit bước B làm biến thể mặc định — mượn như vậy là âm thầm bắn request có giá sàn vào
        /// đúng ad unit mà bước B đang xin không giá sàn, số liệu hai bước trộn vào nhau.
        /// </summary>
        public List<AdmobFloorVariant> BuildFloorVariants(int tierIndex)
        {
            var variants = new List<AdmobFloorVariant>();

            var level = LevelAt(tierIndex);
            if (level?.adIds != null)
            {
                var multiplier = level.Multiplier;
                foreach (var adUnitId in level.adIds)
                {
                    if (string.IsNullOrEmpty(adUnitId)) continue;
                    variants.Add(new AdmobFloorVariant(adUnitId, multiplier));
                }

                return variants;
            }

            // Không khai "levels" -> dùng thang giá phẳng, mọi bậc MAX chung một bộ ad unit.
            if (HasLevels) return variants;

            variants.AddRange(BuildSharedVariants());

            return variants;
        }

        /// <summary>Một dòng mô tả config để log lúc nhận remote config.</summary>
        public string Describe()
        {
            var defaultId = string.IsNullOrEmpty(DefaultAdUnitId) ? "(TRỐNG — tắt format này)" : DefaultAdUnitId;
            return $"bướcB={defaultId} || bướcC: {DescribeLevels()}";
        }

        private string DescribeLevels()
        {
            if (HasLevels)
            {
                var sb = new StringBuilder();
                for (var i = 0; i < levels.Count; i++)
                {
                    if (sb.Length > 0) sb.Append(" | ");
                    sb.Append($"bậc {i} x{levels[i].Multiplier.ToString("0.##", CultureInfo.InvariantCulture)}: ")
                        .Append(levels[i].DescribeAdIds());
                }

                return sb.ToString();
            }

            var shared = BuildSharedVariants();
            if (shared.Count == 0) return "(TRỐNG — bỏ qua bước C)";

            var sharedSb = new StringBuilder("(thang phẳng, mọi bậc MAX dùng chung) ");
            for (var i = 0; i < shared.Count; i++)
            {
                if (i > 0) sharedSb.Append(", ");
                sharedSb.Append($"#{i} x{shared[i].multiplier.ToString("0.##", CultureInfo.InvariantCulture)} " +
                                $"@{shared[i].adUnitId}");
            }

            return sharedSb.ToString();
        }

        private bool HasLevels => levels != null && levels.Count > 0;

        private AdmobMetaAdsLevelConfig LevelAt(int tierIndex)
        {
            if (!HasLevels) return null;
            if (tierIndex < 0 || tierIndex >= levels.Count) return null;

            return levels[tierIndex];
        }

        private List<AdmobFloorVariant> BuildSharedVariants()
        {
            var variants = new List<AdmobFloorVariant>();

            if (adIds == null) return variants;

            for (var i = 0; i < adIds.Count; i++)
            {
                var adUnitId = adIds[i];
                if (string.IsNullOrEmpty(adUnitId)) continue;

                variants.Add(new AdmobFloorVariant(adUnitId, MultiplierAt(i)));
            }

            return variants;
        }

        // Hệ số <= 0 vô nghĩa với giá sàn (0 = không set sàn, âm = sai) -> coi như 1.0.
        private float MultiplierAt(int index)
        {
            if (multipliers == null || index >= multipliers.Count) return 1f;

            var multiplier = multipliers[index];
            return multiplier > 0f ? multiplier : 1f;
        }
    }

    /// <summary>
    /// Nhóm ad unit AdMob của MỘT bậc mediation ngoài. Mọi ad unit trong nhóm xin CÙNG một giá sàn =
    /// revenue của ad MAX bậc đó × <see cref="multiplier"/> — nhiều id cùng mức giá là để có nhiều cơ
    /// hội fill ở đúng mức đó, không phải để rải thang giá.
    /// </summary>
    [Serializable]
    public class AdmobMetaAdsLevelConfig
    {
        public List<string> adIds = new List<string>();

        /// <summary>Hệ số nhân lên revenue của ad MAX. Thiếu/không hợp lệ = 1.0 (floor ĐÚNG BẰNG revenue).</summary>
        public float multiplier = 1f;

        public float Multiplier => multiplier > 0f ? multiplier : 1f;

        public string DescribeAdIds()
        {
            if (adIds == null || adIds.Count == 0) return "(không có ad unit)";
            return string.Join(", ", adIds);
        }
    }

    /// <summary>
    /// Một biến thể bước C: xin ad ở <see cref="adUnitId"/> với giá sàn = revenue mediation ngoài ×
    /// <see cref="multiplier"/>.
    /// </summary>
    public readonly struct AdmobFloorVariant
    {
        public readonly string adUnitId;
        public readonly float multiplier;

        public AdmobFloorVariant(string adUnitId, float multiplier)
        {
            this.adUnitId = adUnitId;
            this.multiplier = multiplier;
        }
    }
}
