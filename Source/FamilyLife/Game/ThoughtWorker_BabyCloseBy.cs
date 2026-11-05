using RimWorld;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// A parent whose newborn or baby sleeps in their own room, or close by, does not have far to go to care for it: a small, steady lift
    /// for as long as the child is a baby. It ends by itself when the child grows past that stage.
    /// </summary>
    public class ThoughtWorker_BabyCloseBy : ThoughtWorker
    {
        private const float CloseCells = 10f;

        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p.Dead || !p.Spawned || p.relations == null) return false;
            var bed = p.ownership?.OwnedBed;
            if (bed == null || !bed.Spawned) return false;
            var room = bed.GetRoom();
            bool indoors = room != null && !room.PsychologicallyOutdoors;
            foreach (var child in p.relations.Children)
            {
                if (child.Dead || (child.DevelopmentalStage != DevelopmentalStage.Baby && child.DevelopmentalStage != DevelopmentalStage.Newborn)) continue;
                var babyBed = child.ownership?.OwnedBed;
                if (babyBed == null || !babyBed.Spawned || babyBed.Map != bed.Map) continue;
                if (indoors && babyBed.GetRoom() == room) return ThoughtState.ActiveAtStage(0);
                if ((babyBed.Position - bed.Position).LengthHorizontal <= CloseCells && p.Map == babyBed.Map) return ThoughtState.ActiveAtStage(1);
            }
            return false;
        }
    }
}
