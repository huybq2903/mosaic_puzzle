# Trả lời `Request-Loader-AdLoadAndEcpm.md` — event `f_sdk_ad_load` + khoá eCPM/bậc giá

> **Người chạy:** đội stools (làm thay loader) · **Ngày:** 2026-10-26
> **Cách làm:** đọc code pipeline + đo trên ByteHouse production ngày 25/10/2026.

## Tóm tắt

**Phía loader đã sẵn sàng cho cả L1–L5.** Một yêu cầu đổi trước khi phát hành: **mặc định phải là bản GỘP,
chỉ một số game bật TÁCH** (§2 — owner kho chốt 26/10). Ngoài ra có một chỗ phiếu tự mâu thuẫn (§3).

| # | Việc | Trạng thái |
|---|---|---|
| L1 | Event `f_sdk_ad_load` | ✅ Đã thêm luật mapping `ad_load` (lên production cùng lần nạp config tới) — ⚠ chỉ bật ở một số game, xem §2 |
| L2 | 4 khoá trên `f_sdk_ads_data` | ✅ Không cần làm gì — tự vào `event_extra_props` |
| L3 | `ecpm` trong `units[]` + `ad_algorithm` trên `f_sdk_ad_load_stats` | ✅ Như trên |
| L4 | `ad_unit_id` trên show/close | ✅ Như trên |
| L5 | `session_ad_request_index` | ✅ Như trên |

## 1. Đính chính nhỏ: kho KHÔNG bỏ event chưa đăng ký

Phiếu ghi *"chưa đăng ký → kho **bỏ** dòng này"*. Thực tế loader **không có danh sách trắng event**. Event lạ đi
qua nguyên vẹn: mọi khoá vào `event_extra_props`, chỉ có `sub_event`/`sub_event_2` để trống. Đăng ký (luật mapping)
là để nâng `ad_type`/`ad_mediation` lên cột `sub_event`/`sub_event_2` cho các bộ lọc và DQ dùng chung. Bài học
trước đây là 4,36 triệu dòng/ngày `ad_request_data` có `sub_event = UNKNOWN`, không phải mất dòng.

Luật đã thêm:

| | |
|---|---|
| `sub_event` | `ad_type` |
| `sub_event_2` | `ad_mediation` |
| `record_kind` | `point` (một lần gọi, không phủ khoảng) |
| Ở lại `event_extra_props` | `ad_view_id`, `success`, `time_loading_ms`, `tier`, `floor`, `estimated_ecpm`, `ecpm_floor`, `ad_unit_id`, `ad_network`, `error_mess`, `ad_algorithm` |

Khoá mới ở L2–L5 không cần luật: field lạ tự vào `event_extra_props` (`SchemaCheckStep`), giống `units[]` hôm nay.

## 2. Yêu cầu: mặc định GỘP, chỉ một số game TÁCH (owner kho chốt 26/10)

Số đo ngày 25/10/2026:

| | Dòng/ngày | User | Dòng/user |
|---|---|---|---|
| `f_sdk_ad_load_stats` (bản gộp, SDK v2) | 1,45 triệu | 57,4 nghìn | 25,2 |
| `f_sdk_ad_load_success` + `_fail` (bản cũ) | 2,89 triệu | ~58 nghìn | 19,9 + 85,7 |
| Game đang chạy SDK BigData dòng 1.x (có `ad_request_data`/`ad_load_*`) | | **126 nghìn** (15 game) | |
| Toàn bộ silver `f_sdk_event_general` | **630 triệu** | | |

Bản per-attempt cũ đo được ngay trên các game này: 14–175 dòng/user/ngày tuỳ game (`fish.feast` 122,
`coin.sort` 175, `zoodoku` 121, `bee.flow` 14). Với ~158 dòng/user như các bạn đo:

- **Các game SDK 1.x hôm nay:** 126 nghìn × 158 ≈ **20 triệu dòng/ngày (~3% silver)** — chịu được.
- **Nếu các game lớn lên SDK 1.5.5:** 15 game lớn nhất (triplemaster, jelly.mukbang, birdsort2…, ~3,6 triệu user có
  impression) hiện ở SDK `2.3.0`/không ghi version và chưa gửi event ad v2 nào. Lên 1.5.5 với tách mặc định thì
  ≈ 3,6 triệu × 158 ≈ **570 triệu dòng/ngày, gần gấp đôi silver** — riêng triplemaster ~200 triệu.

Mọi thứ ăn theo số dòng (nạp, lưu trữ, part, merge) đều tăng tương ứng. **Owner kho đã chốt:**

- **Mặc định trong SDK 1.5.5 = GỘP** (`f_sdk_ad_load_stats`, đã có `ecpm` trung bình + `ad_algorithm` từ L3).
  Tức là đảo giá trị mặc định của `fAdLoadLogSplit` thành `false`.
- **Chỉ một số game bật TÁCH** (`fAdLoadLogSplit = true` qua remote config của game đó) khi cần nối load ↔
  impression bằng `ad_view_id`. Danh sách game do bên phân tích/MO chọn; báo lại kho trước khi bật game mới.
- Hệ quả khi đọc: `f_sdk_ad_load` chỉ có ở các game đã bật, **không đại diện cho toàn fleet**. Số tổng
  toàn fleet (tỉ lệ thành công, eCPM trung bình theo unit/bậc/thuật toán) đọc từ `f_sdk_ad_load_stats`.

