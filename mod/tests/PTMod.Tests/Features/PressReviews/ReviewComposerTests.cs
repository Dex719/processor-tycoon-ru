using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Rng;
using PTMod.Features.PressReviews;

namespace PTMod.Tests.PressReviews
{
    /// <summary>
    /// The review of one CPU on its day (FR-1 item 4, TSK-007): the axes of <c>ReviewMath</c>, the marks of the outlets of the year and the segment, the metascore
    /// and the verdict, in the record of the archive; the same stream gives the same review, also after the stream's state is restored (AC-3.2).
    /// </summary>
    [Covers(typeof(ReviewComposer))]
    public class ReviewComposerTests
    {
        private static readonly DateTime Day = new DateTime(1995, 3, 15);

        /// <summary>No noise and always the first quote: what a review is without luck.</summary>
        private sealed class QuietRandom : IRandomSource
        {
            public float NextGaussian()
            {
                return 0f;
            }

            public int Range(int minInclusive, int maxExclusive)
            {
                return minInclusive;
            }
        }

        private static CpuSnapshot Cpu(int saveId, float ips, float price, float power, float temperature, float release, Segment segment, string company = "player")
        {
            return new CpuSnapshot
            {
                SaveId = saveId, CompanyKey = company, Name = "CPU " + saveId, Ips = ips, Price = price, PowerConsumption = power,
                Temperature = temperature, SafeTemperature = 90f, ReleaseYearFraction = release, Segment = segment,
            };
        }

        private static PendingReview Pending(int saveId = 7, string company = "player")
        {
            return new PendingReview { cpuSaveId = saveId, companyKey = company, releasedOn = "1995-03-01", publishOn = "1995-03-15", soldBySegment = new[] { 0, 0, 0, 0 } };
        }

        /// <summary>The setup of AC-2.1: axes 73.2 / 65.0 / 55.9 / 62.5 and a total of 67.4 in High End.</summary>
        private static ReviewInput Ac21(int peerId = 100)
        {
            CpuSnapshot peer = Cpu(peerId, 100f, 100f, 10f, 70f, 1995.2f, Segment.HighEnd, "ai:1");
            CpuSnapshot cpu = Cpu(7, 110f, 110f, 110f / 9f, 70f, 1995.2f, Segment.HighEnd);
            return new ReviewInput { Cpu = cpu, Peers = new[] { peer }, NowYearFraction = 1995.2f, Difficulty = 2 };
        }

        private static ReviewRecord Compose(ReviewInput input, DateTime today, IRandomSource rng, List<Outlet> outlets = null)
        {
            return ReviewComposer.Compose(Pending(), "Intelligent", input, today, outlets ?? PressContentFixture.Outlets(), PressContentFixture.Config(), rng);
        }

        private static List<string> OutletIds(ReviewRecord record)
        {
            var ids = new List<string>();
            foreach (OutletLine line in record.outlets) ids.Add(line.outletId);
            return ids;
        }

        // ---- the record ----

        [Test]
        public void Compose_FillsTheRecordOfTheArchive()
        {
            ReviewRecord r = Compose(Ac21(), Day, new QuietRandom());

            Assert.Equal(7, r.cpuSaveId);
            Assert.Equal("CPU 7", r.cpuName);
            Assert.Equal("player", r.companyKey);
            Assert.Equal("Intelligent", r.companyName);
            Assert.Equal("1995-03-01", r.releasedOn, "the day of the release comes from the queue");
            Assert.Equal("1995-03-15", r.publishedOn, "the day of the review");
            Assert.Equal(0, r.segment, "High End");
            Assert.False(r.noAnalog);
            Assert.False(r.compact);
            Assert.Equal(0, r.mailId, "the mail is not written here");
            Assert.Equal(0, r.reputationDelta, "the effects are left for ReviewEffects");
            Assert.Near(1.0, r.hypeAtPublish, 1e-6);
            Assert.NotNull(r.notes);
            Assert.Equal(0, r.notes.Count);
            Assert.Equal(new List<int> { 100 }, r.peerSaveIds, "the reference CPU");
        }

