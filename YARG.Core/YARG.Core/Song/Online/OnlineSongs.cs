using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using YARG.Core.Logging;

namespace YARG.Core.Song
{
    /// <summary>
    /// Mirrors the online song database into Festival-style song folders, so the normal scanner picks them up.
    /// Covers and lyrics are saved; charts (every sync) and audio (when a song is previewed or played) are
    /// downloaded into memory only, through signed links.
    /// </summary>
    public static class OnlineSongs
    {
        // From the leaderboard server: it hands each signed-in player the list for their environment (prod or dev)
        // Point this (and KEY_DOMAIN) at your own song database server
        public const string DATABASE_URL = "https://api.example.com/lb/songdb";
        public const string DATABASE_FILE = "songdb.json";
        // The API and CDN only answer requests carrying the game's key
        public const string API_KEY_HEADER = "X-LunarRhythm-Key";
        // The key goes only to our own hosts, never to other URLs a song entry points at
        private const string KEY_DOMAIN = "example.com";
        // The database entry a folder was built from; written last, so it marks a complete folder
        internal const string ENTRY_FILE = "online.json";
        private const string VERSION_KEY = "serverChangeList";
        // Not a song: the title screen news line, edited in the songdb tool
        public const string NEWS_KEY = "_news";

        private static readonly Regex SHORTNAME = new("^[a-z0-9][a-z0-9_-]*$");
        private static readonly string[] IMAGE_FORMATS = { ".png", ".jpg", ".jpeg" };
        private static readonly string[] VIDEO_FORMATS = { ".mp4", ".webm", ".mov" };

        private static readonly HttpClient Http = CreateClient();
        private static readonly ConcurrentDictionary<string, object> FolderLocks = new(StringComparer.OrdinalIgnoreCase);
        // Charts of online songs, by folder: never written to disk
        private static readonly ConcurrentDictionary<string, byte[]> Charts = new(StringComparer.OrdinalIgnoreCase);
        // Audio of the last few songs previewed or played, oldest first
        private static readonly List<(string Folder, Dictionary<string, byte[]> Stems)> RecentStems = new();
        private const int STEMS_KEPT = 2;

        /// <summary>Progress text for loading screens; written from worker threads.</summary>
        public static volatile string Status = string.Empty;

        /// <summary>News text from the last database read (online or cached); empty if none.</summary>
        public static volatile string News = string.Empty;

        // Kept from Sync so later media downloads (previews, gameplay) can send it too
        private static volatile string? _apiKey;
        internal static string? ApiKey { get => _apiKey; set => _apiKey = value; }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("LunarRhythm");
            return client;
        }

        /// <summary>
        /// Brings the song folders in <paramref name="root"/> in line with the database.
        /// Falls back to the last downloaded database when offline. Never throws.
        /// </summary>
        public static void Sync(string root, bool allowNetwork, string? apiKey, string url = DATABASE_URL)
        {
            _apiKey = apiKey;
            // Only charts fetched now count: signed out or offline, the online songs aren't playable
            Charts.Clear();
            try
            {
                Directory.CreateDirectory(root);
                var db = LoadDatabase(root, allowNetwork, apiKey, url);
                if (db == null)
                {
                    return;
                }

                News = db[NEWS_KEY]?.Type == JTokenType.String ? (string) db[NEWS_KEY]! : string.Empty;
                var songs = db.Properties()
                    .Where(p => SHORTNAME.IsMatch(p.Name) && p.Value is JObject)
                    .ToDictionary(p => p.Name, p => (JObject) p.Value);
                foreach (var name in db.Properties().Select(p => p.Name).Where(n => !songs.ContainsKey(n) && n != NEWS_KEY))
                {
                    YargLogger.LogFormatWarning("Skipping online song with invalid shortname {0}", name);
                }

                foreach (var dir in Directory.GetDirectories(root))
                {
                    if (!songs.ContainsKey(Path.GetFileName(dir)))
                    {
                        TryDelete(dir);
                    }
                }

                int done = 0;
                Parallel.ForEach(songs, new ParallelOptions { MaxDegreeOfParallelism = 6 }, song =>
                {
                    SyncSong(Path.Combine(root, song.Key), song.Value, allowNetwork);
                    Status = $"{Interlocked.Increment(ref done)} / {songs.Count}";
                });
                YargLogger.LogFormatInfo("Synced {0} online songs", songs.Count);
            }
            catch (Exception ex)
            {
                YargLogger.LogException(ex, "Online song sync failed");
            }
            finally
            {
                Status = string.Empty;
            }
        }

