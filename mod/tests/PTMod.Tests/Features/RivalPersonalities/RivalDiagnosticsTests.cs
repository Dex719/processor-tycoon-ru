using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Log;
using PTMod.Features.RivalPersonalities;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The self-check at the start, the isolation of failures and the measurements of the daily work (NFR-1, NFR-2, TSK-024). The rules of the self-check and the
    /// clock of the timings are pure and run here; what needs the game (the registry of the core, the event system, the stopwatch in the patches) is checked by reading
    /// the sources: every patch body is inside its own <c>try</c> and reports to <c>ModHooks</c>, the feature counts no failures of its own, every measured stretch is
    /// closed in a <c>finally</c>.
    /// </summary>
    [Covers(typeof(RivalSelfCheckRules), typeof(RivalSelfCheckReport), typeof(RivalCheckFacts), typeof(RivalPatchLine), typeof(RivalTimingBook),
        typeof(RivalTimingSummary), typeof(RivalTimingScope))]
    public class RivalDiagnosticsTests
    {
        // ---- the facts of a healthy start ----

        /// <summary>The 14 declarations of the 12 targets as the registry of the core reports them, all applied.</summary>
        private static RivalCheckFacts Healthy()
        {
            var facts = new RivalCheckFacts { Archetypes = 7, Assignments = 13, CatalogUsable = true, EventSystemActive = true };
            foreach (string target in RivalSelfCheckRules.RequiredTargets) facts.Patches.Add(new RivalPatchLine { Target = target, Applied = true });
            facts.Patches.Add(new RivalPatchLine { Target = "AIBehaviourMarket.OnTick", Applied = true });              // the prefix and the postfix
            facts.Patches.Add(new RivalPatchLine { Target = "ContractNegotiationWindow.UpdateScores", Applied = true }); // the prefix and the finalizer
            facts.Patches.Add(new RivalPatchLine { Target = RivalSelfCheckRules.OptionalTarget, Applied = true, Optional = true });
            facts.RegisteredEvents.AddRange(ReactionTags.EventIds);
            facts.RegisteredEffects.AddRange(ReactionTags.EffectTypes);
            facts.KnownPredicates.AddRange(RivalPredicateNames.All);
            return facts;
        }

        // ---- the line of the log ----

        [Test]
        public void AHealthyStart_IsOneLine_WithTheNumbersOfTheDesign()
        {
            RivalSelfCheckReport report = RivalSelfCheckRules.Run(Healthy());

            Assert.True(report.IsOk);
            Assert.False(report.IsFatal);
            Assert.Equal("self-check OK (12 patches, 7 archetypes, 13 assignments, 5 events)", report.Summary());
            Assert.Equal("[PTMod] [rival-personalities] INFO self-check OK (12 patches, 7 archetypes, 13 assignments, 5 events)",
                LogFormatter.Format("rival-personalities", LogLevel.Info, report.Summary()), "the line of tasks.md TSK-024, word for word");
            Assert.Equal(0, report.Problems.Count, string.Join("; ", report.Problems));
            Assert.Equal(0, report.Warnings.Count, string.Join("; ", report.Warnings));
            Assert.Null(report.MissingLine());
        }

        [Test]
        public void TheOptionalPatchThatIsMissing_IsAWarning_AndTheFeatureGoesOn()
        {
            RivalCheckFacts facts = Healthy();
            facts.Patches.RemoveAll(p => p.Target == RivalSelfCheckRules.OptionalTarget);
            facts.Patches.Add(new RivalPatchLine { Target = RivalSelfCheckRules.OptionalTarget, Applied = false, Optional = true, Detail = "method not found" });

            RivalSelfCheckReport report = RivalSelfCheckRules.Run(facts);

            Assert.True(report.IsOk, "an optional patch is not a problem");
            Assert.False(report.IsFatal);
            Assert.Equal("self-check OK (11 patches, 7 archetypes, 13 assignments, 5 events)", report.Summary());
            Assert.Equal(1, report.Warnings.Count);
            Assert.True(report.Warnings[0].Contains("AcceptableFailureRate") && report.Warnings[0].Contains("method not found"), report.Warnings[0]);
            Assert.Equal("missing targets: AIBehaviourCreation.AcceptableFailureRate", report.MissingLine());
        }

        [Test]
        public void ARequiredTargetThatIsNotInTheGame_IsNamed_AndSwitchesTheFeatureOff()
        {
            RivalCheckFacts facts = Healthy();
            facts.Patches.RemoveAll(p => p.Target == "AIMath.GetClientScores");
            facts.Patches.Add(new RivalPatchLine { Target = "AIMath.GetClientScores", Applied = false, Detail = "no method AIMath.GetClientScores" });

            RivalSelfCheckReport report = RivalSelfCheckRules.Run(facts);

            Assert.False(report.IsOk);
            Assert.True(report.IsFatal);
            Assert.Equal("self-check FAILED (11 patches, 7 archetypes, 13 assignments, 5 events); the feature is switched off", report.Summary());
            Assert.Equal("missing targets: AIMath.GetClientScores", report.MissingLine());
            Assert.Equal(1, report.Problems.Count);
            Assert.True(report.Problems[0].Contains("AIMath.GetClientScores") && report.Problems[0].Contains("is not applied") && report.Problems[0].Contains("no method"), report.Problems[0]);
        }

        [Test]
        public void SeveralMissingTargets_AreOneList_RequiredOnesFirst()
        {
            RivalCheckFacts facts = Healthy();
            facts.Patches.RemoveAll(p => p.Target == "AICompany.Initialize" || p.Target == "Company.AddTechnology" || p.Target == RivalSelfCheckRules.OptionalTarget);

            RivalSelfCheckReport report = RivalSelfCheckRules.Run(facts);

            Assert.True(report.IsFatal);
            Assert.Equal("missing targets: AICompany.Initialize, Company.AddTechnology, AIBehaviourCreation.AcceptableFailureRate", report.MissingLine());
            Assert.True(report.Problems.All(p => p.Contains("is not declared by the feature")), string.Join("; ", report.Problems));
            Assert.Equal(9, report.Patches);
        }

        [Test]
        public void ATargetWithTwoPatches_IsInTheGameOnlyWhenBothAre()
        {
            RivalCheckFacts facts = Healthy();
            int index = facts.Patches.FindLastIndex(p => p.Target == "AIBehaviourMarket.OnTick");
            facts.Patches[index] = new RivalPatchLine { Target = "AIBehaviourMarket.OnTick", Applied = false, Detail = "postfix failed" };

            RivalSelfCheckReport report = RivalSelfCheckRules.Run(facts);

            Assert.True(report.IsFatal);
            Assert.Equal(11, report.Patches, "the target is counted out: one of its two patches is not in the game");
            Assert.Equal("missing targets: AIBehaviourMarket.OnTick", report.MissingLine());
        }

        [Test]
        public void APatchOnAddCpu_IsAProblem_TheReleaseIsTheCoresEvent()
        {
            RivalCheckFacts facts = Healthy();
            facts.Patches.Add(new RivalPatchLine { Target = "AICompany.AddCpu", Applied = true });
            facts.Patches.Add(new RivalPatchLine { Target = "Company.AddCpu", Applied = true });

            RivalSelfCheckReport report = RivalSelfCheckRules.Run(facts);

            Assert.False(report.IsOk);
            Assert.False(report.IsFatal, "wrong, but the game is not in danger");
            Assert.Equal(2, report.Problems.Count);
            Assert.True(report.Problems.All(p => p.Contains("AddCpu") && p.Contains("ModEvents.CpuReleased")));
            Assert.Equal(12, report.Patches, "and it is not counted");
        }

        [Test]
        public void ATargetOutsideTheDesign_IsAWarning()
        {
            RivalCheckFacts facts = Healthy();
            facts.Patches.Add(new RivalPatchLine { Target = "Market.CalculateRatings", Applied = true });

            RivalSelfCheckReport report = RivalSelfCheckRules.Run(facts);

            Assert.True(report.IsOk);
            Assert.Equal(12, report.Patches);
            Assert.Equal(1, report.Warnings.Count);
            Assert.True(report.Warnings[0].Contains("Market.CalculateRatings") && report.Warnings[0].Contains("design"));
        }

        [Test]
        public void WithoutAnArchetype_TheFeatureIsSwitchedOff()
        {
            RivalCheckFacts facts = Healthy();
            facts.Archetypes = 0;
            facts.CatalogUsable = false;

            RivalSelfCheckReport report = RivalSelfCheckRules.Run(facts);

            Assert.True(report.IsFatal);
            Assert.True(report.Problems.Any(p => p.Contains("no usable archetype")), string.Join("; ", report.Problems));
            Assert.True(report.Summary().StartsWith("self-check FAILED (12 patches, 0 archetypes, 13 assignments"), report.Summary());
        }

        [Test]
        public void NoAssignments_IsAWarning_TheFallbackIsGiven()
        {
            RivalCheckFacts facts = Healthy();
            facts.Assignments = 0;

            RivalSelfCheckReport report = RivalSelfCheckRules.Run(facts);

            Assert.True(report.IsOk);
            Assert.Equal(1, report.Warnings.Count);
            Assert.True(report.Summary().Contains("0 assignments"));
        }

        [Test]
        public void ContentThatWasNotRead_IsAProblem_NotAFatalOne()
        {
            RivalCheckFacts facts = Healthy();
            facts.ContentLoaded = false;

            RivalSelfCheckReport report = RivalSelfCheckRules.Run(facts);

            Assert.False(report.IsOk);
            Assert.False(report.IsFatal);
        }

        // ---- the event system ----

        [Test]
        public void WithoutTheEventSystem_NothingIsFatal_AndTheFiveEventsAreCountedFromThePack()
        {
            RivalCheckFacts facts = Healthy();
            facts.EventSystemActive = false;
            facts.RegisteredEvents.Clear();
            facts.RegisteredEffects.Clear();

            RivalSelfCheckReport report = RivalSelfCheckRules.Run(facts);

            Assert.True(report.IsOk);
            Assert.False(report.IsFatal);
            Assert.Equal("self-check OK (12 patches, 7 archetypes, 13 assignments, 5 events)", report.Summary());
            Assert.Equal(1, report.Warnings.Count);
            Assert.True(report.Warnings[0].Contains("event system is not active") && report.Warnings[0].Contains("no reaction is planned"), report.Warnings[0]);
        }

        [Test]
        public void AnEventTheEngineDoesNotKnow_IsAProblem_AndIsNotCounted()
        {
            RivalCheckFacts facts = Healthy();
            facts.RegisteredEvents.Remove(ReactionTags.EventPoach);

            RivalSelfCheckReport report = RivalSelfCheckRules.Run(facts);

            Assert.False(report.IsOk);
            Assert.False(report.IsFatal);
            Assert.Equal(4, report.Events);
            Assert.True(report.Problems.Any(p => p.Contains("rival.poach")), string.Join("; ", report.Problems));
            Assert.True(report.Summary().StartsWith("self-check FAILED (12 patches, 7 archetypes, 13 assignments, 4 events)"));
        }

        [Test]
        public void AnEffectTypeTheEngineDoesNotKnow_IsAProblem()
        {
            RivalCheckFacts facts = Healthy();
            facts.RegisteredEffects.Remove(ReactionTags.EffectPriceCut);

            RivalSelfCheckReport report = RivalSelfCheckRules.Run(facts);

            Assert.False(report.IsOk);
            Assert.True(report.Problems.Any(p => p.Contains("RivalPriceCut")));
            Assert.Equal(5, report.Events);
        }

        [Test]
        public void APredicateTheCoreDoesNotKnow_AndAMissingString_AreWarnings()
        {
            RivalCheckFacts facts = Healthy();
            facts.KnownPredicates.Remove(RivalPredicateNames.AttitudeAtMost);
            facts.MissingStrings.Add("rival.window.title");

            RivalSelfCheckReport report = RivalSelfCheckRules.Run(facts);

            Assert.True(report.IsOk);
            Assert.Equal(2, report.Warnings.Count);
            Assert.True(report.Warnings.Any(w => w.Contains("rival.attitudeAtMost")));
            Assert.True(report.Warnings.Any(w => w.Contains("rival.window.title")));
        }

        [Test]
        public void NoFacts_IsAProblemOfItsOwn_NotACrash()
        {
            RivalSelfCheckReport report = RivalSelfCheckRules.Run(null);

            Assert.False(report.IsOk);
            Assert.False(report.IsFatal);
        }

        // ---- the table of the design against the sources ----

        [Test]
        public void TheTableOfTheRules_IsTheTwelveTargetsOfTheDesign()
        {
            Assert.Equal(11, RivalSelfCheckRules.RequiredTargets.Length);
            Assert.Equal(11, RivalSelfCheckRules.RequiredTargets.Distinct().Count());
            Assert.Equal(12, RivalSelfCheckRules.TargetCount);
            Assert.False(RivalSelfCheckRules.IsRequired(RivalSelfCheckRules.OptionalTarget));
            Assert.True(RivalSelfCheckRules.IsDesignTarget(RivalSelfCheckRules.OptionalTarget));
            Assert.True(RivalSelfCheckRules.IsDesignTarget("AICompany.Initialize"));
            Assert.False(RivalSelfCheckRules.IsDesignTarget("AICompany.AddCpu"));
            Assert.True(RivalSelfCheckRules.IsForbidden("AICompany.AddCpu") && RivalSelfCheckRules.IsForbidden("Company.AddCpu"));
            Assert.False(RivalSelfCheckRules.IsForbidden(null));
            Assert.Equal(5, ReactionTags.EventIds.Length);
            Assert.Equal(5, ReactionTags.EffectTypes.Length);
        }

        [Test]
        public void TheSources_DeclareExactlyTheTargetsOfTheTable_WithTheNamesTheRegistryReports()
        {
            // the registry names a target <declaring type>.<method>; the sources spell the type through an alias (using GameMarket = ...AIBehaviourMarket;)
            string folder = FeatureFolder();
            var targets = new HashSet<string>();
            int declarations = 0;
            var declaration = new Regex(@"\[ModPatch\(\s*typeof\(\s*(\w+)\s*\)\s*,\s*""(\w+)""", RegexOptions.Compiled);
            var alias = new Regex(@"using\s+(\w+)\s*=\s*[\w.]*?(\w+)\s*;", RegexOptions.Compiled);

            foreach (string path in SourceScan.Files(folder, ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                var names = new Dictionary<string, string>();
                foreach (Match match in alias.Matches(code)) names[match.Groups[1].Value] = match.Groups[2].Value;

                foreach (Match match in declaration.Matches(code))
                {
                    string type = match.Groups[1].Value;
                    string resolved;
                    targets.Add((names.TryGetValue(type, out resolved) ? resolved : type) + "." + match.Groups[2].Value);
                    declarations++;
                }
            }

            var expected = new HashSet<string>(RivalSelfCheckRules.RequiredTargets) { RivalSelfCheckRules.OptionalTarget };
            Assert.Equal(14, declarations, "14 patch methods: the prefix and the postfix of OnTick, the prefix and the finalizer of UpdateScores count twice");
            Assert.True(expected.SetEquals(targets), "expected " + string.Join(", ", expected.OrderBy(t => t)) + " but the sources declare " + string.Join(", ", targets.OrderBy(t => t)));
            Assert.False(targets.Any(RivalSelfCheckRules.IsForbidden), "no patch on AddCpu");
        }

        // ---- the game side of the self-check ----

        [Test]
        public void TheSelfCheck_RunsOnGameReady_WaitsForTheEventSystem_AndSwitchesOffOnlyOnAFatalVerdict()
        {
            string check = Code("Diagnostics", "RivalSelfCheck.cs");
            string feature = Code("RivalPersonalitiesFeature.cs");

            Assert.True(Regex.IsMatch(check, @"ModLog\.Info\(Feature, report\.Summary\(\)\)"), "one INFO line when all is well");
            Assert.True(Regex.IsMatch(check, @"if \(report\.IsFatal\) SwitchOff\(report\);"));
            Assert.True(Regex.IsMatch(check, @"ModFeatures\.MarkUnavailable\(Feature,"), "the core's own way to switch a feature off");
            Assert.True(Regex.IsMatch(check, @"foreach \(PatchReport report in reports\)[\s\S]*?report\.FeatureId != Feature"), "the patches of this feature in the registry of the core");
            Assert.True(Regex.IsMatch(check, @"report\.Status == PatchStatus\.Applied"));
            Assert.True(Regex.IsMatch(check, @"definition\.packId == ReactionTags\.PackId"), "the events of the pack in the engine");
            Assert.True(Regex.IsMatch(check, @"stage != ""day"" && facts\.EventSystemActive && facts\.RegisteredEvents\.Count == 0"), "the engine has not read the pack yet: wait a day");
            Assert.True(Regex.IsMatch(check, @"catch \(Exception ex\)"), "it never throws");

            Assert.True(feature.Contains("ModHooks.Guard(FeatureId, \"ModTicks.GameReady.selfCheck\", () => RivalSelfCheck.OnGameReady(this))"));
            Assert.True(feature.Contains("ModHooks.Guard(FeatureId, \"DayPassed.selfCheck\", () => RivalSelfCheck.OnDay(this))"));
            Assert.True(feature.Contains("ModHooks.Guard(FeatureId, \"feature/selfCheck\", () => RivalSelfCheck.OnEnabled(this))"));
        }

        [Test]
        public void OnlyTheSelfCheck_SwitchesTheFeatureOff()
        {
            foreach (string path in SourceScan.Files(FeatureFolder(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                if (path.EndsWith("RivalSelfCheck.cs", StringComparison.Ordinal)) continue;
                Assert.False(code.Contains("MarkUnavailable"), SourceScan.Relative(FeatureFolder(), path) + " switches the feature off");
            }
        }

        // ---- the isolation of failures ----

        [Test]
        public void EveryPatchBody_IsInsideItsOwnTry_AndReportsToTheCore()
        {
            var bodies = PatchBodies();
            Assert.Equal(14, bodies.Count);

            foreach (KeyValuePair<string, string> pair in bodies)
            {
                string name = pair.Key;
                string body = pair.Value;
                Assert.True(Regex.IsMatch(body, @"\btry\b"), name + ": a try");
                Assert.True(Regex.IsMatch(body, @"catch \(Exception e\)"), name + ": catches everything");
                Assert.True(Regex.IsMatch(body, @"ModHooks\.ReportFault\(Feature, \w+, e\)"), name + ": reports with the site");
                Assert.True(Regex.IsMatch(body, @"RivalFaultProbe\.Trip\(\w+\)"), name + ": the artificial failure of the debug build can be injected");
                Assert.False(Regex.IsMatch(body, @"\bthrow\b"), name + ": nothing is thrown at the game");
            }
        }

        [Test]
        public void EveryPatchBody_ThatCanSkip_AsksTheCoreFirst_ExceptTheFinalizerWhichMustAlwaysClose()
        {
            foreach (KeyValuePair<string, string> pair in PatchBodies())
            {
                if (pair.Key.EndsWith(".ScopeExit", StringComparison.Ordinal))
                {
                    Assert.False(pair.Value.Contains("ModHooks.CanRun"), "the finalizer closes the scope even when the feature is Faulted");
                    continue;
                }
                Assert.True(pair.Value.Contains("ModHooks.CanRun(Feature,"), pair.Key + ": a Faulted or Unavailable feature does not run");
            }
        }

        [Test]
        public void TheFinalizer_ClosesTheScope_BeforeTheArtificialFailure_AndPassesTheGamesExceptionOn()
        {
            string patches = Code("Negotiation", "NegotiationPatches.cs");
            string exit = patches.Substring(patches.IndexOf("private static Exception ScopeExit", StringComparison.Ordinal));

            Assert.True(Regex.IsMatch(exit, @"NegotiationScope\.Exit\(\);\s*RivalFaultProbe\.Trip\(ExitSite\);"), "close first");
            Assert.True(Regex.IsMatch(exit, @"return __exception;"), "the exception of the window goes on unchanged");
        }

        [Test]
        public void TheArtificialFailure_ExistsOnlyInTheDebugBuild_AndNeedsTheFlag()
        {
            string probe = SourceScan.ReadText(Path.Combine(FeatureFolder(), "Diagnostics", "RivalFaultProbe.cs"));
            string code = SourceScan.Strip(probe, false);

            Assert.True(Regex.IsMatch(code, @"\[Conditional\(""PTMOD_DEBUG""\)\]\s*public static void Trip\(string site\)"), "the call is not even compiled in a release build");
            Assert.True(Regex.IsMatch(code, @"if \(ModSettings\.Debug\(Flag\)\) throw new InvalidOperationException"), "only with the flag, which is false in a release build");
            Assert.Equal("rival-personalities.debug.throwInPatch", GetConst(code, "Flag"));
        }

        [Test]
        public void TheFeature_KeepsNoCounterOfItsOwnFailures_TheCoreCountsThemAgainstItsThreshold()
        {
            // a field that counts failures, or a limit of failures, would be a second circuit breaker beside ModHooks (hooks.faultThreshold, 5 a session)
            var field = new Regex(@"\b(private|internal|public|protected)\s+(static\s+)?(readonly\s+)?(int|long|uint)\s+_?\w*(fault|error|failure)\w*\s*(;|=)", RegexOptions.IgnoreCase);
            var limit = new Regex(@"\b\w*(fault|failure|error)\w*(threshold|limit|max)\w*\b", RegexOptions.IgnoreCase);
            var offenders = new List<string>();
            foreach (string path in SourceScan.Files(FeatureFolder(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                foreach (Match match in field.Matches(code))
                {
                    // the one exception is the RESULT of one monthly pass (how many of its six steps failed), reported once in the log and not kept between passes
                    if (path.EndsWith("MonthlyEvaluatorCore.cs", StringComparison.Ordinal) && match.Value.Contains("Failures")) continue;
                    offenders.Add(SourceScan.Relative(FeatureFolder(), path) + ": " + match.Value);
                }
                foreach (Match match in limit.Matches(code)) offenders.Add(SourceScan.Relative(FeatureFolder(), path) + ": " + match.Value);
            }
            Assert.Equal(0, offenders.Count, string.Join(", ", offenders));
        }

        // ---- the measurements ----

        [Test]
        public void TheDay_IsTheAverageOverAllDays_EvenThoseWithNothingMeasured()
        {
            var book = new RivalTimingBook();
            var day = new DateTime(1984, 3, 1);
            for (int i = 0; i < 31; i++)
            {
                book.NoteDay(day.AddDays(i));
                if (i == 0)
                {
                    book.Add(day, 1.0);
                    book.Add(day, 0.5);   // two stretches of one day are one sample
                }
                if (i == 10) book.Add(day.AddDays(10), 3.1);
            }

            RivalTimingSummary summary = book.Take();

            Assert.Equal(31, summary.Days);
            Assert.Near(4.6, summary.TotalMs, 1e-9);
            Assert.Near(4.6 / 31, summary.AverageMs, 1e-9);
            Assert.Near(3.1, summary.MaxDayMs, 1e-9, "the dearest day");
            Assert.False(RivalTimingBook.IsOverBudget(summary));
        }

        [Test]
        public void TheBudgets_AreTheOnesOfNfr1()
        {
            Assert.Equal(0.3, RivalTimingBook.DailyBudgetMs);
            Assert.Equal(3.0, RivalTimingBook.MonthlyBudgetMs);
        }

        [Test]
        public void ADayOverTheBudget_IsFlagged_InTheLine()
        {
            var book = new RivalTimingBook();
            var day = new DateTime(1984, 3, 1);
            for (int i = 0; i < 10; i++)
            {
                book.NoteDay(day.AddDays(i));
                book.Add(day.AddDays(i), 0.5);
            }
            book.AddMonthly(1.25);

            RivalTimingSummary summary = book.Take();

            Assert.True(RivalTimingBook.IsOverDailyBudget(summary));
            Assert.False(RivalTimingBook.IsOverMonthlyBudget(summary));
            Assert.True(RivalTimingBook.IsOverBudget(summary));
            Assert.Equal("timing 1984-03: 10 day(s), 0.5 ms a day on average, worst day 0.5 ms (OVER THE BUDGET of 0.3 ms); monthly pass 1.25 ms (within 3 ms)",
                RivalTimingBook.Line("1984-03", summary));
        }

        [Test]
        public void AMonthWithinTheBudget_IsSaidSo()
        {
            var book = new RivalTimingBook();
            var day = new DateTime(1990, 6, 1);
            for (int i = 0; i < 30; i++)
            {
                book.NoteDay(day.AddDays(i));
                book.Add(day.AddDays(i), 0.12);
            }
            book.AddMonthly(2.0);
            book.AddMonthly(0.4);

            RivalTimingSummary summary = book.Take();

            Assert.Equal(2, summary.MonthlyPasses);
            Assert.Near(2.0, summary.MaxMonthlyMs, 1e-9, "the slowest pass");
            Assert.False(RivalTimingBook.IsOverBudget(summary));
            Assert.Equal("timing 1990-06: 30 day(s), 0.12 ms a day on average, worst day 0.12 ms (within 0.3 ms); monthly pass 2 ms (within 3 ms)", RivalTimingBook.Line("1990-06", summary));
        }

        [Test]
        public void AMonthlyPassOverThreeMilliseconds_IsFlagged()
        {
            var book = new RivalTimingBook();
            book.AddMonthly(3.5);

            RivalTimingSummary summary = book.Take();

            Assert.True(RivalTimingBook.IsOverMonthlyBudget(summary));
            Assert.False(RivalTimingBook.IsOverDailyBudget(summary), "no days: nothing to judge");
            Assert.True(RivalTimingBook.Line("1984-03", summary).Contains("monthly pass 3.5 ms (OVER THE BUDGET of 3 ms)"));
        }

        [Test]
        public void Take_StartsTheBookAgain_AndNonsenseIsIgnored()
        {
            var book = new RivalTimingBook();
            var day = new DateTime(1984, 3, 1);
            book.NoteDay(day);
            book.Add(day, double.NaN);
            book.Add(day, -1.0);
            book.Add(day, double.PositiveInfinity);
            book.AddMonthly(double.NaN);
            book.AddMonthly(-2.0);

            RivalTimingSummary first = book.Take();
            RivalTimingSummary second = book.Take();

            Assert.Equal(1, first.Days);
            Assert.Equal(0.0, first.TotalMs);
            Assert.Equal(0, first.MonthlyPasses);
            Assert.Equal(0, second.Days);
            Assert.Equal(0.0, second.AverageMs, "no days, no division by zero");
            Assert.Equal("timing 1984-03: 0 day(s)", RivalTimingBook.Line("1984-03", second));
        }

        [Test]
        public void Clear_ForgetsTheDaysOfTheLastGame()
        {
            var book = new RivalTimingBook();
            book.Add(new DateTime(1984, 3, 1), 1.0);
            book.AddMonthly(1.0);

            book.Clear();

            RivalTimingSummary summary = book.Take();
            Assert.Equal(0, summary.Days);
            Assert.Equal(0, summary.MonthlyPasses);
        }

        [Test]
        public void TheScope_TimesOnlyTheOutermostStretch()
        {
            var scope = new RivalTimingScope();
            long elapsed;

            Assert.True(scope.Enter(100L), "the outermost");
            Assert.False(scope.Enter(150L), "inside it: not timed");
            Assert.Equal(2, scope.Depth);
            Assert.False(scope.Leave(180L, out elapsed), "the inner one ends");
            Assert.Equal(0L, elapsed);
            Assert.True(scope.Leave(300L, out elapsed), "the outer one ends");
            Assert.Equal(200L, elapsed, "from 100 to 300, the inner stretch is inside it");
            Assert.Equal(0, scope.Depth);
        }

        [Test]
        public void TheScope_SurvivesAnUnpairedLeave_AReset_AndAClockThatWentBack()
        {
            var scope = new RivalTimingScope();
            long elapsed;

            Assert.False(scope.Leave(50L, out elapsed), "nothing is open");
            Assert.Equal(0, scope.Depth);

            scope.Enter(100L);
            scope.Enter(110L);
            scope.Reset();
            Assert.Equal(0, scope.Depth);

            scope.Enter(500L);
            Assert.True(scope.Leave(400L, out elapsed));
            Assert.Equal(0L, elapsed, "never negative");
        }

        // ---- the timings in the sources ----

        [Test]
        public void EveryMeasuredStretch_IsClosedInAFinally_AndOnlyTheOutermostEntrancesAreMeasured()
        {
            int begins = 0;
            int ends = 0;
            foreach (string path in SourceScan.Files(FeatureFolder(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                if (path.EndsWith("RivalTimings.cs", StringComparison.Ordinal)) continue;

                int fileBegins = Regex.Matches(code, @"RivalTimings\.Begin\(\)").Count;
                int fileEnds = Regex.Matches(code, @"finally\s*\{[^{}]*RivalTimings\.End\(started\);[^{}]*\}").Count;
                Assert.Equal(fileBegins, fileEnds, SourceScan.Relative(FeatureFolder(), path) + ": every Begin has its End in a finally");
                begins += fileBegins;
                ends += fileEnds;
            }

            // the five daily patches (Creation, Funding, Market prefix and postfix, Caution), the three of the player's moves, the day, the CPU release, the mail
            Assert.Equal(11, begins);
            Assert.Equal(begins, ends);
        }

        [Test]
        public void TheMeasuring_IsForTheDebugBuildAndTheVerboseLog_AndTheLineGoesToTheLogOnceAMonth()
        {
            string timings = Code("Diagnostics", "RivalTimings.cs");
            string feature = Code("RivalPersonalitiesFeature.cs");
            string monthly = Code("Triggers", "MonthlyEvaluator.cs");

            Assert.True(Regex.IsMatch(timings, @"return BuildInfo\.IsDebug \|\| ModSettings\.VerboseLog;"));
            Assert.True(Regex.IsMatch(timings, @"if \(!Enabled\) return 0L;"), "off: Begin costs nothing");
            Assert.True(Regex.IsMatch(timings, @"Stopwatch\.GetTimestamp\(\)"));
            Assert.True(Regex.IsMatch(timings, @"if \(RivalTimingBook\.IsOverBudget\(summary\)\) ModLog\.Warn\(Feature, line\);\s*else ModLog\.Info\(Feature, line\);"));
            Assert.True(Regex.IsMatch(feature, @"_monthly\.Run\(\);\s*ModHooks\.Guard\(FeatureId, ""LateMonthPassed\.timings"", RivalTimings\.OnMonth\);"), "after the monthly pass, once a month");
            Assert.True(Regex.IsMatch(feature, @"ModHooks\.Guard\(FeatureId, ""feature/enabled\.timings"", RivalTimings\.Reset\)") && feature.Contains("Always(\"feature/disabled.timings\", RivalTimings.Reset)"));
            Assert.True(Regex.IsMatch(monthly, @"RivalTimings\.AddMonthly\(LastMilliseconds\);"), "the monthly pass is compared with 3 ms");
        }

        [Test]
        public void TheDailyPatches_AreTimed()
        {
            foreach (string file in new[]
            {
                @"Behaviour\CreationAndResearchPatches.cs", @"Behaviour\MarketPatches.cs", @"Behaviour\CautionPatch.cs", @"Triggers\TriggerPatches.cs",
            })
            {
                string code = Code(file.Split('\\'));
                Assert.True(code.Contains("RivalTimings.Begin()") && code.Contains("RivalTimings.End(started)"), file);
            }
        }

        // ---- helpers ----

        private static string FeatureFolder()
        {
            string folder = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "RivalPersonalities");
            Assert.True(Directory.Exists(folder), "no feature folder " + folder);
            return folder;
        }

        private static string Code(params string[] parts)
        {
            string path = Path.Combine(new[] { FeatureFolder() }.Concat(parts).ToArray());
            Assert.True(File.Exists(path), "missing " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        private static string GetConst(string code, string name)
        {
            Match match = Regex.Match(code, @"const string " + name + @" = ""([^""]*)""");
            Assert.True(match.Success, "no const " + name);
            return match.Groups[1].Value;
        }

        /// <summary>The body of every patch method of the feature: "File.Method" to its text.</summary>
        private static Dictionary<string, string> PatchBodies()
        {
            var bodies = new Dictionary<string, string>();
            foreach (string path in SourceScan.Files(FeatureFolder(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                foreach (Match match in Regex.Matches(code, @"\[ModPatch\(.*?\)\]\s*private static \w+ (\w+)\(", RegexOptions.Singleline))
                {
                    int open = code.IndexOf('{', match.Index + match.Length);
                    int depth = 0;
                    int end = open;
                    for (int i = open; i < code.Length; i++)
                    {
                        if (code[i] == '{') depth++;
                        else if (code[i] == '}' && --depth == 0) { end = i; break; }
                    }
                    bodies[Path.GetFileNameWithoutExtension(path) + "." + match.Groups[1].Value] = code.Substring(open, end - open + 1);
                }
            }
            return bodies;
        }
    }
}
