using UnityEngine;
using YARG.Core.Game;

namespace YARG.Gameplay.Visuals
{
    /// <summary>
    /// The 2D highway (boards NextHighway*): the track seen from straight above, so notes fall flat down the screen.
    /// It's a camera preset, not a new renderer: a camera looking straight down at the flat track, with a narrow field
    /// of view and no curve, goes through the same highway pipeline (layout, fade, multiplayer) as the 3D view. The 3D
    /// view is untouched: turning <see cref="Enabled"/> off (the 2D Highway setting) brings it back.
    /// </summary>
    public static class FlatHighway
    {
        public static bool Enabled => YARG.Settings.SettingsManager.Settings?.FlatHighway.Value ?? true;

        /// <summary>How far up the screen the flat highway sits, in clip space (2 is the screen's height: 0.2 is 10%),
        /// so the strike line isn't down at the bottom edge. The HUD around it follows.</summary>
        public const float Raise = 0.2f;

        /// <summary>How much bigger notes, lifts, their shapes and the strike marks are drawn than the first 2D sizes.</summary>
        public const float NoteScale = 1.25f;

        /// <summary>The widest a note or strike mark gets: 85% of a lane (the track is 2 wide, in 5 lanes), so
        /// neighbours and the rails keep a gap.</summary>
        public const float MaxNoteWidth = 0.34f;

        /// <summary>How much wider than the track's own proportions the flat lanes are drawn.</summary>
        public const float LaneStretch = 2f;

        /// <summary>How far the track leans back from straight down (board NextHighwayDepth, E): far notes shrink a
        /// little, so the highway reads as going into the screen.</summary>
        public const float Tilt = 12f;

        /// <summary>Over the track, from high up with a narrow lens, leaning back <see cref="Tilt"/>; aimed at the same
        /// point of the track as straight down would be. The camera sits at (0, PositionY, PositionZ - 6) on the track
        /// (see CameraPositioner).</summary>
        public static readonly CameraPreset Preset = new("Flat")
        {
            FieldOfView = 20f,
            PositionY = 17f,
            PositionZ = 6.4f - 17f / Mathf.Tan((90f - Tilt) * Mathf.Deg2Rad),
            Rotation = 90f - Tilt,
            FadeLength = 1.25f,
            CurveFactor = 0f,
        };

        /// <summary>The player's highway is flipped (strike line at the top): the Upscroll modifier.</summary>
        public static bool IsUpscroll(YARG.Player.YargPlayer player) => Enabled && player != null && player.Profile.Upscroll;

        /// <summary>A highway look other than the default is worn (an online theme or the player's own preset): the
        /// 2D highway takes its colours.</summary>
        public static bool IsThemed(HighwayPreset preset) =>
            preset != null && preset.BackgroundBaseColor1.ToArgb() != HighwayPreset.Default.BackgroundBaseColor1.ToArgb();

        /// <summary>The camera a player's highway uses: the flat one, or their own 3D preset.</summary>
        public static CameraPreset ViewOf(YARG.Player.YargPlayer player) => Enabled ? Preset : player.CameraPreset;
    }
}
