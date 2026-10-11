using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.Modifiers;
using PTMod.Features.PressReviews;
using PTMod.Tests.Modifiers;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.PressReviews
{
    /// <summary>
    /// The self-check at start, the isolation of errors and the measurements (NFR-1, NFR-2, TSK-019): the line of the log <c>self-check OK (… patches, 6 outlets, …,
    /// channels OK, reputation OK)</c> and the rules behind it, the timer of the daily work (the average within 0.2 ms), the yearly spread of the metascores, and the rule that
    /// a failing provider is written to the log at most once a day and passed on to the core (which counts it and uses ×1) while the feature keeps no counters of errors.
    /// </summary>
    [Covers(typeof(SelfCheckReport), typeof(SelfCheckRules), typeof(PatchLine), typeof(TimingBook), typeof(TimingSummary), typeof(ScoreDistribution), typeof(ScoreSummary))]
    public class PressDiagnosticsTests
    {
        private static PatchLine Patch(string target, bool applied = true, bool optional = false)
        {
            return new PatchLine { Target = target, Applied = applied, Optional = optional };
        }

        // ---- the line of the self-check ----

        [Test]
        public void TheLine_OfTheBuiltInContent_IsTheLineOfTheDesign()
        {
            var report = new SelfCheckReport();
            List<NoteTemplate> notes = new List<NoteTemplate>(PressContentFixture.LoadNotes().Records);
            List<Outlet> outlets = PressContentFixture.Outlets();
            ValidationReport content = PressContentValidator.Validate(outlets, PressContentFixture.Config(), notes);

            SelfCheckRules.CheckContent(outlets.Count, notes.Count, content.Errors.Count, content.CoverageHoles.Count, report);
            SelfCheckRules.CheckPatches(new List<PatchLine>(), false, report);

            Assert.True(report.IsOk, string.Join("; ", report.Problems));
            Assert.Equal("self-check OK (0 patches, 6 outlets, 12 notes, channels OK, reputation OK)", report.Summary());
        }

        [Test]
        public void TheLine_WithTheMarketColumn_CountsOnePatch()
        {
            var report = new SelfCheckReport { Outlets = 6, Notes = 12 };
            SelfCheckRules.CheckPatches(new List<PatchLine> { Patch("HardwareWindow.ShowMarket", true, true) }, true, report);

            Assert.True(report.IsOk);
            Assert.Equal(1, report.Patches);
            Assert.Equal("self-check OK (1 patch, 6 outlets, 12 notes, channels OK, reputation OK)", report.Summary());
        }

        [Test]
        public void TheLine_NamesWhatFailed()
        {
            var report = new SelfCheckReport { Outlets = 1, Notes = 0, ChannelsOk = false };
            report.Fail("the channel popularity.gain is not declared");
            Assert.False(report.IsOk);
            Assert.Equal("self-check FAILED (0 patches, 1 outlet, 0 notes, channels FAILED, reputation OK)", report.Summary());

            var reputation = new SelfCheckReport { Outlets = 6, Notes = 12, ReputationOk = false };
            Assert.Equal("self-check FAILED (0 patches, 6 outlets, 12 notes, channels OK, reputation FAILED)", reputation.Summary());
        }

        [Test]
        public void AWarning_DoesNotFailTheCheck()
        {
            var report = new SelfCheckReport { Outlets = 6, Notes = 12 };
            report.Warn("an optional patch is not applied");
            report.Warn(null);
            report.Fail("");
            report.Fail(null);

            Assert.True(report.IsOk);
            Assert.Equal(1, report.Warnings.Count);
            Assert.Equal(0, report.Problems.Count);
            Assert.True(report.Summary().StartsWith("self-check OK ("));
        }

        // ---- the patches ----

        [Test]
        public void NoPatches_AndTheColumnOff_IsRight()
        {
            var report = new SelfCheckReport();
            SelfCheckRules.CheckPatches(null, false, report);
            Assert.True(report.IsOk);
            Assert.Equal(0, report.Patches);
        }

        [Test]
        public void ThePatchesOnTheReleasesAndOnTheFormulasOfTheMarket_AreForbidden()
        {
            foreach (string target in new[] { "Company.AddCpu", "AICompany.AddCpu", "Market.UpdatePopularities", "Market.CalculateRatings" })
            {
                Assert.True(SelfCheckRules.IsForbidden(target), target);

                var report = new SelfCheckReport();
                SelfCheckRules.CheckPatches(new List<PatchLine> { Patch(target) }, false, report);
                Assert.False(report.IsOk, target + " must be a problem");
                Assert.True(report.Problems[0].Contains(target), report.Problems[0]);
            }
            Assert.False(SelfCheckRules.IsForbidden("HardwareWindow.ShowMarket"));
            Assert.False(SelfCheckRules.IsForbidden(null));
            Assert.False(SelfCheckRules.IsForbidden(""));
        }

        [Test]
        public void TheMarketColumnPatch_IsExpectedExactlyWhenTheColumnWasSwitchedOnAtStart()
        {
            var declared = new List<PatchLine> { Patch(SelfCheckRules.MarketColumnTarget, true, true) };

            var onAndThere = new SelfCheckReport();
            SelfCheckRules.CheckPatches(declared, true, onAndThere);
            Assert.True(onAndThere.IsOk);

            var onButMissing = new SelfCheckReport();
            SelfCheckRules.CheckPatches(new List<PatchLine>(), true, onButMissing);
            Assert.False(onButMissing.IsOk, "the column is on but its patch is not declared");

            var offButDeclared = new SelfCheckReport();
            SelfCheckRules.CheckPatches(declared, false, offButDeclared);
            Assert.False(offButDeclared.IsOk, "the patch must not exist while the column is off");
        }

        [Test]
        public void AnOptionalPatchThatDidNotApply_IsAWarning_AnyOtherMissingPatchIsAProblem()
        {
            var optional = new SelfCheckReport();
            SelfCheckRules.CheckPatches(new List<PatchLine> { Patch(SelfCheckRules.MarketColumnTarget, false, true) }, true, optional);
            Assert.True(optional.IsOk, "the feature goes on without the column");
            Assert.Equal(0, optional.Patches);
            Assert.Equal(1, optional.Warnings.Count);

            var required = new SelfCheckReport();
            SelfCheckRules.CheckPatches(new List<PatchLine> { Patch("Somewhere.Else", false, false) }, false, required);
            Assert.False(required.IsOk);
        }

        // ---- the content, the identifiers ----

        [Test]
        public void TheContent_NeedsAnOutlet_NoErrors_AndNoHoles()
        {
            var good = new SelfCheckReport();
            SelfCheckRules.CheckContent(6, 12, 0, 0, good);
            Assert.True(good.IsOk);
            Assert.Equal(6, good.Outlets);
            Assert.Equal(12, good.Notes);

            var none = new SelfCheckReport();
            SelfCheckRules.CheckContent(0, 12, 0, 0, none);
            Assert.False(none.IsOk);

            var errors = new SelfCheckReport();
            SelfCheckRules.CheckContent(6, 12, 2, 0, errors);
            Assert.False(errors.IsOk);

            var holes = new SelfCheckReport();
            SelfCheckRules.CheckContent(6, 12, 0, 3, holes);
            Assert.False(holes.IsOk);

            var noNotes = new SelfCheckReport();
            SelfCheckRules.CheckContent(6, 0, 0, 0, noNotes);
            Assert.True(noNotes.IsOk, "no notes only means no quarterly notes");
            Assert.Equal(1, noNotes.Warnings.Count);
        }

        [Test]
        public void TheTagsOfTheLetters_PassTheRulesOfTheIdentifiers()
        {
            try
            {
                ModIds.Load(CoreContentTests.LoadCore().GetObject("prefixes"));
                var tags = new List<string>
                {
                    ReviewMailText.TagOf(1), AwardsMailText.TagOf(1990), NotesMailText.TagOf(new DateTime(1990, 1, 1)),
                    DigestMailText.TagOf(new DateTime(1990, 1, 1)), ArticleLog.TagOf(1),
                };

                var good = new SelfCheckReport();
                SelfCheckRules.CheckTags(tags, tag => ModIds.IsTagAllowed("press-reviews", tag), good);
                Assert.True(good.IsOk, string.Join("; ", good.Problems));

                var bad = new SelfCheckReport();
                SelfCheckRules.CheckTags(new[] { "rec:cpu:1" }, tag => ModIds.IsTagAllowed("press-reviews", tag), bad);
                Assert.False(bad.IsOk, "another feature's tag is not the press's");

                SelfCheckRules.CheckTags(null, null, new SelfCheckReport());
            }
            finally
            {
                ModIds.Clear();
            }
        }

        // ---- the timer ----

        [Test]
        public void TheTimer_AddsTheHandlersOfADayIntoOneSample()
        {
            var book = new TimingBook();
            var day = new DateTime(1995, 3, 1);
            book.Add(day, 0.03);
            book.Add(day, 0.02);                       // the second handler of the same day
            book.Add(day.AddDays(1), 0.10);
            book.Add(day.AddDays(2), 0.01);

            TimingSummary summary = book.Take();
            Assert.Equal(3, summary.Days);
            Assert.Near(0.16, summary.TotalMs, 1e-9);
            Assert.Near(0.10, summary.MaxDayMs, 1e-9);
            Assert.Near(0.16 / 3, summary.AverageMs, 1e-9);
            Assert.False(TimingBook.IsOverBudget(summary));
        }

        [Test]
        public void TheTimer_StartsAgainAfterTake_AndIgnoresWhatIsNotATime()
        {
            var book = new TimingBook();
            var day = new DateTime(1995, 3, 1);
            book.Add(day, 0.05);
            book.Add(day, double.NaN);
            book.Add(day, -1.0);
            book.Add(day, double.PositiveInfinity);
            Assert.Equal(1, book.Take().Days);

            TimingSummary empty = book.Take();
            Assert.Equal(0, empty.Days);
            Assert.Near(0.0, empty.AverageMs);
            Assert.False(TimingBook.IsOverBudget(empty), "nothing to judge");
        }

        [Test]
        public void TheTimer_KnowsTheBudgetOf02Ms()
        {
            Assert.Near(0.2, TimingBook.BudgetMs);

            var book = new TimingBook();
            book.Add(new DateTime(1995, 3, 1), 0.2);
            Assert.False(TimingBook.IsOverBudget(book.Take()), "0.2 ms is within the budget");

            book.Add(new DateTime(1995, 3, 1), 0.21);
            Assert.True(TimingBook.IsOverBudget(book.Take()));
        }

        [Test]
        public void TheLineOfTheMonth_SaysTheAverageAndTheWorstDay()
        {
            var book = new TimingBook();
            book.Add(new DateTime(1995, 3, 1), 0.021);
            book.Add(new DateTime(1995, 3, 2), 0.14);
            string line = TimingBook.Line("1995-03", book.Take());

            Assert.Equal("timing 1995-03: 2 day(s), 0.081 ms a day on average, worst day 0.14 ms (within 0.2 ms)", line);

            book.Add(new DateTime(1995, 4, 1), 0.5);
            Assert.True(TimingBook.Line("1995-04", book.Take()).Contains("OVER THE BUDGET"));
            Assert.Equal("timing 1995-05: 0 day(s)", TimingBook.Line("1995-05", new TimingSummary()));
        }

        // ---- the spread of the metascores ----

        [Test]
        public void TheSummary_IsTheMedianAndTheTwoShares()
        {
            ScoreSummary odd = ScoreDistribution.Summarize(new[] { 90, 10, 60, 70, 50 });
            Assert.Equal(5, odd.Count);
            Assert.Near(60.0, odd.Median);
            Assert.Near(0.2, odd.ShareHigh, 1e-9);
            Assert.Near(0.2, odd.ShareLow, 1e-9);

            ScoreSummary even = ScoreDistribution.Summarize(new[] { 60, 70, 80, 90 });
            Assert.Near(75.0, even.Median);

            ScoreSummary empty = ScoreDistribution.Summarize(new int[0]);
            Assert.Equal(0, empty.Count);
            Assert.Near(0.0, empty.Median);
            Assert.Equal(0, ScoreDistribution.Summarize(null).Count);
        }

        [Test]
        public void TheShares_HaveTheirBoundariesAt85AndBelow40()
        {
            ScoreSummary s = ScoreDistribution.Summarize(new[] { 84, 85, 86, 39, 40, 41 });
            Assert.Near(2.0 / 6, s.ShareHigh, 1e-9, "85 is high, 84 is not");
            Assert.Near(1.0 / 6, s.ShareLow, 1e-9, "39 is low, 40 is not");
            Assert.Equal(85, ScoreDistribution.HighScore);
            Assert.Equal(40, ScoreDistribution.LowScore);
        }

        [Test]
        public void TheTargets_AreTheOnesOfTheDesign()
        {
            Func<int, int, int, ScoreSummary> make = (median, highPercent, lowPercent) =>
            {
                var scores = new List<int>();
                for (int i = 0; i < 100; i++) scores.Add(i < highPercent ? 90 : i < highPercent + lowPercent ? 30 : median);
                return ScoreDistribution.Summarize(scores);
            };

            Assert.True(ScoreDistribution.IsWithinTargets(make(62, 15, 15)), "15 % each is within");
            Assert.False(ScoreDistribution.IsWithinTargets(make(62, 16, 0)), "16 % high is not");
            Assert.False(ScoreDistribution.IsWithinTargets(make(62, 0, 16)), "16 % low is not");
            Assert.True(ScoreDistribution.IsWithinTargets(Summary(55)), "a median of 55 is within");
            Assert.True(ScoreDistribution.IsWithinTargets(Summary(70)), "a median of 70 is within");
            Assert.False(ScoreDistribution.IsWithinTargets(Summary(54)));
            Assert.False(ScoreDistribution.IsWithinTargets(Summary(71)));
            Assert.True(ScoreDistribution.IsWithinTargets(new ScoreSummary()), "nothing to judge");
        }

        private static ScoreSummary Summary(int median)
        {
            return ScoreDistribution.Summarize(new[] { median, median, median });
        }

        [Test]
        public void TheScoresOfAYear_AreThoseOfTheReviewsPublishedInIt_ByEveryCompany()
        {
            var archive = new List<ReviewRecord>
            {
                new ReviewRecord { metascore = 70, publishedOn = "1995-01-02", companyKey = "player" },
                new ReviewRecord { metascore = 50, publishedOn = "1995-12-31", companyKey = "ai:1" },
                new ReviewRecord { metascore = 99, publishedOn = "1996-01-01", companyKey = "ai:1" },
                new ReviewRecord { metascore = 11, publishedOn = "garbage", companyKey = "ai:1" },
                null,
            };
            Assert.Equal(new List<int> { 70, 50 }, ScoreDistribution.ScoresOfYear(archive, 1995));
            Assert.Equal(0, ScoreDistribution.ScoresOfYear(null, 1995).Count);
        }

        [Test]
        public void TheLineOfTheYear_SaysWhetherTheSpreadIsWithinTheTargets()
        {
            var scores = new List<int>();
            for (int i = 0; i < 48; i++) scores.Add(i < 3 ? 88 : i < 5 ? 35 : 62);
            string within = ScoreDistribution.Line(1995, ScoreDistribution.Summarize(scores));
            Assert.Equal("balance 1995: 48 review(s), median 62, 85 or more: 6.3 %, below 40: 4.2 % (within the targets)", within);

            string out_ = ScoreDistribution.Line(1996, ScoreDistribution.Summarize(new[] { 95, 95, 95, 50 }));
            Assert.True(out_.Contains("OUT OF THE TARGETS") && out_.Contains("review-config.json"), out_);
            Assert.Equal("balance 1997: 0 review(s)", ScoreDistribution.Line(1997, new ScoreSummary()));
        }

        // ---- the game's side, by its source ----

        private static string Code(params string[] path)
        {
            string file = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "PressReviews", Path.Combine(path));
            return SourceScan.Strip(SourceScan.ReadText(file), true);
        }

        private static string CodeWithStrings(params string[] path)
        {
            string file = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "PressReviews", Path.Combine(path));
            return SourceScan.Strip(SourceScan.ReadText(file), false);
        }

        [Test]
        public void TheSelfCheck_RunsWhenTheGameIsReady_AndLogsOneLine()
        {
            string feature = Code("PressReviewsFeature.cs");
            Assert.True(Regex.IsMatch(feature, @"ModTicks\s*\.\s*GameReady\s*\+=\s*PressSelfCheck\s*\.\s*OnGameReady"), "on GameReady, when the registries of the core are complete");

            string check = CodeWithStrings("Diagnostics", "PressSelfCheck.cs");
            Assert.True(check.Contains("report.Summary()"), "the line is the summary of the report");
            Assert.True(Regex.IsMatch(check, @"ModLog\s*\.\s*Info\s*\(\s*Feature\s*,\s*report\s*\.\s*Summary\s*\(\s*\)"), "an INFO line when all is well");
            Assert.True(check.Contains("ModHooks.Report") && check.Contains("SelfCheckRules.CheckPatches"), "the patches come from the registry of the core");
            Assert.True(check.Contains("ModModifiers.IsDeclared(ModChannels.PopularityGain)") && check.Contains("ModModifiers.HasAny(ModChannels.PopularityGain)"), "the channel is declared and has its provider");
            Assert.True(check.Contains("ModReputation.Get(") && check.Contains("ModIds.IsTagAllowed"), "the reputation and the identifiers");
            Assert.True(check.Contains("\"Company.AddCpu\"") && check.Contains("\"AICompany.AddCpu\"") && check.Contains("\"Market.UpdatePopularities\""),
                "the patches of the core that the events and the channel stand on");
            Assert.True(Regex.IsMatch(check, @"catch\s*\(\s*Exception\s+ex\s*\)\s*\{\s*report\s*\.\s*Fail"), "a check that throws is a finding, not an exception");
            Assert.True(Code("PressReviewsFeature.cs").Contains("PressSelfCheck.Run()"), "and it runs when the feature is switched on in the middle of a game");
        }

        [Test]
        public void TheProvider_LogsAFailureAtMostOnceADay_AndPassesItOnToTheCore()
        {
            string effects = Code("Effects", "ReviewEffects.cs");
            int start = effects.IndexOf("public ProviderResult ProvideWithReason", StringComparison.Ordinal);
            Assert.True(start > 0);
            string provider = effects.Substring(start, effects.IndexOf("public void DailyRefresh", start, StringComparison.Ordinal) - start);

            Assert.True(Regex.IsMatch(provider, @"catch\s*\(\s*Exception\s+ex\s*\)\s*\{\s*LogProviderFailure\s*\(\s*ex\s*\)\s*;\s*throw\s*;"),
                "the exception is logged and rethrown: the core counts it against the feature and uses ×1");

            int log = effects.IndexOf("private void LogProviderFailure", StringComparison.Ordinal);
            string logBody = effects.Substring(log);
            Assert.True(Regex.IsMatch(logBody, @"providerFailureDay\s*==\s*today\s*\)\s*return"), "once per game day");
            Assert.True(logBody.Contains("providerFailureDay = today"));
        }

        [Test]
        public void AProviderThatThrows_GivesTimes1_AndTheCoreCountsTheFaultAgainstTheFeature()
        {
            // NFR-2: the provider of the feature lets the exception through; the engine of the core (the code the game runs) uses ×1 and counts one fault for the feature
            var model = new PopularityModel(() => PressContentFixture.Config(), new HypeBook(), key => { throw new InvalidOperationException("boom"); }, () => true);
            ChannelRegistry registry = ModifierFixtures.Registry(null);
            registry.RegisterReasonedProvider("press-reviews", "popularity.gain", query => model.Evaluate(query.CompanyKey, query.CpuSaveId));
            ModifierQuery q = ModifierFixtures.Q("player", 10, Segment.HighEnd);

            Assert.Equal(1f, registry.Get("popularity.gain", q), "the formula of the game goes on with ×1");
            Assert.Equal(1, registry.ProviderFaults("press-reviews"), "and the failure is the feature's, counted by the core");
            Assert.Equal(0, registry.ProviderFaults("some-other-feature"));
        }

        [Test]
        public void TheFeature_KeepsNoCountersOfErrorsOfItsOwn()
        {
            // the circuit breaker is the core's (ModHooks.Guard, hooks.faultThreshold); the only counter of the feature is the attempts of a queued review
            var counter = new Regex(@"\b(?:error|fault|failure|exception)s?(?:Count|Total|Counter)\b|\b(?:Count|Total)Of(?:Errors|Faults|Failures)\b", RegexOptions.IgnoreCase);
            foreach (string path in SourceScan.Files(Path.Combine(SourceScan.SrcDirOrFail(), "Features", "PressReviews"), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                Assert.False(counter.IsMatch(code), SourceScan.Relative(SourceScan.SrcDirOrFail(), path) + " counts errors itself");
            }
        }

        [Test]
        public void TheMeasurements_AreOnlyForTheDebugBuild()
        {
            string timings = Code("Diagnostics", "PressTimings.cs");
            Assert.True(Regex.IsMatch(timings, @"Begin\s*\(\s*\)\s*\{\s*return\s+PressReviewsFeature\s*\.\s*DebugVerbose\s*\?\s*Stopwatch\s*\.\s*GetTimestamp\s*\(\s*\)\s*:\s*0L"), "without the parameter nothing is measured");
            Assert.True(Regex.IsMatch(timings, @"OnMonth\s*\(\s*\)\s*\{\s*if\s*\(\s*!PressReviewsFeature\s*\.\s*DebugVerbose\s*\)\s*return"), "the monthly line is for the debug build");
            Assert.True(Regex.IsMatch(timings, @"OnYear\s*\(\s*\)\s*\{\s*if\s*\(\s*!PressReviewsFeature\s*\.\s*DebugVerbose\s*\)\s*return"), "and so is the yearly one");

            string feature = Code("PressReviewsFeature.cs");
            Assert.True(Regex.IsMatch(feature, @"ctx\s*\.\s*OnTick\s*\(\s*TickPhase\s*\.\s*MonthPassed\s*,\s*PressTimings\s*\.\s*OnMonth"), "once a month, on the first");
            Assert.True(Regex.IsMatch(feature, @"ctx\s*\.\s*OnTick\s*\(\s*TickPhase\s*\.\s*LateYearPassed\s*,\s*PressTimings\s*\.\s*OnYear"), "once a year, after the awards");
            Assert.True(feature.Contains("#if PTMOD_DEBUG") && feature.Contains("DebugVerboseParam"), "the parameter debug.verbose exists in the debug build only");
        }

        [Test]
        public void TheDailyWork_IsTimed_ByTheSchedulerAndByTheEffects()
        {
            foreach (string[] file in new[] { new[] { "Game", "ReviewScheduler.cs" }, new[] { "Effects", "ReviewEffects.cs" } })
            {
                string code = Code(file);
                Assert.True(Regex.IsMatch(code, @"long\s+started\s*=\s*PressTimings\s*\.\s*Begin\s*\(\s*\)\s*;\s*try\s*\{[^}]*\}\s*finally\s*\{\s*PressTimings\s*\.\s*End\s*\(\s*started\s*\)\s*;"),
                    string.Join("/", file) + " times its daily handler with Begin/End in a finally");
            }
        }

        [Test]
        public void TheMeasurements_UseOnlyTheMonthlyAndYearlyPhases_NotADailyHandlerOfTheirOwn()
        {
            // the measuring itself must not add to the daily cost it measures
            string feature = Code("PressReviewsFeature.cs");
            Assert.False(Regex.IsMatch(feature, @"OnTick\s*\(\s*TickPhase\s*\.\s*DayPassed\s*,\s*PressTimings"), "PressTimings has no handler of the day");
        }
    }
}
