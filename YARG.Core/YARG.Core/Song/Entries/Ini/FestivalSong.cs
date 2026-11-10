using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using MoonscraperChartEditor.Song.IO;
using Newtonsoft.Json.Linq;
using YARG.Core.Chart;
using YARG.Core.IO;
using YARG.Core.IO.Ini;

namespace YARG.Core.Song
{
    /// <summary>
    /// Festival/Octave song folders: info.json + notes.mid with four 5-lane pad parts.
    /// The chart is rewritten in memory into tracks YARG already parses as 5-lane parts.
    /// </summary>
    public static class FestivalSong
    {
        public const string INFO_FILE = "info.json";
        public const string LYRICS_FILE = "lyrics.txt";

        // How long the last lyric line stays up when nothing follows it
        private const double LAST_LINE_SECONDS = 5;

        private static readonly Regex LRC_LINE = new(@"^\[(\d+):(\d+(?:\.\d+)?)\](.*)$");
        private static readonly Regex LRC_INLINE_WORD = new(@"<(\d+):(\d+(?:\.\d+)?)>");
        private static readonly Regex LRC_TAG = new(@"\{[^}]*\}");

        // Festival track -> YARG track it is loaded as
        private static readonly (string Festival, string Yarg)[] TRACK_MAP =
        {
            ("PART GUITAR", "PART GUITAR"), // Lead
            ("PART BASS",   "PART BASS"),
            ("PART DRUMS",  "PART RHYTHM"), // pad drums are 5 lanes, not a drum kit
            ("PART VOCALS", "PART KEYS"),   // tap vocals
        };

        private static readonly int[] DIFFICULTY_BASES = { 60, 72, 84, 96 };

        private const int FESTIVAL_SOLO_NOTE = 101;
        private const int YARG_SOLO_NOTE = 103;
        private static readonly Difficulty[] DIFFICULTIES = { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard, Difficulty.Expert };

        // Lift markers sit 6-10 above each difficulty's lane notes and collide with
        // YARG's 5-lane markers (forced strum, solo, tap, player sections)
        public static bool IsLiftMarker(int note)
        {
            return DIFFICULTY_BASES.Any(b => note >= b + 6 && note <= b + 10);
        }

        public static IniModifierCollection ReadInfo(string path)
        {
            var info = JObject.Parse(File.ReadAllText(path));
            var ini = new StringBuilder("[song]\n");

            void Add(string key, JToken? value)
            {
                string? text = value?.Type == JTokenType.Array
                    ? string.Join(", ", value.Values<string>())
                    : value?.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    ini.Append(key).Append(" = ").Append(text!.Replace('\n', ' ').Replace('\r', ' ')).Append('\n');
                }
            }

            Add("name", info["title"]);
            Add("artist", info["artist"]);
            Add("album", info["album"]);
            Add("year", info["release_year"]);
            Add("genre", info["genre"]?.FirstOrDefault() ?? info["genre"]);
            Add("charter", info["charter"]);
            Add("loading_phrase", info["loading_phrase"]);
            Add("preview_start_time", info["preview_start_time"]);
            Add("cover", info["art"]);
            if (info["length"]?.Type is JTokenType.Integer or JTokenType.Float)
            {
                Add("song_length", (long) ((double) info["length"]! * 1000));
            }

