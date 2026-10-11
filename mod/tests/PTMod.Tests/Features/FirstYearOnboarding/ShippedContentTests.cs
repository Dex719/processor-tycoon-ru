using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.L10n;
using PTMod.Features.FirstYearOnboarding.Logic;

namespace PTMod.Tests.Features.FirstYearOnboarding
{
    /// <summary>
    /// The shipped <c>steps.json</c> and <c>config.json</c> (TSK-004) and the texts in <c>L10n/{ru,en}/first-year-onboarding.json</c> (TSK-005), read from
    /// the source tree and checked the way the game checks them at start: through the real parser of conditions of the core with the real predicates, and the
    /// shipped string tables. They have to build the catalog with no errors and no warnings, follow the table "Полный набор шагов" of design.md, and the
    /// texts have to meet the rules of NFR-4 and NFR-5 (3-6 paragraphs, up to 1200 characters, the action at the end, the numbers of the game).
    /// </summary>
    [Covers(typeof(StepCatalog), typeof(StepValidator), typeof(StepMapper), typeof(StepDefinition))]
    public class ShippedContentTests
    {
        private static readonly string[] Idm =
        {
            "welcome", "research", "design_cpu", "dev_wait", "release_price", "production", "first_sales", "oem_offer", "finance", "graduation"
        };

        private static readonly string[] Foundry =
        {
            "welcome", "research", "foundry_services", "first_client", "factory_expansion", "finance", "graduation"
        };

        private static StepCatalog Catalog(ShippedContext context)
        {
            StepCatalog catalog = context.BuildCatalog();
            Assert.Equal(0, catalog.Errors.Count, "errors in the shipped steps:\n  " + string.Join("\n  ", catalog.Errors));
            Assert.Equal(0, catalog.Warnings.Count, "warnings in the shipped steps:\n  " + string.Join("\n  ", catalog.Warnings));
            return catalog;
        }

        private static string[] Ids(IEnumerable<StepDefinition> steps)
        {
            return steps.Select(s => s.Id).ToArray();
        }

        // ---- the steps ----

        [Test]
        public void Steps_PassTheCatalog_WithoutErrorsOrWarnings()
        {
            ShippedContext context = ShippedContext.Load();
            StepCatalog catalog = Catalog(context);

            Assert.Equal(19, catalog.All.Count, "13 steps of the tracks and 6 tips");
            Assert.False(catalog.IsEmpty);
        }

        [Test]
        public void Steps_TheThreeTracksHaveTheDesignedLength_AC_3_2()
        {
            StepCatalog catalog = Catalog(ShippedContext.Load());

            Assert.Equal(10, catalog.SequentialCount("idm"));
            Assert.Equal(10, catalog.SequentialCount("fabless"));
            Assert.Equal(7, catalog.SequentialCount("foundry"));
            Assert.Equal(Idm, Ids(catalog.StepsFor("idm", StepKind.Sequential)), "welcome -> research -> design_cpu -> dev_wait -> release_price -> production -> first_sales -> oem_offer -> finance -> graduation");
            Assert.Equal(Idm, Ids(catalog.StepsFor("fabless", StepKind.Sequential)));
            Assert.Equal(Foundry, Ids(catalog.StepsFor("foundry", StepKind.Sequential)));
        }

        [Test]
        public void Steps_SixTipsInTheShippedFile()
        {
            StepCatalog catalog = Catalog(ShippedContext.Load());

            string[] all = { "tip_debt", "tip_default", "tip_contract_signed", "tip_missed_sales", "tip_year_summary", "tip_old_cpu" };
            Assert.Equal(all, Ids(catalog.StepsFor("idm", StepKind.Situational)));
            Assert.Equal(all, Ids(catalog.StepsFor("fabless", StepKind.Situational)));
            Assert.Equal(new[] { "tip_debt", "tip_default", "tip_year_summary" }, Ids(catalog.StepsFor("foundry", StepKind.Situational)));
            Assert.Equal(6, catalog.All.Count(s => s.Kind == StepKind.Situational));
            Assert.True(catalog.All.Where(s => s.Kind == StepKind.Situational).All(s => s.CompleteOnShow && s.ShowWhen != null), "a tip triggers by showWhen and is done when sent");
        }

