using System.Linq;
using NUnit.Framework;
using UnityEngine;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>
    /// Cờ log-của-người-làm-game. Luật cứng (chốt owner 16/10): **bản production tuyệt đối không
    /// mang cờ này** — một lần dev quên tắt là hỏng toàn bộ dữ liệu bản phát hành. Nên cờ chỉ đến
    /// từ LOẠI BUILD, không ai ghi đè được.
    /// <br/>⚠ Test chạy trong Editor nên <c>IsTestBuild</c> ở đây luôn true — phần "production thì
    /// vắng" được ghim bằng cách kiểm chính BIỂU THỨC nguồn, cộng hai lớp chặn: không có setter, và
    /// tên key nằm trong sổ key-trung-tâm.
    /// </summary>
    public class TestFlagTests
    {
        [Test]
        public void InEditor_FlagIsOn_AndSourceSaysEditor()
        {
            var info = new TestFlagInfoRepository().GetInfo();

            Assert.AreEqual(true, info[TestFlagInfoRepository.KEY_IS_TEST]);
            Assert.AreEqual(TestFlagInfoRepository.SOURCE_EDITOR,
                info[TestFlagInfoRepository.KEY_TEST_SOURCE],
                "Editor play phải nhận nguồn 'editor' — đúng ca artifact app_open/retention từng " +
                "phải đi giải mã Editor.log mới kết luận được");
            Assert.AreEqual(2, info.Count, "Chỉ hai field, không chở gì thêm");
        }

        [Test]
        public void FlagComesOnlyFromBuildType_NoOverridePossible()
        {
            // Đây là lớp chặn QUAN TRỌNG NHẤT: giá trị tính tại chỗ từ loại build, không có setter,
            // không đọc config, không đọc kho local. Thêm bất cứ nguồn nào khác là mở lại đúng cánh
            // cửa "dev quên tắt → hỏng dữ liệu bản phát hành".
            Assert.AreEqual(Application.isEditor || Debug.isDebugBuild,
                TestFlagInfoRepository.IsTestBuild);

            var setters = typeof(TestFlagInfoRepository)
                .GetProperties()
                .Where(property => property.CanWrite)
                .ToArray();
            Assert.IsEmpty(setters, "Không được có đường ghi cờ từ bên ngoài");
        }

        [Test]
        public void GameCannotInjectTheFlag_ViaCommonParam()
        {
            // Không có lớp chặn này thì một dòng Common.Set("isTest", true) trong code game là đủ
            // làm hỏng dữ liệu thật của bản phát hành.
            var state = new CommonParamState();

            Assert.AreEqual(CommonParamSet.CentralKeyRejected,
                state.Set(TestFlagInfoRepository.KEY_IS_TEST, true, persist: false));
            Assert.AreEqual(CommonParamSet.CentralKeyRejected,
                state.Set(TestFlagInfoRepository.KEY_TEST_SOURCE, "fake", persist: false));
            Assert.IsFalse(state.Has(TestFlagInfoRepository.KEY_IS_TEST));
        }

        [Test]
        public void KeyNamesAreContract()
        {
            Assert.AreEqual("isTest", TestFlagInfoRepository.KEY_IS_TEST);
            Assert.AreEqual("testSource", TestFlagInfoRepository.KEY_TEST_SOURCE);
        }
    }
}
