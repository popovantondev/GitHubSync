using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

public sealed class GitHubWriteException : IOException
{
    public readonly string Category, RequestId;
    public readonly int Status;
    public GitHubWriteException(string category, int status = 0, string requestId = "") : base("GitHub write: " + category) { Category = category; Status = status; RequestId = requestId; }
}
// Exactly one mutation per call. Redirects and automatic mutation retries are forbidden.
public static class GitHubWrite
{
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
    private sealed class BlobContent : HttpContent
    {
        private readonly string data;
        private readonly Action<long> progress;
        private static readonly byte[] Prefix = Encoding.ASCII.GetBytes("{\"encoding\":\"base64\",\"content\":\"");
        private static readonly byte[] Suffix = Encoding.ASCII.GetBytes("\"}");
        public BlobContent(string value, Action<long> report)
        {
            data = value; progress = report;
            if (data.Length % 4 != 0) throw new GitHubWriteException("invalid-request");
            foreach (char c in data) if (!(c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '+' || c == '/' || c == '=')) throw new GitHubWriteException("invalid-request");
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        }
        protected override bool TryComputeLength(out long length) { length = Prefix.Length + (long)data.Length + Suffix.Length; return true; }
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext context)
        {
            await stream.WriteAsync(Prefix, 0, Prefix.Length).ConfigureAwait(false);
            byte[] buffer = new byte[65536];
            for (int offset = 0; offset < data.Length;) {
                int count = Math.Min(buffer.Length, data.Length - offset);
                Encoding.ASCII.GetBytes(data, offset, count, buffer, 0);
                await stream.WriteAsync(buffer, 0, count).ConfigureAwait(false);
                offset += count;
                long bytes = (long)offset / 4 * 3;
                if (offset == data.Length) bytes -= data.EndsWith("==", StringComparison.Ordinal) ? 2 : data.EndsWith("=", StringComparison.Ordinal) ? 1 : 0;
                if (progress != null) progress(bytes);
            }
            await stream.WriteAsync(Suffix, 0, Suffix.Length).ConfigureAwait(false);
        }
    }
    private static Action<long> Progress(string path)
    {
        if (String.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        Dictionary<string, object> snapshot;
        try { snapshot = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8)); } catch { return null; }
        var clock = Stopwatch.StartNew();
        return delegate(long sent) {
            if (clock.ElapsedMilliseconds < 250 && sent != Convert.ToInt64(snapshot["fileBytes"])) return;
            clock.Restart();
            snapshot["fileSent"] = sent; snapshot["fileProgressKnown"] = true; snapshot["updated"] = DateTime.UtcNow.ToString("o");
            try {
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, Json.Serialize(snapshot), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            } catch { /* A UI snapshot failure must not cause a second mutation. */ }
        };
    }
    private static string Category(int status, string body)
    {
        string message = "";
        try { var error = Json.Deserialize<Dictionary<string, object>>(body); if (error.ContainsKey("message")) message = Convert.ToString(error["message"]).ToLowerInvariant(); } catch { }
        // Never expose server bodies: they can echo credentials, payloads or private paths.
        if (status == 401) return "authentication";
        if (status == 429 || (status == 403 && (message.Contains("rate limit") || message.Contains("abuse")))) return "rate-limit";
        if (status == 403 && (message.Contains("sso") || message.Contains("saml"))) return "sso";
        if (status == 403) return "permission";
        if (status == 404) return "not-found";
        if (status == 409) return "conflict";
        if (status == 413 || (status == 422 && (message.Contains("too large") || message.Contains("exceeds") || message.Contains("maximum size")))) return "size";
        if (status == 422) return "validation";
        if (status >= 500) return "uncertain";
        return "http-error";
    }
    private static string SafeRequestId(HttpResponseMessage response)
    {
        IEnumerable<string> values;
        if (!response.Headers.TryGetValues("X-GitHub-Request-Id", out values)) return "";
        string value = String.Join("", values);
        return System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Fa-f0-9:]{1,80}$") ? value : "";
    }
    private static async Task<string> ReadResponse(HttpResponseMessage response)
    {
        using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
        using (var buffer = new MemoryStream()) {
            byte[] chunk = new byte[8192]; int count;
            while ((count = await stream.ReadAsync(chunk, 0, chunk.Length).ConfigureAwait(false)) != 0) {
                if (buffer.Length + count > 1024 * 1024) throw new GitHubWriteException("invalid-response", (int)response.StatusCode);
                buffer.Write(chunk, 0, count);
            }
            return Encoding.UTF8.GetString(buffer.ToArray());
        }
    }
    public static string SendJson(string method, string url, string json, string token, int timeoutSeconds, HttpMessageHandler transport = null)
    {
        return Send(method, url, json, null, token, timeoutSeconds, "", transport);
    }
    public static string SendBlob(string url, string base64, string token, int timeoutSeconds, string progressPath, HttpMessageHandler transport = null)
    {
        return Send("POST", url, "", base64, token, timeoutSeconds, progressPath, transport);
    }
    private static string Send(string method, string url, string json, string base64, string token, int timeoutSeconds, string progressPath, HttpMessageHandler transport)
    {
        Uri uri;
        if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != "https" || uri.Host != "api.github.com" || !uri.IsDefaultPort || uri.UserInfo != "" || uri.Query != "" || !uri.AbsolutePath.StartsWith("/repos/", StringComparison.Ordinal) || (method != "POST" && method != "PATCH")) throw new GitHubWriteException("invalid-request");
        if (base64 != null && (method != "POST" || !uri.AbsolutePath.EndsWith("/git/blobs", StringComparison.Ordinal))) throw new GitHubWriteException("invalid-request");
        if (timeoutSeconds < 1 || timeoutSeconds > 86400) throw new GitHubWriteException("invalid-request");
        using (var handler = transport ?? new HttpClientHandler { AllowAutoRedirect = false })
        using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSeconds), MaxResponseContentBufferSize = 1024 * 1024 })
        using (var request = new HttpRequestMessage(new HttpMethod(method), uri)) {
            request.Headers.UserAgent.ParseAdd("GitHubSync/1.5.1"); request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            if (!String.IsNullOrEmpty(token)) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            request.Content = base64 != null ? (HttpContent)new BlobContent(base64, Progress(progressPath)) : new StringContent(json, Encoding.UTF8, "application/json");
            try {
                using (var response = client.SendAsync(request).GetAwaiter().GetResult()) {
                    string body = ReadResponse(response).GetAwaiter().GetResult();
                    if (!response.IsSuccessStatusCode) throw new GitHubWriteException(Category((int)response.StatusCode, body), (int)response.StatusCode, SafeRequestId(response));
                    return body;
                }
            } catch (GitHubWriteException) { throw; }
            catch (OperationCanceledException) { throw new GitHubWriteException("timeout"); }
            catch { throw new GitHubWriteException("connection"); }
        }
    }
}
