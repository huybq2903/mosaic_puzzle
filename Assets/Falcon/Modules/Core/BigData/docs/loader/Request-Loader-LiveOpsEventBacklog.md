# Việc phía kho, đọng lại từ bốn lượt soát event live-op

> Viết 06/11/2026, từ SDK (BigData) sang phía kho.
> Nguồn: bốn doc cầm tay `docs/games/Request-Game-{TeamArena,BoatRush,LadderStreak,TeamQuest}.md`.
> **Dev client không liên quan tới bất cứ mục nào trong file này.**

## 0. Ba nhóm, và nhóm nào cần code

| Nhóm | Là gì | Cần sửa loader? | Chặn gì |
|---|---|---|---|
| **A** — bốn câu TRA | `SELECT` trên silver, read-only | 🟢 **KHÔNG** | ✅ **đã tra xong** — kết quả ở §A, doc Quest đã sửa theo |
| **A5** — một câu tra nữa, tầm thường | `SELECT` trên silver, read-only | 🟢 **KHÔNG** | 🟢 **không chặn gì** |
| **A6** — kiểm kê key cho **ba event còn lại** | `SELECT` trên silver, read-only | 🟢 **KHÔNG** | ⚠ chặn việc tin ba doc kia |
| **B** — hai việc sửa thật | một era + (tuỳ chọn) một cột | ✅ có | **B1** chặn đọc đúng số Card Collection; B2 không chặn gì |
| **C** — luật đọc phải GHI LẠI | không code, chỉ ghi vào catalog | 🟢 không | không chặn, nhưng quên là đọc sai im lặng |

➡ **Trả lời câu "4 câu này cần loader xử lý à?": không.** Cả bốn là tra dữ liệu đang có, không
dòng code nào. Nhưng chúng **chặn** việc chốt doc Quest, nên nằm đầu file.

✅ **Cập nhật: A1–A4 đã tra xong.** Ba trong bốn câu ra kết quả **khác** doc, và doc Quest đã sửa
theo dữ liệu. Kết quả ghi dưới từng câu.

🔴 **Nhưng kết luận lớn nhất của lượt tra không phải sáu key.** Cộng cả bốn kết quả lại thì thấy
**bản thử Quest không theo hợp đồng**: không trục tính năng, không `live_ops_round_id`, không dòng
tài nguyên, mã team là một Int một giá trị, và một bước `milestone` đang chở *"vừa cộng map"*. Nó
trả lời được **gần như không câu nào** của spec.

➡ Nên doc Quest **thôi lấy bản thử làm chuẩn**: bản thử là **bằng chứng về game**, không phải chuẩn
để giữ. Hệ quả cho phía kho nhỏ hơn hẳn so với bản trước của file này: **A5 thôi là hạng mục chặn**,
và phần việc còn lại chỉ là một luật đọc (**C10**).

⚠ **Một giả định cần ai biết thì chặn:** chưa có sheet/chart nào đọc bản thử Quest. Nếu có thì nói
ngay — lúc đó mới cần era thật cho nó.

---

## A. Bốn câu TRA — ✅ ĐÃ XONG, và ba trong bốn làm doc phải sửa

Bản gốc của bên dữ liệu đã tự ghi việc này (`quest_log.md` §3.1 câu 2: *"Kho — tự làm"*), nhưng
chưa làm. Event Team Quest **đã có bản thử trong kho**, nên doc cầm tay đang ghi vị trí key theo
**phỏng đoán** ở ba chỗ.

🔴 **Luật quyết định cho cả bốn câu:** tra ra khác doc thì **SỬA DOC, KHÔNG SỬA APP** — luật
*"không xoá key đang có"* thắng.

### A1. Sáu key `team_quest_*` đang nằm trên log nào, bước nào, kiểu gì

Sáu key: `team_quest_team_code` · `team_quest_team_score` · `team_quest_my_maps` ·
`team_quest_maps` · `team_quest_difficulty` · `team_quest_grant_source`.

Cần biết: với mỗi key → `event` nào, `sub_event` nào, kiểu String hay Int, và nằm ở
`event_extra_props` của stream nào.

✅ **Kết quả: khác doc ở hai chỗ.** Cả sáu key nằm trong `event_extra_props` của `f_sdk_live_ops`;
**không** key nào nằm trên dòng tài nguyên, và bước `eligible` không mang key nào.

