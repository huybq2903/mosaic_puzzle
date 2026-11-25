package com.falcon.dfpool;

import android.app.Activity;
import android.os.Bundle;
import com.applovin.mediation.MaxReward;
import com.applovin.mediation.adapter.MaxAdapterError;
import com.applovin.mediation.adapter.MaxInterstitialAdapter;
import com.applovin.mediation.adapter.MaxRewardedAdapter;
import com.applovin.mediation.adapter.listeners.MaxInterstitialAdapterListener;
import com.applovin.mediation.adapter.listeners.MaxRewardedAdapterListener;
import com.applovin.mediation.adapter.parameters.MaxAdapterInitializationParameters;
import com.applovin.mediation.adapter.parameters.MaxAdapterResponseParameters;
import com.applovin.mediation.adapters.MediationAdapterBase;
import com.applovin.sdk.AppLovinSdk;
import com.google.android.gms.ads.AdError;
import com.google.android.gms.ads.FullScreenContentCallback;
import com.google.android.gms.ads.interstitial.InterstitialAd;
import com.google.android.gms.ads.rewarded.RewardedAd;
import java.util.Locale;
import java.util.Map;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * MAX custom SDK adapter for DFPool slots. Register on the MAX dashboard:
 * Custom Network (SDK) -> Android adapter class name = {@code com.falcon.dfpool.DFPoolAdapter}.
 *
 * <p>Tier price: Custom Parameters {"floor": "12.5"} first, then the number in the Placement ID.
 * Bottom tier: Custom Parameters {"is_bottom": "true"} or a Placement ID containing "bottom"/"catch_all".
 */
