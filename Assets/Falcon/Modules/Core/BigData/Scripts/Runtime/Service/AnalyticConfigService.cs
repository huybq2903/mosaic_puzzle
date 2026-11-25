/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2025-06-12
 */

using Falcon.Helpers.Devkit;
using Falcon.Modules.Core.RemoteConfig;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    public class AnalyticConfigService : MySingleton<AnalyticConfigService>, IConfigUpdatedReact
    {
        private readonly LazyVal<AnalyticConfig> _config;

        public AnalyticConfigService(FConfigController controller)
        {
            _config = new(controller.Config<AnalyticConfig>);
        }
        
        public bool ShouldNotSendLogToServer => _config.Value.fCoreAnalyticShouldNotSendLogToServer;
        public bool Testing => _config.Value.fCoreAnalyticTesting;
        public string TestingSingleUrl => _config.Value.fCoreAnalyticTestingSingleUrl;
        public string TestingBatchUrl => _config.Value.fCoreAnalyticTestingBatchUrl;
        public float BatchLogSendSec => _config.Value.fBatchLogSendSec;
        public int AppOpenMinBackgroundSec => _config.Value.fAppOpenMinBackgroundSec;
        public int OfferAttributionGraceSec => _config.Value.fOfferAttributionGraceSec;
        /// <summary>true = tách mỗi lần gọi load một dòng · false = gộp cụm — xem <see cref="IAdLoadLogConfig"/>.</summary>
        public bool AdLoadLogSplit => _config.Value.fAdLoadLogSplit;
        /// <summary>Van S2 phần 2 — xem <see cref="AnalyticConfig.fAppIssueEmergencyEnabled"/>.</summary>
        public bool AppIssueEmergencyEnabled => _config.Value.fAppIssueEmergencyEnabled;
        /// <summary>Van đọc native định kỳ — xem <see cref="AnalyticConfig.fDeviceSignalsEnabled"/>.</summary>
        public bool DeviceSignalsEnabled => _config.Value.fDeviceSignalsEnabled;
        

        public void OnConfigUpdated()
        {
            _config.Reset();
        }
    }
}