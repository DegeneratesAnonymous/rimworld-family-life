using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Framework;

namespace FamilyLife.Domain
{
    // Who is in which class, right now. Replaces the single class list that used to hang on the one Teacher office: every class is its own session, any number run at once, and nobody is
    // in two at a time. Nothing here is saved (a class under way is not resumed after a load). Owner: Education.

    public enum SessionRole : byte { Instructor, Assistant, Pupil }

    /// <summary>One class (or one-to-one lesson): who leads it, who assists, who learns, what and where. An assistant (a subject specialist, later) can take over from the instructor.</summary>
    public sealed class ClassSessionRecord
    {
        public int Instructor, StartTick;
        public string Subject, Location;
        public List<int> Assistants = new List<int>(), Pupils = new List<int>();
    }

    public sealed class ClassBoard
    {
        public const int MaxPupils = ClassRegistry.MaxClassSize;
        private readonly Dictionary<int, ClassSessionRecord> byInstructor = new Dictionary<int, ClassSessionRecord>();
        private readonly Dictionary<int, int> sessionOf = new Dictionary<int, int>();          // person -> instructor of the session they are in
        private readonly HashSet<string> places = new HashSet<string>();

        public IEnumerable<ClassSessionRecord> Sessions => byInstructor.Values;
        public ClassSessionRecord Of(int instructor) => byInstructor.TryGetValue(instructor, out var s) ? s : null;
        public bool IsBusy(int person) => sessionOf.ContainsKey(person);
        public bool PlaceBusy(string location) => !string.IsNullOrEmpty(location) && places.Contains(location);
        public ClassSessionRecord SessionOf(int person) => sessionOf.TryGetValue(person, out int i) ? Of(i) : null;

        /// <summary>
        /// Opens a session. All-or-nothing for the instructor and the place (an instructor already busy, or a place already in use, opens nothing); pupils who are already in another class,
        /// or who are themselves leading one, are simply left out. Returns the pupils actually seated, or null if the session could not open.
        /// </summary>
        public List<int> Open(int instructor, IEnumerable<int> pupils, string subject, string location, int now, IEnumerable<int> assistants = null)
        {
            if (instructor <= 0 || IsBusy(instructor) || PlaceBusy(location)) return null;
            var assist = (assistants ?? Enumerable.Empty<int>()).Distinct().Where(a => a != instructor && !IsBusy(a)).ToList();
            var seated = (pupils ?? Enumerable.Empty<int>()).Distinct().Where(p => p != instructor && !assist.Contains(p) && !IsBusy(p) && !byInstructor.ContainsKey(p)).Take(MaxPupils).ToList();
            var s = new ClassSessionRecord { Instructor = instructor, StartTick = now, Subject = subject, Location = location, Assistants = assist, Pupils = seated };
            byInstructor[instructor] = s; sessionOf[instructor] = instructor;
            foreach (int a in assist) sessionOf[a] = instructor;
            foreach (int p in seated) sessionOf[p] = instructor;
            if (!string.IsNullOrEmpty(location)) places.Add(location);
            return seated.ToList();
        }

        public void Close(int instructor)
        {
            if (!byInstructor.TryGetValue(instructor, out var s)) return;
            byInstructor.Remove(instructor);
            foreach (int id in new[] { instructor }.Concat(s.Assistants).Concat(s.Pupils)) if (sessionOf.TryGetValue(id, out int i) && i == instructor) sessionOf.Remove(id);
            if (!string.IsNullOrEmpty(s.Location)) places.Remove(s.Location);
        }

        /// <summary>A free person joins a running class to help. Refused if they are already in a class, the class is not running, or it already has <see cref="MaxAssistants"/>.</summary>
        public bool AddAssistant(int instructor, int person)
        {
            if (!byInstructor.TryGetValue(instructor, out var s) || person <= 0 || person == instructor || IsBusy(person) || byInstructor.ContainsKey(person) || s.Assistants.Count >= MaxAssistants) return false;
            s.Assistants.Add(person); sessionOf[person] = instructor; return true;
        }
        public const int MaxAssistants = 2;

        /// <summary>A pupil leaves the session (released, or gone). They are free to join another.</summary>
        public void Release(int pupil)
        {
            if (!sessionOf.TryGetValue(pupil, out int i) || i == pupil) return;
            var s = Of(i); s?.Pupils.Remove(pupil); s?.Assistants.Remove(pupil); sessionOf.Remove(pupil);
        }

        /// <summary>An assistant takes over a session whose instructor can no longer lead it. Returns false if they are not an assistant of it.</summary>
        public bool Substitute(int oldInstructor, int assistant)
        {
            if (!byInstructor.TryGetValue(oldInstructor, out var s) || !s.Assistants.Contains(assistant) || byInstructor.ContainsKey(assistant)) return false;
            byInstructor.Remove(oldInstructor); sessionOf.Remove(oldInstructor);
            s.Assistants.Remove(assistant); s.Instructor = assistant; byInstructor[assistant] = s;
            foreach (int p in s.Pupils.Concat(s.Assistants)) sessionOf[p] = assistant;
            sessionOf[assistant] = assistant;
            return true;
        }

        public void Clear() { byInstructor.Clear(); sessionOf.Clear(); places.Clear(); }
    }

    /// <summary>
    /// When each teacher next holds a class. It is the teacher's own, so each pace their own classes; it was a single date on the one Teacher post. Saved in a community-wide payload. A
    /// teacher with no entry starts from the old post's date if they held it, which is how an existing save carries on without a gap or a rush.
    /// </summary>
    public sealed class TeacherSchedule : IModuleState
    {
        public const string Id = "wils.education.schedule";
        private readonly Dictionary<int, int> next = new Dictionary<int, int>();

        public bool Has(int teacher) => next.ContainsKey(teacher);
        public int NextFor(int teacher) => next.TryGetValue(teacher, out int t) ? t : 0;
        public void Set(int teacher, int tick) { if (teacher > 0) next[teacher] = tick; }
        public void Seed(int teacher, int legacyTick) { if (teacher > 0 && !next.ContainsKey(teacher)) next[teacher] = Math.Max(0, legacyTick); }
        public bool Due(int teacher, int now) => now >= NextFor(teacher);
        public void Forget(int teacher) { next.Remove(teacher); }
        public IEnumerable<int> People => next.Keys;

        public string Encode() { var ids = next.Keys.OrderBy(i => i).ToList(); return new StateWriter().Ints("p", ids).Ints("t", ids.Select(i => next[i]).ToList()).ToString(); }
        public static TeacherSchedule Decode(StateReader r)
        {
            var s = new TeacherSchedule(); var p = r.Ints("p"); var t = r.Ints("t");
            for (int i = 0; i < p.Count && i < t.Count; i++) if (p[i] > 0) s.next[p[i]] = t[i];
            return s;
        }
    }

    public sealed class TeacherScheduleFactory : IStateFactory<TeacherSchedule>
    {
        public static readonly TeacherScheduleFactory Instance = new TeacherScheduleFactory();
        public int Version => 1;
        public TeacherSchedule Create() => new TeacherSchedule();
        public TeacherSchedule Decode(StateReader reader, int version) => TeacherSchedule.Decode(reader);
        public TeacherSchedule FromLegacy(StateReader legacy) => new TeacherSchedule();
    }

    public static class TeacherScheduleAccess
    {
        public static TeacherSchedule TeacherSchedule(this FamilyRegistry r) => r.Shared.Of(Domain.TeacherSchedule.Id, TeacherScheduleFactory.Instance);
    }
}
