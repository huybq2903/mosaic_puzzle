/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-02
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using Falcon.Helpers.Devkit;
using MoonSharp.Interpreter;
using UnityEngine;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime.LuaDynamic
{
    /// <summary>Phía Unity mà <see cref="LuaAdContext"/> gọi tới khi Lua xin load.</summary>
    internal interface ILuaAdBridge
    {
        /// <summary>
        /// Xin load sau <paramref name="delaySeconds"/>. true nếu nhận yêu cầu. Unit đang chờ (Waiting) thì lịch
        /// mới THAY lịch cũ. <paramref name="linkedAdUnitId"/> chỉ có ở AdMob, null = bản load thường.
        /// </summary>
        bool Load(string mediation, AdType format, string adUnitId, double floor, double delaySeconds,
            string linkedAdUnitId);

        bool Cancel(string mediation, AdType format, string adUnitId, string linkedAdUnitId);
    }

    /// <summary>
    /// Biến toàn cục <c>context</c> trong Lua — cầu nối duy nhất giữa Unity và script, đúng như spec:
    /// <code>
    /// -- Unity -> Lua (chỉ đọc, chép lại trước mỗi lần gọi vào Lua)
    /// context.listInterstitialAd[key] = { mediation, adUnitType, adUnitIdType, adUnitName, adUnitId, floor, eCpm,
    ///                                     startLoadTime, loadingTime, numRetry,
    ///                                     linkedAdUnitId, multiplier, timeConfig, networkName, status, lastError }
    /// context.listRewardedAd[key]     = { ...cùng các field... }
    ///   key = adUnitId, hoặc adUnitId .. "@" .. linkedAdUnitId với bản load AdMob gắn id MAX
    ///   adUnitName = adUnitId (giữ field cho đúng bảng của spec)
    ///   adUnitIdType = "default_1" | "default_2" | "h1" | "h2" | ... theo sc_ad_bid_floor (nil nếu id không có
    ///   trong danh sách); multiplier / timeConfig cũng lấy từ sc_ad_bid_floor. Id của sc_ad_bid_floor có sẵn
    ///   trong list (status "Idle") trước cả khi script xin load.
    /// context.params                  -- tham số của lượt hỏi IsShow... (nil ngoài lượt đó)
    ///
    /// -- Lua -> Unity
    /// context.Max.LoadInterstitial(adUnitId, floor, delayTime)
    /// context.Max.LoadRewarded(adUnitId, floor, delayTime)
    /// context.Admob.LoadInterstitial(adUnitId, floor, delayTime [, linkedAdUnitId])
    /// context.Admob.LoadRewarded(adUnitId, floor, delayTime [, linkedAdUnitId])
    ///   linkedAdUnitId = id MAX đầy đủ mà bản load AdMob này gắn vào (bước C). Cùng adUnitId AdMob, mỗi
    ///   linkedAdUnitId là một bản load riêng: trạng thái riêng, ad riêng.
    /// </code>
    /// Không đưa thẳng object C# nào vào Lua (không dùng UserData / reflection) -> chạy ổn trên IL2CPP và
    /// script chỉ chạm được đúng những gì liệt kê ở trên. Log từ Lua: dùng <c>print</c>.
    /// </summary>
    internal sealed class LuaAdContext
    {
        public const string GLOBAL_NAME = "context";
        public const string PARAMS_NAME = "params";

        private static readonly DateTime _Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private readonly ILuaAdBridge _bridge;

        private Table _context;
        private Table _interList;
        private Table _rewardList;

        public LuaAdContext(ILuaAdBridge bridge)
        {
            _bridge = bridge;
        }

        /// <summary>Tạo bảng <c>context</c> trong môi trường Lua mới.</summary>
        public void Install(Script script)
        {
            _context = new Table(script);
            _interList = new Table(script);
            _rewardList = new Table(script);
            _context.Set("listInterstitialAd", DynValue.NewTable(_interList));
            _context.Set("listRewardedAd", DynValue.NewTable(_rewardList));
            _context.Set("Max", DynValue.NewTable(MediationTable(script, LuaAdRecord.MEDIATION_MAX)));
            _context.Set("Admob", DynValue.NewTable(MediationTable(script, LuaAdRecord.MEDIATION_ADMOB)));
            script.Globals.Set(GLOBAL_NAME, DynValue.NewTable(_context));
        }

        /// <summary>
        /// Gắn giá trị mới nhất của mọi unit vào 2 danh sách của context (ghi đè tại chỗ, xem
        /// <see cref="Fill"/>). Gọi trước MỖI lần vào Lua.
        /// </summary>
        public void Sync(IEnumerable<LuaAdRecord> interstitials, IEnumerable<LuaAdRecord> rewardeds, bool isOffline)
        {
            Fill(_interList, interstitials);
            Fill(_rewardList, rewardeds);
            _context?.Set("isOffline", DynValue.NewBoolean(isOffline));
        }

        /// <summary>
        /// Gắn <c>context.params</c> cho lượt hỏi IsShow.... Giá trị: string / bool / số; kiểu khác được đổi
        /// thành chuỗi. null = gỡ params.
        /// </summary>
        public void SetParams(IDictionary<string, object> values)
        {
            if (_context == null) return;
            if (values == null)
            {
                _context.Set(PARAMS_NAME, DynValue.Nil);
                return;
            }

            var table = new Table(_context.OwnerScript);
            foreach (var pair in values)
                if (!string.IsNullOrEmpty(pair.Key))
                    table.Set(pair.Key, ToDyn(pair.Value));
            _context.Set(PARAMS_NAME, DynValue.NewTable(table));
        }

        public DynValue Params => _context?.Get(PARAMS_NAME) ?? DynValue.Nil;

        // ===== DEBUG (chỉ để soi log khi test local, gỡ được) =====
        // Script tự gắn nhãn vào entry của nó (field "debugLabel", vd "x1.2->1.1") và tóm tắt thuật toán vào
        // context.debugSummary["Interstitial" | "Reward"]. Log [AdArbitration] đọc ra nếu có, không có thì thôi.

        /// <summary>Field <c>debugLabel</c> mà script gắn vào <c>list[key]</c>. null = không có.</summary>
        public string DebugLabel(AdType format, string key)
        {
            var list = format == AdType.Reward ? _rewardList : _interList;
            var row = list?.Get(key);
            if (row == null || row.Type != DataType.Table) return null;
            var value = row.Table.Get("debugLabel");
            return value.Type == DataType.String ? value.String : null;
        }

        /// <summary><c>context.debugSummary["Interstitial" | "Reward"]</c>. null = không có.</summary>
        public string DebugSummary(AdType format)
        {
            var table = _context?.Get("debugSummary");
            if (table == null || table.Type != DataType.Table) return null;
            var value = table.Table.Get(format == AdType.Reward ? "Reward" : "Interstitial");
            return value.Type == DataType.String ? value.String : null;
        }

        /// <summary>
        /// Gắn giá trị của từng unit vào <c>list[key]</c>. KHÔNG xoá bảng, KHÔNG thay bảng con: chỉ ghi đè
        /// đúng các field của spec -> field Lua tự gắn thêm vào entry (vd hệ số, bộ đếm của thuật toán) được giữ.
        /// </summary>
        private static void Fill(Table list, IEnumerable<LuaAdRecord> records)
        {
            if (list == null || records == null) return;
            foreach (var r in records)
            {
                var key = r.Key;
                var existing = list.Get(key);
                var row = existing.Type == DataType.Table ? existing.Table : new Table(list.OwnerScript);
                row.Set("mediation", DynValue.NewString(r.mediation));
                row.Set("adUnitType", DynValue.NewString(r.adUnitType));
                row.Set("adUnitIdType", r.adUnitIdType == null ? DynValue.Nil : DynValue.NewString(r.adUnitIdType));
                row.Set("multiplier", DynValue.NewNumber(r.multiplier));
                row.Set("timeConfig", r.timeConfig == null ? DynValue.Nil : DynValue.NewString(r.timeConfig));
                row.Set("adUnitName", DynValue.NewString(r.adUnitId));
                row.Set("adUnitId", DynValue.NewString(r.adUnitId));
                row.Set("linkedAdUnitId", r.linkedAdUnitId == null ? DynValue.Nil : DynValue.NewString(r.linkedAdUnitId));
                row.Set("floor", DynValue.NewNumber(r.floor));
                row.Set("eCpm", DynValue.NewNumber(r.eCpm));
                row.Set("startLoadTime", DynValue.NewNumber(r.startLoadUtc.HasValue ? ToUnix(r.startLoadUtc.Value) : 0d));
                row.Set("loadingTime", DynValue.NewNumber(r.loadingTime));
                row.Set("numRetry", DynValue.NewNumber(r.numRetry));
                row.Set("networkName", r.networkName == null ? DynValue.Nil : DynValue.NewString(r.networkName));
                row.Set("status", DynValue.NewString(r.isShowing ? "Showing" : r.status.ToString()));
                row.Set("lastError", r.lastError == null ? DynValue.Nil : DynValue.NewString(r.lastError));
                if (existing.Type != DataType.Table) list.Set(key, DynValue.NewTable(row));
            }
        }

        private Table MediationTable(Script script, string mediation)
        {
            var table = new Table(script);
            table.Set("LoadInterstitial", LoadCallback(mediation, AdType.Interstitial, "LoadInterstitial"));
            table.Set("LoadRewarded", LoadCallback(mediation, AdType.Reward, "LoadRewarded"));
            table.Set("CancelInterstitial", CancelCallback(mediation, AdType.Interstitial, "CancelInterstitial"));
            table.Set("CancelRewarded", CancelCallback(mediation, AdType.Reward, "CancelRewarded"));
            return table;
        }

        private DynValue CancelCallback(string mediation, AdType format, string name)
        {
            return DynValue.NewCallback((_, args) =>
            {
                var offset = args.IsMethodCall ? 1 : 0;
                var adUnitId = AsString(Arg(args, offset));
                var linkedAdUnitId = mediation == LuaAdRecord.MEDIATION_ADMOB ? AsString(Arg(args, offset + 1)) : null;
                bool cancelled;
                try
                {
                    cancelled = _bridge.Cancel(mediation, format, adUnitId, linkedAdUnitId);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    cancelled = false;
                }

                return DynValue.NewBoolean(cancelled);
            }, mediation + "." + name);
        }

        private DynValue LoadCallback(string mediation, AdType format, string name)
        {
            return DynValue.NewCallback((_, args) =>
            {
                //cho phép cả context.Max:LoadX(...) lẫn context.Max.LoadX(...)
                var offset = args.IsMethodCall ? 1 : 0;
                var adUnitId = AsString(Arg(args, offset));
                var floor = AsNumber(Arg(args, offset + 1));
                var delay = AsNumber(Arg(args, offset + 2));
                //tham số thứ 4 chỉ có ở Admob; Max đúng 3 tham số như spec
                var linkedAdUnitId = mediation == LuaAdRecord.MEDIATION_ADMOB ? AsString(Arg(args, offset + 3)) : null;
                bool accepted;
                try
                {
                    accepted = _bridge.Load(mediation, format, adUnitId, floor, delay, linkedAdUnitId);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    accepted = false;
                }

                return DynValue.NewBoolean(accepted);
            }, mediation + "." + name);
        }

        private static DynValue ToDyn(object value)
        {
            switch (value)
            {
                case null: return DynValue.Nil;
                case string s: return DynValue.NewString(s);
                case bool b: return DynValue.NewBoolean(b);
                case int _:
                case long _:
                case float _:
                case double _:
                case decimal _:
                case short _:
                case byte _:
                case uint _:
                case ulong _:
                    return DynValue.NewNumber(Convert.ToDouble(value, CultureInfo.InvariantCulture));
                case DateTime dt: return DynValue.NewNumber(ToUnix(dt.ToUniversalTime()));
                case Enum en: return DynValue.NewString(en.ToString());
                default: return DynValue.NewString(Convert.ToString(value, CultureInfo.InvariantCulture));
            }
        }

        private static DynValue Arg(CallbackArguments args, int index)
            => index < args.Count ? args[index] ?? DynValue.Nil : DynValue.Nil;

        private static string AsString(DynValue v)
        {
            if (v == null || v.IsNil()) return null;
            return v.Type == DataType.Number
                ? v.Number.ToString(CultureInfo.InvariantCulture)
                : v.CastToString();
        }

        private static double AsNumber(DynValue v)
        {
            if (v == null || v.IsNil()) return 0d;
            var n = v.CastToNumber();
            return n.HasValue && !double.IsNaN(n.Value) && !double.IsInfinity(n.Value) ? n.Value : 0d;
        }

        private static double ToUnix(DateTime utc) => (utc - _Epoch).TotalSeconds;
    }
}
