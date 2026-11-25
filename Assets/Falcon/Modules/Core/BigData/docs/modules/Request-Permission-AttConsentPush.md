# Yêu cầu: ba event quyền/consent chưa có emitter nào — gửi owner module `Core/ThirdParty/Ump` và `Notification`

> **Người gửi:** owner module `Core/BigData`
> **Người nhận:** §A → owner `Core/ThirdParty/Ump` (`FalconUMP`, 1.2.14) · §B → owner
> `Modules/Notification` (`NotificationManager`, 1.0.13)
> **Ngày:** viết 10/08/2026, **viết lại 09/11/2026** (bản cũ `Request-Ump-PermissionLog.md` đã lệch
> code: Ump giờ là 1.2.14, và 🔴 luật đọc consent ở bản cũ **SAI** — xem §A2)
> **Căn cứ:** hợp đồng `sdk-contract-vnext.md` §D8
> **Việc cần làm:** §A hai lời gọi · §B một lời gọi. Không đổi luồng, không đổi UI, không thêm thư viện.

## Trạng thái: cả ba event đều TRỐNG

Bên mình grep toàn `Assets/Falcon`: **không một lời gọi nào** tới `App.ReportPermission` /
`ReportAttStatus` / `ReportAdsConsent` / `ReportPushPermission`. Hạ tầng đủ cả từ 1.3.x — chỉ không
ai phát.

| Event | Ai điền được | Thiếu nó thì sao |
|---|---|---|
| `f_sdk_permission_att` | §A — Ump | eCPM và match-rate attribution phụ thuộc thẳng vào nó. So doanh thu ads giữa hai nhóm user mà không biết tỉ lệ ATT thì rất có thể đang đo **lệch tỉ lệ ATT** chứ không phải đo cái mình tưởng |
| `f_sdk_permission_ads_consent` | §A — Ump | y như trên, cho thị trường EU |
| `f_sdk_permission_push` | §B — Notification | **mẫu số của mọi phân tích push**: "bao nhiêu % user còn nghe được mình". Không có nó thì mọi tỉ lệ mở push đều chia cho một số sai |

## API (dùng chung cho cả §A và §B)

```csharp
using Falcon.Modules.Core.BigData;

FalconBigDataController.App.ReportAttStatus(AttStatus.Denied);
FalconBigDataController.App.ReportAdsConsent(granted: true);
FalconBigDataController.App.ReportPushPermission(granted: false);
```

Ba điều làm việc này rẻ hơn tưởng:

- **Gọi thoải mái, kể cả mỗi lần khởi động.** `PermissionLogService` nhớ giá trị đã gửi lần trước
  và **báo trùng không đẻ log**. Nhờ vậy *"user tự đổi trong Settings của máy rồi quay lại game"*
  bắt được mà không phình volume.
- 🔴 **NHƯNG phải chờ BigData init — đây là chỗ bản cũ của bên mình nói sai.** Bản cũ ghi *"không
  cần chờ BigData init"*. Phần *log* thì đúng (vào hàng đợi rồi mới gửi batch), nhưng **chạm tới API
  thì không**: `FalconBigDataController.App` là `Instance.AppApi`, mà `Instance` của `MySingleton`
  **ném `SingletonException`** nếu singleton chưa được tạo — không trả null, không im lặng. `FalconUMP`
  chạy **rất sớm**, nên một exception ở đó có thể chặn luôn `FALCON_UMP_COMPLETE` (và mọi thứ xếp sau
  nó). Hai cách an toàn, chọn một:

  ```csharp
  // (a) gọi SAU khi UMP đã emit xong — đơn giản nhất
  GameEvent<bool>.Emit(FALCON_UMP_COMPLETE);
  FalconBigDataController.App.ReportAttStatus(...);

  // (b) hoặc hỏi trước, nếu cần gọi sớm
  if (MySingletonService.TryGetInstance<FalconBigDataController>(out _))
      FalconBigDataController.App.ReportAttStatus(...);
  ```

  ⚠ Nếu chọn (b) thì phải chắc có **một đường gọi lại** khi BigData đã sẵn sàng, không thì lần đầu
  trên máy sẽ rơi im lặng. Chỗ đọc-lúc-khởi-động ở A1(2) là đường đó.
- **Mỗi loại quyền một event riêng**, tên event do enum sinh nên không gõ sai được.

