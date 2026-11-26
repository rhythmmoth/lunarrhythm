using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using YARG.Core.Input;
using YARG.Core.IO;
using YARG.Core.Replays;
using YARG.Core.Logging;

namespace YARG.Core.Song
{
    /// <summary>
    /// The online lobby: one WebSocket to the leaderboard server's /lb/lobby (leaderboard-server/Lobbies.cs). The server
    /// owns the lobby; the game sends what the player does and reads <see cref="State"/>, which is replaced on every
    /// change. Menus poll it each frame, like the other online state.
    /// </summary>
    public static class Lobby
    {
        public const int MAX_PLAYERS = 4;
        private static readonly TimeSpan PingEvery = TimeSpan.FromSeconds(5);

        /// <summary>How long the game keeps trying to get back into its lobby after the connection breaks.</summary>
        public static readonly TimeSpan ReconnectFor = TimeSpan.FromSeconds(25);

        public class Player
        {
            public long Id;
            public string Name = "";
            public int Level;
            public string? Alias, Frame, Color, Avatar;
            public string? Instrument, Difficulty, Pick;
            public bool Ready, Loaded, SitsOut, Dropped;
            /// <summary>Their connection broke; the server keeps their place for a while to reconnect.</summary>
            public bool Away;
            public int Progress;
        }

        /// <summary>A public lobby still picking songs, from <see cref="List"/>.</summary>
        public class OpenLobby
        {
            public string Code = "";
            public Player Host = new();
            public List<string> Players = new();
        }

        public class PlayerResult
        {
            public long Id;
            public int Score, Stars;
            public bool Fc;
        }

        public class SetlistSong
        {
            public string Song = "";
            public long By;
            public List<PlayerResult> Results = new();
        }

        public class LobbyState
        {
            public long You;
            public string Code = "";
            public bool Private;
            /// <summary>lobby (picking and readying), sync (the current song loading), playing, between (after a song:
            /// results, and parts can change before the next one).</summary>
            public string Phase = "lobby";
            public long Host;
            public List<Player> Players = new();
            public List<SetlistSong> Setlist = new();
            public int Song;
            /// <summary>Server time (Unix ms) the current song starts for everyone; set while playing.</summary>
            public long? StartAt;
            public long? Deadline;
            /// <summary>The setlist that just finished, for the summary; null until one has.</summary>
            public List<SetlistSong>? Last;

            public Player? Me => Players.FirstOrDefault(p => p.Id == You);
            public bool IsHost => Host == You;
            public SetlistSong? Current => Phase != "lobby" && Song < Setlist.Count ? Setlist[Song] : null;
            public Player? ById(long id) => Players.FirstOrDefault(p => p.Id == id);
        }

        /// <summary>Another player's button presses for the current song, for drawing their highway.</summary>
        public class RemoteFeed
        {
            public readonly int Song;
            public readonly ConcurrentQueue<GameInput> Inputs = new();
            private long _watermarkBits = BitConverter.DoubleToInt64Bits(double.NegativeInfinity);

            public RemoteFeed(int song) => Song = song;

            /// <summary>How far (seconds of song) the player has sent everything; their highway can't run past it.</summary>
            public double Watermark => BitConverter.Int64BitsToDouble(Interlocked.Read(ref _watermarkBits));

            internal void Advance(double to)
            {
                if (to > Watermark) Interlocked.Exchange(ref _watermarkBits, BitConverter.DoubleToInt64Bits(to));
            }
        }

        /// <summary>Each other player's setup (a replay frame without inputs, base64) and the song it's for.</summary>
        public static readonly ConcurrentDictionary<long, (int Song, string Setup)> Setups = new();

        /// <summary>Each other player's incoming button presses. A new song replaces the feed.</summary>
        public static readonly ConcurrentDictionary<long, RemoteFeed> Feeds = new();

        /// <summary>The feed for a player and song, created on first use so presses arriving early aren't lost.</summary>
        public static RemoteFeed Feed(long player, int song) =>
            Feeds.AddOrUpdate(player, _ => new RemoteFeed(song), (_, f) => f.Song == song ? f : new RemoteFeed(song));

