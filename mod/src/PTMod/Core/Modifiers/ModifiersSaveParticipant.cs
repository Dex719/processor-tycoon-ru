using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Modifiers;
using PTMod.Core.Logic.Save;

namespace PTMod.Core
{
    /// <summary>What <see cref="ModifiersSaveCodec.Restore"/> did with a saved block.</summary>
    internal struct ModifiersRestoreReport
    {
        /// <summary>Entries put back into the registry.</summary>
        public int Restored;

        /// <summary>Entries whose end date had come: not restored, nothing to report (the game moved on).</summary>
        public int Ended;

        /// <summary>Entries that could not be used (a bad field, a bad scope or date, or the registry refused them); each is a warning.</summary>
        public int Dropped;
    }

    /// <summary>
    /// The saved form of the modifier entries and the way back (FR-12 item 7, <c>ModifierEntryDto</c> of the side file), without any game type and without
    /// the log: problems are returned as text. One entry is one object of the array <c>modifiers</c>:
    /// <c>{ "id": "m3", "feature": "market-shocks", "channel": "market.demand.lowend", "scope": "Global", "mult": 0.7, "expires": "1985-04-01",
    /// "reason": "ms.event.recession", "reasonArgs": ["..."] }</c>. <c>company</c> is written for the scope <c>Company</c>, <c>cpu</c> for <c>Cpu</c>,
    /// <c>segment</c> (the segment's number) when the scope has a segment; <c>expires</c>, <c>reason</c> and <c>reasonArgs</c> only when set.
    /// </summary>
    internal static class ModifiersSaveCodec
    {
        private const string DateFormat = "yyyy-MM-dd";

        /// <summary>The array to store, in the order the entries were added; null when there are no entries (the block is left out).</summary>
        internal static JsonArray Write(IReadOnlyList<ModifierEntry> entries)
        {
            if (entries == null || entries.Count == 0) return null;

            var array = new JsonArray(entries.Count);
            foreach (ModifierEntry entry in entries) array.Add(ToJson(entry));
            return array;
        }

        internal static JsonObject ToJson(ModifierEntry entry)
        {
            var item = new JsonObject();
            item.Set("id", entry.Id);
            item.Set("feature", entry.FeatureId);
            item.Set("channel", entry.Channel);
            item.Set("scope", entry.Scope.Kind.ToString());
            if (entry.Scope.Kind == ScopeKind.Company) item.Set("company", entry.Scope.CompanyKey);
            if (entry.Scope.Kind == ScopeKind.Cpu) item.Set("cpu", entry.Scope.CpuSaveId);
            if (entry.Scope.Segment != Segment.Any) item.Set("segment", (int)entry.Scope.Segment);
            item.Set("mult", entry.Multiplier);
            if (entry.ExpiresOn.HasValue) item.Set("expires", entry.ExpiresOn.Value.ToString(DateFormat, CultureInfo.InvariantCulture));
            if (entry.ReasonKey != null) item.Set("reason", entry.ReasonKey);
            if (entry.ReasonArgs.Length > 0)
            {
                var args = new JsonArray(entry.ReasonArgs.Length);
                foreach (string arg in entry.ReasonArgs) args.Add(arg ?? string.Empty);
                item.Set("reasonArgs", args);
            }
            return item;
        }

