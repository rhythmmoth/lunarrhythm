using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Chart;
using YARG.Helpers.Extensions;
using YARG.Settings;
using YARG.Song;

namespace YARG.Gameplay.Visuals
{
    public enum GameBackgroundMode
    {
        Visualizer,
        SongVideo,
        Moonlight
    }

    public enum VisualizerStyle
    {
        Auto,
        Tide,
        Pulse,
        Meteor,
        Aurora,
        Searchlights,
        Shatter,
        Grid,
        Skyline,
        Glow,
        Smoke,
        Fireflies,
        Constellations
    }

    /// <summary>
    /// The in-game visualizer (board NextVisualizers): a sky behind the highway drawn by LunarVisualizer.shader, kept in
    /// time with the chart's own beatlines. The style comes from the genre (or Settings), the colours from the cover;
    /// choruses glow, star power turns it gold and every 100-note streak sends a shooting star.
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public class MoonVisualizer : GameplayBehaviour
    {
        private const float STREAK_SECONDS = 0.9f;

        private static readonly int ColA = Shader.PropertyToID("_ColA"), ColB = Shader.PropertyToID("_ColB"),
            T = Shader.PropertyToID("_T"), Beat = Shader.PropertyToID("_Beat"), Down = Shader.PropertyToID("_Down"),
            Bar = Shader.PropertyToID("_Bar"), BarLen = Shader.PropertyToID("_BarLen"), Energy = Shader.PropertyToID("_Energy"),
            Gold = Shader.PropertyToID("_Gold"), Streak = Shader.PropertyToID("_Streak"), Style = Shader.PropertyToID("_Style"),
            Intensity = Shader.PropertyToID("_Intensity"), Aspect = Shader.PropertyToID("_Aspect");

        private Material _material;
        private double[] _beats = Array.Empty<double>();
        private double[] _bars = Array.Empty<double>();
        private readonly List<(double Time, float Energy)> _sections = new();
        private float _energy = 0.7f, _gold;
        private int _streaks;
        private float _streakStart = -10f;

        /// <summary>The visualizer is still getting its colours from the song's cover: the song's veil waits for it, so
        /// the sky doesn't change colour in front of the player.</summary>
        public static bool Loading { get; private set; }

        protected override void GameplayAwake()
        {
            var shader = Resources.Load<Shader>("LunarVisualizer");
            _material = new Material(shader);
            GetComponent<RawImage>().material = _material;

            var song = GameManager.Song;
            var style = SettingsManager.Settings.VisualizerStyle.Value;
            if (style == VisualizerStyle.Auto) style = song != null ? ForGenre(Genrelizer.MajorOf(song)) : VisualizerStyle.Aurora;
            _material.SetFloat(Style, (int) style - 1);
            _material.SetFloat(Intensity, SettingsManager.Settings.VisualizerIntensity.Value);
            _material.SetFloat(Energy, _energy);
            _material.SetFloat(Streak, 2f);
            _material.SetFloat(BarLen, 2f);
            if (song != null)
            {
                Loading = true;
                LoadCoverColours(song).Forget();
            }
            if (GameManager.Chart != null) Prepare(GameManager.Chart);
        }

        protected override void OnChartLoaded(SongChart chart) => Prepare(chart);

        protected override void GameplayDestroy()
        {
            Loading = false;
            if (_material != null) Destroy(_material);
        }

        /// <summary>One sky per major genre (board NextGenres); every subgenre shares its major genre's.</summary>
        public static VisualizerStyle ForGenre(MajorGenre genre) => genre switch
        {
            MajorGenre.Rock                 => VisualizerStyle.Searchlights,
            MajorGenre.Metal                => VisualizerStyle.Meteor,
            MajorGenre.PunkSceneCore        => VisualizerStyle.Shatter,
            MajorGenre.Pop                  => VisualizerStyle.Pulse,
            MajorGenre.DanceElectronic      => VisualizerStyle.Grid,
            MajorGenre.HipHop               => VisualizerStyle.Skyline,
            MajorGenre.RnbSoulFunk          => VisualizerStyle.Glow,
            MajorGenre.JazzBlues            => VisualizerStyle.Smoke,
            MajorGenre.CountryFolk          => VisualizerStyle.Fireflies,
            MajorGenre.ClassicalTraditional => VisualizerStyle.Constellations,
            MajorGenre.World                => VisualizerStyle.Tide,
            _                               => VisualizerStyle.Aurora // Alternative and Other
        };

        private void Prepare(SongChart chart)
        {
            var beats = new List<double>();
            var bars = new List<double>();
            foreach (var line in chart.SyncTrack.Beatlines)
            {
                if (line.Type == BeatlineType.Weak) continue;
                beats.Add(line.Time);
                if (line.Type == BeatlineType.Measure) bars.Add(line.Time);
            }
            _beats = beats.ToArray();
            _bars = bars.ToArray();

            _sections.Clear();
            foreach (var section in chart.Sections)
            {
                string name = section.Name.ToLowerInvariant();
                float energy = name.Contains("chorus") || name.Contains("solo") ? 1f
                    : name.Contains("intro") || name.Contains("outro") || name.Contains("break") || name.Contains("quiet") ? 0.45f
                    : 0.7f;
                _sections.Add((section.Time, energy));
            }
        }

        private async UniTaskVoid LoadCoverColours(YARG.Core.Song.SongEntry song)
        {
            try
            {
                await ApplyCoverColours(song);
            }
            finally
            {
                Loading = false;
            }
        }

        private async UniTask ApplyCoverColours(YARG.Core.Song.SongEntry song)
        {
            using var image = await UniTask.RunOnThreadPool(song.LoadAlbumData);
            if (image == null || this == null) return;
            var cover = image.LoadTexture(false);
            // Shrink to 8x8 on the GPU and read that back: the top and bottom halves' average colours
            var small = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(cover, small);
            var read = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            RenderTexture.active = small;
            read.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(small);
            var pixels = read.GetPixels();
            Destroy(read);
            Destroy(cover);

            Color top = Color.black, bottom = Color.black;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (i < 32) bottom += pixels[i] / 32f;
                else top += pixels[i] / 32f;
            }
            // A deep sky from one half, a bright accent from the other; greys stay the menus' blue
            Color.RGBToHSV(bottom, out float h1, out float s1, out _);
            Color.RGBToHSV(top, out float h2, out float s2, out _);
            if (s1 > 0.12f) _material.SetColor(ColA, Color.HSVToRGB(h1, Mathf.Max(s1, 0.5f) * 0.85f, 0.42f));
            if (s2 > 0.12f) _material.SetColor(ColB, Color.HSVToRGB(h2, Mathf.Max(s2, 0.45f), 0.95f));
        }

