using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace RealtimeEvents
{
    /// <summary>Owns a client across scene changes and pumps it automatically.</summary>
    public sealed class EventTracker : MonoBehaviour
    {
        private IEventClient client;
        private bool paused;
        private bool focused = true;
        private bool quitting;
        private bool stopped;
        internal Func<bool> ReadFocus = () => Application.isFocused;
        public string LastError => client?.LastError;
        public long FirstOpenTimestamp => client.FirstOpenTimestamp;
        public Task Ready => client.Ready;
        public Task Completion => client.Completion;
        public string GetDeviceModel() => client.GetDeviceModel();
        public string GetUserPseudoId() => client.GetUserPseudoId();
        public string GetCountry() => client.GetCountry();

        public static EventTracker Create(EventOptions options)
        {
            var client = new EventClient(options);
            var host = new GameObject("Realtime Events");
            DontDestroyOnLoad(host);
            var tracker = host.AddComponent<EventTracker>();
            tracker.client = client;
            tracker.focused = tracker.ReadFocus();
            tracker.ApplyState();
            return tracker;
        }

        public void Track(IDictionary<string, object> values) => client.Track(values);
        public void Track(params (string Key, object Value)[] values) => client.Track(values);
        /// <summary>Track a named event using event_name, with optional event fields.</summary>
        public void Track(string eventName, IDictionary<string, object> values) => client.Track(eventName, values);
        public void Track(string eventName, params (string Key, object Value)[] values) => client.Track(eventName, values);
        /// <summary>Queue a persistent-property update for future events. Thread-safe.</summary>
        public void SetUserProperties(IDictionary<string, object> values) => client.SetUserProperties(values);
        public void SetUserProperties(params (string Key, object Value)[] values) => client.SetUserProperties(values);
        /// <summary>Add a unique string to realtime_variants for future events. Thread-safe.</summary>
        public void AddVariant(string value) => client.AddVariant(value);
        /// <summary>Remove a string from realtime_variants for future events. Thread-safe.</summary>
        public void RemoveVariant(string value) => client.RemoveVariant(value);
        /// <summary>Add a unique string to realtime_segments for future events. Thread-safe.</summary>
        public void AddSegment(string value) => client.AddSegment(value);
        /// <summary>Remove a string from realtime_segments for future events. Thread-safe.</summary>
        public void RemoveSegment(string value) => client.RemoveSegment(value);
        /// <summary>Refresh automatic mobile properties. Call on Unity's main thread.</summary>
        public void RefreshDeviceProperties() => client.RefreshDeviceProperties();
        /// <summary>Schedule persistence. Await PersistAsync before a controlled external handoff. Thread-safe.</summary>
        public void Persist() => client.Persist();
        public Task PersistAsync() => client.PersistAsync();
        public void Flush()
        {
            // Reject worker calls before reading Unity state.
            client.CheckUsable();
            RefreshFocus();
            client.Flush();
        }

        private void Update()
        {
            if (stopped) return;
            try
            {
                RefreshFocus();
                client?.Tick();
            }
            catch (ObjectDisposedException) { stopped = true; }
        }

        private void RefreshFocus()
        {
            // Reconcile missed focus callbacks before sending; pause/disable/quit still take priority.
            var currentFocus = ReadFocus();
            if (focused != currentFocus) OnApplicationFocus(currentFocus);
        }

        private void OnApplicationPause(bool paused)
        {
            this.paused = paused;
            ApplyState();
        }

        private void OnApplicationFocus(bool focused)
        {
            this.focused = focused;
            ApplyState();
        }

        private void OnApplicationQuit()
        {
            quitting = true;
            ApplyState();
        }

        private void OnEnable() => ApplyState();
        private void OnDisable() => ApplyState();

        private void ApplyState()
        {
            if (stopped) return;
            try { client?.SetSuspended(paused || !focused || !isActiveAndEnabled || quitting); }
            catch (ObjectDisposedException) { stopped = true; }
            catch (Exception error) { Debug.LogException(error, this); }
        }

        private void OnDestroy()
        {
            try { client?.Dispose(); }
            catch (Exception error) { Debug.LogException(error, this); }
        }
    }
}
