using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// The one place that touches world tiles, so the rest of the code can keep a plain tile number. Everything else keeps a plain tile number and asks here.
    /// </summary>
    internal static class WorldTiles
    {
        internal static PlanetTile Native(int id) => new PlanetTile(id, Find.WorldGrid.Surface);
        internal static int Id(PlanetTile tile) => tile.tileId;

        internal static int HomeTile(Map map) => map == null ? -1 : Id(map.Tile);
        internal static GlobalTargetInfo Target(int id) => new GlobalTargetInfo(Native(id));
        internal static int CaravanTile(Caravan caravan) => Id(caravan.Tile);

        internal static List<int> Neighbours(int id)
        {
            var result = new List<int>();
            var list = new List<PlanetTile>();
            Find.WorldGrid.GetTileNeighbors(Native(id), list);
            foreach (var n in list) result.Add(n.tileId);
            return result;
        }

        /// <summary>Land, passable, empty, reachable from home, and not freezing or scorching.</summary>
        internal static bool Safe(int home, int id)
        {
            var tile = Native(id);
            var info = Find.WorldGrid[tile];
            if (info == null || info.WaterCovered || Find.World.Impassable(tile) || Find.WorldObjects.AnyWorldObjectAt(tile)) return false;
            if (info.hilliness == Hilliness.Impassable || info.temperature < -25f || info.temperature > 45f) return false;
            return Find.WorldReachability.CanReach(Native(home), tile);
        }

        internal static float Temperature(int id) => Find.WorldGrid[Native(id)].temperature;
        internal static float Rainfall(int id) => Find.WorldGrid[Native(id)].rainfall;
        internal static void Jump(int id) => CameraJumper.TryJump(Target(id));
    }
}
