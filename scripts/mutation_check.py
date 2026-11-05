"""Breaks a rule on purpose, runs the tests, and reports whether the suite noticed. Always restores the file.

Usage: python scripts/mutation_check.py
A mutation that is NOT caught means a rule has no test protecting it.
"""
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "Source" / "FamilyLife"

MUTATIONS = [
    ("uncompleted personal time fulfills independence", "Domain/AgencyBehavior.cs",
     "ticks < 1800 ||", "false ||"),
    ("associates gain management authority", "Domain/HouseRelations.cs",
     "RoleInHouse(person,family)>=HouseRole.Steward", "RoleInHouse(person,family)>=HouseRole.Associate"),
    ("repeated same-day favors farm family standing", "Domain/HouseRelations.cs",
     "if(c.LastPositiveDay>=day) return;", ""),
    ("social admission overwrites biological lineage", "Domain/SocialFamilies.cs",
     "p.SocialFamilyId=family;", "p.SocialFamilyId=family;p.PrimaryLineageId=family;"),

    ("area resolution ignores map identity", "Domain/ContextPolicies.cs",
     "map >= 0 && mapId(candidate) != map", "false"),
    ("ambiguous legacy areas pick an arbitrary home", "Domain/ContextPolicies.cs",
     "if (found != null) return null;", "if (false) return null;"),
    ("unrelated recreation targets become a shared activity", "Domain/ContextPolicies.cs",
     "first > 0 && first == second", "true"),

    ("unwitnessed family members learn the incident", "Domain/FamilyStories.cs",
     "(witnesses ?? Enumerable.Empty<int>()).Append(actor)", "Persons.Select(p => p.Id).Append(actor)"),
    ("family grievance multiplies with each witness", "Domain/FamilyStories.cs",
     "&& !i.FamiliesAffected.Contains(family)", ""),
    ("children can organize revenge", "Domain/FamilyStories.cs",
     "|| p.Juvenile ||", "|| false ||"),
    ("a friend joins a plot against their loved one", "Domain/FamilyStories.cs",
     "targetLove >= 45 ||", "false ||"),
    ("insufficient goods count as a handover", "Domain/FamilySupplies.cs",
     "actualCount < r.Count", "false"),
    ("theft transfers legal ownership", "Domain/FamilySupplies.cs",
     "r.Approach == SupplyApproach.Steal || source != null && source.OwnerFamily != source.HolderFamily ? source.OwnerFamily : receiver", "receiver"),
    ("public collection can confiscate private stock", "Domain/FamilySupplies.cs",
     "if (r.Approach == SupplyApproach.PublicClaim && source != null) return false;", ""),
    ("splitting drops family ownership", "Domain/FamilySupplies.cs",
     "if (claim != null) ClaimStock(result, claim.OwnerFamily, claim.HolderFamily, claim.MapId, claim.Kind);", ""),

    ("discovery runs every active-visit update", "Domain/WorkScheduling.cs",
     "now - last < interval", "false"),
    ("work changes never invalidate the discovery cache", "Domain/WorkScheduling.cs",
     "(!invalidated || now - last < minimum)", "true"),
    ("the same map is always searched first", "Domain/WorkScheduling.cs",
     "next = (start + 1) % count;", "next = start;"),
    ("spatial queries include distant pawns", "Domain/WorkScheduling.cs",
     "if (dx * dx + dz * dz <= squared)", "if (true)"),
    ("observed facts alias the caller's relation lists", "Domain/ObservationCache.cs",
     "new List<int>(values)", "values"),
    ("aging out of childhood is invisible to reconciliation", "Domain/ObservationCache.cs",
     "a.Juvenile == b.Juvenile &&", ""),
    ("performance captures accumulate across restarts", "Domain/PerformanceProbe.cs",
     "metrics.Clear(); startTick = tick;", "startTick = tick;"),
    ("households never dissolve", "Domain/FamilyRegistry.Apply.cs",
     "            DissolveEmpty(now, events);\n        }\n\n        private void PlaceNewResident", "            \n        }\n\n        private void PlaceNewResident"),
    ("lineage is recomputed instead of kept", "Domain/FamilyRegistry.Apply.cs",
     "if (person.PrimaryLineageId != 0) return person.PrimaryLineageId;\n            if (!visiting.Add", "if (!visiting.Add"),
    ("a field is forgotten in the save code", "Persistence/Persist.cs",
     'Scribe_Values.Look(ref LastHouseholdId, "lastHousehold");\n', ""),
    ("a leaving colonist keeps their household", "Domain/FamilyRegistry.Apply.cs",
     "if (p.HouseholdId != 0 && !p.InColony) LeaveHousehold(p, now, events);", "if (false) LeaveHousehold(p, now, events);"),
    ("parents are replaced instead of accumulated", "Domain/FamilyRegistry.Apply.cs",
     "p.ParentIds = Union(p.ParentIds, o.ParentIds);", "p.ParentIds = new List<int>(o.ParentIds);"),
    ("a vanished partner counts as a breakup", "Domain/FamilyRegistry.Apply.cs",
     ".Where(id => person.ExIds.Contains(id)))", ".Where(id => true))"),
    ("grief ignores how close people were", "Domain/FamilyRegistry.Bonds.cs",
     "if (bond.Attachment < BondTuning.GriefMinAttachment) continue;", ""),
    ("the dead grieve too", "Domain/FamilyRegistry.Bonds.cs",
     "if (!personById.TryGetValue(bond.From, out var survivor) || !survivor.InColony) continue;",
     "if (!personById.TryGetValue(bond.From, out var survivor)) continue;"),
    ("bond values are not bounded", "Domain/Bonds.cs",
     "public static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);", "public static float Clamp(float v) => v;"),
    ("bonds toward the dead keep changing", "Domain/FamilyRegistry.Bonds.cs",
     "if (other.Presence == Presence.Dead) { bond.LastTick = now; continue; }", ""),
    ("a long gap is not limited", "Domain/FamilyRegistry.Bonds.cs",
     "Math.Min(BondTuning.MaxHoursPerUpdate, ", "Math.Min(1e9f, "),
    ("opinion is not clamped", "Domain/FamilyRegistry.Bonds.cs",
     "Math.Max(-100, Math.Min(100, value.Value))", "value.Value"),
    # Phase 3
    ("the family-time budget never runs out", "Domain/FamilyActivity.cs",
     "return FamilyTicksToday(personId, now) < dailyTargetTicks;", "return true;"),
    ("the budget does not reset on a new day", "Domain/FamilyActivity.cs",
     "if (t.Day != day) { t.Day = day; t.Ticks = 0; }", "if (t.Day != day) { t.Day = day; }"),
    ("a child is read to all night", "Domain/FamilyActivity.cs",
     "return t == null || now - t.LastStoryTick >= ActivityTuning.StoryIntervalTicks;", "return true;"),
    ("the worst storyteller reads", "Domain/FamilyActivity.cs",
     "candidates.OrderByDescending(r => r.Social).ThenBy(r => r.Id)", "candidates.OrderBy(r => r.Social).ThenBy(r => r.Id)"),
    ("a book teaches no more than no book", "Domain/FamilyActivity.cs",
     "public const float BookEfficiency = 0.5f;", "public const float BookEfficiency = 0.001f;"),
    ("former partners are sought out", "Domain/FamilyActivity.cs",
     "default: return 0f;                     // former partners are not sought out", "default: return 1f;"),
    ("strangers count as family meals", "Domain/FamilyActivity.cs",
     "if (!KindOf(eater, other).HasValue) continue;", ""),
    ("meals in other rooms count", "Domain/FamilyActivity.cs",
     "if (roomKey < 0 || meal.RoomKey != roomKey) continue;", "if (roomKey < 0) continue;"),
    ("time together is unbounded", "Domain/FamilyActivity.cs",
     "bond.Attachment = BondTuning.Clamp(bond.Attachment + ActivityTuning.QualityAttachmentPerHour * hours * helpful * room);", "bond.Attachment = bond.Attachment + ActivityTuning.QualityAttachmentPerHour * hours * helpful * room;"),
    ("time together does not ease tension", "Domain/FamilyActivity.cs",
     "bond.Tension = BondTuning.Clamp(bond.Tension - ActivityTuning.QualityTensionReliefPerHour * hours * fit);", ""),
    ("how long since last together is ignored", "Domain/FamilyActivity.cs",
     "float staleness = Math.Max(0.2f, Math.Min(3f, hoursSince / ActivityTuning.StaleHoursFullDesire));", "float staleness = 1f;"),
    # Phases 4-6
    ("a family takes up traditions it does not value", "Domain/Culture.cs",
     "if (held == null && value >= TraditionCatalog.AdoptAt && people >= TraditionCatalog.MinPeopleInColony)", "if (held == null && people >= TraditionCatalog.MinPeopleInColony)"),
    ("a tradition is never given up", "Domain/Culture.cs",
     "else if (held != null && value < TraditionCatalog.AbandonBelow)", "else if (false)"),
    ("a lone person celebrates traditions", "Domain/Culture.cs",
     "if (people < TraditionCatalog.MinPeopleInColony || (cohesion.HasValue && cohesion.Value < TraditionCatalog.MinCohesion)) continue;", ""),
    ("muting a tradition does nothing", "Domain/Culture.cs",
     "if (t.Muted || now - Math.Max(t.AdoptedTick, t.LastHeldTick)", "if (now - Math.Max(t.AdoptedTick, t.LastHeldTick)"),
    ("a tradition is held too often", "Domain/Culture.cs",
     "< def.IntervalDays * ActivityTuning.TicksPerDay) continue;", "< 0) continue;"),
    ("memories are never trimmed", "Domain/Culture.cs",
     "if (mine.Count > CultureTuning.MaxMemoriesPerLineage)", "if (false)"),
    ("trivial events become memories", "Domain/Culture.cs",
     "if (weight < CultureTuning.MemoryThreshold || GetLineage(lineageId) == null) return false;", "if (GetLineage(lineageId) == null) return false;"),
    ("a bad day opens a rift", "Domain/Conflict.cs",
     "now - b.HighTensionSince >= ConflictTuning.SustainTicks", "now - b.HighTensionSince >= 0"),
    ("an open rift does not hold tension up", "Domain/Conflict.cs",
     "float floor = now - rift.StartTick < ConflictTuning.FloorTicks ? rift.Severity * ConflictTuning.TensionFloorShare : 0f;", "float floor = 0f;"),
    ("a rift never fades by itself", "Domain/Conflict.cs",
     "if (now - rift.CalmSince >= ConflictTuning.CalmTicks) Close(rift, now, true, result);", ""),
    ("closed rifts pile up", "Domain/Conflict.cs",
     "while (closed.Count > ConflictTuning.MaxClosedRifts)", "while (false)"),
    ("making peace does not ease tension", "Domain/Conflict.cs",
     "if (ab != null) { ab.Tension = BondTuning.Clamp(ab.Tension - ConflictTuning.ReconcileTensionRelief);", "if (ab != null) { "),
    ("a steady couple breaks up as often as a failing one", "Domain/Conflict.cs",
     "case CoupleStage.Steady: return 0.15f;", "case CoupleStage.Steady: return 2.5f;"),
    ("a couple in crisis is as steady as any", "Domain/Conflict.cs",
     "if (score >= 25f) return CoupleStage.Distant;\n            return CoupleStage.Crisis;", "if (score >= 25f) return CoupleStage.Distant;\n            return CoupleStage.Steady;"),
    ("affairs happen when switched off", "Domain/Conflict.cs",
     "return enabled && stage >= CoupleStage.Distant", "return stage >= CoupleStage.Distant"),
    ("affairs happen in steady relationships", "Domain/Conflict.cs",
     "return enabled && stage >= CoupleStage.Distant", "return enabled"),
    ("a child is only favoured by a little", "Domain/Conflict.cs",
     "attention[k.Id] <= attention[favoured.Id] * ConflictTuning.NeglectRatio", "attention[k.Id] <= attention[favoured.Id] * 10f"),
    ("coming of age is never noticed", "Domain/FamilyRegistry.Apply.cs",
     "if (!isNew && wasJuvenile && !o.Juvenile && o.Presence == Presence.Colony)", "if (false)"),
    ("anyone back from anywhere is a reunion", "Domain/FamilyRegistry.Apply.cs",
     "if (p.AwayTick >= 0 && now - p.AwayTick >= ActivityTuning.ReunionAwayTicks)", "if (p.AwayTick >= 0)"),
    # Rework: feelings decide closeness
    ("relatives who dislike each other settle as close as any", "Domain/Bonds.cs",
     "return BaseAttachment(kind) * 0.6f + 0.15f * familiarity + 0.5f * opinion;", "return BaseAttachment(kind) * 0.6f + 0.15f * familiarity;"),
    ("time with someone disliked helps as much as with someone liked", "Domain/FamilyActivity.cs",
     "return Math.Max(-0.6f, Math.Min(1.2f, 0.45f + opinion / 55f));", "return 1f;"),
    ("close bonds keep growing as fast as distant ones", "Domain/FamilyActivity.cs",
     "float room = Math.Max(0.1f, 1f - bond.Attachment / 100f) * 1.6f;", "float room = 1f;"),
    ("the last opinion is not remembered", "Domain/FamilyRegistry.Bonds.cs",
     "bond.LastOpinion = opinionValue;", ""),
    ("culture is scaled so ordinary families never reach a value", "Domain/Culture.cs",
     "public const float ProfileScale = 55f;", "public const float ProfileScale = 25f;"),
    # Family tree
    ("a parent is not centred over their children", "Domain/FamilyTree.cs",
     "unit.X = Math.Max(startX, Math.Min(startX + width - unit.Size, center - unit.Size / 2f));", "unit.X = startX;"),
    ("neighbouring branches overlap", "Domain/FamilyTree.cs",
     "cursor += child.Width + TreeTuning.SiblingGap;", "cursor += 0.5f;"),
    ("children are drawn level with their parents", "Domain/FamilyTree.cs",
     "want = Math.Max(want, gen[parent] + 1);", "want = Math.Max(want, gen[parent]);"),
    ("partners are drawn on different levels", "Domain/FamilyTree.cs",
     "foreach (var other in p.PartnerIds().Concat(p.ExIds).Where(shown.Contains)) want = Math.Max(want, gen[other]);", ""),
    ("an old family is never trimmed", "Domain/FamilyTree.cs",
     "if (shown.Count <= TreeTuning.MaxPeople) break;", "if (true) break;"),
    # Step-families and joinings
    ("a former partner is drawn as a current partner", "Domain/FamilyTree.cs",
     "(LinkedAsFormer(a, b) ? TreeLink.Former : TreeLink.None);", "(LinkedAsFormer(a, b) ? TreeLink.Partners : TreeLink.None);"),
    ("children hang from the middle of the unit instead of between their parents", "Domain/FamilyTree.cs",
     ": inside.Average(p => parentUnit.Members.IndexOf(p) + 0.5f);", ": parentUnit.Size / 2f;"),
    ("the other parent is dropped from the tree", "Domain/FamilyTree.cs",
     "foreach (var parent in p.ParentIds) if (personById.ContainsKey(parent)) shown.Add(parent);", ""),
    ("former partners are left out of the tree", "Domain/FamilyTree.cs",
     "foreach (var other in p.PartnerIds().Concat(p.ExIds)) if (personById.ContainsKey(other)) shown.Add(other);", "foreach (var other in p.PartnerIds()) if (personById.ContainsKey(other)) shown.Add(other);"),
    ("half-siblings and step-children are left out of the tree", "Domain/FamilyTree.cs",
     "foreach (var child in ChildrenOf(id)) if (personById.ContainsKey(child)) shown.Add(child);", ""),
    ("every partnership is a joining of families", "Domain/FamilyRegistry.Apply.cs",
     "if (a.PrimaryLineageId != 0 && b.PrimaryLineageId != 0 && a.PrimaryLineageId != b.PrimaryLineageId)", "if (a.PrimaryLineageId != 0 && b.PrimaryLineageId != 0)"),
    ("shared children make a step-family", "Domain/FamilyRegistry.Apply.cs",
     "if (aKids.Any(c => !bKids.Contains(c)) || bKids.Any(c => !aKids.Contains(c)))", "if (aKids.Any() || bKids.Any())"),
    ("a childs own parent counts as a step-parent", "Domain/FamilyTree.cs",
     "partnerId != personId && !person.ParentIds.Contains(partnerId) && personById", "partnerId != personId && personById"),
    ("couples with children are not kept together in a crowded unit", "Domain/FamilyTree.cs",
     "if (w > 0f && ChildrenOf(x).Any(c => ChildrenOf(y).Contains(c))) w += 2f;", ""),
    ("present partners are not drawn to the right", "Domain/FamilyTree.cs",
     "for (int i = 0; i < order.Count; i++) side += i * partnered[order[i]];", ""),
    ("a founder and their spouse are listed as a family of their own", "Domain/FamilyTree.cs",
     "if (p.PrimaryLineageId == lineageId && ++byDescent >= 2) return true;", "if (p.PrimaryLineageId == lineageId && ++byDescent >= 1) return true;"),
    ("someone is always shown their own tiny family", "Domain/FamilyTree.cs",
     "if (own != 0 && IsListedFamily(own)) return own;", "if (own != 0) return own;"),
    # Performance guards
    ("the attempt table is swept on every call", "Domain/Throttle.cs",
     "(!everPruned || now < lastPrune || now - lastPrune >= PruneEveryTicks)", "true"),
    ("the attempt table is never cleared", "Domain/Throttle.cs",
     "if (now < kv.Value || now - kv.Value > StaleAfterTicks) old.Add(kv.Key);", ""),
    ("an attempt never has to wait", "Domain/Throttle.cs",
     "if (last.TryGetValue(Key(id, channel), out var at) && now >= at && now - at < cooldownTicks) return false;", ""),
    ("the line to a married child comes to the couple instead of the child", "Domain/FamilyTree.cs",
     "unit.ChildMember = Math.Max(0, unit.Members.IndexOf(childMember));", "unit.ChildMember = 0;"),
    ("closeness ignores how the other person feels", "Domain/Insight.cs",
     "if (result.ToFocus != null) values.Add(result.ToFocus.Attachment);", ""),
    ("closeness is the larger of the two directions", "Domain/Insight.cs",
     "if (values.Count > 0) result.Value = values.Average();", "if (values.Count > 0) result.Value = values.Max();"),
    # Starting families
    ("a family is married to a stranger", "Domain/StartingFamilies.cs",
     "foreach (var family in people.Where(p => !string.IsNullOrEmpty(p.LastName)).GroupBy(p => p.LastName))", "foreach (var family in new[] { people.Where(p => !string.IsNullOrEmpty(p.LastName)) })"),
    ("a family without a mother still marries someone", "Domain/StartingFamilies.cs",
     "if (parents.Count == 2) plan.Spouses.Add", "if (parents.Count >= 1) plan.Spouses.Add"),
    ("children are given only one parent", "Domain/StartingFamilies.cs",
     "foreach (var parent in parents)\n                        plan.ParentOf.Add", "foreach (var parent in parents.Take(1))\n                        plan.ParentOf.Add"),
    ("two families can get the same surname", "Domain/StartingFamilies.cs",
     "return Surnames.OrderBy(_ => rng.Next()).Take(Math.Max(0, Math.Min(count, Surnames.Length))).ToList();", "return Enumerable.Repeat(Surnames[rng.Next(Surnames.Length)], Math.Max(0, count)).ToList();"),
    # Family zones
    ("someone already given the zone is given it again", "Domain/Zones.cs",
     "&& !lineage.ZoneApplied.Contains(p.Id))", ")"),
    ("choosing another zone leaves everyone on the old one", "Domain/Zones.cs",
     "lineage.ZoneApplied = new List<int>();\n        }", "}"),
    ("people who are away are given the zone", "Domain/Zones.cs",
     "Persons.Where(p => p.InColony && (p.PrimaryLineageId", "Persons.Where(p => (p.PrimaryLineageId"),
    # Insight and culture
    ("every bond change is announced", "Domain/Insight.cs",
     "if (!bond.TierSeen)\n                {", "if (false)\n                {"),
    ("a bond that merely started low is announced as cooling", "Domain/Insight.cs",
     "&& KindOf(holder, other).HasValue && bond.TopReason(false) != null)", "&& KindOf(holder, other).HasValue)"),
    ("a bond is announced as closer with no reason", "Domain/Insight.cs",
     "bond.Attachment >= InsightTuning.CloseAt && bond.TopReason(true) != null", "bond.Attachment >= InsightTuning.CloseAt"),
    ("milestones are not rate limited", "Domain/Insight.cs",
     "if (events.Count >= InsightTuning.MaxMilestonesPerUpdate) continue;", ""),
    ("the same milestone repeats every hour", "Domain/Insight.cs",
     "if (bond.LastMilestoneTick > 0 && now - bond.LastMilestoneTick < InsightTuning.MilestoneCooldownTicks) continue;", ""),
    ("causes pile up without limit", "Domain/Insight.cs",
     "while (Reasons.Count > MaxReasons)", "while (false)"),
    ("culture does not change how much teaching happens", "Domain/Insight.cs",
     "return Math.Max(0.1f, Math.Min(0.9f, 0.35f + 0.35f * lean));", "return 0.5f;"),
    ("culture does not change how often partners go out", "Domain/Insight.cs",
     "return Math.Max(0.15f, Math.Min(0.9f, 0.45f + 0.3f * lean));", "return 0.5f;"),
    ("appetite for family time is the same for everyone", "Domain/Insight.cs",
     "return Math.Max(0.5f, Math.Min(1.5f, family * personal));", "return 1f;"),
    ("appetite does not change who is sought out", "Domain/FamilyActivity.cs",
     "* (1f - 0.8f * tension / 100f) * appetite;", "* (1f - 0.8f * tension / 100f);"),
    ("clashing characters cause no friction", "Domain/Insight.cs",
     "total += Math.Max(0f, gap - 1.0f) * 6f;", ""),
    ("being the odd one out of the family's values costs nothing", "Domain/Insight.cs",
     "&& Math.Sign(mine) != Math.Sign(family)) total += 4f;", "&& Math.Sign(mine) != Math.Sign(family)) total += 0f;"),
    # Relationship events
    ("gains do not shrink as a bond grows", "Domain/Events/EventEngine.cs",
     "private static float Room(float v) => Math.Max(0.15f, 1f - v / 100f) * 1.5f;", "private static float Room(float v) => 1f;"),
    ("repeating an event pays the same each time", "Domain/Events/EventEngine.cs",
     "return n == 0 ? 1f : (n == 1 ? 0.6f : (n == 2 ? 0.35f : 0.2f));", "return 1f;"),
    ("a poor moment opens a rift at any tension", "Domain/Events/EventEngine.cs",
     "&& (ab?.Tension ?? 0f) >= EventTuning.RiftOpensAtTension &&", "&& true &&"),
    ("one conversation closes a neglect rift", "Domain/Events/EventEngine.cs",
     "bool bySelf = c.Rift.Cause == RiftCause.Neglect || c.Rift.Cause == RiftCause.Rivalry;", "bool bySelf = false;"),
    ("a promise is made on any outcome", "Domain/Events/EventEngine.cs",
     "if ((d.Effects & EventEffect.Promise) != 0 && r.Tier >= EventTier.Good)", "if ((d.Effects & EventEffect.Promise) != 0)"),
    ("a promise that goes unkept is never broken", "Domain/Events/EventEngine.cs",
     "p.State = 2;\n                broken.Add(p);", "broken.Add(p);"),
    ("spending the time does not keep a promise", "Domain/FamilyActivity.cs",
     "            KeepPromises(a, b, ticks, now);\n", ""),
    ("an event has no cooldown", "Domain/Events/EventEngine.cs",
     "if (since < d.CooldownDays)", "if (false)"),
    ("family moments happen between strangers", "Domain/Events/EventEngine.cs",
     "case EventBucket.IntraFamily: return family;", "case EventBucket.IntraFamily: return true;"),
    ("an event that needs a third person does not", "Domain/Events/EventEngine.cs",
     "if (d.NeedsThird && c.Third == null)", "if (false)"),
    ("personal moments move whole families", "Domain/Events/EventEngine.cs",
     "&& (d.Effects & (EventEffect.TiesFamilies | EventEffect.Incident | EventEffect.Bridge)) != 0)", ")"),
    ("every outcome becomes a defining memory", "Domain/Events/EventEngine.cs",
     "&& (r.Tier == EventTier.Great || r.Tier == EventTier.Poor);", "&& true;"),
    ("children do no more to a feud than adults", "Domain/Events/EventEngine.cs",
     "float bridge = (d.Effects & EventEffect.Bridge) != 0 ? 1.5f : 1f;", "float bridge = 1f;"),
    ("child events happen without children being supported", "Domain/Events/EventEngine.cs",
     "if (d.NeedsChild && !childrenEnabled)", "if (false)"),
    ("the lesson roles are reversed", "Domain/Events/EventEngine.cs",
     "var teacher = d.RecipientTeaches ? c.Recipient : c.Initiator;", "var teacher = d.RecipientTeaches ? c.Initiator : c.Recipient;"),
    ("duplicate contexts add selection weight", "Domain/Events/EventEngine.cs",
     "if (seen.Add(key)) pool.Add", "pool.Add"),
    ("family effects erase apprenticeship teaching", "Domain/Events/EventCatalog.cs",
     "D.Effects |= fx;", "D.Effects = fx;"),
    ("pair events are duplicated for every witness", "Domain/Events/EventEngine.cs",
     "defs.Where(d => d.NeedsThird == (c.Third != null))", "defs"),
    ("the recipient daily limit is ignored", "Domain/Events/EventEngine.cs",
     "EventsTodayFor(c.Recipient.Id, c.Now) >= EventTuning.MaxPerPersonPerDay", "false"),
    ("disabled skills can be selected for lessons", "Domain/Events/EventEngine.cs",
     "if (!learner.Skills.ContainsKey(kv.Key)) continue;", ""),
    ("confronting someone costs nothing when it goes badly", "Domain/Events/EventEngine.cs",
     "float heat = r.Tier == EventTier.Poor ? 8f :", "float heat = r.Tier == EventTier.Poor ? 1f :"),
    ("ambient moments use up the daily allowance", "Domain/Events/EventEngine.cs",
     "if (e.Weight != EventWeight.Ambient && (e.A == personId || e.B == personId)) n++;", "if (e.A == personId || e.B == personId) n++;"),
    ("every outcome is a good one", "Domain/Events/EventEngine.cs",
     "public const float GoodAt = 0.52f;", "public const float GoodAt = 0.0f;"),
    ("what happened is not recorded", "Domain/Events/EventEngine.cs",
     "EventLog.Add(new EventRecord { EventId = d.Id,", "var unused = (new EventRecord { EventId = d.Id,"),
    ("a strained family tie never eases", "Domain/Events/EventEngine.cs",
     "t.Grievance = Math.Max(0f, t.Grievance - EventTuning.TieGrievanceEasePerDay * days);", ""),
    ("a poor reception does not depend on trust", "Domain/Events/EventCatalog.cs",
     "Factor.RecipientTrust, 0.25f, Factor.RecipientAttachment, 0.15f,", "Factor.RecipientTrust, 0.0f, Factor.RecipientAttachment, 0.15f,"),
]


