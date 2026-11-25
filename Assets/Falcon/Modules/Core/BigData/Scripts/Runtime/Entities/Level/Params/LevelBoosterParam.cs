/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-10
 */

using System;
using System.Collections.Generic;
using Falcon.Helpers.Devkit;
using JetBrains.Annotations;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    ///     Một lần DÙNG BOOSTER TRONG VÁN (sau Start, trước kết cục) — payload của <see cref="FLevelBoosterLog"/>, ký
    ///     với loader 10/11. Booster chọn TRƯỚC ván vẫn ở <c>preBoostersUsed</c> của dòng Start; event này không chở.
    ///     <br/>🔴 Luật đọc: event này trả lời <i>dùng ở đoạn nào của ván</i> và <i>booster có từ đâu</i>. Còn <i>dùng
    ///     bao nhiêu lần</i> vẫn đọc <c>boostersUsed</c> của dòng level — <b>đừng cộng hai nguồn</b>.
    ///     <br/>Có dòng là đã dùng: popup gợi ý dùng booster là sự kiện của bề mặt Ui
    ///     (<see cref="FUiSurface.BoosterOffer"/>). Không kế thừa <c>ALiveOpsParam</c> — ngữ cảnh nằm trên dòng level
    ///     cùng lượt, nối bằng <see cref="playTurnId"/>.
    /// </summary>
    [Serializable]
    public class LevelBoosterParam : FParam
    {
        /// <summary>Lượt chơi đang mở — SDK tự gắn (dùng booster giữa ván thì lượt luôn mở).</summary>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string playTurnId;

        /// <summary>
        ///     Mã booster theo sổ <c>Resource.RegisterBooster</c> — BẮT BUỘC. Thiếu thì SDK BỎ DÒNG: "đã dùng một
        ///     booster nào đó" không trả lời được câu nào. Mã lạ: cảnh báo log dev, vẫn gửi.
        /// </summary>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string boosterId;

        /// <summary>% tiến độ ván lúc dùng, <c>[0, 100]</c>. Ngoài khoảng: BỎ KEY, không kẹp.</summary>
        [FKey(RemoveIfNull = true)] public float? progressPct;

        /// <summary>
        ///     Booster lúc dùng CÓ TỪ ĐÂU — không phải "trả bằng gì": <c>inventory</c> (có sẵn trong kho đồ) ·
        ///     <c>currency</c> (mua tại chỗ) · <c>ad</c> (xem QC lấy) · <c>free</c> (game cho). Tập đóng
        ///     <see cref="FPayMethod"/>. Không biết thì để vắng.
        /// </summary>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string payMethod;

        [FKey(RemoveIfNull = true)] [CanBeNull] public Dictionary<string, object> extraMeta;

        /// <summary>Dòng có đáng gửi không — thiếu mã booster thì không.</summary>
        public bool HasBooster => !string.IsNullOrWhiteSpace(boosterId);

        public override void CorrectValues()
        {
            base.CorrectValues();
            progressPct = LevelActionRules.Progress(progressPct, "LevelBooster");
            payMethod = LevelActionRules.PayMethod(payMethod, "LevelBooster");
            if (HasBooster) FBoosterRegistry.WarnIfUnknown(boosterId, "f_sdk_level_action_booster");
        }

        public override Dictionary<string, object> ToDictionary()
        {
            var dictionary = base.ToDictionary();
            dictionary.Remove(nameof(extraMeta));
            return dictionary.MergeExtras(extraMeta, this, "LevelBooster",
                "Overload tiện tay không có tham số tương ứng thì dùng " +
                "Level.OnBooster(new LevelBoosterParam{…}).");
        }
    }
}
