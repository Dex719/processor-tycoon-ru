using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PTMod.Core.Logic.Json;

namespace PTMod.Tests.Json
{
    /// <summary>
    /// ModJson parser and writer (TSK-009): every JSON type, escapes and unicode, numbers (1e-7, 4294967295, -0), errors with
    /// line and column, the 8 MiB / depth 64 limits, order-preserving round trips, and 10 000 random inputs that must never throw.
    /// Source text avoids unicode escapes: a '~' in a test string stands for a backslash (see <see cref="Esc"/>).
    /// </summary>
    [Covers(typeof(ModJson), typeof(JsonError))]
    public class ModJsonTests
    {
        // ---------- helpers ----------

        /// <summary>'~' becomes a backslash: writes JSON escapes without doubling every backslash.</summary>
        private static string Esc(string text)
        {
            return text.Replace('~', '\\');
        }

        private static JsonNode Parse(string text)
        {
            JsonNode node;
            JsonError error;
            bool ok = ModJson.TryParse(text, out node, out error);
            Assert.True(ok, "expected a successful parse of " + Shown(text) + ", error: " + error);
            Assert.NotNull(node);
            Assert.Null(error);
            return node;
        }

        private static JsonError Fail(string text)
        {
            JsonNode node;
            JsonError error;
            bool ok = ModJson.TryParse(text, out node, out error);
            Assert.False(ok, "expected a parse error for " + Shown(text));
            Assert.Null(node);
            Assert.NotNull(error);
            return error;
        }

        private static void AssertError(string text, int line, int column, string messagePart)
        {
            JsonError error = Fail(text);
            Assert.True(error.Line == line && error.Column == column,
                "error position for " + Shown(text) + ": expected " + line + ":" + column + ", actual " + error.Line + ":" + error.Column + " (" + error.Message + ")");
            Assert.True(error.Message.IndexOf(messagePart, StringComparison.Ordinal) >= 0,
                "message for " + Shown(text) + " should contain \"" + messagePart + "\", actual: " + error.Message);
        }

        private static string Shown(string text)
        {
            if (text == null) return "null";
            var sb = new StringBuilder("\"");
            for (int i = 0; i < text.Length && i < 80; i++)
            {
                char c = text[i];
                if (c < ' ' || c > '~') sb.Append("<").Append(((int)c).ToString("x", CultureInfo.InvariantCulture)).Append(">");
                else sb.Append(c);
            }
            if (text.Length > 80) sb.Append("...");
            return sb.Append("\"").ToString();
        }

        private static string Compact(JsonNode node)
        {
            return ModJson.Write(node, false);
        }

        private static bool IsNegativeZero(double d)
        {
            return d == 0 && BitConverter.DoubleToInt64Bits(d) < 0;
        }

        private static string Nested(string open, string close, int depth)
        {
            return string.Concat(Enumerable.Repeat(open, depth)) + "1" + string.Concat(Enumerable.Repeat(close, depth));
        }

        // ---------- parse: types ----------

        [Test]
        public void Parse_Literals()
        {
            Assert.Equal(JsonKind.Null, Parse("null").Kind);
            Assert.True(Parse("null").IsNull);

            var yes = (JsonValue)Parse("true");
            Assert.Equal(JsonKind.Bool, yes.Kind);
            Assert.True(yes.BoolValue);

            var no = (JsonValue)Parse(" false ");
            Assert.Equal(JsonKind.Bool, no.Kind);
            Assert.False(no.BoolValue);
        }

        [Test]
        public void Parse_AnyValueAtTheRoot()
        {
            Assert.Equal("abc", ((JsonValue)Parse("\"abc\"")).StringValue);
            Assert.Equal(42.0, ((JsonValue)Parse("42")).NumberValue);
            Assert.True(Parse("[]") is JsonArray);
            Assert.True(Parse("{}") is JsonObject);
        }

        [Test]
        public void Parse_Strings_Plain()
        {
            Assert.Equal("", ((JsonValue)Parse("\"\"")).StringValue);
            Assert.Equal("hello world", ((JsonValue)Parse("\"hello world\"")).StringValue);
            Assert.Equal("a/b", ((JsonValue)Parse("\"a/b\"")).StringValue);
            Assert.Equal(JsonKind.String, Parse("\"x\"").Kind);
        }

        [Test]
        public void Parse_Numbers()
        {
            var cases = new[]
            {
                new KeyValuePair<string, double>("0", 0),
                new KeyValuePair<string, double>("1", 1),
                new KeyValuePair<string, double>("-1", -1),
                new KeyValuePair<string, double>("123456789012345", 123456789012345.0),
                new KeyValuePair<string, double>("1.5", 1.5),
                new KeyValuePair<string, double>("-1.5e+3", -1500),
                new KeyValuePair<string, double>("1E2", 100),
                new KeyValuePair<string, double>("1e2", 100),
                new KeyValuePair<string, double>("1e-7", 1e-7),
                new KeyValuePair<string, double>("1E-7", 1e-7),
                new KeyValuePair<string, double>("4294967295", 4294967295.0),
                new KeyValuePair<string, double>("0.1", 0.1),
                new KeyValuePair<string, double>("0.0", 0),
                new KeyValuePair<string, double>("0e0", 0),
                new KeyValuePair<string, double>("1e308", 1e308),
                new KeyValuePair<string, double>("1.7976931348623157e308", double.MaxValue),
                new KeyValuePair<string, double>("5e-324", double.Epsilon),
                new KeyValuePair<string, double>("1e-400", 0),
                new KeyValuePair<string, double>("12345678.12345678", 12345678.12345678),
            };

            foreach (KeyValuePair<string, double> c in cases)
            {
                JsonNode node = Parse(c.Key);
                Assert.Equal(JsonKind.Number, node.Kind, c.Key);
                Assert.True(((JsonValue)node).NumberValue == c.Value, c.Key + " -> " + ((JsonValue)node).NumberValue.ToString("R", CultureInfo.InvariantCulture));
            }
        }

        [Test]
        public void Parse_NegativeZero_KeepsItsSign()
        {
            foreach (string text in new[] { "-0", "-0.0", "-0e0", "-0E+5" })
            {
                double d = ((JsonValue)Parse(text)).NumberValue;
                Assert.True(IsNegativeZero(d), text + " must be negative zero");
            }
            Assert.False(IsNegativeZero(((JsonValue)Parse("0")).NumberValue));
            Assert.Equal("-0", Compact(Parse("-0")));
            Assert.Equal("0", Compact(Parse("0")));
        }

        [Test]
        public void Parse_UInt32Max_IsExact()
        {
            var value = (JsonValue)Parse("4294967295");
            Assert.Equal(4294967295.0, value.NumberValue);
            long wide;
            Assert.True(value.TryGetInt64(out wide));
            Assert.Equal(4294967295L, wide);
            int narrow;
            Assert.False(value.TryGetInt32(out narrow));
            Assert.Equal("4294967295", Compact(value));
        }

        [Test]
        public void Parse_LongIntegers_KeepExactText()
        {
            // 2^53 + 1 does not fit a double; the parser keeps the literal, so ids and counters survive.
            var big = (JsonValue)Parse("9007199254740993");
            long l;
            Assert.True(big.TryGetInt64(out l));
            Assert.Equal(9007199254740993L, l);
            Assert.Equal("9007199254740993", Compact(big));

            var ulongMax = (JsonValue)Parse("18446744073709551615");
            ulong u;
            Assert.True(ulongMax.TryGetUInt64(out u));
            Assert.Equal(ulong.MaxValue, u);
            Assert.False(ulongMax.TryGetInt64(out l));
            Assert.Equal("18446744073709551615", Compact(ulongMax));

            var longMin = (JsonValue)Parse("-9223372036854775808");
            Assert.True(longMin.TryGetInt64(out l));
            Assert.Equal(long.MinValue, l);
            Assert.Equal("-9223372036854775808", Compact(longMin));

            // beyond UInt64: still a finite number, readable as double only
            var huge = (JsonValue)Parse("123456789012345678901234567890");
            Assert.False(huge.TryGetUInt64(out u));
            Assert.Equal("123456789012345678901234567890", Compact(huge));
        }

        [Test]
        public void Parse_Numbers_Rejected()
        {
            string[] bad =
            {
                "01", "-01", "00", "-", "1.", ".5", "+1", "1e", "1e+", "1E-", "--1", "0x10", "1.5.2", "1.e5", "- 1",
                "NaN", "Infinity", "-Infinity", "1e999", "-1e999", "1e400", "1,5",
            };
            foreach (string text in bad)
            {
                JsonError error = Fail(text);
                Assert.True(error.Line == 1 && error.Column >= 1, "position of the error for " + text);
            }
        }

