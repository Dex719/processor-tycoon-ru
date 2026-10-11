using System;
using PTMod.Features.RivalAcquisitions;

namespace PTMod.Tests.Features.RivalAcquisitions
{
    /// <summary>
    /// <c>StakeBook</c> (TSK-014; FR-6): the records of the stakes and the rescue orders, the condition of the rescue prefix P3, the month of the
    /// dividend, and the records of a stake purchase and of a rescue in the journal.
    /// </summary>
    [Covers(typeof(StakeBook))]
    public class StakeBookTests
    {
        private static readonly DateTime Today = new DateTime(1986, 1, 1);

        // ---- stakes ----

        [Test]
        public void AddShare_OpensAStake_ThenAddsToIt()
        {
            // AC-6.1: 25% for 116.15 million; then 10% more.
            var s = new AcquisitionsState();

            double first = StakeBook.AddShare(s, 10, 388, 0.25, 116150000.0, Today);
            double second = StakeBook.AddShare(s, 10, 388, 0.10, 50000000.0, Today.AddMonths(3));

            Assert.Near(0.25, first, 1e-12);
            Assert.Near(0.35, second, 1e-12);
            Assert.Equal(1, s.stakes.Count);
            StakeData stake = StakeBook.Find(s, 10, 388);
            Assert.Near(166150000.0, stake.invested, 1e-6);
            Assert.Equal("1986-01-01", stake.since, "the date of the first purchase");
            Assert.Near(0.35, CompanyStatusRules.OwnedShare(s, 10, 388), 1e-12, "the deal rules see the same share");
        }

        [Test]
        public void AddShare_NeverReachesOneHalf_AndIgnoresWhatIsNotAShare()
        {
            var s = new AcquisitionsState();
            StakeBook.AddShare(s, 10, 388, 0.49, 1.0, Today);

            Assert.Near(0.49, StakeBook.AddShare(s, 10, 388, 0.05, 1.0, Today), 1e-12, "0.54 would be control: refused");
            Assert.Near(0.49, StakeBook.AddShare(s, 10, 388, 0.51, 1.0, Today), 1e-12, "1.00: refused");
            Assert.Near(0.49, StakeBook.AddShare(s, 10, 388, 0.0, 1.0, Today), 1e-12);
            Assert.Near(0.49, StakeBook.AddShare(s, 10, 388, double.NaN, 1.0, Today), 1e-12);
            Assert.Near(0.49, StakeBook.Find(s, 10, 388).share, 1e-12);
            Assert.Throws<ArgumentNullException>(() => StakeBook.AddShare(null, 1, 1, 0.1, 1.0, Today));
        }

        [Test]
        public void AStaleStake_IsIgnoredByTheReads_AndReplacedByANewPurchase()
        {
            var s = new AcquisitionsState();
            s.stakes.Add(new StakeData { targetUniqueId = 10, targetSaveId = 100, share = 0.40, invested = 5.0, since = "1980-01-01" });

            Assert.Null(StakeBook.Find(s, 10, 388), "another company with that UniqueID");
            double now = StakeBook.AddShare(s, 10, 388, 0.10, 1.0, Today);

            Assert.Near(0.10, now, 1e-12, "the stale share does not count");
            Assert.Equal(1, s.stakes.Count);
            Assert.Equal(388, s.stakes[0].targetSaveId);
        }

        [Test]
        public void RemoveStake_GivesTheShareItHad()
        {
            var s = new AcquisitionsState();
            StakeBook.AddShare(s, 10, 388, 0.25, 1.0, Today);

            Assert.Near(0.25, StakeBook.RemoveStake(s, 10), 1e-12);
            Assert.Equal(0, s.stakes.Count);
            Assert.Near(0.0, StakeBook.RemoveStake(s, 10), 0.0, "nothing left");
            Assert.Near(0.0, StakeBook.RemoveStake(null, 10), 0.0);
        }

        [Test]
        public void RecordDividend_AddsUp()
        {
            var stake = new StakeData { share = 0.25 };
            StakeBook.RecordDividend(stake, 1000000.0);
            StakeBook.RecordDividend(stake, 500000.0);
            StakeBook.RecordDividend(stake, -3.0);
            StakeBook.RecordDividend(stake, double.PositiveInfinity);
            StakeBook.RecordDividend(null, 1.0);

            Assert.Near(1500000.0, stake.dividendsPaid, 1e-6);
            Assert.Near(500000.0, stake.lastDividend, 1e-6, "the last good one; a loss, infinity and a null stake change nothing");
        }

