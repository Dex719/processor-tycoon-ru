using PTMod.Features.RivalAcquisitions;

namespace PTMod.Tests.Features.RivalAcquisitions
{
    /// <summary>
    /// <c>ValuationMath</c> (TSK-006, FR-1): the example of 1985 in design.md, the floor, the margin, the bankrupt company's snapshot and the share.
    /// </summary>
    [Covers(typeof(ValuationMath), typeof(Valuation))]
    public class ValuationMathTests
    {
        private const double Million = 1000000.0;

        private static ValuationConfig Config()
        {
            return RivalTestData.Balance().Valuation;
        }

        private static Valuation Evaluate(CompanySnapshot t, ValuationConfig c = null)
        {
            return ValuationMath.Evaluate(t, RivalTestData.Player(), c ?? Config());
        }

        [Test]
        public void Example1985_GivesTheFiguresOfTheDesign()
        {
            Valuation v = Evaluate(RivalTestData.ExampleTarget());

            Assert.Near(294.4 * Million, v.AFab, 1.0, "A_fab = 2 x 147.2 million");
            Assert.Near(12.0 * Million, v.ATech, 1.0, "A_tech = 0.5 x 3 x 400000 x 600 / 30");
            Assert.Near(96.0 * Million, v.ACpu, 1.0, "A_cpu = 2 x 120 million x 0.4");
            Assert.Near(0.08, v.Share, 1e-12);
            Assert.Near(402.4 * Million * 0.08, v.SharePremium, 1.0);
            Assert.Near(434.6 * Million, v.E, 0.1 * Million, "E = 434.6 million +- 0.1 million (AC-1.1)");
            Assert.Near(27.75 * Million, v.EMin, 1.0, "E_min = 0.25 x 111 million");
            Assert.False(v.FloorApplied);
        }

        [Test]
        public void EmptyCompany_IsWorthTheFloor()
        {
            var t = RivalTestData.Plain(0.0, 0.0, 111.0 * Million);
            Valuation v = Evaluate(t);

            Assert.Near(0.0, v.AFab + v.ATech + v.ACpu, 1e-9);
            Assert.Near(27.75 * Million, v.E, 1e-6, "AC-1.2: no lines, no CPUs, no gap: E = E_min");
            Assert.Near(27.75 * Million, v.EMin, 1e-6);
            Assert.True(v.FloorApplied);
        }

        [Test]
        public void TheFloor_FollowsTheCreditLimitAndTheConfig()
        {
            var t = RivalTestData.Plain(0.0, 0.0, 200.0 * Million);
            ValuationConfig c = Config();
            c.FloorDebtLimitShare = 0.5;

            Assert.Near(100.0 * Million, Evaluate(t, c).E, 1e-6);

            t.CreditLimit = 0.0;   // no bank: no floor
            Assert.Near(0.0, Evaluate(t, c).E, 1e-9);
        }

        [Test]
        public void TheMargin_IsClampedToZeroAndOne()
        {
            Assert.Near(0.4, ValuationMath.Margin(100.0, 60.0), 1e-12);
            Assert.Near(0.0, ValuationMath.Margin(100.0, 150.0), 1e-12, "sold below the cost: no margin, not a negative one");
            Assert.Near(1.0, ValuationMath.Margin(100.0, -20.0), 1e-12, "a negative cost cannot give more than all of the income");
            Assert.Near(0.0, ValuationMath.Margin(0.0, 10.0), 1e-12, "no price: no division by zero");
            Assert.Near(0.0, ValuationMath.Margin(double.NaN, 10.0), 1e-12);
            Assert.Near(0.0, ValuationMath.Margin(100.0, double.PositiveInfinity), 1e-12);

            // The same through the valuation: a CPU that loses money adds nothing.
            var t = RivalTestData.Plain(0.0, 0.0, 0.0);
            CpuSnapshot loser = RivalTestData.Cpu(1);
            loser.RealUnitCost = 150.0;
            t.Cpus.Add(loser);
            Assert.Near(0.0, Evaluate(t).ACpu, 1e-9);
        }

        [Test]
        public void BankruptSnapshot_CountsHalfOfTheCpusAndNoShare()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            t.Status = CompanyStatus.BankruptAuction;
            foreach (CpuSnapshot cpu in t.Cpus)
            {
                cpu.IsRetired = true;   // every CPU of a bankrupt company is retired; the snapshot says which were on sale
                cpu.ActiveAtBankruptcy = true;
            }

