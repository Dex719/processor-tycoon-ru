using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Modifiers;

namespace PTMod.Core
{
    /// <summary>
    /// Named multiplier channels for the formulas of the game (FR-12): features multiply a formula by adding saved entries or by registering
    /// providers; the core's patches (P1-P11, P19) read the channel instead of each feature patching the formula. The engine - the product, the
    /// scopes, the balance range and the safety fuse, the one-day cache, <see cref="Explain"/> - is the game-free <see cref="ChannelRegistry"/>; this
    /// class is its one instance and its place in the game:
    /// <list type="bullet">
    /// <item><b>Context.</b> A formula asks with a <see cref="ModifierContext"/> (company, CPU, segment, contract, design). It becomes the engine's
    /// <see cref="ModifierQuery"/> (<see cref="ModCompanies.KeyOf"/> for the key, <c>Cpu.SaveID</c>, <c>Contract.ContractID</c>); a provider gets the
    /// context back, completed. Nothing is allocated when a channel has nothing registered (<see cref="Get"/> returns 1 after one lookup).</item>
    /// <item><b>Channels.</b> The 27 channels of <c>core.json</c> are declared in the Declare stage of the start, before any feature registers
    /// (D21); an entry or provider for a channel that is not declared yet is accepted and bound by name later (FR-12 item 16). A feature adds a
    /// channel only for a name the registry does not have (<see cref="DeclareChannel"/>).</item>
    /// <item><b>Entries and providers.</b> Entries (<see cref="Add"/>) are saved with the game (block <c>modifiers</c>, <see cref="ModifiersSaveParticipant"/>)
    /// and may end on a date; providers (<see cref="RegisterProvider(string, string, Func{ModifierContext, float}, string)"/>) are code, registered once per start. Both
    /// apply only while their feature is Enabled; the records of a feature that is off are kept. A provider that throws, or returns NaN, an
    /// infinity or a negative number, counts as x1 and as a fault of its feature.</item>
    /// <item><b>Time.</b> The registry's day is the last finished game day: it moves on <c>DayPassed</c> (also during the new game's fast-forward), which removes the
    /// entries that ended and empties the cache. A temporary entry added by a feature in the ticks of a day therefore counts that day as its
    /// first (<c>Add(..., 90)</c> on 1990-01-01 works through 1990-03-31); on 1990-04-01 it is gone.</item>
    /// <item><b>Cache.</b> The value of a channel is cached until the day ends, per company, CPU, segment and contract. It is dropped by every change of
    /// the records, by the monthly drift of the reputation, by a change of a feature's state and by <see cref="Invalidate(string)"/>; a feature
    /// whose provider depends on something that changes inside a day (the chosen architecture, a patch released) calls <see cref="Invalidate(string)"/> itself.</item>
    /// </list>
    /// Main thread only. Never throws into the caller: a failure is counted against the core and the answer is 1.
    /// </summary>
    public static class ModModifiers
    {
        private const string CoreId = ModLog.CoreFeatureId;
        private const string GetSite = "ModModifiers/Get";
        private const string ExplainSite = "ModModifiers/Explain";
        private const string ReadySite = "ModModifiers/ready";
        private const int DayPriority = 1000;   // before the features' handlers of DayPassed: they see the ended entries gone

        /// <summary>The channel engine. Created with the class; its feature predicate is <see cref="ModFeatures.IsActive"/>.</summary>
        internal static readonly ChannelRegistry Registry = CreateRegistry();

        private static readonly HashSet<string> UnknownOwnersWarned = new HashSet<string>(StringComparer.Ordinal);
        private static readonly bool[] TypeFlags = new bool[ModChannels.ModifierTypeCount];

        // The context of the question being answered, for the providers: the engine hands them its own query, they want the game's context. Set
        // around every Get and Explain (saved and restored, so a provider that asks another channel does not disturb the outer question).
        private static ModifierContext _current;
        private static int _typeFlagsVersion = -1;
        private static bool _declared;

        // ---- entries ----