        [Test]
        public void Steps_OnlyWelcomeAndTheTwoMoneyTipsAreCritical()
        {
            StepCatalog catalog = Catalog(ShippedContext.Load());

            Assert.Equal(new[] { "welcome", "tip_debt", "tip_default" }, catalog.All.Where(s => s.Priority == "critical").Select(s => s.Id).ToArray());
            Assert.True(catalog.All.Where(s => s.Priority != "critical").All(s => s.Priority == "normal"));
        }

        [Test]
        public void Steps_TheInformationStepsAreDoneOnShow_TheOthersTrackTheGame()
        {
            StepCatalog catalog = Catalog(ShippedContext.Load());

            foreach (string id in new[] { "welcome", "dev_wait", "finance", "graduation" })
            {
                Assert.True(catalog.Find(id).CompleteOnShow, id + " is an information step");
                Assert.Null(catalog.Find(id).CompleteWhen);
            }
            foreach (string id in new[] { "research", "design_cpu", "release_price", "production", "first_sales", "oem_offer", "foundry_services", "first_client", "factory_expansion" })
            {
                Assert.False(catalog.Find(id).CompleteOnShow, id + " waits for the player");
                Assert.NotNull(catalog.Find(id).CompleteWhen);
            }
        }

        [Test]
        public void Steps_ShowWhenOnlyWhereTheDesignTableHasIt()
        {
            StepCatalog catalog = Catalog(ShippedContext.Load());

            var withShowWhen = catalog.All.Where(s => s.Kind == StepKind.Sequential && s.ShowWhen != null).Select(s => s.Id).OrderBy(i => i, StringComparer.Ordinal).ToArray();
            Assert.Equal(new[] { "dev_wait", "finance", "oem_offer" }, withShowWhen);
        }

        [Test]
        public void Steps_RemindersFollowTheDesignTable()
        {
            StepCatalog catalog = Catalog(ShippedContext.Load());
            var expected = new Dictionary<string, int[]>
            {
                { "research", new[] { 15, 40 } }, { "design_cpu", new[] { 20, 50 } }, { "production", new[] { 10, 30 } },
                { "first_sales", new[] { 20, 45 } }, { "oem_offer", new[] { 30, 90 } }, { "foundry_services", new[] { 20 } },
                { "first_client", new[] { 30, 90 } }, { "factory_expansion", new[] { 60 } }
            };

            foreach (StepDefinition step in catalog.All)
            {
                int[] days;
                if (expected.TryGetValue(step.Id, out days))
                    Assert.Equal(days, step.Reminders.Select(r => r.AfterDays).ToArray(), step.Id);
                else
                    Assert.Equal(0, step.Reminders.Length, step.Id + " has no reminders");
            }
            Assert.Equal(new[] { "lowEndBudget" }, catalog.Find("first_sales").Reminders[0].Args);
        }

        [Test]
        public void Steps_ButtonsAndPlaceholdersOfTheMails()
        {
            StepCatalog catalog = Catalog(ShippedContext.Load());

            Assert.Equal(new[] { "skipAll" }, catalog.Find("welcome").Mail.Buttons);
            Assert.Equal(new[] { "company", "founder" }, catalog.Find("welcome").Mail.Args);
            Assert.Equal(new[] { "openCpuDesigner", "hint", "skipStep" }, catalog.Find("design_cpu").Mail.Buttons);
            Assert.Equal(new[] { "openFactoryExpansion", "hint", "skipStep" }, catalog.Find("factory_expansion").Mail.Buttons);
            Assert.Equal(new[] { "openFactoryExpansion" }, catalog.Find("tip_missed_sales").Mail.Buttons);
            Assert.Equal(new[] { "highEndBudget", "midRangeBudget", "lowEndBudget", "industriesBudget" }, catalog.Find("release_price").Mail.Args);
            Assert.Equal(new[] { "debtLimit", "defaultDays" }, catalog.Find("tip_debt").Mail.Args);
            Assert.Equal(new[] { "defaultDays" }, catalog.Find("tip_default").Mail.Args);
            Assert.Equal(new[] { "money" }, catalog.Find("finance").Mail.Args);
            Assert.Equal(7, catalog.Find("tip_missed_sales").StreakDays);

            // A step with a "Hint" button has the hints to show.
            foreach (StepDefinition step in catalog.All.Where(s => s.Mail.Buttons.Contains("hint")))
                Assert.True(step.Reminders.Length > 0, step.Id + " has the Hint button");
            // Only the welcome mail offers to skip everything; every step with a condition to wait for offers to skip itself.
            Assert.Equal(new[] { "welcome" }, catalog.All.Where(s => s.Mail.Buttons.Contains("skipAll")).Select(s => s.Id).ToArray());
            foreach (StepDefinition step in catalog.All.Where(s => s.Kind == StepKind.Sequential && s.CompleteWhen != null))
                Assert.True(step.Mail.Buttons.Contains("skipStep"), step.Id + " can be skipped");
        }

