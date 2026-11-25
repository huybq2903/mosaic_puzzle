/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2025-06-06
 */

using System;
using System.Collections;
using Falcon.Helpers.Devkit;
using Falcon.Helpers.EventBus;
using Falcon.Modules.Core.BigData;
#if GOOGLE_MOBILE_ADS_ENABLE
using GoogleMobileAds.Common;
using Falcon.Modules.Core.SaveLoad.Runtime;
using GoogleMobileAds.Api;
using GoogleMobileAds.Ump.Api;
#endif
using UnityEngine;
#if UNITY_IOS
using System;
using Falcon.Modules.Core.RemoteConfig;
using System.Collections;
using Falcon.Helpers.EventBus;
#if UNITY_ADS_IOS_SUPPORT_ENABLE
using Unity.Advertisement.IosSupport;
#endif
#endif

namespace Falcon.Modules.Core.ThirdParty.Ump.Runtime
{
    public class FalconUMP : AutoSingleton<FalconUMP>
    {
        public const string FALCON_UMP_IS_IN_EUROPE = "falcon_ump_is_in_europe";
        public const string FALCON_UMP_HAS_CONSENT = "falcon_ump_has_consent";
        public const string FALCON_UMP_COMPLETE = "falcon.modules.core.thirdparty.ump.complete";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            _ = Instance;
        }

        protected override void Awake()
        {
            base.Awake();
            StartCoroutine(InitUmp());
        }

        private IEnumerator InitUmp()
        {
#if GOOGLE_MOBILE_ADS_ENABLE
            // Set MỘT LẦN, trước mọi Update, và KHÔNG đổi nữa.
            MobileAds.RaiseAdEventsOnUnityMainThread = true;
            MobileAdsEventExecutor.Initialize();
            yield return null;

            bool handled = false;            // chống callback nổ 2 lần / nổ muộn
            bool fired = false;
            FormError err = null;

            ConsentInformation.Update(new ConsentRequestParameters(), e =>
            {
                if (handled) return;         // callback zombie tới muộn -> bỏ qua
                handled = true;
                fired = true;
                err = e;
            });

            // Timeout chỉ để KHÔNG chặn UX vô hạn — KHÔNG gọi Update lần nữa.
            float timeOut = 5f;
            while (!fired && (timeOut -= Time.deltaTime) > 0f) yield return null;

            if (!fired)
            {
                handled = true;              // "đóng" callback: nếu native nổ muộn cũng bị lambda bỏ qua
#if ENABLE_DEBUG_LOG
                Debug.LogError("[UMP] Update timeout.");
#endif
                OnShowPopupATT();
                yield break;
            }

            if (err != null)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError($"FalconUMP > Update error {err.ErrorCode}: {err.Message}");
#endif
                OnShowPopupATT();
                yield break;
            }

            //Required trước LoadAndShow = form CMP sẽ hiện -> báo lần hiện popup (BigData đợt 3 B2 / B14b).
            //⚠ suy "Required ⇒ form hiện" chưa chạy thử: kiểm trên máy EU / debug geography của UMP.
            if (ConsentInformation.ConsentStatus == ConsentStatus.Required) ReportConsentFormPrompt();

