using System.Collections.Generic;
using System.Linq;
using PTMod.Features.RivalAcquisitions;

namespace PTMod.Tests.Features.RivalAcquisitions
{
    /// <summary>The models of the tabs "My stakes" and "History" (TSK-015; FR-6, FR-14 item 2): which stakes and orders are listed, in what order, and the journal of the deals.</summary>
    [Covers(typeof(StakesModel), typeof(StakeRow), typeof(HistoryModel), typeof(HistoryRow))]
    public class StakesAndHistoryModelTests
    {
        private static List<CompanySnapshot> Companies()
        {
            return new List<CompanySnapshot>
            {
                UiTestData.Company(1, "Alpha", CompanyStatus.Healthy),
                UiTestData.Company(2, "Beta", CompanyStatus.InDefault),
                UiTestData.Company(3, "Gamma", CompanyStatus.Bankrupt),
                UiTestData.Company(4, "Delta", CompanyStatus.Absorbed),
            };
        }

        // ---- stakes ----

        [Test]
        public void TheStakes_AreListedByShare_WithTheirNumbers()
        {
            List<CompanySnapshot> companies = Companies();
            var s = new AcquisitionsState();
            s.stakes.Add(new StakeData { targetUniqueId = 1, targetSaveId = companies[0].SaveId, share = 0.10, invested = 40e6, dividendsPaid = 3e6, lastDividend = 1e6 });
            s.stakes.Add(new StakeData { targetUniqueId = 2, targetSaveId = companies[1].SaveId, share = 0.25, invested = 90e6 });

            List<StakeRow> rows = StakesModel.Build(s, companies);

            Assert.Equal(new[] { 2, 1 }, rows.Select(r => r.UniqueId).ToArray(), "the biggest stake first");
            StakeRow alpha = StakesModel.Find(rows, 1);
            Assert.Near(0.10, alpha.Share, 1e-12);
            Assert.Near(40e6, alpha.Invested, 1e-6);
            Assert.Near(3e6, alpha.DividendsPaid, 1e-6);
            Assert.Near(1e6, alpha.LastDividend, 1e-6);
            Assert.Equal("Alpha", alpha.Name);
            Assert.Equal(CompanyStatus.Healthy, alpha.Status);
            Assert.Equal(CompanyStatus.InDefault, StakesModel.Find(rows, 2).Status);
            Assert.Near(RivalTestData.DebtLimit, alpha.CreditLimit, 1e-6);
        }

        [Test]
        public void AStakeInACompanyThatIsGone_OrStale_IsNotListed()
        {
            List<CompanySnapshot> companies = Companies();
            var s = new AcquisitionsState();
            s.stakes.Add(new StakeData { targetUniqueId = 3, targetSaveId = companies[2].SaveId, share = 0.25 });   // bankrupt: the stake is written off
            s.stakes.Add(new StakeData { targetUniqueId = 4, targetSaveId = companies[3].SaveId, share = 0.25 });   // taken over
            s.stakes.Add(new StakeData { targetUniqueId = 1, targetSaveId = 999, share = 0.25 });                    // another company has this UniqueID now
            s.stakes.Add(new StakeData { targetUniqueId = 77, targetSaveId = 477, share = 0.25 });                   // not in the game

            Assert.Equal(0, StakesModel.Build(s, companies).Count);
        }

        [Test]
        public void ARescueOrderThatIsOn_IsListedWithoutAStake_AnInactiveOneIsNot()
        {
            List<CompanySnapshot> companies = Companies();
            var s = new AcquisitionsState();
            StakeBook.SetOrder(s, 1, companies[0].SaveId, 100e6, true);
            StakeBook.SetOrder(s, 2, companies[1].SaveId, 50e6, false);

            List<StakeRow> rows = StakesModel.Build(s, companies);

            Assert.Equal(1, rows.Count);
            Assert.Equal(1, rows[0].UniqueId);
            Assert.Equal(0.0, rows[0].Share);
            Assert.True(rows[0].OrderActive);
            Assert.Near(100e6, rows[0].OrderMaxAmount, 1e-6);
        }

        [Test]
        public void AStakeWithAnOrder_ShowsBoth()
        {
            List<CompanySnapshot> companies = Companies();
            var s = new AcquisitionsState();
            s.stakes.Add(new StakeData { targetUniqueId = 2, targetSaveId = companies[1].SaveId, share = 0.25 });
            StakeBook.SetOrder(s, 2, companies[1].SaveId, 70e6, true);

            StakeRow row = StakesModel.Build(s, companies)[0];

            Assert.Near(0.25, row.Share, 1e-12);
            Assert.True(row.OrderActive);
            Assert.Near(70e6, row.OrderMaxAmount, 1e-6);
        }

