# Hợp đồng log cho event live-op — ĐỀ XUẤT v1

> **Trạng thái: ĐỀ XUẤT, chưa code.** Gửi **loader** và **visualizer** soát trước khi triển khai.
> Viết 14/10/2026. Phiên bản SDK: **1.5.0** (đã code + test, chờ loader ký event id).
> **Phải ký trước khi SDK publish** — đúng thứ tự đã làm với cụm quảng cáo v2.
> **Sửa 14/10 sau lượt soát của visualizer:** §2.1 (luật mẫu số + bằng chứng `action` không suy được
> từ `priority`); §5.1–§5.2 (hai view nền, `GROUP BY` thay `DISTINCT`, `LEFT JOIN`, chuẩn hoá `NULL`
> cho `round_id`); §5.3 (#2/#3/#4/#9 đổi mẫu số và công thức); §5.5 (era của `action`, Daily
> Challenge); §6 mục 4; §8 thêm Q7. Mọi số đo trong các mục này lấy trên bee android 14/10.
> **Sửa 14/10 (lượt 3, chốt owner):** `priority` đổi tên thành `live_ops_stage` và **thôi gánh hai
> vai** (§2, §2.1); thêm `live_ops_enter_ts` (§2, §5.3); log level nhận **bốn** field thay vì ba
> (§3, §6.3), nên `race_stage` / `path_stage` thành dư; Q5 đổi câu.
> **Sửa 19/10 (sau khi code + rà lại 4 module event):** §4 luật **3** đổi — mốc khoảng cách reset theo **ĐỢT**, không theo lượt (`*_since_enter` của Race cũng đo từ lúc vào đợt tuần); thêm §4 luật **4** — sổ mốc chỉ còn **đợt đang chạy + đợt vừa đóng**, dòng của đợt xa hơn gửi **vắng mặt** và dòng của mùa vừa đóng mang **lịch của chính mùa đó**; §4 luật **5** ghi rõ event chạy chồng (Race, Sky Path, Adventure) thì cặp `live_ops_level_*` **luôn vắng**. Lý do và bằng chứng: CHANGELOG mục chưa phát hành.
> **Sửa 14/10 (lượt 4):** loader + visualizer đã trả lời **Q1** (bốn param lên cột thật — ✅ cả
> thiết kế đứng được) và **Q5** (không nhân bản `live_ops_enter_ts`). **Q7** → đối chiếu Daily
> Challenge ở **phụ lục C**.

## Vì sao cần bản này

Sáu event đã có doc tracking (Master League, Race/Go-Kart, Adventure, Sky Path, Endless Treasure,
Card Collection). Mỗi event tự đặt tên cho **cùng một thứ**:

| Thứ | Sáu event đang đặt |
|---|---|
| Mã đợt | `league_id` · `race_id` · `adventure_cycle_id` · `path_cycle_id` · `endless_treasure_cycle_id` · `card_col_season_id` |
| Mốc tiến độ | funnel `milestone` (Race) · `detail.league_stage` (ML) · dòng thưởng (Adventure, Sky Path, Card) · dòng nhận quà (ET) |
| Số lần thử | `race_attempt` · `path_attempt` · không có (4 event còn lại) |

Hệ quả: mỗi event mới là một lần đặt tên mới, một lần đăng ký `funnelName` với loader, và một lần
visualizer viết lại query. **Dashboard không bao giờ tự động được.**

Bản này chuẩn hoá đúng phần khung đó thành **cột hợp đồng dùng chung cho mọi event**, và để lại
phần riêng (điểm, rank, bậc sao, bot…) cho từng event như hiện nay.

---

# 1. Tóm tắt: log nào thêm gì

| Log | Thêm gì | Loại việc |
|---|---|---|
| **`f_sdk_live_ops_data`** | **LOG MỚI** — xương sống tiến độ event (thay funnel `event_*`) | 🔴 Cần loader **ký event id mới** |
| `f_sdk_resource_data` | 4 param `live_ops_*` | 🟡 Additive |
| `f_sdk_ads_data` | 4 param `live_ops_*` | 🟡 Additive |
| `f_sdk_in_app_data` | 4 param `live_ops_*` | 🟡 Additive |
| `f_sdk_iap_start_purchase_data` | 4 param `live_ops_*` | 🟡 Additive |
| `f_sdk_iap_offer_data` | 4 param `live_ops_*` | 🟡 Additive |
| `f_sdk_ui_impression` | 4 param `live_ops_*` — **vào khoá gộp cụm** | 🟡 Additive (event này **chưa ký**, thêm luôn thì không sinh era) |
| `f_sdk_event_data` (custom) | 4 param `live_ops_*` | 🟡 Additive |
| `f_sdk_level_data` | **4** field `live_ops_*` (tuỳ chọn, xem §3) | 🟡 Additive |
| `f_sdk_funnel_data` | **Không đổi gì** — giữ nguyên cho `ftue` và 4 vocab di sản | ⚪ Không đụng |

Bốn param dùng chung:

| Param | Kiểu | Nghĩa |
|---|---|---|
| `live_ops_event` | string | Danh tính event, **để trần**: `card_collection`, `master_league`, `race`, `sky_path`, `adventure`, `endless_treasure` |
| `live_ops_cycle_id` | string | **Đợt** — cái có lịch thật. Rỗng = client chưa biết mã đợt (xem §5.4) |
| `live_ops_round_id` | string | **Lượt trong đợt**, chỉ event cho chơi lại nhiều lượt (Race: một bảng đấu = một round). Vắng = đợt chỉ có một lượt (trong kho sẽ là `NULL` — kéo theo một luật nối, §5.2) |
| `live_ops_stage` | int | **Mốc mà dòng này THUỘC VỀ** (không phải "mốc user đang ở"). Vắng khi dòng không gắn với mốc nào |

⚠ **Đây là cột toàn fleet, không phải cột riêng từng event.** Mọi event của mọi game dùng chung
đúng bốn cột này — không phạm luật "không thêm cột cho từng event" đã chốt 13/10.

---

# 2. Log mới `f_sdk_live_ops_data`

Hành vi gần giống `f_sdk_funnel_data` nhưng **chỉ có một hình dạng** nên bỏ được nhiều thứ.