| Key | Kiểu | Bước |
|---|---|---|
| `team_quest_team_code` | **Int**, và nhận **đúng một giá trị** trên mọi dòng ⚠ | `open`, `join`, `milestone`, `claim` |
| `team_quest_team_score` | Int | `open`, `join`, `milestone` |
| `team_quest_my_maps` | Int | `open`, `join`, `milestone` |
| `team_quest_maps` | Int | `milestone` |
| `team_quest_difficulty` | **Int** — 🔴 bản mới đổi sang String, xem dưới | `milestone` |
| *(không có)* `team_quest_popup` | — | bản cũ **chưa** gửi loại popup |
| `team_quest_grant_source` | String | `claim` |

➡ Doc Quest giờ **viết trọn thiết kế đúng** (bảng key ở §5), không viết dưới dạng bản vá lên bản
log hiện tại; phần *"bản log hiện tại phải bỏ gì"* gom vào **§11** của doc đó. Riêng
`team_quest_grant_source` ở lại trên bước `claim` — đó là một trong hai chỗ bản log hiện tại làm
đúng.

🔴 **`team_quest_difficulty` sẽ ĐỔI KIỂU: Int → String**, mang **đúng tên độ khó của game**
(`normal` / `hard` / `super_hard`, cùng vốn từ với log level `f_sdk_level_pass` mà Q16 đang đọc).
ℹ️ Và String là kiểu **bản gốc bên dữ liệu đã khai** cho field E5 ngay từ đầu — bản log cũ mới là
chỗ lệch. Luật đọc ở **C10**.

⚠ **`team_quest_team_code` là Int và chỉ có một giá trị** ⇒ không phải mã team: giá trị giữ chỗ, hay
chỉ số slot UI. Doc Quest **bỏ hẳn** key này — giữ một key *"trông như mã team mà không phải"* còn
tệ hơn bỏ, vì người đọc sau sẽ `GROUP BY` nó và thấy cả game chỉ có **một** team.

🔴 Hệ quả: **mã team chỉ nằm ở `feature_instance_id`**, không có đường đối chiếu thứ hai. Dev đóng
dấu sai team thì không ai phát hiện được.

### A2. `team_quest_team_score` nằm trên bước `join` hay trên dòng Treasure Map?

🔴 **Đây là câu đắt nhất trong bốn câu.**

✅ **Kết quả: nằm trên `join`** (và trên `open`, `milestone`), với giá trị cỡ "điểm team lúc đó".

⚠ **Doc Quest từng bỏ key mới `team_quest_team_score_at_join` để dùng key cũ. Nay RÚT LẠI.** Hai
cái **không cùng nghĩa**: một là *"điểm team lúc gửi bản tin"*, một là *"điểm team lúc user vào
team"*. Chúng trùng nhau trên đúng bước `join` và **lệch nhau trên dòng ghi bù** — chỗ duy nhất
quan trọng. Dùng chung một tên thì một dòng ghi bù điền đúng và một dòng điền sai **trông y như
nhau**, không ai soát được.

➡ Key cũ giữ nghĩa *"ảnh chụp lúc gửi"* ở `open` và ở dòng map. Key mới mang đại lượng *"lúc vào
team"*. Hai key, hai đại lượng — không phải hai nguồn cho một sự thật.

🔴 **Nhưng một luật đọc phải ghi kèm: key này KHÁC NGHĨA theo bước.**

| Bước | Nghĩa |
|---|---|
| `join` | điểm team **lúc user vào team** — field chịu lực của toàn bộ luật "rương mất vì join muộn" |
| `open` | điểm team **lúc mở popup** — một ảnh chụp, không phải mốc |
| `milestone` | điểm **vừa góp** của một trận — bước này là *"vừa cộng map"*, xem **A5** |

Và một việc dev phải xác nhận, không phải việc kho: trên `join` thì *"điểm lúc này"* và *"điểm lúc
join"* **trùng thời điểm**, nên bản thử có thể đang điền từ cache mà vẫn ra đúng số. Trên **hàm ghi
bù** cho người đã ở team từ trước thì không — ở đó bắt buộc là `ScoreAtUserJoin` của server. Doc đã
ghi thành 🔴.

### A3. Bản thử đã có dòng Treasure Map chưa?

Tra: có dòng `f_sdk_resource_source` nào `currency = 'team_quest_map'` không.

✅ **Kết quả: CHƯA CÓ dòng nào.** Không `f_sdk_resource_source/sink` nào mang
`currency = 'team_quest_map'`, cũng không dòng tài nguyên nào mang `live_ops_event = 'team_quest'`.
Dòng dạng "quest" duy nhất của game thuộc daily quest, không liên quan.

