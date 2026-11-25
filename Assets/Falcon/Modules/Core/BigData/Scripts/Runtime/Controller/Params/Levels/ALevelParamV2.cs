/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-01-08
 */

using System;
using System.Collections.Generic;
using Falcon.Helpers.Devkit;
using JetBrains.Annotations;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    public abstract class ALevelParamV2 : ALiveOpsParam 
    {
        public string playTurnId;
        public int currentLevel;

        /// <summary>
        ///     Danh tính bản thiết kế màn — băm của <b>data + param</b>, do CLIENT tính. Đổi
        ///     layout HOẶC đổi tuning là id mới, nên không trộn hai phiên bản màn vào một rổ.
        ///     <br/>🔴 <b>Data rỗng thì phải VẮNG, không được gửi</b> (kho bắt 09/11). Băm của chuỗi
        ///     rỗng là một giá trị trông hợp lệ — <c>1B2M2</c> — nên game không dùng level từ server
        ///     sẽ gửi CÙNG một id cho mọi màn, và ai <c>GROUP BY</c> theo cột này sẽ gộp cả game lại
        ///     thành một màn. Vắng còn hơn một id chung: vắng thì người ta đi tìm. Chặn đặt ở nơi
        ///     TÍNH id (Level Core), không ở <c>Md5Utils</c> — hàm đó còn phục vụ hợp đồng với game
        ///     server. Chặn theo <c>levelData</c> rỗng chứ không theo chuỗi ghép rỗng: data rỗng +
        ///     param có chữ ra id của param, dùng chung mọi màn cùng tuning, và lẫn đúng vào tập
        ///     "game dùng lại thiết kế màn" nên không ai soi ra.
        /// </summary>
        [FKey(RemoveIfNull = true)] [CanBeNull]
        public string currentLevelId;

        /// <summary>
        ///     Danh tính của riêng DATA màn (không gồm param tuning) — băm của <b>chỉ</b>
        ///     <c>levelData</c>, do <b>CLIENT</b> tính, cùng hàm và cùng độ dài với
        ///     <see cref="currentLevelId"/> (<c>Md5Utils.GetMd5First5Char</c>, 5 ký tự base64 = 30 bit).
        ///     <br/>⚠ <b>Sửa 09/11</b>: bản đầu của doc này viết <i>"băm do GAME SERVER đặt, client chỉ
        ///     echo lại"</i> — <b>sai</b>, và phía kho đã dựng một luật đọc lên chỗ sai đó (*"level_data_id
        ///     của server sẽ cho câu trả lời thẳng về trùng băm"*). Đường thật đi một chiều LÊN:
        ///     <c>CSLevelReady.md5LevelData = FLevelManager.GetLevelDataMd5(level)</c> rồi mới
        ///     <c>Send()</c>; trong SDK không có chỗ nào gán field này từ bản tin của server.
        ///     <br/>Vì sao vẫn cần cạnh <see cref="currentLevelId"/>:
        ///     <br/>1. <b>Nối được hai kho</b>: server CÓ chuỗi băm này (client gửi lên trong
        ///     <c>cs_level_ready</c>/<c>cs_level_start</c>), nên backend index theo nó là kho analytics
        ///     join sang catalog level được. Chỉ là nó <b>không phải id do server cấp</b> — cần một id
        ///     thật của server thì đó là việc khác, không phải field này.
        ///     <br/>2. <b>Tách "đổi layout" với "đổi tuning"</b>: <see cref="currentLevelId"/> đổi khi
        ///     một trong hai đổi mà không nói cái nào; so hai id là biết. Đây là việc DUY NHẤT mà id
        ///     kia không làm được — cùng hàm băm nên nó <b>không</b> là phép kiểm độc lập về trùng băm.
        ///     <br/>⚠ KHÔNG log <c>levelData</c>/<c>levelParam</c> thô lên đây: đó là cùng một chuỗi
        ///     byte lặp lại trên mọi ván của mọi user (~1–3 KB × hàng chục dòng/user/ngày). Nội dung
        ///     nằm ở catalog, dòng log chỉ mang ID.
        /// </summary>
        [FKey(RemoveIfNull = true)] [CanBeNull]
        public string levelDataId;

        [NotNull] public string difficulty = UNKNOWN;

        // elo KHÔNG còn ở đây: nó tả NGƯỜI CHƠI chứ không tả lượt chơi (luật chủ thể §H6), và
        // là state quan-sát-được nên hợp đồng xếp vào nhóm "gửi khi ĐỔI" — nay đi bằng bản tin
        // f_sdk_user_label qua FalconBigDataController.Level.SetUserElo(...). Gửi kèm mọi level
        // event là nhân bản một sự thật của user lên stream dày nhất hệ.


        // movesLimit / timeLimitSec KHÔNG còn ở đây: chúng tả BẢN THIẾT KẾ MÀN chứ không tả lượt
        // chơi, mà luật container §H6 cấm thả attribute của chủ thể ngoại xuống flat. Nay chúng đi
        // trong bundle levelLabels dưới tên hợp đồng (FLevelLabelKey) — API nhận vào không đổi,
        // SDK tự bỏ đúng nhà. Xem EntityLifecycle-Design.md §5c.

        [FKey(RemoveIfNull = true)] [CanBeNull]
        public Dictionary<string, object> extraMeta = null;

        public bool comeBackAfterFirstPass = false;
        
        [FKey(Ignore = true)] public bool autoCheckComeBackAfterFirstPass = true;

        public override void CorrectValues()
        {
            base.CorrectValues();
            currentLevel = CheckNumberNonNegative(currentLevel, "currentLevel");
            difficulty = CheckNonBlank(difficulty, "difficulty");
        }

        public override Dictionary<string, object> ToDictionary()
        {
            var dictionary = base.ToDictionary();
            dictionary.Remove(nameof(extraMeta));
            dictionary.PutIfAbsent("status", Status);
            return dictionary.MergeExtras(extraMeta, this);
        }

        public virtual string LevelId => currentLevel + "_Difficulty_" + difficulty;
        
        public abstract LevelStatus Status { get; }
    }
}