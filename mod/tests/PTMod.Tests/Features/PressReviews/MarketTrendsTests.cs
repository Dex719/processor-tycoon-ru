using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Features.PressReviews;

namespace PTMod.Tests.PressReviews
{
    /// <summary>
    /// The quarterly notes of the press (FR-10, TSK-015): the growth of the leader and the change of the median price, told only in tens of percent and words
    /// ("about +40 %", 0.42 gives 40), the conditions of the notes, the choice of up to two a quarter with the rests, the snapshots of the market, and the letter.
    /// </summary>
    [Covers(typeof(MarketTrends), typeof(MarketStatsBook), typeof(NoteFacts), typeof(NoteEvaluator), typeof(NoteCooldowns), typeof(NoteSelection),
        typeof(NoteRenderer), typeof(NotesMailText), typeof(NotesMailContent))]
    public class MarketTrendsTests
    {
        private static readonly DateTime April = new DateTime(1996, 4, 1);

        private sealed class FixedRandom : IRandomSource
        {
            private readonly Queue<int> values;

            public FixedRandom(params int[] ranges)
            {
                values = new Queue<int>(ranges);
            }

            public float NextGaussian()
            {
                return 0f;
            }

            public int Range(int minInclusive, int maxExclusive)
            {
                int v = values.Count > 0 ? values.Dequeue() : minInclusive;
                return Math.Max(minInclusive, Math.Min(maxExclusive - 1, v));
            }
        }

        private static NoteTemplate Note(string id, NoteConditionType type, Segment segment = Segment.Any, float? min = null, float? max = null,
            float weight = 1f, int cooldown = 4, string textKey = "press.note.highEndFocus", params string[] args)
        {
            return new NoteTemplate
            {
                Id = id,
                Condition = new NoteCondition { Type = type, Segment = segment, Min = min, Max = max },
                TextKey = textKey,
                Args = new List<string>(args),
                CooldownQuarters = cooldown,
                Weight = weight,
            };
        }

        private static ReviewRecord Review(int id, string companyKey, string companyName, string published)
        {
            return new ReviewRecord { cpuSaveId = id, cpuName = "CPU" + id, companyKey = companyKey, companyName = companyName, publishedOn = published, segment = 1, metascore = 60 };
        }

        // ---- the numbers of the trends ----

        [Test]
        public void TheLeaderIs_TheHighestIpsOfTheLeadingCpus()
        {
            var leaders = new[]
            {
                new CpuSnapshot { Ips = 80f }, new CpuSnapshot { Ips = 150f }, new CpuSnapshot { Ips = 0f }, new CpuSnapshot { Ips = float.NaN },
            };

            Assert.Equal(150f, MarketTrends.LeaderIps(leaders));
            Assert.Equal(120f, MarketTrends.LeaderIps(new List<float> { 120f, 90f }));
            Assert.True(float.IsNaN(MarketTrends.LeaderIps(new CpuSnapshot[0])), "no leader, no number");
            Assert.True(float.IsNaN(MarketTrends.LeaderIps((IReadOnlyList<CpuSnapshot>)null)));
        }

        [Test]
        public void TheGrowthOfTheLeader_IsTheChangeOfItsIpsOverTheYear_AC101()
        {
            var ago = new[] { new CpuSnapshot { Ips = 100f }, new CpuSnapshot { Ips = 60f } };
            var now = new[] { new CpuSnapshot { Ips = 142f }, new CpuSnapshot { Ips = 90f } };

            Assert.Near(0.42, MarketTrends.LeaderIpsGrowth(now, ago), 1e-5);
            Assert.Near(-0.30, MarketTrends.LeaderIpsGrowth(ago, new[] { new CpuSnapshot { Ips = 142f } }), 0.01, "the leader got slower: a negative growth");
        }

        [Test]
        public void TheGrowth_IsUnknown_WhenAYearHasNoLeader()
        {
            var some = new[] { new CpuSnapshot { Ips = 100f } };

            Assert.True(float.IsNaN(MarketTrends.LeaderIpsGrowth(some, new CpuSnapshot[0])));
            Assert.True(float.IsNaN(MarketTrends.LeaderIpsGrowth(new CpuSnapshot[0], some)));
            Assert.True(float.IsNaN(MarketTrends.Change(10f, 0f)), "nothing to divide by");
            Assert.True(float.IsNaN(MarketTrends.Change(float.NaN, 10f)));
            Assert.True(float.IsNaN(MarketTrends.Change(-1f, 10f)));
        }

