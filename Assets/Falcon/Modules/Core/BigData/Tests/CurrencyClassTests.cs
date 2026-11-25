using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>
    /// Hạng tài nguyên (<c>currency_class</c>) — cột loader ký 06/11, tập ĐÓNG 9 giá trị.
    /// </summary>
    public class CurrencyClassTests
    {
        [Test]
        public void TheClosedSet_HasExactlyTheNineSignedValues()
        {
            foreach (var value in new[]
                     {
                         "hard", "soft", "live", "booster", "unlimited",
                         "material", "event_counter", "progress", "ticket"
                     })
                Assert.IsTrue(FCurrencyClass.IsValid(value), value);

            foreach (var value in new[] { "unknown", "", "coins", "Hard ", "event-counter" })
                Assert.IsFalse(FCurrencyClass.IsValid(value), value);
        }

        [Test]
        public void MatchingLowercasesButDoesNotStripUnderscores()
        {
            // 🔴 Ký với loader §2.3: CHỈ thường hoá. Dùng lại Normalize của FExtraMeta /
            // FWireReservedKeys là sai — hai hàm đó còn bỏ dấu "_", nên "eventcounter" sẽ lọt. Mà
            // event_counter là một trong hai hạng PHI KINH TẾ: lọt một biến thể là mở đường cho hai
            // chuỗi cùng nghĩa trên cùng một cột, rồi luật "loại phi kinh tế" bỏ sót một nửa.
            Assert.IsTrue(FCurrencyClass.IsValid("EVENT_COUNTER"));
            Assert.AreEqual("event_counter", FCurrencyClass.Canonical("Event_Counter"));
            Assert.IsFalse(FCurrencyClass.IsValid("eventcounter"), "Bỏ dấu _ là SAI hợp đồng");
            Assert.IsNull(FCurrencyClass.Canonical("eventcounter"));
        }

        [Test]
        public void TheFourSinkWords_AreSigned()
        {
            Assert.AreEqual("play_cost", FResourceWhen.PlayCost);
            Assert.AreEqual("booster_use", FResourceWhen.BoosterUse);
            Assert.AreEqual("upgrade", FResourceWhen.Upgrade);
            Assert.AreEqual("revive", FResourceWhen.Revive);
        }
    }

    /// <summary>
    /// Sổ khai hạng. Bất biến đáng giá nhất ở đây là <b>LẦN KHAI ĐẦU THẮNG</b> — ngược với
    /// <c>FunnelRegistry</c> (lần sau thắng) và ngược với bản đầu của ticket kho.
    /// </summary>
    public class CurrencyClassRegistryTests
    {
        [Test]
        public void TheFirstDeclarationWins()
        {
            // Hạng của một đồng tiền là một SỰ THẬT về game, không phải cấu hình để ghi đè. Để lần
            // sau thắng thì hạng phụ thuộc thứ tự khởi động — cùng một build gửi hai hạng khác nhau
            // giữa hai phiên, và không ai soi ra.

            CurrencyClassRegistry.Declare("reg_gold_1", FCurrencyClass.Soft);
            CurrencyClassRegistry.Declare("reg_gold_1", FCurrencyClass.Hard);

            Assert.AreEqual(FCurrencyClass.Soft, CurrencyClassRegistry.ClassOf("reg_gold_1"));
        }

        [Test]
        public void AClassOutsideTheClosedSet_IsRefused_NotStored()
        {
            CurrencyClassRegistry.Declare("reg_gold_2", "unknown");

            Assert.IsNull(CurrencyClassRegistry.ClassOf("reg_gold_2"),
                "Vắng thì kho tra theo tên; một hạng lạ thì kho TIN và đọc sai net-flow");
        }

        [Test]
        public void ARefusedDeclaration_DoesNotBlockTheRealOne()
        {
            // Luật lần-đầu-thắng chỉ áp cho lời khai HỢP LỆ: một lời khai bị bỏ không được chiếm chỗ.
            CurrencyClassRegistry.Declare("reg_gold_3", "nonsense");
            CurrencyClassRegistry.Declare("reg_gold_3", FCurrencyClass.Soft);

            Assert.AreEqual(FCurrencyClass.Soft, CurrencyClassRegistry.ClassOf("reg_gold_3"));
        }

        [Test]
        public void CurrencyNameMatchesCaseInsensitively_ButUnderscoresAreSignificant()
        {
            CurrencyClassRegistry.Declare("Reg_Pow_Magnet", FCurrencyClass.Booster);

            Assert.AreEqual(FCurrencyClass.Booster, CurrencyClassRegistry.ClassOf("reg_pow_magnet"));
            Assert.AreEqual(FCurrencyClass.Booster, CurrencyClassRegistry.ClassOf("REG_POW_MAGNET"));
            Assert.IsNull(CurrencyClassRegistry.ClassOf("regpowmagnet"), "Tên tiền khác dấu _ là tên KHÁC");
        }

        [Test]
        public void AnUndeclaredName_ResolvesToNothing_AndIsNeverGuessed()
        {
            Assert.IsNull(CurrencyClassRegistry.ClassOf("reg_screw_4"));
            Assert.IsNull(CurrencyClassRegistry.ClassForLog("reg_screw_4"));
        }
    }

    /// <summary>Sổ khai mã booster — phương án (a) của phía đọc số, chốt 06/11.</summary>
    public class BoosterRegistryTests
    {
        [Test]
        public void RegisteredIdsMatchCaseInsensitively()
        {
            FBoosterRegistry.Register("Bst_Pow_Magnet");

            Assert.IsTrue(FBoosterRegistry.IsRegistered("bst_pow_magnet"));
            Assert.IsFalse(FBoosterRegistry.IsRegistered("bst_nothing_here"));
        }

        [Test]
        public void AnEmptyBook_MeansTheGameDoesNotUseThisMechanism_SoStaySilent()
        {
            // Sổ rỗng thì mọi mã đều "lạ" — nhắc về từng mã là ồn vô ích cho mọi game chưa dùng.
            // ⚠ Sổ là static nên test này chỉ đúng khi chạy TRƯỚC mọi test có Register; thay vì dựa
            // vào thứ tự, canh đúng điều quan trọng: mã lạ KHÔNG ném và KHÔNG chặn gì.
            FBoosterRegistry.WarnIfUnknown("bst_unknown_one", "test");
        }

        [Test]
        public void RegisteringABooster_DoesNotDeclareItsCurrencyClass()
        {
            // 🔴 Cố ý: loader §7.1 xin "một nguồn khai duy nhất cho mỗi game". Tự khai hạng ở đây là
            // mở đường ghi THỨ HAI vào sổ hạng, rồi nó cãi nhau với DeclareCurrency và — với luật
            // lần-đầu-thắng — kết quả phụ thuộc thứ tự khởi động.
            FBoosterRegistry.Register("bst_hammer_5");

            Assert.IsNull(CurrencyClassRegistry.ClassOf("bst_hammer_5"),
                "Sổ booster chỉ ghi TÊN; hạng vẫn phải khai bằng DeclareCurrency");
        }
    }

    /// <summary>
    /// Đóng dấu hạng lên dòng tài nguyên. Đặt ở <c>CorrectValues</c> vì đó là <b>cửa duy nhất mọi
    /// đường tạo dòng đều đi qua</b> — cả ba ctor của <c>FResourceLog</c>, đường
    /// <c>ResourceExchangeService</c>, và <c>OldCodeSupportResourceParam</c> của code cũ. Đóng ở
    /// service thì đường ctor legacy lọt, mà đó đúng là đường phần lớn dữ liệu đang đi.
    /// <br/>⚠ Mấy test này khai vào sổ THẬT (<c>Instance</c>), nên mỗi test dùng một tên tiền riêng.
    /// </summary>
    public class ResourceParamCurrencyClassTests
    {
        private static ResourceParam Row(string currency)
        {
            return new ResourceParam { currency = currency, itemType = "x", itemId = "x", amount = 1 };
        }

        [Test]
        public void ADeclaredName_GetsItsClassStampedWithoutTheCallSiteDoingAnything()
        {
            CurrencyClassRegistry.Declare("test_gold_a", FCurrencyClass.Soft);
            var param = Row("test_gold_a");

            param.CorrectValues();

            Assert.AreEqual(FCurrencyClass.Soft, param.currencyClass);
        }

        [Test]
        public void AnUndeclaredName_LeavesTheKeyAbsent()
        {
            var param = Row("test_screw_b");

            param.CorrectValues();

            Assert.IsNull(param.currencyClass, "Chưa khai thì VẮNG — không đoán, không mặc định");
        }

        [Test]
        public void AHandSetClass_BeatsTheBook()
        {
            CurrencyClassRegistry.Declare("test_gold_c", FCurrencyClass.Soft);
            var param = Row("test_gold_c");
            param.currencyClass = FCurrencyClass.EventCounter;

            param.CorrectValues();

            Assert.AreEqual(FCurrencyClass.EventCounter, param.currencyClass);
        }

        // ---- Hạng unlimited KHÔNG có số dư (ký với loader 12/11, AbsentNotZero mục 3) ----

        [Test]
        public void Unlimited_DropsBothBalanceFields_KeepsAmount()
        {
            // Đã có game nhét DateTime.Ticks của mốc hết hạn (~6,39e17) vào value_after.
            CurrencyClassRegistry.Declare("test_unlimited_a", FCurrencyClass.Unlimited);
            var param = Row("test_unlimited_a");
            param.amount = 1800;
            param.valueBefore = 639_000_000_000_000_000L;
            param.valueAfter = 639_000_018_000_000_000L;

            param.CorrectValues();

            Assert.AreEqual(FCurrencyClass.Unlimited, param.currencyClass);
            Assert.IsNull(param.valueBefore);
            Assert.IsNull(param.valueAfter);
            Assert.AreEqual(1800, param.amount, "amount = số giây cộng — giữ nguyên");
            var dict = param.ToDictionary();
            Assert.IsFalse(dict.ContainsKey("valueBefore"));
            Assert.IsFalse(dict.ContainsKey("valueAfter"));
        }

        [Test]
        public void Unlimited_ByHand_AlsoDropsBalance()
        {
            var param = Row("test_unlimited_b");
            param.currencyClass = "UNLIMITED";
            param.valueAfter = 5;

            param.CorrectValues();

            Assert.IsNull(param.valueAfter);
        }

        [Test]
        public void OtherClasses_KeepTheirBalance()
        {
            CurrencyClassRegistry.Declare("test_live_keep", FCurrencyClass.Live);
            var param = Row("test_live_keep");
            param.valueBefore = 3;
            param.valueAfter = 4;

            param.CorrectValues();

            Assert.AreEqual(3, param.valueBefore);
            Assert.AreEqual(4, param.valueAfter);
        }

        [Test]
        public void AHandSetClassOutsideTheClosedSet_IsDropped_NotTurnedIntoUnknown()
        {
            // 🔴 Cả vết sẹo của repo nằm ở đây: "Unknown" là một giá trị SAI mà trông hợp lệ. Trên
            // cột này nó còn tệ hơn — nó được đếm vào mẫu số "đã có hạng" nên độ phủ TRÔNG NHƯ đã
            // cải thiện, trong khi chẳng ai biết thêm gì.
            var param = Row("test_gold_d");
            param.currencyClass = "unknown";

            param.CorrectValues();

            Assert.IsNull(param.currencyClass);
        }

        [Test]
        public void TheLegacyPathIsCoveredToo()
        {
            // OldCodeSupportResourceParam là đường của code cũ — nó kế thừa ResourceParam nên đi qua
            // cùng cửa. Test này canh chuyện ai đó dời phép đóng dấu sang service.
            CurrencyClassRegistry.Declare("test_heart_e", FCurrencyClass.Live);
            var param = new OldCodeSupportResourceParam
            {
                currency = "test_heart_e", itemType = "x", itemId = "x", amount = 1
            };

            param.CorrectValues();

            Assert.AreEqual(FCurrencyClass.Live, param.currencyClass);
        }

        [Test]
        public void TheClassNameIsReservedOnTheWire()
        {
            Assert.IsTrue(FWireReservedKeys.IsReserved("currencyClass"));
            Assert.IsTrue(FWireReservedKeys.IsReserved("currency_class"));
        }
    }

    /// <summary>
    /// Sổ booster phải có NGƯỜI DÙNG ở đúng hai stream mà hợp đồng §2(a) chỉ tên — không thì nó là
    /// một cái sổ không ai tra, và lệch tên vẫn lệch. Mấy test này canh chuyện đó.
    /// </summary>
    public class BoosterIdConsumersTests
    {
        [Test]
        public void ALevelRowsBoosterKeys_AreCheckedAgainstTheBook()
        {
            // Không assert vào log (harness không bắt log) — assert vào chuyện đường này CHẠY và
            // không ném, cộng với việc bộ đếm không bị sửa. Giá trị của test: nếu ai bỏ lời gọi
            // WarnIfAnyUnknown khỏi CorrectValues thì ba test dưới vẫn xanh, nhưng lời gọi đó là một
            // dòng duy nhất và nó nằm cạnh vòng lọc số âm đã có test — đổi lại, test này ghim việc
            // CorrectValues của Start row GỌI BASE (ca hồi quy thật ở dưới).
            FBoosterRegistry.Register("csm_hammer");
            var param = new LevelStartParamV2
            {
                currentLevel = 7,
                preBoostersUsed = new System.Collections.Generic.Dictionary<string, int>
                {
                    ["csm_hammer"] = 1, ["csm_not_in_book"] = 2
                }
            };

            param.CorrectValues();

            Assert.AreEqual(2, param.preBoostersUsed.Count, "Mã lạ VẪN GỬI — chặn là mất dòng");
        }

        [Test]
        public void StartRow_NowRunsItsParentValidation()
        {
            // 🔴 Ca hồi quy 09/11: LevelStartParamV2.CorrectValues return sớm khi preBoostersUsed ==
            // null và KHÔNG gọi base, nên validate của ALevelParamV2 (currentLevel, difficulty) chưa
            // bao giờ chạy trên dòng Start — stream dày nhất hệ. Phát hiện lúc nối sổ booster vào
            // đúng hàm đó.
            var param = new LevelStartParamV2 { currentLevel = -5, difficulty = string.Empty };

            // Hai error log là ĐÚNG hành vi — chúng chính là bằng chứng lớp cha đã chạy. Phải khai
            // trước, không thì test runner của Unity đánh đỏ vì log error không ai nhận.
            LogAssert.Expect(LogType.Error, new Regex("currentLevel"));
            LogAssert.Expect(LogType.Error, new Regex("difficulty"));

            param.CorrectValues();

            Assert.AreEqual(0, param.currentLevel, "currentLevel âm phải bị lớp cha xử");
            Assert.AreEqual("Unknown", param.difficulty, "difficulty rỗng phải bị lớp cha xử");
        }

        [Test]
        public void ABoosterResourceRow_IsCheckedAgainstTheBook()
        {
            CurrencyClassRegistry.Declare("csm_magnet", FCurrencyClass.Booster);
            var param = new ResourceParam
            {
                currency = "csm_magnet", itemType = "x", itemId = "x", amount = 1,
                resourceWhen = FResourceWhen.BoosterUse
            };

            param.CorrectValues();

            Assert.AreEqual(FCurrencyClass.Booster, param.currencyClass,
                "Dòng vẫn mang hạng dù mã chưa có trong sổ booster");
        }

        [Test]
        public void ABlankCurrency_DoesNotAskTheGameToDeclareTheWordUnknown()
        {
            // CheckNonBlank biến tên rỗng thành "Unknown" TRƯỚC khi tới chỗ tra sổ. Nhắc game đi khai
            // hạng cho một cái tên mà chính SDK vừa bịa ra là một lời khuyên sai.
            var param = new ResourceParam
            {
                currency = string.Empty, itemType = "x", itemId = "x", amount = 1
            };

            LogAssert.Expect(LogType.Error, new Regex("currency"));

            param.CorrectValues();

            Assert.AreEqual("Unknown", param.currency);
            Assert.IsNull(param.currencyClass);
        }
    }
}
