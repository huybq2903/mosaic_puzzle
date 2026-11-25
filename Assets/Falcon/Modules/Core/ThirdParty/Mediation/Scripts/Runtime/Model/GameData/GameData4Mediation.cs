using Falcon.Modules.Core.AccountData;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime
{
    [FGameDataType("mediation_data")]
    public class GameData4Mediation : FGameData<GameData4Mediation>
    {
        public string mediationVersion;

        public void SyncVersion()
        {
            if (mediationVersion == MediationVersion.VALUE) return;
            mediationVersion = MediationVersion.VALUE;
            UpdateToServer();
        }

        public override void OnUpdateFromServer()
        {
            SyncVersion();
        }
    }
}
