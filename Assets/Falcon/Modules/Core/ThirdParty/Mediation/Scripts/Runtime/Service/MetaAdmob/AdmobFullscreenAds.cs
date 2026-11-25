using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GoogleMobileAds.Api;
using UnityEngine;

namespace Falcon.Admob
{
    /// <summary>
    /// Tầng interstitial HOẶC rewarded của AdMob (một instance cho mỗi format), giữ một
    /// <see cref="AdmobBidPool"/>:
    ///
    ///  - BƯỚC B: 1 ad AdMob xin không set giá sàn -> giá AdMob "tự nhiên", nạp ngay khi SDK init xong
    ///    VÀ remote config đã cấp ad unit default (<c>defaultAdId</c>).
    ///    Config về muộn hơn SDK cũng không sao: tầng tự bật lên ở <see cref="OnRemoteConfigUpdated"/>.
    ///  - BƯỚC C: mỗi lần mediation chạy song song (vd MAX) load xong một ad, project gọi
    ///    <see cref="AdmobMediation.NotifyExternalAdLoaded"/>; tầng này tra BẬC của ad unit đó
    ///    (<see cref="AdmobMediationHooks.externalTierProvider"/>) rồi xin AdMob đúng NHÓM ad unit của
    ///    bậc ấy trong remote config, tất cả cùng một giá sàn = revenue mà ad MAX vừa trả về. Bậc khác
    ///    không bị đụng tới. Remote config không khai <c>adIds</c> nào -> bỏ qua hẳn bước C, KHÔNG
    ///    mượn ad unit bước B làm mức mặc định.
    ///
    ///    Mỗi lần MAX load xong chỉ xin MỘT LẦN; ad unit AdMob nào no-fill thì bỏ luôn, chờ tới lần ad
    ///    unit MAX của bậc đó load lại. Slot floor cũng KHÔNG tự nạp lại sau khi show
    ///    (<see cref="AdmobBidAd.OnShowFinished"/>) — chu kỳ của nó bám hẳn vào ad MAX cùng bậc.
    ///
    /// KHÔNG có mediation ngoài (dự án chỉ chạy AdMob) thì bước C không bao giờ chạy — tầng này vẫn hoạt
    /// động bình thường với riêng bước B.
    ///
    /// Class này KHÔNG implement interface nào của project: entry point vẫn do project quyết định.
    /// </summary>
    public class AdmobFullscreenAds
    {
        /// <summary>Không có ad nào sẵn sàng. Cùng quy ước với mediation ngoài để so trực tiếp.</summary>
        public const double NoAd = AdmobBidAd.NoAd;

        /// <summary>Có ad nhưng chưa biết giá -> thắng "không có gì" nhưng thua mọi mức giá thật.</summary>
        public const double UnknownRevenue = AdmobBidAd.UnknownRevenue;

        private readonly AdmobAdFormat _format;
        private readonly AdmobMediationConfig _config;
        private readonly AdmobBidPool _pool;
        private readonly AdmobBidSettings _settings;
        private readonly string _logTag;

        private bool _sdkReady;

        // Ad unit bước B đã phát lệnh load. Ad unit đến từ remote config nên có thể chưa có lúc SDK init
        // xong, hoặc đổi giữa phiên -> giữ lại để biết khi nào cần chạy (lại) bước B.
        private string _loadedDefaultAdUnitId;

        // Trạng thái bước B/C đã log lần gần nhất — chỉ log lại khi thật sự đổi.
        private bool _loggedRunsStepB, _loggedRunsStepC;

        // Mediation ngoài có thể load xong TRƯỚC khi MobileAds.Initialize hoàn tất (hai SDK init song
        // song). Mỗi BẬC có nhóm ad unit AdMob riêng nên phải giữ lại một mục cho MỖI bậc — gộp chung
        // thành một mức cao nhất là mất hẳn bước C của các bậc còn lại. Trong cùng một bậc thì giữ mức
        // cao nhất là đủ, mức thấp hơn sẽ tự đến ở những lần load sau.
        private readonly Dictionary<int, PendingTier> _pendingTiers = new Dictionary<int, PendingTier>();

