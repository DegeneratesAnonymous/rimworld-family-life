using System;
using System.Linq;

namespace FamilyLife.Domain
{
    public enum ComfortResult : byte { Failed, Eased, Resolved }

    public sealed partial class FamilyRegistry
    {
        public const float ComfortEasedAt = .4f, ComfortResolvedAt = .65f, ComfortWillTry = .3f;

        /// <summary>Who may try to talk someone through a bad moment: their partner, or their parent (a parent of a child always; of a grown child only if close).</summary>
        public bool CanComfort(int helper, int sufferer)
        {
            var h = GetPerson(helper); var s = GetPerson(sufferer);
            if (h == null || s == null || helper == sufferer || h.Presence != Presence.Colony || s.Presence != Presence.Colony) return false;
            if (s.PartnerIds().Contains(helper)) return true;
            return s.ParentIds.Contains(helper) && (s.Juvenile || (GetBond(sufferer, helper)?.Attachment ?? 0f) >= 40f);
        }

        /// <summary>
        /// How well this person's words will land, 0 to 1. It rests on what the sufferer feels for them (affection and trust, held back by friction),
        /// how good the helper is with people, and that a partner or a parent of a child is who they most want. No dice: the game adds a little chance.
        /// </summary>
        public float ComfortQuality(int helper, int sufferer, float helperSocial01)
        {
            var bond = GetBond(sufferer, helper);
            float attachment = Math.Max(-100f, Math.Min(100f, bond?.Attachment ?? 0f)) / 100f;
            float trust = Math.Max(-100f, Math.Min(100f, bond?.Trust ?? 0f)) / 100f;
            float tension = Math.Max(0f, Math.Min(100f, bond?.Tension ?? 0f)) / 100f;
            var s = GetPerson(sufferer);
            float kin = s == null ? 0f : s.PartnerIds().Contains(helper) ? .1f : s.ParentIds.Contains(helper) && s.Juvenile ? .1f : 0f;
            float q = .2f + attachment * .35f + trust * .25f - tension * .2f + Math.Max(0f, Math.Min(1f, helperSocial01)) * .2f + kin;
            return Math.Max(0f, Math.Min(1f, q));
        }

        public static ComfortResult ClassifyComfort(float quality) => quality >= ComfortResolvedAt ? ComfortResult.Resolved : quality >= ComfortEasedAt ? ComfortResult.Eased : ComfortResult.Failed;

        /// <summary>What a talk leaves behind between the two: closer when it helped, a little rawer when it did not.</summary>
        public void RecordComfort(int helper, int sufferer, ComfortResult result, int now)
        {
            if (GetPerson(helper) == null || GetPerson(sufferer) == null) return;
            switch (result)
            {
                case ComfortResult.Resolved:
                    ChangeBond(sufferer, helper, new BondChange(2f, 3f, -3f, 1f), now); ChangeBond(helper, sufferer, new BondChange(1.5f, 1f, -1f, 1f), now); break;
                case ComfortResult.Eased:
                    ChangeBond(sufferer, helper, new BondChange(1f, 1.5f, -1.5f, 1f), now); ChangeBond(helper, sufferer, new BondChange(1f, .5f, -.5f, 1f), now); break;
                default:
                    ChangeBond(sufferer, helper, new BondChange(0f, -.5f, 1.5f, 0f), now); break;
            }
            RecordTogether(helper, sufferer, result == ComfortResult.Failed ? 300 : 900, now, BondCause.Care);
        }
    }
}
