/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-10-26
 */

using Falcon.Helpers.Devkit;
using Falcon.Modules.Core.BigData;
using Falcon.Modules.Core.RemoteConfigCms;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.AnalyticParamsSync
{
    /// <summary>
    /// Bản ghi đè <see cref="IAdLoadLogConfig"/> biết CẢ HAI service remote config — khuôn giống
    /// <see cref="ServerTimeRepository"/>: inject bản cơ bản của BigData rồi cộng thêm nguồn thứ hai.
    /// <br/>Luật OR (chốt 26/10): <b>một bên gửi <c>false</c> là về GỘP ngay</b>; khoá vắng mặt tính
    /// là <c>true</c> (tách) nên chưa cấu hình gì trên server thì vẫn tách.
    /// <br/>Đọc lại mỗi lần được hỏi, không cache theo phiên: cả hai service đều cập nhật giữa phiên
    /// (<c>IConfigUpdatedReact.OnConfigUpdated</c> bên <c>Core.RemoteConfig</c> đã reset
    /// <c>AnalyticConfigService</c>; <c>FConfigControllerCms</c> trả instance mới nhất) → flip ăn
    /// ngay, đúng yêu cầu "lập tức".
    /// <br/>⚠ Module này tham chiếu <c>Core.RemoteConfigCms</c>. Đó KHÔNG phải phụ thuộc mới của
    /// toàn hệ: AnalyticParamsSync vốn đã tham chiếu <c>Core.ThirdParty.Mediation</c>, mà Mediation
    /// tham chiếu cứng CMS — nên game nào biên dịch được module này thì đã có CMS.
    /// </summary>
    [Primary]
    public class ServerAdLoadLogConfig : IAdLoadLogConfig
    {
        private readonly ReserveAdLoadLogConfig _reserve;
        private readonly FConfigControllerCms _cms;

        public ServerAdLoadLogConfig(ReserveAdLoadLogConfig reserve, FConfigControllerCms cms)
        {
            _reserve = reserve;
            _cms = cms;
        }

        public bool GroupAdLoadLog =>
            // OR hai nguồn: bản cơ bản đã mang nửa Core.RemoteConfig
            _reserve.GroupAdLoadLog || !_cms.Config<AdLoadLogConfigCms>().fAdLoadLogSplit;
    }
}
