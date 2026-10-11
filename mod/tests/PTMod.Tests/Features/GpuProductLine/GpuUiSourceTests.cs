using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The game side of TSK-022...TSK-026, checked on the source (the windows cannot be built into the unit tests): the window is a <c>Panel</c> and its dialogs are
    /// <c>Modal</c> (time stands in them); the founding, the retiring, the cancelling and the stopping ask <c>ModUI.Confirm</c>; the last tab is kept with
    /// <c>ModSave.Flags</c>; there is no desktop icon; the window is refreshed once a game day and not while it is closed; the designer recomputes on the events of its
    /// controls and never on a timer or a frame; every characteristic and every node carries a tooltip of <c>ModUI.Tooltip</c>; the release dialog opens on the event of
    /// the finished project.
    /// </summary>
    public class GpuUiSourceTests
    {
        private static string UiFile(string name)
        {
            string path = Path.Combine(GpuFiles.FeatureSourceDir, "Ui", name);
            Assert.True(File.Exists(path), "no " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), true);
        }

        private static string UiFileWithStrings(string name)
        {
            string path = Path.Combine(GpuFiles.FeatureSourceDir, "Ui", name);
            Assert.True(File.Exists(path), "no " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        private static string FeatureCode()
        {
            return SourceScan.Strip(SourceScan.ReadText(Path.Combine(GpuFiles.FeatureSourceDir, "GpuFeature.cs")), false);
        }

        private static int Count(string code, string pattern)
        {
            return Regex.Matches(code, pattern).Count;
        }

        [Test]
        public void TheWindowIsAPanel_AndTheDialogsAreModal()
        {
            Assert.True(UiFile("GpuWindow.cs").Contains("WindowTemplate.Panel"), "the window \"Видеокарты\": the game goes on");
            Assert.False(UiFile("GpuWindow.cs").Contains("WindowTemplate.Modal"));
            Assert.True(UiFile("GpuFoundDialog.cs").Contains("WindowTemplate.Modal"), "the founding stops the time (NFR-5)");
            Assert.True(UiFile("GpuReleaseDialog.cs").Contains("WindowTemplate.Modal"), "the launch stops the time (AC-4.3)");
            foreach (string name in new[] { "GpuWindow.cs", "GpuFoundDialog.cs", "GpuReleaseDialog.cs" })
                Assert.True(UiFile(name).Contains("ModUI.CreateWindow("), name + " makes its window with ModUI");
        }

        [Test]
        public void TheMovesThatCannotBeUndone_AskFirst()
        {
            Assert.Equal(1, Count(UiFile("GpuFoundDialog.cs"), @"ModUI\.Confirm\("), "the founding (AC-1.1: presses and confirms)");
            Assert.True(UiFile("GpuFoundDialog.cs").Contains("feature.Division.Found()"));
            Assert.Equal(2, Count(UiFile("GpuLineupTab.cs"), @"ModUI\.Confirm\("), "retiring a card and cancelling a project");
            Assert.True(UiFile("GpuLineupTab.cs").Contains("_feature.Lineup.Retire(id)"));
            Assert.True(UiFile("GpuLineupTab.cs").Contains("_feature.Projects.Cancel(id)"));
            Assert.Equal(1, Count(UiFile("GpuResearchTab.cs"), @"ModUI\.Confirm\("), "stopping the research loses its progress");
        }

        [Test]
        public void TheLastTab_IsKeptInTheSave_WithTheFlagsOfTheCore()
        {
            string window = UiFileWithStrings("GpuWindow.cs");
            Assert.True(window.Contains("ModSave.Flags.GetInt(Feature, GpuTabs.FlagKey, 0)"), "read when the window opens");
            Assert.True(Count(window, @"ModSave\.Flags\.SetInt\(Feature, GpuTabs\.FlagKey, GpuTabs\.ToStored\(") >= 2, "written when a tab is opened and when it is chosen");
            Assert.True(UiFile("GpuWindow.cs").Contains("GpuTabs.All"), "the four tabs");
        }

        [Test]
        public void TheEntryIsTheHubOnly_NoIconOnTheDesktop()
        {
            var hits = new List<string>();
            foreach (string path in SourceScan.Files(GpuFiles.FeatureSourceDir, ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                foreach (Match match in Regex.Matches(code, @"\bButtonTaskbar\b|\bHubEntryButton\b|\bButtonMenuFallback\b|\bTaskbar\b|\bDesktopIcon\b"))
                    hits.Add(SourceScan.Relative(GpuFiles.FeatureSourceDir, path) + ":" + SourceScan.LineOf(code, match.Index) + ": " + match.Value);
            }
            Assert.True(hits.Count == 0, "FR-1 item 8: the window opens from the hub of the mods only:\n  " + string.Join("\n  ", hits));
            Assert.True(FeatureCode().Contains("GpuWindow.Open();"), "the button of the hub opens the window");
        }

        [Test]
        public void TheWindow_IsRefreshedOncePerGameDay_AndNeverWhileItIsClosed()
        {
            string feature = FeatureCode();
            Assert.True(feature.Contains("ctx.OnTick(TickPhase.DayPassed, UiDayTick, UiDayPriority);"), "one handler of the day");
            Assert.True(Regex.IsMatch(feature, @"private void UiDayTick\(\)\s*\{\s*Timed\(GpuPerfMeter\.UiDay, ""ui/day"", GpuWindow\.RefreshDaily\);\s*\}"), "under the guard of the feature, timed");
            Assert.False(feature.Contains("RunDuringFastForward"), "nothing moves while a game is fast-forwarded");

            string window = UiFile("GpuWindow.cs");
            Assert.True(Regex.IsMatch(window, @"public static void RefreshDaily\(\)\s*\{\s*if \(!IsOpen \|\| _view == null\) return;"), "a closed window costs nothing (AC-8.2)");
            Assert.True(window.Contains("private static void OnWindowClosed()"), "the view is dropped when the window closes");
        }

        [Test]
        public void TheUiHasNoFrameLoop_AndNoTimerOfItsOwn()
        {
            var hits = new List<string>();
            foreach (string path in SourceScan.Files(Path.Combine(GpuFiles.FeatureSourceDir, "Ui"), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                foreach (Match match in Regex.Matches(code, @"\b(?:Update|LateUpdate|FixedUpdate|OnGUI)\s*\(\s*\)|\bInvokeRepeating\b|\bStartCoroutine\b|\bWaitForSeconds\b|\bMonoBehaviour\b"))
                    hits.Add(SourceScan.Relative(GpuFiles.FeatureSourceDir, path) + ":" + SourceScan.LineOf(code, match.Index) + ": " + match.Value);
            }
            Assert.True(hits.Count == 0, "NFR-1: the windows work on events and on the tick of the day:\n  " + string.Join("\n  ", hits));
        }

        [Test]
        public void TheDesigner_RecomputesOnTheEventsOfItsControls_AndOnlyThere()
        {
            string tab = UiFile("GpuDesignerTab.cs");
            Assert.False(tab.Contains(".Recompute("), "the tab never asks for a recomputation: the setters of the model do it");
            Assert.Equal(1, Count(tab, @"_kept\.RefreshParts\(\)"), "the parts are read again when the tab is built, nowhere else");

            // every change of an input is made inside the handler of a control
            foreach (string call in new[] { "SelectArch", "SelectNode", "SelectMemory", "SelectBus", "SelectCooler", "SetDie", "SetClockPercent", "SetVramIndex" })
                Assert.True(Regex.IsMatch(tab, @"Timed\(\(\) => _model\." + call + @"\("), call + " is called from the event of its control");

            // the daily refresh only looks at the shape and at the button
            Match daily = Regex.Match(tab, @"public void RefreshDaily\(\)\s*\{(?<body>.*?)\n        \}", RegexOptions.Singleline);
            Assert.True(daily.Success, "RefreshDaily");
            string body = daily.Groups["body"].Value;
            Assert.True(body.Contains("PeekPartsSignature()") && body.Contains("RefreshDevelopState()"));
            Assert.False(Regex.IsMatch(body, @"\bSelect\w+\(|\bSet\w+\(|Recompute|RefreshParts"), "a day recomputes nothing");

            Assert.True(tab.Contains("Stopwatch"), "the recomputation is measured (NFR-1: a millisecond)");
        }

        [Test]
        public void EveryCharacteristicAndEveryNode_HasATooltipFromTheBridgeOfModUI()
        {
            string designer = UiFile("GpuDesignerTab.cs");
            Assert.True(designer.Contains("GpuStatLineView.Create(column, line.Label, line.Value, line.Tone, () => TipOf(key))"), "a tooltip made on hover for every line");
            string line = UiFile("GpuUi.cs");
            Assert.True(line.Contains("ModUI.Tooltip(row.gameObject, tip)"), "GpuStatLineView hands it to ModUI.Tooltip");
            Assert.True(line.Contains("ModUI.Tooltip(widget.gameObject"), "GpuUi.Tip");
            Assert.True(UiFile("GpuResearchTab.cs").Contains("GpuUi.TipLive(button"), "every node of the tree");
            Assert.False(Regex.IsMatch(line + designer, @"\bTooltipHandler\b|\bStartTrigger\b"), "no bridge of its own to the tooltips of the game");

            // every dropdown and slider of the designer has a tooltip argument
            string withStrings = UiFileWithStrings("GpuDesignerTab.cs");
            Assert.Equal(5, Count(withStrings, @"ModUI\.Dropdown\("));
            Assert.Equal(5, Count(withStrings, @"ModL10n\.L\(""gpu\.design\.(?:arch|node|memory|bus|cooler)\.tip""\)"));
            Assert.Equal(3, Count(withStrings, @"ModL10n\.L\(""gpu\.design\.(?:die|clock|vram)\.tip""\)"));
        }

        [Test]
        public void TheReleaseDialog_OpensOnTheFinishedProject_AndWaitsOneFrame()
        {
            string feature = FeatureCode();
            Assert.True(feature.Contains("Projects.ReadyToRelease += OnProjectReady;"), "the event of the service");
            Assert.True(Regex.IsMatch(feature, @"private void OnProjectReady\(string productId\)\s*\{\s*Guard\(""ui/release"", \(\) => GpuReleaseDialog\.OnReady\(productId\)\);\s*\}"));
            string dialog = UiFile("GpuReleaseDialog.cs");
            Assert.True(Regex.IsMatch(dialog, @"public static void OnReady\(string productId\)\s*\{\s*GpuUi\.Defer\(\(\) => Open\(productId\)\);\s*\}"), "after the tick that finished the project");
            Assert.True(dialog.Contains("Pending.Enqueue(productId)"), "several finished cards wait their turn");
            Assert.True(dialog.Contains("_model.Release(out reason)"), "\"Выпустить\" releases through the model");
            Assert.True(UiFile("GpuLineupTab.cs").Contains("GpuReleaseDialog.Open(id)"), "the button of the lineup for a card that waits");
        }

        [Test]
        public void TheFoundDialog_OpensInsteadOfTheWindow_UntilTheDivisionIsFounded()
        {
            string window = UiFile("GpuWindow.cs");
            Assert.True(Regex.IsMatch(window, @"if \(!feature\.Division\.IsFounded\)\s*\{\s*GpuFoundDialog\.Open\(tab\);\s*return;\s*\}"));
            string dialog = UiFile("GpuFoundDialog.cs");
            Assert.True(dialog.Contains("GpuFoundModel.Build(feature.Division, feature.Content, feature.Game, GpuUi.Text)"));
            Assert.True(dialog.Contains("GpuWindow.Open(_then)"), "the window opens after the founding");
        }

        [Test]
        public void TheDesignerAndTheTabs_StoreNothingInTheBrowserOfTheGame()
        {
            // the pure models hold no widget; the tabs hold theirs and drop them with the tab
            foreach (string name in new[] { "GpuResearchTab.cs", "GpuDesignerTab.cs", "GpuLineupTab.cs", "GpuMarketTab.cs" })
            {
                string code = UiFile(name);
                Assert.True(code.Contains(": IGpuTabView"), name + " is a tab of the window");
                Assert.True(code.Contains("public static IGpuTabView Build(RectTransform body, GpuFeature feature)"), name + " is built in the body of the window");
            }
        }
    }
}
