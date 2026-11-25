/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 */

using Falcon.Helpers.Devkit;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Thứ điền được tham số TIẾN ĐỘ của lượt đang mở (điểm, số nước, thời lượng, booster, tiến độ, extra) lên một
    /// dòng heartbeat. Tách khỏi <see cref="ILevelTurnParamsProvider"/> để code thuần (và test) dùng được mà không
    /// phải là singleton của container.
    /// </summary>
    public interface ITurnParamsFiller
    {
        /// <summary>
        /// Điền tham số tiến độ của lượt đang mở vào <paramref name="param"/>. Chỉ điền cái đo được — không đo được
        /// thì để nguyên (field vắng). KHÔNG điền định danh lượt: SDK tự điền từ cache.
        /// <br/>Chạy trên main thread, lúc app xuống nền và lúc <c>Level.OnPauseExit</c>: phải RẺ, không side effect.
        /// </summary>
        /// <returns>false nếu lúc này không có gì để điền (vd game coi là không trong level).</returns>
        bool FillTurnParams(LevelHeartBeatParamV2 param);
    }

    /// <summary>
    /// Nguồn tham số lượt chơi mà BigData gọi tới lúc tự bắn heartbeat (hợp đồng đợt 3 B5: SDK quyết lúc bắn, game
    /// chỉ khai tham số). Module APS cài interface này (<c>ALevelHeartBeatService</c>); game tự quản tham số thì
    /// thay vào đó gọi <c>Level.OnHeartbeat</c> mỗi khi tham số đổi.
    /// <br/>Là singleton của container: mọi lớp cài nó được tự tạo và nối vào BigData lúc khởi động.
    /// </summary>
    public interface ILevelTurnParamsProvider : ITurnParamsFiller, IMySingleton
    {
    }
}
