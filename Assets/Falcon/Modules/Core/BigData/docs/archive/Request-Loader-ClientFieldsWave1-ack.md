# Nhận ticket "trường contract còn thiếu — đợt 1" — gửi đội api/viz (06/11/2026)

> Trả lời `bigdata-access-service/docs/semantic/ticket-sdk-client-1-truong-con-thieu.md` (05/11).
> Soát từng mục **đối chiếu code đang có**, không đối chiếu ký ức. Kết quả: **ba mục đã có trong
> SDK** (một trong ba bị hiểu là chưa có), **bốn mục là lỗ thật**, và **một mục viết thiếu điều
> kiện — thiếu chỗ đó thì làm theo là sai ngược**.
> ⚠ Bên mình **không nhắc lại số liệu nội bộ** của các bạn trong file này (tên game, số dòng, số
> tiền) — doc SDK đi theo module, không phải chỗ giữ số nội bộ.

## 0. Phán quyết từng mục

| Mục | Phán quyết |
|---|---|
| §0 hiện gói IAP | 🟢 Không phải việc SDK — contract có đủ, việc đẩy game lên bản |
| **§1 log UI gộp phiên** | ⚠ **ĐÃ CÓ từ 1.5.x** (`f_sdk_ui_impression`). Đề xuất cần sửa **năm** chỗ — §1 dưới |
| §1b `f_sdk_level_action` | ✅ **Lỗ thật, việc của SDK, và là mục TO nhất ticket** — cần loader ký trước |
| §2 mã booster chung | ✅ Lỗ thật. Chọn **(a)**, và nó dùng **chung cơ chế** với `currency_class` |
| §3 hạng race / league | ✅ Lỗ thật, **nhỏ** — và nó gom được chỗ 4 doc event đang mỗi event một key |
| §4 `f_sdk_update_prompt` | ⚠ Thêm được, nhưng **chưa ai gọi được**: repo không có module nào hiện popup cập nhật |
| **§5 cờ test iOS** | 🔴 **ĐÃ CÓ** `is_test` + `test_source` trên **mọi** log, cả iOS. Nhưng hai thứ các bạn xin thêm thì một cái **cố ý không làm**, một cái bị **luật cứng cấm** — §5 dưới |
| §6 heartbeat | 🟢 Không phải việc SDK |
| **§7 gắn `live_ops_event`** | 🟢 Không phải việc SDK — ⚠ **nhưng luật viết thiếu điều kiện**, §7 dưới |
| Chặn: loại tiền lạ | ✅ Chính là ticket `currency_class` của loader — làm một lần cho cả hai |
| Chặn: funnel chưa `Register` | 🔴 **ĐÃ CÓ** `FunnelRegistry.Register` + lưới bắt quên-Register |
| Chặn: `start_at` = 1970 | ✅ Lỗ thật và **rẻ** — nhưng cần các bạn nói rõ cột nào, §"start_at" dưới |
| Chặn: mã giao dịch rỗng | ⚠ Cảnh báo **đã có**; **không** chặn gửi — lý do ở dưới |

---

## §1. Log UI gộp phiên — đã có, và năm chỗ đề xuất nên sửa

`f_sdk_ui_impression` đang chạy thật: cụm gộp theo khoá **`(surface_id, ui_action, live_ops_event,
live_ops_cycle_id, …)`** kèm `count`. Bốn doc event live-op bên mình vừa viết **đang dùng** nó.

### 🔴 1. `impression_count` / `click_count` **không nên là hai cột**

`ui_action` đã tách hai thứ đó thành **hai DÒNG**. Thêm hai cột là **hai cách biểu diễn cho một
chuyện**, và rồi sẽ có ngày hai cách lệch nhau.

➡ CTR theo vị trí đọc bằng `sum(count)` group theo `ui_action`. 🔴 Và **không** `count(*)` — cụm gộp
nên đếm dòng là đếm sai, chỗ này đã là lỗi đọc thật ở bốn event.

### 🔴 2. `impression_ad_not_ready_count` nên là **một giá trị `ui_action` thứ ba**

Cùng lý do. Nó là *"đã hiện mà QC chưa sẵn"* — một **loại sự kiện**, không phải một bộ đếm song song.
Làm vậy thì nó tự có đủ mọi chiều mà hai loại kia có, không cần cột mới nào.

🟢 Và phần nghĩa các bạn nêu thì đúng và đáng làm: nó **khác** `ad_available` trên
`f_sdk_ad_show_attempt`, vì cái kia là lúc người **đã bấm**.

