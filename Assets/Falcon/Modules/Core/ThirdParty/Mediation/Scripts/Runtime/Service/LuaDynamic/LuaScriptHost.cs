/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-02
 */

using System;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Platforms;
using UnityEngine;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime.LuaDynamic
{
    /// <summary>
    /// Bọc một môi trường Lua (MoonSharp) đã nạp script từ server.
    ///
    /// Script được viết theo MỘT trong hai kiểu:
    ///  - Hàm toàn cục: <c>function OnInterstitialLoadSuccess(adUnitId) ... end</c>
    ///  - Object A: script <c>return A</c> (hoặc khai báo biến toàn cục <c>A</c>), các hàm khai báo bằng DẤU
    ///    HAI CHẤM <c>function A:OnInterstitialLoadSuccess(adUnitId) ... end</c> — C# gọi kèm self = A.
    /// Có object thì chỉ tìm hàm trong object, không tìm hàm toàn cục.
    ///
    /// An toàn:
    ///  - Sandbox <see cref="CoreModules.Preset_SoftSandbox"/>: không có io, os.execute, load/require, debug.
    ///  - Mỗi lần gọi chạy trong coroutine tự ngắt sau <see cref="_MAX_INSTRUCTIONS"/> lệnh: script lặp vô hạn
    ///    chỉ hỏng lượt gọi đó, không treo game.
    ///  - Mọi lỗi Lua bị bắt lại ở đây; người gọi nhận <c>null</c>.
    /// Chỉ gọi từ main thread.
    /// </summary>
    internal sealed class LuaScriptHost
    {
        private const string _TAG = "[LuaAd]";
        private const string _OBJECT_NAME = "A";

        //ngắt mỗi _AUTO_YIELD_EVERY lệnh để đếm; quá _MAX_SLICES lần -> coi là lặp vô hạn
        private const int _AUTO_YIELD_EVERY = 1000;
        private const int _MAX_SLICES = 500;
        private const int _MAX_INSTRUCTIONS = _AUTO_YIELD_EVERY * _MAX_SLICES;

        private static bool _platformSet;

        private readonly Script _script;
        private Table _object;

        public Script Script => _script;

        /// <summary>Đang chạy kiểu object A (true) hay hàm toàn cục (false).</summary>
        public bool IsObjectMode => _object != null;

        private LuaScriptHost(Script script)
        {
            _script = script;
        }

        /// <summary>
        /// Biên dịch + chạy phần thân script. <paramref name="install"/> được gọi TRƯỚC khi chạy script để cài
        /// <c>context</c> vào biến toàn cục (script có thể dùng context ngay ở thân file).
        /// Trả null nếu script lỗi; lý do nằm trong <paramref name="error"/>.
        /// </summary>
        public static LuaScriptHost Create(string code, Action<Script> install, out string error)
        {
            error = null;
            try
            {
                if (!_platformSet)
                {
                    //không đọc file / biến môi trường / chạy lệnh hệ thống, dù module nào lỡ được bật
                    Script.GlobalOptions.Platform = new LimitedPlatformAccessor();
                    _platformSet = true;
                }

                var script = new Script(CoreModules.Preset_SoftSandbox);
                //print() của script: chỉ in khi ENABLE_DEBUG_LOG. Gán no-op (không để null) để MoonSharp không tự
                //in ra Console theo mặc định
#if ENABLE_DEBUG_LOG
                script.Options.DebugPrint = s => Debug.Log($"{_TAG}[lua] {s}");
#else
                script.Options.DebugPrint = _ => { };
#endif

                var host = new LuaScriptHost(script);
                install?.Invoke(script);

                var chunk = script.LoadString(code, null, "sc_ad_monetize_lua_script");
                var returned = host.RunGuarded(chunk, Array.Empty<DynValue>());

                if (returned != null && returned.Type == DataType.Table)
                    host._object = returned.Table;
                else
                {
                    var global = script.Globals.Get(_OBJECT_NAME);
                    if (global.Type == DataType.Table) host._object = global.Table;
                }

                return host;
            }
            catch (InterpreterException e)
            {
                error = e.DecoratedMessage ?? e.Message;
                return null;
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }
        }

        /// <summary>Script có khai báo hàm này không.</summary>
        public bool Has(string functionName) => Resolve(functionName) != null;

        /// <summary>
        /// Gọi hàm <paramref name="functionName"/> của script. Trả về kết quả, hoặc null nếu script không có
        /// hàm đó / hàm ném lỗi / chạy quá giới hạn lệnh (đã log).
        /// </summary>
        public DynValue Call(string functionName, params DynValue[] args)
        {
            var fn = Resolve(functionName);
            if (fn == null) return null;

            DynValue[] callArgs = args ?? Array.Empty<DynValue>();
            if (_object != null)
            {
                //object A: hàm khai báo bằng dấu hai chấm -> tham số đầu là self
                callArgs = new DynValue[callArgs.Length + 1];
                callArgs[0] = DynValue.NewTable(_object);
                if (args != null) Array.Copy(args, 0, callArgs, 1, args.Length);
            }

            return CallFunction(fn, functionName, callArgs);
        }

        /// <summary>
        /// Gọi thẳng một hàm Lua (vd callback của <c>context.setTimeout</c>) — không thêm self. null = lỗi
        /// (đã log) hoặc <paramref name="function"/> không phải hàm.
        /// </summary>
        public DynValue CallFunction(DynValue function, string label, params DynValue[] args)
        {
            if (function == null || function.Type != DataType.Function) return null;
            try
            {
                return RunGuarded(function, args ?? Array.Empty<DynValue>());
            }
            catch (InterpreterException e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError($"{_TAG} lỗi khi chạy {label}: {e.DecoratedMessage ?? e.Message}");
#endif
                return null;
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError($"{_TAG} lỗi khi chạy {label}: {e}");
#endif
                return null;
            }
        }

        private DynValue Resolve(string functionName)
        {
            var value = _object != null ? _object.Get(functionName) : _script.Globals.Get(functionName);
            return value != null && value.Type == DataType.Function ? value : null;
        }

        private DynValue RunGuarded(DynValue function, DynValue[] args)
        {
            var co = _script.CreateCoroutine(function).Coroutine;
            co.AutoYieldCounter = _AUTO_YIELD_EVERY;

            var result = co.Resume(args);
            var slices = 1;
            while (co.State == CoroutineState.ForceSuspended)
            {
                if (++slices > _MAX_SLICES)
                    throw new ScriptRuntimeException(
                        $"chạy quá {_MAX_INSTRUCTIONS} lệnh trong một lượt gọi (lặp vô hạn?) -> ngắt");
                result = co.Resume();
            }

            return result;
        }
    }
}
