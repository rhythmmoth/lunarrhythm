using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using YARG.Core.Logging;

namespace YARG.Core.Song
{
    /// <summary>
    /// The auto-updater's checks: the latest build's file list (path → size and SHA-256) and zip on the CDN, made by
    /// tools/make_update.py. The game compares its own files with the list, downloads the zip only when something
    /// differs, and stages just the changed files (checked against the list) for a script to copy in once the game
    /// has closed.
    /// </summary>
    public static class BuildUpdate
    {
        public const string MANIFEST_URL = "https://cdn.example.com/builds/lunarrhythm-latest.json";
        public const string ZIP_URL = "https://cdn.example.com/builds/lunarrhythm-latest.zip";

        public class FileEntry
        {
            public long Size;
            public string Sha256 = "";
        }

        public class Manifest
        {
            /// <summary>Relative path (forward slashes) → the file.</summary>
            public Dictionary<string, FileEntry> Files = new();

            /// <summary>When the build was made (its build_stamp.txt, UTC seconds); 0 on lists from before stamps.</summary>
            public long Built;
        }

        /// <summary>Whether the CDN's build should replace this one: only when it's newer, so a build made after the
        /// last upload isn't swapped back for the older one. An unstamped (older) game always checks, as before; a
        /// stamped game never takes an unstamped list, which predates it.</summary>
        public static bool IsNewer(Manifest manifest, long ownBuilt) => ownBuilt == 0 || manifest.Built > ownBuilt;

        /// <summary>This game's build stamp from its StreamingAssets folder; 0 if it has none (or isn't readable).</summary>
        public static long ReadStamp(string streamingAssetsDir)
        {
            try
            {
                return long.TryParse(File.ReadAllText(Path.Combine(streamingAssetsDir, "build_stamp.txt")).Trim(), out long stamp) ? stamp : 0;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return 0;
            }
        }

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

        /// <summary>The file list as text (its hash tells whether it changed since the last check), or null when it
        /// can't be had (offline, not published yet).</summary>
        public static async Task<string?> FetchManifest(CancellationToken cancel = default)
        {
            try
            {
                using var request = Request(MANIFEST_URL);
                using var response = await Http.SendAsync(request, cancel).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                YargLogger.LogWarning($"Update check failed: {e.Message}");
                return null;
            }
        }

        /// <summary>Reads the list; null if it isn't one, or names a path outside the game folder.</summary>
        public static Manifest? Parse(string json)
        {
            try
            {
                var manifest = JsonConvert.DeserializeObject<Manifest>(json);
                if (manifest?.Files == null || manifest.Files.Count == 0) return null;
                return manifest.Files.Keys.All(SafePath) ? manifest : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>A relative path that stays inside the game folder.</summary>
        internal static bool SafePath(string path) =>
            !string.IsNullOrEmpty(path) && !Path.IsPathRooted(path) && !path.Contains(':')
            && path.Split('/', '\\').All(part => part.Length > 0 && part != "." && part != "..");

        public static string Hash(string text) => Hex(SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(text)));

        /// <summary>The listed files that are missing or different in the game folder (size first, then checksum).</summary>
        public static List<string> Changed(Manifest manifest, string gameDir)
        {
            var changed = new List<string>();
            foreach (var (path, entry) in manifest.Files)
            {
                string local = Path.Combine(gameDir, path);
                var info = new FileInfo(local);
                if (!info.Exists || info.Length != entry.Size || !HashFile(local).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    changed.Add(path);
                }
            }
            return changed;
        }

        /// <summary>Downloads the build's zip to a file; progress reports (bytes so far, total or -1).</summary>
        public static async Task<bool> Download(string toFile, Action<long, long>? progress, CancellationToken cancel = default)
        {
            try
            {
                using var request = Request(ZIP_URL);
                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return false;
                long total = response.Content.Headers.ContentLength ?? -1;
                using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var output = File.Create(toFile);
                var buffer = new byte[1 << 16];
                long read = 0;
                int n;
                while ((n = await input.ReadAsync(buffer, 0, buffer.Length, cancel).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer, 0, n, cancel).ConfigureAwait(false);
                    read += n;
                    progress?.Invoke(read, total);
                }
                return true;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
            {
                YargLogger.LogWarning($"Update download failed: {e.Message}");
                return false;
            }
        }

        /// <summary>Extracts the changed files from the zip into the staging folder, each checked against the list.
        /// False (and nothing staged) if any is missing from the zip or doesn't match.</summary>
        public static bool Stage(string zipFile, Manifest manifest, IEnumerable<string> changed, string stagingDir)
        {
            try
            {
                if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
                using var zip = ZipFile.OpenRead(zipFile);
                var entries = zip.Entries.ToDictionary(e => e.FullName.Replace('\\', '/'), e => e);
                foreach (string path in changed)
                {
                    if (!SafePath(path) || !entries.TryGetValue(path, out var entry)) return Fail(stagingDir, $"{path} isn't in the update");
                    string target = Path.Combine(stagingDir, path);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, true);
                    var expected = manifest.Files[path];
                    if (new FileInfo(target).Length != expected.Size || !HashFile(target).Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        return Fail(stagingDir, $"{path} doesn't match the update's list");
                    }
                }
                return true;
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                return Fail(stagingDir, e.Message);
            }
        }

        private static bool Fail(string stagingDir, string why)
        {
            YargLogger.LogWarning($"Update not staged: {why}");
            try
            {
                if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
            }
            catch (IOException) { }
            return false;
        }

        private static HttpRequestMessage Request(string url)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            string? key = OnlineSongs.ApiKey;
            if (!string.IsNullOrEmpty(key) && OnlineSongs.IsKeyHost(request.RequestUri!))
            {
                request.Headers.Add(OnlineSongs.API_KEY_HEADER, key);
            }
            return request;
        }

        internal static string HashFile(string path)
        {
            using var stream = File.OpenRead(path);
            return Hex(SHA256.Create().ComputeHash(stream));
        }

        private static string Hex(byte[] bytes) => string.Concat(bytes.Select(b => b.ToString("x2")));
    }
}
