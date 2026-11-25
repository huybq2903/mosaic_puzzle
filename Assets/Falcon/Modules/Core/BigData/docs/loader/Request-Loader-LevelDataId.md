# Thêm cột `level_data_id` trên `f_sdk_level_data`

> Gửi loader + visualizer. Viết 19/10/2026. **Additive, một cột string ~20 byte.**
> Phiên bản: bản sau BigData 1.5.1.

## 1. Cột mới

| Cột | Nghĩa |
|---|---|
| `level_data_id` | Danh tính của riêng **DATA** màn (không gồm param tuning), **do GAME SERVER đặt** — client chỉ echo nguyên văn |

Cột `current_level_id` đang có **vẫn giữ nguyên**: nó là băm của `data + param` và do **client** tính.

## 2. Vì sao cần cả hai

**(a) Nối được hai kho.** Hiện cùng một màn có **hai id khác nhau**: kho analytics nhận id do client
tính (`data + param`), còn game backend dùng id do chính nó đặt (`data`). Không ai join được hai hệ
theo thiết kế màn — ví dụ không kiểm được "số ván của thiết kế X bên analytics có khớp với bên game
server không". Có cột mới thì join trực tiếp sang **catalog level của backend**, nơi đã có đủ `data`
và `param`.

**(b) Tách "đổi layout" với "đổi tuning".** `current_level_id` đổi khi **một trong hai** đổi, mà
không nói cái nào. So hai cột là biết: cùng `level_data_id` + khác `current_level_id` = **chỉ đổi
tuning**; khác cả hai = **đổi layout**.

## 3. Vì sao KHÔNG gửi `levelData` / `levelParam` thô

Bên mình có cân nhắc rồi bác: `levelData` cỡ **1–3 KB**, nhân với **50–150 dòng level/user/ngày** là
**50–450 KB/user/ngày** của **cùng một chuỗi byte lặp lại** (mọi user chơi màn 50 gửi y hệt nhau).
Nội dung thuộc **catalog**, dòng log chỉ mang id. Nếu kho **không** lấy được catalog từ game backend
thì nói bên mình — lúc đó phương án là một event riêng bắn **một lần mỗi thiết kế mỗi thiết bị**,
không phải mỗi ván.

## 4. Việc phía các bạn

1. **Nhận cột `level_data_id`** (additive, vắng mặt = client chưa có id của server).
2. **Xác nhận kho lấy được catalog level từ game backend** (map `level_data_id` → `data`/`param`).
   Nếu không, bên mình phải làm phương án event-catalog ở §3.
3. **Chạy giúp một phép kiểm trùng id** trên cột đang có — 5 ký tự base64 = 30 bit, xác suất trùng
   nhỏ nhưng kiểm được:

```sql
SELECT current_level_id, count(DISTINCT current_level) AS n_levels
FROM   f_sdk_level_data
GROUP BY 1 HAVING n_levels > 1
```

Có dòng trả về nghĩa là **hai màn khác nhau đang chung một id**, và số của chúng đang trộn vào nhau
im lặng. Nếu có thì bên mình nâng độ dài băm.

---
*File xoá sau khi các bạn ack, bản lưu ở git history — như lệ.*
