using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    public enum FavorState : byte { Owed, Called, Settled, Defaulted, Lapsed }

    /// <summary>
    /// A kindness one house really did for someone in another, remembered as something owed. It exists only because of a recorded gift, and it is
    /// settled only by a request the supply system really completed.
    /// </summary>
    public sealed partial class FavorDebt
    {
        public int Id, CreditorFamily, DebtorId, DebtorFamily, GiftIncidentId, CreatedTick, CalledTick, EndedTick, RequestId;
        public FavorState State;
        public bool Open => State == FavorState.Owed || State == FavorState.Called;
    }

    public sealed partial class FamilyRegistry
    {
        public const int FavorLifeTicks = 45 * ActivityTuning.TicksPerDay;
        public List<FavorDebt> Favors = new List<FavorDebt>();
        public int NextFavorId = 1;

        /// <summary>Notices gifts this house recently gave to people in other houses and remembers them as favors owed.</summary>
        public int RememberFavors(int family, int now)
        {
            int added = 0;
            foreach (var gift in Incidents.Where(i => i.Kind == IncidentKind.Gift && i.ActorFamily == family && i.VictimFamily != 0 && i.VictimFamily != family && i.Severity >= 8f && now - i.Tick < 20 * ActivityTuning.TicksPerDay)
                .OrderByDescending(i => i.Tick).Take(8).ToList())
            {
                if (Favors.Any(f => f.GiftIncidentId == gift.Id)) continue;
                if (GetPerson(gift.VictimId)?.IsAlive != true) continue;
                if (Favors.Count >= 64) { var old = Favors.FirstOrDefault(f => !f.Open); if (old == null) break; Favors.Remove(old); }
                Favors.Add(new FavorDebt { Id = NextFavorId++, CreditorFamily = family, DebtorId = gift.VictimId, DebtorFamily = gift.VictimFamily, GiftIncidentId = gift.Id, CreatedTick = now });
                added++;
            }
            return added;
        }

        public bool CallInFavor(int favorId, int requestId, int now)
        {
            var favor = Favors.FirstOrDefault(f => f.Id == favorId);
            var request = Requests.FirstOrDefault(r => r.Id == requestId);
            if (favor == null || favor.State != FavorState.Owed || request == null || FamilyOf(request.RequesterId) != favor.CreditorFamily || FamilyOf(request.DonorId) != favor.DebtorFamily) return false;
            favor.State = FavorState.Called; favor.CalledTick = now; favor.RequestId = requestId;
            return true;
        }

        /// <summary>
        /// Settles or defaults a called favor from how its request really ended. Honouring it deepens trust; refusing it costs the debtor
        /// the creditor's regard. Favors nobody called lapse on their own.
        /// </summary>
        public void MaintainFavors(int now)
        {
            foreach (var favor in Favors.Where(f => f.Open).ToList())
            {
                var request = favor.RequestId == 0 ? null : Requests.FirstOrDefault(r => r.Id == favor.RequestId);
                if (favor.State == FavorState.Called && request != null && !request.Open)
                {
                    bool honoured = request.State == SupplyState.Exchanged || request.State == SupplyState.Stored || request.State == SupplyState.Repaid;
                    favor.State = honoured ? FavorState.Settled : FavorState.Defaulted; favor.EndedTick = now;
                    int creditor = request.RequesterId, debtor = request.DonorId;
                    if (honoured) ChangeBond(creditor, debtor, new BondChange(1f, 2f, -1f, 1f), now);
                    else ChangeBond(creditor, debtor, new BondChange(-1f, -3f, 4f, 0f), now);
                }
                else if (now - favor.CreatedTick > FavorLifeTicks || GetPerson(favor.DebtorId)?.IsAlive != true) { favor.State = FavorState.Lapsed; favor.EndedTick = now; }
            }
        }
    }
}
