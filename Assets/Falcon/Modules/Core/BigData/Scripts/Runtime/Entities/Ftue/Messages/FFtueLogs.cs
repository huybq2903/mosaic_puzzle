/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-10
 */

using System;
using Falcon.Helpers.Devkit;
using JetBrains.Annotations;
using UnityEngine.Scripting;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Họ <c>f_sdk_ftue_*</c> — ký với loader 10/11, thay funnel <c>ftue</c> của §D6. Mỗi mốc một event,
    /// không field nào điều khiển nghĩa của field khác. Không mang cột thời gian: mọi dòng đã có
    /// <c>totalPlayTime</c> (đếm từ 0 lúc cài) và <c>client_sent_time</c> — tính khoảng là việc của server.
    /// </summary>
    [Serializable]
    public class FFtueFirstScreenLog : AFalconLog
    {
        [Preserve]
        public FFtueFirstScreenLog()
        {
        }

        public override string Event => "f_sdk_ftue_first_screen";
    }

    /// <inheritdoc cref="FFtueFirstScreenLog"/>
    [Serializable]
    public class FFtueTutorialStartLog : AFalconLog
    {
        /// <summary>Phiên bản tutorial do game đặt (vd <c>tut_v3</c>). Vắng nếu game không khai.</summary>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string tutorialId;

        [Preserve]
        public FFtueTutorialStartLog()
        {
        }

        public override string Event => "f_sdk_ftue_tutorial_start";
    }

    /// <inheritdoc cref="FFtueFirstScreenLog"/>
    [Serializable]
    public class FFtueTutorialStepLog : AFalconLog
    {
        /// <inheritdoc cref="FFtueTutorialStartLog.tutorialId"/>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string tutorialId;

        /// <summary>Thứ tự bước TRONG tutorial này, ≥ 1. Không phải liền mạch.</summary>
        public int stepIndex;

        /// <summary>Tên bước — vocab của từng game, snake_case. Không so chéo game.</summary>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string stepName;

        [Preserve]
        public FFtueTutorialStepLog()
        {
        }

        public override string Event => "f_sdk_ftue_tutorial_step";
    }

    /// <inheritdoc cref="FFtueFirstScreenLog"/>
    [Serializable]
    public class FFtueTutorialCompleteLog : AFalconLog
    {
        /// <inheritdoc cref="FFtueTutorialStartLog.tutorialId"/>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string tutorialId;

        [Preserve]
        public FFtueTutorialCompleteLog()
        {
        }

        public override string Event => "f_sdk_ftue_tutorial_complete";
    }

    /// <inheritdoc cref="FFtueFirstScreenLog"/>
    [Serializable]
    public class FFtueTutorialSkipLog : AFalconLog
    {
        /// <inheritdoc cref="FFtueTutorialStartLog.tutorialId"/>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string tutorialId;

        /// <summary>Bước xa nhất đã gửi trước khi bỏ qua — SDK tự điền. Vắng nếu bỏ qua trước bước đầu.</summary>
        [FKey(RemoveIfNull = true)] public int? lastStepIndex;

        [Preserve]
        public FFtueTutorialSkipLog()
        {
        }

        public override string Event => "f_sdk_ftue_tutorial_skip";
    }
}