            ConsentForm.LoadAndShowConsentFormIfRequired(formError =>
            {
                OnShowPopupATT();
                if (formError != null)
                {
#if ENABLE_DEBUG_LOG
                    Debug.LogError($"FalconUMP > LoadAndShow error {formError.ErrorCode}: {formError.Message}");
#endif
                    return;
                }
                var consent = ConsentInformation.ConsentStatus;
                SaveLoadHandler.Save(FALCON_UMP_IS_IN_EUROPE, consent == ConsentStatus.Required);
                SaveLoadHandler.Save(FALCON_UMP_HAS_CONSENT, consent == ConsentStatus.Obtained);
                if (consent is ConsentStatus.Required or ConsentStatus.Obtained) ReportAdsConsent();
            });
#endif
            yield break;
        }

        private void OnShowPopupATT()
        {
#if UNITY_IOS && UNITY_ADS_IOS_SUPPORT_ENABLE
            ReportAttStatus();
#endif
#if UNITY_IOS
            string cmpString = PlayerPrefs.GetString("IABTCF_AddtlConsent");
            bool showAtt = false;
            if (!string.IsNullOrEmpty(cmpString))
            {
                string[] cmpSlices = cmpString.Split("~");
                if (cmpSlices.Length >= 2)
                {
                    string version = cmpSlices[0];
                    if (version == "2" || version == "1")
                    {
                        if (cmpSlices[1].Contains("2878"))
                        {
                            showAtt = true;
                        }
                    }
                }
            }
            else
            {
                showAtt = true;
            }
#if UNITY_ADS_IOS_SUPPORT_ENABLE
            if (showAtt)
            {
                //show popup ATT
                if (ATTrackingStatusBinding.GetAuthorizationTrackingStatus() ==
                    ATTrackingStatusBinding.AuthorizationTrackingStatus.NOT_DETERMINED)
                {
                    ReportAttPrompt();
                    ATTrackingStatusBinding.RequestAuthorizationTracking();
                }
            }
            else
            {
                string keyCheckAttSecondOpen = "check_att";
                if (PlayerPrefs.HasKey(keyCheckAttSecondOpen))
                {
                    //show popup ATT
                    var versionFromRemote = FConfigController.Instance.Config<UmpConfig>().versionShowATTInSecondOpen;
                    int compareVersion = -1;
                    if (string.IsNullOrEmpty(versionFromRemote))
                    {
                        compareVersion = -1;
                    }
                    else
                    {
                        compareVersion = CompareVersion(Application.version, versionFromRemote);
                    }

                    if (compareVersion >= 0)
                    {
                        if (ATTrackingStatusBinding.GetAuthorizationTrackingStatus() ==
                            ATTrackingStatusBinding.AuthorizationTrackingStatus.NOT_DETERMINED)
                        {
                            ReportAttPrompt();
                            ATTrackingStatusBinding.RequestAuthorizationTracking();
                            showAtt = true;
                        }
                    }
                }
                else
                {
                    PlayerPrefs.SetInt(keyCheckAttSecondOpen, 1);
                }
            }

            StartCoroutine(WaitUntilDetermined(showAtt));
#else
            Debug.LogError("Chưa cài package Unity.Advertisement.IosSupport.");
#endif
#elif UNITY_ANDROID
            StartCoroutine(WaitEof());

            IEnumerator WaitEof()
            {
                yield return new WaitForEndOfFrame();
#if ENABLE_DEBUG_LOG
                Debug.Log("init UMP complete");
#endif
                GameEvent<bool>.Emit(FALCON_UMP_COMPLETE);
            }

#endif
        }

#if UNITY_IOS
        private int CompareVersion(string v1, string v2)
        {
            try
            {
                var parts1 = v1.Split('.');
                var parts2 = v2.Split('.');

                int maxLength = Math.Max(parts1.Length, parts2.Length);

                for (int i = 0; i < maxLength; i++)
                {
                    int p1 = i < parts1.Length ? int.Parse(parts1[i]) : 0;
                    int p2 = i < parts2.Length ? int.Parse(parts2[i]) : 0;

                    if (p1 > p2) return 1;
                    if (p1 < p2) return -1;
                }

                return 0;
            }
            catch
            {
                return -1;
            }
        }

#endif

