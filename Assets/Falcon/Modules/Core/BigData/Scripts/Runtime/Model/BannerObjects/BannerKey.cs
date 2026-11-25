/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2025-06-12
 */

using System;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    ///     Chiều gộp của một cụm impression banner — mọi impression cùng khoá này mới được gộp một
    ///     dòng, nên <b>chỉ field nằm trong khoá mới còn đúng trên dòng gộp</b> (luật §4i của
    ///     <c>EntityLifecycle-Design.md</c>).
    ///     <br/><see cref="AdUnitId"/> vào khoá từ 19/10 dù mediation CHƯA gửi: chiều này là cách
    ///     duy nhất đối chiếu tiền với bảng kê của nhà cung cấp (họ báo doanh thu theo ad unit).
    ///     Thêm trước vì thêm SAU lúc giá trị đã lên wire là sinh ra một ô trống im lặng — dòng
    ///     per-impression (inter/reward) có mã, dòng gộp không, và chart "doanh thu theo ad unit"
    ///     cho banner ra rỗng mà không lỗi, không warning.
    ///     <br/>Chi phí gần như bằng 0: game một ad unit mỗi format thì mọi impression trong cụm
    ///     vốn đã cùng mã, thêm chiều KHÔNG chẻ thêm dòng nào. Game nhiều unit thì số dòng nhân
    ///     lên, mà phần nhân lên đúng bằng thông tin cần.
    /// </summary>
    public readonly struct BannerKey : IEquatable<BannerKey>
    {
        public string AdWhere { get; }
        public string AdPrecision { get; }
        public string AdCountry { get; }
        public string AdNetwork { get; }
        public string AdMediation { get; }

        /// <summary>Mã ad unit của mediation — null khi mediation chưa gửi (hành xử y như trước 19/10).</summary>
        public string AdUnitId { get; }

        /// <summary>
        ///     Thuật toán load của tầng mediation (26/10). VÀO KHOÁ vì nó rời rạc và gần như hằng
        ///     trong một phiên: khoá thì một dòng chỉ mang một thuật toán, còn để ngoài khoá kiểu
        ///     "bản cuối thắng" thì hai thuật toán trong cùng một cụm sẽ bị dán nhãn sai.
        ///     <br/>Dòng per-impression (inter/reward) lấy field này qua <c>ApplyContext</c>; cụm
        ///     banner không đi qua đó (decor return sớm với bản tin cụm) nên phải chở từ đây.
        /// </summary>
        public string AdAlgorithm { get; }
        
        public int CurrentLevel { get; }

        public BannerKey(string adWhere, string adPrecision, string adCountry, string adNetwork,
            string adMediation, int currentLevel, string adUnitId = null, string adAlgorithm = null)
        {
            AdWhere = adWhere;
            AdPrecision = adPrecision;
            AdCountry = adCountry;
            AdNetwork = adNetwork;
            AdMediation = adMediation;
            CurrentLevel = currentLevel;
            AdUnitId = adUnitId;
            AdAlgorithm = adAlgorithm;
        }

        public bool Equals(BannerKey other)
        {
            return AdWhere == other.AdWhere && AdPrecision == other.AdPrecision && AdCountry == other.AdCountry && AdNetwork == other.AdNetwork && AdMediation == other.AdMediation && CurrentLevel == other.CurrentLevel && AdUnitId == other.AdUnitId && AdAlgorithm == other.AdAlgorithm;
        }

        public override bool Equals(object obj)
        {
            return obj is BannerKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(AdWhere, AdPrecision, AdCountry, AdNetwork, AdMediation, CurrentLevel, AdUnitId,
                AdAlgorithm);
        }

        public static bool operator ==(BannerKey left, BannerKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(BannerKey left, BannerKey right)
        {
            return !left.Equals(right);
        }
    }
}