🔴 **Luật xuyên suốt cả hai phần: KHÔNG BIẾT thì KHÔNG GỬI.** Không gửi thì người đọc đi tìm; gửi
một giá trị sai trông hợp lệ thì người đọc dùng luôn. Mọi cái ⚠ dưới đây đều là một ca của luật này.

---

# §A — module `Core/ThirdParty/Ump`: ATT + consent quảng cáo

## A1. ATT — hai chỗ gọi

Trong `FalconUMP`:

**(1) Sau khi trạng thái đã xác định** — trong `WaitUntilDetermined(bool showAtt)`, ngay trước
`GameEvent<bool>.Emit(FALCON_UMP_COMPLETE)`:

```csharp
GameEvent<bool>.Emit(FALCON_UMP_COMPLETE);
FalconBigDataController.App.ReportAttStatus(ToFalconAtt(      // SAU Emit — xem mục API
    ATTrackingStatusBinding.GetAuthorizationTrackingStatus()));
```

⚠ **Gọi SAU `Emit`**, không phải trước. Lý do ở mục **API** trên: `App` ném exception nếu BigData
chưa init, và chặn `FALCON_UMP_COMPLETE` thì chặn cả dây sau nó. Thứ tự này không mất gì — event
không phụ thuộc vào việc nó được gửi trước hay sau `Emit`.

**(2) Và một lần lúc khởi động, TRƯỚC prompt** — đầu `OnShowPopupATT()`:

```csharp
FalconBigDataController.App.ReportAttStatus(ToFalconAtt(
    ATTrackingStatusBinding.GetAuthorizationTrackingStatus()));
```

Chỗ (2) là chỗ bắt ca user **đã trả lời từ phiên trước** hoặc **vừa đổi trong Settings ngoài app**.
Không có nó thì mẫu số chỉ có người mới. Báo trùng không đẻ log nên gọi cả hai chỗ là miễn phí.

Hàm map, đặt cạnh hai chỗ trên:

```csharp
private static AttStatus ToFalconAtt(ATTrackingStatusBinding.AuthorizationTrackingStatus status)
{
    return status switch
    {
        ATTrackingStatusBinding.AuthorizationTrackingStatus.AUTHORIZED   => AttStatus.Authorized,
        ATTrackingStatusBinding.AuthorizationTrackingStatus.DENIED       => AttStatus.Denied,
        ATTrackingStatusBinding.AuthorizationTrackingStatus.RESTRICTED   => AttStatus.Restricted,
        _                                                                => AttStatus.NotDetermined
    };
}
```

⚠ **`RESTRICTED` KHÔNG phải `DENIED`.** Restricted là bị chặn ở tầng thiết bị (parental control,
MDM) — hỏi cũng không hiện prompt. Gộp hai cái làm một là mất đúng nhóm user **không bao giờ
opt-in được**, tức làm hỏng chính mẫu số mà người ta đang muốn đo.

⚠ **Đừng tự bọc `#if UNITY_IOS` quanh lời gọi.** `ReportAttStatus` **đã tự chặn bên trong**: trên
Android nó in một dòng Info rồi thôi. Nhưng **chỗ gọi phải nằm trong đúng define mà
`ATTrackingStatusBinding` đang sống** (`UNITY_IOS && UNITY_ADS_IOS_SUPPORT_ENABLE`), không thì
không compile.

## A2. 🔴 Consent — và chỗ bản ticket cũ của bên mình nói SAI

Code hiện tại, trong callback của `ConsentForm.LoadAndShowConsentFormIfRequired`:

```csharp
var consent = ConsentInformation.ConsentStatus;
SaveLoadHandler.Save(FALCON_UMP_IS_IN_EUROPE, consent == ConsentStatus.Required);
SaveLoadHandler.Save(FALCON_UMP_HAS_CONSENT,  consent == ConsentStatus.Obtained);
```

🔴 **Ticket cũ của bên mình bảo cứ gửi `ReportAdsConsent(consent == Obtained)`. Rút lại.**

`ConsentStatus.Obtained` của UMP nghĩa là **"user đã trả lời cái form"** — *không* phải "user đã
đồng ý". Người bấm **"Do not consent"** cũng ra `Obtained`. Nên map `Obtained → granted` sẽ dán nhãn
**đồng ý** lên toàn bộ người EU **từ chối**, và cột đó sẽ nói ngược sự thật.