        [Test]
        public void Steps_TipDefault_HasTheVariantForBankruptcyRecovery()
        {
            StepCatalog catalog = Catalog(ShippedContext.Load());
            StepDefinition tip = catalog.Find("tip_default");

            Assert.Equal(1, tip.Mail.Variants.Length);
            Assert.Equal("onb.tip_default.body.rec", tip.Mail.Variants[0].BodyKey);
            Assert.Equal("{\"type\":\"featureActive\",\"id\":\"bankruptcy-recovery\"}", tip.Mail.Variants[0].When.ToString());
        }

        [Test]
        public void Steps_FablessAndIdmShareTheSteps_FoundryHasItsOwn()
        {
            StepCatalog catalog = Catalog(ShippedContext.Load());

            foreach (string id in new[] { "design_cpu", "dev_wait", "release_price", "production", "first_sales", "oem_offer" })
                Assert.Equal(new[] { "idm", "fabless" }, catalog.Find(id).Tracks, id);
            foreach (string id in new[] { "foundry_services", "first_client", "factory_expansion" })
                Assert.Equal(new[] { "foundry" }, catalog.Find(id).Tracks, id);
            foreach (string id in new[] { "welcome", "research", "finance", "graduation" })
                Assert.Equal(new[] { "idm", "fabless", "foundry" }, catalog.Find(id).Tracks, id);
        }

        [Test]
        public void Config_IsTheDesignedRhythm()
        {
            OnboardingConfig config = ShippedContext.Load().MapConfig();

            Assert.Equal(730, config.TutorialWindowDays);
            Assert.Equal(1, config.MinDaysBetweenMails);
            Assert.Equal(120, config.DefaultDaysFallback);
        }

        [Test]
        public void Files_CarryTheSchemaOfTheirFormat()
        {
            ShippedContext context = ShippedContext.Load();

            foreach (string text in new[] { context.StepsText, context.ConfigText })
            {
                JsonNode node;
                JsonError error;
                Assert.True(ModJson.TryParse(text, out node, out error), "" + error);
                Assert.Equal(1, ((JsonObject)node).GetInt32("schema", 0));
            }
        }

        // ---- the strings ----

        private static IEnumerable<string> LanguageKeys(StepCatalog catalog)
        {
            var keys = new List<string>();
            foreach (StepDefinition step in catalog.All)
            {
                keys.Add(step.Mail.TitleKey);
                keys.Add(step.Mail.BodyKey);
                foreach (string track in step.Tracks) keys.Add(step.Mail.BodyKey + "." + track);   // optional, filtered by the caller
                foreach (BodyVariant variant in step.Mail.Variants) keys.Add(variant.BodyKey);
                foreach (ReminderSpec reminder in step.Reminders) keys.Add(reminder.HintKey);
                if (step.Kind == StepKind.Sequential)
                {
                    keys.Add(step.NameKey);
                    if (step.CompleteWhen != null) keys.Add(step.DoneKey);
                }
            }
            return keys.Distinct();
        }

