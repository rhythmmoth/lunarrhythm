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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using YARG.Core.Logging;

namespace YARG.Core.Song
{
    /// <summary>
    /// Client for the leaderboard server (leaderboard-server/ in the repo root). Players sign in with Discord in
    /// their browser; the server hands the game a session token, kept in the data folder.
    /// </summary>
    public static class Leaderboards
    {
        // Point this at your own leaderboard server
        public const string BASE_URL = "https://api.example.com/lb/";
        private const string TOKEN_FILE = "leaderboard_token";
        private const string QUEUE_FILE = "leaderboard_queue.json";

        public class Run
        {
            public string Shortname = "";
            public string Instrument = "";
            public string Difficulty = "";
            public string Chart = "";
            public int Score;
            public int Stars;
            public int NotesHit;
            public int NotesMissed;
            public bool Fc;
        }

        public class Result
        {
            public bool Accepted;
            public string? Reason;
            public bool PersonalBest;
            public int? Rank;
            public int? Best;
        }

        public class Entry
        {
            public int Rank;
            public string Name = "";
            public int Score;
            public int Stars;
            public string Difficulty = "";
            public bool Fc;
            /// <summary>Path of the Discord picture under the server, or null for players without one.</summary>
            public string? Avatar;
        }

        public class Board
        {
            /// <summary>False when the song has no leaderboard (unranked, or no such part).</summary>
            public bool Ranked = true;
            public List<Entry> Entries = new();
            /// <summary>The signed-in player's own row, wherever they rank.</summary>
            public Entry? Me;
        }

        internal static string BaseUrl = BASE_URL;
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
        private static readonly object QueueLock = new();
        private static string? _folder;
        private static volatile string? _token;

        public static bool SignedIn => _token != null;

        internal static string? Token => _token;

        /// <summary>The Discord name the player signed in as, or null when signed out.</summary>
        public static string? PlayerName { get; private set; }

        public static void Init(string dataFolder)
        {
            _folder = dataFolder;
            // Token on the first line, Discord name on the second (encrypted on Windows, see TokenFile)
            string file = Path.Combine(dataFolder, TOKEN_FILE);
            var lines = (TokenFile.Read(file) ?? "").Split('\n');
            _token = lines[0].Trim() != "" ? lines[0].Trim() : null;
            PlayerName = _token != null && lines.Length > 1 ? lines[1].TrimEnd('\r') : null;
            if (_token != null && TokenFile.Encrypted && TokenFile.IsPlainText(file))
            {
                SaveToken(_token, PlayerName); // saved before encryption existed
            }
        }

        /// <summary>The API name for a game instrument, or null if that instrument isn't ranked.</summary>
        public static string? InstrumentName(Instrument instrument) => instrument switch
        {
            Instrument.FiveFretGuitar => "guitar",
            Instrument.FiveFretBass   => "bass",
            Instrument.Keys           => "vocals",  // Festival's tap vocals load as keys
            Instrument.FiveFretRhythm => "drums",   // and pad drums as rhythm
            _ => null,
        };

        public static string? DifficultyName(Difficulty difficulty) => difficulty switch
        {
            Difficulty.Easy or Difficulty.Medium or Difficulty.Hard or Difficulty.Expert => difficulty.ToString().ToLowerInvariant(),
            _ => null,
        };