        /// <summary>
        /// Adds a saved entry: <paramref name="multiplier"/> (finite, 0 or more) on <paramref name="channel"/> for <paramref name="scope"/>, owned by
        /// <paramref name="featureId"/>. <paramref name="durationDays"/> 0 means unlimited, otherwise the entry ends after that many game days.
        /// <paramref name="reasonKey"/> (a ModL10n key) and <paramref name="reasonArgs"/> are what <see cref="Explain"/> shows. Returns the id of the
        /// entry (the key of <see cref="Remove"/>), or null with a warning in the log when the call is invalid. The entry of a feature that is
        /// switched off is kept and does not apply.
        /// </summary>
        public static string Add(string featureId, string channel, ModifierScope scope, float multiplier,
            int durationDays = 0, string reasonKey = null, string[] reasonArgs = null)
        {
            try
            {
                WarnIfUnknownOwner(featureId, "a modifier on '" + channel + "'");
                if (durationDays > 0) EnsureDay();
                ModifierEntry entry = Registry.Add(featureId, channel, scope, multiplier, durationDays, reasonKey, reasonArgs);
                return entry == null ? null : entry.Id;
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "modifiers: Add failed", ex);
                return null;
            }
        }

        /// <summary>Removes one entry by its id; false if there is none.</summary>
        public static bool Remove(string entryId)
        {
            try
            {
                return Registry.Remove(entryId);
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "modifiers: Remove failed", ex);
                return false;
            }
        }

        /// <summary>Removes the entries of a feature, all of them or those of one channel; returns how many. Providers stay (they are removed with the process).</summary>
        public static int RemoveAll(string featureId, string channel = null)
        {
            try
            {
                return Registry.RemoveAll(featureId, channel);
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "modifiers: RemoveAll failed", ex);
                return 0;
            }
        }

        // ---- providers ----

        /// <summary>
        /// Registers a provider: a function of the question that returns a multiplier (0 or more). It is not saved - register it once, in
        /// <c>Register</c> or on <c>GameReady</c>, not in <c>OnEnabled</c>: it is skipped while the feature is not Enabled and runs again when
        /// it is. The same delegate registered again by the same feature on the same channel is ignored. The result is cached with the channel's
        /// value for the rest of the day (see <see cref="Invalidate(string)"/>). <paramref name="reasonKey"/> is the fixed reason <see cref="Explain"/> shows.
        /// The channel may be declared later.
        /// </summary>
        public static void RegisterProvider(string featureId, string channel, Func<ModifierContext, float> provider, string reasonKey = null)
        {
            try
            {
                WarnIfUnknownOwner(featureId, "a provider on '" + channel + "'");
                Func<ModifierQuery, float> wrapped = provider == null ? null : (Func<ModifierQuery, float>)(query => provider(ProviderContext()));
                Registry.RegisterProvider(featureId, channel, wrapped, reasonKey, provider);
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "modifiers: RegisterProvider failed", ex);
            }
        }

        /// <summary>
        /// Registers a provider that returns a reason with every call (<see cref="ProviderResult"/>: a ModL10n key and its arguments), for
        /// <see cref="Explain"/>. Everything else is as for the plain provider.
        /// </summary>
        public static void RegisterReasonedProvider(string featureId, string channel, Func<ModifierContext, ProviderResult> provider)
        {
            try
            {
                WarnIfUnknownOwner(featureId, "a provider on '" + channel + "'");
                Func<ModifierQuery, ProviderResult> wrapped = provider == null ? null : (Func<ModifierQuery, ProviderResult>)(query => provider(ProviderContext()));
                Registry.RegisterReasonedProvider(featureId, channel, wrapped, provider);
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "modifiers: RegisterReasonedProvider failed", ex);
            }
        }

        // ---- asking ----

        /// <summary>
        /// The multiplier of a channel for a question: the product of the applicable entries and providers, held to the channel's balance range
        /// (if it has one) and then to its safety fuse [Min, Max]. 1 for a channel with nothing registered - without any allocation. The result of
        /// a question is cached until the day ends (not for a design, not for a channel declared with <c>Cacheable = false</c>).
        /// </summary>
        public static float Get(string channel, ModifierContext ctx)
        {
            if (channel == null || !Registry.HasAny(channel)) return 1f;
            if (!ModHooks.CanRun(CoreId, GetSite)) return 1f;

            try
            {
                return Evaluate(channel, ctx);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, GetSite, ex);
                return 1f;
            }
        }

        /// <summary>
        /// The breakdown behind <see cref="Get"/> for the same question (never cached, calls the providers): the applied multipliers with their
        /// source feature, reason and end date, the product before the limits (<see cref="ModifierBreakdown.Raw"/>), the value <see cref="Get"/>
        /// returns (<see cref="ModifierBreakdown.Total"/>) and whether the balance range or the fuse cut it. Multipliers equal to 1 are not listed.
        /// For tooltips: append <c>core.explain.capped</c> after the value when <see cref="ModifierBreakdown.IsBalanceCapped"/> is set.
        /// </summary>
        public static ModifierBreakdown Explain(string channel, ModifierContext ctx)
        {
            if (channel == null || !ModHooks.CanRun(CoreId, ExplainSite)) return Registry.Explain(null, default(ModifierQuery));

            ModifierContext saved = _current;
            try
            {
                ctx = ctx.WithKey();
                _current = ctx;
                return Registry.Explain(channel, ctx.ToQuery());
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, ExplainSite, ex);
                return Registry.Explain(null, default(ModifierQuery));
            }
            finally
            {
                _current = saved;
            }
        }

        /// <summary>
        /// True when something is registered on the channel (an entry or a provider of any feature, one that applies to the question or not): the
        /// fast path of the patches. A false answer means the game's value stands, with no further work.
        /// </summary>
        public static bool HasAny(string channel)
        {
            return Registry.HasAny(channel);
        }

        // ---- channels ----

        /// <summary>
        /// Declares a channel of a feature, for a name the registry of the core does not have (the 27 channels of <c>core.json</c> are declared at
        /// start). True when the channel is declared (also when it was, with the same bounds); false, with a warning in the log, when the declaration is
        /// invalid or the name is taken with other bounds. A family is declared with the suffix <c>.*</c>.
        /// </summary>
        public static bool DeclareChannel(ChannelSpec spec)
        {
            try
            {
                return Registry.Declare(spec);
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "modifiers: DeclareChannel failed", ex);
                return false;
            }
        }

        /// <summary>True when a declaration covers the name: the exact name or a family such as <c>gpu.demand.*</c>.</summary>
        public static bool IsDeclared(string channel)
        {
            return Registry.IsDeclared(channel);
        }

        /// <summary>A copy of the declaration that covers the name (for a member of a family, the family's), or null.</summary>
        public static ChannelSpec SpecOf(string channel)
        {
            return Registry.SpecOf(channel);
        }

        // ---- invalidation ----

        /// <summary>
        /// Drops the cached values of a channel (all channels when null) and raises <see cref="ChannelChanged"/>: the next question is answered anew.
        /// A feature calls this when the state its provider depends on changes inside a game day (FR-12 item 17).
        /// </summary>
        public static void Invalidate(string channel = null)
        {
            try
            {
                Registry.Invalidate(channel);
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "modifiers: Invalidate failed", ex);
            }
        }

        /// <summary>Drops the cached values of one company in a channel (all channels when null); other companies keep theirs.</summary>
        public static void Invalidate(string channel, string companyKey)
        {
            try
            {
                Registry.Invalidate(channel, companyKey);
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "modifiers: Invalidate failed", ex);
            }
        }

        /// <summary>
        /// The value of a channel may have changed (an entry or provider came or went, an entry ended, the channel was declared, a feature changed
        /// state, <see cref="Invalidate(string)"/>): the channel name. A tooltip that shows a value subscribes to refresh itself. Handlers of
        /// features that are off are skipped, a failing handler does not stop the others.
        /// </summary>
        public static event Action<string> ChannelChanged;

        // ---- for the core ----

        /// <summary>The entries of all features in the order they were added (disabled features' included); a snapshot.</summary>
        internal static IReadOnlyList<ModifierEntry> Entries
        {
            get { return Registry.Entries; }
        }

        /// <summary>Declared channels and undeclared names in use, with entry and provider counts (the Diagnostics tab).</summary>
        internal static IReadOnlyList<ChannelInfo> Describe()
        {
            return Registry.Describe();
        }

        /// <summary>Names with entries or providers but no declaration (the self-check warns about them once every feature has registered).</summary>
        internal static IReadOnlyList<string> UndeclaredChannels()
        {
            return Registry.UndeclaredChannels();
        }

        /// <summary>
        /// Patch P1's fast path: something is registered on the channel of this value of the game's <c>ModifierType</c> (0-6). A flag per type, rebuilt
        /// only when the registry changed (<see cref="ChannelRegistry.Version"/>), so the answer is an integer compare and an array read.
        /// </summary>
        internal static bool HasAnyForType(int modifierType)
        {
            ChannelRegistry registry = Registry;
            if (_typeFlagsVersion != registry.Version) RebuildTypeFlags(registry);
            return (uint)modifierType < (uint)TypeFlags.Length && TypeFlags[modifierType];
        }

        /// <summary>
        /// <see cref="Get"/> for a caller that guards itself (patch P1 counts a failure at its own site): the same value, but a failure throws.
        /// The caller has checked <see cref="HasAny"/> when it wants the allocation-free path.
        /// </summary>
        internal static float Evaluate(string channel, ModifierContext ctx)
        {
            ModifierContext saved = _current;
            try
            {
                ctx = ctx.WithKey();
                _current = ctx;
                return Registry.Get(channel, ctx.ToQuery());
            }
            finally
            {
                _current = saved;
            }
        }

        /// <summary>
        /// Moves the registry to the game's day: entries that ended are removed, the cache is emptied when the day changed. Used on load (before the
        /// entries are restored), before <c>GameReady</c> and when a temporary entry is added while the registry has no day yet. Does nothing outside the game scene.
        /// </summary>
        internal static void SyncToday()
        {
            DateTime today = ModTicks.Today;
            if (today.Year <= 1) return;

            ReportEnded(Registry.SetToday(today), today);
        }

        // ---- start ----

        /// <summary>
        /// Declare stage: the block <c>modifiers</c> joins the side file, the 27 channels of <c>core.json</c> are declared (before any feature registers,
        /// D21), the registry follows the game's day, the reputation's drift and the states of the features.
        /// </summary>
        [CoreInit(CoreInitStage.Declare)]
        internal static void Declare()
        {
            if (_declared) return;
            _declared = true;

            CoreSection.RegisterPart(ModifiersSaveParticipant.Instance);
            DeclareCoreChannels();

            // The day also moves during the new game's fast-forward and the loading screen: temporary entries and the one-day cache count real game days.
            ModTicks.Subscribe(CoreId, TickPhase.DayPassed, OnDayPassed, DayPriority, TickOptions.RunDuringFastForward | TickOptions.RunDuringLoading);
            ModTicks.BeforeGameReady += OnBeforeGameReady;
            ModReputation.Drifted += OnReputationDrifted;
            ModFeatures.StateChanged += OnFeatureStateChanged;
        }

        /// <summary>The <c>channels</c> array of <c>core.json</c> into the registry; a bad row is a warning and the rest is declared.</summary>
        private static void DeclareCoreChannels()
        {
            try
            {
                JsonObject core = ModContent.LoadConfig(ModContent.CoreFeatureId, ModContent.CoreFile, ModContent.CoreSchema);
                JsonArray channels = core.GetArray("channels");

                var problems = new List<string>();
                List<ChannelSpec> specs = ModChannels.ReadSpecs(channels, problems);
                foreach (string problem in problems) ModLog.Warn(CoreId, "modifiers: " + ModContent.CoreFile + " " + problem);

                int declared = 0;
                foreach (ChannelSpec spec in specs)
                {
                    if (Registry.Declare(spec)) declared++;
                }
                ModLog.Debug(CoreId, "modifiers: " + declared + " of " + (channels == null ? 0 : channels.Count) + " channel(s) of " + ModContent.CoreFile + " declared");
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "modifiers: the channels of core.json could not be declared; every channel uses the default fuse until they are", ex);
            }
        }

        // ---- the engine's reports ----

        private static ChannelRegistry CreateRegistry()
        {
            var registry = new ChannelRegistry(ModFeatures.IsActive);
            registry.Warning += OnRegistryWarning;
            registry.ProviderFailed += OnProviderFailed;
            registry.ChannelChanged += OnRegistryChannelChanged;
            return registry;
        }

        private static void OnRegistryWarning(string featureId, string message)
        {
            ModLog.Warn(string.IsNullOrEmpty(featureId) ? CoreId : featureId, "modifiers: " + message);
        }

        /// <summary>A provider threw or returned an unusable number: x1 was used; the failure counts against the feature that owns the provider (FR-2 item 3).</summary>
        private static void OnProviderFailed(string featureId, string channel, Exception exception)
        {
            ModHooks.ReportFault(featureId, "ModModifiers/provider " + channel, exception);
        }

        private static void OnRegistryChannelChanged(string channel)
        {
            ModHooks.Raise("ModModifiers.ChannelChanged", ChannelChanged, channel);
        }

        // ---- the game's moments ----

        /// <summary><c>DateController.OnDayPassed</c>: the day is over - entries that end today are removed, the cache of the day goes.</summary>
        private static void OnDayPassed()
        {
            DateTime today = ModTicks.Today;
            if (today.Year <= 1) return;

            ReportEnded(Registry.SetToday(today), today);
        }

        /// <summary>Just before <c>GameReady</c>: the game's date is the registry's day, and nothing cached in the scene that went is kept.</summary>
        private static void OnBeforeGameReady(GameReadyReason reason)
        {
            if (!ModHooks.CanRun(CoreId, ReadySite)) return;
            try
            {
                SyncToday();
                Registry.Invalidate();
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, ReadySite, ex);
            }
        }

        /// <summary>The monthly drift moved the reputations that providers read (FR-13): the day's cache is stale.</summary>
        private static void OnReputationDrifted()
        {
            Registry.Invalidate();
        }

        /// <summary>
        /// A feature was switched on or off, faulted or became unavailable: the entries and providers it owns start or stop applying. Every channel is
        /// dropped, not only those where the feature has records - a provider of another feature may look at this feature's state.
        /// </summary>
        private static void OnFeatureStateChanged(string featureId, FeatureState state)
        {
            Registry.Invalidate();
        }

        // ---- internals ----

        /// <summary>The context of the question being answered, completed, for the provider that is being called.</summary>
        private static ModifierContext ProviderContext()
        {
            return _current.ForProvider();
        }

        private static void RebuildTypeFlags(ChannelRegistry registry)
        {
            for (int type = 0; type < TypeFlags.Length; type++) TypeFlags[type] = registry.HasAny(ModChannels.OfModifierType(type));
            _typeFlagsVersion = registry.Version;
        }

        /// <summary>A temporary entry needs the day to count from; before the game scene exists the registry has none and takes the game's when it can.</summary>
        private static void EnsureDay()
        {
            if (Registry.Today.Year > 1) return;

            SyncToday();
        }

        /// <summary>A record from a feature the registry does not know never applies; say so once (an id typed wrong is the usual cause).</summary>
        private static void WarnIfUnknownOwner(string featureId, string what)
        {
            if (string.IsNullOrEmpty(featureId) || featureId == CoreId) return;
            if (ModFeatures.All.Count == 0 || ModFeatures.Find(featureId) != null) return;
            if (!UnknownOwnersWarned.Add(featureId)) return;

            ModLog.Warn(featureId, "modifiers: " + what + " belongs to '" + featureId + "', which is not a feature of the mod; it will never apply");
        }

        private static void ReportEnded(IReadOnlyList<ModifierEntry> ended, DateTime today)
        {
            if (ended == null || ended.Count == 0) return;

            var text = new StringBuilder();
            for (int i = 0; i < ended.Count && i < 8; i++)
            {
                if (i > 0) text.Append(", ");
                text.Append(ended[i].Id).Append(' ').Append(ended[i].FeatureId).Append(':').Append(ended[i].Channel);
            }
            if (ended.Count > 8) text.Append(", ...");
            ModLog.Debug(CoreId, "modifiers: " + ended.Count.ToString(CultureInfo.InvariantCulture) + " entr" + (ended.Count == 1 ? "y" : "ies") + " ended on "
                                 + today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " and " + (ended.Count == 1 ? "was" : "were") + " removed (" + text + ")");
        }
    }
}
