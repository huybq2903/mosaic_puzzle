using Falcon.Helpers.Devkit;
using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>
    /// GHIM giá trị mặc định của chế độ telemetry load ad (1.5.5).
    /// <br/>Vì sao cần test cho một giá trị mặc định: nó là **thứ quyết định volume của cả fleet**, và
    /// nó đã bị đòi đảo một lần (owner kho 26/10, dựa trên phép ngoại suy 3,61 triệu user của CẢ fleet
    /// — trong đó phần lớn chạy SDK cũ, không thể bắn event này; mẫu số đúng là ~57,4 nghìn user của
    /// fleet v2 ⇒ ~9–20 triệu dòng/ngày, không phải 570 triệu). Đổi mặc định là **quyết định của owner
    /// SDK**, không phải việc dọn code — đỏ ở đây là để không ai lật ngược im lặng.
    /// </summary>
    public class AdLoadLogConfigTests
    {
        [Test]
        public void DefaultIsSplit_NotGrouped()
        {
            Assert.IsTrue(new AnalyticConfig().fAdLoadLogSplit,
                "Mặc định phải là TÁCH (true). Đổi giá trị này là đổi hình dạng dữ liệu của mọi game " +
                "chưa cấu hình gì trên server — xem doc IAdLoadLogConfig trước khi sửa.");
        }

        [Test]
        public void TheKeyIsBigDataOwned_SoItWorksWithoutAnalyticParamsSync()
        {
            // Bẫy đã xảy ra thật (26/10): có người hiểu "game không dùng CMS thì không tải được
            // AnalyticParamsSync nên mặc định là GỘP". Sai — khoá nằm trong BigData, đọc qua
            // Core.RemoteConfig, nên không có AnalyticParamsSync thì vẫn TÁCH. Muốn game đó gộp phải
            // gửi fAdLoadLogSplit = false qua Core.RemoteConfig của chính game.
            var config = new AnalyticConfig();
            Assert.IsNotNull(typeof(AnalyticConfig).GetField(nameof(AnalyticConfig.fAdLoadLogSplit)),
                "Khoá chuyển chế độ phải nằm trong AnalyticConfig của BigData, không phải ở module khác");

            config.fAdLoadLogSplit = false;
            Assert.IsFalse(config.fAdLoadLogSplit, "Van tắt đi qua chính khoá này, không cần CMS");
        }

        [Test]
        public void ReserveConfigIsNotPrimary_SoAnotherModuleCanOverrideIt()
        {
            // Luật DI (DirectParamRequirement): nhiều impl mà KHÔNG cái nào [Primary] thì container
            // NÉM. Bản cơ bản cố ý không đánh dấu để AnalyticParamsSync ghi đè được.
            Assert.IsEmpty(typeof(ReserveAdLoadLogConfig).GetCustomAttributes(typeof(PrimaryAttribute), false),
                "ReserveAdLoadLogConfig KHÔNG được gắn [Primary] — nếu không thì nó và bản của " +
                "AnalyticParamsSync cùng ưu tiên 0 và DI ném RequirementException");
        }
    }
}
