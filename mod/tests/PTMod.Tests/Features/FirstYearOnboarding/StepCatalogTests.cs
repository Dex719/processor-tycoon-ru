using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core.Logic.Json;
using PTMod.Features.FirstYearOnboarding.Logic;

namespace PTMod.Tests.Features.FirstYearOnboarding
{
    /// <summary>
    /// The model of the steps and the catalog that checks them (TSK-002, FR-3): a correct set loads whole; a duplicate id, an unknown track, an
    /// unknown predicate, a missing string, a wrong <c>afterDays</c>, <c>completeWhen</c> together with <c>completeOnShow</c>, a placeholder name outside
    /// the list, a placeholder index beyond the arguments and an empty track are each an error that drops the step (or the track) with a reason, while the
    /// other steps still load. Mapping of the JSON to the model and of the rhythm config is tested here too.
    /// </summary>
    [Covers(typeof(StepCatalog), typeof(StepValidator), typeof(StepMapper), typeof(StepDefinition), typeof(StepKind), typeof(MailSpec),
        typeof(BodyVariant), typeof(ReminderSpec), typeof(OnboardingConfig), typeof(ConditionRef), typeof(IStepValidationContext))]
    public class StepCatalogTests
    {
        // ---- builders ----

        private static StepDefinition Seq(string id, int order, params string[] tracks)
        {
            return new StepDefinition
            {
                Id = id,
                Order = order,
                Kind = StepKind.Sequential,
                Tracks = tracks.Length == 0 ? new[] { "idm" } : tracks,
                CompleteWhen = OnbJson.Cond("{'type':'researchActive'}"),
                Mail = new MailSpec { TitleKey = "onb." + id + ".title", BodyKey = "onb." + id + ".body" }
            };
        }

        private static StepDefinition Info(string id, int order, params string[] tracks)
        {
            StepDefinition step = Seq(id, order, tracks);
            step.CompleteWhen = null;
            step.CompleteOnShow = true;
            return step;
        }

        private static StepDefinition Tip(string id, params string[] tracks)
        {
            StepDefinition step = Seq(id, 0, tracks);
            step.Kind = StepKind.Situational;
            step.ShowWhen = OnbJson.Cond("{'type':'moneyBelow','amount':0}");
            step.CompleteWhen = null;
            step.CompleteOnShow = true;
            return step;
        }

        /// <summary>A correct set that has steps in all three tracks and one tip.</summary>
        private static List<StepDefinition> ValidSet()
        {
            return new List<StepDefinition>
            {
                Info("welcome", 10, "idm", "fabless", "foundry"),
                Seq("research", 20, "idm", "fabless", "foundry"),
                Seq("design_cpu", 30, "idm", "fabless"),
                Seq("foundry_services", 30, "foundry"),
                Tip("tip_debt", "idm", "fabless", "foundry")
            };
        }

        private static FakeStepContext ContextFor(IEnumerable<StepDefinition> steps)
        {
            return new FakeStepContext().WithStrings(steps.ToArray());
        }

        private static StepCatalog Build(List<StepDefinition> steps, FakeStepContext context = null)
        {
            return StepCatalog.Build(steps, new OnboardingConfig(), context ?? ContextFor(steps));
        }

        private static string AllErrors(StepCatalog catalog)
        {
            return string.Join(" | ", catalog.Errors);
        }

        private static bool HasError(StepCatalog catalog, string part)
        {
            return catalog.Errors.Any(e => e.Contains(part));
        }

        // ---- a correct file ----

        [Test]
        public void Build_CorrectSet_LoadsWholeWithoutErrors()
        {
            StepCatalog catalog = Build(ValidSet());

            Assert.Equal(0, catalog.Errors.Count, AllErrors(catalog));
            Assert.Equal(0, catalog.Warnings.Count, string.Join(" | ", catalog.Warnings));
            Assert.Equal(5, catalog.All.Count);
            Assert.False(catalog.IsEmpty);
            Assert.True(catalog.HasTrack("idm") && catalog.HasTrack("fabless") && catalog.HasTrack("foundry"));
            Assert.NotNull(catalog.Config);
        }

        [Test]
        public void StepsFor_SequentialStepsComeByOrder_WhateverTheFileOrder()
        {
            var steps = new List<StepDefinition>
            {
                Seq("third", 30), Info("first", 10), Seq("second", 20)
            };
            StepCatalog catalog = Build(steps);

            Assert.Equal(new[] { "first", "second", "third" }, catalog.StepsFor("idm", StepKind.Sequential).Select(s => s.Id).ToArray());
            Assert.Equal(3, catalog.SequentialCount("idm"));
            Assert.Equal(0, catalog.SequentialCount("foundry"));
        }

        [Test]
        public void StepsFor_FiltersByTrackAndKind_TipsKeepTheFileOrder()
        {
            var steps = ValidSet();
            steps.Add(Tip("tip_a", "idm"));
            steps.Add(Tip("tip_b", "idm", "foundry"));
            StepCatalog catalog = Build(steps);

            Assert.Equal(new[] { "welcome", "research", "design_cpu" }, catalog.StepsFor("idm", StepKind.Sequential).Select(s => s.Id).ToArray());
            Assert.Equal(new[] { "welcome", "research", "foundry_services" }, catalog.StepsFor("foundry", StepKind.Sequential).Select(s => s.Id).ToArray());
            Assert.Equal(new[] { "tip_debt", "tip_a", "tip_b" }, catalog.StepsFor("idm", StepKind.Situational).Select(s => s.Id).ToArray());
            Assert.Equal(new[] { "tip_debt", "tip_b" }, catalog.StepsFor("foundry", StepKind.Situational).Select(s => s.Id).ToArray());
            Assert.Equal(0, catalog.StepsFor("nobody", StepKind.Sequential).Count);
            Assert.Equal(0, catalog.StepsFor(null, StepKind.Situational).Count);
        }

