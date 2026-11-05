using FamilyLife.Domain;
using System.Globalization;
using System.Text;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("calibration-output");
Directory.CreateDirectory(output);
var summary = new StringBuilder("# Seeded story calibration\n\nFive RimWorld years = 300 days. Seed 1701. Synthetic opportunities, not a live playtest or measured activity distribution. Profiles and initial histories vary; opportunity budgets are identical.\n\n| Scenario | Mode | Events | Unique IDs | Great % | Mean attachment | Mean trust | Mean tension | Saturated bonds % | Final grievance |\n|---|---|---:|---:|---:|---:|---:|---:|---:|---:|\n");
var frequencies = new StringBuilder("scenario,mode,event,tier,count\n");
var history = new StringBuilder("scenario,mode,day,attachment,trust,tension,grievance\n");
var allDefinitions = EventCatalog.All.Concat(TraditionMoments.All).ToList();
var coverage = allDefinitions.ToDictionary(d => d.Id, d => 0);
var endpoints = new Dictionary<string,(float attachment,float grievance,float saturation)>();
foreach (string scenario in new[] { "compatible", "hostile", "feud-child-friends", "proud-abrasive", "kind-low-social" })
foreach (string mode in new[] { "isolated-events", "synthetic-hourly", "recovery" })
{
    var rng = new Random(1701);
    var registry = new FamilyRegistry();
    var people = new List<PersonObservation>();
    for (int id = 1; id <= 8; id++)
    {
        bool child = id % 4 >= 3 || id % 4 == 0;
        int first = id <= 4 ? 1 : 5;
        people.Add(new PersonObservation { Id = id, Name = "Pawn" + id, LastName = first == 1 ? "Oak" : "Ash", Presence = Presence.Colony,
            Juvenile = child, BirthAbsTick = -10000000 + id, AgeTicks = child ? 6000000 : 60000000,
            SpouseIds = child ? new List<int>() : new List<int> { id == first ? first + 1 : first }, ParentIds = child ? new List<int> { first, first + 1 } : new List<int>() });
    }
    registry.Apply(people, ActivityTuning.TicksPerDay);
    float warmth = scenario == "compatible" || scenario == "kind-low-social" ? 1.2f : -1.2f;
    float steady = scenario == "compatible" ? 1f : scenario == "kind-low-social" ? .3f : -1f;
    int social = scenario == "kind-low-social" ? 2 : 12;
    var actors = people.ToDictionary(p => p.Id, p => new Actor { Id = p.Id, Child = p.Juvenile, AgeYears = p.Juvenile ? 10 : 30, LineageId = registry.FamilyOf(p.Id),
        Profile = new float[DimInfo.Count], Skills = new Dictionary<string,int> { ["Social"] = social, ["Intellectual"] = 8, ["Cooking"] = 8, ["Shooting"] = 8 }, Mood = .6f });
    foreach (var a in actors.Values)
    { a.Profile[(int)Dim.Warmth] = scenario == "feud-child-friends" && a.Child ? .9f : warmth; a.Profile[(int)Dim.Steadiness] = steady; a.Profile[(int)Dim.Status] = scenario == "proud-abrasive" ? 1.5f : 0; }
    bool recovering = false;
    int Opinion(int a, int b) => recovering ? 45 : scenario == "feud-child-friends" && actors[a].Child && actors[b].Child ? 60 : warmth > 0 ? 45 : -45;
    registry.UpdateBonds((a,b) => Opinion(a,b), ActivityTuning.TicksPerDay, null);
    if (scenario == "feud-child-friends") registry.Ties.Add(new FamilyTie { LineageA = Math.Min(registry.FamilyOf(1),registry.FamilyOf(5)), LineageB = Math.Max(registry.FamilyOf(1),registry.FamilyOf(5)), Grievance = 70, LastTick = ActivityTuning.TicksPerDay });
    var counts = new SortedDictionary<string,int>(); int total = 0, great = 0;
    for (int day = 2; day <= 301; day++)
    {
        int now = day * ActivityTuning.TicksPerDay;
        if (mode == "recovery" && day >= 152)
        { recovering = true; foreach (var actor in actors.Values) { actor.Profile[(int)Dim.Warmth] = 1.2f; actor.Profile[(int)Dim.Steadiness] = 1f; } }
        if (mode != "isolated-events") for (int h = 0; h < 24; h++) registry.UpdateBonds((a,b) => Opinion(a,b), now - ActivityTuning.TicksPerDay + h * BondTuning.TicksPerHour, null, id => actors[id].Profile);
        registry.UpdateTies(now); registry.UpdateCommitments(now); registry.MaintainAgency(now);
        foreach (var a in actors.Values)
        {
            var b = actors[1 + rng.Next(8)]; if (a == b) continue;
            ActivityKind setting = new[] { ActivityKind.None, ActivityKind.Meal, ActivityKind.GameNight, ActivityKind.Competition, ActivityKind.Learning }[rng.Next(5)];
            var c = new PairContext { Registry = registry, Initiator = a, Recipient = b, Now = now, SameFamily = a.LineageId == b.LineageId,
                InitiatorBond = registry.GetBond(a.Id,b.Id), RecipientBond = registry.GetBond(b.Id,a.Id), Kind = registry.KindOf(registry.GetPerson(a.Id),registry.GetPerson(b.Id)),
                FamilyTie = registry.GetTie(a.LineageId,b.LineageId), Rift = registry.OpenRiftBetween(a.Id,b.Id), Setting = setting, FromActivity = setting != ActivityKind.None,
                QualityHours = setting == ActivityKind.None ? .28f : 0, OpinionOfRecipient = Opinion(a.Id,b.Id), OpinionOfInitiator = Opinion(b.Id,a.Id), RelationshipPace = .25f };
            var def = registry.PickEvent(c, EventCatalog.All, rng, true); if (def == null) continue;
            var result = registry.ResolveEvent(def,c,rng); registry.ApplyEvent(result,now);
            string key = def.Id + "," + result.Tier; counts[key] = counts.GetValueOrDefault(key) + 1; total++; coverage[def.Id]++; if (result.Tier == EventTier.Great) great++;
        }
        history.AppendLine($"{scenario},{mode},{day-1},{registry.Bonds.Average(b=>b.Attachment):F3},{registry.Bonds.Average(b=>b.Trust):F3},{registry.Bonds.Average(b=>b.Tension):F3},{registry.Ties.Sum(t=>t.Grievance):F3}");
    }
    endpoints[scenario+"/"+mode] = (registry.Bonds.Average(b=>b.Attachment), registry.Ties.Sum(t=>t.Grievance), 100f*registry.Bonds.Count(b=>b.Attachment>=95&&b.Trust>=95)/Math.Max(1,registry.Bonds.Count));
    foreach (var pair in counts) frequencies.AppendLine($"{scenario},{mode},{pair.Key},{pair.Value}");
    summary.AppendLine($"| {scenario} | {mode} | {total} | {counts.Keys.Select(k=>k.Split(',')[0]).Distinct().Count()} | {100f*great/Math.Max(1,total):F1} | {registry.Bonds.Average(b=>b.Attachment):F1} | {registry.Bonds.Average(b=>b.Trust):F1} | {registry.Bonds.Average(b=>b.Tension):F1} | {100f*registry.Bonds.Count(b=>b.Attachment>=95&&b.Trust>=95)/Math.Max(1,registry.Bonds.Count):F1} | {registry.Ties.Sum(t=>t.Grievance):F1} |");
}
summary.AppendLine("\nFrequency/tier counts and daily trajectories are written alongside this report. Zero-frequency events are not evidence of a bug: third-party, achievement, distress and other missing contexts are not fabricated. Natural runs select from the 90 core definitions and do not exercise every core opportunity or any of the 48 tradition-specific gathering episodes. The coverage table includes all 138 definitions, with unexercised tradition entries explicitly zero. Controlled outcome probes include all 138 but bypass eligibility and do not apply consequences. No work time or TPS is measured; jobs, pathing, social memories, actual activity distribution and player interruption require live validation. Recovery mode deliberately changes profiles and opinions after day 150 to test whether entrenched histories can improve; it does not model an organic personality change. Synthetic-hourly mode adds passive bond updates but does not simulate physical activity jobs. All initial pawns remain at their starting age and colony presence to isolate social rules.");
File.WriteAllText(Path.Combine(output,"summary.md"),summary.ToString());
File.WriteAllText(Path.Combine(output,"frequencies.csv"),frequencies.ToString());
File.WriteAllText(Path.Combine(output,"history.csv"),history.ToString());
File.WriteAllLines(Path.Combine(output,"coverage.csv"), new[]{"event,selected_count"}.Concat(coverage.Select(k => k.Key+","+k.Value)));
Console.WriteLine(summary);

