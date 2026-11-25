/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-09
 */

using System;
using UnityEngine.Scripting;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Dấu "lượt xả cụm Ui của phiên trước đã MẤT" — bắn ở lần mở app sau một lần bị tắt ngang mà
    /// còn cụm chưa xả (ký với loader 09/11, tách khỏi cụm Ui). <b>Không field riêng nào.</b>
    /// <br/>Tách ra vì nó không phải một bề mặt Ui: ở event cũ nó là một giá trị <c>ui_action</c>
    /// không có <c>surface_id</c>, tức giá trị của một field quyết định field khác được phép vắng.
    /// <br/>⚠ Luật đọc NGƯỢC với cụm Ui: ở event này <c>count(*)</c> là ĐÚNG — một dòng là một phiên
    /// mất. Và là <b>CHẶN DƯỚI</b>: dấu ghi đĩa có thể trượt, xem <see cref="UiFlushLossMarker"/>.
    /// </summary>
    [Serializable]
    public class FUiFlushLostLog : AFalconLog
    {
        [Preserve]
        public FUiFlushLostLog()
        {
        }

        public override string Event => "f_sdk_ui_flush_lost";
    }
}
