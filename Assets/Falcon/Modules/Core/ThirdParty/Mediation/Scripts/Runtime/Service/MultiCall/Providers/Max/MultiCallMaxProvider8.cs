using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Falcon.Helpers.Devkit;
using Falcon.Modules.Core.BigData;
using Falcon.Modules.Core.Utils.Time.Runtime;
using UnityEngine;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime
{
    public class MultiCallMaxProvider8 : MultiCallMaxProviderParent<MultiCallMaxProvider8>
    {
#if MAX_ENABLE
        protected override int IndexProvider => 8;

        private const string _LOG_TAG = "[MAX algorithm 8]";
        private const float _MULTIPLIER_STEP = 0.1f;
        private const float _CLOSED_AD_RELOAD_DELAY = 1f;

        private const string _STEP_START = "KHỞI ĐỘNG";
        private const string _STEP_CAPTURE = "CHỤP CONFIG";
        private const string _STEP_SCHEDULE_DEFAULT = "LÊN LỊCH DEFAULT";
        private const string _STEP_RUN_DEFAULT = "CHẠY LỊCH DEFAULT";
        private const string _STEP_GATE = "CỔNG REQUEST";
        private const string _STEP_REQUEST = "GỬI REQUEST";
        private const string _STEP_LOADED = "LOAD OK";
        private const string _STEP_FAILED = "LOAD FAIL";
        private const string _STEP_OFFLINE = "OFFLINE";
        private const string _STEP_RETRY_DEFAULT = "RETRY DEFAULT";
        private const string _STEP_NEW_BASE = "BASE MỚI";
        private const string _STEP_RESET = "RESET BẬC";
        private const string _STEP_DECREASE = "GIẢM HỆ SỐ";
        private const string _STEP_FAIL_LIMIT = "GIỚI HẠN FAIL";
        private const string _STEP_RETRY_FLOOR = "RETRY FLOOR";
        private const string _STEP_RELEASE = "NHẢ CHỖ";
        private const string _STEP_REFILL = "LẤP BẬC";
        private const string _STEP_CACHE = "KHO";
        private const string _STEP_CLOSED = "ĐÓNG AD";
        private const string _STEP_RESUME = "NẠP LẠI";
        private const string _STEP_NETWORK = "MẠNG";

        private static MultiCallMaxProvider8Settings Settings => MultiCallMaxProvider8Config.Current;

        private static bool IsOffline =>
            Application.internetReachability == NetworkReachability.NotReachable;

        private static void Log(string step, string message) =>
            MediationManager.Instance.DebugLog(_LOG_TAG + "[" + step + "] " + message);

        private static string Floor(double floorEcpm)
            => floorEcpm > 0d ? "$" + floorEcpm.ToString("F4", CultureInfo.InvariantCulture) + " eCPM" : "không set";

        private static string Multiplier(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        private Format _rewarded;
        private Format _interstitial;
        private Coroutine _networkWatcher;

        private Format Rewarded => _rewarded ??= new Format(true, LoadRewardedAd);
        private Format Interstitial => _interstitial ??= new Format(false, LoadInterstitialAd);
        private Format FormatOf(AdType type) => type == AdType.Reward ? Rewarded : Interstitial;

        #region rewarded

        public override void LoadRewarded()
        {
            Rewarded.Start();
            StartNetworkWatcher();
        }

        protected override void InternalLoadRewarded(string adUnitId)
        {
            var a = Rewarded.PrepareRequest(adUnitId);
            if (a == null) return;

            MaxSdk.SetRewardedAdExtraParameter(adUnitId, MultiCall.KEY_SET_BID_FLOOR,
                a.currentFloor.ToString(CultureInfo.InvariantCulture));
            AdViewRequestGate.Open(AdType.Reward, "Max", AdViewRequestGate.MultiCallAlgorithm(GetIndex()));
            SnapshotRequestTier(MultiCall.Instance.dictionaryRewarded, adUnitId);
            MaxSdk.LoadRewardedAd(adUnitId);
        }

        protected override void InternalLoadRewardedLoadFailed(string adUnitId) => Rewarded.OnLoadFailed(adUnitId);

        protected override void OnRewardedAdLoadedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            base.OnRewardedAdLoadedEvent(adUnitId, adInfo);
            Rewarded.OnLoaded(adUnitId, LoadedRevenue(adInfo), adInfo.Revenue, adInfo.NetworkName);
        }

        protected override bool CanLoadRewarded(MultiCall.AdTypeMultiCall a) => Rewarded.CanLoad(a);

        protected override void UpdateMultiplier(string adUnitId) => Rewarded.OnAdClosed(adUnitId);

        #endregion

        #region interstitial

        public override void LoadInterstitial()
        {
            Interstitial.Start();
            StartNetworkWatcher();
        }

        protected override void InternalLoadInterstitial(string adUnitId)
        {
            var a = Interstitial.PrepareRequest(adUnitId);
            if (a == null) return;

            MaxSdk.SetInterstitialExtraParameter(adUnitId, MultiCall.KEY_SET_BID_FLOOR,
                a.currentFloor.ToString(CultureInfo.InvariantCulture));
            AdViewRequestGate.Open(AdType.Interstitial, "Max", AdViewRequestGate.MultiCallAlgorithm(GetIndex()));
            SnapshotRequestTier(MultiCall.Instance.dictionaryInterstitial, adUnitId);
            MaxSdk.LoadInterstitial(adUnitId);
        }

        protected override void InternalLoadInterstitialLoadFailed(string adUnitId) =>
            Interstitial.OnLoadFailed(adUnitId);

        protected override void OnInterstitialLoadedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            base.OnInterstitialLoadedEvent(adUnitId, adInfo);
            Interstitial.OnLoaded(adUnitId, LoadedRevenue(adInfo), adInfo.Revenue, adInfo.NetworkName);
        }

        protected override bool CanLoadInterstitial(MultiCall.AdTypeMultiCall a) => Interstitial.CanLoad(a);

        protected override void UpdateMultiplierInterstitial(string adUnitId) => Interstitial.OnAdClosed(adUnitId);

        #endregion

        protected override bool SkipLoadWhenHigherTierLoaded => false;

        protected override string DescribeSlotLevel(AdType type, MultiCall.AdTypeMultiCall slot)
            => FormatOf(type).DescribeLevel(slot);

        protected override string DescribeFloorSlots(AdType type) => FormatOf(type).DescribeSlots();

        #region network

        private void StartNetworkWatcher()
        {
            if (_networkWatcher != null) return;

            Log(_STEP_NETWORK, $"bắt đầu theo dõi mạng: kiểm tra mỗi {Settings.NetworkCheckInterval}s, " +
                               $"trạng thái hiện tại={Application.internetReachability} -> " +
                               (IsOffline ? "OFFLINE (chặn request)" : "ONLINE (cho request)"));
            _networkWatcher = MainGameObj.Instance.StartCoroutine(WatchNetwork());
        }

        private IEnumerator WatchNetwork()
        {
            var wasOffline = IsOffline;
            while (true)
            {
                yield return new WaitForSecondsRealtime(Settings.NetworkCheckInterval);

                var offline = IsOffline;
                if (offline == wasOffline) continue;
                wasOffline = offline;

                if (offline)
                {
                    Log(_STEP_NETWORK, $"MẤT MẠNG ({Application.internetReachability}) -> từ giờ chặn mọi request mới, " +
                                       "request đang bay fail sẽ không bị tính");
                    continue;
                }

                Log(_STEP_NETWORK, $"CÓ MẠNG LẠI ({Application.internetReachability}) -> nạp lại Inter rồi Reward");
                Interstitial.Resume(0f, true, "có mạng lại");
                Rewarded.Resume(0f, true, "có mạng lại");
            }
        }

        #endregion

        private sealed class SlotState
        {
            public int generation;
            public int fails;
            public bool exhausted;
        }

        private sealed class Format
        {
            private readonly bool _rewarded;
            private readonly string _name;
            private readonly Action<string> _load;

            private readonly List<float> _configMultipliers = new();
            private readonly Dictionary<string, SlotState> _states = new();
            private readonly Dictionary<string, int> _floorRetryToken = new();
            private readonly Dictionary<string, int> _defaultLoadToken = new();

            private int _tokenSeq;
            private int _defaultCount;
            private int _generation;
            private bool _captured;
            private bool _started;

            public Format(bool rewarded, Action<string> load)
            {
                _rewarded = rewarded;
                _name = rewarded ? "Reward" : "Inter";
                _load = load;
            }

            private void Step(string step, string message) => Log(step, "[" + _name + "] " + message);

            private Dictionary<string, MultiCall.AdTypeMultiCall> Slots => _rewarded
                ? MultiCall.Instance.dictionaryRewarded
                : MultiCall.Instance.dictionaryInterstitial;

            private double BaseValue
            {
                get => _rewarded ? MultiCall.Instance.valueDefaultRewarded : MultiCall.Instance.valueDefaultInterstitial;
                set
                {
                    if (_rewarded) MultiCall.Instance.valueDefaultRewarded = value;
                    else MultiCall.Instance.valueDefaultInterstitial = value;
                }
            }

            private MultiCallMaxProvider8FormatSettings Config => Settings.For(_rewarded);
            private int MaxConcurrent => Config.MaxConcurrentFloor;
            private int MaxCached => Config.maxCached;

            private float DefaultLoadDelay(string adUnitId, int order, out string source)
            {
                var config = Config;
                if (config.TryGetDefaultLoadDelay(adUnitId, out var delay))
                {
                    source = "riêng theo id (defaultLoadDelayByAdUnit)";
                    return delay;
                }

                delay = config.DefaultLoadDelay(adUnitId, order);
                source = $"mặc định thứ tự {order} x {Mathf.Max(0f, config.defaultLoadDelaySeconds)}s";
                return delay;
            }

            #region lifecycle

            public void Start()
            {
                CaptureConfig();

                Step(_STEP_START, $"{_defaultCount} id default, {_configMultipliers.Count} bậc floor, " +
                                  $"tối đa {MaxConcurrent} bậc floor bận cùng lúc, " +
                                  $"kho tối đa {CacheLimitText()}, base hiện tại=${BaseValue:F6}, " +
                                  $"đã có base={_started} | config {_name}: {Config.Describe()}");

                var list = Sorted();
                for (var i = 0; i < list.Count; i++)
                {
                    var a = list[i].Value;
                    if (i < _defaultCount)
                    {
                        a.multiplier = 0;
                        var delay = DefaultLoadDelay(list[i].Key, i, out var source);
                        Step(_STEP_START, $"  {Describe(a)}: là id default -> lên lịch gọi sau {delay}s ({source})");
                        ScheduleDefaultLoad(list[i].Key, delay, _STEP_START);
                    }
                    else
                    {
                        var before = a.multiplier;
                        a.multiplier = _configMultipliers[i - _defaultCount];
                        Step(_STEP_START, $"  {Describe(a)}: bậc floor, đặt hệ số về config " +
                                          $"x{Multiplier(before)} -> x{Multiplier(a.multiplier)}, " +
                                          "chờ có base eCPM mới load");
                    }
                }
            }

            public void Resume(float firstDelay, bool overridePendingRetry, string reason)
            {
                Step(_STEP_RESUME, $"lý do: {reason} | id default rảnh gọi sau {firstDelay}s + delay của từng id, " +
                                   (overridePendingRetry ? "GHI ĐÈ lịch retry đang chờ" : "giữ nguyên lịch retry đang chờ"));

                var order = 0;
                foreach (var pair in Sorted())
                {
                    var a = pair.Value;
                    if (a.multiplier != 0) continue;

                    if (a.isWaiting)
                    {
                        Step(_STEP_RESUME, $"  BỎ QUA {Describe(a)}: đang chờ kết quả load");
                        continue;
                    }

                    if (a.eCpm >= 0)
                    {
                        Step(_STEP_RESUME, $"  BỎ QUA {Describe(a)}: đang giữ ad ${a.eCpm:F6}");
                        continue;
                    }

                    if (!overridePendingRetry && _defaultLoadToken.ContainsKey(pair.Key))
                    {
                        Step(_STEP_RESUME, $"  BỎ QUA {Describe(a)}: đã có lịch load/retry đang chờ");
                        continue;
                    }

                    var at = firstDelay + DefaultLoadDelay(pair.Key, order++, out var source);
                    Step(_STEP_RESUME, $"  {Describe(a)}: lên lịch gọi sau {at}s ({firstDelay}s + {source})");
                    ScheduleDefaultLoad(pair.Key, at, _STEP_RESUME);
                }

                Refill(_STEP_RESUME);
            }

            public void OnAdClosed(string adUnitId)
            {
                if (Slots.TryGetValue(adUnitId, out var a))
                {
                    if (a.multiplier != 0)
                    {
                        var before = a.multiplier;
                        RestoreConfigMultiplier(a);
                        Step(_STEP_CLOSED, $"{Describe(a)} vừa đóng -> rời kho, hệ số x{Multiplier(before)} " +
                                           $"-> config x{Multiplier(a.multiplier)}");
                    }
                    else
                    {
                        Step(_STEP_CLOSED, $"{Describe(a)} vừa đóng -> rời kho");
                    }
                }

                CountCached(out var used);
                Step(_STEP_CLOSED, $"kho còn {used}/{CacheLimitText()} -> nạp lại id default + lấp bậc floor");
                Resume(_CLOSED_AD_RELOAD_DELAY, false, "đóng ad " + adUnitId);
            }

            #endregion

            #region request gate

            public bool CanLoad(MultiCall.AdTypeMultiCall a)
            {
                if (IsOffline)
                {
                    Step(_STEP_GATE, $"CHẶN {Describe(a)}: offline ({Application.internetReachability})");
                    return false;
                }

                if (IsCacheFull(out var used))
                {
                    Step(_STEP_GATE, $"CHẶN {Describe(a)}: kho đã load {used}/{MaxCached} ad, chạm mức tối đa");
                    return false;
                }

                if (a.multiplier == 0)
                {
                    Step(_STEP_GATE, $"CHO QUA {Describe(a)}: id default (online, kho {used}/{CacheLimitText()})");
                    return true;
                }

                if (!_started)
                {
                    Step(_STEP_GATE, $"CHẶN {Describe(a)}: chưa có base eCPM (chưa id default nào load xong)");
                    return false;
                }

                if (State(a.adUnitId).exhausted)
                {
                    Step(_STEP_GATE, $"CHẶN {Describe(a)}: bậc đã DỪNG vì fail đủ maxFails, chờ base mới");
                    return false;
                }

                if (!Contains(TargetFloors(), a.adUnitId))
                {
                    Step(_STEP_GATE, $"CHẶN {Describe(a)}: không thuộc {MaxConcurrent} bậc trống thấp nhất");
                    return false;
                }

                var busy = BusyCount(a.adUnitId);
                if (busy >= MaxConcurrent)
                {
                    Step(_STEP_GATE, $"CHẶN {Describe(a)}: đủ {busy}/{MaxConcurrent} bậc floor đang bận");
                    return false;
                }

                Step(_STEP_GATE, $"CHO QUA {Describe(a)}: bậc floor, bận {busy}/{MaxConcurrent}, " +
                                 $"kho {used}/{CacheLimitText()}");
                return true;
            }

            private bool IsCacheFull(out int used)
            {
                CountCached(out used);
                var max = MaxCached;
                return max > 0 && used >= max;
            }

            private void CountCached(out int used)
            {
                used = 0;
                foreach (var pair in Slots)
                {
                    if (pair.Value.eCpm >= 0) used++;
                }
            }

            private string CacheLimitText()
            {
                var max = MaxCached;
                return max > 0 ? max.ToString(CultureInfo.InvariantCulture) : "∞";
            }

            #endregion

            #region load callbacks

            public MultiCall.AdTypeMultiCall PrepareRequest(string adUnitId)
            {
                if (!Slots.TryGetValue(adUnitId, out var a))
                {
                    Step(_STEP_REQUEST, $"BỎ {adUnitId}: không có trong danh sách ad unit");
                    return null;
                }

                a.currentFloor = a.multiplier * BaseValue * MultiCall.MULTIPLIER;

                var state = State(adUnitId);
                state.generation = _generation;

                var failLimit = a.multiplier == 0
                    ? "id default, không giới hạn fail"
                    : $"đã fail {state.fails}/{LimitText(adUnitId)} trong base này";

                Step(_STEP_REQUEST, $"{Describe(a)} floor={Floor(a.currentFloor)} = x{Multiplier(a.multiplier)} " +
                                    $"* base ${BaseValue:F6} * {MultiCall.MULTIPLIER} | base#{_generation} | " +
                                    $"fail liên tiếp={a.countLoadAttempt} -> lần thử #{a.countLoadAttempt + 1} | {failLimit}");
                return a;
            }

            public void OnLoaded(string adUnitId, double baseRevenue, double revenue, string network)
            {
                if (!Slots.TryGetValue(adUnitId, out var a)) return;

                var state = State(adUnitId);
                Step(_STEP_LOADED, $"{Describe(a)} floor={Floor(a.currentFloor)} revenue=${revenue:F6} " +
                                   $"network={network} fail trước đó={a.failCountBeforeLoad} | " +
                                   $"request thuộc base#{state.generation}, base hiện tại #{_generation}");

                if (a.multiplier == 0)
                {
                    Step(_STEP_LOADED, $"  -> id default load xong: base eCPM ${BaseValue:F6} -> ${baseRevenue:F6}");
                    BaseValue = baseRevenue;
                    StartNewBase();
                }
                else if (state.generation != _generation)
                {
                    Step(_STEP_LOADED, $"  -> bậc floor thuộc base cũ #{state.generation}: giữ ad, reset bộ đếm theo base mới");
                    ResetSlot(a, state, _STEP_LOADED);
                }
                else
                {
                    Step(_STEP_LOADED, $"  -> bậc floor có ad: đếm fail {state.fails} -> 0");
                    state.fails = 0;
                }

                CountCached(out var used);
                Step(_STEP_CACHE, $"sau LOAD OK kho đã load {used}/{CacheLimitText()}" +
                                  (MaxCached > 0 && used >= MaxCached ? " -> ĐẠT MỨC, dừng gửi request mới" : string.Empty));

                Refill(_STEP_LOADED);
            }

            public void OnLoadFailed(string adUnitId)
            {
                if (!Slots.TryGetValue(adUnitId, out var a)) return;

                var state = State(adUnitId);
                var elapsed = (TimeUtils.UTCNow - a.timeLoad).TotalSeconds;
                Step(_STEP_FAILED, $"{Describe(a)} floor={Floor(a.currentFloor)} fail liên tiếp={a.countLoadAttempt} " +
                                   $"sau {elapsed:F1}s | request thuộc base#{state.generation}, base hiện tại #{_generation}");

                if (IsOffline)
                {
                    var attemptBefore = a.countLoadAttempt;
                    a.countLoadAttempt = Mathf.Max(0, a.countLoadAttempt - 1);
                    Step(_STEP_OFFLINE, $"{Describe(a)} fail lúc offline -> KHÔNG tính: fail liên tiếp " +
                                        $"{attemptBefore} -> {a.countLoadAttempt}, đếm fail giữ {state.fails}, " +
                                        "không retry, chờ có mạng lại");
                    return;
                }

                if (a.multiplier == 0)
                {
                    var defaultDelay = RetryDelay(a);
                    Step(_STEP_RETRY_DEFAULT, $"{Describe(a)} retry sau {defaultDelay}s " +
                                              $"(lịch [{string.Join(",", a.listTimeRetry ?? new List<int>())}], " +
                                              $"lần fail {a.countLoadAttempt})");
                    ScheduleDefaultLoad(adUnitId, defaultDelay, _STEP_RETRY_DEFAULT);
                    return;
                }

                if (state.generation != _generation)
                {
                    Step(_STEP_FAILED, $"  -> request thuộc base cũ #{state.generation}: không giảm hệ số, " +
                                       "không retry theo lịch cũ");
                    ResetSlot(a, state, _STEP_FAILED);
                    Release(adUnitId, "kết quả của base cũ đã về");
                    return;
                }

                state.fails++;
                DecreaseMultiplier(a);

                var limit = Config.MaxFails(adUnitId);
                if (limit > 0 && state.fails >= limit)
                {
                    state.exhausted = true;
                    Step(_STEP_FAIL_LIMIT, $"DỪNG {Describe(a)}: đã fail {state.fails}/{limit} lần trong base#{_generation} " +
                                           "mà chưa có ad -> không request nữa tới base eCPM mới");
                    Release(adUnitId, "bậc đã dừng");
                    return;
                }

                Step(_STEP_FAIL_LIMIT, $"{Describe(a)}: đã fail {state.fails}/{LimitText(adUnitId)} lần trong " +
                                       $"base#{_generation} -> còn được retry");

                OnFloorLoadFailed(adUnitId, RetryDelay(a));
            }

            private static int RetryDelay(MultiCall.AdTypeMultiCall a)
            {
                var retries = a.listTimeRetry;
                if (retries == null || retries.Count == 0) return 0;

                var index = Mathf.Clamp(a.countLoadAttempt - 1, 0, retries.Count - 1);
                return retries[index];
            }

            #endregion

            #region base eCPM

            private void StartNewBase()
            {
                var firstBase = !_started;
                _started = true;
                _generation++;

                Step(_STEP_NEW_BASE, $"#{_generation} = ${BaseValue:F6}" +
                                     (firstBase ? " (base đầu tiên -> bắt đầu load bậc floor)" : string.Empty) +
                                     $" | huỷ {_floorRetryToken.Count} lịch retry floor đang chờ");
                _floorRetryToken.Clear();

                var resetNow = 0;
                var inFlight = 0;
                foreach (var pair in Sorted())
                {
                    var a = pair.Value;
                    if (a.isWaiting)
                    {
                        if (a.multiplier != 0)
                        {
                            inFlight++;
                            Step(_STEP_NEW_BASE, $"  {Describe(a)}: đang chờ kết quả -> chạy nốt, reset khi có kết quả");
                        }

                        continue;
                    }

                    if (a.multiplier == 0)
                    {
                        Step(_STEP_NEW_BASE, $"  {Describe(a)}: id default, fail liên tiếp {a.countLoadAttempt} -> 0");
                        a.countLoadAttempt = 0;
                        continue;
                    }

                    ResetSlot(a, State(pair.Key), _STEP_NEW_BASE);
                    resetNow++;
                }

                Step(_STEP_NEW_BASE, $"tổng kết: reset ngay {resetNow} bậc, {inFlight} bậc đang bay reset sau");
            }

            private void ResetSlot(MultiCall.AdTypeMultiCall a, SlotState state, string trigger)
            {
                var multiplierBefore = a.multiplier;
                var attemptBefore = a.countLoadAttempt;
                var failsBefore = state.fails;
                var exhaustedBefore = state.exhausted;

                RestoreConfigMultiplier(a);
                a.countLoadAttempt = 0;
                state.fails = 0;
                state.exhausted = false;
                state.generation = _generation;

                Step(_STEP_RESET, $"({trigger}) {Describe(a)}: hệ số x{Multiplier(multiplierBefore)} -> " +
                                  $"x{Multiplier(a.multiplier)}, fail liên tiếp {attemptBefore} -> 0, " +
                                  $"đếm fail {failsBefore} -> 0" +
                                  (exhaustedBefore ? ", BỎ trạng thái DỪNG" : string.Empty) +
                                  $", gắn base#{_generation}");
            }

            #endregion

            #region multiplier

            private void CaptureConfig()
            {
                if (_captured) return;

                if (Slots.Count == 0)
                {
                    Step(_STEP_CAPTURE, "chưa có ad unit nào (danh sách bid floor rỗng) -> chưa chụp");
                    return;
                }

                _captured = true;

                foreach (var pair in Slots)
                {
                    if (pair.Value.multiplier == 0) _defaultCount++;
                }

                var list = Sorted();
                for (var i = _defaultCount; i < list.Count; i++)
                {
                    _configMultipliers.Add(list[i].Value.multiplier);
                }

                var tiers = new List<string>(_configMultipliers.Count);
                for (var i = 0; i < _configMultipliers.Count; i++)
                {
                    tiers.Add($"bậc {i + 1} x{Multiplier(_configMultipliers[i])} (sàn x{Multiplier(LowerBound(i))})");
                }

                Step(_STEP_CAPTURE, $"{_defaultCount} id default, {_configMultipliers.Count} bậc floor: " +
                                    $"[{string.Join("; ", tiers)}]");
            }

            private int Tier(MultiCall.AdTypeMultiCall a) => a.index - _defaultCount;

            private bool HasTier(int tier) => tier >= 0 && tier < _configMultipliers.Count;

            private float LowerBound(int tier) => tier == 0 ? 1f : _configMultipliers[tier - 1];

            private void RestoreConfigMultiplier(MultiCall.AdTypeMultiCall a)
            {
                var tier = Tier(a);
                if (HasTier(tier)) a.multiplier = _configMultipliers[tier];
            }

            private void DecreaseMultiplier(MultiCall.AdTypeMultiCall a)
            {
                var period = Config.failsToDecreaseMultiplier;
                if (period <= 0)
                {
                    Step(_STEP_DECREASE, $"{Describe(a)}: KHÔNG giảm (failsToDecreaseMultiplier={period} tắt giảm)");
                    return;
                }

                if (a.countLoadAttempt <= 0 || a.countLoadAttempt % period != 0)
                {
                    Step(_STEP_DECREASE, $"{Describe(a)}: KHÔNG giảm, fail liên tiếp {a.countLoadAttempt} " +
                                         $"chưa chia hết cho {period}");
                    return;
                }

                var tier = Tier(a);
                if (!HasTier(tier))
                {
                    Step(_STEP_DECREASE, $"{Describe(a)}: KHÔNG giảm, không xác định được bậc");
                    return;
                }

                var lowerBound = LowerBound(tier);
                if (a.multiplier <= lowerBound)
                {
                    Step(_STEP_DECREASE, $"{Describe(a)}: KHÔNG giảm, đã ở sàn x{Multiplier(lowerBound)}");
                    return;
                }

                var before = a.multiplier;
                a.multiplier = Mathf.Max(a.multiplier - _MULTIPLIER_STEP, lowerBound);
                Step(_STEP_DECREASE, $"{Describe(a)}: x{Multiplier(before)} -> x{Multiplier(a.multiplier)} " +
                                     $"(fail {a.countLoadAttempt} lần, mỗi {period} lần giảm {_MULTIPLIER_STEP}, " +
                                     $"sàn x{Multiplier(lowerBound)})" +
                                     (a.multiplier <= lowerBound ? " -> ĐÃ CHẠM SÀN, không giảm thêm" : string.Empty));
            }

            #endregion

            #region floor queue

            private void ScheduleDefaultLoad(string adUnitId, float delay, string trigger)
            {
                var token = ++_tokenSeq;
                var replaced = _defaultLoadToken.TryGetValue(adUnitId, out var oldToken);
                _defaultLoadToken[adUnitId] = token;
                Step(_STEP_SCHEDULE_DEFAULT, $"({trigger}) {adUnitId} gọi sau {delay}s, lịch #{token}" +
                                             (replaced ? $" (thay lịch #{oldToken})" : string.Empty));

                MultiCall.Instance.DelayCall(delay, adUnitId, id =>
                {
                    if (!_defaultLoadToken.TryGetValue(id, out var current) || current != token)
                    {
                        Step(_STEP_RUN_DEFAULT, $"{id} lịch #{token} đã bị thay/huỷ -> bỏ qua");
                        return;
                    }

                    _defaultLoadToken.Remove(id);
                    Step(_STEP_RUN_DEFAULT, $"{id} lịch #{token} tới giờ -> gọi load");
                    _load(id);
                    if (Slots.TryGetValue(id, out var slot) && !slot.isWaiting)
                    {
                        Step(_STEP_RUN_DEFAULT, $"{id} không gửi được request (đang có ad / bị cổng chặn / throttled), " +
                                                "chờ đóng ad hoặc có mạng lại để nạp lại");
                    }
                });
            }

            private void Release(string adUnitId, string reason)
            {
                Step(_STEP_RELEASE, $"{adUnitId}: {reason} -> nhả chỗ, lấp bậc khác ở frame sau");
                MultiCall.Instance.DelayCall(0, adUnitId, _ => Refill(_STEP_RELEASE));
            }

            private void Refill(string trigger)
            {
                if (!_started)
                {
                    Step(_STEP_REFILL, $"({trigger}) bỏ qua: chưa có base eCPM");
                    return;
                }

                if (IsOffline)
                {
                    Step(_STEP_REFILL, $"({trigger}) bỏ qua: offline");
                    return;
                }

                var target = TargetFloors();
                DropRetriesOutside(target);

                if (IsCacheFull(out var used))
                {
                    Step(_STEP_REFILL, $"({trigger}) bỏ qua: kho đã load {used}/{MaxCached}, chạm mức tối đa");
                    return;
                }

                var max = MaxConcurrent;
                var busy = BusyCount(null);
                var free = max - busy;
                Step(_STEP_REFILL, $"({trigger}) bậc trống thấp nhất=[{DescribeTargets(target)}] " +
                                   $"bận={busy}/{max} -> còn {Mathf.Max(0, free)} chỗ | kho {used}/{CacheLimitText()}");

                foreach (var pair in target)
                {
                    if (IsBusy(pair)) continue;
                    if (free <= 0)
                    {
                        Step(_STEP_REFILL, $"  CHỜ CHỖ {Describe(pair.Value)} (đủ {max} bậc đang bận)");
                        continue;
                    }

                    _load(pair.Key);
                    if (pair.Value.isWaiting)
                    {
                        free--;
                        Step(_STEP_REFILL, $"  ĐÃ GỬI {Describe(pair.Value)}, còn {free} chỗ");
                    }
                    else
                    {
                        Step(_STEP_REFILL, $"  KHÔNG GỬI ĐƯỢC {Describe(pair.Value)} (xem log {_STEP_GATE} ngay trên)");
                    }
                }
            }

            private void OnFloorLoadFailed(string adUnitId, float retryDelay)
            {
                var name = Slots.TryGetValue(adUnitId, out var failed) ? Describe(failed) : adUnitId;

                if (!Contains(TargetFloors(), adUnitId))
                {
                    Step(_STEP_RETRY_FLOOR, $"KHÔNG retry {name}: đã bị đẩy khỏi {MaxConcurrent} bậc trống thấp nhất");
                    Release(adUnitId, "bị đẩy khỏi nhóm");
                    return;
                }

                var token = ++_tokenSeq;
                _floorRetryToken[adUnitId] = token;
                Step(_STEP_RETRY_FLOOR, $"{name} retry sau {retryDelay}s, lịch #{token} (vẫn giữ chỗ)");

                MultiCall.Instance.DelayCall(retryDelay, adUnitId, id =>
                {
                    if (!_floorRetryToken.TryGetValue(id, out var current) || current != token)
                    {
                        Step(_STEP_RETRY_FLOOR, $"{id} lịch #{token} đã bị huỷ (base mới / bị đẩy khỏi nhóm) -> bỏ qua");
                        return;
                    }

                    _floorRetryToken.Remove(id);
                    Step(_STEP_RETRY_FLOOR, $"{id} lịch #{token} tới giờ -> gọi load");
                    _load(id);
                    if (Slots.TryGetValue(id, out var slot) && !slot.isWaiting)
                    {
                        Step(_STEP_RETRY_FLOOR, $"RETRY BỊ CHẶN {Describe(slot)} -> nhả chỗ, lấp bậc khác");
                        Refill(_STEP_RETRY_FLOOR);
                    }
                });
            }

            private List<KeyValuePair<string, MultiCall.AdTypeMultiCall>> TargetFloors()
            {
                var max = MaxConcurrent;
                var result = new List<KeyValuePair<string, MultiCall.AdTypeMultiCall>>(max);
                foreach (var pair in Sorted())
                {
                    if (result.Count >= max) break;
                    if (pair.Value.multiplier == 0 || pair.Value.eCpm >= 0) continue;
                    if (State(pair.Key).exhausted) continue;
                    result.Add(pair);
                }

                return result;
            }

            private bool IsBusy(KeyValuePair<string, MultiCall.AdTypeMultiCall> pair)
                => pair.Value.isWaiting || _floorRetryToken.ContainsKey(pair.Key);

            private int BusyCount(string exclude)
            {
                var count = 0;
                foreach (var pair in Slots)
                {
                    if (pair.Value.multiplier == 0 || pair.Key == exclude) continue;
                    if (IsBusy(pair)) count++;
                }

                return count;
            }

            private void DropRetriesOutside(List<KeyValuePair<string, MultiCall.AdTypeMultiCall>> target)
            {
                List<string> drop = null;
                foreach (var key in _floorRetryToken.Keys)
                {
                    if (Contains(target, key)) continue;
                    (drop ??= new List<string>()).Add(key);
                }

                if (drop == null) return;
                foreach (var key in drop)
                {
                    _floorRetryToken.Remove(key);
                    var label = Slots.TryGetValue(key, out var slot) ? Describe(slot) : key;
                    Step(_STEP_RETRY_FLOOR, $"HUỶ retry {label}: bị đẩy khỏi {MaxConcurrent} bậc trống thấp nhất, nhả chỗ");
                }
            }

            private static bool Contains(List<KeyValuePair<string, MultiCall.AdTypeMultiCall>> target, string key)
            {
                foreach (var pair in target)
                {
                    if (pair.Key == key) return true;
                }

                return false;
            }

            #endregion

            #region describe

            private List<KeyValuePair<string, MultiCall.AdTypeMultiCall>> Sorted()
                => MultiCall.Instance.GetDictionarySortedByIndex(Slots);

            private SlotState State(string adUnitId)
            {
                if (_states.TryGetValue(adUnitId, out var state)) return state;

                state = new SlotState { generation = _generation };
                _states[adUnitId] = state;
                return state;
            }

            private string LimitText(string adUnitId)
            {
                var limit = Config.MaxFails(adUnitId);
                return limit > 0 ? limit.ToString(CultureInfo.InvariantCulture) : "∞";
            }

            private string Describe(MultiCall.AdTypeMultiCall a)
            {
                var level = a.multiplier == 0
                    ? "DEFAULT #" + (a.index + 1)
                    : "bậc " + (Tier(a) + 1) + " x" + Multiplier(a.multiplier);
                return level + " adUnit=" + a.adUnitId;
            }

            private string DescribeTargets(List<KeyValuePair<string, MultiCall.AdTypeMultiCall>> target)
            {
                var parts = new List<string>(target.Count);
                foreach (var pair in target)
                {
                    var state = pair.Value.isWaiting ? "đang load"
                        : _floorRetryToken.ContainsKey(pair.Key) ? "chờ retry"
                        : "rảnh";
                    parts.Add(Describe(pair.Value) + " (" + state + ")");
                }

                return string.Join("; ", parts);
            }

            public string DescribeLevel(MultiCall.AdTypeMultiCall slot)
            {
                if (slot.multiplier == 0) return "default";

                var current = Multiplier(slot.multiplier);
                var exhausted = State(slot.adUnitId).exhausted ? " dừng" : string.Empty;
                var tier = Tier(slot);
                if (!HasTier(tier)) return "x" + current + exhausted;

                var original = Multiplier(_configMultipliers[tier]);
                return (original == current ? "x" + current : "x" + original + "->" + current) + exhausted;
            }

            public string DescribeSlots()
            {
                CountCached(out var used);
                return " slot floor=" + BusyCount(null) + "/" + MaxConcurrent + " cache=" + used + "/" +
                       CacheLimitText() + " base#" + _generation + (IsOffline ? " OFFLINE" : string.Empty);
            }

            #endregion
        }
#endif
    }

    public static class MultiCallMaxProvider8Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInit()
        {
            MultiCallMaxProvider8.Instance.Initialize();
        }
    }
}