        [Test]
        public void Strings_EveryKeyTheStepsUseExistsInBothLanguages()
        {
            ShippedContext context = ShippedContext.Load();
            StepCatalog catalog = Catalog(context);
            int checkedKeys = 0;
            var missing = new List<string>();

            foreach (string key in LanguageKeys(catalog))
            {
                bool optionalTrackText = key.EndsWith(".idm", StringComparison.Ordinal) || key.EndsWith(".fabless", StringComparison.Ordinal) || key.EndsWith(".foundry", StringComparison.Ordinal);
                bool inRu = context.Tables.Contains("ru", key);
                bool inEn = context.Tables.Contains("en", key);
                if (optionalTrackText && !inRu && !inEn) continue;

                checkedKeys++;
                if (!inRu) missing.Add("ru: " + key);
                if (!inEn) missing.Add("en: " + key);
            }

            Assert.True(missing.Count == 0, "missing strings:\n  " + string.Join("\n  ", missing));
            Assert.True(checkedKeys > 60, "too few keys checked: " + checkedKeys);
        }

        [Test]
        public void Strings_RuAndEnHaveTheSameKeysAndPlaceholders()
        {
            ShippedContext context = ShippedContext.Load();

            L10nMismatchReport report = context.Tables.Compare("ru", "en");
            Assert.True(report.ToLines().Count == 0, string.Join("\n", report.ToLines()));
            Assert.True(context.Tables.Count("ru") >= 100, "the feature has about 130 strings: " + context.Tables.Count("ru"));
            Assert.Equal(context.Tables.Count("ru"), context.Tables.Count("en"));

            foreach (string key in context.Tables.Keys("ru"))
            {
                string ru, en;
                context.Tables.TryGet("ru", key, out ru);
                context.Tables.TryGet("en", key, out en);
                Assert.Equal(StepValidator.MaxPlaceholderIndex(ru), StepValidator.MaxPlaceholderIndex(en), "the highest placeholder differs in " + key);
                Assert.True(key.StartsWith("onb.", StringComparison.Ordinal), "every key carries the prefix of the feature: " + key);
                Assert.True(ru.Trim().Length > 0 && en.Trim().Length > 0, "empty text: " + key);
                Assert.False(ru.Contains("|") || en.Contains("|"), "'|' is the plural separator: " + key);
            }
        }

        [Test]
        public void Strings_TitlesCarryTheMentorPrefix_InBothLanguages()
        {
            ShippedContext context = ShippedContext.Load();
            StepCatalog catalog = Catalog(context);

            foreach (StepDefinition step in catalog.All)
            {
                string ru, en;
                context.Tables.TryGet("ru", step.Mail.TitleKey, out ru);
                context.Tables.TryGet("en", step.Mail.TitleKey, out en);
                Assert.True(ru.StartsWith(StepValidator.MentorPrefixRu + " ", StringComparison.Ordinal), step.Id + " (ru): " + ru);
                Assert.True(en.StartsWith("[Mentor] ", StringComparison.Ordinal), step.Id + " (en): " + en);
            }
        }

        [Test]
        public void Strings_EveryBodyIsThreeToSixParagraphs_UpTo1200Characters_EndingWithTheAction()
        {
            ShippedContext context = ShippedContext.Load();
            StepCatalog catalog = Catalog(context);
            var problems = new List<string>();
            int bodies = 0;

            foreach (string key in BodyKeys(catalog, context))
            {
                foreach (string language in new[] { "ru", "en" })
                {
                    string text;
                    Assert.True(context.Tables.TryGet(language, key, out text), language + ": " + key);
                    bodies++;

                    string[] paragraphs = text.Split(new[] { "\n\n" }, StringSplitOptions.None);
                    if (paragraphs.Length < 3 || paragraphs.Length > 6) problems.Add(language + " " + key + ": " + paragraphs.Length + " paragraphs");
                    if (text.Length > 1200) problems.Add(language + " " + key + ": " + text.Length + " characters");
                    string action = language == "ru" ? "<b>Что сделать сейчас:</b> " : "<b>What to do now:</b> ";
                    if (!paragraphs[paragraphs.Length - 1].StartsWith(action, StringComparison.Ordinal))
                        problems.Add(language + " " + key + ": the last paragraph is not the action");
                    if (paragraphs.Take(paragraphs.Length - 1).Any(p => p.Contains("<b>")))
                        problems.Add(language + " " + key + ": only the last paragraph is the action");
                    if (paragraphs.Any(p => p.Trim().Length == 0)) problems.Add(language + " " + key + ": an empty paragraph");
                }
            }

            Assert.True(problems.Count == 0, "texts that break NFR-4/NFR-5:\n  " + string.Join("\n  ", problems));
            Assert.Equal(2 * 22, bodies, "13 steps and 6 tips, the fabless texts of design_cpu and production, the variant of tip_default: 22 bodies in each language");
        }

