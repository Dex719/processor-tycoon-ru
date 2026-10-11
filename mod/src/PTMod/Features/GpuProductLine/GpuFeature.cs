using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using PTMod.Core;
using UnityEngine;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The entry point of gpu-product-line, the GPU product line next to the CPU (design.md, "GpuFeature"; FR-1, D15). The core finds it by
    /// <c>[ModFeature]</c> and gives it everything a feature needs: settings (<c>ctx.Param</c>), the page in the mods hub (<c>ctx.HubPage</c>), the section of the
    /// side save file (<c>ctx.SaveParticipant</c>), the days (<c>ctx.OnTick</c>) and the patch GP1 of the production screen (<c>ctx.Patch</c>). The feature does not subscribe to the
    /// events of the game itself (<c>Player.OnNewGame</c>, <c>SaveHandler.OnLoaded</c>, <c>BankruptcyHandler.OnCompanyWentBankrupt</c> or the day events of
    /// <c>DateController</c>): the days, the start of a session and the save come from the core, and every handler runs under <see cref="Guard"/>, so an exception
    /// is logged as <c>[gpu-product-line]</c> and counted against the feature by <c>ModHooks</c> (this class keeps no counters of its own).
    /// </summary>
    [ModFeature]
    public sealed class GpuFeature : IModFeature
    {
        public const string FeatureId = "gpu-product-line";

        private readonly bool[] _unavailable = new bool[Enum.GetValues(typeof(GpuCapability)).Length];
        private bool _contentChecked;

        /// <summary>The feature of this session; null until the core has called <see cref="Register"/>.</summary>
        public static GpuFeature Instance { get; private set; }

        // ---- IModFeature ----

        public string Id
        {
            get { return FeatureId; }
        }

        public string NameKey
        {
            get { return "gpu.feature.name"; }
        }

        public string DescriptionKey
        {
            get { return "gpu.feature.desc"; }
        }

        /// <summary>No hard dependencies: <c>rival-acquisitions</c> and <c>historical-events</c> use the API and the channels of this feature themselves.</summary>
        public string[] DependsOn
        {
            get { return new string[0]; }
        }

        public bool EnabledByDefault
        {
            get { return true; }
        }

        public bool SupportsHotToggle
        {
            get { return true; }
        }

        /// <summary>The content of the feature, loaded in <see cref="Register"/>. Never null after it.</summary>
        public GpuContentDb Content { get; private set; }

        /// <summary>The state kept in the save. Never null after <see cref="Register"/>.</summary>
        public GpuState State { get; private set; }

        // ---- services (Logic/Services; the game reaches them only through GpuGameAdapter) ----

        /// <summary>The founding of the division (FR-1). Never null after <see cref="Register"/>.</summary>
        public GpuDivision Division { get; private set; }

        /// <summary>The GPU laboratory (FR-2).</summary>
        public GpuResearch Research { get; private set; }

        /// <summary>The designer without its window (FR-3): the tab and the projects evaluate a design through it.</summary>
        public GpuDesigner Designer { get; private set; }

        /// <summary>The development projects (FR-4).</summary>
        public GpuProjects Projects { get; private set; }

        /// <summary>The reserve of factory lines and the production on the own factory, or by order when fabless (FR-5).</summary>
        public GpuProduction Production { get; private set; }

        /// <summary>The GPU market: the day of sales, the background, the seed (FR-6, FR-9).</summary>
        public GpuMarketService Market { get; private set; }

        /// <summary>The player's lineup: the release, the settings of a card, the retirement, the storage and the books (FR-7).</summary>
        public GpuLineup Lineup { get; private set; }

        /// <summary>The segments and eras of the market: what opens and closes with the calendar, the requirements, the mode of the market (FR-11).</summary>
        public GpuEras Eras { get; private set; }

        /// <summary>The named makers: who enters, leaves and merges, the cards they launch, the AI companies they are bound to (FR-12, FR-13).</summary>
        public GpuRivals Rivals { get; private set; }

        /// <summary>The letters of the market: eras, segments, makers and new leaders (FR-14).</summary>
        public GpuNews News { get; private set; }

        /// <summary>The platform synergy of CPU and GPU: the rings of the shares, the bonuses of the month, the table the two providers read (FR-15).</summary>
        public GpuSynergy Synergy { get; private set; }

        /// <summary>The shared experience of a process node: what patch GP2 adds to the lines of the player's company on a node (FR-16).</summary>
        public GpuSharedLearning SharedLearning { get; private set; }

        /// <summary>The console orders: the offers of the console makers, their deliveries and breaches (FR-19, Could); null until <see cref="Register"/>.</summary>
        public GpuConsoleOrders ConsoleOrders { get; private set; }

        /// <summary>What <c>GpuApi</c> and the predicates of <c>ModConditions</c> answer (FR-18); null until <see cref="Register"/>.</summary>
        public GpuApiService Api { get; private set; }

        /// <summary>The channels of ModModifiers and the synergy providers.</summary>
        internal GpuModifierBridge Bridge { get; private set; }

        /// <summary>The game for the services.</summary>
        internal GpuGameAdapter Game { get; private set; }

        /// <summary>
        /// The timer of the handlers of the days and months (NFR-1: a day of the feature at most 0.5 ms, a month at most 5 ms). It measures only while the verbose log is
        /// on (<c>ModSettings.VerboseLog</c>) and writes one line a game month to the log (<see cref="LogPerf"/>).
        /// </summary>
        internal GpuPerfMeter Perf { get; private set; }

        // ---- settings (ModSettings.GetBool(FeatureId, key); the core keeps whether the feature itself is on) ----

        public bool RivalsEnabled
        {
            get { return ModSettings.GetBool(FeatureId, "rivals"); }
        }

        public bool SynergyEnabled
        {
            get { return ModSettings.GetBool(FeatureId, "synergy"); }
        }

        public bool NewsEnabled
        {
            get { return ModSettings.GetBool(FeatureId, "news"); }
        }

        /// <summary>"CPU first" (D18): the GPU take only the lines the CPU leave. Off by default.</summary>
        public bool CpuPriority
        {
            get { return ModSettings.GetBool(FeatureId, "cpuPriority"); }
        }

        public bool ConsoleOrdersEnabled
        {
            get { return ModSettings.GetBool(FeatureId, "consoleOrders"); }
        }

        /// <summary>Whether an ability of the feature is available; the self-check switches abilities off with <see cref="SetAvailable"/>.</summary>
        public bool IsAvailable(GpuCapability capability)
        {
            int index = (int)capability;
            return index >= 0 && index < _unavailable.Length && !_unavailable[index];
        }

        internal void SetAvailable(GpuCapability capability, bool available)
        {
            int index = (int)capability;
            if (index >= 0 && index < _unavailable.Length) _unavailable[index] = !available;
        }

        /// <summary>
        /// Runs a handler of the feature under <c>ModHooks.Guard</c>: an exception is written to the log with the id of the feature and the site
        /// (<c>gpu/&lt;siteId&gt;</c>) and counted against the feature; after too many the core switches it off. There is no other error counter.
        /// </summary>
        internal void Guard(string siteId, Action body)
        {
            ModHooks.Guard(FeatureId, "gpu/" + siteId, body);
        }

        /// <summary>
        /// <see cref="Guard"/> with the stopwatch of the debug mode (NFR-1): the handler is timed as a part of <paramref name="kind"/> (<see cref="GpuPerfMeter.Day"/>,
        /// <see cref="GpuPerfMeter.Month"/>, <see cref="GpuPerfMeter.UiDay"/>) when the verbose log is on; otherwise it costs one test of the flag. The meter never throws.
        /// </summary>
        internal void Timed(string kind, string siteId, Action body)
        {
            GpuPerfMeter perf = Perf;
            long started = perf != null ? perf.Begin() : 0L;
            Guard(siteId, body);
            if (started != 0L) perf.End(kind, started, ModTicks.Today);
        }

        // ---- Register / OnEnabled / OnDisabled ----

        public void Register(FeatureContext ctx)
        {
            Instance = this;

            LoadContent();
            State = new GpuState(Content, LogSaveWarning);

            ctx.Param(Switch("rivals", true));
            ctx.Param(Switch("synergy", true));
            ctx.Param(Switch("news", true));
            ctx.Param(Switch("consoleOrders", true));
            ctx.Param(Switch("cpuPriority", false));

            ctx.HubPage("gpu.hub.title", BuildHubPage);
            ctx.SaveParticipant(new GpuSaveParticipant(State));

            // GP1: the cards' lines on the production screen of the game; optional, a failure switches off GpuCapability.ProductionUi only.
            ctx.Patch(typeof(ProductionWindowPatch));

            // GP2: the lines of the cards count into the learning of a process node (FR-16); optional, a failure switches off GpuCapability.SharedLearning only.
            ctx.Patch(typeof(TotalLinesAllocatedPatch));

            CreateServices();

            // The days come from the core, without RunDuringFastForward / RunDuringLoading: nothing moves while a new game is fast-forwarded or a save
            // loads. PreContractSellTick is the first phase of the day that touches the factory: the cards take their lines before any CPU is produced.
            ctx.OnTick(TickPhase.PreContractSellTick, ProductionTick);
            ctx.OnTick(TickPhase.Tick, ResearchTick);
            ctx.OnTick(TickPhase.Tick, ProjectsTick);
            ctx.OnTick(TickPhase.MonthPassed, MonthTick);

            // The market runs without the division too (FR-6 item 8). It sells after the CPUs of the day (MarketSellTick); the day closes on DayPassed; the month
            // renews the background after the cache of the nodes (priority: higher first) and closes the books of the cards after the market.
            ctx.OnTick(TickPhase.MarketSellTick, MarketTick);
            ctx.OnTick(TickPhase.DayPassed, MarketDayTick);
            ctx.OnTick(TickPhase.DayPassed, LineupDayTick);
            ctx.OnTick(TickPhase.MonthPassed, MarketMonthTick, MarketMonthPriority);
            ctx.OnTick(TickPhase.MonthPassed, LineupMonthTick, LineupMonthPriority);

            // Stage 2. The vendors fill their stock before the market sells and look at their companies at the end of the day; the calendar of the segments and the
            // vendors move on the 1st of a month, after the cache of the nodes and before the market renews the background.
            ctx.OnTick(TickPhase.MarketSellTick, RivalsStockTick, RivalsStockPriority);
            ctx.OnTick(TickPhase.DayPassed, RivalsDayTick);
            ctx.OnTick(TickPhase.MonthPassed, ErasMonthTick, ErasMonthPriority);
            ctx.OnTick(TickPhase.MonthPassed, RivalsMonthTick, RivalsMonthPriority);

            // Stage 3. The synergy samples the shares of the day after the market closed it, and counts its bonuses on the 1st of a month after the market and the vendors.
            ctx.OnTick(TickPhase.DayPassed, SynergyDayTick, SynergyDayPriority);
            ctx.OnTick(TickPhase.MonthPassed, SynergyMonthTick, SynergyMonthPriority);

            // Console orders (FR-19): the cards go to the console makers from the stock before the shop opens (ContractSellTick runs before MarketSellTick); the offers, the terms and the
            // buttons of the letters are looked at when the day closes. The hub asks before it switches the feature off while an order runs; the letters come back through the adapter.
            ctx.OnTick(TickPhase.ContractSellTick, ConsoleDeliveryTick);
            ctx.OnTick(TickPhase.DayPassed, ConsoleDayTick, ConsoleDayPriority);
            ctx.ConfirmBeforeDisable(BuildDisableConfirmation);
            BindConsoleMail();

            // The window "Видеокарты" is brought up to date once a game day while it is open (AC-8.2), after the market and the lineup closed the day.
            ctx.OnTick(TickPhase.DayPassed, UiDayTick, UiDayPriority);

            // A new game: the market of the start year is seeded once, when the fast-forward is over (it reaches every subscriber).
            ctx.OnTick(TickPhase.FastForwardEnded, SeedTick);

            // The synergy providers on market.rating.cpu and gpu.rating, once; the core skips them while the feature is off.
            Bridge.RegisterSynergyProviders(Synergy.Table.Lookup, () => SynergyEnabled);
            ModSettings.Changed += OnSettingChanged;

            // The predicates gpu.* of ModConditions (FR-18 item 4); GpuApi is the static face of the same services.
            GpuConditions.Register();

            // The fields and members of the game are there from the start: the abilities are right from the first day (the ports wait for the game).
            Guard("selfCheck/start", () => GpuSelfCheck.Run(this, false));

            // The game is ready (a save loaded, a new game fast-forwarded): the place to look at the content once more.
            ModTicks.GameReady += OnGameReady;
        }

        private void CreateServices()
        {
            Perf = new GpuPerfMeter(() => ModSettings.VerboseLog, Stopwatch.GetTimestamp, Stopwatch.Frequency, LogPerf);
            Bridge = new GpuModifierBridge();
            Game = new GpuGameAdapter(Bridge);
            Research = new GpuResearch(Content, State, Game);
            Division = new GpuDivision(Content, State, Game, Research);
            Designer = new GpuDesigner(Content, Game);
            Projects = new GpuProjects(Content, State, Game, Designer);
            Production = new GpuProduction(Content, State, Game, () => CpuPriority, () => IsAvailable(GpuCapability.Production), LogProductionWarning);
            Market = new GpuMarketService(Content, State, Game, Production, new GpuRandom(ModRandom.Stream(GpuRandom.BackgroundStream)), LogProductionWarning);
            Lineup = new GpuLineup(Content, State, Game, Production, Market);
            Eras = new GpuEras(Content, State, Game);
            Rivals = new GpuRivals(Content, State, Game, Market, new GpuRandom(ModRandom.Stream(GpuRandom.RivalsStream)), () => RivalsEnabled, LogProductionWarning);

            // Stage 3: the synergy (its table is what the providers of the two channels read), the shared experience of the nodes (patch GP2), the API of the feature.
            Synergy = new GpuSynergy(Content, State, Game, Market, () => SynergyEnabled, () => RivalsEnabled);
            Synergy.Changed += OnSynergyChanged;
            SharedLearning = new GpuSharedLearning(Content, Production, () => SynergyEnabled && IsAvailable(GpuCapability.SharedLearning));
            Api = new GpuApiService(State, Game, Division, Market, Rivals, Research, Synergy, Lineup, () => Content != null && Content.IsUsable && ModFeatures.IsActive(FeatureId));
            Api.ProductReleased += GpuApi.RaiseProductReleased;

            // The switch "Соперники" (FR-12 item 5) is asked every day; the vendors of the year come with the seed of the market (AC-9.1).
            Market.RivalsEnabled = () => RivalsEnabled;
            Market.Seeded += OnMarketSeeded;

            // The letters of the market (FR-14): they listen to the eras and to the makers; the core gives the ids and keeps the limits. A handler of an event is guarded on its own,
            // so a letter that fails does not stop the month that raised it. The switch "Новости" is asked for each leader; no letters in the fast-forward and while a save loads.
            News = new GpuNews(Content, State, Game, Eras, Rivals, new GpuMailAdapter(), GpuUi.Text, () => NewsEnabled, MailAllowed, Guard, LogProductionWarning);
            News.Bind();

            // A finished project opens the dialog of the release (FR-4 item 6); the dialog is the window's business, the service only says the card is ready.
            Projects.ReadyToRelease += OnProjectReady;

            // Console orders (Could, FR-19): the offers go out as letters with buttons; taking the tied card off production breaks the order.
            ConsoleOrders = new GpuConsoleOrders(Content, State, Game, Production, Market, Lineup, new GpuMailAdapter(), GpuUi.Text, () => ConsoleOrdersEnabled, MailAllowed, LogProductionWarning);
            Lineup.Retired += OnCardRetired;
        }

        /// <summary>
        /// The order of the monthly handlers (higher first): the cache of the nodes (0), the calendar of the segments and the eras, the vendors (they launch cards on the
        /// segments of the month), then the market (its background needs the nodes, its open segments the calendar), then the lineup.
        /// </summary>
        internal const int ErasMonthPriority = -2, RivalsMonthPriority = -5, MarketMonthPriority = -10, LineupMonthPriority = -20;

        /// <summary>The synergy counts its bonuses on the 1st of a month after the lineup closed the books (higher first).</summary>
        internal const int SynergyMonthPriority = -30;

        /// <summary>The console orders look at their offers and terms after the market and the lineup closed the day (higher first).</summary>
        internal const int ConsoleDayPriority = -20;

        /// <summary>The synergy takes the shares of the day after the market and the lineup closed it (higher first; they run at 0).</summary>
        internal const int SynergyDayPriority = -10;

        /// <summary>The vendors fill their stock before the market sells (higher first; the market runs at 0).</summary>
        internal const int RivalsStockPriority = 10;

        /// <summary>The window runs after every other handler of the day (higher first).</summary>
        internal const int UiDayPriority = -50;

        public void OnEnabled()
        {
            ModLog.Debug(FeatureId, "enabled");
            BindConsoleMail();   // the handler of the answers is the core's, one per feature: put it back in case it was taken away
            if (ModTicks.IsGameReady && PTModHost.Instance != null)
            {
                // Switched on in the middle of a session: GameReady will not come again. Look at the content after the switch is over.
                PTModHost.Instance.Run(CheckContentLater());
            }
        }

        public void OnDisabled()
        {
            ModLog.Debug(FeatureId, "disabled");
            if (Perf != null) Perf.Flush();   // the month in progress is written, not lost

            // FR-19 item 7: the running console orders are cancelled with the penalty, the open offers are closed (the player was asked in the hub, or the code did it on purpose)
            Guard("console/disabled", CancelConsoleOrders);
            GpuWindow.Close();
            GpuDesignerTab.Forget();
        }

        // ---- content ----

        private void LoadContent()
        {
            var problems = new List<string>();
            try
            {
                Content = GpuContentDb.Load(new GpuModContentSource(), problems);
            }
            catch (Exception ex)
            {
                // GpuContentDb.Load does not throw; this keeps Register from failing whatever happens.
                ModLog.Error(FeatureId, "content: cannot load", ex);
                Content = GpuContentDb.Load(null, null);
            }

            foreach (string problem in problems) ModLog.Warn(FeatureId, "content: " + problem);
            ModLog.Debug(FeatureId, "content: " + Content.Architectures.Count + " architectures, " + Content.Memories.Count + " memory types, "
                         + Content.Coolers.Count + " coolers, " + Content.ResearchNodes.Count + " research nodes, " + Content.Segments.Count + " segments");
        }

        private void OnGameReady(GameReadyReason reason)
        {
            Guard("gameReady", CheckContent);
            Guard("gameReady/ui", GpuDesignerTab.Forget);   // the design in progress belongs to the game that was left
            Guard("gameReady/session", StartSession);
            Guard("gameReady/market", StartMarket);
            Guard("gameReady/console", ConsoleSessionStart);
        }

        private IEnumerator CheckContentLater()
        {
            yield return null;
            Guard("contentCheck", CheckContent);
            Guard("session", StartSession);
            Guard("session/market", StartMarket);
            Guard("session/console", ConsoleSessionStart);
        }

        /// <summary>
        /// The market of the session (after the cache of the nodes): its windows start anew; a game that was never seeded - a save without the section (AC-9.1), or
        /// a game the feature was switched on in - is seeded for its year; a segment without a background gets one.
        /// </summary>
        private void StartMarket()
        {
            // The makers are brought up to date without a word: a maker whose company is not in this game any more leaves quietly (FR-14 item 4, the quiet migration).
            News.Silently(() =>
            {
                Eras.ApplyModeSwitchIfNeeded(State);   // a save of the other market mode (stage 1 in the eras): the cards keep their popularity, the market is seeded again
                Market.StartSession();
                Rivals.StartSession();
            });
        }

        /// <summary>The game is in its normal course: ready, not fast-forwarding, not loading. The news make letters only then (FR-14 item 4).</summary>
        private static bool MailAllowed()
        {
            return ModTicks.IsGameReady && !ModTicks.IsFastForwarding && !ModTicks.IsLoading;
        }

        /// <summary>
        /// A save was loaded or a new game is ready (or the feature was switched on in the middle of one): the caches of the session start anew - the process
        /// nodes (the only search of the scene, again once a month), the day of production, the table of the synergy - and the full self-check runs.
        /// </summary>
        private void StartSession()
        {
            Production.Reset();
            RefreshNodes();
            RebuildSynergy();
            GpuSelfCheckBook check = GpuSelfCheck.Run(this, true);
            if (check.Ok)
            {
                ModLog.Info(FeatureId, check.Summary());
                return;
            }
            ModLog.Warn(FeatureId, check.Summary());
            foreach (string problem in check.Problems) ModLog.Info(FeatureId, "self-check: " + problem);
        }

        // ---- the days ----

        private void ProductionTick()
        {
            Timed(GpuPerfMeter.Day, "production", Production.ProduceDay);
        }

        private void ResearchTick()
        {
            Timed(GpuPerfMeter.Day, "research", Research.DailyTick);
        }

        private void ProjectsTick()
        {
            Timed(GpuPerfMeter.Day, "projects", Projects.DailyTick);
        }

        private void MonthTick()
        {
            Timed(GpuPerfMeter.Month, "month/nodes", RefreshNodes);
        }

        private void MarketTick()
        {
            Timed(GpuPerfMeter.Day, "market", Market.SellDay);
        }

        private void MarketDayTick()
        {
            Timed(GpuPerfMeter.Day, "market/day", Market.DayTick);
        }

        private void MarketMonthTick()
        {
            Timed(GpuPerfMeter.Month, "market/month", Market.MonthTick);
        }

        private void LineupDayTick()
        {
            Timed(GpuPerfMeter.Day, "lineup/day", Lineup.DayTick);
        }

        private void LineupMonthTick()
        {
            Timed(GpuPerfMeter.Month, "lineup/month", Lineup.MonthTick);
        }

        private void SynergyDayTick()
        {
            Timed(GpuPerfMeter.Day, "synergy/day", Synergy.DayTick);
        }

        private void SynergyMonthTick()
        {
            Timed(GpuPerfMeter.Month, "synergy/month", Synergy.MonthTick);
        }

        private void RivalsStockTick()
        {
            Timed(GpuPerfMeter.Day, "rivals/stock", Rivals.RefillStock);
        }

        private void RivalsDayTick()
        {
            Timed(GpuPerfMeter.Day, "rivals/day", Rivals.CheckBoundCompanies);
        }

        private void ErasMonthTick()
        {
            Timed(GpuPerfMeter.Month, "eras/month", Eras.MonthTick);
        }

        private void RivalsMonthTick()
        {
            Timed(GpuPerfMeter.Month, "rivals/month", Rivals.MonthTick);
        }

        private void OnMarketSeeded(int year)
        {
            Guard("rivals/seed", () => News.Silently(() => Rivals.SeedForYear(year)));
        }

        private void SeedTick()
        {
            Guard("market/seed", SeedNewGame);
        }

        // ---- console orders (FR-19) ----

        private void ConsoleDeliveryTick()
        {
            Timed(GpuPerfMeter.Day, "console/delivery", ConsoleOrders.DeliverDay);
        }

        private void ConsoleDayTick()
        {
            Timed(GpuPerfMeter.Day, "console/day", ConsoleOrders.DayTick);
        }

        private void ConsoleSessionStart()
        {
            if (ConsoleOrders != null) ConsoleOrders.SessionStart();
        }

        private void CancelConsoleOrders()
        {
            if (ConsoleOrders != null) ConsoleOrders.CancelAll();
        }

        private void OnCardRetired(string productId)
        {
            Guard("console/retired", () => ConsoleOrders.OnProductRetired(productId));
        }

        /// <summary>The handlers of the letters of the console orders, bound to the core's mail once per call: the answers, the buttons that leave the letter open, the delivery.</summary>
        private void BindConsoleMail()
        {
            GpuMailAdapter.BindConsoleHandlers(
                (mailId, choiceId) => Guard("console/choice", () => ConsoleOrders.OnChoice(mailId, choiceId)),
                (mailId, actionId) => Guard("console/action", () => OnConsoleAction(actionId)),
                mailId => Guard("console/delivered", () => ConsoleOrders.OnMailDelivered(mailId)));
        }

        // "Открыть линейку" in the letter: the window of the cards, on the tab where the orders are.
        private static void OnConsoleAction(string actionId)
        {
            if (actionId == GpuConsoleOrders.ActionOpenLineup) GpuWindow.Open(GpuTab.Lineup);
        }

        /// <summary>AC-19.5: what the hub asks before it switches the feature off - null (nothing to ask) unless a console order runs.</summary>
        private DisableConfirmation BuildDisableConfirmation()
        {
            DisableConfirmation result = null;
            Guard("console/confirm", () =>
            {
                GpuDisableConfirmation ask = ConsoleOrders != null ? ConsoleOrders.BuildDisableConfirmation() : null;
                if (ask != null) result = new DisableConfirmation { MessageKey = ask.MessageKey, Args = ask.Args, ConfirmLabelKey = ask.ConfirmLabelKey };
            });
            return result;
        }

        private void UiDayTick()
        {
            Timed(GpuPerfMeter.UiDay, "ui/day", GpuWindow.RefreshDaily);
        }

        private void OnProjectReady(string productId)
        {
            Guard("ui/release", () => GpuReleaseDialog.OnReady(productId));
        }

        /// <summary>
        /// FR-9 item 3: the fast-forward of a new game is over - the market of the start year is seeded, once (the state was reset before the fast-forward). The cache of
        /// the nodes is built first: the game is not ready yet, so the background takes the nodes of the timeline; a segment that cannot get its card now (no frontier
        /// frequency before the companies are ready) gets it when the game is ready.
        /// </summary>
        private void SeedNewGame()
        {
            RefreshNodes();
            Market.SeedForYear(Game.Today.Year);
        }

        /// <summary>
        /// The lines of the cards the production screen shows (GP1): the reserve of today on the own factory; 0 without the division, for a fabless company or when the
        /// ability <see cref="GpuCapability.ProductionUi"/> is off - the screen is then the game's.
        /// </summary>
        internal int ProductionScreenLines
        {
            get
            {
                if (Production == null || State == null || !State.Data.divisionFounded || !IsAvailable(GpuCapability.ProductionUi) || Production.FablessToday) return 0;
                return Production.ReservedToday;
            }
        }

        /// <summary>The cache of the process nodes: the player's own, and the nodes of the saved cards and designs and of the timeline of the rivals.</summary>
        private void RefreshNodes()
        {
            var names = new List<string>();
            GpuSaveSection data = State.Data;
            if (data.products != null)
            {
                foreach (GpuProductSave product in data.products)
                {
                    if (product != null) names.Add(product.node);
                }
            }
            if (data.designs != null)
            {
                foreach (GpuDesignSave design in data.designs)
                {
                    if (design != null) names.Add(design.node);
                }
            }
            if (Content.Timeline != null && Content.Timeline.nodeByYear != null)
            {
                foreach (GpuYearNode entry in Content.Timeline.nodeByYear)
                {
                    if (entry != null) names.Add(entry.node);
                }
            }
            Game.RefreshNodes(names);
        }

        /// <summary>The bonuses of the synergy changed in the section (a load): the table of the providers is rebuilt and the channels dropped.</summary>
        internal void RebuildSynergy()
        {
            Synergy.Rebuild();
            Bridge.InvalidateSynergy();
        }

        /// <summary>The breakdown of a channel the synergy writes into, for the player's company (the tab "Рынок", FR-15 item 5); null when the player's company is not there.</summary>
        internal GpuExplainView ExplainSynergy(GpuSynergyChannel channel)
        {
            var player = ModCompanies.Resolve(ModCompanies.PlayerKey);
            if (player == null) return null;
            return Bridge.ExplainView(channel == GpuSynergyChannel.Cpu ? ModChannels.MarketRatingCpu : ModChannels.GpuRating, player);
        }

        /// <summary>The monthly count of the synergy is done (the table is rebuilt by the service): the cached values of the two channels are dropped.</summary>
        private void OnSynergyChanged()
        {
            Guard("synergy/changed", Bridge.InvalidateSynergy);
        }

        /// <summary>
        /// FR-16, patch GP2: the lines to add to <c>Company.TotalLinesAllocated(node)</c> of the player - the lines the cards took on the node today times
        /// <c>synergy.gpuLearningShare</c>; 0 with the switch <c>synergy</c> off. Called by the patch for the player's company only, every time the game asks.
        /// </summary>
        internal int SharedLearningLines(string nodeName)
        {
            GpuSharedLearning learning = SharedLearning;
            return learning == null ? 0 : learning.ExtraLines(nodeName);
        }

        private void OnSettingChanged(string featureId, string key)
        {
            if (featureId != FeatureId || key != "synergy") return;
            Guard("settings/synergy", Bridge.InvalidateSynergy);
        }

        /// <summary>
        /// FR-10 item 3: with no architecture, memory, cooler or segment left after the checks the feature cannot work, so it switches itself off
        /// and tells the player once. The files are the author's or the player's to fix; the log names what was wrong with each.
        /// </summary>
        private void CheckContent()
        {
            if (Content == null || Content.IsUsable || _contentChecked) return;
            _contentChecked = true;

            ModLog.Error(FeatureId, "content: the feature is switched off, " + Content.UnusableReason);
            ModNotify.ShowKey(FeatureId, "gpu.notify.contentUnusable", Content.UnusableReason);
            ModFeatures.SetEnabled(FeatureId, false);
        }

        // ---- settings and the hub ----

        private static ParamSpec Switch(string key, bool defaultOn)
        {
            return new ParamSpec
            {
                FeatureId = FeatureId,
                Key = key,
                LabelKey = "gpu.settings." + key,
                TooltipKey = "gpu.settings." + key + ".tip",
                Type = ParamType.Bool,
                Default = defaultOn ? 1 : 0,
                Min = 0,
                Max = 1,
            };
        }

        /// <summary>The page of the feature in the mods hub (FR-1 item 1): the state of the division, the button "Видеокарты". The switches of the settings come from the <c>ParamSpec</c>s.</summary>
        private void BuildHubPage(RectTransform page)
        {
            if (!Content.IsUsable)
            {
                ModUI.Text(page, ModL10n.L("gpu.hub.content.unusable", Content.UnusableReason));
                return;
            }

            bool founded = State.Data.divisionFounded;
            ModUI.Text(page, ModL10n.L(founded ? "gpu.hub.division.founded" : "gpu.hub.division.notFounded"));
            ModUI.Button(page, ModL10n.L("gpu.hub.open"), OpenWindowFromHub, ButtonStyle.CallToAction, ModL10n.L("gpu.hub.open.tip"));
            BuildSelfCheckLines(page);
        }

        /// <summary>The result of the self-check on the page (TSK-011): the counts and the abilities that are off.</summary>
        private void BuildSelfCheckLines(RectTransform page)
        {
            GpuSelfCheckBook check = GpuSelfCheck.Last;
            if (check == null)
            {
                ModUI.Text(page, ModL10n.L("gpu.hub.selfCheck.notRun"));
                return;
            }
            ModUI.Text(page, ModL10n.L(check.Ok ? "gpu.hub.selfCheck.ok" : "gpu.hub.selfCheck.problems",
                check.PatchesApplied, check.PatchesDeclared, check.FieldsFound, GpuSelfCheckBook.ExpectedFields,
                check.CallsFound, GpuSelfCheckBook.ExpectedCalls, check.PortsMatched, GpuSelfCheckBook.ExpectedPorts));
            if (!IsAvailable(GpuCapability.Production)) ModUI.Text(page, ModL10n.L("gpu.hub.capability.production"));
            if (!IsAvailable(GpuCapability.ProductionUi)) ModUI.Text(page, ModL10n.L("gpu.hub.capability.productionUi"));
            if (!IsAvailable(GpuCapability.SharedLearning)) ModUI.Text(page, ModL10n.L("gpu.hub.capability.sharedLearning"));
        }

        private void OpenWindowFromHub()
        {
            Guard("hub/open", () =>
            {
                ModHub.Close();   // the hub is a modal window that holds the time; the GPU window does not
                GpuWindow.Open();
            });
        }

        private static void LogSaveWarning(string message)
        {
            ModLog.Warn(FeatureId, "save: " + message);
        }

        private static void LogProductionWarning(string message)
        {
            ModLog.Warn(FeatureId, message);
        }

        /// <summary>The line of a game month from <see cref="Perf"/>: the verbose log; a warning when an average is over its budget.</summary>
        private static void LogPerf(string line, bool overBudget)
        {
            if (overBudget) ModLog.Warn(FeatureId, line);
            else ModLog.Debug(FeatureId, line);
        }
    }
}
