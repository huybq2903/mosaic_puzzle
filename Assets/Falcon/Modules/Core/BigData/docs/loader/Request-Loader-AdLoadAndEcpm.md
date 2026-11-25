# Loader — đăng ký 1 event mới + 4 khoá mới cho stream quảng cáo

> Gửi loader 26/10/2026. Nguồn: SDK BigData **1.5.5**.
> Hai yêu cầu gốc, cùng một ngày: (1) nối được **load ↔ show** theo `ad_view_id`; (2) *"bản tin nào
> lấy được eCPM thì phải log eCPM"*.

## 0. Cần loader làm gì

| # | Việc | Ảnh hưởng |
|---|---|---|
| L1 | **Đăng ký event mới `f_sdk_ad_load`** | ✅ loader đã thêm luật mapping 26/10. ⚠ **Sửa lời phiếu**: kho KHÔNG bỏ event lạ — nó đi qua nguyên vẹn, mọi khoá vào `event_extra_props`, chỉ `sub_event`/`sub_event_2` để trống (vết sẹo thật là 4,36 triệu dòng/ngày `ad_request_data` mang `sub_event = UNKNOWN`, không phải mất dòng) |
| L2 | Thêm 4 khoá vào `f_sdk_ads_data` | Dòng impression hiện không có bậc/giá sàn/thuật toán/số thứ tự trong phiên |
| L3 | Thêm 2 khoá vào `f_sdk_ad_load_stats` | Bản gộp chưa có eCPM, và chưa tách được theo thuật toán |
| L4 | Thêm 1 khoá vào `f_sdk_ad_show_data` và `f_sdk_ad_close_data` | `ad_unit_id` cho hai mốc đó |
| L5 | Thêm `session_ad_request_index` vào `f_sdk_ad_request_data` | Mẫu số theo phiên cho phễu fill |

## L1. Event mới `f_sdk_ad_load` — telemetry load dạng TÁCH

Một dòng cho **mỗi lần gọi load của một ad unit**. Hình dạng đầy đủ: spec §5.11f.

| Key | Kiểu | Mô tả |
|---|---|---|
| `ad_view_id` | string | **Lý do tồn tại của event này** — id của ĐỢT xin ad, trùng với `ad_view_id` của dòng impression mà đợt ấy sinh ra. Có thể **vắng** (cố ý — xem ⚠ dưới) |
| `ad_type` · `ad_mediation` · `ad_algorithm` · `ad_unit_id` | string | Định danh |
| `success` | bool | Lần gọi này có ad về hay không |
| `time_loading_ms` | long | Thời gian của đúng lần gọi này |
| `tier` · `floor` · `estimated_ecpm` · `ecpm_floor` | double | Bậc config · giá sàn USD · eCPM ước tính · giá sàn eCPM. Thiếu thì **vắng key**, không gửi 0 |
| `ad_network` | string | Network trả ad về — chỉ khi `success = true` |
| `error_mess` | string | Lỗi của đúng lần này |
| `session_ad_load_index` | int | Lần gọi load thứ mấy **của format này trong PHIÊN**, kể cả lần fail (từ 1) |
| `session_ad_load_success_index` | int | Ad thứ mấy **vào kho** trong phiên (chỉ đếm lần thành công) — **vắng trên dòng fail**. eCPM đi theo trục này |

⚠ **Volume**: đo 22/09, bản per-attempt đẻ ~**158 dòng/user/ngày** so với ~**20** của bản gộp — tức
chế độ tách là **×8 trên mỗi user có SDK này**. Con số tuyệt đối thì phụ thuộc **game nào đang chạy
SDK**, nên nó là số đo của phía kho (nội bộ, không chép vào phiếu này).

> **Luật vận hành đã chốt 26/10:** mặc định **TÁCH** là để game nhỏ / đang tích hợp có dữ liệu ngay
> mà không phải cấu hình gì — **không** phải lời khẳng định "mọi game bật tách đều chịu được".
> ⚠ **TRƯỚC khi một game lớn nâng lên bản có chế độ tách, phải tắt tách cho game đó**, rồi chỉ bật
> lại khi thật cần nối `load ↔ show`. Việc bật/tắt là **cấu hình remote của từng game**, báo kho
> trước khi bật thêm game mới.

Van tắt vẫn còn nguyên: remote config `fAdLoadLogSplit = false` ở **một trong hai** service config
(luật OR) — về bản gộp ngay giữa phiên. ⚠ Van này là **của BigData** (`Core.RemoteConfig`), nên game
**không dùng CMS vẫn tắt được**; và ngược lại, **thiếu `AnalyticParamsSync` KHÔNG làm game tự chạy
gộp** — không cấu hình gì thì nó tách.

⚠ **`ad_view_id` vắng là cố ý**: SDK chỉ đóng dấu khi view của đợt **chưa tiêu thụ**. Lần load rơi
vào khoảng *"ad vừa chiếu xong — đợt sau chưa mở"* (callback về muộn, mediation tự refill, banner tự
refresh) thì không có id, vì dán id của ad vừa chiếu còn tệ hơn để trống. Lúc đó nối bằng
`ad_unit_id`.

## L2. `f_sdk_ads_data` — thêm 4 khoá

