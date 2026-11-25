using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RealtimeEvents
{
    // Also lets lifecycle tests exercise the processor independently of worker scheduling.
    internal interface IEventClient : IDisposable
    {
        string LastError { get; }
        long FirstOpenTimestamp { get; }
        Task Ready { get; }
        Task Completion { get; }
        string GetDeviceModel();
        string GetUserPseudoId();
        string GetCountry();
        void Track(IDictionary<string, object> values);
        void Track(params (string Key, object Value)[] values);
        void Track(string name, IDictionary<string, object> values);
        void Track(string name, params (string Key, object Value)[] values);
        void SetUserProperties(IDictionary<string, object> values);
        void SetUserProperties(params (string Key, object Value)[] values);
        void AddVariant(string value);
        void RemoveVariant(string value);
        void AddSegment(string value);
        void RemoveSegment(string value);
        void RefreshDeviceProperties();
        void Persist();
        Task PersistAsync();
        void Flush();
        void Tick();
        void SetSuspended(bool value);
        void CheckUsable(bool mainThread = true);
    }

    /// <summary>Nonblocking client. Create and pump on Unity's main thread. Await PersistAsync for durability.</summary>
    public sealed class EventClient : IEventClient
    {
        private sealed class Snapshot
        {
            internal JsonFields Properties;
            internal string Error, Model, UserId, Country = "__";
            internal long FirstOpen;
            internal int Buffered;
        }

        private readonly EventOptions options;
        private readonly string disabledPath;
        private volatile bool disabledSaved;
        private double disableRetryAt;
        private readonly SerialWorker worker;
        private readonly MainThreadTransport transport;
        private readonly Func<double> clock;
        private readonly Func<long> utcNow;
        private readonly Func<Func<IDictionary<string, object>>> captureDevice;
        private readonly int threadId = Thread.CurrentThread.ManagedThreadId;
        private EventProcessor processor; // Only the storage worker accesses the processor.
        private Exception initializationError;
        private Exception pendingFailure;
        private volatile Snapshot snapshot = new Snapshot();
        private volatile string operationError;
        private volatile Exception shutdownFailure;
        private volatile bool suspended;
        private int disposed;
        private int tickPending;
        private double workTime;
        private long workUtc;

        public Task Ready { get; }
        public Task Completion => worker.Completion;
        public string LastError => shutdownFailure?.Message ?? operationError ?? transport.LastError ?? snapshot.Error ??
            (transport.Disabled ? "SDK disabled after HTTP 410." : null);
        public long FirstOpenTimestamp => snapshot.FirstOpen;
        public bool IsSuspended => Volatile.Read(ref disposed) != 0 || suspended || transport.Disabled;
        public bool IsSending => transport.IsSending;
        public int BufferedEventCount => snapshot.Buffered;

        public EventClient(EventOptions options) : this(options, null, null, null, null, UnityEngine.Application.platform) { }

        internal EventClient(EventOptions options,
            Func<EventOptions, IEventTransport, Func<double>, Func<long>, EventProcessor> factory,
            IEventTransport nativeTransport = null, Func<double> clock = null, Func<long> utcNow = null,
            UnityEngine.RuntimePlatform runtimePlatform = (UnityEngine.RuntimePlatform)(-1))
        {
            this.options = (options ?? throw new ArgumentNullException(nameof(options))).Snapshot();
            this.clock = clock ?? (() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
            this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            // Bind relative paths now, before asynchronous startup can observe a changed working directory.
            this.options.StorageDirectory = Path.GetFullPath(
                this.options.StorageDirectory ?? EventProcessor.DefaultDirectory(this.options.Endpoint));
            disabledPath = Path.Combine(this.options.StorageDirectory, "sdk-disabled");
            // Capture Unity-only properties here. Android JNI and installation/storage work run on the worker.
            if (factory == null)
            {
                captureDevice = DeviceProperties.CreateReader;
                var readDevice = captureDevice();
                var readLegacyState = LegacyStateMigration.CreateReader();
                factory = (settings, network, time, utc) => new EventProcessor(settings, null, network, time, utc,
                    FirstOpen.ReadInstallTimeOnWorker, readDevice,
                    initializeCountryProperties: true, initializeUserPseudoId: true, initializeSessions: true,
                    initializeSdkProperties: true, runtimePlatform: runtimePlatform, readLegacyState: readLegacyState);
            }
            transport = new MainThreadTransport(nativeTransport ?? new UnityTransport(this.options.Endpoint, this.options.RequestTimeoutSeconds, apiKey: this.options.ApiKey),
                this.options.RequestTimeoutSeconds, this.clock);
            worker = new SerialWorker(this.options.MaxPendingOperations, this.options.MaxPendingBytes);
            var initialTime = this.clock();
            var initialUtc = this.utcNow();
            Ready = worker.Post(() =>
            {
                workTime = initialTime;
                workUtc = initialUtc;
                try
                {
                    disabledSaved = transport.Disabled = File.Exists(disabledPath);
                    if (!disabledSaved) processor = factory(this.options, transport, () => workTime, () => workUtc);
                    Publish();
                }
                catch (Exception error)
                {
                    initializationError = error;
                    operationError = error.Message;
                    // Failed processor initialization cannot recover through ticks. Drain rejected commands
                    // and end the worker so callers can safely initialize another client.
                    Close(initialTime, initialUtc);
                    throw;
                }
            });
        }

        public string GetDeviceModel() { CheckUsable(false); return snapshot.Model; }
        public string GetUserPseudoId() { CheckUsable(false); return snapshot.UserId; }
        public string GetCountry() { CheckUsable(false); return snapshot.Country; }

        public void Track(IDictionary<string, object> values) => TrackAsync(values);
        public void Track(params (string Key, object Value)[] values) => TrackAsync(values);
        public void Track(string name, IDictionary<string, object> values) => TrackAsync(name, values);
        public void Track(string name, params (string Key, object Value)[] values) => TrackAsync(name, values);

        /// <summary>Completes when accepted by the processor. Await PersistAsync afterwards to confirm a disk save.</summary>
        public Task TrackAsync(IDictionary<string, object> values) => CaptureEvent(null, values);
        public Task TrackAsync(params (string Key, object Value)[] values) => TrackAsync(JsonFields.Dictionary(values));
        public Task TrackAsync(string name, IDictionary<string, object> values)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (name.Length > options.MaxBatchBytes) throw new ArgumentException("Event name exceeds the size limit.", nameof(name));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Event name must not be blank.", nameof(name));
            return CaptureEvent(name, values);
        }
        public Task TrackAsync(string name, params (string Key, object Value)[] values) => TrackAsync(name, JsonFields.Dictionary(values));

        private Task CaptureEvent(string name, IDictionary<string, object> values)
        {
            CheckUsable(false);
            if (transport.Disabled) return Task.CompletedTask;
            var timestamp = utcNow();
            var time = clock();
            var callbackThread = Thread.CurrentThread.ManagedThreadId != threadId;
            var data = JsonFields.Capture(values, options.MaxBatchBytes, name);
            return Post(() => processor.Track(data, timestamp, callbackThread), DiskQueue.Utf8.GetByteCount(data.Json), time, timestamp);
        }

        public void SetUserProperties(IDictionary<string, object> values) => SetUserPropertiesAsync(values);
        public void SetUserProperties(params (string Key, object Value)[] values) => SetUserPropertiesAsync(values);
        public Task SetUserPropertiesAsync(params (string Key, object Value)[] values) => SetUserPropertiesAsync(JsonFields.Dictionary(values));
        /// <summary>Completes after the updated properties have been saved and published.</summary>
        public Task SetUserPropertiesAsync(IDictionary<string, object> values)
        {
            CheckUsable(false);
            if (transport.Disabled) return Task.CompletedTask;
            var data = JsonFields.Capture(values, options.MaxUserPropertiesBytes);
            return Post(() => processor.SetUserProperties(data), DiskQueue.Utf8.GetByteCount(data.Json));
        }

        /// <summary>Add a unique string to realtime_variants for future events. Thread-safe.</summary>
        public void AddVariant(string value) => AddVariantAsync(value);
        /// <summary>Remove a string from realtime_variants for future events. Thread-safe.</summary>
        public void RemoveVariant(string value) => RemoveVariantAsync(value);
        /// <summary>Add a unique string to realtime_segments for future events. Thread-safe.</summary>
        public void AddSegment(string value) => AddSegmentAsync(value);
        /// <summary>Remove a string from realtime_segments for future events. Thread-safe.</summary>
        public void RemoveSegment(string value) => RemoveSegmentAsync(value);

        /// <summary>Completes after the updated variants have been saved. Duplicate adds do nothing.</summary>
        public Task AddVariantAsync(string value) => UpdateArrayPropertyAsync("realtime_variants", value, true);
        /// <summary>Completes after the updated variants have been saved. Missing values do nothing.</summary>
        public Task RemoveVariantAsync(string value) => UpdateArrayPropertyAsync("realtime_variants", value, false);
        /// <summary>Completes after the updated segments have been saved. Duplicate adds do nothing.</summary>
        public Task AddSegmentAsync(string value) => UpdateArrayPropertyAsync("realtime_segments", value, true);
        /// <summary>Completes after the updated segments have been saved. Missing values do nothing.</summary>
        public Task RemoveSegmentAsync(string value) => UpdateArrayPropertyAsync("realtime_segments", value, false);

        private Task UpdateArrayPropertyAsync(string key, string value, bool add)
        {
            CheckUsable(false);
            if (transport.Disabled) return Task.CompletedTask;
            if (value == null) throw new ArgumentNullException(nameof(value));
            // Bound and account for the queued input before the worker reads the latest array.
            var data = JsonFields.Capture(new Dictionary<string, object> { [key] = new[] { value } }, options.MaxUserPropertiesBytes);
            return Post(() => processor.UpdateArrayProperty(key, value, add), DiskQueue.Utf8.GetByteCount(data.Json));
        }

        public void RefreshDeviceProperties()
        {
            CheckUsable();
            if (transport.Disabled) return;
            var read = captureDevice?.Invoke();
            Post(() =>
            {
                if (read != null) processor.SetDeviceReader(read);
                processor.RefreshDeviceProperties();
            });
        }

        public void Persist() => PersistAsync();
        /// <summary>Saves remaining accepted, unattempted events, subject to disk-quota eviction. Does not wait for HTTP.</summary>
        public Task PersistAsync() => Post(() => processor.Persist(), barrier: true);

        public void Flush()
        {
            CheckUsable();
            transport.Pump();
            if (transport.Disabled) { Tick(); return; }
            // An explicit flush must follow already submitted events, even if an older tick is queued.
            Post(() => processor.Flush(), reportSuccess: false);
        }

        public void Tick()
        {
            CheckUsable();
            if (disabledSaved) return;
            transport.Pump();
            if (Interlocked.Exchange(ref tickPending, 1) != 0) return;
            try
            {
                Post(() =>
                {
                    try
                    {
                        processor.Tick();
                    }
                    finally { Volatile.Write(ref tickPending, 0); }
                }, reportSuccess: false);
            }
            catch (IOException) { Volatile.Write(ref tickPending, 0); } // Retry next frame; never wait for capacity.
        }

        public void SetSuspended(bool value)
        {
            CheckUsable();
            if (transport.Disabled) { Tick(); return; }
            if (suspended == value) return;
            suspended = value;
            var time = clock();
            var utc = utcNow();
            var version = transport.Suspend();
            try
            {
                var read = value ? null : captureDevice?.Invoke();
                Post(() =>
                {
                    if (read != null) processor.SetDeviceReader(read);
                    try { processor.SetSuspended(value); }
                    catch (EventProcessor.FocusChangeLimitException error)
                    {
                        // The main thread already suspended networking for this transition.
                        // Close on the worker without touching Unity or waiting for queue capacity.
                        shutdownFailure = error;
                        Close(time, utc);
                        throw;
                    }
                    if (!value) transport.Resume(version);
                }, time: time, utc: utc, reportSuccess: false);
            }
            catch (Exception error)
            {
                // Lifecycle cannot wait for capacity. Fail closed and drain all accepted work.
                if (shutdownFailure == null) shutdownFailure = error;
                Dispose();
                throw;
            }
        }

        private Task Post(Action action, int cost = 0, double? time = null, long? utc = null, bool reportSuccess = true, bool barrier = false)
        {
            CheckUsable(false);
            var capturedTime = time ?? clock();
            var capturedUtc = utc ?? utcNow();
            try
            {
                return worker.Post(() =>
                {
                    workTime = capturedTime;
                    workUtc = capturedUtc;
                    try
                    {
                        if (initializationError != null) throw new InvalidOperationException("Event client initialization failed.", initializationError);
                        if (!DisableIfRequested(barrier)) action();
                        if (barrier)
                        {
                            var failure = pendingFailure;
                            pendingFailure = null;
                            if (failure != null) throw new InvalidOperationException("An earlier queued operation failed.", failure);
                        }
                        if (reportSuccess && pendingFailure == null) operationError = null;
                    }
                    catch (Exception error)
                    {
                        if (!barrier && pendingFailure == null) pendingFailure = error;
                        operationError = error.Message;
                        throw;
                    }
                    finally { if (transport.Disabled) Volatile.Write(ref tickPending, 0); Publish(); }
                }, cost);
            }
            catch (IOException error)
            {
                if (reportSuccess) operationError = error.Message;
                throw;
            }
        }

        private bool DisableIfRequested(bool force = false)
        {
            if (!transport.Disabled) return false;
            if (!disabledSaved && (force || workTime >= disableRetryAt))
            {
                // Back off failed background writes; explicit barriers and shutdown still try immediately.
                disableRetryAt = workTime + options.InitialRetrySeconds;
                try
                {
                    Directory.CreateDirectory(options.StorageDirectory);
                    File.WriteAllText(disabledPath, "");
                }
                // A failed marker must not prevent retirement or persistence of accepted events.
                finally { processor?.SetSuspended(true); }
                disabledSaved = true;
            }
            return true;
        }

        private void Publish()
        {
            if (processor == null) return;
            var previous = snapshot;
            var properties = processor.UserProperties;
            var sameProperties = properties == previous.Properties;
            var error = processor.LastError;
            var firstOpen = processor.FirstOpenTimestamp;
            var buffered = processor.BufferedEventCount;
            var country = processor.GetCountry();
            // Idle ticks should neither parse unchanged JSON nor allocate another snapshot.
            if (sameProperties && error == previous.Error && firstOpen == previous.FirstOpen &&
                buffered == previous.Buffered && country == previous.Country) return;
            snapshot = new Snapshot
            {
                Properties = properties, Error = error, FirstOpen = firstOpen, Buffered = buffered,
                Model = sameProperties ? previous.Model : processor.GetDeviceModel(),
                UserId = sameProperties ? previous.UserId : processor.GetUserPseudoId(), Country = country
            };
        }

        public void CheckUsable(bool mainThread = true)
        {
            if (mainThread) CheckMainThread();
            if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(EventClient));
        }

        private void CheckMainThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != threadId)
                throw new InvalidOperationException("Networking and lifecycle calls must use the Unity main thread.");
        }

        /// <summary>Stops networking immediately and schedules final persistence without joining the worker.</summary>
        public void Dispose()
        {
            if (Volatile.Read(ref disposed) != 0) return;
            CheckMainThread();
            transport.Suspend();
            Close(clock(), utcNow());
        }

        private void Close(double time, long utc)
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            suspended = true;
            transport.SuspendPending();
            worker.Close(() =>
            {
                workTime = time;
                workUtc = utc;
                try
                {
                    try { DisableIfRequested(true); }
                    finally { processor?.Dispose(); }
                    if (initializationError != null) throw new InvalidOperationException("Event client initialization failed.", initializationError);
                    if (pendingFailure != null) throw new InvalidOperationException("An earlier queued operation failed.", pendingFailure);
                    if (shutdownFailure != null) throw new InvalidOperationException("Event client closed after a lifecycle failure.", shutdownFailure);
                }
                catch (Exception error) { operationError = error.Message; throw; }
            });
        }

        public Task DisposeAsync() { Dispose(); return Completion; }
    }
}
