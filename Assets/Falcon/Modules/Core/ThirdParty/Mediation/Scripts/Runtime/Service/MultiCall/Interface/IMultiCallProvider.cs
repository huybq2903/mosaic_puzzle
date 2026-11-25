/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2025-10-28
 */

using System;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime
{
    public interface IMultiCallProvider
    {
        int GetIndex();
        bool IsInterstitialReady();
        Falcon.Modules.Core.BigData.AdReadyInfo PeekInterstitial();
        void ShowInterstitial(string where, Action onRewardedComplete, Action onFailed, bool showForRewarded);
        bool IsRewardedVideoReady();

        /// <param name="allowInterstitial">
        /// false = người gọi KHÔNG cho chiếu inter thay rewarded -> chỉ so giá các ad rewarded.
        /// </param>
        void ShowRewardedVideo(string where, Action onRewardedComplete, Action onFailed = null,
            bool allowInterstitial = true);

        /// <summary>
        /// Giá (USD / impression) ad MAX đắt nhất ĐANG SẴN SÀNG trong SDK của format, KHÔNG tính nguồn phụ;
        /// &lt; 0 = không có. Không soát/không kích load (chỉ đọc) — dùng để so giá với nguồn ngoài MultiCall.
        /// </summary>
        double BestMaxRevenue(Falcon.Helpers.Devkit.AdType type);
        void RegisterEvent();
        void LoadInterstitial();

        /// <summary>Nạp lại interstitial sau một quãng bị Floor Personalization chặn load.</summary>
        void ResumeInterstitialLoading();
        void LoadRewarded();
    }
}