using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.EventSystem;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The entry point of the feature <c>historical-events</c> (design.md, Component 1; FR-17): a calendar of real events of the industry 1971-2025 that
    /// finds its participants in the game being played. The feature only decides <i>when</i> an event fires and <i>for whom</i>; the effects, the windows,
    /// the timers, the mails and the log are the event system's (the pack <c>historical-events</c> is registered with it), and everything that changes the
    /// game's formulas goes through the channels of <c>ModModifiers</c>. The feature has no patch of its own on <c>Company.AddCpu</c>,
    /// <c>AICompany.AddCpu</c>, <c>DateController.NextMonth</c> or <c>CentralBank.DebtLimit</c>.
    /// <para>
    /// This file is the entry point: the registration (the settings, the pack, the section of the side save file), the loading of the calendar and the wiring
    /// of the parts. Those that decide when an entry fires: the predicates <c>hist.*</c> (<see cref="HistConditions"/>), the scheduler and its ticks
    /// (<see cref="CalendarScheduler"/>), the dispatcher (<see cref="HistoricalDispatcher"/>) and the catch-up of a late start (<see cref="CatchUpService"/>).
    /// The effect handlers (TSK-009..015: the six services, the bridge to the flaw mechanic <c>ForceDesignFlaw</c> and the flag <c>HistFlag</c>), the provider of
    /// <c>market.rating.cpu</c> and the AI chooser of the decisions (TSK-016) are wired in <see cref="RegisterServices"/>. The page of the hub "Хроника отрасли"
    /// (<see cref="ChroniclePage"/>, TSK-022), the optional letters about the game's own events (<see cref="BuiltInEventNews"/>, TSK-023) and the self-check together with
    /// the switching off and on in the middle of a game (<see cref="HistSelfCheck"/>, <see cref="OnEnabled"/>, <see cref="OnDisabled"/>, TSK-024) are wired here too. The
    /// provider of <c>acquisition.price</c> (the antitrust surcharge, <see cref="AcquisitionAntitrust"/>, TSK-028) stands next to the provider of <c>market.rating.cpu</c>
    /// in <see cref="RegisterServices"/>.
    /// </para>
    /// </summary>
    [ModFeature]
    public sealed class HistoricalEventsFeature : IModFeature
    {
        /// <summary>The slug of the feature: the key of its content, strings, settings and save section, and the namespace <c>PTMod.Features.HistoricalEvents</c>.</summary>
        public const string FeatureId = HistIds.Feature;

        private PackOptions _pack;
        private HistSaveSection _save;
        private HistRuntime _runtime;
        private CalendarScheduler _scheduler;
        private HistServices _services;
        private HistoricalAiChooser _chooser;
        private int _chooserPriority;
        private IReadOnlyList<CalendarEntry> _calendar = new CalendarEntry[0];
        private HistContentParams _params = HistContentParams.Defaults();
        private bool _calendarLoaded;
        private bool _eventsChecked;
        private bool _selfChecked;
        private bool _lockWarned;

        /// <summary>The running feature; set in <see cref="Register"/>. Check <c>ModFeatures.IsActive(FeatureId)</c> before using what it holds.</summary>
        public static HistoricalEventsFeature Instance { get; private set; }

        public string Id
        {
            get { return FeatureId; }
        }

        public string NameKey
        {
            get { return "hist.feature.name"; }
        }

        public string DescriptionKey
        {
            get { return "hist.feature.desc"; }
        }

        /// <summary>
        /// Hard dependency: the event system. <c>rival-personalities</c>, <c>design-flaw-recalls</c>, <c>rival-acquisitions</c> and
        /// <c>gpu-product-line</c> are soft links (the feature works without them, with fallbacks) and are not listed.
        /// </summary>
        public string[] DependsOn
        {
            get { return new[] { HistIds.EventSystem }; }
        }

        public bool EnabledByDefault
        {
            get { return true; }
        }

        public bool SupportsHotToggle
        {
            get { return true; }
        }

        // ---- the state of the feature ----

        /// <summary>
        /// The valid entries of the calendar, in the order of the file. Empty until the game is ready (<see cref="CalendarLoaded"/>): the conditions
        /// need the predicates and the check of the events needs the loaded packs. Never null.
        /// </summary>
        internal IReadOnlyList<CalendarEntry> Calendar
        {
            get { return _calendar; }
        }

        /// <summary>
        /// The section of this feature in the side file of the save in play was written by a newer mod (the design: "the feature is off for this
        /// save"). The core handed out defaults and keeps the original section, which it writes back unchanged; if the calendar played on the empty state
        /// it would fire again what the player's file says has fired. The scheduler does nothing for such a save; asks the core every time, so
        /// that a new game after it, or the menu, is free at once.
        /// </summary>
        internal bool SaveLocked
        {
            get { return ModSave.IsSectionNewer(FeatureId); }
        }

        /// <summary>The calendar was read and judged (the game was ready once in this session).</summary>
        internal bool CalendarLoaded
        {
            get { return _calendarLoaded; }
        }

        /// <summary>The numbers of <c>params.json</c>; the defaults of the code until the calendar is loaded. Never null.</summary>
        internal HistContentParams Params
        {
            get { return _params; }
        }

        /// <summary>The section of the side save file (its <c>Data</c> is the state of the scheduler and the services). Null before <see cref="Register"/>.</summary>
        internal HistSaveSection Save
        {
            get { return _save; }
        }

        /// <summary>What the parts that touch the game share (the world, the share cache, the roles, the evaluator). Null before <see cref="Register"/>.</summary>
        internal HistRuntime Runtime
        {
            get { return _runtime; }
        }

        /// <summary>The scheduler of the calendar (statuses, the fired entries, the debug fire). Null before <see cref="Register"/>.</summary>
        internal CalendarScheduler Scheduler
        {
            get { return _scheduler; }
        }

        /// <summary>The services of the non-standard effects (cases, suits, export rules, price wars, patent disputes, races). Null before <see cref="Register"/>.</summary>
        internal HistServices Services
        {
            get { return _services; }
        }

        /// <summary>The switch of a category in the settings (used by the scheduler, the chronicle and the antitrust surcharge); an unknown category is off.</summary>
        public static bool IsCategoryActive(string category)
        {
            return HistSettings.IsCategoryEnabled(category);
        }

        // ---- IModFeature ----

        /// <summary>
        /// Declares the parameters, the pack of the event system, the section of the side save file, the predicates <c>hist.*</c>, the scheduler with the
        /// dispatcher and the catch-up, and their ticks (day, month, the end of the fast-forward), the page of the chronicle (<c>ctx.HubPage</c>, TSK-022) and the patch
        /// class of the letter about the spin-off of the foundries (<c>ctx.Patch</c>, TSK-023). Runs once per session. The effect handlers, the provider of
        /// <c>market.rating.cpu</c> and that of <c>acquisition.price</c> and the AI chooser (<c>EventAi.AddChooser</c>) are registered by <see cref="RegisterServices"/>.
        /// </summary>
        public void Register(FeatureContext ctx)
        {
            Instance = this;
            HistSettings.Declare(ctx);

            // The engine keeps this object and reads Severity whenever it resolves an effect, so a change of the intensity is made in place.
            _pack = new PackOptions { SettingsFeatureId = FeatureId, Severity = HistSettings.Intensity };
            EventRegistry.RegisterPack(HistIds.PackId, HistIds.PackFolder, _pack);

            _save = new HistSaveSection(() => _calendar, CurrentDay);
            ctx.SaveParticipant(_save);

            // The predicates must be there before the calendar is read: the parser refuses an entry that names one it does not know.
            HistConditions.Register();

            _runtime = new HistRuntime(this);
            HistRoleScope.SetAmbient(_runtime.AmbientFrame());
            var dispatcher = new HistoricalDispatcher(_runtime);
            var catchUp = new CatchUpService(this, _runtime);
            _scheduler = new CalendarScheduler(this, _runtime, dispatcher, catchUp);

            // The order inside a month: the shares first (the scheduler and, later, the services read them), then the scheduler. A day has one handler.
            ctx.OnTick(TickPhase.DayPassed, _scheduler.OnDay);
            ctx.OnTick(TickPhase.MonthPassed, OnShareRebuild, HistTickPriority.ShareCache);
            ctx.OnTick(TickPhase.MonthPassed, _scheduler.OnMonth, HistTickPriority.Scheduler);
            ctx.OnTick(TickPhase.FastForwardEnded, _scheduler.OnFastForwardEnded);

            RegisterServices(ctx);

            // FR-15: the page of the hub "Хроника отрасли" (three tabs; the debug block exists in a debug build only).
            ctx.HubPage(ChroniclePage.TitleKey, ChroniclePage.Build);

            // FR-16 (Could): the letters of reference about the game's own events. One optional postfix; the game's logic is not changed.
            ctx.Patch(typeof(BuiltInEventNews));

            ModTicks.GameReady += OnGameReady;
            ModSettings.Changed += OnSettingChanged;
        }

        /// <summary>
        /// The services of the non-standard effects (TSK-009 to TSK-016): the eight effect types for the event system (the six services, <c>ForceDesignFlaw</c>, <c>HistFlag</c>), the AI chooser, the questions the predicates <c>hist.CaseActive</c> and
        /// <c>hist.PlayerHasCpuAbove</c> ask, the provider of <c>market.rating.cpu</c> (the export rules and the races), the provider of <c>acquisition.price</c> (the antitrust
        /// surcharge) and the monthly and daily passes. The
        /// providers are registered once and never saved (the contract of <c>ModModifiers</c>); whatever they read is in the section of the save.
        /// </summary>
        private void RegisterServices(FeatureContext ctx)
        {
            _services = new HistServices(this, _runtime);

            var handlers = new IEventEffectHandler[]
            {
                new CaseService(_services), new SettlementService(_services), new ExportRuleService(_services),
                new PriceWarService(_services), new DisputeService(_services), new RaceService(_services),
                new FlawBridge(_services), new HistFlagEffect(_services),
            };
            foreach (IEventEffectHandler handler in handlers) EventEffects.Register(handler.Type, handler);

            _runtime.State.CaseActive = (caseId, targetNotPlayer) => _services.Cases.IsActive(caseId, targetNotPlayer);
            _runtime.State.ExportThreshold = ruleId => _services.Export.ThresholdOf(ruleId);

            ModModifiers.RegisterReasonedProvider(FeatureId, ModChannels.MarketRatingCpu, _services.Rating.Provide);

            // FR-19: the antitrust surcharge on the price of an acquisition. Once, here: the core binds a provider to a channel by name (the order of Register of the
            // features does not matter) and skips it while the feature is off. The channel is declared and read by rival-acquisitions; without it nobody asks.
            ModModifiers.RegisterReasonedProvider(FeatureId, ModChannels.AcquisitionPrice, _services.Acquisition.Multiplier);

            // The AI chooser of the decisions hist.decide.* (FR-13). The priority is that of params.json, which is read once the game is ready; until then the default (10).
            // Adding the same chooser again moves it to its new priority (see RefreshChooserPriority).
            _chooser = new HistoricalAiChooser(() => _params);
            _chooserPriority = _params.AiChooserPriority;
            EventAi.AddChooser(_chooser, _chooserPriority);

            // The order inside a month is the design's: cases, suits, export rules, disputes, races (HistTickPriority); a failing service does not stop the next.
            ctx.OnTick(TickPhase.MonthPassed, () => ModHooks.Guard(FeatureId, "cases/month", _services.Cases.OnMonth), HistTickPriority.Cases);
            ctx.OnTick(TickPhase.MonthPassed, () => ModHooks.Guard(FeatureId, "settlements/month", _services.Settlements.OnMonth), HistTickPriority.Settlements);
            ctx.OnTick(TickPhase.MonthPassed, () => ModHooks.Guard(FeatureId, "export/month", _services.Export.OnMonth), HistTickPriority.ExportRules);
            ctx.OnTick(TickPhase.MonthPassed, () => ModHooks.Guard(FeatureId, "disputes/month", _services.Disputes.OnMonth), HistTickPriority.Disputes);
            ctx.OnTick(TickPhase.MonthPassed, () => ModHooks.Guard(FeatureId, "races/month", _services.Races.OnMonth), HistTickPriority.Races);
            ctx.OnTick(TickPhase.DayPassed, () => ModHooks.Guard(FeatureId, "priceWars/day", _services.Wars.OnDay));

            ModCompanies.Changed += OnCompaniesChanged;
        }

        /// <summary>A company spawned or went bankrupt: the saved prices of the CPUs of a bankrupt one in a price war are put back.</summary>
        private void OnCompaniesChanged()
        {
            if (_services == null || !ModFeatures.IsActive(FeatureId)) return;
            ModHooks.Guard(FeatureId, "priceWars/companies", _services.Wars.OnCompaniesChanged);
        }

        /// <summary>
        /// The feature became active: a feature switched on inside a game (the core does not raise <c>GameReady</c> for it) reads the calendar now. The
        /// entries of <c>ModModifiers</c> that the feature made earlier are not recreated: they were kept while it was off (FR-17 item 3), and their time went on, so the
        /// ones that ended meanwhile are gone. What stood still is picked up where it stopped: the cases, suits, disputes and rules by the month ticks that are delivered
        /// again, the providers by the core, which asks them again; the cached answers of the channels are dropped so that the next question is made afresh.
        /// </summary>
        public void OnEnabled()
        {
            ModHooks.Guard(FeatureId, "feature/enabled", () =>
            {
                if (!ModTicks.IsGameReady) return;

                WarnIfLocked();
                EnsureCalendar();
                if (ModTicks.ReadyReason == GameReadyReason.Loaded) _save.TryApplyDefaults();
                _scheduler.OnEnabled();
                RunSelfCheckOnce();
                BuiltInEventNews.Attach();
                InvalidateProviders();
            });
        }

        /// <summary>
        /// The feature stopped (FR-17 item 3). <c>ModModifiers.RemoveAll</c> is NOT called: the entries of the feature stay in the save and are not applied
        /// while it is off. The providers answer 1 (the core skips the providers of a feature that is not active; the cached answers are dropped here), the cases, suits,
        /// disputes and rules stand still (their ticks are not delivered), and the price wars end now: the prices of the AI go back (<c>RestorePriceCut</c> or the saved targets).
        /// </summary>
        public void OnDisabled()
        {
            // FR-17 item 3: the providers answer 1 (the core skips them while the feature is off), the cases and disputes stand still (no ticks), the prices of the AI go back.
            ModHooks.Guard(FeatureId, "feature/disabled", () =>
            {
                if (_services == null) return;

                _services.Wars.OnFeatureDisabled();
                _services.Export.Forget();
                _services.Races.Forget();
                InvalidateProviders();
            });
        }

        /// <summary>The answers the providers of the feature gave are dropped (the channels are asked again): <c>market.rating.cpu</c> and <c>acquisition.price</c> (FR-17 item 3).</summary>
        private static void InvalidateProviders()
        {
            ModModifiers.Invalidate(ModChannels.MarketRatingCpu);
            ModModifiers.Invalidate(ModChannels.AcquisitionPrice);
        }

        // ---- internals ----

        /// <summary>The game state is ready (a save was loaded, or a new game finished its fast-forward): the calendar can be judged now.</summary>
        private void OnGameReady(GameReadyReason reason)
        {
            ModHooks.Guard(FeatureId, "feature/gameReady", () =>
            {
                WarnIfLocked();
                EnsureCalendar();

                // A loaded save without a section gets its statuses by the date now; a new game is set up by the scheduler (after the fast-forward, or here).
                if (reason == GameReadyReason.Loaded) _save.TryApplyDefaults();
                _scheduler.OnGameReady(reason);

                RunSelfCheckOnce();
                BuiltInEventNews.Attach();

                // A price asked for before the game was ready was answered with 1 and may be remembered.
                ModModifiers.Invalidate(ModChannels.AcquisitionPrice);
            });
        }

        /// <summary>One warning per locked save (the lock is the core's answer, see <see cref="SaveLocked"/>).</summary>
        private void WarnIfLocked()
        {
            if (!SaveLocked)
            {
                _lockWarned = false;
                return;
            }

            if (_lockWarned) return;
            _lockWarned = true;
            ModLog.Warn(FeatureId, "save: the section of this feature was written by a newer mod; the calendar does nothing for this save and the section is kept as it is");
        }

        /// <summary>Follows the intensity setting into the pack of the event system.</summary>
        private void OnSettingChanged(string featureId, string key)
        {
            if (featureId != FeatureId) return;

            ModHooks.Guard(FeatureId, "feature/settingChanged", () =>
            {
                if (key == HistParamSpecs.Intensity && _pack != null) _pack.Severity = HistSettings.Intensity;
                if (_runtime != null) _runtime.OnSettingChanged(key);

                // A category that a provider reads moved: the cached answers of its channel are out of date.
                AcquisitionAntitrust.OnSettingChanged(key);
                if (key == HistCategories.Export || key == HistCategories.Race) ModModifiers.Invalidate(ModChannels.MarketRatingCpu);
            });
        }

        /// <summary>
        /// <c>TickPhase.MonthPassed</c>: the market shares of the last full months are read again (the scheduler and the services use the cache). The shares moved, so the prices of
        /// acquisitions, which the antitrust surcharge works out from them, are asked again (<c>ModModifiers.Invalidate("acquisition.price")</c>, FR-19 item 4).
        /// </summary>
        private void OnShareRebuild()
        {
            ModHooks.Guard(FeatureId, "feature/shares", () =>
            {
                _runtime.Shares.Rebuild(_runtime.World, _params.ShareCacheMonths);
                ModModifiers.Invalidate(ModChannels.AcquisitionPrice);
            });
        }

        /// <summary>
        /// Reads <c>params.json</c> and <c>calendar.json</c> once per session. Fewer than <see cref="CalendarValidator.MinValidEntries"/> valid entries make the
        /// feature Unavailable until the game restarts (design.md, "Самопроверка на старте"): a calendar that small is a broken install or a broken override.
        /// </summary>
        internal void EnsureCalendar()
        {
            if (_calendarLoaded) return;

            _params = CalendarContent.LoadParams();
            RefreshChooserPriority();

            IEventCatalog packs = CalendarContent.LoadedPacks();
            if (packs == null) ModLog.Warn(FeatureId, "content: the event packs are not loaded yet; the events of the calendar are checked against them when they are");

            _calendar = CalendarContent.LoadCalendar(packs);
            _calendarLoaded = true;
            _eventsChecked = packs != null;

            if (!CalendarValidator.IsUsable(_calendar.Count))
            {
                string why = "calendar.json has " + _calendar.Count + " valid entries, at least " + CalendarValidator.MinValidEntries + " are needed";
                ModLog.Error(FeatureId, "content: " + why + "; the feature is unavailable");
                ModFeatures.MarkUnavailable(FeatureId, why);
            }
        }

        /// <summary>The priority of the AI chooser follows <c>aiChooserPriority</c> of <c>params.json</c> (the file is read after the registration): the same chooser is added again and the event system moves it.</summary>
        private void RefreshChooserPriority()
        {
            if (_chooser == null || _chooserPriority == _params.AiChooserPriority) return;

            _chooserPriority = _params.AiChooserPriority;
            EventAi.AddChooser(_chooser, _chooserPriority);
        }

        /// <summary>
        /// The second phase of the existence rule (requests, item 3): the calendar was read before the event system had read its packs, so the entries are
        /// now checked against them and those that fire an event the pack does not have are dropped with an error in the log. Does nothing once done or
        /// while the packs are not loaded. True when entries were dropped (the scheduler then rebuilds its queue).
        /// </summary>
        internal bool EnsureEventsChecked()
        {
            if (_eventsChecked || !_calendarLoaded) return false;

            IEventCatalog packs = CalendarContent.LoadedPacks();
            if (packs == null) return false;
            _eventsChecked = true;

            var kept = new List<CalendarEntry>(_calendar.Count);
            foreach (CalendarEntry entry in _calendar)
            {
                var problems = new List<string>();
                if (CalendarValidator.ValidateEventsExist(entry, packs, problems))
                {
                    // Only warnings can be left (an event without a text or a cause): the entry works, the gap is a line in the log.
                    foreach (string problem in problems) ModLog.Warn(FeatureId, "content: " + problem);
                    kept.Add(entry);
                    continue;
                }
                foreach (string problem in problems) ModLog.Error(FeatureId, "content: " + problem);
            }

            bool dropped = kept.Count != _calendar.Count;
            _calendar = kept;
            RunSelfCheckOnce();

            if (!CalendarValidator.IsUsable(_calendar.Count))
            {
                string why = "only " + _calendar.Count + " entries of calendar.json fire events the pack has, at least " + CalendarValidator.MinValidEntries + " are needed";
                ModLog.Error(FeatureId, "content: " + why + "; the feature is unavailable");
                ModFeatures.MarkUnavailable(FeatureId, why);
            }
            return dropped;
        }

        /// <summary>
        /// The self-check of the feature (<see cref="HistSelfCheck"/>), once per session: when the calendar is read and the event system has read its packs, which can be at
        /// <c>GameReady</c> or later (the first day of a new game). It needs both: the events the calendar fires are looked for in the packs.
        /// </summary>
        internal void RunSelfCheckOnce()
        {
            if (_selfChecked || !_calendarLoaded || CalendarContent.LoadedPacks() == null) return;

            _selfChecked = true;
            HistSelfCheck.RunAndLog(this);
        }

        /// <summary>The day number of the running game, or null in the menu and while a scene loads (nothing can be said about statuses then).</summary>
        private static int? CurrentDay()
        {
            if (!ModTicks.IsGameReady) return null;

            DateTime today = ModTicks.Today;
            if (today == default(DateTime)) return null;
            return SimpleDate.FromDateTime(today).Days;
        }
    }
}
