using System.Collections.Generic;
using Falcon.Helpers.Devkit;
using UnityEngine;
using UnityEngine.Scripting;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime
{
    public sealed class FloorPersonalizationSnapshot
    {
        public readonly string deviceTier;
        public readonly string[] segments;
        public readonly string[] variants;

        public FloorPersonalizationSnapshot(string deviceTier, string[] segments, string[] variants)
        {
            this.deviceTier = deviceTier;
            this.segments = segments;
            this.variants = variants;
        }
    }

    public static class FloorPersonalizationUserInfo
    {
        public const string KEY_DEVICE_TIER = "deviceTier";
        public const string KEY_REALTIME_SEGMENTS = "realtimeSegments";
        public const string KEY_REALTIME_VARIANTS = "realtimeVariants";

        private const string _PREF_DEVICE_TIER = "falcon_mediation_floor_device_tier";

        private static volatile FloorPersonalizationSnapshot _snapshot;

        public static FloorPersonalizationSnapshot Snapshot => _snapshot;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RestorePersistedDeviceTier()
        {
            var tier = PlayerPrefs.GetString(_PREF_DEVICE_TIER, null);
            if (string.IsNullOrEmpty(tier) || _snapshot != null) return;
            _snapshot = new FloorPersonalizationSnapshot(tier, null, null);
        }

        public static void Update(FloorRuleResponse response)
        {
            if (response == null) return;

            var tier = NonBlank(response.config?.deviceTier) ?? _snapshot?.deviceTier;
            _snapshot = new FloorPersonalizationSnapshot(tier, AsArray(response.realtimeSegment),
                AsArray(response.realtimeVariant));

            if (tier == null || tier == PlayerPrefs.GetString(_PREF_DEVICE_TIER, null)) return;
            PlayerPrefs.SetString(_PREF_DEVICE_TIER, tier);
            PlayerPrefs.Save();
        }

        private static string NonBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string[] AsArray(string value)
        {
            var trimmed = NonBlank(value);
            return trimmed == null ? null : new[] { trimmed };
        }
    }

    //tạo bằng reflection (FReflection) — assembly riêng không ai tham chiếu tĩnh, giữ khỏi bị strip
    [Preserve]
    public class FloorPersonalizationCustomInfo : IFCustomInfoRepository
    {
        public Dictionary<string, object> GetInfo()
        {
            var info = new Dictionary<string, object>();
            var snapshot = FloorPersonalizationUserInfo.Snapshot;
            if (snapshot == null) return info;

            if (!string.IsNullOrEmpty(snapshot.deviceTier))
                info[FloorPersonalizationUserInfo.KEY_DEVICE_TIER] = snapshot.deviceTier;
            if (snapshot.segments is { Length: > 0 })
                info[FloorPersonalizationUserInfo.KEY_REALTIME_SEGMENTS] = snapshot.segments;
            if (snapshot.variants is { Length: > 0 })
                info[FloorPersonalizationUserInfo.KEY_REALTIME_VARIANTS] = snapshot.variants;
            return info;
        }
    }
}
