using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>
    /// Cửa soi key <c>extraMeta</c> trùng tên field THẬT (<see cref="FExtraMeta"/>).
    /// <br/>Ca sinh ra nó: merge là <c>PutIfAbsentAndNotNull</c> = ai-ghi-trước-giữ, mà field thật
    /// ghi trước ⇒ key extras trùng tên bị BỎ, im lặng. Bản tin vẫn đi, cột vẫn có giá trị — của
    /// SDK, không phải của game.
    /// <br/>Trước lượt 06/11 chỉ <c>FeatureParam</c> có cửa soi này; mười hai chỗ còn lại mất key
    /// mà không ai hô. Test cuối file là cái chặn chỗ thứ mười ba.
    /// </summary>
    public class ExtraMetaGuardTests
    {
        [Test]
        public void StealsARealField_CatchesEverySpelling()
        {
            // Tên trên wire bị chuẩn hoá ở phía sau, nên camel và snake cuối cùng đụng nhau
            Assert.IsTrue(FExtraMeta.StealsARealField(typeof(ResourceParam), "amount"));
            Assert.IsTrue(FExtraMeta.StealsARealField(typeof(ResourceParam), "AMOUNT"));
            Assert.IsTrue(FExtraMeta.StealsARealField(typeof(ResourceParam), "item_type"),
                "snake_case của itemType phải bắt được");
            Assert.IsTrue(FExtraMeta.StealsARealField(typeof(ResourceParam), "itemType"));
        }

        [Test]
        public void StealsARealField_LeavesNormalGameKeysAlone()
        {
            Assert.IsFalse(FExtraMeta.StealsARealField(typeof(ResourceParam), "team_quest_map"));
            Assert.IsFalse(FExtraMeta.StealsARealField(typeof(ResourceParam), null));
            Assert.IsFalse(FExtraMeta.StealsARealField(null, "amount"));
        }

        [Test]
        public void TwoOpenDoors_AreNotRealFields()
        {
            // extraMeta và param là CỬA MỞ của chính param, không phải field hợp đồng — ai đặt key
            // tên "param" thì không lấy mất chỗ của ai cả.
            Assert.IsFalse(FExtraMeta.StealsARealField(typeof(EventParam), "extraMeta"));
            Assert.IsFalse(FExtraMeta.StealsARealField(typeof(EventParam), "param"));
        }

        [Test]
        public void InheritedFieldsCount_SoSubclassesAreCoveredToo()
        {
            // OldCodeSupportResourceParam không khai lại `amount` — nó thừa kế. Gom theo KIỂU THẬT
            // lúc chạy nên field của cha vẫn tính.
            Assert.IsTrue(FExtraMeta.StealsARealField(typeof(OldCodeSupportResourceParam), "amount"));
        }

        [Test]
        public void MergeExtras_RealFieldWins_AndThatIsExactlyWhyWeWarn()
        {
            var target = new Dictionary<string, object> { ["amount"] = 7L };
            target.MergeExtras(new Dictionary<string, object> { ["amount"] = 999L },
                new ResourceParam());

            Assert.AreEqual(7L, target["amount"],
                "Field thật ghi trước nên nó thắng — giá trị extras mất, và mất im lặng");
        }

        [Test]
        public void MergeExtras_KeepsKeysThatDoNotCollide()
        {
            var target = new Dictionary<string, object> { ["amount"] = 7L };
            target.MergeExtras(new Dictionary<string, object> { ["team_quest_map"] = 3 },
                new ResourceParam());

            Assert.AreEqual(3, target["team_quest_map"]);
        }

        [Test]
        public void MergeExtras_SurvivesNothingToDo()
        {
            var target = new Dictionary<string, object>();
            Assert.AreSame(target, target.MergeExtras(null, new ResourceParam()));
            Assert.AreSame(target, target.MergeExtras(new Dictionary<string, object>(), null));
            Assert.AreEqual(0, target.Count);
        }

        // ── Cái chặn chỗ thứ mười ba ────────────────────────────────────────────

        /// <summary>
        ///     Mọi lớp có cửa <c>extraMeta</c> phải đi qua <see cref="FExtraMeta.MergeExtras"/>:
        ///     key extras trùng tên một field thật KHÔNG được đè field đó trên wire.
        ///     <br/>Test này không soi code, nó soi HÀNH VI — nên lớp thứ mười ba thêm vào mà quên
        ///     dùng helper sẽ đỏ ở đây, không phải đỏ ở production sáu tháng sau.
        /// </summary>
        [Test]
        public void EveryTypeWithExtraMeta_KeepsItsRealFieldOnTheWire()
        {
            var checked_ = new List<string>();

            foreach (var type in typeof(FExtraMeta).Assembly.GetTypes())
            {
                if (type.IsAbstract || type.GetConstructor(Type.EmptyTypes) == null) continue;

                // Chỉ lớp PARAM: lớp LOG (PlainLog) có field initializer gọi singleton nên dựng
                // trần là ném — ngoài tầm test thuần. Chỗ extras duy nhất ở mức log là
                // FUiExposureLog, và nó dùng cùng một helper.
                if (!typeof(FParam).IsAssignableFrom(type)) continue;

                var extras = type.GetField("extraMeta", BindingFlags.Instance | BindingFlags.Public);
                if (extras == null || extras.FieldType != typeof(Dictionary<string, object>)) continue;

                var toDictionary = type.GetMethod("ToDictionary", Type.EmptyTypes);
                if (toDictionary == null) continue;

                // Một field chuỗi thật, ghi được, để làm mồi
                var bait = type.GetFields(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(f => f.FieldType == typeof(string) && f.Name != "extraMeta");
                if (bait == null) continue;

                const string kept = "__REAL_VALUE__";
                var instance = Activator.CreateInstance(type);
                bait.SetValue(instance, kept);

                var baseline = (Dictionary<string, object>)toDictionary.Invoke(instance, null);
                var wireKey = baseline?.FirstOrDefault(kv => Equals(kv.Value, kept)).Key;
                if (string.IsNullOrEmpty(wireKey)) continue;   // field này không lên wire

                var attacker = Activator.CreateInstance(type);
                bait.SetValue(attacker, kept);
                extras.SetValue(attacker, new Dictionary<string, object> { [wireKey] = "__STOLEN__" });

                var merged = (Dictionary<string, object>)toDictionary.Invoke(attacker, null);
                Assert.AreEqual(kept, merged[wireKey],
                    $"{type.Name}: key extras '{wireKey}' đè được field thật — chỗ này chưa đi qua " +
                    "FExtraMeta.MergeExtras, hoặc merge không còn là ai-ghi-trước-giữ");
                Assert.IsFalse(merged.ContainsKey("extraMeta"),
                    $"{type.Name}: extraMeta không được lên dây nguyên khối");

                checked_.Add(type.Name);
            }

            // Mức sàn, không phải mức chốt (lượt 06/11 soi được 25 lớp): thêm stream là số này chỉ tăng. Nó ở đây để
            // một lượt refactor làm vòng lặp trên không soi được lớp nào nữa thì test đỏ, chứ không
            // xanh vì "không có gì để soi".
            Assert.GreaterOrEqual(checked_.Count, 20,
                "Soi được quá ít lớp — vòng lặp trên hỏng, không phải code sạch: " +
                string.Join(", ", checked_));
        }
    }
}
