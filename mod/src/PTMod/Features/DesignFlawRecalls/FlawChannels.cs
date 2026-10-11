using System;
using System.Collections.Generic;
using PTMod.Core;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameDataFinder = ProcessorTycoon.DataFinder;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The entries of <c>ModModifiers</c> of the feature (FR-5 item 5, FR-8, FR-9, NFR-8; design.md Component 11): the game's side of the
    /// <see cref="FlawChannelBook"/>. The patch lowers the rating of a CPU (<c>market.rating.cpu</c>, <c>ModifierScope.Cpu</c>), the waiting for an answer and the
    /// ignoring of a flaw lower the chance of a signed contract of the company (<c>contract.signChance</c>, <c>ModifierScope.Company</c>). The core saves the
    /// entries and ends them by their term; the ids are kept in the records of the feature.
    /// <para>
    /// Switching the feature off takes nothing away: <c>ModModifiers.RemoveAll</c> is never called here or anywhere in the feature, the core keeps the entries
    /// and does not apply them while the feature is off; switching it on does not make them again, so the end dates stay (NFR-2, D13). The book has no way to
    /// call <c>RemoveAll</c> (the gateway does not offer it) and a test checks the sources for it.
    /// </para>
    /// </summary>
    internal sealed class FlawChannels
    {
        private readonly FlawChannelBook _book;

        public FlawChannels(FlawStore store)
        {
            _book = new FlawChannelBook(store, new ModModifiersGateway(), line => ModLog.Warn(DesignFlawRecallsFeature.FeatureId, line));
        }

        /// <summary>The pure book (the response engine and the dispatcher work on it).</summary>
        internal FlawChannelBook Book
        {
            get { return _book; }
        }

        /// <summary>A patch on one CPU: <c>market.rating.cpu</c> times <paramref name="multiplier"/> for <paramref name="durationDays"/> days. The id of the entry is kept in the record.</summary>
        internal string AddCpuRating(int cpuSaveId, float multiplier, int durationDays, int recordId)
        {
            return _book.AddCpuRating(cpuSaveId, multiplier, durationDays, recordId);
        }

        /// <summary><c>contract.signChance</c> of the company times <paramref name="multiplier"/>, with the reason <c>dfr.mod.pending</c> or <c>dfr.mod.ignore</c>.</summary>
        internal string AddSignChance(string companyKey, float multiplier, int durationDays, int recordId, string reasonKey)
        {
            return _book.AddSignChance(companyKey, multiplier, durationDays, recordId, reasonKey);
        }

        /// <summary><c>ModModifiers.Remove</c> by the id of the entry.</summary>
        internal void Remove(string entryId)
        {
            _book.Remove(entryId);
        }

        /// <summary>Every entry of the record (the owner went bankrupt, the CPU is gone).</summary>
        internal void RemoveAllOfRecord(FlawRecord record)
        {
            _book.RemoveAllOfRecord(record);
        }

        /// <summary>Only the entries made for the company (a CPU changed its owner, FR-19).</summary>
        internal void RemoveCompanyEntries(FlawRecord record)
        {
            _book.RemoveCompanyEntries(record);
        }

        /// <summary>
        /// After <c>GameReady(Loaded)</c>: compares the ids in the records with the entries the core really holds, warns about the lost patches, takes away the stale
        /// "waiting" entries.
        /// </summary>
        internal void Reconcile()
        {
            int today = FlawDays.FromDate(ModTicks.Today);
            if (today <= 0) return;

            FlawChannelReport report = _book.Reconcile(today, FlawResponseEngine.AwaitsDecision);
            if (report.Alive + report.Ended + report.Lost + report.StaleRemoved > 0)
            {
                ModLog.Info(DesignFlawRecallsFeature.FeatureId, "channels reconciled: " + report.Alive + " entries in force, " + report.Ended + " ended, "
                                                                 + report.Lost + " lost, " + report.StaleRemoved + " stale removed");
            }
        }

        /// <summary>The gateway to <c>ModModifiers</c>: only <c>Add</c>, <c>Remove</c> and <c>Explain</c>; no <c>RemoveAll</c>.</summary>
        private sealed class ModModifiersGateway : IFlawModifierGateway
        {
            public string Add(string channel, ModifierScope scope, float multiplier, int durationDays, string reasonKey, string[] reasonArgs)
            {
                return ModModifiers.Add(DesignFlawRecallsFeature.FeatureId, channel, scope, multiplier, durationDays, reasonKey, reasonArgs);
            }

            public bool Remove(string entryId)
            {
                return ModModifiers.Remove(entryId);
            }

            public IList<FlawLiveEntry> LiveEntries(string channel, string companyKey, int cpuSaveId)
            {
                var live = new List<FlawLiveEntry>();

                ModifierContext context = ModifierContext.ForKey(companyKey);
                if (cpuSaveId >= 0)
                {
                    GameCpu cpu = GameDataFinder.FindCpu(cpuSaveId);
                    if (cpu == null) return live;
                    context.Cpu = cpu;
                }

                ModifierBreakdown breakdown = ModModifiers.Explain(channel, context);
                foreach (AppliedModifier applied in breakdown)
                {
                    if (applied.EntryId == null || !string.Equals(applied.FeatureId, DesignFlawRecallsFeature.FeatureId, StringComparison.Ordinal)) continue;
                    live.Add(new FlawLiveEntry(applied.EntryId, channel, applied.ReasonKey));
                }
                return live;
            }

            public string CpuName(int cpuSaveId)
            {
                string name = FlawText.CpuName(cpuSaveId);
                return string.IsNullOrEmpty(name) ? null : name;
            }
        }
    }
}