        [Test]
        public void TheMedianPrice_IsTheMiddleOfThePositivePrices()
        {
            Assert.Equal(200f, MarketTrends.Median(new List<float> { 100f, 900f, 200f }));
            Assert.Equal(150f, MarketTrends.Median(new List<float> { 100f, 200f }), "two prices: their mean");
            Assert.Equal(200f, MarketTrends.Median(new List<float> { 0f, -5f, 200f, float.NaN }), "a price of 0 or below, or NaN, is no price");
            Assert.True(float.IsNaN(MarketTrends.Median(new List<float>())));
            Assert.True(float.IsNaN(MarketTrends.Median(null)));
        }

        [Test]
        public void TheChangeOfTheMedianPrice_IsAFraction()
        {
            Assert.Near(-0.15, MarketTrends.MedianPriceChange(new List<float> { 85f, 85f, 85f }, new List<float> { 100f, 100f }), 1e-5);
            Assert.Near(0.25, MarketTrends.MedianPriceChange(new List<float> { 125f }, new List<float> { 90f, 100f, 110f }), 1e-5);
            Assert.True(float.IsNaN(MarketTrends.MedianPriceChange(new List<float>(), new List<float> { 100f })));
        }

        // ---- told to the player ----

        [Test]
        public void A42PercentGrowth_IsToldAs_About40Percent()
        {
            Assert.Equal(40, MarketTrends.RoundedPercent(0.42f));
            Assert.Equal("около +40 %", MarketTrends.Qualitative(0.42f, PressUiFixture.Text("ru")));
            Assert.Equal("about +40%", MarketTrends.Qualitative(0.42f, PressUiFixture.Text("en")));
        }

        [Test]
        public void ThePercentIsRoundedToTens_HalvesAwayFromZero()
        {
            Assert.Equal(30, MarketTrends.RoundedPercent(0.31f));
            Assert.Equal(50, MarketTrends.RoundedPercent(0.45f), "45 is half of the step: up");
            Assert.Equal(40, MarketTrends.RoundedPercent(0.44f));
            Assert.Equal(10, MarketTrends.RoundedPercent(0.05f));
            Assert.Equal(0, MarketTrends.RoundedPercent(0.04f));
            Assert.Equal(-10, MarketTrends.RoundedPercent(-0.13f));
            Assert.Equal(-50, MarketTrends.RoundedPercent(-0.45f));
            Assert.Equal(100, MarketTrends.RoundedPercent(0.96f));
            Assert.Equal(0, MarketTrends.RoundedPercent(float.NaN));
            Assert.Equal(0, MarketTrends.RoundedPercent(float.PositiveInfinity));
            Assert.Equal(100000, MarketTrends.RoundedPercent(1e9f), "a huge number does not overflow the rounding; it is held at 1000 times");
        }

        [Test]
        public void ANegativeChange_ShowsItsSign_AndZeroHasNone()
        {
            Assert.Equal("около -10 %", MarketTrends.Qualitative(-0.13f, PressUiFixture.Text("ru")));
            Assert.Equal("около 0 %", MarketTrends.Qualitative(0.01f, PressUiFixture.Text("ru")));
            Assert.Equal("about -10%", MarketTrends.Qualitative(-0.13f, PressUiFixture.Text("en")));
        }

        [Test]
        public void ThePriceChange_IsToldInAWord()
        {
            PressText ru = PressUiFixture.Text("ru");
            PressText en = PressUiFixture.Text("en");

            Assert.Equal("растут", MarketTrends.QualitativeChange(0.2f, ru));
            Assert.Equal("падают", MarketTrends.QualitativeChange(-0.12f, ru));
            Assert.Equal("почти не меняются", MarketTrends.QualitativeChange(0.03f, ru), "under 5 % is no change");
            Assert.Equal("почти не меняются", MarketTrends.QualitativeChange(float.NaN, ru));
            Assert.Equal("falling", MarketTrends.QualitativeChange(-0.2f, en));
            Assert.Equal("rising", MarketTrends.QualitativeChange(0.2f, en));
        }

