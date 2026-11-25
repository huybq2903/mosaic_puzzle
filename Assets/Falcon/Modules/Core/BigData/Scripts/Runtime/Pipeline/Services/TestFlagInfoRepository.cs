/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-10-16
 */

using System.Collections.Generic;
using Falcon.Helpers.Devkit;
using UnityEngine;
using UnityEngine.Scripting;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    ///     Đánh dấu dòng log do NGƯỜI LÀM GAME sinh ra, để kho lọc khỏi dữ liệu thật — đi qua cơ
    ///     chế <c>IFCustomInfoRepository</c> nên dán lên mọi log, không thêm plumbing.
    ///     <br/><b>Luật cứng (chốt owner 16/10): bản PRODUCTION tuyệt đối không bao giờ mang cờ
    ///     này.</b> Giá trị tính tại chỗ từ loại build — <see cref="Application.isEditor"/> hoặc
    ///     <see cref="Debug.isDebugBuild"/> — và **không ai ghi đè được**: không API, không remote
    ///     config, không common param. Lý do: một lần dev quên tắt là hỏng toàn bộ dữ liệu của bản
    ///     phát hành, mà hỏng kiểu đó không có dấu hiệu gì.
    ///     <br/>⚠ KHÔNG persist xuống đĩa. Cờ là thuộc tính của BUILD, không phải của máy: cài bản
    ///     dev rồi cài bản store lên cùng thiết bị, nếu cờ sống trong kho local thì dòng production
    ///     mang cờ test — đúng cái luật trên cấm.
    ///     <br/>⚠ KHÔNG dùng cờ này để TẮT log. Tắt là việc của
    ///     <c>fCoreAnalyticShouldNotSendLogToServer</c> / URL testing trong <see cref="AnalyticConfig"/>.
    ///     Trộn hai mục đích thì sớm muộn có người bật cờ để chặn log rồi thắc mắc sao kho không có số.
    ///     <br/>⚠ KHÔNG lấy "máy root / emulator / nghi cheat" làm nguồn cho cờ: nhiều người chơi
    ///     thật dùng máy root, đánh dấu test là mất dữ liệu thật im lặng. Cờ này chỉ nói "log của
    ///     người làm game".
    ///     <br/>Hệ quả đã biết và chấp nhận: **build QA ở chế độ release trông như production**.
    ///     Muốn tách nhóm đó thì đổi bundle id, hoặc cho nó khỏi gửi bằng config — xem doc §5.2b
    ///     của spec.
    /// </summary>
    [Preserve]
    public class TestFlagInfoRepository : IFCustomInfoRepository
    {
        /// <inheritdoc cref="FCentralUserParamService.ParamKey.App.IS_TEST"/>
        public const string KEY_IS_TEST = FCentralUserParamService.ParamKey.App.IS_TEST;

        /// <inheritdoc cref="FCentralUserParamService.ParamKey.App.TEST_SOURCE"/>
        public const string KEY_TEST_SOURCE = FCentralUserParamService.ParamKey.App.TEST_SOURCE;

        public const string SOURCE_EDITOR = "editor";
        public const string SOURCE_DEV_BUILD = "dev_build";

        /// <summary>
        ///     Loại build hiện tại có phải môi trường của người làm game không. Tính mỗi lần đọc,
        ///     không cache xuống đĩa (xem doc lớp).
        /// </summary>
        public static bool IsTestBuild => Application.isEditor || Debug.isDebugBuild;

        /// <summary>Vì sao dòng này là test — null nếu đây là bản production.</summary>
        public static string Source =>
            Application.isEditor ? SOURCE_EDITOR : Debug.isDebugBuild ? SOURCE_DEV_BUILD : null;

        public Dictionary<string, object> GetInfo()
        {
            // Bản production: KHÔNG gửi key nào — "vắng = dữ liệu thật" là mặc định an toàn, và
            // dòng prod không tốn thêm byte nào.
            if (!IsTestBuild) return new Dictionary<string, object>();

            return new Dictionary<string, object>
            {
                [KEY_IS_TEST] = true,
                [KEY_TEST_SOURCE] = Source
            };
        }
    }
}
