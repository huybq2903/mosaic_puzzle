# SDK BigData → loader: ba cột thôi gửi số giả, chuyển sang VẮNG (12/11/2026)

> Gửi: loader. Từ: owner SDK BigData.
> Ba đề nghị sinh ra từ thư trả lời đội access (`bigdata-access-service/docs/semantic/reply-sdk-client-2-dq-log-game.md`, mục A2 · A3 · A4).
> Cả ba theo cùng một luật: **vắng còn hơn một giá trị sai trông hợp lệ**. Owner SDK đã đồng ý cả ba. **Chưa code, chờ các bạn ký**, vì
> phía kho có chỗ đang biến "vắng" thành số sai — nếu SDK đổi mà kho không đổi theo thì số sẽ **tệ hơn** hôm nay. **Cả ba thay đổi do
> SDK làm; phía kho chỉ có hai việc nhỏ** (mục 1: một chỗ; mục 3: hai chỗ). Mục 2 kho không phải làm gì.

## Tóm tắt

| # | Cột | Hôm nay SDK gửi | Đề nghị | Kho cần làm |
|---|---|---|---|---|
| 1 | `startup_duration_ms` trên `app_open` cold | Luôn có số — kể cả khi app bị OS mở ngầm rồi người chơi mở sau hàng chục phút | **Vắng** nếu app đã từng xuống nền trước khi bản tin cold bắn | Dòng phiên: vắng phải ra **NULL**, không về 0 |
| 2 | `event_duration` trên dòng level (`duration` phía SDK) | `0` khi game không truyền thời lượng | **Vắng** khi game không truyền | **Không phải làm gì** — dòng lượt đã ra NULL khi vắng. Chỉ lưu ý cách đọc trung bình |
| 3 | `value_before` / `value_after` trên dòng tài nguyên hạng `unlimited` | Nguyên số game đưa — thực tế có game đưa **ticks của mốc hết hạn** | **Bỏ** hai field này khi hạng là `unlimited` | Luật số dư + một ring DQ phải bỏ qua hạng `unlimited` |

**Không cần era / không cần đổi tên event**, lý do chung: ở cả ba cột, luật đọc an toàn cho dữ liệu cũ (*"0 hay vắng đều coi là không biết"*,
*"hạng `unlimited` loại khỏi kinh tế"*) **vẫn đúng nguyên văn** cho dữ liệu mới. Bản mới chỉ làm số sạch hơn: ít dòng rác hơn, và `0` mới
có nghĩa là `0` thật. Không có cột nào đổi đơn vị hay đổi nghĩa của giá trị có mặt.

---

## 1. `startup_duration_ms` — vắng khi app từng xuống nền trước bản tin cold

**Hiện trạng (code):**
- Bắt đầu đo khi code C# sống dậy; kết thúc khi chuỗi init tuần tự của SDK chạy tới bước app_open.
- Đồng hồ đơn điệu dừng khi **máy** ngủ, nhưng không dừng khi **app** bị treo ở nền.
- ⇒ OS mở process ngầm (push, prefetch), người chơi mở app sau đó ⇒ quãng chờ bị tính vào. Đây là đuôi hàng chục phút mà đội access
  đo được.

**Đề nghị:** SDK đã ghi mốc mỗi lần app xuống nền. Nếu có mốc nào **trước** khi bản tin cold bắn, thì `startup_duration_ms` **vắng**. Các
cột khác của `app_open` không đổi, và bản tin vẫn bắn.

**Kho cần làm:** `SdkSessionState.absorbAppOpen` đang đọc `startup_duration_ms ?: 0L` (*"jsonb_extract_int64 vắng = 0"*). Phải đổi thành
**NULL khi vắng**. Nếu không, cột `startup_duration_ms` của dòng phiên sẽ có thêm một khối số `0` giả ở đúng những ca này.

Kèm một câu cho các bạn (không thuộc đề nghị): dòng phiên lấy `open_type` từ `app_open` sớm nhất nhưng lấy `startup_duration_ms` từ dòng
cold sớm nhất. Đội access vì vậy thấy phiên `hot` có "thời gian khởi động" — SDK **chưa bao giờ** gửi cột này trên bản tin hot.

## 2. Thời lượng lượt level — vắng khi game không truyền

**Hiện trạng (code):** `Level.OnPass/OnFail(…, TimeSpan duration = default)`; payload luôn ghi `(long)duration.TotalSeconds`. Game bỏ
trống ⇒ wire nhận `0`, không phân biệt được với lượt thật sự dưới 1 giây.

**Đề nghị:** `duration` thành nullable, **vắng khỏi payload khi game không truyền**. API cũ giữ nguyên chữ ký nguồn (game không phải sửa
code). Giá trị game truyền tường minh, kể cả `0`, vẫn gửi như cũ.

