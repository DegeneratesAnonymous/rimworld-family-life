using System.Collections.Generic;
using System.Linq;
using FamilyLife.Framework;
using RimWorld;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// One letter telling the player that a loaded save refers to modules that are not installed. It only reads the save; it activates and changes nothing a module owns. A new game, a
    /// prepared module (removed on purpose) and a module the player dismissed are never mentioned. Owner: the framework.
    /// </summary>
    internal static class ModuleNotice
    {
        // Modules already announced in this session. A save is announced once per run of the game, not on every reload; a dismissal ("don't show again") is saved with the game instead.
        private static readonly HashSet<string> announced = new HashSet<string>();

        private static string Key(string prefix, string id) => prefix + id.Replace('.', '_');
        private static string NameOf(string id) { var k = Key("WFL_ModuleName_", id); return k.CanTranslate() ? k.Translate().ToString() : id; }

        internal static void ShowIfNeeded(FamilyComponent c)
        {
            var r = c?.Registry; if (r == null) return;
            bool Installed(string id) => FamilyFramework.Catalog.IsRegistered(id);
            AbsentModules.ClearSuppressionForInstalled(r, Installed);
            var absent = AbsentModules.ToReport(r, Installed).Where(m => !announced.Contains(m.ModuleId)).ToList();
            if (absent.Count == 0) return;
            foreach (var m in absent) announced.Add(m.ModuleId);
            var lines = absent.Select(m =>
            {
                string k = Key("WFL_ModuleInactive_", m.ModuleId);
                string inactive = k.CanTranslate() ? k.Translate().ToString() : "WFL_ModuleAbsent_Generic".Translate().ToString();
                if (m.State != ModuleState.Active) inactive += " " + "WFL_ModuleAbsent_Unprepared".Translate();
                return "WFL_ModuleAbsent_Item".Translate(NameOf(m.ModuleId), inactive).ToString();
            });
            string text = string.Join("\n\n", lines) + "\n\n" + "WFL_ModuleAbsent_Footer".Translate();
            var def = DefDatabase<LetterDef>.GetNamedSilentFail("WFL_ModuleAbsent");
            if (def == null) { Log.Warning("[Wil's Family Life] Modules not installed but present in this save: " + string.Join(", ", absent.Select(m => m.ModuleId)) + "."); return; }
            var letter = (ChoiceLetter_ModuleAbsent)LetterMaker.MakeLetter((absent.Count == 1 ? "WFL_ModuleAbsent_TitleOne" : "WFL_ModuleAbsent_TitleMany").Translate(), text, def);
            letter.modules = absent.Select(m => m.ModuleId).ToList();
            Find.LetterStack.ReceiveLetter(letter);
        }
    }

    public class ChoiceLetter_ModuleAbsent : ChoiceLetter
    {
        public List<string> modules = new List<string>();

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                yield return Option_Close;
                yield return new DiaOption("WFL_ModuleAbsent_Quiet".Translate())
                {
                    action = () =>
                    {
                        var r = FamilyComponent.Current?.Registry;
                        if (r != null) foreach (var id in modules) r.Lifecycle.SuppressNotice(id);
                        Find.LetterStack.RemoveLetter(this);
                    },
                    resolveTree = true
                };
            }
        }

        public override void ExposeData() { base.ExposeData(); Scribe_Collections.Look(ref modules, "wflModules", LookMode.Value); if (modules == null) modules = new List<string>(); }
    }
}
