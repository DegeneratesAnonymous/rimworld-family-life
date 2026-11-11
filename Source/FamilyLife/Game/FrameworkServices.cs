using System;
using System.Collections.Generic;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// The small, deliberate surface the framework offers to optional modules for things that need the live game: the clock, who is free to be called away, moods, a conversation,
    /// a meeting, the developer-test switch. Each member forwards to framework internals that stay internal. Nothing here is a place to add convenience for a single module: if only
    /// one module needs something, it belongs in that module. Owner: the framework.
    /// </summary>
    public static class FrameworkServices
    {
        // ---- time and presence

        /// <summary>The current game tick.</summary>
        public static int Now => Activities.Now;

        /// <summary>The pawn the player is currently playing as (Perspective Shift), or 0.</summary>
        public static int AvatarId => PerspectiveShiftBridge.AvatarId;
        public static bool IsAvatar(Pawn p) => PerspectiveShiftBridge.IsAvatar(p);

        /// <summary>Not drafted, hurt or in a break; and, outside a developer test, not being played or selected.</summary>
        public static bool IsFree(FamilyComponent c, int pawnId) => Governance.Free(c, pawnId);
        public static bool FreeFor(FamilyComponent c, int pawnId) => OfficeDriver.FreeFor(c, pawnId);
        /// <summary>Idle enough to be asked to do something now (or simply undrafted and awake during a developer test).</summary>
        public static bool Ready(Pawn p) => OfficeDriver.Ready(p);
        /// <summary>Awake and able, for when the player asks for something and the pawn need not be idle.</summary>
        public static bool Available(Pawn p) => OfficeDriver.Available(p);
        /// <summary>Why this person should stop (a need, or an event they have been called to), or null.</summary>
        public static string NeedsReason(Pawn p) => OfficeDriver.NeedsReason(p);

        // ---- moods

        /// <summary>Gives a pawn a memory by thought def name (a missing def is ignored with a warning, never an error).</summary>
        public static void GainMemory(Pawn pawn, string thoughtName, Pawn other) => Activities.GainMemory(pawn, thoughtName, other);

        // ---- conversations and meetings

        /// <summary>One cooperative exchange between a post holder and another person, on a topic. Relationship changes go through the shared, daily-limited path.</summary>
        public static ExchangeResult RunConversation(Pawn leader, Pawn other, OfficeKind kind, SkillDef subject, ExchangeTopic topic, int seed, float facility = 0f) => Conversations.Run(leader, other, kind, subject, topic, seed, facility);

        /// <summary>Sends <paramref name="actor"/> to <paramref name="other"/> to hold a meeting the module registered. False if either is not free.</summary>
        public static bool StartMeeting(Pawn actor, Pawn other, JobDef def, int id, bool byPlayer = false, bool recipientConsented = false) => GovernanceMeetings.Start(actor, other, def, id, byPlayer, recipientConsented);
        /// <summary>A number that has not been used before, handed over only when physical work was really finished.</summary>
        public static int NextToken() => GovernanceMeetings.NextToken();

        /// <summary>The person served by one of their own house's post-holders feels it. Relationship changes are made in the exchanges themselves, never here.</summary>
        public static void Served(FamilyRegistry r, OfficeKind kind, Pawn holder, Pawn served, int now) => OfficeDriver.Served(r, kind, holder, served, now);

        // ---- planned events (the framework owns the notice, the saved pending state and the authority checks)

        /// <summary>Plans a registered event for tomorrow and says so with a notice that never blocks. Does nothing if the post is vacant or its holder is not on a map.</summary>
        public static void PlanEvent(int savedKind) => OfficeEvents.Plan(savedKind);
        /// <summary>Begins a registered event now: the framework checks the post and the holder, then the owning module's behaviour runs.</summary>
        public static bool BeginPlannedEvent(int savedKind) => OfficeEvents.Begin(savedKind);
        /// <summary>Removes the notices of the given planned events from the letter stack. Returns a line saying so, if any were there.</summary>
        public static IEnumerable<string> RemoveNotices(params int[] savedKinds) => OfficeEvents.RemoveNotices(savedKinds);
        /// <summary>The in-game year and whether it is the quadrum the yearly events fall in, at a map's longitude.</summary>
        public static int Year(Map m) => OfficeEvents.Year(m);
        public static bool IsAprimay(Map m) => OfficeEvents.IsAprimay(m);

        // ---- the post's work in general

        /// <summary>Furniture or rooms that belong to a school, a library or a study, not a dining hall. Judged by name, because mods name them so.</summary>
        public static bool LooksLikeSchool(ThingDef def) => HallDriver.LooksLikeSchool(def);

        /// <summary>0 to 1: a skill level scaled so 20 is full. A pawn who cannot do the skill is 0.</summary>
        public static float SkillFraction(Pawn p, SkillDef def) { var s = p?.skills?.GetSkill(def); return s == null || s.TotallyDisabled ? 0f : Math.Min(1f, s.Level / 20f); }

        /// <summary>A post's fitness for a Social/Intellect style post (used by posts that have not declared their own).</summary>
        public static float SocialFitness(Pawn p) => p == null || p.skills == null || p.Dead || p.WorkTagIsDisabled(WorkTags.Social) ? 0f : .6f * SkillFraction(p, SkillDefOf.Intellectual) + .4f * SkillFraction(p, SkillDefOf.Social);

        // ---- developer tests

        /// <summary>Set by a module's developer action so a test is not held up by who happens to be busy.</summary>
        public static int ForceUntil { get => OfficeDriver.ForceUntil; set => OfficeDriver.ForceUntil = value; }
        public static bool Forced => OfficeDriver.Forced;
        /// <summary>Written to the log during a developer test only, so a flow that does not start says why.</summary>
        public static void Trace(string text) => OfficeDriver.Trace(text);
        /// <summary>Runs the posts' work now, rather than at the next check.</summary>
        public static void WorkNow(FamilyComponent c, int now, OfficeKind? only = null) => OfficeDriver.WorkNow(c, now, only);
        /// <summary>What stands between a post and its work, in plain lines. Each module contributes its own section.</summary>
        public static List<string> Diagnose(FamilyComponent c, int now, OfficeKind? only = null) => OfficeDriver.Diagnose(c, now, only);
        /// <summary>Shown to the person when a developer action finishes.</summary>
        public static FamilySettings Settings => FamilyLifeMod.Settings;
    }

    /// <summary>
    /// Whether a pawn should stop what a class or an appointment has them doing (a real need, an emergency, danger), and their skill and tiredness. Read on a short timer and cached,
    /// so a job may ask every tick. Public because every module's jobs use the same answer; the cache and the rest stay internal.
    /// </summary>
    public static class PawnReadinessApi
    {
        public static string StopReason(Pawn p, int now) => PawnReadiness.StopReason(p, now);
        public static bool Excusable(string reason) => PawnReadiness.Excusable(reason);
        public static int Level(Pawn p, SkillDef d) => PawnReadiness.Level(p, d);
        public static float Fatigue(Pawn p) => PawnReadiness.Fatigue(p);
        public static bool Danger(Map map) => PawnReadiness.Danger(map);
    }
}
