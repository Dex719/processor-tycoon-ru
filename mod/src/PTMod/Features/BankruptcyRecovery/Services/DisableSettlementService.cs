using System.Globalization;
using PTMod.Core;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// The safe switch-off in the game (design.md, <c>DisableSettlementService</c>; FR-12, D9): the question the hub asks before it switches the feature off, and the
    /// settlement that turns what is owed into ordinary debt of the balance. The rules are <see cref="DisableSettlementEngine"/>'s (pure logic with the tests); this
    /// class adds the texts of the question, the notification and the log.
    /// <list type="bullet">
    /// <item><see cref="BuildConfirmation"/> is the provider of <c>FeatureContext.ConfirmBeforeDisable</c>: null when nothing is owed (the hub then switches off at
    /// once), otherwise the sum, the balance after it and, if that balance is below the default threshold, the warning about the default.</item>
    /// <item><see cref="Settle"/> runs from <c>OnDisabled</c> (the "Yes" of the hub and a switch from code alike) and is safe to repeat.</item>
    /// </list>
    /// </summary>
    internal sealed class DisableSettlementService
    {
        private readonly DisableSettlementEngine _engine;

        public DisableSettlementService(RecoveryEnvironment env)
        {
            _engine = new DisableSettlementEngine(env, new SaveMarks());
        }

        /// <summary>The rules of the settlement.</summary>
        public DisableSettlementEngine Engine
        {
            get { return _engine; }
        }

        /// <summary>
        /// The question before the switch-off, built from the live numbers (the hub calls this when the player flips the switch). Null: nothing is owed or the
        /// player's company is not there, the feature goes off without a question. The sums are written the way the game writes money, in full.
        /// </summary>
        public DisableConfirmation BuildConfirmation()
        {
            DisablePlan plan = _engine.Quote();
            if (plan == null) return null;

            // "{2}" follows the full stop of the sentence, so the warning brings its own space
            string risk = plan.WouldDefault ? " " + ModL10n.L("rec.disable.defaultRisk") : string.Empty;
            return new DisableConfirmation
            {
                MessageKey = "rec.disable.confirm",
                Args = new[] { RecoveryViews.MoneyExact(plan.Total), RecoveryViews.MoneyExact(plan.BalanceAfter), risk },
                ConfirmLabelKey = "rec.disable.confirmLabel",
            };
        }

        /// <summary>
        /// Turns the obligations into ordinary debt and closes them; repeating it does nothing. Writes the line of the log and, if money was taken, shows the
        /// notification. <paramref name="reason"/> is only for the log. Never throws (the caller runs it under the guard of the feature all the same).
        /// </summary>
        public SettleResult Settle(string reason)
        {
            SettleResult result = _engine.Settle();
            switch (result.Status)
            {
                case SettleStatus.Done:
                    ModLog.Info(RecoveryApi.FeatureId, "settle (" + reason + "): " + Money(result.Converted) + " became ordinary debt, "
                                                       + result.LoansClosed.ToString(CultureInfo.InvariantCulture) + " loan(s) closed"
                                                       + (result.LineClosed ? ", the credit line closed" : string.Empty)
                                                       + (result.InvestorClosed ? ", the investor contract closed" : string.Empty));
                    if (result.Converted > 0.0) ModNotify.ShowKey(RecoveryApi.FeatureId, "rec.notify.converted", RecoveryViews.Money(result.Converted));
                    break;
                case SettleStatus.Unavailable:
                    ModLog.Warn(RecoveryApi.FeatureId, "settle (" + reason + "): the player's company or the ledger is not there, the obligations stay as they are");
                    break;
            }
            return result;
        }

        // The sum for the log: plain digits, no game formatting (the formatter may not be ready).
        private static string Money(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
