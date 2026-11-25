/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-10-14
 */

using System;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    ///     Ngữ cảnh event live-op trần — dùng cho stream KHÔNG có param riêng (vd exposure UI đi
    ///     qua van gộp cụm). Điền tay, hoặc để <c>LiveOps.Stamp(ref, "card_collection")</c> điền.
    /// </summary>
    [Serializable]
    public sealed class LiveOpsRef : ALiveOpsParam
    {
    }
}
