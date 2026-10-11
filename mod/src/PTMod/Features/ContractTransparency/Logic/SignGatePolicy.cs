namespace PTMod.Features.ContractTransparency
{
    /// <summary>What the prefix of <c>ContractNegotiationWindow.SignContract</c> does with a click on "Sign contract" (FR-10).</summary>
    public enum SignAction
    {
        /// <summary>The original runs now (and the deal is logged after it).</summary>
        PassThrough = 0,

        /// <summary>The signature is put off and the player is asked.</summary>
        Ask = 1,

        /// <summary>A question about a deal is open already: the click is swallowed, so one question cannot become two deals.</summary>
        Swallow = 2,
    }

    /// <summary>What <see cref="SignGatePolicy.Decide"/> needs to know about the click.</summary>
    public struct SignInput
    {
        /// <summary>The mod's own call of the original after the player said "Sign".</summary>
        public bool Bypass;

        /// <summary>The setting "confirmation of offers" (the deals follow it, FR-10 item 1).</summary>
        public ConfirmMode Mode;

        /// <summary>The private fields of the negotiation window the question reads were all found (FR-12 item 3).</summary>
        public bool FieldsAvailable;

        /// <summary>A question about a deal is up or waits.</summary>
        public bool QuestionOpen;
    }

    /// <summary>
    /// The rule of the gate before a deal between companies is signed (FR-10 items 1, 3 and 4): the mode "never ask" and the mod's own call go through; so does a window whose fields the mod cannot read (the
    /// vanilla game, nothing is hidden); a click while the question is open is swallowed; any other click is asked about. A deal has no risk model in this spec, so "only risky" asks as "always".
    /// </summary>
    public static class SignGatePolicy
    {
        public static SignAction Decide(SignInput input)
        {
            if (input.Bypass) return SignAction.PassThrough;
            if (ConfirmPolicy.FromSetting(ConfirmPolicy.ToSetting(input.Mode)) == ConfirmMode.Never) return SignAction.PassThrough;
            if (!input.FieldsAvailable) return SignAction.PassThrough;
            if (input.QuestionOpen) return SignAction.Swallow;
            return SignAction.Ask;
        }
    }
}