        /// <summary>A player's replay frame without its inputs, as base64, for the others to set up their highway.</summary>
        public static string EncodeSetup(ReplayFrame frame)
        {
            string Write(ReplayFrame f)
            {
                using var memory = new System.IO.MemoryStream();
                using (var writer = new System.IO.BinaryWriter(memory, Encoding.UTF8, true))
                {
                    new ReplayFrame(f.Profile, f.EngineParameters, f.Stats, Array.Empty<GameInput>()).Serialize(writer);
                }
                return Convert.ToBase64String(memory.ToArray());
            }
            // The others show the account name; the local profile's own name stays on this PC (read back as a copy)
            var copy = DecodeSetup(Write(frame)) ?? throw new InvalidOperationException("setup didn't read back");
            copy.Profile.Name = "";
            return Write(copy);
        }

        /// <summary>Reads a setup from <see cref="EncodeSetup"/>; null if it isn't one (it comes from another player).</summary>
        public static ReplayFrame? DecodeSetup(string setup)
        {
            try
            {
                using var memory = new System.IO.MemoryStream(Convert.FromBase64String(setup));
                using var data = FixedArray.ReadRemainder(memory);
                var stream = data.ToValueStream();
                return new ReplayFrame(ref stream, ReplayIO.REPLAY_VERSIONS.CURRENT);
            }
            catch (Exception e)
            {
                YargLogger.LogWarning($"Bad lobby setup: {e.Message}");
                return null;
            }
        }

        /// <summary>The lobby the player is in, or null. Replaced (never changed in place) on every update.</summary>
        public static volatile LobbyState? State;

        /// <summary>The server's last refusal ("no_lobby", "lobby_full"...), cleared by <see cref="TakeError"/>.</summary>
        private static string? _error;

        /// <summary>Why the connection ended ("replaced", "idle", "lost"...), or null while connected or never connected.</summary>
        public static volatile string? Closed;

        public static bool Connected => _socket?.State == WebSocketState.Open;

        /// <summary>The connection broke while in a lobby and the game is getting back in; <see cref="State"/> is kept
        /// meanwhile, so a song carries on.</summary>
        public static volatile bool Reconnecting;

        /// <summary>The open lobbies from the last <see cref="List"/>, or null before one.</summary>
        public static volatile List<OpenLobby>? Open;

        /// <summary>Server clock minus ours, in ms, from the last ping's round trip.</summary>
        public static long ClockOffsetMs => Interlocked.Read(ref _offsetMs);

        public static long ServerNowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + ClockOffsetMs;

        private static ClientWebSocket? _socket;
        private static CancellationTokenSource? _cancel;
        private static readonly SemaphoreSlim SendLock = new(1, 1);
        private static long _offsetMs;
        private static long _pingSentMs;
        private static long _pingId;
        private static int _generation;
        private static readonly ConcurrentDictionary<long, long> PingsSent = new();
        // (round trip, offset) of the latest pings: the offset of the fastest is the truest
        private static readonly List<(long RoundTrip, long Offset)> ClockSamples = new();

        public static string? TakeError() => Interlocked.Exchange(ref _error, null);

        /// <summary>Opens the connection (signed-in players only). False if the server can't be reached.</summary>
        public static Task<bool> Connect() => Connected ? Task.FromResult(true) : OpenSocket(false);

        private static async Task<bool> OpenSocket(bool keepState)
        {
            if (Leaderboards.Token is not { } token)
            {
                return false;
            }
            if (!keepState)
            {
                Disconnect();
            }
            int generation = _generation;
            var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
            socket.Options.SetRequestHeader(Leaderboards.CLIENT_HEADER, Leaderboards.CLIENT_VERSION.ToString());
            var uri = new Uri(Leaderboards.BaseUrl.Replace("https://", "wss://").Replace("http://", "ws://") + "lobby");
            if (OnlineSongs.ApiKey is { Length: > 0 } key && OnlineSongs.IsKeyHost(new Uri(Leaderboards.BaseUrl)))
            {
                socket.Options.SetRequestHeader(OnlineSongs.API_KEY_HEADER, key);
            }
            var cancel = new CancellationTokenSource();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await socket.ConnectAsync(uri, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                YargLogger.LogWarning($"Lobby connection failed: {e.Message}");
                socket.Dispose();
                return false;
            }
            if (generation != _generation)
            {
                socket.Dispose(); // left while connecting
                return false;
            }
            _socket = socket;
            _cancel?.Cancel(); // a reconnect: the old connection's pings stop
            _cancel = cancel;
            Closed = null;
            _ = Task.Run(() => Receive(socket, cancel.Token));
            _ = Task.Run(() => KeepAlive(cancel.Token));
            return true;
        }