        /// <summary>The chart's SHA-1, the same hash the songdb tool publishes.</summary>
        public static string? ChartHash(string songFolder)
        {
            // Online charts are only in memory; a chart file covers anything else
            string path = Path.Combine(songFolder, "notes.mid");
            byte[]? chart = OnlineSongs.TryGetChart(songFolder, out var online) ? online
                : File.Exists(path) ? File.ReadAllBytes(path) : null;
            if (chart == null)
            {
                return null;
            }
            using var sha = SHA1.Create();
            return BitConverter.ToString(sha.ComputeHash(chart)).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// Opens Discord's sign-in in the browser and waits until the player finishes it. Returns their name, or
        /// null if they gave up (the server forgets an unfinished sign-in after 10 minutes).
        /// </summary>
        public static async Task<string?> SignIn(Action<string> openUrl, CancellationToken cancel = default)
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            string state = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            openUrl(BaseUrl + "auth/start?state=" + state);

            var giveUp = DateTime.UtcNow.AddMinutes(10);
            while (DateTime.UtcNow < giveUp)
            {
                await Task.Delay(2000, cancel).ConfigureAwait(false);
                try
                {
                    using var response = await Send(HttpMethod.Get, "auth/poll?state=" + state, null).ConfigureAwait(false);
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        return null;
                    }
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        continue;
                    }
                    var body = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                    SaveToken((string?) body["token"], (string?) body["name"]);
                    return PlayerName;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    // A dropped poll is fine; the next one picks up where it left off
                }
            }
            return null;
        }

        public static async Task SignOut()
        {
            if (_token == null)
            {
                return;
            }
            try
            {
                using var _ = await Send(HttpMethod.Post, "auth/signout", null).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                YargLogger.LogWarning($"Leaderboard sign-out didn't reach the server ({ex.Message})");
            }
            SaveToken(null);
        }

        /// <summary>
        /// Sends a run. Returns the server's verdict, or null if it couldn't be reached; the run is then kept and
        /// sent again by <see cref="RetryQueued"/>.
        /// </summary>
        public static async Task<Result?> Submit(Run run)
        {
            if (_token == null)
            {
                return null;
            }
            try
            {
                return await Post(run).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                YargLogger.LogWarning($"Leaderboard unreachable, run kept for later ({ex.Message})");
                lock (QueueLock)
                {
                    var queue = ReadQueue();
                    queue.Add(run);
                    WriteQueue(queue);
                }
                return null;
            }
        }

        /// <summary>Sends runs that couldn't be sent earlier. Never throws.</summary>
        public static async Task RetryQueued()
        {
            List<Run> queue;
            lock (QueueLock)
            {
                queue = ReadQueue();
                if (queue.Count == 0 || _token == null)
                {
                    return;
                }
                WriteQueue(new List<Run>());
            }

            var failed = new List<Run>();
            foreach (var run in queue)
            {
                try
                {
                    await Post(run).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    failed.Add(run);
                }
            }

            if (failed.Count > 0)
            {
                lock (QueueLock)
                {
                    var now = ReadQueue();
                    now.AddRange(failed);
                    WriteQueue(now);
                }
            }
        }

        /// <summary>
        /// Short-lived links for song files: the CDN only serves links the server signed, and it signs them only for
        /// signed-in players. Returns original URL -> signed link (URLs it won't sign are left out), or null when
        /// signed out, banned or offline. Blocks, so call it off the main thread.
        /// </summary>
        public static Dictionary<string, string>? SignUrls(IEnumerable<string> urls)
        {
            if (_token == null)
            {
                return null;
            }

            var wanted = new List<string>(new HashSet<string>(urls.Where(u => u.Length > 0)));
            var signed = new Dictionary<string, string>();
            try
            {
                for (int start = 0; start < wanted.Count; start += MAX_SIGN_BATCH)
                {
                    var batch = wanted.GetRange(start, Math.Min(MAX_SIGN_BATCH, wanted.Count - start));
                    using var response = Send(HttpMethod.Post, "sign", JsonConvert.SerializeObject(new { urls = batch }))
                        .GetAwaiter().GetResult();
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        SaveToken(null);
                        return null;
                    }
                    if (!response.IsSuccessStatusCode)
                    {
                        return null;
                    }
                    var body = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    if (body["urls"] is JObject links)
                    {
                        foreach (var link in links.Properties())
                        {
                            signed[link.Name] = (string) link.Value!;
                        }
                    }
                }
                return signed;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                YargLogger.LogWarning($"Couldn't get song file links ({ex.Message})");
                return null;
            }
        }

        private const int MAX_SIGN_BATCH = 64; // the server's limit per request

        /// <summary>The top of a song's board for one instrument. Null if the server couldn't be reached.</summary>
        public static async Task<Board?> GetBoard(string shortname, string instrument, int limit)
        {
            try
            {
                using var response = await Send(HttpMethod.Get,
                    $"{Uri.EscapeDataString(shortname)}?instrument={Uri.EscapeDataString(instrument)}&limit={limit}", null)
                    .ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return new Board { Ranked = false };
                }
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }
                return JsonConvert.DeserializeObject<Board>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                return null;
            }
        }

        /// <summary>A board entry's Discord picture as PNG bytes, or null.</summary>
        public static async Task<byte[]?> GetAvatar(string path)
        {
            try
            {
                using var response = await Send(HttpMethod.Get, path, null).ConfigureAwait(false);
                return response.IsSuccessStatusCode
                    ? await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false)
                    : null;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return null;
            }
        }

        private static async Task<Result?> Post(Run run)
        {
            using var response = await Send(HttpMethod.Post, "scores", JsonConvert.SerializeObject(new
            {
                shortname = run.Shortname, instrument = run.Instrument, difficulty = run.Difficulty, chart = run.Chart,
                score = run.Score, stars = run.Stars, notesHit = run.NotesHit, notesMissed = run.NotesMissed, fc = run.Fc,
            })).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                SaveToken(null); // the server no longer knows this token, so sign-in has to happen again
                return new Result { Reason = "signed_out" };
            }
            if (!response.IsSuccessStatusCode)
            {
                // Rate limited or the server is down: worth another try later, unlike a rejected score
                throw new HttpRequestException($"leaderboard server returned {(int) response.StatusCode}");
            }
            return JsonConvert.DeserializeObject<Result>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        }

        private static Task<HttpResponseMessage> Send(HttpMethod method, string path, string? json)
        {
            var uri = new Uri(BaseUrl + path);
            var request = new HttpRequestMessage(method, uri);
            string? key = OnlineSongs.ApiKey;
            if (!string.IsNullOrEmpty(key) && OnlineSongs.IsKeyHost(uri))
            {
                request.Headers.Add(OnlineSongs.API_KEY_HEADER, key);
            }
            if (_token is { } token)
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }
            if (json != null)
            {
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }
            return Http.SendAsync(request);
        }

        private static void SaveToken(string? token, string? name = null)
        {
            _token = string.IsNullOrEmpty(token) ? null : token;
            PlayerName = _token == null ? null : name?.Replace("\r", "").Replace("\n", " ");
            if (_folder == null)
            {
                return;
            }
            string file = Path.Combine(_folder, TOKEN_FILE);
            if (_token == null)
            {
                File.Delete(file);
            }
            else
            {
                TokenFile.Write(file, _token + "\n" + PlayerName);
            }
        }

        private static List<Run> ReadQueue()
        {
            string? file = _folder == null ? null : Path.Combine(_folder, QUEUE_FILE);
            try
            {
                return file != null && File.Exists(file)
                    ? JsonConvert.DeserializeObject<List<Run>>(File.ReadAllText(file)) ?? new List<Run>()
                    : new List<Run>();
            }
            catch (JsonException)
            {
                return new List<Run>(); // a half-written queue after a crash: those runs are lost, nothing else is
            }
        }

        private static void WriteQueue(List<Run> queue)
        {
            if (_folder != null)
            {
                File.WriteAllText(Path.Combine(_folder, QUEUE_FILE), JsonConvert.SerializeObject(queue));
            }
        }
    }
}
