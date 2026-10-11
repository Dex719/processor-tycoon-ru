using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Json;
using AICompany = ProcessorTycoon.CompanySystem.AICompany;
using GameBankruptcy = ProcessorTycoon.Bank.BankruptcyHandler;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The entry point of <c>rival-acquisitions</c> (I06): the player buys the assets of bankrupt rivals, takes over companies in default or healthy
    /// ones, holds minority stakes and gives rescue orders (design.md, "RivalAcquisitionsFeature"). This class is the skeleton the rest hangs on:
    /// the content (<see cref="Balance"/>), the books of the feature (<see cref="State"/>, kept in the side save by
    /// <see cref="AcquisitionsSaveParticipant"/>), the implementation behind <c>RivalAcquisitionsApi</c> and the subscription to the bankruptcy
    /// event of the game. The core owns the switch, the fault counter and the circuit breaker: the feature has no flags or counters of its own.
    /// <list type="bullet">
    /// <item><b>No hard dependencies.</b> <c>CapitalLedger</c> of <c>bankruptcy-recovery</c> works without that feature being on, so
    /// <see cref="DependsOn"/> is empty; <c>cpu-architectures</c> and <c>gpu-product-line</c> are soft (checked with <c>ModFeatures.IsActive</c>).</item>
    /// <item><b>Hot toggle.</b> Switching off drops the scene subscription and the API implementation and rolls nothing back: the assets are in the
    /// game's own save, the stakes, auctions and rescue orders stay in the section and work again after switching on.</item>
    /// <item><b>The bankruptcy event</b> (<c>BankruptcyHandler.OnCompanyWentBankrupt</c>) is subscribed when the game is ready
    /// (<c>ModTicks.GameReady</c>, or <c>OnEnabled</c> for a feature switched on in the middle of a scene) and dropped when the scene is left
    /// (<c>PTModHost.SceneLeaving</c>) because the handler is a singleton of the scene.</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    [ModFeature]
    public sealed class RivalAcquisitionsFeature : IModFeature
    {
        /// <summary>The id of the feature (the slug of its spec; also the section of the side save and the folder of its content).</summary>
        public const string FeatureId = "rival-acquisitions";

        private const string SiteEnabled = "RivalAcquisitionsFeature.OnEnabled";
        private const string SiteGameReady = "RivalAcquisitionsFeature.OnGameReady";
        private const string SiteSceneLeaving = "RivalAcquisitionsFeature.OnSceneLeaving";
        private const string SiteBankrupt = "BankruptcyHandler.OnCompanyWentBankrupt";
        private const string SiteMonth = "RivalAcquisitionsFeature.OnMonth";
        private const string SiteSaveRead = "RivalAcquisitionsFeature.OnSaveRead";

        private readonly BankruptcyLink _bankruptcy;
        private RivalAcquisitionsService _service;
        private bool _linkMissingWarned;

        /// <summary>Creates the feature object; the core calls this once. Nothing is started here (see <see cref="Register"/>).</summary>
        public RivalAcquisitionsFeature()
        {
            _bankruptcy = new BankruptcyLink(OnCompanyWentBankrupt);
        }

        public string Id
        {
            get { return FeatureId; }
        }

        public string NameKey
        {
            get { return "acq.feature.name"; }
        }

        public string DescriptionKey
        {
            get { return "acq.feature.description"; }
        }

        /// <summary>No hard dependencies: the money goes through the ledger of <c>bankruptcy-recovery</c>, which works without that feature on.</summary>
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

        // ---- what the rest of the feature reaches ----

        /// <summary>The feature object, set when the core registers it; null before that. Patches (static classes) and services reach the feature through it.</summary>
        internal static RivalAcquisitionsFeature Instance { get; private set; }

        /// <summary>The numbers of the feature (<c>Content/rival-acquisitions/balance.json</c>): the defaults of the code until the file is read, never null.</summary>
        internal AcquisitionsBalance Balance { get; private set; } = AcquisitionsBalance.Defaults();

        /// <summary>The books of the feature: deals, stakes, auctions, rescue orders. Never null; the save participant replaces it when a save is read or a game resets.</summary>
        internal AcquisitionsState State { get; set; } = new AcquisitionsState();

        /// <summary>
        /// The AI company the executor is taking out of the game right now, or null. The feature's own hooks ignore the bankruptcy event of this
        /// company (it is the deal, not a bankruptcy of its own).
        /// </summary>
        internal AICompany ExecutingTarget { get; set; }

        /// <summary>The rivals' defaults and bankruptcies: the mails, the auctions, the backfill (TSK-013).</summary>
        internal RivalDistressMonitor Monitor { get; private set; }

        /// <summary>The player's stakes, dividends and rescue orders (TSK-014).</summary>
        internal StakeService Stakes { get; private set; }

        // ---- IModFeature ----

        public void Register(FeatureContext ctx)
        {
            Instance = this;
            LoadBalance();

            _service = new RivalAcquisitionsService(this);
            Monitor = new RivalDistressMonitor(this);
            Stakes = new StakeService(this);
            ctx.SaveParticipant(new AcquisitionsSaveParticipant(this));
            ctx.Patch(typeof(AcquisitionPatches));                    // P1-P3 and P4 (the mark in the table of the game)
            ctx.OnTick(TickPhase.LateMonthPassed, OnMonth);           // dividends, expired auctions, ended episodes of default
            ModMail.RegisterActionHandler(FeatureId, AcquisitionMail.OnAction);

            ctx.HubPage("acq.hub.title", AcquisitionsHubPage.Build);  // the section "Deals" of the hub: opens the window (TSK-015)

            ModTicks.GameReady += OnGameReady;
            ModSave.AfterRead += OnSaveRead;
            PTModHost.SceneLeaving += OnSceneLeaving;

#if PTMOD_DEBUG
            ApiEventProbe.Attach();   // the test subscriber of the events of the API (debug flags of ptmod_settings.json only)
#endif
            AcquisitionPatches.CheckTargets();
            ModLog.Info(FeatureId, GameRelatedFeatures.Describe());   // the soft APIs of step 9 (FR-16): a missing one is skipped by the deal
        }

        public void OnEnabled()
        {
            RivalAcquisitionsApi.Register(_service);

            // A feature switched on in the middle of a scene gets no GameReady: subscribe now when the game is already up, and run the backfill
            // of the auctions if this save never had it (a new game is marked done: the bankruptcies of its fast-forward get no auctions).
            if (ModTicks.IsGameReady)
            {
                ModHooks.Guard(FeatureId, SiteEnabled, () =>
                {
                    CloseOffersForNewerSave();
                    AttachGameEvents();
                    CheckAbsorbedCompaniesAreGone();
                    if (!State.backfillDone)
                    {
                        if (ModTicks.ReadyReason == GameReadyReason.NewGame) State.backfillDone = true;
                        else Monitor.BackfillAuctions();
                    }
                });
            }
        }

        public void OnDisabled()
        {
            // A switched-off feature gets no SceneLeaving, so the scene's singleton is released here. Nothing else is rolled back.
            DetachGameEvents();
            RivalAcquisitionsApi.Unregister(_service);
            ExecutingTarget = null;
        }

        // ---- content ----

        private void LoadBalance()
        {
            try
            {
                JsonObject config = ModContent.LoadConfig(FeatureId, AcquisitionsBalance.FileName, AcquisitionsBalance.SchemaVersion);
                var problems = new List<string>();
                Balance = AcquisitionsBalance.FromJson(config, problems);
                foreach (string line in problems) ModLog.Warn(FeatureId, "content: balance.json: " + line);
            }
            catch (Exception e)
            {
                Balance = AcquisitionsBalance.Defaults();
                ModLog.Error(FeatureId, "content: balance.json could not be read, the defaults of the code are used", e);
            }
        }

        // ---- the game is ready ----

        private void OnGameReady(GameReadyReason reason)
        {
            if (!ModFeatures.IsActive(FeatureId)) return;
            ModHooks.Guard(FeatureId, SiteGameReady, () => HandleGameReady(reason));
        }

        private void HandleGameReady(GameReadyReason reason)
        {
            AttachGameEvents();

            if (reason == GameReadyReason.NewGame)
            {
                // The bankruptcies of the new-game fast-forward get no auctions (FR-11 item 3), and the AI has no state of the feature to create.
                State.backfillDone = true;
                return;
            }

            CheckAbsorbedCompaniesAreGone();

            // The auctions of companies that were bankrupt before the feature (FR-3 item 5): once per save.
            if (!State.backfillDone) Monitor.BackfillAuctions();
        }

        // ---- a save of a newer mod ----

        /// <summary><c>ModSave.AfterRead</c>: every participant has its data. The core's raise skips this handler while the feature is not active.</summary>
        private void OnSaveRead()
        {
            ModHooks.Guard(FeatureId, SiteSaveRead, CloseOffersForNewerSave);
        }

        /// <summary>
        /// NFR-3: when the side file (or this feature's section) was written by a newer mod, the core keeps it unchanged and hands the feature an empty state, and
        /// the feature offers no deal for that save: what a deal records would not be saved. The core only says the status is "newer" (any section may be the
        /// cause), so the file is read once and the version of OUR section is checked (<see cref="AcquisitionsSection.IsNewerThanSupported"/>).
        /// </summary>
        private void CloseOffersForNewerSave()
        {
            if (ModSave.Status != SideFileStatus.Newer) return;

            string text = null;
            try
            {
                string path = ModSave.SideFilePath;
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) text = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                ModLog.Warn(FeatureId, "save: the side file could not be read to check its version (" + e.GetType().Name + ")");
            }

            if (!AcquisitionsSection.IsNewerThanSupported(text)) return;
            State.CloseOffers();
            ModLog.Warn(FeatureId, "save: the side file was written by a newer mod; the feature has only defaults for it and offers no deal in this save");
        }

        // ---- the month ----

        /// <summary><c>LateMonthPassed</c> (the core skips it while loading and fast-forwarding, and while the feature is not active).</summary>
        private void OnMonth()
        {
            if (!ModFeatures.IsActive(FeatureId)) return;
            ModHooks.Guard(FeatureId, SiteMonth + "/auctions", () => Monitor.OnMonth());
            ModHooks.Guard(FeatureId, SiteMonth + "/dividends", () => Stakes.OnMonth());
        }

        /// <summary>A company the books call absorbed must be bankrupt in the game (the executor takes it out that way); anything else is logged.</summary>
        private void CheckAbsorbedCompaniesAreGone()
        {
            List<DealRecord> deals = State.deals;
            for (int i = 0; i < deals.Count; i++)
            {
                DealRecord deal = deals[i];
                if (deal == null || !AcquisitionsState.IsAbsorbingType(deal.type)) continue;

                ICompany company = ModCompanies.Resolve(CompanyKey.ForAi(deal.targetUniqueId));
                if (company != null && !company.IsBankrupt)
                {
                    ModLog.Warn(FeatureId, "save: deal #" + deal.id + " says '" + deal.targetName + "' (ai:" + deal.targetUniqueId + ") was absorbed, but the game has it alive");
                }
            }
        }

        // ---- the scene ----

        private void OnSceneLeaving()
        {
            try
            {
                DetachGameEvents();
                Monitor.Clear();
                ExecutingTarget = null;
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(FeatureId, SiteSceneLeaving, e);
            }
        }

        private void AttachGameEvents()
        {
            if (_bankruptcy.Attach())
            {
                _linkMissingWarned = false;
                return;
            }

            if (_linkMissingWarned) return;
            _linkMissingWarned = true;
            ModLog.Warn(FeatureId, "events: BankruptcyHandler is not in the scene; the bankruptcies of rivals are not noticed until the next scene");
        }

        private void DetachGameEvents()
        {
            _bankruptcy.Detach();
        }

        /// <summary><c>BankruptcyHandler.OnCompanyWentBankrupt</c>: an AI company went bankrupt, or the executor took one out (<see cref="ExecutingTarget"/>).</summary>
        private void OnCompanyWentBankrupt(ICompany company)
        {
            if (!ModFeatures.IsActive(FeatureId)) return;

            ModHooks.Guard(FeatureId, SiteBankrupt, () =>
            {
                if (company == null || company.IsPlayer) return;
                if (ReferenceEquals(company, ExecutingTarget)) return;   // the deal's own removal of its target

                ModLog.Debug(FeatureId, "bankruptcy: " + (ModCompanies.KeyOf(company) ?? "a company") + " went bankrupt");
                Monitor.OnCompanyWentBankrupt(company);   // the auction, the mail, RivalBankrupt, the write-off of a stake
            });
        }

        /// <summary>
        /// The subscription to <c>BankruptcyHandler.OnCompanyWentBankrupt</c>. The game type is touched only inside these two methods
        /// (no inlining), so that the feature class itself never needs the game's assembly to load.
        /// </summary>
        private sealed class BankruptcyLink
        {
            private readonly Action<ICompany> _handler;
            private GameBankruptcy _source;

            public BankruptcyLink(Action<ICompany> handler)
            {
                _handler = handler;
            }

            /// <summary>Subscribes the handler to the scene's <c>BankruptcyHandler</c>, once. False when the scene has none.</summary>
            [MethodImpl(MethodImplOptions.NoInlining)]
            public bool Attach()
            {
                GameBankruptcy source = GameBankruptcy.Instance;
                if (source == null) return false;

                Detach();
                source.OnCompanyWentBankrupt += _handler;
                _source = source;
                return true;
            }

            /// <summary>Unsubscribes and forgets the handler of the scene. Safe to call twice; never throws.</summary>
            [MethodImpl(MethodImplOptions.NoInlining)]
            public void Detach()
            {
                GameBankruptcy source = _source;
                _source = null;
                if (ReferenceEquals(source, null)) return;

                try
                {
                    source.OnCompanyWentBankrupt -= _handler;
                }
                catch (Exception e)
                {
                    ModLog.Debug(FeatureId, "events: could not unsubscribe from BankruptcyHandler (" + e.GetType().Name + ")");
                }
            }
        }
    }
}
