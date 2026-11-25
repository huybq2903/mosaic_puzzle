# Nhận ticket `currency_class` — gửi loader (06/11/2026)

> Trả lời `ticket8/currency/Request-SDK-CurrencyClass.md`.
> **Nhận đề nghị.** Lý lẽ đứng: người biết chắc hạng của một đồng tiền là người định nghĩa nó, và
> loader đoán thì **luôn đi sau game một nhịp**. Dưới đây là hai câu các bạn hỏi, cộng **bốn chỗ
> trong spec bên mình đề nghị sửa trước khi code** — đều là chỗ đã có án trong module này.

## 1. Hai câu các bạn hỏi

### Câu 1 — `DeclareCurrency` đặt ở đâu?

✅ **Đặt ở `FalconBigDataController.Resource.DeclareCurrency` (BigData). Inventory gọi vào đó, không
tự giữ sổ.** Ba lý do, theo thứ tự nặng:

| Lý do | |
|---|---|
| **Tập đóng phải ở cùng chỗ với hợp đồng** | `FCurrencyClass` là từ vựng đóng, đúng hạng với `FResourceWhen` / `FFeatureStep` — chúng nằm ở BigData vì đổi chúng là **đổi hợp đồng**. Để tập đóng ở một module tiêu thụ là mời người sửa nó mà không ai thấy |
| **Không phải game nào cũng dùng Inventory** | Chính ticket viết *"game dùng Inventory nên được khai sẵn"* — còn game không dùng thì sao? Sổ ở BigData thì cả hai nhóm đều có đường khai |
| **Vẫn đúng yêu cầu "một nguồn khai duy nhất"** | Sổ là **một**; Inventory chỉ là **một người gọi** vào đó, từ chỗ định nghĩa resource. Game không dùng Inventory thì gọi trực tiếp |

⚠ **Nhưng phần Inventory là ticket riêng, không phải việc bên mình.** Inventory nằm ngoài các module
BigData được sửa, nên bên mình ship **sổ + API + cảnh báo**, còn việc Inventory đọc thuộc tính
`currencyClass` trên định nghĩa resource rồi gọi `DeclareCurrency` là **ticket gửi owner Inventory**.
Nếu Inventory chưa làm thì game vẫn khai tay được — không ai bị chặn.

### Câu 2 — bản SDK nào có field?

🔴 **Không phải 1.6.0.** Bản đó **đã đóng và đã test xong hôm nay** (470/470 trong Unity). Thêm field
wire mới vào một bản đã đóng là mất đúng cái mà việc đóng mua được.

➡ **1.7.0**, và nó phụ thuộc **các bạn**, không phụ thuộc tốc độ code bên mình. Theo luật §11 của
spec, **loader phải ký cột trước khi SDK publish**. Nên mốc là:

| Thứ tự | Việc | Của ai |
|---|---|---|
| 1 | Ký cột `currency_class` + chốt tập đóng 9 giá trị | loader |
| 2 | Code + test + publish 1.7.0 | BigData |
| 3 | Ghi mốc era vào catalog: *"trước bản app đầu mang BigData 1.7.0: hạng do loader đoán"* | loader |
| 4 | Inventory gọi `DeclareCurrency` từ định nghĩa resource | owner Inventory |

⚠ Và như mọi era: **đừng tin số bản làm biên thật.** Biên đọc được từ dữ liệu — ngày đầu tiên một
game có dòng `economy_txn` mang `class_source = 'client'`. Hai biên lệch nhau vài ngày vì người chơi
cập nhật rải rác, đó là chuyện bình thường.

---

## 2. Bốn chỗ bên mình đề nghị sửa trong spec

### 🔴 2.1. Giá trị ngoài tập đóng: **BỎ KEY**, đừng thay bằng giá trị thay thế

Spec viết *"bỏ và ghi error log, giống cách `CorrectValues` đang làm với `amount` âm"*. Hai ca đó
**không giống nhau**, và chỗ khác nhau là chỗ đau:

| | `amount` âm | `currencyClass` lạ |
|---|---|---|
| `CorrectValues` làm gì | kẹp về 0 (`CheckNumberNonNegative`) | — |
| Khuôn cho **chuỗi** trong module này | `CheckNonBlank` → thay bằng `"Unknown"` | 🔴 **không được dùng khuôn này** |