        [Test]
        public void Parse_Number_ErrorPositions()
        {
            AssertError("01", 1, 2, "leading zeros");
            AssertError("1.", 1, 3, "a digit after '.'");
            AssertError("-", 1, 2, "a digit is expected");
            AssertError("1e+", 1, 4, "exponent");
            AssertError("[1e999]", 1, 2, "out of range");
            AssertError(".5", 1, 1, "unexpected character '.'");
        }

        // ---------- parse: strings ----------

        [Test]
        public void Parse_Strings_Escapes()
        {
            string text = Esc("\"a~\"b~~c~/d~be~ff~ng~rh~ti\"");
            Assert.Equal("a\"b\\c/d\be\ff\ng\rh\ti", ((JsonValue)Parse(text)).StringValue);
        }

        [Test]
        public void Parse_Strings_UnicodeEscapes()
        {
            // ~u0041 ~u00e9 ~u044F  (upper and lower case hex digits)
            string expected = new string(new[] { 'A', (char)0xE9, (char)0x44F, (char)0x2028 });
            Assert.Equal(expected, ((JsonValue)Parse(Esc("\"~u0041~u00e9~u044F~u2028\""))).StringValue);

            // a surrogate pair written as two escapes is one emoji
            string pile = char.ConvertFromUtf32(0x1F600);
            Assert.Equal(pile, ((JsonValue)Parse(Esc("\"~uD83D~uDE00\""))).StringValue);

            // a lone surrogate escape is accepted as is (the writer keeps it as an escape)
            string lone = ((JsonValue)Parse(Esc("\"~ud800\""))).StringValue;
            Assert.Equal(1, lone.Length);
            Assert.Equal(0xD800, (int)lone[0]);

            // control characters by escape
            string control = ((JsonValue)Parse(Esc("\"~u0001\""))).StringValue;
            Assert.Equal(1, control.Length);
            Assert.Equal(1, (int)control[0]);
        }

        [Test]
        public void Parse_Strings_RawUnicode()
        {
            const string russian = "Привет, мир! Процессор «Фотон»";
            Assert.Equal(russian, ((JsonValue)Parse("\"" + russian + "\"")).StringValue);

            string emoji = char.ConvertFromUtf32(0x1F680) + "x" + char.ConvertFromUtf32(0x10FFFF);
            Assert.Equal(emoji, ((JsonValue)Parse("\"" + emoji + "\"")).StringValue);

            // DEL and U+2028 are legal inside a JSON string
            string odd = "a" + (char)0x7F + (char)0x2028 + "b";
            Assert.Equal(odd, ((JsonValue)Parse("\"" + odd + "\"")).StringValue);
        }

        [Test]
        public void Parse_Strings_KeysCanHaveEscapes()
        {
            var obj = (JsonObject)Parse(Esc("{\"a~nb\":1,\"c~\"d\":2,\"~u0041\":3}"));
            Assert.Equal(new[] { "a\nb", "c\"d", "A" }, obj.Keys.ToArray());
        }

        // ---------- parse: structure ----------

        [Test]
        public void Parse_Whitespace()
        {
            var array = (JsonArray)Parse(" \t\r\n[ 1 ,\n2\t,\r\n3 ]\r\n ");
            Assert.Equal(3, array.Count);

            // only the four JSON whitespace characters are blanks
            foreach (char odd in new[] { '\f', '\v', (char)0xA0, (char)0x2003 })
            {
                JsonError error = Fail("[1," + odd + "2]");
                Assert.Equal(1, error.Line);
                Assert.Equal(4, error.Column);
                Assert.True(error.Message.Contains("U+"), "unprintable characters are shown as U+XXXX: " + error.Message);
            }
        }

        [Test]
        public void Parse_LeadingBom_IsIgnored()
        {
            string bom = ((char)0xFEFF).ToString();
            var obj = (JsonObject)Parse(bom + "{\"a\":1}");
            Assert.Equal(1, obj.Count);

            // positions do not count the BOM
            AssertError(bom + "?", 1, 1, "unexpected character '?'");
            AssertError(bom, 1, 1, "unexpected end of input");
        }

        [Test]
        public void Parse_EmptyContainers()
        {
            Assert.Equal(0, ((JsonObject)Parse("{}")).Count);
            Assert.Equal(0, ((JsonObject)Parse("{ \n }")).Count);
            Assert.Equal(0, ((JsonArray)Parse("[]")).Count);
            Assert.Equal(0, ((JsonArray)Parse("[ \n ]")).Count);

            var nested = (JsonArray)Parse("[[],{},[[]]]");
            Assert.Equal(3, nested.Count);
            Assert.True(nested[1] is JsonObject);
        }

        [Test]
        public void Parse_NestedDocument()
        {
            string text = "{\"name\":\"Фотон\",\"year\":1985,\"price\":12.5,\"flags\":[true,false,null],"
                          + "\"stats\":{\"ips\":1e3,\"tags\":[\"a\",\"b\"],\"extra\":{}},\"empty\":[]}";
            var root = (JsonObject)Parse(text);

            Assert.Equal("Фотон", root.GetString("name"));
            Assert.Equal(1985, root.GetInt32("year"));
            Assert.Equal(12.5, root.GetDouble("price"));

            JsonArray flags = root.GetArray("flags");
            Assert.Equal(3, flags.Count);
            Assert.True(((JsonValue)flags[0]).BoolValue);
            Assert.False(((JsonValue)flags[1]).BoolValue);
            Assert.True(flags[2].IsNull);

            JsonObject stats = root.GetObject("stats");
            Assert.Equal(1000.0, stats.GetDouble("ips"));
            Assert.Equal(2, stats.GetArray("tags").Count);
            Assert.Equal(0, stats.GetObject("extra").Count);
            Assert.Equal(0, root.GetArray("empty").Count);
        }

        [Test]
        public void Parse_ObjectKeys_KeepTextOrder()
        {
            var obj = (JsonObject)Parse("{\"z\":1,\"a\":2,\"m\":3,\"b\":{\"y\":1,\"x\":2}}");
            Assert.Equal(new[] { "z", "a", "m", "b" }, obj.Keys.ToArray());
            Assert.Equal(new[] { "y", "x" }, obj.GetObject("b").Keys.ToArray());

            // more members than the linear-search threshold: order and lookups still right
            var text = new StringBuilder("{");
            var expected = new List<string>();
            for (int i = 0; i < 40; i++)
            {
                string key = "k" + ((i * 7919) % 101).ToString(CultureInfo.InvariantCulture);
                expected.Add(key);
                if (i > 0) text.Append(',');
                text.Append('"').Append(key).Append("\":").Append(i);
            }
            text.Append('}');
            var big = (JsonObject)Parse(text.ToString());
            Assert.Equal(expected, big.Keys.ToList());
            for (int i = 0; i < 40; i++) Assert.Equal(i, big.GetInt32(expected[i]));
        }

        [Test]
        public void Parse_DuplicateKey_IsAnError()
        {
            AssertError("{\"a\":1,\"a\":2}", 1, 8, "duplicate key \"a\"");
            AssertError("{\"a\":{\"b\":1,\"b\":1}}", 1, 13, "duplicate key");

            // the same key in different objects is fine
            Parse("[{\"a\":1},{\"a\":2}]");
        }

        [Test]
        public void Parse_SharedInstances_ForConstants()
        {
            Assert.True(ReferenceEquals(JsonValue.Null, Parse("null")));
            Assert.True(ReferenceEquals(JsonValue.True, Parse("true")));
            Assert.True(ReferenceEquals(JsonValue.False, Parse("false")));
        }

        // ---------- parse: errors ----------

