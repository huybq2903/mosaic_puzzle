/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-06
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Falcon.Helpers.Devkit;
using JetBrains.Annotations;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    ///     Gộp <c>extraMeta</c> (dictionary game tự đặt) vào dictionary wire, và CẢNH BÁO khi một
    ///     key trùng tên field THẬT của chính lớp đang gộp.
    ///     <br/>Vì sao phải cảnh báo: merge dùng <c>PutIfAbsentAndNotNull</c> = ai-ghi-trước-giữ, mà
    ///     field thật ghi trước ⇒ key extras trùng tên bị BỎ, <b>im lặng</b>. Bản tin vẫn đi, dòng
    ///     vẫn có, cột vẫn có giá trị — giá trị của SDK, không phải của game. Người gọi chỉ biết khi
    ///     một con số trông sai, nhiều tháng sau. Tên hay trùng không phải tên hiếm:
    ///     <c>amount</c>, <c>currency</c>, <c>level</c> — đúng những tên với tay tới đầu tiên.
    ///     <br/>Cảnh báo chứ KHÔNG chặn: cho extras đè field thật thì field của hợp đồng thành thứ
    ///     game ghi đè được. Cảnh báo hiện lúc dev đang code — đúng lúc còn sửa được.
    ///     <br/>⚠ So với field của CHÍNH lớp đó, <b>không</b> so với <see cref="FWireReservedKeys"/>:
    ///     một key extras trên log event trùng tên một cột của log ad thì không mất gì, nên bắt cả
    ///     sổ cấm toàn cục là ồn và sai chỗ. Sổ cấm toàn cục dành cho kênh mở
    ///     (<c>Common.Set</c>, <c>Label.*</c>), nơi key đi lên <b>mọi</b> log.
    ///     <br/>So khớp theo dạng CHUẨN HOÁ (bỏ <c>_</c>, thường hoá) — cùng luật
    ///     <see cref="FWireReservedKeys"/>, vì tên trên wire bị chuẩn hoá ở phía sau nên
    ///     <c>failCount</c> và <c>fail_count</c> cuối cùng đụng nhau.
    ///     <br/>Giá: một lookup HashSet + hai alloc nhỏ mỗi key extras, mà extras thường 0–5 key.
    ///     Sổ tên field gom bằng reflection theo KIỂU THẬT lúc chạy, nên lớp con tự có đủ field của
    ///     cha, và thêm field vào hợp đồng là sổ tự dài ra.
    /// </summary>
    public static class FExtraMeta
    {
        /// <summary>Hai cửa mở của chính các param — không phải field hợp đồng, nên không tính.</summary>
        private const string ExtrasField = "extraMeta";

        private const string FreeParamField = "param";

        private static readonly ConcurrentDictionary<Type, HashSet<string>> kRealFields = new();

        /// <summary>
        ///     Key này có đang lấy mất chỗ của một field thật trên <paramref name="owner"/> không.
        ///     Tách riêng để test được bằng assert thẳng, không phải bắt log.
        /// </summary>
        public static bool StealsARealField([CanBeNull] Type owner, [CanBeNull] string key)
        {
            if (owner == null || string.IsNullOrEmpty(key)) return false;
            return RealFieldsOf(owner).Contains(Normalize(key));
        }

        /// <summary>
        ///     Gộp <paramref name="extras"/> vào <paramref name="target"/>, cảnh báo từng key trùng
        ///     tên field thật của <paramref name="owner"/>.
        /// </summary>
        /// <param name="owner">
        ///     Thường là <c>this</c> của param đang dựng dictionary — lấy KIỂU THẬT lúc chạy.
        /// </param>
        /// <param name="context">Nhãn đứng đầu câu cảnh báo. Để trống thì dùng tên kiểu.</param>
        /// <param name="hint">Câu khuyên riêng của stream, nối vào cuối. Có thể để trống.</param>
        public static Dictionary<string, object> MergeExtras(
            this Dictionary<string, object> target,
            [CanBeNull] Dictionary<string, object> extras,
            [CanBeNull] object owner,
            [CanBeNull] string context = null,
            [CanBeNull] string hint = null)
        {
            if (target == null || extras == null || extras.Count == 0) return target;

            var type = owner?.GetType();
            foreach (var (key, value) in extras)
            {
                if (StealsARealField(type, key)) Warn(type, key, context, hint);
                target.PutIfAbsentAndNotNull(key, value);
            }

            return target;
        }

        private static void Warn(Type type, string key, [CanBeNull] string context, [CanBeNull] string hint)
        {
            var who = string.IsNullOrEmpty(context) ? type.Name : context;
            AnalyticLogger.Instance.Warning(
                $"{who}: extraMeta['{key}'] trùng tên một field THẬT của {type.Name}. Dùng field đó " +
                "thay vì đi qua extras — đi qua extras là một cột hai nguồn, và hôm nào field thật " +
                "được điền thì giá trị extras MẤT IM LẶNG (merge là ai-ghi-trước-giữ)." +
                (string.IsNullOrEmpty(hint) ? string.Empty : " " + hint));
        }

        private static HashSet<string> RealFieldsOf(Type type)
        {
            return kRealFields.GetOrAdd(type, t =>
            {
                var names = new HashSet<string>();
                foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (field.Name == ExtrasField || field.Name == FreeParamField) continue;
                    names.Add(Normalize(field.Name));
                }

                return names;
            });
        }

        private static string Normalize(string key)
        {
            return key.Replace("_", string.Empty).ToLowerInvariant();
        }
    }
}
