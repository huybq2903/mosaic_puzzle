# Loader trả lời 12 việc đọng — ghi sổ, và ba chỗ luật đọc phải sửa (09/11/2026)

> Thư của họ: `loader-v3-work/ticket9/Reply-Loader-SDK-Backlog-0911.md` (repo kho).
> Thư trả lời của bên mình: `loader-v3-work/ticket9/Reply-SDK-Loader-Backlog-0911.md`.
> File này là **bản ghi phía SDK**: cái gì đóng, cái gì thành việc, và **ba chỗ luật đọc của kho đang sai**.
> Luật nhà: không chép số liệu nội bộ (tên game, package id, app version của game, số dòng/user) vào doc SDK.

## 0. Đóng được gì — cả bốn cửa chặn ship đều mở

| Việc | Trước 09/11 | Sau |
|---|---|---|
| Ký `f_sdk_ui_impression` | 🔴 chặn Ui API + **cả bốn doc event live-op** | ✅ **đã ký 06/11**, nhận cả **bốn** chỗ bên mình nói chính xác lại |
| Ký `f_sdk_level_action` | 🔴 chặn — bên mình đã hứa không code trước khi ký | ✅ **đã ký 06/11** |
| Rule mapping `f_sdk_ad_show_attempt` + `f_sdk_ad_load_stats` | 🔴 *"game không được ship bản gửi hai event này"* | ✅ **rule đã chạy từ cuối tháng 9** — cửa chặn này **chưa từng tồn tại** suốt tháng 10 |
| Ký `currency_class` + tập 9 giá trị | 🔴 chặn 1.7.0 | ✅ **đã ký 06/11**, kho nhận hạng client |

🔴 **Phải ghi lại một cái sai của bên mình**: bảng README đã treo *"chờ loader seed mapping rule, game không được ship"* trên
`Request-Loader-AdEventV2.md` **từ 23/09 tới 09/11** — trong khi rule đã lên kho **23–24/09**, tức gần như cùng ngày. Suốt bảy tuần
bên mình vừa báo sai một cửa chặn ship, vừa kê nó đứng đầu danh sách *"mất gì nếu chờ"*. Thư của họ đến chậm, nhưng **bên mình cũng
không hỏi lại lần nào**. Bài học đem vào lệ làm việc: một mục 🔴 *"chặn ship"* treo quá hai tuần thì phải **tra dữ liệu** chứ không chờ thư —
ở đây một câu đếm dòng `f_sdk_ad_show_attempt` là đủ.

Bốn chỗ nhận thêm trên `f_sdk_ui_impression` (họ sửa catalog §17.1 theo cả bốn):

1. `impression_ad_not_ready` phát **KÈM** `impression`, là **tập con**; luật đọc *"không cộng ngang `ui_action`"*.
2. Trường level trong khoá tên **`current_level`**, giá trị là **tiến độ**, chụp lúc bề mặt hiện.
3. Đo phần mất: họ **bỏ** đề nghị dòng `lost` mang số lần mất, **ký hình dạng rẻ của bên mình** — một dòng `ui_action = 'flush_lost'`,
   `count = 1`, **không `surface_id`**, đọc là *"số phiên có lượt xả bị mất"*.
4. `Booster.Register` nửa sau đi cùng `DeclareCurrency` — hạng `booster` đã nằm trong tập 9 đã ký.

⚠ **Bẫy lúc code mục 3**: dòng `flush_lost` **không có `surface_id`**. Nếu param Ui chạy `CheckNonBlank` trên field đó thì null hoá
`"Unknown"` và dòng đo-phần-mất tự biến thành một bề mặt giả — đúng thứ vết sẹo bên mình đang đi xoá. Phải kiểm trước khi dựng cụm Ui.

## 1. Bảy câu của họ — trả lời của bên mình

