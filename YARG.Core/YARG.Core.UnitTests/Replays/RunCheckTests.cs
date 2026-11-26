using Newtonsoft.Json.Linq;
using NUnit.Framework;
using YARG.Core.Engine.Guitar;
using YARG.Core.Game;
using YARG.Core.Input;
using YARG.Core.Replays;
using YARG.Core.Replays.Analyzer;
using YARG.Core.Song;

namespace YARG.Core.UnitTests.Replays;

/// <summary>
/// Real runs replayed the way the leaderboard server checks them: each saved replay's frames against its chart read
/// through <see cref="OnlineSongs.ReadChartWithInfo"/>. Needs LR_TEST_REPLAYS (a replays folder) and LR_TEST_SONGS
/// (song folders named "Artist - Title" with notes.mid and info.json); skipped without them. LR_TEST_SINCE
/// (a date) leaves out older replays.
/// </summary>
public class RunCheckTests
{
    private static ReplayFrame Frame(Instrument instrument, EnginePreset preset, bool bass, Action<YargProfile>? change = null,
        float[]? stars = null)
    {
        var profile = new YargProfile { GameMode = GameMode.FiveFretGuitar, CurrentInstrument = instrument, CurrentDifficulty = Difficulty.Expert };
        change?.Invoke(profile);
        var param = preset.FiveFretGuitar.Create(stars ?? (bass ? StarThresholds.Bass : StarThresholds.Guitar), StarThresholds.SoloBonus, bass);
        param.SongSpeed = 1;
        return new ReplayFrame(profile, param, new GuitarStats(), Array.Empty<GameInput>());
    }

    [Test]
    public void RecordingsRoundTripAndRefuseJunk()
    {
        var plain = Frame(Instrument.FiveFretBass, EnginePreset.Default, true);
        var inputs = Enumerable.Range(0, 3000).Select(i => new GameInput(i * 0.07, i % 8, i % 2 == 0)).ToArray();
        var frame = new ReplayFrame(plain.Profile, plain.EngineParameters, plain.Stats, inputs);

        string recording = Leaderboards.EncodeRecording(frame);
        TestContext.WriteLine($"3000 inputs: {recording.Length} chars");
        var back = Leaderboards.DecodeRecording(recording, 1_000_000);
        Assert.That(back.Inputs, Is.EqualTo(inputs));
        Assert.That(back.Profile.CurrentInstrument, Is.EqualTo(Instrument.FiveFretBass));
        Assert.That(RunRules.Refuse(back), Is.Null);

        Assert.Throws<InvalidDataException>(() => Leaderboards.DecodeRecording(recording, 1000), "too big unpacked");
        Assert.That(() => Leaderboards.DecodeRecording("bm90IGd6aXA=", 1_000_000), Throws.Exception);
    }

