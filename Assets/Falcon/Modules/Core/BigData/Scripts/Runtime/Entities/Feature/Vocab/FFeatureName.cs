/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-02
 */

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    ///     Tên TÍNH NĂNG theo từ vựng hợp đồng kho §4.1 — dùng hằng số ở đây thay vì gõ chuỗi tay.
    ///     <br/>⚠ <b>Đây là GỢI Ý, không phải cửa chặn.</b> Tên ngoài danh sách vẫn gửi được bình
    ///     thường và SDK <b>không kiểm</b> (án owner 30/10): kiểm theo danh sách = bắt game đăng ký
    ///     tên trước, đúng cái ma sát mà hợp đồng live-op sinh ra để dẹp. Cùng khuôn
    ///     <see cref="FFunnelName"/> và <see cref="FFeatureStep"/>.
    ///     <br/>Vậy hằng số để làm gì: rủi ro thật của trục này không phải "tên lạ" mà là <b>LỖI
    ///     GÕ sinh ra một tính năng song song</b> — <c>guild</c> vs <c>guilds</c> chia đôi số liệu,
    ///     im lặng. Autocomplete dẹp nó ngay lúc gõ, không phải đợi DQ bên kho phát hiện sau.
    ///     <br/>Tên HIỂN THỊ của từng game (Team / Club / Clan) là chuyện UI — kho gọi
    ///     <see cref="Guild"/>.
    /// </summary>
    public static class FFeatureName
    {
        /// <summary>Bang hội / team / club / clan — thực thể nhiều người.</summary>
        public const string Guild = "guild";

        /// <summary>Bạn bè (gửi tim, tặng thẻ) — thực thể nhiều người, không có "nhóm".</summary>
        public const string Friend = "friend";

        public const string DailyGift = "daily_gift";
        public const string LuckyWheel = "lucky_wheel";
        public const string StarChest = "star_chest";
        public const string LevelChest = "level_chest";
        public const string WinStreak = "win_streak";
        public const string HomeDecor = "home_decor";
        public const string Collection = "collection";
        public const string Upgrade = "upgrade";
        public const string Cosmetic = "cosmetic";
        public const string IdleMission = "idle_mission";
    }
}
