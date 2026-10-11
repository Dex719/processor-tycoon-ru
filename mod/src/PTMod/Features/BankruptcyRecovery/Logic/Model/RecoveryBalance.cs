using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    // The typed form of Content/bankruptcy-recovery/balance.json (FR-12 item 1, NFR-7; design.md, "Контент: balance.json"). The classes are plain
    // DTOs: the public fields carry the names of the JSON members, so ModJson.Bind fills them straight from the merged config of
    // ModContent.LoadConfig and ModJson.From gives the file back (the test compares the defaults with the shipped file). The initializers
    // are the defaults: the same numbers as the shipped file and as design.md. Validate() puts the default back into every field that lies outside
    // its range and says so in the problem list (the feature writes each line to ModLog).

    /// <summary>The section <c>limits</c>: the floor of the base of the credit limits.</summary>
    public sealed class LimitsConfig
    {
        /// <summary>The floor of the base of the limits: <c>D_L = max(D, floorBase × Inflation)</c>; 0..1e9.</summary>
        public double floorBase = 2000000.0;
    }

    /// <summary>The section <c>preWarning</c> (FR-1): when the warning letter is sent and when the episode is forgotten. Shares of <c>D</c>.</summary>
    public sealed class PreWarningConfig
    {
        /// <summary>The letter is sent when the balance is below <c>−thresholdDebtLimitShare × D</c>; 0..10.</summary>
        public double thresholdDebtLimitShare = 0.75;

        /// <summary>The episode is over when the balance is back at or above <c>−resetDebtLimitShare × D</c>; 0..10, not above the threshold share.</summary>
        public double resetDebtLimitShare = 0.5;
    }

    /// <summary>The section <c>earlyExit</c> (FR-9).</summary>
    public sealed class EarlyExitConfig
    {
        /// <summary>Days in a row above the threshold after which the default is cancelled; 1..120.</summary>
        public int streakDays = 7;
    }

    /// <summary>The section <c>startupCredit</c> (FR-7): the startup credit line and the term loan it turns into.</summary>
    public sealed class StartupCreditConfig
    {
        /// <summary>How long the line stays open after the foundation of the company, in years; 1..10.</summary>
        public int years = 3;

        /// <summary>The coefficient <c>a_d</c> of the limit per difficulty level (0..5), a share of <c>D_L</c>; exactly 6 values, each 0..10.</summary>
        public double[] baseDebtLimitShareByDifficulty = { 1.5, 1.25, 1.0, 0.75, 0.5, 0.25 };

        /// <summary>The share of the revenue of the last 3 full months that adds to the limit; 0..1.</summary>
        public double revenueShare = 0.5;

        /// <summary>The upper bound of the limit, a share of <c>D_L</c>; 0..10.</summary>
        public double capDebtLimitShare = 3.0;

        /// <summary>The annual interest rate of the drawn part before the <c>bank.interest</c> multiplier; 0..2.</summary>
        public double annualRate = 0.10;

        /// <summary>The line draws money when the balance is below <c>−autoDrawTriggerShare × D</c>; 0..10.</summary>
        public double autoDrawTriggerShare = 0.9;

        /// <summary>The auto draw lifts the balance to <c>−autoDrawTargetShare × D</c>; 0..10, not above the trigger share.</summary>
        public double autoDrawTargetShare = 0.6;

        /// <summary>The share of a positive balance that repays the drawn part each month; 0..1.</summary>
        public double sweepShare = 0.25;

        /// <summary>The term of the loan the drawn part turns into when the line closes, in months; 1..120.</summary>
        public int termMonths = 24;

        /// <summary>The annual rate of that term loan before the multiplier; 0..2.</summary>
        public double termAnnualRate = 0.15;
    }

    /// <summary>The section <c>bridgeLoan</c> (FR-6).</summary>
    public sealed class BridgeConfig
    {
        /// <summary>The loan is offered when the balance is below <c>−eligibleDebtLimitShare × D</c> (or the company is in default); 0..10.</summary>
        public double eligibleDebtLimitShare = 0.75;

        /// <summary>The share of <c>D_L</c> in the largest amount; 0..10.</summary>
        public double baseDebtLimitShare = 1.0;

        /// <summary>The share of the revenue of the last 3 full months in the largest amount; 0..1.</summary>
        public double revenueShare = 0.25;

        /// <summary>The fee, a share of the amount; 0..1.</summary>
        public double fee = 0.05;

        /// <summary>The annual rate before the multiplier; 0..2.</summary>
        public double annualRate = 0.30;

        /// <summary>The term in months; 1..120.</summary>
        public int months = 12;

        /// <summary>The pause between two bridge loans, in days; 0..3650.</summary>
        public int cooldownDays = 180;
    }

    /// <summary>The terms of the rescue loan (<c>bailout.loan</c>).</summary>
    public sealed class LoanTermsConfig
    {
        /// <summary>The annual rate before the multiplier; 0..2.</summary>
        public double annualRate = 0.40;

        /// <summary>The term in months; 1..120.</summary>
        public int months = 36;
    }

    /// <summary>The terms of the investor share (<c>bailout.investor</c>).</summary>
    public sealed class InvestorConfig
    {
        /// <summary>The share of the positive profit of the last month the investor takes; 0..1.</summary>
        public double profitShare = 0.30;

        /// <summary>How many months the contract lasts; 1..120.</summary>
        public int months = 60;

        /// <summary>The ceiling of all payments, in multiples of the amount received; 0..100.</summary>
        public double capMultiple = 3.0;

        /// <summary>The expense item of the payments: <c>Royalties</c> (the owner's decision) or <c>Interest</c>.</summary>
        public string expenseOrigin = "Royalties";
    }

    /// <summary>The section <c>bailout</c> (FR-8): the last chance offer.</summary>
    public sealed class BailoutConfig
    {
        /// <summary>How many times per game the offer is made, per difficulty level (0..5); exactly 6 whole numbers, each 0..100.</summary>
        public int[] perGameByDifficulty = { 2, 1, 1, 1, 1, 0 };

        /// <summary>The balance after the offer is <c>+targetDebtLimitShare × D</c>; 0..10.</summary>
        public double targetDebtLimitShare = 0.25;

        public LoanTermsConfig loan = new LoanTermsConfig();

        public InvestorConfig investor = new InvestorConfig();

        /// <summary>The change of the reputation of the player when the offer is accepted; −100..100.</summary>
        public int reputationDelta = -10;
    }

    /// <summary>The section <c>assets</c> (FR-3): the sale of production lines.</summary>
    public sealed class AssetsConfig
    {
        /// <summary>How many lines must stay after a sale (the game never lets a company sell all of them); 1..100.</summary>
        public int minLinesLeft = 1;
    }

    /// <summary>
    /// The numbers of the feature: the typed form of <c>Content/bankruptcy-recovery/balance.json</c> (FR-12 item 1, NFR-7). The services and the
    /// pure math read everything from here. <see cref="FromJson"/> takes the merged config of <c>ModContent.LoadConfig</c>; a member of the wrong
    /// type, outside its range, an array of the wrong length, an unknown expense item or a pair of values that contradict each other keeps (gets)
    /// the default and adds one line to the problem list, for example <c>bridgeLoan.annualRate out of range, default 0.30 used</c> (AC-12.1).
    /// Never throws.
    /// </summary>
    public sealed class RecoveryBalance
    {
        /// <summary>The <c>schema</c> of the content file this version reads.</summary>
        public const int Schema = 1;

        /// <summary>The number of difficulty levels (<c>Player.DifficultyLevel</c> is 0..5); the length of the <c>*ByDifficulty</c> arrays.</summary>
        public const int DifficultyLevels = 6;

        /// <summary>The expense items the investor share may use.</summary>
        public static readonly IReadOnlyList<string> ExpenseOrigins = new[] { "Royalties", "Interest" };

        /// <summary>The version of the file; <c>ModContent</c> checks it, the code does not use it.</summary>
        public int schema = Schema;

        public LimitsConfig limits = new LimitsConfig();
        public PreWarningConfig preWarning = new PreWarningConfig();
        public EarlyExitConfig earlyExit = new EarlyExitConfig();
        public StartupCreditConfig startupCredit = new StartupCreditConfig();
        public BridgeConfig bridgeLoan = new BridgeConfig();
        public BailoutConfig bailout = new BailoutConfig();
        public AssetsConfig assets = new AssetsConfig();

        /// <summary>A balance with the defaults of the code (the same as the shipped <c>balance.json</c>).</summary>
        public static RecoveryBalance Defaults()
        {
            return new RecoveryBalance();
        }

        /// <summary>
        /// Reads the merged config and checks it. A null or empty config (the built-in file could not be read; <c>ModContent</c> has reported it)
        /// gives the defaults without a problem line. <paramref name="problems"/> may be null.
        /// </summary>
        public static RecoveryBalance FromJson(JsonObject config, List<string> problems)
        {
            List<string> found = problems ?? new List<string>();
            if (config == null || config.Count == 0) return Defaults();

            var errors = new List<string>();
            RecoveryBalance balance = ModJson.Bind<RecoveryBalance>(config, errors) ?? Defaults();
            foreach (string error in errors) found.Add(StripRoot(error));

            balance.Validate(found);
            return balance;
        }

        /// <summary>
        /// Puts the default into every field that is outside its range (NaN and infinity included) and adds a line to <paramref name="problems"/>
        /// for each. Ranges: shares of <c>D</c> 0..10, shares of a base 0..1, annual rates 0..2, terms 1..120 months, <c>floorBase</c> 0..1e9,
        /// <c>streakDays</c> 1..120, <c>cooldownDays</c> 0..3650 (design.md, the table of the fields). A missing section is replaced by its defaults.
        /// </summary>
        public void Validate(List<string> problems)
        {
            if (problems == null) problems = new List<string>();
            var d = new RecoveryBalance();

            if (limits == null) limits = Missing("limits", new LimitsConfig(), problems);
            if (preWarning == null) preWarning = Missing("preWarning", new PreWarningConfig(), problems);
            if (earlyExit == null) earlyExit = Missing("earlyExit", new EarlyExitConfig(), problems);
            if (startupCredit == null) startupCredit = Missing("startupCredit", new StartupCreditConfig(), problems);
            if (bridgeLoan == null) bridgeLoan = Missing("bridgeLoan", new BridgeConfig(), problems);
            if (bailout == null) bailout = Missing("bailout", new BailoutConfig(), problems);
            if (bailout.loan == null) bailout.loan = Missing("bailout.loan", new LoanTermsConfig(), problems);
            if (bailout.investor == null) bailout.investor = Missing("bailout.investor", new InvestorConfig(), problems);
            if (assets == null) assets = Missing("assets", new AssetsConfig(), problems);

            Check(ref limits.floorBase, 0, 1e9, d.limits.floorBase, "limits.floorBase", problems);

            PreWarningConfig pw = preWarning;
            PreWarningConfig dpw = d.preWarning;
            Check(ref pw.thresholdDebtLimitShare, 0, 10, dpw.thresholdDebtLimitShare, "preWarning.thresholdDebtLimitShare", problems);
            Check(ref pw.resetDebtLimitShare, 0, 10, dpw.resetDebtLimitShare, "preWarning.resetDebtLimitShare", problems);
            if (pw.resetDebtLimitShare > pw.thresholdDebtLimitShare)
            {
                problems.Add("preWarning.resetDebtLimitShare must not exceed preWarning.thresholdDebtLimitShare, defaults "
                             + Fmt(dpw.thresholdDebtLimitShare) + " and " + Fmt(dpw.resetDebtLimitShare) + " used");
                pw.thresholdDebtLimitShare = dpw.thresholdDebtLimitShare;
                pw.resetDebtLimitShare = dpw.resetDebtLimitShare;
            }

            Check(ref earlyExit.streakDays, 1, 120, d.earlyExit.streakDays, "earlyExit.streakDays", problems);

            StartupCreditConfig sc = startupCredit;
            StartupCreditConfig dsc = d.startupCredit;
            Check(ref sc.years, 1, 10, dsc.years, "startupCredit.years", problems);
            CheckArray(ref sc.baseDebtLimitShareByDifficulty, 0, 10, dsc.baseDebtLimitShareByDifficulty, "startupCredit.baseDebtLimitShareByDifficulty", problems);
            Check(ref sc.revenueShare, 0, 1, dsc.revenueShare, "startupCredit.revenueShare", problems);
            Check(ref sc.capDebtLimitShare, 0, 10, dsc.capDebtLimitShare, "startupCredit.capDebtLimitShare", problems);
            Check(ref sc.annualRate, 0, 2, dsc.annualRate, "startupCredit.annualRate", problems);
            Check(ref sc.autoDrawTriggerShare, 0, 10, dsc.autoDrawTriggerShare, "startupCredit.autoDrawTriggerShare", problems);
            Check(ref sc.autoDrawTargetShare, 0, 10, dsc.autoDrawTargetShare, "startupCredit.autoDrawTargetShare", problems);
            if (sc.autoDrawTargetShare > sc.autoDrawTriggerShare)
            {
                problems.Add("startupCredit.autoDrawTargetShare must not exceed startupCredit.autoDrawTriggerShare, defaults "
                             + Fmt(dsc.autoDrawTriggerShare) + " and " + Fmt(dsc.autoDrawTargetShare) + " used");
                sc.autoDrawTriggerShare = dsc.autoDrawTriggerShare;
                sc.autoDrawTargetShare = dsc.autoDrawTargetShare;
            }
            Check(ref sc.sweepShare, 0, 1, dsc.sweepShare, "startupCredit.sweepShare", problems);
            Check(ref sc.termMonths, 1, 120, dsc.termMonths, "startupCredit.termMonths", problems);
            Check(ref sc.termAnnualRate, 0, 2, dsc.termAnnualRate, "startupCredit.termAnnualRate", problems);

            BridgeConfig br = bridgeLoan;
            BridgeConfig dbr = d.bridgeLoan;
            Check(ref br.eligibleDebtLimitShare, 0, 10, dbr.eligibleDebtLimitShare, "bridgeLoan.eligibleDebtLimitShare", problems);
            Check(ref br.baseDebtLimitShare, 0, 10, dbr.baseDebtLimitShare, "bridgeLoan.baseDebtLimitShare", problems);
            Check(ref br.revenueShare, 0, 1, dbr.revenueShare, "bridgeLoan.revenueShare", problems);
            Check(ref br.fee, 0, 1, dbr.fee, "bridgeLoan.fee", problems);
            Check(ref br.annualRate, 0, 2, dbr.annualRate, "bridgeLoan.annualRate", problems);
            Check(ref br.months, 1, 120, dbr.months, "bridgeLoan.months", problems);
            Check(ref br.cooldownDays, 0, 3650, dbr.cooldownDays, "bridgeLoan.cooldownDays", problems);

            BailoutConfig bo = bailout;
            BailoutConfig dbo = d.bailout;
            CheckArray(ref bo.perGameByDifficulty, 0, 100, dbo.perGameByDifficulty, "bailout.perGameByDifficulty", problems);
            Check(ref bo.targetDebtLimitShare, 0, 10, dbo.targetDebtLimitShare, "bailout.targetDebtLimitShare", problems);
            Check(ref bo.loan.annualRate, 0, 2, dbo.loan.annualRate, "bailout.loan.annualRate", problems);
            Check(ref bo.loan.months, 1, 120, dbo.loan.months, "bailout.loan.months", problems);
            Check(ref bo.investor.profitShare, 0, 1, dbo.investor.profitShare, "bailout.investor.profitShare", problems);
            Check(ref bo.investor.months, 1, 120, dbo.investor.months, "bailout.investor.months", problems);
            Check(ref bo.investor.capMultiple, 0, 100, dbo.investor.capMultiple, "bailout.investor.capMultiple", problems);
            CheckOrigin(ref bo.investor.expenseOrigin, dbo.investor.expenseOrigin, "bailout.investor.expenseOrigin", problems);
            Check(ref bo.reputationDelta, -100, 100, dbo.reputationDelta, "bailout.reputationDelta", problems);

            Check(ref assets.minLinesLeft, 1, 100, d.assets.minLinesLeft, "assets.minLinesLeft", problems);
        }

        // ---- checks ----

        private static T Missing<T>(string path, T replacement, List<string> problems)
        {
            problems.Add(path + " is missing, defaults used");
            return replacement;
        }

        private static void Check(ref double value, double min, double max, double fallback, string path, List<string> problems)
        {
            if (!double.IsNaN(value) && !double.IsInfinity(value) && value >= min && value <= max) return;
            problems.Add(path + " out of range, default " + Fmt(fallback) + " used");
            value = fallback;
        }

        private static void Check(ref int value, int min, int max, int fallback, string path, List<string> problems)
        {
            if (value >= min && value <= max) return;
            problems.Add(path + " out of range, default " + Fmt(fallback) + " used");
            value = fallback;
        }

        // An array per difficulty level: all or nothing (a wrong length or one bad value rejects the whole array).
        private static void CheckArray(ref double[] value, double min, double max, double[] fallback, string path, List<string> problems)
        {
            string why = null;
            if (value == null)
            {
                why = "is missing";
            }
            else if (value.Length != DifficultyLevels)
            {
                why = "needs exactly " + DifficultyLevels.ToString(CultureInfo.InvariantCulture) + " values, got " + value.Length.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                for (int i = 0; i < value.Length && why == null; i++)
                {
                    double v = value[i];
                    if (double.IsNaN(v) || double.IsInfinity(v) || v < min || v > max)
                        why = "value " + i.ToString(CultureInfo.InvariantCulture) + " is out of range";
                }
            }
            if (why == null) return;

            var text = new StringBuilder();
            for (int i = 0; i < fallback.Length; i++)
            {
                if (i > 0) text.Append(", ");
                text.Append(Fmt(fallback[i]));
            }
            problems.Add(path + " " + why + ", default [" + text + "] used");
            value = (double[])fallback.Clone();
        }

        private static void CheckArray(ref int[] value, int min, int max, int[] fallback, string path, List<string> problems)
        {
            string why = null;
            if (value == null)
            {
                why = "is missing";
            }
            else if (value.Length != DifficultyLevels)
            {
                why = "needs exactly " + DifficultyLevels.ToString(CultureInfo.InvariantCulture) + " values, got " + value.Length.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                for (int i = 0; i < value.Length && why == null; i++)
                {
                    if (value[i] < min || value[i] > max) why = "value " + i.ToString(CultureInfo.InvariantCulture) + " is out of range";
                }
            }
            if (why == null) return;

            problems.Add(path + " " + why + ", default [" + string.Join(", ", Array.ConvertAll(fallback, f => Fmt(f))) + "] used");
            value = (int[])fallback.Clone();
        }

        private static void CheckOrigin(ref string value, string fallback, string path, List<string> problems)
        {
            if (value != null)
            {
                foreach (string known in ExpenseOrigins)
                {
                    if (!string.Equals(value, known, StringComparison.OrdinalIgnoreCase)) continue;
                    value = known;   // the spelling of the table
                    return;
                }
            }
            problems.Add(path + " must be " + string.Join(" or ", new List<string>(ExpenseOrigins).ToArray()) + ", default " + fallback + " used");
            value = fallback;
        }

        // 0.3 -> "0.30", 0.125 -> "0.125", 2000000 -> "2000000": how the problem lines show a default.
        private static string Fmt(double value)
        {
            if (value == Math.Floor(value) && Math.Abs(value) < 1e15) return value.ToString("0", CultureInfo.InvariantCulture);
            return value.ToString("0.00#", CultureInfo.InvariantCulture);
        }

        // "$.bridgeLoan.annualRate: expected number, got string" -> "bridgeLoan.annualRate: expected number, got string"
        private static string StripRoot(string error)
        {
            if (error.StartsWith("$.", StringComparison.Ordinal)) return error.Substring(2);
            if (error.StartsWith("$", StringComparison.Ordinal)) return "balance" + error.Substring(1);
            return error;
        }
    }
}
