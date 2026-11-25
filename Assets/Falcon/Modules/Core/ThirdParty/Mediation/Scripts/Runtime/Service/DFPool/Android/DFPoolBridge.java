package com.falcon.dfpool;

import android.app.Activity;
import android.content.Context;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;
import com.google.ads.mediation.admob.AdMobAdapter;
import com.google.android.gms.ads.AdRequest;
import com.google.android.gms.ads.AdValue;
import com.google.android.gms.ads.AdapterResponseInfo;
import com.google.android.gms.ads.LoadAdError;
import com.google.android.gms.ads.MobileAds;
import com.google.android.gms.ads.ResponseInfo;
import com.google.android.gms.ads.interstitial.InterstitialAd;
import com.google.android.gms.ads.interstitial.InterstitialAdLoadCallback;
import com.google.android.gms.ads.rewarded.RewardedAd;
import com.google.android.gms.ads.rewarded.RewardedAdLoadCallback;
import java.math.BigDecimal;
import java.math.RoundingMode;
import java.util.Locale;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicInteger;

/**
 * AdMob ad POOL + table of "which MAX slot is borrowing which ad".
 *
 * <p>Deliberately dumb: every decision (which ad goes to which slot, when to release, evict, expire)
 * lives in C# (DFPoolFormat.cs, single-threaded). This layer only holds AdMob objects and answers the adapter.
 *
 * <ul>
 *   <li>Pool: handle -> loaded AdMob ad (with its value at load). Releasing a slot does NOT remove the ad.
 *   <li>Slot: maxSlotId -> borrowed handle + state. The MAX waterfall only reads the PRICE of the borrowed
 *       ad to pick a tier; the ad object is taken out of the pool only at show time.
 * </ul>
 */
public final class DFPoolBridge {
  static final String TAG = "DFPool";

  public static final int FORMAT_INTERSTITIAL = 0;
  public static final int FORMAT_REWARDED = 1;

  // Slot states - C# uses the same numbers (DFPoolNative.SlotState).
  public static final int SLOT_NONE = 0;
  /** Ad borrowed, waiting for the first MAX tier <= ad price. */
  public static final int SLOT_PENDING = 1;
  /** Waterfall claimed a tier - slot is ready to show the borrowed ad. */
  public static final int SLOT_CLAIMED = 2;
  /** Released while the waterfall is running: next tier is claimed empty so MAX stops right away. */
  public static final int SLOT_HOLLOW_PENDING = 3;
  /** Released after claim: MAX holds an empty ad; a flush show returns AD_NOT_READY to free the slot. */
  public static final int SLOT_HOLLOW_CLAIMED = 4;
  /** Borrowed ad was taken out of the pool and is on screen. */
  public static final int SLOT_SHOWING = 5;

  /** MAX local extra carrying the slot id to the adapter (fallback when getAdUnitId() is null). */
  public static final String EXTRA_SLOT_ID = "dfpool_slot_id";

  private static final String KEY_RUSD = "rusd";
  // Google multi-platform mediation extras - same keys/values as the C# meta AdMob layer
  // (AdmobMultiPlatformUtils.AUCTION_CONTEXT_KEY / USER_ACQUISITION_CHANNEL_KEY / DAYS_SINCE_INSTALL_KEY).
  private static final String KEY_AUCTION_CONTEXT = "auction_context";
  private static final String AUCTION_CONTEXT_PARALLEL = "parallel";
  private static final String KEY_UA_CHANNEL = "ua_channel";
  private static final String KEY_DAYS_SINCE_INSTALL = "days_since_install";
  private static final String KEY_AD_VALUE_STRING = "ad_value_string";
  private static final String KEY_AD_VALUE = "ad_value";
  private static final String KEY_AD_VALUE_CURRENCY = "ad_value_currency";

  private static final Handler mainHandler = new Handler(Looper.getMainLooper());
  private static final AtomicBoolean mobileAdsInitCalled = new AtomicBoolean(false);

