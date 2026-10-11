using System;
using System.Globalization;
using PTMod.Core;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>What <see cref="FlawRollEngine"/> did with a released CPU.</summary>
    public enum FlawRollOutcome
    {
        /// <summary>The roll was made and failed: the CPU has no flaw.</summary>
        NoFlaw,

        /// <summary>The roll was made and a hidden record was created.</summary>
        FlawCreated,

        /// <summary>A variant was added to the record of its base CPU.</summary>
        VariantAttached,

        /// <summary>A variant of a CPU that has no (open) record: it counts as sound and gets none.</summary>
        VariantWithoutRecord,

        /// <summary>The new game's fast-forward: nothing is rolled (the owner's policy: the fast-forward is neutral).</summary>
        SkippedFastForward,

        /// <summary>The CPU was rolled before (<c>rolledCpuIds</c>, FR-1 item 5).</summary>
        SkippedAlreadyRolled,

        /// <summary>The CPU is an AI one and <c>aiFlaws</c> is off.</summary>
        SkippedAiOff,

        /// <summary>The save of the feature is held back (a newer mod wrote it): no flaws are started for this save.</summary>
        SkippedLocked,
    }

    /// <summary>The settings, numbers and clock the roll needs; <c>FlawRoller</c> is the game's implementation, a test brings a fake.</summary>
    public interface IFlawRollHost
    {
        /// <summary>The numbers of the feature.</summary>
        FlawBalance Balance { get; }

        /// <summary>The random stream of the roll (<c>ModRandom.Stream("design-flaw-recalls/roll")</c>).</summary>
        IModRng Rng { get; }

        /// <summary>The <c>flawFrequency</c> setting (the multiplier of the probability).</summary>
        float FlawFrequency { get; }

        /// <summary>The <c>aiFlaws</c> setting: AI companies get hidden flaws.</summary>
        bool AiFlawsEnabled { get; }

        /// <summary>The save of the feature is locked (written by a newer mod).</summary>
        bool SaveLocked { get; }

        /// <summary>Today as a day number (days from 1971-01-01).</summary>
        int Today { get; }

        /// <summary>The inflation now (stored in the record: the quote brings the development cost to the prices of the day).</summary>
        float Inflation { get; }

        /// <summary>A line for the log.</summary>
        void Log(string message);
    }

    /// <summary>
    /// The roll at the release of a CPU (FR-1, design.md Component 3 and the data flow "roll at the release"): the guards, the probability, the chance,
    /// the severity and the type, the creation of the record, the attaching of a variant to the record of its base CPU. Pure: what the game gives (the
    /// numbers of the design) arrives through a function that is called only when a roll is really made, and the settings and the clock through
    /// <see cref="IFlawRollHost"/>. <c>FlawRoller</c> wraps it for <c>ModEvents.CpuReleased</c>.
    /// </summary>
    public sealed class FlawRollEngine
    {
        private readonly FlawStore _store;
        private readonly IFlawRollHost _host;

        public FlawRollEngine(FlawStore store, IFlawRollHost host)
        {
            if (store == null) throw new ArgumentNullException("store");
            if (host == null) throw new ArgumentNullException("host");
            _store = store;
            _host = host;
        }

        /// <summary>
        /// Handles the release of one CPU. The guards, in order: the fast-forward of a new game (no record, no roll), a save locked by a newer mod,
        /// a CPU that was rolled before; a variant is attached to the record of its base CPU and never rolls itself; an AI CPU rolls only with
        /// <c>aiFlaws</c>. The roll logs <c>roll cpu=&lt;id&gt; p=&lt;p&gt; result=&lt;bool&gt;</c>.
        /// </summary>
        /// <param name="companyKey">The key of the owner (<c>ModCompanies.KeyOf</c>).</param>
        /// <param name="isAi">The owner is an AI company.</param>
        /// <param name="cpuId"><c>SaveID</c> of the released CPU.</param>
        /// <param name="baseCpuId"><c>SaveID</c> of <c>Cpu.Base</c> for a variant; ignored otherwise.</param>
        /// <param name="isVariant"><c>Cpu.Base != null</c>.</param>
        /// <param name="duringFastForward">The new game's fast-forward runs.</param>
        /// <param name="readRisk">Reads the numbers of the design from the game; called only when a roll is made.</param>
        public FlawRollOutcome OnRelease(string companyKey, bool isAi, int cpuId, int baseCpuId, bool isVariant, bool duringFastForward,
            Func<FlawRiskInput> readRisk)
        {
            if (duringFastForward) return FlawRollOutcome.SkippedFastForward;
            if (_host.SaveLocked) return FlawRollOutcome.SkippedLocked;
            if (_store.WasRolled(cpuId)) return FlawRollOutcome.SkippedAlreadyRolled;

            if (isVariant) return AttachVariant(cpuId, baseCpuId);
            if (isAi && !_host.AiFlawsEnabled) return FlawRollOutcome.SkippedAiOff;
            if (readRisk == null) throw new ArgumentNullException("readRisk");

            FlawBalance b = _host.Balance;
            FlawRiskInput input = readRisk();
            input.IsAi = isAi;
            input.Rush = !isAi && _store.IsRush(cpuId);

            float p = FlawRiskModel.Probability(input, b, _host.FlawFrequency);
            IModRng rng = _host.Rng;
            bool flawed = rng.Chance(p);
            _host.Log("roll cpu=" + cpuId.ToString(CultureInfo.InvariantCulture) + " p=" + p.ToString("0.000", CultureInfo.InvariantCulture)
                      + " result=" + (flawed ? "true" : "false"));

            _store.MarkRolled(cpuId);
            if (!flawed) return FlawRollOutcome.NoFlaw;

            FlawSeverity severity = FlawRiskModel.PickSeverity(p, b, rng);
            FlawTypeDef type = FlawRiskModel.PickType(FlawRiskModel.Factors(input, b), input.Year, b.Types, rng);
            FlawRecord record = _store.Create(companyKey, cpuId, type, severity, p, _host.Today, _host.Inflation);
            return record == null ? FlawRollOutcome.NoFlaw : FlawRollOutcome.FlawCreated;
        }

        /// <summary>A variant shares the die of its base CPU: it joins the record of the base CPU and never rolls (FR-1 item 3).</summary>
        private FlawRollOutcome AttachVariant(int variantId, int baseCpuId)
        {
            _store.MarkRolled(variantId);

            FlawRecord record = _store.FindByCpu(baseCpuId);
            if (record == null || !_store.AttachVariant(record, variantId)) return FlawRollOutcome.VariantWithoutRecord;
            return FlawRollOutcome.VariantAttached;
        }
    }
}
