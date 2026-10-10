using System;
using System.Collections;
using System.Collections.Generic;

namespace PTMod.Core.Logic.Json
{
    /// <summary>
    /// A JSON object: string keys (ordinal, case-sensitive) to nodes, in insertion order. Parsing keeps the order of
    /// the text, <see cref="Set"/> on an existing key keeps its position, a new key goes to the end, so a
    /// parse/modify/write cycle never reshuffles the fields of a file (and unknown fields survive untouched).
    /// Values are never C# null: null becomes <see cref="JsonValue.Null"/>.
    /// Small objects are searched linearly, larger ones (more than 8 members) get a hash index.
    /// </summary>
    public sealed class JsonObject : JsonNode, IEnumerable<KeyValuePair<string, JsonNode>>
    {
        private const int IndexThreshold = 8;
        private static readonly KeyValuePair<string, JsonNode>[] NoItems = new KeyValuePair<string, JsonNode>[0];

        private KeyValuePair<string, JsonNode>[] _items = NoItems;
        private int _count;
        private Dictionary<string, int> _index;   // key -> position; only when _count > IndexThreshold
        private int _version;                     // bumped by structural changes, makes enumerators fail fast

        public JsonObject()
        {
        }

        public override JsonKind Kind => JsonKind.Object;

        public int Count => _count;

        /// <summary>
        /// The member with this key, or null when there is none (a missing member and a JSON null member differ:
        /// the second one is a <see cref="JsonValue.Null"/> node). The setter is <see cref="Set"/>.
        /// </summary>
        public JsonNode this[string key]
        {
            get
            {
                int i = IndexOf(key);
                return i < 0 ? null : _items[i].Value;
            }
            set { Set(key, value); }
        }

        public bool ContainsKey(string key)
        {
            return IndexOf(key) >= 0;
        }

        public bool TryGetValue(string key, out JsonNode value)
        {
            int i = IndexOf(key);
            value = i < 0 ? null : _items[i].Value;
            return i >= 0;
        }

        /// <summary>Sets a member: replaces the value of an existing key in place, appends a new key. Returns this object for chaining.</summary>
        public JsonObject Set(string key, JsonNode value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            JsonNode node = value ?? JsonValue.Null;

            int i = IndexOf(key);
            if (i >= 0)
            {
                _items[i] = new KeyValuePair<string, JsonNode>(_items[i].Key, node);
                return this;
            }

            Append(key, node);
            return this;
        }

        /// <summary>Like <see cref="Set"/> but throws <see cref="ArgumentException"/> when the key exists (collection initializer: new JsonObject { { "a", 1 } }).</summary>
        public JsonObject Add(string key, JsonNode value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (IndexOf(key) >= 0) throw new ArgumentException("The key already exists: " + key, nameof(key));
            Append(key, value ?? JsonValue.Null);
            return this;
        }

        public bool Remove(string key)
        {
            int i = IndexOf(key);
            if (i < 0) return false;

            _count--;
            if (i < _count) Array.Copy(_items, i + 1, _items, i, _count - i);
            _items[_count] = default(KeyValuePair<string, JsonNode>);
            _version++;

            if (_index != null)
            {
                if (_count > IndexThreshold) BuildIndex();
                else _index = null;
            }
            return true;
        }

        public void Clear()
        {
            Array.Clear(_items, 0, _count);
            _count = 0;
            _index = null;
            _version++;
        }

        /// <summary>The keys in member order. The sequence fails if the object changes structurally while it is enumerated.</summary>
        public IEnumerable<string> Keys
        {
            get
            {
                int version = _version;
                for (int i = 0; ; i++)
                {
                    if (version != _version) throw ModifiedDuringEnumeration();
                    if (i >= _count) yield break;
                    yield return _items[i].Key;
                }
            }
        }

        /// <summary>The values in member order.</summary>
        public IEnumerable<JsonNode> Values
        {
            get
            {
                int version = _version;
                for (int i = 0; ; i++)
                {
                    if (version != _version) throw ModifiedDuringEnumeration();
                    if (i >= _count) yield break;
                    yield return _items[i].Value;
                }
            }
        }

