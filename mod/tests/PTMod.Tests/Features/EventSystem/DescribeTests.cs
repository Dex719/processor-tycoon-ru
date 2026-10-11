using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Json;
using PTMod.Features.EventSystem;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// The texts of the engine (FR-7, NFR-4, NFR-5, TSK-017/020): the line of an effect is built from the saved key and arguments only, so a change of language never
    /// changes a number; the letter of a choice carries the exact effects and their chances, the tag <c>es:&lt;id&gt;</c> and the priority of the case; the lines under
    /// the letter, the countdown bar of the window and the tables of both languages.
    /// </summary>
    [Covers(typeof(EventMailFormatter), typeof(CountdownBar), typeof(ITextSource), typeof(EffectText))]
    public class DescribeTests
    {
        private static readonly int Start = Defs.Day(1985, 6, 1);

        private static ChoiceRig RigWithHire()
        {
            EventChoice hire = Fx.Choice("hire",
                Fx.Effect("Money", "{ 'direction': 'Spend', 'min': 1041000, 'max': 1041000 }"),
                Fx.Effect("ModifierTimed", "{ 'channel': 'research.speed', 'multiplier': 1.12 }", 365),
                Fx.Effect("Reputation", "{ 'delta': 2, 'chance': 0.5 }"));
            EventDefinition definition = ChoiceEvents.Make("es.hire", new[] { Fx.Choice("decline"), hire },
                false, Fx.Effect("Money", "{ 'direction': 'Earn', 'min': 200000, 'max': 200000 }"), Fx.Effect("Reputation", "{ 'delta': 1, 'chance': 0 }"));
            var rig = new ChoiceRig(definition);
            rig.Text.With("es.t.hire", "Нанять", "Hire").With("es.t.decline", "Отказаться", "Decline");
            rig.SetDay(Start);
            return rig;
        }

        // ---- the line of an effect ----

        [Test]
        public void TheLineOfAnEffect_IsBuiltFromTheSavedKeyAndArguments_AndNoNumberChangesWithTheLanguage()
        {
            ChoiceRig rig = RigWithHire();
            rig.Rig.Fire("es.hire", CompanyKey.Player);
            ChoiceOptionDto hire = rig.Rig.Instance("es.hire").options[1];

            AppliedEffect money = hire.effects[0];
            AppliedEffect research = hire.effects[1];
            Assert.Equal("es.fx.money.spend", money.textKey);
            Assert.Equal("Расход $1.04M", EventMailFormatter.EffectLine(money, rig.Text));
            Assert.Equal("Скорость исследований: +12% на 365 дн.", EventMailFormatter.EffectLine(research, rig.Text));

            rig.Text.Language = "en";
            Assert.Equal("Spending $1.04M", EventMailFormatter.EffectLine(money, rig.Text));
            Assert.Equal("Research speed: +12% for 365 days", EventMailFormatter.EffectLine(research, rig.Text));
            Assert.Equal("$1.04M", money.textArgs[0], "the saved arguments are the same in both languages");
            Assert.Equal("research.speed", research.textArgs[0], "the channel id is saved; its name is looked up when the line is drawn");
        }

        [Test]
        public void TheLineOfAnEffect_SaysItsChance_AndThatItsNumberWasCut_AndFollowsTheSavedNumberOnly()
        {
            ChoiceRig rig = RigWithHire();
            rig.Rig.Fire("es.hire", CompanyKey.Player);
            AppliedEffect chance = rig.Rig.Instance("es.hire").options[1].effects[2];
            Assert.Equal("Репутация: +2 (50% шанс)", EventMailFormatter.EffectLine(chance, rig.Text));
            rig.Text.Language = "en";
            Assert.Equal("Reputation: +2 (50% chance)", EventMailFormatter.EffectLine(chance, rig.Text));

            var cut = new AppliedEffect
            {
                type = "ModifierTimed", channel = "research.speed", value = 4f, durationDays = 30, clamped = true, chance = 1f,
                textKey = "es.fx.modifier", textArgs = new[] { "research.speed", "+300%", "30" },
            };
            Assert.Equal("Research speed: +300% for 30 days (capped)", EventMailFormatter.EffectLine(cut, rig.Text));
            rig.Text.Language = "ru";
            Assert.Equal("Скорость исследований: +300% на 30 дн. (ограничено)", EventMailFormatter.EffectLine(cut, rig.Text));

            cut.textArgs = new[] { "research.speed", "+25%", "30" };   // the saved arguments are the only source
            Assert.Equal("Скорость исследований: +25% на 30 дн. (ограничено)", EventMailFormatter.EffectLine(cut, rig.Text));
            Assert.Equal(string.Empty, EventMailFormatter.EffectLine(null, rig.Text));

            var unknown = new AppliedEffect { type = "Gone", chance = 1f };
            Assert.True(EventMailFormatter.EffectLine(unknown, rig.Text).StartsWith("Эффект не применён"), "an effect without a text key is described as a failed one");
        }

        [Test]
        public void TheNumbersOfTheLetter_AreTheOnesTheJournalKeepsAfterTheChoice()
        {
            ChoiceRig rig = RigWithHire();
            EventInstanceDto instance = rig.FireAndDeliver("es.hire");
            FakeMail.Letter letter = rig.Mail.OfInstance(instance.id);
            string body = letter.Plan.Args[1];
            Assert.True(body.Contains("Расход $1.04M"), body);
            Assert.True(body.Contains("Скорость исследований: +12% на 365 дн."), body);

            rig.ClickLetter(instance.id, "hire");
            var view = new JournalView(rig.Text, id => rig.Catalog.Get(id), null, null);
            JournalRow row = view.History(rig.Rig.Log.Entries, null)[0];
            Assert.Equal(EventLogKinds.Resolved, row.Kind);
            Assert.True(Array.IndexOf(row.Lines, "Расход $1.04M") >= 0, string.Join(" | ", row.Lines));
            Assert.True(Array.IndexOf(row.Lines, "Скорость исследований: +12% на 365 дн.") >= 0, string.Join(" | ", row.Lines));
            Assert.Equal(-1041000f, rig.Rig.Game.Money[1].Value, "the amount of the letter is the amount spent (the first entry is the event's own income)");
        }

        // ---- the letter ----

        [Test]
        public void TheLetterOfAChoice_CarriesTheTag_ThePriority_TheTextTheCauseTheEffectsAndTheButtons()
        {
            ChoiceRig rig = RigWithHire();
            EventInstanceDto instance = rig.FireAndDeliver("es.hire");
            FakeMail.Letter letter = rig.Mail.OfInstance(instance.id);
            EventMailPlan plan = letter.Plan;

            Assert.Equal("es:" + instance.id, plan.Tag);
            Assert.Equal(MailPriority.Critical, plan.Priority);
            Assert.Equal("es.ui.mail.title", plan.TitleKey);
            Assert.Equal("es.ui.mail.body", plan.BodyKey);
            Assert.Equal("[Событие] Событие es.hire", rig.Text.L(plan.TitleKey, plan.Args));
            Assert.Equal(plan.Args[1], rig.Text.L(plan.BodyKey, plan.Args));

            string body = plan.Args[1];
            Assert.True(body.StartsWith("Текст es.hire"), body);
            Assert.True(body.Contains("Почему: Причина es.hire"), body);
            Assert.True(body.Contains("Сразу:\n    – Доход $200K"), "what the event does at once: " + body);
            Assert.False(body.Contains("Репутация: +1"), "an effect whose chance did not come up did not happen, the letter does not mention it: " + body);
            Assert.True(body.Contains("Варианты:\n• Отказаться\n• Нанять\n    – Расход $1.04M"), body);
            Assert.True(body.Contains("    – Репутация: +2 (50% шанс)"), "the chance of an effect is shown, its roll is not: " + body);

            Assert.Equal(2, plan.Choices.Count);
            Assert.Equal("decline", plan.Choices[0].Id);
            Assert.Equal("es.t.decline", plan.Choices[0].LabelKey);
            Assert.True(plan.Choices[0].Enabled);
            Assert.Equal("hire", plan.Choices[1].Id);
        }

        [Test]
        public void TheLetterOfAnEventWithoutAChoice_IsNormal_AndTheNewsAboutARivalIsLow()
        {
            EventDefinition plain = ChoiceEvents.Make("es.plain", null, false, Fx.Effect("Reputation", "{ 'delta': 3 }"));
            plain.notifyPlayer = true;
            var rig = new ChoiceRig(plain);
            rig.Text.Names["ai:1"] = "Contoso";
            rig.SetDay(Start);

            rig.Rig.Fire("es.plain", CompanyKey.Player);
            rig.Rig.Fire("es.plain", "ai:1");
            Assert.Equal(2, rig.Mail.Letters.Count);

            EventMailPlan info = rig.Mail.Letters[0].Plan;
            Assert.Equal(MailPriority.Normal, info.Priority);
            Assert.Equal(0, info.Choices.Count);
            Assert.Equal("es.ui.mail.title", info.TitleKey);
            Assert.True(info.Args[1].Contains("Сразу:\n    – Репутация: +3"), info.Args[1]);

            EventMailPlan news = rig.Mail.Letters[1].Plan;
            Assert.Equal(MailPriority.Low, news.Priority);
            Assert.Equal("es.ui.mail.news", news.TitleKey);
            Assert.Equal("[Новости] Событие es.plain", rig.Text.L(news.TitleKey, news.Args));
            Assert.True(news.Args[1].StartsWith("Компания: Contoso"), news.Args[1]);
        }

        [Test]
        public void TheNewsAboutARivalWithAChoice_NamesTheDecision()
        {
            EventDefinition definition = ChoiceEvents.Make("es.strike", new[] { Fx.Choice("settle", Fx.Effect("Reputation", "{ 'delta': 1 }")), Fx.Choice("refuse") });
            definition.notifyPlayer = true;
            var rig = new ChoiceRig(definition);
            rig.Text.With("es.t.settle", "Договориться", "Settle");
            rig.SetDay(Start);

            rig.Rig.Fire("es.strike", "ai:1");
            Assert.Equal(1, rig.Mail.Letters.Count, "one news letter, after the decision");
            string body = rig.Mail.Letters[0].Plan.Args[1];
            Assert.True(body.Contains("Решение: Договориться"), body);
            Assert.True(body.Contains("    – Репутация: +1"), body);
        }

        // ---- the lines under the letter ----

        [Test]
        public void TheStatusLines_SayTheDeadlineThenTheDaysLeft_InBothLanguages()
        {
            var text = new TableText();
            int deadline = Defs.Day(1985, 6, 15);
            Assert.Equal("Ответить до 15.06.1985 (14 дн.)", EventMailFormatter.StatusDeadline(deadline, Start, text));
            Assert.Equal("Осталось 9 из 14 дн.", EventMailFormatter.StatusDaysLeft(9, 14, text));
            Assert.Equal("Осталось 0 из 14 дн.", EventMailFormatter.StatusDaysLeft(-3, 14, text), "never negative");

            text.Language = "en";
            Assert.Equal("Respond by 15.06.1985 (14 days)", EventMailFormatter.StatusDeadline(deadline, Start, text));
            Assert.Equal("9 of 14 days left", EventMailFormatter.StatusDaysLeft(9, 14, text));

            Assert.Equal("timer", EventMailFormatter.Status("timer", string.Empty));
            Assert.Equal("timer\nwarning", EventMailFormatter.Status("timer", "warning"));
        }

        [Test]
        public void TheTagOfALetter_NamesTheInstance_AndOnlyTheInstance()
        {
            Assert.Equal("es:57", EventMailFormatter.TagOf(57));
            int id;
            Assert.True(EventMailFormatter.TryParseTag("es:57", out id));
            Assert.Equal(57, id);
            foreach (string bad in new[] { null, string.Empty, "es:", "es:0", "es:fx:3/common/0", "es:12a", "ms:5", "57", "es:-4" })
            {
                Assert.False(EventMailFormatter.TryParseTag(bad, out id), "not an instance: " + bad);
            }
        }

        // ---- the countdown of the window ----

        [Test]
        public void TheCountdownBar_HasOneDivisionPerDay_AndNeverLosesADayLeftToRounding()
        {
            var text = new TableText();
            Assert.Equal("■■■■■■■■■□□□□□", CountdownBar.Cells(9, 14));
            Assert.Equal("Осталось 9 из 14 дн. ■■■■■■■■■□□□□□", CountdownBar.Format(9, 14, text));
            Assert.Equal("□□□□□□□□□□□□□□", CountdownBar.Cells(0, 14));
            Assert.Equal("■■■■■■■■■■■■■■", CountdownBar.Cells(20, 14), "more than the timer: the whole bar");
            Assert.Equal(CountdownBar.MaxCells, CountdownBar.Cells(100, 365).Length, "a long timer is scaled to the longest bar");
            Assert.True(CountdownBar.Cells(1, 365).StartsWith("■"), "one day left is one division, not none");
            Assert.Equal("■", CountdownBar.Cells(1, 0), "a timer of 0 counts as 1");

            text.Language = "en";
            Assert.Equal("14 of 14 days left ■■■■■■■■■■■■■■", CountdownBar.Format(14, 14, text));
        }

        // ---- the tables ----

        [Test]
        public void TheTablesOfBothLanguages_HaveTheSameKeys_AllOfTheFeature_AndEveryKeyTheCodeUsesIsThere()
        {
            string src = SourceScan.SrcDirOrFail();
            var ru = ReadKeys(Path.Combine(src, "L10n", "ru", "event-system.json"));
            var en = ReadKeys(Path.Combine(src, "L10n", "en", "event-system.json"));

            var onlyRu = new List<string>(ru);
            onlyRu.RemoveAll(en.Contains);
            var onlyEn = new List<string>(en);
            onlyEn.RemoveAll(ru.Contains);
            Assert.True(onlyRu.Count == 0 && onlyEn.Count == 0, "keys of one language only: ru " + string.Join(",", onlyRu.ToArray()) + "; en " + string.Join(",", onlyEn.ToArray()));
            foreach (string key in ru) Assert.True(key.StartsWith("es.", StringComparison.Ordinal), "a key of another feature: " + key);

            var used = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var literal = new Regex("\"(es\\.[A-Za-z][A-Za-z0-9_]*(?:\\.[A-Za-z][A-Za-z0-9_]*)*)\"", RegexOptions.Compiled);
            string root = Path.Combine(src, "Features", "EventSystem");
            foreach (string folder in new[] { "Logic/UI", "Logic/Choices", "Logic/Log", "Logic/Effects/Handlers", "Logic/Settings", "UI" })
            {
                foreach (string path in SourceScan.Files(Path.Combine(root, folder), ".cs"))
                {
                    string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                    foreach (Match match in literal.Matches(code)) used[match.Groups[1].Value] = SourceScan.Relative(src, path);
                }
            }
            Assert.True(used.Count > 40, "too few keys found in the sources (" + used.Count + "): wrong folders?");

            var missing = new List<string>();
            foreach (KeyValuePair<string, string> key in used)
            {
                if (!ru.Contains(key.Key)) missing.Add(key.Key + " (" + key.Value + ")");
            }
            Assert.True(missing.Count == 0, "keys the code uses that the tables lack:\n  " + string.Join("\n  ", missing.ToArray()));
        }

        [Test]
        public void EveryTextOfTheLetters_FormatsWithItsArguments_InBothLanguages()
        {
            var text = new TableText();
            string[] twoArgs = { "es.ui.deadline", "es.ui.days_left", "es.ui.status.debt", "es.ui.choice.warned", "es.ui.journal.until", "es.ui.journal.active.row" };
            string[] oneArg = { "es.ui.notify.new", "es.ui.notify.deadline", "es.ui.auto_chosen", "es.ui.debt_warning", "es.ui.choice_unavailable", "es.reason.requires",
                                "es.reason.credit_limit", "es.fx.chance", "es.ui.cause", "es.ui.mail.company", "es.ui.mail.decision", "es.ui.mail.option", "es.ui.mail.effect" };
            foreach (string language in new[] { "ru", "en" })
            {
                text.Language = language;
                foreach (string key in twoArgs)
                {
                    string line = text.L(key, "AAA", "BBB");
                    Assert.True(line.Contains("AAA") && line.Contains("BBB"), language + " " + key + " lost an argument: " + line);
                }
                foreach (string key in oneArg)
                {
                    Assert.True(text.L(key, "AAA").Contains("AAA"), language + " " + key + " lost its argument");
                }
            }
            Assert.Equal(0, text.Warnings.Count, string.Join("; ", text.Warnings));
        }

        private static HashSet<string> ReadKeys(string path)
        {
            Assert.True(File.Exists(path), "no table " + path);
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(SourceScan.ReadText(path), out node, out error), path + ": " + error);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, JsonNode> member in (JsonObject)node) keys.Add(member.Key);
            return keys;
        }
    }
}
