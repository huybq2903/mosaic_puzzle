## 1.3.14
- **`OnLevelStart(param: LevelStartParamV2)` đưa `preBoostersUsed` vào FIELD THẬT của dòng Start** — booster chọn TRƯỚC ván. Trước
  đây `OnLevelStart` tự dựng dòng Start mà không có chỗ cho field đó; truyền object vào `param` thì key chỉ lọt lên dây qua extras,
  bỏ qua kiểm hợp lệ của field thật (số âm, sổ mã booster `Resource.RegisterBooster`). Không đổi chữ ký nào: game đã truyền object
  level param thì tự hưởng.
- **Kênh legacy `param` thôi đổ vào extras những key trùng field thật của dòng Start** (`currentLevel`, `difficulty`, `playTurnId`,
  `currentLevelId`, `levelDataId`…). Level Core tự tính các field đó nên giá trị từ object game chỉ có thể bị đè — mà vẫn in một cảnh
  báo trùng cột mỗi lần bắt đầu ván, cảnh báo giả làm dev quen tay bỏ qua cảnh báo thật. Kèm một hệ quả đúng ý: màn KHÔNG có data
  thì `currentLevelId`/`levelDataId` để trống (bản sửa `1B2M2` của 1.3.13), và trước đây một object legacy mang hai key đó có thể
  lấp chỗ trống qua extras — giờ không lấp nữa. `extraMeta` tường minh của game **không** lọc: game tự đặt tên trùng cột là lỗi thật.
- Kiểm: compile cả `Scripts/Runtime` bằng csc với dll project đã build — 0 lỗi. Module **không có bộ test**, nên chưa có test tự động
  canh hai luật trên.
----------------------------------


## 1.3.13
- 🔴 **`current_level_id` / `level_data_id` không còn gửi khi màn KHÔNG CÓ DATA** — kho bắt 09/11, đo trên nhiều game.
  `Md5Utils.GetMd5First5Char("")` trả **`1B2M2`** (băm base64 của chuỗi rỗng): một giá trị **trông hợp lệ**. Và
  `Normalize(null)` trả `""` nên **null cũng ra đúng chuỗi đó**. Game không dùng level từ server thì `GetLevelData(level)`
  trả `""` — kể cả ở nhánh `catch` — nên **mọi màn của game ấy gửi cùng một id**, và ai `GROUP BY current_level_id` sẽ gộp
  cả game lại thành **một màn** mà không thấy gì sai. Nay `CurrentLevelDataId` và `CurrentMd5LevelDataParam` trả **null** khi
  không có data; hai field bên BigData đều `[FKey(RemoveIfNull = true)]` nên null là **vắng khỏi dây**, không hoá `"Unknown"`.
  Vắng còn hơn một id chung: vắng thì người ta đi tìm, sai thì người ta dùng.
  - ⚠ Cửa đặt ở **`FLevelManager`**, KHÔNG ở `Md5Utils`: hàm đó còn tính băm cho `CSLevelReady` / `CSLevelStart` /
    `CSLevelResult` / `CSLevelVerifyReq` — hợp đồng với game server, không được đổi vì một chuyện của log.
  - ⚠ Xét **`currentLevelData` rỗng**, không xét chuỗi ghép rỗng: data rỗng + param có chữ thì băm ra id của RIÊNG param,
    dùng chung mọi màn cùng tuning — nó lẫn đúng vào tập *"game dùng lại thiết kế màn"* mà kho thống kê được, nên không ai
    soi ra.
  - ⚠ Dùng `IsNullOrWhiteSpace` chứ không `IsNullOrEmpty`: chuỗi chỉ-khoảng-trắng cũng cho một id hằng dùng chung mọi màn
    (`" "` → `chXun`, `"\n"` → `aLMp2`) — cùng một lỗi, chỉ khác chữ.
