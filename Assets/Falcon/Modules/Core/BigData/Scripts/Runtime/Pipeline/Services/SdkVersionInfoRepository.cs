/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-10-14
 */

using System.Collections.Generic;
using Falcon.Helpers.Devkit;
using UnityEngine.Scripting;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    ///     Dán phiên bản BigData lên MỌI log (đi qua cơ chế <c>IFCustomInfoRepository</c> → central
    ///     user param, cùng đường với nhãn AB và common param).
    ///     <br/>Vì sao per-row chứ không gửi một lần mỗi phiên: số này quyết định <b>NGHĨA</b> của
    ///     chính dòng log đó. Mọi mốc era của kho ("từ bản này `floor` là trung bình", "từ bản này
    ///     hai cột streak ngừng gửi") hiện phải suy gián tiếp từ <c>appVersion</c> của game — mà
    ///     một bản game có thể vá SDK giữa đường, nên suy kiểu đó sai âm thầm. Có field này thì
    ///     cắt era là một phép lọc: <c>WHERE sdk_version >= '…'</c>.
    ///     <br/>Phiên bản của MODULE KHÁC (Mediation, GameData…) KHÔNG đi đường này — chúng không
    ///     đổi nghĩa dòng log, nên để sau gửi một dòng property mỗi phiên là đủ.
    ///     <br/>Không phải key chỉ-đúng-một-khoảnh-khắc, nên bản tin CỤM (ad load stats, ui
    ///     impression, banner) cũng mang bình thường.
    /// </summary>
    /// <remarks>
    ///     <see cref="PreserveAttribute"/> vì lớp này CHỈ được dựng qua reflection (container quét
    ///     <c>IFCustomInfoRepository</c> rồi <c>Activator.CreateInstance</c>) — không có chỗ nào
    ///     tham chiếu tĩnh tới nó, nên managed stripping của bản build IL2CPP có quyền cắt. Cắt là
    ///     field biến mất khỏi log một cách im lặng, chỉ bản build mới lỗi còn editor vẫn chạy đúng.
    /// </remarks>
    [Preserve]
    public class SdkVersionInfoRepository : IFCustomInfoRepository
    {
        /// <inheritdoc cref="FCentralUserParamService.ParamKey.App.SDK_VERSION"/>
        public const string KEY = FCentralUserParamService.ParamKey.App.SDK_VERSION;

        public Dictionary<string, object> GetInfo()
        {
            return new Dictionary<string, object> { [KEY] = BigDataVersion.VALUE };
        }
    }
}
