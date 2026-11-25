using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Falcon.Helpers.Devkit;
using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>
    /// Bất biến của hợp đồng tính năng (<c>docs/design/FeatureContract.md</c> §10).
    /// <br/>🔴 Viết ở mức PARAM, KHÔNG dựng log: <c>FKeyService.Encode</c> là lời gọi static thuần
    /// nên chạy THẬT trong <c>Tools~/TestHarness</c>. Dựng log là rơi vào nhóm ~70 test cần Unity
    /// runtime — nhóm chưa từng *chạy*, chỉ *compile* (đúng số phận ba test wire
    /// <c>FAdLoadLogTests</c> ở 1.5.7), mà mấy bất biến dưới đây là loại không được ghim bằng mắt.
    /// </summary>
    public class FeatureContractTests
    {
        private const BindingFlags WIRE = BindingFlags.Public | BindingFlags.Instance;

        private static FeatureParam StateParam(string instanceId)
        {
            return new FeatureParam
            {
                feature = "guild",
                featureStep = FFeatureStep.State,
                featureInstanceId = instanceId,
                featureJoinedTs = 1_700_000_000_000
            };
        }

        // ---- `null` vs `""` của featureInstanceId: hai nghĩa, phải tách được ----

        [Test]
        public void EmptyInstanceId_ReachesTheWire()
        {
            // "" = "đã mở khoá, KHÔNG ở thực thể nào" ⇒ kho XOÁ thực thể. Mất nó thì bản state của
            // người vừa bị kick thành "vắng key = không chạm" và họ ở lại nhóm cũ trong kho, im
            // lặng — bug tệ nhất của cả hợp đồng. FKeyService chỉ bỏ null, nên "" phải lọt.
            var dict = StateParam(string.Empty).ToDictionary();

            Assert.IsTrue(dict.ContainsKey("featureInstanceId"), "\"\" là ngoại lệ CÓ TÊN của luật bỏ-key");
            Assert.AreEqual(string.Empty, dict["featureInstanceId"]);
        }

        [Test]
        public void NullInstanceId_IsAbsentFromTheWire()
        {
            // null = "tính năng không có khái niệm thực thể" ⇒ kho KHÔNG chạm. Khác hẳn "" ở trên.
            var dict = StateParam(null).ToDictionary();

            Assert.IsFalse(dict.ContainsKey("featureInstanceId"));
        }

        [Test]
        public void FlattenStep_AlsoKeepsTheEmptyString()
        {
            // Cổng thứ HAI trên đường ra dây: FFeatureLog.ToDictionary() trải param bằng
            // PutIfAbsentAndNotNull. Ghim ngay trên extension vì test ở mức log thì phải dựng
            // PlainLog ⇒ rơi vào nhóm cần Unity runtime và bất biến này mất người canh.
            var dict = new Dictionary<string, object>()
                .PutIfAbsentAndNotNull("featureInstanceId", string.Empty);

            Assert.IsTrue(dict.ContainsKey("featureInstanceId"), "Chuỗi rỗng KHÁC null — không được lọc");
            Assert.AreEqual(string.Empty, dict["featureInstanceId"]);
        }

        // ---- Trục tính năng trên các stream khác ----

        [Test]
        public void FeatureAxis_ReachesTheWire_FromTheDeepestParam()
        {
            // LevelPassParamV2 : LevelProgressParamV2 : ALevelParamV2 : ALiveOpsParam : FParam
            // — bốn tầng (05/11 bỏ một tầng). FKeyService dùng
            // GetFields(Instance | Public) nên lấy CẢ field public thừa kế; ghim kẻo ai đổi flag.
            var dict = new LevelPassParamV2
            {
                playTurnId = "turn-1", currentLevel = 5,
                feature = "guild", featureInstanceId = "T_8812", featureStage = 3
            }.ToDictionary();

            Assert.AreEqual("guild", dict["feature"]);
            Assert.AreEqual("T_8812", dict["featureInstanceId"]);
            Assert.AreEqual(3, dict["featureStage"]);
            Assert.AreEqual("turn-1", dict["playTurnId"], "Field của lớp con vẫn phải còn");
        }

        [Test]
        public void BothAxes_LiveOnOneBase_RightUnderFParam()
        {
            // ĐẢO CHIỀU test cũ (02/11 → 05/11). Trước đây ba field feature* bị CẤM xuất hiện ở
            // LiveOpsRef, vì khoá gộp của UiExposureState chỉ có trục event: đóng dấu tính năng lên
            // ref UI thì vừa không lên dây vừa gộp hai bang hội vào một dòng. Hai trục đã về một
            // lớp nền (tầng AFeatureParam khai "tính năng LÀ MỘT event", sai hẳn), nên giờ chúng
            // PHẢI có — và cái giá đã trả: khoá gộp chở đủ bảy ô, ghim ở
            // UiLiveOpsClusterTests.FeatureAxis_IsPartOfTheClusterKey.
            var axes = new[]
            {
                "liveOpsEvent", "liveOpsCycleId", "liveOpsRoundId", "liveOpsStage",
                "feature", "featureInstanceId", "featureStage"
            };

            foreach (var name in axes)
                Assert.IsNotNull(typeof(ALiveOpsParam).GetField(name, WIRE),
                    $"ALiveOpsParam.{name} biến mất — một trong hai trục đang bị tách ra");

            foreach (var name in axes)
                Assert.IsNotNull(typeof(LiveOpsRef).GetField(name, WIRE),
                    $"LiveOpsRef.{name} biến mất — ref UI phải chở được cả hai trục");

            Assert.AreEqual(typeof(FParam), typeof(ALiveOpsParam).BaseType,
                "Lớp nền phải nằm NGAY dưới FParam: chen tầng vào giữa là lặp lại bẫy 02/11 " +
                "(tầng mới không chuyền tiếp CorrectValues thì luật của nó mất người chạy)");
        }

        [Test]
        public void ExposureLog_DeclaresTheFeatureAxisItself()
        {
            // FUiExposureLog KHÔNG kế thừa lớp nền — nó chép TỪNG FIELD MỘT từ LiveOpsRef. Nên
            // thêm field vào lớp nền mà quên chép là mất trục tính năng của mọi dòng impression,
            // im lặng. Nửa "ctor có chép không" thì phải dựng log ⇒ rơi vào nhóm cần Unity runtime
            // (FeatureAxis_ReachesTheLog, chỉ compile). Nửa "log có KHAI field không" thì
            // reflection kiểm được, và đó là nửa hay bị quên.
            foreach (var name in new[] { "feature", "featureInstanceId", "featureStage" })
                Assert.IsNotNull(typeof(FUiExposureLog).GetField(name, WIRE),
                    $"FUiExposureLog.{name} thiếu — trục tính năng không lên dây ở dòng cụm Ui");
        }

        // ---- CopyContextFrom: chỗ DUY NHẤT liệt kê bảy field ngữ cảnh ----

        [Test]
        public void CopyContextFrom_CarriesEveryContextField_AndCannotRot()
        {
            // Đây là test chống MỤC, không phải test chức năng. Ai thêm field thứ tám vào lớp nền
            // mà quên thêm dòng chép trong CopyContextFrom thì field đó ở target vẫn null ⇒ ĐỎ.
            // Không ghim được bằng mắt: ba chỗ chép tay cũ (ApplyLiveOps của InAppValidation,
            // PurchaseAttemptState, FUiExposureLog) mục im lặng đúng kiểu này suốt lượt gộp 05/11.
            var fields = ContextFields();
            Assert.AreEqual(7, fields.Length,
                "Lớp nền đổi số field ngữ cảnh — thêm dòng chép vào CopyContextFrom rồi sửa số này");

            var source = new LiveOpsRef();
            foreach (var field in fields)
                field.SetValue(source, field.FieldType == typeof(string) ? (object)"x" : 7);

            var target = new ResourceParam();
            target.CopyContextFrom(source);

            foreach (var field in fields)
                Assert.IsNotNull(field.GetValue(target),
                    $"CopyContextFrom bỏ sót '{field.Name}' — thêm một dòng cho nó trong ALiveOpsParam");
        }

        [Test]
        public void CopyContextFrom_DoesNotOverwriteWhatIsAlreadySet()
        {
            var target = new ResourceParam { liveOpsEvent = "mine", feature = "daily_gift" };

            target.CopyContextFrom(new LiveOpsRef
            {
                liveOpsEvent = "theirs", liveOpsCycleId = "season_12",
                feature = "guild", featureInstanceId = "T_1"
            });

            Assert.AreEqual("mine", target.liveOpsEvent, "Khuôn ??= — giá trị đã có phải thắng");
            Assert.AreEqual("daily_gift", target.feature, "Khuôn ??= — giá trị đã có phải thắng");
            Assert.AreEqual("season_12", target.liveOpsCycleId, "Ô trống thì mới điền");
            Assert.AreEqual("T_1", target.featureInstanceId, "Ô trống thì mới điền");
        }

        [Test]
        public void CopyContextFrom_NullSource_IsNoOp()
        {
            // PurchaseAttemptState gọi với _context có thể null (lượt mua không có ngữ cảnh nào).
            var target = new ResourceParam { feature = "guild" };

            target.CopyContextFrom(null);

            Assert.AreEqual("guild", target.feature);
            Assert.IsNull(target.liveOpsEvent);
        }

        private static FieldInfo[] ContextFields()
        {
            return typeof(ALiveOpsParam).GetFields(WIRE | BindingFlags.DeclaredOnly);
        }

        // ---- Stamp: tay thắng ----

        [Test]
        public void Stamp_DoesNotOverwriteWhatTheGamePassedByHand()
        {
            var param = new ResourceParam { feature = "daily_gift", featureInstanceId = "X", featureStage = 9 };

            param.StampFeature("guild", "T_8812", 1);

            Assert.AreEqual("daily_gift", param.feature, "Khuôn ??= — tay thắng");
            Assert.AreEqual("X", param.featureInstanceId);
            Assert.AreEqual(9, param.featureStage);
        }

        [Test]
        public void Stamp_FillsTheEmptySlotsOnly()
        {
            var param = new ResourceParam { featureInstanceId = "X" };

            param.StampFeature("guild", "T_8812", 1);

            Assert.AreEqual("guild", param.feature);
            Assert.AreEqual("X", param.featureInstanceId, "Chỗ đã có thì giữ");
            Assert.AreEqual(1, param.featureStage);
        }

        [Test]
        public void Stamp_WithoutAFeatureName_DoesNothing()
        {
            // Giống LiveOps.Stamp: tên rỗng thì không đóng dấu gì, kể cả thực thể — một dòng mang
            // thực thể mà không mang tên tính năng thì kho không nối về đâu được.
            var param = new ResourceParam();

            param.StampFeature(string.Empty, "T_8812", 1);

            Assert.IsNull(param.feature);
            Assert.IsNull(param.featureInstanceId);
            Assert.IsNull(param.featureStage);
        }

        // ---- featureEffect: SDK điền phần nào ----

        [Test]
        public void DefaultEffect_MatchesTheWarehouseTable()
        {
            // Bảng §6 (hợp đồng kho §4.2, cột "Client — SDK điền"). Profile CHỈ đọc field này chứ
            // không suy từ tên bước, nên lệch bảng là profile đọc sai mà không ai ném lỗi.
            Assert.AreEqual(FFeatureEffect.Unlock, FFeatureEffect.DefaultFor(FFeatureStep.Unlock));
            Assert.AreEqual(FFeatureEffect.Sync, FFeatureEffect.DefaultFor(FFeatureStep.State));
            Assert.AreEqual(FFeatureEffect.Enter, FFeatureEffect.DefaultFor(FFeatureStep.Group.Join));
            Assert.AreEqual(FFeatureEffect.Exit, FFeatureEffect.DefaultFor(FFeatureStep.Group.Leave));
            Assert.AreEqual(FFeatureEffect.SetVariant,
                FFeatureEffect.DefaultFor(FFeatureStep.Group.VariantChanged));
        }

        [Test]
        public void DefaultEffect_IsDroppedForStepsThatChangeNothing()
        {
            foreach (var step in new[]
                     {
                         FFeatureStep.Open, FFeatureStep.Interact, FFeatureStep.Claim,
                         FFeatureStep.Group.AcceptRequest, FFeatureStep.Group.HelpRequest,
                         FFeatureStep.Group.HelpGive, FFeatureStep.Group.Chat
                     })
                Assert.IsNull(FFeatureEffect.DefaultFor(step), $"'{step}' không đổi trạng thái ⇒ bỏ key");
        }

        [Test]
        public void DefaultEffect_IsNeverGuessedForAFeatureOwnStep()
        {
            // Bước riêng của tính năng: chỉ game biết nó có tác động gì (`create` là enter,
            // `kick_member` thì không) ⇒ SDK không được đoán hộ.
            Assert.IsNull(FFeatureEffect.DefaultFor("create"));
            Assert.IsNull(FFeatureEffect.DefaultFor("kick_member"));
            Assert.IsNull(FFeatureEffect.DefaultFor("leader_transferred"));
            Assert.IsNull(FFeatureEffect.DefaultFor(null));
        }

        // ---- Từ vựng: bản sao dễ trôi ----

        private static string[] Consts(System.Type type)
        {
            return type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => (string)f.GetRawConstantValue())
                .ToArray();
        }

        [Test]
        public void StepVocabulary_MatchesTheWarehouseContract_AndKeepsTheTwoTiersApart()
        {
            // Hằng số ở đây là BẢN SAO của hợp đồng kho §4.1 — chỗ dễ trôi nhất. Hai danh sách
            // literal dưới đây là bản ký 30/10; thêm/đổi một bước mà không sửa doc thì test đỏ.
            // Và chúng phải ở ĐÚNG BẬC: bước chung thẳng trên lớp, bước nhóm trong Group — bậc là
            // thứ chỗ gọi đọc được, nên để lẫn là dev tưởng mọi tính năng đều có `help_give`.
            var common = new[] { "unlock", "state", "open", "interact", "claim" };
            var group = new[]
            {
                "join", "accept_request", "leave", "help_request", "help_give", "chat",
                "variant_changed"
            };

            CollectionAssert.AreEquivalent(common, Consts(typeof(FFeatureStep)));
            CollectionAssert.AreEquivalent(group, Consts(typeof(FFeatureStep.Group)));

            foreach (var step in common.Concat(group))
                Assert.AreEqual(step.ToLowerInvariant(), step, "Bước phải snake_case — Parse xé chữ HOA");
        }

        [Test]
        public void FeatureNameVocabulary_MatchesTheWarehouseContract()
        {
            // Cũng là bản sao của §4.1. Hằng số KHÔNG phải cửa chặn (tên ngoài danh sách vẫn gửi
            // được, §0) — nó chỉ dẹp lỗi gõ `guild` vs `guilds` ngay lúc gõ, vì đó mới là rủi ro
            // thật của trục này: một tính năng song song chia đôi số liệu, im lặng.
            var expected = new[]
            {
                "guild", "friend", "daily_gift", "lucky_wheel", "star_chest", "level_chest",
                "win_streak", "home_decor", "collection", "upgrade", "cosmetic", "idle_mission"
            };

            CollectionAssert.AreEquivalent(expected, Consts(typeof(FFeatureName)));

            foreach (var name in expected)
                Assert.AreEqual(name.ToLowerInvariant(), name, "Tên tính năng phải snake_case (§4.1)");
        }

        [Test]
        public void AFeatureNameOutsideTheVocabulary_IsStillAccepted()
        {
            // Ghim án owner 30/10: SDK KHÔNG kiểm danh sách. Tính năng mới không phải chờ bản SDK,
            // và phiên đầu chưa có remote config cũng không mất dữ liệu.
            var param = new FeatureParam { feature = "brand_new_thing", featureStep = FFeatureStep.Open };

            param.CorrectValues();

            Assert.AreEqual("brand_new_thing", param.ToDictionary()["feature"]);
        }

        [Test]
        public void EffectVocabulary_MatchesTheWarehouseContract()
        {
            var expected = new[] { "unlock", "enter", "exit", "set_variant", "sync" };

            CollectionAssert.AreEquivalent(expected, Consts(typeof(FFeatureEffect)));
        }

        // ---- joinedTs thiếu: cảnh báo, KHÔNG chặn ----

        [Test]
        public void StateWithoutJoinedTs_StillCarriesEverything()
        {
            // Chặn ở client là giấu DQ của kho. Cảnh báo thì có (AnalyticLogger.Warning —
            // [Conditional("FALCON_LOG_DEBUG")] nên không assert được ở đây), còn phần ghim được là
            // "VẪN GỬI": CorrectValues không được bỏ field nào, không được hạ "" về null.
            var param = new FeatureParam
            {
                feature = "guild", featureStep = FFeatureStep.State, featureInstanceId = "T_8812"
            };

            param.CorrectValues();
            var dict = param.ToDictionary();

            Assert.AreEqual("guild", dict["feature"]);
            Assert.AreEqual("T_8812", dict["featureInstanceId"]);
            Assert.AreEqual(FFeatureStep.State, dict["featureStep"]);
            Assert.IsFalse(dict.ContainsKey("featureJoinedTs"), "Thiếu thì vắng key, không điền 0");
        }

        [Test]
        public void ExtraMeta_FlattensAndNeverOverwritesARealField()
        {
            // Extras trải phẳng bằng PutIfAbsentAndNotNull (ai-ghi-trước-giữ) nên nếu game nhét
            // "featureEffect" vào extraMeta thì field THẬT thắng — đúng lý do chín field của hợp
            // đồng không đi đường extras.
            var dict = new FeatureParam
            {
                feature = "guild",
                featureStep = FFeatureStep.Group.HelpGive,
                featureEffect = null,
                extraMeta = new Dictionary<string, object>
                {
                    ["help_item"] = "card",
                    ["request_id"] = "r-1",
                    ["featureStep"] = "bịa"
                }
            }.ToDictionary();

            Assert.AreEqual("card", dict["help_item"]);
            Assert.AreEqual("r-1", dict["request_id"]);
            Assert.AreEqual(FFeatureStep.Group.HelpGive, dict["featureStep"], "Field thật phải thắng extras");
            Assert.IsFalse(dict.ContainsKey("extraMeta"), "extraMeta không được lên dây nguyên khối");
        }

        [Test]
        public void ExtraMeta_StillCarriesAFieldItStoleWhenTheRealOneIsEmpty()
        {
            // Cửa sau dễ đi nhất: nhét field thật qua extras vì overload tiện tay không có tham số
            // đó (`unlock` cần featureUnlockedTs). Nó CHẠY ĐƯỢC — đúng cái làm nó nguy hiểm, nên
            // SDK cảnh báo (AnalyticLogger, không assert được ở đây) mà KHÔNG chặn: chặn là mất số.
            var dict = new FeatureParam
            {
                feature = "guild",
                featureStep = FFeatureStep.Unlock,
                extraMeta = new Dictionary<string, object> { ["feature_unlocked_ts"] = 1L }
            }.ToDictionary();

            Assert.AreEqual(1L, dict["feature_unlocked_ts"], "Vẫn gửi — cảnh báo chứ không veto");
        }

        [Test]
        public void ResourceWhen_HasTheTwoValuesTheFeatureContractSigned()
        {
            // Hai dòng tài nguyên của hợp đồng tính năng (bản Team §2.3, §2.8). Luật nhà: *When có
            // hằng số thì CẤM gõ string tay, nên thiếu hằng là dev tự chế chuỗi và cross-game lệch.
            Assert.AreEqual("create_fee", FResourceWhen.CreateFee);
            Assert.AreEqual("help_received", FResourceWhen.HelpReceived);
        }
    }
}
