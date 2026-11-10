using System;
using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class CareerTests
    {
        private static CareerDefinition Teaching() => new CareerDefinition
        {
            Id = "wils.education.teaching",
            Level2 = new CareerRule { GeneralXp = 100 }.Skill("Social", 6),
            Specialties = { ["Shooting"] = new CareerRule { GeneralXp = 150, SpecialtyXp = 60 }.Skill("Shooting", 12) }
        };
        private static Func<string, int> Skills(int social, int shooting) => s => s == "Social" ? social : s == "Shooting" ? shooting : 0;

        [Fact] public void Level_one_needs_nothing() { var l = new CareerLedger(); Assert.Equal(1, l.Level(1, "wils.education.teaching")); Assert.False(l.IsQualified(1, "wils.education.teaching")); }

        [Fact] public void Level_two_needs_both_skill_and_experience()
        {
            var l = new CareerLedger(); var d = Teaching();
            l.Award(1, d.Id, null, "k", "a", 40, 1); l.Award(1, d.Id, null, "k", "b", 40, 2); l.Award(1, d.Id, null, "k", "c", 30, 3);
            Assert.Empty(l.Evaluate(1, d, Skills(5, 0)));                       // experience met, skill short
            Assert.Equal(new[] { d.Id }, l.Evaluate(1, d, Skills(6, 0)));
            Assert.Equal(2, l.Level(1, d.Id));
            var m = new CareerLedger(); m.Award(2, d.Id, null, "k", "a", 40, 1);
            Assert.Empty(m.Evaluate(2, d, Skills(20, 0)));                      // skill met, experience short
        }

        [Fact] public void Level_three_needs_level_two_and_specialty_experience_which_may_come_first()
        {
            var l = new CareerLedger(); var d = Teaching();
            for (int i = 0; i < 4; i++) l.Award(1, d.Id, "Shooting", "k", "s" + i, 40, i);
            Assert.Equal(160, l.GeneralXp(1, d.Id)); Assert.Equal(160, l.SpecialtyXp(1, d.Id, "Shooting"));
            Assert.Equal(new[] { d.Id, d.Id + ":Shooting" }, l.Evaluate(1, d, Skills(6, 12)));
            Assert.Equal(3, l.Level(1, d.Id));
            var n = new CareerLedger(); n.Award(2, d.Id, "Shooting", "k", "x", 40, 1);   // specialty experience banked before qualifying
            Assert.Equal(40, n.SpecialtyXp(2, d.Id, "Shooting")); Assert.False(n.IsQualified(2, d.Id));
        }

        [Fact] public void A_specialty_is_never_awarded_without_level_two()
        {
            var l = new CareerLedger(); var d = Teaching();
            for (int i = 0; i < 4; i++) l.Award(1, d.Id, "Shooting", "k", "s" + i, 40, i);
            Assert.Empty(l.Evaluate(1, d, Skills(2, 20)));                      // social too low for level 2
            Assert.False(l.IsSpecialist(1, d.Id, "Shooting"));
        }

        [Fact] public void Qualification_survives_skill_decay_and_a_save()
        {
            var l = new CareerLedger(); var d = Teaching();
            for (int i = 0; i < 3; i++) l.Award(1, d.Id, null, "k", "k" + i, 40, i);
            l.Evaluate(1, d, Skills(6, 0));
            Assert.Empty(l.Evaluate(1, d, Skills(0, 0)));
            Assert.True(l.IsQualified(1, d.Id));
            var back = CareerLedger.Decode(l.Encode());
            Assert.True(back.IsQualified(1, d.Id)); Assert.Equal(120, back.GeneralXp(1, d.Id));
            Assert.Equal(l.Encode(), back.Encode());
        }

        [Fact] public void The_same_activity_is_credited_once_even_after_a_save()
        {
            var l = new CareerLedger(); Assert.Equal(10, l.Award(1, "c", null, "k", "lesson-5", 10, 1));
            Assert.Equal(0, l.Award(1, "c", null, "k", "lesson-5", 10, 1));
            var back = CareerLedger.Decode(l.Encode()); Assert.Equal(0, back.Award(1, "c", null, "k", "lesson-5", 10, 1));
            Assert.Equal(10, back.GeneralXp(1, "c"));
        }

        [Fact] public void A_normal_working_day_earns_full_experience_and_only_heavy_repetition_is_trimmed()
        {
            var l = new CareerLedger(); int total = 0;
            for (int i = 0; i < CareerLedger.FullPerDay; i++) total += l.Award(1, "c", null, "k", "t" + i, 10, 7);
            Assert.Equal(10 * CareerLedger.FullPerDay, total);                                           // a hospital or school day: all full value
            int ninth = l.Award(1, "c", null, "k", "t9", 10, 7), twentieth = 0;
            for (int i = 10; i < 21; i++) twentieth = l.Award(1, "c", null, "k", "t" + i, 10, 7);
            Assert.True(ninth <= 10 && ninth >= 9); Assert.True(twentieth < ninth);
            Assert.Equal(10, l.Award(1, "c", null, "other", "x", 10, 7));                                // another kind is not affected
            Assert.Equal(10, l.Award(1, "c", null, "k", "next", 10, 8));                                 // a new day starts fresh
        }

        [Fact] public void Extreme_repetition_never_drops_below_the_floor_and_the_backstop_bounds_the_day()
        {
            var l = new CareerLedger(); int last = 0, total = 0;
            for (int i = 0; i < 300; i++) { last = l.Award(1, "c", null, "k", "e" + i, 20, 3); total += last; }
            Assert.True(total <= CareerLedger.SafetyCapPerDay);
            var m = new CareerLedger(); int old = CareerLedger.SafetyCapPerDay;
            try
            {
                CareerLedger.SafetyCapPerDay = 0; int worst = 20;
                for (int i = 0; i < 300; i++) worst = System.Math.Min(worst, m.Award(1, "c", null, "k", "e" + i, 20, 3));
                Assert.Equal(10, worst);                                                                  // half of normal, however many
            }
            finally { CareerLedger.SafetyCapPerDay = old; }
        }

        [Fact] public void A_hospital_or_school_colony_still_progresses_fast_enough()
        {
            var l = new CareerLedger(); int days = 0; var d = Teaching();
            for (; days < 30 && !l.IsQualified(1, d.Id); days++)
            {
                for (int v = 0; v < 12; v++) l.Award(1, d.Id, null, "teach.class", "c" + days + "_" + v, 10, days);        // twelve pupil-classes a day
                l.Evaluate(1, d, Skills(6, 0));
            }
            Assert.True(l.IsQualified(1, d.Id)); Assert.True(days <= 2);
        }

        [Fact] public void Share_settings_are_configurable()
        {
            int f = CareerLedger.FullPerDay; float fa = CareerLedger.RepeatFalloff, ms = CareerLedger.MinShare;
            try { CareerLedger.FullPerDay = 2; CareerLedger.RepeatFalloff = .25f; CareerLedger.MinShare = .7f; Assert.Equal(1f, CareerLedger.Share(1)); Assert.Equal(.75f, CareerLedger.Share(2), 3); Assert.Equal(.7f, CareerLedger.Share(50), 3); }
            finally { CareerLedger.FullPerDay = f; CareerLedger.RepeatFalloff = fa; CareerLedger.MinShare = ms; }
        }

        [Fact] public void Old_activity_records_are_forgotten_and_the_record_is_bounded()
        {
            int old = CareerLedger.SafetyCapPerDay; float f = CareerLedger.RepeatFalloff;
            try
            {
                CareerLedger.SafetyCapPerDay = 0; CareerLedger.RepeatFalloff = 0f;
                var l = new CareerLedger(); l.Award(1, "c", null, "k", "old", 5, 1);
                Assert.Equal(0, l.Award(1, "c", null, "k", "old", 5, 2));                                  // remembered
                l.Award(1, "c", null, "k", "new", 5, 1 + CareerLedger.RememberDays + 5);                   // pruned the old one
                Assert.True(l.Award(1, "c", null, "k", "old", 5, 1 + CareerLedger.RememberDays + 5) > 0);
                var big = new CareerLedger(); for (int i = 0; i < CareerLedger.RememberedActivities + 300; i++) big.Award(1, "c", null, "k", "e" + i, 1, 100 + i / 50);
                Assert.True(CareerLedger.Decode(big.Encode()).Encode().Split((char)10).Length < 20);
            }
            finally { CareerLedger.SafetyCapPerDay = old; CareerLedger.RepeatFalloff = f; }
        }

        [Fact] public void The_same_event_credits_each_person_once_but_every_participant()
        {
            var l = new CareerLedger(); Assert.True(l.Award(1, "c", null, "k", "class-9", 10, 1) > 0); Assert.True(l.Award(2, "c", null, "k", "class-9", 10, 1) > 0);
            Assert.Equal(0, l.Award(1, "c", null, "k", "class-9", 10, 1));
            Assert.True(l.Award(1, "c", "Melee", "k", "class-9", 10, 1) > 0);          // a specialty credit for the same event is its own record
        }

        [Fact] public void Definitions_that_change_between_versions_never_take_a_qualification_away_and_a_lowered_bar_is_met_on_the_next_check()
        {
            var l = new CareerLedger(); var d = Teaching();
            for (int i = 0; i < 3; i++) l.Award(1, d.Id, null, "k", "x" + i, 40, i * 3);
            l.Evaluate(1, d, Skills(6, 0));
            d.Level2.GeneralXp = 9999; d.Level2.Skills["Social"] = 18;                 // a later version makes it far harder
            Assert.Empty(l.Evaluate(1, d, Skills(0, 0))); Assert.True(l.IsQualified(1, d.Id));
            var m = new CareerLedger(); m.Award(2, d.Id, null, "k", "y", 40, 1);       // a later version makes it easier
            d.Level2.GeneralXp = 30; d.Level2.Skills["Social"] = 2; Assert.Equal(new[] { d.Id }, m.Evaluate(2, d, Skills(3, 0)));
        }

        [Fact] public void Careers_of_a_module_that_is_not_loaded_are_kept_untouched_and_reported()
        {
            var l = new CareerLedger(); l.Award(1, "wils.other.craft", "Fine", "k", "e", 20, 1); l.Grandfather(1, "wils.other.craft");
            string before = l.Encode();
            l.Evaluate(1, Teaching(), Skills(20, 20));
            Assert.Equal(before, l.Encode());
            Assert.Equal(new[] { "wils.other.craft" }, l.UnknownCareers(new[] { Teaching() }).ToArray());
            var back = CareerLedger.Decode(before); Assert.True(back.IsQualified(1, "wils.other.craft")); Assert.Equal(20, back.SpecialtyXp(1, "wils.other.craft", "Fine"));
        }

        [Fact] public void A_grandfathered_qualification_invents_no_experience_and_is_marked_legacy()
        {
            var l = new CareerLedger(); Assert.True(l.Grandfather(1, "c")); Assert.False(l.Grandfather(1, "c"));
            Assert.True(l.IsQualified(1, "c")); Assert.Equal(0, l.GeneralXp(1, "c")); Assert.True(l.IsLegacy(1, "c"));
            Assert.True(CareerLedger.Decode(l.Encode()).IsLegacy(1, "c"));
        }

        [Fact] public void Several_people_and_several_specialties_are_independent()
        {
            var l = new CareerLedger(); var d = Teaching(); d.Specialties["Melee"] = new CareerRule { GeneralXp = 100, SpecialtyXp = 30 }.Skill("Melee", 10);
            foreach (int p in new[] { 1, 2 })
            {
                for (int i = 0; i < 4; i++) { l.Award(p, d.Id, "Shooting", "k", "s" + i, 40, i); l.Award(p, d.Id, "Melee", "k", "m" + i, 40, 10 + i); }
                l.Evaluate(p, d, s => 20);
            }
            Assert.Equal(new[] { "Melee", "Shooting" }, l.Specialties(1, d.Id).ToArray());
            Assert.Equal(new[] { "Melee", "Shooting" }, l.Specialties(2, d.Id).ToArray());
        }

        [Fact] public void Missing_requirements_are_listed_for_the_ui()
        {
            var l = new CareerLedger(); var d = Teaching(); l.Award(1, d.Id, null, "k", "a", 30, 1);
            var m = l.Missing(1, d.Id, null, d.Level2, Skills(4, 0));
            Assert.Contains("Social 6", m); Assert.Contains("experience 30/100", m);
        }

        [Fact] public void Career_history_is_kept_per_person_bounded_and_saved()
        {
            var l = new CareerLedger(); l.NoteQualified(1, "c", 100); l.NoteQualified(2, "c", 200); l.NoteQualified(1, "c:Melee", 300);
            Assert.Equal(new[] { 100, 300 }, l.HistoryOf(1).Select(e => e.Tick).ToArray());
            Assert.Equal(2, CareerLedger.Decode(l.Encode()).HistoryOf(1).Count);
            for (int i = 0; i < CareerLedger.MaxHistory + 20; i++) l.NoteQualified(3, "c", 1000 + i);
            Assert.True(CareerLedger.Decode(l.Encode()).HistoryOf(3).Count <= CareerLedger.MaxHistory);
        }

        [Fact] public void An_achievement_is_recognised_by_one_assembly_only_and_recognition_changes_nothing()
        {
            var l = new CareerLedger(); l.NoteQualified(1, "c", 100); l.NoteQualified(2, "c", 500);
            var first = l.Recognise(300); Assert.Single(first); Assert.Equal(1, first[0].Person);
            Assert.Empty(l.Recognise(300));
            var next = CareerLedger.Decode(l.Encode()).Recognise(900); Assert.Single(next); Assert.Equal(2, next[0].Person);
            l.Grandfather(5, "c"); Assert.True(l.IsQualified(5, "c"));
        }
    }
}
