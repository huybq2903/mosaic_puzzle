using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RealtimeEvents;
using UnityEngine;

namespace Databuckets.Analytics
{
    /// <summary>Application-wide analytics with automatic Unity lifecycle handling.</summary>
    public static class DatabucketsTracker
    {
        private static volatile EventTracker tracker;
        private static string initializedEndpoint;
        private static string initializedApiKey;

        // Use a managed null check: Record and the cached getters also run on callback threads.
        private static EventTracker Current => tracker ??
            throw new InvalidOperationException("Call DatabucketsTracker.Init before using analytics.");

        /// <summary>Initialize once on Unity's main thread. Repeating the same configuration is harmless.</summary>
        public static void Init(string endpoint, string apiKey) => Init(endpoint, apiKey, null);

        /// <summary>Initialize with persistent properties applied before startup events. Ignored when reusing an active tracker.</summary>
        public static void Init(string endpoint, string apiKey, IDictionary<string, object> initialUserProperties)
        {
            if (apiKey == null) throw new ArgumentNullException(nameof(apiKey));
            var options = new EventOptions { Endpoint = endpoint, ApiKey = apiKey }.Snapshot();
            var current = tracker;
            if (current != null && !current.Ready.IsFaulted && !current.Completion.IsCompleted)
            {
                if (initializedEndpoint != endpoint || initializedApiKey != apiKey)
                    throw new InvalidOperationException("DatabucketsTracker is already initialized with a different endpoint or API key.");
                return;
            }
            if (!ReferenceEquals(current, null) && !current.Completion.IsCompleted)
                throw new InvalidOperationException("Wait for DatabucketsTracker.Completion before initializing again.");

            if (current != null) UnityEngine.Object.Destroy(current.gameObject);
            options.InitialUserProperties = initialUserProperties;
            var created = EventTracker.Create(options);
            created.gameObject.name = "Databuckets Analytics";
            initializedEndpoint = endpoint;
            initializedApiKey = apiKey;
            tracker = created;
        }

        /// <summary>Record a named event. Null or omitted fields mean no additional fields. Thread-safe.</summary>
        public static void Record(string eventName, IDictionary<string, object> values = null) =>
            Current.Track(eventName, values ?? new Dictionary<string, object>());

        public static string LastError => Current.LastError;
        public static long FirstOpenTimestamp => Current.FirstOpenTimestamp;
        public static Task Ready => Current.Ready;
        public static Task Completion => Current.Completion;
        public static string GetDeviceModel() => Current.GetDeviceModel();
        public static string GetUserPseudoId() => Current.GetUserPseudoId();
        public static string GetCountry() => Current.GetCountry();
        public static void SetUserProperties(IDictionary<string, object> values) => Current.SetUserProperties(values);
        public static void SetUserProperties(params (string Key, object Value)[] values) => Current.SetUserProperties(values);
        public static void SetVariants(params string[] values) => Current.SetUserProperties(("realtime_variants", values));
        public static void ResetVariants() => Current.SetUserProperties(("realtime_variants", null));
        public static void SetSegments(params string[] values) => Current.SetUserProperties(("realtime_segments", values));
        public static void ResetSegments() => Current.SetUserProperties(("realtime_segments", null));
        public static void SetPersonalizations(params string[] values) => Current.SetUserProperties(("realtime_personalizations", values));
        public static void ResetPersonalizations() => Current.SetUserProperties(("realtime_personalizations", null));
        /// <summary>Refresh mobile properties on Unity's main thread.</summary>
        public static void RefreshDeviceProperties() => Current.RefreshDeviceProperties();
        public static void Persist() => Current.Persist();
        /// <summary>Confirm persistence of remaining unattempted events, subject to disk-quota eviction. Thread-safe.</summary>
        public static Task PersistAsync() => Current.PersistAsync();
        /// <summary>Request a send on Unity's main thread, respecting retry backoff.</summary>
        public static void Flush() => Current.Flush();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            tracker = null;
            initializedEndpoint = null;
            initializedApiKey = null;
        }
    }
}