        private static IEnumerable<string> BodyKeys(StepCatalog catalog, ShippedContext context)
        {
            return LanguageKeys(catalog).Where(k => k.Contains(".body")).Where(k => context.Tables.Contains("ru", k) || context.Tables.Contains("en", k));
        }

        [Test]
        public void Strings_PlaceholdersAreIndexedAndFitTheArgsOfTheirMail_InBothLanguages()
        {
            ShippedContext context = ShippedContext.Load();
            StepCatalog catalog = Catalog(context);

            foreach (StepDefinition step in catalog.All)
            {
                var keys = new List<string> { step.Mail.TitleKey, step.Mail.BodyKey };
                keys.AddRange(step.Tracks.Select(t => step.Mail.BodyKey + "." + t).Where(k => context.Tables.Contains("ru", k)));
                keys.AddRange(step.Mail.Variants.Select(v => v.BodyKey));
                foreach (string key in keys)
                {
                    foreach (string language in new[] { "ru", "en" })
                    {
                        string text;
                        context.Tables.TryGet(language, key, out text);
                        Assert.True(StepValidator.MaxPlaceholderIndex(text) < step.Mail.Args.Length, language + " " + key + " uses more placeholders than the " + step.Mail.Args.Length + " args of " + step.Id);
                    }
                }
                foreach (ReminderSpec reminder in step.Reminders)
                {
                    foreach (string language in new[] { "ru", "en" })
                    {
                        string text;
                        context.Tables.TryGet(language, reminder.HintKey, out text);
                        Assert.True(StepValidator.MaxPlaceholderIndex(text) < reminder.Args.Length, language + " " + reminder.HintKey);
                    }
                }
            }
        }

        [Test]
        public void Strings_EveryListedArgIsUsedByTheText()
        {
            // An argument that no text uses is a wasted number in the mail: every listed arg appears in the title or the main body of its step in both languages.
            ShippedContext context = ShippedContext.Load();
            StepCatalog catalog = Catalog(context);

            foreach (StepDefinition step in catalog.All.Where(s => s.Mail.Args.Length > 0))
            {
                foreach (string language in new[] { "ru", "en" })
                {
                    string text;
                    context.Tables.TryGet(language, step.Mail.BodyKey, out text);
                    string title;
                    context.Tables.TryGet(language, step.Mail.TitleKey, out title);
                    for (int i = 0; i < step.Mail.Args.Length; i++)
                    {
                        string marker = "{" + i + "}";
                        Assert.True(text.Contains(marker) || title.Contains(marker), language + ": " + step.Id + " lists '" + step.Mail.Args[i] + "' as " + marker + " but never uses it");
                    }
                }
            }
        }