  private static volatile DFPoolListener listener;
  private static volatile Context appContext;
  private static volatile boolean debugLog;
  private static volatile boolean adValueInMicros;

  private DFPoolBridge() {}

  static final class PoolAd {
    final int handle;
    final int format;
    final String adUnitId;
    final Object ad;
    final double valueUsd;
    final String adSource;

    PoolAd(int handle, int format, String adUnitId, Object ad, double valueUsd, String adSource) {
      this.handle = handle;
      this.format = format;
      this.adUnitId = adUnitId;
      this.ad = ad;
      this.valueUsd = valueUsd;
      this.adSource = adSource;
    }
  }

  static final class Binding {
    // mutable only while PENDING (rebindPending), always under synchronized(this)
    volatile int handle;
    volatile double valueUsd;
    final AtomicInteger state = new AtomicInteger(SLOT_PENDING);

    Binding(int handle, double valueUsd) {
      this.handle = handle;
      this.valueUsd = valueUsd;
    }
  }

  private static final Map<Integer, PoolAd> pool = new ConcurrentHashMap<>();
  private static final Map<String, Binding> bindings = new ConcurrentHashMap<>();

  static void logD(String message) {
    if (debugLog) Log.d(TAG, message);
  }

  // =========================================================================
  // API CHO UNITY
  // =========================================================================

  /**
   * Sets the listener and initializes Google Mobile Ads (idempotent - fine if another AdMob layer already
   * did). Must run AFTER consent, so C# only calls it once MAX is initialized.
   */
  public static void init(
      Activity activity, DFPoolListener unityListener, boolean debug, boolean valueInMicros) {
    listener = unityListener;
    debugLog = debug;
    adValueInMicros = valueInMicros;
    if (activity != null) appContext = activity.getApplicationContext();

    if (appContext != null && mobileAdsInitCalled.compareAndSet(false, true)) {
      final Context ctx = appContext;
      mainHandler.post(
          () -> {
            try {
              MobileAds.initialize(ctx, status -> logD("[B0-INIT] MobileAds initialized"));
            } catch (Throwable t) {
              Log.e(TAG, "MobileAds.initialize failed", t);
            }
          });
    }
    logD("[B0-INIT] init (debug=" + debug + ", adValueInMicros=" + valueInMicros + ")");
  }

  /**
   * Requests one AdMob ad at floor {@code floorEcpmUsd} (USD per 1000 impressions, sent as the rusd extra in
   * cents). {@code handle} is assigned by C# and becomes the pool key if the load succeeds.
   */
  public static void loadAd(
      final int format,
      final int handle,
      final String adUnitId,
      final double floorEcpmUsd,
      final String uaChannel,
      final String daysSinceInstall) {
    final Context ctx = appContext;
    final DFPoolListener l = listener;
    if (ctx == null || adUnitId == null || adUnitId.isEmpty()) {
      if (l != null) l.onAdFailed(handle, -1, "not initialized or empty ad unit", null);
      return;
    }

    final AdRequest request = buildRequest(floorEcpmUsd, uaChannel, daysSinceInstall);
    logD(
        String.format(
            Locale.US,
            "[B2-ADMOB-REQ] #%d %s unit=%s floorEcpm=%.4f",
            handle,
            format == FORMAT_REWARDED ? "rewarded" : "inter",
            adUnitId,
            floorEcpmUsd));

    mainHandler.post(
        () -> {
          try {
            if (format == FORMAT_REWARDED) {
              RewardedAd.load(
                  ctx,
                  adUnitId,
                  request,
                  new RewardedAdLoadCallback() {
                    @Override
                    public void onAdLoaded(RewardedAd ad) {
                      onLoaded(format, handle, adUnitId, ad, ad.getResponseInfo(), floorEcpmUsd);
                    }

                    @Override
                    public void onAdFailedToLoad(LoadAdError error) {
                      onFailed(handle, error);
                    }
                  });
            } else {
              InterstitialAd.load(
                  ctx,
                  adUnitId,
                  request,
                  new InterstitialAdLoadCallback() {
                    @Override
                    public void onAdLoaded(InterstitialAd ad) {
                      onLoaded(format, handle, adUnitId, ad, ad.getResponseInfo(), floorEcpmUsd);
                    }

                    @Override
                    public void onAdFailedToLoad(LoadAdError error) {
                      onFailed(handle, error);
                    }
                  });
            }
          } catch (Throwable t) {
            Log.e(TAG, "load failed", t);
            DFPoolListener cb = listener;
            if (cb != null) cb.onAdFailed(handle, -2, String.valueOf(t.getMessage()), null);
          }
        });
  }

