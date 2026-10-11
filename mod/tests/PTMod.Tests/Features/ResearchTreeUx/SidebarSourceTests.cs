using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Features.ResearchTreeUx;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// The rules of TSK-021 (FR-10, AC-10.1, scenario M11) that live in the shape of the code and cannot be run without the game, read as text: the width of
    /// the side panel is changed without a patch on the game's panel, through its three private fields; every number comes from <see cref="SidebarWidthRule"/>;
    /// the factor is kept in the profile and held in the range of <c>ui.json</c>; the original size and both positions come back when the feature is switched
    /// off; a panel that does not take the width switches off this part alone. What the owner checks in the game is M11: the strip on the left edge, the
    /// width up to 1.6 times, the same width after a reload of the scene, and the shut panel completely outside the screen.
    /// </summary>
    public class SidebarSourceTests
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

        // ---- no patch, only the three fields ----

        [Test]
        public void ThePanelIsChanged_WithoutAPatchOnIt()
        {
            // Decision D12: the panel is not rebuilt and UpdateWindowVisibility is not patched; the game's own move reads the fields the mod writes.
            var violations = new List<string>();
            foreach (string path in SourceScan.Files(Path.Combine(FeatureDir(), "Patches"), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                if (code.Contains("SideWindow")) violations.Add(SourceScan.Relative(FeatureDir(), path));
            }
            Assert.True(violations.Count == 0, "a patch on the side panel:\n  " + string.Join("\n  ", violations));

            string feature = Code("ResearchTreeUxFeature.cs");
            Assert.False(feature.Contains("typeof(SideWindowPatches)") || Regex.IsMatch(feature, @"ctx\.Patch\(\s*typeof\(\w*Side\w*\)"), "the feature declares no patch of the panel");
            Assert.False(Code("Sidebar/SidebarResizer.cs", false).Contains("UpdateWindowVisibility"), "the move of the game is not called, replaced or patched: it is left to read the fields");
        }

        [Test]
        public void TheThreeFields_AreReadWithAccessTools_AndAreTheOnesTheSelfCheckLooksFor()
        {
            string resizer = Code("Sidebar/SidebarResizer.cs", false);
            string resolve = BodyOf(resizer, "private static void ResolveFields()");

            foreach (string name in new[] { "rectTransform", "visiblePosition", "hiddenPosition" })
                Assert.True(Regex.IsMatch(resolve, @"AccessTools\.Field\(\s*typeof\(GameSideWindow\)\s*,\s*""" + name + @"""\s*\)"), name);
            Assert.True(resolve.Contains("\"isOpen\""), "the flag of the game is used when it is there, the place of the panel judges when it is not");

            string rule = Code("Logic/UxParts.cs", false);
            Assert.True(rule.Contains("\"SideWindow.rectTransform\"") && rule.Contains("\"SideWindow.visiblePosition\"") && rule.Contains("\"SideWindow.hiddenPosition\""),
                "the same three members are in the table of the self-check");
        }

        [Test]
        public void NoField_ThePanelDoesNotHave_IsAssumed_TheAttachLeavesWithoutIt()
        {
            string attach = BodyOf(Code("Sidebar/SidebarResizer.cs", false), "public static SidebarResizer Attach(");

            Assert.True(Regex.IsMatch(attach, @"_rectField\s*==\s*null\s*\|\|\s*_visibleField\s*==\s*null\s*\|\|\s*_hiddenField\s*==\s*null"), "a missing field");
            Assert.True(attach.Contains("TryRead(_visibleField") && attach.Contains("TryRead(_hiddenField"), "and a field that does not hold a number");
            Assert.True(Regex.IsMatch(attach, @"return null;"), "the panel is left as the game made it");
        }

        // ---- the numbers come from the rule ----

        [Test]
        public void TheSizeAndThePositions_ComeFromTheRule_WithTheRangeOfTheFile()
        {
            string apply = BodyOf(Code("Sidebar/SidebarResizer.cs"), "private void ApplyFactor(");

            Assert.True(Regex.IsMatch(apply, @"_feature\.SidebarConfig\.Clamp\("), "the factor is held in the range of ui.json first");
            Assert.True(apply.Contains("SidebarWidthRule.Width(_originalWidth, factor)"));
            Assert.True(apply.Contains("SidebarWidthRule.VisiblePosition(_originalVisible, _originalWidth, factor, _pivot)"));
            Assert.True(apply.Contains("SidebarWidthRule.HiddenPosition(_originalHidden, _originalWidth, factor, _pivot)"));
            Assert.True(apply.Contains("SetSizeWithCurrentAnchors(UnityRectTransform.Axis.Horizontal, width / _scale)"), "the size, in the units of the panel");
            Assert.True(apply.Contains("WritePositions()") && apply.Contains("PlaceNow(open)"), "the fields of the game, then the panel itself");
        }

        [Test]
        public void ThePivotAndTheScale_AreReadFromThePanel_NotAssumed()
        {
            string ctor = BodyOf(Code("Sidebar/SidebarResizer.cs"), "private SidebarResizer(");

            Assert.True(Regex.IsMatch(ctor, @"_pivot\s*=\s*rect\.pivot\.x"));
            Assert.True(Regex.IsMatch(ctor, @"_scale\s*=\s*Math\.Abs\(rect\.localScale\.x\)"));
            Assert.True(Regex.IsMatch(ctor, @"_originalWidth\s*=\s*width\s*\*\s*_scale"), "the width in the units the positions are written in");
            Assert.True(Regex.IsMatch(ctor, @"_originalSizeDelta\s*=\s*rect\.sizeDelta"), "what to give back");
        }

        [Test]
        public void TheGamesMoveIsStopped_BeforeThePanelIsPlaced()
        {
            string place = BodyOf(Code("Sidebar/SidebarResizer.cs"), "private void PlaceNow(");

            int cancel = place.IndexOf("CancelTween()", StringComparison.Ordinal);
            int set = place.IndexOf("_rect.anchoredPosition = position", StringComparison.Ordinal);
            Assert.True(cancel >= 0 && set > cancel, "a move that runs would write the old place on top of the new one");
            Assert.True(Regex.IsMatch(place, @"position\.x\s*=\s*open\s*\?\s*_visible\s*:\s*_hidden"), "the place of the open or the shut panel");
        }

        [Test]
        public void TheTweenOfTheGame_IsStoppedInsideAGuardedMethod_SoAnotherLeanTweenCostsNothing()
        {
            string resizer = Code("Sidebar/SidebarResizer.cs");
            string cancel = BodyOf(resizer, "private void CancelTween()");

            Assert.True(Regex.IsMatch(cancel, @"try\s*\{\s*CancelLean\(_rect\);\s*\}\s*catch\s*\(\s*Exception\s*\)"), "the game's type is touched inside try");
            Assert.True(Regex.IsMatch(resizer, @"\[MethodImpl\(MethodImplOptions\.NoInlining\)\]\s*private static void CancelLean\("), "in a method of its own");
        }

        // ---- the profile ----

        [Test]
        public void TheFactor_IsKeptInTheProfile_AndAppliedAgainWhenTheSceneIsBuilt()
        {
            string resizer = Code("Sidebar/SidebarResizer.cs");

            string save = BodyOf(resizer, "private void SaveFactor()");
            Assert.True(save.Contains("prefs.SidebarWidthFactor = _factor") && save.Contains("ResearchUxPrefs.Save()"), "FR-10 item 2: written to ModProfile");
            Assert.True(Regex.IsMatch(save, @"Math\.Abs\(prefs\.SidebarWidthFactor\s*-\s*_factor\)\s*<\s*SameFactor\)\s*return"), "nothing is written when nothing changed");

            string saved = BodyOf(resizer, "private void ApplySaved()");
            Assert.True(Regex.IsMatch(saved, @"_feature\.SidebarConfig\.Clamp\(\s*ResearchUxPrefs\.Current\.SidebarWidthFactor\s*\)"), "read from the profile and held in the range");
            Assert.True(Regex.IsMatch(saved, @"IsNeutral\(saved\)\)\s*return"), "a factor of 1 leaves the panel exactly as the game made it");
            Assert.True(saved.Contains("UxWidgets.Defer(VerifyWidth)"), "the check of the width comes a frame later, after a layout has run");
        }

        [Test]
        public void TheDragIsSavedOnce_WhenItEnds_NotOnEveryFrame()
        {
            string resizer = Code("Sidebar/SidebarResizer.cs");

            Assert.False(BodyOf(resizer, "internal void Drag(").Contains("SaveFactor"), "the profile is not written while the hand moves");
            string end = BodyOf(resizer, "internal void EndDrag(");
            Assert.True(end.Contains("SaveFactor()") && end.Contains("VerifyWidth()"));
        }

        // ---- switching off gives the panel back ----

        [Test]
        public void Detach_GivesBackTheSizeAndBothPositions_AndTakesTheStripAway()
        {
            string detach = BodyOf(Code("Sidebar/SidebarResizer.cs"), "public void Detach()");

            string[] order =
            {
                "_destroyed = true", "UnityObject.Destroy(handle.gameObject)", "bool open = IsOpen()", "_rect.sizeDelta = _originalSizeDelta",
                "_visible = _originalVisible", "_hidden = _originalHidden", "WritePositions()", "PlaceNow(open)",
            };
            int last = -1;
            foreach (string step in order)
            {
                int at = detach.IndexOf(step, StringComparison.Ordinal);
                Assert.True(at > last, step + " comes after the one before it");
                last = at;
            }
            Assert.True(detach.Contains("if (_destroyed) return;"), "safe to call twice");
            Assert.True(detach.Contains("_window == null || _rect == null"), "and after the panel is gone");
        }

        [Test]
        public void TheFeature_TakesThePanelBack_WhenItIsSwitchedOff_AndWhenTheSceneIsLeft()
        {
            string feature = Code("ResearchTreeUxFeature.cs", false);

            Assert.True(Regex.IsMatch(BodyOf(feature, "public void OnDisabled()"), @"ModHooks\.Guard\(\s*FeatureId\s*,\s*""OnDisabled/sidebar""\s*,\s*DestroySidebar\s*\)"));
            Assert.True(Regex.IsMatch(BodyOf(feature, "private void OnSceneLeaving()"), @"ModHooks\.Guard\(\s*FeatureId\s*,\s*""SceneLeaving/sidebar""\s*,\s*DestroySidebar\s*\)"));

            string destroy = BodyOf(feature, "private void DestroySidebar()");
            Assert.True(Regex.IsMatch(destroy, @"_sidebar\s*=\s*null[\s\S]*Detach\(\)"), "the reference goes, the panel is given back");
        }

        [Test]
        public void TheFeature_PutsTheStripBack_WhenItIsSwitchedOn_WithoutAReloadOfTheScene()
        {
            string feature = Code("ResearchTreeUxFeature.cs", false);

            Assert.True(Regex.IsMatch(BodyOf(feature, "public void OnEnabled()"), @"ModHooks\.Guard\(\s*FeatureId\s*,\s*""OnEnabled/sidebar""\s*,\s*AttachToSideWindow\s*\)"));
            Assert.True(feature.Contains("PTModHost.SceneReady += OnSceneReady"), "and when the game scene is built");
            string scene = BodyOf(feature, "private void OnSceneReady(");
            Assert.True(scene.Contains("PTModHost.GameScene") && scene.Contains("AttachToSideWindow"), "only the game scene has the panel");

            string attach = BodyOf(feature, "private void AttachToSideWindow()");
            Assert.True(Regex.IsMatch(attach, @"\(Parts\s*&\s*UxParts\.Sidebar\)\s*==\s*0\)\s*return"), "NFR-2: only while the part is on");
            Assert.True(attach.Contains("FindFirstObjectByType<GameSideWindow>"), "the panel is looked up, shut or open");
            Assert.True(attach.Contains("_sidebar.IsAlive") && attach.Contains("ReferenceEquals(_sidebar.Window, window)"), "a panel that has the strip is left alone");
        }

        // ---- the part switches itself off ----

        [Test]
        public void APanelThatDoesNotTakeTheWidth_SwitchesOffThisPartAlone_AndIsGivenBack()
        {
            string resizer = Code("Sidebar/SidebarResizer.cs");
            string verify = BodyOf(resizer, "private void VerifyWidth()");

            Assert.True(verify.Contains("SidebarWidthRule.IsNeutral(_factor)"), "nothing to check when nothing was changed");
            Assert.True(Regex.IsMatch(verify, @"expected\s*=\s*SidebarWidthRule\.Width\(_originalWidth,\s*_factor\)"));
            Assert.True(Regex.IsMatch(verify, @"actual\s*=\s*_rect\.rect\.width\s*\*\s*_scale"), "the width the panel really has");
            Assert.True(verify.Contains("_feature.OnSidebarRejected("));

            string rejected = BodyOf(Code("ResearchTreeUxFeature.cs"), "internal void OnSidebarRejected(");
            Assert.True(Regex.IsMatch(rejected, @"Parts\s*&=\s*~UxParts\.Sidebar"), "the bit of this part, none of the others");
            Assert.True(rejected.Contains("DestroySidebar()"), "and the panel goes back to the game's size");
            Assert.True(rejected.Contains("ModLog.Warn("));
        }

        // ---- the strip ----

        [Test]
        public void TheStrip_StraddlesTheLeftEdge_AboveThePanel_AndStaysOutOfItsLayout()
        {
            string create = BodyOf(Code("Sidebar/SidebarResizer.cs"), "private bool CreateHandle()");

            Assert.True(create.Contains("SetAsLastSibling()"), "a full-size background child of the panel cannot cover the strip");
            Assert.False(create.Contains("SetAsFirstSibling()"));
            Assert.True(Regex.IsMatch(create, @"pivot\s*=\s*new UnityVector2\(HandleOutside\s*/\s*\(HandleOutside\s*\+\s*HandleInside\)"), "the pivot stands on the edge, so most of the strip is outside the panel");
            Assert.True(Regex.IsMatch(create, @"sizeDelta\s*=\s*new UnityVector2\(HandleOutside\s*\+\s*HandleInside,\s*0f\)"), "and it is a narrow band");
            Assert.True(Regex.IsMatch(create, @"ignoreLayout\s*=\s*true"), "a layout group on the panel does not take it into its rows");
            Assert.True(Regex.IsMatch(create, @"anchorMin\s*=\s*new UnityVector2\(0f,\s*0f\)") && Regex.IsMatch(create, @"anchorMax\s*=\s*new UnityVector2\(0f,\s*1f\)"),
                "the whole height of the left edge");
            Assert.True(Regex.IsMatch(create, @"raycastTarget\s*=\s*true"), "it takes the pointer");
            Assert.True(create.Contains("_rect.Find(HandleName)"), "a strip of an earlier life is not left to double up");
            Assert.True(create.Contains("catch (Exception e)") && create.Contains("DestroyImmediate(holder)"), "a strip that fails to build leaves nothing behind");
        }

        [Test]
        public void ADrag_BeginsOnlyOnAnOpenPanel_WithTheLeftButton()
        {
            string begin = BodyOf(Code("Sidebar/SidebarResizer.cs"), "internal void BeginDrag(");

            Assert.True(begin.Contains("InputButton.Left"));
            Assert.True(Regex.IsMatch(begin, @"if\s*\(\s*!IsOpen\(\)\s*\)\s*return"), "only the edge of a shut panel shows: it is not dragged");
            Assert.True(begin.Contains("CancelTween()"), "a move of the game in progress does not fight the hand");
        }

        [Test]
        public void TheDrag_IsMeasuredInTheUnitsOfTheParent_TheFrameThatDoesNotChange()
        {
            string parentX = BodyOf(Code("Sidebar/SidebarResizer.cs"), "private bool TryParentX(");

            Assert.True(parentX.Contains("_rect.parent as UnityRectTransform"));
            Assert.True(parentX.Contains("ScreenPointToLocalPointInRectangle(parent, eventData.position, eventData.pressEventCamera"));

            string drag = BodyOf(Code("Sidebar/SidebarResizer.cs"), "internal void Drag(");
            Assert.True(drag.Contains("SidebarWidthRule.FactorAfterDrag(_dragStartFactor, _originalWidth, _dragStartX - x,"), "the distance from where the hand took the edge");
        }

        [Test]
        public void ADoubleClickOnTheStrip_ReturnsTheOriginalWidth_AndASinglePressGoesOnToThePanel()
        {
            string pressed = BodyOf(Code("Sidebar/SidebarResizer.cs"), "internal void Pressed(");

            Assert.True(Regex.IsMatch(pressed, @"clickCount\s*>=\s*2"));
            Assert.True(pressed.Contains("ResetWidth()"));
            Assert.True(pressed.Contains("ExecuteHierarchy(_rect.gameObject, eventData,") && pressed.Contains("pointerDownHandler"), "the panel brings itself to the front as for every press");

            string reset = BodyOf(Code("Sidebar/SidebarResizer.cs"), "public void ResetWidth()");
            Assert.True(reset.Contains("ApplyFactor(SidebarWidthRule.NeutralFactor)") && reset.Contains("SaveFactor()"));
        }

        [Test]
        public void TheEventsOfTheStrip_CatchTheirOwnExceptions_AtTheBorder()
        {
            string code = Code("Sidebar/SidebarResizer.cs", false);
            string run = BodyOf(code, "private void Run(Action action)");

            Assert.True(Regex.IsMatch(run, @"catch\s*\(\s*Exception\s+e\s*\)\s*\{\s*ModHooks\.ReportFault\(\s*ResearchTreeUxFeature\.FeatureId,\s*Site,\s*e\s*\)"), "counted against the feature");
            foreach (string handler in new[] { "OnBeginDrag", "OnDrag", "OnEndDrag", "OnPointerDown" })
                Assert.True(Regex.IsMatch(BodyOf(code, "public void " + handler + "("), @"Run\("), handler + " goes through Run");
        }

        [Test]
        public void TheSidebar_RunsNothingEveryFrame()
        {
            // Everything is an event of the pointer: no Update, no timer, no tick (FR-8 item 4: nothing runs while the feature is off, and nothing needs to while it is on).
            foreach (string path in SourceScan.Files(Path.Combine(FeatureDir(), "Sidebar"), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                Assert.False(Regex.IsMatch(code, @"\bvoid\s+(?:Update|LateUpdate|FixedUpdate)\s*\(|\bInvokeRepeating\s*\(|\bStartCoroutine\s*\("), SourceScan.Relative(FeatureDir(), path));
            }
        }

        [Test]
        public void TheTooltipOfTheStrip_IsTheCoresBridge_ReadWhenThePointerEnters()
        {
            string resizer = Code("Sidebar/SidebarResizer.cs", false);

            Assert.True(Regex.IsMatch(resizer, @"ModUI\.Tooltip\(\s*target\s*,\s*\(\)\s*=>\s*new ModUI\.TooltipText\(string\.Empty,\s*ModL10n\.L\(""rtux\.sidebar\.handle\.tip""\)\)\)"),
                "a provider, so a change of language is followed");
            Assert.False(resizer.Contains("TooltipData") || resizer.Contains("TooltipHandler"), "no tooltip of its own");
        }

        // ---- the page in the hub ----

        [Test]
        public void ThePageInTheHub_ExplainsTheEdge_AndHasTheButtonForTheOriginalWidth()
        {
            string feature = Code("ResearchTreeUxFeature.cs", false);
            string page = BodyOf(feature, "private void BuildHubPage(");

            Assert.True(Regex.IsMatch(page, @"\(Parts\s*&\s*UxParts\.Sidebar\)\s*!=\s*0"), "only while the part is on");
            Assert.True(page.Contains("rtux.sidebar.hub.desc") && page.Contains("ResetSidebarFromHub"));
            Assert.True(BodyOf(feature, "private string PartsOffText()").Contains("UxParts.Sidebar"), "a part the self-check switched off is named");

            string reset = BodyOf(feature, "private void ResetSidebarFromHub()");
            Assert.True(reset.Contains("ModHooks.Guard(FeatureId, \"hub/sidebar\""));
            Assert.True(reset.Contains("sidebar.ResetWidth()"), "with the panel in the scene");
            Assert.True(Regex.IsMatch(reset, @"SidebarWidthFactor\s*=\s*SidebarWidthRule\.NeutralFactor[\s\S]*ResearchUxPrefs\.Save\(\)"), "and without it the choice of the profile goes back");
        }

        [Test]
        public void TheRangeComesFromTheContentFile_ThroughTheOneDoorOfTheFeature()
        {
            string feature = Code("ResearchTreeUxFeature.cs");

            Assert.True(Regex.IsMatch(BodyOf(feature, "private void LoadContent()"), @"_sidebarConfig\s*=\s*content\.Sidebar"));
            Assert.True(Regex.IsMatch(Code("Logic/UxContent.cs", false), @"content\.Sidebar\s*=\s*Section\(\s*""sidebar"""), "read by the loader of the whole file");
        }

        [Test]
        public void TheFolderOfTheSidebar_HoldsNoSecondReaderOfTheContent_AndNoWriteToTheGamesFiles()
        {
            foreach (string path in SourceScan.Files(Path.Combine(FeatureDir(), "Sidebar"), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                Assert.False(code.Contains("ModContent."), SourceScan.Relative(FeatureDir(), path) + " reads no content file");
                Assert.False(Regex.IsMatch(code, @"\bFile\.(?:Write|Create|Delete|Append)|\bSaveHandler\b"), SourceScan.Relative(FeatureDir(), path) + " writes no file of the game");
            }
        }
    }
}
