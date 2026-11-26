using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using Discord;
using UnityEngine;
using YARG.Core;
using YARG.Core.Logging;
using YARG.Core.Song;
using YARG.Gameplay;
using YARG.Menu;
using YARG.Settings;

namespace YARG.Integration
{
    public enum DiscordRichPresenceMode
    {
        Show,
        Limited,
        Hide
    }

    /// <summary>
    /// Discord Rich Presence through the LunarRhythm Discord app (board NextPresence): "Playing LunarRhythm" with the
    /// menus (level and season level), picking a song, the song being played (its cover, part, difficulty, time left),
    /// an online lobby (party size, and Ask to Join straight into it) and the result just set. "Limited" shows only
    /// "In the menus".
    /// </summary>
    public class DiscordController : MonoSingleton<DiscordController>
    {
        // The LunarRhythm app (the one the leaderboard's sign-in and the bot use); its art assets hold the icons
        private const long APPLICATION_ID = 1567204379675721900;

#if UNITY_EDITOR || YARG_TEST_BUILD
        private const string ICON = "icon_dev";
        private const string ICON_TEXT = "LunarRhythm (dev build)";
#elif YARG_NIGHTLY_BUILD
        private const string ICON = "icon_nightly";
        private const string ICON_TEXT = "LunarRhythm (nightly)";
#else
        private const string ICON = "icon_stable";
        private const string ICON_TEXT = "LunarRhythm";
#endif

        // Discord takes about 5 updates per 20 s: look every 2 s, send only changes, at most one every 4 s
        private const float LOOK_EVERY = 2f;
        private const float SEND_EVERY = 4f;
        private const int LOBBY_SIZE = 4;

        private bool _initialized;
        private Discord.Discord _discord;
        private long _gameStart;
        private float _nextLook, _lastSent = -100f;
        private string _sentKey;

        // The cover of the song being played or just finished (a short-lived signed link), fetched once per song
        private SongEntry _coverSong;
        private string _cover;

        public void Initialize()
        {
            _initialized = true;
            CreateInstance();
        }

        public void CreateInstance()
        {
            // Localization and settings are loaded by then; nothing when presence is off or already running
            if (!_initialized || SettingsManager.Settings.DiscordRichPresence.Value == DiscordRichPresenceMode.Hide || _discord != null)
            {
                return;
            }

            try
            {
                _discord = new Discord.Discord(APPLICATION_ID, (ulong) CreateFlags.NoRequireDiscord);
            }
            catch (ResultException e) when (e.Result == Result.InternalError)
            {
                YargLogger.LogWarning("Failed to start Discord presence client: Discord is not running or unreachable.");
                _discord = null;
                return;
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Failed to start Discord presence client.");
                _discord = null;
                return;
            }

            var activities = _discord.GetActivityManager();
            // A friend accepted our Ask to Join: the secret is their lobby code
            activities.OnActivityJoin += Menu.Online.LobbyMenu.JoinFromDiscord;
#if !UNITY_EDITOR
            // So Discord can start the game for a friend who isn't running it yet
            try
            {
                activities.RegisterCommand($"\"{System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName}\"");
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Couldn't register LunarRhythm's launch command with Discord.");
            }
#endif
            _gameStart = DateTimeOffset.Now.ToUnixTimeSeconds();
            _sentKey = null;
            _nextLook = 0f;
        }

        private void Update()
        {
            if (_discord is null)
            {
                return;
            }

            try
            {
                _discord.RunCallbacks();
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Failed to run Discord client callbacks!");
                TryDispose();
                return;
            }

            if (Time.unscaledTime < _nextLook)
            {
                return;
            }
            _nextLook = Time.unscaledTime + LOOK_EVERY;
            var (activity, key) = Describe();
            if (key != _sentKey && Time.unscaledTime - _lastSent >= SEND_EVERY)
            {
                _sentKey = key;
                _lastSent = Time.unscaledTime;
                SetActivity(activity);
            }
        }