if (endpoints.Values.Any(e => !float.IsFinite(e.attachment) || e.saturation >= 99)) throw new Exception("Nonfinite or uniformly saturated history");
if (endpoints["compatible/synthetic-hourly"].attachment <= endpoints["hostile/synthetic-hourly"].attachment + 10) throw new Exception("Compatible and hostile histories converged");
if (endpoints["hostile/recovery"].grievance > endpoints["hostile/synthetic-hourly"].grievance + 0.01f) throw new Exception("Scripted recovery increased grievance");

// Controlled resolution probes cover every definition, bypassing eligibility on purpose.
// They measure execution/reception math, not whether the narrative can legitimately occur.
var probes = new StringBuilder("profile,event,poor,flat,good,great,mean_execution,mean_reception\n");
foreach (string profile in new[]{"compatible","hostile","feud-child-friends","proud-abrasive","kind-low-social"})
foreach (var def in allDefinitions)
{
    bool friendly = profile == "compatible" || profile == "kind-low-social";
    Actor ProbeActor(int id, bool child)
    {
        var actor = new Actor{Id=id,Child=child,AgeYears=child?10:30,Profile=new float[DimInfo.Count],Mood=friendly?.7f:.3f,
            Skills=new Dictionary<string,int>{{"Social",profile=="kind-low-social"?2:16},{"Cooking",8},{"Intellectual",8},{"Shooting",8}}};
        actor.Profile[(int)Dim.Warmth]=friendly?1.2f:-1.2f;
        actor.Profile[(int)Dim.Steadiness]=friendly?.8f:-1f;
        actor.Profile[(int)Dim.Status]=profile=="proud-abrasive"?1.5f:0;
        return actor;
    }
    var a=ProbeActor(1,def.Scenario==EventScenario.ChildChild);
    var b=ProbeActor(2,def.Scenario==EventScenario.ChildChild || def.Scenario==EventScenario.AdultChild && !def.ThirdIsChild);
    if(profile=="feud-child-friends" && a.Child && b.Child) {a.Profile[(int)Dim.Warmth]=b.Profile[(int)Dim.Warmth]=.9f;friendly=true;}
    var c=new PairContext{Registry=new FamilyRegistry(),Initiator=a,Recipient=b,Third=def.NeedsThird?ProbeActor(3,def.ThirdIsChild):null,
        InitiatorBond=new Bond{Attachment=friendly?60:15,Trust=friendly?60:10,Tension=friendly?5:70},
        RecipientBond=new Bond{Attachment=friendly?60:15,Trust=friendly?60:10,Tension=friendly?5:70},
        FamilyTie=new FamilyTie{Goodwill=friendly?40:0,Grievance=friendly?0:70},OpinionOfRecipient=friendly?45:-45,OpinionOfInitiator=friendly?45:-45};
    var tiers=new int[4];float execution=0,reception=0;
    for(int sample=0;sample<100;sample++)
    {
        var result=c.Registry.ResolveEvent(def,c,new Random(1701+sample));
        if(!float.IsFinite(result.Score) || result.InitiatorFeeling < -1 || result.RecipientFeeling > 1) throw new Exception("Invalid result: "+def.Id);
        tiers[(int)result.Tier]++;execution+=result.Execution;reception+=result.Reception;
    }
    probes.AppendLine($"{profile},{def.Id},{tiers[0]},{tiers[1]},{tiers[2]},{tiers[3]},{execution/100:F3},{reception/100:F3}");
}
File.WriteAllText(Path.Combine(output,"controlled-outcomes.csv"),probes.ToString());
