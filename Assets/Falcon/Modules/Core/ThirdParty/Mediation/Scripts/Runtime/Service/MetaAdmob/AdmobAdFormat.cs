namespace Falcon.Admob
{
    /// <summary>
    /// Format quảng cáo dùng NỘI BỘ module. Cố ý KHÔNG dùng enum AdType của project nào cả để module
    /// export sang dự án khác không kéo theo dependency — project tự map sang enum của mình trong lớp
    /// bridge (xem <see cref="AdmobMediationHooks"/>).
    /// </summary>
    public enum AdmobAdFormat
    {
        Banner = 0,
        Interstitial = 1,
        Rewarded = 2,
        AppOpen = 3
    }
}