        // ---- the conditions ----

        private static NoteFacts Facts(Action<NoteFacts> fill = null)
        {
            var facts = new NoteFacts();
            if (fill != null) fill(facts);
            return facts;
        }

        [Test]
        public void TheLeaderGrowthCondition_NeedsTheSegmentsValueToBeKnownAndInRange()
        {
            NoteCondition c = Note("x", NoteConditionType.LeaderGrowth, Segment.HighEnd, min: 0.25f).Condition;

            Assert.True(NoteEvaluator.Holds(c, Facts(f => f.LeaderGrowth[0] = 0.42f)));
            Assert.True(NoteEvaluator.Holds(c, Facts(f => f.LeaderGrowth[0] = 0.25f)), "the bounds are inclusive");
            Assert.False(NoteEvaluator.Holds(c, Facts(f => f.LeaderGrowth[0] = 0.24f)));
            Assert.False(NoteEvaluator.Holds(c, Facts(f => f.LeaderGrowth[1] = 0.9f)), "another segment");
            Assert.False(NoteEvaluator.Holds(c, Facts()), "unknown is not enough");
        }

        [Test]
        public void ThePriceCondition_ReadsTheBoundsThatTheFileHas()
        {
            NoteCondition falling = Note("x", NoteConditionType.MedianPriceChange, Segment.LowEnd, max: -0.10f).Condition;
            NoteCondition rising = Note("y", NoteConditionType.MedianPriceChange, Segment.HighEnd, min: 0.15f).Condition;
            NoteCondition band = Note("z", NoteConditionType.MedianPriceChange, Segment.MidRange, min: -0.05f, max: 0.05f).Condition;

            Assert.True(NoteEvaluator.Holds(falling, Facts(f => f.MedianPriceChange[2] = -0.10f)));
            Assert.False(NoteEvaluator.Holds(falling, Facts(f => f.MedianPriceChange[2] = -0.09f)));
            Assert.True(NoteEvaluator.Holds(rising, Facts(f => f.MedianPriceChange[0] = 0.2f)));
            Assert.False(NoteEvaluator.Holds(rising, Facts(f => f.MedianPriceChange[0] = 0.1f)));
            Assert.True(NoteEvaluator.Holds(band, Facts(f => f.MedianPriceChange[1] = 0f)));
            Assert.False(NoteEvaluator.Holds(band, Facts(f => f.MedianPriceChange[1] = 0.06f)));
        }

        [Test]
        public void TheOtherConditions_PgaReputationEntrantAndFocus()
        {
            Assert.True(NoteEvaluator.Holds(Note("p", NoteConditionType.PgaShift).Condition, Facts(f => f.PgaShift = true)));
            Assert.False(NoteEvaluator.Holds(Note("p", NoteConditionType.PgaShift).Condition, Facts()));

            NoteCondition up = Note("u", NoteConditionType.ReputationTrend, min: 8f).Condition;
            NoteCondition down = Note("d", NoteConditionType.ReputationTrend, max: -8f).Condition;
            Assert.True(NoteEvaluator.Holds(up, Facts(f => f.ReputationChange = 9f)));
            Assert.False(NoteEvaluator.Holds(up, Facts(f => f.ReputationChange = 7f)));
            Assert.True(NoteEvaluator.Holds(down, Facts(f => f.ReputationChange = -8f)));
            Assert.False(NoteEvaluator.Holds(down, Facts()), "a player without a history has no trend");

            NoteCondition entrant = Note("e", NoteConditionType.NewEntrant).Condition;
            Assert.True(NoteEvaluator.Holds(entrant, Facts(f => f.NewEntrantName = "Cyrix")));
            Assert.False(NoteEvaluator.Holds(entrant, Facts()));

            Assert.True(NoteEvaluator.Holds(Note("f", NoteConditionType.SegmentFocus, Segment.HighEnd).Condition, Facts()), "a static note always holds");
        }