| Key | Kiểu | Mô tả |
|---|---|---|
| `tier` | double | Bậc giá sàn trong config của unit đang chiếu. ⚠ Vắng = **không đặt sàn**, khác `0` |
| `floor` | double | Giá sàn THẬT đã đặt của unit (USD/1000 impression) |
| `ad_algorithm` | string | Tên thuật toán load của đợt |
| `session_ad_impression_index` | int | Lần hiển thị thứ mấy **của format này trong PHIÊN** (từ 1). ⚠ Khác `type_count` (sổ theo ĐỜI). ⚠ **Vắng trên dòng gộp cụm banner** — ở đó đọc `impression_count` |

> **KHÔNG thêm eCPM vào dòng này** (chốt 26/10). Dòng impression đã có `ad_rev` — tiền THẬT của đúng
> lượt đó, kèm `ad_precision` — nên eCPM ở đây chỉ là `ad_rev × 1000`, chở hai lần một thứ. eCPM có
> giá trị riêng ở mốc **load** (§L1), nơi chưa có đồng doanh thu nào. So *"ước tính lúc load vs tiền
> thật lúc chiếu"* thì **join hai dòng bằng `ad_view_id`** — chính là thứ event mới ở L1 mở ra.
>
> `ad_unit_id` đã có trên dây từ trước; xác nhận lại vì giờ nó là **khoá** để SDK tra sổ sự thật theo
> unit — thiếu nó là dòng impression mất luôn `tier`/`floor`/`ad_network`.

## L3. `f_sdk_ad_load_stats` — thêm 2 khoá

| Key | Ở đâu | Mô tả |
|---|---|---|
| `ecpm` | trong `units[]` | eCPM **trung bình** các lần có số (làm tròn 4 chữ số). Lần không báo, hoặc báo ≤ 0, không vào mẫu |
| `ad_algorithm` | mức dòng | Thuật toán load — **nằm trong khoá gộp**, nên một dòng chỉ một thuật toán |

## L4. `ad_unit_id` cho mốc show và close

- `f_sdk_ad_show_data`: mediation điền (việc V4 phía Mediation) — có thể vắng cho tới khi họ làm.
- `f_sdk_ad_close_data`: SDK tự điền từ unit đã ghi ở mốc **impression** của cùng view.
- Mốc **request** và **show_attempt** thì **không** có: lúc đó nhiều unit đang load song song, SDK
  không biết cái nào sẽ chiếu nên không đoán.

## L5. `f_sdk_ad_request_data` — thêm `session_ad_request_index`

| Key | Kiểu | Mô tả |
|---|---|---|
| `session_ad_request_index` | int | Lần **xin ad** thứ mấy của format này trong phiên (từ 1) |

> Bốn sổ đếm trong phiên (`request` · `load` · `load_success` · `impression`) đều đếm theo
> **(phiên × format)**, không phải một sổ chung: eCPM đi theo thứ tự của chính format đó, mà gộp
> chung thì banner tự refresh vài chục lần sẽ đẩy số của inter/reward thành vô nghĩa. Cần số chung
> thì kho cộng lại được; tách ra từ số chung thì không.
>
> ⚠ **`ad_mediation` KHÔNG đổi** ở bản này. SDK có cân việc chuẩn hoá về `max`/`levelplay`/`admob`
> rồi bỏ (đổi giá trị một cột đang chạy thì mọi mapping lọc chữ cũ phải sửa — không đáng). Chữ trên
> dây vẫn là `"Max"`/`"Admob"`. Riêng việc **nhánh IronSource tự khai là `"Max"`** (nên kho không có
> LevelPlay) thì phải sửa ở nguồn: việc V5 của phiếu Mediation.

## Phép tự kiểm sau khi lên

> ⚠ **Cách đọc, loader chỉ 26/10 — viết sai là ra số đúng kiểu nhưng vô nghĩa:** `success` là **bool** ⇒
> `JSONExtractBool`, đừng `jsonb_extract_string` (trả `''`, đếm ra 0, trông y như "chưa gửi"). Phân biệt
> **vắng key** với **giá trị 0** chỉ làm được bằng
> `JSONType(toString(event_extra_props), 'estimated_ecpm') = 'Null'` — `JSONExtractFloat` trả `0` cho cả hai,
> nên câu `count(estimated_ecpm IS NULL)` **luôn ra 0**.

```sql
-- mọi dòng load thành công phải có eCPM (sau khi Mediation làm V1)
countIf(JSONType(toString(event_extra_props), 'estimated_ecpm') = 'Null') / count(*)
  where event = 'f_sdk_ad_load'
    and JSONExtractBool(toString(event_extra_props), 'success')                  → ~0

-- nối load ↔ show
count(distinct ad_view_id) có cả dòng load thành công và dòng f_sdk_ads_data      → phễu fill thật

-- eCPM lúc load vs doanh thu thật lúc chiếu (cùng ad_view_id)
estimated_ecpm / (ad_rev * 1000)                                                 → ~1

-- eCPM theo thứ tự trong phiên (câu hỏi gốc của yêu cầu 26/10)
avg(estimated_ecpm) group by ad_type, session_ad_load_success_index              → đường cong theo index
avg(ad_rev)         group by ad_type, session_ad_impression_index                → tiền thật theo index
```

> ⚠ **Đừng union hai đời rồi group theo `floor`** (loader nhắc 26/10): `floor` mới trên `f_sdk_ads_data` và
> `units[].floor` là **TIỀN** (USD/1000 impression), còn `floor` trên `f_sdk_ad_load_success`/`_fail` **đời cũ**
> là **HỆ SỐ bậc**. Hai cột cùng tên, khác đơn vị.
