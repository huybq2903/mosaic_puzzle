# SDK BigData → loader: thêm `revive_type` cho hồi sinh, và mở `f_sdk_level_action_booster` (10/11/2026)

> Gửi: loader. Từ: owner SDK BigData.
> Hai việc trên họ `f_sdk_level_action_*` các bạn ký 09/11. **Cả hai chưa publish** — `f_sdk_level_action_revive` mới chỉ nằm trong
> bản chưa phát hành — nên thêm lúc này **không sinh era** nào. Chưa code, chờ các bạn ký.

## Tóm tắt

| # | Đổi gì | Vì sao |
|---|---|---|
| 1 | `f_sdk_level_action_revive` thêm field tuỳ chọn **`revive_type`** | Dòng revive hôm nay không nói được *loại* hồi sinh. Game có nhiều loại (+5 lượt đi, +30 giây, phá bom rồi chơi tiếp…) sẽ phải nhét vào extras, và các bạn không đọc chéo game được |
| 2 | Event MỚI **`f_sdk_level_action_booster`** — một dòng = một lần dùng booster **trong ván** | Ba nguồn booster hiện có trả lời *dùng bao nhiêu* và *tốn bao nhiêu*, nhưng không trả lời *dùng ở đoạn nào của ván* và *booster lúc dùng là có sẵn hay mua tại chỗ* |

---

## 1. `revive_type` trên `f_sdk_level_action_revive`

| Field | Kiểu | Nghĩa |
|---|---|---|
| `revive_type` | string, tuỳ chọn | Loại hồi sinh — **vocab MỞ của từng game**, snake_case, ổn định (`extra_moves`, `extra_time`, `clear_bombs`…). Vắng = game không khai |

Các field đã ký giữ nguyên (`play_turn_id` · `progress_pct` · `pay_method`).

- **Không field nào đổi nghĩa theo `revive_type`.** Nó chỉ là một chiều để cắt số, giống `surface_id` của cụm Ui.
- **Cố ý KHÔNG có field "số lượng"** (`amount = 5`). Đơn vị của nó sẽ đổi theo loại (lượt đi hay giây), tức đúng kiểu *một field bị field
  khác điều khiển nghĩa*. Cần tách "+5" với "+10" thì game đưa vào tên loại (`extra_moves_5`) — đó là cấu hình của loại hồi sinh.
- **Popup một màn, nhiều lựa chọn** (xem QC lấy +5, trả gem lấy +10): vẫn là một bề mặt `revive_offer`. Người chơi chọn gì thì đọc từ
  `revive_type` + `pay_method` của dòng **dùng**.
- **Hồi sinh nhiều lần trong một ván** (lần đầu miễn phí, lần hai mất gem): mỗi lần một dòng. Thứ tự các bạn tự đếm theo `play_turn_id`
  và `client_sent_time`; SDK không gửi số thứ tự (client không tổng hợp — §D6).
- `revive_type` là vocab của từng game ⇒ **không so chéo game theo giá trị**; chéo game chỉ so *có hay không hồi sinh*, *trả bằng gì*.

---

## 2. Event MỚI `f_sdk_level_action_booster`

Một dòng = **một lần dùng booster TRONG ván** (sau `Start`, trước kết cục). Booster chọn **trước** khi vào ván vẫn ở `pre_boosters_used`
của dòng `Start`, như hôm nay — event này không chở nó.

| Field | Kiểu | Nghĩa |
|---|---|---|
| `play_turn_id` | string | Lượt đang mở — SDK tự gắn (dùng booster giữa ván thì lượt luôn mở) |
| `booster_id` | string, **bắt buộc** | Mã theo sổ `Resource.RegisterBooster`. Mã lạ: SDK cảnh báo log dev, **vẫn gửi** (cùng luật đã ký). **Thiếu mã: SDK BỎ dòng** — "đã dùng một booster nào đó" không trả lời được câu nào |
| `progress_pct` | number `[0,100]` | % tiến độ ván lúc dùng. Ngoài khoảng ⇒ vắng, không kẹp |
| `pay_method` | string | Booster lúc dùng **có từ đâu**: `inventory` (có sẵn trong kho đồ) · `currency` (mua tại chỗ) · `ad` (xem QC lấy) · `free` (game cho). Cùng tập đóng của revive. Không biết thì vắng |

