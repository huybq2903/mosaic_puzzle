/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2025-06-12
 */

using Falcon.Helpers.Devkit;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    public class AdLogService : MySingleton<AdLogService>, ILogDecorator<FAdLog>
    {
        private readonly IFPlayerAdRepository _adRepository;

        // Inject CACHE chứ không phải AdRequestService: decorator chỉ cần vai giữ-state, mà
        // service thì ôm LogScheduleService (vai bắn log) — inject cả cục là vòng chết DI
        // (án 12/08: Schedule→Decor→đây→AdRequestService→Schedule). Cache zero đường tới
        // LogScheduleService nên ctor-inject hợp lệ — luật ở ILogDecorator, test cấu trúc canh.
        private readonly AdViewCache _adViewCache;

        // Sổ theo ad unit: cùng lý do inject như cache view ở trên — vai giữ-state, zero đường tới
        // LogScheduleService.
        private readonly AdUnitFactsCache _unitFacts;
        private readonly AdSessionCounterCache _sessionCounters;

        public AdLogService(IFPlayerAdRepository adRepository, AdViewCache adViewCache,
            AdUnitFactsCache unitFacts, AdSessionCounterCache sessionCounters)
        {
            _adRepository = adRepository;
            _adViewCache = adViewCache;
            _unitFacts = unitFacts;
            _sessionCounters = sessionCounters;
        }

        public void Decor(FAdLog adLog)
        {
            var logParams = adLog.param;
            var (typeWatched, adLtv) = _adRepository.NewAdWatched(logParams.type, logParams.adRev);
            adLog.typeCount = typeWatched;
            adLog.adLtv = adLtv;

            // Bản tin gộp nhiều impression: mọi thứ dưới đây là của MỘT lần xem, mà "lần xem hiện
            // tại" lúc gửi chỉ là cái cuối cùng trong cụm — dán lên cả cụm là bịa cho N-1 cái còn
            // lại. Doanh thu ở trên vẫn cộng bình thường vì nó cộng được.
            if (adLog.IsSpanRecord) return;

            // Hiển thị thứ mấy của format này TRONG PHIÊN — khác typeCount ở trên (sổ theo ĐỜI).
            // Nằm dưới early-return nên dòng gộp cụm banner KHÔNG có: một dòng ở đó gộp N
            // impression, "thứ mấy" không có nghĩa (đọc impressionCount).
            adLog.sessionAdImpressionIndex ??= _sessionCounters.NextImpressionIndex(logParams.type);

            // Sổ eCPM theo phiên (1.5.7): nguồn của firstAdEcpm/previousAdEcpm trên dòng LOAD sau đó.
            // ⚠ Ghi ở DECORATOR chứ không ở AdRequestService.LogImpression, vì không phải impression
            // nào cũng đi qua cửa đó: nhánh IronSource và GMA trong module Mediation tự dựng
            // `ExtendAdLog` (: FAdLog) rồi Send() — decorator thì CHẠY cho cả hai đường (decorate-once
            // lúc vào hàng chờ), nên đặt ở đây mới không có đường nào lọt.
            // CHỈ format TOÀN MÀN: họ dính-màn tự refresh liên tục nên sẽ luôn là "lượt trước", đẩy
            // chính quảng cáo toàn màn ra khỏi câu hỏi mà hai cột này sinh ra để trả lời.
            if (!AdViewState.StaysOnScreen(logParams.type))
                _sessionCounters.RecordImpressionRevenue(logParams.adRev);

            // Họ DÍNH MÀN (native/MREC/collapsible) nằm trong UI game nên thường hiện NGAY SAU một
            // quảng cáo toàn màn (popup kết quả sau rewarded chẳng hạn) — đóng dấu "vừa xem loại
            // nào, cách đây mấy giây" để đo được *"native sau loại ad nào thì ăn hơn"*. Game truyền
            // tay thì tay thắng. Dòng gộp cụm không bao giờ tới đây (đã return ở trên) — đúng §4i.
            if (AdViewState.StaysOnScreen(logParams.type))
            {
                var (prevFormat, prevSec) = _adViewCache.LastFullScreenAd();
                logParams.prevFullScreenAdFormat ??= prevFormat;
                logParams.prevFullScreenAdSec ??= prevSec;
            }

            // Nối vào phễu fill: đóng request đang chờ của cùng format (§C). Mốc TIÊU THỤ —
            // impression thứ hai trên cùng view là SDK tự xoay id mới thay vì im lặng dùng lại
            // (án dup 5% loader 03/09: một adViewId bị tính tiền N lần vì mediation quên
            // OnRequested đợt mới; chốt owner: khuôn play_turn_id, tự sinh chứ không để trống).
            var (adViewId, fillLatencyMs, rotated) = _adViewCache.StampImpression(logParams.type);
            adLog.adViewId ??= adViewId;
            adLog.fillLatencyMs ??= fillLatencyMs;
            if (rotated)
                AnalyticLogger.Instance.Warning(
                    $"OnImpression({logParams.type}): view hiện tại đã có impression rồi — SDK xoay " +
                    "adViewId mới để không trùng mã. Đây là dấu hiệu mediation thiếu OnRequested " +
                    "cho đợt load mới (mỗi đợt xin ad là một request).");

            // Chỗ chiếu / ngữ cảnh / mediation: cùng luật hai chiều với mốc show và close
            // (mốc tự nhập thì thắng và cập nhật cache; bỏ trống thì lấy từ cache) — luật nằm MỘT
            // chỗ trong AdViewState.ApplyContext, đây chỉ là mốc thứ ba gọi vào nó.
            _adViewCache.ApplyContext(logParams, logParams.type);

            // Sự thật của RIÊNG UNIT đang chiếu (network thắng, bậc, giá sàn, eCPM): mediation chỉ
            // nói chúng ở mốc load, nên lấy từ sổ theo unit — khoá là adUnitId của chính dòng này.
            // Không có adUnitId thì KHÔNG đoán: một đợt có nhiều unit load song song, lấy unit của
            // lần load cuối là dán sự thật của unit khác (đúng ca monetize mô tả 26/10).
            _unitFacts.ApplyUnitFacts(logParams);

            // Ghi unit đã chiếu vào view để mốc CLOSE của cùng view chở được nó
            _adViewCache.SetImpressionUnit(logParams.type, logParams.adUnitId);
        }
    }
}