  /** Drops an ad from the pool (expired / evicted). AdMob has no destroy, releasing the reference is enough. */
  public static void destroyAd(int handle) {
    if (pool.remove(handle) != null) logD("[POOL] destroy #" + handle);
  }

  /** Slot borrows ad {@code handle}. Returns false if the ad is no longer in the pool. */
  public static boolean bindSlot(String slotId, int handle) {
    if (slotId == null) return false;
    PoolAd ad = pool.get(handle);
    if (ad == null) return false;
    bindings.put(slotId, new Binding(handle, ad.valueUsd));
    logD(String.format(Locale.US, "[B3-BIND] %s <- #%d value=%.6f", slotId, handle, ad.valueUsd));
    return true;
  }

  /**
   * Swaps the ad borrowed by a slot whose waterfall has NOT claimed a tier yet (state PENDING). The
   * next tier the waterfall reaches is matched against the new ad price. Returns false when the
   * waterfall already claimed (or the slot was released) - caller must flush instead.
   */
  public static boolean rebindPending(String slotId, int handle) {
    Binding b = slotId == null ? null : bindings.get(slotId);
    PoolAd ad = pool.get(handle);
    if (b == null || ad == null) return false;
    synchronized (b) {
      if (b.state.get() != SLOT_PENDING) return false;
      int old = b.handle;
      b.handle = handle;
      b.valueUsd = ad.valueUsd;
      logD(String.format(Locale.US, "[B3-REBIND] %s #%d -> #%d value=%.6f", slotId, old, handle, ad.valueUsd));
      return true;
    }
  }

  /**
   * Slot gives its ad back (the ad STAYS in the pool). Waterfall running -> next tier is claimed empty;
   * already claimed -> the next show (flush show) returns AD_NOT_READY so MAX frees the slot.
   */
  public static void releaseSlot(String slotId) {
    if (slotId == null) return;
    Binding b = bindings.get(slotId);
    if (b == null) return;
    while (true) {
      int s = b.state.get();
      int next;
      if (s == SLOT_PENDING) next = SLOT_HOLLOW_PENDING;
      else if (s == SLOT_CLAIMED) next = SLOT_HOLLOW_CLAIMED;
      else return;
      if (b.state.compareAndSet(s, next)) {
        logD("[B4-FLUSH] " + slotId + " release #" + b.handle + " -> state " + (next == SLOT_HOLLOW_PENDING ? "HOLLOW_PENDING (tier ke tiep nhan rong)" : "HOLLOW_CLAIMED (cho show gia)"));
        return;
      }
    }
  }

  /** Removes the slot state entirely (after MAX freed the slot / load failed / show finished). */
  public static void clearSlot(String slotId) {
    if (slotId != null && bindings.remove(slotId) != null) logD("[SLOT] " + slotId + " cleared");
  }

  public static int getSlotState(String slotId) {
    Binding b = slotId == null ? null : bindings.get(slotId);
    return b == null ? SLOT_NONE : b.state.get();
  }

  // =========================================================================
  // API CHO ADAPTER MAX
  // =========================================================================