| Key | Kiểu | Bắt buộc | Mô tả |
|---|---|---|---|
| `live_ops_event` | string | ✓ | Xem §1 |
| `live_ops_cycle_id` | string | ✓ | Rỗng = chưa biết mã đợt |
| `live_ops_round_id` | string | — | Chỉ event nhiều lượt |
| `action` | string | ✓ | Sáu giá trị, định nghĩa ở **§2.1** |
| `live_ops_stage` | int | — | **Số mốc** — CHỈ có ở dòng `milestone` và `claim`. Dòng vòng đời (`eligible`/`open`/`join`/`complete`) **vắng mặt**: chúng không phải mốc, mà `action` đã đủ định danh. **Không còn cột `priority`** |
| `cycle_index` | int | — | Đợt thứ mấy — trục sort bằng SỐ (tránh `season_9 > season_10` khi sort chuỗi). **Số nguyên tăng dần 1, 2, 3… trong lịch sử event. KHÔNG phải timestamp, KHÔNG phải mã đợt** |
| `cycle_start_ts` / `cycle_end_ts` | long | — | Lịch **của ĐỢT** (chung mọi user), epoch ms UTC, từ CONFIG event. **Chỉ có ở log này** |
| `live_ops_enter_ts` | long | — | Mốc **USER NÀY** vào đợt (epoch ms UTC) = thời điểm bước hợp lệ đầu tiên của **đợt** (lượt không dịch mốc này — §4 luật 3). Có nó thì `cycle_end_ts − live_ops_enter_ts` ra ngay **nhóm countdown lúc vào** trên *mọi* dòng live-ops, khỏi join ngược về dòng `join` của chính user. Đây cũng là thứ gánh vai cũ của `funnel_day` |
| `play_sec_since_enter` / `_prev_step` | long | — | §4 |
| `level_passes_since_enter` / `_prev_step` | long | — | §4 |
| `level_attempts_since_enter` / `_prev_step` | long | — | §4 |
| `live_ops_level_passes_since_enter` / `_prev_step` | long | — | §4 |
| `live_ops_level_attempts_since_enter` / `_prev_step` | long | — | §4 |
| `current_level` | int | — | Level hiện tại |

## 2.1 Sáu `action` — bản gốc ở `LiveOpsTrackingPattern.md` §2a

**Không định nghĩa lại ở đây.** Bảng mốc trong pattern (ký 17/09) là bản gốc: `eligible` (đủ tư
cách — **TRẠNG THÁI**, không phải khoảnh khắc), `open` (mở UI event, van nén 1 dòng/chu kỳ), `join`
(tham gia), `milestone` (số mốc — bản pattern gọi là `priority`, log mới đặt ở
`live_ops_stage`), `complete` (về đích), `claim` (nhận thưởng).

Bản này thêm đúng một thứ: **luật mẫu số**, vì đó là chỗ đọc sai nhiều nhất.

| Câu hỏi | Mẫu số | Tử số |
|---|---|---|
| Event có đáng mở không | `eligible` | `open` |
| UI có thuyết phục không | `open` | `join` |
| Thiết kế bên trong có gắt không | `join` | `milestone(N)`, `complete` |

**Không gộp ba action đầu vào một rổ** — ba mẫu số khác nhau, chênh nhau rất nhiều. Chart nào cũng
phải **ghi tên mẫu số lên nhãn**. Pattern §2a đọc drop-off theo đúng thứ tự `eligible → open →
join`, nên phép tự kiểm là: mỗi `(user, đợt)` phải thoả `eligible ≥ open`. Đợt nào `eligible = 0`
thì phía đọc **từ chối vẽ chart %**, đừng âm thầm rơi về mẫu số `join` — rơi kiểu đó thì mọi tỷ lệ
vống lên mà vẫn trông hợp lý.

🔴 **`join` KHÔNG kéo theo `open`** *(sửa 28/10 — bỏ `open ≥ join` khỏi phép tự kiểm)*. `open`
nghĩa là *user MỞ UI của event*; `join` nghĩa là *user đã vào cuộc*, và nó **có thể do code tự
bắn**: tự cho tham gia khi đủ điều kiện, hoặc theo trạng thái server. Án thực địa: kho đo Key
Collection 28/10 thấy rất nhiều cặp `(user, đợt)` có `join` mà không có `open`, và ở nhóm đó
`eligible → join` cách **0 giây** (nhóm bình thường cách 3–5 phút) — tức join không qua UI. Hệ quả:

- **`open ≥ join` KHÔNG phải bất biến**, đừng dựng luật DQ trên nó;
- với event có auto-join, `join/open` **không còn là tỷ lệ phễu**: đọc `join/eligible` cho *"bao
  nhiêu người vào cuộc"*, và `open/eligible` + §2d `ui.impression` cho câu hỏi về UI;
- **không được bắn `open` bù** cho đường auto-join — đó là khai *user đã mở UI* trong khi họ chưa
  từng mở, làm hỏng đúng cái mẫu số của câu hỏi UI;
- event nào có cả hai đường thì **khai nguồn trên chính dòng `join`** bằng `extraMeta`
  (`join_source = "auto" | "ui"`) — SDK đã merge `extraMeta` vào dòng live-op, không cần sửa gì.

⚠ **`action` phải giữ là cột riêng, KHÔNG suy từ `priority`.** Đo trên bee android (dòng funnel
01/09–14/10): `priority` của cùng một action khác nhau theo từng event, và **đụng nhau** với số mốc.

| `funnel_name` | eligible | open | join | milestone | complete | claim |
|---|---|---|---|---|---|---|
| `event_master_league` | 10 | 15 | 20 | *không có dòng nào* | 30 | 40 |
| `event_race` | **3** | 5 | 10 | **1, 2, 3** | 40 | — |
| `event_card_collection` | 10 | 15 | — | — | — | — |

Race dùng `priority = 3` cho **cả** `eligible` **và** `milestone(3)`. Master League thì chưa có dòng
`milestone` nào — mốc Stage của nó nằm trong `detail.league_stage`, đúng như §9 đã ghi.