        private static JObject? LoadDatabase(string root, bool allowNetwork, string? apiKey, string url)
        {
            string cached = Path.Combine(root, DATABASE_FILE);
            if (allowNetwork)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    if (!string.IsNullOrEmpty(apiKey))
                    {
                        request.Headers.Add(API_KEY_HEADER, apiKey);
                    }
                    // Signed out, the server sends nothing (401) and the saved copy is used, as when offline
                    if (Leaderboards.Token is { } token && IsKeyHost(request.RequestUri!))
                    {
                        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                    }
                    using var response = Http.SendAsync(request).GetAwaiter().GetResult();
                    response.EnsureSuccessStatusCode();
                    string text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    var db = JObject.Parse(text);
                    File.WriteAllText(cached, text);
                    return db;
                }
                catch (Exception ex)
                {
                    YargLogger.LogFormatWarning("Couldn't download the online song database ({0}), using the saved copy", ex.Message);
                }
            }

            try
            {
                return File.Exists(cached) ? JObject.Parse(File.ReadAllText(cached)) : null;
            }
            catch (Exception ex)
            {
                YargLogger.LogException(ex, "Saved online song database is unreadable");
                return null;
            }
        }

        private static void SyncSong(string folder, JObject entry, bool allowNetwork)
        {
            string entryFile = Path.Combine(folder, ENTRY_FILE);
            var old = ReadEntry(folder);
            bool current = old != null && old[VERSION_KEY]?.ToString() == entry[VERSION_KEY]?.ToString();
            if (!allowNetwork)
            {
                return; // nothing playable without the chart, which only ever lives in memory
            }

            // The CDN only serves signed links, which only signed-in players get. Ask before touching the folder,
            // so a signed-out player keeps whatever they already had. The chart is fetched on every sync (it's
            // never saved); the cover and lyrics only when the song changed.
            string notes = Url(entry, "data", "notes");
            string cover = current ? string.Empty : Url(entry, "assets", "cover");
            string lyrics = current ? string.Empty : Url(entry, "assets", "lyrics");
            var links = Leaderboards.SignUrls(new[] { notes, cover, lyrics });
            if (links == null)
            {
                return;
            }

            byte[]? chart = links.TryGetValue(notes, out var notesLink) ? DownloadBytes(notesLink) : null;
            if (chart == null)
            {
                YargLogger.LogFormatWarning("Online song {0} has no downloadable chart; skipping", Path.GetFileName(folder));
                return;
            }

            if (!current)
            {
                // New or updated: start clean so stale files never mix with the new version
                if (Directory.Exists(folder) && !TryDelete(folder))
                {
                    return;
                }
                Directory.CreateDirectory(folder);

                if (links.TryGetValue(lyrics, out var lyricsLink))
                {
                    Download(lyricsLink, Path.Combine(folder, FestivalSong.LYRICS_FILE));
                }

                if (links.TryGetValue(cover, out var coverLink))
                {
                    // The extension comes from the original URL; the signed one ends in its query
                    Download(coverLink, Path.Combine(folder, "album" + Extension(cover, IMAGE_FORMATS, ".png")));
                }

                File.WriteAllText(Path.Combine(folder, FestivalSong.INFO_FILE), MakeInfo(entry).ToString());
                File.WriteAllText(entryFile, entry.ToString());
            }
            else
            {
                RemoveOldMedia(folder);
            }

            Charts[Key(folder)] = chart;
        }

        // Older builds saved the chart, audio and video next to the cover; none of those are kept on disk now
        private static void RemoveOldMedia(string folder)
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                string name = Path.GetFileName(file).ToLowerInvariant();
                string ext = Path.GetExtension(name);
                if (name == "notes.mid" || IniAudio.SupportedFormats.Contains(ext) || VIDEO_FORMATS.Contains(ext)
                    || ext == ".part")
                {
                    try { File.Delete(file); } catch (Exception) { }
                }
            }
        }

        private static string Key(string folder) => Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, '/');

        /// <summary>
        /// The chart of an online song, which is only ever kept in memory (fetched by <see cref="Sync"/>).
        /// False for other songs, and for online ones while signed out.
        /// </summary>
        public static bool TryGetChart(string folder, out byte[] chart) => Charts.TryGetValue(Key(folder), out chart!);

        /// <summary>
        /// Stands in for the chart file's last-write time in the song cache: the same chart always gives the same
        /// stamp, a changed chart (almost surely) a different one.
        /// </summary>
        public static DateTime ChartStamp(byte[] chart)
        {
            using var sha = SHA1.Create();
            long bits = BitConverter.ToInt64(sha.ComputeHash(chart), 0) & long.MaxValue;
            return new DateTime(bits % DateTime.MaxValue.Ticks, DateTimeKind.Utc);
        }

        /// <summary>Festival info.json from a database entry, so FestivalSong.ReadInfo handles it.</summary>
        internal static JObject MakeInfo(JObject entry)
        {
            var otherInfo = entry["other_info"] as JObject;
            var meta = otherInfo?["metadata"] as JObject;
            var info = new JObject();
            void Copy(string key, JToken? value)
            {
                if (value != null && value.Type != JTokenType.Null)
                {
                    info[key] = value.DeepClone();
                }
            }

            Copy("title", entry["title"]);
            Copy("artist", entry["artist"]);
            Copy("album", entry["album"]);
            Copy("release_year", entry["release_year"]);
            Copy("charter", entry["charter"]);
            Copy("genre", meta?["genre"]);
            Copy("length", meta?["length"]);
            Copy("preview_start_time", meta?["preview_start_time"]);
            Copy("diff", otherInfo?["diff"]);
            return info;
        }

        public static bool IsOnline(string root, SongEntry song)
        {
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(song.ActualLocation).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True once the song's audio is in memory, so previews and gameplay can start without a download.</summary>
        public static bool StemsReady(string folder)
        {
            string key = Key(folder);
            lock (RecentStems)
            {
                return RecentStems.Exists(s => s.Folder == key);
            }
        }

        /// <summary>
        /// Downloads the song's audio into memory; it's never saved. The last few songs stay loaded, so a preview
        /// followed by playing the song downloads once. False if any stem failed.
        /// </summary>
        public static bool FetchStems(string folder)
        {
            // Preview and gameplay can ask for the same song at once; let one finish the other's work
            lock (FolderLocks.GetOrAdd(Key(folder), _ => new object()))
            {
                if (StemsReady(folder))
                {
                    return true;
                }

                var wanted = new List<(string File, string Url)>();
                if (ReadEntry(folder)?["stems"] is JObject stems)
                {
                    foreach (var stem in stems.Properties())
                    {
                        string url = stem.Value.Type == JTokenType.String ? ((string) stem.Value!).Trim() : string.Empty;
                        // Stem names double as file names; only ones the audio loader knows are any use
                        if (url.Length > 0 && IniAudio.SupportedStems.Contains(stem.Name))
                        {
                            wanted.Add((stem.Name + Extension(url, IniAudio.SupportedFormats, ".ogg"), url));
                        }
                    }
                }

                var links = Leaderboards.SignUrls(wanted.Select(w => w.Url));
                if (links == null)
                {
                    return false; // signed out or offline
                }

                var loaded = new ConcurrentDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                int done = 0;
                bool ok = true;
                Parallel.ForEach(wanted, stem =>
                {
                    var bytes = links.TryGetValue(stem.Url, out var link) ? DownloadBytes(link) : null;
                    if (bytes == null)
                    {
                        ok = false;
                    }
                    else
                    {
                        loaded[stem.File] = bytes;
                    }
                    Status = $"{Interlocked.Increment(ref done)} / {wanted.Count} files";
                });
                Status = string.Empty;
                if (!ok || loaded.IsEmpty)
                {
                    return false;
                }

                lock (RecentStems)
                {
                    RecentStems.Add((Key(folder), new Dictionary<string, byte[]>(loaded, StringComparer.OrdinalIgnoreCase)));
                    if (RecentStems.Count > STEMS_KEPT)
                    {
                        RecentStems.RemoveAt(0); // streams already playing hold their own reference
                    }
                }
                return true;
            }
        }

        /// <summary>The song's audio files by name (e.g. "lead.ogg"), or null if they aren't in memory.</summary>
        public static IReadOnlyDictionary<string, byte[]>? GetStems(string folder)
        {
            string key = Key(folder);
            lock (RecentStems)
            {
                return RecentStems.Find(s => s.Folder == key).Stems;
            }
        }

        /// <summary>True for folders made by <see cref="Sync"/>.</summary>
        public static bool IsOnlineFolder(string folder) => File.Exists(Path.Combine(folder, ENTRY_FILE));

        private static JObject? ReadEntry(string folder)
        {
            try
            {
                string path = Path.Combine(folder, ENTRY_FILE);
                return File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : null;
            }
            catch (Exception)
            {
                return null; // treated as not synced; the next sync rebuilds it
            }
        }

        private static string Url(JObject entry, string section, string key)
        {
            return entry[section]?[key] is JValue { Type: JTokenType.String } value ? ((string) value!).Trim() : string.Empty;
        }

        private static string Extension(string url, string[] allowed, string fallback)
        {
            try
            {
                string ext = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
                return allowed.Contains(ext) ? ext : fallback;
            }
            catch (UriFormatException)
            {
                return fallback;
            }
        }

        // Covers and lyrics: written beside the target and moved in, so a cut connection never leaves a broken file
        private static bool Download(string url, string path)
        {
            var bytes = DownloadBytes(url);
            if (bytes == null)
            {
                return false;
            }

            string part = path + ".part";
            try
            {
                File.WriteAllBytes(part, bytes);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                File.Move(part, path);
                return true;
            }
            catch (Exception ex)
            {
                YargLogger.LogWarning($"Couldn't save {path} ({ex.Message})");
                try { File.Delete(part); } catch (Exception) { }
                return false;
            }
        }

        private static byte[]? DownloadBytes(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                return null;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                string? key = _apiKey;
                if (!string.IsNullOrEmpty(key) && IsKeyHost(uri))
                {
                    request.Headers.Add(API_KEY_HEADER, key);
                }
                using var response = Http.SendAsync(request).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
                return response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // The path only: the signed query is a short-lived pass, no need to log it
                YargLogger.LogWarning($"Download failed: {uri.GetLeftPart(UriPartial.Path)} ({ex.Message})");
                return null;
            }
        }

        internal static bool IsKeyHost(Uri uri)
        {
            return uri.Scheme == Uri.UriSchemeHttps
                && (uri.Host.Equals(KEY_DOMAIN, StringComparison.OrdinalIgnoreCase)
                    || uri.Host.EndsWith("." + KEY_DOMAIN, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryDelete(string dir)
        {
            try
            {
                Directory.Delete(dir, true);
                return true;
            }
            catch (Exception ex)
            {
                // Usually a file still in use (menu music); the next sync tries again
                YargLogger.LogWarning($"Couldn't remove {dir}: {ex.Message}");
                return false;
            }
        }
    }
}
