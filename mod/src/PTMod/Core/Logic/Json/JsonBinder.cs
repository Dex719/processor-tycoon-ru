using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace PTMod.Core.Logic.Json
{
    /// <summary>
    /// Maps JSON trees to plain DTO classes and back (the engine behind <see cref="ModJson.Bind{T}"/> and
    /// <see cref="ModJson.From(object)"/>). Rules, for both directions:
    /// <list type="bullet">
    /// <item>Members are the public instance fields (not readonly or const) and public properties with a public getter and
    /// setter (no indexers); the JSON key is the member name. Get-only properties such as <c>public string Id =&gt; id;</c> are skipped.</item>
    /// <item>Bind: unknown keys are ignored, missing keys keep the value from the constructor and initializers, JSON null sets a
    /// reference or Nullable member to null (and leaves an int, bool, enum or struct member alone), a value of the wrong kind or range
    /// keeps the member default and adds an error with a path ("$.a[2].b: expected number, got string"). A list element
    /// that cannot be converted is dropped (its error keeps the JSON index), a dictionary entry likewise.</item>
    /// <item>From: a C# null member is written as JSON null (so Bind restores the null even when an initializer says otherwise);
    /// only a null <see cref="JsonNode"/> member is left out. NaN and infinities become JSON null (with an error), floats are written in
    /// their shortest round-trip form (0.1f becomes 0.1), enums as names, <see cref="JsonNode"/> members are copied.</item>
    /// <item>Neither direction throws for bad data; at most 100 errors are reported per call.</item>
    /// </list>
    /// Reflection results are cached per type. Single-threaded use is the norm; the caches are locked anyway.
    /// </summary>
    internal static class JsonBinder
    {
        private const int MaxErrors = 100;

        private enum Outcome
        {
            Ok,      // a value was produced
            Null,    // JSON null: the caller clears a reference member and leaves a value member alone
            Error,   // an error was reported: the caller keeps the default
        }

        // ---------- entry points ----------

        internal static T Bind<T>(JsonNode node, List<string> errors) where T : new()
        {
            var context = new Context(errors);
            object value;
            try
            {
                Outcome outcome = BindValue(typeof(T), node ?? JsonValue.Null, context, 0, out value);
                if (outcome == Outcome.Ok && value is T) return (T)value;
            }
            catch (Exception ex)
            {
                context.Path.Length = 1;
                context.Error("internal error: " + ex.GetType().Name + ": " + ex.Message);
            }

            try
            {
                return new T();
            }
            catch (Exception ex)
            {
                context.Path.Length = 1;
                context.Error("cannot create " + TypeName(typeof(T)) + ": " + Unwrap(ex).Message);
                return default(T);
            }
        }

        internal static object Bind(Type type, JsonNode node, List<string> errors)
        {
            var context = new Context(errors);
            try
            {
                object value;
                Outcome outcome = BindValue(type, node ?? JsonValue.Null, context, 0, out value);
                if (outcome == Outcome.Ok) return value;
            }
            catch (Exception ex)
            {
                context.Path.Length = 1;
                context.Error("internal error: " + ex.GetType().Name + ": " + ex.Message);
            }
            return DefaultInstance(type);
        }

        internal static JsonNode From(object dto, List<string> errors)
        {
            var context = new Context(errors);
            try
            {
                return ToNode(dto, context, 0) ?? JsonValue.Null;
            }
            catch (Exception ex)
            {
                context.Path.Length = 1;
                context.Error("internal error: " + ex.GetType().Name + ": " + ex.Message);
                return JsonValue.Null;
            }
        }

        // ---------- errors and paths ----------

        private sealed class Context
        {
            private readonly List<string> _errors;
            private int _reported;

            public Context(List<string> errors)
            {
                _errors = errors;
            }

            public readonly StringBuilder Path = new StringBuilder("$");

            public void Error(string message)
            {
                if (_errors == null) return;
                if (_reported >= MaxErrors)
                {
                    if (_reported == MaxErrors)
                    {
                        _errors.Add("too many errors, the rest is not reported");
                        _reported++;
                    }
                    return;
                }
                _reported++;
                _errors.Add(Path + ": " + message);
            }
        }

        private static int PushKey(Context context, string key)
        {
            int mark = context.Path.Length;
            StringBuilder path = context.Path;
            if (IsPlainKey(key))
            {
                path.Append('.').Append(key);
            }
            else
            {
                path.Append("[\"");
                foreach (char c in key)
                {
                    if (c == '"' || c == '\\') path.Append('\\');
                    path.Append(c);
                }
                path.Append("\"]");
            }
            return mark;
        }

        private static int PushIndex(Context context, int index)
        {
            int mark = context.Path.Length;
            context.Path.Append('[').Append(index.ToString(CultureInfo.InvariantCulture)).Append(']');
            return mark;
        }

        private static bool IsPlainKey(string key)
        {
            if (key.Length == 0) return false;
            foreach (char c in key)
            {
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-')) return false;
            }
            return true;
        }

        private static string KindName(JsonNode node)
        {
            switch (node.Kind)
            {
                case JsonKind.Null: return "null";
                case JsonKind.Bool: return "boolean";
                case JsonKind.Number: return "number";
                case JsonKind.String: return "string";
                case JsonKind.Object: return "object";
                default: return "array";
            }
        }

        private static Outcome Mismatch(Context context, string expected, JsonNode node)
        {
            context.Error("expected " + expected + ", got " + KindName(node));
            return Outcome.Error;
        }

        private static string TypeName(Type type)
        {
            if (!type.IsGenericType) return type.Name;

            string name = type.Name;
            int tick = name.IndexOf('`');
            if (tick > 0) name = name.Substring(0, tick);

            var sb = new StringBuilder(name).Append('<');
            Type[] args = type.GetGenericArguments();
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(TypeName(args[i]));
            }
            return sb.Append('>').ToString();
        }

        private static Exception Unwrap(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            return ex;
        }

        private static string Shorten(string text)
        {
            return text.Length <= 40 ? text : text.Substring(0, 40) + "...";
        }

        // ---------- types, members, plans (cached) ----------

        private sealed class Member
        {
            public string Name;
            public Type Type;
            public FieldInfo Field;
            public PropertyInfo Property;

            public object Get(object instance)
            {
                return Field != null ? Field.GetValue(instance) : Property.GetValue(instance, null);
            }

            public void Set(object instance, object value)
            {
                if (Field != null) Field.SetValue(instance, value);
                else Property.SetValue(instance, value, null);
            }
        }

        private sealed class TypeMap
        {
            public readonly List<Member> Members = new List<Member>();
            public readonly Dictionary<string, Member> ByName = new Dictionary<string, Member>(StringComparer.Ordinal);
        }

        private enum PlanKind
        {
            Dto,
            Sequence,
            Dictionary,
            Unsupported,
        }

        private sealed class Plan
        {
            public PlanKind Kind;
            public Type Concrete;    // what is instantiated (List<T> for IList<T>, ...)
            public Type Element;     // sequence element or dictionary value type
            public MethodInfo Add;   // ICollection<T>.Add for sets; null when the non-generic IList is used
            public string Problem;
        }

        private static readonly Dictionary<Type, TypeMap> Maps = new Dictionary<Type, TypeMap>();
        private static readonly Dictionary<Type, Plan> Plans = new Dictionary<Type, Plan>();

        private static TypeMap MapOf(Type type)
        {
            lock (Maps)
            {
                TypeMap map;
                if (Maps.TryGetValue(type, out map)) return map;
                map = BuildMap(type);
                Maps[type] = map;
                return map;
            }
        }

        private static TypeMap BuildMap(Type type)
        {
            var map = new TypeMap();

            // Base classes first, then the type itself; inside a type fields, then properties, each in declaration order.
            var chain = new List<Type>();
            for (Type t = type; t != null && t != typeof(object) && t != typeof(ValueType); t = t.BaseType) chain.Insert(0, t);

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (Type t in chain)
            {
                FieldInfo[] fields = t.GetFields(flags);
                Array.Sort(fields, (a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
                foreach (FieldInfo field in fields)
                {
                    if (field.IsInitOnly || field.IsLiteral) continue;
                    AddMember(map, new Member { Name = field.Name, Type = field.FieldType, Field = field });
                }

                PropertyInfo[] properties = t.GetProperties(flags);
                Array.Sort(properties, (a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
                foreach (PropertyInfo property in properties)
                {
                    if (property.GetIndexParameters().Length != 0) continue;
                    if (property.GetGetMethod(false) == null || property.GetSetMethod(false) == null) continue;
                    AddMember(map, new Member { Name = property.Name, Type = property.PropertyType, Property = property });
                }
            }
            return map;
        }

        private static void AddMember(TypeMap map, Member member)
        {
            Member existing;
            if (map.ByName.TryGetValue(member.Name, out existing))
            {
                map.Members[map.Members.IndexOf(existing)] = member;   // a derived member hides the base one
            }
            else
            {
                map.Members.Add(member);
            }
            map.ByName[member.Name] = member;
        }

        private static Plan PlanOf(Type type)
        {
            lock (Plans)
            {
                Plan plan;
                if (Plans.TryGetValue(type, out plan)) return plan;
                plan = BuildPlan(type);
                Plans[type] = plan;
                return plan;
            }
        }

        private static Plan BuildPlan(Type type)
        {
            Type concrete = type;
            Type element = null;
            Type key = null;
            bool dictionary = false;

            if (type.IsInterface || type.IsAbstract)
            {
                if (type.IsGenericType)
                {
                    Type definition = type.GetGenericTypeDefinition();
                    Type[] args = type.GetGenericArguments();
                    if (definition == typeof(IEnumerable<>) || definition == typeof(ICollection<>) || definition == typeof(IList<>)
                        || definition == typeof(IReadOnlyCollection<>) || definition == typeof(IReadOnlyList<>))
                    {
                        element = args[0];
                        concrete = typeof(List<>).MakeGenericType(args[0]);
                    }
                    else if (definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>))
                    {
                        dictionary = true;
                        key = args[0];
                        element = args[1];
                        concrete = typeof(Dictionary<,>).MakeGenericType(args);
                    }
                }
                if (element == null)
                    return new Plan { Kind = PlanKind.Unsupported, Problem = "unsupported type " + TypeName(type) + " (an interface or abstract class)" };
            }
            else
            {
                foreach (Type face in type.GetInterfaces())
                {
                    if (!face.IsGenericType || face.GetGenericTypeDefinition() != typeof(IDictionary<,>)) continue;
                    Type[] args = face.GetGenericArguments();
                    dictionary = true;
                    key = args[0];
                    element = args[1];
                    break;
                }
                if (!dictionary)
                {
                    foreach (Type face in type.GetInterfaces())
                    {
                        if (!face.IsGenericType || face.GetGenericTypeDefinition() != typeof(ICollection<>)) continue;
                        element = face.GetGenericArguments()[0];
                        break;
                    }
                }
                if (element == null) return new Plan { Kind = PlanKind.Dto, Concrete = type };
            }

            if (concrete.GetConstructor(Type.EmptyTypes) == null)
                return new Plan { Kind = PlanKind.Unsupported, Problem = "unsupported type " + TypeName(type) + " (no public parameterless constructor)" };

            if (dictionary)
            {
                if (key != typeof(string))
                    return new Plan { Kind = PlanKind.Unsupported, Problem = "unsupported type " + TypeName(type) + " (dictionary keys must be strings)" };
                if (!typeof(IDictionary).IsAssignableFrom(concrete))
                    return new Plan { Kind = PlanKind.Unsupported, Problem = "unsupported type " + TypeName(type) };
                return new Plan { Kind = PlanKind.Dictionary, Concrete = concrete, Element = element };
            }

            MethodInfo add = null;
            if (!typeof(IList).IsAssignableFrom(concrete)) add = typeof(ICollection<>).MakeGenericType(element).GetMethod("Add");
            return new Plan { Kind = PlanKind.Sequence, Concrete = concrete, Element = element, Add = add };
        }

        private static bool IsUnsupportedLeaf(Type type)
        {
            return type == typeof(object) || type == typeof(TimeSpan) || type == typeof(Guid) || type == typeof(DateTimeOffset)
                   || type == typeof(IntPtr) || type == typeof(UIntPtr) || type.IsPointer || type.IsByRef
                   || typeof(Delegate).IsAssignableFrom(type) || typeof(Type).IsAssignableFrom(type);
        }

        private static bool CanBeNull(Type type)
        {
            return !type.IsValueType || Nullable.GetUnderlyingType(type) != null;
        }

        private static object DefaultOf(Type type)
        {
            return type.IsValueType && Nullable.GetUnderlyingType(type) == null ? Activator.CreateInstance(type) : null;
        }

        private static object DefaultInstance(Type type)
        {
            try
            {
                if (type.IsValueType) return Activator.CreateInstance(type);
                if (type.IsInterface || type.IsAbstract || type == typeof(string)) return null;
                return Activator.CreateInstance(type, true);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ---------- Bind ----------

        private static Outcome BindValue(Type type, JsonNode node, Context context, int depth, out object value)
        {
            value = null;

            if (typeof(JsonNode).IsAssignableFrom(type)) return BindNode(type, node, context, out value);
            if (node.IsNull) return Outcome.Null;

            Type underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null) type = underlying;

            if (type.IsEnum) return BindEnum(type, node, context, out value);

            JsonValue scalar = node as JsonValue;
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Boolean:
                    if (scalar != null && scalar.Kind == JsonKind.Bool)
                    {
                        value = scalar.BoolValue;
                        return Outcome.Ok;
                    }
                    return Mismatch(context, "boolean", node);

                case TypeCode.String:
                    if (scalar != null && scalar.Kind == JsonKind.String)
                    {
                        value = scalar.StringValue;
                        return Outcome.Ok;
                    }
                    return Mismatch(context, "string", node);

                case TypeCode.Char:
                    if (scalar != null && scalar.Kind == JsonKind.String && scalar.StringValue.Length == 1)
                    {
                        value = scalar.StringValue[0];
                        return Outcome.Ok;
                    }
                    if (scalar != null && scalar.Kind == JsonKind.String)
                    {
                        context.Error("expected a string of one character, got " + scalar.StringValue.Length.ToString(CultureInfo.InvariantCulture) + " characters");
                        return Outcome.Error;
                    }
                    return Mismatch(context, "string", node);

                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.Byte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                case TypeCode.Single:
                case TypeCode.Double:
                case TypeCode.Decimal:
                    return BindNumber(type, Type.GetTypeCode(type), node, context, out value);

                case TypeCode.Object:
                    return BindComposite(type, node, context, depth, out value);

                default:
                    context.Error("unsupported type " + TypeName(type));
                    return Outcome.Error;
            }
        }

        private static Outcome BindNode(Type type, JsonNode node, Context context, out object value)
        {
            value = null;
            if (type.IsInstanceOfType(node))
            {
                try
                {
                    value = node.DeepClone();
                    return Outcome.Ok;
                }
                catch (InvalidOperationException)
                {
                    context.Error("the JSON is nested too deeply");
                    return Outcome.Error;
                }
            }
            if (node.IsNull) return Outcome.Null;   // null into JsonObject / JsonArray: the member becomes null

            string expected = type == typeof(JsonObject) ? "object" : type == typeof(JsonArray) ? "array" : "null, boolean, number or string";
            return Mismatch(context, expected, node);
        }

        private static Outcome BindNumber(Type type, TypeCode code, JsonNode node, Context context, out object value)
        {
            value = null;
            var number = node as JsonValue;
            if (number == null || number.Kind != JsonKind.Number) return Mismatch(context, "number", node);

            switch (code)
            {
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                    {
                        long l;
                        if (!number.TryGetInt64(out l)) return IntegerProblem(context, number, type);
                        long min, max;
                        SignedRange(code, out min, out max);
                        if (l < min || l > max) return OutOfRange(context, number, type);
                        switch (code)
                        {
                            case TypeCode.SByte: value = (sbyte)l; break;
                            case TypeCode.Int16: value = (short)l; break;
                            case TypeCode.Int32: value = (int)l; break;
                            default: value = l; break;
                        }
                        return Outcome.Ok;
                    }

                case TypeCode.Byte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                    {
                        ulong u;
                        if (!number.TryGetUInt64(out u)) return IntegerProblem(context, number, type);
                        ulong max = UnsignedMax(code);
                        if (u > max) return OutOfRange(context, number, type);
                        switch (code)
                        {
                            case TypeCode.Byte: value = (byte)u; break;
                            case TypeCode.UInt16: value = (ushort)u; break;
                            case TypeCode.UInt32: value = (uint)u; break;
                            default: value = u; break;
                        }
                        return Outcome.Ok;
                    }

                case TypeCode.Single:
                    {
                        float f = (float)number.NumberValue;
                        if (float.IsInfinity(f)) return OutOfRange(context, number, type);
                        value = f;
                        return Outcome.Ok;
                    }

                case TypeCode.Double:
                    value = number.NumberValue;
                    return Outcome.Ok;

                default:   // Decimal
                    try
                    {
                        value = (decimal)number.NumberValue;
                        return Outcome.Ok;
                    }
                    catch (OverflowException)
                    {
                        return OutOfRange(context, number, type);
                    }
            }
        }

        private static void SignedRange(TypeCode code, out long min, out long max)
        {
            switch (code)
            {
                case TypeCode.SByte: min = sbyte.MinValue; max = sbyte.MaxValue; break;
                case TypeCode.Int16: min = short.MinValue; max = short.MaxValue; break;
                case TypeCode.Int32: min = int.MinValue; max = int.MaxValue; break;
                default: min = long.MinValue; max = long.MaxValue; break;
            }
        }

        private static ulong UnsignedMax(TypeCode code)
        {
            switch (code)
            {
                case TypeCode.Byte: return byte.MaxValue;
                case TypeCode.UInt16: return ushort.MaxValue;
                case TypeCode.UInt32: return uint.MaxValue;
                default: return ulong.MaxValue;
            }
        }

        private static Outcome IntegerProblem(Context context, JsonValue number, Type type)
        {
            double d = number.NumberValue;
            if (d != Math.Floor(d)) context.Error("expected an integer (" + TypeName(type) + "), got " + JsonValue.FormatNumber(d));
            else OutOfRange(context, number, type);
            return Outcome.Error;
        }

        private static Outcome OutOfRange(Context context, JsonValue number, Type type)
        {
            context.Error("the value " + number.NumberText() + " is out of range for " + TypeName(type));
            return Outcome.Error;
        }

        private static Outcome BindEnum(Type type, JsonNode node, Context context, out object value)
        {
            value = null;
            var scalar = node as JsonValue;
            if (scalar != null && scalar.Kind == JsonKind.String)
            {
                if (TryParseEnum(type, scalar.StringValue, out value)) return Outcome.Ok;
                context.Error("\"" + Shorten(scalar.StringValue) + "\" is not a value of " + TypeName(type));
                return Outcome.Error;
            }
            if (scalar != null && scalar.Kind == JsonKind.Number)
            {
                long l;
                if (scalar.TryGetInt64(out l))
                {
                    try
                    {
                        object candidate = Enum.ToObject(type, l);
                        if (type.IsDefined(typeof(FlagsAttribute), false) || Enum.IsDefined(type, candidate))
                        {
                            value = candidate;
                            return Outcome.Ok;
                        }
                    }
                    catch (Exception)
                    {
                        // falls through to the error below
                    }
                }
                context.Error("the value " + scalar.NumberText() + " is not a value of " + TypeName(type));
                return Outcome.Error;
            }
            return Mismatch(context, "string", node);
        }

        private static bool TryParseEnum(Type type, string text, out object value)
        {
            value = null;
            string[] parts = text.Split(',');
            if (parts.Length > 1 && !type.IsDefined(typeof(FlagsAttribute), false)) return false;

            string[] names = Enum.GetNames(type);
            foreach (string part in parts)
            {
                string name = part.Trim();
                bool found = false;
                foreach (string candidate in names)
                {
                    if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found) return false;
            }

            try
            {
                value = Enum.Parse(type, text, true);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static Outcome BindComposite(Type type, JsonNode node, Context context, int depth, out object value)
        {
            value = null;
            if (depth >= ModJson.MaxDepth)
            {
                context.Error("the JSON is nested too deeply");
                return Outcome.Error;
            }
            if (IsUnsupportedLeaf(type))
            {
                context.Error("unsupported type " + TypeName(type));
                return Outcome.Error;
            }

            if (type.IsArray)
            {
                if (type.GetArrayRank() != 1)
                {
                    context.Error("unsupported type " + TypeName(type) + " (multidimensional array)");
                    return Outcome.Error;
                }
                return BindArray(type.GetElementType(), node, context, depth, out value);
            }

            Plan plan = PlanOf(type);
            switch (plan.Kind)
            {
                case PlanKind.Sequence:
                    return BindSequence(plan, node, context, depth, out value);
                case PlanKind.Dictionary:
                    return BindDictionary(plan, node, context, depth, out value);
                case PlanKind.Unsupported:
                    context.Error(plan.Problem);
                    return Outcome.Error;
                default:
                    return BindDto(type, node, context, depth, out value);
            }
        }

        private static bool TryCreate(Type type, Context context, out object instance)
        {
            try
            {
                instance = Activator.CreateInstance(type, true);
                return instance != null;
            }
            catch (Exception ex)
            {
                instance = null;
                context.Error("cannot create " + TypeName(type) + ": " + Unwrap(ex).Message);
                return false;
            }
        }

        private static Outcome BindArray(Type elementType, JsonNode node, Context context, int depth, out object value)
        {
            value = null;
            var array = node as JsonArray;
            if (array == null) return Mismatch(context, "array", node);

            var items = new List<object>(array.Count);
            for (int i = 0; i < array.Count; i++)
            {
                int mark = PushIndex(context, i);
                object item;
                Outcome outcome = BindValue(elementType, array[i], context, depth + 1, out item);
                context.Path.Length = mark;

                if (outcome == Outcome.Error) continue;
                items.Add(outcome == Outcome.Null ? DefaultOf(elementType) : item);
            }

            Array result = Array.CreateInstance(elementType, items.Count);
            for (int i = 0; i < items.Count; i++) result.SetValue(items[i], i);
            value = result;
            return Outcome.Ok;
        }

        private static Outcome BindSequence(Plan plan, JsonNode node, Context context, int depth, out object value)
        {
            value = null;
            var array = node as JsonArray;
            if (array == null) return Mismatch(context, "array", node);

            object instance;
            if (!TryCreate(plan.Concrete, context, out instance)) return Outcome.Error;

            var list = instance as IList;
            for (int i = 0; i < array.Count; i++)
            {
                int mark = PushIndex(context, i);
                object item;
                Outcome outcome = BindValue(plan.Element, array[i], context, depth + 1, out item);
                if (outcome == Outcome.Null) item = DefaultOf(plan.Element);

                if (outcome != Outcome.Error)
                {
                    try
                    {
                        if (plan.Add == null) list.Add(item);
                        else plan.Add.Invoke(instance, new[] { item });
                    }
                    catch (Exception ex)
                    {
                        context.Error("cannot add the element: " + Unwrap(ex).Message);
                    }
                }
                context.Path.Length = mark;
            }
            value = instance;
            return Outcome.Ok;
        }

        private static Outcome BindDictionary(Plan plan, JsonNode node, Context context, int depth, out object value)
        {
            value = null;
            var obj = node as JsonObject;
            if (obj == null) return Mismatch(context, "object", node);

            object instance;
            if (!TryCreate(plan.Concrete, context, out instance)) return Outcome.Error;

            var dictionary = (IDictionary)instance;
            foreach (KeyValuePair<string, JsonNode> entry in obj)
            {
                int mark = PushKey(context, entry.Key);
                object item;
                Outcome outcome = BindValue(plan.Element, entry.Value, context, depth + 1, out item);
                if (outcome == Outcome.Null) item = DefaultOf(plan.Element);

                if (outcome != Outcome.Error)
                {
                    try
                    {
                        dictionary[entry.Key] = item;
                    }
                    catch (Exception ex)
                    {
                        context.Error("cannot add the entry: " + Unwrap(ex).Message);
                    }
                }
                context.Path.Length = mark;
            }
            value = instance;
            return Outcome.Ok;
        }

        private static Outcome BindDto(Type type, JsonNode node, Context context, int depth, out object value)
        {
            value = null;
            var obj = node as JsonObject;
            if (obj == null) return Mismatch(context, "object", node);

            object instance;
            if (!TryCreate(type, context, out instance)) return Outcome.Error;

            TypeMap map = MapOf(type);
            foreach (KeyValuePair<string, JsonNode> entry in obj)
            {
                Member member;
                if (!map.ByName.TryGetValue(entry.Key, out member)) continue;   // unknown key: ignored, not an error

                int mark = PushKey(context, entry.Key);
                object memberValue;
                Outcome outcome = BindValue(member.Type, entry.Value, context, depth + 1, out memberValue);
                // JSON null clears a reference or Nullable member; a plain value member (int, bool, enum, struct) cannot hold it and is left alone.
                if (outcome == Outcome.Ok || (outcome == Outcome.Null && CanBeNull(member.Type)))
                {
                    try
                    {
                        member.Set(instance, outcome == Outcome.Ok ? memberValue : null);
                    }
                    catch (Exception ex)
                    {
                        context.Error("cannot set the member: " + Unwrap(ex).Message);
                    }
                }
                context.Path.Length = mark;
            }
            value = instance;
            return Outcome.Ok;
        }

        // ---------- From ----------

        /// <summary>Returns the node, or null when the value is null or cannot be represented (the latter reports an error).</summary>
        private static JsonNode ToNode(object value, Context context, int depth)
        {
            if (value == null) return null;

            var node = value as JsonNode;
            if (node != null)
            {
                try
                {
                    return node.DeepClone();
                }
                catch (InvalidOperationException)
                {
                    context.Error("the JSON is nested too deeply");
                    return null;
                }
            }

            string text = value as string;
            if (text != null) return JsonValue.Of(text);

            Type type = value.GetType();
            if (type.IsEnum) return EnumToNode(value, type);

            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Boolean:
                    return JsonValue.Of((bool)value);
                case TypeCode.Char:
                    return JsonValue.Of(value.ToString());
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.Byte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                    return JsonValue.Of(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                case TypeCode.UInt64:
                    return JsonValue.Of((ulong)value);
                case TypeCode.Single:
                    {
                        float f = (float)value;
                        if (float.IsNaN(f) || float.IsInfinity(f)) return NotFinite(context);
                        return JsonValue.Of(f);   // shortest text of the float: 0.1f is written as 0.1
                    }
                case TypeCode.Double:
                    {
                        double d = (double)value;
                        if (double.IsNaN(d) || double.IsInfinity(d)) return NotFinite(context);
                        return JsonValue.Of(d);
                    }
                case TypeCode.Decimal:
                    return JsonValue.Of((double)(decimal)value);
                case TypeCode.Object:
                    break;
                default:
                    context.Error("unsupported type " + TypeName(type));
                    return null;
            }

            if (IsUnsupportedLeaf(type))
            {
                context.Error("unsupported type " + TypeName(type));
                return null;
            }
            if (type.IsArray && type.GetArrayRank() != 1)
            {
                context.Error("unsupported type " + TypeName(type) + " (multidimensional array)");
                return null;
            }
            return ToContainer(value, type, context, depth);
        }

        private static JsonNode NotFinite(Context context)
        {
            context.Error("NaN and infinity cannot be written in JSON, written as null");
            return JsonValue.Null;
        }

        private static JsonNode EnumToNode(object value, Type type)
        {
            string name = value.ToString();   // "Name" or "A, B" for flags; a plain number for an undefined value
            bool numeric = name.Length > 0 && (char.IsDigit(name[0]) || name[0] == '-');
            if (numeric)
            {
                try
                {
                    return JsonValue.Of(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                }
                catch (OverflowException)
                {
                    return JsonValue.Of(Convert.ToUInt64(value, CultureInfo.InvariantCulture));
                }
            }
            return JsonValue.Of(name);
        }

        private static JsonNode ToContainer(object value, Type type, Context context, int depth)
        {
            if (depth + 1 > ModJson.MaxDepth)
            {
                context.Error("the data is nested deeper than " + ModJson.MaxDepth.ToString(CultureInfo.InvariantCulture) + " levels");
                return null;
            }

            var dictionary = value as IDictionary;
            if (dictionary != null)
            {
                var obj = new JsonObject();
                foreach (DictionaryEntry entry in dictionary)
                {
                    var key = entry.Key as string;
                    if (key == null)
                    {
                        context.Error("unsupported dictionary key type " + (entry.Key == null ? "null" : TypeName(entry.Key.GetType())));
                        continue;
                    }
                    int mark = PushKey(context, key);
                    obj.Set(key, ToNode(entry.Value, context, depth + 1) ?? JsonValue.Null);
                    context.Path.Length = mark;
                }
                return obj;
            }

            var sequence = value as IEnumerable;
            if (sequence != null)
            {
                var array = new JsonArray();
                int index = 0;
                foreach (object item in sequence)
                {
                    int mark = PushIndex(context, index++);
                    array.Add(ToNode(item, context, depth + 1) ?? JsonValue.Null);
                    context.Path.Length = mark;
                }
                return array;
            }

            var result = new JsonObject();
            foreach (Member member in MapOf(type).Members)
            {
                int mark = PushKey(context, member.Name);
                object memberValue;
                try
                {
                    memberValue = member.Get(value);
                }
                catch (Exception ex)
                {
                    context.Error("cannot read the member: " + Unwrap(ex).Message);
                    context.Path.Length = mark;
                    continue;
                }

                if (memberValue != null)
                {
                    JsonNode child = ToNode(memberValue, context, depth + 1);
                    if (child != null) result.Set(member.Name, child);
                }
                else if (!typeof(JsonNode).IsAssignableFrom(member.Type))
                {
                    result.Set(member.Name, JsonValue.Null);   // an explicit null, so Bind can restore a null that the initializer would not give
                }
                context.Path.Length = mark;
            }
            return result;
        }
    }
}
