# Hai cột `win_streak` / `lose_streak` trên `f_sdk_level_data` NGỪNG GỬI

> Gửi loader / data team. Viết 14/10/2026. **Không có event mới, không có cột mới** — đây là thư
> báo *ngừng gửi* hai cột đang sống, kèm cách lấy lại con số nếu cần.

## 1. Chuyện gì đổi

Từ bản SDK **1.5.0**, mọi dòng `f_sdk_level_data` **không còn mang** `win_streak` và `lose_streak`.
Dữ liệu cũ **giữ nguyên**, là một era — đừng JOIN lẫn với dữ liệu sau mốc đổi bản.

Phía client bỏ hẳn: hai field trên wire, bộ đếm trong kho local, và 4 API
`Level.WinStreak` / `LoseStreak` / `ResetWinStreak` / `ResetLoseStreak`.

## 2. Vì sao bỏ chứ không sửa

1. **Suy lại được từ chính stream này.** Chuỗi Pass/Fail là một window function trên
   `f_sdk_level_data`. Không mất thông tin nào.
2. **Không phủ được ván bỏ ngang, và mọi cách phủ đều sai.** Yêu cầu ban đầu của bên mình là
   "chuỗi phải tính cả ván bỏ ngang". Nhưng "bỏ = thua" là **chính sách của từng game** chứ không
   phải bằng chứng; và ván bỏ do **app bị kill không phân biệt được** với user chủ động bỏ. Nhét
   một định nghĩa vào cột chung là cột đó chỉ còn đúng với một game.
3. **Không ai dùng.** Rà toàn bộ code client 14/10: không module nào đọc, không chỗ nào gọi reset;
   module feature streak của game tự giữ chuỗi riêng lấy từ server. Phía các bạn cũng xác nhận
   không chart nào dùng — nếu **có** thì nói ngay, bên mình hoãn.

## 3. Lấy lại con số nếu cần (chuỗi Pass/Fail thuần)

Định nghĩa y như cột cũ: chỉ `Pass`/`Fail` đụng chuỗi, `Skip`/`HeartBeat`/`Start` trong suốt.

```sql
-- chuỗi thắng/thua tại mỗi dòng terminal, theo user, theo thời gian
WITH t AS (
    SELECT user_id, created_at, status,
           -- mỗi lần ĐỔI kết cục là mở một đoạn mới
           countIf(status != prev_status) OVER (
               PARTITION BY user_id ORDER BY created_at
           ) AS streak_block
    FROM (
        SELECT user_id, created_at, status,
               lagInFrame(status) OVER (PARTITION BY user_id ORDER BY created_at) AS prev_status
        FROM f_sdk_level_data
        WHERE status IN ('Pass', 'Fail')
    )
)
SELECT user_id, created_at, status,
       row_number() OVER (PARTITION BY user_id, streak_block ORDER BY created_at) AS streak_len
FROM t
```

Hai điều khác với cột cũ, nói trước để khỏi thắc mắc khi đối chiếu hai era:

- **Mất log thì lệch.** Cột cũ do client đếm nên dòng nào mất trên đường gửi, chuỗi ở dòng sau vẫn
  đúng. Suy từ stream thì một dòng mất là chuỗi đoạn đó ngắn đi. Mức mất log hiện tại nhỏ, và loại
  phân tích dùng chuỗi (thua ≥3 ván → tỉ lệ rời game) chịu được nhiễu đó.
- **Ván bỏ ngang** giờ nhìn thấy trực tiếp: `Start` **không có** `Pass`/`Fail` cùng
  `play_turn_id`. Muốn "chuỗi tính cả drop" thì thêm nhóm đó vào chuỗi — và đây là chỗ các bạn
  **chọn được định nghĩa**, thay vì nhận một định nghĩa cứng từ client.

## 4. Việc phía các bạn

1. **Xác nhận không có chart / model / cột dẫn xuất nào đang đọc hai cột này.** Có thì bên mình
   hoãn phần wire lại.
2. Ghi mốc era vào catalog: hai cột ngừng có dữ liệu từ 1.5.0.
3. Nếu có người cần chuỗi, dựng view theo query ở mục 3 (bên mình đề nghị đặt tên khác
   `win_streak` để không ai nhầm với cột cũ).

---
*File xoá sau khi các bạn ack, bản lưu ở git history — như lệ.*