### 🔴 3. Khoá gộp các bạn đề xuất **bỏ mất trục live-op** mà khoá hiện tại đang có

Đề xuất ghi khoá là `session × surface_id × ui_action × level × trigger` — vắng
`live_ops_event` / `live_ops_cycle_id`. Hai cột đó **đang có** trong khoá, và **bốn doc event đang
đứng trên chúng** (bề mặt `team_quest_join` là đường duy nhất tin được cho bậc phễu *"đã thấy popup
Join"*). Bỏ là hỏng cả bốn.

➡ Khoá đúng là **cái đang có, CỘNG thêm** `level` và `trigger`, không phải thay.

### ⚠ 4. `level` trong khoá: lỗ thật, nhưng phải biết giá

Cụm trải **cả phiên**, mà người chơi đổi level giữa phiên ⇒ một cụm **không thể** mang một giá trị
`level` duy nhất. Nên chỉ có hai đường: **đưa `level` vào khoá** (số dòng nhân lên theo số level
người chơi trong phiên) hoặc **bỏ hẳn câu hỏi theo level**. Không có đường thứ ba.

➡ Bên mình nghiêng **đưa vào khoá**: `trigger` cũng vậy, và cả hai đều là chiều người ta sẽ hỏi.
Nhưng số dòng là của các bạn trả, nên các bạn chốt.

### 🔴 5. "Lưu đệm và gửi lại lúc mở app": SDK **cố ý KHÔNG** persist, và lý do nằm trong code

`UiExposureClusterService` ghi rõ: *"KHÔNG persist qua kill: telemetry exposure, doanh thu/phễu không
đứng trên nó"*. Đây là một quyết định có chủ ý, không phải chỗ bỏ sót.

Và **chính các bạn đã viết ra cái giá**: *"cụm phục hồi dạt sang phiên / ngày sau"*. Mà dùng chính của
mục này là **CTR theo phiên** — nên persist là tự làm hỏng thứ mình cần: mỗi cụm phục hồi sẽ nằm ở
**sai phiên**, và không ai phân biệt được nó với một cụm thật của phiên đó.

➡ Đổi trade-off này cần owner quyết, không phải bên mình quyết. Bên mình nêu lựa chọn thứ ba: giữ
không persist, và **đo phần mất** — flush lúc pause đã có, nên phần mất là "cụm chưa flush lúc bị
kill". Nếu các bạn cần con số đó thì nó đo được, rẻ hơn persist và không làm lệch grain.

### ⚠ Và chuyện "chưa vào contract loader-v3" thì **đang chờ chính phía kho**

Đúng là nó chưa vào contract. Nhưng bên mình **đã xin ký từ trước**:
`Request-Loader-UiImpression.md` — *"⏳ chờ ký `f_sdk_ui_impression`, phải ký trước khi game ship Ui
API"* — và tới nay chưa có bản ký.

➡ Nên mục §1 không chặn ở phía SDK. Nó chặn ở **một chữ ký**, và việc đó nằm cùng phía với các bạn.
Ký xong thì cả `level` / `trigger` / `ui_action` thứ ba đi chung một lượt, không phải hai lần sửa
contract. Bốn doc event live-op cũng đang chờ đúng chữ ký đó
(`Request-Loader-LiveOpsEventBacklog.md`).

---

## §5. Cờ test: đã có — và hai thứ xin thêm thì không nên

**Đã có, trên mọi log, cả iOS:** `is_test` + `test_source` (`editor` / `dev_build`), đi qua
`IFCustomInfoRepository` nên dán lên **mọi** bản tin, không cần plumbing. Tính tại chỗ từ
`Application.isEditor || Debug.isDebugBuild`.

Hai thứ các bạn xin thêm:

| Xin | Trả lời |
|---|---|
| **TestFlight** | ⚠ Hiện **không** nhận ra, và lớp đó đã tự ghi hệ quả: *"build QA ở chế độ release trông như production"*. Có một đường **đúng luật**: TestFlight đọc được từ **sandbox receipt** — tức **suy từ môi trường**, không phải do ai khai, nên không phá luật dưới. Nhưng nó cần đọc native iOS, không có API Unity thuần ⇒ owner quyết có đáng hay không. Đường rẻ hơn mà lớp đó đã gợi: **đổi bundle id** cho bản QA, hoặc cho nó khỏi gửi bằng config |
| **"máy trong danh sách tester do game khai"** | 🔴 **Không.** Luật cứng chốt owner 16/10: *"bản PRODUCTION tuyệt đối không bao giờ mang cờ này... không ai ghi đè được: không API, không remote config, không common param"*. Lý do ghi ngay tại chỗ: **một lần dev quên tắt là hỏng toàn bộ dữ liệu của bản phát hành, mà hỏng kiểu đó không có dấu hiệu gì.** Một danh sách do game khai là đúng cái cửa đó |

🟢 Và như các bạn tự ghi ở đầu mục: dữ liệu test đã tách sang `f_sdk_event_test`, nên mục này có thể
đóng luôn trừ phần TestFlight.

---

## §7. Gắn `live_ops_event` lên bản tin level: luật thiếu MỘT điều kiện

Ticket viết: *"**Ván chơi trong event** cũng phải gắn `live_ops_event` trên bản tin level"* — **không
điều kiện**. Làm đúng câu đó là sai ở một nửa số event, và sai **ngược hướng** với chỗ nó định sửa:

| Loại event | Phải làm gì | Làm sai thì |
|---|---|---|
| Event có **bộ level riêng** (league, key collection, race…) | ✅ **PHẢI gắn** | đúng ca các bạn nêu: ván event lẫn vào số map chính |
| Event **chạy chồng lên map chính** (ván thường cũng tính) | 🔴 **KHÔNG được gắn** | lọc *"chỉ level map chính"* sẽ **loại bỏ ván map chính thật** — mất số, im lặng, và mất đúng nhóm người chơi tích cực nhất |

➡ Luật đủ là: **gắn khi và chỉ khi ván đó thuộc bộ level RIÊNG của event.**

Bên mình đã ra án này hai lần ở phía doc event: một event chạy chồng thì *"dòng level không mang
`live_ops_event`"* là **đúng hợp đồng** (doc Ladder Streak, bảng "đang chạy — đừng sửa", và câu E ở
§9 nói rõ cả hai nhánh); một doc event khác (Race) bên mình đã **rút lại** yêu cầu gắn đúng vì lý do
này.