  /**
   * Called by the MAX waterfall for tier {@code tierEcpmUsd} of a slot. Claims (true) when:
   *
   * <ul>
   *   <li>the slot was released mid-load -> claim empty so MAX stops (never falls to backfill);
   *   <li>tier <= borrowed ad price, or this is the bottom (catch-all) tier.
   * </ul>
   */
  static boolean claimForWaterfall(String slotId, double tierEcpmUsd, boolean isBottom) {
    Binding b = slotId == null ? null : bindings.get(slotId);
    if (b == null) return false;

    if (b.state.compareAndSet(SLOT_HOLLOW_PENDING, SLOT_HOLLOW_CLAIMED)) {
      logD("[B4-FLUSH] " + slotId + " hollow-claim tier=" + tierEcpmUsd);
      return true;
    }
    double adEcpm;
    boolean claimed;
    synchronized (b) {
      // same lock as rebindPending: handle/value cannot change between the price check and the claim
      if (b.state.get() != SLOT_PENDING) return b.state.compareAndSet(SLOT_HOLLOW_PENDING, SLOT_HOLLOW_CLAIMED);
      if (!pool.containsKey(b.handle)) return false; // ad was dropped -> let the waterfall continue
      adEcpm = b.valueUsd * 1000.0;
      boolean match = (tierEcpmUsd > 0 && tierEcpmUsd <= adEcpm) || isBottom;
      claimed = match && b.state.compareAndSet(SLOT_PENDING, SLOT_CLAIMED);
    }
    if (claimed) {
      logD(
          String.format(
              Locale.US,
              "[B3-WATERFALL] %s CLAIM tier=%.4f (ad ecpm=%.4f, bottom=%b)",
              slotId,
              tierEcpmUsd,
              adEcpm,
              isBottom));
      return true;
    }
    // releaseSlot ran concurrently -> claim empty
    if (b.state.compareAndSet(SLOT_HOLLOW_PENDING, SLOT_HOLLOW_CLAIMED)) return true;
    logD(String.format(Locale.US, "[B3-WATERFALL] %s skip tier=%.4f (ad ecpm=%.4f)", slotId, tierEcpmUsd, adEcpm));
    return false;
  }

  /** Takes the borrowed ad out of the pool to show it. null = flush show / ad gone -> adapter returns AD_NOT_READY. */
  static PoolAd takeForShow(String slotId, int format) {
    Binding b = slotId == null ? null : bindings.get(slotId);
    if (b == null) return null;
    PoolAd ad = pool.get(b.handle);
    if (ad == null || ad.format != format) return null;
    if (!b.state.compareAndSet(SLOT_CLAIMED, SLOT_SHOWING)) return null;
    pool.remove(b.handle);
    logD("[B5-SHOW] " + slotId + " takes #" + b.handle);
    return ad;
  }

  static void reportPaid(String slotId, PoolAd ad, AdValue value, ResponseInfo info) {
    DFPoolListener l = listener;
    if (l == null || value == null) return;
    try {
      String source = extractAdSource(info);
      l.onAdPaid(
          slotId,
          ad.handle,
          value.getValueMicros(),
          value.getCurrencyCode(),
          value.getPrecisionType(),
          source.isEmpty() ? ad.adSource : source);
    } catch (Throwable t) {
      Log.e(TAG, "onAdPaid callback failed", t);
    }
  }

  // =========================================================================
  // INTERNAL
  // =========================================================================

  private static void onLoaded(
      int format,
      int handle,
      String adUnitId,
      Object ad,
      ResponseInfo info,
      double floorEcpmUsd) {
    double value = resolveAdValue(floorEcpmUsd, info);
    String source = extractAdSource(info);
    pool.put(handle, new PoolAd(handle, format, adUnitId, ad, value, source));
    logD(
        String.format(
            Locale.US,
            "[B2-ADMOB-OK] +#%d unit=%s value=%.6f (ecpm=%.4f, floor=%.4f) source=%s size=%d",
            handle,
            adUnitId,
            value,
            value * 1000.0,
            floorEcpmUsd,
            source,
            pool.size()));
    DFPoolListener l = listener;
    if (l != null) {
      try {
        l.onAdLoaded(handle, value, source, responseIdOf(info));
      } catch (Throwable t) {
        Log.e(TAG, "onAdLoaded callback failed", t);
      }
    }
  }

