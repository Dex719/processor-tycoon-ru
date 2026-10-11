using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// <see cref="IPackContentSource"/> over <c>ModContent</c> (decision 0006): the built-in file embedded in <c>PTMod.dll</c> as
    /// <c>content/&lt;folder&gt;/&lt;file&gt;.json</c>, with the player's override from <c>&lt;game&gt;/PTMod/content/&lt;folder&gt;/&lt;file&gt;.json</c> merged in
    /// (AC-1.3). A thin adapter: the merge, the schema check and the per-record skipping are the core's.
    /// </summary>
    internal sealed class CoreContentSource : IPackContentSource
    {
        /// <summary>
        /// The merged <c>config.json</c> of the pack, or null when the pack ships none. A pack without a config is normal, so the existence of the built-in
        /// resource is checked first: <c>ModContent.LoadConfig</c> would log an error for a missing file.
        /// </summary>
        public JsonObject LoadConfig(string folder, string file, int schema)
        {
            string resource = ContentMerger.BuiltinResourceName(folder, file);
            if (resource == null || !HasResource(resource)) return null;
            return ModContent.LoadConfig(folder, file, schema);
        }

        /// <summary>
        /// The records of the pack's events file. A pack whose built-in file is not there (yet) loads nothing with one warning instead of the error
        /// <c>ModContent</c> would log: an override needs the built-in file anyway.
        /// </summary>
        public IReadOnlyList<T> LoadRecords<T>(string folder, string file, Func<JsonObject, List<string>, T> map,
            Func<T, List<string>, bool> validate, int schema) where T : class, IContentRecord
        {
            string resource = ContentMerger.BuiltinResourceName(folder, file);
            if (resource != null && !HasResource(resource))
            {
                ModLog.Warn(EventSystemApi.FeatureId, "pack folder '" + folder + "' has no " + ContentMerger.NormalizeFile(file) + " yet; it defines no events");
                return new T[0];
            }
            return ModContent.LoadRecords<T>(folder, file, map, validate, schema);
        }

        private static bool HasResource(string logicalName)
        {
            foreach (string name in ModContent.ResourceNames(logicalName))
            {
                if (string.Equals(name, logicalName, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
