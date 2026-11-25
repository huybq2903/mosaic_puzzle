using System;
using System.Collections.Generic;

namespace RealtimeEvents
{
    /// <summary>Settings are copied when the client is created.</summary>
    public sealed class EventOptions
    {
        public string Endpoint { get; set; }
        public string ApiKey { get; set; }
        public string StorageDirectory { get; set; }
        /// <summary>Properties merged with saved values before startup events. Captured when the client is created.</summary>
        public IDictionary<string, object> InitialUserProperties { get; set; }
        internal JsonFields CapturedInitialUserProperties { get; private set; }
        public int MaxBatchEvents { get; set; } = 100;
        public int MaxBatchBytes { get; set; } = 256 * 1024;
        public long MaxDiskBytes { get; set; } = 64 * 1024 * 1024;
        public int MaxDiskFiles { get; set; } = 4096;
        public int MaxUserPropertiesBytes { get; set; } = 64 * 1024;
        public int MaxPendingOperations { get; set; } = 1024;
        public int MaxPendingBytes { get; set; } = 4 * 1024 * 1024;
        public double FlushIntervalSeconds { get; set; } = 1;
        public double InitialRetrySeconds { get; set; } = 2;
        public double MaxRetrySeconds { get; set; } = 60;
        public int RequestTimeoutSeconds { get; set; } = 15;

        internal EventOptions Snapshot()
        {
            var copy = (EventOptions)MemberwiseClone();
            // Validate the values we return, even if the caller changes the original during validation.
            if (!Uri.TryCreate(copy.Endpoint, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host) ||
                uri.AbsolutePath != "/push" || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0)
                throw new ArgumentException("Endpoint must be an absolute HTTPS URL ending in /push.", nameof(Endpoint));
            if (copy.ApiKey != null)
            {
                if (string.IsNullOrWhiteSpace(copy.ApiKey))
                    throw new ArgumentException("API key must not be blank.", nameof(ApiKey));
                foreach (var character in copy.ApiKey)
                    if (character < ' ' || character > '~')
                        throw new ArgumentException("API key must contain only printable ASCII characters.", nameof(ApiKey));
            }
            if (copy.MaxBatchEvents < 1 || copy.MaxBatchBytes < 3 || copy.MaxBatchBytes > JsonFields.MaximumBytes || copy.MaxDiskBytes < copy.MaxBatchBytes ||
                copy.MaxDiskFiles < 1 || copy.MaxDiskFiles > 65536 || copy.MaxUserPropertiesBytes < 2 || copy.MaxUserPropertiesBytes > JsonFields.MaximumBytes ||
                copy.MaxPendingOperations < 1 || copy.MaxPendingBytes < copy.MaxBatchBytes ||
                copy.RequestTimeoutSeconds < 1 || !Positive(copy.FlushIntervalSeconds) ||
                !Positive(copy.InitialRetrySeconds) || !Positive(copy.MaxRetrySeconds) || copy.MaxRetrySeconds < copy.InitialRetrySeconds)
                throw new ArgumentOutOfRangeException(nameof(EventOptions), "Batch, disk, timeout and retry limits must be positive and consistent.");
            if (copy.InitialUserProperties != null)
            {
                copy.CapturedInitialUserProperties = JsonFields.Capture(copy.InitialUserProperties, copy.MaxUserPropertiesBytes);
                // Further snapshots on the worker reuse immutable JSON, without invoking caller code.
                copy.InitialUserProperties = null;
            }
            return copy;
        }

        private static bool Positive(double value) => value > 0 && !double.IsInfinity(value);
    }
}