        [Test]
        public void StepsFor_ReturnsAReadOnlyList()
        {
            StepCatalog catalog = Build(ValidSet());
            IList<StepDefinition> list = catalog.StepsFor("idm", StepKind.Sequential);

            Assert.Throws<NotSupportedException>(() => list.Add(Seq("sneaky", 99)));
            Assert.Throws<NotSupportedException>(() => catalog.All.Add(Seq("sneaky", 99)));
            Assert.Throws<NotSupportedException>(() => catalog.Errors.Add("x"));
        }

        [Test]
        public void Find_ByIdOrNull()
        {
            StepCatalog catalog = Build(ValidSet());

            Assert.Equal("research", catalog.Find("research").Id);
            Assert.Null(catalog.Find("unknown"));
            Assert.Null(catalog.Find(null));
        }

        // ---- the rejected steps (each case: the step is out with a reason, the others stay) ----

        [Test]
        public void Build_DuplicateId_KeepsTheFirstStep()
        {
            var steps = ValidSet();
            StepDefinition copy = Seq("research", 25, "idm");
            copy.Mail.TitleKey = "onb.research.title";
            steps.Insert(2, copy);

            StepCatalog catalog = Build(steps);

            Assert.True(HasError(catalog, "step 'research' skipped: duplicate id"), AllErrors(catalog));
            Assert.Equal(5, catalog.All.Count);
            Assert.Equal(20, catalog.Find("research").Order, "the first one stays");
        }

        [Test]
        public void Build_UnknownTrack_SkipsTheStep()
        {
            var steps = ValidSet();
            StepDefinition bad = Seq("bad_track", 40, "idm", "mainframe");
            steps.Add(bad);

            StepCatalog catalog = Build(steps);

            Assert.True(HasError(catalog, "step 'bad_track' skipped: tracks[1]: unknown track 'mainframe'"), AllErrors(catalog));
            Assert.Null(catalog.Find("bad_track"));
            Assert.Equal(5, catalog.All.Count);
        }

        [Test]
        public void Build_NoTracksOrDuplicateTrack_IsAnError()
        {
            var steps = ValidSet();
            StepDefinition none = Seq("no_tracks", 40);
            none.Tracks = new string[0];
            StepDefinition twice = Seq("twice", 41, "idm", "idm");
            steps.Add(none);
            steps.Add(twice);

            StepCatalog catalog = Build(steps);

            Assert.True(HasError(catalog, "step 'no_tracks' skipped: tracks: at least one track"), AllErrors(catalog));
            Assert.True(HasError(catalog, "step 'twice' skipped: tracks[1]: duplicate track 'idm'"), AllErrors(catalog));
        }

        [Test]
        public void Build_UnknownPredicate_SkipsTheStepWithTheReason_AC_3_1()
        {
            var steps = ValidSet();
            steps[1].CompleteWhen = OnbJson.Cond("{'type':'researchActve'}");   // the typo of AC-3.1

            StepCatalog catalog = Build(steps);

            Assert.True(HasError(catalog, "step 'research' skipped: completeWhen.type: unknown predicate 'researchActve'"), AllErrors(catalog));
            Assert.Null(catalog.Find("research"));
            Assert.NotNull(catalog.Find("welcome"), "the other steps work");
            Assert.NotNull(catalog.Find("design_cpu"));
        }

        [Test]
        public void Build_UnknownPredicateInShowWhenAndInAVariant_IsAnError()
        {
            var steps = ValidSet();
            steps[2].ShowWhen = OnbJson.Cond("{'all':[{'type':'researchActive'},{'not':{'type':'nope'}}]}");
            steps[3].Mail.Variants = new[] { new BodyVariant { When = OnbJson.Cond("{'type':'nada'}"), BodyKey = "onb.foundry_services.body" } };

            StepCatalog catalog = Build(steps);

            Assert.True(HasError(catalog, "step 'design_cpu' skipped: showWhen.all[1].not.type: unknown predicate 'nope'"), AllErrors(catalog));
            Assert.True(HasError(catalog, "step 'foundry_services' skipped: mail.variants[0].when.type: unknown predicate 'nada'"), AllErrors(catalog));
        }

        [Test]
        public void Build_MissingString_SkipsTheStep()
        {
            var steps = ValidSet();
            FakeStepContext context = ContextFor(steps);
            context.Russian.Remove("onb.research.title");
            context.Russian.Remove("onb.design_cpu.body");

            StepCatalog catalog = Build(steps, context);

            Assert.True(HasError(catalog, "step 'research' skipped: mail.titleKey: the string 'onb.research.title' does not exist"), AllErrors(catalog));
            Assert.True(HasError(catalog, "step 'design_cpu' skipped: mail.bodyKey: the string 'onb.design_cpu.body' does not exist"), AllErrors(catalog));
            Assert.Null(catalog.Find("research"));
        }

        [Test]
        public void Build_MissingHintOrVariantString_IsAnError()
        {
            var steps = ValidSet();
            steps[1].Reminders = new[] { new ReminderSpec { AfterDays = 15, HintKey = "onb.research.hint1" } };
            steps[2].Mail.Variants = new[] { new BodyVariant { When = OnbJson.Cond("{'type':'inDefault'}"), BodyKey = "onb.design_cpu.body.rec" } };

            StepCatalog catalog = Build(steps);

            Assert.True(HasError(catalog, "reminders[0].hintKey: the string 'onb.research.hint1' does not exist"), AllErrors(catalog));
            Assert.True(HasError(catalog, "mail.variants[0].bodyKey: the string 'onb.design_cpu.body.rec' does not exist"), AllErrors(catalog));
        }

