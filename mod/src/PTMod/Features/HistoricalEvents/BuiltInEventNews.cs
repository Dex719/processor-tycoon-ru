using System;
using PTMod.Core;
using PTMod.Features.EventSystem;
using GameCompanyEventHandler = ProcessorTycoon.CompanySystem.CompanyEventHandler;
using GameCompanyHelper = ProcessorTycoon.CompanySystem.CompanyHelper;
using GameDataFinder = ProcessorTycoon.DataFinder;
using GameLicenseHelper = ProcessorTycoon.LicenseHelper;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The letters of reference about the game's own events (FR-16, Could; design.md, Component 10): the spin-off of the foundries of the company that plays AMD, and the end of
    /// the x86 licences. The game does both on its own; the feature only tells the player how it was, with an event of the pack that has no effects
    /// (<c>hist.news.solarfoundries</c>, <c>hist.news.x86license</c>), the same letter as any other event of the calendar (the reference, the source).
    /// <list type="bullet">
    /// <item><b>The spin-off.</b> The one patch of the feature: an optional postfix of <c>CompanyEventHandler.TriggerSolarFoundriesSpinOff</c>. The method runs every time the game looks
    /// for the spin-off (on 2009-03-02 and when the company is about to go under), and returns early when there is nothing to do; the postfix tells the two apart by the foundry
    /// that was not there before and is there now (<see cref="BuiltInNewsCore"/>). It reads the game and sends a letter; it changes no result, argument or state of the game.</item>
    /// <item><b>The licence.</b> A subscription to <c>LicenseHelper.OnX86LicenseRevoked</c>, made when the game is ready (<see cref="Attach"/>). The game raises the event again when it
    /// loads a save in which the licence was revoked (<c>SaveHandler.OnLastLoadedTick</c>), so nothing is sent while a save loads or the new game fast-forwards.</item>
    /// <item><b>When nothing is sent:</b> the feature is off, <c>historyNotes</c> is off (these letters are nothing but reference), the game is loading or fast-forwarding,
    /// or the letter was sent already in this game.</item>
    /// </list>
    /// Every entry point is under <c>ModHooks.Guard</c>: an exception is counted against the feature and never reaches the game.
    /// </summary>
    internal static class BuiltInEventNews
    {
        private const string FeatureId = HistIds.Feature;
        private const string SpinOffSite = "FR-16/TriggerSolarFoundriesSpinOff.postfix";
        private const string LicenseSite = "FR-16/OnX86LicenseRevoked";

        /// <summary><c>AICompany.UniqueID</c> of the company that plays AMD (<c>CompanyHelper.IsEMD</c>).</summary>
        private const int ParentUniqueId = 1;

        private static readonly BuiltInNewsCore Core = new BuiltInNewsCore();
        private static readonly EventBusFirer Firer = new EventBusFirer();

        private static GameLicenseHelper _subscribed;

        // ---- the patch ----

        [ModPatch(typeof(GameCompanyEventHandler), "TriggerSolarFoundriesSpinOff", PatchKind.Postfix, Optional = true)]
        private static void SpinOffPostfix()
        {
            if (!ModFeatures.IsActive(FeatureId)) return;
            ModHooks.Guard(FeatureId, SpinOffSite, OnSpinOffRan);
        }

        // ---- the subscription ----

        /// <summary>
        /// The game scene is ready (or the feature was switched on inside it): remembers whether the foundry is already there, and subscribes to the revocation of the licence
        /// of the scene's <c>LicenseHelper</c> (once per object: the subscription is taken off the earlier one and put on again). Under the guard of the caller.
        /// </summary>
        internal static void Attach()
        {
            Core.Reset();
            Core.Baseline(FoundryName() != null);

            GameLicenseHelper helper = GameLicenseHelper.Instance;
            if (helper == null) return;

            if (_subscribed != null && !ReferenceEquals(_subscribed, helper)) _subscribed.OnX86LicenseRevoked -= OnLicenseRevoked;
            helper.OnX86LicenseRevoked -= OnLicenseRevoked;
            helper.OnX86LicenseRevoked += OnLicenseRevoked;
            _subscribed = helper;
        }

        private static void OnLicenseRevoked()
        {
            if (!ModFeatures.IsActive(FeatureId)) return;
            ModHooks.Guard(FeatureId, LicenseSite, () =>
            {
                if (!Core.ShouldAnnounceLicense(Busy(), HistSettings.HistoryNotes)) return;

                Send(BuiltInNews.X86LicenseEventId, BuiltInNews.X86LicenseEventId, string.Empty, string.Empty);
            });
        }

        // ---- the bodies ----

        private static void OnSpinOffRan()
        {
            string foundry = FoundryName();
            if (!Core.ShouldAnnounceSpinOff(foundry != null, Busy(), HistSettings.HistoryNotes)) return;

            Send(BuiltInNews.SolarFoundriesEventId, BuiltInNews.SolarFoundriesEventId, ParentName(), foundry);
        }

        /// <summary>A save is loading or the new game fast-forwards: what the game raises then is a replay, not news.</summary>
        private static bool Busy()
        {
            return ModTicks.IsLoading || ModTicks.IsFastForwarding || !ModTicks.IsGameReady;
        }

        private static void Send(string eventId, params string[] args)
        {
            FireResult result = Firer.Fire(eventId, ModCompanies.PlayerKey, args);
            if (result == FireResult.UnknownEvent || result == FireResult.Disabled)
            {
                ModLog.Warn(FeatureId, "news: the engine did not take " + eventId + " (" + result + ")");
            }
        }

        // ---- the world ----

        /// <summary>The name of the company SolarFoundries if the game has it, else null.</summary>
        private static string FoundryName()
        {
            foreach (ICompany company in GameDataFinder.FindAllAICompanies(false))
            {
                if (company != null && GameCompanyHelper.IsSolarFoundries(company)) return company.Name ?? string.Empty;
            }
            return null;
        }

        /// <summary>The name of the company that plays AMD; empty when the game has none.</summary>
        private static string ParentName()
        {
            ICompany parent = ModCompanies.Resolve(CompanyKeyOf(ParentUniqueId));
            return parent == null ? string.Empty : parent.Name ?? string.Empty;
        }

        private static string CompanyKeyOf(int uniqueId)
        {
            return "ai:" + uniqueId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
