using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.L10n;
using PTMod.Features.BankruptcyRecovery.Logic;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-014 (FR-10 item 2): the "Journal" tab shows the last 50 records of the journal, the newest first, each with the name of its operation in the language of the
    /// player (records of the switch-off, <c>disable.convert</c>, included). The names are string keys <c>rec.journal.op.&lt;code&gt;</c>; every code the feature writes has
    /// one in both languages, and a code the table does not know is still shown, with the code.
    /// </summary>
    [Covers(typeof(JournalLines), typeof(JournalRow))]
    public class JournalLinesTests
    {
        private static readonly IMenuText Text = new FakeMenuText();

        private static JournalEntryData Entry(string op, double amount, string date = "1976-03-01", string details = "")
        {
            return new JournalEntryData { date = date, op = op, amount = amount, details = details };
        }

        private static List<JournalEntryData> Numbered(int count)
        {
            var list = new List<JournalEntryData>();
            for (int i = 0; i < count; i++) list.Add(Entry("loan.interest", -(i + 1), "1976-03-01", "n" + i));
            return list;
        }

        // ---- which records, in which order ----

        [Test]
        public void Latest_ShowsTheLastFifty_NewestFirst()
        {
            List<JournalEntryData> entries = Numbered(60);

            IReadOnlyList<JournalRow> rows = JournalLines.Latest(Text, entries);

            Assert.Equal(50, JournalLines.Shown);
            Assert.Equal(50, rows.Count);
            Assert.Equal("n59", rows[0].Details, "the newest record is on top");
            Assert.Equal("n10", rows[49].Details, "the 51st from the end is the first that does not fit");
        }

        [Test]
        public void Latest_WithFewerRecords_ShowsAll_OfThemNewestFirst()
        {
            IReadOnlyList<JournalRow> rows = JournalLines.Latest(Text, Numbered(3));

            Assert.Equal(3, rows.Count);
            Assert.Equal("n2", rows[0].Details);
            Assert.Equal("n0", rows[2].Details);
        }

        [Test]
        public void Latest_OfNothing_IsEmpty_AndNullRecordsAreSkipped()
        {
            Assert.Equal(0, JournalLines.Latest(Text, null).Count);
            Assert.Equal(0, JournalLines.Latest(Text, new List<JournalEntryData>()).Count);
            Assert.Equal(0, JournalLines.Latest(Text, Numbered(5), 0).Count, "a limit below 1 shows nothing");

            var entries = Numbered(3);
            entries.Insert(1, null);
            Assert.Equal(3, JournalLines.Latest(Text, entries).Count, "the empty slot is not a row");
        }

        [Test]
        public void Latest_OfTheRealJournal_ShowsTheNewestOf200()
        {
            // the journal keeps a ring of 200; the tab shows the last 50 of it
            var rig = new RecoveryRig();
            for (int i = 0; i < 250; i++) rig.Journal.Record("line.draw", i + 1, "n" + i);

            IReadOnlyList<JournalRow> rows = JournalLines.Latest(Text, rig.Journal.Snapshot());

            Assert.Equal(RecoveryJournal.Capacity, rig.Journal.Count);
            Assert.Equal(50, rows.Count);
            Assert.Equal("n249", rows[0].Details);
            Assert.Equal("n200", rows[49].Details);
        }

        // ---- one row ----

        [Test]
        public void ARow_HasTheNameOfItsOperation_TheDate_TheSignedSum_AndTheNote()
        {
            IReadOnlyList<JournalRow> rows = JournalLines.Latest(Text, new[] { Entry("bridge.fee", -420000.0, "1976-03-01", "bridge-1") });

            JournalRow row = rows[0];
            Assert.Equal("rec.journal.op.bridge.fee", row.Operation);
            Assert.Equal("d:1976-03-01", row.Date);
            Assert.Equal("-$420000", row.AmountText);
            Assert.Near(-420000.0, row.Amount, 0);
            Assert.Equal("bridge-1", row.Details);
            Assert.Equal(MenuTone.Bad, row.Tone, "money that went out");
        }

        [Test]
        public void TheSign_AndTheTone_FollowTheDirectionOfTheMoney()
        {
            IReadOnlyList<JournalRow> rows = JournalLines.Latest(Text, new[]
            {
                Entry("line.open", 0.0, "1975-01-01", "until 1978-01-01"),
                Entry("bridge.open", 8400000.0),
                Entry("loan.interest", -100000.0),
            });

            Assert.Equal(MenuTone.Bad, rows[0].Tone);
            Assert.Equal("-$100000", rows[0].AmountText);
            Assert.Equal(MenuTone.Good, rows[1].Tone);
            Assert.Equal("+$8400000", rows[1].AmountText, "money that came in has a plus");
            Assert.Equal(MenuTone.Muted, rows[2].Tone);
            Assert.Equal("-", rows[2].AmountText, "an operation that moves no money has a dash");
        }

        [Test]
        public void AnUnknownCode_IsShownWithTheCode_NotHidden()
        {
            IReadOnlyList<JournalRow> rows = JournalLines.Latest(Text, new[] { Entry("future.thing", 5.0), Entry(null, 5.0), Entry("", 5.0) });

            Assert.Equal(3, rows.Count);
            Assert.Equal("rec.journal.op.unknown(future.thing)", rows[2].Operation, "the code goes in as the argument");
            Assert.Equal("rec.journal.op.unknown()", rows[1].Operation, "a missing code has an empty argument");
            Assert.Equal("rec.journal.op.unknown()", rows[0].Operation);
            Assert.Null(JournalLines.KeyOf("future.thing"));
            Assert.Null(JournalLines.KeyOf(null));
        }

        [Test]
        public void ABadDate_IsShownAsItWasStored_AndBadNumbersAreZero()
        {
            IReadOnlyList<JournalRow> rows = JournalLines.Latest(Text, new[]
            {
                Entry("loan.prepay", double.NaN, "not-a-date", null),
                Entry("loan.prepay", double.PositiveInfinity, null),
            });

            Assert.Equal("not-a-date", rows[1].Date);
            Assert.Equal(string.Empty, rows[1].Details);
            Assert.Near(0.0, rows[1].Amount, 0, "a number that is not finite is shown as 0");
            Assert.Equal("-", rows[1].AmountText);
            Assert.Equal(string.Empty, rows[0].Date);
            Assert.Near(0.0, rows[0].Amount, 0);
        }

        [Test]
        public void TheRecordsOfTheSwitchOff_AreInTheJournalToo()
        {
            IReadOnlyList<JournalRow> rows = JournalLines.Latest(Text, new[] { Entry("disable.convert", -9500000.0, "1980-06-01", "3 loans") });

            Assert.Equal("rec.journal.op.disable.convert", rows[0].Operation, "TSK-014: the record of the conversion into ordinary debt has a name");
            Assert.Equal("-$9500000", rows[0].AmountText);
        }

        // ---- the names ----

        private static L10nTables Tables(string language)
        {
            var problems = new List<string>();
            var tables = new L10nTables();
            tables.AddJson(language, "l10n/" + language + "/" + RecoveryTestData.FeatureId + ".json", RecoveryTestData.ReadStrings(language), problems);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            return tables;
        }

        [Test]
        public void EveryKnownOperation_HasANameInBothLanguages()
        {
            L10nTables ru = Tables("ru");
            L10nTables en = Tables("en");

            Assert.True(JournalLines.KnownOps.Count >= 19, "the codes of the feature and of the switch-off");
            foreach (string op in JournalLines.KnownOps)
            {
                string key = JournalLines.KeyOf(op);
                Assert.Equal("rec.journal.op." + op, key);
                Assert.True(ru.Contains("ru", key), "ru lacks " + key);
                Assert.True(en.Contains("en", key), "en lacks " + key);
            }
            Assert.True(ru.Contains("ru", JournalLines.UnknownKey) && en.Contains("en", JournalLines.UnknownKey), "the name of an unknown code");
        }

        [Test]
        public void EveryCodeTheFeatureWrites_IsInTheTable()
        {
            // The codes are string literals in the engines and in the asset service (the ledger's own generic file is left out). A new code without a name
            // would show up as "Operation <code>" in the tab: this finds it here instead.
            string root = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "BankruptcyRecovery");
            var patterns = new[]
            {
                new Regex(@"\bRecord\(\s*""([a-z]+\.[a-zA-Z]+)"""),
                new Regex(@"\.(?:CapitalIn|CapitalOut|Interest|InvestorPayment)\(\s*[^,;]+,\s*""([a-z]+\.[a-zA-Z]+)"""),
                new Regex(@"\bRevenue\([^;]*?""([a-z]+\.[a-zA-Z]+)"""),
            };

            var found = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string folder in new[] { "Logic", "Services" })
            {
                foreach (string path in SourceScan.Files(Path.Combine(root, folder), ".cs"))
                {
                    if (path.EndsWith("CapitalLedgerCore.cs", StringComparison.Ordinal)) continue;
                    string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                    foreach (Regex pattern in patterns)
                    {
                        foreach (Match m in pattern.Matches(code)) found.Add(m.Groups[1].Value);
                    }
                }
            }

            Assert.True(found.Count >= 18, "the scan found " + found.Count + " codes: " + string.Join(", ", found));
            foreach (string op in found) Assert.NotNull(JournalLines.KeyOf(op), "the code '" + op + "' is written by the feature but has no name for the tab");
        }
    }
}
