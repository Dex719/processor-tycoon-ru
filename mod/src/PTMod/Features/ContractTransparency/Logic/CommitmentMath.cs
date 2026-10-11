using System;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// What can go wrong with an offer (FR-3 item 3). The values are bits, so a report carries any combination; <see cref="ConfirmPolicy"/> asks whether
    /// there is any. The numbers behind each flag are in <see cref="CommitmentMath"/> and <c>rules.json</c>.
    /// </summary>
    [Flags]
    public enum RiskFlags
    {
        None = 0,

        /// <summary>The CPU needs more production lines than the factory has free now.</summary>
        CapacityShortNow = 1,

        /// <summary>The free lines would not be enough once the other pending offers are won.</summary>
        Overcommit = 2,

        /// <summary>Every month of the contract loses money.</summary>
        NegativeProfit = 4,

        /// <summary>The margin is below the threshold of the contract automation (<c>Player.ContractProfitMargin</c>).</summary>
        BelowAutomationMargin = 8,

        /// <summary>The actual probability of winning is below <c>risk.lowActualChance</c>.</summary>
        LowActualChance = 16,

        /// <summary>The fine for breaking the contract on day one is more than the company's cash (times <c>risk.breakFineVsCashRatio</c>).</summary>
        BreakFineExceedsCash = 32,

        /// <summary>The player's "cancel offers automatically" switch is on and the lines are short: the game will withdraw the offer on the review date.</summary>
        AutoRejectWillWithdraw = 64,
    }

    /// <summary>What <see cref="CommitmentMath.Evaluate"/> needs to know about one offer; the adapter fills it from the contract, the CPU and the company.</summary>
    public struct CommitmentInput
    {
        /// <summary><c>Contract.Units</c>: units per month.</summary>
        public int Units;

        /// <summary><c>Contract.PaymentPerUnit</c>.</summary>
        public float PaymentPerUnit;

        /// <summary><c>Contract.FinePerUnit</c> (the game sets it to twice the payment).</summary>
        public float FinePerUnit;

        /// <summary><c>Cpu.RealUnitCostWithNewPrice(PaymentPerUnit)</c>: what a unit costs the producer at this price.</summary>
        public float RealUnitCostAtPayment;

        /// <summary><c>ProductionMath.RequiredProductionLines(cpu, Units)</c>.</summary>
        public int RequiredLines;

        /// <summary><c>FactoryBase.ProductionCapacity</c> of the manufacturer.</summary>
        public int CapacityLines;

        /// <summary><c>ContractHelper.ContractedProductionLines(manufacturer.Owner)</c>: lines already taken by active contracts.</summary>
        public int ContractedLines;

        /// <summary><c>ContractHelper.FutureProductionLinesDemand(owner)</c> without this contract: lines promised in other pending offers.</summary>
        public int PendingOfferLines;

        /// <summary>Days from the review date to the end of the contract (<c>DateController.DaysUntil(Acceptance, Termination)</c>).</summary>
        public int TermDays;

        /// <summary>Months of the contract.</summary>
        public int TermMonths;

        /// <summary><c>Player.AutomateRejection</c>: the "cancel offers automatically" switch.</summary>
        public bool AutomateRejection;

        /// <summary><c>Player.ContractProfitMargin</c>, in percent (the game's default is 40).</summary>
        public int AutomationMarginPercent;

        /// <summary><c>ICompany.MoneyAmount</c> of the offering company.</summary>
        public float CompanyCash;

        /// <summary>The probability that the draw picks this offer, 0..1 (<see cref="OddsMath"/>).</summary>
        public double ActualProbability;
    }

    /// <summary>The numbers of an offer's commitments and the risks found in them.</summary>
    public struct CommitmentReport
    {
        /// <summary><c>Units × (PaymentPerUnit − RealUnitCostAtPayment)</c>: the profit of one month (negative for a loss).</summary>
        public double MonthlyProfit;

        /// <summary><see cref="MonthlyProfit"/> × the months of the contract.</summary>
        public double TermProfit;

        /// <summary><c>(Payment − UnitCost) / Payment</c>, computed in float like <c>ContractAutomation.SendOffers</c>; 0 for a payment of 0 or less.</summary>
        public double Margin;

        /// <summary>The lines the CPU needs for the contract (<c>ProductionMath.RequiredProductionLines</c>), as it was given.</summary>
        public int RequiredLines;

        /// <summary>The lines the factory has (<c>FactoryBase.ProductionCapacity</c>), as it was given.</summary>
        public int CapacityLines;

        /// <summary>The lines taken by active contracts, as it was given.</summary>
        public int ContractedLines;

        /// <summary>The lines promised in the other pending offers (never negative).</summary>
        public int PendingLines;

        /// <summary>Lines the factory has free now: capacity minus the lines of active contracts (negative when it is already overbooked).</summary>
        public int FreeLinesNow;

        /// <summary><see cref="FreeLinesNow"/> minus the lines promised in other pending offers.</summary>
        public int FreeLinesAfterPending;

        /// <summary>How many lines the CPU lacks now (0 when it fits).</summary>
        public int LinesShortNow;

        /// <summary><c>Units / 30</c>: the demand of one day.</summary>
        public double DailyUnits;

        /// <summary><c>(Units / 30) × D²</c>, D = <see cref="CommitmentInput.TermDays"/>: the fine for breaking the contract on its first day (<c>ContractHelper.FinesFromContract</c>).</summary>
        public double BreakFineAtStart;

        /// <summary><c>FinePerUnit × Units / 30</c>: the fine of one day if no unit is delivered.</summary>
        public double DailyShortfallFine;

        public RiskFlags Risks;
    }

    /// <summary>
    /// The obligations an offer takes on and the risks in them (FR-3; design.md formulas F6, F8-F11 and the list of risks). Pure arithmetic: the
    /// adapter reads the game, this class counts. Nothing here changes the game's rules; the thresholds come from <see cref="ContractRules"/>.
    /// </summary>
    public static class CommitmentMath
    {
        /// <summary>The game spreads the monthly volume over 30 days (<c>Units / 30</c>).</summary>
        public const double DaysPerMonth = 30.0;

        /// <summary>Counts the report of <paramref name="input"/> with the thresholds of <paramref name="rules"/> (the defaults when null).</summary>
        public static CommitmentReport Evaluate(CommitmentInput input, ContractRules rules)
        {
            if (rules == null) rules = ContractRules.Default;

            var report = new CommitmentReport();
            int units = Math.Max(0, input.Units);

            // Profit, F8.
            report.MonthlyProfit = units * ((double)input.PaymentPerUnit - input.RealUnitCostAtPayment);
            report.TermProfit = report.MonthlyProfit * Math.Max(0, input.TermMonths);

            // The margin is the game's own float expression: ContractAutomation compares it with ContractProfitMargin / 100f.
            float margin = input.PaymentPerUnit > 0f ? (input.PaymentPerUnit - input.RealUnitCostAtPayment) / input.PaymentPerUnit : 0f;
            report.Margin = margin;

            // Production lines, F6.
            int pending = Math.Max(0, input.PendingOfferLines);
            report.RequiredLines = input.RequiredLines;
            report.CapacityLines = input.CapacityLines;
            report.ContractedLines = input.ContractedLines;
            report.PendingLines = pending;
            report.FreeLinesNow = input.CapacityLines - input.ContractedLines;
            report.FreeLinesAfterPending = report.FreeLinesNow - pending;
            report.LinesShortNow = Math.Max(0, input.RequiredLines - report.FreeLinesNow);

            // Fines, F10 and F11.
            double termDays = Math.Max(0, input.TermDays);
            report.DailyUnits = units / DaysPerMonth;
            report.BreakFineAtStart = report.DailyUnits * termDays * termDays;
            report.DailyShortfallFine = input.FinePerUnit * report.DailyUnits;

            // Risks.
            RiskFlags risks = RiskFlags.None;
            bool capacityShort = input.RequiredLines > report.FreeLinesNow;
            if (capacityShort) risks |= RiskFlags.CapacityShortNow;
            if (input.RequiredLines > report.FreeLinesAfterPending) risks |= RiskFlags.Overcommit;
            if (report.MonthlyProfit < 0) risks |= RiskFlags.NegativeProfit;
            if (rules.UseAutomationMarginAsThreshold && margin < input.AutomationMarginPercent / 1e+02f) risks |= RiskFlags.BelowAutomationMargin;
            if (input.ActualProbability < rules.LowActualChance) risks |= RiskFlags.LowActualChance;
            if (report.BreakFineAtStart > 0 && report.BreakFineAtStart > rules.BreakFineVsCashRatio * input.CompanyCash) risks |= RiskFlags.BreakFineExceedsCash;
            if (input.AutomateRejection && capacityShort) risks |= RiskFlags.AutoRejectWillWithdraw;
            report.Risks = risks;
            return report;
        }
    }
}
