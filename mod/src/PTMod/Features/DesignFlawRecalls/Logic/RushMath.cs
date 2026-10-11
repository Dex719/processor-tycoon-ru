using System;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The arithmetic of the "Rush project" switch of the CPU designer (FR-20, design.md formula 11): a rushed project is shorter and dearer.
    /// <code>
    /// projectTime' = max(1, round(projectTime * (1 - rush.timeCut)))
    /// projectCost' = projectCost * rush.costMultiplier
    /// </code>
    /// 400 days and 80 million with <c>timeCut</c> 0.25 and <c>costMultiplier</c> 1.10 become 300 days and 88 million (per month: 6.0 to 8.8 million). The numbers
    /// come from <c>flaws.json</c> (<c>rush</c>); the probability of a flaw grows by <c>risk.kRush</c> (<see cref="FlawRiskModel"/>, <c>RS</c>). Pure: the patch of
    /// <c>CreatorCpu.Project</c> feeds it the game's numbers and writes the results back. No random numbers, and a broken number never makes a project cheaper or
    /// longer: NaN counts as "no rush".
    /// </summary>
    public static class RushMath
    {
        /// <summary>A project is at least one day long, however short the rush cuts it.</summary>
        public const int MinProjectDays = 1;

        /// <summary>The length of a rushed project: <c>max(1, round(days * (1 - timeCut)))</c>, halves rounded up. A project of less than a day is not a project and is left alone.</summary>
        public static int ApplyTime(int projectTime, FlawBalance.RushBalance rush)
        {
            if (rush == null) throw new ArgumentNullException("rush");
            if (projectTime < MinProjectDays) return projectTime;

            // The cut as the decimal number it was written as (0.3f is 0.3), so that 10 days at 0.25 are 7.5 and not 7.4999999; decimal keeps the half exact.
            decimal cut = (decimal)FlawMath.Clamp(FlawMath.Exact(rush.TimeCut), 0.0, 1.0);
            decimal days = Math.Round((decimal)projectTime * (1m - cut), MidpointRounding.AwayFromZero);
            return days < MinProjectDays ? MinProjectDays : (int)days;
        }

        /// <summary>The length of a rushed project with the numbers of the balance.</summary>
        public static int ApplyTime(int projectTime, FlawBalance balance)
        {
            if (balance == null) throw new ArgumentNullException("balance");
            return ApplyTime(projectTime, balance.Rush);
        }

        /// <summary>
        /// The cost of a rushed project: <c>cost * costMultiplier</c>. A multiplier below 1 or a broken one (NaN, an infinity) counts as 1: rushing is never cheaper.
        /// A broken cost (NaN, an infinity) is returned as it is, not made worse.
        /// </summary>
        public static float ApplyCost(float projectCost, FlawBalance.RushBalance rush)
        {
            if (rush == null) throw new ArgumentNullException("rush");
            if (!FlawMath.IsFinite(projectCost)) return projectCost;

            double multiplier = FlawMath.Exact(rush.CostMultiplier);
            if (!FlawMath.IsFinite(multiplier) || multiplier < 1.0) multiplier = 1.0;

            double cost = projectCost * multiplier;
            if (cost > float.MaxValue) return projectCost;
            return (float)cost;
        }

        /// <summary>The cost of a rushed project with the numbers of the balance.</summary>
        public static float ApplyCost(float projectCost, FlawBalance balance)
        {
            if (balance == null) throw new ArgumentNullException("balance");
            return ApplyCost(projectCost, balance.Rush);
        }

        /// <summary>How much shorter a rushed project is, as a whole number of percent (0.25 gives 25): the number in the tooltip of the switch. The same cut <see cref="ApplyTime(int, FlawBalance.RushBalance)"/> uses.</summary>
        public static int TimeCutPercent(FlawBalance.RushBalance rush)
        {
            if (rush == null) throw new ArgumentNullException("rush");
            decimal cut = (decimal)FlawMath.Clamp(FlawMath.Exact(rush.TimeCut), 0.0, 1.0);
            return (int)Math.Round(cut * 100m, MidpointRounding.AwayFromZero);
        }

        /// <summary>How much dearer a rushed project is, as a whole number of percent (1.10 gives 10): the number in the tooltip of the switch. The same multiplier <see cref="ApplyCost(float, FlawBalance.RushBalance)"/> uses.</summary>
        public static int CostPremiumPercent(FlawBalance.RushBalance rush)
        {
            if (rush == null) throw new ArgumentNullException("rush");
            double multiplier = FlawMath.Exact(rush.CostMultiplier);
            if (!FlawMath.IsFinite(multiplier) || multiplier < 1.0) return 0;
            if (multiplier > 100.0) multiplier = 100.0;
            return (int)Math.Round(((decimal)multiplier - 1m) * 100m, MidpointRounding.AwayFromZero);
        }
    }
}
