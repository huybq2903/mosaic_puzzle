/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-09
 */

using System;
using System.Collections.Generic;
using Falcon.Helpers.Devkit;
using JetBrains.Annotations;
using UnityEngine.Scripting;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Bản tin CỤM exposure UI — <b>một dòng = một cụm</b> (surface × ngữ cảnh × trigger) trong một
    /// stretch, mỗi hành vi là một cột đếm. Ký với loader 09/11, thay <c>f_sdk_ui_impression</c> (hình
    /// dạng một-dòng-một-hành-vi, đóng băng ở 1.7.0). Đổi hình dạng thì đổi TÊN event, không dựa vào
    /// <c>sdk_version</c> — luật chung kho ghi cùng ngày.
    /// <br/>⚠ Luật đọc: tỉ lệ chỉ đúng khi CỘNG QUA KHOẢNG (<c>sum(clickCount) / sum(impressionCount)</c>).
    /// Tỉ lệ trên từng dòng là SAI: lần hiện và lần bấm có thể rơi vào hai cửa sổ xả, nên dòng
    /// <c>impressionCount = 0</c>, <c>clickCount &gt; 0</c> là bình thường.
    /// <br/>Không có cột level: tiến độ đọc <c>maxPassedLevel</c> chung của mọi dòng (tiến độ lúc XẢ).
    /// </summary>
    [Serializable]
    public class FUiExposureLog : AFalconLog, IPerfPackCarrier
    {
        /// <summary>Bề mặt UI — vocab của từng game, hoặc hằng số dùng chung ở <see cref="FUiSurface"/>. Luôn có.</summary>
        public string surfaceId;

        /// <summary>Bốn cột đếm LUÔN lên dây, kể cả khi bằng 0: trong một cụm, 0 là số thật của cửa sổ xả.</summary>
        public int impressionCount;

        /// <inheritdoc cref="impressionCount"/>
        public int clickCount;

        /// <inheritdoc cref="impressionCount"/>
        public int dismissCount;

        /// <summary>Tập con của <see cref="impressionCount"/> — bất biến từng dòng, xem <see cref="UiTally"/>.</summary>
        public int adNotReadyCount;

        /// <summary>VÌ SAO bề mặt hiện ra, trong khoá gộp — xem <c>FUiApi.OnImpression</c>.</summary>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string trigger;

        [FKey(RemoveIfNull = true)] [CanBeNull]
        public Dictionary<string, object> extraMeta;

        /// <summary>Ngữ cảnh event live-op của cụm — xem <see cref="UiExposureState.Entry.liveOps"/>.</summary>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string liveOpsEvent;

        /// <inheritdoc cref="liveOpsEvent"/>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string liveOpsCycleId;

        /// <inheritdoc cref="liveOpsEvent"/>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string liveOpsRoundId;

        /// <inheritdoc cref="liveOpsEvent"/>
        [FKey(RemoveIfNull = true)] public int? liveOpsStage;

        /// <summary>
        /// Ngữ cảnh TÍNH NĂNG của cụm — chép từ cùng một <see cref="LiveOpsRef"/>; cả ba nằm trong
        /// khoá gộp của <see cref="UiExposureState"/>.
        /// </summary>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string feature;

        /// <inheritdoc cref="feature"/>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string featureInstanceId;

        /// <inheritdoc cref="feature"/>
        [FKey(RemoveIfNull = true)] public int? featureStage;

        [Preserve]
        public FUiExposureLog()
        {
        }

        public FUiExposureLog(UiExposureState.Entry entry)
        {
            surfaceId = entry.surfaceId;
            impressionCount = entry.impressionCount;
            clickCount = entry.clickCount;
            dismissCount = entry.dismissCount;
            adNotReadyCount = entry.adNotReadyCount;
            trigger = entry.trigger;
            extraMeta = entry.extraMeta;
            liveOpsEvent = entry.liveOps?.liveOpsEvent;
            liveOpsCycleId = entry.liveOps?.liveOpsCycleId;
            liveOpsRoundId = entry.liveOps?.liveOpsRoundId;
            liveOpsStage = entry.liveOps?.liveOpsStage;
            feature = entry.liveOps?.feature;
            featureInstanceId = entry.liveOps?.featureInstanceId;
            featureStage = entry.liveOps?.featureStage;
        }

        public override string Event => "f_sdk_ui_exposure";

        public override Dictionary<string, object> ToDictionary()
        {
            var dictionary = base.ToDictionary();
            dictionary.Remove(nameof(extraMeta));
            return dictionary.MergeExtras(extraMeta, this);
        }
    }
}
