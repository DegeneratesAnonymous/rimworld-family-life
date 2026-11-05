using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    public class FamilySettings : ModSettings
    {
        public bool familyStories = true, familySupplies = true, familyTheft = true, familyViolence = true, lethalPlots;
        public bool logEvents;
        public bool griefAmplification = true;
        public bool familyActivities = true;
        public float dailyFamilyHours = 2f;
        public bool bedtimeStories = true;
        public bool sharedMeals = true;
        public bool dates = true;
        public bool teaching = true;
        public bool traditions = true;
        public bool babyBonding = true;
        public bool bedside = true;
        public bool conflicts = true;
        public bool rivalry = true;
        public bool relationshipHealth = true;
        public bool affairs;
        public bool bondMessages = true;
        public bool strictRomance;
        public bool socialEvents = true;
        public bool zonesAllowWork = true;
        public bool familyDoors = true;
        public bool familyMeals = true;
        public bool eventsFamily = true;
        public bool eventsIndividual = true;
        public bool eventsInterFamily = true;
        public bool sharedActivityEvents = true;
        public int eventNotices = 1;               // 0 history only, 1 moments that matter, 2 every notable moment
        public float eventFrequency = 1f;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref familyStories, "familyStories", true);
            Scribe_Values.Look(ref familySupplies, "familySupplies", true);
            Scribe_Values.Look(ref familyTheft, "familyTheft", true);
            Scribe_Values.Look(ref familyViolence, "familyViolence", true);
            Scribe_Values.Look(ref lethalPlots, "lethalPlots");
            Scribe_Values.Look(ref logEvents, "logEvents");
            Scribe_Values.Look(ref griefAmplification, "griefAmplification", true);
            Scribe_Values.Look(ref familyActivities, "familyActivities", true);
            Scribe_Values.Look(ref dailyFamilyHours, "dailyFamilyHours", 2f);
            Scribe_Values.Look(ref bedtimeStories, "bedtimeStories", true);
            Scribe_Values.Look(ref sharedMeals, "sharedMeals", true);
            Scribe_Values.Look(ref dates, "dates", true);
            Scribe_Values.Look(ref teaching, "teaching", true);
            Scribe_Values.Look(ref traditions, "traditions", true);
            Scribe_Values.Look(ref babyBonding, "babyBonding", true);
            Scribe_Values.Look(ref bedside, "bedside", true);
            Scribe_Values.Look(ref conflicts, "conflicts", true);
            Scribe_Values.Look(ref rivalry, "rivalry", true);
            Scribe_Values.Look(ref relationshipHealth, "relationshipHealth", true);
            Scribe_Values.Look(ref affairs, "affairs");
            Scribe_Values.Look(ref bondMessages, "bondMessages", true);
            Scribe_Values.Look(ref strictRomance, "strictRomance");
            Scribe_Values.Look(ref socialEvents, "socialEvents", true);
            Scribe_Values.Look(ref zonesAllowWork, "zonesAllowWork", true);
            Scribe_Values.Look(ref familyDoors, "familyDoors", true);
            Scribe_Values.Look(ref familyMeals, "familyMeals", true);
            Scribe_Values.Look(ref eventsFamily, "eventsFamily", true);
            Scribe_Values.Look(ref eventsIndividual, "eventsIndividual", true);
            Scribe_Values.Look(ref eventsInterFamily, "eventsInterFamily", true);
            Scribe_Values.Look(ref sharedActivityEvents, "sharedActivityEvents", true);
            Scribe_Values.Look(ref eventNotices, "eventNotices", 1);
            Scribe_Values.Look(ref eventFrequency, "eventFrequency", 1f);
        }
    }

    public class FamilyLifeMod : Mod
    {
        public static FamilySettings Settings;
        private static readonly List<string> applied = new List<string>();
        private static readonly List<string> failed = new List<string>();

        public FamilyLifeMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<FamilySettings>();

            // One patch class at a time: if a hook fails (another mod, a new game build) it is skipped and logged,
            // and the periodic reconcile still keeps the family records correct.
            var harmony = new Harmony("SpacemanWil.WilsFamilyLife");
            foreach (var type in typeof(FamilyLifeMod).Assembly.GetTypes().Where(t => t.IsDefined(typeof(HarmonyPatch), false)))
            {
                try { harmony.CreateClassProcessor(type).Patch(); applied.Add(type.Name); }
                catch (Exception e)
                {
                    failed.Add(type.Name);
                    Log.Warning("[Wil's Family Life] Skipped hook " + type.Name + ": " + e.Message);
                }
            }
            LongEventHandler.ExecuteWhenFinished(FamilyTabs.RegisterOnHumanlikes);
            Log.Message("[Wil's Family Life] Loaded (" + applied.Count + " hooks" + (failed.Count > 0 ? ", " + failed.Count + " skipped" : "") + ").");
        }

        public override string SettingsCategory() => "Wil's Family Life";

        private Vector2 settingsScroll;
        private float settingsHeight = 1400;
        public override void DoSettingsWindowContents(Rect inRect)
        {
            var list = new Listing_Standard();
            Widgets.BeginScrollView(inRect, ref settingsScroll, new Rect(0, 0, inRect.width - 20, settingsHeight));
            list.Begin(new Rect(0, 0, inRect.width - 20, settingsHeight));
            list.CheckboxLabeled("WFL_S_Activities".Translate(), ref Settings.familyActivities, "WFL_S_ActivitiesTip".Translate());
            if (Settings.familyActivities)
            {
                list.Label("WFL_S_Hours".Translate(Settings.dailyFamilyHours.ToString("0.0")), tooltip: "WFL_S_HoursTip".Translate());
                Settings.dailyFamilyHours = Mathf.Round(list.Slider(Settings.dailyFamilyHours, 0.5f, 4f) * 2f) / 2f;
                list.CheckboxLabeled("WFL_S_Stories".Translate(), ref Settings.bedtimeStories, "WFL_S_StoriesTip".Translate());
                list.CheckboxLabeled("WFL_S_Meals".Translate(), ref Settings.sharedMeals, "WFL_S_MealsTip".Translate());
                list.CheckboxLabeled("WFL_S_Dates".Translate(), ref Settings.dates, "WFL_S_DatesTip".Translate());
                list.CheckboxLabeled("WFL_S_Teaching".Translate(), ref Settings.teaching, "WFL_S_TeachingTip".Translate());
                list.CheckboxLabeled("WFL_S_BabyBonding".Translate(), ref Settings.babyBonding, "WFL_S_BabyBondingTip".Translate());
                list.CheckboxLabeled("WFL_S_Bedside".Translate(), ref Settings.bedside, "WFL_S_BedsideTip".Translate());
            }
            list.CheckboxLabeled("WFL_S_Traditions".Translate(), ref Settings.traditions, "WFL_S_TraditionsTip".Translate());
            list.CheckboxLabeled("WFL_S_Conflicts".Translate(), ref Settings.conflicts, "WFL_S_ConflictsTip".Translate());
            if (Settings.conflicts)
            {
                list.CheckboxLabeled("WFL_S_Rivalry".Translate(), ref Settings.rivalry, "WFL_S_RivalryTip".Translate());
                list.CheckboxLabeled("WFL_S_Health".Translate(), ref Settings.relationshipHealth, "WFL_S_HealthTip".Translate());
                list.CheckboxLabeled("WFL_S_Affairs".Translate(), ref Settings.affairs, "WFL_S_AffairsTip".Translate());
                list.CheckboxLabeled("WFL_S_Strict".Translate(), ref Settings.strictRomance, "WFL_S_StrictTip".Translate());
            }
            list.CheckboxLabeled("WFL_S_FamilyStories".Translate(), ref Settings.familyStories);
            list.CheckboxLabeled("WFL_S_FamilySupplies".Translate(), ref Settings.familySupplies);
            list.CheckboxLabeled("WFL_S_FamilyTheft".Translate(), ref Settings.familyTheft);
            list.CheckboxLabeled("WFL_S_FamilyViolence".Translate(), ref Settings.familyViolence);
            list.CheckboxLabeled("WFL_S_LethalPlots".Translate(), ref Settings.lethalPlots);
            list.CheckboxLabeled("WFL_S_SocialEvents".Translate(), ref Settings.socialEvents, "WFL_S_SocialEventsTip".Translate());
            if (Settings.socialEvents)
            {
                list.CheckboxLabeled("WFL_S_EventsFamily".Translate(), ref Settings.eventsFamily, "WFL_S_EventsFamilyTip".Translate());
                list.CheckboxLabeled("WFL_S_EventsIndividual".Translate(), ref Settings.eventsIndividual, "WFL_S_EventsIndividualTip".Translate());
                list.CheckboxLabeled("WFL_S_EventsInter".Translate(), ref Settings.eventsInterFamily, "WFL_S_EventsInterTip".Translate());
                list.CheckboxLabeled("WFL_S_EventsShared".Translate(), ref Settings.sharedActivityEvents, "WFL_S_EventsSharedTip".Translate());
                list.Label("WFL_S_EventFrequency".Translate(Settings.eventFrequency.ToString("0.0")), tooltip: "WFL_S_EventFrequencyTip".Translate());
                Settings.eventFrequency = Mathf.Round(list.Slider(Settings.eventFrequency, 0.25f, 2f) * 4f) / 4f;
                list.Label(("WFL_S_EventNotices_" + Settings.eventNotices).Translate(), tooltip: "WFL_S_EventNoticesTip".Translate());
                Settings.eventNotices = Mathf.RoundToInt(list.Slider(Settings.eventNotices, 0f, 2f));
            }
            list.CheckboxLabeled("WFL_S_BondMessages".Translate(), ref Settings.bondMessages, "WFL_S_BondMessagesTip".Translate());
            list.CheckboxLabeled("WFL_S_Grief".Translate(), ref Settings.griefAmplification, "WFL_S_GriefTip".Translate());
            list.CheckboxLabeled("WFL_S_FamilyMeals".Translate(), ref Settings.familyMeals, "WFL_S_FamilyMealsTip".Translate());
            list.CheckboxLabeled("WFL_S_FamilyDoors".Translate(), ref Settings.familyDoors, "WFL_S_FamilyDoorsTip".Translate());
            list.CheckboxLabeled("WFL_S_LogEvents".Translate(), ref Settings.logEvents, "WFL_S_LogEventsTip".Translate());
            settingsHeight = list.CurHeight + 12;
            list.End();
            Widgets.EndScrollView();
        }
    }
}
