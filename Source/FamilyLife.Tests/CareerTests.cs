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
            l.Award(1, d.Id, null, "a", 40, 1); l.Award(1, d.Id, null, "b", 40, 2); l.Award(1, d.Id, null, "c", 30, 3);
            Assert.Empty(l.Evaluate(1, d, Skills(5, 0)));                       // experience met, skill short
            Assert.Equal(new[] { d.Id }, l.Evaluate(1, d, Skills(6, 0)));
            Assert.Equal(2, l.Level(1, d.Id));
            var m = new CareerLedger(); m.Award(2, d.Id, null, "a", 40, 1);
            Assert.Empty(m.Evaluate(2, d, Skills(20, 0)));                      // skill met, experience short
        }

        [Fact] public void Level_three_needs_level_two_and_specialty_experience_which_may_come_first()
        {
            var l = new CareerLedger(); var d = Teaching();
            for (int i = 0; i < 4; i++) l.Award(1, d.Id, "Shooting", "s" + i, 40, i);
            Assert.Equal(160, l.GeneralXp(1, d.Id)); Assert.Equal(160, l.SpecialtyXp(1, d.Id, "Shooting"));
            Assert.Equal(new[] { d.Id, d.Id + ":Shooting" }, l.Evaluate(1, d, Skills(6, 12)));
            Assert.Equal(3, l.Level(1, d.Id));
            var n = new CareerLedger(); n.Award(2, d.Id, "Shooting", "x", 40, 1);   // specialty experience banked before qualifying
            Assert.Equal(40, n.SpecialtyXp(2, d.Id, "Shooting")); Assert.False(n.IsQualified(2, d.Id));
        }

        [Fact] public void A_specialty_is_never_awarded_without_level_two()
        {
            var l = new CareerLedger(); var d = Teaching();
            for (int i = 0; i < 4; i++) l.Award(1, d.Id, "Shooting", "s" + i, 40, i);
            Assert.Empty(l.Evaluate(1, d, Skills(2, 20)));                      // social too low for level 2
            Assert.False(l.IsSpecialist(1, d.Id, "Shooting"));
        }

        [Fact] public void Qualification_survives_skill_decay_and_a_save()
        {
            var l = new CareerLedger(); var d = Teaching();
            for (int i = 0; i < 3; i++) l.Award(1, d.Id, null, "k" + i, 40, i);
            l.Evaluate(1, d, Skills(6, 0));
            Assert.Empty(l.Evaluate(1, d, Skills(0, 0)));
            Assert.True(l.IsQualified(1, d.Id));
            var back = CareerLedger.Decode(l.Encode());
            Assert.True(back.IsQualified(1, d.Id)); Assert.Equal(120, back.GeneralXp(1, d.Id));
            Assert.Equal(l.Encode(), back.Encode());
        }

        [Fact] public void The_same_activity_is_credited_once_even_after_a_save()
        {
            var l = new CareerLedger(); Assert.Equal(10, l.Award(1, "c", null, "lesson-5", 10, 1));
            Assert.Equal(0, l.Award(1, "c", null, "lesson-5", 10, 1));
            var back = CareerLedger.Decode(l.Encode()); Assert.Equal(0, back.Award(1, "c", null, "lesson-5", 10, 1));
            Assert.Equal(10, back.GeneralXp(1, "c"));
        }

        [Fact] public void Trivial_repetition_is_capped_per_day_and_resumes_next_day()
        {
            var l = new CareerLedger(); int total = 0;
            for (int i = 0; i < 50; i++) total += l.Award(1, "c", null, "t" + i, 10, 7);
            Assert.Equal(CareerLedger.DailyCap, total);
            Assert.Equal(10, l.Award(1, "c", null, "next", 10, 8));
        }

        [Fact] public void Several_people_and_several_specialties_are_independent()
        {
            var l = new CareerLedger(); var d = Teaching(); d.Specialties["Melee"] = new CareerRule { GeneralXp = 100, SpecialtyXp = 30 }.Skill("Melee", 10);
            foreach (int p in new[] { 1, 2 })
            {
                for (int i = 0; i < 4; i++) { l.Award(p, d.Id, "Shooting", "s" + i, 40, i); l.Award(p, d.Id, "Melee", "m" + i, 40, 10 + i); }
                l.Evaluate(p, d, s => 20);
            }
            Assert.Equal(new[] { "Melee", "Shooting" }, l.Specialties(1, d.Id).ToArray());
            Assert.Equal(new[] { "Melee", "Shooting" }, l.Specialties(2, d.Id).ToArray());
        }

        [Fact] public void Missing_requirements_are_listed_for_the_ui()
        {
            var l = new CareerLedger(); var d = Teaching(); l.Award(1, d.Id, null, "a", 30, 1);
            var m = l.Missing(1, d.Id, null, d.Level2, Skills(4, 0));
            Assert.Contains("Social 6", m); Assert.Contains("experience 30/100", m);
        }
    }
}