        [Test]
        public void EqualStakes_AreOrderedByName()
        {
            List<CompanySnapshot> companies = Companies();
            var s = new AcquisitionsState();
            s.stakes.Add(new StakeData { targetUniqueId = 2, targetSaveId = companies[1].SaveId, share = 0.25 });
            s.stakes.Add(new StakeData { targetUniqueId = 1, targetSaveId = companies[0].SaveId, share = 0.25 });

            Assert.Equal(new[] { 1, 2 }, StakesModel.Build(s, companies).Select(r => r.UniqueId).ToArray());
        }

        [Test]
        public void NoBooks_NoCompanies_NoRows()
        {
            Assert.Equal(0, StakesModel.Build(null, Companies()).Count);
            Assert.Equal(0, StakesModel.Build(new AcquisitionsState(), null).Count);
            Assert.Equal(0, StakesModel.Build(new AcquisitionsState(), new List<CompanySnapshot> { null }).Count);
            Assert.Null(StakesModel.Find(null, 1));
        }

        // ---- history ----

        private static DealRecord Deal(int id, string type, string date, double price = 100.0, bool partial = false, double share = 0.0, string notes = "")
        {
            return new DealRecord { id = id, type = type, targetUniqueId = id, targetSaveId = 400 + id, targetName = "Company " + id, date = date, price = price, partial = partial, share = share, notes = notes };
        }

        [Test]
        public void TheJournal_IsShownNewestFirst_WithDatesInTheGamesFormat()
        {
            var deals = new List<DealRecord>
            {
                Deal(1, "Hostile", "1985-03-01"),
                Deal(2, "Stake", "1986-01-01", share: 0.25),
                Deal(3, "BankruptAssets", "1987-06-15"),
            };

            List<HistoryRow> rows = HistoryModel.Rows(deals);

            Assert.Equal(new[] { 3, 2, 1 }, rows.Select(r => r.Id).ToArray());
            Assert.Equal("15.06.1987", rows[0].Date);
            Assert.Equal("Company 3", rows[0].TargetName);
            Assert.Near(100.0, rows[0].Price, 1e-9);
            Assert.Near(0.25, rows[1].Share, 1e-12);
            Assert.Equal(3, rows[0].TargetUniqueId);
            Assert.Equal(403, rows[0].TargetSaveId);
        }

        [Test]
        public void EachKindOfDeal_HasItsOwnText()
        {
            var deals = new List<DealRecord>
            {
                Deal(1, "BankruptAssets", "1985-01-01"), Deal(2, "Distressed", "1985-01-01"), Deal(3, "Hostile", "1985-01-01"),
                Deal(4, "Stake", "1985-01-01"), Deal(5, "RescueOrder", "1985-01-01"),
            };

            List<HistoryRow> rows = HistoryModel.Rows(deals);

            Assert.Equal(UiKeys.HistoryBankrupt, rows.First(r => r.Id == 1).TypeKey);
            Assert.Equal(UiKeys.HistoryDistressed, rows.First(r => r.Id == 2).TypeKey);
            Assert.Equal(UiKeys.HistoryHostile, rows.First(r => r.Id == 3).TypeKey);
            Assert.Equal(UiKeys.HistoryStake, rows.First(r => r.Id == 4).TypeKey);
            Assert.Equal(UiKeys.HistoryRescue, rows.First(r => r.Id == 5).TypeKey);
            Assert.Equal(DealType.RescueOrder, rows.First(r => r.Id == 5).Type);
            Assert.Equal(5, rows.Select(r => r.TypeKey).Distinct().Count());
        }

        [Test]
        public void APartialDeal_IsMarked_WithItsNotes()
        {
            var deals = new List<DealRecord> { Deal(1, "Hostile", "1985-03-01", partial: true, notes: "step 6: fewer lines than planned"), Deal(2, "Hostile", "1985-03-02") };

            List<HistoryRow> rows = HistoryModel.Rows(deals);

            Assert.False(rows[0].Partial);
            Assert.True(rows[1].Partial);
            Assert.Equal("step 6: fewer lines than planned", rows[1].Notes);
        }

        [Test]
        public void ARecordOfAnUnknownType_OrANullOne_IsSkipped()
        {
            var deals = new List<DealRecord> { Deal(1, "Hostile", "1985-03-01"), Deal(2, "Merger", "1985-03-02"), null };

            Assert.Equal(1, HistoryModel.Rows(deals).Count);
            Assert.Equal(0, HistoryModel.Rows(null).Count);
        }

        [Test]
        public void TheTab_ShowsTheLastRowsOnly()
        {
            var deals = new List<DealRecord>();
            for (int i = 1; i <= HistoryModel.MaxRows + 20; i++) deals.Add(Deal(i, "Stake", "1985-01-01"));

            List<HistoryRow> rows = HistoryModel.Rows(deals);

            Assert.Equal(HistoryModel.MaxRows, rows.Count);
            Assert.Equal(HistoryModel.MaxRows + 20, rows[0].Id, "the newest is on top");
        }

        [Test]
        public void ABadDate_IsShownAsItIs()
        {
            var deals = new List<DealRecord> { Deal(1, "Hostile", "someday") };

            Assert.Equal("someday", HistoryModel.Rows(deals)[0].Date);
        }
    }
}
