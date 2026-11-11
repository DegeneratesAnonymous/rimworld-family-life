using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using Xunit;

namespace FamilyLife.Tests
{
    /// <summary>
    /// Reads the office records of a real save written before module state existed, using the migration's own list of legacy labels and the modules' own state code. Run it with
    /// WFL_REAL_SAVE pointing at a .rws file (a copy is not needed: the file is only read). Without the variable it does nothing, so the suite stays machine-independent.
    ///
    /// What this proves: the legacy labels a real save contains are the ones the migration reads, every value survives into the modules' payloads, migrating twice changes nothing, and
    /// saving again then reading gives the same state. What it cannot prove: that RimWorld's loader calls the reader at the right moment, or that anything plays correctly. That needs
    /// the game (see docs/MODULAR-ARCHITECTURE.md, "Verification status").
    /// </summary>
    public class RealSaveMigrationTests
    {
        private static IEnumerable<XElement> OfficeRecords(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                var reader = System.Xml.XmlReader.Create(stream, new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, IgnoreWhitespace = true });
                while (reader.Read())
                {
                    if (reader.NodeType == System.Xml.XmlNodeType.Element && reader.Name == "communityOffices")
                    {
                        var block = (XElement)XNode.ReadFrom(reader);
                        foreach (var li in block.Elements("li")) yield return li;
                        yield break;
                    }
                }
            }
        }

        private static int Int(XElement e, string name) => int.TryParse((string)e.Element(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

        private static LegacyOfficeValues Legacy(XElement office)
        {
            var v = new LegacyOfficeValues();
            foreach (var l in LegacyOfficeLabels.Ints) { int x = Int(office, l); if (x != 0) v.Ints[l] = x; }
            foreach (var l in LegacyOfficeLabels.Bools) if (string.Equals((string)office.Element(l), "True", StringComparison.OrdinalIgnoreCase)) v.Bools[l] = true;
            foreach (var l in LegacyOfficeLabels.Floats) if (float.TryParse((string)office.Element(l), NumberStyles.Float, CultureInfo.InvariantCulture, out float f) && f != 0f) v.Floats[l] = f;
            foreach (var l in LegacyOfficeLabels.IntLists) { var e = office.Element(l); var list = e == null ? null : e.Elements("li").Select(x => int.Parse((string)x, CultureInfo.InvariantCulture)).ToList(); if (list != null && list.Count > 0) v.IntLists[l] = list; }
            foreach (var l in LegacyOfficeLabels.StringLists) { var e = office.Element(l); var list = e == null ? null : e.Elements("li").Select(x => (string)x).ToList(); if (list != null && list.Count > 0) v.StringLists[l] = list; }
            return v;
        }

        private static OfficeKind KindOf(XElement office)
        {
            string k = (string)office.Element("kind");          // a post of the default kind (the healer) is not written
            return k != null && Enum.TryParse(k, out OfficeKind kind) ? kind : OfficeKind.Healer;
        }

        [Fact]
        public void A_real_pre_extraction_save_migrates_once_and_reads_back_the_same()
        {
            string path = Environment.GetEnvironmentVariable("WFL_REAL_SAVE");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;          // opt-in: nothing to check on a machine without a save

            var records = OfficeRecords(path).ToList();
            Assert.NotEmpty(records);
            int migrated = 0, healers = 0, teachers = 0;
            foreach (var li in records)
            {
                var office = new CommunityOffice { Id = Int(li, "id"), Kind = KindOf(li), HolderId = Int(li, "holder"), ElectedTick = Int(li, "elected") };
                var values = Legacy(li);
                bool had = values.Ints.Count + values.Bools.Count + values.Floats.Count + values.IntLists.Count + values.StringLists.Count > 0;
                Assert.Equal(had, LegacyOfficeMigration.Capture(office, values));
                Assert.False(LegacyOfficeMigration.Capture(office, values));          // loading twice never doubles anything
                if (!had) continue;
                migrated++;

                if (office.Kind == OfficeKind.Healer)
                {
                    healers++;
                    var care = office.Care();
                    Assert.Equal(Int(li, "nextRound"), care.NextRoundTick);
                    Assert.Equal(Int(li, "roundStart"), care.RoundStartTick);
                    Assert.Equal(li.Element("examined")?.Elements("li").Select(x => int.Parse((string)x)).ToList() ?? new List<int>(), care.Examined);
                    Assert.True(care.Started);          // an older save has already had its first day
                }
                if (office.Kind == OfficeKind.Teacher)
                {
                    teachers++;
                    var edu = office.Education();
                    Assert.Equal(Int(li, "lastTestingYear"), edu.LastTestingYear);
                    Assert.Equal(li.Element("lessonLog")?.Elements("li").Select(x => (string)x).ToList() ?? new List<string>(), edu.LessonLog);
                    Assert.Equal(Int(li, "testingStart"), office.Assessment().TestingStartTick);
                }

                // saved again, then read again: the same state, and the legacy payload is not read a second time
                office.FlushState();
                var again = new CommunityOffice { Id = office.Id, Kind = office.Kind, HolderId = office.HolderId, ElectedTick = office.ElectedTick };
                again.PayloadIds = new List<string>(office.PayloadIds); again.PayloadData = new List<string>(office.PayloadData);
                again.PayloadHolders = new List<int>(office.PayloadHolders); again.PayloadVersions = new List<int>(office.PayloadVersions);
                if (office.Kind == OfficeKind.Healer) Assert.Equal(office.Care().Encode(), again.Care().Encode());
                if (office.Kind == OfficeKind.Teacher) Assert.Equal(office.Education().Encode(), again.Education().Encode());
            }
            Assert.True(migrated > 0, "the save holds no legacy module state; pick a save written before module payloads");
            Console.WriteLine("real save: " + records.Count + " posts, " + migrated + " migrated (" + healers + " healer, " + teachers + " teacher)");
        }
    }
}