        [Test]
        public void Build_EmptyKey_IsRequired()
        {
            var steps = ValidSet();
            steps[1].Mail.TitleKey = null;
            steps[2].Mail.BodyKey = "";

            StepCatalog catalog = Build(steps);

            Assert.True(HasError(catalog, "step 'research' skipped: mail.titleKey: required"), AllErrors(catalog));
            Assert.True(HasError(catalog, "step 'design_cpu' skipped: mail.bodyKey: required"), AllErrors(catalog));
        }

        [Test]
        public void Build_AfterDaysOutsideTheRange_IsAnError()
        {
            var steps = ValidSet();
            FakeStepContext context = ContextFor(steps);
            context.Russian["h"] = "hint";
            steps[1].Reminders = new[] { new ReminderSpec { AfterDays = 0, HintKey = "h" } };
            steps[2].Reminders = new[] { new ReminderSpec { AfterDays = 3651, HintKey = "h" } };
            steps[3].Reminders = new[] { new ReminderSpec { AfterDays = 1, HintKey = "h" }, new ReminderSpec { AfterDays = 3650, HintKey = "h" } };

            StepCatalog catalog = Build(steps, context);

            Assert.True(HasError(catalog, "step 'research' skipped: reminders[0].afterDays: 0 is outside 1..3650"), AllErrors(catalog));
            Assert.True(HasError(catalog, "step 'design_cpu' skipped: reminders[0].afterDays: 3651 is outside 1..3650"), AllErrors(catalog));
            Assert.NotNull(catalog.Find("foundry_services"), "the ends of the range 1 and 3650 are allowed");
        }

        [Test]
        public void Build_AfterDaysMustAscend()
        {
            var steps = ValidSet();
            FakeStepContext context = ContextFor(steps);
            context.Russian["h"] = "hint";
            steps[1].Reminders = new[] { new ReminderSpec { AfterDays = 40, HintKey = "h" }, new ReminderSpec { AfterDays = 15, HintKey = "h" } };
            steps[2].Reminders = new[] { new ReminderSpec { AfterDays = 15, HintKey = "h" }, new ReminderSpec { AfterDays = 15, HintKey = "h" } };

            StepCatalog catalog = Build(steps, context);

            Assert.True(HasError(catalog, "step 'research' skipped: reminders[1].afterDays: 15 must be greater than the previous reminder (40)"), AllErrors(catalog));
            Assert.True(HasError(catalog, "step 'design_cpu' skipped: reminders[1].afterDays: 15 must be greater"), AllErrors(catalog));
        }

        [Test]
        public void Build_CompleteWhenAndCompleteOnShowTogether_IsAnError()
        {
            var steps = ValidSet();
            steps[1].CompleteOnShow = true;   // research already has completeWhen
            StepDefinition neither = Seq("neither", 40);
            neither.CompleteWhen = null;
            steps.Add(neither);

            StepCatalog catalog = Build(steps);

            Assert.True(HasError(catalog, "step 'research' skipped: completeWhen: cannot be used together with completeOnShow"), AllErrors(catalog));
            Assert.True(HasError(catalog, "step 'neither' skipped: completeWhen: the step needs completeWhen or completeOnShow"), AllErrors(catalog));
            Assert.Null(catalog.Find("research"));
            Assert.Null(catalog.Find("neither"));
        }

        [Test]
        public void Build_ArgsOutsideTheWhitelist_IsAnError()
        {
            var steps = ValidSet();
            steps[1].Mail.Args = new[] { "company", "turboBudget" };
            FakeStepContext context = ContextFor(steps);
            context.Russian["h"] = "hint";
            steps[2].Reminders = new[] { new ReminderSpec { AfterDays = 5, HintKey = "h", Args = new[] { "lowEndBudget", "secret" } } };

            StepCatalog catalog = Build(steps, context);

            Assert.True(HasError(catalog, "step 'research' skipped: mail.args[1]: unknown placeholder 'turboBudget'"), AllErrors(catalog));
            Assert.True(HasError(catalog, "step 'design_cpu' skipped: reminders[0].args[1]: unknown placeholder 'secret'"), AllErrors(catalog));
        }

        [Test]
        public void Build_EveryNameOfTheWhitelist_IsAccepted()
        {
            var steps = ValidSet();
            steps[1].Mail.Args = StepValidator.ArgNames.ToArray();
            FakeStepContext context = ContextFor(steps);
            context.Russian["onb.research.body"] = "{0} {1} {2} {3} {4} {5} {6} {7} {8}";

            StepCatalog catalog = Build(steps, context);

            Assert.Equal(9, StepValidator.ArgNames.Count);
            Assert.Equal(0, catalog.Errors.Count, AllErrors(catalog));
        }

        [Test]
        public void Build_PlaceholderIndexBeyondTheArgs_IsAnError()
        {
            var steps = ValidSet();
            steps[1].Mail.Args = new[] { "company" };                       // {0} only
            FakeStepContext context = ContextFor(steps);
            context.Russian["onb.research.body"] = "Hello {0} and {1}";     // {1} is beyond
            context.Russian["onb.design_cpu.title"] = StepValidator.MentorPrefixRu + " {0}";   // no args at all
            context.Russian["h"] = "Budget {0}";
            steps[3].Reminders = new[] { new ReminderSpec { AfterDays = 5, HintKey = "h" } };

            StepCatalog catalog = Build(steps, context);

            Assert.True(HasError(catalog, "step 'research' skipped: mail.bodyKey: the string 'onb.research.body' uses {1} but the step lists only 1 argument(s)"), AllErrors(catalog));
            Assert.True(HasError(catalog, "step 'design_cpu' skipped: mail.titleKey: the string 'onb.design_cpu.title' uses {0} but the step lists only 0 argument(s)"), AllErrors(catalog));
            Assert.True(HasError(catalog, "step 'foundry_services' skipped: reminders[0].hintKey: the string 'h' uses {0} but the step lists only 0 argument(s)"), AllErrors(catalog));
        }

