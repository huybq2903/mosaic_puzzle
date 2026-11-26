using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Falcon.InGame.LevelEditor.Ai
{
    public sealed class AiStatus
    {
        public bool Reachable, Ready, Busy;
        public string Message = "";
    }

    public sealed class AiChoice
    {
        public string id, label, hint;
    }

    public sealed class AiMode
    {
        public string id, label, hint;
    }

    public sealed class AiOption
    {
        public string id, label, type, hint;
        [JsonProperty("default")] public string def;
        public List<string> modes = new();
        public List<AiChoice> choices = new();
    }

    public sealed class AiSpec
    {
        public List<AiMode> modes = new();
        public List<AiOption> options = new();
    }

    public sealed class AiModel
    {
        public string id, tag;
    }

    public sealed class AiResult
    {
        public string name, bg, prompt;
    }

    public sealed class AiJob
    {
        public string status = "running", error;
        public List<AiResult> results = new();
        public List<string> errors = new();
        public List<string> log = new();
    }

    /// <summary>Máy khách của server ảnh Mosaic Studio (server/server.py): trạng thái, model, tuỳ chọn, vẽ ảnh bằng AI và lấy kết quả.</summary>
    public sealed class AiClient
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

        public string Url = "http://127.0.0.1:8765";

        private string Base => (Url ?? "").Trim().TrimEnd('/');

        private async Task<T> Get<T>(string path)
        {
            using var response = await Http.GetAsync(Base + path).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(Detail(text, (int)response.StatusCode));
            return JsonConvert.DeserializeObject<T>(text);
        }

        private static string Detail(string json, int code)
        {
            try { return JsonConvert.DeserializeObject<Dictionary<string, object>>(json)?["detail"]?.ToString() ?? "HTTP " + code; }
            catch { return "HTTP " + code; }
        }

        public async Task<AiStatus> Status()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                using var response = await Http.GetAsync(Base + "/api/status", cts.Token).ConfigureAwait(false);
                var j = JsonConvert.DeserializeObject<Dictionary<string, object>>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                return new AiStatus { Reachable = true, Ready = j.TryGetValue("ready", out var r) && Convert.ToBoolean(r), Busy = j.TryGetValue("busy", out var b) && Convert.ToBoolean(b), Message = j.TryGetValue("message", out var m) ? m?.ToString() ?? "" : "" };
            }
            catch { return new AiStatus(); }
        }

        public Task<AiSpec> Spec() => Get<AiSpec>("/api/options");

        public async Task<List<AiModel>> Models(bool refresh) => (await Get<Dictionary<string, List<AiModel>>>("/api/models" + (refresh ? "?refresh=1" : "")).ConfigureAwait(false)).GetValueOrDefault("models") ?? new List<AiModel>();

        /// <summary>Gửi ảnh gốc (PNG) và tuỳ chọn; trả mã job.</summary>
        public async Task<string> Start(byte[] png, string mode, string model, int count, Dictionary<string, string> options)
        {
            using var form = new MultipartFormDataContent
            {
                { new StringContent(mode), "mode" },
                { new StringContent(model), "model" },
                { new StringContent(count.ToString()), "count" },
                { new StringContent(JsonConvert.SerializeObject(options)), "options" },
            };
            var file = new ByteArrayContent(png);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, "image", "input.png");
            using var response = await Http.PostAsync(Base + "/api/generate", form).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(Detail(text, (int)response.StatusCode));
            return JsonConvert.DeserializeObject<Dictionary<string, object>>(text)["id"].ToString();
        }

        public Task<AiJob> Job(string id) => Get<AiJob>("/api/jobs/" + id);

        public async Task<byte[]> Image(string id, string name)
        {
            using var response = await Http.GetAsync(Base + "/api/jobs/" + id + "/images/" + name).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }

        public async Task Cancel(string id)
        {
            try
            {
                using var response = await Http.PostAsync(Base + "/api/jobs/" + id + "/cancel", null).ConfigureAwait(false);
            }
            catch
            {
                // job tự huỷ sau 60 giây không ai hỏi thăm
            }
        }
    }
}
