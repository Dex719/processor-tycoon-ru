using System;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The estimate of the risk of a hidden flaw in the CPU designer (FR-14, design.md Component 15): the probability of the roll at the release, worked out by the same
    /// function (<see cref="FlawRiskModel.Probability"/>) from the numbers of the design on the screen, rounded to 5 percent for the player. Pure, and no random number is
    /// drawn: the hint is the chance, not a roll.
    /// </summary>
    public static class FlawRiskHint
    {
        /// <summary>The estimate is rounded to a multiple of this many percent.</summary>
        public const int StepPercent = 5;

        /// <summary>
        /// The probability of a hidden flaw for a design of the player (never an AI design). <paramref name="rush"/> is the state of the switch "Rush project" (FR-20);
        /// <paramref name="flawFrequency"/> the setting <c>flawFrequency</c>, which multiplies the probability as it does at the release.
        /// </summary>
        public static float Probability(FlawRiskInput input, FlawBalance balance, float flawFrequency, bool rush)
        {
            input.IsAi = false;
            input.Rush = rush;
            return FlawRiskModel.Probability(input, balance, flawFrequency);
        }

        /// <summary>The probability as a whole number of percent, rounded to the nearest multiple of <see cref="StepPercent"/> (halves go up); 0..100.</summary>
        public static int Percent(float probability)
        {
            if (float.IsNaN(probability) || probability <= 0f) return 0;
            if (probability >= 1f) return 100;

            double steps = Math.Round(probability * 100.0 / StepPercent, MidpointRounding.AwayFromZero);
            return (int)steps * StepPercent;
        }

        /// <summary>The risk is real but smaller than half a step: the hint says "under 5 percent" instead of "about 0".</summary>
        public static bool IsBelowStep(float probability)
        {
            return probability > 0f && Percent(probability) == 0;
        }
    }
}
