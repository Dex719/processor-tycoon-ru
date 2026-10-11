using System.Collections.Generic;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// The small rules behind the events of the feature (FR-14 items 1 and 2), without the game: what counts as "an offer was sent" and which source a subscriber sees. The patches ask these
    /// questions, so the answers are tested here and the patches only call.
    /// </summary>
    public static class OfferEventRules
    {
        /// <summary>
        /// A call of <c>Contract.AddOffer</c> sent an offer exactly when the list of offers grew: a duplicate, an offer the game turned down for its requirements and a call that threw leave the
        /// list as it was, and none of those is an event (AC-14.1: one event per sending).
        /// </summary>
        public static bool WasSent(int offersBefore, int offersAfter)
        {
            return offersAfter > offersBefore;
        }

        /// <summary>
        /// The source a subscriber of <c>OfferSent</c> gets: by hand, the automation or the API. An offer the player sent by hand without a screen (no risk, or the mode "never ask") is by hand
        /// like one confirmed on the screen; how it was confirmed is a matter of the log (<see cref="OfferSource.ManualAutoConfirmed"/>), not of the event.
        /// </summary>
        public static OfferSource PublicSource(OfferSource source)
        {
            return source == OfferSource.ManualAutoConfirmed ? OfferSource.Manual : source;
        }
    }

    /// <summary>
    /// A copy of the result of a tender for the subscribers of <c>TenderResolved</c> (FR-14 item 2). The log keeps the original and writes it into the save: a subscriber that changes a field
    /// (a careless one, or one that wants to shorten a name) must not change what the player sees in the log or what is saved. Every list is copied, so the copy shares nothing with the original.
    /// </summary>
    public static class TenderResultCopy
    {
        /// <summary>The copy; null for null.</summary>
        public static TenderResultDto Of(TenderResultDto source)
        {
            if (source == null) return null;

            var copy = new TenderResultDto
            {
                contractKey = source.contractKey,
                contractLabel = source.contractLabel,
                date = source.date,
                outcome = source.outcome,
                reasonKey = source.reasonKey,
                playerCpuId = source.playerCpuId,
                playerCpuName = source.playerCpuName,
                playerGameChance = source.playerGameChance,
                playerActual = source.playerActual,
                winnerCpuId = source.winnerCpuId,
                winnerCompany = source.winnerCompany,
                winnerCpuName = source.winnerCpuName,
                capacityNotChecked = source.capacityNotChecked,
                offers = new List<OfferRowDto>(),
                pruned = new List<PrunedRowDto>(),
            };

            if (source.offers != null)
            {
                foreach (OfferRowDto row in source.offers)
                {
                    if (row == null) continue;
                    copy.offers.Add(new OfferRowDto { company = row.company, cpu = row.cpu, ips = row.ips, chance = row.chance, actual = row.actual, player = row.player });
                }
            }

            if (source.pruned != null)
            {
                foreach (PrunedRowDto row in source.pruned)
                {
                    if (row == null) continue;
                    copy.pruned.Add(new PrunedRowDto
                    {
                        company = row.company, cpu = row.cpu, required = row.required, contracted = row.contracted, capacity = row.capacity, maker = row.maker, player = row.player,
                    });
                }
            }
            return copy;
        }
    }
}
