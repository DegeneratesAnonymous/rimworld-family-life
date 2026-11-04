using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class WorkSchedulingTests
    {
        [Fact]
        public void Disabled_performance_scopes_allocate_nothing()
        {
            var probe = new PerformanceProbe();
            probe.Measure("disabled").Dispose();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) probe.Measure("disabled").Dispose();
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        [Fact]
        public void Performance_capture_reports_calls_and_restart_discards_the_previous_capture()
        {
            var probe = new PerformanceProbe();
            probe.Start(100);
            using (probe.Measure("first")) { }
            using (probe.Measure("first")) { }
            var report = probe.Stop(200);
            Assert.Contains("Ticks: 100", report);
            Assert.Contains("first: calls=2", report);
            Assert.False(probe.Enabled);
            probe.Start(1000);
            using (probe.Measure("second")) { }
            var next = probe.Stop(1100);
            Assert.DoesNotContain("first:", next);
            Assert.Contains("second: calls=1", next);
        }

        [Fact]
        public void Discovery_refreshes_ten_times_less_often_than_active_visit_updates()
        {
            var discovery = new TickGate(300);
            int refreshes = 0, updates = 0;
            for (int tick = 0; tick < 3000; tick += 30)
            { updates++; if (discovery.Take(tick)) refreshes++; }
            Assert.Equal(100, updates);
            Assert.Equal(10, refreshes);
        }

        [Fact]
        public void Invalidated_work_is_refreshed_promptly_and_clock_rewind_is_safe()
        {
            var gate = new TickGate(300);
            Assert.True(gate.Take(1000));
            Assert.False(gate.Take(1030));
            gate.Invalidate();
            Assert.True(gate.Take(1030));
            Assert.True(gate.Take(20));
            Assert.False(gate.Take(20));
            Assert.True(gate.Take(320));
            Assert.True(gate.Take(5000));
            Assert.False(gate.Take(5000)); // no catch-up loop after a long gap
        }

        [Fact]
        public void A_burst_of_work_changes_is_coalesced_without_postponing_refresh_forever()
        {
            var gate = new TickGate(300, 60);
            Assert.True(gate.Take(0));
            for (int tick = 1; tick < 60; tick++) { gate.Invalidate(); Assert.False(gate.Take(tick)); }
            Assert.True(gate.Take(60));
            Assert.False(gate.Take(90));
            Assert.True(gate.Take(360));
        }

        [Fact]
        public void Map_schedules_are_independent()
        {
            var a = new TickGate(300); var b = new TickGate(300);
            Assert.True(a.Take(0)); Assert.True(b.Take(0));
            a.Invalidate();
            Assert.True(a.Take(30)); Assert.False(b.Take(30));
        }

        [Fact]
        public void Maintenance_phases_run_once_each_per_hour_without_a_same_tick_burst()
        {
            var gate = new TickGate(625);
            var counts = new int[4]; int phase = 0;
            for (int tick = 1; tick <= 2500; tick++)
                if (gate.Take(tick)) { counts[phase]++; phase = (phase + 1) % 4; }
            Assert.Equal(new[] { 1, 1, 1, 1 }, counts);
        }

        [Fact]
        public void Map_cursor_rotates_without_a_search_having_to_resume_it()
        {
            var cursor = new SearchCursor();
            Assert.Equal(new[] { 0, 1, 2, 0 }, Enumerable.Range(0, 4).Select(_ => cursor.Take(3)).ToArray());
            cursor.Reset(); Assert.Equal(0, cursor.Take(3));
        }

        [Fact]
        public void Rotating_search_reaches_every_item_with_a_small_budget_and_survives_removals()
        {
            var cursor = new SearchCursor(); var seen = new HashSet<int>();
            for (int pass = 0; pass < 10; pass++)
            {
                int start = cursor.Take(100);
                for (int i = 0; i < 10; i++)
                { int index = (start + i) % 100; seen.Add(index); cursor.ResumeAfter(index, 100); }
            }
            Assert.Equal(100, seen.Count);
            Assert.InRange(cursor.Take(3), 0, 2);
            Assert.Equal(0, cursor.Take(0));
            Assert.Equal(0, cursor.Take(1));
        }

        [Fact]
        public void Spatial_queries_match_brute_force_across_boundaries_and_negative_coordinates()
        {
            var rng = new Random(123);
            var points = Enumerable.Range(0, 500).Select(id => (id, x: rng.Next(-100, 101), z: rng.Next(-100, 101))).ToArray();
            var index = new NearbyIndex<int>(6);
            foreach (var p in points) index.Add(p.id, p.x, p.z);
            for (int i = 0; i < 100; i++)
            {
                int x = rng.Next(-100, 101), z = rng.Next(-100, 101), radius = rng.Next(0, 20);
                var expected = points.Where(p => (p.x - x) * (p.x - x) + (p.z - z) * (p.z - z) <= radius * radius).Select(p => p.id).OrderBy(id => id);
                Assert.Equal(expected, index.Within(x, z, radius, i).OrderBy(id => id));
            }
        }

        [Fact]
        public void Bounding_cell_candidates_can_be_budgeted_even_when_all_fail_the_distance_check()
        {
            var index = new NearbyIndex<int>(6);
            for (int i = 0; i < 500; i++) index.Add(i, 6, 6);
            Assert.Empty(index.Within(0, 0, 6));
            Assert.Equal(256, index.Candidates(0, 0, 6).Take(256).Count());
        }

        [Fact]
        public void Dense_neighborhoods_rotate_candidates_instead_of_starving_the_end_of_a_bucket()
        {
            var index = new NearbyIndex<int>(6);
            for (int i = 0; i < 100; i++) index.Add(i, 1, 1);
            var seen = new HashSet<int>();
            for (int pass = 0; pass < 100; pass++)
                foreach (var id in index.Within(1, 1, 6, pass).Take(8)) seen.Add(id);
            Assert.Equal(100, seen.Count);
            Assert.Empty(index.Within(100, 100, 6));
        }

        [Fact]
        public void Third_context_copy_does_not_overwrite_the_plain_pairs_circumstances()
        {
            var pair = new PairContext { Initiator = new Actor { Id = 1 }, Recipient = new Actor { Id = 2 }, Now = 100, Setting = ActivityKind.Meal };
            var third = pair.Copy(); third.Third = new Actor { Id = 3 }; third.ThirdAttachToRecipient = 40;
            Assert.Null(pair.Third);
            Assert.Equal(-1, pair.ThirdAttachToRecipient);
            Assert.Same(pair.Initiator, third.Initiator);
            Assert.Same(pair.Recipient, third.Recipient);
            Assert.Equal(pair.Now, third.Now);
            Assert.Equal(pair.Setting, third.Setting);
        }
    }
}