## 3. Đã kiểm trên dữ liệu thật (máy test `com.fc.sdk.block.escape`, SDK 1.5.5, 26/10 09:38–09:53Z)

**Đúng:**
- Luật mapping chạy: `f_sdk_ad_load` có `sub_event = ad_type`, `sub_event_2 = ad_mediation`.
- `session_ad_load_index` / `session_ad_load_success_index` có trên `f_sdk_ad_load`, đếm riêng theo format
  (Interstitial 1–5, Reward 1–27). `session_ad_request_index` và `session_ad_impression_index` cũng đếm riêng theo format.
- Vắng key đúng luật: `ad_network`/`estimated_ecpm` chỉ trên lần thành công, `error_mess` chỉ trên lần fail; `ad_view_id`
  vắng đúng ở các lần refill ngay sau khi chiếu.
- **Nối load ↔ impression bằng `ad_view_id` khớp tuyệt đối:** `estimated_ecpm` của lần load thắng = `ad_rev × 1000` của dòng
  impression cùng view (0,556187 · 0,581762 · 0,555839).
- `ad_unit_id` trên show/close (L4) có đủ.

**Cần sửa:**
1. **`ad_algorithm` vắng trên MỌI dòng `f_sdk_ad_load`** (L1 ghi là khoá định danh). Có trên request/show/close/impression.
2. **`tier = 0` được gửi** (unit `ce7962cbe12a7bce`, `2525aa953e6fe547`, `e0a73c09b585b48f` — cả dòng load lẫn impression).
   Những unit này không có `ecpm_floor`, tức là unit KHÔNG đặt sàn ⇒ theo hợp đồng phải **vắng** `tier`, không gửi 0
   (luật chốt 23/09, test `MissingTier_StaysNull_NotZero`).
3. **`ad_algorithm` vắng trên dòng impression Banner** (request Banner có `single`).

**Đã xác nhận (26/10):**
- Không có `f_sdk_ad_load_stats` từ máy test vì máy đang ở chế độ TÁCH; hai chế độ loại trừ nhau. Kiểm L3 = gửi
  `fAdLoadLogSplit = false` cho máy/game test. ⚠ Client nói mặc định của SDK **vẫn là TÁCH** — lệch với §2 (owner kho chốt
  mặc định GỘP); chờ hai bên chốt lại trước khi phát hành.
- `tier` lệch một nhịp **chỉ trên dòng FAIL** — lỗi phía Mediation (`MultiCallMaxProviderParent.OnRewardedAdLoadFailedEvent`
  hạ bậc trước rồi mới log), thành việc V6 của phiếu Mediation (chụp bậc lúc gọi LoadAd, kiểm cả 8 provider). Dòng success
  đúng. ⇒ Dữ liệu trước bản sửa V6: `tier` trên dòng fail = bậc của lần load SAU, đừng ghép với kết quả fail.

**Kiểm lại SDK 1.5.6 (26/10 10:32–10:40Z, cùng máy test): cả 3 lỗi đã sửa.**
- `ad_algorithm` có trên mọi dòng `f_sdk_ad_load`; dòng impression Banner có `ad_algorithm = single`.
- Unit không đặt sàn (`ce7962cbe12a7bce`, `2525aa953e6fe547`, `e0a73c09b585b48f`) **vắng** `tier` ở cả load lẫn impression.
- `tier` hết lệch nhịp: trên 48 dòng có cả hai khoá, `ecpm_floor = tier × eCPM mốc` khớp 100% (mốc = eCPM lần thắng gần nhất:
  0,46094 → 0,28535 → 0,2512) — `tier` là bậc ĐÃ DÙNG cho chính lần load đó. (1.5.5: dòng fail lệch, vd `tier` 1,7 mà sàn
  vẫn = 1,8 × mốc.)
- Nối load ↔ impression vẫn khớp tuyệt đối (0,460936 · 0,232381).
- Còn lại: L3 (`ecpm`/`ad_algorithm` trên `f_sdk_ad_load_stats`) — cần một lượt test với `fAdLoadLogSplit = false`.

## 4. Lưu ý khi viết câu tự kiểm

- `success` là bool ⇒ đọc bằng `JSONExtractBool`, **không** `jsonb_extract_string` (trả `''`, đếm ra 0, trông y
  như "chưa gửi").
- *"Thiếu thì vắng key"* chỉ phân biệt được bằng `JSONType(toString(event_extra_props), 'estimated_ecpm') = 'Null'`
  (đã thử 26/10; phải `toString` vì `JSONType` không nhận thẳng cột JSONB). `JSONExtractFloat`/`jsonb_extract_*`
  trả `0` khi vắng key. Câu `count(estimated_ecpm IS NULL)` trong phiếu sẽ
  luôn ra 0 nếu viết thẳng.
- `floor` trên `f_sdk_ads_data` (mới) là **TIỀN**, cùng nghĩa với `units[].floor`; khác `floor` **hệ số** trên
  `f_sdk_ad_load_success/_fail` cũ. Đừng union hai đời rồi group theo `floor`.
- Đồng ý không thêm eCPM vào dòng impression: `ad_rev × 1000` đã là eCPM thật của lượt đó.
