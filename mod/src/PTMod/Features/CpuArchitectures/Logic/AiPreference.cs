using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// What one AI company likes (D10, FR-7): a record of <c>ai-preferences.json</c>. A historical company is found by <see cref="UniqueId"/> (the
    /// <c>AICompany.UniqueID</c>, the index in <c>CompanySpawner.historicalCompanies</c>), every other company gets the record marked
    /// <see cref="IsDefault"/>. The displayed name of a company never matters here.
    /// </summary>
    public sealed class AiPreference : IContentRecord
    {
        public const int MaxUniqueId = 99;
        public const int MaxLagYears = 10;
        public const float MaxWeight = 3f;

        /// <summary>The slug of the record (<c>intel</c>, <c>texas-instruments</c>, <c>default</c>), the key of an override.</summary>
        public string Id { get; set; }

        /// <summary>The same slug; the name <c>Key</c> of design.md.</summary>
        public string Key
        {
            get { return Id; }
        }

        /// <summary>The <c>AICompany.UniqueID</c> of the company; -1 for the default record.</summary>
        public int UniqueId = -1;

        /// <summary>The record for every company without a record of its own; has no <see cref="UniqueId"/>.</summary>
        public bool IsDefault;

        /// <summary>How many years after its appearance the company takes up a family.</summary>
        public int LagYears;

        /// <summary>The liking for a family: 0 never, 1 usual, up to 3. A family without an entry counts as 0.</summary>
        public Dictionary<string, float> Weights = new Dictionary<string, float>(StringComparer.Ordinal);

        /// <summary>The weight of a family, 0 when the record has no entry for it.</summary>
        public float WeightOf(string familyId)
        {
            float weight;
            return familyId != null && Weights.TryGetValue(familyId, out weight) ? weight : 0f;
        }

        /// <summary>Reads one record of <c>ai-preferences.json</c>; null (with the reason in <paramref name="errors"/>) for a member of the wrong kind or a missing one.</summary>
        public static AiPreference Map(JsonObject json, List<string> errors)
        {
            var reader = new ArchJsonReader(json, errors);
            var preference = new AiPreference();

            preference.Id = reader.String("id", true);
            preference.IsDefault = reader.Bool("default", false);
            bool hasUniqueId = reader.Has("uniqueId");
            if (hasUniqueId) preference.UniqueId = reader.Int("uniqueId", false, -1);
            preference.LagYears = reader.Int("lagYears", false, 0);

            if (preference.IsDefault && hasUniqueId) reader.Fail("uniqueId", "the default record must not have a uniqueId");
            if (!preference.IsDefault && !hasUniqueId) reader.Fail("uniqueId", "is required (or mark the record \"default\": true)");

            JsonObject weights = reader.RawObject("weights", true);
            if (weights != null)
            {
                foreach (KeyValuePair<string, JsonNode> entry in weights)
                {
                    float weight;
                    if (!ArchJsonReader.TryFloat(entry.Value, out weight)) reader.Fail("weights." + entry.Key, "expected a number");
                    else preference.Weights[entry.Key] = weight;
                }
            }

            return reader.Failed ? null : preference;
        }

        /// <summary>Checks the values of a record (ranges of the id, the lag, the weights). False rejects the record; the messages say why.</summary>
        public static bool ValidateRecord(AiPreference preference, List<string> messages)
        {
            if (messages == null) messages = new List<string>();
            if (preference == null)
            {
                messages.Add("the record is null");
                return false;
            }

            int before = messages.Count;
            if (!ArchIds.IsRecordSlug(preference.Id)) messages.Add("id: must be 1-32 characters out of a-z, 0-9, _ and - (got '" + preference.Id + "')");
            if (!preference.IsDefault && (preference.UniqueId < 0 || preference.UniqueId > MaxUniqueId))
                messages.Add("uniqueId: " + preference.UniqueId.ToString(CultureInfo.InvariantCulture) + " is outside 0.." + MaxUniqueId.ToString(CultureInfo.InvariantCulture));
            if (preference.LagYears < 0 || preference.LagYears > MaxLagYears)
                messages.Add("lagYears: " + preference.LagYears.ToString(CultureInfo.InvariantCulture) + " is outside 0.." + MaxLagYears.ToString(CultureInfo.InvariantCulture));

            foreach (KeyValuePair<string, float> entry in preference.Weights)
            {
                if (!ArchIds.IsFamilyId(entry.Key)) messages.Add("weights: '" + entry.Key + "' is not a family id");
                else if (float.IsNaN(entry.Value) || float.IsInfinity(entry.Value) || entry.Value < 0f || entry.Value > MaxWeight)
                    messages.Add("weights." + entry.Key + ": " + entry.Value.ToString("R", CultureInfo.InvariantCulture) + " is outside 0.." + MaxWeight.ToString("R", CultureInfo.InvariantCulture));
            }
            return messages.Count == before;
        }

        public override string ToString()
        {
            return "AiPreference(" + Id + ")";
        }
    }

    /// <summary>
    /// The preferences of all AI companies after the checks across records (FR-1, design.md "ai-preferences.json"): unique <c>uniqueId</c>s, exactly
    /// one default record, no weights for families the catalog does not know. A broken list never leaves the AI without a default: a built-in
    /// fallback record (proprietary only, lag 2 years) takes its place and the error says so.
    /// </summary>
    public sealed class AiPreferenceTable
    {
        private readonly List<AiPreference> _preferences = new List<AiPreference>();
        private readonly Dictionary<int, AiPreference> _byUniqueId = new Dictionary<int, AiPreference>();
        private readonly List<string> _errors = new List<string>();
        private readonly List<string> _warnings = new List<string>();

        private AiPreferenceTable()
        {
        }

        /// <summary>The records of the historical companies, in the order of the file.</summary>
        public IReadOnlyList<AiPreference> Preferences
        {
            get { return _preferences; }
        }

        /// <summary>The record for every other company; never null.</summary>
        public AiPreference Default { get; private set; }

        /// <summary>The file had exactly one usable default record; false when <see cref="Default"/> is the built-in fallback.</summary>
        public bool HadDefault { get; private set; }

        public IReadOnlyList<string> Errors
        {
            get { return _errors; }
        }

        public IReadOnlyList<string> Warnings
        {
            get { return _warnings; }
        }

        /// <summary>The record of the historical company with this <c>UniqueID</c>, or null (then <see cref="Default"/> applies).</summary>
        public AiPreference ByUniqueId(int uniqueId)
        {
            AiPreference preference;
            return _byUniqueId.TryGetValue(uniqueId, out preference) ? preference : null;
        }

        /// <summary>The record for a company: its own, otherwise the default. Never null.</summary>
        public AiPreference Match(int uniqueId)
        {
            return ByUniqueId(uniqueId) ?? Default;
        }

        /// <summary>
        /// Builds the table from the records that passed <see cref="AiPreference.ValidateRecord"/>. A repeated <c>id</c> or <c>uniqueId</c> and a second
        /// default record are errors (the first stays); a weight for a family that is not in <paramref name="catalog"/> is dropped with a warning
        /// (null catalog: no such check).
        /// </summary>
        public static AiPreferenceTable Build(IReadOnlyList<AiPreference> raw, ArchCatalog catalog)
        {
            var table = new AiPreferenceTable();
            var ids = new HashSet<string>(StringComparer.Ordinal);

            if (raw != null)
            {
                foreach (AiPreference preference in raw)
                {
                    if (preference == null) continue;

                    string id = preference.Id ?? "?";
                    if (!ids.Add(id))
                    {
                        table._errors.Add(id + ": duplicate id; the first record with it is kept");
                        continue;
                    }

                    if (preference.IsDefault)
                    {
                        if (table.Default != null)
                        {
                            table._errors.Add(id + ": a second default record; the first one (" + table.Default.Id + ") is kept");
                            continue;
                        }
                        table.Default = Prune(preference, catalog, table._warnings);
                        table.HadDefault = true;
                        continue;
                    }

                    if (table._byUniqueId.ContainsKey(preference.UniqueId))
                    {
                        table._errors.Add(id + ": uniqueId " + preference.UniqueId.ToString(CultureInfo.InvariantCulture) + " is already used by '"
                                          + table._byUniqueId[preference.UniqueId].Id + "'; this record is skipped");
                        continue;
                    }

                    AiPreference kept = Prune(preference, catalog, table._warnings);
                    table._byUniqueId[kept.UniqueId] = kept;
                    table._preferences.Add(kept);
                }
            }

            if (table.Default == null)
            {
                table._errors.Add("default: there is no usable default record; a built-in one (proprietary only) is used");
                table.Default = Fallback();
            }
            return table;
        }

        /// <summary>The record used when the file has no usable default: only the default family, two years of lag.</summary>
        public static AiPreference Fallback()
        {
            var preference = new AiPreference { Id = "default", IsDefault = true, LagYears = 2 };
            preference.Weights[ArchCatalog.DefaultId] = 1f;
            return preference;
        }

        private static AiPreference Prune(AiPreference source, ArchCatalog catalog, List<string> warnings)
        {
            if (catalog == null) return source;

            var copy = new AiPreference { Id = source.Id, UniqueId = source.UniqueId, IsDefault = source.IsDefault, LagYears = source.LagYears };
            foreach (KeyValuePair<string, float> entry in source.Weights)
            {
                if (catalog.Contains(entry.Key)) copy.Weights[entry.Key] = entry.Value;
                else warnings.Add(source.Id + ": the weight of the unknown family '" + entry.Key + "' is skipped");
            }
            return copy;
        }
    }
}
