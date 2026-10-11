using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// The rules of TSK-011 and TSK-013 that live in the shape of the code and cannot be run without the game (the sources are read as text): the
    /// modifiers panel and the compare window are made of the widgets of <c>ModUI</c> and do not stop the game, <c>ModModifiers.Explain</c> is asked
    /// for the seven channels of the types 0 to 6 with the player's company, the tooltip is built when the cursor comes, the compare window reads only
    /// what the inspector and the market windows show (never the aims of the AI), keeps no object of the game between redraws, and the button of the
    /// inspector, the hub page and the windows are all taken away when the feature is switched off. What the owner checks in the game is the look
    /// and the place of every piece.
    /// </summary>
    public class ModifiersCompareSourceTests
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

        // ---- the modifiers (TSK-011) ----

        [Test]
        public void TheSnapshot_AsksExplainForTheSevenChannelsOfTheTypes0To6_WithThePlayer()
        {
            string reader = Code("Modifiers/ModifierSnapshotReader.cs", false);

            // type -> channel, in the order of the enum (design: Component 7; the table of patch P1 of the core)
            var channels = new[]
            {
                "ModChannels.ResearchSpeed", "ModChannels.FactoryOperatingCost", "ModChannels.ConstructionCost", "ModChannels.DevelopmentCost",
                "ModChannels.ResearchCost", "ModChannels.Taxes", "ModChannels.BankInterest",
            };
            int previous = -1;
            foreach (string channel in channels)
            {
                int at = reader.IndexOf(channel, StringComparison.Ordinal);
                Assert.True(at > previous, channel + " comes in the order of the types");
                previous = at;
            }

            Assert.True(Regex.IsMatch(reader, @"ModModifiers\.Explain\(\s*Channels\[type\]\s*,\s*ModifierContext\.For\(company\)\s*\)"), "Explain with the player's company");
            Assert.True(reader.Contains("GameModifiers.GetModifier((GameModifierType)type, company)"), "the totals of the game's own call");
        }

        [Test]
        public void AFailingExplain_LeavesNullForThatChannel_AndTheReportGoesOn()
        {
            string body = BodyOf(Code("Modifiers/ModifierSnapshotReader.cs"), "private static void ReadExplain(");

            Assert.True(Regex.IsMatch(body, @"try\s*\{[^}]*Explain[^}]*\}\s*catch\s*\(\s*Exception\s+\w+\s*\)\s*\{[^}]*ExplainByType\[type\]\s*=\s*null"), "a throw gives null for the channel");
            Assert.True(body.Contains("!breakdown.IsDeclared"), "a channel that is not declared is treated as not available");
        }

        [Test]
        public void EachPartOfTheSnapshot_IsReadUnderItsOwnGuard()
        {
            string reader = Code("Modifiers/ModifierSnapshotReader.cs", false);

            foreach (string part in new[] { "\"facts\"", "\"research\"", "\"totals\"", "\"bonuses\"", "\"projects\"" })
                Assert.True(reader.Contains("Guard(" + part), "the part " + part);
        }

        [Test]
        public void TheRunningProjects_AreTheResearchProjectsOfThePlayersCompany()
        {
            string body = BodyOf(Code("Modifiers/ModifierSnapshotReader.cs"), "private static void ReadProjects(");

            Assert.True(body.Contains("GetTimedObjectsOfType<GameProject>()"), "from the scheduler of the game");
            Assert.True(body.Contains("ReferenceEquals(project.Company, company)"), "of this company only");
            Assert.True(body.Contains("GetResearchProject()"), "only the research ones");
        }

        [Test]
        public void ThePanel_IsAWindowOfModUi_ThatDoesNotStopTheGame_AndIsMadeOfItsWidgets()
        {
            string panel = Code("Modifiers/ModifiersPanel.cs");

            Assert.True(Regex.IsMatch(panel, @"ModUI\.CreateWindow\([^;]*WindowTemplate\.Panel"), "the panel template does not pause the game (the modal one does)");
            Assert.False(panel.Contains("WindowTemplate.Modal"), "never modal");
            Assert.True(panel.Contains("ModUI.Text("), "the lines are texts of ModUI");
            Assert.True(panel.Contains("UxWidgets.ScrollBody("), "in a scrolling body");
        }

        [Test]
        public void ThePanel_IsRedrawnOncePerFrame_AndOnlyWhileItIsOpen()
        {
            string panel = Code("Modifiers/ModifiersPanel.cs");

            Assert.True(Regex.IsMatch(panel, @"public\s+void\s+RefreshIfOpen\(\)\s*\{\s*if\s*\(\s*!IsOpen\s*\|\|\s*_lastRefreshFrame\s*==\s*UnityTime\.frameCount\s*\)\s*return;"),
                "a shut panel costs one check; the second ask of a frame returns");
            Assert.True(panel.Contains("PerfProbe.Begin()") && panel.Contains("PerfOp.ModifiersPanel"), "the time of a redraw goes to the debug log through the probe (NFR-1)");
        }

        [Test]
        public void ThePanelAndTheButton_ShowTheSameReport()
        {
            string reports = Code("Modifiers/ModifierReports.cs");
            string panel = Code("Modifiers/ModifiersPanel.cs");

            Assert.True(panel.Contains("ModifierReports.Build(_feature)"), "the panel reads the report");
            Assert.True(reports.Contains("report.ToTooltip(GameTipFormat.Instance)"), "the tooltip is made of the report");
            Assert.True(Regex.IsMatch(reports, @"ModifierReportBuilder\.Build\(\s*snapshot\s*,\s*extra\s*,\s*GameTipFormat\.Instance\s*\)"), "by the pure builder with the game's formatters");
        }

        [Test]
        public void TheButton_IsOnTheStrip_WithTheTooltipBuiltWhenTheCursorComes_AndTheClickTogglesThePanel()
        {
            string view = Code("Tree/TreeUxView.cs");
            string strip = Code("Tree/TabStrip.cs", false);

            Assert.True(strip.Contains("\"rtux.mod.button\""), "the caption");
            Assert.True(Regex.IsMatch(strip, @"public\s+bool\s+AddModifiersButton\(\)"), "added to the strip on request");
            Assert.True(Regex.IsMatch(strip, @"public\s+event\s+Action\s+ModifiersClicked"), "a click is reported to the owner");
            Assert.True(Regex.IsMatch(view, @"TechTooltips\.Attach\(\s*strip\.ModifiersButtonObject\s*,\s*BuildModifiersTip\s*\)"), "the tooltip is a provider, called on the hover");
            Assert.True(Regex.IsMatch(view, @"OnModifiersClicked\(\)\s*\{[^}]*_modifiers\.Toggle\(\)"), "the click opens and closes the pinned panel");
        }

        [Test]
        public void ThePanel_FollowsTheDay_TheLanguage_AndTheChosenResearch()
        {
            string view = Code("Tree/TreeUxView.cs");

            Assert.True(Regex.IsMatch(view, @"OnDayPassed\(\)\s*\{[^}]*_modifiers\.RefreshIfOpen\(\)"), "the tick of the day");
            Assert.True(Regex.IsMatch(view, @"OnButtonStatesUpdated\(\)\s*\{[^}]*_modifiers\.RefreshIfOpen\(\)"), "a research chosen or stopped");
            Assert.True(Regex.IsMatch(view, @"OnLanguageChanged\(\)\s*\{[^}]*_modifiers\.OnLanguageChanged\(\)"), "the language");
        }

        [Test]
        public void TheView_PutsThePanelAway_BeforeTheStrip()
        {
            string body = BodyOf(Code("Tree/TreeUxView.cs"), "public void Destroy()");

            int panel = body.IndexOf("_modifiers.Destroy()", StringComparison.Ordinal);
            int strip = body.IndexOf("_strip.Destroy()", StringComparison.Ordinal);
            Assert.True(panel > 0 && strip > panel, "the panel goes with the view (FR-8 item 3)");
        }

        [Test]
        public void TheBlocksOfOtherFeatures_ComeThroughTheSeam_AndAFailureOfASourceStaysThere()
        {
            string feature = Code("ResearchTreeUxFeature.cs");

            Assert.True(Regex.IsMatch(feature, @"internal\s+Func<GameCompany,\s*IReadOnlyList<ModifierLineGroup>>\s+ModifierGroupsProvider\s*\{\s*get;\s*set;\s*\}"), "the seam TSK-014 fills");
            string body = BodyOf(feature, "internal IReadOnlyList<ExtraModifierGroup> ModifierGroupsFor(");
            Assert.True(body.Contains("catch (Exception"), "caught at the border");
        }

        // ---- the comparison (TSK-013) ----

        [Test]
        public void TheCompareWindow_IsAWindowOfModUi_ThatDoesNotStopTheGame()
        {
            string window = Code("Compare/CompareWindow.cs");

            Assert.True(Regex.IsMatch(window, @"ModUI\.CreateWindow\([^;]*WindowTemplate\.Panel"), "the panel template does not pause the game");
            Assert.False(window.Contains("WindowTemplate.Modal"), "never modal");
        }

        [Test]
        public void TheCompareWindow_HasTwoPairsOfDropdownsAndASwapButton()
        {
            string window = Code("Compare/CompareWindow.cs", false);

            Assert.Equal(2, Regex.Matches(window, @"ModUI\.Dropdown\(").Count, "the code makes a pair per side, once for each of the two");
            Assert.True(Regex.IsMatch(window, @"ModUI\.Button\(\s*content\s*,\s*ModL10n\.L\(""rtux\.compare\.swap""\)\s*,\s*Swap\s*\)"), "the button 'Swap'");
            Assert.True(window.Contains("\"rtux.compare.company\"") && window.Contains("\"rtux.compare.cpu\""), "the captions of the dropdowns");
            Assert.True(Regex.IsMatch(window, @"BuildSide\(\s*_a\b") && Regex.IsMatch(window, @"BuildSide\(\s*_b\b"), "both sides are built");
        }

        [Test]
        public void TheCompareWindow_KeepsOnlyKeysAndSaveIds_AndChecksThemEveryRedraw()
        {
            string window = Code("Compare/CompareWindow.cs");
            string refresh = BodyOf(window, "private void RefreshAll()");

            Assert.True(refresh.Contains("_selection.Validate(CompanyAvailable, CpuAvailable)"), "AC-6.3: a gone company or processor resets its side");
            Assert.False(Regex.IsMatch(window, @"\b(?:CpuRef|CompareSelection)\b[^;]*\bGameCpu\b[^;]*;"), "the selection holds no object of the game");
            // The fields of the class: no GameCpu of its own except the lists rebuilt on every redraw.
            Assert.True(Regex.IsMatch(window, @"public\s+List<GameCpu>\s+Cpus\s*=\s*new\s+List<GameCpu>\(\)"), "the lists of the dropdowns");
            Assert.True(window.Contains("side.Cpus = CpuFactsReader.CpusOf("), "rebuilt from the game every time");
        }

        [Test]
        public void TheCompareWindow_IsRedrawnEveryDay_AndWhenTheLanguageOrTheThemeChange()
        {
            string feature = Code("ResearchTreeUxFeature.cs");
            string window = Code("Compare/CompareWindow.cs");

            Assert.True(Regex.IsMatch(feature, @"private\s+void\s+OnDay\(\)\s*\{[^}]*compare\.Refresh\(\)"), "the tick of the day");
            Assert.True(window.Contains("ModL10n.LanguageChanged += OnLanguageChanged") && window.Contains("ModUI.ThemeChanged += OnThemeChanged"), "the language and the theme");
            Assert.True(window.Contains("ModL10n.LanguageChanged -= OnLanguageChanged") && window.Contains("ModUI.ThemeChanged -= OnThemeChanged"), "and they are let go");
        }

        [Test]
        public void TheFactsOfAProcessor_AreOnlyWhatTheInspectorAndTheMarketWindowsShow()
        {
            string reader = Code("Compare/CpuFactsReader.cs");

            // FR-6 item 10: the aims of the AI, its production lines and its stock are not shown by the game; the comparison does not read them.
            var hidden = new Regex(@"\b(?:Strategy|AIStrategy|TargetMarket|TargetPrice|CurrentProductionLines|CurrentContractedProductionLines|LinesActuallyAllocated|FailedStock|"
                + @"ProducedLastDay|IncomeLastDay|IncomeLastDayFromContracts|DevelopmentCost|MissedSales|Stock|PotentialBuyers)\b");
            Assert.False(hidden.IsMatch(reader), "CpuFactsReader reads a field the game does not show for a rival");

            foreach (string shown in new[] { "cpu.PowerConsumption", "cpu.Ips", "cpu.Price", "cpu.AveragePopularity", "cpu.RealUnitCost", "cpu.Market" })
                Assert.True(reader.Contains(shown), shown + " is read");
        }

        [Test]
        public void TheListOfCompanies_LeavesOutFoundriesAndTheBankrupt_LikeTheInspector()
        {
            string body = BodyOf(Code("Compare/CompareWindow.cs"), "private void ReadCompanies()");

            Assert.True(body.Contains("company.IsBankrupt || company.IsFoundry"), "the inspector lists neither");
            Assert.True(body.Contains("ModCompanies.All"), "from the core's list of companies");
        }

        [Test]
        public void TheFirstColumnOfThePair_IsThePlayer_AndTheSecondTheFirstRival()
        {
            string body = BodyOf(Code("Compare/CompareWindow.cs"), "private string DefaultCompanyKey(");

            Assert.True(body.Contains("_companies[0].Key") && body.Contains("_companies[1].Key"), "A: the player (the first of the list), B: the next company");
        }

        [Test]
        public void TheInspectorButton_StandsBesideTheOldOne_AndOpensTheWindowWithTheCurrentCpuAsA()
        {
            string button = Code("Compare/InspectorButton.cs", false);

            Assert.True(button.Contains("\"simpleOrDetailedButton\"") && button.Contains("\"currentCpu\""), "the two private fields of the game");
            Assert.True(Regex.IsMatch(button, @"_feature\.OpenCompare\(\s*current\s*,\s*null\s*\)"), "A is the processor of the inspector, B is empty");
            Assert.True(button.Contains("\"rtux.compare.button\""), "the caption 'Compare'");
            Assert.True(Regex.IsMatch(button, @"ModUI\.Button\(\s*parent\s*,"), "a button of ModUI");
        }

        [Test]
        public void TheInspectorPatch_IsOneOptionalPostfix_UnderTheGuard()
        {
            string patches = Code("Patches/InspectorPatches.cs", false);

            Assert.Equal(1, Regex.Matches(patches, @"\[ModPatch\(").Count, "one patch (NFR-7)");
            Assert.True(Regex.IsMatch(patches, @"\[ModPatch\(typeof\(GameInspectorWindow\),\s*""Start"",\s*PatchKind\.Postfix,\s*Optional\s*=\s*true\)\]"), "a postfix on Start, optional");
            Assert.True(patches.Contains("ModHooks.Guard(ResearchTreeUxFeature.FeatureId"), "under the guard of the feature");

            string feature = Code("ResearchTreeUxFeature.cs");
            Assert.True(feature.Contains("ctx.Patch(typeof(InspectorPatches))"), "declared to the core");
        }

        [Test]
        public void TheFeature_OpensTheComparisonFromTheHub_WithTheHubClosedFirst()
        {
            string feature = Code("ResearchTreeUxFeature.cs", false);

            Assert.True(Regex.IsMatch(feature, @"ctx\.HubPage\(\s*""rtux\.hub\.title""\s*,\s*BuildHubPage\s*\)"), "the page of the feature");
            string body = BodyOf(feature, "private void OpenCompareFromHub()");
            int close = body.IndexOf("ModHub.Close()", StringComparison.Ordinal);
            int open = body.IndexOf("OpenCompare(", StringComparison.Ordinal);
            Assert.True(close >= 0 && open > close, "the hub is modal and holds the game: it closes before the window opens");
            Assert.True(body.Contains("CpuFactsReader.LatestOf("), "A is the latest processor of the player (item 9)");
            Assert.True(body.Contains("ModHooks.Guard("), "a click on the hub page is guarded");
        }

        [Test]
        public void SwitchingTheFeatureOff_TakesTheComparisonAway()
        {
            string feature = Code("ResearchTreeUxFeature.cs");

            Assert.True(Regex.IsMatch(feature, @"OnDisabled\(\)\s*\{[^}]*DestroyCompare"), "taken away in OnDisabled (FR-8 item 3)");
            Assert.True(Regex.IsMatch(feature, @"OnSceneLeaving\(\)\s*\{[^}]*DestroyCompare"), "and when the scene is left");

            string body = BodyOf(feature, "private void DestroyCompare()");
            Assert.True(body.Contains("compare.Destroy()") && body.Contains("button.Destroy()"), "the window and the button");

            string window = Code("Compare/CompareWindow.cs");
            Assert.True(Regex.IsMatch(window, @"public\s+void\s+Destroy\(\)\s*\{\s*_destroyed\s*=\s*true;"), "the window drops its references");
        }

        [Test]
        public void TheWindows_AreOnlyOpenedWhileTheFeatureIsEnabledAndThePartIsOn()
        {
            string feature = Code("ResearchTreeUxFeature.cs");
            string body = BodyOf(feature, "internal void OpenCompare(");

            Assert.True(body.Contains("ModFeatures.IsActive(FeatureId)"), "not when the feature is off");
            Assert.True(body.Contains("(Parts & UxParts.Compare) == 0"), "not when the self-check switched the part off (NFR-2)");
        }

        [Test]
        public void TheRowsOfOtherFeatures_ComeThroughTheSeam_AndAreCopiedAcrossTheBorder()
        {
            string feature = Code("ResearchTreeUxFeature.cs");
            string rows = Code("Compare/CompareRows.cs");

            Assert.True(Regex.IsMatch(feature, @"internal\s+Func<GameCpu,\s*GameCpu,\s*IReadOnlyList<CompareRow>>\s+CompareRowsProvider\s*\{\s*get;\s*set;\s*\}"), "the seam TSK-014 fills");
            string body = BodyOf(feature, "internal IReadOnlyList<CompareLine> CompareLinesFor(");
            Assert.True(body.Contains("catch (Exception"), "caught at the border");
            Assert.True(rows.Contains("public static List<CompareLine> FromApi("), "the public row becomes the model's row");
        }

        [Test]
        public void TheFormatters_AreTheGamesOwn()
        {
            string game = Code("Compare/GameFormatter.cs");

            foreach (string call in new[]
            {
                "FloatToIps(", "FloatToFrequency(", "FloatToMoney(", "FloatToPercentageString(", "CoreCountString(", "CacheString(", "FloatToString(", "FloatToIntString(",
            })
                Assert.True(game.Contains("GameStringFormatter." + call), call);
        }

        // ---- the strings ----

        [Test]
        public void EveryLabelKeyOfTheCompareRows_IsBuiltFromTheIdsOfTheModel()
        {
            // The labels are keys made from the prefix and the id (rtux.compare.row.<id>); the test of the model checks the tables, this one that the
            // window reads the label from the line and not from a list of its own.
            string window = Code("Compare/CompareWindow.cs", false);

            Assert.True(window.Contains("line.LabelKey"), "the label of a row is the key of the line");
            Assert.False(Regex.IsMatch(window, @"""rtux\.compare\.row\."), "no list of row keys in the window");
        }
    }
}
