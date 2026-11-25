using Falcon.Modules.Core.BigData;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime
{
    internal static class AdSkipReasonMap
    {
        public static AdSkipReason From(StatusShowAds status)
        {
            switch (status)
            {
                case StatusShowAds.IntervalTime: return AdSkipReason.Cooldown;
                case StatusShowAds.IntervalBetweenIv: return AdSkipReason.PostAdGrace;
                case StatusShowAds.IntervalLevel: return AdSkipReason.LevelInterval;
                case StatusShowAds.LevelUnlock: return AdSkipReason.LevelGate;
                case StatusShowAds.DayLimit: return AdSkipReason.DailyCap;
                case StatusShowAds.DayLimitGroup: return AdSkipReason.DailyCap;
                case StatusShowAds.SessionLimit: return AdSkipReason.SessionCap;
                case StatusShowAds.DeActive: return AdSkipReason.Disabled;
                case StatusShowAds.LevelLimit: return AdSkipReason.LevelCap;
                default: return AdSkipReason.Other;
            }
        }
    }
}
