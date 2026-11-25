/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-09
 */

using JetBrains.Annotations;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Chuyển lời gọi <c>Level.OnAction</c> cũ (1.7.0) sang hợp đồng ký 09/11 — đúng bảng chuyển đã
    /// gửi loader. Tách thành hàm thuần để test được: ctor của <see cref="FLevelApi"/> là
    /// <c>internal</c>.
    /// </summary>
    public static class LevelActionAdapter
    {
#pragma warning disable 618
        /// <returns>
        ///     <c>revive</c>: param cần ghi, hoặc <c>null</c> nếu lời gọi cũ không còn sinh dòng nào.
        ///     <c>warning</c>: câu cảnh báo cho dev khi lời gọi bị bỏ, hoặc <c>null</c>.
        /// </returns>
        public static (LevelReviveParam revive, string warning) ToRevive([CanBeNull] LevelActionParam param)
        {
            if (param == null) return (null, null);

            if (param.action == FLevelAction.Revive)
            {
                if (!param.accepted)
                    return (null,
                        "Level.OnAction(revive, accepted: false) không còn sinh dòng nào: lời mời / từ chối " +
                        $"hồi sinh giờ là cụm Ui — gọi Ui.OnImpression / Ui.OnDismissed với bề mặt " +
                        $"'{FUiSurface.ReviveOffer}'.");

                return (new LevelReviveParam
                {
                    playTurnId = param.playTurnId,
                    progressPct = param.progressPct,
                    payMethod = param.payMethod,
                    extraMeta = param.extraMeta
                }, null);
            }

            if (param.action == FLevelAction.Booster)
                return (null,
                    "Level.OnAction(booster) không còn sinh dòng nào: lượt dùng booster đã có ở dòng level " +
                    "(preBoostersUsed / boostersUsed) và dòng tài nguyên (resourceWhen = booster_use).");

            return (null, $"Level.OnAction: action '{param.action}' không thuộc tập đã ký — bỏ.");
        }
#pragma warning restore 618
    }
}
