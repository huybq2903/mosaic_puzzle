# "ad_close mồ côi" — xác nhận lỗi phía SDK, đã sửa

> Trả lời báo cáo của data team (coin.sort, 13/10). Viết 16/10/2026.
> **Kết luận: đúng là lỗi của SDK, không phải dữ liệu thừa.** Các bạn suy luận đúng nguyên nhân —
> phần dưới bổ sung **đường code cụ thể**, **hai hệ quả nữa chưa nêu trong báo cáo**, và **cách cắt
> era**.

## 1. Đúng như các bạn đo

| Điều các bạn kết luận | Bên mình xác nhận |
|---|---|
| Không có impression thừa (lệch AppLovin S2S −0,6% / −1,6%) | ✅ Đúng — SDK còn ít hơn nhà mạng, mức hao hụt bình thường |
| Close mang id của quảng cáo tải sẵn kế tiếp | ✅ Đúng, và đây là lỗi đóng dấu id, không phải lỗi đếm |
| Khoảng cách p50 = 52s (reward) / 33s (inter) ≈ thời gian xem | ✅ Khớp với cơ chế bên dưới |
| Reward 46% > inter 14% | ✅ Vì thời gian xem dài hơn → preload kịp hoàn tất **trước** khi user đóng |

## 2. Đường code

SDK giữ **MỘT slot view cho mỗi format** (`AdViewState._viewByType`), vì mediation chỉ có một ad
object và một lần load in-flight cho mỗi format. Trình tự thật:

```
OnRequested  → sinh view A, đặt vào slot
OnShown      → đọc slot  → A
Impression   → đọc slot  → A
OnRequested  → mediation PRELOAD ad kế tiếp → sinh view B, ĐÈ slot      ← chỗ hỏng
OnClosed     → đọc slot  → B        ✗ close mang id của ad chưa hiện
```

Thiết kế cũ giả định "request mới nghĩa là view cũ đã xong" — đúng với thời mediation load lại
*sau* khi đóng, sai từ khi có preload.

## 3. Hai hệ quả nữa, chưa có trong báo cáo

1. **`has_click` của dòng close mồ côi không tin được** → **CTR tính từ dòng close bị lệch**. Cú
   click được ghi lên view đang chiếu, còn dòng close lại đọc view khác. SDK không bắn event click
   riêng, nên đây là đường duy nhất ra CTR của reward/inter.
2. **`ad_view_id` trên `f_sdk_resource_data` cũng dính**. Game đọc `Ad.CurrentViewId(type)` lúc
   **phát thưởng rewarded** — tức ngay sau close, sau khi đã preload — nên quà/doanh thu rewarded bị
   nối vào một lần xem **chưa từng xảy ra**. Nhờ bạn đo thêm: *tỉ lệ dòng resource có `ad_view_id`
   trỏ tới view không có impression*. Mình đoán nó xấp xỉ 46% của nhánh reward.

## 4. Một dấu hiệu nhận dạng sạch (không cần heuristic)

Dòng close mồ côi có **`shown_duration_sec` VẮNG MẶT**: view preload chưa qua mốc show nên SDK
không bịa số 0. Nên nhóm đó là *thiếu thời lượng*, không phải *sai thời lượng* — và nhận ra được
bằng chính cột của nó, không phải đoán theo thời gian.

## 5. Đã sửa

Tách sổ **"view ĐANG CHIẾU"** (đã show, chưa close) ra khỏi slot:

| Mốc | Đọc view nào |
|---|---|
| `OnRequested` | ghi vào **slot** (như cũ) |
| `OnShown` | lấy slot, rồi **ghi sổ đang-chiếu** |
| Impression · click · close · `CurrentViewId` | **view đang chiếu**, không có thì mới lấy slot |
| Banner | **không đổi** — id banner là id INSTANCE theo hợp đồng, và banner không có close |

Ca thiếu close (app bị kill, mediation quên bắn) **tự lành** ở lần chiếu sau: show mới đè sổ. +7
test ghim, trong đó có đúng kịch bản các bạn mô tả.

## 6. Việc phía các bạn

1. **Đồng ý không vá bằng heuristic** — bên mình cũng không muốn một luật đoán-theo-thời-gian nằm
   lại trong pipeline sau khi SDK đã đúng.
2. **Cắt era bằng `sdk_version`** (cột này có từ 1.5.0, đi theo mọi dòng): `ad_view_id` của dòng
   close chỉ tin được từ **1.5.1** trở đi. Dữ liệu cũ giữ nguyên, đừng sửa ngược.
3. **Đo lại sau khi bản mới phủ** — kỳ vọng: tỉ lệ close mồ côi về gần 0, `closed_at` phủ ~100% view
   thay vì 55%.
4. Nếu đo được mục 3.2 (resource trỏ view không impression) thì báo lại — nó quyết định có phải
   backfill phần nối thưởng rewarded hay không.