        [Test]
        public void ASegmentConditionWithoutASegment_NeverHolds()
        {
            NoteCondition c = Note("x", NoteConditionType.LeaderGrowth, Segment.Any, min: 0f).Condition;
            Assert.False(NoteEvaluator.Holds(c, Facts(f => { f.LeaderGrowth[0] = 1f; f.LeaderGrowth[1] = 1f; })));
        }

        // ---- the rests ----

        [Test]
        public void ANoteRests_UntilTheDayOfItsEntry_AndMayComeAgainOnThatDay()
        {
            var list = new List<NoteCooldown>();
            NoteCooldowns.Rest(list, "perf", NoteCooldowns.UntilOf(April, 4));

            Assert.Equal("1997-04-01", list[0].until, "4 quarters later");
            Assert.True(NoteCooldowns.IsResting(list, "perf", new DateTime(1996, 7, 1)));
            Assert.True(NoteCooldowns.IsResting(list, "perf", new DateTime(1997, 1, 1)));
            Assert.False(NoteCooldowns.IsResting(list, "perf", new DateTime(1997, 4, 1)), "the day itself it may be chosen");
            Assert.False(NoteCooldowns.IsResting(list, "other", April));
            Assert.False(NoteCooldowns.IsResting(null, "perf", April));
        }

        [Test]
        public void TheRestOfANote_IsOneEntryPerNote_AndAQuarterIsThreeMonths()
        {
            var list = new List<NoteCooldown>();
            NoteCooldowns.Rest(list, "a", NoteCooldowns.UntilOf(April, 1));
            NoteCooldowns.Rest(list, "a", NoteCooldowns.UntilOf(April, 2));

            Assert.Equal(1, list.Count);
            Assert.Equal("1996-10-01", list[0].until);
            Assert.Equal(new DateTime(1996, 7, 1), NoteCooldowns.UntilOf(April, 1));
            Assert.Equal(new DateTime(1996, 7, 1), NoteCooldowns.UntilOf(April, 0), "at least one quarter");
        }

        [Test]
        public void TheListOfRests_ForgetsTheOnesThatAreOver_AndKeepsItsLimit()
        {
            var list = new List<NoteCooldown>
            {
                new NoteCooldown { noteId = "over", until = "1996-01-01" },
                new NoteCooldown { noteId = "today", until = "1996-04-01" },
                new NoteCooldown { noteId = "later", until = "1997-01-01" },
                new NoteCooldown { noteId = "broken", until = "soon" },
            };

            Assert.Equal(3, NoteCooldowns.Prune(list, April), "over, today (it may come again) and the broken one go");
            Assert.Equal(1, list.Count);
            Assert.Equal("later", list[0].noteId);

            var many = new List<NoteCooldown>();
            for (int i = 0; i < PressStateCodec.MaxNoteCooldowns + 20; i++) NoteCooldowns.Rest(many, "n" + i, April.AddYears(5));
            Assert.Equal(PressStateCodec.MaxNoteCooldowns, many.Count);
            Assert.Equal("n20", many[0].noteId, "the oldest go first");
        }

        // ---- the choice ----

        [Test]
        public void TheMonthsOfTheNotes_AreJanuaryAprilJulyAndOctober()
        {
            for (int month = 1; month <= 12; month++)
                Assert.Equal(month == 1 || month == 4 || month == 7 || month == 10, NoteSelection.IsQuarterMonth(month), "month " + month);
        }

        [Test]
        public void AtMostTwoNotesComeInAQuarter_OfThoseWhoseConditionHolds()
        {
            var notes = new List<NoteTemplate>
            {
                Note("a", NoteConditionType.SegmentFocus), Note("b", NoteConditionType.SegmentFocus), Note("c", NoteConditionType.SegmentFocus),
                Note("d", NoteConditionType.PgaShift),
            };

            List<NoteTemplate> picked = NoteSelection.Pick(notes, Facts(), new List<NoteCooldown>(), April, NoteSelection.MaxPerQuarter, new FixedRandom());

            Assert.Equal(2, picked.Count, "three notes hold, two are chosen");
            Assert.False(picked.Exists(n => n.Id == "d"), "the one whose condition does not hold never");
        }

