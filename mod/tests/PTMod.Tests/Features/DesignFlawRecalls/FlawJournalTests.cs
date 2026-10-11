using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Features.DesignFlawRecalls;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// TSK-017 (FR-12, AC-12.1): the page "Flaws and recalls". The rows of the two tabs (<see cref="FlawJournalModel"/>): a hidden record is never there, "My CPUs" are the flaws of the
    /// player that are known, "Rivals" are the public incidents of the AI companies (what a rival keeps quiet is not shown), the button "Fund a new stepping" belongs to a patched
    /// flaw of the player with the saved price and term (AC-12.1: "Patch, -12 %, 200 days" and "20 million, 90 days"), the order and the cap of the finished records. The page itself
    /// (<c>UI/FlawJournalPage.cs</c>) is checked on its source: the registration with <c>HubPage</c>, the tabs, the confirmation before the revision is paid, the debug block that exists
    /// only in a debug build and only with <c>debug</c> on, and that every text it uses is in both string tables.
    /// </summary>
    [Covers(typeof(FlawJournalModel), typeof(FlawJournalRow), typeof(FlawJournalTab))]
    public class FlawJournalTests
    {
        private static readonly int Today = FlawCycleKit.MonthStart(30);

        private static FlawRecord Patched(int id, string company = "player", int daysLeft = 200, float loss = 0.12f)
        {
            FlawRecord record = FlawCycleKit.Record(FlawState.Mitigated, id, 100 + id, company, FlawSeverity.Major, FlawResponse.Patch);
            record.Data.mitigationEndDay = Today + daysLeft;
            record.Data.ipsLoss = loss;
            record.Data.publicSinceDay = Today - 100;
            record.Data.spentTotal = 7250000f;
            record.Data.quote = new FlawQuote { SteppingCost = 20000000f, SteppingDays = 90, SteppingDailyCost = 20000000f / 90f }.ToDto();
            return record;
        }

        private static List<FlawJournalRow> Rows(FlawJournalTab tab, params FlawRecord[] records)
        {
            return FlawJournalModel.Rows(records, tab, Today);
        }

        // ---- which records, which tab (FR-12 items 1 and 4) ----

        [Test]
        public void AHiddenRecord_IsNeverShown_OnAnyTab()
        {
            FlawRecord mine = FlawCycleKit.Record(FlawState.Hidden, 1, 100, "player");
            FlawRecord rival = FlawCycleKit.Record(FlawState.Hidden, 2, 101, "ai:3");
            rival.Data.publicSinceDay = Today - 5;   // even a corrupt record that says it was public

            Assert.Equal(0, Rows(FlawJournalTab.Mine, mine, rival).Count, "My CPUs");
            Assert.Equal(0, Rows(FlawJournalTab.Rivals, mine, rival).Count, "Rivals");
            Assert.False(FlawJournalModel.IsShown(mine, FlawJournalTab.Mine));
            Assert.False(FlawJournalModel.IsShown(null, FlawJournalTab.Mine), "no record, no line");
        }

        [Test]
        public void MyCpus_ShowsTheKnownFlawsOfThePlayer_FinishedOnesToo()
        {
            FlawRecord waiting = FlawCycleKit.Record(FlawState.InternalKnown, 1, 100, "player");
            FlawRecord stepping = FlawCycleKit.Record(FlawState.Stepping, 2, 101, "player");
            FlawRecord done = FlawCycleKit.Record(FlawState.Resolved, 3, 102, "player", response: FlawResponse.Recall);
            FlawRecord rival = FlawCycleKit.Record(FlawState.Public, 4, 103, "ai:3");

            List<FlawJournalRow> rows = Rows(FlawJournalTab.Mine, waiting, stepping, done, rival);

            Assert.Equal(new[] { 2, 1, 3 }, rows.Select(r => r.RecordId).ToArray(), "the open ones first, newest first; then the finished ones");
            Assert.True(rows.All(r => r.CompanyKey == "player"), "no rival on this tab");
        }

        [Test]
        public void Rivals_ShowsOnlyWhatIsPublicKnowledge()
        {
            FlawRecord publicNow = FlawCycleKit.Record(FlawState.Public, 1, 100, "ai:3");
            publicNow.Data.publicSinceDay = Today - 10;
            FlawRecord patchedAfterwards = Patched(2, "ai:4");
            FlawRecord secretInternal = FlawCycleKit.Record(FlawState.InternalKnown, 3, 102, "ai:3");
            FlawRecord secretRevision = FlawCycleKit.Record(FlawState.Stepping, 4, 103, "ai:5");
            secretRevision.Data.quietStepping = true;
            FlawRecord recalledInternal = FlawCycleKit.Record(FlawState.Resolved, 5, 104, "ai:6", response: FlawResponse.Recall);   // a recall is never done in secret
            FlawRecord quietlyResolved = FlawCycleKit.Record(FlawState.Resolved, 6, 105, "ai:6", response: FlawResponse.Stepping);
            quietlyResolved.Data.quietStepping = true;
            FlawRecord mine = FlawCycleKit.Record(FlawState.Public, 7, 106, "player");
            mine.Data.publicSinceDay = Today - 1;

            List<FlawJournalRow> rows = Rows(FlawJournalTab.Rivals, publicNow, patchedAfterwards, secretInternal, secretRevision, recalledInternal, quietlyResolved, mine);

            Assert.Equal(new[] { 2, 1, 5 }, rows.Select(r => r.RecordId).ToArray(),
                "a public flaw, a patch after it became public, a recall; not a secret flaw, a quiet revision or a quietly finished one (no rumours, FR-11 item 5), not the player's");
        }

        [Test]
        public void IsPublicKnowledge_IsAboutPublicSinceDay_AndTheRecall()
        {
            Assert.False(FlawJournalModel.IsPublicKnowledge(null));
            Assert.False(FlawJournalModel.IsPublicKnowledge(FlawCycleKit.Record(FlawState.InternalKnown, 1)));
            Assert.True(FlawJournalModel.IsPublicKnowledge(Patched(2)));
            Assert.True(FlawJournalModel.IsPublicKnowledge(FlawCycleKit.Record(FlawState.Resolved, 3, response: FlawResponse.Recall)));
            Assert.False(FlawJournalModel.IsPublicKnowledge(FlawCycleKit.Record(FlawState.Resolved, 4, response: FlawResponse.Patch)));
        }

        // ---- the order and the cap ----

        [Test]
        public void TheFinishedRecords_AreCapped_AndTheOpenOnesAreAlwaysShown()
        {
            var records = new List<FlawRecord>();
            for (int id = 1; id <= 30; id++) records.Add(FlawCycleKit.Record(FlawState.Resolved, id, 100 + id, "player", response: FlawResponse.Recall));
            records.Add(FlawCycleKit.Record(FlawState.Public, 31, 200, "player"));
            records.Add(FlawCycleKit.Record(FlawState.Mitigated, 5000, 201, "player", response: FlawResponse.Patch));
            records[records.Count - 1].Data.mitigationEndDay = Today + 10;

            List<FlawJournalRow> rows = FlawJournalModel.Rows(records, FlawJournalTab.Mine, Today);

            Assert.Equal(2 + FlawJournalModel.MaxFinalRows, rows.Count, "two open records and the newest finished ones");
            Assert.Equal(new[] { 5000, 31 }, rows.Take(2).Select(r => r.RecordId).ToArray(), "the open ones come first, newest first");
            Assert.Equal(30, rows[2].RecordId, "then the newest finished one");
            Assert.False(rows.Any(r => r.RecordId == 1), "the oldest finished record fell off");
            Assert.Equal(20, FlawJournalModel.MaxFinalRows);
        }

        [Test]
        public void Rows_OfNothing_AreEmpty()
        {
            Assert.Equal(0, FlawJournalModel.Rows(null, FlawJournalTab.Mine, Today).Count);
            Assert.Equal(0, FlawJournalModel.Rows(new FlawRecord[0], FlawJournalTab.Rivals, Today).Count);
            Assert.Throws<ArgumentNullException>(() => FlawJournalModel.RowOf(null, Today));
            Assert.Throws<ArgumentNullException>(() => FlawJournalModel.StatusKey(null));
        }

        // ---- AC-12.1 ----

        [Test]
        public void AC12_1_APatchWith200DaysLeft_ShowsThePatchAndTheButtonToFundAStepping()
        {
            FlawJournalRow row = Rows(FlawJournalTab.Mine, Patched(1, daysLeft: 200, loss: 0.12f)).Single();

            Assert.Equal(FlawState.Mitigated, row.State);
            Assert.Equal(200, row.DaysLeft, "200 days left of the patch");
            Assert.Near(0.12, row.IpsLoss, 1e-6, "-12 % of the performance");
            Assert.Equal("mitigated", FlawJournalModel.StatusKey(row), "the status text is 'Patch, -12 %, 200 days' (the page fills the numbers in)");
            Assert.True(row.CanFundStepping, "the button 'Fund a new stepping'");
            Assert.Equal(20000000f, row.FundCost, "20 million");
            Assert.Equal(90, row.FundDays, "90 days");
        }

        [Test]
        public void TheButtonToFundAStepping_IsOnlyForAPatchedFlawOfThePlayerThatHasAQuote()
        {
            Assert.False(Rows(FlawJournalTab.Rivals, Patched(1, "ai:3")).Single().CanFundStepping, "the rival's patch has no button");

            FlawRecord noQuote = Patched(2);
            noQuote.Data.quote = null;
            Assert.False(Rows(FlawJournalTab.Mine, noQuote).Single().CanFundStepping, "no quote, no price to show");

            foreach (FlawState state in new[] { FlawState.InternalKnown, FlawState.Public, FlawState.Stepping, FlawState.Resolved, FlawState.Expired, FlawState.Closed })
            {
                FlawRecord record = Patched(3);
                record.State = state;
                record.Data.publicSinceDay = Today - 1;
                FlawJournalRow row = FlawJournalModel.RowOf(record, Today);
                Assert.False(row.CanFundStepping, "no button for a flaw in " + state);
                Assert.Equal(0f, row.FundCost);
            }
        }

        [Test]
        public void Row_DaysLeft_IsTheTermOfTheRevisionOrThePatch_NeverNegative()
        {
            FlawRecord stepping = FlawCycleKit.Record(FlawState.Stepping, 1, 100, "player", response: FlawResponse.Stepping);
            stepping.Data.steppingEndDay = Today + 40;
            Assert.Equal(40, FlawJournalModel.RowOf(stepping, Today).DaysLeft);

            stepping.Data.steppingEndDay = Today - 3;
            Assert.Equal(0, FlawJournalModel.RowOf(stepping, Today).DaysLeft, "past its term by the calendar");

            Assert.Equal(0, FlawJournalModel.RowOf(FlawCycleKit.Record(FlawState.Public, 2), Today).DaysLeft, "no term runs");
        }

        [Test]
        public void Row_CarriesTheColumnsOfTheSpec()
        {
            FlawRecord record = Patched(9);
            record.Data.coverUp = true;
            record.Data.typeId = "thermal";

            FlawJournalRow row = FlawJournalModel.RowOf(record, Today);

            Assert.Equal(9, row.RecordId);
            Assert.Equal(109, row.BaseCpuId, "CPU");
            Assert.Equal("player", row.CompanyKey, "company");
            Assert.Equal("thermal", row.TypeId, "type");
            Assert.Equal(FlawSeverity.Major, row.Severity, "severity");
            Assert.Equal(FlawResponse.Patch, row.Response, "response");
            Assert.Equal(7250000f, row.SpentTotal, "spent");
            Assert.True(row.CoverUp);
            Assert.False(row.IsFinal);
            Assert.False(row.Waiting, "the owner has answered");
            Assert.True(FlawJournalModel.RowOf(FlawCycleKit.Record(FlawState.Public, 10), Today).Waiting, "a public flaw without an answer waits for one");
        }

        // ---- the words ----

        [Test]
        public void StatusKey_NamesWhatTheOwnerIsDoing()
        {
            Func<FlawState, FlawResponse, bool, string> key = (state, response, quiet) =>
            {
                FlawRecord record = FlawCycleKit.Record(state, 1, 100, "player", response: response);
                record.Data.quietStepping = quiet;
                return FlawJournalModel.StatusKey(FlawJournalModel.RowOf(record, Today));
            };

            Assert.Equal("waitingInternal", key(FlawState.InternalKnown, FlawResponse.None, false));
            Assert.Equal("waitingPublic", key(FlawState.Public, FlawResponse.None, false));
            Assert.Equal("ignoredInternal", key(FlawState.InternalKnown, FlawResponse.Ignore, false));
            Assert.Equal("ignoredPublic", key(FlawState.Public, FlawResponse.Ignore, false));
            Assert.Equal("stepping", key(FlawState.Stepping, FlawResponse.Stepping, false));
            Assert.Equal("steppingQuiet", key(FlawState.Stepping, FlawResponse.Stepping, true));
            Assert.Equal("mitigated", key(FlawState.Mitigated, FlawResponse.Patch, false));
            Assert.Equal("resolvedRecall", key(FlawState.Resolved, FlawResponse.Recall, false));
            Assert.Equal("resolvedStepping", key(FlawState.Resolved, FlawResponse.Stepping, false));
            Assert.Equal("resolvedPatch", key(FlawState.Resolved, FlawResponse.Patch, false));
            Assert.Equal("resolved", key(FlawState.Resolved, FlawResponse.None, false));
            Assert.Equal("expired", key(FlawState.Expired, FlawResponse.Ignore, false));
            Assert.Equal("closed", key(FlawState.Closed, FlawResponse.None, false));
        }

        [Test]
        public void EveryStatus_HasATextInBothLanguages_WithTheSamePlaceholders()
        {
            Dictionary<string, string> ru = FlawApiKit.Own("ru");
            Dictionary<string, string> en = FlawApiKit.Own("en");

            foreach (string status in FlawJournalModel.AllStatusKeys)
            {
                string key = "dfr.journal.status." + status;
                Assert.True(ru.ContainsKey(key) && en.ContainsKey(key), "both languages have " + key);
                Assert.Equal(Placeholders(ru[key]), Placeholders(en[key]), key + ": the same {n} in both languages");
                Assert.True(Placeholders(ru[key]).All(n => n <= 1), key + ": only {0} (the loss of the patch) and {1} (the days left) exist");
            }

            Assert.Equal(new[] { 0, 1 }, Placeholders(ru["dfr.journal.status.mitigated"]).ToArray(), "AC-12.1: 'Patch, -{0} %, {1} days'");
            Assert.True(ru["dfr.journal.status.mitigated"].Contains("Заплатка"), "the Russian text of AC-12.1 starts with the word 'Patch'");
        }

        [Test]
        public void EveryKeyInTheStatusTable_IsReachable()
        {
            // The table must not keep a text no status can return (and every status the model can return is in AllStatusKeys).
            var reachable = new HashSet<string>(FlawJournalModel.AllStatusKeys);
            foreach (FlawState state in Enum.GetValues(typeof(FlawState)).Cast<FlawState>())
            {
                foreach (FlawResponse response in Enum.GetValues(typeof(FlawResponse)).Cast<FlawResponse>())
                {
                    foreach (bool quiet in new[] { false, true })
                    {
                        FlawRecord record = FlawCycleKit.Record(state, 1, 100, "player", response: response);
                        record.Data.quietStepping = quiet;
                        string key = FlawJournalModel.StatusKey(FlawJournalModel.RowOf(record, Today));
                        Assert.True(reachable.Contains(key), state + "/" + response + " gives '" + key + "', which is not in AllStatusKeys");
                    }
                }
            }

            foreach (string key in FlawApiKit.Own("ru").Keys.Where(k => k.StartsWith("dfr.journal.status.", StringComparison.Ordinal)))
            {
                Assert.True(reachable.Contains(key.Substring("dfr.journal.status.".Length)), key + " is not returned by StatusKey");
            }
        }

        private static List<int> Placeholders(string text)
        {
            return Regex.Matches(text, @"\{(\d+)\}").Cast<Match>().Select(m => int.Parse(m.Groups[1].Value)).Distinct().OrderBy(n => n).ToList();
        }

        // ---- the page (checked on its source) ----

        [Test]
        public void ThePage_IsRegisteredWithHubPage_UnderTheTitleOfTheDesign()
        {
            Assert.True(FlawApiKit.Code("DesignFlawRecallsFeature.cs").Contains("ctx.HubPage(FlawJournalPage.TitleKey, FlawJournalPage.Build);"), "registered in Register");
            Assert.True(FlawApiKit.Code("UI/FlawJournalPage.cs").Contains("internal const string TitleKey = \"dfr.journal.title\";"), "dfr.journal.title");
            Assert.Equal("Брак и отзывы", FlawApiKit.Own("ru")["dfr.journal.title"]);
            Assert.Equal("Flaws and recalls", FlawApiKit.Own("en")["dfr.journal.title"]);
        }

        [Test]
        public void ThePage_HasTheTwoTabs_AScrollingList_AndRowsMadeWithInfo()
        {
            string page = FlawApiKit.Code("UI/FlawJournalPage.cs");

            Assert.True(page.Contains("AddTab(FlawJournalTab.Mine, \"dfr.journal.tab.mine\");") && page.Contains("AddTab(FlawJournalTab.Rivals, \"dfr.journal.tab.rivals\");"), "My CPUs and Rivals");
            Assert.True(page.Contains("ModUI.Button(_tabs, caption"), "the tabs are ModUI buttons");
            Assert.True(page.Contains("ModUI.ScrollView(page, BodyHeight)"), "the rows are in a scroll view");
            Assert.True(page.Contains("ModUI.Info(body, title, status)"), "a row is a ModUI.Info");
            Assert.True(page.Contains("FlawJournalModel.Rows("), "the rows come from the pure model: a hidden record is not there");
            Assert.Equal("Мои CPU", FlawApiKit.Own("ru")["dfr.journal.tab.mine"]);
            Assert.Equal("Соперники", FlawApiKit.Own("ru")["dfr.journal.tab.rivals"]);
            Assert.False(page.Contains("FlawState.Hidden"), "the page never asks about a hidden record: the model drops it");
        }

        [Test]
        public void TheButtonToFundAStepping_AsksForAConfirmation_BeforeTheRevisionIsPaid()
        {
            string page = FlawApiKit.Code("UI/FlawJournalPage.cs");

            Assert.Equal(1, FlawApiKit.Occurrences(page, "FundSteppingForMitigated("), "the revision is started in one place");
            Assert.Equal(1, Regex.Matches(page, @"(?<![A-Za-z])Fund\(recordId\)").Count, "and that place is reached from one place");
            Assert.True(Regex.IsMatch(page, @"ModUI\.Confirm\([^;]*\(\) => Fund\(recordId\)"), "only the 'Yes' of ModUI.Confirm starts it");

            string button = FlawApiKit.Between(page, "if (row.CanFundStepping)", "private static string Title(");
            Assert.True(button.Contains("() => AskToFund(recordId)"), "the button asks first");
            Assert.False(button.Contains("FundSteppingForMitigated"), "the button does not pay by itself");

            string fund = FlawApiKit.Between(page, "private static void Fund(int recordId)", "#if PTMOD_DEBUG");
            Assert.True(fund.Contains("feature.Applier.FundSteppingForMitigated(record)"), "the call of the response engine of FR-8 item 5");
        }

        [Test]
        public void TheDebugButton_IsOnlyInADebugBuild_AndOnlyWithTheSettingOn()
        {
            string page = FlawApiKit.Code("UI/FlawJournalPage.cs");

            int ifDebug = page.IndexOf("#if PTMOD_DEBUG", StringComparison.Ordinal);
            int endIf = page.LastIndexOf("#endif", StringComparison.Ordinal);
            Assert.True(ifDebug > 0 && endIf > ifDebug, "a debug-only section");
            Assert.Equal(2, FlawApiKit.Occurrences(page, "#if PTMOD_DEBUG"), "the call and the block");

            string debug = page.Substring(page.IndexOf("#if PTMOD_DEBUG", ifDebug + 5, StringComparison.Ordinal));
            Assert.True(debug.Contains("BuildDebugBlock(RectTransform page)") && debug.Contains("if (!FlawSettings.Debug) return;"), "and only with debug on");
            Assert.True(debug.Contains("ModUI.Dropdown(") && debug.Contains("ModUI.Button(") && debug.Contains("DesignFlawRecallsApi.Registry.ForceFlaw("), "a drop-down list, a button, ForceFlaw");

            string before = page.Substring(0, page.IndexOf("#if PTMOD_DEBUG", StringComparison.Ordinal));
            Assert.False(before.Contains("ForceFlaw"), "nothing that forces a flaw outside the debug sections");
        }

        [Test]
        public void ThePage_UsesOnlyTextsThatBothTablesHave()
        {
            Dictionary<string, string> ru = FlawApiKit.Own("ru");
            Dictionary<string, string> en = FlawApiKit.Own("en");

            foreach (string file in new[] { "UI/FlawJournalPage.cs", "Patches/CpuBadgePatch.cs", "Patches/DesignerRiskHint.cs" })
            {
                foreach (string key in FlawApiKit.KeyLiterals(FlawApiKit.Code(file)))
                {
                    Assert.True(ru.ContainsKey(key), file + " uses " + key + ": missing in ru");
                    Assert.True(en.ContainsKey(key), file + " uses " + key + ": missing in en");
                }
            }
        }

        [Test]
        public void TheStringTablesOfTheFeature_HaveTheSameKeysInBothLanguages_AndEveryKeyOfTheCodeExists()
        {
            Dictionary<string, string> ru = FlawApiKit.Own("ru");
            Dictionary<string, string> en = FlawApiKit.Own("en");

            Assert.Equal(ru.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(), en.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(), "the same keys");
            foreach (string key in ru.Keys) Assert.Equal(Placeholders(ru[key]), Placeholders(en[key]), key + ": the same {n} in both languages");

            foreach (KeyValuePair<string, string> file in FlawApiKit.AllCode())
            {
                foreach (string key in FlawApiKit.KeyLiterals(file.Value))
                {
                    if (key.StartsWith("dfr.decide.", StringComparison.Ordinal) || key == "dfr.news.ai") continue;   // ids of events and of the journal source, not texts
                    Assert.True(ru.ContainsKey(key), file.Key + " uses the key " + key + " that the tables do not have");
                }
            }
        }
    }
}
