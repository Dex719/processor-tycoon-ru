using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// The rules of TSK-014 that live in the shape of the code and cannot be run without the game (the sources are read as text): the registry is
    /// installed behind <c>ResearchTreeUxApi</c> in <c>Register</c>, every call into a provider of another feature goes through the guarded call that
    /// reports on the feature owning the provider, the parts of the feature ask the registry through the seams, the strip is made again when a tab is
    /// registered or a feature is switched, and the files of <c>Api/</c> stay free of the rest of the feature (a feature that depends on this one is built
    /// with <c>Api/</c> only). What the owner checks in the game is the tab of <c>cpu-architectures</c> after "Cores", the page of the debug tab, and
    /// the debug flag that makes it throw. The books themselves are tried by <see cref="ApiRegistryTests"/>.
    /// </summary>
    public class ApiWiringSourceTests
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

        // ---- installed in Register ----

        [Test]
        public void TheRegistry_IsInstalledBehindTheApi_InRegister_AndTheSeamsAreItsAnswers()
        {
            string feature = Code("ResearchTreeUxFeature.cs");
            string register = BodyOf(feature, "public void Register(FeatureContext ctx)");

            Assert.True(Regex.IsMatch(register, @"_api\s*=\s*new\s+ResearchTreeUxRegistry\(\s*this\s*\)"), "the registry is made");
            Assert.True(Regex.IsMatch(register, @"ResearchTreeUxApi\.Install\(\s*_api\s*\)"), "and installed: the registrations made before are handed over");
            Assert.True(Regex.IsMatch(register, @"TooltipLineProvider\s*=\s*technology\s*=>\s*_api\.TooltipLines\(\s*technology\s*\)"), "tooltip lines");
            Assert.True(Regex.IsMatch(register, @"LabBusyTextProvider\s*=\s*_api\.LabBusyText\s*;"), "the lab for the list");
            Assert.True(Regex.IsMatch(register, @"ModifierGroupsProvider\s*=\s*company\s*=>\s*_api\.ModifierGroups\(\s*company\s*\)"), "blocks of the modifiers panel");
            Assert.True(Regex.IsMatch(register, @"CompareRowsProvider\s*=\s*\(\s*a\s*,\s*b\s*\)\s*=>\s*_api\.CompareRows\(\s*a\s*,\s*b\s*\)"), "rows of the compare table");
            Assert.True(Regex.IsMatch(register, @"_api\.TabsChanged\s*\+=\s*OnTabsRegistered"), "a tab registered later makes the strip again");
            Assert.True(Regex.IsMatch(register, @"ModFeatures\.StateChanged\s*\+=\s*OnFeatureStateChanged"), "a feature switched makes the strip again (FR-7 item 5)");
        }

        [Test]
        public void TheRegistry_IsInstalledBeforeAnyPatchOrTickIsDeclared()
        {
            string register = BodyOf(Code("ResearchTreeUxFeature.cs"), "public void Register(FeatureContext ctx)");

            int install = register.IndexOf("ResearchTreeUxApi.Install(", StringComparison.Ordinal);
            int patch = register.IndexOf("ctx.Patch(", StringComparison.Ordinal);
            Assert.True(install > 0 && patch > install, "other features may register at any moment; nothing they hand over is lost");
        }

        [Test]
        public void TheDebugTab_IsRegisteredOnlyInTheDebugBuild_ThroughTheApiLikeAnyOtherTab()
        {
            string raw = SourceScan.ReadText(Path.Combine(FeatureDir(), "ResearchTreeUxFeature.cs"));
            string tab = SourceScan.ReadText(Path.Combine(FeatureDir(), "Debug", "DebugTab.cs"));

            Assert.True(Regex.IsMatch(raw, @"#if\s+PTMOD_DEBUG\s+ResearchTreeUxApi\.RegisterTab\(\s*new\s+DebugTab\(\)\s*\);[^#]*#endif"), "behind PTMOD_DEBUG, through the public API");
            Assert.True(tab.TrimStart().StartsWith("#if PTMOD_DEBUG", StringComparison.Ordinal), "the whole file is debug only");
            Assert.True(tab.TrimEnd().EndsWith("#endif", StringComparison.Ordinal), "and closed at the end");
            Assert.True(Regex.IsMatch(tab, @"Order\s*\{\s*get\s*\{\s*return\s+100;"), "after \"Cores\" (70) and before the architectures (200)");
            Assert.True(tab.Contains("ModSettings.Debug(ThrowFlag)"), "the flag that makes it throw (AC-7.2 by hand)");
        }

        // ---- the calls into providers ----

        [Test]
        public void EveryCallIntoAProvider_GoesThroughTheGuardedCall()
        {
            string registry = Code("Registry/ResearchTreeUxRegistry.cs");
            var members = new Regex(@"\.(?:IsAvailable|BuildContent|OnShown|OnHidden|BadgeCount|GetRows|GetGroup|GetLines|IsLabBusy|BusyText)\(");

            int found = 0;
            var offenders = new List<string>();
            foreach (string line in registry.Split('\n'))
            {
                if (!members.IsMatch(line)) continue;
                found++;
                // The call stands in a lambda handed to ProviderCalls.Run / LabOccupancy.Poll (the only places that catch the exception).
                if (!Regex.IsMatch(line, @"\b(?:p|s)\s*=>"))
                    offenders.Add(line.Trim());
            }

            Assert.True(found >= 9, "found the calls of the providers: " + found);
            Assert.True(offenders.Count == 0, "a call into a provider outside the guarded call:\n  " + string.Join("\n  ", offenders));
        }

        [Test]
        public void AFailure_IsCountedAgainstTheOwnerOfTheProvider_NotAgainstThisFeature()
        {
            string registry = Code("Registry/ResearchTreeUxRegistry.cs", false);

            Assert.True(Regex.IsMatch(registry, @"ModFeatures\.FeatureOfType\(\s*provider\.GetType\(\)\s*\)"), "the owner is the feature of the type of the provider (D11)");
            Assert.True(Regex.IsMatch(registry, @"ModHooks\.ReportFault\(\s*owner\s*,\s*site\s*,\s*error\s*\)"), "reported with the owner");
            Assert.False(Regex.IsMatch(registry, @"ModHooks\.ReportFault\(\s*FeatureId"), "never on research-tree-ux for the mistake of another feature");
            Assert.False(Regex.IsMatch(registry, @"ModHooks\.Guard\(\s*FeatureId[^;]*\.(?:GetRows|GetGroup|GetLines|IsLabBusy|BusyText|BuildContent)"),
                "a Guard on this feature would let a stranger's mistake make it Faulted");
        }

        [Test]
        public void TheProvidersOfASwitchedOffFeature_AreNotAsked()
        {
            string registry = Code("Registry/ResearchTreeUxRegistry.cs", false);

            Assert.True(Regex.IsMatch(registry, @"OwnerActive\s*=\s*owner\s*=>\s*ModFeatures\.IsActive\(\s*owner\s*\)"), "the test of the owner");
            foreach (string kind in new[] { "_tabs", "_compareRows", "_modifierLines", "_tooltipLines" })
                Assert.True(Regex.IsMatch(registry, kind + @"\.Collect\(\s*OwnerActive\s*,"), kind + " is collected with the test of the owner");
            Assert.True(registry.Contains("LabOccupancy.Poll(_labSources, OwnerActive,"), "so is the lab");
        }

        [Test]
        public void IsActive_IsTheStateOfTheFeature_AndARegistrationDoesNotDependOnIt()
        {
            string registry = Code("Registry/ResearchTreeUxRegistry.cs", false);

            Assert.True(Regex.IsMatch(registry, @"public\s+bool\s+IsActive\s*\{\s*get\s*\{\s*return\s+ModFeatures\.IsActive\(\s*FeatureId\s*\)\s*;"), "FR-7 item 6");
            foreach (string method in new[] { "public void RegisterTab(", "private void AddKeyless<T>(" })
            {
                string body = BodyOf(registry, method);
                Assert.False(body.Contains("IsActive"), method + " records the registration whatever the state of the feature is");
            }
        }

        [Test]
        public void ADuplicateTabId_IsRefusedWithAWarning_AndAFailureOfTheTabPropertiesIsReportedOnTheOwner()
        {
            string body = BodyOf(Code("Registry/ResearchTreeUxRegistry.cs", false), "public void RegisterTab(");

            Assert.True(Regex.IsMatch(body, @"case\s+AddOutcome\.KeyTaken\s*:\s*ModLog\.Warn\("), "AC: a duplicate is rejected with a warning");
            Assert.True(Regex.IsMatch(body, @"catch\s*\(\s*Exception\s+e\s*\)\s*\{\s*ReportFault\(\s*owner"), "TabId, TitleKey or Order that throw are the provider's fault");
            Assert.True(body.Contains("RaiseTabsChanged()"), "a new tab is made known to the strip");
        }

        // ---- the parts ask the registry ----

        [Test]
        public void TheStrip_IsMadeOfTheBuiltInTabsAndTheTabsOfOtherFeatures()
        {
            string view = Code("Tree/TreeUxView.cs");
            string feature = Code("ResearchTreeUxFeature.cs");

            Assert.True(view.Contains("TabStrip.Build(parent, _feature.TabsInStrip(), withList)"), "built from the merged list");
            Assert.True(Regex.IsMatch(BodyOf(view, "private void SyncTabs()"), @"_strip\.SetTabs\(\s*_feature\.TabsInStrip\(\)\s*\)"), "made again from it");
            Assert.True(Regex.IsMatch(feature, @"TabMerge\.Combine\(\s*Tabs\s*,\s*api\.ExternalTabs\(\)\s*\)"), "the merge is the pure TabMerge");
            Assert.True(Regex.IsMatch(BodyOf(feature, "private void OnTabsRegistered()"), @"view\.OnProvidersChanged\(\)"), "a registration");
            Assert.True(Regex.IsMatch(BodyOf(feature, "private void OnFeatureStateChanged("), @"view\.OnProvidersChanged\(\)"), "a feature switched");
        }

        [Test]
        public void TheSwitchOfThisFeatureItself_BuildsAndTakesAwayTheInterface_NotTheStateHandler()
        {
            string body = BodyOf(Code("ResearchTreeUxFeature.cs"), "private void OnFeatureStateChanged(");

            Assert.True(Regex.IsMatch(body, @"string\.Equals\(\s*featureId\s*,\s*FeatureId\s*,\s*StringComparison\.Ordinal\s*\)\s*\)\s*return"),
                "OnEnabled and OnDisabled do that");
        }

        [Test]
        public void ThePageOfATab_IsBuiltOnce_WhenTheWindowIsOpen_AndTheProviderIsToldWhenItIsShownAndHidden()
        {
            string view = Code("Tree/TreeUxView.cs");

            Assert.Equal(1, Regex.Matches(view, @"\.BuildTabContent\(").Count);
            Assert.True(BodyOf(view, "private ExternalTabPane EnsurePane(").Contains("BuildTabContent("), "only where the page is made, once per scene");
            Assert.True(Regex.IsMatch(BodyOf(view, "private void ApplyExternal()"), @"!_strip\.IsVisible\)\s*return"), "not while the window is shut");
            Assert.True(BodyOf(view, "private void ApplyExternal()").Contains("TabShown("), "OnShown");
            Assert.True(BodyOf(view, "private void TellHidden(").Contains("TabHidden("), "OnHidden");
            Assert.True(Regex.IsMatch(BodyOf(view, "private void OnHidden()"), @"TellHidden\(\s*_externalActiveId\s*\)"), "the window closing counts as hiding");
        }

        [Test]
        public void AProviderThatFails_TakesItsTabOutOfTheStrip_AndTheViewGoesBack()
        {
            string view = Code("Tree/TreeUxView.cs");

            Assert.True(Regex.IsMatch(BodyOf(view, "private void ApplyExternal()"), @"pane\s*==\s*null\)\s*\{\s*SyncTabs\(\)"), "BuildContent failed");
            Assert.True(Regex.IsMatch(BodyOf(view, "private void TellHidden("), @"TabHidden\([^)]*\)\)\s*SyncTabs\(\)"), "OnHidden failed");
            Assert.True(BodyOf(view, "private void RefreshExternalBadges()").Contains("entry.Disabled"), "BadgeCount failed");
            Assert.True(Regex.IsMatch(BodyOf(view, "private void SyncTabs()"), @"SelectTab\(\s*back\s*,\s*false\s*\)"), "the last built-in tab comes back; nothing is written to the profile");
        }

        [Test]
        public void TheSavedTab_OfAnotherFeature_ComesBackWhenThatFeatureShowsItAfterTheStripWasMade()
        {
            string view = Code("Tree/TreeUxView.cs");

            Assert.True(Regex.IsMatch(BodyOf(view, "private void Create()"), @"_restoreTabId\s*=\s*saved"), "remembered when the saved tab is not in the strip yet");
            Assert.True(Regex.IsMatch(BodyOf(view, "private void SyncTabs()"), @"_strip\.Contains\(\s*_restoreTabId\s*\)[\s\S]*SelectTab\(\s*restore\s*,\s*false\s*\)"),
                "chosen without being written back, once the tab is there");
            Assert.True(Regex.IsMatch(BodyOf(view, "private void SelectTab("), @"if\s*\(\s*byUser\s*\)\s*_restoreTabId\s*=\s*null"), "a choice of the player ends the wait");
        }

        [Test]
        public void TheCountersOfTheTabs_ComeWithTheMarks()
        {
            string view = Code("Tree/TreeUxView.cs");
            string refresh = BodyOf(view, "public void RefreshNew()");

            int badges = refresh.IndexOf("RefreshExternalBadges()", StringComparison.Ordinal);
            int reader = refresh.IndexOf("_reader == null", StringComparison.Ordinal);
            Assert.True(badges > 0 && reader > badges, "the counters of other features need no reader of the technologies");
            Assert.True(BodyOf(view, "private void RefreshExternalBadges()").Contains("_strip.SetBadge("), "drawn on the caption");
        }

        [Test]
        public void ThePageOfATab_IsOpaque_BelowTheStrip_AndFillsItsPanel()
        {
            string pane = Code("Tree/ExternalTabPane.cs");

            Assert.True(Regex.IsMatch(pane, @"offsetMax\s*=\s*new\s+UnityVector2\(\s*0f\s*,\s*-TabStrip\.StripHeight\s*\)"), "below the strip");
            Assert.True(Regex.IsMatch(pane, @"background\.raycastTarget\s*=\s*true"), "taking the pointer");
            Assert.True(Regex.IsMatch(pane, @"childForceExpandHeight\s*=\s*true"), "the root of the provider fills the page");
            Assert.True(Regex.IsMatch(pane, @"public\s+void\s+Destroy\(\)"), "and goes away");
            Assert.False(Regex.IsMatch(pane, @"\b_tree\b|\bTreeIndex\b|\bTreeFilter\b"), "the tree is not touched");
        }

        [Test]
        public void TheLabSources_AreAskedByTheListAndByTheReminder()
        {
            string feature = Code("ResearchTreeUxFeature.cs");
            string notifier = Code("Notify/ResearchUxNotifier.cs");
            string registry = Code("Registry/ResearchTreeUxRegistry.cs");

            Assert.True(Regex.IsMatch(registry, @"public\s+string\s+LabBusyText\(\)"), "the text for the list");
            Assert.True(Regex.IsMatch(registry, @"public\s+bool\s+LabIsBusy\(\)"), "the yes or no for the reminder");
            Assert.True(Regex.IsMatch(registry, @"PollLab\(\s*false\s*\)"), "the reminder asks for no text");
            Assert.True(Regex.IsMatch(feature, @"internal\s+bool\s+LabIsBusy\(\)"), "the feature hands it on");
            Assert.True(notifier.Contains("_feature.LabIsBusy()"), "the notifier asks it");
        }

        [Test]
        public void OpenTab_ClosesTheHub_ThenOpensTheWindowOnTheTab_OnlyForATabThatIsShown()
        {
            string body = BodyOf(Code("ResearchTreeUxFeature.cs"), "internal void OpenTab(string tabId)");

            Assert.True(body.Contains("ModFeatures.IsActive(FeatureId)"), "nothing while the feature is off");
            int has = body.IndexOf("view.HasTab(tabId)", StringComparison.Ordinal);
            int close = body.IndexOf("ModHub.Close()", StringComparison.Ordinal);
            int open = body.IndexOf("view.OpenWindowOnTab(tabId)", StringComparison.Ordinal);
            Assert.True(has > 0 && close > has && open > close, "the tab exists, then the modal hub goes, then the window opens");
            Assert.True(body.Contains("ModHooks.Guard("), "under a guard");
        }

        // ---- the public API stays what it was, and stays free of the rest of the feature ----

        [Test]
        public void TheApiFolder_UsesNothingOfTheRestOfTheFeature()
        {
            // A feature that depends on research-tree-ux is built with Api/ of it and the core, nothing else (build_feature.py), so a file of Api/ that names a type
            // of the registry, the view or the pure logic would break the build of that feature.
            string api = Path.Combine(FeatureDir(), "Api");
            var inside = new HashSet<string>(StringComparer.Ordinal);
            var declaration = new Regex(@"\b(?:class|struct|enum|interface)\s+(?<n>[A-Z]\w*)");

            foreach (string path in SourceScan.Files(api, ".cs"))
            {
                foreach (Match match in declaration.Matches(SourceScan.Strip(SourceScan.ReadText(path), true))) inside.Add(match.Groups["n"].Value);
            }

            var outside = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
            {
                if (path.Replace('\\', '/').Contains("/Api/")) continue;
                foreach (Match match in declaration.Matches(SourceScan.Strip(SourceScan.ReadText(path), true)))
                {
                    string name = match.Groups["n"].Value;
                    if (!inside.Contains(name)) outside.Add(name);
                }
            }

            Assert.True(outside.Count > 30 && inside.Count >= 8, "found the types: " + outside.Count + " outside, " + inside.Count + " inside");

            var violations = new List<string>();
            foreach (string path in SourceScan.Files(api, ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                foreach (string name in outside)
                {
                    if (Regex.IsMatch(code, @"(?<![\w.])" + Regex.Escape(name) + @"(?![\w])"))
                        violations.Add(SourceScan.Relative(api, path) + " names " + name);
                }
            }

            Assert.True(violations.Count == 0, "Api/ must stand on the core and on itself:\n  " + string.Join("\n  ", violations));
        }

        [Test]
        public void TheContractOfTheApi_IsKept()
        {
            string contract = Code("Api/IResearchTreeUxApi.cs");

            foreach (string member in new[] { "bool IsActive", "void RegisterTab(IResearchTabProvider", "void RegisterCompareRows(ICompareRowProvider", "void RegisterModifierLines(IModifierLineSource",
                "void RegisterTechTooltipLines(ITechTooltipLineSource", "void RegisterLabOccupancy(ILabOccupancySource", "void OpenTab(string tabId" })
                Assert.True(contract.Contains(member), "IResearchTreeUxApi: " + member);

            string facade = Code("Api/ResearchTreeUxApi.cs");
            foreach (string member in new[] { "public static bool IsActive", "public static void RegisterTab(", "public static void RegisterCompareRows(", "public static void RegisterModifierLines(",
                "public static void RegisterTechTooltipLines(", "public static void RegisterLabOccupancy(", "public static void OpenTab(", "internal static void Install(IResearchTreeUxApi" })
                Assert.True(facade.Contains(member), "ResearchTreeUxApi: " + member);
        }

        [Test]
        public void TheRegistry_ImplementsTheContract_AndIsNotPartOfTheApiFolder()
        {
            string path = Path.Combine(FeatureDir(), "Registry", "ResearchTreeUxRegistry.cs");
            string code = Code("Registry/ResearchTreeUxRegistry.cs");

            Assert.True(File.Exists(path));
            Assert.True(Regex.IsMatch(code, @"internal\s+sealed\s+class\s+ResearchTreeUxRegistry\s*:\s*IResearchTreeUxApi"), "the implementation of the contract");
            foreach (string member in new[] { "public void RegisterTab(", "public void RegisterCompareRows(", "public void RegisterModifierLines(", "public void RegisterTechTooltipLines(",
                "public void RegisterLabOccupancy(", "public void OpenTab(" })
                Assert.True(code.Contains(member), member);
        }
    }
}
