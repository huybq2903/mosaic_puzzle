# SDK BigData → loader: gộp cụm Ui thành một dòng, tách `flush_lost`, hồi sinh chỉ ghi lúc đã dùng (09/11/2026)

> Gửi: loader. Từ: owner SDK BigData.
> Thư này **rút lại một phần hợp đồng các bạn đã ký 06/11** cho `f_sdk_ui_impression` và `f_sdk_level_action`. Bên mình nói thẳng
> lý do ở từng mục. Chưa code ba mục 1–3, chờ các bạn ký. Riêng việc bỏ `current_level` khỏi khoá cụm Ui thì **đã code, chưa
> publish** (mục 1, nhánh B).

## Tóm tắt

| # | Đổi gì | Vì sao |
|---|---|---|
| 1 | Cụm Ui: **một dòng một cụm**, mỗi hành vi là một **cột đếm** (thay vì một dòng một hành vi). Thêm hành vi `dismiss`. Bỏ `current_level` | Tập hành vi là tập đóng nên cột rẻ ngang giá trị; hết lặp khoá; hết bẫy cộng ngang `ui_action` |
| 2 | `flush_lost` thành **event riêng** | Nó không phải bề mặt Ui: không có `surface_id`, và giá trị `ui_action` của nó quyết định field nào được vắng |
| 3 | Hồi sinh: event riêng, **chỉ ghi lúc đã dùng**. Mời/nhận/từ chối đi vào cụm Ui | Một dòng gói cả lời mời + quyết định + cách trả là gói một QUÁ TRÌNH vào một dòng, có cờ `accepted` điều khiển field khác |
| 4 | Ngừng phát `f_sdk_level_action` (1.7.0) | Thay bằng mục 3 |

