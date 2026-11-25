# LuaDynamic — bộ test

Chỉ dùng để test khi server chưa gửi `sc_ad_monetize_lua_script`. **Xoá cả thư mục `Samples` là gỡ sạch.**

| File | Vai trò |
|---|---|
| `Resources/LuaAdTest.txt` | Lua script mẫu (coi như server trả về): load inter + rewarded **theo thuật toán 8 của MultiCall** (`MultiCallMaxProvider8`), chỉ dùng đúng hợp đồng của spec. Danh sách id lấy từ `sc_ad_bid_floor` qua `context` (theo `adUnitIdType`); phần `CONFIG` đầu file chỉ còn tham số thuật toán, thay cho JSON CMS `moMulMaxProvider8Config` |
| `LuaAdTestInjector.cs` | Lúc mở app (BeforeSceneLoad), nạp script trên qua `SCAdMonetizeLuaScript.OnData()` và hiện bảng nút test |

## Script mẫu chạy thế nào

- **Thân script:** không load gì, chỉ khai báo hàm.
- **`OnInitMaxSuccess()` / `OnInitAdmobSuccess()`** (hàm nào tới trước, chỉ một lần): đọc id từ `context`, phân loại theo `adUnitIdType`:
  - `adUnitIdType` chứa `default` (không phân biệt hoa thường) = id default; còn lại (`h1`, `h2`, ...) = dynamic floor, sàn do Lua tính = multiplier × base. **Không** dùng multiplier để nhận id default.
  - **Thứ tự load do script quyết định**, theo tên `adUnitIdType` (số ở cuối so như số): `default_0` → `default_1` → `default_2` → ... → `default_10`; `h1` → `h2` → `h3`. Không theo thứ tự `sc_ad_bid_floor` trả về. Thác AdMob bước C vẫn chạy multiplier cao trước.
  - retry = `CONFIG.<format>.defaultRetry` / `floorRetry` của script. **Không** dùng `timeConfig` của bid floor.
  - Xin load các id default MAX, cách nhau `defaultLoadDelaySeconds` (bước A).
  - `OnInitAdmobSuccess()` thêm bước B (id default AdMob) và bật bước C. Chỉ có `OnInitMaxSuccess()` thì không có bước B, C.
- **`On...LoadSuccess` của id default:** eCPM là **base mới**. Reset các bậc floor, xin load tối đa `maxConcurrentFloor` bậc trống thấp nhất với sàn = hệ số × base.
- **`On...LoadSuccess` của bậc floor:** lấp bậc trống kế tiếp.
- **`On...LoadFail`:**
  - Bậc floor: xin lại theo lịch `retry` (vẫn giữ chỗ). Cứ `failsToDecreaseMultiplier` lần fail liên tiếp thì hệ số giảm 0.1. Đủ `maxFails` thì dừng bậc tới base mới.
  - Id default: retry theo lịch.
  - Lỗi mạng: không tính, thử lại sau 2s.
- **`On...ShowSuccess`:** bậc vừa show về hệ số config. Id default xin lại 1s sau khi ad đóng, đồng thời lấp bậc floor.
- **`IsShowInterstitial` / `IsShowRewarded`:** có ad `Ready` thì trả `true` (rewarded tính cả inter).

### AdMob: 3 bước A / B / C như thời MetaAdmob

Id AdMob lấy từ `sc_ad_bid_floor` (`mediation = "admob"` hoặc id `ca-app-pub-...`):

- **A:** MAX chạy thuật toán 8 như trên.
- **B (id AdMob `default_<n>`):** load **không sàn** lúc `OnInitAdmobSuccess()`.
  - Fail: retry backoff 1, 2, 4, … 64s.
  - Show xong hoặc mất ad: nạp lại ngay.
- **C (id AdMob `h<n>`, chỉ khi có `OnInitAdmobSuccess()`):** mỗi lần **một id MAX bất kỳ** load xong với eCPM E, script chạy một thác giá AdMob:
  - Xin lần lượt từng id, multiplier cao nhất trước, sàn = E × multiplier, mỗi lúc một request.
  - Có ad thì dừng. Fail thì xuống id kế. Id cuối cũng fail thì dừng, chờ lần MAX load sau.
  - Cùng id MAX load lại thì thác mới **thay** thác cũ. Kết quả về muộn của thác cũ bị bỏ qua, ad vẫn giữ.
  - Id floor AdMob show xong **không** tự nạp lại.

