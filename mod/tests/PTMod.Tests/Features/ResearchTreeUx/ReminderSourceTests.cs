using System;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// The rules of TSK-015 that live in the shape of the code and cannot be run without the game (the sources are read as text): the reminders are driven by
    /// the day and month ticks of the core (which keep them out of the fast-forward, the loading and a switched-off feature), the switches are read at
    /// every call, the pop-up is <c>ModNotify</c>'s (which holds a note back during the batch run of a month), a lab busy with another feature's research is
    /// not idle, and the reminder about new research needs the marks. What the owner checks in the game is AC-9.1 (the note on the eighth idle day, none for
    /// thirty days), AC-9.2 and the note held back in <c>NextMonth</c>. The rules themselves are tried by <see cref="NotifierPolicyTests"/>.
    /// </summary>
    public class ReminderSourceTests
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

        // ---- the ticks of the core (FR-9 item 3, FR-8 item 4) ----

        [Test]
        public void TheReminders_AreTicksOfTheCore_WithTheDefaultGuards()
        {
            string feature = Code("ResearchTreeUxFeature.cs");

            Assert.True(Regex.IsMatch(feature, @"ctx\.OnTick\(\s*TickPhase\.DayPassed\s*,\s*OnDayReminders\s*\)"), "the idle lab, once a day");
            Assert.True(Regex.IsMatch(feature, @"ctx\.OnTick\(\s*TickPhase\.MonthPassed\s*,\s*OnMonthReminders\s*\)"), "new research, once a month");

            // The options RunDuringFastForward and RunDuringLoading are what would let a reminder out of the fast-forward of a new game.
            foreach (string file in new[] { "ResearchTreeUxFeature.cs", "Notify/ResearchUxNotifier.cs" })
            {
                string code = Code(file);
                Assert.False(code.Contains("TickOptions"), file + " keeps the defaults of the core: no tick in the fast-forward or under the loading screen");
                Assert.False(code.Contains("ModTicks.Subscribe("), file + " subscribes through FeatureContext.OnTick only");
            }
        }

        [Test]
        public void TheNote_IsShownByModNotify_NotByThePopupOfTheGame()
        {
            string notifier = Code("Notify/ResearchUxNotifier.cs", false);

            Assert.True(Regex.IsMatch(notifier, @"ModNotify\.ShowKey\(\s*ResearchTreeUxFeature\.FeatureId\s*,\s*""rtux\.notify\.idle""\s*,\s*_state\.IdleDays\s*\)"), "the idle lab");
            Assert.True(Regex.IsMatch(notifier, @"ModNotify\.ShowKey\(\s*ResearchTreeUxFeature\.FeatureId\s*,\s*""rtux\.notify\.newTech""\s*,\s*unseen\s*\)"), "new research");
            Assert.False(Regex.IsMatch(notifier, @"\bPopup\w*|\bInstantiateGenericNotification\b"), "the pop-up, its limits and its waiting during NextMonth are the core's (FR-9 item 3)");
        }

        // ---- the switches (FR-9 item 4) ----

        [Test]
        public void TheSwitches_AreReadAtEveryCall_AndAReminderThatIsOff_LeavesAtOnce()
        {
            string notifier = Code("Notify/ResearchUxNotifier.cs");

            string day = BodyOf(notifier, "public void OnDay()");
            Assert.True(Regex.IsMatch(day, @"if\s*\(\s*!ResearchTreeUxFeature\.NotifyIdleLab\s*\)\s*\{\s*_state\.IdleDays\s*=\s*0;\s*return;"), "the days do not count while the switch is off");

            string month = BodyOf(notifier, "public void OnMonth()");
            Assert.True(Regex.IsMatch(month, @"if\s*\(\s*!ResearchTreeUxFeature\.NotifyNewTech\s*\)\s*return;"), "no note of new research when the switch is off");

            string feature = Code("ResearchTreeUxFeature.cs", false);
            Assert.True(Regex.IsMatch(feature, @"ModSettings\.GetBool\(\s*FeatureId\s*,\s*ParamNotifyNewTech\s*\)"), "read from the settings of the core each time");
            Assert.True(Regex.IsMatch(feature, @"ModSettings\.GetBool\(\s*FeatureId\s*,\s*ParamNotifyIdleLab\s*\)"));
            Assert.Equal(2, Regex.Matches(feature, @"Type\s*=\s*ParamType\.Bool").Count, "the two switches of the hub (ParamSpec)");
        }

        // ---- the lab (FR-7 item 7, AC-9.2) ----

        [Test]
        public void ALabBusyWithAnotherFeature_IsAskedOnlyOnADayWithoutOwnResearch()
        {
            string day = BodyOf(Code("Notify/ResearchUxNotifier.cs"), "public void OnDay()");

            Assert.True(Regex.IsMatch(day, @"busyBySource\s*=\s*!hasCurrent\s*&&\s*_feature\.LabIsBusy\(\)"), "a day with a research costs no call into another feature");
            Assert.True(Regex.IsMatch(day, @"ShouldNotifyIdle\(\s*_state\s*,\s*hasCurrent\s*,\s*busyBySource\s*,\s*available\s*,\s*_feature\.NotifyConfig\s*\)"), "and the policy settles it");
        }

        [Test]
        public void TheLabIsRead_WithOneCallWhileAResearchRuns()
        {
            string body = BodyOf(Code("Tree/TechSnapshotReader.cs"), "public bool ReadLab(");

            int current = body.IndexOf("sector.CurrentResearch()", StringComparison.Ordinal);
            int early = body.IndexOf("if (hasCurrent) return true;", StringComparison.Ordinal);
            int refresh = body.IndexOf("RefreshStates()", StringComparison.Ordinal);
            Assert.True(current > 0 && early > current && refresh > early, "the pass over the technologies is only for the days the lab is idle (NFR-1: 0.1 ms)");
            Assert.True(body.Contains("IsAvailable"), "what can be started is what the list and the marks call available");
        }

        // ---- new research (FR-9 item 1) ----

        [Test]
        public void TheNoteAboutNewResearch_NeedsTheMarksAndASeenSet_AndKeepsOutOfAnOpenWindow()
        {
            string month = BodyOf(Code("Notify/ResearchUxNotifier.cs"), "public void OnMonth()");

            Assert.True(Regex.IsMatch(month, @"\(\s*_feature\.Parts\s*&\s*UxParts\.Tabs\s*\)\s*==\s*0\)\s*return;"), "nothing else ever marks a technology as seen");
            Assert.True(month.Contains("save.NeedsInitialization"), "before the set of the seen is known every technology would be new");
            Assert.True(Regex.IsMatch(month, @"ShouldNotifyNew\(\s*_state\s*,\s*month\s*,\s*unseen\s*,\s*_feature\.IsTreeWindowOpen\s*,\s*_feature\.NotifyConfig\s*\)"), "the window is open: no note");
            Assert.True(month.Contains("ModTicks.Today"), "the month is the month of the game");
        }

        [Test]
        public void TheCountsStartAfresh_WithAGame_AScene_AndASwitching()
        {
            string feature = Code("ResearchTreeUxFeature.cs");

            Assert.True(Regex.IsMatch(BodyOf(feature, "private void OnGameReady("), @"_notifier\.Reset\(\)"), "a new game or a loaded one");
            Assert.True(Regex.IsMatch(BodyOf(feature, "private void ForgetScene()"), @"_notifier\.Reset\(\)"), "a scene left, the feature switched off");
            Assert.True(Regex.IsMatch(BodyOf(feature, "public void OnEnabled()"), @"_notifier\.Reset\(\)"), "switched on again");
        }

        [Test]
        public void TheNotifier_HoldsNoObjectOfTheGame()
        {
            string notifier = Code("Notify/ResearchUxNotifier.cs");

            Assert.False(Regex.IsMatch(notifier, @"\bUnityEngine\b|\bGameObject\b|\bProcessorTycoon\b"), "numbers and snapshots only: nothing survives a scene");
            Assert.False(Regex.IsMatch(notifier, @"\bPlayer\b"), "the sector is read by the snapshot reader");
        }
    }
}
