using System;
using System.Runtime.CompilerServices;
using PTMod.Core.Logic.Segments;
using Cpu = ProcessorTycoon.Hardware.Cpu;
using GameMarket = ProcessorTycoon.MarketSystem.Market;

namespace PTMod.Core
{
    /// <summary>
    /// The market segment of a CPU (FR-23 items 6-7): the single classifier of the mod. Every feature that needs "which segment is this CPU
    /// in" calls this class instead of keeping a rule of its own (<c>rival-personalities</c> and <c>press-reviews</c> had two that disagreed).
    /// <list type="bullet">
    /// <item><b>By price.</b> <see cref="Classify(Cpu)"/> and <see cref="Classify(float)"/> compare the price with the current budgets of the
    /// game's market (<c>Market.MidRangeBudget</c>, <c>LowEndBudget</c>, <c>IndustriesBudget</c>) times the band <c>segments.priceBand</c> of
    /// <c>core.json</c> (1.25): above the mid-range budget is HighEnd, above the low-end budget MidRange, above the industries budget LowEnd,
    /// the rest Industries (the rule is <c>SegmentRule.ByPrice</c>). The budgets move with the years, so the answer for one price changes over
    /// time; the same CPU can be MidRange one year and LowEnd later. <see cref="Segment.Any"/> when the market does not exist yet
    /// (<c>Market.Instance</c> is null: the menu, a scene that is loading), the CPU is null or the price is not a finite positive number.</item>
    /// <item><b>By sales.</b> <see cref="FromSales"/> and <see cref="DominantBySales"/> give the segment where a CPU sold most (a tie goes to
    /// the smaller index, nothing sold gives <see cref="Segment.Any"/>).</item>
    /// </list>
    /// The game's classes are touched only in the small methods at the end (no inlining); when they do not match the game the error is
    /// logged once and every price-based answer is <see cref="Segment.Any"/>. Main thread only. Never throws.
    /// </summary>
    public static class ModSegments
    {
        private const string CoreId = ModLog.CoreFeatureId;

        private static float _band;          // 0 until read from core.json
        private static bool _gameBroken;     // the game's market does not match the mod: Any for prices, until restart

        /// <summary>
        /// The band above a budget that still counts as that segment (<c>segments.priceBand</c> of <c>core.json</c>, 1.25; a missing,
        /// non-finite or non-positive value gives the default). Read on first use.
        /// </summary>
        public static float PriceBand
        {
            get
            {
                float band = _band;
                if (band > 0f) return band;

                double read = SegmentRule.DefaultBand;
                try
                {
                    read = ModContent.GetCoreNumber("segments", "priceBand", SegmentRule.DefaultBand);
                }
                catch (Exception)
                {
                    // ModContent logs its own problems and does not throw; the default stands.
                }
                band = read > 0 && !double.IsNaN(read) && !double.IsInfinity(read) && read < float.MaxValue ? (float)read : SegmentRule.DefaultBand;
                _band = band;
                return band;
            }
        }

        /// <summary>The segment of <paramref name="cpu"/> by its price (<c>Cpu.Price</c>); <see cref="Segment.Any"/> for a null CPU or when it cannot be said.</summary>
        public static Segment Classify(Cpu cpu)
        {
            if (ReferenceEquals(cpu, null)) return Segment.Any;

            try
            {
                return Classify(cpu.Price);
            }
            catch (Exception ex)
            {
                GameFailed("reading the price of a CPU", ex);
                return Segment.Any;
            }
        }

        /// <summary>
        /// The segment of a CPU sold at <paramref name="price"/> by the current budgets of the market and the band
        /// (<see cref="PriceBand"/>): HighEnd above <c>MidRangeBudget × band</c>, MidRange above <c>LowEndBudget × band</c>, LowEnd above
        /// <c>IndustriesBudget × band</c>, otherwise Industries. <see cref="Segment.Any"/> when <c>Market.Instance</c> is null, the price is
        /// not a finite positive number or the market cannot be read.
        /// </summary>
        public static Segment Classify(float price)
        {
            if (_gameBroken) return Segment.Any;
            if (!(price > 0f) || float.IsInfinity(price)) return Segment.Any;   // NaN, zero, negative, infinite: no segment, and no need to ask the market

            try
            {
                float mid, low, industries;
                if (!ReadBudgets(out mid, out low, out industries)) return Segment.Any;
                return SegmentRule.ByPrice(price, mid, low, industries, PriceBand);
            }
            catch (Exception ex) when (ex is NullReferenceException || ex is ArgumentOutOfRangeException)
            {
                return Segment.Any;   // the market exists but its populations are not set up yet (between Awake and Start)
            }
            catch (Exception ex)
            {
                GameFailed("reading the budgets of Market", ex);
                return Segment.Any;
            }
        }

        /// <summary>The segment with the most sales (a tie goes to the smaller index: HighEnd, MidRange, LowEnd, Industries); nothing sold gives <see cref="Segment.Any"/>.</summary>
        public static Segment FromSales(int soldHighEnd, int soldMidRange, int soldLowEnd, int soldIndustries)
        {
            return SegmentRule.FromSales(soldHighEnd, soldMidRange, soldLowEnd, soldIndustries);
        }

        /// <summary>The segment where <paramref name="cpu"/> sold most yesterday (<c>SoldLastDayBy*</c>); <see cref="Segment.Any"/> for a null CPU or when nothing was sold.</summary>
        public static Segment DominantBySales(Cpu cpu)
        {
            if (ReferenceEquals(cpu, null)) return Segment.Any;

            try
            {
                return SegmentRule.FromSales(cpu.SoldLastDayByHighEnd, cpu.SoldLastDayByMidRange, cpu.SoldLastDayByLowEnd, cpu.SoldLastDayByIndustries);
            }
            catch (Exception ex)
            {
                GameFailed("reading the sales of a CPU", ex);
                return Segment.Any;
            }
        }

        /// <summary>A read of the game's state failed: logged once (the log limits repeats), a mismatch of the game's classes switches the price rule off.</summary>
        private static void GameFailed(string what, Exception ex)
        {
            if (ex is MissingMemberException || ex is TypeLoadException || ex is MemberAccessException || ex is BadImageFormatException)
            {
                if (_gameBroken) return;
                _gameBroken = true;
                ModLog.Error(CoreId, "segments: " + what + " failed, the game's classes do not match the mod (another game version?); "
                                     + "Classify gives Segment.Any until the game restarts", ex);
                return;
            }
            ModLog.Error(CoreId, "segments: " + what + " failed; the answer is Segment.Any", ex);
        }

        // ---- the game's classes: touched only below; if they do not match the game, the JIT fails in the method and the caller catches it ----

        /// <summary>The budgets of the three lower populations; false when there is no market (<c>Market.Instance</c> is null).</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ReadBudgets(out float mid, out float low, out float industries)
        {
            mid = 0f;
            low = 0f;
            industries = 0f;

            GameMarket market = GameMarket.Instance;
            if (market == null) return false;

            mid = market.MidRangeBudget;
            low = market.LowEndBudget;
            industries = market.IndustriesBudget;
            return true;
        }
    }
}