Mỗi id MAX có **bản load AdMob riêng** cho từng id floor: script truyền id MAX đó làm tham số thứ 4
`linkedAdUnitId`. Vì vậy mọi id MAX đều xin đủ các mức floor, không giẫm lên nhau (giống kho riêng theo bậc
của C#). Chỉ khi bản load của **chính id MAX đó** còn đang load (thác trước) hoặc đang giữ ad thì thác bỏ qua,
sang id kế. Chưa hỗ trợ `levels` (mỗi bậc MAX một bộ id AdMob khác nhau).

Log: lọc `[LUA][AdmobInterstitial]` / `[LUA][AdmobRewarded]`, gồm `SẴN SÀNG -> bướcB=... bướcC=...`,
`BƯỚC B (...)`, `BƯỚC C: '...' load xong eCPM=... -> thác #n`, `GỬI` / `BỎ QUA` / `DỪNG`.

## Cách test

1. Server phải trả `sc_ad_bid_floor` có `adUnitIdType` (`default_1`, `h1`, ...) cho từng id. Id không có
   `adUnitIdType` bị script bỏ qua. Log kiểm tra: `[LuaAd] bid floor (...): N id -> thêm ...` liệt kê từng id
   dạng `I:Max:default_1:x0:<id>`.
2. Thêm define `LUA_AD_TEST` vào Player Settings > Scripting Define Symbols (Android). Không có define này thì code test không được biên dịch.
3. Build bản có `ENABLE_DEBUG_LOG`, rồi lọc logcat theo `LuaAd`.
4. Dùng bảng nút ở góc trên trái màn hình:
   - **Hỏi IsShowInterstitial / IsShowRewarded:** gọi `MediationManager`, kèm `level` lấy từ nút `-` / `+`.
   - **Hỏi rồi show:** hỏi trước, nếu `true` thì show.
   - **Show Inter / Show Rewarded / Show RW (không inter):** gọi `MediationManager` như game vẫn gọi.
   - **Log danh sách ad:** in trạng thái các unit của Lua.

Test xong: gỡ define `LUA_AD_TEST`, hoặc xoá thư mục `Samples`.

## Log đúng

Log của script **giống hệt log `MultiCallMaxProvider8`** (cùng tên bước, cùng cách mô tả `DEFAULT #n adUnit=...` /
`bậc n xM adUnit=...`), nhưng tag là `[LUA][MAX algorithm 10]` (algorithm 10 = Lua):

| Thuật toán | Prefix | Lọc logcat |
|---|---|---|
| MultiCall (C#) | `[MAX algorithm 8][BƯỚC] [Inter]` | `[MAX algorithm 8]` |
| Lua | `[LUA][MAX algorithm 10][BƯỚC] [Inter]` | `[LUA][MAX algorithm 10]` |

Phiên LUA thì **không được** có dòng `[MAX algorithm 8]` nào (MultiCall không chạy).

1. `[LuaAd][TEST] giả lập sc_ad_monetize_lua_script`
2. `nạp script #<md5> — thân script đã xin 0 lệnh load` → `bid floor (nạp script): N id -> thêm ... | CÓ/KHÔNG có id AdMob`
3. `CHỐT chế độ phiên: LUA` → `MAX chế độ load: LUA` → `callback MAX init thành công` → (có AdMob: `INIT ADMOB` → `MobileAds.Initialize xong`) → `gọi OnInitAdmobSuccess() / OnInitMaxSuccess() sang Lua` → `[CHỤP CONFIG] (..., từ sc_ad_bid_floor)` → `[KHỞI ĐỘNG]` → `[LÊN LỊCH DEFAULT] (KHỞI ĐỘNG) ... lịch #n` → `[CỔNG REQUEST] CHO QUA` → `[GỬI REQUEST] DEFAULT #n ...` (+ `[LUA][Admob...] SẴN SÀNG` nếu có AdMob)
4. `[LOAD OK] DEFAULT ... ad eCPM=$X` → `-> id default load xong: base eCPM ...` → `[BASE MỚI] #1 = $X (base đầu tiên ...)` → `[RESET BẬC]` từng bậc → `[KHO]` → `[LẤP BẬC] bậc trống thấp nhất=[...]` → `[GỬI REQUEST] bậc 1..3 floor = xM * base $X` + `ĐÃ GỬI`
5. Floor fail: `[LOAD FAIL]` → `[GIẢM HỆ SỐ]` → `[GIỚI HẠN FAIL] ... còn được retry` → `[RETRY FLOOR] ... retry sau Ns, lịch #n (vẫn giữ chỗ)` → `[GỬI REQUEST] ... | bắn sau Ns`. Fail lần 5 → `[GIẢM HỆ SỐ] x1.5 -> x1.4`.
6. Default fail: `[LOAD FAIL]` → `[RETRY DEFAULT] ... retry sau Ns (lịch [...])` → `[LÊN LỊCH DEFAULT] (RETRY DEFAULT)`.
7. Show: MỘT log `[LuaAd] [AdArbitration][REWARDED|INTERSTITIAL] CHỌN AD ĐỂ SHOW tại '...'` (giống log
   AdArbitration của MultiCall): `MAX best`, `-- MAX rewarded --`, `-- MAX interstitial --`, `ADMOB rewarded best=`,
   `ADMOB interstitial best=`, mỗi ad unit một dòng (chưa có ad: eCpm=$-100), `<== MAX BEST` / `<== ADMOB BEST`,
   `=> THẮNG BID`, `=> AD SẼ SHOW`. Lọc `[AdArbitration]`; nút "Log danh sách ad" in cùng định dạng → `[LuaAd] SHOW` →
   - **DEBUG (chỉ test local):** script gắn nhãn `[x1.2->1.1]`, `[x1.5 DỪNG]`, `[B default]`, `[C x1.2 thác #3]` và
     dòng `algo=LUA10 base=$X (eCPM) base#n slot floor=b/m` qua field `debugLabel` / `context.debugSummary`. Gỡ: xoá
     khối `DEBUG` + các lời gọi `publishDebugAll()` trong script; phía C# thiếu field thì tự in `[default]` / `[floor]`. `DISPLAYED` → `[ĐÓNG AD] ... vừa show -> rời kho` → `[NẠP LẠI] lý do: đóng ad ...` → `[LuaAd] HIDDEN` → load lại 1s sau khi đóng.
8. Base mới khi có lịch retry: `[BASE MỚI] #n ... | huỷ N lịch retry floor đang chờ` → `[GỬI REQUEST]` với floor mới.
9. Lỗi mạng: `[OFFLINE] ... fail lúc offline -> KHÔNG tính ..., thử lại sau 2s`. Chỉ khi **mã lỗi** là mất mạng
   (Max `NoNetwork` / AdMob code `2`). Online mà vẫn thấy `[OFFLINE]` là sai.

**Khác log C# (do Lua không có hẹn giờ):** `[CỔNG REQUEST]` / `[GỬI REQUEST]` in lúc Lua **xin** load; lệnh có
delay thì thêm `| bắn sau Ns`. Lúc request thật bắn đi xem dòng `[LuaAd] LOAD ...` (thay cho `[CHẠY LỊCH DEFAULT]`
của C#). Base / floor ở Lua là **eCPM** (C# in revenue = eCPM / 1000). Thêm bước `[MẤT AD]` khi ad hết hạn / chiếu hỏng.

Script đã được chạy giả lập: một C# console đóng vai Unity, dùng đồng hồ ảo và mạng quảng cáo giả. Các bước base / lấp bậc / retry / giảm hệ số / show / base mới huỷ lịch retry / lỗi mạng đều khớp `MultiCallMaxProvider8`.

**Khác với C#:**
- Không có `throttled` (đang dùng 3G thì chỉ load index 0).
- Lịch retry của bậc bị đẩy khỏi nhóm không huỷ được thật, chỉ không còn giữ chỗ. Lệnh đó vẫn chạy, có ad thì giữ, fail thì lấp bậc khác.
- Lua không theo dõi mạng liên tục, chỉ thử lại 2s một lần khi gặp lỗi mạng.

**Dấu hiệu sai:**
- **`CHỐT chế độ phiên: MULTICALL`:** script chưa vào kịp trước bước khởi tạo MAX.
- **`Load... -> false` kèm cảnh báo trùng id:** id MAX đang được MultiCall / FloorPersonalization / DFPool dùng.
