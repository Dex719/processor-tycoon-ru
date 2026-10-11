using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Json;
using PTMod.Features.EventSystem;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// The journal of events and its window (FR-8, TSK-019/020): the history newest first and the active effects with their end dates (AC-8.1), the filters, the limit of
    /// 400 entries (AC-8.2), the redrawing from keys and saved arguments when the language changes (FR-8 item 5), the texts of the records surviving a save.
    /// </summary>
    [Covers(typeof(JournalView), typeof(JournalFilter), typeof(JournalRow), typeof(ActiveEffectRow), typeof(EventLogStore), typeof(EventLogEntry), typeof(EffectRecord))]
    public class JournalTests
    {
        private static readonly int Today = Defs.Day(1985, 12, 1);

        private static AppliedEffect Timed(string channel, float value, int endDay, string company = CompanyKey.Player, string state = AppliedEffectStates.Active)
        {
            return new AppliedEffect
            {
                id = "1/hire/1", instanceId = 1, type = "ModifierTimed", channel = channel, value = value, companyKey = company, endDay = endDay, startDay = Today - 10,
                state = state, textKey = "es.fx.modifier", textArgs = new[] { channel, JournalView.Percent(value), "365" }, chance = 1f,
            };
        }

        private static JournalView ViewOver(TableText text, params EventDefinition[] definitions)
        {
            var catalog = new FakeCatalog(Fx.DesignLimits(), definitions);
            return new JournalView(text, id => catalog.Get(id), null, effect => effect.instanceId == 1 ? "Источник" : string.Empty);
        }

        // ---- the active effects ----

        [Test]
        public void AC_8_1_AnActiveEffect_ShowsItsChange_ItsEndDate_AndTheDaysLeft()
        {
            var text = new TableText();
            JournalView view = ViewOver(text);
            IList<ActiveEffectRow> rows = view.Active(new[] { Timed("research.speed", 1.12f, Defs.Day(1986, 6, 1)) }, Today, null);

            Assert.Equal(1, rows.Count);
            Assert.Equal("Скорость исследований +12 % — до 01.06.1986 (182 дн.)", rows[0].Text);
            Assert.Equal(182, rows[0].DaysLeft);
            Assert.Equal(Defs.Day(1986, 6, 1), rows[0].EndDay);
            Assert.Equal("Источник", rows[0].Source);

            text.Language = "en";
            Assert.Equal("Research speed +12 % — until 01.06.1986 (182 days)", view.Active(new[] { Timed("research.speed", 1.12f, Defs.Day(1986, 6, 1)) }, Today, null)[0].Text);
        }

        [Test]
        public void AnActiveEffect_NamesItsCompanyOrTheWholeMarket_AndTheRowsGoByTheEndDate()
        {
            var text = new TableText();
            text.Names[CompanyKey.Player] = "Мой завод";
            text.Names["ai:3"] = "Contoso";
            JournalView view = ViewOver(text);

            var effects = new List<AppliedEffect>
            {
                Timed("market.demand.lowend", 0.8f, Today + 90, EventCompanyKeys.Global),
                Timed("production.output", 0.6f, Today + 30, "ai:3"),
                Timed("research.speed", 1.1f, Today + 60),
            };
            IList<ActiveEffectRow> rows = view.Active(effects, Today, null);
            Assert.Equal(3, rows.Count);
            Assert.Equal("Выпуск фабрики -40 % — до " + new TableText().Date(Today + 30) + " (30 дн.)", rows[0].Text, "the soonest to end first");
            Assert.Equal("Contoso", rows[0].Company);
            Assert.Equal("Мой завод", rows[1].Company);
            Assert.Equal("весь рынок", rows[2].Company);
            Assert.Equal("Спрос: low-end -20 % — до " + new TableText().Date(Today + 90) + " (90 дн.)", rows[2].Text);

            IList<ActiveEffectRow> mine = view.Active(effects, Today, new JournalFilter { OnlyMine = true });
            Assert.Equal(2, mine.Count, "the filter keeps the player's effects and the global ones");
            Assert.Equal(0, new List<ActiveEffectRow>(mine).FindAll(r => r.CompanyKey == "ai:3").Count);
        }

        [Test]
        public void AnActiveEffect_ShowsTheCurrentValueOfACurve_AndMarksAFrozenOne_AndSkipsWhatIsOver()
        {
            var text = new TableText();
            var catalog = new FakeCatalog(Fx.DesignLimits());
            var view = new JournalView(text, id => catalog.Get(id), (effect, day) => effect.value * 2f, null);

            AppliedEffect frozen = Timed("research.speed", 1.1f, Today + 10, state: AppliedEffectStates.Suspended);
            AppliedEffect done = Timed("research.speed", 1.1f, Today + 10, state: AppliedEffectStates.Done);
            AppliedEffect oneOff = Timed("research.speed", 1.1f, 0);
            IList<ActiveEffectRow> rows = view.Active(new[] { Timed("research.speed", 1.1f, Today + 10), frozen, done, oneOff, null }, Today, null);

            Assert.Equal(2, rows.Count, "a finished effect, a one-off and null are not listed");
            Assert.True(rows[0].Text.StartsWith("Скорость исследований +120 %"), "the value of the curve on this day (the test doubles it): " + rows[0].Text);
            Assert.True(rows[0].Frozen || rows[1].Frozen);
            string frozenText = rows[0].Frozen ? rows[0].Text : rows[1].Text;
            Assert.True(frozenText.EndsWith("(заморожено)"), frozenText);
        }

        [Test]
        public void AnActiveEffectWithoutAChannel_IsDescribedByItsOwnLine()
        {
            var text = new TableText();
            var odd = new AppliedEffect
            {
                id = "2/common/0", instanceId = 2, type = "Odd", companyKey = CompanyKey.Player, endDay = Today + 5, state = AppliedEffectStates.Active, chance = 1f,
                textKey = "es.fx.popularity", textArgs = new[] { "+3%" },
            };
            Assert.Equal("Популярность CPU: +3% — до " + text.Date(Today + 5) + " (5 дн.)", ViewOver(text).Active(new[] { odd }, Today, null)[0].Text);
        }

        // ---- the history ----

        private static EventLogEntry Entry(int day, string eventId, string company, string kind, string choice = null, params EffectRecord[] records)
        {
            var entry = new EventLogEntry { day = day, instanceId = day, eventId = eventId, companyKey = company, kind = kind, choiceId = choice };
            entry.effects.AddRange(records);
            return entry;
        }

        private static EffectRecord Record(string key, string arg, bool hit = true, bool clamped = false)
        {
            return new EffectRecord { type = "Money", value = -1f, hit = hit, clamped = clamped, textKey = key, textArgs = new[] { arg } };
        }

        [Test]
        public void TheHistory_IsNewestFirst_NamesTheDateTheCompanyTheEventAndWhatHappened()
        {
            var text = new TableText();
            text.Names[CompanyKey.Player] = "Мой завод";
            text.Names["ai:1"] = "Contoso";
            EventDefinition hire = ChoiceEvents.Make("es.hire", new[] { Fx.Choice("hire"), Fx.Choice("decline") });
            text.With("es.t.hire", "Нанять", "Hire");
            JournalView view = ViewOver(text, hire);

            var entries = new List<EventLogEntry>
            {
                Entry(Defs.Day(1985, 6, 1), "es.hire", CompanyKey.Player, EventLogKinds.Fired, null, Record("es.fx.money.earn", "$200K")),
                Entry(Defs.Day(1985, 6, 5), "es.hire", CompanyKey.Player, EventLogKinds.Resolved, "hire", Record("es.fx.money.spend", "$1.04M")),
                Entry(Defs.Day(1985, 6, 9), "es.hire", "ai:1", EventLogKinds.AutoResolved, "hire"),
                Entry(Defs.Day(1985, 6, 12), "es.hire", CompanyKey.Player, EventLogKinds.Cancelled),
                Entry(Defs.Day(1985, 6, 20), "dfr.recall", EventCompanyKeys.Global, EventLogKinds.EffectEnded, null, Record("es.fx.money.spend", "$5K", hit: true, clamped: true)),
                Entry(Defs.Day(1985, 6, 21), "es.hire", CompanyKey.Player, EventLogKinds.EffectFailed, null, Record("es.fx.reputation", "+1", hit: false)),
            };

            IList<JournalRow> rows = view.History(entries, null);
            Assert.Equal(6, rows.Count);
            Assert.Equal(Defs.Day(1985, 6, 21), rows[0].Day, "the newest first");
            Assert.Equal(Defs.Day(1985, 6, 1), rows[5].Day);

            Assert.Equal("01.06.1985  Мой завод: Событие es.hire", rows[5].Header);
            Assert.Equal("Сработало", rows[5].Outcome);
            Assert.Equal("Расход $1.04M", rows[4].Lines[0]);
            Assert.Equal("Выбрано: Нанять", rows[4].Outcome);
            Assert.Equal("Выбрано автоматически: Нанять", rows[3].Outcome);
            Assert.True(rows[3].Header.Contains("Contoso"), rows[3].Header);
            Assert.Equal("Отменено", rows[2].Outcome);
            Assert.Equal("Эффект закончился", rows[1].Outcome);
            Assert.Equal("Расход $5K (ограничено)", rows[1].Lines[0]);
            Assert.True(rows[1].Header.EndsWith(": dfr.recall"), "an event the registry does not know is named by its id: " + rows[1].Header);
            Assert.Equal("Эффект не применён", rows[0].Outcome);
            Assert.Equal("Репутация: +1 (не сработало)", rows[0].Lines[0], "a chance that did not come up is said so after the decision");
            Assert.True(rows[4].Text.Contains("\n"), "a row with effects is a block of lines");
            Assert.Equal(rows[2].Header + " — " + rows[2].Outcome, rows[2].Text);
        }

        [Test]
        public void TheHistoryFilters_KeepThePlayersEntriesAndTheGlobalOnes_AndOneCategory()
        {
            var text = new TableText();
            EventDefinition a = ChoiceEvents.Make("es.a", null);
            a.category = "universal";
            EventDefinition b = ChoiceEvents.Make("es.b", null);
            b.category = "history";
            JournalView view = ViewOver(text, a, b);

            var entries = new List<EventLogEntry>
            {
                Entry(1, "es.a", CompanyKey.Player, EventLogKinds.Fired),
                Entry(2, "es.a", "ai:1", EventLogKinds.Fired),
                Entry(3, "es.b", EventCompanyKeys.Global, EventLogKinds.Fired),
                Entry(4, "es.b", "ai:2", EventLogKinds.Fired),
                Entry(5, "es.gone", CompanyKey.Player, EventLogKinds.Cancelled),
            };

            Assert.Equal(5, view.History(entries, new JournalFilter()).Count);
            Assert.Equal(3, view.History(entries, new JournalFilter { OnlyMine = true }).Count, "the player's two and the global one");
            Assert.Equal(2, view.History(entries, new JournalFilter { Category = "history" }).Count);
            Assert.Equal(1, view.History(entries, new JournalFilter { Category = "history", OnlyMine = true }).Count);
            Assert.Equal("history,universal", string.Join(",", new List<string>(view.Categories(entries)).ToArray()));
            Assert.Equal(0, view.History(null, null).Count);
        }

        [Test]
        public void FR_8_5_AChangeOfLanguage_RedrawsTheRowsFromTheirKeys_WithTheSameNumbers()
        {
            var text = new TableText();
            EventDefinition hire = ChoiceEvents.Make("es.hire", new[] { Fx.Choice("hire"), Fx.Choice("decline") });
            text.With("es.t.hire", "Нанять", "Hire");
            JournalView view = ViewOver(text, hire);

            var entries = new List<EventLogEntry>
            {
                Entry(Defs.Day(1985, 6, 5), "es.hire", CompanyKey.Player, EventLogKinds.Resolved, "hire",
                    new EffectRecord { type = "ModifierTimed", channel = "research.speed", value = 1.12f, durationDays = 365, hit = true,
                                       textKey = "es.fx.modifier", textArgs = new[] { "research.speed", "+12%", "365" } }),
            };

            JournalRow ru = view.History(entries, null)[0];
            Assert.Equal("Выбрано: Нанять", ru.Outcome);
            Assert.Equal("Скорость исследований: +12% на 365 дн.", ru.Lines[0]);

            text.Language = "en";
            JournalRow en = view.History(entries, null)[0];
            Assert.Equal("Chosen: Hire", en.Outcome);
            Assert.Equal("Research speed: +12% for 365 days", en.Lines[0]);
            Assert.Equal("+12%", entries[0].effects[0].textArgs[1], "the saved arguments did not change");
        }

        [Test]
        public void ARecordOfAnOlderSave_WithoutAText_IsStillShown()
        {
            var text = new TableText();
            JournalView view = ViewOver(text);
            var old = new EffectRecord { type = "Money", value = -25000f, hit = true };
            var oldChannel = new EffectRecord { type = "ModifierTimed", channel = "research.speed", value = 1.2f, hit = true };

            Assert.True(view.RecordLine(old).StartsWith("Money"), view.RecordLine(old));
            Assert.Equal("ModifierTimed +20 %", view.RecordLine(oldChannel));
        }

        // ---- the store ----

        [Test]
        public void AC_8_2_AJournalOf400Entries_LosesItsOldestWhenThe401stComes()
        {
            var store = new EventLogStore();
            var added = new List<int>();
            store.Added += entry => added.Add(entry.day);

            for (int day = 1; day <= 400; day++) store.Add(Entry(day, "es.x", CompanyKey.Player, EventLogKinds.Fired));
            Assert.Equal(400, store.Count);
            Assert.Equal(1, store.Entries[0].day);

            store.Add(Entry(401, "es.x", CompanyKey.Player, EventLogKinds.Fired));
            Assert.Equal(400, store.Count, "still 400");
            Assert.Equal(2, store.Entries[0].day, "the oldest is gone");
            Assert.Equal(401, store.Entries[399].day);
            Assert.Equal(401, added.Count, "the window hears every entry");

            // a limit set by the packs
            var small = new EventLogStore(() => 3);
            for (int day = 1; day <= 5; day++) small.Add(Entry(day, "es.x", CompanyKey.Player, EventLogKinds.Fired));
            Assert.Equal("3,4,5", string.Join(",", new List<EventLogEntry>(small.Entries).ConvertAll(e => e.day.ToString()).ToArray()));
        }

        [Test]
        public void TheTextOfARecord_SurvivesTheSave_SoTheJournalCanBeDrawnAfterALoad()
        {
            var effect = new AppliedEffect
            {
                id = "1/hire/0", instanceId = 1, type = "Money", value = -1041000f, hit = true, clamped = true, textKey = "es.fx.money.spend", textArgs = new[] { "$1.04M" },
            };
            var store = new EventLogStore();
            store.Add(EventLogStore.Entry(Today, new EventInstanceDto { id = 1, eventId = "es.hire", companyKey = CompanyKey.Player }, EventLogKinds.Resolved, "hire",
                new[] { effect }));

            var dto = new EventSystemSaveDto();
            store.Export(dto);
            JsonObject json = (JsonObject)ModJson.From(dto);
            var errors = new List<string>();
            EventSystemSaveDto back = ModJson.Bind<EventSystemSaveDto>(json, errors);
            Assert.Equal(0, errors.Count, string.Join("; ", errors));

            var loaded = new EventLogStore();
            var problems = new List<string>();
            loaded.Import(back, problems);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));

            EffectRecord record = loaded.Entries[0].effects[0];
            Assert.Equal("es.fx.money.spend", record.textKey);
            Assert.Equal("$1.04M", record.textArgs[0]);
            Assert.True(record.clamped);
            Assert.Equal("Расход $1.04M (ограничено)", ViewOver(new TableText()).RecordLine(record));
        }

        [Test]
        public void AnEntryOfAnOlderSaveWithoutAText_IsLoaded_WithNoArguments()
        {
            var dto = new EventSystemSaveDto();
            dto.log = new List<EventLogEntry>
            {
                new EventLogEntry
                {
                    day = 5, eventId = "es.hire", companyKey = CompanyKey.Player, kind = EventLogKinds.Fired,
                    effects = new List<EffectRecord> { new EffectRecord { type = "Money", value = 10f, textArgs = null } },
                },
            };
            var loaded = new EventLogStore();
            loaded.Import(dto, new List<string>());
            Assert.Equal(1, loaded.Count);
            Assert.NotNull(loaded.Entries[0].effects[0].textArgs);
            Assert.Equal(0, loaded.Entries[0].effects[0].textArgs.Length);
        }
    }
}
