/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-18
 */

using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime
{
    /// <summary>
    /// Log luồng Floor Personalization + Databuckets. Chỉ có mặt khi bật một trong hai define:
    /// <br/>• <c>FLOOR_PERSONALIZATION_LOG</c> — chỉ log của module này (EditorBuild "Enable Log" tự thêm).
    /// <br/>• <c>ENABLE_DEBUG_LOG</c> — log của cả Mediation (như trước).
    /// <br/>Không bật thì lời gọi bị compiler bỏ hẳn (kể cả chuỗi nội suy), không tốn gì trong bản release.
    /// <br/>Lọc logcat: <c>adb logcat -s Unity | grep -E "FloorPersonalization|Databuckets"</c>.
    /// </summary>
    public static class FloorPersonalizationLog
    {
        public const string DEFINE = "FLOOR_PERSONALIZATION_LOG";

        [Conditional(DEFINE), Conditional("ENABLE_DEBUG_LOG")]
        public static void Info(string message)
        {
            Debug.Log("<color=#80FFFF>" + message + "</color>");
        }

        [Conditional(DEFINE), Conditional("ENABLE_DEBUG_LOG")]
        public static void Error(string message)
        {
            Debug.LogError(message);
        }
    }
}