        [Test]
        public void Compose_TheAxesAreThoseOfTheRequirement_AC21()
        {
            ReviewRecord r = Compose(Ac21(), Day, new QuietRandom());

            Assert.Equal(4, r.axes.Length);
            Assert.Near(73.2, r.axes[(int)Axis.Performance], 0.05, "performance");
            Assert.Near(65.0, r.axes[(int)Axis.Value], 0.05, "value");
            Assert.Near(55.9, r.axes[(int)Axis.Efficiency], 0.05, "efficiency");
            Assert.Near(62.5, r.axes[(int)Axis.Thermal], 0.05, "thermal");
        }

        [Test]
        public void Compose_TheOutletsOfTheYearMarkTheCpu_AndTheMetascoreIsTheirMean()
        {
            ReviewRecord r = Compose(Ac21(), Day, new QuietRandom());

            Assert.Equal(new List<string> { "kilobyte", "overclock_world" }, OutletIds(r), "1995 High End: Electronics Today closed in 1992, Silicon Review opens in 1997");
            // KiloByte weighs as the segment does: B = 67.4. Overclock World shifts the weights to 0.65 / 0.10 / 0.15 / 0.10 and has a bias of -3: 68.7 - 3 = 65.7.
            Assert.Equal(67, r.outlets[0].score, "KiloByte");
            Assert.Equal(66, r.outlets[1].score, "Overclock World");
            Assert.Equal(67, r.metascore, "round(mean(67, 66)) = round(66.5), halves go up");
            Assert.Equal("press.verdict.average", r.verdictKey);
        }

        [Test]
        public void Compose_EveryOutletGetsAQuoteFromItsPool()
        {
            ReviewRecord r = Compose(Ac21(), Day, new QuietRandom());
            foreach (OutletLine line in r.outlets)
            {
                Assert.True(!string.IsNullOrEmpty(line.quoteKey) && line.quoteKey.StartsWith("press.quote."), "a quote for " + line.outletId + ": " + line.quoteKey);
            }
        }

        [Test]
        public void Compose_TheYearOfTheReviewPicksTheOutlets()
        {
            ReviewRecord early = Compose(Ac21(), new DateTime(1990, 6, 1), new QuietRandom());
            Assert.Equal(new List<string> { "electronics_today", "kilobyte", "overclock_world" }, OutletIds(early), "1990 High End: three outlets (AC-3.1)");

            ReviewRecord late = Compose(Ac21(), new DateTime(1999, 6, 1), new QuietRandom());
            Assert.Equal(new List<string> { "kilobyte", "overclock_world", "silicon_review" }, OutletIds(late), "1999 High End");
        }

        [Test]
        public void Compose_TheSegmentOfTheCpu_PicksTheOutletsAndTheWeights()
        {
            CpuSnapshot embedded = Cpu(8, 100f, 80f, 2f, 50f, 1995.2f, Segment.Industries);
            ReviewInput input = new ReviewInput { Cpu = embedded, Peers = new CpuSnapshot[0], NowYearFraction = 1995.2f, Difficulty = 2 };

            ReviewRecord r = Compose(input, Day, new QuietRandom());
            Assert.Equal(3, r.segment, "Industries");
            Assert.Equal(new List<string> { "embedded_digest" }, OutletIds(r), "1995 Industries: only Embedded Digest");
        }

        // ---- the reference CPUs ----

        [Test]
        public void Compose_WithoutReferenceCpus_TheCompanysPreviousCpuIsTheReference()
        {
            ReviewInput input = Ac21();
            input.Peers = new CpuSnapshot[0];
            input.OwnPrevious = Cpu(55, 100f, 100f, 10f, 70f, 1995.2f, Segment.HighEnd);

            ReviewRecord r = Compose(input, Day, new QuietRandom());
            Assert.False(r.noAnalog, "there is something to compare with");
            Assert.Equal(new List<int> { 55 }, r.peerSaveIds);
            Assert.Near(73.2, r.axes[(int)Axis.Performance], 0.05, "the same axes as against a peer");
        }

