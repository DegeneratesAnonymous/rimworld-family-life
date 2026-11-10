using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    // What the teacher's post remembers: what the children studied, the books used, the recent subjects and the assessment in progress. Every field has a safe default. Owner: Education.

    public sealed partial class CommunityOffice
    {
        /// <summary>The teacher's record of what each child studied since the last assessment; see <see cref="LessonRecord"/>.</summary>
        public List<string> LessonLog = new List<string>();
        /// <summary>Books already used for a pupil's lesson, so the same book cannot give the same child its experience over and over: "pupil|book|tick".</summary>
        public List<string> BookUse = new List<string>();
        /// <summary>The subjects of the last few classes, newest last, so the teacher varies them.</summary>
        public List<string> RecentSubjects = new List<string>();
        /// <summary>Each assessed child's accumulated sitting performance (hundredths), parallel to TestIds; and how many windows they were excused from.</summary>
        public List<int> TestScore = new List<int>(), TestExcused = new List<int>();
    }
}