➡ Doc Quest đổi từ *"SỬA dòng đang có"* sang **"viết dòng mới"**.

🔴 **Nhưng nguy cơ đếm đôi không mất, nó dời chỗ** — và doc đã xử lý: bước `milestone` hiện tại
đang là *"vừa cộng map"* (**A5**), nên dòng tài nguyên mới **THAY** nó, không đứng cạnh nó. Để cả
hai là một lần cộng map ra **hai** bản tin.

🔴 Đoán sai hướng *"chưa có"* là dev viết dòng mới bên cạnh dòng cũ ⇒ kho thấy **gấp đôi số map**,
và mọi câu về điểm cá nhân / điểm team sai gấp đôi.

### A4. Bản thử có gửi `f_sdk_ui_impression` nào của Team Quest không?

Doc Quest đang ghi *"Team Quest **không** có impression"* và đặt một mục nghiệm thu
*"**0** dòng `f_sdk_ui_impression` mang `live_ops_event = team_quest`"*.

✅ **Kết quả: CÓ gửi**, hai `surface_id`: `team_quest_main` và `team_quest_join`.

➡ Mục nghiệm thu đó **SAI**. Doc Quest đã sửa **ba** chỗ: §0 câu 5 (*"Quest không có impression"* →
*"giữ hai bề mặt đang gửi, đừng thêm bề mặt mới"*), §6, và mục nghiệm thu ở §10. Luật *"không xoá
thứ đang có"* thắng.

🔴 **Luật đọc đi kèm:** hai bề mặt này đọc bằng `sum(count)`, **không** `count(*)` — theo hợp đồng
cụm impression, như mọi bề mặt khác. Cùng một chỗ dễ sai mà B1 nói tới ở nhóm impression.

ℹ️ Đúng bốn câu tra kiểu này đã **đóng hai hạng mục chặn ship** của Ladder Streak lượt trước (mã
đợt, và `live_ops_stage` là `milestoneId` hay số chuỗi). Nên chúng rẻ và đáng làm trước.

### 🟢 A5. Danh sách bản app của bản thử Quest — để rào khi đọc

Bước `milestone` của bản thử đang mang nghĩa *"vừa cộng map"*, không phải *"team đạt rương"*. Ba
dấu hiệu: nó mang `team_quest_difficulty` (một mốc của team không có độ khó), `team_quest_maps` = 1
(giống *"vừa được 1 map"*), và `team_quest_team_score` **một chữ số** (không ngưỡng rương nào là một
chữ số).

🔴 **Doc Quest đã xử lý ở phía GỬI**: bản mới **bỏ** bước đó, map chuyển sang dòng tài nguyên. Nên
trong **một bản app**, `milestone` chỉ có **một** nghĩa.

➡ **Việc của kho, duy nhất:** lấy **danh sách `app_version` của bản thử**, để khi đọc `milestone` của
Team Quest thì **loại** chúng ra (**C10**).

ℹ️ **Sửa một câu sai của bản trước file này.** Nó ghi A5 là hạng mục **chặn ship**, với lý do
*"không cột nào tách hai nghĩa ra được"*. **Sai** — `app_version` tách được, đúng như cách đã dùng
cho `milestone` của Ladder. Điều kiện duy nhất là bản mới không gửi cả hai nghĩa, và doc Quest đã
ghi thành 🔴. Không cần era, không cần ba câu tra chéo như bản trước đề nghị.

---

### ⚠ A6. Kiểm kê key cho Ladder Streak và Boat Rush — đúng câu A1, đổi tiền tố

🔴 **Lý do có mục này:** A1 là câu tra đã lộ ra **cả năm** vấn đề của Team Quest. Và nó **chỉ được
chạy cho Team Quest**. Ba doc kia viết mà chưa ai kiểm kê key của bản log đang chạy.

Mức rủi ro **không bằng nhau**, nên đừng làm cả ba:

| Event | Có bản log đang chạy? | Rủi ro trùng key | Cần A6? |
|---|---|---|---|
| **Ladder Streak** | ✅ có, và **theo hợp đồng** — mã đợt trên 100% dòng, `live_ops_stage` đúng nghĩa mốc, round cố ý rỗng, sáu bước chuẩn, impression qua API cụm, không đóng dấu dòng level | ⚠ **thật**: doc thêm ~10 key `ladder_streak_*` mới, trong khi sáu bước đang chạy đã mang key gì thì **chưa ai biết** | ✅ **có** |
| **Boat Rush** | ⚠ chưa rõ ngoài một dòng grant | ⚠ doc thêm ~14 key `boat_rush_*` | ✅ **có** |
| **Team Arena** | 🟢 **không có** — việc mới hoàn toàn, và tính năng Team phải ship trước | 🟢 không | 🟢 **không cần** |

