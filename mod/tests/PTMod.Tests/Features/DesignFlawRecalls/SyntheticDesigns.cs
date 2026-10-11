using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.DesignFlawRecalls;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// The population of "typical designs" the balance test draws from (TSK-024; requirements, "Acceptance Metrics"): the numbers of a finished CPU design
    /// (<see cref="FlawRiskInput"/>) as the game would hand them to the roll at the release. Everything here is an ASSUMPTION about how people design CPUs, written
    /// down in one place so that it can be argued with; the coefficients under test come from <see cref="FlawBalance"/>, never from here.
    /// <list type="bullet">
    /// <item><b>Years.</b> One campaign from the default start: designs are released uniformly in <see cref="FromYear"/>..<see cref="ToYear"/> (1980..2010).</item>
    /// <item><b>Failure rate.</b> Grows with the complexity of the era: 0.18 in 1980 and 0.58 in 2018 (the two rows of design.md "Formulas and balance" that are
    /// plain designs), a straight line between them, plus a spread of +-<see cref="FailureRateSpread"/> for how much the designer pushed the part.</item>
    /// <item><b>Overclock.</b> <see cref="OverclockShare"/> of the designs are overclocked, by <see cref="OverclockMin"/>..<see cref="OverclockMax"/> over the ceiling of the
    /// owner; the rest stay at 70..100 % of it.</item>
    /// <item><b>Heat.</b> <see cref="OverheatShare"/> of the designs run hotter than the package allows, by up to <see cref="OverheatMax"/> of the safe temperature.</item>
    /// <item><b>Process.</b> <see cref="NodeRecordShare"/> of the designs are made on a node the manufacturer has a record of (learned 30..100 %); the rest are made
    /// on a node it has not met (the formula then uses <c>unknownNodeImmaturity</c>).</item>
    /// <item><b>Not in the population:</b> the rush project and the AI multiplier (the population is the player's, without a rush, difficulty 2), unless a test asks.</item>
    /// </list>
    /// </summary>
    internal sealed class SyntheticPopulation
    {
        public int FromYear = 1980;
        public int ToYear = 2010;

        /// <summary><c>Player.DifficultyLevel</c> of every design (the default 2 is the "normal" level, multiplier 1.0).</summary>
        public int Difficulty = 2;

        public bool Rush;
        public bool IsAi;

        public float FailureRateIn1980 = 0.18f;
        public float FailureRatePerYear = 0.0105f;   // (0.58 - 0.18) / (2018 - 1980)
        public float FailureRateSpread = 0.08f;
        public float FailureRateMin = 0.02f;
        public float FailureRateMax = 0.80f;

        public float OverclockShare = 0.15f;
        public float OverclockMin = 0.05f;
        public float OverclockMax = 0.40f;

        public float OverheatShare = 0.08f;
        public float OverheatMax = 0.15f;

        public float NodeRecordShare = 0.80f;
        public float NodeProgressMin = 0.30f;
        public float NodeProgressMax = 1.00f;
    }

    /// <summary>
    /// The generator of <see cref="SyntheticPopulation"/>: a list of designs from an <see cref="IModRng"/>. Deterministic: the same generator state gives the same
    /// list, and every design draws the same number of values whatever the branch it takes, so a change of one share never shifts the designs after it.
    /// Internal, so the runner never discovers it.
    /// </summary>
    internal static class SyntheticDesigns
    {
        /// <summary>How many numbers one design draws from the generator.</summary>
        public const int DrawsPerDesign = 10;

        public static List<FlawRiskInput> Generate(int count, IModRng rng, SyntheticPopulation population)
        {
            if (rng == null) throw new ArgumentNullException("rng");
            if (population == null) population = new SyntheticPopulation();

            var designs = new List<FlawRiskInput>(Math.Max(0, count));
            for (int i = 0; i < count; i++) designs.Add(One(rng, population));
            return designs;
        }

        /// <summary>One design. Draws exactly <see cref="DrawsPerDesign"/> numbers.</summary>
        public static FlawRiskInput One(IModRng rng, SyntheticPopulation p)
        {
            int year = rng.Range(p.FromYear, p.ToYear + 1);
            float spread = (rng.Value() - 0.5f) * 2f * p.FailureRateSpread;
            bool overclocked = rng.Chance(p.OverclockShare);
            float overclock = p.OverclockMin + rng.Value() * (p.OverclockMax - p.OverclockMin);
            bool overheated = rng.Chance(p.OverheatShare);
            float overheat = rng.Value() * p.OverheatMax;
            bool knownNode = rng.Chance(p.NodeRecordShare);
            float progress = p.NodeProgressMin + rng.Value() * (p.NodeProgressMax - p.NodeProgressMin);
            float cool = 0.5f + rng.Value() * 0.45f;   // how far below the safe temperature a design that is not overheated runs
            float safeRoll = rng.Value();

            float failureRate = p.FailureRateIn1980 + p.FailureRatePerYear * (year - 1980) + spread;
            failureRate = Math.Min(p.FailureRateMax, Math.Max(p.FailureRateMin, failureRate));

            float maxFrequency = 50f + 10f * (year - 1980);   // the ceiling of the owner grows with the research; only the ratio matters
            float frequency = overclocked ? maxFrequency * (1f + overclock) : maxFrequency * (0.7f + 0.3f * cool);

            float safe = 70f + 20f * safeRoll;   // below 95, the cap of the game's own formula
            float temperature = overheated ? safe * (1f + overheat) : safe * cool;

            return new FlawRiskInput
            {
                FailureRate = failureRate,
                Frequency = frequency,
                MaxFrequency = maxFrequency,
                Temperature = temperature,
                SafeTemperature = safe,
                HasNodeRecord = knownNode,
                NodeProgress = knownNode ? progress : 0f,
                Year = year,
                Difficulty = p.Difficulty,
                IsAi = p.IsAi,
                Rush = p.Rush,
            };
        }
    }
}
