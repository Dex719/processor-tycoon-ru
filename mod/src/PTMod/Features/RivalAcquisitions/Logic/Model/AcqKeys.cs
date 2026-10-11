namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The <c>ModL10n</c> keys the pure logic hands to the interface: the reasons a deal is not available, the errors that block a plan, the
    /// consequences and warnings of a plan. The logic never formats a sentence; the window calls <c>ModL10n.L(key, arg)</c>. Every key is defined
    /// in <c>L10n/ru/rival-acquisitions.json</c> and <c>L10n/en/rival-acquisitions.json</c> (a test checks it) and carries the prefix <c>acq.</c>.
    /// </summary>
    public static class AcqKeys
    {
        // ---- why a deal is not available (DealQuote.ReasonKey) ----

        /// <summary>The player is in default or bankrupt: nothing can be bought.</summary>
        public const string ReasonPlayerDefault = "acq.reason.playerDefault";

        /// <summary>A hostile takeover is on cooldown; the argument is the date it becomes available, <c>dd.MM.yyyy</c>.</summary>
        public const string ReasonCooldown = "acq.reason.cooldown";

        /// <summary>The player lacks money; the argument is the shortfall as a whole number of dollars (the window formats it).</summary>
        public const string ReasonNoCash = "acq.reason.noCash";

        public const string ReasonAbsorbed = "acq.reason.absorbed";
        public const string ReasonBankrupt = "acq.reason.bankrupt";
        public const string ReasonNoAuction = "acq.reason.noAuction";

        /// <summary>The auction is over; the argument is its last day, <c>dd.MM.yyyy</c>.</summary>
        public const string ReasonAuctionExpired = "acq.reason.auctionExpired";

        public const string ReasonNotBankrupt = "acq.reason.notBankrupt";
        public const string ReasonNotInDefault = "acq.reason.notInDefault";
        public const string ReasonUseDistressed = "acq.reason.useDistressed";
        public const string ReasonStakesOff = "acq.reason.stakesOff";
        public const string ReasonStakeMax = "acq.reason.stakeMax";
        public const string ReasonUnsupported = "acq.reason.unsupported";

        // ---- why a confirmed deal was cancelled before any change (DealOutcome.CancelReasonKey) ----

        /// <summary>The company the player chose is not the one in the game any more (another <c>SaveID</c> / <c>UniqueID</c>).</summary>
        public const string ReasonTargetChanged = "acq.reason.targetChanged";

        /// <summary>The status of the company changed since the window showed it (it went into default, out of it, or bankrupt).</summary>
        public const string ReasonStatusChanged = "acq.reason.statusChanged";

        /// <summary>The price went up since the window showed it; the argument is the new price as a whole number of dollars.</summary>
        public const string ReasonPriceChanged = "acq.reason.priceChanged";

        /// <summary>The money service of <c>bankruptcy-recovery</c> (<c>CapitalLedger</c>) has not started: nothing can be paid.</summary>
        public const string ReasonLedgerMissing = "acq.reason.ledgerMissing";

        /// <summary>Another deal is being carried out right now.</summary>
        public const string ReasonBusy = "acq.reason.busy";

        /// <summary>The side file of the save was written by a newer mod: the feature has only defaults for it and offers no deal (NFR-3).</summary>
        public const string ReasonSaveNewer = "acq.reason.saveNewer";

        // ---- why a rescue cannot be made (RescueDecision.ReasonKey; the text follows "Спасти X не удалось: ") ----

        public const string RescueNotNeeded = "acq.rescue.reason.notNeeded";
        public const string RescueNoLimit = "acq.rescue.reason.noLimit";
        public const string RescueNoValue = "acq.rescue.reason.noValue";

        /// <summary>The injection is above the order's limit; the argument is the injection as a whole number of dollars.</summary>
        public const string RescueOverLimit = "acq.rescue.reason.overLimit";

        /// <summary>The player cannot pay the injection; the argument is the shortfall as a whole number of dollars.</summary>
        public const string RescueNoCash = "acq.rescue.reason.noCash";

        public const string RescueOverShare = "acq.rescue.reason.overShare";

        /// <summary>The money service (<c>CapitalLedger</c>) has not started: the injection cannot be paid.</summary>
        public const string RescueNoLedger = "acq.rescue.reason.noLedger";

        // ---- reasons of the reputation changes (ModReputation.Add) ----

        public const string ReputationHostile = "acq.reputation.hostile";
        public const string ReputationRescue = "acq.reputation.rescue";
        public const string ReputationDistressed = "acq.reputation.distressed";
        public const string ReputationBankrupt = "acq.reputation.bankrupt";

        // ---- mails and notifications (design.md "Письма и уведомления"; tags in MailTags) ----

        public const string MailActionDeals = "acq.mail.action.deals";
        public const string MailActionRescueOrder = "acq.mail.action.rescueOrder";

        /// <summary>{0} the company.</summary>
        public const string MailDefaultSubject = "acq.mail.default.subject";

        /// <summary>{0} the company, {1} the days left to the bankruptcy.</summary>
        public const string MailDefaultBody = "acq.mail.default.body";

        /// <summary>{0} the company.</summary>
        public const string MailAuctionSubject = "acq.mail.auction.subject";

        /// <summary>{0} the company, {1} the last day of the auction.</summary>
        public const string MailAuctionBody = "acq.mail.auction.body";

        /// <summary>{0} the company; one subject per kind of purchase.</summary>
        public const string MailDealSubjectBankrupt = "acq.mail.deal.subject.bankrupt";
        public const string MailDealSubjectDistressed = "acq.mail.deal.subject.distressed";
        public const string MailDealSubjectHostile = "acq.mail.deal.subject.hostile";

        /// <summary>{0} the company, {1} the price, {2} the cash taken over, {3} lines, {4} technologies, {5} CPUs, {6} contracts.</summary>
        public const string MailDealBody = "acq.mail.deal.body";

        /// <summary>The same as <see cref="MailDealBody"/> for a deal where a step failed.</summary>
        public const string MailDealBodyPartial = "acq.mail.deal.bodyPartial";

        /// <summary>{0} the company.</summary>
        public const string MailRescueSubject = "acq.mail.rescue.subject";

        /// <summary>{0} the company, {1} the injection, {2} the stake in percent.</summary>
        public const string MailRescueBody = "acq.mail.rescue.body";

        /// <summary>{0} the company.</summary>
        public const string MailStakeLostSubject = "acq.mail.stakeLost.subject";

        /// <summary>{0} the company, {1} the stake in percent.</summary>
        public const string MailStakeLostBody = "acq.mail.stakeLost.body";

        /// <summary>{0} the company.</summary>
        public const string MailRescueFailedSubject = "acq.mail.rescueFailed.subject";

        /// <summary>"Спасти {0} не удалось: {1}": {0} the company, {1} the reason (design key <c>stake.rescueFailed</c>).</summary>
        public const string StakeRescueFailed = "acq.stake.rescueFailed";

        /// <summary>The notification of a dividend: {0} the company, {1} the amount (design key <c>stake.dividend</c>).</summary>
        public const string StakeDividend = "acq.stake.dividend";

        // ---- what blocks a plan (AcquisitionPlan.BlockingErrors) ----

        public const string PlanNotPurchase = "acq.plan.error.notPurchase";
        public const string PlanMustKeepLines = "acq.plan.error.mustKeepLines";
        public const string PlanAckRequired = "acq.plan.error.ackRequired";

        /// <summary>A CPU needs a base model that the snapshot does not hold; the argument is the CPU's name.</summary>
        public const string PlanFamilyBase = "acq.plan.error.familyBase";

        public const string PlanNoWafer = "acq.plan.error.noWafer";

        // ---- consequences that need the "I understand" tick (AcquisitionPlan.Consequences) ----

        public const string ConsequenceX86 = "acq.consequence.x86";
        public const string ConsequenceGlobalFoundries = "acq.consequence.globalFoundries";
        public const string ConsequenceIntelFoundry = "acq.consequence.intelFoundry";

        /// <summary>The target is GlobalFoundries: its exclusive supply contract with AMD passes to the player (information, no tick needed).</summary>
        public const string ConsequenceGfAmdContract = "acq.consequence.gfAmdContract";

        /// <summary>The player is fabless and gets lines: the fabless discount on development is lost (design key <c>preview.becomesIdm</c>).</summary>
        public const string ConsequenceBecomesIdm = "acq.preview.becomesIdm";

        // ---- warnings of a plan (AcquisitionPlan.Warnings): nothing blocks, the player should know ----

        /// <summary>The contract with a foundry does not pass, so the CPU is made at the foundry's base markup; arguments: the CPU's name, the foundry's name.</summary>
        public const string WarnNoContract = "acq.plan.warn.noContract";

        /// <summary>A wafer size the player cannot have takes its lines on a smaller one; arguments: the diameters in mm, from and to.</summary>
        public const string WarnLinesRemapped = "acq.plan.warn.linesRemapped";

        /// <summary>The player does not develop CPUs, so the target's CPUs are retired instead of passing over.</summary>
        public const string WarnNoCpuDivision = "acq.plan.warn.noCpuDivision";
    }
}
