using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Core
{
    /// <summary>
    /// A record of a content file (<c>{ "schema": 1, "records": [ ... ] }</c>) as <c>ModContent</c> sees it: only the identity.
    /// A feature wraps its own definition in a class with this interface; the <see cref="Id"/> is the key by which an override
    /// replaces or disables the built-in record (FR-10 item 3) and must equal the <c>id</c> member of the JSON record.
    /// </summary>
    public interface IContentRecord
    {
        string Id { get; }
    }

    /// <summary>
    /// What happened while a content file was loaded (FR-10; <c>ModContent.ReportOf</c>, the self-check and the Diagnostics tab read it).
    /// The counters describe the merge of the player's override into the built-in file:
    /// <see cref="Builtin"/> built-in records accepted, <see cref="Replaced"/> overridden by an override record with the same id,
    /// <see cref="Added"/> new ids from the override, <see cref="Removed"/> built-in records switched off by <c>"disabled": true</c>.
    /// A record that could not be used (not an object, no id, rejected by the mapper or the validation, duplicate id) is counted in
    /// <see cref="Skipped"/> and explained in <see cref="Errors"/>; the other records still load. Messages are English, they name the
    /// file, the origin (built-in or override), the record id and, when the feature says so, the field.
    /// </summary>
    public sealed class ContentReport
    {
        public int Builtin, Replaced, Added, Removed;

        /// <summary>Problems that need the author's or the player's attention (a skipped record, an ignored override). Empty when all is well.</summary>
        public List<string> Errors = new List<string>();

        /// <summary>Feature id (the content folder), as passed to the loader.</summary>
        public string FeatureId = string.Empty;

        /// <summary>File name with the extension (<c>samples.json</c>).</summary>
        public string File = string.Empty;

        /// <summary>Records that were rejected, from either file. Each one has an entry in <see cref="Errors"/>.</summary>
        public int Skipped;

        /// <summary>An override file was present (its text was given), whether or not it could be used.</summary>
        public bool OverrideFound;

        /// <summary>The override file was read and merged; false when absent or when it was ignored as a whole.</summary>
        public bool OverrideApplied;

        /// <summary>Notes that are not errors: a record disabled in the override that matches nothing, a disabled built-in record, soft messages of a validation that accepted the record.</summary>
        public List<string> Warnings = new List<string>();

        /// <summary>Ids of the built-in records replaced by the override, in override order. For a config file: the top-level fields that changed.</summary>
        public List<string> ReplacedIds = new List<string>();

        /// <summary>Ids that the override added. For a config file: the new top-level fields.</summary>
        public List<string> AddedIds = new List<string>();

        /// <summary>Ids of the built-in records the override removed.</summary>
        public List<string> RemovedIds = new List<string>();

        public ContentReport()
        {
        }

        public ContentReport(string featureId, string file)
        {
            FeatureId = featureId ?? string.Empty;
            File = file ?? string.Empty;
        }

        public bool HasErrors
        {
            get { return Errors.Count > 0; }
        }

        /// <summary>An override file existed but was thrown away as a whole (unknown schema, bad JSON, wrong shape).</summary>
        public bool OverrideIgnored
        {
            get { return OverrideFound && !OverrideApplied; }
        }

        /// <summary>One line for the log: "framework-sample/samples.json: built-in 3, replaced 1, added 0, removed 1, skipped 0, errors 0".</summary>
        public override string ToString()
        {
            return FeatureId + "/" + File + ": built-in " + Builtin.ToString(CultureInfo.InvariantCulture)
                   + ", replaced " + Replaced.ToString(CultureInfo.InvariantCulture)
                   + ", added " + Added.ToString(CultureInfo.InvariantCulture)
                   + ", removed " + Removed.ToString(CultureInfo.InvariantCulture)
                   + ", skipped " + Skipped.ToString(CultureInfo.InvariantCulture)
                   + ", errors " + Errors.Count.ToString(CultureInfo.InvariantCulture);
        }
    }
}
