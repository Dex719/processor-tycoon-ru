using System;
using System.Globalization;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The three priorities of one designer of the AI (<c>AIBehaviourCreation</c>: the private fields <c>designFocus</c>,
    /// <c>powerConsumptionFocus</c>, <c>simulationPriority</c>, the arguments of <c>ChangePriorities</c>), as the numbers of the game's
    /// enumerations: <c>CpuDesignFocus</c> HighPerformance 0, LowUnitCost 1, Inlet 2, EMD 3; <c>PowerConsumptionFocus</c> Irrelevant 0,
    /// LowConsumption 1; <c>SimulationPriority</c> High 0, Normal 1, Low 2, SuperHigh 3.
    /// </summary>
    public struct CreatorPriorities : IEquatable<CreatorPriorities>
    {
        public int DesignFocus;
        public int PowerFocus;
        public int SimulationPriority;

        public CreatorPriorities(int designFocus, int powerFocus, int simulationPriority)
        {
            DesignFocus = designFocus;
            PowerFocus = powerFocus;
            SimulationPriority = simulationPriority;
        }

        public bool Equals(CreatorPriorities other)
        {
            return DesignFocus == other.DesignFocus && PowerFocus == other.PowerFocus && SimulationPriority == other.SimulationPriority;
        }

        public override bool Equals(object obj)
        {
            return obj is CreatorPriorities && Equals((CreatorPriorities)obj);
        }

        public override int GetHashCode()
        {
            return (DesignFocus * 31 + PowerFocus) * 31 + SimulationPriority;
        }

        /// <summary>For the log: <c>design=1 power=0 simulation=2</c>.</summary>
        public override string ToString()
        {
            return "design=" + DesignFocus.ToString(CultureInfo.InvariantCulture)
                   + " power=" + PowerFocus.ToString(CultureInfo.InvariantCulture)
                   + " simulation=" + SimulationPriority.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// What the character of a rival makes of the priorities of a designer (FR-3 item 4, AC-3.3): <c>designFocusOverride</c> and
    /// <c>powerFocusOverride</c> of the archetype on top of the designer's own (prefab) values. A designer whose own design focus is Inlet (2) or
    /// EMD (3) of the game's <c>CpuDesignFocus</c> keeps it. The simulation priority is never changed. Pure.
    /// </summary>
    public static class PriorityMath
    {
        /// <summary>The numbers of the game's <c>CpuDesignFocus</c>.</summary>
        public const int HighPerformance = 0;

        public const int LowUnitCost = 1;
        public const int Inlet = 2;
        public const int Emd = 3;

        /// <summary>The numbers of the game's <c>PowerConsumptionFocus</c>.</summary>
        public const int PowerIrrelevant = 0;

        public const int PowerLowConsumption = 1;

        /// <summary>A designer with this design focus keeps it whatever the archetype says: Inlet (2) and EMD (3).</summary>
        public static bool KeepsDesignFocus(int designFocus)
        {
            return designFocus == Inlet || designFocus == Emd;
        }

        /// <summary>The priorities the designer gets from the archetype: <paramref name="baseline"/> with the overrides that apply.</summary>
        public static CreatorPriorities Target(CreatorPriorities baseline, DesignFocusOverride design, PowerFocusOverride power)
        {
            CreatorPriorities target = baseline;
            if (!KeepsDesignFocus(baseline.DesignFocus))
            {
                if (design == DesignFocusOverride.HighPerformance) target.DesignFocus = HighPerformance;
                else if (design == DesignFocusOverride.LowUnitCost) target.DesignFocus = LowUnitCost;
            }

            if (power == PowerFocusOverride.Irrelevant) target.PowerFocus = PowerIrrelevant;
            else if (power == PowerFocusOverride.LowConsumption) target.PowerFocus = PowerLowConsumption;
            return target;
        }
    }

    /// <summary>
    /// What the feature knows about one designer of a rival (design.md, Component 3: <c>CreatorBaseline</c>, a runtime cache that is not saved):
    /// the snapshot of its own priorities and what the character set. The rules:
    /// <list type="bullet">
    /// <item>The snapshot is taken the first time the character is applied. When the game itself changed the priorities since then
    /// (<c>AIBehaviourController.ReformCyrux</c> rewrites those of Cyrix), the values found are the new snapshot.</item>
    /// <item><c>ChangePriorities</c> is called only when the target differs from the current values: it resets the designer's cache of designs and
    /// its random generator.</item>
    /// <item><see cref="PlanRevert"/> gives the snapshot back, but only while the values are still the ones the character set (the game's own later
    /// change is left alone).</item>
    /// </list>
    /// Pure.
    /// </summary>
    public sealed class CreatorRecord
    {
        /// <summary>The designer's own priorities (the prefab values, or those the game set later).</summary>
        public CreatorPriorities Baseline { get; private set; }

        /// <summary>The priorities after the character was applied (equal to <see cref="Baseline"/> when nothing had to change).</summary>
        public CreatorPriorities Applied { get; private set; }

        /// <summary>The character has been applied to the designer and not reverted.</summary>
        public bool IsApplied { get; private set; }

        /// <summary>
        /// Takes the snapshot (see the class) and works out the priorities of the character. True when they differ from
        /// <paramref name="current"/>, so that <c>ChangePriorities(target)</c> has to be called; then call <see cref="MarkApplied"/>. False: nothing to
        /// change (call <see cref="MarkApplied"/> with <paramref name="target"/> all the same).
        /// </summary>
        public bool Plan(CreatorPriorities current, DesignFocusOverride design, PowerFocusOverride power, out CreatorPriorities target)
        {
            if (!IsApplied || !current.Equals(Applied)) Baseline = current;
            target = PriorityMath.Target(Baseline, design, power);
            return !target.Equals(current);
        }

        /// <summary>The designer now has <paramref name="target"/> (the result of <see cref="Plan"/>).</summary>
        public void MarkApplied(CreatorPriorities target)
        {
            Applied = target;
            IsApplied = true;
        }

        /// <summary>
        /// The way back to the snapshot: true when <c>ChangePriorities(<paramref name="target"/>)</c> has to be called (the character changed the
        /// values and they are still the ones it set). <paramref name="target"/> is always the snapshot. Call <see cref="MarkReverted"/> after.
        /// </summary>
        public bool PlanRevert(CreatorPriorities current, out CreatorPriorities target)
        {
            target = Baseline;
            return IsApplied && current.Equals(Applied) && !Baseline.Equals(current);
        }

        /// <summary>The character is no longer applied to the designer.</summary>
        public void MarkReverted()
        {
            IsApplied = false;
        }
    }
}
