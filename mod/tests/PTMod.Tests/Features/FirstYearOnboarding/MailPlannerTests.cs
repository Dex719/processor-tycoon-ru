using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.L10n;
using PTMod.Features.FirstYearOnboarding.Logic;

namespace PTMod.Tests.Features.FirstYearOnboarding
{
    /// <summary>The shipped string tables as the planner reads them: one language, <c>string.Format</c> for the placeholders. Internal: the runner never discovers it.</summary>
    internal sealed class TableTexts : IMailTexts
    {
        private readonly L10nTables _tables;
        private readonly string _language;

        public TableTexts(L10nTables tables, string language)
        {
            _tables = tables;
            _language = language;
        }

        public bool Has(string key)
        {
            return key != null && _tables.Contains(_language, key);
        }

        public string Text(string key, params object[] args)
        {
            string raw;
            if (!_tables.TryGet(_language, key, out raw)) return "[" + key + "]";
            return args == null || args.Length == 0 ? raw : string.Format(raw, args);
        }
    }

    /// <summary>A placeholder source with fixed values. Internal: the runner never discovers it.</summary>
    internal sealed class FixedSource : IPlaceholderSource
    {
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "highEndBudget", "$599" }, { "midRangeBudget", "$249" }, { "lowEndBudget", "$149" }, { "industriesBudget", "$79" },
            { "debtLimit", "$111M" }, { "defaultDays", "120" }, { "money", "$5,000" }, { "company", "Garage Chips" }, { "founder", "Alex" }
        };

        public bool TryGet(string name, out string value)
        {
            return Values.TryGetValue(name, out value);
        }
    }

    /// <summary>
    /// The mails of the tutorial before they are sent (TSK-012, FR-5, FR-8, FR-13 item 2): the tag, the keys of the title and the body with their variants,
    /// the filled arguments, the priority, the buttons, the line "Already done", the repeat and the closing summary. The planner reads the shipped
    /// <c>steps.json</c> and the shipped Russian and English tables.
    /// </summary>
    [Covers(typeof(MailPlanner), typeof(PlannedMail), typeof(PlannedAction), typeof(IMailTexts), typeof(MailTags))]
    public class MailPlannerTests
    {
        private static readonly ShippedContext Shipped = ShippedContext.Load();
        private static readonly StepCatalog Catalog = Shipped.BuildCatalog();
        private static readonly TableTexts Ru = new TableTexts(Shipped.Tables, "ru");
        private static readonly TableTexts En = new TableTexts(Shipped.Tables, "en");

        private static PlannedMail Plan(string stepId, string track = "idm", IList<string> caughtUp = null, bool isRepeat = false,
                                        Func<ConditionRef, bool> evaluate = null, IMailTexts texts = null)
        {
            StepDefinition step = Catalog.Find(stepId);
            Assert.NotNull(step, "the shipped content has no step " + stepId);
            return MailPlanner.PlanStep(step, track, caughtUp, isRepeat, texts ?? Ru, new FixedSource(), evaluate);
        }

        // ---- the tag, the keys, the arguments ----

        [Test]
        public void TheWelcomeMail_IsCritical_HasItsTag_TheTitleKey_AndTheCompanyAndFounder()
        {
            PlannedMail mail = Plan("welcome");

            Assert.Equal("onb:welcome", mail.Tag);
            Assert.Equal("welcome", mail.StepId);
            Assert.False(mail.IsRepeat);
            Assert.Equal("onb.welcome.title", mail.TitleKey);
            Assert.Equal("onb.welcome.body", mail.BodyKey);
            Assert.Equal(new[] { "Garage Chips", "Alex" }, mail.Args, "args in the order of steps.json: company, founder");
            Assert.Equal(MailPriority.Critical, mail.Priority, "FR-2 item 3: the greeting is not held back by the limits of the day");
            Assert.Null(mail.InlineTitle);
            Assert.Null(mail.StatusLine);
        }

        [Test]
        public void TheReleaseMail_GetsTheFourBudgets_InTheOrderOfTheArgs()
        {
            PlannedMail mail = Plan("release_price");

            Assert.Equal(new[] { "$599", "$249", "$149", "$79" }, mail.Args);
            Assert.Equal(MailPriority.Normal, mail.Priority);
        }

        [Test]
        public void TheDebtTip_IsCritical_AndGetsTheLimitAndTheDays()
        {
            PlannedMail mail = Plan("tip_debt");

            Assert.Equal(new[] { "$111M", "120" }, mail.Args);
            Assert.Equal(MailPriority.Critical, mail.Priority);
            Assert.Equal("onb:tip_debt", mail.Tag);
        }

        [Test]
        public void TheTitleAndTheBody_FillWithTheirArgs_InBothLanguages()
        {
            PlannedMail mail = Plan("welcome");

            Assert.True(Ru.Text(mail.TitleKey, (object[])mail.Args).StartsWith(StepValidator.MentorPrefixRu + " ", StringComparison.Ordinal), "the Russian title starts with the mentor");
            Assert.True(Ru.Text(mail.TitleKey, (object[])mail.Args).Contains("Garage Chips"), "the company is in the title");
            Assert.True(En.Text(mail.TitleKey, (object[])mail.Args).StartsWith("[Mentor] ", StringComparison.Ordinal));
            Assert.True(En.Text(mail.BodyKey, (object[])mail.Args).Contains("Alex"), "the founder is in the English body");
        }

        // ---- the variants of the body ----

        [Test]
        public void TheBody_FollowsTheTrack_WhenTheTablesHaveAVariant()
        {
            Assert.Equal("onb.design_cpu.body", Plan("design_cpu", "idm").BodyKey);
            Assert.Equal("onb.design_cpu.body.fabless", Plan("design_cpu", "fabless").BodyKey, "FR-2: the fabless text");
            Assert.Equal("onb.production.body.fabless", Plan("production", "fabless").BodyKey);
            Assert.Equal("onb.research.body", Plan("research", "fabless").BodyKey, "no variant for this track: the main text");
        }

        [Test]
        public void TheBody_FollowsTheCondition_WhenARecoveryVariantApplies()
        {
            var asked = new List<string>();
            Func<ConditionRef, bool> recoveryOn = condition =>
            {
                asked.Add(condition.ToString());
                return true;
            };

            Assert.Equal("onb.tip_default.body.rec", Plan("tip_default", evaluate: recoveryOn).BodyKey);
            Assert.Equal(1, asked.Count);
            Assert.True(asked[0].Contains("featureActive") && asked[0].Contains("bankruptcy-recovery"), asked[0]);

            Assert.Equal("onb.tip_default.body", Plan("tip_default", evaluate: c => false).BodyKey);
            Assert.Equal("onb.tip_default.body", Plan("tip_default", evaluate: null).BodyKey, "without a world no variant applies");
        }

        [Test]
        public void ACondition_Wins_OverTheTrackVariant_AndATrackVariantOverTheMainText()
        {
            var spec = new MailSpec
            {
                TitleKey = "t",
                BodyKey = "b",
                Variants = new[] { new BodyVariant { When = OnbJson.Cond("{'type':'researchActive'}"), BodyKey = "b.special" } }
            };
            var texts = new KeysOnly("b.special", "b.idm", "b");

            Assert.Equal("b.special", MailPlanner.ChooseBodyKey(spec, "idm", texts, c => true));
            Assert.Equal("b.idm", MailPlanner.ChooseBodyKey(spec, "idm", texts, c => false));
            Assert.Equal("b", MailPlanner.ChooseBodyKey(spec, "foundry", texts, c => false));
        }

        [Test]
        public void AVariantWhoseKeyIsMissing_IsNotChosen()
        {
            var spec = new MailSpec
            {
                BodyKey = "b",
                Variants = new[] { new BodyVariant { When = OnbJson.Cond("{'type':'researchActive'}"), BodyKey = "b.gone" } }
            };

            Assert.Equal("b", MailPlanner.ChooseBodyKey(spec, "idm", new KeysOnly("b"), c => true));
        }

        [Test]
        public void ACondition_ThatThrows_IsFalse()
        {
            var spec = new MailSpec
            {
                BodyKey = "b",
                Variants = new[] { new BodyVariant { When = OnbJson.Cond("{'type':'researchActive'}"), BodyKey = "b.special" } }
            };

            Assert.Equal("b", MailPlanner.ChooseBodyKey(spec, null, new KeysOnly("b", "b.special"), c => { throw new InvalidOperationException("boom"); }));
        }

        // ---- the buttons ----

        [Test]
        public void TheButtons_FollowTheStep_InOrder_AndTheSkipsWorkOnce()
        {
            PlannedMail design = Plan("design_cpu");

            Assert.Equal(new[] { "openCpuDesigner", "hint", "skipStep" }, design.Actions.Select(a => a.Id).ToArray());
            Assert.True(design.Actions[0].Repeatable, "the window opens as often as pressed");
            Assert.True(design.Actions[1].Repeatable, "the hint button is Repeatable");
            Assert.False(design.Actions[2].Repeatable, "FR-8: skip is used once");
            Assert.Equal("onb.button.openCpu", design.Actions[0].LabelKey);
            Assert.Equal("onb.button.hint", design.Actions[1].LabelKey);
            Assert.Equal("onb.button.skipStep", design.Actions[2].LabelKey);

            PlannedAction skipAll = Plan("welcome").Actions.Single();
            Assert.Equal("skipAll", skipAll.Id);
            Assert.False(skipAll.Repeatable);
            Assert.Equal("onb.button.skipAll", skipAll.LabelKey);

            Assert.Equal("openFactoryExpansion", Plan("factory_expansion", "foundry").Actions[0].Id);
            Assert.True(Plan("factory_expansion", "foundry").Actions[0].Repeatable);
        }

        [Test]
        public void EveryButtonLabelAndTooltip_IsInBothTables()
        {
            foreach (StepDefinition step in Catalog.All)
            {
                foreach (PlannedAction action in MailPlanner.ActionsOf(step))
                {
                    foreach (string key in new[] { action.LabelKey, action.TooltipKey })
                    {
                        Assert.True(Ru.Has(key), step.Id + "/" + action.Id + ": no Russian text for " + key);
                        Assert.True(En.Has(key), step.Id + "/" + action.Id + ": no English text for " + key);
                    }
                }
            }
        }

        [Test]
        public void TheHintButton_IsGray_InAStepWithoutHints()
        {
            var step = new StepDefinition { Id = "x", Mail = new MailSpec { Buttons = new[] { "hint" } } };

            PlannedAction hint = MailPlanner.ActionsOf(step).Single();

            Assert.False(hint.Enabled);
            Assert.Equal("onb.hint.none", hint.DisabledReasonKey);

            Assert.True(Plan("research").Actions.Single(a => a.Id == "hint").Enabled, "research has two hints");
        }

        [Test]
        public void AnUnknownButton_IsLeftOut()
        {
            var step = new StepDefinition { Id = "x", Mail = new MailSpec { Buttons = new[] { "skipStep", "teleport", null } } };

            Assert.Equal(new[] { "skipStep" }, MailPlanner.ActionsOf(step).Select(a => a.Id).ToArray());
            Assert.Null(MailPlanner.ActionFor("teleport", step));
            Assert.Equal(0, MailPlanner.ActionsOf(null).Count);
        }

        // ---- priorities ----

        [Test]
        public void ThePriority_IsReadFromTheName()
        {
            Assert.Equal(MailPriority.Critical, MailPlanner.ParsePriority("critical"));
            Assert.Equal(MailPriority.Critical, MailPlanner.ParsePriority("Critical"));
            Assert.Equal(MailPriority.Low, MailPlanner.ParsePriority("low"));
            Assert.Equal(MailPriority.Normal, MailPlanner.ParsePriority("normal"));
            Assert.Equal(MailPriority.Normal, MailPlanner.ParsePriority(null));
            Assert.Equal(MailPriority.Normal, MailPlanner.ParsePriority("urgent"));
        }

        [Test]
        public void TheCriticalSteps_AreTheWelcomeAndTheTwoMoneyTips()
        {
            string[] critical = Catalog.All.Where(s => MailPlanner.ParsePriority(s.Priority) == MailPriority.Critical).Select(s => s.Id).OrderBy(id => id).ToArray();

            Assert.Equal(new[] { "tip_debt", "tip_default", "welcome" }, critical, "design.md: critical for welcome, tip_debt, tip_default");
        }

        // ---- the line "Already done" ----

        [Test]
        public void AMailAfterCaughtUpSteps_CarriesTheLineAlreadyDone()
        {
            PlannedMail mail = Plan("release_price", caughtUp: new[] { "research", "design_cpu" });

            Assert.Equal("Уже сделано: исследование запущено, проект процессора запущен", mail.StatusLine);
        }

        [Test]
        public void TheLineAlreadyDone_IsEnglishInEnglish()
        {
            PlannedMail mail = Plan("release_price", caughtUp: new[] { "research" }, texts: En);

            Assert.True(mail.StatusLine.StartsWith("Already done: ", StringComparison.Ordinal), mail.StatusLine);
        }

        [Test]
        public void TheLineAlreadyDone_SkipsAStepWithoutAPhrase_AndIsNullForNone()
        {
            Assert.Null(Plan("release_price").StatusLine);
            Assert.Null(Plan("release_price", caughtUp: new string[0]).StatusLine);
            Assert.Null(Plan("release_price", caughtUp: new[] { "welcome" }).StatusLine, "welcome has no 'done' phrase: nothing to say");
            Assert.Null(Plan("release_price", caughtUp: new[] { "no_such_step", null, string.Empty }).StatusLine);
            Assert.Equal("Уже сделано: исследование запущено", Plan("release_price", caughtUp: new[] { "welcome", "research" }).StatusLine);
        }

        [Test]
        public void TheCaughtUpList_IsNotChanged()
        {
            var list = new List<string> { "research" };

            Plan("release_price", caughtUp: list);

            Assert.Equal(new[] { "research" }, list.ToArray());
        }

        // ---- the repeat ----

        [Test]
        public void ARepeat_HasTheRepeatTag_ATitleWithTheWord_AndNoLineAlreadyDone()
        {
            PlannedMail mail = Plan("research", isRepeat: true, caughtUp: new[] { "welcome" });

            Assert.Equal("onb:research:repeat", mail.Tag, "FR-13 item 2");
            Assert.True(mail.IsRepeat);
            Assert.Null(mail.TitleKey, "the title is a ready text");
            Assert.Equal(Ru.Text("onb.research.title") + " (повтор)", mail.InlineTitle);
            Assert.Equal("onb.research.body", mail.BodyKey);
            Assert.Null(mail.StatusLine);
            Assert.Equal(new[] { "hint", "skipStep" }, mail.Actions.Select(a => a.Id).ToArray(), "the buttons are the same");
        }

        [Test]
        public void ARepeat_FillsTheTitleWithTheArguments()
        {
            PlannedMail mail = Plan("welcome", isRepeat: true);

            Assert.Equal(Ru.Text("onb.welcome.title", "Garage Chips") + " (повтор)", mail.InlineTitle);
            Assert.Equal(new[] { "Garage Chips", "Alex" }, mail.Args);
        }

        // ---- every shipped mail ----

        [Test]
        public void EveryShippedStep_PlansInBothLanguages_WithNothingLeftUnfilled()
        {
            int planned = 0;
            foreach (StepDefinition step in Catalog.All)
            {
                foreach (string track in step.Tracks)
                {
                    foreach (IMailTexts texts in new IMailTexts[] { Ru, En })
                    {
                        string where = step.Id + "/" + track;
                        PlannedMail mail = MailPlanner.PlanStep(step, track, new[] { "research" }, false, texts, new FixedSource(), c => false);
                        planned++;

                        Assert.True(texts.Has(mail.TitleKey), where + ": no title " + mail.TitleKey);
                        Assert.True(texts.Has(mail.BodyKey), where + ": no body " + mail.BodyKey);
                        Assert.False(Array.IndexOf(mail.Args, PlaceholderResolver.Unknown) >= 0, where + ": a placeholder is unknown");

                        string title = texts.Text(mail.TitleKey, (object[])mail.Args);
                        string body = texts.Text(mail.BodyKey, (object[])mail.Args);
                        Assert.False(title.Contains("{") || body.Contains("{"), where + ": a placeholder was left in the text");
                        Assert.True(body.Length <= StepValidator.MaxBodyLength + 200, where + ": the body is " + body.Length + " characters long");
                        Assert.True(mail.Tag == "onb:" + step.Id, where + ": tag " + mail.Tag);
                    }
                }
            }
            Assert.Equal(Catalog.All.Sum(s => s.Tracks.Length) * 2, planned, "every step in every track of it, in two languages");
            Assert.True(planned >= 60, "the shipped content plans " + planned + " mails");
        }

        // ---- the summary ----

        private static OnboardingState SampleRun()
        {
            var state = new OnboardingState { Mode = OnboardingMode.Completed, Track = "idm" };
            state.GetOrAdd("welcome").Status = StepStatus.Done;
            state.GetOrAdd("research").Status = StepStatus.Done;
            state.GetOrAdd("design_cpu").Status = StepStatus.Skipped;
            state.GetOrAdd("dev_wait").Status = StepStatus.NotApplicable;
            state.GetOrAdd("release_price").Status = StepStatus.Current;
            // production, first_sales, oem_offer, finance have no record: pending
            return state;
        }

        [Test]
        public void TheSummary_ListsEveryStepOfTheTrack_ExceptTheClosingOne_WithHowItEnded()
        {
            string text = MailPlanner.SummaryText(SampleRun(), Catalog, Ru);

            string[] lines = text.Split('\n');
            Assert.Equal("Итог по шагам:", lines[0]);
            Assert.Equal(1 + Catalog.StepsFor("idm", StepKind.Sequential).Count - 1, lines.Length, "the heading and the nine steps, not the closing one");
            Assert.Equal("Знакомство: выполнен", lines[1]);
            Assert.Equal("Первое исследование: выполнен", lines[2]);
            Assert.Equal("Проект процессора: пропущен", lines[3]);
            Assert.Equal("Разработка процессора: не нужен вашей компании", lines[4]);
            Assert.Equal("Цена и выпуск: не пройден", lines[5], "a current step is open, not done");
            Assert.Equal("Запуск производства: не пройден", lines[6], "a step with no record is open");
            Assert.False(text.Contains(Ru.Text("onb.graduation.name")), "the closing step is not in its own summary");
        }

        [Test]
        public void TheSummary_IsEnglishInEnglish()
        {
            string text = MailPlanner.SummaryText(SampleRun(), Catalog, En);

            Assert.Equal(En.Text("onb.summary.header"), text.Split('\n')[0]);
            Assert.Equal(En.Text("onb.summary.line", En.Text("onb.welcome.name"), En.Text("onb.summary.done")), text.Split('\n')[1]);
        }

        [Test]
        public void TheSummaryMail_IsTheClosingStepsMail_WithTheListUnderTheText()
        {
            PlannedMail mail = MailPlanner.PlanSummary(SampleRun(), Catalog, Ru, new FixedSource());

            Assert.Equal("onb:graduation", mail.Tag);
            Assert.Equal("graduation", mail.StepId);
            Assert.Equal("onb.graduation.title", mail.TitleKey);
            Assert.Equal("onb.graduation.body", mail.BodyKey);
            Assert.Equal(0, mail.Args.Length);
            Assert.Equal(0, mail.Actions.Count);
            Assert.Equal(MailPriority.Normal, mail.Priority);
            Assert.False(mail.IsRepeat);
            Assert.Equal(MailPlanner.SummaryText(SampleRun(), Catalog, Ru), mail.StatusLine);
        }

        [Test]
        public void TheSummaryMail_WithoutAClosingStep_UsesTheDefaultKeys()
        {
            StepCatalog noClosing = OnbSteps.Catalog(OnbSteps.Info("welcome", 10), OnbSteps.Seq("research", 20, "{'type':'researchActive'}"));
            var state = new OnboardingState { Mode = OnboardingMode.Completed, Track = "idm" };

            PlannedMail mail = MailPlanner.PlanSummary(state, noClosing, Ru, new FixedSource());

            Assert.Equal(MailPlanner.DefaultSummaryTitleKey, mail.TitleKey);
            Assert.Equal(MailPlanner.DefaultSummaryBodyKey, mail.BodyKey);
            Assert.Equal(MailPriority.Normal, mail.Priority);
            Assert.True(Ru.Has(mail.TitleKey) && Ru.Has(mail.BodyKey), "the default keys exist");
        }

        [Test]
        public void TheSummary_WithoutATrack_OrACatalog_IsNull()
        {
            Assert.Null(MailPlanner.SummaryText(new OnboardingState(), Catalog, Ru));
            Assert.Null(MailPlanner.SummaryText(SampleRun(), null, Ru));
            Assert.Null(MailPlanner.SummaryText(null, Catalog, Ru));
        }

        [Test]
        public void TheSummaryStatuses_AreTheFourWords()
        {
            Assert.Equal("onb.summary.done", MailPlanner.SummaryStatusKey(StepStatus.Done));
            Assert.Equal("onb.summary.skipped", MailPlanner.SummaryStatusKey(StepStatus.Skipped));
            Assert.Equal("onb.summary.na", MailPlanner.SummaryStatusKey(StepStatus.NotApplicable));
            Assert.Equal("onb.summary.open", MailPlanner.SummaryStatusKey(StepStatus.Pending));
            Assert.Equal("onb.summary.open", MailPlanner.SummaryStatusKey(StepStatus.Current));
        }

        // ---- tags ----

        [Test]
        public void TheTag_IsBuiltAndReadBack()
        {
            Assert.Equal("onb:research", MailTags.For("research", false));
            Assert.Equal("onb:research:repeat", MailTags.For("research", true));

            string id;
            bool repeat;
            Assert.True(MailTags.TryParse("onb:research", out id, out repeat));
            Assert.Equal("research", id);
            Assert.False(repeat);

            Assert.True(MailTags.TryParse("onb:release_price:repeat", out id, out repeat));
            Assert.Equal("release_price", id);
            Assert.True(repeat);
        }

        [Test]
        public void ATagThatIsNotOurs_OrIsBroken_IsNotParsed()
        {
            string id;
            bool repeat;
            foreach (string tag in new[] { null, string.Empty, "scn:briefing:1", "onb", "onb:", "onb::repeat", "onb:a:b", "ONB:research", "es:research", "xonb:research" })
            {
                Assert.False(MailTags.TryParse(tag, out id, out repeat), "'" + tag + "' is not a tutorial tag");
                Assert.Null(id);
                Assert.False(repeat);
            }
        }

        // ---- helpers ----

        private sealed class KeysOnly : IMailTexts
        {
            private readonly HashSet<string> _keys;

            public KeysOnly(params string[] keys)
            {
                _keys = new HashSet<string>(keys, StringComparer.Ordinal);
            }

            public bool Has(string key) { return _keys.Contains(key); }

            public string Text(string key, params object[] args) { return key; }
        }
    }
}