        [Test]
        public void Parse_Errors_LineAndColumn()
        {
            AssertError("", 1, 1, "unexpected end of input");
            AssertError("   ", 1, 4, "unexpected end of input");
            AssertError("{", 1, 2, "unexpected end of input");
            AssertError("[1,", 1, 4, "unexpected end of input");
            AssertError("[1 2]", 1, 4, "',' or ']' is expected, found '2'");
            AssertError("{\"a\" 1}", 1, 6, "':' is expected");
            AssertError("{\"a\":}", 1, 6, "unexpected character '}'");
            AssertError("[1,]", 1, 4, "unexpected character ']'");
            AssertError("{\"a\":1,}", 1, 8, "a string key is expected");
            AssertError("{a:1}", 1, 2, "a string key is expected");
            AssertError("{\"a\":1 \"b\":2}", 1, 8, "',' or '}' is expected");
            AssertError("[1] x", 1, 5, "after the end of the JSON value");
            AssertError("{} {}", 1, 4, "after the end");
            AssertError("tru", 1, 4, "'true' is incomplete");
            AssertError("trux", 1, 4, "unexpected character 'x'");
            AssertError("nulL", 1, 4, "'null' is expected");
            AssertError("True", 1, 1, "unexpected character 'T'");
            AssertError("undefined", 1, 1, "unexpected character 'u'");
            AssertError("'a'", 1, 1, "unexpected character '''");
            AssertError("// comment\n1", 1, 1, "unexpected character '/'");
            AssertError("{\"a\":1}}", 1, 8, "after the end");
        }

        [Test]
        public void Parse_Errors_MultiLine()
        {
            AssertError("{\n  \"a\": 1,\n  \"b\": x\n}", 3, 8, "unexpected character 'x'");
            AssertError("{\r\n  \"a\": ?\r\n}", 2, 8, "unexpected character '?'");
            AssertError("[1,\r2,\r?]", 3, 1, "unexpected character '?'");
            AssertError("[\n\n\n\n]]", 5, 2, "after the end");

            // an error on the last line of a long document
            var sb = new StringBuilder("[\n");
            for (int i = 0; i < 999; i++) sb.Append("  1,\n");
            sb.Append("  oops\n]");
            JsonError error = Fail(sb.ToString());
            Assert.Equal(1001, error.Line);
            Assert.Equal(3, error.Column);
            Assert.Equal(sb.ToString().IndexOf("oops", StringComparison.Ordinal), error.Offset);
        }

        [Test]
        public void Parse_Errors_Strings()
        {
            AssertError("\"abc", 1, 1, "unterminated string");
            AssertError("[\"abc", 1, 2, "unterminated string");
            AssertError("\"a\nb\"", 1, 3, "control character U+000A");
            AssertError("\"a" + (char)0x01 + "\"", 1, 3, "control character U+0001");
            AssertError("\"a\tb\"", 1, 3, "control character U+0009");
            AssertError(Esc("\"~x\""), 1, 2, "invalid escape sequence \\x");
            AssertError(Esc("\"abc~\""), 1, 1, "unterminated string");
            AssertError(Esc("\"~u12G4\""), 1, 6, "not a hex digit");
            AssertError(Esc("\"~u12\""), 1, 6, "hex digit");
            AssertError(Esc("\"abc~u12"), 1, 5, "incomplete");
            AssertError("{\"a\nb\":1}", 1, 4, "control character");
        }

        [Test]
        public void Parse_NullInput_IsAnError()
        {
            JsonNode node;
            JsonError error;
            Assert.False(ModJson.TryParse(null, out node, out error));
            Assert.Null(node);
            Assert.Equal(1, error.Line);
            Assert.Equal(1, error.Column);
            Assert.True(error.Message.Contains("null"));
        }

        [Test]
        public void ParseError_ToString_NamesLineAndColumn()
        {
            JsonError error = Fail("{\n  \"a\": x\n}");
            Assert.True(error.ToString().StartsWith("line 2, column 8: "), error.ToString());
            Assert.Equal("line 3, column 4: boom", new JsonError("boom", 3, 4, 10).ToString());
            Assert.Equal(10, new JsonError("boom", 3, 4, 10).Offset);
            Assert.Equal("", new JsonError(null, 1, 1, 0).Message);
        }

        // ---------- limits ----------

        [Test]
        public void Limit_InputSize_ExactlyMaxBytesIsAccepted()
        {
            Assert.Equal(8 * 1024 * 1024, ModJson.MaxBytes);

            string exact = "[" + new string(' ', ModJson.MaxBytes - 2) + "]";
            Assert.Equal(ModJson.MaxBytes, exact.Length);
            Assert.Equal(0, ((JsonArray)Parse(exact)).Count);
        }

        [Test]
        public void Limit_InputSize_OneByteOverIsRejected()
        {
            string over = "[" + new string(' ', ModJson.MaxBytes - 1) + "]";
            JsonError error = Fail(over);
            Assert.Equal(1, error.Line);
            Assert.Equal(1, error.Column);
            Assert.True(error.Message.Contains("8 MiB"), error.Message);
        }

        [Test]
        public void Limit_InputSize_CountsBytesNotCharacters()
        {
            // 'я' takes two bytes in UTF-8. (MaxBytes - 2) / 2 of them plus two quotes is exactly MaxBytes bytes.
            int fits = (ModJson.MaxBytes - 2) / 2;
            string exact = "\"" + new string('я', fits) + "\"";
            Assert.True(exact.Length < ModJson.MaxBytes, "fewer characters than bytes");
            Assert.Equal(fits, ((JsonValue)Parse(exact)).StringValue.Length);

            string over = "\"" + new string('я', fits + 1) + "\"";
            Assert.True(over.Length < ModJson.MaxBytes, "still fewer characters than the limit");
            Fail(over);
        }

        [Test]
        public void Limit_Depth_ArraysAndObjects()
        {
            Assert.Equal(64, ModJson.MaxDepth);

            JsonNode ok = Parse(Nested("[", "]", 64));
            int depth = 0;
            for (JsonNode n = ok; n is JsonArray; n = ((JsonArray)n)[0]) depth++;
            Assert.Equal(64, depth);

            JsonError error = Fail(Nested("[", "]", 65));
            Assert.Equal(1, error.Line);
            Assert.Equal(65, error.Column);
            Assert.True(error.Message.Contains("64"), error.Message);

            Parse(Nested("{\"a\":", "}", 64));
            JsonError objectError = Fail(Nested("{\"a\":", "}", 65));
            Assert.True(objectError.Message.Contains("deeper"), objectError.Message);

            // objects and arrays count together
            string mixed = string.Concat(Enumerable.Repeat("[{\"a\":", 33)) + "1" + string.Concat(Enumerable.Repeat("}]", 33));
            Fail(mixed);   // 66 levels
            string mixedOk = string.Concat(Enumerable.Repeat("[{\"a\":", 32)) + "1" + string.Concat(Enumerable.Repeat("}]", 32));
            Parse(mixedOk);   // 64 levels
        }

        [Test]
        public void Limit_Depth_AbsurdNestingDoesNotOverflowTheStack()
        {
            JsonError error = Fail(new string('[', 1000000));
            Assert.Equal(65, error.Column);

            Fail(string.Concat(Enumerable.Repeat("{\"a\":", 200000)));
        }

        // ---------- write ----------

        [Test]
        public void Write_Compact()
        {
            const string text = "{\"a\":1,\"b\":[1,2,{\"c\":null}],\"d\":{},\"e\":[],\"f\":\"x\",\"g\":true,\"h\":false}";
            Assert.Equal(text, Compact(Parse(text)));
            Assert.Equal(text, Compact(Parse("  {\"a\" : 1 ,\n \"b\":[ 1,2 , { \"c\" : null } ],\"d\":{ },\"e\":[ ],\"f\":\"x\",\"g\":true,\"h\":false}  ")));
        }

        [Test]
        public void Write_Pretty()
        {
            JsonNode node = Parse("{\"a\":1,\"b\":[1,2],\"c\":{},\"d\":[],\"e\":{\"f\":null,\"g\":[{\"h\":\"x\"}]}}");
            string expected =
                "{\n" +
                "  \"a\": 1,\n" +
                "  \"b\": [\n" +
                "    1,\n" +
                "    2\n" +
                "  ],\n" +
                "  \"c\": {},\n" +
                "  \"d\": [],\n" +
                "  \"e\": {\n" +
                "    \"f\": null,\n" +
                "    \"g\": [\n" +
                "      {\n" +
                "        \"h\": \"x\"\n" +
                "      }\n" +
                "    ]\n" +
                "  }\n" +
                "}";
            Assert.Equal(expected, ModJson.Write(node, true));
            Assert.True(ModJson.Write(node, true).IndexOf('\r') < 0, "line breaks are always \\n");
        }

        [Test]
        public void Write_Scalars_AtTheRoot()
        {
            Assert.Equal("null", ModJson.Write(null, false));
            Assert.Equal("null", ModJson.Write(null, true));
            Assert.Equal("null", Compact(JsonValue.Null));
            Assert.Equal("true", Compact(JsonValue.True));
            Assert.Equal("false", Compact(JsonValue.False));
            Assert.Equal("\"x\"", Compact(JsonValue.Of("x")));
            Assert.Equal("5", Compact(JsonValue.Of(5)));
            Assert.Equal("[]", ModJson.Write(new JsonArray(), true));
            Assert.Equal("{}", ModJson.Write(new JsonObject(), true));
        }

