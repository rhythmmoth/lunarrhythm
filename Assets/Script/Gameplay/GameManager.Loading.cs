using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YARG.Core;
using YARG.Core.Audio;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Logging;
using YARG.Core.Replays;
using YARG.Core.Song;
using YARG.Helpers;
using YARG.Gameplay.HUD;
using YARG.Gameplay.Player;
using YARG.Menu;
using YARG.Menu.Navigation;
using YARG.Menu.Persistent;
using YARG.Menu.Settings;
using YARG.Playback;
using YARG.Player;
using YARG.Scores;
using YARG.Settings;
using YARG.Song;

namespace YARG.Gameplay
{
    public partial class GameManager
    {
        private enum LoadFailureState
        {
            None,
            Rescan,
            Error
        }

        [Header("Instrument Prefabs")]
        [SerializeField]
        private GameObject _fiveFretGuitarPrefab;
        [SerializeField]
        private GameObject _sixFretGuitarPrefab;
        [SerializeField]
        private GameObject _fourLaneDrumsPrefab;
        [SerializeField]
        private GameObject _fiveLaneDrumsPrefab;
        [SerializeField]
        private GameObject _proKeysPrefab;
        [SerializeField]
        private GameObject _fiveLaneKeysPrefab;
        [SerializeField]
        private GameObject _proGuitarPrefab;

        private const double NORMALIZED_SAMPLE_VOLUME_MULTIPLIER = 0.5;
        private const double DEFAULT_SAMPLE_VOLUME_MULTIPLIER = 1.0;

        private LoadFailureState _loadState;
        private string _loadFailureMessage;

        // All access to chart data must be done through this event,
        // since things are loaded asynchronously
        // Players are initialized by hand and don't go through this event
        private event Action<SongChart> _chartLoaded;

        public event Action<SongChart> ChartLoaded
        {
            add
            {
                _chartLoaded += value;

                // Invoke now if already loaded, this event is only fired once
                var chart = Chart;
                if (chart != null) value?.Invoke(chart);
            }
            remove => _chartLoaded -= value;
        }

        private event Action _songLoaded;

        public event Action SongLoaded
        {
            add
            {
                _songLoaded += value;

                // Invoke now if already loaded, this event is only fired once
                if (_mixer != null)
                {
                    value?.Invoke();
                }
            }
            remove => _songLoaded -= value;
        }

        private event Action _songStarted;

        public event Action SongStarted
        {
            add
            {
                _songStarted += value;

                // Invoke now if already loaded, this event is only fired once
                if (IsSongStarted) value?.Invoke();
            }
            remove => _songStarted -= value;
        }

