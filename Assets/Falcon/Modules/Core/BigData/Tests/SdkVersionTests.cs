using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>
    /// Ghim <see cref="BigDataVersion.VALUE"/> khớp <c>package.json</c>. Bình thường
    /// <c>BigDataVersionSync</c> (editor) tự đồng bộ hằng đó, nên test này là LƯỚI AN TOÀN CUỐI:
    /// nó bắt ca ai đó sửa tay file sinh ra, hoặc editor chưa kịp chạy (sửa ngoài Unity rồi build
    /// thẳng). Số đó là mốc cắt ERA của cả kho dữ liệu — lệch một bản là mọi phép so trước/sau bản
    /// đó sai âm thầm.
    /// <br/>Test nằm trong asmdef chỉ-Editor + <c>UNITY_INCLUDE_TESTS</c> nên việc đọc file ở đây
    /// KHÔNG đi vào bản build.
    /// </summary>
    public class SdkVersionTests
    {
        private static string PackageJsonPath()
        {
            return Directory
                .GetFiles(Application.dataPath, "package.json", SearchOption.AllDirectories)
                .Select(path => path.Replace('\\', '/'))
                .FirstOrDefault(path => path.EndsWith("/Core/BigData/package.json"));
        }

        [Test]
        public void ConstMatchesPackageJson()
        {
            var path = PackageJsonPath();
            Assert.IsNotNull(path,
                "Không tìm thấy Core/BigData/package.json — module bị đổi chỗ thì sửa lại hàm dò " +
                "đường dẫn trong test này, đừng xoá test (xem doc BigDataVersion).");

            var json = File.ReadAllText(path);
            var match = Regex.Match(json, "\"version\"\\s*:\\s*\"([^\"]+)\"");
            Assert.IsTrue(match.Success, "package.json không có field version?");

            Assert.AreEqual(match.Groups[1].Value, BigDataVersion.VALUE,
                "package.json và BigDataVersion.VALUE lệch nhau. Bình thường BigDataVersionSync tự " +
                "ghi hằng đó — đỏ ở đây nghĩa là file sinh ra bị sửa tay, hoặc editor chưa chạy. " +
                "Số này đi theo mọi dòng log và là mốc cắt era của kho dữ liệu.");
        }

        [Test]
        public void RepositoryShipsTheVersionOnEveryLog()
        {
            var info = new SdkVersionInfoRepository().GetInfo();

            Assert.AreEqual(BigDataVersion.VALUE, info[SdkVersionInfoRepository.KEY]);
            Assert.AreEqual(1, info.Count, "Chỉ chở đúng một field — module khác không đi đường này");
        }

        [Test]
        public void GameCannotShadowTheKey()
        {
            // Central param merge bằng PutIfAbsent nên AI ĐẾN TRƯỚC THẮNG: game khai common param
            // trùng tên mà không bị chặn thì có ca version của SDK bị giá trị của game che.
            // Khai KEY trong ParamKey.App là để nó vào sổ "key trung tâm" và bị từ chối ngay cửa.
            var state = new CommonParamState();

            Assert.AreEqual(CommonParamSet.CentralKeyRejected,
                state.Set(SdkVersionInfoRepository.KEY, "9.9.9", persist: false));
            Assert.IsFalse(state.Has(SdkVersionInfoRepository.KEY));
        }
    }
}
