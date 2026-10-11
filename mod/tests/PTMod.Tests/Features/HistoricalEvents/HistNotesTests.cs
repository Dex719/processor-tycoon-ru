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
    /// The notes of the letters of the pack (TSK-021; FR-14; AC-14.1): the line "Источник: домен" that ends the <c>text</c> of every event, the reference "как это было"
    /// (<c>cause</c>) that <c>historyNotes</c> may switch off, the checks of the validator for the texts of an event, and the shipped content that depends on them:
    /// every event has its text and cause, the events with a choice for the player are <c>important</c>, the pack is in the category <c>history</c>.
    /// </summary>
    [Covers(typeof(HistNotes), typeof(IEventTexts))]
    public class HistNotesTests
    {
        private static string ContentPath(string file)
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Content", "historical-events", file);
        }

        private static JsonObject Table(string language)
        {
            return HistFixture.ObjFromFile(SourceScan.ReadText(Path.Combine(SourceScan.SrcDirOrFail(), "L10n", language, "historical-events.json")));
        }

        private static List<JsonObject> Records(string file)
        {
            JsonArray array = HistFixture.ObjFromFile(SourceScan.ReadText(ContentPath(file))).GetArray("records");
            Assert.NotNull(array, file + " has records");
            return array.OfType<JsonObject>().ToList();
        }

        private static string StringOf(JsonObject obj, string member)
        {
            string value;
            return obj.TryGetString(member, out value) ? value : null;
        }

        private static string KeyOf(JsonObject record, string member)
        {
            var text = record.GetObject(member);
            return text == null ? null : StringOf(text, "key");
        }

        /// <summary>A text of the table with its arguments, the way <c>ModL10n.L</c> fills it in.</summary>
        private static string Say(JsonObject table, string key, params string[] args)
        {
            string template = StringOf(table, key);
            Assert.NotNull(template, "the key " + key + " has a text");
            return args.Length == 0 ? template : string.Format(CultureInfo.InvariantCulture, template, args.Cast<object>().ToArray());
        }

        private static Func<string, string[], string> Speaker(JsonObject table)
        {
            return (key, args) => Say(table, key, args);
        }

        private static List<CalendarEntry> Calendar()
        {
            var entries = new List<CalendarEntry>();
            foreach (JsonObject json in Records("calendar.json"))
            {
                var messages = new List<string>();
                CalendarEntry entry = HistFixture.Map(json, messages);
                Assert.NotNull(entry, string.Join(" | ", messages));
                entries.Add(entry);
            }
            return entries;
        }

        private static Func<string, HistoryInfo> Lookup(List<CalendarEntry> calendar)
        {
            return id =>
            {
                HistoryInfo news = BuiltInNews.Find(id);
                if (news != null) return news;
                CalendarEntry entry = calendar.FirstOrDefault(e => e.Id == id);
                return entry == null ? null : entry.History;
            };
        }

        private static HistoryInfo Info(string fact = "hist.x.fact", params string[] sources)
        {
            var info = new HistoryInfo { TitleKey = "hist.x.title", FactKey = fact };
            info.Sources.AddRange(sources);
            return info;
        }

        // ---- the domain ----

        [Test]
        public void DomainOf_GivesTheRegisteredDomain_WithoutLanguageOrServerNames()
        {
            Assert.Equal("wikipedia.org", HistNotes.DomainOf("https://en.wikipedia.org/wiki/Windows_95"));
            Assert.Equal("wikipedia.org", HistNotes.DomainOf("https://ru.wikipedia.org/wiki/X"));
            Assert.Equal("csis.org", HistNotes.DomainOf("https://www.csis.org/analysis/x"));
            Assert.Equal("globalcompetitionreview.com", HistNotes.DomainOf("https://globalcompetitionreview.com/a"));
            Assert.Equal("bbc.co.uk", HistNotes.DomainOf("https://www.bbc.co.uk/news/1"));
            Assert.Equal("example.com", HistNotes.DomainOf("https://user:pw@news.example.com:8443/a?b=1#c"));
            Assert.Equal("example.com", HistNotes.DomainOf("HTTPS://WWW.EXAMPLE.COM"));
        }

        [Test]
        public void DomainOf_GivesNothing_ForWhatIsNotAUrlWithAHost()
        {
            Assert.Equal(string.Empty, HistNotes.DomainOf(null));
            Assert.Equal(string.Empty, HistNotes.DomainOf(string.Empty));
            Assert.Equal(string.Empty, HistNotes.DomainOf("   "));
            Assert.Equal(string.Empty, HistNotes.DomainOf("https:///path"));
            Assert.Equal(string.Empty, HistNotes.DomainOf("not a url"));
        }

        [Test]
        public void SourceDomain_TakesTheFirstSourceThatHasOne()
        {
            Assert.Equal("wikipedia.org", HistNotes.SourceDomain(Info("f", "https://en.wikipedia.org/a", "https://www.csis.org/b")));
            Assert.Equal("csis.org", HistNotes.SourceDomain(Info("f", "", "https://www.csis.org/b")));
            Assert.Equal(string.Empty, HistNotes.SourceDomain(Info("f")));
            Assert.Equal(string.Empty, HistNotes.SourceDomain(null));
        }

        // ---- the slots ----

        [Test]
        public void TheSlots_FollowTheExtraArguments_AndStayInsideTheEightTheTextTestFormatsWith()
        {
            Assert.Equal(4, HistNotes.FirstExtra);
            Assert.Equal(2, HistNotes.MaxExtraArgs);
            Assert.Equal(HistNotes.FirstExtra + HistNotes.MaxExtraArgs, HistNotes.SourceSlot);
            Assert.Equal(HistNotes.SourceSlot + 1, HistNotes.NotesSlot);
            Assert.Equal(HistNotes.NotesSlot + 1, HistNotes.Length);
            Assert.True(HistNotes.NotesSlot < 8, "every text of the mod is formatted with eight arguments by the architecture tests: a placeholder above {7} would fail them");
        }

        [Test]
        public void Decorate_FillsTheSourceAndTheCause_AndPadsTheArguments()
        {
            var table = Table("ru");
            HistoryInfo info = Info("hist.1995.win95.fact", "https://en.wikipedia.org/wiki/Windows_95");

            string[] args = HistNotes.Decorate("hist.1995.win95", new[] { "hist.1995.win95", "Intel" }, id => id == "hist.1995.win95" ? info : null, true, Speaker(table));

            Assert.Equal(HistNotes.Length, args.Length);
            Assert.Equal("hist.1995.win95", args[0]);
            Assert.Equal("Intel", args[1]);
            Assert.Equal("\nИсточник: wikipedia.org", args[HistNotes.SourceSlot], "the line carries its line break: an event with no source adds nothing");
            Assert.Equal(Say(table, "hist.1995.win95.fact"), args[HistNotes.NotesSlot]);
            Assert.True(args.All(a => a != null), "no null argument");
        }

        [Test]
        public void Decorate_WithTheNotesOff_KeepsTheSource_AndEmptiesTheCause()
        {
            var table = Table("ru");
            HistoryInfo info = Info("hist.1995.win95.fact", "https://en.wikipedia.org/wiki/Windows_95");

            string[] args = HistNotes.Decorate("hist.1995.win95", new[] { "hist.1995.win95" }, id => info, false, Speaker(table));

            Assert.Equal(string.Empty, args[HistNotes.NotesSlot], "historyNotes off: the cause is empty");
            Assert.Equal("\nИсточник: wikipedia.org", args[HistNotes.SourceSlot], "only the reference is switched off");
        }

        [Test]
        public void Decorate_NeverMovesTheArgumentsOfTheTable()
        {
            var table = Table("en");
            var given = new[] { "hist.2009.eufine", "Intel", "AMD", "ai:1", "eu2009" };

            string[] args = HistNotes.Decorate("hist.decide.antitrust.fine", given, id => Info("hist.2009.eufine.fact", "https://gulfnews.com/a"), true, Speaker(table));

            for (int i = 0; i < given.Length; i++) Assert.Equal(given[i], args[i], "position " + i);
            Assert.Equal(5, given.Length, "the argument array that was passed is left as it was");
        }

        [Test]
        public void Decorate_FindsTheEntryOfTheFizzleNews_InItsSecondArgument()
        {
            var table = Table("ru");
            var seen = new List<string>();
            Func<string, HistoryInfo> lookup = id =>
            {
                seen.Add(id);
                return id == "hist.1987.koshiba" ? Info("hist.1987.koshiba.fact", "https://en.wikipedia.org/wiki/Toshiba") : null;
            };

            string[] args = HistNotes.Decorate(DispatcherCore.FizzleEventId, new[] { "Скандал", "hist.1987.koshiba" }, lookup, true, Speaker(table));

            Assert.Equal("\nИсточник: wikipedia.org", args[HistNotes.SourceSlot]);
            Assert.Equal(seen[0], "hist.1987.koshiba", "the first argument of the news is its title, not an entry");
            Assert.Equal("hist.1987.koshiba", HistNotes.EntryIdOf(DispatcherCore.FizzleEventId, new[] { "t", "hist.1987.koshiba" }));
            Assert.Equal("hist.x", HistNotes.EntryIdOf("hist.decide.export", new[] { "hist.x", "y" }));
            Assert.Null(HistNotes.EntryIdOf("hist.decide.export", new string[0]));
            Assert.Null(HistNotes.EntryIdOf("hist.decide.export", null));
        }

        [Test]
        public void Decorate_PrefersTheEntryTheArgumentsName_ToTheEventId()
        {
            // The events of the pack are shared (the price wars, the disputes): the entry is the one the firing is for.
            var table = Table("ru");
            var entries = new Dictionary<string, HistoryInfo>
            {
                { "hist.decide.pricewar.aggressor", Info("hist.1985.dramexit.fact", "https://example.org/event") },
                { "hist.2006.core2war", Info("hist.2006.core2war.fact", "https://en.wikipedia.org/wiki/Core_2") },
            };

            string[] args = HistNotes.Decorate("hist.decide.pricewar.aggressor", new[] { "hist.2006.core2war", "Intel" }, id => entries.ContainsKey(id) ? entries[id] : null, true, Speaker(table));

            Assert.Equal("\nИсточник: wikipedia.org", args[HistNotes.SourceSlot]);
            Assert.Equal(Say(table, "hist.2006.core2war.fact"), args[HistNotes.NotesSlot]);
        }

        [Test]
        public void Decorate_FallsBackToTheEventItself_ForTheNewsAboutTheGamesOwnEvents()
        {
            var table = Table("ru");
            string[] args = HistNotes.Decorate(BuiltInNews.SolarFoundriesEventId, new[] { "SolarFoundries", "EMD" }, BuiltInNews.Find, true, Speaker(table));

            Assert.Equal("\nИсточник: wikipedia.org", args[HistNotes.SourceSlot]);
            Assert.Equal(Say(table, "hist.news.solarfoundries.fact"), args[HistNotes.NotesSlot]);
        }

        [Test]
        public void Decorate_OfAnEventWithNoHistory_LeavesBothSlotsEmpty_AndOverwritesOldOnes()
        {
            var table = Table("ru");
            var stale = new string[HistNotes.Length + 2];
            for (int i = 0; i < stale.Length; i++) stale[i] = "x" + i;

            string[] args = HistNotes.Decorate("hist.fizzle", stale, id => null, true, Speaker(table));

            Assert.Equal(stale.Length, args.Length, "a longer array keeps its length");
            Assert.Equal(string.Empty, args[HistNotes.SourceSlot]);
            Assert.Equal(string.Empty, args[HistNotes.NotesSlot]);
            Assert.Equal("x0", args[0]);
            Assert.Equal("x" + (HistNotes.Length + 1), args[HistNotes.Length + 1]);
        }

        [Test]
        public void Decorate_ToleratesNullsAndMissingPieces()
        {
            string[] none = HistNotes.Decorate("hist.a", null, null, true, null);
            Assert.Equal(HistNotes.Length, none.Length);
            Assert.True(none.All(a => a == string.Empty));

            string[] withNull = HistNotes.Decorate("hist.a", new string[] { null, "b" }, id => null, true, (k, a) => k);
            Assert.Equal(string.Empty, withNull[0]);
            Assert.Equal("b", withNull[1]);

            // A history without a fact key or without a usable source adds only what it has.
            string[] noFact = HistNotes.Decorate("hist.a", new[] { "hist.a" }, id => Info(null, "https://example.org/x"), true, (k, a) => k + (a.Length > 0 ? ":" + a[0] : string.Empty));
            Assert.Equal("\nhist.mail.source:example.org", noFact[HistNotes.SourceSlot]);
            Assert.Equal(string.Empty, noFact[HistNotes.NotesSlot]);
            string[] noSource = HistNotes.Decorate("hist.a", new[] { "hist.a" }, id => Info("hist.a.fact"), true, (k, a) => k);
            Assert.Equal(string.Empty, noSource[HistNotes.SourceSlot]);
            Assert.Equal("hist.a.fact", noSource[HistNotes.NotesSlot]);
        }

        [Test]
        public void LastLine_IsTheTextAfterTheLastLineBreak()
        {
            Assert.Equal("Источник: x.org", HistNotes.LastLine("Один.\nИсточник: x.org"));
            Assert.Equal("Один.", HistNotes.LastLine("Один."));
            Assert.Equal("b", HistNotes.LastLine("a\nb\n"));
            Assert.Equal(string.Empty, HistNotes.LastLine(null));
        }

        [Test]
        public void PlaceholdersOf_ListsTheNumbersOfTheSlotsATextNames()
        {
            Assert.Equal(new[] { 1, 2, 6 }, HistNotes.PlaceholdersOf("{1} против {2}.{6}").ToArray());
            Assert.Equal(new[] { 0 }, HistNotes.PlaceholdersOf("{0:N2} и { не слот").ToArray());
            Assert.Equal(0, HistNotes.PlaceholdersOf("нет слотов").Count);
            Assert.Equal(0, HistNotes.PlaceholdersOf(null).Count);
        }

        // ---- the news about the game's own events ----

        [Test]
        public void BuiltInNews_HaveAHistoryOfTheirOwn_WithAnHttpsSource()
        {
            Assert.Equal(2, BuiltInNews.EventIds.Length);
            foreach (string id in BuiltInNews.EventIds)
            {
                HistoryInfo info = BuiltInNews.Find(id);
                Assert.NotNull(info, id);
                Assert.True(info.TitleKey.StartsWith("hist.", StringComparison.Ordinal) && info.FactKey.StartsWith("hist.", StringComparison.Ordinal), id + ": keys of the feature");
                Assert.True(info.Sources.Count > 0 && info.Sources.All(s => s.StartsWith("https://", StringComparison.Ordinal)), id + ": https sources");
                Assert.True(BuiltInNews.IsNews(id));
            }
            Assert.Null(BuiltInNews.Find("hist.1995.win95"));
            Assert.Null(BuiltInNews.Find(null));
            Assert.False(BuiltInNews.IsNews("hist.fizzle"));
        }

        // ---- the validator ----

        private sealed class TextsOf : IEventTexts
        {
            public readonly HashSet<string> Events = new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<string> WithText = new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<string> WithCause = new HashSet<string>(StringComparer.Ordinal);

            public bool Contains(string eventId) { return Events.Contains(eventId); }

            public bool HasText(string eventId) { return WithText.Contains(eventId); }

            public bool HasCause(string eventId) { return WithCause.Contains(eventId); }
        }

        [Test]
        public void Validator_WarnsOfAnEventWithoutACause_AndOfOneWithoutAText_ButKeepsTheEntry()
        {
            var events = new TextsOf();
            events.Events.Add("hist.1995.win95");
            events.WithText.Add("hist.1995.win95");   // no cause

            Verdict verdict = HistFixture.Judge(HistFixture.Entry(HistFixture.Plain), events, null);

            Assert.True(verdict.Ok, "a gap in the texts is a warning, the entry still works: " + verdict.Text);
            Assert.True(verdict.HasWarning("has no cause"), verdict.Text);
            Assert.False(verdict.HasWarning("has no text"), verdict.Text);

            events.WithText.Clear();
            events.WithCause.Add("hist.1995.win95");
            verdict = HistFixture.Judge(HistFixture.Entry(HistFixture.Plain), events, null);
            Assert.True(verdict.Ok);
            Assert.True(verdict.HasWarning("has no text"), verdict.Text);
        }

        [Test]
        public void Validator_SaysNothing_OfAnEventWithBothTexts_AndAsksNothingOfACatalogWithoutThem()
        {
            var full = new TextsOf();
            full.Events.Add("hist.1995.win95");
            full.WithText.Add("hist.1995.win95");
            full.WithCause.Add("hist.1995.win95");

            Verdict verdict = HistFixture.Judge(HistFixture.Entry(HistFixture.Plain), full, null);
            Assert.True(verdict.Ok);
            Assert.Equal(0, verdict.Warnings.Count, verdict.Text);

            // The plain catalog of the other tests only knows ids: no warnings about texts.
            verdict = HistFixture.Judge(HistFixture.Entry(HistFixture.Plain), HistFixture.AllEvents(), null);
            Assert.True(verdict.Ok);
            Assert.False(verdict.HasWarning("has no cause"));
        }

        [Test]
        public void Validator_ChecksTheTextsInTheSecondPhaseToo()
        {
            var events = new TextsOf();
            events.Events.Add("hist.1995.win95");
            var lines = new List<string>();

            bool ok = false;
            HistFixture.WithIds(() => { ok = CalendarValidator.ValidateEventsExist(HistFixture.Entry(HistFixture.Plain), events, lines); });

            Assert.True(ok, "no error: the event exists");
            Assert.True(lines.Any(l => l.Contains("has no cause")), string.Join(" | ", lines));
            Assert.True(lines.Any(l => l.Contains("has no text")), string.Join(" | ", lines));
        }

        [Test]
        public void Validator_RejectsMoreExtraArgumentsThanTheSlotsOfTheNotesAllow()
        {
            Verdict verdict = HistFixture.Judge(HistFixture.Plain, obj =>
            {
                JsonObject fire = obj.GetArray("stages").OfType<JsonObject>().First().GetArray("fires").OfType<JsonObject>().First();
                var extra = new JsonArray();
                foreach (string value in new[] { "a", "b", "c" }) extra.Add(value);
                fire.Set("extraArgs", extra);
            });

            Assert.False(verdict.Ok);
            Assert.True(verdict.HasError("extraArgs"), verdict.Text);

            verdict = HistFixture.Judge(HistFixture.Plain, obj =>
            {
                JsonObject fire = obj.GetArray("stages").OfType<JsonObject>().First().GetArray("fires").OfType<JsonObject>().First();
                var extra = new JsonArray();
                foreach (string value in new[] { "a", "b" }) extra.Add(value);
                fire.Set("extraArgs", extra);
            });
            Assert.True(verdict.Ok, "two extra arguments are what the slots leave: " + verdict.Text);
        }

        // ---- the shipped content ----

        [Test]
        public void EveryEventOfThePack_HasATextAndACause()
        {
            var problems = new List<string>();
            foreach (JsonObject record in Records("events.json"))
            {
                string id = StringOf(record, "id");
                if (string.IsNullOrEmpty(KeyOf(record, "text"))) problems.Add(id + ": no text");
                if (string.IsNullOrEmpty(KeyOf(record, "cause"))) problems.Add(id + ": no cause");
            }
            Assert.True(problems.Count == 0, string.Join(" | ", problems));
        }

        [Test]
        public void TheCauseOfTheEvents_IsTheSharedKey_AndThatKeyIsTheSlotOfTheReference()
        {
            // The two news that tell about the engine's own doing (not about a real event) keep a cause of their own; every other event shows the fact of its entry.
            var own = new HashSet<string>(StringComparer.Ordinal) { "hist.fizzle", "hist.catchup.summary" };
            foreach (JsonObject record in Records("events.json"))
            {
                string id = StringOf(record, "id");
                string cause = KeyOf(record, "cause");
                if (own.Contains(id)) Assert.NotEqual(HistNotes.CauseKey, cause, id);
                else Assert.Equal(HistNotes.CauseKey, cause, id + ": the cause is the reference of the entry");
            }

            foreach (string language in new[] { "ru", "en" })
            {
                Assert.Equal("{" + HistNotes.NotesSlot + "}", StringOf(Table(language), HistNotes.CauseKey), language + ": hist.cause is the placeholder of the reference");
            }
        }

        [Test]
        public void TheTextOfEveryEvent_EndsWithThePlaceholderOfTheSourceLine_InBothLanguages()
        {
            string slot = "{" + HistNotes.SourceSlot + "}";
            foreach (string language in new[] { "ru", "en" })
            {
                JsonObject table = Table(language);
                foreach (JsonObject record in Records("events.json"))
                {
                    string key = KeyOf(record, "text");
                    string text = StringOf(table, key);
                    Assert.NotNull(text, language + ": " + key);
                    Assert.True(text.EndsWith(slot, StringComparison.Ordinal), language + ": " + key + " ends with " + slot);
                    Assert.Equal(1, HistNotes.PlaceholdersOf(text).Count(n => n == HistNotes.SourceSlot), language + ": " + key + " names the slot once");
                }
            }
        }

        [Test]
        public void NoTextOfThePack_NamesAPositionAboveTheNotes()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                JsonObject table = Table(language);
                foreach (var pair in table)
                {
                    string text = StringOf(table, pair.Key);
                    if (text == null || !pair.Key.StartsWith("hist.", StringComparison.Ordinal)) continue;
                    Assert.True(HistNotes.PlaceholdersOf(text).All(n => n <= HistNotes.NotesSlot), language + ": " + pair.Key + " names a position above the notes");
                }
            }
        }

        [Test]
        public void TheLastLineOfEveryText_IsTheDomainOfTheSource_ForEveryEventTheCalendarFires()
        {
            List<CalendarEntry> calendar = Calendar();
            var lookup = Lookup(calendar);
            var events = Records("events.json").ToDictionary(r => StringOf(r, "id"), r => r, StringComparer.Ordinal);
            int checkedFires = 0;

            foreach (string language in new[] { "ru", "en" })
            {
                JsonObject table = Table(language);
                string label = language == "ru" ? "Источник: " : "Source: ";
                foreach (CalendarEntry entry in calendar)
                {
                    string domain = HistNotes.SourceDomain(entry.History);
                    Assert.True(domain.Length > 0, entry.Id + ": the first source has a domain");

                    var fires = entry.Stages.SelectMany(s => s.Fires).Concat(entry.FallbackFires).ToList();
                    foreach (FireSpec fire in fires)
                    {
                        var given = new List<string> { entry.Id, "Acme", "Beta", "ai:1" };
                        given.AddRange(fire.ExtraArgs);
                        string[] args = HistNotes.Decorate(fire.EventId, given.ToArray(), lookup, true, Speaker(table));

                        string text = Say(table, KeyOf(events[fire.EventId], "text"), args);
                        Assert.Equal(label + domain, HistNotes.LastLine(text), language + ": the last line of " + fire.EventId + " fired by " + entry.Id);
                        checkedFires++;
                    }
                }
            }
            Assert.True(checkedFires > 100, "both languages, every fire of the 42 entries: " + checkedFires);
        }

        [Test]
        public void TheNewsAboutAnEntryThatDidNotHappen_AndAboutTheGamesOwnEvents_AlsoEndWithTheSource()
        {
            List<CalendarEntry> calendar = Calendar();
            var lookup = Lookup(calendar);
            var events = Records("events.json").ToDictionary(r => StringOf(r, "id"), r => r, StringComparer.Ordinal);
            JsonObject ru = Table("ru");

            CalendarEntry koshiba = calendar.First(e => e.Id == "hist.1987.koshiba");
            string[] fizzle = HistNotes.Decorate(DispatcherCore.FizzleEventId, new[] { "Title", koshiba.Id }, lookup, true, Speaker(ru));
            Assert.Equal("Источник: wikipedia.org", HistNotes.LastLine(Say(ru, KeyOf(events["hist.fizzle"], "text"), fizzle)));

            foreach (string id in BuiltInNews.EventIds)
            {
                string[] args = HistNotes.Decorate(id, new[] { id, "EMD", "SolarFoundries" }, lookup, true, Speaker(ru));
                string text = Say(ru, KeyOf(events[id], "text"), args);
                Assert.Equal("Источник: wikipedia.org", HistNotes.LastLine(text), id);
                Assert.True(Say(ru, KeyOf(events[id], "cause"), args).Length > 20, id + ": the cause is the fact");
            }
        }

        [Test]
        public void WithHistoryNotesOff_TheCauseOfEveryEvent_ComesOutEmpty_AndWithThemOn_ItIsTheFactOfTheEntry()
        {
            List<CalendarEntry> calendar = Calendar();
            var lookup = Lookup(calendar);
            var events = Records("events.json").ToDictionary(r => StringOf(r, "id"), r => r, StringComparer.Ordinal);
            JsonObject ru = Table("ru");

            foreach (CalendarEntry entry in calendar)
            {
                foreach (FireSpec fire in entry.Stages.SelectMany(s => s.Fires))
                {
                    string causeKey = KeyOf(events[fire.EventId], "cause");
                    var given = new List<string> { entry.Id, "Acme", "", "" };
                    given.AddRange(fire.ExtraArgs);

                    string off = Say(ru, causeKey, HistNotes.Decorate(fire.EventId, given.ToArray(), lookup, false, Speaker(ru)));
                    string on = Say(ru, causeKey, HistNotes.Decorate(fire.EventId, given.ToArray(), lookup, true, Speaker(ru)));

                    Assert.Equal(string.Empty, off, entry.Id + "/" + fire.EventId + ": historyNotes off hides the reference");
                    Assert.Equal(Say(ru, entry.History.FactKey), on, entry.Id + "/" + fire.EventId + ": historyNotes on shows the fact of the entry");
                }
            }
        }

        [Test]
        public void AC_14_1_TheLetterOfWindows95_HasTheDemandTheFactAndTheDomain()
        {
            List<CalendarEntry> calendar = Calendar();
            var lookup = Lookup(calendar);
            JsonObject definition = Records("events.json").First(r => StringOf(r, "id") == "hist.1995.win95");

            // "MidRange +25 % of demand for 24 months": the numbers of the effect the engine describes.
            JsonObject midrange = definition.GetArray("effects").OfType<JsonObject>().First(e => StringOf(e.GetObject("params"), "segment") == "midrange");
            Assert.Equal("MarketDemand", StringOf(midrange, "type"));
            Assert.Near(1.25, midrange.GetObject("params").GetDouble("multiplier"), 1e-9, "+25 %");
            Assert.Equal(730, midrange.GetInt32("durationDays"), "24 months");

            foreach (string language in new[] { "ru", "en" })
            {
                JsonObject table = Table(language);
                string[] args = HistNotes.Decorate("hist.1995.win95", new[] { "hist.1995.win95", "Intel", "", "" }, lookup, true, Speaker(table));

                string title = Say(table, KeyOf(definition, "title"), args);
                string text = Say(table, KeyOf(definition, "text"), args);
                string cause = Say(table, KeyOf(definition, "cause"), args);

                if (language == "ru") Assert.Equal("Выход Windows 95", title);
                else Assert.True(title.Contains("Windows 95"), title);
                Assert.Equal((language == "ru" ? "Источник: " : "Source: ") + "wikipedia.org", HistNotes.LastLine(text), "the reference of the letter names the domain");
                Assert.True(cause.Contains("Windows 95"), language + ": the reference tells how it was: " + cause);
            }
        }

        [Test]
        public void TheEventsWithAChoiceForThePlayer_AreImportant_AndTheOthersAreNot()
        {
            var problems = new List<string>();
            int withChoices = 0;
            foreach (JsonObject record in Records("events.json"))
            {
                string id = StringOf(record, "id");
                JsonArray choices = record.GetArray("choices");
                bool hasChoices = choices != null && choices.Count > 0;
                bool important = record.TryGetBool("important", out bool flag) && flag;

                if (hasChoices) withChoices++;
                if (hasChoices && !important) problems.Add(id + ": has a choice but is not important (the letter would not be Critical)");
                if (!hasChoices && important) problems.Add(id + ": no choice, yet important (a window with a pause for a piece of news)");
            }
            Assert.True(withChoices >= 15, "the decisions of the pack: " + withChoices);
            Assert.True(problems.Count == 0, string.Join(" | ", problems));
        }

        [Test]
        public void EveryEventOfThePack_IsInTheHistoryCategory_ThePackDeclares()
        {
            JsonObject config = HistFixture.ObjFromFile(SourceScan.ReadText(ContentPath("config.json")));
            var declared = config.GetArray("categories").OfType<JsonObject>().Select(c => StringOf(c, "id")).ToList();
            Assert.True(declared.Contains("history"), "config.json declares the category history: the journal of the engine shows it");

            foreach (JsonObject record in Records("events.json"))
            {
                Assert.Equal("history", StringOf(record, "category"), StringOf(record, "id") + ": the event is logged in the category history");
            }
        }

        [Test]
        public void TheFiresOfTheCalendar_CarryAtMostTheExtraArgumentsTheSlotsLeave()
        {
            foreach (CalendarEntry entry in Calendar())
            {
                foreach (FireSpec fire in entry.Stages.SelectMany(s => s.Fires).Concat(entry.FallbackFires))
                {
                    Assert.True(fire.ExtraArgs.Length <= HistNotes.MaxExtraArgs, entry.Id + "/" + fire.EventId + ": " + fire.ExtraArgs.Length + " extra arguments");
                }
            }
        }

        [Test]
        public void TheLetterOfTheCatchUp_IsTaggedAndOfNormalPriority()
        {
            // FR-14 item 6: the feature's own letters carry a tag hist:<...> and the priority Normal.
            string code = SourceScan.Strip(SourceScan.ReadText(Path.Combine(SourceScan.SrcDirOrFail(), "Features", "HistoricalEvents", "CatchUpService.cs")), true);
            Assert.True(Regex.IsMatch(code, @"Tag\s*=\s*HistIds\.CatchUpMailTag"), "the letter is tagged");
            Assert.True(Regex.IsMatch(code, @"Priority\s*=\s*MailPriority\.Normal"), "and of priority Normal");
            Assert.Equal("hist:catchup", HistIds.CatchUpMailTag);
        }

        [Test]
        public void TheStringTables_HaveTheKeyOfTheSourceLine_AsInTheDesign()
        {
            Assert.Equal("Источник: {0}", StringOf(Table("ru"), HistNotes.SourceKey));
            Assert.Equal("Source: {0}", StringOf(Table("en"), HistNotes.SourceKey));
        }
    }
}
