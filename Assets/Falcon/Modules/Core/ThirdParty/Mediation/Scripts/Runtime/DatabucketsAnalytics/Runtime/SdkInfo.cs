using System.Globalization;

namespace Databuckets.Analytics
{
    /// <summary>SDK metadata, available before analytics is initialized.</summary>
    public static class SdkInfo
    {
        /// <summary>Change this value when releasing a new SDK version. Used by the sdk_ver and sdk_ver_num user properties.</summary>
        public const string Version = "4.0.1";

        /// <summary>Version with dots removed as an integer, such as 401 for 4.0.1. Used by sdk_ver_num.</summary>
        public static readonly int VersionNumber = int.Parse(Version.Replace(".", ""), CultureInfo.InvariantCulture);
    }
}