        private async void Start()
        {
            // Displays the loading screen
            using var context = new LoadingContext();
            var global = GlobalVariables.Instance;

            // Disable until everything's loaded
            enabled = false;

            ApplySampleNormalization();

            YargLogger.LogFormatInfo("Loading song {0} - {1}", Song.Name, Song.Artist);

            if (ReplayInfo != null)
            {
                if (!SongContainer.SongsByHash.TryGetValue(GlobalVariables.State.CurrentReplay.SongChecksum, out var songs))
                {
                    ToastManager.ToastWarning("Song not present in library");
                    global.LoadScene(SceneIndex.Menu);
                    return;
                }
                Song = songs[0];

                context.SetLoadingText("Loading replay...");
                if (!LoadReplay())
                {
                    ToastManager.ToastError("Failed to load replay!");
                    global.LoadScene(SceneIndex.Menu);
                    return;
                }

                if (!GlobalVariables.State.PlayingWithReplay)
                {
                    _replayController.gameObject.SetActive(true);
                }
                else
                {
                    _replayController.gameObject.SetActive(false);
                    var players = new List<YargPlayer>();
                    players.AddRange(PlayerContainer.Players);
                    for (int i = 0; i < YargPlayers.Count; i++)
                    {
                         players.Add(YargPlayers[i]);
                    }

                    YargPlayers = players.ToArray();
                }

                var replayIndex = 0;
                foreach (var player in YargPlayers)
                {
                    if (player.IsReplay)
                    {
                        player.ReplayIndex = replayIndex;
                        replayIndex++;
                    }
                }
            }

            if (OnlineSongs.IsOnline(PathHelper.OnlineSongsPath, Song) && !OnlineSongs.StemsReady(Song.ActualLocation))
            {
                context.SetLoadingText("Downloading song...");
                var download = UniTask.RunOnThreadPool(() => OnlineSongs.FetchStems(Song.ActualLocation));
                while (download.Status == UniTaskStatus.Pending)
                {
                    context.SetSubText(OnlineSongs.Status);
                    await UniTask.NextFrame();
                }

                if (!await download)
                {
                    ToastManager.ToastError(Leaderboards.SignedIn
                        ? "Couldn't download this song. Check your internet connection."
                        : YARG.Song.SongContainer.OFFLINE_NOTICE);
                    global.LoadScene(SceneIndex.Menu);
                    return;
                }
            }

            context.Queue(UniTask.RunOnThreadPool(LoadChart), "Loading chart...");
            context.Queue(UniTask.RunOnThreadPool(LoadAudio), "Loading audio...");
            await context.Wait();

            if (_loadState == LoadFailureState.Rescan)
            {
                ToastManager.ToastWarning("Chart requires a rescan!", () =>
                {
                    MenuManager.Instance.DisableCurrentMenu();
                    SettingsMenu.Instance.gameObject.SetActive(true);
                    SettingsMenu.Instance.SelectTabByName("SongManager");
                });

                global.LoadScene(SceneIndex.Menu);
                return;
            }

            if (_loadState == LoadFailureState.Error)
            {
                YargLogger.LogError(_loadFailureMessage);
                ToastManager.ToastError(_loadFailureMessage);

                global.LoadScene(SceneIndex.Menu);
                return;
            }

            FinalizeChart();

            // Initialize song runner
            _songRunner = new SongRunner(
                _mixer,
                startTime: 0,
                SONG_START_DELAY,
                GlobalVariables.State.SongSpeed,
                Song.SongOffsetSeconds);

            // Spawn players
            CreatePlayers();
            YargLogger.LogFormatDebug("Calculating star cutoffs for {0} players", _players.Count);
            EngineManager.StarScoreThresholds = EngineManager.GetStarScoreCutoffs(_players.ConvertAll(p => p.BaseEngine.StarScoreThresholds));
            YargLogger.LogFormatDebug("Star score thresholds: {0}", string.Join(", ", EngineManager.StarScoreThresholds));


            // Set up the crowd stem so it can be restored after muting (if it exists)
            if (_stemStates.TryGetValue(SongStem.Crowd, out var state))
            {
                state.Total = 1;
                state.Audible = 1;
            }

            if (_loadState == LoadFailureState.Error)
            {
                ToastManager.ToastError(_loadFailureMessage);

                global.LoadScene(SceneIndex.Menu);
                return;
            }

            // Listen for menu inputs
            Navigator.Instance.NavigationEvent += OnNavigationEvent;

            // Debug info
            InitializeDebug();
#if UNITY_EDITOR
            SetDebugEnabled(true);
#endif

            // Initialize/destroy practice mode
            if (IsPractice)
            {
                PracticeManager.DisplayPracticeMenu();
            }
            else
            {
                Destroy(PracticeManager);
            }

            _failMeter.Initialize(EngineManager, this);

            if (SettingsManager.Settings.NoFail.Value == NoFailMode.NoMeter || IsPractice)
            {
                _failMeter.SetActive(false);
            }

            // This is not an else because we still want to subscribe in case the user disables no fail during the song
            // We check in the callback to determine whether we should actually run the fail routine
            if (ReplayInfo == null || GlobalVariables.State.PlayingWithReplay)
            {
                EngineManager.OnSongFailed += OnSongFailed;

                EngineManager.InitializeHappiness();

                SettingsManager.Settings.NoFail.OnChange += OnNoFailModeChanged;
                SettingsManager.Settings.AutoCalibrateAudio.Value = false;
                SettingsManager.Settings.AutoCalibrateVideo.Value = false;
            }

            EngineManager.OnCodaStart += StartCoda;
            EngineManager.OnCodaEnd += EndCoda;

            // Log constant values
            YargLogger.LogFormatDebug("Audio calibration: {0}, video calibration: {1}, song offset: {2}",
                _songRunner.AudioCalibration, _songRunner.VideoCalibration, _songRunner.SongOffset);

            // Into the song: night over the loading screen, the first frames (shaders, highways, visualizer) settle
            // under it, then the song starts under it too and it lifts once those frames (notes spawning, the HUD)
            // run smoothly, at most a second into the 2 s lead-in. Not for a lobby: its start is a server time, so
            // its loading screen stays until then.
            if (!Menu.Online.LobbyPlay.Playing)
            {
                await Moonrise.CoverAsync();
                LoadingScreen.Instance.gameObject.SetActive(false);
                await Moonrise.WaitUntilSmooth();
            }

            // A lobby song starts for everyone at one server time; the loading screen stays until then
            if (Menu.Online.LobbyPlay.Playing)
            {
                context.SetLoadingText("Syncing with the lobby");
                if (!await Menu.Online.LobbyPlay.WaitForStart(SongLength, LobbySetup(), AddLobbyPlayers, context.SetSubText))
                {
                    ToastManager.ToastWarning("The lobby went on without you.");
                    global.LoadScene(SceneIndex.Menu);
                    return;
                }
            }

            // Loaded, enable updates
            enabled = true;
            IsSongStarted = true;
            _songStarted?.Invoke();
            if (!Menu.Online.LobbyPlay.Playing)
            {
                await WaitForFirstFrames();
                Moonrise.Lift(0.5f);
            }
        }

