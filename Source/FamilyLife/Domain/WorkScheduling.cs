using System;
using System.Collections.Generic;

namespace FamilyLife.Domain
{
    /// <summary>Transient tick gate: no catch-up burst, and safe when a save or clock moves backwards.</summary>
    public sealed class TickGate
    {
        private readonly int interval, minimum;
        private int last = -1;
        private bool invalidated;
        public TickGate(int interval, int minimum = 0)
        {
            if (interval < 1 || minimum < 0 || minimum > interval) throw new ArgumentOutOfRangeException(nameof(interval));
            this.interval = interval; this.minimum = minimum;
        }
        public bool Take(int now)
        {
            if (last >= 0 && now >= last && now - last < interval && (!invalidated || now - last < minimum)) return false;
            last = now;
            invalidated = false;
            return true;
        }
        public void Invalidate() => invalidated = true;
    }

    /// <summary>A short-lived spatial index. Queries return only items within the requested circular radius.</summary>
    public sealed class NearbyIndex<T>
    {
        private readonly int cellSize;
        private readonly Dictionary<(int, int), List<Entry>> cells = new Dictionary<(int, int), List<Entry>>();
        private struct Entry { public T Item; public int X, Z; }
        public NearbyIndex(int cellSize) { if (cellSize < 1) throw new ArgumentOutOfRangeException(nameof(cellSize)); this.cellSize = cellSize; }
        private int Cell(int x) => (int)Math.Floor(x / (double)cellSize);
        public void Add(T item, int x, int z)
        {
            var key = (Cell(x), Cell(z));
            if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<Entry>();
            list.Add(new Entry { Item = item, X = x, Z = z });
        }
        public IEnumerable<T> Within(int x, int z, int radius, int offset = 0)
        {
            long squared = (long)radius * radius;
            foreach (var e in Entries(x, z, radius, offset))
            {
                long dx = (long)e.X - x, dz = (long)e.Z - z;
                if (dx * dx + dz * dz <= squared) yield return e.Item;
            }
        }
        /// <summary>Raw bounding-cell candidates let callers charge even rejected distances against a work budget.</summary>
        public IEnumerable<T> Candidates(int x, int z, int radius, int offset = 0)
        {
            foreach (var e in Entries(x, z, radius, offset)) yield return e.Item;
        }
        private IEnumerable<Entry> Entries(int x, int z, int radius, int offset)
        {
            if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
            int minX = Cell(x - radius), minZ = Cell(z - radius);
            int width = Cell(x + radius) - minX + 1, height = Cell(z + radius) - minZ + 1;
            int count = width * height;
            int rotation = offset & int.MaxValue;
            for (int n = 0; n < count; n++)
            {
                int cell = (n + rotation % count) % count;
                if (!cells.TryGetValue((minX + cell / height, minZ + cell % height), out var list)) continue;
                for (int k = 0; k < list.Count; k++) yield return list[(k + rotation % list.Count) % list.Count];
            }
        }
    }

    /// <summary>Rotates the first item considered, even when a previous search used up its budget.</summary>
    public sealed class SearchCursor
    {
        private int next;
        public int Take(int count)
        {
            if (count <= 0) { next = 0; return 0; }
            int start = next % count;
            next = (start + 1) % count;
            return start;
        }
        public void Reset() => next = 0;
        public void ResumeAfter(int index, int count) => next = count <= 0 ? 0 : (index + 1) % count;
    }
}