        [Test]
        public void Build_PlaceholderCheckAlsoCoversTheTrackAndVariantTexts()
        {
            var steps = ValidSet();
            steps[2].Mail.Args = new[] { "company" };
            steps[2].Mail.Variants = new[] { new BodyVariant { When = OnbJson.Cond("{'type':'inDefault'}"), BodyKey = "onb.design_cpu.body.rec" } };
            FakeStepContext context = ContextFor(steps);
            context.Russian["onb.design_cpu.body.fabless"] = "{0} {2}";    // a text of the track, {2} is beyond
            context.Russian["onb.design_cpu.body.rec"] = "{0} {1}";        // a text of a variant, {1} is beyond

            StepCatalog catalog = Build(steps, context);

            Assert.True(HasError(catalog, "mail.bodyKey.fabless: the string 'onb.design_cpu.body.fabless' uses {2}"), AllErrors(catalog));
            Assert.True(HasError(catalog, "mail.variants[0].bodyKey: the string 'onb.design_cpu.body.rec' uses {1}"), AllErrors(catalog));
        }

        [Test]
        public void Build_PlaceholderCheckIsSkippedWhenRawTextsAreNotAvailable()
        {
            var steps = ValidSet();
            FakeStepContext context = ContextFor(steps);
            context.Russian["onb.research.body"] = "{0} {1} {2}";
            context.NoRawTexts = true;

            StepCatalog catalog = Build(steps, context);

            Assert.Equal(0, catalog.Errors.Count, AllErrors(catalog));
            Assert.NotNull(catalog.Find("research"));
        }

        [Test]
        public void Build_EmptyTrack_IsAnErrorAndTheTrackIsOff()
        {
            var steps = new List<StepDefinition> { Info("welcome", 10, "idm", "fabless"), Seq("research", 20, "idm", "fabless") };

            StepCatalog catalog = Build(steps);

            Assert.True(HasError(catalog, "track 'foundry': no sequential steps are left"), AllErrors(catalog));
            Assert.False(catalog.HasTrack("foundry"));
            Assert.True(catalog.HasTrack("idm"));
            Assert.False(catalog.IsEmpty, "a catalog with one working track is usable");
            Assert.Equal(new[] { "foundry" }, catalog.MissingTracks.ToArray(), "the tracks that are off, for the self-check and the hub");
        }

        [Test]
        public void MissingTracks_AreEmptyForAWholeCatalog_AndAllThreeForNoSteps()
        {
            Assert.Equal(0, Build(ValidSet()).MissingTracks.Count);
            Assert.Equal(new[] { "idm", "fabless", "foundry" }, Build(new List<StepDefinition>()).MissingTracks.ToArray(), "the order of the tracks of the validator");
        }

        [Test]
        public void Build_TrackLeftWithTipsOnly_IsEmptyForSequentialSteps()
        {
            var steps = new List<StepDefinition> { Seq("research", 20, "idm"), Tip("tip_debt", "idm", "foundry") };

            StepCatalog catalog = Build(steps);

            Assert.False(catalog.HasTrack("foundry"), "a tip is not a step of the track");
            Assert.True(HasError(catalog, "track 'foundry'"), AllErrors(catalog));
            Assert.Equal(1, catalog.StepsFor("foundry", StepKind.Situational).Count);
        }

        [Test]
        public void Build_NoStepsAtAll_IsEmptyAndTheContentIsBroken()
        {
            StepCatalog catalog = Build(new List<StepDefinition>());

            Assert.True(catalog.IsEmpty);
            Assert.Equal(3, catalog.Errors.Count, "one line per track: " + AllErrors(catalog));
            Assert.False(catalog.HasTrack("idm"));
        }

        [Test]
        public void Build_BrokenStep_DoesNotStopTheOthers()
        {
            var steps = ValidSet();
            steps.Insert(1, Seq("Bad Id!", 15));
            steps.Insert(1, null);

            StepCatalog catalog = Build(steps);

            Assert.True(HasError(catalog, "step 'Bad Id!' skipped: id: 'Bad Id!' is not valid"), AllErrors(catalog));
            Assert.True(HasError(catalog, "step #2 skipped: the record is empty"), AllErrors(catalog));
            Assert.Equal(5, catalog.All.Count, "the five good steps all load");
            Assert.Equal(new[] { "welcome", "research", "design_cpu" }, catalog.StepsFor("idm", StepKind.Sequential).Select(s => s.Id).ToArray());
        }

        [Test]
        public void Build_SeveralReasonsAreAllReported()
        {
            var steps = ValidSet();
            steps[1].Tracks = new[] { "mainframe" };
            steps[1].CompleteWhen = OnbJson.Cond("{'type':'nope'}");
            steps[1].Priority = "urgent";

            StepCatalog catalog = Build(steps);

            string line = catalog.Errors.First(e => e.StartsWith("step 'research'", StringComparison.Ordinal));
            Assert.True(line.Contains("unknown track 'mainframe'"), line);
            Assert.True(line.Contains("unknown predicate 'nope'"), line);
            Assert.True(line.Contains("unknown priority 'urgent'"), line);
        }

