namespace FamilyLife.Domain
{
    /// <summary>The hours routine professional work (a check-up, a sitting of the assessment) is done in. Owner: the framework; Education and Community Care both use it.</summary>
    public static class WorkingHours
    {
        public const int FirstHour = 7, LastHour = 19;
        public static bool Daytime(int hour) => hour >= FirstHour && hour < LastHour;
    }
}
