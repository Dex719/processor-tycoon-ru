#if PTMOD_DEBUG
using System;
using System.Globalization;
using PTMod.Core;
using PTMod.Features.BankruptcyRecovery.Logic;
using TMPro;
using UnityEngine;

namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// The debug block of the page of the feature in the mods hub (TSK-019; design.md "Ручные сценарии в игре", M1-M10): the buttons "Balance = k × D" (k = -0.8, -0.95, -1.1, -1.2,
    /// +0.1) and "DaysInDefault = N" (N = 1, 118) that set up the manual scenarios of the owner without waiting for a real default. Two locks, and a third in the build:
    /// <list type="number">
    /// <item>the whole file is the debug build's (<c>PTMOD_DEBUG</c>): a release build has neither these actions nor the call that builds the block, and its strings are never asked for;</item>
    /// <item>the block is built, and every button works, only while the flag <see cref="Flag"/> of the section <c>debug</c> of <c>ptmod_settings.json</c> is on
    /// (<c>ModSettings.Debug</c>, which is false in a release build whatever the file says); a button of a page that was open when the flag was taken away does nothing;</item>
    /// <item>the feature is active (<c>ModFeatures.IsActive</c>) and the call runs under the guard of the feature.</item>
    /// </list>
    /// The work is <see cref="RecoveryDebugEngine"/>'s (pure logic with the tests): every press writes a record with the mark <c>debug</c> to the journal of the feature and a line to the
    /// log; this class draws the buttons and the line with the result under them. With the flag on, the block also shows the threshold now (<c>D</c>, the base of the limits, the
    /// inflation, the date), and the start of a game or a load writes the same numbers to <c>Player.log</c> (<see cref="LogThreshold"/>: the runtime part of the spike TSK-003, which
    /// asks for them on the first day of a new game of 1975, 1980 and 1985). Main thread only.
    /// </summary>
    internal static class RecoveryDebugActions
    {
        /// <summary>The flag of the section <c>debug</c> of <c>ptmod_settings.json</c> (<c>"debug": { "recovery": true }</c>).</summary>
        internal const string Flag = "debug.recovery";

        private const string SiteBalance = "debug/balance";
        private const string SiteDays = "debug/days";

        /// <summary>The flag is on (a debug build only; in a release build the core answers false, and this file is not compiled at all).</summary>
        internal static bool IsOn
        {
            get { return ModSettings.Debug(Flag); }
        }

        /// <summary>Adds the block to the page: a heading, a hint, the threshold now, the line for the result and the seven buttons. Nothing without the flag.</summary>
        internal static void Build(RectTransform page)
        {
            BankruptcyRecoveryFeature feature = BankruptcyRecoveryFeature.Instance;
            if (page == null || feature == null || !IsOn) return;

            bool decimalComma = string.Equals(ModL10n.Language, "ru", StringComparison.Ordinal);
            ModUI.Text(page, ModL10n.L("rec.debug.heading"), true);
            ModUI.Text(page, ModL10n.L("rec.debug.hint", Flag));
            ModUI.Text(page, ModL10n.L("rec.debug.threshold", RecoveryViews.MoneyExact(feature.Finance.PlayerDebtLimit), RecoveryViews.MoneyExact(feature.Environment.LimitBase()),
                feature.Finance.Inflation.ToString("0.###", CultureInfo.InvariantCulture), RecoveryViews.GameDate(feature.Finance.Today)));
            TMP_Text answer = ModUI.Text(page, string.Empty);

            foreach (double share in RecoveryDebugEngine.BalanceShares)
            {
                double k = share;
                ModUI.Button(page, ModL10n.L("rec.debug.balance", RecoveryDebugEngine.ShareText(k, decimalComma)), () => SetBalance(k, answer));
            }
            foreach (int count in RecoveryDebugEngine.DefaultDays)
            {
                int days = count;
                ModUI.Button(page, ModL10n.L("rec.debug.days", days.ToString(CultureInfo.InvariantCulture)), () => SetDays(days, answer));
            }
        }

        /// <summary>
        /// With the flag on, writes the threshold to the log (a new game after its fast-forward, a loaded save): the numbers the spike TSK-003 wants for the first day of the game.
        /// Does nothing without the flag.
        /// </summary>
        internal static void LogThreshold(BankruptcyRecoveryFeature feature, GameReadyReason reason)
        {
            if (feature == null || !IsOn) return;

            string line = new RecoveryDebugEngine(feature.Environment).ThresholdLine();
            if (line.Length > 0) ModLog.Info(BankruptcyRecoveryFeature.FeatureId, reason + ": " + line);
        }

        private static void SetBalance(double share, TMP_Text answer)
        {
            Run(SiteBalance, answer, engine => engine.SetBalanceShare(share));
        }

        private static void SetDays(int days, TMP_Text answer)
        {
            Run(SiteDays, answer, engine => engine.SetDaysInDefault(days));
        }

        // The press: checks the locks again (the page may be older than the flag), runs the action under the guard, writes the line of the log and the line under the buttons.
        private static void Run(string site, TMP_Text answer, Func<RecoveryDebugEngine, DebugOutcome> action)
        {
            BankruptcyRecoveryFeature feature = BankruptcyRecoveryFeature.Instance;
            if (feature == null || !IsOn || !ModFeatures.IsActive(BankruptcyRecoveryFeature.FeatureId)) return;

            ModHooks.Guard(BankruptcyRecoveryFeature.FeatureId, site, () =>
            {
                DebugOutcome outcome = action(new RecoveryDebugEngine(feature.Environment));
                string text = Describe(outcome);
                ModLog.Info(BankruptcyRecoveryFeature.FeatureId, "debug: " + site + ": " + outcome.Status + " (" + text + ")");
                if (answer != null) answer.text = text;   // a destroyed label (the page was closed) compares equal to null
            });
        }

        // The line under the buttons: what the press did, in the language of the player.
        private static string Describe(DebugOutcome outcome)
        {
            switch (outcome.Status)
            {
                case DebugStatus.Done:
                    if (outcome.Kind == DebugKind.Days)
                        return ModL10n.L("rec.debug.done.days", RecoveryUi.Num((int)outcome.Before), RecoveryUi.Num((int)outcome.After));
                    return ModL10n.L("rec.debug.done.balance", RecoveryViews.MoneyExact(outcome.Before), RecoveryViews.MoneyExact(outcome.After));
                case DebugStatus.Unavailable:
                    return ModL10n.L("rec.debug.unavailable");
                default:
                    return ModL10n.L("rec.debug.rejected", outcome.Reason);
            }
        }
    }
}
#endif
