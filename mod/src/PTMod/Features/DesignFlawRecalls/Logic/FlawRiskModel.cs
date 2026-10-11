using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// What the probability of a hidden flaw is computed from (FR-2, design.md Component 2): the numbers of the finished design, read from the game
    /// by <c>GameCpuView</c> at the moment of the release. A plain value, so the formula can be tested without the game.
    /// </summary>
    public struct FlawRiskInput
    {
        /// <summary><c>CpuBase.FailureRate</c>: the result of the game's formula at design time.</summary>
        public float FailureRate;

        /// <summary><c>CpuBase.Frequency</c>.</summary>
        public float Frequency;

        /// <summary><c>ICompany.MaxFrequency + ResearchModifiers.FrequencyIncrease</c> of the owner at the release (the ceiling without overclock).</summary>
        public float MaxFrequency;

        /// <summary><c>CpuBase.Temperature</c>.</summary>
        public float Temperature;

        /// <summary><c>IPackage.SafeTemperature</c> (the formula limits it to 95, like the game's own failure formula does).</summary>
        public float SafeTemperature;

        /// <summary><c>Manufacturer.CurrentNodeLearningProgress(ProcessNode)</c>, 0..1; used only when <see cref="HasNodeRecord"/>.</summary>
        public float NodeProgress;

        /// <summary>The manufacturer has a <c>NodeOptimization</c> for the process node (otherwise the progress 0 means "unknown", not "raw").</summary>
        public bool HasNodeRecord;

        /// <summary>The year of the release (the window of the flaw types and the era multiplier).</summary>
        public int Year;

        /// <summary><c>Player.DifficultyLevel</c>, 0..5; the same multiplier for the player and the AI.</summary>
        public int Difficulty;

        /// <summary>The owner is an AI company.</summary>
        public bool IsAi;

        /// <summary>The CPU was made with the "rush project" (FR-20). Ignored for an AI company.</summary>
        public bool Rush;
    }

    /// <summary>The factors of the formula (design.md, "Formulas and balance", item 1): each 0..1 except <c>FR</c>, which is the failure rate itself.</summary>
    public struct FlawRiskFactors
    {
        /// <summary>The failure rate of the design.</summary>
        public float FR;

        /// <summary>Overclock: how far the frequency is over the ceiling of the owner, 0..<c>overclockCap</c>.</summary>
        public float OC;

        /// <summary>Overheating: how far the temperature is over the safe one, 0..1.</summary>
        public float TH;

        /// <summary>Immaturity of the process node, 0..1.</summary>
        public float NI;

        /// <summary>Rush project: 0 or 1.</summary>
        public float RS;
    }

    /// <summary>
    /// The probability, the severity and the type of a hidden flaw (FR-2, FR-3, design.md Component 2). Pure: the coefficients come from
    /// <see cref="FlawBalance"/>, the randomness from the <see cref="IModRng"/> of the caller (<c>ModRandom.Stream</c> in the game, a fake in tests).
    /// </summary>
    public static class FlawRiskModel
    {
        /// <summary>The game's failure formula limits the safe temperature to 95; so does this one.</summary>
        public const float MaxSafeTemperature = 95f;

        /// <summary>The most a broken <c>Difficulty</c> value can be read as (the multiplier table has 6 rows).</summary>
        private const int LastDifficulty = FlawBalance.DifficultyCount - 1;

        /// <summary>The factors of the formula for a design. Broken numbers (NaN, infinities, a zero ceiling) count as "no effect".</summary>
        public static FlawRiskFactors Factors(FlawRiskInput input, FlawBalance b)
        {
            FlawBalance.RiskBalance risk = b.Risk;
            var f = new FlawRiskFactors();

            f.FR = (float)Math.Max(0.0, FlawMath.Finite(input.FailureRate));

            double oc = 0.0;
            double ceiling = FlawMath.Finite(input.MaxFrequency);
            if (ceiling > 0.0) oc = FlawMath.Finite(input.Frequency) / ceiling - 1.0;
            f.OC = (float)FlawMath.Clamp(oc, 0.0, Math.Max(0.0, risk.OverclockCap));

            double safe = Math.Min(FlawMath.Finite(input.SafeTemperature), MaxSafeTemperature);
            double th = 0.0;
            if (safe > 0.0) th = (FlawMath.Finite(input.Temperature) - safe) / safe;
            f.TH = (float)FlawMath.Clamp(th, 0.0, 1.0);

            double ni = input.HasNodeRecord ? 1.0 - FlawMath.Finite(input.NodeProgress) : risk.UnknownNodeImmaturity;
            f.NI = (float)FlawMath.Clamp(ni, 0.0, 1.0);

            f.RS = input.Rush && !input.IsAi ? 1f : 0f;
            return f;
        }

        /// <summary>
        /// The probability that the CPU has a hidden flaw: <c>clamp(p_raw * difficulty * era * (AI ? aiRiskMultiplier : 1) * settingsMultiplier, 0, pMax)</c>
        /// with <c>p_raw = base + kFailureRate*FR + kOverclock*OC + kThermal*TH + kNodeImmaturity*NI + kRush*RS</c>.
        /// </summary>
        /// <param name="settingsMultiplier">
        /// The <c>flawFrequency</c> setting (0.5..2.0); a broken value (NaN, an infinity, a negative number) counts as 1.
        /// </param>
        public static float Probability(FlawRiskInput input, FlawBalance b, float settingsMultiplier)
        {
            FlawBalance.RiskBalance risk = b.Risk;
            FlawRiskFactors f = Factors(input, b);

            double raw = risk.Base + risk.KFailureRate * f.FR + risk.KOverclock * f.OC + risk.KThermal * f.TH
                         + risk.KNodeImmaturity * f.NI + risk.KRush * f.RS;

            double settings = FlawMath.IsFinite(settingsMultiplier) && settingsMultiplier >= 0f ? settingsMultiplier : 1.0;
            double p = raw
                       * DifficultyMultiplier(input.Difficulty, b)
                       * EraMultiplier(input.Year, b)
                       * (input.IsAi ? risk.AiRiskMultiplier : 1.0)
                       * settings;

            return (float)FlawMath.Clamp(p, 0.0, FlawMath.Clamp(risk.PMax, 0.0, 1.0));
        }

        /// <summary>The multiplier of the difficulty level (0..5); a level outside the table counts as the nearest end, a missing table as 1.</summary>
        public static float DifficultyMultiplier(int difficulty, FlawBalance b)
        {
            float[] table = b.Risk.DifficultyMultiplier;
            if (table == null || table.Length == 0) return 1f;

            int index = difficulty < 0 ? 0 : (difficulty > LastDifficulty ? LastDifficulty : difficulty);
            return FlawMath.At(table, index, 1f);
        }

        /// <summary>The era multiplier of the release year: the first row of the table that holds the year; 1 when no row does.</summary>
        public static float EraMultiplier(int year, FlawBalance b)
        {
            List<FlawBalance.EraRow> rows = b.Risk.Era;
            if (rows == null) return 1f;

            foreach (FlawBalance.EraRow row in rows)
            {
                if (row != null && year >= row.FromYear && year <= row.ToYear) return row.Multiplier;
            }
            return 1f;
        }

        // ---- severity and type (FR-3) ----

        /// <summary>
        /// The weights of Minor, Major, Critical for a flaw rolled with the probability <paramref name="probability"/>: the base weights, those of Major
        /// and Critical grown by <c>shiftMajor</c> and <c>shiftCritical</c> times p (design.md, item 2). p = 0.233 gives [0.60, 0.44, 0.19].
        /// </summary>
        public static float[] SeverityWeights(float probability, FlawBalance b)
        {
            double p = FlawMath.Clamp(probability, 0.0, 1.0);
            FlawBalance.SeverityBalance s = b.Severity;
            return new[]
            {
                FlawMath.At(s.BaseWeights, 0, 0f),
                (float)(FlawMath.At(s.BaseWeights, 1, 0f) * (1.0 + s.ShiftMajor * p)),
                (float)(FlawMath.At(s.BaseWeights, 2, 0f) * (1.0 + s.ShiftCritical * p)),
            };
        }

        /// <summary>
        /// Picks the severity with the weights of <see cref="SeverityWeights"/>. Draws one number from <paramref name="rng"/>. If no weight is usable
        /// (a broken table), the flaw is <see cref="FlawSeverity.Minor"/>.
        /// </summary>
        public static FlawSeverity PickSeverity(float probability, FlawBalance b, IModRng rng)
        {
            int index = rng.WeightedIndex(SeverityWeights(probability, b));
            return index < 0 ? FlawSeverity.Minor : (FlawSeverity)Math.Min(index, FlawBalance.SeverityCount - 1);
        }

        /// <summary>
        /// The weight of a type for a design: <c>weight * (1 + weightByOverclock*OC) * (1 + weightByThermal*TH)</c> when the year is inside the window of
        /// the type, otherwise 0. For <c>stability</c> at OC = 0.4 it is 3 * (1 + 5 * 0.4) = 9.
        /// </summary>
        public static float TypeWeight(FlawTypeDef type, FlawRiskFactors f, int year)
        {
            if (type == null || year < type.FromYear || year > type.ToYear) return 0f;

            double weight = type.Weight
                            * (1.0 + type.WeightByOverclock * Math.Max(0.0, f.OC))
                            * (1.0 + type.WeightByThermal * Math.Max(0.0, f.TH));
            return FlawMath.IsFinite(weight) && weight > 0.0 ? (float)weight : 0f;
        }

        /// <summary>
        /// Picks the type of the flaw among those whose window holds <paramref name="year"/>, by <see cref="TypeWeight"/>; draws one number from
        /// <paramref name="rng"/>. When no type is available the built-in <c>arith</c> is used (FR-3 item 3): the entry of <paramref name="types"/> with
        /// that id if there is one, otherwise the default one; nothing is drawn then.
        /// </summary>
        public static FlawTypeDef PickType(FlawRiskFactors f, int year, IReadOnlyList<FlawTypeDef> types, IModRng rng)
        {
            if (types != null && types.Count > 0)
            {
                var weights = new float[types.Count];
                bool any = false;
                for (int i = 0; i < weights.Length; i++)
                {
                    weights[i] = TypeWeight(types[i], f, year);
                    if (weights[i] > 0f) any = true;
                }

                if (any)
                {
                    int index = rng.WeightedIndex(weights);
                    if (index >= 0 && index < types.Count) return types[index];
                }
            }

            return Fallback(types);
        }

        private static FlawTypeDef Fallback(IReadOnlyList<FlawTypeDef> types)
        {
            if (types != null)
            {
                for (int i = 0; i < types.Count; i++)
                {
                    if (types[i] != null && types[i].Id == FlawTypeDef.FallbackId) return types[i];
                }
            }
            return FlawTypeDef.FallbackArithmetic();
        }
    }
}
