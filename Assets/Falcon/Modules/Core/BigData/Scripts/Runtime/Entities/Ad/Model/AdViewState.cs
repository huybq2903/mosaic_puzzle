/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-07-31
 */

using System;
using System.Collections.Generic;
using Falcon.Helpers.Devkit;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Ảnh chụp bộ tham số của lần xem ad hiện tại — bản SAO để game đọc; sửa vào đây không đụng
    /// tới state của SDK (SDK là nguồn chân lý duy nhất của vòng đời).
    /// </summary>
    [Serializable]
    public class AdViewSnapshot
    {
        public string adViewId;
        public AdType type;
        public string adWhere;
        public string adWhen;

        /// <summary>Lượt chơi kích hoạt lần chiếu — xem <see cref="AdViewParam.triggerPlayTurnId"/>.</summary>
        public string triggerPlayTurnId;
        public string adMediation;

        /// <summary>Thuật toán load của đợt — xem <see cref="AdViewParam.adAlgorithm"/>.</summary>
        public string adAlgorithm;

        /// <summary>Đã bao lâu kể từ lúc xin ad (ms); null nếu view tự sinh lúc xoay (không đi từ request nào).</summary>
        public int? sinceRequestMs;

        /// <summary>Đã bao lâu kể từ lúc ad lên hình (ms); null nếu chưa qua mốc show.</summary>
        public int? sinceShownMs;
    }

    /// <summary>
    /// Vòng đời MỘT LẦN XEM AD (xem EntityLifecycle-Design.md §4f): request → show → impression →
    /// close. Một <c>adViewId</c> duy nhất cho cả 4 mốc, sinh lúc request và xoay khi có request
    /// mới của cùng format — HOẶC khi show/impression chạm view đã có impression (mediation quên
    /// request đợt mới thì SDK tự xoay, khuôn play_turn_id — xem <see cref="StampImpression"/>;
    /// một id không bao giờ được tính tiền hai lần với non-banner). Request không bao giờ có
    /// impression mang id đó chính là view FILL-FAIL — không cần event fail riêng, sự vắng mặt
    /// đã là tín hiệu.
    /// <br/>Slot theo <see cref="AdType"/> vì mediation chỉ giữ MỘT ad object và MỘT load in-flight
    /// cho mỗi format (load → loaded/failed → show → closed → load lại); các format chạy song song.
    /// Nhờ vậy id tự xoay đúng lúc mà không cần ai gọi "đóng" tường minh.
    /// <br/>⚠ GIỚI HẠN ĐÃ XÁC NHẬN với loader (audit 12/08 mục 5): game chạy NHIỀU ad unit cùng
    /// format song song sẽ tráo id giữa hai unit (request B đè slot khi view A chưa xong → A thành
    /// fill-fail giả). Loader đã khai luật đọc "phễu fill per-view chỉ tin với game 1-unit/format";
    /// fix thật cần key theo adUnitId — chờ demand, vì mediation nội bộ hiện đúng 1 unit/format.
    /// <br/>Ngoài id, slot còn giữ bộ tham số bất biến của view (chỗ chiếu, ngữ cảnh, mediation) để
    /// các mốc sau khỏi nhập lại — cùng luật cache với entity level (§3).
    /// <br/>Mọi số đo thời lượng ở đây tính từ wall-clock CÓ CHỦ ĐÍCH (luật 3 của
    /// <c>MonotonicClock</c>): vòng đời một view VẮT QUA pause của chính game — trên Android, chiếu
    /// inter/rewarded tự nó là một lần pause — mà đồng hồ đơn điệu thì đóng băng khi máy ngủ, dùng
    /// nó là đếm thiếu. Nhiễu chỉnh-giờ chấp nhận được: số per-instance, clamp ≥ 0 sẵn, server
    /// đối chiếu được bằng hiệu timestamp hai event. ĐỪNG "sửa" sang monotonic.
    /// <br/>Pure class — không IO/DI/time, caller truyền mốc thời gian vào. KHÔNG tự lo
    /// thread-safety: <see cref="AdRequestService"/> khoá giúp, vì bên ghi (callback mediation) và
    /// bên đọc (pipeline decor lúc dựng log impression) không đảm bảo cùng thread.
    /// </summary>
    public class AdViewState
    {
        private class AdView
        {
            public string Id;

            /// <summary>0 = view TỰ SINH lúc xoay, không đi từ request nào — mọi số đo tính từ mốc request phải null thay vì bịa.</summary>
            public long RequestedAtMillis;
            public long ShownAtMillis;
            public string Where;
            public string When;
            public string TriggerTurnId;
            public string Mediation;
            public bool Clicked;

            /// <summary>View đã bị impression TIÊU THỤ — mốc show/impression kế tiếp chạm vào là dấu hiệu thiếu request mới, phải xoay id (án dup 5% loader báo 03/09).</summary>
            public bool ImpressionStamped;

            /// <summary>Thuật toán load của ĐỢT này — thuộc tính cả đợt, mọi unit trong đợt giống nhau.</summary>
            public string Algorithm;

            /// <summary>
            /// Ad unit ĐÃ CHIẾU của view này — ghi ở mốc impression (mediation truyền unit ở đó).
            /// Dùng cho mốc close: close thuộc đúng view đã chiếu nên đây là sự thật, không suy đoán.
            /// </summary>
            public string ImpressionUnitId;
        }

        /// <summary>Slot theo format — view MỚI NHẤT đã xin (tức ad sắp chiếu / vừa preload).</summary>
        private readonly Dictionary<AdType, AdView> _viewByType = new();

        /// <summary>
        ///     View ĐANG CHIẾU — đã qua mốc show, chưa qua mốc close.
        ///     <br/>Vì sao phải tách khỏi slot (sửa 16/10, án "ad_close mồ côi" của loader): mediation
        ///     PRELOAD ad kế tiếp ngay sau khi ad hiện tại lên hình, nên lúc user bấm đóng thì slot
        ///     đã là view MỚI — close bị đóng dấu id của một request chưa bao giờ hiện. Loader đo
        ///     được: 99,9% close mồ côi mang id như vậy, khoảng cách p50 = 52s (reward) / 33s
        ///     (inter) đúng bằng thời gian xem; tỉ lệ 46% reward, 14% inter.
        ///     <br/>Họ Ở LẠI MÀN HÌNH (banner / MREC / collapsible / native — xem
        ///     <see cref="StaysOnScreen"/>) KHÔNG dùng sổ này: id của họ là id INSTANCE theo hợp
        ///     đồng, và họ không có mốc close.
        /// </summary>
        private readonly Dictionary<AdType, AdView> _onScreenByType = new();

        private AdType? _lastFullScreenType;
        private long _lastFullScreenClosedAtMillis;

        /// <summary>
        ///     Quảng cáo TOÀN MÀN vừa đóng trong phiên này, và bao lâu kể từ lúc đóng (giây) — để
        ///     dòng của họ dính-màn trả lời được *"native này diễn sau loại ad nào thì ăn hơn"*
        ///     (yêu cầu owner 19/10).
        ///     <br/>CỐ Ý không persist qua phiên: nghĩa của nó là "trong phiên này", nói được rành
        ///     rọt — persist thì sinh ra những con số vài ngày mà không ai biết phải cắt ở đâu.
        ///     <br/>CỐ Ý không cắt ngưỡng ở client: gửi cả hai field, phía đọc tự chọn "trong vòng
        ///     mấy giây mới tính là liền sau". Ngưỡng là câu hỏi phân tích, không phải hằng số SDK.
        ///     <br/>Ghi ở mốc ĐÓNG, nên ca mediation quên bắn close thì field vắng mặt — đúng hơn là
        ///     đoán theo mốc show.
        /// </summary>
        public (AdType? format, int? secAgo) LastFullScreenAd(long nowMillis)
        {
            if (!_lastFullScreenType.HasValue) return (null, null);

            return (_lastFullScreenType,
                (int)Math.Max(0, (nowMillis - _lastFullScreenClosedAtMillis) / 1000));
        }

        /// <summary>
        ///     Format Ở LẠI trên màn hình: banner, MREC, collapsible banner, native. Điểm chung
        ///     DUY NHẤT mà luật này đứng trên — <b>không có mốc <c>close</c></b>: chúng nằm trong UI
        ///     của game, biến mất cùng cái UI đó, chẳng mediation nào bắn callback "đã đóng". Kéo
        ///     theo hai luật: không vào sổ "đang chiếu", và <c>adViewId</c> là id của INSTANCE nên
        ///     không xoay id khi impression lặp (banner tự refresh thì N impression chung một id,
        ///     bản tin cụm mang <c>impressionCount</c>).
        ///     <br/>Họ còn lại (interstitial, reward, app open) chiếm cả màn rồi đóng: một id một
        ///     lần chiếu, có close, và cần sổ "đang chiếu" để close không bị ad vừa preload tráo id.
        ///     <br/>Vì sao là VỊ TỪ chứ không phải <c>== AdType.Banner</c> (sửa 19/10): ba format
        ///     kia cũng không có close, nên so sánh bằng là xếp chúng vào họ fullscreen → mốc show
        ///     ghi chúng vào sổ "đang chiếu" rồi <b>kẹt ở đó</b> chờ một close không bao giờ đến, và
        ///     mọi mốc sau của format đó (impression/click) bị đóng dấu id của view kẹt.
        ///     <br/>⚠ Mức độ: đây là lỗi <b>chờ sẵn</b>, không phải lỗi đang làm sai dữ liệu — đội
        ///     Mediation xác nhận 19/10 là <b>chưa game nào dùng MREC</b>, và collapsible banner của
        ///     AdMob <b>không tự refresh</b> (một creative cố định). Sửa trước vì nó rẻ và vì ngày
        ///     có game bật thì không ai đi dò lại nhánh này.
        ///     <br/>⚠ GIỚI HẠN còn nguyên: sổ khoá theo <see cref="AdType"/> nên N view cùng họ chạy
        ///     SONG SONG (native nhiều ô trên một màn, hay hai ad unit cùng format) dùng chung một
        ///     id — đúng giới hạn loader đã khai ở audit 12/08. Tách được chỉ khi mediation truyền
        ///     <c>adUnitId</c> ngay mốc request, mà <see cref="AdViewParam"/> hiện không có field đó.
        /// </summary>
        public static bool StaysOnScreen(AdType type)
        {
            return type is AdType.Banner or AdType.MREC or AdType.CollapsibleBanner or AdType.Native;
        }

        /// <summary>
        ///     Format có thể có NHIỀU cái trên màn cùng lúc — hiện chỉ <see cref="AdType.Native"/>,
        ///     và đó là lý do người ta dùng native (nhiều ô trong UI: popup kết quả, danh sách shop).
        ///     <br/>Với format như vậy, SDK <b>không phát <c>adViewId</c></b>: mọi mốc chỉ mang
        ///     <see cref="AdType"/> nên không có cách nào biết mốc này thuộc ô nào, mà dán id của ô
        ///     mới nhất lên là một lời khẳng định không có bằng chứng — đúng thứ loader đã gạt ở án
        ///     "ad_close mồ côi" (họ từ chối vá bằng cách ghép theo thời gian). Vắng mặt = KHÔNG BIẾT.
        ///     <br/>Kéo theo: <c>fillLatencyMs</c> cũng vắng (không biết thuộc ô nào thì cũng không
        ///     biết nó đi từ lần xin nào), dòng request không mang id (nếu mang thì nó thành
        ///     "request không bao giờ có impression" = <b>fill-fail giả</b> cho mọi native), và
        ///     chỗ chiếu không được lấy từ cache (cache chỉ biết ô mới nhất).
        ///     <br/>Đổi lại thì mất: phễu fill và mối nối xin→hiện của native. Chấp nhận — hai số đó
        ///     gần như không ai hỏi, còn id sai là số có người tin.
        ///     <br/>Ngày mediation truyền được <c>adUnitId</c> ở mốc request thì khoá sổ theo
        ///     <c>(format, adUnitId)</c>, lúc đó native có id THẬT thay cho vắng mặt.
        /// </summary>
        public static bool CanHaveManyOnScreen(AdType type)
        {
            // ⚠ ĐIỀU KIỆN THÊM FORMAT VÀO ĐÂY: ngày nào một màn có HAI cái cùng format (hai ô MREC
            // trên một popup chẳng hạn) thì thêm format đó vào danh sách này — một dòng. Với MREC
            // và collapsible, giả định "một cái một màn" hiện an toàn, nên chúng vẫn có id.
            return type is AdType.Native;
        }

        /// <summary>
        ///     View của LẦN CHIẾU ĐANG DIỄN RA: ưu tiên view đang trên màn hình, không có thì mới
        ///     lấy slot. Mọi mốc nói về "lần xem hiện tại" (impression, click, close, đọc id) đi
        ///     qua đây — chỉ <see cref="Open"/> và <see cref="SetContext"/> làm việc với slot.
        /// </summary>
        private AdView Current(AdType type)
        {
            if (!StaysOnScreen(type) && _onScreenByType.TryGetValue(type, out var onScreen)) return onScreen;
            return _viewByType.GetValueOrDefault(type);
        }

        /// <summary>
        /// Mở vòng đời một lần xem ad: sinh <see cref="AAdLog.adViewId"/> và giữ vào slot
        /// của format. View cũ cùng format (nếu có) coi như kết thúc — nó hoặc đã xem xong rồi
        /// (mediation load lại ngay sau khi đóng ad), hoặc chưa từng lên hình (fill-fail).
        /// </summary>
        /// <returns>adViewId vừa sinh — caller gắn lên log (id là của SDK nên không nằm trong param).</returns>
        public string Open(AdViewParam param, long nowMillis)
        {
            // Format nhiều-ô-một-lúc không có sổ: xem CanHaveManyOnScreen
            if (CanHaveManyOnScreen(param.type)) return null;

            var id = Guid.NewGuid().ToString();

            // Hai họ field, hai cách đối xử — đợt mới KHÔNG được xoá sạch mọi thứ:
            //  · Where/When là của RIÊNG một lần chiếu (preload chưa biết chiếu ở đâu; đợt sau có
            //    thể chiếu chỗ khác) ⇒ reset, mang sang là "dán ngữ cảnh đoán".
            //  · Mediation/Algorithm là của TẦNG HẠ TẦNG đang chạy, không đổi giữa hai đợt ⇒ giữ
            //    lại khi đợt mới không khai. Đây cũng đúng luật mà ApplyContext vốn đã áp cho
            //    Mediation ("param bỏ trống thì lấy cache") — Open là chỗ duy nhất trước đây ném
            //    cache đi, nên khai một lần ở init của mediation là MẤT từ đợt thứ hai (án 26/10:
            //    phiếu Mediation hướng dẫn khai ở chỗ mở van, mà người đọc hiểu là khai đầu phiên).
            var previous = _viewByType.GetValueOrDefault(param.type);
            _viewByType[param.type] = new AdView
            {
                Id = id,
                RequestedAtMillis = nowMillis,
                Where = param.adWhere,
                When = param.adWhen,
                TriggerTurnId = param.triggerPlayTurnId,
                Mediation = Carry(param.adMediation, previous?.Mediation),
                Algorithm = Carry(param.adAlgorithm, previous?.Algorithm)
            };
            return id;
        }

        /// <summary>
        /// Id của lần xem ad hiện tại cho format này — hàm ĐỌC thuần, không tiêu thụ, không xoay.
        /// Mốc impression của non-banner đi qua <see cref="StampImpression"/> (có tiêu thụ),
        /// không đọc qua đây.
        /// <br/>Trả null nếu format này chưa từng đi qua request (vd đường load nằm ngoài SDK).
        /// <br/>Ca banner tự refresh trong SDK: các impression sau dùng CÙNG id với request đã tạo
        /// ra banner đó — đúng ngữ nghĩa "một banner instance đẻ N impression", bản tin cụm mang
        /// <c>impressionCount</c> nên server đọc id này là id INSTANCE, không phải id lần chiếu.
        /// </summary>
        public string CurrentId(AdType type)
        {
            return Current(type)?.Id;
        }

        /// <summary>
        /// Id view cho dòng LOAD — chỉ trả khi view trong Ô CHƯA tiêu thụ (chưa có impression),
        /// ngược lại trả null.
        /// <br/>⚠ Đọc THẲNG ô (<see cref="_viewByType"/>), KHÔNG qua <see cref="Current"/>: load
        /// luôn thuộc ĐỢT XIN GẦN NHẤT, mà <c>Current</c> lại ưu tiên view ĐANG CHIẾU. Ca thật:
        /// mediation preload ad kế tiếp NGAY TRONG LÚC user đang xem (đúng án close-mồ-côi) — lúc
        /// đó <c>Current</c> trả view đang chiếu (đã tiêu thụ) nên dòng load của đợt mới sẽ mất id,
        /// dù đợt mới đang mở ngay trong ô.
        /// <br/>Vì sao không dùng <see cref="CurrentId"/>: ô giữ "view xin gần nhất" và KHÔNG ai xoá
        /// khi ad chiếu xong, nên lần load rơi vào khoảng "ad vừa chiếu xong – đợt sau chưa mở van"
        /// sẽ nhận id của ad VỪA CHIẾU — dán sai còn tệ hơn để trống (bên kho vẫn nối được bằng
        /// <c>adUnitId</c>).
        /// <br/>Format ở lại trên màn hình (banner) thì KHÔNG áp luật "đã tiêu thụ": một banner
        /// instance đẻ nhiều impression mà vẫn là view đó, các lần refill tiếp theo vẫn thuộc nó.
        /// </summary>
        public string PendingViewId(AdType type)
        {
            var view = _viewByType.GetValueOrDefault(type);
            if (view == null) return null;

            return view.ImpressionStamped && !StaysOnScreen(type) ? null : view.Id;
        }

        /// <summary>
        /// Đóng dấu impression lên view hiện tại — mốc TIÊU THỤ duy nhất của vòng đời (non-banner).
        /// Impression ĐẦU dùng id của request (phễu fill nguyên vẹn). Impression THỨ HAI trở đi
        /// trên cùng view = mediation thiếu OnRequested cho đợt mới → SDK TỰ XOAY id (khuôn
        /// play_turn_id, chốt owner 03/09: người dùng không mở lượt mới thì tự sinh — thà mất mối
        /// nối request còn hơn một id bị tính tiền N lần, đúng án dup 5% loader báo). View xoay
        /// giữ context (chỗ chiếu/ngữ cảnh/mediation là dữ liệu thật của format), nhưng
        /// fill-latency null (không đi từ request nào — không bịa) và cờ click đời trước không lây.
        /// <br/>Họ Ở LẠI MÀN HÌNH (<see cref="StaysOnScreen"/>) KHÔNG BAO GIỜ xoay: id của họ là id
        /// instance theo hợp đồng — N impression chung id là đúng thiết kế, xoay là phá ngữ nghĩa cụm.
        /// </summary>
        /// <returns>(adViewId, fillLatencyMs, rotated) — id null nếu format chưa từng có request; caller cảnh báo khi rotated.</returns>
        public (string adViewId, int? fillLatencyMs, bool rotated) StampImpression(AdType type, long nowMillis)
        {
            var view = Current(type);
            if (view == null) return (null, null, false);

            if (view.ImpressionStamped && !StaysOnScreen(type))
            {
                RotateConsumedView(view);
                view.ImpressionStamped = true;
                return (view.Id, null, true);
            }

            view.ImpressionStamped = true;
            return (view.Id, MillisSince(view.RequestedAtMillis, nowMillis), false);
        }

        /// <summary>
        /// Xoay view đã tiêu thụ thành view tự sinh cho lần hiển thị mới: id mới, mọi mốc thời
        /// gian về 0 (số đo từ request/show của đời trước không thuộc về đời này), cờ click sạch.
        /// Context giữ nguyên — nó là thuộc tính của format đang chạy, không phải của một lần chiếu.
        /// </summary>
        private static void RotateConsumedView(AdView view)
        {
            view.Id = Guid.NewGuid().ToString();
            view.RequestedAtMillis = 0;
            view.ShownAtMillis = 0;
            view.Clicked = false;
            view.ImpressionStamped = false;
        }

        /// <summary>Số ms từ một mốc wall-clock — null nếu mốc không tồn tại (view tự sinh), không bịa 0.</summary>
        private static int? MillisSince(long originMillis, long nowMillis)
        {
            if (originMillis <= 0) return null;
            return (int)Math.Max(0, nowMillis - originMillis);
        }

        /// <summary>
        /// Ghi tham số cho lần xem ad hiện tại của format này — chỉ đối số khác rỗng mới ghi đè.
        /// Preload thì lúc xin ad chưa biết sẽ chiếu ở đâu / trong ngữ cảnh nào: biết lúc nào ghi
        /// lúc đó, các mốc sau (show / impression / close) tự mang theo.
        /// </summary>
        /// <returns>false nếu format này chưa từng đi qua request (không có view nào để ghi).</returns>
        public bool SetContext(AdType type, string adWhere = null, string adWhen = null,
            string adMediation = null, string adAlgorithm = null, string triggerPlayTurnId = null)
        {
            if (!_viewByType.TryGetValue(type, out var view)) return false;
            if (!string.IsNullOrEmpty(adWhere)) view.Where = adWhere;
            if (!string.IsNullOrEmpty(adWhen)) view.When = adWhen;
            if (!string.IsNullOrEmpty(triggerPlayTurnId)) view.TriggerTurnId = triggerPlayTurnId;
            if (!string.IsNullOrEmpty(adMediation)) view.Mediation = adMediation;
            if (!string.IsNullOrEmpty(adAlgorithm)) view.Algorithm = adAlgorithm;
            return true;
        }

        /// <summary>
        /// Ghi ad unit đã chiếu cho view hiện tại (gọi ở mốc impression) — false nếu format chưa có
        /// view nào.
        /// </summary>
        public bool SetImpressionUnit(AdType type, string adUnitId)
        {
            if (string.IsNullOrEmpty(adUnitId)) return false;

            var view = Current(type);
            if (view == null) return false;

            view.ImpressionUnitId = adUnitId;
            return true;
        }

        /// <summary>
        /// Thuật toán load của ĐỢT hiện tại — đọc thẳng ô, nên vẫn trả giá trị sau khi ad đã chiếu
        /// (thuật toán là thuộc tính của tầng mediation đang chạy, không phải của một lần chiếu).
        /// Dùng cho dòng LOAD: mediation khai một lần qua <c>UpdateContext</c> là mọi dòng load của
        /// format đó mang theo, khỏi phải truyền lại trong từng <c>AdLoadResultParam</c>.
        /// </summary>
        public string CurrentAlgorithm(AdType type) => _viewByType.GetValueOrDefault(type)?.Algorithm;

        /// <summary>Ad unit đã chiếu của view hiện tại — null nếu chưa có impression nào mang unit.</summary>
        public string ImpressionUnitId(AdType type) => Current(type)?.ImpressionUnitId;

        /// <summary>
        /// Ảnh chụp lần xem ad hiện tại của format này (null nếu chưa từng có request) — bản sao,
        /// game sửa vào đó không ảnh hưởng state.
        /// </summary>
        public AdViewSnapshot TakeSnapshot(AdType type, long nowMillis)
        {
            var view = Current(type);
            if (view == null) return null;
            return new AdViewSnapshot
            {
                adViewId = view.Id,
                type = type,
                adWhere = view.Where,
                adWhen = view.When,
                triggerPlayTurnId = view.TriggerTurnId,
                adMediation = view.Mediation,
                adAlgorithm = view.Algorithm,
                sinceRequestMs = MillisSince(view.RequestedAtMillis, nowMillis),
                sinceShownMs = MillisSince(view.ShownAtMillis, nowMillis)
            };
        }

        /// <summary>
        /// Vị trí chiếu đã biết của lần xem ad hiện tại (null nếu chưa ai ghi) — dùng điền cho log
        /// impression do mediation bắn.
        /// </summary>
        public string CurrentPlacement(AdType type)
        {
            return Current(type)?.Where;
        }

        /// <summary>Ngữ cảnh kích hoạt đã biết của lần xem ad hiện tại (null nếu chưa ai ghi).</summary>
        public string CurrentContext(AdType type)
        {
            return Current(type)?.When;
        }

        /// <summary>
        /// Đã bao lâu kể từ lúc xin ad cho format này (ms) — dùng làm <c>fillLatencyMs</c> trên log
        /// impression (§D2). Null nếu impression không đi từ request nào của SDK (không bịa 0).
        /// </summary>
        public int? MillisSinceRequest(AdType type, long nowMillis)
        {
            var view = Current(type);
            return view == null ? null : MillisSince(view.RequestedAtMillis, nowMillis);
        }

        /// <summary>
        /// Đánh dấu mốc gọi hiển thị và đồng bộ ngữ cảnh vào param.
        /// <br/>Show chạm view ĐÃ CÓ impression (non-banner) = lần hiển thị MỚI mà mediation quên
        /// OnRequested → xoay id ngay tại đây (cùng án với <see cref="StampImpression"/> — xoay ở
        /// show thì cả bộ show/impression/close của lần chiếu mới đi chung id mới, không nửa nạc).
        /// <br/>Thời gian chờ để null nếu lần show này không đi từ request nào của SDK (không bịa 0).
        /// </summary>
        /// <returns>(adViewId, requestToShowMs, rotated) — id null nếu format chưa từng có request; caller cảnh báo khi rotated.</returns>
        public (string adViewId, int? requestToShowMs, bool rotated) MarkShown(AdShowParam param, long nowMillis)
        {
            if (!_viewByType.TryGetValue(param.type, out var view)) return (null, null, false);

            var rotated = view.ImpressionStamped && !StaysOnScreen(param.type);
            if (rotated) RotateConsumedView(view);

            view.ShownAtMillis = nowMillis;
            ApplyContext(param, view);

            // Từ đây tới lúc close, mọi mốc của format này thuộc về VIEW NÀY — kể cả khi mediation
            // preload ad kế tiếp vào slot giữa chừng. Show mới đè sổ: ca thiếu close (app bị kill,
            // mediation quên bắn) tự lành ở lần chiếu sau chứ không kẹt vĩnh viễn.
            if (!StaysOnScreen(param.type)) _onScreenByType[param.type] = view;

            return (view.Id, MillisSince(view.RequestedAtMillis, nowMillis), rotated);
        }

        /// <summary>
        /// Đánh dấu người chơi bấm vào lần xem ad hiện tại của format này. Cờ nằm trong view nên
        /// request mới tự sạch; bấm nhiều lần vẫn là một cờ.
        /// </summary>
        /// <returns>false nếu format này chưa có view nào (click không thuộc về ai — caller cảnh báo).</returns>
        public bool MarkClicked(AdType type)
        {
            // Native không giữ sổ view (CanHaveManyOnScreen) nên không có chỗ ghi cờ — nhưng đây
            // KHÔNG phải lỗi tích hợp, nên trả true để caller khỏi bắn warning mỗi lần người chơi
            // bấm. ⚠ Hệ quả: click của native không lên wire được (nó vốn không có mốc close để
            // chở cờ) — CTR native phải đọc ở dashboard mediation.
            if (CanHaveManyOnScreen(type)) return true;

            var view = Current(type);
            if (view == null) return false;
            view.Clicked = true;
            return true;
        }

        /// <summary>
        /// Đánh dấu mốc đóng và đồng bộ ngữ cảnh vào param.
        /// <br/>Thời lượng để null nếu chưa từng qua mốc show.
        /// <br/><c>hasClick</c>: caller tự nhập thì giá trị đó thắng; bỏ trống thì lấy CẢ HAI CHIỀU
        /// từ cờ <see cref="MarkClicked"/> — chưa thấy click thì gửi <c>false</c> TƯỜNG MINH (sửa
        /// 23/09 theo đo của loader: 0 dòng nào mang false, nên bên đọc số phải tự COALESCE và
        /// không phân biệt được "không bấm" với "không biết"). Mediation đã nối OnClicked ở mọi
        /// luồng nên cờ này là số đo thật, không phải suy bừa.
        /// </summary>
        /// <returns>(adViewId, shownDurationSec) — thời lượng null nếu chưa từng qua mốc show.</returns>
        public (string adViewId, int? shownDurationSec) MarkClosed(AdCloseParam param, long nowMillis)
        {
            var view = Current(param.type);
            if (view == null) return (null, null);

            ApplyContext(param, view);
            param.hasClick ??= view.Clicked;

            // Lần chiếu khép lại: các mốc sau thuộc về view trong slot (ad vừa preload)
            _onScreenByType.Remove(param.type);

            if (!StaysOnScreen(param.type))
            {
                _lastFullScreenType = param.type;
                _lastFullScreenClosedAtMillis = nowMillis;
            }

            return (view.Id,
                view.ShownAtMillis > 0 ? (int?)Math.Max(0, (nowMillis - view.ShownAtMillis) / 1000) : null);
        }

        /// <summary>
        /// Đồng bộ chỗ chiếu / ngữ cảnh / mediation giữa param và cache — HAI CHIỀU: mốc nào bỏ
        /// trống thì lấy từ cache, mốc nào tự nhập thì giá trị đó thắng VÀ cập nhật cache cho các
        /// mốc sau (preload không biết chỗ chiếu, lúc show mới biết — tin mới nhất là tin đúng nhất).
        /// <br/>Public vì log impression đi bằng <c>FAdLog</c> chứ không qua họ AdViewParam, nên
        /// nó phải gọi từ decor — nhưng LUẬT chỉ có một bản, ở đây.
        /// </summary>
        public void ApplyContext(AdViewParam param, AdType type)
        {
            var view = Current(type);
            if (view != null) ApplyContext(param, view);
        }

        private static void ApplyContext(AdViewParam param, AdView view)
        {
            param.adWhere = Merge(param.adWhere, ref view.Where);
            param.adWhen = Merge(param.adWhen, ref view.When);
            param.triggerPlayTurnId = Merge(param.triggerPlayTurnId, ref view.TriggerTurnId);
            param.adMediation = Merge(param.adMediation, ref view.Mediation);
            param.adAlgorithm = Merge(param.adAlgorithm, ref view.Algorithm);
        }

        /// <summary>
        /// "Unknown" tính là TRỐNG: <c>AdParam.CorrectValues</c> điền sẵn giá trị đó cho mốc
        /// impression trước khi decor chạy, không coi là trống thì cache sẽ bị ghi đè bằng
        /// "Unknown" và mất luôn chỗ chiếu mà game đã báo lúc request.
        /// </summary>
        /// <summary>
        /// Giá trị của đợt mới nếu có, không thì giữ giá trị đợt trước — dùng cho field thuộc TẦNG
        /// HẠ TẦNG (mediation, thuật toán load), thứ không đổi giữa hai đợt xin ad.
        /// </summary>
        private static string Carry(string incoming, string previous)
            => string.IsNullOrEmpty(incoming) ? previous : incoming;

        private static string Merge(string value, ref string cached)
        {
            if (string.IsNullOrEmpty(value) || value == FParam.UNKNOWN) return cached ?? value;
            cached = value;
            return value;
        }
    }
}
