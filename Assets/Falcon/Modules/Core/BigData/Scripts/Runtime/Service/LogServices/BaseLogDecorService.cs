/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2025-06-12
 */

using System.Collections.Concurrent;
using System.Collections.Generic;
using Falcon.Helpers.Devkit;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    public class BaseLogDecorService : MySingleton<BaseLogDecorService>, ILogDecorator<AFalconLog>
    {
        private readonly FCentralUserParamService _centralUserParamService;
        private readonly ITimeRepository _timeRepository;
        private readonly IDataPool _dataPool;
        private readonly PerfPackRepository _perfPackRepository;
        
        private readonly ConcurrentDictionary<string, BasicPoolData<int>> _createId = new();
        private readonly ConcurrentDictionary<string, BasicPoolData<int>> _sendId = new();

        public BaseLogDecorService(
            FCentralUserParamService centralUserParamService,
            ITimeRepository timeRepository,
            IDataPool dataPool,
            PerfPackRepository perfPackRepository
        )
        {
            _perfPackRepository = perfPackRepository;
            _dataPool = dataPool;
            _centralUserParamService = centralUserParamService;
            _timeRepository = timeRepository;
        }

        /// <summary>
        /// Central param nào là định danh của MỘT khoảnh khắc thì không được dán lên bản tin tổng
        /// hợp: giá trị lúc gửi chỉ đúng cho khoảnh khắc cuối cùng trong cụm.
        /// </summary>
        private static readonly HashSet<string> kMomentOnlyKeys = new()
        {
            TurnCustomInfoRepository.PLAY_TURN_ID_KEY
        };

        public int Priority => -100;

        public void Decor(AFalconLog log)
        {
            log.createdDateLocal = MyTime.DateToString(_timeRepository.LocalNow());
            if (log.preview)
            {
                // Bản chụp khẩn: không đốt số thứ tự (dòng khẩn không mang số — EmergencyRows.Stamp), không đặt lại bộ
                // đếm "kể từ bản tin trước" của gói hiệu năng (chỉ fps / khung chậm nhất của lúc dựng).
                log.decorFields = new Dictionary<string, object>();
                if (log is IPerfPackCarrier) _perfPackRepository.PeekPack(log.decorFields);
                else if (log is IPerfScreenCarrier) _perfPackRepository.PutScreen(log.decorFields);
                return;
            }

            log.logTypeCreateId = GetCreateId(log);
            log.logTypeSendId = GetSendId(log);

            // Gói hiệu năng (hợp đồng đợt 3 B1): chụp ĐÚNG MỘT LẦN mỗi log, ở đây — không ở InjectDictionary (có thể bị gọi
            // nhiều lần, mỗi lần lại đặt bộ đếm "kể từ bản tin trước"). Chỉ log đã đánh dấu, không bao giờ lên bản tin tổng
            // hợp. Đọc giá trị đã giữ sẵn, không gọi API Unity.
            if (log.decorFields != null || log.IsSpanRecord) return;
            if (log is IPerfPackCarrier)
            {
                log.decorFields = new Dictionary<string, object>();
                _perfPackRepository.PutPack(log.decorFields);
            }
            else if (log is IPerfScreenCarrier)
            {
                log.decorFields = new Dictionary<string, object>();
                _perfPackRepository.PutScreen(log.decorFields);
            }
        }

        /// <summary>
        /// Chèn central user params vào payload. Bản tin TỔNG HỢP (<see cref="PlainLog.IsSpanRecord"/>)
        /// thì bỏ qua các key chỉ đúng cho MỘT khoảnh khắc — xem <see cref="kMomentOnlyKeys"/>.
        /// </summary>
        public Dictionary<string, object> InjectDictionary(PlainLog log, Dictionary<string, object> dictionary)
        {
            var spanRecord = log?.IsSpanRecord ?? false;
            foreach (var (key, value) in _centralUserParamService.GetUserParams())
            {
                if (spanRecord && kMomentOnlyKeys.Contains(key)) continue;
                dictionary.PutIfAbsent(key, value);
            }

            if (log is AFalconLog { decorFields: { } fields })
                foreach (var (key, value) in fields)
                    dictionary.PutIfAbsent(key, value);

            return dictionary;
        }

        private int GetCreateId(AFalconLog log)
        {
            return _createId
                .GetOrAdd(log.Event + "app_id", key => new BasicPoolData<int>(_dataPool, key, -1))
                .Compute(val => val + 1);
        }

        public int GetSendId(AFalconLog log)
        {
            return _sendId
                .GetOrAdd(log.Event + "_send_id", key => new BasicPoolData<int>(_dataPool, key, -1))
                .Compute(val => val + 1);
        }
    }
}