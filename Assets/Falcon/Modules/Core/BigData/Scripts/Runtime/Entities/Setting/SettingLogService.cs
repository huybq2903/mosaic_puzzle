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
using UnityEngine.Scripting;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>Người chơi đổi một cài đặt — <c>f_sdk_setting_change</c> (hợp đồng đợt 3 B4). Hàng đợi thường.</summary>
    [Serializable]
    public class FSettingChangeLog : AFalconLog
    {
        public FSetting setting;

        /// <summary>Trước / sau: bool với sound / music / haptic, mã ngôn ngữ với language. Trước vắng nếu không biết.</summary>
        [FKey(RemoveIfNull = true)] public object valueBefore;
        [FKey(RemoveIfNull = true)] public object valueAfter;

        [FKey(RemoveIfNull = true)] public MuteReason? muteReason;

        [Preserve]
        public FSettingChangeLog()
        {
        }

        public override string Event => "f_sdk_setting_change";
    }

    /// <summary>
    /// Cài đặt âm thanh / nhạc / rung / ngôn ngữ (hợp đồng đợt 3 B4): giữ trạng thái qua restart cho <c>app_open</c>, gửi
    /// <c>f_sdk_setting_change</c> mỗi lần người chơi đổi. Thay cho cặp tên–giá trị tự do qua <c>f_sdk_property</c>.
    /// </summary>
    public class SettingLogService : MySingleton<SettingLogService>
    {
        private const string STATE_KEY = "falcon.bigdata.setting.state";

        private readonly LogScheduleService _logScheduleService;
        private readonly IDataPool _dataPool;
        private readonly object _lock = new();
        private SettingState _state;

        public SettingLogService(LogScheduleService logScheduleService, IDataPool dataPool)
        {
            _logScheduleService = logScheduleService;
            _dataPool = dataPool;
        }

        /// <summary>Bản sao trạng thái hiện tại (cho <c>app_open</c>).</summary>
        public SettingState Snapshot()
        {
            lock (_lock) return JsonConvert.DeserializeObject<SettingState>(JsonConvert.SerializeObject(State));
        }

        public void DeclareDefaults(bool sound, bool music, bool haptic)
        {
            lock (_lock) State.DeclareDefaults(sound, music, haptic);
            Save();
        }

        public void Sync(bool? sound, bool? music, bool? haptic, string language)
        {
            lock (_lock) State.Sync(sound, music, haptic, language);
            Save();
        }

        public void Set(FSetting setting, bool value, MuteReason? muteReason)
        {
            if (setting == FSetting.Language)
            {
                AnalyticLogger.Instance.Warning("Setting.Set(Language, bool): ngôn ngữ dùng Setting.SetLanguage(code) — bỏ qua.");
                return;
            }

            if (muteReason.HasValue && (value || setting == FSetting.Haptic))
            {
                AnalyticLogger.Instance.Warning($"Setting.Set({setting}, {value}): muteReason chỉ dành cho lúc TẮT tiếng / nhạc — bỏ qua lý do.");
                muteReason = null;
            }

            SettingChange? change;
            lock (_lock) change = State.Set(setting, value);
            Save();
            if (change.HasValue) Send(change.Value, muteReason);
        }

        public void SetLanguage(string code)
        {
            SettingChange? change;
            lock (_lock) change = State.SetLanguage(code);
            Save();
            if (change.HasValue) Send(change.Value, null);
        }

        private SettingState State
        {
            get
            {
                if (_state != null) return _state;
                var json = _dataPool.GetOrDefault<string>(STATE_KEY, null);
                try
                {
                    _state = string.IsNullOrEmpty(json) ? null : JsonConvert.DeserializeObject<SettingState>(json);
                }
                catch (JsonException)
                {
                    _state = null;
                }

                return _state ??= new SettingState();
            }
        }

        private void Save()
        {
            string json;
            lock (_lock) json = JsonConvert.SerializeObject(State);
            _dataPool.Compute<string>(STATE_KEY, _ => json);
        }

        private void Send(SettingChange change, MuteReason? muteReason)
        {
            _logScheduleService.Enqueue(new FSettingChangeLog
            {
                setting = change.Setting,
                valueBefore = change.Before,
                valueAfter = change.After,
                muteReason = muteReason
            });
        }
    }

    /// <summary>
    /// Cài đặt âm thanh / nhạc / rung / ngôn ngữ — <c>FalconBigDataController.Setting</c> (hợp đồng đợt 3 B4).
    /// <code>
    /// FalconBigDataController.Setting.DeclareDefaults(sound: true, music: true, haptic: true); // lúc khởi động
    /// FalconBigDataController.Setting.Sync(sound: save.sound, music: save.music, haptic: save.haptic, language: "vi");
    /// FalconBigDataController.Setting.Set(FSetting.Sound, false, MuteReason.PublicPlace);   // ở nút cài đặt
    /// </code>
    /// SDK tự gửi <c>f_sdk_setting_change</c>, tự giữ trạng thái qua restart cho <c>app_open</c> và tự tính
    /// <c>setting_is_default</c>. Thôi gửi <c>setting_*</c> qua <c>f_sdk_property</c>.
    /// </summary>
    public class FSettingApi
    {
        private readonly SettingLogService _service;

        internal FSettingApi(SettingLogService service)
        {
            _service = service;
        }

        /// <summary>Khai mặc định của game một lần lúc khởi động (để SDK tính <c>setting_is_default</c>).</summary>
        public void DeclareDefaults(bool sound, bool music, bool haptic) => _service.DeclareDefaults(sound, music, haptic);

        /// <summary>
        /// Báo trạng thái HIỆN TẠI lúc khởi động (đọc từ save của game) — không phải người chơi đổi nên không sinh dòng. Cần cho
        /// lần đầu lên bản có API này: SDK chưa biết người chơi đã đổi gì trước đó. Tham số null = không đổi giá trị đang giữ.
        /// </summary>
        public void Sync(bool? sound = null, bool? music = null, bool? haptic = null, string language = null) =>
            _service.Sync(sound, music, haptic, language);

        /// <summary>Người chơi đổi âm thanh / nhạc / rung ở nút cài đặt. Không đổi gì thì không sinh dòng.</summary>
        /// <param name="muteReason">Tuỳ chọn, chỉ khi TẮT tiếng / nhạc: câu trả lời của popup một chạm (UI của game).</param>
        public void Set(FSetting setting, bool value, MuteReason? muteReason = null) =>
            _service.Set(setting, value, muteReason);

        /// <summary>Người chơi đổi ngôn ngữ hiển thị (mã ISO, vd <c>vi</c>, <c>en</c>, <c>pt-BR</c>). Không đổi thì không sinh dòng.</summary>
        public void SetLanguage(string code) => _service.SetLanguage(code);
    }
}
