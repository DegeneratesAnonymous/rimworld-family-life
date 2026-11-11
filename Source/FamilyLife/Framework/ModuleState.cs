using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FamilyLife.Domain;

namespace FamilyLife.Framework
{
    // Module-owned state on a shared record. The framework stores it as an opaque, versioned text payload keyed by a stable id and never looks inside; the owning module encodes and
    // decodes it. A module that is absent leaves its payload exactly as it was, so adding the module back later finds its data intact, and no module's code is needed to load a save.

    /// <summary>State a module keeps on a record. <see cref="Encode"/> is called when the game is saved.</summary>
    public interface IModuleState
    {
        string Encode();
    }

    /// <summary>How a module creates, reads and (once) migrates its state. <see cref="Version"/> is written with the payload, so a later format can still read an earlier one.</summary>
    public interface IStateFactory<T> where T : class, IModuleState
    {
        int Version { get; }
        T Create();
        T Decode(StateReader reader, int version);
        /// <summary>Builds the state from the values an older, all-in-one save wrote directly on the record. Called at most once per record, when the module has no payload yet.</summary>
        T FromLegacy(StateReader legacy);
    }

    /// <summary>A small, dependency-free text format: one key per line, lists separated by commas, with the few characters that would break it escaped. Invariant culture.</summary>
    public sealed class StateWriter
    {
        private readonly StringBuilder text = new StringBuilder();

        private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r").Replace(",", "\\c");

        private StateWriter Put(string key, string value) { if (text.Length > 0) text.Append('\n'); text.Append(key).Append('=').Append(value); return this; }

        public StateWriter Int(string key, int value) => value == 0 ? this : Put(key, value.ToString(CultureInfo.InvariantCulture));
        public StateWriter Bool(string key, bool value) => value ? Put(key, "1") : this;
        public StateWriter Float(string key, float value) => value == 0f ? this : Put(key, value.ToString("R", CultureInfo.InvariantCulture));
        public StateWriter Ints(string key, IList<int> values) => values == null || values.Count == 0 ? this : Put(key, string.Join(",", values.Select(v => v.ToString(CultureInfo.InvariantCulture))));
        public StateWriter Strings(string key, IList<string> values) => values == null || values.Count == 0 ? this : Put(key, string.Join(",", values.Select(v => Escape(v ?? ""))));
        public override string ToString() => text.ToString();
    }

    public sealed class StateReader
    {
        private readonly Dictionary<string, string> map = new Dictionary<string, string>();

        public StateReader(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (var line in text.Split('\n'))
            {
                int eq = line.IndexOf('=');
                if (eq > 0) map[line.Substring(0, eq)] = line.Substring(eq + 1);
            }
        }

        public bool Has(string key) => map.ContainsKey(key);
        public int Int(string key, int fallback = 0) => map.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;
        public bool Bool(string key) => map.TryGetValue(key, out var v) && v == "1";
        public float Float(string key, float fallback = 0f) => map.TryGetValue(key, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : fallback;

        public List<int> Ints(string key)
        {
            var list = new List<int>();
            if (map.TryGetValue(key, out var v) && v.Length > 0)
                foreach (var part in v.Split(',')) if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) list.Add(n);
            return list;
        }

        public List<string> Strings(string key)
        {
            var list = new List<string>();
            if (map.TryGetValue(key, out var v) && v.Length > 0) foreach (var part in v.Split(',')) list.Add(Unescape(part));
            return list;
        }

        private static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
                char n = s[++i];
                sb.Append(n == 'n' ? '\n' : n == 'r' ? '\r' : n == 'c' ? ',' : n);
            }
            return sb.ToString();
        }
    }
}

namespace FamilyLife.Domain
{
    using FamilyLife.Framework;

    public sealed partial class CommunityOffice
    {
        /// <summary>Payload ids, their text, the holder each was made for and the format version of each. Parallel lists.</summary>
        public List<string> PayloadIds = new List<string>(), PayloadData = new List<string>();
        public List<int> PayloadHolders = new List<int>(), PayloadVersions = new List<int>();
        private readonly Dictionary<string, IModuleState> stateCache = new Dictionary<string, IModuleState>();
        private readonly Dictionary<string, int> stateVersion = new Dictionary<string, int>();          // the format version each cached state will be written in

        /// <summary>
        /// A module's state on this post. <paramref name="perHolder"/> state belongs to whoever holds the post: it starts fresh for a new holder (so nothing needs to be reset when a
        /// post changes hands, and a module that was absent at the time still gets it right). Other state outlives holders.
        /// </summary>
        public T StateOf<T>(string id, bool perHolder, IStateFactory<T> factory) where T : class, IModuleState
        {
            Align();
            int i = PayloadIds.IndexOf(id);
            if (i >= 0)
            {
                if (perHolder && PayloadHolders[i] != HolderId)
                {
                    var fresh = factory.Create();
                    PayloadHolders[i] = HolderId; PayloadVersions[i] = factory.Version; PayloadData[i] = fresh.Encode(); stateCache[id] = fresh; stateVersion[id] = factory.Version;
                    return fresh;
                }
                if (stateCache.TryGetValue(id, out var cached) && cached is T typed) return typed;
                var decoded = factory.Decode(new StateReader(PayloadData[i]), PayloadVersions[i]);
                stateCache[id] = decoded; stateVersion[id] = factory.Version;
                return decoded;
            }
            int legacy = PayloadIds.IndexOf(LegacyOfficeMigration.PayloadId);
            var made = legacy >= 0 ? factory.FromLegacy(new StateReader(PayloadData[legacy])) : factory.Create();
            PayloadIds.Add(id); PayloadHolders.Add(HolderId); PayloadVersions.Add(factory.Version); PayloadData.Add(made.Encode());
            stateCache[id] = made; stateVersion[id] = factory.Version;
            return made;
        }

