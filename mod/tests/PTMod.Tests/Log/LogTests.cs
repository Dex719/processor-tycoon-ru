using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using PTMod.Core;
using PTMod.Core.Logic.Log;

namespace PTMod.Tests.Log
{
    /// <summary>
    /// ModLog logic (FR-7, TSK-011): line format, repeat key, 3-per-60-seconds suppression with a summary after
    /// the window, ring buffer of 50 problems. Time is injected as plain numbers, nothing waits.
    /// </summary>
    [Covers(typeof(LogFormatter), typeof(RepeatLimiter), typeof(RepeatSummary), typeof(RepeatDecision),
        typeof(ProblemBuffer), typeof(LogEntry), typeof(LogLevel))]
    public class LogTests
    {
        // ---- LogFormatter ----

        [Test]
        public void Format_HeaderAndMessage()
        {
            Assert.Equal("[PTMod] [event-system] WARN Event 'x' has no effects",
                LogFormatter.Format("event-system", LogLevel.Warn, "Event 'x' has no effects"));
            Assert.Equal("[PTMod] [market-shocks] INFO started", LogFormatter.Format("market-shocks", LogLevel.Info, "started"));
            Assert.Equal("[PTMod] [mod-framework] ERROR boom", LogFormatter.Format("mod-framework", LogLevel.Error, "boom"));
            Assert.Equal("[PTMod] [mod-framework] DEBUG details", LogFormatter.Format("mod-framework", LogLevel.Debug, "details"));
        }

        [Test]
        public void Format_EmptyMessageKeepsOnlyHeader()
        {
            Assert.Equal("[PTMod] [a] INFO", LogFormatter.Format("a", LogLevel.Info, null));
            Assert.Equal("[PTMod] [a] INFO", LogFormatter.Format("a", LogLevel.Info, string.Empty));
        }

        [Test]
        public void Format_BlankFeatureBecomesFramework()
        {
            Assert.Equal("[PTMod] [mod-framework] INFO hi", LogFormatter.Format(null, LogLevel.Info, "hi"));
            Assert.Equal("[PTMod] [mod-framework] INFO hi", LogFormatter.Format("   ", LogLevel.Info, "hi"));
            Assert.Equal("mod-framework", LogFormatter.CoreFeatureId);
            Assert.Equal("[PTMod]", LogFormatter.Prefix);
        }

        [Test]
        public void NormalizeFeatureId_StripsCharactersThatBreakTheFormat()
        {
            Assert.Equal("a b c", LogFormatter.NormalizeFeatureId("a[b]c"));
            Assert.Equal("a b", LogFormatter.NormalizeFeatureId("a\nb"));
            Assert.Equal("event-system", LogFormatter.NormalizeFeatureId("  event-system  "));
            Assert.Equal("event-system", LogFormatter.NormalizeFeatureId("event-system"));
        }

        [Test]
        public void LevelName_AllLevels()
        {
            Assert.Equal("DEBUG", LogFormatter.LevelName(LogLevel.Debug));
            Assert.Equal("INFO", LogFormatter.LevelName(LogLevel.Info));
            Assert.Equal("WARN", LogFormatter.LevelName(LogLevel.Warn));
            Assert.Equal("ERROR", LogFormatter.LevelName(LogLevel.Error));
            Assert.Equal("UNKNOWN", LogFormatter.LevelName((LogLevel)42));
        }

        [Test]
        public void Format_WithException()
        {
            var inner = new InvalidOperationException("inner text");
            var outer = new ArgumentException("outer text", inner);

            string line = LogFormatter.Format("f", LogLevel.Error, "Tick failed", outer, false);
            Assert.Equal("[PTMod] [f] ERROR Tick failed: System.ArgumentException: outer text <- System.InvalidOperationException: inner text", line);

            string noMessage = LogFormatter.Format("f", LogLevel.Error, null, inner, false);
            Assert.Equal("[PTMod] [f] ERROR System.InvalidOperationException: inner text", noMessage);
        }