✅ **Log mới gỡ chuyện này tận gốc** (chốt owner 14/10): không còn cột `priority`, và
`live_ops_stage` **chỉ xuất hiện trên dòng `milestone`/`claim`** — dòng vòng đời để trống. Nên
`max(live_ops_stage)` không thể trộn lẫn với số của `eligible` được nữa; lọc `action = 'milestone'`
vẫn nên giữ như một lớp phòng, nhưng không còn là thứ cứu công thức khỏi sai. Bảng đo ở trên vẫn
cần, nhưng chỉ cho **dữ liệu funnel CŨ** (§5.5).

**Van lọc (client):** một dòng cho mỗi `(live_ops_event, live_ops_cycle_id, live_ops_round_id,
action, live_ops_stage)` mỗi thiết bị — dòng vòng đời có `live_ops_stage` trống nên mỗi `action`
đúng một dòng. Sang đợt/lượt mới tự sạch. Không có luật thứ tự — mua vượt tier là
hợp lệ.

**Ba thứ của funnel cũ KHÔNG có ở đây**, và vì sao:

| Bỏ | Vì sao |
|---|---|
| `funnel_shape` + đăng ký shape | Log này chỉ có một hình dạng. Hết luôn ca "client quên đăng ký → funnel rơi về luật `ftue`, bước bị veto trắng" |
| `funnel_day` | Vai của nó ("vào đợt được mấy ngày") giờ tính từ `cycle_start_ts` — chính xác hơn, vì `funnel_day` phụ thuộc user có bắn `join` hay không |
| `funnel_name` per-event | Danh tính nằm ở `live_ops_event`. **Event mới = 0 việc đăng ký tên với loader** — đây là điều kiện để dashboard tự động |

`live_ops_stage` dùng **chung một tên** ở log này và ở các stream ở §1 — nối hai bên bằng
`USING (live_ops_event, live_ops_cycle_id, live_ops_stage)`, không phải alias cột nào.

---

# 3. Log level: bốn field, và luật "khai thì khai hết"

`f_sdk_level_data` nhận **cả bốn** param `live_ops_*` — **tuỳ chọn**, và khai là một **cam kết độc
quyền**: *ván này thuộc hẳn về event đó*, và nếu có `live_ops_stage`: *thuộc mốc đó*.

| Event | Khai được? | Vì sao |
|---|---|---|
| Master League | ✅ | Có bộ level riêng của mùa |
| Race / Go-Kart | ✅ kèm `round_id` | Ván trong một bảng đấu cụ thể |
| Adventure, Sky Path | ❌ **đừng khai** | Chạy **chồng** lên level thường, hai event có thể cùng lúc — khai là phải chọn một, tức bịa |
| Endless Treasure, Card Collection | — | Không có ván chơi của riêng nó |

⚠ `live_ops_stage` trên dòng level mang nghĩa **"ván này thuộc mốc nào"** — nhân quả, game biết
chắc đây là ván của chặng 2. **Không** phải "lúc đó user đang ở mốc mấy" (trạng thái). Không chắc
thì để trống.

Ba cái được, ngoài chuyện đếm ở §4:

- **"Win rate trong / ngoài event"** thành phép lọc cột thật (`live_ops_event IS NULL` hay không),
  thay vì "ván có extras tiền tố hay không" như bốn doc event đang phải viết.
- Race hết phải nhét bảng đấu vào chỗ khác — dòng level của bảng nào mang `round_id` của bảng đó.
- **`race_stage` và `path_stage` thành DƯ**: "gãy ở ván thứ mấy của chặng" và "win rate theo chặng"
  đọc bằng `live_ops_stage` trên chính dòng level, không cần key riêng của hai event nữa.

⚠ **Luật kỷ luật:** event nào khai thì **mọi** ván của nó phải khai. Khai một nửa thì
`live_ops_level_attempts_*` hụt mà vẫn trông hợp lý.
**DQ tự kiểm:** `live_ops_level_attempts_* ≤ level_attempts_*` luôn đúng — vượt là bug client.

---

# 4. Năm cặp mốc khoảng cách trên log live-ops

Mỗi cặp đo khoảng giữa **mốc vào phễu của đợt** → bước này, và **bước hợp lệ trước** → bước này.

| Cặp | Đếm gì | Nguồn phía client |
|---|---|---|
| `play_sec_since_*` | Giờ **CHƠI** (giây) | `TotalPlayTime`: monotonic, **chỉ chạy khi app foreground**, có persist |
| `level_passes_since_*` | Level **vượt qua** — toàn cục | `MaxPassedLevel` |
| `level_attempts_since_*` | **Lượt chơi mở ra** — toàn cục | State machine lượt chơi (không đếm lời gọi API, nên double-fire không làm phồng) |
| `live_ops_level_passes_since_*` · `live_ops_level_attempts_since_*` | Hai thứ trên nhưng **chỉ ván có khai event này** (§3) | Bộ đếm riêng theo `(event, đợt)`. ⚠ **KHÔNG phải bản trùng của cặp toàn cục** — cặp này là số chính xác của event. ⚠ **Bất biến `live_ops_* ≤ *` CHỈ đúng với cặp attempts** (cùng đếm lượt chơi); **cặp passes KHÔNG có bất biến** vì `level_passes_*` đo tiến độ **map chính** (`MaxPassedLevel`) — event có **bộ level riêng** (Master League) thì `live_ops_level_passes_*` lớn hơn hẳn là ĐÚNG *(sửa nhận định 28/10)*. **Vắng** với event không khai level (chạy chồng level thường). ⚠ **SDK 1.5.0–1.5.5 gửi `0` thay vì vắng** (sửa ở **1.5.6**): với dữ liệu khoảng đó, `0` ở cặp này nghĩa là *không áp dụng*, đừng đọc là *chơi 0 ván trong event* |

**Ví dụ đọc** — dòng `milestone(2)` của Sky Path:

| Field | Giá trị | Nghĩa |
|---|---|---|
| `play_sec_since_prev_step` | 2400 | Từ lúc qua chặng 1, user **chơi 40 phút** |
| `level_passes_since_prev_step` | 7 | …vượt **7 level mới** |
| `level_attempts_since_prev_step` | 23 | …nhưng **mở 23 lượt** → 16 lần thua. Đây là "chặng này gắt cỡ nào" |

**Luật đọc, năm điều:**

1. **Giờ chơi KHÔNG phải wall-clock.** Hai bước cách nhau một đêm ngủ vẫn có thể chỉ chênh vài chục
   giây — đúng thiết kế. Muốn khoảng wall-clock thì trừ `ts` hai dòng.