def run_tests():
    out = subprocess.run(["dotnet", "test", str(ROOT / "Source" / "FamilyLife.Tests" / "FamilyLife.Tests.csproj"), "--no-restore"],
                         capture_output=True, text=True, timeout=600).stdout
    m = re.search(r"(Failed!|Passed!)\s+-\s+Failed:\s+(\d+), Passed:\s+(\d+)", out)
    return (m.group(1) == "Failed!", int(m.group(2)), int(m.group(3))) if m else (None, 0, 0)


def main():
    missed = 0
    only = sys.argv[sys.argv.index('--only') + 1] if '--only' in sys.argv else None      # e.g. --only Events/ runs just those files' mutations
    for name, rel, old, new in MUTATIONS:
        if only and only not in rel:
            continue
        path = SRC / rel
        original = path.read_text(encoding="utf-8")
        if old not in original:
            print(f"SKIP   {name}: pattern not found (the code changed; update this script)")
            missed += 1
            continue
        path.write_text(original.replace(old, new, 1), encoding="utf-8")
        try:
            failed, nfail, npass = run_tests()
        finally:
            path.write_text(original, encoding="utf-8")
        if failed is None:
            print(f"BUILD  {name}: the mutated code did not compile, so this proves nothing")
            missed += 1
        elif failed:
            print(f"caught {name}: {nfail} test(s) failed")
        else:
            print(f"MISSED {name}: all {npass} tests still pass")
            missed += 1
    ok, nfail, npass = run_tests()
    print(f"restored: {'green' if not ok else 'RED'} ({npass} passed, {nfail} failed)")
    return 1 if missed or ok else 0


if __name__ == "__main__":
    sys.exit(main())