        private void Update()
        {
            double t = GameManager.VisualTime;
            bool calm = SettingsManager.Settings.ReduceFlashingLights.Value;

            // Beats and bars from the chart's beatlines, so tempo changes stay in time
            double lastBeat = Last(_beats, t, out _);
            double lastBar = Last(_bars, t, out int bar);
            double nextBar = bar + 1 < _bars.Length ? _bars[bar + 1] : lastBar + 2.0;
            double barLen = bar >= 0 ? Math.Max(0.2, nextBar - lastBar) : 2.0;
            float place = bar >= 0 ? (float) Math.Clamp((t - lastBar) / barLen, 0.0, 1.0) : 0f;

            _material.SetFloat(T, (float) t);
            _material.SetFloat(Beat, Decay(t - lastBeat, 8f) * (calm ? 0.4f : 1f));
            _material.SetFloat(Down, calm ? 0f : Decay(t - lastBar, 6f));
            _material.SetFloat(Bar, Math.Max(bar, 0) + place);
            _material.SetFloat(BarLen, (float) barLen);
            _material.SetFloat(Aspect, Screen.height > 0 ? (float) Screen.width / Screen.height : 16f / 9f);

            // Section energy, star power and streaks ease in and out
            float target = 0.7f;
            foreach (var (time, energy) in _sections)
            {
                if (time > t) break;
                target = energy;
            }
            _energy = Mathf.MoveTowards(_energy, target, Time.deltaTime * 0.8f);
            _material.SetFloat(Energy, _energy);

            bool starPower = false;
            int combo = 0;
            foreach (var player in GameManager.Players)
            {
                if (player == null || player.BaseEngine == null) continue;
                starPower |= player.BaseStats.IsStarPowerActive;
                combo = Math.Max(combo, player.Combo);
            }
            _gold = Mathf.MoveTowards(_gold, starPower ? 1f : 0f, Time.deltaTime * 2.5f);
            _material.SetFloat(Gold, _gold);

            int streaks = combo / 100;
            if (streaks > _streaks) _streakStart = Time.time;
            _streaks = streaks;
            _material.SetFloat(Streak, (Time.time - _streakStart) / STREAK_SECONDS);
        }

        private static float Decay(double since, float rate) => since < 0 ? 0f : Mathf.Exp((float) -since * rate);

        /// <summary>The latest time in <paramref name="times"/> at or before <paramref name="t"/> (and its index, -1 for none).</summary>
        private static double Last(double[] times, double t, out int index)
        {
            index = Array.BinarySearch(times, t);
            if (index < 0) index = ~index - 1;
            return index >= 0 ? times[index] : double.NegativeInfinity;
        }
    }
}
