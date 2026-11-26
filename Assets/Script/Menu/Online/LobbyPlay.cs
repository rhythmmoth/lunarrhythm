using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using YARG.Core;
using YARG.Core.Game;
using YARG.Core.Song;
using YARG.Helpers;
using YARG.Player;
using YARG.Song;

namespace YARG.Menu.Online
{
    /// <summary>
    /// Ties the online lobby (YARG.Core's <see cref="Lobby"/>) to the game: which local player is in it, finding a
    /// setlist song in the library, and launching it. Only the first player who isn't a bot plays in a lobby.
    /// </summary>
    public static class LobbyPlay
    {
        /// <summary>True from launching a lobby song until the menu is back; the game and score screen check it.</summary>
        public static bool Playing { get; private set; }

        /// <summary>A setlist ran since the summary was last closed, so the lobby shows it.</summary>
        public static bool SummaryPending { get; set; }

        /// <summary>Which song of the setlist this game last loaded or sat out (-1: none), so it's done once.</summary>
        public static int Handled { get; set; } = -1;

        public static YargPlayer Me => PlayerContainer.Players.FirstOrDefault(p => !p.Profile.IsBot);

        // The part the player chose (what the lobby shows). A song may be played on a twin part or the nearest
        // difficulty it has; that's set on the profile for the song only, and the chosen part comes back after.
        private static Instrument? _chosenInstrument;
        private static Difficulty? _chosenDifficulty;

        // Loading the current song: what was last reported, and whether "loaded" went out (the heartbeat stops)
        private static int _progress;
        private static bool _loadedSent;

        public static Instrument[] Instruments(YargProfile profile) => profile.GameMode.PossibleInstruments();

        public static Difficulty[] Difficulties(Instrument instrument) => Enum.GetValues(typeof(Difficulty)).Cast<Difficulty>()
            .Where(d => d != Difficulty.Beginner && !(instrument is Instrument.Vocals or Instrument.Harmony && d == Difficulty.ExpertPlus))
            .ToArray();

        private static System.Collections.Generic.Dictionary<string, SongEntry> _byShortname;
        private static object _indexedFrom;

        /// <summary>The library's copy of an online song, by the shortname the server uses. Indexed once per library
        /// (telling an online folder apart checks the disk), since the lobby redraws on every loading step.</summary>
        public static SongEntry Find(string shortname)
        {
            if (shortname == null) return null;
            var songs = SongContainer.Songs;
            if (_byShortname == null || !ReferenceEquals(_indexedFrom, songs))
            {
                _byShortname = new();
                foreach (var song in songs)
                {
                    if (OnlineSongs.ShortnameOf(song.ActualLocation) is { } name) _byShortname[name] = song;
                }
                _indexedFrom = songs;
            }
            return _byShortname.TryGetValue(shortname, out var found) ? found : null;
        }

        /// <summary>Sends the player's instrument and difficulty from their profile (the lobby shows them).</summary>
        public static void SendPart()
        {
            if (Me is not { } me) return;
            var profile = me.Profile;
            if (!Instruments(profile).Contains(profile.CurrentInstrument))
            {
                profile.CurrentInstrument = Instruments(profile)[0];
            }
            var allowed = Difficulties(profile.CurrentInstrument);
            if (!allowed.Contains(profile.CurrentDifficulty))
            {
                // The nearest the lobby offers (not Expert: a Beginner player wants Easy)
                profile.CurrentDifficulty = allowed.OrderBy(d => Math.Abs((int) d - (int) profile.CurrentDifficulty)).First();
            }
            // Also the difficulty solo play falls back to, so it's still this one after the lobby
            profile.DifficultyFallback = profile.CurrentDifficulty;
            _chosenInstrument = profile.CurrentInstrument;
            _chosenDifficulty = profile.CurrentDifficulty;
            _ = Lobby.Part(profile.CurrentInstrument.ToString(), profile.CurrentDifficulty.ToString());
        }

        /// <summary>The instrument to play the song on for a lobby part, or null: the part itself, else its twin
        /// (4-lane and Pro drums, solo vocals and harmonies are the same part, charted either way).</summary>
        public static Instrument? PlayableAs(SongEntry song, Instrument part)
        {
            if (song == null) return null;
            if (song.HasInstrument(part)) return part;
            Instrument? twin = part switch
            {
                Instrument.FourLaneDrums => Instrument.ProDrums,
                Instrument.ProDrums => Instrument.FourLaneDrums,
                Instrument.Vocals => Instrument.Harmony,
                Instrument.Harmony => Instrument.Vocals,
                _ => null,
            };
            return twin is { } t && song.HasInstrument(t) ? t : null;
        }

        public static bool Has(SongEntry song, string part) =>
            !Enum.TryParse(part, out Instrument instrument) || PlayableAs(song, instrument) != null;

        /// <summary>Sets the profile to play this song: the part (or its twin) at the chosen difficulty, or the
        /// nearest one the chart has. False: the song has no such part, so the player sits it out.</summary>
        private static bool Prepare(SongEntry song, YargProfile profile)
        {
            if (PlayableAs(song, profile.CurrentInstrument) is not { } instrument) return false;
            var part = song[instrument];
            var wanted = profile.CurrentDifficulty;
            var available = Enum.GetValues(typeof(Difficulty)).Cast<Difficulty>().Where(d => part[d]).ToList();
            if (available.Count == 0) return false;
            profile.CurrentInstrument = instrument;
            profile.CurrentDifficulty = available.Contains(wanted) ? wanted
                : available.OrderBy(d => Math.Abs((int) d - (int) wanted)).ThenByDescending(d => d).First();
            return true;
        }

