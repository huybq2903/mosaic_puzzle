# SDK BigData → loader: bản + ngày của BigData 1.7.1 – 1.7.4 và AnalyticParamsSync 1.1.9 (13/11/2026)

> Gửi: loader. Từ: owner SDK BigData.
> Thư này trả các mục **"bản + ngày"** mà bốn thư trả lời của các bạn đang chờ (`Reply-Loader-ReviveTypeBooster`,
> `Reply-Loader-AbsentNotZero`, `Reply-Loader-AdShowSkipped`, `Reply-Loader-UiImpression-LevelAction` / `Backlog-0911-d`).
> Kèm hai việc: **mốc era** cho bản sửa hot-sau-ad, và báo **nhãn người chơi bắt đầu có dòng thật**.

## Tóm tắt

| Bản | Publish | Có gì (thư đã ký) |
|---|---|---|
| BigData **1.7.3** (gộp 1.7.1 · 1.7.2 · 1.7.3) | **12/11/2026** | Xem mục 1 |
| BigData **1.7.4** | **13/11/2026** | `f_sdk_ad_show_skipped` · nhãn `no_ads` · **ERA** hot-sau-ad (mục 2) |
| AnalyticParamsSync **1.1.9** (pin BigData 1.7.4) | **13/11/2026** | Tự gửi nhãn `no_ads` qua `f_sdk_user_label` (mục 3) |

**Cách lấy mốc:** dùng cột `sdk_version` trên dòng, **không dùng ngày**. Ngày publish chỉ là lúc bản có trên remote; game nâng bản
muộn hơn nhiều. Lưu ý một ngoại lệ: build lấy thẳng mã nguồn repo (chưa qua remote) có thể lên dây trước ngày publish, với
`sdk_version` là số đã chốt gần nhất lúc build. Ví dụ: silver 11/11 đã có dòng booster / revive, trong khi 1.7.1 chưa publish.
Event mới thì nhận theo **tên**, không cần mốc; mốc chỉ cần cho thay đổi hành vi trên event cũ (1.7.2, 1.7.3, era 1.7.4).

---

## 1. BigData 1.7.3 — publish 12/11 (gồm cả 1.7.1 và 1.7.2)

1.7.1 và 1.7.2 **không** publish riêng. Lần publish 12/11 đưa lên thẳng 1.7.3, nên game lấy từ remote sẽ có cả ba nhóm thay đổi
dưới đây cùng lúc, với `sdk_version` = 1.7.3. `sdk_version` 1.7.1 / 1.7.2 chỉ có thể đến từ build lấy thẳng mã nguồn.

| Từ bản | Thay đổi | Thư |
|---|---|---|
| 1.7.1 | `f_sdk_ui_exposure` + `f_sdk_ui_flush_lost` thay `f_sdk_ui_impression` (đóng băng ở 1.7.0) | `Request-Loader-UiExposureRevive.md` |
| 1.7.1 | `f_sdk_level_action_revive` (+ `revive_type`) · `f_sdk_level_action_booster` · surface `booster_offer` | `Request-Loader-UiExposureRevive.md`, `Request-Loader-ReviveTypeBooster.md` |
| 1.7.1 | Họ `f_sdk_ftue_*` (5 event) | `Request-Loader-Ftue.md` |
| 1.7.2 | Dòng resource đi qua clone context (thưởng Sky Path / Race…) **thôi rơi** 4 field `liveOps*` + 3 field `feature*` | báo lỗi 11/11 |
| 1.7.3 | `startup_duration_ms` · `duration` lượt level · `value_*` hạng `unlimited`: số giả ⇒ **vắng** | `Request-Loader-AbsentNotZero.md` |

Với 1.7.2: dòng resource của thưởng event **trước** 1.7.2 thiếu ngữ cảnh đợt / lượt / tính năng là do lỗi SDK, không phải game không
gửi. Không có cách bù cho dòng cũ.

## 2. BigData 1.7.4 — publish 13/11

- **`f_sdk_ad_show_skipped`** đúng như đã ký 12/11 (4 field, bộ từ `skipReason` đóng).
- **`FUserLabelKey.NO_ADS = "no_ads"`**: `true` khi có quyền bỏ quảng cáo; `null` = gỡ nhãn.
- **ERA — `app_open` hot thôi đếm lần đóng quảng cáo toàn màn.** Từ `sdk_version` ≥ **1.7.4**: pause bắt đầu trong **10 giây**
  sau khi quảng cáo toàn màn (interstitial / rewarded / app open) báo `OnShown`, và quảng cáo đó chưa đóng ⇒ **không** có dòng hot
  khi quay lại. `openIndex` vẫn tăng.
  - ⚠ **Chỉ phủ các đường mediation có gọi `Ad.OnShown`**: MAX trực tiếp, MultiCall, FSN, AdMob bridge. **Chưa phủ GMA app open
    và IronSource** (hai đường này không báo `OnShown` cho BigData). ⇒ Đề nghị kho **giữ bộ lọc phía server** (bỏ hot trong 5 giây
    sau `ad_close` cùng phiên) **cả cho dữ liệu ≥ 1.7.4**, không chỉ cho dữ liệu cũ.
  - Cái giá đã chấp nhận: người chơi bấm Home trong 10 giây đầu của quảng cáo thì mất dòng hot đó.
  - Hệ quả trên cột kho: `resume_count` và `background_sec` của phiên giảm từ bản này. Đây là số sạch hơn, không phải người chơi đổi
    hành vi.

## 3. AnalyticParamsSync 1.1.9 — publish 13/11: nhãn người chơi bắt đầu có dòng thật

Thư 12/11 nhờ báo khi game đầu tiên gọi `Label.User`. Từ AnalyticParamsSync **1.1.9**, module tự gửi `no_ads` qua
`f_sdk_user_label`. Game không phải gọi gì.
- Mốc kiểm: đầu phiên sau khi đăng nhập, và mỗi lần app xuống nền.
- Chỉ gửi **khi cờ đổi**: có quyền ⇒ `true`; mất quyền sau khi đã gửi `true` ⇒ `null`; chưa từng có quyền ⇒ không gửi gì. Vì vậy
  số dòng nhỏ: mỗi người mua No Ads một dòng, cộng dòng gỡ khi hết hạn.
- ⚠ Không có dòng `no_ads` **không** có nghĩa là "không có quyền" cho tới khi game đã lên 1.1.9. User mua trước khi game nâng bản sẽ
  được gửi ở lần mở app đầu tiên sau khi nâng.

Dòng thật sẽ xuất hiện khi game đầu tiên ship bản có AnalyticParamsSync 1.1.9. Bên mình sẽ báo lại khi biết game nào.

## Câu hỏi thêm: từ `level_cap` cho `skipReason`?

Luật của MediationMO có một trần **"tối đa N lần trong một level"**. Bộ từ đã ký chưa có từ cho ý này (`session_cap` / `daily_cap` là
trần theo phiên / ngày; `level_interval` là khoảng cách giữa các level). Đội Mediation sẽ gửi ca này là `other` cho tới khi có từ.
Các bạn đồng ý thêm `level_cap` không? Đồng ý thì BigData thêm `AdSkipReason.LevelCap` ở bản kế tiếp.

## Còn chờ các bạn

1. Ghi era hot-sau-ad (`sdk_version` ≥ 1.7.4) vào catalog, và xác nhận giữ bộ lọc server cho GMA app open / IronSource.
2. Dựng đường `f_sdk_user_label` (mapping + profile) khi có dòng thật, như thư 12/11 đã hẹn.
3. Trả lời câu `level_cap` ở trên.
