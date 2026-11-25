/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-09
 */

using System;
using System.Collections.Generic;
using Falcon.Helpers.Devkit;
using JetBrains.Annotations;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    ///     Một lần HỒI SINH ĐÃ DÙNG — payload của <see cref="FLevelReviveLog"/>, ký với loader 09/11.
    ///     Có dòng là đã dùng: không có <c>offered</c>/<c>accepted</c> — lời mời, nhận, từ chối là
    ///     các sự kiện của popup, đi vào cụm Ui trên bề mặt <see cref="FUiSurface.ReviveOffer"/>.
    ///     <br/>⚠ <b>Cố ý KHÔNG kế thừa <c>ALiveOpsParam</c></b>: ngữ cảnh event/tính năng đã nằm
    ///     trên dòng level cùng lượt, nối bằng <see cref="playTurnId"/> — chở lại là nhân bản một sự
    ///     thật lên stream khác.
    /// </summary>
    [Serializable]
    public class LevelReviveParam : FParam
    {
        /// <summary>
        ///     Lượt chơi được hồi sinh. Không truyền thì SDK điền bằng lượt ĐANG MỞ.
        ///     <br/>🔴 Luật tích hợp (chốt 04/09): game chỉ báo <c>Fail</c> khi đã HẾT đường hồi
        ///     sinh — nên trong lúc popup hồi sinh đang hiện, lượt vẫn mở và id tự gắn đúng; hồi sinh
        ///     xong mà thắng thì <c>Pass</c> cũng thuộc cùng lượt. Game báo Fail trước rồi mới mời thì
        ///     dòng này VẮNG id: SDK không đoán id của lượt đã đóng (§C), vì đoán sai là nối hồi sinh
        ///     vào nhầm ván.
        /// </summary>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string playTurnId;

        /// <summary>% tiến độ ván lúc hồi sinh, <c>[0, 100]</c>. Ngoài khoảng: BỎ KEY, không kẹp.</summary>
        [FKey(RemoveIfNull = true)] public float? progressPct;

        /// <summary>Trả bằng gì — tập đóng <see cref="FPayMethod"/>. Không biết thì để vắng.</summary>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string payMethod;

        /// <summary>
        ///     Loại hồi sinh — VOCAB MỞ của từng game, snake_case, ổn định (<c>extra_moves</c>, <c>extra_time</c>,
        ///     <c>clear_bombs</c>…). Ký với loader 10/11. Không field nào đổi nghĩa theo nó.
        ///     <br/>⚠ Cố ý KHÔNG có field "số lượng": đơn vị của nó đổi theo loại (lượt đi hay giây), tức một field bị
        ///     field khác điều khiển nghĩa. Cần tách "+5" với "+10" thì đưa vào tên loại (<c>extra_moves_5</c>).
        /// </summary>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string reviveType;

        [FKey(RemoveIfNull = true)] [CanBeNull] public Dictionary<string, object> extraMeta;

        public override void CorrectValues()
        {
            base.CorrectValues();
            progressPct = LevelActionRules.Progress(progressPct, "LevelRevive");
            payMethod = LevelActionRules.PayMethod(payMethod, "LevelRevive");
            reviveType = LevelActionRules.OpenVocab(reviveType);
        }

        public override Dictionary<string, object> ToDictionary()
        {
            var dictionary = base.ToDictionary();
            dictionary.Remove(nameof(extraMeta));
            return dictionary.MergeExtras(extraMeta, this, "LevelRevive",
                "Overload tiện tay không có tham số tương ứng thì dùng " +
                "Level.OnRevive(new LevelReviveParam{…}).");
        }
    }
}
