using System;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using RimWorld;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// Education's career: Tutor (anyone), Teacher (qualified), and, later, subject specialists. The thresholds are plain constants so they are easy to tune; they are the preliminary
    /// balance values listed in docs/CAREERS.md. Owner: Education.
    /// </summary>
    internal static class EducationCareers
    {
        public const string Career = "wils.education.teaching";
        public const string Tutor = "wils.education.tutor", Teacher = "wils.education.teacher";
        /// <summary>The twelve vanilla skills a subject specialist can be qualified in (skill def names).</summary>
        public static readonly string[] Subjects = { "Shooting", "Melee", "Construction", "Mining", "Cooking", "Plants", "Animals", "Crafting", "Artistic", "Medicine", "Social", "Intellectual" };
        public static string SpecialistRole(string subject) => "wils.education.specialist." + subject.ToLowerInvariant();
        /// <summary>Subject specialist: a Teacher who also has real mastery (skill 12) and has taught the subject a good deal (150 points of that subject, 400 overall). Preliminary balance values.</summary>
        public const int SpecialistSkill = 12, SpecialistSubjectExperience = 150, SpecialistExperience = 400;

        /// <summary>Teacher: enough social skill to hold a room, and about a dozen real lessons or classes of experience. No single subject is required: teaching is not only Intellectual.</summary>
        public const int TeacherSocial = 4, TeacherExperience = 120;
        /// <summary>Experience for one pupil's finished class (scaled by attendance and delivery) or one-to-one lesson.</summary>
        public const int ClassPoints = 10, TutoringPoints = 8, AssistPoints = 6;
        /// <summary>A specialist who helps for at least half the instruction raises the class delivery by this much, once, whatever the number of helpers.</summary>
        public const float AssistBonus = 1.10f;
        /// <summary>A tutor in the room lifts pupil engagement by this much, once, whatever the number of tutors. Distinct from the specialist delivery bonus.</summary>
        public const float TutorEngagement = .4f;
        /// <summary>A tutor gives a child one lesson at most this often (ticks): about two game hours of rest between lessons.</summary>
        public const int TutorEvery = 5000;

        public static void Register(string moduleId)
        {
            CareerService.RegisterCareer(new CareerDefinition { Id = Career, ModuleId = moduleId, Level2 = new CareerRule { GeneralXp = TeacherExperience }.Skill("Social", TeacherSocial) });
            CareerService.RegisterRole(new ProfessionRole { Id = Tutor, ModuleId = moduleId, Career = Career, Level = 1 });
            CareerService.RegisterRole(new ProfessionRole { Id = Teacher, ModuleId = moduleId, Career = Career, Level = 2 });
            var def = CareerService.Career(Career);
            foreach (string subject in Subjects)
            {
                def.Specialties[subject] = new CareerRule { GeneralXp = SpecialistExperience, SpecialtyXp = SpecialistSubjectExperience }.Skill(subject, SpecialistSkill).Skill("Social", TeacherSocial);
                CareerService.RegisterRole(new ProfessionRole { Id = SpecialistRole(subject), ModuleId = moduleId, Career = Career, Level = 3, Specialty = subject, BonusKey="WFL_Education_SpecialistBonus" });
            }
            CareerService.RegisterSeat(moduleId, OfficeKind.Teacher, Teacher);
            CareerService.Registered(moduleId);
            FamilyFramework.Roster.Register(new WorkItem { Id = moduleId + ".classes", ModuleId = moduleId, Interval = 600, Run = now => { var c = FamilyComponent.Current; if (c != null) EducationDriver.ClassWork(c, now); } });
            FamilyFramework.Roster.Register(new WorkItem { Id = moduleId + ".tutors", ModuleId = moduleId, Interval = 1200, Run = now => { var c = FamilyComponent.Current; if (c != null) TutorWork(c, now); } });
        }

        /// <summary>The subject a specialist-only instructor teaches (their first specialist assignment), or null if they are assigned as a Teacher or hold none.</summary>
        internal static string FocusSubject(FamilyRegistry r, int id)
        {
            var book = r.Assignments(); if (book.IsAssigned(id, Teacher)) return null;
            return Subjects.FirstOrDefault(s => book.IsAssigned(id, SpecialistRole(s)));
        }

        /// <summary>Everyone who instructs classes: assigned Teachers and subject specialists (a specialist gives focused classes in their subject and covers for an absent Teacher).</summary>
        internal static System.Collections.Generic.List<int> Instructors(FamilyRegistry r)
        {
            var book = r.Assignments();
            return book.Holders(Teacher).Concat(Subjects.SelectMany(s => book.Holders(SpecialistRole(s)))).Distinct().Where(id => r.GetPerson(id)?.InColony == true).ToList();
        }

        /// <summary>Experience for a pupil's finished class or lesson, with the subject as the specialty so subject experience is banked for later.</summary>
        internal static void Report(FamilyComponent c, Pawn teacher, Pawn pupil, SkillDef skill, string kind, float share, float delivery, int now)
        {
            int points = Math.Max(1, (int)Math.Round((kind == "tutor" ? TutoringPoints : ClassPoints) * Math.Min(1f, share) * Math.Max(.4f, Math.Min(1.2f, delivery))));
            CareerService.Report(c, teacher, Career, skill?.defName, "teach." + kind, kind + "|" + pupil.thingIDNumber + "|" + now, points, now);
        }

        /// <summary>
        /// Tutors give one-to-one lessons: a free, assigned tutor offers an idle child a subject the tutor is clearly better at than the child (the same gap rule classes use, so nobody
        /// tutors what they know less well). One lesson at a time, spaced out, and never while the tutor is teaching a class.
        /// </summary>
        internal static void TutorWork(FamilyComponent c, int now)
        {
            var r = c.Registry; var s = FamilyLifeMod.Settings;
            if (s != null && !s.holdClasses) return;
            foreach (int id in r.Assignments().Holders(Tutor).ToList())
                if (r.GetPerson(id)?.InColony == true && c.TryGetPawn(id) is Pawn tutor) OneToOne(c, tutor, now, s != null && s.adultStudents);
        }

        /// <summary>
        /// One instructor offers one idle child a lesson in a subject they are clearly better at. Used by tutors, and by a teacher who has no classroom. Spaced out per instructor, and never
        /// while they are in a class (or the child is).
        /// </summary>
        internal static void OneToOne(FamilyComponent c, Pawn tutor, int now, bool adults)
        {
            var r = c.Registry; int id = tutor.thingIDNumber;
            if (!tutor.Spawned || tutor.Dead || ClassSessions.Running(tutor) || ClassSessions.Board.IsBusy(id) || !FrameworkServices.Ready(tutor)) return;
            if (FrameworkServices.NeedsReason(tutor) != null || !Due("tutor" + id, now)) return;
            var pupil = r.Persons.Where(p => p.InColony && p.Id != id && (p.Juvenile || adults) && !ClassSessions.Board.IsBusy(p.Id) && c.TryGetPawn(p.Id) is Pawn k && k.Spawned && !k.Dead && k.Map == tutor.Map && FrameworkServices.FreeFor(c, p.Id) && EducationDriver.LearnableSkill(tutor, k) != null)
                .OrderBy(p => (p.Id * 31 + now / 5000) % 97).Select(p => c.TryGetPawn(p.Id)).FirstOrDefault();
            if (pupil != null) FrameworkServices.StartMeeting(tutor, pupil, EducationDefOf.WFL_OfficeLesson, 0);
        }

        private static readonly System.Collections.Generic.Dictionary<string, int> last = new System.Collections.Generic.Dictionary<string, int>();
        private static bool Due(string key, int now) { if (last.TryGetValue(key, out int t) && now >= t && now - t < TutorEvery) return false; last[key] = now; return true; }

        /// <summary>A finished tutoring lesson: the same experience path a class uses, for the time actually spent.</summary>
        internal static void CompleteTutoring(FamilyRegistry r, Pawn tutor, Pawn pupil, int now)
        {
            var skill = EducationDriver.LearnableSkill(tutor, pupil); if (skill == null) return;
            float delivery = LessonMath.Delivery(PawnReadinessApi.Level(tutor, skill), PawnReadinessApi.Level(tutor, SkillDefOf.Social), 0f, 0f, PawnReadinessApi.Fatigue(tutor));
            var talk = FrameworkServices.RunConversation(tutor, pupil, OfficeKind.Teacher, skill, ExchangeTopic.Explain, tutor.thingIDNumber + pupil.thingIDNumber + now);
            LessonGrant.Apply(FamilyComponent.Current, r, tutor, pupil, skill, Math.Min(1f, EducationDriver.LessonTicks / (float)ClassSchedule.InstructionTicks), EducationDriver.LessonTicks, EducationDriver.LessonTicks, delivery, talk.Quality, 1, null, now, "tutor");
        }
    }
}
