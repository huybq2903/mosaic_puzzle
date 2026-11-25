# SDK BigData → loader: hai giá trị mới của `ecpm_floor_type` — `rule_error`, `rule_timeout` (23/11/2026)

> Gửi: loader (+ access đọc). Từ: owner SDK BigData. Căn cứ: phiếu `docs/modules/Request-Mediation-FirstInterFloorRule.md` W1 / Q2
> (chốt 23/11). **Không đổi code BigData** — SDK không kiểm danh sách vai; giá trị do module Mediation truyền.

## 1. Thêm gì

Cột `ecpm_floor_type` (vai của giá sàn, hiện 8 giá trị `base` · `base_half` · `upgrade_initial` · `upgrade_higher` ·
`upgrade_fallback` · `decay` · `decay_half` · `no_floor`) có thêm **2 giá trị**:

| Giá trị | Nghĩa |
|---|---|
| `rule_error` | Client gọi Rule chọn giá sàn **thất bại** (API lỗi, response hỏng / không hợp lệ, lỗi lúc gọi) ⇒ lần chiếu đó rơi về load mặc định (MultiCall / unit đơn) |
| `rule_timeout` | Gọi Rule **quá thời gian chờ** ⇒ cũng rơi về load mặc định |

- **Ở đâu:** mọi dòng `f_sdk_ad_load` của load mặc định trong opportunity đó (tới khi opportunity interstitial kế tiếp bắt đầu), và
  dòng impression của các ad đó (SDK chép vai từ dòng load của cùng ad unit, như 8 vai cũ).
- **Không gắn** cho nhóm đối chứng (Control / `base_line`, Rule trả flow rỗng) — đó không phải lỗi.

## 2. Vì sao

Trước đây ba ca dưới đây giống hệt nhau trên kho (`ecpm_floor_type` vắng). Sau khi Mediation làm xong phiếu:

| Ca | `ecpm_floor_type` | `realtime_variant` (W6) |
|---|---|---|
| Game không chạy flow chọn sàn | vắng | vắng |
| Nhóm đối chứng | vắng | **có** |
| Gọi Rule lỗi / quá giờ | **`rule_error` / `rule_timeout`** | vắng (Rule không trả lời) |

## 3. Mốc

- Bắt đầu có từ **bản Mediation làm W1** của phiếu (chưa publish). Không cắt theo `sdk_version` BigData được — SDK báo bản Mediation
  khi publish. Trước bản đó: ca gọi lỗi vẫn là `ecpm_floor_type` vắng.
- Ba cột vai / percentile / lần thử của **chính workflow** cũng mới bắt đầu lên kho BigData từ bản Mediation làm W0 — trước đó chúng chỉ
  sang Databuckets (rà 23/11). `ad_candidate_id` từ bản đó trùng mã của Databuckets.

## 4. Nhờ loader

1. Rule mapping / DQ nào đang coi `ecpm_floor_type` là **danh sách đóng 8 giá trị** thì thêm hai giá trị này.
2. Ghi vào luật đọc: `ecpm_floor_type IN ('rule_error','rule_timeout')` = dòng load mặc định do Rule lỗi — **không** thuộc tập dòng
   chấm personalization (không có sàn của Rule).