        [Test]
        public void Strings_QuoteTheNumbersOfTheDesignTable()
        {
            ShippedContext context = ShippedContext.Load();
            Func<string, string, string> text = (language, key) =>
            {
                string value;
                Assert.True(context.Tables.TryGet(language, key, out value), language + " " + key);
                return value;
            };

            // Interest 25% a year and profit tax 25%.
            foreach (string key in new[] { "onb.finance.body", "onb.tip_debt.body", "onb.tip_default.body" })
            {
                Assert.True(text("ru", key).Contains("25%"), "ru " + key);
                Assert.True(text("en", key).Contains("25%"), "en " + key);
            }
            // Storage $0.5 a unit a month.
            Assert.True(text("ru", "onb.production.body").Contains("$0,5"));
            Assert.True(text("en", "onb.production.body").Contains("$0.5"));
            Assert.True(text("ru", "onb.production.body.fabless").Contains("$0,5"));
            Assert.True(text("en", "onb.production.body.fabless").Contains("$0.5"));
            // Price: three budgets and nothing sells; the start popularity 0.05 of 1.0.
            Assert.True(text("ru", "onb.release_price.body").Contains("трёх бюджетов"));
            Assert.True(text("en", "onb.release_price.body").Contains("three budgets"));
            Assert.True(text("ru", "onb.release_price.body").Contains("0,05 из 1,0"));
            Assert.True(text("en", "onb.release_price.body").Contains("0.05 popularity out of 1.0"));
            Assert.True(text("ru", "onb.first_sales.body").Contains("0,05 из 1,0"));
            Assert.True(text("en", "onb.first_sales.hint1").Contains("three budgets"));
            // OEM: a year, twice the payment, 31 December, the margin 40%.
            Assert.True(text("ru", "onb.oem_offer.body").Contains("через год"));
            Assert.True(text("en", "onb.oem_offer.body").Contains("a year after"));
            Assert.True(text("ru", "onb.oem_offer.body").Contains("удвоенная оплата"));
            Assert.True(text("en", "onb.oem_offer.body").Contains("twice the payment"));
            Assert.True(text("ru", "onb.tip_contract_signed.body").Contains("удвоенная оплата"));
            Assert.True(text("en", "onb.tip_contract_signed.body").Contains("twice the payment"));
            Assert.True(text("ru", "onb.oem_offer.body").Contains("31 декабря") && text("ru", "onb.oem_offer.body").Contains("40%"));
            Assert.True(text("en", "onb.oem_offer.body").Contains("December 31") && text("en", "onb.oem_offer.body").Contains("40%"));
            // Research speed grows with the square root of the funding: twice the money, 1.4 times the progress.
            Assert.True(text("ru", "onb.research.body").Contains("1,4"));
            Assert.True(text("en", "onb.research.body").Contains("1.4"));
            // A CPU is old after 7 years (5 from 1990).
            Assert.True(text("ru", "onb.tip_old_cpu.body").Contains("7 лет") && text("ru", "onb.tip_old_cpu.body").Contains("старше 5"));
            Assert.True(text("en", "onb.tip_old_cpu.body").Contains("7 years") && text("en", "onb.tip_old_cpu.body").Contains("5 years"));
            // Foundry royalties 5% (the contract text of the game).
            Assert.True(text("ru", "onb.first_client.body").Contains("5%"));
            Assert.True(text("en", "onb.first_client.body").Contains("5%"));
            // The days of the default are not typed in: the live value is {0}.
            Assert.True(text("ru", "onb.tip_default.body").Contains("{0} дней"));
            Assert.True(text("en", "onb.tip_default.body").Contains("{0} days"));
            Assert.True(text("ru", "onb.tip_debt.body").Contains("{0}") && text("ru", "onb.tip_debt.body").Contains("{1} дней"));
        }

        [Test]
        public void Strings_TheHubTheNewGameWindowAndTheButtonsAreThere()
        {
            ShippedContext context = ShippedContext.Load();
            string[] keys =
            {
                "onb.feature.name", "onb.feature.desc", "onb.param.pause", "onb.param.pause.tip", "onb.param.reminders", "onb.param.reminders.tip",
                "onb.toggle.label", "onb.toggle.tooltip", "onb.toggle.forced", "onb.toggle.forced.on", "onb.toggle.forced.off", "onb.start.notify", "onb.notify.title",
                "onb.button.skipStep", "onb.button.skipAll", "onb.button.hint", "onb.button.openCpu", "onb.button.openFactory",
                "onb.button.skipStep.tip", "onb.button.skipAll.tip", "onb.button.hint.tip", "onb.button.openCpu.tip", "onb.button.openFactory.tip",
                "onb.hint.none", "onb.done.prefix", "onb.repeat.suffix",
                "onb.hub.title", "onb.hub.mode", "onb.hub.mode.off", "onb.hub.mode.active", "onb.hub.mode.skipped", "onb.hub.mode.completed", "onb.hub.mode.aborted",
                "onb.hub.track", "onb.track.idm", "onb.track.fabless", "onb.track.foundry", "onb.hub.step", "onb.hub.step.none", "onb.hub.status.oldSave",
                "onb.hub.button.repeat", "onb.hub.button.skipStep", "onb.hub.button.skipAll", "onb.hub.button.restart", "onb.hub.restart.title", "onb.hub.restart.body",
                "onb.error.content", "onb.summary.header", "onb.summary.line", "onb.summary.done", "onb.summary.skipped", "onb.summary.na", "onb.summary.open"
            };

            foreach (string key in keys)
            {
                Assert.True(context.Tables.Contains("ru", key), "ru " + key);
                Assert.True(context.Tables.Contains("en", key), "en " + key);
            }

            string text;
            context.Tables.TryGet("ru", "onb.feature.name", out text);
            Assert.Equal("Обучение первого года", text);
            context.Tables.TryGet("en", "onb.feature.name", out text);
            Assert.Equal("First-year tutorial", text);
            context.Tables.TryGet("ru", "onb.start.notify", out text);
            Assert.True(text.Contains("первое письмо придёт"));
            context.Tables.TryGet("ru", "onb.hint.none", out text);
            Assert.Equal("Подсказки для этого шага закончились", text);
            context.Tables.TryGet("ru", "onb.toggle.forced", out text);
            Assert.Equal("Задано сценарием: обучение {0}", text);
            context.Tables.TryGet("ru", "onb.hub.step", out text);
            Assert.Equal("Шаг {0} из {1}: {2}", text);
            context.Tables.TryGet("ru", "onb.done.prefix", out text);
            Assert.Equal("Уже сделано:", text);
        }

