/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-10-26
 */

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Bản cơ bản của <see cref="IAdLoadLogConfig"/> — chỉ đọc service remote config mà BigData vốn
    /// đã tham chiếu (<c>Core.RemoteConfig</c>, khoá <c>fAdLoadLogSplit</c>, mặc định <c>true</c>).
    /// Không có khoá trên server → tách, đúng mặc định đã chốt.
    /// <br/>⚠ Class này <b>LUÔN tồn tại cùng BigData</b>, nên "game không cài AnalyticParamsSync" KHÔNG
    /// có nghĩa là chế độ gộp — nó vẫn tách theo mặc định ở đây. Van tắt cho một game là gửi
    /// <c>fAdLoadLogSplit = false</c> qua <c>Core.RemoteConfig</c>, không cần CMS.
    /// <br/>KHÔNG gắn <c>[Primary]</c>: đây là bản để module khác ghi đè (khuôn
    /// <c>ReserveTimeRepository</c> ↔ <c>ServerTimeRepository</c>), và hai <c>[Primary]</c> cùng
    /// ưu tiên là container ném.
    /// </summary>
    public class ReserveAdLoadLogConfig : IAdLoadLogConfig
    {
        private readonly AnalyticConfigService _config;

        public ReserveAdLoadLogConfig(AnalyticConfigService config)
        {
            _config = config;
        }

        public bool GroupAdLoadLog => !_config.AdLoadLogSplit;
    }
}
