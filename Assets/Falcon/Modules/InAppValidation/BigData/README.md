# 📦 Falcon IAP Log - BigData

## 1. Mô tả

- **Giới thiệu:**  
  Module mở rộng cho hệ thống In-App Purchase, dùng để log dữ liệu giao dịch IAP về hệ thống Falcon BigData.

- **Thành phần chính:**

    - BigdataLogger: class  kế thừa từ PurchaseLog có tác dụng gửi log đến hệ thống BigData

## 2. Quick Start

### Cài Module là xong

### Custom InAppParam

Có thể tạo custom `InAppParam` bằng cách kế thừa từ InAppParam, sau đó sử dụng `BigdataLogger.CustomInAppParam` để set custom InAppParam trước khi log.
Các trường trong `InAppParam` đã được tự động điền, nên chỉ cần cung cấp các trường cần custom.
```csharp
public class CustomInAppParam : InAppParam
{
    public string customField;
}
```


Dành cho trường hợp muốn custom `CustomInAppParam` trước khi log
```csharp
BigdataLogger.CustomInAppParam(new CustomInAppParam
{
    customField = "your_custom_field",
});
```
Dành cho trường hợp muốn tạo default `CustomInAppParam`
```csharp
BigdataLogger.CreateDefaultInAppParam = () => new CustomInAppParam
{
    customField = "your_custom_field",
};
```


## 3. Chi tiết xử lý nội bộ

- Khi user mua thành công, `IAPManager` sẽ gọi `BigdataLogger` để log
