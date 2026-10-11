using System.Collections.Generic;
using System.Linq;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Json;
using PTMod.Features.EventSystem;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// TSK-002: the <c>config.json</c> of every pack (categories, <c>channelLimits</c>, <c>maxPendingChoices</c>, <c>reminderDaysBefore</c>, <c>logLimit</c>) and the
    /// way the configs of several packs are merged: categories are united, the limits of the packs narrow each other (D8), the most specific entry of a pack
    /// counts inside the pack.
    /// </summary>
    [Covers(typeof(PackConfigReader), typeof(PackConfig), typeof(EventConfig), typeof(CategoryDefinition), typeof(ChannelLimit))]
    public class EventConfigTests
    {
        private static PackConfig Read(string pack, string json, List<string> problems = null)
        {
            return PackConfigReader.Read(pack, EventTestKit.Obj(json), problems ?? new List<string>());
        }

        private static EventConfig Merge(params PackConfig[] packs)
        {
            return EventConfig.Merge(packs, null);
        }

        private static void AssertLimits(EventConfig config, string channel, float min, float max)
        {
            Assert.Near(min, config.LimitMin(channel), 1e-6, "min of " + channel);
            Assert.Near(max, config.LimitMax(channel), 1e-6, "max of " + channel);
        }

        // ---- the shipped file ----

        [Test]
        public void TheShippedConfig_IsTheOneOfTheDesign()
        {
            var problems = new List<string>();
            ConfigLoad load = ContentMerger.MergeConfig("event-system", "config", EventTestKit.ShippedContent("event-system", "config.json"), null, PackConfigReader.Schema);
            Assert.Equal(0, load.Report.Errors.Count, EventTestKit.Join(load.Report.Errors));

            PackConfig pack = PackConfigReader.Read("event-system", load.Config, problems);
            Assert.Equal(0, problems.Count, EventTestKit.Join(problems));

            EventConfig config = Merge(pack);
            Assert.Equal(1, config.Categories.Count);
            CategoryDefinition universal = config.Category("universal");
            Assert.NotNull(universal);
            Assert.Near(0.011, universal.DailyChance, 1e-7);
            Assert.Equal(45, universal.MinGapDays);
            Assert.Equal(2, universal.MaxActive);
            Assert.Equal("event-system", universal.PackId);

            Assert.Equal(2, config.MaxPendingChoices);
            Assert.Equal(3, config.ReminderDaysBefore);
            Assert.Equal(400, config.LogLimit);

            // FR-5 item 6: [0.25; 4.0] by default, production.output [0.1; 3.0]; design.md: popularity.gain [0.7; 1.6], market.demand.* [0.4; 2.5].
            AssertLimits(config, "research.speed", 0.25f, 4f);
            AssertLimits(config, "production.output", 0.1f, 3f);
            AssertLimits(config, "market.demand.highend", 0.4f, 2.5f);
            AssertLimits(config, "market.demand.industries", 0.4f, 2.5f);
            AssertLimits(config, "popularity.gain", 0.7f, 1.6f);
            AssertLimits(config, "market.rating.cpu", 0.25f, 4f);
        }

        [Test]
        public void TheShippedConfig_AndThePackOfMarketShocks_NarrowEachOther()
        {
            var problems = new List<string>();
            PackConfig engine = PackConfigReader.Read("event-system",
                ContentMerger.MergeConfig("event-system", "config", EventTestKit.ShippedContent("event-system", "config.json"), null, 1).Config, problems);
            PackConfig shocks = PackConfigReader.Read("market-shocks",
                ContentMerger.MergeConfig("market-shocks", "config", EventTestKit.Fixture(EventTestKit.MarketShocksConfig), null, 1).Config, problems);
            Assert.Equal(0, problems.Count, EventTestKit.Join(problems));

            EventConfig config = Merge(engine, shocks);

            AssertLimits(config, "production.output", 0.4f, 1.5f);          // [0.1; 3.0] with [0.4; 1.5]: market-shocks narrows it (D10: Хаос x2 gives 0.4)
            AssertLimits(config, "market.demand.lowend", 0.6f, 1.6f);       // [0.4; 2.5] with [0.6; 1.6]
            AssertLimits(config, "production.memoryCost", 0.5f, 2f);        // only the pack of market-shocks names it, the engine's '*' [0.25; 4] is wider
            AssertLimits(config, "popularity.gain", 0.7f, 1.6f);            // market-shocks says nothing: the engine's own
            AssertLimits(config, "research.speed", 0.25f, 4f);

            Assert.True(config.HasCategory("universal"));
            Assert.True(config.HasCategory("ms.supply"));
            Assert.True(config.HasCategory("ms.industry"));
            Assert.Equal(3, config.Categories.Count);
        }

        // ---- reading a file ----

        [Test]
        public void ACategory_HasItsThreeNumbers_AndTheDefaultsOfAMissingOne()
        {
            PackConfig pack = Read("p", "{ 'categories': [ { 'id': 'a', 'dailyChance': 0.5, 'minGapDays': 10, 'maxActive': 3 }, { 'id': 'b' } ] }");

            Assert.Equal(2, pack.Categories.Count);
            Assert.Equal(0.5f, pack.Categories[0].DailyChance);
            Assert.Equal(10, pack.Categories[0].MinGapDays);
            Assert.Equal(3, pack.Categories[0].MaxActive);
            Assert.Equal(0f, pack.Categories[1].DailyChance, "no lottery");
            Assert.Equal(0, pack.Categories[1].MinGapDays);
            Assert.Equal(PackConfigReader.NoLimit, pack.Categories[1].MaxActive);
        }

        [Test]
        public void ABadCategory_IsSkippedAlone_WithAReason()
        {
            var problems = new List<string>();
            PackConfig pack = Read("p", @"{ 'categories': [
                { 'id': 'ok', 'dailyChance': 0.1 },
                { 'dailyChance': 0.1 },
                { 'id': 'chance', 'dailyChance': 1.5 },
                { 'id': 'gap', 'minGapDays': -1 },
                { 'id': 'active', 'maxActive': 0 },
                { 'id': 'two words' },
                { 'id': 'ok', 'dailyChance': 0.2 },
                'text',
                { 'id': 'last', 'dailyChance': 0.3 } ] }", problems);

            Assert.Equal("ok,last", string.Join(",", pack.Categories.Select(c => c.Id)));
            Assert.Equal(0.1f, pack.Categories[0].DailyChance, "the first definition stays");
            Assert.Equal(7, problems.Count(p => p.StartsWith("skip category: ")), EventTestKit.Join(problems));
            Assert.True(EventTestKit.AnyContains(problems, "duplicate category 'ok'"), EventTestKit.Join(problems));
            Assert.True(EventTestKit.AnyContains(problems, "(categories[2].dailyChance)"), EventTestKit.Join(problems));
        }

        [Test]
        public void AChannelLimit_NeedsAPatternAMinimumAndAMaximum()
        {
            var problems = new List<string>();
            PackConfig pack = Read("p", @"{ 'channelLimits': [
                { 'channel': '*', 'min': 0.25, 'max': 4 },
                { 'channel': 'production.output', 'min': 0.1, 'max': 3 },
                { 'channel': 'market.demand.*', 'min': 0.4, 'max': 2.5 },
                { 'min': 0.5, 'max': 1 },
                { 'channel': 'a.b', 'max': 1 },
                { 'channel': 'a.b', 'min': 0.5 },
                { 'channel': 'a.b', 'min': 2, 'max': 1 },
                { 'channel': 'bad name', 'min': 1, 'max': 2 },
                { 'channel': 'a.*.b', 'min': 1, 'max': 2 },
                { 'channel': 'production.output', 'min': 0.2, 'max': 2 },
                { 'channel': 'negative', 'min': -1, 'max': 2 },
                7 ] }", problems);

            Assert.Equal("*,production.output,market.demand.*", string.Join(",", pack.ChannelLimits.Select(l => l.Pattern)));
            for (int entry = 3; entry <= 11; entry++)
            {
                Assert.True(EventTestKit.AnyContains(problems, "(channelLimits[" + entry + "]"), "entry " + entry + " is skipped with a reason: " + EventTestKit.Join(problems));
            }
            Assert.True(problems.All(p => p.StartsWith("skip channel limit: ")), EventTestKit.Join(problems));
            Assert.True(EventTestKit.AnyContains(problems, "'min' (2) must not exceed 'max' (1)"), EventTestKit.Join(problems));
            Assert.True(EventTestKit.AnyContains(problems, "duplicate limit for 'production.output'"), EventTestKit.Join(problems));
            Assert.True(EventTestKit.AnyContains(problems, "channel pattern 'a.*.b'"), EventTestKit.Join(problems));
        }

        [Test]
        public void TheScalars_AreChecked_AndABadOneKeepsTheDefault()
        {
            var problems = new List<string>();
            PackConfig good = Read("p", "{ 'maxPendingChoices': 3, 'reminderDaysBefore': 0, 'logLimit': 100 }", problems);
            Assert.Equal(3, good.MaxPendingChoices);
            Assert.Equal(0, good.ReminderDaysBefore);
            Assert.Equal(100, good.LogLimit);
            Assert.Equal(0, problems.Count, EventTestKit.Join(problems));

            PackConfig bad = Read("p", "{ 'maxPendingChoices': 0, 'reminderDaysBefore': 'soon', 'logLimit': 5 }", problems);
            Assert.Equal(-1, bad.MaxPendingChoices, "not set");
            Assert.Equal(-1, bad.ReminderDaysBefore);
            Assert.Equal(-1, bad.LogLimit);
            Assert.Equal(3, problems.Count, EventTestKit.Join(problems));

            PackConfig empty = Read("p", "{ }");
            Assert.Equal(-1, empty.MaxPendingChoices);
        }

        [Test]
        public void AnUnknownKey_IsAWarning_AndSchemaIsNot()
        {
            var problems = new List<string>();
            Read("p", "{ 'schema': 1, 'maxPending': 5, '_note': 'x' }", problems);
            Assert.Equal(1, problems.Count, EventTestKit.Join(problems));
            Assert.True(problems[0].Contains("unknown field 'maxPending'"), problems[0]);
        }

        [Test]
        public void ANullFile_IsAnEmptyConfig()
        {
            var problems = new List<string>();
            PackConfig pack = PackConfigReader.Read("p", null, problems);
            Assert.Equal("p", pack.PackId);
            Assert.Equal(0, pack.Categories.Count);
            Assert.Equal(0, pack.ChannelLimits.Count);
            Assert.Equal(0, problems.Count);
        }

        [Test]
        public void IsPattern_AcceptsStarANameAndAFamily()
        {
            Assert.True(PackConfigReader.IsPattern("*"));
            Assert.True(PackConfigReader.IsPattern("production.output"));
            Assert.True(PackConfigReader.IsPattern("market.demand.*"));
            Assert.False(PackConfigReader.IsPattern(""));
            Assert.False(PackConfigReader.IsPattern("market.*.demand"));
            Assert.False(PackConfigReader.IsPattern("**"));
            Assert.False(PackConfigReader.IsPattern(".*"));
            Assert.False(PackConfigReader.IsPattern("a b"));
        }

        // ---- merging ----

        [Test]
        public void WithNoPacks_TheDefaultsApply()
        {
            EventConfig config = EventConfig.Merge(new PackConfig[0], null);
            Assert.Equal(0, config.Categories.Count);
            Assert.Equal(EventConfig.DefaultMaxPendingChoices, config.MaxPendingChoices);
            Assert.Equal(EventConfig.DefaultReminderDaysBefore, config.ReminderDaysBefore);
            Assert.Equal(EventConfig.DefaultLogLimit, config.LogLimit);
            AssertLimits(config, "anything.at.all", EventConfig.DefaultLimitMin, EventConfig.DefaultLimitMax);
            Assert.Null(config.Category("universal"));
            Assert.False(config.HasCategory(null));

            Assert.Equal(2, EventConfig.Defaults.MaxPendingChoices);
            Assert.Equal(0, EventConfig.Merge(null, null).Categories.Count);
        }

        [Test]
        public void TheCategories_OfAllPacks_AreUnited_InTheOrderOfThePackIds()
        {
            PackConfig b = Read("b-pack", "{ 'categories': [ { 'id': 'b1' }, { 'id': 'b2' } ] }");
            PackConfig a = Read("a-pack", "{ 'categories': [ { 'id': 'a1' } ] }");

            EventConfig config = Merge(b, a);   // registered in the other order

            Assert.Equal("a1,b1,b2", string.Join(",", config.Categories.Select(c => c.Id)));
            Assert.Equal("a-pack", config.Category("a1").PackId);
            Assert.Equal("b-pack", config.Category("b2").PackId);
        }

        [Test]
        public void ACategoryOfTwoPacks_KeepsTheFirstByPackId_AndWarns()
        {
            PackConfig first = Read("a-pack", "{ 'categories': [ { 'id': 'shared', 'dailyChance': 0.1 } ] }");
            PackConfig second = Read("b-pack", "{ 'categories': [ { 'id': 'shared', 'dailyChance': 0.9 } ] }");
            var warnings = new List<string>();

            EventConfig config = EventConfig.Merge(new[] { second, first }, warnings.Add);

            Assert.Equal(1, config.Categories.Count);
            Assert.Equal(0.1f, config.Category("shared").DailyChance);
            Assert.Equal(1, warnings.Count);
            Assert.Equal("category 'shared' of pack 'b-pack' is ignored: pack 'a-pack' already defines it", warnings[0]);
        }

        [Test]
        public void Limits_InsideOnePack_TheMostSpecificEntryCounts()
        {
            PackConfig pack = Read("p", @"{ 'channelLimits': [
                { 'channel': '*', 'min': 0.25, 'max': 4 },
                { 'channel': 'a.*', 'min': 0.3, 'max': 3 },
                { 'channel': 'a.b.*', 'min': 0.5, 'max': 2 },
                { 'channel': 'a.b.c', 'min': 0.9, 'max': 1.1 } ] }");
            EventConfig config = Merge(pack);

            AssertLimits(config, "a.b.c", 0.9f, 1.1f);      // the exact name beats every family
            AssertLimits(config, "a.b.d", 0.5f, 2f);        // the longer family beats the shorter one
            AssertLimits(config, "a.x", 0.3f, 3f);
            AssertLimits(config, "a.b", 0.3f, 3f);          // 'a.b.*' starts at 'a.b.': 'a.b' itself is a member of 'a.*' only
            AssertLimits(config, "other", 0.25f, 4f);
        }

        [Test]
        public void Limits_OfSeveralPacks_AreTheNarrowestOfThem()
        {
            PackConfig engine = Read("event-system", "{ 'channelLimits': [ { 'channel': '*', 'min': 0.25, 'max': 4 }, { 'channel': 'x.y', 'min': 0.1, 'max': 3 } ] }");
            PackConfig narrow = Read("narrow", "{ 'channelLimits': [ { 'channel': 'x.y', 'min': 0.4, 'max': 1.5 } ] }");
            PackConfig wide = Read("wide", "{ 'channelLimits': [ { 'channel': 'x.y', 'min': 0.05, 'max': 9 } ] }");
            PackConfig family = Read("family", "{ 'channelLimits': [ { 'channel': 'x.*', 'min': 0.3, 'max': 2 } ] }");

            AssertLimits(Merge(engine, narrow), "x.y", 0.4f, 1.5f);
            AssertLimits(Merge(engine, wide), "x.y", 0.1f, 3f);                 // a pack cannot widen a limit
            AssertLimits(Merge(engine, narrow, wide, family), "x.y", 0.4f, 1.5f);
            AssertLimits(Merge(engine, family), "x.y", 0.3f, 2f);               // a limit of a family narrows the exact name of another pack
            AssertLimits(Merge(engine, family), "x.z", 0.3f, 2f);
            AssertLimits(Merge(wide, narrow, engine), "x.y", 0.4f, 1.5f);       // the order of the packs does not matter
        }

        [Test]
        public void Limits_ForAChannelNobodyMentions_AreTheDefault_AndNarrowingCannotCrossOver()
        {
            PackConfig low = Read("low", "{ 'channelLimits': [ { 'channel': 'c', 'min': 0.1, 'max': 0.5 } ] }");
            PackConfig high = Read("high", "{ 'channelLimits': [ { 'channel': 'c', 'min': 1.5, 'max': 3 } ] }");

            EventConfig config = Merge(low, high);

            AssertLimits(config, "c", 0.5f, 0.5f);   // no room left: the maximum stands, the minimum gives way
            AssertLimits(config, "d", EventConfig.DefaultLimitMin, EventConfig.DefaultLimitMax);
            AssertLimits(config, string.Empty, EventConfig.DefaultLimitMin, EventConfig.DefaultLimitMax);
            AssertLimits(config, null, EventConfig.DefaultLimitMin, EventConfig.DefaultLimitMax);
        }

        [Test]
        public void TheScalars_AreTheSmallestValueAnyPackSets()
        {
            PackConfig a = Read("a", "{ 'maxPendingChoices': 3, 'logLimit': 300 }");
            PackConfig b = Read("b", "{ 'maxPendingChoices': 2, 'reminderDaysBefore': 5 }");
            PackConfig c = Read("c", "{ }");

            EventConfig config = Merge(a, b, c);

            Assert.Equal(2, config.MaxPendingChoices);
            Assert.Equal(5, config.ReminderDaysBefore);
            Assert.Equal(300, config.LogLimit);

            EventConfig onlyDefaults = Merge(c);
            Assert.Equal(EventConfig.DefaultMaxPendingChoices, onlyDefaults.MaxPendingChoices);
        }

        [Test]
        public void AFamilyPattern_NeedsAMemberName()
        {
            PackConfig pack = Read("p", "{ 'channelLimits': [ { 'channel': 'fam.*', 'min': 0.5, 'max': 2 } ] }");
            EventConfig config = Merge(pack);

            AssertLimits(config, "fam.member", 0.5f, 2f);
            AssertLimits(config, "fam", EventConfig.DefaultLimitMin, EventConfig.DefaultLimitMax);
            AssertLimits(config, "fam.", EventConfig.DefaultLimitMin, EventConfig.DefaultLimitMax);
        }
    }
}