- 🔴 **Sửa doc viết sai về `CurrentLevelDataId`** (ở đây và ở bullet 1.3.12 bên dưới): nó **KHÔNG** phải *"băm DATA màn do game
  server đặt, client echo nguyên văn"*. Đường thật đi một chiều **LÊN**:
  `CSLevelReady.md5LevelData = FLevelManager.GetLevelDataMd5(level)` — **client tính** — rồi mới `.Send()`; trong SDK không có
  chỗ nào gán field này **từ** bản tin của server. Hệ quả thật của chỗ sai: phía kho đã ghi vào luật đọc của họ rằng
  *"`level_data_id` của server sẽ cho câu trả lời thẳng về trùng băm"* — **không đứng được**, vì hai id **cùng hàm băm, cùng 5
  ký tự base64 = 30 bit**, chỉ khác đầu vào. Việc join sang catalog màn của backend **vẫn làm được**, nhưng vì server **có**
  chuỗi băm này (client gửi lên) chứ không phải vì nó là id do server cấp. Chi tiết:
  `Modules/Core/BigData/docs/loader/Request-Loader-Backlog0911-ack.md` §2b.
- ⚠ **Chưa có bộ test nào trong module này**, nên bản vá được kiểm bằng đọc code + tính tay giá trị băm, không bằng test đỏ-xanh.
  Dựng rig test cho `FLevelManager` là việc riêng (singleton + IO file).

----------------------------------
## 1.3.12
- Dep: **bigdata 1.5.2** (pin cũ là 1.3.2 trong khi code đã điền `levelDataId` trên `ALevelParamV2` — field chỉ có từ 1.5.2, và `ALevelParamV2 : ALiveOpsParam` chỉ có từ 1.5.0)
- Phơi `CurrentLevelDataId` (băm DATA màn do game server đặt, client đang echo trong CSLevelStart/CSLevelResult) và điền vào `levelDataId` của mọi log level — Start, Pass, Fail và heartbeat. Cần BigData có field đó (bản sau 1.5.1). Mục đích: kho analytics join được sang catalog level của game backend, và tách được 'đổi layout' với 'đổi tuning' khi so với `currentLevelId` (băm data + param do client tính)
----------------------------------


## 1.3.11
- FLevelManager chuyển hẳn về cửa chuẩn FalconBigDataController.Level (OnStart/OnHeartbeat/OnPass/OnFail) thay vì tự dựng FLevelLog + Send: cùng event, wire không đổi byte nào, nhưng invariant "SetLevelConfig trước enqueue" giờ nằm trong cửa OnStart thay vì phải tự nhớ, và Level module hết ôm chi tiết pipeline của BigData
- FIX chia cho 0 ở SendLevelLog nhánh Fail: levelProgress = score*100/totalScore — game không có hệ điểm (totalScore = 0) là DivideByZeroException ngay lúc thua màn, log Fail mất và exception ném vào call site của game. Cùng họ bug đã vá ở ALevelHeartBeatService 1.1.4; giờ totalScore <= 0 thì để levelProgress mặc định
- Dep: bigdata 1.3.2 (cửa OnHeartbeat mới), devkit 1.3.0, remoteconfig 1.1.5
----------------------------------


## 1.3.10
- Nối lại dây đứt: OnLevelStart(param) bị refactor pipeline (4938a6754) làm rơi — chữ ký còn nhận FParam nhưng thân hàm không dùng, game truyền vào bị nuốt im lặng từ đó. Giờ param.ToDictionary chảy vào extraMeta của log Start đúng nghĩa cũ (đời PuzzleLevelLog), cùng khuôn OnLevelResult; extraMeta tường minh thắng khi trùng key
- OnLevelStart/OnLevelResult thêm optional Dictionary<string,object> extraMeta — đi CẢ hai đường: extraMeta của log BigData (server lưu event_extra_props, đăng ký hợp đồng sau cook hồi tố được) và field extraMeta mới trên CSLevelStart/CSLevelResult cho game server (bản THÔ, không trộn với coinSpend/info vốn đã có field riêng). Trên log BigData, key game truyền tường minh THẮNG key suy từ info/param cũ khi trùng; cột hợp đồng thì không nguồn nào đè nổi (merge PutIfAbsent ở tầng param)
- playTurnId chuyển về BigData 1.3.0 quản lý tập trung (LevelTurnService/LevelTurnState): FLevelManager không tự sinh/reset nữa, PlayTurnId proxy đọc turn đang mở (null khi không có turn — trước đây lazy sinh mới); level log không set playTurnId, cache tự điền
- OnLevelStart thêm optional movesLimit/timeLimitSec; OnLevelResult thêm optional failReason (hợp đồng §D)
- movesLimit/timeLimitSec nay đi trong bundle nhãn màn thay vì field phẳng (amendment hợp đồng §H6 — attr của MÀN không thả lên bản tin lượt): OnLevelStart giữ NGUYÊN chữ ký, game không phải sửa dòng nào; FLevelManager gọi thêm FalconBigDataController.Level.SetLevelConfig trước khi Send
- Bỏ elo = -1 trong log Pass/Fail (gây LogError + ép về 0 mỗi lần) — elo để null, vắng mặt khỏi payload
----------------------------------