Vì sao đổi **bây giờ**: 1.7.0 mới publish 09/11, kho chưa có dòng `f_sdk_level_action` nào (thư `-b` của các bạn: *"era 1.7.0 hôm nay
chưa có dòng nào"*). Đây là lúc đổi rẻ nhất. `f_sdk_ui_impression` thì đã có dữ liệu từ 1.3.9 — xem cách giữ silver nguyên ở mục 1.

---

## 1. Cụm Ui: một dòng một cụm

### Đề xuất của bên mình: event MỚI `f_sdk_ui_exposure`

Bên mình **khuyến nghị đổi tên event**, không bắt buộc. Lý do: thư `-c` vừa cho thấy `sdk_version` **không đủ tin** để phân era —
"1.5.6" đang chỉ hai bản code khác nhau. Hình dạng mới đi bằng **tên mới** thì các bạn nhận ra nó bằng tên, không phải dò version.
`f_sdk_ui_impression` giữ nguyên làm event của dữ liệu cũ; hình dạng cuối của nó là **1.7.0**.

Nếu các bạn muốn **giữ tên** (nhánh B): hình dạng mới vẫn nhận ra được bằng **sự có mặt của khoá** `impression_count`, không cần dò
version. Ở nhánh này, việc bỏ `current_level` (đã code) là một era riêng của `f_sdk_ui_impression`.

### Hình dạng

**Khoá cụm** (giữ nguyên trừ việc bỏ tiến độ): `surface_id` × `trigger` × 4 field `live_ops_*` × 3 field `feature*`.

| Field | Kiểu | Nghĩa |
|---|---|---|
| `surface_id` | string | Bề mặt Ui. **Luôn có** (dòng không có bề mặt đã tách sang mục 2) |
| `trigger` · `live_ops_*` · `feature*` | | Như 1.7.0, trong khoá |
| `impression_count` | int ≥ 0 | Số lần hiện |
| `click_count` | int ≥ 0 | Số lần bấm hành động chính |
| `dismiss_count` | int ≥ 0 | **MỚI** — số lần đóng mà **không** làm hành động chính |
| `ad_not_ready_count` | int ≥ 0 | Số lần hiện mà QC chưa sẵn — **tập con** của `impression_count` |
| extras | flatten | Xem luật extras dưới |

**Bốn cột đếm luôn có mặt**, kể cả khi bằng 0: trong một cụm, `0` là số thật của cửa sổ xả đó, không phải "không biết".

**Không còn cột level riêng.** Tiến độ đọc từ `max_passed_level` mà mọi dòng log đều mang (phần đóng dấu chung của SDK). ⚠ Dòng cụm
dựng **lúc xả** (app pause, hoặc khi đủ 32 cụm), nên đó là tiến độ **lúc xả**, có thể cao hơn lúc bề mặt hiện vài màn.

### `dismiss` nghĩa là gì

*Đóng bề mặt mà không làm hành động chính*: bấm X, bấm "Không, cảm ơn", bấm ra ngoài. **Không** gồm: app bị tắt ngang lúc bề mặt đang
mở (cái đó không ai biết — xem mục 2), và chuyển sang bề mặt khác vì hành động chính.

Popup nhiều nút (vd hồi sinh bằng QC / bằng gem): `click` không nói nút nào. Câu đó đọc từ dòng **dùng** của mục 3 (`pay_method`).

### Luật đọc

- **Tỉ lệ chỉ đúng khi cộng qua một khoảng**: CTR = `sum(click_count) / sum(impression_count)`. **Tỉ lệ trên từng dòng là SAI**: lần
  hiện và lần bấm của cùng một người có thể rơi vào **hai cửa sổ xả khác nhau**, nên một dòng có thể có `impression_count = 0` mà
  `click_count > 0`.
- Tỉ lệ từ chối = `sum(dismiss_count) / sum(impression_count)`. `impression − click − dismiss` (cộng qua khoảng) = những lần **không rõ
  kết cục** (app tắt ngang, chuyển màn).
- **Một bất biến bên mình cam kết trên TỪNG dòng**: `ad_not_ready_count ≤ impression_count`. SDK ghi hai số này trong cùng một lời gọi,
  cùng một khoá, nên chúng luôn vào cùng một dòng. Bên mình ghim bằng test lúc code.
- **Extras**: là extras của **lần ghi đầu tiên vào cụm**, bất kể lần đó là hiện, bấm hay đóng. (Trước đây mỗi hành vi một dòng nên
  extras của lần bấm nằm riêng; giờ chúng chung một dòng.) Extras chỉ nên chở ngữ cảnh **bất biến** của bề mặt.

### Giữ silver nguyên được

Nếu bảng silver phía các bạn đang ở dạng một-dòng-một-hành-vi, mapping rule **trải** dòng mới ra bốn dòng (`ui_action` + `count`) là
xong; gold phía sau không phải đổi. Cách nào là quyền của các bạn.

### API phía game

Không đổi chữ ký nào: `Ui.OnImpression` · `Ui.OnClicked` · `Ui.OnImpressionAdNotReady` giữ nguyên. Thêm **`Ui.OnDismissed`** cùng
tham số.

---

## 2. `flush_lost` thành event riêng

Đề xuất tên: **`f_sdk_ui_flush_lost`**. Cơ chế giữ nguyên: lần mở app sau một lần bị tắt ngang mà còn cụm chưa xả ⇒ **một dòng**.

- Không field riêng nào ngoài phần chung của mọi log.
- ⚠ **Ở event này `count(*)` là ĐÚNG** — một dòng là một phiên mất. Ngược với luật *"cấm `count(*)`"* của cụm Ui; xin ghi rõ ở catalog
  để người đọc khỏi mang luật của event kia sang.
- Vẫn là **chặn dưới** (dấu ghi đĩa có thể trượt), như đã ký.

---

## 3. Hồi sinh: chỉ ghi lúc đã dùng

Đề xuất tên: **`f_sdk_level_action_revive`**. Một dòng = **một lần hồi sinh đã xảy ra**.

Bên mình đã soát: luồng level **không** có trạng thái hồi sinh (`LevelStatus` chỉ có Fail · Pass · Skip · Start · HeartBeat), nên
event này không trùng nguồn nào.

| Field | Kiểu | Nghĩa |
|---|---|---|
| `play_turn_id` | string | Lượt chơi đang mở. SDK tự gắn — xem luật tích hợp dưới |
| `progress_pct` | number `[0,100]` | % tiến độ ván lúc hồi sinh. Ngoài khoảng: SDK **bỏ key**, không kẹp |
| `pay_method` | string | `ad` · `currency` · `inventory` · `free` (tập đóng như đã ký). Không biết thì vắng |

**Không còn** `offered` / `accepted`: có dòng là đã dùng. **Không còn** `action` / `booster_id`.

### Luật tích hợp: báo Fail SAU khi hết đường hồi sinh

Đây là luật đã chốt từ 04/09 (*"game chỉ log fail khi hết đường lật kèo — không có hồi sinh cùng lượt"*), bên mình nhắc lại vì
event này dựa vào nó: trong lúc popup hồi sinh đang hiện, lượt chơi **vẫn mở**, nên `play_turn_id` tự gắn đúng; hồi sinh xong mà thắng
thì `Pass` vẫn thuộc **cùng lượt**. Game nào báo `Fail` trước rồi mới mời hồi sinh thì dòng hồi sinh **vắng** `play_turn_id` — SDK
**không** tự đoán id của lượt vừa đóng (luật §C: không bịa id lượt cũ), vì đoán sai là nối hồi sinh vào nhầm ván.

### Mời / nhận / từ chối đọc ở đâu

| Câu hỏi | Nguồn |
|---|---|
| Bao nhiêu lần mời, nhận, từ chối | Cụm Ui (mục 1) trên bề mặt popup hồi sinh |
| Tỉ lệ nhận | `sum(click_count) / sum(impression_count)` trên bề mặt đó |
| Hồi sinh ở đoạn nào của ván | `progress_pct` của dòng mục 3 |
| Từ chối ở đoạn nào | Từ chối ⇒ lượt kết thúc `Fail` ⇒ `level_progress` của **dòng Fail** cùng lượt |
| Tốn bao nhiêu tiền | Hồi sinh bằng tiền tệ sinh dòng tài nguyên `resource_when = 'revive'` (từ đã ký 09/11), nối bằng `play_turn_id` |

Ghi chú: cụm Ui **không** có `play_turn_id`, nên không nối được *từng* lời mời với *từng* lần dùng — chỉ đọc được tỉ lệ cộng qua khoảng.

### Booster: không mở event

Lượt dùng booster đã có ở ba nơi: `pre_boosters_used` (dòng Start), `boosters_used` (dòng Progress/End), dòng tài nguyên
`booster_use`. Bên mình **không** mở event booster. Khi nào cần `progress_pct` của từng lần dùng thì thêm `f_sdk_level_action_booster`
cùng họ tên — `LIKE 'f_sdk_level_action_%'` quét cả họ.

---

## 4. Ngừng phát `f_sdk_level_action`

Từ bản có mục 3, SDK **không phát** `f_sdk_level_action` nữa. API cũ `Level.OnAction(...)` giữ lại (đánh dấu lỗi thời) để code game
đã gọi vẫn build được, và chuyển như sau:

| Lời gọi cũ | Thành |
|---|---|
| `action = revive`, `accepted = true` | một dòng `f_sdk_level_action_revive` |
| `action = revive`, `accepted = false` | không dòng nào (từ chối thuộc cụm Ui) |
| `action = booster` | không dòng nào, kèm cảnh báo cho dev |

⚠ Mất mát đã biết: game nào đang gọi `OnAction(booster…)` trên 1.7.0 sẽ thôi có dòng. 1.7.0 mới publish nên gần như bằng 0, nhưng
không phải 0.

---

## Câu hỏi cho các bạn

1. Ba tên event mới (`f_sdk_ui_exposure` · `f_sdk_ui_flush_lost` · `f_sdk_level_action_revive`) — đồng ý, hay các bạn đặt tên khác?
   Và với mục 1: đổi tên (khuyến nghị) hay giữ tên `f_sdk_ui_impression` (nhánh B)?
2. **Tên bề mặt popup hồi sinh dùng chung mọi game.** `surface_id` là vocab của từng game, nên muốn tính *"tỉ lệ nhận hồi sinh"* chéo
   game thì phải có một tên đăng ký sẵn. Bên mình đề nghị SDK cấp hằng số `revive_offer` (giống cách `ftue` được cấp cho funnel).
   Các bạn có cần không?
3. Mapping: trải dòng mới ra dạng một-dòng-một-hành-vi ở silver, hay đổi silver theo hình dạng mới?

Bên mình gửi **bản + ngày** khi publish.