        [Test]
        public void Strings_NoOtherKeysAreLeftUnused()
        {
            // Every key of the tables is either used by a step, a part of the fixed list of the hub / new game / buttons, or a name or a phrase of a step.
            ShippedContext context = ShippedContext.Load();
            StepCatalog catalog = Catalog(context);
            var used = new HashSet<string>(LanguageKeys(catalog), StringComparer.Ordinal);

            var stray = new List<string>();
            foreach (string key in context.Tables.Keys("ru"))
            {
                bool fixedGroup = key.StartsWith("onb.feature.", StringComparison.Ordinal) || key.StartsWith("onb.param.", StringComparison.Ordinal)
                                  || key.StartsWith("onb.toggle.", StringComparison.Ordinal) || key.StartsWith("onb.button.", StringComparison.Ordinal)
                                  || key.StartsWith("onb.hub.", StringComparison.Ordinal) || key.StartsWith("onb.track.", StringComparison.Ordinal)
                                  || key.StartsWith("onb.summary.", StringComparison.Ordinal) || key.StartsWith("onb.error.", StringComparison.Ordinal)
                                  || key == "onb.start.notify" || key == "onb.notify.title" || key == "onb.hint.none" || key == "onb.done.prefix" || key == "onb.repeat.suffix";
                if (!fixedGroup && !used.Contains(key)) stray.Add(key);
            }
            Assert.True(stray.Count == 0, "strings that nothing refers to (a typo in a key?):\n  " + string.Join("\n  ", stray));
        }

        // ---- overrides: the player's files change the steps (decision 0006) ----

        private sealed class Record : IContentRecord
        {
            public Record(StepDefinition definition)
            {
                Definition = definition;
            }

            public StepDefinition Definition { get; private set; }

            public string Id { get { return Definition.Id; } }
        }

        private static ContentLoad<Record> Merge(ShippedContext context, string overrideText)
        {
            // The composition of StepContentLoader: the mapper and the per-record check of the validator, here over text instead of ModContent's files.
            return ContentMerger.MergeRecords<Record>(
                "first-year-onboarding", "steps.json", context.StepsText, overrideText,
                (json, errors) =>
                {
                    StepDefinition step = StepMapper.Map(json, errors);
                    return step == null ? null : new Record(step);
                },
                (record, errors) => StepValidator.Validate(record.Definition, context, errors, null),
                1);
        }

        private static StepCatalog BuildFrom(ShippedContext context, ContentLoad<Record> load)
        {
            return StepCatalog.Build(load.Records.Select(r => r.Definition).ToList(), context.MapConfig(), context);
        }

