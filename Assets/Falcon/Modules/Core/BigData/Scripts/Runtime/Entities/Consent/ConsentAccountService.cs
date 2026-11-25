/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 */

using System;
using System.Collections.Generic;
using Falcon.Helpers.Devkit;
using Newtonsoft.Json;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Popup xin quyền (B14(b)) + liên kết tài khoản (B14(c)) của hợp đồng đợt 3 — phần C# (đợt 1). Phần SDK TỰ đọc trạng
    /// thái quyền (iOS, và bảng "chưa hỏi / đã từ chối" của Android 13+) cần native ⇒ đợt 2 ở Devkit.
    /// </summary>
    public class ConsentAccountService : MySingleton<ConsentAccountService>
    {
        private const string LINKED_KEY = "falcon.bigdata.account.linked_providers";
        internal const string PUSH_PROMPT_SEEN_KEY = "falcon.bigdata.consent.push_prompt_seen";
        private const string POST_NOTIFICATIONS = "android.permission.POST_NOTIFICATIONS";

        private readonly LogScheduleService _logScheduleService;
        private readonly PermissionLogService _permissionLogService;
        private readonly IDataPool _dataPool;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<PromptPermission, long> _lastSystemReportAt = new();

        public ConsentAccountService(
            LogScheduleService logScheduleService, PermissionLogService permissionLogService, IDataPool dataPool)
        {
            _logScheduleService = logScheduleService;
            _permissionLogService = permissionLogService;
            _dataPool = dataPool;
        }

        /// <summary>ms từ lần game / hàm bọc báo dòng <c>system</c> gần nhất của quyền này (trong phiên); null nếu chưa có.</summary>
        public long? SinceSystemReportMs(PromptPermission permission) =>
            _lastSystemReportAt.TryGetValue(permission, out var at) ? MonotonicClock.ElapsedMillis - at : null;

        /// <summary>SDK đã thấy popup OS xin quyền push (B14(b) — tách "từ chối hẳn" với "chưa hỏi").</summary>
        public bool PushPromptSeen => _dataPool.GetOrDefault(PUSH_PROMPT_SEEN_KEY, false);

        public void Prompt(PromptPermission permission, PromptKind kind, PromptTrigger? trigger, SoftResult? softResult,
            SystemResult? systemResult)
        {
            if (kind == PromptKind.System && softResult.HasValue)
            {
                AnalyticLogger.Instance.Warning("Consent.OnPrompt: softResult chỉ dành cho màn Soft — bỏ.");
                softResult = null;
            }

            if (kind == PromptKind.Soft && systemResult.HasValue)
            {
                AnalyticLogger.Instance.Warning("Consent.OnPrompt: systemResult chỉ dành cho popup System — bỏ.");
                systemResult = null;
            }

            if (permission == PromptPermission.Push && kind == PromptKind.System) _dataPool.Save(PUSH_PROMPT_SEEN_KEY, true);
            if (kind == PromptKind.System) _lastSystemReportAt[permission] = MonotonicClock.ElapsedMillis;
            _logScheduleService.Enqueue(new FPermissionPromptLog
            {
                permission = permission, promptKind = kind, trigger = trigger, softResult = softResult,
                systemResult = systemResult
            });
        }

        /// <summary>
        /// Hàm bọc xin quyền thông báo. Android 13+ (API 33): gọi popup OS qua <c>UnityEngine.Android.Permission</c>, nhận kết
        /// quả TỪNG LẦN (không sót lần từ chối như khi chỉ đọc trạng thái), tự bắn dòng <c>system</c> và đẩy trạng thái vào
        /// <c>f_sdk_permission_push</c>. Nền tảng / bản OS khác: không có popup để bọc — gọi <paramref name="onDone"/> với null.
        /// </summary>
        public void RequestPush(PromptTrigger trigger, Action<bool?> onDone)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (AndroidSdkInt() >= 33)
            {
                if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(POST_NOTIFICATIONS))
                {
                    _permissionLogService.ReportPermission(PermissionType.Push, FPermissionValue.Granted);
                    onDone?.Invoke(true);
                    return;
                }

                var callbacks = new UnityEngine.Android.PermissionCallbacks();
                callbacks.PermissionGranted += _ => Finish(trigger, SystemResult.Granted, onDone);
                callbacks.PermissionDenied += _ => Finish(trigger, SystemResult.Denied, onDone);
                callbacks.PermissionDeniedAndDontAskAgain += _ => Finish(trigger, SystemResult.DeniedDontAskAgain, onDone);
                UnityEngine.Android.Permission.RequestUserPermission(POST_NOTIFICATIONS, callbacks);
                return;
            }
