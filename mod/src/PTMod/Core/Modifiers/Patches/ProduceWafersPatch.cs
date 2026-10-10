using System;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameFactory = ProcessorTycoon.Production.Factory;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Core
{
    /// <summary>
    /// P2: <c>Factory.ProduceWafers(Cpu, bool, bool, int, float)</c>, prefix + postfix + finalizer (FR-12, D5): the one patch behind three
    /// channels, <c>production.unitCost</c>, <c>cpu.failureRate</c> and <c>production.output</c>. The game computes one CPU's day of production in
    /// that method (lines → dies → failed dies → cost → <c>Stock</c>), so the patch does not rewrite the formula: it lends the game other inputs for
    /// the length of the call and gives them back.
    /// <list type="bullet">
    /// <item><b>Prefix</b> (<c>Priority.Last</c>: after the prefixes of anyone else). Asks the channels first, then saves the <c>UnitCost</c> and
    /// <c>FailureRate</c> it is about to replace, and the CPU's <c>ProducedLastDay</c> (for the output), in the <c>__state</c> struct (no
    /// allocation), and puts <c>UnitCost × M(production.unitCost, factory owner, cpu)</c> and <c>clamp(FailureRate × M(cpu.failureRate, the CPU's
    /// company, cpu), 0, 0.95)</c> in their place. The ceiling 0.95 is what keeps a stack of "hidden defect" entries from stopping the
    /// production altogether (AC-12.3). A value is touched only when its multiplier is not 1 - a CPU with nothing registered keeps the game's own
    /// numbers bit for bit. A prefix of someone else that skips the method leaves the state empty, and so the postfix does nothing.</item>
    /// <item><b>Postfix</b> (<c>Priority.First</c>, so that a patch of someone else behind it finds the game's own <c>UnitCost</c> and
    /// <c>FailureRate</c> and the finished day's output). First gives the values back (the saved numbers go into the property again, so they never
    /// reach a save: D5), then
    /// adds <c>round(Δproduced × (M(production.output, factory owner, cpu) − 1))</c> to <c>Market.Stock</c> (<c>CpuBase.MarketData</c>, the
    /// <c>Market</c> property of the CPU) and to <c>ProducedLastDay</c>, both held to [0, 2e9] like the game's own clamp of the stock. <c>Δproduced</c>
    /// is the rise of <c>ProducedLastDay</c> in this call, i.e. the good dies after the failed ones. The cost is not touched - the game
    /// charged it from the gross number of dies - and neither is <c>ProductionMath.DiesPerWafer</c>, on which <c>RequiredProductionLines</c> loops.</item>
    /// <item><b>Finalizer</b>. Gives the values back also when the game's method (or another patch) threw, so a failed call can never leave the
    /// mod's number in a property the save writes. It is a no-op when the postfix has already done it, and returns nothing: the game's
    /// own exception, if there is one, goes on as it would without the mod.</item>
    /// <item><b>What it does not change.</b> <c>Cpu.RealUnitCost</c> (the cost the player and the AI see and price against) is computed from the
    /// stored <c>UnitCost</c> outside this call, so it stays the game's: <c>production.unitCost</c> moves the money actually spent and nothing
    /// else (FR-12 item 19). The reaction of the market to raw materials goes through <c>production.memoryCost</c> / <c>production.waferCost</c>.</item>
    /// <item><b>Fast path.</b> With nothing registered on the three channels the prefix is one integer compare against the registry version, and
    /// the postfix and finalizer see an empty state and return: no lookup, no allocation, far under the 2 µs of the budget (NFR-1; an offline run
    /// of the game's own method measured about 0.03 µs on top of it and no extra bytes). With records the cost is up to three cached channel reads
    /// per call (the value of the day, per company and CPU) - the method runs about twice a day per CPU.</item>
    /// <item><b>Failure</b> is the core's unit <c>P2</c> (<c>P2/prefix</c>, <c>P2/postfix</c>, <c>P2/finalizer</c>): an exception in the mod's part
    /// is counted, the game's numbers stay (or are given back at once), and after <c>hooks.faultThreshold</c> failures the unit stops
    /// answering until the game restarts. Giving the values back never waits for that switch: it is the one thing the patch must do even when it
    /// is off.</item>
    /// </list>
    /// To check in the game (the owner does this with <c>framework-sample</c>, TSK-053; nothing here is run without the game): with entries on the
    /// player's company, <c>cpu.failureRate ×3</c> on a CPU whose <c>FailureRate</c> is 0.4 shows failed dies at 95 % of the day's output (the failed
    /// stock and the "failed" column grow), while the CPU's failure rate in its window and in the save stays 0.4 (AC-12.3);
    /// <c>production.unitCost ×1.3</c> raises the factory's daily spending by about 30 % of the unit-cost part while the CPU's unit cost in the
    /// window does not move; <c>production.output ×0.5</c> halves the day's output and stock gain of that CPU (<c>ProducedLastDay</c> and the stock
    /// in the storage window), <c>×1.5</c> adds half; with the feature switched off everything is the game's again and the entries are kept.
    /// In <c>Player.log</c>: <c>hooks: ... P2</c> applied (three patches), no <c>P2/prefix</c>, <c>P2/postfix</c> or <c>P2/finalizer</c> faults.
    /// </summary>
    internal static class ProduceWafersPatch
    {
        private const string CoreId = ModLog.CoreFeatureId;

        // The unit of the breaker is the part before the first '/'.
        private const string PrefixSite = "P2/prefix";
        private const string PostfixSite = "P2/postfix";
        private const string FinalizerSite = "P2/finalizer";

        // Harmony's Priority.Last and Priority.First. The prefix goes after everybody's prefixes (it lends the values for the original method only);
        // the postfix and the finalizer go first, so that a patch of anyone else behind them sees the game's own UnitCost and FailureRate.
        private const int PrefixPriority = 0;
        private const int GivingBackPriority = 800;

        // Which channels have something registered (a bit each), refreshed only when the registry changed.
        private const int UnitCostBit = 1;
        private const int FailureRateBit = 2;
        private const int OutputBit = 4;

        private static int _channelsVersion = -1;
        private static int _channels;

        /// <summary>
        /// What the prefix hands to the postfix and the finalizer (Harmony's <c>__state</c>, one local of the patched method - a struct, so nothing is
        /// allocated). The default value means "the prefix did nothing": both later steps then return at once.
        /// </summary>
        internal struct ProduceState
        {
            /// <summary><see cref="UnitCost"/> was replaced and has to be given back.</summary>
            public bool UnitCostLent;

            /// <summary><see cref="FailureRate"/> was replaced and has to be given back.</summary>
            public bool FailureRateLent;

            /// <summary><c>production.output</c> has records: the postfix measures the day's output and may add to it.</summary>
            public bool Output;

            public float UnitCost;
            public float FailureRate;
            public int ProducedLastDay;

            /// <summary>The prefix or the postfix has something left to do.</summary>
            public bool IsEmpty
            {
                get { return !UnitCostLent && !FailureRateLent && !Output; }
            }
        }

        [ModPatch(typeof(GameFactory), "ProduceWafers", PatchKind.Prefix, Priority = PrefixPriority,
            ArgTypes = new[] { typeof(GameCpu), typeof(bool), typeof(bool), typeof(int), typeof(float) })]
        private static void Prefix(GameFactory __instance, GameCpu cpu, out ProduceState __state)
        {
            __state = default(ProduceState);
            if (ReferenceEquals(cpu, null)) return;

            int channels = ActiveChannels();
            if (channels == 0) return;                              // nothing registered on the three channels: the game's call as it is
            if (!ModHooks.CanRun(CoreId, PrefixSite)) return;

            try
            {
                // Ask first, change after: a failure in a provider leaves the CPU exactly as the game has it.
                float unitCostMultiplier = 1f, failureRateMultiplier = 1f;
                if ((channels & UnitCostBit) != 0) unitCostMultiplier = Ask(ModChannels.ProductionUnitCost, __instance.Company, cpu);
                if ((channels & FailureRateBit) != 0) failureRateMultiplier = Ask(ModChannels.CpuFailureRate, cpu.Company, cpu);

                float scaled;
                if (ProduceWafersMath.TryScaleUnitCost(cpu.UnitCost, unitCostMultiplier, out scaled))
                {
                    __state.UnitCost = cpu.UnitCost;
                    __state.UnitCostLent = true;
                    cpu.UnitCost = scaled;
                }
                if (ProduceWafersMath.TryScaleFailureRate(cpu.FailureRate, failureRateMultiplier, out scaled))
                {
                    __state.FailureRate = cpu.FailureRate;
                    __state.FailureRateLent = true;
                    cpu.FailureRate = scaled;
                }
                if ((channels & OutputBit) != 0)
                {
                    __state.Output = true;
                    __state.ProducedLastDay = cpu.ProducedLastDay;
                }
            }
            catch (Exception ex)
            {
                GiveBack(cpu, ref __state);
                __state = default(ProduceState);
                ModHooks.ReportFault(CoreId, PrefixSite, ex);
            }
        }

        [ModPatch(typeof(GameFactory), "ProduceWafers", PatchKind.Postfix, Priority = GivingBackPriority,
            ArgTypes = new[] { typeof(GameCpu), typeof(bool), typeof(bool), typeof(int), typeof(float) })]
        private static void Postfix(GameFactory __instance, GameCpu cpu, ref ProduceState __state)
        {
            if (__state.IsEmpty) return;                            // the vanilla path (also when a prefix of someone else skipped the method)

            // The values go back before anything else, and without asking whether the unit is still on: a CPU must never keep the mod's number.
            GiveBack(cpu, ref __state);
            if (!__state.Output) return;
            __state.Output = false;
            if (!ModHooks.CanRun(CoreId, PostfixSite)) return;

            try
            {
                AddOutput(__instance, cpu, __state.ProducedLastDay);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, PostfixSite, ex);
            }
        }

        [ModPatch(typeof(GameFactory), "ProduceWafers", PatchKind.Finalizer, Priority = GivingBackPriority,
            ArgTypes = new[] { typeof(GameCpu), typeof(bool), typeof(bool), typeof(int), typeof(float) })]
        private static void Finalizer(GameCpu cpu, ref ProduceState __state)
        {
            if (!__state.UnitCostLent && !__state.FailureRateLent) return;   // the postfix gave them back, or the prefix never changed them

            try
            {
                GiveBack(cpu, ref __state);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, FinalizerSite, ex);
            }
        }

        // ---- the parts ----

        /// <summary>The bit set of the three channels that have an entry or a provider; rebuilt only when the registry's version moved.</summary>
        private static int ActiveChannels()
        {
            var registry = ModModifiers.Registry;
            int version = registry.Version;
            if (_channelsVersion != version)
            {
                _channels = (registry.HasAny(ModChannels.ProductionUnitCost) ? UnitCostBit : 0)
                            | (registry.HasAny(ModChannels.CpuFailureRate) ? FailureRateBit : 0)
                            | (registry.HasAny(ModChannels.ProductionOutput) ? OutputBit : 0);
                _channelsVersion = version;
            }
            return _channels;
        }

        /// <summary>The channel's multiplier for a company and a CPU. A failure throws: the caller counts it at its own site.</summary>
        private static float Ask(string channel, ICompany company, GameCpu cpu)
        {
            return ModModifiers.Evaluate(channel, new ModifierContext { Company = company, Cpu = cpu });
        }

        /// <summary>Puts the saved <c>UnitCost</c> and <c>FailureRate</c> back into the CPU and forgets that they were lent. Plain assignments: it does not fail.</summary>
        private static void GiveBack(GameCpu cpu, ref ProduceState state)
        {
            if (state.UnitCostLent)
            {
                cpu.UnitCost = state.UnitCost;
                state.UnitCostLent = false;
            }
            if (state.FailureRateLent)
            {
                cpu.FailureRate = state.FailureRate;
                state.FailureRateLent = false;
            }
        }

        /// <summary><c>production.output</c>: the extra (or missing) dies of this call go to the stock and to the day's produced count.</summary>
        private static void AddOutput(GameFactory factory, GameCpu cpu, int producedBefore)
        {
            int produced = cpu.ProducedLastDay - producedBefore;
            if (produced <= 0) return;                              // the method returned early: no line was allocated

            float multiplier = Ask(ModChannels.ProductionOutput, factory.Company, cpu);
            int extra = ProduceWafersMath.ExtraOutput(produced, multiplier);
            if (extra == 0) return;

            // Both new values first, then both assignments.
            int newProduced = ProduceWafersMath.AddWithinLimit(cpu.ProducedLastDay, extra);
            int newStock = ProduceWafersMath.AddWithinLimit(cpu.Market.Stock, extra);
            cpu.ProducedLastDay = newProduced;
            cpu.Market.Stock = newStock;
        }
    }

    /// <summary>
    /// The numbers of patch P2, without the game: how a multiplier becomes the value lent to <c>Factory.ProduceWafers</c> and the extra output.
    /// Every method is a pure function of its arguments.
    /// </summary>
    internal static class ProduceWafersMath
    {
        /// <summary>
        /// The most the failure rate may be during the call (AC-12.3): above it a stack of defect entries would leave no good die and the
        /// production of the CPU would stop.
        /// </summary>
        internal const float FailureRateCap = 0.95f;

        /// <summary>The game's own ceiling of <c>Market.Stock</c> and <c>FailedStock</c> in <c>ProduceWafers</c>; the floor is 0.</summary>
        internal const int CountLimit = 2000000000;

        /// <summary>
        /// <c>unitCost × multiplier</c>. False (and <paramref name="scaled"/> = <paramref name="unitCost"/>) when the game's value stands: the multiplier
        /// is 1, or the product is not a usable cost (not a number, infinite, negative).
        /// </summary>
        internal static bool TryScaleUnitCost(float unitCost, float multiplier, out float scaled)
        {
            scaled = unitCost;
            if (multiplier == 1f) return false;

            float value = unitCost * multiplier;
            if (IsNotFinite(value) || value < 0f) return false;
            scaled = value;
            return true;
        }

        /// <summary>
        /// <c>clamp(rate × multiplier, 0, <see cref="FailureRateCap"/>)</c>. False (and <paramref name="scaled"/> = <paramref name="rate"/>) when the game's
        /// value stands: the multiplier is 1, the rate or the multiplier is not a finite number, or the result equals the rate.
        /// </summary>
        internal static bool TryScaleFailureRate(float rate, float multiplier, out float scaled)
        {
            scaled = rate;
            if (multiplier == 1f || IsNotFinite(rate) || IsNotFinite(multiplier)) return false;

            float value = rate * multiplier;
            if (IsNotFinite(value)) return false;     // the product of two finite floats overflowing: not a usable rate
            if (value < 0f) value = 0f;
            else if (value > FailureRateCap) value = FailureRateCap;
            if (value == rate) return false;
            scaled = value;
            return true;
        }

        /// <summary>
        /// <c>round(produced × (multiplier − 1))</c>: how many dies the channel adds (positive) or takes away (negative) from a call that produced
        /// <paramref name="produced"/> good ones. Rounded like the game's own <c>Mathf.RoundToInt</c> (halves to even). 0 for a multiplier of 1,
        /// a multiplier that is not a number, or nothing produced.
        /// </summary>
        internal static int ExtraOutput(int produced, float multiplier)
        {
            if (produced <= 0 || multiplier == 1f || IsNotFinite(multiplier)) return 0;

            double extra = Math.Round((double)produced * ((double)multiplier - 1.0));
            if (extra >= int.MaxValue) return int.MaxValue;
            if (extra <= int.MinValue) return int.MinValue;
            return (int)extra;
        }

        /// <summary><c>value + extra</c> held to [0, <see cref="CountLimit"/>]; the sum is made in 64 bits, so it cannot wrap.</summary>
        internal static int AddWithinLimit(int value, int extra)
        {
            long sum = (long)value + extra;
            if (sum < 0) return 0;
            if (sum > CountLimit) return CountLimit;
            return (int)sum;
        }

        private static bool IsNotFinite(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value);
        }
    }
}
