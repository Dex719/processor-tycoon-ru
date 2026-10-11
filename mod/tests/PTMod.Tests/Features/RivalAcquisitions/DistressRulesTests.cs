using System;
using System.Collections.Generic;
using PTMod.Features.RivalAcquisitions;

namespace PTMod.Tests.Features.RivalAcquisitions
{
    /// <summary>
    /// <c>DistressRules</c> (TSK-013; FR-3, FR-11): one default mail per episode, the auctions (opened for 365 days with the CPUs of the snapshot,
    /// closed when bought or past their last day), the backfill of an old save, and which bankruptcies are not the rival's own.
    /// </summary>
    [Covers(typeof(DistressRules))]
    public class DistressRulesTests
    {
        private static readonly DateTime Today = new DateTime(1987, 5, 3);

        // ---- the default mail ----

        [Test]
        public void TheDefaultMail_GoesOncePerEpisode_AndTheEpisodeEndsOutOfDefault()
        {
            var s = new AcquisitionsState();
            Assert.True(DistressRules.ShouldMailDefault(s, 412));

            DistressRules.MarkDefaultMailed(s, 412);
            DistressRules.MarkDefaultMailed(s, 412);
            Assert.False(DistressRules.ShouldMailDefault(s, 412), "not twice in an episode");
            Assert.Equal(1, s.defaultMailSentForSaveIds.Count);
            Assert.True(DistressRules.ShouldMailDefault(s, 500), "another company");

            DistressRules.MarkDefaultMailed(s, 500);
            int ended = DistressRules.EndEpisodes(s, saveId => saveId == 500);
            Assert.Equal(1, ended);
            Assert.True(DistressRules.ShouldMailDefault(s, 412), "412 is out of default: its next default gets a mail");
            Assert.False(DistressRules.ShouldMailDefault(s, 500), "500 is still in default");
        }

        [Test]
        public void TheDefaultMail_ToleratesAStateWithoutTheList()
        {
            var s = new AcquisitionsState { defaultMailSentForSaveIds = null };
            Assert.True(DistressRules.ShouldMailDefault(s, 1));
            DistressRules.MarkDefaultMailed(s, 1);
            Assert.False(DistressRules.ShouldMailDefault(s, 1));
            Assert.Equal(0, DistressRules.EndEpisodes(null, id => false));
            Assert.False(DistressRules.ShouldMailDefault(null, 1));
        }

        // ---- auctions ----

        [Test]
        public void OpenAuction_RunsForTheDaysOfTheContent_WithTheCpusOfTheSnapshot()
        {
            // AC-3.1: bankrupt on 1987-05-03, the auction runs until 03.05.1988.
            var s = new AcquisitionsState();

            AuctionData auction = DistressRules.OpenAuction(s, 7, 355, Today, 365, new[] { 980, 991, 1015, 991, -1 });

            Assert.Equal("1987-05-03", auction.bankruptOn);
            Assert.Equal("1988-05-02", auction.until, "365 days of a leap year");
            Assert.Equal("980,991,1015", string.Join(",", auction.snapshotCpuSaveIds.ConvertAll(x => x.ToString()).ToArray()), "no repeats, no negative ids");
            Assert.False(auction.closed);
            Assert.Equal(1, s.auctions.Count);
            Assert.True(CompanyStatusRules.IsAuctionOpen(auction, 355, Today));
            Assert.Equal("1987-05-04", DistressRules.Until(Today, 1));
            Assert.Equal("1987-05-03", DistressRules.Until(Today, -5), "a negative number of days is 0");
        }

        [Test]
        public void OpenAuction_ReplacesTheRecordOfAnOlderCompanyWithTheSameUniqueId()
        {
            var s = new AcquisitionsState();
            s.auctions.Add(new AuctionData { targetUniqueId = 7, targetSaveId = 100, bankruptOn = "1980-01-01", until = "1981-01-01", closed = true });
            s.auctions.Add(new AuctionData { targetUniqueId = 8, targetSaveId = 200, bankruptOn = "1980-01-01", until = "1981-01-01" });

            DistressRules.OpenAuction(s, 7, 355, Today, 365, null);

            Assert.Equal(2, s.auctions.Count, "one record per UniqueID (the save's rule)");
            Assert.Equal(355, s.auctions.Find(a => a.targetUniqueId == 7).targetSaveId);
            Assert.Equal(0, s.auctions.Find(a => a.targetUniqueId == 7).snapshotCpuSaveIds.Count, "no snapshot: no CPUs");
            Assert.Throws<ArgumentNullException>(() => DistressRules.OpenAuction(null, 1, 1, Today, 1, null));
        }