#endif
            onDone?.Invoke(null);
        }

        private void Finish(PromptTrigger trigger, SystemResult result, Action<bool?> onDone)
        {
            Prompt(PromptPermission.Push, PromptKind.System, trigger, null, result);
            var granted = result == SystemResult.Granted;
            _permissionLogService.ReportPermission(PermissionType.Push, granted ? FPermissionValue.Granted : FPermissionValue.Denied);
            onDone?.Invoke(granted);
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static int AndroidSdkInt()
        {
            using var version = new UnityEngine.AndroidJavaClass("android.os.Build$VERSION");
            return version.GetStatic<int>("SDK_INT");
        }
#endif

        public void Link(FAccountLinkLog log)
        {
            var warning = log.DropConflictFieldsUnlessConflict();
            if (warning != null) AnalyticLogger.Instance.Warning(warning);
            if (log.localLevel is < 0) log.localLevel = null;
            if (log.cloudLevel is < 0) log.cloudLevel = null;
            _logScheduleService.Enqueue(log);
        }

        public void SetLinkedProviders(IEnumerable<AccountProvider> providers)
        {
            var wire = providers == null ? null : JsonConvert.SerializeObject(LinkedProviders.ToWire(providers));
            _dataPool.Compute<string>(LINKED_KEY, _ => wire);
        }

        /// <summary>Danh sách đã khai (null = game chưa khai = không có tính năng liên kết ⇒ vắng trên app_open).</summary>
        public List<string> LinkedProvidersWire()
        {
            var json = _dataPool.GetOrDefault<string>(LINKED_KEY, null);
            if (json == null) return null;
            try
            {
                return JsonConvert.DeserializeObject<List<string>>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Popup xin quyền — <c>FalconBigDataController.Consent</c> (hợp đồng đợt 3 B14(b)).
    /// <code>
    /// FalconBigDataController.Consent.OnPrompt(PromptPermission.Push, PromptKind.Soft, PromptTrigger.LevelWin, SoftResult.Accept);
    /// FalconBigDataController.Consent.RequestPush(PromptTrigger.LevelWin, granted => { ... }); // Android: SDK tự ghi dòng system
    /// </code>
    /// </summary>
    public class FConsentApi
    {
        private readonly ConsentAccountService _service;

        internal FConsentApi(ConsentAccountService service)
        {
            _service = service;
        }

        /// <summary>
        /// Game hiện một popup xin quyền. Màn <see cref="PromptKind.Soft"/> của game: luôn game báo, kèm
        /// <paramref name="softResult"/>. Popup OS game tự gọi (không qua <see cref="RequestPush"/>): báo
        /// <see cref="PromptKind.System"/>. Kết quả popup OS vẫn đọc ở log quyền <c>f_sdk_permission_*</c>.
        /// </summary>
        public void OnPrompt(PromptPermission permission, PromptKind kind, PromptTrigger? trigger = null,
            SoftResult? softResult = null) => _service.Prompt(permission, kind, trigger, softResult, null);

        /// <summary>
        /// Xin quyền thông báo qua SDK. Android 13+: SDK gọi popup OS, tự ghi dòng <c>system</c> kèm kết quả và trạng thái quyền;
        /// <paramref name="onDone"/> nhận true / false. Nền tảng khác: <paramref name="onDone"/> nhận null — game tự xin như cũ
        /// (iOS: SDK sẽ tự thấy lần đổi trạng thái khi có phần native, đợt 2).
        /// </summary>
        public void RequestPush(PromptTrigger trigger, Action<bool?> onDone = null) => _service.RequestPush(trigger, onDone);
    }

    /// <summary>
    /// Liên kết tài khoản — <c>FalconBigDataController.Account</c> (hợp đồng đợt 3 B14(c)). Không gửi id tài khoản bên ngoài.
    /// </summary>
    public class FAccountApi
    {
        private readonly ConsentAccountService _service;

        internal FAccountApi(ConsentAccountService service)
        {
            _service = service;
        }

        /// <summary>Một bước mời / đăng nhập / liên kết. Nhiều loại tài khoản = nhiều lời gọi.</summary>
        /// <param name="conflictChoice">Chỉ <see cref="AccountLinkAction.SaveConflict"/>.</param>
        /// <param name="localLevel">Chỉ SaveConflict: level cao nhất đã qua ở máy này (cùng nghĩa <c>current_level</c>).</param>
        /// <param name="cloudLevel">Chỉ SaveConflict: level cao nhất đã qua của tài khoản.</param>
        public void OnLink(AccountProvider provider, AccountLinkAction action, AccountInitiatedBy? initiatedBy = null,
            AccountLinkTrigger? trigger = null, SaveConflictChoice? conflictChoice = null, int? localLevel = null,
            int? cloudLevel = null)
        {
            _service.Link(new FAccountLinkLog
            {
                provider = provider, action = action, initiatedBy = initiatedBy, trigger = trigger,
                conflictChoice = conflictChoice, localLevel = localLevel, cloudLevel = cloudLevel
            });
        }

        /// <summary>
        /// Các loại tài khoản ĐANG liên kết — ảnh chụp trên <c>app_open.linked_providers</c> (lưu qua restart). Gọi lúc khởi
        /// động và sau mỗi lần liên kết / gỡ. Danh sách rỗng = chưa liên kết gì; game không có tính năng liên kết thì đừng gọi.
        /// </summary>
        public void SetLinkedProviders(params AccountProvider[] providers) => _service.SetLinkedProviders(providers);
    }
}