            if (info["diff"] is JObject diff)
            {
                Add("diff_guitar", diff["guitar"]);
                Add("diff_bass", diff["bass"]);
                Add("diff_rhythm", diff["drums"]);
                Add("diff_keys", diff["vocals"]);
            }

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ini.ToString()));
            using var bytes = FixedArray.ReadRemainder(stream);
            return SongIniHandler.ReadSongIni(bytes);
        }

        /// <summary>
        /// Flags notes as lifts using the lift markers in the original (unconverted) Festival MIDI.
        /// A marker shares its tick with the lane note it modifies.
        /// </summary>
        public static void ApplyLifts(SongChart chart, FixedArray<byte> originalMidi)
        {
            MidiFile file;
            using (var input = originalMidi.ToReferenceStream())
            {
                file = MidFileLoader.LoadMidiFile(input);
            }

            foreach (var chunk in file.GetTrackChunks())
            {
                var track = chunk.Events.OfType<SequenceTrackNameEvent>().FirstOrDefault()?.Text switch
                {
                    "PART GUITAR" => chart.FiveFretGuitar,
                    "PART BASS"   => chart.FiveFretBass,
                    "PART DRUMS"  => chart.FiveFretRhythm,
                    "PART VOCALS" => chart.Keys,
                    _ => null
                };
                if (track == null)
                {
                    continue;
                }

                // (difficulty, tick, fret) of every lift marker
                var lifts = new HashSet<(int, long, int)>();
                foreach (var note in chunk.GetNotes())
                {
                    for (int d = 0; d < DIFFICULTY_BASES.Length; d++)
                    {
                        int lane = note.NoteNumber - DIFFICULTY_BASES[d] - 6;
                        if (lane >= 0 && lane < 5)
                        {
                            lifts.Add((d, note.Time, lane + (int) FiveFretGuitarFret.Green));
                        }
                    }
                }

                for (int d = 0; d < DIFFICULTIES.Length && lifts.Count > 0; d++)
                {
                    if (!track.TryGetDifficulty(DIFFICULTIES[d], out var difficulty))
                    {
                        continue;
                    }

                    foreach (var parent in difficulty.Notes)
                    {
                        foreach (var note in parent.AllNotes)
                        {
                            if (lifts.Contains((d, note.Tick, note.Fret)))
                            {
                                note.AddGuitarFlags(GuitarNoteFlags.Lift);
                            }
                        }
                    }

                    SplitMixedChords(difficulty.Notes);
                }
            }
        }

        /// <summary>
        /// Reads a lyrics.txt (LRC) file into lyric phrases, one per timed line. Word timing is used when the
        /// line has inline &lt;mm:ss.xx&gt; stamps or is followed by a &lt;word:start:end|...&gt; line;
        /// otherwise the whole line is one lyric.
        /// </summary>
        public static LyricsTrack ReadLyrics(string path, SyncTrack sync)
        {
            static double Seconds(string minutes, string seconds) =>
                int.Parse(minutes, CultureInfo.InvariantCulture) * 60 + double.Parse(seconds, CultureInfo.InvariantCulture);

            var lines = new List<(double Time, string Text, List<(string Word, double Time)> Words)>();
            foreach (var raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                var match = LRC_LINE.Match(line);
                if (match.Success)
                {
                    double time = Seconds(match.Groups[1].Value, match.Groups[2].Value);
                    string text = LRC_TAG.Replace(match.Groups[3].Value, "");

                    var words = new List<(string, double)>();
                    var stamps = LRC_INLINE_WORD.Matches(text);
                    if (stamps.Count > 0)
                    {
                        // Text before the first stamp starts at the line time, each stamp starts the next word
                        int start = 0;
                        double wordTime = time;
                        foreach (Match stamp in stamps)
                        {
                            words.Add((text[start..stamp.Index].Trim(), wordTime));
                            wordTime = Seconds(stamp.Groups[1].Value, stamp.Groups[2].Value);
                            start = stamp.Index + stamp.Length;
                        }
                        words.Add((text[start..].Trim(), wordTime));
                        words.RemoveAll(w => w.Item1.Length == 0);
                        text = LRC_INLINE_WORD.Replace(text, "");
                    }

                    lines.Add((time, text.Trim(), words));
                }
                else if (line.StartsWith("<") && line.EndsWith(">") && lines.Count > 0 && lines[^1].Words.Count == 0)
                {
                    // <word:start:end|word:start:end|...> timing for the line above
                    foreach (var item in line[1..^1].Split('|'))
                    {
                        int end = item.LastIndexOf(':');
                        int start = end > 0 ? item.LastIndexOf(':', end - 1) : -1;
                        if (start > 0 && double.TryParse(item[(start + 1)..end], NumberStyles.Float, CultureInfo.InvariantCulture, out double wordTime))
                        {
                            lines[^1].Words.Add((item[..start], wordTime));
                        }
                    }
                }
            }

            // Some files list lines slightly out of order
            lines = lines.OrderBy(l => l.Time).ToList();

            var phrases = new List<LyricsPhrase>();
            for (int i = 0; i < lines.Count; i++)
            {
                var (time, text, words) = lines[i];
                if (text.Length == 0)
                {
                    continue; // a blank timed line only ends the line before it
                }

                var flags = LyricSymbolFlags.None;
                if (words.Count == 0)
                {
                    words.Add((text, time));
                    flags = LyricSymbolFlags.LineTimed;
                }

                double start = Math.Min(time, words[0].Time);
                double end = i + 1 < lines.Count ? lines[i + 1].Time : time + LAST_LINE_SECONDS;
                end = Math.Max(end, words[^1].Time);

                var lyrics = words.Select(w => new Chart.LyricEvent(flags, w.Word, w.Time, sync.TimeToTick(w.Time))).ToList();
                uint tick = sync.TimeToTick(start);
                phrases.Add(new LyricsPhrase(start, end - start, tick, sync.TimeToTick(end) - tick, lyrics));
            }
            return new LyricsTrack(phrases);
        }

        private const NoteFlags PHRASE_STARTS = NoteFlags.StarPowerStart | NoteFlags.SoloStart | NoteFlags.CodaStart | NoteFlags.LaneStart;
        private const NoteFlags PHRASE_ENDS = NoteFlags.StarPowerEnd | NoteFlags.SoloEnd | NoteFlags.CodaEnd | NoteFlags.LaneEnd;

        /// <summary>
        /// Festival judges chord notes separately, so a chord mixing lifts and presses becomes a press chord
        /// followed by a lift chord at the same time. Phrase starts stay on the first and phrase ends move to
        /// the second, so Overdrive and solo phrases still start and end (and are awarded) once.
        /// </summary>
        internal static void SplitMixedChords(List<GuitarNote> notes)
        {
            for (int i = 0; i < notes.Count; i++)
            {
                var parent = notes[i];
                var presses = new List<GuitarNote>();
                var lifts = new List<GuitarNote>();
                foreach (var note in parent.AllNotes)
                {
                    (note.IsLift ? lifts : presses).Add(note);
                }

                if (presses.Count == 0 || lifts.Count == 0)
                {
                    continue;
                }

                var pressChord = Group(presses);
                var liftChord = Group(lifts);
                foreach (var note in pressChord.AllNotes)
                {
                    note.DeactivateFlag(PHRASE_ENDS);
                }
                foreach (var note in liftChord.AllNotes)
                {
                    note.DeactivateFlag(PHRASE_STARTS);
                }

                Link(parent.PreviousNote, pressChord);
                Link(pressChord, liftChord);
                Link(liftChord, parent.NextNote);

                notes[i] = pressChord;
                notes.Insert(++i, liftChord);
            }

            static GuitarNote Group(List<GuitarNote> chordNotes)
            {
                var chord = chordNotes[0].CloneWithoutChildNotes();
                for (int i = 1; i < chordNotes.Count; i++)
                {
                    chord.AddChildNote(chordNotes[i].CloneWithoutChildNotes());
                }
                return chord;
            }

            // Same forward/backward references as ChartEventExtensions.DuplicateNotes, children included
            static void Link(GuitarNote? previous, GuitarNote? next)
            {
                if (previous != null)
                {
                    foreach (var note in previous.AllNotes)
                    {
                        note.NextNote = next;
                    }
                }
                if (next != null)
                {
                    foreach (var note in next.AllNotes)
                    {
                        note.PreviousNote = previous;
                    }
                }
            }
        }

        public static FixedArray<byte> ConvertMidi(FixedArray<byte> midi)
        {
            MidiFile file;
            using (var input = midi.ToReferenceStream())
            {
                file = MidFileLoader.LoadMidiFile(input);
            }

            string? NameOf(TrackChunk chunk) =>
                chunk.Events.OfType<SequenceTrackNameEvent>().FirstOrDefault()?.Text;

            // Drop anything already using a target name so converted parts never collide with it
            var targets = TRACK_MAP.Select(t => t.Yarg).Except(TRACK_MAP.Select(t => t.Festival)).ToList();
            file.Chunks.RemoveAll(c => c is TrackChunk t && targets.Contains(NameOf(t)));

            foreach (var chunk in file.GetTrackChunks())
            {
                var nameEvent = chunk.Events.OfType<SequenceTrackNameEvent>().FirstOrDefault();
                int index = System.Array.FindIndex(TRACK_MAP, t => t.Festival == nameEvent?.Text);
                if (index < 0)
                {
                    continue;
                }

                // Some exporters put the name later in the track; the scanner only reads names at tick 0
                int at = chunk.Events.IndexOf(nameEvent!);
                if (at > 0 || nameEvent!.DeltaTime > 0)
                {
                    if (at + 1 < chunk.Events.Count)
                    {
                        chunk.Events[at + 1].DeltaTime += nameEvent!.DeltaTime;
                    }
                    chunk.Events.RemoveAt(at);
                    nameEvent!.DeltaTime = 0;
                    chunk.Events.Insert(0, nameEvent);
                }

                nameEvent!.Text = TRACK_MAP[index].Yarg;
                chunk.RemoveNotes(n => IsLiftMarker(n.NoteNumber));

                // Festival marks solos on 101; YARG reads them on 103 (free now that lift markers are gone)
                foreach (var noteEvent in chunk.Events.OfType<NoteEvent>())
                {
                    if (noteEvent.NoteNumber == FESTIVAL_SOLO_NOTE)
                    {
                        noteEvent.NoteNumber = (SevenBitNumber) YARG_SOLO_NOTE;
                    }
                }
            }

            using var output = new MemoryStream();
            file.Write(output, settings: new WritingSettings { TextEncoding = Encoding.UTF8 });
            output.Position = 0;
            return FixedArray.ReadRemainder(output);
        }
    }
}
