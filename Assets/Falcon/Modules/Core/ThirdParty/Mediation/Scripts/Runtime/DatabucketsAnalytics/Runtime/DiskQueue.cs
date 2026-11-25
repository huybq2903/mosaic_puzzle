using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace RealtimeEvents
{
    internal interface IEventStore : IDisposable
    {
        string ReserveId();
        string Append(string ndjson, string id = null);
        string Peek(string excludedId = null);
        string Read(string id);
        void BeginAttempt(string id);
        void Retry(string id);
        void Delete(string id);
        string ReadUserProperties();
        void WriteUserProperties(string json);
        string ReadCountryCode();
        void WriteCountryCode(string code);
        long? ReadFirstOpenTimestamp();
        void WriteFirstOpen(string line);
        void WriteFirstOpenTimestamp(long timestamp);
        string ReadSession();
        void WriteSession(string json);
        void EnsureSessionEvent(string line, string id);
    }

    /// <summary>One immutable file per persisted batch. Sending claims it; completion or retirement deletes it.</summary>
    internal sealed class DiskQueue : IEventStore
    {
        internal static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        // IL2CPP's POSIX sharing check and handle registration are separate operations.
        // Serialize SDK lease opens/closes; recovery and all payload I/O stay outside this gate.
        private static readonly object leaseGate = new object();
        private const string FirstOpenId = "00000000000000000000.ndjson";
        private readonly string directory;
        private readonly long maxBytes;
        private readonly int maxFiles;
        private readonly int maxBatchBytes;
        private readonly int maxPropertiesBytes;
        private readonly FileStream lease;
        private readonly FileStream processLease;
        private readonly SortedList<string, long> files = new SortedList<string, long>(StringComparer.Ordinal);
        private long usedBytes;
        private long nextId;

        internal DiskQueue(string directory, long maxBytes, int maxFiles = 4096,
            int maxPropertiesBytes = JsonFields.MaximumBytes)
        {
            this.directory = directory = Path.GetFullPath(directory);
            this.maxBytes = maxBytes;
            this.maxFiles = maxFiles;
            // A lower current batch setting must still replay records written by an older configuration.
            this.maxBatchBytes = JsonFields.MaximumBytes;
            this.maxPropertiesBytes = maxPropertiesBytes;
            Directory.CreateDirectory(directory);
            try
            {
                lock (leaseGate)
                {
                    lease = new FileStream(Path.Combine(directory, "queue.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    // Use a separate file for the OS lock: a rejected same-process open of
                    // queue.lock must not release the owner's POSIX locks.
                    processLease = new FileStream(Path.Combine(directory, "queue.process.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    processLease.Lock(0, 1);
                }
                var pendingFirstOpen = File.Exists(Pending(FirstOpenId));
                var sendingFirstOpen = File.Exists(Sending(FirstOpenId));
                if ((pendingFirstOpen && sendingFirstOpen) ||
                    ((pendingFirstOpen || sendingFirstOpen) && File.Exists(FirstOpenPath)))
                    throw new InvalidDataException("Conflicting cached first-open records. Files were preserved.");
                // An uncertain first-open send is retired just like other batches, but its
                // record stays as the timestamp cache. Pending records survive for replay.
                if (sendingFirstOpen) File.Move(Sending(FirstOpenId), FirstOpenPath);
                if (pendingFirstOpen)
                {
                    var length = new FileInfo(Pending(FirstOpenId)).Length;
                    files.Add(FirstOpenId, length);
                    usedBytes += length;
                }
                // Keep an ordered index without allocating/sorting all recovery paths.
                var bytes = new byte[4096];
                var characters = new char[Utf8.GetMaxCharCount(bytes.Length)];
                var decoder = Utf8.GetDecoder();
                foreach (var path in Directory.EnumerateFiles(directory, "*.ndjson"))
                {
                    var name = Path.GetFileName(path);
                    var pendingBatch = name.StartsWith("pending-", StringComparison.Ordinal);
                    var sendingBatch = name.StartsWith("sending-", StringComparison.Ordinal);
                    // Check either copy before trimming or deleting it; directory enumeration is unordered.
                    if ((pendingBatch || sendingBatch) && File.Exists(Path.Combine(directory,
                        (pendingBatch ? "sending-" : "pending-") + name.Substring("pending-".Length))))
                        throw new InvalidDataException("Conflicting pending and sending batch records. Files were preserved.");
                    if (sendingBatch || name.StartsWith("writing-", StringComparison.Ordinal))
                    {
                        // Neither an uncertain send nor an uncommitted write is eligible for replay.
                        File.Delete(path);
                        continue;
                    }
                    if (!pendingBatch)
                    {
                        // Old queue files contain no send history. Preserve them outside automatic replay.
                        var legacy = Path.Combine(directory, "legacy");
                        Directory.CreateDirectory(legacy);
                        File.Move(path, Path.Combine(legacy, Guid.NewGuid().ToString("N") + "-" + name));
                        continue;
                    }
                    var id = name.Substring("pending-".Length);
                    nextId = Math.Max(nextId, ParseBatchId(id));
                    // A killed process can leave an incomplete final event. Never join it to a later event.
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
                    using (var reader = new BinaryReader(stream))
                    {
                        var end = stream.Length;
                        if (end > this.maxBatchBytes) throw new IOException("Queued batch exceeds the supported size limit.");
                        while (end > 0)
                        {
                            // Bounded reads avoid a seek/read for every byte of a large interrupted event.
                            var start = Math.Max(0, end - 4096);
                            stream.Position = start;
                            var newline = Array.LastIndexOf(reader.ReadBytes((int)(end - start)), (byte)'\n');
                            if (newline >= 0)
                            {
                                end = start + newline + 1;
                                break;
                            }
                            end = start;
                        }
                        if (end != stream.Length) stream.SetLength(end);
                        // Detect permanent payload corruption before startup succeeds, rather than
                        // retrying the same undecodable batch forever at the head of the queue.
                        stream.Position = 0;
                        // Stream validation so a large backlog does not allocate one batch-sized array per file.
                        decoder.Reset();
                        int read;
                        while ((read = stream.Read(bytes, 0, bytes.Length)) > 0)
                            decoder.GetChars(bytes, 0, read, characters, 0, stream.Position == end);
                    }
                    var length = new FileInfo(path).Length;
                    if (length == 0) File.Delete(path);
                    else
                    {
                        // Discardable interrupted files do not consume the recovered queue's quota.
                        if (files.Count >= maxFiles)
                            throw new IOException("Existing queue exceeds MaxDiskFiles. Increase the limit to recover it; files were preserved.");
                        files.Add(id, length);
                        usedBytes += length;
                    }
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        // Reserving an ID does not write to disk; it preserves order if a memory batch needs saving later.
        public string ReserveId() => checked(++nextId).ToString("D20", CultureInfo.InvariantCulture) + ".ndjson";

        public string Append(string ndjson, string id = null)
        {
            // Append one new batch to the queue, never to an existing batch's file.
            var bytes = Utf8.GetBytes(ndjson);
            if (bytes.LongLength > maxBytes || (id != null && files.ContainsKey(id)))
                throw new IOException("Batch exceeds the disk quota or already exists.");
            if (files.Count >= maxFiles || bytes.LongLength > maxBytes - usedBytes)
            {
                // Prove enough eligible space exists before deleting accepted events.
                // Protect in-flight files and newer data from an older reserved retry.
                // First-open initialization keeps its startup priority despite its zero ID.
                var evict = new List<string>();
                var remainingBytes = usedBytes;
                foreach (var batch in files)
                {
                    if ((id != null && id != FirstOpenId && string.CompareOrdinal(batch.Key, id) >= 0) ||
                        File.Exists(Sending(batch.Key))) continue;
                    evict.Add(batch.Key);
                    remainingBytes -= batch.Value;
                    if (files.Count - evict.Count < maxFiles && bytes.LongLength <= maxBytes - remainingBytes) break;
                }
                if (files.Count - evict.Count >= maxFiles || bytes.LongLength > maxBytes - remainingBytes)
                    throw new IOException("Event disk queue is full. No older pending batch can be removed.");
                foreach (var oldest in evict) Delete(oldest); // Retains the first-open timestamp cache.
            }
            id = id ?? ReserveId();
            var path = Path.Combine(directory, "writing-" + id);
            WriteNew(bytes, path, Pending(id));
            files.Add(id, bytes.LongLength);
            usedBytes += bytes.LongLength;
            return id;
        }

        private static void WriteNew(byte[] bytes, string path, string destination)
        {
            try
            {
                // Replace any uncommitted remainder from an earlier failed save of this same batch.
                using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                // Publish only after the complete write succeeds. A failed cleanup leaves no replayable copy.
                File.Move(path, destination);
            }
            catch
            {
                // Any unremoved staging file is discarded on restart.
                File.Delete(path);
                throw;
            }
        }

        public string Peek(string excludedId = null)
        {
            // Select the newest batch without scanning the backlog on every tick.
            // An uncleared session outbox must not send, but other files can still drain.
            var index = files.Count - 1;
            if (index >= 0 && files.Keys[index] == excludedId) index--;
            return index < 0 ? null : files.Keys[index];
        }
        private string FirstOpenPath => Path.Combine(directory, "first-open.json");
        private string Pending(string id) => Path.Combine(directory, id == FirstOpenId ? "first-open-pending.json" : "pending-" + id);
        private string Sending(string id) => Path.Combine(directory, id == FirstOpenId ? "first-open-sending.json" : "sending-" + id);
        public string Read(string id) => ReadBounded(Pending(id), maxBatchBytes);

        private static string ReadBounded(string path, int limit)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > limit) throw new IOException("Cached record exceeds the configured size limit.");
                using (var reader = new BinaryReader(stream)) return Utf8.GetString(reader.ReadBytes((int)stream.Length));
            }
        }

        public long? ReadFirstOpenTimestamp()
        {
            var path = files.ContainsKey(FirstOpenId) ? Pending(FirstOpenId) : FirstOpenPath;
            string json;
            try { json = ReadBounded(path, Math.Max(256, maxBatchBytes)); }
            catch (FileNotFoundException) { return null; }
            // This record is both the cache and a replayable event. Reject a damaged event
            // even if its first_open_timestamp survived, instead of sending invalid NDJSON.
            if (!json.EndsWith("\n", StringComparison.Ordinal))
                throw new IOException("Incomplete cached first-open event.");
            var data = JObject.Parse(JsonFields.Load(json.Substring(0, json.Length - 1)).Json);
            var timestamp = data["first_open_timestamp"];
            // Epoch milliseconds are signed: accept values that initialization can write
            // even when the device clock is at or before the Unix epoch.
            if (timestamp == null || timestamp.Type != JTokenType.Integer ||
                !JToken.DeepEquals(timestamp, data["_ts"]) ||
                data["event_name"]?.Type != JTokenType.String || (string)data["event_name"] != "first_open")
                throw new IOException("Invalid cached first-open event.");
            return timestamp.Value<long>();
        }

        public void WriteFirstOpen(string line) => Append(line, FirstOpenId);

        public void WriteFirstOpenTimestamp(long timestamp)
        {
            // Older SDK versions queued -1. Retire that record before it can be sent.
            if (files.ContainsKey(FirstOpenId)) Delete(FirstOpenId);
            else if (!File.Exists(FirstOpenPath))
            {
                // Retain the cache format, but publish directly as metadata, outside the queue.
                var value = timestamp.ToString(CultureInfo.InvariantCulture);
                var bytes = Utf8.GetBytes("{\"event_name\":\"first_open\",\"first_open_timestamp\":" + value + ",\"_ts\":" + value + "}\n");
                WriteNew(bytes, Path.Combine(directory, "writing-" + FirstOpenId), FirstOpenPath);
            }
        }

        public string ReadUserProperties()
        {
            var path = Path.Combine(directory, "user-properties.json");
            try
            {
                // StreamReader's BOM detection can replace the supplied strict UTF-8 decoder.
                // Decode explicitly so corrupt bytes cannot silently change saved properties.
                var json = ReadBounded(path, maxPropertiesBytes);
                return json.Length > 0 && json[0] == '\uFEFF' ? json.Substring(1) : json;
            }
            catch (FileNotFoundException) { return "{}"; }
        }

        public string ReadCountryCode()
        {
            try
            {
                using (var stream = File.OpenRead(Path.Combine(directory, "country-code.txt")))
                {
                    // Our cache is exactly two ASCII bytes. Never load an oversized/corrupt file.
                    if (stream.Length != 2) return null;
                    return UnityTransport.ValidCountryCode(new string(new[] { (char)stream.ReadByte(), (char)stream.ReadByte() }));
                }
            }
            catch (FileNotFoundException) { return null; }
        }

        public void WriteCountryCode(string code) => WriteMetadata("country-code.txt", code);
        public void WriteUserProperties(string json) => WriteMetadata("user-properties.json", json);

        public string ReadSession()
        {
            try { return ReadBounded(Path.Combine(directory, "session.json"), maxBatchBytes * 6 + 4096); }
            catch (FileNotFoundException) { return null; }
        }

        public void WriteSession(string json) => WriteMetadata("session.json", json);

        internal static long ParseBatchId(string id)
        {
            if (!long.TryParse(Path.GetFileNameWithoutExtension(id), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ||
                id != number.ToString("D20", CultureInfo.InvariantCulture) + ".ndjson" || number <= 0)
                throw new IOException("Invalid cached event batch ID.");
            return number;
        }

        public void EnsureSessionEvent(string line, string id)
        {
            nextId = Math.Max(nextId, ParseBatchId(id));
            // A crash may occur after publishing the event but before clearing the session outbox.
            // No sending is allowed until that outbox has been cleared durably.
            if (files.ContainsKey(id))
            {
                if (Read(id) != line) throw new InvalidDataException("Session event conflicts with a queued batch.");
                return;
            }
            Append(line, id);
        }

        private void WriteMetadata(string filename, string value)
        {
            var path = Path.Combine(directory, filename);
            var staging = path + ".tmp";
            var bytes = Utf8.GetBytes(value);
            using (var stream = new FileStream(staging, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            // A kill or write failure leaves either the previous complete snapshot or the new one.
            // A leftover staging file is ignored and overwritten by the next update.
            if (File.Exists(path)) File.Replace(staging, path, null);
            else File.Move(staging, path);
        }

        public void BeginAttempt(string id) => File.Move(Pending(id), Sending(id));
        public void Retry(string id) => File.Move(Sending(id), Pending(id));

        public void Delete(string id)
        {
            if (id == FirstOpenId)
            {
                var path = File.Exists(Sending(id)) ? Sending(id) : Pending(id);
                if (File.Exists(path)) File.Move(path, FirstOpenPath);
            }
            else
            {
                File.Delete(Sending(id));
                File.Delete(Pending(id));
            }
            if (files.TryGetValue(id, out var length))
            {
                files.Remove(id);
                usedBytes -= length;
            }
        }

        public void Dispose()
        {
            lock (leaseGate)
            {
                try { processLease?.Dispose(); }
                finally { lease?.Dispose(); }
            }
        }
    }
}
