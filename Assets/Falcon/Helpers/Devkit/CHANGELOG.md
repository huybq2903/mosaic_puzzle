## 1.4.0
- ✨ **`DeviceSignals` — đọc tín hiệu máy native** (hợp đồng BigData đợt 3, các field "(2)"; BigData 1.8.0 dùng). Lớp mỏng: chỉ đọc +
  quy đổi chung hai nền tảng (`DeviceSignalMaps`, pure, có test ở BigData), nghĩa log là việc của BigData. **Chỉ gọi trên main thread**;
  không có / lỗi / Editor ⇒ `null`, không bao giờ ném.
  - Nhiệt độ máy (Android 10+ 7 mức gộp về `nominal` · `fair` · `serious` · `critical`; iOS `thermalState`) · tiết kiệm pin.
  - Pin (Android): kiểu cắm `ac` · `usb` · `wireless` · `none` (dock xếp `ac`), nhiệt độ °C.
  - Âm thanh: âm lượng nhạc bậc 10 % (**0 đúng là tắt tiếng**, khác 0 làm tròn lên), chế độ chuông (Android), đầu ra `speaker` ·
    `wired` · `bluetooth` (Android: suy từ thiết bị ra đang có, ưu tiên bluetooth > wired > speaker; iOS: đường đang phát thật).
  - Cấu hình: hệ số cỡ chữ (iOS quy từ `UIContentSizeCategory` theo cỡ chữ Body / 17 pt), chữ đậm (Android 12+ / iOS), giảm chuyển
    động, dung lượng trống theo bậc (chỉ Android — iOS là "required reason API").
  - `MsSinceProcessStart` (Android 7+; iOS chờ soát "required reason API"), `ProcessId`, `SystemLowMemory` (Android
    `ActivityManager.MemoryInfo`), số đọc thô quyền thông báo (Android 13+ quyền + rationale, < 13 `areNotificationsEnabled`; iOS
    `authorizationStatus` đọc bất đồng bộ — lần gọi trả kết quả lần đọc trước), ATT (iOS 14+), `HistoricalExits` (Android 11+
    `ApplicationExitInfo`), báo cáo MetricKit `crash` / `hang` gần nhất (iOS 14+).
  - `InstallJavaUncaughtHandler`: handler ngoại lệ Java không ai bắt bằng `AndroidJavaProxy` (`[Preserve]`); handler cũ
    (Crashlytics / hệ thống) **luôn** được gọi tiếp trong `finally`.
  - Android: JNI từ C# (không thêm file Java), service hệ thống lấy một lần. Hằng số đối chiếu developer.android.com 23/11/2026;
    `STREAM_MUSIC` / `RINGER_MODE_*` / `GET_DEVICES_OUTPUTS` đọc qua JNI lúc chạy.
  - ⚠ **iOS tắt mặc định** — cần define `FALCON_DEVKIT_IOS_NATIVE` (cả `Native/iOS/FalconDeviceSignals.mm` lẫn phần C# theo define):
    file `.mm` **chưa được build trên Mac** ở bản này. `.mm` chỉ import Foundation + UIKit, lớp của framework khác tra lúc chạy
    (`NSClassFromString`, nạp động framework hệ thống nếu chưa link), kiểm `respondsToSelector:` trước mỗi lời gọi. Cần một bản build
    Mac + chạy máy thật trước khi bật define.
- README Http: `EnsureSuccess` ném `HttpRequestException` (message `<tên mã>:<body>`), không phải `InvalidOperationException` như tài liệu cũ ghi — ví dụ `catch` trong README sửa theo. Chỉ sửa tài liệu, code không đổi
----------------------------------


## 1.3.2
- Khai thêm `ParamKey.App.IS_TEST = "isTest"` + `TEST_SOURCE = "testSource"` — TÊN của hai key, GIÁ TRỊ do BigData cấp (chỉ bật ở editor / development build). Khai ở đây vì sổ key-trung-tâm là lớp chặn cuối cho luật 'bản production tuyệt đối không mang cờ test': không có nó thì một dòng `Common.Set("isTest", true)` trong code game là đủ làm hỏng dữ liệu thật của bản phát hành. Thuần thêm hằng số, không đổi hành vi gì
----------------------------------


## 1.3.1
- Khai `ParamKey.App.SDK_VERSION = "sdkVersion"` — TÊN của key, còn GIÁ TRỊ do BigData cấp qua IFCustomInfoRepository. Khai ở đây vì sổ "key trung tâm" (CommonParamState gom bằng reflection từ chính các const của ParamKey) là thứ chặn game set common param trùng tên: central param merge bằng PutIfAbsent nên ai đến trước thắng — không chặn thì có ca giá trị của game che mất phiên bản SDK thật, mà cột đó là mốc cắt ERA của cả kho dữ liệu. Thuần thêm một hằng số, không đổi hành vi gì
----------------------------------


## 1.3.0
- MonotonicClock + PlayerSessionService + IDataPool.TrySync
----------------------------------


## 1.2.9
- FIX total_play_time chạy 2× tốc độ thật: PlayerSessionService.OnPostStop và SessionLogService cùng cộng MỘT khoảng foreground vào cùng counter USER_TOTAL_TIME (double-write từ BigData 1.1.7 / 2026-01-08). Giờ PlayerSessionService là writer duy nhất của mode này; KHÔNG reset giá trị tích luỹ (loader clamp bỏ mọi cú tụt — chỉ hạ tốc độ về 1× rồi forward tiếp)
- FIX đồng hồ chơi chạy cả lúc app ở background + TotalPlayTime đếm đôi stretch cuối ở mọi log bắn lúc app pause: thêm mốc đóng băng _pausedAt, TimeSinceLastPause freeze tại lúc pause (thứ tự gọi giữa các ITerminal không còn ảnh hưởng), TotalPlayTime không cộng lại phần đã vào repository. Đúng hợp đồng §H5: giây, foreground-only, counter không reset
- Fix InvalidOperationException trong NoAutoCreateMySingletonImplementShellDisabler: enumerate LINQ lazy trên chính set rồi Remove giữa chừng (thiếu ToList như ISingletonShellSourceDisabler đã làm) — bug tiềm ẩn, chỉ nổ khi project có class gắn [NoAutoCreate]
----------------------------------


## 1.2.8
- Bỏ dependency ngầm vào Odin (Sirenix.Utilities) trong SingletonDecorators, thay bằng GetCustomAttribute chuẩn .NET
- Thêm unit tests cho AtomicRef, MyConcurrentQueue, LazyVal
- Sửa doc: xoá ConcurrentState (không tồn tại) khỏi Core/README; cập nhật tên class thật (IFFile/FLocalFile/FLocalFileRepository) trong BaseSystem/README
----------------------------------


## 1.2.7
- Them tham số về Last Iap vào user params
----------------------------------


## 1.2.6
- Vá lại logic của MainGameObj để tự đảm bảo khi trên scene có nhiều instance
- Vá lại InitService để tự ngắt khi editor stop
----------------------------------


## 1.2.5
- Patch ReservePlayerGeneralRepository._advertisingId
----------------------------------


## 1.2.4
- Sửa RepeatAction
----------------------------------


## 1.2.3
- Thay đổi singleton để có thể thêm các instance sinh qua hàm
- Chuyển ISingletonServiceReady sang SingletonExtension
----------------------------------


## 1.2.2
- Thêm ISingletonsAdviser
----------------------------------


## 1.2.1
- Chỉnh lại RepeatAction và ScheduleAction để đảm bảo concurrency
- Dùng RepeatAction để đồng bộ FDataPool để sync pool vào file mỗi 5p
----------------------------------


## 1.2.0
- Điều chỉnh Devkit để thêm NoLazy và ISingletonServiceReady
----------------------------------


## 1.1.9
- Update IOS Advertisement Support dependency
----------------------------------


## 1.1.8
- Không dùng currentmillis.com để đồng bộ thgian nữa mà chuyển sang dùng api nhà làm trên đầu api bên d4g
----------------------------------


## 1.1.7
- Chuyển DataPool và Time tới thư mục Config
- Đồng bộ thời gian với ITimeRepository
----------------------------------


## 1.1.6
- Restructure Info Repository Info Update
----------------------------------


## 1.1.5
Tách phần khởi tạo giá trị khỏi phần logging do có thể bị ảnh hưởng bởi bật tắt log với scripting symbol
----------------------------------


## 1.1.4
- Thêm 1 số thông tin về CPU và GPU vào IDeviceInfoRepository và FCentralUserParamService 
- Thêm hàm RemoveAll vào MyConcurrentQueue
----------------------------------


## 1.1.3
update InitService to reduce fps
----------------------------------


## 1.1.2
adjust HttpResponse.StreamBody() to close the response with stream, avoid resource leak
----------------------------------


## 1.1.1
fix HttpResponse.StreamBody()
----------------------------------


## 1.1.0
add author
----------------------------------


## 1.0.9
thay đổi FFileRepository và logic của FFile
----------------------------------


## 1.0.8
sửa http Requests uri khi có thông số param
----------------------------------


## 1.0.7
fix http GetRequest uri when have query
----------------------------------


## 1.0.6
thêm file REAME.md
thêm author doc vào 1 số file còn thiếu
----------------------------------