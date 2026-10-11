using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Features.FirstYearOnboarding.Logic;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.FirstYearOnboarding
{
    /// <summary>The members of the game as a table of the ones that are gone. Internal: the runner never discovers it.</summary>
    internal sealed class FakeMemberProbe : IGameMemberProbe
    {
        /// <summary><c>Class.Member</c> -> the reason it is missing.</summary>
        public readonly Dictionary<string, string> Missing = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary><c>Class.Member</c> of the members whose probe throws.</summary>
        public readonly HashSet<string> Throws = new HashSet<string>(StringComparer.Ordinal);

        public readonly List<string> Asked = new List<string>();

        public string Probe(GameMemberSpec member)
        {
            Asked.Add(member.Label);
            if (Throws.Contains(member.Label)) throw new InvalidOperationException("the probe broke");

            string reason;
            return Missing.TryGetValue(member.Label, out reason) ? reason : null;
        }
    }

    /// <summary>
    /// The self-check at start (TSK-016, NFR-2, NFR-4, FR-3 item 3): the rules that turn the findings into one line <c>self-check: OK</c> or a list, over fakes of the
    /// game's members, of the core's acceptance of the predicates and of the string tables; the list of keys the tutorial needs against the shipped tables and
    /// the sources; and the seams of the game layer (read from the source: the game and the core do not run in the test build).
    /// </summary>
    [Covers(typeof(SelfCheckReport), typeof(SelfCheckProblem), typeof(SelfCheckSeverity), typeof(GameMemberSpec), typeof(GameMemberKind), typeof(GameMembers),
        typeof(IGameMemberProbe), typeof(OnboardingSelfCheckInput), typeof(OnboardingSelfCheckRules), typeof(OnboardingKeys))]
    public class OnboardingSelfCheckTests
    {
        // ---- the setup that passes ----

        private static OnboardingSelfCheckInput Healthy(ShippedContext context, StepCatalog catalog, FakeMemberProbe probe)
        {
            return new OnboardingSelfCheckInput
            {
                Members = probe,
                PredicateRefusal = name => null,
                Catalog = catalog,
                HasRussianText = key => context.Tables.Contains("ru", key),
                RussianIsExact = true
            };
        }

        private static OnboardingSelfCheckInput Healthy(out FakeMemberProbe probe)
        {
            ShippedContext context = ShippedContext.Load();
            probe = new FakeMemberProbe();
            return Healthy(context, context.BuildCatalog(), probe);
        }

        private static string Texts(SelfCheckReport report)
        {
            return string.Join(" | ", report.Problems.Select(p => p.ToString()));
        }

        // ---- a clean start ----

        [Test]
        public void AHealthySetup_WritesOk()
        {
            FakeMemberProbe probe;
            OnboardingSelfCheckInput input = Healthy(out probe);

            SelfCheckReport report = OnboardingSelfCheckRules.Run(input);

            Assert.True(report.IsOk, Texts(report));
            Assert.Equal("self-check: OK", report.Summary, "the line the acceptance names");
            Assert.Equal(SelfCheckReport.OkLine, report.Summary);
            Assert.Equal(0, report.Lines().Count);
            Assert.Equal(GameMembers.All.Count, probe.Asked.Count, "every member was looked at");
        }

        [Test]
        public void TheShippedCatalog_NeedsTheKeysTheSelfCheckKnows_AndTheTablesHaveThemAll()
        {
            ShippedContext context = ShippedContext.Load();
            StepCatalog catalog = context.BuildCatalog();

            IList<string> keys = OnboardingKeys.All(catalog);

            Assert.True(keys.Count > 100, "the fixed keys and 130 keys of the tutorial, about: " + keys.Count);
            foreach (string key in keys) Assert.True(context.Tables.Contains("ru", key), "no Russian text for " + key);
        }

        // ---- the members of the game ----

        [Test]
        public void TheMemberList_HasEveryMemberTheAcceptanceNames()
        {
            var labels = new HashSet<string>(GameMembers.All.Select(m => m.Label), StringComparer.Ordinal);

            foreach (string expected in new[]
            {
                "CreatorManager.OpenCreatorWindowOfType", "DateController.ManualPause", "Market.get_HighEndBudget", "Market.get_MidRangeBudget",
                "Market.get_LowEndBudget", "Market.get_IndustriesBudget", "BankruptcyHandler.defaultTimeLimit"
            })
            {
                Assert.True(labels.Contains(expected), "TSK-016 names " + expected);
            }
            Assert.Equal(GameMembers.All.Count, labels.Count, "no member twice");
            Assert.True(GameMembers.All.All(m => !string.IsNullOrEmpty(m.Role)), "every member says what it is for");
        }

        [Test]
        public void TheMembersWithAFallback_AreTheOnesTheTutorialCanDoWithout()
        {
            Func<string, GameMemberSpec> find = label => GameMembers.All.Single(m => m.Label == label);

            Assert.Equal(SelfCheckSeverity.Warning, find("BankruptcyHandler.defaultTimeLimit").Severity, "the fallback of config.json (120)");
            Assert.Equal(SelfCheckSeverity.Warning, find("DateController.currentTimeSpeed").Severity, "without it no pause");
            Assert.Equal(SelfCheckSeverity.Warning, find("BankruptcyHandler.OnCompanyWentBankrupt").Severity, "the time limit still ends the tutorial");
            Assert.Equal(SelfCheckSeverity.Error, find("CreatorManager.OpenCreatorWindowOfType").Severity);
            Assert.Equal(SelfCheckSeverity.Error, find("DateController.ManualPause").Severity);
            Assert.Equal(SelfCheckSeverity.Error, find("Market.get_LowEndBudget").Severity);
        }

        [Test]
        public void AMissingMemberWithoutAFallback_IsAnError_ThatNamesTheMemberTheReasonAndTheUse()
        {
            FakeMemberProbe probe;
            OnboardingSelfCheckInput input = Healthy(out probe);
            probe.Missing["CreatorManager.OpenCreatorWindowOfType"] = "no such method";

            SelfCheckReport report = OnboardingSelfCheckRules.Run(input);

            Assert.False(report.IsOk);
            Assert.Equal(1, report.Errors);
            Assert.Equal(0, report.Warnings);
            string message = report.Problems.Single().Message;
            Assert.True(message.Contains("CreatorManager.OpenCreatorWindowOfType"), message);
            Assert.True(message.Contains("no such method"), message);
            Assert.True(message.Contains("buttons"), "what it is used for: " + message);
            Assert.Equal("self-check: 1 error(s), 0 warning(s)", report.Summary);
            Assert.Equal(OnboardingSelfCheckRules.AreaGame, report.Problems.Single().Area);
        }

        [Test]
        public void ADefaultTimeLimitThatIsGone_IsOnlyAWarning_WithTheFallbackNamed()
        {
            FakeMemberProbe probe;
            OnboardingSelfCheckInput input = Healthy(out probe);
            probe.Missing["BankruptcyHandler.defaultTimeLimit"] = "no such field";

            SelfCheckReport report = OnboardingSelfCheckRules.Run(input);

            Assert.Equal(0, report.Errors, "the tutorial goes on with the 120 days of config.json");
            Assert.Equal(1, report.Warnings);
            string message = report.Problems.Single().Message;
            Assert.True(message.Contains("defaultDaysFallback") && message.Contains("120"), message);
            Assert.False(report.HasErrors);
        }

        [Test]
        public void FourBudgetGettersThatAreGone_AreFourErrors()
        {
            FakeMemberProbe probe;
            OnboardingSelfCheckInput input = Healthy(out probe);
            foreach (string name in new[] { "get_HighEndBudget", "get_MidRangeBudget", "get_LowEndBudget", "get_IndustriesBudget" })
                probe.Missing["Market." + name] = "no such method";

            SelfCheckReport report = OnboardingSelfCheckRules.Run(input);

            Assert.Equal(4, report.Errors, Texts(report));
        }

        [Test]
        public void AProbeThatThrows_CountsAsAMissingMember_AndTheOthersAreStillLookedAt()
        {
            FakeMemberProbe probe;
            OnboardingSelfCheckInput input = Healthy(out probe);
            probe.Throws.Add("DateController.ManualPause");

            SelfCheckReport report = OnboardingSelfCheckRules.Run(input);

            Assert.Equal(1, report.Errors, Texts(report));
            Assert.True(report.Problems.Single().Message.Contains("the check failed"), report.Problems.Single().Message);
            Assert.Equal(GameMembers.All.Count, probe.Asked.Count, "the members after it were looked at");
        }

        [Test]
        public void NoProbe_IsAWarning_NotACrash()
        {
            FakeMemberProbe probe;
            OnboardingSelfCheckInput input = Healthy(out probe);
            input.Members = null;

            SelfCheckReport report = OnboardingSelfCheckRules.Run(input);

            Assert.Equal(0, report.Errors);
            Assert.Equal(1, report.Warnings);
            Assert.True(report.Has(SelfCheckSeverity.Warning, OnboardingSelfCheckRules.AreaGame));
        }

        // ---- the predicates ----

        [Test]
        public void ARefusedPredicate_IsAnError_WithTheReasonOfTheCore()
        {
            FakeMemberProbe probe;
            OnboardingSelfCheckInput input = Healthy(out probe);
            var asked = new List<string>();
            input.PredicateRefusal = name =>
            {
                asked.Add(name);
                return name == OnboardingPredicateLibrary.SoldToday ? "it was refused: the name is taken" : null;
            };

            SelfCheckReport report = OnboardingSelfCheckRules.Run(input);

            Assert.Equal(12, asked.Count, "all twelve predicates of the feature were asked about");
            Assert.Equal(1, report.Errors, Texts(report));
            string message = report.Problems.Single().Message;
            Assert.True(message.Contains("'onb.soldToday'") && message.Contains("the name is taken"), message);
            Assert.True(message.Contains("dropped"), "what it costs: " + message);
            Assert.Equal(OnboardingSelfCheckRules.AreaPredicates, report.Problems.Single().Area);
        }

        [Test]
        public void APredicateCheckThatThrows_IsAnErrorOfThatPredicate()
        {
            FakeMemberProbe probe;
            OnboardingSelfCheckInput input = Healthy(out probe);
            input.PredicateRefusal = name =>
            {
                if (name == OnboardingPredicateLibrary.Automate) throw new InvalidOperationException("boom");
                return null;
            };

            SelfCheckReport report = OnboardingSelfCheckRules.Run(input);

            Assert.Equal(1, report.Errors, Texts(report));
            Assert.True(report.Problems.Single().Message.Contains("onb.automate"));
        }

        [Test]
        public void NoWayToAskTheCore_IsAWarning()
        {
            FakeMemberProbe probe;
            OnboardingSelfCheckInput input = Healthy(out probe);
            input.PredicateRefusal = null;

            SelfCheckReport report = OnboardingSelfCheckRules.Run(input);

            Assert.Equal(0, report.Errors);
            Assert.True(report.Has(SelfCheckSeverity.Warning, OnboardingSelfCheckRules.AreaPredicates));
        }

        // ---- the catalog ----

        [Test]
        public void AnEmptyCatalog_IsAnError_ThatNamesTheLineOfTheHub()
        {
            ShippedContext context = ShippedContext.Load();
            OnboardingSelfCheckInput input = Healthy(context, StepCatalog.Build(null, null, null), new FakeMemberProbe());

            SelfCheckReport report = OnboardingSelfCheckRules.Run(input);

            Assert.Equal(1, report.Errors, "one finding for the whole catalog, not one per track: " + Texts(report));
            string message = report.Problems.Single().Message;
            Assert.True(message.Contains("onb.error.content"), "the hub page shows this key: " + message);
            Assert.True(message.Contains("cannot start"), message);
            Assert.Equal(OnboardingSelfCheckRules.AreaContent, report.Problems.Single().Area);
        }

        [Test]
        public void ANullCatalog_IsTheSameAsAnEmptyOne()
        {
            ShippedContext context = ShippedContext.Load();
            OnboardingSelfCheckInput input = Healthy(context, null, new FakeMemberProbe());

            SelfCheckReport report = OnboardingSelfCheckRules.Run(input);

            Assert.Equal(1, report.Errors, Texts(report));
        }

        [Test]
        public void ATrackWithoutSteps_IsAnError_AndTheHubLineIsNamed()
        {
            ShippedContext context = ShippedContext.Load();
            List<StepDefinition> steps = context.MapSteps();
            foreach (StepDefinition step in steps) step.Tracks = step.Tracks.Where(t => t != "foundry").ToArray();
            steps = steps.Where(s => s.Tracks.Length > 0).ToList();
            StepCatalog catalog = StepCatalog.Build(steps, context.MapConfig(), context);

            SelfCheckReport report = OnboardingSelfCheckRules.Run(Healthy(context, catalog, new FakeMemberProbe()));

            Assert.Equal(1, report.Errors, Texts(report));
            Assert.Equal(0, report.Warnings, "the line 'track foundry' of the catalog is not told twice: " + Texts(report));
            string message = report.Problems.Single().Message;
            Assert.True(message.Contains("'foundry'") && message.Contains("onb.error.content"), message);
        }

        [Test]
        public void ADroppedStep_IsAWarning_WithTheReasonOfTheCatalog()
        {
            ShippedContext context = ShippedContext.Load();
            List<StepDefinition> steps = context.MapSteps();
            steps.Single(s => s.Id == "research").CompleteWhen = OnbJson.Cond("{'type':'researchActve'}");
            StepCatalog catalog = StepCatalog.Build(steps, context.MapConfig(), context);
            Assert.Null(catalog.Find("research"), "the test step is dropped by the checks");

            SelfCheckReport report = OnboardingSelfCheckRules.Run(Healthy(context, catalog, new FakeMemberProbe()));

            Assert.Equal(0, report.Errors, Texts(report));
            Assert.True(report.Problems.Any(p => p.Severity == SelfCheckSeverity.Warning && p.Message.Contains("step 'research' skipped") && p.Message.Contains("researchActve")), Texts(report));
        }

        [Test]
        public void SoftFindingsOfTheCatalog_AreOneWarning()
        {
            ShippedContext context = ShippedContext.Load();
            var longText = new LongBodyContext(context, "onb.welcome.body");
            StepCatalog catalog = StepCatalog.Build(context.MapSteps(), context.MapConfig(), longText);
            Assert.True(catalog.Warnings.Count > 0, "the long text is a soft finding");

            SelfCheckReport report = OnboardingSelfCheckRules.Run(Healthy(context, catalog, new FakeMemberProbe()));

            Assert.Equal(0, report.Errors, Texts(report));
            Assert.Equal(1, report.Warnings, Texts(report));
            Assert.True(report.Problems.Single().Message.Contains("soft finding"), report.Problems.Single().Message);
        }

        /// <summary>The shipped context with one body that is too long, which only the validator's warnings notice.</summary>
        private sealed class LongBodyContext : IStepValidationContext
        {
            private readonly ShippedContext _inner;
            private readonly string _key;

            public LongBodyContext(ShippedContext inner, string key)
            {
                _inner = inner;
                _key = key;
            }

            public bool ValidateCondition(PTMod.Core.Logic.Json.JsonNode node, IList<string> errors, string path)
            {
                return _inner.ValidateCondition(node, errors, path);
            }

            public bool HasString(string key)
            {
                return _inner.HasString(key);
            }

            public string GetRussianText(string key)
            {
                return key == _key ? new string('x', StepValidator.MaxBodyLength + 50) : _inner.GetRussianText(key);
            }
        }

        // ---- the texts ----

        [Test]
        public void AKeyMissingInRussian_IsAnError_ThatNamesTheKey()
        {
            ShippedContext context = ShippedContext.Load();
            OnboardingSelfCheckInput input = Healthy(context, context.BuildCatalog(), new FakeMemberProbe());
            input.HasRussianText = key => key != "onb.hub.title" && key != "onb.research.body" && context.Tables.Contains("ru", key);

            SelfCheckReport report = OnboardingSelfCheckRules.Run(input);

            Assert.Equal(1, report.Errors, Texts(report));
            string message = report.Problems.Single().Message;
            Assert.True(message.Contains("2 key(s)") && message.Contains("onb.hub.title") && message.Contains("onb.research.body"), message);
            Assert.Equal(OnboardingSelfCheckRules.AreaTexts, report.Problems.Single().Area);
        }

        [Test]
        public void ManyMissingKeys_AreListedInOneLine_WithTheCount()
        {
            ShippedContext context = ShippedContext.Load();
            StepCatalog catalog = context.BuildCatalog();
            OnboardingSelfCheckInput input = Healthy(context, catalog, new FakeMemberProbe());
            input.HasRussianText = key => false;

            SelfCheckReport report = OnboardingSelfCheckRules.Run(input);

            Assert.Equal(1, report.Errors, Texts(report));
            string message = report.Problems.Single().Message;
            Assert.True(message.StartsWith(OnboardingKeys.All(catalog).Count + " key(s)", StringComparison.Ordinal), message);
            Assert.True(message.Contains(" more"), "only the first are named: " + message);
            Assert.True(message.Length < 600, "the line stays short: " + message.Length);
        }

        [Test]
        public void ATextReaderThatThrows_IsAMissingKey()
        {
            ShippedContext context = ShippedContext.Load();
            OnboardingSelfCheckInput input = Healthy(context, context.BuildCatalog(), new FakeMemberProbe());
            input.HasRussianText = key =>
            {
                if (key == "onb.hub.title") throw new InvalidOperationException("boom");
                return context.Tables.Contains("ru", key);
            };

            SelfCheckReport report = OnboardingSelfCheckRules.Run(input);

            Assert.Equal(1, report.Errors, Texts(report));
            Assert.True(report.Problems.Single().Message.Contains("onb.hub.title"));
        }

        [Test]
        public void AnApproximateRussianCheck_SaysSo_AsAWarning()
        {
            FakeMemberProbe probe;
            OnboardingSelfCheckInput input = Healthy(out probe);
            input.RussianIsExact = false;

            SelfCheckReport report = OnboardingSelfCheckRules.Run(input);

            Assert.Equal(0, report.Errors);
            Assert.Equal(1, report.Warnings, Texts(report));
            Assert.True(report.Problems.Single().Message.Contains("could not be read by itself"));
        }

        [Test]
        public void NoTextReader_IsAWarning()
        {
            FakeMemberProbe probe;
            OnboardingSelfCheckInput input = Healthy(out probe);
            input.HasRussianText = null;

            SelfCheckReport report = OnboardingSelfCheckRules.Run(input);

            Assert.Equal(0, report.Errors);
            Assert.True(report.Has(SelfCheckSeverity.Warning, OnboardingSelfCheckRules.AreaTexts));
        }

        // ---- the report ----

        [Test]
        public void TheLines_ListErrorsFirst_AndEachNamesItsArea()
        {
            var report = new SelfCheckReport();
            report.Add(SelfCheckSeverity.Warning, "game", "a warning found first");
            report.Add(SelfCheckSeverity.Error, "texts", "an error found second");
            report.Add(SelfCheckSeverity.Warning, "content", "another warning");

            IList<string> lines = report.Lines();

            Assert.Equal(new[]
            {
                "self-check: [texts] an error found second",
                "self-check: [game] a warning found first",
                "self-check: [content] another warning"
            }, lines.ToArray());
            Assert.Equal("self-check: 1 error(s), 2 warning(s)", report.Summary);
            Assert.False(report.IsOk);
        }

        [Test]
        public void ACheckThatFails_IsAWarning_AndTheNextCheckStillRuns()
        {
            FakeMemberProbe probe;
            OnboardingSelfCheckInput input = Healthy(out probe);
            input.MemberList = Exploding();
            var asked = 0;
            input.PredicateRefusal = name =>
            {
                asked++;
                return null;
            };

            SelfCheckReport report = OnboardingSelfCheckRules.Run(input);

            Assert.True(report.Has(SelfCheckSeverity.Warning, OnboardingSelfCheckRules.AreaCheck), Texts(report));
            Assert.Equal(12, asked, "the predicates were checked all the same");
        }

        private static IEnumerable<GameMemberSpec> Exploding()
        {
            throw new InvalidOperationException("the list broke");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }

        [Test]
        public void ANullInput_GivesAWarningAndNothingElse()
        {
            SelfCheckReport report = OnboardingSelfCheckRules.Run(null);

            Assert.Equal(0, report.Errors);
            Assert.Equal(1, report.Warnings);
        }

        // ---- the keys the tutorial needs ----

        [Test]
        public void TheKeysOfAStep_AreTitleBodyVariantsHintsAndForAStepOfTheTrackItsNameAndPhrase()
        {
            var step = OnbSteps.Seq("probe", 10, "{'type':'researchActive'}");
            step.Mail.Variants = new[] { new BodyVariant { When = OnbJson.Cond("{'type':'researchActive'}"), BodyKey = "onb.probe.body.alt" } };
            step.WithReminders(5, 9);

            IList<string> keys = OnboardingKeys.ForStep(step);

            Assert.Equal(new[]
            {
                "onb.probe.title", "onb.probe.body", "onb.probe.body.alt", "onb.probe.hint1", "onb.probe.hint2", "onb.probe.name", "onb.probe.done"
            }, keys.ToArray());

            Assert.Equal(new[] { "onb.welcome.title", "onb.welcome.body", "onb.welcome.name" }, OnboardingKeys.ForStep(OnbSteps.Info("welcome", 10)).ToArray(), "no condition, no phrase");
            Assert.Equal(new[] { "onb.tip.title", "onb.tip.body" }, OnboardingKeys.ForStep(OnbSteps.Tip("tip", "{'type':'researchActive'}")).ToArray(), "a tip has no name");
            Assert.Equal(0, OnboardingKeys.ForStep(null).Count);
        }

        [Test]
        public void TheFixedKeys_AreUnique_Prefixed_AndInBothShippedTables()
        {
            ShippedContext context = ShippedContext.Load();

            Assert.Equal(OnboardingKeys.Fixed.Count, OnboardingKeys.Fixed.Distinct().Count(), "no key twice");
            foreach (string key in OnboardingKeys.Fixed)
            {
                Assert.True(key.StartsWith("onb.", StringComparison.Ordinal), key);
                Assert.True(context.Tables.Contains("ru", key), "no Russian text for " + key);
                Assert.True(context.Tables.Contains("en", key), "no English text for " + key);
            }
        }

        [Test]
        public void TheFixedKeys_AreEveryKeyOfTheTablesThatNoStepOwns()
        {
            ShippedContext context = ShippedContext.Load();
            StepCatalog catalog = context.BuildCatalog();

            // What a step owns: its keys and the optional bodies of its tracks.
            var owned = new HashSet<string>(OnboardingKeys.ForCatalog(catalog), StringComparer.Ordinal);
            foreach (StepDefinition step in catalog.All)
            {
                foreach (string track in step.Tracks) owned.Add(step.Mail.BodyKey + "." + track);
            }

            var strays = new List<string>();
            foreach (string key in context.Tables.Keys("ru"))
            {
                if (!owned.Contains(key) && !OnboardingKeys.Fixed.Contains(key)) strays.Add(key);
            }
            Assert.True(strays.Count == 0, "keys of the table that neither a step nor the fixed list owns (add them to OnboardingKeys.Fixed or drop them):\n  " + string.Join("\n  ", strays));

            // The other way: a fixed key that no table has and no step uses is dead weight, except the two summary keys the step 'graduation' owns as well.
            foreach (string key in OnboardingKeys.Fixed) Assert.True(context.Tables.Contains("ru", key), key);
        }

        private static readonly Regex KeyLiteral = new Regex("\"(onb\\.[A-Za-z0-9_.]*)\"", RegexOptions.Compiled);

        [Test]
        public void EveryKeyTheCodeNames_IsInTheFixedList_AndEveryFixedKeyIsNamedByTheCode()
        {
            string folder = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "FirstYearOnboarding");
            var notKeys = new HashSet<string>(OnboardingPredicateLibrary.Names, StringComparer.Ordinal) { "onb.completed", "onb.pendingModeChanged" };   // predicates and events
            var named = new HashSet<string>(StringComparer.Ordinal);
            var unknown = new List<string>();

            foreach (string path in SourceScan.Files(folder, ".cs"))
            {
                if (Path.GetFileName(path) == "OnboardingKeys.cs") continue;
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                foreach (Match match in KeyLiteral.Matches(code))
                {
                    string key = match.Groups[1].Value;
                    if (key.EndsWith(".", StringComparison.Ordinal) || notKeys.Contains(key)) continue;   // a prefix the code completes, a predicate, an event
                    named.Add(key);
                    if (!OnboardingKeys.Fixed.Contains(key)) unknown.Add(SourceScan.Relative(folder, path) + ": " + key);
                }
            }
            Assert.True(unknown.Count == 0, "keys the code names that OnboardingKeys.Fixed does not list (the self-check would not look for them):\n  " + string.Join("\n  ", unknown));

            // The keys the code builds from a prefix (onb.track.<track>) are not literals.
            var unnamed = OnboardingKeys.Fixed.Where(k => !named.Contains(k) && !k.StartsWith("onb.track.", StringComparison.Ordinal)).ToList();
            Assert.True(unnamed.Count == 0, "fixed keys that no source names any more:\n  " + string.Join("\n  ", unnamed));
        }

        // ---- the members against the code of the game ----

        [Test]
        public void EveryMemberOfTheList_IsInTheDecompiledCodeOfTheGame()
        {
            string code = Path.GetFullPath(Path.Combine(TestEnv.ModDir ?? ".", "..", "..", "pt-modding", "code"));
            if (!Directory.Exists(code))
            {
                Note.Skip("OnboardingSelfCheckTests.EveryMemberOfTheList_IsInTheDecompiledCodeOfTheGame", "the decompiled code of the game is not next to the repository (" + code + ")");
                return;
            }

            var missing = new List<string>();
            foreach (GameMemberSpec member in GameMembers.All)
            {
                string typeName = member.TypeName;
                int nested = typeName.IndexOf('+');
                if (nested >= 0) typeName = typeName.Substring(0, nested);

                string file = Path.Combine(code, typeName.Replace('.', Path.DirectorySeparatorChar) + ".cs");
                if (!File.Exists(file))
                {
                    missing.Add(member.Label + ": no file " + file);
                    continue;
                }

                string text = SourceScan.ReadText(file);
                string pattern;
                switch (member.Kind)
                {
                    case GameMemberKind.Method: pattern = @"\b" + Regex.Escape(member.Name) + @"\s*\("; break;
                    case GameMemberKind.Event: pattern = @"\bevent\b[^;]*\b" + Regex.Escape(member.Name) + @"\b"; break;
                    default: pattern = @"\b" + Regex.Escape(member.Name) + @"\s*;"; break;
                }
                if (!Regex.IsMatch(text, pattern)) missing.Add(member.Label + ": not found in " + Path.GetFileName(file));
            }
            Assert.True(missing.Count == 0, "members of GameMembers.All that the decompiled game does not have:\n  " + string.Join("\n  ", missing));
        }

        // ---- the seams of the game layer ----

        private static string Code(string relative)
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "FirstYearOnboarding", relative);
            string text = SourceScan.ReadText(path);
            Assert.True(text.Length > 0, "missing " + path);
            return SourceScan.Strip(text, false);
        }

        [Test]
        public void TheFeature_RunsTheSelfCheckLast_InsideTheGuard()
        {
            string code = Code("OnboardingFeature.cs");

            Assert.True(Regex.IsMatch(code, @"Guarded\s*\(\s*""self-check""\s*,\s*\(\)\s*=>\s*OnboardingSelfCheck\s*\.\s*Run\s*\(\s*\)\s*\)"), "Register runs OnboardingSelfCheck.Run() under Guarded");

            int check = code.IndexOf("OnboardingSelfCheck.Run", StringComparison.Ordinal);
            foreach (string earlier in new[] { "OnboardingPredicates.Register", "StepContentLoader.Load", "ctx.HubPage", "new OnboardingDirector" })
                Assert.True(code.IndexOf(earlier, StringComparison.Ordinal) < check, "the self-check comes after " + earlier + ": it looks at what they made");
        }

        [Test]
        public void TheGameLayer_WritesOkOrTheFindings_ToTheLogOfTheFeature()
        {
            string code = Code("OnboardingSelfCheck.cs");

            Assert.True(Regex.IsMatch(code, @"if\s*\(\s*report\s*\.\s*IsOk\s*\)\s*\{\s*ModLog\s*\.\s*Info\s*\(\s*id\s*,\s*report\s*\.\s*Summary\s*\)"), "a clean check is one INFO line, report.Summary = 'self-check: OK'");
            Assert.True(Regex.IsMatch(code, @"ModLog\s*\.\s*Error\s*\(\s*id\s*,\s*""self-check: ""\s*\+\s*problem\s*\)"), "an error is one ERROR line");
            Assert.True(Regex.IsMatch(code, @"ModLog\s*\.\s*Warn\s*\(\s*id\s*,\s*""self-check: ""\s*\+\s*problem\s*\)"), "a warning is one WARN line");
            Assert.True(code.Contains("string id = OnboardingFeature.FeatureId;"), "the lines carry [first-year-onboarding]");
            Assert.True(Regex.IsMatch(code, @"catch\s*\(\s*Exception\s+ex\s*\)\s*\{\s*report\s*=\s*new\s+SelfCheckReport"), "a check that fails is a finding and not a crash");
        }

        [Test]
        public void TheGameLayer_AsksTheCoreForThePredicates_TheCatalogOfTheFeature_AndTheRussianTable()
        {
            string code = Code("OnboardingSelfCheck.cs");

            Assert.True(Regex.IsMatch(code, @"ModConditions\s*\.\s*OwnerOf\s*\(\s*name\s*\)"), "who registered the predicate");
            Assert.True(Regex.IsMatch(code, @"ModConditions\s*\.\s*Rejected"), "why the core refused it");
            Assert.True(Regex.IsMatch(code, @"Catalog\s*=\s*OnboardingFeature\s*\.\s*Catalog"), "the catalog of the feature");
            Assert.True(Regex.IsMatch(code, @"texts\s*\.\s*CanReadRussian") && Regex.IsMatch(code, @"texts\s*\.\s*HasRussianText"), "the raw Russian table, when it can be read");
            Assert.True(Regex.IsMatch(code, @":\s*ModL10n\s*\.\s*Has\b"), "the language in use, when it cannot (the report says the check is approximate)");
            Assert.True(Regex.IsMatch(code, @"Members\s*=\s*new\s+ReflectionProbe\s*\(\s*\)"), "the members are looked up by reflection, by name");
            Assert.False(Regex.IsMatch(code, @"\btypeof\s*\("), "a class of the game is never named by a type the compiler resolves: a renamed class must be a finding, not a crash");
        }

        [Test]
        public void TheHubPage_SaysOnbErrorContent_ForBrokenAndDamagedContent()
        {
            string code = Code("OnboardingHubPage.cs");

            Assert.True(Regex.IsMatch(code, @"director\s*==\s*null\s*\|\|\s*OnboardingFeature\s*\.\s*ContentBroken"), "broken content: the one line instead of the page");
            Assert.True(Regex.IsMatch(code, @"ModUI\s*\.\s*Text\s*\(\s*container\s*,\s*ModL10n\s*\.\s*L\s*\(\s*""onb\.error\.content""\s*\)\s*\)"), "the line is the text of onb.error.content");
            Assert.True(Regex.IsMatch(code, @"CurrentStatus\s*\(\s*\)\s*\.\s*ContentDamaged"), "a track without steps adds the line to the page");
        }

        [Test]
        public void TheContextOfTheCore_OffersTheRussianTable_ToTheSelfCheck()
        {
            string code = Code("GameStepValidationContext.cs");

            Assert.True(Regex.IsMatch(code, @"internal\s+bool\s+CanReadRussian"), "CanReadRussian");
            Assert.True(Regex.IsMatch(code, @"internal\s+bool\s+HasRussianText\s*\(\s*string\s+key\s*\)"), "HasRussianText");
        }
    }
}
