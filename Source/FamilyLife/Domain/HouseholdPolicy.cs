using System;
namespace FamilyLife.Domain
{
    public sealed partial class Lineage
    {
        public float FoodReserveDays = 1f, FuelReserveDays = 2f;
        public int MedicineReserve = 2;
        public bool EmergencySharing = true, UsePublicBarterFunds;
    }
    public static class HouseholdPolicy
    {
        public static float FoodTarget(int members, float days) => Math.Max(0,members) * 1.6f * Math.Max(.5f, Math.Min(3f,days));
        public static bool EmergencyAccess(bool permitsSharing, bool isFood, bool starving, bool isMedicine, bool emergencyTreatment)
            => permitsSharing && (isFood && starving || isMedicine && emergencyTreatment);
    }
}
