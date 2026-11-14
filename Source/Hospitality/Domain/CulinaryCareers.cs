using System;
using FamilyLife.Framework;

namespace FamilyLife.Domain
{
    public static class CulinaryCareers
    {
        public const string Module = "wils.hospitality", Career = "wils.hospitality.cooking";
        public const string Cook = "wils.hospitality.cook", Chef = "wils.hospitality.chef";
        public static string Role(string specialty) => Module + "." + specialty.ToLowerInvariant();
        public static CareerDefinition Definition() => new CareerDefinition
        {
            Id = Career, ModuleId = Module,
            Level2 = new CareerRule { GeneralXp = 120 }.Skill("Cooking", 8),
            Specialties = new System.Collections.Generic.Dictionary<string, CareerRule>
            {
                { "Artisan", new CareerRule { GeneralXp = 250, SpecialtyXp = 60 }.Skill("Cooking", 10).Skill("Artistic", 6) },
                { "Bartender", new CareerRule { GeneralXp = 250, SpecialtyXp = 60 }.Skill("Cooking", 8).Skill("Social", 6) },
                { "Innkeeper", new CareerRule { GeneralXp = 250, SpecialtyXp = 60 }.Skill("Cooking", 8).Skill("Social", 8) }
            }
        };
        public static float Presentation(int cooking, int artistic, bool artisan) => Math.Max(0, cooking) + (artisan ? Math.Min(4f, Math.Max(0, artistic) * .25f) : 0);
    }
    public static class BusinessCalendar
    {
        public static bool IsPayday(int day) => day % 15 == 0 || day % 15 == 7;
        public static int Next(int workDay) { int day = workDay + 1; while (!IsPayday(day)) day++; return day; }
        public static int Latest(int day) { while (day > 0 && !IsPayday(day)) day--; return day; }
    }
}