**Tra gì:** đúng câu A1, chỉ đổi tiền tố — mỗi key `ladder_streak_*` / `boat_rush_*` đang gửi →
`sub_event` nào, kiểu String hay Int, nằm ở stream nào.

| Tra ra | Hệ quả |
|---|---|
| không key nào của doc trùng tên key đang gửi | 🟢 xong, không đổi gì |
| trùng tên mà **khác nghĩa** | 🔴 đúng ca `team_quest_team_score`: đổi tên key mới, **đừng** dùng lại |
| trùng tên mà **khác kiểu** | 🔴 đúng ca `team_quest_difficulty`: chốt một kiểu, và rào `app_version` |

### 🔴 A7. Dòng grant 30 phút mạng vô hạn của Boat Rush: `item_type` / `item_id` là gì?

Doc Boat Rush §4.1 bảo dev **gắn nhãn lên dòng grant đang có** thay vì viết dòng mới — lý do *một
món = một đường grant* là đúng, viết dòng mới thì kho thấy **60 phút** thay vì 30.

⚠ Nhưng doc còn viết *"dòng sẵn có thì các field đã có giá trị, không Unknown"* — **câu đó là suy,
không kiểm**, và đã sửa thành một bảng hai nhánh. Năm field tài nguyên mặc định là chuỗi
`"Unknown"`, không phải null.

**Tra:** dòng grant mạng vô hạn 30 phút đang có — `currency`, `item_type`, `item_id`, `amount`.

| Tra ra | Doc ghi gì cho dev |
|---|---|
| có giá trị thật | 🟢 chỉ gắn nhãn, không chạm bốn field |
| `item_type` / `item_id` = `"Unknown"` | 🔴 **điền** chúng cùng lúc gắn nhãn |
| **không có dòng nào** | 🔴 viết dòng mới, điền **cả năm** field |

ℹ️ Đã kiểm phía SDK: module `Lives` trong repo **không ghi dòng tài nguyên nào**, nên dòng này (nếu
có) là **code phía game**.

---

## B. Hai việc sửa thật

### 🔴 B1. Ghi một ERA cho `card_collection` — việc đi KÈM bản SDK sắp phát hành

Bản SDK này **đổi dữ liệu wire của một event đang chạy thật**. Khác các lượt docs-only, lượt này
cần kho ghi mốc.

**Nguyên nhân:** `LiveOps.Stamp` thôi tự điền `liveOpsRoundId` bằng *"lượt cuối SDK tình cờ thấy
trên một bước nào đó"* — một con số **suy từ lịch sử log**, không phải lời khai về dòng đang gửi.
Cả repo chỉ **Card Collection** từng ghi mã lượt vào sổ (`phase_{n}`), nên nó là event duy nhất bị
ảnh hưởng.

**Đổi ở HAI nhóm, không phải một:**

| Nhóm | Đổi gì | Đọc số đổi theo |
|---|---|---|
| **3 dòng tài nguyên** (starter pack, chest reward, chest exchange) | từ nay **VẮNG** `live_ops_round_id` | Trước đó giá trị ở đó chỉ đúng do may: người chơi sang phase 3 mà chưa xong set nào thì sổ còn giữ `phase_2`, nên dòng starter pack có `detail.phase = 3` mà `live_ops_round_id = phase_2` — **hai cột trên một dòng nói ngược nhau**. Thiếu hơn bịa |
| **2 bề mặt impression** | cụm gộp về **một cụm `round = null`** | 🔴 **ĐỔI GRAIN**: **ít dòng hơn, `count` lớn hơn, `sum(count)` KHÔNG đổi**. Chart nào đang đếm `count(*)` dòng impression sẽ **tụt** — mà đếm `count(*)` vốn đã sai theo hợp đồng cụm |

**Cần:** một mục era trong `event-catalog` theo khuôn §317.

⚠ **Số bản app / SDK: chưa có, và SDK không tự đặt.** Số bản BigData do bên mình chốt lúc phát
hành, chưa chốt; còn số bản **app** thì phải chờ game build kèm bản SDK đó, nên không ai biết trước.

