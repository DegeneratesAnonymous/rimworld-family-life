using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FamilyLife.Domain;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// Developer-mode shortcuts for trying the family features without waiting weeks of game time: build a dining hall and a medical room, give people
    /// the skills for a post, bring each timer due, make distant kin, push someone toward a break. None of this runs unless chosen from the debug menu.
    /// </summary>
    internal static class TestTools
    {
        private const string Cat = "Wil's Family Life, testing";

        private static FamilyComponent Comp => FamilyComponent.Current;
        private static int Now => Find.TickManager.TicksGame;

        private static Pawn Selected => Find.Selector.SingleSelectedThing as Pawn;

        // ------------------------------------------------------------------ places

        private static void Put(Map map, ThingDef def, IntVec3 cell, ThingDef stuff = null, Rot4? rot = null)
        {
            if (def == null || !cell.InBounds(map)) return;
            var thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? (stuff ?? ThingDefOf.WoodLog) : null);
            thing.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(thing, cell, map, rot ?? Rot4.North);
        }

        /// <summary>A walled, roofed, floored room with its interior from (x0,z0) to (x1,z1) inclusive and a door in the south wall at the given column.</summary>
        private static void Room(Map map, int x0, int z0, int x1, int z1, int doorX, ThingDef stuff)
        {
            var wall = ThingDefOf.Wall; var door = ThingDefOf.Door;
            for (int x = x0 - 1; x <= x1 + 1; x++)
                for (int z = z0 - 1; z <= z1 + 1; z++)
                {
                    var c = new IntVec3(x, 0, z);
                    if (!c.InBounds(map)) continue;
                    foreach (var t in c.GetThingList(map).Where(t => t.def.category == ThingCategory.Building || t.def.category == ThingCategory.Plant || t.def.category == ThingCategory.Item).ToList()) t.Destroy();
                    bool edge = x < x0 || x > x1 || z < z0 || z > z1;
                    map.terrainGrid.SetTerrain(c, DefDatabase<TerrainDef>.GetNamedSilentFail("WoodPlankFloor") ?? TerrainDefOf.Concrete);
                    if (edge) { if (z == z0 - 1 && x == doorX) Put(map, door, c, stuff); else Put(map, wall, c, stuff); }
                    else map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
                }
        }

        [DebugAction(Cat, "Build a dining hall and a medical room here", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void BuildRooms()
        {
            var map = Find.CurrentMap; var origin = UI.MouseCell();
            var wood = ThingDefOf.WoodLog;
            // Dining hall: 9 wide, 5 deep, three tables with chairs on both long sides.
            int x0 = origin.x, z0 = origin.z;
            Room(map, x0, z0, x0 + 8, z0 + 4, x0 + 4, wood);
            var table = DefDatabase<ThingDef>.GetNamedSilentFail("Table2x2c") ?? DefDatabase<ThingDef>.GetNamedSilentFail("TableShort");
            var chair = DefDatabase<ThingDef>.GetNamedSilentFail("DiningChair");
            for (int i = 0; i < 3; i++)
            {
                int tx = x0 + 1 + i * 3;
                Put(map, table, new IntVec3(tx, 0, z0 + 2), wood);
                Put(map, chair, new IntVec3(tx, 0, z0 + 1), wood);
                Put(map, chair, new IntVec3(tx + 1, 0, z0 + 1), wood);
                Put(map, chair, new IntVec3(tx, 0, z0 + 4), wood);
                Put(map, chair, new IntVec3(tx + 1, 0, z0 + 4), wood);
            }
            // Medical room beside it: four hospital beds.
            int mx = x0 + 11;
            Room(map, mx, z0, mx + 7, z0 + 3, mx + 3, wood);
            var bed = DefDatabase<ThingDef>.GetNamedSilentFail("HospitalBed");
            for (int i = 0; i < 4; i++) Put(map, bed, new IntVec3(mx + 1 + i * 2, 0, z0 + 2));
            // Meals on the floor outside for the hall keeper to fetch.
            foreach (var meal in new[] { "MealFine", "MealSimple" })
            {
                var def = DefDatabase<ThingDef>.GetNamedSilentFail(meal);
                if (def == null) continue;
                var thing = ThingMaker.MakeThing(def); thing.stackCount = 40;
                GenPlace.TryPlaceThing(thing, new IntVec3(x0 + 4, 0, z0 - 3), map, ThingPlaceMode.Near);
            }
            Comp?.Registry.SetHall(map.uniqueID, x0 + 4, z0 + 2);
            Messages.Message("Built a dining hall and a medical room, and named the hall. Doors face south.", MessageTypeDefOf.TaskCompletion, false);
        }

        // ------------------------------------------------------------------ people

        private static void Teach(Pawn p, SkillDef def, int level)
        {
            var s = p.skills?.GetSkill(def);
            if (s == null || s.TotallyDisabled) return;
            s.Level = level; s.passion = Passion.Major;
        }

        [DebugAction(Cat, "Make the selected pawn a doctor, teacher and cook", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Skill()
        {
            var p = Selected;
            if (p == null) { Messages.Message("Select a colonist first.", MessageTypeDefOf.RejectInput, false); return; }
            Teach(p, SkillDefOf.Medicine, 14); Teach(p, SkillDefOf.Intellectual, 14); Teach(p, SkillDefOf.Social, 12); Teach(p, SkillDefOf.Cooking, 12);
            Messages.Message(p.LabelShort + " now has high medicine, intellect, social and cooking, all with passion.", MessageTypeDefOf.TaskCompletion, false);
        }

        [DebugAction(Cat, "Give every adult colonist some skill and a calling", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SkillEveryone()
        {
            int i = 0;
            foreach (var p in Find.CurrentMap.mapPawns.FreeColonistsSpawned.Where(x => x.DevelopmentalStage == DevelopmentalStage.Adult))
            {
                var skills = new[] { SkillDefOf.Medicine, SkillDefOf.Intellectual, SkillDefOf.Cooking, SkillDefOf.Social };
                Teach(p, skills[i % skills.Length], 10 + i % 5); Teach(p, SkillDefOf.Social, 8); i++;
            }
            Messages.Message("Adults now each have a high skill with passion, spread across medicine, intellect and cooking.", MessageTypeDefOf.TaskCompletion, false);
        }

        [DebugAction(Cat, "Push the selected pawn close to a mental break", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void NearBreak()
        {
            var p = Selected;
            if (p?.needs?.mood == null) { Messages.Message("Select a colonist first.", MessageTypeDefOf.RejectInput, false); return; }
            p.needs.mood.CurLevel = 0.12f;
            Messages.Message(p.LabelShort + "'s mood is very low. A partner or parent should come to talk to them.", MessageTypeDefOf.TaskCompletion, false);
        }

        // ------------------------------------------------------------------ timers

        [DebugAction(Cat, "Choose the colony's posts now", allowedGameStates = AllowedGameStates.Playing)]
        private static void ElectNow()
        {
            var r = Comp?.Registry; if (r == null) return;
            foreach (var o in r.Offices) o.NextVoteTick = 0;
            var changed = r.ElectOffices(Now, OfficeDriver.Context(Comp));
            r.MaintainOffices(Now);
            Messages.Message(changed.Count + " post(s) filled. Check the Families window, the colonist bar and a pawn's family tab.", MessageTypeDefOf.TaskCompletion, false);
        }

        [DebugAction(Cat, "Bring a class, a check-up round and a hall meal due now", allowedGameStates = AllowedGameStates.Playing)]
        private static void DueNow()
        {
            var r = Comp?.Registry; if (r == null) return;
            foreach (var o in r.Offices.Where(x => x.Held)) { o.NextSessionTick = 0; o.SessionQueue.Clear(); foreach (var due in FamilyModules.DueNow.Enabled()) due(o); }          // each module brings its own timers due
            OfficeDriver.ForceUntil = Now + 6000;          // for the next two hours a test is not held up by who is busy or on a work schedule
            var c = Comp;
            OfficeDriver.WorkNow(c, Now);          // start them now rather than at the next check
            var lines = OfficeDriver.Diagnose(c, Now);
            foreach (var l in lines) Log.Message("[Wil's Family Life] test: " + l);
            Messages.Message("Started what could start.\n" + string.Join("\n", lines), MessageTypeDefOf.TaskCompletion, false);
        }

        [DebugAction(Cat, "Offer the check-up, outbreak and testing letters now", allowedGameStates = AllowedGameStates.Playing)]
        private static void OfferEventsNow()
        {
            var r = Comp?.Registry; if (r == null) return;
            foreach (var o in r.Offices) o.PendingKind = 0;
            OfficeEvents.Plan(PendingEvent.Checkups);
            Messages.Message("Sent the check-up notice. Use 'Start annual testing now' for the testing, or press the button on a lectern.", MessageTypeDefOf.TaskCompletion, false);
        }

        [DebugAction(Cat, "Call the yearly assembly now", allowedGameStates = AllowedGameStates.Playing)]
        private static void AssemblyNow()
        {
            var r = Comp?.Registry; if (r == null) return;
            r.NextAssemblyTick = Math.Max(1, Now);
            Messages.Message("The yearly assembly will be called at the next check. Campaigning lasts two days; speed up, or run 'Skip two days' to vote sooner.", MessageTypeDefOf.TaskCompletion, false);
        }

        [DebugAction(Cat, "Skip the assembly's campaign to voting", allowedGameStates = AllowedGameStates.Playing)]
        private static void SkipCampaign()
        {
            var a = Comp?.Registry.OpenAssembly;
            if (a == null || a.State != AssemblyState.Campaign) { Messages.Message("No assembly is campaigning.", MessageTypeDefOf.RejectInput, false); return; }
            a.VotingTick = Now;
            Messages.Message("Voting opens at the next check.", MessageTypeDefOf.TaskCompletion, false);
        }

        // ------------------------------------------------------------------ travel

        [DebugAction(Cat, "Propose a camping trip for the selected pawn's house", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Camp()
        {
            var p = Selected; var c = Comp;
            if (p == null || c == null) { Messages.Message("Select a colonist first.", MessageTypeDefOf.RejectInput, false); return; }
            var r = c.Registry; int family = r.FamilyOf(p.thingIDNumber);
            int tile = CampingDriver.PickTile(WorldTiles.HomeTile(Find.CurrentMap), Now, 4);
            var ctx = new CampContext { Available = id => true, IsAvatar = id => Governance.IsAvatar(c, id) };
            var trip = tile < 0 ? null : r.ProposeCamp(family, CampMotive.Recreation, tile, Now, ctx);
            Messages.Message(trip == null ? "Could not propose a trip (one may already be open, or no safe place nearby)." : "A camping trip is proposed. Open the Families window to agree to it.", MessageTypeDefOf.TaskCompletion, false);
        }

        [DebugAction(Cat, "Create a distant relative and call a grand reunion for the selected pawn's house", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Kin()
        {
            var p = Selected; var c = Comp;
            if (p == null || c == null) { Messages.Message("Select a colonist first.", MessageTypeDefOf.RejectInput, false); return; }
            var faction = Find.FactionManager.AllFactionsVisible.Where(f => !f.IsPlayer && !f.HostileTo(Faction.OfPlayer) && f.def.humanlikeFaction && f.def.basicMemberKind != null)
                .OrderByDescending(f => (int)f.def.techLevel).FirstOrDefault();
            if (faction == null) { Messages.Message("No friendly faction to take a relative from.", MessageTypeDefOf.RejectInput, false); return; }
            var kin = PawnGenerator.GeneratePawn(new PawnGenerationRequest(faction.def.basicMemberKind, faction, PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true,
                fixedBiologicalAge: 40f, fixedChronologicalAge: 40f, canGeneratePawnRelations: false));
            kin.relations.AddDirectRelation(PawnRelationDefOf.Sibling, p);
            Find.WorldPawns.PassToWorld(kin, PawnDiscardDecideMode.KeepForever);
            c.Sync(Now);
            var r = c.Registry; int family = r.FamilyOf(p.thingIDNumber);
            var request = r.RequestCeremony(CeremonyKind.GrandReunion, family, 0, 0, Now, ActivityTuning.TicksPerDay, 8 * ActivityTuning.TicksPerDay, CeremonyInfluence.MissingKin);
            if (request == null) { Messages.Message("A reunion is already planned for that house.", MessageTypeDefOf.RejectInput, false); return; }
            request.VisitorIds.Add(kin.thingIDNumber);
            Messages.Message(kin.LabelShort + " of " + faction.Name + " (" + faction.def.techLevel + ") is invited. They arrive within about a day" + (faction.def.techLevel >= TechLevel.Spacer ? ", by shuttle." : ", on foot."), MessageTypeDefOf.TaskCompletion, false);
        }

        // ------------------------------------------------------------------ looking

        [DebugAction(Cat, "Log the posts, hall, assembly and trips", allowedGameStates = AllowedGameStates.Playing)]
        private static void Status()
        {
            var r = Comp?.Registry; if (r == null) return;
            var sb = new StringBuilder("[Wil's Family Life] Community status at tick " + Now + "\n");
            foreach (var o in r.Offices)
                sb.AppendLine("  post " + o.Kind + ": " + (o.Held ? FamilyUi.NameOf(r, o.HolderId) + " work=" + o.Work + " grade=" + r.OfficeGrade(o.Kind).ToString("0.00") + " termEnd=" + o.TermEndTick + " queue=" + o.SessionQueue.Count : "vacant"));
            sb.AppendLine("  hall: " + (r.HallNamed ? "(" + r.HallX + "," + r.HallZ + ")" : "automatic") + "; next assembly tick " + r.NextAssemblyTick);
            foreach (var a in r.Assemblies.TakeLast(2))
                sb.AppendLine("  assembly " + a.Id + " " + a.State + " races=" + a.Races.Count + " voted=" + a.Voted.Count);
            foreach (var t in r.CampTrips.TakeLast(3)) sb.AppendLine("  camp " + t.Id + " " + t.State + " " + t.Motive + " tile=" + t.Tile);
            foreach (var v in r.KinVisits.TakeLast(3)) sb.AppendLine("  kin visit " + v.Id + " " + v.State + " kin=" + v.KinIds.Count);
            Log.Message(sb.ToString());
        }
    }
}
