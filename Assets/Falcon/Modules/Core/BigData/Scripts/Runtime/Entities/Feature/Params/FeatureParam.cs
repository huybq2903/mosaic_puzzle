/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-02
 */

using System;
using System.Collections.Generic;
using Falcon.Helpers.Devkit;
using JetBrains.Annotations;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    ///     Một BƯỚC của một tính năng — payload của <see cref="FFeatureLog"/>. Một log duy nhất chở
    ///     mọi bước của mọi tính năng (hợp đồng <c>docs/design/FeatureContract.md</c> §3).
    ///     <br/>Sáu field riêng ở đây cộng ba field TRỤC TÍNH NĂNG của <see cref="ALiveOpsParam"/>
///     (lớp nền chở bảy field, bốn cái còn lại là trục event) là CHÍN field
    ///     THẬT — cố ý không để chúng đi <c>extraMeta</c>: extras trải phẳng bằng
    ///     <c>PutIfAbsentAndNotNull</c> (ai-ghi-trước-giữ) nên <c>extraMeta["feature_effect"]</c> và
    ///     field <see cref="featureEffect"/> sau bước Parse ra CÙNG MỘT TÊN — hai nguồn cho một cột,
    ///     mất số im lặng. Là field thật thì chúng cũng tự vào sổ <c>FWireReservedKeys</c> nên game
    ///     không đặt trùng được.
    ///     <br/>🔴 <b>Mọi field mang tiền tố <c>feature</c></b> (án owner 30/10): sổ cấm gom tên
    ///     bằng reflection, nên một field tên trần là LẤY MẤT tên đó của mọi game ở
    ///     <c>Common.Set</c> — <c>variant</c> (AB test), <c>joined_ts</c>, <c>unlocked_ts</c>,
    ///     <c>step</c>, <c>where</c> đều là tên game hay dùng (<c>where</c> thì ĐÃ là field wire của
    ///     <see cref="IapPurchaseAttemptParam"/>). Không tốn gì: luật Mapping của pipeline nạp là
    ///     RIÊNG TỪNG EVENT nên tên nào thành <c>sub_event</c>/<c>event_where</c> cũng được, miễn ký
    ///     (<c>feature_step → sub_event</c>, <c>feature_where → event_where</c> — loader ký 30/10).
    /// </summary>
    [Serializable]
    public class FeatureParam : ALiveOpsParam
    {
        /// <summary>
        ///     Bước — một trong <see cref="FFeatureStep"/>, hoặc chuỗi tự do cho bước RIÊNG của
        ///     tính năng (SDK không kiểm, nên tính năng mới không phải chờ bản SDK).
        /// </summary>
        [NotNull] public string featureStep = UNKNOWN;

        /// <summary>
        ///     DẠNG/HẠNG của NGƯỜI GỬI trong thực thể tại lúc làm — guild: vai
        ///     <c>leader</c>/<c>co_leader</c>/<c>member</c>; quà hằng ngày:
        ///     <c>free</c>/<c>ad</c>/<c>vip</c>; nâng cấp: nhánh đang theo.
        ///     <br/>Tên trung tính có chủ đích (án owner 29/10): chỉ thực thể nhiều người mới có
        ///     "vai", tính năng khác dùng cùng chỗ này cho nhãn phân loại của mình.
        ///     <br/>Bắt buộc cho bước <see cref="FFeatureStep.VariantChanged"/>.
        /// </summary>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string featureVariant;

        /// <summary>
        ///     Bản tin này đổi TRẠNG THÁI tính năng của người gửi thế nào — một trong
        ///     <see cref="FFeatureEffect"/>, vắng = không đổi gì.
        ///     <br/>🔴 Profile bên kho chỉ đọc field NÀY, <b>không</b> suy từ
        ///     <see cref="featureStep"/> (hợp đồng kho §7.2: "profile không biết tên bước nào").
        ///     <br/>Ai dựng bản tin thì người đó điền: SDK tự điền cho bước chung + bước nhóm gửi từ
        ///     client (<see cref="FFeatureEffect.DefaultFor"/>), game điền cho bước riêng, và SERVER
        ///     tự điền cho mọi bản gửi hộ — server không chạy SDK. Để trống thì SDK điền bằng
        ///     <c>??=</c> nên truyền tay vẫn thắng.
        /// </summary>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string featureEffect;

        /// <summary>LỐI VÀO / vị trí phát sinh bước. Loader map sang <c>event_where</c>.</summary>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string featureWhere;

        /// <summary>
        ///     Mốc người chơi vào thực thể — epoch millis UTC, <b>giờ SERVER</b>, lấy từ dữ liệu
        ///     tính năng vừa tải về.
        ///     <br/>🔴 <b>Là ngày vào LẦN HIỆN TẠI, không phải lần vào đầu tiên</b> (hợp đồng kho,
        ///     bản Team §2.2): vào lại đúng thực thể cũ thì gửi NGÀY VÀO LẠI. Gửi ngày đầu tiên là
        ///     báo thâm niên dài hơn thật, và <b>không chỗ nào bắt được</b> — SDK chỉ chở con số
        ///     server đưa, không kiểm được.
        ///     <br/>Kho dùng nó làm GIÁ TRỊ thâm niên (<c>since_ts</c>), không dùng để xếp thứ tự
        ///     (thứ tự xếp bằng <c>(server_receive_time, client_sent_time)</c> — chốt 30/10). Nên
        ///     thiếu là mất một con số báo cáo, không phải mất tính đúng ⇒ chỉ CẢNH BÁO, không chặn.
        /// </summary>
        [FKey(RemoveIfNull = true)] public long? featureJoinedTs;

        /// <summary>Mốc MỞ KHOÁ tính năng — epoch millis UTC. Không biết thì bỏ key.</summary>
        [FKey(RemoveIfNull = true)] public long? featureUnlockedTs;

        /// <summary>
        ///     Key-value riêng của bước — trải PHẲNG vào payload. Tham số riêng của từng tính năng
        ///     (<c>guild_*</c>, <c>target_*</c>, <c>help_item</c>, <c>request_id</c>…) đi đường này
        ///     và giữ luật tên "chủ của thứ được đo" (hợp đồng kho §4.2) — <b>không</b> thêm tiền tố
        ///     <c>feature_</c>, vì tiền tố đó chỉ để tránh sổ cấm mà extras không chạm sổ cấm.
        /// </summary>
        [FKey(RemoveIfNull = true)] [CanBeNull] public Dictionary<string, object> extraMeta;

        public override void CorrectValues()
        {
            base.CorrectValues();
            featureStep = CheckNonBlank(featureStep, nameof(featureStep));

            // Chỉ soi bản state: bước khác (help_give, chat…) có thực thể mà không mang mốc vào là
            // BÌNH THƯỜNG — mốc đó là chuyện của bản trạng thái, không phải của từng bước.
            if (featureStep == FFeatureStep.State
                && !string.IsNullOrEmpty(featureInstanceId)
                && !featureJoinedTs.HasValue)
                AnalyticLogger.Instance.Warning(
                    $"Feature '{feature}': bản state đang ở thực thể '{featureInstanceId}' mà thiếu " +
                    "featureJoinedTs — kho sẽ đặt thâm niên = lúc bản tin tới cổng, nên thành viên " +
                    "lâu năm trông như vừa vào. VẪN GỬI (chặn ở client là giấu DQ của kho).");
        }

        public override Dictionary<string, object> ToDictionary()
        {
            var dictionary = base.ToDictionary();
            dictionary.Remove(nameof(extraMeta));

            // Cửa soi key extras nay dùng chung cho MỌI stream (FExtraMeta) — trước lượt 06/11 chỉ
            // hợp đồng này có, mười hai chỗ còn lại mất key im lặng mà không ai hô.
            // Ca thật đã lường của riêng hợp đồng này: bước `unlock` và `leave` cần
            // featureUnlockedTs / featureJoinedTs, mà overload tiện tay Feature.Step(...) không có
            // hai tham số đó ⇒ người gọi rất dễ với sang extraMeta.
            return dictionary.MergeExtras(extraMeta, this, $"Feature '{feature}'",
                "Overload tiện tay không có tham số tương ứng thì dùng " +
                "Feature.Step(new FeatureParam{…}).");
        }
    }
}
