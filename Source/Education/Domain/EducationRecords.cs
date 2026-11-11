using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FamilyLife.Framework;

namespace FamilyLife.Domain
{
    // The community's education record and the assessments, no longer hung on the single Teacher office. Owner: Education.
    //
    //   EducationRecord  (wils.education.record)       what the children have studied, books used, recent subjects. Community-wide: outlives every teacher.
    //   AssessmentLedger (wils.education.assessments)  each teacher's own assessment in progress, when each last held one, and which (pupil, subject, year) have been assessed.

    public sealed class EducationRecord : IModuleState
    {
        public const string Id = "wils.education.record";
        public List<string> LessonLog = new List<string>(), BookUse = new List<string>(), RecentSubjects = new List<string>();
        /// <summary>The old Teacher post's data was copied in. Written so a second load never copies it again.</summary>
        public bool ImportedFromOffice;

        public string Encode() => new StateWriter().Bool("imported", ImportedFromOffice).Strings("lessonLog", LessonLog).Strings("bookUse", BookUse).Strings("recentSubjects", RecentSubjects).ToString();
        public static EducationRecord Decode(StateReader r) => new EducationRecord { ImportedFromOffice = r.Bool("imported"), LessonLog = r.Strings("lessonLog"), BookUse = r.Strings("bookUse"), RecentSubjects = r.Strings("recentSubjects") };
    }

    public sealed class EducationRecordFactory : IStateFactory<EducationRecord>
    {
        public static readonly EducationRecordFactory Instance = new EducationRecordFactory();
        public int Version => 1;
        public EducationRecord Create() => new EducationRecord();
        public EducationRecord Decode(StateReader reader, int version) => EducationRecord.Decode(reader);
        public EducationRecord FromLegacy(StateReader legacy) => new EducationRecord();
    }

    public sealed class AssessmentSitting
    {
        public int Owner, Year, StartTick, Slot;
        public List<int> Ids = new List<int>(), Pass = new List<int>(), Try = new List<int>(), Score = new List<int>(), Excused = new List<int>();
        public void Align()
        {
            while (Pass.Count < Ids.Count) Pass.Add(0); while (Try.Count < Ids.Count) Try.Add(0); while (Score.Count < Ids.Count) Score.Add(0); while (Excused.Count < Ids.Count) Excused.Add(0);
        }
    }

    public sealed class AssessmentLedger : IModuleState
    {
        public const string Id = "wils.education.assessments";
        public const int MaxDone = 400;
        public readonly List<AssessmentSitting> Active = new List<AssessmentSitting>();
        private readonly Dictionary<int, int> lastYear = new Dictionary<int, int>();
        private readonly List<string> done = new List<string>();
        private readonly HashSet<string> migrated = new HashSet<string>();
        private readonly Dictionary<int, int> postponed = new Dictionary<int, int>();
        /// <summary>When the one consolidated assessment notice was sent (0 = none waiting) and for which year. Nothing waits on an answer: when it has stood for a day the assessments begin.</summary>
        public int NoticeTick, NoticeYear;

        public int PostponedUntil(int owner) => postponed.TryGetValue(owner, out int t) ? t : 0;
        public void Postpone(int owner, int until) { if (owner > 0) postponed[owner] = until; }

        public AssessmentSitting Of(int owner) => Active.FirstOrDefault(s => s.Owner == owner);
        public AssessmentSitting SittingFor(int pupil) => Active.FirstOrDefault(s => s.Ids.Contains(pupil));
        public bool PupilBusy(int pupil) => SittingFor(pupil) != null;
        public int LastYearOf(int owner) => lastYear.TryGetValue(owner, out int y) ? y : 0;
        public void SetLastYear(int owner, int year) { if (owner > 0) lastYear[owner] = year; }

        private static string Key(int pupil, string skill, int year) => pupil + "|" + skill + "|" + year;
        public bool WasDone(int pupil, string skill, int year) => done.Contains(Key(pupil, skill, year));
        public void MarkDone(int pupil, string skill, int year) { string k = Key(pupil, skill, year); if (!done.Contains(k)) { done.Add(k); while (done.Count > MaxDone) done.RemoveAt(0); } }