  private static void onFailed(int handle, LoadAdError error) {
    logD("[B2-ADMOB-FAIL] #" + handle + " failed code=" + error.getCode() + " msg=" + error.getMessage());
    DFPoolListener l = listener;
    if (l != null) {
      try {
        l.onAdFailed(handle, error.getCode(), error.getMessage(), responseIdOf(error.getResponseInfo()));
      } catch (Throwable t) {
        Log.e(TAG, "onAdFailed callback failed", t);
      }
    }
  }

  /** Response id of an AdMob response (fill or no-fill) - C# reports it with RecordAuctionEvent. */
  private static String responseIdOf(ResponseInfo info) {
    try {
      return info == null ? null : info.getResponseId();
    } catch (Throwable t) {
      return null;
    }
  }

  /**
   * Same extras as the meta AdMob layer: auction_context=parallel always, ua_channel /
   * days_since_install when C# knows them, rusd (eCPM in cents) only when a floor is set.
   */
  private static AdRequest buildRequest(
      double floorEcpmUsd, String uaChannel, String daysSinceInstall) {
    AdRequest.Builder builder = new AdRequest.Builder();
    Bundle extras = new Bundle();
    extras.putString(KEY_AUCTION_CONTEXT, AUCTION_CONTEXT_PARALLEL);
    if (uaChannel != null && !uaChannel.isEmpty()) extras.putString(KEY_UA_CHANNEL, uaChannel);
    if (daysSinceInstall != null && !daysSinceInstall.isEmpty()) {
      extras.putString(KEY_DAYS_SINCE_INSTALL, daysSinceInstall);
    }
    if (floorEcpmUsd > 0) {
      // rusd is eCPM in CENTS: $12.34 eCPM -> "1234"
      String cents =
          BigDecimal.valueOf(floorEcpmUsd * 100.0)
              .setScale(2, RoundingMode.HALF_UP)
              .stripTrailingZeros()
              .toPlainString();
      extras.putString(KEY_RUSD, cents);
    }
    builder.addNetworkExtrasBundle(AdMobAdapter.class, extras);
    return builder.build();
  }

  /**
   * USD value of ONE impression = max(floor / 1000, AdMob value in response extras). Reads both
   * "ad_value_string" (string) and "ad_value" (number); non-USD currency -> trust the floor only.
   */
  private static double resolveAdValue(double floorEcpmUsd, ResponseInfo info) {
    double floorValue = floorEcpmUsd > 0 ? floorEcpmUsd / 1000.0 : 0.0;
    if (info == null) return floorValue;
    try {
      Bundle extras = info.getResponseExtras();
      if (extras == null) return floorValue;

      Object currency = extras.get(KEY_AD_VALUE_CURRENCY);
      if (currency != null
          && !String.valueOf(currency).isEmpty()
          && !"USD".equalsIgnoreCase(String.valueOf(currency))) {
        return floorValue;
      }

      Object raw = extras.get(KEY_AD_VALUE_STRING);
      if (raw == null) raw = extras.get(KEY_AD_VALUE);
      if (raw == null) return floorValue;

      double parsed = (raw instanceof Number)
          ? ((Number) raw).doubleValue()
          : Double.parseDouble(String.valueOf(raw).trim());
      if (adValueInMicros) parsed /= 1_000_000.0;
      return parsed > floorValue ? parsed : floorValue;
    } catch (Throwable t) {
      return floorValue;
    }
  }

  static String extractAdSource(ResponseInfo info) {
    if (info == null) return "";
    try {
      AdapterResponseInfo loaded = info.getLoadedAdapterResponseInfo();
      if (loaded != null && loaded.getAdSourceName() != null) return loaded.getAdSourceName();
      String cls = info.getMediationAdapterClassName();
      return cls != null ? cls : "";
    } catch (Throwable t) {
      return "";
    }
  }

  static void runOnMain(Runnable r) {
    mainHandler.post(r);
  }
}
