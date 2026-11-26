using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using YARG.Core.Game;

namespace YARG.Core.Song
{
    /// <summary>
    /// Worn highway and note themes (from the player's online profile), laid over their own presets for a song. The
    /// presets they saved are never touched: these return copies.
    /// </summary>
    public static class OnlineThemes
    {
        // A note theme's five colours, by lane; the drum kick takes orange
        private static readonly string[] Lanes = { "Green", "Red", "Yellow", "Blue", "Orange" };

        /// <summary>A copy of the profile with every instrument's lane colours (notes, frets, cymbals, particles) set
        /// from five #rrggbb colours: green, red, yellow, blue, orange. Star power and activation colours stay.</summary>
        public static ColorProfile Notes(ColorProfile profile, IReadOnlyList<string> colors)
        {
            var copy = (ColorProfile) profile.CopyWithNewName(profile.Name);
            var lanes = colors.Select(Parse).ToArray();
            if (lanes.Length != Lanes.Length || lanes.Any(c => c == null))
            {
                return profile;
            }
            foreach (object instrument in new object[] { copy.FiveFretGuitar, copy.FourLaneDrums, copy.FiveLaneDrums })
            {
                foreach (var field in instrument.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (field.FieldType != typeof(Color) || field.Name.Contains("StarPower") || field.Name.Contains("Activation"))
                    {
                        continue;
                    }
                    string name = field.Name.StartsWith("Kick") ? "Orange" + field.Name : field.Name;
                    int lane = System.Array.FindIndex(Lanes, l => name.StartsWith(l));
                    if (lane >= 0)
                    {
                        field.SetValue(instrument, lanes[lane]!.Value);
                    }
                }
            }
            return copy;
        }

        /// <summary>A copy of the preset with its background colours (normal play, not groove) set from three #rrggbb
        /// colours: base, second base, pattern. Each keeps the preset's own transparency.</summary>
        public static HighwayPreset Highway(HighwayPreset preset, IReadOnlyList<string> colors)
        {
            var parsed = colors.Select(Parse).ToArray();
            if (parsed.Length != 3 || parsed.Any(c => c == null))
            {
                return preset;
            }
            var copy = (HighwayPreset) preset.CopyWithNewName(preset.Name);
            copy.BackgroundBaseColor1 = Color.FromArgb(preset.BackgroundBaseColor1.A, parsed[0]!.Value);
            copy.BackgroundBaseColor2 = Color.FromArgb(preset.BackgroundBaseColor2.A, parsed[1]!.Value);
            copy.BackgroundPatternColor = Color.FromArgb(preset.BackgroundPatternColor.A, parsed[2]!.Value);
            // The streak ("groove") look too, or a streak brings back the player's own colours
            copy.BackgroundGrooveBaseColor1 = Color.FromArgb(preset.BackgroundGrooveBaseColor1.A, parsed[0]!.Value);
            copy.BackgroundGrooveBaseColor2 = Color.FromArgb(preset.BackgroundGrooveBaseColor2.A, parsed[1]!.Value);
            copy.BackgroundGroovePatternColor = Color.FromArgb(preset.BackgroundGroovePatternColor.A, parsed[2]!.Value);
            return copy;
        }

        private static Color? Parse(string? hex) =>
            hex != null && hex.Length == 7 && hex[0] == '#' &&
            int.TryParse(hex.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb)
                ? Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF)
                : null;
    }
}