        [Test]
        public void Format_ExceptionStackTraceOnlyWhenAsked()
        {
            Exception thrown = null;
            try
            {
                throw new InvalidOperationException("with trace");
            }
            catch (Exception ex)
            {
                thrown = ex;
            }

            string without = LogFormatter.Format("f", LogLevel.Error, "m", thrown, false);
            string with = LogFormatter.Format("f", LogLevel.Error, "m", thrown, true);
            Assert.False(without.Contains("\n"), "single line by default");
            Assert.True(with.StartsWith(without + "\n"), "stack trace follows on new lines");
            Assert.True(with.Contains("Format_ExceptionStackTraceOnlyWhenAsked"), "the trace names the throwing method");

            // An exception that was never thrown has no trace; nothing is appended.
            Assert.Equal(without.Replace("with trace", "fresh"),
                LogFormatter.Format("f", LogLevel.Error, "m", new InvalidOperationException("fresh"), true));
        }

        [Test]
        public void DescribeException_LimitsChainDepth()
        {
            Exception e = new Exception("e0");
            for (int i = 1; i <= 8; i++) e = new Exception("e" + i, e);

            string text = LogFormatter.DescribeException(e);
            Assert.Equal(4, text.Split(new[] { " <- " }, StringSplitOptions.None).Length, "outer exception and three inner");
            Assert.True(text.StartsWith("System.Exception: e8"));
            Assert.Equal(string.Empty, LogFormatter.DescribeException(null));
        }

        [Test]
        public void CollapseDigits_ReplacesRunsWithOneHash()
        {
            Assert.Equal("tick # failed", LogFormatter.CollapseDigits("tick 5 failed"));
            Assert.Equal("tick # failed", LogFormatter.CollapseDigits("tick 1042 failed"));
            Assert.Equal("#.# of #", LogFormatter.CollapseDigits("12.50 of 100"));
            Assert.Equal("company ai:# lost $#", LogFormatter.CollapseDigits("company ai:7 lost $300000"));
            Assert.Equal("#", LogFormatter.CollapseDigits("0"));
            Assert.Equal("a#b#c#", LogFormatter.CollapseDigits("a1b22c333"));
        }

        [Test]
        public void CollapseDigits_WithoutDigitsReturnsSameText()
        {
            string text = "no numbers here";
            Assert.True(ReferenceEquals(text, LogFormatter.CollapseDigits(text)), "no copy when there is nothing to replace");
            Assert.Equal(string.Empty, LogFormatter.CollapseDigits(null));
            Assert.Equal(string.Empty, LogFormatter.CollapseDigits(string.Empty));
        }

        [Test]
        public void RepeatKey_FeaturePlusTextWithoutNumbers()
        {
            Assert.Equal(LogFormatter.RepeatKey("f", "tick 1 failed"), LogFormatter.RepeatKey("f", "tick 99 failed"));
            Assert.NotEqual(LogFormatter.RepeatKey("f", "tick 1 failed"), LogFormatter.RepeatKey("g", "tick 1 failed"));
            Assert.NotEqual(LogFormatter.RepeatKey("f", "tick 1 failed"), LogFormatter.RepeatKey("f", "tick 1 passed"));
            Assert.Equal(LogFormatter.RepeatKey(null, "x"), LogFormatter.RepeatKey("mod-framework", "x"));
        }

        [Test]
        public void FormatSummary_UsesPhraseFromTheSpec()
        {
            string line = LogFormatter.FormatSummary("f", LogLevel.Warn, "Slow frame", 597);
            Assert.Equal("[PTMod] [f] WARN Slow frame (повторено 597 раз)", line);

            var summary = new RepeatSummary("f", LogLevel.Warn, "Slow frame", 12, 0, 60);
            Assert.Equal("[PTMod] [f] WARN Slow frame (повторено 12 раз)", LogFormatter.FormatSummary(summary));
            Assert.Throws<ArgumentNullException>(() => LogFormatter.FormatSummary(null));
        }

        [Test]
        public void RepeatedPhrase_UsesTheRussianPluralOfRaz()
        {
            // AC-2.2 says "повторено 4 раза", AC-7.1 "повторено N раз": one wording for every summary the mod writes.
            Assert.Equal("повторено 4 раза", LogFormatter.RepeatedPhrase(4));
            Assert.Equal("повторено 2 раза", LogFormatter.RepeatedPhrase(2));
            Assert.Equal("повторено 3 раза", LogFormatter.RepeatedPhrase(3));
            Assert.Equal("повторено 22 раза", LogFormatter.RepeatedPhrase(22));
            Assert.Equal("повторено 104 раза", LogFormatter.RepeatedPhrase(104));
            foreach (int n in new[] { 0, 1, 5, 11, 12, 13, 14, 20, 21, 25, 111, 112, 114, 597 })
                Assert.Equal("повторено " + n + " раз", LogFormatter.RepeatedPhrase(n));
            Assert.Equal("[PTMod] [f] ERROR boom (повторено 4 раза)", LogFormatter.FormatSummary("f", LogLevel.Error, "boom", 4));
        }

