using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    public enum EventBucket : byte { IntraFamily = 0, Individual = 1, InterFamily = 2 }

    /// <summary>Who takes part. A child is a supported juvenile (Biotech); without that, only adult events can happen.</summary>
    public enum EventScenario : byte { AdultAdult = 0, AdultChild = 1, ChildChild = 2 }

    /// <summary>Ambient is brief and quiet, Meaningful changes a relationship noticeably, Defining may enter the family's history.</summary>
    public enum EventWeight : byte { Ambient = 0, Meaningful = 1, Defining = 2 }

    /// <summary>How the moment went for the relationship. Each event words all four in its own way.</summary>
    public enum EventTier : byte { Poor = 0, Flat = 1, Good = 2, Great = 3 }

    /// <summary>The shared activity a moment happens during (G01-G12 of the design). None means two people who happened to be free and near.</summary>
    public enum ActivityKind : byte
    {
        None = 0, Meal = 1, Picnic = 2, Celebration = 3, Storytelling = 4, GameNight = 5, Playdate = 6,
        NatureWalk = 7, Competition = 8, Learning = 9, SupportVisit = 10, CommunityAid = 11, Reconciliation = 12
    }

    /// <summary>Side effects an event can have beyond moving the two bonds. Each is applied by the engine, never by the event itself.</summary>
    [Flags]
    public enum EventEffect : ushort
    {
        None = 0,
        EasesRift = 1,        // a good outcome mends or softens an open rift between the two
        StrainsRift = 2,      // a poor outcome between people already at odds may open one
        Promise = 4,          // a good outcome leaves a tracked commitment to spend time together
        Teaches = 8,          // the recipient (or the learner) gains experience
        TiesFamilies = 16,    // moves the standing between the two families, if they are different families
        Incident = 32,        // a poor outcome leaves a grievance between the two families
        Bridge = 64,          // children and young people soften a strained family standing
        Defines = 128,        // a very good or very poor outcome becomes a defining memory of the family
        Confronts = 256,      // a third person is spoken to: a good outcome chastens them, a poor one escalates
        Romance = 512         // a good outcome lets the game try its own romance interaction, under the game's own rules
    }

    /// <summary>
    /// What the game saw about one person, as plain numbers and flags. The domain never looks at a pawn: the game layer fills this in
    /// from what is really true (mood, health, skills, social memories), and an event whose circumstance is not set simply cannot happen.
    /// </summary>
    public sealed class Actor
    {
        public int Id;
        public string Name = "";
        public bool Child;
        public float AgeYears = 30f;
        public int LineageId;                                   // 0 when the person belongs to no listed family
        public float[] Profile;                                 // temperament and interests, see Dim; null when unknown
        public Dictionary<string, int> Skills = new Dictionary<string, int>();
        public float Mood = 0.5f;                               // 0-1
        public bool Distressed;                                 // low mood, a mental state, a fresh loss or rejection
        public bool Grieving;
        public bool Ill;                                        // sick or injured and in need of care
        public string Achievement;                              // a skill they just gained a level in, else null
        public bool Newcomer;                                   // recently joined the colony
        public bool Bedtime;                                    // a child at the end of their day
        public List<int> Wronged = new List<int>();             // people who recently treated them badly (a negative social memory)
        public bool Absent;                                     // away from the home map

        public int Skill(string key) => key != null && Skills != null && Skills.TryGetValue(key, out var v) ? v : 0;

        public float Axis(Dim d)
        {
            if (Profile == null || (int)d >= Profile.Length) return 0f;
            return Math.Max(-1f, Math.Min(1f, Profile[(int)d] / 1.5f));
        }

        public int BestSkillLevel() => Skills == null || Skills.Count == 0 ? 0 : Skills.Values.Max();
    }

    /// <summary>The standing between two families. It is built from incidents and shared experiences, never averaged from members.</summary>
    public sealed partial class FamilyTie
    {
        public int LineageA;                  // always the lower id
        public int LineageB;
        public float Familiarity;             // 0-100, how well the families know each other
        public float Goodwill;                // 0-100, positive shared history
        public float Grievance;               // 0-100, unresolved wrongs
        public int Shared;                    // meaningful shared moments, good or bad
        public int LastTick;
        public int WrongedBy;                 // the person most recently at the centre of a grievance, 0 when none
        public int WrongedPerson;             // and the one wronged
        public int GrievanceTick;             // when the latest grievance was recorded

        public float Standing => Goodwill - Grievance;

        public FamilyTieState State
        {
            get
            {
                if (Grievance >= 45f && Standing < -15f) return FamilyTieState.Feuding;
                if (Grievance >= 20f) return FamilyTieState.Strained;
                if (Familiarity < 12f && Shared < 2) return FamilyTieState.Strangers;
                if (Goodwill >= 55f && Grievance < 10f) return FamilyTieState.Allied;
                if (Goodwill >= 30f) return FamilyTieState.Friendly;
                return FamilyTieState.Acquainted;
            }
        }
    }

    public enum FamilyTieState : byte { Strangers = 0, Acquainted = 1, Friendly = 2, Allied = 3, Strained = 4, Feuding = 5 }

    /// <summary>A commitment the mod actually recorded: someone promised to spend time with someone by a certain day.</summary>
    public sealed partial class Commitment
    {
        public int Id;
        public int FromId;                    // who promised
        public int ToId;                      // and to whom
        public string EventId = "";           // which event made it
        public int CreatedTick;
        public int DueTick;
        public int Importance = 1;            // 1-3
        public byte State;                    // 0 open, 1 kept, 2 broken, 3 acknowledged (the broken promise was talked about)

        public bool IsOpen => State == 0;
    }

    /// <summary>One thing that happened between two people. The log is bounded; it feeds cooldowns, the history view and diagnostics.</summary>
    public sealed partial class EventRecord
    {
        public string EventId = "";
        public int A;                         // the initiator
        public int B;                         // the recipient
        public int Tick;
        public EventTier Tier;
        public EventWeight Weight;
        public byte Setting;                  // ActivityKind
        public int Third;
    }

    /// <summary>Everything an event looks at. Built by the game layer from a pair of real people and the registry's records.</summary>
    public sealed class PairContext
    {
        public FamilyRegistry Registry;
        public Actor Initiator;
        public Actor Recipient;
        public Actor Third;                   // someone else who matters to this moment (an offender, a witness, a caregiver)
        public Bond InitiatorBond;            // how the initiator feels about the recipient; may be missing
        public Bond RecipientBond;
        public BondKind? Kind;                // how they are connected, null when strangers to the family records
        public bool SameFamily;
        public Rift Rift;                     // an open rift between the two, if any
        public Commitment Broken;             // a recorded promise from the initiator to the recipient that went unkept and has not been talked about
        public FamilyTie FamilyTie;           // the standing between their two families, if they belong to different ones
        public ActivityKind Setting;
        public bool FromActivity;             // reported by a game activity (a story, a meal, a gathering) rather than sampled from two people being near
        public int Now;
        public int OpinionOfRecipient;        // the game's own opinion, initiator about recipient
        public int OpinionOfInitiator;
        public int ThirdAttachToRecipient = -1;   // how attached the third person is to the recipient, -1 when unknown
        public int ThirdAttachToInitiator = -1;
        public int ThirdOpinionOfInitiator;       // the third person's opinion of the initiator
        public bool RomanticallyEligible;         // the game's own rules allow a romance between them (adults, orientation, no conflicting bonds)

        public float AttachI => InitiatorBond == null ? 0f : InitiatorBond.Attachment;
        public float AttachR => RecipientBond == null ? 0f : RecipientBond.Attachment;
        public float TrustI => InitiatorBond == null ? 25f : InitiatorBond.Trust;
        public float TrustR => RecipientBond == null ? 25f : RecipientBond.Trust;
        public float TensionI => InitiatorBond == null ? 0f : InitiatorBond.Tension;
        public float TensionR => RecipientBond == null ? 0f : RecipientBond.Tension;
        public float Tension => Math.Max(TensionI, TensionR);
        public float FamiliarityBetween => Math.Max(InitiatorBond == null ? 0f : InitiatorBond.Familiarity, RecipientBond == null ? 0f : RecipientBond.Familiarity);

        /// <summary>Hours since they last spent time together on purpose; a large number when they never have.</summary>
        public float HoursApart
        {
            get
            {
                int last = Math.Max(InitiatorBond == null ? 0 : InitiatorBond.LastQualityTick, RecipientBond == null ? 0 : RecipientBond.LastQualityTick);
                return last <= 0 ? 24f * 30f : Math.Max(0, Now - last) / (float)BondTuning.TicksPerHour;
            }
        }

        public bool Related => Kind.HasValue && Kind.Value != BondKind.Housemate && Kind.Value != BondKind.Ex;
        public bool Siblings => Kind == BondKind.Sibling;
        public bool InitiatorIsParent => Kind == BondKind.Parent;
        public bool InitiatorIsChildOfRecipient => Kind == BondKind.Child;
        public bool DifferentFamilies => !SameFamily && Initiator.LineageId != 0 && Recipient.LineageId != 0 && Initiator.LineageId != Recipient.LineageId;
        public bool Anyone(Func<Actor, bool> test) => test(Initiator) || test(Recipient);
    }

    /// <summary>A named condition, so the reason an event is or is not possible can be shown.</summary>
    public sealed class Cond
    {
        public readonly string Name;
        public readonly Func<PairContext, bool> Test;
        public Cond(string name, Func<PairContext, bool> test) { Name = name; Test = test; }
        public static Cond Or(string name, params Cond[] any) => new Cond(name, c => any.Any(a => a.Test(c)));
    }

    /// <summary>The ways a moment can move a relationship. Gains are scaled down as a bond grows, so routine events never maximize it.</summary>
    public sealed class Arc
    {
        public readonly float Gain;           // attachment on a very good outcome
        public readonly float Trust;
        public readonly float Relief;         // tension taken away on a very good outcome
        public readonly float Cost;           // tension added on a poor outcome
        public readonly float Familiarity;
        public readonly bool Keen;            // the initiator feels closer even when it is not returned

        public Arc(float gain, float trust, float relief, float cost, float familiarity, bool keen = false)
        {
            Gain = gain; Trust = trust; Relief = relief; Cost = cost; Familiarity = familiarity; Keen = keen;
        }

        public static readonly Arc Story = new Arc(0.5f, 0.5f, 1f, 1f, 0f);
        public static readonly Arc Light = new Arc(1.2f, 1.0f, 3f, 3f, 1.0f);
        public static readonly Arc Warm = new Arc(2.5f, 2.0f, 5f, 4f, 1.5f);
        public static readonly Arc Deep = new Arc(4.0f, 3.5f, 10f, 6f, 2.0f);
        public static readonly Arc Repair = new Arc(3.0f, 4.0f, 18f, 8f, 1.0f);
        public static readonly Arc Clash = new Arc(1.5f, 1.5f, 8f, 9f, 0.5f);
        public static readonly Arc Longing = new Arc(2.5f, 1.5f, 4f, 5f, 1.0f, keen: true);
        public static readonly Arc Rivalry = new Arc(1.5f, 1.0f, 3f, 6f, 1.0f);
    }

    /// <summary>One thing that makes a pawn more or less likely to do (or be moved by) something, with its weight.</summary>
    public enum Factor : byte
    {
        Warmth, Steadiness, Devotion, Hearth, Scholarly, Courage, Revelry, Pride, Social,
        Attachment, Trust, Tension, Familiarity, Mood, Apart, Rift,
        RecipientWarmth, RecipientSteadiness, RecipientAttachment, RecipientTrust, RecipientTension, RecipientMood, RecipientDistress,
        Opinion, RecipientOpinion, Grievance, Goodwill, Skill, Age
    }

    /// <summary>A weighted sum of factors around a base value, kept as data so the reason for a result can be listed.</summary>
    public sealed class Drive
    {
        public readonly float Base;
        public readonly List<KeyValuePair<Factor, float>> Terms = new List<KeyValuePair<Factor, float>>();

        public Drive(float baseValue, params object[] pairs)
        {
            Base = baseValue;
            for (int i = 0; i + 1 < pairs.Length; i += 2) Terms.Add(new KeyValuePair<Factor, float>((Factor)pairs[i], Convert.ToSingle(pairs[i + 1])));
        }

        public float Value(PairContext c, string skill)
        {
            float v = Base;
            foreach (var t in Terms) v += t.Value * Measure(c, t.Key, skill);
            return Math.Max(0f, Math.Min(1f, v));
        }

        public List<KeyValuePair<string, float>> Breakdown(PairContext c, string skill)
        {
            var list = new List<KeyValuePair<string, float>> { new KeyValuePair<string, float>("base", Base) };
            foreach (var t in Terms) list.Add(new KeyValuePair<string, float>(t.Key.ToString(), t.Value * Measure(c, t.Key, skill)));
            return list;
        }

        /// <summary>Every factor reads as roughly -1 to 1 (or 0 to 1 for plain amounts), whatever its source.</summary>
        public static float Measure(PairContext c, Factor f, string skill)
        {
            var i = c.Initiator;
            var r = c.Recipient;
            switch (f)
            {
                case Factor.Warmth: return i.Axis(Dim.Warmth);
                case Factor.Steadiness: return i.Axis(Dim.Steadiness);
                case Factor.Devotion: return i.Axis(Dim.Devotion);
                case Factor.Hearth: return i.Axis(Dim.Hearth);
                case Factor.Scholarly: return i.Axis(Dim.Scholarly);
                case Factor.Courage: return i.Axis(Dim.Martial);
                case Factor.Revelry: return i.Axis(Dim.Revelry);
                case Factor.Pride: return i.Axis(Dim.Status);
                case Factor.Social: return Math.Min(20, i.Skill("Social")) / 20f;
                case Factor.Attachment: return c.AttachI / 100f;
                case Factor.Trust: return c.TrustI / 100f;
                case Factor.Tension: return c.Tension / 100f;
                case Factor.Familiarity: return c.FamiliarityBetween / 100f;
                case Factor.Mood: return (i.Mood - 0.5f) * 2f;
                case Factor.Apart: return Math.Min(1f, c.HoursApart / 96f);
                case Factor.Rift: return c.Rift == null ? 0f : c.Rift.Severity / 100f;
                case Factor.RecipientWarmth: return r.Axis(Dim.Warmth);
                case Factor.RecipientSteadiness: return r.Axis(Dim.Steadiness);
                case Factor.RecipientAttachment: return c.AttachR / 100f;
                case Factor.RecipientTrust: return c.TrustR / 100f;
                case Factor.RecipientTension: return c.TensionR / 100f;
                case Factor.RecipientMood: return (r.Mood - 0.5f) * 2f;
                case Factor.RecipientDistress: return r.Distressed ? 1f : 0f;
                case Factor.Opinion: return Math.Max(-1f, Math.Min(1f, c.OpinionOfRecipient / 60f));
                case Factor.RecipientOpinion: return Math.Max(-1f, Math.Min(1f, c.OpinionOfInitiator / 60f));
                case Factor.Grievance: return c.FamilyTie == null ? 0f : c.FamilyTie.Grievance / 100f;
                case Factor.Goodwill: return c.FamilyTie == null ? 0f : c.FamilyTie.Goodwill / 100f;
                case Factor.Skill: return skill == null ? 0f : Math.Min(20, i.Skill(skill)) / 20f;
                case Factor.Age: return Math.Max(0f, Math.Min(1f, (i.AgeYears - r.AgeYears) / 30f));
                default: return 0f;
            }
        }
    }

    public delegate string TextFor(EventTier tier);

    /// <summary>One relationship event. Definitions are data; the engine supplies the behaviour that every event shares.</summary>
    public sealed class EventDef
    {
        public string Id;                                  // e.g. "A1-01"
        public string Key;                                 // the string key part, e.g. "NeededConversation"
        public EventBucket Bucket;
        public EventScenario Scenario;
        public EventWeight Weight;
        public float CooldownDays = 3f;
        public string Skill;                               // the skill that governs how well the initiator performs it; null means character, not skill
        public float SkillShare = 0.35f;                   // how much execution (against reception) decides the outcome
        public Arc Arc = Arc.Warm;
        public EventEffect Effects;
        public ActivityKind[] Favored = new ActivityKind[0];     // settings that make it more likely
        public ActivityKind[] Needs = new ActivityKind[0];       // settings it can only happen in; empty means any
        public bool NeedsThird;
        public Cond[] Opportunity = new Cond[0];
        public Drive Motive = new Drive(0.3f);
        public Drive Reception = new Drive(0.5f);
        public string TeachSkill;                          // for events that teach: the skill, or "best" for the better skill of the two
        public bool ConcernsPromise;                       // the event is about a recorded promise that was broken
        public bool ActivityOnly;                          // happens only when the game's own activity reports it, never from idle sampling
        public bool ThirdIsChild;                          // the child in an adult-child event is the third person, not one of the pair
        public bool TieIsRecipientThird;                   // family standing moves between the recipient's and third person's families

        public bool NeedsChild => Scenario != EventScenario.AdultAdult;
    }

    /// <summary>What happened when an event was carried out. Applied to the registry by <see cref="FamilyRegistry.ApplyEvent"/>.</summary>
    public sealed class EventResult
    {
        public EventDef Def;
        public PairContext Context;
        public EventTier Tier;
        public float Execution;
        public float Reception;
        public float Score;
        public int RiftOpened;                             // id of a rift the event opened, 0 when none
        public bool RiftEased;
        public bool RiftResolved;
        public int PromiseMade;
        public int PromiseKept;
        public string XpSkill;                             // experience to give, applied by the game
        public float XpAmount;
        public int XpTo;                                   // who learns
        public bool FamilyTieChanged;
        public bool BecameMemory;
        public float InitiatorFeeling;                     // -1 to 1: how the initiator walks away feeling
        public float RecipientFeeling;
    }

    /// <summary>One line of the developer view: why an event is, or is not, possible for a pair.</summary>
    public sealed class EligibilityLine
    {
        public EventDef Def;
        public bool Eligible;
        public string Reason = "";
        public float Motive;
        public float Weight;
    }
}
