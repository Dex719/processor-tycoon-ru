using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// The entry point of the feature <c>contract-transparency</c> (design.md, Component 1; FR-13, NFR-2): explains the chance to sign a contract and
    /// the requirements of a CPU, shows the real probability of a tender, asks for a confirmation before a risky offer and keeps a log of the contract
    /// automation. The feature mostly reads the game; its only intervention is the delay of a manual offer until the player confirms (D1).
    /// <list type="bullet">
    /// <item><b>Hot toggle.</b> <see cref="SupportsHotToggle"/> is true: the core removes the patches and skips the handlers when the player switches the
    /// feature off, and puts them back when it is switched on. <see cref="OnDisabled"/> closes what the feature has open.</item>
    /// <item><b>Fault isolation.</b> Every body of a patch or a handler runs through <see cref="Guarded(string, Action)"/> or
    /// <see cref="Guarded{T}"/>, which are <c>ModHooks.Guard</c> of this feature: an exception is counted by the core (five of them make the feature
    /// <c>Faulted</c> and the game goes on as without the mod), the feature keeps no counter of its own. The debug flag <c>debug.brokenPatch</c> makes every
    /// guarded body throw, which is how the circuit breaker is tried (a debug build only).</item>
    /// <item><b>The rest of the feature</b> hangs on this class: the save participant <see cref="ContractLog"/> and the patch classes (<c>OfferGate</c>,
    /// <c>TenderRecorder</c>, <c>AutomationLogger</c>, <c>ContractWindowAugmenter</c>, <c>BusinessSignGate</c>), the daily letter <c>TenderMailer</c> and its mail action, the way to the
    /// log window (<see cref="LogWindowLauncher"/>) and the settings page of the hub are registered in <see cref="Register"/>; the windows (<c>OddsPanel</c>, <c>OfferReviewDialog</c>,
    /// <c>ContractLogWindow</c>) are made on first use. The predicates <c>ct.offersSent</c> and <c>ct.tendersWon</c> are registered in <see cref="Register"/> (<c>ContractConditions</c>), the events
    /// for the other features are <see cref="ContractTransparencyEvents"/> (FR-14).</item>
    /// </list>
    /// </summary>
    [ModFeature]
    public sealed class ContractTransparencyFeature : IModFeature
    {
        /// <summary>The id of the feature: the slug of its spec, the key of its sections in the save and the settings, and the namespace <c>PTMod.Features.ContractTransparency</c>.</summary>
        public const string FeatureId = ContractParamSpecs.FeatureId;

        private ContractRules _rules = ContractRules.Default;
        private ContractInspector _inspector;
        private ContractLog _log;
        private TenderMailer _mailer;
        private SelfCheckResult _selfCheck;

        public string Id
        {
            get { return FeatureId; }
        }

        public string NameKey
        {
            get { return "ct.hub.name"; }
        }

        public string DescriptionKey
        {
            get { return "ct.hub.description"; }
        }

        /// <summary>No hard dependencies; the links with <c>platform-generations</c> and <c>press-reviews</c> are soft (they only show up as rows of the breakdown).</summary>
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

        /// <summary>The instance the core created; set by <see cref="Register"/>.</summary>
        public static ContractTransparencyFeature Instance { get; private set; }

        /// <summary>The feature is Enabled (not switched off, not Faulted, not Unavailable).</summary>
        public bool IsActive
        {
            get { return ModFeatures.IsActive(FeatureId); }
        }

        /// <summary>The numbers of <c>rules.json</c> with the player's override on top; the defaults until <see cref="Register"/> has run.</summary>
        public ContractRules Rules
        {
            get { return _rules; }
        }

        /// <summary>The adapter to the game's contracts (its handles to the private members are resolved on first use).</summary>
        public ContractInspector Inspector
        {
            get { return _inspector ?? (_inspector = new ContractInspector()); }
        }

        /// <summary>The contract log (the section <c>contract-transparency</c> of the side save file); null until <see cref="Register"/> has run.</summary>
        public ContractLog Log
        {
            get { return _log; }
        }

        /// <summary>
        /// What the check at the start found out about the game's members (TSK-020): the findings and the parts of the feature that depend on a missing one. Null until the feature has been
        /// enabled once (or when the check itself failed). The parts switch themselves off where they meet the member; this is the account of it for the log and the owner.
        /// </summary>
        public SelfCheckResult SelfCheck
        {
            get { return _selfCheck; }
        }

        public void Register(FeatureContext ctx)
        {
            Instance = this;
            _rules = LoadRules();
            ContractTransparencySettings.Declare(ctx);

            // The section of the side save file: the core reads it on a load and writes it after a save (FR-8 item 5, NFR-3).
            _log = new ContractLog(_rules);
            ctx.SaveParticipant(_log);

            // The patches (design.md, "Патчи Harmony"). The core puts them into the game when the feature starts and takes them out when it is switched off.
            ctx.Patch(typeof(OfferGate));
            ctx.Patch(typeof(TenderRecorder));
            ctx.Patch(typeof(AutomationLogger));
            ctx.Patch(typeof(ContractWindowAugmenter));   // the buttons and the actual probability in the contract window (TSK-011)
            ctx.Patch(typeof(BusinessSignGate));          // the question before a deal between companies is signed (TSK-016)
            ContractWindowAugmenter.Install();

            // One letter a day with the results of the tenders and the action "Open the log" (FR-5 items 4-6). No option: nothing in the fast-forward or under the loading screen.
            _mailer = new TenderMailer(this);
            ctx.OnTick(TickPhase.DayPassed, _mailer.OnDayPassed);
            ModMail.RegisterActionHandler(FeatureId, _mailer.OnMailAction);

            // The log window: the button of the contract window, the letter and the settings page open it through the launcher (FR-8 item 1).
            LogWindowLauncher.SetOpener(ContractLogWindow.Open);
            ctx.HubPage("ct.hub.page.title", BuildHubPage);

            // The predicates ct.offersSent and ct.tendersWon for the conditions of the other features (FR-14 item 3). The events (ContractTransparencyEvents) are raised by the patches.
            ContractConditions.Register();
        }

        public void OnEnabled()
        {
            // The check of the game's members (FR-12 item 3, TSK-020): creating the adapter looks the private members of Contract up and logs every one that is missing, once,
            // at the start instead of at the first click; then the whole table of the design is checked: the six patch targets (none is reserved by the core, GetBestOffers
            // is only called) and the fields of the two windows. The result names the parts of the feature that depend on a missing member.
            ModHooks.Guard(FeatureId, "ContractTransparencyFeature.OnEnabled", () =>
            {
                ContractInspector inspector = Inspector;
                if (inspector.IsAvailable) ModLog.Debug(FeatureId, "inspector: the private members of Contract were found");

                _selfCheck = ContractSelfCheck.Run();

                // Switched on in the middle of a scene: the buttons of the contract window appear and its probability gets the actual one at once (FR-13 item 4).
                ContractWindowAugmenter.SetVisible(true);
            });
        }

        public void OnDisabled()
        {
            // The results that came in today and have no letter yet are stale once the feature is off: a letter for them would arrive on some other day. The patches go with
            // the core. The review screen and the log window (later tasks) close here too (FR-13 item 4).
            if (_log != null) _log.Model.DrainTodayTenders();

            // What the feature has on the screen goes: the buttons are hidden and the probability is the game's again; the windows are closed (the review screen counts as cancelled, nothing is sent).
            ModHooks.Guard(FeatureId, "ContractTransparencyFeature.OnDisabled", () =>
            {
                ContractWindowAugmenter.SetVisible(false);
                OddsPanel.Close();
                ContractLogWindow.Close();
                OfferReviewDialog.Close();
            });
        }

        // ---- the settings page of the hub ----

        /// <summary>
        /// The part of the page of the feature in the hub that the parameters cannot express (FR-13 item 3, FR-8 item 1): the meaning of the number of the parameter "confirmation" in words, and the
        /// buttons that open the log and the preview of the automation. The hub draws the four parameters above it by itself.
        /// </summary>
        private static void BuildHubPage(UnityEngine.RectTransform page)
        {
            string mode;
            switch (ContractTransparencySettings.ConfirmMode)
            {
                case ConfirmMode.Always: mode = "ct.hub.mode.always"; break;
                case ConfirmMode.Never: mode = "ct.hub.mode.never"; break;
                default: mode = "ct.hub.mode.risky"; break;
            }
            ModUI.Text(page, ModL10n.L("ct.hub.mode.now", ModL10n.L(mode)));

            ModUI.Button(page, ModL10n.L("ct.hub.openLog"), () =>
            {
                ModHub.Close();   // the hub is a modal window: the log is opened over a closed hub
                ContractLogWindow.Open(LogFilter.All);
            });
            ModUI.Button(page, ModL10n.L("ct.hub.openPreview"), () =>
            {
                ModHub.Close();
                ContractLogWindow.OpenPreview();
            });

            // The description for the players (FR-13, TSK-023): the modes of the confirmation, the actual probability with the example 60/40 -> 21 %, the log.
            foreach (string[] section in HelpSections)
            {
                ModUI.Text(page, ModL10n.L(section[0]), true);
                for (int i = 1; i < section.Length; i++) ModUI.Text(page, ModL10n.L(section[i]));
            }
        }

        /// <summary>The description on the settings page: a row is the key of a heading and then the keys of its paragraphs (<c>ct.hub.help.*</c>).</summary>
        internal static readonly string[][] HelpSections =
        {
            new[] { "ct.hub.help.modes.title", "ct.hub.help.modes" },
            new[] { "ct.hub.help.actual.title", "ct.hub.help.actual", "ct.hub.help.example" },
            new[] { "ct.hub.help.log.title", "ct.hub.help.log" },
        };

        // ---- guards for the bodies of patches and handlers ----

        /// <summary>
        /// Runs <paramref name="body"/> through <c>ModHooks.Guard</c> for this feature: false when it threw (the exception is counted by the core and
        /// logged once per site). <paramref name="site"/> names the hook (<c>OfferGate/AddOffer.prefix</c>).
        /// </summary>
        internal static bool Guarded(string site, Action body)
        {
            return ModHooks.Guard(FeatureId, site, () =>
            {
                TripDebugFlag(site);
                body();
            });
        }

        /// <summary>Like <see cref="Guarded(string, Action)"/> with a result; <paramref name="fallback"/> when the body threw (a prefix returns true, which lets the game run).</summary>
        internal static T Guarded<T>(string site, Func<T> body, T fallback)
        {
            return ModHooks.Guard(FeatureId, site, () =>
            {
                TripDebugFlag(site);
                return body();
            }, fallback);
        }

        /// <summary>The test of the circuit breaker (TSK-001): with <c>debug.brokenPatch</c> in the settings file every guarded body throws, five failures fault the feature.</summary>
        private static void TripDebugFlag(string site)
        {
            if (ModSettings.Debug("debug.brokenPatch")) throw new InvalidOperationException("debug.brokenPatch: " + site);
        }

        // ---- content ----

        /// <summary>
        /// Loads <c>Content/contract-transparency/rules.json</c> with the override from <c>&lt;game&gt;/PTMod/content/contract-transparency/rules.json</c>
        /// laid over it by <c>ModContent.LoadConfig</c>. A value outside its range is replaced by the default and each such replacement is a line in the log.
        /// </summary>
        private static ContractRules LoadRules()
        {
            try
            {
                JsonObject config = ModContent.LoadConfig(FeatureId, ContractRules.FileName, ContractRules.SupportedSchema);
                var problems = new List<string>();
                ContractRules rules = ContractRules.FromJson(config, problems);
                foreach (string problem in problems) ModLog.Warn(FeatureId, "content: " + problem);
                return rules;
            }
            catch (Exception ex)
            {
                ModLog.Error(FeatureId, "content: rules.json could not be read; the defaults are used", ex);
                return ContractRules.Default;
            }
        }
    }
}