        // ---- rescue orders ----

        [Test]
        public void SetOrder_CreatesOrChangesTheOrder_AndRemoveOrderEndsIt()
        {
            var s = new AcquisitionsState();

            RescueOrderData order = StakeBook.SetOrder(s, 10, 388, 100000000.0, true);
            Assert.True(order.active);
            Assert.Near(100000000.0, order.maxAmount, 0.0);

            StakeBook.SetOrder(s, 10, 388, -5.0, false);
            Assert.Equal(1, s.rescueOrders.Count);
            Assert.False(StakeBook.Order(s, 10, 388).active);
            Assert.Near(0.0, StakeBook.Order(s, 10, 388).maxAmount, 0.0, "a negative limit is 0");

            Assert.Null(StakeBook.Order(s, 10, 999), "a stale pair is not this company's order");
            StakeBook.SetOrder(s, 10, 999, 1.0, true);
            Assert.Equal(1, s.rescueOrders.Count, "the stale order is replaced");

            Assert.True(StakeBook.RemoveOrder(s, 10));
            Assert.False(StakeBook.RemoveOrder(s, 10));
            Assert.Throws<ArgumentNullException>(() => StakeBook.SetOrder(null, 1, 1, 1.0, true));
        }

        [Test]
        public void ShouldTryRescue_OnlyWithAnActiveOrder_ADefaultAndABalanceAtTheThreshold()
        {
            // design.md P3: the order is active, negativeBalanceLimit < 0, the company is in default and MoneyAmount <= negativeBalanceLimit.
            Assert.True(StakeBook.ShouldTryRescue(true, -111.0, true, -150.0), "AC-6.3");
            Assert.True(StakeBook.ShouldTryRescue(true, -111.0, true, -111.0), "exactly at the threshold the game goes on to its sale");
            Assert.False(StakeBook.ShouldTryRescue(false, -111.0, true, -150.0), "no active order");
            Assert.False(StakeBook.ShouldTryRescue(true, 0.0, true, -150.0), "the debug call DebugTriggerFabless(0) is ignored");
            Assert.False(StakeBook.ShouldTryRescue(true, -111.0, false, -150.0), "not in default");
            Assert.False(StakeBook.ShouldTryRescue(true, -111.0, true, -100.0), "above the threshold the game's method returns by itself");
        }

        // ---- the month of a dividend ----

        [Test]
        public void PreviousMonth_IsTheMonthBefore()
        {
            int year;
            int month;
            StakeBook.PreviousMonth(new DateTime(1986, 1, 1), out year, out month);
            Assert.Equal(1985, year);
            Assert.Equal(12, month);

            StakeBook.PreviousMonth(new DateTime(1986, 7, 1), out year, out month);
            Assert.Equal(1986, year);
            Assert.Equal(6, month);
        }

        // ---- records ----

        [Test]
        public void Record_OfAStakeAndOfARescue()
        {
            DealRecord stake = StakeBook.Record(DealType.Stake, 10, 388, "Cyrix", Today, 116150000.0, 0.25);
            Assert.Equal("Stake", stake.type);
            Assert.Equal(10, stake.targetUniqueId);
            Assert.Equal(388, stake.targetSaveId);
            Assert.Equal("Cyrix", stake.targetName);
            Assert.Equal("1986-01-01", stake.date);
            Assert.Near(116150000.0, stake.price, 0.0);
            Assert.Near(0.25, stake.share, 0.0);

            DealRecord rescue = StakeBook.Record(DealType.RescueOrder, 10, 388, null, Today, -1.0, 0.7);
            Assert.Equal("RescueOrder", rescue.type);
            Assert.Equal("", rescue.targetName);
            Assert.Near(0.0, rescue.price, 0.0, "a negative price is 0");
            Assert.Near(0.0, rescue.share, 0.0, "a share of one half or more is not a minority stake");
            Assert.False(AcquisitionsState.IsAbsorbingType(rescue.type), "a stake does not absorb the company");
        }
    }
}
