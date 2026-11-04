using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace FamilyLife
{
    [DefOf]
    public static class WFL_FlavorDefOf
    {
        public static JobDef WFL_FamilyTime;
        public static JobDef WFL_Date;
        public static JobDef WFL_Teach;
        public static JobDef WFL_Reconcile;
        public static JobDef WFL_Bedside;
        public static JobDef WFL_Babysit;
        public static JobDef WFL_Cradle;
        public static JobDef WFL_Lullaby;
        public static JobDef WFL_PlayBaby;

        static WFL_FlavorDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(WFL_FlavorDefOf));
    }

    internal sealed class FamilyChoice
    {
        public Pawn Other;
        public JobDef Def;
        public Pawn Sitter;       // for a date: who looks after the children meanwhile
        public Pawn SitterCharge; // and which child
    }

    /// <summary>
    /// Decides which kind of time together a pawn has with a relative: making peace if there is a rift to mend, a date for
    /// partners, teaching when one is clearly ahead of the other, otherwise just company. All of them are the same family-time
    /// job with a different purpose, and all of them come out of the same daily budget.
    /// </summary>
    internal static class Flavors
    {
        private static FamilySettings S => FamilyLifeMod.Settings;
        private static bool On(System.Func<FamilySettings, bool> pick) => S == null || pick(S);

        public static FamilyChoice Choose(Pawn pawn)
        {
            var component = FamilyComponent.Current;
            if (component == null || pawn?.Map == null || !Activities.IsFree(pawn, checkCurrentJob: false) || !Activities.CanSpareAttention(pawn)) return null;
            var registry = component.Registry;
            var record = registry.GetPerson(pawn.thingIDNumber);
            if (record == null || !record.InColony) return null;
            int now = Activities.Now;
            if (!registry.HasFamilyTimeLeft(record.Id, now, Activities.DailyTargetTicks) || !Activities.AttemptAllowed(record.Id, now, 600)) return null;
            Activities.NoteAttempt(record.Id, now);      // looking costs something even when nobody is found, so it is not repeated at once

            var free = new Dictionary<int, Pawn>();
            foreach (var bond in registry.BondsFrom(record.Id))
            {
                var other = component.TryGetPawn(bond.To);
                if (other != null && other.Map == pawn.Map && Activities.IsFree(other) && other.Position.DistanceTo(pawn.Position) <= Activities.FamilyTimeMaxDistance)
                    free[bond.To] = other;
            }
            if (free.Count == 0) return null;

            // Mending a rift comes first: the people involved want to be rid of it.
            if (On(s => s.conflicts))
            {
                foreach (var rift in registry.OpenRiftsOf(record.Id))
                {
                    int otherId = rift.A == record.Id ? rift.B : rift.A;
                    if (free.TryGetValue(otherId, out var other) && registry.MayTryReconcile(rift.Id, now) && pawn.CanReach(other, PathEndMode.Touch, Danger.None))
                        return Commit(record.Id, now, new FamilyChoice { Other = other, Def = WFL_FlavorDefOf.WFL_Reconcile });
                }
            }

            int lineageId = registry.LineageIdFor(record);
            foreach (var candidate in registry.RankCompanions(record.Id, free.Keys, now, Activities.Appetite(pawn, registry, record)).Take(3))
            {
                var other = free[candidate.Id];
                if (!pawn.CanReach(other, PathEndMode.Touch, Danger.None)) continue;
                var otherRecord = registry.GetPerson(other.thingIDNumber);
                var kind = registry.KindOf(record, otherRecord);

                if (kind == BondKind.Partner && On(s => s.dates) && Rand.Chance(registry.DateChance(lineageId)))
                {
                    var plan = PlanDate(pawn, other, registry);
                    if (plan != null) return Commit(record.Id, now, plan);
                }
                if (On(s => s.teaching) && MentorSkill(pawn, other) != null && Rand.Chance(registry.TeachChance(lineageId)))
                    return Commit(record.Id, now, new FamilyChoice { Other = other, Def = WFL_FlavorDefOf.WFL_Teach });
                return Commit(record.Id, now, new FamilyChoice { Other = other, Def = WFL_FlavorDefOf.WFL_FamilyTime });
            }
            return null;
        }

        private static FamilyChoice Commit(int pawnId, int now, FamilyChoice choice)
        {
            Activities.NoteAttempt(pawnId, now);
            return choice;
        }

        // ------------------------------------------------------------------ dates and babysitters

        private static FamilyChoice PlanDate(Pawn pawn, Pawn partner, FamilyRegistry registry)
        {
            var need = ChildNeedingCare(pawn, partner, registry);
            if (need == null) return new FamilyChoice { Other = partner, Def = WFL_FlavorDefOf.WFL_Date };
            var sitter = FindSitter(pawn, partner, need);
            // No one to mind the children: the couple simply spend ordinary time together instead.
            if (sitter == null) return null;
            return new FamilyChoice { Other = partner, Def = WFL_FlavorDefOf.WFL_Date, Sitter = sitter, SitterCharge = need };
        }

        /// <summary>A baby or young child of this home who would be left alone.</summary>
        private static Pawn ChildNeedingCare(Pawn a, Pawn b, FamilyRegistry registry)
        {
            var component = FamilyComponent.Current;
            var record = registry.GetPerson(a.thingIDNumber);
            var household = registry.GetHousehold(record.HouseholdId);
            if (household == null) return null;
            foreach (var id in household.ResidentIds)
            {
                var child = component.TryGetPawn(id);
                if (child != null && child.Spawned && (child.DevelopmentalStage.Baby() || child.DevelopmentalStage.Child()) && id != a.thingIDNumber && id != b.thingIDNumber)
                    return child;
            }
            return null;
        }

        private static Pawn FindSitter(Pawn a, Pawn b, Pawn child)
        {
            Pawn best = null;
            int bestSocial = -1;
            foreach (var c in a.Map.mapPawns.FreeColonistsSpawned)
            {
                if (c == a || c == b || c == child || c.DevelopmentalStage.Juvenile() || !Activities.IsFree(c) || !Activities.CanSpareAttention(c)) continue;
                if (c.relations == null || c.relations.OpinionOf(a) < 15 || c.relations.OpinionOf(b) < 15) continue;
                if (!c.CanReach(child, PathEndMode.Touch, Danger.None)) continue;
                int social = Activities.SocialOf(c);
                if (social > bestSocial) { best = c; bestSocial = social; }
            }
            return best;
        }

        public static void StartSitter(FamilyChoice choice)
        {
            if (choice.Sitter == null || choice.SitterCharge == null) return;
            var job = JobMaker.MakeJob(WFL_FlavorDefOf.WFL_Babysit, choice.SitterCharge);
            choice.Sitter.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        // ------------------------------------------------------------------ mentoring

        /// <summary>The skill this pawn could teach the other: they are clearly ahead, and clearly older.</summary>
        public static SkillRecord MentorSkill(Pawn mentor, Pawn learner)
        {
            if (mentor.skills == null || learner.skills == null) return null;
            if (mentor.ageTracker.AgeBiologicalYearsFloat < learner.ageTracker.AgeBiologicalYearsFloat + 8f) return null;
            SkillRecord best = null;
            int bestGap = 2;
            foreach (var s in mentor.skills.skills)
            {
                if (s.TotallyDisabled) continue;
                var theirs = learner.skills.GetSkill(s.def);
                if (theirs == null || theirs.TotallyDisabled || theirs.Level >= 20) continue;
                int gap = s.Level - theirs.Level;
                if (gap > bestGap) { best = s; bestGap = gap; }
            }
            return best;
        }

        // ------------------------------------------------------------------ what each kind does when it ends

        public static void Complete(Pawn pawn, Pawn partner, JobDef def, int ticks)
        {
            var component = FamilyComponent.Current;
            if (component == null || ticks < 600) return;
            var registry = component.Registry;
            int now = Activities.Now;
            int lineage = registry.LineageIdFor(registry.GetPerson(pawn.thingIDNumber));

            if (def == WFL_FlavorDefOf.WFL_Reconcile)
            {
                CompleteReconcile(pawn, partner, registry, ticks, now);
                SocialMoments.OnActivity(ActivityKind.Reconciliation, pawn, partner);
                return;
            }

            Activities.CompleteFamilyTime(pawn, partner, ticks, def == WFL_FlavorDefOf.WFL_Teach ? BondCause.Teaching : BondCause.Together);
            registry.Reinforce(lineage, Dim.Hearth, 0.2f);
            registry.Reinforce(lineage, Dim.Devotion, 0.3f);

            if (def == WFL_FlavorDefOf.WFL_Date)
            {
                registry.RecordTogether(pawn.thingIDNumber, partner.thingIDNumber, ticks, now, BondCause.Date);       // a date counts for twice as much
                Activities.GainMemory(pawn, "WFL_Date", partner);
                Activities.GainMemory(partner, "WFL_Date", pawn);
                registry.Reinforce(lineage, Dim.Devotion, 0.5f);
            }
            else if (def == WFL_FlavorDefOf.WFL_Teach)
            {
                var skill = MentorSkill(pawn, partner);
                if (skill != null)
                {
                    var theirs = partner.skills.GetSkill(skill.def);
                    var bond = registry.GetBond(partner.thingIDNumber, pawn.thingIDNumber);
                    float xp = ConflictTuning.MentorXp(skill.Level, theirs.Level, Activities.SocialOf(pawn), bond?.Familiarity ?? 0f, ticks);
                    if (xp > 0f)
                    {
                        theirs.Learn(xp);
                        Activities.GainMemory(partner, "WFL_Learned", pawn);
                        Activities.GainMemory(pawn, "WFL_Taught", partner);
                        registry.Reinforce(lineage, Dim.Scholarly, 0.5f);
                    }
                }
            }
            else
            {
                TellFamilyStory(pawn, partner, registry, lineage);
            }

            // What kind of time it was decides which moments fit: teaching is a lesson, two children are at play, the rest is shared recreation.
            var setting = def == WFL_FlavorDefOf.WFL_Teach ? ActivityKind.Learning
                : (def == WFL_FlavorDefOf.WFL_Date ? ActivityKind.None
                : (pawn.DevelopmentalStage.Juvenile() && partner.DevelopmentalStage.Juvenile() ? ActivityKind.Playdate : ActivityKind.GameNight));
            SocialMoments.OnActivity(setting, pawn, partner);
        }

        /// <summary>An older relative tells a younger one about the family's past. It only counts if the family has defining memories to tell.</summary>
        private static void TellFamilyStory(Pawn teller, Pawn listener, FamilyRegistry registry, int lineage)
        {
            if (teller.ageTracker.AgeBiologicalYearsFloat < listener.ageTracker.AgeBiologicalYearsFloat + 20f) return;
            if (registry.MemoriesOf(lineage).Count == 0) return;
            Activities.GainMemory(listener, "WFL_HeardFamilyStory", teller);
            var told = registry.GetLineage(lineage);
            if (told != null) told.StoriesTold++;
            registry.Reinforce(lineage, Dim.Devotion, 0.5f);
        }

        private static void CompleteReconcile(Pawn pawn, Pawn partner, FamilyRegistry registry, int ticks, int now)
        {
            var rift = registry.OpenRiftBetween(pawn.thingIDNumber, partner.thingIDNumber);
            if (rift == null) return;
            var ab = registry.GetBond(pawn.thingIDNumber, partner.thingIDNumber);
            var ba = registry.GetBond(partner.thingIDNumber, pawn.thingIDNumber);
            float forgiving = (Profile(pawn, Dim.Warmth) + Profile(pawn, Dim.Steadiness) + Profile(partner, Dim.Warmth) + Profile(partner, Dim.Steadiness)) / 2f;
            bool accepted = Rand.Chance(ConflictTuning.ReconcileChance(ab?.Trust ?? 25f, ba?.Trust ?? 25f, forgiving));
            registry.TryReconcile(rift.Id, accepted, now);
            registry.AddFamilyTime(pawn.thingIDNumber, ticks, now);
            registry.AddFamilyTime(partner.thingIDNumber, ticks, now);
            if (accepted)
            {
                Activities.GainMemory(pawn, "WFL_MadePeace", partner);
                Activities.GainMemory(partner, "WFL_MadePeace", pawn);
            }
        }

        private static float Profile(Pawn p, Dim d)
        {
            var profile = FamilyComponent.Current?.ProfileOf(p.thingIDNumber);
            return profile == null ? 0f : profile[(int)d];
        }
    }
}