2. **Vắng mặt = KHÔNG BIẾT, không phải 0.** Ba ca vắng: bước đầu đợt (`*_since_prev_step`), client
   không đọc được nguồn, và delta ra âm (user xoá data → client bỏ field + ghi warning).
   **Đừng COALESCE về 0.**
3. **Reset theo ĐỢT, không theo lượt** *(sửa 19/10 — đổi so với bản 14/10)*. `*_since_enter` luôn
   đo từ mốc user vào **đợt**, kể cả event nhiều lượt như Race: dòng mốc của bảng đấu 2 nói *"3 ngày
   kể từ lúc vào đợt tuần"*, **không** phải *"5 phút kể từ lúc vào bảng 2"*. Câu "qua bảng này tốn
   mấy lần thử" đọc bằng `*_since_prev_step` (bước → bước), vẫn nguyên.
   <br>Vì sao đổi: cho lượt vào nhãn sổ mốc thì bước-của-đợt (`eligible`/`open`, không có lượt) và
   bước-của-lượt (`join`/`milestone`) đạp sổ của nhau mỗi nhịp, và mất đúng khoảng cách
   **`eligible` → `join`** — "mở cửa rồi bao lâu user mới chịu vào". Đổi lúc này **miễn phí** vì
   chưa game nào ship log live-ops.
4. **Dòng của đợt ĐÃ ĐÓNG vẫn có số, nhưng có hạn** *(19/10)*. SDK giữ sổ mốc của **đợt đang chạy +
   đợt vừa đóng** (hai ô, trần cứng để pool không phình theo số đợt). Nên dòng `complete`/`claim`
   của mùa vừa đóng — gửi trong lúc mùa mới đã chạy — vẫn đo từ mốc user vào **mùa đó**, và mang
   **lịch của chính mùa đó** (`cycle_start_ts`/`cycle_end_ts`), không mượn lịch mùa đang chạy.
   Dòng của một đợt xa hơn nữa (đã mất ô) thì 10 field khoảng cách + `live_ops_enter_ts` **vắng
   mặt** — thà thiếu hơn báo "vừa vào đợt lúc này".
5. **Có `live_ops_level_*` thì dùng nó; vắng thì dùng cặp toàn cục và hiểu đó là CHẶN TRÊN** (ván
   chapter chen giữa hai mốc cũng bị đếm). ⚠ Event **chạy chồng** level thường (Race, Sky Path,
   Adventure) thì cặp này **luôn vắng** theo đúng §3 — dòng level của họ không được khai event, vì
   nhiều event có thể cùng đang chạy. Nhãn chart phải ghi rõ "chặn trên".
   <br>⚠ **"Chặn trên" chỉ đúng cho attempts.** Với event có **bộ level riêng** (Master League),
   `level_passes_*` không chặn trên gì cả — nó là **tiến độ map chính**, một thước đo khác, nên
   `live_ops_level_passes_* > level_passes_*` là chuyện bình thường, **không** phải lỗi DQ.

`*_since_prev_step` tính theo **thời điểm log**, không theo thang `live_ops_stage`: event được nhảy
cóc mốc, neo theo "mốc ngay dưới" thì đúng ca nhảy cóc lại trắng số. Bước bị client veto **không
dịch mốc**.

---

# 5. Luật đọc & phép nối (phần cho visualizer)

## 5.1 Hai view nền: lịch đợt và catalog lượt

Hai việc khác nhau nên là **hai** view, không gộp.

**(a) `dim_live_ops_cycle` — lịch đợt.** Khoá `(event, cycle_id)`, vì hợp đồng **không có**
`round_start_ts`/`round_end_ts`: mọi lượt của một đợt dùng chung cửa sổ của đợt.

```sql
SELECT live_ops_event, live_ops_cycle_id,
       anyHeavyIf(cycle_start_ts, cycle_start_ts IS NOT NULL) AS cycle_start_ts,
       anyHeavyIf(cycle_end_ts,   cycle_end_ts   IS NOT NULL) AS cycle_end_ts,
       anyHeavyIf(cycle_index,    cycle_index    IS NOT NULL) AS cycle_index
FROM   f_sdk_live_ops_data
GROUP  BY live_ops_event, live_ops_cycle_id
```

**(b) `dim_live_ops_round` — catalog.** Khoá `(event, cycle_id, round_id)`, phục vụ dropdown UI, đếm
số lượt, so giữa các lượt. Thêm `count()` và `uniqExact(account_id)` để biết lượt nào có người chạm.

Cả hai không cần `LIKE`, không cần bảng cấu hình tay, không cần ai đăng ký tên trước. Catalog chỉ
hiện đợt **có ít nhất một user chạm tới** — với câu "game này có event nào, đợt nào" là đủ.

**Ba luật dựng view, cả ba đều rút từ số đo thật trên kho:**

| Luật | Vì sao |
|---|---|
| **`GROUP BY`, KHÔNG `SELECT DISTINCT`** | `DISTINCT` nhiều cột cho *mỗi tổ hợp* một dòng. Đo 14/10 trên `f_sdk_funnel_data` (bee android): `cycle_start_ts` điền **0%** ở Master League và **8,33%** ở Race, `cycle_index` điền **64%** ở ML. Tức cùng một đợt có cả dòng mang lịch và dòng rỗng → `DISTINCT` giữ cả hai → nối vào là **nhân đôi mọi số**, không báo lỗi |
| **`anyHeavyIf` (giá trị xuất hiện nhiều nhất), KHÔNG `min`/`max`** | `min(start)`/`max(end)` thì **một máy sai là cửa sổ giãn ra**, im lặng — đồng hồ máy lệch, hoặc thiết bị còn giữ config đợt cũ. Lấy theo số đông thì phải chiếm đa số mới dịch được. Ra `NULL` thì chấp nhận "không biết lịch đợt", **đừng bịa** |
| **`LEFT JOIN`, không `INNER`** | Không khớp thì mất lịch đợt, chứ không được mất dòng doanh thu |

`anyHeavyIf` chạy được trên ByteHouse (đo 14/10), nhưng bảo đảm của nó là *"đúng khi một giá trị
chiếm hơn một nửa"*, ngoài ca đó thì **không tất định**. Cần tất định thì đếm rồi lấy cái nhiều nhất:
`argMax(cycle_start_ts, cnt)` trên một subquery `GROUP BY (event, cycle_id, cycle_start_ts)`.