        // ---- RepeatLimiter ----

        [Test]
        public void Repeat_FourthIdenticalMessageIsSuppressed()
        {
            var limiter = new RepeatLimiter();
            Assert.True(limiter.Evaluate("f", LogLevel.Warn, "same", 0).Write);
            Assert.True(limiter.Evaluate("f", LogLevel.Warn, "same", 1).Write);
            Assert.True(limiter.Evaluate("f", LogLevel.Warn, "same", 2).Write, "third is the last one written");

            RepeatDecision fourth = limiter.Evaluate("f", LogLevel.Warn, "same", 3);
            Assert.False(fourth.Write);
            Assert.Equal(4, fourth.Occurrence);
            Assert.Null(fourth.SummaryBefore);
            Assert.False(limiter.Evaluate("f", LogLevel.Warn, "same", 59.9).Write, "still inside the 60 s window");
        }

        [Test]
        public void Repeat_NumbersDoNotMakeMessagesDifferent()
        {
            var limiter = new RepeatLimiter();
            Assert.True(limiter.Evaluate("f", LogLevel.Warn, "bad value 1", 0).Write);
            Assert.True(limiter.Evaluate("f", LogLevel.Warn, "bad value 20", 1).Write);
            Assert.True(limiter.Evaluate("f", LogLevel.Warn, "bad value 300", 2).Write);
            Assert.False(limiter.Evaluate("f", LogLevel.Warn, "bad value 4000", 3).Write);
            Assert.Equal(1, limiter.TrackedKeys);
        }

        [Test]
        public void Repeat_DifferentFeaturesAndTextsAreIndependent()
        {
            var limiter = new RepeatLimiter();
            for (int i = 0; i < 3; i++) limiter.Evaluate("a", LogLevel.Warn, "same", i);
            Assert.False(limiter.Evaluate("a", LogLevel.Warn, "same", 3).Write);

            Assert.True(limiter.Evaluate("b", LogLevel.Warn, "same", 3).Write, "same text in another feature has its own budget");
            Assert.True(limiter.Evaluate("a", LogLevel.Warn, "other", 3).Write, "another text in the same feature too");
            Assert.Equal(3, limiter.TrackedKeys);
        }

        [Test]
        public void Repeat_SummaryAfterWindow_AC_7_1()
        {
            // The same warning every frame (60 fps) for 10 seconds, then silence.
            var limiter = new RepeatLimiter();
            var written = new List<double>();
            const int Frames = 600;
            for (int frame = 0; frame < Frames; frame++)
            {
                double now = frame / 60.0;
                if (limiter.Evaluate("market-shocks", LogLevel.Warn, "Channel 'x' is unknown", now).Write) written.Add(now);
            }
            Assert.Equal(3, written.Count, "3 records in the log");

            Assert.Equal(0, limiter.Flush(30).Count, "the window is still open at 30 s");
            Assert.Equal(0, limiter.Flush(59.99).Count, "and at 59.99 s");

            IReadOnlyList<RepeatSummary> summaries = limiter.Flush(60);
            Assert.Equal(1, summaries.Count, "one summary after 60 s");
            RepeatSummary summary = summaries[0];
            Assert.Equal("market-shocks", summary.FeatureId);
            Assert.Equal(LogLevel.Warn, summary.Level);
            Assert.Equal("Channel 'x' is unknown", summary.Message);
            Assert.Equal(Frames - 3, summary.Suppressed, "N counts the suppressed repeats, not the 3 written");
            Assert.Equal(0.0, summary.WindowStartSeconds);
            Assert.Equal(60.0, summary.WindowSeconds);
            Assert.Equal("[PTMod] [market-shocks] WARN Channel 'x' is unknown (повторено 597 раз)", LogFormatter.FormatSummary(summary));

            Assert.Equal(0, limiter.Flush(61).Count, "the summary is delivered once");
            Assert.Equal(0, limiter.TrackedKeys, "a finished window is forgotten");
        }

