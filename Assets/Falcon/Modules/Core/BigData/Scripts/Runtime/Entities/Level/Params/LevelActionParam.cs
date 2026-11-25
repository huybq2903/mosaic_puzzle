/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-09
 */

using System;
using System.Collections.Generic;
using JetBrains.Annotations;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    ///     Tham số của <c>Level.OnAction</c> cũ (1.7.0). Không còn lên dây: <c>f_sdk_level_action</c>
    ///     ngừng phát từ bản sau 1.7.0 (ký với loader 09/11). Lớp này chỉ giữ để code game đã gọi
    ///     vẫn build được; <see cref="LevelActionAdapter"/> chuyển nó sang hợp đồng mới.
    /// </summary>
    [Obsolete("f_sdk_level_action đã ngừng. Hồi sinh đã dùng: Level.OnRevive(...). Lời mời/nhận/từ chối: " +
              "Ui.OnImpression/OnClicked/OnDismissed với FUiSurface.ReviveOffer.")]
    [Serializable]
    public class LevelActionParam
    {
        [CanBeNull] public string playTurnId;
        public string action;
        [CanBeNull] public string boosterId;
        public bool offered;
        public bool accepted;
        public float? progressPct;
        [CanBeNull] public string payMethod;
        [CanBeNull] public Dictionary<string, object> extraMeta;
    }
}