        private void ApplySampleNormalization()
        {
            double multiplier = SettingsManager.Settings.EnableNormalization.Value
                ? NORMALIZED_SAMPLE_VOLUME_MULTIPLIER
                : DEFAULT_SAMPLE_VOLUME_MULTIPLIER;

            GlobalAudioHandler.SetVolumeMultiplier(SongStem.Sfx, multiplier);
            GlobalAudioHandler.SetVolumeMultiplier(SongStem.DrumSfx, multiplier);
            GlobalAudioHandler.SetVolumeMultiplier(SongStem.VoxSample, multiplier);
            GlobalAudioHandler.SetVolumeMultiplier(SongStem.Metronome, multiplier);
        }

        private bool LoadReplay()
        {
            var readOptions = new ReplayReadOptions { KeepFrameTimes = GlobalVariables.VerboseReplays };
            var (result, data) = ReplayIO.TryLoadData(ReplayInfo, readOptions);
            if (result != ReplayReadResult.Valid)
            {
                YargLogger.LogFormatError("Failed to load replay! Result: {0}", result);
                return false;
            }

            // Create YargPlayers from the replay frames
            var players = new YargPlayer[data.Frames.Length];
            for (int i = 0; i < data.Frames.Length; ++i)
            {
                players[i] = new YargPlayer(data.Frames[i], data);
            }

            ReplayData = data;
            YargPlayers = players;
            return true;
        }

        private void LoadChart()
        {
            try
            {
                Chart = Song.LoadChart();
                if (Chart != null)
                {
                    GenerateVenueTrack();
                    GenerateLipsyncTrack();
                }
                else
                {
                    _loadState = LoadFailureState.Rescan;
                }
            }
            catch (Exception ex)
            {
                _loadState = LoadFailureState.Error;
                _loadFailureMessage = "Failed to load chart!";
                YargLogger.LogException(ex, "Failed to load chart!");
            }
        }

        private void GenerateVenueTrack()
        {
            // If we have no venue events, attempt to load from milo
            if (Chart.VenueTrack.IsEmpty)
            {
                    SongChart.LoadVenueFromMilo(Chart, Song);

                    YargLogger.LogFormatDebug("Loaded {0} lighting events from milo", Chart.VenueTrack.Lighting.Count);
            }

            if (File.Exists(VenueAutoGenerationPreset.DefaultPath))
            {
                var preset = new VenueAutoGenerationPreset(VenueAutoGenerationPreset.DefaultPath);
                if (!preset.ChartHasFog(Chart)) // This is separate because we may want to add fog even if venue is authored
                {
                    Chart = preset.GenerateFogEvents(Chart);
                }

                if (Chart.VenueTrack.Lighting.Count == 0)
                {
                    Chart = preset.GenerateLightingEvents(Chart);
                }
            }
        }

        private void GenerateLipsyncTrack()
        {
            SongChart.LoadLipsyncFromMilo(Chart, Song);

            YargLogger.LogFormatDebug("Loaded {0} lipsync events from milo", Chart.LipsyncEvents.Count);
        }

        private void FinalizeChart()
        {
            double audioLength = _mixer.Length;
            double chartLength = Chart.GetEndTime();
            double endTime = Chart.GetEndEvent()?.Time ?? -1;

            // - Chart < Audio < [end] -> Audio
            // - Chart < [end] < Audio -> [end]
            // - [end] < Chart < Audio -> Audio
            // - Audio < Chart         -> Chart
            if (audioLength <= chartLength)
            {
                SongLength = chartLength;
            }
            else if (endTime <= chartLength || audioLength <= endTime)
            {
                SongLength = audioLength;
            }
            else
            {
                SongLength = endTime;
            }

            // Get the first and last note times for the chart
            FirstNoteTime = Chart.GetFirstNoteStartTime();
            LastNoteTime = Chart.GetLastNoteEndTime();

            // Make sure enough beatlines have been generated to cover the song end delay
            Chart.SyncTrack.GenerateBeatlines(SongLength + SONG_END_DELAY, true);

            BeatEventHandler = new BeatEventHandler(Chart.SyncTrack);
            CrowdEventHandler = new CrowdEventHandler(Chart, this);

            _chartLoaded?.Invoke(Chart);

            _songLoaded?.Invoke();
        }

