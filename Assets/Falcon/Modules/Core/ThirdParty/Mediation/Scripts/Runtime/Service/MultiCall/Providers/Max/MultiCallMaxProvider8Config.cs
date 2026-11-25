using System;
using Falcon.Helpers.Devkit;
using Falcon.Modules.Core.RemoteConfigCms;
using UnityEngine;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime
{
    public class MultiCallMaxProvider8Config : FConfigCms
    {
        public string moMulMaxProvider8Config = "";

        public override void OnData()
        {
        }

        private static string _parsedJson;
        private static MultiCallMaxProvider8Settings _settings;

        public static MultiCallMaxProvider8Settings Current
        {
            get
            {
                var json = FConfigControllerCms.Instance.Config<MultiCallMaxProvider8Config>()
                    ?.moMulMaxProvider8Config ?? string.Empty;
                if (_settings != null && json == _parsedJson) return _settings;

                var firstRead = _settings == null;
                _parsedJson = json;
                _settings = MultiCallMaxProvider8Settings.Parse(json);
                MediationManager.Instance.DebugLog("[MAX algorithm 8][CONFIG] " +
                                                   (firstRead ? "đọc lần đầu" : "CMS đổi chuỗi -> đọc lại") +
                                                   " key moMulMaxProvider8Config " +
                                                   (string.IsNullOrWhiteSpace(json)
                                                       ? "RỖNG -> dùng toàn bộ mặc định"
                                                       : "(" + json.Length + " ký tự): " + json) +
                                                   "\n  -> sau parse: " + _settings.Describe());
                return _settings;
            }
        }
    }

    [Serializable]
    public class MultiCallMaxProvider8Settings
    {
        public float networkCheckIntervalSeconds = 2f;
        public MultiCallMaxProvider8FormatSettings interstitial = new();
        public MultiCallMaxProvider8FormatSettings rewarded = new();

        private const float _MIN_NETWORK_CHECK_SECONDS = 0.5f;

        public float NetworkCheckInterval => Mathf.Max(_MIN_NETWORK_CHECK_SECONDS, networkCheckIntervalSeconds);

        public MultiCallMaxProvider8FormatSettings For(bool isRewarded) => isRewarded ? rewarded : interstitial;

        public static MultiCallMaxProvider8Settings Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new MultiCallMaxProvider8Settings();

            try
            {
                var settings = json.JsonToObj<MultiCallMaxProvider8Settings>() ?? new MultiCallMaxProvider8Settings();
                settings.interstitial ??= new MultiCallMaxProvider8FormatSettings();
                settings.rewarded ??= new MultiCallMaxProvider8FormatSettings();
                settings.interstitial.Normalize();
                settings.rewarded.Normalize();
                return settings;
            }
            catch (Exception e)
            {
                MediationManager.Instance.DebugError("[MAX algorithm 8][CONFIG] JSON hỏng -> dùng toàn bộ mặc định: " +
                                                     e.Message);
                return new MultiCallMaxProvider8Settings();
            }
        }

        public string Describe()
            => $"networkCheck={NetworkCheckInterval}s\n  interstitial: {interstitial.Describe()}\n" +
               $"  rewarded: {rewarded.Describe()}";
    }

    [Serializable]
    public class MultiCallMaxProvider8FormatSettings
    {
        public float defaultLoadDelaySeconds = 5f;
        public AdUnitLoadDelay[] defaultLoadDelayByAdUnit = Array.Empty<AdUnitLoadDelay>();
        public int maxConcurrentFloor = 3;
        public int failsToDecreaseMultiplier = 5;
        public int maxFails = 0;
        public AdUnitFailLimit[] maxFailsByAdUnit = Array.Empty<AdUnitFailLimit>();
        public int maxCached = 0;

        public int MaxConcurrentFloor => Mathf.Max(1, maxConcurrentFloor);

        public void Normalize()
        {
            defaultLoadDelayByAdUnit ??= Array.Empty<AdUnitLoadDelay>();
            maxFailsByAdUnit ??= Array.Empty<AdUnitFailLimit>();
        }

        public bool TryGetDefaultLoadDelay(string adUnitId, out float delay)
        {
            foreach (var entry in defaultLoadDelayByAdUnit)
            {
                if (entry == null || entry.adUnitId != adUnitId) continue;

                delay = Mathf.Max(0f, entry.delaySeconds);
                return true;
            }

            delay = 0f;
            return false;
        }

        public float DefaultLoadDelay(string adUnitId, int order)
            => TryGetDefaultLoadDelay(adUnitId, out var delay) ? delay : order * Mathf.Max(0f, defaultLoadDelaySeconds);

        public int MaxFails(string adUnitId)
        {
            foreach (var entry in maxFailsByAdUnit)
            {
                if (entry != null && entry.adUnitId == adUnitId) return entry.maxFails;
            }

            return maxFails;
        }

        public string Describe()
        {
            var delays = new string[defaultLoadDelayByAdUnit.Length];
            for (var i = 0; i < delays.Length; i++)
            {
                var entry = defaultLoadDelayByAdUnit[i];
                delays[i] = entry == null ? "null" : entry.adUnitId + "=" + entry.delaySeconds + "s";
            }

            var limits = new string[maxFailsByAdUnit.Length];
            for (var i = 0; i < limits.Length; i++)
            {
                var entry = maxFailsByAdUnit[i];
                limits[i] = entry == null ? "null" : entry.adUnitId + "=" + entry.maxFails;
            }

            return $"defaultDelay={Mathf.Max(0f, defaultLoadDelaySeconds)}s/thứ tự [{string.Join(", ", delays)}] " +
                   $"concurrentFloor={MaxConcurrentFloor} failsToDecrease={failsToDecreaseMultiplier} " +
                   $"maxFails={maxFails} [{string.Join(", ", limits)}] " +
                   $"maxCached={maxCached}";
        }
    }

    [Serializable]
    public class AdUnitLoadDelay
    {
        public string adUnitId;
        public float delaySeconds;
    }

    [Serializable]
    public class AdUnitFailLimit
    {
        public string adUnitId;
        public int maxFails;
    }
}