        [Test]
        public void NoNote_WhenNoConditionHolds()
        {
            var notes = new List<NoteTemplate> { Note("p", NoteConditionType.PgaShift), Note("e", NoteConditionType.NewEntrant) };

            Assert.Equal(0, NoteSelection.Pick(notes, Facts(), null, April, 2, new FixedRandom()).Count);
            Assert.Equal(0, NoteSelection.Pick(new List<NoteTemplate>(), Facts(), null, April, 2, new FixedRandom()).Count);
            Assert.Equal(0, NoteSelection.Pick(null, Facts(), null, April, 2, new FixedRandom()).Count);
        }

        [Test]
        public void ANoteThatRests_IsNotChosen_AC103()
        {
            var notes = new List<NoteTemplate> { Note("a", NoteConditionType.SegmentFocus), Note("b", NoteConditionType.SegmentFocus) };
            var cooldowns = new List<NoteCooldown> { new NoteCooldown { noteId = "a", until = "1996-07-01" } };

            List<NoteTemplate> picked = NoteSelection.Pick(notes, Facts(), cooldowns, April, 2, new FixedRandom());

            Assert.Equal(1, picked.Count);
            Assert.Equal("b", picked[0].Id);
            Assert.Equal(2, NoteSelection.Pick(notes, Facts(), cooldowns, new DateTime(1996, 7, 1), 2, new FixedRandom()).Count, "on the day the rest ends it is back");
        }

        [Test]
        public void TheHeavierANote_TheLikelierItComesFirst()
        {
            var notes = new List<NoteTemplate>
            {
                Note("light", NoteConditionType.SegmentFocus, weight: 1f), Note("heavy", NoteConditionType.SegmentFocus, weight: 3f),
            };

            // the dice give the fraction of the total weight: 0 … 0.25 is the light note, the rest the heavy one
            Assert.Equal("light", NoteSelection.Pick(notes, Facts(), null, April, 1, new FixedRandom(100000))[0].Id, "0.1 of the weight");
            Assert.Equal("heavy", NoteSelection.Pick(notes, Facts(), null, April, 1, new FixedRandom(300000))[0].Id, "0.3 of the weight");
            Assert.Equal("heavy", NoteSelection.Pick(notes, Facts(), null, April, 1, new FixedRandom(999999))[0].Id);
        }

        [Test]
        public void ANoteWithoutWeight_IsNeverChosen()
        {
            var notes = new List<NoteTemplate> { Note("none", NoteConditionType.SegmentFocus, weight: 0f), Note("neg", NoteConditionType.SegmentFocus, weight: -2f) };

            Assert.Equal(0, NoteSelection.Pick(notes, Facts(), null, April, 2, new FixedRandom()).Count);
        }

        [Test]
        public void TheSameDice_GiveTheSameNotes_InTheSameOrder()
        {
            var notes = new List<NoteTemplate>();
            for (int i = 0; i < 6; i++) notes.Add(Note("n" + i, NoteConditionType.SegmentFocus, weight: 1f + i));

            string a = string.Join(",", NoteSelection.Pick(notes, Facts(), null, April, 2, new FixedRandom(123456, 654321)).ConvertAll(n => n.Id).ToArray());
            string b = string.Join(",", NoteSelection.Pick(notes, Facts(), null, April, 2, new FixedRandom(123456, 654321)).ConvertAll(n => n.Id).ToArray());

            Assert.Equal(a, b);
        }

        [Test]
        public void TheChoice_NeedsDice()
        {
            Assert.Throws<ArgumentNullException>(() => NoteSelection.Pick(new List<NoteTemplate>(), Facts(), null, April, 2, null));
        }

        // ---- the text of a note ----

        [Test]
        public void TheGrowthOfTheLeaderBecomesTheNoteOfTheRequirement_AC101()
        {
            List<NoteTemplate> notes = new List<NoteTemplate>(PressContentFixture.LoadNotes().Records);
            NoteTemplate perf = notes.Find(n => n.Id == "perf_bar_highend");
            NoteFacts facts = Facts(f => f.LeaderGrowth[0] = 0.42f);

            Assert.True(NoteEvaluator.Holds(perf.Condition, facts), "the leader of High End grew by 42 %");
            Assert.Equal("Покупатели High End ждут прироста производительности около +40 % в год.", NoteRenderer.Render(perf, facts, PressUiFixture.Text("ru")));
            Assert.Equal("High End buyers now expect about +40% more performance per year.", NoteRenderer.Render(perf, facts, PressUiFixture.Text("en")));
        }

