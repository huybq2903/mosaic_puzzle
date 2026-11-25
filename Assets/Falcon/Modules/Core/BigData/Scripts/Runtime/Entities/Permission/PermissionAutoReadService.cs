/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 */

using System;
using Falcon.Helpers.Devkit;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// SDK tự đọc trạng thái quyền native (hợp đồng đợt 3 B14(b), đợt 2) lúc mở app, quay lại từ nền, lấy lại focus và
    /// mỗi nhịp đọc tín hiệu máy (~10 giây — iOS đọc quyền thông báo bất đồng bộ, giá trị mới tới ở lần đọc sau). Giá trị
    /// đi vào <c>f_sdk_permission_*</c> có sẵn (vẫn luật chỉ bắn khi đổi) và ảnh chụp <c>perm_*_status</c> trên
    /// <c>app_open</c>.
    /// <list type="bullet">
    /// <item>"Đổi" so với lần đọc <b>native</b> trước (lưu qua restart), không so với giá trị đã gửi — xem
    /// <see cref="PermissionAutoRead"/>.</item>
    /// <item>iOS (push, ATT): native rời <c>not_determined</c> ⇒ tự bắn dòng <c>f_sdk_permission_prompt</c> loại
    /// <c>system</c> (không <c>trigger</c>, giờ = lúc phát hiện), trừ khi game vừa tự báo dòng <c>system</c> cùng quyền
    /// trong 10 giây.</item>
    /// </list>
    /// Main thread.
    /// </summary>
    public class PermissionAutoReadService : MySingleton<PermissionAutoReadService>
    {
        private const string NATIVE_LAST_PREFIX = "falcon.bigdata.permission.native_last_";

        private readonly PermissionLogService _permissionLogService;
        private readonly ConsentAccountService _consentAccountService;
        private readonly LogScheduleService _logScheduleService;
        private readonly IDataPool _dataPool;

        public PermissionAutoReadService(
            PermissionLogService permissionLogService, ConsentAccountService consentAccountService,
            LogScheduleService logScheduleService, IDataPool dataPool)
        {
            _permissionLogService = permissionLogService;
            _consentAccountService = consentAccountService;
            _logScheduleService = logScheduleService;
            _dataPool = dataPool;
        }

        /// <summary>Đọc native ngay, báo nếu đổi; trả ảnh chụp (push, att) cho <c>app_open</c> (null = vắng).</summary>
        public (string push, string att) ReadNow()
        {
            string push = null, att = null;
            try
            {
                var pushReading = DeviceSignals.PushPermission();
                if (pushReading.HasValue)
                {
                    push = PushStatusResolver.Resolve(pushReading.Value, _consentAccountService.PushPromptSeen, NewInstall());
                    Apply(PermissionType.Push, PromptPermission.Push, push,
                        pushReading.Value.IosAuthorizationStatus.HasValue);
                }

                var attStatus = DeviceSignals.IosAttStatus();
                if (attStatus.HasValue)
                {
                    att = PushStatusResolver.Att(attStatus.Value);
                    Apply(PermissionType.Att, PromptPermission.Att, att, true);
                }
            }
            catch (Exception e)
            {
                AnalyticLogger.Instance.Warning("Tự đọc trạng thái quyền lỗi: " + e.Message);
            }

            return (push, att);
        }

        private bool? NewInstall() =>
            _dataPool.HasKey(LoadStepLedger.NEW_INSTALL) ? _dataPool.GetOrDefault(LoadStepLedger.NEW_INSTALL, false) : null;

        private void Apply(PermissionType type, PromptPermission prompt, string current, bool autoPromptPlatform)
        {
            var key = NATIVE_LAST_PREFIX + type;
            var lastNative = _dataPool.GetOrDefault<string>(key, null);
            var (report, autoPrompt) = PermissionAutoRead.Decide(lastNative, current, autoPromptPlatform,
                _consentAccountService.SinceSystemReportMs(prompt));
            if (!report) return;

            _permissionLogService.ReportPermission(type, current);
            if (autoPrompt)
                _logScheduleService.Enqueue(new FPermissionPromptLog { permission = prompt, promptKind = PromptKind.System });
            _dataPool.Compute<string>(key, _ => current);
        }
    }
}
