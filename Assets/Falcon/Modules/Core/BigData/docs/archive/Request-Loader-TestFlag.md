# Hai cột `is_test` / `test_source` — lọc log của người làm game

> Gửi loader + visualizer. Viết 16/10/2026. **Additive, không đổi gì đang chạy.**
> Phiên bản: bản sau BigData 1.5.1.

## 1. Vấn đề

Log sinh ra trong **editor play** và **development build** hiện **không phân biệt được** với log của
người chơi thật. Lần gần nhất chuyện này tốn thời gian của cả hai bên: mấy số `app_open` /
`retention` / `activeDays` của một game trông bất thường, và bên mình phải đi **giải mã `Editor.log`**
mới kết luận được đó là artifact của editor. Có cột này thì lần đó chỉ là một câu
`WHERE is_test IS NULL`.

## 2. Hai cột (chỉ xuất hiện khi là log test)

| Cột | Kiểu | Khi nào có |
|---|---|---|
| `is_test` | bool | **Chỉ khi `true`** — vắng mặt = dữ liệu thật |
| `test_source` | string | Đi kèm: `editor` · `dev_build` |

Dòng production **không mang cả hai**, nên không tốn thêm byte nào trên stream thật.

## 3. Nguồn — chỉ hai, và không ai ghi đè được

Chốt của owner: **bản production tuyệt đối không được mang cờ này**, vì một lần dev quên tắt là hỏng
toàn bộ dữ liệu của bản phát hành mà không có dấu hiệu gì. Nên:

- Giá trị tính tại chỗ từ **loại build** (`Application.isEditor` / `Debug.isDebugBuild`).
- **Không** có API set, **không** đọc remote config, **không** đọc kho local.
- Tên cột nằm trong sổ "key trung tâm" của SDK → code game set common param trùng tên **bị từ chối**.
- **Không persist**: cờ là thuộc tính của BUILD, không của máy — cài bản dev rồi cài bản store lên
  cùng thiết bị thì dòng production không mang cờ.

## 4. Việc phía các bạn

1. **Nhận hai cột** (additive trên mọi event, như `sdk_version`).
2. **Mọi dashboard mặc định lọc `is_test IS NULL`** — luật quan trọng nhất ở đây. Chart mới không
   được âm thầm trộn log của người làm game vào số thật.
3. **GIỮ các dòng đó, đừng drop.** Chúng là thứ để nghiệm thu tích hợp — QA bắn log xong soi được
   ngay trên kho. Nếu tiện, một dashboard QA riêng lọc `is_test = true` sẽ rất hữu ích cho bên mình.
4. Ghi vào catalog ba điều ở §3 (để sau không ai hỏi "sao bản QA không có cờ") và giới hạn ở §5.

## 5. Giới hạn đã biết, chấp nhận

**Build QA ở chế độ release trông như production.** Đó là cái giá trực tiếp của luật ở §3. Hai cách
tách nhóm đó nếu cần:

- **Đổi bundle id** cho build QA (`package_name` đã có sẵn trên envelope — các bạn lọc được ngay,
  bên mình không phải làm gì).
- Cho build đó **khỏi gửi** bằng công tắc config của SDK.

Cho bên mình biết **build QA hiện có đổi bundle id không** — nếu có thì hai cột này chỉ còn phải phủ
editor và development build, đúng như đang làm.

---
*File xoá sau khi các bạn ack, bản lưu ở git history — như lệ.*
