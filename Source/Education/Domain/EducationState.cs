using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Framework;

namespace FamilyLife.Domain
{
    // What the teacher's post remembers. Two payloads: what the children have studied (it belongs to the children and outlives any teacher) and the assessment in progress (it belongs to
    // whoever is teaching and starts fresh with a new teacher). Neither is a field of the shared record. Owner: Education.

    public sealed class EducationOfficeData : IModuleState
    {
        public const string Id = "wils.education";

        public int LastTestingYear;
        /// <summary>The teacher's record of what each child studied since the last assessment; see <see cref="LessonRecord"/>.</summary>
        public List<string> LessonLog = new List<string>();
        /// <summary>Books already used for a pupil's lesson, so the same book cannot give the same child its experience over and over: "pupil|book|tick".</summary>
        public List<string> BookUse = new List<string>();
        /// <summary>The subjects of the last few classes, newest last, so the teacher varies them.</summary>
        public List<string> RecentSubjects = new List<string>();

        public string Encode() => new StateWriter().Int("lastTestingYear", LastTestingYear).Strings("lessonLog", LessonLog).Strings("bookUse", BookUse).Strings("recentSubjects", RecentSubjects).ToString();

        internal static EducationOfficeData Read(StateReader r) => new EducationOfficeData { LastTestingYear = r.Int("lastTestingYear"), LessonLog = r.Strings("lessonLog"), BookUse = r.Strings("bookUse"), RecentSubjects = r.Strings("recentSubjects") };
    }

    public sealed class AssessmentData : IModuleState
    {
        public const string Id = "wils.education.assessment";

        public int TestingStartTick, TestingSlot;
        /// <summary>Each child assessed, parallel to the other lists: sittings completed, a small variation per sitting (hundredths), and windows they were excused from.</summary>
        public List<int> TestIds = new List<int>(), TestPass = new List<int>(), TestTry = new List<int>(), TestScore = new List<int>(), TestExcused = new List<int>();

        public string Encode() => new StateWriter().Int("testingStart", TestingStartTick).Int("testingSlot", TestingSlot).Ints("testIds", TestIds).Ints("testPass", TestPass).Ints("testTry", TestTry).Ints("testScore", TestScore).Ints("testExcused", TestExcused).ToString();

        internal static AssessmentData Read(StateReader r)
        {
            var d = new AssessmentData { TestingStartTick = r.Int("testingStart"), TestingSlot = r.Int("testingSlot"), TestIds = r.Ints("testIds"), TestPass = r.Ints("testPass"), TestTry = r.Ints("testTry"), TestScore = r.Ints("testScore"), TestExcused = r.Ints("testExcused") };
            while (d.TestPass.Count < d.TestIds.Count) d.TestPass.Add(0);
            while (d.TestTry.Count < d.TestIds.Count) d.TestTry.Add(0);
            while (d.TestScore.Count < d.TestIds.Count) d.TestScore.Add(0);
            while (d.TestExcused.Count < d.TestIds.Count) d.TestExcused.Add(0);
            return d;
        }
    }

    public sealed class EducationStateFactory : IStateFactory<EducationOfficeData>
    {
        public static readonly EducationStateFactory Instance = new EducationStateFactory();
        public int Version => 1;
        public EducationOfficeData Create() => new EducationOfficeData();
        public EducationOfficeData Decode(StateReader reader, int version) => EducationOfficeData.Read(reader);
        public EducationOfficeData FromLegacy(StateReader legacy) => EducationOfficeData.Read(legacy);
    }

    public sealed class AssessmentStateFactory : IStateFactory<AssessmentData>
    {
        public static readonly AssessmentStateFactory Instance = new AssessmentStateFactory();
        public int Version => 1;
        public AssessmentData Create() => new AssessmentData();
        public AssessmentData Decode(StateReader reader, int version) => AssessmentData.Read(reader);
        public AssessmentData FromLegacy(StateReader legacy) => AssessmentData.Read(legacy);
    }

    public static class EducationState
    {
        /// <summary>What the children studied, the books used and the recent subjects (outlives the teacher).</summary>
        public static EducationOfficeData Education(this CommunityOffice office) => office.StateOf(EducationOfficeData.Id, false, EducationStateFactory.Instance);
        /// <summary>The assessment in progress (fresh for a new teacher).</summary>
        public static AssessmentData Assessment(this CommunityOffice office) => office.StateOf(AssessmentData.Id, true, AssessmentStateFactory.Instance);
    }
}