⚠ Nên nếu các bạn đẩy luật không điều kiện xuống các game, bên mình sẽ nhận lại được một loạt event
chạy chồng bị đóng dấu — và lúc đó không gỡ lại được từ phía đọc, vì không có cột nào nói *"ván này
vốn là map chính"*.

---

## Các mục là lỗ thật — và cách bên mình đề nghị xếp việc

### §1b `f_sdk_level_action` — mục to nhất, và đáng nhất

🟢 Nhận. Lỗ đúng như mô tả: booster chỉ có **tổng theo lượt**, hồi sinh **không có gì**, và không ai
biết chuyện xảy ra ở **% nào** của level.

Hai điểm bên mình thấy đề xuất làm đúng:

- **`play_turn_id`** để nối sang level / lần thử / kết cục — đúng, và nghĩa là bản tin này **không
  cần** gửi lại kết quả. Bớt một nguồn trùng.
- **`offered` / `accepted` cùng một dòng**, mời-mà-từ-chối cũng có dòng — đúng, vì thiếu nhánh từ
  chối là mẫu số rỗng.

Một điểm bên mình sửa: *"`progress_pct` **bắt buộc**; SDK cảnh báo khi game gửi 0 suốt"*. Cảnh báo
"gửi 0 suốt" là nhận định về **một chuỗi** bản tin, mà param chỉ thấy **một** bản tin ⇒ phải giữ state
để phát hiện. Rẻ hơn và bắt được nhiều hơn: cảnh báo ngay khi **thiếu** (null) hoặc khi
`progress_pct` nằm ngoài `[0, 100]`. Còn "0 suốt" là chuyện **DQ của kho**, và kho thấy rõ hơn client.

⏳ Đây là **event mới** ⇒ theo §11 của spec, **loader phải ký trước khi SDK publish**. Bên mình chưa
code dòng nào trước khi có bản ký.

### §2 mã booster chung — chọn **(a)**, và nó dùng chung cơ chế với `currency_class`

Chọn **(a) sổ đăng ký** `Booster.Register(id)`, không chọn (b) thêm `booster_id` lên dòng tài nguyên.
Lý do: (b) chỉ **nối được** hai bên, còn (a) **chặn tên lệch ngay từ đầu** — mà tên lệch là nguyên
nhân gốc các bạn nêu. Và (b) thêm một cột mang lại đúng thứ `item_id` đã mang.

