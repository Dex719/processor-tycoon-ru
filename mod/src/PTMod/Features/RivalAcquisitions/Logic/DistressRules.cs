using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// <c>DistressRules</c> (FR-3, FR-11; design.md "RivalDistressMonitor"): the bookkeeping of the rivals' troubles, independent of the game.
    /// <list type="bullet">
    /// <item><b>Default mail</b>: one mail per company and episode of default; the episode ends (its mark is dropped) when the company is out
    /// of default again (the monthly check).</item>
    /// <item><b>Auctions</b>: opened on the day of the bankruptcy for <c>deals.bankrupt.auctionDays</c>, with the CPUs that were on sale just
    /// before it; closed when bought or when the last day is past. One record per <c>UniqueID</c>: a new company with an old id replaces it.</item>
    /// <item><b>Backfill</b>: a save made before the feature gets auctions for its bankrupt companies on the first load, without CPUs.</item>
    /// <item><b>What is not a bankruptcy of the rival's own</b>: the player, the target of a deal, a company bought by the player, and anything
    /// during a save load or the new-game fast-forward (FR-11 item 3).</item>
    /// </list>
    /// </summary>
    public static class DistressRules
    {
        /// <summary>Days of default before the game decides on the bankruptcy (<c>BankruptcyHandler.defaultTimeLimit</c> in the scene): the text of the mail.</summary>
        public const int DaysToBankruptcy = 120;

        // ---- the mail of a default ----

        /// <summary>No default mail was sent for this company in its current episode.</summary>
        public static bool ShouldMailDefault(AcquisitionsState s, int saveId)
        {
            return s != null && (s.defaultMailSentForSaveIds == null || !s.defaultMailSentForSaveIds.Contains(saveId));
        }

        /// <summary>The default mail of this episode was sent.</summary>
        public static void MarkDefaultMailed(AcquisitionsState s, int saveId)
        {
            if (s == null) return;
            if (s.defaultMailSentForSaveIds == null) s.defaultMailSentForSaveIds = new List<int>();
            if (!s.defaultMailSentForSaveIds.Contains(saveId)) s.defaultMailSentForSaveIds.Add(saveId);
        }

        /// <summary>
        /// Ends the episodes of the companies that are not in default any more (or are gone): <paramref name="stillInDefault"/> answers for a
        /// <c>SaveID</c>. Returns how many marks were dropped.
        /// </summary>
        public static int EndEpisodes(AcquisitionsState s, Func<int, bool> stillInDefault)
        {
            if (s == null || s.defaultMailSentForSaveIds == null || stillInDefault == null) return 0;
            return s.defaultMailSentForSaveIds.RemoveAll(id => !stillInDefault(id));
        }

        // ---- auctions ----

        /// <summary>The last day of an auction that opens on <paramref name="bankruptOn"/> for <paramref name="days"/> days, <c>yyyy-MM-dd</c>.</summary>
        public static string Until(DateTime bankruptOn, int days)
        {
            return bankruptOn.Date.AddDays(Math.Max(0, days)).ToString(AcquisitionsState.DateFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Opens the auction of the company on <paramref name="today"/> for <paramref name="days"/> days with the CPUs of
        /// <paramref name="snapshotCpuSaveIds"/> (null: none). An earlier record of the same <c>UniqueID</c> is replaced. Returns the record.
        /// </summary>
        public static AuctionData OpenAuction(AcquisitionsState s, int uniqueId, int saveId, DateTime today, int days, IEnumerable<int> snapshotCpuSaveIds)
        {
            if (s == null) throw new ArgumentNullException("s");
            if (s.auctions == null) s.auctions = new List<AuctionData>();
            s.auctions.RemoveAll(a => a == null || a.targetUniqueId == uniqueId);

            var auction = new AuctionData
            {
                targetUniqueId = uniqueId,
                targetSaveId = saveId,
                bankruptOn = today.Date.ToString(AcquisitionsState.DateFormat, CultureInfo.InvariantCulture),
                until = Until(today, days),
                closed = false,
            };
            if (snapshotCpuSaveIds != null)
            {
                foreach (int id in snapshotCpuSaveIds)
                {
                    if (id >= 0 && !auction.snapshotCpuSaveIds.Contains(id)) auction.snapshotCpuSaveIds.Add(id);
                }
            }
            s.auctions.Add(auction);
            return auction;
        }

        /// <summary>Closes the open auction of this very company (the pair of ids); true when one was open.</summary>
        public static bool CloseAuction(AcquisitionsState s, int uniqueId, int saveId)
        {
            if (s == null || s.auctions == null) return false;
            bool closed = false;
            foreach (AuctionData auction in s.auctions)
            {
                if (auction == null || auction.closed || auction.targetUniqueId != uniqueId || auction.targetSaveId != saveId) continue;
                auction.closed = true;
                closed = true;
            }
            return closed;
        }

        /// <summary>Closes the auctions whose last day is before <paramref name="today"/> (FR-3 item 4). Returns how many; nothing for an unknown today.</summary>
        public static int CloseExpired(AcquisitionsState s, DateTime today)
        {
            if (s == null || s.auctions == null || today == default(DateTime)) return 0;
            int closed = 0;
            foreach (AuctionData auction in s.auctions)
            {
                if (auction == null || auction.closed) continue;
                DateTime until;
                bool known = DateTime.TryParseExact(auction.until, AcquisitionsState.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out until);
                if (!known || today.Date > until.Date)
                {
                    auction.closed = true;
                    closed++;
                }
            }
            return closed;
        }

        /// <summary>
        /// The bankrupt company needs an auction from the backfill (FR-3 item 5): it is bankrupt, the player did not buy it, and there is no
        /// record of an auction for this very company (an open, closed or expired one counts; a record of an older company with the same
        /// <c>UniqueID</c> does not).
        /// </summary>
        public static bool NeedsBackfill(AcquisitionsState s, int uniqueId, int saveId, bool isBankrupt)
        {
            if (!isBankrupt || s == null) return false;
            if (IsAbsorbedPair(s, uniqueId, saveId)) return false;
            if (s.auctions != null)
            {
                foreach (AuctionData auction in s.auctions)
                {
                    if (auction != null && auction.targetUniqueId == uniqueId && auction.targetSaveId == saveId) return false;
                }
            }
            return true;
        }

        /// <summary>The player bought this very company (a record of an absorbing deal with the same pair of ids).</summary>
        public static bool IsAbsorbedPair(AcquisitionsState s, int uniqueId, int saveId)
        {
            if (s == null || s.deals == null) return false;
            foreach (DealRecord deal in s.deals)
            {
                if (deal != null && deal.targetUniqueId == uniqueId && deal.targetSaveId == saveId && AcquisitionsState.IsAbsorbingType(deal.type)) return true;
            }
            return false;
        }

        /// <summary>
        /// The bankruptcy is not one the feature reacts to with an auction, a mail and <c>RivalBankrupt</c>: the player's, the deal's own
        /// removal of its target, a company the player bought, or one during a save load or the new-game fast-forward (FR-10 item 5, FR-11 item 3).
        /// </summary>
        public static bool IgnoresBankruptcy(bool isPlayer, bool isExecutingTarget, bool absorbed, bool loading, bool fastForward)
        {
            return isPlayer || isExecutingTarget || absorbed || loading || fastForward;
        }
    }
}
