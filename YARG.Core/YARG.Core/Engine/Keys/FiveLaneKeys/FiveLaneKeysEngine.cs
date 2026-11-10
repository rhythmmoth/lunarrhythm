using System;
using System.Collections.Generic;
using YARG.Core.Chart;
using YARG.Core.Input;
using YARG.Core.Logging;

namespace YARG.Core.Engine.Keys
{
    public abstract class FiveLaneKeysEngine : KeysEngine<GuitarNote>
    {
        public enum FiveLaneKeysAction {
            GreenKey = 0,
            RedKey = 1,
            YellowKey = 2,
            BlueKey = 3,
            OrangeKey = 4,
            // 5 intentionally left blank for symmetry with 6F
            OpenNote = 6,
            Wildcard = 7,
        }

        public bool IsKeyHeld(FiveLaneKeysAction key)
        {
            return (KeyMask & (1 << (int)key)) != 0;
        }

        protected override double[] KeyPressTimes { get; } = new double[8];

        /// <summary>
        /// When each key was last let go. Lifts are judged by these, separately from press times, so that in
        /// fast sections a key re-pressed for the next note doesn't erase (or get erased by) the lift's release.
        /// </summary>
        protected readonly double[] KeyReleaseTimes = new double[8];

        /// <summary>Keys of a note hit by activating overdrive: they count as held until the overdrive key is let go.</summary>
        protected int OverdriveKeyMask;

        /// <summary>A hit this close to the note's time (either side) is Perfect; the rest of the window is Good.</summary>
        public const double PERFECT_WINDOW = 0.05;

        public delegate void HitJudgedEvent(bool perfect);

        /// <summary>Fired for every note hit with a timing judgement.</summary>
        public HitJudgedEvent? OnHitJudged;

        // Festival scoring (from chopt's Fortnite engines): 36 per note, sustains at 12 per beat (25 on vocals)
        private const int FESTIVAL_POINTS_PER_NOTE = 36;
        // A Good hit scores two thirds of a Perfect. The chart's max score still assumes all Perfects.
        public const int GOOD_POINTS_PER_NOTE = 24;

        private bool _lastHitPerfect = true;

        // Festival scores no solo bonus (CHOpt's ceilings don't include one either)
        protected override int SoloBonusPerNote => 0;

        protected override int SustainPointsPerBeat => Chart.Instrument == Instrument.Keys ? 25 : 12;

        protected FiveLaneKeysEngine(InstrumentDifficulty<GuitarNote> chart, SyncTrack syncTrack,
            KeysEngineParameters engineParameters, bool isBot)
            : base(chart, syncTrack, engineParameters, isBot)
        {
            GetWaitCountdowns(Notes);
        }

        public override void Reset(bool keepCurrentButtons = false)
        {
            Array.Fill(KeyReleaseTimes, DEFAULT_PRESS_TIME);
            OverdriveKeyMask = 0;
            base.Reset(keepCurrentButtons);
        }

        private void JudgeHit(GuitarNote note)
        {
            // Judge by when the key moved (release for a lift), not when the engine got to the note
            double inputTime = note.ParentOrSelf.IsLift
                ? KeyReleaseTimes[(int) note.FiveLaneKeysAction]
                : KeyPressTimes[(int) note.FiveLaneKeysAction];
            if (inputTime <= DEFAULT_PRESS_TIME)
            {
                inputTime = CurrentTime; // no key input behind this hit (e.g. lane autohit)
            }

            bool perfect = Math.Abs(inputTime - note.Time) <= PERFECT_WINDOW;
            _lastHitPerfect = perfect;
            if (perfect)
            {
                EngineStats.PerfectHits++;
            }
            else
            {
                EngineStats.GoodHits++;
            }

            OnHitJudged?.Invoke(perfect);
        }

        // A lift uses up its key's release; any other note uses up the press
        private void ConsumeInput(GuitarNote note)
        {
            if (note.ParentOrSelf.IsLift)
            {
                KeyReleaseTimes[(int) note.FiveLaneKeysAction] = DEFAULT_PRESS_TIME;
            }
            else
            {
                KeyPressTimes[(int) note.FiveLaneKeysAction] = DEFAULT_PRESS_TIME;
            }
        }

