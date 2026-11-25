using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Databuckets.Analytics;
using UnityEngine;

namespace RealtimeEvents
{
    /// <summary>Synchronous delivery/session engine owned by EventClient's storage worker.</summary>
    internal sealed class EventProcessor : IDisposable
    {
        internal sealed class FocusChangeLimitException : IOException
        {
            internal FocusChangeLimitException() : base("Pending focus changes are full. Event client must close until storage recovers.") { }
        }

        private readonly EventOptions options;
        private readonly IEventStore store;
        private readonly IEventTransport transport;
        private readonly Func<double> clock;
        private readonly Func<long> utcNow;
        private readonly int threadId = Thread.CurrentThread.ManagedThreadId;
        private readonly object gate = new object();
        private readonly List<string> buffer = new List<string>();
        private long? engagementAt;
        // Timing follows accepted focus transitions; delivery suspends immediately, even during storage recovery.
        private bool engagementSuspended;
        private JsonFields userProperties;
        private readonly JsonFields sdkProperties;
        private SessionState session;
        private bool sessionDirty;
        private double sessionCheckpointAt;
        private string sessionError;
        private double sessionRetryAt;
        private double sessionRetryDelay;
        private sealed class FocusChange
        {
            internal long Timestamp, EngagementTime;
            internal bool Suspended, Startup;
        }
        private readonly Queue<FocusChange> focusChanges = new Queue<FocusChange>();
        private bool firstOpenPending;
        private readonly long? installationTimestamp;
        private readonly JsonFields installationProperties;
        private readonly bool firstOpenCached;
        private Func<IDictionary<string, object>> readDeviceProperties;
        private bool devicePropertiesRefreshPending;
        private double devicePropertiesRetryAt;
        private double devicePropertiesRetryDelay;
        private string devicePropertiesError;
        private string cachedCountryCode;
        private string pendingCountryCode;
        private bool countryCodeReadPending;
        private string countryCodeError;
        private double countryCodeRetryAt;
        private double countryCodeRetryDelay;
        private string lastError;
        private string persistenceError;
        private double persistenceRetryAt;
        private double persistenceRetryDelay;
        private int bufferBytes;
        private IEventRequest request;
        private string activeBody;
        private string activeId;
        private string reservedId;
        private enum ActiveState { Pending, Sending, Retry, Retire }
        private ActiveState activeState;
        private double flushAt;
        private double sendAt;
        private double workAt;
        private double retryDelay;
        private bool flushRequested;
        private bool disposed;

        public string LastError { get => lastError ?? persistenceError ?? sessionError ?? countryCodeError ?? devicePropertiesError; private set => lastError = value; }
        public bool IsSuspended { get; private set; }
        public bool IsSending => request != null;
        public int BufferedEventCount { get { lock (gate) return buffer.Count; } }
        // Immutable identity lets the owning worker reuse unchanged accessor snapshots.
        internal JsonFields UserProperties => userProperties;
        /// <summary>Cached first-open time in Unix epoch milliseconds; -1 if unavailable on iOS, or 0 while an uncached startup event awaits storage recovery.</summary>
        public long FirstOpenTimestamp { get; private set; }

        public EventProcessor(EventOptions options) : this(options, null, null, null, null, FirstOpen.ReadInstallTime,
            DeviceProperties.Read, initializeCountryProperties: true, initializeUserPseudoId: true, initializeSessions: true,
            initializeSdkProperties: true, runtimePlatform: Application.platform,
            readLegacyState: LegacyStateMigration.CreateReader()) { }

