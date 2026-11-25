/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2026-10-27
 */

using System;
using System.Collections.Generic;
using Falcon.Helpers.Devkit;
using Falcon.Modules.Core.BigData;
using Falcon.Modules.Core.ThirdParty.Firebase.Scripts.Runtime;
using Falcon.Modules.Core.ThirdParty.Mmp.Runtime;
using Falcon.Modules.Level.Core;
using UnityEngine;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime
{
    /// <summary>
    /// Analytics các lượt show tự quản (Lua, DFPool…). Cùng bộ kênh mà FalconMaxService / bridge AdMob
    /// dùng: Firebase ad_impression + MMP logAdRevenue + BigData + sổ LTV client.
    ///
    /// Các handler chung đã BỎ QUA slot DFPool (<see cref="ExternalMaxAdUnits"/>) nên đây là cửa log
    /// DUY NHẤT của chúng — không đếm đôi.
    ///  - Ad AdMob mượn: doanh thu lấy từ OnPaidEvent của AdMob (thật), mediation = "Admob".
    ///  - Slot bị network khác của MAX fill (vd AppLovin Exchange): doanh thu lấy từ MAX, mediation = "Max".
    /// </summary>
    internal static class AdShowReporter
    {
        public const string MEDIATION_ADMOB = "Admob";
        public const string MEDIATION_MAX = "Max";
        private const string _PLATFORM_ADMOB = "AdMob";
        private const string _PLATFORM_MAX = "Max";

        public struct Impression
        {
            public AdType type;
            public string mediation;
            public string adUnitId;
            public string network;
            public double revenue;
            public string precision;
            public string where;
            public double? floorEcpmUsd;
            public double? estimatedEcpmUsd;
            public int? waterfallPosition;
            public string instanceName;
            public string formatRaw;
            public Dictionary<string, object> extraMeta;
            public bool bigDataOnly;
        }

        public static void Requested(AdType type)
        {
            Safe(() => AdViewRequestGate.Open(type, MEDIATION_ADMOB, AdViewRequestGate.ALGORITHM_DFPOOL));
        }

        public static void LoadResult(AdType type, string adUnitId, bool success, double multiplier,
            double floorEcpmUsd, double? estimatedEcpmUsd, string network, string error, long loadingMs)
        {
            Safe(() => FalconBigDataController.Ad.OnLoadResult(new AdLoadResultParam
            {
                adType = type,
                adUnitId = adUnitId,
                success = success,
                adMediation = MEDIATION_ADMOB,
                tier = multiplier > 0d ? multiplier : (double?)null,
                //như meta AdMob: không đặt sàn (ad mặc định) -> floor VẮNG, ecpmFloor = 0 (sàn thật đã xin)
                floor = floorEcpmUsd > 0d ? floorEcpmUsd : (double?)null,
                ecpmFloor = floorEcpmUsd,
                estimatedEcpm = estimatedEcpmUsd,
                networkName = network,
                errorMess = error,
                loadingMs = loadingMs
            }));
        }

        public static void Shown(AdType type, string where, string mediation, string adUnitId = null)
            => Safe(() => FalconBigDataController.Ad.OnShown(new AdShowParam
            {
                type = type,
                adWhere = string.IsNullOrWhiteSpace(where) ? null : where,
                adMediation = mediation,
                adUnitId = string.IsNullOrEmpty(adUnitId) ? null : adUnitId
            }));

        public static void Clicked(AdType type)
            => Safe(() => FalconBigDataController.Ad.OnClicked(type));

        public static void ShowFailed(AdType type, string errorCode, string adUnitId, string network,
            double? floorEcpmUsd, double? estimatedEcpmUsd = null)
        {
            Safe(() =>
            {
                FalconBigDataController.Ad.OnShowFailed(type,
                    string.IsNullOrEmpty(errorCode) ? "unknown" : errorCode,
                    adUnitId, network, floorEcpmUsd, estimatedEcpmUsd);
                AdViewRequestGate.Close(type);
            });
        }

        public static void Closed(AdType type, bool? completed, string where)
        {
            Safe(() =>
            {
                FalconBigDataController.Ad.OnClosed(type, adCompleted: completed,
                    adWhere: string.IsNullOrWhiteSpace(where) ? null : where);
                AdViewRequestGate.Close(type);
            });
        }

        public static void Revenue(Impression info)
        {
            Safe(() =>
            {
                var adFormat = info.type.ToString();
                var adUnitId = string.IsNullOrEmpty(info.adUnitId) ? "unknown" : info.adUnitId;
                var where = string.IsNullOrEmpty(info.where) ? adFormat : info.where;
                var network = string.IsNullOrEmpty(info.network) ? "AdMob" : info.network;
                var platform = info.mediation == MEDIATION_MAX ? _PLATFORM_MAX : _PLATFORM_ADMOB;

                if (!info.bigDataOnly)
                {
                    FalconFirebaseLog.LogImpression(platform, network, adUnitId, adFormat, info.revenue);
                    FalconMmpLog.LogRevenue(network, adFormat, info.revenue, adUnitId, where);
                    MarkRevenueSentToMmp();
                    LogBambooTaichi.Instance.Log(info.revenue, info.type);
                    AddClientAdInfo(info.type, info.revenue);
                }

                var param = new AdParam
                {
                    type = info.type,
                    adMediation = info.mediation,
                    adNetwork = network,
                    adUnitId = adUnitId,
                    adFormatRaw = string.IsNullOrEmpty(info.formatRaw) ? adFormat : info.formatRaw,
                    adInstanceName = string.IsNullOrEmpty(info.instanceName) ? null : info.instanceName,
                    adPrecision = info.precision,
                    adRev = info.revenue,
                    adCountry = CountryCode(),
                    currentLevel = CurrentLevel(),
                    ecpmFloor = info.floorEcpmUsd,
                    estimatedEcpm = info.estimatedEcpmUsd,
                    waterfallPosition = info.waterfallPosition,
                    extraMeta = info.extraMeta is { Count: > 0 } ? info.extraMeta : null
                };
                if (!string.IsNullOrWhiteSpace(info.where)) param.adWhere = info.where;
                FalconBigDataController.Ad.OnImpression(param);
            });
        }

#if MAX_ENABLE
        public static Impression WithMax(Impression info, MaxSdkBase.AdInfo adInfo)
        {
            if (adInfo == null) return info;
            info.instanceName = adInfo.NetworkPlacement;
            info.formatRaw = adInfo.AdFormat;
            info.waterfallPosition = LoadedWaterfallPosition(adInfo);
            info.extraMeta = MaxExtraMeta(adInfo);
            return info;
        }

        public static int? LoadedWaterfallPosition(MaxSdkBase.AdInfo adInfo)
        {
            var responses = adInfo?.WaterfallInfo?.NetworkResponses;
            if (responses == null) return null;
            for (var i = 0; i < responses.Count; i++)
                if (responses[i].AdLoadState == MaxSdkBase.MaxAdLoadState.AdLoaded)
                    return i + 1;
            return null;
        }

        public static Dictionary<string, object> MaxExtraMeta(MaxSdkBase.AdInfo adInfo)
        {
            if (adInfo == null) return null;
            var meta = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(adInfo.CreativeIdentifier)) meta.Add("creative_id", adInfo.CreativeIdentifier);
            if (!string.IsNullOrEmpty(adInfo.DspName)) meta.Add("dsp", adInfo.DspName);
            return meta.Count > 0 ? meta : null;
        }
#endif

        public static string AdmobFormatRaw(AdType type) => type == AdType.Reward ? "Rewarded" : "Interstitial";

        /// <summary>AdValue.PrecisionType (int của native) -> chuỗi, cùng cách tầng AdMob ghi.</summary>
        public static string PrecisionName(int precision) => precision switch
        {
            1 => "Estimated",
            2 => "PublisherProvided",
            3 => "Precise",
            _ => "Unknown"
        };

        private static void AddClientAdInfo(AdType type, double revenue)
        {
            var data = GameData4AdInfo.Instance;
            data.adTypeToInfo ??= new Dictionary<AdType, AdInfo>();
            if (data.adTypeToInfo.TryGetValue(type, out var adInfo))
            {
                adInfo.ltv += revenue;
                adInfo.count++;
            }
            else
            {
                data.adTypeToInfo.Add(type, new AdInfo { count = 1, ltv = revenue });
            }

            data.UpdateToServer();
        }

        private static void MarkRevenueSentToMmp()
        {
            var data = GameData4AntiFraude.Instance;
            if (data.sendDataToAppsflyer) return;
            data.sendDataToAppsflyer = true;
            data.UpdateToServer();
        }

        private static string CountryCode()
        {
#if MAX_ENABLE
            try
            {
                return MaxSdk.GetSdkConfiguration()?.CountryCode;
            }
            catch (Exception)
            {
                return null;
            }
#else
            return null;
#endif
        }

        private static int CurrentLevel()
        {
            try
            {
                return LevelData.Instance.level;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static void Safe(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
            }
        }
    }
}
