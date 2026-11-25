# UI Kit của Level Editor

Toàn bộ UI của editor dựng bằng `Kit.*`. UI mới dùng kit, không viết helper riêng. Class `vt-*` còn lại chỉ là
class tính năng gắn lên phần tử kit.


## Component

| Cần | Dùng |
|---|---|
| Khung | `El` · `Row` · `Col` · `Spacer` · `Divider` · `Put` |
| Chữ | `Text(p, text, KitText.*, wrap)` · `Note` · `Tint` |
| Thẻ | `Card` · `Fold` (nút đặt ở `.Actions`) · `Section` · `Head` |
| Nút | `Button` · `Primary` · `Danger` · `Subtle` · `IconButton` · `Close` · `Trash` · `Toggle` · `Chip` / `ToggleChip` · `HelpTip` · `SetBusy` · `Group` |
| Nhập | `Input` · `Search` · `Stepper` · `Choice` · `Check` · `Slider` / `SliderFloat` · `Field` · `SyncValue` |
| Chọn | `Segmented` / `SyncSegmented` · `TabStrip` · `Tabs` · `Dropdown` · `Menu` · `Popover` |
| Danh sách | `Stats` · `Scroll` |
| Trạng thái | `Empty` · `Progress` · `Badge` · `Dot` · `Swatch` |
| Lớp phủ | `Modal` · `Confirm` · `Place` · `Callout` |
| Bề mặt nổi | class `vk-surface` (+ `vk-glass`) |
| Đang chọn | `SetOn` (`vk-on`) cho nút/chip/tab · `vk-selected` cho dòng bảng/list |

Tooltip: `Kit.Tip` / tham số `tip:` đi qua `Tip` của tool.

## File

| File | Nội dung |
|---|---|
| `Kit.cs` | khung, chữ, `Tip`, `SetText` `SetShown` `SetOn` `SetEnabled` `Put` |
| `Kit.Button.cs` | nút (tone × size) |
| `Kit.Icon.cs` | `Icon` + `KitGlyph` |
| `Kit.Tag.cs` | chip, badge, dot, swatch |
| `Kit.Input.cs` | `Field` `Input` `Search` `Stepper` `Choice` |
| `Kit.Section.cs` | `Section` `Head` `Panel` `PanelHead` |
| `Kit.Tabs.cs` | `Tabs` `TabStrip` `Segmented` |
| `Kit.Status.cs` | `Empty` `Progress` `Stats` |
| `Kit.Layer.cs` · `Kit.Popover.cs` · `Kit.Modal.cs` · `Kit.Callout.cs` | lớp phủ, popover, dropdown, modal, confirm |

- USS: token ở `010-tokens.uss`, component ở `900-kit.uss`, chỉnh riêng từng màn ở `980-kit-fit.uss`.
- Nối vào app: `EditorUiHost` (`Ensure`, `Kit.BuildLayers`, `Kit.ResetLayers`).

## Stepper

| Cần | Dùng |
|---|---|
| Sync mỗi khung | `stepper.Sync(v)` — không ghi đè lúc đang gõ |
| Không có −/+ | `buttons: false` |
| −/+ đi qua hàm riêng | `stepper.OnNudge` |
| Lăn chuột khi chưa bấm vào ô | `WheelAlways = true` (mặc định tắt) |
| Ô rộng · ô giãn | class `vk-wide` · `vk-fill` |


## Cờ của modal / popover

| Cờ | Mặc định |
|---|---|
| `KitModal.OwnKeys` | `false` — kit chặn phím tắt, Esc đóng |
| `KitModal.ScrimCloses` | `true` |
| `KitPopover.TakesEscape` | `true` |
| `KitPopover.CloseButtons` | `KitMouse.Left` |
| `KitPopover.Side` · `Gap` | `Below` · `4` |
| `KitDropdown.CustomFill` · `ItemTip` | — |
| `Kit.Modal(…, layer:)` | `Kit.ModalLayer` |

Modal / popover tạo **trước** `WireKit` (vd. dropdown trong các bước bên trái) được kit giữ lại rồi gắn vào layer khi
`BuildLayers` chạy — trước đây chúng mồ côi nên bấm không mở. Dựng lại UI thì gọi `Kit.ResetLayers()` trước.

## Nguyên tắc