        protected override void Overhit(int key)
        {
            if (NoteIndex < Notes.Count && Notes[NoteIndex].IsLift)
            {
                // A lift waits for a release, so in fast sections the next notes get pressed before it resolves.
                // A press for any note already in its window (or the lift's own key) is not a mistake; the
                // press time is kept and used once the lift is out of the way.
                for (int i = NoteIndex; i < Notes.Count && (i == NoteIndex || IsNoteInWindow(Notes[i])); i++)
                {
                    foreach (var note in Notes[i].AllNotes)
                    {
                        if ((int) note.FiveLaneKeysAction == key)
                        {
                            return;
                        }
                    }
                }
            }

            base.Overhit(key);
        }

        protected override bool CanSustainHold(GuitarNote note)
        {
            int held = KeyMask | OverdriveKeyMask;
            return (held & note.DisjointMask) != 0 ||
                (held > 0 && note.FiveLaneKeysAction is FiveLaneKeysAction.Wildcard);
        }

        protected override void HitNote(GuitarNote note)
        {
            if (note.WasHit || note.WasMissed)
            {
                YargLogger.LogFormatTrace("Tried to hit/miss note twice (Key: {0}, Index: {1}, Hit: {2}, Missed: {3})",
                    note.FiveLaneKeysAction, NoteIndex, note.WasHit, note.WasMissed);
                return;
            }

            note.SetHitState(true, false);

            if (!(CodaHasStarted && note.IsBigRockEnding))
            {
                JudgeHit(note);
            }

            ConsumeInput(note);

            // Cancel rest of hit logic during BRE phrase. Key on CodaHasStarted, not IsCodaActive
            // (see DrumsEngine.HitNote): an end-tick finale is judged after the coda's EndTime,
            // where IsCodaActive is already false.
            if (CodaHasStarted && note.IsBigRockEnding)
            {
                base.HitNote(note);
                return;
            }

            // Detect if the last note(s) were skipped
            // bool skipped = SkipPreviousNotes(note);

            if (note.IsStarPower && note.IsStarPowerEnd && note.ParentOrSelf.WasFullyHit())
            {
                AwardStarPower(note);
                EngineStats.StarPowerPhrasesHit++;
            }

            if (note.IsSoloStart)
            {
                StartSolo();
            }

            if (IsSoloActive)
            {
                Solos[CurrentSoloIndex].NotesHit++;
            }

            if (note.IsSoloEnd && note.ParentOrSelf.WasFullyHitOrMissed())
            {
                EndSolo();
            }

            if (note.ParentOrSelf.WasFullyHit())
            {
                ChordStaggerTimer.Disable(CurrentTime, early: true);
            }

            // Festival: every note in a chord counts toward combo, and a note scores at the multiplier
            // from before it was hit (the 10th note still scores at the old multiplier)
            AddScore(note);

            IncrementCombo();

            EngineStats.IncrementNotesHit(note, CurrentTime);

            UpdateMultiplier();

            if (note.IsSustain)
            {
                StartSustain(note);
            }

            OnNoteHit?.Invoke(NoteIndex, note);
            base.HitNote(note);
        }