        [Test]
        public void Build_SameOrderInATrack_KeepsTheFirstStep()
        {
            var steps = ValidSet();
            steps.Add(Seq("clash", 30, "fabless", "foundry"));   // 30 is taken in fabless by design_cpu and in foundry by foundry_services

            StepCatalog catalog = Build(steps);

            Assert.True(HasError(catalog, "step 'clash' skipped: order 30 in track 'fabless' is already used by step 'design_cpu'"), AllErrors(catalog));
            Assert.Null(catalog.Find("clash"));
        }

        [Test]
        public void Build_SameOrderInDifferentTracksOrAsATip_IsFine()
        {
            var steps = ValidSet();                       // design_cpu (idm, fabless) and foundry_services (foundry) both have 30
            StepDefinition tip = Tip("tip_same", "idm");
            tip.Order = 30;                               // a tip's order is ignored
            steps.Add(tip);

            StepCatalog catalog = Build(steps);

            Assert.Equal(0, catalog.Errors.Count, AllErrors(catalog));
        }

        [Test]
        public void Build_StepDoneOfAnUnknownStep_IsAWarningNotAnError()
        {
            var steps = ValidSet();
            steps[1].ShowWhen = OnbJson.Cond("{'all':[{'type':'onb.stepDone','id':'research'},{'type':'onb.stepDone','id':'ghost'}]}");

            StepCatalog catalog = Build(steps);

            Assert.Equal(0, catalog.Errors.Count, AllErrors(catalog));
            Assert.NotNull(catalog.Find("research"));
            Assert.Equal(1, catalog.Warnings.Count);
            Assert.True(catalog.Warnings[0].Contains("asks whether step 'ghost' is done"), catalog.Warnings[0]);
        }

        [Test]
        public void Build_NeverThrows_OnNullInput()
        {
            StepCatalog catalog = StepCatalog.Build(null, null, null);

            Assert.NotNull(catalog);
            Assert.True(catalog.IsEmpty);
            Assert.NotNull(catalog.Config);
            Assert.Equal(730, catalog.Config.TutorialWindowDays, "defaults");
            Assert.Equal(1, catalog.Config.MinDaysBetweenMails);
            Assert.Equal(120, catalog.Config.DefaultDaysFallback);
        }

        [Test]
        public void Build_WithoutAContext_SkipsConditionAndStringChecks()
        {
            var steps = ValidSet();
            steps[1].CompleteWhen = OnbJson.Cond("{'type':'anything'}");

            StepCatalog catalog = StepCatalog.Build(steps, null, null);

            Assert.Equal(0, catalog.Errors.Count, AllErrors(catalog));
            Assert.Equal(5, catalog.All.Count);
        }

        // ---- warnings: the findings that drop nothing ----

        [Test]
        public void Warnings_TitleWithoutTheMentorPrefix_LongBodyAndMissingNames()
        {
            var steps = ValidSet();
            FakeStepContext context = ContextFor(steps);
            context.Russian["onb.research.title"] = "Plain title";
            context.Russian["onb.design_cpu.body"] = new string('x', 1201);
            context.Russian.Remove("onb.foundry_services.name");
            context.Russian.Remove("onb.foundry_services.done");

            StepCatalog catalog = Build(steps, context);

            Assert.Equal(0, catalog.Errors.Count, AllErrors(catalog));
            Assert.Equal(5, catalog.All.Count, "a warning drops nothing");
            string all = string.Join(" | ", catalog.Warnings);
            Assert.True(all.Contains("the title 'onb.research.title' does not start with " + StepValidator.MentorPrefixRu), all);
            Assert.True(all.Contains("the text 'onb.design_cpu.body' has 1201 characters, more than 1200"), all);
            Assert.True(all.Contains("no short name, the string 'onb.foundry_services.name' is missing"), all);
            Assert.True(all.Contains("the string 'onb.foundry_services.done' is missing"), all);
        }

        [Test]
        public void Warnings_ABodyOfExactlyTheLimit_IsFine()
        {
            var steps = ValidSet();
            FakeStepContext context = ContextFor(steps);
            context.Russian["onb.research.body"] = new string('x', 1200);

            Assert.Equal(0, Build(steps, context).Warnings.Count);
        }

        [Test]
        public void Warnings_HintButtonWithoutReminders()
        {
            var steps = ValidSet();
            steps[1].Mail.Buttons = new[] { "hint", "skipStep" };

            StepCatalog catalog = Build(steps);

            Assert.Equal(0, catalog.Errors.Count, AllErrors(catalog));
            Assert.True(catalog.Warnings.Any(w => w.Contains("the 'hint' button has no hints to show")), string.Join(" | ", catalog.Warnings));
        }

        [Test]
        public void Warnings_NamesAreNotRequiredOfTips()
        {
            var steps = ValidSet();
            FakeStepContext context = ContextFor(steps);
            context.Russian.Remove("onb.tip_debt.name");
            context.Russian.Remove("onb.tip_debt.done");

            Assert.Equal(0, Build(steps, context).Warnings.Count);
        }

        // ---- the other fields ----

        [Test]
        public void Validate_IdShape()
        {
            Assert.True(StepValidator.IsValidId("ab"));
            Assert.True(StepValidator.IsValidId("design_cpu"));
            Assert.True(StepValidator.IsValidId("tip2"));
            Assert.True(StepValidator.IsValidId(new string('a', 40)));
            Assert.False(StepValidator.IsValidId(new string('a', 41)));
            Assert.False(StepValidator.IsValidId("a"));
            Assert.False(StepValidator.IsValidId(""));
            Assert.False(StepValidator.IsValidId(null));
            Assert.False(StepValidator.IsValidId("Design"));
            Assert.False(StepValidator.IsValidId("design-cpu"));
            Assert.False(StepValidator.IsValidId("design cpu"));
        }

