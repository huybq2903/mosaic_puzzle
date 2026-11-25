/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 */

using System.Runtime.Serialization;
using Falcon.Helpers.Devkit;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>Lần tải nào: mở app lần đầu sau cài, hay vào level đầu tiên (hợp đồng đợt 3 B2).</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum LoadKind
    {
        [EnumMember(Value = "app")] App,
        [EnumMember(Value = "level")] Level
    }

    /// <summary>Trạm tải (hợp đồng đợt 3 B2). Danh sách ĐÓNG.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum LoadStep
    {
        /// <summary>SDK tự báo — mốc sớm nhất C# chạm được (<c>SubsystemRegistration</c>).</summary>
        [EnumMember(Value = "load_start")] LoadStart,

        /// <summary>SDK tự báo — khung hình đầu tiên.</summary>
        [EnumMember(Value = "first_frame")] FirstFrame,

        /// <summary>Màn đồng ý (CMP) hiện — module Ump báo.</summary>
        [EnumMember(Value = "consent_shown")] ConsentShown,

        /// <summary>SDK tự báo — remote config lấy được từ mạng.</summary>
        [EnumMember(Value = "config_ready")] ConfigReady,

        [EnumMember(Value = "asset_download_start")] AssetDownloadStart,
        [EnumMember(Value = "asset_download_done")] AssetDownloadDone,
        [EnumMember(Value = "level_load_start")] LevelLoadStart,

        /// <summary>Level BẤM ĐƯỢC (không phải lúc bắt đầu dựng).</summary>
        [EnumMember(Value = "level_ready")] LevelReady,
        [EnumMember(Value = "load_error")] LoadError
    }

    /// <summary>
    /// Sổ "trạm nào đã báo" của <c>f_sdk_ftue_load_step</c>: mỗi trạm MỘT lần mỗi lần cài, không ép thứ tự (cùng luật họ
    /// <c>f_sdk_ftue_*</c>). Trạm <see cref="LoadKind.App"/> chỉ nhận trong lần chạy ĐẦU TIÊN sau cài; trạm
    /// <see cref="LoadKind.Level"/> nhận tới khi <see cref="LoadStep.LevelReady"/> đầu tiên đã báo. Máy cập nhật từ bản SDK
    /// cũ (không phải cài mới) không có trạm nào.
    /// <br/>Trên <see cref="IDataPool"/>, không log, không singleton — test chạy được bằng pool giả.
    /// </summary>
    public class LoadStepLedger
    {
        internal const string NEW_INSTALL = "falcon.bigdata.ftue.load.new_install";
        private const string STEP_PREFIX = "falcon.bigdata.ftue.load.";

        private readonly IDataPool _dataPool;
        private bool _firstRun;

        public LoadStepLedger(IDataPool dataPool)
        {
            _dataPool = dataPool;
        }

        /// <summary>
        /// Gọi một lần mỗi lần chạy, lúc SDK khởi tạo. Lần đầu SDK có sổ này: phiên đầu tiên ⇒ cài mới (ghi lại mãi);
        /// không thì là máy cập nhật — không có trạm nào.
        /// </summary>
        public void OnLaunch(int sessionId)
        {
            if (_dataPool.HasKey(NEW_INSTALL)) return;
            var isNew = sessionId <= 1;
            _dataPool.Save(NEW_INSTALL, isNew);
            // Pool chỉ tự xuống đĩa lúc pause / mỗi 5 phút: văng ngay lần chạy đầu mà mất dấu này thì lần sau bị đọc nhầm
            // thành máy cập nhật — đẩy xuống đĩa ngay.
            _dataPool.TrySync();
            _firstRun = isNew;
        }

        private bool IsNewInstall => _dataPool.GetOrDefault(NEW_INSTALL, false);

        /// <summary>Trạm này có được gửi không — chưa gửi, và đúng giai đoạn của loại tải.</summary>
        public bool Accepts(LoadKind kind, LoadStep step)
        {
            if (!IsNewInstall || _dataPool.HasKey(Key(kind, step))) return false;
            return kind == LoadKind.App ? _firstRun : !_dataPool.HasKey(Key(LoadKind.Level, LoadStep.LevelReady));
        }

        public void Mark(LoadKind kind, LoadStep step) => _dataPool.Save(Key(kind, step), true);

        private static string Key(LoadKind kind, LoadStep step) => STEP_PREFIX + kind + "." + step;
    }
}
