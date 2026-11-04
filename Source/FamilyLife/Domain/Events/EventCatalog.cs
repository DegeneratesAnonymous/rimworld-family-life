using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    /// <summary>The named conditions events are built from. Each states, in words, what must be true, so the developer view can say what is missing.</summary>
    public static class C
    {
        private static Cond K(string name, Func<PairContext, bool> test) => new Cond(name, test);
        public static Cond Where(string name, Func<PairContext, bool> test) => new Cond(name, test);

        // who they are to each other
        public static readonly Cond Family = K("they are relatives", c => c.SameFamily || c.Related);
        public static readonly Cond Siblings = K("they are siblings", c => c.Siblings);
        public static readonly Cond InitiatorParent = K("the initiator is the recipient's parent", c => c.InitiatorIsParent);
        public static readonly Cond InitiatorCaresFor = K("the initiator is the recipient's parent or close family", c => c.InitiatorIsParent || (c.Related && c.AttachI >= 30f));
        public static readonly Cond InitiatorOlder = K("the initiator is the older of the two", c => c.Initiator.AgeYears >= c.Recipient.AgeYears + 2f);
        public static readonly Cond InitiatorYounger = K("the initiator is the younger of the two", c => c.Initiator.AgeYears + 2f <= c.Recipient.AgeYears);
        public static readonly Cond InitiatorIsChild = K("the initiator is the child", c => c.Initiator.Child);
        public static readonly Cond InitiatorIsAdult = K("the initiator is the adult", c => !c.Initiator.Child);
        public static readonly Cond ChildOlder = K("the child is old enough to want independence", c => c.Initiator.Child && c.Initiator.AgeYears >= 9f);
        public static readonly Cond DifferentFamilies = K("they belong to different families", c => c.DifferentFamilies);

        // how they feel
        public static Cond Attached(float x) => K("the initiator is attached (" + x + "+)", c => c.AttachI >= x);
        public static Cond AttachedBoth(float x) => K("both are attached (" + x + "+)", c => c.AttachI >= x && c.AttachR >= x);
        public static Cond Trusting(float x) => K("the initiator trusts them (" + x + "+)", c => c.TrustI >= x);
        public static Cond Distrusting(float x) => K("the recipient trusts the initiator little (under " + x + ")", c => c.TrustR < x);
        public static Cond Familiar(float x) => K("they know each other (" + x + "+)", c => c.FamiliarityBetween >= x);
        public static Cond Strangers(float x) => K("they hardly know each other (under " + x + ")", c => c.FamiliarityBetween < x);
        public static Cond TensionAtLeast(float x) => K("there is tension (" + x + "+)", c => c.Tension >= x);
        public static Cond TensionBelow(float x) => K("things are calm (tension under " + x + ")", c => c.Tension < x);
        public static Cond ApartHours(float h) => K("they have not spent time together for " + h + " hours", c => c.HoursApart >= h);
        public static Cond TogetherWithin(float h) => K("they spent time together in the last " + h + " hours", c => c.HoursApart <= h);
        public static Cond OpinionAtLeast(int x) => K("the initiator thinks well of them (" + x + "+)", c => c.OpinionOfRecipient >= x);
        public static Cond OpinionAtMost(int x) => K("the initiator thinks poorly of them (" + x + " or less)", c => c.OpinionOfRecipient <= x);
        public static Cond RecipientOpinionAtLeast(int x) => K("they think well of the initiator (" + x + "+)", c => c.OpinionOfInitiator >= x);
        public static Cond RecipientOpinionAtMost(int x) => K("they think poorly of the initiator (" + x + " or less)", c => c.OpinionOfInitiator <= x);
        public static readonly Cond OnGoodTerms = K("neither dislikes the other", c => c.OpinionOfRecipient >= -5 && c.OpinionOfInitiator >= -5);
        public static readonly Cond AttachmentUneven = K("the initiator is far more attached than they are", c => c.AttachI - c.AttachR >= 15f);

        // what is happening to them
        public static readonly Cond RecipientTroubled = K("the recipient is distressed, grieving or unwell", c => c.Recipient.Distressed || c.Recipient.Grieving || c.Recipient.Ill);
        public static readonly Cond RecipientDistressed = K("the recipient is distressed", c => c.Recipient.Distressed || c.Recipient.Grieving);
        public static readonly Cond InitiatorTroubled = K("the initiator is distressed or grieving", c => c.Initiator.Distressed || c.Initiator.Grieving);
        public static readonly Cond InitiatorNeedsCare = K("the initiator is distressed, grieving or unwell", c => c.Initiator.Distressed || c.Initiator.Grieving || c.Initiator.Ill);
        public static readonly Cond RecipientAchieved = K("the recipient has just achieved something", c => c.Recipient.Achievement != null);
        public static readonly Cond InitiatorAchieved = K("the initiator has just achieved something", c => c.Initiator.Achievement != null);
        public static readonly Cond EitherAchieved = K("one of them has just achieved something", c => c.Initiator.Achievement != null || c.Recipient.Achievement != null);
        public static readonly Cond RecipientNewcomer = K("the recipient has just joined the colony", c => c.Recipient.Newcomer);
        public static readonly Cond RecipientInNeed = K("the recipient is distressed, unwell or new here", c => c.Recipient.Distressed || c.Recipient.Ill || c.Recipient.Newcomer);
        public static readonly Cond RecipientBedtime = K("the child is at the end of their day", c => c.Recipient.Bedtime);
        public static readonly Cond RecipientWronged = K("the recipient was recently treated badly by the third person", c => c.Third != null && c.Recipient.Wronged.Contains(c.Third.Id));
        public static readonly Cond InitiatorWronged = K("the initiator was recently treated badly by the recipient", c => c.Initiator.Wronged.Contains(c.Recipient.Id));
        public static readonly Cond InitiatorWrongedByThem = InitiatorWronged;
        public static readonly Cond RecipientWrongedByInitiator = K("the initiator recently wronged the recipient", c => c.Recipient.Wronged.Contains(c.Initiator.Id));
        public static readonly Cond ChildThirdWrongedByRecipient = K("the child recently suffered at the recipient's hands", c => c.Third != null && c.Third.Wronged.Contains(c.Recipient.Id));
        public static readonly Cond HasThird = K("a third person is involved", c => c.Third != null);
        public static readonly Cond InitiatorWrongedByAdult = K("the initiator was recently put out by the recipient's behaviour", c => c.Initiator.Wronged.Contains(c.Recipient.Id));

        // skills and character
        public static readonly Cond CultureClash = K("their values differ", c => new[] { Dim.Warmth, Dim.Steadiness, Dim.Rootedness, Dim.Devotion, Dim.Status }
            .Any(d => Math.Abs(c.Initiator.Axis(d) - c.Recipient.Axis(d)) >= 0.9f));
        public static Cond SkillGap(int gap) => K("the initiator is " + gap + "+ levels ahead in something", c => c.Initiator.Skills.Any(kv => kv.Value - c.Recipient.Skill(kv.Key) >= gap));
        public static readonly Cond RecipientHasLearning = K("the recipient has something left to learn", c => c.Recipient.Skills.Any(kv => kv.Value < 10));
        public static readonly Cond SharedInterest = K("they share an interest", c => c.Initiator.Skills.Any(kv => kv.Value >= 3 && c.Recipient.Skill(kv.Key) >= 3 && Math.Abs(kv.Value - c.Recipient.Skill(kv.Key)) <= 5));
        public static readonly Cond InitiatorValuesRecipientSkill = K("the initiator is good at what the child achieved", c => c.Recipient.Achievement != null && c.Initiator.Skill(c.Recipient.Achievement) >= 6);
        public static readonly Cond Eligible = K("a romance is possible under the game's own rules", c => c.RomanticallyEligible);

        // rifts, promises, memories
        public static readonly Cond OpenRift = K("they have an open rift", c => c.Rift != null);
        public static readonly Cond NoRift = K("they have no open rift", c => c.Rift == null);
        public static readonly Cond RiftOrTension = K("they have a rift or lasting tension", c => c.Rift != null || c.Tension >= 25f);
        public static readonly Cond RiftMayBeMended = K("a rift is open and has not been tried too recently", c => c.Rift != null && c.Registry.MayTryReconcile(c.Rift.Id, c.Now) && c.Tension < 75f);
        public static readonly Cond SharedMemory = K("the family shares a defining memory involving them", c => c.Initiator.LineageId != 0 && c.Registry.LineageHasMemoryOf(c.Initiator.LineageId, c.Initiator.Id, c.Recipient.Id));
        public static readonly Cond OverlookedChild = K("the child has had far less of the parent's time than a sibling", c =>
            c.Registry.IsOverlooked(c.InitiatorIsParent ? c.Initiator.Id : c.Recipient.Id, c.InitiatorIsParent ? c.Recipient.Id : c.Initiator.Id));
        public static readonly Cond ChildNeglected = K("the child has had little of the parent's time lately", c =>
            c.Registry.IsOverlooked(c.Initiator.Id, c.Recipient.Id) || c.HoursApart >= 72f);
        public static readonly Cond LeftOutSibling = K("the initiator has been left out of what their siblings do together", c => c.Registry.IsLeftOutSibling(c.Initiator.Id));
        public static readonly Cond TurnedDownBefore = K("the initiator has been turned down trying to join in", c => c.Registry.TimesTurnedDown("B3-01", c.Initiator.Id, c.Now - 6 * ActivityTuning.TicksPerDay) >= 2
            || c.Registry.TimesTurnedDown("C3-01", c.Initiator.Id, c.Now - 6 * ActivityTuning.TicksPerDay) >= 2);
        public static readonly Cond NoOpenPromise = K("there is no promise already open between them", c => !c.Registry.HasOpenPromise(c.Initiator.Id, c.Recipient.Id) && !c.Registry.HasOpenPromise(c.Recipient.Id, c.Initiator.Id));
        public static readonly Cond ThirdIsRecipientsParent = K("the third person is the child's parent", c => c.Third != null && c.Registry.IsParentOf(c.Third.Id, c.Recipient.Id));
        public static readonly Cond InitiatorIsThirdsParent = K("the initiator is the child's parent", c => c.Third != null && c.Registry.IsParentOf(c.Initiator.Id, c.Third.Id));
        public static readonly Cond RecipientIsThirdsParent = K("the recipient is the child's parent", c => c.Third != null && c.Registry.IsParentOf(c.Recipient.Id, c.Third.Id));
        public static readonly Cond ThirdAdmiresRecipient = K("the child admires the recipient", c => c.ThirdAttachToRecipient >= 35);
        public static readonly Cond ThirdLikesInitiator = K("the child is attached to the initiator", c => c.ThirdAttachToInitiator >= 40);
        public static readonly Cond ThirdTrustsInitiator = K("the child's parent thinks well of the initiator", c => c.ThirdOpinionOfInitiator >= 25);
        public static readonly Cond ThirdTrustsMediator = K("the third person thinks at least kindly of the initiator", c => c.ThirdOpinionOfInitiator >= 10);
        public static readonly Cond ThirdOtherFamily = K("the third person is from another family than the recipient", c => c.Third != null && c.Third.LineageId != 0 && c.Third.LineageId != c.Recipient.LineageId);
        public static readonly Cond HelpedTheirChild = K("the recipient recently helped the initiator's child", c => c.Third != null && c.Registry.HelpedChildRecently(c.Recipient.Id, c.Third.Id, c.Now));
        public static readonly Cond BrokenPromise = K("a recorded promise to the child went unkept", c => c.Broken != null);

        // the two families
        public static readonly Cond TieStrained = K("their families are at odds", c => c.FamilyTie != null && (c.FamilyTie.State == FamilyTieState.Strained || c.FamilyTie.State == FamilyTieState.Feuding || c.FamilyTie.Grievance >= 15f));
        public static readonly Cond TieStrainedOrAwkward = K("their families are strained or only barely acquainted", c => c.FamilyTie == null || c.FamilyTie.State != FamilyTieState.Allied);
        public static Cond TieGrievance(float x) => K("the families have a grievance (" + x + "+)", c => c.FamilyTie != null && c.FamilyTie.Grievance >= x);
        public static Cond TieGoodwill(float x) => K("the families have goodwill (" + x + "+)", c => c.FamilyTie != null && c.FamilyTie.Goodwill >= x);
        public static Cond TieShared(int n) => K("the families have shared " + n + "+ moments", c => c.FamilyTie != null && c.FamilyTie.Shared >= n);
        public static readonly Cond TieLowStanding = K("the families do not stand well together", c => c.FamilyTie == null || c.FamilyTie.Standing <= 20f);
        public static readonly Cond OldGrievance = K("an old grievance exists", c => (c.FamilyTie != null && c.FamilyTie.GrievanceTick > 0 && c.Now - c.FamilyTie.GrievanceTick >= 8 * ActivityTuning.TicksPerDay)
            || (c.Initiator.LineageId != 0 && c.Registry.LineageHasRupture(c.Initiator.LineageId)));
        public static readonly Cond ThirdFamiliesAtOdds = K("the recipient's and third person's families are at odds", c =>
        {
            if (c.Third == null) return false;
            var tie = c.Registry.GetTie(c.Recipient.LineageId, c.Third.LineageId);
            return tie != null && tie.Grievance >= 15f;
        });
        public static readonly Cond ChildFriendshipsGrowing = K("children of both families have had several good moments", c =>
            c.FamilyTie != null && c.FamilyTie.Grievance >= 10f && c.Registry.GoodChildMomentsBetween(c.Initiator.LineageId, c.Recipient.LineageId, c.Now - 14 * ActivityTuning.TicksPerDay) >= 3);
    }

    /// <summary>The 90 relationship events. Each is data: when it can happen, what moves a person to do it, how it is received, and what it can lead to.</summary>
    public static class EventCatalog
    {
        public static readonly IReadOnlyList<EventDef> All = Build();

        private static readonly Dictionary<string, EventDef> byId = All.ToDictionary(e => e.Id, StringComparer.Ordinal);

        public static EventDef Get(string id) => byId.TryGetValue(id, out var d) ? d : null;

        private const EventBucket Fam = EventBucket.IntraFamily, Ind = EventBucket.Individual, Inter = EventBucket.InterFamily;
        private const EventScenario AA = EventScenario.AdultAdult, AC = EventScenario.AdultChild, CC = EventScenario.ChildChild;
        private const EventWeight Amb = EventWeight.Ambient, Mean = EventWeight.Meaningful, Def = EventWeight.Defining;
        private const ActivityKind None = ActivityKind.None;

        private sealed class B
        {
            public readonly EventDef D;
            private object[] extraReception = new object[0];

            public B(string id, string key, EventBucket bucket, EventScenario scenario, EventWeight weight, float cooldownDays, Arc arc)
            {
                D = new EventDef { Id = id, Key = key, Bucket = bucket, Scenario = scenario, Weight = weight, CooldownDays = cooldownDays, Arc = arc };
            }

            public B Skill(string skill, float share) { D.Skill = skill; D.SkillShare = share; return this; }
            public B When(params Cond[] conditions) { D.Opportunity = conditions; return this; }
            public B Motive(float baseValue, params object[] terms) { D.Motive = new Drive(baseValue, terms); return this; }
            public B Receives(params object[] terms) { extraReception = terms; return this; }
            public B Fx(EventEffect fx) { D.Effects |= fx; return this; }
            public B Fav(params ActivityKind[] kinds) { D.Favored = kinds; return this; }
            public B Only(params ActivityKind[] kinds) { D.Needs = kinds; return this; }
            public B Third(bool child = false) { D.NeedsThird = true; D.ThirdIsChild = child; return this; }
            public B Teaches(string skill = "best", bool recipientTeaches = false) { D.RecipientTeaches = recipientTeaches; D.TeachSkill = skill; D.Effects |= EventEffect.Teaches; return this; }
            public B AboutPromise() { D.ConcernsPromise = true; return this; }
            public B ByActivity() { D.ActivityOnly = true; return this; }
            public B TieOfThird() { D.TieIsRecipientThird = true; return this; }

            public EventDef Done()
            {
                // How the other person takes it: how much they trust and like the initiator, how tense things are, how steady they are.
                var terms = new List<object>
                {
                    Factor.RecipientTrust, 0.25f, Factor.RecipientAttachment, 0.15f, Factor.RecipientTension, -0.25f,
                    Factor.RecipientWarmth, 0.12f, Factor.RecipientMood, 0.10f, Factor.RecipientOpinion, 0.12f
                };
                terms.AddRange(extraReception);
                D.Reception = new Drive(0.42f, terms.ToArray());
                return D;
            }
        }

        private static List<EventDef> Build()
        {
            var e = new List<EventDef>();

            // ============================================================ A1: relatives, adult with adult
            e.Add(new B("A1-01", "NeededConversation", Fam, AA, Mean, 5, Arc.Warm).Skill("Social", 0.4f).Fx(EventEffect.EasesRift)
                .When(C.Family, C.Where("tension or a long time apart", c => c.Tension >= 20f || c.HoursApart >= 72f))
                .Motive(0.2f, Factor.Attachment, 0.25f, Factor.Warmth, 0.2f, Factor.Mood, 0.08f, Factor.Tension, 0.2f, Factor.Apart, 0.15f).Done());
            e.Add(new B("A1-02", "ThoughtfulGesture", Fam, AA, Mean, 3, Arc.Warm)
                .When(C.Family, C.RecipientTroubled, C.Attached(15))
                .Motive(0.15f, Factor.Warmth, 0.35f, Factor.Attachment, 0.3f, Factor.Hearth, 0.1f)
                .Receives(Factor.RecipientTension, -0.05f).Done());
            e.Add(new B("A1-03", "FamilyDisagreement", Fam, AA, Mean, 6, Arc.Clash).Skill("Social", 0.35f).Fx(EventEffect.StrainsRift)
                .When(C.Family, C.Where("conflicting values or unresolved grievances", c => c.Tension >= 25f || (c.Rift != null) || new[] { Dim.Warmth, Dim.Steadiness, Dim.Rootedness, Dim.Devotion, Dim.Status }.Any(d => Math.Abs(c.Initiator.Axis(d) - c.Recipient.Axis(d)) >= 0.9f)))
                .Motive(0.12f, Factor.Steadiness, -0.25f, Factor.Tension, 0.3f, Factor.Pride, 0.15f).Done());
            e.Add(new B("A1-04", "EveningTogether", Fam, AA, Amb, 2, Arc.Light).Fav(ActivityKind.GameNight, ActivityKind.Meal, ActivityKind.Picnic)
                .When(C.Family, C.Attached(20), C.NoRift, C.TensionBelow(30))
                .Motive(0.15f, Factor.Attachment, 0.3f, Factor.Revelry, 0.15f, Factor.Familiarity, 0.1f, Factor.Apart, 0.2f).Done());
            e.Add(new B("A1-05", "UnspokenGrudge", Fam, AA, Mean, 7, Arc.Rivalry).Fx(EventEffect.EasesRift | EventEffect.StrainsRift)
                .When(C.Family, C.TensionAtLeast(30), C.Where("an unresolved grievance", c => c.Rift != null || c.OpinionOfRecipient <= -10))
                .Motive(0.12f, Factor.Tension, 0.3f, Factor.Steadiness, -0.15f, Factor.Warmth, -0.15f, Factor.Rift, 0.15f).Done());
            e.Add(new B("A1-06", "PassingTheTorch", Fam, AA, Mean, 4, Arc.Warm).Skill("Intellectual", 0.5f).Teaches().Fav(ActivityKind.Learning)
                .When(C.Family, C.SkillGap(4), C.Attached(15), C.NoRift)
                .Motive(0.15f, Factor.Scholarly, 0.25f, Factor.Skill, 0.15f, Factor.Warmth, 0.1f, Factor.Attachment, 0.2f).Done());
            e.Add(new B("A1-07", "RememberingTogether", Fam, AA, Mean, 6, Arc.Warm).Fav(ActivityKind.Meal, ActivityKind.Storytelling, ActivityKind.Celebration)
                .When(C.Family, C.SharedMemory, C.Attached(20))
                .Motive(0.15f, Factor.Hearth, 0.2f, Factor.Attachment, 0.25f, Factor.Familiarity, 0.1f).Done());
            e.Add(new B("A1-08", "DefendingARelative", Fam, AA, Mean, 5, Arc.Warm).Fx(EventEffect.Confronts).Third()
                .When(C.Family, C.RecipientWronged, C.Attached(30))
                .Motive(0.15f, Factor.Attachment, 0.3f, Factor.Courage, 0.3f, Factor.Warmth, 0.1f).Done());
            e.Add(new B("A1-09", "ConflictingPriorities", Fam, AA, Mean, 6, Arc.Clash)
                .When(C.Family, C.AttachmentUneven, C.ApartHours(48))
                .Motive(0.12f, Factor.Attachment, 0.25f, Factor.Apart, 0.3f, Factor.Steadiness, -0.1f).Done());
            e.Add(new B("A1-10", "QuietReconciliation", Fam, AA, Mean, 3, Arc.Repair).Skill("Social", 0.3f).Fx(EventEffect.EasesRift)
                .When(C.Family, C.RiftMayBeMended, C.Trusting(15))
                .Motive(0.2f, Factor.Warmth, 0.25f, Factor.Rift, 0.2f, Factor.Attachment, 0.2f).Done());

            // ============================================================ A2: relatives, adult with child
            e.Add(new B("A2-01", "BedtimeStory", Fam, AC, Amb, 1, Arc.Story).Skill("Social", 0.35f).Fav(ActivityKind.Storytelling).ByActivity()
                .When(C.InitiatorIsAdult, C.RecipientBedtime, C.InitiatorCaresFor)
                .Motive(0.6f, Factor.Warmth, 0.2f, Factor.Attachment, 0.2f).Done());
            e.Add(new B("A2-02", "AProudMoment", Fam, AC, Mean, 3, Arc.Warm)
                .When(C.InitiatorIsAdult, C.RecipientAchieved, C.InitiatorCaresFor)
                .Motive(0.3f, Factor.Attachment, 0.3f, Factor.Warmth, 0.15f, Factor.Pride, 0.1f)
                .Receives(Factor.RecipientSteadiness, 0.08f).Done());
            e.Add(new B("A2-03", "PlayingFavorites", Fam, AC, Mean, 7, Arc.Clash).Fx(EventEffect.EasesRift)
                .When(C.OverlookedChild, C.Where("the overlooked child or the noticing parent acts", c => c.Initiator.Child ? c.Kind == BondKind.Child : c.InitiatorIsParent))
                .Motive(0.15f, Factor.Attachment, 0.2f, Factor.Steadiness, -0.15f, Factor.Warmth, 0.15f, Factor.Rift, 0.2f).Done());
            e.Add(new B("A2-04", "ALessonLearned", Fam, AC, Mean, 2, Arc.Warm).Skill("Intellectual", 0.5f).Teaches().Fav(ActivityKind.Learning)
                .When(C.InitiatorIsAdult, C.SkillGap(3), C.RecipientHasLearning, C.Attached(10))
                .Motive(0.2f, Factor.Scholarly, 0.25f, Factor.Social, 0.1f, Factor.Attachment, 0.2f)
                .Receives(Factor.RecipientMood, 0.05f).Done());
            e.Add(new B("A2-05", "SeekingComfort", Fam, AC, Mean, 2, Arc.Longing)
                .When(C.InitiatorIsChild, C.Where("the child is frightened, sad or unwell", c => c.Initiator.Distressed || c.Initiator.Grieving || c.Initiator.Ill), C.Attached(25))
                .Motive(0.25f, Factor.Attachment, 0.35f, Factor.Trust, 0.2f, Factor.Steadiness, -0.15f)
                .Receives(Factor.RecipientWarmth, 0.1f, Factor.RecipientSteadiness, 0.1f).Done());
            e.Add(new B("A2-06", "GrowingIndependence", Fam, AC, Mean, 6, Arc.Clash)
                .When(C.ChildOlder, C.Where("the recipient is the parent", c => c.Kind == BondKind.Child))
                .Motive(0.15f, Factor.Devotion, -0.3f, Factor.Courage, 0.15f, Factor.Pride, 0.1f)
                .Receives(Factor.RecipientSteadiness, 0.1f).Done());
            e.Add(new B("A2-07", "ABrokenPromise", Fam, AC, Mean, 3, Arc.Repair).AboutPromise()
                .When(C.InitiatorIsAdult, C.BrokenPromise)
                .Motive(0.3f, Factor.Warmth, 0.3f, Factor.Attachment, 0.2f)
                .Receives(Factor.RecipientTrust, 0.1f).Done());
            e.Add(new B("A2-08", "ASharedDiscovery", Fam, AC, Amb, 2, Arc.Light).Fx(EventEffect.Promise)
                .Only(ActivityKind.NatureWalk, ActivityKind.Picnic, ActivityKind.Learning, ActivityKind.Playdate, ActivityKind.GameNight)
                .When(C.InitiatorIsChild, C.Attached(15))
                .Motive(0.3f, Factor.Scholarly, 0.2f, Factor.Attachment, 0.25f, Factor.Revelry, 0.1f).Done());
            e.Add(new B("A2-09", "ParentalDisapproval", Fam, AC, Mean, 5, Arc.Clash).Skill("Social", 0.35f).Fx(EventEffect.StrainsRift)
                .When(C.InitiatorIsAdult, C.InitiatorCaresFor, C.Where("conduct at odds with the parent's values", c => c.Initiator.Wronged.Contains(c.Recipient.Id) || new[] { Dim.Warmth, Dim.Steadiness, Dim.Devotion, Dim.Status }.Any(d => Math.Abs(c.Initiator.Axis(d) - c.Recipient.Axis(d)) >= 0.9f)))
                .Motive(0.12f, Factor.Pride, 0.2f, Factor.Steadiness, -0.15f, Factor.Tension, 0.2f).Done());
            e.Add(new B("A2-10", "MakingTimeForThem", Fam, AC, Mean, 4, Arc.Warm).Fx(EventEffect.Promise | EventEffect.EasesRift)
                .When(C.InitiatorIsAdult, C.InitiatorCaresFor, C.ChildNeglected)
                .Motive(0.2f, Factor.Warmth, 0.2f, Factor.Devotion, 0.25f, Factor.Attachment, 0.15f, Factor.Apart, 0.2f).Done());

            // ============================================================ A3: relatives, child with child
            e.Add(new B("A3-01", "SiblingAdventure", Fam, CC, Amb, 2, Arc.Light).Fav(ActivityKind.NatureWalk, ActivityKind.Playdate)
                .When(C.Siblings, C.Attached(10))
                .Motive(0.25f, Factor.Attachment, 0.25f, Factor.Revelry, 0.15f, Factor.Courage, 0.15f).Done());
            e.Add(new B("A3-02", "TheFavoriteToy", Fam, CC, Amb, 3, Arc.Rivalry).Fav(ActivityKind.Playdate, ActivityKind.GameNight)
                .When(C.Siblings, C.Where("a little friction or a bad mood", c => c.Tension >= 8f || c.Initiator.Mood < 0.45f || c.Recipient.Mood < 0.45f))
                .Motive(0.15f, Factor.Tension, 0.2f, Factor.Steadiness, -0.2f, Factor.Revelry, 0.1f).Done());
            e.Add(new B("A3-03", "StandingUpForASibling", Fam, CC, Mean, 4, Arc.Warm).Fx(EventEffect.Confronts).Third()
                .When(C.Siblings, C.RecipientWronged, C.Attached(20))
                .Motive(0.15f, Factor.Attachment, 0.3f, Factor.Courage, 0.3f).Done());
            e.Add(new B("A3-04", "ASecretBetweenThem", Fam, CC, Mean, 6, Arc.Deep)
                .When(C.Siblings, C.Trusting(50), C.AttachedBoth(40), C.TogetherWithin(36))
                .Motive(0.15f, Factor.Trust, 0.35f, Factor.Attachment, 0.2f, Factor.Familiarity, 0.1f).Done());
            e.Add(new B("A3-05", "SiblingCompetition", Fam, CC, Mean, 3, Arc.Rivalry).Fav(ActivityKind.Competition, ActivityKind.GameNight)
                .When(C.Siblings, C.SharedInterest)
                .Motive(0.2f, Factor.Pride, 0.2f, Factor.Courage, 0.1f, Factor.Revelry, 0.1f).Done());
            e.Add(new B("A3-06", "MakingUpAfterAFight", Fam, CC, Mean, 2, Arc.Repair).Fx(EventEffect.EasesRift)
                .When(C.Siblings, C.Where("a recent quarrel", c => c.Rift != null || c.Tension >= 20f))
                .Motive(0.25f, Factor.Warmth, 0.3f, Factor.Attachment, 0.2f, Factor.Pride, -0.15f).Done());
            e.Add(new B("A3-07", "TheProtectiveOlderSibling", Fam, CC, Mean, 3, Arc.Longing)
                .When(C.Siblings, C.InitiatorOlder, C.Where("the younger sibling needs help", c => c.Recipient.Distressed || c.Recipient.Ill || c.Recipient.Grieving), C.Attached(15))
                .Motive(0.2f, Factor.Warmth, 0.3f, Factor.Attachment, 0.25f).Done());
            e.Add(new B("A3-08", "FeelingLeftOut", Fam, CC, Mean, 5, Arc.Longing)
                .When(C.Siblings, C.LeftOutSibling)
                .Motive(0.25f, Factor.Attachment, 0.3f, Factor.Steadiness, -0.15f)
                .Receives(Factor.RecipientWarmth, 0.1f).Done());
            e.Add(new B("A3-09", "ImitatingASibling", Fam, CC, Amb, 3, Arc.Light).Teaches(recipientTeaches: true)
                .When(C.Siblings, C.InitiatorYounger, C.Attached(35), C.Where("the older sibling is ahead", c => c.Recipient.BestSkillLevel() > c.Initiator.BestSkillLevel()))
                .Motive(0.2f, Factor.Attachment, 0.35f, Factor.Familiarity, 0.1f).Done());
            e.Add(new B("A3-10", "TheSharedAchievement", Fam, CC, Mean, 4, Arc.Warm).Fx(EventEffect.Defines)
                .Only(ActivityKind.Playdate, ActivityKind.GameNight, ActivityKind.NatureWalk, ActivityKind.Learning, ActivityKind.Competition, ActivityKind.CommunityAid)
                .When(C.Siblings, C.EitherAchieved)
                .Motive(0.3f, Factor.Attachment, 0.25f, Factor.Warmth, 0.15f).Done());

            // ============================================================ B1: outside the family, adult with adult
            e.Add(new B("B1-01", "AnUnexpectedFriendship", Ind, AA, Mean, 4, Arc.Warm).Skill("Social", 0.4f).Fav(ActivityKind.Meal, ActivityKind.GameNight, ActivityKind.NatureWalk, ActivityKind.Picnic)
                .When(C.OpinionAtLeast(5), C.RecipientOpinionAtLeast(0), C.Where("not yet close", c => c.AttachI < 65f))
                .Motive(0.15f, Factor.Warmth, 0.2f, Factor.Social, 0.15f, Factor.Revelry, 0.1f, Factor.Opinion, 0.25f).Done());
            e.Add(new B("B1-02", "TheMisunderstoodComment", Ind, AA, Amb, 3, Arc.Clash).Skill("Social", 0.45f)
                .When(C.Where("some incompatibility or tension", c => c.Tension >= 8f || c.OpinionOfRecipient < 10 || new[] { Dim.Warmth, Dim.Steadiness }.Any(d => Math.Abs(c.Initiator.Axis(d) - c.Recipient.Axis(d)) >= 0.8f)))
                .Motive(0.25f, Factor.Social, 0.1f, Factor.Warmth, 0.1f).Done());
            e.Add(new B("B1-03", "AFavorRemembered", Ind, AA, Mean, 5, Arc.Warm).Fav(ActivityKind.SupportVisit, ActivityKind.CommunityAid)
                .When(C.RecipientTroubled)
                .Motive(0.15f, Factor.Warmth, 0.3f, Factor.Opinion, 0.25f).Done());
            e.Add(new B("B1-04", "AnAwkwardApology", Ind, AA, Mean, 5, Arc.Repair).Skill("Social", 0.45f).Fx(EventEffect.EasesRift)
                .When(C.Where("a grievance to answer", c => c.Rift != null || c.Tension >= 25f || c.Recipient.Wronged.Contains(c.Initiator.Id)))
                .Motive(0.2f, Factor.Warmth, 0.3f, Factor.Pride, -0.25f, Factor.Rift, 0.15f).Done());
            e.Add(new B("B1-05", "AGrowingAttraction", Ind, AA, Mean, 6, Arc.Longing).Fx(EventEffect.Romance)
                .When(C.Eligible, C.OpinionAtLeast(15), C.Familiar(15), C.TogetherWithin(120))
                .Motive(0.1f, Factor.Opinion, 0.3f, Factor.Attachment, 0.2f, Factor.Warmth, 0.1f).Done());
            e.Add(new B("B1-06", "APersonalBetrayal", Ind, AA, Def, 14, Arc.Rivalry).Fx(EventEffect.StrainsRift | EventEffect.Incident | EventEffect.Defines)
                .When(C.InitiatorWronged, C.OpinionAtMost(-20))
                .Motive(0.2f, Factor.Tension, 0.2f, Factor.Steadiness, -0.2f, Factor.Pride, 0.15f).Done());
            e.Add(new B("B1-07", "FriendlyCompetition", Ind, AA, Amb, 2, Arc.Light).Fav(ActivityKind.Competition, ActivityKind.GameNight)
                .When(C.OpinionAtLeast(-10), C.SharedInterest)
                .Motive(0.2f, Factor.Pride, 0.2f, Factor.Courage, 0.1f, Factor.Revelry, 0.2f).Done());
            e.Add(new B("B1-08", "AnUnexpectedConfidant", Ind, AA, Mean, 5, Arc.Deep).Skill("Social", 0.35f)
                .When(C.InitiatorTroubled, C.Trusting(40), C.Attached(35))
                .Motive(0.2f, Factor.Trust, 0.3f, Factor.Attachment, 0.2f, Factor.Steadiness, -0.15f).Done());
            e.Add(new B("B1-09", "DefendingAFriend", Ind, AA, Mean, 5, Arc.Warm).Fx(EventEffect.Confronts).Third()
                .When(C.RecipientWronged, C.Attached(30))
                .Motive(0.15f, Factor.Attachment, 0.3f, Factor.Courage, 0.3f).Done());
            e.Add(new B("B1-10", "DriftingApart", Ind, AA, Amb, 10, Arc.Longing)
                .When(C.Attached(35), C.ApartHours(240), C.NoRift)
                .Motive(0.1f, Factor.Apart, 0.4f, Factor.Warmth, 0.15f, Factor.Attachment, 0.15f).Done());

            // ============================================================ B2: outside the family, adult with child
            e.Add(new B("B2-01", "TheUnexpectedMentor", Ind, AC, Mean, 4, Arc.Warm).Skill("Intellectual", 0.5f).Teaches().Fav(ActivityKind.Learning)
                .When(C.InitiatorIsAdult, C.SkillGap(3), C.RecipientHasLearning, C.Familiar(5))
                .Motive(0.15f, Factor.Scholarly, 0.2f, Factor.Social, 0.1f, Factor.Warmth, 0.2f, Factor.Skill, 0.1f).Done());
            e.Add(new B("B2-02", "AHelpfulNeighbor", Ind, AC, Amb, 3, Arc.Light)
                .When(C.InitiatorIsAdult, C.RecipientInNeed)
                .Motive(0.15f, Factor.Warmth, 0.35f, Factor.Mood, 0.1f).Done());
            e.Add(new B("B2-03", "TheFamiliarFace", Ind, AC, Amb, 2, Arc.Light)
                .When(C.InitiatorIsAdult, C.OpinionAtLeast(5), C.Familiar(8), C.Where("not yet close", c => c.AttachI < 50f))
                .Motive(0.25f, Factor.Warmth, 0.2f, Factor.Familiarity, 0.2f).Done());
            e.Add(new B("B2-04", "TheSternWarning", Ind, AC, Mean, 4, Arc.Clash).Skill("Social", 0.4f)
                .When(C.InitiatorIsAdult, C.InitiatorWronged)
                .Motive(0.15f, Factor.Steadiness, -0.2f, Factor.Pride, 0.15f, Factor.Tension, 0.2f).Done());
            e.Add(new B("B2-05", "AFriendOfTheFamily", Ind, AC, Mean, 7, Arc.Warm).Fx(EventEffect.Defines).Third()
                .When(C.InitiatorIsAdult, C.ThirdIsRecipientsParent, C.ThirdTrustsInitiator, C.OpinionAtLeast(15), C.Familiar(10))
                .Motive(0.15f, Factor.Opinion, 0.2f, Factor.Warmth, 0.2f, Factor.Hearth, 0.15f).Done());
            e.Add(new B("B2-06", "TheUnwelcomeInfluence", Ind, AC, Mean, 7, Arc.Clash).Third(child: true).Fx(EventEffect.Confronts)
                .When(C.ThirdAdmiresRecipient, C.InitiatorIsThirdsParent, C.CultureClash)
                .Motive(0.15f, Factor.Pride, 0.2f, Factor.Steadiness, -0.1f, Factor.Courage, 0.1f).Done());
            e.Add(new B("B2-07", "SharedCuriosity", Ind, AC, Amb, 3, Arc.Light).Skill("Social", 0.4f).Teaches().Fav(ActivityKind.Learning, ActivityKind.Storytelling, ActivityKind.Playdate, ActivityKind.NatureWalk)
                .When(C.InitiatorIsAdult, C.Where("some common ground", c => c.Initiator.Skills.Any(kv => kv.Value >= 4 && c.Recipient.Skill(kv.Key) >= 1)), C.OpinionAtLeast(0))
                .Motive(0.2f, Factor.Scholarly, 0.25f, Factor.Social, 0.15f, Factor.Warmth, 0.1f).Done());
            e.Add(new B("B2-08", "AReassuringPresence", Ind, AC, Mean, 3, Arc.Longing)
                .When(C.InitiatorIsAdult, C.RecipientDistressed, C.Familiar(8))
                .Motive(0.2f, Factor.Warmth, 0.35f, Factor.Steadiness, 0.1f)
                .Receives(Factor.RecipientTrust, 0.1f).Done());
            e.Add(new B("B2-09", "RecognitionOfTalent", Ind, AC, Amb, 4, Arc.Warm).Skill("Social", 0.35f)
                .When(C.InitiatorIsAdult, C.RecipientAchieved, C.InitiatorValuesRecipientSkill)
                .Motive(0.2f, Factor.Warmth, 0.2f, Factor.Social, 0.15f, Factor.Skill, 0.1f).Done());
            e.Add(new B("B2-10", "AnUncomfortableEncounter", Ind, AC, Amb, 4, Arc.Clash).Skill("Social", 0.4f)
                .When(C.InitiatorIsAdult, C.Where("unfamiliar or disliked", c => c.FamiliarityBetween < 10f || c.OpinionOfInitiator < 0))
                .Motive(0.2f, Factor.Social, 0.2f, Factor.Warmth, 0.2f)
                .Receives(Factor.RecipientSteadiness, 0.1f).Done());

            // ============================================================ B3: outside the family, child with child
            e.Add(new B("B3-01", "NewPlaymate", Ind, CC, Amb, 2, Arc.Light).Fav(ActivityKind.Playdate)
                .When(C.OpinionAtLeast(0), C.Strangers(40))
                .Motive(0.25f, Factor.Warmth, 0.2f, Factor.Revelry, 0.2f, Factor.Opinion, 0.1f).Done());
            e.Add(new B("B3-02", "BestFriends", Ind, CC, Mean, 6, Arc.Deep)
                .When(C.AttachedBoth(50), C.Trusting(45))
                .Motive(0.2f, Factor.Attachment, 0.3f, Factor.Trust, 0.2f).Done());
            e.Add(new B("B3-03", "ThePlaygroundDispute", Ind, CC, Amb, 3, Arc.Clash).Fx(EventEffect.StrainsRift).Fav(ActivityKind.Playdate, ActivityKind.GameNight)
                .When(C.Where("a disagreement brewing", c => c.Tension >= 10f || c.Initiator.Mood < 0.4f || c.Recipient.Mood < 0.4f || new[] { Dim.Warmth, Dim.Steadiness }.Any(d => Math.Abs(c.Initiator.Axis(d) - c.Recipient.Axis(d)) >= 0.8f)))
                .Motive(0.15f, Factor.Steadiness, -0.25f, Factor.Tension, 0.2f, Factor.Pride, 0.1f).Done());
            e.Add(new B("B3-04", "TheNewcomer", Ind, CC, Mean, 5, Arc.Warm)
                .When(C.RecipientNewcomer, C.Where("the initiator is settled here", c => !c.Initiator.Newcomer))
                .Motive(0.2f, Factor.Warmth, 0.25f, Factor.Revelry, 0.1f, Factor.Courage, 0.1f).Done());
            e.Add(new B("B3-05", "StandingUpToABully", Ind, CC, Mean, 5, Arc.Warm).Fx(EventEffect.Confronts).Third()
                .When(C.RecipientWronged)
                .Motive(0.1f, Factor.Courage, 0.35f, Factor.Warmth, 0.25f, Factor.Attachment, 0.1f).Done());
            e.Add(new B("B3-06", "DriftingApartChildren", Ind, CC, Amb, 10, Arc.Longing)
                .When(C.Attached(30), C.ApartHours(168), C.NoRift)
                .Motive(0.1f, Factor.Apart, 0.3f, Factor.Warmth, 0.15f).Done());
            e.Add(new B("B3-07", "SharingADiscovery", Ind, CC, Amb, 2, Arc.Light)
                .When(C.InitiatorAchieved, C.Trusting(25))
                .Motive(0.2f, Factor.Attachment, 0.25f, Factor.Revelry, 0.1f, Factor.Trust, 0.15f).Done());
            e.Add(new B("B3-08", "TheExcludedPlaymate", Ind, CC, Mean, 5, Arc.Longing)
                .When(C.TurnedDownBefore)
                .Motive(0.2f, Factor.Attachment, 0.15f, Factor.Steadiness, -0.1f)
                .Receives(Factor.RecipientWarmth, 0.12f, Factor.RecipientMood, 0.05f).Done());
            e.Add(new B("B3-09", "FriendlyRivalry", Ind, CC, Amb, 3, Arc.Rivalry).Fav(ActivityKind.Competition, ActivityKind.GameNight)
                .When(C.SharedInterest, C.OpinionAtLeast(-5))
                .Motive(0.2f, Factor.Pride, 0.2f, Factor.Courage, 0.1f).Done());
            e.Add(new B("B3-10", "AChildhoodPromise", Ind, CC, Mean, 6, Arc.Warm).Fx(EventEffect.Promise)
                .When(C.AttachedBoth(40), C.NoOpenPromise)
                .Motive(0.2f, Factor.Attachment, 0.3f, Factor.Trust, 0.2f).Done());

            // ============================================================ C1: between families, adult with adult
            e.Add(new B("C1-01", "APeaceOffering", Inter, AA, Mean, 8, Arc.Repair).Fx(EventEffect.TiesFamilies | EventEffect.Incident | EventEffect.EasesRift)
                .When(C.TieStrained)
                .Motive(0.1f, Factor.Warmth, 0.3f, Factor.Pride, -0.2f, Factor.Grievance, 0.15f).Done());
            e.Add(new B("C1-02", "TheReconciliationDinner", Inter, AA, Def, 14, Arc.Repair).Skill("Cooking", 0.3f).Fx(EventEffect.TiesFamilies | EventEffect.Bridge | EventEffect.Defines)
                .Only(ActivityKind.Meal, ActivityKind.Reconciliation).Fav(ActivityKind.Reconciliation)
                .When(C.TieStrainedOrAwkward, C.Where("some awkwardness to ease", c => c.Tension >= 10f || (c.FamilyTie != null && (c.FamilyTie.Grievance >= 8f || c.FamilyTie.Standing < 25f))))
                .Motive(0.15f, Factor.Warmth, 0.25f, Factor.Social, 0.1f, Factor.Grievance, 0.1f).Done());
            e.Add(new B("C1-03", "APublicSlight", Inter, AA, Mean, 8, Arc.Rivalry).Skill("Social", 0.35f).Fx(EventEffect.Incident | EventEffect.StrainsRift).Fav(ActivityKind.Celebration, ActivityKind.Meal)
                .When(C.OpinionAtMost(-15))
                .Motive(0.1f, Factor.Warmth, -0.3f, Factor.Pride, 0.2f, Factor.Grievance, 0.2f).Done());
            e.Add(new B("C1-04", "AFamilyFavor", Inter, AA, Mean, 6, Arc.Warm).Fx(EventEffect.TiesFamilies).Fav(ActivityKind.CommunityAid, ActivityKind.SupportVisit)
                .When(C.RecipientInNeed, C.OpinionAtLeast(0))
                .Motive(0.15f, Factor.Warmth, 0.3f, Factor.Opinion, 0.2f).Done());
            e.Add(new B("C1-05", "AFormalDisagreement", Inter, AA, Mean, 7, Arc.Clash).Skill("Social", 0.5f).Fx(EventEffect.TiesFamilies | EventEffect.Incident)
                .When(C.Where("unresolved incident or clashing expectations", c => (c.FamilyTie != null && c.FamilyTie.Grievance >= 10f) || c.Tension >= 20f || new[] { Dim.Warmth, Dim.Devotion, Dim.Status }.Any(d => Math.Abs(c.Initiator.Axis(d) - c.Recipient.Axis(d)) >= 1.0f)))
                .Motive(0.1f, Factor.Social, 0.3f, Factor.Pride, 0.1f, Factor.Grievance, 0.2f).Done());
            e.Add(new B("C1-06", "TheUnexpectedAlliance", Inter, AA, Def, 14, Arc.Deep).Fx(EventEffect.TiesFamilies | EventEffect.Defines).Fav(ActivityKind.CommunityAid, ActivityKind.Celebration)
                .When(C.TieShared(2), C.TieGoodwill(25), C.OpinionAtLeast(20), C.RecipientOpinionAtLeast(20), C.Where("no standing grievance", c => c.FamilyTie == null || c.FamilyTie.Grievance < 15f))
                .Motive(0.15f, Factor.Trust, 0.25f, Factor.Opinion, 0.25f, Factor.Goodwill, 0.15f).Done());
            e.Add(new B("C1-07", "TheSharedCelebration", Inter, AA, Mean, 7, Arc.Warm).Fx(EventEffect.TiesFamilies).Only(ActivityKind.Celebration)
                .When(C.OpinionAtLeast(-5))
                .Motive(0.3f, Factor.Revelry, 0.2f, Factor.Warmth, 0.2f).Done());
            e.Add(new B("C1-08", "TheUnwelcomeInvitation", Inter, AA, Mean, 8, Arc.Clash).Skill("Social", 0.4f).Fx(EventEffect.TiesFamilies | EventEffect.Incident)
                .When(C.Where("the recipient distrusts them", c => c.TrustR < 35f || (c.FamilyTie != null && c.FamilyTie.Grievance >= 10f)))
                .Motive(0.15f, Factor.Warmth, 0.25f, Factor.Social, 0.1f)
                .Receives(Factor.RecipientTrust, 0.15f, Factor.Grievance, -0.1f).Done());
            e.Add(new B("C1-09", "RememberingAnOldFeud", Inter, AA, Mean, 10, Arc.Rivalry).Fx(EventEffect.Incident | EventEffect.TiesFamilies | EventEffect.StrainsRift)
                .Fav(ActivityKind.Meal, ActivityKind.Celebration, ActivityKind.Storytelling)
                .When(C.OldGrievance)
                .Motive(0.1f, Factor.Grievance, 0.2f, Factor.Warmth, -0.2f, Factor.Hearth, 0.1f).Done());
            e.Add(new B("C1-10", "AThirdPartyIntervention", Inter, AA, Mean, 10, Arc.Repair).Skill("Social", 0.45f).Fx(EventEffect.TiesFamilies | EventEffect.Bridge).Third().TieOfThird()
                .When(C.ThirdOtherFamily, C.ThirdFamiliesAtOdds, C.ThirdTrustsMediator, C.Attached(25))
                .Motive(0.1f, Factor.Warmth, 0.3f, Factor.Social, 0.15f, Factor.Attachment, 0.15f).Done());

            // ============================================================ C2: between families, adult with child
            e.Add(new B("C2-01", "TheNeighborsApprentice", Inter, AC, Mean, 5, Arc.Warm).Skill("Intellectual", 0.5f).Teaches().Fx(EventEffect.TiesFamilies).Fav(ActivityKind.Learning)
                .When(C.InitiatorIsAdult, C.SkillGap(3), C.RecipientHasLearning)
                .Motive(0.15f, Factor.Scholarly, 0.2f, Factor.Warmth, 0.15f, Factor.Skill, 0.1f).Done());
            e.Add(new B("C2-02", "TheProtectiveConfrontation", Inter, AC, Mean, 7, Arc.Clash).Skill("Social", 0.4f).Fx(EventEffect.Confronts | EventEffect.Incident | EventEffect.StrainsRift | EventEffect.TiesFamilies).Third(child: true)
                .When(C.InitiatorIsThirdsParent, C.ChildThirdWrongedByRecipient)
                .Motive(0.2f, Factor.Courage, 0.25f, Factor.Steadiness, -0.15f, Factor.Pride, 0.1f).Done());
            e.Add(new B("C2-03", "AHelpingHand", Inter, AC, Amb, 3, Arc.Light).Fx(EventEffect.TiesFamilies)
                .When(C.InitiatorIsAdult, C.RecipientInNeed)
                .Motive(0.15f, Factor.Warmth, 0.35f).Done());
            e.Add(new B("C2-04", "AnUnwelcomeFriendship", Inter, AC, Mean, 8, Arc.Clash).Fx(EventEffect.TiesFamilies | EventEffect.Incident).Third(child: true)
                .When(C.InitiatorIsThirdsParent, C.ThirdAdmiresRecipient, C.TieStrained)
                .Motive(0.1f, Factor.Warmth, -0.15f, Factor.Pride, 0.2f, Factor.Grievance, 0.25f).Done());
            e.Add(new B("C2-05", "TheSharedCaregiver", Inter, AC, Mean, 6, Arc.Warm).Skill("Social", 0.35f).Fx(EventEffect.TiesFamilies | EventEffect.Bridge).Third().Fav(ActivityKind.Playdate)
                .When(C.InitiatorIsAdult, C.ThirdIsRecipientsParent, C.ThirdTrustsInitiator)
                .Motive(0.15f, Factor.Warmth, 0.3f, Factor.Social, 0.1f).Done());
            e.Add(new B("C2-06", "TheYoungAmbassador", Inter, AC, Mean, 6, Arc.Longing).Fx(EventEffect.TiesFamilies | EventEffect.Bridge)
                .When(C.InitiatorIsChild, C.Where("their families are awkward or at odds", c => c.FamilyTie != null && (c.FamilyTie.Grievance >= 5f || c.FamilyTie.Standing < 25f)))
                .Motive(0.2f, Factor.Warmth, 0.35f, Factor.Courage, 0.15f, Factor.Revelry, 0.1f)
                .Receives(Factor.RecipientWarmth, 0.1f).Done());
            e.Add(new B("C2-07", "AFamilysGratitude", Inter, AC, Mean, 6, Arc.Warm).Fx(EventEffect.TiesFamilies | EventEffect.Bridge).Third(child: true)
                .When(C.InitiatorIsThirdsParent, C.HelpedTheirChild)
                .Motive(0.3f, Factor.Warmth, 0.25f, Factor.Opinion, 0.1f).Done());
            e.Add(new B("C2-08", "TheMisjudgedChild", Inter, AC, Mean, 8, Arc.Warm).Fx(EventEffect.TiesFamilies)
                .When(C.InitiatorIsAdult, C.OpinionAtMost(5), C.Where("the adult expects little of them", c => c.FamilyTie == null || c.FamilyTie.Standing < 30f))
                .Motive(0.2f, Factor.Pride, 0.1f, Factor.Social, 0.1f)
                .Receives(Factor.RecipientSteadiness, 0.1f, Factor.RecipientWarmth, 0.05f).Done());
            e.Add(new B("C2-09", "EncouragementAcrossFamilies", Inter, AC, Mean, 5, Arc.Warm).Skill("Social", 0.35f).Fx(EventEffect.TiesFamilies | EventEffect.Bridge)
                .When(C.InitiatorIsAdult, C.RecipientAchieved, C.InitiatorValuesRecipientSkill)
                .Motive(0.25f, Factor.Warmth, 0.2f, Factor.Social, 0.1f).Done());
            e.Add(new B("C2-10", "TheGenerationalBridge", Inter, AC, Def, 14, Arc.Deep).Fx(EventEffect.TiesFamilies | EventEffect.Bridge | EventEffect.Defines).Third(child: true)
                .When(C.ThirdLikesInitiator, C.RecipientIsThirdsParent, C.TieLowStanding, C.OpinionAtLeast(0))
                .Motive(0.2f, Factor.Warmth, 0.2f, Factor.Social, 0.1f, Factor.Opinion, 0.15f).Done());

            // ============================================================ C3: between families, child with child
            e.Add(new B("C3-01", "FriendshipAcrossFamilies", Inter, CC, Mean, 5, Arc.Warm).Fx(EventEffect.TiesFamilies | EventEffect.Bridge).Fav(ActivityKind.Playdate)
                .When(C.Where("they get along", c => c.AttachI >= 20f || c.OpinionOfRecipient >= 10))
                .Motive(0.2f, Factor.Warmth, 0.25f, Factor.Revelry, 0.15f, Factor.Opinion, 0.15f).Done());
            e.Add(new B("C3-02", "TheFamilyRivalry", Inter, CC, Mean, 6, Arc.Rivalry).Fx(EventEffect.Incident | EventEffect.TiesFamilies)
                .When(C.TieStrained)
                .Motive(0.1f, Factor.Grievance, 0.35f, Factor.Pride, 0.1f, Factor.Warmth, -0.1f).Done());
            e.Add(new B("C3-03", "APlaygroundFight", Inter, CC, Mean, 6, Arc.Clash).Fx(EventEffect.Incident | EventEffect.StrainsRift | EventEffect.TiesFamilies).Fav(ActivityKind.Playdate, ActivityKind.Competition)
                .When(C.Where("a quarrel brewing", c => c.Tension >= 15f || (c.FamilyTie != null && c.FamilyTie.Grievance >= 15f)))
                .Motive(0.1f, Factor.Steadiness, -0.25f, Factor.Tension, 0.2f, Factor.Grievance, 0.15f).Done());
            e.Add(new B("C3-04", "ASharedVictory", Inter, CC, Mean, 5, Arc.Warm).Fx(EventEffect.TiesFamilies | EventEffect.Bridge)
                .Only(ActivityKind.Competition, ActivityKind.Playdate, ActivityKind.Learning, ActivityKind.GameNight, ActivityKind.NatureWalk, ActivityKind.CommunityAid)
                .When(C.EitherAchieved)
                .Motive(0.3f, Factor.Warmth, 0.2f, Factor.Revelry, 0.15f).Done());
            e.Add(new B("C3-05", "TheExcludedChild", Inter, CC, Mean, 6, Arc.Longing).Fx(EventEffect.Incident | EventEffect.TiesFamilies)
                .When(C.TurnedDownBefore)
                .Motive(0.2f, Factor.Attachment, 0.15f, Factor.Steadiness, -0.1f).Done());
            e.Add(new B("C3-06", "BreakingTheCycle", Inter, CC, Def, 14, Arc.Deep).Fx(EventEffect.TiesFamilies | EventEffect.Bridge | EventEffect.Defines)
                .When(C.TieGrievance(15), C.AttachedBoth(45), C.Trusting(40))
                .Motive(0.15f, Factor.Attachment, 0.3f, Factor.Courage, 0.15f).Done());
            e.Add(new B("C3-07", "ACrossFamilyPlaydate", Inter, CC, Mean, 5, Arc.Warm).Skill("Social", 0.3f).Fx(EventEffect.TiesFamilies | EventEffect.Bridge).Only(ActivityKind.Playdate)
                .When(C.Attached(30))
                .Motive(0.25f, Factor.Attachment, 0.3f, Factor.Warmth, 0.15f).Done());
            e.Add(new B("C3-08", "TheCompetitiveFamilies", Inter, CC, Amb, 4, Arc.Rivalry).Fx(EventEffect.TiesFamilies).Fav(ActivityKind.Competition, ActivityKind.GameNight)
                .When(C.SharedInterest)
                .Motive(0.2f, Factor.Pride, 0.2f).Done());
            e.Add(new B("C3-09", "DefendingARivalsChild", Inter, CC, Mean, 6, Arc.Warm).Fx(EventEffect.Confronts | EventEffect.Bridge | EventEffect.TiesFamilies).Third()
                .When(C.RecipientWronged, C.Where("their families are not on good terms", c => c.FamilyTie != null && (c.FamilyTie.Grievance >= 10f || c.FamilyTie.Standing < 20f)))
                .Motive(0.1f, Factor.Courage, 0.3f, Factor.Warmth, 0.3f).Done());
            e.Add(new B("C3-10", "TheNextGeneration", Inter, CC, Def, 14, Arc.Deep).Fx(EventEffect.TiesFamilies | EventEffect.Bridge | EventEffect.Defines)
                .When(C.ChildFriendshipsGrowing, C.Attached(30))
                .Motive(0.2f, Factor.Attachment, 0.25f, Factor.Warmth, 0.15f).Done());

            return e;
        }
    }
}
