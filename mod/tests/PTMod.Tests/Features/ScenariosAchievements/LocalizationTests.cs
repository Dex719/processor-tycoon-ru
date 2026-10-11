using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.L10n;
using PTMod.Features.ScenariosAchievements.Logic;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-005: the strings of the feature, <c>L10n/ru/scenarios-achievements.json</c> and <c>L10n/en/…</c> (NFR-5, NFR-6): every key the content names
    /// has a text in both languages, the two tables agree (keys, plural forms, placeholders), the description of a scenario fits six lines, the
    /// placeholders are indexed ({0}…) and every text survives <c>string.Format</c>, and the keys that the UI strings of design.md name are there.
    /// </summary>
    public class LocalizationTests
    {
        private static readonly Regex Placeholder = new Regex(@"\{(\d+)(?:[,:][^}]*)?\}", RegexOptions.Compiled);

        private static L10nTables LoadTables(out List<string> problems)
        {
            problems = new List<string>();
            var tables = new L10nTables();
            foreach (string language in new[] { "ru", "en" })
            {
                tables.AddJson(language, "l10n/" + language + "/scenarios-achievements.json", ScenarioTestData.ReadStrings(language), problems);
            }
            return tables;
        }

        private static L10nTables Tables()
        {
            List<string> problems;
            L10nTables tables = LoadTables(out problems);
            Assert.True(problems.Count == 0, "the string tables have problems:\n  " + string.Join("\n  ", problems));
            return tables;
        }

        private static string Text(L10nTables tables, string language, string key)
        {
            string text;
            Assert.True(tables.TryGet(language, key, out text), language + " lacks the key " + key);
            return text;
        }

        /// <summary>Every key the shipped content names: titles, descriptions, briefings, goals, fail texts.</summary>
        private static List<string> KeysOfTheContent()
        {
            var keys = new List<string>();
            foreach (ScenarioDefinition s in ScenarioTestData.StandardScenarios())
            {
                keys.Add(s.TitleKey);
                keys.Add(s.DescKey);
                keys.Add(s.BriefingKey);
                foreach (GoalSpec g in s.Goals) keys.Add(g.TitleKey);
                foreach (FailSpec f in s.Fail) keys.Add(f.TextKey);
            }
            foreach (ChallengeDefinition c in ScenarioTestData.StandardChallenges())
            {
                keys.Add(c.TitleKey);
                keys.Add(c.DescKey);
                foreach (FailSpec f in c.Fail) keys.Add(f.TextKey);
                if (c.RequiresPerCompanyCredit) keys.Add(c.UnavailableKey);
            }
            foreach (AchievementDefinition a in ScenarioTestData.StandardAchievements())
            {
                keys.Add(a.TitleKey);
                keys.Add(a.DescKey);
            }
            return keys;
        }

        [Test]
        public void TheTables_LoadCleanly_AndAgreeBetweenTheLanguages()
        {
            L10nTables tables = Tables();
            Assert.True(tables.Count("ru") >= 200, "ru: " + tables.Count("ru"));
            Assert.True(tables.Count("en") >= 200, "en: " + tables.Count("en"));

            L10nMismatchReport report = tables.Compare("ru", "en");
            Assert.True(report.IsClean, "ru and en differ (NFR-5):\n  " + string.Join("\n  ", report.ToLines()));
        }

        [Test]
        public void EveryKeyOfTheContent_HasATextInBothLanguages()
        {
            L10nTables tables = Tables();
            var missing = new List<string>();
            foreach (string key in KeysOfTheContent())
            {
                foreach (string language in new[] { "ru", "en" })
                {
                    if (!tables.Contains(language, key)) missing.Add(language + ":" + key);
                }
            }
            Assert.True(missing.Count == 0, "keys without a text:\n  " + string.Join("\n  ", missing));
            Assert.True(KeysOfTheContent().Count > 150, "sanity: " + KeysOfTheContent().Count + " keys in the content");
        }

        [Test]
        public void EveryScenario_HasATitle_ADescription_ABriefing_AndTheTextsOfItsGoals()
        {
            L10nTables tables = Tables();
            foreach (ScenarioDefinition s in ScenarioTestData.StandardScenarios())
            {
                foreach (string suffix in new[] { "title", "desc", "briefing" })
                {
                    foreach (string language in new[] { "ru", "en" })
                    {
                        Assert.True(Text(tables, language, "scn." + s.Id + "." + suffix).Length > 5, language + " scn." + s.Id + "." + suffix);
                    }
                }

                foreach (GoalSpec goal in s.Goals)
                {
                    Assert.Equal("scn." + s.Id + "." + goal.Id, goal.TitleKey);
                    foreach (string language in new[] { "ru", "en" }) Assert.True(Text(tables, language, goal.TitleKey).Length > 5, language + " " + goal.TitleKey);
                }
            }
        }

        [Test]
        public void TheDescriptionOfAScenario_FitsSixLines()
        {
            // NFR-6: "6 lines" in a window about 70 characters wide; the texts stay under 6 x 70 and carry no more than 5 line breaks
            L10nTables tables = Tables();
            foreach (ScenarioDefinition s in ScenarioTestData.StandardScenarios())
            {
                foreach (string language in new[] { "ru", "en" })
                {
                    string text = Text(tables, language, s.DescKey);
                    int explicitLines = text.Split('\n').Length;
                    int lines = text.Split('\n').Sum(part => Math.Max(1, (part.Length + 69) / 70));
                    Assert.True(explicitLines <= 6, language + " " + s.DescKey + " has " + explicitLines + " lines");
                    Assert.True(lines <= 6, language + " " + s.DescKey + " needs about " + lines + " lines: " + text.Length + " characters");
                }
            }
        }

        [Test]
        public void EveryChallenge_HasATitle_ADescription_AndAFailText()
        {
            L10nTables tables = Tables();
            foreach (ChallengeDefinition c in ScenarioTestData.StandardChallenges())
            {
                foreach (string suffix in new[] { "title", "desc", "fail" })
                {
                    foreach (string language in new[] { "ru", "en" })
                    {
                        Assert.True(Text(tables, language, "chl." + c.Id + "." + suffix).Length > 3, language + " chl." + c.Id + "." + suffix);
                    }
                }

                foreach (FailSpec fail in c.Fail) Assert.Equal("chl." + c.Id + ".fail", fail.TextKey, c.Id + " fail text key");
            }
        }

        [Test]
        public void TheChallengeThatNeedsThePerCompanyThreshold_HasItsReason_InBothLanguages()
        {
            L10nTables tables = Tables();
            Assert.Equal("Порог дефолта игрока не меняется в этой сборке", Text(tables, "ru", "chl.tight-credit.unavailable"));
            Assert.Equal("The player's default threshold isn't adjustable in this build", Text(tables, "en", "chl.tight-credit.unavailable"));
            Assert.Equal("chl.tight-credit.unavailable", ScenarioTestData.Challenge("tight-credit").UnavailableKey);
        }

        [Test]
        public void EveryAchievement_HasATitle_AndADescription()
        {
            L10nTables tables = Tables();
            List<AchievementDefinition> list = ScenarioTestData.StandardAchievements();
            Assert.Equal(37, list.Count);
            foreach (AchievementDefinition a in list)
            {
                foreach (string language in new[] { "ru", "en" })
                {
                    Assert.True(Text(tables, language, "ach." + a.Id + ".title").Length > 2, language + " ach." + a.Id + ".title");
                    Assert.True(Text(tables, language, "ach." + a.Id + ".desc").Length > 5, language + " ach." + a.Id + ".desc");
                }
            }

            // the texts of the design's table of keys
            Assert.Equal("Первый кремний", Text(tables, "ru", "ach.first-cpu.title"));
            Assert.Equal("First silicon", Text(tables, "en", "ach.first-cpu.title"));
            Assert.Equal("Выпустите свой первый процессор.", Text(tables, "ru", "ach.first-cpu.desc"));
            Assert.Equal("Release your first CPU.", Text(tables, "en", "ach.first-cpu.desc"));
        }

        [Test]
        public void TheKeys_OfTheTableOfTheDesign_AreThere_WithTheirTexts()
        {
            L10nTables tables = Tables();
            var expected = new[]
            {
                // key, ru, en (design.md, "Ключи ModL10n (примеры)")
                new[] { "scn.feature.name", "Сценарии и достижения", "Scenarios and achievements" },
                new[] { "scn.feature.desc", "Сценарии с целями, испытания и достижения в глобальном профиле.", "Goal-driven scenarios, challenges and achievements in a global profile." },
                new[] { "scn.hub.title", "Сценарии и достижения", "Scenarios and achievements" },
                new[] { "scn.mode.label", "Режим", "Mode" },
                new[] { "scn.mode.free", "Свободная игра", "Free play" },
                new[] { "scn.garage-1975.title", "Гаражный стартап (1975)", "Garage startup (1975)" },
                new[] { "scn.garage-1975.desc", "Первый процессор, первая прибыль, первые $5 млн. Рекомендуется с обучением.", "First CPU, first profit, first $5M. Recommended with the tutorial." },
                new[] { "scn.fabless-1985.title", "Без фабрики (1985)", "Fabless (1985)" },
                new[] { "scn.fabless-1985.g2", "Доля рынка от 10%", "Market share of 10% or more" },
                new[] { "scn.mhz-race-1990.g1", "Первым выпустить CPU на 100 МГц", "Be first to ship a 100 MHz CPU" },
                new[] { "scn.fail.bankrupt", "Банкротство", "Bankruptcy" },
                new[] { "scn.fail.fablessRule", "Нарушено правило fabless: построена фабрика", "Fabless rule broken: a fab was built" },
                new[] { "scn.fail.deadline", "Время вышло", "Time is up" },
                new[] { "scn.fail.raceLost", "Гонку выиграла {0} ({1})", "{0} won the race ({1})" },
                new[] { "scn.difficulty.locked", "Во время сценария сложность понижать нельзя", "Difficulty can't be lowered during a scenario" },
                new[] { "scn.badge.assisted.feature", "С помощью: отключена фича «{0}»", "Assisted: feature \"{0}\" was turned off" },
                new[] { "chl.tight-credit.unavailable", "Порог дефолта игрока не меняется в этой сборке", "The player's default threshold isn't adjustable in this build" },
                new[] { "scn.result.won", "Сценарий выполнен", "Scenario complete" },
                new[] { "scn.result.lost", "Сценарий провален", "Scenario failed" },
                new[] { "scn.medal.gold", "Золото", "Gold" },
                new[] { "scn.medal.silver", "Серебро", "Silver" },
                new[] { "scn.medal.bronze", "Бронза", "Bronze" },
                new[] { "scn.medal.completed", "Пройдено", "Completed" },
                new[] { "scn.button.continue", "Продолжить игру", "Keep playing" },
                new[] { "scn.button.menu", "В главное меню", "Main menu" },
                new[] { "scn.badge.assisted", "С помощью", "Assisted" },
                new[] { "chl.button", "Испытания ({0})…", "Challenges ({0})…" },
                new[] { "chl.no-debt.title", "Без долгов", "No debt" },
                new[] { "chl.no-debt.desc", "Провал, если деньги ниже нуля больше 30 дней подряд. +25% к счёту.", "Fail if cash stays below zero for over 30 days. +25% score." },
                new[] { "ach.window.title", "Достижения: {0} / {1}", "Achievements: {0} / {1}" },
                new[] { "ach.first-cpu.title", "Первый кремний", "First silicon" },
                new[] { "ach.first-cpu.desc", "Выпустите свой первый процессор.", "Release your first CPU." },
                new[] { "ach.hidden", "???", "???" },
                new[] { "ach.toast", "Достижение: {0}", "Achievement: {0}" },
            };

            foreach (string[] row in expected)
            {
                Assert.Equal(row[1], Text(tables, "ru", row[0]), "ru " + row[0]);
                Assert.Equal(row[2], Text(tables, "en", row[0]), "en " + row[0]);
            }
        }

        [Test]
        public void TheUiStringsOfTheWindows_AreThere()
        {
            // the keys the windows of the feature are going to ask for: the setup section, the briefing, the tracker, the scorecard, the achievements
            L10nTables tables = Tables();
            var keys = new[]
            {
                "scn.hub.catalog", "scn.hub.achievements", "scn.hub.results", "scn.hub.profile",
                "scn.mode.custom", "scn.preview.title", "scn.preview.summary", "scn.preview.technology", "scn.goals.title",
                "scn.goal.required", "scn.goal.optional", "scn.deadline", "scn.best.none", "scn.best.value", "scn.badge.custom", "ach.button",
                "scn.briefing.title", "scn.briefing.challenges", "scn.briefing.button",
                "scn.tracker.title", "scn.tracker.progress", "scn.tracker.done", "scn.tracker.failed", "scn.tracker.unavailable", "scn.tracker.race",
                "scn.badge.assisted.cheats", "scn.badge.assisted.other",
                "scn.toast.goalDone", "scn.toast.goalFailed", "scn.toast.startFailed", "chl.toast.failed", "chl.toast.passed", "ach.toast.assisted", "ach.toast.more",
                "scn.fail.debtLimit", "scn.fail.goal",
                "scn.result.reason", "scn.result.score", "scn.result.mailSubject", "scn.score.goals", "scn.score.timeBonus", "scn.score.challenges", "scn.score.total",
                "scn.medal.none", "scn.table.company", "scn.table.money", "scn.table.revenue", "scn.table.share", "scn.table.cpus", "scn.table.frequency",
                "scn.table.bankrupt", "scn.menu.confirm", "scn.career.title",
                "chl.window.title", "chl.bonus", "chl.none", "chl.reason.company", "chl.reason.scenario",
                "ach.hidden.desc", "ach.category.career", "ach.category.money", "ach.category.tech", "ach.category.market", "ach.category.business",
                "ach.category.meta", "ach.tab.records", "ach.badge.assisted", "ach.unlocked", "ach.records.none", "ach.reset", "ach.reset.confirm1", "ach.reset.confirm2",
            };
            var missing = new List<string>();
            foreach (string key in keys)
            {
                foreach (string language in new[] { "ru", "en" })
                {
                    if (!tables.Contains(language, key)) missing.Add(language + ":" + key);
                }
            }
            Assert.True(missing.Count == 0, "keys missing:\n  " + string.Join("\n  ", missing));

            // the categories are the categories of the content
            foreach (string category in ContentVocabulary.Categories) Assert.True(tables.Contains("ru", "ach.category." + category), category);
            foreach (string medal in ContentVocabulary.Medals) Assert.True(tables.Contains("ru", "scn.medal." + medal), medal);
        }

        [Test]
        public void TheParameters_HaveTheirLabelsAndTooltips()
        {
            L10nTables tables = Tables();
            foreach (string key in new[] { "strictAssisted", "trackerAutoOpen", "toastsEnabled" })
            {
                foreach (string language in new[] { "ru", "en" })
                {
                    Assert.True(Text(tables, language, "scn.param." + key + ".label").Length > 5, language + " label of " + key);
                    Assert.True(Text(tables, language, "scn.param." + key + ".tooltip").Length > 20, language + " tooltip of " + key);
                }
            }
        }

        [Test]
        public void EveryKey_CarriesOnePrefixOfTheFeature()
        {
            L10nTables tables = Tables();
            foreach (string language in new[] { "ru", "en" })
            {
                foreach (string key in tables.Keys(language))
                {
                    Assert.True(ContentVocabulary.KeyPrefixes.Any(p => key.StartsWith(p, StringComparison.Ordinal)), language + ": " + key + " lacks scn. / chl. / ach.");
                }
            }
        }

        [Test]
        public void Placeholders_AreIndexed_MatchBetweenLanguages_AndEveryTextFormats()
        {
            L10nTables tables = Tables();
            var problems = new List<string>();
            object[] args = { "a", "b", "c", "d", "e", "f", "g", "h" };

            foreach (string key in tables.Keys("ru"))
            {
                string ru = Text(tables, "ru", key);
                string en;
                bool hasEn = tables.TryGet("en", key, out en);

                foreach (string language in new[] { "ru", "en" })
                {
                    string text = language == "ru" ? ru : en;
                    if (text == null) continue;
                    if (string.IsNullOrWhiteSpace(text)) problems.Add(language + ":" + key + ": empty");
                    if (text.IndexOf('|') >= 0) problems.Add(language + ":" + key + ": '|' is the inline plural separator");
                    if (Regex.IsMatch(text, @"\{[^0-9]") || Regex.IsMatch(text, @"[^0-9]\}") ) problems.Add(language + ":" + key + ": a placeholder must be {0}, {1}, ...");
                    try
                    {
                        string.Format(CultureInfo.InvariantCulture, text, args);
                    }
                    catch (FormatException ex)
                    {
                        problems.Add(language + ":" + key + ": bad format: " + ex.Message);
                    }
                }

                if (hasEn)
                {
                    string a = string.Join(",", Placeholder.Matches(ru).Cast<Match>().Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x));
                    string b = string.Join(",", Placeholder.Matches(en).Cast<Match>().Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x));
                    if (a != b) problems.Add(key + ": ru has {" + a + "}, en has {" + b + "}");
                }
            }
            Assert.True(problems.Count == 0, "placeholder problems:\n  " + string.Join("\n  ", problems));
        }

        [Test]
        public void ThePluralGroups_AreComplete_InBothLanguages()
        {
            L10nTables tables = Tables();
            Assert.Equal("1 линия", tables.TryGet("ru", "scn.preview.lines.one", out string one) ? one.Replace("{0}", "1") : null);
            Assert.Equal("3 линии", tables.TryGet("ru", "scn.preview.lines.few", out string few) ? few.Replace("{0}", "3") : null);
            Assert.Equal("125 линий", tables.TryGet("ru", "scn.preview.lines.many", out string many) ? many.Replace("{0}", "125") : null);
            Assert.Equal("125 lines", tables.TryGet("en", "scn.preview.lines.other", out string other) ? other.Replace("{0}", "125") : null);
            Assert.True(tables.Contains("ru", "scn.goal.points.few") && tables.Contains("en", "scn.goal.points.other"));
        }

        [Test]
        public void TheTexts_UseTheTypographyOfTheMod()
        {
            // Russian text: guillemets for names in the text, no straight double quotes; the English side keeps straight quotes
            L10nTables tables = Tables();
            foreach (string key in tables.Keys("ru"))
            {
                string text = Text(tables, "ru", key);
                Assert.False(text.Contains("\""), "ru:" + key + " uses straight quotes: " + text);
                Assert.False(text.Contains("  "), "ru:" + key + " has a double space");
                Assert.True(text == text.Trim(), "ru:" + key + " has outer white space");
            }
            foreach (string key in tables.Keys("en"))
            {
                string text = Text(tables, "en", key);
                Assert.False(text.Contains("«") || text.Contains("»"), "en:" + key + " uses guillemets: " + text);
                Assert.False(text.Contains("  "), "en:" + key + " has a double space");
                Assert.True(text == text.Trim(), "en:" + key + " has outer white space");
            }
        }
    }
}
