using Falcon.Shared.BaseLevelEditor;
using Falcon.Shared.EditorUi;

namespace Falcon.InGame.LevelEditor
{
    /// <summary>Mục "Phím tắt" trên menu bar: hộp liệt kê thao tác chuột và phím của editor.</summary>
    [MenuItem("Phím tắt", 100)]
    public sealed class ShortcutsMenu : IMenuItem
    {
        private static readonly (string title, string body)[] Sections =
        {
            ("CHỌN", "Click: chọn mảnh. Ctrl+click: chọn thêm hoặc bớt. Kéo trên nền: chọn bằng khung.\nBấm một màu trong Thông tin tranh: chọn mọi mảnh màu đó.\nM gộp · H ẩn hoặc hiện màu · Esc bỏ chọn."),
            ("CẮT MẢNH (đang chọn đúng 1 mảnh)", "Bấm Tách rồi click đặt điểm (điểm hút vào biên), kéo xuyên hết mảnh.\nEnter hoặc chuột phải: cắt · Esc: huỷ."),
            ("SỬA BIÊN", "Chọn 1 mảnh rồi bấm sát biên: kéo điểm hoặc đầu thanh cong để uốn.\nShift+click: chèn điểm · Delete: xoá điểm · Enter hoặc chuột phải: chốt · Esc: huỷ.\nĐiểm giao của nhiều mảnh dời được, trừ điểm nằm trên khung tranh."),
            ("HÀNG CHỜ", "Click chọn hộp, Ctrl+click chọn thêm, kéo trên nền để chọn bằng khung.\nKéo hộp để đổi cột hoặc thứ tự. Chuột phải: bảng màu và số cát.\n[ và ]: đổi số cát một bậc · H ẩn màu."),
            ("MECHANIC", "N (mechanic Pair): bật chế độ nối hộp. F (mechanic Frozen): bật chế độ băng."),
            ("CHUNG", "Z hoặc Ctrl+Z: hoàn tác · Y hoặc Ctrl+Shift+Z: làm lại.\nGiữ chuột giữa kéo: dịch chuyển tranh · Cuộn chuột: phóng to, thu nhỏ."),
        };

        public void OnClick()
        {
            var modal = Kit.Modal("Phím tắt", KitModalSize.Md);
            foreach (var (title, body) in Sections)
            {
                Kit.Head(modal.Body, title);
                Kit.Text(modal.Body, body, KitText.Dim, true);
            }
            modal.SetOpen(true);
        }
    }
}
