using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    // The teacher's class wrappers over the shared session queue. Owner: Education.

    public sealed partial class FamilyRegistry
    {
        public const int MaxClassSize = 24;          // a class is the colony's children; the hall keeper's meals stay at MaxSessionSize
        public bool BeginClass(int now, int intervalTicks, IEnumerable<int> students) => BeginSession(OfficeKind.Teacher, now, intervalTicks, students, MaxClassSize);
        public int NextStudent(int now) => NextInSession(OfficeKind.Teacher, now);
        public void ClassDone(int student) => SessionDone(OfficeKind.Teacher, student);
    }
}