        /// <summary>Writes every cached state back into its payload. Called by the save, and by anything that copies the record.</summary>
        public void FlushState()
        {
            Align();
            foreach (var kv in stateCache.ToList())
            {
                int i = PayloadIds.IndexOf(kv.Key);
                if (i >= 0) { PayloadData[i] = kv.Value.Encode(); if (stateVersion.TryGetValue(kv.Key, out var v)) PayloadVersions[i] = v; }          // the payload now holds this version's format
            }
        }

        /// <summary>Forgets the decoded copies (after a load, so the next use reads what the save held).</summary>
        public void DropStateCache() { stateCache.Clear(); stateVersion.Clear(); }

        /// <summary>The four lists always agree in length, even if a hand-edited or damaged save left them uneven.</summary>
        private void Align()
        {
            if (PayloadIds == null) PayloadIds = new List<string>(); if (PayloadData == null) PayloadData = new List<string>();
            if (PayloadHolders == null) PayloadHolders = new List<int>(); if (PayloadVersions == null) PayloadVersions = new List<int>();
            while (PayloadData.Count < PayloadIds.Count) PayloadData.Add("");
            while (PayloadHolders.Count < PayloadIds.Count) PayloadHolders.Add(0);
            while (PayloadVersions.Count < PayloadIds.Count) PayloadVersions.Add(1);
            while (PayloadIds.Count > 0 && PayloadData.Count > PayloadIds.Count) PayloadData.RemoveAt(PayloadData.Count - 1);
            while (PayloadHolders.Count > PayloadIds.Count) PayloadHolders.RemoveAt(PayloadHolders.Count - 1);
            while (PayloadVersions.Count > PayloadIds.Count) PayloadVersions.RemoveAt(PayloadVersions.Count - 1);
        }
    }

    public sealed partial class FamilyRegistry
    {
        /// <summary>Which modules are running, stopped for removal, or failed to stop, in this save. Kept for modules that are not installed.</summary>
        public readonly ModuleLifecycle Lifecycle = new ModuleLifecycle();

        /// <summary>Raised when a post gets a new holder or loses its holder: (registry, post, old holder, new holder). Set once by the framework host; nothing in the domain depends on it.</summary>
        public static System.Action<FamilyRegistry, CommunityOffice, int, int> HolderChanged;

        /// <summary>Encodes every post's module state ready for saving.</summary>
        public void FlushAllState() { foreach (var o in Offices) o.FlushState(); }
    }
}

namespace FamilyLife.Framework
{
    /// <summary>
    /// The values an all-in-one save wrote directly on a post, before module state had its own place. They are read once, at load, and kept as one legacy payload. A module that has no
    /// state of its own yet builds it from that payload the first time it asks; if the module is not installed, the values simply stay there until it is. This list is migration
    /// data: names of what older saves wrote.
    /// </summary>
    public static class LegacyOfficeLabels
    {
        public static readonly string[] Ints = { "nextRound", "roundStart", "lastIllness", "lastTestingYear", "testingStart", "testingSlot", "roundWindow", "routineDay", "routineUsed", "lastVisit", "kinSeen", "otherSeen" };
        public static readonly string[] Bools = { "illnessRound" };
        public static readonly string[] Floats = { "kinQuality", "otherQuality" };
        public static readonly string[] IntLists = { "examined", "testIds", "testPass", "testTry", "missed", "refused", "refusedTick", "testScore", "testExcused" };
        public static readonly string[] StringLists = { "lessonLog", "bookUse", "recentSubjects" };
    }

    public sealed class LegacyOfficeValues
    {
        public readonly Dictionary<string, int> Ints = new Dictionary<string, int>();
        public readonly Dictionary<string, bool> Bools = new Dictionary<string, bool>();
        public readonly Dictionary<string, float> Floats = new Dictionary<string, float>();
        public readonly Dictionary<string, List<int>> IntLists = new Dictionary<string, List<int>>();
        public readonly Dictionary<string, List<string>> StringLists = new Dictionary<string, List<string>>();
    }

    public static class LegacyOfficeMigration
    {
        public const string PayloadId = "wils.legacy.office";

        /// <summary>
        /// Keeps the legacy values as a payload on the post. Nothing happens when there are none (a save written after this change has none) or when the post already holds a legacy
        /// payload, so loading twice never doubles anything.
        /// </summary>
        public static bool Capture(CommunityOffice office, LegacyOfficeValues v)
        {
            if (office == null || v == null || office.PayloadIds.Contains(PayloadId)) return false;
            var w = new StateWriter();
            foreach (var kv in v.Ints) w.Int(kv.Key, kv.Value);
            foreach (var kv in v.Bools) w.Bool(kv.Key, kv.Value);
            foreach (var kv in v.Floats) w.Float(kv.Key, kv.Value);
            foreach (var kv in v.IntLists) w.Ints(kv.Key, kv.Value);
            foreach (var kv in v.StringLists) w.Strings(kv.Key, kv.Value);
            string data = w.ToString();
            if (data.Length == 0) return false;
            office.PayloadIds.Add(PayloadId); office.PayloadData.Add(data); office.PayloadHolders.Add(office.HolderId); office.PayloadVersions.Add(1);
            return true;
        }
    }
}
