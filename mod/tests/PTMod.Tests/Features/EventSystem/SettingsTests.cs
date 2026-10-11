using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Json;
using PTMod.Features.EventSystem;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// The settings page of the hub (FR-11, TSK-023): the three parameters, the line that says what each level of the frequency means, the button of the journal, and the
    /// debug list "Запустить сейчас" that exists only with the flag <c>debug.events</c> and fires the event for the player today with its conditions skipped (AC-11.2). The
    /// widgets are Unity's and are not run here; what they are made of is: the rows, the context of the firing, the texts in both languages, and the way the page is wired.
    /// </summary>
    [Covers(typeof(EventSettings), typeof(DebugEventRow), typeof(EventParams))]
    public class SettingsTests
    {
        private static string SrcFolder()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "EventSystem");
        }

        [Test]
        public void TheFrequencyLegend_NamesEveryLevel_InBothLanguages()
        {
            var text = new TableText();
            Assert.Equal("Частота: 0 — Выкл, 1 — Редко (×0,5), 2 — Обычно (×1), 3 — Часто (×1,5).", EventSettings.FrequencyLegend(text));

            text.Language = "en";
            Assert.Equal("Frequency: 0 — Off, 1 — Rare (×0.5), 2 — Normal (×1), 3 — Frequent (×1.5).", EventSettings.FrequencyLegend(text));

            Assert.Equal("es.hub.frequency.0", EventSettings.FrequencyLabelKey(-4), "a number below the range is the lowest level");
            Assert.Equal("es.hub.frequency.3", EventSettings.FrequencyLabelKey(17), "and one above it the highest");
            Assert.Equal(EventSettings.FrequencyLevels, EventParams.Create("event-system")[0].Max + 1, "the legend has a line for every value of the slider");
            Assert.Throws<ArgumentNullException>(() => EventSettings.FrequencyLegend(null));
        }

        [Test]
        public void EveryKeyThePageBuildsByName_IsInBothTables()
        {
            var text = new TableText();
            var keys = new List<string>();
            for (int level = 0; level < EventSettings.FrequencyLevels; level++) keys.Add(EventSettings.FrequencyLabelKey(level));
            foreach (EventFireResult result in Enum.GetValues(typeof(EventFireResult))) keys.Add("es.hub.debug.result." + result.ToString().ToLowerInvariant());
            keys.AddRange(new[] { "es.hub.title", "es.hub.open_log", "es.hub.debug.title", "es.hub.debug.hint", "es.hub.debug.fire", "es.hub.debug.frozen", "es.hub.debug.empty" });

            foreach (string language in new[] { "ru", "en" })
            {
                text.Language = language;
                foreach (string key in keys) Assert.NotNull(text.Find(key), language + " lacks " + key);
                Assert.True(text.L("es.hub.debug.result", "es.x", "ok").Contains("es.x"));
                Assert.True(text.L("es.hub.debug.pack", "market-shocks").Contains("market-shocks"));
            }
            Assert.Equal(0, text.Warnings.Count, string.Join("; ", text.Warnings));
        }

        [Test]
        public void TheDebugRows_AreGroupedByPack_SortedById_AndTheRowsOfAFrozenPackAreGrey()
        {
            EventDefinition b = Fx.Event("ms.b", null);
            b.packId = "market-shocks";
            EventDefinition a = Fx.Event("ms.a", null);
            a.packId = "market-shocks";
            a.title = LocText.Of("Забастовка", "Strike");
            EventDefinition h = Fx.Event("hist.x", null);
            h.packId = "historical-events";
            EventDefinition ownA = Fx.Event("es.z", null);
            ownA.packId = "event-system";

            var text = new TableText();
            IList<DebugEventRow> rows = EventSettings.DebugRows(new[] { b, h, ownA, a }, pack => pack != "market-shocks", text);

            Assert.Equal("ms.a,ms.b,hist.x,es.z", string.Join(",", rows.Select(r => r.EventId).ToArray()), "packs in the order they appear, events of a pack by id");
            Assert.Equal("Забастовка", rows[0].Title);
            Assert.Equal("ms.b.title", rows[1].Title.Replace("[", string.Empty).Replace("]", string.Empty), "an event whose title has no text is shown by its key");
            Assert.False(rows[0].CanFire);
            Assert.Equal("es.hub.debug.frozen", rows[0].BlockedKey);
            Assert.True(rows[2].CanFire);
            Assert.True(rows[3].CanFire);
            Assert.True(rows[0].Detail.Contains("Manual"), "the trigger is shown: " + rows[0].Detail);

            Assert.Equal(0, EventSettings.DebugRows(null, null, text).Count);
            Assert.Equal(0, EventSettings.DebugRows(new EventDefinition[0], null, null).Count);
            Assert.Equal(1, EventSettings.DebugRows(new[] { a, null, new EventDefinition() }, null, text).Count, "empty records are left out; no rule for packs - all are on");
        }

        [Test]
        public void AC_11_2_TheDebugButton_FiresTheEventForThePlayerToday_AndTheLetterHasThreeOptions()
        {
            Assert.Equal("debug.events", EventSettings.DebugFlag);
            EventFireContext context = EventSettings.DebugFire();
            Assert.Equal(CompanyKey.Player, context.CompanyKey, "the target is the player");
            Assert.True(context.IgnoreConditions, "the conditions of the event are skipped");
            Assert.Equal(0, context.DelayDays, "today");

            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                source.Add("event-system", "config", EventTestKit.ShippedContent("event-system", "config.json"));
                source.Add("event-system", "events", EventTestKit.Fixture(EventTestKit.StarterEvents));
                catalog.RegisterPack("event-system", "event-system", new PackOptions());
                catalog.LoadPending();
                Assert.NotNull(catalog.Get("es.trade_show"));

                var definitions = new List<EventDefinition>(catalog.All);
                var rig = new ChoiceRig(new FakeCatalog(catalog.Config, definitions.ToArray()));
                rig.SetDay(Defs.Day(1990, 3, 12));

                var text = new TableText();
                IList<DebugEventRow> rows = EventSettings.DebugRows(definitions, pack => true, text);
                DebugEventRow row = rows.First(r => r.EventId == "es.trade_show");
                Assert.True(row.CanFire, "the button is on");
                Assert.Equal(10, rows.Count, "the list has every event of the registry");

                // the owner presses "Запустить сейчас" on es.trade_show
                EventFireResult result = rig.Rig.Scheduler.Fire(row.EventId, EventSettings.DebugFire());
                rig.Deliver(rig.Today);

                Assert.Equal(EventFireResult.Fired, result);
                EventInstanceDto instance = rig.Rig.Instance("es.trade_show");
                Assert.NotNull(instance, "the instance exists the same day");
                Assert.Equal(CompanyKey.Player, instance.companyKey);
                FakeMail.Letter letter = rig.Mail.OfInstance(instance.id);
                Assert.NotNull(letter, "the letter came the same day");
                Assert.True(letter.Delivered);
                Assert.Equal(3, letter.Plan.Choices.Count, "with the three options");
                Assert.Equal("big_booth,small_booth,skip", string.Join(",", letter.Plan.Choices.Select(c => c.Id).ToArray()));
                Assert.Equal(0, rig.UiFaults.Count, string.Join("; ", rig.UiFaults));
            });
        }

        [Test]
        public void TheFiringOfAFrozenPack_AnswersDisabled_WhichIsWhyItsRowIsGrey()
        {
            EventDefinition shock = Fx.Event("ms.shock", null);
            shock.packId = "market-shocks";
            var catalog = new FakeCatalog(Fx.DesignLimits(), shock).Pack("market-shocks", new PackOptions { SettingsFeatureId = "market-shocks" });
            var rig = new EffectsRig(catalog);
            rig.World.Today = Defs.Day(1990, 1, 1);
            rig.FrozenPacks.Add("market-shocks");

            Assert.Equal(EventFireResult.Disabled, rig.Scheduler.Fire("ms.shock", EventSettings.DebugFire()));
            IList<DebugEventRow> rows = EventSettings.DebugRows(catalog.All, pack => !rig.FrozenPacks.Contains(pack), new TableText());
            Assert.False(rows[0].CanFire);

            rig.FrozenPacks.Clear();
            Assert.Equal(EventFireResult.Fired, rig.Scheduler.Fire("ms.shock", EventSettings.DebugFire()));
        }

        [Test]
        public void TheHubPage_IsDeclaredByTheFeature_OpensTheJournal_AndShowsTheDebugListOnlyUnderTheFlag()
        {
            string feature = SourceScan.Strip(SourceScan.ReadText(Path.Combine(SrcFolder(), "EventSystemFeature.cs")), false);
            Assert.True(Regex.IsMatch(feature, @"_ctx\s*\.\s*HubPage\s*\(\s*""es\.hub\.title""\s*,\s*EventSettingsPage\.Build\s*\)"), "FeatureContext.HubPage");

            string page = SourceScan.Strip(SourceScan.ReadText(Path.Combine(SrcFolder(), "UI", "EventSettingsPage.cs")), false);
            Assert.True(page.Contains("EventLog.OpenWindow()"), "the button of the journal");
            Assert.True(page.Contains("es.hub.open_log"), "with its caption");
            Assert.True(Regex.IsMatch(page, @"if\s*\(\s*ModSettings\.Debug\(\s*EventSettings\.DebugFlag\s*\)\s*\)\s*BuildDebugList\("), "the debug list is built only under ModSettings.Debug(\"debug.events\")");
            Assert.True(page.Contains("EventBus.Fire(row.EventId, EventSettings.DebugFire())"), "the button fires through the API of the bus");
            Assert.Equal(1, Regex.Matches(page, @"BuildDebugList\(").Count - 1, "the list has one call site (plus its own declaration)");
        }
    }
}