        [Test]
        public void Compose_WithNothingToCompareWith_IsAReviewWithoutAnalogues()
        {
            ReviewInput input = Ac21();
            input.Peers = new CpuSnapshot[0];

            ReviewRecord r = Compose(input, Day, new QuietRandom());
            Assert.True(r.noAnalog);
            Assert.Equal(0, r.peerSaveIds.Count);
            Assert.Near(70.0, r.axes[(int)Axis.Performance], 1e-4, "the fixed score of a CPU nothing can be compared with");
        }

        [Test]
        public void ReferenceIds_ListsThePeersInTheirOrder_ElseThePreviousCpu_ElseNothing()
        {
            ReviewInput input = Ac21();
            input.Peers = new[] { Cpu(3, 1f, 1f, 1f, 1f, 1990f, Segment.HighEnd), Cpu(2, 1f, 1f, 1f, 1f, 1990f, Segment.HighEnd) };
            input.OwnPrevious = Cpu(9, 1f, 1f, 1f, 1f, 1990f, Segment.HighEnd);
            Assert.Equal(new List<int> { 3, 2 }, ReviewComposer.ReferenceIds(input));

            input.Peers = null;
            Assert.Equal(new List<int> { 9 }, ReviewComposer.ReferenceIds(input));

            input.OwnPrevious = null;
            Assert.Equal(0, ReviewComposer.ReferenceIds(input).Count);
            Assert.Equal(0, ReviewComposer.ReferenceIds(null).Count);
        }

        // ---- the outlets that are not there ----

        [Test]
        public void Compose_NoOutletInTheContent_TheStubOutletReviews()
        {
            ReviewRecord r = Compose(Ac21(), Day, new QuietRandom(), new List<Outlet>());
            Assert.Equal(1, r.outlets.Count);
            Assert.Equal("press_stub", r.outlets[0].outletId);
            Assert.Equal(r.outlets[0].score, r.metascore, "the only outlet is the metascore");
        }

        [Test]
        public void Compose_TheVerdictFollowsTheMetascore()
        {
            var generous = new Outlet { Id = "fan", YearFrom = 1971, YearTo = 2030, Bias = 40f, NoiseSigma = 0f };
            generous.Segments.Add(Segment.HighEnd);
            var harsh = new Outlet { Id = "critic", YearFrom = 1971, YearTo = 2030, Bias = -60f, NoiseSigma = 0f };
            harsh.Segments.Add(Segment.HighEnd);

            ReviewRecord high = Compose(Ac21(), Day, new QuietRandom(), new List<Outlet> { generous });
            Assert.Equal(100, high.metascore, "67 + 40 is held to 100");
            Assert.Equal("press.verdict.masterpiece", high.verdictKey);

            ReviewRecord low = Compose(Ac21(), Day, new QuietRandom(), new List<Outlet> { harsh });
            Assert.Equal(7, low.metascore, "67 - 60");
            Assert.Equal("press.verdict.flop", low.verdictKey);
        }

        // ---- the dice ----

        [Test]
        public void Compose_OneSeedGivesOneReview()
        {
            ReviewRecord a = Compose(Ac21(), Day, new ModRngSource(new Xoshiro128(77)));
            ReviewRecord b = Compose(Ac21(), Day, new ModRngSource(new Xoshiro128(77)));

            Assert.Equal(a.metascore, b.metascore);
            Assert.Equal(a.outlets.Count, b.outlets.Count);
            for (int i = 0; i < a.outlets.Count; i++)
            {
                Assert.Equal(a.outlets[i].outletId, b.outlets[i].outletId);
                Assert.Equal(a.outlets[i].score, b.outlets[i].score, "the mark of " + a.outlets[i].outletId);
                Assert.Equal(a.outlets[i].quoteKey, b.outlets[i].quoteKey, "the quote of " + a.outlets[i].outletId);
            }
        }