## 5.2 Nối mọi stream về `(event, đợt, mốc)`

Mọi dòng tự khai, nên phép nối là **equi-join**, không có cửa sổ thời gian, không có suy luận:

```sql
SELECT r.*, c.cycle_start_ts, c.cycle_end_ts
FROM   f_sdk_resource_data r
LEFT JOIN dim_live_ops_cycle c USING (live_ops_event, live_ops_cycle_id)
WHERE  r.live_ops_event IS NOT NULL
```

Đổi `f_sdk_resource_data` thành `f_sdk_ads_data`, `f_sdk_in_app_data`,
`f_sdk_iap_start_purchase_data`, `f_sdk_iap_offer_data`, `f_sdk_ui_impression`, `f_sdk_event_data`
— **cùng một khuôn**. Đó là lý do dashboard dựng một lần, event mới cắm vào là chạy.

⚠ **Nối thêm theo lượt (`round_id`) thì phải chuẩn hoá `NULL` ở phía đọc.** Hai số đo, cùng ngày
14/10:

| Phép trên ByteHouse | Khớp? |
|---|---|
| `NULL = NULL` | **không** |
| `'' = ''` | **có** |
| `'' = NULL` | không |

Và "vắng" trong kho hiện nay **là `NULL`, không phải `''`**: 2.239.819 dòng `ftue` (10–14/10) có
`cycle_id` và `race_id` **100% `NULL`, không một dòng rỗng nào**.

Nên join thẳng theo ba cột `(event, cycle_id, round_id)` sẽ **rơi trắng mọi event không có lượt** —
Master League, Adventure, Sky Path, Endless Treasure, Card Collection. Sửa ở **phía đọc**, không bắt
client hay loader đổi gì:

```sql
LEFT JOIN dim_live_ops_round d
       ON  r.live_ops_event    = d.live_ops_event
       AND r.live_ops_cycle_id = d.live_ops_cycle_id
       AND ifNull(r.live_ops_round_id,'') = ifNull(d.live_ops_round_id,'')
```

Chuẩn hoá **đúng `round_id`**, không áp cho cột khác: §3 và §5.5 dùng `live_ops_event IS NULL` làm
phép thử "ván trong / ngoài event", đổi `NULL` thành `''` là làm vỡ chính phép thử đó.

## 5.3 Mười chỉ số lặp lại → đọc từ đâu

Rà sáu doc event thì mười chỉ số này xuất hiện ở hầu hết event. Mỗi ô là một phép nối **cố định**:

| # | Chỉ số | Đọc từ |
|---|---|---|
| 1 | Impression + CTR cửa vào (icon, popup) | `f_sdk_ui_exposure` (≤ 1.7.0: `f_sdk_ui_impression`) + `f_sdk_iap_offer_data`, lọc `live_ops_event` |
| 2 | Tham gia / %DAU | **Ba số riêng**: `eligible` (được mở cửa) → `open` (mở màn hình) → `join` (chơi thật). Đừng gộp một rổ — §2.1 |
| 3 | **Tiến độ cao nhất + phân vị** | `max(live_ops_stage) WHERE action = 'milestone'` per user × đợt, **neo trên tập `eligible` của đợt — user không chạm mốc nào thì điền 0** (⚠ dưới). Dùng `quantileExact(p)`, **không dùng trung bình** |
| 4 | Rụng người giữa các mốc | Phễu **"đạt từ mốc N trở lên"** = số user có `max(live_ops_stage) >= N` **tính trên dòng `action = 'milestone'`** (bắt buộc — §2.1). **KHÔNG** đếm distinct user theo đúng mốc N (⚠ dưới) |
| 5 | **Số lần thử qua một mốc** | `level_attempts_since_prev_step` (hoặc `live_ops_level_attempts_*`) trên dòng `milestone(N)` |
| 6 | Source/Sink do event phát, so với tổng | `f_sdk_resource_data` lọc `live_ops_event`, cắt theo `live_ops_stage` |
| 7 | Trước/sau launch, join vs không: session, playtime, paying rate | Stream session + iap **sẵn có**; mẫu số lấy từ `eligible`/`join` |
| 8 | Doanh thu event / tổng IAP | `f_sdk_in_app_data` lọc `live_ops_event` |
| 9 | Cắt nhóm theo countdown lúc vào đợt | `cycle_end_ts − live_ops_enter_ts` — có trên **mọi** dòng live-ops nên khỏi join ngược về dòng `join`. ⚠ `live_ops_enter_ts` là **bước hợp lệ đầu tiên của ĐỢT** (thường là `eligible`), và **không** dịch theo lượt (§4 luật 3) — muốn đúng mốc `join`, hay mốc vào một bảng đấu cụ thể, thì lấy `cycle_end_ts − ts` trên chính dòng `join` đó. `ts` là epoch ms; trong kho nó là cột **`client_sent_time`**, **KHÔNG** phải `created_date` (⚠ dưới) |
| 10 | Quay lại / mua lại đợt sau | `live_ops_cycle_id` + `cycle_index` |

⚠ Chỉ số 7 là **phép so yếu** ở mọi event: event bật cho mọi user đủ điều kiện nên không có nhóm
đối chứng. Muốn kết luận chắc thì **A/B holdout** (dùng nhãn AB sẵn có trên mọi log).

⚠ **Vì sao #3 phải neo `eligible` và điền 0.** User không chạm mốc nào thì **không có dòng
`milestone` nào** — mà không có dòng nghĩa là *biến mất khỏi phép tính*, chứ không phải *được tính
0*. Ví dụ 100 user `eligible`: 80 người vào xem rồi bỏ (không dòng nào), 20 người lên mốc 5. Trung
vị theo cách cũ ra **5**, đọc thành "một nửa người chơi qua mốc 5", sự thật là **10%**. Nguyên lý:
mọi tỷ lệ phải neo trên **tập gốc xác định trước** (ở đây là `eligible`), không neo trên tập còn sống
sót — giống hệt luật của retention.

