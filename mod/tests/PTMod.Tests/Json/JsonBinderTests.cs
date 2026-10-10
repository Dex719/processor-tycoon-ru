#pragma warning disable CS0649 // fields of the test DTOs are filled by the binder through reflection

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using PTMod.Core.Logic.Json;

namespace PTMod.Tests.Json
{
    /// <summary>
    /// The DTO binder behind ModJson.Bind and ModJson.From (TSK-010): List, Dictionary, nested DTOs, JsonNode members,
    /// uint/int/float/double/bool/string and the other primitives, errors with a path such as $.a[2].b, and the round
    /// trip From(dto) -> Bind giving an equal object.
    /// </summary>
    [Covers(typeof(ModJson))]
    public class JsonBinderTests
    {
        // ---------- DTOs (nested and private, so the runner never takes them for tests) ----------

        private sealed class Scalars
        {
            public bool flag;
            public int i;
            public uint u;
            public long l;
            public ulong ul;
            public float f;
            public double d;
            public string s;
            public short sh;
            public ushort us;
            public byte b;
            public sbyte sb;
            public decimal m;
            public char c;
        }

        private sealed class Inner
        {
            public int n;
            public string s = "x";
        }

        private sealed class Defaults
        {
            public int n = 5;
            public string s = "def";
            public List<int> list = new List<int> { 1, 2 };
            public Inner inner = new Inner();
            public double? maybe = 7;
        }

        private sealed class Item
        {
            public int b;
            public string name;
        }

        private sealed class Root
        {
            public List<Item> a;
        }

        private sealed class Maps
        {
            public Dictionary<string, Inner> map;
            public Dictionary<string, int> counts = new Dictionary<string, int>();
            public Dictionary<string, List<int>> lists;
        }

        private sealed class Nodes
        {
            public JsonNode any;
            public JsonObject obj;
            public JsonArray arr;
            public JsonValue val;
            public Dictionary<string, JsonNode> options;
            public JsonObject @params;
        }

        private enum Color
        {
            Red,
            Green,
            Blue,
        }

        [Flags]
        private enum Perm
        {
            None = 0,
            Read = 1,
            Write = 2,
            Exec = 4,
        }

        private sealed class Enums
        {
            public Color color;
            public Perm perm;
            public Color? maybe;
            public List<Color> list;
        }

        private sealed class Props
        {
            public static int Shared;
            public const int Constant = 1;
            public readonly int fixedValue = 3;
            public string id;
            private int secret = 4;

            public string Name { get; set; }
            public int Count { get; set; }
            public string Id => id;
            public int Hidden { get; private set; }
            public int ReadOnlyWithBody { get { return 5; } }
            public int this[int index] { get { return index; } set { secret = value; } }

            public int Secret { get { return secret; } }
        }

        private class BaseDto
        {
            public int baseValue;
        }

        private sealed class Derived : BaseDto
        {
            public int derivedValue;
        }

        private struct Point
        {
            public int x;
            public int y;
        }

        private sealed class WithStruct
        {
            public Point point;
            public Point? maybePoint;
        }

        private sealed class Unsupported
        {
            public object any;
            public DateTime when;
            public Guid guid;
            public TimeSpan span;
            public int[,] grid;
            public Dictionary<int, string> intKeys;
            public int ok;
        }

        private sealed class Collections
        {
            public int[] ints;
            public string[] strings;
            public Item[] items;
            public IList<int> ilist;
            public IEnumerable<string> ienum;
            public IReadOnlyList<Item> roList;
            public ICollection<int> icoll;
            public IDictionary<string, int> idict;
            public IReadOnlyDictionary<string, Item> roDict;
            public HashSet<string> set;
            public List<List<string>> nested;
        }

        private sealed class Node
        {
            public Node child;
            public int v;
        }

        private sealed class ThrowingCtor
        {
            public ThrowingCtor()
            {
                throw new InvalidOperationException("boom");
            }
        }

        private sealed class Holder
        {
            public ThrowingCtor bad;
            public int ok;
        }

        private sealed class Strict
        {
            private int _value;

            public int Value
            {
                get { return _value; }
                set
                {
                    if (value < 0) throw new ArgumentException("negative");
                    _value = value;
                }
            }

            public int other;
        }

        private sealed class BadGetter
        {
            public int good = 1;
            public int Bad { get { throw new InvalidOperationException("cannot read"); } set { } }
            public int alsoGood = 2;
        }

        private sealed class Big
        {
            public bool flag;
            public int i;
            public uint u;
            public long l;
            public ulong ul;
            public float f;
            public double d;
            public string s;
            public decimal m;
            public char c;
            public Color color;
            public Perm perm;
            public int? maybe;
            public Color? maybeColor;
            public List<Item> items = new List<Item>();
            public Dictionary<string, Inner> map = new Dictionary<string, Inner>();
            public int[] ints;
            public HashSet<string> set;
            public Inner inner;
            public JsonNode any;
            public Point point;
            public List<List<int>> grid;
            public Dictionary<string, List<string>> lists;
            public List<string> strings;
            public string Title { get; set; }
            public double Ratio { get; set; }
        }

        // ---------- helpers ----------

