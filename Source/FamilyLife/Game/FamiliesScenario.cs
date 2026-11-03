using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// The starting colonists of the Three Families scenario: in groups of four, a father, a mother and two children, each family under
    /// its own surname. It is the game's own "starting pawns" step with the ages, genders and surnames fixed, so the setup page, its
    /// reroll buttons and its pawn list all work as usual. The marriages and parent links are added when the game starts, from the
    /// surnames, so they survive anything the player does on the setup page.
    /// </summary>
    public class ScenPart_FamiliesStart : ScenPart_ConfigPage_ConfigureStartingPawns
    {
        private const int PerFamily = 4;

        protected override void GenerateStartingPawns()
        {
            if (!ModsConfig.BiotechActive)         // children are a Biotech feature; without it this is an ordinary starting party
            {
                base.GenerateStartingPawns();
                return;
            }
            int attempt = 0;
            do
            {
                StartingPawnUtility.ClearAllStartingPawns();
                var surnames = StartingFamilies.PickSurnames((TotalPawnCount + PerFamily - 1) / PerFamily, new System.Random(Rand.Int));
                for (int index = 0; index < TotalPawnCount; index++)
                {
                    int role = index % PerFamily;                 // 0 father, 1 mother, 2 and 3 children
                    bool adult = role < 2;
                    float age = adult ? Rand.Range(30f, 42f) : Rand.Range(4f, 12f);
                    var request = StartingPawnUtility.GetGenerationRequest(index);
                    request.FixedBiologicalAge = age;
                    request.ExcludeBiologicalAgeRange = null;          // the setup page's own exclusion clashes with a fixed age
                    request.CanGeneratePawnRelations = false;          // no random parents or siblings: the families are the ones the scenario makes
                    request.FixedChronologicalAge = age;
                    request.AllowedDevelopmentalStages = adult ? DevelopmentalStage.Adult : DevelopmentalStage.Child;
                    request.FixedGender = role == 0 ? Gender.Male : (role == 1 ? Gender.Female : (Gender?)null);
                    request.FixedLastName = surnames[index / PerFamily];
                    StartingPawnUtility.SetGenerationRequest(index, request);
                    StartingPawnUtility.AddNewPawn(index);
                }
                attempt++;
            }
            while (attempt <= 20 && !StartingPawnUtility.WorkTypeRequirementsSatisfied());
        }

        public override void PostGameStart()
        {
            base.PostGameStart();
            try { LinkFamilies(); }
            catch (Exception e) { Log.Warning("[Wil's Family Life] Could not link the starting families: " + e.Message); }
        }

        private static void LinkFamilies()
        {
            var pawns = new List<Pawn>();
            if (Find.GameInitData?.startingAndOptionalPawns != null) pawns.AddRange(Find.GameInitData.startingAndOptionalPawns);
            pawns.AddRange(PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists);
            pawns = pawns.Where(p => p != null && !p.Dead && p.Faction == Faction.OfPlayer && p.RaceProps != null && p.RaceProps.Humanlike)
                .GroupBy(p => p.thingIDNumber).Select(g => g.First()).ToList();

            var plan = StartingFamilies.Plan(pawns.Select(p => new StartingPerson
            {
                Id = p.thingIDNumber,
                LastName = (p.Name as NameTriple)?.Last ?? "",
                IsChild = p.DevelopmentalStage.Juvenile(),
                IsMale = p.gender == Gender.Male
            }));
            var byId = pawns.ToDictionary(p => p.thingIDNumber);

            // The game can still hand a starting pawn relatives of its own choosing; those would cross the families, so they are removed.
            foreach (var pawn in pawns) ClearFamilyRelations(pawn);

            foreach (var pair in plan.Spouses)
                Relate(byId[pair.Key], PawnRelationDefOf.Spouse, byId[pair.Value]);
            foreach (var link in plan.ParentOf)
                Relate(byId[link.Value], PawnRelationDefOf.Parent, byId[link.Key]);
            Log.Message("[Wil's Family Life] Three Families: " + plan.Spouses.Count + " marriages and " + plan.ParentOf.Count + " parent links were set up.");
        }

        private static void ClearFamilyRelations(Pawn pawn)
        {
            if (pawn.relations == null) return;
            foreach (var rel in pawn.relations.DirectRelations.ToList())
            {
                var def = rel.def;
                if (def == PawnRelationDefOf.Parent || def == PawnRelationDefOf.Spouse || def == PawnRelationDefOf.Lover || def == PawnRelationDefOf.Fiance
                    || def == PawnRelationDefOf.ExSpouse || def == PawnRelationDefOf.ExLover)
                    pawn.relations.TryRemoveDirectRelation(def, rel.otherPawn);
            }
        }

        private static void Relate(Pawn pawn, PawnRelationDef relation, Pawn other)
        {
            if (pawn.relations == null || other.relations == null || pawn.relations.DirectRelationExists(relation, other)) return;
            pawn.relations.AddDirectRelation(relation, other);
        }
    }
}
