/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 */

using System;
using System.Runtime.Serialization;
using Falcon.Helpers.Devkit;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine.Scripting;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>Người chơi chỉnh tốc độ / bỏ qua hoạt hình (hợp đồng đợt 3 B7, ký 23/11). Danh sách ĐÓNG.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum SpeedAction
    {
        [EnumMember(Value = "speed_on")] SpeedOn,
        [EnumMember(Value = "speed_off")] SpeedOff,
        [EnumMember(Value = "skip_anim")] SkipAnim,

        /// <summary>Tuỳ chọn: x2 tốn tài nguyên, người chơi bấm lúc đã hết.</summary>
        [EnumMember(Value = "speed_tap_no_stock")] SpeedTapNoStock
    }

    /// <summary>Loại đoạn hoạt hình bị tăng tốc / bỏ qua (hợp đồng đợt 3 B7). Danh sách ĐÓNG — thiếu thì xin sửa hợp đồng.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum AnimType
    {
        [EnumMember(Value = "level_intro")] LevelIntro,
        [EnumMember(Value = "gameplay")] Gameplay,
        [EnumMember(Value = "level_result")] LevelResult,
        [EnumMember(Value = "reward")] Reward,
        [EnumMember(Value = "chest")] Chest,
        [EnumMember(Value = "other")] Other
    }

    /// <summary>
    /// <c>f_sdk_speed_action</c> — một lần bật / tắt x2 hoặc bỏ qua hoạt hình, trong lẫn ngoài level. <c>play_turn_id</c>
    /// (khi đang trong lượt) và <c>perf_screen</c> SDK tự đính. Tổng trên dòng lượt: <c>t_speed_on_sec</c> (B6).
    /// </summary>
    [Serializable]
    public class FSpeedActionLog : AFalconLog, IPerfScreenCarrier
    {
        public SpeedAction action;
        public AnimType animType;

        /// <summary>Tên riêng của đoạn hoạt hình trong game (tuỳ chọn).</summary>
        [FKey(RemoveIfNull = true)] public string animName;

        /// <summary>Chỉ <see cref="SpeedAction.SkipAnim"/>: đã xem bao nhiêu giây trước khi bỏ qua.</summary>
        [FKey(RemoveIfNull = true)] public float? secIntoAnim;

        [Preserve]
        public FSpeedActionLog()
        {
        }

        public override string Event => "f_sdk_speed_action";
    }
}
