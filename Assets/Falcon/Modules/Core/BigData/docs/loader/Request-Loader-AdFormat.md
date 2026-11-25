# `ad_format`: hai HỌ format, hai cột mới, và một phép kiểm nhờ các bạn chạy

> Gửi loader + visualizer. Viết 19/10/2026. **Additive**: hai cột mới, không cột nào đổi nghĩa.
> Phiên bản: bản sau BigData 1.5.2 (số hiệu gửi kèm lúc publish).
> Rà cùng đội Mediation 19/10 — những gì ghi "chưa có dữ liệu" là họ xác nhận.

## 1. Luật đọc: `type` có HAI HỌ, không phải một thang

Bảy giá trị của cột `type` trên `f_sdk_ads_data` chia làm hai họ, và **vòng đời log của hai họ khác
nhau hẳn**. Đây là thứ cần biết trước khi dựng bất cứ chart per-view nào.

| Họ | Giá trị | `ad_view_id` nghĩa gì | Có mốc `close`? |
|---|---|---|---|
| **Dính màn** (nằm trong UI game) | `Banner`, `MREC`, `CollapsibleBanner`, `Native` | id của **INSTANCE** — một instance đẻ N impression chung một id | **KHÔNG**. Chúng biến mất cùng cái UI chứa chúng, không mediation nào bắn callback đóng |
| **Chiếm cả màn** | `Interstitial`, `Reward`, `AppOpen` | **một id một lần chiếu** | Có |

Hệ quả cho phía đọc số:

- **Đừng đếm "số lần xem" bằng `count(DISTINCT ad_view_id)` cho họ dính màn** — với họ, một id là một
  instance, không phải một lần xem. Số lần hiện là `sum(impression_count)`.
- **Đừng chờ dòng `close` cho họ dính màn.** Không có nghĩa là mất dữ liệu; nó không tồn tại.
- Phễu `request → impression` chỉ có nghĩa với họ chiếm cả màn (và chỉ với game **một ad unit mỗi
  format** — luật này các bạn đã khai từ audit 12/08, vẫn còn hiệu lực).

## 2. Hiện trạng dữ liệu — đừng dựng cột cho thứ chưa tồn tại

| Giá trị | Có dữ liệu thật? |
|---|---|
| `Banner`, `Interstitial`, `Reward`, `AppOpen` | ✅ đang chạy |
| `MREC` | ❌ **Chưa game nào bật.** Code của Max có đường phát giá trị này, nhưng đội Mediation xác nhận chưa ai dùng |
| `CollapsibleBanner` | Có đường phát ở GMA. Đội Mediation xác nhận nó **KHÔNG tự refresh** (một creative cố định) → một instance ≈ một impression |
| `Native` | ❌ **Chưa provider nào phát** |

Nói trước vì nếu các bạn dựng dashboard theo đủ 7 giá trị thì ba ô sẽ rỗng, và rỗng-vì-chưa-bật khác
rỗng-vì-lỗi.

## 3. Hai CỘT MỚI cần các bạn ký

| Cột | Kiểu | Nghĩa |
|---|---|---|
| `prev_full_screen_ad_format` | enum string | Quảng cáo **chiếm cả màn** vừa **ĐÓNG** trước dòng này (`Interstitial` / `Reward` / `AppOpen`) |
| `prev_full_screen_ad_sec` | int | Số giây kể từ lúc quảng cáo đó **đóng** |

**Câu cần trả lời:** *"native (hay MREC/collapsible) hiện ngay sau loại quảng cáo nào thì diễn tốt
hơn"* — đây là yêu cầu của đội game, và nó là **break-by**, nên bên mình xin **cột thật** chứ không
đẩy vào blob.

**Bốn luật đọc, xin ghi vào nhãn chart:**

1. **Phải đọc KÈM `prev_full_screen_ad_sec`.** Một mình cái format thì một native hiện **40 phút
   sau** vẫn trông như "liền sau reward". Client **cố ý không cắt ngưỡng**: *"trong vòng mấy giây mới
   tính là liền sau"* là câu hỏi phân tích, các bạn chọn (gợi ý khởi điểm: ≤ 10s).
