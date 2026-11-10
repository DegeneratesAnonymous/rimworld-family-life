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
        /// <summary>
        /// Who declined, of whom, why, and until when. A refusal is respected until it expires. A <see cref="CareRefusalKind.HealerSpecific"/> one concerns the doctor who was refused
        /// and lapses when the post changes hands; a <see cref="CareRefusalKind.ServiceWide"/> one is a refusal of check-ups themselves and outlives any doctor.
        /// </summary>
        public List<CareRefusal> Refusals = new List<CareRefusal>();

        public string Encode()
        {
            var w = new StateWriter().Int("roundStart", RoundStartTick).Int("lastIllness", LastIllnessTick).Int("roundWindow", RoundWindow).Bool("illnessRound", IllnessRound)
                .Ints("examined", Examined).Ints("missed", Missed);
            // Version 2: structured refusals, as parallel lists (the format has no nesting). The reason is always non-empty so the lists stay aligned.
            return w.Ints("refPatient", Refusals.Select(x => x.Patient).ToList()).Ints("refHealer", Refusals.Select(x => x.Healer).ToList())
                .Ints("refKind", Refusals.Select(x => (int)x.Kind).ToList()).Strings("refReason", Refusals.Select(x => string.IsNullOrEmpty(x.Reason) ? CareRefusalReason.Unspecified : x.Reason).ToList())
                .Ints("refTick", Refusals.Select(x => x.Tick).ToList()).Ints("refUntil", Refusals.Select(x => x.Until).ToList()).ToString();
        }

        internal static CareRecordData Read(StateReader r)
        {
            var d = new CareRecordData
            {
                RoundStartTick = r.Int("roundStart"), LastIllnessTick = r.Int("lastIllness"), RoundWindow = r.Int("roundWindow"), IllnessRound = r.Bool("illnessRound"),
                Examined = r.Ints("examined"), Missed = r.Ints("missed")
            };
            // Version 2
            var patient = r.Ints("refPatient"); var healer = r.Ints("refHealer"); var kind = r.Ints("refKind"); var reason = r.Strings("refReason"); var tick = r.Ints("refTick"); var until = r.Ints("refUntil");
            for (int i = 0; i < patient.Count; i++)
            {
                if (patient[i] <= 0 || d.Refusals.Any(x => x.Patient == patient[i] && x.Kind == KindAt(kind, i) && x.Healer == At(healer, i))) continue;
                int t = At(tick, i);
                d.Refusals.Add(new CareRefusal
                {
                    Patient = patient[i], Healer = At(healer, i), Kind = KindAt(kind, i), Reason = i < reason.Count && reason[i].Length > 0 ? reason[i] : CareRefusalReason.Unspecified,
                    Tick = t, Until = i < until.Count ? until[i] : t + AppointmentBackoff.RefusalDays * ActivityTuning.TicksPerDay
                });
            }
            // Version 1 and the all-in-one mod wrote only the patient and the tick: a refusal of check-ups, with no record of which doctor. Kept as exactly that (same cooldown), once.
            var oldPatient = r.Ints("refused"); var oldTick = r.Ints("refusedTick");
            for (int i = 0; i < oldPatient.Count; i++)
            {
                if (oldPatient[i] <= 0 || d.Refusals.Any(x => x.Patient == oldPatient[i] && x.Kind == CareRefusalKind.ServiceWide)) continue;
                int t = i < oldTick.Count ? oldTick[i] : 0;
                d.Refusals.Add(new CareRefusal { Patient = oldPatient[i], Healer = 0, Kind = CareRefusalKind.ServiceWide, Reason = CareRefusalReason.Legacy, Tick = t, Until = t + AppointmentBackoff.RefusalDays * ActivityTuning.TicksPerDay });
            }
            return d;
        }

        private static int At(List<int> list, int i) => i < list.Count ? list[i] : 0;
        /// <summary>An unrecognised kind (written by a newer version) is treated as the more cautious one: a refusal of the service, which is respected whoever the doctor is.</summary>
        private static CareRefusalKind KindAt(List<int> list, int i) => i < list.Count && list[i] == (int)CareRefusalKind.HealerSpecific ? CareRefusalKind.HealerSpecific : CareRefusalKind.ServiceWide;
    }

    /// <summary>What a refusal is about. Saved as a number; never inferred later from what else is on the record.</summary>
    public enum CareRefusalKind : byte
    {
        /// <summary>The patient would not see this doctor. It concerns that doctor only and lapses when someone else takes the post.</summary>
        HealerSpecific = 1,
        /// <summary>The patient declined check-ups generally. It stands whoever the doctor is, until it expires.</summary>
        ServiceWide = 2
    }

    /// <summary>Stable reason codes (data, not display text) for why someone declined.</summary>
    public static class CareRefusalReason
    {
        public const string ReluctantWithHealer = "reluctant_with_healer";          // how they feel about this doctor (tension, little trust or attachment)
        public const string LowMood = "low_mood";          // in too poor a mood for anyone's check-up
        public const string Legacy = "legacy_declined";          // read from a save that recorded only the patient
        public const string Unspecified = "unspecified";
    }

    public sealed class CareRefusal
    {
        public int Patient;
        /// <summary>The doctor who was asking. Meaningful for <see cref="CareRefusalKind.HealerSpecific"/>; for a service-wide refusal it is who happened to ask (0 if unknown).</summary>
        public int Healer;
        public CareRefusalKind Kind;
        public string Reason = CareRefusalReason.Unspecified;
        public int Tick, Until;

        /// <summary>Still being respected at this time (a clock that has gone backwards counts as not respected, as before).</summary>
        public bool ActiveAt(int now) => now >= Tick && now < Until;
        /// <summary>Whether it applies when <paramref name="healer"/> holds the post.</summary>
        public bool AppliesTo(int healer) => Kind == CareRefusalKind.ServiceWide || (Healer != 0 && Healer == healer);
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
        /// <summary>Version 2: refusals are structured (patient, doctor, kind, reason, tick, expiry). Version 1 held only patient and tick; it is read as a refusal of check-ups generally.</summary>
        public int Version => 2;
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
        public List<CareRefusal> Refusals { get => record.Refusals; set => record.Refusals = value ?? new List<CareRefusal>(); }

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
