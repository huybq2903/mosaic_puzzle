/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-08-05
 */

using Falcon.Helpers.Devkit;
using UnityEngine;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Đo hiệu năng quãng chơi để đóng vào bản tin session_data lúc khép (§D7): zero event mới,
    /// zero volume mới. Phải là MonoBehaviour vì đếm cú tụt khung thì bắt buộc nhìn từng frame —
    /// sampler 1Hz chỉ thấy trung bình, không thấy cú giật.
    /// <br/>Update ở đây cố tình chỉ có vài phép cộng; mọi luật nằm ở <see cref="PerfSampleState"/>.
    /// Chạy nền thì Unity không gọi Update nên số liệu tự nhiên chỉ tính lúc foreground.
    /// </summary>
    [NoLazy]
    public class PerfSampleService : MonoSingleton<PerfSampleService>
    {
        private readonly PerfSampleState _state = new();
        private bool _paused;

        // Lấy lười ở frame đầu: MonoSingleton dựng trong pha [NoLazy], container chưa chắc đã sẵn để hỏi Instance.
        private PerfPackRepository _pack;
        private PerfPackRepository Pack => _pack ??= PerfPackRepository.Instance;
        private BatteryLogService _battery;
        private BatteryLogService Battery => _battery ??= BatteryLogService.Instance;
        private bool _firstFrameReported;
        private AppIssueLogService _issues;
        private AppIssueLogService Issues => _issues ??= AppIssueLogService.Instance;
        private DeviceSignalService _device;
        private DeviceSignalService Device => _device ??= DeviceSignalService.Instance;

        private void OnEnable()
        {
            Application.lowMemory += OnLowMemory;
        }

        private void OnDisable()
        {
            Application.lowMemory -= OnLowMemory;
        }

        private void Update()
        {
            // unscaled: game chỉnh Time.timeScale (pause menu, slow-mo) không được làm lệch fps đo được
            if (!_firstFrameReported)
            {
                _firstFrameReported = true;
                LoadStepLogService.Instance.FirstFrame(); // B2: trạm first_frame (chỉ lần chạy đầu sau cài)
            }

            var delta = Time.unscaledDeltaTime;
            _state.AddFrame(delta);
            Pack.AddFrame(delta);
            Battery.Poll(delta);
            Device.Poll(delta);
            Issues.Tick(delta);
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            // Chỉ resume THẬT (sau một lần pause) mới bỏ khung: Unity cũng gọi Pause(false) lúc khởi động,
            // bỏ khung đầu đời app là đổi số mà hợp đồng không nhắc tới.
            if (pauseStatus)
            {
                _paused = true;
                return;
            }

            if (!_paused) return;
            _paused = false;
            _state.MarkResumed();
            Pack.MarkResumed();
            Device.Refresh();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            Issues.OnFocusChanged(hasFocus);
            if (hasFocus) Device.Refresh();
        }

        private void OnApplicationQuit()
        {
            Issues.MarkClean();
        }

        private void OnLowMemory()
        {
            _state.AddMemoryWarning();
            Pack.AddMemoryWarning();
            Issues.OnLowMemory();
        }

        /// <summary>Lấy tóm tắt của quãng vừa rồi và reset — gọi lúc dựng bản tin session_data.</summary>
        public PerfSummary TakeSummary()
        {
            return _state.TakeAndReset();
        }
    }
}