➡ Nên viết mục era với biên là **"từ bản app đầu tiên mang BigData `<x.y.z>`"**, điền số khi phát
hành. Và 🔴 **đừng tin con số đó làm biên thật** — biên đọc được **từ dữ liệu**, chắc chắn hơn bất
kỳ số bản nào:

| Biên | Tra bằng |
|---|---|
| 3 dòng tài nguyên | ngày đầu tiên dòng starter pack / chest reward / chest exchange của `card_collection` **vắng** `live_ops_round_id` |
| 2 bề mặt impression | ngày đầu tiên một `(user, đợt, bề mặt)` chỉ còn **một** cụm `round = null` thay vì nhiều cụm `phase_n` |

Hai biên này **có thể lệch nhau vài ngày** vì người chơi cập nhật app rải rác — đó là chuyện bình
thường của mọi era, không phải lỗi.

**Mất khả năng gì:** phân rã impression theo phase từ cột `live_ops_round_id`. Phép phân rã cũ vốn
không đáng tin — biên tách là *"phase của bước cuối đã log"*, không phải phase lúc bề mặt hiện ra.

### ⚙ B2. Cấp cột cho `feature_effect` — **tuỳ chọn**, không chặn gì

**Vì sao đáng làm:** sau hợp đồng Team, *"vào team"* và *"rời team"* **chỉ** đọc được bằng
`feature_effect`, không đọc được bằng tên bước:

| Lọc theo tên bước | Mất gì |
|---|---|
| `sub_event = 'join'` | **`create`** (lập team — hợp đồng ghi *"`create` là `enter`"*) và **`join_accepted`** (team Private, **server gửi hộ**) |
| `sub_event = 'leave'` | **bị kick** và **`guild_disbanded`**, cả hai **server gửi hộ** |

Mà *"ai rời team trong 3 ngày sau sự kiện"* và *"số người vào / rời của team"* nhắm **đúng vào
người bị kick**. Nên mọi query của Team Quest và Team Arena về vào/rời đều phải trích
`feature_effect` từ `event_extra_props`.

**Hiện trạng:** hợp đồng phía kho (`docs_v2/design/feature-contract.md` §7) để
`feature_effect` / `feature_variant` / `feature_joined_ts` / `feature_unlocked_ts` **ở lại
`event_extra_props`**, và tự ghi: *"Cấp cột cho `feature_variant` / `feature_effect` về sau ⇒ **đếm
lại key trùng tên trước**."*

➡ **Không chặn gì** — `jsonb_extract_string(event_extra_props, '$.feature_effect')` chạy được ngay.
Chỉ là nếu hai event này thành nguồn query thường xuyên thì cột rẻ hơn. **Điều kiện tiên quyết là
đếm key trùng tên**, như hợp đồng đã dặn.

---

## C. Luật đọc phải GHI LẠI — không code, nhưng quên là đọc sai IM LẶNG

Mỗi mục dưới đây là một luật mà **người viết query sau sẽ không tự nhìn ra**, và sai thì không có
gì báo. Cần ghi vào catalog cạnh event tương ứng.

### 🔴 C1. Ba "tiền tệ giả" phải loại khỏi mọi chart kinh tế — **một luật dùng chung**

| Event | `currency` |
|---|---|
| Ladder Streak | `ladder_streak_count` |
| Team Quest | `team_quest_map` |
| Team Arena | `team_arena_flag` |

Ba event độc lập cùng chọn **dòng tài nguyên** để chở một đại lượng **không phải tài nguyên**, vì
hình dạng của nó đúng: có lên, có xuống, có `value_before`/`value_after`, có lý do. Cái giá là
chúng ngồi cùng bảng với coin và booster.

➡ Ai vẽ *"tổng tài nguyên phát ra hôm nay"* mà không loại ba cái này sẽ thấy **hàng triệu đơn vị
"tiền" không tồn tại**.

🔴 **Ghi thành MỘT luật**, không ba ghi chú rời — vì event thứ tư sẽ lại làm thế, và một luật thì
người ta tìm thấy.

✅ **Chốt: đặt luật này ở mục `f_sdk_resource_*` của catalog** (đúng như phía kho đề nghị), và
C2–C9 ghi cạnh từng event. Luật này **thuộc về cái bảng**, không thuộc về event nào — đặt nó dưới
một event là đảm bảo hai event kia bị bỏ sót.

### 🔴 C2. Ladder Streak: `milestone` nhân đôi — chống trùng Ở PHÍA ĐỌC

