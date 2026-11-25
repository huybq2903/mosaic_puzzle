/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-10-26
 */

using System;
using System.Collections.Generic;
using Falcon.Helpers.Devkit;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Số thứ tự TRONG PHIÊN của từng mốc ad (yêu cầu phía đọc số 26/10: *"eCPM thường đổi theo
    /// index này nên thêm vào rất có giá trị phân tích"*) — bốn sổ đếm riêng:
    /// <c>request</c> · <c>load</c> · <c>load thành công</c> · <c>impression</c>.
    /// <br/><b>Đếm theo (PHIÊN × FORMAT)</b>, không phải một sổ chung: eCPM đi theo thứ tự của CHÍNH
    /// format đó (inter thứ 3 của phiên), mà một sổ chung thì banner tự refresh vài chục lần sẽ đẩy
    /// số của inter/reward thành vô nghĩa. Muốn số chung thì cộng ở kho, còn tách ra từ số chung thì
    /// không làm được — nên gửi cái tách.
    /// <br/><b>Reset theo <c>SessionId</c></b>, không theo vòng đời đối tượng: app vào nền rồi quay
    /// lại là phiên mới (luật <c>app_open</c>), mà singleton thì vẫn sống — so id là cách duy nhất
    /// thấy được chuyện đó.
    /// <br/>⚠ Số đếm là của SDK, không persist qua kill: "thứ mấy trong phiên" nói được rành rọt,
    /// còn cộng dồn qua phiên thì đã có <c>typeCount</c> (sổ theo đời, trong
    /// <c>IFPlayerAdRepository</c>) — hai thứ khác nhau, đừng lẫn.
    /// <br/>Pure class — <see cref="AdSessionCounterCache"/> lo khoá và nguồn <c>SessionId</c>.
    /// <br/><b>1.5.7 thêm hai số KHÔNG phải số đếm</b> (<c>firstAdEcpm</c>/<c>previousAdEcpm</c>, bên
    /// quyết sàn xin để tính sàn cho những lần sau). Chúng ở đây vì <b>cùng một luật reset</b>: sổ
    /// theo PHIÊN, so <c>SessionId</c> mà xoá. Tách ra class riêng là nhân đôi đúng cơ chế reset đó —
    /// hai bản sao của một luật thì sớm muộn lệch nhau.
    /// </summary>
    public class AdSessionCounterState
    {
        private class Counters
        {
            public int Request;
            public int Load;
            public int LoadSuccess;
            public int Impression;
        }

        private readonly Dictionary<AdType, Counters> _byType = new();
        private int _sessionId = int.MinValue;
        private double? _firstAdEcpm;
        private bool _firstRecorded;
        private double? _previousAdEcpm;

        /// <summary>Phiên đang đếm — đổi là mọi sổ về 0.</summary>
        public int SessionId => _sessionId;

        /// <summary>
        /// Ghi nhận doanh thu của MỘT lượt chiếu (mốc impression) — nguồn của
        /// <see cref="FAdLoadLog.firstAdEcpm"/> và <see cref="FAdLoadLog.previousAdEcpm"/>.
        /// <br/>Hai cột, hai luật NGƯỢC nhau, cố ý:
        /// <list type="bullet">
        /// <item><b>first</b> chốt ở lượt chiếu ĐẦU TIÊN của phiên và không đổi nữa — kể cả khi lượt
        /// đó không có doanh thu (giá trị để null, nhưng chỗ "lượt đầu" đã bị chiếm). Nếu lọc theo
        /// "số dương" thì lượt thứ hai sẽ được đẩy lên làm lượt đầu, tức khai sai một sự kiện.</item>
        /// <item><b>previous</b> thì lượt mới LUÔN đè, kể cả đè bằng null: cột này nói "lượt ngay
        /// trước", giữ lại số của lượt cũ hơn là trả lời sai câu hỏi.</item>
        /// </list>
        /// <paramref name="adRev"/> là tiền của MỘT lượt nên nhân 1000 ra eCPM.
        /// </summary>
        public void RecordImpressionRevenue(double adRev, int sessionId)
        {
            Touch(sessionId);

            var ecpm = adRev > 0 ? adRev * 1000d : (double?)null;
            _previousAdEcpm = ecpm;

            if (_firstRecorded) return;
            _firstRecorded = true;
            _firstAdEcpm = ecpm;
        }

        /// <summary>
        /// Số lượt chiếu của format này đã đếm trong phiên — ĐỌC, không tăng. 0 nếu chưa có lượt nào
        /// (hoặc phiên đã đổi).
        /// <br/>Dòng load dùng nó để trả lời *"lúc load này thì đã chiếu mấy quảng cáo"* — và vì thế
        /// nó LỆCH MỘT NHỊP với <c>sessionAdImpressionIndex</c> của dòng impression (cùng một quảng
        /// cáo: load thấy 2, impression của nó là 3). Hai số phải mang hai TÊN khác nhau trên dây —
        /// xem <see cref="FAdLoadLog.sessionAdShownBefore"/>.
        /// </summary>
        public int ImpressionsSoFar(AdType type, int sessionId)
        {
            if (sessionId != _sessionId) return 0;
            return _byType.TryGetValue(type, out var counters) ? counters.Impression : 0;
        }

        /// <summary>eCPM của lượt chiếu đầu phiên; null nếu phiên chưa chiếu gì (hoặc phiên đã đổi).</summary>
        public double? FirstAdEcpm(int sessionId) => sessionId == _sessionId ? _firstAdEcpm : null;

        /// <summary>eCPM của lượt chiếu ngay trước; null nếu phiên chưa chiếu gì (hoặc phiên đã đổi).</summary>
        public double? PreviousAdEcpm(int sessionId) => sessionId == _sessionId ? _previousAdEcpm : null;

        public int NextRequest(AdType type, int sessionId) => Next(type, sessionId, c => ++c.Request);

        public int NextLoad(AdType type, int sessionId) => Next(type, sessionId, c => ++c.Load);

        public int NextLoadSuccess(AdType type, int sessionId) => Next(type, sessionId, c => ++c.LoadSuccess);

        public int NextImpression(AdType type, int sessionId) => Next(type, sessionId, c => ++c.Impression);

        private int Next(AdType type, int sessionId, Func<Counters, int> step)
        {
            Touch(sessionId);

            if (!_byType.TryGetValue(type, out var counters)) _byType[type] = counters = new Counters();
            return step(counters);
        }

        /// <summary>Phiên đổi thì mọi sổ của phiên cũ về 0 — một chỗ duy nhất biết luật đó.</summary>
        private void Touch(int sessionId)
        {
            if (sessionId == _sessionId) return;

            _sessionId = sessionId;
            _byType.Clear();
            _firstAdEcpm = null;
            _firstRecorded = false;
            _previousAdEcpm = null;
        }
    }
}
