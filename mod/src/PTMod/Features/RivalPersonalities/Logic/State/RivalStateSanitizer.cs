using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The limits that a section read from the side save file is held to (design.md "Validation Rules" and "Security Considerations", FR-10, NFR-3, TSK-023):
    /// how long each list may be, how long a text, which range a number lies in. The sizes of the lists that the books keep while the game runs are the
    /// books' own constants (<see cref="ReactionBook.MaxHistory"/>, <see cref="CautionBook.MaxEntries"/>...), repeated here so that one place shows
    /// what a damaged or hand-edited file can and cannot do.
    /// </summary>
    public static class RivalStateLimits
    {
        /// <summary>The history of the reactions: the last 100 lines of all rivals.</summary>
        public const int MaxHistory = ReactionBook.MaxHistory;

        /// <summary>The history of the attitude of one rival: the last 20 lines (the panel shows ten).</summary>
        public const int MaxAttitudeHistory = 20;

        /// <summary>The reactions that are planned and not carried out yet.</summary>
        public const int MaxPending = ReactionBook.MaxPending;

        /// <summary>The reactions in effect.</summary>
        public const int MaxActive = ReactionBook.MaxActive;

        /// <summary>The cautions after a public flaw.</summary>
        public const int MaxCautions = CautionBook.MaxEntries;

        /// <summary>The rivals whose character is shown: the 13 AI companies of the game.</summary>
        public const int MaxRevealed = 13;

        /// <summary>The companies a list that holds one record per company may name (the game has 13; a modded game may have more).</summary>
        public const int MaxCompanies = 64;

        /// <summary>The pairs "company and segment" of the undercut cooldowns.</summary>
        public const int MaxCooldowns = MaxCompanies * 4;

        /// <summary>The rows of the share snapshot: the player and every company.</summary>
        public const int MaxShareRows = MaxCompanies + 1;

        /// <summary>The target prices the price book holds (a rival has some 40 CPUs; the world some 300 to 1000).</summary>
        public const int MaxPriceBook = 1000;

        /// <summary>The longest id of a reaction or a share row (<see cref="PriceCutMath.MaxReactionIdLength"/>).</summary>
        public const int MaxIdLength = PriceCutMath.MaxReactionIdLength;

        /// <summary>The longest word of a text (archetype, reaction type, trigger, outcome).</summary>
        public const int MaxTextLength = 64;

        /// <summary>The attitude lies in -100 to +100.</summary>
        public const float MaxAttitude = 100f;

        /// <summary>One change of the attitude (a single step is far below it).</summary>
        public const float MaxDelta = 200f;

        /// <summary>A price above this is not a price (the dearest CPU of the game costs some thousands).</summary>
        public const float MaxPrice = 1000000f;

        /// <summary>The warning of a reaction, in days after the delivery of the mail.</summary>
        public const int MaxTelegraphDays = 365;

        /// <summary>A counter of reactions.</summary>
        public const int MaxCounter = 100000;

        /// <summary>The number of the next reaction stays far from <c>int.MaxValue</c>.</summary>
        public const int MaxSequence = 1000000000;
    }

    /// <summary>
    /// Brings a state that was just read from the file into the shape the rest of the feature relies on (FR-10, NFR-3, TSK-023): numbers are clipped to their
    /// range, texts to their length, dates must be exactly <c>yyyy-MM-dd</c>, a list never exceeds its limit (<see cref="RivalStateLimits"/>), a record that names
    /// no company, has no id or cannot be used is dropped, a company is listed once. The values themselves are never invented: what cannot be repaired by a clip
    /// is dropped. Each list adds at most one line to <c>problems</c> (how many records were dropped or corrected and why), so a damaged file is one short
    /// paragraph in the log, not hundreds of lines.
    /// <para>
    /// Not checked here: whether a company or an archetype exists in the game (the content is read later and the books handle it: an unknown archetype behaves
    /// as the fallback, a record of an unknown company stays unused) and whether a CPU of the price book still exists (<c>PriceBookCore</c> drops those on the
    /// first look). The reactions with a date that cannot be read are dropped here; the effects that the event system holds for them come back through its own
    /// handlers (<c>Restore</c>) with their own dates. Pure; never throws; safe to call twice on the same state.
    /// </para>
    /// </summary>
    public static class RivalStateSanitizer
    {
        /// <summary>Repairs <paramref name="state"/> in place and returns it; a null state gives a new default one.</summary>
        public static RivalState Sanitize(RivalState state, List<string> problems)
        {
            if (state == null) return new RivalState();

            CleanGrace(state, problems);
            CleanAssignments(state, problems);
            CleanRevealed(state, problems);
            CleanAttitudes(state, problems);
            CleanShares(state, problems);
            CleanCooldowns(state, problems);
            CleanPending(state, problems);
            CleanActive(state, problems);
            CleanPriceBook(state, problems);
            CleanCautions(state, problems);
            CleanCounters(state, problems);
            CleanHistory(state, problems);
            CleanDormant(state, problems);
            CleanSequence(state, problems);
            return state;
        }

        // ---- the single values ----

        private static void CleanGrace(RivalState state, List<string> problems)
        {
            if (state.graceStart == null) state.graceStart = string.Empty;
            if (state.graceStart.Length == 0 || IsDate(state.graceStart)) return;

            // a start that cannot be read would hold every reaction back for good (the planner counts 0 days); empty lets the lifecycle start it today
            Add(problems, "graceStart: '" + Shorten(state.graceStart) + "' is not a date yyyy-MM-dd, the grace period starts again today");
            state.graceStart = string.Empty;
        }

        private static void CleanSequence(RivalState state, List<string> problems)
        {
            int next = state.nextReactionSeq;
            if (next < 1) next = 1;
            if (next > RivalStateLimits.MaxSequence) next = RivalStateLimits.MaxSequence;

            // an id r-<n> is never used twice: the next number is above every number that is still in use
            int needed = Math.Min(HighestInternal(state) + 1, RivalStateLimits.MaxSequence);
            if (needed > next)
            {
                Add(problems, "nextReactionSeq: " + Text(next) + " is not above the reactions in the section, raised to " + Text(needed));
                next = needed;
            }
            state.nextReactionSeq = next;
        }

        private static int HighestInternal(RivalState state)
        {
            int highest = 0;
            foreach (PendingReaction item in Live(state.pending)) highest = Math.Max(highest, NumberOf(item.id));
            foreach (ActiveReaction item in Live(state.active)) highest = Math.Max(highest, NumberOf(item.id));
            foreach (PriceBookEntry item in Live(state.priceBook)) highest = Math.Max(highest, NumberOf(item.reactionId));
            return highest;
        }

        private static int NumberOf(string reactionId)
        {
            if (!PriceCutMath.IsInternalId(reactionId)) return 0;
            int number;
            string digits = reactionId.Substring(PriceCutMath.InternalPrefix.Length);
            return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out number) ? number : 0;
        }

        // ---- the characters and the attitudes ----

        private static void CleanAssignments(RivalState state, List<string> problems)
        {
            var tally = new Tally(problems, "assignments");
            var seen = new HashSet<int>();
            var kept = new List<AssignmentEntry>();
            foreach (AssignmentEntry entry in Live(state.assignments))
            {
                if (entry.uid < 0) tally.Drop("no company");
                else if (string.IsNullOrEmpty(entry.archetype)) tally.Drop("no archetype");
                else if (!seen.Add(entry.uid)) tally.Drop("company listed twice");
                else if (kept.Count >= RivalStateLimits.MaxCompanies) tally.Drop("over the limit of " + Text(RivalStateLimits.MaxCompanies));
                else
                {
                    if (entry.archetype.Length > RivalStateLimits.MaxTextLength)
                    {
                        entry.archetype = entry.archetype.Substring(0, RivalStateLimits.MaxTextLength);
                        tally.Fix("archetype cut to " + Text(RivalStateLimits.MaxTextLength) + " characters");
                    }
                    kept.Add(entry);
                }
            }
            state.assignments = kept;
            tally.Flush();
        }

        private static void CleanRevealed(RivalState state, List<string> problems)
        {
            var tally = new Tally(problems, "revealed");
            var kept = new List<int>();
            if (state.revealed != null)
            {
                foreach (int uid in state.revealed)
                {
                    if (uid < 0) tally.Drop("no company");
                    else if (kept.Contains(uid)) tally.Drop("company listed twice");
                    else if (kept.Count >= RivalStateLimits.MaxRevealed) tally.Drop("over the limit of " + Text(RivalStateLimits.MaxRevealed));
                    else kept.Add(uid);
                }
            }
            state.revealed = kept;
            tally.Flush();
        }

        private static void CleanAttitudes(RivalState state, List<string> problems)
        {
            var tally = new Tally(problems, "attitudes");
            var seen = new HashSet<int>();
            var kept = new List<AttitudeRecord>();
            foreach (AttitudeRecord record in Live(state.attitudes))
            {
                if (record.uid < 0) { tally.Drop("no company"); continue; }
                if (!seen.Add(record.uid)) { tally.Drop("company listed twice"); continue; }
                if (kept.Count >= RivalStateLimits.MaxCompanies) { tally.Drop("over the limit of " + Text(RivalStateLimits.MaxCompanies)); continue; }

                // NaN means "never set": the reader gives the baseline of the archetype. A number outside the scale is clipped, an infinity too.
                if (!float.IsNaN(record.value) && (record.value > RivalStateLimits.MaxAttitude || record.value < -RivalStateLimits.MaxAttitude))
                {
                    record.value = record.value > 0f ? RivalStateLimits.MaxAttitude : -RivalStateLimits.MaxAttitude;
                    tally.Fix("value clipped to the scale of -100 to 100");
                }

                record.history = AttitudeHistory(record.history, tally);
                kept.Add(record);
            }
            state.attitudes = kept;
            tally.Flush();
        }

        private static List<AttitudeEntry> AttitudeHistory(List<AttitudeEntry> source, Tally tally)
        {
            var kept = new List<AttitudeEntry>();
            foreach (AttitudeEntry entry in Live(source))
            {
                if (!IsDate(entry.date)) { tally.Drop("history line with an unreadable date"); continue; }
                if (float.IsNaN(entry.delta) || float.IsInfinity(entry.delta)) { entry.delta = 0f; tally.Fix("history delta that was not a number set to 0"); }
                else if (entry.delta > RivalStateLimits.MaxDelta || entry.delta < -RivalStateLimits.MaxDelta)
                {
                    entry.delta = entry.delta > 0f ? RivalStateLimits.MaxDelta : -RivalStateLimits.MaxDelta;
                    tally.Fix("history delta clipped");
                }
                entry.reason = Cut(entry.reason, AttitudeLedger.MaxReasonLength, tally);
                kept.Add(entry);
            }

            int over = kept.Count - RivalStateLimits.MaxAttitudeHistory;
            if (over > 0)
            {
                kept.RemoveRange(0, over);   // the oldest lines go
                tally.DropMany(over, "history longer than " + Text(RivalStateLimits.MaxAttitudeHistory) + " lines");
            }
            return kept;
        }

        // ---- the market ----

        private static void CleanShares(RivalState state, List<string> problems)
        {
            var tally = new Tally(problems, "shareSnapshot");
            ShareSnapshot snapshot = state.shareSnapshot ?? new ShareSnapshot();
            if (snapshot.monthIndex < 0) { snapshot.monthIndex = 0; tally.Fix("monthIndex below 0 set to 0"); }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var kept = new List<ShareRow>();
            foreach (ShareRow row in Live(snapshot.rows))
            {
                if (string.IsNullOrEmpty(row.key) || row.key.Length > RivalStateLimits.MaxIdLength) { tally.Drop("row without a usable key"); continue; }
                if (!seen.Add(row.key)) { tally.Drop("key listed twice"); continue; }
                if (kept.Count >= RivalStateLimits.MaxShareRows) { tally.Drop("over the limit of " + Text(RivalStateLimits.MaxShareRows)); continue; }

                var shares = new float[4];
                if (row.s != null) Array.Copy(row.s, shares, Math.Min(row.s.Length, shares.Length));
                for (int i = 0; i < shares.Length; i++)
                {
                    float share = shares[i];
                    if (float.IsNaN(share) || share < 0f) { shares[i] = 0f; tally.Fix("share outside 0 to 1"); }
                    else if (share > 1f) { shares[i] = 1f; tally.Fix("share outside 0 to 1"); }
                }
                row.s = shares;
                kept.Add(row);
            }
            snapshot.rows = kept;
            state.shareSnapshot = snapshot;
            tally.Flush();
        }

        private static void CleanCooldowns(RivalState state, List<string> problems)
        {
            var tally = new Tally(problems, "undercutCooldowns");
            var kept = new List<UndercutCooldown>();
            foreach (UndercutCooldown entry in Live(state.undercutCooldowns))
            {
                if (entry.uid < 0) { tally.Drop("no company"); continue; }
                if (entry.segment < 0 || entry.segment > 3) { tally.Drop("segment outside 0 to 3"); continue; }
                if (!IsDate(entry.until)) { tally.Drop("unreadable date"); continue; }

                UndercutCooldown same = null;
                foreach (UndercutCooldown other in kept)
                {
                    if (other.uid == entry.uid && other.segment == entry.segment) { same = other; break; }
                }
                if (same != null)
                {
                    // one pair, the later end wins
                    if (string.CompareOrdinal(entry.until, same.until) > 0) same.until = entry.until;
                    tally.Drop("pair listed twice");
                    continue;
                }
                if (kept.Count >= RivalStateLimits.MaxCooldowns) { tally.Drop("over the limit of " + Text(RivalStateLimits.MaxCooldowns)); continue; }
                kept.Add(entry);
            }
            state.undercutCooldowns = kept;
            tally.Flush();
        }

        // ---- the reactions ----

        private static void CleanPending(RivalState state, List<string> problems)
        {
            var tally = new Tally(problems, "pending");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var kept = new List<PendingReaction>();
            foreach (PendingReaction entry in Live(state.pending))
            {
                if (!UsableId(entry.id)) { tally.Drop("no usable id"); continue; }
                if (entry.uid < 0) { tally.Drop("no company"); continue; }
                if (string.IsNullOrEmpty(entry.type)) { tally.Drop("no type"); continue; }
                if (!IsDate(entry.plannedOn)) { tally.Drop("unreadable date"); continue; }
                if (!string.IsNullOrEmpty(entry.fireOn) && !IsDate(entry.fireOn)) { tally.Drop("unreadable date"); continue; }   // an empty fireOn is "mail not delivered yet"
                if (!seen.Add(entry.id)) { tally.Drop("id used twice"); continue; }
                if (kept.Count >= RivalStateLimits.MaxPending) { tally.Drop("over the limit of " + Text(RivalStateLimits.MaxPending)); continue; }

                entry.type = Cut(entry.type, RivalStateLimits.MaxTextLength, tally);
                entry.trigger = Cut(entry.trigger, RivalStateLimits.MaxTextLength, tally);
                entry.segment = ClipSegment(entry.segment, tally);
                if (entry.fireOn == null) entry.fireOn = string.Empty;
                if (entry.mailId < 0) { entry.mailId = 0; tally.Fix("mailId below 0 set to 0"); }
                if (entry.telegraphDays < 0) { entry.telegraphDays = 0; tally.Fix("telegraphDays clipped"); }
                else if (entry.telegraphDays > RivalStateLimits.MaxTelegraphDays) { entry.telegraphDays = RivalStateLimits.MaxTelegraphDays; tally.Fix("telegraphDays clipped"); }
                kept.Add(entry);
            }
            state.pending = kept;
            tally.Flush();
        }

        private static void CleanActive(RivalState state, List<string> problems)
        {
            var tally = new Tally(problems, "active");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var kept = new List<ActiveReaction>();
            foreach (ActiveReaction entry in Live(state.active))
            {
                if (!UsableId(entry.id)) { tally.Drop("no usable id"); continue; }
                if (entry.uid < 0) { tally.Drop("no company"); continue; }
                if (string.IsNullOrEmpty(entry.type)) { tally.Drop("no type"); continue; }
                if (!IsDate(entry.end)) { tally.Drop("unreadable date"); continue; }
                if (!string.IsNullOrEmpty(entry.start) && !IsDate(entry.start)) { tally.Drop("unreadable date"); continue; }
                if (!seen.Add(entry.id)) { tally.Drop("id used twice"); continue; }
                if (kept.Count >= RivalStateLimits.MaxActive) { tally.Drop("over the limit of " + Text(RivalStateLimits.MaxActive)); continue; }

                entry.type = Cut(entry.type, RivalStateLimits.MaxTextLength, tally);
                entry.segment = ClipSegment(entry.segment, tally);
                if (entry.start == null) entry.start = string.Empty;
                kept.Add(entry);
            }
            state.active = kept;
            tally.Flush();
        }

        private static void CleanPriceBook(RivalState state, List<string> problems)
        {
            var tally = new Tally(problems, "priceBook");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var kept = new List<PriceBookEntry>();
            foreach (PriceBookEntry entry in Live(state.priceBook))
            {
                if (entry.cpuSaveId < 0) { tally.Drop("no CPU"); continue; }
                if (entry.uid < 0) { tally.Drop("no company"); continue; }
                if (!UsableId(entry.reactionId)) { tally.Drop("no usable id"); continue; }

                // The way back writes baseTargetPrice into the CPU: a number that is not a price must never get there.
                if (!UsablePrice(entry.baseTargetPrice) || !UsablePrice(entry.cutTarget)) { tally.Drop("price that is not a price"); continue; }
                if (!seen.Add(Text(entry.cpuSaveId) + "|" + entry.reactionId)) { tally.Drop("CPU and reaction listed twice"); continue; }
                if (kept.Count >= RivalStateLimits.MaxPriceBook) { tally.Drop("over the limit of " + Text(RivalStateLimits.MaxPriceBook)); continue; }

                // an unreadable day is tolerated by the book (it counts the cut from the day it looks), but the text must be a date or empty
                if (entry.startedOn == null) entry.startedOn = string.Empty;
                else if (entry.startedOn.Length > 0 && !IsDate(entry.startedOn)) { entry.startedOn = string.Empty; tally.Fix("startedOn that was not a date emptied"); }
                kept.Add(entry);
            }
            state.priceBook = kept;
            tally.Flush();
        }

        private static void CleanCautions(RivalState state, List<string> problems)
        {
            var tally = new Tally(problems, "cautions");
            var byFlaw = new Dictionary<long, CautionEntry>();
            var kept = new List<CautionEntry>();
            foreach (CautionEntry entry in Live(state.cautions))
            {
                if (entry.uid < 0) { tally.Drop("no company"); continue; }
                if (!IsDate(entry.until)) { tally.Drop("unreadable date"); continue; }

                long key = ((long)entry.uid << 32) | (uint)entry.flawId;
                CautionEntry same;
                if (byFlaw.TryGetValue(key, out same))
                {
                    if (string.CompareOrdinal(entry.until, same.until) > 0) same.until = entry.until;   // one record per flaw: the later end wins
                    tally.Drop("flaw listed twice");
                    continue;
                }
                byFlaw[key] = entry;
                kept.Add(entry);
            }

            // past the limit the ones that end first go, as the book does while the game runs (one sort, however long the damaged list is)
            int over = kept.Count - RivalStateLimits.MaxCautions;
            if (over > 0)
            {
                var order = new List<int>(kept.Count);
                for (int i = 0; i < kept.Count; i++) order.Add(i);
                order.Sort((a, b) =>
                {
                    int byEnd = string.CompareOrdinal(kept[a].until, kept[b].until);
                    return byEnd != 0 ? byEnd : a.CompareTo(b);
                });

                var gone = new HashSet<int>();
                for (int i = 0; i < over; i++) gone.Add(order[i]);
                var survivors = new List<CautionEntry>(RivalStateLimits.MaxCautions);
                for (int i = 0; i < kept.Count; i++)
                {
                    if (!gone.Contains(i)) survivors.Add(kept[i]);
                }
                kept = survivors;
                tally.DropMany(over, "over the limit of " + Text(RivalStateLimits.MaxCautions));
            }
            state.cautions = kept;
            tally.Flush();
        }

        private static void CleanCounters(RivalState state, List<string> problems)
        {
            var tally = new Tally(problems, "counters");
            Counters counters = state.counters ?? new Counters();

            if (counters.year < 0 || counters.year > 9999) { counters.year = 0; tally.Fix("year outside 0 to 9999 set to 0"); }
            if (counters.hostileExecutedThisYear < 0) { counters.hostileExecutedThisYear = 0; tally.Fix("hostileExecutedThisYear clipped"); }
            else if (counters.hostileExecutedThisYear > 1000) { counters.hostileExecutedThisYear = 1000; tally.Fix("hostileExecutedThisYear clipped"); }

            counters.lastByUid = Days(counters.lastByUid, tally);
            counters.lastPartnerByUid = Days(counters.lastPartnerByUid, tally);

            var seen = new HashSet<int>();
            var executed = new List<ExecutedCount>();
            foreach (ExecutedCount entry in Live(counters.executed))
            {
                if (entry.uid < 0) { tally.Drop("executed: no company"); continue; }
                if (!seen.Add(entry.uid)) { tally.Drop("executed: company listed twice"); continue; }
                if (executed.Count >= RivalStateLimits.MaxCompanies) { tally.Drop("executed: over the limit of " + Text(RivalStateLimits.MaxCompanies)); continue; }
                if (entry.count < 0) { entry.count = 0; tally.Fix("executed count below 0 set to 0"); }
                else if (entry.count > RivalStateLimits.MaxCounter) { entry.count = RivalStateLimits.MaxCounter; tally.Fix("executed count clipped"); }
                executed.Add(entry);
            }
            counters.executed = executed;

            state.counters = counters;
            tally.Flush();
        }

        private static List<LastReaction> Days(List<LastReaction> source, Tally tally)
        {
            var seen = new HashSet<int>();
            var kept = new List<LastReaction>();
            foreach (LastReaction entry in Live(source))
            {
                if (entry.uid < 0) { tally.Drop("day of a reaction: no company"); continue; }
                if (!IsDate(entry.date)) { tally.Drop("day of a reaction: unreadable date"); continue; }
                if (!seen.Add(entry.uid)) { tally.Drop("day of a reaction: company listed twice"); continue; }
                if (kept.Count >= RivalStateLimits.MaxCompanies) { tally.Drop("day of a reaction: over the limit of " + Text(RivalStateLimits.MaxCompanies)); continue; }
                kept.Add(entry);
            }
            return kept;
        }

        private static void CleanHistory(RivalState state, List<string> problems)
        {
            var tally = new Tally(problems, "history");
            var kept = new List<HistoryEntry>();
            foreach (HistoryEntry entry in Live(state.history))
            {
                if (entry.uid < 0) { tally.Drop("no company"); continue; }
                if (!IsDate(entry.date)) { tally.Drop("unreadable date"); continue; }

                entry.type = Cut(entry.type ?? string.Empty, RivalStateLimits.MaxTextLength, tally);
                entry.outcome = Cut(entry.outcome ?? string.Empty, RivalStateLimits.MaxTextLength, tally);
                entry.segment = ClipSegment(entry.segment, tally);
                kept.Add(entry);
            }

            int over = kept.Count - RivalStateLimits.MaxHistory;
            if (over > 0)
            {
                kept.RemoveRange(0, over);   // the oldest lines go
                tally.DropMany(over, "over the limit of " + Text(RivalStateLimits.MaxHistory));
            }
            state.history = kept;
            tally.Flush();
        }

        private static void CleanDormant(RivalState state, List<string> problems)
        {
            var tally = new Tally(problems, "dormant");
            var kept = new List<int>();
            if (state.dormant != null)
            {
                foreach (int uid in state.dormant)
                {
                    if (uid < 0) tally.Drop("no company");
                    else if (kept.Contains(uid)) tally.Drop("company listed twice");
                    else if (kept.Count >= RivalStateLimits.MaxCompanies) tally.Drop("over the limit of " + Text(RivalStateLimits.MaxCompanies));
                    else kept.Add(uid);
                }
            }
            state.dormant = kept;
            tally.Flush();
        }

        // ---- the pieces ----

        /// <summary>A real segment is -1 (none) to 3 (<c>(int)Segment</c> of the four markets); anything else is "none".</summary>
        private static int ClipSegment(int segment, Tally tally)
        {
            if (segment >= -1 && segment <= 3) return segment;
            tally.Fix("segment outside -1 to 3 set to -1");
            return -1;
        }

        private static bool UsableId(string id)
        {
            return !string.IsNullOrEmpty(id) && id.Length <= RivalStateLimits.MaxIdLength;
        }

        private static bool UsablePrice(float price)
        {
            return PriceCutMath.IsUsablePrice(price) && price <= RivalStateLimits.MaxPrice;
        }

        /// <summary>The text is exactly <c>yyyy-MM-dd</c> and a real date. An empty text is not a date (the callers that allow empty test for it first).</summary>
        private static bool IsDate(string text)
        {
            DateTime date;
            return !string.IsNullOrEmpty(text) && RivalDates.TryParse(text, out date);
        }

        private static string Cut(string text, int length, Tally tally)
        {
            if (text == null) return string.Empty;
            if (text.Length <= length) return text;
            tally.Fix("text cut to " + Text(length) + " characters");
            return text.Substring(0, length);
        }

        private static string Shorten(string text)
        {
            if (text == null) return string.Empty;
            return text.Length <= 24 ? text : text.Substring(0, 24) + "...";
        }

        /// <summary>The records of a list without the null ones (a copy; the list itself may be null).</summary>
        private static List<T> Live<T>(List<T> list) where T : class
        {
            var live = new List<T>();
            if (list == null) return live;
            foreach (T item in list)
            {
                if (item != null) live.Add(item);
            }
            return live;
        }

        private static string Text(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static void Add(List<string> problems, string line)
        {
            if (problems != null) problems.Add(line);
        }

        /// <summary>What happened to one list: how many records went and for which reasons, how many values were corrected. Flushed as one line (two at most).</summary>
        private sealed class Tally
        {
            private readonly List<string> _problems;
            private readonly string _list;
            private readonly List<string> _dropReasons = new List<string>();
            private readonly List<string> _fixReasons = new List<string>();
            private int _dropped;
            private int _fixed;

            public Tally(List<string> problems, string list)
            {
                _problems = problems;
                _list = list;
            }

            public void Drop(string reason)
            {
                DropMany(1, reason);
            }

            public void DropMany(int count, string reason)
            {
                _dropped += count;
                if (!_dropReasons.Contains(reason)) _dropReasons.Add(reason);
            }

            public void Fix(string reason)
            {
                _fixed++;
                if (!_fixReasons.Contains(reason)) _fixReasons.Add(reason);
            }

            public void Flush()
            {
                if (_problems == null) return;
                if (_dropped > 0) _problems.Add(_list + ": " + Text(_dropped) + " record(s) dropped (" + string.Join("; ", _dropReasons.ToArray()) + ")");
                if (_fixed > 0) _problems.Add(_list + ": " + Text(_fixed) + " value(s) corrected (" + string.Join("; ", _fixReasons.ToArray()) + ")");
            }
        }
    }
}
