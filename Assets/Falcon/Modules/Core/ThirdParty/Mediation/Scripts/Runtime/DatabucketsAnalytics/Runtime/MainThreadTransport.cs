using System;
using System.Diagnostics;

namespace RealtimeEvents
{
    // The processor sees immutable outcomes. Only Pump/Suspend touch native requests.
    internal sealed class MainThreadTransport : IEventTransport
    {
        private sealed class Outcome
        {
            internal bool Done, Success, Retry;
            internal long StatusCode;
            internal string Error, Country;
        }

        private sealed class Request : IEventRequest
        {
            internal readonly string Body;
            internal bool Started;
            internal volatile bool Disposed;
            internal volatile Outcome Result = new Outcome();
            internal Request(string body) { Body = body; }
            public bool IsDone => Result.Done;
            public bool Succeeded => Result.Success;
            public bool CanRetry => Result.Retry;
            public long StatusCode => Result.StatusCode;
            public string Error => Result.Error;
            public string CountryCode => Result.Country;
            public void Dispose() => Disposed = true;
            internal void CancelBeforeSend() => Result = new Outcome
            {
                Done = true, Retry = true, Error = "Request canceled before sending."
            };
        }

        private readonly object gate = new object();
        private readonly IEventTransport transport;
        private readonly Func<double> clock;
        private readonly int timeoutSeconds;
        private Request pending;
        private Request active;
        private IEventRequest native;
        private bool suspended;
        internal volatile bool Disabled;
        private int version;
        private double deadline;
        private bool aborting;
        internal string LastError { get; private set; }
        internal bool IsSending => native != null;

        internal MainThreadTransport(IEventTransport transport, int timeoutSeconds = 15, Func<double> clock = null)
        {
            this.transport = transport;
            this.timeoutSeconds = timeoutSeconds;
            this.clock = clock ?? (() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
        }

        public IEventRequest Send(string body)
        {
            lock (gate)
            {
                // The processor disposes its previous request before creating another.
                if (pending != null && !pending.Disposed)
                    throw new RequestNotStartedException(new InvalidOperationException("A request is already pending."));
                pending = new Request(body);
                if (suspended || Disabled) pending.CancelBeforeSend();
                return pending;
            }
        }

        // Main-thread lifecycle changes immediately prevent native sends, including queued ones.
        internal int Suspend()
        {
            var current = SuspendPending();
            if (native != null) CompleteNative();
            return current;
        }

        // Also safe on the worker: invalidate queued resumes without touching native requests.
        internal int SuspendPending()
        {
            lock (gate)
            {
                suspended = true;
                if (pending != null && !pending.Started) pending.CancelBeforeSend();
                return ++version;
            }
        }

        // Called by the worker after it has processed the corresponding resume in FIFO order.
        internal void Resume(int expectedVersion)
        {
            lock (gate) if (version == expectedVersion) suspended = false;
        }

        internal void Pump()
        {
            if (native != null)
            {
                long? observedStatus = null;
                try
                {
                    // These statuses determine disposition without a complete response body.
                    var status = native.StatusCode;
                    if (status == 410 || status == 503) observedStatus = status;
                    if (!active.Disposed && status != 410 && status != 503 && !native.IsDone)
                    {
                        if (clock() < deadline) return;
                        if (aborting || !(native is IAbortableEventRequest cancelable))
                            throw new TimeoutException("Event request timed out.");
                        // Give Unity at most one second to publish the response after cancellation.
                        // Immediate disposal can lose a status whose body stalled after its headers.
                        aborting = true;
                        cancelable.Abort();
                        deadline = clock() + 1;
                        if (!native.IsDone) return;
                    }
                }
                catch (Exception error)
                {
                    active.Result = new Outcome { Done = true, Error = error.Message };
                    StopNative();
                    return;
                }
                CompleteNative(observedStatus);
            }
            lock (gate)
            {
                if (Disabled || suspended || pending == null || pending.Started || pending.Disposed || pending.Result.Done) return;
                active = pending;
                active.Started = true;
            }
            LastError = null;
            try
            {
                aborting = false;
                deadline = clock() + timeoutSeconds;
                native = transport.Send(active.Body) ?? throw new InvalidOperationException("Transport returned no request.");
            }
            catch (Exception error)
            {
                active.Result = new Outcome { Done = true, Retry = error is RequestNotStartedException, Error = error.Message };
                active = null;
            }
        }

        private void CompleteNative(long? observedStatus = null)
        {
            var outcome = new Outcome { Done = true };
            try
            {
                outcome.StatusCode = observedStatus ?? native.StatusCode;
                if (outcome.StatusCode == 410) Disabled = true;
                // Once observed, 410/503 do not depend on later native progress/outcome reads.
                outcome.Retry = outcome.StatusCode == 503;
                if (outcome.StatusCode != 410 && outcome.StatusCode != 503)
                {
                    outcome.Retry = native.CanRetry && !aborting;
                    outcome.Success = native.IsDone && native.Succeeded;
                }
                outcome.Error = outcome.Success ? null : native.Error;
                if (aborting && outcome.StatusCode == 0) outcome.Error = "Event request timed out.";
                if (outcome.Success)
                {
                    // Optional response metadata must not change a successful upload's disposition.
                    try { outcome.Country = native.CountryCode; }
                    catch (Exception error) { LastError = error.Message; }
                }
            }
            catch (Exception error) { outcome.Retry = outcome.StatusCode == 503; outcome.Error = error.Message; }
            active.Result = outcome;
            StopNative();
        }

        private void StopNative()
        {
            var request = native;
            native = null;
            active = null;
            try { request.Dispose(); }
            catch (Exception error) { LastError = error.Message; }
        }
    }
}
