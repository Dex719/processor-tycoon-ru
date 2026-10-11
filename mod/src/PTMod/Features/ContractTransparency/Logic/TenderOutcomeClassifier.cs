namespace PTMod.Features.ContractTransparency
{
    /// <summary>How a tender ended for the player.</summary>
    public enum TenderKind
    {
        /// <summary>The draw picked one of the player's CPUs.</summary>
        Won = 0,

        /// <summary>The draw picked another company's CPU.</summary>
        Lost = 1,

        /// <summary>Nobody got the contract: no offer was left, or the CPU the draw picked is retired.</summary>
        Void = 2,
    }

    /// <summary>Why a tender ended the way it did (FR-5 items 2 and 3).</summary>
    public enum TenderReason
    {
        /// <summary>No reason to give: the tender was won.</summary>
        None = 0,

        /// <summary>Lost: a competitor's offer was drawn (the plain case).</summary>
        Outbid = 1,

        /// <summary>Lost: the game withdrew the player's best offer for lack of production lines (the player's "cancel offers automatically" switch was on).</summary>
        AutoReject = 2,

        /// <summary>Lost: the CPU the player offered last did not play; a stronger CPU of the same company took its place, and that one lost.</summary>
        Shadowed = 3,

        /// <summary>Lost: the CPU that played for the player is retired (a win would have voided the contract); or void: the CPU the draw picked is retired.</summary>
        Retired = 4,

        /// <summary>Void: every offer was withdrawn for lack of lines.</summary>
        NoOffers = 5,
    }

    /// <summary>What the recorder saw of a draw, boiled down to the facts the verdict depends on.</summary>
    public struct TenderFacts
    {
        /// <summary>Offers left after the game's check of production lines: the field of the draw. 0 when nothing was left.</summary>
        public int OffersAfter;

        /// <summary><c>Contract.ChosenCpuID</c> after the draw: -1 for nobody (nothing was left, or the picked CPU is retired).</summary>
        public int ChosenCpuId;

        /// <summary>The chosen CPU belongs to the player.</summary>
        public bool ChosenIsPlayer;

        /// <summary>The player's strongest offer (by IPS) was withdrawn by the check of lines.</summary>
        public bool PlayerStrongestWithdrawn;

        /// <summary>The CPU that played for the player (the best of the player's offers in the draw) is retired.</summary>
        public bool PlayerCpuRetired;

        /// <summary>The CPU the player offered last stayed out of the draw because the player's stronger CPU played instead.</summary>
        public bool PlayerCpuShadowed;
    }

    /// <summary>The verdict of <see cref="TenderOutcomeClassifier.Classify"/>.</summary>
    public struct TenderOutcome
    {
        public TenderOutcome(TenderKind kind, TenderReason reason)
        {
            Kind = kind;
            Reason = reason;
        }

        public TenderKind Kind;
        public TenderReason Reason;

        /// <summary><c>Won</c>, <c>Lost</c> or <c>Void</c>: what <c>TenderResultDto.outcome</c> holds.</summary>
        public string Name
        {
            get
            {
                switch (Kind)
                {
                    case TenderKind.Won: return TenderOutcomeClassifier.OutcomeWon;
                    case TenderKind.Lost: return TenderOutcomeClassifier.OutcomeLost;
                    default: return TenderOutcomeClassifier.OutcomeVoid;
                }
            }
        }

        /// <summary>The key in <c>ModL10n</c> of the short reason ("a competitor won", "withdrawn by auto-cancel"): what <c>TenderResultDto.reasonKey</c> holds.</summary>
        public string ReasonKey
        {
            get
            {
                switch (Reason)
                {
                    case TenderReason.Outbid: return "ct.tender.outbid";
                    case TenderReason.AutoReject: return "ct.tender.autoReject";
                    case TenderReason.Shadowed: return "ct.tender.shadowed";
                    case TenderReason.Retired: return "ct.tender.retired";
                    case TenderReason.NoOffers: return "ct.tender.noOffers";
                    default: return "ct.tender.won";
                }
            }
        }

        /// <summary>The key of the sentence about one contract in the letter of the day ("lost: Intel won with i386; your chance 40 % (actual 21 %)").</summary>
        public string LineKey
        {
            get
            {
                switch (Kind)
                {
                    case TenderKind.Won:
                        return "ct.tender.line.won";
                    case TenderKind.Lost:
                        switch (Reason)
                        {
                            case TenderReason.AutoReject: return "ct.tender.lost.autoReject";
                            case TenderReason.Shadowed: return "ct.tender.lost.shadowed";
                            case TenderReason.Retired: return "ct.tender.lost.retired";
                            default: return "ct.tender.lost.outbid";
                        }
                    default:
                        return Reason == TenderReason.NoOffers ? "ct.tender.void.noOffers" : "ct.tender.void.retired";
                }
            }
        }
    }

    /// <summary>
    /// The verdict on a tender the player took part in (design.md, Component 8; FR-5 items 2 and 3). A table of conditions, checked top to bottom, free of the game:
    /// <list type="number">
    /// <item>No offer left after the check of lines: <b>void, no offers</b> (the check may even have withdrawn the player's offer; the log of withdrawn offers says so).</item>
    /// <item>Offers left but nobody chosen: the draw picked a retired CPU, the game reset the choice: <b>void, retired</b> (for everyone).</item>
    /// <item>The chosen CPU is the player's: <b>won</b>.</item>
    /// <item>Lost. The player's strongest offer was withdrawn by the check of lines: <b>lost, auto-reject</b>.</item>
    /// <item>The CPU that played for the player is retired: <b>lost, retired</b>.</item>
    /// <item>The CPU the player offered last was passed over for a stronger one of the player's: <b>lost, shadowed</b>.</item>
    /// <item>Otherwise <b>lost, outbid</b>.</item>
    /// </list>
    /// </summary>
    public static class TenderOutcomeClassifier
    {
        /// <summary><c>TenderResultDto.outcome</c> of a won tender.</summary>
        public const string OutcomeWon = "Won";

        /// <summary><c>TenderResultDto.outcome</c> of a lost tender.</summary>
        public const string OutcomeLost = "Lost";

        /// <summary><c>TenderResultDto.outcome</c> of a tender nobody got.</summary>
        public const string OutcomeVoid = "Void";

        /// <summary>True for one of <see cref="OutcomeWon"/>, <see cref="OutcomeLost"/>, <see cref="OutcomeVoid"/>.</summary>
        public static bool IsKnownOutcome(string name)
        {
            return name == OutcomeWon || name == OutcomeLost || name == OutcomeVoid;
        }

        /// <summary>The verdict for these facts.</summary>
        public static TenderOutcome Classify(TenderFacts facts)
        {
            if (facts.OffersAfter <= 0) return new TenderOutcome(TenderKind.Void, TenderReason.NoOffers);
            if (facts.ChosenCpuId < 0) return new TenderOutcome(TenderKind.Void, TenderReason.Retired);
            if (facts.ChosenIsPlayer) return new TenderOutcome(TenderKind.Won, TenderReason.None);

            if (facts.PlayerStrongestWithdrawn) return new TenderOutcome(TenderKind.Lost, TenderReason.AutoReject);
            if (facts.PlayerCpuRetired) return new TenderOutcome(TenderKind.Lost, TenderReason.Retired);
            if (facts.PlayerCpuShadowed) return new TenderOutcome(TenderKind.Lost, TenderReason.Shadowed);
            return new TenderOutcome(TenderKind.Lost, TenderReason.Outbid);
        }
    }
}
