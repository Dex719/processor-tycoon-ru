using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// The content hash of a record (FR-1 item 3): SHA-1 of the canonical JSON (members sorted by name at every level, no white space), cut to
    /// 8 hex digits. Two records that say the same thing in a different order or layout hash the same; the hash tells a changed record from the
    /// built-in one with the same id, which makes the scenario "custom" (its results stay out of the records of the profile).
    /// </summary>
    public static class ContentHasher
    {
        /// <summary>Number of hex digits of a hash.</summary>
        public const int HashLength = 8;

        /// <summary>The canonical text of a JSON tree: object members in ordinal order, arrays in their own order, compact.</summary>
        public static string Canonical(JsonNode node)
        {
            if (node == null) return "null";
            return ModJson.Write(Canonicalize(node), false);
        }

        /// <summary>The hash of one record, 8 lower-case hex digits; empty for null.</summary>
        public static string Hash(JsonNode node)
        {
            if (node == null) return string.Empty;
            byte[] data = new UTF8Encoding(false).GetBytes(Canonical(node));
            byte[] digest;
            using (SHA1 sha = SHA1.Create())
            {
                digest = sha.ComputeHash(data);
            }

            var sb = new StringBuilder(HashLength);
            for (int i = 0; i < digest.Length && sb.Length < HashLength; i++)
            {
                sb.Append(digest[i].ToString("x2"));
            }
            return sb.ToString(0, HashLength);
        }

        /// <summary>
        /// The hashes of the records of a record file (<c>{ "schema": 1, "records": [ … ] }</c>) by <c>id</c>. A file that cannot be read, a
        /// record without an id and a repeated id are skipped (the loader of the core reports them); never throws.
        /// </summary>
        public static Dictionary<string, string> HashRecords(string fileText)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(fileText)) return result;

            try
            {
                JsonNode root;
                JsonError error;
                if (!ModJson.TryParse(fileText, out root, out error)) return result;

                var obj = root as JsonObject;
                JsonArray records = obj == null ? null : obj.GetArray("records");
                if (records == null) return result;

                foreach (JsonNode item in records)
                {
                    var record = item as JsonObject;
                    string id;
                    if (record == null || !record.TryGetString("id", out id) || string.IsNullOrEmpty(id)) continue;
                    if (!result.ContainsKey(id)) result[id] = Hash(record);
                }
            }
            catch (Exception)
            {
                // Hashing is a convenience for the "custom" flag; whatever went wrong, the caller works with what was read.
            }
            return result;
        }

        /// <summary>
        /// The custom flag of a loaded record: true when the built-in file has no record with this id, or its hash differs
        /// (<paramref name="builtinHashes"/> from <see cref="HashRecords"/>).
        /// </summary>
        public static bool IsCustom(IDictionary<string, string> builtinHashes, string id, string hash)
        {
            if (builtinHashes == null || id == null) return true;
            string builtin;
            return !builtinHashes.TryGetValue(id, out builtin) || !string.Equals(builtin, hash, StringComparison.Ordinal);
        }

        private static JsonNode Canonicalize(JsonNode node)
        {
            var obj = node as JsonObject;
            if (obj != null)
            {
                var keys = new List<string>(obj.Keys);
                keys.Sort(StringComparer.Ordinal);
                var sorted = new JsonObject();
                foreach (string key in keys) sorted.Set(key, Canonicalize(obj[key]));
                return sorted;
            }

            var array = node as JsonArray;
            if (array != null)
            {
                var copy = new JsonArray(array.Count);
                foreach (JsonNode item in array) copy.Add(Canonicalize(item));
                return copy;
            }
            return node;
        }
    }
}
