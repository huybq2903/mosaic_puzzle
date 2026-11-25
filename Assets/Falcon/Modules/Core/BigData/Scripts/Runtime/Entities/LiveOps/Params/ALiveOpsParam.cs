/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-10-14
 */

using Falcon.Helpers.Devkit;
using JetBrains.Annotations;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    ///     BẢY param ngữ cảnh dùng chung — <b>HAI TRỤC TRỰC GIAO</b> đứng cùng một lớp:
    ///     <br/>• <b>Trục event live-op</b> (4 field, hợp đồng <c>docs/design/LiveOpsEventContract.md</c>)
    ///     <br/>• <b>Trục tính năng</b> (3 field, hợp đồng <c>docs/design/FeatureContract.md</c>)
    ///     <br/>Param nào kế thừa lớp này là stream đó "khai được" cả hai — và khai là <b>LỜI KHAI
    ///     NHÂN QUẢ của game</b>, không phải suy theo thời điểm: SDK không bao giờ tự đoán dòng này
    ///     thuộc event nào hay sinh ra vì tính năng nào. Riêng trục tính năng, SDK còn KHÔNG giữ sổ
    ///     "đang ở tính năng nào" để tự dán, vì <c>GameEvent.Emit</c> chạy đồng bộ nên một chuỗi
    ///     phản ứng sẽ đóng dấu sai lên log của hệ khác (án 05/09 — ĐỒNG THỜI ≠ NGUYÊN NHÂN).
    ///     <br/><br/>⚠ <b>MỘT LỚP KHÔNG CÓ NGHĨA LÀ MỘT TRỤC PHỤ THUỘC TRỤC KIA.</b> Tính năng chạy
    ///     SONG SONG với event trong khi các event loại trừ nhau; một dòng mang cả hai là bình
    ///     thường (xin thẻ trong bang hội giữa mùa card collection), mang một trục cũng bình
    ///     thường, mang không trục nào cũng bình thường. Gộp một lớp là hệ quả của SERIALIZER, không
    ///     phải lời khai về nghiệp vụ: <c>FKeyService.Encode</c> chỉ duyệt <b>public instance
    ///     FIELD</b> (property <c>=&gt;</c> không bao giờ lên wire), nên interface — thứ duy nhất mô
    ///     hình được "hai trục tuỳ chọn, độc lập" — không chở được field; còn composition thì ra
    ///     JSON lồng, mà bảy cái này phải là CỘT PHẲNG cấp một. C# lại chỉ cho kế thừa một lớp. Nên
    ///     chuỗi kế thừa tuyến tính là CƠ CHẾ duy nhất cho wire phẳng mà không phải viết
    ///     <c>ToDictionary</c> ở từng param. Trước 05/11 hai trục là hai tầng
    ///     (<c>AFeatureParam : ALiveOpsParam</c>) — tầng đó khai "tính năng LÀ MỘT event", sai hẳn,
    ///     nên đã gộp.
    ///     <br/>Vì sao không nhét vào <see cref="FParam"/>: param nào KHÔNG thuộc event và KHÔNG do
    ///     tính năng nào gây ra (session, permission, property…) thì bảy field này <b>không tồn
    ///     tại</b>, nên không ai gắn sai được — rẻ hơn viết cảnh báo rồi hy vọng người ta đọc.
    ///     <br/>Vắng <see cref="liveOpsEvent"/> = dòng này không phải của event nào. Vắng
    ///     <see cref="feature"/> = dòng này không phát sinh vì tính năng nào.
    /// </summary>
    public abstract class ALiveOpsParam : FParam
    {
        #region Trục event live-op

        /// <summary>
        ///     Danh tính event, ĐỂ TRẦN (không tiền tố <c>event_</c>): <c>card_collection</c>,
        ///     <c>master_league</c>, <c>race</c>… Trùng giá trị <c>liveOpsEvent</c> trên
        ///     <c>f_sdk_live_ops_data</c> — đó là khoá nối hai bên.
        /// </summary>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string liveOpsEvent;

        /// <summary>
        ///     ĐỢT mà dòng này THUỘC VỀ — không phải "đợt đang chạy". Nhận thưởng mùa 5 trong mùa 6
        ///     thì khai mùa 5, server mới đọc đúng.
        ///     <br/>Chưa biết mã đợt thì <b>hoãn gửi</b> chờ mã về; hết cơ hội thì gửi rỗng —
        ///     tuyệt đối không bịa (bài học Endless Treasure E2).
        /// </summary>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string liveOpsCycleId;

        /// <summary>
        ///     LƯỢT trong đợt — chỉ event cho chơi lại nhiều lượt (Race: một bảng đấu = một lượt).
        ///     Vắng = đợt chỉ có một lượt.
        /// </summary>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string liveOpsRoundId;

        /// <summary>
        ///     MỐC mà dòng này THUỘC VỀ (quà của mốc 3, ván của chặng 2) — nhân quả, không phải
        ///     "lúc đó user đang ở mốc mấy". Không chắc thì để trống.
        /// </summary>
        [FKey(RemoveIfNull = true)] public int? liveOpsStage;

        #endregion

        #region Trục tính năng

        /// <summary>
        ///     Danh tính tính năng, ĐỂ TRẦN: <c>guild</c>, <c>daily_gift</c>, <c>lucky_wheel</c>…
        ///     Từ vựng ở hợp đồng kho §4.1, snake_case.
        ///     <br/>SDK <b>không kiểm theo danh sách</b> (án owner 30/10): kiểm là bắt game đăng ký
        ///     tên trước, đúng cái ma sát mà hợp đồng live-op sinh ra để dẹp — và lấy danh sách từ
        ///     remote config thì phiên đầu chưa có config là SDK chặn tên hợp lệ. Lỗi gõ sinh tính
        ///     năng song song (<c>guild</c> vs <c>guilds</c>) thì DQ bên kho bắt. Ở đây chỉ CẢNH BÁO
        ///     nếu không phải snake_case, vì chữ HOA còn bị bước Parse xé từng ký tự.
        /// </summary>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string feature;

        /// <summary>
        ///     Id THỰC THỂ mà dòng này thuộc về (bang hội, phòng chơi, nhóm co-op). Tính năng một
        ///     người thì không có.
        ///     <br/>🔴 <b><c>null</c> và <c>""</c> là HAI nghĩa khác nhau</b>, và cả hai đều là lời
        ///     khai: <c>null</c> ⇒ vắng key ⇒ "tính năng không có khái niệm thực thể", profile KHÔNG
        ///     CHẠM; <c>""</c> ⇒ "đã mở khoá, KHÔNG ở thực thể nào" ⇒ profile XOÁ thực thể. Đây là
        ///     <b>ngoại lệ có tên</b> của luật "không có giá trị thì bỏ key" (hợp đồng kho §4.2).
        ///     <br/>🔴 Vì vậy <b>CẤM</b> hạ <c>""</c> về null ở field này — khuôn <c>FAdLoadLog</c>
        ///     (chuẩn hoá cho <c>RemoveIfNull</c> bỏ) không được áp vào đây. Mất <c>""</c> thì mọi
        ///     bản <c>state</c> của người không ở thực thể thành "vắng key = không chạm", và người
        ///     bị kick lúc offline quay về thực thể cũ — im lặng, không chỗ nào báo. Ghim bằng test.
        /// </summary>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string featureInstanceId;

        /// <summary>
        ///     TIẾN ĐỘ của tính năng một người (chuỗi nâng cấp, ngày thứ mấy của quà hằng ngày).
        ///     Vắng = tính năng không có tiến độ.
        ///     <br/>Mọi bản tin <c>f_sdk_feature</c> mang field này đều cập nhật tiến độ bên kho,
        ///     không riêng bản <c>state</c> (hợp đồng kho §7.2).
        /// </summary>
        [FKey(RemoveIfNull = true)] public int? featureStage;

        #endregion

        /// <summary>
        ///     Đóng dấu ngữ cảnh TÍNH NĂNG lên chính param này — chỉ điền chỗ đang TRỐNG
        ///     (<c>??=</c>), nên game truyền tay thì TAY THẮNG.
        ///     <br/>Đường của dev game là <c>FalconBigDataController.Feature.Stamp</c>; hàm này là
        ///     chỗ ở DUY NHẤT của luật đó (API chỉ delegate một dòng) — ctor của mọi <c>F*Api</c>
        ///     đều <c>internal</c> nên test không dựng được API, mà bất biến "không đè tay" thì phải
        ///     ghim được bằng test CHẠY THẬT, không ghim bằng mắt.
        ///     <br/><paramref name="instanceId"/> phải truyền TẠI CHỖ GỌI: khác
        ///     <c>LiveOps.Stamp</c>, ở đây không có <c>CurrentCycleId</c> tương đương để SDK tra hộ
        ///     (§7 — không có scope ngầm).
        /// </summary>
        public void StampFeature(string feature, [CanBeNull] string instanceId = null, int? stage = null)
        {
            if (string.IsNullOrEmpty(feature)) return;

            this.feature ??= feature;
            featureInstanceId ??= instanceId;
            featureStage ??= stage;
        }

        /// <summary>
        ///     Đóng ngữ cảnh EVENT LIVE-OP lên chính param này — chỉ điền chỗ đang TRỐNG
        ///     (<c>??=</c>) nên game truyền tay thì TAY THẮNG.
        ///     <br/>Đường của dev game là <c>FalconBigDataController.LiveOps.Stamp</c>; hàm này là
        ///     chỗ ở DUY NHẤT của luật đó (API chỉ tra mã đợt rồi delegate) — ctor của mọi
        ///     <c>F*Api</c> đều <c>internal</c> và module KHÔNG có <c>InternalsVisibleTo</c>, nên
        ///     test (assembly riêng) không dựng được API. Bất biến dưới đây phải ghim được bằng test
        ///     CHẠY THẬT, không ghim bằng mắt. Y khuôn <see cref="StampFeature"/>.
        ///     <br/>🔴 <paramref name="roundId"/> <b>chỉ đến từ đối số</b>. SDK không tra hộ mã lượt
        ///     (sửa 05/11): "lượt cuối SDK tình cờ thấy" không phải lời khai về dòng này, và vì
        ///     <c>??=</c> coi <c>null</c> là ô trống nên tra hộ đồng nghĩa với việc game <b>không
        ///     có cách nào</b> khai "dòng này không thuộc lượt nào". Im lặng = không có lượt.
        ///     <br/>Mã đợt thì khác, và API vẫn tra: <c>DeclareCycle</c> là <b>game đã nói</b>, mỗi
        ///     event chỉ có một đợt đang chạy ⇒ SDK nhớ hộ, không đoán.
        /// </summary>
        public void StampLiveOps(
            string liveOpsEvent, [CanBeNull] string cycleId = null, int? stage = null,
            [CanBeNull] string roundId = null)
        {
            if (string.IsNullOrEmpty(liveOpsEvent)) return;

            this.liveOpsEvent ??= liveOpsEvent;
            liveOpsCycleId ??= cycleId;
            liveOpsStage ??= stage;
            liveOpsRoundId ??= roundId;
        }

        /// <summary>
        ///     Chép CẢ HAI trục ngữ cảnh từ một param/ref khác — chỉ điền chỗ đang TRỐNG
        ///     (<c>??=</c>) nên giá trị đã có trên param này luôn thắng, cùng khuôn
        ///     <c>LiveOps.Stamp</c> và <see cref="StampFeature"/>.
        ///     <br/>⚠ <b>Đây là chỗ DUY NHẤT được phép liệt kê bảy field ngữ cảnh để chép.</b> Trước
        ///     05/11 việc đó nằm rải ở BA nơi ngoài lớp này — <c>ApplyLiveOps</c> của module
        ///     InAppValidation, <see cref="PurchaseAttemptState"/>, log cụm Ui —
        ///     nên mỗi lần lớp nền mọc thêm field là cả ba chỗ mục cùng lúc và <b>im lặng</b>: đúng
        ///     ca ba field <c>feature*</c> bị rơi khỏi dòng <c>started</c> của IAP ngay trong lượt
        ///     gộp 05/11. Thêm field vào lớp này thì <b>PHẢI</b> thêm một dòng ở đây — test ghim
        ///     bằng reflection nên quên là ĐỎ, không phải chờ ai soi.
        ///     <br/><see cref="FUiExposureLog"/> vẫn không dùng được hàm này (nó là
        ///     <c>AFalconLog</c>, không phải param) nên còn là chỗ chép tay thứ ba — có test riêng.
        ///     <br/>⚠ Danh sách "ba chỗ" ở trên từng SÓT một chỗ thứ tư: clone context trong
        ///     <see cref="ExtendResourceLog"/> chép tay và rơi cả bảy field suốt từ lúc ResourceParam kế thừa
        ///     lớp này (báo lỗi 11/11) — nay cũng gọi hàm này. Ai clone một param kế thừa lớp này thì gọi
        ///     <see cref="CopyContextFrom"/>, đừng kê field.
        /// </summary>
        public void CopyContextFrom([CanBeNull] ALiveOpsParam source)
        {
            if (source == null) return;

            liveOpsEvent ??= source.liveOpsEvent;
            liveOpsCycleId ??= source.liveOpsCycleId;
            liveOpsRoundId ??= source.liveOpsRoundId;
            liveOpsStage ??= source.liveOpsStage;
            feature ??= source.feature;
            featureInstanceId ??= source.featureInstanceId;
            featureStage ??= source.featureStage;
        }

        public override void CorrectValues()
        {
            // base rỗng ở FParam, nhưng gọi cho đúng khuôn: ai chen thêm một tầng vào giữa mà lớp
            // này không chuyền tiếp thì luật của tầng đó mất người chạy (đúng cái bẫy 02/11).
            base.CorrectValues();
            liveOpsStage = CheckNumberNonNegative(liveOpsStage, nameof(liveOpsStage));
            featureStage = CheckNumberNonNegative(featureStage, nameof(featureStage));

            if (!string.IsNullOrEmpty(feature) && !IsSnakeCase(feature))
                AnalyticLogger.Instance.Warning(
                    $"Feature '{feature}': tên tính năng phải snake_case (hợp đồng kho §4.1) — chữ " +
                    "HOA bị bước Parse của pipeline nạp xé từng ký tự, nên cột trong kho sẽ khác " +
                    "tên bạn gửi. Cảnh báo, KHÔNG chặn: tên hợp lệ mà SDK chặn thì mất dữ liệu.");
        }

        private static bool IsSnakeCase(string value)
        {
            foreach (var c in value)
                if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '_'))
                    return false;

            return true;
        }
    }
}