        [Test]
        public void Repeat_NoSummaryWhenNothingWasSuppressed()
        {
            var limiter = new RepeatLimiter();
            for (int i = 0; i < 3; i++) limiter.Evaluate("f", LogLevel.Info, "three times is fine", i);

            Assert.Equal(0, limiter.Flush(1000).Count);
            Assert.Equal(0, limiter.TrackedKeys);
        }

        [Test]
        public void Repeat_WindowBoundaryBelongsToTheNextWindow()
        {
            var limiter = new RepeatLimiter();
            for (int i = 0; i < 5; i++) limiter.Evaluate("f", LogLevel.Warn, "same", i);

            Assert.False(limiter.Evaluate("f", LogLevel.Warn, "same", 59.999).Write, "just before the end");

            RepeatDecision restart = limiter.Evaluate("f", LogLevel.Warn, "same", 60);
            Assert.True(restart.Write, "at exactly +60 s a new window starts");
            Assert.Equal(1, restart.Occurrence);
            Assert.NotNull(restart.SummaryBefore, "summary of the finished window comes with the first record of the next");
            Assert.Equal(3, restart.SummaryBefore.Suppressed, "records 4, 5 and the one at 59.999");
        }

        [Test]
        public void Repeat_NewWindowGetsFullBudgetAgain()
        {
            var limiter = new RepeatLimiter();
            for (int i = 0; i < 6; i++) limiter.Evaluate("f", LogLevel.Warn, "same", i);

            Assert.True(limiter.Evaluate("f", LogLevel.Warn, "same", 100).Write);
            Assert.True(limiter.Evaluate("f", LogLevel.Warn, "same", 101).Write);
            Assert.True(limiter.Evaluate("f", LogLevel.Warn, "same", 102).Write);
            RepeatDecision fourth = limiter.Evaluate("f", LogLevel.Warn, "same", 103);
            Assert.False(fourth.Write);
            Assert.Null(fourth.SummaryBefore, "the summary of the first window was already handed out");
        }

        [Test]
        public void Repeat_WindowIsFixedFromTheFirstRecord()
        {
            // Records keep coming, but the window does not slide: the 60 s end at t = 60 no matter what.
            var limiter = new RepeatLimiter();
            for (int t = 0; t < 60; t += 5) limiter.Evaluate("f", LogLevel.Warn, "same", t);

            RepeatDecision next = limiter.Evaluate("f", LogLevel.Warn, "same", 60);
            Assert.True(next.Write);
            Assert.NotNull(next.SummaryBefore);
            Assert.Equal(12 - 3, next.SummaryBefore.Suppressed);
        }

        [Test]
        public void Repeat_FlushIsOrderedByWindowStart()
        {
            var limiter = new RepeatLimiter();
            for (int i = 0; i < 4; i++) limiter.Evaluate("b", LogLevel.Warn, "late", 10 + i);
            for (int i = 0; i < 5; i++) limiter.Evaluate("a", LogLevel.Error, "early", i);

            IReadOnlyList<RepeatSummary> summaries = limiter.Flush(500);
            Assert.Equal(new[] { "a", "b" }, summaries.Select(s => s.FeatureId).ToArray());
            Assert.Equal(new[] { 2, 1 }, summaries.Select(s => s.Suppressed).ToArray());
            Assert.Equal(LogLevel.Error, summaries[0].Level);
        }

        [Test]
        public void Repeat_ForcedFlushClosesOpenWindows()
        {
            var limiter = new RepeatLimiter();
            for (int i = 0; i < 10; i++) limiter.Evaluate("f", LogLevel.Warn, "same", i);
            limiter.Evaluate("f", LogLevel.Warn, "quiet", 0);

            IReadOnlyList<RepeatSummary> summaries = limiter.Flush(20, true);
            Assert.Equal(1, summaries.Count, "only the message with suppressed repeats is summarized");
            Assert.Equal(7, summaries[0].Suppressed);
            Assert.Equal(0, limiter.TrackedKeys);
            Assert.Equal(0, limiter.Flush(20, true).Count);
        }