        [Test]
        public void Write_Escapes()
        {
            string raw = "\"\\\n\r\t\b\f" + (char)0x01 + (char)0x1F + "/";
            string expected = Esc("\"~\"~~~n~r~t~b~f~u0001~u001f/\"");
            Assert.Equal(expected, Compact(JsonValue.Of(raw)));

            // and back
            Assert.Equal(raw, ((JsonValue)Parse(expected)).StringValue);

            // keys are escaped the same way
            var obj = new JsonObject().Set("a\"b\n", 1);
            Assert.Equal(Esc("{\"a~\"b~n\":1}"), Compact(obj));
        }

        [Test]
        public void Write_Unicode_StaysReadable()
        {
            Assert.Equal("\"Привет, мир\"", Compact(JsonValue.Of("Привет, мир")));

            string emoji = char.ConvertFromUtf32(0x1F600);
            Assert.Equal("\"" + emoji + "\"", Compact(JsonValue.Of(emoji)));

            // DEL and U+2028 are legal and are not escaped
            string odd = "a" + (char)0x7F + (char)0x2028;
            Assert.Equal("\"" + odd + "\"", Compact(JsonValue.Of(odd)));
        }

        [Test]
        public void Write_LoneSurrogates_BecomeEscapes()
        {
            string lowFirst = "a" + (char)0xDE00 + "b";
            string highLast = "a" + (char)0xD800;
            string highHigh = new string(new[] { (char)0xD800, (char)0xD800, (char)0xDC00 });   // lone high, then a valid pair
            Assert.Equal(Esc("\"a~ude00b\""), Compact(JsonValue.Of(lowFirst)));
            Assert.Equal(Esc("\"a~ud800\""), Compact(JsonValue.Of(highLast)));
            Assert.Equal(Esc("\"~ud800") + (char)0xD800 + (char)0xDC00 + "\"", Compact(JsonValue.Of(highHigh)));

            // the output is valid for a UTF-8 encoder and parses back to the same string
            foreach (string s in new[] { lowFirst, highLast, highHigh })
            {
                string json = Compact(JsonValue.Of(s));
                Assert.Equal(s, ((JsonValue)Parse(json)).StringValue);
                Assert.Equal(json, Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(json)));
            }
        }

        [Test]
        public void Write_Numbers()
        {
            var cases = new[]
            {
                new KeyValuePair<double, string>(0, "0"),
                new KeyValuePair<double, string>(1, "1"),
                new KeyValuePair<double, string>(-1, "-1"),
                new KeyValuePair<double, string>(100000, "100000"),
                new KeyValuePair<double, string>(123456789012345.0, "123456789012345"),
                new KeyValuePair<double, string>(4294967295.0, "4294967295"),
                new KeyValuePair<double, string>(1.5, "1.5"),
                new KeyValuePair<double, string>(0.1, "0.1"),
                new KeyValuePair<double, string>(0.0001, "0.0001"),
                new KeyValuePair<double, string>(0.00001, "1e-5"),
                new KeyValuePair<double, string>(1e-7, "1e-7"),
                new KeyValuePair<double, string>(-2.5e-10, "-2.5e-10"),
                new KeyValuePair<double, string>(1e15, "1e15"),
                new KeyValuePair<double, string>(1e21, "1e21"),
                new KeyValuePair<double, string>(1.5e300, "1.5e300"),
                new KeyValuePair<double, string>(0.30000000000000004, "0.30000000000000004"),
                new KeyValuePair<double, string>(double.MaxValue, "1.7976931348623157e308"),
                new KeyValuePair<double, string>(-double.MaxValue, "-1.7976931348623157e308"),
                new KeyValuePair<double, string>(1.0 / 3, "0.3333333333333333"),
                new KeyValuePair<double, string>(Math.PI, "3.141592653589793"),
            };
            foreach (KeyValuePair<double, string> c in cases)
                Assert.Equal(c.Value, Compact(JsonValue.Of(c.Key)), c.Key.ToString("R", CultureInfo.InvariantCulture));

            Assert.Equal("-0", Compact(JsonValue.Of(BitConverter.Int64BitsToDouble(long.MinValue))));
        }

        [Test]
        public void Write_Numbers_RoundTripExactly()
        {
            var rng = new Random(20261010);
            var values = new List<double> { double.Epsilon, -double.Epsilon, double.MaxValue, 1e-7, 0.1, 1.0 / 3, 2.2250738585072014E-308 };
            var bytes = new byte[8];
            while (values.Count < 3000)
            {
                rng.NextBytes(bytes);
                double d = BitConverter.ToDouble(bytes, 0);
                if (double.IsNaN(d) || double.IsInfinity(d)) continue;
                values.Add(d);
                values.Add(Math.Round(rng.NextDouble() * 1000000, rng.Next(0, 8)));      // "nice" decimals
                values.Add((rng.NextDouble() - 0.5) * Math.Pow(10, rng.Next(-30, 30)));  // wide exponent range
            }

            foreach (double d in values)
            {
                string text = Compact(JsonValue.Of(d));
                double back = ((JsonValue)Parse(text)).NumberValue;
                Assert.True(back == d, d.ToString("R", CultureInfo.InvariantCulture) + " -> " + text + " -> " + back.ToString("R", CultureInfo.InvariantCulture));
            }
        }

        [Test]
        public void Write_NonFiniteNumbers_AreNull()
        {
            Assert.True(JsonValue.Of(double.NaN).IsNull);
            Assert.True(JsonValue.Of(double.PositiveInfinity).IsNull);
            Assert.True(JsonValue.Of(double.NegativeInfinity).IsNull);
            Assert.Equal("[null,1]", Compact(new JsonArray().Add(double.NaN).Add(1)));
        }

        [Test]
        public void Write_Depth_Limit()
        {
            // 64 levels can be written (and parsed back); 65 cannot
            JsonNode deep = Parse(Nested("[", "]", 64));
            Assert.Equal(Nested("[", "]", 64), Compact(deep));
            Assert.Equal(Nested("[", "]", 64), ModJson.Write(deep, true).Replace("\n", "").Replace(" ", ""));

            var tooDeep = new JsonArray();
            JsonArray tip = tooDeep;
            for (int i = 0; i < 64; i++)
            {
                var next = new JsonArray();
                tip.Add(next);
                tip = next;
            }
            Assert.Throws<InvalidOperationException>(() => ModJson.Write(tooDeep, false));
            Assert.Throws<InvalidOperationException>(() => ModJson.Write(tooDeep, true));
            Assert.Throws<InvalidOperationException>(() => ModJson.Write(tooDeep, new StringWriter(), false));
        }

        [Test]
        public void Write_CyclicTree_ThrowsInsteadOfOverflowingTheStack()
        {
            var array = new JsonArray();
            array.Add(array);
            Assert.Throws<InvalidOperationException>(() => ModJson.Write(array, false));

            var obj = new JsonObject();
            obj.Set("self", obj);
            Assert.Throws<InvalidOperationException>(() => ModJson.Write(obj, true));

            // ToString never throws
            Assert.Equal("<JSON too deep>", obj.ToString());
            Assert.Throws<InvalidOperationException>(() => obj.DeepClone());
            Assert.False(obj.Equals(new JsonObject().Set("self", new JsonObject())));
            obj.GetHashCode();   // must terminate
        }

        [Test]
        public void Write_ToTextWriter_MatchesTheStringForm()
        {
            JsonNode node = Parse("{\"a\":[1,2,{\"b\":\"Привет\"}],\"c\":null,\"d\":{\"e\":1.5}}");
            foreach (bool pretty in new[] { false, true })
            {
                var writer = new StringWriter();
                ModJson.Write(node, writer, pretty);
                Assert.Equal(ModJson.Write(node, pretty), writer.ToString());
            }
            Assert.Throws<ArgumentNullException>(() => ModJson.Write(node, (TextWriter)null, false));
        }

        // ---------- round trips ----------

