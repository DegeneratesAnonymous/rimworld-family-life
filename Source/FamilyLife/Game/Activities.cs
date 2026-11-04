using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace FamilyLife
{
    /// <summary>
    /// What the game layer needs to decide and carry out family activities: who is free, who to be with, and what a finished
    /// activity does. The choices themselves are made by the domain (<see cref="FamilyRegistry.RankCompanions"/> and friends).
    /// </summary>
    internal static class Activities
    {
        public const float JoyPerTick = 0.00018f;
        public const float FamilyTimeMaxDistance = 40f;

        private static FamilySettings Settings => FamilyLifeMod.Settings;
        public static bool FamilyTimeEnabled => Settings == null || Settings.familyActivities;
        public static bool StoriesEnabled => Settings == null || (Settings.familyActivities && Settings.bedtimeStories);
        public static bool MealRewardsEnabled => Settings == null || (Settings.familyActivities && Settings.sharedMeals);
        public static int DailyTargetTicks => Settings == null ? ActivityTuning.DefaultDailyTargetTicks : Mathf.RoundToInt(Settings.dailyFamilyHours * BondTuning.TicksPerHour);
        public static int Now => Find.TickManager.TicksGame;

        public static ThoughtDef Thought(string name) => DefDatabase<ThoughtDef>.GetNamedSilentFail(name);

        /// <summary>
        /// Someone who can be spent time with, or share a story, without being pulled off anything that matters: awake, well,
        /// not on a work shift, and currently idle or doing something for themselves rather than a job.
        /// </summary>
        public static bool IsFree(Pawn p, bool checkCurrentJob = true)
        {
            if (p == null || !p.Spawned || p.Dead || p.Downed || p.Drafted || p.InMentalState) return false;
            if (p.RaceProps == null || !p.RaceProps.Humanlike || p.IsPrisoner || p.DevelopmentalStage.Baby()) return false;
            if (!p.Awake()) return false;
            var assignment = p.timetable?.CurrentAssignment;
            if (assignment == TimeAssignmentDefOf.Work || assignment == TimeAssignmentDefOf.Sleep) return false;
            if (!checkCurrentJob) return true;      // someone choosing what to do next has no current job to judge
            var tag = p.mindState?.lastJobTag;
            return tag == JobTag.Idle || tag == JobTag.SatisfyingNeeds;
        }

        private static readonly AttemptThrottle throttle = new AttemptThrottle();

        /// <summary>
        /// Stops something that keeps being offered (a think-tree entry, a joy giver) from doing its work every time. Channels keep
        /// the kinds of attempt apart for one pawn. Not saved: a reload simply allows a fresh try.
        /// </summary>
        public static bool AttemptAllowed(int pawnId, int now, int cooldownTicks, int channel = 0) => throttle.Allowed(pawnId, now, cooldownTicks, channel);

        public static void NoteAttempt(int pawnId, int now, int channel = 0) => throttle.Note(pawnId, now, channel);

        /// <summary>Well enough, fed enough and rested enough to spend time on someone else.</summary>
        public static bool CanSpareAttention(Pawn p)
        {
            if (p.needs?.food != null && p.needs.food.CurLevel < 0.2f) return false;
            if (p.needs?.rest != null && p.needs.rest.CurLevel < 0.15f) return false;
            return !HealthAIUtility.ShouldSeekMedicalRest(p);
        }

        // ------------------------------------------------------------------ family time

        /// <summary>The relative this pawn most wants to be with right now, among those who are free and near. Null if nobody or out of budget.</summary>
        public static Pawn ChooseCompanion(Pawn pawn)
        {
            var component = FamilyComponent.Current;
            if (component == null || pawn?.Map == null || !IsFree(pawn, checkCurrentJob: false) || !CanSpareAttention(pawn)) return null;
            var registry = component.Registry;
            var record = registry.GetPerson(pawn.thingIDNumber);
            if (record == null || !record.InColony) return null;
            int now = Now;
            if (!registry.HasFamilyTimeLeft(record.Id, now, DailyTargetTicks) || !AttemptAllowed(record.Id, now, 600)) return null;

            float appetite = Appetite(pawn, registry, record);
            var available = new List<int>();
            foreach (var bond in registry.BondsFrom(record.Id))
            {
                var other = component.TryGetPawn(bond.To);
                if (other == null || other.Map != pawn.Map || !IsFree(other)) continue;
                if (other.Position.DistanceTo(pawn.Position) > FamilyTimeMaxDistance) continue;
                available.Add(bond.To);
            }
            var ranked = registry.RankCompanions(record.Id, available, now, appetite);
            foreach (var candidate in ranked.Take(3))
            {
                var other = component.TryGetPawn(candidate.Id);
                if (other != null && pawn.CanReach(other, PathEndMode.Touch, Danger.None))
                {
                    NoteAttempt(record.Id, now);
                    return other;
                }
            }
            return null;
        }

        /// <summary>How much this person wants family time: their family's culture and their own character both count.</summary>
        public static float Appetite(Pawn pawn, FamilyRegistry registry, PersonRecord record)
        {
            var component = FamilyComponent.Current;
            return registry.FamilyTimeAppetite(registry.LineageIdFor(record), component?.ProfileOf(pawn.thingIDNumber));
        }

        public static void CompleteFamilyTime(Pawn pawn, Pawn partner, int ticksSpent, BondCause cause = BondCause.Together)
        {
            var component = FamilyComponent.Current;
            if (component == null || ticksSpent < 600) return;
            int now = Now;
            var registry = component.Registry;
            registry.RecordTogether(pawn.thingIDNumber, partner.thingIDNumber, ticksSpent, now, cause);
            registry.AddFamilyTime(pawn.thingIDNumber, ticksSpent, now);
            registry.AddFamilyTime(partner.thingIDNumber, ticksSpent, now);
            if (ticksSpent >= 1200)
            {
                GainMemory(pawn, "WFL_FamilyTimeSpent", partner);
                GainMemory(partner, "WFL_FamilyTimeSpent", pawn);
            }
        }

        public static void GainMemory(Pawn pawn, string thoughtName, Pawn other)
        {
            var def = Thought(thoughtName);
            if (def == null || pawn?.needs?.mood?.thoughts?.memories == null || pawn.Dead) return;
            pawn.needs.mood.thoughts.memories.TryGainMemory(def, other);
        }

        // ------------------------------------------------------------------ bedtime stories

        public static int SocialOf(Pawn p) => p.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;

        public static SkillRecord BestSkillOf(Pawn p, Pawn forChild)
        {
            SkillRecord best = null;
            foreach (var s in p.skills.skills)
            {
                if (s.TotallyDisabled) continue;
                var childSkill = forChild.skills.GetSkill(s.def);
                if (childSkill == null || childSkill.TotallyDisabled || childSkill.Level >= ActivityTuning.StoryMaxChildSkill) continue;
                if (best == null || s.Level > best.Level) best = s;
            }
            return best;
        }

        /// <summary>The unforbidden, reachable book that would teach this child the most, or null. A story works without one.</summary>
        public static Thing FindBookFor(Pawn reader, Pawn child)
        {
            Thing best = null;
            float bestValue = 0f;
            var things = reader.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);
            for (int i = 0; i < things.Count; i++)
            {
                var t = things[i];
                var comp = (t as ThingWithComps)?.GetComp<CompBook>();
                if (comp == null || !t.Spawned || t.IsForbidden(reader)) continue;
                if (t.Position.DistanceTo(reader.Position) > 40f) continue;
                float value = 0f;
                foreach (var doer in comp.Doers.OfType<BookOutcomeDoerGainSkillExp>())
                    foreach (var pair in doer.Values)
                    {
                        var skill = child.skills.GetSkill(pair.Key);
                        if (skill != null && !skill.TotallyDisabled && skill.Level < ActivityTuning.StoryMaxChildSkill) value += pair.Value;
                    }
                if (value <= bestValue) continue;
                if (!reader.CanReserve(t) || !reader.CanReach(t, PathEndMode.ClosestTouch, Danger.None)) continue;
                best = t;
                bestValue = value;
            }
            return best;
        }

        /// <summary>Teaches the child a little, strengthens the bond, and counts the time. Returns a short description for the log.</summary>
        public static string CompleteStory(Pawn reader, Pawn child, Thing book, int ticksRead)
        {
            var component = FamilyComponent.Current;
            if (component == null || child.skills == null || reader.skills == null || ticksRead < ActivityTuning.StoryTicks / 2) return null;
            int now = Now;
            float lengthShare = Mathf.Clamp01(ticksRead / (float)ActivityTuning.StoryTicks);
            float social = SocialOf(reader);
            string taught = null;

            var comp = (book as ThingWithComps)?.GetComp<CompBook>();
            bool anyFromBook = false;
            if (comp != null)
            {
                foreach (var doer in comp.Doers.OfType<BookOutcomeDoerGainSkillExp>())
                    foreach (var pair in doer.Values)
                    {
                        var skill = child.skills.GetSkill(pair.Key);
                        if (skill == null || skill.TotallyDisabled || skill.Level >= ActivityTuning.StoryMaxChildSkill) continue;
                        float xp = FamilyRegistry.StoryXp(true, pair.Value, ticksRead, social, 0f);
                        skill.Learn(xp);
                        anyFromBook = true;
                        taught = pair.Key.defName;
                    }
            }
            if (!anyFromBook)
            {
                var best = BestSkillOf(reader, child);
                if (best != null)
                {
                    var skill = child.skills.GetSkill(best.def);
                    skill.Learn(FamilyRegistry.StoryXp(false, 0f, ticksRead, social, best.Level) * lengthShare);
                    taught = best.def.defName;
                }
            }

            component.Registry.RecordTogether(reader.thingIDNumber, child.thingIDNumber, ticksRead, now, BondCause.Story);
            component.Registry.AddFamilyTime(reader.thingIDNumber, ticksRead, now);
            component.Registry.AddFamilyTime(child.thingIDNumber, ticksRead, now);
            GainMemory(child, "WFL_HeardBedtimeStory", reader);
            GainMemory(reader, "WFL_ToldBedtimeStory", child);
            SocialMoments.OnActivity(ActivityKind.Storytelling, reader, child, "A2-01");       // the story is the setting; how it was received is the moment
            return taught;
        }

        // ------------------------------------------------------------------ eating together

        public static void OnAte(Pawn pawn)
        {
            if (!MealRewardsEnabled || pawn == null || !pawn.RaceProps.Humanlike) return;
            var component = FamilyComponent.Current;
            var record = component?.Registry.GetPerson(pawn.thingIDNumber);
            if (record == null || !record.InColony) return;

            int now = Now;
            var room = pawn.Spawned ? pawn.GetRoom() : null;
            int roomKey = room != null && !room.PsychologicallyOutdoors ? room.ID : -1;

            var ring = component.RecentMeals;
            ring.RemoveAll(m => now - m.Tick > ActivityTuning.SharedMealWindowTicks * 2);
            foreach (var partnerId in component.Registry.SharedMealPartners(record.Id, roomKey, now, ring))
            {
                component.Registry.RecordTogether(record.Id, partnerId, (int)ActivityTuning.SharedMealTicks, now, BondCause.Meal);
                var partner = component.TryGetPawn(partnerId);
                GainMemory(pawn, "WFL_SharedMeal", partner);
                GainMemory(partner, "WFL_SharedMeal", pawn);
            }
            SocialMoments.OnMeal(pawn, roomKey, ring, now);
            ring.RemoveAll(m => m.PersonId == record.Id);
            ring.Add(new RecentMeal { PersonId = record.Id, Tick = now, RoomKey = roomKey });
            if (ring.Count > 64) ring.RemoveRange(0, ring.Count - 64);
        }
    }
}