🟢 Giữ *"id lạ → cảnh báo (log dev) + vẫn gửi"* như ticket viết. Chặn gửi là mất số.

ℹ️ **Ba việc cùng một hình dạng**, nên bên mình làm **một cơ chế**, không ba:

| Việc | Trạng thái |
|---|---|
| `FunnelRegistry.Register` | 🟢 **đã có và đang chạy** |
| `DeclareCurrency` (ticket loader) | ✅ sẽ dựng trên cùng khuôn |
| `Booster.Register` (mục này) | ✅ dùng lại khuôn đó |

### §3 hạng race / league — nhỏ, và gom được một chỗ đang lộn xộn

🟢 Nhận: `rank` · `group_size` · `score` · `group_id` trên dòng live-op lúc kết thúc round / mùa.

ℹ️ Và nó **dọn một chỗ bên mình đang chịu**: bốn doc event hiện mỗi event tự đặt một key riêng cho
hạng. Có field chung thì các event sau dùng field, không đặt thêm tên.

### §4 `f_sdk_update_prompt` — thêm được, nhưng **chưa ai gọi được**

⚠ Bên mình đã tìm: **không có module nào trong repo hiện popup cập nhật**. Nên API log thêm vào thì
**không có người gọi** — nó chỉ chạy khi mỗi game tự gắn vào UI của mình, tức giá trị phụ thuộc hoàn
toàn vào việc đẩy từng game.

➡ Đề nghị: **xếp sau** §1b / §2 / §3, hoặc kèm điều kiện "khi có một module nào nhận việc hiện popup
này". Nếu các bạn vẫn muốn có sớm thì nói, nó nhỏ.

---

## `start_at` = 1970 — nhận, nhưng cần các bạn nói rõ cột nào

✅ Lỗ thật: trong code level **không có chỗ nào** kiểm mốc giờ. Và tỉ lệ các bạn đo là lớn, nên đáng
làm — mục này nằm trong tầm bên mình sửa, rẻ.

❓ **Nhưng phải biết `start_at` là cột nào**, vì hai khả năng dẫn tới hai việc khác hẳn:

| Nếu `start_at` là | Thì |
|---|---|
| một field **game truyền vào** bản tin level | ✅ Bên mình kiểm trong `CorrectValues` như mọi field khác — error log + bỏ key. Làm được ngay |
| suy từ **mốc giờ của envelope** (giờ dựng log) | 🔴 Bên mình **không sửa được giá trị** — nó là đồng hồ máy người chơi. Chỉ phát hiện được (so với mốc hợp lý) và **đánh cờ**, để các bạn lọc. Mà đánh cờ là **thêm field wire** ⇒ cần ký |

➡ Cho bên mình tên cột và nguồn của nó, bên mình trả lời trong một lượt.

## Mã giao dịch IAP rỗng — cảnh báo đã có; **không** chặn gửi

**Đã có:** `InAppParam.transactionId` đi qua `CheckNonBlank` ⇒ rỗng thì **`Debug.LogError`** ngay, và
giá trị lên wire thành **`"Unknown"`**, không phải `""`.

➡ Nên các game các bạn thấy gửi `transaction_id = ""` thì **không đi qua đường này**: hoặc bản SDK cũ,
hoặc dựng bản tin bằng đường khác. Đẩy game lên bản là đủ, không cần SDK thêm gì.

ℹ️ **Một mẹo đọc, miễn phí:** trên bản hiện tại ca đó hiện ra thành `transaction_id = 'Unknown'`, nên
DQ nên bắt **cả hai** chuỗi. Và `"Unknown"` thì **không dedupe được** — nhiều giao dịch cùng mang một
giá trị, nên đừng đếm distinct trên nó.

🔴 **Còn "không gửi bản tin mua khi thiếu mã store": bên mình không làm.** Một dòng mua thiếu mã vẫn
là **doanh thu thật**; bỏ nó là mất tiền trong báo cáo để đổi lấy một ô sạch. Và ca gian lận các bạn
nêu (một tài khoản mua hàng nghìn lần trong một ngày) **phía kho thấy rõ hơn client** — client chỉ
thấy một giao dịch mỗi lần, không thấy chuỗi.

## Hai mục "không thuộc client" — xác nhận

🟢 Thời gian click → cài (AppsFlyer S2S) và `product_type` từ postback store: đồng ý, không phải SDK.

---

## Phản hồi của họ — 06/11, owner chốt (và bốn chỗ bên mình nói chính xác lại)

