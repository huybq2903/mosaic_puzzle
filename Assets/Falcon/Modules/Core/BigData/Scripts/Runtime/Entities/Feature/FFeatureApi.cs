/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-02
 */

using System.Collections.Generic;
using JetBrains.Annotations;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    ///     TÍNH NĂNG — <c>FalconBigDataController.Feature</c>. Một hợp đồng cho MỌI tính năng
    ///     (hợp đồng đầy đủ: <c>docs/design/FeatureContract.md</c>).
    ///     <br/>Ba việc của dev game, không hơn:
    ///     <br/>1. <see cref="State"/> ngay sau khi tải xong dữ liệu tính năng TỪ SERVER — một bản
    ///     mỗi lượt chạy app, thêm một bản mỗi lần đồng bộ lại.
    ///     <br/>2. <see cref="Step(string,string,string,string,int?,string,string,Dictionary{string,object})"/>
    ///     ở từng bước: <c>unlock</c>, <c>open</c>, <c>join</c>, <c>leave</c>, <c>claim</c>… hoặc
    ///     bước riêng của tính năng (chuỗi tự do).
    ///     <br/>3. <see cref="Stamp"/> lên param của dòng tài nguyên / ad / iap / offer / custom /
    ///     level phát sinh VÌ tính năng — để mọi stream nối được về đúng (tính năng, thực thể).
    ///     <br/>Không phải đăng ký tên tính năng với ai (án owner 30/10): danh tính nằm ở giá trị.
    ///     <br/>⚠ SDK <b>không</b> tự gọi <see cref="State"/> hộ: chỉ game biết lúc nào nó có sự
    ///     thật từ server, và dựng bản đó từ cache là báo một trạng thái có thể đã cũ.
    /// </summary>
    public class FFeatureApi
    {
        private readonly FeatureLogService _service;

        internal FFeatureApi(FeatureLogService service)
        {
            _service = service;
        }

        /// <summary>
        ///     Ghi một bước của tính năng.
        ///     <br/><paramref name="effect"/> để trống thì SDK tự điền NẾU bước nằm trong từ vựng
        ///     chung/nhóm (<see cref="FFeatureEffect.DefaultFor"/>); bước RIÊNG của tính năng thì
        ///     game phải truyền, vì SDK không được đoán hộ.
        ///     <br/>Không có van lọc "một lần mỗi đợt" như live-op — tính năng luôn bật nên không có
        ///     đợt để làm khoá van. Bước dày đặc thì game tự gộp trước khi gọi.
        ///     <br/>Trả false khi bản tin bị bỏ (thiếu <paramref name="feature"/>); gọi xong bỏ qua
        ///     giá trị trả về cũng đúng.
        /// </summary>
        public bool Step(
            string feature, string step, [CanBeNull] string instanceId = null,
            [CanBeNull] string variant = null, int? stage = null, [CanBeNull] string where = null,
            [CanBeNull] string effect = null,
            [CanBeNull] Dictionary<string, object> extraMeta = null)
        {
            return _service.LogStep(new FeatureParam
            {
                feature = feature,
                featureStep = step,
                featureInstanceId = instanceId,
                featureVariant = variant,
                featureStage = stage,
                featureWhere = where,
                featureEffect = effect,
                extraMeta = extraMeta
            });
        }

        /// <inheritdoc cref="Step(string,string,string,string,int?,string,string,Dictionary{string,object})"/>
        public bool Step(FeatureParam param)
        {
            return _service.LogStep(param);
        }

        /// <summary>
        ///     Báo TRẠNG THÁI hiện tại của một tính năng — gọi ngay sau khi tải xong dữ liệu tính
        ///     năng TỪ SERVER (không dựng từ cache). Một bản mỗi lượt chạy app, thêm một bản mỗi lần
        ///     đồng bộ lại. Luật đầy đủ: hợp đồng kho §4.3.
        ///     <br/>🔴 <paramref name="instanceId"/> <b>không có giá trị mặc định</b> vì người gọi
        ///     buộc phải quyết định — ba lựa chọn là ba lời khai khác nhau:
        ///     <br/>· <c>null</c> ⇒ vắng key ⇒ "tính năng không có khái niệm thực thể", kho không chạm;
        ///     <br/>· <c>""</c> ⇒ "đã mở khoá, KHÔNG ở thực thể nào" ⇒ kho XOÁ thực thể đang giữ;
        ///     <br/>· <c>"T_8812"</c> ⇒ đang ở thực thể đó.
        ///     <br/>Quên gửi <c>""</c> cho người vừa rời nhóm lúc offline là để họ ở lại thực thể cũ
        ///     trong kho, im lặng — xem <see cref="ALiveOpsParam.featureInstanceId"/>.
        ///     <br/><paramref name="stage"/> có ở đây vì luật <c>sync</c> bên kho GHI ĐÈ tiến độ nếu
        ///     bản tin có: tính năng một người (quà hằng ngày, chuỗi nâng cấp) báo tiến độ ngay
        ///     trong bản này, khỏi cần thêm một bước.
        /// </summary>
        public bool State(
            string feature, [CanBeNull] string instanceId, long? joinedTs = null,
            long? unlockedTs = null, [CanBeNull] string variant = null, int? stage = null,
            [CanBeNull] Dictionary<string, object> extraMeta = null)
        {
            return _service.LogStep(new FeatureParam
            {
                feature = feature,
                featureStep = FFeatureStep.State,
                featureInstanceId = instanceId,
                featureJoinedTs = joinedTs,
                featureUnlockedTs = unlockedTs,
                featureVariant = variant,
                featureStage = stage,
                extraMeta = extraMeta
            });
        }

        /// <summary>
        ///     Đóng ngữ cảnh tính năng lên param của một stream khác (resource / ad / iap / offer /
        ///     custom / level): điền <c>feature</c> + thực thể + tiến độ nếu dòng đó thuộc một mốc.
        ///     <br/>Chỉ điền chỗ đang TRỐNG — game truyền tay thì tay thắng.
        ///     <br/>⚠ Gọi hàm này là một LỜI KHAI: "dòng này phát sinh VÌ tính năng đó". Không gọi =
        ///     "không thuộc tính năng nào", là lời khai ĐÚNG chứ không phải thiếu dữ liệu.
        ///     <br/>⚠ Khác <c>LiveOps.Stamp</c>: <paramref name="instanceId"/> phải truyền TẠI CHỖ
        ///     GỌI, vì SDK không giữ sổ "thực thể đang ở" để tra hộ (§7).
        /// </summary>
        public void Stamp(
            ALiveOpsParam param, string feature, [CanBeNull] string instanceId = null, int? stage = null)
        {
            param?.StampFeature(feature, instanceId, stage);
        }
    }
}
