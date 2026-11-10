using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Keys;
using YARG.Core.Engine.Keys.Engines;
using YARG.Core.Input;
using YARG.Core.Song;

namespace YARG.Core.UnitTests.Engine;

public class KeysEngineTester : EngineTester
{
    [Test]
    public void MatchingKeyBeforeChordTremoloLaneStart_DoesNotOverhit()
    {
        var (engine, notes) = CreateChordTremoloLaneProximityEngine();

        PressKey(engine, notes.Notes[0].Time, ProKeysAction.GreenKey);
        ReleaseKey(engine, 0.1, ProKeysAction.GreenKey);
        PressKey(engine, notes.Notes[1].Time - 0.2, ProKeysAction.GreenKey);

        Assert.That(engine.EngineStats.Overhits, Is.Zero);
    }

    [Test]
    public void MismatchedKeyBeforeChordTremoloLaneStart_RecordsOverhit()
    {
        var (engine, notes) = CreateChordTremoloLaneProximityEngine();

        int? overhitKey = null;
        engine.OnOverhit += key => overhitKey = key;

        PressKey(engine, notes.Notes[0].Time, ProKeysAction.GreenKey);
        ReleaseKey(engine, 0.1, ProKeysAction.GreenKey);
        PressKey(engine, notes.Notes[1].Time - 0.2, ProKeysAction.BlueKey);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1));
            Assert.That(overhitKey, Is.EqualTo((int) FiveLaneKeysEngine.FiveLaneKeysAction.BlueKey));
        }
    }

    [Test]
    public void MatchingTopKeyBeforeTrillLaneStart_DoesNotOverhit()
    {
        var (engine, notes) = CreateTrillLaneProximityEngine();

        PressKey(engine, notes.Notes[0].Time, ProKeysAction.GreenKey);
        ReleaseKey(engine, 0.1, ProKeysAction.GreenKey);
        PressKey(engine, notes.Notes[1].Time - 0.2, ProKeysAction.OrangeKey);

        Assert.That(engine.EngineStats.Overhits, Is.Zero);
    }

    [Test]
    public void MatchingBottomKeyBeforeTrillLaneStart_DoesNotOverhit()
    {
        var (engine, notes) = CreateTrillLaneProximityEngine();

        PressKey(engine, notes.Notes[0].Time, ProKeysAction.GreenKey);
        ReleaseKey(engine, 0.1, ProKeysAction.GreenKey);
        PressKey(engine, notes.Notes[1].Time - 0.2, ProKeysAction.GreenKey);

        Assert.That(engine.EngineStats.Overhits, Is.Zero);
    }

    [Test]
    public void MismatchedKeyBeforeTrillLaneStart_DoesNotOverhit()
    {
        var (engine, notes) = CreateTrillLaneProximityEngine();

        int? overhitKey = null;
        engine.OnOverhit += key => overhitKey = key;

        PressKey(engine, notes.Notes[0].Time, ProKeysAction.GreenKey);
        ReleaseKey(engine, 0.1, ProKeysAction.GreenKey);
        PressKey(engine, notes.Notes[1].Time - 0.2, ProKeysAction.YellowKey);

        Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1));
        Assert.That(overhitKey, Is.EqualTo((int) FiveLaneKeysEngine.FiveLaneKeysAction.YellowKey));
    }

    [Test]
    public void Lift_ReleaseInWindow_Hits()
    {
        var engine = CreateLiftEngine(false);

        PressKey(engine, 0.0, ProKeysAction.GreenKey);
        ReleaseKey(engine, 0.5, ProKeysAction.GreenKey);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
        }
    }

    [Test]
    public void Lift_PressAheadOfLift_NeitherHitsNorOverhits()
    {
        var engine = CreateLiftEngine(false);

        PressKey(engine, 0.0, ProKeysAction.GreenKey);
        ReleaseKey(engine, 0.1, ProKeysAction.GreenKey);
        PressKey(engine, 0.3, ProKeysAction.GreenKey);
        engine.Update(0.5); // holding the key through the lift's time must not hit it

        using (Assert.EnterMultipleScope())
        {
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
        }

        ReleaseKey(engine, 0.51, ProKeysAction.GreenKey);
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
    }

    [Test]
    public void Lift_ReleaseOutsideWindow_Misses()
    {
        var engine = CreateLiftEngine(false);

        PressKey(engine, 0.0, ProKeysAction.GreenKey);
        ReleaseKey(engine, 0.1, ProKeysAction.GreenKey);
        engine.Update(1.0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
            Assert.That(engine.EngineStats.Combo, Is.Zero);
        }
    }

    [Test]
    public void Lift_AfterSustainOnSameLane_HitByReleasingTheSustain()
    {
        var sustain = new GuitarNote(FiveFretGuitarFret.Green, GuitarNoteType.Strum, GuitarNoteFlags.None, NoteFlags.None, 0.0, 0.45, 0, 432);
        var lift = CreateLift(FiveFretGuitarFret.Green, 0.5, 480);
        LinkNotes(sustain, lift);
        var notes = new InstrumentDifficulty<GuitarNote>(Instrument.Keys, Difficulty.Expert, [sustain, lift], new(), new());
        var engine = new YargFiveLaneKeysEngine(notes, CreateSyncTrack(), new KeysEngineParameters(
            CreateHitWindowSettings(), 4, 0, 0, StarMultiplierThresholds, SoloBonusStarMultiplierThresholds,
            0.05, 0, false, true), false);

        PressKey(engine, 0.0, ProKeysAction.GreenKey);
        ReleaseKey(engine, 0.49, ProKeysAction.GreenKey);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
        }
    }

    [Test]
    public void Lift_Bot_HitsEverything()
    {
        var engine = CreateLiftEngine(true);

        engine.Update(1.5);

        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(4)); // the chord counts as two notes
    }

    [Test]
    public void FestivalScoring_DelayedMultiplierAndPerNoteCombo()
    {
        // 11 single notes, then a 2-note chord: 13 notes in combo
        var notes = new List<GuitarNote>();
        for (int i = 0; i < 11; i++)
        {
            notes.Add(CreateNote(FiveFretGuitarFret.Green, NoteFlags.None, i * 0.5, (uint) (i * 480)));
        }
        var chord = CreateNote(FiveFretGuitarFret.Green, NoteFlags.None, 5.5, 5280);
        chord.AddChildNote(CreateNote(FiveFretGuitarFret.Red, NoteFlags.None, 5.5, 5280));
        notes.Add(chord);
        LinkNotes(notes.ToArray());

        var engine = new YargFiveLaneKeysEngine(
            new InstrumentDifficulty<GuitarNote>(Instrument.FiveFretGuitar, Difficulty.Expert, notes, new(), new()),
            CreateSyncTrack(), CreateFestivalParams(4), true);
        engine.Update(7.0);

        using (Assert.EnterMultipleScope())
        {
            // Notes 1-10 at 1x, notes 11-13 at 2x (the 10th note doesn't get its own bump)
            Assert.That(engine.EngineStats.TotalScore, Is.EqualTo(10 * 36 + 3 * 36 * 2));
            Assert.That(engine.EngineStats.MaxCombo, Is.EqualTo(13));
            Assert.That(engine.EngineStats.IsFullCombo, Is.True);
            Assert.That(engine.EngineStats.TotalScore, Is.EqualTo(engine.BaseScore));
        }
    }

    [Test]
    public void FestivalScoring_GoodHitsScoreLessThanPerfect()
    {
        var notes = new[]
        {
            CreateNote(FiveFretGuitarFret.Green, NoteFlags.None, 0.0, 0),
            CreateNote(FiveFretGuitarFret.Green, NoteFlags.None, 1.0, 960),
        };
        LinkNotes(notes);
        // Wide window so a hit 80ms late is still in it but outside the Perfect window
        var hitWindow = new HitWindowSettings(0.3, 0.3, 1.0, false, 0, 1.0, 1.0, 0.15, 0.25);
        var engine = new YargFiveLaneKeysEngine(
            new InstrumentDifficulty<GuitarNote>(Instrument.FiveFretGuitar, Difficulty.Expert, notes.ToList(), new(), new()),
            CreateSyncTrack(),
            new KeysEngineParameters(hitWindow, 4, 0, 0, StarMultiplierThresholds, SoloBonusStarMultiplierThresholds,
                0.05, 0, false, true),
            false);

        PressKey(engine, 0.0, ProKeysAction.GreenKey);
        ReleaseKey(engine, 0.1, ProKeysAction.GreenKey);
        PressKey(engine, 1.08, ProKeysAction.GreenKey);
        engine.Update(2.0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That((engine.EngineStats.PerfectHits, engine.EngineStats.GoodHits), Is.EqualTo((1, 1)));
            Assert.That(engine.EngineStats.TotalScore, Is.EqualTo(36 + FiveLaneKeysEngine.GOOD_POINTS_PER_NOTE));
            Assert.That(engine.EngineStats.TotalScore, Is.LessThan(engine.BaseScore));
        }
    }

    [Test]
    public void FestivalScoring_SolosGiveNoBonus()
    {
        // CHOpt's Festival ceilings have no solo bonus, so a played solo must not push a score past them
        var first = CreateNote(FiveFretGuitarFret.Green, NoteFlags.Solo | NoteFlags.SoloStart, 0.0, 0);
        var last = CreateNote(FiveFretGuitarFret.Red, NoteFlags.Solo | NoteFlags.SoloEnd, 0.5, 480);
        LinkNotes(first, last);
        var engine = new YargFiveLaneKeysEngine(
            new InstrumentDifficulty<GuitarNote>(Instrument.FiveFretGuitar, Difficulty.Expert, [first, last], new(), new()),
            CreateSyncTrack(), CreateFestivalParams(4), true);
        engine.Update(2.0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
            Assert.That(engine.EngineStats.SoloBonuses, Is.Zero);
            Assert.That(engine.EngineStats.MaxSoloBonusPoints, Is.Zero, "stars don't expect one either");
            Assert.That(engine.EngineStats.TotalScore, Is.EqualTo(engine.BaseScore));
        }
    }

    [Test]
    public void FestivalScoring_BassAndVocalsReachSixTimes()
    {
        var notes = new List<GuitarNote>();
        for (int i = 0; i < 60; i++)
        {
            notes.Add(CreateNote(FiveFretGuitarFret.Green, NoteFlags.None, i * 0.25, (uint) (i * 240)));
        }
        LinkNotes(notes.ToArray());

        var engine = new YargFiveLaneKeysEngine(
            new InstrumentDifficulty<GuitarNote>(Instrument.Keys, Difficulty.Expert, notes, new(), new()),
            CreateSyncTrack(), CreateFestivalParams(6), true);
        engine.Update(20.0);

        Assert.That(engine.EngineStats.ScoreMultiplier, Is.EqualTo(6));
    }

    // An overdrive phrase at 0 (hit, so overdrive is ready), then a green note at 2.0
    private static YargFiveLaneKeysEngine CreateOverdriveReadyEngine(bool overdriveHitsNotes)
    {
        var phrase = CreateNote(FiveFretGuitarFret.Green, NoteFlags.StarPower | NoteFlags.StarPowerStart | NoteFlags.StarPowerEnd, 0.0, 0);
        var due = CreateNote(FiveFretGuitarFret.Green, NoteFlags.None, 2.0, 1920);
        LinkNotes(phrase, due);
        // Overdrive's end is measured in measures, which needs a time signature (CreateSyncTrack has none)
        var sync = CreateSyncTrack();
        sync.TimeSignatures.Add(new TimeSignatureChange(4, 4, 0.0, 0, 0, 0, 0, 0));
        var engine = new YargFiveLaneKeysEngine(
            new InstrumentDifficulty<GuitarNote>(Instrument.FiveFretGuitar, Difficulty.Expert, [phrase, due], new(), new()),
            sync, CreateFestivalParams(4), false) { OverdriveHitsNotes = overdriveHitsNotes };
        PressKey(engine, 0.0, ProKeysAction.GreenKey);
        ReleaseKey(engine, 0.1, ProKeysAction.GreenKey);
        Assert.That(engine.CanStarPowerActivate, Is.True);
        return engine;
    }

    [Test]
    public void Overdrive_ActivatingWithNoKeyHeldHitsTheDueNote()
    {
        var engine = CreateOverdriveReadyEngine(true);
        PressKey(engine, 2.02, ProKeysAction.StarPower);
        engine.Update(2.2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
            Assert.That(engine.EngineStats.IsStarPowerActive, Is.True);
            Assert.That(engine.EngineStats.PerfectHits, Is.EqualTo(2), "judged by the activation, 20 ms late");
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
        }
    }

    // Overdrive ready, then a green sustain at 2.0 (0.45 s long) and a lift at 2.5
    private static YargFiveLaneKeysEngine CreateOverdriveSustainEngine(FiveFretGuitarFret liftFret)
    {
        var phrase = CreateNote(FiveFretGuitarFret.Green, NoteFlags.StarPower | NoteFlags.StarPowerStart | NoteFlags.StarPowerEnd, 0.0, 0);
        var sustain = new GuitarNote(FiveFretGuitarFret.Green, GuitarNoteType.Strum, GuitarNoteFlags.None, NoteFlags.None, 2.0, 0.45, 1920, 432);
        var lift = CreateLift(liftFret, 2.5, 2400);
        LinkNotes(phrase, sustain, lift);
        var sync = CreateSyncTrack();
        sync.TimeSignatures.Add(new TimeSignatureChange(4, 4, 0.0, 0, 0, 0, 0, 0));
        var engine = new YargFiveLaneKeysEngine(
            new InstrumentDifficulty<GuitarNote>(Instrument.FiveFretGuitar, Difficulty.Expert, [phrase, sustain, lift], new(), new()),
            sync, CreateFestivalParams(4), false) { OverdriveHitsNotes = true };
        PressKey(engine, 0.0, ProKeysAction.GreenKey);
        ReleaseKey(engine, 0.1, ProKeysAction.GreenKey);
        return engine;
    }

    [TestCase(FiveFretGuitarFret.Green)]
    [TestCase(FiveFretGuitarFret.Red)]
    public void Overdrive_HoldingTheKeyHoldsTheSustainAndLettingGoHitsTheLift(FiveFretGuitarFret liftFret)
    {
        var engine = CreateOverdriveSustainEngine(liftFret);
        PressKey(engine, 2.0, ProKeysAction.StarPower);
        engine.Update(2.3);
        Assert.That(engine.EngineStats.PendingScore, Is.GreaterThan(0), "sustain still held mid-way");
        ReleaseKey(engine, 2.49, ProKeysAction.StarPower);
        engine.Update(3.0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(3));
            Assert.That(engine.EngineStats.Combo, Is.EqualTo(3));
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
        }
    }

    [Test]
    public void Overdrive_LettingGoEarlyDropsTheSustain()
    {
        var engine = CreateOverdriveSustainEngine(FiveFretGuitarFret.Green);
        PressKey(engine, 2.0, ProKeysAction.StarPower);
        ReleaseKey(engine, 2.05, ProKeysAction.StarPower);
        engine.Update(2.3);
        Assert.That(engine.EngineStats.PendingScore, Is.Zero);
    }

    [Test]
    public void Overdrive_DoesNotHitWhileAFretKeyIsHeld()
    {
        var engine = CreateOverdriveReadyEngine(true);
        PressKey(engine, 1.5, ProKeysAction.RedKey); // held, and not the due note's key
        PressKey(engine, 2.0, ProKeysAction.StarPower);
        engine.Update(2.2);
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
    }

    [Test]
    public void Overdrive_DoesNotHitWhenTheSettingIsOff()
    {
        var engine = CreateOverdriveReadyEngine(false);
        PressKey(engine, 2.0, ProKeysAction.StarPower);
        engine.Update(2.2);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
            Assert.That(engine.EngineStats.IsStarPowerActive, Is.True, "overdrive still activates");
        }
    }

    [Test]
    public void Overdrive_CanActivateAfterOnePhrase()
    {
        var phrase = CreateNote(FiveFretGuitarFret.Green, NoteFlags.StarPower | NoteFlags.StarPowerStart | NoteFlags.StarPowerEnd, 0.0, 0);
        var after = CreateNote(FiveFretGuitarFret.Green, NoteFlags.None, 2.0, 1920);
        LinkNotes(phrase, after);

        var engine = new YargFiveLaneKeysEngine(
            new InstrumentDifficulty<GuitarNote>(Instrument.FiveFretGuitar, Difficulty.Expert, [phrase, after], new(), new()),
            CreateSyncTrack(), CreateFestivalParams(4), false);
        PressKey(engine, 0.0, ProKeysAction.GreenKey);
        ReleaseKey(engine, 0.1, ProKeysAction.GreenKey);

        Assert.That(engine.CanStarPowerActivate, Is.True);
    }

    private static KeysEngineParameters CreateFestivalParams(int maxMultiplier)
    {
        return new KeysEngineParameters(CreateHitWindowSettings(), maxMultiplier, 0, 0, StarMultiplierThresholds,
            SoloBonusStarMultiplierThresholds, 0.05, 0, false, true);
    }

    [Test]
    public void Judgement_PerfectNearNoteGoodFurtherOut()
    {
        var tap = CreateNote(FiveFretGuitarFret.Green, NoteFlags.None, 0.0, 0);
        var late = CreateNote(FiveFretGuitarFret.Red, NoteFlags.None, 0.5, 480);
        var lift = CreateLift(FiveFretGuitarFret.Red, 1.0, 960);
        LinkNotes(tap, late, lift);
        var festivalWindow = new HitWindowSettings(0.25, 0.25, 1.0, false, 0, 1.0, 1.0, 0.15, 0.25); // +-125 ms
        var engine = new YargFiveLaneKeysEngine(
            new InstrumentDifficulty<GuitarNote>(Instrument.FiveFretGuitar, Difficulty.Expert, [tap, late, lift], new(), new()),
            CreateSyncTrack(),
            new KeysEngineParameters(festivalWindow, 4, 0, 0, StarMultiplierThresholds, SoloBonusStarMultiplierThresholds,
                0.125, 0, false, true),
            false);
        var judged = new List<bool>();
        engine.OnHitJudged += judged.Add;

        PressKey(engine, 0.01, ProKeysAction.GreenKey);   // 10 ms late: Perfect
        PressKey(engine, 0.58, ProKeysAction.RedKey);     // 80 ms late: Good
        ReleaseKey(engine, 0.98, ProKeysAction.RedKey);   // lift judged by its release, 20 ms early: Perfect

        using (Assert.EnterMultipleScope())
        {
            Assert.That(judged, Is.EqualTo(new[] { true, false, true }));
            Assert.That(engine.EngineStats.PerfectHits, Is.EqualTo(2));
            Assert.That(engine.EngineStats.GoodHits, Is.EqualTo(1));
        }
    }

    [Test]
    public void Chord_TappedWithKeysApart_Hits()
    {
        var chord = CreateNote(FiveFretGuitarFret.Red, NoteFlags.None, 1.0, 960);
        chord.AddChildNote(CreateNote(FiveFretGuitarFret.Yellow, NoteFlags.None, 1.0, 960));
        LinkNotes(chord);
        var engine = new YargFiveLaneKeysEngine(
            new InstrumentDifficulty<GuitarNote>(Instrument.FiveFretGuitar, Difficulty.Expert, [chord], new(), new()),
            CreateSyncTrack(), CreateFestivalParams(4), false);

        // Pads: red is tapped and already up again before yellow lands 40 ms later
        PressKey(engine, 0.98, ProKeysAction.RedKey);
        ReleaseKey(engine, 1.0, ProKeysAction.RedKey);
        PressKey(engine, 1.02, ProKeysAction.YellowKey);
        engine.Update(2.0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
            Assert.That(engine.EngineStats.PerfectHits, Is.EqualTo(2), "each key judged by its own press");
        }
    }

    [Test]
    public void Lift_ReleasedLate_IsGoodAndScoresLess()
    {
        var tap = CreateNote(FiveFretGuitarFret.Green, NoteFlags.None, 0.0, 0);
        var lift = CreateLift(FiveFretGuitarFret.Green, 0.5, 480);
        LinkNotes(tap, lift);
        var festivalWindow = new HitWindowSettings(0.25, 0.25, 1.0, false, 0, 1.0, 1.0, 0.15, 0.25); // +-125 ms
        var engine = new YargFiveLaneKeysEngine(
            new InstrumentDifficulty<GuitarNote>(Instrument.FiveFretGuitar, Difficulty.Expert, [tap, lift], new(), new()),
            CreateSyncTrack(),
            new KeysEngineParameters(festivalWindow, 4, 0, 0, StarMultiplierThresholds, SoloBonusStarMultiplierThresholds,
                0.125, 0, false, true),
            false);

        PressKey(engine, 0.0, ProKeysAction.GreenKey);
        ReleaseKey(engine, 0.58, ProKeysAction.GreenKey); // 80 ms late
        engine.Update(1.0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That((engine.EngineStats.PerfectHits, engine.EngineStats.GoodHits), Is.EqualTo((1, 1)));
            Assert.That(engine.EngineStats.TotalScore, Is.EqualTo(36 + FiveLaneKeysEngine.GOOD_POINTS_PER_NOTE));
        }
    }

    // Green tap, then a split chord at 0.5: red press + green lift (as FestivalSong.SplitMixedChords builds it)
    private static (YargFiveLaneKeysEngine, GuitarNote[]) CreateSplitChordEngine()
    {
        var tap = CreateNote(FiveFretGuitarFret.Green, NoteFlags.None, 0.0, 0);
        var mixed = CreateNote(FiveFretGuitarFret.Red, NoteFlags.None, 0.5, 480);
        mixed.AddChildNote(CreateLift(FiveFretGuitarFret.Green, 0.5, 480));
        var notes = new List<GuitarNote> { tap, mixed };
        LinkNotes(tap, mixed);
        FestivalSong.SplitMixedChords(notes);
        var engine = new YargFiveLaneKeysEngine(
            new InstrumentDifficulty<GuitarNote>(Instrument.FiveFretGuitar, Difficulty.Expert, notes, new(), new()),
            CreateSyncTrack(), CreateFestivalParams(4), false);
        return (engine, notes.ToArray());
    }

    [TestCase(true)]
    [TestCase(false)]
    public void SplitChord_PressAndLiftTogether_BothHit(bool releaseFirst)
    {
        var (engine, notes) = CreateSplitChordEngine();
        Assert.That(notes, Has.Length.EqualTo(3)); // tap, press chord, lift chord

        PressKey(engine, 0.0, ProKeysAction.GreenKey);
        if (releaseFirst)
        {
            ReleaseKey(engine, 0.49, ProKeysAction.GreenKey);
            PressKey(engine, 0.51, ProKeysAction.RedKey);
        }
        else
        {
            PressKey(engine, 0.49, ProKeysAction.RedKey);
            ReleaseKey(engine, 0.51, ProKeysAction.GreenKey);
        }
        engine.Update(1.0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(3));
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
            Assert.That(engine.EngineStats.MaxCombo, Is.EqualTo(3));
        }
    }

    // Fast lift sections. Test hit window is +-0.05s.
    private static YargFiveLaneKeysEngine CreateFastEngine(params GuitarNote[] notes)
    {
        LinkNotes(notes);
        return new YargFiveLaneKeysEngine(
            new InstrumentDifficulty<GuitarNote>(Instrument.FiveFretGuitar, Difficulty.Expert, notes.ToList(), new(), new()),
            CreateSyncTrack(), CreateFestivalParams(4), false);
    }

    [Test]
    public void FastLifts_EarlyReleaseOfNextLiftStillCounts()
    {
        var engine = CreateFastEngine(
            CreateNote(FiveFretGuitarFret.Green, NoteFlags.None, 0.0, 0),
            CreateNote(FiveFretGuitarFret.Red, NoteFlags.None, 0.05, 48),
            CreateLift(FiveFretGuitarFret.Green, 0.10, 96),
            CreateLift(FiveFretGuitarFret.Red, 0.15, 144));

        PressKey(engine, 0.0, ProKeysAction.GreenKey);
        PressKey(engine, 0.05, ProKeysAction.RedKey);
        ReleaseKey(engine, 0.12, ProKeysAction.RedKey);   // red lifted before the green lift resolved
        ReleaseKey(engine, 0.13, ProKeysAction.GreenKey);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(4));
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
        }
    }

    [Test]
    public void FastLifts_PressForNextNoteBeforeLiftIsNotOverhit()
    {
        var engine = CreateFastEngine(
            CreateNote(FiveFretGuitarFret.Green, NoteFlags.None, 0.0, 0),
            CreateLift(FiveFretGuitarFret.Green, 0.10, 96),
            CreateNote(FiveFretGuitarFret.Red, NoteFlags.None, 0.14, 134));

        PressKey(engine, 0.0, ProKeysAction.GreenKey);
        PressKey(engine, 0.11, ProKeysAction.RedKey);     // rolled onto red before letting go of green
        ReleaseKey(engine, 0.12, ProKeysAction.GreenKey);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(3));
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
        }
    }

    [Test]
    public void FastLiftChords_RePressBeforeOtherKeyReleasedStillHits()
    {
        // Girl Hell 1999 drums: red+yellow press chord, lift chord, press chord at 16ths
        var press1 = CreateNote(FiveFretGuitarFret.Red, NoteFlags.None, 0.0, 0);
        press1.AddChildNote(CreateNote(FiveFretGuitarFret.Yellow, NoteFlags.None, 0.0, 0));
        var lift = CreateLift(FiveFretGuitarFret.Red, 0.086, 82);
        lift.AddChildNote(CreateLift(FiveFretGuitarFret.Yellow, 0.086, 82));
        var press2 = CreateNote(FiveFretGuitarFret.Red, NoteFlags.None, 0.172, 165);
        press2.AddChildNote(CreateNote(FiveFretGuitarFret.Yellow, NoteFlags.None, 0.172, 165));
        var engine = CreateFastEngine(press1, lift, press2);

        PressKey(engine, 0.0, ProKeysAction.RedKey);
        PressKey(engine, 0.01, ProKeysAction.YellowKey);
        ReleaseKey(engine, 0.08, ProKeysAction.RedKey);
        PressKey(engine, 0.13, ProKeysAction.RedKey);     // red back down before yellow lets go
        ReleaseKey(engine, 0.135, ProKeysAction.YellowKey);
        PressKey(engine, 0.17, ProKeysAction.YellowKey);
        engine.Update(0.5);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(6));
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
        }
    }

    [Test]
    public void FastLifts_OneReleaseHitsOnlyOneLift()
    {
        var engine = CreateFastEngine(
            CreateNote(FiveFretGuitarFret.Green, NoteFlags.None, 0.0, 0),
            CreateLift(FiveFretGuitarFret.Green, 0.10, 96),
            CreateLift(FiveFretGuitarFret.Green, 0.13, 125));

        PressKey(engine, 0.0, ProKeysAction.GreenKey);
        ReleaseKey(engine, 0.10, ProKeysAction.GreenKey);
        engine.Update(0.5);

        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
    }

    // Green tap at 0.0, green lift at 0.5, red+yellow lift chord at 1.0
    private static YargFiveLaneKeysEngine CreateLiftEngine(bool isBot)
    {
        var tap = CreateNote(FiveFretGuitarFret.Green, NoteFlags.None, 0.0, 0);
        var lift = CreateLift(FiveFretGuitarFret.Green, 0.5, 480);
        var chord = CreateLift(FiveFretGuitarFret.Red, 1.0, 960);
        chord.AddChildNote(CreateLift(FiveFretGuitarFret.Yellow, 1.0, 960));

        LinkNotes(tap, lift, chord);

        var notes = new InstrumentDifficulty<GuitarNote>(Instrument.Keys, Difficulty.Expert,
            [tap, lift, chord], new(), new());
        var engineParams = new KeysEngineParameters(
            CreateHitWindowSettings(),
            4,
            0,
            0,
            StarMultiplierThresholds,
            SoloBonusStarMultiplierThresholds,
            0.05,
            0,
            false,
            true);

        return new YargFiveLaneKeysEngine(notes, CreateSyncTrack(), engineParams, isBot);
    }

    private static GuitarNote CreateLift(FiveFretGuitarFret fret, double time, uint tick)
    {
        return new GuitarNote(fret, GuitarNoteType.Strum, GuitarNoteFlags.Lift, NoteFlags.None, time, 0, tick, 0);
    }

    private static (YargFiveLaneKeysEngine Engine, InstrumentDifficulty<GuitarNote> Notes) CreateChordTremoloLaneProximityEngine()
    {
        var firstNote = CreateNote(FiveFretGuitarFret.Green, NoteFlags.None, 0.0, 0);
        var laneStart = CreateNote(FiveFretGuitarFret.Green, NoteFlags.Tremolo | NoteFlags.LaneStart, 1.0, 480);
        laneStart.AddChildNote(CreateNote(FiveFretGuitarFret.Red, NoteFlags.Tremolo | NoteFlags.LaneStart, 1.0, 480));
        var laneEnd = CreateNote(FiveFretGuitarFret.Green, NoteFlags.Tremolo | NoteFlags.LaneEnd, 1.2, 576);
        laneEnd.AddChildNote(CreateNote(FiveFretGuitarFret.Red, NoteFlags.Tremolo | NoteFlags.LaneEnd, 1.2, 576));

        LinkNotes(firstNote, laneStart, laneEnd);

        var notes = new InstrumentDifficulty<GuitarNote>(Instrument.Keys, Difficulty.Expert,
            [firstNote, laneStart, laneEnd], new(), new());
        var engineParams = new KeysEngineParameters(
            CreateHitWindowSettings(),
            4,
            0,
            0,
            StarMultiplierThresholds,
            SoloBonusStarMultiplierThresholds,
            0.05,
            0,
            false,
            true);

        return (new YargFiveLaneKeysEngine(notes, CreateSyncTrack(), engineParams, false), notes);
    }

    private static (YargFiveLaneKeysEngine Engine, InstrumentDifficulty<GuitarNote> Notes) CreateTrillLaneProximityEngine()
    {
        var firstNote = CreateNote(FiveFretGuitarFret.Green, NoteFlags.None, 0.0, 0);
        var laneStart = CreateNote(FiveFretGuitarFret.Green, NoteFlags.Trill | NoteFlags.LaneStart, 1.0, 480);
        var laneEnd = CreateNote(FiveFretGuitarFret.Orange, NoteFlags.Trill | NoteFlags.LaneEnd, 1.2, 576);

        LinkNotes(firstNote, laneStart, laneEnd);

        var notes = new InstrumentDifficulty<GuitarNote>(Instrument.Keys, Difficulty.Expert,
            [firstNote, laneStart, laneEnd], new(), new());
        var engineParams = new KeysEngineParameters(
            CreateHitWindowSettings(),
            4,
            0,
            0,
            StarMultiplierThresholds,
            SoloBonusStarMultiplierThresholds,
            0.05,
            0,
            false,
            true);

        return (new YargFiveLaneKeysEngine(notes, CreateSyncTrack(), engineParams, false), notes);
    }

    private static GuitarNote CreateNote(FiveFretGuitarFret fret, NoteFlags flags, double time, uint tick)
    {
        return new GuitarNote(fret, GuitarNoteType.Strum, GuitarNoteFlags.None, flags, time, 0, tick, 0);
    }

    private static void LinkNotes(params GuitarNote[] notes)
    {
        for (int i = 0; i < notes.Length; i++)
        {
            if (i > 0)
            {
                notes[i].PreviousNote = notes[i - 1];
            }

            if (i < notes.Length - 1)
            {
                notes[i].NextNote = notes[i + 1];
            }
        }
    }

    private static SyncTrack CreateSyncTrack()
    {
        var syncTrack = new SyncTrack(480);
        syncTrack.Tempos.Add(new TempoChange(120, 0, 0));
        return syncTrack;
    }

    private static HitWindowSettings CreateHitWindowSettings()
    {
        return new HitWindowSettings(0.1, 0.1, 1.0, false, 0, 1.0, 1.0, 0.15, 0.25);
    }

    private static void PressKey(YargFiveLaneKeysEngine engine, double time, ProKeysAction action)
    {
        QueueInput(engine, GameInput.Create(time, action, true));
    }

    private static void ReleaseKey(YargFiveLaneKeysEngine engine, double time, ProKeysAction action)
    {
        QueueInput(engine, GameInput.Create(time, action, false));
    }

    private static void QueueInput(YargFiveLaneKeysEngine engine, GameInput input)
    {
        engine.QueueInput(ref input);
        engine.Update(input.Time);
    }
}