        /// <summary>This player's setup for the others in the lobby: their replay frame, before any presses.</summary>
        private string LobbySetup()
        {
            try
            {
                return _players.FirstOrDefault(p => !p.Player.Profile.IsBot) is { } me
                    ? Lobby.EncodeSetup(me.ConstructReplayData().Frame)
                    : null;
            }
            catch (Exception e)
            {
                // Without it the others just don't see this highway
                YargLogger.LogException(e, "Couldn't make the lobby setup");
                return null;
            }
        }

        /// <summary>The other lobby players' highways, driven by the presses they stream. Kept apart from
        /// <see cref="_players"/>, so they're never scored, saved or part of the band.</summary>
        private readonly List<BasePlayer> _remotePlayers = new();
        private readonly Dictionary<BasePlayer, long> _remoteIds = new();

        /// <summary>Under the veil, after the song starts: the visualizer's colours are in and the highways have drawn a
        /// few frames (notes spawned, art built), at most 1.2 s into the 2 s lead-in.</summary>
        private async UniTask WaitForFirstFrames()
        {
            var highways = FindAnyObjectByType<Visuals.HighwayCameraRendering>();
            int from = highways != null ? highways.FramesRendered : 0;
            float until = Time.unscaledTime + 1.2f;
            while (Time.unscaledTime < until && this != null
                && (Visuals.MoonVisualizer.Loading || highways != null && highways.FramesRendered - from < 10))
            {
                await UniTask.NextFrame();
            }
        }

        private void AddLobbyPlayers()
        {
            if (Lobby.State is not { } state) return;
            int index = _players.Count - 1;
            foreach (var p in state.Players)
            {
                if (p.Id == state.You || p.SitsOut || p.Dropped || !p.Loaded) continue;
                if (_players.Count + _remotePlayers.Count >= Lobby.MAX_PLAYERS) break;
                if (!Lobby.Setups.TryGetValue(p.Id, out var setup) || setup.Song != state.Song) continue;
                if (Lobby.DecodeSetup(setup.Setup) is not { } frame) continue;
                // The vocal track shows one kind of vocals; a singer of the other kind isn't shown
                if (frame.Profile.GameMode == GameMode.Vocals && _vocalTrackInitialized && frame.Profile.CurrentInstrument != _vocalPart) continue;
                try
                {
                    var player = new YargPlayer(frame, Lobby.Feed(p.Id, state.Song), p.Name);
                    if (SpawnPlayer(player, ++index, null) is { } spawned)
                    {
                        _remotePlayers.Add(spawned);
                        _remoteIds[spawned] = p.Id;
                    }
                }
                catch (Exception e)
                {
                    YargLogger.LogException(e, "Couldn't add a lobby player's highway");
                }
            }
        }

        private int _highwayIndex;
        private Instrument _vocalPart;
        private int _vocalIndex;
        private bool _vocalTrackInitialized;

