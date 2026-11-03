using System.Collections.Generic;
using FamilyLife.Domain;
using RimWorld;
using Verse;

namespace FamilyLife
{
    /// <summary>Turns a domain <see cref="Grief"/> into a mood memory on the living pawn. Stronger attachment, deeper stage.</summary>
    internal static class GriefApplier
    {
        public static void Apply(List<Grief> griefs, System.Func<int, Pawn> pawnOf)
        {
            if (griefs == null || griefs.Count == 0) return;
            var def = DefDatabase<ThoughtDef>.GetNamedSilentFail("WFL_FamilyGrief");
            if (def == null) return;

            foreach (var grief in griefs)
            {
                var pawn = pawnOf(grief.PersonId);
                if (pawn == null || pawn.Dead || pawn.needs?.mood?.thoughts?.memories == null) continue;
                var lost = pawnOf(grief.LostId);
                var memory = (Thought_Memory)ThoughtMaker.MakeThought(def);
                memory.SetForcedStage(StageFor(grief.Attachment, def.stages.Count));
                pawn.needs.mood.thoughts.memories.TryGainMemory(memory, lost);
            }
        }

        public static int StageFor(float attachment, int stageCount)
        {
            int stage = attachment >= 70f ? 2 : (attachment >= 45f ? 1 : 0);
            return stage < stageCount ? stage : stageCount - 1;
        }
    }
}