Không có `offered` / `accepted`: có dòng là đã dùng. **Popup gợi ý dùng booster** (hiện / bấm / đóng) là sự kiện của bề mặt Ui, đi vào
`f_sdk_ui_exposure` — y hệt cách tách của hồi sinh.

### 🔴 Luật đọc quan trọng nhất: một câu hỏi, MỘT nguồn

Lượt dùng booster giữa ván sẽ có mặt ở **hai** nơi: dòng của event này, và map `boosters_used` của dòng level. Nên:

| Câu hỏi | Đọc ở đâu |
|---|---|
| Ván này dùng booster nào, bao nhiêu lần | `boosters_used` của dòng level (như hôm nay) |
| Mỗi lần dùng rơi vào đoạn nào của ván | `progress_pct` của `f_sdk_level_action_booster` |
| Booster lúc dùng là có sẵn hay mua tại chỗ | `pay_method` của `f_sdk_level_action_booster` |
| Tốn bao nhiêu | Dòng tài nguyên (`resource_when = 'booster_use'` cho món bị tiêu), nối bằng `play_turn_id` |

**Đừng cộng hai nguồn** để ra số lần dùng. Ngược lại, hai nguồn cho một **phép kiểm DQ miễn phí**: số dòng booster theo
(`play_turn_id`, `booster_id`) phải bằng giá trị cùng mã trong `boosters_used` của lượt đó. Lệch là game gọi thiếu một bên.

---

## API phía game

```csharp
Level.OnRevive(progressPct: 60f, payMethod: FPayMethod.Ad, reviveType: "extra_moves");
Level.OnBooster(boosterId: "hammer", progressPct: 42f, payMethod: FPayMethod.Inventory);
```

`payMethod` không có giá trị mặc định ở cả hai, cố ý: game luôn biết người chơi vừa trả/lấy bằng gì; thật sự không biết thì truyền `null`.

## Câu hỏi cho các bạn

1. Đồng ý tên `revive_type` và event `f_sdk_level_action_booster`?
2. Popup gợi ý dùng booster có cần một tên bề mặt dùng chung như `revive_offer` không (vd `booster_offer`)? Không cần thì để vocab game.
3. Phép kiểm DQ *"số dòng booster = `boosters_used`"* — các bạn có muốn dựng không? Bên mình chỉ đề nghị, không chặn gì.

Bên mình gửi **bản + ngày** khi publish — cùng một lần với ba event đã ký 09/11 và họ `f_sdk_ftue_*`.

---

## Kho trả lời 10/11 (`Reply-Loader-ReviveTypeBooster.md`) — ĐÃ KÝ, đã code

- ✅ Ký nguyên hai mục; catalog §17.2 ghi cả bảng *"một câu hỏi, MỘT nguồn"* và sửa §10 trỏ sang event booster.
- ✅ Xin hằng số **`booster_offer`** ⇒ đã cấp (`FUiSurface.BoosterOffer`).
- ✅ Sẽ dựng phép kiểm DQ booster khi có dòng thật. Kho hỏi hai câu về `boosters_used`: **có gồm booster chọn trước ván không**, và **đếm
  lần dùng hay số món**. Câu trả lời thật (thư `Reply-SDK-Loader-ReviveTypeBooster.md`): **chưa từng được định nghĩa** — SDK không tính
  field đó, game truyền nguyên map, hợp đồng chỉ ghi *"số lượng dùng trong màn"*. Từ 10/11 định nghĩa: **số lần dùng, không gồm chọn
  trước ván** (ghi trên doc field + spec §5.6f). Dữ liệu cũ tuỳ game ⇒ đề nghị phép kiểm **tự dò quy ước từng game**: so với cả
  `boosters_used` lẫn `boosters_used − pre_boosters_used`, lệch cả hai mới là lỗi tích hợp.

**Còn nợ kho:** bản + ngày khi publish (cùng lần với Ui exposure, hồi sinh, FTUE).
