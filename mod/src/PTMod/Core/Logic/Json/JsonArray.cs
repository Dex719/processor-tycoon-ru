using System;
using System.Collections;
using System.Collections.Generic;

namespace PTMod.Core.Logic.Json
{
    /// <summary>
    /// A JSON array: an ordered list of nodes. Elements are never C# null: null becomes <see cref="JsonValue.Null"/>.
    /// </summary>
    public sealed class JsonArray : JsonNode, IEnumerable<JsonNode>
    {
        private readonly List<JsonNode> _items;

        public JsonArray()
        {
            _items = new List<JsonNode>();
        }

        public JsonArray(int capacity)
        {
            _items = new List<JsonNode>(capacity);
        }

        public override JsonKind Kind => JsonKind.Array;

        public int Count => _items.Count;

        /// <summary>The element at the index; like List, an index out of range throws <see cref="ArgumentOutOfRangeException"/> (see <see cref="TryGetValue"/>).</summary>
        public JsonNode this[int index]
        {
            get { return _items[index]; }
            set { _items[index] = value ?? JsonValue.Null; }
        }

        public bool TryGetValue(int index, out JsonNode value)
        {
            if (index < 0 || index >= _items.Count)
            {
                value = null;
                return false;
            }
            value = _items[index];
            return true;
        }

        /// <summary>Appends an element. Returns this array for chaining (and works in a collection initializer).</summary>
        public JsonArray Add(JsonNode value)
        {
            _items.Add(value ?? JsonValue.Null);
            return this;
        }

        public JsonArray AddRange(IEnumerable<JsonNode> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            foreach (JsonNode value in values) _items.Add(value ?? JsonValue.Null);
            return this;
        }

        public JsonArray Insert(int index, JsonNode value)
        {
            _items.Insert(index, value ?? JsonValue.Null);
            return this;
        }

        public void RemoveAt(int index)
        {
            _items.RemoveAt(index);
        }

        public void Clear()
        {
            _items.Clear();
        }

        public IEnumerator<JsonNode> GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        // ---- node contract ----

        internal override JsonNode CloneCore(int depth)
        {
            CheckDepth(depth + 1);
            var copy = new JsonArray(_items.Count);
            for (int i = 0; i < _items.Count; i++) copy._items.Add(_items[i].CloneCore(depth + 1));
            return copy;
        }

        internal override bool EqualsCore(JsonNode other, int depth)
        {
            var array = other as JsonArray;
            if (array == null || array._items.Count != _items.Count) return false;
            if (depth + 1 > ModJson.MaxDepth) return false;

            for (int i = 0; i < _items.Count; i++)
            {
                if (!_items[i].EqualsCore(array._items[i], depth + 1)) return false;
            }
            return true;
        }

        internal override int HashCore(int depth)
        {
            if (depth + 1 > ModJson.MaxDepth) return 0;
            int hash = 17 + _items.Count;
            for (int i = 0; i < _items.Count; i++) hash = hash * 31 + _items[i].HashCore(depth + 1);
            return hash;
        }

        /// <summary>Parser support: appends without the null check (the parser never produces C# null).</summary>
        internal void AppendParsed(JsonNode value)
        {
            _items.Add(value);
        }
    }
}
