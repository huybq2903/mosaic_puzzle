/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-10-26
 */

using Falcon.Helpers.Devkit;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Vai GIỮ-STATE cho <see cref="AdUnitFactsState"/> — cùng hình dạng (và cùng lý do) với
    /// <see cref="AdViewCache"/>: bên GHI là callback mediation, bên ĐỌC là decorator lúc gửi, không
    /// đảm bảo cùng thread; và decorator ctor-inject được vì class này ZERO đường tới
    /// <c>LogScheduleService</c> (án deadlock DI 12/08).
    /// </summary>
    public class AdUnitFactsCache : MySingleton<AdUnitFactsCache>
    {
        private readonly AdUnitFactsState _state = new();
        private readonly object _lock = new();
        private readonly ITimeRepository _timeRepository;

        public AdUnitFactsCache(ITimeRepository timeRepository)
        {
            _timeRepository = timeRepository;
        }

        /// <inheritdoc cref="AdUnitFactsState.Record"/>
        public void Record(AdLoadResultParam param)
        {
            lock (_lock) _state.Record(param, _timeRepository.CurrentTimeMillis);
        }

        /// <inheritdoc cref="AdUnitFactsState.ApplyTo"/>
        public void ApplyUnitFacts(AdParam param)
        {
            lock (_lock)
            {
                _state.ApplyTo(param);
                // Dán xong là TIÊU THỤ: sự thật của một quảng cáo chỉ dùng cho đúng một lượt chiếu.
                _state.ConsumeAdFacts(param?.adUnitId);
            }
        }

        /// <inheritdoc cref="AdUnitFactsState.Get"/>
        public AdUnitFactsState.UnitFacts Get(string adUnitId)
        {
            lock (_lock) return _state.Get(adUnitId);
        }
    }
}
