using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Framework;

namespace FamilyLife.Domain
{
    // What the doctor's post remembers, in two payloads that live for different lengths of time. Neither is a field of the shared record. Owner: Community Care.
    //
    //   CareRecordData (wils.care.record) - the community's care record: the round in progress, who has been seen, who was missed, who declined and until when, the last outbreak.
    //   It belongs to the community, not to whoever holds the post: a new doctor inherits an outstanding round and everyone's refusals, and nobody is examined twice because the
    //   doctor changed.
    //
    //   CareOfficeData (wils.care) - the doctor's own working state: when they plan the next round, today's routine-visit budget, and the record of how they treated their own house
    //   and others. It belongs to the holder and starts fresh for the next one.

    /// <summary>The community's record of care. Outlives every doctor.</summary>
    public sealed class CareRecordData : IModuleState
    {
        public const string Id = "wils.care.record";

        public int RoundStartTick, LastIllnessTick;
        /// <summary>How long the current round stays open, in ticks; 0 in an older save, which means the original fourteen days.</summary>
        public int RoundWindow;
        /// <summary>The current round was called for an outbreak: the sick are seen first.</summary>
        public bool IllnessRound;
        /// <summary>People seen in the current round.</summary>
        public List<int> Examined = new List<int>();
        /// <summary>People the last round did not reach. They go first next time and are never marked as having failed.</summary>
        public List<int> Missed = new List<int>();
        /// <summary>People who declined this round, and when. A refusal is respected: they are not asked again for several days.</summary>
        public List<int> Refused = new List<int>(), RefusedTick = new List<int>();

        public string Encode() => new StateWriter().Int("roundStart", RoundStartTick).Int("lastIllness", LastIllnessTick).Int("roundWindow", RoundWindow).Bool("illnessRound", IllnessRound)
            .Ints("examined", Examined).Ints("missed", Missed).Ints("refused", Refused).Ints("refusedTick", RefusedTick).ToString();

        internal static CareRecordData Read(StateReader r)
        {
            var d = new CareRecordData
            {
                RoundStartTick = r.Int("roundStart"), LastIllnessTick = r.Int("lastIllness"), RoundWindow = r.Int("roundWindow"), IllnessRound = r.Bool("illnessRound"),
                Examined = r.Ints("examined"), Missed = r.Ints("missed"), Refused = r.Ints("refused"), RefusedTick = r.Ints("refusedTick")
            };
            while (d.RefusedTick.Count < d.Refused.Count) d.RefusedTick.Add(0);
            return d;
        }
    }

    /// <summary>The doctor's own working state. Starts fresh for each holder.</summary>
    public sealed class CareOfficeData : IModuleState
    {
        public const string Id = "wils.care";

        /// <summary>False until the new holder's first day: the first round is planned a day after they take the post.</summary>
        public bool Started;
        public int NextRoundTick;
        /// <summary>Routine visits made today (the daily budget) and when the last one was.</summary>
        public int RoutineDay, RoutineUsed, LastVisitTick;
        /// <summary>The favouritism record: the sum of check-up quality for people of the holder's house and for everyone else, with counts.</summary>
        public float KinQuality, OtherQuality;
        public int KinSeen, OtherSeen;

        public string Encode() => new StateWriter().Bool("started", Started).Int("nextRound", NextRoundTick).Int("routineDay", RoutineDay).Int("routineUsed", RoutineUsed).Int("lastVisit", LastVisitTick)
            .Float("kinQuality", KinQuality).Float("otherQuality", OtherQuality).Int("kinSeen", KinSeen).Int("otherSeen", OtherSeen).ToString();

        internal static CareOfficeData Read(StateReader r, bool started) => new CareOfficeData
        {
            Started = started, NextRoundTick = r.Int("nextRound"), RoutineDay = r.Int("routineDay"), RoutineUsed = r.Int("routineUsed"), LastVisitTick = r.Int("lastVisit"),
            KinQuality = r.Float("kinQuality"), OtherQuality = r.Float("otherQuality"), KinSeen = r.Int("kinSeen"), OtherSeen = r.Int("otherSeen")
        };
    }

    public sealed class CareStateFactory : IStateFactory<CareOfficeData>
    {
        public static readonly CareStateFactory Instance = new CareStateFactory();
        /// <summary>Version 2 holds only the holder's own part; version 1 (an interim format) held both, and is read for the keys that belong here.</summary>
        public int Version => 2;
        public CareOfficeData Create() => new CareOfficeData();
        public CareOfficeData Decode(StateReader reader, int version) => CareOfficeData.Read(reader, reader.Bool("started"));
        /// <summary>An older save wrote these directly on the post; it has already had its first day.</summary>
        public CareOfficeData FromLegacy(StateReader legacy) => CareOfficeData.Read(legacy, true);
    }

