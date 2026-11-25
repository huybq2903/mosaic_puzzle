# Gửi loader — 4 field mới trên `f_sdk_funnel_data` (BigData 1.3.8, additive)

Bối cảnh: mọi phép cắt cohort theo countdown của live-op (Master League vừa rồi) đều kẹt chờ
bảng lịch event từ ops vì log client không chở lịch; và vụ funnel "chết trắng do quên Register"
chỉ phát hiện được bằng nội suy hành vi stream. Bốn field này xử cả hai — đều additive,
`RemoveIfNull` (vắng mặt = client cũ / game không truyền, đọc như cũ).

## 1. Ba field game điền — meta chu kỳ

| Field | Kiểu | Nghĩa |
|---|---|---|
| `cycleIndex` | int | Số thứ tự chu kỳ (mùa/giải thứ mấy) — trục SORT bằng số; đừng sort tên `cycleId` ("season_9" > "season_10" khi sort chuỗi) |
| `cycleStartTs` / `cycleEndTs` | long | Mốc mở/đóng chu kỳ, **epoch millis UTC**, từ config event — đúng con số UI countdown của game đang vẽ |

Cook gợi ý: `countdown lúc log = cycleEndTs − ts` → bucket "join khi còn ≥4 / 1–4 / <1 ngày"
đứng ngay trên dòng Join, **không cần dim lịch event nữa**.

Hai luật đọc PHẢI có trong catalog:
- **Chu kỳ động**: game extend event giữa mùa thì các dòng sau mang `cycleEndTs` mới — đó là
  lịch sử thật, không phải data bẩn. Đọc theo giá-trị-tại-thời-điểm-log, đừng assume bất biến
  trong một `cycleId`.
- **Lịch ngược (`end ≤ start`)**: client CỐ Ý giữ raw (chỉ warning phía client) — gặp là bug
  config event, cần thấy để truy nguồn, đừng silently drop.

## 2. Một field SDK tự đóng — `funnelShape`

Giá trị: `once_ordered` / `once_per_cycle` / `repeatable`. Đây là **hình dạng van ĐANG ÁP tại
client lúc log** (SDK đọc từ registry của nó), KHÔNG phải shape "đúng" theo thiết kế — vì thế
nó mang tin: không phải hằng số, nó đổi theo build/trạng thái đăng ký, và sai được.

Vai DQ đề nghị các bạn dựng rule: **catalog giữ shape chuẩn của từng funnel** (từ nay mỗi lần
đăng ký funnel mới bọn mình sẽ khai kèm shape) — dòng nào mang `funnelShape` lệch catalog =
client quên `Register`, funnel đó đang chạy nhầm luật ftue phía client (bước bị veto trắng,
count tụt không phải vì user bỏ chơi). Cùng khuôn với vệt nhận dạng xoay-id đã ký: wire chở
thực tế, catalog giữ chuẩn, lệch = chuông.

## 2b. Cùng chuyến 1.3.8 — biên turn của LEVEL đổi ngữ nghĩa (đọc kỹ, ảnh hưởng cook argMax)

Hợp đồng cũ §A3.2/3: biên turn = Start-to-Start, 1 Start + 2 terminal (revive) cùng
`play_turn_id`, cook `argMax` bản-cuối. **Từ 1.3.8: TERMINAL ĐÓNG LƯỢT** — căn cứ game-theory
hiện hành: game chỉ log fail khi hết đường lật kèo (revive xảy ra TRƯỚC khi fail được log), nên
ca revive-cùng-turn không tồn tại thực tế, 2 terminal = 2 lượt chơi.

Đổi gì trong data:
- Game tích hợp ĐÚNG (mỗi lượt một Start): **không đổi một byte nào** — mỗi turn vẫn 1 Start +
  1 terminal, cook argMax thành vestigial nhưng vô hại, giữ cũng được.
- Game QUÊN Start (ca thật vừa bắt được ở một module): trước đây từ ván mồ côi thứ hai, id ván
  trước bị dùng lại → argMax **nuốt mất ván** hoặc **lật ngược kết quả ván trước** (Pass thành
  Fail). Từ 1.3.8: mỗi ván mồ côi tự sinh id riêng, nhận diện được bằng **turn không có dòng
  Start** — DQ per game/bản đo được mức quên-Start, cùng khuôn vệt xoay-id.
- Double-fire cùng kết quả: vẫn cùng id như cũ, argMax khử — không đổi.

## 2c. BỔ SUNG 14/10 — hai field GIỜ CHƠI trên cùng event (xin ack một lượt)

