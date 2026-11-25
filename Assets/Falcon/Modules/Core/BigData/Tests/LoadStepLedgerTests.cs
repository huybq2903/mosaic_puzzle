using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>Hợp đồng đợt 3 B2: trạm tải — một lần mỗi lần cài, đúng giai đoạn.</summary>
    public class LoadStepLedgerTests
    {
        [Test]
        public void NewInstall_FirstRun_AcceptsAppStepsOnce()
        {
            var pool = new FakeDataPool();
            var ledger = new LoadStepLedger(pool);
            ledger.OnLaunch(1);

            Assert.IsTrue(ledger.Accepts(LoadKind.App, LoadStep.LoadStart));
            ledger.Mark(LoadKind.App, LoadStep.LoadStart);
            Assert.IsFalse(ledger.Accepts(LoadKind.App, LoadStep.LoadStart), "mỗi trạm một lần");
            Assert.IsTrue(ledger.Accepts(LoadKind.App, LoadStep.FirstFrame), "không ép thứ tự");
        }

        [Test]
        public void SecondRun_RejectsAppSteps_ButLevelStepsWaitForFirstLevel()
        {
            var pool = new FakeDataPool();
            new LoadStepLedger(pool).OnLaunch(1);

            var secondRun = new LoadStepLedger(pool);
            secondRun.OnLaunch(2);
            Assert.IsFalse(secondRun.Accepts(LoadKind.App, LoadStep.AssetDownloadStart), "trạm app chỉ ở lần chạy đầu");
            Assert.IsTrue(secondRun.Accepts(LoadKind.Level, LoadStep.LevelLoadStart), "người chơi chưa vào level nào");

            secondRun.Mark(LoadKind.Level, LoadStep.LevelReady);
            Assert.IsFalse(secondRun.Accepts(LoadKind.Level, LoadStep.LoadError), "qua level đầu thì thôi");
        }

        [Test]
        public void UpgradedDevice_HasNoSteps()
        {
            var ledger = new LoadStepLedger(new FakeDataPool());
            ledger.OnLaunch(7); // lần đầu SDK có sổ mà đã ở phiên 7 = máy cập nhật từ bản cũ
            Assert.IsFalse(ledger.Accepts(LoadKind.App, LoadStep.LoadStart));
            Assert.IsFalse(ledger.Accepts(LoadKind.Level, LoadStep.LevelReady));
        }

        [Test]
        public void Enums_WireValues()
        {
            Assert.AreEqual("app", WireName.Of(LoadKind.App));
            Assert.AreEqual("asset_download_done", WireName.Of(LoadStep.AssetDownloadDone));
            Assert.AreEqual("level_ready", WireName.Of(LoadStep.LevelReady));
            Assert.AreEqual("consent_shown", WireName.Of(LoadStep.ConsentShown));
        }
    }
}
