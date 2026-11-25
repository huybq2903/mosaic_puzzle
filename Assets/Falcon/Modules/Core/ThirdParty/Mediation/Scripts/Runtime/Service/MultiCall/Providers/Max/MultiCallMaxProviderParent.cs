/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2025-10-28
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using Falcon.Helpers.Devkit;
using Falcon.Modules.Core.BigData;
using Falcon.Modules.Core.Utils.Time.Runtime;
using UnityEngine;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime
{
    /// <summary>
    /// Module multi call adUnitId for max sdk
    /// </summary>
    public abstract class MultiCallMaxProviderParent<T> : MultiCallProvider<T>
        where T : MultiCallMaxProviderParent<T>, new()
    {
        protected override int IndexProvider { get; }
        protected override MediationType MediationType => MediationType.Max;

        /// <summary>Giá trị <c>adMediation</c> gửi lên big data — cùng chuỗi với FalconMaxService._MAX.</summary>
        private const string _MAX = "Max";

        /// <summary>
        /// <c>multiplier</c> trong config là <c>float</c>, còn <c>tier</c> là <c>double</c>. Gán
        /// thẳng thì 1.2f giãn thành 1.2000000476837158 — đúng cái hợp đồng cấm ("float box lên
        /// double là 1.2000000476…, tier tự tách đôi trên wire"), và số đó JOIN ngược về config
        /// không khớp. Đi qua decimal lấy đúng 7 chữ số có nghĩa đã ghi trong config nên 1.2f in
        /// lại đúng "1.2".
        /// <br/>Bậc &lt;= 0 (unit default không đặt sàn) -> null: vắng = KHÔNG đặt sàn, khác "đặt sàn 0"
        /// (Request-Mediation-AdEcpmAndParams.md V2).
        /// </summary>
        private static double? ToTier(float multiplier) => multiplier > 0f ? (double)(decimal)multiplier : null;

        /// <summary>
        /// Chụp bậc ĐANG DÙNG cho lần load này — gọi ngay trước <c>MaxSdk.Load*</c>. Dòng load (cả success
        /// lẫn fail) đọc lại <see cref="MultiCall.AdTypeMultiCall.requestTier"/> thay vì <c>multiplier</c>
        /// sống: thang hạ bậc của provider chạy TRƯỚC khi log fail nên đọc field sống là ra bậc của lần SAU
        /// (Request-Mediation-AdEcpmAndParams.md V6).
        /// </summary>
        protected static void SnapshotRequestTier(Dictionary<string, MultiCall.AdTypeMultiCall> dictionary,
            string adUnitId)
        {
            if (adUnitId == null || !dictionary.TryGetValue(adUnitId, out var a)) return;
            a.requestTier = a.multiplier;
            //vai sàn của opportunity personalization (rule_error / rule_timeout) — chỉ interstitial, chụp LÚC GỬI
            a.requestFloorType = dictionary == MultiCall.Instance.dictionaryInterstitial
                ? InterstitialPersonalization.Current.DefaultLoadFloorType
                : null;
        }

        protected sealed override void RegisterProvider()
        {
            MultiCall.Instance.Register(Instance, MediationType);
        }

#if MAX_ENABLE
        protected static double LoadedRevenue(MaxSdkBase.AdInfo adInfo)
        {
#if UNITY_EDITOR
            return adInfo.Revenue < 0d ? 0d : adInfo.Revenue;
#else
            return adInfo.Revenue;
#endif
        }
#endif

        #region rewarded

#if MAX_ENABLE
        protected override bool IsRewardedVideoReady(string adUnitId)
        {
            return MaxSdk.IsRewardedAdReady(adUnitId);
        }

        protected override void InternalShowRewarded(string adUnitId, string where)
        {
            var a = MultiCall.Instance.dictionaryRewarded[adUnitId];
            MediationManager.Instance.DebugLog("Internal Show Rewarded : " + adUnitId + " x " +
                                               a.multiplier);
            closedRewarded = false;
            receivedRewarded = false;
            FalconBigDataController.Ad.OnShown(new AdShowParam
                { type = AdType.Reward, adWhere = where, adMediation = _MAX, adUnitId = adUnitId });
            MaxSdk.ShowRewardedAd(adUnitId, where);
        }

        protected override void RegisterRewardedEvents()
        {
            MaxSdkCallbacks.Rewarded.OnAdLoadedEvent += OnRewardedAdLoadedEvent;
            MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent += OnRewardedAdLoadFailedEvent;
            //đăng ký VÔ ĐIỀU KIỆN: đây là mốc tiêu ad (MarkAdConsumed), không còn là event chỉ để log.
            MaxSdkCallbacks.Rewarded.OnAdDisplayedEvent += OnRewardedAdDisplayedEvent;

            MaxSdkCallbacks.Rewarded.OnAdRevenuePaidEvent += OnRewardedAdRevenuePaidEvent;
            MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent += OnRewardedAdDisplayFailedEvent;
            MaxSdkCallbacks.Rewarded.OnAdHiddenEvent += OnRewardedAdHiddenEvent;
            MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent += OnRewardedAdReceivedRewardEvent;
        }

        private void OnRewardedAdRevenuePaidEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool) tự lo
            if (MultiCall.Instance.dictionaryRewarded.ContainsKey(adUnitId))
            {
                var a = MultiCall.Instance.dictionaryRewarded[adUnitId];
                MediationManager.Instance.DebugLog("On Rewarded Ad Revenue Paid Event: " + adUnitId + " x " +
                                                   a.multiplier);
            }

            MediationManager.Instance.DebugLog("On Rewarded Ad Revenue Paid Revenue : " + adInfo.Revenue);
            ResetMultiplier(adUnitId);
            OnAdRevenuePaidEvent(AdType.Reward, adInfo);
        }

        private void OnRewardedAdDisplayFailedEvent(
            string adUnitId, MaxSdkBase.ErrorInfo errorInfo, MaxSdkBase.AdInfo adInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool) tự lo
            //show hỏng thì MAX huỷ ad -> giá cũ trong cache là giá ma, phải xoá ngay
            MarkAdConsumed(MultiCall.Instance.dictionaryRewarded, adUnitId);
            if (MultiCall.Instance.dictionaryRewarded.ContainsKey(adUnitId))
            {
                var a = MultiCall.Instance.dictionaryRewarded[adUnitId];
                MultiCall.Instance.DelayCall(1, adUnitId, LoadRewardedAd);
                MediationManager.Instance.DebugLog("On Rewarded Ad Display Failed Event: " + adUnitId + " x " +
                                                   a.multiplier);
            }
            else
            {
                MultiCall.Instance.DelayCall(1, adUnitId, LoadRewardedAd);
            }

            onRewardedFailed?.Invoke();
            MediationManager.Instance.DebugLog("On Rewarded Ad Display Failed Revenue : " + adInfo.Revenue);
        }

        private void OnRewardedAdDisplayedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool) tự lo
            //ad ĐÃ lên hình -> giờ mới thật sự là "đã tiêu"
            MarkAdConsumed(MultiCall.Instance.dictionaryRewarded, adUnitId);
            if (MultiCall.Instance.dictionaryRewarded.ContainsKey(adUnitId))
            {
                var a = MultiCall.Instance.dictionaryRewarded[adUnitId];
                MediationManager.Instance.DebugLog("On Rewarded Ad Displayed Event: " + adUnitId + " x " +
                                                   a.multiplier);
            }

            MediationManager.Instance.DebugLog("On Rewarded Ad Displayed Revenue : " + adInfo.Revenue);
        }

        private void OnRewardedAdHiddenEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool) tự lo
            //chốt chặn cuối: ad đóng rồi thì chắc chắn không còn trong kho, kể cả khi mốc displayed bị mất
            MarkAdConsumed(MultiCall.Instance.dictionaryRewarded, adUnitId);
            if (MultiCall.Instance.dictionaryRewarded.ContainsKey(adUnitId))
            {
                if (ConsumeShowSession())
                {
                    var time = (int)(TimeUtils.UTCNow - MultiCall.Instance.dateTimeTimeShow).TotalSeconds;
                    new CSAdFinish(idAd, AdType.Reward.ToString(), Placement(placementId), adInfo.NetworkName,
                        MultiCall.MAX, adUnitId, adInfo.Revenue, time).Send();
                }
                else
                {
                    MediationManager.Instance.DebugError(
                        "MAX multicall > bỏ qua CSAdFinish rewarded lặp của lượt show đã kết thúc : " + adUnitId);
                }

                var a = MultiCall.Instance.dictionaryRewarded[adUnitId];
                MediationManager.Instance.DebugLog("On Rewarded Ad Hidden Event: " + adUnitId + " x " +
                                                   a.multiplier);
                //update multiplier
                UpdateMultiplier(adUnitId);
            }
            else
            {
                MultiCall.Instance.DelayCall(1, adUnitId, LoadRewardedAd);
            }
            //where truyền TƯỜNG MINH, cùng nguồn với CSAdFinish ở trên. Bỏ trống thì OnClosed đọc
            //ad-view cache của BigData, mà cache đó chỉ có MỘT slot cho mỗi AdType và bị xoá trắng
            //ở mọi OnRequested — multicall chạy N ad unit mỗi format, chỉ cần một unit load lại
            //trong lúc ad đang chiếu là log close ra rỗng → DWH default "Unknown".
            //placementId thì chỉ gán ở hàm show (MultiCallProvider) nên luôn đúng lượt đang đóng.
            //adCompleted lấy từ receivedRewarded — hợp đồng: "Rewarded: giữ bool từ
            //OnAdReceivedRewardEvent rồi truyền vào đây (Hidden đến sau Reward)". Trước đây bỏ
            //trống nên DWH không phân biệt được lượt xem hết với lượt tắt giữa chừng.
            FalconBigDataController.Ad.OnClosed(
                AdType.Reward, adCompleted: receivedRewarded, adWhere: Placement(placementId));
            //view kết thúc -> mở van, đợt refill ngay dưới mới được mở view mới.
            AdViewRequestGate.Close(AdType.Reward);

            closedRewarded = true;
            onRewardedHidden?.Invoke();
            CheckReceivedRewarded();
            MediationManager.Instance.DebugLog("On Rewarded Ad Hidden Revenue : " + adInfo.Revenue);
        }

        protected virtual void UpdateMultiplier(string adUnitId)
        {
        }

        private void OnRewardedAdReceivedRewardEvent(
            string adUnitId, MaxSdkBase.Reward reward, MaxSdkBase.AdInfo adInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool) tự lo
            receivedRewarded = true;
            onRewardedComplete?.Invoke();
            CheckReceivedRewarded();
        }

        private void OnRewardedAdLoadFailedEvent(string adUnitId, MaxSdkBase.ErrorInfo errorInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool) tự lo
            if (MultiCall.Instance.dictionaryRewarded.ContainsKey(adUnitId))
            {
                var a = MultiCall.Instance.dictionaryRewarded[adUnitId];
                InternalLoadRewardedLoadFailed(adUnitId);
                a.isWaiting = false;
                MediationManager.Instance.DebugLog("On Rewarded Ad Load Failed Event: " + adUnitId + " x " +
                                                   a.multiplier);

                var b = LogInstance.Instance.GetAdRequestLogItemByAdUnitId(adUnitId);
                if (b != null)
                {
                    if (!b.errorDetail.ContainsKey(errorInfo.Code.ToString()))
                    {
                        b.errorDetail.Add(errorInfo.Code.ToString(), 0);
                    }

                    b.errorDetail[errorInfo.Code.ToString()]++;
                }
                else
                {
#if ENABLE_DEBUG_LOG
                    Debug.LogError("don't exists rewarded load failed id : " + adUnitId);
#endif
                }
                FalconBigDataController.Ad.OnLoadResult(new AdLoadResultParam()
                {
                    adType = AdType.Reward,
                    adUnitId = adUnitId,
                    success = false,
                    adMediation = _MAX,
                    adAlgorithm = AdViewRequestGate.MultiCallAlgorithm(GetIndex()),
                    networkName = null,
                    tier = ToTier(a.requestTier), //BẬC trong config — xem OnRewardedAdLoadedEvent
                    ecpmFloor = a.currentFloor, //giá sàn eCPM ĐÃ GỬI kèm request (jC7Fp), 0 = unit không set sàn
                    errorMess = errorInfo.Code.ToString(),
                    loadingMs = errorInfo.LatencyMillis
                });
                ReportGiveUpIfExhausted(a, AdType.Reward, errorInfo.Code.ToString(),
                    MultiCall.Instance.IsRewardedVideoReady());
            }
        }

        /// <summary>
        /// Nhãn <c>LOAD_ERROR</c> cho CẢ ĐỢT: chỉ khi unit này đã đi hết thang retry trong config
        /// (<c>listTimeRetry</c>) mà kho của format vẫn RỖNG — tức đợt load đang thua thật, không
        /// phải một floor lẻ no-fill trong khi floor khác đã fill xong.
        /// <see cref="AdViewRequestGate.ReportLoadGaveUp"/> tự chặn từ lần thứ hai trở đi nên
        /// không spam kênh nhãn dù N unit cùng cạn.
        /// <br/>
        /// ⚠ Ngưỡng do mediation TỰ ĐẶT (MAX/multicall retry vô hạn, không có mốc bỏ cuộc thật) —
        /// cần data team chốt trước khi tin số fill rate.
        /// </summary>
        private static void ReportGiveUpIfExhausted(
            MultiCall.AdTypeMultiCall entry, AdType type, string errorCode, bool anyAdReady)
        {
            if (anyAdReady) return;
            //thang rỗng thì retry lặp vô hạn với delay 0 — không có khái niệm "cạn", bỏ qua.
            if (entry.listTimeRetry == null || entry.listTimeRetry.Count == 0) return;
            if (entry.countLoadAttempt < entry.listTimeRetry.Count) return;
            AdViewRequestGate.ReportLoadGaveUp(type, errorCode);
        }
