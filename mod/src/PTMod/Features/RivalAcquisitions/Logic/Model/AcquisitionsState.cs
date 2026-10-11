using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// A stake of the player in an AI company (FR-6), as kept in the section of the side save. A plain DTO of public fields: the core's
    /// <c>ModJson.From</c> / <c>ModJson.Bind</c> read and write it as is, so the field names are the JSON keys and must not change.
    /// </summary>
    [Serializable]
    public sealed class StakeData
    {
        /// <summary><c>UniqueID</c> of the target AI company (the key other features use).</summary>
        public int targetUniqueId;

        /// <summary><c>SaveID</c> of the target when the stake was bought; a company whose pair does not match is a stale record.</summary>
        public int targetSaveId;

        /// <summary>The share of the target the player owns, above 0 and below 0.5 (the content limits it to <c>stake.maxShare</c>).</summary>
        public double share;

        /// <summary>The money the player paid for the stake in total.</summary>
        public double invested;

        /// <summary>The dividends received so far.</summary>
        public double dividendsPaid;

        /// <summary>The last dividend received (0 until the first one); the tab "My stakes" shows it. A save without the field reads 0.</summary>
        public double lastDividend;

        /// <summary>The game date of the first purchase, <c>yyyy-MM-dd</c>.</summary>
        public string since = "";
    }

    /// <summary>
    /// The auction of the assets of a bankrupt AI company (FR-3): open for <c>deals.bankrupt.auctionDays</c> after the bankruptcy. A plain DTO of
    /// public fields (see <see cref="StakeData"/>).
    /// </summary>
    [Serializable]
    public sealed class AuctionData
    {
        public int targetUniqueId;

        public int targetSaveId;

        /// <summary>The game date of the bankruptcy, <c>yyyy-MM-dd</c>.</summary>
        public string bankruptOn = "";

        /// <summary>The last day of the auction, <c>yyyy-MM-dd</c>.</summary>
        public string until = "";

        /// <summary><c>SaveID</c> of the CPUs the company still had on sale just before the bankruptcy (empty for an auction opened later by the backfill).</summary>
        public List<int> snapshotCpuSaveIds = new List<int>();

        /// <summary>The auction is over: bought, or expired. The record stays as history.</summary>
        public bool closed;
    }

    /// <summary>A standing rescue order (FR-6): inject money into the company when it is about to go bankrupt. A plain DTO of public fields.</summary>
    [Serializable]
    public sealed class RescueOrderData
    {
        public int targetUniqueId;

        public int targetSaveId;

        /// <summary>The most the player agrees to spend on one rescue.</summary>
        public double maxAmount;

        public bool active;
    }

    /// <summary>
    /// What the feature keeps in its section of the side save file (<c>ModSave</c>, section <c>rival-acquisitions</c>): the deals, the stakes,
    /// the auctions, the rescue orders and a few markers. A plain DTO of public fields that <c>ModJson.Bind</c> / <c>ModJson.From</c> convert
    /// as is, so the field names are the JSON keys and must not change. There is NO version field: the core writes and reads <c>_schema</c> of the
    /// section itself (NFR-3). Everything the deals moved to the player is in the game's own save; this is only the feature's books.
    /// <para>
    /// <see cref="Sanitize"/> is the validation of NFR-7 for what a file may hold: it drops records that cannot be right and repairs the
    /// counters, saying so in a list of English problem lines. Whether a record still points at a living company (the <c>UniqueID</c> / <c>SaveID</c>
    /// pair) is not decided here: that needs the game, and a stale record is ignored but kept.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class AcquisitionsState
    {
        /// <summary>The shares the player may hold are below this (0.5 is control of the company); the content narrows it further.</summary>
        public const double ShareLimit = 0.5;

        /// <summary>The format of the dates the state stores.</summary>
        public const string DateFormat = "yyyy-MM-dd";

        /// <summary>The finished deals, oldest first.</summary>
        public List<DealRecord> deals = new List<DealRecord>();

        /// <summary>The player's stakes, one record per company.</summary>
        public List<StakeData> stakes = new List<StakeData>();

        /// <summary>The auctions of the assets of bankrupt companies, open and closed.</summary>
        public List<AuctionData> auctions = new List<AuctionData>();

        /// <summary>The rescue orders, one record per company.</summary>
        public List<RescueOrderData> rescueOrders = new List<RescueOrderData>();

        /// <summary><c>SaveID</c> of the companies the "in default" mail was already sent for in the current episode of default.</summary>
        public List<int> defaultMailSentForSaveIds = new List<int>();

        /// <summary>The game date of the last hostile takeover, <c>yyyy-MM-dd</c>; empty for none (the cooldown of FR-5).</summary>
        public string lastHostileOn = "";

        /// <summary>The auctions of the companies that were bankrupt before the feature existed were opened (FR-3 item 5).</summary>
        public bool backfillDone;

        /// <summary>The number the next deal gets, from 1.</summary>
        public int nextDealId = 1;

        // ---- a save of a newer mod ----

        /// <summary>
        /// The section of this save was written by a newer version of the mod (or the whole file was): the core keeps it as it is and hands the feature an
        /// empty state, and the feature offers no deal in this save (NFR-3), because what it records would be lost. Not a field of the section: the property
        /// has no public setter, so <c>ModJson</c> neither reads nor writes it.
        /// </summary>
        public bool OffersClosed { get; private set; }

        /// <summary>Closes every offer of the feature for this save (see <see cref="OffersClosed"/>).</summary>
        public void CloseOffers()
        {
            OffersClosed = true;
        }

        // ---- reads for the API ----

        /// <summary>The share of the company that the player owns: the stake of <paramref name="uniqueId"/>, or 0 when there is none (or the record is unusable).</summary>
        public double ShareOf(int uniqueId)
        {
            if (stakes == null) return 0.0;
            for (int i = 0; i < stakes.Count; i++)
            {
                StakeData stake = stakes[i];
                if (stake == null || stake.targetUniqueId != uniqueId) continue;
                return IsValidShare(stake.share) ? stake.share : 0.0;
            }
            return 0.0;
        }

        /// <summary>
        /// The company was bought by the player: a <see cref="DealType.BankruptAssets"/>, <see cref="DealType.Distressed"/> or
        /// <see cref="DealType.Hostile"/> deal exists for it (what <c>RivalAcquisitionsApi.IsAbsorbedByPlayer</c> answers).
        /// </summary>
        public bool IsAbsorbed(int uniqueId)
        {
            if (deals == null) return false;
            for (int i = 0; i < deals.Count; i++)
            {
                DealRecord deal = deals[i];
                if (deal != null && deal.targetUniqueId == uniqueId && IsAbsorbingType(deal.type)) return true;
            }
            return false;
        }

        /// <summary>The auction record of the company that is still open, or null.</summary>
        public AuctionData OpenAuctionOf(int uniqueId)
        {
            if (auctions == null) return null;
            for (int i = 0; i < auctions.Count; i++)
            {
                AuctionData auction = auctions[i];
                if (auction != null && !auction.closed && auction.targetUniqueId == uniqueId) return auction;
            }
            return null;
        }

        // ---- writes ----

        /// <summary>
        /// Puts a deal into the books: gives it the next number (<see cref="nextDealId"/>, raised past every number already used) and appends it.
        /// Returns the number. The executor calls it before the target leaves the game, so <see cref="IsAbsorbed"/> is true from then on.
        /// </summary>
        public int AddDeal(DealRecord deal)
        {
            if (deal == null) throw new ArgumentNullException("deal");
            if (deals == null) deals = new List<DealRecord>();

            int next = nextDealId < 1 ? 1 : nextDealId;
            for (int i = 0; i < deals.Count; i++)
            {
                if (deals[i] != null && deals[i].id >= next) next = deals[i].id + 1;
            }

            deal.id = next;
            nextDealId = next + 1;
            deals.Add(deal);
            return deal.id;
        }

        // ---- type and value checks (shared with the logic that comes later) ----

        /// <summary>True when <paramref name="type"/> is exactly the name of a <see cref="DealType"/> (the spelling <c>DealRecord.type</c> uses).</summary>
        public static bool IsKnownType(string type)
        {
            DealType parsed;
            return !string.IsNullOrEmpty(type)
                   && Enum.TryParse(type, false, out parsed)
                   && Enum.IsDefined(typeof(DealType), parsed)
                   && string.Equals(parsed.ToString(), type, StringComparison.Ordinal);
        }

        /// <summary>True for the deal types after which the target company is out of the game because of the player.</summary>
        public static bool IsAbsorbingType(string type)
        {
            return string.Equals(type, DealType.BankruptAssets.ToString(), StringComparison.Ordinal)
                   || string.Equals(type, DealType.Distressed.ToString(), StringComparison.Ordinal)
                   || string.Equals(type, DealType.Hostile.ToString(), StringComparison.Ordinal);
        }

        /// <summary>A share a stake or a deal record may hold: above 0 and below <see cref="ShareLimit"/>.</summary>
        public static bool IsValidShare(double share)
        {
            return share > 0.0 && share < ShareLimit;
        }

        /// <summary>True for a date written as <c>yyyy-MM-dd</c> that exists.</summary>
        public static bool IsValidDate(string text)
        {
            DateTime parsed;
            return !string.IsNullOrEmpty(text)
                   && DateTime.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed);
        }

        // ---- validation (NFR-7) ----

        /// <summary>
        /// Makes the state safe to use after it was bound from a file: replaces missing lists and strings, drops the records that cannot be right
        /// (an unknown deal type, a negative id or amount, a share outside 0..0.5, a date that is not <c>yyyy-MM-dd</c>, a repeated id), removes
        /// negative ids from the id lists, and raises <see cref="nextDealId"/> above every deal number. Each change adds an English line to
        /// <paramref name="problems"/> (may be null). Returns how many records were dropped. Never throws.
        /// </summary>
        public int Sanitize(List<string> problems)
        {
            int dropped = 0;
            dropped += SanitizeDeals(problems, out int maxDealId);
            dropped += SanitizeStakes(problems);
            dropped += SanitizeAuctions(problems);
            dropped += SanitizeRescueOrders(problems);

            defaultMailSentForSaveIds = CleanIds(defaultMailSentForSaveIds, "defaultMailSentForSaveIds", problems);

            if (lastHostileOn == null) lastHostileOn = "";
            if (lastHostileOn.Length > 0 && !IsValidDate(lastHostileOn))
            {
                Report(problems, "lastHostileOn: '" + lastHostileOn + "' is not a date " + DateFormat + ", it is cleared");
                lastHostileOn = "";
            }

            if (nextDealId < 1)
            {
                Report(problems, "nextDealId: " + nextDealId.ToString(CultureInfo.InvariantCulture) + " is below 1, it is set to 1");
                nextDealId = 1;
            }
            if (nextDealId <= maxDealId)
            {
                Report(problems, "nextDealId: " + nextDealId.ToString(CultureInfo.InvariantCulture) + " is not above the last deal number "
                                 + maxDealId.ToString(CultureInfo.InvariantCulture) + ", it is raised");
                nextDealId = maxDealId + 1;
            }
            return dropped;
        }

        private int SanitizeDeals(List<string> problems, out int maxId)
        {
            maxId = 0;
            if (deals == null) deals = new List<DealRecord>();

            int dropped = 0;
            var kept = new List<DealRecord>(deals.Count);
            var seen = new HashSet<int>();
            for (int i = 0; i < deals.Count; i++)
            {
                DealRecord deal = deals[i];
                string why = CheckDeal(deal, seen);
                if (why != null)
                {
                    dropped++;
                    Report(problems, "deals[" + Index(i) + "]: " + why + ", the record is dropped");
                    continue;
                }

                NormalizeDeal(deal, i, problems);
                kept.Add(deal);
                seen.Add(deal.id);
                if (deal.id > maxId) maxId = deal.id;
            }
            deals = kept;
            return dropped;
        }

        private static string CheckDeal(DealRecord deal, HashSet<int> seen)
        {
            if (deal == null) return "is null";
            if (deal.id < 1) return "id " + Index(deal.id) + " is below 1";
            if (seen.Contains(deal.id)) return "id " + Index(deal.id) + " is repeated";
            if (!IsKnownType(deal.type)) return "type '" + deal.type + "' is not a deal type";
            if (deal.targetUniqueId < 0) return "targetUniqueId " + Index(deal.targetUniqueId) + " is negative";
            if (deal.targetSaveId < 0) return "targetSaveId " + Index(deal.targetSaveId) + " is negative";
            if (!IsFinite(deal.price) || deal.price < 0.0) return "price is not a finite number of 0 or more";
            if (!IsFinite(deal.assumedCash)) return "assumedCash is not a finite number";
            if (!(deal.share >= 0.0 && deal.share < ShareLimit)) return "share is outside 0.." + ShareLimit.ToString(CultureInfo.InvariantCulture);
            if (!IsValidDate(deal.date)) return "date '" + deal.date + "' is not a date " + DateFormat;
            return null;
        }

        private static void NormalizeDeal(DealRecord deal, int index, List<string> problems)
        {
            string path = "deals[" + Index(index) + "]";
            if (deal.targetName == null) deal.targetName = "";
            if (deal.notes == null) deal.notes = "";
            if (deal.techCount < 0) deal.techCount = 0;
            if (deal.providerContracts < 0) deal.providerContracts = 0;
            if (deal.clientContracts < 0) deal.clientContracts = 0;

            deal.cpuSaveIds = CleanIds(deal.cpuSaveIds, path + ".cpuSaveIds", problems);
            deal.retiredCpuSaveIds = CleanIds(deal.retiredCpuSaveIds, path + ".retiredCpuSaveIds", problems);

            var lines = new List<WaferLines>();
            if (deal.lines != null)
            {
                for (int i = 0; i < deal.lines.Count; i++)
                {
                    WaferLines entry = deal.lines[i];
                    if (entry == null || entry.waferId < 0 || entry.lines < 0)
                    {
                        Report(problems, path + ".lines[" + Index(i) + "]: not a wafer size with a number of lines, the entry is dropped");
                        continue;
                    }
                    lines.Add(entry);
                }
            }
            deal.lines = lines;
        }

        private int SanitizeStakes(List<string> problems)
        {
            if (stakes == null) stakes = new List<StakeData>();

            int dropped = 0;
            var kept = new List<StakeData>(stakes.Count);
            var seen = new HashSet<int>();
            for (int i = 0; i < stakes.Count; i++)
            {
                StakeData stake = stakes[i];
                string why = null;
                if (stake == null) why = "is null";
                else if (stake.targetUniqueId < 0 || stake.targetSaveId < 0) why = "has a negative company id";
                else if (!IsValidShare(stake.share)) why = "share is outside 0.." + ShareLimit.ToString(CultureInfo.InvariantCulture);
                else if (!IsFinite(stake.invested) || stake.invested < 0.0) why = "invested is not a finite number of 0 or more";
                else if (!IsFinite(stake.dividendsPaid) || stake.dividendsPaid < 0.0) why = "dividendsPaid is not a finite number of 0 or more";
                else if (seen.Contains(stake.targetUniqueId)) why = "the company " + Index(stake.targetUniqueId) + " already has a stake";

                if (why != null)
                {
                    dropped++;
                    Report(problems, "stakes[" + Index(i) + "]: " + why + ", the record is dropped");
                    continue;
                }

                if (!IsFinite(stake.lastDividend) || stake.lastDividend < 0.0)
                {
                    Report(problems, "stakes[" + Index(i) + "].lastDividend: not a finite number of 0 or more, it is set to 0");
                    stake.lastDividend = 0.0;
                }
                if (stake.since == null) stake.since = "";
                if (stake.since.Length > 0 && !IsValidDate(stake.since))
                {
                    Report(problems, "stakes[" + Index(i) + "].since: '" + stake.since + "' is not a date " + DateFormat + ", it is cleared");
                    stake.since = "";
                }
                seen.Add(stake.targetUniqueId);
                kept.Add(stake);
            }
            stakes = kept;
            return dropped;
        }

        private int SanitizeAuctions(List<string> problems)
        {
            if (auctions == null) auctions = new List<AuctionData>();

            int dropped = 0;
            var kept = new List<AuctionData>(auctions.Count);
            var seen = new HashSet<int>();
            for (int i = 0; i < auctions.Count; i++)
            {
                AuctionData auction = auctions[i];
                string why = null;
                if (auction == null) why = "is null";
                else if (auction.targetUniqueId < 0 || auction.targetSaveId < 0) why = "has a negative company id";
                else if (!IsValidDate(auction.bankruptOn)) why = "bankruptOn '" + auction.bankruptOn + "' is not a date " + DateFormat;
                else if (!IsValidDate(auction.until)) why = "until '" + auction.until + "' is not a date " + DateFormat;
                else if (seen.Contains(auction.targetUniqueId)) why = "the company " + Index(auction.targetUniqueId) + " already has an auction";

                if (why != null)
                {
                    dropped++;
                    Report(problems, "auctions[" + Index(i) + "]: " + why + ", the record is dropped");
                    continue;
                }

                auction.snapshotCpuSaveIds = CleanIds(auction.snapshotCpuSaveIds, "auctions[" + Index(i) + "].snapshotCpuSaveIds", problems);
                seen.Add(auction.targetUniqueId);
                kept.Add(auction);
            }
            auctions = kept;
            return dropped;
        }

        private int SanitizeRescueOrders(List<string> problems)
        {
            if (rescueOrders == null) rescueOrders = new List<RescueOrderData>();

            int dropped = 0;
            var kept = new List<RescueOrderData>(rescueOrders.Count);
            var seen = new HashSet<int>();
            for (int i = 0; i < rescueOrders.Count; i++)
            {
                RescueOrderData order = rescueOrders[i];
                string why = null;
                if (order == null) why = "is null";
                else if (order.targetUniqueId < 0 || order.targetSaveId < 0) why = "has a negative company id";
                else if (!IsFinite(order.maxAmount) || order.maxAmount < 0.0) why = "maxAmount is not a finite number of 0 or more";
                else if (seen.Contains(order.targetUniqueId)) why = "the company " + Index(order.targetUniqueId) + " already has a rescue order";

                if (why != null)
                {
                    dropped++;
                    Report(problems, "rescueOrders[" + Index(i) + "]: " + why + ", the record is dropped");
                    continue;
                }

                seen.Add(order.targetUniqueId);
                kept.Add(order);
            }
            rescueOrders = kept;
            return dropped;
        }

        /// <summary>A list of ids without the negative ones; a null list becomes an empty one.</summary>
        private static List<int> CleanIds(List<int> ids, string path, List<string> problems)
        {
            if (ids == null) return new List<int>();

            int removed = 0;
            var result = new List<int>(ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] < 0) removed++;
                else result.Add(ids[i]);
            }
            if (removed > 0) Report(problems, path + ": " + Index(removed) + " negative id(s) removed");
            return result;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static string Index(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static void Report(List<string> problems, string line)
        {
            if (problems != null) problems.Add(line);
        }
    }
}
