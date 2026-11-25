/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2026-10-27
 */

#if MAX_ENABLE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Falcon.Admob;
using Falcon.Helpers.Devkit;
using Falcon.Modules.Core.ThirdParty.Mmp.Runtime;
using Falcon.Modules.Level.Core;
using UnityEngine;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime.DFPool
{
    /// <summary>Một ad AdMob trong kho.</summary>
    internal sealed class DFPoolEntry
    {
        public int handle;
        public string admobAdUnitId;
        public double multiplier;
        public double floorEcpmUsd;

        /// <summary>Ad của <c>defaultAdId</c> (xin không giá sàn), không phải của thang giá sàn.</summary>
        public bool isDefault;

        /// <summary>Giá USD của MỘT impression lúc load = max(sàn / 1000, ad_value của AdMob).</summary>
        public double valueUsd;

        public string adSource;
        public DateTime expiresAtUtc;

        /// <summary>Slot đang mượn ad này; null = đang rảnh trong kho.</summary>
        public DFPoolSlot borrowedBy;

        /// <summary>Số lần waterfall MAX không nhận ad này (giá dưới mọi tier, không có tier đáy).</summary>
        public int rejectCount;

        public double SecondsLeft => (expiresAtUtc - DateTime.UtcNow).TotalSeconds;

        // Google multi-platform mediation (cùng cách tầng meta AdMob): response id của ad, lúc load xong, và
        // đã khai báo "thua" với AdMob chưa (mỗi response chỉ khai báo một lần).
        public string responseId;
        public DateTime loadedAtUtc;
        public bool lossRecorded;
    }

    internal enum DFSlotPhase
    {
        /// <summary>Trống, chờ mượn ad.</summary>
        Idle,

        /// <summary>Đã mượn ad, MAX waterfall đang chạy.</summary>
        Loading,

        /// <summary>MAX đã nhận tier cho ad mượn — sẵn sàng show.</summary>
        Ready,

        /// <summary>MAX fill bằng network KHÁC (vd AppLovin Exchange) — show được, không flush được.</summary>
        ReadyForeign,

        /// <summary>Đã trả ad về kho, đang chờ / đang show giả để MAX nhả slot.</summary>
        Flushing,

        /// <summary>Đang chiếu.</summary>
        Showing
    }

    internal sealed class DFPoolSlot
    {
        public readonly string id;
        public DFSlotPhase phase = DFSlotPhase.Idle;
        public DFPoolEntry entry;

        public double foreignRevenue;
        public string foreignNetwork;

        public int loadFails;
        public DateTime nextLoadAtUtc = DateTime.MinValue;

        public bool flushShowSent;
        public DateTime flushRetryAtUtc = DateTime.MinValue;

        // lượt show đang mở
        public bool showIsForeign;
        public int showHandle = -1;
        public double showValue;
        public string showAdUnitId;
        public string showNetwork;
        public double? showFloorEcpm;
        public string showWhere;
        public bool showRewardGranted;
        public bool showPaidReported;
        public string showResponseId;
        public DateTime showStartedUtc;
        public Action showOnReward, showOnHidden, showOnFail;
        public SecondaryAdPaid showOnPaid;

        public DFPoolSlot(string id) => this.id = id;

        public double Value => phase switch
        {
            DFSlotPhase.Ready => entry?.valueUsd ?? SecondaryAdSource.NO_AD,
            DFSlotPhase.ReadyForeign => foreignRevenue,
            _ => SecondaryAdSource.NO_AD
        };

        public void ClearShow()
        {
            showIsForeign = false;
            showHandle = -1;
            showValue = 0d;
            showAdUnitId = null;
            showNetwork = null;
            showFloorEcpm = null;
            showWhere = null;
            showRewardGranted = false;
            showPaidReported = false;
            showResponseId = null;
            showStartedUtc = default;
            showOnReward = showOnHidden = showOnFail = null;
            showOnPaid = null;
        }
    }

    /// <summary>
    /// KHO AdMob + các slot MAX của MỘT format.
    ///
    /// <code>
    ///   KHO (sắp theo giá)      ad#1 $14.2 | ad#2 $11.4 | ad#3 $9.1 | ...
    ///                             │ mượn        │ mượn
    ///                          slot MAX-A    slot MAX-B
    /// </code>
    ///
    ///  - Load: mỗi ad AdMob load xong vào kho kèm giá + hạn dùng.
    ///  - Gắn slot: slot trống mượn ad đắt nhất chưa ai mượn rồi MaxSdk.LoadX(slot) — waterfall nhận
    ///    tier đầu tiên &lt;= giá ad đó.
    ///  - Ad mới đắt hơn ad đang gắn: trả ad cũ về kho (KHÔNG xoá), show giả để MAX nhả slot, rồi gắn
    ///    ad mới.
    ///  - Show: chiếu slot đắt nhất; xong thì slot trống và tự mượn ad tốt nhất tiếp theo.
    ///  - Hết hạn: ad quá hạn bị loại khỏi kho; đang được slot mượn thì slot đó bị flush.
    ///
    /// Chạy hoàn toàn trên Unity main thread.
    /// </summary>
    internal sealed class DFPoolFormat
    {
        private const string _FLUSH_PLACEMENT = "dfpool_flush";
        private const double _PENDING_TIMEOUT_SECONDS = 90d;
        private const double _FLUSH_RETRY_SECONDS = 2d;
        private const int _MAX_REJECTS = 2;
        private const double _DEFAULT_RETRY_BASE_SECONDS = 5d;
        private const double _DEFAULT_RETRY_MAX_SECONDS = 120d;

        public readonly AdType type;
        private readonly int _nativeFormat;
        private readonly DFPoolConfigData _root;
        private readonly List<DFPoolTarget> _targets;

        //ad mặc định (defaultAdId): null = format này không chạy luồng mặc định
        private readonly DFPoolTarget _defaultTarget;
        private int _defaultFails;
        private DateTime _defaultNextAtUtc = DateTime.MinValue;
        private readonly List<DFPoolSlot> _slots = new List<DFPoolSlot>();
        private readonly List<DFPoolEntry> _pool = new List<DFPoolEntry>();

        private sealed class PendingRequest
        {
            public DFPoolTarget target;
            public double floorEcpmUsd;
            public DateTime startedAtUtc;
            public bool isDefault;
        }

        private readonly Dictionary<int, PendingRequest> _pending = new Dictionary<int, PendingRequest>();
        private readonly HashSet<string> _inFlightUnits = new HashSet<string>();
        private bool _pumping;
        private bool _dirty;
        private int _waitLoggedHandle = -1;

        private string Tag => type == AdType.Reward ? "[rewarded]" : "[inter]";

        public DFPoolFormat(AdType type, DFPoolFormatConfig config, DFPoolConfigData root)
        {
            this.type = type;
            _nativeFormat = type == AdType.Reward ? DFPoolNative.FORMAT_REWARDED : DFPoolNative.FORMAT_INTERSTITIAL;
            _root = root;
            _targets = config.ValidTargets();
            foreach (var id in config.ValidSlotIds()) _slots.Add(new DFPoolSlot(id));

            var defaultAdUnitId = config.DefaultAdUnitId;
            if (defaultAdUnitId != null)
                _defaultTarget = new DFPoolTarget { multiplier = 0d, adUnitId = defaultAdUnitId };
        }

        public string DefaultAdUnitId => _defaultTarget?.adUnitId;

        public int TargetCount => _targets.Count;

        public IEnumerable<string> SlotIds
        {
            get
            {
                foreach (var s in _slots) yield return s.id;
            }
        }

        public bool AnySlotShowing
        {
            get
            {
                foreach (var s in _slots)
                    if (s.phase == DFSlotPhase.Showing)
                        return true;
                return false;
            }
        }

        private DFPoolSlot Slot(string id)
        {
            foreach (var s in _slots)
                if (s.id == id)
                    return s;
            return null;
        }

        #region bước 2: revenue MAX -> n request AdMob

        /// <summary>Một ad MAX (bước 1) vừa load xong với revenue USD/impression.</summary>
        public void OnExternalRevenue(double revenueUsd)
        {
            if (revenueUsd <= 0d || _targets.Count == 0) return;
            var baseEcpm = revenueUsd * 1000d;
            var sent = 0;
            var skipped = new StringBuilder();

            foreach (var target in _targets)
            {
                var multiplier = target.multiplier > 0d ? target.multiplier : 1d;
                var floorEcpm = baseEcpm * multiplier;
                var floorValue = floorEcpm / 1000d;

                //một ad unit AdMob chỉ một request cùng lúc
                if (_inFlightUnits.Contains(target.adUnitId))
                {
                    skipped.Append($" x{multiplier:0.##}=đang-xin");
                    continue;
                }

                //kho đầy và giá sàn không hơn ad rẻ nhất đang rảnh -> có load về cũng bị đẩy ra ngay
                if (_pool.Count >= _root.poolCap)
                {
                    var cheapest = CheapestFreeEntry();
                    if (cheapest == null || floorValue <= cheapest.valueUsd)
                    {
                        skipped.Append($" x{multiplier:0.##}=kho-đầy");
                        continue;
                    }
                }

                //đã có ad còn tươi của đúng ad unit này, load ở giá SÀN >= sàn mới -> request thừa
                if (HasFreshEntry(target.adUnitId, floorEcpm))
                {
                    skipped.Append($" x{multiplier:0.##}=đã-có-ad-sàn-cao-hơn");
                    continue;
                }

                sent++;

                var handle = DFPoolMediation.NextHandle();
                _pending[handle] = new PendingRequest
                {
                    target = target, floorEcpmUsd = floorEcpm, startedAtUtc = DateTime.UtcNow
                };
                _inFlightUnits.Add(target.adUnitId);
                AdShowReporter.Requested(type);
                SendLoad(handle, target.adUnitId, floorEcpm);
                DFPoolMediation.Log(DFPoolStage.ADMOB_REQUEST, $"{Tag} request #{handle} {target.adUnitId} x{multiplier:0.##} " +
                                    $"floor={Money(floorValue)} (MAX {Money(revenueUsd)})");
            }

            DFPoolMediation.Log(DFPoolStage.ADMOB_REQUEST,
                $"{Tag} MAX {Money(revenueUsd)} -> gửi {sent}/{_targets.Count} request" +
                (skipped.Length > 0 ? $", bỏ qua:{skipped}" : ""));
        }

        public bool OwnsHandle(int handle) => _pending.ContainsKey(handle);

        public void OnNativeLoaded(int handle, double valueUsd, string adSource, string responseId)
        {
            if (!_pending.TryGetValue(handle, out var req)) return;
            _pending.Remove(handle);
            _inFlightUnits.Remove(req.target.adUnitId);

            var entry = new DFPoolEntry
            {
                handle = handle,
                admobAdUnitId = req.target.adUnitId,
                multiplier = req.target.multiplier,
                floorEcpmUsd = req.floorEcpmUsd,
                isDefault = req.isDefault,
                valueUsd = valueUsd,
                adSource = adSource,
                expiresAtUtc = DateTime.UtcNow.AddMinutes(_root.ttlMinutes),
                responseId = string.IsNullOrEmpty(responseId) ? null : responseId,
                loadedAtUtc = DateTime.UtcNow
            };
            RecordAuction(req.target.adUnitId, responseId);
            _pool.Add(entry);
            if (req.isDefault) _defaultFails = 0;

            AdShowReporter.LoadResult(type, req.target.adUnitId, true, req.target.multiplier, req.floorEcpmUsd,
                valueUsd * 1000d, adSource, null, (long)(DateTime.UtcNow - req.startedAtUtc).TotalMilliseconds);
            DFPoolMediation.Log(req.isDefault ? DFPoolStage.DEFAULT_LOADED : DFPoolStage.ADMOB_LOADED,
                $"{Tag} +#{handle} {Money(valueUsd)} ({adSource}){(req.isDefault ? " [mặc định]" : "")} -> kho {_pool.Count}");
            RequestPump();
        }

        public void OnNativeFailed(int handle, int code, string message, string responseId)
        {
            if (!_pending.TryGetValue(handle, out var req)) return;
            _pending.Remove(handle);
            _inFlightUnits.Remove(req.target.adUnitId);
            //no-fill có response id cũng khai báo hob, như meta AdMob ("cả lượt fill lẫn no-fill")
            RecordAuction(req.target.adUnitId, responseId);
            AdShowReporter.LoadResult(type, req.target.adUnitId, false, req.target.multiplier, req.floorEcpmUsd,
                null, null, code.ToString(CultureInfo.InvariantCulture),
                (long)(DateTime.UtcNow - req.startedAtUtc).TotalMilliseconds);

            if (req.isDefault)
            {
                var wait = DelayDefaultRetry();
                DFPoolMediation.Log(DFPoolStage.DEFAULT_FAILED, $"{Tag} #{handle} {req.target.adUnitId} [mặc định] no fill " +
                                                            $"(code={code}) -> thử lại sau {wait:0}s");
                return;
            }

            DFPoolMediation.Log(DFPoolStage.ADMOB_FAILED, $"{Tag} #{handle} {req.target.adUnitId} no fill (code={code})");
        }

        #endregion

        #region ad mặc định: xin không giá sàn, luôn giữ sẵn 1 ad trong kho

        /// <summary>
        /// Giữ đúng 1 ad của <c>defaultAdId</c> trong kho. Không phụ thuộc revenue MAX: load ngay lượt Pump
        /// đầu tiên, rồi load lại mỗi khi ad đó rời kho (show xong / hết hạn) hoặc không dùng được nữa
        /// (sắp hết hạn, bị waterfall từ chối). Load fail -> chờ giãn dần rồi thử lại.
        ///
        /// Ad mặc định vào CHUNG kho với ad thang giá sàn: cũng sắp theo giá, mượn slot MAX, show qua
        /// DFPoolAdapter như mọi ad khác — nhờ vậy MAX ghi nhận được impression của nó.
        /// </summary>
        private void KeepDefaultLoaded()
        {
            if (_defaultTarget == null) return;
            var adUnitId = _defaultTarget.adUnitId;
            if (_inFlightUnits.Contains(adUnitId)) return;
            if (DateTime.UtcNow < _defaultNextAtUtc) return;

            List<DFPoolEntry> stale = null;
            foreach (var e in _pool)
            {
                if (!e.isDefault) continue;
                //đang được slot mượn, hoặc còn đem gắn slot được -> chưa cần ad mới
                if (e.borrowedBy != null) return;
                if (e.SecondsLeft >= _root.minBindSeconds && e.rejectCount < _MAX_REJECTS) return;
                (stale ??= new List<DFPoolEntry>()).Add(e);
            }

            if (stale != null)
            {
                var rejected = false;
                foreach (var e in stale)
                {
                    rejected |= e.rejectCount >= _MAX_REJECTS;
                    DFPoolMediation.Log(DFPoolStage.DEFAULT_REQUEST, $"{Tag} bỏ ad mặc định #{e.handle} " +
                                        (e.rejectCount >= _MAX_REJECTS ? "(waterfall từ chối)" : "(sắp hết hạn)"));
                    RemoveEntry(e);
                }

                //bị waterfall từ chối = giá dưới mọi tier: load lại ngay cũng sẽ y hệt -> giãn ra như fail
                if (rejected)
                {
                    var wait = DelayDefaultRetry();
                    DFPoolMediation.Log(DFPoolStage.DEFAULT_REQUEST, $"{Tag} ad mặc định bị từ chối -> load lại sau {wait:0}s " +
                                        "(kiểm tra tier đáy is_bottom trên MAX)");
                    return;
                }
            }

            var handle = DFPoolMediation.NextHandle();
            _pending[handle] = new PendingRequest
            {
                target = _defaultTarget, floorEcpmUsd = 0d, startedAtUtc = DateTime.UtcNow, isDefault = true
            };
            _inFlightUnits.Add(adUnitId);
            AdShowReporter.Requested(type);
            SendLoad(handle, adUnitId, 0d);
            DFPoolMediation.Log(DFPoolStage.DEFAULT_REQUEST, $"{Tag} request #{handle} {adUnitId} [mặc định, không giá sàn]");
        }

        /// <summary>Hẹn lượt load ad mặc định kế tiếp: 5s, 10s, 20s... tối đa 120s. Trả về số giây chờ.</summary>
        private double DelayDefaultRetry()
        {
            _defaultFails++;
            var wait = Math.Min(_DEFAULT_RETRY_MAX_SECONDS,
                _DEFAULT_RETRY_BASE_SECONDS * Math.Pow(2d, _defaultFails - 1));
            _defaultNextAtUtc = DateTime.UtcNow.AddSeconds(wait);
            return wait;
        }

        #endregion

        #region điều phối kho <-> slot

        /// <summary>
        /// Đánh dấu cần chạy lại luật; chạy THẬT một lần ở cuối frame (<see cref="PumpIfDirty"/>). Nhờ vậy
        /// mọi ad AdMob / event MAX về gần như đồng thời được gom lại rồi mới quyết định: ad đắt nhất
        /// trong cả đợt mới là ad được gắn, không phải ad về đầu tiên.
        /// </summary>
        public void RequestPump() => _dirty = true;

        public void PumpIfDirty()
        {
            if (!_dirty) return;
            Pump();
        }

        /// <summary>Chạy lại toàn bộ luật: hết hạn -> đổi ad tốt hơn -> gắn slot trống -> giới hạn kho.</summary>
        public void Pump()
        {
            if (_pumping) return;
            _pumping = true;
            _dirty = false;
            try
            {
                DropStuckRequests();
                Expire();
                KeepDefaultLoaded();
                ContinueFlushes();
                Upgrade();
                FillIdleSlots();
                EnforceCap();
                LogSnapshotIfChanged();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                _pumping = false;
            }
        }

        private string _lastSnapshot;

        /// <summary>
        /// [POOL]: trạng thái 2 slot + toàn bộ kho, chỉ in khi slot / kho THỰC SỰ đổi. So sánh bỏ qua số
        /// phút còn lại của ad — nếu không, đồng hồ đếm hạn làm dòng này in lại mỗi phút.
        /// </summary>
        private void LogSnapshotIfChanged()
        {
#if ENABLE_DEBUG_LOG
            var snapshot = DescribeCache(withTtl: false);
            if (snapshot == _lastSnapshot) return;
            _lastSnapshot = snapshot;
            DFPoolMediation.Log(DFPoolStage.POOL, $"{Tag}\n{DescribeCache()}");
#endif
        }

        private void DropStuckRequests()
        {
            List<int> stuck = null;
            foreach (var kv in _pending)
                if ((DateTime.UtcNow - kv.Value.startedAtUtc).TotalSeconds > _PENDING_TIMEOUT_SECONDS)
                    (stuck ??= new List<int>()).Add(kv.Key);
            if (stuck == null) return;
            foreach (var h in stuck)
            {
                _inFlightUnits.Remove(_pending[h].target.adUnitId);
                _pending.Remove(h);
            }
        }

        private void Expire()
        {
            for (var i = _pool.Count - 1; i >= 0; i--)
            {
                var entry = _pool[i];
                if (entry.SecondsLeft > 0d) continue;

                var slot = entry.borrowedBy;
                if (slot != null && (slot.phase == DFSlotPhase.Ready || slot.phase == DFSlotPhase.Loading))
                {
                    DFPoolMediation.Log(DFPoolStage.EXPIRE, $"{Tag} #{entry.handle} hết hạn khi slot {slot.id} đang mượn -> flush");
                    StartFlush(slot);
                }

                RemoveEntry(entry);
            }
        }

        /// <summary>Trả ad của slot về kho (ad VẪN trong kho) rồi bắt đầu nhả slot MAX.</summary>
        private void StartFlush(DFPoolSlot slot)
        {
            DFPoolNative.ReleaseSlot(slot.id);
            if (slot.entry != null) slot.entry.borrowedBy = null;
            slot.entry = null;

            //Ready: MAX đang giữ ad -> show giả ở ContinueFlushes. Loading: chờ MAX trả kết quả
            //load (tier kế tiếp nhận rỗng), xem OnMaxLoaded / OnMaxLoadFailed.
            slot.phase = DFSlotPhase.Flushing;
            slot.flushShowSent = false;
            slot.flushRetryAtUtc = DateTime.MinValue;
        }

        private void ContinueFlushes()
        {
            foreach (var slot in _slots)
            {
                if (slot.phase != DFSlotPhase.Flushing || slot.flushShowSent) continue;
                if (DateTime.UtcNow < slot.flushRetryAtUtc) continue;
                //HollowClaimed: slot đã trả ad sau khi MAX nhận tier. None: không còn binding nào.
                //Cả hai trường hợp adapter đều trả AD_NOT_READY -> không có gì lên màn hình.
                var state = DFPoolNative.GetSlotState(slot.id);
                if (state != DFPoolNative.SlotState.HollowClaimed && state != DFPoolNative.SlotState.None) continue;
                if (!IsMaxReady(slot.id)) continue;
                //đang có ad khác trên màn hình -> chờ, show giả lúc này MAX có thể từ chối mà vẫn giữ ad
                if (DFPoolMediation.IsAnyFullscreenShowing) continue;

                slot.flushShowSent = true;
                DFPoolMediation.Log(DFPoolStage.FLUSH, $"{Tag} slot {slot.id}: show giả để MAX nhả slot");
                ShowMax(slot.id, _FLUSH_PLACEMENT);
            }
        }

        /// <summary>
        /// Đưa ad đắt nhất trong kho vào slot SỚM NHẤT có thể, kể cả khi ad về dồn dập trong lúc các slot
        /// đang load / đang chờ nhả:
        ///  A. Slot đang Loading (waterfall chưa nhận tier): ĐỔI THẲNG ad mượn sang ad tốt hơn ở native,
        ///     không flush — tier kế tiếp được so với giá ad mới.
        ///  B. Slot Ready: trả ad cũ về kho + show giả để lấy ad tốt hơn, NHƯNG luôn chừa lại ít nhất một
        ///     slot show được — không bao giờ để cả 2 slot cùng đang reset.
        ///  Slot đang Flushing không cần làm gì: lúc nhả xong nó mượn ad đắt nhất CÓ MẶT LÚC ĐÓ trong kho
        ///  (FillIdleSlots), nên ad về trong lúc chờ vẫn được tính.
        /// </summary>
        private void Upgrade()
        {
            RebindLoadingSlots();
            UpgradeReadySlots();
        }

        /// <summary>Slot trống lấy được ad ngay trong lượt Pump này (không bị backoff).</summary>
        private int IdleSlotsReadyToLoad()
        {
            var n = 0;
            foreach (var s in _slots)
                if (s.phase == DFSlotPhase.Idle && DateTime.UtcNow >= s.nextLoadAtUtc)
                    n++;
            return n;
        }

        /// <summary>Số slot đang show được ngay (Ready còn hạn hoặc ReadyForeign, MAX báo ready).</summary>
        private int ShowableSlots()
        {
            var n = 0;
            foreach (var s in _slots)
            {
                if (s.phase == DFSlotPhase.Ready && s.entry != null && s.entry.SecondsLeft > 0d && IsMaxReady(s.id)) n++;
                else if (s.phase == DFSlotPhase.ReadyForeign && IsMaxReady(s.id)) n++;
            }

            return n;
        }

        private bool Beats(DFPoolEntry candidate, DFPoolEntry current)
            => current == null || candidate.valueUsd > current.valueUsd * (1d + _root.upgradeMinGain);

        private void RebindLoadingSlots()
        {
            for (var guard = 0; guard < _slots.Count; guard++)
            {
                var free = FreeEntriesSorted();
                //slot trống sẽ lấy các ad đắt nhất ngay sau đây (FillIdleSlots) -> nhường cho chúng trước,
                //để cả hai slot cùng có ad thay vì một slot đổi qua đổi lại
                var reserved = IdleSlotsReadyToLoad();
                if (free.Count <= reserved) return;
                var candidate = free[reserved];

                DFPoolSlot weakest = null;
                foreach (var s in _slots)
                    if (s.phase == DFSlotPhase.Loading && s.entry != null &&
                        (weakest == null || s.entry.valueUsd < weakest.entry.valueUsd))
                        weakest = s;
                if (weakest == null || !Beats(candidate, weakest.entry)) return;

                var old = weakest.entry;
                //false = waterfall vừa nhận tier cho ad cũ -> slot sắp Ready, nhánh B lo
                if (!DFPoolNative.RebindPending(weakest.id, candidate.handle))
                {
                    DFPoolMediation.Log(DFPoolStage.REBIND, $"{Tag} slot {weakest.id}: waterfall vừa nhận tier cho " +
                                                        $"#{weakest.entry.handle}, không đổi được -> chờ READY rồi B4-UPGRADE");
                    return;
                }

                old.borrowedBy = null;
                candidate.borrowedBy = weakest;
                weakest.entry = candidate;
                DFPoolMediation.Log(DFPoolStage.REBIND, $"{Tag} slot {weakest.id} đang load: đổi #{old.handle} {Money(old.valueUsd)}" +
                                    $" -> #{candidate.handle} {Money(candidate.valueUsd)} (không cần reset)");
            }
        }

        private void UpgradeReadySlots()
        {
            for (var guard = 0; guard < _slots.Count; guard++)
            {
                var free = FreeEntriesSorted();

                //slot trống / đang flush sẽ lấy trước các ad đắt nhất đang rảnh
                var reserved = IdleSlotsReadyToLoad();
                foreach (var s in _slots)
                    if (s.phase == DFSlotPhase.Flushing)
                        reserved++;
                if (free.Count <= reserved) return;
                var candidate = free[reserved];

                DFPoolSlot weakest = null;
                foreach (var s in _slots)
                    if (s.phase == DFSlotPhase.Ready && s.entry != null &&
                        (weakest == null || s.entry.valueUsd < weakest.entry.valueUsd))
                        weakest = s;
                if (weakest == null || !Beats(candidate, weakest.entry)) return;

                //chừa lại ít nhất MỘT slot show được — reset nốt slot cuối là mở ra khoảng trống "không có
                //ad" đúng lúc game gọi show. Slot kia nhả xong + Ready thì lượt Pump sau mới tới slot này.
                if (ShowableSlots() <= 1)
                {
                    if (_waitLoggedHandle == candidate.handle) return;
                    _waitLoggedHandle = candidate.handle;
                    DFPoolMediation.Log(DFPoolStage.WAIT, $"{Tag} có #{candidate.handle} {Money(candidate.valueUsd)} tốt hơn slot " +
                                        $"{weakest.id}, nhưng đây là slot show được cuối cùng -> chờ slot kia xong");
                    return;
                }

                DFPoolMediation.Log(DFPoolStage.UPGRADE, $"{Tag} slot {weakest.id}: #{weakest.entry.handle} {Money(weakest.entry.valueUsd)}" +
                                    $" -> có #{candidate.handle} {Money(candidate.valueUsd)} tốt hơn, trả ad cũ về kho");
                StartFlush(weakest);
                ContinueFlushes();
            }
        }

        private void FillIdleSlots()
        {
            foreach (var slot in _slots)
            {
                if (slot.phase != DFSlotPhase.Idle) continue;
                if (DateTime.UtcNow < slot.nextLoadAtUtc) continue;

                //MAX vẫn còn giữ ad ở slot này (vd foreign chưa show) -> không load đè
                if (IsMaxReady(slot.id)) continue;

                while (true)
                {
                    var free = FreeEntriesSorted();
                    if (free.Count == 0) break;
                    var entry = free[0];
                    if (DFPoolNative.BindSlot(slot.id, entry.handle))
                    {
                        entry.borrowedBy = slot;
                        slot.entry = entry;
                        slot.phase = DFSlotPhase.Loading;
                        DFPoolMediation.Log(DFPoolStage.BIND, $"{Tag} slot {slot.id} mượn #{entry.handle} {Money(entry.valueUsd)} -> load MAX");
                        LoadMax(slot.id);
                        break;
                    }

                    //native không còn ad này (đã mất) -> bỏ khỏi kho, thử ad kế
                    RemoveEntry(entry);
                }
            }
        }

        private void EnforceCap()
        {
            while (_pool.Count > _root.poolCap)
            {
                var cheapest = CheapestFreeEntry();
                if (cheapest == null) return;
                DFPoolMediation.Log(DFPoolStage.EVICT, $"{Tag} kho đầy -> bỏ #{cheapest.handle} {Money(cheapest.valueUsd)}");
                RemoveEntry(cheapest);
            }
        }

        private List<DFPoolEntry> FreeEntriesSorted()
        {
            var list = new List<DFPoolEntry>();
            foreach (var e in _pool)
                if (e.borrowedBy == null && e.SecondsLeft >= _root.minBindSeconds && e.rejectCount < _MAX_REJECTS)
                    list.Add(e);
            list.Sort((a, b) => b.valueUsd.CompareTo(a.valueUsd));
            return list;
        }

        /// <summary>
        /// Ad rảnh rẻ nhất để đẩy ra khi kho đầy. KHÔNG tính ad mặc định: đẩy nó ra thì
        /// <see cref="KeepDefaultLoaded"/> lại load ngay ad khác -> vòng lặp request vô ích. Ad mặc định
        /// tự rời kho khi show / hết hạn / bị từ chối.
        /// </summary>
        private DFPoolEntry CheapestFreeEntry()
        {
            DFPoolEntry cheapest = null;
            foreach (var e in _pool)
                if (e.borrowedBy == null && !e.isDefault && (cheapest == null || Score(e) < Score(cheapest)))
                    cheapest = e;
            return cheapest;

            //ad sắp hết hạn / bị waterfall từ chối coi như rẻ nhất
            double Score(DFPoolEntry e) =>
                e.SecondsLeft < _root.minBindSeconds || e.rejectCount >= _MAX_REJECTS ? -1d : e.valueUsd;
        }

        /// <summary>
        /// Kho đã có ad còn dùng được của <paramref name="admobAdUnitId"/> được load ở giá SÀN
        /// &gt;= <paramref name="floorEcpmUsd"/> chưa. So SÀN với SÀN (không so với revenue thực của ad):
        /// sàn mới không vượt sàn đã xin thành công thì request mới không có gì hơn.
        /// </summary>
        private bool HasFreshEntry(string admobAdUnitId, double floorEcpmUsd)
        {
            foreach (var e in _pool)
                if (e.admobAdUnitId == admobAdUnitId && e.floorEcpmUsd >= floorEcpmUsd &&
                    e.SecondsLeft >= _root.minBindSeconds && e.rejectCount < _MAX_REJECTS)
                    return true;
            return false;
        }

        private void RemoveEntry(DFPoolEntry entry)
        {
            if (entry.borrowedBy != null && entry.borrowedBy.entry == entry) entry.borrowedBy.entry = null;
            entry.borrowedBy = null;
            _pool.Remove(entry);
            DFPoolNative.DestroyAd(entry.handle);
        }

        /// <summary>Slot không còn giữ ad mượn (load fail / nhả xong / foreign) -> ad về kho.</summary>
        private void ReturnEntry(DFPoolSlot slot)
        {
            if (slot.entry != null) slot.entry.borrowedBy = null;
            slot.entry = null;
        }

        private void ToIdle(DFPoolSlot slot)
        {
            DFPoolNative.ClearSlot(slot.id);
            ReturnEntry(slot);
            slot.phase = DFSlotPhase.Idle;
            slot.flushShowSent = false;
        }

        #endregion

        #region MAX events của slot

        public void OnMaxLoaded(string slotId, MaxSdkBase.AdInfo adInfo)
        {
            var slot = Slot(slotId);
            if (slot == null) return;
            var state = DFPoolNative.GetSlotState(slotId);

            //Adapter đã nhận tier (Claimed / HollowClaimed) CHƯA đủ để kết luận ad là của ta: MAX vẫn có
            //thể chọn bid AppLovin cao hơn. Phải khớp cả network MAX trả về, nếu không thì coi là ad
            //foreign -> KHÔNG bao giờ show giả (show giả lúc đó sẽ chiếu thật ad AppLovin).
            var isWaterfallAd = IsWaterfallAd(adInfo);
            if (!isWaterfallAd &&
                (state == DFPoolNative.SlotState.Claimed || state == DFPoolNative.SlotState.HollowClaimed))
            {
                DFPoolMediation.Log(DFPoolStage.FOREIGN, $"{Tag} slot {slotId}: adapter đã nhận tier nhưng MAX trả network " +
                                    $"'{adInfo?.NetworkName}' -> coi là ad foreign, không reset");
                state = DFPoolNative.SlotState.None;
            }

            switch (slot.phase)
            {
                case DFSlotPhase.Loading when state == DFPoolNative.SlotState.Claimed:
                    slot.phase = DFSlotPhase.Ready;
                    slot.loadFails = 0;
                    DFPoolMediation.Log(DFPoolStage.READY, $"{Tag} slot {slotId} READY #{slot.entry?.handle} " +
                                        $"{Money(slot.entry?.valueUsd ?? 0d)} (MAX tier {Money(adInfo?.Revenue ?? 0d)})");
                    break;

                case DFSlotPhase.Loading when state == DFPoolNative.SlotState.HollowClaimed:
                case DFSlotPhase.Flushing when state == DFPoolNative.SlotState.HollowClaimed:
                    ReturnEntry(slot);
                    slot.phase = DFSlotPhase.Flushing;
                    DFPoolMediation.Log(DFPoolStage.FLUSH, $"{Tag} slot {slotId}: waterfall nhận tier rỗng (hollow) -> chờ show giả");
                    break;

                case DFSlotPhase.Loading:
                case DFSlotPhase.Flushing:
                    //waterfall không gọi tới adapter của ta mà vẫn fill -> network khác của MAX thắng
                    DFPoolNative.ClearSlot(slotId);
                    ReturnEntry(slot);
                    slot.phase = DFSlotPhase.ReadyForeign;
                    slot.loadFails = 0;
                    slot.foreignRevenue = adInfo != null && adInfo.Revenue > 0d ? adInfo.Revenue : 0d;
                    slot.foreignNetwork = adInfo?.NetworkName;
                    DFPoolMediation.Log(DFPoolStage.FOREIGN, $"{Tag} slot {slotId} bị {slot.foreignNetwork} fill " +
                                        $"{Money(slot.foreignRevenue)} — giữ tới lúc show");
                    break;
            }

            RequestPump();
        }

        public void OnMaxLoadFailed(string slotId, MaxSdkBase.ErrorInfo error)
        {
            var slot = Slot(slotId);
            if (slot == null) return;

            if (slot.phase == DFSlotPhase.Loading)
            {
                if (slot.entry != null) slot.entry.rejectCount++;
                slot.loadFails++;
                slot.nextLoadAtUtc = DateTime.UtcNow.AddSeconds(Math.Min(64d, Math.Pow(2d, slot.loadFails)));
                DFPoolMediation.Log(DFPoolStage.LOAD_FAIL, $"{Tag} slot {slotId} MAX load fail ({error?.Code}) -> trả ad về kho, " +
                                    $"thử lại sau {(slot.nextLoadAtUtc - DateTime.UtcNow).TotalSeconds:0}s");
                ToIdle(slot);
            }
            else if (slot.phase == DFSlotPhase.Flushing)
            {
                DFPoolMediation.Log(DFPoolStage.FLUSH_DONE, $"{Tag} slot {slotId} load fail lúc đang nhả -> slot trống");
                ToIdle(slot);
            }

            RequestPump();
        }

        public void OnMaxDisplayed(string slotId)
        {
            var slot = Slot(slotId);
            if (slot == null || slot.phase != DFSlotPhase.Showing) return;
            DFPoolMediation.Log(DFPoolStage.DISPLAYED, $"{Tag} slot {slotId} đã lên màn hình " +
                                                       $"({(slot.showIsForeign ? slot.showNetwork : "AdMob #" + slot.showHandle)})");
            //MMP lúc lên hình như MAX / meta AdMob, kèm af_level. FSN đã tự nghe MAX displayed.
            try
            {
                FalconMmpLog.LogEvent(
                    type == AdType.Reward ? FalconMmpLog.MMP_REWARDED_DISPLAYED : FalconMmpLog.MMP_INTERS_DISPLAYED,
                    new Dictionary<string, string> { { "af_level", LevelData.Instance.level + "" } });
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        public void OnMaxClicked(string slotId)
        {
            var slot = Slot(slotId);
            if (slot == null || slot.phase != DFSlotPhase.Showing) return;
            AdShowReporter.Clicked(type);
        }

        public void OnMaxDisplayFailed(string slotId, MaxSdkBase.ErrorInfo error)
        {
            var slot = Slot(slotId);
            if (slot == null) return;

            if (slot.phase == DFSlotPhase.Flushing)
            {
                if (IsMaxReady(slotId))
                {
                    //MAX từ chối show (vd đang có ad khác) mà vẫn giữ ad -> thử lại sau
                    DFPoolMediation.Log(DFPoolStage.FLUSH_RETRY, $"{Tag} slot {slotId}: MAX từ chối show giả ({error?.Code}) " +
                                                             $"mà vẫn giữ ad -> thử lại sau {_FLUSH_RETRY_SECONDS:0}s");
                    slot.flushShowSent = false;
                    slot.flushRetryAtUtc = DateTime.UtcNow.AddSeconds(_FLUSH_RETRY_SECONDS);
                }
                else
                {
                    DFPoolMediation.Log(DFPoolStage.FLUSH_DONE, $"{Tag} slot {slotId} đã được nhả");
                    ToIdle(slot);
                }

                RequestPump();
                return;
            }

            if (slot.phase != DFSlotPhase.Showing) return;

            DFPoolMediation.Log(DFPoolStage.SHOW_FAIL, $"{Tag} slot {slotId} show lỗi ({error?.Code} {error?.Message}), " +
                                                   $"MAX còn giữ ad={IsMaxReady(slotId)}");
            AdShowReporter.ShowFailed(type, error?.Code.ToString(), slot.showAdUnitId, slot.showNetwork,
                slot.showFloorEcpm, slot.showValue > 0d ? slot.showValue * 1000d : (double?)null);
            var onFail = slot.showOnFail;
            var wasForeign = slot.showIsForeign;
            var foreignValue = slot.showValue;

            if (IsMaxReady(slotId))
            {
                //MAX từ chối show mà vẫn giữ ad trong slot
                if (slot.showHandle >= 0) DFPoolNative.DestroyAd(slot.showHandle);
                slot.ClearShow();
                DFPoolMediation.EndShow();
                if (wasForeign)
                {
                    //ad thật của network khác -> giữ lại cho lượt sau
                    slot.phase = DFSlotPhase.ReadyForeign;
                    slot.foreignRevenue = foreignValue;
                }
                else
                {
                    //ad AdMob đã bị bỏ -> slot đang giữ vỏ rỗng, show giả để nhả
                    DFPoolNative.ClearSlot(slotId);
                    slot.phase = DFSlotPhase.Flushing;
                    slot.flushShowSent = false;
                    slot.flushRetryAtUtc = DateTime.UtcNow.AddSeconds(_FLUSH_RETRY_SECONDS);
                }

                RequestPump();
            }
            else
            {
                FinishShow(slot);
            }

            onFail?.Invoke();
        }

        public void OnMaxHidden(string slotId)
        {
            var slot = Slot(slotId);
            if (slot == null) return;

            if (slot.phase == DFSlotPhase.Flushing)
            {
                DFPoolMediation.Log(DFPoolStage.FLUSH_DONE, $"{Tag} slot {slotId} hidden lúc đang nhả (bất thường) -> slot trống");
                ToIdle(slot);
                RequestPump();
                return;
            }

            if (slot.phase != DFSlotPhase.Showing) return;

            DFPoolMediation.Log(DFPoolStage.HIDDEN, $"{Tag} slot {slotId} đóng ad (reward={slot.showRewardGranted}, " +
                                                $"paid={slot.showPaidReported}) -> slot trống, mượn ad kế tiếp");
            AdShowReporter.Closed(type, type == AdType.Reward ? slot.showRewardGranted : (bool?)null, slot.showWhere);
            var onHidden = slot.showOnHidden;
            FinishShow(slot);
            onHidden?.Invoke();
        }

        public void OnMaxReward(string slotId)
        {
            var slot = Slot(slotId);
            if (slot == null || slot.phase != DFSlotPhase.Showing || slot.showRewardGranted) return;
            slot.showRewardGranted = true;
            DFPoolMediation.Log(DFPoolStage.REWARD, $"{Tag} slot {slotId} user nhận thưởng");
            slot.showOnReward?.Invoke();
        }

        /// <summary>Doanh thu theo MAX — chỉ dùng cho slot bị network khác fill (ad AdMob đã có OnPaid thật).</summary>
        public void OnMaxRevenuePaid(string slotId, MaxSdkBase.AdInfo adInfo)
        {
            var slot = Slot(slotId);
            if (slot == null || adInfo == null || !slot.showIsForeign || slot.showPaidReported) return;
            slot.showPaidReported = true;
            DFPoolMediation.Log(DFPoolStage.PAID, $"{Tag} slot {slotId} paid {Money(adInfo.Revenue)} ({adInfo.NetworkName}, theo MAX)");
            var partners = FalconMaxService.Current;
            AdShowReporter.Revenue(AdShowReporter.WithMax(new AdShowReporter.Impression
            {
                type = type,
                mediation = AdShowReporter.MEDIATION_MAX,
                adUnitId = slotId,
                network = adInfo.NetworkName,
                revenue = adInfo.Revenue,
                precision = adInfo.RevenuePrecision,
                where = slot.showWhere,
                estimatedEcpmUsd = adInfo.Revenue * 1000d,
                bigDataOnly = partners != null
            }, adInfo));
            partners?.LogExternalMaxRevenue(adInfo, type);
            NotifyPaid(slot, AdShowReporter.MEDIATION_MAX, adInfo.NetworkName, adInfo.Revenue);
            //network khác của MAX thắng lượt này -> các ad AdMob trong kho đều thua
            OnExternalImpression(adInfo.Revenue, adInfo.RevenuePrecision);
        }

        public void OnNativePaid(string slotId, long valueMicros, string currency, int precision, string adSource)
        {
            var slot = Slot(slotId);
            if (slot == null || slot.showPaidReported) return;
            slot.showPaidReported = true;

            var revenue = valueMicros / 1_000_000d;
            var network = string.IsNullOrEmpty(adSource) ? slot.showNetwork : adSource;
            DFPoolMediation.Log(DFPoolStage.PAID, $"{Tag} slot {slotId} paid {Money(revenue)} {currency} ({adSource}), " +
                                $"giá lúc load {Money(slot.showValue)}");
            AdShowReporter.Revenue(new AdShowReporter.Impression
            {
                type = type,
                mediation = AdShowReporter.MEDIATION_ADMOB,
                adUnitId = slot.showAdUnitId,
                network = network,
                revenue = revenue,
                precision = AdShowReporter.PrecisionName(precision),
                where = slot.showWhere,
                floorEcpmUsd = slot.showFloorEcpm,
                estimatedEcpmUsd = slot.showValue * 1000d,
                formatRaw = AdShowReporter.AdmobFormatRaw(type)
            });
            NotifyPaid(slot, AdShowReporter.MEDIATION_ADMOB, network, revenue);
            RecordWinImpression(slot, valueMicros, precision);
        }

        #region Google multi-platform mediation (y như tầng meta AdMob: AdmobBidAd / AdmobFullscreenAds)

        private static string PublisherOf(string adUnitId)
        {
            if (string.IsNullOrEmpty(adUnitId)) return null;
            var slash = adUnitId.IndexOf('/');
            return slash > 0 ? adUnitId.Substring(0, slash) : null;
        }

        private AdmobAdFormat AdmobFormat => type == AdType.Reward ? AdmobAdFormat.Rewarded : AdmobAdFormat.Interstitial;

        /// <summary>Gửi request AdMob kèm extras multi-platform mediation (auction_context / ua_channel / days_since_install).</summary>
        private void SendLoad(int handle, string adUnitId, double floorEcpm)
        {
            string uaChannel = null;
            string daysSinceInstall = null;
            try
            {
                var channel = AdmobMediationHooks.UaChannel();
                if (!string.IsNullOrEmpty(channel))
                    uaChannel = AdmobMultiPlatformUtils.UserAcquisitionChannel.FromNetwork(channel);
                var days = AdmobMediationHooks.DaysSinceInstall();
                if (days.HasValue) daysSinceInstall = days.Value.ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            DFPoolNative.LoadAd(_nativeFormat, handle, adUnitId, floorEcpm, uaChannel, daysSinceInstall);
        }

        /// <summary>
        /// Khai báo với AdMob bid cao nhất KHÔNG đến từ AdMob cho response vừa nhận (fill lẫn no-fill). Cùng
        /// nguồn hob với meta AdMob (<see cref="AdmobMediationHooks.ExternalBestRevenue"/>), &lt;= 0 gửi "0".
        /// </summary>
        private void RecordAuction(string adUnitId, string responseId)
        {
            if (string.IsNullOrEmpty(responseId)) return;
            try
            {
                var revenue = AdmobMediationHooks.ExternalBestRevenue(AdmobFormat);
                var hob = double.IsNaN(revenue) || double.IsInfinity(revenue) || revenue <= 0d
                    ? "0"
                    : AdmobBidAd.FormatUsd(revenue);
                AdmobMultiPlatformUtils.RecordAuctionEvent(PublisherOf(adUnitId), responseId, hob);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>Lấy response các ad trong kho chưa khai báo thua (load trước <paramref name="loadedBefore"/>), đánh dấu luôn.</summary>
        private void ClaimLostResponseIds(List<string> into, DateTime? loadedBefore, out string publisherId)
        {
            publisherId = null;
            foreach (var e in _pool)
            {
                if (e.lossRecorded || string.IsNullOrEmpty(e.responseId)) continue;
                if (loadedBefore.HasValue && e.loadedAtUtc > loadedBefore.Value) continue;
                e.lossRecorded = true;
                into.Add(e.responseId);
                publisherId ??= PublisherOf(e.admobAdUnitId);
            }
        }

        /// <summary>
        /// Ad AdMob của DFPool THẮNG lượt show -> khai báo impression: response của ad vừa chiếu (đứng đầu) + response
        /// các ad AdMob khác trong kho đã thua (load trước lúc show). Như <c>AdmobBidAd.RecordImpression</c>.
        /// </summary>
        private void RecordWinImpression(DFPoolSlot slot, long valueMicros, int precision)
        {
            try
            {
                if (string.IsNullOrEmpty(slot.showResponseId)) return;
                var ids = new List<string> { slot.showResponseId };
                ClaimLostResponseIds(ids, slot.showStartedUtc, out _);
                var value = (valueMicros / 1_000_000m).ToString(CultureInfo.InvariantCulture);
                DFPoolMediation.Log(DFPoolStage.PAID, $"{Tag} AdMob thắng -> khai báo impression {ids.Count} response " +
                                                      $"(1 thắng + {ids.Count - 1} thua) piv={value}");
                AdmobMultiPlatformUtils.RecordImpressionEvent(PublisherOf(slot.showAdUnitId), ids, value,
                    precision.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// MAX THẮNG lượt show (ad MAX của mediation chính hoặc network khác fill slot) -> khai báo impression cho
        /// mọi ad AdMob trong kho chưa khai báo thua, piv = giá bên thắng. Như <c>AdmobFullscreenAds.OnExternalImpression</c>.
        /// </summary>
        public void OnExternalImpression(double revenueUsd, string maxPrecision)
        {
            try
            {
                if (double.IsNaN(revenueUsd) || double.IsInfinity(revenueUsd) || revenueUsd < 0d) return;
                var ids = new List<string>();
                ClaimLostResponseIds(ids, null, out var publisherId);
                if (ids.Count == 0) return;
                var pip = maxPrecision switch
                {
                    "exact" => 3,
                    "publisher_defined" => 2,
                    "estimated" => 1,
                    _ => 0
                };
                var value = AdmobBidAd.FormatUsd(revenueUsd);
                DFPoolMediation.Log(DFPoolStage.PAID, $"{Tag} MAX thắng -> khai báo impression cho {ids.Count} response " +
                                                      $"AdMob thua piv={value}");
                AdmobMultiPlatformUtils.RecordImpressionEvent(publisherId, ids, value,
                    pip.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        #endregion

        private static void NotifyPaid(DFPoolSlot slot, string mediation, string network, double revenue)
        {
            var onPaid = slot.showOnPaid;
            slot.showOnPaid = null;
            if (onPaid == null) return;
            try
            {
                onPaid(mediation, network, revenue);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private void FinishShow(DFPoolSlot slot)
        {
            if (slot.showHandle >= 0) DFPoolNative.DestroyAd(slot.showHandle);
            slot.ClearShow();
            ToIdle(slot);
            DFPoolMediation.EndShow();
            RequestPump();
        }

        #endregion

        #region show (ISecondaryAdSource)

        private DFPoolSlot BestReadySlot()
        {
            DFPoolSlot best = null;
            foreach (var s in _slots)
            {
                if (s.phase != DFSlotPhase.Ready && s.phase != DFSlotPhase.ReadyForeign) continue;
                if (s.phase == DFSlotPhase.Ready && (s.entry == null || s.entry.SecondsLeft <= 0d)) continue;
                if (!IsMaxReady(s.id)) continue;
                if (best == null || s.Value > best.Value) best = s;
            }

            return best;
        }

        public bool IsReady() => BestReadySlot() != null;

        public double BestRevenue() => BestReadySlot()?.Value ?? SecondaryAdSource.NO_AD;

        public string BestAdUnitId()
        {
            var s = BestReadySlot();
            if (s == null) return null;
            return s.phase == DFSlotPhase.Ready ? s.entry.admobAdUnitId : s.id;
        }

        public string BestNetwork()
        {
            var s = BestReadySlot();
            if (s == null) return null;
            return s.phase == DFSlotPhase.Ready
                ? string.IsNullOrEmpty(s.entry.adSource) ? "AdMob" : s.entry.adSource
                : s.foreignNetwork;
        }

        public bool Show(string where, Action onReward, Action onHidden, Action onFail, SecondaryAdPaid onPaid)
        {
            var slot = BestReadySlot();
            if (slot == null)
            {
                DFPoolMediation.Log(DFPoolStage.SHOW_FAIL, $"{Tag} được gọi show nhưng không slot nào ready");
                return false;
            }

            var candidates = new StringBuilder();
            foreach (var s in _slots)
                candidates.Append(' ').Append(s.id).Append('=')
                    .Append(s.phase == DFSlotPhase.Ready || s.phase == DFSlotPhase.ReadyForeign
                        ? Money(s.Value)
                        : s.phase.ToString());
            DFPoolMediation.Log(DFPoolStage.SHOW, $"{Tag} chọn slot đắt nhất:{candidates}");

            slot.ClearShow();
            slot.showIsForeign = slot.phase == DFSlotPhase.ReadyForeign;
            slot.showValue = slot.Value;
            slot.showWhere = where;
            slot.showOnReward = onReward;
            slot.showOnHidden = onHidden;
            slot.showOnFail = onFail;
            slot.showOnPaid = onPaid;

            if (slot.showIsForeign)
            {
                slot.showAdUnitId = slot.id;
                slot.showNetwork = slot.foreignNetwork;
            }
            else
            {
                //ad rời kho ngay lúc show — native lấy nó ra ở adapter.show
                var entry = slot.entry;
                slot.showHandle = entry.handle;
                slot.showAdUnitId = entry.admobAdUnitId;
                slot.showNetwork = string.IsNullOrEmpty(entry.adSource) ? "AdMob" : entry.adSource;
                //như meta AdMob (price.floorUsd × 1000): ad mặc định = sàn 0, không phải trống
                slot.showFloorEcpm = entry.floorEcpmUsd;
                slot.showResponseId = entry.responseId;
                entry.lossRecorded = true; //ad đang chiếu: khai báo ở nhánh "thắng", không bao giờ là "thua"
                entry.borrowedBy = null;
                slot.entry = null;
                _pool.Remove(entry);
            }

            slot.showStartedUtc = DateTime.UtcNow;
            slot.phase = DFSlotPhase.Showing;
            DFPoolMediation.BeginShow();
            DFPoolMediation.Log(DFPoolStage.SHOW, $"{Tag} SHOW slot {slot.id} {Money(slot.showValue)} " +
                                $"({(slot.showIsForeign ? slot.showNetwork : "AdMob #" + slot.showHandle)}) @ {where}");
            AdShowReporter.Shown(type, where,
                slot.showIsForeign ? AdShowReporter.MEDIATION_MAX : AdShowReporter.MEDIATION_ADMOB, slot.showAdUnitId);
            //af_*_show KHÔNG bắn ở đây: tầng phiên (FalconMaxService) đã bắn cho mọi lượt show, như meta AdMob
            ShowMax(slot.id, where);
            return true;
        }

        public string DescribeCache() => DescribeCache(withTtl: true);

        private string DescribeCache(bool withTtl)
        {
            var sb = new StringBuilder();
            foreach (var s in _slots)
            {
                sb.Append("    slot ").Append(s.id).Append(' ').Append(s.phase);
                if (s.phase == DFSlotPhase.Ready && s.entry != null)
                    sb.Append(" #").Append(s.entry.handle).Append(' ').Append(Money(s.entry.valueUsd));
                else if (s.phase == DFSlotPhase.ReadyForeign)
                    sb.Append(' ').Append(s.foreignNetwork).Append(' ').Append(Money(s.foreignRevenue));
                sb.Append('\n');
            }

            var sorted = new List<DFPoolEntry>(_pool);
            sorted.Sort((a, b) => b.valueUsd.CompareTo(a.valueUsd));
            sb.Append("    kho ").Append(sorted.Count).Append('/').Append(_root.poolCap).Append(':');
            foreach (var e in sorted)
            {
                sb.Append(" #").Append(e.handle).Append(' ').Append(Money(e.valueUsd));
                if (e.isDefault) sb.Append("[mặc-định]");
                if (e.borrowedBy != null) sb.Append("@").Append(e.borrowedBy.id);
                if (withTtl) sb.Append(" (").Append(Math.Max(0, (int)e.SecondsLeft / 60)).Append("m)");
            }

            if (_pending.Count > 0) sb.Append(" | đang xin ").Append(_pending.Count);
            return sb.ToString();
        }

        #endregion

        #region MAX helpers

        /// <summary>
        /// Ad MAX vừa load có phải ad waterfall DFPoolNetwork (của ta) không. Chỉ ad này mới được reset
        /// bằng show giả; ad AppLovin / network khác thì giữ tới lúc show thật.
        /// </summary>
        private bool IsWaterfallAd(MaxSdkBase.AdInfo adInfo)
        {
            var network = adInfo?.NetworkName;
            if (string.IsNullOrEmpty(network)) return false;
            if (network.IndexOf("applovin", StringComparison.OrdinalIgnoreCase) >= 0) return false;

            var expected = _root.customNetworkName;
            return string.IsNullOrWhiteSpace(expected) ||
                   string.Equals(network.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private bool IsMaxReady(string id) => type == AdType.Reward
            ? MaxSdk.IsRewardedAdReady(id)
            : MaxSdk.IsInterstitialReady(id);

        private void LoadMax(string id)
        {
            if (type == AdType.Reward) MaxSdk.LoadRewardedAd(id);
            else MaxSdk.LoadInterstitial(id);
        }

        private void ShowMax(string id, string placement)
        {
            if (type == AdType.Reward) MaxSdk.ShowRewardedAd(id, placement);
            else MaxSdk.ShowInterstitial(id, placement);
        }

        private static string Money(double usd)
            => "$" + (usd * 1000d).ToString("0.###", CultureInfo.InvariantCulture) + " eCPM";

        #endregion
    }
}
#endif