2. **Chỉ có trên dòng của họ dính màn đi per-impression** (`Native`, `MREC`, `CollapsibleBanner`).
   Dòng **gộp cụm** của banner không mang — `sec` là số liên tục nên không cộng được cũng không vào
   khoá cụm được. Mà banner là **đồ đạc cố định**, câu "hiện sau ad nào" không áp dụng cho nó.
3. **Vắng mặt = phiên này chưa có quảng cáo chiếm-cả-màn nào ĐÓNG.** Sổ không sống qua phiên (nghĩa
   của field là *"trong phiên này"*). Đừng COALESCE về một giá trị nào.
4. Đóng dấu ở mốc **ĐÓNG**, không phải mốc show. Ca mediation quên bắn close thì field **vắng** —
   bên mình chọn vắng thay vì đoán theo mốc show.

## 4. Dòng `Native` sẽ KHÔNG mang `ad_view_id` và `fill_latency_ms`

Không phải lỗi, là quyết định có chủ đích — và nó khác với ba format còn lại trong cùng họ.

Native được thiết kế để có **nhiều ô trên một màn** (popup kết quả, danh sách shop). Mà mọi mốc trong
API của SDK chỉ mang `type`, không mang gì phân biệt được ô nào. Nếu SDK vẫn dán id của ô mới nhất
thì hai quảng cáo khác nhau mang **cùng một id**, và `fill_latency_ms` của ô cũ bị tính theo lần xin
của ô mới. Đó đúng là loại phỏng đoán các bạn đã **từ chối** ở án "ad_close mồ côi" (không vá bằng
ghép theo thời gian) — nên bên mình chọn **vắng mặt = không biết**.

Hệ quả, xin ghi vào luật đọc:

- **Không tính fill-rate / phễu request→impression cho `Native`.** Dòng request của native cũng không
  mang id (mang mà không bao giờ có impression khớp thì **mọi native thành fill-fail giả**).
- **CTR của native không có trong dữ liệu của bên mình.** Native không có mốc `close`, mà cờ click của
  SDK đi theo dòng close. CTR native phải đọc ở dashboard mediation. Nếu các bạn cần CTR native **trong
  kho**, nói để bên mình mở một đường riêng — đó là việc phải thiết kế, không phải field thêm vào.
- `MREC` và `CollapsibleBanner` **vẫn có id** bình thường: với chúng, giả định "một cái một màn" còn
  đúng. Ngày nào có game đặt hai ô cùng format trên một màn, bên mình sẽ bỏ id của format đó và **báo
  các bạn trước**.

## 5. "Doanh thu quảng cáo trong lúc event X chạy" — xin suy bằng cửa sổ thời gian

Bên mình **không** gửi 4 param `live_ops_*` trên dòng ad, và đây là quyết định chứ không phải thiếu:
gắn event cho một cái banner đáy màn chỉ có thể là **ambient** (*"lúc đó event đang chạy"*), mà luật
nhà bên mình cấm đóng dấu ambient — đóng dấu là **lời khai nhân quả của game**, không phải trùng thời
điểm.

Câu đó suy được từ dữ liệu đã có:

```sql
-- doanh thu ad phát sinh trong cửa sổ của một đợt event
SELECT sum(ad_rev)
FROM f_sdk_ads_data a
JOIN (SELECT DISTINCT live_ops_event, live_ops_cycle_id, cycle_start_ts, cycle_end_ts
      FROM f_sdk_live_ops_data) c
  ON a.account_id = c.account_id            -- nếu cần cắt theo user tham gia
WHERE a.client_sent_time BETWEEN c.cycle_start_ts AND c.cycle_end_ts
```

Còn ca **nhân quả thật** — *"xem rewarded để lấy thêm lượt TRONG event"* — thì khi có game làm, bên
mình sẽ đóng dấu 4 param đó trên chính dòng rewarded và báo các bạn. Hiện **chưa game nào** làm.

