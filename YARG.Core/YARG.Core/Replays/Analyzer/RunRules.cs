using System.IO;
using System.Linq;
using YARG.Core.Engine;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Vocals;
using YARG.Core.Game;

namespace YARG.Core.Replays.Analyzer
{
    /// <summary>
    /// The game's star thresholds per part (multiples of the chart's base score), shared by the game's players and the
    /// leaderboard server's run check, so a run can't bring its own. Each call returns a fresh copy.
    /// </summary>
    public static class StarThresholds
    {
        public static float[] Guitar => new[] { 0.06f, 0.12f, 0.2f, 0.47f, 0.78f, 1.15f };
        public static float[] Bass => new[] { 0.05f, 0.1f, 0.19f, 0.47f, 0.78f, 1.15f };
        /// <summary>5-lane keys (Festival lead, bass, keys and vocals pads): lowered for Festival scoring.</summary>
        public static float[] FiveLane => new[] { 0.05f, 0.11f, 0.18f, 0.4f, 0.68f, 1.05f };
        public static float[] Drums => new[] { 0.06f, 0.12f, 0.2f, 0.45f, 0.75f, 1.09f };
        public static float[] ProKeys => new[] { 0.06f, 0.12f, 0.2f, 0.47f, 0.78f, 1.15f };
        public static float[] Vocals => new[] { 0.05f, 0.11f, 0.19f, 0.46f, 0.77f, 1.06f };
        public static float[] SoloBonus => new[] { 0.05f, 0.1f, 0.2f, 0.35f, 0.65f, 0.95f };
    }

    /// <summary>
    /// What a recorded run must be to count on a leaderboard, checked on the run's own setup (the leaderboard server
    /// does this before replaying it). Built the way the game's players build their engines, so keep them in step.
    /// </summary>
    public static class RunRules
    {
        /// <summary>Why the run can't count ("bot", "song_speed", "modifiers", "engine_preset"), or null if it can.</summary>
        public static string? Refuse(ReplayFrame frame)
        {
            if (frame.Profile.IsBot) return "bot";
            if (frame.EngineParameters.SongSpeed != 1) return "song_speed";
            if (frame.Profile.CurrentModifiers != Modifier.None) return "modifiers";
            return UsesDefaultEngine(frame) ? null : "engine_preset";
        }

        /// <summary>The run's engine settings are exactly the default preset's, as the game makes them for its part.</summary>
        public static bool UsesDefaultEngine(ReplayFrame frame)
        {
            var actual = frame.EngineParameters;
            var expected = DefaultParameters(frame.Profile, actual);
            if (expected == null || expected.GetType() != actual.GetType()) return false;
            expected.SongSpeed = actual.SongSpeed;
            return Bytes(expected).SequenceEqual(Bytes(actual));
        }

        // The game's own thresholds (stars come from the replay, so a run can't bring easier ones); only the vocals
        // mic rate and sing-to-activate setting come from the run, as they're per-PC settings
        private static BaseEngineParameters? DefaultParameters(YargProfile profile, BaseEngineParameters actual)
        {
            var preset = EnginePreset.Default;
            var solo = StarThresholds.SoloBonus;
            var instrument = profile.CurrentInstrument;
            switch (profile.GameMode)
            {
                case GameMode.FiveFretGuitar:
                    bool bass = instrument == Instrument.FiveFretBass;
                    return preset.FiveFretGuitar.Create(bass ? StarThresholds.Bass : StarThresholds.Guitar, solo, bass);
                case GameMode.FourLaneDrums:
                case GameMode.FiveLaneDrums:
                case GameMode.EliteDrums:
                    return actual is DrumsEngineParameters drums ? preset.Drums.Create(StarThresholds.Drums, solo, drums.Mode) : null;
                case GameMode.ProKeys:
                    // Pro keys never gets the bass multiplier; 5-lane keys does on bass and (Festival) on Keys
                    bool isBass = instrument is Instrument.FiveFretBass or Instrument.Keys;
                    return instrument == Instrument.ProKeys
                        ? preset.ProKeys.Create(StarThresholds.ProKeys, solo, false)
                        : preset.ProKeys.Create(StarThresholds.FiveLane, solo, isBass);
                case GameMode.Vocals:
                    return actual is VocalsEngineParameters vocals
                        ? preset.Vocals.Create(StarThresholds.Vocals, solo, profile.CurrentDifficulty, (float) vocals.ApproximateVocalFps,
                            vocals.SingToActivateStarPower)
                        : null;
                default:
                    return null;
            }
        }

        private static byte[] Bytes(BaseEngineParameters parameters)
        {
            using var memory = new MemoryStream();
            using (var writer = new BinaryWriter(memory))
            {
                parameters.Serialize(writer);
            }
            return memory.ToArray();
        }
    }
}
