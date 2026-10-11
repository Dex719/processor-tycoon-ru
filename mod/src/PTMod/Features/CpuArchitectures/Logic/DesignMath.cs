using System;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// What a family does to a CPU designed on it (FR-4; design.md, "Дизайн CPU"): IPC and IPS × <c>design.ipc</c>, power × <c>design.power</c> (the
    /// temperature is then recomputed by the game's own <c>CpuMath.Temperature</c>), the cost of the project × <c>design.devCost</c>. The same numbers
    /// serve the designer of the player, the CPUs of the AI companies (<c>DataConverter.CpuDesignToCpu</c>) and the provider of the channel
    /// <c>development.cost.design</c>, so the player and the computer pay the same price for the same architecture (FR-4 items 2 and 5).
    /// <para>
    /// Pure arithmetic over <see cref="DesignDef"/>: a missing or neutral design (1/1/1) changes nothing, a factor that is not a positive number
    /// counts as 1, so a broken record cannot turn a CPU into NaN. The patches stay thin: they take the design of the company and call these.
    /// </para>
    /// </summary>
    public static class DesignMath
    {
        /// <summary>The design changes nothing: no design, or all three multipliers are exactly 1 (the patches then leave the game's numbers alone).</summary>
        public static bool IsNeutral(DesignDef design)
        {
            return design == null || (Factor(design.Ipc) == 1f && Factor(design.Power) == 1f && Factor(design.DevCost) == 1f);
        }

        /// <summary>IPC or IPS after the family: <c>value × design.ipc</c>.</summary>
        public static float Ipc(float value, DesignDef design)
        {
            return design == null ? value : value * Factor(design.Ipc);
        }

        /// <summary>Power consumption in watts after the family: <c>value × design.power</c>.</summary>
        public static float Power(float watts, DesignDef design)
        {
            return design == null ? watts : watts * Factor(design.Power);
        }

        /// <summary>
        /// The multiplier of the project cost (the value of the provider of <c>development.cost.design</c>): <c>design.devCost × (1 + immaturity)</c>.
        /// <paramref name="immaturity"/> is the surcharge of immature tools (stage 2, FR-12 item 3: 0 now, up to 0.25); a negative or unusable one counts as 0.
        /// </summary>
        public static float CostFactor(DesignDef design, float immaturity)
        {
            float surcharge = IsUsable(immaturity) ? immaturity : 0f;
            return (design == null ? 1f : Factor(design.DevCost)) * (1f + surcharge);
        }

        /// <summary>The cost of a project after the family: the game's cost × <see cref="CostFactor"/> (AC-4.1: 50M on x86 gives 57.5M).</summary>
        public static float Cost(float cost, DesignDef design, float immaturity)
        {
            return cost * CostFactor(design, immaturity);
        }

        private static float Factor(float factor)
        {
            return IsUsable(factor) && factor > 0f ? factor : 1f;
        }

        private static bool IsUsable(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
        }
    }
}