## 6. Một phép kiểm nhờ các bạn chạy

Mediation map chuỗi format của network sang `type` bằng một `switch` **có nhánh default**, và nhánh đó
**đoán**: IronSource gán mọi chuỗi ngoài `banner` / `interstitial` / `rewarded_video` thành
**`AppOpen`**. Nghĩa là rác không rơi vào rổ "không biết" mà rơi vào một format **thật, có doanh thu
thật**.

Nhờ các bạn đo xem nó có đang xảy ra không:

```sql
SELECT type, ad_format_raw, count(*), sum(ad_rev)
FROM f_sdk_ads_data
WHERE ad_format_raw IS NOT NULL
GROUP BY type, ad_format_raw
ORDER BY count(*) DESC
```

Dòng nào `type = 'AppOpen'` mà `ad_format_raw` nói chuyện khác → đúng ca này.

⚠ **Phép kiểm có điểm mù**: `ad_format_raw` hiện **chỉ được điền trên đường Max và Admob**. Đường
IronSource **không điền** — tức đúng chỗ dễ sai nhất lại không đo được. Bên mình đã xin đội Mediation
sửa cả hai (nhánh default thôi đoán + mọi provider điền `ad_format_raw`), nhưng owner chốt **hoãn** vì
chưa thấy vấn đề thật. Nếu phép kiểm trên cho thấy số đáng kể, nói để bên mình đẩy ưu tiên lên.

## 7. Báo trước: `ad_unit_id` sắp vào dòng ad

Bên mình vừa đưa `ad_unit_id` **vào khoá gộp cụm banner** (đã làm, chưa có hiệu lực), và đã xin đội
Mediation điền giá trị đó ở mốc impression. Hai điều khi nó bật:

- Dòng ad có thêm cột `ad_unit_id` → **đối chiếu doanh thu với bảng kê của AppLovin/AdMob** được, vì
  họ báo tiền theo ad unit. Đây là chiều các bạn từng hỏi.
- **Số dòng cụm banner có thể tăng** với game chạy nhiều ad unit cùng format (A/B giá sàn). Game một
  unit mỗi format thì **không tăng dòng nào**. Phần tăng chính là phần chẻ theo unit.

Làm theo thứ tự này (khoá trước, giá trị sau) là có lý do: nếu giá trị lên wire trước mà khoá cụm thêm
sau, thì dòng per-impression có mã còn dòng gộp không — chart "doanh thu theo ad unit" cho banner ra
**rỗng mà không lỗi, không warning**.

## 8. Việc phía các bạn

- [ ] **Ký 2 cột** `prev_full_screen_ad_format` + `prev_full_screen_ad_sec` (mục 3) — **trước khi bản
      mới publish**, cùng thứ tự đã làm với cụm quảng cáo v2.
- [ ] Ghi 4 luật đọc ở mục 3 và 3 luật ở mục 4 vào tài liệu luật đọc phía các bạn.
- [ ] Chạy phép kiểm mục 6, gửi lại con số.
- [ ] Xác nhận đã biết mục 2 (ba giá trị chưa có dữ liệu) và mục 7 (`ad_unit_id` sắp tới).

## 9. Câu cần các bạn trả lời

| # | Câu hỏi | Vì sao cần |
|---|---|---|
| F1 | Ngưỡng *"liền sau"* các bạn muốn dùng là bao nhiêu giây? | Chỉ để bên mình ghi vào doc cho thống nhất — client không cắt |
| F2 | Các bạn có cần **CTR native** trong kho không? | Có thì phải thiết kế một đường riêng (native không có mốc close) — bên mình cần biết sớm |
| F3 | Nếu sau này `Native` lên volume cao, các bạn muốn bên mình **gộp cụm** nó không? | Gộp thì **mất** `prev_full_screen_ad_sec`, mất thời điểm chính xác, mất `play_turn_id`. Bên mình nghiêng **không gộp**; nhưng nếu các bạn thấy volume là vấn đề thì phải chốt **trước khi** native lên dữ liệu, vì đổi sau là era |
