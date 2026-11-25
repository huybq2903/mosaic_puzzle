using System;
using System.Collections.Generic;
using UnityEngine.Networking;

namespace RealtimeEvents
{
    internal interface IEventRequest : IDisposable
    {
        bool IsDone { get; }
        bool Succeeded { get; }
        bool CanRetry { get; }
        long StatusCode { get; }
        string Error { get; }
        string CountryCode { get; }
    }

    internal interface IEventTransport
    {
        IEventRequest Send(string ndjson);
    }

    // Native cancellation may finish asynchronously and still return an HTTP status.
    internal interface IAbortableEventRequest : IEventRequest
    {
        void Abort();
    }

    // Only construction/setup failures before invoking the native send are safe to retry.
    internal sealed class RequestNotStartedException : Exception
    {
        internal RequestNotStartedException(Exception cause) : base(cause.Message, cause) { }
    }

    internal sealed class UnityTransport : IEventTransport
    {
        private readonly string endpoint;
        private readonly int timeout;
        private readonly string apiKey;
        private readonly Action<UnityWebRequest> startRequest;

        internal UnityTransport(string endpoint, int timeout, Action<UnityWebRequest> startRequest = null, string apiKey = null)
        {
            this.endpoint = WithCountryResolution(endpoint);
            this.timeout = timeout;
            this.apiKey = apiKey;
            this.startRequest = startRequest ?? (request => request.SendWebRequest());
        }

        private static string WithCountryResolution(string endpoint)
        {
            var uri = new UriBuilder(endpoint);
            var parameters = new List<string>();
            foreach (var parameter in uri.Query.TrimStart('?').Split('&'))
            {
                if (parameter.Length == 0) continue;
                var equals = parameter.IndexOf('=');
                var name = equals < 0 ? parameter : parameter.Substring(0, equals);
                if (!string.Equals(Uri.UnescapeDataString(name), "resolve_country", StringComparison.OrdinalIgnoreCase))
                    parameters.Add(parameter);
            }
            parameters.Add("resolve_country=true");
            uri.Query = string.Join("&", parameters);
            return uri.Uri.AbsoluteUri;
        }

        public IEventRequest Send(string ndjson)
        {
            var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST);
            var started = false;
            try
            {
                request.uploadHandler = new UploadHandlerRaw(DiskQueue.Utf8.GetBytes(ndjson));
                request.downloadHandler = new CountryResponse();
                request.timeout = timeout;
                request.redirectLimit = 0;
                request.SetRequestHeader("Content-Type", "application/x-ndjson; charset=utf-8");
                if (apiKey != null) request.SetRequestHeader("X-API-Key", apiKey);
                started = true;
                startRequest(request);
                return new Request(request);
            }
            catch (Exception error)
            {
                request.Dispose();
                if (!started) throw new RequestNotStartedException(error);
                throw;
            }
        }

        // The push API returns plain text, e.g. COUNTRY:VN. Discard oversized responses
        // without failing an otherwise successful upload or buffering an unbounded body.
        private sealed class CountryResponse : DownloadHandlerScript
        {
            private readonly byte[] body = new byte[64];
            private int length;
            // Reuse the receive buffer instead of allocating managed arrays for every response chunk.
            internal CountryResponse() : base(new byte[4096]) { }
            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (dataLength > body.Length - length) length = body.Length + 1;
                else if (dataLength > 0)
                {
                    Buffer.BlockCopy(data, 0, body, length, dataLength);
                    length += dataLength;
                }
                return true;
            }

            internal string CountryCode
            {
                get
                {
                    if (length > body.Length) return null;
                    var text = System.Text.Encoding.ASCII.GetString(body, 0, length).Trim();
                    return text.StartsWith("COUNTRY:", StringComparison.Ordinal)
                        ? ValidCountryCode(text.Substring(8)) : null;
                }
            }
        }

        internal static string ValidCountryCode(string value) =>
            value != null && value.Length == 2 && value[0] >= 'A' && value[0] <= 'Z' &&
            value[1] >= 'A' && value[1] <= 'Z' ? value : null;

        private sealed class Request : IAbortableEventRequest
        {
            private readonly UnityWebRequest request;
            internal Request(UnityWebRequest request) => this.request = request;
            public bool IsDone => request.isDone;
            public long StatusCode => request.responseCode;
            public bool Succeeded => request.result == UnityWebRequest.Result.Success && request.responseCode >= 200 && request.responseCode < 300;
            public bool CanRetry => request.responseCode == 503 ||
                (request.isDone && request.responseCode == 0 && request.result == UnityWebRequest.Result.ConnectionError && request.uploadedBytes == 0);
            public string Error => "HTTP " + request.responseCode + ": " + request.error;
            public string CountryCode => IsDone && Succeeded ? ((CountryResponse)request.downloadHandler).CountryCode : null;
            public void Abort() => request.Abort();
            public void Dispose()
            {
                try { Abort(); }
                finally { request.Dispose(); }
            }
        }
    }
}