        private struct PendingTier
        {
            public string externalAdUnitId;
            public double revenueUsd;
        }

        internal AdmobFullscreenAds(MonoBehaviour runner, AdmobAdFormat format, AdmobMediationConfig config)
        {
            _format = format;
            _config = config;
            _logTag = format == AdmobAdFormat.Interstitial ? "AdmobInterstitial" : "AdmobRewarded";

            var settings = new AdmobBidSettings
            {
                format = format,
                enableLog = config.enableLog,
                adValueInMicros = config.adValueInMicros,
                publisherIdProvider = config.ResolvePublisherId,
                // Ad AdMob thắng: gom thêm id các ad thua cùng lượt ở MỌI slot của format này. Đọc _pool
                // lúc gọi (không phải lúc dựng) nên gán trước khi _pool được tạo vẫn đúng.
                lostResponseIdsCollector = (into, loadedBefore) => _pool.ClaimLostResponseIds(into, loadedBefore)
            };
            _settings = settings;

            // Ad unit của từng slot do pool truyền vào, TOÀN BỘ đến từ remote config (B: id default,
            // C: nhóm id của từng bậc) -> báo cáo AdMob tách sẵn theo bước và theo từng bậc.
            _pool = new AdmobBidPool((key, adUnitId, isDefaultSlot) => format == AdmobAdFormat.Interstitial
                ? (AdmobBidAd)new AdmobBidInterstitialAd(runner, adUnitId, key, isDefaultSlot, settings)
                : new AdmobBidRewardedAd(runner, adUnitId, key, isDefaultSlot, settings));
        }

        /// <summary>Format của tầng này.</summary>
        public AdmobAdFormat Format => _format;

        /// <summary>
        /// Ad unit BƯỚC B đang dùng, đọc từ remote config. Rỗng = config chưa
        /// về, hoặc MO cố ý bỏ bước B của format này.
        /// </summary>
        public string DefaultAdUnitId => AdmobRemoteConfigStore.GetDefaultAdUnitId(_format);

        /// <summary>
        /// Tầng này có hoạt động không: bật bidding VÀ remote config cấp được ít nhất MỘT bước
        /// (B hoặc C). Hai bước độc lập nhau — thiếu id bước B thì vẫn chạy được bước C và ngược lại.
        /// Đọc động: config về muộn thì tầng tự bật lên, không cần restart.
        /// </summary>
        public bool IsEnabled => RunsStepB || RunsStepC;

        /// <summary>BƯỚC B có chạy không — remote config phải có defaultAdId cho format này.</summary>
        public bool RunsStepB
            => _config.enableBidding && AdmobRemoteConfigStore.HasDefaultAdUnit(_format);

        /// <summary>BƯỚC C có chạy không — remote config phải khai ít nhất một ad unit mức floor.</summary>
        public bool RunsStepC
            => _config.enableBidding && AdmobRemoteConfigStore.HasFloorAdUnits(_format);

        /// <summary>
        /// Giá cao nhất AdMob đang cầm được (bước B + C), <see cref="NoAd"/> nếu không có ad nào sẵn
        /// sàng. Dùng để so với giá mediation ngoài ngay tại thời điểm show.
        /// </summary>
        public double BestAvailableRevenue => IsEnabled ? _pool.BestRevenue() : NoAd;

        public bool IsReady() => IsEnabled && _pool.HasAd();

        /// <summary>
        /// Ad unit của ad sẽ được chiếu nếu tầng này thắng giá; null khi không có ad. Chỉ để log —
        /// phải hỏi TRƯỚC khi gọi <see cref="ShowBest"/>, vì ad được rút khỏi kho ngay lúc show.
        /// </summary>
        public string BestAdUnitId => IsEnabled ? _pool.Best()?.AdUnitId : null;

        /// <summary>Network thắng thầu của ad sẽ được chiếu; null khi không có ad. Chỉ để log.</summary>
        public string BestNetwork => IsEnabled ? _pool.Best()?.BestNetwork : null;

