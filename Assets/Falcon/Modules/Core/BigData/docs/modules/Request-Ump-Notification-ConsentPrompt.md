# Ump + Notification — báo popup xin quyền cho BigData (đợt 3 B2 / B14)

> Gửi §A owner `Core/ThirdParty/Ump` (`FalconUMP` 1.2.14) · §B owner `Modules/Notification` (`NotificationManager` 1.0.13).
> Người gửi: owner `Core/BigData`. Căn cứ: hợp đồng BigData đợt 3 B2 + B14(b) (ba bên ký 23/11). Đi kèm (không thay)
> `Request-Permission-AttConsentPush.md` — ticket đó lo **trạng thái** quyền; ticket này lo **lần hiện popup**.

## Vì sao

- Log quyền chỉ bắn khi trạng thái ĐỔI. Popup hiện mà người chơi giữ nguyên, hoặc game chưa từng hỏi, đều không có dòng — kho không
  tách được "chưa được hỏi" với "đã từ chối", và không tính được tỉ lệ đồng ý popup.
- BigData **1.8.0** đã có API: `FalconBigDataController.Consent.OnPrompt(PromptPermission, PromptKind, PromptTrigger?)` và trạm tải
  `Ftue.OnLoadStep(LoadKind.App, LoadStep.ConsentShown)`.

## §A. Ump (`FalconUMP.cs`)

- [ ] **A1. Form CMP.** Trước `ConsentForm.LoadAndShowConsentFormIfRequired` (dòng ~87), đọc `ConsentInformation.ConsentStatus`. Nếu
  là `Required` thì form sẽ hiện — gọi:

  ```csharp
  FalconBigDataController.Consent.OnPrompt(PromptPermission.AdsConsent, PromptKind.System, PromptTrigger.FirstOpen);
  FalconBigDataController.Ftue.OnLoadStep(LoadKind.App, LoadStep.ConsentShown);
  ```

  ⚠ Cách suy "Required ⇒ form hiện" **chưa chạy thử**; kiểm trên máy test ở EU (hoặc bật debug geography của UMP).
- [ ] **A2. ATT.** Ngay trước hai chỗ `ATTrackingStatusBinding.RequestAuthorizationTracking()` (dòng ~134, ~159) — chỉ gọi khi trạng thái
  còn `NOT_DETERMINED`, nên popup chắc chắn hiện:

  ```csharp
  FalconBigDataController.Consent.OnPrompt(PromptPermission.Att, PromptKind.System, PromptTrigger.FirstOpen);
  ```

  (Có phần native ở đợt 2, SDK sẽ tự thấy lần đổi trạng thái ATT; khi đó BigData tự bỏ dòng trùng.)

## §B. Notification (`NotificationManager.cs`)

> Đính chính bản trả lời SDK 23/11 ("framework không có chỗ nào xin quyền thông báo"): **module này có** — `RequestPermission()` gọi
> `RequestNotificationPermission()` (Android, `PermissionRequest`) / `RequestAuthorization()` (iOS, `AuthorizationRequest`).

- [ ] **B1. Android 13+.** Thay `new PermissionRequest()` bằng hàm bọc của BigData — SDK tự ghi dòng `system` kèm kết quả từng lần
  (kể cả lần từ chối mà trạng thái không đổi):

  ```csharp
  FalconBigDataController.Consent.RequestPush(PromptTrigger.FirstOpen, granted => { /* như cũ */ });
  ```

  Không đổi được ngay thì tối thiểu báo popup trước lúc xin:
  `Consent.OnPrompt(PromptPermission.Push, PromptKind.System, PromptTrigger.FirstOpen)`.
- [ ] **B2. iOS.** Trước `new AuthorizationRequest(...)`, nếu `iOSNotificationCenter.GetNotificationSettings().AuthorizationStatus` còn
  `NotDetermined` thì gọi `Consent.OnPrompt(PromptPermission.Push, PromptKind.System, PromptTrigger.FirstOpen)`.
- [ ] **B3.** Trigger: module xin lúc khởi động ⇒ `FirstOpen`. Game gọi lại ở chỗ khác thì truyền trigger tương ứng (thêm tham số).

## Kiểm

- Máy cài mới: có đúng một dòng `f_sdk_permission_prompt` (`promptKind = system`) cho mỗi popup OS hiện ra, đi trước dòng
  `f_sdk_permission_*` đổi trạng thái.
- Máy đã quyết trước đó: không có dòng prompt.
