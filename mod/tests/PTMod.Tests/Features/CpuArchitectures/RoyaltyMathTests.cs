using System.Collections.Generic;
using PTMod.Features.CpuArchitectures;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// The royalty for x86 without a licence (FR-16, TSK-035): 3 % of the income of the x86 CPUs of last month, paid by every company that does not hold the
    /// licence to the holders that are alive in equal parts. AC-16.1 (10M gives 300k, 150k each to Intel and AMD), AC-16.2 (the licence revoked: no royalty), AC-16.3
    /// (a holder pays nothing), no living holder, no income, the same rules for the player and the AI (FR-16 item 4).
    /// </summary>
    [Covers(typeof(RoyaltyMath), typeof(RoyaltyPlan), typeof(RoyaltyCompany), typeof(RoyaltyCharge), typeof(RoyaltyPayout))]
    public class RoyaltyMathTests
    {
        private const int Intel = 0;
        private const int Amd = 1;

        private static X86LicenseConfig License()
        {
            return ArchTestKit.BuiltInBalance().x86License;
        }

        /// <summary>The player (id 3), Intel (id 10), AMD (id 11) and Cyrix (id 12).</summary>
        private static List<RoyaltyCompany> World(float player, float intel = 0f, float amd = 0f, float cyrix = 0f)
        {
            return new List<RoyaltyCompany>
            {
                new RoyaltyCompany { CompanyId = 3, UniqueId = -1, X86Income = player },
                new RoyaltyCompany { CompanyId = 10, UniqueId = Intel, X86Income = intel },
                new RoyaltyCompany { CompanyId = 11, UniqueId = Amd, X86Income = amd },
                new RoyaltyCompany { CompanyId = 12, UniqueId = 10, X86Income = cyrix },
            };
        }

        private static float PayoutOf(RoyaltyPlan plan, int companyId)
        {
            foreach (RoyaltyPayout payout in plan.Payouts)
            {
                if (payout.CompanyId == companyId) return payout.Amount;
            }
            return 0f;
        }

        // ---- the sum ----

        [Test]
        public void TheBuiltInLicence_IsThreePercent_ForIntelAndAmd()
        {
            X86LicenseConfig license = License();
            Assert.Equal("x86", license.isa);
            Assert.Near(0.03, license.royaltyRate, 1e-6);
            Assert.Equal(new[] { 0, 1 }, license.holderIds, "Intel and AMD, as CompanyHelper.IsInlet and IsEMD");
        }

        [Test]
        public void Royalty_IsTheRateTimesTheIncome_NeverNegative()
        {
            Assert.Near(300000.0, RoyaltyMath.Royalty(10000000f, 0.03f), 1.0, "AC-16.1: 3 % of 10M");
            Assert.Equal(0f, RoyaltyMath.Royalty(0f, 0.03f), "no income, no royalty");
            Assert.Equal(0f, RoyaltyMath.Royalty(-5f, 0.03f), "a loss is not a royalty");
            Assert.Equal(0f, RoyaltyMath.Royalty(1e6f, 0f), "rate 0");
            Assert.Equal(0f, RoyaltyMath.Royalty(1e6f, -1f));
            Assert.Equal(0f, RoyaltyMath.Royalty(float.NaN, 0.03f));
            Assert.Equal(0f, RoyaltyMath.Royalty(float.PositiveInfinity, 0.03f));
            Assert.Equal(0f, RoyaltyMath.Royalty(1e6f, float.NaN));
        }

        [Test]
        public void Distribute_IsEqual_AndEmptyWithoutHolders()
        {
            float[] shares = RoyaltyMath.Distribute(300000f, 2);
            Assert.Equal(2, shares.Length);
            Assert.Near(150000.0, shares[0], 0.5);
            Assert.Near(150000.0, shares[1], 0.5);

            Assert.Equal(1, RoyaltyMath.Distribute(300000f, 1).Length);
            Assert.Equal(0, RoyaltyMath.Distribute(300000f, 0).Length, "no living holder: nobody to pay");
            Assert.Equal(0, RoyaltyMath.Distribute(300000f, -2).Length);
            Assert.Equal(0, RoyaltyMath.Distribute(0f, 2).Length, "nothing to divide");
            Assert.Equal(0, RoyaltyMath.Distribute(float.NaN, 2).Length);
        }

        [Test]
        public void IsHolder_ByTheUniqueIdOfTheHistoricalCompany_NeverThePlayer()
        {
            int[] holders = License().holderIds;
            Assert.True(RoyaltyMath.IsHolder(Intel, holders));
            Assert.True(RoyaltyMath.IsHolder(Amd, holders));
            Assert.False(RoyaltyMath.IsHolder(10, holders), "Cyrix has no licence");
            Assert.False(RoyaltyMath.IsHolder(-1, new[] { -1, 0 }), "the player never holds it");
            Assert.False(RoyaltyMath.IsHolder(0, null));
            Assert.False(RoyaltyMath.IsHolder(0, new int[0]));
        }

        // ---- the month ----

        [Test]
        public void AC16_1_ThePlayerEarnsTenMillionOnX86_Pays300k_IntelAndAmdGet150kEach()
        {
            RoyaltyPlan plan = RoyaltyMath.Plan(World(10000000f), License(), false);

            Assert.False(plan.Free);
            Assert.Equal(1, plan.Charges.Count, "only the player owes");
            Assert.Equal(3, plan.Charges[0].CompanyId);
            Assert.Near(300000.0, plan.Charges[0].Amount, 1.0);
            Assert.Near(300000.0, plan.Total, 1.0);
            Assert.Near(300000.0, plan.ChargeOf(3), 1.0);

            Assert.Equal(2, plan.Payouts.Count);
            Assert.Near(150000.0, PayoutOf(plan, 10), 1.0, "Intel");
            Assert.Near(150000.0, PayoutOf(plan, 11), 1.0, "AMD");
        }

        [Test]
        public void AC16_3_AHolderPaysNothing_EvenWithX86Income()
        {
            RoyaltyPlan plan = RoyaltyMath.Plan(World(0f, intel: 50000000f, amd: 20000000f), License(), false);
            Assert.True(plan.IsEmpty, "Intel and AMD sell x86 and owe nothing");
            Assert.Equal(0f, plan.ChargeOf(10));
            Assert.Equal(0f, plan.ChargeOf(11));
            Assert.Equal(0, plan.Payouts.Count);
        }

        [Test]
        public void TheSameRulesForThePlayerAndAnAiCompany()
        {
            // The player and Cyrix earn the same: both owe the same.
            RoyaltyPlan plan = RoyaltyMath.Plan(World(4000000f, cyrix: 4000000f), License(), false);
            Assert.Equal(2, plan.Charges.Count);
            Assert.Near(plan.ChargeOf(3), plan.ChargeOf(12), 1e-3, "FR-16 item 4");
            Assert.Near(120000.0, plan.ChargeOf(12), 1.0);
            Assert.Near(240000.0, plan.Total, 1.0);
            Assert.Near(120000.0, PayoutOf(plan, 10), 1.0, "half of the total to each holder");
            Assert.Near(120000.0, PayoutOf(plan, 11), 1.0);
        }

        [Test]
        public void AC16_2_TheLicenceRevoked_NoRoyalty_X86IsFree()
        {
            RoyaltyPlan plan = RoyaltyMath.Plan(World(10000000f), License(), true);
            Assert.True(plan.Free);
            Assert.True(plan.IsEmpty);
            Assert.Equal(0, plan.Payouts.Count);
            Assert.Equal(0f, plan.Total);
        }

        [Test]
        public void NoLivingHolder_NoRoyalty()
        {
            List<RoyaltyCompany> world = World(10000000f);
            world[1].IsBankrupt = true;
            world[2].IsBankrupt = true;
            RoyaltyPlan plan = RoyaltyMath.Plan(world, License(), false);
            Assert.True(plan.Free, "both holders are bankrupt: x86 is free even if the game's flag was not raised");
            Assert.True(plan.IsEmpty);
        }

        [Test]
        public void OneLivingHolder_GetsEverything()
        {
            List<RoyaltyCompany> world = World(10000000f);
            world[2].IsBankrupt = true;   // AMD
            RoyaltyPlan plan = RoyaltyMath.Plan(world, License(), false);
            Assert.Equal(1, plan.Payouts.Count);
            Assert.Near(300000.0, PayoutOf(plan, 10), 1.0, "Intel alone");
        }

        [Test]
        public void NoIncome_NothingIsCharged_AndABankruptOrAFoundryPaysNothing()
        {
            Assert.True(RoyaltyMath.Plan(World(0f, cyrix: 0f), License(), false).IsEmpty, "income 0");
            Assert.True(RoyaltyMath.Plan(World(-100f), License(), false).IsEmpty, "a loss");

            List<RoyaltyCompany> world = World(10000000f, cyrix: 5000000f);
            world[0].IsBankrupt = true;
            world[3].IsFoundry = true;
            RoyaltyPlan plan = RoyaltyMath.Plan(world, License(), false);
            Assert.True(plan.IsEmpty, "the bankrupt player and a foundry are not charged");

            Assert.True(RoyaltyMath.Plan(null, License(), false).IsEmpty);
            Assert.True(RoyaltyMath.Plan(new List<RoyaltyCompany>(), License(), false).Free, "no holder among no companies");
            Assert.True(RoyaltyMath.Plan(new List<RoyaltyCompany> { null }, null, false).IsEmpty, "a missing record and a missing config are safe");
        }

        [Test]
        public void ThePlan_DoesNotDependOnAnythingButTheInput_AndTheRateCanBeChanged()
        {
            X86LicenseConfig license = License();
            license.royaltyRate = 0.10f;
            RoyaltyPlan first = RoyaltyMath.Plan(World(10000000f, cyrix: 1000000f), license, false);
            RoyaltyPlan second = RoyaltyMath.Plan(World(10000000f, cyrix: 1000000f), license, false);

            Assert.Near(1000000.0, first.ChargeOf(3), 1.0, "10 %");
            Assert.Near(100000.0, first.ChargeOf(12), 1.0);
            Assert.Equal(first.Total, second.Total);
            Assert.Equal(first.Charges.Count, second.Charges.Count);

            license.holderIds = new[] { 1 };   // AMD alone holds the licence
            RoyaltyPlan amdOnly = RoyaltyMath.Plan(World(10000000f, intel: 5000000f), license, false);
            Assert.Near(500000.0, amdOnly.ChargeOf(10), 1.0, "Intel does not hold it here: it pays");
            Assert.Equal(1, amdOnly.Payouts.Count);
            Assert.Equal(11, amdOnly.Payouts[0].CompanyId);
        }
    }
}
