using System;
using System.Collections.Generic;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>One company as the royalty sees it: who it is, whether it can pay or receive, and what its x86 CPUs earned in the last full month.</summary>
    public sealed class RoyaltyCompany
    {
        /// <summary><c>ICompany.SaveID</c>.</summary>
        public int CompanyId;

        /// <summary><c>AICompany.UniqueID</c>; -1 for the player (he never holds the licence).</summary>
        public int UniqueId = -1;

        public bool IsBankrupt;

        /// <summary>A foundry designs no CPUs, so it pays nothing (its income is 0 anyway).</summary>
        public bool IsFoundry;

        /// <summary>The income of the last full month from the CPUs of this company on families of the licensed ISA (<c>CpuBase.MarketData.Income[Count − 2]</c> summed).</summary>
        public float X86Income;
    }

    /// <summary>What one company owes this month.</summary>
    public sealed class RoyaltyCharge
    {
        public int CompanyId;
        public float Amount;
    }

    /// <summary>What one holder of the licence is paid this month.</summary>
    public sealed class RoyaltyPayout
    {
        public int CompanyId;
        public float Amount;
    }

    /// <summary>The royalties of a month: who pays what, who is paid what. Empty when the licence is revoked, when no holder is alive or when nothing was earned.</summary>
    public sealed class RoyaltyPlan
    {
        public readonly List<RoyaltyCharge> Charges = new List<RoyaltyCharge>();
        public readonly List<RoyaltyPayout> Payouts = new List<RoyaltyPayout>();

        /// <summary>The licence of x86 was revoked (<c>LicenseHelper.X86LicenseHasBeenRevoked</c>) or no holder is alive: x86 is free.</summary>
        public bool Free;

        /// <summary>The sum of all the charges.</summary>
        public float Total;

        public bool IsEmpty
        {
            get { return Charges.Count == 0; }
        }

        /// <summary>What the company pays this month; 0 for a company that owes nothing.</summary>
        public float ChargeOf(int companyId)
        {
            for (int i = 0; i < Charges.Count; i++)
            {
                if (Charges[i].CompanyId == companyId) return Charges[i].Amount;
            }
            return 0f;
        }
    }

    /// <summary>
    /// The royalty for x86 without a licence (FR-16, stage 2; design.md Component 12, "Роялти x86"): x86 is open to everybody, but a company that does not hold the
    /// licence (the player and every AI company except the holders - Intel and AMD by default) pays <c>royaltyRate</c> of the income of its CPUs on x86 families to
    /// the holders, who are alive, in equal parts.
    /// <code>
    /// income_c  = Σ over the CPUs of company c with ISA x86 of Income[Count − 2]           the last full month
    /// royalty_c = royaltyRate × income_c                                                    0.03; none for a holder, none for 0 income
    /// payee     = Σ royalty_c / (the number of holders that are not bankrupt)
    /// </code>
    /// No royalty at all when the licence is revoked or no holder is alive. The same rules for the player and the AI. Pure: the incomes come in as numbers.
    /// </summary>
    public static class RoyaltyMath
    {
        /// <summary><c>royaltyRate × income</c>; never negative; 0 for an income that is not a usable number.</summary>
        public static float Royalty(float x86IncomeLastMonth, float rate)
        {
            if (float.IsNaN(x86IncomeLastMonth) || float.IsInfinity(x86IncomeLastMonth) || x86IncomeLastMonth <= 0f) return 0f;
            if (float.IsNaN(rate) || float.IsInfinity(rate) || rate <= 0f) return 0f;

            float royalty = x86IncomeLastMonth * rate;
            return float.IsInfinity(royalty) ? 0f : royalty;
        }

        /// <summary>The royalty divided equally among the living holders; an empty array for no holders (and for nothing to divide).</summary>
        public static float[] Distribute(float royalty, int liveHolders)
        {
            if (liveHolders <= 0 || float.IsNaN(royalty) || royalty <= 0f) return new float[0];

            var shares = new float[liveHolders];
            float share = royalty / liveHolders;
            for (int i = 0; i < liveHolders; i++) shares[i] = share;
            return shares;
        }

        /// <summary>The company with this <c>AICompany.UniqueID</c> holds the licence (<c>x86License.holderIds</c>). The player (-1) never does.</summary>
        public static bool IsHolder(int uniqueId, int[] holderIds)
        {
            if (uniqueId < 0 || holderIds == null) return false;
            for (int i = 0; i < holderIds.Length; i++)
            {
                if (holderIds[i] == uniqueId) return true;
            }
            return false;
        }

        /// <summary>
        /// The royalties of the month. <paramref name="revoked"/> is the game's flag (<c>X86LicenseHasBeenRevoked</c>). Companies are walked in the order given, so
        /// the plan does not depend on anything but the input; a bankrupt company or a foundry pays nothing, a holder pays nothing (AC-16.3), a company that earned
        /// nothing on x86 pays nothing.
        /// </summary>
        public static RoyaltyPlan Plan(IReadOnlyList<RoyaltyCompany> companies, X86LicenseConfig license, bool revoked)
        {
            var plan = new RoyaltyPlan();
            if (license == null) license = new X86LicenseConfig();
            if (companies == null) return plan;

            var holders = new List<RoyaltyCompany>();
            for (int i = 0; i < companies.Count; i++)
            {
                RoyaltyCompany c = companies[i];
                if (c != null && !c.IsBankrupt && IsHolder(c.UniqueId, license.holderIds)) holders.Add(c);
            }

            if (revoked || holders.Count == 0)
            {
                plan.Free = true;
                return plan;
            }

            for (int i = 0; i < companies.Count; i++)
            {
                RoyaltyCompany c = companies[i];
                if (c == null || c.IsBankrupt || c.IsFoundry || IsHolder(c.UniqueId, license.holderIds)) continue;

                float amount = Royalty(c.X86Income, license.royaltyRate);
                if (!(amount > 0f)) continue;
                plan.Charges.Add(new RoyaltyCharge { CompanyId = c.CompanyId, Amount = amount });
                plan.Total += amount;
            }

            float[] shares = Distribute(plan.Total, holders.Count);
            for (int i = 0; i < shares.Length; i++) plan.Payouts.Add(new RoyaltyPayout { CompanyId = holders[i].CompanyId, Amount = shares[i] });
            return plan;
        }
    }
}
