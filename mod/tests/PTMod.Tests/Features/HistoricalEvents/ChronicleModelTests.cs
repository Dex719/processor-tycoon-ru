using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.L10n;
using PTMod.Features.HistoricalEvents;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.HistoricalEvents
{
    /// <summary>
    /// The page "Хроника отрасли" (TSK-022; FR-15; AC-15.1): the lines of the three tabs, made by <see cref="ChronicleModel"/> from the section of the save and the calendar. The
    /// world is a fake over the real Russian table, the real calendar and the real events of the pack; the figures are written the way the game writes them in the examples
    /// of the requirements ("1 млрд", "10 млн"). The widgets of the page itself need the game and are checked by reading the source.
    /// </summary>
    [Covers(typeof(ChronicleModel), typeof(ChronicleRow), typeof(ChronicleTab), typeof(IChronicleWorld), typeof(CatchUpMarks))]
    public class ChronicleModelTests
    {
        private const string Win95 = "hist.1995.win95";
        private const string EuFine = "hist.2009.eufine";

        private static string SrcPath(params string[] parts)
        {
            return Path.Combine(new[] { SourceScan.SrcDirOrFail() }.Concat(parts).ToArray());
        }

        private static JsonObject Table(string language)
        {
            return HistFixture.ObjFromFile(SourceScan.ReadText(SrcPath("L10n", language, "historical-events.json")));
        }

        private static List<CalendarEntry> LoadCalendar()
        {
            JsonArray records = HistFixture.ObjFromFile(SourceScan.ReadText(SrcPath("Content", "historical-events", "calendar.json"))).GetArray("records");
            var list = new List<CalendarEntry>();
            foreach (JsonObject json in records.OfType<JsonObject>())
            {
                var messages = new List<string>();
                CalendarEntry entry = HistFixture.Map(json, messages);
                Assert.NotNull(entry, string.Join(" | ", messages));
                list.Add(entry);
            }
            return list;
        }

        private static int Day(string iso)
        {
            SimpleDate date;
            Assert.True(SimpleDate.TryParse(iso, out date), iso);
            return date.Days;
        }

        // ---- the fake world ----

        private sealed class World : IChronicleWorld
        {
            private readonly JsonObject _table;
            public readonly List<CalendarEntry> Entries;
            public readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "player", "Ваша компания" }, { "ai:0", "Intel" }, { "ai:1", "AMD" }, { "ai:3", "Texas" },
            };
            public readonly Dictionary<string, List<CatchUpEffect>> Effects = new Dictionary<string, List<CatchUpEffect>>(StringComparer.Ordinal);
            public readonly HashSet<string> OffCategories = new HashSet<string>(StringComparer.Ordinal);
            public readonly Dictionary<string, string> Choices = new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly Dictionary<string, string> IdNames = new Dictionary<string, string>(StringComparer.Ordinal);

            public World(string language = "ru")
            {
                _table = Table(language);
                Entries = LoadCalendar();
                Data = new HistSaveData { initialized = true };
                Intensity = 1f;
            }

            public int Today { get; set; }
            public HistSaveData Data { get; set; }
            public float Intensity { get; set; }
            public bool Spoilers { get; set; }

            public IReadOnlyList<CalendarEntry> Calendar { get { return Entries; } }

            public CalendarEntry Entry(string id)
            {
                return Entries.First(e => e.Id == id);
            }

            public bool IsCategoryEnabled(string category) { return !OffCategories.Contains(category); }

            public IReadOnlyList<CatchUpEffect> EffectsOf(string eventId)
            {
                List<CatchUpEffect> list;
                return Effects.TryGetValue(eventId, out list) ? list : null;
            }

            public string ChoiceMadeFor(CalendarEntry entry)
            {
                string choice;
                return Choices.TryGetValue(entry.Id, out choice) ? choice : null;
            }

            public string Text(string key, params string[] args)
            {
                string template;
                Assert.True(_table.TryGetString(key, out template), "the table has the key " + key);
                return args.Length == 0 ? template : string.Format(CultureInfo.InvariantCulture, template, args.Cast<object>().ToArray());
            }

            public bool HasText(string key)
            {
                string template;
                return _table.TryGetString(key, out template);
            }

            public string Plural(string key, long n, params string[] args)
            {
                PluralForm form = PluralRules.Select("ru", n);
                string suffixed = key + "." + PluralRules.Suffix(form);
                var all = new List<object> { n.ToString(CultureInfo.InvariantCulture) };
                all.AddRange(args);
                return string.Format(CultureInfo.InvariantCulture, Text(suffixed), all.ToArray());
            }

            public string CompanyName(string companyKey)
            {
                string name;
                return Names.TryGetValue(companyKey ?? string.Empty, out name) ? name : string.Empty;
            }

            public string EntryTitle(CalendarEntry entry, string subjectName, string secondName)
            {
                return Text(entry.History.TitleKey, entry.Id, subjectName, secondName);
            }

            public string Date(int day) { return SimpleDate.FromDays(day).ToString(); }

            public string MonthYear(int day)
            {
                SimpleDate date = SimpleDate.FromDays(day);
                return Text("hist.month." + date.Month) + " " + date.Year;
            }

            public string Money(float amount)
            {
                if (amount >= 1e9f) return Math.Round(amount / 1e9, 1).ToString("0.#", CultureInfo.InvariantCulture) + " млрд";
                if (amount >= 1e6f) return Math.Round(amount / 1e6, 1).ToString("0.#", CultureInfo.InvariantCulture) + " млн";
                return amount.ToString("0", CultureInfo.InvariantCulture);
            }

            public string Ips(float ips) { return ips.ToString("0.#", CultureInfo.InvariantCulture) + " IPS"; }

            public string ChannelName(string channel) { return channel == "production.output" ? "Выпуск продукции" : channel; }

            public string SegmentName(string segment) { return Text("hist.segment." + segment.ToLowerInvariant()); }

            public string NameOfId(string kind, string id)
            {
                string name;
                if (IdNames.TryGetValue(kind + ":" + id, out name)) return name;
                if (kind == "case" && HasText("hist.chronicle.case." + id)) return Text("hist.chronicle.case." + id);
                return null;
            }
        }

        private static CatchUpEffect Demand(string segment, float multiplier, int days)
        {
            return new CatchUpEffect { Type = "MarketDemand", Segment = segment, Multiplier = multiplier, Scalable = true, DurationDays = days };
        }

        private static EntryStateDto Fired(World world, string id, string iso, int stagesFired = 1, params string[] playerRoles)
        {
            var dto = new EntryStateDto { id = id, status = (int)EntryStatus.Fired, firedDay = Day(iso), stagesFired = stagesFired };
            world.Data.entries.Add(dto);
            return dto;
        }

        private static EntryStateDto State(World world, string id, EntryStatus status, int jitter = 0)
        {
            var dto = new EntryStateDto { id = id, status = (int)status, jitter = jitter };
            world.Data.entries.Add(dto);
            return dto;
        }

        private static void Roles(EntryStateDto dto, string role, params string[] keys)
        {
            dto.targets.Add(new RoleTargetsDto { role = role, companyKeys = keys.ToList() });
        }

        // ---- the cases and disputes ----

        [Test]
        public void AC_15_1_AnAppeal_IsOneLine_WithTheSumTheStageTheRulingThePaidAndTheGuarantee()
        {
            var world = new World { Today = Day("2010-01-01") };
            world.Data.cases.Add(new CaseDto
            {
                caseId = "eu2009", targetKey = "player", mode = "fine", stage = CaseStages.Appeal, fine = 1000000000f, paid = 0f, provisioned = 0f,
                rulingDay = Day("2014-05-13"), guaranteeFeePerMonth = 833333.3f, openedDay = Day("2009-05-13"),
            });

            List<ChronicleRow> rows = ChronicleModel.Rows(ChronicleTab.Cases, world);

            Assert.Equal(1, rows.Count);
            Assert.Equal("Еврокомиссия против вас: 1 млрд, апелляция, решение ~ май 2014, уплачено 0, гарантия 10 млн в год", rows[0].Full);
        }

        [Test]
        public void ACase_AgainstARival_NamesTheCompany_AndAClosedCaseIsNotListed()
        {
            var world = new World();
            world.Data.cases.Add(new CaseDto { caseId = "ftc1998", targetKey = "ai:0", mode = "consent", stage = CaseStages.ConsentFight, fine = 0f, rulingDay = Day("1999-06-01") });
            world.Data.cases.Add(new CaseDto { caseId = "eu2009", targetKey = "ai:1", mode = "fine", stage = CaseStages.Closed, fine = 5e8f });
            world.Data.cases.Add(new CaseDto { caseId = "eu2009", targetKey = "ai:3", mode = "fine", stage = CaseStages.Paid, fine = 5e8f });

            List<ChronicleRow> rows = ChronicleModel.Cases(world);

            Assert.Equal(1, rows.Count, "a case that has ended (or whose fine is paid) is not open any more");
            Assert.Equal("Федеральная торговая комиссия США против Intel: суд, решение ~ июнь 1999", rows[0].Full, "no sum when there is no fine; no 'paid' without one");
        }

        [Test]
        public void ACaseThatWaitsForTheChoiceOfThePlayer_AsksForAttention_AndShowsNoMoneyPaid()
        {
            var world = new World();
            world.Data.cases.Add(new CaseDto { caseId = "eu2009", targetKey = "player", mode = "fine", stage = CaseStages.Awaiting, fine = 4e7f });
            world.Data.cases.Add(new CaseDto { caseId = "eu2009", targetKey = "ai:0", mode = "fine", stage = CaseStages.Awaiting, fine = 4e7f });

            List<ChronicleRow> rows = ChronicleModel.Cases(world);

            Assert.True(rows[0].Attention, "the player has to decide");
            Assert.False(rows[1].Attention, "a rival decides by itself");
            Assert.Equal("Еврокомиссия против вас: 40 млн, ждёт решения", rows[0].Full);
        }

        [Test]
        public void ACaseAfterAnAnnulment_ShowsTheDateOfTheNewFine_AndAReserveShowsWhatWasPutIn()
        {
            var world = new World();
            world.Data.cases.Add(new CaseDto { caseId = "eu2009", targetKey = "player", mode = "fine", stage = CaseStages.Reimposition, fine = 1e9f, reimposeDay = Day("2023-03-01") });
            world.Data.cases.Add(new CaseDto { caseId = "eu2009", targetKey = "ai:0", mode = "fine", stage = CaseStages.Provision, fine = 2e8f, provisioned = 2e8f, rulingDay = Day("2014-05-01") });

            List<ChronicleRow> rows = ChronicleModel.Cases(world);

            Assert.True(rows[0].Full.Contains("штраф отменён, возможен новый, новый штраф ~ март 2023"), rows[0].Full);
            Assert.True(rows[1].Full.Contains("резерв, решение ~ май 2014, уплачено 200 млн"), rows[1].Full);
            Assert.False(rows[1].Full.Contains("гарантия"), "a reserve pays no fee for a guarantee");
        }

        [Test]
        public void ACaseWithoutANameForTheRegulator_IsSaidToBeTheRegulators()
        {
            var world = new World();
            world.Data.cases.Add(new CaseDto { caseId = "unknown9", targetKey = "player", mode = "fine", stage = CaseStages.Appeal, fine = 1e8f, rulingDay = Day("2020-01-01") });

            Assert.True(ChronicleModel.Cases(world)[0].Title == "Регулятор против вас", ChronicleModel.Cases(world)[0].Title);
        }

        [Test]
        public void ASuitAndADispute_AreListedWithTheirPartiesTheStageAndTheNearestDate()
        {
            var world = new World();
            world.Data.settlements.Add(new SettlementDto { id = "s1", plaintiffKey = "player", defendantKey = "ai:0", resolveDay = Day("2011-03-01"), legalPerMonth = 2.5e6f });
            world.Data.disputes.Add(new DisputeDto { disputeId = "d1", mode = "infringement", plaintiffKey = "ai:1", defendantKey = "player", choice = "fight", resolveDay = Day("1994-02-01") });
            world.Data.disputes.Add(new DisputeDto { disputeId = "d2", mode = "licenseWithdrawal", plaintiffKey = "ai:0", defendantKey = "ai:1", choice = string.Empty, endDay = Day("1990-05-01") });

            List<ChronicleRow> rows = ChronicleModel.Cases(world);

            Assert.Equal("Иск: вы против Intel: расходы 2.5 млн в месяц, решение ~ март 2011", rows[0].Full);
            Assert.Equal("Патентный спор: AMD против вас: копирование дизайна, суд, решение ~ февраль 1994", rows[1].Full);
            Assert.Equal("Патентный спор: Intel против AMD: отзыв лицензии, ждёт решения, до май 1990", rows[2].Full);
        }

        [Test]
        public void TheCasesTab_IsEmpty_WhenNothingIsGoingOn_AndAnEmptyTabHasItsOwnWords()
        {
            var world = new World();
            Assert.Equal(0, ChronicleModel.Rows(ChronicleTab.Cases, world).Count);
            Assert.Equal(0, ChronicleModel.Rows(ChronicleTab.Active, world).Count);
            Assert.Equal(0, ChronicleModel.Rows(ChronicleTab.Past, world).Count);
            Assert.Equal(0, ChronicleModel.Rows(ChronicleTab.Past, null).Count);

            foreach (ChronicleTab tab in Enum.GetValues(typeof(ChronicleTab)))
            {
                Assert.True(world.HasText(ChronicleModel.EmptyKey(tab)), tab + ": the words for an empty tab");
            }
        }

        // ---- the past ----

        [Test]
        public void ThePast_ListsTheEntriesThatFired_TheLatestFirst_WithTheDateTheTitleTheCompaniesAndTheOutcome()
        {
            var world = new World { Today = Day("2010-01-01") };
            Fired(world, Win95, "1995-08-30");
            EntryStateDto fine = Fired(world, EuFine, "2009-05-20");
            Roles(fine, "target", "player");
            EntryStateDto jiji = Fired(world, "hist.1999.jiji", "1999-09-21");
            Roles(jiji, "target", "ai:0", "ai:1");

            List<ChronicleRow> rows = ChronicleModel.Past(world);

            Assert.Equal(3, rows.Count);
            Assert.Equal("2009-05-20 — Еврокомиссия штрафует Ваша компания: вы · произошло", rows[0].Full, "the title names the company of the game; the line says 'you'");
            Assert.True(rows[1].Full.StartsWith("1999-09-21 — ", StringComparison.Ordinal) && rows[1].Full.EndsWith(": Intel, AMD · произошло", StringComparison.Ordinal), rows[1].Full);
            Assert.Equal("1995-08-30 — Выход Windows 95: произошло", rows[2].Full, "an entry for the whole market has no companies to name");
        }

        [Test]
        public void ThePast_AddsTheChoiceThePlayerMade()
        {
            var world = new World { Today = Day("2010-01-01") };
            EntryStateDto fine = Fired(world, EuFine, "2009-05-20");
            Roles(fine, "target", "player");
            world.Choices[EuFine] = "Апелляция";

            Assert.True(ChronicleModel.Past(world)[0].Text.EndsWith("произошло, ваш выбор: Апелляция", StringComparison.Ordinal), ChronicleModel.Past(world)[0].Text);
        }

        [Test]
        public void ThePast_TellsWhatBecameOfEntriesThatDidNotFire_AndSkipsTheOnesThatDidNotHappenYet()
        {
            var world = new World { Today = Day("2012-01-01") };
            State(world, "hist.2011.tohoku", EntryStatus.Disabled);
            State(world, "hist.2008.lehman", EntryStatus.Expired, jitter: 5);
            State(world, "hist.2005.archswitch", EntryStatus.Error);
            State(world, "hist.1971.microprocessor", EntryStatus.SkippedBeforeStart);
            State(world, "hist.2017.corewar", EntryStatus.Pending);
            State(world, "hist.2009.settlement", EntryStatus.Waiting);

            List<ChronicleRow> rows = ChronicleModel.Past(world);

            Assert.Equal(3, rows.Count, "the entries before the start of the game, the pending and the waiting ones are not history of this game");
            Assert.True(rows[0].Full.Contains("2011-03-11") && rows[0].Full.EndsWith("категория выключена", StringComparison.Ordinal), rows[0].Full);
            Assert.True(rows[1].Full.Contains("2008-09-20") && rows[1].Full.EndsWith("в этой партии не состоялось", StringComparison.Ordinal), rows[1].Full + " (the anchor moved by the jitter)");
            Assert.True(rows[2].Attention && rows[2].Full.EndsWith("сбой при обработке", StringComparison.Ordinal), rows[2].Full);
        }

        [Test]
        public void ThePast_NamesAtMostFourCompanies_AndCountsTheRest()
        {
            var world = new World { Today = Day("2000-01-01") };
            EntryStateDto dto = Fired(world, "hist.1998.sub1000", "1998-04-20");
            world.Names["ai:4"] = "C4";
            world.Names["ai:5"] = "C5";
            world.Names["ai:6"] = "C6";
            Roles(dto, "allCpuCompanies", "ai:0", "ai:1", "ai:3", "ai:4", "ai:5", "ai:6");

            string text = ChronicleModel.Past(world)[0].Text;

            Assert.True(text.StartsWith("Intel, AMD, Texas, C4, и ещё 2 · ", StringComparison.Ordinal), text);
        }

        [Test]
        public void WithoutSpoilers_NothingOfTheFutureIsShown_AnywhereOnThePage()
        {
            var world = new World { Today = Day("1995-01-01"), Spoilers = false };
            foreach (CalendarEntry entry in world.Entries) State(world, entry.Id, entry.Anchor.Days < world.Today ? EntryStatus.SkippedBeforeStart : EntryStatus.Pending);

            foreach (ChronicleTab tab in Enum.GetValues(typeof(ChronicleTab)))
            {
                foreach (ChronicleRow row in ChronicleModel.Rows(tab, world))
                {
                    Assert.False(Regex.IsMatch(row.Full, @"\b(199[5-9]|20\d\d)\b"), tab + ": a line names a year to come: " + row.Full);
                }
            }
            Assert.Equal(0, ChronicleModel.Past(world).Count);
        }

        [Test]
        public void WithSpoilers_TheYearOfTheThreeNearestEntriesIsShown_AndNothingMore()
        {
            var world = new World { Today = Day("1995-01-01"), Spoilers = true };
            foreach (CalendarEntry entry in world.Entries) State(world, entry.Id, entry.Anchor.Days < world.Today ? EntryStatus.SkippedBeforeStart : EntryStatus.Pending);

            List<ChronicleRow> rows = ChronicleModel.Past(world);

            Assert.Equal(1, rows.Count, "the line of the years only: no title, no detail of an entry to come");
            Assert.Equal("Ближайшие события (годы): 1995, 1997, 1998", rows[0].Full, "win95 (1995-08), the Asian crisis (1997-07) and the sub-$1000 PC war (1998-04)");
            Assert.False(world.Entries.Where(e => e.Anchor.Days >= world.Today).Any(e => rows[0].Full.Contains(world.EntryTitle(e, string.Empty, string.Empty))));
        }

        [Test]
        public void WithSpoilers_AnEntryOfACategoryThatIsOff_OrAlreadyPast_IsNotCounted()
        {
            var world = new World { Today = Day("1995-09-01"), Spoilers = true };
            foreach (CalendarEntry entry in world.Entries) State(world, entry.Id, entry.Anchor.Days < world.Today ? EntryStatus.SkippedBeforeStart : EntryStatus.Pending);
            world.OffCategories.Add("macro");   // 1997 Asian crisis is macro
            world.OffCategories.Add("disaster");

            string years = ChronicleModel.Past(world)[0].Text;

            Assert.Equal(3, years.Split(',').Length, years);
            Assert.False(years.StartsWith("1995", StringComparison.Ordinal), "win95 has fired by now: " + years);

            var few = new World { Today = Day("2025-06-01"), Spoilers = true };
            foreach (CalendarEntry entry in few.Entries) State(few, entry.Id, entry.Anchor.Days < few.Today ? EntryStatus.SkippedBeforeStart : EntryStatus.Pending);
            Assert.Equal(0, ChronicleModel.Past(few).Count, "no entry is left to come");
        }

        // ---- what is in force ----

        private static World WithWin95(int daysAfterFiring, float intensity = 1f)
        {
            var world = new World { Today = Day("1995-08-30") + daysAfterFiring, Intensity = intensity };
            Fired(world, Win95, "1995-08-30");
            world.Effects[Win95] = new List<CatchUpEffect> { Demand("midrange", 1.25f, 730), Demand("lowend", 1.15f, 730) };
            return world;
        }

        [Test]
        public void TheActiveTab_ShowsTheLastingEffectsOfAFiredEntry_WithTheDaysLeft()
        {
            World world = WithWin95(100);

            List<ChronicleRow> rows = ChronicleModel.Active(world);

            Assert.Equal(2, rows.Count);
            Assert.Equal("Выход Windows 95: спрос: Mid Range +25 %, осталось 630 дней", rows[0].Full);
            Assert.Equal("Выход Windows 95: спрос: Low End +15 %, осталось 630 дней", rows[1].Full);
        }

        [Test]
        public void TheActiveTab_ShowsTheSizeTheEffectHasWithTheIntensityApplied()
        {
            Assert.True(ChronicleModel.Active(WithWin95(1, 0.5f))[0].Full.Contains("Mid Range +13 %"), "1.25 at half the intensity is +12.5 %, rounded away from zero");
            Assert.True(ChronicleModel.Active(WithWin95(1, 1.5f))[0].Full.Contains("Mid Range +38 %"), "and at one and a half +37.5 %");

            var unscaled = WithWin95(1, 0.5f);
            unscaled.Effects[Win95] = new List<CatchUpEffect> { new CatchUpEffect { Type = "MarketDemand", Segment = "midrange", Multiplier = 1.25f, Scalable = false, DurationDays = 730 } };
            Assert.True(ChronicleModel.Active(unscaled)[0].Full.Contains("+25 %"), "an effect that is not scalable keeps its size");
        }

        [Test]
        public void TheActiveTab_DropsTheEffectsThatHaveEnded_AndSortsBySoonestEnd()
        {
            World world = WithWin95(400);
            world.Effects[Win95] = new List<CatchUpEffect> { Demand("midrange", 1.25f, 730), Demand("lowend", 1.15f, 300), Demand("highend", 1.1f, 500) };

            List<ChronicleRow> rows = ChronicleModel.Active(world);

            Assert.Equal(2, rows.Count, "the 300-day effect ended 100 days ago");
            Assert.True(rows[0].Text.Contains("High End") && rows[0].Text.EndsWith("осталось 100 дней", StringComparison.Ordinal), rows[0].Full);
            Assert.True(rows[1].Text.EndsWith("осталось 330 дней", StringComparison.Ordinal), rows[1].Full);

            World ended = WithWin95(730);
            Assert.Equal(0, ChronicleModel.Active(ended).Count, "on the day it ends nothing is left");
        }

        [Test]
        public void TheActiveTab_TakesTheLaterStagesFromTheirOwnDay_AndOnlyTheOnesThatHavePlayed()
        {
            var world = new World { Today = Day("2020-07-01") };
            Fired(world, "hist.2020.covid", "2020-03-11", stagesFired: 1);
            CalendarEntry covid = world.Entry("hist.2020.covid");
            Assert.True(covid.Stages.Count >= 2, "covid has a second stage");
            world.Effects[covid.Stages[0].Fires[0].EventId] = new List<CatchUpEffect> { Demand("all", 0.8f, 200) };
            world.Effects[covid.Stages[1].Fires[0].EventId] = new List<CatchUpEffect> { Demand("all", 1.3f, 365) };

            List<ChronicleRow> first = ChronicleModel.Active(world);
            Assert.Equal(1, first.Count, "the second stage has not played yet");
            Assert.True(first[0].Text.StartsWith("спрос: все сегменты -20 %", StringComparison.Ordinal), first[0].Text);

            world.Data.entries.Single(e => e.id == "hist.2020.covid").stagesFired = covid.Stages.Count;
            world.Today = world.Data.entries.Single(e => e.id == "hist.2020.covid").firedDay + covid.Stages[1].OffsetDays + 100;
            List<ChronicleRow> both = ChronicleModel.Active(world);
            Assert.True(both.Any(r => r.Text.Contains("+30 %") && r.Text.EndsWith("осталось 265 дней", StringComparison.Ordinal)), string.Join(" | ", both.Select(r => r.Full)));
        }

        [Test]
        public void TheActiveTab_ShowsAnEntryThatWasCaughtUpAtTheStart_ForAsLongAsItsEffectsLast()
        {
            var world = new World { Today = Day("2021-03-01") };
            EntryStateDto shortage = State(world, "hist.2020.shortage", EntryStatus.SkippedBeforeStart);
            shortage.flags.Add(CatchUpMarks.FlagFor(Day("2021-01-01")));
            CalendarEntry entry = world.Entry("hist.2020.shortage");
            world.Effects[entry.Stages[0].Fires[0].EventId] = new List<CatchUpEffect> { Demand("all", 0.9f, 1000) };
            State(world, "hist.2020.covid", EntryStatus.SkippedBeforeStart);   // before the start and not caught up: nothing of it is in force

            List<ChronicleRow> rows = ChronicleModel.Active(world);

            int ends = entry.Anchor.Days + 1000;
            Assert.Equal(1, rows.Count);
            Assert.True(rows[0].Text.EndsWith("осталось " + (ends - world.Today) + " дней", StringComparison.Ordinal), rows[0].Full + " (ends on day " + ends + ")");
        }

        [Test]
        public void ACaughtUpStageThatBeganAfterTheStart_WasNeverApplied_AndIsNotShown()
        {
            var world = new World { Today = Day("2021-03-01") };
            EntryStateDto covid = State(world, "hist.2020.covid", EntryStatus.SkippedBeforeStart);
            CalendarEntry entry = world.Entry("hist.2020.covid");
            covid.flags.Add(CatchUpMarks.FlagFor(entry.Anchor.Days + 10));   // the game started 10 days after the anchor: the stage at +90 never played
            world.Effects[entry.Stages[0].Fires[0].EventId] = new List<CatchUpEffect> { Demand("all", 0.8f, 1000) };
            world.Effects[entry.Stages[1].Fires[0].EventId] = new List<CatchUpEffect> { Demand("all", 1.3f, 1000) };

            List<ChronicleRow> rows = ChronicleModel.Active(world);

            Assert.Equal(1, rows.Count, string.Join(" | ", rows.Select(r => r.Full)));
            Assert.True(rows[0].Text.Contains("-20 %"));
        }

        [Test]
        public void TheActiveTab_ListsTheExportRulesTheWarsAndTheRaces()
        {
            var world = new World { Today = Day("2023-01-01") };
            world.Data.exportRules.Add(new ExportRuleDto
            {
                ruleId = "us2022", threshold = 120.5f, blockedShare = 0.15f, endDay = 0,
                exemptions = new List<ExemptionDto> { new ExemptionDto { companyKey = "ai:0", kind = "license", factor = 0.5f, endDay = 0 }, new ExemptionDto { companyKey = "player", kind = "lobby" } },
            });
            world.Data.priceWars.Add(new PriceWarDto { warId = "core2006", endDay = Day("2023-06-01") });
            world.IdNames["war:core2006"] = "Войны Core 2";
            world.Data.races.Add(new RaceDto { raceId = "ghz2000", attribute = "frequency", topN = 3, bonus = 1.1f, endDay = Day("2023-09-01") });
            world.Data.races.Add(new RaceDto { raceId = "core2017", attribute = "coreCount", topN = 5, bonus = 1.05f, endDay = 0 });

            List<ChronicleRow> rows = ChronicleModel.Active(world);

            Assert.Equal(4, rows.Count);
            Assert.Equal("Экспортный контроль: порог 120.5 IPS, под ограничением до 15 % покупателей, без срока; исключения: Intel (лицензия), вы (лоббирование)", rows[0].Full);
            Assert.Equal("Войны Core 2: идёт до 2023-06-01", rows[1].Full);
            Assert.Equal("Гонка частот: топ-3, рейтинг +10 %, до 2023-09-01", rows[2].Full);
            Assert.Equal("Гонка ядер: топ-5, рейтинг +5 %, без срока", rows[3].Full);

            world.IdNames.Clear();
            Assert.Equal("Ценовая война: идёт до 2023-06-01", ChronicleModel.Active(world)[1].Full, "a war without a name of its own is a price war");
        }

        [Test]
        public void SignedPercent_WritesTheChangeWithItsSign()
        {
            Assert.Equal("+25 %", ChronicleModel.SignedPercent(1.25f));
            Assert.Equal("-10 %", ChronicleModel.SignedPercent(0.9f));
            Assert.Equal("0 %", ChronicleModel.SignedPercent(1f));
            Assert.Equal("0 %", ChronicleModel.SignedPercent(float.NaN));
            Assert.Equal("+13 %", ChronicleModel.SignedPercent(1.125f));
            Assert.Equal("-100 %", ChronicleModel.SignedPercent(0f));
        }

        [Test]
        public void CatchUpMarks_RoundTripTheStartDay_AndIgnoreOtherFlags()
        {
            string flag = CatchUpMarks.FlagFor(14975);
            Assert.Equal("caughtUp-14975", flag);
            Assert.True(CalendarValidator.IsFlagName(flag), "a flag the section can hold");

            int day;
            Assert.True(CatchUpMarks.TryRead(new[] { "launched", flag }, out day));
            Assert.Equal(14975, day);
            Assert.False(CatchUpMarks.TryRead(new[] { "launched", "caughtUp-x", "caughtUp--3" }, out day));
            Assert.Equal(0, day);
            Assert.False(CatchUpMarks.TryRead(null, out day));
        }

        [Test]
        public void TheFlagOfACaughtUpEntry_SurvivesTheSectionOfTheSave()
        {
            var data = new HistSaveData { initialized = true };
            var entry = new EntryStateDto { id = "hist.2020.shortage", status = (int)EntryStatus.SkippedBeforeStart };
            entry.flags.Add(CatchUpMarks.FlagFor(14975));
            data.entries.Add(entry);

            HistSaveData back = HistSaveCodec.Read(HistSaveCodec.Write(data), null);

            int day;
            Assert.True(CatchUpMarks.TryRead(back.entries[0].flags, out day), "the chronicle still knows the entry was caught up after a load");
            Assert.Equal(14975, day);
        }

        // ---- the strings ----

        [Test]
        public void EveryWordOfThePage_IsInBothLanguages_IncludingTheKeysItBuilds()
        {
            var keys = new List<string>
            {
                "hist.chronicle.title", "hist.chronicle.unavailable", "hist.chronicle.error", "hist.chronicle.soon", "hist.chronicle.others", "hist.chronicle.you",
                "hist.chronicle.past.title", "hist.chronicle.past.text", "hist.chronicle.active.text", "hist.chronicle.segment.all", "hist.chronicle.fx.demand",
                "hist.chronicle.fx.channel", "hist.chronicle.until", "hist.chronicle.open", "hist.chronicle.war.title", "hist.chronicle.war.text",
                "hist.chronicle.export.title", "hist.chronicle.export.text", "hist.chronicle.export.exceptions", "hist.chronicle.exempt.line",
                "hist.chronicle.race.other", "hist.chronicle.race.text", "hist.chronicle.case.vsPlayer", "hist.chronicle.case.vsCompany", "hist.chronicle.case.regulator",
                "hist.chronicle.case.ruling", "hist.chronicle.case.reimpose", "hist.chronicle.case.paid", "hist.chronicle.case.guarantee",
                "hist.chronicle.settlement.title", "hist.chronicle.settlement.text", "hist.chronicle.dispute.title", "hist.chronicle.dispute.mode.other",
                "hist.chronicle.dispute.choice.waiting", "hist.chronicle.dispute.until", "hist.chronicle.debug.heading", "hist.chronicle.debug.entry",
                "hist.chronicle.debug.forceLeader", "hist.chronicle.debug.forceLeader.tip", "hist.chronicle.debug.fire", "hist.chronicle.debug.fired",
                "hist.chronicle.debug.notFired", "hist.chronicle.debug.noEntries",
            };
            foreach (string name in new[] { "past", "active", "cases" }) keys.Add("hist.chronicle.tab." + name);
            foreach (string name in new[] { "past", "active", "cases" }) keys.Add("hist.chronicle.empty." + name);
            foreach (string name in new[] { "fired", "choice", "expired", "disabled", "error" }) keys.Add("hist.chronicle.outcome." + name);
            for (int stage = 0; stage <= CaseStages.Closed; stage++) keys.Add("hist.chronicle.case.stage." + stage);
            for (int month = 1; month <= 12; month++) keys.Add("hist.month." + month);
            foreach (string attribute in RaceAttributes.All) keys.Add("hist.chronicle.race." + attribute);
            foreach (string kind in new[] { ExemptionKinds.License, ExemptionKinds.Lobby }) keys.Add("hist.chronicle.exempt." + kind);
            foreach (string mode in new[] { DisputeModes.LicenseWithdrawal, DisputeModes.Infringement }) keys.Add("hist.chronicle.dispute.mode." + mode);
            foreach (string action in new[] { DisputeActions.Arbitrate, DisputeActions.License, DisputeActions.Independent, DisputeActions.Fight, DisputeActions.Settle, DisputeActions.Redesign })
                keys.Add("hist.chronicle.dispute.choice." + action);
            foreach (string caseId in new[] { "eu2009", "ftc1998", "jftc2005" }) keys.Add("hist.chronicle.case." + caseId);

            var missing = new List<string>();
            foreach (string language in new[] { "ru", "en" })
            {
                JsonObject table = Table(language);
                foreach (string key in keys)
                {
                    string text;
                    if (!table.TryGetString(key, out text) || string.IsNullOrWhiteSpace(text)) missing.Add(language + ":" + key);
                }
            }
            Assert.True(missing.Count == 0, "keys without a text: " + string.Join(", ", missing));
        }

        [Test]
        public void TheCaseNames_CoverEveryCaseTheCalendarOpens()
        {
            // The regulators are named by the id of the case the calendar's fires carry as their first extra argument.
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (CalendarEntry entry in LoadCalendar())
            {
                foreach (FireSpec fire in entry.Stages.SelectMany(s => s.Fires))
                {
                    if (fire.EventId.StartsWith("hist.decide.antitrust.", StringComparison.Ordinal) || fire.EventId == "hist.2005.jftc") ids.Add(fire.ExtraArgs[0]);
                }
            }
            Assert.True(ids.Count >= 3, "eu2009, ftc1998, jftc2005: " + string.Join(", ", ids));
            foreach (string id in ids) Assert.True(Table("ru").TryGetString("hist.chronicle.case." + id, out _), "a name for the regulator of " + id);
        }

        // ---- the page and the world (read from the source: they need the game) ----

        private static string Source(params string[] parts)
        {
            return SourceScan.Strip(SourceScan.ReadText(SrcPath(new[] { "Features", "HistoricalEvents" }.Concat(parts).ToArray())), true);
        }

        [Test]
        public void ThePage_IsRegisteredInTheHub_WithThreeTabs_AndTheDebugBlockOnlyInADebugBuild()
        {
            string feature = Source("HistoricalEventsFeature.cs");
            Assert.True(Regex.IsMatch(feature, @"ctx\.HubPage\(\s*ChroniclePage\.TitleKey\s*,\s*ChroniclePage\.Build\s*\)"), "ctx.HubPage(title, build)");

            string page = SourceScan.ReadText(SrcPath("Features", "HistoricalEvents", "UI", "ChroniclePage.cs"));
            string code = SourceScan.Strip(page, true);
            Assert.True(page.Contains("TitleKey = \"hist.chronicle.title\""), "the heading of the page");
            foreach (string tab in new[] { "Past", "Active", "Cases" }) Assert.True(code.Contains("ChronicleTab." + tab), "the tab " + tab);
            Assert.True(Regex.Matches(code, @"AddTab\(").Count >= 4, "three tabs are added");

            int debugStart = code.IndexOf("#if PTMOD_DEBUG", StringComparison.Ordinal);
            Assert.True(debugStart > 0, "the debug block is conditional");
            string afterIf = code.Substring(debugStart);
            Assert.True(afterIf.Contains("DebugFireNow(") && afterIf.Contains("ForceLeader") && afterIf.Contains("ModUI.Dropdown("), "the debug block: the list of entries, run now, debugForceLeader");
            string beforeIf = code.Substring(0, debugStart);
            Assert.False(beforeIf.Contains("DebugFireNow(") || beforeIf.Contains("ForceLeader"), "nothing of the debug tools outside the conditional blocks");
            Assert.True(code.Contains("HistSettings.Debug"), "the block is shown with the setting debug");
        }

        [Test]
        public void ThePage_DrawsTheLinesOfTheModel_AndTheWorldOnlyReads()
        {
            string page = Source("UI", "ChroniclePage.cs");
            Assert.True(page.Contains("ChronicleModel.Rows(") && page.Contains("new ChronicleWorld("), "the lines are the model's");
            Assert.True(page.Contains("ModHooks.Guard("), "a failing build of the list is a line of text, not an exception");
            Assert.True(page.Contains("ModUI.ScrollView(") && page.Contains("ModUI.Info("), "the list of the design: ModUI.ScrollView with ModUI.Info lines");

            string world = Source("Game", "ChronicleWorld.cs");
            Assert.False(Regex.IsMatch(world, @"ModModifiers\.|EventBus\.|ModMail\.|ModNotify\.|SetFlag|ModSettings\.Set|\.Scheduler|\.Services"), "the world of the chronicle only reads");
            Assert.True(world.Contains("EventLog.Entries") && world.Contains("EventRegistry.Get("), "the choices come from the journal of the engine, the effects from its definitions");
        }

        [Test]
        public void TheCatchUp_MarksTheEntriesItMadeEffectsOf()
        {
            string code = Source("CatchUpService.cs");
            Assert.True(Regex.IsMatch(code, @"Scheduler\.SetFlag\(\s*item\.EntryId\s*,\s*CatchUpMarks\.FlagFor\(\s*startDay\s*\)\s*\)"), "the chronicle learns from the flag which entries were caught up");
        }
    }
}
