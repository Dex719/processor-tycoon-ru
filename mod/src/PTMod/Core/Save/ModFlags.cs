using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Json;

namespace PTMod.Core
{
    /// <summary>
    /// The feature flags of a save (<c>ModSave.Flags</c>; the block <c>flags</c> of the section <c>mod-framework</c>, FR-5): small named integers
    /// a feature keeps per game without a section of its own - "the welcome mail was sent", "the last hub tab". A flag is addressed by the
    /// feature id and a key; a bool is stored as 0 or 1, and a flag nobody set reads as false / the fallback.
    /// <list type="bullet">
    /// <item>The values live in the save: they go to the side file with every save, are put back on load, start empty in a new game and are
    /// emptied on the way to the menu. Setting one raises no event and writes nothing by itself.</item>
    /// <item>The flags of a feature that is switched off or removed stay in the save; nobody is stopped from reading another feature's flag
    /// (conditions read them: the <c>flag</c> predicate of FR-16).</item>
    /// <item>A call with an empty feature id or key does nothing and reads as the fallback (a warning for a write). Nothing here throws.</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    public sealed class ModFlags
    {
        private const string CoreId = ModLog.CoreFeatureId;

        private readonly Dictionary<string, Dictionary<string, int>> _values =
            new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

        internal ModFlags()
        {
            Part = new FlagsPart(this);
        }

        /// <summary>The block <c>flags</c> of the section of the core (registered by <see cref="ModSave"/>).</summary>
        internal ICoreSectionPart Part { get; }

        /// <summary>Number of flags set, over all features (diagnostics and tests).</summary>
        internal int Count
        {
            get
            {
                int count = 0;
                foreach (Dictionary<string, int> keys in _values.Values) count += keys.Count;
                return count;
            }
        }

        /// <summary>True when the flag is set to anything but 0; an unset flag is false.</summary>
        public bool Get(string featureId, string key)
        {
            return GetInt(featureId, key, 0) != 0;
        }

        /// <summary>Sets a flag: <c>Set(id, "welcomeSent")</c> raises it, <c>Set(id, key, false)</c> lowers it (the key stays, with 0).</summary>
        public void Set(string featureId, string key, bool value = true)
        {
            SetInt(featureId, key, value ? 1 : 0);
        }

        /// <summary>The value of the flag, or <paramref name="fallback"/> when it was never set (a flag set to 0 reads 0, not the fallback).</summary>
        public int GetInt(string featureId, string key, int fallback = 0)
        {
            if (string.IsNullOrEmpty(featureId) || string.IsNullOrEmpty(key)) return fallback;

            Dictionary<string, int> keys;
            int value;
            return _values.TryGetValue(featureId, out keys) && keys.TryGetValue(key, out value) ? value : fallback;
        }

        public void SetInt(string featureId, string key, int value)
        {
            if (string.IsNullOrEmpty(featureId) || string.IsNullOrEmpty(key))
            {
                ModLog.Warn(string.IsNullOrEmpty(featureId) ? CoreId : featureId, "flags: a flag needs a feature id and a key, the call is ignored");
                return;
            }

            Dictionary<string, int> keys;
            if (!_values.TryGetValue(featureId, out keys))
            {
                keys = new Dictionary<string, int>(StringComparer.Ordinal);
                _values[featureId] = keys;
            }
            keys[key] = value;
        }

        internal void Clear()
        {
            _values.Clear();
        }

        /// <summary>
        /// The block as it is stored: <c>{ "feature": { "key": 1 } }</c>, features and keys in ordinal order (a stable text between saves);
        /// null when no flag is set.
        /// </summary>
        internal JsonNode ToJson()
        {
            if (_values.Count == 0) return null;

            var root = new JsonObject();
            foreach (string featureId in Sorted(_values.Keys))
            {
                Dictionary<string, int> keys = _values[featureId];
                if (keys.Count == 0) continue;

                var node = new JsonObject();
                foreach (string key in Sorted(keys.Keys)) node.Set(key, keys[key]);
                root.Set(featureId, node);
            }
            return root.Count == 0 ? null : root;
        }

        /// <summary>
        /// Replaces the flags with the stored block. What cannot be a flag is dropped and described in <paramref name="problems"/> (path first):
        /// a feature entry that is not an object, a value that is not a whole number (a JSON <c>true</c>/<c>false</c> is taken as 1/0).
        /// </summary>
        internal void FromJson(JsonNode block, List<string> problems)
        {
            _values.Clear();

            var root = block as JsonObject;
            if (root == null)
            {
                if (block != null && !block.IsNull) problems.Add("flags: expected an object, got " + block.Kind.ToString().ToLowerInvariant());
                return;
            }

            foreach (KeyValuePair<string, JsonNode> feature in root)
            {
                var keys = feature.Value as JsonObject;
                if (keys == null)
                {
                    problems.Add("flags." + feature.Key + ": expected an object, got " + feature.Value.Kind.ToString().ToLowerInvariant());
                    continue;
                }
                if (feature.Key.Length == 0)
                {
                    problems.Add("flags: an entry with an empty feature id is dropped");
                    continue;
                }

                foreach (KeyValuePair<string, JsonNode> flag in keys)
                {
                    int value;
                    if (!TryInt(flag.Value, out value))
                    {
                        problems.Add("flags." + feature.Key + "." + flag.Key + ": " + flag.Value + " is not a whole number, the flag is dropped");
                        continue;
                    }
                    if (flag.Key.Length == 0)
                    {
                        problems.Add("flags." + feature.Key + ": a flag with an empty key is dropped");
                        continue;
                    }
                    SetInt(feature.Key, flag.Key, value);
                }
            }
        }

        private static bool TryInt(JsonNode node, out int value)
        {
            value = 0;
            var scalar = node as JsonValue;
            if (scalar == null) return false;
            if (scalar.Kind == JsonKind.Bool)
            {
                value = scalar.BoolValue ? 1 : 0;
                return true;
            }
            return scalar.Kind == JsonKind.Number && scalar.TryGetInt32(out value);
        }

        private static List<string> Sorted(IEnumerable<string> keys)
        {
            var list = new List<string>(keys);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        /// <summary>The block <c>flags</c> as a part of the section of the core.</summary>
        private sealed class FlagsPart : ICoreSectionPart
        {
            private readonly ModFlags _owner;

            public FlagsPart(ModFlags owner)
            {
                _owner = owner;
            }

            public string Key
            {
                get { return CoreSection.FlagsKey; }
            }

            public void ResetToDefaults()
            {
                _owner.Clear();
            }

            public void Read(JsonNode block, int schema)
            {
                var problems = new List<string>();
                _owner.FromJson(block, problems);
                foreach (string problem in problems) ModLog.Warn(CoreId, "save: " + problem);
            }

            public JsonNode Write()
            {
                return _owner.ToJson();
            }
        }
    }
}