        /// <summary>Opens a teacher's assessment. Refused if they already have one running or no pupil is left to assess. Pupils already sitting for another teacher are left out.</summary>
        public AssessmentSitting Begin(int owner, int year, int tick, IEnumerable<int> pupils)
        {
            if (owner <= 0 || Of(owner) != null) return null;
            var ids = pupils.Distinct().Where(p => !PupilBusy(p)).ToList();
            if (ids.Count == 0) return null;
            var s = new AssessmentSitting { Owner = owner, Year = year, StartTick = tick, Ids = ids };
            s.Align(); Active.Add(s); SetLastYear(owner, year);
            return s;
        }
        public void End(int owner) { Active.RemoveAll(s => s.Owner == owner); }

        /// <summary>A teacher is gone: their assessment in progress passes to another teacher, once that teacher has none of their own. Pupils and results are kept as they are.</summary>
        public bool Transfer(int from, int to)
        {
            var s = Of(from); if (s == null || from == to || Of(to) != null) return false;
            s.Owner = to; return true;
        }

        public bool HasMigrated(string key) => migrated.Contains(key);
        public void MarkMigrated(string key) { migrated.Add(key); }

        public string Encode()
        {
            var w = new StateWriter().Ints("n", Enumerable.Range(0, Active.Count).ToList());
            for (int i = 0; i < Active.Count; i++)
            {
                var s = Active[i]; s.Align();
                w.Int("o" + i, s.Owner).Int("y" + i, s.Year).Int("t" + i, s.StartTick).Int("l" + i, s.Slot).Ints("i" + i, s.Ids).Ints("p" + i, s.Pass).Ints("r" + i, s.Try).Ints("c" + i, s.Score).Ints("x" + i, s.Excused);
            }
            var owners = lastYear.Keys.OrderBy(k => k).ToList();
            var po = postponed.Keys.OrderBy(k => k).ToList();
            w.Int("nt", NoticeTick).Int("ny", NoticeYear).Ints("po", po).Ints("pt", po.Select(k => postponed[k]).ToList());
            return w.Ints("lo", owners).Ints("ly", owners.Select(o => lastYear[o]).ToList()).Strings("done", done).Strings("mig", migrated.OrderBy(m => m, StringComparer.Ordinal).ToList()).ToString();
        }

        public static AssessmentLedger Decode(StateReader r)
        {
            var l = new AssessmentLedger();
            foreach (int i in r.Ints("n"))
            {
                var s = new AssessmentSitting { Owner = r.Int("o" + i), Year = r.Int("y" + i), StartTick = r.Int("t" + i), Slot = r.Int("l" + i), Ids = r.Ints("i" + i), Pass = r.Ints("p" + i), Try = r.Ints("r" + i), Score = r.Ints("c" + i), Excused = r.Ints("x" + i) };
                if (s.Owner > 0 && s.Ids.Count > 0 && l.Of(s.Owner) == null) { s.Align(); l.Active.Add(s); }
            }
            var lo = r.Ints("lo"); var ly = r.Ints("ly");
            for (int i = 0; i < lo.Count && i < ly.Count; i++) l.lastYear[lo[i]] = ly[i];
            l.NoticeTick = r.Int("nt"); l.NoticeYear = r.Int("ny");
            var po = r.Ints("po"); var pt = r.Ints("pt"); for (int i = 0; i < po.Count && i < pt.Count; i++) l.postponed[po[i]] = pt[i];
            foreach (var d in r.Strings("done")) l.done.Add(d);
            foreach (var m in r.Strings("mig")) l.migrated.Add(m);
            return l;
        }
    }

    public sealed class AssessmentLedgerFactory : IStateFactory<AssessmentLedger>
    {
        public static readonly AssessmentLedgerFactory Instance = new AssessmentLedgerFactory();
        public int Version => 1;
        public AssessmentLedger Create() => new AssessmentLedger();
        public AssessmentLedger Decode(StateReader reader, int version) => AssessmentLedger.Decode(reader);
        public AssessmentLedger FromLegacy(StateReader legacy) => new AssessmentLedger();
    }

    public static class AssessmentOwnership
    {
        /// <summary>
        /// Who assesses a pupil's work in one subject: the instructor who gave the most of it (ties to the lowest id). If that teacher is no longer an instructor, or the lesson was
        /// recorded before teachers were named, the lowest-numbered current instructor does. The same inputs always give the same owner, so two teachers never both assess it.
        /// </summary>
        public static int OwnerOf(IEnumerable<LessonRecord> recordsForPupilSkill, IList<int> instructors)
        {
            if (instructors == null || instructors.Count == 0) return 0;
            var best = recordsForPupilSkill.Where(r => r.Teacher != 0 && instructors.Contains(r.Teacher)).GroupBy(r => r.Teacher)
                .Select(g => new { Teacher = g.Key, Xp = g.Sum(x => x.Xp) }).OrderByDescending(x => x.Xp).ThenBy(x => x.Teacher).FirstOrDefault();
            return best?.Teacher ?? instructors.Min();
        }