| # | Câu | Trả lời | Thành việc gì |
|---|---|---|---|
| 1 | `pay_method` trên `f_sdk_level_action` | ✅ **nhận**, tuỳ chọn, tập đóng `ad`/`currency`/`inventory`/`free`, **chỉ gửi khi `accepted`**, không biết thì **vắng** | vào bản dựng `f_sdk_level_action` |
| 2 | Tên bốn field hạng race/league | ✅ theo **tiền tố `live_ops_`** của họ, không đổi chữ nào | vào bản dựng hạng |
| 3 | Số bản của era | ✅ **1.6.0** | — |
| 4 | `live_ops_level_*`: vắng hay 0 | 🔴 **vắng là ý định** — và luật đọc của họ **sai độc lập** với chuyện đó | họ sửa luật đọc + 3 phép tra |
| 5 | Tên game Boat Rush (A7) | ⏸ bên mình không có; A6 của họ cho thấy event **chưa chạy** | **đóng A7**, mở lại khi event lên |
| 6 | Không gửi `current_level_id` khi data rỗng | ✅ **nhận — lỗi SDK thật** | **Level Core**: chặn id khi data rỗng |
| 7 | Bốn từ phía tiêu `FResourceWhen` | ✅ **ký cả bốn**, giữ nguyên tên | +4 hằng số, đi cùng `DeclareCurrency` |

### Vì sao nhận `pay_method` (chứ không gạt là trùng dòng tài nguyên)

Hai giá trị `ad` và `free` **không sinh dòng tài nguyên nào**. Thiếu field này thì *"hồi sinh bằng xem QC"*, *"hồi sinh bằng vàng"* và
*"hồi sinh miễn phí"* là **ba thứ không phân biệt được** từ kho — hai cái sau join được qua `play_turn_id`, cái đầu không có gì để join.
Đường join lý thuyết qua `ad_view_id` thì chính kho vừa đo: gần như **chưa tồn tại** trên thực tế.

⚠ Ràng buộc đã gửi kho ghi vào catalog: `pay_method = 'ad'` **không** bảo đảm có dòng `f_sdk_ad_view` (QC hỏng giữa đường mà game vẫn
thưởng bù). Đếm *"số lần hồi sinh bằng QC"* đọc field này; đếm *"số QC đã chiếu"* đọc stream ad. Không suy cái này ra cái kia.

### Vì sao tiền tố `live_ops_` là đúng — mạnh hơn lập luận của họ

Họ nói `score` trần *"hay bị game đặt trùng"*. Soi code thì nặng hơn: **`score` đã là field thật** của dòng level
(`LevelParam.score`, `LevelProgressParamV2.score`). Nên `score` trần trên dòng live-op **trùng tên một cột đang chạy ở stream khác**, và
ai gộp hai stream sẽ thấy một cột `score` mang hai nghĩa. Tiền tố cũng khớp bộ field đang có (`live_ops_enter_ts`,
`live_ops_level_passes_since_enter`) nên không sinh ngoại lệ.

ℹ️ Cảnh báo trùng key của 1.6.0 (`FExtraMeta`) phủ luôn ca này: sau khi bốn field lên, game còn gửi `live_ops_score` qua `extraMeta` sẽ
bị nhắc **ngay lúc tích hợp** thay vì mất dữ liệu im lặng theo luật first-wins.

## 2. 🔴 Ba chỗ luật đọc phải sửa — hai chỗ là lỗi doc của SDK

### 2a. `0` ở cặp `live_ops_level_*` — luật của kho đảo nghĩa ô dễ gặp nhất

Kho đang đọc *"`0` trên SDK ≥ 1.5.6 = event không có bộ level riêng"*. Trên code **đã sửa**, nhánh `opened` của
`LiveOpsLogService` gán thẳng `0` cho **event CÓ bộ level riêng**, nghĩa *"vừa vào đợt, chưa chơi ván nào"* — số thật. Nên luật ấy đọc
**dòng đầu tiên của mọi event có bộ level riêng** thành *"không có bộ level riêng"*.