        /// <summary>Puts a player's highway (or a place on the vocal track) in the scene, next to the ones before.
        /// Null if there's no highway for the game mode.</summary>
        private BasePlayer SpawnPlayer(YargPlayer player, int index, int? lastHighScore)
        {
            if (player.Profile.GameMode != GameMode.Vocals)
            {
                _highwayIndex++;
                var prefab = player.Profile.GameMode switch
                {
                    GameMode.FiveFretGuitar => _fiveFretGuitarPrefab,
                    GameMode.SixFretGuitar  => _sixFretGuitarPrefab,
                    GameMode.FourLaneDrums  => _fourLaneDrumsPrefab,
                    GameMode.FiveLaneDrums  => _fiveLaneDrumsPrefab,
                    GameMode.EliteDrums     => Song.HasInstrument(Instrument.FiveLaneDrums) ? _fiveLaneDrumsPrefab : _fourLaneDrumsPrefab,
                    GameMode.ProKeys        => player.Profile.CurrentInstrument is Instrument.ProKeys ? _proKeysPrefab : _fiveLaneKeysPrefab,
                    GameMode.ProGuitar      => _proGuitarPrefab,
                    _                       => null
                };

                if (prefab == null) return null;

                var playerObject = Instantiate(prefab,
                    new Vector3(_highwayIndex * TRACK_SPACING_X, 100f, 0f), prefab.transform.rotation);

                // Setup player
                var trackPlayer = playerObject.GetComponent<TrackPlayer>();
                var trackView = _trackViewManager.CreateTrackView();
                trackPlayer.Initialize(_highwayIndex, player, Chart, trackView, _mixer, lastHighScore);

                _trackViewManager.AddTrackPlayer(trackPlayer);
                return trackPlayer;
            }
            else
            {
                // Initialize the vocal track if it hasn't been already, and hide lyric bar
                if (!_vocalTrackInitialized)
                {
                    _highwayIndex++;
                    VocalTrack.gameObject.SetActive(true);
                    // Position the vocal track at its highway slot so the shader's WorldPosToIndex maps it correctly
                    VocalTrack.transform.position = new Vector3(_highwayIndex * TRACK_SPACING_X, 100, 0);
                    _trackViewManager.CreateVocalTrackView(_highwayIndex);

                    // Since all players have to select the same vocals
                    // type (solo/harmony) this works no problem.
                    var chart = player.Profile.CurrentInstrument == Instrument.Vocals
                        ? Chart.Vocals
                        : Chart.Harmony;
                    VocalTrack.Initialize(chart, player, Song.VocalScrollSpeedScalingFactor);
                    _vocalPart = player.Profile.CurrentInstrument;

                    _lyricBar.gameObject.SetActive(false);
                    _vocalTrackInitialized = true;
                }

                // Create the player on the vocal track

                var vocalsPlayer = VocalTrack.CreatePlayer();
                _vocalIndex++;
                var playerHud = _trackViewManager.CreateVocalsPlayerHUD();

                var percussionTrack = VocalTrack.CreatePercussionTrack();
                percussionTrack.TrackSpeed = VocalTrack.TrackSpeed;
                vocalsPlayer.Initialize(index, _vocalIndex, player, Chart, playerHud, percussionTrack, lastHighScore, VocalTrack.TrackSpeed);

                return vocalsPlayer;
            }
        }

        private void CreatePlayers()
        {
            try
            {
                _players = new List<BasePlayer>();

                _vocalTrackInitialized = false;

                int index = -1;
                _highwayIndex = -1;
                _vocalIndex = -1;
                bool themesWorn = false;
                foreach (var player in YargPlayers)
                {
                    player.IsScoreValid = true;

                    if (!player.IsReplay)
                    {
                        // Reset microphone (resets channel buffers)
                        // We probably wanna do this no matter what, so put it up here
                        player.Bindings.Microphone?.Reset();
                    }

                    // Skip if the player is sitting out
                    if (player.SittingOut)
                    {
                        continue;
                    }
                    index++;

                    if (!player.IsReplay)
                    {
                        // Don't do this if it's a replay, because the replay
                        // would've already set its own presets at this point
                        player.RefreshPresets();
                        // The online account is one person: the first human player wears its themes
                        if (!themesWorn && !player.Profile.IsBot)
                        {
                            themesWorn = true;
                            player.ApplyOnlineThemes(YARG.Menu.Main.ProfileCard.Profile);
                        }
                    }

                    var lastHighScore = ScoreContainer.GetHighScore(Song.Hash, player.Profile.Id, player.Profile.CurrentInstrument, false)?.Score;
                    YargLogger.LogFormatInfo("Current high score for player {0} on {1}: {2}",
                        player.Profile.Name, player.Profile.CurrentInstrument, lastHighScore ?? 0);

                    var spawned = SpawnPlayer(player, index, lastHighScore);
                    // Skip if there's no prefab for the game mode
                    if (spawned == null) continue;
                    _players.Add(spawned);

                    // Add (or increase total of) the stem state
                    var stem = player.Profile.CurrentInstrument.ToSongStem();
                    if (stem == SongStem.Bass && !_stemStates.ContainsKey(SongStem.Bass))
                    {
                        stem = SongStem.Rhythm;
                    }

                    if (stem != _backgroundStem && _stemStates.TryGetValue(stem, out var state))
                    {
                        ++state.Total;
                        ++state.Audible;
                    }
                    else if (_stemStates.TryGetValue(_backgroundStem, out state))
                    {
                        // Ensures the stem will still play at a minimum of 50%, even if all players mute
                        state.Total += 2;
                        state.Audible += 2;
                    }
                }
            }
            catch (Exception ex)
            {
                _loadState = LoadFailureState.Error;
                _loadFailureMessage = "Failed to load song!";
                YargLogger.LogException(ex, "Failed to load song!");
            }
        }
    }
}
