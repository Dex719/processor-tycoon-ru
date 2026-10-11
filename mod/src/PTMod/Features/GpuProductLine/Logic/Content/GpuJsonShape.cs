using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// Finds the fields of a JSON tree that the DTO it is bound to does not have (a typo in a content file would otherwise be ignored
    /// silently by <see cref="ModJson.Bind{T}"/>). Same members as the binder sees: public instance fields and properties with a public getter and setter.
    /// </summary>
    internal static class GpuJsonShape
    {
        private static readonly Dictionary<Type, Dictionary<string, Type>> Members = new Dictionary<Type, Dictionary<string, Type>>();

        /// <summary>
        /// Adds one note per unknown field to <paramref name="notes"/>: <c>unknown field 'year' (ignored)</c>, with the path for nested objects.
        /// <paramref name="ignoreTop"/> names fields of the top level that are not part of the DTO (<c>schema</c> of a config file, <c>disabled</c> of a record).
        /// </summary>
        public static void CollectUnknown(JsonNode node, Type type, string[] ignoreTop, List<string> notes)
        {
            Walk(node, type, string.Empty, ignoreTop, notes, 0);
        }

        private static void Walk(JsonNode node, Type type, string path, string[] ignoreTop, List<string> notes, int depth)
        {
            if (depth > 8) return;
            var obj = node as JsonObject;
            if (obj == null || !IsRecordType(type)) return;

            Dictionary<string, Type> members = MembersOf(type);
            foreach (KeyValuePair<string, JsonNode> pair in obj)
            {
                Type memberType;
                if (!members.TryGetValue(pair.Key, out memberType))
                {
                    if (depth == 0 && ignoreTop != null && Array.IndexOf(ignoreTop, pair.Key) >= 0) continue;
                    notes.Add("unknown field '" + path + pair.Key + "' (ignored)");
                    continue;
                }

                Type element = ElementOf(memberType);
                if (element != null)
                {
                    var array = pair.Value as JsonArray;
                    if (array == null || !IsRecordType(element)) continue;
                    int index = 0;
                    foreach (JsonNode item in array)
                    {
                        Walk(item, element, path + pair.Key + "[" + index + "].", ignoreTop, notes, depth + 1);
                        index++;
                    }
                }
                else if (IsRecordType(memberType))
                {
                    Walk(pair.Value, memberType, path + pair.Key + ".", ignoreTop, notes, depth + 1);
                }
            }
        }

        private static bool IsRecordType(Type type)
        {
            return type.IsClass && type != typeof(string) && !typeof(IEnumerable).IsAssignableFrom(type);
        }

        /// <summary>The element type of a <c>List&lt;T&gt;</c> member, otherwise null.</summary>
        private static Type ElementOf(Type type)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return type.GetGenericArguments()[0];
            return null;
        }

        private static Dictionary<string, Type> MembersOf(Type type)
        {
            lock (Members)
            {
                Dictionary<string, Type> cached;
                if (Members.TryGetValue(type, out cached)) return cached;

                var map = new Dictionary<string, Type>(StringComparer.Ordinal);
                foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!field.IsInitOnly && !field.IsLiteral) map[field.Name] = field.FieldType;
                }
                foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (property.GetIndexParameters().Length != 0) continue;
                    MethodInfo getter = property.GetGetMethod();
                    MethodInfo setter = property.GetSetMethod();
                    if (getter != null && setter != null) map[property.Name] = property.PropertyType;
                }
                Members[type] = map;
                return map;
            }
        }
    }
}
