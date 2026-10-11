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
    /// The measurements of NFR-1 (TSK-018): the budgets as numbers (a tab 5 ms, the list 3 ms, a quiet game day 0.1 ms, a tooltip and the modifiers
    /// report 1 ms), the arithmetic of a session (the first call is a warm-up, a sample over the limit is told once, a sample the budget does not
    /// apply to is only counted), the sum of the parts of one game day, and the places in the code that take the measurements. The clock and the log
    /// are the adapter's (<c>PerfProbe</c>) and are not run here; the owner reads the lines in <c>Player.log</c> of a debug session.
    /// </summary>
    [Covers(typeof(PerfOp), typeof(PerfVerdict), typeof(PerfBudget), typeof(PerfStats), typeof(PerfDaySum))]
    public class PerfBudgetTests
    {
        private const string Pascal = "ResearchTreeUx";

        // ---- the budgets ----

        [Test]
        public void TheBudgets_AreTheNumbersOfNfr1()
        {
            Assert.Near(5.0, PerfBudget.LimitMs(PerfOp.TabSwitch));
            Assert.Near(3.0, PerfBudget.LimitMs(PerfOp.ListRefresh));
            Assert.Near(0.1, PerfBudget.LimitMs(PerfOp.DayTick));
            Assert.Near(1.0, PerfBudget.LimitMs(PerfOp.Tooltip));
            Assert.Near(1.0, PerfBudget.LimitMs(PerfOp.ModifiersReport));
            Assert.Near(0.1, PerfBudget.LimitMs(PerfOp.ButtonStates), 1e-9, "a shut window: the postfix does no more than a look at the window");
        }

        [Test]
        public void TheRedrawsWithoutABudget_AreUnlimited()
        {
            foreach (PerfOp op in new[] { PerfOp.ModifiersPanel, PerfOp.CompareTable })
            {
                Assert.False(PerfBudget.HasLimit(op), op.ToString());
                Assert.False(PerfBudget.IsOver(op, 1e9), op + " is never over");
                Assert.Equal("none", PerfBudget.LimitText(op));
            }
            foreach (PerfOp op in new[] { PerfOp.TabSwitch, PerfOp.ListRefresh, PerfOp.DayTick, PerfOp.Tooltip, PerfOp.ModifiersReport, PerfOp.ButtonStates })
                Assert.True(PerfBudget.HasLimit(op), op.ToString());
        }

        [Test]
        public void IsOver_IsStrictlyAboveTheLimit()
        {
            Assert.False(PerfBudget.IsOver(PerfOp.TabSwitch, 5.0), "exactly the limit is within it");
            Assert.True(PerfBudget.IsOver(PerfOp.TabSwitch, 5.01));
            Assert.False(PerfBudget.IsOver(PerfOp.DayTick, 0.1));
            Assert.True(PerfBudget.IsOver(PerfOp.DayTick, 0.11));
        }

        [Test]
        public void EveryOperation_HasAName_AndNamesDiffer()
        {
            var names = new HashSet<string>();
            foreach (PerfOp op in Enum.GetValues(typeof(PerfOp)))
            {
                string name = PerfBudget.Name(op);
                Assert.True(!string.IsNullOrEmpty(name) && name != op.ToString(), "a name for " + op);
                Assert.True(names.Add(name), "a name used twice: " + name);
            }
        }

        // ---- the lines ----

        [Test]
        public void TheLine_SaysTheTimeTheLimitAndTheDetail()
        {
            Assert.Equal("perf: tab switch 0.42 ms (limit 5 ms) tab memory applied", PerfBudget.Line(PerfOp.TabSwitch, 0.4184, PerfVerdict.Ok, "tab memory applied"));
            Assert.Equal("perf: day tick 0.03 ms (limit 0.1 ms)", PerfBudget.Line(PerfOp.DayTick, 0.03, PerfVerdict.Ok, null));
            Assert.Equal("perf: compare table 1.50 ms (limit none) x", PerfBudget.Line(PerfOp.CompareTable, 1.5, PerfVerdict.Ok, "x"));
        }

        [Test]
        public void TheLine_OfASampleOverTheLimit_SaysSo()
        {
            string line = PerfBudget.Line(PerfOp.ListRefresh, 7.31, PerfVerdict.OverBudget, "list: 12 rows");
            Assert.Equal("perf: list refresh 7.31 ms is over the limit of 3 ms list: 12 rows", line);
        }

        [Test]
        public void TheLine_OfTheFirstCallAndOfAnUnjudgedSample_SaysWhy()
        {
            Assert.True(PerfBudget.Line(PerfOp.Tooltip, 31.2, PerfVerdict.WarmUp, null).Contains("first call, not judged"));
            Assert.True(PerfBudget.Line(PerfOp.DayTick, 2.0, PerfVerdict.NotJudged, null).Contains("a window is open, not judged"));
        }

        [Test]
        public void TheLine_UsesAPointWhateverTheCulture()
        {
            var old = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ru-RU");
                Assert.Equal("1.25", PerfBudget.Ms(1.25));
                Assert.True(PerfBudget.Line(PerfOp.DayTick, 0.05, PerfVerdict.Ok, null).Contains("0.05 ms (limit 0.1 ms)"));
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = old;
            }
        }

        // ---- the statistics ----

        [Test]
        public void TheFirstSample_IsAWarmUp_AndNotJudged()
        {
            var stats = new PerfStats();

            Assert.Equal(PerfVerdict.WarmUp, stats.Record(PerfOp.TabSwitch, 400.0), "the first call is the JIT, not the tab switch");
            Assert.Equal(0, stats.Count(PerfOp.TabSwitch));
            Assert.Equal(0, stats.OverCount(PerfOp.TabSwitch));
            Assert.Near(400.0, stats.WarmUpMs(PerfOp.TabSwitch).Value);

            Assert.Equal(PerfVerdict.Ok, stats.Record(PerfOp.TabSwitch, 0.5));
            Assert.Equal(1, stats.Count(PerfOp.TabSwitch));
        }

        [Test]
        public void TheWarmUp_IsPerOperation()
        {
            var stats = new PerfStats();
            stats.Record(PerfOp.TabSwitch, 1.0);

            Assert.Equal(PerfVerdict.WarmUp, stats.Record(PerfOp.ListRefresh, 1.0), "the list has not run yet");
            Assert.Equal(PerfVerdict.Ok, stats.Record(PerfOp.TabSwitch, 1.0));
            Assert.Null(stats.WarmUpMs(PerfOp.Tooltip), "an operation that has not run has no first call");
        }

        [Test]
        public void ASampleOverTheLimit_IsOverBudget_AndCounted()
        {
            var stats = new PerfStats();
            stats.Record(PerfOp.ListRefresh, 1.0);   // warm-up

            Assert.Equal(PerfVerdict.Ok, stats.Record(PerfOp.ListRefresh, 2.9));
            Assert.Equal(PerfVerdict.Ok, stats.Record(PerfOp.ListRefresh, 3.0));
            Assert.Equal(PerfVerdict.OverBudget, stats.Record(PerfOp.ListRefresh, 3.5));
            Assert.Equal(PerfVerdict.OverBudget, stats.Record(PerfOp.ListRefresh, 9.0));

            Assert.Equal(4, stats.Count(PerfOp.ListRefresh));
            Assert.Equal(2, stats.OverCount(PerfOp.ListRefresh));
            Assert.Near(9.0, stats.MaxMs(PerfOp.ListRefresh));
            Assert.Near((2.9 + 3.0 + 3.5 + 9.0) / 4.0, stats.AverageMs(PerfOp.ListRefresh), 1e-9);
        }

        [Test]
        public void AnOperationWithoutALimit_IsNeverOver()
        {
            var stats = new PerfStats();
            stats.Record(PerfOp.CompareTable, 1.0);

            Assert.Equal(PerfVerdict.Ok, stats.Record(PerfOp.CompareTable, 5000.0));
            Assert.Equal(0, stats.OverCount(PerfOp.CompareTable));
            Assert.Near(5000.0, stats.MaxMs(PerfOp.CompareTable));
        }

        [Test]
        public void AnUnjudgedSample_IsOnlyCounted_ItNeverSpoilsTheNumbers()
        {
            var stats = new PerfStats();
            stats.Record(PerfOp.DayTick, 0.01);   // warm-up

            Assert.Equal(PerfVerdict.NotJudged, stats.Record(PerfOp.DayTick, 3.0, false), "the day the window was open");
            Assert.Equal(PerfVerdict.Ok, stats.Record(PerfOp.DayTick, 0.02));

            Assert.Equal(1, stats.Count(PerfOp.DayTick));
            Assert.Equal(1, stats.NotJudgedCount(PerfOp.DayTick));
            Assert.Equal(0, stats.OverCount(PerfOp.DayTick));
            Assert.Near(0.02, stats.MaxMs(PerfOp.DayTick), 1e-9, "the 3 ms of the open window is not in the slowest");
        }

        [Test]
        public void AnUnjudgedSample_DoesNotUseUpTheWarmUp()
        {
            var stats = new PerfStats();

            Assert.Equal(PerfVerdict.NotJudged, stats.Record(PerfOp.DayTick, 3.0, false));
            Assert.Equal(PerfVerdict.WarmUp, stats.Record(PerfOp.DayTick, 0.5), "the first judged sample is still the first call");
        }

        [Test]
        public void NonsenseTimes_CountAsZero()
        {
            var stats = new PerfStats();
            stats.Record(PerfOp.Tooltip, 0.1);

            Assert.Equal(PerfVerdict.Ok, stats.Record(PerfOp.Tooltip, -5.0));
            Assert.Equal(PerfVerdict.Ok, stats.Record(PerfOp.Tooltip, double.NaN));
            Assert.Equal(PerfVerdict.Ok, stats.Record(PerfOp.Tooltip, double.PositiveInfinity));

            Assert.Equal(3, stats.Count(PerfOp.Tooltip));
            Assert.Near(0.0, stats.MaxMs(PerfOp.Tooltip));
        }

        [Test]
        public void AnOperationOutsideTheEnum_IsIgnored()
        {
            var stats = new PerfStats();

            Assert.Equal(PerfVerdict.Ok, stats.Record((PerfOp)99, 1.0));
            Assert.Equal(0, stats.Count((PerfOp)99));
            Assert.Near(0.0, stats.AverageMs((PerfOp)(-1)));
            Assert.False(stats.HasSamples);
        }

        [Test]
        public void Reset_ForgetsEverything()
        {
            var stats = new PerfStats();
            stats.Record(PerfOp.TabSwitch, 1.0);
            stats.Record(PerfOp.TabSwitch, 9.0);
            stats.Record(PerfOp.DayTick, 1.0, false);
            Assert.True(stats.HasSamples);

            stats.Reset();

            Assert.False(stats.HasSamples);
            Assert.Equal(0, stats.Count(PerfOp.TabSwitch));
            Assert.Equal(0, stats.OverCount(PerfOp.TabSwitch));
            Assert.Equal(0, stats.NotJudgedCount(PerfOp.DayTick));
            Assert.Equal(PerfVerdict.WarmUp, stats.Record(PerfOp.TabSwitch, 1.0), "the next session starts with its own first call");
        }

        [Test]
        public void TheSummary_HasOneLinePerOperationThatRan_InOrder()
        {
            var stats = new PerfStats();
            Assert.Equal(0, stats.SummaryLines().Count);

            stats.Record(PerfOp.ListRefresh, 6.0);
            stats.Record(PerfOp.ListRefresh, 1.0);
            stats.Record(PerfOp.ListRefresh, 4.0);
            stats.Record(PerfOp.TabSwitch, 2.0);
            stats.Record(PerfOp.TabSwitch, 1.0);
            stats.Record(PerfOp.DayTick, 5.0, false);

            IReadOnlyList<string> lines = stats.SummaryLines();

            Assert.Equal(3, lines.Count, string.Join("\n", lines));
            Assert.True(lines[0].StartsWith("perf summary: tab switch: 1 sample(s), average 1.00 ms, slowest 1.00 ms, first call 2.00 ms, limit 5 ms"), lines[0]);
            Assert.True(lines[1].StartsWith("perf summary: list refresh: 2 sample(s), average 2.50 ms, slowest 4.00 ms, first call 6.00 ms, limit 3 ms"), lines[1]);
            Assert.True(lines[1].Contains("OVER the limit 1 time(s)"), lines[1]);
            Assert.True(lines[2].Contains("day tick: 0 sample(s)") && lines[2].Contains("1 with a window open not judged"), lines[2]);
        }

        // ---- the sum of a game day ----

        [Test]
        public void TheParts_OfOneDay_AreAddedUp()
        {
            var day = new PerfDaySum();
            double done;
            bool judged;

            Assert.False(day.Add(100, 0.03, true, out done, out judged), "nothing finished yet");
            Assert.False(day.Add(100, 0.04, true, out done, out judged), "the second handler of the same day");

            Assert.True(day.Flush(out done, out judged));
            Assert.Near(0.07, done, 1e-9);
            Assert.True(judged);
        }

        [Test]
        public void TheNextDay_FinishesTheDayInHand()
        {
            var day = new PerfDaySum();
            double done;
            bool judged;
            day.Add(100, 0.03, true, out done, out judged);
            day.Add(100, 0.04, true, out done, out judged);

            Assert.True(day.Add(101, 0.05, true, out done, out judged), "a part of day 101 ends day 100");
            Assert.Near(0.07, done, 1e-9);
            Assert.True(judged);

            Assert.True(day.Flush(out done, out judged));
            Assert.Near(0.05, done, 1e-9, "and starts day 101 with itself");
        }

        [Test]
        public void ADayCountsAsJudged_OnlyIfEveryPartWas()
        {
            var day = new PerfDaySum();
            double done;
            bool judged;
            day.Add(7, 0.01, true, out done, out judged);
            day.Add(7, 3.0, false, out done, out judged);   // the list redrew: a window was open
            day.Add(7, 0.01, true, out done, out judged);

            Assert.True(day.Flush(out done, out judged));
            Assert.False(judged);
            Assert.Near(3.02, done, 1e-9);
        }

        [Test]
        public void TheNextDayStartsJudgedAgain()
        {
            var day = new PerfDaySum();
            double done;
            bool judged;
            day.Add(7, 3.0, false, out done, out judged);

            Assert.True(day.Add(8, 0.01, true, out done, out judged));
            Assert.False(judged, "day 7 was not judged");

            day.Flush(out done, out judged);
            Assert.True(judged, "day 8 was");
        }

        [Test]
        public void Flush_WithNothingInHand_SaysSo_AndTwiceIsEmpty()
        {
            var day = new PerfDaySum();
            double done;
            bool judged;
            Assert.False(day.Flush(out done, out judged));

            day.Add(1, 0.5, true, out done, out judged);
            Assert.True(day.Flush(out done, out judged));
            Assert.False(day.Flush(out done, out judged), "the day was taken");
        }

        [Test]
        public void ANonsenseTimePart_CountsAsZero()
        {
            var day = new PerfDaySum();
            double done;
            bool judged;
            day.Add(1, double.NaN, true, out done, out judged);
            day.Add(1, -2.0, true, out done, out judged);
            day.Add(1, 0.02, true, out done, out judged);

            day.Flush(out done, out judged);
            Assert.Near(0.02, done, 1e-9);
        }

        // ---- where the code measures ----

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

        [Test]
        public void EveryOperationWithABudget_IsMeasuredWhereItRuns()
        {
            // TSK-018: TabStrip.Select (the choice of a tab, in the view), ResearchListView.Refresh, the postfix of UpdateButtonStates, the tooltip, plus the
            // day tick and the report of the modifiers.
            string view = Code("Tree/TreeUxView.cs");
            Assert.True(Regex.IsMatch(BodyOf(view, "private void SelectTab("), @"PerfProbe\.Begin\(\)[\s\S]*PerfProbe\.End\(\s*PerfOp\.TabSwitch\b"), "a tab switch is timed");
            Assert.True(Regex.IsMatch(BodyOf(view, "private TipText BuildTip("), @"PerfProbe\.Begin\(\)[\s\S]*PerfProbe\.End\(\s*PerfOp\.Tooltip\b"), "the text of a tooltip is timed");

            string list = Code("List/ResearchListView.cs");
            Assert.True(Regex.IsMatch(BodyOf(list, "public void Refresh()"), @"PerfProbe\.Begin\(\)[\s\S]*PerfProbe\.End\(\s*PerfOp\.ListRefresh\b"), "a redraw of the list is timed");

            string feature = Code("ResearchTreeUxFeature.cs");
            Assert.True(Regex.IsMatch(BodyOf(feature, "internal void OnButtonStatesUpdated("), @"PerfProbe\.Begin\(\)[\s\S]*PerfProbe\.End\(\s*PerfOp\.ButtonStates\b"),
                "the postfix of UpdateButtonStates is timed");
            Assert.True(Regex.IsMatch(BodyOf(feature, "private void OnDay()"), @"PerfProbe\.Begin\(\)[\s\S]*PerfProbe\.EndDayPart\("), "the tick of the day is timed");
            Assert.True(Regex.IsMatch(BodyOf(feature, "private void OnDayReminders()"), @"PerfProbe\.Begin\(\)[\s\S]*PerfProbe\.EndDayPart\("), "and the reminders of the day");

            string reports = Code("Modifiers/ModifierReports.cs");
            Assert.True(Regex.IsMatch(BodyOf(reports, "public static ModifierReport Build("), @"PerfProbe\.Begin\(\)[\s\S]*PerfProbe\.End\(\s*PerfOp\.ModifiersReport\b"), "the report is timed");
        }

        [Test]
        public void TheMeasuring_BuildsNoStringUnlessASessionIsVerbose()
        {
            // The day tick and the postfix run every day for the whole game: the line is built only after `started != 0L`, which is false unless
            // ModSettings.VerboseLog is on.
            string probe = Code("Tree/PerfProbe.cs");
            Assert.True(Regex.IsMatch(BodyOf(probe, "public static long Begin()"), @"return\s+Enabled\s*\?\s*Stopwatch\.GetTimestamp\(\)\s*:\s*0L"), "Begin gives 0 without a verbose log");
            Assert.True(Regex.IsMatch(BodyOf(probe, "public static bool Enabled"), @"ModSettings\.VerboseLog"), "the switch is the log's own");
            Assert.True(Regex.IsMatch(BodyOf(probe, "public static double End("), @"if\s*\(\s*started\s*==\s*0L\s*\)\s*return"), "End does nothing for 0");
            Assert.True(Regex.IsMatch(BodyOf(probe, "public static void EndDayPart("), @"if\s*\(\s*started\s*==\s*0L\s*\)\s*return"), "nor does EndDayPart");

            string feature = Code("ResearchTreeUxFeature.cs");
            foreach (string method in new[] { "private void OnDay()", "private void OnDayReminders()", "internal void OnButtonStatesUpdated(" })
            {
                string body = BodyOf(feature, method);
                Assert.True(body.Contains("started != 0L"), method + " ends the measurement only when one was begun");
                Assert.False(Regex.IsMatch(body, @"\bStopwatch\b"), method + " does not read the clock itself");
            }
        }

        [Test]
        public void OnlyTheProbe_ReadsTheClock()
        {
            // One place takes the time, so a change of the unit or of the log cannot leave a stale copy behind.
            var users = new List<string>();
            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                if (Regex.IsMatch(code, @"\bStopwatch\b")) users.Add(SourceScan.Relative(FeatureDir(), path));
            }
            Assert.Equal(new[] { "Tree/PerfProbe.cs" }, users.ToArray());
        }

        [Test]
        public void TheDailyPostfix_StillLeavesFirstOnTheFastForward()
        {
            // NFR-1: nothing, not even the probe, runs before the early exit of a new game's fast-forward.
            string code = Code("Patches/TreeWindowPatches.cs");
            string body = BodyOf(code, "private static void UpdateButtonStatesPostfix(");

            Assert.True(Regex.IsMatch(body.TrimStart('{').TrimStart(), @"^if\s*\(\s*ModTicks\.IsFastForwarding\s*\)\s*return;"), "the first statement is the exit");
            Assert.False(body.Contains("PerfProbe"), "the postfix itself holds no probe: it is in the feature, after the checks");
        }

        [Test]
        public void TheSummary_IsWrittenWhenTheInterfaceIsTakenAway()
        {
            string view = Code("Tree/TreeUxView.cs");
            Assert.True(BodyOf(view, "public void Destroy()").Contains("PerfProbe.ReportAndReset()"), "the numbers of the session reach the log at the end");
        }
    }
}