        [Test]
        public void Compose_AfterTheStateOfTheStreamIsRestored_RepeatsTheReview_AC32()
        {
            var live = new Xoshiro128(1234);
            for (int i = 0; i < 25; i++) live.NextUInt32();   // the game has been running for a while
            uint[] saved = live.GetState();

            ReviewRecord first = Compose(Ac21(), Day, new ModRngSource(live));

            Xoshiro128 restored;
            Assert.True(Xoshiro128.TryFromState(saved, out restored), "the saved state is valid");
            ReviewRecord again = Compose(Ac21(), Day, new ModRngSource(restored));

            Assert.Equal(first.metascore, again.metascore, "a save and a load do not change the review");
            for (int i = 0; i < first.outlets.Count; i++)
            {
                Assert.Equal(first.outlets[i].score, again.outlets[i].score);
                Assert.Equal(first.outlets[i].quoteKey, again.outlets[i].quoteKey);
            }
        }

        [Test]
        public void Compose_TheNoiseIsWhatMakesTwoSeedsDiffer_AndStaysSmall()
        {
            var metascores = new HashSet<int>();
            for (uint seed = 1; seed <= 200; seed++)
            {
                ReviewRecord r = Compose(Ac21(), Day, new ModRngSource(new Xoshiro128(seed)));
                Assert.InRange(r.metascore, 55, 80, "seed " + seed + ": the noise of sigma 3-4 does not turn a 67 into a flop or a masterpiece");
                metascores.Add(r.metascore);
            }
            Assert.True(metascores.Count > 3, "different seeds give different marks: " + metascores.Count + " distinct metascores");
        }

        // ---- bad input ----

        [Test]
        public void Compose_NullArguments_AreRefused()
        {
            Assert.Throws<ArgumentNullException>(() => ReviewComposer.Compose(null, "x", Ac21(), Day, PressContentFixture.Outlets(), PressContentFixture.Config(), new QuietRandom()));
            Assert.Throws<ArgumentNullException>(() => ReviewComposer.Compose(Pending(), "x", null, Day, PressContentFixture.Outlets(), PressContentFixture.Config(), new QuietRandom()));
            Assert.Throws<ArgumentNullException>(() => ReviewComposer.Compose(Pending(), "x", Ac21(), Day, PressContentFixture.Outlets(), PressContentFixture.Config(), null));
        }

        [Test]
        public void Compose_ASegmentOfAny_IsReviewedAsMidRange_NotWithASegmentOfMinusOne()
        {
            ReviewInput input = Ac21();
            input.Cpu = Cpu(7, 110f, 110f, 12f, 70f, 1995.2f, Segment.Any);

            ReviewRecord r = Compose(input, Day, new QuietRandom());
            Assert.Equal((int)Segment.MidRange, r.segment);
        }

        [Test]
        public void Compose_ANamelessCpuAndCompany_GivesEmptyTextsNotNulls()
        {
            ReviewInput input = Ac21();
            input.Cpu.Name = null;
            input.Cpu.CompanyKey = null;
            PendingReview pending = Pending(7, "ai:4");
            pending.releasedOn = null;

            ReviewRecord r = ReviewComposer.Compose(pending, null, input, Day, PressContentFixture.Outlets(), PressContentFixture.Config(), new QuietRandom());
            Assert.Equal(string.Empty, r.cpuName);
            Assert.Equal(string.Empty, r.companyName);
            Assert.Equal("ai:4", r.companyKey, "the queue's key stands in");
            Assert.Equal("1995-03-15", r.releasedOn, "a missing release day is the day of the review");
        }
    }
}
