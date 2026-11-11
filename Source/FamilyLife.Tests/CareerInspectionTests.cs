using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class CareerInspectionTests
    {
        private static CareerDefinition Career() => new CareerDefinition
        {
            Id = "test.career",
            Level2 = new CareerRule { GeneralXp = 100 }.Skill("Medicine", 6),
            Specialties = { ["surgery"] = new CareerRule { GeneralXp = 150, SpecialtyXp = 60 }.Skill("Medicine", 10) }
        };

        [Fact]
        public void Inspecting_locked_and_unknown_people_never_awards_or_writes_a_qualification()
        {
            var ledger = new CareerLedger(); var def = Career();
            ledger.Award(1, def.Id, "surgery", "operation", "real-work", 200, 1);
            string saved = ledger.Encode();
            foreach (int id in new[] { 1, 99 })
                for (int frame = 0; frame < 20; frame++)
                {
                    var rows = ledger.Progress(id, def, _ => 20);
                    Assert.All(rows, row => Assert.False(row.Earned));
                    Assert.False(rows.Single(row => row.Key == "surgery").Available);
                }
            Assert.Equal(saved, ledger.Encode());
            Assert.Equal(AssignResult.NotQualified, AssignmentBook.Check(new ProfessionRole { Career = def.Id, Level = 2 }, ledger, 1));
        }

        [Fact]
        public void A_legacy_qualification_remains_assignable_when_inspected_with_unavailable_skills()
        {
            var ledger = new CareerLedger(); var def = Career(); ledger.Grandfather(1, def.Id);
            string saved = ledger.Encode();
            var row = ledger.Progress(1, def, _ => 0).Single(p => p.Key == null);
            Assert.True(row.Earned);
            Assert.Equal(0, row.GeneralXp);
            Assert.Equal(AssignResult.Assigned, AssignmentBook.Check(new ProfessionRole { Career = def.Id, Level = 2 }, ledger, 1));
            Assert.Equal(saved, ledger.Encode());
        }
    }
}
