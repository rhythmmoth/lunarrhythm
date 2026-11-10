using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.IO;
using YARG.Core.Song;
using YARG.Core.Song.Cache;

namespace YARG.Core.UnitTests.Song;

public class FestivalSongTests
{
    private static TrackChunk Track(string name, params int[] notes)
    {
        var chunk = new TrackChunk(new SequenceTrackNameEvent(name));
        foreach (int note in notes)
        {
            chunk.Events.Add(new NoteOnEvent((SevenBitNumber) note, (SevenBitNumber) 100));
            chunk.Events.Add(new NoteOffEvent((SevenBitNumber) note, (SevenBitNumber) 0) { DeltaTime = 120 });
        }
        return chunk;
    }

    [Test]
    public void ConvertMidi_RenamesPadPartsStripsLiftsAndMovesSolos()
    {
        var midi = new MidiFile(
            Track("PART VOCALS", 96, 102),
            Track("PART DRUMS", 97),
            Track("PART GUITAR", 100, 101, 106, 116),
            Track("PART KEYS", 60)); // pre-existing target name must not survive
        using var input = new MemoryStream();
        midi.Write(input);
        input.Position = 0;
        using var bytes = FixedArray.ReadRemainder(input);

        using var converted = FestivalSong.ConvertMidi(bytes);
        using var stream = converted.ToReferenceStream();
        var result = MidiFile.Read(stream);

        var tracks = result.GetTrackChunks().ToDictionary(
            t => t.Events.OfType<SequenceTrackNameEvent>().First().Text,
            t => t.Events.OfType<NoteOnEvent>().Select(e => (int) e.NoteNumber).ToArray());

        Assert.That(tracks.Keys, Is.EquivalentTo(new[] { "PART KEYS", "PART RHYTHM", "PART GUITAR" }));
        Assert.That(tracks["PART KEYS"], Is.EqualTo(new[] { 96 }));
        Assert.That(tracks["PART RHYTHM"], Is.EqualTo(new[] { 97 }));
        Assert.That(tracks["PART GUITAR"], Is.EqualTo(new[] { 100, 103, 116 })); // solo 101 -> 103, lift 106 gone
    }

    [Test]
    public void ConvertMidi_MovesALateTrackNameToTickZero()
    {
        var late = new TrackChunk(new Melanchall.DryWetMidi.Core.TextEvent("x") { DeltaTime = 100 }, new SequenceTrackNameEvent("PART GUITAR") { DeltaTime = 200 },
            new NoteOnEvent((SevenBitNumber) 96, (SevenBitNumber) 100) { DeltaTime = 50 },
            new NoteOffEvent((SevenBitNumber) 96, (SevenBitNumber) 0) { DeltaTime = 60 });
        using var input = new MemoryStream();
        new MidiFile(new TrackChunk(), late).Write(input);
        input.Position = 0;
        using var bytes = FixedArray.ReadRemainder(input);

        using var converted = FestivalSong.ConvertMidi(bytes);
        var track = MidiFile.Read(converted.ToReferenceStream()).GetTrackChunks().Last();
        var times = new List<(string, long)>();
        long tick = 0;
        foreach (var ev in track.Events)
        {
            tick += ev.DeltaTime;
            times.Add((ev.EventType.ToString(), tick));
        }

        Assert.That(times, Is.EqualTo(new[] { ("SequenceTrackName", 0L), ("Text", 100L), ("NoteOn", 350L), ("NoteOff", 410L) }));
    }