        protected override void MissNote(GuitarNote note)
        {
            if (note.WasHit || note.WasMissed)
            {
                YargLogger.LogFormatTrace("Tried to hit/miss note twice (Key: {0}, Index: {1}, Hit: {2}, Missed: {3})",
                    note.FiveLaneKeysAction, NoteIndex, note.WasHit, note.WasMissed);
                return;
            }

            ConsumeInput(note);

            // Can't miss a note during the coda. Key on CodaHasStarted, not IsCodaActive (see
            // DrumsEngine.MissNote): the miss is judged at the back-end time, which for an
            // end-tick finale falls after the coda's EndTime where IsCodaActive is already false.
            if (CodaHasStarted && note.IsBigRockEnding)
            {
                // Resolve the whole chord, matching GuitarEngine (see DrumsEngine.MissNote).
                note.SetHitState(true, true);
                base.HitNote(note);
                return;
            }

            note.SetMissState(true, false);

            if (note.IsStarPower)
            {
                StripStarPower(note);
            }

            if (note is { IsSoloStart: true, IsSoloEnd: true } && note.ParentOrSelf.WasFullyHitOrMissed())
            {
                // While a solo is active, end the current solo and immediately start the next.
                if (IsSoloActive)
                {
                    EndSolo();
                    StartSolo();
                }
                else
                {
                    // If no solo is currently active, start and immediately end the solo.
                    StartSolo();
                    EndSolo();
                }
            }
            else if (note.IsSoloEnd && note.ParentOrSelf.WasFullyHitOrMissed())
            {
                EndSolo();
            }
            else if (note.IsSoloStart)
            {
                StartSolo();
            }

            // Festival counts combo per note, so any miss resets it
            ResetCombo();

            UpdateMultiplier();

            if (CodaHasStarted)
            {
                Codas[CurrentCodaIndex].MissNote();
            }

            OnNoteMissed?.Invoke(NoteIndex, note);
            base.MissNote(note);
        }

        protected override void AddScore(GuitarNote note)
        {
            int points = _lastHitPerfect ? FESTIVAL_POINTS_PER_NOTE : GOOD_POINTS_PER_NOTE;
            AddScore(points);
            EngineStats.NoteScore += points;
        }

        protected sealed override (int baseScore, int noteScore) CalculateChartScores()
        {
            double baseScore = 0;
            double noteScore = 0;
            int combo = 0;
            foreach (var note in Notes)
            {
                // Exclude BRE notes from base score calculation since they can't be scored
                if (note.IsBigRockEnding)
                {
                    continue;
                }

                foreach (var child in note.AllNotes)
                {
                    // Multiplier from the combo before this note, as in HitNote
                    int multiplier = Math.Min((combo / 10) + 1, BaseParameters.MaxMultiplier);
                    double scoreForNote = FESTIVAL_POINTS_PER_NOTE + Math.Round(child.TickLength / TicksPerSustainPoint);

                    baseScore += multiplier * scoreForNote;
                    noteScore += scoreForNote;
                    combo++;
                }
            }

            YargLogger.LogDebug($"[Keys] Base score: {baseScore}, Max Combo: {combo}");
            return ((int) Math.Round(baseScore), (int) Math.Round(noteScore));
        }

        // protected override bool IsKeyInTime(GuitarNote note, double frontEnd) => IsKeyInTime(note, (int)note.FiveLaneKeysAction, frontEnd);

        protected override bool IsKeyInTime(GuitarNote note, double frontEnd)
        {
            if (note.Fret != (int) FiveFretGuitarFret.Wildcard)
            {
                return IsKeyInTime(note, (int) note.FiveLaneKeysAction, frontEnd);
            }

            // Check that any key was pressed within the front end
            // TODO: Eliminate this loop by tracking a global LastKeyPressTime or something
            foreach (var pressTime in KeyPressTimes)
            {
                if (pressTime > note.Time + frontEnd)
                {
                    return true;
                }
            }

            return false;
        }

        protected FiveLaneKeysAction ProKeysActionToFiveLaneKeysAction(ProKeysAction action)
        {
            return action switch
            {
                ProKeysAction.GreenKey => FiveLaneKeysAction.GreenKey,
                ProKeysAction.RedKey => FiveLaneKeysAction.RedKey,
                ProKeysAction.YellowKey => FiveLaneKeysAction.YellowKey,
                ProKeysAction.BlueKey => FiveLaneKeysAction.BlueKey,
                ProKeysAction.OrangeKey => FiveLaneKeysAction.OrangeKey,
                ProKeysAction.OpenNote => FiveLaneKeysAction.OpenNote,
                _ => throw new Exception("Unhandled")
            };
        }
    }
}