1. Giữ lối **Sync mỗi khung**: mọi `Set*` chỉ ghi khi giá trị đổi.
2. **Không style tĩnh trong C#** — `style.xxx =` chỉ cho giá trị tính lúc chạy.
3. Chữ trên UI: tiến trình dùng `Progress`, giải thích đặt trong tooltip (`Kit.Tip`).
4. USS: `var()` chỉ ở thuộc tính một giá trị; không có `gap` — thêm con qua `Kit.Put` mới có khoảng cách.
5. **`flex-shrink` mặc định là `0`**, khác CSS web. Khối nào phải co lại trong khung cha (vùng cuộn, thân
   modal, ô ảnh) thì viết đủ `flex-grow: 1; flex-shrink: 1; min-height: 0` — thiếu `flex-shrink` là nó
   phình theo nội dung rồi tràn đè lên phần bên dưới, mà chỉ lộ ra khi danh sách đủ dài.
   `Kit.Col` / `Kit.Row` còn mang sẵn `.vk-col { flex-shrink: 0 }` / `.vk-row { flex-shrink: 0 }` trong
   `900-kit.uss`, nạp sau file tầng — ghi đè ở file tầng bằng selector một class là **thua**. Viết
   `.vk-col.vt-abc { flex-shrink: 1 }` (hai class) hoặc gắn class `vk-grow`.

## Quy ước UX

| Việc | Quy ước |
|---|---|
| Tone nút | Một `Primary` mỗi khung · phá dữ liệu `Danger` + `Confirm` · phụ `Subtle` |
| Đóng | `Kit.Close` (icon `×`, rê chuột nền đỏ) + Esc, không thêm nút "Đóng" ở footer. Bỏ một mục trong danh sách cũng dùng `Kit.Close` |
| Xoá | `Kit.Trash` (icon thùng rác, rê chuột nền đỏ) |
| Nút rút gọn | Nút chức năng có icon rõ nghĩa (tải lại · thư mục · +/− · ‹ ›) dùng `IconButton`, chữ cũ chuyển vào tooltip. Nút mà chữ đổi theo trạng thái (vd. "đang quét…") thì giữ chữ |
| Chữ nút | Viết hoa chữ đầu. Tiêu đề section truyền chữ thường, kit tự IN HOA |
| Từ vựng | `Huỷ` · `Đóng` · `Dừng` · `Xoá` · `Bỏ chọn` |
| Tiếng Anh | Chỉ tên riêng (CMS, Insight, Stream, LGD, 3D) |
| Cột panel (`vt-panel`) | Sát biên màn hình: không margin, không viền. Chỉ đệm 2px ở cạnh chạm mép màn hình hoặc chạm vạch tách (`vt-left`: trái; `vt-right` · `vt-tune`: trái + phải), trên/dưới không đệm. Cột bên phải chỉ giữ 1 vạch trái để tách khi hai cột nằm cạnh nhau. Chữ trần cần lề thì lề đặt ở chính khối đó (vd. `vt-header`, `vt-footer`), không đặt ở panel |

## Kiểm

- Mở Level Editor thật, thử Esc, rê chuột, trạng thái tắt.

## `SetShown`/`SetText` trên `null` thoát im lặng

`if (element == null) return;` — cố ý, để gọi được trên widget chưa dựng. Đổi lại: **widget dựng ra
mà quên giữ tham chiếu thì mọi lệnh ẩn/hiện sau đó không làm gì**, và nó đứng nguyên ở trạng thái lúc
tạo, không lỗi không log. Dựng một hàng ô tái dùng (`StoreCell`, row của bảng…) thì mỗi widget mới phải
vào cả khối khởi tạo bản ghi ô, không chỉ vào cây UI.

## Thứ tự vẽ = thứ tự anh em, `position: absolute` không nâng lớp

Nhãn/huy hiệu đè lên ảnh phải được **thêm vào cây SAU** phần tử ảnh, không thì ảnh phủ lên nó. USS
không có `z-index`; muốn nâng sau khi đã dựng thì gọi `BringToFront()`. Bẫy ở chỗ nó không hỏng đều:
ô nào ảnh chừa đúng góc đặt nhãn thì vẫn thấy, ô nào ảnh lấp kín thì nhãn biến mất — đọc ra thành
"chỉ vài ô bị lỗi" chứ không phải "sai thứ tự".