        [Test]
        public void Validate_OrderPriorityStreakAndKind()
        {
            var steps = ValidSet();
            steps[1].Order = 0;
            steps[2].Order = 10000;
            steps[3].Priority = "Normal";                 // exact lower case
            steps[4].StreakDays = 366;
            StepDefinition badKind = Seq("bad_kind", 40);
            badKind.Kind = (StepKind)7;
            StepDefinition streak = Seq("streak", 41);
            streak.StreakDays = -1;
            steps.Add(badKind);
            steps.Add(streak);

            StepCatalog catalog = Build(steps);

            Assert.True(HasError(catalog, "step 'research' skipped: order: 0 is outside 1..9999"), AllErrors(catalog));
            Assert.True(HasError(catalog, "step 'design_cpu' skipped: order: 10000 is outside 1..9999"), AllErrors(catalog));
            Assert.True(HasError(catalog, "step 'foundry_services' skipped: priority: unknown priority 'Normal'"), AllErrors(catalog));
            Assert.True(HasError(catalog, "step 'tip_debt' skipped: streakDays: 366 is outside 0..365"), AllErrors(catalog));
            Assert.True(HasError(catalog, "step 'bad_kind' skipped: kind: unknown kind"), AllErrors(catalog));
            Assert.True(HasError(catalog, "step 'streak' skipped: streakDays: -1"), AllErrors(catalog));
        }

        [Test]
        public void Validate_ASituationalStepNeedsItsTrigger()
        {
            var steps = ValidSet();
            steps[4].ShowWhen = null;

            StepCatalog catalog = Build(steps);

            Assert.True(HasError(catalog, "step 'tip_debt' skipped: showWhen: a situational step needs the condition that triggers it"), AllErrors(catalog));
        }

        [Test]
        public void Validate_OnlyKnownButtonsAndPriorities()
        {
            var steps = ValidSet();
            steps[1].Mail.Buttons = new[] { "skipStep", "fly" };
            steps[2].Priority = "critical";
            steps[3].Priority = "low";

            StepCatalog catalog = Build(steps);

            Assert.True(HasError(catalog, "step 'research' skipped: mail.buttons[1]: unknown button 'fly'"), AllErrors(catalog));
            Assert.NotNull(catalog.Find("design_cpu"));
            Assert.NotNull(catalog.Find("foundry_services"));
            CollectionsEqual(new[] { "skipAll", "skipStep", "hint", "openCpuDesigner", "openFactoryExpansion" }, StepValidator.Buttons);
            CollectionsEqual(new[] { "critical", "normal", "low" }, StepValidator.Priorities);
            CollectionsEqual(new[] { "idm", "fabless", "foundry" }, StepValidator.Tracks);
        }

        [Test]
        public void Validate_NullStepAndAThrowingContext_NeverThrow()
        {
            var errors = new List<string>();
            Assert.False(StepValidator.Validate(null, null, errors, null));
            Assert.Equal(1, errors.Count);

            errors.Clear();
            Assert.False(StepValidator.Validate(Seq("research", 20), new ThrowingContext(), errors, null));
            Assert.True(errors[0].Contains("could not be checked"), errors[0]);
        }

        [Test]
        public void Validate_ConditionNodeMissing_IsAnError()
        {
            var steps = ValidSet();
            steps[1].CompleteWhen = new ConditionRef();   // a reference without a node

            StepCatalog catalog = Build(steps);

            Assert.True(HasError(catalog, "step 'research' skipped: completeWhen: the condition is empty"), AllErrors(catalog));
        }

        [Test]
        public void MaxPlaceholderIndex_ReadsIndexesAndSkipsEscapes()
        {
            Assert.Equal(-1, StepValidator.MaxPlaceholderIndex(null));
            Assert.Equal(-1, StepValidator.MaxPlaceholderIndex(""));
            Assert.Equal(-1, StepValidator.MaxPlaceholderIndex("no placeholders"));
            Assert.Equal(0, StepValidator.MaxPlaceholderIndex("{0}"));
            Assert.Equal(2, StepValidator.MaxPlaceholderIndex("{2} before {0}"));
            Assert.Equal(11, StepValidator.MaxPlaceholderIndex("{11}"));
            Assert.Equal(1, StepValidator.MaxPlaceholderIndex("{0} {1:N0}"));
            Assert.Equal(3, StepValidator.MaxPlaceholderIndex("{0,5} {3,-4:C}"));
            Assert.Equal(-1, StepValidator.MaxPlaceholderIndex("{{5}} is an escaped brace"));
            Assert.Equal(0, StepValidator.MaxPlaceholderIndex("{{5}} and {0}"));
            Assert.Equal(-1, StepValidator.MaxPlaceholderIndex("{name} and { 1 } are not placeholders"));
        }

        // ---- the mapper ----

