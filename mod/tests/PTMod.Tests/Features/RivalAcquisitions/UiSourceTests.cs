using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Json;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.RivalAcquisitions
{
    /// <summary>
    /// The windows of the feature (TSK-015…TSK-019) need Unity and the game, so they are not part of the test build; these tests read their source and check what the
    /// Acceptance asks for: the modal window, the tooltips of the closed deals, <c>ModUI.Confirm</c> and no dialog of our own, the gate before the executor, the hub page,
    /// the actions of the mails, the patch P4 - and the localization: no sentence in the code, every key of the code in both tables, no key of the tables left unused.
    /// That the windows look and work as intended in the running game is checked by the owner (M1-M9 of design.md, TSK-022).
    /// </summary>
    public class UiSourceTests
    {
        private static string Folder()
        {
            Assert.NotNull(TestEnv.SrcDir, "source folder not found; run the tests through python mod/build.py --tests");
            return Path.Combine(TestEnv.SrcDir, "Features", "RivalAcquisitions");
        }

        private static string Read(params string[] parts)
        {
            string path = Path.Combine(Folder(), Path.Combine(parts));
            Assert.True(File.Exists(path), "missing " + path);
            return SourceScan.Strip(File.ReadAllText(path), false);   // the code without its comments
        }

        private static List<string> UiFiles()
        {
            return SourceScan.Files(Path.Combine(Folder(), "UI"), ".cs");
        }

        private static void HasAll(string code, string what, Dictionary<string, string> required)
        {
            foreach (KeyValuePair<string, string> pair in required)
            {
                Assert.True(code.Contains(pair.Value), what + ": " + pair.Key + " (missing: " + pair.Value + ")");
            }
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

        private static readonly Regex KeyLiteral = new Regex("\"(acq\\.[A-Za-z0-9_.]+)\"", RegexOptions.Compiled);

        // ---- the windows ----

        [Test]
        public void TheWindowDeals_IsModal_WithThreeTabs_AndReadsTheGameOnce()
        {
            string code = Read("UI", "AcquisitionsWindow.cs");

            HasAll(code, "the window", new Dictionary<string, string>
            {
                { "a window of ModUI", "ModUI.CreateWindow(WindowId, WindowTemplate.Modal" },
                { "the tabs", "\"acq.tab.companies\", \"acq.tab.stakes\", \"acq.tab.history\"" },
                { "the title", "\"acq.window.title\"" },
                { "the snapshots of all companies", "GameSnapshotAdapter.All(player, state, balance.Valuation.CpuIncomeMonths)" },
                { "the rows by the pure table", "DealsTable.Build(" },
                { "the stakes by the pure model", "StakesModel.Build(" },
                { "the journal by the pure model", "HistoryModel.Rows(" },
                { "the time of opening goes to the log", "ModLog.Debug(Feature, \"ui: the window opened in \"" },
                { "a company can be focused", "internal static void Open(int focusUniqueId = -1" },
                { "the action of a mail opens the stakes", "internal static void OpenRescue(int uniqueId)" },
                { "under the guard", "ModHooks.Guard(Feature, \"ui/window\"" },
                { "the tab is built under the guard", "ModHooks.Guard(Feature, \"ui/tab/\"" },
                { "nothing without the feature", "ModFeatures.IsActive(Feature)" },
            });
            Assert.False(code.Contains("WindowTemplate.Panel"), "the window holds the game: it is not a Panel");
        }

        [Test]
        public void TheTabCompanies_ShowsTheTableTheButtonsAndTheReasonsInTooltips()
        {
            string code = Read("UI", "CompaniesTab.cs");

            HasAll(code, "the tab", new Dictionary<string, string>
            {
                { "the pure rows", "CompanyRow" },
                { "the buttons of the deals", "foreach (DealOffer offer in row.Offers) AddOffer(" },
                { "a closed deal is inactive", "button.Interactable = open" },
                { "the reason in the tooltip of a closed deal", "AcqUi.Tip(button, ModL10n.L(\"acq.tip.closed\"), ReasonText.Of(f, offer.ReasonKey, offer.ReasonArg))" },
                { "the price on the button", "UiKeys.DealHostile" },
                { "the status text", "ModL10n.L(row.StatusKey" },
                { "the valuation", "row.Valuation.E" },
                { "the share of the player", "row.OwnedShare" },
                { "the preview opens from the button", "DealPreviewWindow.Open(offer.Type" },
                { "the dialog of a stake opens from the button", "StakeDialog.Open(" },
                { "a deal that runs locks the buttons", "AcquisitionExecutor.IsBusy" },
                { "the player in default", "AcqKeys.ReasonPlayerDefault" },
                { "all closed", "\"acq.details.allClosed\"" },
            });
        }

        [Test]
        public void TheTooltips_GoThroughModUiTooltip()
        {
            string ui = Read("UI", "AcqUi.cs");

            Assert.True(ui.Contains("ModUI.Tooltip(widget.gameObject"), "the tooltip of a widget");
            Assert.True(ui.Contains("ModUI.Tooltip(target, line.TipHeader"), "the tooltip of a line of the preview");
            Assert.False(ui.Contains("TooltipHandler"), "no bridge of our own to the tooltips of the game");
        }

        [Test]
        public void TheTabStakes_HasTheOrderControls_AndTheJournalTabTheMarkPartial()
        {
            string stakes = Read("UI", "StakesTab.cs");
            string history = Read("UI", "HistoryTab.cs");
            string controls = Read("UI", "RescueOrderControls.cs");

            HasAll(stakes, "the stakes", new Dictionary<string, string>
            {
                { "the rescue controls", "RescueOrderControls.Build(" },
                { "buy more", "StakeDialog.Open(row.UniqueId, row.SaveId)" },
                { "the last dividend", "row.LastDividend" },
                { "the dividends in total", "row.DividendsPaid" },
            });
            HasAll(history, "the history", new Dictionary<string, string>
            {
                { "the mark", "\"acq.history.partial\"" },
                { "the notes of a partial deal", "\"acq.history.notes\"" },
                { "the newest first by the pure model", "data.History" },
            });
            HasAll(controls, "the rescue order", new Dictionary<string, string>
            {
                { "the switch", "ModUI.Toggle(" },
                { "the limit slider", "ModUI.Slider(" },
                { "the order is kept by the service", "feature.Stakes.SetRescueOrder(" },
                { "the scale of the slider", "RescueScale.For(" },
            });
        }

        [Test]
        public void ThePreview_AsksModUiConfirm_AndHasNoDialogOfItsOwn()
        {
            string preview = Read("UI", "DealPreviewWindow.cs");
            string dialog = Read("UI", "StakeDialog.cs");

            HasAll(preview, "the preview", new Dictionary<string, string>
            {
                { "the question", "ModUI.Confirm(model.ConfirmTitle, model.ConfirmMessage, OnConfirmed" },
                { "the pure model", "DealPreviewModel.Build(" },
                { "the plan is built anew", "AcquisitionPlanBuilder.Build(quote, target, player, gate.Options())" },
                { "the pure gate", "new PreviewGate()" },
                { "the button waits for the gate", "gate.CanBuy(plan, AcquisitionExecutor.IsBusy)" },
                { "the tick of the consequences", "ModUI.Toggle(body, ModL10n.L(\"acq.consequence.ack\")" },
                { "the choice of the lines", "gate.SetKeepLines(keep, plan.KeepLinesRequired)" },
                { "the sell button is locked where it must be", "sell.Interactable = canChoose && !model.KeepLinesRequired" },
                { "the price and the status the player saw", "AcquisitionExecutor.Execute(type, uniqueId, saveId, gate.Options(), quote.Price, target.Status)" },
                { "the reason of a refusal", "ReasonText.Of(AcqUi.Format, key, arg)" },
            });
            Assert.True(preview.IndexOf("gate.TryBegin(", StringComparison.Ordinal) < preview.IndexOf("AcquisitionExecutor.Execute(", StringComparison.Ordinal),
                "the gate is passed before the executor is called (no second press while a deal runs)");
            Assert.True(preview.Contains("gate.End("), "the gate is released when the deal ends");

            HasAll(dialog, "the stake dialog", new Dictionary<string, string>
            {
                { "the question", "ModUI.Confirm(ModL10n.L(\"acq.stake.confirm.title\"), message" },
                { "the purchase", "feature.Stakes.Buy(ai, share)" },
                { "the pure model", "StakeDialogModel.Build(" },
                { "the dividend of last month", "StakeService.LastMonthProfit(ai)" },
                { "the dropdown of the shares", "ModUI.Dropdown(" },
                { "the rescue order", "RescueOrderControls.Build(" },
            });

            foreach (string path in UiFiles())
            {
                string code = SourceScan.Strip(File.ReadAllText(path), false);
                Assert.False(Regex.IsMatch(code, @"CreateWindow\([^;]*(confirm|Confirm)"), path + ": no confirmation window of our own");
                Assert.False(Regex.IsMatch(code, @"\bnew\s+ConfirmDialog\b"), path + ": ModUI.Confirm is the only question");
            }
        }

        [Test]
        public void TheHubPage_OpensTheWindow_AfterClosingTheHub()
        {
            string feature = Read("RivalAcquisitionsFeature.cs");
            string page = Read("UI", "AcquisitionsHubPage.cs");

            Assert.True(feature.Contains("ctx.HubPage(\"acq.hub.title\", AcquisitionsHubPage.Build)"), "FR-2 item 1: the page of the feature in the hub");
            Assert.True(page.IndexOf("ModHub.Close()", StringComparison.Ordinal) < page.IndexOf("AcquisitionsWindow.Open()", StringComparison.Ordinal),
                "the hub is modal: it is closed first");
        }

        [Test]
        public void TheApiAndTheMailActions_OpenTheWindowOnTheCompany()
        {
            string service = Read("RivalAcquisitionsService.cs");
            string mail = Read("Services", "AcquisitionMail.cs");

            Assert.True(service.Contains("AcquisitionsWindow.Open(focusUniqueId)"), "RivalAcquisitionsApi.OpenWindow reaches the window");
            Assert.True(mail.Contains("AcquisitionsWindow.OpenRescue(uniqueId)"), "the action 'Rescue order' opens the stakes");
            Assert.True(mail.Contains("MailTags.ActionRescue"), "the actions are told apart");
            Assert.True(mail.Contains("RivalAcquisitionsApi.OpenWindow(uniqueId)"), "the action 'Deals' opens the window on the company");
            Assert.False(mail.Contains("MailId"), "the id of a mail is never kept");
        }

        [Test]
        public void TheApiProbe_IsDebugOnly_AndSubscribesToEveryEvent()
        {
            string path = Path.Combine(Folder(), "Debug", "ApiEventProbe.cs");
            Assert.True(File.Exists(path), "TSK-017: the test subscriber in the debug build");
            string raw = File.ReadAllText(path).Trim();
            Assert.True(raw.StartsWith("#if PTMOD_DEBUG", StringComparison.Ordinal) && raw.EndsWith("#endif", StringComparison.Ordinal), "the whole file is the debug build's");

            string code = SourceScan.Strip(raw, false);
            foreach (string name in new[] { "DealCompleted", "StakeChanged", "RivalDefaulted", "RivalBankrupt" })
            {
                Assert.True(Regex.Matches(code, @"RivalAcquisitionsApi\." + name + @"\s*\+=").Count == 2, name + ": a subscriber that fails and one that logs");
            }
            Assert.True(code.Contains("ModSettings.Debug("), "only the flags of the debug section switch it on");
            Assert.True(code.IndexOf("Throw(\"DealCompleted\")", StringComparison.Ordinal) < code.IndexOf("Log(\"DealCompleted #\"", StringComparison.Ordinal),
                "the failing subscriber comes first, so the logging one shows the others still run");

            string feature = Read("RivalAcquisitionsFeature.cs");
            Assert.True(feature.Contains("#if PTMOD_DEBUG") && feature.Contains("ApiEventProbe.Attach();"), "the feature attaches the probe in the debug build only");
        }

        // ---- P4 ----

        [Test]
        public void P4_OnlyEditsTheCellOfTheGamesTable_AndIsOptional()
        {
            string code = Read("Patches", "AcquisitionPatches.cs");
            string patch = code.Substring(code.IndexOf("ShowCompaniesPostfix", StringComparison.Ordinal));
            patch = patch.Substring(0, patch.IndexOf("internal static bool CheckTargets()", StringComparison.Ordinal));

            Assert.True(Regex.IsMatch(code, @"\[ModPatch\(typeof\(GameCompaniesWindow\),\s*""ShowCompanies"",\s*PatchKind\.Postfix,\s*ArgTypes\s*=\s*new Type\[0\],\s*Optional\s*=\s*true\)\]"),
                "P4: an optional postfix of CompaniesSpreadsheetWindow.ShowCompanies");
            HasAll(patch, "P4", new Dictionary<string, string>
            {
                { "the feature is active", "ModFeatures.IsActive(Feature)" },
                { "under the guard", "ModHooks.Guard(Feature, SiteP4" },
                { "the window's own list and table", "GameSpreadsheet ___spreadsheet, List<ICompany> ___companies" },
                { "only a company the player took over", "RivalAcquisitionsApi.IsAbsorbedByPlayer(ai.UniqueID)" },
                { "only a bankrupt one", "!ai.IsBankrupt" },
                { "the cell of the column Bankrupt", "table.EditRow(i, BankruptColumn, text)" },
                { "the text from the table", "ModL10n.L(\"acq.table.absorbed\")" },
            });
            Assert.True(code.Contains("BankruptColumn = 8"), "the ninth column of the game's table");
            Assert.False(patch.Contains("__result"), "the result of the game's method is not touched");
            Assert.False(Regex.IsMatch(patch, @"return\s+false\s*;"), "a postfix cannot skip the game's method, and does not try");
        }

        // ---- localization (TSK-018) ----

        [Test]
        public void EveryKeyOfTheWindowCode_IsDefinedInBothLanguages()
        {
            var files = new List<string>(UiFiles());
            files.Add(Path.Combine(Folder(), "Patches", "AcquisitionPatches.cs"));
            files.Add(Path.Combine(Folder(), "Services", "AcquisitionMail.cs"));
            files.Add(Path.Combine(Folder(), "RivalAcquisitionsFeature.cs"));

            var keys = new HashSet<string>();
            foreach (string path in files)
            {
                string code = SourceScan.Strip(File.ReadAllText(path), false);
                foreach (Match match in KeyLiteral.Matches(code)) keys.Add(match.Groups[1].Value);
            }
            Assert.True(keys.Count >= 70, "the scan found the keys of the windows: " + keys.Count);

            foreach (string language in new[] { "ru", "en" })
            {
                JsonObject table = Table(language);
                var missing = new List<string>();
                foreach (string key in keys)
                {
                    if (table.GetString(key) == null) missing.Add(key);
                }
                Assert.True(missing.Count == 0, language + ": keys of the code without a text: " + string.Join(", ", missing));
            }
        }

        [Test]
        public void TheWindowCode_HasNoSentencesOfItsOwn()
        {
            // TSK-018: all texts come from ModL10n. A string literal that reads like a sentence (words with spaces, or any Cyrillic) is a hard-coded text;
            // log lines, exception messages, names of objects and keys are not.
            var found = new List<string>();
            foreach (string path in UiFiles())
            {
                string code = SourceScan.Strip(File.ReadAllText(path), false);
                string[] lines = code.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line.Contains("ModLog.") || line.Contains("Exception(") || line.Contains("Assert.")) continue;
                    foreach (Match match in Regex.Matches(line, "\"((?:[^\"\\\\]|\\\\.)*)\""))
                    {
                        string literal = match.Groups[1].Value;
                        if (Regex.IsMatch(literal, "[Ѐ-ӿ]") || Regex.IsMatch(literal, @"[A-Za-z]{3,}\s+[A-Za-z]{2,}"))
                        {
                            found.Add(Path.GetFileName(path) + ":" + (i + 1) + ": \"" + literal + "\"");
                        }
                    }
                }
            }
            Assert.True(found.Count == 0, "hard-coded text in the windows:\n  " + string.Join("\n  ", found));
        }

        [Test]
        public void EveryKeyOfTheTables_IsUsedBySomeCode()
        {
            var used = new HashSet<string>();
            foreach (string path in SourceScan.Files(Folder(), ".cs"))
            {
                string code = SourceScan.Strip(File.ReadAllText(path), false);
                foreach (Match match in KeyLiteral.Matches(code)) used.Add(match.Groups[1].Value);
            }

            JsonObject table = Table("en");
            var unused = new List<string>();
            foreach (KeyValuePair<string, JsonNode> pair in table)
            {
                if (!used.Contains(pair.Key)) unused.Add(pair.Key);
            }
            Assert.True(unused.Count == 0, "keys of the tables that no code uses: " + string.Join(", ", unused));
        }

        [Test]
        public void EveryKey_OfTheTables_CarriesThePrefix()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                foreach (KeyValuePair<string, JsonNode> pair in Table(language))
                {
                    Assert.True(pair.Key.StartsWith("acq.", StringComparison.Ordinal), language + ": " + pair.Key + " lacks the prefix of the feature");
                }
            }
        }

        [Test]
        public void TheConsequenceTexts_NameTheRealCompanies_InBothLanguages()
        {
            // TSK-018: the warnings of FR-12 speak of Intel, AMD, GlobalFoundries and Cyrix by their real names, in Russian as in English.
            foreach (string language in new[] { "ru", "en" })
            {
                JsonObject table = Table(language);
                Assert.True(table.GetString("acq.consequence.x86").Contains("Cyrix"), language + ": the x86 warning names Cyrix");
                Assert.True(table.GetString("acq.consequence.x86").Contains("x86"), language + ": the x86 warning names the license");
                Assert.True(table.GetString("acq.consequence.globalFoundries").Contains("GlobalFoundries"), language + ": the spin-off warning names GlobalFoundries");
                Assert.True(table.GetString("acq.consequence.intelFoundry").Contains("Intel"), language + ": the foundry warning names Intel");
                Assert.True(table.GetString("acq.consequence.gfAmdContract").Contains("GlobalFoundries"), language + ": the contract note names GlobalFoundries");
            }
        }

        [Test]
        public void TheCompanyNames_AreTheGamesOwn_NotTextsOfTheTables()
        {
            // TSK-018: the names of companies in lists come from the game (ICompany.Name); the tables hold no company name as a key's text of a list.
            string windows = Read("UI", "CompaniesTab.cs");

            Assert.True(windows.Contains("AcqUi.Plain(row.Name)"), "the name of a row is the company's own");
            foreach (string company in new[] { "Intel", "AMD", "GlobalFoundries", "Cyrix" })
            {
                Assert.False(Regex.IsMatch(windows, "\"[^\"]*" + company + "[^\"]*\""), "no company name is spelled out in the code of the table: " + company);
            }
        }
    }
}