⚠ **Vì sao #4 phải là "từ mốc N trở lên".** §2 cho phép nhảy cóc mốc (*mua vượt tier là hợp lệ*),
nên đếm theo đúng từng mốc sẽ cho phễu **đi lên**: mốc 1 có 50 user, mốc 2 có 10, mốc 3 có 30 vì gói
quà đẩy thẳng lên mốc 3. Nhìn như lỗi dữ liệu nhưng dữ liệu đúng, công thức sai.
`max(live_ops_stage) >= N` thì luôn giảm dần theo đúng bản chất, và người nhảy cóc vẫn được tính cho mọi
mốc dưới.

⚠ **Phân vị còn đúng một việc:** một con số cho mỗi đợt để **so các đợt theo thời gian** (đợt 5 có
khó hơn đợt 4 không). Còn bên trong một đợt thì **phễu từng bước đọc tốt hơn** phân vị. Trung bình
thì bỏ hẳn — một người cày xuyên đêm đủ kéo lệch cả đợt.

⚠ **Mốc thời gian.** `ts` của hợp đồng là epoch ms, trong kho là `client_sent_time` (`Int64`). Cạnh
nó có `created_date` nhìn rất giống "thời điểm sự kiện" nhưng chỉ là `client_sent_time` đã đổi sang
giờ phiên (+7), nên nó **lệch ngày** với `created_day`: đo được dòng `created_day = 2026-10-11` mà
`created_date = 2026-10-12 01:00`. Lấy nhầm cột đó đi trừ `cycle_end_ts` thì countdown sai 7 tiếng
mà không có dấu hiệu gì.

Hai điều nữa về cột này. **Tên có chữ "sent" nhưng giá trị đóng dấu lúc DỰNG bản tin**, không phải
lúc gửi (`PlainLog.clientCreateDate = CurrentTimeMillis`, gán ngay khi khởi tạo) — nên luật **hoãn
gửi** ở §5.4 không làm sai countdown. Và vì `cycle_end_ts` đến từ config server còn mốc này từ đồng
hồ máy user, **countdown ra số âm là chuyện bình thường**, đừng coi là bug.

## 5.4 Bốn ca biên, tất cả đều nhìn thấy được

| Ca | Biểu hiện | Đọc thế nào |
|---|---|---|
| Client chưa biết mã đợt | `live_ops_cycle_id` **rỗng** | Client giữ luật **hoãn gửi**: chờ mã đợt về mới gửi; hết cơ hội thì gửi với mã rỗng — **không bịa mã** (bài học Endless Treasure). Dòng rỗng là "thiếu dữ liệu", đừng gán vào đợt nào |
| Nhận thưởng mùa 5 trong mùa 6 | Dòng mang `cycle_id` = mùa 5, `ts` nằm ngoài `[start, end]` của mùa 5 | **Đúng như vậy** — game khai đợt mà dòng THUỘC VỀ, không khai đợt đang chạy |
| Dòng quà khai `stage = 3` mà không có `milestone(3)` | Join thiếu | Game quên bắn mốc — **bắt được bằng query**, trước đây vô hình |
| `live_ops_level_attempts > level_attempts` | Bất biến bị vỡ | Bug client, xem §3 |

## 5.5 Era — dữ liệu cũ của sáu event

| Thứ cũ | Xử lý |
|---|---|
| Dòng funnel `event_master_league` / `event_race` / `event_endless_treasure` trên `f_sdk_funnel_data` | **Nằm yên với đúng nghĩa cũ.** Dữ liệu mới đi log mới. Đừng UNION hai bên mà không cắt theo mốc bản SDK |
| Key `league_id` · `race_id` · `endless_treasure_cycle_id` · `adventure_cycle_id` · `path_cycle_id` · `card_col_season_id` | Nghỉ dần, thay bằng `live_ops_cycle_id` |
| `resource_when = 'event_endless_treasure'` (vocab sống, **không đổi**) | Khi nối với `live_ops_event` (để trần) thì **bỏ tiền tố `event_`** |
| `f_sdk_level_data`: "ván trong event" nhận ra bằng extras tiền tố | Chuyển sang `live_ops_event IS NOT NULL` với event khai được (§3) |

## 5.5b PHA SONG SONG — ba event đang chạy gửi CẢ HAI stream (chốt owner 15/10)

Để dashboard đang chạy **không vỡ**, ba event đang có dữ liệu (Master League · Race · Endless
Treasure) sẽ **thêm** dòng live-ops mà **không bỏ** dòng funnel cũ, trong một hoặc vài đợt.

| Điều cần biết | Chi tiết |
|---|---|
| **Chỉ dòng MỐC bị gửi đôi** | Dòng mốc không mang `amount` / `localized_price` / `ad_view_id` → **không thể** đếm đôi tiền, tài nguyên, doanh thu hay ad |
| **Các stream khác KHÔNG thêm dòng nào** | `Stamp` chỉ điền thêm cột vào dòng đang gửi. Resource / ad / iap / offer / custom / level: **số dòng y nguyên** |
| ⚠ **Một chart đọc MỘT stream** | Chỗ duy nhất có thể đếm đôi là "bao nhiêu người tham gia" nếu UNION hai stream mốc. Đừng UNION trong pha này |
| **Biết event đã chuyển hay chưa** | `sdk_version >= '1.5.0'` **và** có dòng `f_sdk_live_ops_data` của `live_ops_event` đó |
| **Hai tên một giá trị** | `league_id` = `live_ops_cycle_id`, `detail.league_stage` = `live_ops_stage`, `endless_treasure_cycle_id` = `live_ops_cycle_id`… — tiện làm **phép kiểm chéo** trong pha này |
| ⚠ **Cụm UI tách dòng** | Khi event truyền ngữ cảnh, số **dòng** cụm tăng (tách theo đợt/mốc) dù **tổng lần hiện không đổi**: `SUM(count)` đúng, `COUNT(*)` vống lên |

Bỏ stream cũ cần đủ bốn điều kiện (ghi trong doc từng event): dashboard mới khớp số trọn một đợt ·
loader đã ký · bỏ **đúng ranh giới đợt** · ghi mốc era.

⚠ Dòng funnel cũ **có** cột `action` (tên giá trị khớp sáu action của bản này), nhưng `priority` đi
kèm thì mỗi event một bảng mã riêng — xem bảng đo ở §2.1. Nên UNION hai thời kỳ thì nối theo
`action`; log mới **không có** cột `priority` (số mốc nằm ở `live_ops_stage`, chỉ trên dòng
`milestone`/`claim`), nên map `priority` cũ → `live_ops_stage` **chỉ cho dòng `milestone`**.

