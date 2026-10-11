using System;
using PTMod.Core;
using GameAIMath = ProcessorTycoon.AISystem.Math.AIMath;
using GameAIScores = ProcessorTycoon.AISystem.Math.AIScores;
using GameNegotiationWindow = ProcessorTycoon.CompanySystem.Business.ContractNegotiationWindow;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The attitude in the negotiations of business contracts (design.md, Component 10; FR-8): the AI side of a deal with the player gets
    /// <c>round(a / 5)</c> points of <c>Interest</c>, and a foundry partner takes <c>clamp(round(a / 10), 0, 10)</c> points off the markup (<c>Price</c>).
    /// <list type="bullet">
    /// <item><c>ContractNegotiationWindow.UpdateScores</c>: a prefix opens <see cref="NegotiationScope"/> for the companies of the window (the private
    /// <c>currentProvider</c> and <c>currentClient</c>), a finalizer closes it in every case and passes the exception on.</item>
    /// <item><c>AIMath.GetProviderScores</c> and <c>AIMath.GetClientScores</c>: postfixes that add the bonus to the result when the company is the AI side
    /// of the open scope. The game calls them from <c>AIBehaviourBusiness</c> too (AI-AI deals, monthly): with the scope closed they return at once, so
    /// those results are the vanilla ones (AC-8.2). A veto (<c>Interest = -1000</c>) is never changed, and the window sets the same veto for difficulty 5
    /// after the call.</item>
    /// </list>
    /// All four are declared with <c>ctx.Patch</c> (the self-check of the core covers them). A failure is counted by the core and the game's scores stay.
    /// </summary>
    internal static class NegotiationPatches
    {
        private const string Feature = RivalPersonalitiesFeature.FeatureId;
        private const string EnterSite = "NegotiationPatches/ContractNegotiationWindow.UpdateScores.prefix";
        private const string ExitSite = "NegotiationPatches/ContractNegotiationWindow.UpdateScores.finalizer";
        private const string ProviderSite = "NegotiationPatches/AIMath.GetProviderScores.postfix";
        private const string ClientSite = "NegotiationPatches/AIMath.GetClientScores.postfix";

        [ModPatch(typeof(GameNegotiationWindow), "UpdateScores", PatchKind.Prefix)]
        private static void ScopeEnter(ICompany ___currentProvider, ICompany ___currentClient)
        {
            if (!ModHooks.CanRun(Feature, EnterSite)) return;
            try
            {
                RivalFaultProbe.Trip(EnterSite);
                NegotiationScope.Enter(___currentProvider, ___currentClient);
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(Feature, EnterSite, e);
            }
        }

        /// <summary>Closes the scope whatever happened; the exception of the window (if any) goes on to the game unchanged.</summary>
        [ModPatch(typeof(GameNegotiationWindow), "UpdateScores", PatchKind.Finalizer)]
        private static Exception ScopeExit(Exception __exception)
        {
            try
            {
                NegotiationScope.Exit();
                RivalFaultProbe.Trip(ExitSite);   // after the scope is closed: a failure here must not leave the window's scope open
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(Feature, ExitSite, e);
            }
            return __exception;
        }

        [ModPatch(typeof(GameAIMath), "GetProviderScores", PatchKind.Postfix)]
        private static void ProviderScores(ICompany provider, ref GameAIScores __result)
        {
            if (!NegotiationScope.Active) return;   // every AI-AI deal ends here
            if (!ModHooks.CanRun(Feature, ProviderSite)) return;
            try
            {
                RivalFaultProbe.Trip(ProviderSite);
                NegotiationScope.Adjust(true, provider, ref __result.Interest, ref __result.Price);
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(Feature, ProviderSite, e);
            }
        }

        [ModPatch(typeof(GameAIMath), "GetClientScores", PatchKind.Postfix)]
        private static void ClientScores(ICompany client, ref GameAIScores __result)
        {
            if (!NegotiationScope.Active) return;
            if (!ModHooks.CanRun(Feature, ClientSite)) return;
            try
            {
                RivalFaultProbe.Trip(ClientSite);
                NegotiationScope.Adjust(false, client, ref __result.Interest, ref __result.Price);
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(Feature, ClientSite, e);
            }
        }
    }
}
