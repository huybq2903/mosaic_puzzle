/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-07-31
 */

using System;
using Falcon.Helpers.Devkit;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Bắn log ba mốc vòng đời ad view (request/show/close) — §B + §C. State nằm ở
    /// <see cref="AdViewCache"/> (tách riêng sau án deadlock 12/08 — xem doc bên đó); class này
    /// chỉ còn vai DỰNG BẢN TIN tại các khoảnh khắc, nên nó là bên duy nhất của entity ad ôm
    /// <see cref="LogScheduleService"/>.
    /// </summary>
    public class AdRequestService : MySingleton<AdRequestService>
    {
        private readonly AdViewCache _cache;
        private readonly LogScheduleService _logScheduleService;
        private readonly BannerLogService _bannerLogService;
        private readonly AdLoadStatsService _loadStatsService;
        private readonly IAdLoadLogConfig _loadLogConfig;
        private readonly AdUnitFactsCache _unitFacts;
        private readonly AdSessionCounterCache _sessionCounters;
        private readonly FullscreenAdPresence _fullscreenPresence;

        private readonly TurnLedgerService _turnLedger;

        public AdRequestService(
            AdViewCache cache, LogScheduleService logScheduleService, BannerLogService bannerLogService,
            AdLoadStatsService loadStatsService, IAdLoadLogConfig loadLogConfig, AdUnitFactsCache unitFacts,
            AdSessionCounterCache sessionCounters, FullscreenAdPresence fullscreenPresence,
            TurnLedgerService turnLedger)
        {
            _turnLedger = turnLedger;
            _cache = cache;
            _logScheduleService = logScheduleService;
            _bannerLogService = bannerLogService;
            _loadStatsService = loadStatsService;
            _loadLogConfig = loadLogConfig;
            _unitFacts = unitFacts;
            _sessionCounters = sessionCounters;
            _fullscreenPresence = fullscreenPresence;
        }

        /// <summary>
        /// Mediation gọi load một ad → mở vòng đời + log mốc request. Service CHỈ nhận param —
        /// nén knob phẳng thành param là việc của <see cref="FAdApi"/> (một đường xử lý ở đây).
        /// </summary>
        public void RequestAd(AdViewParam param)
        {
            if (param == null) return;
            // networkType là SỐ ĐO của SDK, không phải cái game khai — stamp đè bất kể param
            param.networkType = NetworkTypeExtensions.Current();
            var adViewId = _cache.Open(param);
            _logScheduleService.Enqueue(new FAdRequestLog(param)
            {
                adViewId = adViewId,
                sessionAdRequestIndex = _sessionCounters.NextRequestIndex(param.type)
            });
        }

        /// <summary>
        /// Game muốn chiếu ad — bắn TRƯỚC nhánh kiểm tra kho. Id view chỉ đóng khi có ad sẵn:
        /// kho rỗng mà vẫn đóng id của đợt đang load là nói dối "sắp chiếu cái này".
        /// </summary>
        public void ShowAttempt(AdType type, string adWhere, bool adAvailable, string adWhen, string adMediation,
            string triggerPlayTurnId = null)
        {
            // Hai field B12 không truyền thì lấy từ ngữ cảnh game đã khai qua UpdateContext (đường của game trước
            // khi Mediation có tham số) — "SDK đính lên mọi log của lần chiếu đó" (hợp đồng đợt 3 B12).
            var context = _cache.TakeSnapshot(type);
            adWhen = FirstNonEmpty(adWhen, context?.adWhen);
            triggerPlayTurnId = FirstNonEmpty(triggerPlayTurnId, context?.triggerPlayTurnId);
            var warning = FAdWhen.WarningAtShow(adWhen, $"OnShowAttempt({type}, '{adWhere}')");
            if (warning != null) AnalyticLogger.Instance.Warning(warning);

            _logScheduleService.Enqueue(new FAdShowAttemptLog
            {
                type = type,
                adWhere = adWhere,
                adWhen = adWhen,
                triggerPlayTurnId = triggerPlayTurnId,
                adMediation = adMediation,
                adAvailable = adAvailable,
                adViewId = adAvailable ? _cache.CurrentViewId(type) : null
            });
        }

        /// <summary>
        /// Luật của game chặn một cơ hội chiếu ad (ký loader 12/11). Format không phải game tự bật,
        /// hoặc thiếu điểm chiếu ⇒ cảnh báo và KHÔNG gửi (xem <see cref="FAdShowSkippedLog.RejectReason"/>).
        /// </summary>
        public void ShowSkipped(AdType type, string adWhere, AdSkipReason skipReason, string adWhen,
            bool? adAvailable = null, AdReadyInfo readyAd = null, string triggerPlayTurnId = null)
        {
            var reject = FAdShowSkippedLog.RejectReason(type, adWhere);
            if (reject != null)
            {
                AnalyticLogger.Instance.Warning($"OnShowSkipped bỏ qua: {reject}.");
                return;
            }

            if (skipReason == AdSkipReason.Other)
                AnalyticLogger.Instance.Warning(
                    $"OnShowSkipped({type}, '{adWhere}') dùng lý do Other — vẫn gửi, nhưng kho không tách " +
                    "được luật này. Luật hay dùng thì xin thêm giá trị vào AdSkipReason.");

            if (adAvailable == null)
                AnalyticLogger.Instance.Warning(
                    $"OnShowSkipped({type}, '{adWhere}') thiếu adAvailable — vẫn gửi, nhưng kho không biết lúc " +
                    "đó có ad đang chờ không. Truyền IsInterstitialReady() / ad đang chờ của mediation.");

            var context = _cache.TakeSnapshot(type);
            adWhen = FirstNonEmpty(adWhen, context?.adWhen);
            var whenWarning = FAdWhen.WarningIfOutsideList(adWhen, $"OnShowSkipped({type}, '{adWhere}')");
            if (whenWarning != null) AnalyticLogger.Instance.Warning(whenWarning);

            var log = new FAdShowSkippedLog
            {
                type = type,
                adWhere = adWhere,
                adWhen = adWhen,
                triggerPlayTurnId = FirstNonEmpty(triggerPlayTurnId, context?.triggerPlayTurnId),
                skipReason = skipReason
            };
            if (log.ApplyReadyAd(adAvailable, readyAd, _cache.CurrentViewId(type),
                    _cache.TakeSnapshot(type)?.adMediation))
                AnalyticLogger.Instance.Warning(
                    $"OnShowSkipped({type}, '{adWhere}') có readyAd nhưng adAvailable != true — SDK bỏ meta ad " +
                    "(không có ad thì không có gì để mô tả).");
            _logScheduleService.Enqueue(log);
        }

        /// <summary>Ad toàn màn hiển thị hỏng — pause sau đó không còn là do ad.</summary>
        public void ShowFailed(AdType type)
        {
            _fullscreenPresence.MarkEnded(type);
            if (FullscreenAdPresenceState.IsFullscreen(type)) _turnLedger.OnFullscreenAdEnded();
        }

        /// <summary>Mediation gọi hiển thị ad → log mốc show (kèm thời gian chờ từ lúc request).</summary>
        public void ShowAd(AdShowParam param)
        {
            if (param == null) return;
            // Mốc khởi phát cho câu "pause sắp tới có phải do ad không" — đúng khi mediation gọi OnShown
            // đồng bộ quanh lệnh show, TRƯỚC khi app pause; đường nào phủ, đường nào chưa: xem
            // FullscreenAdPresenceState.
            _fullscreenPresence.MarkShown(param.type);
            var shown = _cache.MarkShown(param);
            // Sau MarkShown: adWhen / triggerPlayTurnId đã được nối với ngữ cảnh game khai (UpdateContext)
            if (FullscreenAdPresenceState.IsFullscreen(param.type))
                _turnLedger.OnFullscreenAdShown(param.adWhen, param.triggerPlayTurnId);
            if (shown.rotated)
                AnalyticLogger.Instance.Warning(
                    $"OnShown({param.type}) trên view đã có impression — SDK xoay adViewId mới cho " +
                    "lần hiển thị này. Dấu hiệu mediation thiếu OnRequested cho đợt load mới.");
            _logScheduleService.Enqueue(new FAdShowLog(param)
            {
                adViewId = shown.adViewId,
                requestToShowMs = shown.requestToShowMs
            });
        }

        /// <summary>
        /// Impression/paid event → log doanh thu. Banner đi vào cụm gộp (per-impression là đúng
        /// cái hố volume mà cụm sinh ra để lấp); format khác enqueue <see cref="FAdLog"/> —
        /// decorator tự đóng adViewId/fillLatency/context/LTV như đường của module Mediation.
        /// </summary>
        public void LogImpression(AdParam param)
        {
            if (param == null) return;

            if (param.type == AdType.Banner)
            {
                _bannerLogService.Log(param.adWhere, param.adPrecision, param.adCountry, param.adRev,
                    param.adNetwork, param.adMediation, param.currentLevel ?? 0, param.adUnitId,
                    param.adAlgorithm);
                return;
            }

            _logScheduleService.Enqueue(new FAdLog(param));
        }

        /// <summary>
        /// Kết quả một lần gọi load của một unit — đi MỘT trong hai đường, theo
        /// <see cref="IAdLoadLogConfig"/>:
        /// <list type="bullet">
        /// <item><b>tách</b> (mặc định): enqueue thẳng một <see cref="FAdLoadLog"/> mang
        /// <c>adViewId</c> của đợt xin ad đang mở → nối được load ↔ show.</item>
        /// <item><b>gộp</b>: vào kho gộp cụm như trước (per-attempt là volume không chặn trên —
        /// đúng vụ <c>mo_multiple_floor</c> bị chặn server).</item>
        /// </list>
        /// Hai đường loại trừ nhau nên không đếm đôi. Đường tách CHỈ ĐỌC cache ad view
        /// (<see cref="AdViewCache.PendingViewId"/>), không mở/đóng/tiêu thụ view nào — đây vẫn
        /// không phải mốc của vòng đời một lần xem.
        /// </summary>
        public void LogLoadResult(AdLoadResultParam param)
        {
            // Bẫy đổi tên 23/09: trường cũ tên `floor` mang HỆ SỐ, giờ `floor` là GIÁ THẬT (USD) còn
            // hệ số dời sang `tier`. Code mediation cũ (`floor = a.multiplier`) VẪN COMPILE nên sai
            // im lặng — cảnh báo tại chỗ là thứ duy nhất bắt được.
            if (param is { floor: > 0, tier: null })
                AnalyticLogger.Instance.Warning(
                    $"OnLoadResult({param.adType}/{param.adUnitId}): có `floor` = {param.floor} (GIÁ SÀN " +
                    "THẬT, USD) nhưng không có `tier`. Nếu đang truyền hệ số bậc trong config thì đổi " +
                    "sang `tier` — `floor` chỉ dành cho mediation BIẾT giá sàn thật (vd AdMob).");

            // Mã nhát thử: CALLER THẮNG, SDK chỉ cấp khi thiếu. Mediation có bảng riêng của mình
            // (vd ad_uuid của f_sdk_ad_full_data) thì truyền mã đó vào đây là hai bảng join được;
            // mỗi bên tự cấp mã thì không. Cấp TRƯỚC khi ghi sổ unit và trước hook, để dòng load,
            // dòng impression và Databuckets cùng mang một mã.
            param.adCandidateId ??= Guid.NewGuid().ToString();

            // Sổ theo UNIT: mediation chỉ nói network thắng / bậc / giá sàn / eCPM ở mốc này, mà dòng
            // impression sau đó cần chúng. Ghi TRƯỚC khi dựng log để chính dòng load cũng dùng được.
            _unitFacts.Record(param);

            // Sổ đếm theo phiên chạy ở CẢ HAI chế độ: số thứ tự là sự thật của lần gọi này, không
            // phải của bản tin — chế độ gộp không chở được nó, nhưng cũng không được làm lệch sổ.
            var loadIndex = _sessionCounters.NextLoadIndex(param.adType);
            var successIndex = param.success ? _sessionCounters.NextLoadSuccessIndex(param.adType) : (int?)null;

            if (_loadLogConfig.GroupAdLoadLog)
            {
                _loadStatsService.Record(param);
            }
            else
            {
                // Hai số eCPM là của PHIÊN, không phải của attempt: đọc MỘT lần trong cùng một khoá
                // để hai cột trên cùng dòng log là ảnh của cùng một thời điểm. Chỉ đọc ở nhánh TÁCH —
                // bản gộp không chở được số theo phiên.
                var session = _sessionCounters.ImpressionSnapshot(param.adType);
                _logScheduleService.Enqueue(new FAdLoadLog(param)
                {
                    adViewId = _cache.PendingViewId(param.adType),
                    sessionAdLoadIndex = loadIndex,
                    sessionAdLoadSuccessIndex = successIndex,
                    // Thuật toán là thuộc tính của cả ĐỢT: mediation khai một lần qua UpdateContext
                    // là đủ. Trước 1.5.6 dòng load chỉ đọc param nên khai kiểu đó KHÔNG tới được
                    // đây — một cửa mở mà không có đường vào (phía đọc số bắt 26/10).
                    adAlgorithm = param.adAlgorithm ?? _cache.CurrentAlgorithm(param.adType),
                    firstAdEcpm = session.first,
                    previousAdEcpm = session.previous,
                    // SỐ ĐẾM, không phải index: 0 là sự thật ("chưa chiếu lượt nào"), nên gửi cả 0.
                    sessionAdShownBefore = session.shown
                });
            }

            FBigDataHooks.RaiseAdLoadResult(param);
        }

        /// <summary>
        /// Người chơi bấm vào ad → ghi cờ vào view hiện tại, log close của cùng view tự mang
        /// <c>hasClick</c>. KHÔNG bắn bản tin riêng (hiện tại) — xem doc ở <see cref="FAdApi.OnClicked"/>.
        /// </summary>
        public void ClickAd(AdType type)
        {
            if (_cache.MarkClicked(type)) return;

            AnalyticLogger.Instance.Warning(
                $"OnClicked({type}) bỏ qua: format này chưa đi qua OnRequested nên không có lần xem " +
                "ad nào để nhận cú click.");
        }

        /// <summary>Ad đóng lại → log mốc cuối của vòng đời (kèm thời lượng hiển thị).</summary>
        public void CloseAd(AdCloseParam param)
        {
            if (param == null) return;
            _fullscreenPresence.MarkEnded(param.type);
            if (FullscreenAdPresenceState.IsFullscreen(param.type)) _turnLedger.OnFullscreenAdEnded();

            // Unit của dòng close: mediation điền thì thắng; bỏ trống thì lấy unit đã ghi ở mốc
            // IMPRESSION của đúng view này. Đó là SỰ THẬT (chỉ một unit được chiếu, và nó vừa báo
            // impression), không phải "unit của lần load cuối" — lấy lần load cuối là bịa, vì một
            // đợt có nhiều unit load song song.
            param.adUnitId ??= _cache.ImpressionUnitId(param.type);

            var closed = _cache.MarkClosed(param);
            _logScheduleService.Enqueue(new FAdCloseLog(param)
            {
                adViewId = closed.adViewId,
                shownDurationSec = closed.shownDurationSec
            });
        }

        /// <summary>
        /// Ghi vị trí chiếu cho lần xem ad hiện tại của format này (preload xong mới biết chiếu ở
        /// đâu). Các mốc sau — show, impression, close — tự mang theo, khỏi nhập lại từng mốc.
        /// Chưa có request nào của format thì cảnh báo và bỏ qua.
        /// </summary>
        public void SetAdContext(AdType type, string adWhere = null, string adWhen = null,
            string adMediation = null, string adAlgorithm = null, string triggerPlayTurnId = null)
        {
            var whenWarning = FAdWhen.WarningIfOutsideList(adWhen, $"UpdateContext({type})");
            if (whenWarning != null) AnalyticLogger.Instance.Warning(whenWarning);
            if (_cache.SetContext(type, adWhere, adWhen, adMediation, adAlgorithm, triggerPlayTurnId)) return;

            AnalyticLogger.Instance.Warning(
                $"UpdateAdContext({type}) bỏ qua: format này chưa đi qua OnAdRequested nên chưa có " +
                "lần xem ad nào để ghi tham số.");
        }

        // ---- Đọc — giữ làm cửa cũ cho FAdApi/mediation; bản chất nằm ở AdViewCache ----

        /// <inheritdoc cref="AdViewCache.CurrentViewId"/>
        public string CurrentAdViewId(AdType type)
        {
            return _cache.CurrentViewId(type);
        }

        /// <inheritdoc cref="AdViewCache.TakeSnapshot"/>
        public AdViewSnapshot CurrentAdView(AdType type)
        {
            return _cache.TakeSnapshot(type);
        }

        /// <inheritdoc cref="AdViewCache.FillLatencyMs"/>
        public int? FillLatencyMs(AdType type)
        {
            return _cache.FillLatencyMs(type);
        }

        /// <inheritdoc cref="AdViewCache.ApplyContext"/>
        public void ApplyAdContext(AdViewParam param, AdType type)
        {
            _cache.ApplyContext(param, type);
        }

        private static string FirstNonEmpty(string value, string fallback)
            => string.IsNullOrEmpty(value) || value == FParam.UNKNOWN ? (string.IsNullOrEmpty(fallback) ? value : fallback) : value;
    }
}