        [Test]
        public void Repeat_ConfigureChangesLimitAndWindow()
        {
            var limiter = new RepeatLimiter();
            limiter.Configure(1, 10);
            Assert.Equal(1, limiter.Limit);
            Assert.Equal(10.0, limiter.WindowSeconds);

            Assert.True(limiter.Evaluate("f", LogLevel.Warn, "same", 0).Write);
            Assert.False(limiter.Evaluate("f", LogLevel.Warn, "same", 1).Write);
            Assert.Equal(1, limiter.Flush(10).Count, "the 10 s window is over");

            limiter.Configure(5, 60);
            for (int i = 0; i < 5; i++) Assert.True(limiter.Evaluate("g", LogLevel.Warn, "same", 100 + i).Write);
            Assert.False(limiter.Evaluate("g", LogLevel.Warn, "same", 105).Write);
        }

        [Test]
        public void Repeat_BadSettingsAreClamped()
        {
            var limiter = new RepeatLimiter(0, -5);
            Assert.Equal(1, limiter.Limit);
            Assert.Equal(60.0, limiter.WindowSeconds);

            limiter.Configure(-3, double.NaN);
            Assert.Equal(1, limiter.Limit);
            Assert.Equal(60.0, limiter.WindowSeconds);

            limiter.Configure(2, double.PositiveInfinity);
            Assert.Equal(2, limiter.Limit);
            Assert.Equal(60.0, limiter.WindowSeconds);

            Assert.Equal(3, new RepeatLimiter().Limit);
            Assert.Equal(60.0, new RepeatLimiter().WindowSeconds);
        }

        [Test]
        public void Repeat_NullMessageAndFeatureDoNotThrow()
        {
            var limiter = new RepeatLimiter();
            Assert.True(limiter.Evaluate(null, LogLevel.Info, null, 0).Write);
            Assert.True(limiter.Evaluate("", LogLevel.Info, "", 0).Write);
            Assert.Equal(1, limiter.TrackedKeys, "null and empty are the same message of the framework");
        }

        [Test]
        public void Repeat_ClockGoingBackDoesNotBreakTheWindow()
        {
            var limiter = new RepeatLimiter();
            for (int i = 0; i < 3; i++) limiter.Evaluate("f", LogLevel.Warn, "same", 100 + i);
            Assert.False(limiter.Evaluate("f", LogLevel.Warn, "same", 50).Write, "an earlier time is still inside the window");
            Assert.Equal(0, limiter.Flush(50).Count);
        }

        [Test]
        public void Repeat_TrackedKeysAreBounded()
        {
            var limiter = new RepeatLimiter();
            int total = RepeatLimiter.MaxTrackedKeys * 3;
            for (int i = 0; i < total; i++)
            {
                string message = "unique message " + new string((char)('a' + i % 26), 1 + i / 26); // differs by letters, not numbers
                // each message twice more so that it gets a suppressed repeat and a summary when evicted
                for (int repeat = 0; repeat < 5; repeat++) limiter.Evaluate("f", LogLevel.Warn, message, i * 0.001);
            }
            Assert.True(limiter.TrackedKeys <= RepeatLimiter.MaxTrackedKeys, "tracked keys: " + limiter.TrackedKeys);

            IReadOnlyList<RepeatSummary> summaries = limiter.Flush(0.5);
            Assert.True(summaries.Count > 0, "evicted windows leave their summaries for the next flush");
            Assert.True(summaries.All(s => s.Suppressed == 2), "5 records: 3 written, 2 suppressed");
        }

        [Test]
        public void Repeat_ThreadsShareOneBudget()
        {
            var limiter = new RepeatLimiter();
            int written = 0;
            var threads = new List<Thread>();
            for (int t = 0; t < 4; t++)
            {
                var thread = new Thread(() =>
                {
                    for (int i = 0; i < 500; i++)
                    {
                        if (limiter.Evaluate("f", LogLevel.Warn, "shared", 0).Write) Interlocked.Increment(ref written);
                    }
                });
                threads.Add(thread);
                thread.Start();
            }
            foreach (Thread thread in threads) thread.Join();

            Assert.Equal(3, written);
            Assert.Equal(1997, limiter.Flush(60)[0].Suppressed);
        }

        // ---- ProblemBuffer ----