        /// <summary>Dump cache AdMob (mọi slot + revenue) để log lúc so giá.</summary>
        public string DescribeCache()
        {
            if (IsEnabled) return _pool.Describe();

            if (!_config.enableBidding) return "    (tầng AdMob TẮT — enableBidding = false)";

            return "    (tầng AdMob KHÔNG chạy — remote config không có ad unit bước B lẫn bước C " +
                   $"cho {_format})";
        }

        /// <summary>
        /// Show ad AdMob đắt nhất đang có. Dùng khi project TỰ bọc guard/flow của mình (vd so giá với
        /// mediation khác rồi mới gọi) — ở đây KHÔNG bọc lại. Trả false nếu đúng lúc đó không còn ad nào
        /// -> người gọi tự fallback.
        /// </summary>
        public bool ShowBest(string where, Action onReward, Action onHidden, Action onFail)
        {
            if (!IsEnabled) return false;

            var best = _pool.Best();
            if (best == null) return false;

            Log($"SHOW ad AdMob: slot '{best.Key}' ({(best.IsDefaultSlot ? "default" : "floor")}) " +
                $"revenue=${best.Revenue:F6} floor=${best.LoadedFloorUsd:F6} tại {where}");
            best.Show(where, onReward, onHidden, onFail);
            return true;
        }

        /// <summary>
        /// Đường show ĐỘC LẬP cho dự án chỉ chạy AdMob: tự bọc ad-flow guard
        /// (<see cref="AdmobMediationHooks.onAdFlowBegin"/>) và tự trả fail khi không có ad.
        ///
        /// <paramref name="onCompleted"/>: interstitial = ad đã đóng; rewarded = user đã xem đủ và ad đã
        /// đóng. Bỏ giữa chừng -> <paramref name="onFailed"/>.
        /// </summary>
        public void Show(string where, Action onCompleted = null, Action onFailed = null)
        {
            var rewardEarned = false;
            var finished = false;

            void Finish(bool success)
            {
                if (finished) return;
                finished = true;

                AdmobMediationHooks.EndAdFlow();

                if (success) onCompleted?.Invoke();
                else onFailed?.Invoke();
            }

            if (!IsReady())
            {
                Log($"Show('{where}') nhưng KHÔNG có ad nào sẵn sàng.");
                onFailed?.Invoke();
                return;
            }

            AdmobMediationHooks.BeginAdFlow();

            var needsReward = _format == AdmobAdFormat.Rewarded;
            var shown = ShowBest(where,
                onReward: () => rewardEarned = true,
                onHidden: () => Finish(!needsReward || rewardEarned),
                onFail: () => Finish(false));

            //af_*_show CHỈ bắn ở đường show độc lập này (dự án chỉ chạy AdMob). Show qua mediation ngoài
            //(SecondaryAdSource -> ShowBest) thì tầng phiên của project đã bắn rồi -> không bắn lại, tránh đếm đôi.
            if (shown) AdmobMediationHooks.Track(needsReward ? "af_rewarded_show" : "af_inters_show");
            else Finish(false);
        }

        // === lifecycle, chỉ AdmobMediation gọi ===

        internal void OnSdkInitialized()
        {
            // Đặt cờ VÔ ĐIỀU KIỆN, kể cả khi chưa có ad unit: ad unit bước B đến từ remote config nên
            // có thể về SAU lúc SDK init xong. Gác cờ này sau IsEnabled như trước là tầng AdMob chết
            // luôn cả phiên nếu config về muộn.
            _sdkReady = true;
            _loggedRunsStepB = RunsStepB;
            _loggedRunsStepC = RunsStepC;

            // Vô điều kiện: "format này chạy bước nào" là câu hỏi thứ hai khi soi log, ngay sau
            // "AdMob có init không".
#if ENABLE_DEBUG_LOG
            Debug.Log($"[{_logTag}] SDK sẵn sàng -> {DescribeSteps()} | " +
                      $"{AdmobRemoteConfigStore.GetConfig(_format).Describe()}");
#endif

            if (!IsEnabled) return;

            StartWhenReady();
        }