## 1.3.9
- Fix: reset IAP cộng dồn (_price/_currencyCode) khi OnLevelStart — trước đây CSLevelResult mang theo IAP của các level trước trong phiên
- LevelHeartBeat nhận LevelHeartBeatParamV2 (BigData 1.2.9 đổi tên từ LevelHeartBearParamV2 do typo)
----------------------------------


## 1.3.8
- Patch GUID using
----------------------------------


## 1.3.7
- Add LevelHeartBeat to FLevelManager
- Adjust FLevelManager to use new LevelParamV2
----------------------------------


## 1.3.6
- Đồng bộ levelData giữa các sự kiện OnLevelReady, OnLevelStart, OnLevelResult
----------------------------------


## 1.3.5
- update lại phần tính levelProgress khi gửi level log
----------------------------------


## 1.3.4
- Thêm tham số levelProgress để gửi sang Bigdata trong hàm OnLevelResult
----------------------------------


## 1.3.3
Thêm thông tin FilterID, AbVariant cho AB testing Level
----------------------------------


## 1.3.2
- Gửi md5 của level lên bigdata
----------------------------------


## 1.3.1
- Thêm hàm SendLevelPlayingInfo để gửi các sự kiện trong quá trình chơi level lên Server
----------------------------------


## 1.3.0
- thêm string info vào bản tin SCLevelResult
----------------------------------


## 1.2.9
- Thiết lập param cho CSLevelResult trùng với CSLevelStart
----------------------------------


## 1.2.8
- Thêm param vào PuzzleLevelLog
----------------------------------


## 1.2.7
- fix lỗi nullpointer
----------------------------------


## 1.2.6
- Support AB Testing bằng Server Logic
----------------------------------


## 1.2.5
- Sửa lỗi gửi các bản tin CSLevelXXX trước khi Login
----------------------------------


## 1.2.4
- Đổi FLevelLog thành PuzzleLevelLog
----------------------------------


## 1.2.3
- try cache lỗi từ ILevelProvider
----------------------------------


## 1.2.2
- Chuẩn hoá ký tự xuống dòng trước khi mấy MD5
----------------------------------


## 1.2.1
- Thêm tham số vào CSLevelResult
----------------------------------


## 1.2.0
- Tối ưu performance
----------------------------------


## 1.1.9
- Thêm class LevelCompressor để nén level text
----------------------------------


## 1.1.8
- Bỏ GetLevelDifficulty(int level) trong IFLevelProvider
----------------------------------


## 1.1.7
- Bỏ hàm GetLevelParam(int level), thay bằng hàm GetLevelParam(string levelData) trong IFLevelProvider
----------------------------------


## 1.1.6
- fix log level_start
----------------------------------


## 1.1.5
- Thêm log action khi vào chơi level
----------------------------------


## 1.1.4
- fix lỗi save difficulty
----------------------------------


## 1.1.3
- Thêm startCurrentLevel = true || false vào LevelData để kiểm tra level đã start hay chưa
----------------------------------


## 1.1.2
- Xoá level mà server trả về trước đó trên Client, nếu truyền về giá trị nội dung rỗng
----------------------------------


## 1.1.1
- Send LevelLog
----------------------------------


## 1.1.0
- Truyền thêm các tham số lên Server cho onLevelResult
----------------------------------


## 1.0.22
- thêm total score vào level result
----------------------------------


## 1.0.21
- đăng ký sự kiện lắng nghe để trả về level hiện tại cho các module khác
----------------------------------


## 1.0.20
- Thêm levelParam vào CSLevelStart để gửi lên Server
----------------------------------


## 1.0.19
Thêm log level
----------------------------------


## 1.0.18
log funnel
----------------------------------


## 1.0.17
Thêm hàm public int[] GetAllLevels() trả về mảng các level đã được tải từ Server về, theo thứ thự tăng dần, Ví dụ: [1,3,6,7,8,12,15,17]
----------------------------------


## 1.0.16
Thêm Force Play Level, để test bất kỳ level nào trên CMS
----------------------------------