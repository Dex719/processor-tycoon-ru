using System;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>Who sends an offer, as the gate sees it.</summary>
    public enum OfferSource
    {
        /// <summary>The player pressed "Send offer" (and, when a screen was needed, confirmed it).</summary>
        Manual = 0,

        /// <summary>The player pressed "Send offer" and no screen was needed: no risk, or the mode is "never ask".</summary>
        ManualAutoConfirmed = 1,

        /// <summary>The contract automation of the game (31 December).</summary>
        Automation = 2,

        /// <summary>Another feature through the API, or any other code.</summary>
        Api = 3,
    }

    /// <summary>What the gate does with a call of <c>Contract.AddOffer</c>.</summary>
    public enum GateAction
    {
        /// <summary>The call goes through unchanged and is not logged (the AI's offers, an offer the game would drop anyway).</summary>
        PassThrough = 0,

        /// <summary>The call goes through and, if the offer is really added, it is recorded (<see cref="GateDecision.Source"/> says as what).</summary>
        PassAndRecord = 1,

        /// <summary>The call is held back and the review screen opens (FR-3 item 1).</summary>
        Review = 2,
    }

    /// <summary>What <see cref="OfferGatePolicy.Decide"/> looks at; the prefix fills it with the cheapest facts first.</summary>
    public struct GateInput
    {
        /// <summary>The mod's own call (<c>OfferGate.BypassScope</c>): the player has just confirmed it on the screen.</summary>
        public bool Bypass;

        /// <summary>The contract automation is running (<c>OfferGate.AutomationScope</c>).</summary>
        public bool AutomationRunning;

        /// <summary>The CPU belongs to the player's company.</summary>
        public bool IsPlayerCpu;

        /// <summary>The CPU is already among the offers of the contract: the game ignores the call.</summary>
        public bool AlreadyOffered;

        /// <summary>The CPU fails a requirement of the contract: the game ignores the call (FR-3 item 7; the game keeps the button dead).</summary>
        public bool FailsRequirements;

        /// <summary>The player's setting (FR-4).</summary>
        public ConfirmMode Mode;
    }

    /// <summary>The verdict of <see cref="OfferGatePolicy.Decide"/>.</summary>
    public struct GateDecision
    {
        public GateDecision(GateAction action, OfferSource source)
        {
            Action = action;
            Source = source;
        }

        public GateAction Action;
        public OfferSource Source;
    }

    /// <summary>
    /// The rule of the gate on <c>Contract.AddOffer</c> (design.md, Component 7; FR-3, FR-4), without the game. Checked top to bottom, cheapest question first:
    /// <list type="number">
    /// <item>The mod's own call (the player has confirmed): through, recorded as <see cref="OfferSource.Manual"/>.</item>
    /// <item>The automation: through, tagged <see cref="OfferSource.Automation"/> (it writes its own lines); never a screen (FR-4 item 5).</item>
    /// <item>A CPU of another company (the AI): through, not recorded.</item>
    /// <item>A call the game would ignore (already offered, fails a requirement): through, not recorded.</item>
    /// <item>The player's offer: <see cref="ConfirmPolicy"/> decides between the screen and <see cref="OfferSource.ManualAutoConfirmed"/>. The risks are only asked for when
    /// the mode is "only risky": the caller passes a function and it is not called for the two other modes.</item>
    /// </list>
    /// </summary>
    public static class OfferGatePolicy
    {
        /// <summary>The verdict. <paramref name="risks"/> (the risks of the offer) is called at most once, and only for <see cref="ConfirmMode.Risky"/>; null counts as no risks.</summary>
        public static GateDecision Decide(GateInput input, Func<RiskFlags> risks)
        {
            if (input.Bypass) return new GateDecision(GateAction.PassAndRecord, OfferSource.Manual);
            if (input.AutomationRunning) return new GateDecision(GateAction.PassAndRecord, OfferSource.Automation);
            if (!input.IsPlayerCpu) return new GateDecision(GateAction.PassThrough, OfferSource.Api);
            if (input.AlreadyOffered || input.FailsRequirements) return new GateDecision(GateAction.PassThrough, OfferSource.Manual);

            RiskFlags found = RiskFlags.None;
            if (ConfirmPolicy.FromSetting(ConfirmPolicy.ToSetting(input.Mode)) == ConfirmMode.Risky && risks != null) found = risks();

            return ConfirmPolicy.NeedsConfirmation(input.Mode, found)
                ? new GateDecision(GateAction.Review, OfferSource.Manual)
                : new GateDecision(GateAction.PassAndRecord, OfferSource.ManualAutoConfirmed);
        }
    }
}