⚠ Có một event live-op **không nằm trong sáu event ở §9** và dùng hẳn bộ action riêng:
`event_daily_challenge` chạy `unlock` · `popup_open` · `makeup_ad` · `day_start` · `day_win` ·
`day_lose` · `milestone_claim` — **bảy** giá trị, không cái nào thuộc sáu action của bản này. Xem
§8 Q7.

---

# 6. Việc phía loader

1. **Ký event id mới `f_sdk_live_ops_data`** + seed mapping rule. SDK **không publish** tới khi rule
   lên — đúng thứ tự đã làm với cụm quảng cáo v2.
2. **Nhận 4 param `live_ops_*`** trên 7 stream ở §1 (additive), **lên cột thật** — các bạn đã đồng ý
   14/10 (Q1). Nhắc lại để khỏi phải tranh lại về sau: đây là bốn chiều lọc chính của mọi dashboard
   event, và là **cột toàn fleet** chứ không phải cột riêng từng event.
3. **Nhận 4 field** `live_ops_*` trên `f_sdk_level_data` (§3).
4. **Giữ nguyên quy ước "vắng = `NULL`"** như hiện nay, và **đừng trộn hai dạng** `NULL`/`''` trong
   cùng một cột giữa các stream. Phía đọc tự chuẩn hoá bằng `ifNull` (§5.2) — không xin đổi gì, chỉ
   xin nhất quán.
5. `f_sdk_ui_impression` **chưa ký** — ký luôn cùng chuyến thì không sinh era.
6. Ghi vào catalog bốn luật đọc ở §4 và bốn ca biên ở §5.4. Đặc biệt: **đừng COALESCE mốc khoảng
   cách về 0**.
7. Ghi mốc era ở §5.5.

# 7. Việc phía visualizer

1. Dựng **hai** view nền `dim_live_ops_cycle` + `dim_live_ops_round` từ §5.1.
2. Dựng **một** bộ panel template theo §5.3, tham số hoá bằng `live_ops_event` +
   `live_ops_cycle_id` — không hard-code tên event nào.
3. **Trong pha song song (§5.5b): một chart đọc một stream, đừng UNION hai stream mốc.**
4. Chốt với bên mình cách hiển thị bốn ca biên §5.4 (nhất là dòng `cycle_id` rỗng: để riêng một rổ,
   đừng trộn vào đợt gần nhất).

# 8. Câu cần hai bên trả lời

| # | Câu hỏi | Ảnh hưởng |
|---|---|---|
| ✅ Q1 | **ĐÃ TRẢ LỜI 14/10: ĐƯỢC** — bốn param lên cột thật trên cả 7 stream. Đây là điều kiện nền của cả bản này. | Nếu phải nằm blob thì ba filter chính của dashboard (event / đợt / mốc) không pushdown được — cả thiết kế này mất phần lớn giá trị |
| Q2 | `f_sdk_live_ops_data` có cần thêm field nào cho phía đọc số không? | Thêm bây giờ rẻ, thêm sau là era |
| Q3 | `f_sdk_ui_impression` ký luôn cùng chuyến chứ? | Nếu để sau thì khi ký sẽ sinh era trên chính event vừa ký |
| Q4 | Dòng `live_ops_cycle_id` rỗng xử lý thế nào ở phía đọc? | Quyết định cách client báo ca "chưa biết mã đợt" |
| ✅ Q5 | **ĐÃ TRẢ LỜI 14/10: KHÔNG nhân bản.** `live_ops_enter_ts` chỉ nằm trên log live-ops; chart nào cần nhóm countdown thì join theo `(user, live_ops_event, live_ops_cycle_id)` | — |
| Q6 | Era ở §5.5: cắt theo **mốc bản SDK** — bên mình đang thêm `sdk_version` vào **mọi dòng log** (xem dưới). Các bạn cần thêm gì để dùng được nó? | Quyết định cách bên mình ghi mốc |
| 🟡 Q7 | **Daily Challenge** — đối chiếu xong ở **phụ lục C**: lệch 4 chỗ, và có **1 lỗ thật** của hợp đồng nếu lượt chơi hằng ngày không đi qua Level API. Chờ owner của DC trả lời 3 câu ở phụ lục D | Nếu có thì sáu `action` ở §2.1 không đủ, phải chốt thêm trước khi ký — thêm sau là era |

**Kèm chuyến này: `sdk_version` trên mọi dòng log.** Phiên bản **module BigData** sinh ra dòng đó.
Nó biến mọi câu hỏi era thành một phép lọc (`WHERE sdk_version >= '1.4.3'`) thay vì suy từ
`app_version` của game — mà suy kiểu đó sai âm thầm, vì một bản game có thể vá SDK giữa đường. Đây
cũng là câu các bạn đã hỏi ba lần trong đợt quảng cáo v2 ("bản Mediation/SDK nào đã lên tới đâu").
Phiên bản của **module khác** (Mediation, GameData…) không đi đường này — sẽ gửi một dòng property
mỗi phiên, bàn sau.

---

# 9. Phụ lục A — sáu event hiện tại phải sửa gì

| Event | Việc |
|---|---|
| **Master League** | Chuyển `eligible`/`open`/`join`/`complete`/`claim` sang log mới; `league_id` → `live_ops_cycle_id`; mốc Stage đi `action = milestone` thay vì đọc từ `detail.league_stage`; log level khai 3 field (có bộ level riêng); `cycle_start_ts` hết treo vì đã có `DeclareCycle` |
| **Race / Go-Kart** | Bảng đấu từ `cycleId` → `live_ops_round_id`, đợt tuần thành `live_ops_cycle_id` (đúng nghĩa); `race_attempt` → bỏ, dùng `level_attempts_since_prev_step`; `race_stage` → bỏ, dùng `live_ops_stage` trên dòng level; log level khai 4 field |
| **Adventure** | **Thêm** dòng live-ops (trước thiết kế không có funnel): `eligible` + `milestone` mỗi mốc. Log level **không** khai event (chạy chồng) |
| **Sky Path** | Như Adventure; `path_attempt` → bỏ. `path_stage` chỉ bỏ được nếu Sky Path KHÔNG chạy chồng event khác (§3) — chạy chồng thì giữ key riêng |
| **Endless Treasure** | `endless_treasure_cycle_id` → `live_ops_cycle_id`; bậc Stage đi `milestone`; giữ `endless_treasure_progress` làm chiều riêng |
| **Card Collection** | Chưa code nên sửa doc là xong: `card_col_season_id` → `live_ops_cycle_id`; set → `live_ops_stage` **cộng dồn** (phase 1 set 7 = 7, phase 2 set 7 = 22) nên "số set hoàn thành" thành `max(live_ops_stage)`; giữ `card_col_phase`/`_set`/`_tier`/`_variant` làm chiều riêng |