Van lọc chống trùng của SDK là **sổ theo máy**. Ba đường dẫn tới "sổ rỗng mà mốc đã đạt", và chỉ
một đường là multi-device:

| Đường | Tần suất |
|---|---|
| máy thứ hai | ít |
| **cài lại app / xoá data** | thường xuyên |
| **lên bản app lần đầu có log**, lúc đang giữa đợt | mỗi user một lần |

**Luật đọc:**

| Câu | Đọc đúng |
|---|---|
| % user đạt rương n | ✅ **EXISTS** — trùng không ảnh hưởng |
| % nhận quà sau khi đạt | ✅ EXISTS |
| **thời gian `milestone` → `claim`** | 🔴 **`min(created_date)`** theo (user, đợt, stage) |

🔴 **`min(created_date)` là đáp án đúng, không phải tạm.** `created_date` là giờ **TẠO** log, không
phải giờ tới server — máy nào **chứng kiến** thì tạo log **trước**, nên dòng sớm nhất luôn là dòng
thật, kể cả khi nó về kho muộn hơn.

**Hai lựa chọn đã cân và ĐÃ LOẠI (owner 06/11) — đừng mở lại:**

| Lựa chọn | Vì sao loại |
|---|---|
| Thêm key `ladder_streak_log_via` = `live`/`sync` | Nó **không SỬA** cái trùng, chỉ **DÁN NHÃN**. Kho vẫn hai dòng, vẫn cần một luật chọn dòng. Mua đúng một bit (*"chưa máy nào từng chứng kiến"*) mà **`app_version` đã cho rồi** |
| Để **server** gửi bước `milestone` | Cách duy nhất chữa tận gốc, nhưng giá là một đường gửi log phía server cho riêng event này, mua **một** câu hỏi mà `min(created_date)` đã trả lời đúng và miễn phí |

➡ **Chống trùng nằm ở phía đọc. Client không thêm gì, server không thêm gì.**

### 🔴 C3. BỎ phép lọc `level_attempts_since_enter = 0` — áp cho cả Ladder và Quest

Cả hai bản gốc đề xuất dùng bộ đếm này để loại dòng ghi bù. **Không tin được:**

- bộ đếm là *"từ lúc vào **ĐỢT**"* trên **máy đang gửi** (không phải từ lúc vào round);
- máy vừa cài có thể đã chơi vài ván trước khi data về ⇒ **đếm > 0 mà vẫn là** dòng ghi bù;
- máy thật có thể bắn mốc ngay ván đầu ⇒ **đếm = 0 mà là** dòng thật.

➡ Dùng `min(created_date)` + `app_version`.

### 🔴 C4. Team Quest: Σ `amount` của Treasure Map là số CỦA MỘT MÁY

Doc **cố ý** không ghi dòng map từ fetch/đồng bộ — và đó là **quyết định đúng** cho loại dòng này
(map là một lần cộng thật, không phải con số trạng thái; ghi từ sync là ghi đôi). Hệ quả: **map
kiếm ở máy khác không bao giờ vào kho.**

| Câu | Ảnh hưởng |
|---|---|
| điểm cá nhân theo phân vị | 🔴 **thiếu** với user nhiều máy |
| độ tập trung đóng góp (top 1 / top 3) | 🔴 thiếu theo |
| nhịp điểm team theo giờ | 🔴 thiếu theo |
| team có active không | ✅ EXISTS |
| giờ đóng góp đầu tiên | ✅ `min` |
| độ khó trận thắng | ✅ |

➡ **Số chính thức lấy từ bảng server "Lần ở team"** (cột `map`). Dòng resource dùng cho **thời
điểm** và **độ khó** — hai thứ server không có.

ℹ️ Bản gốc đã tự biết một nửa: phép kiểm chất lượng của nó viết *"**trên từng máy**, trong từng
round, Σ `amount` = `max(value_after)`"*. Đúng — nhưng rồi điểm cá nhân vẫn đọc Σ `amount` như số
tuyệt đối.

ℹ️ **Khác Ladder Streak**: ở đó dòng chuỗi **có** ghi từ đồng bộ (`via = sync`) nên số là toàn cục.
Hai lựa chọn khác nhau, **cả hai đều đúng** cho loại dòng của mình — nhưng phải biết mình đang đọc
loại nào.

### ⚠ C5. Team Quest: `milestone` gom theo CỘT TEAM, đừng cắt chuỗi `round_id`

