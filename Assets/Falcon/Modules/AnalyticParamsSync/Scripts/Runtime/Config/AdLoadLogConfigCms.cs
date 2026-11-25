/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-10-26
 */

using Falcon.Modules.Core.RemoteConfigCms;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.AnalyticParamsSync
{
    /// <summary>
    /// Nửa CMS của khoá chọn chế độ telemetry load ad — cùng tên khoá với
    /// <c>AnalyticConfig.fAdLoadLogSplit</c> ở service <c>Core.RemoteConfig</c>, vì hai service là
    /// hai nguồn ĐỘC LẬP và bên đọc số muốn tắt ở đâu cũng được.
    /// <br/>Mặc định <c>true</c> (tách) để không cần cấu hình gì trên server; luật là OR nên chỉ
    /// cần MỘT service gửi <c>false</c> là về gộp — xem <see cref="ServerAdLoadLogConfig"/>.
    /// </summary>
    public class AdLoadLogConfigCms : IFalconConfigCms
    {
        public bool fAdLoadLogSplit = true;
    }
}