#endif
        protected virtual void InternalLoadRewardedLoadFailed(string adUnitId)
        {
        }
#if MAX_ENABLE

        protected virtual void OnRewardedAdLoadedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool) tự lo
            if (MultiCall.Instance.dictionaryRewarded.ContainsKey(adUnitId))
            {
                var a = MultiCall.Instance.dictionaryRewarded[adUnitId];
                a.failCountBeforeLoad = Math.Max(0, a.countLoadAttempt - 1);
                a.countLoadAttempt = 0;
                a.isWaiting = false;
                a.eCpm = LoadedRevenue(adInfo);
                a.loadedFloor = a.currentFloor;
                a.networkName = adInfo.NetworkName;
                MediationManager.Instance.DebugLog("On Rewarded Ad Loaded Event: " + adUnitId);
                if (a.multiplier != 0)
                {
                    MediationManager.Instance.DebugLog("floor : " + MultiCall.Instance.valueDefaultRewarded + " xxxx " +
                                                       a.multiplier + " === " +
                                                       a.multiplier * MultiCall.Instance.valueDefaultRewarded);
                }
                else
                {
                    MediationManager.Instance.DebugLog("multiplier : 0");
                }

                var b = LogInstance.Instance.GetAdRequestLogItemByAdUnitId(adUnitId);
                if (b != null)
                {
                    var c = new AdSuccessInfo
                    {
                        index = b.numRequest,
                        revenueUsd = adInfo.Revenue,
                        time = new DateTimeOffset(TimeUtils.UTCNow).ToUnixTimeMilliseconds(),
                        adMediation = "Max",
                        adNetwork = adInfo.NetworkName
                    };
                    b.adSuccessInfos.Add(c);
                }
                else
                {
#if ENABLE_DEBUG_LOG
                    Debug.LogError("don't exists adRequestLogItem rewarded id : " + adUnitId);
#endif
                }

                //tier = BẬC trong config (multiplier), KHÔNG nhân ra mức bid tuyệt đối.
                //Hợp đồng: "ĐỪNG tự nhân multiplier ra mức bid tuyệt đối (base eCPM động, số nhân
                //ra là số bịa)". Công thức cũ (multiplier × valueDefaultRewarded × MULTIPLIER) lấy
                //valueDefaultRewarded = revenue của lần load THÀNH CÔNG ĐẦU TIÊN của unit default
                //trong session, nên cùng multiplier 1.2 ra số khác nhau mỗi session/máy/ngày —
                //không group được, và trước lần thắng đầu tiên thì valueDefault = 0 nên mọi dòng
                //đầu session mang số 0. eCPM thật per bậc: JOIN adUnitId sang f_sdk_ads_data.
                //floor bỏ trống: MAX giữ giá sàn thật trên dashboard, client không biết số đó.
                FalconBigDataController.Ad.OnLoadResult(new AdLoadResultParam()
                {
                    adType = AdType.Reward,
                    adUnitId = adUnitId,
                    success = true,
                    adMediation = _MAX,
                    adAlgorithm = AdViewRequestGate.MultiCallAlgorithm(GetIndex()),
                    networkName = adInfo.NetworkName,
                    tier = ToTier(a.requestTier),
                    ecpmFloor = a.currentFloor,
                    estimatedEcpm = MultiCall.ToEcpm(adInfo.Revenue),
                    loadingMs = adInfo.LatencyMillis
                });
            }

            MediationManager.Instance.DebugLog("On Rewarded Ad Loaded Revenue : " + adInfo.Revenue);
        }
