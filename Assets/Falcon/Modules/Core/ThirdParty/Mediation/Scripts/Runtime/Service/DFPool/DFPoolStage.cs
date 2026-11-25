/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2026-10-28
 */

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime.DFPool
{
    /// <summary>
    /// Mã giai đoạn in ở đầu MỌI dòng log của DFPool: <c>[DFPool][B3-READY][inter] ...</c>.
    /// Lọc log theo mã để theo dõi đúng một bước của luồng. Danh sách đầy đủ + log mẫu: README.md.
    /// Log native (logcat, tag "DFPool") dùng cùng bộ mã.
    /// </summary>
    internal static class DFPoolStage
    {
        // B0 — khởi động
        public const string INIT = "B0-INIT";

        // B1 — ad MAX bước 1 (MultiCall) load xong -> mốc giá
        public const string MAX_REVENUE = "B1-MAX-REVENUE";

        // B2 — request AdMob theo multiplier, ad về kho
        public const string ADMOB_REQUEST = "B2-ADMOB-REQ";
        public const string ADMOB_LOADED = "B2-ADMOB-OK";
        public const string ADMOB_FAILED = "B2-ADMOB-FAIL";

        // B2 — ad AdMob mặc định (defaultAdId, không giá sàn), giữ sẵn 1 ad trong kho
        public const string DEFAULT_REQUEST = "B2-DEFAULT-REQ";
        public const string DEFAULT_LOADED = "B2-DEFAULT-OK";
        public const string DEFAULT_FAILED = "B2-DEFAULT-FAIL";

        // B3 — slot MAX mượn ad + waterfall
        public const string BIND = "B3-BIND";
        public const string REBIND = "B3-REBIND";
        public const string READY = "B3-READY";
        public const string FOREIGN = "B3-FOREIGN";
        public const string LOAD_FAIL = "B3-LOAD-FAIL";

        // B4 — có ad tốt hơn: trả ad cũ về kho + show giả để MAX nhả slot
        public const string UPGRADE = "B4-UPGRADE";
        public const string WAIT = "B4-WAIT";
        public const string FLUSH = "B4-FLUSH";
        public const string FLUSH_DONE = "B4-FLUSH-DONE";
        public const string FLUSH_RETRY = "B4-FLUSH-RETRY";

        // B5 — show
        public const string SHOW = "B5-SHOW";
        public const string DISPLAYED = "B5-DISPLAYED";
        public const string PAID = "B5-PAID";
        public const string REWARD = "B5-REWARD";
        public const string HIDDEN = "B5-HIDDEN";
        public const string SHOW_FAIL = "B5-SHOW-FAIL";

        // B6 — hết hạn / kho đầy
        public const string EXPIRE = "B6-EXPIRE";
        public const string EVICT = "B6-EVICT";

        // ảnh chụp kho + slot mỗi khi có thay đổi
        public const string POOL = "POOL";
    }
}