    [Test]
    public void SplitMixedChords_SeparatesLiftsAndKeepsPhrasesOnce()
    {
        var flags = NoteFlags.StarPower | NoteFlags.StarPowerStart | NoteFlags.StarPowerEnd;
        var before = new GuitarNote(FiveFretGuitarFret.Yellow, GuitarNoteType.Strum, GuitarNoteFlags.None, NoteFlags.None, 0, 0, 0, 0);
        var mixed = new GuitarNote(FiveFretGuitarFret.Green, GuitarNoteType.Strum, GuitarNoteFlags.None, flags, 1, 0, 480, 0);
        mixed.AddChildNote(new GuitarNote(FiveFretGuitarFret.Red, GuitarNoteType.Strum, GuitarNoteFlags.Lift, flags, 1, 0, 480, 0));
        mixed.AddChildNote(new GuitarNote(FiveFretGuitarFret.Blue, GuitarNoteType.Strum, GuitarNoteFlags.None, flags, 1, 0, 480, 0));
        var after = new GuitarNote(FiveFretGuitarFret.Orange, GuitarNoteType.Strum, GuitarNoteFlags.None, NoteFlags.None, 2, 0, 960, 0);
        before.NextNote = mixed; mixed.PreviousNote = before; mixed.NextNote = after; after.PreviousNote = mixed;
        var notes = new List<GuitarNote> { before, mixed, after };

        FestivalSong.SplitMixedChords(notes);

        Assert.That(notes, Has.Count.EqualTo(4));
        var press = notes[1];
        var lift = notes[2];
        Assert.Multiple(() =>
        {
            Assert.That(press.NoteMask, Is.EqualTo((1 << 0) | (1 << 3)), "green + blue press");
            Assert.That(press.IsLift, Is.False);
            Assert.That(lift.Fret, Is.EqualTo((int) FiveFretGuitarFret.Red));
            Assert.That(lift.IsLift && !lift.IsChord);
            Assert.That(press.IsStarPowerStart && !press.IsStarPowerEnd, "phrase starts on the press half");
            Assert.That(!lift.IsStarPowerStart && lift.IsStarPowerEnd, "phrase ends on the lift half");
            Assert.That(before.NextNote, Is.SameAs(press));
            Assert.That(press.NextNote, Is.SameAs(lift));
            Assert.That(press.ChildNotes[0].NextNote, Is.SameAs(lift));
            Assert.That(lift.NextNote, Is.SameAs(after));
            Assert.That(after.PreviousNote, Is.SameAs(lift));
        });
    }