⚠ **Và `opened` không phải `sub_event = 'open'`** — chỗ này lúc đầu em viết lỏng tay rồi phải soi lại `ResolveSlot`: cờ bật khi SDK
**chưa có ô sổ mốc** cho cặp (event, đợt) rồi mở ô mới, **bất kể bước ấy tên gì**. Nên dòng `0` hợp lệ có thể là `eligible`/`join`/
`milestone`; và ngược lại, **dòng level cũng mở được ô** (`ReportLevel` gọi `ResolveSlot` rồi bỏ cờ đi), nên một dòng `open` đi **sau**
một dòng level của cùng đợt thì **không** `opened` và mang số thật khác 0. Phép nhận dạng phía kho là
**`min(created_date)` trong mỗi (user, event, `live_ops_cycle_id`)** — đúng idiom họ đã dùng cho C2/C8.

Luật đúng (đã gửi họ): **vắng** = không có bộ level riêng (đọc cặp toàn cục, ghi *"chặn trên"*) · **`0` ở dòng đầu tiên của
(user, event, đợt)** = có bộ riêng, chưa chơi ván nào · **`0` ngoài dòng đầu tiên** = đáng soi.

Và ba phép tra để biết dòng `0` của họ thuộc loại nào: (1) có phải dòng đầu tiên của đợt — nếu đúng thì **không có lỗi nào cả**;
(2) có phải dòng **test** — ngày họ soi đúng ngày máy test của bên mình chạy, mà log test chỉ sang bảng riêng từ 02/11; (3) **cờ dính** —
`declaresLevels` ghi theo EVENT và **sống mãi**, nên một dòng level lỡ khai event là cờ bật vĩnh viễn; nếu dòng lỡ khai là Fail/Quit thì
cờ bật mà bộ đếm không nhích ⇒ `0` ở mọi bước. Nhánh (3) là **lỗi tích hợp của game** (phạm hợp đồng §3), là một dấu DQ — **không phải
một luật đọc**.

✅ **Owner đã xác nhận 09/11: dòng đã phát hành dừng ở 1.5.5.** Các bản **1.5.6 · 1.5.7 · 1.5.8 · 1.6.0 chưa phát hành**, nên:
- era `>= 1.6.0` kho viết sẵn là **đúng và an toàn**, chỉ là **chưa có dòng nào thoả** — đừng chờ thấy số mới tin là luật chạy;
- dòng mang `sdk_version` 1.5.6–1.5.8 mà kho thấy **không phải fleet**: chúng từ **máy test / build dựng trực tiếp từ nhánh**, nên
  **không được dựng luật đọc** trên tập đó — và đó đúng là chỗ phát sinh câu hỏi thứ tư của họ;
- bên mình nợ kho **bản + ngày** lúc phát hành, để họ ghi mốc *"era bắt đầu có dòng từ ngày…"*.

