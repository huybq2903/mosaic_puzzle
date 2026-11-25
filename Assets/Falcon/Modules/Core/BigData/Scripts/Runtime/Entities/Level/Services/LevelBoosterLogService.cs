/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-10
 */

using Falcon.Helpers.Devkit;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    public class LevelBoosterLogService : MySingleton<LevelBoosterLogService>
    {
        private readonly LogScheduleService _logScheduleService;
        private readonly LevelTurnService _levelTurnService;

        public LevelBoosterLogService(
            LogScheduleService logScheduleService, LevelTurnService levelTurnService)
        {
            _logScheduleService = logScheduleService;
            _levelTurnService = levelTurnService;
        }

        public void LogBooster(LevelBoosterParam param)
        {
            if (param == null) return;

            if (!param.HasBooster)
            {
                AnalyticLogger.Instance.Warning(
                    "LevelBooster: thiếu boosterId — BỎ DÒNG. 'Đã dùng một booster nào đó' không trả lời được câu " +
                    "nào; truyền mã theo sổ Resource.RegisterBooster.");
                return;
            }

            param.playTurnId ??= _levelTurnService.OpenPlayTurnId;
            if (param.playTurnId == null)
                AnalyticLogger.Instance.Warning(
                    "LevelBooster: không có lượt chơi nào đang mở — dòng booster sẽ VẮNG playTurnId. Booster chọn TRƯỚC " +
                    "ván thì khai ở preBoostersUsed của Level.OnStart, không phải ở đây.");

            _logScheduleService.Enqueue(new FLevelBoosterLog(param));
        }
    }
}
