using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

// Shared by the isolated download worker and its synthetic HTTP tests.
public sealed class SyncEntry
{
    public string name, url, digest, hashKind, accept, action, localHash, revision;
    public long length;
}
public sealed class SyncPlan
{
    public string repository, contentKind, sourceId, branch, prefix, resultUrl, directory, tag;
    public long releaseId;
    public SyncEntry[] entries;
}
public static class SyncTransfer
{
    private static string ExtendedPath(string path)
    {
        // PowerShell 5.1 can cache legacy .NET path switches before loading us.
        // Extended absolute paths avoid depending on host/global registry settings.
        if(path.StartsWith(@"\\?\",StringComparison.Ordinal)) return Path.GetFullPath(path);
        if(!Path.IsPathRooted(path)) path=Path.GetFullPath(path);
        return Path.GetFullPath(path.StartsWith(@"\\",StringComparison.Ordinal) ? @"\\?\UNC\"+path.Substring(2) : @"\\?\"+path);
    }
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };
    public static string Fingerprint(string path)
    {
        path=ExtendedPath(path);
        if (!File.Exists(path)) return "-";
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var hash = SHA256.Create()) return Hex(hash.ComputeHash(stream));
    }
    private static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
    public static string HashFile(string path, string kind)
    {
        path=ExtendedPath(path);
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (HashAlgorithm hash = kind == "git" ? (HashAlgorithm)SHA1.Create() : SHA256.Create())
        {
            if (kind == "git") {
                byte[] header = Encoding.ASCII.GetBytes("blob " + stream.Length + "\0");
                hash.TransformBlock(header, 0, header.Length, header, 0);
                byte[] buffer = new byte[65536]; int count;
                while ((count = stream.Read(buffer, 0, buffer.Length)) > 0) hash.TransformBlock(buffer, 0, count, buffer, 0);
                hash.TransformFinalBlock(new byte[0], 0, 0); return Hex(hash.Hash);
            }
            return Hex(hash.ComputeHash(stream));
        }
    }
    public static string SafePath(string directory, string relative)
    {
        if (String.IsNullOrWhiteSpace(relative) || relative.StartsWith("/") || relative.IndexOf('\\') >= 0 || relative.IndexOf(':') >= 0) throw new IOException("Unsafe relative path: " + relative);
        string[] parts = relative.Replace('\\', '/').Split('/');
        foreach (string part in parts) {
            string stem = part.Split('.')[0];
            if (part == "" || part == "." || part == ".." || part.EndsWith(".") || part.EndsWith(" ") || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || part.Equals(".githubsync", StringComparison.OrdinalIgnoreCase) || part.Equals(".git", StringComparison.OrdinalIgnoreCase)
                || System.Text.RegularExpressions.Regex.IsMatch(stem, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) throw new IOException("Unsupported Windows path: " + relative);
        }
        string root = ExtendedPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine(root, String.Join(Path.DirectorySeparatorChar.ToString(), parts)));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Path escapes destination.");
        AssertNoLinks(full);
        if (Directory.Exists(full)) throw new IOException("A directory occupies a file path: " + relative);
        string parent = Path.GetDirectoryName(full);
        while (!String.IsNullOrEmpty(parent)) { if (File.Exists(parent)) throw new IOException("A file blocks a destination folder."); parent = Path.GetDirectoryName(parent); }
        return full;
    }
    private static void AssertNoLinks(string path)
    {
        WindowsPathSafety.AssertNoLinks(path);
    }
    public static void Preview(SyncPlan plan)
    {
        if (!Directory.Exists(ExtendedPath(plan.directory))) throw new IOException("Choose an existing download folder.");
        ValidateDestinations(plan);
        foreach (SyncEntry entry in plan.entries) {
            string target = SafePath(plan.directory, entry.name);
            entry.localHash = Fingerprint(target);
            entry.action = entry.localHash == "-" ? "add" : Matches(target, entry) ? "same" : "update";
        }
    }
    private static void ValidateDestinations(SyncPlan plan)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (SyncEntry entry in plan.entries) {
            if (!seen.Add(SafePath(plan.directory,entry.name))) throw new IOException("Paths differ only by case or are duplicated.");
        }
        foreach (string path in seen) {
            for (string parent=Path.GetDirectoryName(path); !String.IsNullOrEmpty(parent); parent=Path.GetDirectoryName(parent))
                if (seen.Contains(parent)) throw new IOException("A selected file also occupies another file's folder.");
        }
    }
    private static bool Matches(string path, SyncEntry entry)
    {
        return File.Exists(path) && new FileInfo(path).Length == entry.length && !String.IsNullOrEmpty(entry.digest)
            && HashFile(path, entry.hashKind).Equals(entry.digest, StringComparison.OrdinalIgnoreCase);
    }
    private static void AtomicJson(string path, object value)
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, Json.Serialize(value), new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
    }
    public static string Run(SyncPlan plan, string token, string progressPath, string controlPath, HttpMessageHandler transport = null)
    {
        if (plan.entries == null || plan.entries.Length == 0) throw new IOException("Select files first.");
        // Check every target before creating anything. Consent binds local content too.
        ValidateDestinations(plan);
        foreach (SyncEntry entry in plan.entries) {
            string target = SafePath(plan.directory, entry.name);
            if (Fingerprint(target) != entry.localHash) throw new IOException("Local file changed after review: " + entry.name);
            if (entry.length < 0 || !ValidApiUrl(entry.url)) throw new IOException("Invalid download resource.");
            if(plan.contentKind=="code" && entry.length>100L*1024*1024) throw new IOException("Code download API limit: use release attachments for large files.");
        }
        string service = Path.Combine(ExtendedPath(plan.directory), ".githubsync");
        AssertNoLinks(service);
        string taskId;
        using (var hash = SHA256.Create()) taskId = Hex(hash.ComputeHash(Encoding.UTF8.GetBytes(plan.repository + "|" + plan.contentKind + "|" + plan.sourceId))).Substring(0,32);
        string partials = Path.Combine(service, "partials", taskId);
        AssertNoLinks(partials); Directory.CreateDirectory(partials);
        long total = plan.entries.Sum(e => e.length), done = 0; int filesDone = 0;
        var confirmed = new List<string>(); string current = "";
        Action<string,long,long,string> report = delegate(string state, long fileSize, long bytes, string message) {
            AtomicJson(progressPath, new { state = state, direction = "download", mode = plan.contentKind, sourceId = plan.sourceId,
                resultUrl = plan.resultUrl, localDirectory = plan.directory, file = current, fileBytes = fileSize, fileSent = bytes,
                totalBytes = total, completedBytes = done, filesTotal = plan.entries.Length, filesCompleted = filesDone,
                confirmedFiles = confirmed.ToArray(), message = message, verification = plan.entries.All(e => !String.IsNullOrEmpty(e.digest)) ? "hash" : "size" });
        };
        Func<bool> stopping = delegate { return !String.IsNullOrEmpty(controlPath) && File.Exists(controlPath); };
        using (var handler = transport ?? new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None })
        using (var client = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan })
        {
            try {
                foreach (SyncEntry entry in plan.entries) {
                    current = entry.name;
                    if (stopping()) { report("paused", entry.length, 0, ""); return "paused"; }
                    string target = SafePath(plan.directory, entry.name);
                    if (Fingerprint(target) != entry.localHash) throw new IOException("Local file changed after review: " + entry.name);
                    if (entry.action == "same") {
                        if (!Matches(target, entry)) throw new IOException("Local file no longer matches.");
                    } else {
                        string key; using (var sha = SHA256.Create()) key = Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(entry.name + "|" + entry.url + "|" + entry.digest + "|" + entry.revision + "|" + entry.length))).Substring(0,32);
                        string partial = Path.Combine(partials, key + ".part"), metadata = Path.Combine(partials, key + ".json");
                        AssertNoLinks(partial); AssertNoLinks(metadata);
                        string validator = "";
                        if (File.Exists(metadata)) { try { var data = Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(metadata)); validator = Convert.ToString(data["etag"]); } catch { validator = ""; } }
                        long offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
                        // An invalid application-owned partial is replaced only by a full response.
                        if (offset > entry.length) offset=0;
                        // No validator and no immutable/hash identity: restart rather than mixing representations.
                        if (offset > 0 && String.IsNullOrEmpty(validator) && String.IsNullOrEmpty(entry.digest)) offset = 0;
                        bool complete = offset == entry.length && File.Exists(partial) && Matches(partial, entry);
                        if(!complete && offset == entry.length) offset=0;
                        for (int attempt = 0; !complete && attempt < 5; attempt++) {
                            try {
                                using (var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(10)))
                                using (var watch = new Timer(delegate { if (stopping()) cancel.Cancel(); }, null, 0, 200))
                                using (HttpResponseMessage response = Fetch(client, entry, token, offset, validator, cancel.Token)) {
                                    if ((int)response.StatusCode == 416) {
                                        if (File.Exists(partial) && new FileInfo(partial).Length == entry.length && Matches(partial, entry)) { complete = true; break; }
                                        throw new InvalidDataException("Invalid range response; restart required.");
                                    }
                                    response.EnsureSuccessStatusCode();
                                    string etag = response.Headers.ETag != null && !response.Headers.ETag.IsWeak ? response.Headers.ETag.ToString() : "";
                                    if ((int)response.StatusCode == 206) {
                                        var range = response.Content.Headers.ContentRange;
                                        if (range == null || range.Unit != "bytes" || range.From != offset || range.Length != entry.length || range.To < range.From) throw new InvalidDataException("Invalid Content-Range.");
                                        if (!String.IsNullOrEmpty(validator) && etag != validator) throw new InvalidDataException("Resource changed while resuming.");
                                    } else if ((int)response.StatusCode == 200) {
                                        if (offset > 0) report("downloading", entry.length, 0, "range-restart");
                                        offset = 0;
                                    } else throw new InvalidDataException("Unexpected download status.");
                                    if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value != entry.length - offset) throw new InvalidDataException("Resource length changed.");
                                    validator = etag;
                                    AtomicJson(metadata, new { etag = validator, length = entry.length, sourceId = plan.sourceId });
                                    using (var input = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                                    using (var output = new FileStream(partial, offset == 0 ? FileMode.Create : FileMode.Open, FileAccess.Write, FileShare.None)) {
                                        output.Position = offset; byte[] buffer = new byte[65536]; int count;
                                        var clock = System.Diagnostics.Stopwatch.StartNew();
                                        while ((count = input.ReadAsync(buffer, 0, buffer.Length, cancel.Token).GetAwaiter().GetResult()) > 0) {
                                            cancel.Token.ThrowIfCancellationRequested();
                                            if (offset + count > entry.length) throw new InvalidDataException("Response exceeds expected size.");
                                            output.Write(buffer, 0, count); offset += count;
                                            if (clock.ElapsedMilliseconds >= 250) { report("downloading", entry.length, offset, ""); clock.Restart(); }
                                        }
                                        output.Flush(true);
                                    }
                                    if (offset != entry.length) throw new IOException("Incomplete response.");
                                    complete = true;
                                }
                            } catch (InvalidDataException) { throw; }
                            catch (Exception) {
                                if (stopping()) { report("paused", entry.length, File.Exists(partial) ? new FileInfo(partial).Length : 0, ""); return "paused"; }
                                if (attempt == 4) throw;
                                offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
                                report("retrying", entry.length, offset, "");
                                for (int tick = 0; tick < 10 * (attempt + 1); tick++) { if (stopping()) break; Thread.Sleep(200); }
                                if (String.IsNullOrEmpty(validator) && String.IsNullOrEmpty(entry.digest)) offset = 0;
                            }
                        }
                        report("verifying", entry.length, entry.length, "");
                        if (!String.IsNullOrEmpty(entry.digest) && !Matches(partial, entry)) throw new InvalidDataException("Downloaded checksum differs; file was not installed.");
                        if(plan.contentKind=="code" && entry.length<1024) {
                            string text=File.ReadAllText(partial,Encoding.UTF8);
                            if(text.StartsWith("version https://git-lfs.github.com/spec/v1",StringComparison.Ordinal)) throw new InvalidDataException("Git LFS pointer: downloading the actual LFS object is not supported.");
                        }
                        if (stopping()) { report("paused", entry.length, entry.length, ""); return "paused"; }
                        target = SafePath(plan.directory, entry.name);
                        if (Fingerprint(target) != entry.localHash) throw new IOException("Local file changed after review: " + entry.name);
                        Directory.CreateDirectory(Path.GetDirectoryName(target)); AssertNoLinks(target);
                        if (File.Exists(target)) {
                            string backup = Path.Combine(service, "backups", Guid.NewGuid().ToString("N"), entry.name.Replace('/', Path.DirectorySeparatorChar));
                            AssertNoLinks(backup); Directory.CreateDirectory(Path.GetDirectoryName(backup));
                            File.Replace(partial, target, backup);
                        } else File.Move(partial, target);
                    }
                    confirmed.Add(entry.name); done += entry.length; filesDone++; report("downloading", entry.length, entry.length, "");
                }
                report("completed", 0, 0, ""); return "completed";
            } catch (Exception ex) { report("failed", 0, 0, ex.Message); throw; }
        }
    }
    private static bool ValidApiUrl(string value)
    {
        Uri uri; return Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == "https" && uri.Host == "api.github.com" && uri.IsDefaultPort && String.IsNullOrEmpty(uri.UserInfo) && uri.AbsolutePath.StartsWith("/repos/", StringComparison.Ordinal);
    }
    private static HttpResponseMessage Fetch(HttpClient client, SyncEntry entry, string token, long offset, string validator, CancellationToken cancel)
    {
        Uri uri = new Uri(entry.url);
        bool anonymous=false;
        for (int redirect = 0; redirect < 7; redirect++) {
            using (var request = new HttpRequestMessage(HttpMethod.Get, uri)) {
                request.Headers.UserAgent.ParseAdd("GitHubSync/1.5.1"); request.Headers.Accept.ParseAdd(entry.accept);
                request.Headers.AcceptEncoding.ParseAdd("identity");
                if (uri.Host == "api.github.com") {
                    request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
                    if (!anonymous && !String.IsNullOrEmpty(token)) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                }
                if (offset > 0) { request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, null); if (!String.IsNullOrEmpty(validator)) request.Headers.TryAddWithoutValidation("If-Range", validator); }
                var response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).GetAwaiter().GetResult();
                int status = (int)response.StatusCode;
                if(status==401 && uri.Host=="api.github.com" && !anonymous && !String.IsNullOrEmpty(token)) {response.Dispose();anonymous=true;continue;}
                if (status == 301 || status == 302 || status == 303 || status == 307 || status == 308) {
                    Uri next = response.Headers.Location; if (next != null && !next.IsAbsoluteUri) next = new Uri(uri, next); response.Dispose();
                    if (next == null || next.Scheme != "https" || !next.IsDefaultPort || !String.IsNullOrEmpty(next.UserInfo) || !(next.Host == "github.com" || next.Host == "api.github.com" || next.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Unsafe download redirect.");
                    uri = next; continue;
                }
                return response;
            }
        }
        throw new IOException("Too many redirects.");
    }
}
