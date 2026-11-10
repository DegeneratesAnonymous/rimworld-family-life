using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Framework;

namespace FamilyLife.Domain
{
    // What the doctor's post remembers: the round in progress, who was seen, missed or declined, the day's visits and how people were treated. It is stored on the post as this module's
    // own payload (not as fields of the shared record), starts fresh for each new holder, and survives the module being absent. Owner: Community Care.

    public sealed class CareOfficeData : IModuleState
    {
        public const string Id = "wils.care";

        /// <summary>False until the new holder's first day: the first round is planned a day after they take the post.</summary>
        public bool Started;
        public int NextRoundTick, RoundStartTick, LastIllnessTick;
        /// <summary>How long the current round stays open, in ticks; 0 in an older save, which means the original fourteen days.</summary>
        public int RoundWindow;
        /// <summary>The current round was called for an outbreak: the sick are seen first.</summary>
        public bool IllnessRound;
        /// <summary>People the doctor has seen in the current round.</summary>
        public List<int> Examined = new List<int>();
        /// <summary>People the last round did not reach. They go first next time and are never marked as having failed.</summary>
        public List<int> Missed = new List<int>();
        /// <summary>People who declined this round, and when. A refusal is respected: they are not asked again for several days.</summary>
        public List<int> Refused = new List<int>(), RefusedTick = new List<int>();
        /// <summary>Routine visits made today (the daily budget) and when the last one was.</summary>
        public int RoutineDay, RoutineUsed, LastVisitTick;
        /// <summary>The favouritism record: the sum of check-up quality for people of the holder's house and for everyone else, with counts.</summary>
        public float KinQuality, OtherQuality;
        public int KinSeen, OtherSeen;

        public string Encode() => new StateWriter()
            .Bool("started", Started).Int("nextRound", NextRoundTick).Int("roundStart", RoundStartTick).Int("lastIllness", LastIllnessTick).Int("roundWindow", RoundWindow)
            .Bool("illnessRound", IllnessRound).Ints("examined", Examined).Ints("missed", Missed).Ints("refused", Refused).Ints("refusedTick", RefusedTick)
            .Int("routineDay", RoutineDay).Int("routineUsed", RoutineUsed).Int("lastVisit", LastVisitTick)
            .Float("kinQuality", KinQuality).Float("otherQuality", OtherQuality).Int("kinSeen", KinSeen).Int("otherSeen", OtherSeen).ToString();

        internal static CareOfficeData Read(StateReader r, bool started)
        {
            var d = new CareOfficeData
            {
                Started = started, NextRoundTick = r.Int("nextRound"), RoundStartTick = r.Int("roundStart"), LastIllnessTick = r.Int("lastIllness"), RoundWindow = r.Int("roundWindow"),
                IllnessRound = r.Bool("illnessRound"), Examined = r.Ints("examined"), Missed = r.Ints("missed"), Refused = r.Ints("refused"), RefusedTick = r.Ints("refusedTick"),
                RoutineDay = r.Int("routineDay"), RoutineUsed = r.Int("routineUsed"), LastVisitTick = r.Int("lastVisit"),
                KinQuality = r.Float("kinQuality"), OtherQuality = r.Float("otherQuality"), KinSeen = r.Int("kinSeen"), OtherSeen = r.Int("otherSeen")
            };
            while (d.RefusedTick.Count < d.Refused.Count) d.RefusedTick.Add(0);
            return d;
        }
    }

    public sealed class CareStateFactory : IStateFactory<CareOfficeData>
    {
        public static readonly CareStateFactory Instance = new CareStateFactory();
        public int Version => 1;
        public CareOfficeData Create() => new CareOfficeData();
        public CareOfficeData Decode(StateReader reader, int version) => CareOfficeData.Read(reader, reader.Bool("started"));
        /// <summary>An older save wrote these directly on the post; it has already had its first day.</summary>
        public CareOfficeData FromLegacy(StateReader legacy) => CareOfficeData.Read(legacy, true);
    }

    public static class CareState
    {
        /// <summary>The doctor's state on this post (fresh for a new holder).</summary>
        public static CareOfficeData Care(this CommunityOffice office)
        {
            var d = office.StateOf(CareOfficeData.Id, true, CareStateFactory.Instance);
            // A new holder plans their first round a day after taking the post (the same moment the post's own record notes as when they were chosen).
            if (!d.Started) { d.Started = true; d.NextRoundTick = office.ElectedTick + ActivityTuning.TicksPerDay; }
            return d;
        }
    }
}