        [Test]
        public void Buffer_KeepsLast50InOrder()
        {
            var buffer = new ProblemBuffer();
            Assert.Equal(50, buffer.Capacity);
            Assert.Equal(50, ProblemBuffer.DefaultCapacity);

            for (int i = 0; i < 60; i++) Assert.True(buffer.Add(Entry(LogLevel.Warn, "m" + i, i)));

            Assert.Equal(50, buffer.Count);
            Assert.Equal(60, buffer.TotalAdded);
            Assert.Equal(10, buffer.Dropped);

            IReadOnlyList<LogEntry> items = buffer.Snapshot();
            Assert.Equal(50, items.Count);
            Assert.Equal("m10", items[0].Message, "oldest kept");
            Assert.Equal("m59", items[49].Message, "newest");
            Assert.Equal(Enumerable.Range(10, 50).Select(n => "m" + n).ToArray(), items.Select(e => e.Message).ToArray());
        }

        [Test]
        public void Buffer_OrderBeforeItWraps()
        {
            var buffer = new ProblemBuffer(5);
            buffer.Add(Entry(LogLevel.Error, "a", 1));
            buffer.Add(Entry(LogLevel.Warn, "b", 2));
            buffer.Add(Entry(LogLevel.Error, "c", 3));

            Assert.Equal(new[] { "a", "b", "c" }, buffer.Snapshot().Select(e => e.Message).ToArray());
            Assert.Equal(0, buffer.Dropped);
        }

        [Test]
        public void Buffer_OnlyWarningsAndErrors()
        {
            var buffer = new ProblemBuffer();
            Assert.False(buffer.Add(null));
            Assert.False(buffer.Add(Entry(LogLevel.Debug, "d", 0)));
            Assert.False(buffer.Add(Entry(LogLevel.Info, "i", 0)));
            Assert.True(buffer.Add(Entry(LogLevel.Warn, "w", 0)));
            Assert.True(buffer.Add(Entry(LogLevel.Error, "e", 0)));

            Assert.Equal(2, buffer.Count);
            Assert.Equal(2, buffer.TotalAdded);
        }

        [Test]
        public void Buffer_SnapshotIsACopy()
        {
            var buffer = new ProblemBuffer(3);
            buffer.Add(Entry(LogLevel.Warn, "a", 0));
            IReadOnlyList<LogEntry> before = buffer.Snapshot();

            buffer.Add(Entry(LogLevel.Warn, "b", 0));
            buffer.Clear();

            Assert.Equal(1, before.Count);
            Assert.Equal("a", before[0].Message);
            Assert.Equal(0, buffer.Count);
            Assert.Equal(0, buffer.Snapshot().Count);
            Assert.Equal(2, buffer.TotalAdded, "clearing does not rewrite history");
        }

        [Test]
        public void Buffer_ReusableAfterClear()
        {
            var buffer = new ProblemBuffer(2);
            buffer.Add(Entry(LogLevel.Warn, "a", 0));
            buffer.Add(Entry(LogLevel.Warn, "b", 0));
            buffer.Add(Entry(LogLevel.Warn, "c", 0));
            buffer.Clear();
            buffer.Add(Entry(LogLevel.Warn, "d", 0));
            buffer.Add(Entry(LogLevel.Warn, "e", 0));
            buffer.Add(Entry(LogLevel.Warn, "f", 0));

            Assert.Equal(new[] { "e", "f" }, buffer.Snapshot().Select(e => e.Message).ToArray());
        }

        [Test]
        public void Buffer_ResizeKeepsNewest()
        {
            var buffer = new ProblemBuffer(5);
            for (int i = 0; i < 7; i++) buffer.Add(Entry(LogLevel.Warn, "m" + i, 0)); // keeps m2..m6, dropped 2

            buffer.Resize(3);
            Assert.Equal(3, buffer.Capacity);
            Assert.Equal(new[] { "m4", "m5", "m6" }, buffer.Snapshot().Select(e => e.Message).ToArray());
            Assert.Equal(4, buffer.Dropped);

            buffer.Resize(10);
            buffer.Add(Entry(LogLevel.Warn, "m7", 0));
            Assert.Equal(new[] { "m4", "m5", "m6", "m7" }, buffer.Snapshot().Select(e => e.Message).ToArray());
        }

