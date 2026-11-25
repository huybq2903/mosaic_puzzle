# SDK BigData → loader: FTUE thành entity riêng, họ event `f_sdk_ftue_*` (09/11/2026)

> Gửi: loader. Từ: owner SDK BigData.
> Thư này đề nghị **thay funnel `ftue`** (hợp đồng §D6, chạy trên `f_sdk_funnel_data`) bằng **năm event riêng**. Chưa code, chờ các
> bạn ký. Có một việc nhờ các bạn tra trước (mục 4), vì nó quyết định có phải giữ luật đọc cũ song song hay không.

## Tóm tắt

| Đổi gì | Vì sao |
|---|---|
| FTUE thôi đi qua `f_sdk_funnel_data`, có họ event riêng `f_sdk_ftue_*` | Ba điểm yếu của funnel `ftue` (mục 1) đều nằm ở hình dạng funnel chung, không vá được từ phía game |
| Thêm mốc **từng bước tutorial** và **bỏ qua tutorial** | Hôm nay chỉ biết *bao nhiêu người xong*, không biết *bỏ ở bước nào*; người bấm "skip" bị ghi thành "xong" |
| Chốt nghĩa mốc đầu: *màn hình đầu tiên người chơi tương tác được* | `scene_enter` hiện chỉ ghi *"qua loading/menu"* — game có home hiểu là home, game vào thẳng màn chơi hiểu là màn chơi |
| Hai phép kiểm DQ neo vào funnel `ftue` cần neo lại (mục 3) | 10a đang đếm dòng `ftue` `scene_enter`/`join`; `ftueStep1` đọc profile `last_funnel_step_order_by_funnel` |

Vì sao đổi **bây giờ**: repo SDK không có chỗ nào gọi funnel `ftue`, và theo owner thì gần như chưa game nào gửi. Chưa có dữ liệu là lúc
đổi rẻ nhất — giống `f_sdk_level_action` tuần này.

---

## 1. Vì sao không giữ funnel `ftue`

Funnel `ftue` dùng shape `OnceOrdered`: game tự đánh `priority` 0, 1, 2… và SDK chặn bước nào không liền mạch hoặc lặp lại. Ba hệ quả:

1. **Chèn một bước là phải đánh số lại.** Sổ chống trùng của SDK khoá theo `priority`. Game thêm một bước tutorial vào giữa ở bản sau thì
   người chơi đang dở tutorial gặp sổ cũ, và mọi bước sau bị chặn trắng.
2. **Bỏ một bước là chặn mọi bước sau.** Shape này bắt liền mạch; tutorial thật thì hay có bước tuỳ điều kiện.
3. **Không có chỗ cho "bỏ qua".** Thêm cờ `skipped` thì lại là một field điều khiển nghĩa của dòng.

Live-op từng đi qua funnel rồi phải tách ra `f_sdk_live_ops_data` vì đúng họ lỗi này.

## 2. Hình dạng đề nghị

Một lần cài = một FTUE. Mỗi mốc một event. Họ tên `f_sdk_ftue_*` — `LIKE 'f_sdk_ftue_%'` quét cả họ.

| Event | Field riêng | Bắn khi |
|---|---|---|
| `f_sdk_ftue_first_screen` | — | Màn hình **đầu tiên người chơi tương tác được** (home nếu game có home, màn chơi nếu vào thẳng) |
| `f_sdk_ftue_tutorial_start` | `tutorial_id` | Tutorial onboarding bắt đầu |
| `f_sdk_ftue_tutorial_step` | `tutorial_id` · `step_index` · `step_name` | Một bước tutorial **HIỆN RA** (không phải lúc làm xong) |
| `f_sdk_ftue_tutorial_complete` | `tutorial_id` | Làm hết tutorial |
| `f_sdk_ftue_tutorial_skip` | `tutorial_id` · `last_step_index` | Người chơi bấm bỏ qua |

- **`tutorial_id`**: id phiên bản tutorial do game đặt (vd `tut_v3`). Game đổi tutorial ở bản sau thì đặt id mới ⇒ số liệu hai phiên bản
  không lẫn, và không có chuyện đánh số lại.
- **`step_index`**: số nguyên ≥ 1, thứ tự bước **trong** `tutorial_id` đó. Không phải liền mạch.
- **`step_name`**: tên bước do game đặt, snake_case, ổn định (`first_swipe`, `first_match`…). **Vocab của từng game** — không so chéo game,
  và cũng không nên so: bước 7 của game A không phải bước 7 của game B.
- **`last_step_index`**: SDK tự điền = `step_index` lớn nhất đã gửi trong `tutorial_id` đó. Vắng nếu bỏ qua trước bước đầu tiên.

### Luật SDK tự giữ (game không phải nhớ)

- **Mỗi mốc một lần cho mỗi lần cài.** Riêng `tutorial_step`: một lần cho mỗi (`tutorial_id`, `step_index`) — người chơi tắt app rồi tutorial
  chạy lại từ đầu thì không đếm đôi. Sổ nằm ở máy: **cài lại app là sổ mới**.
- **`complete` và `skip` loại trừ nhau** trong một `tutorial_id`: cái đến trước thắng, cái sau bị bỏ (kèm cảnh báo cho dev).
- **SDK không ép thứ tự.** Bỏ hay thêm bước đều không bị chặn.
- `tutorial_id` khai một lần ở mốc start và SDK nhớ (sống qua lần mở app sau). Bước gửi khi chưa có start thì SDK cảnh báo và để
  `tutorial_id` **vắng**, không đoán.
- **Không mang cột thời gian nào.** Mọi dòng đã có `total_play_time` (đếm từ 0 lúc cài) và `client_sent_time`; §D6 đã chốt việc tính khoảng
  là của server — client không tổng hợp gì.

