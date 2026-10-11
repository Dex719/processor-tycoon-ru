using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// The rules of TSK-016 (FR-8 items 3 and 4, scenario M9) that live in the shape of the code and cannot be run without the game (the sources are read as
    /// text): switching the feature off takes away everything it put on the scene and gives the tree back as the game made it, switching it on puts the interface
    /// back without a reload of the scene and from the same preferences, nothing the feature hangs on stays subscribed while it is off, and the daily work
    /// goes only through the ticks of the core, which are not called for a feature that is not enabled. What the owner checks in the game is M9 itself: every
    /// button and every line visible after the switch-off with the tab "Cache" chosen, the button "Compare" gone, and the interface back after the switch-on.
    /// </summary>
    public class LifecycleSourceTests
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

        // ---- switching off ----

        [Test]
        public void OnDisabled_TakesAwayTheViewAndTheComparison_EachUnderItsOwnGuard_AndDropsTheScene()
        {
            string body = BodyOf(Code("ResearchTreeUxFeature.cs", false), "public void OnDisabled()");

            int view = body.IndexOf("DestroyView", StringComparison.Ordinal);
            int compare = body.IndexOf("DestroyCompare", StringComparison.Ordinal);
            int sidebar = body.IndexOf("DestroySidebar", StringComparison.Ordinal);
            int forget = body.IndexOf("ForgetScene()", StringComparison.Ordinal);
            Assert.True(view > 0 && compare > view && sidebar > compare && forget > sidebar,
                "the interface, the comparison and the inspector button, the width of the side panel, then the references to the scene");
            Assert.True(Regex.IsMatch(body, @"ModHooks\.Guard\(\s*FeatureId\s*,\s*""OnDisabled/view""\s*,\s*DestroyView\s*\)"), "a failure of one step does not stop the next");
            Assert.True(Regex.IsMatch(body, @"ModHooks\.Guard\(\s*FeatureId\s*,\s*""OnDisabled/compare""\s*,\s*DestroyCompare\s*\)"));
            Assert.True(Regex.IsMatch(body, @"ModHooks\.Guard\(\s*FeatureId\s*,\s*""OnDisabled/sidebar""\s*,\s*DestroySidebar\s*\)"), "the side panel gets its own width back (FR-10 item 3)");
            Assert.True(body.Contains("PTModHost.SceneLeaving -= OnSceneLeaving"), "a switched-off feature gets no SceneLeaving");
            Assert.True(body.Contains("ModL10n.LanguageChanged -= OnLanguageChanged"), "nor the language");
        }

        [Test]
        public void TheView_GivesTheTreeBackAsTheGameMadeIt()
        {
            string view = BodyOf(Code("Tree/TreeUxView.cs"), "public void Destroy()");

            string[] order = { "DropAllPanes()", "_list.Destroy()", "_modifiers.Destroy()", "_strip.Destroy()", "_badges.Destroy()", "_filter.Reset()", "TechTooltips.Detach(", "HoverSeenTracker.Remove(" };
            int last = -1;
            foreach (string step in order)
            {
                int at = view.IndexOf(step, StringComparison.Ordinal);
                Assert.True(at > last, step + " comes after the one before it");
                last = at;
            }
            Assert.True(view.Contains("_destroyed = true"), "safe to call twice");
            Assert.True(view.Contains("ModL10n.LanguageChanged -= OnLanguageChanged") && view.Contains("ModUI.ThemeChanged -= OnThemeChanged"), "the view subscribes and unsubscribes in pairs");
        }

        [Test]
        public void TheFilter_PutsEveryButtonAndEveryLineBack_AsActive()
        {
            string reset = BodyOf(Code("Tree/TreeFilter.cs"), "public void Reset()");

            Assert.True(Regex.IsMatch(reset, @"foreach\s*\(\s*GameResearchButton\s+button\s+in\s+_tree\.Buttons\s*\)\s*\{\s*if\s*\(\s*button\s*!=\s*null\s*\)\s*SetShown\(\s*button\.gameObject\s*,\s*true\s*\)"),
                "every button, not only the ones the tab hid (AC-8.1)");
            Assert.True(Regex.IsMatch(reset, @"foreach\s*\(\s*UiImage\s+line\s+in\s+_tree\.Lines\s*\)\s*\{\s*if\s*\(\s*line\s*!=\s*null\s*\)\s*SetShown\(\s*line\.gameObject\s*,\s*true\s*\)"), "every line");
        }

        [Test]
        public void ThePagesOfOtherFeatures_AreToldTheyAreHidden_AndRemoved()
        {
            string view = Code("Tree/TreeUxView.cs");
            string drop = BodyOf(view, "private void DropPane(");

            Assert.True(drop.Contains("TabHidden(pane.Entry)"), "a provider that showed its page is told");
            Assert.True(drop.Contains("pane.Destroy()"), "and the page goes");
            Assert.True(BodyOf(view, "private void DropAllPanes()").Contains("_externalActiveId = null"), "no tab of another feature stays chosen");
        }

        [Test]
        public void TheView_LeavesNoReferenceToTheGameBehind()
        {
            string feature = Code("ResearchTreeUxFeature.cs");
            string forget = BodyOf(feature, "private void ForgetScene()");

            Assert.True(Regex.IsMatch(forget, @"_tree\s*=\s*null"), "the picture of the tree");
            Assert.True(Regex.IsMatch(forget, @"_snapshots\s*=\s*null"), "the technologies of the scene");
            Assert.True(forget.Contains("TreeFocus.Reset()"), "the controller of the view");
            Assert.True(Regex.IsMatch(BodyOf(feature, "private void DestroyView()"), @"_view\s*=\s*null"), "the view");
            Assert.True(Regex.IsMatch(BodyOf(feature, "private void DestroyCompare()"), @"_compare\s*=\s*null[\s\S]*_inspectorButton\s*=\s*null"), "the comparison and its button");
        }

        // ---- switching on ----

        [Test]
        public void OnEnabled_PutsTheInterfaceBack_StepByStep_EachUnderItsOwnGuard()
        {
            string body = BodyOf(Code("ResearchTreeUxFeature.cs", false), "public void OnEnabled()");

            string[] steps = { "ResearchUxSelfCheck.Run()", "EnsureSeenInitialized", "AttachToBuiltWindow", "AttachToInspector", "AttachToSideWindow" };
            int last = -1;
            foreach (string step in steps)
            {
                int at = body.IndexOf(step, StringComparison.Ordinal);
                Assert.True(at > last, step + " in this order");
                last = at;
            }
            Assert.Equal(5, Regex.Matches(body, @"ModHooks\.Guard\(\s*FeatureId\s*,\s*""OnEnabled/").Count, "no step may throw into the core");
            Assert.True(body.Contains("PTModHost.SceneLeaving += OnSceneLeaving"), "the scene is watched again");
        }

        [Test]
        public void TheWindowThatWasBuiltBeforeTheSwitchingOn_IsTakenUp_WithoutAReloadOfTheScene()
        {
            string feature = Code("ResearchTreeUxFeature.cs");
            string attach = BodyOf(feature, "private void AttachToBuiltWindow()");

            Assert.True(attach.Contains("FindFirstObjectByType<GameTreeWindow>"), "the window is looked up, shut or open");
            Assert.True(attach.Contains("TreeIndex.IsBuilt(window)"), "only a window the game has built");
            Assert.True(attach.Contains("OnTreeBuilt(window)"), "the same path as the patch");
            Assert.True(Regex.IsMatch(BodyOf(feature, "internal void OnTreeBuilt("), @"DestroyView\(\);[\s\S]*TreeUxView\.Build\("), "an interface that is there is taken away first, never two");
        }

        [Test]
        public void TheViewComesBack_FromThePreferences_NotFromWhatTheViewKept()
        {
            string view = Code("Tree/TreeUxView.cs");
            string create = BodyOf(view, "private void Create()");

            Assert.True(Regex.IsMatch(create, @"saved\s*=\s*ResearchUxPrefs\.Current\.LastTab"), "the last tab of the profile");
            Assert.True(Regex.IsMatch(create, @"TabSelection\.Resolve\(\s*saved\s*,\s*_strip\.TabIds\s*\)"), "and the tab it resolves to in the strip");
            Assert.True(Regex.IsMatch(create, @"SetViewMode\(\s*ResearchUxPrefs\.Current\.ViewMode\s*,\s*false\s*\)"), "the mode of the profile");
            Assert.True(create.Contains("ShowWindowState()"), "a window that is open when the feature is switched on gets its marks at once");
        }

        // ---- nothing runs while the feature is off (FR-8 item 4) ----

        [Test]
        public void TheDailyWork_IsOnlyTicksOfTheCore_DeclaredInRegister()
        {
            string feature = Code("ResearchTreeUxFeature.cs");
            string register = BodyOf(feature, "public void Register(FeatureContext ctx)");

            Assert.Equal(3, Regex.Matches(register, @"ctx\.OnTick\(").Count, "the list and the comparison, the idle lab, the new research");
            Assert.Equal(3, Regex.Matches(feature, @"\bctx\.OnTick\(").Count, "and nowhere else");

            var violations = new List<string>();
            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                foreach (Match match in Regex.Matches(code, @"\bInvokeRepeating\s*\(|\bFixedUpdate\s*\(|\bOnDayPassed\s*\+=|\bModTicks\.Subscribe\s*\("))
                    violations.Add(SourceScan.Relative(FeatureDir(), path) + ":" + SourceScan.LineOf(code, match.Index) + ": " + match.Value);
            }
            Assert.True(violations.Count == 0, "a timer of the feature's own would run while the feature is off:\n  " + string.Join("\n  ", violations));
        }

        [Test]
        public void TheDailyPostfix_AndThePatches_LeaveWhenTheFeatureIsNotEnabled()
        {
            string patches = Code("Patches/TreeWindowPatches.cs");
            string inspector = Code("Patches/InspectorPatches.cs");

            Assert.True(Regex.IsMatch(patches, @"if\s*\(\s*!ModFeatures\.IsActive\(\s*\w+\s*\)\s*\|\|\s*!ModHooks\.CanRun\("), "the postfix of the day");
            Assert.True(patches.Contains("if (!ModFeatures.IsActive(ResearchTreeUxFeature.FeatureId)) return;"), "the postfix of the build");
            Assert.True(inspector.Contains("ModFeatures.IsActive") || inspector.Contains("ModHooks.Guard"), "and the one of the inspector");
        }

        [Test]
        public void EveryEventTheFeatureSubscribesTo_IsLeftWhenItIsSwitchedOff_OrIsTheCoresToSkip()
        {
            // An event of the mod subscribed in Register lives for the session, and the core does not call the handler of a feature that is not enabled
            // (ModTicks.GameReady, ModFeatures.StateChanged, the registry's own TabsChanged under a Guard). The ones subscribed anywhere else are paired.
            var plus = new Regex(@"(?<event>\b(?:[A-Za-z_]\w*\.)+[A-Za-z_]\w*)\s*\+=\s*(?<handler>[A-Za-z_]\w*)");
            string all = string.Join("\n", SourceScan.Files(FeatureDir(), ".cs").Select(p => SourceScan.Strip(SourceScan.ReadText(p), true)));

            // A handler is a method of the feature; `x.Total += amount` is arithmetic.
            var methods = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match declared in Regex.Matches(all, @"\b(?:void|bool|int|string)\s+(?<n>[A-Za-z_]\w*)\s*\(")) methods.Add(declared.Groups["n"].Value);
            string register = BodyOf(Code("ResearchTreeUxFeature.cs"), "public void Register(FeatureContext ctx)");

            var unpaired = new List<string>();
            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                foreach (Match match in plus.Matches(code))
                {
                    if (!methods.Contains(match.Groups["handler"].Value)) continue;

                    string target = match.Groups["event"].Value;
                    string name = target.Substring(target.LastIndexOf('.') + 1);
                    bool inRegister = register.IndexOf(target + " +=", StringComparison.Ordinal) >= 0 || register.IndexOf(target + "+=", StringComparison.Ordinal) >= 0;
                    bool paired = Regex.IsMatch(all, Regex.Escape(target) + @"\s*-=");
                    bool ownEvent = path.EndsWith("TabStrip.cs", StringComparison.OrdinalIgnoreCase) || target.StartsWith("strip.", StringComparison.Ordinal);   // events of objects the feature owns
                    if (!inRegister && !paired && !ownEvent) unpaired.Add(SourceScan.Relative(FeatureDir(), path) + ": " + target + " (" + name + ")");
                }
            }

            Assert.True(unpaired.Count == 0, "subscribed and never unsubscribed:\n  " + string.Join("\n  ", unpaired));
        }

        // ---- the page in the hub ----

        [Test]
        public void ThePageInTheHub_HasTheCompareButton_TheSwitchesOfTheReminders_AndTheStateOfTheParts()
        {
            string feature = Code("ResearchTreeUxFeature.cs", false);
            string page = BodyOf(feature, "private void BuildHubPage(");

            Assert.True(Regex.IsMatch(feature, @"ctx\.HubPage\(\s*""rtux\.hub\.title""\s*,\s*BuildHubPage\s*\)"), "FR-8 item 1: the page of the feature");
            Assert.True(page.Contains("ModUI.Button(page, ModL10n.L(\"rtux.hub.compare\")"), "the button \"Compare CPUs\"");
            Assert.True(page.Contains("PartsOffText()"), "what the self-check switched off (NFR-2)");
            Assert.True(feature.Contains("Key = ParamNotifyNewTech") && feature.Contains("Key = ParamNotifyIdleLab"), "the two switches are parameters, which the hub draws");
            Assert.True(BodyOf(feature, "private string PartsOffText()").Contains("ModFeatures.IsActive(FeatureId)"), "nothing is said about a feature that is not running");
        }

        [Test]
        public void TheHubIsClosed_BeforeTheComparisonOpens()
        {
            string body = BodyOf(Code("ResearchTreeUxFeature.cs"), "private void OpenCompareFromHub()");

            int close = body.IndexOf("ModHub.Close()", StringComparison.Ordinal);
            int open = body.IndexOf("OpenCompare(", StringComparison.Ordinal);
            Assert.True(close > 0 && open > close, "the hub is modal and holds the game, the comparison does not");
        }
    }
}