        [Test]
        public void Buffer_CapacityIsClamped()
        {
            Assert.Equal(1, new ProblemBuffer(0).Capacity);
            Assert.Equal(1, new ProblemBuffer(-7).Capacity);
            Assert.Equal(ProblemBuffer.MaxCapacity, new ProblemBuffer(1000000).Capacity);

            var buffer = new ProblemBuffer(4);
            buffer.Resize(0);
            Assert.Equal(1, buffer.Capacity);
            buffer.Resize(int.MaxValue);
            Assert.Equal(ProblemBuffer.MaxCapacity, buffer.Capacity);
        }

        [Test]
        public void Buffer_ThreadsDoNotCorruptTheRing()
        {
            var buffer = new ProblemBuffer();
            var threads = new List<Thread>();
            for (int t = 0; t < 4; t++)
            {
                int id = t;
                var thread = new Thread(() =>
                {
                    for (int i = 0; i < 1000; i++) buffer.Add(Entry(LogLevel.Error, "t" + id + "-" + i, i));
                });
                threads.Add(thread);
                thread.Start();
            }
            foreach (Thread thread in threads) thread.Join();

            Assert.Equal(4000, buffer.TotalAdded);
            Assert.Equal(50, buffer.Count);
            Assert.Equal(50, buffer.Snapshot().Select(e => e.Message).Distinct().Count());
        }

        // ---- LogEntry ----

        [Test]
        public void Entry_NormalizesAndBuildsTheLine()
        {
            var plain = new LogEntry(LogLevel.Warn, null, "careful", 12.5);
            Assert.Equal("mod-framework", plain.FeatureId);
            Assert.False(plain.IsSummary);
            Assert.Equal(12.5, plain.TimeSeconds);
            Assert.Equal("[PTMod] [mod-framework] WARN careful", plain.Line);

            var withDetails = new LogEntry(LogLevel.Error, "event-system", "Load failed", 1, "System.IO.IOException: disk");
            Assert.Equal("[PTMod] [event-system] ERROR Load failed: System.IO.IOException: disk", withDetails.Line);

            var summary = new LogEntry(LogLevel.Warn, "event-system", "Load failed", 60, null, 14);
            Assert.True(summary.IsSummary);
            Assert.Equal(14, summary.SuppressedCount);
            Assert.Equal("[PTMod] [event-system] WARN Load failed (повторено 14 раз)", summary.Line);

            Assert.Equal(string.Empty, new LogEntry(LogLevel.Info, "f", null, 0).Message);
            Assert.Equal(0, new LogEntry(LogLevel.Info, "f", "m", 0, null, -5).SuppressedCount);
        }

        [Test]
        public void Entry_DrivenByTheLimiterGivesTheSpecScenario()
        {
            // What the ModLog adapter does: evaluate, write what is allowed, keep problems, flush summaries.
            var limiter = new RepeatLimiter();
            var buffer = new ProblemBuffer();
            var file = new List<string>();

            Action<double> warn = now =>
            {
                RepeatDecision d = limiter.Evaluate("f", LogLevel.Warn, "frame hiccup", now);
                if (d.SummaryBefore != null) file.Add(LogFormatter.FormatSummary(d.SummaryBefore));
                if (!d.Write) return;
                file.Add(LogFormatter.Format("f", LogLevel.Warn, "frame hiccup"));
                buffer.Add(new LogEntry(LogLevel.Warn, "f", "frame hiccup", now));
            };

            for (int i = 0; i < 600; i++) warn(i / 60.0);
            foreach (RepeatSummary s in limiter.Flush(60))
            {
                file.Add(LogFormatter.FormatSummary(s));
                buffer.Add(new LogEntry(s.Level, s.FeatureId, s.Message, 60, null, s.Suppressed));
            }

            Assert.Equal(4, file.Count);
            Assert.Equal("[PTMod] [f] WARN frame hiccup", file[0]);
            Assert.Equal("[PTMod] [f] WARN frame hiccup (повторено 597 раз)", file[3]);
            Assert.Equal(4, buffer.Count);
            Assert.True(buffer.Snapshot()[3].IsSummary);
        }

        private static LogEntry Entry(LogLevel level, string message, double time)
        {
            return new LogEntry(level, "test", message, time);
        }
    }
}
