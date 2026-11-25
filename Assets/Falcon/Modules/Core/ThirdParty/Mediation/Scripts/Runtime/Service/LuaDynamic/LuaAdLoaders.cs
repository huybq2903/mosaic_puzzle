/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-02
 */

#if MAX_ENABLE
using System;
using System.Collections.Generic;
using System.Globalization;
using Falcon.Helpers.Devkit;
using GoogleMobileAds.Api;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime.LuaDynamic
{
    public struct LuaPaidDetail
    {
        public int? waterfallPosition;
        public string instanceName;
        public string formatRaw;
        public Dictionary<string, object> extraMeta;
        public MaxSdkBase.AdInfo maxInfo;

        public static LuaPaidDetail FromMax(MaxSdkBase.AdInfo info) => new LuaPaidDetail
        {
            maxInfo = info,
            instanceName = info.NetworkPlacement,
            formatRaw = info.AdFormat,
            waterfallPosition = AdShowReporter.LoadedWaterfallPosition(info),
            extraMeta = AdShowReporter.MaxExtraMeta(info)
        };
    }

    /// <summary>Một mạng quảng cáo mà Lua gọi được qua <c>context.&lt;Mediation&gt;.LoadInterstitial/LoadRewarded</c>.</summary>
    internal interface ILuaAdLoader
    {
        string Mediation { get; }

        /// <summary>Gửi request cho <paramref name="record"/> (đã có format + adUnitId + floor). Kết quả về qua LuaAdMonetize.</summary>
        void Load(LuaAdRecord record);

        bool IsReady(LuaAdRecord record);

        /// <summary>Chiếu ad. false = không còn ad để chiếu, KHÔNG có callback nào được bắn.</summary>
        bool Show(LuaAdRecord record, string where);
    }

    /// <summary>
    /// Interstitial / rewarded MAX do Lua điều khiển. Unit đã load qua đây được khai báo với
    /// <see cref="ExternalMaxAdUnits"/> -> FalconMaxService / MultiCall / DFPool / bridge AdMob bỏ qua event
    /// của nó, ở đây là nơi DUY NHẤT xử lý.
    /// MAX tự retry bị tắt (disable_auto_retries): retry là việc của script.
    /// </summary>
    internal sealed class LuaMaxLoader : ILuaAdLoader
    {
        private readonly HashSet<string> _inter = new HashSet<string>();
        private readonly HashSet<string> _reward = new HashSet<string>();

        public string Mediation => LuaAdRecord.MEDIATION_MAX;

        public LuaMaxLoader()
        {
            Subscribe(AdType.Interstitial);
            Subscribe(AdType.Reward);
            MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent += (id, _, __) =>
            {
                if (Owns(AdType.Reward, id)) LuaAdMonetize.OnAdRewarded(AdType.Reward, id);
            };
        }

        private void Subscribe(AdType format)
        {
            void Loaded(string id, MaxSdkBase.AdInfo info)
            {
                if (Owns(format, id))
                    LuaAdMonetize.OnAdLoaded(format, id, info == null ? 0d : info.Revenue * 1000d, info?.NetworkName,
                        info?.LatencyMillis ?? 0L);
            }

            void LoadFailed(string id, MaxSdkBase.ErrorInfo error)
            {
                if (Owns(format, id))
                    LuaAdMonetize.OnAdLoadFailed(format, id,
                        error == null ? "unknown" : $"{error.Code} {error.Message}", error?.LatencyMillis ?? 0L);
            }

            void Displayed(string id, MaxSdkBase.AdInfo _)
            {
                if (Owns(format, id)) LuaAdMonetize.OnAdDisplayed(format, id);
            }

            void DisplayFailed(string id, MaxSdkBase.ErrorInfo error, MaxSdkBase.AdInfo _)
            {
                if (Owns(format, id)) LuaAdMonetize.OnAdShowFailed(format, id, error?.Code.ToString());
            }

            void Hidden(string id, MaxSdkBase.AdInfo _)
            {
                if (Owns(format, id)) LuaAdMonetize.OnAdHidden(format, id);
            }

            void Clicked(string id, MaxSdkBase.AdInfo _)
            {
                if (Owns(format, id)) LuaAdMonetize.OnAdClicked(format, id);
            }

            void Paid(string id, MaxSdkBase.AdInfo info)
            {
                if (Owns(format, id) && info != null)
                    LuaAdMonetize.OnAdPaid(format, id, info.Revenue, info.RevenuePrecision, info.NetworkName,
                        LuaPaidDetail.FromMax(info));
            }

            if (format == AdType.Reward)
            {
                MaxSdkCallbacks.Rewarded.OnAdLoadedEvent += Loaded;
                MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent += LoadFailed;
                MaxSdkCallbacks.Rewarded.OnAdDisplayedEvent += Displayed;
                MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent += DisplayFailed;
                MaxSdkCallbacks.Rewarded.OnAdHiddenEvent += Hidden;
                MaxSdkCallbacks.Rewarded.OnAdClickedEvent += Clicked;
                MaxSdkCallbacks.Rewarded.OnAdRevenuePaidEvent += Paid;
            }
            else
            {
                MaxSdkCallbacks.Interstitial.OnAdLoadedEvent += Loaded;
                MaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent += LoadFailed;
                MaxSdkCallbacks.Interstitial.OnAdDisplayedEvent += Displayed;
                MaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent += DisplayFailed;
                MaxSdkCallbacks.Interstitial.OnAdHiddenEvent += Hidden;
                MaxSdkCallbacks.Interstitial.OnAdClickedEvent += Clicked;
                MaxSdkCallbacks.Interstitial.OnAdRevenuePaidEvent += Paid;
            }
        }

        private HashSet<string> Set(AdType format) => format == AdType.Reward ? _reward : _inter;

        private bool Owns(AdType format, string adUnitId) => adUnitId != null && Set(format).Contains(adUnitId);

        public void Load(LuaAdRecord record)
        {
            var id = record.adUnitId;
            var floor = record.floor.ToString(CultureInfo.InvariantCulture);
            var first = Set(record.format).Add(id);
            if (first)
            {
                ExternalMaxAdUnits.Claim(id);
                //ad MAX thật của phiên Lua -> DFPool lấy revenue làm mốc giá sàn AdMob (như ad MAX của MultiCall)
                ExternalMaxAdUnits.MarkPriceAnchor(id);
            }

            //thường đã set ở CallAfterInit; id script tự thêm sau init thì set ở lần load đầu
            DisableAutoRetry(record.format, id);
            if (record.format == AdType.Reward)
            {
                MaxSdk.SetRewardedAdExtraParameter(id, MultiCall.KEY_SET_BID_FLOOR, floor);
                MaxSdk.LoadRewardedAd(id);
            }
            else
            {
                MaxSdk.SetInterstitialExtraParameter(id, MultiCall.KEY_SET_BID_FLOOR, floor);
                MaxSdk.LoadInterstitial(id);
            }
        }

        private static readonly HashSet<string> _retryDisabled = new HashSet<string>();

        /// <summary>Set <c>disable_auto_retries</c> cho unit MAX (mỗi unit một lần) + log vàng như MultiCall.</summary>
        internal static void DisableAutoRetry(AdType format, string id)
        {
            if (string.IsNullOrEmpty(id) || !_retryDisabled.Add((format == AdType.Reward ? "r:" : "i:") + id)) return;
            if (format == AdType.Reward)
            {
                MaxSdk.SetRewardedAdExtraParameter(id, "disable_auto_retries", "true");
                MediationManager.Instance.DebugLog("CallAfterInit (LUA) rewarded: " + id + " - disable_auto_retries");
            }
            else
            {
                MaxSdk.SetInterstitialExtraParameter(id, "disable_auto_retries", "true");
                MediationManager.Instance.DebugLog("CallAfterInit (LUA) interstitial: " + id + " - disable_auto_retries");
            }
        }

        public bool IsReady(LuaAdRecord record) => record.format == AdType.Reward
            ? MaxSdk.IsRewardedAdReady(record.adUnitId)
            : MaxSdk.IsInterstitialReady(record.adUnitId);

        public bool Show(LuaAdRecord record, string where)
        {
            if (!IsReady(record)) return false;
            if (record.format == AdType.Reward) MaxSdk.ShowRewardedAd(record.adUnitId, where);
            else MaxSdk.ShowInterstitial(record.adUnitId, where);
            return true;
        }
    }

    /// <summary>
    /// Interstitial / rewarded AdMob (GMA SDK) do Lua điều khiển. Mỗi BẢN LOAD (<see cref="LuaAdRecord.Key"/>)
    /// giữ tối đa một ad: cùng một ad unit AdMob có thể load nhiều bản song song (mỗi linkedAdUnitId một bản),
    /// SDK nhận adUnitId thật, còn ad + callback đi theo key của bản load.
    /// Giá sàn gửi qua extras "rusd" (cent eCPM), giá ad lấy từ response extras "ad_value_string" ngay lúc
    /// load xong — cùng cách tầng MetaAdmob đang làm.
    /// Callback GMA có thể về trên thread nền -> mọi thứ được đẩy về main thread qua <see cref="LuaAdMonetize.Post"/>.
    /// </summary>
    internal sealed class LuaAdmobLoader : ILuaAdLoader
    {
        private const string _BID_FLOOR_KEY = "rusd";
        private const string _AD_VALUE_KEY = "ad_value_string";
        private const string _AD_VALUE_CURRENCY_KEY = "ad_value_currency";

        //key = LuaAdRecord.Key
        private readonly Dictionary<string, InterstitialAd> _inter = new Dictionary<string, InterstitialAd>();
        private readonly Dictionary<string, RewardedAd> _reward = new Dictionary<string, RewardedAd>();

        public string Mediation => LuaAdRecord.MEDIATION_ADMOB;

        public void Load(LuaAdRecord record)
        {
            var adUnitId = record.adUnitId;
            var id = record.Key;
            var format = record.format;
            var startedUtc = DateTime.UtcNow;
            var request = new AdRequest();
            if (record.floor > 0d)
            {
                //floor là eCPM USD -> rusd tính bằng cent eCPM
                if (request.Extras == null) request.Extras = new Dictionary<string, string>();
                request.Extras[_BID_FLOOR_KEY] = (record.floor * 100d).ToString("0.##", CultureInfo.InvariantCulture);
            }

            if (format == AdType.Reward)
            {
                RewardedAd.Load(adUnitId, request, (ad, error) => LuaAdMonetize.Post(() =>
                {
                    var latencyMs = (long)(DateTime.UtcNow - startedUtc).TotalMilliseconds;
                    if (error != null || ad == null)
                    {
                        ad?.Destroy();
                        Failed(format, id, error, latencyMs);
                        return;
                    }

                    if (_reward.TryGetValue(id, out var old) && old != ad) old?.Destroy();
                    _reward[id] = ad;
                    AttachRewarded(id, ad);
                    Loaded(format, id, ad.GetResponseInfo(), latencyMs);
                }));
            }
            else
            {
                InterstitialAd.Load(adUnitId, request, (ad, error) => LuaAdMonetize.Post(() =>
                {
                    var latencyMs = (long)(DateTime.UtcNow - startedUtc).TotalMilliseconds;
                    if (error != null || ad == null)
                    {
                        ad?.Destroy();
                        Failed(format, id, error, latencyMs);
                        return;
                    }

                    if (_inter.TryGetValue(id, out var old) && old != ad) old?.Destroy();
                    _inter[id] = ad;
                    AttachInterstitial(id, ad);
                    Loaded(format, id, ad.GetResponseInfo(), latencyMs);
                }));
            }
        }

        private static void Failed(AdType format, string id, LoadAdError error, long latencyMs)
            => LuaAdMonetize.OnAdLoadFailed(format, id,
                error == null ? "no_ad" : $"{error.GetCode()} {error.GetMessage()}", latencyMs);

        private static void Loaded(AdType format, string id, ResponseInfo info, long latencyMs)
        {
            var network = info?.GetLoadedAdapterResponseInfo()?.AdSourceName;
            LuaAdMonetize.OnAdLoaded(format, id, ParseEcpm(info), string.IsNullOrEmpty(network) ? "AdMob" : network,
                latencyMs);
        }

        public bool IsReady(LuaAdRecord record)
        {
            if (record.format == AdType.Reward)
                return _reward.TryGetValue(record.Key, out var r) && r != null && r.CanShowAd();
            return _inter.TryGetValue(record.Key, out var i) && i != null && i.CanShowAd();
        }

        public bool Show(LuaAdRecord record, string where)
        {
            if (!IsReady(record)) return false;
            var id = record.Key;
            if (record.format == AdType.Reward)
                _reward[id].Show(_ => LuaAdMonetize.Post(() => LuaAdMonetize.OnAdRewarded(AdType.Reward, id)));
            else
                _inter[id].Show();
            return true;
        }

        private void AttachInterstitial(string id, InterstitialAd ad)
        {
            const AdType format = AdType.Interstitial;
            ad.OnAdFullScreenContentOpened += () => LuaAdMonetize.Post(() => LuaAdMonetize.OnAdDisplayed(format, id));
            ad.OnAdFullScreenContentClosed += () => LuaAdMonetize.Post(() =>
            {
                if (_inter.TryGetValue(id, out var cur) && cur == ad) _inter.Remove(id);
                ad.Destroy();
                LuaAdMonetize.OnAdHidden(format, id);
            });
            ad.OnAdFullScreenContentFailed += error => LuaAdMonetize.Post(() =>
            {
                if (_inter.TryGetValue(id, out var cur) && cur == ad) _inter.Remove(id);
                ad.Destroy();
                LuaAdMonetize.OnAdShowFailed(format, id, error?.GetCode().ToString());
            });
            ad.OnAdClicked += () => LuaAdMonetize.Post(() => LuaAdMonetize.OnAdClicked(format, id));
            ad.OnAdPaid += value => Paid(format, id, value);
        }

        private void AttachRewarded(string id, RewardedAd ad)
        {
            const AdType format = AdType.Reward;
            ad.OnAdFullScreenContentOpened += () => LuaAdMonetize.Post(() => LuaAdMonetize.OnAdDisplayed(format, id));
            ad.OnAdFullScreenContentClosed += () => LuaAdMonetize.Post(() =>
            {
                if (_reward.TryGetValue(id, out var cur) && cur == ad) _reward.Remove(id);
                ad.Destroy();
                LuaAdMonetize.OnAdHidden(format, id);
            });
            ad.OnAdFullScreenContentFailed += error => LuaAdMonetize.Post(() =>
            {
                if (_reward.TryGetValue(id, out var cur) && cur == ad) _reward.Remove(id);
                ad.Destroy();
                LuaAdMonetize.OnAdShowFailed(format, id, error?.GetCode().ToString());
            });
            ad.OnAdClicked += () => LuaAdMonetize.Post(() => LuaAdMonetize.OnAdClicked(format, id));
            ad.OnAdPaid += value => Paid(format, id, value);
        }

        private static void Paid(AdType format, string id, AdValue value)
        {
            if (value == null) return;
            var revenue = value.Value / 1_000_000d;
            var precision = value.Precision.ToString();
            var detail = new LuaPaidDetail { formatRaw = AdShowReporter.AdmobFormatRaw(format) };
            LuaAdMonetize.Post(() => LuaAdMonetize.OnAdPaid(format, id, revenue, precision, null, detail));
        }

        /// <summary>eCPM (USD) từ extras lúc load. 0 = không đọc được giá.</summary>
        private static double ParseEcpm(ResponseInfo info)
        {
            try
            {
                var extras = info?.GetResponseExtras();
                if (extras == null || !extras.TryGetValue(_AD_VALUE_KEY, out var raw) || string.IsNullOrEmpty(raw))
                    return 0d;
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value <= 0d)
                    return 0d;
                if (extras.TryGetValue(_AD_VALUE_CURRENCY_KEY, out var currency) && !string.IsNullOrEmpty(currency) &&
                    !currency.Equals("USD", StringComparison.OrdinalIgnoreCase))
                    return 0d;

                //tài khoản trả theo micros hoặc theo USD của 1 impression. 1 impression >= $1 (eCPM $1000)
                //là phi thực tế -> số >= 1 chắc chắn là micros.
                if (value >= 1d) value /= 1_000_000d;
                return value * 1000d;
            }
            catch
            {
                return 0d;
            }
        }
    }
}
#endif