Mà đây là kiểu sai tệ nhất: con số vẫn đẹp, vẫn có phân bố granted/denied trông hợp lý, không ai
nghi. (`FALCON_UMP_HAS_CONSENT` trong code cũng đang mang nghĩa này — việc dùng nó nội bộ thế nào là
quyền của module, bên mình chỉ nói về **cái gửi lên dây**.)

### Lấy "đồng ý" ở đâu cho đúng

Sự đồng ý thật nằm trong **TC string** của IAB TCF (`IABTCF_TCString` trong `PlayerPrefs`), ở
**purpose consents** — purpose **1** (lưu/đọc thông tin trên thiết bị) và purpose **4** (chọn
quảng cáo cá nhân hoá).

Trong repo **đã có sẵn một parser**: `TCStringParser` (module Mediation,
`Service/GoogleMobileAds/TCStringParser.cs`) — nó decode core string và phơi ra
`HasPurposeConsent(int)`, kèm hai hằng số `PurposeStoreAccess = 1` và `PurposePersonalisedAds = 4`.

⚠ File đó hiện **chưa commit và CHƯA CÓ AI GỌI**. Nên trước khi dùng, hai owner Ump + Mediation cần
chốt nó thuộc về đâu — bên mình không có quyền sửa cả hai module nên chỉ nêu.

```csharp
// Ý tưởng — tên API tuỳ chỗ parser được đặt sau khi hai bên chốt
var raw = <đọc "IABTCF_TCString">;                       // ⚠ xem hai ô dưới
if (string.IsNullOrEmpty(raw)) return;                   // 🔴 BẮT BUỘC — xem ngay dưới
var tc = TCStringParser.Parse(raw);
var granted = tc.HasPurposeConsent(TCStringParser.PurposeStoreAccess)
           && tc.HasPurposeConsent(TCStringParser.PurposePersonalisedAds);
FalconBigDataController.App.ReportAdsConsent(granted);
```

🔴 **Cái `if` rỗng đó là bắt buộc, không phải phòng xa.** `TCStringParser.Parse(null)` và
`Parse("")` **trả về `new TCString()`** — tức mọi `PurposeConsents` đều `false`. Thiếu một dòng
chuỗi rỗng mà vẫn gửi thì **"không đọc được" ra y như "user từ chối"**: lại đúng cái giá trị sai
trông hợp lệ mà cả thư này đang đi gỡ.

⚠ **Và có một đường rẻ hơn, nên thử trước khi dùng parser.** CMP theo TCF còn ghi sẵn
**`IABTCF_PurposeConsents`** — một chuỗi `'0'`/`'1'` đánh chỉ số theo purpose. Nếu UMP ghi khoá đó
thì không cần parse TC string chút nào:

```csharp
var pc = <đọc "IABTCF_PurposeConsents">;
if (string.IsNullOrEmpty(pc) || pc.Length < 4) return;    // chưa biết -> không gửi
var granted = pc[0] == '1' && pc[3] == '1';               // purpose 1 và 4
```

Đi đường này thì `TCStringParser` (file chưa commit, chưa ai gọi) khỏi phải nhận chủ.

🔴 **Chỗ phải kiểm trước khi code: trên Android, `PlayerPrefs` có đọc được khoá `IABTCF_*` không.**
CMP ghi chúng vào **default `SharedPreferences`** của app, còn Unity `PlayerPrefs` trên Android dùng
**file prefs riêng của nó**. Nếu đúng vậy thì `PlayerPrefs.GetString("IABTCF_TCString")` trên Android
luôn ra chuỗi rỗng — và GDPR thì phần lớn nằm ở Android, nên cột này sẽ **trắng đúng chỗ cần nhất**.
Dấu hiệu: chỗ duy nhất trong repo đang đọc khoá IABTCF (`PlayerPrefs.GetString("IABTCF_AddtlConsent")`
trong `FalconUMP`) nằm trong `#if UNITY_IOS` — iOS thì `NSUserDefaults` dùng chung nên đọc được.

Nếu kiểm ra là không đọc được, đường Android phải qua JNI:

```csharp
using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
using var prefsMgr = new AndroidJavaClass("android.preference.PreferenceManager");
using var prefs = prefsMgr.CallStatic<AndroidJavaObject>("getDefaultSharedPreferences", activity);
var raw = prefs.Call<string>("getString", "IABTCF_TCString", null);
```

