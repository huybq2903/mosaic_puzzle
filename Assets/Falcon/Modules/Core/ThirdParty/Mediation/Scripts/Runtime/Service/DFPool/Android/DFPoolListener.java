package com.falcon.dfpool;

/**
 * Callbacks from native to Unity (implemented by an AndroidJavaProxy in DFPoolNative.cs).
 * All callbacks fire on the Android main thread - C# marshals them to the Unity main thread.
 */
public interface DFPoolListener {
  /**
   * An AdMob ad loaded and entered the pool. valueUsd = USD value of ONE impression. responseId = AdMob
   * response id (null if unknown), used by C# for Google multi-platform mediation events.
   */
  void onAdLoaded(int handle, double valueUsd, String adSource, String responseId);

  /** An AdMob request failed (nothing entered the pool). responseId may be null. */
  void onAdFailed(int handle, int errorCode, String errorMessage, String responseId);

  /** The AdMob ad shown through a MAX slot reported its real revenue (OnPaidEventListener). */
  void onAdPaid(
      String slotId, int handle, long valueMicros, String currency, int precision, String adSource);
}
