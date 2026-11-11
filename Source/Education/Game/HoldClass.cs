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
    // The "Hold class" button and the class that waits for the next opportunity. Owner: Education.

    internal static class HoldClass
    {
        private static Texture2D icon;
        private static Texture2D Icon => icon ?? (icon = ContentFinder<Texture2D>.Get("WilsFamilyLife/Roles/teacher", false) ?? BaseContent.BadTex);

        /// <summary>The "Hold class" button for a lectern in a room that could be a classroom, or null.</summary>
        internal static Gizmo For(Building b)
        {
            if (b == null || !b.Spawned || b.Faction != Faction.OfPlayer || !ClassRooms.IsLectern(b.def)) return null;
            var reg = FamilyComponent.Current?.Registry;
            if (reg == null || !OfficeAuthority.MayDirect(reg, FrameworkServices.AvatarId, OfficeKind.Teacher)) return null;
            var room = b.GetRoom();
            if (room == null || room.PsychologicallyOutdoors || ClassRooms.IsOtherRoom(room)) return null;
            return new Command_Action
            {
                defaultLabel = "WFL_HoldClass_Label".Translate(),
                defaultDesc = "WFL_HoldClass_Desc".Translate(),
                icon = Icon,
                action = () => Start(b)
            };
        }

        // A class asked for while the teacher cannot start (hungry, tired, in the middle of something that matters) is held at the next opportunity, for up to a day.
        private static bool pending; private static Building pendingLectern; private static int pendingUntil;
        internal static bool IsQueued => pending;

        internal static void Queue(Building lectern, int now) { pending = true; pendingLectern = lectern; pendingUntil = now + ActivityTuning.TicksPerDay; }
        internal static void Cancel() => pending = false;

        /// <summary>Called as the post's work runs: tries the queued class again, and drops it once held, impossible or a day old.</summary>
        internal static void Tick(int now)
        {
            if (!pending) return;
            if (now > pendingUntil || now < pendingUntil - 2 * ActivityTuning.TicksPerDay) { pending = false; return; }
            if (pendingLectern != null && !pendingLectern.Spawned) pendingLectern = null;
            var result = Attempt(pendingLectern, now, false);
            if (result != BeginResult.Later) pending = false;
        }

        /// <summary>Starts the class if the teacher can. Later means the teacher is not able just now (the caller may queue it); Cannot means it is not possible at all.</summary>
        private static BeginResult Attempt(Building lectern, int now, bool announce)
        {
            var c = FamilyComponent.Current; var r = c?.Registry; if (r == null) return BeginResult.Cannot;
            var office = r.OfficeFor(OfficeKind.Teacher);
            var teacher = office != null && office.Held ? c.TryGetPawn(office.HolderId) : null;
            if (teacher == null || !teacher.Spawned || lectern != null && teacher.Map != lectern.Map) { if (announce) Messages.Message("WFL_HoldClass_NoTeacher".Translate(), MessageTypeDefOf.RejectInput, false); return BeginResult.Cannot; }
            if (ClassSessions.Running(teacher)) { if (announce) Messages.Message("WFL_HoldClass_Running".Translate(teacher.LabelShort), MessageTypeDefOf.RejectInput, false); return BeginResult.Cannot; }
            bool adults = FamilyLifeMod.Settings != null && FamilyLifeMod.Settings.adultStudents;
            var ids = r.Persons.Where(p => p.InColony && p.Id != office.HolderId && (p.Juvenile || adults))
                .Where(p => c.TryGetPawn(p.Id) is Pawn k && k.Spawned && k.Map == teacher.Map && FrameworkServices.Available(k)).Select(p => p.Id).Take(24).ToList();
            if (ids.Count == 0) { if (announce) Messages.Message("WFL_HoldClass_NoPupils".Translate(), MessageTypeDefOf.RejectInput, false); return BeginResult.Later; }          // nobody free yet: worth waiting for
            var saved = office.SessionQueue.ToList(); int savedStart = office.SessionStartTick;
            office.SessionQueue = ids; office.SessionStartTick = now;          // they are the class: lessons are credited the same way as a class held on the usual round
            var result = ClassSessions.TryBegin(c, teacher, ids, now, lectern, true);
            if (result != BeginResult.Started) { office.SessionQueue = saved; office.SessionStartTick = savedStart; return result; }
            Messages.Message("WFL_HoldClass_Started".Translate(teacher.LabelShort, ids.Count), teacher, MessageTypeDefOf.PositiveEvent, false);
            return result;
        }

        private static void Start(Building lectern)
        {
            var c = FamilyComponent.Current; var r = c?.Registry; if (r == null) return;
            int now = FrameworkServices.Now;
            if (!OfficeAuthority.MayDirect(r, FrameworkServices.AvatarId, OfficeKind.Teacher)) return;          // checked again here: the button may have been shown to someone else
            StartOrQueue(lectern, now);
        }

        /// <summary>Holds the class now, or at the next opportunity if the teacher (or the children) cannot just now.</summary>
        internal static void StartOrQueue(Building lectern, int now)
        {
            var result = Attempt(lectern, now, true);
            if (result == BeginResult.Later)
            {
                Queue(lectern, now);
                var c = FamilyComponent.Current; var o = c?.Registry.OfficeFor(OfficeKind.Teacher); var t = o != null ? c.TryGetPawn(o.HolderId) : null;
                Messages.Message("WFL_HoldClass_Queued".Translate(t?.LabelShort ?? "-"), t, MessageTypeDefOf.NeutralEvent, false);
            }
            else if (result == BeginResult.Cannot && lectern != null) { var c = FamilyComponent.Current; var o = c?.Registry.OfficeFor(OfficeKind.Teacher); var t = o != null ? c.TryGetPawn(o.HolderId) : null; if (t != null && !ClassSessions.Running(t)) Messages.Message("WFL_HoldClass_Cannot".Translate(t.LabelShort), MessageTypeDefOf.RejectInput, false); }
        }
    }
}