Đội api/viz **nhận hết**, và owner của họ chốt:

| Mục | Chốt của họ | Nghĩa với phía mình |
|---|---|---|
| §1 log UI | nhận **cả 5** điểm sửa. Khoá **giữ nguyên + thêm `level` và `trigger`** (chấp nhận số dòng tăng) · `ui_action` thứ ba "QC chưa sẵn" · **không persist, chỉ đo phần mất** | ✅ Việc của BigData, quyết định đã đủ để code. ⏳ Publish chờ ký |
| §1b `f_sdk_level_action` | nhận theo sửa của mình (`progress_pct` cảnh báo khi **vắng** / ngoài `[0,100]`; "0 suốt" là DQ kho) | ⏳ Chưa code dòng nào trước khi có bản ký |
| §2 `Booster.Register` | đồng ý (a), **làm chung cơ chế** với khai hạng tiền | ⚠ chỉ **nửa** độc lập — xem dưới |
| §3 hạng race / league | đồng ý | ⏳ thêm field ⇒ chờ ký |
| §4 popup cập nhật | đồng ý **xếp sau** | 🟢 hết việc lúc này |
| §5 cờ test | **ĐÓNG.** Rút lại ý "danh sách tester do game khai" (đúng luật cứng 16/10), và **không cần** TestFlight | 🟢 hết việc |
| §7 gắn `live_ops_event` | **đã sửa thành có điều kiện.** Họ soát dữ liệu: hiện các game đang gắn **đúng** luật mới | 🟢 hết việc — và chỗ này nếu không nói thì luật sai đã xuống tới game |
| `start_at` = 1970 | cột đó suy từ **giờ dựng bản tin Start (envelope)**, không phải field game truyền. Loader báo giá trị 1970 **đã hết** ở gold ⇒ **ĐÓNG** | 🟢 Hết việc. Đúng nhánh thứ hai trong hai nhánh mình đặt ra — và đó là nhánh SDK **không sửa được giá trị** |
| Mã giao dịch rỗng | nhận, không chặn gửi; DQ của họ sẽ bắt **cả** `""` **và** `"Unknown"` | 🟢 hết việc |

🔴 **Chữ ký `f_sdk_ui_impression` và `f_sdk_level_action` đã chuyển sang loader (ticket loader đợt 9
mục 4).** Đây là thứ duy nhất còn chặn hai mục to nhất.

### Bốn chỗ bên mình nói chính xác lại, đã gửi họ trước khi loader ký

Soát code để chuẩn bị làm thì thấy bốn chỗ cần nói rõ hơn — chi tiết ở phần "Bổ sung 06/11" trong
thư gửi họ:

1. 🔴 **`ui_action` "QC chưa sẵn" phát KÈM `impression`, không phát THAY.** Phát thay thì
   `ui_action = 'impression'` **tụt** đúng bằng tỉ lệ QC-chưa-sẵn, CTR của chính bề mặt đó **tăng**
   bằng đúng tỉ lệ ấy — tức **một era** trên event còn chưa ký, mà không ai cố ý đổi gì. Phát kèm thì
   nó là **tập con**: `impression` giữ nghĩa, CTR vẫn `click / impression`, và luật đọc là **đừng
   cộng ngang `ui_action`** (vốn đã đúng). Và cách này đúng mô hình ban đầu của họ.
2. 🔴 **`level` trong khoá dùng tên `current_level`, nguồn `MaxPassedLevel`, chụp lúc `Record`.** Tức
   nó là **TIẾN ĐỘ**, không phải *"màn đang chơi"* — repo đã có ghi chú đúng chuyện này cho nhãn IAP.
   Chụp lúc flush là ra tiến độ lúc pause, sai thời điểm.
3. ⚠ **Đo phần mất: đo được "số phiên có flush bị mất", KHÔNG đo "mất bao nhiêu lần hiện".** Muốn con
   số sau thì phải lưu chính dữ liệu cụm — đúng thứ vừa chọn không làm. Và cái dấu lúc mở app là
   **bản tin mới** ⇒ ký chung đợt 9.
4. ⚠ **`Booster.Register` chỉ độc lập một nửa** (bên mình nói hơi rộng, đã thu lại): kiểm khoá
   `boosters_used` thì thuần client, ship lúc nào cũng được; còn kiểm **dòng tài nguyên** của booster
   thì SDK **không biết dòng nào là booster** cho tới khi có `currency_class = 'booster'` ⇒ đi cùng
   `DeclareCurrency`.
