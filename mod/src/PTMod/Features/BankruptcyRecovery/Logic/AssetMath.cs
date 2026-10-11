using System;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    /// <summary>
    /// The small rules behind the asset sale (FR-3, FR-4, FR-5; design.md <c>AssetSaleService</c>): how many production lines may be sold, when the sale of the
    /// whole production is open, the margin the CPU list is ordered by, and the warning about the contracts. They repeat what the game's own window of the
    /// production lines does (<c>ManageLinesWindow.UpdateSlider</c> and <c>UpdateUI</c>), because the feature sells at the game's prices and by the game's
    /// rules and adds none of its own. The prices (<c>ProductionMath.SaleCost</c>, <c>StorageMath.Income</c>) are the game's and are asked on its side.
    /// </summary>
    internal static class AssetMath
    {
        /// <summary>No line of this size can be sold: the player has none or the last line would go.</summary>
        public const string ReasonMinLines = "rec.lines.minOne";

        /// <summary>The player has no line of that size to sell.</summary>
        public const string ReasonNoLines = "rec.lines.none";

        /// <summary>The division cannot be sold: the company is a foundry.</summary>
        public const string ReasonFoundry = "rec.division.foundry";

        /// <summary>The division cannot be sold: there are no production lines.</summary>
        public const string ReasonNoProduction = "rec.division.noLines";

        /// <summary>The division cannot be sold on the hardest difficulty ("impossible", level 5).</summary>
        public const string ReasonImpossible = "rec.division.impossible";

        /// <summary>The hardest difficulty level: the game does not let the player sell the division.</summary>
        public const int HardestDifficulty = 5;

        /// <summary>
        /// How many lines of one wafer size the player may sell: all of them, but at least <paramref name="minLinesLeft"/> lines must remain in the whole
        /// factory (the game keeps 1: <c>ManageLinesWindow.UpdateSlider</c> takes one off when the sale would empty the factory). 0 for a company without
        /// lines of production (a fabless one, <c>UpdateSlider</c> sets the slider to 0).
        /// </summary>
        /// <param name="linesOfWafer">The lines of that wafer size (<c>Factory.AmountOfLines</c>).</param>
        /// <param name="totalCapacity">All lines of the factory (<c>Factory.ProductionCapacity</c>).</param>
        /// <param name="minLinesLeft">The lines that must stay (<c>assets.minLinesLeft</c>, 1 or more).</param>
        /// <param name="isFabless">The company has no production of its own.</param>
        public static int MaxSellableLines(int linesOfWafer, int totalCapacity, int minLinesLeft, bool isFabless)
        {
            if (isFabless || linesOfWafer <= 0 || totalCapacity <= 0) return 0;
            int keep = minLinesLeft < 1 ? 1 : minLinesLeft;
            long room = (long)totalCapacity - keep;   // lines that may go in the whole factory
            if (room <= 0) return 0;
            return (int)Math.Min(linesOfWafer, room);
        }

        /// <summary>Null when something can be sold, otherwise the key of the reason (the Assets tab shows it under the button).</summary>
        public static string LinesBlock(int linesOfWafer, int maxSellable)
        {
            if (maxSellable > 0) return null;
            return linesOfWafer <= 0 ? ReasonNoLines : ReasonMinLines;
        }

        /// <summary>The number of lines to sell, held between 0 and what may be sold.</summary>
        public static int ClampLines(int wanted, int maxSellable)
        {
            if (maxSellable <= 0 || wanted <= 0) return 0;
            return wanted > maxSellable ? maxSellable : wanted;
        }

        /// <summary>The capacity left after a sale (never below 0).</summary>
        public static int CapacityAfter(int capacity, int sold)
        {
            long left = (long)capacity - Math.Max(0, sold);
            return left < 0L ? 0 : (int)left;
        }

        /// <summary>
        /// The capacity after the sale is smaller than the lines the active OEM contracts need (FR-3 item 4): the player may miss deliveries and pay fines.
        /// </summary>
        public static bool ContractsAtRisk(int capacityAfter, int contractedLines)
        {
            return contractedLines > 0 && capacityAfter < contractedLines;
        }

        /// <summary>
        /// Null when the sale of the whole production is open, otherwise the key of the reason (FR-5 item 2): the same conditions as the game's button
        /// <c>sellDivisionButton</c> in <c>ManageLinesWindow.UpdateUI</c>.
        /// </summary>
        public static string DivisionBlock(bool isFoundry, int productionCapacity, int difficulty)
        {
            if (difficulty == HardestDifficulty) return ReasonImpossible;
            if (productionCapacity < 1) return ReasonNoProduction;
            if (isFoundry) return ReasonFoundry;
            return null;
        }

        /// <summary>The daily margin of a CPU, <c>(price − real unit cost) × sold yesterday</c> (FR-4 item 4); 0 for a number that is not finite.</summary>
        public static double DailyMargin(double price, double realUnitCost, int soldLastDay)
        {
            double margin = (price - realUnitCost) * soldLastDay;
            return double.IsNaN(margin) || double.IsInfinity(margin) ? 0.0 : margin;
        }
    }
}
