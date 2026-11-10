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
    // Planned routine events (a check-up round, the annual assessment) as the framework sees them: a saved pending kind, a notice that never blocks, and authority checks. What
    // beginning or skipping one actually does belongs to the module that registered it (PlannedEventCatalog). Owner: the framework.

    /// <summary>
    /// A notice about a planned routine event. It does not hold anything up: the event begins by itself at the planned time. The player may begin it now, put it off for a week, or
    /// skip it this time, if they are allowed to direct that post.
    /// </summary>
    public class ChoiceLetter_OfficeEvent : ChoiceLetter
    {
        public int kind;

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                var c = FamilyComponent.Current;
                var def = FamilyModules.PlannedEvents.For(kind);
                if (c != null && def != null && OfficeAuthority.MayDirect(c.Registry, PerspectiveShiftBridge.AvatarId, def.Post))
                {
                    yield return new DiaOption("WFL_Event_Begin".Translate()) { action = () => { OfficeEvents.Begin(kind); Find.LetterStack.RemoveLetter(this); }, resolveTree = true };
                    yield return new DiaOption("WFL_Event_Reschedule".Translate()) { action = () => { OfficeEvents.Reschedule(kind); Find.LetterStack.RemoveLetter(this); }, resolveTree = true };
                    yield return new DiaOption("WFL_Event_Pass".Translate()) { action = () => { OfficeEvents.Pass(kind); Find.LetterStack.RemoveLetter(this); }, resolveTree = true };
                }
                yield return Option_Close;          // nothing needs answering: it goes ahead by itself
            }
        }

        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref kind, "wflEventKind"); }
    }

    internal static partial class OfficeEvents
    {
        private const int Day = ActivityTuning.TicksPerDay;
        private const int PlanLead = Day, RescheduleBy = 7 * Day;
        private static LetterDef letterDef;
        private static LetterDef LetterKind => letterDef ?? (letterDef = DefDatabase<LetterDef>.GetNamedSilentFail("WFL_OfficeEvent") ?? LetterDefOf.NeutralEvent);

        private static CommunityOffice Office(OfficeKind kind) => FamilyComponent.Current?.Registry.OfficeFor(kind);
        private static CommunityOffice OfficeFor(int eventKind) { var d = FamilyModules.PlannedEvents.For(eventKind); return d == null ? null : Office(d.Post); }

        internal static float Longitude(Map m) => Find.WorldGrid.LongLatOf(m.Tile).x;
        internal static bool IsAprimay(Map m) => GenDate.Quadrum(Find.TickManager.TicksAbs, Longitude(m)) == Quadrum.Aprimay;
        internal static int Year(Map m) => GenDate.Year(Find.TickManager.TicksAbs, Longitude(m));

        /// <summary>Plans the event for tomorrow and says so. Nothing waits on an answer.</summary>
        private static void Plan(int kind, Pawn holder, int now)
        {
            var o = OfficeFor(kind); if (o == null || !o.Held) return;
            o.PendingKind = kind; o.PendingTick = now + PlanLead;
            string key = FamilyModules.PlannedEvents.For(kind)?.NoticeKey ?? "Checkups";
            var letter = (ChoiceLetter_OfficeEvent)LetterMaker.MakeLetter(("WFL_Event_" + key + "_Title").Translate(), ("WFL_Event_" + key + "_Text").Translate(holder.LabelShort), LetterKind, holder);
            letter.kind = kind;
            Find.LetterStack.ReceiveLetter(letter);
        }

        internal static void Plan(int kind) { var c = FamilyComponent.Current; var o = OfficeFor(kind); var p = o != null && o.Held ? c?.TryGetPawn(o.HolderId) : null; if (p != null && p.Spawned) Plan(kind, p, Activities.Now); }

        /// <summary>Removes the notices of the given planned events from the letter stack (a saved notice names a letter class the module owns). Returns a line saying so, if any were there.</summary>
        internal static IEnumerable<string> RemoveNotices(params int[] kinds)
        {
            int n = 0;
            foreach (var l in Find.LetterStack.LettersListForReading.OfType<ChoiceLetter_OfficeEvent>().Where(x => kinds.Contains(x.kind)).ToList()) { Find.LetterStack.RemoveLetter(l); n++; }
            if (n > 0) yield return n + " notice(s) removed from the letter stack";
        }

        private static void DropNotices(int kind)
        {
            foreach (var l in Find.LetterStack.LettersListForReading.OfType<ChoiceLetter_OfficeEvent>().Where(x => x.kind == kind).ToList()) Find.LetterStack.RemoveLetter(l);
        }

        // ------------------------------------------------------------------ beginning, putting off and skipping

        // ------------------------------------------------------------------ beginning, putting off and skipping

        /// <summary>Begins a planned event now. The framework checks the post and the holder; the module that owns the event does the rest.</summary>
        internal static bool Begin(int kind)
        {
            var def = FamilyModules.PlannedEvents.For(kind);
            var c = FamilyComponent.Current; var r = c?.Registry; if (r == null) return false;
            if (def == null)
            {
                // the module that owned this event is absent or switched off: clear a waiting event rather than leave it forever, and run nothing
                foreach (var po in r.Offices) if (po.PendingKind == kind) po.PendingKind = 0;
                return false;
            }
            var o = Office(def.Post);
            if (o == null || !o.Held) { Messages.Message(def.NoPostKey.Translate(), MessageTypeDefOf.RejectInput, false); return false; }
            var holder = c.TryGetPawn(o.HolderId);
            if (holder == null || !holder.Spawned) { o.PendingKind = 0; return false; }
            o.PendingKind = 0;
            DropNotices(kind);
            return def.Begin();
        }

        /// <summary>Put it off for a week. Checked again when it runs: only someone who may direct the post can.</summary>
        internal static void Reschedule(int kind)
        {
            var def = FamilyModules.PlannedEvents.For(kind);
            var c = FamilyComponent.Current; var o = OfficeFor(kind); if (o == null || c == null || def == null) return;
            if (!OfficeAuthority.MayDirect(c.Registry, PerspectiveShiftBridge.AvatarId, def.Post)) return;
            if (o.PendingKind == kind) o.PendingTick += RescheduleBy;
            DropNotices(kind);
        }

        internal static void Pass(int kind)
        {
            var def = FamilyModules.PlannedEvents.For(kind);
            var o = OfficeFor(kind); var c = FamilyComponent.Current; if (o == null || c == null || def == null) return;
            if (!OfficeAuthority.MayDirect(c.Registry, PerspectiveShiftBridge.AvatarId, def.Post)) return;
            o.PendingKind = 0;
            def.Pass();
            DropNotices(kind);
        }

        /// <summary>The buttons modules add to a building's context (a lectern, a medical bed), each skipped while its module is disabled.</summary>
        internal static IEnumerable<Gizmo> Gizmos(Building b)
        {
            if (b == null || !b.Spawned || b.Faction != Faction.OfPlayer) yield break;
            foreach (var provider in FamilyModules.BuildingGizmos.Enabled())
                foreach (var g in provider(b)) yield return g;
        }
    }
}
