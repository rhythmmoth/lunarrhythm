using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using YARG.Core.Extensions;
using YARG.Core.IO;
using YARG.Core.Logging;
using YARG.Core.Replays;

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
            /// <summary>The run's recording (<see cref="EncodeRecording"/>) for the server to replay; null from old builds.</summary>
            public string? Recording;
            /// <summary>Kept for later: when it was first kept (unix seconds) and how often sending it failed since.</summary>
            public long QueuedAt;
            public int Tries;
        }

        /// <summary>A kept run is given up after this many failed tries or this long, so one the server can never take
        /// (it keeps answering "try later") isn't sent on every song scan forever.</summary>
        public const int MAX_TRIES = 10;
        public static readonly TimeSpan MAX_KEPT = TimeSpan.FromDays(7);

        /// <summary>Whether a kept run that just failed again is still worth keeping.</summary>
        public static bool KeepTrying(Run run, DateTimeOffset now)
        {
            run.Tries++;
            if (run.QueuedAt == 0) run.QueuedAt = now.ToUnixTimeSeconds(); // kept by a build from before these fields
            return run.Tries < MAX_TRIES && now.ToUnixTimeSeconds() - run.QueuedAt < MAX_KEPT.TotalSeconds;
        }

        /// <summary>A run's replay frame (setup, inputs and stats) for the server to replay: a format version, then the
        /// frame, gzipped, as base64. Typically 10-60 KB.</summary>
        public static string EncodeRecording(ReplayFrame frame)
        {
            using var memory = new MemoryStream();
            using (var gzip = new GZipStream(memory, CompressionLevel.Optimal, true))
            using (var writer = new BinaryWriter(gzip))
            {
                writer.Write(ReplayIO.REPLAY_VERSIONS.CURRENT);
                frame.Serialize(writer);
            }
            return Convert.ToBase64String(memory.ToArray());
        }

        /// <summary>Reads <see cref="EncodeRecording"/>. Throws on anything that isn't one (it comes from a player);
        /// maxBytes caps the unpacked size.</summary>
        public static ReplayFrame DecodeRecording(string recording, int maxBytes)
        {
            using var gzip = new GZipStream(new MemoryStream(Convert.FromBase64String(recording)), CompressionMode.Decompress);
            using var raw = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (raw.Length + read > maxBytes) throw new InvalidDataException("recording too large");
                raw.Write(buffer, 0, read);
            }
            raw.Position = 0;
            using var data = FixedArray.ReadRemainder(raw);
            var stream = data.ToValueStream();
            int version = stream.Read<int>(Endianness.Little);
            if (version != ReplayIO.REPLAY_VERSIONS.CURRENT) throw new InvalidDataException($"recording version {version}");
            return new ReplayFrame(ref stream, version);
        }

        public class Result
        {
            public bool Accepted;
            public string? Reason;
            public bool PersonalBest;
            public int? Rank;
            public int? Best;
            /// <summary>Accepted runs only (from servers with progression): the run's XP, line by line.</summary>
            public XpInfo? Xp;
            /// <summary>Challenges this run finished.</summary>
            public List<string> Completed = new();
            public Progress? Progress;
            /// <summary>Looks this run unlocked.</summary>
            public List<string> Unlocked = new();
        }

        /// <summary>How a player looks to others: alias text (null for none), colours as #rrggbb (null for none) and
        /// the highway and note themes by id.</summary>
        public class Look
        {
            public string? Alias, Frame, Color, Banner, Highway, Notes;
            /// <summary>Type -> the worn cosmetic's id.</summary>
            public Dictionary<string, string> Ids = new();
        }

        /// <summary>The profile popup: levels, stats, looks and the whole catalog with what's owned.</summary>
        public class Profile : LevelInfo
        {
            public string Name = "";
            public Look Look = new();
            public ProfileStats Stats = new();
            public Dictionary<string, InstrumentStats> Instruments = new();
            public SeasonLevel? Season;
            public List<CosmeticItem> Items = new();
            public List<CosmeticItem> Recent = new();
            public List<CosmeticItem> Next = new();
        }

        public class ProfileStats
        {
            /// <summary>Global rank (prod players only).</summary>
            public int? Rank;
            public long TotalScore;
            public int Fcs;
            public int Plays;
            public string? Favourite;
            /// <summary>Unix seconds.</summary>
            public long Created;
        }

        public class InstrumentStats
        {
            public int Level = 1;
            public int Stars;
        }

        public class SeasonLevel
        {
            public int Number;
            public string Name = "";
            public int Level;
        }

        public class CosmeticItem
        {
            public string Id = "";
            public string Type = "";
            public string Label = "";
            public string? Color;
            /// <summary>Highway and note themes: their colours (see <see cref="OnlineThemes"/>); null keeps the player's presets.</summary>
            public List<string>? Colors;
            public string Rarity = "common";
            public bool Owned;
            public bool Equipped;
            public string Requirement = "";
            public long Have;
            public long Need = 1;
            /// <summary>Unix seconds it was unlocked (null for free looks and locked ones).</summary>
            public long? Unlocked;
            /// <summary>Recently unlocked: when.</summary>
            public long? Date;
        }

        public class XpInfo
        {
            public List<XpLine> Lines = new();
            public int Total;
            public int LevelBefore;
        }

        public class XpLine
        {
            public string Label = "";
            public int Xp;
        }

        /// <summary>A level and how far into it: Into of Need XP.</summary>
        public class LevelInfo
        {
            public int Level = 1;
            public long Into;
            public long Need = 1;
        }

        /// <summary>A player's levels and challenges, worked out by the server.</summary>
        public class Progress : LevelInfo
        {
            public long Total;
            public Dictionary<string, LevelInfo> Instruments = new();
            public DailyInfo Daily = new();
            public WeeklyInfo Weekly = new();
            public SeasonProgress? Season;
        }

        public class DailyInfo
        {
            public DateTime Ends;
            public List<ChallengeInfo> Items = new();
        }

        public class WeeklyInfo
        {
            public DateTime Ends;
            public int Bonus;
            public bool Done;
            public double Multiplier = 2;
            public List<WeeklySong> Songs = new();
        }

        public class WeeklySong
        {
            public string Shortname = "";
            public bool Done;
        }

        public class SeasonProgress : LevelInfo
        {
            public int Number;
            public string Name = "";
            public string Start = "";
            public string End = "";
            public bool Running;
            public List<ChallengeInfo> Challenges = new();
        }

        public class ChallengeInfo
        {
            public string Id = "";
            public string Label = "";
            public int Xp;
            public string? Reward;
            public int Count = 1;
            public int Progress;
            public bool Done;
            /// <summary>The server's own progress text when it isn't "n of m" (season level, rank).</summary>
            public string? Note;
        }

        /// <summary>The server's answer to the latest run sent this session (null while it's on its way, or if it
        /// couldn't be sent): the score screen shows its XP once it arrives.</summary>
        public static volatile Result? LastResult;

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

        /// <summary>A player's totals across every ranked song: their best run per song and instrument.</summary>
        public class GlobalEntry
        {
            /// <summary>The player's id, for <see cref="GetPlayer"/> (0 from older servers).</summary>
            public long Id;
            public int Rank;
            public string Name = "";
            public string? Avatar;
            public long Total;
            /// <summary>Stars per instrument; Guitar is Lead.</summary>
            public int Guitar, Bass, Drums, Vocals;
            public int Fcs;
            /// <summary>Account level (0 from servers before progression).</summary>
            public int Level;
            /// <summary>Worn looks (null from servers before cosmetics).</summary>
            public Look? Look;
        }

        public class GlobalBoard
        {
            /// <summary>Everyone ranked, not only the entries sent.</summary>
            public int Players;
            public List<GlobalEntry> Entries = new();
            public GlobalEntry? Me;
            /// <summary>Only on the season board: which season it is.</summary>
            public SeasonInfo? Season;
        }

        public class SeasonInfo
        {
            public int Number;
            public string Name = "";
            /// <summary>YYYY-MM-DD, UTC; the end is exclusive.</summary>
            public string Start = "";
            public string End = "";
            /// <summary>False once it ended: the board is then the final standings.</summary>
            public bool Running;
        }

        /// <summary>A title screen jukebox track: links into the CDN's public /jukebox/ folder.</summary>
        public class JukeboxTrack
        {
            public string Title = "";
            public string Artist = "";
            public string Url = "";
            public string? Cover;
            /// <summary>Shortname of the game song this track promotes, if any.</summary>
            public string? Song;
        }

        private class JukeboxList
        {
            public List<JukeboxTrack> Tracks = new();
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

        /// <summary>The signed-in player's Discord picture, as a path for <see cref="GetAvatar"/>; null if none.</summary>
        public static string? PlayerAvatar { get; private set; }

        public enum AccountState { SignedOut, Ok, Banned, Offline }

        /// <summary>How long the game keeps working without reaching the server.</summary>
        public static readonly TimeSpan OfflineWindow = TimeSpan.FromHours(24);

        /// <summary>When the server last confirmed the account, or null if never (or signed out).</summary>
        public static DateTime? LastCheck { get; private set; }
        /// <summary>Whether the account was banned at the last check; kept so going offline doesn't lift a ban.</summary>
        /// <summary>
        /// This build's version for the leaderboard server. Bump it whenever anything a score depends on changes (the
        /// engine, the recording format, the run checks), then raise MIN_CLIENT_VERSION on the server once the release
        /// is out: older builds then get the update screen instead of sending scores.
        /// </summary>
        public const int CLIENT_VERSION = 2; // 2: the pad's 220 ms hit window, overhits as misses, lobby between-songs
        public const string CLIENT_HEADER = "X-LunarRhythm-Client";
        public const string RELEASES_URL = "https://github.com/rhythmmoth/lunarrhythm/releases/";

        /// <summary>The server said this build is too old for scores and lobbies (from the account check or a run).</summary>
        public static bool UpdateRequired { get; private set; }

        public static bool Banned { get; private set; }
        public static string? BanReason { get; private set; }

        /// <summary>
        /// Asks the server about the signed-in account. A token the server no longer knows signs the player out.
        /// The result is saved, so <see cref="Banned"/> and <see cref="LastCheck"/> survive going offline.
        /// </summary>
        public static async Task<AccountState> CheckAccount()
        {
            if (_token == null)
            {
                return AccountState.SignedOut;
            }
            try
            {
                using var response = await Send(HttpMethod.Get, "me", null).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    SaveToken(null);
                    return AccountState.SignedOut;
                }
                if (!response.IsSuccessStatusCode)
                {
                    return Banned ? AccountState.Banned : AccountState.Offline;
                }
                var body = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                Banned = (bool?) body["banned"] == true;
                UpdateRequired = (bool?) body["updateRequired"] == true;
                BanReason = Banned ? (string?) body["banReason"] : null;
                PlayerAvatar = (string?) body["avatar"];
                LastCheck = DateTime.UtcNow;
                SaveToken(_token, PlayerName);
                return Banned ? AccountState.Banned : AccountState.Ok;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                return Banned ? AccountState.Banned : AccountState.Offline;
            }
        }

        /// <summary>Time left to play offline; zero once the window has run out (or the clock was set back).</summary>
        public static TimeSpan OfflineTimeLeft
        {
            get
            {
                if (LastCheck is not { } last || DateTime.UtcNow < last)
                {
                    return TimeSpan.Zero;
                }
                var left = OfflineWindow - (DateTime.UtcNow - last);
                return left > TimeSpan.Zero ? left : TimeSpan.Zero;
            }
        }

        public static void Init(string dataFolder)
        {
            _folder = dataFolder;
            // Lines: token, Discord name, last server check (unix seconds), "banned", avatar path
            // (encrypted on Windows, see TokenFile)
            string file = Path.Combine(dataFolder, TOKEN_FILE);
            var lines = (TokenFile.Read(file) ?? "").Split('\n');
            _token = lines[0].Trim() != "" ? lines[0].Trim() : null;
            PlayerName = _token != null && lines.Length > 1 ? lines[1].TrimEnd('\r') : null;
            LastCheck = _token != null && lines.Length > 2 && long.TryParse(lines[2].Trim(), out long checkedAt)
                ? DateTimeOffset.FromUnixTimeSeconds(checkedAt).UtcDateTime
                : null;
            Banned = _token != null && lines.Length > 3 && lines[3].Trim() == "banned";
            PlayerAvatar = _token != null && lines.Length > 4 && lines[4].Trim() != "" ? lines[4].Trim() : null;
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
            LastResult = null;
            if (_token == null)
            {
                return null;
            }
            try
            {
                LastResult = await Post(run).ConfigureAwait(false);
                if (LastResult?.Reason == "update_required")
                {
                    UpdateRequired = true;
                }
                return LastResult;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                YargLogger.LogWarning($"Leaderboard unreachable, run kept for later ({ex.Message})");
                lock (QueueLock)
                {
                    var queue = ReadQueue();
                    run.QueuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
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
                    if (KeepTrying(run, DateTimeOffset.UtcNow)) failed.Add(run);
                    else YargLogger.LogWarning($"Gave up sending a kept run ({run.Shortname} {run.Instrument}) after {run.Tries} tries");
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

        /// <summary>The top of the global leaderboard. Null if the server couldn't be reached.</summary>
        /// <summary>The running season's board, or the last one's final standings; null if there's never been a season
        /// (or the server can't be reached).</summary>
        public static Task<GlobalBoard?> GetSeason(int limit) => GetBoard($"season?limit={limit}");

        public static Task<GlobalBoard?> GetGlobal(int limit) => GetBoard($"global?limit={limit}");

        /// <summary>The signed-in player's profile; null signed out, offline or from an older server. Asking also saves
        /// any looks the player has newly earned.</summary>
        public static async Task<Profile?> GetProfile()
        {
            try
            {
                using var response = await Send(HttpMethod.Get, "profile", null).ConfigureAwait(false);
                return response.IsSuccessStatusCode
                    ? JsonConvert.DeserializeObject<Profile>(await response.Content.ReadAsStringAsync().ConfigureAwait(false))
                    : null;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                return null;
            }
        }

        /// <summary>Another player's profile overview (no catalog, unlocks or XP numbers); null if they can't be shown,
        /// signed out, offline or from an older server.</summary>
        public static async Task<Profile?> GetPlayer(long id)
        {
            try
            {
                using var response = await Send(HttpMethod.Get, $"players/{id}", null).ConfigureAwait(false);
                return response.IsSuccessStatusCode
                    ? JsonConvert.DeserializeObject<Profile>(await response.Content.ReadAsStringAsync().ConfigureAwait(false))
                    : null;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                return null;
            }
        }

        /// <summary>Wears a look (null: the starting look for that type). False if it's locked or the server can't be reached.</summary>
        public static async Task<bool> Equip(string type, string? id)
        {
            try
            {
                using var response = await Send(HttpMethod.Post, "equip", JsonConvert.SerializeObject(new { type, id })).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return false;
                }
                var answer = JsonConvert.DeserializeAnonymousType(await response.Content.ReadAsStringAsync().ConfigureAwait(false),
                    new { equipped = false });
                return answer?.equipped == true;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                return false;
            }
        }

        /// <summary>The signed-in player's levels and challenges; null signed out, offline or from an older server.</summary>
        public static async Task<Progress?> GetProgress()
        {
            try
            {
                using var response = await Send(HttpMethod.Get, "progress", null).ConfigureAwait(false);
                return response.IsSuccessStatusCode
                    ? JsonConvert.DeserializeObject<Progress>(await response.Content.ReadAsStringAsync().ConfigureAwait(false))
                    : null;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                return null;
            }
        }

        private static async Task<GlobalBoard?> GetBoard(string path)
        {
            try
            {
                using var response = await Send(HttpMethod.Get, path, null).ConfigureAwait(false);
                return response.IsSuccessStatusCode
                    ? JsonConvert.DeserializeObject<GlobalBoard>(await response.Content.ReadAsStringAsync().ConfigureAwait(false))
                    : null;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                return null;
            }
        }

        /// <summary>The jukebox for the player's environment. Null when signed out, banned or offline.</summary>
        public static async Task<List<JukeboxTrack>?> GetJukebox()
        {
            if (_token == null)
            {
                return null;
            }
            try
            {
                using var response = await Send(HttpMethod.Get, "jukebox", null).ConfigureAwait(false);
                return response.IsSuccessStatusCode
                    ? JsonConvert.DeserializeObject<JukeboxList>(await response.Content.ReadAsStringAsync().ConfigureAwait(false))?.Tracks
                    : null;
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
                recording = run.Recording,
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
            request.Headers.Add(CLIENT_HEADER, CLIENT_VERSION.ToString());
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
            if (token != _token)
            {
                // A different account (or none): nothing carries over from the last one's checks
                LastCheck = string.IsNullOrEmpty(token) ? null : DateTime.UtcNow; // a sign-in just reached the server
                Banned = false;
                BanReason = null;
                PlayerAvatar = null;
            }
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
                long checkedAt = LastCheck is { } t ? new DateTimeOffset(t, TimeSpan.Zero).ToUnixTimeSeconds() : 0;
                TokenFile.Write(file, $"{_token}\n{PlayerName}\n{checkedAt}\n{(Banned ? "banned" : "")}\n{PlayerAvatar}");
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