    [Test]
    public void ReadInfo_MapsMetadata()
    {
        string path = Path.GetTempFileName();
        File.WriteAllText(path, """
            { "title": "SKY", "artist": "1nonly", "release_year": "2025", "genre": ["Rap/Hip-Hop"],
              "charter": ["Luna", "Two"], "length": 134, "preview_start_time": 85853,
              "diff": { "vocals": 5, "drums": 3, "bass": 0, "guitar": 1 } }
            """);
        try
        {
            var ini = FestivalSong.ReadInfo(path);
            Assert.Multiple(() =>
            {
                Assert.That(ini.Extract("name", out string name) && name == "SKY");
                Assert.That(ini.Extract("charter", out string charter) && charter == "Luna, Two");
                Assert.That(ini.Extract("genre", out string genre) && genre == "Rap/Hip-Hop");
                Assert.That(ini.Extract("song_length", out long length) && length == 134000);
                Assert.That(ini.Extract("diff_keys", out int vocals) && vocals == 5);
                Assert.That(ini.Extract("diff_rhythm", out int drums) && drums == 3);
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void ReadLyrics_HandlesLineWordAndInlineTiming()
    {
        string path = Path.GetTempFileName();
        File.WriteAllText(path, """
            [ti:lyrics]
            [00:10.00]Plain line here
            [00:12.00]
            [00:14.00]{agent:v1}Timed words
            <Timed:14.1:14.5|words:14.5:15.0>
            [00:20.00]There's <00:20.50>no <00:21.00>doubt
            [00:19.00]Out of order
            """);
        try
        {
            var sync = new SyncTrack(480);
            sync.Tempos.Add(new TempoChange(120, 0, 0));
            var phrases = FestivalSong.ReadLyrics(path, sync).Phrases;

            Assert.That(phrases.Select(p => string.Join(" ", p.Lyrics.Select(l => l.Text))),
                Is.EqualTo(new[] { "Plain line here", "Timed words", "Out of order", "There's no doubt" }));
            Assert.Multiple(() =>
            {
                Assert.That(phrases[0].Time, Is.EqualTo(10.0));
                Assert.That(phrases[0].TimeEnd, Is.EqualTo(12.0)); // blank line ends it
                Assert.That(phrases[1].Lyrics.Select(l => l.Time), Is.EqualTo(new[] { 14.1, 14.5 }));
                Assert.That(phrases[3].Lyrics.Select(l => l.Time), Is.EqualTo(new[] { 20.0, 20.5, 21.0 }));
                Assert.That(phrases[3].TimeEnd, Is.EqualTo(25.0)); // last line gets a fixed duration
                Assert.That(phrases[0].Tick, Is.EqualTo(sync.TimeToTick(10.0)));
                Assert.That(phrases[0].Lyrics[0].LineTimed, Is.True);  // plain line: no highlight
                Assert.That(phrases[1].Lyrics[0].LineTimed, Is.False); // word timed: highlights
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    // Set YARG_TEST_FESTIVAL_DIR to a folder of Festival song folders to run this
    [Test, Category("Integration")]
    public void ScanAndLoad_FestivalLibrary()
    {
        string? dir = Environment.GetEnvironmentVariable("YARG_TEST_FESTIVAL_DIR");
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            Assert.Ignore("Set YARG_TEST_FESTIVAL_DIR to run.");
        }

        string tmp = Path.Combine(Path.GetTempPath(), "festival_scan");
        Directory.CreateDirectory(tmp);
        var cache = CacheHandler.RunScan(false, Path.Combine(tmp, "cache.bin"), Path.Combine(tmp, "bad.txt"), false, new List<string> { dir! });
        var entries = cache.Entries.Values.SelectMany(e => e).ToList();
        // Folders with malformed info.json are expected to land in bad songs instead
        int folders = Directory.GetDirectories(dir!).Count(d =>
        {
            try { Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(d, FestivalSong.INFO_FILE))); return true; }
            catch { return false; }
        });
        TestContext.Out.WriteLine($"{entries.Count} entries from {folders} info.json folders");
        if (File.Exists(Path.Combine(tmp, "bad.txt")))
        {
            TestContext.Out.WriteLine(File.ReadAllText(Path.Combine(tmp, "bad.txt")));
        }

        int failed = 0;
        int lifts = 0;
        int withLyrics = 0;
        int solos = 0;
        foreach (var entry in entries)
        {
            var chart = entry.LoadChart();
            var parts = new[] { chart!.FiveFretGuitar, chart.FiveFretBass, chart.FiveFretRhythm, chart.Keys };
            withLyrics += chart.Lyrics.IsEmpty ? 0 : 1;
            if (!chart.Lyrics.IsEmpty && !File.Exists(Path.Combine(entry.ActualLocation, FestivalSong.LYRICS_FILE)))
            {
                TestContext.Out.WriteLine($"lyrics without lyrics.txt: {entry.ActualLocation}");
            }
            foreach (var part in parts)
                foreach (var parent in part.GetDifficulty(Difficulty.Expert).Notes)
                {
                    bool anyLift = false, anyPress = false;
                    foreach (var note in parent.AllNotes)
                    {
                        if (note.IsLift) anyLift = true; else anyPress = true;
                    }
                    Assert.That(anyLift && anyPress, Is.False, $"mixed chord left in {entry} at {parent.Time}");
                }
            solos += parts.Sum(p => p.GetDifficulty(Difficulty.Expert).Phrases.Count(ph => ph.Type == PhraseType.Solo));
            foreach (var part in parts)
                foreach (var parent in part.GetDifficulty(Difficulty.Expert).Notes)
                    foreach (var note in parent.AllNotes)
                        lifts += note.IsLift ? 1 : 0;
            if (parts.All(p => p.GetDifficulty(Difficulty.Expert).Notes.Count == 0))
            {
                failed++;
                TestContext.Out.WriteLine($"no expert notes: {entry}");
            }
        }

        TestContext.Out.WriteLine($"{lifts} expert lift notes");
        Assert.That(lifts, Is.Positive);
        TestContext.Out.WriteLine($"{solos} expert solo sections");
        Assert.That(solos, Is.Positive);
        int lyricFiles = entries.Count(e => File.Exists(Path.Combine(e.ActualLocation, FestivalSong.LYRICS_FILE)));
        TestContext.Out.WriteLine($"{withLyrics} songs with lyrics, {lyricFiles} lyrics files");
        Assert.That(withLyrics, Is.EqualTo(lyricFiles));
        Assert.That(entries.Count, Is.EqualTo(folders));
        Assert.That(failed, Is.Zero);
    }
}
