using System;

namespace PTMod.Core.Logic.Json
{
    /// <summary>The kind of a JSON node: the four scalar kinds (all of them are <see cref="JsonValue"/>) plus object and array.</summary>
    public enum JsonKind
    {
        Null = 0,
        Bool = 1,
        Number = 2,
        String = 3,
        Object = 4,
        Array = 5,
    }

    /// <summary>
    /// Base of the JSON tree built by <see cref="ModJson"/>. The hierarchy is closed: <see cref="JsonObject"/>,
    /// <see cref="JsonArray"/> and <see cref="JsonValue"/> are the only node types. A child of a container is never
    /// a C# null: JSON null is the shared <see cref="JsonValue.Null"/> node.
    /// Equality is structural (see <see cref="Equals(object)"/>), <see cref="ToString"/> gives compact JSON.
    /// No Unity or game types are used, so the same code runs in the game and in the unit tests.
    /// </summary>
    public abstract class JsonNode
    {
        internal JsonNode()
        {
        }

        public abstract JsonKind Kind { get; }

        public bool IsNull => Kind == JsonKind.Null;

        public bool IsObject => Kind == JsonKind.Object;

        public bool IsArray => Kind == JsonKind.Array;

        /// <summary>Scalar: null, bool, number or string.</summary>
        public bool IsScalar => Kind != JsonKind.Object && Kind != JsonKind.Array;

        /// <summary>
        /// Independent copy of the whole subtree (scalars are immutable and shared).
        /// Throws <see cref="InvalidOperationException"/> when the subtree is deeper than <see cref="ModJson.MaxDepth"/>
        /// (only possible for a hand-built, e.g. cyclic, tree: parsed trees obey the limit).
        /// </summary>
        public JsonNode DeepClone()
        {
            return CloneCore(0);
        }

        internal abstract JsonNode CloneCore(int depth);

        internal abstract bool EqualsCore(JsonNode other, int depth);

        internal abstract int HashCore(int depth);

        /// <summary>
        /// Structural equality: same kind and same content. Object members are compared regardless of order
        /// (JSON semantics; <see cref="JsonObject.Keys"/> exposes the order), numbers by value (-0 equals 0).
        /// </summary>
        public override bool Equals(object obj)
        {
            var other = obj as JsonNode;
            return other != null && (ReferenceEquals(this, other) || EqualsCore(other, 0));
        }

        public override int GetHashCode()
        {
            return HashCore(0);
        }

        /// <summary>Compact JSON text, for logs and debugging; never throws.</summary>
        public override string ToString()
        {
            try
            {
                return ModJson.Write(this, false);
            }
            catch (InvalidOperationException)
            {
                return "<JSON too deep>";
            }
        }

        /// <summary>Guards the recursion of Clone/Equals/Hash/Write against hand-built (possibly cyclic) trees.</summary>
        internal static void CheckDepth(int depth)
        {
            if (depth > ModJson.MaxDepth)
                throw new InvalidOperationException("JSON nesting is deeper than " + ModJson.MaxDepth + " levels");
        }

        // Implicit conversions make building trees short: obj.Set("year", 1985); arr.Add("text").
        // A null string becomes JSON null; NaN and infinities become JSON null (see JsonValue.Of(double)).

        public static implicit operator JsonNode(string value)
        {
            return JsonValue.Of(value);
        }

        public static implicit operator JsonNode(bool value)
        {
            return JsonValue.Of(value);
        }

        // One operator per numeric family keeps every primitive convertible without ambiguity (C# picks the nearest source type):
        // byte and ushort widen to ushort, short and sbyte to int, uint and ulong have their own.
        public static implicit operator JsonNode(ushort value)
        {
            return JsonValue.Of((long)value);
        }

        public static implicit operator JsonNode(int value)
        {
            return JsonValue.Of((long)value);
        }

        public static implicit operator JsonNode(uint value)
        {
            return JsonValue.Of((long)value);
        }

        public static implicit operator JsonNode(long value)
        {
            return JsonValue.Of(value);
        }

        public static implicit operator JsonNode(ulong value)
        {
            return JsonValue.Of(value);
        }

        /// <summary>A float keeps its short text: 0.1f becomes 0.1, not 0.10000000149011612.</summary>
        public static implicit operator JsonNode(float value)
        {
            return JsonValue.Of(value);
        }

        public static implicit operator JsonNode(double value)
        {
            return JsonValue.Of(value);
        }
    }
}