        /// <summary>
        /// Puts the saved entries into the registry (which the caller has emptied and moved to the game's day): each with its own id, scope,
        /// multiplier, end date and reason. An entry that cannot be used is dropped alone - a line in <paramref name="problems"/> says why - and the
        /// others are read on. The registry itself reports entries it refuses (<c>Warning</c>) and silently skips those that have ended.
        /// </summary>
        internal static ModifiersRestoreReport Restore(ChannelRegistry registry, JsonArray block, List<string> problems)
        {
            var report = new ModifiersRestoreReport();
            if (block == null) return report;

            for (int i = 0; i < block.Count; i++)
            {
                string where = "modifiers[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                JsonNode node = block[i];
                var item = node as JsonObject;
                if (item == null)
                {
                    problems.Add(where + ": expected an object, got " + node.Kind.ToString().ToLowerInvariant() + "; the entry is dropped");
                    report.Dropped++;
                    continue;
                }

                string id = item.GetString("id");
                if (id != null) where += " (" + id + ")";

                // mult has no usable default: a missing value would read as a valid multiplier of 0.
                var errors = new List<string>();
                ModifierEntryDto dto = ModJson.Bind<ModifierEntryDto>(item, errors);
                if (errors.Count > 0 || dto == null)
                {
                    problems.Add(where + ": " + (errors.Count > 0 ? errors[0] : "unreadable") + "; the entry is dropped");
                    report.Dropped++;
                    continue;
                }
                if (!item.ContainsKey("mult") || item["mult"].IsNull)
                {
                    problems.Add(where + ": 'mult' is missing; the entry is dropped");
                    report.Dropped++;
                    continue;
                }

                ModifierScope scope;
                string error;
                if (!ModifierScope.TryCreate(dto.scope, dto.company, dto.cpu, dto.segment, out scope, out error))
                {
                    problems.Add(where + ": " + error + "; the entry is dropped");
                    report.Dropped++;
                    continue;
                }

                DateTime? expires = null;
                if (dto.expires != null)
                {
                    DateTime date;
                    if (!DateTime.TryParseExact(dto.expires, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                    {
                        problems.Add(where + ": 'expires' is '" + dto.expires + "', not " + DateFormat + "; the entry is dropped");
                        report.Dropped++;
                        continue;
                    }
                    expires = date;
                }

                if (registry.Restore(dto.id, dto.feature, dto.channel, scope, dto.mult, expires, dto.reason, dto.reasonArgs) != null) report.Restored++;
                else if (expires.HasValue && expires.Value.Date <= registry.Today) report.Ended++;
                else report.Dropped++;
            }
            return report;
        }
    }

    /// <summary>
    /// The block <c>modifiers</c> of the section <c>mod-framework</c> of the side file (FR-12 item 7, FR-5): the entries of <see cref="ModModifiers"/>
    /// with their ids, scopes, end dates and reasons. The entries of features that are off, unknown or newer than this build are stored and restored
    /// like the others - they just do not apply (FR-12 item 8). Providers are code and are not saved: features register them again at start.
    /// <list type="bullet">
    /// <item><b>Reading</b> follows the order the engine needs: the registry is emptied, moved to the game's day (<see cref="ModModifiers.SyncToday"/>,
    /// so entries that ended while the game was not played are not put back), then every entry is restored. A bad entry costs that entry and
    /// leaves a warning; a block that is not an array is dropped with a warning (the file is rewritten at the next save).</item>
    /// <item><b>Reset</b> (a new game, leaving to the menu, a save without the block) empties the entries; declarations and providers stay.</item>
    /// <item><b>Writing</b> leaves the block out when there are no entries.</item>
    /// </list>
    /// </summary>
    internal sealed class ModifiersSaveParticipant : ICoreSectionPart
    {
        /// <summary>The one block, registered by <see cref="ModModifiers"/> at the Declare stage.</summary>
        internal static readonly ModifiersSaveParticipant Instance = new ModifiersSaveParticipant();

        private const string CoreId = ModLog.CoreFeatureId;
        private const int MaxReportedProblems = 10;

        private ModifiersSaveParticipant()
        {
        }

        public string Key
        {
            get { return CoreSection.ModifiersKey; }
        }

        public void ResetToDefaults()
        {
            ModModifiers.Registry.ClearEntries();
        }

        public void Read(JsonNode block, int schema)
        {
            ChannelRegistry registry = ModModifiers.Registry;
            registry.ClearEntries();
            ModModifiers.SyncToday();

            if (block == null || block.IsNull) return;

            var array = block as JsonArray;
            if (array == null)
            {
                ModLog.Warn(CoreId, "save: the block 'modifiers' is " + block.Kind.ToString().ToLowerInvariant() + ", not an array; no modifier entries are restored");
                return;
            }

            var problems = new List<string>();
            ModifiersRestoreReport report = ModifiersSaveCodec.Restore(registry, array, problems);

            for (int i = 0; i < problems.Count && i < MaxReportedProblems; i++) ModLog.Warn(CoreId, "save: " + problems[i]);
            if (problems.Count > MaxReportedProblems)
                ModLog.Warn(CoreId, "save: modifiers: " + (problems.Count - MaxReportedProblems) + " more problem(s) of the same kind are not listed");
            ModLog.Debug(CoreId, "save: modifiers: " + report.Restored + " entr" + (report.Restored == 1 ? "y" : "ies") + " restored, "
                                 + report.Ended + " ended, " + report.Dropped + " dropped");
        }

        public JsonNode Write()
        {
            return ModifiersSaveCodec.Write(ModModifiers.Registry.Entries);
        }
    }
}