        [Test]
        public void CloseExpired_ClosesTheDayAfterTheLastDay_AsTheStatusDoes()
        {
            // AC-3.3: on 1988-05-04 the auction until 1988-05-03 is over; on the last day itself it is still open.
            var s = new AcquisitionsState();
            AuctionData auction = DistressRules.OpenAuction(s, 7, 355, new DateTime(1987, 5, 4), 365, null);   // until 1988-05-03

            Assert.Equal(0, DistressRules.CloseExpired(s, new DateTime(1988, 5, 3)));
            Assert.False(auction.closed);
            Assert.True(CompanyStatusRules.IsAuctionOpen(auction, 355, new DateTime(1988, 5, 3)));

            Assert.Equal(1, DistressRules.CloseExpired(s, new DateTime(1988, 5, 4)));
            Assert.True(auction.closed);
            Assert.False(CompanyStatusRules.IsAuctionOpen(auction, 355, new DateTime(1988, 5, 4)));
            Assert.Equal(0, DistressRules.CloseExpired(s, new DateTime(1990, 1, 1)), "closed once");
            Assert.Equal(0, DistressRules.CloseExpired(s, default(DateTime)), "no clock: nothing");
        }

        [Test]
        public void CloseAuction_ClosesOnlyThisVeryCompany()
        {
            var s = new AcquisitionsState();
            DistressRules.OpenAuction(s, 7, 355, Today, 365, null);

            Assert.False(DistressRules.CloseAuction(s, 7, 999), "another SaveID: a stale pair");
            Assert.True(DistressRules.CloseAuction(s, 7, 355));
            Assert.False(DistressRules.CloseAuction(s, 7, 355), "already closed");
            Assert.False(DistressRules.CloseAuction(null, 7, 355));
        }

        // ---- backfill ----

        [Test]
        public void NeedsBackfill_OnlyForBankruptsWithoutAnAuctionAndNotBoughtByThePlayer()
        {
            var s = new AcquisitionsState();
            Assert.True(DistressRules.NeedsBackfill(s, 7, 355, true));
            Assert.False(DistressRules.NeedsBackfill(s, 7, 355, false), "alive");

            s.auctions.Add(new AuctionData { targetUniqueId = 7, targetSaveId = 355, bankruptOn = "1987-05-03", until = "1988-05-02", closed = true });
            Assert.False(DistressRules.NeedsBackfill(s, 7, 355, true), "its auction is over already");
            Assert.True(DistressRules.NeedsBackfill(s, 7, 400, true), "a newer company with the same UniqueID");

            s.deals.Add(new DealRecord { id = 1, type = "Hostile", targetUniqueId = 9, targetSaveId = 900, date = "1986-01-01" });
            Assert.False(DistressRules.NeedsBackfill(s, 9, 900, true), "bought by the player");
            Assert.True(DistressRules.IsAbsorbedPair(s, 9, 900));
            Assert.False(DistressRules.IsAbsorbedPair(s, 9, 901), "another company with that UniqueID");

            s.deals.Add(new DealRecord { id = 2, type = "Stake", targetUniqueId = 10, targetSaveId = 1000, date = "1986-01-01" });
            Assert.False(DistressRules.IsAbsorbedPair(s, 10, 1000), "a stake does not absorb");
        }

        // ---- what is not a bankruptcy of the rival's own ----

        [Test]
        public void IgnoresBankruptcy_ForThePlayerTheDealLoadingAndFastForward()
        {
            Assert.False(DistressRules.IgnoresBankruptcy(false, false, false, false, false), "an AI company that went bankrupt during play");
            Assert.True(DistressRules.IgnoresBankruptcy(true, false, false, false, false), "the player");
            Assert.True(DistressRules.IgnoresBankruptcy(false, true, false, false, false), "the deal's own target (FR-10 item 5)");
            Assert.True(DistressRules.IgnoresBankruptcy(false, false, true, false, false), "a company the player bought");
            Assert.True(DistressRules.IgnoresBankruptcy(false, false, false, true, false), "loading (FR-11 item 3)");
            Assert.True(DistressRules.IgnoresBankruptcy(false, false, false, false, true), "the new-game fast-forward (FR-11 item 3)");
            Assert.Equal(120, DistressRules.DaysToBankruptcy);
        }
    }
}
