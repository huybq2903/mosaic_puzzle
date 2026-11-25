/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 */

using System;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>Cài đặt bật / tắt của game (hợp đồng đợt 3 B4). Ngôn ngữ đi hàm riêng (<c>Setting.SetLanguage</c>).</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum FSetting
    {
        [EnumMember(Value = "sound")] Sound,
        [EnumMember(Value = "music")] Music,
        [EnumMember(Value = "haptic")] Haptic,
        [EnumMember(Value = "language")] Language
    }

    /// <summary>Vì sao người chơi tắt tiếng — câu hỏi một chạm là UI của game, SDK chỉ nhận giá trị (B4). Danh sách ĐÓNG.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum MuteReason
    {
        [EnumMember(Value = "public_place")] PublicPlace,
        [EnumMember(Value = "focus")] Focus,
        [EnumMember(Value = "dislike_sound")] DislikeSound,
        [EnumMember(Value = "skip")] Skip
    }

    /// <summary>Một lần đổi cài đặt — dữ liệu của dòng <c>f_sdk_setting_change</c>.</summary>
    public readonly struct SettingChange
    {
        public readonly FSetting Setting;
        public readonly object Before;
        public readonly object After;

        public SettingChange(FSetting setting, object before, object after)
        {
            Setting = setting;
            Before = before;
            After = after;
        }
    }

    /// <summary>
    /// Trạng thái cài đặt âm thanh / nhạc / rung / ngôn ngữ mà SDK tự giữ qua restart (hợp đồng đợt 3 B4, ký 23/11).
    /// Serializable để lưu nguyên vào kho của SDK. Pure — caller lưu / khoá.
    /// </summary>
    [Serializable]
    public class SettingState
    {
        public bool? defaultSound, defaultMusic, defaultHaptic;
        public bool? sound, music, haptic;
        public string language;

        public void DeclareDefaults(bool soundDefault, bool musicDefault, bool hapticDefault)
        {
            defaultSound = soundDefault;
            defaultMusic = musicDefault;
            defaultHaptic = hapticDefault;
        }

        /// <summary>Game báo trạng thái hiện tại (lúc khởi động) — KHÔNG phải người chơi đổi, không sinh dòng.</summary>
        public void Sync(bool? soundNow, bool? musicNow, bool? hapticNow, string languageNow)
        {
            sound = soundNow ?? sound;
            music = musicNow ?? music;
            haptic = hapticNow ?? haptic;
            if (!string.IsNullOrEmpty(languageNow)) language = languageNow;
        }

        /// <summary>
        /// Người chơi đổi một cài đặt bật / tắt. Giá trị trước = giá trị SDK đang giữ; chưa giữ thì lấy mặc định game khai;
        /// không có cả hai thì coi là ngược lại (công tắc bật / tắt). Null nếu không đổi gì.
        /// </summary>
        public SettingChange? Set(FSetting setting, bool value)
        {
            bool before;
            switch (setting)
            {
                case FSetting.Sound:
                    before = sound ?? defaultSound ?? !value;
                    sound = value;
                    break;
                case FSetting.Music:
                    before = music ?? defaultMusic ?? !value;
                    music = value;
                    break;
                case FSetting.Haptic:
                    before = haptic ?? defaultHaptic ?? !value;
                    haptic = value;
                    break;
                default:
                    return null;
            }

            return before == value ? null : new SettingChange(setting, before, value);
        }

        /// <summary>Người chơi đổi ngôn ngữ hiển thị (mã ISO). Null nếu không đổi.</summary>
        public SettingChange? SetLanguage(string code)
        {
            if (string.IsNullOrEmpty(code) || code == language) return null;
            var before = language;
            language = code;
            return new SettingChange(FSetting.Language, before, code);
        }

        /// <summary>Cả ba công tắc còn ở mặc định game khai. Null nếu chưa đủ dữ liệu để biết.</summary>
        public bool? IsDefault()
        {
            if (defaultSound == null || defaultMusic == null || defaultHaptic == null) return null;
            var s = sound ?? defaultSound;
            var m = music ?? defaultMusic;
            var h = haptic ?? defaultHaptic;
            return s == defaultSound && m == defaultMusic && h == defaultHaptic;
        }

        /// <summary>Giá trị hiện tại (đã giữ, không thì mặc định) — cho <c>app_open</c>.</summary>
        public bool? CurrentSound => sound ?? defaultSound;
        public bool? CurrentMusic => music ?? defaultMusic;
        public bool? CurrentHaptic => haptic ?? defaultHaptic;
    }
}