        /// <summary>
        /// The sync step for one song: downloads the audio, then loads the game scene, which reports "loaded" and waits
        /// for the shared start (see <see cref="WaitForStart"/>). A song without the player's part is sat out.
        /// </summary>
        public static async UniTaskVoid Sync(int index, string shortname)
        {
            Handled = index;
            SummaryPending = true;
            _loadedSent = false;
            _ = Lobby.SyncClock(); // a sharp clock for this song's shared start
            var me = Me;
            var song = Find(shortname);
            if (me == null || !Prepare(song, me.Profile))
            {
                _loadedSent = true;
                _ = Lobby.Loaded(true, 0);
                return;
            }

            Report(10);
            Heartbeat(index).Forget();
            if (OnlineSongs.IsOnline(PathHelper.OnlineSongsPath, song) && !OnlineSongs.StemsReady(song.ActualLocation))
            {
                bool ok = await UniTask.RunOnThreadPool(() => OnlineSongs.FetchStems(song.ActualLocation));
                if (!ok)
                {
                    // Can't get it: sit this one out rather than hold everyone
                    _loadedSent = true;
                    _ = Lobby.Loaded(true, 0);
                    Persistent.ToastManager.ToastWarning("Couldn't download this song, so you're sitting it out.");
                    return;
                }
            }
            if (Lobby.State?.Phase != "sync" || Lobby.State.Song != index)
            {
                return; // dropped, or the lobby moved on while downloading
            }
            Report(60);

            foreach (var player in PlayerContainer.Players)
            {
                player.SittingOut = player != me;
            }
            GlobalVariables.State.CurrentSong = song;
            GlobalVariables.State.SongSpeed = 1f;
            GlobalVariables.State.PlayingAShow = false;
            Playing = true;
            GlobalVariables.Instance.LoadScene(SceneIndex.Gameplay);
        }

        /// <summary>
        /// Called by the game once everything is loaded: tells the lobby (with this player's setup, for the others'
        /// copy of their highway), then waits for the shared start time. allLoaded runs once when everyone has loaded,
        /// during the countdown, so the others' highways are in place before the start.
        /// False: the lobby is gone or went on without this player, so the game should go back to the menu.
        /// </summary>
        public static async UniTask<bool> WaitForStart(double lengthSeconds, string setup, Action allLoaded, Action<string> status)
        {
            int index = Lobby.State?.Song ?? -1;
            _loadedSent = true;
            await Lobby.Loaded(false, lengthSeconds, setup);
            bool added = false;
            while (true)
            {
                var state = Lobby.State;
                if (state == null || state.Song != index || state.Me is not { } me || me.Dropped)
                {
                    return false;
                }
                if (state.Phase == "playing" && state.StartAt is { } at)
                {
                    if (!added)
                    {
                        added = true;
                        allLoaded();
                    }
                    long left = at - Lobby.ServerNowMs;
                    if (left <= 0)
                    {
                        return true;
                    }
                    status($"Starting in {Math.Ceiling(left / 1000.0):0}");
                }
                else
                {
                    int waiting = state.Players.Count(p => !p.Loaded);
                    status(waiting == 1 ? "Waiting for 1 player" : $"Waiting for {waiting} players");
                }
                await UniTask.NextFrame();
            }
        }

        /// <summary>The game's end: the run goes to the lobby (display only; leaderboards get it separately).</summary>
        public static void Finished(int score, int stars, bool fc)
        {
            if (Playing)
            {
                _ = Lobby.Result(score, stars, fc);
            }
        }

        /// <summary>Back in the menu (score screen continue, a quit or a failed load): the chosen part comes back,
        /// in case the song was played on a twin part or another difficulty.</summary>
        public static void Returned()
        {
            Playing = false;
            if (Me is { } me && _chosenInstrument is { } instrument && _chosenDifficulty is { } difficulty)
            {
                me.Profile.CurrentInstrument = instrument;
                me.Profile.CurrentDifficulty = difficulty;
            }
        }

        private static void Report(int percent)
        {
            _progress = percent;
            _ = Lobby.Progress(percent);
        }

        /// <summary>While the song downloads and loads, tells the lobby it's still going every few seconds: a long
        /// download isn't mistaken for a stalled one.</summary>
        private static async UniTaskVoid Heartbeat(int index)
        {
            while (!_loadedSent && Lobby.State is { Phase: "sync" } state && state.Song == index)
            {
                await UniTask.Delay(5000, ignoreTimeScale: true);
                if (!_loadedSent) _ = Lobby.Progress(_progress);
            }
        }

        /// <summary>The menu opens the open lobbies next (a lobby was left mid-song, still online).</summary>
        public static bool BackToList { get; set; }

        /// <summary>Leaves the lobby but stays online, for the open lobbies to show next; offline, it's
        /// <see cref="Leave"/>.</summary>
        public static void LeaveToList()
        {
            if (!Lobby.Connected)
            {
                Leave();
                return;
            }
            Returned();
            SummaryPending = false;
            Handled = -1;
            BackToList = true;
            _ = Lobby.Leave();
        }

        /// <summary>Leaves the lobby for good, from anywhere.</summary>
        public static void Leave()
        {
            Returned();
            SummaryPending = false;
            Handled = -1;
            Lobby.Disconnect();
        }
    }
}
