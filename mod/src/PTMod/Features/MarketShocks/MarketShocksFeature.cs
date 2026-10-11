using System.Collections;
using PTMod.Core;
using PTMod.Features.EventSystem;

namespace PTMod.Features.MarketShocks
{
    /// <summary>
    /// The entry point of the feature <c>market-shocks</c> (design.md, "Фича"; FR-1, FR-14): market shocks - fab incidents, jumps of memory and wafer
    /// prices, booms and recessions, trade complaints - as a content pack of the event system (<c>event-system</c> is the hard dependency, so the core
    /// makes this feature Unavailable when the engine is missing and applies none of its patches: the prices of the game stay vanilla, AC-1.2).
    /// The feature decides nothing about <i>when</i> a shock falls or <i>whom</i> it hits: that is the engine's lottery over the categories of
    /// <c>Content/market-shocks/config.json</c> and the records of <c>shocks.json</c>. What it brings is the three effect types, the two predicates,
    /// the patches that read the two global channels of the pack and the two settings.
    /// <para>
    /// <see cref="Register"/> follows the order of the design: the effect types, the predicates, the pack, the patch classes, the parameters, the
    /// subscriptions. The channels <c>production.memoryCost</c> and <c>production.waferCost</c> are not declared here - the core's registry
    /// declares them before any feature registers (<c>ModModifiers.DeclareChannel</c> is never called). Nothing here works during the fast-forward
    /// of a new game: the engine handles no days then, the channels stay 1 and the patches leave at once (AC-1.3).
    /// </para>
    /// </summary>
    [ModFeature]
    public sealed class MarketShocksFeature : IModFeature
    {
        /// <summary>The slug of the feature: the key of its content, strings and settings, and the namespace <c>PTMod.Features.MarketShocks</c>.</summary>
        public const string FeatureId = ShockRegistration.FeatureId;

        private const string Site = "feature/";

        public string Id
        {
            get { return FeatureId; }
        }

        public string NameKey
        {
            get { return ShockRegistration.NameKey; }
        }

        public string DescriptionKey
        {
            get { return ShockRegistration.DescriptionKey; }
        }

        public string[] DependsOn
        {
            get { return ShockRegistration.DependsOn(); }
        }

        public bool EnabledByDefault
        {
            get { return true; }
        }

        public bool SupportsHotToggle
        {
            get { return true; }
        }

        // ---- IModFeature ----

        /// <summary>
        /// Registers everything the feature gives the engine and the core, once per session (the core calls it when the feature first becomes active,
        /// after <c>event-system</c> has registered). The engine reads the content of the pack later, on the first <c>GameReady</c>, when every feature
        /// has registered, so the order of the registrations among features does not matter.
        /// </summary>
        public void Register(FeatureContext ctx)
        {
            // 1. The effect types of the pack (ShockRegistration.EffectTypes lists their names). Each reads the intensity from ShockPack.Options.
            var handlers = new IEventEffectHandler[] { new ModifierRampEffect(), new ModifierWaveEffect(), new InventoryLossEffect() };
            foreach (IEventEffectHandler handler in handlers) EventEffects.Register(handler.Type, handler);

            // 2. The predicates ms.sellsBelowCost and ms.hasRecentCpu, owner market-shocks.
            ShockPredicates.Register();

            // 3. The pack. The engine keeps this object and reads Severity and Frequency whenever it uses them; the settings change them in place.
            EventRegistry.RegisterPack(ShockPack.PackId, ShockPack.PackId, ShockPack.Options);

            // 4. The patches; all four targets are "formulas of the feature" for the core (ReservedKind.FeatureFormula, owner market-shocks).
            ctx.Patch(typeof(MaterialCostPatch));
            ctx.Patch(typeof(WaferCostPatches));
            ctx.Patch(typeof(MemoryDescriptionPatch));

            // 5. The parameters and the page of the hub, and their values into the options of the pack (Settings/MarketShocksSettings.cs).
            MarketShocksSettings.Declare(ctx);

            // 6. The subscriptions: the daily cache of the two channels, the settings, the first GameReady.
            ShockChannels.Attach();
            MarketShocksSettings.Attach();
            ModTicks.GameReady += OnGameReady;
        }

        /// <summary>
        /// The feature became active (after its patches were applied): the options of the pack take the settings as they are now, and the cache of the
        /// channels is dropped, because a switch of the feature changes what the channels give (FR-14 items 2, 3). A handler of a feature that is off
        /// is not called by the core, so the cache cannot rely on its own subscriptions for this.
        /// </summary>
        public void OnEnabled()
        {
            ModHooks.Guard(FeatureId, Site + "enabled", () =>
            {
                MarketShocksSettings.Apply();
                ShockChannels.Invalidate();
            });
        }

        /// <summary>
        /// The feature stopped being active: the core removes its patches right after, so the next reading of a channel must not come from the cache.
        /// The engine freezes the effects of the pack by <c>PackOptions.SettingsFeatureId</c>; nothing of theirs is removed here.
        /// </summary>
        public void OnDisabled()
        {
            ModHooks.Guard(FeatureId, Site + "disabled", () => ShockChannels.Invalidate());
        }

        // ---- internals ----

        /// <summary>The game state is ready (a save was loaded, or a new game finished its fast-forward): the pack's line (AC-1.1) and the self-check go to the log.</summary>
        private static void OnGameReady(GameReadyReason reason)
        {
            ModHooks.Guard(FeatureId, Site + "gameReady", () =>
            {
                ShockChannels.Invalidate();
                ReportPackWhenLoaded();
            });
        }

        /// <summary>
        /// Says how many events of the pack the engine has accepted. The engine reads the packs on this same <c>GameReady</c>; when its handler has not
        /// run yet the registry is still empty, and the report waits one frame.
        /// </summary>
        private static void ReportPackWhenLoaded()
        {
            int events = CountPackEvents();
            PTModHost host = PTModHost.Instance;
            if (events > 0 || host == null || !EventSystemApi.IsActive)
            {
                ReportPack(events);
                return;
            }

            host.Run(ReportPackNextFrame());
        }

        private static IEnumerator ReportPackNextFrame()
        {
            yield return null;
            ModHooks.Guard(FeatureId, Site + "packReport", () => ReportPack(CountPackEvents()));
        }

        private static void ReportPack(int events)
        {
            if (events <= 0)
            {
                ModLog.Warn(FeatureId, "pack: the event system has accepted none of the events of the pack (check the content report of shocks.json)");
            }
            else
            {
                ModLog.Info(FeatureId, ShockRegistration.PackRegisteredLine(events));
                if (events != ShockRegistration.ExpectedEvents)
                {
                    ModLog.Warn(FeatureId, "pack: " + events + " of " + ShockRegistration.ExpectedEvents + " events passed the validation of the event system (check the content report of shocks.json)");
                }
            }

            // TSK-017: the pack has been read (or has not): the self-check says what it stands on, in one line when all is well.
            MarketShocksSelfCheck.RunAndLog();
        }

        private static int CountPackEvents()
        {
            int count = 0;
            foreach (EventDefinition definition in EventRegistry.All)
            {
                if (definition != null && definition.packId == ShockPack.PackId) count++;
            }
            return count;
        }
    }
}