        [Test]
        public void ThePriceNote_SaysFallingOrRising()
        {
            List<NoteTemplate> notes = new List<NoteTemplate>(PressContentFixture.LoadNotes().Records);
            NoteTemplate pressure = notes.Find(n => n.Id == "price_pressure_low");
            NoteFacts facts = Facts(f => f.MedianPriceChange[2] = -0.2f);

            Assert.True(NoteEvaluator.Holds(pressure.Condition, facts));
            Assert.Equal("Цены в Low End падают: покупатели считают каждый доллар.", NoteRenderer.Render(pressure, facts, PressUiFixture.Text("ru")));
            Assert.Equal("Prices in Low End are falling: buyers count every dollar.", NoteRenderer.Render(pressure, facts, PressUiFixture.Text("en")));
        }

        [Test]
        public void TheCompanyNotes_NameThePlayerOrTheNewcomer()
        {
            List<NoteTemplate> notes = new List<NoteTemplate>(PressContentFixture.LoadNotes().Records);
            NoteFacts facts = Facts(f => { f.PlayerCompanyName = "Intelligent"; f.NewEntrantName = "Cyrix"; f.ReputationChange = 9f; });

            Assert.Equal("Репутация Intelligent растёт: пресса всё чаще отзывается о компании доброжелательно.",
                NoteRenderer.Render(notes.Find(n => n.Id == "player_rep_up"), facts, PressUiFixture.Text("ru")));
            Assert.Equal("На рынке новое имя: компания Cyrix выпустила свой первый процессор.",
                NoteRenderer.Render(notes.Find(n => n.Id == "new_entrant"), facts, PressUiFixture.Text("ru")));
        }

        [Test]
        public void ACompanyNameWithATag_IsMadeSafe()
        {
            NoteTemplate note = new List<NoteTemplate>(PressContentFixture.LoadNotes().Records).Find(n => n.Id == "new_entrant");
            string text = NoteRenderer.Render(note, Facts(f => f.NewEntrantName = "<b>Evil</b> Corp"), PressUiFixture.Text("en"));

            Assert.False(text.Contains("<"), text);
            Assert.True(text.Contains("bEvil/b Corp"), text);
        }

        [Test]
        public void EveryNoteOfTheContent_ReadsWellInBothLanguages_WithNoCoefficientInIt()
        {
            NoteFacts facts = Facts(f =>
            {
                for (int s = 0; s < 4; s++) { f.LeaderGrowth[s] = 0.4237f; f.MedianPriceChange[s] = -0.1234f; }
                f.PlayerCompanyName = "Intelligent";
                f.NewEntrantName = "Cyrix";
            });
            var decimals = new Regex(@"\d[.,]\d");

            foreach (string language in new[] { "ru", "en" })
            {
                PressText text = PressUiFixture.Text(language);
                foreach (NoteTemplate note in PressContentFixture.LoadNotes().Records)
                {
                    string line = NoteRenderer.Render(note, facts, text);
                    Assert.True(line.Length > 10, language + " " + note.Id + ": " + line);
                    Assert.False(line.Contains("{") || line.Contains("}"), language + " " + note.Id + " has an argument left open: " + line);
                    Assert.False(decimals.IsMatch(line), language + " " + note.Id + " shows a number with decimals: " + line);
                    Assert.False(line.Contains("0.4237") || line.Contains("42"), language + " " + note.Id + " gives the raw growth away: " + line);
                }
            }
        }