#endif

        #endregion

        #region interstitial

#if MAX_ENABLE
        protected override void InternalShowInterstitial(string adUnitId)
        {
            FalconBigDataController.Ad.OnShown(new AdShowParam
                { type = AdType.Interstitial, adWhere = placementId, adMediation = _MAX, adUnitId = adUnitId });
            MaxSdk.ShowInterstitial(adUnitId);
        }

        protected override bool IsInterstitialReady(string adUnitId)
        {
            return MaxSdk.IsInterstitialReady(adUnitId);
        }

        protected override void RegisterInterstitialEvents()
        {
            MaxSdkCallbacks.Interstitial.OnAdLoadedEvent += OnInterstitialLoadedEvent;
            MaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent += OnInterstitialLoadFailedEvent;
            MaxSdkCallbacks.Interstitial.OnAdDisplayedEvent += OnInterstitialAdDisplayedEvent;
            MaxSdkCallbacks.Interstitial.OnAdRevenuePaidEvent += OnInterstitialAdRevenuePaidEvent;
            MaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent += OnInterstitialAdDisplayFailedEvent;
            MaxSdkCallbacks.Interstitial.OnAdHiddenEvent += OnInterstitialHiddenEvent;
        }

        /// <summary>
        /// Ad unit của workflow Floor Personalization: FalconMaxService lo trọn show → hidden (nhánh unit
        /// đơn). Multicall bỏ qua hẳn, không thì một lượt chiếu đẻ hai bộ log close / đếm show hai lần và
        /// gọi nhầm callback của lượt show multicall trước đó.
        /// </summary>
        private static bool IsFloorPersonalizationUnit(string adUnitId) =>
            InterstitialPersonalization.Current.IsOwnedUnit(adUnitId);

        private void OnInterstitialAdRevenuePaidEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool) tự lo
            if (IsFloorPersonalizationUnit(adUnitId)) return;
            ResetMultiplier(adUnitId);
            OnAdRevenuePaidEvent(AdType.Interstitial, adInfo);
        }

        private void OnAdRevenuePaidEvent(AdType type, MaxSdkBase.AdInfo adInfo)
        {
            //log to adInfo
            double timeShow = (TimeUtils.UTCNow - MultiCall.Instance.dateTimeTimeShow).TotalSeconds;
            new CSAdInfo(idAd, Placement(placementId), type.ToString(), adInfo.NetworkName, "MAX", (int)timeShow,
                    adInfo.Revenue)
                .Send();
        }

        private void OnInterstitialAdDisplayFailedEvent(
            string adUnitId, MaxSdkBase.ErrorInfo errorInfo, MaxSdkBase.AdInfo adInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool) tự lo
            if (IsFloorPersonalizationUnit(adUnitId)) return;
            //show hỏng thì MAX huỷ ad -> giá cũ trong cache là giá ma, phải xoá ngay
            MarkAdConsumed(MultiCall.Instance.dictionaryInterstitial, adUnitId);
            if (MultiCall.Instance.dictionaryInterstitial.ContainsKey(adUnitId))
            {
                var a = MultiCall.Instance.dictionaryInterstitial[adUnitId];
                MultiCall.Instance.DelayCall(1, adUnitId, LoadInterstitialAd);
            }
            else
            {
                MultiCall.Instance.DelayCall(1, adUnitId, LoadInterstitialAd);
            }

            onInterstitialFailed?.Invoke();
        }

        private void OnInterstitialAdDisplayedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool) tự lo
            if (IsFloorPersonalizationUnit(adUnitId)) return;
            //ad ĐÃ lên hình -> giờ mới thật sự là "đã tiêu"
            MarkAdConsumed(MultiCall.Instance.dictionaryInterstitial, adUnitId);
            MediationManager.Instance.NumberShowInterstitial++;
        }

        private void OnInterstitialHiddenEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool) tự lo
            if (IsFloorPersonalizationUnit(adUnitId)) return;
            //chốt chặn cuối: ad đóng rồi thì chắc chắn không còn trong kho, kể cả khi mốc displayed bị mất
            MarkAdConsumed(MultiCall.Instance.dictionaryInterstitial, adUnitId);
            if (MultiCall.Instance.dictionaryInterstitial.ContainsKey(adUnitId))
            {
                if (ConsumeShowSession())
                {
                    var time = (int)(TimeUtils.UTCNow - MultiCall.Instance.dateTimeTimeShow).TotalSeconds;
                    new CSAdFinish(idAd, AdType.Interstitial.ToString(), Placement(placementId), adInfo.NetworkName,
                        MultiCall.MAX, adUnitId, adInfo.Revenue, time).Send();
                }
                else
                {
                    MediationManager.Instance.DebugError(
                        "MAX multicall > bỏ qua CSAdFinish interstitial lặp của lượt show đã kết thúc : " + adUnitId);
                }

                UpdateMultiplierInterstitial(adUnitId);
            }
            else
            {
                MultiCall.Instance.DelayCall(1, adUnitId, LoadInterstitialAd);
            }
            //where truyền TƯỜNG MINH — xem lý do ở OnRewardedAdHiddenEvent.
            //adCompleted để TRỐNG: inter không có callback đo "xem hết", đừng đoán.
            FalconBigDataController.Ad.OnClosed(AdType.Interstitial, adWhere: Placement(placementId));
            //view kết thúc -> mở van, đợt refill (DelayCall ở trên) mới được mở view mới.
            AdViewRequestGate.Close(AdType.Interstitial);
            onInterstitialClosed?.Invoke();
        }

        /// <summary>
        /// Interstitial vừa đóng (đã rời kho). Mặc định: nạp lại sau 1s như trước. Provider nào cần đổi
        /// hệ số / thứ tự nạp sau khi show thì override.
        /// </summary>
        protected virtual void UpdateMultiplierInterstitial(string adUnitId)
        {
            MultiCall.Instance.DelayCall(1, adUnitId, LoadInterstitialAd);
        }

        private void OnInterstitialLoadFailedEvent(string adUnitId, MaxSdkBase.ErrorInfo errorInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool) tự lo
            if (MultiCall.Instance.dictionaryInterstitial.ContainsKey(adUnitId))
            {
                InternalLoadInterstitialLoadFailed(adUnitId);
                var a = MultiCall.Instance.dictionaryInterstitial[adUnitId];
                a.isWaiting = false;
                var b = LogInstance.Instance.GetAdRequestLogItemByAdUnitId(adUnitId);
                if (b != null)
                {
                    if (!b.errorDetail.ContainsKey(errorInfo.Code.ToString()))
                    {
                        b.errorDetail.Add(errorInfo.Code.ToString(), 0);
                    }

                    b.errorDetail[errorInfo.Code.ToString()]++;
                }
                else
                {
#if ENABLE_DEBUG_LOG
                    Debug.LogError("don't exists inter load failed id : " + adUnitId);
#endif
                }
                FalconBigDataController.Ad.OnLoadResult(new AdLoadResultParam()
                {
                    adType = AdType.Interstitial,
                    adUnitId = adUnitId,
                    success = false,
                    adMediation = _MAX,
                    adAlgorithm = AdViewRequestGate.MultiCallAlgorithm(GetIndex()),
                    networkName = null,
                    tier = ToTier(a.requestTier), //BẬC trong config — xem OnRewardedAdLoadedEvent
                    ecpmFloor = a.currentFloor, //giá sàn eCPM ĐÃ GỬI kèm request (jC7Fp), 0 = unit không set sàn
                    ecpmFloorType = a.requestFloorType,
                    errorMess = errorInfo.Code.ToString(),
                    loadingMs = errorInfo.LatencyMillis
                });
                ReportGiveUpIfExhausted(a, AdType.Interstitial, errorInfo.Code.ToString(),
                    MultiCall.Instance.IsInterstitialReady());
            }
        }
