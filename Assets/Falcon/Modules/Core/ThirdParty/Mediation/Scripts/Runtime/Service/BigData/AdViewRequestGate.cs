using System.Collections.Generic;
using Falcon.Helpers.Devkit;
using Falcon.Modules.Core.BigData;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime
{
    /// <summary>
    /// Van cho mốc <c>FalconBigDataController.Ad.OnRequested</c> — MỘT lần cho mỗi ĐỢT xin ad,
    /// KHÔNG phải mỗi lần gọi LoadAd. Đúng hợp đồng Request-Mediation-AdLifecycle.md: "CHỈ gọi ở
    /// lần xin ĐẦU của mỗi đợt (preload sau boot / refill sau khi show xong). Vòng RETRY TỰ ĐỘNG
    /// bên trong KHÔNG gọi lại dòng này".
    /// <br/>
    /// <b>Vì sao phải có van thay vì gọi thẳng:</b> BigData giữ ad-view theo MỘT slot cho mỗi
    /// <see cref="AdType"/>, và MỖI lần <c>OnRequested</c> là sinh <c>adViewId</c> mới + xoá trắng
    /// slot cũ (<c>AdViewState.Open</c>). Tầng multicall lại bắn LoadAd cho N ad unit song song rồi
    /// retry theo thang cấu hình, nên gọi thẳng thì:
    /// <list type="bullet">
    /// <item>mẫu số request phồng N×retry — <c>fill rate = 1 − load_error/request</c> thành vô nghĩa;</item>
    /// <item><c>adViewId</c> ở mốc show/impression/close rơi về unit load CUỐI chứ không phải view
    /// đang chiếu — nhãn load_error/show_error dán nhầm view.</item>
    /// </list>
    /// Van giữ đúng MỘT view mở cho mỗi format: mở ở lần xin đầu của đợt, đóng khi view kết thúc
    /// (<c>OnClosed</c> / <c>OnShowFailed</c>); đợt refill sau đó mới mở view mới.
    /// <br/>
    /// Toàn bộ luật nằm ở phía mediation — module BigData KHÔNG đổi.
    /// </summary>
    public static class AdViewRequestGate
    {
        // adAlgorithm khai ở mốc request (Request-Mediation-AdEcpmAndParams.md V3): mọi mốc sau của
        // cùng view tự mang theo. Chuỗi phải ỔN ĐỊNH — phía đọc số group theo nó.

        /// <summary>Đường một-unit: FalconMaxService không multicall, bridge AdMob.</summary>
        public const string ALGORITHM_SINGLE = "single";

        /// <summary>Workflow Interstitial eCPM Floor Personalization.</summary>
        public const string ALGORITHM_FLOOR_PERSONALIZATION = "floor_personalization";

        public const string ALGORITHM_LUA = "lua";

        public static string LuaAlgorithm(string scriptId) =>
            string.IsNullOrWhiteSpace(scriptId) ? ALGORITHM_LUA : ALGORITHM_LUA + "_" + scriptId.Trim();

        public const string ALGORITHM_META_ADMOB = "meta_admob";

        public const string ALGORITHM_DFPOOL = "dfpool";

        /// <summary>Đường multicall, theo provider index đang chạy (<c>IMultiCallProvider.GetIndex()</c>).</summary>
        public static string MultiCallAlgorithm(int providerIndex) => "multicall_" + providerIndex;

        private class GateState
        {
            public bool IsOpen;
            public bool GaveUpReported;
        }

        private static readonly Dictionary<AdType, GateState> _stateByType = new();
        private static readonly object _lock = new();

        /// <summary>
        /// Mở view cho ĐỢT xin ad này. Trả <c>true</c> nếu thật sự mở (đã bắn <c>OnRequested</c>),
        /// <c>false</c> nếu đợt đang mở rồi — unit khác cùng đợt, hoặc retry: cả hai đều KHÔNG
        /// được bắn thêm mốc request.
        /// </summary>
        /// <param name="adAlgorithm">Thuật toán load của đợt — xem các hằng <c>ALGORITHM_*</c>.</param>
        public static bool Open(AdType type, string adMediation, string adAlgorithm = null)
        {
            lock (_lock)
            {
                var state = Get(type);
                if (state.IsOpen) return false;
                state.IsOpen = true;
                state.GaveUpReported = false;
            }

            //gọi NGOÀI lock: FAdApi tự khoá bên trong, ôm lock của mình khi gọi sang module khác
            //là mời deadlock (đúng án DI 12/08 đã tách AdViewCache ra khỏi AdRequestService).
            FalconBigDataController.Ad.OnRequested(type, adMediation: adMediation, adAlgorithm: adAlgorithm);
            return true;
        }

        /// <summary>
        /// View đã kết thúc (ad đóng, hoặc show hỏng) — đợt load kế tiếp được mở view mới.
        /// Gọi NGAY SAU <c>Ad.OnClosed</c> / <c>Ad.OnShowFailed</c> của cùng format, và gọi VÔ
        /// ĐIỀU KIỆN: kẹt van là mọi đợt load sau mất luôn mốc request.
        /// </summary>
        public static void Close(AdType type)
        {
            lock (_lock)
            {
                var state = Get(type);
                state.IsOpen = false;
                state.GaveUpReported = false;
            }
        }

        /// <summary>
        /// Cả đợt bỏ cuộc (thang retry đã cạn mà vẫn không có ad nào trong kho) — nhãn
        /// <c>LOAD_ERROR</c>, MỘT lần cho mỗi view theo hợp đồng ("Gọi mỗi lần unit fail là spam
        /// kênh nhãn"). Gọi bao nhiêu lần cũng được, van tự chặn từ lần thứ hai trở đi.
        /// <br/>
        /// ⚠ Định nghĩa "bỏ cuộc" là do tầng mediation tự đặt (MAX retry vô hạn, không có mốc bỏ
        /// cuộc thật) — xem chỗ gọi. Cần data team chốt lại trước khi tin con số fill rate.
        /// </summary>
        public static void ReportLoadGaveUp(AdType type, string errorCode)
        {
            lock (_lock)
            {
                var state = Get(type);
                if (!state.IsOpen || state.GaveUpReported) return;
                state.GaveUpReported = true;
            }

            FalconBigDataController.Ad.OnLoadFailed(type, errorCode);
        }

        private static GateState Get(AdType type)
        {
            if (_stateByType.TryGetValue(type, out var state)) return state;
            state = new GateState();
            _stateByType[type] = state;
            return state;
        }
    }
}
