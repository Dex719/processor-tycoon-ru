using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// The rules of TSK-004...TSK-007 that live in the shape of the code and cannot be run without the game (the sources are read as text):
    /// the tooltips go through <c>ModUI.Tooltip</c> and the feature has no tooltip data, no trigger and no bridge of its own (TSK-006), the
    /// patches touch only the methods of the design and the postfix of every day leaves first when the game fast-forwards (NFR-1), the tabs
    /// only hide and show (D1: the layout of the game is never moved), and the view puts its interface away. The parts that need the game
    /// (the look of the strip, the position of the view, the marks on the buttons) are for the owner's check.
    /// </summary>
    public class TabsAndTooltipsSourceTests
    {
        private const string Pascal = "ResearchTreeUx";

        // The game's tooltip machinery: the bridge to it is the core's (decision D4), the feature never names it.
        private static readonly Regex TooltipMachinery = new Regex(
            @"\b(?:TooltipData|TooltipHandler|TooltipTrigger|StartTrigger|CancelTrigger|DelayedTooltip|CurrentTooltip)\b", RegexOptions.Compiled);

        private static readonly Regex PatchDeclaration = new Regex(
            @"\[ModPatch\(\s*typeof\((?<type>\w+)\)\s*,\s*""(?<method>\w+)""", RegexOptions.Compiled);

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

        // ---- tooltips (TSK-006) ----

        [Test]
        public void TheFeature_HasNoTooltipDataTriggerOrBridgeOfItsOwn()
        {
            var violations = new List<string>();
            int files = 0;
            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
            {
                files++;
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                foreach (Match match in TooltipMachinery.Matches(code))
                    violations.Add(SourceScan.Relative(FeatureDir(), path) + ":" + SourceScan.LineOf(code, match.Index) + ": " + match.Value);
            }

            Assert.True(files > 10, "too few files in " + FeatureDir());
            Assert.True(violations.Count == 0,
                "the bridge to the game's tooltips is ModUI.Tooltip of the core (TSK-006, decision D4):\n  " + string.Join("\n  ", violations));
        }

        [Test]
        public void TheScanOfTheTooltipMachinery_RecognisesTheForbiddenNames()
        {
            Assert.True(TooltipMachinery.IsMatch("ScriptableObject.CreateInstance<TooltipData>();"));
            Assert.True(TooltipMachinery.IsMatch("TooltipHandler.Instance.StartTrigger(data, 0, null);"));
            Assert.False(TooltipMachinery.IsMatch("ModUI.Tooltip(target, provider);"));
            Assert.False(TooltipMachinery.IsMatch("TechTooltips.Attach(button, provider);"));
        }

        [Test]
        public void TechTooltips_GoesThroughModUiTooltip()
        {
            string code = Code("Tooltips/TechTooltips.cs");

            Assert.True(Regex.IsMatch(code, @"ModUI\.Tooltip\(\s*target\s*,\s*\(\)\s*=>"), "Attach hangs a provider on the object through ModUI.Tooltip");
            Assert.True(Regex.IsMatch(code, @"public\s+static\s+void\s+Attach\(\s*\w+\s+target\s*,\s*Func<TipText>\s+provider\s*\)"), "Attach(target, provider)");
            Assert.True(Regex.IsMatch(code, @"public\s+static\s+void\s+Detach\(\s*\w+\s+target\s*\)"), "Detach(target)");
        }

        [Test]
        public void TheSeenTimer_DoesNotTouchTheTooltip()
        {
            string code = Code("Tooltips/HoverSeenTracker.cs");

            Assert.False(code.Contains("ModUI.Tooltip"), "HoverSeenTracker leaves the tooltip alone (design: Component 6)");
            Assert.True(code.Contains("HoverSeenTimer"), "the rule is the pure timer");
            Assert.True(Regex.IsMatch(code, @"IPointerEnterHandler"), "enter");
            Assert.True(Regex.IsMatch(code, @"IPointerExitHandler"), "exit");
            Assert.True(code.Contains("unscaledDeltaTime"), "real time: a paused game counts as well");
        }

        [Test]
        public void TheTooltipIsBuiltWhenTheCursorComes_NotWhenTheButtonsAreMade()
        {
            string view = Code("Tree/TreeUxView.cs");

            Assert.True(Regex.IsMatch(view, @"TechTooltips\.Attach\(\s*button\.gameObject\s*,\s*\(\)\s*=>\s*BuildTip\(\s*id\s*\)\s*\)"),
                "the provider is a lambda that builds the text on the hover");
            Assert.True(view.Contains("TechTooltipBuilder.Build("), "the text is made by the pure builder");
        }

        // ---- the patches (D1, NFR-1, NFR-7) ----

        [Test]
        public void ThePatches_TouchOnlyTheMethodsOfTheDesign()
        {
            var allowed = new HashSet<string>
            {
                "GameTreeWindow.InitializeUI", "GameTreeWindow.UpdateButtonStates",
                "GameInspectorWindow.Start",
            };
            var found = new List<string>();
            foreach (string path in SourceScan.Files(Path.Combine(FeatureDir(), "Patches"), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                foreach (Match match in PatchDeclaration.Matches(code)) found.Add(match.Groups["type"].Value + "." + match.Groups["method"].Value);
            }

            Assert.True(found.Contains("GameTreeWindow.InitializeUI"), "InitializeUI is patched");
            Assert.True(found.Contains("GameTreeWindow.UpdateButtonStates"), "UpdateButtonStates is patched (TSK-005)");
            foreach (string target in found) Assert.True(allowed.Contains(target), "a patch outside the design: " + target);
            Assert.Equal(found.Count, found.Distinct().Count(), "one patch per method (NFR-7)");
        }

        [Test]
        public void TheDailyPostfix_LeavesFirstWhenTheGameFastForwards()
        {
            string code = Code("Patches/TreeWindowPatches.cs");
            int method = code.IndexOf("UpdateButtonStatesPostfix", System.StringComparison.Ordinal);
            Assert.True(method > 0, "the postfix of UpdateButtonStates");

            int open = code.IndexOf('{', method);
            string body = code.Substring(open + 1);
            string firstStatement = body.Substring(0, body.IndexOf(';')).Trim();

            Assert.True(Regex.IsMatch(firstStatement, @"^if\s*\(\s*ModTicks\.IsFastForwarding\s*\)\s*return$"),
                "the first statement is the check of the fast-forward (NFR-1), got: " + firstStatement);
        }

        [Test]
        public void TheDailyPostfix_CatchesWhatThrows_AtTheBorderOfTheHook()
        {
            string code = Code("Patches/TreeWindowPatches.cs");

            Assert.True(Regex.IsMatch(code, @"catch\s*\(\s*Exception\s+\w+\s*\)\s*\{\s*ModHooks\.ReportFault\("), "the exception goes to the core's counter");
            Assert.True(code.Contains("ModHooks.CanRun("), "a Faulted feature does not run");
        }

        // ---- the tabs only hide and show (D1) ----

        [Test]
        public void TheTabs_NeverMoveOrResizeAnObjectOfTheTree()
        {
            var violations = new List<string>();
            var forbidden = new Regex(
                @"\.(?:anchoredPosition|anchoredPosition3D|localPosition|localScale|sizeDelta|SetParent|SetSiblingIndex|SetAsFirstSibling|SetAsLastSibling|Destroy|DestroyImmediate)\b",
                RegexOptions.Compiled);

            // The filter is the part that changes what the tree shows: it only switches objects on and off.
            string code = Code("Tree/TreeFilter.cs");
            foreach (Match match in forbidden.Matches(code))
                violations.Add("TreeFilter.cs:" + SourceScan.LineOf(code, match.Index) + ": " + match.Value);

            Assert.True(violations.Count == 0, "decision D1: a tab hides and shows, the layout of the game stays:\n  " + string.Join("\n  ", violations));
            Assert.True(Regex.IsMatch(code, @"\.SetActive\("), "it does switch objects");
        }

        [Test]
        public void TheFocus_MovesOnlyTheRectangleTheGameMovesOnADrag()
        {
            string code = Code("Tree/TreeFocus.cs", false);

            Assert.True(Regex.IsMatch(code, @"""controller"""), "ResearchTreeController.controller");
            Assert.Equal(1, Regex.Matches(code, @"\.anchoredPosition\s*\+=").Count, "one move, of that rectangle");
            Assert.False(Regex.IsMatch(code, @"\.localScale\s*="), "the zoom is the player's");
        }

        [Test]
        public void TheView_PutsItsInterfaceAway()
        {
            string code = Code("Tree/TreeUxView.cs");
            int destroy = code.IndexOf("public void Destroy()", System.StringComparison.Ordinal);
            Assert.True(destroy > 0, "Destroy exists");
            string body = code.Substring(destroy);

            foreach (string part in new[] { "_strip.Destroy()", "_badges.Destroy()", "_filter.Reset()", "TechTooltips.Detach(", "HoverSeenTracker.Remove(", "ModL10n.LanguageChanged -=", "ModUI.ThemeChanged -=" })
                Assert.True(body.Contains(part), "Destroy does: " + part);
        }

        [Test]
        public void TheFeature_BuildsTheViewAfterTheTreeAndTakesItAwayWhenSwitchedOffOrWhenTheSceneIsLeft()
        {
            string code = Code("ResearchTreeUxFeature.cs");

            Assert.True(code.Contains("TreeUxView.Build("), "built in OnTreeBuilt");
            Assert.True(Regex.IsMatch(code, @"OnDisabled\(\)\s*\{[^}]*DestroyView"), "taken away in OnDisabled (FR-8 item 3)");
            Assert.True(Regex.IsMatch(code, @"OnSceneLeaving\(\)\s*\{[^}]*DestroyView"), "taken away when the scene is left");
            Assert.True(code.Contains("ModTicks.GameReady += OnGameReady"), "ModTicks.GameReady, not a subscription to the game (NFR-8)");
        }
    }
}
