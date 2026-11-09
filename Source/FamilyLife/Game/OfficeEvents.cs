using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace FamilyLife
{
    /// <summary>A neutral event the player can begin or pass on: the doctor's check-ups, a round called for an outbreak, and the annual testing of the children.</summary>
    public class ChoiceLetter_OfficeEvent : ChoiceLetter
    {
        public int kind;

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                yield return new DiaOption("WFL_Event_Begin".Translate()) { action = () => { OfficeEvents.Begin(kind); Find.LetterStack.RemoveLetter(this); }, resolveTree = true };
                yield return new DiaOption("WFL_Event_Pass".Translate()) { action = () => { OfficeEvents.Pass(kind); Find.LetterStack.RemoveLetter(this); }, resolveTree = true };
                yield return Option_Close;          // decide later: left alone it goes ahead by itself after three days
            }
        }

        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref kind, "wflEventKind"); }
    }

    /// <summary>
    /// The check-up round, the round called for an outbreak and the annual testing. Each is offered as a letter; the player begins it, passes on it, or leaves it, in which
    /// case it begins by itself after three days so the posts keep working for people who never answer. The state is kept on the post, so it is saved.
    /// </summary>
    internal static class OfficeEvents
    {
        private const int Day = ActivityTuning.TicksPerDay;
        private const int AutoBegin = 3 * Day, IllnessGap = 10 * Day;
        private static LetterDef letterDef;
        private static LetterDef LetterKind => letterDef ?? (letterDef = DefDatabase<LetterDef>.GetNamedSilentFail("WFL_OfficeEvent") ?? LetterDefOf.NeutralEvent);

        // ------------------------------------------------------------------ reading the colony

        internal static float Longitude(Map m) => Find.WorldGrid.LongLatOf(m.Tile).x;
        internal static bool IsAprimay(Map m) => GenDate.Quadrum(Find.TickManager.TicksAbs, Longitude(m)) == Quadrum.Aprimay;
        internal static int Year(Map m) => GenDate.Year(Find.TickManager.TicksAbs, Longitude(m));

        /// <summary>Down with an illness (not an injury): something that gives them the sick thought.</summary>
        internal static bool IsSick(Pawn p) => p?.health?.hediffSet?.hediffs != null && p.health.hediffSet.hediffs.Any(h => h.def.makesSickThought && h.Visible);

        /// <summary>A quarter of the colony, and at least three, ill at once: an outbreak or a plague.</summary>
        internal static bool MassIllness(Map map)
        {
            if (map == null) return false;
            var people = map.mapPawns.FreeColonistsSpawned;
            int sick = people.Count(IsSick);
            return sick >= Math.Max(3, (int)Math.Ceiling(people.Count * .25));
        }

        private static int Interval()
        {
            var s = FamilyLifeMod.Settings;
            return 60 * Day / Math.Max(1, Math.Min(4, s?.checkupsPerYear ?? 2));
        }

        private static CommunityOffice Office(OfficeKind kind) => FamilyComponent.Current?.Registry.OfficeFor(kind);
        private static CommunityOffice OfficeFor(int eventKind) => Office(eventKind == PendingEvent.Testing ? OfficeKind.Teacher : OfficeKind.Healer);

        /// <summary>In an outbreak the sick are seen first.</summary>
        internal static Func<int, int> Priority(FamilyComponent c, CommunityOffice o) => o != null && o.IllnessRound ? (id => IsSick(c.TryGetPawn(id)) ? 0 : 1) : (Func<int, int>)null;

        // ------------------------------------------------------------------ offering

        private static void Offer(int kind, Pawn holder, int now)
        {
            var o = OfficeFor(kind); if (o == null || !o.Held) return;
            o.PendingKind = kind; o.PendingTick = now;
            string key = kind == PendingEvent.Testing ? "Testing" : kind == PendingEvent.Illness ? "Illness" : "Checkups";
            var letter = (ChoiceLetter_OfficeEvent)LetterMaker.MakeLetter(("WFL_Event_" + key + "_Title").Translate(), ("WFL_Event_" + key + "_Text").Translate(holder.LabelShort), LetterKind, holder);
            letter.kind = kind;
            Find.LetterStack.ReceiveLetter(letter);
        }

        internal static void Offer(int kind) { var c = FamilyComponent.Current; var o = OfficeFor(kind); var p = o != null && o.Held ? c?.TryGetPawn(o.HolderId) : null; if (p != null && p.Spawned) Offer(kind, p, Activities.Now); }

        // ------------------------------------------------------------------ beginning and passing

        internal static bool Begin(int kind)
        {
            var c = FamilyComponent.Current; var r = c?.Registry; if (r == null) return false;
            int now = Activities.Now;
            var o = OfficeFor(kind);
            if (o == null || !o.Held) { Messages.Message(("WFL_Event_NoPost_" + (kind == PendingEvent.Testing ? "Teacher" : "Doctor")).Translate(), MessageTypeDefOf.RejectInput, false); return false; }
            var holder = c.TryGetPawn(o.HolderId);
            if (holder == null || !holder.Spawned) { o.PendingKind = 0; return false; }
            o.PendingKind = 0;
            if (kind == PendingEvent.Testing) return BeginTesting(c, r, o, holder, now);
            if (kind == PendingEvent.Illness)
            {
                o.IllnessRound = true; o.LastIllnessTick = now;
                if (r.CheckupOpen(now))
                {
                    foreach (var id in o.Examined.ToList()) if (IsSick(c.TryGetPawn(id))) o.Examined.Remove(id);          // the sick are to be looked at again
                    o.RoundStartTick = now;
                }
                else { o.NextRoundTick = 0; r.BeginCheckupRound(now, Interval()); }
            }
            else if (!r.CheckupOpen(now)) { o.NextRoundTick = 0; r.BeginCheckupRound(now, Interval()); }
            CheckupQueue.Reset();
            Messages.Message((kind == PendingEvent.Illness ? "WFL_Checkup_Illness" : "WFL_Checkup_Round").Translate(holder.LabelShort), holder, MessageTypeDefOf.NeutralEvent, false);
            return true;
        }

        internal static void Pass(int kind)
        {
            var o = OfficeFor(kind); var c = FamilyComponent.Current; if (o == null) return;
            int now = Activities.Now;
            o.PendingKind = 0;
            if (kind == PendingEvent.Testing) { var t = c?.TryGetPawn(o.HolderId); if (t?.Map != null) o.LastTestingYear = Year(t.Map); }
            else if (kind == PendingEvent.Illness) o.LastIllnessTick = now - 5 * Day;          // asked again in five days if it is still going on
            else o.NextRoundTick = now + Interval() / 2;
        }

        // ------------------------------------------------------------------ the doctor

        internal static void Healer(FamilyComponent c, FamilyRegistry r, CommunityOffice o, Pawn doctor, int now)
        {
            if (!r.CheckupOpen(now)) o.IllnessRound = false;
            if (o.PendingKind == PendingEvent.Checkups || o.PendingKind == PendingEvent.Illness)
            {
                if (now - o.PendingTick > AutoBegin || now < o.PendingTick) Begin(o.PendingKind);
                return;
            }
            if (o.PendingKind != 0) return;
            if (!o.IllnessRound && now - o.LastIllnessTick > IllnessGap && MassIllness(doctor.Map)) { Offer(PendingEvent.Illness, doctor, now); return; }
            if (!r.CheckupOpen(now) && now >= o.NextRoundTick) Offer(PendingEvent.Checkups, doctor, now);
        }

        // ------------------------------------------------------------------ the teacher and the annual testing

        private static List<int> Children(FamilyComponent c, FamilyRegistry r, int teacherId)
        {
            bool adults = FamilyLifeMod.Settings != null && FamilyLifeMod.Settings.adultStudents;
            return r.Persons.Where(p => p.InColony && p.Id != teacherId && (p.Juvenile || adults)).Select(p => p.Id).Take(24).ToList();
        }

        internal static void Teacher(FamilyComponent c, FamilyRegistry r, CommunityOffice o, Pawn teacher, int now)
        {
            if (o.TestingStartTick != 0) { RunTesting(c, r, o, teacher, now); return; }
            if (o.PendingKind == PendingEvent.Testing)
            {
                if (now - o.PendingTick > AutoBegin || now < o.PendingTick) Begin(PendingEvent.Testing);
                return;
            }
            if (o.PendingKind != 0) return;
            if (IsAprimay(teacher.Map) && o.LastTestingYear != Year(teacher.Map) && Children(c, r, o.HolderId).Count > 0) Offer(PendingEvent.Testing, teacher, now);
        }

        private static bool BeginTesting(FamilyComponent c, FamilyRegistry r, CommunityOffice o, Pawn teacher, int now)
        {
            if (o.TestingStartTick != 0) return false;
            var ids = Children(c, r, o.HolderId);
            if (ids.Count == 0) { Messages.Message("WFL_Event_NoPupils".Translate(), MessageTypeDefOf.RejectInput, false); return false; }
            o.TestingStartTick = now; o.TestingSlot = -1; o.LastTestingYear = Year(teacher.Map);
            o.TestIds = ids; o.TestPass = ids.Select(_ => 0).ToList(); o.TestTry = ids.Select(_ => 0).ToList();
            Messages.Message("WFL_Testing_Begins".Translate(teacher.LabelShort, ids.Count), teacher, MessageTypeDefOf.NeutralEvent, false);
            return true;
        }

        private static int Level(Pawn p, SkillDef d) { var s = p?.skills?.GetSkill(d); return s == null || s.TotallyDisabled ? 0 : s.Level; }

        private static void RunTesting(FamilyComponent c, FamilyRegistry r, CommunityOffice o, Pawn teacher, int now)
        {
            if (now < o.TestingStartTick) { o.TestingStartTick = now; }
            if (now - o.TestingStartTick >= TestingMath.DurationTicks) { FinishTesting(c, r, o, teacher, now); return; }
            int slot = (now - o.TestingStartTick) / TestingMath.SlotTicks;
            if (slot == o.TestingSlot) return;
            o.TestingSlot = slot;
            // Every game hour each child who is up and free sits one check: at a desk in the classroom when there is one, and where they are when there is not.
            bool room = ClassRooms.TeachingSpot(teacher, out var cell, out var area, out _);
            var seats = room ? ClassRooms.Seats(area, cell, o.TestIds.Count) : new List<IntVec3>();
            for (int i = 0; i < o.TestIds.Count; i++)
            {
                var kid = c.TryGetPawn(o.TestIds[i]);
                if (kid == null || !kid.Spawned || kid.Dead || !OfficeDriver.Available(kid) || !OfficeDriver.Ready(kid) || OfficeDriver.NeedsAttention(kid)) continue;          // asleep or busy: no check this hour
                if (seats.Count > 0)
                {
                    var seat = seats[i % seats.Count];
                    if (!kid.CanReach(seat, PathEndMode.OnCell, Danger.None)) continue;
                    var job = JobMaker.MakeJob(WFL_StoryDefOf.WFL_TestStudent, teacher, seat);
                    job.playerForced = true;
                    kid.jobs.StartJob(job, JobCondition.InterruptForced, null, false, tag: JobTag.Misc);
                }
                else Attempt(kid);
            }
        }

        /// <summary>One hourly check for this child, against the teacher's difficulty.</summary>
        internal static void Attempt(Pawn kid)
        {
            var c = FamilyComponent.Current; var o = Office(OfficeKind.Teacher); if (c == null || o == null || o.TestingStartTick == 0) return;
            int i = o.TestIds.IndexOf(kid.thingIDNumber); if (i < 0) return;
            var teacher = c.TryGetPawn(o.HolderId);
            int teacherScore = GuidedExchanges.LeaderScore(Level(teacher, SkillDefOf.Social), Level(teacher, SkillDefOf.Intellectual));
            int best = 0; foreach (var s in kid.skills.skills) if (!s.TotallyDisabled && s.Level > best) best = s.Level;
            bool pass = TestingMath.Passes(Level(kid, SkillDefOf.Intellectual), best, Rand.RangeInclusive(1, 20), TestingMath.Difficulty(teacherScore));
            o.TestTry[i]++; if (pass) o.TestPass[i]++;
            MoteMaker.ThrowText(kid.DrawPos, kid.Map, Phrasing.Say(pass ? "WFL_TestPass" : "WFL_TestFail", kid.thingIDNumber + o.TestTry[i]), pass ? new Color(.72f, 1f, .72f) : new Color(1f, .8f, .65f));
        }

        private static void FinishTesting(FamilyComponent c, FamilyRegistry r, CommunityOffice o, Pawn teacher, int now)
        {
            var lines = new List<string>(); float quality = 0f; int judged = 0;
            for (int i = 0; i < o.TestIds.Count; i++)
            {
                var kid = c.TryGetPawn(o.TestIds[i]);
                int pass = i < o.TestPass.Count ? o.TestPass[i] : 0, tries = i < o.TestTry.Count ? o.TestTry[i] : 0;
                if (kid == null || kid.Dead || kid.skills == null) continue;
                var level = TestingMath.Level(pass, tries);
                var chosen = kid.skills.skills.Where(s => !s.TotallyDisabled).OrderByDescending(s => (int)s.passion).ThenBy(s => s.Level).Take(TestingMath.SkillCount(level)).ToList();
                foreach (var s in chosen) s.Learn(TestingMath.XpEach(level), true);
                Activities.GainMemory(kid, level >= ResultQuality.Good ? "WFL_TestingProud" : level == ResultQuality.Poor ? "WFL_TestingDisappointed" : "WFL_TestingDone", teacher);
                lines.Add("WFL_Testing_Line".Translate(kid.LabelShort, ("WFL_Quality_" + level).Translate(), pass, tries, string.Join(", ", chosen.Select(s => s.def.skillLabel.CapitalizeFirst()))));
                quality += (int)level / 3f; judged++;
                r.RecordTogether(teacher.thingIDNumber, kid.thingIDNumber, 2500, now, BondCause.Teaching);
            }
            if (judged > 0) r.RecordOfficeWork(OfficeKind.Teacher, teacher.thingIDNumber, GovernanceMeetings.NextToken(), now, .4f + .6f * quality / judged);
            o.TestingStartTick = 0; o.TestingSlot = 0; o.TestIds = new List<int>(); o.TestPass = new List<int>(); o.TestTry = new List<int>();
            if (lines.Count > 0)
                Find.LetterStack.ReceiveLetter("WFL_Testing_ResultTitle".Translate(), "WFL_Testing_ResultText".Translate(teacher.LabelShort, string.Join("\n", lines)), LetterDefOf.NeutralEvent, teacher);
        }

        // ------------------------------------------------------------------ buttons

        /// <summary>The player can begin an event whenever they like: from a medical bed, and from a lectern.</summary>
        internal static IEnumerable<Gizmo> Gizmos(Building b)
        {
            if (b == null || !b.Spawned || b.Faction != Faction.OfPlayer) yield break;
            var room = b.GetRoom();
            if (room == null || room.PsychologicallyOutdoors) yield break;
            if (b is Building_Bed bed && (bed.Medical || room.Role == RoomRoleDefOf.Hospital) && Office(OfficeKind.Healer)?.Held == true)
            {
                bool outbreak = MassIllness(b.Map);
                yield return new Command_Action
                {
                    defaultLabel = (outbreak ? "WFL_Event_BeginIllness" : "WFL_Event_BeginCheckups").Translate(),
                    defaultDesc = "WFL_Event_BeginCheckupsDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("WilsFamilyLife/Roles/healer", false) ?? BaseContent.BadTex,
                    action = () => Begin(outbreak ? PendingEvent.Illness : PendingEvent.Checkups)
                };
            }
            if (ClassRooms.IsLectern(b.def) && !ClassRooms.IsOtherRoom(room) && Office(OfficeKind.Teacher)?.Held == true && Office(OfficeKind.Teacher).TestingStartTick == 0)
                yield return new Command_Action
                {
                    defaultLabel = "WFL_Event_BeginTesting".Translate(),
                    defaultDesc = "WFL_Event_BeginTestingDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("WilsFamilyLife/Roles/teacher", false) ?? BaseContent.BadTex,
                    action = () => Begin(PendingEvent.Testing)
                };
        }
    }

    /// <summary>One sitting of the hourly test: the child goes to a desk, sits for a little under an hour's worth of the check, and the check is made.</summary>
    public class JobDriver_TestStudent : JobDriver
    {
        private const int SitTicks = 600;
        private int sat;
        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref sat, "wflSat"); }
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => pawn.Drafted || pawn.InMentalState || OfficeDriver.NeedsAttention(pawn) || FamilyComponent.Current?.Registry.OfficeFor(OfficeKind.Teacher)?.TestingStartTick == 0);
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            var sit = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            sit.initAction = () => pawn.pather?.StopDead();
            sit.tickAction = () => { if (++sat >= SitTicks) { OfficeEvents.Attempt(pawn); EndJobWith(JobCondition.Succeeded); } };
            yield return sit;
        }
    }
}
