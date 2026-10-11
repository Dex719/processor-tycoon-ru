using System.Globalization;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// The sentence about one contract in the letter of the day (FR-5 item 4), split into the part the game is not needed for: which text of the string table fits a
    /// result, and the arguments to put into it. The adapter <c>TenderMailer</c> does <c>ModL10n.L(key, args)</c>. All texts take the same eight arguments, each uses the ones it
    /// needs, so a text can be reworded without touching the code:
    /// <c>{0}</c> the contract, <c>{1}</c> the winning company, <c>{2}</c> the winning CPU (the player's own for a won tender), <c>{3}</c> the chance of the game in percent,
    /// <c>{4}</c> the actual probability in percent, <c>{5}</c> the lines the offer needed, <c>{6}</c> the lines already taken, <c>{7}</c> the lines of the factory.
    /// </summary>
    public static class TenderLines
    {
        /// <summary>How many arguments <see cref="Args"/> gives.</summary>
        public const int ArgCount = 8;

        /// <summary>The key of the text for a result: the outcome and the reason of the result decide (a result that is not understood is read as a plain defeat).</summary>
        public static string KeyOf(TenderResultDto tender)
        {
            if (tender == null) return new TenderOutcome(TenderKind.Lost, TenderReason.Outbid).LineKey;

            switch (tender.outcome)
            {
                case TenderOutcomeClassifier.OutcomeWon:
                    return new TenderOutcome(TenderKind.Won, TenderReason.None).LineKey;
                case TenderOutcomeClassifier.OutcomeVoid:
                    return new TenderOutcome(TenderKind.Void, tender.reasonKey == "ct.tender.noOffers" ? TenderReason.NoOffers : TenderReason.Retired).LineKey;
                default:
                    return new TenderOutcome(TenderKind.Lost, ReasonOf(tender.reasonKey)).LineKey;
            }
        }

        /// <summary>
        /// The eight arguments for the text of the result. Names are cleaned of markup (<see cref="TextSafe.EscapeMarkup"/>); the percentages have the decimals of
        /// the rules and the separator of the language; the lines are those of the player's withdrawn offer (0 when it was not withdrawn).
        /// </summary>
        public static string[] Args(TenderResultDto tender, ContractRules rules, bool decimalComma)
        {
            var args = new string[ArgCount];
            for (int i = 0; i < ArgCount; i++) args[i] = string.Empty;
            if (tender == null) return args;
            if (rules == null) rules = ContractRules.Default;

            args[0] = TextSafe.EscapeMarkup(tender.contractLabel);
            args[1] = TextSafe.EscapeMarkup(tender.winnerCompany);
            args[2] = TextSafe.EscapeMarkup(tender.winnerCpuName);
            args[3] = ChanceFormat.Whole(tender.playerGameChance);
            args[4] = ChanceFormat.Percent(tender.playerActual, rules.ActualPercentDecimals, decimalComma);

            PrunedRowDto own = FirstPlayerWithdrawn(tender);
            args[5] = (own == null ? 0 : own.required).ToString(CultureInfo.InvariantCulture);
            args[6] = (own == null ? 0 : own.contracted).ToString(CultureInfo.InvariantCulture);
            args[7] = (own == null ? 0 : own.capacity).ToString(CultureInfo.InvariantCulture);
            return args;
        }

        private static PrunedRowDto FirstPlayerWithdrawn(TenderResultDto tender)
        {
            if (tender.pruned == null) return null;
            foreach (PrunedRowDto row in tender.pruned)
            {
                if (row != null && row.player) return row;
            }
            return null;
        }

        private static TenderReason ReasonOf(string reasonKey)
        {
            switch (reasonKey)
            {
                case "ct.tender.autoReject": return TenderReason.AutoReject;
                case "ct.tender.shadowed": return TenderReason.Shadowed;
                case "ct.tender.retired": return TenderReason.Retired;
                default: return TenderReason.Outbid;
            }
        }
    }
}
