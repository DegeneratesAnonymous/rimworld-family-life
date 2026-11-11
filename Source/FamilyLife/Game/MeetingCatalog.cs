using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Framework;
using Verse;
using Verse.AI;

namespace FamilyLife
{
    /// <summary>
    /// One kind of face-to-face meeting a module runs through the framework's shared talk job (a lesson, a check-up): which job it is, how long the two stay together, how the job is
    /// prepared when it starts, and what happens when it was really finished. The framework owns the walking, the reservations and the interruption rules; the module owns the meaning.
    /// </summary>
    public sealed class MeetingDefinition
    {
        public string Id, ModuleId;
        /// <summary>The job def this meeting uses; resolved by name when it is first needed, so a module whose defs are missing is simply not found.</summary>
        public string JobDefName;
        /// <summary>How long the speaker and the other person stay together before the meeting counts as finished.</summary>
        public int Ticks = 180;
        /// <summary>Extra time the listener is willing to wait beyond <see cref="Ticks"/> before giving up.</summary>
        public int ListenerSlackTicks = 1200;
        /// <summary>Sets up the job when it starts (for example the standing place), or does nothing.</summary>
        public Action<Job, Pawn> Prepare;
        /// <summary>Runs when the meeting was really finished: (speaker, other, id, now).</summary>
        public Action<Pawn, Pawn, int, int> Complete;
    }

    public sealed class MeetingCatalog
    {
        private readonly ModuleCatalog modules;
        private readonly Dictionary<string, MeetingDefinition> byJob = new Dictionary<string, MeetingDefinition>();
        private readonly HashSet<string> ids = new HashSet<string>();
        public MeetingCatalog(ModuleCatalog modules) { this.modules = modules; }

        public void Register(MeetingDefinition def)
        {
            if (def == null || def.Complete == null || string.IsNullOrEmpty(def.JobDefName)) throw new RegistrationException("A meeting was registered without its job and its completion.");
            ModuleCatalog.CheckId(def.Id, "Meeting");
            if (!def.Id.StartsWith(def.ModuleId + ".", StringComparison.Ordinal)) throw new RegistrationException("Meeting '" + def.Id + "' must be inside its module's namespace '" + def.ModuleId + ".'.");
            if (!ids.Add(def.Id)) throw new RegistrationException("Meeting '" + def.Id + "' is registered twice.");
            if (byJob.ContainsKey(def.JobDefName)) throw new RegistrationException("Meeting '" + def.Id + "' uses job '" + def.JobDefName + "', which already belongs to '" + byJob[def.JobDefName].Id + "'.");
            byJob[def.JobDefName] = def;
        }

        /// <summary>Whether some installed module defined this job as a meeting (even if that module is stopped, its job must not be mistaken for an ordinary talk).</summary>
        public bool Known(JobDef job) => job != null && byJob.ContainsKey(job.defName);

        /// <summary>The meeting for a job, or null when no module defines it or its module is stopped.</summary>
        public MeetingDefinition Find(JobDef job) => job != null && byJob.TryGetValue(job.defName, out var d) && modules.IsEnabled(d.ModuleId) ? d : null;
    }
}