        /// <summary>
        /// Remote config vừa đổi. Ad unit bước B nằm trong đó nên đây cũng là chỗ tầng AdMob được bật
        /// lên lần đầu khi config về muộn hơn MobileAds.Initialize.
        /// </summary>
        internal void OnRemoteConfigUpdated()
        {
            if (!_sdkReady) return;

            // Chỉ log khi trạng thái ĐỔI — config có thể refresh nhiều lần mà chẳng đổi gì.
            if (RunsStepB != _loggedRunsStepB || RunsStepC != _loggedRunsStepC)
            {
                _loggedRunsStepB = RunsStepB;
                _loggedRunsStepC = RunsStepC;
#if ENABLE_DEBUG_LOG
                Debug.Log($"[{_logTag}] remote config đổi -> {DescribeSteps()}");
#endif
            }

            if (!IsEnabled) return;

            StartWhenReady();
        }

        private string DescribeSteps()
            => $"bướcB={(RunsStepB ? "CHẠY (adUnit=" + DefaultAdUnitId + ")" : "BỎ QUA (không có id)")} " +
               $"bướcC={(RunsStepC ? "CHẠY" : "BỎ QUA (không có id)")}";

        // Chạy được nhiều lần: chỉ phát lệnh bước B khi ad unit đổi (hoặc lần đầu), và bậc nào đang
        // xếp hàng thì bắn nốt.
        private void StartWhenReady()
        {
            if (RunsStepB)
            {
                var adUnitId = DefaultAdUnitId;
                if (adUnitId != _loadedDefaultAdUnitId)
                {
                    _loadedDefaultAdUnitId = adUnitId;
                    Log($"BƯỚC B: load giá MẶC ĐỊNH (không set sàn), adUnit={adUnitId}");
                    _pool.LoadDefault(adUnitId);
                }
            }

            if (!RunsStepC || _pendingTiers.Count == 0) return;

            Log($"phát lại {_pendingTiers.Count} bậc đã xếp hàng lúc tầng AdMob chưa sẵn sàng.");
            foreach (var pending in _pendingTiers)
                RequestFloorGroup(pending.Key, pending.Value.externalAdUnitId, pending.Value.revenueUsd);

            _pendingTiers.Clear();
        }

        // BƯỚC C: một ad unit của mediation ngoài vừa load xong -> xin AdMob nhóm ad unit của ĐÚNG bậc
        // đó. Bậc khác không bị đụng tới.
        internal void OnExternalAdLoaded(string externalAdUnitId, double revenue)
        {
            if (!_config.enableBidding) return;

            if (revenue <= 0d)
            {
                Log($"'{externalAdUnitId}' load xong nhưng revenue=${revenue:F6} (<= 0) " +
                    "-> KHÔNG xin ad AdMob theo sàn (không có mức sàn hợp lệ).");
                return;
            }

            var tier = AdmobMediationHooks.ExternalTier(_format, externalAdUnitId);

            // Chưa init xong HOẶC remote config chưa cấp ad unit mức floor -> xếp hàng, đừng vứt: MAX
            // load xong trước lúc config AdMob về là chuyện thường, mất tín hiệu đó là mất luôn bước C
            // của bậc ấy cho tới lần MAX load kế tiếp. Hàng đợi có key là bậc nên bị chặn trên bởi số
            // bậc, config về mà thật sự không có mức floor nào thì nó chỉ nằm im.
            if (!_sdkReady || !RunsStepC)
            {
                if (!_pendingTiers.TryGetValue(tier, out var pending) || revenue > pending.revenueUsd)
                {
                    _pendingTiers[tier] = new PendingTier
                    {
                        externalAdUnitId = externalAdUnitId,
                        revenueUsd = revenue
                    };
                }

                Log($"'{externalAdUnitId}' (bậc {tier}) load xong revenue=${revenue:F6} nhưng tầng AdMob " +
                    $"chưa sẵn sàng -> xếp hàng (cao nhất của bậc này=${_pendingTiers[tier].revenueUsd:F6}).");
                return;
            }

            RequestFloorGroup(tier, externalAdUnitId, revenue);
        }

