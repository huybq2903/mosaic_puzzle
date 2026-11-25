/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2026-10-21
 */

using Falcon.Helpers.Devkit;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime
{
    /// <summary>
    /// Số ad fullscreen ĐÃ lên hình trong lần mở app này, đếm riêng Interstitial / Rewarded — nguồn
    /// DUY NHẤT cho <c>ad_index_session</c> của Databuckets và <c>ad_index</c> gửi Subsequent Ad Rule.
    /// Ad đang load / đang chiếu có index = số đã chiếu + 1.
    /// <br/>Đếm ở mốc displayed của MAX (FalconMaxService, mọi ad unit, cả nhánh multicall) và mốc hidden
    /// của nguồn phụ AdMob (không có mốc displayed riêng). Interstitial chiếu thay rewarded tính là Interstitial.
    /// </summary>
    public static class AdSessionCounter
    {
        public static int InterstitialShown { get; private set; }
        public static int RewardedShown { get; private set; }

        public static int Shown(AdType type) => type == AdType.Reward ? RewardedShown : InterstitialShown;

        internal static void OnInterstitialShown() => InterstitialShown++;

        internal static void OnRewardedShown() => RewardedShown++;
    }
}
