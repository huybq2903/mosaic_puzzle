/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-02
 */

#if MAX_ENABLE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Falcon.Helpers.Devkit;
using Falcon.Modules.Core.BigData;
using Falcon.Modules.Core.SaveLoad.Runtime;
using Falcon.Modules.Core.ThirdParty.Firebase.Scripts.Runtime;
using Falcon.Modules.Level.Core;
using MoonSharp.Interpreter;
using UnityEngine;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime.LuaDynamic
{
    /// <summary>
    /// ĐIỂM VÀO của LuaDynamic: chạy Lua script do server gửi (<see cref="SCAdMonetizeLuaScript"/>) để điều
    /// khiển việc load interstitial / rewarded trên BỘ AD UNIT RIÊNG, độc lập với MultiCall. Tự khởi động
    /// (RuntimeInitializeOnLoadMethod); server chưa gửi script (hoặc gửi rỗng) thì module đứng yên.
    ///
    /// Luồng: <c>sc_ad_bid_floor</c> (danh sách ad unit: adUnitId, multiplier, adUnitIdType, ... -> có sẵn trong
    /// <c>context.list...</c>) -> <c>sc_ad_monetize_lua_script</c> (một lần đầu game, so bằng md5) -> init MAX ->
    /// báo Lua OnInitMaxSuccess() trong callback MAX init -> init AdMob (CHỈ khi danh sách bid floor có id AdMob)
    /// -> báo Lua OnInitAdmobSuccess() khi AdMob init xong.
    /// Script bắt đầu load các id default trong 2 hàm này.
    ///
    /// Unity -> Lua: ĐÚNG 4 hàm mỗi format (thiếu hàm nào thì bỏ qua hàm đó):
    ///  - OnInterstitialLoadSuccess(adUnitId, linkedAdUnitId) / OnRewardedLoadSuccess(adUnitId, linkedAdUnitId)
    ///  - OnInterstitialLoadFail(adUnitId, linkedAdUnitId)    / OnRewardedLoadFail(adUnitId, linkedAdUnitId)
    ///  - OnInterstitialShowSuccess(adUnitId, linkedAdUnitId) / OnRewardedShowSuccess(adUnitId, linkedAdUnitId)
    ///    linkedAdUnitId = nil, trừ bản load AdMob có gắn id MAX (xem <see cref="LuaAdContext"/>).
    ///  - IsShowInterstitial()                / IsShowRewarded()
    ///    game hỏi bất cứ lúc nào qua <c>MediationManager.IsShowInterstitial/IsShowRewarded</c>, tham số ở
    ///    <c>context.params</c>.
    /// Lua -> Unity: <c>context.Max.LoadInterstitial/LoadRewarded(adUnitId, floor, delayTime)</c>,
    /// <c>context.Admob.LoadInterstitial/LoadRewarded(adUnitId, floor, delayTime [, linkedAdUnitId])</c>.
    /// OnInit...Success xin load các id default; từ đó mỗi kết quả load / show là một lượt Lua trả lại các lệnh
    /// load tiếp theo.
    ///
    /// Show (FalconMaxService gọi, Lua KHÔNG tham gia): interstitial = ad inter có eCpm cao nhất; rewarded =
    /// ad có eCpm cao nhất trong cả inter lẫn RW. Lua không có ad sẵn thì FalconMaxService chạy luồng cũ.
    /// </summary>
    public static class LuaAdMonetize
    {
        private const string _TAG = "[LuaAd]";
        private const float _TICK_SECONDS = 0.25f;

        private const string _FN_IS_SHOW_INTER = "IsShowInterstitial";
        private const string _FN_IS_SHOW_REWARD = "IsShowRewarded";
        private const string _FN_INIT_MAX = "OnInitMaxSuccess";
        private const string _FN_INIT_ADMOB = "OnInitAdmobSuccess";
        private const string _FN_NETWORK_BACK = "OnNetworkBack";

        private static readonly string[] _AllFunctions =
        {
            _FN_INIT_MAX, _FN_INIT_ADMOB, _FN_NETWORK_BACK,
            "OnInterstitialLoadSuccess", "OnInterstitialLoadFail", "OnInterstitialShowSuccess", _FN_IS_SHOW_INTER,
            "OnRewardedLoadSuccess", "OnRewardedLoadFail", "OnRewardedShowSuccess", _FN_IS_SHOW_REWARD
        };

        private static readonly Bridge _bridge = new Bridge();

        //đếm lần xin load — phân biệt lịch của script cũ / script mới
        private static long _requestSeq;

        //mốc init xong của từng mediation: lịch xin TRƯỚC mốc này tính delay từ mốc này
        private static readonly Dictionary<string, DateTime> _mediationReadyUtc = new Dictionary<string, DateTime>();

        //AdMob: danh sách sc_ad_bid_floor có id AdMob -> Lua tự init MobileAds sau callback MAX init thành công
        //(xem InitAdmob)
        private static bool _maxInitDone;
        private static bool _bidFloorHasAdmob;
        private static bool _admobInitRequested;
        private static bool _admobInitDone;

        //các hàm OnInit...Success đã gọi sang Lua — script mới thay giữa phiên được gọi lại đúng các hàm này
        private static readonly List<string> _initCalls = new List<string>();

        private static bool _bootstrapped;
        private static Runner _runner;

        private static LuaScriptHost _host;
        private static LuaAdContext _context;
        private static string _scriptHash;
        private static string _scriptText;
        private static string _scriptMd5;
        private static string _scriptId;

        public static string ScriptId => _scriptId;

        private static readonly Dictionary<string, LuaAdRecord> _inter = new Dictionary<string, LuaAdRecord>();
        private static readonly Dictionary<string, LuaAdRecord> _reward = new Dictionary<string, LuaAdRecord>();

        private static readonly Dictionary<string, ILuaAdLoader> _loaders = new Dictionary<string, ILuaAdLoader>();

        //việc đẩy từ thread khác (SC message, callback GMA) về main thread
        private static readonly object _queueLock = new object();
        private static readonly List<Action> _queue = new List<Action>();
        private static readonly List<Action> _drain = new List<Action>();

        //lượt show đang chạy
        private static LuaAdRecord _showing;
        private static AdType _showingAs;
        private static string _showingWhere;
        private static double _showingEcpm;
        private static double _showingFloor;
        private static string _showingNetwork;
        private static bool _showingPaid;
        private static bool _rewardGranted;
        private static Action _onDisplayed, _onReward, _onHidden, _onFail;
        private static Action<AdType, string, double, LuaPaidDetail> _onPaid;

        //đã nhận ít nhất một bản tin sc_ad_monetize_lua_script (kể cả rỗng / lỗi)
        private static bool _responded;

        //chế độ của CẢ PHIÊN, chốt lúc init MAX: true = Lua, false = MultiCall, null = chưa chốt
        private static bool? _luaMode;

        /// <summary>Đang có script hợp lệ chạy.</summary>
        public static bool IsRunning => _host != null;

        /// <summary>Server đã trả lời bản tin Lua (có script hay rỗng đều tính). Hàng rào init MAX chờ cờ này.</summary>
        public static bool HasResponded => _responded;

        /// <summary>Phiên này chạy bằng Lua (đã chốt lúc init MAX).</summary>
        public static bool IsLuaMode => _luaMode == true;

        /// <summary>Đã chốt chế độ phiên chưa (Lua hay MultiCall). Meta AdMob chờ cờ này rồi mới quyết định có chạy không.</summary>
        public static bool IsModeLocked => _luaMode.HasValue;

        /// <summary>
        /// Chốt chế độ cho cả phiên — FalconMaxService gọi đúng một lần lúc init MAX. Có script hợp lệ -> Lua
        /// (trả true, MultiCall không được bật); không có -> MultiCall, từ đó mọi script về sau bị bỏ qua.
        /// Đổi chế độ giữa phiên là phải dừng cả hệ load đang chạy, nên không hỗ trợ.
        /// </summary>
        public static bool LockMode()
        {
            if (_luaMode.HasValue) return _luaMode.Value;
            if (_host != null)
            {
                _luaMode = true;
                SyncBidFloors("chốt chế độ LUA");
                if (HasLuaAdUnits())
                {
                    Log($"CHỐT chế độ phiên: LUA (script #{_scriptHash}) — MultiCall không chạy" +
                        (_responded ? "" : " | server CHƯA trả lời bản tin Lua -> chạy bằng script ĐÃ LƯU"));
                    return true;
                }

                _luaMode = false;
                Warn($"CHỐT chế độ phiên: KHÔNG dùng LUA dù có script #{_scriptHash} — không có ad unit nào cho Lua" +
                     $" (sc_ad_bid_floor_v2 trả lời={AdBidFloorsV2.HasResponded}, {AdBidFloorsV2.List.Count} id" +
                     $" | sc_ad_bid_floor {MultiCall.Instance?.RawAdBidFloors.Count ?? 0} id)" +
                     " -> dự phòng MultiCall, không có id MultiCall thì dùng id SOMediationSetting");
                _bidFloorHasAdmob = false;
                DropHost();
                return false;
            }

            _luaMode = false;
            Log(_responded
                ? "CHỐT chế độ phiên: MULTICALL (server không gửi script hợp lệ)"
                : "CHỐT chế độ phiên: MULTICALL (chưa nhận sc_ad_monetize_lua_script lúc init MAX)");
            DropHost();
            return false;
        }

        // id MAX = đúng 16 ký tự hex, nằm trong dấu nháy trong script
        private static readonly System.Text.RegularExpressions.Regex _MaxIdInScript =
            new System.Text.RegularExpressions.Regex("[\"']([0-9a-fA-F]{16})[\"']");

        /// <summary>
        /// Ad unit MAX của Lua, để khai báo <c>disable_b2b_ad_unit_ids</c> + mảng init MAX (FalconMaxService gọi
        /// TRƯỚC MaxSdk.InitializeSdk). Gồm id MAX của <c>sc_ad_bid_floor</c> + id script đã xin load + MỌI id MAX ghi
        /// dạng chữ trong script (script tự có id riêng ngoài danh sách bid floor).
        /// </summary>
        private static bool HasLuaAdUnits()
            => AllRecords().Any() ||
               (!string.IsNullOrEmpty(_scriptText) && _MaxIdInScript.IsMatch(_scriptText));

        public static string[] MaxAdUnitIds()
        {
            var ids = AllRecords().Where(r => r.IsMax).Select(r => r.adUnitId).ToList();
            if (!string.IsNullOrEmpty(_scriptText))
                foreach (System.Text.RegularExpressions.Match m in _MaxIdInScript.Matches(_scriptText))
                    ids.Add(m.Groups[1].Value);

            var result = ids.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct()
                .ToArray();
            //FalconMaxService gọi hàm này NGAY TRƯỚC MaxSdk.InitializeSdk -> chốt danh sách cho cả phiên
            _maxIdsAtInit ??= new HashSet<string>(result);
            Log($"id MAX của Lua ({result.Length}, gồm cả id bậc floor chưa xin load): {string.Join(",", result)}");
            return result;
        }

        /// <summary>
        /// Id MAX đã khai lúc init (disable_b2b_ad_unit_ids + mảng MaxSdk.InitializeSdk); null = MAX chưa init.
        /// Sau init, id MAX KHÔNG có ở đây (bid floor về muộn, script thay giữa phiên, id không ghi dạng chữ trong
        /// script) thì KHÔNG được load: thiếu 2 bước đó có thể crash. Id AdMob không bị giới hạn.
        /// </summary>
        private static HashSet<string> _maxIdsAtInit;

        //id MAX bị từ chối vì chưa khai lúc init: chỉ log lần đầu mỗi id
        private static readonly HashSet<string> _notDeclaredLogged = new HashSet<string>();

        private static bool IsMaxDeclaredAtInit(string adUnitId)
            => _maxIdsAtInit == null || (!string.IsNullOrWhiteSpace(adUnitId) && _maxIdsAtInit.Contains(adUnitId.Trim()));

        #region khởi động / nạp script

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_bootstrapped) return;
            _bootstrapped = true;

            ILuaAdLoader max = new LuaMaxLoader();
            ILuaAdLoader admob = new LuaAdmobLoader();
            _loaders[max.Mediation] = max;
            _loaders[admob.Mediation] = admob;

            var go = new GameObject("LuaAdMonetize");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            _runner = go.AddComponent<Runner>();
            Log("bootstrap xong, chờ script từ server (sc_ad_monetize_lua_script)");

            LoadCachedScript();
        }

        #region cache script

        // Script nạp THÀNH CÔNG gần nhất (từ server) + md5 của nó. Mở app nạp ngay bản này, không chờ server:
        // server không trả lời kịp (hàng rào init MAX hết 7s) thì phiên chạy bằng script đã lưu thay vì MultiCall.
        private const string _KEY_CACHE_SCRIPT = "FALCON_LUA_AD_SCRIPT";
        private const string _KEY_CACHE_MD5 = "FALCON_LUA_AD_SCRIPT_MD5";
        private const string _KEY_CACHE_ID = "FALCON_LUA_AD_SCRIPT_ID";

        //md5 của bản đang lưu trên máy, null = không có
        private static string _cachedMd5;

        private static void LoadCachedScript()
        {
            string script, md5, scriptId;
            try
            {
                script = Falcon.Modules.Core.SaveLoad.Runtime.SaveLoadHandler.Load<string>(_KEY_CACHE_SCRIPT, null);
                md5 = Falcon.Modules.Core.SaveLoad.Runtime.SaveLoadHandler.Load<string>(_KEY_CACHE_MD5, null);
                scriptId = Falcon.Modules.Core.SaveLoad.Runtime.SaveLoadHandler.Load<string>(_KEY_CACHE_ID, null);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return;
            }

            if (string.IsNullOrWhiteSpace(script))
            {
                Log("không có script Lua đã lưu");
                return;
            }

            _cachedMd5 = md5;
            Log($"nạp script Lua ĐÃ LƯU id={scriptId} md5={md5} ({script.Length} ký tự), vẫn chờ server gửi bản mới");
            ApplyScriptOnMain(script, md5, scriptId, fromCache: true);
        }

        private static void SaveCache(string luaScript, string md5, string scriptId)
        {
            try
            {
                Falcon.Modules.Core.SaveLoad.Runtime.SaveLoadHandler.Save(_KEY_CACHE_SCRIPT, luaScript);
                Falcon.Modules.Core.SaveLoad.Runtime.SaveLoadHandler.Save(_KEY_CACHE_MD5, md5);
                SaveCachedScriptId(scriptId);
                _cachedMd5 = md5;
                Log($"đã LƯU script id={scriptId} md5={md5} cho các phiên sau");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private static void SaveCachedScriptId(string scriptId)
        {
            if (scriptId == null)
                Falcon.Modules.Core.SaveLoad.Runtime.SaveLoadHandler.DeleteKey(_KEY_CACHE_ID);
            else
                Falcon.Modules.Core.SaveLoad.Runtime.SaveLoadHandler.Save(_KEY_CACHE_ID, scriptId);
        }

        private static void UpdateScriptIdIfChanged(string scriptId, bool updateRunning, bool fromCache)
        {
            if (scriptId == null) return;
            if (updateRunning && scriptId != _scriptId)
            {
                Log($"giữ script đang chạy, đổi id {_scriptId} -> {scriptId}");
                _scriptId = scriptId;
            }

            if (fromCache) return;
            try
            {
                SaveCachedScriptId(scriptId);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private static void ClearCache(string reason)
        {
            try
            {
                if (!Falcon.Modules.Core.SaveLoad.Runtime.SaveLoadHandler.ExistsKey(_KEY_CACHE_SCRIPT)) return;
                Falcon.Modules.Core.SaveLoad.Runtime.SaveLoadHandler.DeleteKey(_KEY_CACHE_SCRIPT);
                Falcon.Modules.Core.SaveLoad.Runtime.SaveLoadHandler.DeleteKey(_KEY_CACHE_MD5);
                Falcon.Modules.Core.SaveLoad.Runtime.SaveLoadHandler.DeleteKey(_KEY_CACHE_ID);
                _cachedMd5 = null;
                Log($"XOÁ script đã lưu ({reason}) -> phiên sau không có Lua tới khi server gửi script");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>md5 (hex thường) của script — dùng khi server không gửi md5.</summary>
        private static string ComputeMd5(string text)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var bytes = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));
                var sb = new System.Text.StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        /// <summary>
        /// Chạy thử script trong môi trường riêng (lệnh load bị từ chối hết) — chỉ để biết có lỗi không trước khi
        /// lưu cho phiên sau (phiên MULTICALL không nạp script).
        /// </summary>
        private static bool ValidateScript(string luaScript, out string error)
        {
            var context = new LuaAdContext(new RejectBridge());
            var host = LuaScriptHost.Create(luaScript, context.Install, out error);
            return host != null;
        }

        #endregion

        /// <summary>Đẩy việc về main thread. Gọi được từ mọi thread.</summary>
        internal static void Post(Action action)
        {
            if (action == null) return;
            lock (_queueLock) _queue.Add(action);
        }

        /// <summary>
        /// Nạp script mới từ server. <paramref name="md5"/> trùng script đang chạy = bỏ qua (không so cả chuỗi);
        /// md5 rỗng thì client tự tính. Rỗng = tắt Lua + xoá script đã lưu. Script lỗi = giữ nguyên script đang
        /// chạy (và bản đã lưu). Nạp thành công = lưu đè cho các phiên sau. Gọi được từ mọi thread.
        /// </summary>
        public static void ApplyScript(string luaScript, string md5 = null, string scriptId = null)
            => Post(() => ApplyScriptOnMain(luaScript, md5, scriptId, fromCache: false));

        /// <summary>
        /// Giá trị coi như RỖNG: null, toàn khoảng trắng, hoặc chỉ gồm dấu nháy (<c>""</c>, <c>''</c>, <c>"\"\""</c>), hoặc
        /// chữ <c>null</c> / <c>nil</c>. Web cấu hình không cho để trống thì nhập <c>""</c> để tắt Lua.
        /// </summary>
        private static bool IsBlankValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return true;
            var s = value.Trim().Trim('"', '\'', '\\', ' ', '\t', '\r', '\n');
            return s.Length == 0 || string.Equals(s, "null", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(s, "nil", StringComparison.OrdinalIgnoreCase);
        }

        /// <param name="fromCache">true = script đã lưu nạp lúc mở app: không tính là server đã trả lời, không lưu lại.</param>
        private static void ApplyScriptOnMain(string luaScript, string md5, string scriptId, bool fromCache)
        {
            if (!fromCache) _responded = true;
            var source = fromCache ? "script ĐÃ LƯU" : "server";
            md5 = IsBlankValue(md5) ? null : md5.Trim();
            scriptId = IsBlankValue(scriptId) ? null : scriptId.Trim();
            var empty = IsBlankValue(luaScript);
            if (empty) luaScript = null;
            if (md5 == null && !empty) md5 = ComputeMd5(luaScript);

            //so md5 trước: server có thể chỉ gửi md5 (script rỗng) khi script không đổi
            if (_host != null && md5 != null && string.Equals(md5, _scriptMd5, StringComparison.OrdinalIgnoreCase))
            {
                Log($"{source}: script md5={md5} trùng script đang chạy -> bỏ qua");
                UpdateScriptIdIfChanged(scriptId, updateRunning: true, fromCache);
                return;
            }

            //chỉ có md5, trùng bản đã lưu = script không đổi (vd phiên MULTICALL, không có script đang chạy)
            if (empty && md5 != null && string.Equals(md5, _cachedMd5, StringComparison.OrdinalIgnoreCase))
            {
                Log($"{source}: chỉ gửi md5={md5}, trùng script đã lưu -> giữ bản đã lưu");
                UpdateScriptIdIfChanged(scriptId, updateRunning: false, fromCache);
                return;
            }

            //server tắt Lua -> phiên sau quay về MultiCall. Phiên này giữ chế độ đã chốt (xử lý ngay dưới)
            if (empty && !fromCache) ClearCache("server gửi script rỗng");

            if (_luaMode == false)
            {
                //phiên đã chốt MULTICALL không nạp script; script hợp lệ được lưu để phiên sau chạy Lua
                if (empty || fromCache) return;
                if (ValidateScript(luaScript, out var validateError))
                {
                    Log($"phiên này đã chốt MULTICALL -> không nạp script md5={md5}, chỉ lưu (áp dụng từ phiên sau)");
                    SaveCache(luaScript, md5, scriptId);
                }
                else
                {
#if ENABLE_DEBUG_LOG
                    Debug.LogError($"{_TAG} script md5={md5} lỗi, KHÔNG lưu (giữ bản đã lưu nếu có): {validateError}");
#endif
                }

                return;
            }

            if (empty)
            {
                if (_host == null)
                {
                    Log("server gửi script rỗng -> không dùng Lua");
                    return;
                }

                if (_luaMode == true)
                {
                    //đã chốt Lua, MultiCall không chạy -> tắt Lua là phiên này hết ad
                    Log("server gửi script rỗng nhưng phiên đã chốt LUA -> giữ script đang chạy");
                    return;
                }

                Log("server gửi script rỗng -> TẮT Lua");
                DropHost();
                return;
            }

            var hash = md5;
            var replacing = _host != null;
            var oldHash = _scriptHash;

            //thân script mới chạy ngay trong Create và tự xin load các id default -> lịch của nó có seq > mốc này
            var seqBefore = _requestSeq;
            var context = new LuaAdContext(_bridge);
            var host = LuaScriptHost.Create(luaScript, script =>
            {
                context.Install(script);
                context.Sync(_inter.Values, _reward.Values, IsOffline);
            }, out var error);
            if (host == null)
            {
                //thân script lỗi giữa chừng: huỷ các lịch nó đã kịp xin
                CancelWaiting(r => r.requestSeq > seqBefore);
#if ENABLE_DEBUG_LOG
                Debug.LogError($"{_TAG} {source}: script #{hash} lỗi, KHÔNG nạp" +
                               (_host != null ? " (giữ script đang chạy)" : "") +
                               (fromCache ? "" : " (KHÔNG lưu, giữ bản đã lưu nếu có)") + $": {error}");
#endif
                //bản đã lưu hỏng (vd client mới đổi hợp đồng) -> bỏ, khỏi nạp lại mỗi lần mở app
                if (fromCache) ClearCache("script đã lưu bị lỗi");
                return;
            }

            //lịch còn chờ của script cũ không còn ai chịu trách nhiệm
            CancelWaiting(r => r.requestSeq <= seqBefore);
            _host = host;
            _context = context;
            _scriptHash = hash;
            _scriptText = luaScript;
            _scriptMd5 = md5;
            _scriptId = scriptId;

            if (!fromCache) SaveCache(luaScript, md5, scriptId);

            var has = _AllFunctions.Where(host.Has).ToArray();
            Log($"{source}: " + (replacing ? $"THAY script #{oldHash} -> " : "nạp script ") +
                $"#{hash} id={scriptId} algorithm={AdViewRequestGate.LuaAlgorithm(scriptId)} ({luaScript.Length} ký tự, kiểu {(host.IsObjectMode ? "object A" : "hàm toàn cục")}) " +
                $"— có hàm: {(has.Length == 0 ? "(không có hàm nào!)" : string.Join(", ", has))} " +
                $"— thân script đã xin {_requestSeq - seqBefore} lệnh load");
            if (!host.Has(_FN_INIT_MAX) && !host.Has(_FN_INIT_ADMOB))
                Warn($"script không có {_FN_INIT_MAX} / {_FN_INIT_ADMOB} -> chỉ load được những gì thân script tự xin");

            SyncBidFloors("nạp script");

            if (!replacing) return;

            //script mới bắt đầu trắng: báo lại các mốc init đã qua để nó tự khởi động, rồi báo ad đang giữ
            foreach (var fn in _initCalls.ToArray())
            {
                Log($"script mới: gọi lại {fn}() (mediation đã init từ trước)");
                CallLua(fn);
            }

            ReplayReadyAds();
        }

        /// <summary>
        /// Script mới bắt đầu với trạng thái trắng, trong khi C# có thể đang giữ sẵn ad (Ready) do script cũ
        /// load — lệnh load của script mới cho các unit đó bị từ chối nên nó không bao giờ biết. Báo lại từng
        /// unit đang giữ ad qua chính <c>On...LoadSuccess(adUnitId)</c> để script dựng lại trạng thái (base,
        /// bậc đã có ad...). Unit đang Loading thì kết quả sẽ tự về script mới.
        /// </summary>
        private static void ReplayReadyAds()
        {
            var ready = AllRecords().Where(r => r.status == LuaAdStatus.Ready && !r.isShowing).ToArray();
            if (ready.Length == 0) return;
            Log($"báo lại {ready.Length} unit đang giữ ad cho script mới: " +
                string.Join(", ", ready.Select(r => $"{r.adUnitType[0]}:{Name(r)}={Money(r.eCpm)}")));
            foreach (var r in ready)
            {
                //script mới có thể vừa đổi trạng thái unit khác trong lúc xử lý -> kiểm lại từng unit
                if (_host == null || r.status != LuaAdStatus.Ready || r.isShowing) continue;
                CallLua(Fn(r, "LoadSuccess"), LuaArgs(r));
            }
        }

        private static void DropHost()
        {
            CancelWaiting(_ => true);
            _host = null;
            _context = null;
            _scriptHash = null;
            _scriptText = null;
            _scriptMd5 = null;
            _scriptId = null;
        }

        //request đang chờ delay của script cũ không còn ai chịu trách nhiệm -> huỷ
        private static void CancelWaiting(Func<LuaAdRecord, bool> match)
        {
            foreach (var r in AllRecords())
                if (r.status == LuaAdStatus.Waiting && match(r))
                    r.status = LuaAdStatus.Idle;
        }

        #endregion

        #region danh sách ad unit từ sc_ad_bid_floor

        /// <summary>SCAdBidFloor gọi sau khi nhận bản tin (gọi được từ mọi thread).</summary>
        public static void OnBidFloorsChanged() => Post(() =>
        {
            if (SyncBidFloors("nhận bid floor") > 0) ReplayInitCalls("bid floor có id mới");
        });

        private static void ReplayInitCalls(string reason)
        {
            if (_host == null) return;
            foreach (var fn in _initCalls.ToArray())
            {
                if (!_host.Has(fn)) continue;
                Log($"{reason}: gọi lại {fn}() để script khởi động các id mới");
                CallLua(fn);
            }
        }

        /// <summary>
        /// Đưa danh sách <c>sc_ad_bid_floor_v2</c> (hoặc cache) vào record: id mới được tạo sẵn ở trạng thái
        /// Idle (Lua thấy ngay trong <c>context.list...</c>, kèm adUnitIdType / multiplier / timeConfig), id đã có thì
        /// cập nhật thông tin. KHÔNG xoá id đã biết (có thể đang load / giữ ad). Có id AdMob -> sẽ init AdMob.
        /// </summary>
        private static int SyncBidFloors(string reason)
        {
            if (_host == null || _luaMode == false) return 0;

            List<AdBidFloorV2> list;
            string source;
            try
            {
                list = LuaBidFloorSource(out source);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return 0;
            }

            int added = 0, updated = 0, skipped = 0;
            foreach (var b in list)
            {
                if (b == null || string.IsNullOrWhiteSpace(b.adUnitId))
                {
                    skipped++;
                    continue;
                }

                var id = b.adUnitId.Trim();
                var format = b.rewarded ? AdType.Reward : AdType.Interstitial;
                var mediation = BidFloorMediation(b);
                var records = Records(format);
                if (records.TryGetValue(id, out var record))
                {
                    if (record.mediation != mediation)
                    {
                        Warn($"bid floor {id}: id này đang thuộc {record.mediation}, server ghi {mediation} -> bỏ qua");
                        skipped++;
                        continue;
                    }

                    updated++;
                }
                else
                {
                    if (UsesAdUnit(Records(format == AdType.Reward ? AdType.Interstitial : AdType.Reward), id))
                    {
                        Warn($"bid floor {id}: id này đang được dùng cho format khác -> bỏ qua");
                        skipped++;
                        continue;
                    }

                    if (mediation == LuaAdRecord.MEDIATION_MAX && IsUsedByOtherModule(id))
                    {
                        Warn($"bid floor {id}: unit đang được MultiCall / FloorPersonalization / DFPool dùng -> bỏ qua");
                        skipped++;
                        continue;
                    }

                    record = new LuaAdRecord { format = format, mediation = mediation, adUnitId = id };
                    records[id] = record;
                    added++;
                }

                record.fromBidFloor = true;
                record.adUnitIdType = string.IsNullOrWhiteSpace(b.adUnitIdType) ? null : b.adUnitIdType.Trim();
                record.multiplier = b.multiplier;
                record.timeConfig = string.IsNullOrWhiteSpace(b.timeConfig) ? null : b.timeConfig;
                //bản load AdMob gắn id MAX dùng chung thông tin của id gốc
                foreach (var linked in records.Values)
                    if (linked.linkedAdUnitId != null && linked.adUnitId == id)
                        linked.CopyBidFloorInfo(record);
            }

            var hadAdmob = _bidFloorHasAdmob;
            _bidFloorHasAdmob = AllRecords().Any(r => r.fromBidFloor && r.mediation == LuaAdRecord.MEDIATION_ADMOB);
            Log($"bid floor {source} ({reason}): {list.Count} id -> thêm {added}, cập nhật {updated}, bỏ {skipped}" +
                $" | {DescribeBidFloors()} | " +
                (_bidFloorHasAdmob ? "CÓ id AdMob -> init AdMob sau MAX" : "KHÔNG có id AdMob -> không init AdMob"));

            //danh sách có AdMob về muộn (MAX đã init, đã báo OnInitMaxSuccess) -> init AdMob luôn
            if (_bidFloorHasAdmob && !hadAdmob && _maxInitDone) InitAdmob("bid floor mới có id AdMob, MAX đã init xong");
            return added;
        }

        /// <summary>
        /// Danh sách ad unit cho Lua: <c>sc_ad_bid_floor_v2</c> (server / cache). v2 không có id nào -> lấy id từ
        /// <c>sc_ad_bid_floor</c> (v1, bản thô chưa lọc provider index, server / cache).
        /// </summary>
        private static List<AdBidFloorV2> LuaBidFloorSource(out string source)
        {
            var v2 = AdBidFloorsV2.List;
            if (v2.Any(b => b != null && !string.IsNullOrWhiteSpace(b.adUnitId)))
            {
                source = "v2";
                return v2;
            }

            var v1 = MultiCall.Instance?.RawAdBidFloors;
            if (v1 == null || v1.Count == 0)
            {
                source = "v2 (rỗng, v1 cũng rỗng)";
                return v2;
            }

            source = $"v1 (v2 không có id nào, lấy đủ {v1.Count} id, không lọc algorithmIds)";
            return v1.Where(b => b != null).Select(b => new AdBidFloorV2
            {
                adUnitId = b.adUnitId,
                rewarded = b.rewarded,
                multiplier = b.multiplier,
                timeConfig = b.timeConfig,
                mediation = b.mediation,
                adUnitIdType = b.adUnitIdType
            }).ToList();
        }

        //cách nhận max / admob dùng chung với MultiCall: AdBidFloor.MediationKind
        private static string BidFloorMediation(AdBidFloorV2 b)
            => b.MediationKind() == AdBidFloor.MEDIATION_ADMOB
                ? LuaAdRecord.MEDIATION_ADMOB
                : LuaAdRecord.MEDIATION_MAX;

        private static string DescribeBidFloors()
            => string.Join(", ", AllRecords().Where(r => r.fromBidFloor && r.linkedAdUnitId == null)
                .Select(r => $"{r.adUnitType[0]}:{r.mediation}:{r.adUnitIdType ?? "?"}:x{r.multiplier.ToString("0.##", CultureInfo.InvariantCulture)}:{r.adUnitId}"));

        #endregion

        #region game hỏi Lua: IsShowInterstitial / IsShowRewarded

        /// <summary>
        /// Hỏi script có nên show interstitial không. <c>context.params</c> = { where, level } + mọi cặp trong
        /// <paramref name="param"/> (string / bool / số; kiểu khác đổi thành chuỗi), và cũng được truyền làm
        /// tham số của hàm Lua. Chưa có script / script không có hàm / hàm lỗi -> <paramref name="defaultValue"/>.
        /// Chỉ gọi từ main thread.
        /// </summary>
        public static bool IsShowInterstitial(string where, IDictionary<string, object> param, bool defaultValue)
            => AskShow(_FN_IS_SHOW_INTER, where, param, defaultValue);

        /// <summary>Như <see cref="IsShowInterstitial"/>, cho rewarded (hàm Lua <c>IsShowRewarded</c>).</summary>
        public static bool IsShowRewarded(string where, IDictionary<string, object> param, bool defaultValue)
            => AskShow(_FN_IS_SHOW_REWARD, where, param, defaultValue);

        private static bool AskShow(string function, string where, IDictionary<string, object> param,
            bool defaultValue)
        {
            if (_host == null || !_host.Has(function)) return defaultValue;

            var values = new Dictionary<string, object> { ["where"] = where };
            try
            {
                values["level"] = LevelData.Instance.level;
            }
            catch
            {
                //chưa có dữ liệu level -> bỏ qua
            }

            var mediation = MediationManager.Instance;
            values["sinceLastInterSec"] = Math.Floor(mediation.SecondsSinceLastInterstitial);
            values["interShownCount"] = mediation.NumberShowInterstitial;

            if (param != null)
                foreach (var pair in param)
                    values[pair.Key] = pair.Value;

            try
            {
                _context.SetParams(values);
                var result = CallLua(function, _context.Params);
                var answer = result == null ? defaultValue : result.CastToBool();
                Log($"{function}({where}) -> {answer}{(result == null ? " (mặc định)" : "")}");
                return answer;
            }
            finally
            {
                _context?.SetParams(null);
            }
        }

        #endregion

        #region Lua -> Unity: load

        private static Dictionary<string, LuaAdRecord> Records(AdType format)
            => format == AdType.Reward ? _reward : _inter;

        //lệnh load AdMob bị chặn vì DFPool đã nhận format: chỉ log lần đầu mỗi id (script có thể gọi lại liên tục)
        private static readonly HashSet<string> _takeoverSkipLogged = new HashSet<string>();

        private static IEnumerable<LuaAdRecord> AllRecords() => _inter.Values.Concat(_reward.Values);

        /// <summary><c>context.&lt;mediation&gt;.LoadInterstitial/LoadRewarded(adUnitId, floor, delayTime [, linkedAdUnitId])</c></summary>
        private static bool RequestLoad(string mediation, AdType format, string adUnitId, double floor,
            double delaySeconds, string linkedAdUnitId)
        {
            var fn = $"{mediation}.Load{(format == AdType.Reward ? "Rewarded" : "Interstitial")}";

            //DFPool (custom adapter) đang lo AdMob của format này -> Lua KHÔNG dùng AdMob nữa (tránh request gấp
            //đôi). Ad MAX của Lua load xong vẫn kích bước C của DFPool (unit MAX của Lua là mốc giá).
            if (mediation == LuaAdRecord.MEDIATION_ADMOB && AdmobTakeover.IsDecided &&
                AdmobTakeover.IsClaimed(format == AdType.Reward))
            {
                if (_takeoverSkipLogged.Add(fn + "|" + adUnitId))
                    Log($"{fn}({adUnitId}) bị bỏ qua: DFPool đang load AdMob format này (log 1 lần / id)");
                return false;
            }

            //id MAX chưa khai lúc init MAX (không có trong disable_b2b_ad_unit_ids + mảng init) -> không load
            if (mediation == LuaAdRecord.MEDIATION_MAX && !IsMaxDeclaredAtInit(adUnitId))
            {
                if (_notDeclaredLogged.Add(fn + "|" + adUnitId))
                    Warn($"{fn}({adUnitId}) bị bỏ qua: id MAX này KHÔNG được khai lúc init MAX (bid floor về muộn / " +
                         "script thay giữa phiên / id không ghi dạng chữ trong script) -> có thể crash nếu load (log 1 lần / id)");
                return false;
            }

            var record = GetOrCreate(fn, mediation, format, adUnitId, linkedAdUnitId);
            if (record == null) return false;

            //Loading / Ready: không xin thêm được. Waiting: lịch mới THAY lịch cũ (Lua dùng để "huỷ" lịch
            //retry cũ khi có base mới — xin lại với floor mới, delay mới)
            if (record.status == LuaAdStatus.Loading || record.status == LuaAdStatus.Ready)
            {
                Log($"{fn}({Name(record)}) bị bỏ qua: unit đang {record.status}");
                return false;
            }

            var replaced = record.status == LuaAdStatus.Waiting;
            record.pendingFloor = Math.Max(0d, floor);
            record.waitFromUtc = DateTime.UtcNow;
            record.delaySeconds = Math.Max(0d, delaySeconds);
            record.requestSeq = ++_requestSeq;
            record.status = LuaAdStatus.Waiting;
            Log($"{fn}({Name(record)}) floor={Money(record.pendingFloor)} delay={record.delaySeconds:0.##}s" +
                (replaced ? " (thay lịch đang chờ)" : "") +
                (_mediationReadyUtc.ContainsKey(mediation)
                    ? ""
                    : $" | CHƯA gửi: chờ {mediation} init xong mới đếm delay rồi gửi"));
            return true;
        }

        /// <summary>
        /// Record của bản load (adUnitId + linkedAdUnitId), tạo mới nếu chưa có. null = không hợp lệ cho Lua (đã
        /// log lý do).
        /// </summary>
        private static LuaAdRecord GetOrCreate(string fn, string mediation, AdType format, string adUnitId,
            string linkedAdUnitId)
        {
            if (string.IsNullOrWhiteSpace(adUnitId))
            {
                Warn($"{fn}: thiếu adUnitId -> bỏ qua");
                return null;
            }

            adUnitId = adUnitId.Trim();
            linkedAdUnitId = string.IsNullOrWhiteSpace(linkedAdUnitId) ? null : linkedAdUnitId.Trim();
            if (!_loaders.ContainsKey(mediation)) return null;

            var key = LuaAdRecord.MakeKey(adUnitId, linkedAdUnitId);
            var records = Records(format);
            if (records.TryGetValue(key, out var record))
            {
                if (record.mediation != mediation)
                {
                    Warn($"{fn}({key}): unit này đang thuộc {record.mediation} -> bỏ qua");
                    return null;
                }
            }
            else
            {
                if (UsesAdUnit(Records(format == AdType.Reward ? AdType.Interstitial : AdType.Reward), adUnitId))
                {
                    Warn($"{fn}({adUnitId}): id này đang được dùng cho format khác -> bỏ qua");
                    return null;
                }

                if (UsesAdUnit(records, adUnitId, other => other.mediation != mediation))
                {
                    Warn($"{fn}({adUnitId}): id này đang thuộc mediation khác -> bỏ qua");
                    return null;
                }

                if (mediation == LuaAdRecord.MEDIATION_MAX && IsUsedByOtherModule(adUnitId))
                {
                    Warn($"{fn}({adUnitId}): unit này đang được MultiCall / FloorPersonalization / DFPool dùng — " +
                         "Lua phải dùng bộ ad unit RIÊNG -> bỏ qua");
                    return null;
                }

                record = new LuaAdRecord
                {
                    format = format, mediation = mediation, adUnitId = adUnitId, linkedAdUnitId = linkedAdUnitId
                };
                //bản load gắn id MAX: lấy adUnitIdType / multiplier / timeConfig của id gốc (từ bid floor)
                if (linkedAdUnitId != null && records.TryGetValue(adUnitId, out var source))
                    record.CopyBidFloorInfo(source);
                records[key] = record;
            }

            return record;
        }

        //có record nào (mọi bản load) dùng ad unit này không
        private static bool UsesAdUnit(Dictionary<string, LuaAdRecord> records, string adUnitId,
            Func<LuaAdRecord, bool> match = null)
            => records.Values.Any(r => r.adUnitId == adUnitId && (match == null || match(r)));

        private static bool IsUsedByOtherModule(string adUnitId)
        {
            try
            {
                if (ExternalMaxAdUnits.IsExternal(adUnitId)) return true;
                if (InterstitialPersonalization.Current.IsOwnedUnit(adUnitId)) return true;
                var multiCall = MultiCall.Instance;
                return multiCall != null &&
                       (multiCall.dictionaryInterstitial.ContainsKey(adUnitId) ||
                        multiCall.dictionaryRewarded.ContainsKey(adUnitId));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }

        /// <summary>
        /// Mediation đã init xong chưa — request của mediation nào CHỈ được bắn khi chính mediation đó init
        /// xong. Lần đầu sẵn sàng: lệnh xin trước đó tính delay từ lúc này.
        /// </summary>
        private static bool MediationReady(string mediation, DateTime now)
        {
            if (_mediationReadyUtc.ContainsKey(mediation)) return true;

            bool ready;
            if (mediation == LuaAdRecord.MEDIATION_MAX)
                ready = _maxInitDone; //callback init (sau CallAfterInit), không dùng MaxSdk.IsInitialized()
            else if (mediation == LuaAdRecord.MEDIATION_ADMOB)
                ready = AdmobReady();
            else
                ready = false;
            if (!ready) return false;

            //lịch xin trước lúc init (thân script chạy trước init) tính delay từ lúc này, giống thuật toán 8
            //lên lịch id default lúc mediation init xong
            _mediationReadyUtc[mediation] = now;
            foreach (var r in AllRecords())
                if (r.mediation == mediation && r.status == LuaAdStatus.Waiting && r.waitFromUtc < now)
                    r.waitFromUtc = now;
            Log($"{mediation} đã init -> bắt đầu đếm delay các lệnh load {mediation} đang chờ");
            return true;
        }

        /// <summary>
        /// AdMob init xong chưa. Lua chỉ tự init MobileAds khi danh sách <c>sc_ad_bid_floor</c> có id AdMob (xem
        /// <see cref="InitAdmob"/>); không có thì lệnh load AdMob chờ mãi, trừ khi module khác đã init MobileAds.
        /// </summary>
        private static bool AdmobReady() => _admobInitDone || Falcon.Admob.AdmobMediation.SdkInitialized;

        /// <summary>
        /// FalconMaxService gọi trong callback MAX init thành công (phiên Lua). Gọi <c>OnInitMaxSuccess()</c> ngay
        /// (MAX load luôn, không chờ AdMob), rồi nếu danh sách bid floor có id AdMob thì init AdMob (tuần tự sau
        /// MAX, KHÔNG song song), xong thì gọi <c>OnInitAdmobSuccess()</c>.
        /// </summary>
        public static void OnMaxSdkInitialized()
        {
            if (_maxInitDone) return;
            _maxInitDone = true;
            CallAfterInit();
            Log("callback MAX init thành công -> báo Lua OnInitMaxSuccess" +
                (_bidFloorHasAdmob ? ", sau đó init AdMob" : " (bid floor không có id AdMob)"));
            NotifyInit(_FN_INIT_MAX);
            if (_bidFloorHasAdmob) InitAdmob("MAX init xong, bid floor có id AdMob");
        }

        /// <summary>
        /// Giống <c>MultiCall.CallAfterInit</c>: tắt auto retry của MAX cho mọi unit MAX đã biết format (bid floor
        /// + id script đã xin load). Id chỉ ghi trong script, chưa biết format -> set ở lần load đầu.
        /// </summary>
        private static void CallAfterInit()
        {
            foreach (var r in AllRecords().Where(r => r.IsMax))
                LuaMaxLoader.DisableAutoRetry(r.format, r.adUnitId);
        }

        /// <summary>
        /// Init AdMob cho Lua (một lần), CHỈ sau callback MAX init thành công (đã qua consent). Xong thì gọi
        /// <c>OnInitAdmobSuccess()</c>. Lệnh load AdMob chờ tới khi init xong (xem <see cref="MediationReady"/>).
        /// </summary>
        private static void InitAdmob(string reason)
        {
            if (_admobInitRequested || _admobInitDone) return;
            _admobInitRequested = true;

            if (Falcon.Admob.AdmobMediation.SdkInitialized)
            {
                _admobInitDone = true;
                Log($"INIT ADMOB ({reason}): MobileAds đã được init từ trước -> dùng luôn");
                NotifyInit(_FN_INIT_ADMOB);
                return;
            }

            Log($"INIT ADMOB ({reason}) -> MobileAds.Initialize ...");
            GoogleMobileAds.Api.MobileAds.Initialize(_ => Post(() =>
            {
                _admobInitDone = true;
                Log("MobileAds.Initialize xong");
                NotifyInit(_FN_INIT_ADMOB);
            }));
        }

        /// <summary>Gọi <c>OnInitMaxSuccess()</c> / <c>OnInitAdmobSuccess()</c> sang Lua (mỗi hàm một lần mỗi script).</summary>
        private static void NotifyInit(string function)
        {
            if (_initCalls.Contains(function)) return;
            _initCalls.Add(function);
            if (_host == null) return;
            Log($"gọi {function}() sang Lua" + (_host.Has(function) ? "" : " — script KHÔNG có hàm này, bỏ qua"));
            CallLua(function);
        }

        /// <summary>Chạy các request đã hết delay. Gọi mỗi tick ở main thread.</summary>
        private static void PumpLoads()
        {
            //_luaMode chỉ được chốt lúc bắt đầu init MAX = đã qua consent. Từ đây mỗi mediation tự chờ init của
            //chính nó (MediationReady). AdMob chỉ init sau callback MAX init thành công nên cũng sau MAX
            if (_host == null || _luaMode != true) return;
            var now = DateTime.UtcNow;

            foreach (var r in AllRecords())
            {
                if (r.status != LuaAdStatus.Waiting || r.isShowing) continue;
                if (!MediationReady(r.mediation, now) || now < r.DueUtc) continue;
                if (!_loaders.TryGetValue(r.mediation, out var loader)) continue;

                //DFPool (custom adapter) load AdMob của format này -> id mediation=admob KHÔNG gửi request
                //(tránh request AdMob gấp đôi). Chờ DFPool quyết định xong rồi mới gửi id AdMob.
                if (r.mediation == LuaAdRecord.MEDIATION_ADMOB)
                {
                    if (!AdmobTakeover.IsDecided) continue;
                    //lịch đặt TRƯỚC lúc DFPool quyết định (lệnh mới đã bị chặn ở RequestLoad) -> huỷ
                    if (AdmobTakeover.IsClaimed(r.format == AdType.Reward))
                    {
                        r.status = LuaAdStatus.Idle;
                        if (_takeoverSkipLogged.Add("pump|" + r.Key))
                            Log($"BỎ LOAD {r.mediation} {r.adUnitType} {Name(r)}: DFPool đang load AdMob format này");
                        continue;
                    }
                }

                r.status = LuaAdStatus.Loading;
                r.floor = r.pendingFloor;
                r.eCpm = 0d;
                r.loadingTime = 0d;
                r.networkName = null;
                r.lastError = null;
                r.startLoadUtc = now;
                r.adAlgorithm = AdViewRequestGate.LuaAlgorithm(_scriptId);
                Log($"LOAD {r.mediation} {r.adUnitType} {Name(r)} floor={Money(r.floor)}");
                try
                {
                    AdViewRequestGate.Open(r.format, r.mediation, r.adAlgorithm);
                    CountRequest(r);
                    loader.Load(r);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    FailLoad(r, "exception " + e.Message);
                }
            }
        }

        //ad Ready mà mạng báo hết hạn (AdMob ~1h) -> coi như load hỏng để script xin lại
        private static void CheckExpired()
        {
            if (_host == null) return;
            List<LuaAdRecord> expired = null;
            foreach (var r in AllRecords())
            {
                if (r.status != LuaAdStatus.Ready || r.isShowing) continue;
                if (_loaders.TryGetValue(r.mediation, out var loader) && !loader.IsReady(r))
                    (expired ??= new List<LuaAdRecord>()).Add(r);
            }

            if (expired == null) return;
            foreach (var r in expired)
            {
                Log($"{r.adUnitType} {Name(r)} hết hạn / mạng đã thu hồi ad -> báo load fail");
                FailLoad(r, "expired", measureLoading: false);
            }
        }

        #endregion

        #region callback từ loader (main thread)

        // Loader báo kết quả theo KEY của bản load (LuaAdRecord.Key): MAX = adUnitId, AdMob = adUnitId hoặc
        // adUnitId@linkedAdUnitId.
        private static LuaAdRecord Find(AdType format, string key)
            => key != null && Records(format).TryGetValue(key, out var r) ? r : null;

        private static bool IsShowingRecord(AdType format, string key)
            => _showing != null && _showing.format == format && _showing.Key == key;

        private static string Fn(LuaAdRecord r, string suffix)
            => (r.format == AdType.Reward ? "OnRewarded" : "OnInterstitial") + suffix;

        // tham số của On...(adUnitId, linkedAdUnitId)
        private static DynValue[] LuaArgs(LuaAdRecord r) => new[]
        {
            DynValue.NewString(r.adUnitId),
            r.linkedAdUnitId == null ? DynValue.Nil : DynValue.NewString(r.linkedAdUnitId)
        };

        internal static void OnAdLoaded(AdType format, string key, double eCpm, string network, long latencyMs)
        {
            var r = Find(format, key);
            if (r == null || r.status != LuaAdStatus.Loading) return;
            r.status = LuaAdStatus.Ready;
            r.eCpm = Math.Max(0d, eCpm);
            r.networkName = network;
            r.loadingTime = Elapsed(r);
            Log($"LOAD OK {r.mediation} {r.adUnitType} {Name(r)} eCpm={Money(r.eCpm)} ({network}) sau {r.loadingTime:0.##}s" +
                $" | numRetry {r.numRetry} -> 0");
            //load được ad -> đếm lỗi lại từ đầu cho lần load tiếp theo
            r.numRetry = 0;
            ReportLoad(r, true, null, latencyMs);
            if (r.IsMax)
                FalconFirebaseLog.Log(r.format == AdType.Reward
                    ? FalconFirebaseLog.ADS_REWARD_LOAD
                    : FalconFirebaseLog.AD_INTER_LOAD_SUCCESS);
            CountSuccess(r);
            CallLua(Fn(r, "LoadSuccess"), LuaArgs(r));
        }

        internal static void OnAdLoadFailed(AdType format, string key, string error, long latencyMs)
        {
            var r = Find(format, key);
            if (r == null || r.status != LuaAdStatus.Loading) return;
            var errorCode = ErrorCode(r, error);
            ReportLoad(r, false, errorCode, latencyMs);
            if (r.IsMax && r.format == AdType.Interstitial)
                FalconFirebaseLog.Log(FalconFirebaseLog.AD_INTER_LOAD_FAILED, error);
            CountError(r, errorCode);
            FailLoad(r, error);
            ReportGiveUpIfExhausted(r, errorCode);
        }

        private const string _KEY_DECISION_POLICY = "decision_policy_load_ad";

        private static string ErrorCode(LuaAdRecord r, string error)
        {
            if (string.IsNullOrWhiteSpace(error)) return error;
            var trimmed = error.Trim();
            var space = trimmed.IndexOf(' ');
            return space > 0 ? trimmed.Substring(0, space) : trimmed;
        }

        private static void ReportGiveUpIfExhausted(LuaAdRecord r, string errorCode)
        {
            var ladder = RetryLadderLength(r.timeConfig);
            if (ladder == 0 || r.numRetry < ladder || Records(r.format).Values.Any(HasAd)) return;
            AdViewRequestGate.ReportLoadGaveUp(r.format, errorCode);
        }

        private static int RetryLadderLength(string timeConfig)
        {
            if (string.IsNullOrWhiteSpace(timeConfig)) return 0;
            var count = 0;
            foreach (var step in timeConfig.Split(','))
                if (int.TryParse(step, out _)) count++;
            return count;
        }

        private static AdRequestLogItem RequestLogItem(LuaAdRecord r)
        {
            var log = LogInstance.Instance;
            var item = log.GetAdRequestLogItemByAdUnitId(r.adUnitId);
            if (item != null) return item;
            item = new AdRequestLogItem
            {
                adUnitId = r.adUnitId,
                adType = r.format.ToString().ToLower(),
                decisionPolicy = SaveLoadHandler.Load(_KEY_DECISION_POLICY, "client-" + AdViewRequestGate.ALGORITHM_LUA),
                numRequest = 0,
                errorDetail = new Dictionary<string, int>(),
                adSuccessInfos = new List<AdSuccessInfo>()
            };
            log.AddAdLog(item);
            return item;
        }

        private static void CountRequest(LuaAdRecord r)
        {
            try
            {
                RequestLogItem(r).numRequest++;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private static void CountError(LuaAdRecord r, string errorCode)
        {
            try
            {
                var item = RequestLogItem(r);
                var code = string.IsNullOrEmpty(errorCode) ? "unknown" : errorCode;
                item.errorDetail.TryGetValue(code, out var count);
                item.errorDetail[code] = count + 1;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private static void CountSuccess(LuaAdRecord r)
        {
            try
            {
                var item = RequestLogItem(r);
                item.adSuccessInfos.Add(new AdSuccessInfo
                {
                    index = item.numRequest,
                    revenueUsd = r.Revenue,
                    time = new DateTimeOffset(Falcon.Modules.Core.Utils.Time.Runtime.TimeUtils.UTCNow)
                        .ToUnixTimeMilliseconds(),
                    adMediation = r.mediation,
                    adNetwork = r.networkName
                });
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// Unit về Idle, numRetry +1, rồi gọi On...LoadFail. <paramref name="measureLoading"/> = false khi không
        /// phải lỗi của một request (ad hết hạn / chiếu hỏng) -> giữ loadingTime của lần load trước.
        /// </summary>
        private static void FailLoad(LuaAdRecord r, string error, bool measureLoading = true)
        {
            r.status = LuaAdStatus.Idle;
            r.eCpm = 0d;
            if (measureLoading) r.loadingTime = Elapsed(r);
            r.numRetry++;
            r.lastError = error;
            Log($"LOAD FAIL {r.mediation} {r.adUnitType} {Name(r)} ({error}) numRetry={r.numRetry}");
            CallLua(Fn(r, "LoadFail"), LuaArgs(r));
        }

        internal static void OnAdDisplayed(AdType format, string key)
        {
            if (!IsShowingRecord(format, key)) return;
            var r = _showing;
            FsnAdManager.NotifyFullScreenAdDisplayed();
            Log($"DISPLAYED {r.mediation} {r.adUnitType} {Name(r)}");
            Invoke(_onDisplayed);
            r.ResetAfterShow();
            CallLua(Fn(r, "ShowSuccess"), LuaArgs(r));
        }

        internal static void OnAdRewarded(AdType format, string key)
        {
            if (!IsShowingRecord(format, key) || _rewardGranted) return;
            _rewardGranted = true;
            Log($"REWARD {Name(_showing)}");
            if (format == AdType.Reward) FalconFirebaseLog.Log(FalconFirebaseLog.ADS_REWARD_COMPLETE, _showingWhere);
            Invoke(_onReward);
        }

        internal static void OnAdClicked(AdType format, string key)
        {
            if (!IsShowingRecord(format, key)) return;
            if (format == AdType.Reward) FalconFirebaseLog.Log(FalconFirebaseLog.ADS_REWARD_CLICK, _showingWhere);
            else FalconFirebaseLog.Log(FalconFirebaseLog.AD_INTER_CLICKED);
            AdShowReporter.Clicked(format);
        }

        internal static void OnAdShowFailed(AdType format, string key, string errorCode)
        {
            if (!IsShowingRecord(format, key)) return;
            var r = _showing;
            Log($"SHOW FAIL {r.mediation} {r.adUnitType} {Name(r)} ({errorCode})");
            if (r.format == AdType.Reward) FalconFirebaseLog.Log(FalconFirebaseLog.ADS_REWARD_SHOW_FAIL, errorCode);
            AdShowReporter.ShowFailed(r.format, errorCode, r.adUnitId, _showingNetwork,
                _showingFloor > 0d ? _showingFloor : (double?)null, _showingEcpm > 0d ? _showingEcpm : (double?)null);
            var onFail = _onFail;
            EndShow();

            //MAX từ chối chiếu mà vẫn giữ ad -> giữ cho lượt sau (eCpm chưa bị reset vì chưa displayed);
            //mất ad -> báo script xin lại
            if (_loaders.TryGetValue(r.mediation, out var loader) && loader.IsReady(r))
                r.status = LuaAdStatus.Ready;
            else
                FailLoad(r, "show_failed " + errorCode, measureLoading: false);

            Invoke(onFail);
        }

        internal static void OnAdHidden(AdType format, string key)
        {
            if (!IsShowingRecord(format, key)) return;
            var r = _showing;

            //interstitial chiếu thay rewarded: xem xong (đóng ad) là được thưởng
            if (_showingAs == AdType.Reward && r.format == AdType.Interstitial && !_rewardGranted)
            {
                _rewardGranted = true;
                Invoke(_onReward);
            }

            Log($"HIDDEN {r.mediation} {r.adUnitType} {Name(r)} reward={_rewardGranted}");
            AdShowReporter.Closed(r.format, r.format == AdType.Reward ? _rewardGranted : (bool?)null, _showingWhere);
            var onHidden = _onHidden;
            EndShow();
            //lệnh load Lua xin cho unit này lúc ad đang chiếu (ở OnShowSuccess) tính delay từ lúc ad đóng
            if (r.status == LuaAdStatus.Waiting) r.waitFromUtc = DateTime.UtcNow;
            Invoke(onHidden);
        }

        internal static void OnAdPaid(AdType format, string key, double revenue, string precision, string network,
            LuaPaidDetail detail = default)
        {
            if (!IsShowingRecord(format, key) || _showingPaid) return;
            _showingPaid = true;
            var r = _showing;
            var onPaid = _onPaid;
            _onPaid = null;
            Log($"PAID {r.mediation} {r.adUnitType} {Name(r)} ${revenue.ToString("0.########", CultureInfo.InvariantCulture)}");
            AdShowReporter.Revenue(new AdShowReporter.Impression
            {
                bigDataOnly = detail.maxInfo != null && onPaid != null,
                type = r.format,
                mediation = ReporterMediation(r),
                adUnitId = r.adUnitId,
                network = network ?? _showingNetwork,
                revenue = revenue,
                precision = precision,
                where = _showingWhere,
                floorEcpmUsd = _showingFloor > 0d ? _showingFloor : (double?)null,
                estimatedEcpmUsd = _showingEcpm > 0d ? _showingEcpm : (double?)null,
                waterfallPosition = detail.waterfallPosition,
                instanceName = detail.instanceName,
                formatRaw = detail.formatRaw,
                extraMeta = detail.extraMeta
            });

            try
            {
                onPaid?.Invoke(r.format, network ?? _showingNetwork, revenue, detail);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        #endregion

        #region show (FalconMaxService gọi)

        /// <summary>
        /// Ad sẽ được chiếu cho lượt show <paramref name="showType"/>: interstitial = inter eCpm cao nhất;
        /// rewarded = eCpm cao nhất trong RW (+ inter nếu <paramref name="allowInterForReward"/>). Hoà giá thì
        /// RW thắng inter. null = Lua không có ad nào sẵn.
        /// </summary>
        public static LuaAdRecord PeekBest(AdType showType, bool allowInterForReward = true)
        {
            if (_host == null || _showing != null) return null;
            if (showType == AdType.Interstitial) return BestReady(_inter.Values, null);
            if (showType != AdType.Reward) return null;
            var best = BestReady(_reward.Values, null);
            return allowInterForReward ? BestReady(_inter.Values, best) : best;
        }

        public static double BestReadyRevenue(AdType format, string mediation, LuaAdRecord except)
        {
            var best = 0d;
            foreach (var r in Records(format).Values)
            {
                if (r == except || r.mediation != mediation || !HasAd(r)) continue;
                if (!_loaders.TryGetValue(r.mediation, out var loader) || !loader.IsReady(r)) continue;
                if (r.Revenue > best) best = r.Revenue;
            }

            return best;
        }

        public static bool HasReady(AdType showType, bool allowInterForReward = true)
            => PeekBest(showType, allowInterForReward) != null;

        private static LuaAdRecord BestReady(IEnumerable<LuaAdRecord> records, LuaAdRecord best)
        {
            foreach (var r in records)
            {
                if (r.status != LuaAdStatus.Ready || r.isShowing) continue;
                if (!_loaders.TryGetValue(r.mediation, out var loader) || !loader.IsReady(r)) continue;
                if (best == null || r.eCpm > best.eCpm) best = r;
            }

            return best;
        }

        /// <summary>
        /// Chiếu ad tốt nhất cho lượt <paramref name="showType"/> (xem <see cref="PeekBest"/>). false = không có ad,
        /// KHÔNG callback nào được bắn. true = bắn đúng một trong <paramref name="onHidden"/> /
        /// <paramref name="onFail"/>; <paramref name="onReward"/> trước onHidden khi người chơi được thưởng
        /// (ad rewarded: theo callback của mạng; inter chiếu thay rewarded: lúc đóng ad).
        /// </summary>
        public static bool Show(AdType showType, string where, bool allowInterForReward, Action onDisplayed,
            Action onReward, Action onHidden, Action onFail, Action<AdType, string, double, LuaPaidDetail> onPaid)
        {
            var r = PeekBest(showType, allowInterForReward);
            if (_host != null && _showing == null) LogCache(showType, where, allowInterForReward, r);
            if (r == null || !_loaders.TryGetValue(r.mediation, out var loader)) return false;

            _showing = r;
            _showingAs = showType;
            _showingWhere = where;
            _showingEcpm = r.eCpm;
            _showingFloor = r.floor;
            _showingNetwork = r.networkName;
            _showingPaid = false;
            _rewardGranted = false;
            _onDisplayed = onDisplayed;
            _onReward = onReward;
            _onHidden = onHidden;
            _onFail = onFail;
            _onPaid = onPaid;

            r.isShowing = true;
            r.status = LuaAdStatus.Idle; //ad rời "kho" ngay lúc chiếu
            Log($"SHOW {showType}{(showType != r.format ? " (bằng inter)" : "")}: {r.mediation} {r.adUnitType} " +
                $"{Name(r)} eCpm={Money(r.eCpm)} @ {where}");

            AdShowReporter.Shown(r.format, where, ReporterMediation(r), r.adUnitId);

            bool shown;
            try
            {
                shown = loader.Show(r, where);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                shown = false;
            }

            if (shown) return true;

            //vừa hết ad đúng lúc gọi -> trả lại trạng thái, không bắn callback show nào
            AdShowReporter.ShowFailed(r.format, "not_ready_at_show", r.adUnitId, _showingNetwork,
                _showingFloor > 0d ? _showingFloor : (double?)null, _showingEcpm > 0d ? _showingEcpm : (double?)null);
            EndShow();
            FailLoad(r, "not_ready_at_show", measureLoading: false);
            return false;
        }

        private static void EndShow()
        {
            if (_showing != null) _showing.isShowing = false;
            _showing = null;
            _showingWhere = null;
            _showingNetwork = null;
            _onDisplayed = _onReward = _onHidden = _onFail = null;
            _onPaid = null;
        }

        #region log kho ad (AdArbitration)

        private const string _ARBITRATION_TAG = "[AdArbitration]";

        //eCpm in ra cho unit chưa có ad
        private const double _NO_AD_ECPM = -100d;

        /// <summary>Kho ad hiện tại (đủ 4 nhóm) — dùng cho bảng test.</summary>
        internal static string DescribeCache() => string.Join("\n", CacheSections(null, true, true));

        /// <summary>Log kho ad ngay lúc này (không show), cùng định dạng với log lúc show.</summary>
        internal static void LogCacheNow(string reason)
        {
            Log($"{_ARBITRATION_TAG}[KHO] TRẠNG THÁI KHO ({reason}) — ad đang được giữ & giá:\n" +
                string.Join("\n", CacheSections(null, true, true)));
        }

        /// <summary>
        /// MỘT log lúc show (lọc: <c>[AdArbitration]</c>), giống log AdArbitration của MultiCall: các nhóm được xét
        /// (MAX rewarded / MAX interstitial / ADMOB rewarded / ADMOB interstitial), mỗi ad unit một dòng (chưa có ad
        /// -> eCpm = -100), best của từng nhóm, mediation thắng và ad sẽ show.
        /// </summary>
        private static void LogCache(AdType showType, string where, bool allowInterForReward, LuaAdRecord chosen)
        {
            var isReward = showType == AdType.Reward;
            var withReward = isReward;
            var withInter = !isReward || allowInterForReward;
            var crossShow = isReward && allowInterForReward;

            var formats = new List<AdType>();
            if (withReward) formats.Add(AdType.Reward);
            if (withInter) formats.Add(AdType.Interstitial);

            var maxBest = BestEcpm(formats, LuaAdRecord.MEDIATION_MAX);
            var admobRv = withReward ? BestEcpm(new[] { AdType.Reward }, LuaAdRecord.MEDIATION_ADMOB) : (double?)null;
            var admobInter = withInter ? BestEcpm(new[] { AdType.Interstitial }, LuaAdRecord.MEDIATION_ADMOB) : (double?)null;

            var sb = new System.Text.StringBuilder();
            sb.Append($"{_ARBITRATION_TAG}[{(isReward ? "REWARDED" : "INTERSTITIAL")}] CHỌN AD ĐỂ SHOW tại '{where}'" +
                      " — ad đang được giữ & giá:");
            sb.Append($"\n  MAX   best={Usd(maxBest)}" +
                      (crossShow ? " (= max của rewarded & interstitial vì có cross-show)" : ""));
            foreach (var line in CacheSections(chosen, withReward, withInter, crossShow)) sb.Append('\n').Append(line);

            var bids = $"MAX={Usd(maxBest)}" + (admobRv.HasValue ? $" vs ADMOB_RV={Usd(admobRv.Value)}" : "") +
                       (admobInter.HasValue ? $" vs ADMOB_INTER={Usd(admobInter.Value)}" : "");
            sb.Append($"\n  => THẮNG BID: {(chosen == null ? "KHÔNG AI (không có ad nào sẵn sàng)" : chosen.mediation.ToUpperInvariant())}" +
                      $" ({bids}; quy ước {_NO_AD_ECPM:0}=không có ad, 0=có ad chưa biết giá, eCpm cao nhất thắng, " +
                      "hoà giá thì rewarded thắng interstitial, cùng format thì ad xin load trước thắng)");
            sb.Append(chosen == null
                ? "\n  => AD SẼ SHOW: KHÔNG CÓ -> trả về luồng cũ"
                : $"\n  => AD SẼ SHOW: {chosen.mediation.ToUpperInvariant()} {FormatName(chosen.format)} {Label(chosen)} " +
                  $"{Name(chosen)} eCpm={Usd(chosen.eCpm)} network={chosen.networkName ?? "?"} ready={IsReadyNow(chosen)}");
            Log(sb.ToString());
        }

        /// <summary>
        /// Các nhóm của log, thứ tự: MAX rewarded, MAX interstitial, ADMOB rewarded, ADMOB interstitial (bỏ nhóm
        /// không được xét). Trong nhóm: theo thứ tự script xin load lần đầu, ad best của nhóm có "&lt;== ... BEST".
        /// </summary>
        private static IEnumerable<string> CacheSections(LuaAdRecord chosen, bool withReward, bool withInter,
            bool crossShow = false)
        {
            foreach (var mediation in new[] { LuaAdRecord.MEDIATION_MAX, LuaAdRecord.MEDIATION_ADMOB })
            {
                var med = mediation.ToUpperInvariant();
                foreach (var format in new[] { AdType.Reward, AdType.Interstitial })
                {
                    if (format == AdType.Reward ? !withReward : !withInter) continue;

                    var group = Records(format).Values.Where(r => r.mediation == mediation).ToArray();
                    var best = BestReady(group, null);
                    var cross = crossShow && format == AdType.Interstitial ? " (có thể được show chéo thay rewarded)" : "";

                    if (mediation == LuaAdRecord.MEDIATION_MAX)
                    {
                        yield return $"  -- MAX {FormatName(format)}{cross} -- {group.Length} unit, {group.Count(IsReadyNow)} có ad";
                        //DEBUG: tóm tắt thuật toán do script tự gắn (context.debugSummary)
                        var summary = _context?.DebugSummary(format);
                        if (summary != null) yield return "    " + summary;
                    }
                    else
                        yield return $"  ADMOB {FormatName(format)} best={Usd(best?.eCpm ?? _NO_AD_ECPM)}{cross} — " +
                                     $"{group.Length} unit, {group.Count(IsReadyNow)} có ad";

                    if (group.Length == 0) yield return "    (script chưa xin load unit nào)";
                    foreach (var r in group)
                    {
                        var ready = IsReadyNow(r);
                        var floor = r.status == LuaAdStatus.Waiting ? r.pendingFloor : r.floor;
                        yield return $"    {Label(r)} {Name(r)} state={(r.isShowing ? "Showing" : r.status.ToString())} " +
                                     $"ready={ready} eCpm={Usd(ready ? r.eCpm : _NO_AD_ECPM)} floor={Usd(floor)} " +
                                     $"fails={r.numRetry} network={(ready ? r.networkName ?? "?" : "?")}" +
                                     (r == best ? $"   <== {med} BEST" : "") +
                                     (r == chosen ? "   <== SẼ SHOW" : "");
                    }
                }
            }
        }

        //eCpm cao nhất của mediation trong các format, -100 nếu không có ad
        private static double BestEcpm(IEnumerable<AdType> formats, string mediation)
        {
            LuaAdRecord best = null;
            foreach (var format in formats)
                best = BestReady(Records(format).Values.Where(r => r.mediation == mediation), best);
            return best?.eCpm ?? _NO_AD_ECPM;
        }

        // [default] = id không đặt sàn; [floor] = id MAX có sàn; [floor gắn MAX <id>] = bản load AdMob bước C
        private static string Label(LuaAdRecord r)
        {
            //DEBUG: nhãn do script tự gắn (vd "x1.2->1.1") nếu có
            var debug = _context?.DebugLabel(r.format, r.Key);
            if (!string.IsNullOrEmpty(debug)) return $"[{debug}]";
            if (r.linkedAdUnitId != null) return $"[floor gắn MAX {r.linkedAdUnitId}]";
            var floor = r.status == LuaAdStatus.Waiting ? r.pendingFloor : r.floor;
            //id từ bid floor chưa load lần nào (vd id gốc của id AdMob floor: chỉ load qua bản gắn MAX)
            // -> floor = 0 nhưng KHÔNG phải default, in loại id thay vì đoán
            if (r.status == LuaAdStatus.Idle && floor <= 0d)
                return r.adUnitIdType != null ? $"[{r.adUnitIdType} chưa load]" : "[chưa load]";
            return floor > 0d ? "[floor]" : "[default]";
        }

        private static string FormatName(AdType format) => format == AdType.Reward ? "rewarded" : "interstitial";

        private static bool IsReadyNow(LuaAdRecord r)
            => HasAd(r) && _loaders.TryGetValue(r.mediation, out var loader) && loader.IsReady(r);

        private static string Usd(double v) => "$" + v.ToString("F6", CultureInfo.InvariantCulture);

        private static bool HasAd(LuaAdRecord r) => r.status == LuaAdStatus.Ready && !r.isShowing;

        #endregion

        #endregion

        #region tiện ích

        /// <summary>Đồng bộ context rồi gọi hàm Lua. null = không có hàm / lỗi.</summary>
        private static DynValue CallLua(string function, params DynValue[] args)
        {
            if (_host == null || !_host.Has(function)) return null;
            try
            {
                _context?.Sync(_inter.Values, _reward.Values, IsOffline);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            return _host.Call(function, args);
        }

        private static void ReportLoad(LuaAdRecord r, bool success, string error, long latencyMs)
        {
            try
            {
                FalconBigDataController.Ad.OnLoadResult(new AdLoadResultParam
                {
                    adType = r.format,
                    adUnitId = r.adUnitId,
                    success = success,
                    adMediation = r.mediation,
                    adAlgorithm = r.adAlgorithm,
                    networkName = success ? r.networkName : null,
                    tier = r.multiplier > 0d ? r.multiplier : (double?)null,
                    floor = !r.IsMax && r.floor > 0d ? r.floor : (double?)null,
                    ecpmFloor = r.floor > 0d ? r.floor : 0d,
                    estimatedEcpm = success && r.eCpm > 0d ? r.eCpm : (double?)null,
                    errorMess = error,
                    loadingMs = latencyMs
                });
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
            }
        }

        private static string ReporterMediation(LuaAdRecord r)
            => r.IsMax ? AdShowReporter.MEDIATION_MAX : AdShowReporter.MEDIATION_ADMOB;

        private static double Elapsed(LuaAdRecord r)
            => r.startLoadUtc.HasValue ? Math.Max(0d, (DateTime.UtcNow - r.startLoadUtc.Value).TotalSeconds) : 0d;

        private static void Invoke(Action action)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private static string Name(LuaAdRecord r) => r.Key;

        private static string Money(double ecpm) => "$" + ecpm.ToString("0.###", CultureInfo.InvariantCulture);

        private static void Log(string message)
        {
#if ENABLE_DEBUG_LOG
            Debug.Log($"{_TAG} {message}");
#endif
        }

        private static void Warn(string message)
        {
#if ENABLE_DEBUG_LOG
            Debug.LogWarning($"{_TAG} {message}");
#endif
        }

        private static bool IsOffline => Application.internetReachability == NetworkReachability.NotReachable;

        private static bool _wasOffline;
        private static float _nextNetworkCheck;

        private static void WatchNetwork()
        {
            if (Time.unscaledTime < _nextNetworkCheck) return;
            _nextNetworkCheck = Time.unscaledTime + MultiCallMaxProvider8Config.Current.NetworkCheckInterval;

            var offline = IsOffline;
            if (offline == _wasOffline) return;
            _wasOffline = offline;
            if (offline)
            {
                Log($"MẤT MẠNG ({Application.internetReachability}) -> context.isOffline = true");
                return;
            }

            if (_host == null || _luaMode != true || !_maxInitDone) return;
            Log($"CÓ MẠNG LẠI ({Application.internetReachability}) -> gọi {_FN_NETWORK_BACK}()" +
                (_host.Has(_FN_NETWORK_BACK) ? "" : " — script KHÔNG có hàm này, bỏ qua"));
            CallLua(_FN_NETWORK_BACK);
        }

        private static bool CancelLoad(string mediation, AdType format, string adUnitId, string linkedAdUnitId)
        {
            if (string.IsNullOrWhiteSpace(adUnitId)) return false;
            var key = LuaAdRecord.MakeKey(adUnitId.Trim(),
                string.IsNullOrWhiteSpace(linkedAdUnitId) ? null : linkedAdUnitId.Trim());
            if (!Records(format).TryGetValue(key, out var record) || record.mediation != mediation) return false;
            if (record.status != LuaAdStatus.Waiting) return false;

            record.status = LuaAdStatus.Idle;
            Log($"{mediation}.Cancel{(format == AdType.Reward ? "Rewarded" : "Interstitial")}({Name(record)}): " +
                $"huỷ lịch load floor={Money(record.pendingFloor)}");
            return true;
        }

        private sealed class Runner : MonoBehaviour
        {
            private float _nextTick;

            private void Update()
            {
                lock (_queueLock)
                {
                    if (_queue.Count > 0)
                    {
                        _drain.AddRange(_queue);
                        _queue.Clear();
                    }
                }

                if (_drain.Count > 0)
                {
                    foreach (var action in _drain) LuaAdMonetize.Invoke(action);
                    _drain.Clear();
                }

                if (Time.unscaledTime < _nextTick) return;
                _nextTick = Time.unscaledTime + _TICK_SECONDS;
                WatchNetwork();
                PumpLoads();
                CheckExpired();
            }
        }

        #endregion

        #region bridge

        private sealed class Bridge : ILuaAdBridge
        {
            public bool Load(string mediation, AdType format, string adUnitId, double floor, double delaySeconds,
                string linkedAdUnitId) => RequestLoad(mediation, format, adUnitId, floor, delaySeconds, linkedAdUnitId);

            public bool Cancel(string mediation, AdType format, string adUnitId, string linkedAdUnitId)
                => CancelLoad(mediation, format, adUnitId, linkedAdUnitId);
        }

        //chỉ để chạy thử script (ValidateScript): từ chối mọi lệnh load
        private sealed class RejectBridge : ILuaAdBridge
        {
            public bool Load(string mediation, AdType format, string adUnitId, double floor, double delaySeconds,
                string linkedAdUnitId) => false;

            public bool Cancel(string mediation, AdType format, string adUnitId, string linkedAdUnitId) => false;
        }

        #endregion
    }
}
#endif
