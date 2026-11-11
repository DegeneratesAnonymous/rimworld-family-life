using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    /// <summary>Someone who left a household in a reconciliation sweep. <see cref="DestHousehold"/> is the household they joined in the same sweep (0 if none); <see cref="Away"/> means they are only absent (a caravan, a trip), not gone.</summary>
    public sealed class Leaver { public int Pawn, DestHousehold; public bool Away; }

    /// <summary>
    /// Who keeps what when a household changes. Personal funds always stay with their owner (a personal account never moves). Household funds and debts follow explicit rules:
    /// <list type="bullet">
    /// <item>Someone only being <b>away</b> (caravan, trip) moves nothing, and a household whose members are all away keeps its account for them.</item>
    /// <item>A person who <b>moves out</b> of a household that carries on (growing up, a roommate, a parent taking a job elsewhere) takes none of the household's money or debt: those belong to the household.</item>
    /// <item>A <b>divorce or separation</b> from someone who stays gives the leaver, and any dependents leaving with them, an equal per-person share of the household's silver and debts.</item>
    /// <item>A household that <b>dissolves</b> because everyone left (marriage into another household, everyone moving on) divides per person among those who joined other households; the share of anyone merely away stays in the old account for when they return.</item>
    /// <item>On <b>return</b>, a former member who joins a household while their old household is gone brings its remaining account into the new household.</item>
    /// </list>
    /// Every transfer is a share of what is there: the totals of silver and of liabilities never change, and each step has its own key, so repeating the reconciliation changes nothing.
    /// </summary>
    public static class HouseholdRules
    {
        public static string Account(int household) => Ledger.IdFor(AccountKind.Household, household);

        private static string Destination(Ledger l, Leaver x)
        {
            if (x.DestHousehold > 0) { l.Ensure(AccountKind.Household, x.DestHousehold); return Account(x.DestHousehold); }
            l.Ensure(AccountKind.Person, x.Pawn); return Ledger.IdFor(AccountKind.Person, x.Pawn);
        }

        /// <summary>Applies the rules for one household in one sweep. <paramref name="remaining"/> is how many living residents it still has. Returns the number of transfers made.</summary>
        public static int OnLeave(Ledger l, int oldHousehold, int remaining, IList<Leaver> leavers, bool divorce, string keyBase, int day)
        {
            var from = l.Get(Account(oldHousehold)); if (from == null || from.Closed || leavers == null || leavers.Count == 0) return 0;
            var ordered = leavers.OrderBy(x => x.Pawn).ToList(); int made = 0;
            if (remaining > 0)
            {
                if (!divorce) return 0;                                                   // moving out of a household that carries on takes nothing from it
                var going = ordered.Where(x => !x.Away).ToList();
                for (int i = 0; i < going.Count; i++)
                    if (l.TransferShare(from.Id, Destination(l, going[i]), 1, remaining + going.Count - i, keyBase + ":" + going[i].Pawn, day) == LedgerResult.Ok) made++;
                return made;
            }
            for (int i = 0; i < ordered.Count; i++)                                       // dissolved: per person among the leavers; the away keep their share where it is
            {
                if (ordered[i].Away) continue;
                if (l.TransferShare(from.Id, Destination(l, ordered[i]), 1, ordered.Count - i, keyBase + ":" + ordered[i].Pawn, day) == LedgerResult.Ok) made++;
            }
            return made;
        }

        /// <summary>A former member joins a household after their old household has gone: what remained in the old account (their share and any other away member's) comes with them.</summary>
        public static bool OnReturn(Ledger l, int oldHousehold, int newHousehold, bool oldStillHasResidents, string key, int day)
        {
            if (oldHousehold <= 0 || newHousehold <= 0 || oldHousehold == newHousehold || oldStillHasResidents) return false;
            var old = l.Get(Account(oldHousehold)); if (old == null || old.Closed) return false;
            l.Ensure(AccountKind.Household, newHousehold);
            return l.TransferShare(old.Id, Account(newHousehold), 1, 1, key, day) == LedgerResult.Ok;
        }
    }
}
