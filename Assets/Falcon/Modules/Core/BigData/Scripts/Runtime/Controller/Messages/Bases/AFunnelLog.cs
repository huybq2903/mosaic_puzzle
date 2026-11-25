/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2025-06-12
 */

using System;
using System.Collections.Generic;
using Falcon.Helpers.Devkit;
using UnityEngine.Scripting;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    [Serializable]
    public class AFunnelLog : AFalconLog, IPerfPackCarrier
    {
        public FunnelParam param;
        public string funnelDay;

        /// <summary>
        /// Hình dạng van ĐANG ÁP tại client lúc log — SDK tự đóng trong decor
        /// (<see cref="FunnelShapeWire.ToWireName"/>), game không đụng. Không phải hằng số
        /// thiết kế mà là TRẠNG THÁI client có thể sai — chính vì thế nó mang tin: catalog giữ
        /// shape chuẩn, field này chở shape thực tế, lệch nhau = quên Register (DQ bắt được ngay
        /// ca funnel rơi về luật ftue trượt trắng, khỏi nội suy từ hành vi stream).
        /// </summary>
        public string funnelShape;

        /// <summary>
        ///     Thời gian CHƠI từ mốc VÀO PHỄU của chu kỳ này tới bước này, đơn vị GIÂY — SDK tự
        ///     đóng, game không truyền (luật §H3: giá trị máy suy được thì không giao tay dev).
        ///     <br/>Đồng hồ là <c>TotalPlayTime</c>: monotonic, CHỈ chạy khi app ở foreground, có
        ///     persist. Không phải wall-clock — user chỉnh giờ máy tiến thì wall-clock phồng vĩnh
        ///     viễn mà loader chỉ clamp được chiều tụt (án 12/08).
        ///     <br/>Mốc vào phễu = bước ĐẦU TIÊN hợp lệ của funnel trong chu kỳ, KHÔNG phải bước
        ///     <see cref="FFunnelAction.Join"/>: funnel mới phần lớn không có bước join
        ///     (<c>eligible</c>/<c>open</c>), neo theo action là bỏ trắng đúng mấy event đó.
        ///     Bước đầu chu kỳ mang giá trị 0. Chu kỳ đổi (<c>cycleId</c> khác) là hai mốc reset —
        ///     không thì mùa 2 đo từ mùa 1 (đúng bug <c>funnelDay</c> đã phải sửa).
        ///     <br/>Vắng mặt = KHÔNG BIẾT (sổ mốc lệch sổ phiên, delta ra số âm), không phải 0.
        /// </summary>
        [FKey(RemoveIfNull = true)] public long? playSecSinceEnter;

        /// <summary>
        ///     Thời gian CHƠI từ bước HỢP LỆ gần nhất của funnel này (cùng chu kỳ) tới bước này,
        ///     đơn vị GIÂY — cùng đồng hồ và cùng luật vắng-mặt với <see cref="playSecSinceEnter"/>.
        ///     <br/>"Bước gần nhất" tính theo THỜI ĐIỂM LOG, không theo thang <c>priority</c>:
        ///     <see cref="FunnelShape.OncePerCycle"/> cho phép nhảy cóc mốc (mua vượt tier là hợp
        ///     lệ), neo theo "priority ngay dưới" thì đúng ca nhảy cóc lại không có số.
        ///     <br/>Bước đầu chu kỳ: VẮNG MẶT (chưa có bước trước, không gửi 0). Bước bị van đánh
        ///     trượt KHÔNG dịch mốc — nếu dịch thì bước sau đo từ một bước chưa từng lên wire.
        /// </summary>
        [FKey(RemoveIfNull = true)] public long? playSecSincePrevStep;

        /// <summary>
        ///     Số level MỚI VƯỢT QUA từ mốc vào phễu của chu kỳ tới bước này — SDK tự đóng, đọc
        ///     <c>MaxPassedLevel</c> của SDK chứ không đọc <c>currentLevel</c> game truyền vào
        ///     <c>OnStep</c> (field đó optional, nhiều chỗ không truyền thì cột rỗng lỗ chỗ).
        ///     <br/>Vì sao cần cạnh <see cref="playSecSinceEnter"/>: giờ chơi ĐẾM CẢ lúc user mở
        ///     app rồi để đó (ngồi lobby 20 phút vẫn cộng 1.200 giây), còn số này là tiến độ thật.
        ///     Hai số bổ nghĩa cho nhau — "mốc 5 cần 40 phút chơi và 12 level" nói được nhiều hơn
        ///     hẳn chỉ một trong hai.
        ///     <br/>Đếm level VƯỢT QUA, KHÔNG đếm số ván chơi: thua 10 lần một level thì khoảng
        ///     cách vẫn là 0. Muốn đo công sức (số ván kể cả thua) thì cần một bộ đếm ván toàn cục
        ///     khác — SDK hiện chỉ có <c>playCount</c> theo từng level.
        ///     <br/>Cùng luật mốc với hai field giờ chơi: bước đầu chu kỳ = 0, chu kỳ đổi là
        ///     reset, bước bị veto không dịch mốc, số âm (xoá data) = VẮNG MẶT chứ không phải 0.
        /// </summary>
        [FKey(RemoveIfNull = true)] public long? levelsSinceEnter;

        /// <summary>
        ///     Số level MỚI VƯỢT QUA từ bước hợp lệ gần nhất của funnel này (cùng chu kỳ) tới bước
        ///     này — cùng nguồn số và cùng luật vắng-mặt với <see cref="levelsSinceEnter"/>.
        ///     Bước đầu chu kỳ: VẮNG MẶT.
        /// </summary>
        [FKey(RemoveIfNull = true)] public long? levelsSincePrevStep;

        [Preserve]
        protected AFunnelLog()
        {
        }

        public AFunnelLog(FunnelParam param)
        {
            this.param = param;
        }

        protected AFunnelLog(
            string funnelName, string action, int priority,
            int? currentLevel = null, FParam logParams = null
        )
        {
            if (logParams is FunnelParam funnelParam)
            {
                funnelParam.funnelName = funnelName;
                funnelParam.action = action;
                funnelParam.priority = priority;
                funnelParam.currentLevel = currentLevel;
                param = funnelParam;
            }
            else
            {
                param = new OldCodeSupportFunnelParam()
                {
                    funnelName = funnelName,
                    action = action,
                    priority = priority,
                    currentLevel = currentLevel,
                    extraMeta = logParams?.ToDictionary()
                };
            }

            AnalyticLogger.Instance.Info($"{GetType().Name}:{param.ToJson()}");
            param.CorrectValues();
        }

        public override string Event => "f_sdk_funnel_data";

        public override Dictionary<string, object> ToDictionary()
        {
            var result = base.ToDictionary();
            result.Remove(nameof(param));
            foreach (var (key, value) in param.ToDictionary()) 
                result.PutIfAbsentAndNotNull(key, value);
            return result;
        }
    }
}