Xin kiểm rồi báo lại — bên mình **không** đoán hộ, vì đoán sai ở đây là một cột trắng mà không ai biết.

🔴 **Nếu chưa lấy được purpose consent một cách chắc chắn thì ĐỪNG GỬI event này.** Thà cột trống
còn hơn một cột nói ngược. Trống thì ai cần sẽ đi hỏi; nói ngược thì không ai hỏi cả.

### Hai ca KHÔNG được gửi

```csharp
if (consent is ConsentStatus.Required or ConsentStatus.Obtained) { /* mới xét tới chuyện gửi */ }
```

⚠ **`NotRequired`** — user ngoài EU **không hề từ chối**, họ không được hỏi. Map
`NotRequired → denied` là nhuộm toàn bộ người ngoài EU thành "denied" và giết luôn ý nghĩa của
con số.

⚠ **`Unknown`** (chưa update xong / lỗi) — chưa biết thì chưa gửi. Lưu ý code hiện có **ba đường
thoát sớm** gọi `OnShowPopupATT()` mà *không* chạy tới chỗ đọc consent: timeout 5 giây, `err != null`,
và `formError != null`. Ba đường đó đúng là phải không gửi gì.

### Và một chỗ gọi nữa, nếu có

Nếu module có cửa **"privacy options" / mở lại form consent** cho user đổi ý, xin gọi
`ReportAdsConsent` ở đó nữa — đó đúng là chỗ giá trị **đổi**, mà event này sinh ra để ghi lại việc đổi.

---

# §B — module `Notification`: quyền nhận push

## B1. Sau khi xin quyền

Trong `NotificationManager`, hai coroutine của `RequestPermission()`:

**Android** — `RequestNotificationPermission()`:

```csharp
var request = new PermissionRequest();
while (request.Status == PermissionStatus.RequestPending)
    yield return null;
_checkNotificationPermissionDone = true;
ReportPush(request.Status);          // THÊM
ShowCacheNoti();
```

**iOS** — `RequestAuthorization()`, trong `using (var req = ...)` sau vòng chờ:

```csharp
_checkNotificationPermissionDone = true;
FalconBigDataController.App.ReportPushPermission(req.Granted);   // THÊM
ShowCacheNoti();
```

⚠ `req.Granted` ở mốc này thì đủ dùng, nhưng lúc **đọc lại ở B2** thì `AuthorizationStatus` có **5
giá trị**, không phải 2 — xin map theo bảng ở B2, đừng tự `!= Denied`.

Hàm map cho Android:

```csharp
private static void ReportPush(PermissionStatus status)
{
    switch (status)
    {
        case PermissionStatus.Allowed:
            FalconBigDataController.App.ReportPushPermission(true);
            break;
        case PermissionStatus.Denied:
        case PermissionStatus.DeniedDontAskAgain:
        case PermissionStatus.NotificationsBlockedForApp:
            FalconBigDataController.App.ReportPushPermission(false);
            break;
        // NotRequested / RequestPending: CHƯA BIẾT -> không gửi
    }
}
```

⚠ `NotificationsBlockedForApp` **là denied** — app bị tắt thông báo ở Settings. Về mặt *"còn nghe
được mình không"* thì nó y hệt denied, mà đó đúng là câu event này trả lời.

⚠ `NotRequested` / `RequestPending` **không phải denied** — chưa hỏi hoặc đang chờ. Không gửi.

## B2. 🔴 Và một lần lúc khởi động — đây mới là phần quan trọng

`RequestPermission()` chỉ chạy trong luồng init, tức **chỉ nắm được trạng thái ở lần xin quyền**.
User **tắt thông báo trong Settings** vài tuần sau thì không có đường nào biết — mà đó chính là
nhóm làm mẫu số trôi dần theo thời gian.

Xin đọc trạng thái **hiện tại** mỗi lần khởi động (báo trùng không đẻ log nên gọi mỗi lần là miễn phí):

```csharp
#if UNITY_ANDROID
ReportPush(AndroidNotificationCenter.UserPermissionToPost);
#elif UNITY_IOS
var status = iOSNotificationCenter.GetNotificationSettings().AuthorizationStatus;
#endif
```

