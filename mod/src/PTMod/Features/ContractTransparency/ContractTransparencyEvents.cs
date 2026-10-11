using System;
using PTMod.Core;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// The events of the feature for the other features (design.md, Component 12; FR-14 items 1 and 2): an offer of the player was sent, a tender the player took part in was drawn. A feature that
    /// wants the first offer of a game (a tutorial, an achievement) subscribes here instead of patching <c>Contract.AddOffer</c> itself.
    /// <list type="bullet">
    /// <item><b>Raised through <c>ModHooks.Raise</c>.</b> The handlers run one by one, in the order of subscription; a handler of a feature that is switched off is skipped and an exception of a
    /// handler is counted against the handler's own feature, so a broken subscriber neither breaks the game nor stops the others, and never counts against this feature.</item>
    /// <item><b><see cref="OfferSent"/></b> comes once for every offer of the player that was really added to a contract: by hand (with or without the review screen), by the contract automation, or
    /// through <c>Contract.AddOffer</c> from other code. An offer the game ignores (already offered, a requirement failed) is not an event. The arguments are the key of the contract
    /// (<see cref="ContractInspector.ContractKey"/>, the same string as in the log), the id of the CPU and the source: <see cref="OfferSource.Manual"/>, <see cref="OfferSource.Automation"/> or
    /// <see cref="OfferSource.Api"/>. It comes after the offer is in the contract and in the log.</item>
    /// <item><b><see cref="TenderResolved"/></b> comes for the result of a tender the player took part in, as it is written to the log (FR-5): won, lost or not held. Not in the fast-forward of a new
    /// game and not under the loading screen. The subscriber gets a copy (<see cref="TenderResultCopy"/>): changing it changes nothing in the log.</item>
    /// </list>
    /// The same moments feed the counters of the predicates <c>ct.offersSent</c> and <c>ct.tendersWon</c> (<see cref="ContractPredicates"/>). Main thread only.
    /// </summary>
    public static class ContractTransparencyEvents
    {
        private const string OfferSentEvent = "ContractTransparencyEvents.OfferSent";
        private const string TenderResolvedEvent = "ContractTransparencyEvents.TenderResolved";

        /// <summary>An offer of the player was sent: the key of the contract, <c>Cpu.SaveID</c>, who sent it.</summary>
        public static event Action<string, int, OfferSource> OfferSent;

        /// <summary>A tender the player took part in was drawn: a copy of the result that went to the log.</summary>
        public static event Action<TenderResultDto> TenderResolved;

        /// <summary>Called by the postfix of <c>Contract.AddOffer</c> when the offer was added. Never throws.</summary>
        internal static void RaiseOfferSent(string contractKey, int cpuId, OfferSource source)
        {
            Action<string, int, OfferSource> handlers = OfferSent;
            if (handlers == null) return;

            try
            {
                ModHooks.Raise(OfferSentEvent, handlers, contractKey ?? string.Empty, cpuId, OfferEventRules.PublicSource(source));
            }
            catch (Exception ex)
            {
                ModLog.Error(ContractParamSpecs.FeatureId, "events: OfferSent could not be raised", ex);
            }
        }

        /// <summary>Called by the postfix of <c>Contract.ChooseCpu</c> after the result was written to the log. Never throws.</summary>
        internal static void RaiseTenderResolved(TenderResultDto result)
        {
            Action<TenderResultDto> handlers = TenderResolved;
            if (handlers == null || result == null) return;

            try
            {
                ModHooks.Raise(TenderResolvedEvent, handlers, TenderResultCopy.Of(result));
            }
            catch (Exception ex)
            {
                ModLog.Error(ContractParamSpecs.FeatureId, "events: TenderResolved could not be raised", ex);
            }
        }
    }
}
