using System;
using PTMod.Core;
using PTMod.Core.Logic.Features;

namespace PTMod.Features.MarketShocks
{
    /// <summary>
    /// The two channels of the pack that the patches of this feature read (design.md, "Каналы и патчи"; FR-8 items 6, 7; NFR-1):
    /// <c>production.memoryCost</c> (<see cref="MemoryK"/>, the price of cache memory in <c>CpuMath.MaterialCost</c>) and
    /// <c>production.waferCost</c> (<see cref="WaferK"/>, the material of a wafer in <c>ProductionMath.WaferMaterialCost</c>).
    /// <list type="bullet">
    /// <item><b>The channels are not declared here.</b> The core declares both in <c>core.json</c> (owner <c>market-shocks</c>, global, fuses
    /// [0.5; 2.0] and [0.6; 2.0]) before any <c>Register</c>; this class only names them (<see cref="ModChannels"/>) and reads them.
    /// <c>ModModifiers.DeclareChannel</c> is never called.</item>
    /// <item><b>One reading a day.</b> The patches run for every CPU every day and many times in the AI's search for a design, so each
    /// multiplier is held in a <see cref="DailyValueCache"/>: <c>ModModifiers.Get</c> is called at most once per game day (<c>ModTicks.Today</c>
    /// changes) and again after <see cref="Invalidate"/>. With nothing registered on the channel (<c>ModModifiers.HasAny</c> is false) the answer
    /// is 1 with no reading: the patches see <c>k == 1</c> and leave at once.</item>
    /// <item><b>When the cache is dropped</b> (FR-8 item 6): a feature changes state (<c>ModFeatures.StateChanged</c>: the pack or the engine is
    /// switched on or off), a save is loaded or a new game has started (<c>ModTicks.GameReady</c>: the engine has restored its effects), and the
    /// core reports a change of one of the two channels (<c>ModModifiers.ChannelChanged</c>: a contribution moved in the middle of a day).
    /// <see cref="Attach"/> subscribes once (the feature's <c>Register</c> calls it; the first read does it too, so the patches work without it).
    /// A handler of a feature that is switched off is not called by the core; the feature's <c>OnDisabled</c>/<c>OnEnabled</c> call
    /// <see cref="Invalidate"/> for that case.</item>
    /// <item><b>Failure.</b> A reading that throws is counted against the feature (<c>ModHooks.ReportFault</c>) and the multiplier is 1: the
    /// patches then leave the game's value alone.</item>
    /// </list>
    /// </summary>
    internal static class ShockChannels
    {
        /// <summary>The id of the feature (the owner of the two channels and of their formulas).</summary>
        public const string FeatureId = ShockRegistration.FeatureId;

        /// <summary><c>production.memoryCost</c>: the price of cache memory (<c>Memory.UnitCostPerCache</c> in <c>CpuMath.MaterialCost</c>).</summary>
        public const string MemoryCost = ModChannels.ProductionMemoryCost;

        /// <summary><c>production.waferCost</c>: the material of a wafer (<c>ProductionMath.WaferMaterialCost</c>).</summary>
        public const string WaferCost = ModChannels.ProductionWaferCost;

        private const string ReadSite = "ShockChannels/read";

        private static readonly DailyValueCache MemoryCache = new DailyValueCache(MemoryCost, DayNow, () => ModModifiers.HasAny(MemoryCost), () => ReadChannel(MemoryCost));
        private static readonly DailyValueCache WaferCache = new DailyValueCache(WaferCost, DayNow, () => ModModifiers.HasAny(WaferCost), () => ReadChannel(WaferCost));

        private static bool _attached;

        /// <summary>The multiplier of <c>production.memoryCost</c> for today; 1 when there is no shock. Cached for the game day.</summary>
        public static float MemoryK
        {
            get { return ValueOf(MemoryCache); }
        }

        /// <summary>The multiplier of <c>production.waferCost</c> for today; 1 when there is no shock. Cached for the game day.</summary>
        public static float WaferK
        {
            get { return ValueOf(WaferCache); }
        }

        /// <summary>Drops the cached values of both channels: the next read asks the channel again.</summary>
        public static void Invalidate()
        {
            MemoryCache.Invalidate();
            WaferCache.Invalidate();
        }

        /// <summary>
        /// Subscribes the cache to the moments that change what the channels say (see the class summary). Safe to call more than once; the
        /// subscription lives as long as the game runs (the handlers are static and cheap).
        /// </summary>
        public static void Attach()
        {
            if (_attached) return;
            _attached = true;

            ModModifiers.ChannelChanged += OnChannelChanged;
            ModTicks.GameReady += OnGameReady;
            ModFeatures.StateChanged += OnFeatureStateChanged;
        }

        private static float ValueOf(DailyValueCache cache)
        {
            try
            {
                if (!_attached) Attach();
                return cache.Value;
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(FeatureId, ReadSite, ex);
                return 1f;
            }
        }

        /// <summary>The game day as a number that changes with the date; -1 when the date cannot be read (outside the game scene), which is never cached.</summary>
        private static long DayNow()
        {
            DateTime today = ModTicks.Today;
            return today.Year <= 1 ? -1L : today.Ticks / TimeSpan.TicksPerDay;
        }

        /// <summary>The channels are global: the empty context asks for the multiplier everyone shares (the core holds it to the channel's fuse).</summary>
        private static float ReadChannel(string channel)
        {
            return ModModifiers.Get(channel, default(ModifierContext));
        }

        private static void OnChannelChanged(string channel)
        {
            MemoryCache.OnChannelChanged(channel);
            WaferCache.OnChannelChanged(channel);
        }

        private static void OnGameReady(GameReadyReason reason)
        {
            Invalidate();
        }

        private static void OnFeatureStateChanged(string featureId, FeatureState state)
        {
            Invalidate();
        }
    }
}