        [Test]
        public void EveryNoteOfTheContent_HasAConditionThatCanHoldOnSomeFacts()
        {
            // a smoke test of the whole pipeline over the built-in notes: facts that make every condition true choose notes, and only the notes asked for
            NoteFacts facts = Facts(f =>
            {
                for (int s = 0; s < 4; s++) { f.LeaderGrowth[s] = 0.5f; f.MedianPriceChange[s] = 0.5f; }
                f.MedianPriceChange[2] = -0.5f;
                f.MedianPriceChange[1] = -0.5f;
                f.PgaShift = true;
                f.ReputationChange = 10f;
                f.PlayerCompanyName = "Me";
                f.NewEntrantName = "Cyrix";
            });
            List<NoteTemplate> candidates = NoteSelection.Candidates(new List<NoteTemplate>(PressContentFixture.LoadNotes().Records), facts, null, April);

            Assert.True(candidates.Count >= 9, "found " + candidates.Count);
            Assert.True(candidates.Exists(n => n.Id == "pga_shift"));
            Assert.False(candidates.Exists(n => n.Id == "player_rep_down"), "the reputation is not falling");
        }

        // ---- the snapshots of the market ----

        [Test]
        public void ASnapshot_IsWritten_AndAnotherOfTheSameYearAndSegmentReplacesIt()
        {
            var stats = new List<SegmentYearStat>();
            MarketStatsBook.Upsert(stats, 1995, 0, 150f, 549f);
            MarketStatsBook.Upsert(stats, 1995, 0, 160f, 500f);

            Assert.Equal(1, stats.Count);
            Assert.Equal(160f, MarketStatsBook.Find(stats, 1995, 0).leaderIps);
            Assert.Equal(500f, MarketStatsBook.Find(stats, 1995, 0).medianPrice);
            Assert.Null(MarketStatsBook.Find(stats, 1995, 1));
            Assert.Null(MarketStatsBook.Find(stats, 1994, 0));
            Assert.True(MarketStatsBook.HasYear(stats, 1995));
            Assert.False(MarketStatsBook.HasYear(stats, 1994));
        }

        [Test]
        public void ASegmentWithNeitherNumber_IsNotStored_AndAnUnknownOneIsStoredAsZero()
        {
            var stats = new List<SegmentYearStat>();
            MarketStatsBook.Upsert(stats, 1995, 0, float.NaN, float.NaN);
            MarketStatsBook.Upsert(stats, 1995, 9, 100f, 100f);
            Assert.Equal(0, stats.Count);

            MarketStatsBook.Upsert(stats, 1995, 1, 120f, float.NaN);
            Assert.Equal(120f, stats[0].leaderIps);
            Assert.Equal(0f, stats[0].medianPrice);
        }

        [Test]
        public void TheSnapshots_KeepTheLastThreeYears()
        {
            var stats = new List<SegmentYearStat>();
            for (int year = 1990; year <= 1994; year++)
            {
                for (int segment = 0; segment < 4; segment++) MarketStatsBook.Upsert(stats, year, segment, 100f + year, 300f);
            }

            Assert.Equal(PressStateCodec.MaxYearStats, stats.Count);
            Assert.False(MarketStatsBook.HasYear(stats, 1991), "the oldest years are gone");
            Assert.True(MarketStatsBook.HasYear(stats, 1992));
            Assert.True(MarketStatsBook.HasYear(stats, 1994));
        }

        [Test]
        public void TheFacts_ComeFromTwoConsecutiveYearsOfSnapshots()
        {
            var stats = new List<SegmentYearStat>();
            MarketStatsBook.Upsert(stats, 1994, 0, 100f, 500f);
            MarketStatsBook.Upsert(stats, 1995, 0, 142f, 425f);
            MarketStatsBook.Upsert(stats, 1995, 1, 90f, 200f);          // no 1994 snapshot of this segment
            var facts = new NoteFacts();

            MarketStatsBook.FillFacts(stats, 1995, facts);

            Assert.Near(0.42, facts.LeaderGrowth[0], 1e-5);
            Assert.Near(-0.15, facts.MedianPriceChange[0], 1e-5);
            Assert.True(float.IsNaN(facts.LeaderGrowth[1]), "one year of a segment gives no growth");
            Assert.True(float.IsNaN(facts.LeaderGrowth[2]) && float.IsNaN(facts.MedianPriceChange[3]));
        }

        [Test]
        public void NoSnapshots_NoFacts()
        {
            var facts = new NoteFacts();
            MarketStatsBook.FillFacts(null, 1995, facts);

            for (int s = 0; s < 4; s++) Assert.True(float.IsNaN(facts.LeaderGrowth[s]) && float.IsNaN(facts.MedianPriceChange[s]));
            Assert.Throws<ArgumentNullException>(() => MarketStatsBook.FillFacts(null, 1995, null));
        }