### API phía game

Mỗi mốc một hàm, không có tham số chọn mốc, không có số `priority`, không phải khai shape:

```csharp
Ftue.OnFirstScreen();
Ftue.OnTutorialStart("tut_v3");
Ftue.OnTutorialStep(3, "first_match");
Ftue.OnTutorialComplete();
Ftue.OnTutorialSkip();
```

Chỉ dùng cho **tutorial onboarding**. Tutorial của từng tính năng (battle pass, event…) không phải FTUE — đọc qua cụm Ui / live-op của tính năng đó.

## 3. Luật đọc đề nghị

**Phễu chéo game** (bốn mốc lõi giống hệt nhau ở mọi game):

```
mở app lần đầu (app_open cold đầu đời)
  → f_sdk_ftue_first_screen
  → f_sdk_ftue_tutorial_start
  → f_sdk_ftue_tutorial_complete  |  f_sdk_ftue_tutorial_skip
  → f_sdk_level_data Start màn 1  → Pass màn 1
```

- **Rơi ở bước nào:** `max(step_index)` của `f_sdk_ftue_tutorial_step` theo user, cắt theo (game, `tutorial_id`).
- **Bỏ qua ở đâu:** `last_step_index` của dòng skip.
- **Một user có hai dòng cùng mốc** (cài lại app, hoặc hai máy cùng tài khoản): lấy dòng **đầu tiên**.
- **Thứ tự các mốc của một user:** sắp theo `client_sent_time` (cùng một đồng hồ máy nên thứ tự đúng kể cả khi máy lệch giờ). Còn câu
  *"dữ liệu này lên kho từ khi nào"* thì đọc `server_receive_time` — bài học thư `-d` tuần này.

**Hai phép kiểm DQ cần neo lại:**
- **10a** (`ftue ≈ new installs`): đổi *actual* từ *"dòng ftue `scene_enter`/`join`"* sang **số user có `f_sdk_ftue_first_screen`**.
- **`ftueStep1`**: đang đọc profile `last_funnel_step_order_by_funnel`. Đề nghị đổi sang *"người mới ngày D có `f_sdk_ftue_first_screen`"*.

**Rollup gold đang chờ** (`time_to_scene` · `time_to_tutorial_complete` · `time_to_level_1`): `time_to_scene` neo vào `first_screen`;
`time_to_tutorial_complete` nên có thêm nhánh **skip** (hoặc một cột `tutorial_outcome` ở gold — quyền của các bạn).

## 4. Nhờ tra trước

1. **Funnel `ftue` cũ có dòng nào không:** đếm `f_sdk_funnel_data` `funnel_name = 'ftue'` theo game, 30 ngày. Nếu có, dữ liệu đó đọc theo
   luật funnel cũ; từ bản SDK mới thì đọc theo `f_sdk_ftue_*`. SDK sẽ **không chặn** game gửi funnel `ftue` cũ — chỉ đánh dấu hằng số đó
   lỗi thời và trỏ sang API mới.
2. Năm tên event — đồng ý, hay các bạn đặt tên khác?

Bên mình gửi **bản + ngày** khi publish.

---

## Kho trả lời 10/11 (`loader-v3-work/ticket9/Reply-Loader-Ftue.md`) — ĐÃ KÝ

- ✅ **Ký cả năm tên event và luật đọc**, không đổi chữ nào. Catalog §18. Phía loader không phải code: event `f_sdk_*` mới tự vào silver.
- ❌ **Đính chính câu của bên mình:** *"gần như chưa game nào gửi funnel `ftue`"* — **sai**. Funnel `ftue` cũ đang chạy thật trên nhiều
  game (một game tới vài chục bước). Repo SDK không gọi nó là đúng — chính **game** gọi `Funnel` với tên `ftue`. Ngoài ra còn nhiều
  funnel tutorial **tự đặt tên** (`ftue_tutorial`, `TUTORIAL`, `tutorial`, `onboarding`…) không đi qua hằng số `ftue`. Bài học: *"chưa ai
  dùng"* là câu hỏi cho DỮ LIỆU, không phải cho grep repo SDK.
- **Dữ liệu cũ KHÔNG map sang họ mới.** Dòng cũ không có start/complete/skip hay `tutorial_id`; bước đầu của funnel cũ có thể là màn
  loading (khác nghĩa `first_screen`); funnel cũ còn trộn mốc level vào giữa tutorial; và game lên bản mới có thể gửi cả hai. ⇒ Phân
  biệt hai thời kỳ bằng **tên event**; **đừng cộng hai nguồn**.
- SDK vì vậy **giữ nguyên hành vi** của funnel `ftue` (shape `OnceOrdered` vẫn đăng ký sẵn) — chỉ đánh dấu lỗi thời `FFunnelName.Ftue` +
  ba hằng số bước, trỏ sang `Ftue.*`. Đổi luật lọc của một funnel đang chạy là đổi số trên dữ liệu sống.
- **DQ:** `ftueStep1` kho sẽ đổi thành *"người mới có `first_screen` HOẶC có bước funnel cũ"* để game chưa lên bản không bị tính là rơi.
  ⚠ Phép kiểm **10a không phải của loader** mà của đội access (`bigdata-access-service`, `dq-integration-coverage.md`) — thư này gửi nhầm
  chỗ; phải báo đội access.
- **Gold:** `time_to_scene` neo vào `first_screen`, thêm cột `tutorial_outcome` (`complete` / `skip`).

**Còn nợ kho:** (1) bản + ngày khi publish; (2) **báo các game đang dùng funnel `ftue` cũ và các funnel tutorial tự đặt tên** chuyển sang
`Ftue.*` — danh sách game nằm trong thư trả lời của kho (không chép vào doc SDK).
