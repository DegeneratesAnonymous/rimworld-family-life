using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace FamilyLife.Domain
{
    /// <summary>Opt-in diagnostics. Disabled scopes do not allocate, read a clock, or format text.</summary>
    public sealed class PerformanceProbe
    {
        private sealed class Metric { public long Calls, Total, Max, Allocated; }
        private readonly Dictionary<string, Metric> metrics = new Dictionary<string, Metric>();
        private long started;
        private int startTick;
        public bool Enabled { get; private set; }
        public Scope Measure(string name) => Enabled ? new Scope(this, name) : default;
        public void Start(int tick) { metrics.Clear(); startTick = tick; started = Stopwatch.GetTimestamp(); Enabled = true; }
        public string Stop(int tick)
        {
            Enabled = false;
            double seconds = (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;
            var sb = new StringBuilder("[Wil's Family Life] Performance capture (inclusive timings; nested rows must not be summed)\n");
            sb.AppendLine($"Ticks: {tick - startTick}; wall seconds: {seconds:0.00}; observed TPS: {(tick - startTick) / Math.Max(0.001, seconds):0.0}");
            foreach (var p in metrics.OrderByDescending(p => p.Value.Total))
            {
                double total = p.Value.Total * 1000.0 / Stopwatch.Frequency;
                double max = p.Value.Max * 1000.0 / Stopwatch.Frequency;
                sb.AppendLine($"{p.Key}: calls={p.Value.Calls}, total={total:0.000} ms, mean={total / p.Value.Calls:0.000} ms, max={max:0.000} ms, allocated={p.Value.Allocated} bytes");
            }
#if RW_1_6
            sb.AppendLine("Allocation measurement unavailable on 1.6; bytes above are zero.");
#endif
            return sb.ToString();
        }
        private static long Allocated()
        {
#if RW_1_7 || NET10_0_OR_GREATER
            return GC.GetAllocatedBytesForCurrentThread();
#else
            return 0;
#endif
        }
        public readonly struct Scope : IDisposable
        {
            private readonly PerformanceProbe owner;
            private readonly string name;
            private readonly long start, allocated;
            internal Scope(PerformanceProbe owner, string name)
            { this.owner = owner; this.name = name; start = Stopwatch.GetTimestamp(); allocated = Allocated(); }
            public void Dispose()
            {
                if (owner == null || !owner.Enabled) return;
                long elapsed = Stopwatch.GetTimestamp() - start, bytes = Allocated() - allocated;
                if (!owner.metrics.TryGetValue(name, out var m)) owner.metrics[name] = m = new Metric();
                m.Calls++; m.Total += elapsed; m.Max = Math.Max(m.Max, elapsed); m.Allocated += bytes;
            }
        }
    }

}