        // ---- a new company ----

        [Test]
        public void ACompany_WithItsFirstReviewInTheLastQuarter_IsNew()
        {
            var reviews = new List<ReviewRecord>
            {
                Review(1, "ai:0", "Intel", "1994-01-10"), Review(2, "ai:0", "Intel", "1996-02-10"),
                Review(3, "ai:1", "Cyrix", "1996-02-20"),
                Review(4, "ai:2", "AMD", "1994-06-01"), Review(5, "ai:2", "AMD", "1996-03-01"),
            };

            Assert.Equal("Cyrix", MarketStatsBook.FindNewEntrant(reviews, April.AddMonths(-3), April, "player"));
        }

        [Test]
        public void TheFirstReview_NotTheLatest_DecidesWhoIsNew()
        {
            var reviews = new List<ReviewRecord> { Review(1, "ai:0", "Intel", "1993-01-10"), Review(2, "ai:1", "Cyrix", "1995-05-01"), Review(3, "ai:1", "Cyrix", "1996-02-20") };

            Assert.Null(MarketStatsBook.FindNewEntrant(reviews, April.AddMonths(-3), April, "player"), "Cyrix had a review last year");
        }

        [Test]
        public void ThePlayersCompany_IsNeverNew()
        {
            var reviews = new List<ReviewRecord> { Review(1, "ai:0", "Intel", "1993-01-10"), Review(2, "player", "Me", "1996-02-20") };

            Assert.Null(MarketStatsBook.FindNewEntrant(reviews, April.AddMonths(-3), April, "player"));
        }

        [Test]
        public void NobodyIsNew_UntilThePressHasWatchedTheMarketForAYear()
        {
            // at the start of a game every company has no review yet: they would all be "new" in the first quarters
            var reviews = new List<ReviewRecord> { Review(1, "ai:0", "Intel", "1995-12-10"), Review(2, "ai:1", "Cyrix", "1996-02-20") };

            Assert.Null(MarketStatsBook.FindNewEntrant(reviews, April.AddMonths(-3), April, "player"));
            Assert.Null(MarketStatsBook.FindNewEntrant(new List<ReviewRecord>(), April.AddMonths(-3), April, "player"));
            Assert.Null(MarketStatsBook.FindNewEntrant(null, April.AddMonths(-3), April, "player"));
        }

        [Test]
        public void SeveralNewCompanies_TheEarliestIsNamed()
        {
            var reviews = new List<ReviewRecord>
            {
                Review(1, "ai:0", "Intel", "1993-01-10"),
                Review(2, "ai:2", "Late", "1996-03-20"), Review(3, "ai:1", "Early", "1996-01-05"),
            };

            Assert.Equal("Early", MarketStatsBook.FindNewEntrant(reviews, April.AddMonths(-3), April, "player"));
        }

        // ---- the letter ----

        [Test]
        public void TheLetter_HasTheTagOfTheMonth_AndOneParagraphANote()
        {
            NotesMailContent content = NotesMailText.Build(April, new[] { "First note.", "", "  ", "Second note." });

            Assert.Equal("press:notes:1996-04", content.Tag);
            Assert.Equal(1, content.Args.Length);
            Assert.Equal("First note.\n\nSecond note.", content.Args[0]);
            Assert.Equal("press:notes:1996-04", NotesMailText.TagOf(new DateTime(1996, 4, 17)));
        }

        [Test]
        public void ALetterWithoutNotes_HasNothingToSay()
        {
            Assert.Equal("", NotesMailText.Build(April, new string[0]).Args[0]);
            Assert.Equal("", NotesMailText.Build(April, null).Args[0]);
        }

        [Test]
        public void TheStringsOfTheLetter_AreInBothLanguages()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                PressText text = PressUiFixture.Text(language);
                Assert.True(text("press.mail.notes.subject").Length > 0, language + " subject");
                Assert.Equal("a note", text("press.mail.notes.body", "a note"), language + " body");
            }
        }
    }
}