Yêu cầu từ phía phân tích: "người chơi mất bao lâu để đi từ đầu phễu tới mốc này, và từ mốc trước
tới mốc này". Hai field mới, **SDK tự đóng** (game không truyền được):

| Field | Kiểu | Nghĩa |
|---|---|---|
| `playSecSinceEnter` | long | Giờ **chơi** (giây) từ mốc **vào phễu** của chu kỳ này tới bước này. Bước đầu chu kỳ = **0** |
| `playSecSincePrevStep` | long | Giờ **chơi** (giây) từ **bước hợp lệ gần nhất** của funnel đó trong cùng chu kỳ. Bước đầu chu kỳ: **vắng mặt** |
| `levelsSinceEnter` | long | Số **level mới vượt qua** từ mốc vào phễu tới bước này (nguồn: `MaxPassedLevel` của SDK). Bước đầu chu kỳ = **0** |
| `levelsSincePrevStep` | long | Số **level mới vượt qua** từ bước hợp lệ gần nhất. Bước đầu chu kỳ: **vắng mặt** |

Vì sao **hai cặp** chứ không một: chúng bù lỗ cho nhau. Giờ chơi **đếm cả** lúc user mở app rồi để
đó (ngồi lobby 20 phút vẫn cộng 1.200 giây); số level thì không. Đổi lại số level **đứng yên** khi
user cày lại một màn (thua 10 lần vẫn là 0 level); giờ chơi thì không. "Mốc 5 cần 40 phút chơi và
12 level" nói được nhiều hơn hẳn chỉ một trong hai.

Năm điều cần vào catalog:

1. **Đồng hồ là thời gian CHƠI tích luỹ, không phải wall-clock**: monotonic, **chỉ chạy khi app ở
   foreground**. Nên hai bước cách nhau một đêm ngủ vẫn có thể chỉ chênh vài chục giây — đúng theo
   thiết kế, không phải lỗi. Muốn khoảng wall-clock thì trừ `ts` của hai dòng, dữ liệu đã có sẵn.
2. **Vắng mặt = KHÔNG BIẾT, không phải 0.** Hai ca: bước đầu chu kỳ (`playSecSincePrevStep`), và
   ca sổ mốc client lệch sổ phiên (delta ra âm → client bỏ field + ghi warning). Xin **đừng
   COALESCE về 0** — đó đúng là ca cần đếm riêng.
3. **Mốc "vào phễu" = bước đầu tiên hợp lệ trong chu kỳ, KHÔNG phải `join`.** Funnel mới phần lớn
   không có bước `join` (Card Collection: `eligible`/`open`; Endless Treasure: chỉ `open`), neo
   theo action là mấy event đó trắng số. Mốc này **nhất quán trong cùng `funnelName`** nhưng khác
   nhau giữa các funnel — nên đừng trung bình xuyên funnel.
4. `playSecSincePrevStep` tính theo **thời điểm log**, không theo thang `priority`: shape
   `once_per_cycle` cho phép nhảy cóc mốc, neo theo "priority ngay dưới" thì ca nhảy cóc trắng số.
5. Cặp level đếm **level vượt qua**, KHÔNG đếm số ván chơi (thua 10 lần một màn = 0). Nguồn là
   `MaxPassedLevel` của SDK, không phải `currentLevel` game truyền vào `OnStep` — field đó optional
   nên nếu đọc từ đó thì cột rỗng lỗ chỗ.

⚠ Nói trước một câu vì chắc các bạn sẽ hỏi: đây là **field toàn fleet** — mọi funnel của mọi game
đều có — **không phải key riêng của một event**. Nó không phạm luật "không thêm cột cho từng
event"; chính vì thế bên mình mới đặt nó thành cột hợp đồng thay vì nhét vào blob extras.

## 3. Việc phía các bạn

1. Ack 4 field của 1.3.8 **+ 2 field giờ chơi ở mục 2c** (tất cả additive trên event sống
   `f_sdk_funnel_data`).
2. Ghi 2 luật đọc ở mục 1 vào catalog.
3. Backfill shape chuẩn cho các funnel đã đăng ký: `ftue` = once_ordered; `battle_pass`,
   `daily_quest`, `piggy_bank`, `lucky_wheel` = repeatable (shape này client NGHỈ HƯU 05/09 —
   chỉ còn vocab di sản dùng); `event_master_league` = once_per_cycle (TÊN MỚI thay
   `master_league` — tên cũ đóng băng, data dưới nó là era hỏng đừng JOIN lẫn); event tương lai
   theo khuôn đều `event_*` = once_per_cycle.

---
*File xoá sau khi các bạn ack, bản lưu ở git history — như lệ.*
