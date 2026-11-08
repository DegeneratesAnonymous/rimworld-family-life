using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    public enum CampMotive : byte { Recreation, Reconciliation, Teaching, ComingOfAge, Tradition }
    public enum CampState : byte { Proposed, Authorized, Away, Completed, Declined, Expired, Cut }
    public enum CampMomentKind : byte { FiresideTalk, SharedMeal, Lesson, Exploration, Story, Argument, Mishap, Reconciliation, QuietSupport, Celebration }

    public sealed partial class CampMoment
    {
        public int ActorId, OtherId, Tick;
        public float Outcome;
        public CampMomentKind Kind;
    }

    /// <summary>
    /// A family's wish to spend a day or two away from the colony. It is only ever a proposal: the player decides, forms the caravan with the game's own
    /// screen, and the trip counts only if people really stay at the place for the whole time. Nothing here launches, moves or removes anyone.
    /// </summary>
    public sealed partial class CampTrip
    {
        public int Id, FamilyId, OrganizerId, ProposedTick, ExpiresTick, AuthorizedTick, ArrivedTick, EndedTick, Tile, StayTicks, Seed, SubjectId;
        public float Quality;
        public CampMotive Motive;
        public CampState State;
        public List<int> Suggested = new List<int>();     // who the family would like to bring; the player chooses the real caravan
        public List<int> Members = new List<int>();       // who actually stayed
        public List<CampMoment> Moments = new List<CampMoment>();
        public bool Open => State == CampState.Proposed || State == CampState.Authorized || State == CampState.Away;
    }

    /// <summary>What the game can say about people, weather and skill for a trip. All optional.</summary>
    public sealed class CampContext
    {
        public Func<int, bool> Available;       // free, able, not quest-locked or otherwise tied up
        public Func<int, bool> IsAvatar;        // a player-controlled pawn is never suggested
        public Func<int, float> Skill;          // outdoors skill, 0-1
        public Func<int, float[]> Profile;      // trait-derived dimensions; optional
        public float Weather;                   // -1 miserable to +1 lovely at the place
    }

    public sealed partial class FamilyRegistry
    {
        public const int CampProposalDays = 6, CampDeparture = 10, CampCooldownDays = 20, CampDeclinedCooldownDays = 30, MaxCampHistory = 24;
        public List<CampTrip> CampTrips = new List<CampTrip>();
        public int NextCampId = 1, CampCursor, LastCampScanTick = -1000000;

        public CampTrip CampTripFor(int id) => CampTrips.FirstOrDefault(c => c.Id == id);
        public CampTrip OpenCampFor(int family) => CampTrips.FirstOrDefault(c => c.FamilyId == family && c.Open);

        /// <summary>Whether this house has a real reason to want a trip right now, and which. Null when it does not.</summary>
        public CampMotive? AssessCamp(int family, int now, CampContext ctx = null)
        {
            ctx = ctx ?? new CampContext();
            var house = GetLineage(family);
            if (house == null || !IsActiveHouse(family) || OpenCampFor(family) != null) return null;
            var last = CampTrips.Where(c => c.FamilyId == family).OrderByDescending(c => c.ProposedTick).FirstOrDefault();
            if (last != null && now - last.ProposedTick < (last.State == CampState.Declined ? CampDeclinedCooldownDays : CampCooldownDays) * ActivityTuning.TicksPerDay) return null;
            var members = MembersOf(family).Where(m => m.InColony && (ctx.Available == null || ctx.Available(m.Id)) && !(ctx.IsAvatar != null && ctx.IsAvatar(m.Id))).ToList();
            var adults = members.Where(m => !m.Juvenile).ToList();
            if (adults.Count < 2 || members.Count < 2) return null;
            var ids = new HashSet<int>(members.Select(m => m.Id));
            if (Rifts.Any(r => r.IsOpen && ids.Contains(r.A) && ids.Contains(r.B))) return CampMotive.Reconciliation;
            if (Ceremonies.Any(c => c.Kind == CeremonyKind.ComingOfAge && c.LineageId == family && now - c.CompletedTick < 12 * ActivityTuning.TicksPerDay)) return CampMotive.ComingOfAge;
            if (members.Any(m => m.Juvenile) && adults.Any(a => (ctx.Skill == null ? a.TopSkillLevel / 20f : ctx.Skill(a.Id)) >= .5f)) return CampMotive.Teaching;
            if (house.CultureOf(Dim.Hearth) >= 15f) return CampMotive.Tradition;
            float pairs = 0f; int n = 0;
            foreach (var a in adults.Take(6)) foreach (var b in adults.Take(6)) if (a.Id < b.Id) { var bond = GetBond(a.Id, b.Id); pairs += ((bond?.Attachment ?? 0f) + (bond?.Trust ?? 0f)) / 2f; n++; }
            return n > 0 && pairs / n >= 15f ? CampMotive.Recreation : (CampMotive?)null;
        }

        /// <summary>Records the family's wish, with the place chosen by the game. Nobody has been asked to go and nothing has started.</summary>
        public CampTrip ProposeCamp(int family, CampMotive motive, int tile, int now, CampContext ctx = null)
        {
            ctx = ctx ?? new CampContext();
            if (tile < 0 || OpenCampFor(family) != null || !IsActiveHouse(family)) return null;
            var picks = MembersOf(family).Where(m => m.InColony && (ctx.Available == null || ctx.Available(m.Id)) && !(ctx.IsAvatar != null && ctx.IsAvatar(m.Id)))
                .OrderBy(m => m.Juvenile).ThenBy(m => m.BirthAbsTick).ThenBy(m => m.Id).Take(8).Select(m => m.Id).ToList();
            if (picks.Count < 2) return null;
            if (CampTrips.Count >= MaxCampHistory) { var old = CampTrips.FirstOrDefault(c => !c.Open); if (old == null) return null; CampTrips.Remove(old); }
            int seed = unchecked(family * 7919 ^ now / ActivityTuning.TicksPerDay * 104729 ^ tile) & int.MaxValue;
            var organizer = HeadOf(family);
            var trip = new CampTrip
            {
                Id = NextCampId++, FamilyId = family, OrganizerId = picks.Contains(organizer) ? organizer : picks[0], Motive = motive, State = CampState.Proposed,
                ProposedTick = now, ExpiresTick = now + CampProposalDays * ActivityTuning.TicksPerDay, Tile = tile, Seed = seed,
                StayTicks = ActivityTuning.TicksPerDay + seed % 3 * (ActivityTuning.TicksPerDay / 2), Suggested = picks
            };
            CampTrips.Add(trip);
            return trip;
        }

        /// <summary>The player agrees to the idea. They still choose who goes and send the caravan themselves.</summary>
        public bool AuthorizeCamp(int id, int now)
        {
            var trip = CampTripFor(id);
            if (trip == null || trip.State != CampState.Proposed || now > trip.ExpiresTick) return false;
            trip.State = CampState.Authorized; trip.AuthorizedTick = now; trip.ExpiresTick = now + CampDeparture * ActivityTuning.TicksPerDay;
            return true;
        }

        /// <summary>A refusal costs nothing but a longer wait before the family asks again.</summary>
        public bool DeclineCamp(int id, int now)
        {
            var trip = CampTripFor(id);
            if (trip == null || trip.State != CampState.Proposed) return false;
            trip.State = CampState.Declined; trip.EndedTick = now;
            return true;
        }

        public void MaintainCamps(int now)
        {
            foreach (var trip in CampTrips.Where(c => (c.State == CampState.Proposed || c.State == CampState.Authorized) && now > c.ExpiresTick).ToList())
            { trip.State = CampState.Expired; trip.EndedTick = now; }
        }

        /// <summary>
        /// The caravan has reached the place. The stay begins only for an authorized trip, and only with at least two people of the house really there.
        /// </summary>
        public bool BeginCampStay(int id, IEnumerable<int> present, int now)
        {
            var trip = CampTripFor(id);
            if (trip == null || trip.State != CampState.Authorized || now > trip.ExpiresTick) return false;
            var here = (present ?? Enumerable.Empty<int>()).Distinct().Where(p => GetPerson(p)?.InColony == true).ToList();
            if (here.Count(p => BelongsToFamily(GetPerson(p), trip.FamilyId)) < 2) return false;
            trip.State = CampState.Away; trip.ArrivedTick = now; trip.Members = here.Take(12).ToList();
            return true;
        }

        /// <summary>
        /// One check of a trip under way. Leaving early, or being left with fewer than two, ends it with nothing gained; staying the whole time completes
        /// it. Returns true once the trip is over, either way.
        /// </summary>
        public bool AdvanceCamp(int id, IEnumerable<int> presentNow, int now, CampContext ctx = null)
        {
            var trip = CampTripFor(id);
            if (trip == null || trip.State != CampState.Away) return trip != null && !trip.Open;
            var here = new HashSet<int>((presentNow ?? Enumerable.Empty<int>()).Where(p => trip.Members.Contains(p)));
            if (here.Count < 2) { trip.State = CampState.Cut; trip.EndedTick = now; return true; }
            if (now - trip.ArrivedTick < trip.StayTicks) return false;
            CompleteCamp(trip, here.ToList(), now, ctx ?? new CampContext());
            return true;
        }

        private void CompleteCamp(CampTrip trip, List<int> stayed, int now, CampContext ctx)
        {
            trip.Members = stayed; trip.State = CampState.Completed; trip.EndedTick = now;
            var pairs = new List<KeyValuePair<int, int>>();
            foreach (int a in stayed) foreach (int b in stayed) if (a < b && pairs.Count < 28) pairs.Add(new KeyValuePair<int, int>(a, b));
            float regard = pairs.Count == 0 ? 0f : pairs.Average(p => (((GetBond(p.Key, p.Value)?.Attachment ?? 0f) + (GetBond(p.Key, p.Value)?.Trust ?? 0f)) / 200f));
            float tension = pairs.Count == 0 ? 0f : pairs.Average(p => (GetBond(p.Key, p.Value)?.Tension ?? 0f) / 100f);
            float skill = stayed.Max(p => Math.Max(0f, Math.Min(1f, ctx.Skill == null ? (GetPerson(p)?.TopSkillLevel ?? 0) / 20f : ctx.Skill(p))));
            float disposition = CampDisposition(trip.Motive, stayed, ctx);
            int troubles = Incidents.Count(i => i.Tick >= trip.ArrivedTick && i.Kind != IncidentKind.Gift && i.Kind != IncidentKind.Restitution && stayed.Contains(i.ActorId) && stayed.Contains(i.VictimId));
            float quality = .3f + regard * .35f - tension * .25f + Math.Max(-1f, Math.Min(1f, ctx.Weather)) * .15f + skill * .15f + disposition * .1f - Math.Min(.5f, troubles * .25f);
            quality = Math.Max(-1f, Math.Min(1f, quality));
            trip.Quality = quality;
            trip.Moments = BuildCampMoments(trip, stayed, now, quality, tension, ctx);
            foreach (var p in pairs)
            {
                ChangeBond(p.Key, p.Value, new BondChange(2f * quality, 1.5f * quality, -2f * quality, 2f), now);
                ChangeBond(p.Value, p.Key, new BondChange(2f * quality, 1.5f * quality, -2f * quality, 2f), now);
                bool teaching = trip.Motive == CampMotive.Teaching && (GetPerson(p.Key)?.Juvenile != GetPerson(p.Value)?.Juvenile);
                if (quality > 0f) RecordTogether(p.Key, p.Value, Math.Min(trip.StayTicks, 12000), now, teaching ? BondCause.Teaching : BondCause.Together);   // time spent badly is not time well spent
            }
            if (trip.Motive == CampMotive.Reconciliation && quality > .2f)
                foreach (var rift in Rifts.Where(r => r.IsOpen && stayed.Contains(r.A) && stayed.Contains(r.B))) rift.Severity = Math.Max(0f, rift.Severity - 20f * quality);
            var house = GetLineage(trip.FamilyId);
            if (house != null && trip.Motive == CampMotive.Tradition) house.Standing = Math.Min(100f, house.Standing + Math.Max(0f, quality));
        }

        private static float CampDisposition(CampMotive motive, List<int> stayed, CampContext ctx)
        {
            if (ctx.Profile == null || stayed.Count == 0) return 0f;
            float total = 0f; int n = 0;
            foreach (int id in stayed)
            {
                var p = ctx.Profile(id);
                if (p == null || p.Length < DimInfo.Count) continue;
                float fit;
                switch (motive)
                {
                    case CampMotive.Reconciliation: fit = (p[(int)Dim.Warmth] + p[(int)Dim.Steadiness]) / 6f; break;
                    case CampMotive.Teaching: fit = (p[(int)Dim.Scholarly] + p[(int)Dim.Devotion]) / 6f; break;
                    case CampMotive.ComingOfAge: fit = (p[(int)Dim.Devotion] + p[(int)Dim.Revelry]) / 6f; break;
                    case CampMotive.Tradition: fit = (p[(int)Dim.Rootedness] + p[(int)Dim.Hearth]) / 6f; break;
                    default: fit = (p[(int)Dim.Warmth] + p[(int)Dim.Revelry]) / 6f; break;
                }
                total += Math.Max(-1f, Math.Min(1f, fit)); n++;
            }
            return n == 0 ? 0f : total / n;
        }

        private List<CampMoment> BuildCampMoments(CampTrip trip, List<int> stayed, int now, float quality, float tension, CampContext ctx)
        {
            var moments = new List<CampMoment>();
            var pairs = new List<KeyValuePair<int, int>>();
            foreach (int a in stayed.OrderBy(x => x)) foreach (int b in stayed.OrderBy(x => x)) if (a < b) pairs.Add(new KeyValuePair<int, int>(a, b));
            int count = Math.Min(4, Math.Max(1, pairs.Count));
            for (int i = 0; i < count; i++)
            {
                var pair = pairs[(trip.Seed + i * 17) % pairs.Count];
                bool youngPair = GetPerson(pair.Key)?.Juvenile != GetPerson(pair.Value)?.Juvenile;
                CampMomentKind kind;
                if (quality < -.15f && (tension > .2f || i == 0)) kind = CampMomentKind.Argument;
                else if (ctx.Weather < -.5f && i == 0) kind = quality > 0f ? CampMomentKind.QuietSupport : CampMomentKind.Mishap;
                else if (trip.Motive == CampMotive.Reconciliation && i == 0) kind = quality > 0f ? CampMomentKind.Reconciliation : CampMomentKind.Argument;
                else if (trip.Motive == CampMotive.Teaching && youngPair) kind = CampMomentKind.Lesson;
                else if (trip.Motive == CampMotive.ComingOfAge && i == 0) kind = CampMomentKind.Celebration;
                else
                {
                    var calm = new[] { CampMomentKind.FiresideTalk, CampMomentKind.SharedMeal, CampMomentKind.Exploration, CampMomentKind.Story };
                    kind = calm[(trip.Seed + i) % calm.Length];
                }
                moments.Add(new CampMoment { ActorId = pair.Key, OtherId = pair.Value, Tick = trip.ArrivedTick + (i + 1) * trip.StayTicks / (count + 1), Kind = kind, Outcome = quality });
            }
            return moments;
        }
    }
}