        /// <summary>
        /// Mediation ngoài vừa có impression (tức nó THẮNG giá lượt show này) -> khai báo impression với
        /// AdMob cho response của MỌI ad AdMob đang giữ (mọi slot) — tất cả đều đã thua lượt này. Mỗi ad
        /// chỉ bị khai báo thua MỘT lần, xem <see cref="AdmobBidAd.ClaimLostResponseIds"/>.
        /// </summary>
        internal void OnExternalImpression(double revenueUsd, AdValue.PrecisionType precision)
        {
            if (!IsEnabled) return;

            // MAX trả -1 khi không đọc được revenue: piv âm không phải một mức giá thật.
            if (double.IsNaN(revenueUsd) || double.IsInfinity(revenueUsd) || revenueUsd < 0d)
            {
                Log($"mediation ngoài thắng giá nhưng revenue=${revenueUsd:F6} không hợp lệ " +
                    "-> KHÔNG khai báo impression.");
                return;
            }

            var responseIds = new List<string>();
            _pool.ClaimLostResponseIds(responseIds);
            if (responseIds.Count == 0)
            {
                Log($"mediation ngoài thắng giá (revenue=${revenueUsd:F6}) nhưng kho AdMob không còn ad nào " +
                    "chưa khai báo thua -> không có response để khai báo impression.");
                return;
            }

            var value = AdmobBidAd.FormatUsd(revenueUsd);
            Log($"mediation ngoài thắng giá -> khai báo impression cho {responseIds.Count} response AdMob " +
                $"thua [{string.Join(",", responseIds)}] piv={value} pip={precision}.");
            AdmobMultiPlatformUtils.RecordImpressionEvent(_settings.PublisherId, responseIds, value,
                ((int)precision).ToString(CultureInfo.InvariantCulture));
        }

        internal void Dispose() => _pool?.Dispose();

        /// <summary>
        /// BƯỚC C: xin nhóm ad unit của bậc <paramref name="tier"/> theo thác giá, giá sàn =
        /// <paramref name="revenue"/> × multiplier (1.0 -> floor ĐÚNG BẰNG revenue mà ad MAX của bậc đó
        /// vừa trả về): xin lần lượt từng ad unit, multiplier CAO NHẤT trước, thành công thì dừng, fail
        /// mới xin ad unit kế tiếp, ad unit cuối vẫn fail thì dừng (xem <see cref="AdmobBidPool.RequestFloor"/>). Đọc lại config ở
        /// MỖI lần gọi để remote config về muộn vẫn có hiệu lực ngay.
        ///
        /// Gọi ĐÚNG MỘT LẦN cho mỗi lần ad unit MAX của bậc đó load xong.
        /// </summary>
        private void RequestFloorGroup(int tier, string externalAdUnitId, double revenue)
        {
            var variants = AdmobRemoteConfigStore.GetConfig(_format).BuildFloorVariants(tier);

            if (variants.Count == 0)
            {
                Log($"BƯỚC C: '{externalAdUnitId}' (bậc {tier}) revenue=${revenue:F6} nhưng KHÔNG có ad " +
                    "unit nào cho bậc này trong remote config -> bỏ qua.");
                return;
            }

            if (_config.enableLog)
            {
                var sb = new StringBuilder();
                for (var i = 0; i < variants.Count; i++)
                {
                    sb.Append("\n    ").Append($"#{i} x{variants[i].multiplier:0.##} " +
                                               $"-> floor=${revenue * variants[i].multiplier:F6} " +
                                               $"adUnit={variants[i].adUnitId}");
                }

                Log($"BƯỚC C: '{externalAdUnitId}' (bậc {tier}) load xong revenue=${revenue:F6} " +
                    $"-> thác giá AdMob {variants.Count} ad unit (lần lượt từng ad unit, multiplier cao -> thấp, thành công thì dừng):{sb}");
            }

            _pool.RequestFloor(tier, revenue, variants);
        }

        private void Log(string message)
        {
#if ENABLE_DEBUG_LOG
            if (_config.enableLog) Debug.Log($"[{_logTag}] {message}");
#endif
        }
    }
}
