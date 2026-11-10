using System;
using System.Collections.Generic;

namespace FamilyLife.Domain
{
    // The teacher's class wrappers over the shared session queue (the same queue the hall keeper's meals use). Owner: Education.

    public static class ClassRegistry
    {
        public const int MaxClassSize = 24;          // a class is the colony's children; the hall keeper's meals stay at FamilyRegistry.MaxSessionSize

        public static bool BeginClass(this FamilyRegistry r, int now, int intervalTicks, IEnumerable<int> students) => r.BeginSession(OfficeKind.Teacher, now, intervalTicks, students, MaxClassSize);
        public static int NextStudent(this FamilyRegistry r, int now) => r.NextInSession(OfficeKind.Teacher, now);
        public static void ClassDone(this FamilyRegistry r, int student) => r.SessionDone(OfficeKind.Teacher, student);

        /// <summary>
        /// Ends everything the teacher has under way so the module can be taken away: the class list is emptied, an assessment in progress is stopped (what the children studied stays on the
        /// record), a waiting planned event is cleared, and the post is given up. Returns what was done, one line each.
        /// </summary>
        public static List<string> EndEducationActivity(this FamilyRegistry r, int now, params int[] plannedKinds)
        {
            var done = new List<string>();
            var o = r.OfficeFor(OfficeKind.Teacher);
            if (o == null) return done;
            if (o.SessionQueue.Count > 0) { done.Add("the class list (" + o.SessionQueue.Count + " children) was emptied"); o.SessionQueue.Clear(); }
            var a = o.Assessment();
            if (a.TestingStartTick != 0) { a.TestingStartTick = 0; a.TestingSlot = 0; a.TestIds = new List<int>(); a.TestPass = new List<int>(); a.TestTry = new List<int>(); a.TestScore = new List<int>(); a.TestExcused = new List<int>(); done.Add("the assessment in progress was stopped"); }
            if (Array.IndexOf(plannedKinds, o.PendingKind) >= 0) { o.PendingKind = 0; done.Add("a planned event waiting to begin was cleared"); }
            if (o.Held && r.ResignOffice(OfficeKind.Teacher, o.HolderId, now)) done.Add("the teacher's post was given up");
            return done;
        }
    }
}
