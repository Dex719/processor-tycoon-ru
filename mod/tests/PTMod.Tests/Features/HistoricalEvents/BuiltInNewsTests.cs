using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Json;
using PTMod.Features.HistoricalEvents;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.HistoricalEvents
{
    /// <summary>
    /// The letters of reference about the game's own events (TSK-023; FR-16; AC-16.1): when they are sent (<see cref="BuiltInNewsCore"/>: the foundry that appears, the licence that
    /// is revoked, never while loading or fast-forwarding), the events of the pack and their words, and the shape of the patch and the subscription (read from the source: they need
    /// the game).
    /// </summary>
    [Covers(typeof(BuiltInNewsCore), typeof(BuiltInNews))]
    public class BuiltInNewsTests
    {
        private static string Src(params string[] parts)
        {
            return Path.Combine(new[] { SourceScan.SrcDirOrFail() }.Concat(parts).ToArray());
        }

        private static JsonObject Table(string language)
        {
            return HistFixture.ObjFromFile(SourceScan.ReadText(Src("L10n", language, "historical-events.json")));
        }

        private static JsonObject EventRecord(string id)
        {
            JsonArray records = HistFixture.ObjFromFile(SourceScan.ReadText(Src("Content", "historical-events", "events.json"))).GetArray("records");
            JsonObject record = records.OfType<JsonObject>().FirstOrDefault(r => r.GetString("id") == id);
            Assert.NotNull(record, "the pack has the event " + id);
            return record;
        }

        // ---- the spin-off of the foundries ----

        [Test]
        public void TheSpinOff_IsAnnouncedOnce_WhenTheFoundryAppearsAfterTheGamesMethodRan()
        {
            var core = new BuiltInNewsCore();
            core.Baseline(false);

            Assert.False(core.ShouldAnnounceSpinOff(false, false, true), "the method ran and returned early: nothing happened");
            Assert.True(core.ShouldAnnounceSpinOff(true, false, true), "the foundry is there now and was not before: 2009-03-02");
            Assert.False(core.ShouldAnnounceSpinOff(true, false, true), "the method runs again (the company is in trouble): the foundry was there already");
        }

        [Test]
        public void TheSpinOff_IsNotAnnounced_WhenTheFoundryWasThereBeforeTheFeatureLooked()
        {
            var core = new BuiltInNewsCore();
            core.Baseline(true);

            Assert.False(core.ShouldAnnounceSpinOff(true, false, true), "a save in which the spin-off happened long ago");
        }

        [Test]
        public void TheSpinOff_IsNotAnnounced_WhenNothingIsKnownOfTheStateBefore_AndTheFirstLookBecomesTheBaseline()
        {
            var core = new BuiltInNewsCore();
            Assert.False(core.IsKnown);

            Assert.False(core.ShouldAnnounceSpinOff(true, false, true), "without a baseline a foundry that is there may be old");
            Assert.True(core.IsKnown);
            Assert.False(core.ShouldAnnounceSpinOff(true, false, true));

            var fresh = new BuiltInNewsCore();
            Assert.False(fresh.ShouldAnnounceSpinOff(false, false, true));
            Assert.True(fresh.ShouldAnnounceSpinOff(true, false, true), "after a first look at a world without it, the appearance is news");
        }

        [Test]
        public void TheSpinOff_IsNotAnnounced_WhileASaveLoadsOrTheNewGameFastForwards_NorLater()
        {
            var core = new BuiltInNewsCore();
            core.Baseline(false);

            Assert.False(core.ShouldAnnounceSpinOff(true, true, true), "busy: the game replays its history");
            Assert.False(core.ShouldAnnounceSpinOff(true, false, true), "and the news about it does not come later either");
        }

        [Test]
        public void TheSpinOff_IsNotAnnounced_WithTheHistoryNotesOff()
        {
            var core = new BuiltInNewsCore();
            core.Baseline(false);

            Assert.False(core.ShouldAnnounceSpinOff(true, false, false), "these letters are nothing but reference");
            Assert.False(core.ShouldAnnounceSpinOff(true, false, true));
        }

        [Test]
        public void ANewScene_StartsOver()
        {
            var core = new BuiltInNewsCore();
            core.Baseline(false);
            Assert.True(core.ShouldAnnounceSpinOff(true, false, true));

            core.Reset();
            Assert.False(core.IsKnown);
            core.Baseline(false);
            Assert.True(core.ShouldAnnounceSpinOff(true, false, true), "a new game: the spin-off can happen again");
        }

        // ---- the licence ----

        [Test]
        public void TheLicence_IsAnnouncedOnce()
        {
            var core = new BuiltInNewsCore();

            Assert.True(core.ShouldAnnounceLicense(false, true));
            Assert.False(core.ShouldAnnounceLicense(false, true), "once per game");
        }

        [Test]
        public void TheLicence_IsNotAnnounced_WhileLoading_AndTheReplayIsNeverAnnouncedLater()
        {
            var core = new BuiltInNewsCore();

            Assert.False(core.ShouldAnnounceLicense(true, true), "SaveHandler.OnLastLoadedTick raises it again on a load");
            Assert.False(core.ShouldAnnounceLicense(false, true), "what was raised while loading already happened");
        }

        [Test]
        public void TheLicence_IsNotAnnounced_WithTheHistoryNotesOff()
        {
            var core = new BuiltInNewsCore();

            Assert.False(core.ShouldAnnounceLicense(false, false));
        }

        // ---- the events of the pack ----

        [Test]
        public void TheTwoEvents_AreNewsWithoutEffectsOrChoices_ForThePlayer()
        {
            foreach (string id in BuiltInNews.EventIds)
            {
                JsonObject record = EventRecord(id);
                Assert.Equal(0, (record.GetArray("effects") ?? new JsonArray()).Count, id + ": the game does its part, the letter has no effect");
                Assert.True(record.GetArray("choices") == null || record.GetArray("choices").Count == 0, id + ": no choice");
                Assert.False(record.GetBool("important"), id + ": a piece of news, not a window with a pause");
                Assert.Equal("history", record.GetString("category"));
                Assert.Equal("Manual", record.GetObject("trigger").GetString("type"), id + ": fired by the feature");
                Assert.Equal("Player", record.GetObject("targets").GetString("type"), id + ": for the player");
            }
        }

        [Test]
        public void AC_16_1_TheLetterOfTheSpinOff_IsCalledTheSpinOffOfTheFoundriesOfAmd_AndHasAReference()
        {
            JsonObject record = EventRecord(BuiltInNews.SolarFoundriesEventId);
            JsonObject ru = Table("ru");
            JsonObject en = Table("en");

            Assert.Equal("Выделение фабрик AMD", ru.GetString(record.GetObject("title").GetString("key")));
            Assert.True(en.GetString(record.GetObject("title").GetString("key")).Contains("AMD"));

            HistoryInfo info = BuiltInNews.Find(BuiltInNews.SolarFoundriesEventId);
            foreach (JsonObject table in new[] { ru, en })
            {
                string[] args = HistNotes.Decorate(BuiltInNews.SolarFoundriesEventId, new[] { BuiltInNews.SolarFoundriesEventId, "EMD", "SolarFoundries" }, BuiltInNews.Find, true,
                    (key, a) => string.Format(CultureInfo.InvariantCulture, table.GetString(key), a.Cast<object>().ToArray()));
                string text = string.Format(CultureInfo.InvariantCulture, table.GetString(record.GetObject("text").GetString("key")), args.Cast<object>().ToArray());
                string cause = string.Format(CultureInfo.InvariantCulture, table.GetString(record.GetObject("cause").GetString("key")), args.Cast<object>().ToArray());

                Assert.True(text.Contains("EMD") && text.Contains("SolarFoundries"), "the letter names the companies of the game: " + text);
                Assert.True(HistNotes.LastLine(text).EndsWith("wikipedia.org", StringComparison.Ordinal), "the source: " + text);
                Assert.True(cause.Contains("2009") && cause.Contains("GlobalFoundries"), "the reference tells how it was: " + cause);
            }
            Assert.Equal("wikipedia.org", HistNotes.SourceDomain(info));
        }

        [Test]
        public void TheLetterOfTheLicence_HasAReference_WithoutNamingACompanyThatTheGameDoesNotTell()
        {
            JsonObject record = EventRecord(BuiltInNews.X86LicenseEventId);
            foreach (string language in new[] { "ru", "en" })
            {
                JsonObject table = Table(language);
                string text = table.GetString(record.GetObject("text").GetString("key"));
                Assert.False(Regex.IsMatch(text, @"\{[1-5]\}"), language + ": the game does not say which holder left, so the text names no company: " + text);
                Assert.True(table.GetString("hist.news.x86license.fact").Length > 80, language + ": the reference");
            }
        }

        // ---- the patch and the subscription ----

        private static string Code()
        {
            return SourceScan.Strip(SourceScan.ReadText(Src("Features", "HistoricalEvents", "BuiltInEventNews.cs")), false);
        }

        [Test]
        public void ThePatch_IsOneOptionalPostfix_OnTheMethodOfTheSpinOff_ThatChangesNothing()
        {
            string raw = SourceScan.ReadText(Src("Features", "HistoricalEvents", "BuiltInEventNews.cs"));
            string code = Code();

            Assert.Equal(1, Regex.Matches(raw, @"\[ModPatch\(").Count, "one patch");
            Assert.True(Regex.IsMatch(code, @"\[ModPatch\(\s*typeof\(GameCompanyEventHandler\)\s*,\s*""TriggerSolarFoundriesSpinOff""\s*,\s*PatchKind\.Postfix\s*,\s*Optional\s*=\s*true\s*\)\]"),
                "an Optional postfix of CompanyEventHandler.TriggerSolarFoundriesSpinOff");
            Assert.False(Regex.IsMatch(code, @"__result|__instance|\bref\s|\bout\s|return\s+false|PatchKind\.(Prefix|Transpiler|Finalizer)"), "the game's logic is not changed: no result, no argument, no skip");
            Assert.True(code.Contains("ModFeatures.IsActive(") && code.Contains("ModHooks.Guard("), "the patch runs only for an active feature, and under the guard");

            string feature = SourceScan.Strip(SourceScan.ReadText(Src("Features", "HistoricalEvents", "HistoricalEventsFeature.cs")), true);
            Assert.True(Regex.IsMatch(feature, @"ctx\.Patch\(\s*typeof\(BuiltInEventNews\)\s*\)"), "the feature declares the patch class");
        }

        [Test]
        public void TheNews_AreNotSentWhileLoadingOrFastForwarding_AndOnlyWithTheNotesOn()
        {
            string code = Code();

            Assert.True(code.Contains("ModTicks.IsLoading") && code.Contains("ModTicks.IsFastForwarding"), "the same events are raised again when a save loads");
            Assert.True(code.Contains("HistSettings.HistoryNotes"), "the letters are reference: they follow the setting");
            Assert.True(code.Contains("Core.ShouldAnnounceSpinOff(") && code.Contains("Core.ShouldAnnounceLicense("), "the decisions are the pure core's");
        }

        [Test]
        public void TheLicenceSubscription_IsMadeOnceForTheObjectOfTheScene()
        {
            string code = Code();

            Assert.True(Regex.IsMatch(code, @"OnX86LicenseRevoked\s*-=\s*OnLicenseRevoked\s*;\s*helper\.OnX86LicenseRevoked\s*\+=\s*OnLicenseRevoked"), "taken off before it is put on: never twice on one object");
            string feature = SourceScan.Strip(SourceScan.ReadText(Src("Features", "HistoricalEvents", "HistoricalEventsFeature.cs")), true);
            Assert.True(Regex.Matches(feature, @"BuiltInEventNews\.Attach\(\)").Count >= 2, "at GameReady and when the feature is switched on inside a game");
        }

        [Test]
        public void TheNews_GoThroughTheDoorThatFillsInTheNotes()
        {
            string code = Code();

            Assert.True(code.Contains("new EventBusFirer()") && code.Contains("Firer.Fire("), "EventBusFirer.WithNotes puts the source line and the reference into the arguments");
            Assert.True(code.Contains("ModCompanies.PlayerKey"), "for the player");
        }
    }
}
