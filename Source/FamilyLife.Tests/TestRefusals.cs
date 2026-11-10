using FamilyLife.Domain;

namespace FamilyLife.Tests
{
    /// <summary>Builds a refusal the way the rules would have recorded it, with the standard cooldown.</summary>
    internal static class TestRefusals
    {
        public static CareRefusal Make(int patient, int healer, CareRefusalKind kind, int tick, string reason = "test") =>
            new CareRefusal { Patient = patient, Healer = healer, Kind = kind, Reason = reason, Tick = tick, Until = tick + AppointmentBackoff.RefusalDays * ActivityTuning.TicksPerDay };
    }
}
