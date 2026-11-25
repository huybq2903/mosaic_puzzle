using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>Hợp đồng đợt 3 B4: cài đặt âm thanh / nhạc / rung / ngôn ngữ.</summary>
    public class SettingStateTests
    {
        [Test]
        public void Set_UsesKeptValue_ThenDefault_ThenToggle()
        {
            var state = new SettingState();
            state.DeclareDefaults(true, true, false);

            var change = state.Set(FSetting.Sound, false);
            Assert.AreEqual(true, change.Value.Before, "chưa giữ thì trước = mặc định");
            Assert.AreEqual(false, change.Value.After);

            Assert.IsNull(state.Set(FSetting.Sound, false), "không đổi thì không có dòng");

            var noDefault = new SettingState();
            Assert.AreEqual(false, noDefault.Set(FSetting.Haptic, true).Value.Before, "không có mặc định: công tắc");
        }

        [Test]
        public void Sync_RecordsWithoutChange()
        {
            var state = new SettingState();
            state.DeclareDefaults(true, true, true);
            state.Sync(false, null, null, "vi");

            Assert.AreEqual(false, state.CurrentSound);
            Assert.AreEqual(true, state.CurrentMusic, "null = giữ (mặc định)");
            Assert.AreEqual("vi", state.language);
            Assert.AreEqual(false, state.IsDefault());
        }

        [Test]
        public void IsDefault_NeedsDeclaredDefaults()
        {
            var state = new SettingState();
            state.Sync(true, true, true, null);
            Assert.IsNull(state.IsDefault(), "chưa khai mặc định thì không biết");
            state.DeclareDefaults(true, true, true);
            Assert.AreEqual(true, state.IsDefault());
        }

        [Test]
        public void Language_BeforeIsAbsentWhenUnknown()
        {
            var state = new SettingState();
            var first = state.SetLanguage("en");
            Assert.IsNull(first.Value.Before);
            Assert.AreEqual("en", first.Value.After);
            Assert.IsNull(state.SetLanguage("en"));
            Assert.AreEqual("en", state.SetLanguage("vi").Value.Before);
        }

        [Test]
        public void Enums_WireValues()
        {
            Assert.AreEqual("sound", WireName.Of(FSetting.Sound));
            Assert.AreEqual("language", WireName.Of(FSetting.Language));
            Assert.AreEqual("public_place", WireName.Of(MuteReason.PublicPlace));
            Assert.AreEqual("dislike_sound", WireName.Of(MuteReason.DislikeSound));
        }
    }
}