        [Test]
        public void Map_FullRecord_ReadsEveryField()
        {
            JsonObject json = OnbJson.Object(@"{
                'id': 'design_cpu', 'order': 30, 'kind': 'sequential', 'tracks': ['idm', 'fabless'],
                'showWhen': { 'type': 'onb.cpuProjectActive' },
                'completeWhen': { 'any': [ { 'type': 'onb.cpuProjectActive' }, { 'type': 'cpuCountAtLeast', 'count': 1 } ] },
                'streakDays': 3, 'priority': 'critical', 'pauseOnShow': true,
                'mail': { 'titleKey': 'onb.design_cpu.title', 'bodyKey': 'onb.design_cpu.body', 'args': ['company', 'founder'],
                          'buttons': ['openCpuDesigner', 'hint', 'skipStep'],
                          'variants': [ { 'when': { 'type': 'featureActive', 'id': 'bankruptcy-recovery' }, 'bodyKey': 'onb.design_cpu.body.rec' } ] },
                'reminders': [ { 'afterDays': 20, 'hintKey': 'onb.design_cpu.hint1', 'args': ['lowEndBudget'] }, { 'afterDays': 50, 'hintKey': 'onb.design_cpu.hint2' } ]
            }");
            var errors = new List<string>();

            StepDefinition step = StepMapper.Map(json, errors);

            Assert.NotNull(step, string.Join("; ", errors));
            Assert.Equal(0, errors.Count);
            Assert.Equal("design_cpu", step.Id);
            Assert.Equal(30, step.Order);
            Assert.Equal(StepKind.Sequential, step.Kind);
            Assert.Equal(new[] { "idm", "fabless" }, step.Tracks);
            Assert.True(step.InTrack("fabless"));
            Assert.False(step.InTrack("foundry"));
            Assert.False(step.InTrack(null));
            Assert.True(step.ShowWhen.Node.IsObject);
            Assert.True(step.CompleteWhen.Node.IsObject);
            Assert.Null(step.CompleteWhen.Compiled, "the logic never compiles a condition");
            Assert.False(step.CompleteOnShow);
            Assert.Equal(3, step.StreakDays);
            Assert.Equal("critical", step.Priority);
            Assert.True(step.PauseOnShow);
            Assert.Equal("onb.design_cpu.title", step.Mail.TitleKey);
            Assert.Equal("onb.design_cpu.body", step.Mail.BodyKey);
            Assert.Equal(new[] { "company", "founder" }, step.Mail.Args);
            Assert.Equal(new[] { "openCpuDesigner", "hint", "skipStep" }, step.Mail.Buttons);
            Assert.Equal(1, step.Mail.Variants.Length);
            Assert.Equal("onb.design_cpu.body.rec", step.Mail.Variants[0].BodyKey);
            Assert.True(step.Mail.Variants[0].When.Node.IsObject);
            Assert.Equal(2, step.Reminders.Length);
            Assert.Equal(20, step.Reminders[0].AfterDays);
            Assert.Equal("onb.design_cpu.hint1", step.Reminders[0].HintKey);
            Assert.Equal(new[] { "lowEndBudget" }, step.Reminders[0].Args);
            Assert.Equal(0, step.Reminders[1].Args.Length);
            Assert.Equal("onb.design_cpu.name", step.NameKey);
            Assert.Equal("onb.design_cpu.done", step.DoneKey);
        }

        [Test]
        public void Map_Defaults_AndUnknownFieldsAreIgnored()
        {
            JsonObject json = OnbJson.Object(@"{
                'id': 'welcome', 'tracks': ['idm'], 'completeOnShow': true, 'color': 'red', 'extra': { 'a': 1 },
                'mail': { 'titleKey': 't', 'bodyKey': 'b', 'unknown': [1, 2] }
            }");
            var errors = new List<string>();

            StepDefinition step = StepMapper.Map(json, errors);

            Assert.NotNull(step, string.Join("; ", errors));
            Assert.Equal(StepKind.Sequential, step.Kind, "kind defaults to sequential");
            Assert.Equal(0, step.Order);
            Assert.Equal("normal", step.Priority, "priority defaults to normal");
            Assert.True(step.CompleteOnShow);
            Assert.False(step.PauseOnShow);
            Assert.Equal(0, step.StreakDays);
            Assert.Null(step.ShowWhen);
            Assert.Null(step.CompleteWhen);
            Assert.Equal(0, step.Mail.Args.Length);
            Assert.Equal(0, step.Mail.Buttons.Length);
            Assert.Equal(0, step.Mail.Variants.Length);
            Assert.Equal(0, step.Reminders.Length);
        }

        [Test]
        public void Map_KindIsCaseInsensitive_SituationalIsRead()
        {
            var errors = new List<string>();
            StepDefinition step = StepMapper.Map(OnbJson.Object("{'id':'tip_x','kind':'Situational','tracks':['idm'],'mail':{'titleKey':'t','bodyKey':'b'}}"), errors);

            Assert.NotNull(step, string.Join("; ", errors));
            Assert.Equal(StepKind.Situational, step.Kind);
        }

