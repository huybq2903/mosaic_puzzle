/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-09
 */

using Falcon.Helpers.Devkit;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    public class LevelReviveLogService : MySingleton<LevelReviveLogService>
    {
        private readonly LogScheduleService _logScheduleService;
        private readonly LevelTurnService _levelTurnService;

        public LevelReviveLogService(
            LogScheduleService logScheduleService, LevelTurnService levelTurnService)
        {
            _logScheduleService = logScheduleService;
            _levelTurnService = levelTurnService;
        }

        public void LogRevive(LevelReviveParam param)
        {
            if (param == null) return;

            param.playTurnId ??= _levelTurnService.OpenPlayTurnId;
            if (param.playTurnId == null)
                AnalyticLogger.Instance.Warning(
                    "LevelRevive: không có lượt chơi nào đang mở — dòng hồi sinh sẽ VẮNG playTurnId và " +
                    "không nối được về ván nào. Luật tích hợp: chỉ báo Fail SAU khi người chơi đã từ chối " +
                    "hồi sinh (lượt phải còn mở trong lúc popup hiện). SDK không đoán id lượt đã đóng.");

            _logScheduleService.Enqueue(new FLevelReviveLog(param));
        }
    }
}
