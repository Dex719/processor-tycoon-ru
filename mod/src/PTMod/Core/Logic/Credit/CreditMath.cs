namespace PTMod.Core.Logic.Credit
{
    /// <summary>
    /// The arithmetic of the channel <c>bank.creditLimit</c> (FR-12 item 19, FR-24): the one place where the default threshold of a company is
    /// computed, shared by the game's code (the transpilers P10/P11 through <c>ModCreditHooks</c>) and by the features (<c>ModCredit</c>), so the
    /// threshold a feature reads is the one the game applies.
    /// <list type="bullet">
    /// <item><b>Global part</b> (P9, <see cref="GlobalDebtLimit"/>): <c>CentralBank.DebtLimit = max(1e6, DebtLimit × M(global))</c>. Every reader of
    /// <c>DebtLimit</c> sees it, the AI's own reserves in <c>AIBehaviour*</c> included.</item>
    /// <item><b>Company part</b> (P10, <see cref="CompanyMultiplier"/>): the multiplier of a company over the global one. The channel's value for a
    /// company is the product of the global entries and the company's own; the global part is already inside <c>DebtLimit</c>, so the company part is
    /// <c>M(company) / M(global)</c> - a global ×0.8 alone leaves every threshold at <c>−DebtLimit</c> (AC-24.3, no double application).</item>
    /// <item><b>Threshold</b> (<see cref="Threshold"/>): <c>−DebtLimit × company part</c> when the transpiler P10 is in the game, else <c>−DebtLimit</c>.</item>
    /// </list>
    /// A multiplier that is not a finite number of 0 or more counts as 1 (the game's value stands); the debt limit itself is taken as the game has
    /// it, so the threshold is always the one the game's own comparison would use. Pure: no game, no Unity.
    /// </summary>
    public static class CreditMath
    {
        /// <summary>The floor of <c>CentralBank.UpdateCreditLimit</c>: the debt limit is never below 1 million.</summary>
        public const float MinDebtLimit = 1e6f;

        /// <summary>
        /// The default threshold of a company - a negative number, compared with the balance like the game does (default when
        /// <c>MoneyAmount &lt; threshold</c>). <paramref name="perCompanyActive"/> true (P10 applied): <c>−debtLimit × companyMultiplier</c>;
        /// false: <c>−debtLimit</c>, the multiplier is ignored. A zero limit gives 0 (never −0).
        /// </summary>
        /// <param name="debtLimit">The game's <c>CentralBank.DebtLimit</c> (already multiplied by the global part, P9).</param>
        /// <param name="companyMultiplier">The company's part of <c>bank.creditLimit</c> (<see cref="CompanyMultiplier"/>).</param>
        /// <param name="perCompanyActive">The transpiler P10 is in the game (<c>ModCredit.IsPerCompanyActive</c>).</param>
        public static float Threshold(float debtLimit, float companyMultiplier, bool perCompanyActive)
        {
            float limit = perCompanyActive ? debtLimit * Usable(companyMultiplier) : debtLimit;
            float threshold = -limit;
            return threshold == 0f ? 0f : threshold;
        }

        /// <summary>The positive debt limit behind a threshold (<c>ModCredit.DebtLimitOf</c>, <c>PlayerDebtLimit</c>): <c>−threshold</c>, 0 for a zero threshold.</summary>
        public static float LimitOf(float threshold)
        {
            float limit = -threshold;
            return limit == 0f ? 0f : limit;
        }

        /// <summary>
        /// Patch P9: the debt limit after <c>CentralBank.UpdateCreditLimit</c> with the global multiplier applied, <c>max(1e6, limit × M)</c>. A multiplier
        /// that is not usable leaves the game's limit as it is (no floor applied by the mod either).
        /// </summary>
        public static float GlobalDebtLimit(float gameDebtLimit, float globalMultiplier)
        {
            if (!IsUsable(globalMultiplier) || globalMultiplier == 1f) return gameDebtLimit;

            float limit = gameDebtLimit * globalMultiplier;
            return limit < MinDebtLimit ? MinDebtLimit : limit;
        }

        /// <summary>
        /// The company's part of <c>bank.creditLimit</c>: its product over the global product, <c>M(company) / M(global)</c>, because the global part is
        /// already in <c>DebtLimit</c> (P9). An unusable company product counts as 1; an unusable or zero global product is no divisor (the company
        /// product is returned).
        /// </summary>
        /// <param name="companyProduct">The channel's value for the company (global entries, entries of all companies, of the company, its providers).</param>
        /// <param name="globalProduct">The channel's value without a company (the global entries and providers only).</param>
        public static float CompanyMultiplier(float companyProduct, float globalProduct)
        {
            float company = Usable(companyProduct);
            if (company == globalProduct) return 1f;   // only global entries: the company has no part of its own (also 0 and 0)
            if (!IsUsable(globalProduct) || globalProduct == 0f) return company;

            float part = company / globalProduct;
            return IsUsable(part) ? part : 1f;
        }

        /// <summary>A multiplier the formulas accept: finite and 0 or more.</summary>
        public static bool IsUsable(float multiplier)
        {
            return !float.IsNaN(multiplier) && !float.IsInfinity(multiplier) && multiplier >= 0f;
        }

        private static float Usable(float multiplier)
        {
            return IsUsable(multiplier) ? multiplier : 1f;
        }
    }
}
