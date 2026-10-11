using PTMod.Core;
using PTMod.Features.EventSystem;

namespace PTMod.Features.MarketShocks
{
    /// <summary>
    /// Registers the two predicates of the pack in the catalog of <c>ModConditions</c> (FR-12, design.md, "Предикаты") through
    /// <c>EventConditions.Register</c>, with the owner <c>market-shocks</c>: <c>ms.sellsBelowCost</c> (a CPU of the company sells under its cost while the
    /// company holds a share of the market) and <c>ms.hasRecentCpu</c> (the company has a fresh CPU on sale). The meaning, the parameters and their defaults
    /// are <see cref="ShockPredicateLogic"/>'s (pure); the CPUs come from <see cref="GameCpuSource"/>. The names carry the prefix of the feature, as
    /// <c>ModIds.IsPredicateNameAllowed</c> demands; <c>ModConditions.Register</c> refuses any other name and logs it. The core wraps a registered predicate so that it
    /// is false while the feature is not active and an exception counts against the feature.
    /// </summary>
    internal static class ShockPredicates
    {
        private static readonly GameCpuSource Source = new GameCpuSource();

        private static bool _registered;

        /// <summary>
        /// Registers both predicates (once per session; the feature's <c>Register</c> calls it). The catalog is shared and does not forget, so a second call
        /// would only be refused with a warning.
        /// </summary>
        public static void Register()
        {
            if (_registered) return;
            _registered = true;

            EventConditions.Register(ShockPredicateLogic.SellsBelowCostName, ShockPack.PackId,
                (args, ctx) => ShockPredicateLogic.SellsBelowCost(args, ctx, Source));
            EventConditions.Register(ShockPredicateLogic.HasRecentCpuName, ShockPack.PackId,
                (args, ctx) => ShockPredicateLogic.HasRecentCpu(args, ctx, Source));
        }
    }
}