        [Test]
        public void RoundTrip_PreservesFieldOrder_AndUnknownFields()
        {
            // Not alphabetical, with members a newer mod version might add: all of it must come back as it was.
            const string text = "{\"schema\":2,\"zeta\":{\"q\":[1,2,3],\"a\":null},\"features\":{\"known\":{\"x\":1},"
                                + "\"from-the-future\":{\"_schema\":9,\"anything\":[{\"deep\":[true,false]}],\"ratio\":0.125}},"
                                + "\"alpha\":\"Ключ\",\"b\":-0,\"c\":1e-7}";
            JsonNode node = Parse(text);
            string written = Compact(node);
            Assert.Equal(text, written);

            Assert.Equal(new[] { "schema", "zeta", "features", "alpha", "b", "c" }, ((JsonObject)node).Keys.ToArray());

            // pretty output parses back to the same document and the same order
            JsonNode again = Parse(ModJson.Write(node, true));
            Assert.Equal(text, Compact(again));
            Assert.True(node.Equals(again));
        }

        [Test]
        public void RoundTrip_EditInPlace_KeepsOtherMembersUntouched()
        {
            var obj = (JsonObject)Parse("{\"b\":1,\"a\":2,\"unknown\":{\"x\":[1]},\"c\":3}");

            obj.Set("a", 20);              // existing key: stays in place
            obj.Set("added", "new");       // new key: at the end
            obj.Remove("b");               // removed: the rest keeps its order
            obj.Set("b", 5);               // re-added: at the end

            Assert.Equal("{\"a\":20,\"unknown\":{\"x\":[1]},\"c\":3,\"added\":\"new\",\"b\":5}", Compact(obj));
        }

        [Test]
        public void RoundTrip_Strings()
        {
            string[] samples =
            {
                "", " ", "plain", "quote\"inside", "back\\slash", "tab\tnewline\ncr\r", "Привет", "日本語",
                char.ConvertFromUtf32(0x1F600), "a" + (char)0xD800 + "b", "/slash/", ((char)0xE9).ToString(), ((char)0).ToString(),
            };
            foreach (string s in samples)
            {
                var array = new JsonArray().Add(s);
                var obj = new JsonObject().Set(s, s);
                foreach (bool pretty in new[] { false, true })
                {
                    Assert.Equal(s, ((JsonValue)((JsonArray)Parse(ModJson.Write(array, pretty)))[0]).StringValue);
                    var back = (JsonObject)Parse(ModJson.Write(obj, pretty));
                    Assert.Equal(s, back.GetString(s));
                }
            }
        }

        [Test]
        public void RoundTrip_LongIntegersSurvive()
        {
            const string text = "[18446744073709551615,-9223372036854775808,9007199254740993,1000000000000000,-999999999999999]";
            Assert.Equal(text, Compact(Parse(text)));
            Assert.Equal(text, Compact(Parse(ModJson.Write(Parse(text), true))));
        }

        [Test]
        public void RoundTrip_NumberSpellings_BecomeCanonical()
        {
            // Values are kept, the spelling is normalised.
            Assert.Equal("[1.5,100,100,1e-7,0.1,0]", Compact(Parse("[1.50,1E2,1e+2,1E-7,0.10,0e5]")));
        }

        [Test]
        public void RoundTrip_PrettyAndCompact_AreEqualTrees()
        {
            JsonNode node = Parse("{\"a\":[1,{\"b\":[]},[[],[1]]],\"c\":\"x\",\"d\":{\"e\":{\"f\":{}}},\"g\":[true,false,null,0.5,-3]}");
            JsonNode fromPretty = Parse(ModJson.Write(node, true));
            JsonNode fromCompact = Parse(ModJson.Write(node, false));
            Assert.True(node.Equals(fromPretty));
            Assert.True(node.Equals(fromCompact));
            Assert.Equal(Compact(fromPretty), Compact(fromCompact));
        }

        // ---------- fuzz ----------

        private static readonly string[] SeedDocuments =
        {
            "{\"a\":1,\"b\":[1,2,3],\"c\":{\"d\":null,\"e\":\"text\"}}",
            "[1,2.5,-3e10,true,false,null,\"s\",[],{}]",
            "{\"name\":\"Фотон\",\"escapes\":\"a~\"b~~c~n~u0041\",\"nums\":[0,-0,1e-7,4294967295,18446744073709551615]}",
            "  {\n  \"k\" : [ { \"x\" : 1 } , { \"y\" : [ ] } ] ,\r\n \"z\" : \"v\"\n}\n",
            "\"just a string\"",
            "123.456e-7",
            "[[[[[[[[[[1]]]]]]]]]]",
            "{\"deep\":{\"deeper\":{\"deepest\":{\"v\":[1,[2,[3,[4]]]]}}}}",
        };

        private const string FuzzAlphabet = "{}[]\",:0123456789.eE+-tfnrulsa\\ \n\t/xyzTFN";

        private static string RandomText(Random rng)
        {
            int mode = rng.Next(10);
            var sb = new StringBuilder();

            if (mode < 3)
            {
                // soup of JSON characters
                int length = rng.Next(0, 40);
                for (int i = 0; i < length; i++) sb.Append(FuzzAlphabet[rng.Next(FuzzAlphabet.Length)]);
            }
            else if (mode < 4)
            {
                // arbitrary UTF-16, including lone surrogates, controls, BOM, NUL
                int length = rng.Next(0, 30);
                for (int i = 0; i < length; i++)
                {
                    int pick = rng.Next(6);
                    if (pick == 0) sb.Append((char)rng.Next(0, 0x20));
                    else if (pick == 1) sb.Append((char)rng.Next(0xD800, 0xE000));
                    else if (pick == 2) sb.Append((char)rng.Next(0x80, 0xD800));
                    else if (pick == 3) sb.Append((char)rng.Next(0xE000, 0x10000));
                    else sb.Append(FuzzAlphabet[rng.Next(FuzzAlphabet.Length)]);
                }
            }
            else if (mode < 5)
            {
                // truncated valid document
                string seed = Esc(SeedDocuments[rng.Next(SeedDocuments.Length)]);
                sb.Append(seed, 0, rng.Next(0, seed.Length + 1));
            }
            else
            {
                // valid document with a few random edits
                sb.Append(Esc(SeedDocuments[rng.Next(SeedDocuments.Length)]));
                int edits = rng.Next(1, 4);
                for (int i = 0; i < edits && sb.Length > 0; i++)
                {
                    int at = rng.Next(sb.Length);
                    switch (rng.Next(4))
                    {
                        case 0: sb.Remove(at, Math.Min(rng.Next(1, 4), sb.Length - at)); break;
                        case 1: sb.Insert(at, FuzzAlphabet[rng.Next(FuzzAlphabet.Length)]); break;
                        case 2: sb[at] = FuzzAlphabet[rng.Next(FuzzAlphabet.Length)]; break;
                        default: sb.Insert(at, sb.ToString(Math.Max(0, at - 5), Math.Min(5, at))); break;
                    }
                }
            }
            return sb.ToString();
        }

        [Test]
        public void Fuzz_TenThousandRandomStrings_NeverThrow()
        {
            var rng = new Random(1985);
            int parsed = 0, rejected = 0;

            for (int i = 0; i < 10000; i++)
            {
                string text = RandomText(rng);
                JsonNode node;
                JsonError error;
                bool ok;
                try
                {
                    ok = ModJson.TryParse(text, out node, out error);
                }
                catch (Exception ex)
                {
                    throw new AssertFailedException("TryParse threw " + ex.GetType().Name + " for " + Shown(text) + ": " + ex.Message);
                }

                if (ok)
                {
                    parsed++;
                    Assert.NotNull(node, Shown(text));
                    Assert.Null(error, Shown(text));

                    // whatever parses must write and parse back to an equal tree, in both layouts
                    foreach (bool pretty in new[] { false, true })
                    {
                        JsonNode again = Parse(ModJson.Write(node, pretty));
                        Assert.True(node.Equals(again), "round trip of " + Shown(text));
                    }
                }
                else
                {
                    rejected++;
                    Assert.Null(node, Shown(text));
                    Assert.NotNull(error, Shown(text));
                    Assert.True(error.Line >= 1 && error.Column >= 1, "position of " + Shown(text) + ": " + error);
                    Assert.True(error.Offset >= 0 && error.Offset <= text.Length, "offset of " + Shown(text) + ": " + error.Offset);
                    Assert.True(error.Message.Length > 0, "message of " + Shown(text));
                }
            }

            // the generator must reach both outcomes, otherwise the test proves nothing
            Assert.True(parsed >= 300, "valid inputs generated: " + parsed);
            Assert.True(rejected >= 3000, "invalid inputs generated: " + rejected);
        }

