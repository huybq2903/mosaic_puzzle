using System;
using System.Reflection;
using System.Runtime.Serialization;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>
    /// Giá trị wire của một enum (theo <see cref="EnumMemberAttribute"/>) — để test bộ từ ĐÓNG mà không cần Newtonsoft (asmdef
    /// test không tham chiếu nó).
    /// </summary>
    internal static class WireName
    {
        public static string Of(Enum value)
        {
            var member = value.GetType().GetField(value.ToString());
            return member?.GetCustomAttribute<EnumMemberAttribute>()?.Value ?? value.ToString();
        }
    }
}
