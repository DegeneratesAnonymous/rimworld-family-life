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

        [Fact] public void Repeating_the_same_kind_of_activity_on_one_day_earns_less_each_time_and_a_new_day_resets_it()
        {
            var l = new CareerLedger(); int a = l.Award(1, "c", null, "k", "t0", 100, 7), b = l.Award(1, "c", null, "k", "t1", 100, 7), c = l.Award(1, "c", null, "k", "t2", 100, 7);
            Assert.Equal(100, a); Assert.True(b < a && c < b);
            Assert.Equal(100, l.Award(1, "c", null, "other", "t3", 100, 7));          // a different kind is not diminished
            Assert.Equal(100, l.Award(1, "c", null, "k", "t4", 100, 8));
        }

        [Fact] public void The_safety_cap_is_configurable_and_can_be_turned_off()
        {
            int old = CareerLedger.SafetyCapPerDay; float f = CareerLedger.RepeatFactor;
            try
            {
                CareerLedger.RepeatFactor = 1f; CareerLedger.SafetyCapPerDay = 50;
                var l = new CareerLedger(); int total = 0; for (int i = 0; i < 20; i++) total += l.Award(1, "c", null, "k", "t" + i, 10, 1);
                Assert.Equal(50, total);
                CareerLedger.SafetyCapPerDay = 0; var m = new CareerLedger(); total = 0; for (int i = 0; i < 20; i++) total += m.Award(1, "c", null, "k", "t" + i, 10, 1);
                Assert.Equal(200, total);
            }
            finally { CareerLedger.SafetyCapPerDay = old; CareerLedger.RepeatFactor = f; }
        }

        [Fact] public void Old_activity_records_are_forgotten_and_the_record_is_bounded()
        {
            int old = CareerLedger.SafetyCapPerDay; float f = CareerLedger.RepeatFactor;
            try
            {
                CareerLedger.SafetyCapPerDay = 0; CareerLedger.RepeatFactor = 1f;
                var l = new CareerLedger(); l.Award(1, "c", null, "k", "old", 5, 1);
                Assert.Equal(0, l.Award(1, "c", null, "k", "old", 5, 2));                                  // remembered
                l.Award(1, "c", null, "k", "new", 5, 1 + CareerLedger.RememberDays + 5);                   // pruned the old one
                Assert.True(l.Award(1, "c", null, "k", "old", 5, 1 + CareerLedger.RememberDays + 5) > 0);
                var big = new CareerLedger(); for (int i = 0; i < CareerLedger.RememberedActivities + 300; i++) big.Award(1, "c", null, "k", "e" + i, 1, 100 + i / 50);
                Assert.True(CareerLedger.Decode(big.Encode()).Encode().Split((char)10).Length < 20);
            }
            finally { CareerLedger.SafetyCapPerDay = old; CareerLedger.RepeatFactor = f; }
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
    }
}
