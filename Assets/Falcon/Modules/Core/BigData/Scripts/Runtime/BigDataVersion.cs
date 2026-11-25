/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-10-14
 */

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    ///     Phiên bản module BigData, dán lên MỌI log qua <see cref="SdkVersionInfoRepository"/>.
    ///     <br/>Vì sao phải là HẰNG SỐ chứ không đọc <c>package.json</c> lúc chạy: file đó không nằm
    ///     trong <c>Resources</c> nên runtime không thấy nó; đọc được thì cũng là IO ở đường log.
    ///     <br/>⚠ <b>SỐ NÀY SINH TỰ ĐỘNG — đừng sửa tay.</b> <c>BigDataVersionSync</c> (code editor)
    ///     đọc <c>package.json</c> rồi ghi lại hằng này mỗi khi file đó đổi và mỗi lần nạp domain.
    ///     Bump version = sửa <b>đúng một chỗ</b>: <c>package.json</c>.
    ///     <br/>⚠ Lưới an toàn cuối là <c>SdkVersionTests.ConstMatchesPackageJson</c> — nó đỏ nếu
    ///     hai bên lệch (ai đó sửa tay, hoặc editor chưa kịp chạy). Đừng xoá test đó: số này là mốc
    ///     cắt ERA của cả kho dữ liệu, sai một bản là mọi phép so trước/sau bản đó lệch âm thầm.
    /// </summary>
    public static class BigDataVersion
    {
        public const string VALUE = "1.8.1";
    }
}
