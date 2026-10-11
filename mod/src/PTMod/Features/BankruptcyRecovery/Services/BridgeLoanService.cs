using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// The bridge loan in the game (design.md, <c>BridgeLoanService</c>; FR-6). The rules are <see cref="BridgeLoanEngine"/>'s (pure logic with the tests):
    /// who may take the loan, how much, what it costs. The window asks <see cref="Check"/>, <see cref="MaxAmount"/> and the terms for the preview and calls
    /// <see cref="Take"/> after the player confirmed; the payments are the <see cref="LoanService"/>'s, the loan is a normal one in its book.
    /// </summary>
    internal sealed class BridgeLoanService
    {
        private readonly BridgeLoanEngine _engine;

        public BridgeLoanService(RecoveryEnvironment env, LoanService loans)
        {
            _engine = new BridgeLoanEngine(env, loans.Book);
        }

        /// <summary>Whether a bridge loan may be taken now; the reason of a "no" is a key of the string tables (<c>rec.bridge.unavailable.*</c>).</summary>
        public Eligibility Check()
        {
            return _engine.Check();
        }

        /// <summary><c>A_max = β × D_L + γ × R_3</c>: the most the player may take.</summary>
        public double MaxAmount()
        {
            return _engine.MaxAmount();
        }

        /// <summary>The fee of a loan of that amount, paid at once.</summary>
        public double Fee(double amount)
        {
            return _engine.Fee(amount);
        }

        /// <summary>The monthly payment of a loan of that amount at today's rates.</summary>
        public double Payment(double amount)
        {
            return _engine.Payment(amount);
        }

        /// <summary>The yearly rate of the loan with the multiplier of <c>bank.interest</c>, as a fraction.</summary>
        public double EffectiveRate()
        {
            return _engine.EffectiveRate();
        }

        /// <summary>
        /// Takes the loan after the player confirmed (FR-6 item 2): the amount is credited outside the report, the fee goes through the report, the
        /// annuity loan is registered. Does nothing, and says why, when it cannot be taken.
        /// </summary>
        public BridgeTakeResult Take(double amount)
        {
            LoanData loan;
            return _engine.Take(amount, out loan);
        }
    }
}