        /// <summary>The pupils this teacher is to assess this year: those with studied-but-unassessed subjects whose owner is this teacher, in a fixed order.</summary>
        public static List<int> PupilsFor(int owner, IList<string> log, IList<int> instructors, int year, AssessmentLedger ledger, int max = 24)
        {
            var recs = (log ?? new List<string>()).Select(LessonRecord.Parse).Where(r => r != null && !ledger.WasDone(r.Pupil, r.Skill, year)).ToList();
            return recs.GroupBy(r => new { r.Pupil, r.Skill }).Where(g => OwnerOf(g, instructors) == owner).Select(g => g.Key.Pupil).Distinct().Where(p => !ledger.PupilBusy(p)).OrderBy(p => p).Take(max).ToList();
        }

        /// <summary>The merged record, per subject, of everything the pupil studied that this teacher owns. Several teachers' lessons in one subject count once, together.</summary>
        public static List<LessonRecord> RecordsOwnedBy(int owner, int pupil, IList<string> log, IList<int> instructors, int year, AssessmentLedger ledger)
        {
            var recs = LessonRecord.For(log?.ToList(), pupil).Where(r => !ledger.WasDone(pupil, r.Skill, year)).ToList();
            var merged = new List<LessonRecord>();
            foreach (var g in recs.GroupBy(r => r.Skill).OrderBy(g => g.Key, StringComparer.Ordinal))
                if (OwnerOf(g, instructors) == owner) merged.Add(LessonRecord.Merge(g));
            return merged;
        }
    }

    public static class EducationRecords
    {
        /// <summary>The community's education record. The first time it is asked for in a save that had the old Teacher post's data, that data is copied in once.</summary>
        public static EducationRecord EducationRecord(this FamilyRegistry r)
        {
            bool had = r.Shared.Has(Domain.EducationRecord.Id);
            var rec = r.Shared.Of(Domain.EducationRecord.Id, EducationRecordFactory.Instance);
            if (!had && !rec.ImportedFromOffice)
            {
                var office = r.OfficeFor(OfficeKind.Teacher);
                if (office != null && office.PayloadIds.Contains(EducationOfficeData.Id))
                {
                    var old = office.Education();
                    rec.LessonLog = old.LessonLog.ToList(); rec.BookUse = old.BookUse.ToList(); rec.RecentSubjects = old.RecentSubjects.ToList();
                }
                rec.ImportedFromOffice = true;
            }
            return rec;
        }

        public static AssessmentLedger Assessments(this FamilyRegistry r) => r.Shared.Of(AssessmentLedger.Id, AssessmentLedgerFactory.Instance);

        /// <summary>
        /// Brings an assessment the old Teacher post had under way, and its "held this year" marker, into the ledger as that holder's own, once. The old data is left where it was.
        /// </summary>
        public static bool MigrateLegacyAssessment(this FamilyRegistry r)
        {
            var office = r.OfficeFor(OfficeKind.Teacher); if (office == null || office.HolderId == 0) return false;
            var ledger = r.Assessments(); string key = "office:" + office.HolderId;
            if (ledger.HasMigrated(key)) return false;
            ledger.MarkMigrated(key);
            if (office.PendingKind == PendingEvent.Testing) office.PendingKind = 0;          // the old planned notice is no longer used: assessments start from each teacher's own schedule
            if (office.PayloadIds.Contains(EducationOfficeData.Id) && office.Education().LastTestingYear != 0) ledger.SetLastYear(office.HolderId, Math.Max(ledger.LastYearOf(office.HolderId), office.Education().LastTestingYear));
            if (office.PayloadIds.Contains(AssessmentData.Id))
            {
                var a = office.Assessment();
                if (a.TestingStartTick != 0 && a.TestIds.Count > 0 && ledger.Of(office.HolderId) == null)
                {
                    var s = new AssessmentSitting { Owner = office.HolderId, Year = ledger.LastYearOf(office.HolderId), StartTick = a.TestingStartTick, Slot = a.TestingSlot, Ids = a.TestIds.ToList(), Pass = a.TestPass.ToList(), Try = a.TestTry.ToList(), Score = a.TestScore.ToList(), Excused = a.TestExcused.ToList() };
                    s.Align(); ledger.Active.Add(s);
                }
            }
            return true;
        }
    }
}