`milestone` là sự thật của **team** nhưng **mỗi thành viên tự bắn một dòng**, và mỗi người có
`live_ops_round_id` **khác nhau** (giờ vào team khác nhau). Team 20 người ⇒ tới **20 dòng** cùng
một rương.

| Câu | Đọc đúng |
|---|---|
| % team đạt rương n | **EXISTS** theo (đợt, team) |
| giờ team đạt rương n | `min(created_date)` theo (đợt, team) − `cycle_start_ts` |

⚠ `min()` ở đây là *"thành viên **online** đầu tiên nhận ra"*. Ai lúc đó offline sẽ bắn muộn hàng
giờ. Với phân vị thì được; đừng dùng làm mốc chính xác.

ℹ️ Tiền tố team trong `round_id` **không cần cắt ra** — `feature_instance_id` là cột thật. Giữ tiền
tố đó làm **phép kiểm miễn phí**: nó phải khớp `feature_instance_id`, lệch là biết có chỗ sai.

### ⚠ C6. Mẫu số "theo ngày" KHÔNG đặt được lên bước bị nén — chung cả bốn event

Van lọc nén `eligible` và `open` còn **một dòng mỗi đợt**. Nên mọi câu *"tỷ lệ X theo ngày"* với
mẫu số là `eligible` hay `open` đều **không ra số theo ngày**.

**Thay bằng:** *active hôm đó* (bảng user daily) × *đã qua level mở event* (`max_passed_level`) ×
*hôm đó nằm trong* `[cycle_start_ts, cycle_end_ts]`.

🔴 **Và một thứ KHÔNG suy lại được:** `open` chỉ nói *"đã thấy UI đâu đó trong đợt"*. **Ngày nào**
thì thông tin đó **chưa bao giờ rời khỏi máy** — không khâu xử lý nào lấy lại được.

⚠ **Đừng hardcode độ dài đợt.** Nó là config và **đã từng đổi** (Ladder Streak đổi giữa đời).
Đọc `cycle_start_ts` / `cycle_end_ts` của **chính dòng đó**.

### ⚠ C7. Team Quest: bậc "thấy popup Join" đọc bằng vế HOẶC

`open` là một dòng mỗi đợt và `team_quest_popup` đi theo **lần mở đầu tiên**, nên ai mở popup chính
trước thì dòng duy nhất của họ mang `main`.

🟢 **Đường chính là IMPRESSION của bề mặt `team_quest_join`**, không phải bước `open`. Cụm
impression mang `count` và **không** bị nén còn-một-dòng-đầu-tiên như bước, nên nó trả lời đúng
*"trong đợt này có thấy popup Join không"*. Đây là việc thật của bề mặt đó, không phải bề mặt cho
đẹp.

➡ Và giữ vế HOẶC của bản gốc làm đường **đối soát**: *có `open` với `popup = join_team` HOẶC đã có
`join`*.

⚠ Đọc bằng `sum(count)`, **không** `count(*)` — như mọi cụm impression (B1 nói cùng chỗ sai này).

### ⚠ C8. Boat Rush: `milestone` cũng nhân đôi xuyên máy

Cùng cơ chế C2. Luật đọc:

| Câu | An toàn |
|---|---|
| chặng cao nhất | ✅ `max(live_ops_stage)` |
| race thắng | ✅ EXISTS |
| số lần thử | ✅ đếm `join`, không đếm `milestone` |
| **phân bố hạng** | ⚠ hai dòng (một `live` có hạng, một `sync` không) ⇒ **ưu tiên `log_via = 'live'`** |

➡ Và mục nghiệm thu *"không ghi lặp"* của **mọi** event phải đọc là *"không ghi lặp **trên cùng một
máy, trong cùng một lần cài**"*.

### ⚠ C9. `feature_instance_id = ''` KHÁC `NULL`

| Giá trị | Nghĩa |
|---|---|
| **`''`** rỗng | *"đã mở khoá, **không ở** thực thể nào"* — chỉ bản tin **`state`** được khai |
| **vắng / NULL** | *"dòng này **không do** tính năng đó gây ra"* |

🔴 Đọc lẫn hai cái là **đếm sai người không có team** — mà đó đúng là câu *"% user unlock nhưng
không có team"*.

➡ Và nếu thấy `''` trên một bản tin **khác** `state` thì đó là **lỗi phía gửi**, không phải dữ liệu
thật — DQ nên bắt.

### ⚠ C10. Team Quest: loại dòng của BẢN THỬ khi đọc, rào bằng `app_version`

Bản thử Quest không theo hợp đồng (§0). Hai chỗ phải rào:

