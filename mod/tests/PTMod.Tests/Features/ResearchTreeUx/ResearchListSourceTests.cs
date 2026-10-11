using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// The rules of TSK-009 that live in the shape of the code and cannot be run without the game (the sources are read as text): the list is
    /// built from the widgets of <c>ModUI</c>, "Research" takes the path of the button of the tree, the rows are made once and reused, the day
    /// redraws the list once, the mode and the filters go to the profile, and the list leaves the buttons and the lines of the tree alone.
    /// What the owner checks in the game is the look of the panel, the click, the warning and the time of a redraw.
    /// </summary>
    public class ResearchListSourceTests
    {
        private const string Pascal = "ResearchTreeUx";

        private static string FeatureDir()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", Pascal);
        }

        private static string Code(string relative, bool blankStrings = true)
        {
            string path = Path.Combine(FeatureDir(), relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "missing " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), blankStrings);
        }

        // The text of a member from its signature to the brace that closes it (the code is already stripped of comments).
        private static string BodyOf(string code, string signature)
        {
            int start = code.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, signature + " exists");

            int open = code.IndexOf('{', start);
            int depth = 0;
            for (int i = open; i < code.Length; i++)
            {
                if (code[i] == '{') depth++;
                else if (code[i] == '}' && --depth == 0) return code.Substring(open, i - open + 1);
            }
            return code.Substring(open);
        }

        // ---- the widgets (D3) ----

        [Test]
        public void TheList_IsMadeOfTheWidgetsOfModUi_NotOfCopiesOfTheGamesList()
        {
            string view = Code("List/ResearchListView.cs");
            string row = Code("List/ResearchListRow.cs");

            foreach (string call in new[] { "ModUI.Toggle(", "ModUI.Dropdown(", "ModUI.ScrollView(", "ModUI.Text(" })
                Assert.True(view.Contains(call), "the view uses " + call);
            foreach (string call in new[] { "ModUI.Button(", "ModUI.Text(" })
                Assert.True(row.Contains(call), "the row uses " + call);

            // The list of the game subscribes to DateController in its Start and never unsubscribes (decision D3): a copy would throw inside the tick.
            var gameList = new Regex(@"(?<![\w.])(?:ResearchList|researchListPrefab)(?![\w])");
            foreach (string code in new[] { view, row })
                Assert.False(gameList.IsMatch(code), "no use of the game's own ResearchList");
        }

        [Test]
        public void ThreeTogglesAndADropdown_AreTheControlsOfTheList()
        {
            string view = Code("List/ResearchListView.cs", false);

            Assert.Equal(3, Regex.Matches(view, @"ModUI\.Toggle\(").Count, "the three filters");
            Assert.Equal(1, Regex.Matches(view, @"ModUI\.Dropdown\(").Count, "the sort");
            foreach (string key in new[] { "rtux.list.onlyAvailable", "rtux.list.hideResearched", "rtux.list.hideUnnecessary", "rtux.list.empty", "rtux.list.research", "rtux.lab.busy" })
                Assert.True(view.Contains("\"" + key + "\""), "the caption " + key);
        }

        // ---- choosing a research (FR-2 item 5, NFR-3) ----

        [Test]
        public void Research_TakesTheSamePathAsTheButtonOfTheTree()
        {
            string body = BodyOf(Code("List/ResearchListView.cs"), "private void StartResearch(");

            int sector = body.IndexOf("ResearchSector.SetCurrentResearch(technology)", StringComparison.Ordinal);
            int panel = body.IndexOf("CurrentResearchUi.SetTechnology(technology)", StringComparison.Ordinal);
            int window = body.IndexOf("Window.ForceUIUpdate()", StringComparison.Ordinal);
            Assert.True(sector >= 0 && panel > sector && window > panel,
                "SetCurrentResearch, then SetTechnology, then ForceUIUpdate - the order of the click handler of the tree button");
        }

        [Test]
        public void ATechnologyTheCompanyDoesNotNeed_GoesThroughTheWarningOfTheGame()
        {
            string body = BodyOf(Code("List/ResearchListView.cs"), "public void Select(");

            int rule = body.IndexOf("ListRowRules.ActionOf(", StringComparison.Ordinal);
            int warning = body.IndexOf("warning.Open(start)", StringComparison.Ordinal);
            int direct = body.LastIndexOf("start();", StringComparison.Ordinal);
            Assert.True(rule >= 0, "the rule decides");
            Assert.True(warning > rule, "the warning of the game opens for StartAfterWarning");
            Assert.True(direct > warning, "the direct start comes after the branch of the warning, which returns");
            Assert.True(body.Contains("ChooseAction.StartAfterWarning"), "the branch");
            Assert.True(Regex.IsMatch(body, @"warning\.Open\(start\);\s*return;"), "the warning branch does not fall through to a direct start");
        }

        [Test]
        public void TheCallbackOfTheWarning_RunsUnderTheGuardOfTheFeature()
        {
            string view = Code("List/ResearchListView.cs", false);

            Assert.True(Regex.IsMatch(view, @"ModHooks\.Guard\(\s*ResearchTreeUxFeature\.FeatureId\s*,\s*""ResearchList/start""\s*,\s*\(\)\s*=>\s*StartResearch\("),
                "the game calls the confirm callback, so an exception must not leave it");
        }

        // ---- the rows are made once ----

        [Test]
        public void TheRows_AreMadeOnDemandAndReused()
        {
            string view = Code("List/ResearchListView.cs");
            string refresh = BodyOf(view, "public void Refresh()");

            Assert.Equal(1, Regex.Matches(view, @"ResearchListRow\.Create\(").Count, "one place makes a row");
            Assert.True(view.Contains("_rows.TryGetValue("), "a made row is looked up first");
            Assert.True(Regex.IsMatch(refresh, @"\.SetActive\(\s*false\s*\)"), "rows that are not needed are switched off on a redraw");
            Assert.False(refresh.Contains("Destroy"), "and never destroyed on a redraw");
        }

        [Test]
        public void TheRow_WritesOnlyWhatChanged()
        {
            string row = Code("List/ResearchListRow.cs");

            Assert.True(row.Contains("private static void Write("), "a text is written through the comparing helper");
            Assert.True(Regex.IsMatch(row, @"string\.Equals\(\s*last\s*,\s*value\s*,\s*StringComparison\.Ordinal\s*\)"), "same text, no write");
            Assert.Equal(1, Regex.Matches(row, @"\.text\s*=").Count, "the helper is the only place that writes a text of a column");
        }

        // ---- the day (FR-2 item 7) ----

        [Test]
        public void TheDay_RedrawsTheList_ThroughTheTickOfTheCore_AndOncePerFrame()
        {
            string feature = Code("ResearchTreeUxFeature.cs");
            string view = Code("Tree/TreeUxView.cs");
            string list = Code("List/ResearchListView.cs");

            Assert.True(Regex.IsMatch(feature, @"ctx\.OnTick\(\s*TickPhase\.DayPassed\s*,\s*OnDay\s*\)"), "the feature subscribes through FeatureContext.OnTick");
            Assert.True(Regex.IsMatch(feature, @"private\s+void\s+OnDay\(\)\s*\{[^}]*OnDayPassed\(\)"), "OnDay tells the view");
            Assert.True(Regex.IsMatch(view, @"public\s+void\s+OnDayPassed\(\)\s*\{[^}]*RefreshOncePerFrame\(\)"), "the view redraws the list");
            Assert.True(Regex.IsMatch(view, @"OnButtonStatesUpdated\(\)\s*\{[^}]*RefreshOncePerFrame\(\)"), "the postfix of UpdateButtonStates asks too, and the second ask is free");
            Assert.True(Regex.IsMatch(list, @"_lastRefreshFrame\s*==\s*UnityTime\.frameCount"), "within one frame the second ask returns");
            Assert.True(Regex.IsMatch(list, @"public\s+void\s+RefreshOncePerFrame\(\)\s*\{\s*if\s*\(\s*!IsVisible\s*\)\s*return;"), "nothing is done while the list is not on screen");
        }

        [Test]
        public void TheRedraw_TimesItselfForTheDebugLog()
        {
            string list = Code("List/ResearchListView.cs", false);

            Assert.True(list.Contains("PerfProbe.Begin()"), "NFR-1: the time of a redraw is measured");
            Assert.True(list.Contains("PerfOp.ListRefresh"), "against the budget of the list, and written to the debug log");
        }

        // ---- the switch and the profile ----

        [Test]
        public void TheSwitch_IsTwoButtonsOnTheTabStrip()
        {
            string strip = Code("Tree/TabStrip.cs", false);

            Assert.True(strip.Contains("\"rtux.view.tree\"") && strip.Contains("\"rtux.view.list\""), "the two captions");
            Assert.True(Regex.IsMatch(strip, @"public\s+event\s+Action<string>\s+ViewModeClicked"), "a click is reported to the owner");
            Assert.True(Regex.IsMatch(strip, @"public\s+void\s+SetViewMode\(\s*string\s+mode\s*\)"), "the owner marks the mode");
            Assert.True(Regex.IsMatch(strip, @"Build\(\s*UnityTransform\s+parent\s*,\s*IReadOnlyList<TabDefinition>\s+tabs\s*,\s*bool\s+withViewSwitch\s*=\s*false\s*\)"),
                "the switch is built only when the list is");
        }

        [Test]
        public void TheModeAndTheFilters_GoToTheProfile_ThatTheCoreWrites()
        {
            string view = Code("Tree/TreeUxView.cs");
            string list = Code("List/ResearchListView.cs");

            Assert.True(Regex.IsMatch(view, @"ResearchUxPrefs\.Current\.ViewMode\s*=\s*wanted;\s*ResearchUxPrefs\.Save\(\)"), "the mode of a click is saved");
            Assert.True(Regex.IsMatch(view, @"SetViewMode\(\s*ResearchUxPrefs\.Current\.ViewMode\s*,\s*false\s*\)"), "and restored without being written back");
            Assert.True(Regex.IsMatch(list, @"_filter\.WriteTo\(\s*ResearchUxPrefs\.Current\s*\)\s*;\s*ResearchUxPrefs\.Save\(\)"), "a change of a filter is saved");
            Assert.True(Regex.IsMatch(list, @"ListFilter\.FromPrefs\(\s*ResearchUxPrefs\.Current\s*\)"), "and restored");
        }

        [Test]
        public void WhenTheListCannotBeMade_TheTreeStays()
        {
            string view = Code("Tree/TreeUxView.cs");

            Assert.True(Regex.IsMatch(view, @"if\s*\(\s*list\s*==\s*null\s*\|\|\s*!list\.Show\(\)\s*\)\s*wanted\s*=\s*UxPrefsModel\.ViewTree"), "NFR-2: a failed list leaves the tree");
        }

        // ---- the lab (FR-2 item 9, D10) ----

        [Test]
        public void TheBusyLab_IsAskedOfTheSeamOfTheFeature_AndTheButtonsStayActive()
        {
            string feature = Code("ResearchTreeUxFeature.cs");
            string view = Code("Tree/TreeUxView.cs");
            string list = Code("List/ResearchListView.cs", false);

            Assert.True(Regex.IsMatch(feature, @"internal\s+Func<string>\s+LabBusyTextProvider\s*\{\s*get;\s*set;\s*\}"), "the seam TSK-014 fills");
            Assert.True(Regex.IsMatch(feature, @"internal\s+string\s+LabBusyText\(\)"), "caught at the border");
            Assert.True(view.Contains("LabBusyText = _feature.LabBusyText"), "the view hands it to the list");
            Assert.True(Regex.IsMatch(list, @"ModL10n\.L\(\s*""rtux\.lab\.busy""\s*,\s*busy\s*\)"), "the line is written from the text of the source");
            Assert.False(Regex.IsMatch(list, @"Interactable\s*=[^;]*[Ll]ab"), "the occupancy never disables a button (the owner of the occupancy settles it)");
        }

        // ---- the tree is left alone (D1, NFR-3) ----

        [Test]
        public void TheList_NeverTouchesTheButtonsOrTheLinesOfTheTree()
        {
            var tree = new Regex(@"_tree\.(?:Buttons|Lines|TreeContent|LineLinks|ButtonOf)\b");
            foreach (string file in new[] { "List/ResearchListView.cs", "List/ResearchListRow.cs" })
            {
                string code = Code(file);
                Assert.False(tree.IsMatch(code), file + " does not read or move the buttons and lines of the tree");
            }

            // The only things the list takes from the tree are the panel of the current research and the window, for the three calls of a choice.
            string view = Code("List/ResearchListView.cs");
            var uses = new List<string>();
            foreach (Match match in Regex.Matches(view, @"_tree\.(\w+)")) uses.Add(match.Groups[1].Value);
            Assert.True(uses.Count > 0, "the choice reads the panel and the window");
            foreach (string use in uses) Assert.True(use == "CurrentResearchUi" || use == "Window", "_tree." + use);
        }

        [Test]
        public void TheView_PutsTheListAway()
        {
            string view = Code("Tree/TreeUxView.cs");
            string destroy = BodyOf(view, "public void Destroy()");

            Assert.True(destroy.Contains("_list.Destroy()"), "the panel goes with the view (FR-8 item 3)");
            Assert.True(Regex.IsMatch(Code("List/ResearchListView.cs"), @"public\s+void\s+Destroy\(\)\s*\{\s*_destroyed\s*=\s*true;\s*DestroyPanel\(\)"), "and the objects with it");
        }
    }
}
