using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    /// <summary>
    /// The save code cannot run outside the game, but the most common save bug can be caught here: a field added to the
    /// model and never written to the save. Every serialisable field must appear in a Scribe call in Persist.cs.
    /// </summary>
    public class PersistenceCoverageTests
    {
        private static string PersistSource()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "Source", "FamilyLife", "Persistence", "Persist.cs");
                if (File.Exists(candidate)) return File.ReadAllText(candidate) + File.ReadAllText(Path.Combine(dir.FullName, "Source", "FamilyLife", "Persistence", "PersistGovernance.cs"));
                dir = dir.Parent;
            }
            throw new FileNotFoundException("Persist.cs not found above " + AppContext.BaseDirectory);
        }

        // Indexes and caches are rebuilt on load, so they are not saved.
        private static readonly string[] NotSaved = { "personById", "lineageById", "householdById", "childrenByParent", "indexed", "bondByPair", "timeByPerson", "deadThisSweep", "claimByThing", "agencyByPerson", "kinshipCache", "stateCache", "stateVersion" };

        [Theory]
        [InlineData(typeof(FamilyEvent))]
        [InlineData(typeof(PersonRecord))]
        [InlineData(typeof(Affiliation))]
        [InlineData(typeof(Lineage))]
        [InlineData(typeof(Household))]
        [InlineData(typeof(Bond))]
        [InlineData(typeof(TimeRecord))]
        [InlineData(typeof(TraditionRecord))]
        [InlineData(typeof(GatheringRequest))]
        [InlineData(typeof(CeremonyRecord))]
        [InlineData(typeof(PrestigeRecord))]
        [InlineData(typeof(Memory))]
        [InlineData(typeof(Rift))]
        [InlineData(typeof(BondReason))]
        [InlineData(typeof(FamilyTie))]
        [InlineData(typeof(Commitment))]
        [InlineData(typeof(EventRecord))]
        [InlineData(typeof(FamilyIncident))]
        [InlineData(typeof(RevengePlot))]
        [InlineData(typeof(HouseContact))]
        [InlineData(typeof(HousePetition))]
        [InlineData(typeof(HouseSituation))]
        [InlineData(typeof(HouseAmbitionRecord))]
        [InlineData(typeof(StewardRecord))]
        [InlineData(typeof(MarriageAlliance))]
        [InlineData(typeof(AttentionRecord))]
        [InlineData(typeof(AgencyRecord))]
        [InlineData(typeof(StockClaim))]
        [InlineData(typeof(PlanInvitee))]
        [InlineData(typeof(CeremonyPlan))]
        [InlineData(typeof(StewardDuty))]
        [InlineData(typeof(FamilyOffice))]
        [InlineData(typeof(SuccessionCase))]
        [InlineData(typeof(CrimeRecord))]
        [InlineData(typeof(HeistParticipant))]
        [InlineData(typeof(Heist))]
        [InlineData(typeof(CrimeKnowledge))]
        [InlineData(typeof(ProtectionArrangement))]
        [InlineData(typeof(ColonyInstitution))]
        [InlineData(typeof(InstitutionMovement))]
        [InlineData(typeof(ElectionEndorsement))]
        [InlineData(typeof(ElectionVote))]
        [InlineData(typeof(Election))]
        [InlineData(typeof(CoupParticipant))]
        [InlineData(typeof(CoupPlan))]
        [InlineData(typeof(FavorDebt))]
        [InlineData(typeof(CouncilMatter))]
        [InlineData(typeof(CommunityOffice))]
        [InlineData(typeof(CommunityAssembly))]
        [InlineData(typeof(AssemblyRace))]
        [InlineData(typeof(AssemblyEndorsement))]
        [InlineData(typeof(CampTrip))]
        [InlineData(typeof(KinVisit))]
        [InlineData(typeof(PersonalItem))]
        [InlineData(typeof(Beneficiary))]
        [InlineData(typeof(EstateClaim))]
        [InlineData(typeof(EstateItem))]
        [InlineData(typeof(EstateAsk))]
        [InlineData(typeof(EstateCase))]
        [InlineData(typeof(SupplyRequest))]
        [InlineData(typeof(FamilyRegistry))]
        public void Every_field_is_written_to_the_save(Type type)
        {
            string source = PersistSource();
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => !f.IsInitOnly || f.FieldType.IsGenericType)
                .Select(f => f.Name)
                .Where(n => !NotSaved.Contains(n))
                .ToList();

            foreach (var name in fields)
            {
                bool saved = Regex.IsMatch(source, @"Scribe_\w+\.Look\(\s*ref\s+" + Regex.Escape(name) + @"\b");
                Assert.True(saved, type.Name + "." + name + " is not written by Persist.cs");
            }
        }

        [Fact]
        public void Save_labels_are_unique_within_each_class()
        {
            string source = PersistSource();
            foreach (Match cls in Regex.Matches(source, @"partial class (\w+) : IExposable(.*?)(?=\n    public sealed partial class|\z)", RegexOptions.Singleline))
            {
                var labels = Regex.Matches(cls.Groups[2].Value, @"Look\(\s*ref\s+\w+,\s*""(\w+)""").Select(m => m.Groups[1].Value).ToList();
                Assert.Equal(labels.Count, labels.Distinct().Count());
            }
        }
    }
}
