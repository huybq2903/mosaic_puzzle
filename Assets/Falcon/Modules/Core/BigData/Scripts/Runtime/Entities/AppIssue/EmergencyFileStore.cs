/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 */

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// File khẩn của đường gửi khẩn (hợp đồng đợt 3 S2 §0.4): mỗi lần sự cố một file trong thư mục riêng (không qua pool
    /// chung, không đọc-sửa-ghi — luồng canh và handler lỗi chết ghi cùng lúc không đè nhau). Lần mở sau đưa vào hàng đợi
    /// thường, giữ nguyên <c>uuid</c> (nằm trong <c>data</c>).
    /// <br/>Chỉ <c>System.IO</c> + Newtonsoft — chạy được ngoài main thread. Mọi lỗi IO nuốt: file khẩn là lưới an toàn.
    /// </summary>
    public class EmergencyFileStore
    {
        private const string PREFIX = "emergency_";
        private const string EXT = ".json";

        private readonly string _dir;

        public EmergencyFileStore(string dir)
        {
            _dir = dir;
        }

        /// <returns>Đường dẫn file đã ghi; null nếu không ghi được.</returns>
        public string Write(IReadOnlyList<DataWrapper> wrappers)
        {
            try
            {
                Directory.CreateDirectory(_dir);
                var records = new List<Record>(wrappers.Count);
                foreach (var wrapper in wrappers) records.Add(Record.From(wrapper));
                var path = Path.Combine(_dir, PREFIX + Guid.NewGuid().ToString("N") + EXT);
                var temp = path + ".tmp";
                File.WriteAllText(temp, JsonConvert.SerializeObject(records));
                File.Move(temp, path); // tên mới (Guid) — không có file đích để đè
                return path;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Mọi file khẩn còn lại (file hỏng bị xoá luôn — không đọc được thì giữ cũng vô ích).</summary>
        public List<(string path, List<DataWrapper> wrappers)> ReadAll()
        {
            var result = new List<(string, List<DataWrapper>)>();
            try
            {
                if (!Directory.Exists(_dir)) return result;
                foreach (var path in Directory.GetFiles(_dir, PREFIX + "*" + EXT))
                {
                    try
                    {
                        var records = JsonConvert.DeserializeObject<List<Record>>(File.ReadAllText(path));
                        var wrappers = new List<DataWrapper>();
                        if (records != null)
                            foreach (var record in records)
                                if (record?.data != null)
                                    wrappers.Add(record.ToWrapper());
                        result.Add((path, wrappers));
                    }
                    catch (Exception)
                    {
                        Delete(path);
                    }
                }
            }
            catch (Exception)
            {
                // thư mục không đọc được: lần sau thử lại
            }

            return result;
        }

        public void Delete(string path)
        {
            if (path == null) return;
            try
            {
                File.Delete(path);
            }
            catch (Exception)
            {
                // còn file thì lần mở sau gửi lại — trùng uuid, loader dedup trong 1 giờ (§0.4)
            }
        }

        /// <summary>
        /// Bản ghi trung gian: deserialize thẳng <see cref="DataWrapper"/> thì đi qua constructor có ghi log (và tên field
        /// <c>@event</c>) — tách ra cho chắc định dạng file không đổi theo lớp wrapper.
        /// </summary>
        [Serializable]
        private class Record
        {
            public string data;
            public long clientSendTime;
            public string @event;
            public string packageName;
            public string platform;

            public static Record From(DataWrapper wrapper) => new()
            {
                data = wrapper.data,
                clientSendTime = wrapper.clientSendTime,
                @event = wrapper.@event,
                packageName = wrapper.packageName,
                platform = wrapper.platform
            };

            public DataWrapper ToWrapper() => DataWrapper.Quiet(data, clientSendTime, @event, packageName, platform);
        }
    }
}