#endif
        protected virtual void InternalLoadInterstitialLoadFailed(string adUnitId)
        {
        }
#if MAX_ENABLE
        protected virtual void OnInterstitialLoadedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool) tự lo
            if (MultiCall.Instance.dictionaryInterstitial.ContainsKey(adUnitId))
            {
                var a = MultiCall.Instance.dictionaryInterstitial[adUnitId];
                a.failCountBeforeLoad = Math.Max(0, a.countLoadAttempt - 1);
                a.countLoadAttempt = 0;
                a.isWaiting = false;
                a.eCpm = LoadedRevenue(adInfo);
                a.loadedFloor = a.currentFloor;
                a.networkName = adInfo.NetworkName;

                var b = LogInstance.Instance.GetAdRequestLogItemByAdUnitId(adUnitId);
                if (b != null)
                {
                    var c = new AdSuccessInfo
                    {
                        index = b.numRequest,
                        revenueUsd = adInfo.Revenue,
                        time = new DateTimeOffset(TimeUtils.UTCNow).ToUnixTimeMilliseconds(),
                        adMediation = "Max",
                        adNetwork = adInfo.NetworkName
                    };
                    b.adSuccessInfos.Add(c);
                }
                else
                {
#if ENABLE_DEBUG_LOG
                    Debug.LogError("don't exists interstitial load failed id : " + adUnitId);
#endif
                }

                //tier = BẬC trong config — xem lý do đầy đủ ở OnRewardedAdLoadedEvent.
                FalconBigDataController.Ad.OnLoadResult(new AdLoadResultParam()
                {
                    adType = AdType.Interstitial,
                    adUnitId = adUnitId,
                    success = true,
                    adMediation = _MAX,
                    adAlgorithm = AdViewRequestGate.MultiCallAlgorithm(GetIndex()),
                    networkName = adInfo.NetworkName,
                    tier = ToTier(a.requestTier),
                    ecpmFloor = a.currentFloor,
                    ecpmFloorType = a.requestFloorType,
                    estimatedEcpm = MultiCall.ToEcpm(adInfo.Revenue),
                    loadingMs = adInfo.LatencyMillis
                });
                
            }
        }
#endif

        #endregion
    }
}