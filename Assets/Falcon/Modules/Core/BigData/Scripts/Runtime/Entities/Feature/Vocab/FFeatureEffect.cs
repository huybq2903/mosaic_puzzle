/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-02
 */

using JetBrains.Annotations;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    ///     Năm giá trị của <see cref="FeatureParam.featureEffect"/> — "bản tin này đổi trạng thái
    ///     tính năng của người gửi thế nào". Vắng = không đổi gì.
    ///     <br/>Profile bên kho chỉ đọc field đó, KHÔNG suy từ tên bước (hợp đồng kho §7.2), nên ai
    ///     dựng bản tin thì người đó điền. Bảng đầy đủ ba cột (SDK / game / server) ở hợp đồng kho
    ///     §4.2; <see cref="DefaultFor"/> là đúng cột SDK.
    /// </summary>
    public static class FFeatureEffect
    {
        /// <summary>Mở khoá tính năng.</summary>
        public const string Unlock = "unlock";

        /// <summary>Vào một thực thể.</summary>
        public const string Enter = "enter";

        /// <summary>Ra khỏi thực thể.</summary>
        public const string Exit = "exit";

        /// <summary>Đổi dạng/hạng trong thực thể.</summary>
        public const string SetVariant = "set_variant";

        /// <summary>Đồng bộ lại toàn bộ trạng thái (bản <c>state</c>).</summary>
        public const string Sync = "sync";

        /// <summary>
        ///     Giá trị SDK tự điền cho một bước, hoặc <c>null</c> = BỎ KEY.
        ///     <br/>⚠ <c>null</c> ở đây gộp HAI ca khác nhau, và gộp là đúng: bước chung/bước nhóm
        ///     <b>không đổi trạng thái</b> (<c>open</c>, <c>interact</c>, <c>claim</c>,
        ///     <c>accept_request</c>, <c>help_request</c>, <c>help_give</c>, <c>chat</c>) và bước
        ///     RIÊNG của tính năng — cái sau thì <b>SDK không được đoán hộ</b>, game tự điền
        ///     (<c>create</c> là <c>enter</c>, <c>kick_member</c> thì bỏ key: chỉ game biết). Hai ca
        ///     cùng ra "SDK không điền gì", nên đừng thêm cảnh báo cho ca sau — phần lớn bước riêng
        ///     đúng là không có tác động nào.
        ///     <br/>⚠ Cũng KHÔNG điền cho bản tin do SERVER gửi hộ: server không chạy SDK, và bảng
        ///     của server khác bảng này (<c>join_accepted → enter</c>, <c>kicked → exit</c>).
        /// </summary>
        [CanBeNull]
        public static string DefaultFor([CanBeNull] string step)
        {
            return step switch
            {
                FFeatureStep.Unlock => Unlock,
                FFeatureStep.State => Sync,
                FFeatureStep.Group.Join => Enter,
                FFeatureStep.Group.Leave => Exit,
                FFeatureStep.Group.VariantChanged => SetVariant,
                _ => null
            };
        }
    }
}
