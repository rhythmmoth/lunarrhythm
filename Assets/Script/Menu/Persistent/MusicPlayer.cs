using System.Diagnostics.CodeAnalysis;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Settings;
using YARG.Helpers.Extensions;
using YARG.Core.Audio;
using YARG.Core.Song;
using YARG.Song;
using System.Threading.Tasks;

namespace YARG.Menu.Persistent
{
    public class MusicPlayer : MonoBehaviour
    {
        private static SongEntry _nowPlaying = null;
        /// <summary>The game song the current jukebox track promotes ("Play this song"), or null.</summary>
        public static SongEntry NowPlaying => _nowPlaying;
        /// <summary>The jukebox track playing on the title screen, or null.</summary>
        public static Jukebox.Track CurrentTrack { get; private set; }
        /// <summary>Fires on the main thread when <see cref="CurrentTrack"/> changes.</summary>
        public static event System.Action NowPlayingChanged;

        private object _lock = new();
        private StemMixer _mixer = null;

        [SerializeField]
        private Image _playPauseButton;
        [SerializeField]
        private TextMeshProUGUI _songText;
        [SerializeField]
        private TextMeshProUGUI _artistText;

        [Space]
        [SerializeField]
        private Sprite _playSprite;
        [SerializeField]
        private Sprite _pauseSprite;

        private async void OnEnable()
        {
            _songText.text = _artistText.text = string.Empty;

            // Wait until the loading is done
            await UniTask.WaitUntil(() => !LoadingScreen.IsActive);

            StemSettings.ApplySettings = false; // ensure that MusicPlayer uses the full-volume mix
            NextSong();
        }

        private void OnDisable()
        {
            StemSettings.ApplySettings = SettingsManager.Settings.ApplyVolumesInMusicLibrary.Value; // reset to default value
            lock (_lock)
            {
                _mixer?.Dispose();
                _mixer = null;
            }
        }

        private static Task<StemMixer> _current;
        /// <summary>Plays the next jukebox track. With nothing playable yet it checks again a little later.</summary>
        public async void NextSong()
        {
            const int MAX_TRIES = 5;
            for (int tries = 0; tries < MAX_TRIES; tries++)
            {
                var track = await Jukebox.Next();
                if (this == null || !isActiveAndEnabled)
                {
                    return;
                }
                if (track == null)
                {
                    break;
                }

                Task<StemMixer> task;
                lock (_lock)
                {
                    const float SPEED = 1f;
                    double volume = SettingsManager.Settings.MusicPlayerVolume.Value;
                    // normalize: tracks uploaded at different loudness play at the same level
                    _current = task = Task.Run(() => GlobalAudioHandler.LoadCustomFile(track.AudioPath, SPEED, volume, normalize: true));
                }

                var mixer = await task;
                if (mixer == null)
                {
                    continue;
                }
                CurrentTrack = track;
                _nowPlaying = Jukebox.FindSong(track.Song);

                lock (_lock)
                {
                    if (_current != task || !gameObject.activeSelf)
                    {
                        mixer.Dispose();
                        continue;
                    }

                    _mixer?.Dispose();
                    _mixer = mixer;
                    _mixer.SongEnd += () =>
                    {
                        _mixer.Dispose();
                        _mixer = null;
                        NextSong();
                    };
                    _mixer.Play();

                    _songText.text = track.Title;
                    _artistText.text = track.Artist;
                    _playPauseButton.sprite = _pauseSprite;
                }
                NowPlayingChanged?.Invoke();
                return;
            }
            CurrentTrack = null;
            _nowPlaying = null;
            NowPlayingChanged?.Invoke();

            // Offline or not signed in yet: try again in a while
            if (!await UniTask.Delay(30_000, ignoreTimeScale: true, cancellationToken: destroyCancellationToken).SuppressCancellationThrow()
                && this != null && isActiveAndEnabled && CurrentTrack == null)
            {
                NextSong();
            }
        }

        public void UpdateVolume(double volume)
        {
            lock (_lock)
            {
                _mixer?.SetVolume(volume);
            }
        }

        public void TogglePlay()
        {
            lock (_lock)
            {
                if (_mixer == null)
                {
                    return;
                }

                if (!_mixer.IsPaused)
                {
                    _mixer.Pause();
                    _playPauseButton.sprite = _playSprite;
                }
                else
                {
                    _mixer.Play();
                    _playPauseButton.sprite = _pauseSprite;
                }
            }
        }
    }
}