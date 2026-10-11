namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The <c>ModL10n</c> keys the pure models of the windows hand to the interface (TSK-015…TSK-018): statuses, the labels of the deal buttons, the blocks and
    /// lines of the preview, the kinds of deals of the journal. The models never compose a sentence themselves; every key is defined in
    /// <c>L10n/ru/rival-acquisitions.json</c> and <c>L10n/en/rival-acquisitions.json</c> (a test checks it) and carries the prefix <c>acq.</c>. The keys only the
    /// windows use (captions of buttons, tabs, notices) are written in the window code and checked by a test that scans it.
    /// </summary>
    public static class UiKeys
    {
        // ---- the status of a company (the table "Companies") ----

        public const string StatusHealthy = "acq.status.healthy";

        /// <summary>{0} the days in default.</summary>
        public const string StatusDefault = "acq.status.default";

        /// <summary>{0} the last day of the auction, <c>dd.MM.yyyy</c>.</summary>
        public const string StatusAuction = "acq.status.auction";

        /// <summary>{0} the day of the takeover.</summary>
        public const string StatusAbsorbed = "acq.status.absorbed";

        /// <summary>The same without a day (the journal has no record of the takeover).</summary>
        public const string StatusAbsorbedPlain = "acq.status.absorbed.plain";

        public const string StatusBankrupt = "acq.status.bankrupt";

        // ---- the buttons of the deals ----

        /// <summary>{0} the price.</summary>
        public const string DealBankrupt = "acq.deal.bankrupt";

        public const string DealDistressed = "acq.deal.distressed";
        public const string DealHostile = "acq.deal.hostile";

        /// <summary>The same captions without a price (a deal shown closed for a company that is not of the kind it is for).</summary>
        public const string DealBankruptPlain = "acq.deal.bankrupt.plain";

        public const string DealDistressedPlain = "acq.deal.distressed.plain";
        public const string DealHostilePlain = "acq.deal.hostile.plain";

        /// <summary>"Buy a stake" without a price (when none can be worked out).</summary>
        public const string DealStake = "acq.deal.stake";

        /// <summary>{0} the price of the smallest stake.</summary>
        public const string DealStakeFrom = "acq.deal.stakeFrom";

        // ---- the preview of a purchase ----

        /// <summary>{0} the company.</summary>
        public const string PreviewTitleBankrupt = "acq.preview.title.bankrupt";

        public const string PreviewTitleDistressed = "acq.preview.title.distressed";
        public const string PreviewTitleHostile = "acq.preview.title.hostile";

        public const string ConfirmTitle = "acq.preview.confirm.title";

        /// <summary>{0} the company, {1} the price, {2} the balance after the deal.</summary>
        public const string ConfirmBankrupt = "acq.preview.confirm.bankrupt";

        public const string ConfirmDistressed = "acq.preview.confirm.distressed";
        public const string ConfirmHostile = "acq.preview.confirm.hostile";

        public const string SectionValuation = "acq.preview.section.valuation";
        public const string SectionMoney = "acq.preview.section.money";
        public const string SectionTransfers = "acq.preview.section.transfers";
        public const string SectionRemoved = "acq.preview.section.removed";
        public const string SectionWarnings = "acq.preview.section.warnings";
        public const string SectionConsequences = "acq.preview.section.consequences";

        /// <summary>{0} the fab, {1} the technologies, {2} the CPUs, {3} the premium for the stake (design key <c>preview.valuation</c>).</summary>
        public const string PreviewValuation = "acq.preview.valuation";

        /// <summary>{0} the lowest price (design key <c>preview.cashFloor</c>).</summary>
        public const string PreviewCashFloor = "acq.preview.cashFloor";

        public const string PreviewCashStays = "acq.preview.cashStays";

        public const string LabelValuation = "acq.preview.label.valuation";
        public const string LabelMultiplier = "acq.preview.label.multiplier";
        public const string LabelChannel = "acq.preview.label.channel";
        public const string LabelOwnedShare = "acq.preview.label.ownedShare";
        public const string LabelPrice = "acq.preview.label.price";
        public const string LabelCash = "acq.preview.label.cash";
        public const string LabelDebt = "acq.preview.label.debt";
        public const string LabelNet = "acq.preview.label.net";
        public const string LabelSaleIncome = "acq.preview.label.saleIncome";
        public const string LabelBalanceAfter = "acq.preview.label.balanceAfter";
        public const string LabelReputation = "acq.preview.label.reputation";

        /// <summary>{0} the lines, joined.</summary>
        public const string PreviewLines = "acq.preview.lines";

        public const string PreviewLinesSold = "acq.preview.linesSold";

        /// <summary>{0} the number of lines, {1} the wafer diameter in mm.</summary>
        public const string PreviewLineItem = "acq.preview.lineItem";

        /// <summary>{0} the number of lines, {1} the diameter they have, {2} the diameter they move to.</summary>
        public const string PreviewLineItemMoved = "acq.preview.lineItemMoved";

        /// <summary>{0} the rise per month (design key <c>preview.opex</c>).</summary>
        public const string PreviewOpex = "acq.preview.opex";

        /// <summary>{0} the number.</summary>
        public const string PreviewTechs = "acq.preview.techs";

        public const string PreviewTechsPartial = "acq.preview.techsPartial";
        public const string PreviewNodes = "acq.preview.nodes";
        public const string PreviewCpus = "acq.preview.cpus";
        public const string PreviewCpusRetired = "acq.preview.cpusRetired";
        public const string PreviewOem = "acq.preview.oem";
        public const string PreviewInHouse = "acq.preview.inHouse";
        public const string PreviewProviderContracts = "acq.preview.providerContracts";

        /// <summary>{0} the client, {1} the provider (design key <c>preview.clientContract</c>).</summary>
        public const string PreviewClientContract = "acq.preview.clientContract";

        public const string PreviewFoundryFlag = "acq.preview.foundryFlag";

        /// <summary>The researched CPU architectures and their support pass to the player (FR-16; <c>cpu-architectures</c> is on).</summary>
        public const string PreviewRelatedArchitectures = "acq.preview.relatedArch";

        /// <summary>The GPU lineup of the target passes to the player (FR-16; <c>gpu-product-line</c> is on and the target has a GPU division).</summary>
        public const string PreviewRelatedGpu = "acq.preview.relatedGpu";

        /// <summary>The assets of a bankrupt company: a GPU lineup, if there was one, does not pass (its vendor left the market).</summary>
        public const string PreviewGpuNotPassed = "acq.preview.gpuNotPassed";

        public const string PreviewNothing = "acq.preview.nothing";

        public const string PreviewRetireCpus = "acq.preview.retireCpus";

        /// <summary>{0} the two companies of the contract (design key <c>preview.contractDropped</c>).</summary>
        public const string PreviewContractDropped = "acq.preview.contractDropped";

        public const string PreviewProviderDropped = "acq.preview.providerDropped";

        public const string TipValuationHeader = "acq.preview.tip.valuation";

        /// <summary>{0} the number, {1} the names.</summary>
        public const string TipTechs = "acq.preview.tip.techs";

        public const string TipCpus = "acq.preview.tip.cpus";
        public const string TipTechsHeader = "acq.preview.tip.techsHeader";
        public const string TipNodesHeader = "acq.preview.tip.nodesHeader";
        public const string TipCpusHeader = "acq.preview.tip.cpusHeader";
        public const string TipContractsHeader = "acq.preview.tip.contractsHeader";

        /// <summary>{0} how many more.</summary>
        public const string TipMore = "acq.preview.tip.more";

        // ---- the journal ----

        public const string HistoryBankrupt = "acq.history.type.bankrupt";
        public const string HistoryDistressed = "acq.history.type.distressed";
        public const string HistoryHostile = "acq.history.type.hostile";
        public const string HistoryStake = "acq.history.type.stake";
        public const string HistoryRescue = "acq.history.type.rescue";
    }
}