        /// <summary>What to show right now, and a key that changes whenever it does (the clock aside).</summary>
        private (Activity Activity, string Key) Describe()
        {
            var activity = new Activity { Assets = { LargeImage = ICON, LargeText = ICON_TEXT } };
            bool limited = SettingsManager.Settings.DiscordRichPresence.Value == DiscordRichPresenceMode.Limited;
            var scene = GlobalVariables.Instance.CurrentScene;
            var lobby = limited ? null : YARG.Core.Song.Lobby.State;

            if (!limited && scene == SceneIndex.Gameplay && FindAnyObjectByType<GameManager>() is { Song: { } song } game)
            {
                WithCover(ref activity, song);
                activity.Details = $"{song.Name} · {song.Artist}";
                var me = game.YargPlayers?.FirstOrDefault(p => !p.IsRemote && !p.SittingOut && !p.Profile.IsBot);
                bool paused = game.IsSongStarted && game.Paused; // the song's clock only exists once it has loaded
                activity.State = paused ? "Paused"
                    : me != null ? $"{PartName(me.Profile.CurrentInstrument)} · {me.Profile.CurrentDifficulty}" : "Playing";
                if (!paused && game.IsSongStarted)
                {
                    double left = Math.Max(0, game.SongLength - game.SongTime);
                    activity.Timestamps.End = DateTimeOffset.Now.ToUnixTimeSeconds() + (long) left;
                }
            }
            else if (!limited && scene == SceneIndex.Score && GlobalVariables.State.CurrentSong is { } finished)
            {
                WithCover(ref activity, finished);
                activity.Details = $"Finished {finished.Name}";
                // Cards are structs: cast so "no human player" is null, not an empty card
                var card = GlobalVariables.State.ScoreScreenStats?.PlayerScores?
                    .Where(p => !p.Player.Profile.IsBot).Select(p => (YARG.Menu.ScoreScreen.PlayerScoreCard?) p).FirstOrDefault();
                if (card is { } c)
                {
                    int stars = Mathf.Clamp(Mathf.FloorToInt(c.Stats.Stars), 0, 6);
                    activity.State = string.Join(" · ", new[]
                    {
                        stars == 1 ? "1 star" : $"{stars} stars",
                        c.Stats.IsFullCombo ? "full combo" : null,
                        c.IsHighScore ? "new best" : null,
                    }.Where(s => s != null));
                }
                activity.Timestamps.Start = _gameStart;
            }
            else
            {
                activity.Timestamps.Start = _gameStart;
                if (lobby != null)
                {
                    activity.Details = lobby.Phase == "lobby" ? "Online lobby · picking songs" : "Online lobby · between songs";
                    activity.State = "In a lobby";
                }
                else if (!limited && MenuManager.Instance != null && MenuManager.Instance.CurrentMenu is
                    MenuManager.Menu.MusicLibrary or MenuManager.Menu.DifficultySelect)
                {
                    activity.Details = "Picking a song";
                }
                else
                {
                    activity.Details = "In the menus";
                    if (!limited && Menu.Main.ProfileCard.Profile is { } profile)
                    {
                        activity.State = profile.Season is { } season
                            ? $"Level {profile.Level} · Season {season.Number} level {season.Level}"
                            : $"Level {profile.Level}";
                    }
                }
            }

            // In an online lobby (in its menu or playing its songs): the party, and Ask to Join with its code
            if (lobby != null && !string.IsNullOrEmpty(lobby.Code))
            {
                activity.Party.Id = "lobby-" + lobby.Code;
                activity.Party.Size.CurrentSize = Math.Min(lobby.Players.Count, LOBBY_SIZE);
                activity.Party.Size.MaxSize = LOBBY_SIZE;
                activity.Secrets.Join = lobby.Code;
                if (scene != SceneIndex.Gameplay)
                {
                    activity.State = $"In a lobby ({activity.Party.Size.CurrentSize} of {LOBBY_SIZE})";
                }
            }

            string key = string.Join("|", activity.Details, activity.State, activity.Assets.LargeImage, activity.Party.Id,
                activity.Party.Size.CurrentSize, activity.Timestamps.End == 0 ? "" : "timed");
            return (activity, key);
        }

        // The song's cover as the big picture with the crescent small, once its link is in; the crescent alone before
        private void WithCover(ref Activity activity, SongEntry song)
        {
            if (song != _coverSong)
            {
                _coverSong = song;
                _cover = null;
                FetchCover(song).Forget();
            }
            activity.Assets.LargeText = song.Name;
            if (_cover != null)
            {
                activity.Assets.LargeImage = _cover;
                activity.Assets.SmallImage = ICON;
                activity.Assets.SmallText = ICON_TEXT;
            }
        }

        private async UniTaskVoid FetchCover(SongEntry song)
        {
            string link = null;
            try
            {
                link = await UniTask.RunOnThreadPool(() => OnlineSongs.CoverLink(song.ActualLocation));
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Couldn't get the song's cover for Discord.");
            }
            // Discord takes image links of up to 256 characters
            if (song == _coverSong && link != null && link.Length <= 256)
            {
                _cover = link;
                _nextLook = 0f;
            }
        }

        private static string PartName(Instrument instrument) => Leaderboards.InstrumentName(instrument) switch
        {
            "guitar" => "Lead",
            "bass"   => "Bass",
            "drums"  => "Drums",
            "vocals" => "Vocals",
            _        => instrument.ToString(),
        };

        private void SetActivity(Activity activity)
        {
            _discord?.GetActivityManager().UpdateActivity(activity, result =>
            {
                if (result != Result.Ok)
                {
                    YargLogger.LogFormatWarning("Discord Activity Error: {0}", result);
                }
            });
        }

        private void OnApplicationQuit()
        {
            TryDispose();
        }

        public void TryDispose()
        {
            if (_discord == null)
            {
                return;
            }

            try
            {
                _discord.GetActivityManager().ClearActivity(_ => { });
                _discord.Dispose();
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Failed to dispose of Discord client.");
            }
            _discord = null;
            _sentKey = null;
        }
    }
}
