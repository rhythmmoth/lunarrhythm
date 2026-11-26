using System.Drawing;
using NUnit.Framework;
using YARG.Core.Game;
using YARG.Core.Song;

namespace YARG.Core.UnitTests.Song;

public class OnlineThemesTests
{
    private static readonly string[] Pastel = { "#9be8b0", "#ffb3c1", "#fff1a8", "#a8d8ff", "#ffcf9e" };

    [Test]
    public void NoteThemesColourEveryLaneAndLeaveTheOriginalAlone()
    {
        var mine = ColorProfile.Default;
        var themed = OnlineThemes.Notes(mine, Pastel);
        var green = Color.FromArgb(255, 0x9b, 0xe8, 0xb0);
        var orange = Color.FromArgb(255, 0xff, 0xcf, 0x9e);
        Assert.That(themed.FiveFretGuitar.GreenNote.ToArgb(), Is.EqualTo(green.ToArgb()));
        Assert.That(themed.FiveFretGuitar.OrangeFret.ToArgb(), Is.EqualTo(orange.ToArgb()));
        Assert.That(themed.FourLaneDrums.KickNote.ToArgb(), Is.EqualTo(orange.ToArgb()), "the kick takes orange");
        Assert.That(themed.FourLaneDrums.GreenDrum.ToArgb(), Is.EqualTo(green.ToArgb()));
        Assert.That(themed.FiveFretGuitar.GreenNoteStarPower.ToArgb(), Is.EqualTo(mine.FiveFretGuitar.GreenNoteStarPower.ToArgb()),
            "star power stays");
        Assert.That(mine.FiveFretGuitar.GreenNote.ToArgb(), Is.Not.EqualTo(green.ToArgb()), "the saved profile is untouched");
        Assert.That(OnlineThemes.Notes(mine, new[] { "#ffffff" }), Is.SameAs(mine), "a bad list changes nothing");
    }

    [Test]
    public void HighwayThemesKeepThePresetsTransparency()
    {
        var mine = HighwayPreset.Default;
        var themed = OnlineThemes.Highway(mine, new[] { "#0b1a2e", "#9fd6ff", "#3a6a8f" });
        Assert.That(themed.BackgroundBaseColor2.A, Is.EqualTo(mine.BackgroundBaseColor2.A));
        Assert.That((themed.BackgroundBaseColor2.R, themed.BackgroundBaseColor2.G, themed.BackgroundBaseColor2.B), Is.EqualTo(((byte) 0x9f, (byte) 0xd6, (byte) 0xff)));
        Assert.That(mine.BackgroundBaseColor1.ToArgb(), Is.EqualTo(HighwayPreset.Default.BackgroundBaseColor1.ToArgb()));
        Assert.That(OnlineThemes.Highway(mine, new[] { "nope", "#000000", "#000000" }), Is.SameAs(mine));
        Assert.That(themed.BackgroundGrooveBaseColor1.ToArgb() & 0xFFFFFF, Is.EqualTo(0x0b1a2e), "a streak keeps the theme");
        Assert.That(themed.BackgroundGroovePatternColor.ToArgb() & 0xFFFFFF, Is.EqualTo(0x3a6a8f));
    }
}