⚠ Ghi lại cách suy sai của chính mình ở bước này: em từng định suy *"1.5.6 chưa phát hành"* **từ việc commit bump chưa push**. Suy thế là
sai, vì **publish là một việc khác với push** trong repo này (lịch sử có *"1.3.7 publish khi fix xoay-id còn chờ test"*, *"dời khỏi 1.3.5
đã publish 20/08"*) — và kho đang thấy dòng 1.5.6 trên hai game, tức bản đó tới được máy nào đó bằng đường khác. Kết luận đúng, nhưng
phải đến từ owner chứ không từ git log.

### 2b. 🔴 `level_data_id` **không phải** id của server — doc SDK viết sai, kho dựng luật lên chỗ sai đó

XML-doc của `ALevelParamV2.levelDataId` viết *"băm do **GAME SERVER** đặt, client chỉ echo lại nguyên văn"*. **Sai.** Đường thật đi một
chiều **LÊN**:

```
CSLevelReady.md5LevelData = FLevelManager.GetLevelDataMd5(level)
                          = Md5Utils.GetMd5First5Char(GetLevelData(level))   // CLIENT tính
FLevelManager.currentMd5LevelData = csLevelReady.md5LevelData                // rồi mới .Send()
CurrentLevelDataId  ⇒  levelDataId  ⇒  level_data_id
```

Soi **mọi** chỗ gán `md5LevelData` trong SDK: không có chỗ nào nhận giá trị **từ** bản tin của server. Hệ quả:

1. `level_data_id` **cũng mang lỗi `1B2M2`** (băm của chỉ `levelData` ⇒ data rỗng ra đúng chuỗi đó).
2. Nó **không phải phép kiểm độc lập**. Câu của họ *"`level_data_id` của server sẽ cho câu trả lời thẳng khi phủ đủ"* không đứng được:
   hai field **cùng hàm băm, cùng 5 ký tự base64 = 30 bit**, chỉ khác đầu vào (`data` so với `data + param`).
3. Việc join sang catalog level của backend **vẫn làm được**, nhưng vì lý do khác: server **có** chuỗi băm này (client gửi lên trong
   `cs_level_ready`/`cs_level_start`) nên backend index theo nó là nối được. Cần một id **thật** của server thì đó là việc mới.

✅ Doc đã sửa tại chỗ, kèm dòng *"Sửa 09/11"* nói rõ bản đầu sai gì — để lần sau ai đọc field này không dựng lại cùng một luật.

### 2c. `1B2M2` — lỗi SDK, và chặn phải đặt đúng chỗ

- `Md5Utils.GetMd5First5Char(input)` = `Convert.ToBase64String(MD5(input)).Substring(0, 5)`; `Normalize(null)` trả `""` ⇒ **null và
  rỗng cho cùng kết quả**; base64(MD5(`""`)) bắt đầu bằng **`1B2M2`**.
- `GetLevelData(level)` trả `""` khi không có file level từ server **và** ở nhánh `catch` ⇒ game không dùng level server gửi **cùng một
  id cho mọi màn**, ai `GROUP BY` cột đó gộp cả game thành một màn.

Hai ràng buộc của bản sửa:

- ⚠ **Không sửa trong `Md5Utils`** — hàm đó còn tính băm cho `CSLevelStart` / `CSLevelResult` / `CSLevelVerifyReq`, là hợp đồng với game
  server. Chặn đặt ở **nơi xuất giá trị cho BigData** (`FLevelManager`).
- ⚠ **Chặn theo `levelData` rỗng**, không theo *"chuỗi ghép rỗng"*: data rỗng + param có chữ ra id của **param**, dùng chung mọi màn cùng
  tuning, và lẫn đúng vào tập *"game dùng lại thiết kế màn"* nên không ai soi ra.

Cả hai field đã là `[FKey(RemoveIfNull = true)]` nên null là **vắng khỏi dây**, không phải `"Unknown"` — không cần thêm gì ở phía BigData.

## 3. Bốn từ phía tiêu — ký, và vì sao vocab lệch một bên là lỗi thiết kế

`play_cost` · `booster_use` · `upgrade` · `revive` vào `FResourceWhen`, tập đóng.

Kiểm lại vocab: phía tiêu **chỉ có** `shop` và `create_fee` (cộng `iap` là đường tiền thật), còn lại đều là phía **nhận**. Vocab lệch một
bên thì game **không có chữ để ghi**, và cái gì không có chữ thì rơi về `Unknown` — đúng cơ chế kho đo được *"phía tiêu phủ rất thấp"*.
Đây là lỗi của bên mình lúc đặt vocab, không phải lỗi game không chịu gửi.

Hai ghi chú chống đọc lẫn:
- **`booster_use` (dòng tài nguyên) khác `action = 'booster'` (`f_sdk_level_action`)**: món bị tiêu so với hành động trong ván. Một lần
  dùng booster sinh **cả hai** dòng, nối bằng `play_turn_id` — **đừng cộng hai nơi** để ra *"số lần dùng booster"*.
- **`play_cost` là phí VÀO chơi**, nên trên cùng một lượt nó đứng cạnh `level_reward` (phía nhận), không loại trừ nhau.

### `Unknown` mặc định của `resourceWhen` / `resourceWhere`

Đồng ý về nguyên tắc là nên **vắng** thay vì `"Unknown"`, nhưng đó là **đổi nghĩa một cột đang chạy** (`event_when` từ `'Unknown'` thành
NULL) nên phải đi kèm era, và phải làm **sau** khi bốn từ trên đã lên: đổi mặc định trước khi có vocab thì chỉ biến `Unknown` thành NULL,
không ai đọc được hơn. Đã hỏi kho NULL ở cột đó có vỡ chart nào không.

## 4. Việc sinh ra từ thư này

Tất cả đã **làm xong trong ngày 09/11**. Số version owner đã chốt cuối ngày: **Level Core 1.3.13** · **BigData 1.7.0** (khối "Chưa phát hành" gộp thẳng vào 1.7.0 vì bản đó cũng chưa publish — không đẻ thêm một mốc không có gì sau nó).

| # | Việc | Module | Trạng thái |
|---|---|---|---|
| 1 | Chặn `current_level_id` + `level_data_id` khi `levelData` rỗng | **Level Core 1.3.13** | ✅ **code xong và ĐÃ COMPILE** — `Falcon.Modules.Level.Core.Runtime.dll` build lúc 11:12 (sau mọi sửa), chuỗi `HasLevelData` có trong dll, **0 lỗi** trong đường dẫn module. ⚠ Module không có bộ test nên luật mới kiểm bằng đọc code + tính tay giá trị băm, không có test tự động canh |
| 2 | +4 hằng số phía tiêu `FResourceWhen` | BigData | ✅ |
| 3 | `DeclareCurrency` + sổ mã booster | BigData | ✅ — hai sổ để **static**, không `MySingleton`, vì param không có DI nên đi qua singleton trong `CorrectValues` là đẩy mọi test của đường đóng dấu vào nhóm cần Unity runtime |
| 4 | Bốn field hạng `liveOps*` trên dòng live-op | BigData | ✅ |
| 5 | `f_sdk_level_action` + `pay_method` | BigData | ✅ |
| 6 | Cụm Ui: `current_level` + `trigger` vào khoá, `ui_action` thứ ba (**kèm**, không thay), dòng `flush_lost` | BigData | ✅ — và bẫy `CheckNonBlank` ở §0 **đã gỡ**, kèm test canh |

🔴 **Hai cái bẫy tự bắt trong lượt code**, ghi lại vì cả hai đều là *"tính năng trông như đã có mà thực ra là số 0"*:
- **Dấu đo-phần-mất của cụm Ui chưa từng nổ được.** `FDataPool.Save` chỉ ghi cache RAM (đĩa chỉ được ghi ở `OnPostStop` và nhịp 5
  phút) — mà ca cần đo CHÍNH LÀ ca `OnPostStop` không chạy. Và `IPioneer.OnPreContinue` **không chạy ở cold start**
  (`AppFlowService.CheckGameContinue` return sớm khi `!_gameStop`), tức đi đường đó là cài một cái đo không bao giờ đo được ca của nó.
  Nay sync tay + soi dấu một lần cho mỗi tiến trình. ⚠ Số đo là **chặn dưới**.
- **Bản `MySingleton` của sổ hạng tiền làm 4 test quan trọng nhất thành ENV** — nhóm chỉ được *compile*, chưa từng *chạy*, đúng nhóm
  đã trả giá ở 1.5.6. Đổi sang static thì chúng chạy trong ~30 giây.

⚠ **Số version của khối chưa phát hành phải là 1.7.0**, không phải 1.6.1: catalog của loader đã ghi *"chờ 1.7.0"* cho
`currency_class_source = 'client'`. Chọn số khác thì phải báo họ sửa catalog.

## 5. Còn chờ kho

1. **§2a**: chạy ba phép tra rồi sửa luật đọc (cần sửa **dù** kết quả tra ra sao). Bên mình nợ họ danh sách bản 1.5.6–1.6.0 đã phát
   hành kèm ngày.
2. **§2b**: đếm `1B2M2` trên `level_data_id`, sửa §12 + mô tả cột — field do **client** tính.
3. Ghi ràng buộc *"`pay_method = 'ad'` không bảo đảm có dòng `f_sdk_ad_view'"*.
4. NULL ở `event_when` / `event_where` có vỡ chart nào không.
5. Hai câu đọc còn mở, **không chặn gì**: `AdFormat` F1/F2/F3 · `AdCloseOrphan` phần 3,4%/3,1% dòng close không có impression nào
   (kho nói *"chưa soi tiếp"*, và đây **không** phải lỗi preload).
6. `ad_view_id` trên dòng tài nguyên: kho **chưa đo được** vì game gần như chưa gửi — không phải việc của SDK, là việc tích hợp ở game.

---

## 6. Vòng hai — kho trả lời thư `Reply-SDK-Loader-Backlog-0911.md` (09/11, `…-0911-b.md`)

Kho làm xong cả bảy mục bên mình nhờ và sửa catalog theo cả ba chỗ luật đọc. Ghi sổ:

| Mục | Kết quả | Việc phía SDK |
|---|---|---|
| §4 ba phép tra dòng `0` | **Không lỗi.** Chỉ event có bộ level riêng mới mang cặp `live_ops_level_*`, event khác vắng hoàn toàn. Dòng `0` hoặc là dòng đầu đợt, hoặc đi kèm `attempts = 0` ở user đã có dòng level khai đúng event (bước trước ván event đầu tiên) | — |
| §6 `1B2M2` trên `level_data_id` | **0 dòng** trong 30 ngày. Lỗi `1B2M2` chỉ từng lộ ở `current_level_id`; game không dùng data server thì cũng không gửi `level_data_id` | — (bản sửa Level Core 1.3.13 vẫn đúng cho cả hai field) |
| §1 `pay_method = 'ad'` | Đã ghi catalog | — |
| §7 NULL ở `event_when`/`event_where` | **Không vỡ chart nào**: gold đã gộp rỗng lẫn `Unknown` về `UNKNOWN` | ✅ **Mở đường** cho việc đổi mặc định `resourceWhen`/`resourceWhere` từ `"Unknown"` sang vắng, xếp sau bản currency (1.7.0 — đã publish). Khi phát hành phải gửi kho **bản + ngày** để họ ghi era cột silver |
| §3 1.5.6–1.6.0 chưa phát hành | ⚠ **Không khớp dữ liệu** — xem 6a | **Owner phải trả lời** |
| `FResourceWhen` + A7 | Đóng cả hai | — |
| §8.1 `flush_lost` chặn dưới | Đã sửa luật đọc | — |

Ghi chú kho để bên mình biết: gold `live_ops_step` vẫn **gộp "vắng" về `0`** (quyết định owner kho 29/10) — ở tầng gold hai nghĩa
tách bằng **tên event**, không bằng con số. Silver vẫn giữ đúng hai nghĩa.

### 6a. ~~🔴 Dòng `sdk_version = 1.5.6` đầu tiên trên kho **có trước** khi repo này có số 1.5.6~~ — ❌ **RÚT LẠI 09/11, kết luận SAI**

> **Đính chính (kho trả lời `-d`):** không có bản "1.5.6" nào chạy trước SDK. Mốc *"21/10"* là **một máy dev đặt đồng hồ lùi 8 ngày**:
> máy ghi 21/10, **server nhận 29/10**. Server nhận 1.5.6 lần đầu **26/10 10:32 UTC** trên game test, khoảng 45 phút sau khi repo commit
> số đó (16:48 giờ VN). Mọi cặp (game, bản app) mang 1.5.6 đều **qua** phép tách `f_sdk_ad_load` mà chính mục này đề ra.
> **Lỗi suy luận của bên mình:** đem ngày commit so với `created_day` mà không hỏi cột đó theo đồng hồ nào — nó theo **đồng hồ máy**
> (trong cửa sổ tin cậy lùi tới 10 ngày). Một mốc *"sớm nhất"* là đúng loại số mà một máy lệch giờ duy nhất làm sai được.
> **Luật rút ra:** hỏi *"bản X lên kho từ khi nào"* thì đọc **`server_receive_time`**, không đọc `created_day` / `client_sent_time`;
> và đừng dựng kết luận *"có bản lạ"* trên MỘT mốc sớm nhất — chạy phép tách trước, kết luận sau.
> Phần dưới giữ nguyên để không xoá vết.


Kho thấy dòng thật (không phải log test) mang `1.5.6` từ **21/10** và `1.5.8` từ **03/11**. Đối chiếu với repo:

| Số | Lần đầu xuất hiện trong repo này | Kho thấy trên game từ |
|---|---|---|
| 1.5.4 | 20/10 | — |
| 1.5.5 | **26/10 13:40** (commit đổi thẳng 1.5.4 → 1.5.5) | — |
| 1.5.6 | **26/10 16:48** | **21/10** |
| 1.5.8 | 29/10 14:35 | 03/11 |

`sdk_version` trên dây chỉ có **một nguồn** (`SdkVersionInfoRepository` ← `BigDataVersion.VALUE`, sinh từ `package.json`). Suốt
20/10–26/10 repo này ghi **1.5.4**, và transcript làm việc cùng kỳ cũng chỉ có 1.5.4. Không repo nào khác trên máy làm việc có
1.5.6 sớm hơn.

➡ **Dòng "1.5.6" từ 21/10 là code KHÁC mang cùng số** — một bản sao SDK do bên khác tự đánh số. Đây không phải lần đầu: mục
1.5.6 của CHANGELOG đã ghi *"bản mediation trong SDK repo đi SAU bản game đang build"*. Hệ quả cho kho đúng như họ lo: `sdk_version`
không còn đủ để phân era cho 1.5.6.

**Dấu vân tay kho tự kiểm được:** event `f_sdk_ad_load` (dòng load chế độ **tách** — mặc định) và khoá `session_ad_load_index` có
từ **1.5.5 của repo này (26/10)**, hợp đồng ký cùng ngày. Bản nào mang `1.5.6` trên một game có chiếu ad mà **không bao giờ** phát
`f_sdk_ad_load` thì gần như chắc không phải 1.5.6 của repo này. ⚠ Trừ trường hợp game bật chế độ **gộp** qua remote config
(`fAdLoadLogSplit`) — khi đó chỉ có `f_sdk_ad_load_stats`, event này có từ 1.4.0 nên không làm vân tay được.

1.5.8 (từ 03/11) thì **có thể** là bản của repo này: số đó có trong repo từ 29/10. Publish lên registry không cần push git, nên chỉ
owner biết.

**Còn chờ owner:** (1) ai publish 1.5.6 / 1.5.8 tới game, (2) bản "1.5.6" từ 21/10 do ai dựng, từ code nào.

### 6b. Một dòng catalog kho vừa ghi sẽ sai ở bản sau

Kho vừa ghi *"`current_level` trong khoá cụm Ui được chụp lúc bề mặt hiện"* — đúng với **1.7.0**. Nhưng bên mình đã **bỏ** field đó
(chưa publish, xem CHANGELOG "Chưa phát hành"): mọi dòng log đã mang `maxPassedLevel` do decor chung đóng, và tên `current_level`
trùng một cột mang nghĩa khác ở stream ad/IAP. Phải báo kho kèm era khi bản đó publish.
