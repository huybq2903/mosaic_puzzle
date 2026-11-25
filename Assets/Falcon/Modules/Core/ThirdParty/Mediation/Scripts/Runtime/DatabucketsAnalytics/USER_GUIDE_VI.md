# Databuckets Analytics 4.0.1 — Hướng dẫn sử dụng

[English](USER_GUIDE.md) | [Tiếng Việt](USER_GUIDE_VI.md)

Phiên bản SDK: **4.0.1**. Yêu cầu Unity **2022.3 trở lên**, thiết bị có bộ nhớ cục bộ ghi được và hỗ trợ luồng nền. Không hỗ trợ WebGL.

Hướng dẫn này trình bày cách tích hợp và sử dụng thường gặp. Xem [hướng dẫn đầy đủ bằng tiếng Anh](USER_GUIDE.md) để tra cứu toàn bộ API và giới hạn cấu hình; [README.md](README.md) mô tả chi tiết các trường sự kiện và cơ chế gửi dữ liệu.

## 1. Cài đặt và khởi tạo

1. Trong **Window → Package Manager**, cài `com.unity.nuget.newtonsoft-json` phiên bản `3.2.1`.
2. Import `DatabucketsAnalytics-4.0.1.unitypackage`. Giữ toàn bộ tệp, kể cả thư mục `Editor`.
3. Tạo `DatabucketsBootstrap.cs`, gắn vào GameObject trong scene đầu tiên. Thay URL HTTPS `/push` và API key bằng thông tin được cung cấp.

Nếu dùng Package Manager, chọn **Window → Package Manager → + → Add package from tarball** rồi chọn `com.realtimeevents.sdk-4.0.1.tgz`; Unity sẽ cài các dependency đã khai báo. Chỉ chọn một cách cài đặt để tránh trùng assembly.

```csharp
using System.Collections.Generic;
using Databuckets.Analytics;
using UnityEngine;

public sealed class DatabucketsBootstrap : MonoBehaviour
{
    private async void Awake()
    {
        try
        {
            // Truyền thông tin ban đầu trước khi SDK tạo sự kiện khởi động.
            DatabucketsTracker.Init("https://events.example.com/push", "YOUR_API_KEY",
                new Dictionary<string, object>
                {
                    ["plan"] = "premium",
                    ["language"] = "vi"
                });
            await DatabucketsTracker.Ready;

            DatabucketsTracker.Record("game_started");
        }
        catch (System.Exception error)
        {
            Debug.LogWarning("Lỗi analytics: " + error.Message);
        }
    }
}
```

Gọi `Init` trên luồng chính của Unity. Hàm này trả về trước khi quá trình khởi tạo trên luồng nền hoàn tất. Có thể gọi `Record` và `SetUserProperties` ngay sau khi `Init` trả về: các lệnh được xếp hàng sau lệnh khởi tạo. `await Ready` giúp phát hiện lỗi khởi tạo và đọc các giá trị đã nạp; không có nghĩa là máy chủ đã nhận sự kiện.

**Không hủy hoặc tắt GameObject “Databuckets Analytics”** do SDK tạo trong quá trình sử dụng; nó tự hoạt động qua các scene và khi quay lại từ quảng cáo.

### Thuộc tính ban đầu và thứ tự xử lý

Dictionary truyền vào `Init` được chụp lại trong lúc gọi hàm, sau đó được gộp và lưu với thuộc tính đã có **trước khi tạo `first_open` và `session_start` ban đầu**. Các sự kiện ghi ngay sau `Init` cũng nhận những giá trị này. Lời gọi cũ `Init(endpoint, apiKey)` vẫn dùng được.

- Khóa được truyền vào ghi đè giá trị đã lưu; các khóa khác được giữ nguyên. Dictionary null hoặc rỗng không bổ sung giá trị nào. Giá trị null của một thuộc tính được lưu thành JSON null.
- Thuộc tính được lưu qua các lần mở ứng dụng. Các trường do SDK quản lý vẫn tuân theo thứ tự ưu tiên của SDK.
- Không thay đổi dictionary hoặc các giá trị lồng bên trong khi `Init` đang chụp dữ liệu. Thay đổi sau khi hàm trả về không làm thay đổi bản đã chụp.
- Dữ liệu đầu vào không hợp lệ hoặc vượt giới hạn gây exception ngay trong `Init`. Nếu kết quả gộp vượt giới hạn hoặc lưu thất bại, `Ready` báo lỗi trước khi tạo sự kiện mới.
- Gọi lại `Init` với cùng endpoint/API key khi tracker còn hoạt động sẽ bỏ qua dictionary ban đầu; cấu hình endpoint/API key khác sẽ gây exception. Dùng `SetUserProperties` để cập nhật sau đó.

