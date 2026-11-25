# CHANGE LOG
# 1.2.17
- báo lần HIỆN popup xin quyền cho BigData (`Request-Ump-Notification-ConsentPrompt.md` §A, hợp đồng đợt 3 B2 / B14b), cần BigData 1.8.0:
  - form CMP: `ConsentStatus == Required` ngay trước `LoadAndShowConsentFormIfRequired` -> `Consent.OnPrompt(AdsConsent, System, FirstOpen)` + `Ftue.OnLoadStep(App, ConsentShown)`. ⚠ suy "Required ⇒ form hiện" chưa chạy thử trên máy EU
  - ATT: ngay trước cả hai chỗ `RequestAuthorizationTracking()` (chỉ khi còn `NOT_DETERMINED`) -> `Consent.OnPrompt(Att, System, FirstOpen)`
  - BigData chưa sẵn sàng thì chờ (trần 60s) như log quyền cũ — khi đó giờ của trạm `consent_shown` là lúc BigData sẵn sàng, không phải lúc form hiện
- pin `falcon.modules.core.bigdata` 1.8.0

# 1.2.16
- gửi `f_sdk_permission_att` (iOS: đầu `OnShowPopupATT` và sau `FALCON_UMP_COMPLETE`) và `f_sdk_permission_ads_consent` (khi `Required`/`Obtained`, purpose 1 + 4 của `IABTCF_PurposeConsents`; đọc không được thì không gửi) — từ bản này mới có dữ liệu
- asmdef thêm BigData

# 1.2.15
- rào Debug.Log / LogWarning / LogError bằng #if ENABLE_DEBUG_LOG

# 1.2.14
- sửa lại luồng, bỏ sử dụng main thread

# 1.2.13
- fix minor bug

# 1.2.12
- làm lại luồng att cho ios

# 1.2.11
- fix scripting define

# 1.2.10
- fix lỗi build ios, thêm ump config

# 1.2.9
- fix lỗi editor ios

# 1.2.8
- fix lỗi dontdestroy SingletonThirdParty

# 1.2.7
- thêm biến check null cho instance của Auto Singleton

# 1.2.6
- fix lỗi khi ump time out

# 1.2.5
- thêm scripting define hỗ trợ window (build map editor)

# 1.2.4
- fix lỗi ko update consent đc ở 1 số máy.

# 1.2.3
- đợi cuối frame mới bắn sự kiện init thành công