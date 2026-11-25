/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-02
 */

using Falcon.Modules.Core.Network;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime.LuaDynamic
{
    /// <summary>
    /// Server gửi Lua script điều khiển load / show interstitial + rewarded (một lần đầu game, sau
    /// <c>sc_ad_bid_floor</c>). <c>md5LuaScript</c> trùng script đang chạy thì bỏ qua, không phải so cả chuỗi.
    /// <c>luaScript</c> rỗng (và md5 khác script đang chạy) = tắt Lua, quay về luồng cũ.
    /// Danh sách ad unit lấy từ <c>sc_ad_bid_floor</c>; có id AdMob thì AdMob được init sau khi MAX init xong,
    /// xem LuaAdMonetize.OnMaxSdkInitialized. Script và hợp đồng hàm: xem README.md cùng thư mục.
    /// </summary>
    [FAMessage("sc_ad_monetize_lua_script")]
    public class SCAdMonetizeLuaScript : SCMessage
    {
        public string luaScript;

        /// <summary>md5 của luaScript, để so nhanh script có đổi không. Rỗng = so cả chuỗi.</summary>
        public string md5LuaScript;

        public string idLuaScript;

        public override void OnData()
        {
#if MAX_ENABLE
            LuaAdMonetize.ApplyScript(luaScript, md5LuaScript, idLuaScript);
#endif
        }
    }
}