        /// <summary>The connection broke mid-lobby: tries to get back in for <see cref="ReconnectFor"/>, keeping the
        /// lobby state (the server gives the place back). Gives up as an ordinary lost connection.</summary>
        private static async Task Reconnect(string reason)
        {
            int generation = _generation;
            Reconnecting = true;
            var until = DateTime.UtcNow + ReconnectFor;
            while (DateTime.UtcNow < until && generation == _generation)
            {
                await Task.Delay(1500).ConfigureAwait(false);
                if (generation == _generation && await OpenSocket(true).ConfigureAwait(false))
                {
                    Reconnecting = false;
                    return;
                }
            }
            if (generation == _generation)
            {
                Reconnecting = false;
                Closed = reason;
                State = null;
                Setups.Clear();
                Feeds.Clear();
            }
        }

        /// <summary>Leaves any lobby and closes the connection.</summary>
        public static void Disconnect()
        {
            Interlocked.Increment(ref _generation); // stops a reconnect in progress
            Reconnecting = false;
            var socket = Interlocked.Exchange(ref _socket, null);
            _cancel?.Cancel();
            _cancel = null;
            State = null;
            Setups.Clear();
            Feeds.Clear();
            if (socket != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", timeout.Token).ConfigureAwait(false);
                    }
                    catch (Exception) { /* closing anyway */ }
                    socket.Dispose();
                });
            }
        }

        // ---------- what the player does ----------

        public static Task Create(bool isPrivate = false) => Send(new { t = "create", @private = isPrivate });
        /// <summary>Asks for the open lobbies; the answer lands in <see cref="Open"/>.</summary>
        public static Task List() => Send(new { t = "list" });
        public static Task Join(string code) => Send(new { t = "join", code });
        public static Task Leave() => Send(new { t = "leave" });
        public static Task Part(string instrument, string difficulty) => Send(new { t = "part", instrument, difficulty });
        /// <summary>Picks a song by its online shortname; null removes the pick.</summary>
        public static Task Pick(string? song) => Send(new { t = "pick", song });
        public static Task Ready(bool on) => Send(new { t = "ready", on });
        public static Task Progress(int percent) => Send(new { t = "progress", value = percent });
        /// <summary>Loaded and ready to start. sitOut: the chart has no part for the player's instrument.</summary>
        /// setup: this player's replay frame without inputs (base64), so the others can draw their highway.
        public static Task Loaded(bool sitOut, double lengthSeconds, string? setup = null) =>
            Send(new { t = "loaded", sitOut, length = (int) Math.Min(lengthSeconds * 1000, int.MaxValue), setup });

        /// <summary>Sends the button presses inputs[from..] and how far the song has got (everything before it is sent).</summary>
        public static async Task SendInputs(IReadOnlyList<GameInput> inputs, int from, double watermark)
        {
            const int perMessage = 150; // the server takes 200 at most
            do
            {
                int count = Math.Min(perMessage, inputs.Count - from);
                var list = new object[Math.Max(count, 0)];
                for (int k = 0; k < list.Length; k++)
                {
                    var input = inputs[from + k];
                    list[k] = new object[] { input.Time, input.Action, input.Integer };
                }
                from += list.Length;
                // The watermark only goes out with the last batch, once nothing earlier is left to send
                double w = from >= inputs.Count ? watermark : inputs[from].Time;
                await Send(new { t = "in", w, i = list }).ConfigureAwait(false);
            } while (from < inputs.Count);
        }
        public static Task Result(int score, int stars, bool fc) => Send(new { t = "result", score, stars, fc });

        private static async Task Send(object message)
        {
            var socket = _socket;
            if (socket?.State != WebSocketState.Open)
            {
                return;
            }
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message));
            await SendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                YargLogger.LogWarning($"Lobby send failed: {e.Message}");
            }
            finally
            {
                SendLock.Release();
            }
        }

        // ---------- what the server says ----------

        private static async Task KeepAlive(CancellationToken cancel)
        {
            try
            {
                // A few quick pings first, so the clock is good from the start
                for (int i = 0; i < 4 && !cancel.IsCancellationRequested; i++)
                {
                    await Ping().ConfigureAwait(false);
                    await Task.Delay(150, cancel).ConfigureAwait(false);
                }
                while (!cancel.IsCancellationRequested)
                {
                    await Ping().ConfigureAwait(false);
                    await Task.Delay(PingEvery, cancel).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
        }

        /// <summary>A few pings in quick succession, to sharpen the clock right before a song starts.</summary>
        public static async Task SyncClock()
        {
            for (int i = 0; i < 4; i++)
            {
                await Ping().ConfigureAwait(false);
                await Task.Delay(120).ConfigureAwait(false);
            }
        }

        private static Task Ping()
        {
            long id = Interlocked.Increment(ref _pingId);
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            PingsSent[id] = now;
            Interlocked.Exchange(ref _pingSentMs, now);
            return Send(new { t = "ping", id });
        }

        /// <summary>One ping's answer: the server read its clock about halfway through the round trip. The offset
        /// kept is the one from the fastest of the latest pings, which a slow trip can't skew. Internal for tests.</summary>
        internal static void AddClockSample(long sentMs, long receivedMs, long serverNowMs)
        {
            lock (ClockSamples)
            {
                ClockSamples.Add((receivedMs - sentMs, serverNowMs - (sentMs + receivedMs) / 2));
                if (ClockSamples.Count > 12)
                {
                    ClockSamples.RemoveAt(0);
                }
                Interlocked.Exchange(ref _offsetMs, ClockSamples.OrderBy(c => c.RoundTrip).First().Offset);
            }
        }

        private static async Task Receive(ClientWebSocket socket, CancellationToken cancel)
        {
            var buffer = new byte[64 * 1024];
            string reason = "lost";
            try
            {
                while (!cancel.IsCancellationRequested)
                {
                    int length = 0;
                    WebSocketReceiveResult r;
                    do
                    {
                        if (length == buffer.Length)
                        {
                            Array.Resize(ref buffer, buffer.Length * 2);
                        }
                        r = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length), cancel)
                            .ConfigureAwait(false);
                        length += r.Count;
                    } while (!r.EndOfMessage);
                    if (r.MessageType == WebSocketMessageType.Close)
                    {
                        reason = string.IsNullOrEmpty(socket.CloseStatusDescription) ? "lost" : socket.CloseStatusDescription!;
                        break;
                    }
                    Apply(Encoding.UTF8.GetString(buffer, 0, length), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                }
            }
            catch (OperationCanceledException)
            {
                return; // we closed it
            }
            catch (Exception e)
            {
                YargLogger.LogWarning($"Lobby connection lost: {e.Message}");
            }
            if (_socket == socket)
            {
                _socket = null;
                // A break mid-lobby (not the server sending us away) is worth getting back in for
                if (State != null && reason is "lost" or "idle" or "slow")
                {
                    _ = Task.Run(() => Reconnect(reason));
                    return;
                }
                Closed = reason;
                State = null;
            }
        }

        /// <summary>Applies one server message. Internal for tests.</summary>
        internal static void Apply(string json, long receivedMs)
        {
            JObject msg;
            try
            {
                msg = JObject.Parse(json);
            }
            catch (JsonException)
            {
                return;
            }
            switch ((string?) msg["t"])
            {
                case "pong":
                    if ((long?) msg["now"] is { } now)
                    {
                        // Matched by id; an older server sends none, so the last ping is the best guess
                        long sent = (long?) msg["id"] is { } id && PingsSent.TryRemove(id, out long at) ? at : Interlocked.Read(ref _pingSentMs);
                        if (sent > 0)
                        {
                            AddClockSample(sent, receivedMs, now);
                        }
                    }
                    break;
                case "lobbies":
                    Open = msg["list"]?.ToObject<List<OpenLobby>>() ?? new List<OpenLobby>();
                    break;
                case "state":
                    State = msg.ToObject<LobbyState>();
                    break;
                case "setup":
                    if ((long?) msg["from"] is { } who && (int?) msg["song"] is { } song && (string?) msg["setup"] is { } setup)
                    {
                        Setups[who] = (song, setup);
                    }
                    break;
                case "in":
                    if ((long?) msg["from"] is { } sender && (int?) msg["song"] is { } index && msg["i"] is JArray presses)
                    {
                        var feed = Feed(sender, index);
                        foreach (var p in presses)
                        {
                            if (p is JArray { Count: 3 } a)
                            {
                                feed.Inputs.Enqueue(new GameInput((double) a[0], (int) a[1], (int) a[2]));
                            }
                        }
                        if ((double?) msg["w"] is { } w) feed.Advance(w);
                    }
                    break;
                case "left":
                    State = null;
                    break;
                case "error":
                    _error = (string?) msg["reason"];
                    break;
            }
        }
    }
}
