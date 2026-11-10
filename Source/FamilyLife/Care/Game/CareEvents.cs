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
    // The doctor's planned events, outbreak detection and priorities. Owner: Community Care.

    /// <summary>What a player (or the pawn they are playing) is shown of the offices' private business.</summary>
    internal static class OfficeCareView
    {
        /// <summary>The result of someone's check-up is theirs and the doctor's. Someone being played who is neither does not see it floating over a stranger's head.</summary>
        internal static bool MayKnowResult(Pawn patient, Pawn doctor)
        {
            int avatar = PerspectiveShiftBridge.AvatarId;
            return avatar == 0 || avatar == patient.thingIDNumber || avatar == doctor.thingIDNumber;
        }
    }

    internal static partial class OfficeEvents
    {
        private const int IllnessGap = 10 * Day;

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

        /// <summary>In an outbreak the sick are seen first.</summary>
        internal static Func<int, int> Priority(FamilyComponent c, CommunityOffice o) => o != null && o.Care().IllnessRound ? (id => IsSick(c.TryGetPawn(id)) ? 0 : 1) : (Func<int, int>)null;

        private static int sickCheckedAt = -100000; private static bool sickRemaining;
        /// <summary>In an outbreak assessment the sick come before the rest: a well person is not seen while someone ill is still waiting. In any other round, everyone is on the list.</summary>
        internal static bool SickFirst(FamilyComponent c, FamilyRegistry r, CommunityOffice o, int id)
        {
            if (o == null || !o.Care().IllnessRound || IsSick(c.TryGetPawn(id))) return true;
            int now = Activities.Now;
            if (now - sickCheckedAt > 600 || now < sickCheckedAt)
            {
                sickCheckedAt = now;
                sickRemaining = r.Persons.Any(p => p.InColony && p.Id != o.HolderId && !o.Care().Examined.Contains(p.Id) && IsSick(c.TryGetPawn(p.Id)));
            }
            return !sickRemaining;
        }

        // ------------------------------------------------------------------ planning

        internal static void Healer(FamilyComponent c, FamilyRegistry r, CommunityOffice o, Pawn doctor, int now)
        {
            var care = o.Care();
            if (!r.CheckupOpen(now)) care.IllnessRound = false;
            if (o.PendingKind == PendingEvent.Checkups || o.PendingKind == PendingEvent.Illness)
            {
                if (now >= o.PendingTick || o.PendingTick - now > 40 * Day) Begin(o.PendingKind);          // the planned time has come (a saved plan from before simply begins)
                return;
            }
            if (o.PendingKind != 0) return;
            // An outbreak: the doctor changes priorities at once (the sick first, tending always before this) and the player is told. Nothing waits for an answer.
            if (!o.Care().IllnessRound && now - o.Care().LastIllnessTick > IllnessGap && MassIllness(doctor.Map)) { Begin(PendingEvent.Illness); return; }
            if (!r.CheckupOpen(now) && now >= o.Care().NextRoundTick) Plan(PendingEvent.Checkups, doctor, now);
        }

        // ------------------------------------------------------------------ the teacher and the annual assessment

        /// <summary>Begins a check-up round, or the sick-first round called for an outbreak.</summary>
        internal static bool BeginCare(int kind)
        {
            var c = FamilyComponent.Current; var r = c?.Registry; if (r == null) return false;
            int now = Activities.Now;
            var o = Office(OfficeKind.Healer); if (o == null || !o.Held) return false;
            var holder = c.TryGetPawn(o.HolderId); if (holder == null) return false;
            if (kind == PendingEvent.Illness)
            {
                o.Care().IllnessRound = true; o.Care().LastIllnessTick = now;
                if (r.CheckupOpen(now))
                {
                    foreach (var id in o.Care().Examined.ToList()) if (IsSick(c.TryGetPawn(id))) o.Care().Examined.Remove(id);          // the sick are to be looked at again
                    o.Care().RoundStartTick = now;
                    o.Care().RoundWindow = CheckupWorkload.WindowDays(r.Persons.Count(p => p.InColony && p.Id != o.HolderId), true) * Day;
                }
                else { o.Care().NextRoundTick = 0; r.BeginCheckupRound(now, Interval()); }
            }
            else if (!r.CheckupOpen(now)) { o.Care().NextRoundTick = 0; r.BeginCheckupRound(now, Interval()); }
            AppointmentBook.Reset();
            Messages.Message((kind == PendingEvent.Illness ? "WFL_Checkup_Illness" : "WFL_Checkup_Round").Translate(holder.LabelShort), holder, MessageTypeDefOf.NeutralEvent, false);
            return true;
        }

        /// <summary>Skipping a round puts the next one back; skipping the outbreak round asks again in five days if it is still going on.</summary>
        internal static void PassCare(int kind)
        {
            var o = Office(OfficeKind.Healer); if (o == null) return;
            int now = Activities.Now;
            if (kind == PendingEvent.Illness) o.Care().LastIllnessTick = now - 5 * Day;
            else o.Care().NextRoundTick = now + Interval() / 2;
        }

        /// <summary>The "Begin check-ups" button on a medical bed.</summary>
        internal static IEnumerable<Gizmo> CareGizmos(Building b)
        {
            var room = b.GetRoom();
            if (room == null || room.PsychologicallyOutdoors) yield break;
            var reg = FamilyComponent.Current?.Registry; if (reg == null) yield break;
            if (b is Building_Bed bed && (bed.Medical || room.Role == RoomRoleDefOf.Hospital) && Office(OfficeKind.Healer)?.Held == true && OfficeAuthority.MayDirect(reg, PerspectiveShiftBridge.AvatarId, OfficeKind.Healer))
            {
                bool outbreak = MassIllness(b.Map);
                yield return new Command_Action
                {
                    defaultLabel = (outbreak ? "WFL_Event_BeginIllness" : "WFL_Event_BeginCheckups").Translate(),
                    defaultDesc = "WFL_Event_BeginCheckupsDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("WilsFamilyLife/Roles/healer", false) ?? BaseContent.BadTex,
                    action = () => { if (OfficeAuthority.MayDirect(reg, PerspectiveShiftBridge.AvatarId, OfficeKind.Healer)) Begin(outbreak ? PendingEvent.Illness : PendingEvent.Checkups); }
                };
            }
        }
    }
}