🔴 **Luật một câu: loại các `app_version` của bản log cũ ra khỏi MỌI câu đọc Team Quest** — không
chỉ lúc đọc `milestone`.

| Vì sao rộng thế | |
|---|---|
| `milestone` | ở bản cũ nghĩa là *"vừa cộng map"*, không phải *"đạt rương"* (A5) |
| mọi câu *"theo team"* | dòng cũ **không có** `feature_instance_id` ⇒ lôi vào một mớ dòng team rỗng |
| mọi câu *"rời rồi vào lại"* | dòng cũ **không có** `live_ops_round_id` ⇒ không phân biệt được lần ở team |
| `team_quest_difficulty` | kiểu **đổi** từ số sang chuỗi: dòng cũ mang số, dòng mới mang tên độ khó của game (`normal` / `hard` / `super_hard`) |

🟢 **Không cần era riêng cho hai chuyện này** nếu đã rào bằng bản app: dữ liệu bản thử ít, một game,
cửa sổ ngắn, và **không có chart nào đọc nó** — nếu có thì nói ngay.

⚠ Và đừng trông chờ bản thử trả lời câu nào: không mã team, không round, không dòng tài nguyên. Mọi
câu *"theo team"*, *"điểm cá nhân"*, *"rời rồi vào lại"* đều **chỉ có từ bản mới trở đi**.

---

## D. Đã đóng — ghi lại để không ai mở lại

| Câu từng mở | Kết quả |
|---|---|
| Ladder: server thêm `cycleId` vào config được không? (bản gốc đánh **chặn ship**) | ✅ **Đóng** — mã đợt đã có trên 100% dòng, chung mọi user |
| Ladder: `live_ops_stage` là `milestoneId` hay số chuỗi? | ✅ **Đóng — `milestoneId` 1…10**, liên tục. Không cần era, không đổi key |
| Ladder: coi mỗi lần leo lại là một `round` không? | ✅ **KHÔNG.** Mốc rương là **lũy kế theo đợt**; thêm lượt vào là cho van lọc ghi lại **cả 10 rương mỗi lần đứt chuỗi**. Các lần leo suy được từ chuỗi dòng tài nguyên (`value_before`/`value_after` + `via`) |
| Ladder: dùng level map chính hay bộ level riêng? | ✅ **Map chính** ⇒ dòng level **không** được đóng dấu, và cặp `live_ops_level_*` **vắng là ĐÚNG** |
| SDK: truyền `roundId` và mã đợt cũ cho dòng nhận muộn thế nào? (Quest §3.1 câu 1) | ✅ **Đóng 06/11** — game truyền thẳng vào `OnStep` / `Stamp`; **im lặng = không có lượt**. SDK thôi tra hộ |
| Quest: mốc thời gian trong `round_id` có đồng nhất giữa hai máy? | ✅ **Có** — server giữ profile theo `account_id`. Giữ thành một mục nghiệm thu phía client |

🔴 **Và một hệ quả đáng ghi của mục cuối:** khi `round_id` **giống nhau** giữa hai máy, dòng trùng
xuyên máy là **nhiễu vô hại** (mọi phép đọc là EXISTS hoặc `min()`). Khi nó **khác nhau**, cùng
những dòng đó đếm ra **một lần "đổi team" không tồn tại** mỗi lần người chơi mở máy khác. Một con
số quyết cả hai kịch bản — nên mục nghiệm thu đó không phải hình thức.

---

## E. Thứ tự đề nghị

1. ~~**A1–A4**~~ — ✅ xong. Ba trong bốn làm doc Quest phải sửa; đã sửa.
2. 🟢 **A5** — một câu tra tầm thường (danh sách bản app của bản log cũ). **Không chặn ship**; bản
   trước của file này ghi sai chỗ này.
3. ⚠ **A6 + A7** — kiểm kê key cho Ladder / Boat Rush, và dòng grant mạng của Boat Rush. Cùng một
   câu tra đã lộ ra cả năm vấn đề của Quest, và nó chưa chạy cho ba event kia.
3. **B1** — era `card_collection`, đi kèm bản SDK sắp phát hành. Số bản điền sau, **biên đọc từ dữ
   liệu**.
4. **C1, C2, C9** — ba luật dễ sai nhất và sai **im lặng**.
5. **C3–C8** — ghi vào catalog cạnh từng event.
6. **B2** — cột `feature_effect`, khi nào hai event Team thành nguồn query thường xuyên.