Năm field tài nguyên (`currency`, `itemType`, `itemId`, `resourceWhen`, `resourceWhere`) mặc định là
chuỗi `"Unknown"`, và đó là **cái bẫy đã ghi sổ** của module: `"Unknown"` là một giá trị **trông hợp
lệ**, nên không ai biết nó thiếu. Hạng tài nguyên thì tệ hơn nữa — một dòng `currency_class =
'unknown'` sẽ được **đếm vào** mẫu số "đã có hạng", làm con số 64% của các bạn **trông như đã cải
thiện** trong khi không.

➡ Nên: giá trị lạ ⇒ **error log + BỎ KEY**. Vắng mặt là lời khai đúng: *"không biết"*.

### ⚠ 2.2. Khai hai hạng khác nhau: **lần ĐẦU thắng**, không phải lần sau

Spec viết *"lần khai sau thắng, kèm warning"*. Bên mình đề nghị **ngược lại**:

- Lần-sau-thắng làm giá trị phụ thuộc **thứ tự gọi lúc khởi tạo** — mà thứ tự đó do scene và DI
  quyết định, không ổn định giữa các bản build.
- Tệ hơn: một lần khai sai **muộn** sẽ **ghi đè im lặng** một khai đúng, và trong **cùng một phiên**
  các dòng trước/sau thời điểm đó mang hai hạng khác nhau cho cùng một đồng tiền. Kho không gỡ được.
- Sửa một khai sai là **sửa code**, không phải ghi đè lúc chạy.

➡ Lần đầu thắng, warning vẫn in đủ để lộ xung đột. Đổi hướng là một dòng, nên nếu các bạn thấy
lần-sau-thắng đúng hơn thì nói, bên mình theo.

### ⚠ 2.3. "Không phân biệt hoa/thường": **chỉ thường hoá, KHÔNG bỏ `_`**

Module này đã có một luật chuẩn hoá tên (`FWireReservedKeys`): bỏ `_` **và** thường hoá, để
`fail_count` / `failCount` / `FAIL_COUNT` đều bắt được. **Đừng** dùng luật đó ở đây.

Lý do: tên tiền dùng `_` làm **dấu phân cách thật** (`pow_magnet`, `infi_live`, `turn_*`). Bỏ `_` là
gộp `infi_live` với một tên `infilive` khác nghĩa, mà gộp sai hạng thì hỏng đúng con số kinh tế.

➡ Tra sổ bằng **`ToLowerInvariant()` thôi**.

### 🟢 2.4. Cảnh báo tên chưa khai (§2.3 của ticket): nhận, và nó **trùng với một ticket khác**

Đội api/viz vừa gửi một ticket khác xin đúng cơ chế này: *"tên loại tiền ngoài danh sách game đã
đăng ký → cảnh báo (22 game có > 20 loại tiền do nhét mã vật phẩm vào ô loại tiền)"*. Hai bên hỏi
**cùng một thứ**, nên bên mình làm một lần.

ℹ️ Và nó là cái thứ **ba** cùng hình dạng trong module: `FunnelRegistry.Register` đã có sẵn và chạy
đúng kiểu này; ticket api/viz còn xin **sổ đăng ký booster** nữa. Bên mình sẽ dựng
`DeclareCurrency` **trên cùng khuôn** `FunnelRegistry` thay vì ba cơ chế riêng.

---

## 3. Hai điểm bên mình xác nhận không đổi

- 🟢 **`itemType` / `itemId` giữ nguyên** — đồng ý và đã ghi: `itemType` là tập **mở** do game đặt,
  `currencyClass` là tập **đóng** dùng chung fleet. Hai thứ khác hạng, không thay nhau.
- 🟢 **`currency`, `amount`, `flowType`, `exchangeId` không đổi.** Đồng ý.

## 4. Một câu bên mình hỏi lại

❓ **`unlimited` và `event_counter` là "phi kinh tế" — ai chịu trách nhiệm loại chúng?**

Bốn doc event bên mình vừa viết có **ba** "tiền tệ giả" (`ladder_streak_count`, `team_quest_map`,
`team_arena_flag`) và đã ghi **một luật đọc dùng chung** là phải loại khỏi mọi chart kinh tế
(ticket `Request-Loader-LiveOpsEventBacklog.md` mục C1).

Nếu `currency_class` tới, luật đó nên đổi từ *"loại ba tên này"* sang *"loại
`class IN ('unlimited','event_counter')`"* — tức luật đi theo **hạng**, không đi theo tên, và tự đúng
cho event thứ tư. Các bạn chốt thì bên mình sửa C1 theo.