        [Test]
        public void Override_ReplacesAStepById()
        {
            ShippedContext context = ShippedContext.Load();
            string text = @"{ 'schema': 1, 'records': [ {
                'id': 'research', 'order': 20, 'kind': 'sequential', 'tracks': ['idm', 'fabless', 'foundry'],
                'completeWhen': { 'type': 'researchActive' }, 'pauseOnShow': false,
                'mail': { 'titleKey': 'onb.research.title', 'bodyKey': 'onb.research.body', 'buttons': ['skipStep'] },
                'reminders': [ { 'afterDays': 5, 'hintKey': 'onb.research.hint1' } ] } ] }".Replace('\'', '"');

            ContentLoad<Record> load = Merge(context, text);
            StepCatalog catalog = BuildFrom(context, load);

            Assert.Equal(1, load.Report.Replaced);
            Assert.Equal(0, load.Report.Errors.Count, string.Join("; ", load.Report.Errors));
            Assert.Equal(new[] { 5 }, catalog.Find("research").Reminders.Select(r => r.AfterDays).ToArray());
            Assert.False(catalog.Find("research").PauseOnShow);
            Assert.Equal(19, catalog.All.Count);
            Assert.Equal(10, catalog.SequentialCount("idm"));
        }

        [Test]
        public void Override_ABrokenReplacement_IsRejectedAndTheBuiltInStepStays()
        {
            ShippedContext context = ShippedContext.Load();
            string text = @"{ 'schema': 1, 'records': [ {
                'id': 'research', 'order': 20, 'tracks': ['idm'],
                'completeWhen': { 'type': 'researchActve' },
                'mail': { 'titleKey': 'onb.research.title', 'bodyKey': 'onb.research.body' } } ] }".Replace('\'', '"');

            ContentLoad<Record> load = Merge(context, text);
            StepCatalog catalog = BuildFrom(context, load);

            Assert.Equal(1, load.Report.Skipped);
            Assert.True(load.Report.Errors.Any(e => e.Contains("research") && e.Contains("unknown predicate \"researchActve\"")), string.Join("; ", load.Report.Errors));
            Assert.Equal(0, load.Report.Replaced);
            Assert.Equal(3, catalog.Find("research").Tracks.Length, "the built-in step is untouched");
            Assert.Equal(19, catalog.All.Count);
        }

        [Test]
        public void Override_DisablesAStepAndAddsANewOne()
        {
            ShippedContext context = ShippedContext.Load();
            string text = @"{ 'schema': 1, 'records': [
                { 'id': 'oem_offer', 'disabled': true },
                { 'id': 'my_tip', 'kind': 'situational', 'tracks': ['idm'], 'showWhen': { 'type': 'onb.soldToday' }, 'completeOnShow': true,
                  'mail': { 'titleKey': 'onb.tip_old_cpu.title', 'bodyKey': 'onb.tip_old_cpu.body' } } ] }".Replace('\'', '"');

            ContentLoad<Record> load = Merge(context, text);
            StepCatalog catalog = BuildFrom(context, load);

            Assert.Equal(1, load.Report.Removed);
            Assert.Equal(1, load.Report.Added);
            Assert.Null(catalog.Find("oem_offer"));
            Assert.NotNull(catalog.Find("my_tip"));
            Assert.Equal(9, catalog.SequentialCount("idm"), "the track is one step shorter and still works");
            Assert.Equal(0, catalog.Errors.Count, string.Join("; ", catalog.Errors));
        }

        [Test]
        public void Override_AllStepsOfATrackDisabled_SwitchesTheTrackOff()
        {
            ShippedContext context = ShippedContext.Load();
            string text = "{ 'schema': 1, 'records': [ " + string.Join(", ", Foundry.Select(id => "{ 'id': '" + id + "', 'disabled': true }")) + " ] }";

            ContentLoad<Record> load = Merge(context, text.Replace('\'', '"'));
            StepCatalog catalog = BuildFrom(context, load);

            // The four steps shared with the other tracks go for every track; the three of the foundry go with the foundry's last step.
            Assert.Equal(7, load.Report.Removed);
            Assert.False(catalog.HasTrack("foundry"), "no sequential step is left for the foundry");
            Assert.True(catalog.Errors.Any(e => e.Contains("track 'foundry'")), string.Join("; ", catalog.Errors));
            Assert.True(catalog.HasTrack("idm") && catalog.HasTrack("fabless"), "the tracks with steps of their own still work");
            Assert.False(catalog.IsEmpty);
        }
    }
}
