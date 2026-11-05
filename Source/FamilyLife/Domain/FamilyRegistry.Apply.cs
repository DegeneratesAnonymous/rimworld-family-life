using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    public sealed partial class FamilyRegistry
    {
        /// <summary>
        /// Reconciles the model with what the game currently shows and returns what changed.
        /// <paramref name="observations"/> must describe every person currently in the colony plus the relatives worth
        /// recording; a recorded colonist who is not mentioned is treated as having left.
        /// Applying the same observations twice changes nothing the second time, and the order of the list does not matter.
        /// </summary>
        public List<FamilyEvent> Apply(IEnumerable<PersonObservation> observations, int nowTick)
        {
            EnsureIndexes();
            var events = new List<FamilyEvent>();
            var obs = Normalize(observations);
            bool bootstrapping = Persons.Count == 0;

            // Everyone recorded as in the colony who was not observed has left it.
            var observedIds = new HashSet<int>(obs.Select(o => o.Id));
            foreach (var p in Persons.ToList())
            {
                if (p.Presence == Presence.Colony && !observedIds.Contains(p.Id))
                    obs.Add(ToObservation(p, Presence.Away));
            }
            obs.Sort((a, b) => a.Id.CompareTo(b.Id));

            var prevPartners = new Dictionary<int, HashSet<int>>();
            var prevParents = new Dictionary<int, HashSet<int>>();
            foreach (var o in obs)
            {
                if (personById.TryGetValue(o.Id, out var existing))
                {
                    prevPartners[o.Id] = new HashSet<int>(existing.PartnerIds());
                    prevParents[o.Id] = new HashSet<int>(existing.ParentIds);
                }
            }

            var newlyRecorded = new HashSet<int>();
            deadThisSweep = new HashSet<int>(obs.Where(x => x.Presence == Presence.Dead).Select(x => x.Id));
            foreach (var o in obs) Upsert(o, nowTick, bootstrapping, events, newlyRecorded);
            deadThisSweep = null;
            RebuildChildIndex();

            AssignLineages(nowTick, events);
            kinshipCache.Clear();
            foreach(int id in newlyRecorded)
            {
                var child=GetPerson(id);
                if(!child.Juvenile || child.ParentIds.Count==0) continue;
                var parents=child.ParentIds.Select(GetPerson).ToList();
                if(parents.All(p=>p!=null && p.SocialFamilyId>0) && parents.Select(p=>p.SocialFamilyId).Distinct().Count()==1)
                    child.SocialFamilyId=parents[0].SocialFamilyId;
            }
            AffiliateLateParents(obs, prevParents, newlyRecorded, nowTick, events);
            AffiliateSpouses(nowTick, events);
            ReportPartnershipChanges(obs, prevPartners, newlyRecorded, nowTick, events);
            ReportJoinings(events, nowTick, bootstrapping);
            PlanHouseholds(obs, prevPartners, newlyRecorded, nowTick, events);

            if (bootstrapping && Persons.Count > 0)
                events.Insert(0, new FamilyEvent(FamilyEventKind.RecordsBegan, nowTick));

            foreach (var e in events)
            {
                if (!e.IsChronicle) continue;
                // Records that began with a game already in progress are not "joins" and "births": say so once.
                if (bootstrapping && e.Kind != FamilyEventKind.RecordsBegan) continue;
                Chronicle.Add(e);
            }
            if (Chronicle.Count > MaxChronicle) Chronicle.RemoveRange(0, Chronicle.Count - MaxChronicle);

            return events;
        }

        // ------------------------------------------------------------------ input

        private static List<PersonObservation> Normalize(IEnumerable<PersonObservation> observations)
        {
            var byId = new Dictionary<int, PersonObservation>();
            if (observations != null)
            {
                foreach (var o in observations)
                {
                    if (o == null) continue;
                    o.ParentIds = Clean(o.ParentIds, o.Id);
                    // Marriage dates arrive in the game's order; sorting the ids must not separate a date from its spouse.
                    var sinceById = new Dictionary<int, int>();
                    for (int i = 0; o.SpouseIds != null && o.SpouseSince != null && i < o.SpouseIds.Count && i < o.SpouseSince.Count; i++)
                        sinceById[o.SpouseIds[i]] = o.SpouseSince[i];
                    o.SpouseIds = Clean(o.SpouseIds, o.Id);
                    o.SpouseSince = o.SpouseIds.Select(id => sinceById.TryGetValue(id, out var s) ? s : 0).ToList();
                    o.LoverIds = Clean(o.LoverIds, o.Id);
                    o.ExIds = Clean(o.ExIds, o.Id);
                    byId[o.Id] = o;
                }
            }
            var list = byId.Values.ToList();
            list.Sort((a, b) => a.Id.CompareTo(b.Id));
            return list;
        }

        private static List<int> Clean(List<int> ids, int selfId)
        {
            if (ids == null) return new List<int>();
            return ids.Where(i => i != selfId).Distinct().OrderBy(i => i).ToList();
        }

        private static List<int> SpouseSinceFor(List<int> spouseIds, PersonObservation o, List<int> oldSpouses, List<int> oldSince)
        {
            var result = new List<int>(spouseIds.Count);
            foreach (var id in spouseIds)
            {
                int at = o.SpouseIds.IndexOf(id);
                int known = at >= 0 && at < o.SpouseSince.Count ? o.SpouseSince[at] : 0;
                if (known == 0)
                {
                    int before = oldSpouses.IndexOf(id);      // a partner who died keeps the date we already knew
                    if (before >= 0 && before < oldSince.Count) known = oldSince[before];
                }
                result.Add(known);
            }
            return result;
        }

        private static List<int> Union(List<int> existing, List<int> observed)
        {
            return (existing ?? new List<int>()).Union(observed).OrderBy(i => i).ToList();
        }

        private HashSet<int> deadThisSweep;      // who is observed dead in the sweep being applied, so processing order cannot matter

        private List<int> KeepDeadPartners(List<int> existing, List<int> observed, List<int> observedExes)
        {
            var result = new SortedSet<int>(observed);
            foreach (var id in existing ?? new List<int>())
                if (!observedExes.Contains(id) && personById.TryGetValue(id, out var other) && (other.Presence == Presence.Dead || (deadThisSweep != null && deadThisSweep.Contains(id))))
                    result.Add(id);
            return result.ToList();
        }

        private static PersonObservation ToObservation(PersonRecord p, Presence presence)
        {
            return new PersonObservation
            {
                Id = p.Id,
                Name = p.Name,
                LastName = p.LastName,
                BirthAbsTick = p.BirthAbsTick,
                AgeTicks = int.MaxValue,
                Presence = presence,
                Juvenile = p.Juvenile,
                ParentIds = new List<int>(p.ParentIds),
                SpouseIds = new List<int>(p.SpouseIds),
                LoverIds = new List<int>(p.LoverIds),
                ExIds = new List<int>(p.ExIds)
            };
        }

        private void Upsert(PersonObservation o, int now, bool bootstrapping, List<FamilyEvent> events, HashSet<int> newlyRecorded)
        {
            bool isNew = !personById.TryGetValue(o.Id, out var p);
            Presence before = Presence.Away;
            if (isNew)
            {
                p = new PersonRecord { Id = o.Id, FirstSeenTick = now, Presence = o.Presence };
                Persons.Add(p);
                personById[p.Id] = p;
                newlyRecorded.Add(p.Id);
                events.Add(new FamilyEvent(FamilyEventKind.PersonRecorded, now, p.Id));
            }
            else
            {
                before = p.Presence;
            }

            if (!string.IsNullOrEmpty(o.Name)) p.Name = o.Name;
            p.LastName = o.LastName ?? "";
            p.BirthAbsTick = o.BirthAbsTick;
            bool wasJuvenile = p.Juvenile;
            p.Juvenile = o.Juvenile;
            // Descent never un-happens, and the game clears every relation of a pawn it discards. So parents only accumulate.
            p.ParentIds = Union(p.ParentIds, o.ParentIds);
            // A partner who has died stays on record (the game forgets them once it discards the body); a living partner
            // follows the game, so a divorce moves them to the ex list.
            var oldSpouses = p.SpouseIds;
            var oldSince = p.SpouseSince ?? new List<int>();
            p.SpouseIds = KeepDeadPartners(p.SpouseIds, o.SpouseIds, o.ExIds);
            p.LoverIds = KeepDeadPartners(p.LoverIds, o.LoverIds, o.ExIds);
            p.ExIds = new List<int>(o.ExIds);
            p.SpouseSince = SpouseSinceFor(p.SpouseIds, o, oldSpouses, oldSince);
            if (!string.IsNullOrEmpty(o.TopSkill)) { p.TopSkill = o.TopSkill; p.TopSkillLevel = o.TopSkillLevel; }

            if (isNew)
            {
                if (o.Presence == Presence.Colony)
                {
                    p.EverColonist = true;
                    if (!bootstrapping)
                    {
                        if (o.ParentIds.Count > 0 && o.AgeTicks < NewbornAgeTicks)
                            events.Add(new FamilyEvent(FamilyEventKind.Birth, now, p.Id, o.ParentIds[0]));
                        else
                            events.Add(new FamilyEvent(FamilyEventKind.JoinedColony, now, p.Id));
                    }
                }
                p.Presence = o.Presence;
                return;
            }

            if (!isNew && wasJuvenile && !o.Juvenile && o.Presence == Presence.Colony)
                events.Add(new FamilyEvent(FamilyEventKind.CameOfAge, now, p.Id));
            if (before == o.Presence) return;
            p.Presence = o.Presence;
            switch (o.Presence)
            {
                case Presence.Dead:
                    p.DeathTick = now;
                    events.Add(new FamilyEvent(FamilyEventKind.Died, now, p.Id));
                    break;
                case Presence.Colony:
                    p.EverColonist = true;
                    p.DeathTick = -1;
                    events.Add(new FamilyEvent(FamilyEventKind.JoinedColony, now, p.Id));
                    if (p.AwayTick >= 0 && now - p.AwayTick >= ActivityTuning.ReunionAwayTicks)
                        events.Add(new FamilyEvent(FamilyEventKind.Reunion, now, p.Id));
                    p.AwayTick = -1;
                    break;
                case Presence.Away:
                    if (before == Presence.Dead) p.DeathTick = -1;
                    else
                    {
                        if (before == Presence.Colony) p.AwayTick = now;
                        events.Add(new FamilyEvent(FamilyEventKind.LeftColony, now, p.Id));
                    }
                    break;
            }
        }

        // ------------------------------------------------------------------ lineages

        /// <summary>
        /// A lineage is built around people with a colonist in their family tree: colonists, their ancestors, and everyone
        /// descended from those ancestors (siblings, cousins). In-laws and other relatives are recorded but have no lineage.
        /// A person's lineage is decided once and then kept, so a changed surname never moves anyone.
        /// A person with no known ancestry whose spouse carries the same surname does not found a second family of that
        /// name: they belong to the spouse's family by marriage.
        /// </summary>
        private void AssignLineages(int now, List<FamilyEvent> events)
        {
            var bearing = new HashSet<int>();
            foreach (var p in Persons)
                if (p.EverColonist) AddAncestors(p.Id, bearing, 0);

            var queue = new Queue<int>(bearing);
            while (queue.Count > 0)
            {
                int id = queue.Dequeue();
                if (!childrenByParent.TryGetValue(id, out var kids)) continue;
                foreach (var kid in kids)
                    if (personById.ContainsKey(kid) && bearing.Add(kid)) queue.Enqueue(kid);
            }

            foreach (var id in bearing.OrderBy(x => x).ToList())
                EnsureLineage(personById[id], new HashSet<int>(), bearing, now, events);
        }

        /// <summary>Gives the person a lineage (or a marriage affiliation), settling their parents and spouse first. Returns 0 if it has none.</summary>
        private int EnsureLineage(PersonRecord person, HashSet<int> visiting, HashSet<int> bearing, int now, List<FamilyEvent> events)
        {
            if (person.PrimaryLineageId != 0) return person.PrimaryLineageId;
            if (!visiting.Add(person.Id)) return 0;     // a cycle in damaged data: stop, never loop

            var parents = UsableParents(person, bearing);
            foreach (var parent in parents) EnsureLineage(parent, visiting, bearing, now, events);
            var withLineage = parents.Where(par => par.PrimaryLineageId != 0).ToList();
            if (withLineage.Count > 0)
            {
                var source = withLineage.FirstOrDefault(par => SameName(par.LastName, person.LastName)) ?? withLineage[0];
                person.PrimaryLineageId = source.PrimaryLineageId;
                return person.PrimaryLineageId;
            }

            // No known ancestry. Join the spouse's family if they share a surname, rather than founding a duplicate.
            if (AlreadyBelongsByMarriage(person)) return 0;
            foreach (var spouseId in person.SpouseIds.OrderBy(x => x))
            {
                if (!personById.TryGetValue(spouseId, out var spouse) || !bearing.Contains(spouseId)) continue;
                if (!SameName(spouse.LastName, person.LastName)) continue;
                bool spouseIsRoot = UsableParents(spouse, bearing).Count == 0;
                if (spouseIsRoot && spouse.Id > person.Id) continue;      // of two founders, the lower id is the root
                int spouseLineage = EnsureLineage(spouse, visiting, bearing, now, events);
                if (spouseLineage == 0) continue;
                Affiliate(person, spouseLineage, AffiliationKind.Spouse, now, events);
                return 0;
            }

            var lineage = new Lineage
            {
                Id = NextLineageId++,
                Name = string.IsNullOrEmpty(person.LastName) ? person.Name : person.LastName,
                FounderId = person.Id,
                FoundedTick = now
            };
            Lineages.Add(lineage);
            lineageById[lineage.Id] = lineage;
            person.PrimaryLineageId = lineage.Id;
            events.Add(new FamilyEvent(FamilyEventKind.LineageFounded, now, person.Id, lineage.Id));
            return lineage.Id;
        }

        private List<PersonRecord> UsableParents(PersonRecord person, HashSet<int> bearing)
        {
            var list = new List<PersonRecord>();
            foreach (var pid in person.ParentIds.OrderBy(x => x))
                if (bearing.Contains(pid) && personById.TryGetValue(pid, out var parent)) list.Add(parent);
            return list;
        }

        private bool AlreadyBelongsByMarriage(PersonRecord person)
        {
            foreach (var lineage in Lineages)
                if (lineage.HasAffiliate(person.Id) && SameName(lineage.Name, person.LastName)) return true;
            return false;
        }

        private void AddAncestors(int id, HashSet<int> set, int depth)
        {
            if (depth > AncestorDepthLimit || !personById.TryGetValue(id, out var p)) return;
            if (!set.Add(id)) return;
            foreach (var parentId in p.ParentIds) AddAncestors(parentId, set, depth + 1);
        }

        private static bool SameName(string a, string b)
        {
            return !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A parent link that appears after a person was first recorded is adoption or recognition, not descent.</summary>
        private void AffiliateLateParents(List<PersonObservation> obs, Dictionary<int, HashSet<int>> prevParents,
            HashSet<int> newlyRecorded, int now, List<FamilyEvent> events)
        {
            foreach (var o in obs)
            {
                if (newlyRecorded.Contains(o.Id) || !prevParents.TryGetValue(o.Id, out var before)) continue;
                var person = personById[o.Id];
                if (person.PrimaryLineageId == 0) continue;
                foreach (var parentId in person.ParentIds)
                {
                    if (before.Contains(parentId) || !personById.TryGetValue(parentId, out var parent)) continue;
                    if (parent.PrimaryLineageId == 0 || parent.PrimaryLineageId == person.PrimaryLineageId) continue;
                    Affiliate(person, parent.PrimaryLineageId, AffiliationKind.Recognized, now, events);
                }
            }
        }

        private void AffiliateSpouses(int now, List<FamilyEvent> events)
        {
            foreach (var person in Persons)
            {
                if (person.PrimaryLineageId == 0) continue;
                foreach (var spouseId in person.SpouseIds)
                {
                    if (!personById.TryGetValue(spouseId, out var spouse)) continue;
                    if (spouse.PrimaryLineageId == 0 || spouse.PrimaryLineageId == person.PrimaryLineageId) continue;
                    Affiliate(person, spouse.PrimaryLineageId, AffiliationKind.Spouse, now, events);
                }
            }
        }

        private void Affiliate(PersonRecord person, int lineageId, AffiliationKind kind, int now, List<FamilyEvent> events)
        {
            var lineage = GetLineage(lineageId);
            if (lineage == null || lineage.HasAffiliate(person.Id)) return;
            lineage.Affiliations.Add(new Affiliation(person.Id, kind, now));
            events.Add(new FamilyEvent(FamilyEventKind.LineageAffiliated, now, person.Id, lineageId));
        }

        // ------------------------------------------------------------------ partnerships

        private void ReportPartnershipChanges(List<PersonObservation> obs, Dictionary<int, HashSet<int>> prevPartners,
            HashSet<int> newlyRecorded, int now, List<FamilyEvent> events)
        {
            var reported = new HashSet<long>();
            foreach (var o in obs)
            {
                if (newlyRecorded.Contains(o.Id) || !prevPartners.TryGetValue(o.Id, out var before)) continue;
                var person = personById[o.Id];
                var current = new HashSet<int>(person.PartnerIds());
                foreach (var other in current.Except(before))
                    if (reported.Add(PairKey(o.Id, other, true)))
                        events.Add(new FamilyEvent(FamilyEventKind.PartnershipFormed, now, Math.Min(o.Id, other), Math.Max(o.Id, other)));
                // Only an explicit break-up counts: a partner who merely vanishes (discarded by the game) did not break up.
                foreach (var other in before.Except(current).Where(id => person.ExIds.Contains(id)))
                    if (reported.Add(PairKey(o.Id, other, false)))
                        events.Add(new FamilyEvent(FamilyEventKind.PartnershipEnded, now, Math.Min(o.Id, other), Math.Max(o.Id, other)));
            }
        }

        /// <summary>
        /// A new partnership can join two families (each partner belongs to a different one by descent) and can make a step-family
        /// (one of them already has children who are not the other's). Both are events in their own right. Not reported while the
        /// records are only beginning, since those partnerships are not new.
        /// </summary>
        private void ReportJoinings(List<FamilyEvent> events, int now, bool bootstrapping)
        {
            if (bootstrapping) return;
            foreach (var e in events.Where(x => x.Kind == FamilyEventKind.PartnershipFormed).ToList())
            {
                if (!personById.TryGetValue(e.A, out var a) || !personById.TryGetValue(e.B, out var b)) continue;
                if (a.PrimaryLineageId != 0 && b.PrimaryLineageId != 0 && a.PrimaryLineageId != b.PrimaryLineageId)
                    events.Add(new FamilyEvent(FamilyEventKind.FamiliesJoined, now, a.Id, b.Id));
                var aKids = ChildrenOf(a.Id);
                var bKids = ChildrenOf(b.Id);
                if (aKids.Any(c => !bKids.Contains(c)) || bKids.Any(c => !aKids.Contains(c)))
                    events.Add(new FamilyEvent(FamilyEventKind.StepFamilyFormed, now, a.Id, b.Id));
            }
        }

        private static long PairKey(int a, int b, bool formed)
        {
            long lo = Math.Min(a, b), hi = Math.Max(a, b);
            return (lo << 33) ^ (hi << 1) ^ (formed ? 1 : 0);
        }

        // ------------------------------------------------------------------ households

        private void PlanHouseholds(List<PersonObservation> obs, Dictionary<int, HashSet<int>> prevPartners,
            HashSet<int> newlyRecorded, int now, List<FamilyEvent> events)
        {
            // 1. People who are no longer in the colony leave their household (and remember it).
            foreach (var p in Persons)
                if (p.HouseholdId != 0 && !p.InColony) LeaveHousehold(p, now, events);

            // 2. Partners who have just paired up (both already known) move in together.
            var handled = new HashSet<long>();
            foreach (var o in obs)
            {
                if (newlyRecorded.Contains(o.Id) || !prevPartners.TryGetValue(o.Id, out var before)) continue;
                var a = personById[o.Id];
                if (!a.InColony || a.Juvenile || a.HouseholdId == 0) continue;
                foreach (var otherId in a.PartnerIds())
                {
                    if (before.Contains(otherId) || newlyRecorded.Contains(otherId)) continue;
                    if (!personById.TryGetValue(otherId, out var b)) continue;
                    if (!b.InColony || b.Juvenile || b.HouseholdId == 0) continue;
                    if (!handled.Add(PairKey(a.Id, b.Id, true))) continue;
                    FormCouple(a.Id < b.Id ? a : b, a.Id < b.Id ? b : a, now, events);
                }
            }

            // 3. Everyone in the colony without a household gets one: adults first, oldest first.
            var unhoused = Persons
                .Where(p => p.InColony && p.HouseholdId == 0)
                .OrderBy(p => p.Juvenile ? 1 : 0)
                .ThenBy(p => p.BirthAbsTick)
                .ThenBy(p => p.Id)
                .ToList();
            foreach (var p in unhoused) PlaceNewResident(p, now, events);

            // 4. Households nobody lives in any more are dissolved.
            DissolveEmpty(now, events);
        }

        private void PlaceNewResident(PersonRecord p, int now, List<FamilyEvent> events)
        {
            // Back where they used to live, if that household still exists.
            if (p.LastHouseholdId != 0 && householdById.TryGetValue(p.LastHouseholdId, out var home))
            {
                JoinHousehold(p, home, now, events);
                return;
            }

            if (!p.Juvenile)
            {
                foreach (var partnerId in p.PartnerIds())
                {
                    if (personById.TryGetValue(partnerId, out var partner) && partner.InColony && !partner.Juvenile && partner.HouseholdId != 0)
                    {
                        JoinHousehold(p, householdById[partner.HouseholdId], now, events);
                        return;
                    }
                }
            }

            // A child goes to a parent, preferring the household that holds both parents; then to a juvenile sibling.
            var parents = p.ParentIds
                .OrderBy(id => id)
                .Select(id => personById.TryGetValue(id, out var par) ? par : null)
                .Where(par => par != null && par.InColony && par.HouseholdId != 0)
                .ToList();
            if (parents.Count > 0)
            {
                var chosen = parents.FirstOrDefault(par => parents.Any(o => o.Id != par.Id && o.HouseholdId == par.HouseholdId)) ?? parents[0];
                JoinHousehold(p, householdById[chosen.HouseholdId], now, events);
                return;
            }
            if (p.Juvenile)
            {
                foreach (var siblingId in SiblingsOf(p.Id))
                {
                    var sibling = personById[siblingId];
                    if (sibling.InColony && sibling.Juvenile && sibling.HouseholdId != 0)
                    {
                        JoinHousehold(p, householdById[sibling.HouseholdId], now, events);
                        return;
                    }
                }
            }

            var household = NewHousehold(now);
            events.Add(new FamilyEvent(FamilyEventKind.HouseholdFormed, now, p.Id, household.Id));
            JoinHousehold(p, household, now, events, quiet: true);
        }

        /// <summary>
        /// Two adults who have just become partners. Where they live is decided by who would be left behind:
        /// nobody (then that household just grows), or others (then the couple and their dependent children move together).
        /// </summary>
        private void FormCouple(PersonRecord a, PersonRecord b, int now, List<FamilyEvent> events)
        {
            if (a.HouseholdId == b.HouseholdId) return;
            var ha = householdById[a.HouseholdId];
            var hb = householdById[b.HouseholdId];
            var depsA = DependentsIn(a, ha);
            var depsB = DependentsIn(b, hb);

            Household target;
            if (HasOtherPartnerIn(a, b, ha)) target = ha;                 // a already shares a home with another partner: b joins it
            else if (HasOtherPartnerIn(b, a, hb)) target = hb;
            else if (StayingBehind(ha, a, depsA) == 0) target = ha;       // a's household is just a and their dependents
            else if (StayingBehind(hb, b, depsB) == 0) target = hb;
            else
            {
                target = NewHousehold(now);
                events.Add(new FamilyEvent(FamilyEventKind.HouseholdFormed, now, a.Id, target.Id));
            }

            if (target == ha) { MoveGroup(b, depsB, target, now, events); }
            else if (target == hb) { MoveGroup(a, depsA, target, now, events); }
            else { MoveGroup(a, depsA, target, now, events); MoveGroup(b, depsB, target, now, events); }
        }

        private bool HasOtherPartnerIn(PersonRecord person, PersonRecord excluded, Household household)
        {
            foreach (var pid in person.PartnerIds())
            {
                if (pid == excluded.Id) continue;
                if (household.ResidentIds.Contains(pid)) return true;
            }
            return false;
        }

        private List<PersonRecord> DependentsIn(PersonRecord parent, Household household)
        {
            var list = new List<PersonRecord>();
            foreach (var childId in ChildrenOf(parent.Id))
                if (personById.TryGetValue(childId, out var child) && child.InColony && child.Juvenile && child.HouseholdId == household.Id)
                    list.Add(child);
            return list;
        }

        private static int StayingBehind(Household household, PersonRecord mover, List<PersonRecord> dependents)
        {
            return household.ResidentIds.Count(id => id != mover.Id && !dependents.Any(d => d.Id == id));
        }

        private void MoveGroup(PersonRecord adult, List<PersonRecord> dependents, Household target, int now, List<FamilyEvent> events)
        {
            JoinHousehold(adult, target, now, events);
            foreach (var d in dependents) JoinHousehold(d, target, now, events);
        }

        private Household NewHousehold(int now)
        {
            var household = new Household { Id = NextHouseholdId++, FormedTick = now };
            Households.Add(household);
            householdById[household.Id] = household;
            return household;
        }

        private void JoinHousehold(PersonRecord p, Household household, int now, List<FamilyEvent> events, bool quiet = false)
        {
            if (p.HouseholdId == household.Id) return;
            if (p.HouseholdId != 0) LeaveHousehold(p, now, events);
            p.HouseholdId = household.Id;
            if (!household.ResidentIds.Contains(p.Id)) household.ResidentIds.Add(p.Id);
            household.ResidentIds.Sort();
            if (!quiet) events.Add(new FamilyEvent(FamilyEventKind.HouseholdJoined, now, p.Id, household.Id));
        }

        private void LeaveHousehold(PersonRecord p, int now, List<FamilyEvent> events)
        {
            if (p.HouseholdId == 0) return;
            int id = p.HouseholdId;
            if (householdById.TryGetValue(id, out var household)) household.ResidentIds.Remove(p.Id);
            p.LastHouseholdId = id;
            p.HouseholdId = 0;
            events.Add(new FamilyEvent(FamilyEventKind.HouseholdLeft, now, p.Id, id));
        }

        private void DissolveEmpty(int now, List<FamilyEvent> events)
        {
            for (int i = Households.Count - 1; i >= 0; i--)
            {
                var h = Households[i];
                if (h.ResidentIds.Count > 0) continue;
                Households.RemoveAt(i);
                householdById.Remove(h.Id);
                events.Add(new FamilyEvent(FamilyEventKind.HouseholdDissolved, now, 0, h.Id));
            }
        }

        // ------------------------------------------------------------------ player-directed changes

        /// <summary>
        /// Moves a colonist to another household (or to a new one when <paramref name="targetHouseholdId"/> is 0).
        /// A moving adult takes their juvenile children who live with them.
        /// </summary>
        public List<FamilyEvent> MoveToHousehold(int personId, int targetHouseholdId, int now, bool withDependents = true)
        {
            EnsureIndexes();
            var events = new List<FamilyEvent>();
            if (!personById.TryGetValue(personId, out var person) || !person.InColony) return events;

            Household target;
            if (targetHouseholdId == 0)
            {
                // Moving out of a household you live alone in changes nothing.
                var current = person.HouseholdId != 0 ? householdById[person.HouseholdId] : null;
                var deps0 = current != null && withDependents && !person.Juvenile ? DependentsIn(person, current) : new List<PersonRecord>();
                if (current != null && StayingBehind(current, person, deps0) == 0) return events;
                target = NewHousehold(now);
                events.Add(new FamilyEvent(FamilyEventKind.HouseholdFormed, now, person.Id, target.Id));
            }
            else if (!householdById.TryGetValue(targetHouseholdId, out target) || person.HouseholdId == target.Id)
            {
                return events;
            }

            var from = person.HouseholdId != 0 ? householdById[person.HouseholdId] : null;
            var deps = from != null && withDependents && !person.Juvenile ? DependentsIn(person, from) : new List<PersonRecord>();
            MoveGroup(person, deps, target, now, events);
            DissolveEmpty(now, events);
            return events;
        }
    }
}
