using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>Hợp đồng đợt 3 B14(b)(c): bộ giá trị popup xin quyền / liên kết tài khoản.</summary>
    public class ConsentAccountTests
    {
        [Test]
        public void PromptEnums_WireValues()
        {
            Assert.AreEqual("ads_consent", WireName.Of(PromptPermission.AdsConsent));
            Assert.AreEqual("soft", WireName.Of(PromptKind.Soft));
            Assert.AreEqual("first_open", WireName.Of(PromptTrigger.FirstOpen));
            Assert.AreEqual("dismiss", WireName.Of(SoftResult.Dismiss));
            Assert.AreEqual("denied_dont_ask_again", WireName.Of(SystemResult.DeniedDontAskAgain));
        }

        [Test]
        public void AccountEnums_WireValues()
        {
            Assert.AreEqual("google_play_games", WireName.Of(AccountProvider.GooglePlayGames));
            Assert.AreEqual("save_conflict", WireName.Of(AccountLinkAction.SaveConflict));
            Assert.AreEqual("auto", WireName.Of(AccountInitiatedBy.Auto));
            Assert.AreEqual("keep_cloud", WireName.Of(SaveConflictChoice.KeepCloud));
        }

        [Test]
        public void LinkedProviders_WireIsSortedAndDistinct()
        {
            CollectionAssert.AreEqual(new[] { "facebook", "google_play_games" },
                LinkedProviders.ToWire(new[]
                    { AccountProvider.GooglePlayGames, AccountProvider.Facebook, AccountProvider.GooglePlayGames }));
            CollectionAssert.IsEmpty(LinkedProviders.ToWire(new AccountProvider[0]), "rỗng = chưa liên kết");
        }
    }
}