Ví dụ gọi liên tiếp, không cần đợi `Ready`:

```csharp
using System.Collections.Generic;
using Databuckets.Analytics;

DatabucketsTracker.Init("https://events.example.com/push", "YOUR_API_KEY",
    new Dictionary<string, object> { ["plan"] = "premium" });
DatabucketsTracker.Record("game_started"); // plan = premium.
DatabucketsTracker.SetUserProperties(("plan", "free"));
DatabucketsTracker.Record("plan_changed"); // plan = free nếu cập nhật thành công.
```

Các lệnh chạy theo thứ tự được đưa vào hàng đợi. Sự kiện ghi sau lời gọi cập nhật sẽ dùng giá trị mới nếu cập nhật thành công, kể cả khi khởi tạo chưa xong. Khi gọi từ nhiều luồng, cần tự phối hợp thứ tự nếu các lệnh phụ thuộc nhau. Cập nhật không sửa những sự kiện đã tạo, bao gồm sự kiện khởi động. Vì vậy, gọi `SetUserProperties` sau `await Ready` không thể bổ sung thuộc tính cho `first_open` đã tạo.

`Ready` chỉ chờ khởi tạo, bao gồm việc gộp/lưu thuộc tính ban đầu; không chờ những cập nhật gửi sau đó. Để xác nhận một cập nhật qua API tĩnh, gọi `await DatabucketsTracker.PersistAsync()` sau cập nhật và xử lý exception. Khi dùng trực tiếp `EventClient`, có thể chờ `SetUserPropertiesAsync()`.

`SetUserProperties()` đặt **thông tin người chơi dùng chung cho các sự kiện tiếp theo**, ví dụ `plan` và `language`. SDK tự lưu qua các lần mở ứng dụng. Gọi lại khi thông tin thay đổi; không cần gọi trước mỗi sự kiện.

Trên Android và iOS, SDK tự thu thập ngôn ngữ ưu tiên của thiết bị vào `device.user_default_language`, dưới dạng mã ngôn ngữ viết thường như `en-us` hoặc `vi-vn`. Khi khởi tạo, SDK chuyển `device.language` đã lưu sang khóa mới; nếu khóa mới đã có giá trị thì giữ giá trị đó. Các sự kiện đã nằm trong hàng đợi không bị sửa. Nếu không lưu được thay đổi hoặc kết quả vượt giới hạn kích thước, `Ready` báo lỗi. Khóa `language` trong ví dụ là thuộc tính người chơi do ứng dụng tự đặt, tách biệt với trường tự động này.

## 2. Ghi sự kiện kèm tham số

Thêm `using Databuckets.Analytics;` và `using System.Collections.Generic;` ở đầu script ghi sự kiện. Gọi một lần khi hành động xảy ra:

```csharp
using System.Collections.Generic;
using Databuckets.Analytics;

try
{
    DatabucketsTracker.Record("level_completed", new Dictionary<string, object>
    {
        ["level"] = 12,
        ["score"] = 9500,
        ["won"] = true
    });
}
catch (System.Exception error)
{
    UnityEngine.Debug.LogWarning("Lỗi analytics: " + error.Message);
}
```

`level`, `score`, `won` chỉ thuộc sự kiện này. Sự kiện cũng nhận `plan`, `language` đã đặt ở trên. Tránh dùng cùng tên cho hai loại thông tin: nếu trùng, thuộc tính người chơi được ưu tiên. Không có tham số thì dùng `Record("game_started")`.

**Không cần gọi `Persist()`, `PersistAsync()` hay `Flush()` sau mỗi sự kiện.** SDK tự xử lý lưu/gửi và việc chuyển ứng dụng ra nền.

## 3. Khi nào cần gọi hàm lưu?

**Game chủ động mở quảng cáo, trình duyệt hoặc cửa hàng:** nếu muốn chờ lưu sự kiện trước khi chuyển, dùng `PersistAsync()`. Ví dụ trong một hàm `async`, gọi trên luồng chính của Unity:

```csharp
using Databuckets.Analytics;

try
{
    DatabucketsTracker.Record("store_open_requested");
    await DatabucketsTracker.PersistAsync();
    UnityEngine.Application.OpenURL("https://example.com/your-store-page");
}
catch (System.Exception error)
{
    UnityEngine.Debug.LogWarning("Chưa xác nhận lưu analytics: " + error.Message);
}
```

