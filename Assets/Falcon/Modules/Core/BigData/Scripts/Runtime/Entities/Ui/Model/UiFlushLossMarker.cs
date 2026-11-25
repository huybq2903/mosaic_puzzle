/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-09
 */

using Falcon.Helpers.Devkit;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    ///     Đường đo PHẦN MẤT của cụm exposure UI (ký với loader 09/11): một DẤU trên đĩa nói "đang
    ///     có một thế hệ cụm mở mà chưa xả". App bị kill cứng thì lượt xả không chạy, dấu còn lại, và
    ///     phiên sau bắn <b>một</b> dòng <c>flush_lost</c>.
    ///     <br/>Lớp thuần quanh <see cref="IDataPool"/> — cùng khuôn <see cref="UiExposureState"/>,
    ///     để bất biến ở đây test được mà không cần Unity runtime.
    ///     <br/><br/>🔴 <b>Vì sao chỉ một DẤU chứ không phải một SỐ</b>: giữ số lần hiện đã mất nghĩa
    ///     là ghi đĩa mỗi lần <c>Record</c> — đúng cái giá mà quyết định "không persist cụm" đã từ
    ///     chối trả. Loader đã bỏ đề nghị dòng mang số để lấy hình dạng này, và đọc nó là <i>"số PHIÊN
    ///     có lượt xả bị mất"</i>.
    ///     <br/><br/>🔴 <b>Hai cái bẫy mà bản đầu của lượt này đã sập, ghi lại để đừng gỡ ra:</b>
    ///     <br/>1. <b>Phải <see cref="IDataPool.TrySync"/> tay.</b> <c>FDataPool.Save</c> chỉ ghi vào
    ///     cache RAM; đĩa chỉ được ghi ở <c>OnPostStop</c> và nhịp lặp 5 phút. Mà ca cần đo CHÍNH LÀ
    ///     ca <c>OnPostStop</c> không chạy — nên không sync tay thì dấu bay cùng tiến trình và
    ///     <c>flush_lost</c> <b>không bao giờ nổ</b>. Sync cả lúc XOÁ nữa: dấu đã xuống đĩa mà xoá chỉ
    ///     trong cache thì một lần kill sau khi xả THÀNH CÔNG vẫn thấy dấu ⇒ dương tính giả.
    ///     Sync ở đây rẻ vì nó xảy ra <b>một lần cho mỗi thế hệ cụm</b> (mỗi lượt lên tiền cảnh), chứ
    ///     không phải mỗi lần hiện.
    ///     <br/>2. <b>Không đi bằng <c>IPioneer.OnPreContinue</c>.</b> <c>AppFlowService.CheckGameContinue</c>
    ///     return sớm khi <c>!_gameStop</c>, nên <b>cold start KHÔNG gọi</b> nó — mà cold start đúng là
    ///     thứ xảy ra sau một lần kill cứng. Đi đường đó là cài một cái đo không bao giờ đo được ca nó
    ///     sinh ra để đo. Thay vào đó <see cref="TakeStaleOnce"/> chạy <b>một lần cho mỗi tiến
    ///     trình</b>, ở lần chạm đầu tiên vào bất kỳ cửa nào của entity — đúng ở cả cold start lẫn warm
    ///     resume. Lượt xả lúc pause cũng gọi nó, nên chậm nhất là pause đầu tiên của phiên sau là
    ///     báo được.
    ///     <br/><br/>⚠ <b>Số đo là CHẶN DƯỚI, không phải số đúng.</b> <c>TrySync</c> trả <c>false</c>
    ///     khi không lấy được khoá file, và dấu đặt rồi mà tiến trình chết trước khi sync xong thì
    ///     cũng mất. Kho đọc <i>"ít nhất bấy nhiêu phiên bị mất"</i>.
    /// </summary>
    public class UiFlushLossMarker
    {
        private const string KEY = "ui_clusterOpenUnflushed";

        private readonly IDataPool _dataPool;

        private bool _markedOpen;
        private bool _staleChecked;

        public UiFlushLossMarker(IDataPool dataPool)
        {
            _dataPool = dataPool;
        }

        /// <summary>
        ///     Lần đầu trong tiến trình: dấu của phiên trước còn không. Trả <c>true</c> đúng một lần,
        ///     và chỉ khi phiên trước thật sự để lại dấu.
        ///     <br/>Phải gọi TRƯỚC <see cref="MarkOpen"/>, không thì dấu mới vừa đặt bị chính mình đọc
        ///     thành dấu cũ.
        /// </summary>
        public bool TakeStaleOnce()
        {
            if (_staleChecked) return false;
            _staleChecked = true;

            if (!_dataPool.HasKey(KEY)) return false;

            Erase();
            return true;
        }

        /// <summary>Một thế hệ cụm vừa mở — đặt dấu. Thế hệ đã đặt rồi thì không ghi lại.</summary>
        public void MarkOpen()
        {
            if (_markedOpen) return;

            _markedOpen = true;
            _dataPool.Save(KEY, 1);
            _dataPool.TrySync();
        }

        /// <summary>Thế hệ cụm vừa rời kho gộp (xả lúc pause, hoặc xả sớm vì tràn van) — xoá dấu.</summary>
        public void ClearMark()
        {
            if (!_markedOpen) return;

            Erase();
        }

        private void Erase()
        {
            _markedOpen = false;
            _dataPool.Delete(KEY);
            _dataPool.TrySync();
        }
    }
}
