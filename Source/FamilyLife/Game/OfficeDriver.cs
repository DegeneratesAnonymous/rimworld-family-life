using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// The game side of the community posts: who is fit, who can vote, and the real work that counts. Credit comes only from things the game saw happen:
    /// a patient tended (the pawn's own record), a lesson actually given, a supply request the supply system finished, a quarrel actually mediated, or
    /// a hearing actually held. Nothing here changes a work priority or gives anyone a stat.
    /// </summary>
    internal static class OfficeDriver
    {
        private const int Day = ActivityTuning.TicksPerDay;
        private static readonly Dictionary<OfficeKind, int> lastAct = new Dictionary<OfficeKind, int>();

        internal static OfficeContext Context(FamilyComponent c) => new OfficeContext
        {
            Skill = (id, kind) => SkillFor(c.TryGetPawn(id), kind),
            Free = id => Governance.Free(c, id),
            Player = id => Governance.IsAvatar(c, id)
        };

        /// <summary>0 to 1. A pawn we cannot see, or who cannot do the kind of work, is not fit.</summary>
        internal static float SkillFor(Pawn p, OfficeKind kind)
        {
            if (p == null || p.skills == null || p.Dead) return 0f;
            float S(SkillDef def) { var s = p.skills.GetSkill(def); return s == null || s.TotallyDisabled ? 0f : Math.Min(1f, s.Level / 20f); }
            bool social = !p.WorkTagIsDisabled(WorkTags.Social);
            switch (kind)
            {
                case OfficeKind.Healer: return p.WorkTypeIsDisabled(WorkTypeDefOf.Doctor) ? 0f : S(SkillDefOf.Medicine);
                case OfficeKind.Teacher: return social ? .6f * S(SkillDefOf.Intellectual) + .4f * S(SkillDefOf.Social) : 0f;
                case OfficeKind.Quartermaster: return social ? .5f * S(SkillDefOf.Social) + .5f * S(SkillDefOf.Crafting) : 0f;
                case OfficeKind.Mediator: return social ? S(SkillDefOf.Social) : 0f;
                default: return social ? .5f * S(SkillDefOf.Intellectual) + .5f * S(SkillDefOf.Social) : 0f;
            }
        }

        internal static void Run(FamilyComponent c, int now)
        {
            var r = c.Registry;
            foreach (var office in r.ElectOffices(now, Context(c)).Where(o => o.Held))
            {
                var pawn = c.TryGetPawn(office.HolderId);
                if (pawn != null) Messages.Message("WFL_Office_Elected".Translate(pawn.LabelShort, ("WFL_OfficeKind_" + office.Kind).Translate()), pawn, MessageTypeDefOf.PositiveEvent, false);
            }
            r.MaintainOffices(now);
            foreach (var office in r.Offices.Where(o => o.Held).ToList())
            {
                try { Work(c, r, office, now); }
                catch (Exception ex) { Log.Warning("[Wil's Family Life] Community post '" + office.Kind + "' skipped: " + ex.Message); }
            }
        }

        private static bool Due(OfficeKind kind, int now)
        {
            if (lastAct.TryGetValue(kind, out int last) && now >= last && now - last < Day / 2) return false;
            lastAct[kind] = now;
            return true;
        }

        private static Pawn Spawned(FamilyComponent c, int id) { var p = c.TryGetPawn(id); return p != null && p.Spawned && !p.Dead ? p : null; }

        private static void Work(FamilyComponent c, FamilyRegistry r, CommunityOffice office, int now)
        {
            int holderId = office.HolderId;
            var holder = Spawned(c, holderId);
            switch (office.Kind)
            {
                case OfficeKind.Healer:
                {
                    if (holder?.records == null) return;
                    int tended = (int)holder.records.GetValue(RecordDefOf.TimesTendedOther);
                    if (!office.CounterReady) { office.Counter = tended; office.CounterReady = true; }
                    else if (tended > office.Counter)
                    {
                        int delta = Math.Min(3, tended - office.Counter); office.Counter = tended;
                        for (int i = 0; i < delta; i++) r.RecordOfficeWork(OfficeKind.Healer, holderId, GovernanceMeetings.NextToken(), now);
                    }
                    bool needed = holder.Map != null && holder.Map.mapPawns.FreeColonistsSpawned.Take(80).Any(p => HealthAIUtility.ShouldBeTendedNowByPlayer(p));
                    r.NoteOfficeNeed(OfficeKind.Healer, needed, now);
                    break;
                }
                case OfficeKind.Teacher:
                {
                    var children = r.Persons.Where(p => p.InColony && p.Juvenile && p.Id != holderId).Take(20).ToList();
                    r.NoteOfficeNeed(OfficeKind.Teacher, children.Count > 0, now);
                    if (holder == null || children.Count == 0 || !Due(OfficeKind.Teacher, now)) return;
                    foreach (var child in children.Select(k => Spawned(c, k.Id)).Where(k => k != null && k.Map == holder.Map).OrderBy(k => k.thingIDNumber))
                        if (GovernanceMeetings.Start(holder, child, WFL_StoryDefOf.WFL_OfficeLesson, 0)) break;
                    break;
                }
                case OfficeKind.Quartermaster:
                {
                    foreach (var req in r.Requests.Skip(Math.Max(0, r.Requests.Count - 60)))
                        if (req.CreatedTick >= office.ElectedTick && (req.State == SupplyState.Exchanged || req.State == SupplyState.Stored || req.State == SupplyState.Repaid) &&
                            (req.DonorId == holderId || req.RequesterId == holderId))
                            r.RecordOfficeWork(OfficeKind.Quartermaster, holderId, req.Id, now);
                    r.NoteOfficeNeed(OfficeKind.Quartermaster, r.Requests.Any(q => q.Open), now);
                    break;
                }
                case OfficeKind.Mediator:
                {
                    var rift = r.Rifts.Where(x => x.IsOpen && x.A != holderId && x.B != holderId && r.GetPerson(x.A)?.InColony == true && r.GetPerson(x.B)?.InColony == true)
                        .OrderByDescending(x => x.Severity).ThenBy(x => x.Id).FirstOrDefault();
                    r.NoteOfficeNeed(OfficeKind.Mediator, rift != null, now);
                    if (holder == null || rift == null || !Due(OfficeKind.Mediator, now)) return;
                    var party = Spawned(c, rift.A) ?? Spawned(c, rift.B);
                    if (party != null && party.Map == holder.Map) GovernanceMeetings.Start(holder, party, WFL_StoryDefOf.WFL_StewardMediate, rift.Id);
                    break;
                }
                default:
                {
                    var accusation = r.NextAccusationFor(holderId);
                    r.NoteOfficeNeed(OfficeKind.Judge, accusation != null, now);
                    if (holder == null || accusation == null || !Due(OfficeKind.Judge, now)) return;
                    var accused = Spawned(c, accusation.ActorId);
                    if (accused != null && accused.Map == holder.Map) GovernanceMeetings.Start(holder, accused, WFL_StoryDefOf.WFL_JudgeHearing, accusation.Id);
                    break;
                }
            }
        }

        /// <summary>A lesson from the colony's teacher: a real conversation job that ends with the child learning something the teacher is better at.</summary>
        internal static void CompleteLesson(FamilyRegistry r, Pawn teacher, Pawn child, int now)
        {
            if (teacher?.skills == null || child?.skills == null) return;
            SkillDef best = null; int gap = 2;
            foreach (var skill in DefDatabase<SkillDef>.AllDefsListForReading)
            {
                var t = teacher.skills.GetSkill(skill); var k = child.skills.GetSkill(skill);
                if (t == null || k == null || t.TotallyDisabled || k.TotallyDisabled || t.Level - k.Level <= gap) continue;
                gap = t.Level - k.Level; best = skill;
            }
            if (best != null) child.skills.Learn(best, 250f, true);
            r.RecordTogether(teacher.thingIDNumber, child.thingIDNumber, 1800, now, BondCause.Teaching);
            r.RecordOfficeWork(OfficeKind.Teacher, teacher.thingIDNumber, GovernanceMeetings.NextToken(), now);
        }

        /// <summary>A hearing held in person. Only an accusation the judge knows first-hand can be upheld.</summary>
        internal static void CompleteHearing(FamilyRegistry r, Pawn judge, Pawn accused, int incidentId, int now)
        {
            var verdict = r.HearAccusation(judge.thingIDNumber, incidentId, now);
            if (verdict != Verdict.Upheld) return;
            r.RecordOfficeWork(OfficeKind.Judge, judge.thingIDNumber, GovernanceMeetings.NextToken(), now);
            Messages.Message("WFL_Judge_Upheld".Translate(judge.LabelShort, accused.LabelShort), judge, MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