public class DFPoolAdapter extends MediationAdapterBase
    implements MaxInterstitialAdapter, MaxRewardedAdapter {

  private static final String CUSTOM_PARAM_FLOOR_KEY = "floor";
  private static final String CUSTOM_PARAM_IS_BOTTOM_KEY = "is_bottom";
  private static final Pattern NUMBER_PATTERN = Pattern.compile("([0-9]+(?:\\.[0-9]+)?)");

  public DFPoolAdapter(AppLovinSdk sdk) {
    super(sdk);
  }

  @Override
  public void initialize(
      MaxAdapterInitializationParameters parameters,
      Activity activity,
      OnCompletionListener onCompletionListener) {
    onCompletionListener.onCompletion(InitializationStatus.INITIALIZED_SUCCESS, null);
  }

  @Override
  public String getSdkVersion() {
    return "1.0.0";
  }

  @Override
  public String getAdapterVersion() {
    return "1.0.0.0";
  }

  @Override
  public void onDestroy() {}

  // =========================================================================
  // INTERSTITIAL
  // =========================================================================

  @Override
  public void loadInterstitialAd(
      MaxAdapterResponseParameters parameters,
      Activity activity,
      MaxInterstitialAdapterListener listener) {
    if (shouldClaim(parameters)) listener.onInterstitialAdLoaded();
    else listener.onInterstitialAdLoadFailed(MaxAdapterError.NO_FILL);
  }

  @Override
  public void showInterstitialAd(
      MaxAdapterResponseParameters parameters,
      final Activity activity,
      final MaxInterstitialAdapterListener listener) {
    final String slotId = slotIdOf(parameters);
    final DFPoolBridge.PoolAd pooled =
        DFPoolBridge.takeForShow(slotId, DFPoolBridge.FORMAT_INTERSTITIAL);
    if (pooled == null || activity == null || !(pooled.ad instanceof InterstitialAd)) {
      // Flush show (slot released its ad) or ad is gone -> MAX drops the slot, nothing is rendered.
      DFPoolBridge.logD("[B4-FLUSH] " + slotId + " inter show -> AD_NOT_READY (show gia, khong hien gi)");
      listener.onInterstitialAdDisplayFailed(MaxAdapterError.AD_NOT_READY);
      return;
    }

    final InterstitialAd ad = (InterstitialAd) pooled.ad;
    DFPoolBridge.runOnMain(
        () -> {
          ad.setOnPaidEventListener(
              value -> DFPoolBridge.reportPaid(slotId, pooled, value, ad.getResponseInfo()));
          ad.setFullScreenContentCallback(
              new FullScreenContentCallback() {
                @Override
                public void onAdShowedFullScreenContent() {
                  listener.onInterstitialAdDisplayed();
                }

                @Override
                public void onAdFailedToShowFullScreenContent(AdError adError) {
                  listener.onInterstitialAdDisplayFailed(MaxAdapterError.INTERNAL_ERROR);
                }

                @Override
                public void onAdClicked() {
                  listener.onInterstitialAdClicked();
                }

                @Override
                public void onAdDismissedFullScreenContent() {
                  listener.onInterstitialAdHidden();
                }
              });
          ad.show(activity);
        });
  }

  // =========================================================================
  // REWARDED
  // =========================================================================

  @Override
  public void loadRewardedAd(
      MaxAdapterResponseParameters parameters,
      Activity activity,
      MaxRewardedAdapterListener listener) {
    if (shouldClaim(parameters)) listener.onRewardedAdLoaded();
    else listener.onRewardedAdLoadFailed(MaxAdapterError.NO_FILL);
  }

  @Override
  public void showRewardedAd(
      MaxAdapterResponseParameters parameters,
      final Activity activity,
      final MaxRewardedAdapterListener listener) {
    final String slotId = slotIdOf(parameters);
    final DFPoolBridge.PoolAd pooled =
        DFPoolBridge.takeForShow(slotId, DFPoolBridge.FORMAT_REWARDED);
    if (pooled == null || activity == null || !(pooled.ad instanceof RewardedAd)) {
      DFPoolBridge.logD("[B4-FLUSH] " + slotId + " rewarded show -> AD_NOT_READY (show gia, khong hien gi)");
      listener.onRewardedAdDisplayFailed(MaxAdapterError.AD_NOT_READY);
      return;
    }

    configureReward(parameters);
    final RewardedAd ad = (RewardedAd) pooled.ad;
    final AtomicBoolean earned = new AtomicBoolean(false);

    DFPoolBridge.runOnMain(
        () -> {
          ad.setOnPaidEventListener(
              value -> DFPoolBridge.reportPaid(slotId, pooled, value, ad.getResponseInfo()));
          ad.setFullScreenContentCallback(
              new FullScreenContentCallback() {
                @Override
                public void onAdShowedFullScreenContent() {
                  listener.onRewardedAdDisplayed();
                }

                @Override
                public void onAdFailedToShowFullScreenContent(AdError adError) {
                  listener.onRewardedAdDisplayFailed(MaxAdapterError.INTERNAL_ERROR);
                }

                @Override
                public void onAdClicked() {
                  listener.onRewardedAdClicked();
                }

                @Override
                public void onAdDismissedFullScreenContent() {
                  if (earned.get() || shouldAlwaysRewardUser()) {
                    MaxReward reward = getReward();
                    listener.onUserRewarded(reward);
                  }
                  listener.onRewardedAdHidden();
                }
              });
          ad.show(activity, rewardItem -> earned.set(true));
        });
  }

  // =========================================================================
  // HELPERS
  // =========================================================================

  private boolean shouldClaim(MaxAdapterResponseParameters parameters) {
    return DFPoolBridge.claimForWaterfall(
        slotIdOf(parameters), parseTierFloor(parameters), isBottomTier(parameters));
  }

  private static String slotIdOf(MaxAdapterResponseParameters parameters) {
    if (parameters == null) return null;
    Map<String, Object> extras = parameters.getLocalExtraParameters();
    if (extras != null) {
      Object v = extras.get(DFPoolBridge.EXTRA_SLOT_ID);
      if (v != null) return String.valueOf(v);
    }
    return parameters.getAdUnitId();
  }

  private static double parseTierFloor(MaxAdapterResponseParameters parameters) {
    if (parameters == null) return -1.0;
    Bundle custom = parameters.getCustomParameters();
    if (custom != null && custom.containsKey(CUSTOM_PARAM_FLOOR_KEY)) {
      Object raw = custom.get(CUSTOM_PARAM_FLOOR_KEY);
      double parsed = parseDoubleSafe(raw != null ? String.valueOf(raw) : null);
      if (parsed > 0) return parsed;
    }
    return parseDoubleSafe(parameters.getThirdPartyAdPlacementId());
  }

  private static boolean isBottomTier(MaxAdapterResponseParameters parameters) {
    if (parameters == null) return false;
    Bundle custom = parameters.getCustomParameters();
    if (custom != null && custom.containsKey(CUSTOM_PARAM_IS_BOTTOM_KEY)) {
      Object raw = custom.get(CUSTOM_PARAM_IS_BOTTOM_KEY);
      if (raw != null && Boolean.parseBoolean(String.valueOf(raw).trim())) return true;
    }
    String placementId = parameters.getThirdPartyAdPlacementId();
    if (placementId == null) return false;
    String lower = placementId.toLowerCase(Locale.US);
    return lower.contains("bottom") || lower.contains("catch_all");
  }

  private static double parseDoubleSafe(String raw) {
    if (raw == null || raw.trim().isEmpty()) return -1.0;
    String trimmed = raw.trim();
    try {
      return Double.parseDouble(trimmed);
    } catch (NumberFormatException ignored) {
      Matcher m = NUMBER_PATTERN.matcher(trimmed);
      String last = null;
      while (m.find()) last = m.group(1);
      if (last != null) {
        try {
          return Double.parseDouble(last);
        } catch (NumberFormatException ignoredAgain) {
          // fall through
        }
      }
    }
    return -1.0;
  }
}
