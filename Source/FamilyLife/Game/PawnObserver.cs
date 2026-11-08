using System;
using System.Collections.Generic;
using FamilyLife.Domain;
using RimWorld;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// Reads vanilla relations into plain observations. Vanilla stays the source of truth for who is related to whom;
    /// the mod only records and interprets it. The walk is bounded so a large world cannot make a sweep expensive.
    /// </summary>
    internal static class PawnObserver
    {
        private const int MaxDepth = 3;       // generations up and down from a colonist
        private const int MaxPeople = 500;

        /// <param name="index">Pawn lookup by id; replaced with the pawns seen this time.</param>
        /// <param name="knownColonyIds">People recorded as in the colony. Any of them the walk did not reach (a colonist who died or left
        /// with no relatives around) are looked up directly, so a death is never mistaken for someone simply leaving.</param>
        public static List<PersonObservation> Observe(Dictionary<int, Pawn> index, IEnumerable<int> knownColonyIds, Dictionary<int, float[]> profiles = null)
        {
            var previous = new Dictionary<int, Pawn>(index);
            index.Clear();
            var colonyIds = new HashSet<int>();
            var colonists = new List<Pawn>();
            AddColonists(PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists_NoLodgers, colonists, colonyIds);
            AddColonists(PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_SlavesOfColony, colonists, colonyIds);

            var result = new List<PersonObservation>();
            var seen = new HashSet<int>();
            var queue = new Queue<KeyValuePair<Pawn, int>>();
            foreach (var colonist in colonists)
            {
                seen.Add(colonist.thingIDNumber);
                queue.Enqueue(new KeyValuePair<Pawn, int>(colonist, 0));
            }

            while (queue.Count > 0)
            {
                var entry = queue.Dequeue();
                if (result.Count >= MaxPeople && entry.Value > 0) continue;     // the cap trims relatives, never colonists
                var pawn = entry.Key;
                index[pawn.thingIDNumber] = pawn;
                result.Add(Make(pawn, colonyIds, profiles));
                if (entry.Value >= MaxDepth || pawn.relations == null) continue;

                foreach (var rel in pawn.relations.DirectRelations)
                {
                    var other = rel.otherPawn;
                    if (!Recordable(other) || seen.Contains(other.thingIDNumber)) continue;
                    if (rel.def == PawnRelationDefOf.Parent)
                    {
                        seen.Add(other.thingIDNumber);
                        queue.Enqueue(new KeyValuePair<Pawn, int>(other, entry.Value + 1));
                    }
                    else if (IsPartnerOrEx(rel.def))
                    {
                        seen.Add(other.thingIDNumber);                                    // recorded, but not walked further
                        queue.Enqueue(new KeyValuePair<Pawn, int>(other, MaxDepth));
                    }
                }

                foreach (var other in pawn.relations.RelatedPawns)
                {
                    if (!Recordable(other) || seen.Contains(other.thingIDNumber) || other.relations == null) continue;
                    if (!other.relations.DirectRelationExists(PawnRelationDefOf.Parent, pawn)) continue;      // a child of this pawn
                    seen.Add(other.thingIDNumber);
                    queue.Enqueue(new KeyValuePair<Pawn, int>(other, entry.Value + 1));
                }
            }

            if (knownColonyIds != null)
            {
                foreach (var id in knownColonyIds)
                {
                    if (seen.Contains(id)) continue;
                    var pawn = previous.TryGetValue(id, out var remembered) && remembered != null ? remembered : FindPawn(id);
                    if (pawn == null || pawn.Discarded || !Recordable(pawn)) continue;
                    seen.Add(id);
                    index[id] = pawn;
                    result.Add(Make(pawn, colonyIds, profiles));
                }
            }
            return result;
        }

        private static Pawn FindPawn(int id)
        {
            var all = PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead;
            for (int i = 0; i < all.Count; i++)
                if (all[i].thingIDNumber == id) return all[i];
            return null;
        }

        private static void AddColonists(List<Pawn> source, List<Pawn> into, HashSet<int> ids)
        {
            if (source == null) return;
            foreach (var p in source)
                if (Recordable(p) && ids.Add(p.thingIDNumber)) into.Add(p);
        }

        private static bool Recordable(Pawn p)
        {
            return p != null && !p.Discarded && p.RaceProps != null && p.RaceProps.Humanlike;
        }

        private static bool IsPartnerOrEx(PawnRelationDef def)
        {
            return def == PawnRelationDefOf.Spouse || def == PawnRelationDefOf.Lover || def == PawnRelationDefOf.Fiance
                || def == PawnRelationDefOf.ExSpouse || def == PawnRelationDefOf.ExLover;
        }

        private static PersonObservation Make(Pawn p, HashSet<int> colonyIds, Dictionary<int, float[]> profiles)
        {
            var o = new PersonObservation { Id = p.thingIDNumber };
            if (p.Name != null)
            {
                o.Name = p.Name.ToStringShort;
                o.LastName = (p.Name as NameTriple)?.Last ?? "";
                o.FirstName = (p.Name as NameTriple)?.First ?? "";
            }
            else
            {
                o.Name = p.LabelShort;
            }
            if (p.ageTracker != null)
            {
                o.BirthAbsTick = p.ageTracker.BirthAbsTicks;
                long age = p.ageTracker.AgeBiologicalTicks;
                o.AgeTicks = age > int.MaxValue ? int.MaxValue : (int)age;
            }
            o.Presence = p.Dead ? Presence.Dead : (colonyIds.Contains(p.thingIDNumber) ? Presence.Colony : Presence.Away);
            o.Juvenile = p.DevelopmentalStage.Juvenile();
            if (o.Presence == Presence.Colony)
            {
                if (profiles != null) profiles[p.thingIDNumber] = TraitReader.ProfileOf(p);
                var best = TraitReader.BestSkill(p);
                if (best != null) { o.TopSkill = best.def.defName; o.TopSkillLevel = best.Level; }
            }

            if (p.relations != null)
            {
                foreach (var rel in p.relations.DirectRelations)
                {
                    var other = rel.otherPawn;
                    if (other == null) continue;
                    int id = other.thingIDNumber;
                    var def = rel.def;
                    if (def == PawnRelationDefOf.Parent) o.ParentIds.Add(id);
                    else if (def == PawnRelationDefOf.Spouse) { o.SpouseIds.Add(id); o.SpouseSince.Add(rel.startTicks); }
                    else if (def == PawnRelationDefOf.Lover || def == PawnRelationDefOf.Fiance) o.LoverIds.Add(id);
                    else if (def == PawnRelationDefOf.ExSpouse || def == PawnRelationDefOf.ExLover) o.ExIds.Add(id);
                }
            }
            return o;
        }
    }
}