        [Test]
        public void Map_WrongKinds_AreReportedAndTheRecordIsRefused()
        {
            var errors = new List<string>();
            StepDefinition step = StepMapper.Map(OnbJson.Object(@"{
                'id': 5, 'order': 'ten', 'kind': 'weekly', 'tracks': 'idm', 'showWhen': 'always', 'completeOnShow': 'yes', 'streakDays': 1.5,
                'priority': 3, 'pauseOnShow': 1,
                'mail': { 'titleKey': 1, 'bodyKey': 'b', 'args': ['ok', 2], 'buttons': 'skip', 'variants': [ 5, { 'bodyKey': 'x' } ] },
                'reminders': [ 'soon', { 'afterDays': 'ten', 'hintKey': 7, 'args': {} } ]
            }"), errors);

            Assert.Null(step);
            string all = string.Join(" | ", errors);
            foreach (string expected in new[]
            {
                "id: expected a string", "order: expected an integer", "kind: unknown kind 'weekly'", "tracks: expected an array of strings",
                "showWhen: expected an object", "completeOnShow: expected true or false", "streakDays: expected an integer", "priority: expected a string",
                "pauseOnShow: expected true or false", "mail.titleKey: expected a string", "mail.args[1]: expected a string",
                "mail.buttons: expected an array of strings", "mail.variants[0]: expected an object", "mail.variants[1].when: the condition is missing",
                "reminders[0]: expected an object", "reminders[1].afterDays: expected an integer", "reminders[1].hintKey: expected a string",
                "reminders[1].args: expected an array of strings"
            })
            {
                Assert.True(all.Contains(expected), "missing '" + expected + "' in: " + all);
            }
        }

        [Test]
        public void Map_MissingIdOrMail_IsRefused()
        {
            var errors = new List<string>();
            Assert.Null(StepMapper.Map(OnbJson.Object("{'tracks':['idm'],'mail':{}}"), errors));
            Assert.True(errors.Contains("id: required"), string.Join("; ", errors));

            errors.Clear();
            Assert.Null(StepMapper.Map(OnbJson.Object("{'id':'x1','tracks':['idm']}"), errors));
            Assert.True(errors.Contains("mail: the object is missing"), string.Join("; ", errors));

            errors.Clear();
            Assert.Null(StepMapper.Map(OnbJson.Object("{'id':'x1','mail':[]}"), errors));
            Assert.True(errors.Contains("mail: expected an object"), string.Join("; ", errors));

            errors.Clear();
            Assert.Null(StepMapper.Map(OnbJson.Object("{'id':'','mail':{}}"), errors));
            Assert.True(errors.Contains("id: required"), string.Join("; ", errors));

            Assert.Null(StepMapper.Map(null, errors));
            Assert.Null(StepMapper.Map(OnbJson.Object("{'mail':{}}"), null), "the error list is optional");
        }

        [Test]
        public void MapConfig_ReadsTheRhythm_AndUsesDefaultsForTheRest()
        {
            var warnings = new List<string>();
            OnboardingConfig config = StepMapper.MapConfig(OnbJson.Object("{'schema':1,'tutorialWindowDays':365,'minDaysBetweenMails':2,'defaultDaysFallback':90}"), warnings);

            Assert.Equal(365, config.TutorialWindowDays);
            Assert.Equal(2, config.MinDaysBetweenMails);
            Assert.Equal(90, config.DefaultDaysFallback);
            Assert.Equal(0, warnings.Count);

            OnboardingConfig defaults = StepMapper.MapConfig(OnbJson.Object("{'schema':1}"), warnings);
            Assert.Equal(730, defaults.TutorialWindowDays);
            Assert.Equal(1, defaults.MinDaysBetweenMails);
            Assert.Equal(120, defaults.DefaultDaysFallback);
            Assert.Equal(0, warnings.Count);

            OnboardingConfig none = StepMapper.MapConfig(null, null);
            Assert.Equal(730, none.TutorialWindowDays);
        }

        [Test]
        public void MapConfig_OutOfRangeNumbersAreBroughtIntoTheRange()
        {
            var warnings = new List<string>();
            OnboardingConfig config = StepMapper.MapConfig(OnbJson.Object("{'tutorialWindowDays':5,'minDaysBetweenMails':0,'defaultDaysFallback':99999}"), warnings);

            Assert.Equal(30, config.TutorialWindowDays);
            Assert.Equal(1, config.MinDaysBetweenMails, "never more than a mail a day (NFR-5)");
            Assert.Equal(3650, config.DefaultDaysFallback);
            Assert.Equal(3, warnings.Count);
            Assert.True(warnings[0].Contains("config.tutorialWindowDays: 5 is outside 30..3650, 30 is used"), warnings[0]);

            Assert.Equal(3650, StepMapper.MapConfig(OnbJson.Object("{'tutorialWindowDays':100000}"), null).TutorialWindowDays);
            Assert.Equal(365, StepMapper.MapConfig(OnbJson.Object("{'minDaysBetweenMails':1000}"), null).MinDaysBetweenMails);
        }

        [Test]
        public void MapConfig_WrongKindsKeepTheDefaults()
        {
            var warnings = new List<string>();
            OnboardingConfig config = StepMapper.MapConfig(OnbJson.Object("{'tutorialWindowDays':'long','minDaysBetweenMails':true,'defaultDaysFallback':null}"), warnings);

            Assert.Equal(730, config.TutorialWindowDays);
            Assert.Equal(1, config.MinDaysBetweenMails);
            Assert.Equal(120, config.DefaultDaysFallback, "null counts as a wrong kind");
            Assert.Equal(3, warnings.Count);
            Assert.True(warnings[0].Contains("config.tutorialWindowDays: expected a number, 730 is used"), warnings[0]);
        }

        [Test]
        public void MapConfig_ARoundedNumberIsAccepted()
        {
            Assert.Equal(400, StepMapper.MapConfig(OnbJson.Object("{'tutorialWindowDays':400.4}"), null).TutorialWindowDays);
        }

        [Test]
        public void ConditionRef_ToStringShowsTheNode()
        {
            Assert.Equal("{\"type\":\"researchActive\"}", OnbJson.Cond("{'type':'researchActive'}").ToString());
            Assert.Equal("(none)", new ConditionRef().ToString());
            Assert.True(Seq("research", 20).ToString().Contains("research"));
        }

        // ---- helpers ----

        private static void CollectionsEqual(string[] expected, IReadOnlyList<string> actual)
        {
            Assert.Equal(expected, actual.ToArray());
        }

        private sealed class ThrowingContext : IStepValidationContext
        {
            public bool ValidateCondition(JsonNode node, IList<string> errors, string path)
            {
                throw new InvalidOperationException("no parser today");
            }

            public bool HasString(string key)
            {
                throw new InvalidOperationException("no tables today");
            }

            public string GetRussianText(string key)
            {
                throw new InvalidOperationException("no tables today");
            }
        }
    }
}
