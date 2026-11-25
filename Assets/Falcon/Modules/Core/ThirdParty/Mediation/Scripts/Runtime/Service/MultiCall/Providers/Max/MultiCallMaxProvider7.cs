/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2026-07-28
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using Falcon.Helpers.Devkit;
using Falcon.Modules.Core.BigData;
using Falcon.Modules.Core.RemoteConfigCms;
using Falcon.Modules.Core.Utils.Time.Runtime;
using UnityEngine;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime
{
    /// <summary>
    /// Module multi call adUnitId for max sdk
    /// cập nhật giá trị valueDefault khi load xong qc
    /// hệ số mỗi bậc lấy đúng từ remote config; fail liên tục thì giảm 0.1, chặn dưới ở hệ số config bậc trước
    /// </summary>
    public class MultiCallMaxProvider7 : MultiCallMaxProviderParent<MultiCallMaxProvider7>
    {
#if MAX_ENABLE
        protected override int IndexProvider => 7;

        private readonly List<float> _listMultiplierRewardedDefault = new();
        private readonly List<float> _listMultiplierInterstitialDefault = new();
        private int _defaultCountRewarded;
        private int _defaultCountInterstitial;
        private int _periodDecreaseFloor;
        private const int _GROUP_COUNT = 3; // tối đa 3 bậc floor bận (đang load / chờ retry) cùng lúc

        private FloorQueue _rewardedFloors;
        private FloorQueue _interstitialFloors;

        private FloorQueue RewardedFloors =>
            _rewardedFloors ??= new FloorQueue("Reward", () => MultiCall.Instance.dictionaryRewarded,
                LoadRewardedAd, a => Describe(a, _defaultCountRewarded));

        private FloorQueue InterstitialFloors =>
            _interstitialFloors ??= new FloorQueue("Inter", () => MultiCall.Instance.dictionaryInterstitial,
                LoadInterstitialAd, a => Describe(a, _defaultCountInterstitial));

        private const string _LOG_TAG = "[MAX algorithm 7] ";

        private static void Log(string message) => MediationManager.Instance.DebugLog(_LOG_TAG + message);

        /// <summary>"DEFAULT #1 adUnit=..." hoặc "bậc 2 x1.5 adUnit=..." (bậc floor đánh số từ 1).</summary>
        private static string Describe(MultiCall.AdTypeMultiCall a, int defaultCount)
        {
            var level = a.multiplier == 0
                ? "DEFAULT #" + (a.index + 1)
                : "bậc " + (a.index - defaultCount + 1) + " x" +
                  a.multiplier.ToString("0.##", CultureInfo.InvariantCulture);
            return level + " adUnit=" + a.adUnitId;
        }

        //floor = mức sàn eCPM (USD) gửi MAX; 0 = id default không set sàn
        private static string Floor(double floorEcpm)
            => floorEcpm > 0d ? "$" + floorEcpm.ToString("F4", CultureInfo.InvariantCulture) + " eCPM" : "không set";

        #region rewarded

        //rewarded vừa đóng (đã rời kho)
        protected override void UpdateMultiplier(string adUnitId)
        {
            if (!MultiCall.Instance.dictionaryRewarded.TryGetValue(adUnitId, out var a)) return;

            var tier = a.index - _defaultCountRewarded;
            if (tier >= 0 && tier < _listMultiplierRewardedDefault.Count)
            {
                //load lại sau show -> trả về đúng multiplier gốc của remote config
                a.multiplier = _listMultiplierRewardedDefault[tier];
                //KHÔNG load lại bậc này ngay: chỉ load khi còn chỗ (xem FloorQueue)
                RewardedFloors.Refill();
                return;
            }

            MultiCall.Instance.DelayCall(1, adUnitId, LoadRewardedAd);
        }

        //interstitial vừa đóng (đã rời kho) — xem UpdateMultiplier
        protected override void UpdateMultiplierInterstitial(string adUnitId)
        {
            if (!MultiCall.Instance.dictionaryInterstitial.TryGetValue(adUnitId, out var a)) return;

            var tier = a.index - _defaultCountInterstitial;
            if (tier >= 0 && tier < _listMultiplierInterstitialDefault.Count)
            {
                a.multiplier = _listMultiplierInterstitialDefault[tier];
                InterstitialFloors.Refill();
                return;
            }

            MultiCall.Instance.DelayCall(1, adUnitId, LoadInterstitialAd);
        }

        protected override void InternalLoadRewardedLoadFailed(string adUnitId)
        {
            var time = 0;
            if (MultiCall.Instance.dictionaryRewarded.ContainsKey(adUnitId))
            {
                var a = MultiCall.Instance.dictionaryRewarded[adUnitId];
                var countAttempt = a.countLoadAttempt;
                try
                {
                    if (a.listTimeRetry.Count > countAttempt - 1)
                    {
                        time = a.listTimeRetry[countAttempt - 1];
                    }
                    else if (a.listTimeRetry.Count > 0)
                    {
                        time = a.listTimeRetry[a.listTimeRetry.Count - 1];
                    }
                }
                catch
                {
                    time = 0;
                }

                var diff = (TimeUtils.UTCNow - a.timeLoad).TotalSeconds;
                Log($"LOAD FAIL Reward {Describe(a, _defaultCountRewarded)} floor={Floor(a.currentFloor)} " +
                    $"fail liên tiếp={countAttempt} sau {diff:F1}s");

                DecreaseMultiplier(a, countAttempt, _defaultCountRewarded, _listMultiplierRewardedDefault);

                var retryDelay = (float)time;
                if (a.multiplier == 0) Log($"  -> DEFAULT retry sau {retryDelay}s");
                //bậc floor: retry hay nhả chỗ do FloorQueue quyết
                if (a.multiplier != 0) RewardedFloors.OnFloorLoadFailed(adUnitId, retryDelay);
                else MultiCall.Instance.DelayCall(retryDelay, adUnitId, LoadRewardedAd);
                return;
            }

            MultiCall.Instance.DelayCall(time, adUnitId, LoadRewardedAd);
        }

        private void ReloadDefaultCountRewarded()
        {
            if (_defaultCountRewarded == 0)
            {
                foreach (var call in MultiCall.Instance.dictionaryRewarded)
                {
                    if (call.Value.multiplier == 0)
                    {
                        _defaultCountRewarded++;
                    }
                }
            }
        }

        public override void LoadRewarded()
        {
            //xử lý _listMultiplierDefault nếu chưa có phần tử
            if (_listMultiplierRewardedDefault.Count == 0)
            {
                ReloadDefaultCountRewarded();
                CaptureConfigMultipliers(MultiCall.Instance.dictionaryRewarded, _defaultCountRewarded,
                    _listMultiplierRewardedDefault);
                _periodDecreaseFloor = FConfigControllerCms.Instance.Config<MultiCallConfig>().periodDecreaseFloor;
            }

            //load 1 id default
            //delay 5,10s để load id default tiếp theo
            //nếu đã load được id với ecpm cao, thì ko load id thấp
            Log($"LoadRewarded: {_defaultCountRewarded} id default, {_listMultiplierRewardedDefault.Count} bậc floor " +
                $"multipliers=[{string.Join(", ", _listMultiplierRewardedDefault)}] " +
                $"periodDecreaseFloor={_periodDecreaseFloor}");
            var list = MultiCall.Instance.GetDictionarySortedByIndex(MultiCall.Instance.dictionaryRewarded);
            for (int i = 0; i < list.Count; i++)
            {
                if (i < _defaultCountRewarded)
                {
                    list[i].Value.multiplier = 0;
                    MultiCall.Instance.DelayCall(i * 5, list[i].Key, LoadRewardedAd);
                }
                else
                {
                    list[i].Value.multiplier = _listMultiplierRewardedDefault[i - _defaultCountRewarded];
                }
            }
        }

        protected override void InternalLoadRewarded(string adUnitId)
        {
            if (MultiCall.Instance.dictionaryRewarded.ContainsKey(adUnitId))
            {
                var a = MultiCall.Instance.dictionaryRewarded[adUnitId];
                var currentFloor = a.multiplier * MultiCall.Instance.valueDefaultRewarded * MultiCall.MULTIPLIER;
                //countLoadAttempt được cộng SAU hàm này -> lúc này = số lần fail liên tiếp trước lần thử này
                Log($"LOAD Reward {Describe(a, _defaultCountRewarded)} floor={Floor(currentFloor)} " +
                    $"(valueDefault=${MultiCall.Instance.valueDefaultRewarded:F6}) " +
                    $"fail liên tiếp={a.countLoadAttempt} -> lần thử #{a.countLoadAttempt + 1}");
                MaxSdk.SetRewardedAdExtraParameter(adUnitId, MultiCall.KEY_SET_BID_FLOOR, currentFloor.ToString(CultureInfo.InvariantCulture));
                a.currentFloor = currentFloor;
                AdViewRequestGate.Open(AdType.Reward, "Max", AdViewRequestGate.MultiCallAlgorithm(GetIndex()));
                SnapshotRequestTier(MultiCall.Instance.dictionaryRewarded, adUnitId);
                MaxSdk.LoadRewardedAd(adUnitId);
            }
        }

        protected override void OnRewardedAdLoadedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            base.OnRewardedAdLoadedEvent(adUnitId, adInfo);
            if (MultiCall.Instance.dictionaryRewarded.ContainsKey(adUnitId))
            {
                var a = MultiCall.Instance.dictionaryRewarded[adUnitId];
                Log($"LOAD OK Reward {Describe(a, _defaultCountRewarded)} floor={Floor(a.currentFloor)} " +
                    $"revenue=${adInfo.Revenue:F6} network={adInfo.NetworkName} " +
                    $"fail trước đó={a.failCountBeforeLoad}");

                if (a.multiplier == 0)
                {
                    MultiCall.Instance.valueDefaultRewarded = LoadedRevenue(adInfo);
                    RewardedFloors.started = true;
                }

                //default xong lần đầu / một bậc vừa có qc (nhả chỗ) -> lấp lại
                RewardedFloors.Refill();
            }
        }

        protected override bool CanLoadRewarded(MultiCall.AdTypeMultiCall a)
        {
            return RewardedFloors.CanLoad(a);
        }

        #endregion

        #region interstitial

        protected override void InternalLoadInterstitialLoadFailed(string adUnitId)
        {
            var time = 0;
            if (MultiCall.Instance.dictionaryInterstitial.ContainsKey(adUnitId))
            {
                var a = MultiCall.Instance.dictionaryInterstitial[adUnitId];
                var countAttempt = a.countLoadAttempt;
                try
                {
                    if (a.listTimeRetry.Count > countAttempt - 1)
                    {
                        time = a.listTimeRetry[countAttempt - 1];
                    }
                    else if (a.listTimeRetry.Count > 0)
                    {
                        time = a.listTimeRetry[a.listTimeRetry.Count - 1];
                    }
                }
                catch
                {
                    time = 0;
                }

                var diff = (TimeUtils.UTCNow - a.timeLoad).TotalSeconds;
                Log($"LOAD FAIL Inter {Describe(a, _defaultCountInterstitial)} floor={Floor(a.currentFloor)} " +
                    $"fail liên tiếp={countAttempt} sau {diff:F1}s");

                DecreaseMultiplier(a, countAttempt, _defaultCountInterstitial, _listMultiplierInterstitialDefault);

                var retryDelay = (float)time;
                if (a.multiplier == 0) Log($"  -> DEFAULT retry sau {retryDelay}s");
                //bậc floor: retry hay nhả chỗ do FloorQueue quyết
                if (a.multiplier != 0) InterstitialFloors.OnFloorLoadFailed(adUnitId, retryDelay);
                else MultiCall.Instance.DelayCall(retryDelay, adUnitId, LoadInterstitialAd);
                return;
            }

            MultiCall.Instance.DelayCall(time, adUnitId, LoadInterstitialAd);
        }

        private void ReloadDefaultCountInterstitial()
        {
            if (_defaultCountInterstitial == 0)
            {
                foreach (var call in MultiCall.Instance.dictionaryInterstitial)
                {
                    if (call.Value.multiplier == 0)
                    {
                        _defaultCountInterstitial++;
                    }
                }
            }
        }

        public override void LoadInterstitial()
        {
            //xử lý _listMultiplierDefault nếu chưa có phần tử
            if (_listMultiplierInterstitialDefault.Count == 0)
            {
                ReloadDefaultCountInterstitial();
                CaptureConfigMultipliers(MultiCall.Instance.dictionaryInterstitial, _defaultCountInterstitial,
                    _listMultiplierInterstitialDefault);
                //trước đây chỉ đọc ở LoadRewarded -> inter chạy trước thì = 0, phép % chia cho 0
                _periodDecreaseFloor = FConfigControllerCms.Instance.Config<MultiCallConfig>().periodDecreaseFloor;
            }

            //load 1 id default
            //delay 5,10s để load id default tiếp theo
            //nếu đã load được id với ecpm cao, thì ko load id thấp
            Log($"LoadInterstitial: {_defaultCountInterstitial} id default, " +
                $"{_listMultiplierInterstitialDefault.Count} bậc floor " +
                $"multipliers=[{string.Join(", ", _listMultiplierInterstitialDefault)}] " +
                $"periodDecreaseFloor={_periodDecreaseFloor}");
            var list = MultiCall.Instance.GetDictionarySortedByIndex(MultiCall.Instance.dictionaryInterstitial);
            for (int i = 0; i < list.Count; i++)
            {
                if (i < _defaultCountInterstitial)
                {
                    list[i].Value.multiplier = 0;
                    MultiCall.Instance.DelayCall(i * 5, list[i].Key, LoadInterstitialAd);
                }
                else
                {
                    list[i].Value.multiplier = _listMultiplierInterstitialDefault[i - _defaultCountInterstitial];
                }
            }
        }

        protected override void InternalLoadInterstitial(string adUnitId)
        {
            if (MultiCall.Instance.dictionaryInterstitial.ContainsKey(adUnitId))
            {
                var a = MultiCall.Instance.dictionaryInterstitial[adUnitId];
                var currentFloor = a.multiplier * MultiCall.Instance.valueDefaultInterstitial * MultiCall.MULTIPLIER;
                Log($"LOAD Inter {Describe(a, _defaultCountInterstitial)} floor={Floor(currentFloor)} " +
                    $"(valueDefault=${MultiCall.Instance.valueDefaultInterstitial:F6}) " +
                    $"fail liên tiếp={a.countLoadAttempt} -> lần thử #{a.countLoadAttempt + 1}");
                MaxSdk.SetInterstitialExtraParameter(adUnitId, MultiCall.KEY_SET_BID_FLOOR, currentFloor.ToString(CultureInfo.InvariantCulture));
                a.currentFloor = currentFloor;
                AdViewRequestGate.Open(AdType.Interstitial, "Max", AdViewRequestGate.MultiCallAlgorithm(GetIndex()));
                SnapshotRequestTier(MultiCall.Instance.dictionaryInterstitial, adUnitId);
                MaxSdk.LoadInterstitial(adUnitId);
            }
        }

        protected override void OnInterstitialLoadedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            base.OnInterstitialLoadedEvent(adUnitId, adInfo);
            if (MultiCall.Instance.dictionaryInterstitial.ContainsKey(adUnitId))
            {
                var a = MultiCall.Instance.dictionaryInterstitial[adUnitId];
                Log($"LOAD OK Inter {Describe(a, _defaultCountInterstitial)} floor={Floor(a.currentFloor)} " +
                    $"revenue=${adInfo.Revenue:F6} network={adInfo.NetworkName} " +
                    $"fail trước đó={a.failCountBeforeLoad}");

                if (a.multiplier == 0)
                {
                    MultiCall.Instance.valueDefaultInterstitial = LoadedRevenue(adInfo);
                    InterstitialFloors.started = true;
                }

                //xem OnRewardedAdLoadedEvent
                InterstitialFloors.Refill();
            }
        }

        protected override bool CanLoadInterstitial(MultiCall.AdTypeMultiCall a)
        {
            return InterstitialFloors.CanLoad(a);
        }

        #endregion

        /// <summary>
        /// Chụp hệ số gốc của từng bậc floor đúng như remote config trả về (sau các id default), để dùng
        /// làm mức khởi đầu, mức reset sau show và mốc chặn dưới khi giảm floor.
        /// </summary>
        private static void CaptureConfigMultipliers(Dictionary<string, MultiCall.AdTypeMultiCall> dict,
            int defaultCount, List<float> into)
        {
            var list = MultiCall.Instance.GetDictionarySortedByIndex(dict);
            for (int i = defaultCount; i < list.Count; i++)
            {
                into.Add(list[i].Value.multiplier);
            }
        }

        /// <summary>
        /// Cứ mỗi _periodDecreaseFloor lần load fail liên tiếp thì giảm hệ số của bậc 0.1, nhưng không
        /// thấp hơn hệ số config của bậc ngay trước nó. Bậc floor đầu tiên chặn ở 1.0 (= giá id default).
        /// </summary>
        private void DecreaseMultiplier(MultiCall.AdTypeMultiCall a, int countAttempt, int defaultCount,
            List<float> configMultipliers)
        {
            if (configMultipliers.Count == 0 || _periodDecreaseFloor <= 0) return;
            if (countAttempt % _periodDecreaseFloor != 0) return;

            var tier = a.index - defaultCount;
            if (tier < 0 || tier >= configMultipliers.Count) return;

            var lowerBound = tier == 0 ? 1f : configMultipliers[tier - 1];
            //đã chạm (hoặc config không tăng dần nên đang thấp hơn) mốc dưới -> giữ nguyên, không đẩy lên
            if (a.multiplier <= lowerBound) return;

            a.multiplier = Mathf.Max(a.multiplier - 0.1f, lowerBound);
            Log($"  -> GIẢM hệ số {a.adUnitId} còn x{a.multiplier.ToString("0.##", CultureInfo.InvariantCulture)} " +
                $"(fail {countAttempt} lần, chặn dưới x{lowerBound.ToString("0.##", CultureInfo.InvariantCulture)})");
        }

        //algo 7 tự quản thứ tự: luôn lấp bậc trống thấp nhất, kể cả khi bậc cao hơn đang có qc
        protected override bool SkipLoadWhenHigherTierLoaded => false;

        //log show: "x1.2" khi đang đúng hệ số config, "x1.2->1.1" khi đã bị giảm
        protected override string DescribeSlotLevel(AdType type, MultiCall.AdTypeMultiCall slot)
        {
            if (slot.multiplier == 0) return "default";

            var configs = type == AdType.Reward ? _listMultiplierRewardedDefault : _listMultiplierInterstitialDefault;
            var defaultCount = type == AdType.Reward ? _defaultCountRewarded : _defaultCountInterstitial;
            var tier = slot.index - defaultCount;
            var current = slot.multiplier.ToString("0.##", CultureInfo.InvariantCulture);
            if (tier < 0 || tier >= configs.Count) return "x" + current;

            var original = configs[tier].ToString("0.##", CultureInfo.InvariantCulture);
            return original == current ? "x" + current : "x" + original + "->" + current;
        }

        //log show: số bậc floor đang bận (đang load / chờ retry) trên số tối đa
        protected override string DescribeFloorSlots(AdType type)
        {
            var queue = type == AdType.Reward ? RewardedFloors : InterstitialFloors;
            return " slot floor=" + queue.Busy + "/" + _GROUP_COUNT;
        }

        /// <summary>
        /// Lập lịch các bậc floor (multiplier != 0) của MỘT format:
        ///  - nhóm mục tiêu = _GROUP_COUNT bậc TRỐNG (chưa có qc) thấp nhất; bậc đang có qc bị bỏ qua dù
        ///    nằm dưới hay trên -> luôn ưu tiên lấp bậc trống thấp trước;
        ///  - bậc BẬN = đang chờ callback load hoặc đang chờ retry; tối đa _GROUP_COUNT bậc bận cùng lúc;
        ///  - bậc vừa trống (show xong) KHÔNG load ngay mà chờ có chỗ. Bậc bận bị đẩy khỏi nhóm mục tiêu
        ///    thì chờ kết quả của nó: thành công -> giữ qc, fail -> không retry nữa; cả hai đều nhả chỗ cho
        ///    bậc trống thấp nhất. Bậc bị đẩy ra mà đang chờ retry (tức đã fail) thì bỏ ngay.
        /// </summary>
        private sealed class FloorQueue
        {
            private readonly string _name;
            private readonly Func<Dictionary<string, MultiCall.AdTypeMultiCall>> _dict;
            private readonly Action<string> _load;
            private readonly Func<MultiCall.AdTypeMultiCall, string> _describe;

            //adUnitId bậc floor đang chờ retry -> mã lượt retry; lượt bị huỷ/thay thì coroutine tự bỏ qua
            private readonly Dictionary<string, int> _retryToken = new();
            private int _retrySeq;

            //đã có id default load xong -> có valueDefault để tính floor, mới bắt đầu load bậc floor
            public bool started;

            /// <summary>Số bậc floor đang bận (đang load / chờ retry).</summary>
            public int Busy => BusyCount(null);

            public FloorQueue(string name, Func<Dictionary<string, MultiCall.AdTypeMultiCall>> dict,
                Action<string> load, Func<MultiCall.AdTypeMultiCall, string> describe)
            {
                _name = name;
                _dict = dict;
                _load = load;
                _describe = describe;
            }

            /// <summary>Còn chỗ thì load các bậc trống thấp nhất chưa bận.</summary>
            public void Refill()
            {
                if (!started) return;

                var target = TargetFloors();
                DropRetriesOutside(target);

                var free = _GROUP_COUNT - BusyCount(null);
                Log($"REFILL {_name}: bậc trống thấp nhất=[{DescribeTargets(target)}] " +
                    $"bận={BusyCount(null)}/{_GROUP_COUNT} -> còn {Mathf.Max(0, free)} chỗ");

                foreach (var pair in target)
                {
                    if (IsBusy(pair)) continue;
                    if (free <= 0)
                    {
                        Log($"  -> CHỜ CHỖ {_name} {_describe(pair.Value)} (đủ {_GROUP_COUNT} bậc đang bận)");
                        continue;
                    }

                    //gọi thẳng (không DelayCall): LoadXAd bật isWaiting ngay nên lần đếm chỗ sau thấy đúng
                    _load(pair.Key);
                    if (pair.Value.isWaiting) free--;
                }
            }

            /// <summary>Cổng load: id default luôn qua; bậc floor phải nằm trong nhóm mục tiêu VÀ còn chỗ.</summary>
            public bool CanLoad(MultiCall.AdTypeMultiCall a)
            {
                if (a.multiplier == 0) return true;
                if (!started) return false;

                return Contains(TargetFloors(), a.adUnitId) && BusyCount(a.adUnitId) < _GROUP_COUNT;
            }

            /// <summary>
            /// Bậc floor load fail (gọi lúc isWaiting còn true). Còn trong nhóm mục tiêu -> giữ chỗ và retry
            /// sau <paramref name="retryDelay"/>; bị đẩy khỏi nhóm -> không retry, nhả chỗ.
            /// </summary>
            public void OnFloorLoadFailed(string adUnitId, float retryDelay)
            {
                _dict().TryGetValue(adUnitId, out var failed);
                var name = failed != null ? _describe(failed) : adUnitId;

                if (!Contains(TargetFloors(), adUnitId))
                {
                    Log($"  -> BỎ {_name} {name}: đã bị đẩy khỏi {_GROUP_COUNT} bậc trống thấp nhất, " +
                        "không retry, nhả chỗ");
                    //parent bỏ isWaiting SAU hàm này -> lấp chỗ ở frame sau mới đếm đúng
                    MultiCall.Instance.DelayCall(0, adUnitId, _ => Refill());
                    return;
                }

                Log($"  -> RETRY {_name} {name} sau {retryDelay}s (vẫn giữ chỗ)");
                var token = ++_retrySeq;
                _retryToken[adUnitId] = token;
                MultiCall.Instance.DelayCall(retryDelay, adUnitId, id =>
                {
                    if (!_retryToken.TryGetValue(id, out var current) || current != token) return;

                    _retryToken.Remove(id);
                    _load(id);
                    //bị cổng chặn (đã bị đẩy khỏi nhóm trong lúc chờ) -> chỗ này trống, lấp cho bậc khác
                    if (_dict().TryGetValue(id, out var slot) && !slot.isWaiting)
                    {
                        Log($"  -> RETRY BỊ CHẶN {_name} {_describe(slot)}: không còn trong nhóm / hết chỗ");
                        Refill();
                    }
                });
            }

            private List<KeyValuePair<string, MultiCall.AdTypeMultiCall>> TargetFloors()
            {
                var result = new List<KeyValuePair<string, MultiCall.AdTypeMultiCall>>(_GROUP_COUNT);
                foreach (var pair in MultiCall.Instance.GetDictionarySortedByIndex(_dict()))
                {
                    if (result.Count >= _GROUP_COUNT) break;
                    if (pair.Value.multiplier == 0 || pair.Value.eCpm >= 0) continue;
                    result.Add(pair);
                }

                return result;
            }

            private bool IsBusy(KeyValuePair<string, MultiCall.AdTypeMultiCall> pair)
                => pair.Value.isWaiting || _retryToken.ContainsKey(pair.Key);

            private int BusyCount(string exclude)
            {
                var count = 0;
                foreach (var pair in _dict())
                {
                    if (pair.Value.multiplier == 0 || pair.Key == exclude) continue;
                    if (IsBusy(pair)) count++;
                }

                return count;
            }

            private void DropRetriesOutside(List<KeyValuePair<string, MultiCall.AdTypeMultiCall>> target)
            {
                List<string> drop = null;
                foreach (var key in _retryToken.Keys)
                {
                    if (Contains(target, key)) continue;
                    (drop ??= new List<string>()).Add(key);
                }

                if (drop == null) return;
                foreach (var key in drop)
                {
                    _retryToken.Remove(key);
                    var label = _dict().TryGetValue(key, out var slot) ? _describe(slot) : key;
                    Log($"  -> BỎ RETRY {_name} {label}: bị đẩy khỏi {_GROUP_COUNT} bậc trống thấp nhất, nhả chỗ");
                }
            }

            private string DescribeTargets(List<KeyValuePair<string, MultiCall.AdTypeMultiCall>> target)
            {
                var parts = new List<string>(target.Count);
                foreach (var pair in target)
                {
                    var state = pair.Value.isWaiting ? "đang load"
                        : _retryToken.ContainsKey(pair.Key) ? "chờ retry"
                        : "rảnh";
                    parts.Add(_describe(pair.Value) + " (" + state + ")");
                }

                return string.Join("; ", parts);
            }

            private static bool Contains(List<KeyValuePair<string, MultiCall.AdTypeMultiCall>> target, string key)
            {
                foreach (var pair in target)
                {
                    if (pair.Key == key) return true;
                }

                return false;
            }
        }

#endif
    }

    public static class MultiCallMaxProvider7Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInit()
        {
            MultiCallMaxProvider7.Instance.Initialize();
        }
    }
}