✅ **Bên mình đã đọc code của package để chắc chắn việc này chạy được**: `UserPermissionToPost`
không chỉ đọc giá trị đã lưu — nó gọi **`AreNotificationsEnabled()` qua JNI**, tức hỏi hệ thống
ngay lúc đó. Nên nó **thật sự bắt được** ca user tắt thông báo trong Settings. (Có một bước đọc
`PlayerPrefs` kèm theo, nhưng JNI mới là thứ quyết định.)

⚠ **Hai bẫy của `UserPermissionToPost`:**

1. 🔴 **Nó trả `PermissionStatus.Denied` nếu notification center CHƯA init** (`if (!Initialize())
   return PermissionStatus.Denied;`). Gọi quá sớm là **bịa ra một dòng denied**. Xin gọi sau khi
   `AndroidNotificationCenter` đã init — tức sau `InitChannel()` trong luồng hiện có là được.
2. ⚠ **Dưới API 33 nó trả `Allowed` vô điều kiện** — xem B3.

**Bảng map cho iOS** (`AuthorizationStatus` có 5 giá trị):

| Giá trị | Gửi gì | Vì sao |
|---|---|---|
| `Authorized` | `true` | — |
| `Provisional` | `true` | Thông báo **vẫn tới**, chỉ là im lặng (vào Notification Center, không banner). Về câu *"còn nghe được mình không"* thì đây là CÓ |
| `Denied` | `false` | — |
| `NotDetermined` | **không gửi** | chưa hỏi |
| `Ephemeral` | **không gửi** | chỉ có ở App Clip, không phải ngữ cảnh của game |

⚠ Nếu owner muốn tách `Provisional` khỏi `Authorized` thì nói với bên mình — hiện API chỉ nhận
`bool` nên phải mở thêm đường, và bên mình **không** tự mở trước khi có người cần.

(Các API trên có trong `com.unity.mobile.notifications` 2.4.1 mà project đang dùng.)

## B3. Một luật đọc xin ghi kèm khi ship

⚠ **Android dưới API 33 không có runtime permission** — `PermissionRequest.Status` thành `Allowed`
ngay lập tức. Nên trong dữ liệu, một phần `granted` của Android là **"được mặc định"** chứ không
phải "user bấm đồng ý". Việc này **không cần client làm gì** (`os_version` đã có trên envelope, kho
tự cắt được), nhưng xin nói rõ trong CHANGELOG để không ai đọc tỉ lệ opt-in Android mà tưởng user
chủ động bấm.

---

## Những gì KHÔNG cần làm (cả §A và §B)

- **Không** dùng `FPropertyLog` / `property_data`. Đó là log di sản thời hệ thống còn sơ khai, dùng
  sinh biểu đồ động (dấu vết còn nguyên ở field `priority` = *"thứ tự step"*); phía server nó thành
  event `f_sdk_property`. Mượn kênh đó là trộn ngữ nghĩa với dữ liệu biểu đồ đời cũ. Hợp đồng §D8
  đã bỏ hướng này từ 10/08/2026.
- **Không** tự cache / so sánh giá trị cũ để tránh gửi trùng — `PermissionLogService` làm rồi, và nó
  ghi mốc *sau* khi enqueue nên ca xấu nhất là thừa một log trùng, không bao giờ mất vĩnh viễn.
- **Không** gộp ba quyền vào một event mang key. Hợp đồng §D8 tách ba event có lý do: vocab **đóng
  toàn-fleet** mà analyst lọc trực tiếp thì nằm trong **tên event**. Tách rồi muốn gộp thì miễn phí
  (`event LIKE 'f_sdk_permission_%'`), gộp rồi muốn tách là cả một chiến dịch convert.
- **Không** đổi luồng UMP, thứ tự prompt, hay thời điểm xin quyền push. Mọi lời gọi ở đây chỉ **đọc**
  trạng thái đã có.

## Nghiệm thu

Không có bước bật công tắc nào. Event lên prod → DQ của loader tự đếm → coverage per game tự cập nhật.

Kiểm nhanh ở editor: `AnalyticLogger` in ra `f_sdk_permission_att:{...}` /
`f_sdk_permission_ads_consent:{...}` / `f_sdk_permission_push:{...}` **đúng một lần cho mỗi lần đổi
giá trị** — gọi lại mà không đổi thì không in gì, đó là van chống trùng đang chạy đúng.

Xin bump version module + một dòng CHANGELOG: kho cần mốc *"từ bản X mới có dữ liệu"* để không đọc
khoảng trước đó thành "0% đồng ý".