# 10. Phụ lục B — những thứ CỐ Ý không chuẩn hoá

| Không làm | Vì sao |
|---|---|
| `live_ops_progress` (điểm/tiến độ dạng số) | Adventure là *điểm*, ET là *độ sâu bậc*, Card là *số set*, Race là *số ván thắng*. Một cột cho bốn nghĩa thì không so chéo được — đúng lý do vừa bỏ hai cột `win_streak`/`lose_streak` |
| `live_ops_attempt` do game khai | Mỗi game định nghĩa "lần thử" một kiểu. Thước đo fleet để SDK tự đếm (§4) thì game không khai sai được |
| `live_ops_trigger` (vì sao cửa vào bật) | Chưa chart nào hỏi. Event nào cần thì dùng key riêng; khi có chart thật thì nâng lên cột sau |
| Event/đợt trên log level của event chạy **chồng** | Một ván thuộc nhiều event thì khai là bịa (§3) |

# 11. Phụ lục C — Daily Challenge lệch chỗ nào (trả lời Q7)

⚠ Module Daily Challenge **không nằm trong repo SDK**, nên đối chiếu dưới đây dựa trên **danh sách
7 action visualizer đo được trên kho**, không phải trên code.

| Action của DC | Khớp gì | Vấn đề |
|---|---|---|
| `unlock` | ≈ `eligible` | **Không chỉ là tên.** `eligible` phải bắn **mỗi phiên** khi user còn đủ điều kiện (nó là mẫu số — §2.1). `unlock` nghe như khoảnh khắc MỘT LẦN, bắn kiểu đó thì từ đợt 2 mẫu số gần như rỗng (bug Master League G1) |
| `popup_open` | = `open` | Chỉ là tên |
| `makeup_ad` | **không khớp** | Đây là **một lần xem ad**, không phải mốc tiến độ → thuộc stream ad (`adWhen` = event + 4 param), lượt được bù là **dòng resource**. Để trong funnel là dùng funnel làm **máy đếm sự kiện**, điều `TrackingGuide` cấm thẳng |
| `day_start` · `day_win` · `day_lose` | **không khớp** | **Lượt chơi + kết cục, lặp lại trong đợt** → thuộc **stream level** (`live_ops_event` + `live_ops_stage` = ngày thứ mấy), nơi đã có `duration` / `boostersUsed` / `failReason` / `levelProgress` + ba cặp mốc SDK tự đếm. Nhồi vào log mốc là mất hết |
| `milestone_claim` | gộp `milestone` + `claim` | Gộp là mất phân biệt **"đạt mốc"** vs **"đã nhận quà"** — đúng cảnh báo đã phải viết riêng trong doc Adventure và Card Collection |

**Lỗ thật của hợp đồng:** sáu `action` không có mốc nào cho *"một lượt chơi và kết cục"*, vì phần đó
được giả định nằm ở stream level. Nếu thử thách hằng ngày của DC là mini-game **không** đi qua Level
API thì nó không có chỗ đứng.

| | Cách | Đánh giá |
|---|---|---|
| **A** | DC dùng **Level API** cho lượt chơi hằng ngày | **Khuyến nghị.** Entity level của SDK nghĩa là "một LƯỢT CHƠI", không phải "màn chapter" — Master League đã dùng đúng thế cho bộ level riêng của mùa |
| B | Thêm 3 action `attempt_start` / `_win` / `_lose` | Phản đối: biến log mốc thành log sự kiện lặp (van lọc vô nghĩa), và trùng hẳn chức năng stream level |

**Ba câu cần owner của DC trả lời:**

1. **Đợt là gì** — mỗi ngày một đợt độc lập, hay một mùa gồm N ngày có mốc tích luỹ? (quyết định
   `live_ops_cycle_id` là ngày hay mùa, `live_ops_stage` là ngày thứ mấy hay mốc thứ mấy)
2. **Lượt chơi hằng ngày có đi qua Level API không?** (quyết định A hay B)
3. **`makeup_ad` bù cái gì** — một lượt chơi, hay một ngày đã bỏ lỡ? (quyết định dòng resource ghi gì)

**Thứ tự chuyển:** DC đi **sau cùng**, sau 6 event ở phụ lục A. Nó đang chạy và đang có dữ liệu, lại
là event duy nhất cần quyết định về *hình dạng* chứ không chỉ đổi tên — nên để hợp đồng chạy thật ít
nhất một đợt rồi mới chuyển.

---

# 12. Phụ lục D — API phía game (để hai bên biết client sẽ gửi gì)

```csharp
// 1. Khai đợt một lần, khi data đợt về
FalconBigDataController.LiveOps.DeclareCycle(
    liveOpsEvent: "card_collection", cycleId: seasonId,
    cycleIndex: seasonIndex, cycleStartTs: startMs, cycleEndTs: endMs);

// 2. Bước vòng đời — KHÔNG truyền stage (nó không phải mốc)
FalconBigDataController.LiveOps.OnStep("card_collection", LiveOpsAction.Eligible);
FalconBigDataController.LiveOps.OnStep("card_collection", LiveOpsAction.Open);

// 3. Mốc — stage là số mốc
FalconBigDataController.LiveOps.OnStep("card_collection", LiveOpsAction.Milestone, stage: 7);

// 4. Đọc, để truyền sang các stream khác
var cycle = FalconBigDataController.LiveOps.CurrentCycleId("card_collection");
```

Năm cặp mốc khoảng cách và `live_ops_enter_ts` do **SDK tự đóng** — game không truyền, không set
được.