        private static JsonNode Parse(string text)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out node, out error), "parse: " + error);
            return node;
        }

        private static T Bind<T>(string json, out List<string> errors) where T : new()
        {
            errors = new List<string>();
            return ModJson.Bind<T>(Parse(json), errors);
        }

        private static T BindClean<T>(string json) where T : new()
        {
            List<string> errors;
            T result = Bind<T>(json, out errors);
            Assert.Equal(0, errors.Count, "unexpected binder errors: " + string.Join(" | ", errors));
            return result;
        }

        private static string Text(JsonNode node)
        {
            return ModJson.Write(node, false);
        }

        // ---------- Bind: scalars ----------

        [Test]
        public void Bind_Scalars_OfEveryType()
        {
            Scalars dto = BindClean<Scalars>(
                "{\"flag\":true,\"i\":-5,\"u\":4294967295,\"l\":-9223372036854775808,\"ul\":18446744073709551615,\"f\":0.1,\"d\":1e-7,"
                + "\"s\":\"привет\",\"sh\":-32768,\"us\":65535,\"b\":255,\"sb\":-128,\"m\":12.5,\"c\":\"x\"}");

            Assert.True(dto.flag);
            Assert.Equal(-5, dto.i);
            Assert.Equal(4294967295u, dto.u);
            Assert.Equal(long.MinValue, dto.l);
            Assert.Equal(ulong.MaxValue, dto.ul);
            Assert.Equal(0.1f, dto.f);
            Assert.Equal(1e-7, dto.d);
            Assert.Equal("привет", dto.s);
            Assert.Equal((short)-32768, dto.sh);
            Assert.Equal((ushort)65535, dto.us);
            Assert.Equal((byte)255, dto.b);
            Assert.Equal((sbyte)-128, dto.sb);
            Assert.Equal(12.5m, dto.m);
            Assert.Equal('x', dto.c);
        }

        [Test]
        public void Bind_Integers_AcceptWholeNumbersInAnySpelling()
        {
            Scalars dto = BindClean<Scalars>("{\"i\":3.0,\"u\":1e3,\"l\":-0,\"b\":2E0}");
            Assert.Equal(3, dto.i);
            Assert.Equal(1000u, dto.u);
            Assert.Equal(0L, dto.l);
            Assert.Equal((byte)2, dto.b);

            Scalars edge = BindClean<Scalars>("{\"i\":2147483647,\"sh\":32767,\"sb\":127,\"us\":0,\"l\":9223372036854775807}");
            Assert.Equal(int.MaxValue, edge.i);
            Assert.Equal(long.MaxValue, edge.l);
        }

        [Test]
        public void Bind_Integers_RejectFractionsAndOverflow()
        {
            var cases = new[]
            {
                new[] { "{\"i\":3000000000}", "$.i: the value 3000000000 is out of range for Int32" },
                new[] { "{\"i\":-2147483649}", "$.i: the value -2147483649 is out of range for Int32" },
                new[] { "{\"i\":1.5}", "$.i: expected an integer (Int32), got 1.5" },
                new[] { "{\"u\":-1}", "$.u: the value -1 is out of range for UInt32" },
                new[] { "{\"u\":4294967296}", "$.u: the value 4294967296 is out of range for UInt32" },
                new[] { "{\"b\":256}", "$.b: the value 256 is out of range for Byte" },
                new[] { "{\"sb\":-129}", "$.sb: the value -129 is out of range for SByte" },
                new[] { "{\"us\":65536}", "$.us: the value 65536 is out of range for UInt16" },
                new[] { "{\"sh\":32768}", "$.sh: the value 32768 is out of range for Int16" },
                new[] { "{\"ul\":-1}", "$.ul: the value -1 is out of range for UInt64" },
                new[] { "{\"ul\":18446744073709551616}", "$.ul: the value 18446744073709551616 is out of range for UInt64" },
                new[] { "{\"l\":9223372036854775808}", "$.l: the value 9223372036854775808 is out of range for Int64" },
                new[] { "{\"l\":0.5}", "$.l: expected an integer (Int64), got 0.5" },
                new[] { "{\"f\":1e300}", "$.f: the value 1e300 is out of range for Single" },
            };
            foreach (string[] c in cases)
            {
                List<string> errors;
                Scalars dto = Bind<Scalars>(c[0], out errors);
                Assert.Equal(new[] { c[1] }, errors.ToArray(), c[0]);
                Assert.Equal(0, dto.i);
                Assert.Equal(0u, dto.u);
                Assert.Equal((byte)0, dto.b);
                Assert.Equal(0L, dto.l);
                Assert.Equal(0f, dto.f);
            }
        }

        [Test]
        public void Bind_WrongKinds_KeepDefaultsAndNameTheKinds()
        {
            List<string> errors;
            Scalars dto = Bind<Scalars>("{\"i\":\"5\",\"flag\":1,\"s\":5,\"d\":true,\"c\":\"xy\",\"u\":[1],\"b\":{\"x\":1},\"m\":\"1\"}", out errors);
            Assert.Equal(new[]
            {
                "$.i: expected number, got string",
                "$.flag: expected boolean, got number",
                "$.s: expected string, got number",
                "$.d: expected number, got boolean",
                "$.c: expected a string of one character, got 2 characters",
                "$.u: expected number, got array",
                "$.b: expected number, got object",
                "$.m: expected number, got string",
            }, errors.ToArray());
            Assert.Equal(0, dto.i);
            Assert.False(dto.flag);
            Assert.Null(dto.s);
            Assert.Equal(0.0, dto.d);
        }

        // ---------- Bind: paths ----------

        [Test]
        public void Bind_ErrorPath_ListElementMember()
        {
            List<string> errors;
            Root dto = Bind<Root>("{\"a\":[{\"b\":1},{\"b\":2},{\"b\":\"x\"}]}", out errors);

            Assert.Equal(new[] { "$.a[2].b: expected number, got string" }, errors.ToArray());
            Assert.Equal(3, dto.a.Count);
            Assert.Equal(1, dto.a[0].b);
            Assert.Equal(2, dto.a[1].b);
            Assert.Equal(0, dto.a[2].b);
        }

        [Test]
        public void Bind_ErrorPath_DictionaryKeysAndNestedLists()
        {
            List<string> errors;
            Maps maps = Bind<Maps>("{\"map\":{\"ai:1\":{\"n\":\"bad\"},\"plain\":{\"n\":1},\"a\\\"b\":{\"n\":true},\"\":{\"n\":null}},\"lists\":{\"k\":[1,\"x\"]}}", out errors);
            Assert.Equal(new[]
            {
                "$.map[\"ai:1\"].n: expected number, got string",
                "$.map[\"a\\\"b\"].n: expected number, got boolean",
                "$.lists.k[1]: expected number, got string",
            }, errors.ToArray());
            Assert.Equal(4, maps.map.Count);
            Assert.Equal(1, maps.map["plain"].n);
            Assert.Equal(new List<int> { 1 }, maps.lists["k"]);

            Collections nested = Bind<Collections>("{\"nested\":[[\"a\",1],[\"b\"]]}", out errors);
            Assert.Equal(new[] { "$.nested[0][1]: expected string, got number" }, errors.ToArray());
            Assert.Equal(2, nested.nested.Count);
            Assert.Equal(new List<string> { "a" }, nested.nested[0]);
            Assert.Equal(new List<string> { "b" }, nested.nested[1]);
        }

        [Test]
        public void Bind_ErrorPath_RootAndNestedObjects()
        {
            List<string> errors;
            Scalars fromArray = Bind<Scalars>("[1,2]", out errors);
            Assert.Equal(new[] { "$: expected object, got array" }, errors.ToArray());
            Assert.NotNull(fromArray);

            Defaults dto = Bind<Defaults>("{\"inner\":[1],\"list\":{\"a\":1},\"s\":null}", out errors);
            Assert.Equal(new[] { "$.inner: expected object, got array", "$.list: expected array, got object" }, errors.ToArray());
            Assert.Equal("x", dto.inner.s);
            Assert.Equal(new List<int> { 1, 2 }, dto.list);

            int number = ModJson.Bind<int>(Parse("\"x\""), errors = new List<string>());
            Assert.Equal(0, number);
            Assert.Equal(new[] { "$: expected number, got string" }, errors.ToArray());
        }

        // ---------- Bind: defaults, nulls, unknown keys ----------

        [Test]
        public void Bind_MissingKeys_KeepConstructorValues()
        {
            Defaults dto = BindClean<Defaults>("{}");
            Assert.Equal(5, dto.n);
            Assert.Equal("def", dto.s);
            Assert.Equal(new List<int> { 1, 2 }, dto.list);
            Assert.NotNull(dto.inner);
            Assert.Equal(7.0, dto.maybe.Value);
        }

        [Test]
        public void Bind_UnknownKeys_AreIgnoredSilently()
        {
            Defaults dto = BindClean<Defaults>("{\"zzz\":1,\"N\":99,\"inner\":{\"unknown\":[1,2],\"n\":3},\"also\":{\"deep\":[{}]}}");
            Assert.Equal(5, dto.n);   // keys are case sensitive: "N" is unknown
            Assert.Equal(3, dto.inner.n);
        }

        [Test]
        public void Bind_JsonNull_ClearsReferenceMembers_AndSkipsValueMembers()
        {
            Defaults dto = BindClean<Defaults>("{\"n\":null,\"s\":null,\"list\":null,\"inner\":null,\"maybe\":null}");
            Assert.Equal(5, dto.n);          // an int cannot be null: left as it is
            Assert.Null(dto.s);
            Assert.Null(dto.list);
            Assert.Null(dto.inner);
            Assert.False(dto.maybe.HasValue);

            Scalars scalars = BindClean<Scalars>("{\"flag\":null,\"i\":null,\"f\":null,\"s\":null}");
            Assert.False(scalars.flag);
            Assert.Equal(0, scalars.i);

            Enums enums = BindClean<Enums>("{\"color\":null,\"maybe\":null,\"list\":null}");
            Assert.Equal(Color.Red, enums.color);
            Assert.Null(enums.list);
        }

        [Test]
        public void Bind_ReplacesDefaultCollections_DoesNotAppend()
        {
            Defaults dto = BindClean<Defaults>("{\"list\":[9]}");
            Assert.Equal(new List<int> { 9 }, dto.list);
            Defaults empty = BindClean<Defaults>("{\"list\":[]}");
            Assert.Equal(0, empty.list.Count);
        }

        // ---------- Bind: collections ----------

        [Test]
        public void Bind_ListsDictionariesNestedDtos()
        {
            Maps maps = BindClean<Maps>("{\"map\":{\"x\":{\"n\":1,\"s\":\"one\"},\"y\":{\"n\":2}},\"counts\":{\"a\":1,\"b\":2},\"lists\":{\"p\":[1,2],\"q\":[]}}");
            Assert.Equal(2, maps.map.Count);
            Assert.Equal(1, maps.map["x"].n);
            Assert.Equal("one", maps.map["x"].s);
            Assert.Equal("x", maps.map["y"].s);   // initializer of Inner survives
            Assert.Equal(new[] { "a", "b" }, maps.counts.Keys.ToArray());
            Assert.Equal(2, maps.counts["b"]);
            Assert.Equal(new List<int> { 1, 2 }, maps.lists["p"]);
            Assert.Equal(0, maps.lists["q"].Count);

            Root root = BindClean<Root>("{\"a\":[{\"b\":1,\"name\":\"first\"},{\"b\":2}]}");
            Assert.Equal(2, root.a.Count);
            Assert.Equal("first", root.a[0].name);
            Assert.Null(root.a[1].name);
        }

        [Test]
        public void Bind_ArraysAndInterfaces()
        {
            Collections c = BindClean<Collections>(
                "{\"ints\":[1,2,3],\"strings\":[\"a\",\"b\"],\"items\":[{\"b\":7}],"
                + "\"ilist\":[4,5],\"ienum\":[\"x\"],\"roList\":[{\"b\":8}],\"icoll\":[6],"
                + "\"idict\":{\"k\":1},\"roDict\":{\"r\":{\"b\":9}},\"set\":[\"s\",\"t\",\"s\"],\"nested\":[[\"n\"]]}");

            Assert.Equal(new[] { 1, 2, 3 }, c.ints);
            Assert.Equal(new[] { "a", "b" }, c.strings);
            Assert.Equal(7, c.items[0].b);
            Assert.True(c.ilist is List<int>);
            Assert.Equal(new[] { 4, 5 }, c.ilist.ToArray());
            Assert.Equal(new[] { "x" }, c.ienum.ToArray());
            Assert.Equal(8, c.roList[0].b);
            Assert.Equal(new[] { 6 }, c.icoll.ToArray());
            Assert.Equal(1, c.idict["k"]);
            Assert.Equal(9, c.roDict["r"].b);
            Assert.Equal(2, c.set.Count);
            Assert.True(c.set.Contains("s") && c.set.Contains("t"));
            Assert.Equal(new List<string> { "n" }, c.nested[0]);
        }

        [Test]
        public void Bind_List_ElementErrors_DropOnlyTheBadElement()
        {
            List<string> errors;
            Collections c = Bind<Collections>("{\"ints\":[1,\"x\",3,null,2.5,4],\"strings\":[\"a\",null,\"b\",7]}", out errors);

            Assert.Equal(new[]
            {
                "$.ints[1]: expected number, got string",
                "$.ints[4]: expected an integer (Int32), got 2.5",
                "$.strings[3]: expected string, got number",
            }, errors.ToArray());
            Assert.Equal(new[] { 1, 3, 0, 4 }, c.ints);                 // null element -> default(int)
            Assert.Equal(new[] { "a", null, "b" }, c.strings);          // null element -> null
        }

        [Test]
        public void Bind_Dictionary_EntryErrors_DropOnlyTheBadEntry()
        {
            List<string> errors;
            Maps maps = Bind<Maps>("{\"counts\":{\"a\":1,\"b\":\"x\",\"c\":3,\"d\":null},\"map\":{\"k\":null}}", out errors);
            Assert.Equal(new[] { "$.counts.b: expected number, got string" }, errors.ToArray());
            Assert.Equal(new[] { "a", "c", "d" }, maps.counts.Keys.ToArray());
            Assert.Equal(0, maps.counts["d"]);
            Assert.Null(maps.map["k"]);
        }

        [Test]
        public void Bind_Errors_AreCapped()
        {
            var sb = new StringBuilder("{\"ints\":[");
            for (int i = 0; i < 1000; i++) sb.Append(i > 0 ? ",\"x\"" : "\"x\"");
            sb.Append("]}");

            List<string> errors;
            Collections c = Bind<Collections>(sb.ToString(), out errors);
            Assert.Equal(101, errors.Count);
            Assert.Equal("$.ints[0]: expected number, got string", errors[0]);
            Assert.Equal("too many errors, the rest is not reported", errors[100]);
            Assert.Equal(0, c.ints.Length);
        }

        // ---------- Bind: JsonNode members ----------

        [Test]
        public void Bind_JsonNodeMembers_AreCopiedAndTyped()
        {
            JsonNode source = Parse("{\"any\":{\"a\":[1,2]},\"obj\":{\"x\":1},\"arr\":[1],\"val\":\"text\",\"options\":{\"o1\":{\"k\":1},\"o2\":null,\"o3\":5},\"params\":{\"p\":true}}");
            List<string> errors = new List<string>();
            Nodes dto = ModJson.Bind<Nodes>(source, errors);

            Assert.Equal(0, errors.Count);
            Assert.True(dto.any is JsonObject);
            Assert.Equal("{\"a\":[1,2]}", Text(dto.any));
            Assert.Equal(1, dto.obj.GetInt32("x"));
            Assert.Equal(1, dto.arr.Count);
            Assert.Equal("text", dto.val.StringValue);
            Assert.Equal(3, dto.options.Count);
            Assert.Equal(1, ((JsonObject)dto.options["o1"]).GetInt32("k"));
            Assert.True(ReferenceEquals(JsonValue.Null, dto.options["o2"]), "a JSON null entry is a null node, not C# null");
            Assert.Equal(5.0, ((JsonValue)dto.options["o3"]).NumberValue);
            Assert.True(dto.@params.GetBool("p"));

            // the DTO owns its nodes: changing them does not touch the source tree
            dto.obj.Set("x", 99);
            ((JsonObject)dto.any).Set("changed", true);
            Assert.Equal(1, ((JsonObject)source).GetObject("obj").GetInt32("x"));
            Assert.False(((JsonObject)source).GetObject("any").ContainsKey("changed"));
            Assert.False(ReferenceEquals(((JsonObject)source)["any"], dto.any));
        }

        [Test]
        public void Bind_JsonNodeMembers_NullAndMismatch()
        {
            Nodes nulls = BindClean<Nodes>("{\"any\":null,\"val\":null,\"obj\":null,\"arr\":null}");
            Assert.True(ReferenceEquals(JsonValue.Null, nulls.any), "JsonNode and JsonValue members take a null node");
            Assert.True(ReferenceEquals(JsonValue.Null, nulls.val));
            Assert.Null(nulls.obj);   // a JSON null clears a JsonObject member
            Assert.Null(nulls.arr);

            List<string> errors;
            Nodes bad = Bind<Nodes>("{\"obj\":[1],\"arr\":{\"x\":1},\"val\":[1],\"@params\":1,\"params\":5}", out errors);
            Assert.Equal(new[]
            {
                "$.obj: expected object, got array",
                "$.arr: expected array, got object",
                "$.val: expected null, boolean, number or string, got array",
                "$.params: expected object, got number",
            }, errors.ToArray());
            Assert.Null(bad.obj);
            Assert.Null(bad.@params);
        }

        // ---------- Bind: enums, nullables, structs, properties ----------

        [Test]
        public void Bind_Enums_ByNameAndNumber()
        {
            Enums dto = BindClean<Enums>("{\"color\":\"green\",\"perm\":\"Read, write\",\"maybe\":\"Blue\",\"list\":[\"Red\",2]}");
            Assert.Equal(Color.Green, dto.color);
            Assert.Equal(Perm.Read | Perm.Write, dto.perm);
            Assert.Equal(Color.Blue, dto.maybe.Value);
            Assert.Equal(new[] { Color.Red, Color.Blue }, dto.list.ToArray());

            Assert.Equal(Perm.Read | Perm.Exec, BindClean<Enums>("{\"perm\":5}").perm);
            Assert.Equal(Perm.None, BindClean<Enums>("{\"perm\":0}").perm);
        }

        [Test]
        public void Bind_Enums_Errors()
        {
            List<string> errors;
            Enums dto = Bind<Enums>("{\"color\":\"purple\",\"perm\":\"Read, Fly\",\"maybe\":7,\"list\":[true]}", out errors);
            Assert.Equal(new[]
            {
                "$.color: \"purple\" is not a value of Color",
                "$.perm: \"Read, Fly\" is not a value of Perm",
                "$.maybe: the value 7 is not a value of Color",
                "$.list[0]: expected string, got boolean",
            }, errors.ToArray());
            Assert.Equal(Color.Red, dto.color);

            Bind<Enums>("{\"color\":\"Red, Green\"}", out errors);
            Assert.Equal(1, errors.Count);   // a list of names only for [Flags] enums

            Bind<Enums>("{\"color\":\"1\"}", out errors);
            Assert.Equal(1, errors.Count);   // numbers go as numbers, not as text
        }

        [Test]
        public void Bind_Nullables()
        {
            Defaults dto = BindClean<Defaults>("{\"maybe\":2.5}");
            Assert.Equal(2.5, dto.maybe.Value);

            List<string> errors;
            Defaults bad = Bind<Defaults>("{\"maybe\":\"x\"}", out errors);
            Assert.Equal(new[] { "$.maybe: expected number, got string" }, errors.ToArray());
            Assert.Equal(7.0, bad.maybe.Value);

            WithStruct withStruct = BindClean<WithStruct>("{\"point\":{\"x\":1,\"y\":2},\"maybePoint\":{\"x\":3}}");
            Assert.Equal(1, withStruct.point.x);
            Assert.Equal(2, withStruct.point.y);
            Assert.Equal(3, withStruct.maybePoint.Value.x);
        }

        [Test]
        public void Bind_Struct_AtTheRoot()
        {
            Point p = BindClean<Point>("{\"x\":5,\"y\":-6}");
            Assert.Equal(5, p.x);
            Assert.Equal(-6, p.y);
        }

        [Test]
        public void Bind_Members_FieldsAndWritableProperties_Only()
        {
            Props before = new Props();
            Props dto = BindClean<Props>(
                "{\"Name\":\"n\",\"Count\":3,\"Id\":\"IGNORED\",\"id\":\"real\",\"Hidden\":9,\"Shared\":5,\"fixedValue\":9,"
                + "\"Constant\":9,\"secret\":9,\"Secret\":9,\"ReadOnlyWithBody\":9,\"Item\":9}");

            Assert.Equal("n", dto.Name);
            Assert.Equal(3, dto.Count);
            Assert.Equal("real", dto.Id);          // computed from the field
            Assert.Equal("real", dto.id);
            Assert.Equal(0, dto.Hidden);           // private setter
            Assert.Equal(0, Props.Shared);         // static
            Assert.Equal(3, dto.fixedValue);       // readonly
            Assert.Equal(4, dto.Secret);           // private field, get-only property
            Assert.Equal(5, dto.ReadOnlyWithBody);
            Assert.Equal(3, before.fixedValue);
        }

        [Test]
        public void Bind_InheritedMembers()
        {
            Derived dto = BindClean<Derived>("{\"baseValue\":1,\"derivedValue\":2}");
            Assert.Equal(1, dto.baseValue);
            Assert.Equal(2, dto.derivedValue);
        }

        [Test]
        public void Bind_Decimal()
        {
            Assert.Equal(0.1m, BindClean<Scalars>("{\"m\":0.1}").m);
            List<string> errors;
            Bind<Scalars>("{\"m\":1e300}", out errors);
            Assert.Equal(new[] { "$.m: the value 1e300 is out of range for Decimal" }, errors.ToArray());
        }

        // ---------- Bind: roots, unsupported, failures ----------

        [Test]
        public void Bind_RootTypes()
        {
            List<string> errors = new List<string>();
            Assert.Equal(new List<string> { "a", "b" }, ModJson.Bind<List<string>>(Parse("[\"a\",\"b\"]"), errors));
            Dictionary<string, int> dict = ModJson.Bind<Dictionary<string, int>>(Parse("{\"x\":1,\"y\":2}"), errors);
            Assert.Equal(2, dict.Count);
            Assert.Equal(5, ModJson.Bind<int>(Parse("5"), errors));
            Assert.Equal(2.5, ModJson.Bind<double>(Parse("2.5"), errors));
            Assert.True(ModJson.Bind<bool>(Parse("true"), errors));
            Assert.Equal(0, errors.Count);

            JsonNode source = Parse("{\"a\":[1]}");
            JsonObject copy = ModJson.Bind<JsonObject>(source, errors);
            Assert.True(copy.Equals(source));
            Assert.False(ReferenceEquals(copy, source));

            JsonObject wrong = ModJson.Bind<JsonObject>(Parse("[1]"), errors);
            Assert.NotNull(wrong);   // a fresh empty object
            Assert.Equal(0, wrong.Count);
            Assert.Equal(new[] { "$: expected object, got array" }, errors.ToArray());
        }

        [Test]
        public void Bind_NullNode_GivesADefaultObject()
        {
            var errors = new List<string>();
            Scalars fromNull = ModJson.Bind<Scalars>(null, errors);
            Scalars fromJsonNull = ModJson.Bind<Scalars>(JsonValue.Null, errors);
            Assert.NotNull(fromNull);
            Assert.NotNull(fromJsonNull);
            Assert.Equal(0, errors.Count);

            // errors may be null
            Scalars noList = ModJson.Bind<Scalars>(Parse("{\"i\":\"x\"}"), null);
            Assert.NotNull(noList);
        }

        [Test]
        public void Bind_NonGeneric()
        {
            var errors = new List<string>();
            Assert.Equal("s", (string)ModJson.Bind(typeof(string), JsonValue.Of("s"), errors));
            var scalars = (Scalars)ModJson.Bind(typeof(Scalars), Parse("{\"i\":4}"), errors);
            Assert.Equal(4, scalars.i);
            Assert.Equal(0, errors.Count);

            Assert.Null(ModJson.Bind(typeof(IDisposable), Parse("{}"), errors));
            Assert.Equal(new[] { "$: unsupported type IDisposable (an interface or abstract class)" }, errors.ToArray());
            Assert.Throws<ArgumentNullException>(() => ModJson.Bind(null, Parse("{}"), errors));

            Assert.NotNull(ModJson.Bind(typeof(Scalars), JsonValue.Null, errors));
            Assert.Null(ModJson.Bind(typeof(string), JsonValue.Null, errors));
        }

        [Test]
        public void Bind_UnsupportedMembers_ReportAndCarryOn()
        {
            List<string> errors;
            Unsupported dto = Bind<Unsupported>(
                "{\"any\":{\"a\":1},\"when\":\"2020-01-01\",\"guid\":\"x\",\"span\":\"1\",\"grid\":[[1]],\"intKeys\":{\"1\":\"a\"},\"ok\":5}", out errors);

            Assert.Equal(new[]
            {
                "$.any: unsupported type Object",
                "$.when: unsupported type DateTime",
                "$.guid: unsupported type Guid",
                "$.span: unsupported type TimeSpan",
                "$.grid: unsupported type Int32[,] (multidimensional array)",
                "$.intKeys: unsupported type Dictionary<Int32, String> (dictionary keys must be strings)",
            }, errors.ToArray());
            Assert.Equal(5, dto.ok);
        }

        [Test]
        public void Bind_FailingConstructorsAndSetters_AreReported()
        {
            List<string> errors;
            Holder holder = Bind<Holder>("{\"bad\":{},\"ok\":3}", out errors);
            Assert.Equal(new[] { "$.bad: cannot create ThrowingCtor: boom" }, errors.ToArray());
            Assert.Null(holder.bad);
            Assert.Equal(3, holder.ok);

            Strict strict = Bind<Strict>("{\"Value\":-1,\"other\":7}", out errors);
            Assert.Equal(new[] { "$.Value: cannot set the member: negative" }, errors.ToArray());
            Assert.Equal(7, strict.other);

            errors.Clear();
            ThrowingCtor none = ModJson.Bind<ThrowingCtor>(Parse("{}"), errors);
            Assert.Null(none);
            Assert.True(errors.Count >= 1 && errors[0].StartsWith("$: cannot create ThrowingCtor: boom"), string.Join("|", errors));
        }

        [Test]
        public void Bind_CyclicTree_StopsAtTheDepthLimit()
        {
            var cyclic = new JsonObject();
            cyclic.Set("v", 1);
            cyclic.Set("child", cyclic);

            var errors = new List<string>();
            Node root = ModJson.Bind<Node>(cyclic, errors);

            Assert.NotNull(root);
            Assert.Equal(1, errors.Count);
            Assert.True(errors[0].Contains("nested too deeply"), errors[0]);
            int chain = 0;
            for (Node n = root; n != null; n = n.child) chain++;
            Assert.True(chain >= 60 && chain <= 66, "chain length " + chain);

            // the same for a hand-built cyclic array bound to a list of lists
            var loop = new JsonArray();
            loop.Add(loop);
            errors.Clear();
            ModJson.Bind<List<List<List<int>>>>(loop, errors);
            Assert.True(errors.Count <= 2);
        }

        // ---------- From ----------

        [Test]
        public void From_Scalars_AllTypes()
        {
            var dto = new Scalars
            {
                flag = true, i = -5, u = uint.MaxValue, l = long.MinValue, ul = ulong.MaxValue, f = 0.1f, d = 1e-7, s = "привет",
                sh = -32768, us = 65535, b = 255, sb = -128, m = 12.5m, c = 'x',
            };
            JsonNode node = ModJson.From(dto);
            Assert.Equal(
                "{\"flag\":true,\"i\":-5,\"u\":4294967295,\"l\":-9223372036854775808,\"ul\":18446744073709551615,\"f\":0.1,\"d\":1e-7,"
                + "\"s\":\"привет\",\"sh\":-32768,\"us\":65535,\"b\":255,\"sb\":-128,\"m\":12.5,\"c\":\"x\"}",
                Text(node));
        }

        [Test]
        public void From_NullMembers_AreWrittenAsNull()
        {
            var dto = new Scalars();   // s stays null
            Assert.True(((JsonObject)ModJson.From(dto))["s"].IsNull);

            var defaults = new Defaults { inner = null, maybe = null, list = null };
            Assert.Equal("{\"n\":5,\"s\":\"def\",\"list\":null,\"inner\":null,\"maybe\":null}", Text(ModJson.From(defaults)));

            // JsonNode members: a C# null is left out, an explicit JSON null node is written
            var nodes = new Nodes { any = JsonValue.Null, options = new Dictionary<string, JsonNode> { { "k", null } } };
            Assert.Equal("{\"any\":null,\"options\":{\"k\":null}}", Text(ModJson.From(nodes)));
            Assert.Equal("[\"a\",null]", Text(ModJson.From(new List<string> { "a", null })));
        }

        [Test]
        public void From_NonFiniteNumbers_BecomeNullWithAnError()
        {
            var errors = new List<string>();
            JsonNode node = ModJson.From(new Scalars { f = float.NaN, d = double.PositiveInfinity, i = 1 }, errors);
            Assert.Equal(new[]
            {
                "$.f: NaN and infinity cannot be written in JSON, written as null",
                "$.d: NaN and infinity cannot be written in JSON, written as null",
            }, errors.ToArray());
            var obj = (JsonObject)node;
            Assert.True(obj["f"].IsNull && obj["d"].IsNull);
            Assert.Equal(1, obj.GetInt32("i"));
        }

        [Test]
        public void From_Floats_UseTheirShortestText()
        {
            Assert.Equal("0.1", Text(ModJson.From(0.1f)));
            Assert.Equal("[0.1,0.2,0.3,16777216,3.4028235e38]", Text(ModJson.From(new[] { 0.1f, 0.2f, 0.3f, 16777216f, float.MaxValue })));
            Assert.Equal("0.1", Text(JsonValue.Of(0.1f)));

            // and binding reads the same float back
            Assert.Equal(0.1f, BindClean<Scalars>(Text(ModJson.From(new Scalars { f = 0.1f }))).f);
            foreach (float f in new[] { 1.1f, 2.7182817f, 1e-7f, 123456.79f, 3.14159274f, float.Epsilon })
                Assert.Equal(f, BindClean<Scalars>(Text(ModJson.From(new Scalars { f = f }))).f);
        }

        [Test]
        public void From_Enums()
        {
            var dto = new Enums { color = Color.Green, perm = Perm.Read | Perm.Write, list = new List<Color> { Color.Red, Color.Blue, (Color)9 } };
            Assert.Equal("{\"color\":\"Green\",\"perm\":\"Read, Write\",\"maybe\":null,\"list\":[\"Red\",\"Blue\",9]}", Text(ModJson.From(dto)));
            Assert.Equal("{\"color\":\"Red\",\"perm\":\"None\",\"maybe\":null,\"list\":null}", Text(ModJson.From(new Enums())));
            Assert.Equal("\"Blue\"", Text(ModJson.From(Color.Blue)));
        }

        [Test]
        public void From_Collections()
        {
            var c = new Collections
            {
                ints = new[] { 1, 2 },
                strings = new[] { "a" },
                items = new[] { new Item { b = 3, name = "n" } },
                set = new HashSet<string> { "only" },
                idict = new Dictionary<string, int> { { "z", 1 }, { "a", 2 }, { "m", 3 } },
                nested = new List<List<string>> { new List<string> { "x" }, new List<string>() },
            };
            Assert.Equal(
                "{\"ints\":[1,2],\"strings\":[\"a\"],\"items\":[{\"b\":3,\"name\":\"n\"}],\"ilist\":null,\"ienum\":null,\"roList\":null,\"icoll\":null,"
                + "\"idict\":{\"z\":1,\"a\":2,\"m\":3},\"roDict\":null,\"set\":[\"only\"],\"nested\":[[\"x\"],[]]}",
                Text(ModJson.From(c)));
        }

        [Test]
        public void From_JsonNodeMembers_AreCopies()
        {
            var inner = new JsonObject().Set("k", 1);
            var dto = new Nodes { any = inner, obj = inner, @params = new JsonObject().Set("p", true) };
            var node = (JsonObject)ModJson.From(dto);

            // arr and val are JsonNode members that are null: left out; options is a null dictionary: written as null
            Assert.Equal("{\"any\":{\"k\":1},\"obj\":{\"k\":1},\"options\":null,\"params\":{\"p\":true}}", Text(node));
            inner.Set("k", 2);
            Assert.Equal(1, node.GetObject("any").GetInt32("k"));
            Assert.False(ReferenceEquals(node["any"], node["obj"]));
        }

        [Test]
        public void From_Members_FieldsAndWritableProperties_Only()
        {
            var dto = new Props { id = "real", Name = "n", Count = 3 };
            Assert.Equal("{\"id\":\"real\",\"Name\":\"n\",\"Count\":3}", Text(ModJson.From(dto)));
            Assert.Equal("{\"baseValue\":1,\"derivedValue\":2}", Text(ModJson.From(new Derived { baseValue = 1, derivedValue = 2 })));
            Assert.Equal("{\"x\":1,\"y\":2}", Text(ModJson.From(new Point { x = 1, y = 2 })));
        }

        [Test]
        public void From_Roots()
        {
            Assert.True(ModJson.From(null).IsNull);
            Assert.Equal("\"x\"", Text(ModJson.From("x")));
            Assert.Equal("5", Text(ModJson.From(5)));
            Assert.Equal("true", Text(ModJson.From(true)));
            Assert.Equal("[1,2]", Text(ModJson.From(new List<int> { 1, 2 })));
            Assert.Equal("{\"a\":1}", Text(ModJson.From(new Dictionary<string, int> { { "a", 1 } })));
            Assert.Equal("\"x\"", Text(ModJson.From('x')));
            Assert.Equal("12.5", Text(ModJson.From(12.5m)));

            JsonNode source = Parse("{\"a\":[1]}");
            JsonNode copy = ModJson.From(source);
            Assert.True(copy.Equals(source));
            Assert.False(ReferenceEquals(copy, source));
        }

        [Test]
        public void From_Unsupported_ReportsAndCarriesOn()
        {
            var errors = new List<string>();
            var dto = new Unsupported
            {
                any = new object(), when = new DateTime(2020, 1, 1), guid = Guid.NewGuid(), span = TimeSpan.FromSeconds(1),
                grid = new int[2, 2], intKeys = new Dictionary<int, string> { { 1, "a" } }, ok = 5,
            };
            JsonNode node = ModJson.From(dto, errors);

            Assert.Equal(new[]
            {
                "$.any: unsupported type Object",
                "$.when: unsupported type DateTime",
                "$.guid: unsupported type Guid",
                "$.span: unsupported type TimeSpan",
                "$.grid: unsupported type Int32[,] (multidimensional array)",
                "$.intKeys: unsupported dictionary key type Int32",
            }, errors.ToArray());
            Assert.Equal("{\"intKeys\":{},\"ok\":5}", Text(node));

            errors.Clear();
            Assert.True(ModJson.From(new object(), errors).IsNull);
            Assert.Equal(1, errors.Count);
        }

        [Test]
        public void From_GetterThatThrows_IsReported()
        {
            var errors = new List<string>();
            JsonNode node = ModJson.From(new BadGetter(), errors);
            Assert.Equal(new[] { "$.Bad: cannot read the member: cannot read" }, errors.ToArray());
            Assert.Equal("{\"good\":1,\"alsoGood\":2}", Text(node));
        }

        [Test]
        public void From_CyclicDto_StopsAtTheDepthLimit()
        {
            var loop = new Node { v = 1 };
            loop.child = loop;

            var errors = new List<string>();
            JsonNode node = ModJson.From(loop, errors);

            Assert.Equal(1, errors.Count);
            Assert.True(errors[0].Contains("nested deeper than 64"), errors[0]);
            string text = Text(node);   // nesting is within the limit, so it can be written (and parsed back)
            Assert.NotNull(Parse(text));
            Assert.Equal(64, text.Count(ch => ch == '{'));
        }

        [Test]
        public void From_NeverThrows_ForOddInput()
        {
            Assert.True(ModJson.From(new Action(() => { })).IsNull);
            Assert.True(ModJson.From(typeof(string)).IsNull);
            Assert.True(ModJson.From(IntPtr.Zero).IsNull);
            ModJson.From(new Exception("x"));
            ModJson.From(new System.Text.StringBuilder("abc"));
            ModJson.From(Environment.NewLine);
        }

        // ---------- round trips ----------

        private static Big SampleBig()
        {
            return new Big
            {
                flag = true, i = -42, u = uint.MaxValue, l = 9007199254740993L, ul = ulong.MaxValue, f = 0.1f, d = 1.0 / 3, s = "Процессор \"Фотон\"\n\t/ \\",
                m = 12.25m, c = 'я', color = Color.Blue, perm = Perm.Read | Perm.Exec, maybe = 7, maybeColor = null,
                items = new List<Item> { new Item { b = 1, name = "one" }, new Item { b = 2 } },
                map = new Dictionary<string, Inner> { { "ai:1", new Inner { n = 5, s = "five" } }, { "player", new Inner() } },
                ints = new[] { 3, 1, 2 }, set = new HashSet<string> { "solo" }, inner = new Inner { n = -1, s = null },
                any = new JsonObject().Set("unknown", new JsonArray().Add(1).Add(new JsonObject().Set("deep", JsonValue.Null))),
                point = new Point { x = 4, y = 5 },
                grid = new List<List<int>> { new List<int> { 1, 2 }, new List<int>(), new List<int> { 3 } },
                lists = new Dictionary<string, List<string>> { { "k", new List<string> { "a", "b" } } },
                strings = new List<string> { "a", null, "c" },
                Title = "title", Ratio = 2.5,
            };
        }

        private static void AssertSameBig(Big expected, Big actual)
        {
            Assert.Equal(expected.flag, actual.flag);
            Assert.Equal(expected.i, actual.i);
            Assert.Equal(expected.u, actual.u);
            Assert.Equal(expected.l, actual.l);
            Assert.Equal(expected.ul, actual.ul);
            Assert.Equal(expected.f, actual.f);
            Assert.Equal(expected.d, actual.d);
            Assert.Equal(expected.s, actual.s);
            Assert.Equal(expected.m, actual.m);
            Assert.Equal(expected.c, actual.c);
            Assert.Equal(expected.color, actual.color);
            Assert.Equal(expected.perm, actual.perm);
            Assert.Equal(expected.maybe, actual.maybe);
            Assert.Equal(expected.maybeColor, actual.maybeColor);
            Assert.Equal(expected.point.x, actual.point.x);
            Assert.Equal(expected.point.y, actual.point.y);
            Assert.Equal(expected.Title, actual.Title);
            Assert.Equal(expected.Ratio, actual.Ratio);
            Assert.Equal(expected.ints, actual.ints);
            Assert.Equal(expected.strings, actual.strings);
            Assert.Equal(expected.grid, actual.grid);
            if (expected.set == null) Assert.Null(actual.set);
            else Assert.True(expected.set.SetEquals(actual.set));
            if (expected.any == null) Assert.Null(actual.any);
            else Assert.True(expected.any.Equals(actual.any));
            Assert.Equal(expected.lists.Keys.ToArray(), actual.lists.Keys.ToArray());
            foreach (string key in expected.lists.Keys) Assert.Equal(expected.lists[key], actual.lists[key]);

            // nested DTO collections: compare through their JSON form
            Assert.True(ModJson.From(expected.items).Equals(ModJson.From(actual.items)), "items");
            Assert.True(ModJson.From(expected.map).Equals(ModJson.From(actual.map)), "map");
            Assert.Equal(expected.map.Keys.ToArray(), actual.map.Keys.ToArray());
            if (expected.inner == null) Assert.Null(actual.inner);
            else Assert.True(ModJson.From(expected.inner).Equals(ModJson.From(actual.inner)), "inner");
            Assert.True(ModJson.From(expected).Equals(ModJson.From(actual)), "whole object");
        }

        [Test]
        public void RoundTrip_FromThenBind_GivesAnEqualObject()
        {
            Big original = SampleBig();
            var errors = new List<string>();
            JsonNode tree = ModJson.From(original, errors);
            Big copy = ModJson.Bind<Big>(tree, errors);

            Assert.Equal(0, errors.Count, string.Join(" | ", errors));
            AssertSameBig(original, copy);
            Assert.False(ReferenceEquals(original.items, copy.items));
        }

        [Test]
        public void RoundTrip_ThroughText_Compact_AndPretty()
        {
            Big original = SampleBig();
            foreach (bool pretty in new[] { false, true })
            {
                string text = ModJson.Write(ModJson.From(original), pretty);
                var errors = new List<string>();
                Big copy = ModJson.Bind<Big>(Parse(text), errors);

                Assert.Equal(0, errors.Count, string.Join(" | ", errors));
                AssertSameBig(original, copy);
                Assert.Equal(text, ModJson.Write(ModJson.From(copy), pretty), "the text is stable");
            }
        }

        [Test]
        public void RoundTrip_LongIdsKeepEveryDigit()
        {
            var dto = new Scalars { l = 9007199254740993L, ul = 18446744073709551615UL };
            Scalars copy = BindClean<Scalars>(ModJson.Write(ModJson.From(dto), false));
            Assert.Equal(9007199254740993L, copy.l);
            Assert.Equal(18446744073709551615UL, copy.ul);
        }

        [Test]
        public void RoundTrip_SaveSectionLikeDto()
        {
            // The shape the feature specs use for their ModSave sections: plain classes, lists instead of dictionaries.
            const string section =
                "{\"seeded\":true,\"items\":[{\"b\":1,\"name\":\"ai:0\"},{\"b\":2,\"name\":\"player\"}],"
                + "\"map\":{\"ai:1\":{\"n\":3,\"s\":\"x\"}},\"future\":{\"_schema\":9,\"kept\":[1,2,3]}}";
            JsonNode tree = Parse(section);
            Big bound = ModJson.Bind<Big>(tree, null);   // unknown members are simply not there
            Assert.Equal(2, bound.items.Count);
            Assert.Equal("player", bound.items[1].name);
            Assert.Equal(3, bound.map["ai:1"].n);
        }

        // ---------- randomised ----------

        private static Big RandomBig(Random rng)
        {
            string[] words = { "alpha", "beta", "gamma", "Процессор", "a\"b", "tab\t", "", "line\nbreak", "/slash\\" };
            Func<string> word = () => words[rng.Next(words.Length)];

            var big = new Big
            {
                flag = rng.Next(2) == 0, i = rng.Next(int.MinValue, int.MaxValue), u = (uint)rng.Next() * 2u + (uint)rng.Next(2),
                l = ((long)rng.Next() << 32) | (uint)rng.Next(), ul = ((ulong)rng.Next() << 33) ^ (ulong)rng.Next(),
                f = (float)((rng.NextDouble() - 0.5) * Math.Pow(10, rng.Next(-8, 8))), d = (rng.NextDouble() - 0.5) * Math.Pow(10, rng.Next(-30, 30)),
                s = rng.Next(5) == 0 ? null : word() + word(), m = rng.Next(-1000, 1000) / 4m, c = (char)rng.Next('a', 'z'),
                color = (Color)rng.Next(3), perm = (Perm)rng.Next(8), maybe = rng.Next(2) == 0 ? (int?)null : rng.Next(),
                maybeColor = rng.Next(2) == 0 ? (Color?)null : (Color)rng.Next(3),
                point = new Point { x = rng.Next(), y = -rng.Next() }, Title = word(), Ratio = rng.NextDouble(),
                items = new List<Item>(), map = new Dictionary<string, Inner>(), lists = new Dictionary<string, List<string>>(),
            };
            int count = rng.Next(0, 6);
            for (int k = 0; k < count; k++) big.items.Add(new Item { b = rng.Next(), name = rng.Next(3) == 0 ? null : word() });
            count = rng.Next(0, 5);
            for (int k = 0; k < count; k++) big.map["key" + rng.Next(1000) + word()] = new Inner { n = rng.Next(), s = rng.Next(3) == 0 ? null : word() };
            count = rng.Next(0, 5);
            for (int k = 0; k < count; k++) big.lists["l" + k] = Enumerable.Range(0, rng.Next(0, 4)).Select(_ => word()).ToList();
            if (rng.Next(2) == 0) big.ints = Enumerable.Range(0, rng.Next(0, 6)).Select(_ => rng.Next(-100, 100)).ToArray();
            if (rng.Next(2) == 0) big.set = new HashSet<string>(Enumerable.Range(0, rng.Next(0, 4)).Select(_ => word() + rng.Next(50)));
            if (rng.Next(2) == 0) big.inner = new Inner { n = rng.Next(), s = word() };
            if (rng.Next(2) == 0) big.strings = Enumerable.Range(0, rng.Next(0, 4)).Select(_ => rng.Next(4) == 0 ? null : word()).ToList();
            if (rng.Next(2) == 0) big.grid = Enumerable.Range(0, rng.Next(0, 3)).Select(_ => Enumerable.Range(0, rng.Next(0, 3)).Select(__ => rng.Next(100)).ToList()).ToList();
            if (rng.Next(2) == 0) big.any = new JsonObject().Set("x", new JsonArray().Add(rng.Next()).Add(word()).Add(JsonValue.Null)).Set("y", rng.NextDouble());
            if (big.lists.Count == 0) big.lists = new Dictionary<string, List<string>>();
            return big;
        }

        [Test]
        public void RoundTrip_RandomDtos()
        {
            var rng = new Random(424242);
            for (int n = 0; n < 400; n++)
            {
                Big original = RandomBig(rng);
                if (original.ints == null) original.ints = new int[0];
                if (original.strings == null) original.strings = new List<string>();
                if (original.grid == null) original.grid = new List<List<int>>();

                bool pretty = n % 2 == 0;
                string text = ModJson.Write(ModJson.From(original), pretty);
                var errors = new List<string>();
                Big copy = ModJson.Bind<Big>(Parse(text), errors);

                Assert.Equal(0, errors.Count, "#" + n + ": " + string.Join(" | ", errors));
                AssertSameBig(original, copy);
            }
        }

        private static JsonNode RandomTree(Random rng, int depth)
        {
            string[] keys = { "flag", "i", "u", "l", "ul", "f", "d", "s", "items", "map", "ints", "set", "inner", "any", "point", "grid",
                              "lists", "strings", "color", "perm", "maybe", "Title", "Ratio", "b", "name", "n", "x", "y", "zzz", "" };
            int kind = rng.Next(depth > 4 ? 4 : 7);
            switch (kind)
            {
                case 0: return JsonValue.Null;
                case 1: return JsonValue.Of(rng.Next(2) == 0);
                case 2: return JsonValue.Of(rng.Next(5) == 0 ? rng.NextDouble() * 1e20 : rng.Next(-1000, 1000) / (rng.Next(3) == 0 ? 4.0 : 1.0));
                case 3: return JsonValue.Of(new[] { "", "a", "Red", "Read, Write", "Blue", "x", "я", "0" }[rng.Next(8)]);
                case 4:
                case 5:
                    {
                        var obj = new JsonObject();
                        int count = rng.Next(0, 6);
                        for (int k = 0; k < count; k++) obj.Set(keys[rng.Next(keys.Length)], RandomTree(rng, depth + 1));
                        return obj;
                    }
                default:
                    {
                        var array = new JsonArray();
                        int count = rng.Next(0, 5);
                        for (int k = 0; k < count; k++) array.Add(RandomTree(rng, depth + 1));
                        return array;
                    }
            }
        }

        [Test]
        public void Fuzz_RandomTrees_NeverThrow_AndAlwaysGiveAnObject()
        {
            var rng = new Random(2026);
            int withErrors = 0;
            for (int n = 0; n < 3000; n++)
            {
                JsonNode tree = RandomTree(rng, 0);
                try
                {
                    var errors = new List<string>();
                    Assert.NotNull(ModJson.Bind<Big>(tree, errors));
                    Assert.NotNull(ModJson.Bind<Collections>(tree, errors));
                    Assert.NotNull(ModJson.Bind<Scalars>(tree, errors));
                    Assert.NotNull(ModJson.Bind<Maps>(tree, errors));
                    Assert.NotNull(ModJson.Bind<Nodes>(tree, errors));
                    Assert.NotNull(ModJson.Bind<Enums>(tree, errors));
                    Assert.NotNull(ModJson.Bind<Defaults>(tree, errors));
                    ModJson.Bind<List<Item>>(tree, errors);
                    ModJson.Bind<Dictionary<string, Inner>>(tree, errors);
                    ModJson.Bind<Dictionary<string, List<int>>>(tree, errors);
                    foreach (string error in errors) Assert.True(error.StartsWith("$") && error.Length > 3, "error text: " + error);
                    if (errors.Count > 0) withErrors++;
                }
                catch (AssertFailedException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new AssertFailedException("Bind threw " + ex.GetType().Name + " for " + tree + ": " + ex.Message);
                }
            }
            Assert.True(withErrors > 500, "the random trees must provoke errors, saw " + withErrors);
        }

        [Test]
        public void Fuzz_BoundTreesSurviveFromAndWrite()
        {
            // Whatever the binder accepted from random input must convert back to JSON and parse again.
            var rng = new Random(99);
            for (int n = 0; n < 1500; n++)
            {
                JsonNode tree = RandomTree(rng, 0);
                var errors = new List<string>();
                Big bound = ModJson.Bind<Big>(tree, errors);
                JsonNode again = ModJson.From(bound, errors);
                string text = ModJson.Write(again, n % 2 == 0);
                Assert.True(again.Equals(Parse(text)), "round trip of the bound object, #" + n);
            }
        }

        // ---------- speed ----------

        [Test]
        public void Speed_BindingALargeSection()
        {
            var items = new JsonArray();
            for (int i = 0; i < 40000; i++) items.Add(new JsonObject().Set("b", i).Set("name", "item " + i));
            JsonNode tree = new JsonObject().Set("a", items);

            var watch = Stopwatch.StartNew();
            var errors = new List<string>();
            Root root = ModJson.Bind<Root>(tree, errors);
            long bindMs = watch.ElapsedMilliseconds;

            watch.Restart();
            JsonNode back = ModJson.From(root);
            long fromMs = watch.ElapsedMilliseconds;

            Assert.Equal(0, errors.Count);
            Assert.Equal(40000, root.a.Count);
            Assert.Equal(39999, root.a[39999].b);
            Assert.True(back.Equals(tree));
            Assert.True(bindMs < 5000 && fromMs < 5000, "bind " + bindMs + " ms, from " + fromMs + " ms");
        }
    }
}