**SDK quảng cáo tự chuyển ứng dụng, bạn chỉ nhận callback click:** dùng `Persist()` để yêu cầu lưu ngay, không chờ callback pause/focus. Đặt đoạn này trong callback click:

```csharp
using Databuckets.Analytics;

try
{
    DatabucketsTracker.Record("ad_clicked");
    DatabucketsTracker.Persist();
}
catch (System.Exception error)
{
    UnityEngine.Debug.LogWarning("Lỗi analytics: " + error.Message);
}
```

`Persist()` không chờ lưu xong. `PersistAsync()` chờ lưu dữ liệu chưa bắt đầu gửi trên máy; **không xác nhận máy chủ đã nhận** và không bảo vệ yêu cầu đang gửi.

Ghi reward/revenue ngay trong callback tương ứng. Không đợi đến lúc thoát game mới ghi/lưu. Không tự gọi lại `Record()` chỉ vì lưu thất bại: nếu sự kiện đã được nhận, chỉ thử lại việc lưu để tránh ghi trùng.

**Không cách nào bảo đảm lưu xong nếu hệ điều hành tắt ứng dụng đột ngột.** Nếu có lỗi, kiểm tra `DatabucketsTracker.LastError`; không xóa dữ liệu SDK để xử lý lỗi.

## 4. Thuộc tính ban đầu khi dùng cấu hình riêng

Nếu cần đổi giới hạn hoặc thư mục lưu, dùng `EventTracker.Create(options)` và đặt `EventOptions.InitialUserProperties`. Tạo tracker một lần trên luồng chính, giữ tham chiếu và dùng các phương thức của tracker đó cho những lần gọi tiếp theo; nó không khởi tạo API tĩnh `DatabucketsTracker`.

```csharp
using System.Collections.Generic;
using RealtimeEvents;
using UnityEngine;

public sealed class CustomAnalyticsBootstrap : MonoBehaviour
{
    public static EventTracker Analytics { get; private set; }

    private async void Awake()
    {
        try
        {
            if (Analytics != null) return;
            Analytics = EventTracker.Create(new EventOptions
            {
                Endpoint = "https://events.example.com/push",
                ApiKey = "YOUR_API_KEY",
                InitialUserProperties = new Dictionary<string, object>
                {
                    ["plan"] = "premium", ["language"] = "vi"
                },
                MaxBatchEvents = 25,
                MaxUserPropertiesBytes = 8 * 1024
            });
            await Analytics.Ready;
            Analytics.Track("game_started");
        }
        catch (System.Exception error)
        {
            Debug.LogWarning("Lỗi analytics: " + error.Message);
        }
    }
}
```

Chỉ dùng một trong hai bootstrap: API tĩnh mặc định hoặc tracker có cấu hình riêng. Không chạy cả hai cho cùng nơi lưu dữ liệu. `new EventClient(options)` cũng hỗ trợ `InitialUserProperties`, nhưng bạn phải tự quản lý vòng lặp và vòng đời ứng dụng.

| Thành phần | Cách dùng |
| --- | --- |
| `Init(endpoint, apiKey, initialUserProperties)` | Đặt thuộc tính trước sự kiện khởi động khi tạo tracker mới; dictionary là tùy chọn. |
| `InitialUserProperties` | Kiểu `IDictionary<string, object>`, mặc định null. Dữ liệu tương thích JSON được chụp khi tạo client. |
| `MaxUserPropertiesBytes` | Mặc định **65.536 byte**; cho phép từ **2** đến **1.048.576 byte**. Giới hạn áp dụng cho cả dữ liệu đầu vào và kết quả gộp với thuộc tính đã lưu, bao gồm thuộc tính tự động của SDK. |
| `Ready` | Chờ khởi tạo và lưu thuộc tính ban đầu; xử lý exception nếu thất bại. |
| `SetUserProperties(...)` | Xếp hàng cập nhật và lưu thuộc tính cho sự kiện về sau. Hàm trả về trước khi lưu xong. |
| `PersistAsync()` | Chờ lưu các sự kiện đã nhận nhưng chưa bắt đầu gửi còn lại, trong giới hạn dung lượng; cũng báo lỗi thao tác trước đó chưa được báo qua lần chờ lưu. Không chờ HTTP. |

Nếu `Ready` lỗi, kiểm tra exception và `LastError`, chờ `Completion` hoàn tất trong khối xử lý lỗi, khắc phục nguyên nhân rồi mới gọi lại `Init`. Không chặn luồng chính bằng `.Wait()` hoặc `.Result`.
