# Sửa §4 hợp đồng live-ops: mốc khoảng cách reset theo ĐỢT, không theo lượt

> Gửi loader + visualizer. Viết 19/10/2026. **Đọc kèm `docs/design/LiveOpsEventContract.md`** —
> bản các bạn đang giữ là bản **14/10**, và §4 đã đổi sau khi bên mình code xong.
> **Chưa game nào ship log `f_sdk_live_ops_data`**, nên đổi lúc này **miễn phí**: không có dữ liệu cũ
> nào mang nghĩa cũ, không era, không backfill. Đó là lý do bên mình đổi ngay thay vì để sau.

## 1. Vì sao phải đổi

Bản 14/10 nói: event có `round_id` thì mốc khoảng cách **reset mỗi lượt** (Race: mỗi bảng đấu đo
lại). Code xong thì lộ ra hai chỗ vỡ, và cả hai đều làm **sai số liệu**, không phải bất tiện:

**(a) Bước-của-đợt và bước-của-lượt đạp sổ của nhau.** `eligible` / `open` nói về **cả đợt** nên
không mang `round_id`; `join` / `milestone` / `complete` / `claim` thì mang. Nếu sổ mốc khoá theo
`(đợt, lượt)` thì hai nhóm này là **hai thân phận khác nhau** → mỗi lần xen nhau là **reset**: mất
đúng khoảng cách **`eligible` → `join`** (*"mở cửa rồi bao lâu user mới chịu vào"*), và dòng
`milestone` ngay sau đó báo `*_since_enter = 0`.

**(b) Dòng của đợt ĐÃ ĐÓNG xoá sổ của đợt đang chạy.** Master League chạm chỗ này **mỗi lần sang
mùa**: màn bảng xếp hạng ghi `open` của mùa đang chạy, rồi ghi `complete` của **mùa vừa đóng** → dòng
mùa cũ reset sổ của mùa mới → `live_ops_enter_ts` của mùa đang chạy thành *"lúc user mở xem bảng mùa
trước"*, và 10 field khoảng cách sai theo. Mỗi mùa một lần, với đúng nhóm user chịu vào xem kết quả.

## 2. §4 luật 3 — ĐỔI

> **`*_since_enter` luôn đo từ mốc user vào ĐỢT, không reset theo lượt** — kể cả event nhiều lượt
> như Race.

Nghĩa là dòng `milestone` của **bảng đấu 2** nói *"3 ngày kể từ lúc vào đợt tuần"*, **không** phải
*"5 phút kể từ lúc vào bảng 2"*.

Câu *"qua bảng này tốn mấy lần thử"* vẫn trả lời được, bằng **`*_since_prev_step`** (bước → bước) —
field đó không đổi gì.

`live_ops_round_id` **vẫn nằm trong khoá chống trùng** của bên mình (mỗi bảng đấu vẫn là một lượt
riêng, vẫn có đủ dòng), nó chỉ **rời khỏi nhãn của sổ mốc**.

## 3. §4 luật 4 — THÊM

> Sổ mốc giữ **đợt đang chạy + đợt vừa đóng** (hai ô, trần cứng để pool client không phình theo số
> đợt).

Ba hệ quả cho phía đọc:

| Ca | Dòng mang gì |
|---|---|
| Dòng của đợt **đang chạy** | đủ `live_ops_enter_ts` + 10 field khoảng cách |
| Dòng `complete`/`claim` của **đợt vừa đóng** (gửi khi đợt mới đã chạy) | đủ, và đo từ mốc user vào **chính đợt đó**; `cycle_start_ts`/`cycle_end_ts` cũng là của **chính đợt đó** (bản chụp lưu trong ô, không mượn lịch đợt đang chạy) |
| Dòng của một đợt **xa hơn nữa** (đã bị đạp khỏi sổ) | `live_ops_enter_ts` + 10 field khoảng cách **VẮNG MẶT** |

Luật đọc: **vắng = không biết**, đừng COALESCE về 0. Bên mình chọn vắng thay vì đo lại từ 0, vì đo
lại từ 0 là báo *"user vừa vào đợt lúc này"* — một con số trông hợp lý mà sai hẳn.

## 4. §4 luật 5 — THÊM (cái này ảnh hưởng chart ngay)

> Event **chạy chồng** level thường (Race, Sky Path, Adventure) thì cặp
> `live_ops_level_passes_*` / `live_ops_level_attempts_*` **LUÔN VẮNG**.

Vì dòng level của chúng **không được khai event**: một ván có thể đồng thời thuộc Race, Sky Path và
Adventure, khai một cái là chọn bừa. Luật §3 của hợp đồng cấm chính việc đó.

Chart của mấy event này phải dùng cặp **toàn cục** `level_passes_*` / `level_attempts_*`, và **ghi rõ
trên nhãn** rằng đó là **chặn trên** (ván chơi chuyện khác chen giữa hai mốc cũng bị đếm).

Event có **bộ level riêng** (Master League) thì cặp `live_ops_level_*` có số thật — chúng khai event
được vì ván của chúng không chồng với ai.

## 5. Mốc cắt

Bên mình gắn `sdk_version` lên **mọi dòng log** (đã ship). Bản mang nghĩa mới của §4 là **bản sau
BigData 1.5.2** — số hiệu chính xác gửi kèm lúc publish. Nên nếu sau này cần phân biệt, phép lọc là
một điều kiện trên `sdk_version`, không phải suy từ `app_version` của game.

Nhưng nhắc lại điều quan trọng nhất: **chưa có dữ liệu nào mang nghĩa cũ**, nên thực tế các bạn không
cần era nào cả. Chỉ cần đọc theo bản này.

## 6. Việc phía các bạn

- [ ] Thay §4 trong bản các bạn đang giữ bằng mục 2 + 3 + 4 của thư này.
- [ ] Sửa lại chart/template nào đang định đọc `*_since_enter` theo nghĩa **per-lượt** (nếu đã dựng).
- [ ] Ghi luật *"vắng = không biết"* cho `live_ops_enter_ts` (mục 3) vào tài liệu luật đọc.
- [ ] Ghi *"chặn trên"* vào nhãn chart của Race / Sky Path / Adventure (mục 4).

## 7. Câu cần các bạn trả lời

| # | Câu hỏi | Vì sao cần |
|---|---|---|
| M1 | Có chart/template nào các bạn đã dựng dựa trên `*_since_enter` **per-lượt** chưa? | Nếu có thì bên mình cần biết để không ai đọc số sai một nhịp |
| M2 | Hai ô (đợt đang chạy + đợt vừa đóng) có đủ cho các bạn không? | Nếu phía đọc cần đo cả *"vào mùa bao lâu thì về đích"* trên những mùa xa hơn, bên mình phải tính cách khác — nhưng nói trước là trần cứng hai ô có lý do: pool của client không được phình theo số đợt |
| ✅ M3 | ~~**`f_sdk_live_ops_data` đã ký chưa?**~~ — **ĐÃ KÝ (owner xác nhận 20/10)**, hết chặn | Đây vẫn là việc chặn: **phải ký trước khi game đầu tiên ship**, nếu không dữ liệu những ngày đầu hỏng vĩnh viễn — đúng thứ tự đã làm với cụm quảng cáo v2 |