        internal EventProcessor(EventOptions options, IEventStore store, IEventTransport transport, Func<double> clock,
            Func<long> utcNow = null, Func<long?> readInstallTime = null,
            Func<IDictionary<string, object>> readDeviceProperties = null, bool initializeCountryProperties = false,
            bool initializeUserPseudoId = false, bool initializeSessions = false, bool initializeSdkProperties = false,
            RuntimePlatform runtimePlatform = (RuntimePlatform)(-1), Func<LegacyState> readLegacyState = null)
        {
            this.options = (options ?? throw new ArgumentNullException(nameof(options))).Snapshot();
            this.transport = transport ?? new UnityTransport(this.options.Endpoint, this.options.RequestTimeoutSeconds, apiKey: this.options.ApiKey);
            this.clock = clock ?? (() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
            this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            this.readDeviceProperties = readDeviceProperties;
            retryDelay = this.options.InitialRetrySeconds;
            persistenceRetryDelay = this.options.InitialRetrySeconds;
            devicePropertiesRetryDelay = this.options.InitialRetrySeconds;
            countryCodeRetryDelay = this.options.InitialRetrySeconds;
            sessionRetryDelay = this.options.InitialRetrySeconds;
            flushAt = this.clock() + this.options.FlushIntervalSeconds;
            LegacyState legacy = null;
            if (store == null)
            {
                var directory = this.options.StorageDirectory ?? DefaultDirectory(this.options.Endpoint);
                if (!Directory.Exists(directory) && readLegacyState != null)
                {
                    legacy = readLegacyState();
                    readLegacyState = null;
                }
                if (readInstallTime != null && !Directory.Exists(directory) && legacy?.FirstOpen == null)
                {
                    // A new queue can create its Documents parent on iOS. Read existing
                    // metadata first; a missing queue directory cannot contain a cache.
                    var installed = readInstallTime();
                    readInstallTime = () => installed;
                }
                store = new DiskQueue(directory, this.options.MaxDiskBytes, this.options.MaxDiskFiles, this.options.MaxUserPropertiesBytes);
            }
            this.store = store;
            try
            {
                userProperties = JsonFields.Load(this.store.ReadUserProperties());
                MigrateLanguageProperty();
                var cachedFirstOpen = readInstallTime == null ? null : this.store.ReadFirstOpenTimestamp();
                var cachedSession = initializeSessions ? this.store.ReadSession() : null;
                if (readLegacyState != null && (string.IsNullOrWhiteSpace(userProperties.GetString("user_pseudo_id")) ||
                    (readInstallTime != null && !cachedFirstOpen.HasValue) || (initializeSessions && cachedSession == null)))
                    legacy = readLegacyState();
                // All public clients save SDK metadata before first_open and session events.
                // Internal pipeline tests can omit automatic startup properties/events.
                if (initializeSdkProperties)
                {
                    var platform = PlatformProperties.Name(runtimePlatform);
                    sdkProperties = JsonFields.Capture(new Dictionary<string, object>
                    {
                        ["sdk_ver"] = SdkInfo.Version,
                        ["sdk_ver_num"] = SdkInfo.VersionNumber,
                        ["platform"] = platform,
                        ["app_platform"] = platform
                    });
                    SetUserProperties(sdkProperties);
                }
                // Save initial properties before identity and startup events. A failed save must fault
                // initialization, rather than emit events without the requested properties.
                if (this.options.CapturedInitialUserProperties != null)
                    SetUserProperties(this.options.CapturedInitialUserProperties);
                // Commit identity before accepting events, so a restart cannot change their user ID.
                if ((initializeUserPseudoId || initializeSessions) && string.IsNullOrWhiteSpace(userProperties.GetString("user_pseudo_id")))
                    SetUserProperties(("user_pseudo_id", legacy?.UserId ?? "db-" + Guid.NewGuid().ToString("D")));
                if (readInstallTime != null)
                {
                    if (!cachedFirstOpen.HasValue && legacy?.FirstOpen != null)
                    {
                        // A prior SDK already recorded this installation. Publish metadata directly,
                        // before any event can be queued, so crashes/restarts cannot emit first_open.
                        this.store.WriteFirstOpenTimestamp(legacy.FirstOpen.Value);
                        cachedFirstOpen = legacy.FirstOpen;
                    }
                    var cached = cachedFirstOpen;
                    firstOpenCached = cached.HasValue;
                    FirstOpenTimestamp = cached ?? 0;
                    installationTimestamp = cached ?? SelectFirstOpenTimestamp(readInstallTime);
                    installationProperties = JsonFields.Capture(new Dictionary<string, object>
                    {
                        ["install_day"] = installationTimestamp == -1 ? null : JsonFields.UtcDate(installationTimestamp.Value)
                    });
                }
                // Internal pipeline tests can omit automatic startup properties/events.
                if (initializeCountryProperties)
                {
                    // Defaults must apply even if saving optional device metadata fails.
                    userProperties = userProperties.Update(JsonFields.Capture(new Dictionary<string, object>
                    {
                        ["geo.country"] = "__", ["country"] = "__"
                    }));
                    countryCodeReadPending = true;
                    UpdateCountryCode(this.clock());
                }
                RefreshDeviceProperties();
                if (initializeSessions)
                {
                    session = SessionState.Load(cachedSession);
                    if (cachedSession == null && legacy?.SessionNumber > 0)
                    {
                        session = new SessionState
                        {
                            Id = userProperties.GetString("user_pseudo_id") + ":legacy", Number = legacy.SessionNumber
                        };
                        this.store.WriteSession(session.Json);
                    }
                    if (session.Number > 0) userProperties = userProperties.Update(session.Properties);
                    firstOpenPending = readInstallTime != null;
                    focusChanges.Enqueue(new FocusChange
                    {
                        Timestamp = this.utcNow(), EngagementTime = ReadEngagementClock(), Startup = true
                    });
                    TryUpdateSession(this.clock());
                }
                // Internal callers can test the event pipeline independently of startup events.
                else if (readInstallTime != null) InitializeFirstOpen();
            }
            catch
            {
                this.store.Dispose();
                throw;
            }
        }

        private void MigrateLanguageProperty()
        {
            const string previous = "device.language";
            const string current = "device.user_default_language";
            var migrated = userProperties.Remove(previous);
            if (ReferenceEquals(migrated, userProperties)) return;
            if (!migrated.ContainsKey(current))
                migrated = migrated.Update(JsonFields.Capture(new Dictionary<string, object>
                {
                    [current] = userProperties.GetString(previous)
                }));
            JsonFields.CheckSize(migrated.Json, options.MaxUserPropertiesBytes);
            // Publish only after saving the rename. Already queued events keep their original bytes.
            store.WriteUserProperties(migrated.Json);
            userProperties = migrated;
        }

        private void InitializeFirstOpen()
        {
            var timestamp = installationTimestamp.Value;
            if (firstOpenCached)
            {
                if (timestamp == -1) store.WriteFirstOpenTimestamp(-1);
                return;
            }
            if (timestamp == -1)
            {
                store.WriteFirstOpenTimestamp(-1);
                FirstOpenTimestamp = -1;
                return;
            }
            // Keep the cache and pending event in the same durable record. A restart cannot
            // observe a cached timestamp without its event, or enqueue the event twice.
            var data = userProperties.Update(JsonFields.Capture(new Dictionary<string, object>
            {
                ["event_name"] = "first_open", ["first_open_timestamp"] = timestamp
            }));
            var engagementNow = ReadEngagementClock();
            // Installation time may be historical; first_open never measures elapsed engagement.
            var line = data.EventLine(JsonFields.Capture(new Dictionary<string, object>()), timestamp, 0, timestamp);
            if (DiskQueue.Utf8.GetByteCount(line) > options.MaxBatchBytes)
                throw new ArgumentException("First-open event exceeds MaxBatchBytes.");
            store.WriteFirstOpen(line);
            // A delayed installation marker must not consume foreground time accumulated
            // since session_start or the latest real resume.
            if (!engagementAt.HasValue) CommitEngagement(engagementNow);
            FirstOpenTimestamp = timestamp;
        }

        private long SelectFirstOpenTimestamp(Func<long?> readInstallTime)
        {
            var now = utcNow();
            var installed = readInstallTime();
            return installed == -1 || (installed > 0 && installed < now - 2L * 24 * 60 * 60 * 1000)
                ? installed.Value : now;
        }

        internal static string DefaultDirectory(string endpoint)
        {
            using (var hash = SHA256.Create())
                return Path.Combine(Application.persistentDataPath, "RealtimeEvents", BitConverter.ToString(hash.ComputeHash(DiskQueue.Utf8.GetBytes(endpoint))).Replace("-", ""));
        }

        /// <summary>Snapshots an event. Background-thread or suspended calls persist synchronously before acceptance.</summary>
        public void Track(IDictionary<string, object> values)
        {
            var timestamp = utcNow();
            Track(values, timestamp);
        }

        private void Track(IDictionary<string, object> values, long timestamp) => Track(JsonFields.Capture(values, options.MaxBatchBytes), timestamp);

        internal void Track(JsonFields data, long timestamp, bool durable = false)
        {
            lock (gate)
            {
                CheckUsable(false);
                // Reject oversized payloads first. Rollover can reset engagement, so validate
                // its actual byte count only after the event's session has been established.
                ValidateEvent(data.EventLine(userProperties, timestamp, 0, installationTimestamp));
                var activityTimestamp = utcNow();
                if (session != null)
                {
                    ApplyFocusChanges();
                    UpdateSession(activityTimestamp, true);
                }
                var engagementNow = ReadEngagementClock();
                var line = data.EventLine(userProperties, timestamp, EngagementTime(engagementNow), installationTimestamp);
                var bytes = ValidateEvent(line);
                if (durable || IsSuspended || Thread.CurrentThread.ManagedThreadId != threadId)
                {
                    // Ad callbacks need not wait for Unity's next frame or lifecycle notification.
                    Persist();
                    store.Append(line);
                    RecordSessionActivity(activityTimestamp);
                    CommitEngagement(engagementNow);
                    return;
                }
                if (buffer.Count == options.MaxBatchEvents || bufferBytes + (long)bytes > options.MaxBatchBytes)
                    Persist();
                if (buffer.Count == 0) flushAt = clock() + options.FlushIntervalSeconds;
                buffer.Add(line);
                bufferBytes += bytes;
                RecordSessionActivity(activityTimestamp);
                CommitEngagement(engagementNow);
            }
        }

        public void Track(params (string Key, object Value)[] values)
        {
            var timestamp = utcNow();
            Track(JsonFields.Dictionary(values), timestamp);
        }

        /// <summary>Snapshots a named event using event_name. Saved user properties take precedence.</summary>
        public void Track(string eventName, IDictionary<string, object> values)
        {
            var timestamp = utcNow();
            Track(eventName, values, timestamp);
        }

        private void Track(string eventName, IDictionary<string, object> values, long timestamp)
        {
            if (eventName == null) throw new ArgumentNullException(nameof(eventName));
            if (string.IsNullOrWhiteSpace(eventName)) throw new ArgumentException("Event name must not be empty or whitespace.", nameof(eventName));
            Track(JsonFields.Capture(values, options.MaxBatchBytes, eventName), timestamp);
        }

        public void Track(string eventName, params (string Key, object Value)[] values)
        {
            var timestamp = utcNow();
            Track(eventName, JsonFields.Dictionary(values), timestamp);
        }

        /// <summary>Atomically add or overwrite properties for future events and save them across restarts. Thread-safe.</summary>
        public void SetUserProperties(IDictionary<string, object> values)
        {
            SetUserProperties(JsonFields.Capture(values, options.MaxUserPropertiesBytes));
        }

        internal void SetUserProperties(JsonFields update)
        {
            lock (gate)
            {
                CheckUsable(false);
                if (update.Members.Length == 0) return;
                var next = userProperties.Update(update);
                if (sdkProperties != null) next = next.Update(sdkProperties);
                if (installationProperties != null) next = next.Update(installationProperties);
                if (session != null) next = next.Update(session.Properties);
                var json = next.Json;
                JsonFields.CheckSize(json, options.MaxUserPropertiesBytes);
                if (ReferenceEquals(next, userProperties) || json == userProperties.Json) return;
                store.WriteUserProperties(json);
                userProperties = next; // Publish only after the entire update is durable.
            }
        }

        public void SetUserProperties(params (string Key, object Value)[] values) => SetUserProperties(JsonFields.Dictionary(values));

        internal void UpdateArrayProperty(string key, string value, bool add)
        {
            lock (gate)
            {
                CheckUsable(false);
                var values = userProperties.GetStringArray(key);
                if (add)
                {
                    if (values.Contains(value)) return;
                    values.Add(value);
                }
                else if (values.RemoveAll(item => string.Equals(item, value, StringComparison.Ordinal)) == 0) return;
                SetUserProperties(new Dictionary<string, object> { [key] = values.Count == 0 ? null : values });
            }
        }

        /// <summary>Cached device.mobile_model_name, or null when unavailable. Thread-safe.</summary>
        public string GetDeviceModel() => GetUserProperty("device.mobile_model_name");
        /// <summary>The cached user_pseudo_id. Thread-safe.</summary>
        public string GetUserPseudoId() => GetUserProperty("user_pseudo_id");
        /// <summary>The first resolved country code, or "__" until available. Thread-safe.</summary>
        public string GetCountry()
        {
            lock (gate)
            {
                CheckUsable(false);
                return cachedCountryCode ?? pendingCountryCode ?? "__";
            }
        }

        private string GetUserProperty(string key)
        {
            lock (gate)
            {
                CheckUsable(false);
                return userProperties.GetString(key);
            }
        }

        private int ValidateEvent(string line)
        {
            var bytes = DiskQueue.Utf8.GetByteCount(line);
            if (bytes > options.MaxBatchBytes) throw new ArgumentException("Event exceeds MaxBatchBytes.", "values");
            return bytes;
        }

        // Quantize absolute monotonic time so fractional milliseconds are not lost per event.
        // Called under gate (or during construction); serialization callbacks run before sampling.
        private long ReadEngagementClock() => Math.Max(engagementAt ?? long.MinValue, (long)Math.Floor(clock() * 1000));
        private long EngagementTime(long now) => engagementSuspended || !engagementAt.HasValue ? 0 : Math.Max(0, now - engagementAt.Value);
        private void CommitEngagement(long now) => engagementAt = now;

        private void SaveSession(SessionState next)
        {
            store.WriteSession(next.Json);
            session = next;
            sessionDirty = false;
            sessionCheckpointAt = clock() + options.FlushIntervalSeconds;
            ClearSessionError();
            userProperties = userProperties.Update(next.Properties);
        }

        private void ClearSessionError()
        {
            // Partial progress must not reset backoff while a boundary or focus event still needs storage.
            if (sessionDirty || focusChanges.Count > 0 || session.PendingEvent != null) return;
            sessionError = null;
            sessionRetryAt = 0;
            sessionRetryDelay = options.InitialRetrySeconds;
        }

        private void SaveSessionActivity()
        {
            if (sessionDirty) SaveSession(session);
        }

        private void RecordSessionActivity(long timestamp)
        {
            // A rejected payload must not extend the session, even when persistence ran first.
            if (session == null || timestamp <= session.LastActivity) return;
            var next = session.Copy();
            next.LastActivity = timestamp;
            session = next;
            sessionDirty = true;
        }

        private void DrainSessionEvent()
        {
            if (session.PendingEvent == null) return;
            store.EnsureSessionEvent(session.PendingEvent, session.PendingId);
            var next = session.Copy();
            next.PendingId = next.PendingEvent = null;
            SaveSession(next);
        }

        private void TransitionSession(SessionState next, string name, long timestamp, long engagementNow, bool resuming)
        {
            // SDK-owned names and session fields cannot be replaced by custom properties.
            var properties = userProperties.Update(next.Properties).Update(JsonFields.Capture(new Dictionary<string, object>
            {
                ["event_name"] = name
            }));
            // A new session starts at zero. An expired session recovered on resume cannot
            // claim foreground time from the new session. Delayed ticks stop at the timeout.
            var engagement = next.Active || resuming ? 0 :
                Math.Min(EngagementTime(engagementNow), SessionState.TimeoutMilliseconds);
            next.PendingEvent = properties.EventLine(JsonFields.Capture(new Dictionary<string, object>()), timestamp, engagement, installationTimestamp);
            ValidateEvent(next.PendingEvent);
            PersistEvents(); // Preserve batch age before reserving the newer boundary's ID.
            next.PendingId = store.ReserveId();
            SaveSession(next);
            // Saving the outbox accepts the event, even if publishing/clearing it needs a retry.
            CommitEngagement(engagementNow);
            DrainSessionEvent();
        }

        private void UpdateSession(long utcNow, bool activity, long? engagementTime = null, bool resuming = false)
        {
            var engagementNow = engagementTime ?? ReadEngagementClock();
            DrainSessionEvent();
            // Compare against a representable expiry; subtracting distant signed timestamps can overflow.
            if (session.Active && session.LastActivity <= long.MaxValue - SessionState.TimeoutMilliseconds &&
                utcNow >= session.LastActivity + SessionState.TimeoutMilliseconds)
            {
                var ended = session.Copy();
                ended.Active = false;
                TransitionSession(ended, "session_end", checked(session.LastActivity + SessionState.TimeoutMilliseconds), engagementNow, resuming);
            }
            if (activity && !session.Active)
            {
                var started = new SessionState
                {
                    Id = userProperties.GetString("user_pseudo_id") + ":" + utcNow.ToString(CultureInfo.InvariantCulture),
                    Number = checked(session.Number + 1), LastActivity = utcNow, Active = true
                };
                TransitionSession(started, "session_start", utcNow, engagementNow, resuming);
            }
            if (firstOpenPending)
            {
                InitializeFirstOpen();
                firstOpenPending = false;
            }
            ClearSessionError();
        }

        private void ApplyFocusChanges()
        {
            // Replay transitions in callback order, using captured clocks even after a storage
            // outage. Do not consume time or remove a transition until its event is accepted.
            while (focusChanges.Count > 0)
            {
                var change = focusChanges.Peek();
                UpdateSession(change.Timestamp, !change.Suspended, change.EngagementTime, !change.Suspended);
                if (!change.Startup)
                {
                    var properties = userProperties.Update(JsonFields.Capture(new Dictionary<string, object>
                    {
                        ["event_name"] = change.Suspended ? "app_focus_end" : "app_focus_start"
                    }));
                    var duration = change.Suspended && session.Active ? EngagementTime(change.EngagementTime) : 0;
                    var line = properties.EventLine(JsonFields.Capture(new Dictionary<string, object>()), change.Timestamp, duration, installationTimestamp);
                    var bytes = ValidateEvent(line);
                    if (buffer.Count == options.MaxBatchEvents || bufferBytes + (long)bytes > options.MaxBatchBytes)
                        PersistEvents();
                    if (buffer.Count == 0) flushAt = clock() + options.FlushIntervalSeconds;
                    buffer.Add(line);
                    bufferBytes += bytes;
                }
                if (session.Active) RecordSessionActivity(change.Timestamp);
                CommitEngagement(change.EngagementTime);
                engagementSuspended = change.Suspended;
                focusChanges.Dequeue();
                ClearSessionError();
            }
        }

        private void TryUpdateSession(double now)
        {
            if (session == null || now < sessionRetryAt) return;
            try
            {
                ApplyFocusChanges();
                UpdateSession(utcNow(), false);
                if (now >= sessionCheckpointAt) SaveSessionActivity();
            }
            // Conflicting committed records cannot recover through retries. Fail startup
            // and release ownership rather than reporting Ready with a permanently blocked queue.
            catch (Exception error) when (!(error is InvalidDataException))
            {
                sessionError = error.Message;
                sessionRetryAt = now + sessionRetryDelay;
                sessionRetryDelay = Math.Min(options.MaxRetrySeconds, sessionRetryDelay * 2);
            }
        }

        internal void SetDeviceReader(Func<IDictionary<string, object>> read) => readDeviceProperties = read;

        /// <summary>Refresh the captured device reader on the owning thread. Failures retry without stopping delivery.</summary>
        public void RefreshDeviceProperties()
        {
            lock (gate)
            {
                CheckUsable();
                if (readDeviceProperties == null && !installationTimestamp.HasValue) return;
                devicePropertiesRefreshPending = true;
                devicePropertiesRetryAt = 0;
                UpdateDeviceProperties(clock());
            }
        }

        private void UpdateDeviceProperties(double now)
        {
            if (!devicePropertiesRefreshPending || now < devicePropertiesRetryAt) return;
            try
            {
                // Save install_day even if collecting optional native device properties fails.
                if (installationProperties != null) SetUserProperties(installationProperties);
                if (readDeviceProperties != null) SetUserProperties(readDeviceProperties());
                devicePropertiesRefreshPending = false;
                devicePropertiesError = null;
                devicePropertiesRetryDelay = options.InitialRetrySeconds;
            }
            catch (Exception error)
            {
                // Optional metadata has its own retry clock. It must never delay sending,
                // acknowledging, or cleaning up an event request, even if it keeps failing.
                devicePropertiesError = error.Message;
                devicePropertiesRetryAt = now + devicePropertiesRetryDelay;
                devicePropertiesRetryDelay = Math.Min(options.MaxRetrySeconds, devicePropertiesRetryDelay * 2);
            }
        }

        /// <summary>Requests immediate sending. Existing retry backoff is still respected.</summary>
        public void Flush()
        {
            lock (gate)
            {
                CheckUsable();
                flushRequested = buffer.Count > 0;
                Tick();
            }
        }

        /// <summary>Advance networking once per frame. Network/storage errors are reported through LastError.</summary>
        public void Tick()
        {
            lock (gate)
            {
                CheckUsable();
                var now = clock();
                // Session timeouts do not depend on request completion or network retry backoff.
                TryUpdateSession(now);
                if (!IsSuspended)
                {
                    UpdateCountryCode(now);
                    UpdateDeviceProperties(now);
                }
                try
                {
                    if (now < workAt) return;
                    if (request != null && activeState == ActiveState.Sending)
                    {
                        bool done;
                        try { done = request.IsDone; }
                        catch
                        {
                            // An unreadable native request must not keep the queue stuck in Sending.
                            activeState = ActiveState.Retire;
                            throw;
                        }
                        if (!done && !IsSuspended) return;
                        CaptureOutcome();
                    }
                    if (activeState != ActiveState.Pending)
                    {
                        try { ResolveOutcome(now); }
                        finally { StopRequest(); }
                    }
                    // Suspension stops sending, but failed retirement/restoration still needs cleanup.
                    if (IsSuspended) return;

                    // Newer data takes priority over a pending body cached after a failed
                    // claim. Its disk file remains eligible unless quota eviction removed it.
                    if (activeId != null && (BufferReady(now) || store.Peek(session?.PendingId) != activeId))
                    {
                        activeId = activeBody = reservedId = null;
                    }
                    if (activeBody == null && now >= sendAt && BufferReady(now))
                    {
                        // The ready buffer is newer than the disk backlog. Send it directly
                        // so a full or unavailable disk cannot prevent its upload.
                        reservedId = store.ReserveId();
                        activeBody = string.Concat(buffer);
                        ClearBuffer();
                    }
                    if (now >= sendAt && activeBody == null)
                    {
                        activeId = store.Peek(session?.PendingId);
                        if (activeId != null) activeBody = store.Read(activeId);
                    }
                    if (now < sendAt) return;
                    if (activeBody != null)
                    {
                        // A persisted batch must leave the replayable queue BEFORE native networking starts.
                        if (activeId != null) store.BeginAttempt(activeId);
                        activeState = ActiveState.Sending;
                        try { request = transport.Send(activeBody); }
                        catch (Exception error)
                        {
                            LastError = error.Message;
                            activeState = error is RequestNotStartedException ? ActiveState.Retry : ActiveState.Retire;
                            ResolveOutcome(now);
                        }
                    }
                }
                catch (Exception error)
                {
                    // Keep the current disposition: a storage failure must never re-enable a retired batch.
                    LastError = error.Message;
                    workAt = now + retryDelay;
                    sendAt = workAt;
                    retryDelay = Math.Min(options.MaxRetrySeconds, retryDelay * 2);
                }
                finally
                {
                    // Slow HTTP, delivery storage failures, and their backoff must all allow
                    // overdue memory to be saved when it cannot start sending yet.
                    // A failed focus transition has already attempted persistence this tick.
                    if (!IsSuspended || now >= sessionRetryAt) TryPersistPending(now);
                }
            }
        }

        /// <summary>Synchronously save unattempted events. An in-flight attempt is never made replayable by persistence.</summary>
        public void Persist()
        {
            lock (gate)
            {
                CheckUsable(false);
                try { ApplyFocusChanges(); }
                finally { PersistEvents(); }
            }
        }

        private void PersistEvents()
        {
            try { SaveSessionActivity(); }
            finally
            {
                // A failed activity checkpoint must not prevent saving accepted event payloads.
                // An older retry can be unsavable while newer buffered data can still fit
                // by evicting older pending files. Always attempt both saves before returning.
                try { SaveActive(); }
                finally { SaveBuffer(); }
            }
            ResetPersistenceRetry();
        }

        /// <summary>Save and cancel on suspension; schedule a fresh attempt on resumption. Call on the owning thread.</summary>
        public void SetSuspended(bool suspended)
        {
            lock (gate)
            {
                CheckUsable();
                var changed = IsSuspended != suspended;
                if (changed && session != null)
                {
                    if (focusChanges.Count >= options.MaxPendingOperations)
                    {
                        // Storage failures must not turn repeated lifecycle callbacks into an
                        // unbounded history. Shutdown still drains the transitions already accepted.
                        IsSuspended = true;
                        throw new FocusChangeLimitException();
                    }
                    focusChanges.Enqueue(new FocusChange
                    {
                        Timestamp = utcNow(), EngagementTime = ReadEngagementClock(), Suspended = suspended
                    });
                }
                if (suspended)
                {
                    if (session == null) { engagementAt = null; engagementSuspended = true; }
                    IsSuspended = true;
                    try
                    {
                        try
                        {
                            if (request != null && activeState == ActiveState.Sending) CaptureOutcome();
                            ResolveOutcome(clock());
                        }
                        // Cleanup of an older attempt must not prevent saving newer, unattempted events.
                        finally { Persist(); }
                    }
                    finally { StopRequest(); }
                }
                else if (changed)
                {
                    if (session == null) { engagementAt = ReadEngagementClock(); engagementSuspended = false; }
                    IsSuspended = false;
                    workAt = sendAt = 0;
                    flushRequested = buffer.Count > 0;
                    // Refresh before creating session events so they use the resumed device's timezone.
                    RefreshDeviceProperties();
                    TryUpdateSession(clock());
                    flushRequested = buffer.Count > 0;
                }
            }
        }

        private void StopRequest()
        {
            var pendingRequest = request;
            request = null;
            pendingRequest?.Dispose();
        }

        private void CaptureOutcome()
        {
            // A running zero-byte request is still uncertain. The transport only permits a completed
            // zero-byte connection failure, or an explicit 503 (even if its upload completed).
            // If inspecting native request state itself fails, the outcome remains uncertain.
            activeState = ActiveState.Retire;
            if (request.CanRetry) activeState = ActiveState.Retry;
            var succeeded = request.Succeeded;
            LastError = succeeded ? null : request.Error;
            if (succeeded && request.IsDone && cachedCountryCode == null && pendingCountryCode == null)
            {
                try
                {
                    var code = UnityTransport.ValidCountryCode(request.CountryCode);
                    if (code != null)
                    {
                        // Keep the first valid response, including while its disk write is pending.
                        pendingCountryCode = code;
                        // A newly received code gets one immediate attempt, even after a startup read failure.
                        countryCodeRetryAt = 0;
                        countryCodeRetryDelay = options.InitialRetrySeconds;
                        UpdateCountryCode(clock());
                    }
                }
                catch (Exception error) { countryCodeError = error.Message; }
            }
        }

        private void UpdateCountryCode(double now)
        {
            if ((!countryCodeReadPending && pendingCountryCode == null) || now < countryCodeRetryAt) return;
            try
            {
                // A failed startup read must never let a later response overwrite an existing cache.
                var saved = UnityTransport.ValidCountryCode(store.ReadCountryCode());
                var code = saved ?? pendingCountryCode;
                if (code != null)
                {
                    userProperties = userProperties.Update(JsonFields.Capture(new Dictionary<string, object>
                    {
                        ["geo.country"] = code
                    }));
                    if (saved == null) store.WriteCountryCode(code);
                    cachedCountryCode = code;
                    pendingCountryCode = null;
                }
                countryCodeReadPending = false;
                countryCodeError = null;
                countryCodeRetryDelay = options.InitialRetrySeconds;
            }
            catch (Exception error)
            {
                // Optional cache I/O must not block acknowledgment or cause a resend.
                countryCodeError = error.Message;
                countryCodeRetryAt = now + countryCodeRetryDelay;
                countryCodeRetryDelay = Math.Min(options.MaxRetrySeconds, countryCodeRetryDelay * 2);
            }
        }

        private void ResolveOutcome(double now)
        {
            if (activeState == ActiveState.Retry)
            {
                if (activeId != null) store.Retry(activeId);
                activeState = ActiveState.Pending;
                SaveActive(); // If this fails, Pending still owns the original body in memory.
                sendAt = now + retryDelay;
                retryDelay = Math.Min(options.MaxRetrySeconds, retryDelay * 2);
            }
            else if (activeState == ActiveState.Retire)
            {
                if (activeId != null) store.Delete(activeId);
                retryDelay = options.InitialRetrySeconds;
                sendAt = 0;
            }
            else return;
            activeBody = null;
            activeId = null;
            reservedId = null;
            activeState = ActiveState.Pending;
        }

        private void SaveActive()
        {
            // A retry keeps its own batch ID and body; never enqueue it again or merge it with the buffer.
            if (activeState == ActiveState.Pending && activeBody != null && activeId == null) activeId = store.Append(activeBody, reservedId);
        }

        private void SaveBuffer()
        {
            if (buffer.Count == 0) return;
            store.Append(string.Concat(buffer));
            ClearBuffer();
        }

        private void TryPersistPending(double now)
        {
            // Delivery can drain a previously unsavable retry without another buffer save.
            if (buffer.Count == 0 && activeBody == null && !sessionDirty)
            {
                ResetPersistenceRetry();
                return;
            }
            if (now < persistenceRetryAt || (!IsSuspended && !BufferReady(now))) return;
            try
            {
                if (IsSuspended) PersistEvents();
                else SaveBuffer();
            }
            catch (Exception error)
            {
                // Saving newer memory must not delay acknowledging or draining older disk batches.
                persistenceError = error.Message;
                persistenceRetryAt = now + persistenceRetryDelay;
                persistenceRetryDelay = Math.Min(options.MaxRetrySeconds, persistenceRetryDelay * 2);
            }
        }

        private void ResetPersistenceRetry()
        {
            persistenceError = null;
            persistenceRetryAt = 0;
            persistenceRetryDelay = options.InitialRetrySeconds;
        }

        private bool BufferReady(double now) => buffer.Count > 0 && (flushRequested ||
            buffer.Count >= options.MaxBatchEvents || bufferBytes >= options.MaxBatchBytes || now >= flushAt);

        private void ClearBuffer()
        {
            buffer.Clear();
            bufferBytes = 0;
            flushRequested = false;
            ResetPersistenceRetry();
        }

        internal void CheckUsable(bool mainThread = true)
        {
            if (mainThread && Thread.CurrentThread.ManagedThreadId != threadId)
                throw new InvalidOperationException("Networking and lifecycle calls must use the thread that created the processor.");
            if (disposed) throw new ObjectDisposedException(nameof(EventProcessor));
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                CheckUsable();
                try { SetSuspended(true); }
                finally
                {
                    disposed = true;
                    try { StopRequest(); }
                    finally { store.Dispose(); }
                }
            }
        }
    }
}
