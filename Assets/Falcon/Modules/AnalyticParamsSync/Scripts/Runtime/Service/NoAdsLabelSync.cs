/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-12
 */

using System;
using System.Threading;
using System.Threading.Tasks;
using Falcon.Helpers.Devkit;
using Falcon.Modules.Core.BigData;
using Falcon.Modules.Core.ThirdParty.Mediation.Runtime;
using UnityEngine;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.AnalyticParamsSync
{
    /// <summary>
    /// Tự gắn nhãn người chơi <see cref="FUserLabelKey.NO_ADS"/> theo cờ bỏ quảng cáo của module
    /// Mediation (<see cref="GameData4RemoveAds"/>) — game không phải tự gọi <c>Label.User</c>.
    /// <br/>Module này đặt được việc đó vì nó VỐN ĐÃ tham chiếu Mediation; BigData thì không được
    /// (Mediation phụ thuộc BigData, không có chiều ngược lại).
    /// <br/>Hai mốc kiểm:
    /// <br/>· ĐẦU PHIÊN, sau khi đăng nhập xong (cờ là FGameData đồng bộ server — cài lại app thì
    ///   phải chờ bản server về mới đúng). Chạy NỀN, không chặn chuỗi <c>IInit</c> tuần tự.
    /// <br/>· MỖI LẦN APP PAUSE — bắt lần mua giữa phiên mà không phải móc vào luồng mua.
    /// <br/>Chỉ gửi KHI ĐỔI so với lần gửi trước (nhớ ở máy): có quyền ⇒ <c>true</c>; mất quyền sau khi đã
    /// gửi <c>true</c> ⇒ <c>null</c> (lệnh gỡ nhãn). Chưa từng có quyền ⇒ không gửi gì — "không có" là
    /// mặc định, gửi cho mọi user chỉ tốn bản tin.
    /// <br/>⚠ Giới hạn: sổ "đã gửi" nằm ở máy. Cài lại app rồi MẤT quyền ngay lượt đó thì không gỡ được
    /// nhãn cũ phía server — chấp nhận, ca hiếm.
    /// <br/>⚠ Kho chưa dựng đường nhãn người chơi (loader 12/11) — dòng gửi trước vẫn được giữ raw.
    /// </summary>
    [NoLazy]
    public class NoAdsLabelSync : MySingleton<NoAdsLabelSync>, IInit, ITerminal
    {
        private const string SENT_TRUE_PREF_KEY = "falcon_aps_no_ads_label_sent";

        private readonly object _lock = new();

        public Task Init(CancellationToken cancellationToken = default)
        {
            _ = SyncAfterLogin();
            return Task.CompletedTask;
        }

        public void OnPostStop()
        {
            Sync();
        }

        private async Task SyncAfterLogin()
        {
            try
            {
                // Đăng nhập hỏng (offline) vẫn kiểm — cờ có bản lưu ở máy.
                await AccountLoginListener.WaitLogin();
                Sync();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private void Sync()
        {
            lock (_lock)
            {
                var hasNoAds = GameData4RemoveAds.Instance.removeAds;
                var sentTrue = PlayerPrefs.GetInt(SENT_TRUE_PREF_KEY, 0) == 1;
                if (hasNoAds == sentTrue) return;

                if (hasNoAds)
                {
                    FalconBigDataController.Label.User(FUserLabelKey.NO_ADS, true);
                    PlayerPrefs.SetInt(SENT_TRUE_PREF_KEY, 1);
                }
                else
                {
                    FalconBigDataController.Label.User(FUserLabelKey.NO_ADS, null);
                    PlayerPrefs.DeleteKey(SENT_TRUE_PREF_KEY);
                }

                PlayerPrefs.Save();
            }
        }
    }
}