#if UNITY_IOS && UNITY_ADS_IOS_SUPPORT_ENABLE
        IEnumerator WaitUntilDetermined(bool showAtt)
        {
#if UNITY_EDITOR
            yield return null;
#else
            if (showAtt)
            {
                yield return new WaitUntil(() =>
                    ATTrackingStatusBinding.GetAuthorizationTrackingStatus() !=
                    ATTrackingStatusBinding.AuthorizationTrackingStatus.NOT_DETERMINED);
            }

#endif
            GameEvent<bool>.Emit(FALCON_UMP_COMPLETE);
            ReportAttStatus();
#if ENABLE_DEBUG_LOG
            Debug.Log("init UMP complete");
#endif
        }

        /// <summary>
        /// Popup ATT của OS sắp hiện (chỉ gọi khi trạng thái còn NOT_DETERMINED) — <c>f_sdk_permission_prompt</c>.
        /// Bắn TRƯỚC <see cref="ReportAttStatus"/> của lần đổi trạng thái: cùng hàng chờ BigData, khởi động trước.
        /// </summary>
        private void ReportAttPrompt()
        {
            ReportWhenBigDataReady(() =>
                FalconBigDataController.Consent.OnPrompt(PromptPermission.Att, PromptKind.System,
                    PromptTrigger.FirstOpen));
        }

        private void ReportAttStatus()
        {
            ReportWhenBigDataReady(() =>
                FalconBigDataController.App.ReportAttStatus(
                    ToFalconAtt(ATTrackingStatusBinding.GetAuthorizationTrackingStatus())));
        }

        private static AttStatus ToFalconAtt(ATTrackingStatusBinding.AuthorizationTrackingStatus status)
        {
            return status switch
            {
                ATTrackingStatusBinding.AuthorizationTrackingStatus.AUTHORIZED => AttStatus.Authorized,
                ATTrackingStatusBinding.AuthorizationTrackingStatus.DENIED => AttStatus.Denied,
                ATTrackingStatusBinding.AuthorizationTrackingStatus.RESTRICTED => AttStatus.Restricted,
                _ => AttStatus.NotDetermined
            };
        }

#endif

#if GOOGLE_MOBILE_ADS_ENABLE
        private const string _IABTCF_PURPOSE_CONSENTS = "IABTCF_PurposeConsents";
        private const int _PURPOSE_STORE_ACCESS_INDEX = 0;
        private const int _PURPOSE_PERSONALISED_ADS_INDEX = 3;

        /// <summary>
        /// Form CMP sắp hiện — popup xin quyền <c>ads_consent</c> + trạm tải <c>consent_shown</c>. BigData sẵn sàng thì
        /// gọi ngay (giờ trạm tải đúng lúc form hiện); chưa thì chờ như các log quyền khác.
        /// </summary>
        private void ReportConsentFormPrompt()
        {
            ReportWhenBigDataReady(() =>
            {
                FalconBigDataController.Consent.OnPrompt(PromptPermission.AdsConsent, PromptKind.System,
                    PromptTrigger.FirstOpen);
                FalconBigDataController.Ftue.OnLoadStep(LoadKind.App, LoadStep.ConsentShown);
            });
        }

        private void ReportAdsConsent()
        {
            ReportWhenBigDataReady(() =>
            {
                var purposeConsents = ReadIabTcfString(_IABTCF_PURPOSE_CONSENTS);
                if (string.IsNullOrEmpty(purposeConsents) ||
                    purposeConsents.Length <= _PURPOSE_PERSONALISED_ADS_INDEX)
                    return;
                var granted = purposeConsents[_PURPOSE_STORE_ACCESS_INDEX] == '1' &&
                              purposeConsents[_PURPOSE_PERSONALISED_ADS_INDEX] == '1';
                FalconBigDataController.App.ReportAdsConsent(granted);
            });
        }

        private static string ReadIabTcfString(string key)
        {
#if UNITY_EDITOR
            return null;
#elif UNITY_ANDROID
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var preferenceManager = new AndroidJavaClass("android.preference.PreferenceManager");
                using var prefs =
                    preferenceManager.CallStatic<AndroidJavaObject>("getDefaultSharedPreferences", activity);
                return prefs.Call<string>("getString", key, null);
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogException(e);
#endif
                return null;
            }
#elif UNITY_IOS
            return PlayerPrefs.GetString(key, null);
#else
            return null;
#endif
        }
#endif

        private const float _BIG_DATA_WAIT_SECONDS = 60f;

        private void ReportWhenBigDataReady(Action report)
        {
            StartCoroutine(WaitBigDataThen(report));
        }

        private static IEnumerator WaitBigDataThen(Action report)
        {
            var waitUntil = Time.realtimeSinceStartup + _BIG_DATA_WAIT_SECONDS;
            while (!MySingletonService.TryGetInstance<FalconBigDataController>(out _))
            {
                if (Time.realtimeSinceStartup > waitUntil) yield break;
                yield return null;
            }

            try
            {
                report();
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogException(e);
#endif
            }
        }
    }
}