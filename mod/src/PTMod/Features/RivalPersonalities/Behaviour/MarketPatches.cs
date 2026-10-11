using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using AICompany = ProcessorTycoon.CompanySystem.AICompany;
using GameAI = ProcessorTycoon.CompanySystem.AI;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameMarket = ProcessorTycoon.AISystem.AIBehaviourMarket;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The prices of the AI (design.md, Component 4, D3; formulas 3 and 4; FR-3 item 3, FR-7 item 3, FR-13 item 6): a prefix and a postfix of
    /// <c>AIBehaviourMarket.OnTick()</c>, which the controller calls every day and <c>AIBehaviourController.TriggerPriceUpdate</c> calls when the AI
    /// adds a CPU.
    /// <list type="bullet">
    /// <item><b>Prefix</b> remembers the CPUs that have no price yet (<c>Strategy.TargetPrice &lt;= 0</c> or <c>Price &lt;= 0</c>, not retired), in a
    /// list from a pool. Nothing when the launch multiplier of the company is 1 (fast-forward, character not applied, foundry).</item>
    /// <item><b>Postfix</b>: those CPUs got their launch price from <c>HandleInitialPrice</c>; each gets <c>TargetPrice = Price = max(TargetPrice ×
    /// launchPriceMult, RealUnitCost × 1.05)</c> (<see cref="PriceCutMath.LaunchPrice"/>, the margin is <c>launchPriceFloorMargin</c>). Then the price
    /// cuts of the company hold their target prices down (<see cref="PriceBook.Clamp"/>).</item>
    /// </list>
    /// The daily path allocates nothing: the lists come from <see cref="ListPool{T}"/> and go back in the postfix. A failure is counted by the core
    /// and leaves the game's prices.
    /// </summary>
    internal static class MarketPatches
    {
        private const string Feature = RivalPersonalitiesFeature.FeatureId;
        private const string PrefixSite = "MarketPatches/AIBehaviourMarket.OnTick.prefix";
        private const string PostfixSite = "MarketPatches/AIBehaviourMarket.OnTick.postfix";

        private static readonly ListPool<GameCpu> Pool = new ListPool<GameCpu>(8);

        [ModPatch(typeof(GameMarket), "OnTick", PatchKind.Prefix)]
        private static void MarketPrefix(GameAI ___ai, AICompany ___company, out List<GameCpu> __state)
        {
            __state = null;
            if (!ModHooks.CanRun(Feature, PrefixSite)) return;
            long started = RivalTimings.Begin();
            try
            {
                RivalFaultProbe.Trip(PrefixSite);
                RivalPersonalitiesFeature feature = RivalPersonalitiesFeature.Instance;
                if (feature == null || ___ai == null || !ModFeatures.IsActive(Feature)) return;
                if (feature.Modifiers.LaunchPriceMultiplier(___company) == 1f) return;

                List<GameCpu> cpus = ___ai.GetCpus();
                if (cpus == null) return;

                List<GameCpu> unpriced = null;
                for (int i = 0; i < cpus.Count; i++)
                {
                    GameCpu cpu = cpus[i];
                    if (cpu == null || cpu.IsRetired) continue;
                    float target = cpu.Strategy != null ? cpu.Strategy.TargetPrice : -1f;
                    if (cpu.Price > 0f && target > 0f) continue;

                    if (unpriced == null) unpriced = Pool.Rent();
                    unpriced.Add(cpu);
                }
                __state = unpriced;
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(Feature, PrefixSite, e);
                __state = null;
            }
            finally
            {
                RivalTimings.End(started);
            }
        }

        [ModPatch(typeof(GameMarket), "OnTick", PatchKind.Postfix)]
        private static void MarketPostfix(GameAI ___ai, AICompany ___company, List<GameCpu> __state)
        {
            long started = RivalTimings.Begin();
            try
            {
                if (!ModHooks.CanRun(Feature, PostfixSite)) return;
                RivalFaultProbe.Trip(PostfixSite);
                RivalPersonalitiesFeature feature = RivalPersonalitiesFeature.Instance;
                if (feature == null || !ModFeatures.IsActive(Feature)) return;

                if (__state != null && __state.Count > 0) PriceLaunches(feature, ___company, __state);

                // The book is the section of this game: not before it was read (a load reuses the save ids of the CPUs) and not in the fast-forward.
                if (!ModTicks.IsFastForwarding && feature.Registry.Lifecycle.StateReady && !ReferenceEquals(___company, null))
                {
                    feature.Prices.Clamp(___company, ___ai);
                }
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(Feature, PostfixSite, e);
            }
            finally
            {
                Pool.Return(__state);
                RivalTimings.End(started);
            }
        }

        /// <summary>The CPUs that got their first price in this call get the launch price of the archetype (formula 3).</summary>
        private static void PriceLaunches(RivalPersonalitiesFeature feature, AICompany company, List<GameCpu> launched)
        {
            float multiplier = feature.Modifiers.LaunchPriceMultiplier(company);
            if (multiplier == 1f) return;
            float floorMargin = feature.LaunchPriceFloorMargin;

            for (int i = 0; i < launched.Count; i++)
            {
                GameCpu cpu = launched[i];
                if (cpu == null || cpu.IsRetired || cpu.Strategy == null) continue;

                float target = cpu.Strategy.TargetPrice;
                if (!PriceCutMath.IsUsablePrice(target) || !PriceCutMath.IsUsablePrice(cpu.Price)) continue;   // still without a price

                float price = PriceCutMath.LaunchPrice(target, multiplier, cpu.RealUnitCost, floorMargin);
                if (price == target && price == cpu.Price) continue;

                cpu.Strategy.TargetPrice = price;
                cpu.Price = price;
                if (ModSettings.VerboseLog)
                {
                    ModLog.Debug(Feature, "launch price of " + cpu.Name + " (ai:" + company.UniqueID.ToString(CultureInfo.InvariantCulture) + "): "
                                          + target.ToString("0.##", CultureInfo.InvariantCulture) + " -> " + price.ToString("0.##", CultureInfo.InvariantCulture));
                }
            }
        }
    }
}
