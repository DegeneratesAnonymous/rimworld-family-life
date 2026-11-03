using System;
using System.Collections.Generic;

namespace FamilyLife.Domain
{
    /// <summary>
    /// Keeps something that is tried over and over (a pawn looking for someone to spend time with, a think-tree entry offered again every
    /// few seconds) from running every time it is asked. Not saved: after a reload everything may be tried once more.
    /// </summary>
    public sealed class AttemptThrottle
    {
        public const int StaleAfterTicks = 60000;        // an entry older than this can no longer block anything useful
        public const int PruneEveryTicks = 5000;         // and is cleared out at most this often
        public const int PruneAbove = 512;

        private readonly Dictionary<long, int> last = new Dictionary<long, int>();
        private bool everPruned;
        private int lastPrune;

        /// <summary>How many times the table has been swept. Exposed so a test can check it is not swept on every call.</summary>
        public int Sweeps { get; private set; }
        public int Count => last.Count;

        private static long Key(int id, int channel) => ((long)channel << 32) | (uint)id;

        /// <summary>True unless this was last noted less than <paramref name="cooldownTicks"/> ago. A clock that went backwards (a loaded save) allows it.</summary>
        public bool Allowed(int id, int now, int cooldownTicks, int channel = 0)
        {
            if (last.TryGetValue(Key(id, channel), out var at) && now >= at && now - at < cooldownTicks) return false;
            return true;
        }

        public void Note(int id, int now, int channel = 0)
        {
            last[Key(id, channel)] = now;
            if (last.Count > PruneAbove && (!everPruned || now < lastPrune || now - lastPrune >= PruneEveryTicks)) Prune(now);
        }

        private void Prune(int now)
        {
            everPruned = true;
            lastPrune = now;
            Sweeps++;
            var old = new List<long>();
            foreach (var kv in last)
                if (now < kv.Value || now - kv.Value > StaleAfterTicks) old.Add(kv.Key);
            foreach (var key in old) last.Remove(key);
        }
    }
}
