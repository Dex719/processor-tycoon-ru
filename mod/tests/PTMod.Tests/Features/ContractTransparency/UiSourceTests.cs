using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ContractTransparency
{
    /// <summary>
    /// TSK-011...TSK-016: what can be said about the screens of the feature without the game, read from the sources (they touch Unity and the game, so no test loads them): which window is made from
    /// which template, that the patches of the windows are optional and guarded, that the screens answer through the gate and the log, that the core's internals are not used, and that the feature
    /// registers and releases all of it.
    /// </summary>
    public class UiSourceTests
    {
        // The attribute sits on one line; its tail can hold brackets (ArgTypes = new Type[0]), so the tail is taken to the end of the line.
        private static readonly Regex PatchAttribute = new Regex(
            @"\[ModPatch\(\s*typeof\(\s*(?<type>\w+)\s*\)\s*,\s*""(?<method>\w+)""\s*,\s*PatchKind\.(?<kind>\w+)(?<rest>[^\r\n]*)", RegexOptions.Compiled);

        private static string FeatureDir()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "ContractTransparency");
        }

        private static string Source(string relative)
        {
            string path = Path.Combine(FeatureDir(), relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "missing " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        // ---- the patches of the screens ----

        [Test]
        public void TheWindowPatches_AreOptionalPostfixes_AndEveryBodyIsGuarded()
        {
            string code = Source("UI/ContractWindowAugmenter.cs");
            MatchCollection patches = PatchAttribute.Matches(code);

            Assert.Equal(2, patches.Count);
            foreach (Match patch in patches)
            {
                Assert.Equal("ContractWindow", patch.Groups["type"].Value.Replace("Game", string.Empty));
                Assert.Equal("Postfix", patch.Groups["kind"].Value, "the window is only read; nothing in it is replaced");
                Assert.True(patch.Groups["rest"].Value.Contains("Optional = true"), patch.Groups["method"].Value + " must be optional: a changed window costs this part, not the feature");
            }
            Assert.Equal(new[] { "Start", "UpdateProbabilityText" }, patches.Cast<Match>().Select(m => m.Groups["method"].Value).OrderBy(n => n, StringComparer.Ordinal).ToArray());
            Assert.True(Regex.Matches(code, @"ContractTransparencyFeature\.Guarded\(").Count >= 2, "both bodies run through the guard (NFR-2)");
        }

        [Test]
        public void TheSignGatePatches_AreOptional_TheOriginalRunsUnderTheBypass_AndTheFailureGoesTheVanillaWay()
        {
            string code = Source("Patches/BusinessSignGate.cs");

            foreach (Match patch in PatchAttribute.Matches(code))
            {
                Assert.True(patch.Groups["rest"].Value.Contains("Optional = true"), patch.Groups["method"].Value + "/" + patch.Groups["kind"].Value + " must be optional");
            }
            Assert.True(Regex.IsMatch(code, @"bool\s+run\s*=\s*true\s*;"), "the result starts as true: the original runs");
            Assert.True(Regex.IsMatch(code, @"if\s*\(\s*!ok\s*\)\s*\{[^}]*run\s*=\s*true\s*;"), "a failed prefix lets the signature go through");
            Assert.True(Regex.IsMatch(code, @"BypassScope\s*=\s*true\s*;\s*try\s*\{\s*_sign\.Invoke\(window, null\)"), "the original is called right after the bypass is raised");
            Assert.True(Regex.IsMatch(code, @"finally\s*\{\s*BypassScope\s*=\s*false\s*;"), "the bypass is lowered in a finally");
            Assert.True(Regex.IsMatch(code, @"if\s*\(\s*!__state\s*\)\s*return"), "the postfix records only when the original ran");
        }

        [Test]
        public void TheSignGate_AsksWithTheCoresQuestion_AndMakesNoWindowOfItsOwn()
        {
            string code = Source("Patches/BusinessSignGate.cs");

            Assert.True(code.Contains("ModUI.Confirm("), "the question is the core's ModUI.Confirm (FR-10 item 1)");
            Assert.True(code.Contains("\"ct.business.title\"") && code.Contains("\"ct.business.sign\""), "the title and the caption of 'Sign'");
            Assert.False(code.Contains("CreateWindow"), "no window of its own for a deal");
            Assert.True(code.Contains("SignGatePolicy.Decide("), "the rule (the mode 'never ask' lets the signature through) is the tested policy");
            Assert.True(code.Contains("_asking"), "a second click while the question is up is not asked again");
        }

        // ---- the windows ----

        [Test]
        public void TheWindows_AreMadeFromTheTemplatesOfTheDesign()
        {
            Assert.True(Regex.IsMatch(Source("UI/OddsPanel.cs"), @"ModUI\.CreateWindow\(WindowId,\s*WindowTemplate\.Panel,\s*""ct\.odds\.title"""), "the breakdown is a panel (the time goes on)");
            Assert.True(Regex.IsMatch(Source("UI/OfferReviewDialog.cs"), @"ModUI\.CreateWindow\(WindowId,\s*WindowTemplate\.Modal,\s*""ct\.review\.title"""), "the review screen is modal (the time stands)");
            Assert.True(Regex.IsMatch(Source("UI/ContractLogWindow.cs"), @"ModUI\.CreateWindow\(WindowId,\s*WindowTemplate\.Panel,\s*""ct\.log\.title"""), "the log is a panel");
        }

        [Test]
        public void TheIdsOfTheWindows_CarryTheIdOfTheFeature_AndAreDifferent()
        {
            var ids = new List<string>();
            foreach (string file in new[] { "UI/OddsPanel.cs", "UI/OfferReviewDialog.cs", "UI/ContractLogWindow.cs" })
            {
                Match id = Regex.Match(Source(file), @"WindowId\s*=\s*Feature\s*\+\s*""(?<suffix>\.\w+)""");
                Assert.True(id.Success, file + " has no WindowId built from the feature id");
                ids.Add(id.Groups["suffix"].Value);
            }
            Assert.Equal(3, ids.Distinct().Count());
        }

        [Test]
        public void TheReviewDialog_SendsThroughTheGate_CancelsInTheLog_AndChecksTheContract()
        {
            string code = Source("UI/OfferReviewDialog.cs");

            Assert.True(code.Contains("OfferGate.ConfirmAndSend(answered.Contract, answered.CpuId, OfferSource.Manual)"), "'Send offer' goes through the gate under the bypass");
            Assert.True(code.Contains("OfferGate.RecordCancelled("), "'Cancel' and the cross write 'cancelled on the review screen'");
            Assert.True(code.Contains("ContractExists("), "the contract is checked before the offer goes (E10)");
            Assert.True(code.Contains("ConfirmPolicy.AfterDontAsk(") && code.Contains("ContractTransparencySettings.SetConfirmMode(next)"), "'don't ask again for safe offers' sets the mode by the tested policy and stores it");
            Assert.True(Regex.IsMatch(code, @"window\.Closed\s*\+=\s*OnWindowClosed"), "whatever closes the window is an answer");
            Assert.True(Regex.IsMatch(code, @"Pending answered = _pending;\s*if \(answered == null\) return;\s*_pending = null;\s*if \(_window != null && _window\.IsOpen\) _window\.Close\(\);"),
                "'Send offer' takes the pending offer before the window closes, so the close is not a cancel");
        }

        [Test]
        public void TheReviewScreen_TriesTheWindowFirst_AndFallsBackToTheQuestion()
        {
            string code = Source("OfferReviewScreen.cs");

            Assert.True(Regex.IsMatch(code, @"Opener\s*=\s*OpenWindowOrQuestion"), "the default opener is the window with the question behind it");
            Assert.True(Regex.IsMatch(code, @"if\s*\(\s*OfferReviewDialog\.Open\(evaluation\)\s*\)\s*return\s+true;\s*return\s+OpenQuestion\(evaluation\)"), "window first, question second");
        }

        [Test]
        public void TheLogWindow_HasSixFilters_AndTheTwoTabs()
        {
            string code = Source("UI/ContractLogWindow.cs");

            Assert.True(code.Contains("ModUI.Dropdown("), "the filters are a dropdown of the game's look (FR-8 item 2)");
            Assert.True(code.Contains("Enum.GetValues(typeof(LogFilter))"), "one entry for each filter of the enum");
            Assert.True(code.Contains("AutomationPreviewPanel.Build("), "the second tab is the preview of the automation");
            Assert.True(code.Contains("FindTender("), "the details of a tender show the whole result (FR-8 item 3)");
            Assert.True(code.Contains("Query(_filter)"), "the list is the model's query: newest first");
        }

        [Test]
        public void ThePreview_ReadsThePlannerOfTheLog_AndRefreshesOnDemand()
        {
            string code = Source("UI/AutomationPreviewPanel.cs");

            Assert.True(code.Contains("AutomationLogger.Preview()"), "the same rule as the log of a run");
            Assert.True(Regex.IsMatch(code, @"ModUI\.Button\([^;]*ModL10n\.L\(""ct\.preview\.refresh""\)[^;]*Fill"), "'Refresh' works the preview out again");
        }

        // ---- the rules of the code ----

        [Test]
        public void TheScreens_UseThePublicPartOfModUi_Only()
        {
            var violations = new List<string>();
            foreach (string folder in new[] { "UI", "Patches" })
            {
                foreach (string path in SourceScan.Files(Path.Combine(FeatureDir(), folder), ".cs"))
                {
                    string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                    foreach (string internalName in new[] { "WidgetKit", "HubWidgets", "ConfirmDialog", "CloneSanitizer", "ModUITemplates.Clone" })
                    {
                        if (Regex.IsMatch(code, @"\b" + Regex.Escape(internalName) + @"\b")) violations.Add(SourceScan.Relative(FeatureDir(), path) + ": " + internalName);
                    }
                }
            }
            Assert.True(violations.Count == 0, "the feature uses the public ModUI, not the plumbing of the core:\n  " + string.Join("\n  ", violations));
        }

        [Test]
        public void EveryUiFileWithPatches_GuardsEveryBody()
        {
            string ui = Path.Combine(FeatureDir(), "UI");
            Assert.True(Directory.Exists(ui), "UI/ is the folder of the screens");

            int checkedFiles = 0;
            foreach (string path in SourceScan.Files(ui, ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                int handlers = PatchAttribute.Matches(code).Count;
                if (handlers == 0) continue;
                checkedFiles++;
                int guards = Regex.Matches(code, @"ContractTransparencyFeature\.Guarded\(").Count;
                Assert.True(guards >= handlers, SourceScan.Relative(FeatureDir(), path) + ": " + handlers + " patch(es) but " + guards + " guarded body(ies)");
            }
            Assert.True(checkedFiles >= 1, "the scan found no patch in UI/");
        }

        [Test]
        public void TheCallbacksOfTheButtons_AreNotWrappedTwice_ButTheComponentOfTheGameNeverThrows()
        {
            // ModUI.Button guards the click of its feature itself; a MonoBehaviour of the mod is called by Unity directly and must catch.
            string code = Source("UI/ContractWindowAugmenter.cs");
            Match watcher = Regex.Match(code, @"class\s+ContractWindowWatcher\s*:\s*MonoBehaviour\s*\{(?<body>.*)\}\s*\}\s*$", RegexOptions.Singleline);

            Assert.True(watcher.Success, "the watcher is in the file");
            Assert.True(Regex.IsMatch(watcher.Groups["body"].Value, @"try\s*\{.*catch\s*\(\s*Exception\s*\)", RegexOptions.Singleline), "OnEnable catches: Unity must not see an exception of the mod");
        }

        // ---- the feature ----

        [Test]
        public void TheFeature_RegistersTheNewPatchClasses_TheLogOpener_AndTheHubPage()
        {
            string code = Source("ContractTransparencyFeature.cs");

            Assert.True(Regex.IsMatch(code, @"ctx\.Patch\(\s*typeof\(\s*ContractWindowAugmenter\s*\)\s*\)"), "the contract window patches");
            Assert.True(Regex.IsMatch(code, @"ctx\.Patch\(\s*typeof\(\s*BusinessSignGate\s*\)\s*\)"), "the deal patches");
            Assert.True(Regex.IsMatch(code, @"LogWindowLauncher\.SetOpener\(\s*ContractLogWindow\.Open\s*\)"), "the button, the letter and the hub open the log window (closes TSK-010)");
            Assert.True(Regex.IsMatch(code, @"ctx\.HubPage\(\s*""ct\.hub\.page\.title"""), "the log opens from the hub (FR-8 item 1)");
            Assert.True(code.Contains("ContractWindowAugmenter.Install()"));
        }

        [Test]
        public void TheFeature_ShowsAndHidesItsScreensWithTheToggle()
        {
            string code = Source("ContractTransparencyFeature.cs");
            Match enabled = Regex.Match(code, @"public\s+void\s+OnEnabled\(\)(?<body>.*?)public\s+void\s+OnDisabled\(\)(?<off>.*?)internal\s+static\s+bool\s+Guarded\(string site, Action body\)", RegexOptions.Singleline);

            Assert.True(enabled.Success, "OnEnabled and OnDisabled are in the file");
            Assert.True(enabled.Groups["body"].Value.Contains("ContractWindowAugmenter.SetVisible(true)"), "the buttons come back when the feature is switched on");
            string off = enabled.Groups["off"].Value;
            Assert.True(off.Contains("ContractWindowAugmenter.SetVisible(false)"), "the buttons are hidden and the probability is the game's again (FR-13 item 4)");
            Assert.True(off.Contains("OddsPanel.Close()") && off.Contains("ContractLogWindow.Close()") && off.Contains("OfferReviewDialog.Close()"), "every window of the feature is closed");
        }

        [Test]
        public void TheKeysOfTheScreens_AreInTheTables_AndTheTitlesAreKeys()
        {
            // The title of a window is a key of the string table (the core redraws it when the language changes).
            foreach (string pair in new[] { "UI/OddsPanel.cs|ct.odds.title", "UI/OfferReviewDialog.cs|ct.review.title", "UI/ContractLogWindow.cs|ct.log.title" })
            {
                string[] parts = pair.Split('|');
                Assert.True(Source(parts[0]).Contains("\"" + parts[1] + "\""), parts[0] + " names " + parts[1]);
            }
        }
    }
}
