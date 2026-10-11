using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Json;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.RivalAcquisitions
{
    /// <summary>
    /// The debug actions of the manual scenarios (TSK-021) and the two spikes (TSK-003, TSK-004) need Unity and the game, so they are not part of the test build; these tests read their
    /// source and check what the Acceptance asks for: the whole code is the debug build's, it is shown only for the flag, each action leaves a line marked <c>debug</c>, the list of the
    /// actions is the one of design.md, the spikes stand apart from the executor, and every text comes from the tables. That the buttons work in the running game is checked by the
    /// owner (M1-M5, TSK-022; the spikes are the owner's tool for TSK-003 and TSK-004).
    /// </summary>
    public class DebugActionsSourceTests
    {
        private static readonly string[] DebugFiles =
        {
            Path.Combine("Debug", "AcquisitionsDebugActions.cs"),
            Path.Combine("Debug", "SpikeCpuTransfer.cs"),
            Path.Combine("Debug", "SpikeAssets.cs"),
            Path.Combine("Logic", "AcqDebugMath.cs"),
        };

        // A key of the tables is a literal handed to ModL10n.L or to Say (a ledger operation code such as "acq.debug.cash" has the same shape but is no key).
        private static readonly Regex KeyLiteral = new Regex("(?:ModL10n\\.L|Say)\\(\"(acq\\.[A-Za-z0-9_.]+)\"", RegexOptions.Compiled);

        // The key of a refusal is the last literal of a Refuse call: Refuse("a reason in English", "acq.debug.fail.limit").
        private static readonly Regex RefuseKeyLiteral = new Regex("Refuse\\([^;]*?\"(acq\\.[A-Za-z0-9_.]+)\"\\s*[,)]", RegexOptions.Compiled);

        // A literal that is the key of a failure note.
        private static readonly Regex FailureKeyLiteral = new Regex("\"acq\\.(?:debug|spike)\\.fail\\.[A-Za-z]+\"", RegexOptions.Compiled);

        private static string Folder()
        {
            Assert.NotNull(TestEnv.SrcDir, "source folder not found; run the tests through python mod/build.py --tests");
            return Path.Combine(TestEnv.SrcDir, "Features", "RivalAcquisitions");
        }

        private static string Raw(string relative)
        {
            string path = Path.Combine(Folder(), relative);
            Assert.True(File.Exists(path), "missing " + path);
            return File.ReadAllText(path);
        }

        private static string Code(string relative)
        {
            return SourceScan.Strip(Raw(relative), false);   // without comments
        }

        /// <summary>The text from <paramref name="start"/> to <paramref name="end"/> (the end of the code when the end is not found).</summary>
        private static string Between(string code, string start, string end)
        {
            int from = code.IndexOf(start, StringComparison.Ordinal);
            Assert.True(from >= 0, "not found: " + start);
            int to = code.IndexOf(end, from + start.Length, StringComparison.Ordinal);
            return to < 0 ? code.Substring(from) : code.Substring(from, to - from);
        }

        private static JsonObject Table(string language)
        {
            string path = Path.Combine(TestEnv.SrcDir, "L10n", language, "rival-acquisitions.json");
            Assert.True(File.Exists(path), "missing " + path);
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(File.ReadAllText(path), out node, out error), path + ": " + error);
            return (JsonObject)node;
        }

        // ---- the debug build only ----

        [Test]
        public void TheDebugCode_IsWhollyTheDebugBuilds()
        {
            foreach (string relative in DebugFiles)
            {
                string raw = Raw(relative).Trim();
                Assert.True(raw.StartsWith("#if PTMOD_DEBUG", StringComparison.Ordinal) && raw.EndsWith("#endif", StringComparison.Ordinal),
                    relative + ": the whole file is under PTMOD_DEBUG, so a release build has no trace of it");
                Assert.Equal(1, Regex.Matches(raw, @"^\s*#if\b", RegexOptions.Multiline).Count, relative + ": one #if, the one that opens the file");
            }
        }

        [Test]
        public void TheHubPage_CallsTheActions_OnlyInTheDebugBuild()
        {
            string raw = Raw(Path.Combine("UI", "AcquisitionsHubPage.cs"));

            Assert.True(Regex.IsMatch(raw, @"#if\s+PTMOD_DEBUG\s+AcquisitionsDebugActions\.Build\(page\);[^\r\n]*\s+#endif"), "the call is between #if PTMOD_DEBUG and #endif");
            Assert.Equal(1, Regex.Matches(Code(Path.Combine("UI", "AcquisitionsHubPage.cs")), "AcquisitionsDebugActions").Count, "and it is the only mention in the code of the page");
        }

        [Test]
        public void TheActions_AppearOnlyForTheFlag_AndNotInARelease()
        {
            string code = Code(Path.Combine("Debug", "AcquisitionsDebugActions.cs"));

            Assert.True(code.Contains("internal const string FlagActions = \"debug.acqActions\""), "the flag of the debug section");
            Assert.True(code.Contains("internal const string FlagSpikes = \"debug.acqSpikes\""), "the spikes have a flag of their own");
            Assert.True(code.Contains("!ModSettings.Debug(FlagActions)"), "the flag is checked through ModSettings.Debug (false in a release build)");
            Assert.True(code.IndexOf("ModSettings.Debug(FlagActions)", StringComparison.Ordinal) < code.IndexOf("ModUI.VerticalList(", StringComparison.Ordinal),
                "nothing is put on the page before the flag is checked");
            Assert.True(code.Contains("if (ModSettings.Debug(FlagSpikes))"), "the spikes are added only for their flag");
            Assert.True(Regex.IsMatch(code, @"if \(ModSettings\.Debug\(FlagSpikes\)\)\s*\{\s*SpikeCpuTransfer\.Build\(list\);\s*SpikeAssets\.Build\(list\);"), "both spikes, under that flag");
        }

        // ---- the list of design.md ----

        [Test]
        public void TheActions_AreTheOnesOfTheDesign()
        {
            string code = Code(Path.Combine("Debug", "AcquisitionsDebugActions.cs"));
            string giveCash = Between(code, "private static void GiveCash()", "private static void SetBalance()");
            string setBalance = Between(code, "private static void SetBalance()", "private static void SetDays()");
            string setDays = Between(code, "private static void SetDays()", "private static void Bankrupt()");
            string bankrupt = Between(code, "private static void Bankrupt()", "private static void EndAuction()");

            // "Give the player N million"
            Assert.True(giveCash.Contains("CapitalLedger.CapitalIn(Feature, amount"), "the money goes through the ledger the deals use");
            Assert.True(giveCash.Contains("AcqDebugMath.Dollars(_millions)"), "N million in dollars");
            Assert.True(giveCash.Contains("CapitalLedger.IsAvailable"), "without the ledger nothing is given and the owner is told");

            // "Target: balance = k x D"
            Assert.True(setBalance.Contains("ModCredit.DebtLimitOf(ai)"), "D is the debt limit of the core");
            Assert.True(setBalance.Contains("AcqDebugMath.BalanceFor(_ratio, limit)"), "k x D");
            Assert.True(setBalance.Contains("ai.MoneyAmount = (float)balance"), "the balance of the company");

            // "Target: DaysInDefault = N"
            Assert.True(setDays.Contains("ai.DaysInDefault = _days"), "the count of days");

            // "Target: bankrupt" - the vanilla way
            Assert.True(bankrupt.Contains("AcqDebugMath.BankruptBalance(limit)") && bankrupt.Contains("ai.MoneyAmount = (float)balance"), "the balance under the threshold");
            Assert.True(bankrupt.Contains("ai.DaysInDefault = AcqDebugMath.LastDayBeforeBankruptcy"), "and the last day of the term: the game declares the bankruptcy itself");
            Assert.False(bankrupt.Contains("TriggerBankruptcy"), "the vanilla path: the action does not call the bankruptcy of the game");
            Assert.False(bankrupt.Contains("OnCompanyWentBankrupt") || bankrupt.Contains("NotifyCompanyWentBankrupt"), "and does not raise the event by hand");

            foreach (string key in new[] { "acq.debug.cash.give", "acq.debug.ratio.set", "acq.debug.days.set", "acq.debug.bankrupt" })
            {
                Assert.True(code.Contains("\"" + key + "\""), "the button " + key);
            }
        }

        [Test]
        public void TheActionsOfAnAiCompany_RefuseABankruptAndTheYardstickD()
        {
            string code = Code(Path.Combine("Debug", "AcquisitionsDebugActions.cs"));
            string guard = Between(code, "private static void OnTarget(", "private static List<GameAiCompany> Rivals()");

            Assert.True(guard.Contains("ai == null || ai.IsBankrupt"), "a bankrupt company is no target of balance, days and bankruptcy");
            Assert.True(guard.Contains("ModHooks.Guard(Feature, \"debug/\" + name"), "under the guard of the feature");
            Assert.True(code.Contains("if (!(limit > 0.0))"), "a company without a debt limit has no D to count in");
        }

        [Test]
        public void EveryAction_LeavesALineMarkedDebug_AndTheSpikesToo()
        {
            string actions = Code(Path.Combine("Debug", "AcquisitionsDebugActions.cs"));

            Assert.True(actions.Contains("ModLog.Info(Feature, \"debug: \" + text)"), "Journal writes to the log of the feature, marked debug:");
            foreach (string method in new[] { "GiveCash", "SetBalance", "SetDays", "Bankrupt", "EndAuction", "ResetCooldown" })
            {
                string body = BodyOf(actions, "private static void " + method + "()");
                Assert.True(body.Contains("Journal("), method + ": the action writes its line");
                Assert.True(body.Contains("Say("), method + ": and shows a note");
            }

            foreach (string relative in new[] { Path.Combine("Debug", "SpikeCpuTransfer.cs"), Path.Combine("Debug", "SpikeAssets.cs") })
            {
                string code = Code(relative);
                Assert.True(code.Contains("AcquisitionsDebugActions.Journal(\"spike 00"), relative + ": the results go to the log marked debug");
                Assert.True(Regex.IsMatch(code, "ModHooks\\.Guard\\(Feature, \"debug/spike00[34]/"), relative + ": every action runs under the guard");
            }
        }

        [Test]
        public void EveryRefusal_LeavesALineMarkedDebug_Too()
        {
            // A click that does nothing is traced as a click that does something: the note of a refusal sends the owner to Player.log, so the line must be there.
            string actions = Code(Path.Combine("Debug", "AcquisitionsDebugActions.cs"));
            string refuse = BodyOf(actions, "internal static void Refuse(");
            Assert.True(refuse.Contains("Journal(\"refused: \" + reason)") && refuse.Contains("Say(key, args)"), "Refuse writes the debug line first, then shows the note");

            foreach (string relative in new[] { Path.Combine("Debug", "AcquisitionsDebugActions.cs"), Path.Combine("Debug", "SpikeCpuTransfer.cs"), Path.Combine("Debug", "SpikeAssets.cs") })
            {
                int refusals = 0;
                foreach (string statement in Code(relative).Split(';'))
                {
                    if (!FailureKeyLiteral.IsMatch(statement)) continue;
                    Assert.True(statement.Contains("Refuse("), relative + ": a failure note is shown only through Refuse, so that it leaves its line: " + statement.Trim());
                    refusals++;
                }
                Assert.True(refusals >= 3, relative + ": the scan found the refusals of the file: " + refusals);
            }

            foreach (string relative in new[] { Path.Combine("Debug", "SpikeCpuTransfer.cs"), Path.Combine("Debug", "SpikeAssets.cs") })
            {
                string helper = BodyOf(Code(relative), "private static void Refuse(string reason, string key)");
                Assert.True(helper.Contains("Log(\"refused: \" + reason)") && helper.Contains("Say(key)"), relative + ": the helper writes the spike's line first, then shows the note");
            }
        }

        /// <summary>The text of a method from its header to the closing brace at the same indent (found by counting braces).</summary>
        private static string BodyOf(string code, string header)
        {
            int start = code.IndexOf(header, StringComparison.Ordinal);
            Assert.True(start >= 0, "not found: " + header);
            int open = code.IndexOf('{', start);
            int depth = 0;
            for (int i = open; i < code.Length; i++)
            {
                if (code[i] == '{') depth++;
                else if (code[i] == '}' && --depth == 0) return code.Substring(start, i - start + 1);
            }
            return code.Substring(start);
        }

        [Test]
        public void TheExtraActions_TouchOnlyTheBooksTheyNameAndNeedTheirTarget()
        {
            string code = Code(Path.Combine("Debug", "AcquisitionsDebugActions.cs"));
            string auction = BodyOf(code, "private static void EndAuction()");
            string cooldown = BodyOf(code, "private static void ResetCooldown()");

            Assert.True(auction.Contains("feature.State.OpenAuctionOf(ai.UniqueID)") && auction.Contains("auction.targetSaveId != ai.SaveID"), "the open auction of this very company");
            Assert.True(auction.Contains("auction.until = yesterday") && auction.Contains("AcqDebugMath.YesterdayText(ModTicks.Today)"), "its last day becomes yesterday");
            Assert.False(auction.Contains("closed = true"), "the record is left to the monthly pass, as with a real expiry");
            Assert.True(cooldown.Contains("feature.State.lastHostileOn = \"\""), "the cooldown is the date of the last hostile takeover");
        }

        // ---- the spikes ----

        [Test]
        public void TheSpikes_StandApartFromTheExecutor_AndDoNotChangeTheBooks()
        {
            foreach (string relative in new[] { Path.Combine("Debug", "SpikeCpuTransfer.cs"), Path.Combine("Debug", "SpikeAssets.cs") })
            {
                string code = Code(relative);
                Assert.False(code.Contains("AcquisitionExecutor"), relative + ": the spike checks the game with plain calls, not our executor");
                Assert.False(code.Contains("AcquisitionPlanBuilder") || code.Contains("DealExecution"), relative + ": nor the plan");
                Assert.False(code.Contains("State.AddDeal") || code.Contains(".State."), relative + ": the books of the feature are not touched");
                Assert.False(code.Contains("CapitalLedger"), relative + ": no money moves");
            }
        }

        [Test]
        public void TheSpikeTsk003_DoesSteps5And8_AndCountsTheEvents()
        {
            string code = Code(Path.Combine("Debug", "SpikeCpuTransfer.cs"));

            foreach (string call in new[]
            {
                "giver.GetBaseCpus()", "giver.CpusOrderedByProfit()", "cpus.Remove(cpu)",                              // step 5: out of the three lists
                "cpu.Company = player", "GameAccess.ResetProductionCaches(cpu)", "owner.AddCpu(cpu)", "window.AddCpu(cpu)", // step 8
                "data.OnNewCpu", "ModEvents.RaiseCpuTransferred(Feature, target, player, cpu)",
                "ModEvents.CpuTransferred += onTransferred", "ModEvents.CpuReleased += onReleased",                      // the counters of the acceptance
                "ModEvents.CpuTransferred -= onTransferred", "ModEvents.CpuReleased -= onReleased",
                "contract.Initialize(provider, player)", "cpu.MarkupPrice",                                              // the contract of a target that is a client
            })
            {
                Assert.True(code.Contains(call), "the spike does: " + call);
            }

            Assert.False(code.Contains(".Company.AddCpu(") || code.Contains("target.AddCpu"), "Company.AddCpu would announce a release: the design avoids it");
            Assert.True(code.IndexOf("cpus.Remove(cpu)", StringComparison.Ordinal) < code.IndexOf("cpu.Company = player", StringComparison.Ordinal), "step 5 comes before step 8");
            Assert.True(code.Contains("LastFamily.Add(") && code.Contains("moved.BaseSaveId"), "the family is remembered by SaveID to be checked after a load");
        }

        [Test]
        public void TheSpikeTsk004_CoversTheFiveLinesOfTheAcceptance()
        {
            string code = Code(Path.Combine("Debug", "SpikeAssets.cs"));

            foreach (string call in new[]
            {
                "GameAccess.SetSuppressPopup(mine, true)", "research.Progress = 1f", "mine.Update(true)",              // technologies without windows
                "player.ExpandFactory(1, 0f, wafer.ID)", "player.ExpandFactory(1, 0f, int.MaxValue)",                  // a known and an unknown wafer size
                "contract.Initialize(player, contract.Client)", "cpu.SetNewManufacturer(player)",                      // the supply contract
                "player.Divisions.FoundryServices = on", "window.SetFoundryServicesToggle(on)",                        // the flag, with and without
                "GameAccess.NotifyCompanyWentBankrupt(target)",                                                        // the signal for Intel
            })
            {
                Assert.True(code.Contains(call), "the spike does: " + call);
            }

            Assert.True(code.Contains("if (quiet) GameAccess.SetSuppressPopup(mine, false)"), "the windows are switched on again, whatever happens");
            Assert.True(code.Contains("player.Factory.ProductionCapacity = capacity0"), "the capacity the game raises before it fails is put back");
            Assert.False(code.Contains("TriggerBankruptcy"), "the signal is the event only: the target does not go bankrupt");
        }

        // ---- the texts ----

        [Test]
        public void EveryKeyOfTheDebugCode_IsDefinedInBothLanguages()
        {
            var keys = new HashSet<string>();
            foreach (string relative in DebugFiles)
            {
                foreach (Match match in KeyLiteral.Matches(Code(relative))) keys.Add(match.Groups[1].Value);
                foreach (Match match in RefuseKeyLiteral.Matches(Code(relative))) keys.Add(match.Groups[1].Value);
            }
            Assert.True(keys.Count >= 50, "the scan found the keys of the debug code: " + keys.Count);

            foreach (string language in new[] { "ru", "en" })
            {
                JsonObject table = Table(language);
                var missing = new List<string>();
                foreach (string key in keys)
                {
                    if (table.GetString(key) == null) missing.Add(key);
                }
                Assert.True(missing.Count == 0, language + ": keys of the debug code without a text: " + string.Join(", ", missing));
            }
        }

        [Test]
        public void TheShownTexts_AreKeysOfTheTables_NotSentencesOfTheCode()
        {
            // What the player reads (a widget of ModUI, a note on the screen) comes from ModL10n: every literal in such a statement is a key. The log is English and free.
            var found = new List<string>();
            foreach (string relative in DebugFiles)
            {
                foreach (string statement in Code(relative).Split(';'))
                {
                    if (!statement.Contains("ModUI.") && !statement.Contains("Say(")) continue;
                    foreach (Match match in Regex.Matches(statement, "\"((?:[^\"\\\\]|\\\\.)*)\""))
                    {
                        string literal = match.Groups[1].Value;
                        bool isKey = Regex.IsMatch(literal, @"^acq\.[A-Za-z0-9_.]+$");
                        bool readsLikeText = literal.Contains(" ") || Regex.IsMatch(literal, "[\\u0400-\\u04FF]");   // a number or an on/off word handed to a note is no text
                        if (!isKey && readsLikeText) found.Add(relative + ": \"" + literal + "\"");
                    }
                }
            }
            Assert.True(found.Count == 0, "texts of the debug widgets that are not keys: " + string.Join("; ", found));
        }

        [Test]
        public void TheExistingApiProbe_IsKept()
        {
            string probe = Raw(Path.Combine("Debug", "ApiEventProbe.cs")).Trim();
            Assert.True(probe.StartsWith("#if PTMOD_DEBUG", StringComparison.Ordinal) && probe.EndsWith("#endif", StringComparison.Ordinal));
            Assert.True(probe.Contains("internal static void Attach()") && probe.Contains("debug.acqApiProbeThrow"), "TSK-017's test subscriber is as it was");
            Assert.True(Code("RivalAcquisitionsFeature.cs").Contains("ApiEventProbe.Attach();"), "and the feature still attaches it");
        }
    }
}
