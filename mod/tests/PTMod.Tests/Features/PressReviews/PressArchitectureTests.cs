using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.PressReviews
{
    /// <summary>
    /// The rules of the design that are about what the code of press-reviews does not contain: no classifier of segments of its own (the rule belongs to the
    /// core), no patch on the methods that raise <c>CpuReleased</c>, no direct subscription to the game's date or save, no Unity or game in the pure logic.
    /// The sources are read from the tree; comments and the text of strings are not code and are not looked at.
    /// </summary>
    public class PressArchitectureTests
    {
        private static string FeatureDir()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "PressReviews");
        }

        private static List<KeyValuePair<string, string>> Sources(string subfolder, bool keepStrings)
        {
            string root = subfolder == null ? FeatureDir() : Path.Combine(FeatureDir(), subfolder);
            var result = new List<KeyValuePair<string, string>>();
            foreach (string path in SourceScan.Files(root, ".cs"))
                result.Add(new KeyValuePair<string, string>(SourceScan.Relative(FeatureDir(), path), SourceScan.Strip(SourceScan.ReadText(path), !keepStrings)));
            Assert.True(result.Count > 0, "no sources under " + root);
            return result;
        }

        private static void AssertNone(Regex rule, string why, string subfolder = null, bool keepStrings = false)
        {
            var violations = new List<string>();
            foreach (KeyValuePair<string, string> file in Sources(subfolder, keepStrings))
            {
                foreach (Match match in rule.Matches(file.Value))
                    violations.Add(file.Key + ":" + SourceScan.LineOf(file.Value, match.Index) + ": " + match.Value.Trim());
            }
            Assert.True(violations.Count == 0, why + ":\n  " + string.Join("\n  ", violations));
        }

        [Test]
        public void ThereIsNoClassifierOfSegmentsInTheFeature()
        {
            // FR-2 item 1: the rule belongs to the core (ModSegments, SegmentRule, segments.priceBand); the feature only calls it.
            AssertNone(new Regex(@"\bSegmentInference\b"), "no type SegmentInference");
            AssertNone(new Regex(@"\b(?:MidRangeBudget|LowEndBudget|IndustriesBudget|HighEndBudget)\b|\bpriceBand\b|\bPriceBand\b|\bSegmentRule\b"),
                "the feature must not read the budgets of the market or apply the price band itself");
        }

        [Test]
        public void ThePlayersAndTheAIsSegment_ComeFromTheCoreAndFromTheStrategy()
        {
            string text = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureDir(), "Game", "CpuSnapshotFactory.cs")), true);
            Assert.True(text.Contains("ModSegments.FromSales"), "the player's CPU: ModSegments.FromSales over the sales of the wait");
            Assert.True(text.Contains("ModSegments.Classify"), "no sales or the instant mode: ModSegments.Classify");
            Assert.True(text.Contains("Strategy") && text.Contains("TargetMarket"), "an AI CPU: its target market");
        }

        [Test]
        public void TheFeaturePatchesNeitherCompanyAddCpuNorAICompanyAddCpu()
        {
            // The release comes from ModEvents.CpuReleased (P20 of the core); a patch of its own would be refused by ModHooks.
            AssertNone(new Regex(@"\bAddCpu\b"), "no patch on AddCpu");
        }

        [Test]
        public void TheFeatureDoesNotSubscribeToTheGamesDateOrSaveItself()
        {
            // Ticks go through ctx.OnTick, the save through ctx.SaveParticipant, a new game through ModSave: no direct subscriptions.
            AssertNone(new Regex(@"\b(?:DateController|SaveHandler|Player)\s*\.\s*(?:Instance\s*\.\s*)?On\w+\s*\+="),
                "no direct subscription to DateController, SaveHandler or Player.OnNewGame");
        }

        [Test]
        public void TheReleaseComesFromTheEventOfTheCore_AndATransferIsNotListenedTo()
        {
            // TSK-007: one subscription to ModEvents.CpuReleased, made in Register; CpuTransferred makes no review.
            string feature = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureDir(), "PressReviewsFeature.cs")), true);
            Assert.Equal(1, Regex.Matches(feature, @"ModEvents\s*\.\s*CpuReleased\s*\+=").Count, "one subscription to CpuReleased");
            AssertNone(new Regex(@"CpuTransferred"), "a transfer of a CPU is not a release: no review is made for it");

            string register = Between(feature, "public void Register(FeatureContext ctx)", "public void OnEnabled()");
            Assert.True(register.Contains("ModEvents.CpuReleased +="), "the subscription is made once, in Register");
            Assert.True(Regex.IsMatch(register, @"ctx\s*\.\s*OnTick\s*\(\s*TickPhase\s*\.\s*DayPassed"), "the day of the queue comes through ctx.OnTick(DayPassed)");
        }

        [Test]
        public void TheQueueFiltersTheReleasesInThePureLogic()
        {
            // The rules of TSK-007 (fast-forward, variants, not released, seen before) are ReviewQueue.Admit, the scheduler only asks it.
            string scheduler = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureDir(), "Game", "ReviewScheduler.cs")), false);
            Assert.True(Regex.IsMatch(scheduler, @"ReviewQueue\s*\.\s*Admit\s*\("), "the scheduler asks ReviewQueue.Admit");
            Assert.True(Regex.IsMatch(scheduler, @"duringFastForward"), "and passes the flag of the fast-forward");
            Assert.True(Regex.IsMatch(scheduler, @"ModHooks\s*\.\s*Guard\s*\(") && scheduler.Contains("scheduler/release"), "the handler of the event is guarded");
            Assert.True(scheduler.Contains("ModRandom.Stream"), "the wait is drawn from the stream of the feature");
            Assert.True(scheduler.Contains("SoldLastDayByHighEnd") && scheduler.Contains("SoldLastDayByIndustries"), "the sales of the day by segment are added up");
        }

        [Test]
        public void TheFeatureHasNoReputationDrift()
        {
            // TSK-008: the monthly pull of the reputation to 50 is the core's (ModReputation.Drift, driven by the core). The feature neither calls it nor
            // writes the old reason press.drift to the history.
            AssertNone(new Regex(@"press\.drift|ModReputation\s*\.\s*Drift\s*\("), "no drift of the reputation in the feature", null, true);
        }

        [Test]
        public void EffectMath_DoesNotMentionDrift()
        {
            string text = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureDir(), "Logic", "EffectMath.cs")), false);
            Assert.False(text.IndexOf("Drift", System.StringComparison.OrdinalIgnoreCase) >= 0, "EffectMath has no drift");
        }

        [Test]
        public void TheProviderOfPopularityGain_IsRegisteredOnceInRegister_AndNotTouchedWhenTheFeatureIsSwitched()
        {
            string feature = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureDir(), "PressReviewsFeature.cs")), true);
            string register = Between(feature, "public void Register(FeatureContext ctx)", "public void OnEnabled()");
            Assert.True(Regex.IsMatch(register, @"ModModifiers\s*\.\s*RegisterReasonedProvider\s*\(\s*FeatureId\s*,\s*ModChannels\s*\.\s*PopularityGain"),
                "ModModifiers.RegisterReasonedProvider(FeatureId, ModChannels.PopularityGain, ...) in Register");

            int registrations = 0;
            foreach (KeyValuePair<string, string> file in Sources(null, true))
                registrations += Regex.Matches(file.Value, @"\bRegister(?:Reasoned)?Provider\b").Count;
            Assert.Equal(1, registrations, "the provider is registered in exactly one place");
            AssertNone(new Regex(@"ModModifiers\s*\.\s*(?:RemoveAll|Remove|Add)\s*\("), "the feature keeps no entries of its own: the contribution is the provider");

            string tail = feature.Substring(feature.IndexOf("public void OnEnabled()", System.StringComparison.Ordinal));
            Assert.False(tail.Contains("ModModifiers.Register"), "OnEnabled and OnDisabled do not recreate the provider");
            Assert.False(tail.Contains("ModModifiers.Remove"), "and do not remove it");
        }

        [Test]
        public void TheFeaturePatchesNoFormulaOfTheMarket_ItsEffectIsTheProviderOfTheChannel()
        {
            // TSK-020: the one exception is the optional market column (UI/MarketColumnPatch.cs, a postfix on HardwareWindow.ShowMarket, declared only when the
            // parameter marketColumn is on at the start); every other file of the feature has no [ModPatch].
            var patch = new Regex(@"\[\s*ModPatch\s*\(");
            var violations = new List<string>();
            foreach (KeyValuePair<string, string> file in Sources(null, false))
            {
                if (file.Key.Replace('\\', '/') == "UI/MarketColumnPatch.cs") continue;
                foreach (Match match in patch.Matches(file.Value)) violations.Add(file.Key + ":" + SourceScan.LineOf(file.Value, match.Index));
            }
            Assert.True(violations.Count == 0, "no patch of its own: the effects go through the channel popularity.gain and the events of the core\n  " + string.Join("\n  ", violations));
            AssertNone(new Regex(@"\bUpdatePopularities\b|\bCalculateRatings\b"), "the formulas of the market are not touched");
        }

        [Test]
        public void TheReputationChangeUsesTheReasonOfThePress_AndModReputationAdd()
        {
            string effects = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureDir(), "Effects", "ReviewEffects.cs")), true);
            Assert.True(Regex.IsMatch(effects, @"ModReputation\s*\.\s*Add\s*\(\s*company\s*,\s*delta\s*,\s*EffectMath\s*\.\s*ReviewReason\s*\("), "ModReputation.Add(company, delta, press.review:<id>)");
            Assert.True(effects.Contains("ModModifiers.Invalidate(ModChannels.PopularityGain"), "the cached values of the channel are dropped when the reputation changes");
            Assert.True(effects.Contains("ModReputation.Changed") || SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureDir(), "PressReviewsFeature.cs")), true).Contains("ModReputation.Changed +="),
                "the feature listens to ModReputation.Changed");
        }

        private static string Between(string text, string from, string to)
        {
            int start = text.IndexOf(from, System.StringComparison.Ordinal);
            Assert.True(start >= 0, "not found: " + from);
            int end = text.IndexOf(to, start, System.StringComparison.Ordinal);
            Assert.True(end > start, "not found after it: " + to);
            return text.Substring(start, end - start);
        }

        [Test]
        public void TheEntryPointDeclaresItsPartsThroughTheContext()
        {
            string text = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureDir(), "PressReviewsFeature.cs")), true);
            Assert.True(Regex.IsMatch(text, @"\[ModFeature\]\s*public\s+sealed\s+class\s+PressReviewsFeature\s*:\s*IModFeature"), "[ModFeature] sealed class PressReviewsFeature : IModFeature");
            Assert.True(text.Contains("ctx.Param("), "the parameters are declared with ctx.Param");
            Assert.True(text.Contains("ctx.SaveParticipant("), "the section of the save is registered with ctx.SaveParticipant");
            Assert.True(text.Contains("PressSaveAdapter"), "and it is the PressSaveAdapter");
        }

        [Test]
        public void TheSaveAdapterWritesNoVersion()
        {
            // _schema is the core's: neither the adapter nor the state may name it.
            AssertNone(new Regex(@"_schema"), "the feature must not write or read _schema", null, true);
        }

        [Test]
        public void ThePureLogic_DoesNotReferenceUnityOrTheGame()
        {
            var engine = new Regex(@"\busing\s+(?:static\s+)?(?:global::)?(?:UnityEngine|UnityEditor|Unity|TMPro|ProcessorTycoon|HarmonyLib)\b"
                + @"|(?<![\w.])(?:global::)?(?:UnityEngine|UnityEditor|TMPro|HarmonyLib|ProcessorTycoon)(?=\s*\.\s*\w)");
            AssertNone(engine, "Features/PressReviews/Logic/** must compile without Unity, Harmony and the game", "Logic");
        }

        [Test]
        public void ThePureLogic_DoesNotUseTheNewerLanguageAndLibraryFeaturesTheTestBuildLacks()
        {
            // 0003-build-toolchain: no init/record/required/Index/Range/Span in code that the test build (net48) compiles.
            AssertNone(new Regex(@"\{\s*get\s*;\s*init\s*;|\brecord\s+(?:struct\s+|class\s+)?\w+\s*[(\{]|\brequired\s+(?:public|internal)|\bSpan<|\bMath\.Clamp\b|\bMathF\b|\bIndex\b\s+\w+\s*=|\bRange\b\s+\w+\s*="),
                "constructs that do not compile in the test build", "Logic");
        }
    }
}
