using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Json;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-013 to TSK-016 (FR-2, FR-8, NFR-4): the seams of the windows, the letters and the strings that cannot be loaded here (they talk to the game), read from the
    /// sources. The windows are made by <c>ModUI</c> from the game's templates, modal, with no dialog or tooltip of their own; every operation of the menu asks the core's
    /// <c>ModUI.Confirm</c> first; the letters are critical with a reusable action and keep no id; the page in the hub closes the hub before the menu opens; no text is typed into the code;
    /// the string tables of ru and en have the same keys and the same placeholders. The logic behind these seams has its own tests.
    /// </summary>
    public class RecoveryUiSeamsTests
    {
        private static string FeatureDir()
        {
            string dir = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "BankruptcyRecovery");
            Assert.True(Directory.Exists(dir), "the folder of the feature is missing: " + dir);
            return dir;
        }

        private static string Code(params string[] relative)
        {
            string path = Path.Combine(FeatureDir(), Path.Combine(relative));
            Assert.True(File.Exists(path), "the file is missing: " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        private static List<KeyValuePair<string, string>> AllCode(bool stripStrings)
        {
            var found = new List<KeyValuePair<string, string>>();
            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
            {
                found.Add(new KeyValuePair<string, string>(SourceScan.Relative(FeatureDir(), path).Replace('\\', '/'), SourceScan.Strip(SourceScan.ReadText(path), stripStrings)));
            }
            Assert.True(found.Count > 20, "too few sources found: " + found.Count);
            return found;
        }

        // The text of the window files of the menu (the three parts of one class).
        private static string MenuCode()
        {
            return Code("UI", "RecoveryWindow.cs") + "\n" + Code("UI", "RecoveryWindow.Loans.cs") + "\n" + Code("UI", "RecoveryWindow.Journal.cs");
        }

        // ---- the windows (TSK-013, TSK-015) ----

        [Test]
        public void TheMenuAndTheOffer_AreModalWindowsMadeByModUI()
        {
            string menu = Code("UI", "RecoveryWindow.cs");
            Assert.True(Regex.IsMatch(menu, @"ModUI\.CreateWindow\(WindowId,\s*WindowTemplate\.Modal,\s*""rec\.menu\.title"""), "the menu: a Modal window (the template's PauseTrigger stops the time)");

            string offer = Code("UI", "BailoutOfferWindow.cs");
            Assert.True(Regex.IsMatch(offer, @"ModUI\.CreateWindow\(WindowId,\s*WindowTemplate\.Modal,\s*""rec\.bailout\.title"""), "the offer: a Modal window");
            Assert.True(Regex.IsMatch(offer, @"class\s+BailoutOfferWindow\s*:\s*IBailoutPresenter"), "the window is the presenter of the engine");
        }

        [Test]
        public void TheFeature_HasNoDialogOrTooltipBridgeOfItsOwn()
        {
            var windows = new Regex(@"\bCreateWindow\s*\(");
            var ownBridges = new Regex(@"\b(?:TooltipHandler|TooltipData|StartTrigger|CancelTrigger|ConfirmationWindow|FablessConfirmationWindow)\b");
            var offenders = new List<string>();
            int made = 0;
            foreach (KeyValuePair<string, string> file in AllCode(true))
            {
                foreach (Match m in windows.Matches(file.Value))
                {
                    made++;
                    if (file.Key != "UI/RecoveryWindow.cs" && file.Key != "UI/BailoutOfferWindow.cs") offenders.Add(file.Key + ":" + SourceScan.LineOf(file.Value, m.Index) + ": a window");
                }
                foreach (Match m in ownBridges.Matches(file.Value)) offenders.Add(file.Key + ":" + SourceScan.LineOf(file.Value, m.Index) + ": " + m.Value);
            }
            Assert.Equal(2, made, "the menu and the offer are the only windows of the feature");
            Assert.True(offenders.Count == 0, "confirmations and tooltips are the core's (ModUI.Confirm, ModUI.Tooltip):\n  " + string.Join("\n  ", offenders));
        }

        [Test]
        public void TheMenu_HasTheFourTabs_AndEachIsBuilt()
        {
            string menu = MenuCode();
            foreach (string key in new[] { "rec.tab.summary", "rec.tab.assets", "rec.tab.loans", "rec.tab.journal" })
                Assert.True(menu.Contains("\"" + key + "\""), "the tab " + key);
            foreach (string build in new[] { "BuildSummary(feature)", "BuildAssets(feature)", "BuildLoans(feature)", "BuildJournal(feature)" })
                Assert.True(menu.Contains(build), "the tab is built: " + build);

            // RecoveryApi.OpenMenu reaches the window
            Assert.True(Code("RecoveryService.cs").Contains("RecoveryWindow.Open(tab)"), "OpenMenu opens the window on the tab");
            Assert.True(Code("Patches", "WarningWindowButton.cs").Contains("RecoveryApi.OpenMenu(RecoveryTab.Summary)"), "the button of the warning window opens the summary");
        }

        [Test]
        public void TheBuildOfATab_IsTimedInTheDebugLog()
        {
            // TSK-013: "the window is built in 50 ms or less (the debug log)"
            string menu = Code("UI", "RecoveryWindow.cs");
            Assert.True(menu.Contains("Stopwatch.StartNew()"), "the build is timed");
            Assert.True(Regex.IsMatch(menu, @"ModLog\.Debug\(Feature,\s*""menu: the tab """), "and the time goes to the debug log");
        }

        [Test]
        public void EveryOperation_AsksTheCoreFirst_AndOnlyThenRuns()
        {
            string menu = MenuCode();
            var operations = new[]
            {
                new Regex(@"\.Assets\.SellLines\("), new Regex(@"\.Assets\.SellStock\("), new Regex(@"\.Assets\.Retire\("),
                new Regex(@"\.Loans\.Prepay\("), new Regex(@"\.CreditLine\.Draw\("), new Regex(@"\.CreditLine\.Repay\("), new Regex(@"\.BridgeLoans\.Take\("),
            };

            Assert.True(Regex.IsMatch(menu, @"ModUI\.Confirm\(plan\.Title,\s*plan\.Message,\s*onYes\)"), "Ask hands the text of the plan to the core's question");
            Assert.True(Regex.Matches(menu, @"\bAsk\(plan,").Count >= 7, "seven operations ask");

            foreach (Regex operation in operations)
            {
                MatchCollection calls = operation.Matches(menu);
                Assert.True(calls.Count >= 1, "the operation is not in the window: " + operation);
                foreach (Match call in calls)
                {
                    int from = Math.Max(0, call.Index - 700);
                    string before = menu.Substring(from, call.Index - from);
                    Assert.True(before.Contains("Ask(plan,"), "the operation " + operation + " runs outside the confirmation, line " + SourceScan.LineOf(menu, call.Index));
                }
            }
        }

        [Test]
        public void TheQuestion_ShowsTheBalanceAfter_ThroughThePlanner()
        {
            string menu = MenuCode();
            Assert.True(menu.Contains("new ConfirmPlanner(GameMenuText.Instance, feature.Finance.Money, feature.Finance.PlayerDebtLimit)"), "the planner gets the balance and the threshold");
            foreach (string plan in new[] { ".SellLines(", ".SellStock(", ".Retire(", ".Prepay(", ".Draw(", ".RepayLine(", ".Bridge(" })
                Assert.True(menu.Contains("Planner(feature)" + plan), "the plan of " + plan);
        }

        [Test]
        public void ACloseOption_IsInactiveWithItsReason_AsATooltip()
        {
            string ui = Code("UI", "RecoveryUi.cs");
            Assert.True(Regex.IsMatch(ui, @"button\.Interactable\s*=\s*false;"), "an option that is not available is inactive");
            Assert.True(ui.Contains("ModUI.Tooltip(button.gameObject, string.Empty, disabledReason)"), "the reason is the tooltip of the core");
            Assert.True(ui.Contains("showReason"), "and a muted line under the button, where the layout allows");
        }

        // ---- the offer (TSK-015) ----

        [Test]
        public void TheOfferWindow_CrossIsNotARefusal_DeclineAsksOnce_AndAcceptCallsTheService()
        {
            string offer = Code("UI", "BailoutOfferWindow.cs");

            int closed = offer.IndexOf("private void OnWindowClosed()", StringComparison.Ordinal);
            int closedEnd = offer.IndexOf("\n        }", closed, StringComparison.Ordinal);
            string closedBody = offer.Substring(closed, closedEnd - closed);
            Assert.True(closedBody.Contains("_service.Dismissed()"), "the cross reports Dismissed");
            Assert.False(closedBody.Contains("Decline"), "the cross is not a refusal");
            Assert.True(closedBody.Contains("if (_decided) return;"), "a decision already made is not reported again");

            Assert.Equal(1, Regex.Matches(offer, @"_service\.Decline\(\)").Count, "Decline has one caller");
            int confirmed = offer.IndexOf("private void ConfirmedDecline()", StringComparison.Ordinal);
            Assert.True(confirmed > 0 && offer.IndexOf("_service.Decline()", StringComparison.Ordinal) > confirmed, "only the answer Yes of the core's question declines");
            Assert.True(Regex.IsMatch(offer, @"ModUI\.Confirm\(""rec\.bailout\.decline\.title"",\s*""rec\.bailout\.decline\.confirm"",\s*ConfirmedDecline"), "the question before the bankruptcy");

            Assert.True(offer.Contains("_service.Accept(kind)"), "accepting is the service's");
            Assert.True(offer.Contains("BailoutKind.RescueLoan") && offer.Contains("BailoutKind.InvestorShare"), "both ways");
            Assert.True(Regex.IsMatch(offer, @"OnAcceptInvestor,\s*texts\.InvestorUnavailable"), "the investor way is inactive with the reason while a contract runs");
        }

        [Test]
        public void TheFeature_GivesTheOfferWindowToTheService_AndWiresTheLettersAndThePage()
        {
            string code = Code("BankruptcyRecoveryFeature.cs");
            int register = code.IndexOf("public void Register(FeatureContext ctx)", StringComparison.Ordinal);
            string body = code.Substring(register);

            Assert.True(body.Contains("_bailout.Presenter = _offerWindow;"), "without the presenter the offer is never made");
            Assert.True(body.Contains("ctx.OnTick(TickPhase.LateTick, _monitor.OnDay)"), "the warning letter is checked once a day");
            Assert.True(body.Contains("ModMail.RegisterActionHandler(FeatureId, RecoveryMonitor.OnMailAction)"), "the action of the letters");
            Assert.True(body.Contains("ctx.HubPage(RecoveryHubPage.TitleKey, RecoveryHubPage.Build)"), "the page in the hub");
            Assert.False(body.Contains("TickOptions."), "the letters do not run in the fast-forward or under the loading screen (FR-1 item 3)");
        }

        // ---- the letters (TSK-015) ----

        [Test]
        public void TheLetters_AreCritical_WithAReusableActionAndTheTagsOfTheFeature()
        {
            string mail = Code("Services", "RecoveryMonitor.cs");

            Assert.True(mail.Contains("\"rec:prewarning\"") && mail.Contains("\"rec:default\""), "the tags");
            Assert.True(mail.Contains("MailPriority.Critical"), "Critical");
            Assert.True(Regex.IsMatch(mail, @"Id\s*=\s*ActionOpenMenu,\s*LabelKey\s*=\s*""rec\.mail\.action\.openMenu"",\s*Repeatable\s*=\s*true"), "a reusable action \"Recovery options\"");
            Assert.True(mail.Contains("\"rec.mail.prewarning.subject\"") && mail.Contains("\"rec.mail.default.subject\""));
            Assert.True(mail.Contains("RecoveryApi.OpenMenu(RecoveryTab.Summary)"), "the action opens the menu on the summary");
        }

        [Test]
        public void TheIdOfALetter_IsKeptNowhere_TheLetterIsFoundByItsTag()
        {
            string mail = Code("Services", "RecoveryMonitor.cs");

            Assert.True(mail.Contains("ModMail.Find(RecoveryApi.FeatureId, tag)"), "ModMail.Find looks the letter up by the tag");
            Assert.False(Regex.IsMatch(mail, @"=\s*ModMail\.Send\("), "the result of Send is not stored");
            foreach (KeyValuePair<string, string> file in AllCode(true))
                Assert.False(Regex.IsMatch(file.Value, @"\b(?:int|long)\s+_?\w*[Mm]ail\w*Id\b"), file.Key + " keeps the id of a letter");
        }

        // ---- the page in the hub (TSK-015) ----

        [Test]
        public void TheHubPage_ClosesTheHub_ThenOpensTheMenu()
        {
            string page = Code("UI", "RecoveryHubPage.cs");

            Assert.True(page.Contains("TitleKey = \"rec.hub.title\""), "the heading of the page");
            int close = page.IndexOf("ModHub.Close();", StringComparison.Ordinal);
            int open = page.IndexOf("RecoveryApi.OpenMenu(RecoveryTab.Summary);", StringComparison.Ordinal);
            Assert.True(close > 0 && open > close, "the hub is a modal window: it is closed before the menu opens");
            Assert.True(page.Contains("ModL10n.L(\"rec.menu.open\")"), "the button \"Recovery options\"");
        }

        // ---- no typed text (TSK-016) ----

        [Test]
        public void NoRussianTextIsTypedIntoTheCode()
        {
            var cyrillic = new Regex("[Ѐ-ӿ]");
            var offenders = new List<string>();
            foreach (KeyValuePair<string, string> file in AllCode(false))
            {
                foreach (Match m in cyrillic.Matches(file.Value)) offenders.Add(file.Key + ":" + SourceScan.LineOf(file.Value, m.Index));
            }
            Assert.True(offenders.Count == 0, "texts live in L10n/<language>/bankruptcy-recovery.json:\n  " + string.Join("\n  ", offenders));
        }

        [Test]
        public void TheWindows_TakeTheirWordsFromTheStringTables()
        {
            // A literal with a space and letters is a sentence. In the windows, the letters and the button of the warning window the only sentences allowed are
            // messages for the log (ModLog) and for exceptions; every text the player reads is a key of the tables ("rec.*") or comes from one.
            var literal = new Regex("\"((?:[^\"\\\\]|\\\\.)*)\"");
            var letters = new Regex("[A-Za-z]{2}");
            var offenders = new List<string>();
            int checkedLiterals = 0;
            foreach (KeyValuePair<string, string> file in AllCode(false))
            {
                bool player = file.Key.StartsWith("UI/", StringComparison.Ordinal) || file.Key == "Services/RecoveryMonitor.cs" || file.Key == "Patches/WarningWindowButton.cs";
                if (!player) continue;

                string[] lines = file.Value.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line.Contains("ModLog.") || line.Contains("throw new") || line.Contains("const string")) continue;
                    foreach (Match m in literal.Matches(line))
                    {
                        string text = m.Groups[1].Value;
                        checkedLiterals++;
                        if (text.IndexOf(' ') >= 0 && letters.IsMatch(text)) offenders.Add(file.Key + ":" + (i + 1) + ": \"" + text + "\"");
                    }
                }
            }
            Assert.True(checkedLiterals > 50, "the scan found only " + checkedLiterals + " literals: is it broken?");
            Assert.True(offenders.Count == 0, "a sentence typed into the code of a window:\n  " + string.Join("\n  ", offenders));
        }

        [Test]
        public void TheCallsThatShowText_GetKeysOrProducedText_NeverALiteralSentence()
        {
            var shows = new Regex(@"\b(?:ModUI\.(?:Text|Button|Info|Toggle|Slider|Dropdown|Confirm|Tooltip)|ModNotify\.(?:Show|ShowKey)|\.SetTitle|RecoveryUi\.(?:Heading|Line|Button))\(");
            var literal = new Regex("\"((?:[^\"\\\\]|\\\\.)*)\"");
            var offenders = new List<string>();
            int calls = 0;
            foreach (KeyValuePair<string, string> file in AllCode(false))
            {
                string[] lines = file.Value.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!shows.IsMatch(lines[i])) continue;
                    calls++;
                    foreach (Match m in literal.Matches(lines[i]))
                    {
                        string text = m.Groups[1].Value;
                        if (text.Length == 0) continue;
                        if (text.StartsWith("rec.", StringComparison.Ordinal) || text.StartsWith("core.", StringComparison.Ordinal)) continue;
                        if (text.IndexOf(' ') < 0 && text.IndexOf('<') < 0 && !Regex.IsMatch(text, "[A-Za-z]{3}.*[A-Za-z]{3}")) continue;   // "-", "\n", numbers
                        offenders.Add(file.Key + ":" + (i + 1) + ": \"" + text + "\"");
                    }
                }
            }
            Assert.True(calls > 40, "the scan found only " + calls + " calls: is it broken?");
            Assert.True(offenders.Count == 0, "a call that shows text is given a literal that is not a key:\n  " + string.Join("\n  ", offenders));
        }

        // ---- the tables (TSK-016) ----

        private static JsonObject Table(string language)
        {
            return RecoveryTestData.ParseObject(RecoveryTestData.ReadStrings(language));
        }

        private static List<string> KeysOf(JsonObject table)
        {
            var keys = new List<string>();
            foreach (KeyValuePair<string, JsonNode> pair in table) keys.Add(pair.Key);
            return keys;
        }

        private static string TextOf(JsonObject table, string key)
        {
            string text;
            Assert.True(table.TryGetString(key, out text), key + " is a string");
            return text;
        }

        private static string Placeholders(string text)
        {
            var numbers = new SortedSet<int>();
            foreach (Match m in Regex.Matches(text, @"\{(\d+)\}")) numbers.Add(int.Parse(m.Groups[1].Value));
            return string.Join(",", numbers);
        }

        [Test]
        public void TheTwoTables_HaveTheSameKeys_AllWithTheRecPrefix()
        {
            JsonObject ru = Table("ru");
            JsonObject en = Table("en");
            List<string> ruKeys = KeysOf(ru);
            List<string> enKeys = KeysOf(en);

            Assert.True(ruKeys.Count >= 150, "the tables hold the strings of the windows, the letters and the notifications: " + ruKeys.Count);
            var problems = new List<string>();
            foreach (string key in ruKeys)
            {
                if (!key.StartsWith("rec.", StringComparison.Ordinal)) problems.Add("key without the prefix: " + key);
                if (!enKeys.Contains(key)) problems.Add("en lacks " + key);
            }
            foreach (string key in enKeys)
            {
                if (!ruKeys.Contains(key)) problems.Add("ru lacks " + key);
            }
            Assert.True(problems.Count == 0, string.Join("\n  ", problems));
        }

        [Test]
        public void ThePlaceholders_AreTheSameInBothLanguages_AndNoTextIsEmpty()
        {
            JsonObject ru = Table("ru");
            JsonObject en = Table("en");

            var problems = new List<string>();
            foreach (string key in KeysOf(ru))
            {
                string ruText = TextOf(ru, key);
                string enText = TextOf(en, key);
                if (string.IsNullOrWhiteSpace(ruText) || string.IsNullOrWhiteSpace(enText)) problems.Add(key + ": an empty text");
                if (Placeholders(ruText) != Placeholders(enText)) problems.Add(key + ": ru has {" + Placeholders(ruText) + "}, en has {" + Placeholders(enText) + "}");
            }
            Assert.True(problems.Count == 0, string.Join("\n  ", problems));
        }

        [Test]
        public void TheStringsTheAcceptanceNames_Exist_WithTheirPlaceholders()
        {
            JsonObject ru = Table("ru");
            JsonObject en = Table("en");

            // key -> the placeholders its texts must have
            var needed = new Dictionary<string, string>
            {
                { "rec.menu.title", string.Empty }, { "rec.menu.open", string.Empty },
                { "rec.tab.summary", string.Empty }, { "rec.tab.assets", string.Empty }, { "rec.tab.loans", string.Empty }, { "rec.tab.journal", string.Empty },
                { "rec.summary.daysLeft", "0" }, { "rec.summary.notInDefault", string.Empty }, { "rec.summary.payment", "0,1" },
                { "rec.lines.sell", "0,1,2" }, { "rec.lines.contractsWarning", string.Empty }, { "rec.lines.minOne", string.Empty },
                { "rec.cpu.sellStock", "0" }, { "rec.cpu.retire", "0" },
                { "rec.bridge.title", string.Empty }, { "rec.bridge.terms", "0,1,2,3" }, { "rec.bridge.unavailable.active", string.Empty },
                { "rec.credit.title", string.Empty }, { "rec.credit.state", "0,1,2,3" }, { "rec.credit.autodraw", string.Empty },
                { "rec.bailout.title", string.Empty }, { "rec.bailout.loan", "0,1,2" }, { "rec.bailout.investor", "0,1,2" }, { "rec.bailout.decline", string.Empty },
                { "rec.reputation.bailout", string.Empty },
                { "rec.mail.prewarning.subject", string.Empty }, { "rec.mail.prewarning.body", "0,1" }, { "rec.mail.default.subject", string.Empty },
                { "rec.mail.default.body", "0,1,2" }, { "rec.mail.action.openMenu", string.Empty },
                { "rec.confirm.title", string.Empty }, { "rec.confirm.after", "0" },
                { "rec.notify.loanClosed", "0" }, { "rec.notify.autoDraw", "0" }, { "rec.notify.converted", "0" },
                // the question before the switch-off (TSK-022 uses these; TSK-016 asks for them in the tables)
                { "rec.disable.confirm", "0,1,2" }, { "rec.disable.defaultRisk", string.Empty }, { "rec.disable.confirmLabel", string.Empty },
            };

            var problems = new List<string>();
            foreach (KeyValuePair<string, string> row in needed)
            {
                foreach (JsonObject table in new[] { ru, en })
                {
                    string text;
                    if (!table.TryGetString(row.Key, out text)) { problems.Add("missing " + row.Key); continue; }
                    if (Placeholders(text) != row.Value) problems.Add(row.Key + ": placeholders {" + Placeholders(text) + "}, expected {" + row.Value + "}");
                }
            }
            Assert.True(problems.Count == 0, string.Join("\n  ", problems));
        }

        // The number of arguments of the call whose "(" is at <paramref name="open"/>: top-level commas, brackets, nested calls and strings respected.
        private static int ArgumentsAfterKey(string code, int afterKey)
        {
            int i = afterKey;
            while (i < code.Length && char.IsWhiteSpace(code[i])) i++;
            if (i >= code.Length || code[i] == ')') return 0;
            if (code[i] != ',') return -1;   // not a plain call: the key is used some other way

            int count = 1;
            int depth = 0;
            for (i++; i < code.Length; i++)
            {
                char c = code[i];
                if (c == '"')
                {
                    for (i++; i < code.Length && code[i] != '"'; i++)
                    {
                        if (code[i] == '\\') i++;
                    }
                    continue;
                }
                if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}')
                {
                    if (depth == 0) return count;
                    depth--;
                }
                else if (c == ',' && depth == 0) count++;
            }
            return -1;
        }

        private static int PlaceholderCount(string text)
        {
            int highest = -1;
            foreach (Match m in Regex.Matches(text, @"\{(\d+)\}")) highest = Math.Max(highest, int.Parse(m.Groups[1].Value));
            return highest + 1;
        }

        [Test]
        public void EveryCallThatFillsAString_PassesAsManyArgumentsAsTheStringHasPlaceholders()
        {
            // L("rec.x", a, b), ModL10n.L(...), t.L(...), ModNotify.ShowKey(Feature, "rec.x", a): a string with {2} and two arguments is a bug the player sees as a hole in a sentence.
            JsonObject ru = Table("ru");
            JsonObject en = Table("en");
            var call = new Regex("(?:\\bL\\(\\s*|\\bShowKey\\(\\s*[A-Za-z_.]+\\s*,\\s*)\"(rec\\.[A-Za-z0-9_.\\-]*[A-Za-z0-9_\\-])\"");

            var problems = new List<string>();
            int checkedCalls = 0;
            foreach (KeyValuePair<string, string> file in AllCode(false))
            {
                foreach (Match m in call.Matches(file.Value))
                {
                    string key = m.Groups[1].Value;
                    int given = ArgumentsAfterKey(file.Value, m.Index + m.Length);
                    if (given < 0) continue;

                    checkedCalls++;
                    string where = file.Key + ":" + SourceScan.LineOf(file.Value, m.Index) + " " + key;
                    foreach (JsonObject table in new[] { ru, en })
                    {
                        string text;
                        if (!table.TryGetString(key, out text)) continue;   // the other test names the missing key
                        int needed = PlaceholderCount(text);
                        if (needed != given) problems.Add(where + ": the string has " + needed + " placeholder(s), the call passes " + given);
                    }
                }
            }
            Assert.True(checkedCalls > 100, "the scan found only " + checkedCalls + " calls: is it broken?");
            Assert.True(problems.Count == 0, string.Join("\n  ", problems));
        }

        [Test]
        public void TheArgumentCounter_CountsTopLevelArgumentsOnly()
        {
            Assert.Equal(0, ArgumentsAfterKey(")", 0));
            Assert.Equal(1, ArgumentsAfterKey(", a)", 0));
            Assert.Equal(3, ArgumentsAfterKey(", Num(lines), wafer ?? string.Empty, _t.Money(revenue, 2))", 0));
            Assert.Equal(2, ArgumentsAfterKey(", \"a, b)\", x[1, 2])", 0), "commas inside strings and brackets are not arguments");
            Assert.Equal(-1, ArgumentsAfterKey(" + suffix)", 0), "a key used some other way is skipped");
        }

        [Test]
        public void TheGameTemplatesAndTheBadKeys_AreNotInTheTables()
        {
            // a stray text of the game's own tooltips or a key of another feature would mean a table copied from elsewhere
            foreach (string language in new[] { "ru", "en" })
            {
                foreach (string key in KeysOf(Table(language)))
                    Assert.False(key.StartsWith("rec..", StringComparison.Ordinal) || key.EndsWith(".", StringComparison.Ordinal) || key.Contains(" "), "a malformed key: " + key);
            }
        }
    }
}