**Kho cần làm: không có gì.** `LevelTurnState` đã đọc cột này **không** kèm `?: 0`, nên dòng lượt tự ra NULL. `SdkSessionState` cộng
`event_duration ?: 0L` vào tổng/max của phiên — với tổng và max thì vắng = 0 không làm sai số. Chỉ một lưu ý **cách đọc**: trung bình
"thời lượng lượt" chia theo số lượt thì chỉ đếm lượt có thời lượng.

⚠ **Giới hạn phải nói trước:** đường Level Core (`FLevelManager.OnLevelResult(win, int time, …)`) bắt game truyền `time` là số bắt buộc.
Game truyền `0` thì đó là `0` tường minh, SDK **không đoán** để đổi thành vắng. Đề nghị này chỉ sạch hoá những game gọi thẳng API của
BigData. Ca `0` đi qua Level Core vẫn là việc của đội game (thư access mục A3).

## 3. `value_before` / `value_after` — bỏ khi hạng là `unlimited`

**Hiện trạng:** hạng `unlimited` là *"quyền dùng vô hạn CÓ THỜI HẠN — không phải số dư"* (đã ký 06/11, phi kinh tế). SDK vẫn chuyển
nguyên `value_*` của game lên wire. Đội access đo được `value_after` cỡ 6,39 × 10¹⁷, đúng bằng `DateTime.Ticks` năm 2026: game ghi
**mốc hết hạn** vào ô số dư.

**Đề nghị:** khi hạng tiền của dòng là `unlimited` (SDK đã tự tra sổ hạng lúc gửi), SDK **bỏ** `value_before` / `value_after` khỏi payload,
kèm một cảnh báo dev. `amount` giữ nguyên, và nghĩa đúng của nó là **số giây được cộng / bị trừ**.

**Mốc hết hạn không mất đi đâu:** nó đã có chỗ riêng là nhãn người chơi `lives_unlimited_end_ts` (epoch ms), đã hướng dẫn cho đội game.
Bên mình **không** đề nghị cột `expire_at` trên dòng tài nguyên: đó là một cột chỉ có nghĩa khi hạng là `unlimited`, tức một field bị field
khác điều khiển nghĩa.

**Kho cần làm:**
- **Luật số dư profile** (`ProfileRuleType`, `value_now = value_after ?: (value_now ± amount)`): khi vắng `value_after`, luật sẽ **cộng dồn
  `amount`** ⇒ "số dư" `unlimited_live` thành tổng số giây từng nhận, cũng vô nghĩa. Đề nghị luật này **bỏ qua hạng `unlimited`**.
- **Ring DQ** `ResourceRings` (*"có dòng tiền mà không ai có số dư ⇒ game chưa gửi `value_before/after`"*): hạng `unlimited` sẽ luôn rơi vào
  đây. Đề nghị loại hạng này khỏi ring.

---

## Câu hỏi cho các bạn

1. Ký cả ba? Có cột nào các bạn muốn giữ hành vi cũ không?
2. Mục 1: đổi `startup_duration_ms` của dòng phiên sang NULL — các bạn làm được trước hoặc cùng lúc bản SDK publish không?
3. Mục 3: luật số dư bỏ qua hạng `unlimited` — làm ở luật profile hay ở chỗ khác, tuỳ các bạn.

Ký xong bên mình code, kèm test, và gửi **bản + ngày** khi publish.

---

## Kho trả lời 12/11 (`loader-v3-work/ticket9/Reply-Loader-AbsentNotZero.md`) — ĐÃ KÝ cả ba, đã code

- ✅ **Mục 1:** kho sửa **hai** chỗ chứ không phải một — ngoài tracker phiên, đường **cook** cũng biến vắng thành 0
  (`jsonb_extract_int64` trả 0 khi thiếu key). Đã sửa cả hai, **lên trước bản SDK** (silver hôm nay chưa có bản tin cold nào thiếu
  key nên không đổi số). Câu kèm (phiên `hot` mang "thời gian khởi động") kho cũng đã sửa: cột chỉ gắn khi phiên mở đầu bằng cold.
- ✅ **Mục 2:** kho không phải làm gì, chỉ sửa mô tả `duration_sec` (*"NULL = lượt không terminal, hoặc game không truyền thời lượng"*).
- ✅ **Mục 3:** kho **không phải làm gì** — bên mình lo thừa. Luật số dư profile chỉ chạy cho `hard`/`soft`/`live`; ring DQ số dư đã
  loại `unlimited_live` theo tên, và nhánh "chưa gửi value_*" xét ở mức game nên không dính. Kho lưu ý: SDK bỏ theo **sổ hạng phía
  client** ⇒ game chưa khai hạng cho tên đó thì vẫn gửi ticks (kho gắn hạng phía server nên kho không bị ảnh hưởng).
- **Code SDK:** BigData **1.7.3**, +8 test (589/589).

**Còn nợ kho:** bản + ngày khi publish.
