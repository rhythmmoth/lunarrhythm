using System.IO;
using YARG.Core.Extensions;
using YARG.Core.IO;
using YARG.Core.Replays;

namespace YARG.Core.Engine.Keys
{
    public class KeysStats : BaseStats
    {
        /// <summary>
        /// Number of overhits which have occurred.
        /// </summary>
        public int Overhits;

        /// <summary>
        /// Amount of overhits which were ignored due to fat-fingering.
        /// </summary>
        public int FatFingersIgnored;

        /// <summary>
        /// Notes hit close to their time (Festival "Perfect") and the rest of the hits ("Good").
        /// </summary>
        /// shortcut: not written to replays (that would change the replay format), so replays show 0 for both
        public int PerfectHits;
        public int GoodHits;

        // Combo counts every note (Festival), so the base check against TotalNotes applies

        public KeysStats()
        {
        }

        public KeysStats(KeysStats stats) : base(stats)
        {
            Overhits = stats.Overhits;
            FatFingersIgnored = stats.FatFingersIgnored;
            PerfectHits = stats.PerfectHits;
            GoodHits = stats.GoodHits;
        }

        public KeysStats(ref FixedArrayStream stream, int version)
            : base(ref stream, version)
        {
            Overhits = stream.Read<int>(Endianness.Little);
            FatFingersIgnored = stream.Read<int>(Endianness.Little);
        }

        public override void Reset()
        {
            base.Reset();
            Overhits = 0;
            FatFingersIgnored = 0;
            PerfectHits = 0;
            GoodHits = 0;
        }

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);

            writer.Write(Overhits);
            writer.Write(FatFingersIgnored);
        }

        public override ReplayStats ConstructReplayStats(string name, bool isReplayPlayer)
        {
            return new ProKeysReplayStats(name, isReplayPlayer, this);
        }
    }
}