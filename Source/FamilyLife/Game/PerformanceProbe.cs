using FamilyLife.Domain;
using LudeonTK;
using Verse;

namespace FamilyLife
{
    internal static class PerformanceDebug
    {
        [DebugAction("Wil's Family Life", "Start performance capture", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Start() { FamilyComponent.Current?.Performance.Start(Find.TickManager.TicksGame); Log.Message("[Wil's Family Life] Performance capture started. Run unpaused at the speed being measured, then stop capture."); }
        [DebugAction("Wil's Family Life", "Stop and report performance capture", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Stop()
        {
            var probe = FamilyComponent.Current?.Performance;
            if (probe != null && probe.Enabled) Log.Message(probe.Stop(Find.TickManager.TicksGame));
        }
    }
}
