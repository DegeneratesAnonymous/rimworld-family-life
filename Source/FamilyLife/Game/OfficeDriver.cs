using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace FamilyLife
{

    /// <summary>
    /// The game side of the two community posts. The teacher holds a class every few days (the player chooses how often, and whether adults may
    /// join); the healer gives every colonist a check-up a couple of times a year in the medical room. Credit comes only from things the game saw:
    /// a lesson actually given, an examination actually done, a patient actually tended. Nothing here changes a work priority.
    /// </summary>
    internal static partial class OfficeDriver
    {
        private const int Day = ActivityTuning.TicksPerDay;
        private static readonly Dictionary<string, int> lastAct = new Dictionary<string, int>();

        internal static OfficeContext Context(FamilyComponent c) => new OfficeContext
        {
            Skill = (id, kind) => SkillFor(c.TryGetPawn(id), kind),
            Free = id => Governance.Free(c, id),
            Player = id => Governance.IsAvatar(c, id),
            Passion = (id, kind) => PassionFor(c.TryGetPawn(id), kind)
        };

        /// <summary>Whether the pawn burns for the skills the post uses: a calling, which is the clearest reason to want it.</summary>
        internal static bool PassionFor(Pawn p, OfficeKind kind)
        {
            if (p?.skills == null) return false;
            bool Burns(SkillDef def) { var s = p.skills.GetSkill(def); return s != null && !s.TotallyDisabled && s.passion != Passion.None; }
            switch (kind)
            {
                case OfficeKind.Healer: return Burns(SkillDefOf.Medicine);
                case OfficeKind.Teacher: return Burns(SkillDefOf.Intellectual) || Burns(SkillDefOf.Social);
                case OfficeKind.HallKeeper: return Burns(SkillDefOf.Cooking) || Burns(SkillDefOf.Social);
                default: return false;
            }
        }

        /// <summary>
        /// Once a day: the holder feels how the post is going (trusted when they do it well, let down when they do not), and the rest of their house feels
        /// pride in kin who holds a post worth having. These are the pawn's and the family's own stake in a post, not a penalty for an empty one.
        /// </summary>
        private static void Esteem(FamilyComponent c, FamilyRegistry r, int now)
        {
            if (!Due("esteem", now, Day)) return;
            foreach (var office in r.Offices.Where(o => o.Held).ToList())
            {
                var holder = Spawned(c, office.HolderId);
                if (holder == null) continue;
                float grade = r.OfficeGrade(office.Kind);
                bool appointed = FamilyRegistry.IsAppointed(office.Kind);
                bool neglecting = office.UnmetSinceTick != 0 && now - office.UnmetSinceTick > 3 * Day;       // work was needed and is not being done
                string mood = appointed || grade >= .65f ? "WFL_PostTrusted" : grade >= .35f || !neglecting && office.Work == 0 ? "WFL_PostAdequate" : "WFL_PostFailing";
                Activities.GainMemory(holder, mood, null);
                if (!appointed && grade < .5f && office.Work > 0) continue;
                foreach (var kin in r.MembersOf(office.HolderFamily).Where(m => m.InColony && m.Id != office.HolderId).Take(12))
                    if (Spawned(c, kin.Id) is Pawn k) Activities.GainMemory(k, "WFL_KinInPost", holder);
            }
        }

        /// <summary>The person served by one of their own house's post-holders feels it, and the holder earns the trust of anyone they serve.</summary>
        internal static void Served(FamilyRegistry r, OfficeKind kind, Pawn holder, Pawn served, int now)
        {
            // The relationship is changed in the exchanges themselves (FamilyRegistry.ProfessionalExchange, limited per pair per day); crediting it again here would count it twice.
            if (r.KinOfHolder(served.thingIDNumber, kind)) Activities.GainMemory(served, "WFL_ServedByKin", holder);
        }

        /// <summary>0 to 1. A pawn we cannot see, or who cannot do the kind of work, is not fit.</summary>
        internal static float SkillFor(Pawn p, OfficeKind kind)
        {
            if (p == null || p.skills == null || p.Dead) return 0f;
            float S(SkillDef def) { var s = p.skills.GetSkill(def); return s == null || s.TotallyDisabled ? 0f : Math.Min(1f, s.Level / 20f); }
            if (kind == OfficeKind.Healer) return p.WorkTypeIsDisabled(WorkTypeDefOf.Doctor) ? 0f : S(SkillDefOf.Medicine);
            return p.WorkTagIsDisabled(WorkTags.Social) ? 0f : .6f * S(SkillDefOf.Intellectual) + .4f * S(SkillDefOf.Social);
        }

        internal static void Run(FamilyComponent c, int now)
        {
            var r = c.Registry;
            foreach (var office in r.ElectOffices(now, Context(c)).Where(o => o.Held))
            {
                var pawn = c.TryGetPawn(office.HolderId);
                if (pawn != null) Messages.Message("WFL_Office_Elected".Translate(pawn.LabelShort, ("WFL_OfficeKind_" + office.Kind).Translate()), pawn, MessageTypeDefOf.PositiveEvent, false);
            }
            SyncChaplain(c, r, now);
            r.MaintainOffices(now);
            Esteem(c, r, now);
            AssemblyDriver.Run(c, r, now);
            // each module's post work runs from the one shared roster, and not at all while that module is off
            FamilyModules.Roster.RunDue(now, 0, (item, ex) => Log.Warning("[Wil's Family Life] Community work '" + item.Id + "' skipped: " + ex.Message));
        }

        /// <summary>The chaplain is the ideoligion's moral guide, the one who converts and speaks for the faith. The post simply follows the game.</summary>
        private static void SyncChaplain(FamilyComponent c, FamilyRegistry r, int now)
        {
            int person = 0;
            try
            {
                if (ModsConfig.IdeologyActive)
                {
                    var ideo = Faction.OfPlayer.ideos?.PrimaryIdeo;
                    var guide = ideo?.RolesListForReading.FirstOrDefault(x => x.def == PreceptDefOf.IdeoRole_Moralist)?.ChosenPawnSingle();
                    if (guide != null && guide.Faction == Faction.OfPlayer && !guide.Dead) person = guide.thingIDNumber;
                }
            }
            catch (Exception ex) { Log.Warning("[Wil's Family Life] Could not read the moral guide: " + ex.Message); }
            if (r.AppointOffice(OfficeKind.Chaplain, person, now) && person != 0 && c.TryGetPawn(person) is Pawn p)
                Messages.Message("WFL_Chaplain_Named".Translate(p.LabelShort), p, MessageTypeDefOf.NeutralEvent, false);
        }

        private static bool Due(string key, int now, int gap)
        {
            if (lastAct.TryGetValue(key, out int last) && now >= last && now - last < gap) return false;
            lastAct[key] = now;
            return true;
        }

        private static Pawn Spawned(FamilyComponent c, int id) { var p = c.TryGetPawn(id); return p != null && p.Spawned && !p.Dead ? p : null; }

        internal static void Work(FamilyComponent c, FamilyRegistry r, CommunityOffice office, int now)
        {
            var provider = FamilyModules.WorkFor(office.Kind);          // null when the module that defines this post is absent or off
            if (provider == null) return;
            try { provider(c, r, office, Spawned(c, office.HolderId), FamilyLifeMod.Settings, now); }
            catch (Exception ex) { Log.Warning("[Wil's Family Life] Community post '" + office.Kind + "' skipped: " + ex.Message); }
        }

        internal static void HallWork(FamilyComponent c, FamilyRegistry r, CommunityOffice office, Pawn holder, FamilySettings s, int now) =>
            HallDriver.Work(c, r, office, holder, s, now, (key, n, gap) => Due(key, n, gap));

        /// <summary>Set by the developer menu so a test is not held up by who happens to be busy: people only need to be undrafted and not in a mental break.</summary>
        internal static int ForceUntil;
        internal static bool Ready(Pawn p) => p != null && (Activities.IsFree(p) || (Activities.Now < ForceUntil && p.Spawned && !p.Dead && !p.Downed && !p.Drafted && !p.InMentalState && !p.IsPrisoner && p.RaceProps.Humanlike && !p.DevelopmentalStage.Baby()));

        internal static bool Forced => Activities.Now < ForceUntil;

        /// <summary>Awake enough and able: not drafted, hurt, in a break or a prisoner. Used when the player asks for something, which does not wait for the pawn to be idle.</summary>
        internal static bool Available(Pawn p) => p != null && p.Spawned && !p.Dead && !p.Downed && !p.Drafted && !p.InMentalState && !p.IsPrisoner && p.RaceProps.Humanlike && !p.DevelopmentalStage.Baby() && p.Awake();

        private static readonly string[] UrgentNeeds = { "Food", "Rest", "Bladder", "Hygiene" };

        /// <summary>
        /// Someone who should stop what they are doing: hungry, tired, in need of the toilet or a wash, or called away into a gathering. A check-up or a class stops for
        /// them and is taken up again afterwards, rather than holding them to it.
        /// </summary>
        internal static bool NeedsAttention(Pawn p) => NeedsReason(p) != null;

        /// <summary>Why this person should stop (a need, or an event they have been called to), or null.</summary>
        internal static string NeedsReason(Pawn p)
        {
            if (p == null || p.Dead) return null;
            try
            {
                var needs = p.needs?.AllNeeds;
                if (needs != null)
                    for (int i = 0; i < needs.Count; i++)
                    {
                        var n = needs[i];
                        if (n?.def != null && UrgentNeeds.Contains(n.def.defName) && n.CurLevelPercentage < .15f) return n.def.defName + " " + n.CurLevelPercentage.ToString("0.00");
                    }
                var lord = p.GetLord();
                if (lord?.LordJob is LordJob_VoluntarilyJoinable) return "called to an event";
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>Written to the log during a developer test only, so a flow that does not start says why.</summary>
        internal static void Trace(string text) { if (Forced) Log.Message("[Wil's Family Life] test: " + text); }

        /// <summary>What stands between the doctor and a clinic, and the teacher and a class, in plain lines. Each module contributes its own section. Used by the developer menu.</summary>
        internal static List<string> Diagnose(FamilyComponent c, int now, OfficeKind? only = null)
        {
            var lines = new List<string>();
            foreach (var section in FamilyModules.Diagnostics.Enabled())
                if (only == null || only == section.Kind) lines.AddRange(section.Lines(c, now));
            return lines;
        }

        /// <summary>Runs the posts' work now, rather than at the next check.</summary>
        internal static void WorkNow(FamilyComponent c, int now, OfficeKind? only = null)
        {
            foreach (var office in c.Registry.Offices.Where(o => o.Held && (only == null || o.Kind == only)).ToList())
            {
                try { Work(c, c.Registry, office, now); }
                catch (Exception ex) { Log.Warning("[Wil's Family Life] test: post '" + office.Kind + "' failed: " + ex); }
            }
        }
        /// <summary>Not drafted, hurt or in a break; and, outside a developer test, not being played or selected.</summary>
        internal static bool FreeFor(FamilyComponent c, int id)
        {
            var p = c.TryGetPawn(id);
            if (Forced && p != null) return !p.Drafted && !p.Downed && !p.InMentalState && !p.IsPrisoner;
            return Governance.Free(c, id);
        }

        /// <summary>A lesson lasts two game hours; a check-up a few seconds at triple speed.</summary>
        internal const int LessonTicks = 5000, CheckupTicks = 900;

    }
}
