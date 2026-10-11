using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using UnityEngine;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// The entry point of platform-generations (design.md, Component 1): generations of platforms (sockets and memory standards), the installed
    /// base of each company, early adopters, the penalty for changing a socket, and the OEM contracts' taste for new platforms. The effects reach
    /// the game only through the channels of <c>ModModifiers</c> (market.rating.cpu, contract.signChance); there is no patch of the game's formulas here.
    /// <para>
    /// This file holds the state (the generations, the balance, the installed base, the chronology, the OEM preferences), the registration (the
    /// three switches, the section of the side save file, the providers of the channels, the handlers of the core's events, the ticks) and the
    /// loading of the content. The work is in the classes it creates: <see cref="PlatformModifierProvider"/> (the channels),
    /// <see cref="PlatformReleaseHandlers"/> (releases and transfers of CPUs), <see cref="PlatformSubscriptions"/> (the clock of the game and the
    /// resynchronization), <see cref="PlatformNotifier"/> (letters and notifications), <see cref="ReleaseWarning"/> (the one patch), the window
    /// <see cref="PlatformBrowserWindow"/>, the predicates <see cref="PlatformConditions"/> and the events <see cref="PlatformEvents"/>.
    /// </para>
    /// </summary>
    [ModFeature]
    public sealed class PlatformGenerationsFeature : IModFeature, IModSaveParticipant
    {
        /// <summary>The slug of the feature: the key of its content, strings, settings and save section.</summary>
        public const string FeatureId = "platform-generations";

        /// <summary>The running feature; set in <see cref="Register"/>. Check <c>ModFeatures.IsActive(FeatureId)</c> before using what it holds.</summary>
        public static PlatformGenerationsFeature Instance { get; private set; }

        /// <summary>How many times the generations are tried per game scene when the answer is "none" (the scene's objects were not there yet).</summary>
        private const int MaxCatalogAttempts = 3;

        private readonly PlatformSaveSection _section;
        private readonly PlatformReleaseHandlers _releaseHandlers;
        private readonly PlatformSubscriptions _subscriptions;
        private IPlatformNotifier _notifier;
        private bool _balanceLoaded;
        private bool _selfChecked;
        private int _catalogAttempts;

        public PlatformGenerationsFeature()
        {
            _section = new PlatformSaveSection(FeatureId, line => ModLog.Warn(FeatureId, "save: " + line));
            Provider = new PlatformModifierProvider(this);
            ContractPrefs = new ContractPreferences(this);
            _releaseHandlers = new PlatformReleaseHandlers(this);
            _subscriptions = new PlatformSubscriptions(this);
            _notifier = new PlatformNotifier(this);
        }

        public string Id
        {
            get { return FeatureId; }
        }

        public string NameKey
        {
            get { return "pg.hub.title"; }
        }

        public string DescriptionKey
        {
            get { return "pg.hub.description"; }
        }

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

        // ---- the state of the feature ----

        /// <summary>
        /// The generations. Empty until the game scene is ready (<see cref="CatalogReady"/>): the sockets and memories are checked against the
        /// game's objects, which exist only in the game scene. Never null.
        /// </summary>
        public GenerationCatalog Catalog { get; private set; } = GenerationCatalog.Empty;

        /// <summary>The generations have been loaded and checked (the game scene was ready).</summary>
        public bool CatalogReady { get; private set; }

        /// <summary>The numbers of the feature; the defaults of the code until <c>balance.json</c> is read in <see cref="OnEnabled"/>. Never null.</summary>
        public PlatformBalance Balance { get; private set; } = PlatformBalance.Defaults();

        /// <summary>The section of the side save file as it was read, or started empty (see <see cref="ResetToDefaults"/>). Never null.</summary>
        internal PlatformGenerationsSave SaveData
        {
            get { return _section.Data; }
        }

        /// <summary>
        /// The state was started from nothing (a new game, a save without the section, a section the core could not read) and the chronology has
        /// to be reconstructed from the CPUs of the market (FR-12 items 3-4, TSK-011/012). Whoever reconstructs it lowers the flag.
        /// </summary>
        internal bool NeedsReconstruction
        {
            get { return _section.NeedsReconstruction; }
            set { _section.NeedsReconstruction = value; }
        }

        /// <summary>The installed base of CPUs by company and socket (FR-4); derived from the ledgers of the game, never saved.</summary>
        public InstalledBaseLedger InstalledBase { get; } = new InstalledBaseLedger();

        /// <summary>The chronology: announcements, launches, early adopters, the first CPU of each company on each socket (FR-2, FR-3, FR-6); saved.</summary>
        public PlatformTimeline Timeline { get; } = new PlatformTimeline();

        /// <summary>The generation each contract prefers (FR-8, the "per contract" mode); saved.</summary>
        public OemPreferences Preferences { get; } = new OemPreferences();

        /// <summary>
        /// The timers of <c>ApplyMonth</c>, <c>RebuildFull</c>, <c>Reconstruction.Run</c> and <c>RebuildCache</c> against their budgets, and the count of
        /// calls of the providers per game day (TSK-020, NFR-1). The figures go to the log in the verbose mode (<c>ModSettings.VerboseLog</c>).
        /// </summary>
        public PlatformPerf Perf { get; } = new PlatformPerf();

        /// <summary>The providers of <c>market.rating.cpu</c> and <c>contract.signChance</c> with their cache (TSK-009).</summary>
        internal PlatformModifierProvider Provider { get; }

        /// <summary>The preferences of contracts on the game side (TSK-009, TSK-011).</summary>
        internal ContractPreferences ContractPrefs { get; }

        /// <summary>What tells the player about announcements, launches, early adopters and penalties: <see cref="PlatformNotifier"/> (it shows nothing in the fast-forward, while loading or with the switch off); <see cref="NullNotifier"/> when set to null.</summary>
        internal IPlatformNotifier Notifier
        {
            get { return _notifier; }
            set { _notifier = value ?? NullNotifier.Instance; }
        }

        // ---- IModFeature ----

        /// <summary>
        /// Declares the three switches, the save participant, the providers of both channels, the handlers of the core's events, the ticks, the one
        /// patch (the warning in the release window), the predicates for scenarios and the page in the mods hub. Runs once per session; nothing here is
        /// declared again when the feature is switched off and on.
        /// </summary>
        public void Register(FeatureContext ctx)
        {
            Instance = this;
            PlatformSettings.Declare(ctx);
            ctx.SaveParticipant(this);
            ModTicks.GameReady += OnGameReady;

            // TSK-009: the only way the feature reaches the game's formulas (D1). Registered once; the core skips them while the feature is off.
            Provider.RefreshSettings();
            Provider.Register();

            // TSK-010: the releases and the transfers of CPUs come from the core (P20); the feature has no patch on Company.AddCpu / AICompany.AddCpu.
            ModEvents.CpuReleased += _releaseHandlers.OnCpuReleased;
            ModEvents.CpuTransferred += _releaseHandlers.OnCpuTransferred;

            // TSK-011: the year, the month, the end of the fast-forward, the contracts, the scene.
            _subscriptions.Register(ctx);

            // TSK-014: the warning in the release window (FR-7), a postfix that only reads; the target is Optional.
            ctx.Patch(typeof(ReleaseWarning));

            // TSK-020: the self-check of the members and hooks the feature stands on runs once when the feature starts (OnEnabled), after the
            // core has applied the patch; the timers and the counters of the providers are in Perf, the day is closed by the subscriptions.

            // TSK-018: the predicates pg.* for the scenarios (FR-11); the events of PlatformEvents are raised by the handlers.
            PlatformConditions.Register();

            // TSK-015: the window "Платформы" is opened from the page of the feature in the hub.
            ctx.HubPage("pg.hub.title", BuildHubPage);
        }

        /// <summary>
        /// The feature became active: reads the numbers and the generations (when the game scene is up), and inside a game catches up the period it
        /// missed - the reconstruction adds what is missing, the installed base and the caches are rebuilt (FR-13 item 3).
        /// </summary>
        public void OnEnabled()
        {
            ModHooks.Guard(FeatureId, "feature/enabled", () =>
            {
                CheckSelfOnce();
                EnsureContent();
                _subscriptions.OnEnabled();
            });
        }

        /// <summary>The feature stopped. Nothing is dropped: the state and the registrations stay, the core skips an inactive feature.</summary>
        public void OnDisabled()
        {
            PlatformBrowserWindow.Close();
        }

        // ---- IModSaveParticipant (the section "platform-generations"; the work is PlatformSaveSection's) ----

        public string SectionId
        {
            get { return _section.SectionId; }
        }

        /// <summary>The version of the section; the core writes it as <c>_schema</c> and hands the stored one to <see cref="Read"/>.</summary>
        public int SchemaVersion
        {
            get { return _section.SchemaVersion; }
        }

        /// <summary>
        /// A new game (<c>ModSave.Status == NewGame</c>, before the fast-forward starts), a save without the section, a section newer than this
        /// build, leaving to the menu: the state is empty and has to be reconstructed (FR-12 items 1 and 4). The chronology, the preferences, the
        /// installed base and the cache of the multipliers are dropped.
        /// </summary>
        public void ResetToDefaults()
        {
            _section.ResetToDefaults();
            Timeline.Clear();
            Preferences.Clear();
            InstalledBase.Clear();
            Provider.Clear();
        }

        /// <summary>
        /// The stored section (without <c>_schema</c>): the chronology and the preferences are built from it (FR-12 item 5); a record that fails the
        /// checks is dropped with a line in the log, a generation the content does not know is kept. The installed base and the cache are built when the
        /// game is ready. A section with no generation at all counts as missing: the chronology is reconstructed then.
        /// </summary>
        public void Read(JsonObject section, int schema)
        {
            _section.Read(section, schema);
            InstalledBase.Clear();
            Provider.Clear();

            var problems = new List<string>();
            Timeline.ReadFrom(SaveData, GameAdapter.Today(), problems);
            Preferences.ReadFrom(SaveData, problems);
            foreach (string problem in problems) ModLog.Warn(FeatureId, "save: " + problem);

            if (SaveData.generations.Count == 0) NeedsReconstruction = true;
        }

        /// <summary>The section to store, without <c>_schema</c> (the core adds it): the chronology and the preferences are folded into it first.</summary>
        public JsonObject Write()
        {
            Timeline.WriteTo(SaveData);
            Preferences.WriteTo(SaveData);
            return _section.Write();
        }

        // ---- the hub ----

        /// <summary>
        /// The page of the feature in the mods hub (FR-9 item 1, FR-13): a line about what the window shows and the button that opens it, the
        /// result of the self-check, and the description of how the platforms work for the player (TSK-023) with the numbers of the balance in
        /// force, so what the page says is what the game does.
        /// </summary>
        private void BuildHubPage(RectTransform page)
        {
            ModUI.Text(page, ModL10n.L("pg.hub.page.intro"));
            ModUI.Button(page, ModL10n.L("pg.hub.open"), OpenWindowFromHub, ButtonStyle.CallToAction, ModL10n.L("pg.hub.open.tip"));
            ModHooks.Guard(FeatureId, "hub/selfCheck", () => AddSelfCheckLine(page));
            ModHooks.Guard(FeatureId, "hub/guide", () => AddGuide(page));
        }

        private void AddSelfCheckLine(RectTransform page)
        {
            PlatformSelfCheckBook book = SelfCheck;
            if (book == null)
            {
                ModUI.Text(page, ModL10n.L("pg.hub.selfcheck.notRun"));
                return;
            }

            string found = PlatformNumbers.Int(book.MembersFound + book.HooksOk);
            string expected = PlatformNumbers.Int(book.MembersExpected + book.HooksExpected);
            ModUI.Text(page, book.Ok
                ? ModL10n.L("pg.hub.selfcheck.ok", found, expected)
                : ModL10n.L("pg.hub.selfcheck.problems", found, expected, PlatformNumbers.Int(book.Problems.Count)));
        }

        private void AddGuide(RectTransform page)
        {
            ViewText text = PlatformViewText.Instance;
            ModUI.Text(page, ModL10n.L("pg.hub.guide.title"), true);
            foreach (GuideSection section in PlatformGuide.Build(Balance, Catalog, text))
            {
                ModUI.Text(page, section.Title, true);
                ModUI.Text(page, section.Body);
            }

            ModUI.Text(page, ModL10n.L("pg.hub.guide.gens.title"), true);
            if (Catalog.Count == 0)
            {
                ModUI.Text(page, ModL10n.L("pg.hub.guide.gens.none"));
                return;
            }
            foreach (GuideGeneration generation in PlatformGuide.Generations(Catalog, text)) ModUI.Text(page, generation.Line);
        }

        private void OpenWindowFromHub()
        {
            ModHooks.Guard(FeatureId, "hub/open", () =>
            {
                ModHub.Close();   // the hub is a modal window that holds the time; the window "Платформы" does not
                PlatformBrowserWindow.Open();
            });
        }

        // ---- the self-check and the timers (TSK-020) ----

        /// <summary>
        /// The result of the self-check, or null before it ran (once per session, when the feature starts for the first time): the members of the
        /// game and the hooks of the core the feature stands on. The hub page shows it.
        /// </summary>
        internal PlatformSelfCheckBook SelfCheck
        {
            get { return PlatformSelfCheck.Last; }
        }

        private void CheckSelfOnce()
        {
            if (_selfChecked) return;
            _selfChecked = true;
            PlatformSelfCheck.Run(this);
        }

        /// <summary>
        /// Says how long the last run of <paramref name="op"/> took: one warning per session when it went over its budget (NFR-1), otherwise a line
        /// in the verbose log. The time itself is booked by <see cref="Perf"/> before this is called.
        /// </summary>
        internal void ReportPerf(PerfOp op)
        {
            PlatformPerf perf = Perf;
            if (perf.TakeWarning(op)) ModLog.Warn(FeatureId, "perf: " + perf.DescribeLast(op) + " went over its budget; so far: " + perf.Summary());
            else if (ModSettings.VerboseLog) ModLog.Debug(FeatureId, "perf: " + perf.DescribeLast(op));
        }

        // ---- internals ----

        /// <summary>The game state is ready (a save was loaded, or a new game finished its fast-forward): the state is made (FR-12 items 3-5).</summary>
        private void OnGameReady(GameReadyReason reason)
        {
            ModHooks.Guard(FeatureId, "feature/gameReady", () =>
            {
                EnsureContent();
                _subscriptions.OnGameReady(reason);
            });
        }

        /// <summary>A new game scene: the generations that could not be loaded may be tried again.</summary>
        internal void OnSceneReady()
        {
            _catalogAttempts = 0;
        }

        /// <summary>
        /// Reads <c>balance.json</c> once, and <c>generations.json</c> once the objects of the game scene exist (the sockets and memories are
        /// checked against them). A load that finds no generation at all (the scene's objects are not there yet) is not kept and is tried again
        /// later, a few times per scene. Safe to call again.
        /// </summary>
        internal void EnsureContent()
        {
            if (!_balanceLoaded)
            {
                Balance = PlatformContent.LoadBalance();
                _balanceLoaded = true;
            }

            if (CatalogReady || _catalogAttempts >= MaxCatalogAttempts || !SceneObjectsExist()) return;

            _catalogAttempts++;
            GenerationCatalog catalog = PlatformContent.LoadCatalog();
            if (catalog.Count == 0)
            {
                ModLog.Warn(FeatureId, "content: no generation could be loaded (attempt " + _catalogAttempts + " of " + MaxCatalogAttempts + "); the effects stay off");
                return;
            }

            Catalog = catalog;
            CatalogReady = true;
        }

        /// <summary>
        /// The objects of the game scene (packages, memories) exist: the scene is ready, or the date controller is running already (the new game's
        /// fast-forward runs in the callback of the scene transition and may end before the scene is announced as ready).
        /// </summary>
        private static bool SceneObjectsExist()
        {
            if (PTModHost.CurrentScene != PTModHost.GameScene) return false;
            return PTModHost.IsSceneReady || ModTicks.Today != default(DateTime);
        }
    }
}
