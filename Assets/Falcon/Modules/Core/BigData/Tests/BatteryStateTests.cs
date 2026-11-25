using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>Hợp đồng đợt 3 B3 (đợt 1): pin từ API Unity + sự kiện đổi trạng thái sạc.</summary>
    public class BatteryStateTests
    {
        [Test]
        public void FromUnity_MapsLevelAndStatus_UnknownIsAbsent()
        {
            var reading = BatteryReading.FromUnity(0.734f, "Charging");
            Assert.AreEqual(73, reading.Pct);
            Assert.AreEqual(true, reading.Charging);

            Assert.AreEqual(true, BatteryReading.FromUnity(1f, "Full").Charging, "đầy mà vẫn cắm = đang cắm sạc");
            Assert.AreEqual(false, BatteryReading.FromUnity(0.5f, "Discharging").Charging);
            Assert.AreEqual(false, BatteryReading.FromUnity(0.5f, "NotCharging").Charging);

            var unknown = BatteryReading.FromUnity(-1f, "Unknown");
            Assert.IsNull(unknown.Pct, "-1 của Unity = không biết, không gửi");
            Assert.IsNull(unknown.Charging);
        }

        [Test]
        public void ChargingChange_FiresOnlyOnRealChange()
        {
            var state = new BatteryState();
            Assert.IsFalse(state.Record(new BatteryReading(50, false)), "lần đọc đầu không phải đổi");
            Assert.IsFalse(state.Record(new BatteryReading(49, false)), "chỉ % đổi thì không bắn");
            Assert.IsTrue(state.Record(new BatteryReading(49, true)));
            Assert.IsFalse(state.Record(new BatteryReading(48, null)), "không biết thì không phải đổi");
            Assert.IsFalse(state.Record(new BatteryReading(48, true)), "so với lần đọc trước (không biết)");
        }

        [Test]
        public void PutInto_OmitsUnknownFields()
        {
            var dictionary = new System.Collections.Generic.Dictionary<string, object>();
            new BatteryReading(null, true).PutInto(dictionary);
            Assert.IsFalse(dictionary.ContainsKey("batteryPct"));
            Assert.AreEqual(true, dictionary["batteryCharging"]);
        }
    }
}