        public IEnumerator<KeyValuePair<string, JsonNode>> GetEnumerator()
        {
            int version = _version;
            for (int i = 0; ; i++)
            {
                if (version != _version) throw ModifiedDuringEnumeration();
                if (i >= _count) yield break;
                yield return _items[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        // ---- typed reads: a missing member or a member of another kind gives false / the default ----

        public bool TryGetString(string key, out string value)
        {
            var node = this[key] as JsonValue;
            if (node != null && node.Kind == JsonKind.String)
            {
                value = node.StringValue;
                return true;
            }
            value = null;
            return false;
        }

        public string GetString(string key, string defaultValue = null)
        {
            string value;
            return TryGetString(key, out value) ? value : defaultValue;
        }

        public bool TryGetBool(string key, out bool value)
        {
            var node = this[key] as JsonValue;
            if (node != null && node.Kind == JsonKind.Bool)
            {
                value = node.BoolValue;
                return true;
            }
            value = false;
            return false;
        }

        public bool GetBool(string key, bool defaultValue = false)
        {
            bool value;
            return TryGetBool(key, out value) ? value : defaultValue;
        }

        public bool TryGetDouble(string key, out double value)
        {
            var node = this[key] as JsonValue;
            if (node != null && node.Kind == JsonKind.Number)
            {
                value = node.NumberValue;
                return true;
            }
            value = 0;
            return false;
        }

        public double GetDouble(string key, double defaultValue = 0)
        {
            double value;
            return TryGetDouble(key, out value) ? value : defaultValue;
        }

        /// <summary>A number without fractional part inside the Int32 range; anything else (including 1.5) is "not an int".</summary>
        public bool TryGetInt32(string key, out int value)
        {
            var node = this[key] as JsonValue;
            if (node != null && node.TryGetInt32(out value)) return true;
            value = 0;
            return false;
        }

        public int GetInt32(string key, int defaultValue = 0)
        {
            int value;
            return TryGetInt32(key, out value) ? value : defaultValue;
        }

        public bool TryGetInt64(string key, out long value)
        {
            var node = this[key] as JsonValue;
            if (node != null && node.TryGetInt64(out value)) return true;
            value = 0;
            return false;
        }

        public long GetInt64(string key, long defaultValue = 0)
        {
            long value;
            return TryGetInt64(key, out value) ? value : defaultValue;
        }

        /// <summary>The member as an object, or null when it is missing or not an object.</summary>
        public JsonObject GetObject(string key)
        {
            return this[key] as JsonObject;
        }

        /// <summary>The member as an array, or null when it is missing or not an array.</summary>
        public JsonArray GetArray(string key)
        {
            return this[key] as JsonArray;
        }

        // ---- parser support ----

        /// <summary>Appends a member unless the key already exists (the parser treats a duplicate key as an error).</summary>
        internal bool TryAppend(string key, JsonNode value)
        {
            if (IndexOf(key) >= 0) return false;
            Append(key, value);
            return true;
        }

        // ---- node contract ----

        internal override JsonNode CloneCore(int depth)
        {
            CheckDepth(depth + 1);
            var copy = new JsonObject();
            if (_count > 0) copy._items = new KeyValuePair<string, JsonNode>[_count];
            for (int i = 0; i < _count; i++)
                copy.Append(_items[i].Key, _items[i].Value.CloneCore(depth + 1));
            return copy;
        }

        internal override bool EqualsCore(JsonNode other, int depth)
        {
            var obj = other as JsonObject;
            if (obj == null || obj._count != _count) return false;
            if (depth + 1 > ModJson.MaxDepth) return false;

            for (int i = 0; i < _count; i++)
            {
                int j = obj.IndexOf(_items[i].Key);
                if (j < 0 || !_items[i].Value.EqualsCore(obj._items[j].Value, depth + 1)) return false;
            }
            return true;
        }

        internal override int HashCore(int depth)
        {
            if (depth + 1 > ModJson.MaxDepth) return 0;
            int hash = 31 * _count;
            for (int i = 0; i < _count; i++)
            {
                // Order-independent (addition), like the equality.
                hash += _items[i].Key.GetHashCode() * 397 ^ _items[i].Value.HashCore(depth + 1);
            }
            return hash;
        }

        // ---- storage ----

        private int IndexOf(string key)
        {
            if (key == null) return -1;
            if (_index != null)
            {
                int found;
                return _index.TryGetValue(key, out found) ? found : -1;
            }
            for (int i = 0; i < _count; i++)
            {
                if (string.Equals(_items[i].Key, key, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        private void Append(string key, JsonNode value)
        {
            if (_count == _items.Length) Array.Resize(ref _items, Math.Max(4, _count * 2));
            _items[_count] = new KeyValuePair<string, JsonNode>(key, value);
            if (_index != null) _index[key] = _count;
            _count++;
            _version++;
            if (_index == null && _count > IndexThreshold) BuildIndex();
        }

        private void BuildIndex()
        {
            _index = new Dictionary<string, int>(_count * 2, StringComparer.Ordinal);
            for (int i = 0; i < _count; i++) _index[_items[i].Key] = i;
        }

        private static InvalidOperationException ModifiedDuringEnumeration()
        {
            return new InvalidOperationException("The JSON object was modified while it was being enumerated");
        }
    }
}