    [Test]
    public void ACraftedInputCountIsRefusedWithoutAllocatingIt()
    {
        var plain = Frame(Instrument.FiveFretGuitar, EnginePreset.Default, false);
        using var raw = new MemoryStream();
        using (var writer = new BinaryWriter(raw, System.Text.Encoding.UTF8, true))
        {
            writer.Write(ReplayIO.REPLAY_VERSIONS.CURRENT);
            plain.Serialize(writer); // ends with the input count, 0
        }
        var bytes = raw.ToArray();
        BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, bytes.Length - 4);
        using var packed = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(packed, System.IO.Compression.CompressionLevel.Fastest, true))
        {
            gzip.Write(bytes);
        }
        string recording = Convert.ToBase64String(packed.ToArray());
        long before = GC.GetTotalAllocatedBytes();
        Assert.Throws<InvalidDataException>(() => Leaderboards.DecodeRecording(recording, 1_000_000));
        Assert.That(GC.GetTotalAllocatedBytes() - before, Is.LessThan(10_000_000), "didn't allocate for 2 billion inputs");
    }

    [Test]
    public void OnlyPlainRunsOnTheDefaultEngineCount()
    {
        Assert.That(RunRules.Refuse(Frame(Instrument.FiveFretGuitar, EnginePreset.Default, false)), Is.Null);
        Assert.That(RunRules.Refuse(Frame(Instrument.FiveFretBass, EnginePreset.Default, true)), Is.Null);
        Assert.That(RunRules.Refuse(Frame(Instrument.FiveFretGuitar, EnginePreset.Default, true)), Is.EqualTo("engine_preset"),
            "the bass multiplier on guitar");
        Assert.That(RunRules.Refuse(Frame(Instrument.FiveFretGuitar, EnginePreset.Casual, false)), Is.EqualTo("engine_preset"));
        Assert.That(RunRules.Refuse(Frame(Instrument.FiveFretGuitar, EnginePreset.Default, false, p => p.IsBot = true)), Is.EqualTo("bot"));
        Assert.That(RunRules.Refuse(Frame(Instrument.FiveFretGuitar, EnginePreset.Default, false,
            p => p.AddSingleModifier(Modifier.AllStrums))), Is.EqualTo("modifiers"));
        Assert.That(RunRules.Refuse(Frame(Instrument.FiveFretGuitar, EnginePreset.Default, false,
            stars: new[] { 0.01f, 0.02f, 0.03f, 0.04f, 0.05f, 0.06f })), Is.EqualTo("engine_preset"), "easier star thresholds");
        var slow = Frame(Instrument.FiveFretGuitar, EnginePreset.Default, false);
        slow.EngineParameters.SongSpeed = 0.8;
        Assert.That(RunRules.Refuse(slow), Is.EqualTo("song_speed"));
    }

    [Test]
    public void SavedRunsReplayToTheirOwnScores()
    {
        string? replays = Environment.GetEnvironmentVariable("LR_TEST_REPLAYS");
        string? songs = Environment.GetEnvironmentVariable("LR_TEST_SONGS");
        if (replays == null || songs == null) Assert.Ignore("LR_TEST_REPLAYS / LR_TEST_SONGS not set");

        int passed = 0, failed = 0, skipped = 0;
        string scratch = Path.Combine(Path.GetTempPath(), "lr-runcheck");
        foreach (string file in Directory.EnumerateFiles(replays, "*.replay"))
        {
            var (result, info, data) = ReplayIO.TryDeserialize(file, new ReplayReadOptions());
            string folder = Path.Combine(songs, $"{info?.ArtistName} - {info?.SongName}");
            if (result != ReplayReadResult.Valid || data == null || !File.Exists(Path.Combine(folder, "notes.mid")) || !File.Exists(Path.Combine(folder, "info.json")))
            {
                skipped++;
                continue;
            }
            var chart = OnlineSongs.ReadChartWithInfo(File.ReadAllBytes(Path.Combine(folder, "notes.mid")),
                JObject.Parse(File.ReadAllText(Path.Combine(folder, "info.json"))), scratch);
            if (chart == null || info!.SongSpeed != 1f)
            {
                skipped++;
                continue;
            }
            // Runs saved before the engine and charts last changed can't replay the same; LR_TEST_SINCE skips them
            if (DateTime.TryParse(Environment.GetEnvironmentVariable("LR_TEST_SINCE"), out var since)
                && File.GetLastWriteTime(file) < since)
            {
                skipped++;
                continue;
            }
            foreach (var frame in data.Frames)
            {
                if (RunRules.Refuse(frame) is { } refused)
                {
                    TestContext.WriteLine($"refused ({refused}): {Path.GetFileName(file)}");
                }
                var stats = ReplayAnalyzer.AnalyzeRun(chart, frame).ResultStats;
                var offsets = stats.GetOffsetSamples();
                if (offsets.Count > 0)
                {
                    double mean = offsets.Average(), sd = Math.Sqrt(offsets.Average(x => (x - mean) * (x - mean)));
                    TestContext.WriteLine($"timing {frame.Profile.CurrentInstrument} n={offsets.Count} sd={sd * 1000:0.0}ms mean={mean * 1000:0.0}ms");
                }
                // What the server compares: the score and note counts (other stats differ by float rounding)
                if (stats.TotalScore == frame.Stats.TotalScore && stats.NotesHit == frame.Stats.NotesHit
                    && stats.NotesMissed == frame.Stats.NotesMissed)
                {
                    passed++;
                }
                else
                {
                    failed++;
                    TestContext.WriteLine($"FAIL {Path.GetFileName(file)} {frame.Profile.CurrentInstrument}: " +
                        $"{frame.Stats.TotalScore} -> {stats.TotalScore}, hit {frame.Stats.NotesHit} -> {stats.NotesHit}");
                }
            }
        }
        TestContext.WriteLine($"passed {passed}, failed {failed}, skipped {skipped}");
        Assert.That(passed, Is.GreaterThan(0));
        Assert.That(failed, Is.EqualTo(0));
    }
}
