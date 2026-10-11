using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The names the feature uses on the channels of <c>ModModifiers</c> (design.md "Каналы ModModifiers"): the feature declares no channel of its own, it
    /// puts saved entries on two standard channels of the core. The reasons are keys of the L10n tables of the feature (<c>Explain</c> shows them).
    /// </summary>
    public static class FlawChannelNames
    {
        /// <summary>The rating of a CPU (patch P4 of the core): the software patch multiplies <c>Overall</c> of the CPU.</summary>
        public const string MarketRatingCpu = "market.rating.cpu";

        /// <summary>The chance that a client signs a contract with a CPU of the company (patch P8 of the core).</summary>
        public const string ContractSignChance = "contract.signChance";

        /// <summary>The reason of the entry of a patch (<c>Программная заплатка: {0}</c>, the argument is the name of the CPU).</summary>
        public const string ReasonPatch = "dfr.mod.patch";

        /// <summary>The reason of the entry that clients wait for the answer to a public flaw with.</summary>
        public const string ReasonPending = "dfr.mod.pending";

        /// <summary>The reason of the entry that an ignored public flaw leaves.</summary>
        public const string ReasonIgnore = "dfr.mod.ignore";

        /// <summary>The lowest multiplier the feature puts on a channel (the content validator keeps its numbers above it; the book enforces it again).</summary>
        public const float MinMultiplier = 0.5f;

        /// <summary>The highest multiplier: the feature never raises a channel (a patch does not make a CPU better).</summary>
        public const float MaxMultiplier = 1.0f;
    }

    /// <summary>An entry of <c>ModModifiers</c> of the feature that is in force now, as <c>Explain</c> reports it.</summary>
    public sealed class FlawLiveEntry
    {
        public FlawLiveEntry(string id, string channel, string reasonKey)
        {
            Id = id;
            Channel = channel;
            ReasonKey = reasonKey;
        }

        /// <summary>The id of the entry (<c>m-...</c>), the key of <c>ModModifiers.Remove</c>.</summary>
        public string Id { get; private set; }

        public string Channel { get; private set; }

        /// <summary>The reason the entry was made with (one of the <see cref="FlawChannelNames"/> reasons); may be <c>null</c>.</summary>
        public string ReasonKey { get; private set; }
    }

    /// <summary>
    /// What the channel book needs from <c>ModModifiers</c> (the game's implementation is <c>FlawChannels</c>; a test brings a fake). There is no
    /// <c>RemoveAll</c> here on purpose: switching the feature off must not take its entries away (NFR-2, D13), and the book cannot do it.
    /// </summary>
    public interface IFlawModifierGateway
    {
        /// <summary><c>ModModifiers.Add(feature, channel, scope, multiplier, durationDays, reasonKey, reasonArgs)</c>; the id of the entry, or <c>null</c> when the core refused it.</summary>
        string Add(string channel, ModifierScope scope, float multiplier, int durationDays, string reasonKey, string[] reasonArgs);

        /// <summary><c>ModModifiers.Remove</c>; <c>false</c> when there is no such entry any more.</summary>
        bool Remove(string entryId);

        /// <summary>
        /// The entries of the feature that apply to the company (<paramref name="cpuSaveId"/> is -1) or to the CPU of the company on the channel, as
        /// <c>ModModifiers.Explain</c> lists them. Empty when nothing applies or the question cannot be asked.
        /// </summary>
        IList<FlawLiveEntry> LiveEntries(string channel, string companyKey, int cpuSaveId);

        /// <summary>The name of the CPU for the text of the reason; <c>null</c> when it is not found.</summary>
        string CpuName(int cpuSaveId);
    }

    /// <summary>What <see cref="FlawChannelBook.Reconcile"/> found.</summary>
    public sealed class FlawChannelReport
    {
        /// <summary>The ids of the records that are still in force.</summary>
        public int Alive;

        /// <summary>The ids of entries that ended on their own (their term is over): forgotten without a warning.</summary>
        public int Ended;

        /// <summary>The ids of a patch that should still be running and are not there: a warning was written.</summary>
        public int Lost;

        /// <summary>The "waiting for the answer" entries that belonged to a record that no longer waits (or to none): removed.</summary>
        public int StaleRemoved;

        /// <summary>The lines written as warnings.</summary>
        public readonly List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// The records of the feature in the channels of <c>ModModifiers</c> (FR-5 item 5, FR-8, FR-9; design.md Component 11). It makes the entries, keeps their
    /// ids in <c>FlawRecordDto.modifierEntryIds</c> (so they survive a save: the core saves the entries themselves) and takes them away by those ids.
    /// <list type="bullet">
    /// <item><b>Patch:</b> <c>market.rating.cpu</c>, <c>ModifierScope.Cpu</c>, <c>1 - ipsLoss</c>, reason <c>dfr.mod.patch</c>.</item>
    /// <item><b>Waiting for the answer / ignore:</b> <c>contract.signChance</c>, <c>ModifierScope.Company</c>, reasons <c>dfr.mod.pending</c> and <c>dfr.mod.ignore</c>.</item>
    /// <item><b>Never</b> removes everything the feature owns: a switch-off keeps the entries, a switch-on does not make them again (D13).</item>
    /// </list>
    /// Pure: the core is behind <see cref="IFlawModifierGateway"/>. Main thread only.
    /// </summary>
    public sealed class FlawChannelBook
    {
        /// <summary>The "no CPU" value of <see cref="IFlawModifierGateway.LiveEntries"/>.</summary>
        public const int NoCpu = -1;

        private readonly FlawStore _store;
        private readonly IFlawModifierGateway _gateway;
        private readonly Action<string> _warn;
        private readonly Dictionary<string, string> _reasons = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _channels = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <param name="store">The records whose lists of entry ids the book keeps.</param>
        /// <param name="gateway">The core.</param>
        /// <param name="warn">Where the warnings go (the log of the feature); <c>null</c> - nowhere.</param>
        public FlawChannelBook(FlawStore store, IFlawModifierGateway gateway, Action<string> warn)
        {
            if (store == null) throw new ArgumentNullException("store");
            if (gateway == null) throw new ArgumentNullException("gateway");
            _store = store;
            _gateway = gateway;
            _warn = warn;
        }

        // ---- adding ----

        /// <summary>
        /// A patch on one CPU: <c>market.rating.cpu</c> times <paramref name="multiplier"/> (kept in [0.5, 1]) for <paramref name="durationDays"/> days, in the name
        /// of the record <paramref name="recordId"/>. Returns the id of the entry, which is also kept in the record; <c>null</c> when the record is not there or the
        /// core refused the entry.
        /// </summary>
        public string AddCpuRating(int cpuSaveId, float multiplier, int durationDays, int recordId)
        {
            FlawRecord record = _store.Find(recordId);
            if (record == null)
            {
                Warn("channels: no record #" + recordId + " for a rating entry of cpu " + cpuSaveId);
                return null;
            }

            string name = _gateway.CpuName(cpuSaveId) ?? string.Empty;
            string id = _gateway.Add(FlawChannelNames.MarketRatingCpu, ModifierScope.Cpu(cpuSaveId), Limit(multiplier), Days(durationDays),
                FlawChannelNames.ReasonPatch, new[] { name });
            return Keep(record, id, FlawChannelNames.MarketRatingCpu, FlawChannelNames.ReasonPatch, "rating entry of cpu " + cpuSaveId);
        }

        /// <summary>
        /// A change of the chance of a signed contract for the whole company: <c>contract.signChance</c> times <paramref name="multiplier"/> (kept in [0.5, 1]) for
        /// <paramref name="durationDays"/> days with the reason <paramref name="reasonKey"/> (<see cref="FlawChannelNames.ReasonPending"/> or
        /// <see cref="FlawChannelNames.ReasonIgnore"/>), in the name of the record <paramref name="recordId"/>. The id of the entry is kept in the record.
        /// </summary>
        public string AddSignChance(string companyKey, float multiplier, int durationDays, int recordId, string reasonKey)
        {
            FlawRecord record = _store.Find(recordId);
            if (record == null)
            {
                Warn("channels: no record #" + recordId + " for a sign-chance entry of " + companyKey);
                return null;
            }
            if (string.IsNullOrEmpty(companyKey))
            {
                Warn("channels: no company for a sign-chance entry of record #" + recordId);
                return null;
            }

            string id = _gateway.Add(FlawChannelNames.ContractSignChance, ModifierScope.Company(companyKey), Limit(multiplier), Days(durationDays),
                reasonKey, new string[0]);
            return Keep(record, id, FlawChannelNames.ContractSignChance, reasonKey, "sign-chance entry of " + companyKey);
        }

        private string Keep(FlawRecord record, string id, string channel, string reason, string what)
        {
            if (string.IsNullOrEmpty(id))
            {
                Warn("channels: the core refused the " + what + " of record #" + record.Id);
                return null;
            }

            Ids(record).Add(id);
            _reasons[id] = reason;
            _channels[id] = channel;
            return id;
        }

        private static float Limit(float multiplier)
        {
            if (float.IsNaN(multiplier)) return FlawChannelNames.MaxMultiplier;
            if (multiplier < FlawChannelNames.MinMultiplier) return FlawChannelNames.MinMultiplier;
            return multiplier > FlawChannelNames.MaxMultiplier ? FlawChannelNames.MaxMultiplier : multiplier;
        }

        private static int Days(int days)
        {
            return days < 1 ? 1 : days;
        }

        // ---- removing ----

        /// <summary>Takes one entry away by its id (and forgets it in the record that holds it). <c>false</c> when the core has no such entry any more.</summary>
        public bool Remove(string entryId)
        {
            if (string.IsNullOrEmpty(entryId)) return false;

            foreach (FlawRecord record in _store.All)
            {
                List<string> ids = Ids(record);
                if (ids.Remove(entryId)) break;
            }

            bool removed = SafeRemove(entryId);
            Forget(entryId);
            return removed;
        }

        /// <summary>Takes away every entry of the record (the owner went bankrupt, the revision that was paid for is over). Returns how many.</summary>
        public int RemoveAllOfRecord(FlawRecord record)
        {
            return RemoveEntries(record, id => true);
        }

        /// <summary>
        /// Takes away the entries made for the company only (<c>contract.signChance</c>: the waiting and the ignore), and keeps the patch of the CPU (FR-19:
        /// the CPU changes its owner, the company-wide consequences stay with the old one). Returns how many.
        /// </summary>
        public int RemoveCompanyEntries(FlawRecord record)
        {
            Learn(record);
            return RemoveEntries(record, id => string.Equals(ChannelOf(id), FlawChannelNames.ContractSignChance, StringComparison.Ordinal));
        }

        /// <summary>Takes away the "waiting for the answer" entry of the record (the decision is made). Returns how many.</summary>
        public int RemovePending(FlawRecord record)
        {
            Learn(record);
            return RemoveEntries(record, id => string.Equals(ReasonOf(id), FlawChannelNames.ReasonPending, StringComparison.Ordinal));
        }

        /// <summary>Takes away the entries of the patch (the revision that was paid for is finished). Returns how many.</summary>
        public int RemoveRatings(FlawRecord record)
        {
            Learn(record);
            return RemoveEntries(record, id => string.Equals(ChannelOf(id), FlawChannelNames.MarketRatingCpu, StringComparison.Ordinal));
        }

        /// <summary>The record has an entry that waits for the answer (<see cref="FlawChannelNames.ReasonPending"/>).</summary>
        public bool HasPending(FlawRecord record)
        {
            if (record == null) return false;

            Learn(record);
            foreach (string id in Ids(record))
            {
                if (string.Equals(ReasonOf(id), FlawChannelNames.ReasonPending, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private int RemoveEntries(FlawRecord record, Func<string, bool> pick)
        {
            if (record == null) return 0;

            List<string> ids = Ids(record);
            int removed = 0;
            for (int i = ids.Count - 1; i >= 0; i--)
            {
                string id = ids[i];
                if (!pick(id)) continue;

                ids.RemoveAt(i);
                SafeRemove(id);
                Forget(id);
                removed++;
            }
            return removed;
        }

        private bool SafeRemove(string id)
        {
            try
            {
                return _gateway.Remove(id);
            }
            catch (Exception e)
            {
                Warn("channels: removing entry " + id + " failed: " + e.Message);
                return false;
            }
        }

        private void Forget(string id)
        {
            _reasons.Remove(id);
            _channels.Remove(id);
        }

        private string ReasonOf(string id)
        {
            string reason;
            return _reasons.TryGetValue(id, out reason) ? reason : null;
        }

        private string ChannelOf(string id)
        {
            string channel;
            return _channels.TryGetValue(id, out channel) ? channel : null;
        }

        private static List<string> Ids(FlawRecord record)
        {
            if (record.Data.modifierEntryIds == null) record.Data.modifierEntryIds = new List<string>();
            return record.Data.modifierEntryIds;
        }

        /// <summary>
        /// Learns the channel and the reason of the ids of the record that the book did not make itself (they came from a save): one question to the core per
        /// company and per CPU. Does nothing when every id is known.
        /// </summary>
        private void Learn(FlawRecord record)
        {
            if (record == null) return;

            List<string> ids = Ids(record);
            bool unknown = false;
            foreach (string id in ids)
            {
                if (!_channels.ContainsKey(id))
                {
                    unknown = true;
                    break;
                }
            }
            if (!unknown) return;

            Absorb(_gateway.LiveEntries(FlawChannelNames.ContractSignChance, record.CompanyKey, NoCpu));
            if (record.Data.cpuIds == null) return;
            foreach (int cpuId in record.Data.cpuIds) Absorb(_gateway.LiveEntries(FlawChannelNames.MarketRatingCpu, record.CompanyKey, cpuId));
        }

        private void Absorb(IList<FlawLiveEntry> live)
        {
            if (live == null) return;

            foreach (FlawLiveEntry entry in live)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Id)) continue;
                _channels[entry.Id] = entry.Channel;
                _reasons[entry.Id] = entry.ReasonKey;
            }
        }

        // ---- after a load ----

        /// <summary>
        /// After a load (<c>GameReady(Loaded)</c>, FR-8, AC-8.2): compares the ids kept in the records with the entries the core really holds.
        /// <list type="bullet">
        /// <item>An id the core no longer has is dropped from the record; if it was the patch of a record that should still be patched (its term is not over) a
        /// warning is written, otherwise the entry has simply ended.</item>
        /// <item>An entry that waits for the answer to a flaw that does not wait any more (or that no record knows) is stale and is removed.</item>
        /// </list>
        /// </summary>
        /// <param name="today">Today as a day number (the term of a patch is the day it ends).</param>
        /// <param name="awaitsDecision">Whether a record still waits for the decision of its owner.</param>
        public FlawChannelReport Reconcile(int today, Func<FlawRecord, bool> awaitsDecision)
        {
            var report = new FlawChannelReport();
            var live = new Dictionary<string, FlawLiveEntry>(StringComparer.Ordinal);
            var companies = new HashSet<string>(StringComparer.Ordinal);

            foreach (FlawRecord record in _store.All)
            {
                if (!string.IsNullOrEmpty(record.CompanyKey)) companies.Add(record.CompanyKey);
            }
            foreach (string company in companies) Collect(live, _gateway.LiveEntries(FlawChannelNames.ContractSignChance, company, NoCpu));

            foreach (FlawRecord record in _store.All)
            {
                if (record.Data.cpuIds == null || !HasRatingTerm(record, today)) continue;
                foreach (int cpuId in record.Data.cpuIds) Collect(live, _gateway.LiveEntries(FlawChannelNames.MarketRatingCpu, record.CompanyKey, cpuId));
            }

            var owned = new HashSet<string>(StringComparer.Ordinal);
            foreach (FlawRecord record in _store.All)
            {
                List<string> ids = Ids(record);
                var kept = new List<string>();
                bool ratingAlive = false;

                foreach (string id in ids)
                {
                    FlawLiveEntry entry;
                    if (!live.TryGetValue(id, out entry))
                    {
                        report.Ended++;
                        Forget(id);
                        continue;
                    }

                    kept.Add(id);
                    owned.Add(id);
                    _channels[id] = entry.Channel;
                    _reasons[id] = entry.ReasonKey;
                    if (string.Equals(entry.Channel, FlawChannelNames.MarketRatingCpu, StringComparison.Ordinal)) ratingAlive = true;
                }

                if (HasRatingTerm(record, today) && !ratingAlive)
                {
                    report.Lost++;
                    string line = "channels: the patch entries of record #" + record.Id + " are lost (its term runs to day " + record.Data.mitigationEndDay
                                  + ", today is " + today + "); the CPUs are sold without the patch's effect";
                    report.Warnings.Add(line);
                    Warn(line);
                }

                ids.Clear();
                ids.AddRange(kept);
                report.Alive += kept.Count;

                bool waiting = awaitsDecision != null && awaitsDecision(record);
                if (!waiting) report.StaleRemoved += RemoveEntries(record, id => string.Equals(ReasonOf(id), FlawChannelNames.ReasonPending, StringComparison.Ordinal));
            }

            // A "waiting" entry that no record holds (the record was lost): nobody would ever take it away.
            foreach (KeyValuePair<string, FlawLiveEntry> pair in live)
            {
                if (owned.Contains(pair.Key)) continue;
                if (!string.Equals(pair.Value.ReasonKey, FlawChannelNames.ReasonPending, StringComparison.Ordinal)) continue;

                SafeRemove(pair.Key);
                Forget(pair.Key);
                report.StaleRemoved++;
            }
            return report;
        }

        /// <summary>The patch of the record should be running on <paramref name="today"/>: it is mitigated (or being replaced by a paid revision), it costs some performance and its term has not ended.</summary>
        private static bool HasRatingTerm(FlawRecord record, int today)
        {
            bool patched = record.State == FlawState.Mitigated || record.State == FlawState.Stepping;
            // A patch that costs nothing (ipsLoss 0) multiplies by 1, and Explain does not list a multiplier of 1: there is nothing to look for.
            return patched && record.Data.ipsLoss > 0f && record.Data.mitigationEndDay > today;
        }

        private static void Collect(Dictionary<string, FlawLiveEntry> into, IList<FlawLiveEntry> entries)
        {
            if (entries == null) return;

            foreach (FlawLiveEntry entry in entries)
            {
                if (entry != null && !string.IsNullOrEmpty(entry.Id)) into[entry.Id] = entry;
            }
        }

        private void Warn(string line)
        {
            if (_warn != null) _warn(line);
        }
    }
}