    public sealed class CareRecordFactory : IStateFactory<CareRecordData>
    {
        public static readonly CareRecordFactory Instance = new CareRecordFactory();
        public int Version => 1;
        public CareRecordData Create() => new CareRecordData();
        public CareRecordData Decode(StateReader reader, int version) => CareRecordData.Read(reader);
        public CareRecordData FromLegacy(StateReader legacy) => CareRecordData.Read(legacy);
    }

    /// <summary>
    /// The doctor's state as the module's code sees it: one object with the members it always had, each stored where its lifetime says (see the notes above). Cheap to create; read
    /// and write it through this and the right payload is updated.
    /// </summary>
    public sealed class CareView
    {
        private readonly CareOfficeData holder;
        private readonly CareRecordData record;
        internal CareView(CareOfficeData holder, CareRecordData record) { this.holder = holder; this.record = record; }

        // the community's record
        public int RoundStartTick { get => record.RoundStartTick; set => record.RoundStartTick = value; }
        public int LastIllnessTick { get => record.LastIllnessTick; set => record.LastIllnessTick = value; }
        public int RoundWindow { get => record.RoundWindow; set => record.RoundWindow = value; }
        public bool IllnessRound { get => record.IllnessRound; set => record.IllnessRound = value; }
        public List<int> Examined { get => record.Examined; set => record.Examined = value ?? new List<int>(); }
        public List<int> Missed { get => record.Missed; set => record.Missed = value ?? new List<int>(); }
        public List<int> Refused { get => record.Refused; set => record.Refused = value ?? new List<int>(); }
        public List<int> RefusedTick { get => record.RefusedTick; set => record.RefusedTick = value ?? new List<int>(); }

        // the doctor's own
        public bool Started { get => holder.Started; set => holder.Started = value; }
        public int NextRoundTick { get => holder.NextRoundTick; set => holder.NextRoundTick = value; }
        public int RoutineDay { get => holder.RoutineDay; set => holder.RoutineDay = value; }
        public int RoutineUsed { get => holder.RoutineUsed; set => holder.RoutineUsed = value; }
        public int LastVisitTick { get => holder.LastVisitTick; set => holder.LastVisitTick = value; }
        public float KinQuality { get => holder.KinQuality; set => holder.KinQuality = value; }
        public float OtherQuality { get => holder.OtherQuality; set => holder.OtherQuality = value; }
        public int KinSeen { get => holder.KinSeen; set => holder.KinSeen = value; }
        public int OtherSeen { get => holder.OtherSeen; set => holder.OtherSeen = value; }

        /// <summary>Both payloads as text, for comparing two states.</summary>
        public string Encode() => record.Encode() + "|" + holder.Encode();
    }

    public static class CareState
    {
        /// <summary>The community's care record on this post. Not reset when the doctor changes.</summary>
        public static CareRecordData CareRecord(this CommunityOffice office)
        {
            // An interim format (before the record was split out) kept everything in the holder's payload; read its record keys the first time.
            if (!office.PayloadIds.Contains(CareRecordData.Id) && !office.PayloadIds.Contains(LegacyOfficeMigration.PayloadId))
            {
                int old = office.PayloadIds.IndexOf(CareOfficeData.Id);
                if (old >= 0 && old < office.PayloadVersions.Count && office.PayloadVersions[old] == 1)
                {
                    var carried = CareRecordData.Read(new StateReader(office.PayloadData[old]));
                    office.PayloadIds.Add(CareRecordData.Id); office.PayloadHolders.Add(0); office.PayloadVersions.Add(1); office.PayloadData.Add(carried.Encode());
                }
            }
            return office.StateOf(CareRecordData.Id, false, CareRecordFactory.Instance);
        }

        /// <summary>The doctor's own working state on this post (fresh for a new holder).</summary>
        public static CareOfficeData CareHolder(this CommunityOffice office)
        {
            var d = office.StateOf(CareOfficeData.Id, true, CareStateFactory.Instance);
            // A new holder plans their first round a day after taking the post (the same moment the post's own record notes as when they were chosen).
            if (!d.Started) { d.Started = true; d.NextRoundTick = office.ElectedTick + ActivityTuning.TicksPerDay; }
            return d;
        }

        /// <summary>The doctor's state as one object; each part is stored where its lifetime says.</summary>
        public static CareView Care(this CommunityOffice office)
        {
            var holder = office.CareHolder();
            return new CareView(holder, office.CareRecord());
        }
    }
}