            Valuation v = Evaluate(t);

            Assert.Near(48.0 * Million, v.ACpu, 1.0, "the CPUs of the snapshot count 0.5");
            Assert.Near(0.0, v.Share, 1e-12, "AC-3.2: the share of a bankrupt company is 0");
            Assert.Near(0.0, v.SharePremium, 1e-9);
            Assert.Near(354.4 * Million, v.E, 0.1 * Million, "E = 354.4 million (AC-3.2)");
        }

        [Test]
        public void BankruptCompany_DoesNotCountCpusOutsideTheSnapshot()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            t.Status = CompanyStatus.Bankrupt;
            foreach (CpuSnapshot cpu in t.Cpus) cpu.IsRetired = true;   // not ActiveAtBankruptcy: a backfilled auction has no snapshot

            Assert.Near(0.0, Evaluate(t).ACpu, 1e-9);
        }

        [Test]
        public void ARetiredCpuOfALivingCompany_AddsNothing()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            t.Cpus[1].IsRetired = true;

            Assert.Near(48.0 * Million, Evaluate(t).ACpu, 1.0);
        }

        [Test]
        public void TheShare_IsTheLargerOfTheMarketAndTheFoundryShare()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            t.MarketShare = 0.05;
            t.FoundryMarketShare = 0.30;

            Valuation v = Evaluate(t);

            Assert.Near(0.30, v.Share, 1e-12);
            Assert.Near(402.4 * Million * 1.30, v.E, 1.0);
        }

        [Test]
        public void TheShare_IsClampedAndAnUnreadableOneCountsZero()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            t.MarketShare = 7.0;
            Assert.Near(1.0, Evaluate(t).Share, 1e-12);

            t.MarketShare = double.NaN;
            t.FoundryMarketShare = -2.0;
            Assert.Near(0.0, Evaluate(t).Share, 1e-12);
        }

        [Test]
        public void TheSums_AreDouble_NotFloat()
        {
            // 16777217 is the first whole number a float cannot hold; the double sum must keep it.
            var t = RivalTestData.Plain(0.0, 0.0, 0.0);
            t.ManufacturingDivisionValue = 16777217.0;

            Assert.Equal(33554434.0, Evaluate(t).AFab, "2 x 16777217 exactly");
        }

        [Test]
        public void ConfigCoefficients_ScaleTheirParts()
        {
            ValuationConfig c = Config();
            c.KFab = 1.0;
            c.KTech = 1.0;
            c.KCpu = 2.0;
            c.KShare = 0.0;

            Valuation v = Evaluate(RivalTestData.ExampleTarget(), c);

            Assert.Near(147.2 * Million, v.AFab, 1.0);
            Assert.Near(24.0 * Million, v.ATech, 1.0);
            Assert.Near(192.0 * Million, v.ACpu, 1.0);
            Assert.Near(0.0, v.SharePremium, 1e-9);
            Assert.Near(363.2 * Million, v.E, 1.0);
        }

        [Test]
        public void BrokenNumbers_NeverGiveNegativeOrNotANumber()
        {
            var t = RivalTestData.Plain(0.0, 0.0, 111.0 * Million);
            t.ManufacturingDivisionValue = double.NaN;
            t.TechGap.Add(RivalTestData.Tech(1, double.PositiveInfinity, 10.0));
            t.TechGap.Add(RivalTestData.Tech(2, -5.0, 10.0));
            t.TechGap.Add(null);
            t.Cpus.Add(null);
            CpuSnapshot odd = RivalTestData.Cpu(9);
            odd.Income12 = double.NaN;
            t.Cpus.Add(odd);

            Valuation v = Evaluate(t);

            Assert.False(double.IsNaN(v.E) || double.IsInfinity(v.E), "E is a number");
            Assert.Near(27.75 * Million, v.E, 1e-6, "nothing usable in the snapshot: the floor");
        }

        [Test]
        public void NothingToEvaluate_IsAnEmptyValuation()
        {
            Valuation v = ValuationMath.Evaluate(null, RivalTestData.Player(), Config());

            Assert.Near(0.0, v.E, 1e-12);
            Assert.Near(0.0, v.EMin, 1e-12);
        }
    }
}