        [Test]
        public void Fuzz_ErrorPositions_PointInsideTheLine()
        {
            var rng = new Random(77);
            for (int i = 0; i < 2000; i++)
            {
                string text = RandomText(rng);
                JsonNode node;
                JsonError error;
                if (ModJson.TryParse(text, out node, out error)) continue;

                // recompute line and column independently from the offset
                int line = 1, lineStart = text.Length > 0 && text[0] == (char)0xFEFF ? 1 : 0;
                for (int k = lineStart; k < error.Offset; k++)
                {
                    if (text[k] == '\n' || (text[k] == '\r' && !(k + 1 < text.Length && text[k + 1] == '\n')))
                    {
                        line++;
                        lineStart = k + 1;
                    }
                }
                Assert.Equal(line, error.Line, "line of " + Shown(text));
                Assert.Equal(error.Offset - lineStart + 1, error.Column, "column of " + Shown(text));
            }
        }

        // ---------- speed sanity ----------

        [Test]
        public void Speed_OneMegabyteDocument_ParsesAndWritesQuickly()
        {
            var array = new JsonArray();
            for (int i = 0; i < 6000; i++)
            {
                array.Add(new JsonObject()
                    .Set("id", 100000 + i)
                    .Set("name", "Процессор номер " + i)
                    .Set("price", i * 1.25 + 0.1)
                    .Set("tags", new JsonArray().Add("a").Add("b").Add(i % 2 == 0))
                    .Set("stats", new JsonObject().Set("ips", i * 1e3).Set("w", 0.0001 * i).Set("note", JsonValue.Null)));
            }
            string text = ModJson.Write(array, true);
            Assert.True(text.Length > 900000, "document size: " + text.Length);

            var watch = Stopwatch.StartNew();
            JsonNode parsed = null;
            for (int i = 0; i < 3; i++) parsed = Parse(text);
            long parseMs = watch.ElapsedMilliseconds / 3;

            watch.Restart();
            string written = null;
            for (int i = 0; i < 3; i++) written = ModJson.Write(parsed, true);
            long writeMs = watch.ElapsedMilliseconds / 3;

            Assert.Equal(text, written);
            Assert.True(parseMs < 3000 && writeMs < 3000, "parse " + parseMs + " ms, write " + writeMs + " ms");
        }
    }

    /// <summary>The tree types: JsonValue, JsonObject, JsonArray, structural equality, cloning, conversions.</summary>
    [Covers(typeof(JsonNode), typeof(JsonObject), typeof(JsonArray), typeof(JsonValue), typeof(JsonKind))]
    public class JsonNodeTests
    {
        private static JsonNode Parse(string text)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out node, out error), "parse: " + error);
            return node;
        }

        // ---------- kinds and values ----------

        [Test]
        public void Kinds_AndPredicates()
        {
            Assert.Equal(JsonKind.Null, JsonValue.Null.Kind);
            Assert.Equal(JsonKind.Bool, JsonValue.True.Kind);
            Assert.Equal(JsonKind.Number, JsonValue.Of(1).Kind);
            Assert.Equal(JsonKind.String, JsonValue.Of("x").Kind);
            Assert.Equal(JsonKind.Object, new JsonObject().Kind);
            Assert.Equal(JsonKind.Array, new JsonArray().Kind);

            Assert.True(JsonValue.Null.IsNull && JsonValue.Null.IsScalar);
            Assert.True(JsonValue.Of("x").IsScalar && !JsonValue.Of("x").IsNull);
            Assert.True(new JsonObject().IsObject && !new JsonObject().IsArray && !new JsonObject().IsScalar);
            Assert.True(new JsonArray().IsArray && !new JsonArray().IsObject && !new JsonArray().IsScalar);
        }

        [Test]
        public void Value_Factories()
        {
            Assert.True(ReferenceEquals(JsonValue.True, JsonValue.Of(true)));
            Assert.True(ReferenceEquals(JsonValue.False, JsonValue.Of(false)));
            Assert.True(ReferenceEquals(JsonValue.Null, JsonValue.Of((string)null)));
            Assert.Equal("", JsonValue.Of("").StringValue);
            Assert.Equal(1.5, JsonValue.Of(1.5).NumberValue);
            Assert.Equal(7.0, JsonValue.Of(7).NumberValue);

            // JSON cannot hold NaN or infinity
            Assert.True(JsonValue.Of(double.NaN).IsNull);
            Assert.True(JsonValue.Of(double.PositiveInfinity).IsNull);
            Assert.True(JsonValue.Of(double.NegativeInfinity).IsNull);
        }

        [Test]
        public void Value_AccessorsOfOtherKinds_AreNeutral()
        {
            Assert.False(JsonValue.Of("true").BoolValue);
            Assert.Equal(0.0, JsonValue.Of("5").NumberValue);
            Assert.Null(JsonValue.Of(5).StringValue);
            Assert.Null(JsonValue.Null.StringValue);
            long l;
            int i;
            ulong u;
            Assert.False(JsonValue.Of("5").TryGetInt64(out l));
            Assert.False(JsonValue.True.TryGetInt32(out i));
            Assert.False(JsonValue.Null.TryGetUInt64(out u));
        }

        [Test]
        public void Value_IntegerReaders_Boundaries()
        {
            int i;
            Assert.True(JsonValue.Of(int.MaxValue).TryGetInt32(out i) && i == int.MaxValue);
            Assert.True(JsonValue.Of(int.MinValue).TryGetInt32(out i) && i == int.MinValue);
            Assert.False(JsonValue.Of((long)int.MaxValue + 1).TryGetInt32(out i));
            Assert.False(JsonValue.Of((long)int.MinValue - 1).TryGetInt32(out i));
            Assert.False(JsonValue.Of(1.5).TryGetInt32(out i));
            Assert.False(JsonValue.Of(-0.25).TryGetInt32(out i));
            Assert.True(JsonValue.Of(3.0).TryGetInt32(out i) && i == 3);
            Assert.True(JsonValue.Of(BitConverter.Int64BitsToDouble(long.MinValue)).TryGetInt32(out i) && i == 0, "-0 reads as 0");

            long l;
            Assert.True(JsonValue.Of(long.MaxValue).TryGetInt64(out l) && l == long.MaxValue);
            Assert.True(JsonValue.Of(long.MinValue).TryGetInt64(out l) && l == long.MinValue);
            Assert.False(JsonValue.Of(1e19).TryGetInt64(out l));
            Assert.False(JsonValue.Of(-1e19).TryGetInt64(out l));

            ulong u;
            Assert.True(JsonValue.Of(ulong.MaxValue).TryGetUInt64(out u) && u == ulong.MaxValue);
            Assert.False(JsonValue.Of(ulong.MaxValue).TryGetInt64(out l));
            Assert.False(JsonValue.Of(-1).TryGetUInt64(out u));
            Assert.False(JsonValue.Of(1e20).TryGetUInt64(out u));
            Assert.True(JsonValue.Of(4294967295.0).TryGetUInt64(out u) && u == 4294967295UL);
        }

        [Test]
        public void Value_LongConstructors_AreExact()
        {
            long[] samples = { 0, 1, -1, 999999999999999L, 1000000000000000L, 9007199254740993L, long.MaxValue, long.MinValue, -9007199254740993L };
            foreach (long sample in samples)
            {
                long back;
                Assert.True(JsonValue.Of(sample).TryGetInt64(out back));
                Assert.Equal(sample, back);
                Assert.Equal(sample.ToString(CultureInfo.InvariantCulture), ModJson.Write(JsonValue.Of(sample), false));
            }
            ulong max = ulong.MaxValue;
            Assert.Equal("18446744073709551615", ModJson.Write(JsonValue.Of(max), false));
        }

        // ---------- equality ----------

        [Test]
        public void Equality_Scalars()
        {
            Assert.True(JsonValue.Of("a").Equals(JsonValue.Of("a")));
            Assert.False(JsonValue.Of("a").Equals(JsonValue.Of("A")));
            Assert.True(JsonValue.Of(1).Equals(JsonValue.Of(1.0)));
            Assert.False(JsonValue.Of(1).Equals(JsonValue.Of("1")));
            Assert.False(JsonValue.True.Equals(JsonValue.Of(1)));
            Assert.False(JsonValue.Null.Equals(JsonValue.False));
            Assert.True(JsonValue.Null.Equals(JsonValue.Null));
            Assert.False(JsonValue.Of(1).Equals(null));
            Assert.False(JsonValue.Of(1).Equals("1"));

            // 0 and -0 are the same number
            JsonValue negativeZero = JsonValue.Of(BitConverter.Int64BitsToDouble(long.MinValue));
            Assert.True(JsonValue.Of(0).Equals(negativeZero));
            Assert.Equal(JsonValue.Of(0).GetHashCode(), negativeZero.GetHashCode());
        }

        [Test]
        public void Equality_LongIntegers_AreExact()
        {
            // doubles cannot tell these apart, the nodes can
            Assert.False(JsonValue.Of(9007199254740993L).Equals(JsonValue.Of(9007199254740992.0)));
            Assert.False(JsonValue.Of(9007199254740993L).Equals(JsonValue.Of(9007199254740992L)));
            Assert.True(JsonValue.Of(9007199254740993L).Equals(Parse("9007199254740993")));

            // same value through the long and the double route
            Assert.True(JsonValue.Of(1000000000000000L).Equals(JsonValue.Of(1e15)));
            Assert.Equal(JsonValue.Of(1000000000000000L).GetHashCode(), JsonValue.Of(1e15).GetHashCode());
            Assert.True(JsonValue.Of(ulong.MaxValue).Equals(Parse("18446744073709551615")));
            Assert.False(JsonValue.Of(ulong.MaxValue).Equals(JsonValue.Of(1.8446744073709552e19)));
        }

        [Test]
        public void Equality_Containers()
        {
            JsonNode a = Parse("{\"x\":[1,2,{\"y\":null}],\"z\":\"s\"}");
            JsonNode sameOrder = Parse("{\"x\":[1,2,{\"y\":null}],\"z\":\"s\"}");
            JsonNode otherOrder = Parse("{\"z\":\"s\",\"x\":[1,2,{\"y\":null}]}");
            Assert.True(a.Equals(sameOrder) && a.GetHashCode() == sameOrder.GetHashCode());
            Assert.True(a.Equals(otherOrder) && a.GetHashCode() == otherOrder.GetHashCode(), "member order is not significant");
            Assert.Equal(new[] { "z", "x" }, ((JsonObject)otherOrder).Keys.ToArray());

            Assert.False(a.Equals(Parse("{\"x\":[1,2,{\"y\":null}]}")), "fewer members");
            Assert.False(a.Equals(Parse("{\"x\":[1,2,{\"y\":null}],\"z\":\"t\"}")), "other value");
            Assert.False(a.Equals(Parse("{\"x\":[2,1,{\"y\":null}],\"z\":\"s\"}")), "array order is significant");
            Assert.False(a.Equals(Parse("{\"x\":[1,2],\"z\":\"s\"}")), "shorter array");
            Assert.False(Parse("[]").Equals(Parse("{}")));
            Assert.False(Parse("[]").Equals(JsonValue.Null));
            Assert.True(Parse("[]").Equals(new JsonArray()));
            Assert.True(Parse("{}").Equals(new JsonObject()));
        }

        // ---------- JsonObject ----------

        [Test]
        public void Object_SetGetRemove()
        {
            var obj = new JsonObject();
            Assert.Equal(0, obj.Count);
            Assert.Null(obj["a"]);
            Assert.False(obj.ContainsKey("a"));

            Assert.True(ReferenceEquals(obj, obj.Set("a", 1)), "Set returns the object");
            obj.Set("b", "two").Set("c", true);
            Assert.Equal(3, obj.Count);
            Assert.True(obj.ContainsKey("b"));
            Assert.Equal(new[] { "a", "b", "c" }, obj.Keys.ToArray());

            JsonNode found;
            Assert.True(obj.TryGetValue("b", out found));
            Assert.Equal("two", ((JsonValue)found).StringValue);
            Assert.False(obj.TryGetValue("nope", out found));
            Assert.Null(found);

            obj["d"] = 4;   // indexer setter
            Assert.Equal(4, obj.GetInt32("d"));

            Assert.True(obj.Remove("b"));
            Assert.False(obj.Remove("b"));
            Assert.False(obj.Remove(null));
            Assert.Equal(new[] { "a", "c", "d" }, obj.Keys.ToArray());

            obj.Clear();
            Assert.Equal(0, obj.Count);
            Assert.Equal(0, obj.Keys.Count());
            obj.Set("again", 1);
            Assert.Equal(1, obj.Count);
        }

        [Test]
        public void Object_Set_ExistingKeyKeepsItsPosition()
        {
            var obj = new JsonObject().Set("a", 1).Set("b", 2).Set("c", 3);
            obj.Set("b", "changed");
            Assert.Equal(new[] { "a", "b", "c" }, obj.Keys.ToArray());
            Assert.Equal("changed", obj.GetString("b"));
            Assert.Equal(3, obj.Count);
        }

        [Test]
        public void Object_NullsAndMissingKeys()
        {
            var obj = new JsonObject();
            obj.Set("n", (JsonNode)null);
            Assert.True(obj["n"].IsNull, "a null value becomes JSON null");
            Assert.True(ReferenceEquals(JsonValue.Null, obj["n"]));
            Assert.True(obj.ContainsKey("n"), "a null member exists, unlike a missing one");
            Assert.Null(obj["missing"]);

            Assert.Throws<ArgumentNullException>(() => obj.Set(null, 1));
            Assert.Throws<ArgumentNullException>(() => obj.Add(null, 1));
            Assert.Null(obj[null]);
            Assert.False(obj.ContainsKey(null));
            JsonNode ignored;
            Assert.False(obj.TryGetValue(null, out ignored));

            Assert.Throws<ArgumentException>(() => obj.Add("n", 2));
            obj.Add("fresh", 1);
            Assert.Equal(2, obj.Count);

            // keys are case sensitive and ordinal
            obj.Set("Fresh", 2);
            Assert.Equal(1, obj.GetInt32("fresh"));
            Assert.Equal(2, obj.GetInt32("Fresh"));
        }

        [Test]
        public void Object_ManyMembers_UseTheIndex()
        {
            var obj = new JsonObject();
            for (int i = 0; i < 200; i++) obj.Set("k" + i, i);
            Assert.Equal(200, obj.Count);
            for (int i = 0; i < 200; i += 17) Assert.Equal(i, obj.GetInt32("k" + i));

            // remove from the middle: order of the rest is unchanged, lookups still right
            for (int i = 50; i < 150; i++) Assert.True(obj.Remove("k" + i));
            Assert.Equal(100, obj.Count);
            Assert.Null(obj["k100"]);
            Assert.Equal(49, obj.GetInt32("k49"));
            Assert.Equal(150, obj.GetInt32("k150"));
            Assert.Equal("k49", obj.Keys.ElementAt(49));
            Assert.Equal("k150", obj.Keys.ElementAt(50));

            obj.Set("k100", "back");
            Assert.Equal("k100", obj.Keys.Last());
            Assert.Equal("back", obj.GetString("k100"));

            // shrink below the index threshold and grow again
            foreach (string key in obj.Keys.ToList()) if (key != "k1" && key != "k2") obj.Remove(key);
            Assert.Equal(new[] { "k1", "k2" }, obj.Keys.ToArray());
            for (int i = 0; i < 20; i++) obj.Set("n" + i, i);
            Assert.Equal(22, obj.Count);
            Assert.Equal(19, obj.GetInt32("n19"));
            Assert.Equal(1, obj.GetInt32("k1"));
        }

        [Test]
        public void Object_Enumeration()
        {
            var obj = new JsonObject().Set("a", 1).Set("b", "x");

            var pairs = new List<string>();
            foreach (KeyValuePair<string, JsonNode> pair in obj) pairs.Add(pair.Key + "=" + pair.Value);
            Assert.Equal(new[] { "a=1", "b=\"x\"" }, pairs.ToArray());
            Assert.Equal(new[] { "1", "\"x\"" }, obj.Values.Select(v => v.ToString()).ToArray());

            // a structural change during enumeration fails fast; replacing a value is fine
            Assert.Throws<InvalidOperationException>(() => { foreach (var pair in obj) obj.Set("new" + pair.Key, 0); });
            Assert.Throws<InvalidOperationException>(() => { foreach (string key in obj.Keys) obj.Remove(key); });
            Assert.Throws<InvalidOperationException>(() => { foreach (JsonNode value in obj.Values) obj.Clear(); });
            var fresh = new JsonObject().Set("a", 1).Set("b", 2);
            foreach (var pair in fresh) fresh.Set(pair.Key, 9);
            Assert.Equal("{\"a\":9,\"b\":9}", fresh.ToString());
        }

        [Test]
        public void Object_TypedGetters()
        {
            var obj = (JsonObject)Parse("{\"s\":\"text\",\"i\":42,\"neg\":-7,\"d\":2.5,\"b\":true,\"n\":null,\"big\":9007199254740993,"
                                        + "\"huge\":5000000000,\"o\":{\"x\":1},\"a\":[1,2]}");

            Assert.Equal("text", obj.GetString("s"));
            Assert.Equal("fallback", obj.GetString("i", "fallback"));
            Assert.Equal("fallback", obj.GetString("missing", "fallback"));
            Assert.Equal("fallback", obj.GetString("n", "fallback"));
            Assert.Null(obj.GetString("missing"));

            Assert.Equal(42, obj.GetInt32("i"));
            Assert.Equal(-7, obj.GetInt32("neg"));
            Assert.Equal(-1, obj.GetInt32("d", -1));          // 2.5 is not an integer
            Assert.Equal(-1, obj.GetInt32("huge", -1));       // out of Int32
            Assert.Equal(-1, obj.GetInt32("s", -1));
            Assert.Equal(5000000000L, obj.GetInt64("huge"));
            Assert.Equal(9007199254740993L, obj.GetInt64("big"));
            Assert.Equal(-5L, obj.GetInt64("d", -5));

            Assert.Equal(2.5, obj.GetDouble("d"));
            Assert.Equal(42.0, obj.GetDouble("i"));
            Assert.Equal(9.5, obj.GetDouble("s", 9.5));

            Assert.True(obj.GetBool("b"));
            Assert.True(obj.GetBool("s", true));
            Assert.False(obj.GetBool("missing"));

            Assert.Equal(1, obj.GetObject("o").GetInt32("x"));
            Assert.Null(obj.GetObject("a"));
            Assert.Null(obj.GetObject("missing"));
            Assert.Equal(2, obj.GetArray("a").Count);
            Assert.Null(obj.GetArray("o"));

            string text;
            int number;
            bool flag;
            double real;
            long wide;
            Assert.True(obj.TryGetString("s", out text) && text == "text");
            Assert.False(obj.TryGetString("i", out text));
            Assert.Null(text);
            Assert.True(obj.TryGetInt32("i", out number) && number == 42);
            Assert.False(obj.TryGetInt32("d", out number));
            Assert.Equal(0, number);
            Assert.True(obj.TryGetBool("b", out flag) && flag);
            Assert.False(obj.TryGetBool("s", out flag));
            Assert.True(obj.TryGetDouble("d", out real) && real == 2.5);
            Assert.False(obj.TryGetDouble("b", out real));
            Assert.True(obj.TryGetInt64("huge", out wide) && wide == 5000000000L);
            Assert.False(obj.TryGetInt64("n", out wide));
        }

        // ---------- JsonArray ----------

        [Test]
        public void Array_Operations()
        {
            var array = new JsonArray();
            Assert.Equal(0, array.Count);

            Assert.True(ReferenceEquals(array, array.Add(1)), "Add returns the array");
            array.Add("two").Add(JsonValue.True);
            Assert.Equal(3, array.Count);
            Assert.Equal("two", ((JsonValue)array[1]).StringValue);

            array.Insert(1, 1.5);
            Assert.Equal("[1,1.5,\"two\",true]", array.ToString());

            array[0] = "first";
            Assert.Equal("[\"first\",1.5,\"two\",true]", array.ToString());

            array.RemoveAt(1);
            Assert.Equal(3, array.Count);

            array.AddRange(new JsonNode[] { 7, null, "x" });
            Assert.Equal("[\"first\",\"two\",true,7,null,\"x\"]", array.ToString());
            Assert.Throws<ArgumentNullException>(() => array.AddRange(null));

            JsonNode value;
            Assert.True(array.TryGetValue(0, out value));
            Assert.False(array.TryGetValue(6, out value));
            Assert.False(array.TryGetValue(-1, out value));
            Assert.Null(value);
            Assert.Throws<ArgumentOutOfRangeException>(() => { JsonNode ignored = array[6]; });
            Assert.Throws<ArgumentOutOfRangeException>(() => { array[-1] = 1; });

            Assert.Equal(6, array.Select(x => x).Count());
            array.Clear();
            Assert.Equal(0, array.Count);
            Assert.Equal(0, new JsonArray(16).Count);
        }

        [Test]
        public void Array_NullElements_BecomeJsonNull()
        {
            var array = new JsonArray();
            array.Add(null);
            array.Insert(0, null);
            array[0] = null;
            Assert.True(array[0].IsNull && array[1].IsNull);
            Assert.True(ReferenceEquals(JsonValue.Null, array[0]));
        }

        // ---------- cloning ----------

        [Test]
        public void DeepClone_IsIndependent()
        {
            var original = (JsonObject)Parse("{\"a\":[1,{\"b\":\"x\"}],\"c\":{\"d\":[]},\"n\":null}");
            var copy = (JsonObject)original.DeepClone();

            Assert.True(original.Equals(copy));
            Assert.False(ReferenceEquals(original, copy));
            Assert.False(ReferenceEquals(original["a"], copy["a"]));
            Assert.False(ReferenceEquals(original.GetObject("c"), copy.GetObject("c")));
            Assert.Equal(original.ToString(), copy.ToString());
            Assert.Equal(new[] { "a", "c", "n" }, copy.Keys.ToArray());

            // change the copy: the original stays as it was
            ((JsonObject)copy.GetArray("a")[1]).Set("b", "changed");
            copy.GetObject("c").Set("new", 1);
            copy.Remove("n");
            Assert.Equal("{\"a\":[1,{\"b\":\"x\"}],\"c\":{\"d\":[]},\"n\":null}", original.ToString());
            Assert.False(original.Equals(copy));

            // scalars are immutable and shared
            JsonValue text = JsonValue.Of("s");
            Assert.True(ReferenceEquals(text, text.DeepClone()));

            var array = (JsonArray)Parse("[[1],[2,[3]]]");
            var arrayCopy = (JsonArray)array.DeepClone();
            Assert.True(array.Equals(arrayCopy));
            ((JsonArray)arrayCopy[0]).Add(9);
            Assert.Equal("[[1],[2,[3]]]", array.ToString());
        }

        // ---------- conversions and text ----------

        [Test]
        public void ImplicitConversions()
        {
            JsonNode text = "abc";
            JsonNode flag = true;
            JsonNode small = 5;
            JsonNode wide = 5000000000L;
            JsonNode real = 2.5;
            JsonNode unsigned = ulong.MaxValue;
            JsonNode none = (string)null;

            Assert.Equal("abc", ((JsonValue)text).StringValue);
            Assert.True(((JsonValue)flag).BoolValue);
            Assert.Equal(5.0, ((JsonValue)small).NumberValue);
            Assert.Equal(5000000000.0, ((JsonValue)wide).NumberValue);
            Assert.Equal(2.5, ((JsonValue)real).NumberValue);
            Assert.Equal("18446744073709551615", unsigned.ToString());
            Assert.True(none.IsNull);

            // float and uint go through the widening conversions
            JsonNode fromFloat = 0.5f;
            JsonNode fromUInt = 4294967295u;
            Assert.Equal("0.5", fromFloat.ToString());
            Assert.Equal("4294967295", fromUInt.ToString());
        }

        [Test]
        public void CollectionInitializers()
        {
            var obj = new JsonObject { { "a", 1 }, { "b", "x" }, { "c", new JsonArray { 1, "y", true, null } } };
            Assert.Equal("{\"a\":1,\"b\":\"x\",\"c\":[1,\"y\",true,null]}", obj.ToString());
            Assert.Throws<ArgumentException>(() => { var dup = new JsonObject { { "a", 1 }, { "a", 2 } }; });
        }

        [Test]
        public void ToString_IsCompactJson()
        {
            Assert.Equal("null", JsonValue.Null.ToString());
            Assert.Equal("\"x\"", JsonValue.Of("x").ToString());
            Assert.Equal("12", JsonValue.Of(12).ToString());
            Assert.Equal("{\"a\":[1,2]}", Parse("{ \"a\" : [ 1 , 2 ] }").ToString());
        }

        [Test]
        public void JsonKind_Values_AreStable()
        {
            // the numbers may end up in logs and tests of other packages
            Assert.Equal(0, (int)JsonKind.Null);
            Assert.Equal(1, (int)JsonKind.Bool);
            Assert.Equal(2, (int)JsonKind.Number);
            Assert.Equal(3, (int)JsonKind.String);
            Assert.Equal(4, (int)JsonKind.Object);
            Assert.Equal(5, (int)JsonKind.Array);
        }
    }
}
