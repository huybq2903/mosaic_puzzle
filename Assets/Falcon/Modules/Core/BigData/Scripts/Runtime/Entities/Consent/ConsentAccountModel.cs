/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 */

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Falcon.Helpers.Devkit;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine.Scripting;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>Quyền của popup xin phép (hợp đồng đợt 3 B14(b)) — cùng tập với log quyền đã có.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum PromptPermission
    {
        [EnumMember(Value = "att")] Att,
        [EnumMember(Value = "push")] Push,
        [EnumMember(Value = "ads_consent")] AdsConsent
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum PromptKind
    {
        /// <summary>Màn hỏi trước của game ("bật thông báo để nhận quà?").</summary>
        [EnumMember(Value = "soft")] Soft,

        /// <summary>Popup của OS, hoặc form CMP với <c>ads_consent</c>.</summary>
        [EnumMember(Value = "system")] System
    }

    /// <summary>Lúc hỏi (hợp đồng B14) — danh sách ĐÓNG.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum PromptTrigger
    {
        [EnumMember(Value = "first_open")] FirstOpen,
        [EnumMember(Value = "level_win")] LevelWin,
        [EnumMember(Value = "level_start")] LevelStart,
        [EnumMember(Value = "settings")] Settings,
        [EnumMember(Value = "feature")] Feature,
        [EnumMember(Value = "reward")] Reward,
        [EnumMember(Value = "other")] Other
    }

    /// <summary>Chỉ dòng <see cref="PromptKind.Soft"/>.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum SoftResult
    {
        [EnumMember(Value = "accept")] Accept,
        [EnumMember(Value = "decline")] Decline,
        [EnumMember(Value = "dismiss")] Dismiss
    }

    /// <summary>Kết quả popup OS — chỉ dòng <c>system</c> mà SDK biết kết quả (hàm bọc Android).</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum SystemResult
    {
        [EnumMember(Value = "granted")] Granted,
        [EnumMember(Value = "denied")] Denied,
        [EnumMember(Value = "denied_dont_ask_again")] DeniedDontAskAgain
    }

    /// <summary>Một lần hiện popup xin quyền — <c>f_sdk_permission_prompt</c> (hợp đồng đợt 3 B14(b)).</summary>
    [Serializable]
    public class FPermissionPromptLog : AFalconLog
    {
        public PromptPermission permission;
        public PromptKind promptKind;
        [FKey(RemoveIfNull = true)] public PromptTrigger? trigger;
        [FKey(RemoveIfNull = true)] public SoftResult? softResult;
        [FKey(RemoveIfNull = true)] public SystemResult? systemResult;

        [Preserve]
        public FPermissionPromptLog()
        {
        }

        public override string Event => "f_sdk_permission_prompt";
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum AccountProvider
    {
        [EnumMember(Value = "google_play_games")] GooglePlayGames,
        [EnumMember(Value = "game_center")] GameCenter,
        [EnumMember(Value = "facebook")] Facebook,
        [EnumMember(Value = "apple")] Apple,
        [EnumMember(Value = "google")] Google,
        [EnumMember(Value = "other")] Other
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum AccountLinkAction
    {
        [EnumMember(Value = "prompt_shown")] PromptShown,
        [EnumMember(Value = "success")] Success,
        [EnumMember(Value = "cancel")] Cancel,
        [EnumMember(Value = "fail")] Fail,
        [EnumMember(Value = "unlink")] Unlink,

        /// <summary>Đổi sang tài khoản khác của CÙNG provider (không phải liên kết mới).</summary>
        [EnumMember(Value = "switch")] Switch,

        /// <summary>Liên kết xong thấy tài khoản đã có tiến độ ở máy khác.</summary>
        [EnumMember(Value = "save_conflict")] SaveConflict
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum AccountInitiatedBy
    {
        /// <summary>Người chơi bấm — chỉ loại này là lựa chọn.</summary>
        [EnumMember(Value = "user")] User,

        /// <summary>Game tự đăng nhập ngầm lúc mở.</summary>
        [EnumMember(Value = "auto")] Auto
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum AccountLinkTrigger
    {
        [EnumMember(Value = "first_open")] FirstOpen,
        [EnumMember(Value = "settings")] Settings,
        [EnumMember(Value = "reward")] Reward,
        [EnumMember(Value = "other")] Other
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum SaveConflictChoice
    {
        [EnumMember(Value = "keep_local")] KeepLocal,
        [EnumMember(Value = "keep_cloud")] KeepCloud,
        [EnumMember(Value = "cancel")] Cancel
    }

    /// <summary>
    /// Mời / đăng nhập / liên kết tài khoản — <c>f_sdk_account_link</c> (hợp đồng đợt 3 B14(c)). <b>Không gửi id tài khoản
    /// bên ngoài</b> — chỉ loại tài khoản và hành động. Nhiều loại tài khoản = nhiều dòng.
    /// </summary>
    [Serializable]
    public class FAccountLinkLog : AFalconLog
    {
        public AccountProvider provider;
        public AccountLinkAction action;
        [FKey(RemoveIfNull = true)] public AccountInitiatedBy? initiatedBy;
        [FKey(RemoveIfNull = true)] public AccountLinkTrigger? trigger;

        /// <summary>Chỉ <see cref="AccountLinkAction.SaveConflict"/>.</summary>
        [FKey(RemoveIfNull = true)] public SaveConflictChoice? conflictChoice;

        /// <summary>Chỉ <see cref="AccountLinkAction.SaveConflict"/>: tiến độ (level cao nhất đã qua) ở máy này / của tài khoản.</summary>
        [FKey(RemoveIfNull = true)] public int? localLevel;
        [FKey(RemoveIfNull = true)] public int? cloudLevel;

        [Preserve]
        public FAccountLinkLog()
        {
        }

        public override string Event => "f_sdk_account_link";

        /// <summary>Bỏ field chỉ dành cho <c>save_conflict</c> khi action khác. Trả câu cảnh báo nếu có bỏ.</summary>
        public string DropConflictFieldsUnlessConflict()
        {
            if (action == AccountLinkAction.SaveConflict) return null;
            if (conflictChoice == null && localLevel == null && cloudLevel == null) return null;
            conflictChoice = null;
            localLevel = null;
            cloudLevel = null;
            return $"Account.OnLink({provider}, {action}): conflictChoice / localLevel / cloudLevel chỉ dành cho SaveConflict — bỏ.";
        }
    }

    /// <summary>Danh sách provider đang liên kết (cho <c>app_open.linked_providers</c>), dạng chữ wire, sắp xếp ổn định.</summary>
    public static class LinkedProviders
    {
        public static List<string> ToWire(IEnumerable<AccountProvider> providers)
        {
            var set = new SortedSet<string>(StringComparer.Ordinal);
            if (providers != null)
                foreach (var provider in providers)
                    set.Add(JsonConvert.SerializeObject(provider).Trim('"'));
            return new List<string>(set);
        }
    }
}
