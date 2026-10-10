using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.L10n;

namespace PTMod.Tests.L10n
{
    /// <summary>
    /// ModL10n logic (FR-8, TSK-013): ru/en plural rules, string tables with layers, the ru -> en -> ru -> [key] chain
    /// (AC-8.1), plural forms (1, 2, 5, 11, 21, 22, 111 in ru; 1, 2 in en), LocText from JSON, and the report of keys
    /// that exist in one language only. Nothing here touches Unity, the disk or the clock.
    /// </summary>
    [Covers(typeof(PluralRules), typeof(PluralForm), typeof(L10nTables), typeof(L10nMismatchReport), typeof(LocTextResolver), typeof(LocText))]
    public class L10nTests
    {
        private const string RuTable = @"{
            ""t.hello"": ""Привет, {0}!"",
            ""t.rate"": ""Курс {0}"",
            ""t.brace"": ""Формула {x}"",
            ""t.badformat"": ""Нужно {0} и {1}"",
            ""core.mail.daysLeft"": ""{0} день|{0} дня|{0} дней"",
            ""t.days.one"": ""Остался {0} день"",
            ""t.days.few"": ""Осталось {0} дня"",
            ""t.days.many"": ""Осталось {0} дней"",
            ""t.items.one"": ""{0} предмет ({1})"",
            ""t.items.few"": ""{0} предмета ({1})"",
            ""t.items.many"": ""{0} предметов ({1})"",
            ""t.nofew.one"": ""одна штука {0}"",
            ""t.nofew.many"": ""штук {0}"",
            ""t.pair2"": ""один {0}|много {0}"",
            ""t.pair1"": ""всегда {0}"",
            ""t.ruonly"": ""только русский""
        }";

        private const string EnTable = @"{
            ""t.hello"": ""Hello, {0}!"",
            ""t.days.one"": ""{0} day left"",
            ""t.days.other"": ""{0} days left"",
            ""t.apples.one"": ""{0} apple"",
            ""t.apples.other"": ""{0} apples"",
            ""t.enonly"": ""english only""
        }";

        private static L10nTables SampleTables()
        {
            var tables = new L10nTables();
            var errors = new List<string>();
            tables.AddJson("ru", "l10n/ru.json", RuTable, errors);
            tables.AddJson("en", "l10n/en.json", EnTable, errors);
            Assert.Equal(0, errors.Count, "the sample tables must load without errors: " + string.Join("; ", errors));
            return tables;
        }

        private static LocTextResolver SampleResolver(List<string> warnings = null, string language = null)
        {
            var resolver = new LocTextResolver(SampleTables(), warnings == null ? (Action<string>)null : warnings.Add);
            if (language != null) resolver.Language = language;
            return resolver;
        }

        // ---- PluralRules ----

        [Test]
        public void PluralRu_RequiredNumbers()
        {
            Assert.Equal(PluralForm.One, PluralRules.Select("ru", 1));
            Assert.Equal(PluralForm.Few, PluralRules.Select("ru", 2));
            Assert.Equal(PluralForm.Many, PluralRules.Select("ru", 5));
            Assert.Equal(PluralForm.Many, PluralRules.Select("ru", 11));
            Assert.Equal(PluralForm.One, PluralRules.Select("ru", 21));
            Assert.Equal(PluralForm.Few, PluralRules.Select("ru", 22));
            Assert.Equal(PluralForm.Many, PluralRules.Select("ru", 111));
        }

        [Test]
        public void PluralRu_WholeRanges()
        {
            Assert.Equal(PluralForm.Many, PluralRules.Select("ru", 0));
            foreach (long n in new long[] { 2, 3, 4, 22, 23, 24, 32, 102, 103, 104, 122, 1002 })
                Assert.Equal(PluralForm.Few, PluralRules.Select("ru", n), "n=" + n);
            foreach (long n in new long[] { 1, 21, 31, 101, 121, 1001, 1000001 })
                Assert.Equal(PluralForm.One, PluralRules.Select("ru", n), "n=" + n);
            for (long n = 5; n <= 20; n++) Assert.Equal(PluralForm.Many, PluralRules.Select("ru", n), "n=" + n);
            for (long n = 25; n <= 30; n++) Assert.Equal(PluralForm.Many, PluralRules.Select("ru", n), "n=" + n);
            foreach (long n in new long[] { 100, 111, 112, 113, 114, 211, 212, 1011, 1012, 1013, 1014 })
                Assert.Equal(PluralForm.Many, PluralRules.Select("ru", n), "n=" + n);
        }

        [Test]
        public void PluralEn_OneAndOther()
        {
            Assert.Equal(PluralForm.One, PluralRules.Select("en", 1));
            Assert.Equal(PluralForm.Other, PluralRules.Select("en", 2));
            foreach (long n in new long[] { 0, 5, 11, 21, 22, 111, 1000 })
                Assert.Equal(PluralForm.Other, PluralRules.Select("en", n), "n=" + n);
        }

        [Test]
        public void Plural_SignIsIgnoredAndExtremesDoNotThrow()
        {
            Assert.Equal(PluralForm.One, PluralRules.Select("ru", -1));
            Assert.Equal(PluralForm.Few, PluralRules.Select("ru", -2));
            Assert.Equal(PluralForm.Many, PluralRules.Select("ru", -11));
            Assert.Equal(PluralForm.One, PluralRules.Select("en", -1));
            Assert.Equal(PluralForm.Many, PluralRules.Select("ru", long.MinValue));
            Assert.Equal(PluralForm.Other, PluralRules.Select("en", long.MinValue));
            Assert.Equal(PluralForm.Many, PluralRules.Select("ru", long.MaxValue));
        }

        [Test]
        public void Plural_LanguageFamilies()
        {
            Assert.Equal(PluralForm.Few, PluralRules.Select("RU", 3));
            Assert.Equal(PluralForm.Few, PluralRules.Select("ru-RU", 3));
            Assert.Equal(PluralForm.Few, PluralRules.Select("uk", 3));
            Assert.Equal(PluralForm.Few, PluralRules.Select("be", 3));
            Assert.Equal(PluralForm.Other, PluralRules.Select("de", 3));
            Assert.Equal(PluralForm.Other, PluralRules.Select("kk", 3));
            Assert.Equal(PluralForm.One, PluralRules.Select(null, 1));
            Assert.Equal(PluralForm.Other, PluralRules.Select("", 3));
            Assert.True(PluralRules.IsSlavic("ru_RU"));
            Assert.False(PluralRules.IsSlavic("en"));
            Assert.False(PluralRules.IsSlavic("rux"));
        }

        [Test]
        public void PluralRules_FormsSuffixesAndParts()
        {
            Assert.Equal(new[] { PluralForm.One, PluralForm.Few, PluralForm.Many }, PluralRules.FormsOf("ru").ToArray());
            Assert.Equal(new[] { PluralForm.One, PluralForm.Other }, PluralRules.FormsOf("en").ToArray());

            foreach (PluralForm form in new[] { PluralForm.One, PluralForm.Few, PluralForm.Many, PluralForm.Other })
            {
                PluralForm parsed;
                Assert.True(PluralRules.TryParseSuffix(PluralRules.Suffix(form), out parsed));
                Assert.Equal(form, parsed);
                Assert.Equal(form, PluralRules.Candidates(form)[0], "the exact form is tried first");
                Assert.Equal(PluralRules.Candidates(form).Count, PluralRules.Candidates(form).Distinct().Count());
            }

            PluralForm none;
            Assert.False(PluralRules.TryParseSuffix("Few", out none));
            Assert.False(PluralRules.TryParseSuffix("", out none));
            Assert.False(PluralRules.TryParseSuffix(null, out none));
            Assert.False(PluralRules.TryParseSuffix("two", out none));

            Assert.Equal(new[] { PluralForm.Few, PluralForm.Many, PluralForm.Other }, PluralRules.Candidates(PluralForm.Few).ToArray());

            Assert.Equal(0, PluralRules.PartIndex(PluralForm.One, 3));
            Assert.Equal(1, PluralRules.PartIndex(PluralForm.Few, 3));
            Assert.Equal(2, PluralRules.PartIndex(PluralForm.Many, 3));
            Assert.Equal(2, PluralRules.PartIndex(PluralForm.Other, 3));
            Assert.Equal(0, PluralRules.PartIndex(PluralForm.One, 2));
            Assert.Equal(1, PluralRules.PartIndex(PluralForm.Few, 2));
            Assert.Equal(1, PluralRules.PartIndex(PluralForm.Many, 2));
            Assert.Equal(1, PluralRules.PartIndex(PluralForm.Other, 2));
            Assert.Equal(0, PluralRules.PartIndex(PluralForm.Many, 1));
            Assert.Equal(0, PluralRules.PartIndex(PluralForm.Many, 0));
        }

        [Test]
        public void NormalizeLanguage_TrimsAndLowers()
        {
            Assert.Equal("ru", PluralRules.NormalizeLanguage("  RU "));
            Assert.Equal("en", PluralRules.NormalizeLanguage("En"));
            Assert.Equal("ru-ru", PluralRules.NormalizeLanguage("ru-RU"));
            Assert.Equal(string.Empty, PluralRules.NormalizeLanguage(null));
            Assert.Equal(string.Empty, PluralRules.NormalizeLanguage("   "));
        }

        // ---- L10nTables ----

        [Test]
        public void Tables_LoadAndRead()
        {
            var tables = new L10nTables();
            var errors = new List<string>();
            int stored = tables.AddJson("ru", "l10n/ru.json", "{ \"core.hub.title\": \"Моды\", \"core.common.yes\": \"Да\" }", errors);

            Assert.Equal(2, stored);
            Assert.Equal(0, errors.Count);
            Assert.Equal(2, tables.Count("ru"));
            Assert.Equal(0, tables.Count("en"));
            string text;
            Assert.True(tables.TryGet("ru", "core.hub.title", out text));
            Assert.Equal("Моды", text);
            Assert.True(tables.Contains("ru", "core.common.yes"));
            Assert.False(tables.Contains("ru", "core.common.no"));
            Assert.False(tables.Contains("en", "core.common.yes"));
            Assert.Equal("l10n/ru.json", tables.SourceOf("ru", "core.hub.title"));
            Assert.Null(tables.SourceOf("ru", "nope"));
            Assert.Null(tables.SourceOf("en", "core.hub.title"));
        }

        [Test]
        public void Tables_LanguageIsCaseInsensitive()
        {
            var tables = new L10nTables();
            tables.AddJson("RU", "f", "{ \"a\": \"1\" }", null);

            string text;
            Assert.True(tables.TryGet("ru", "a", out text));
            Assert.True(tables.TryGet("Ru", "a", out text));
            Assert.True(tables.Contains("RU", "a"));
            Assert.Equal(1, tables.Count("rU"));
            Assert.Equal(new[] { "ru" }, tables.Languages.ToArray());
        }

        [Test]
        public void Tables_LaterLayerWins()
        {
            var tables = new L10nTables();
            tables.AddJson("ru", "l10n/ru.json", "{ \"a\": \"builtin a\", \"b\": \"builtin b\" }", null);
            int stored = tables.AddJson("ru", "PTMod/l10n/ru.json", "{ \"a\": \"override a\", \"c\": \"new c\" }", null);

            Assert.Equal(2, stored);
            string text;
            tables.TryGet("ru", "a", out text);
            Assert.Equal("override a", text);
            tables.TryGet("ru", "b", out text);
            Assert.Equal("builtin b", text);
            Assert.Equal("PTMod/l10n/ru.json", tables.SourceOf("ru", "a"));
            Assert.Equal("l10n/ru.json", tables.SourceOf("ru", "b"));
            Assert.Equal(3, tables.Count("ru"));
        }

        [Test]
        public void Tables_BadEntryIsSkippedTheRestLoads()
        {
            var tables = new L10nTables();
            var errors = new List<string>();
            int stored = tables.AddJson("ru", "f.json",
                "{ \"a\": \"ok\", \"b\": 5, \"\": \"x\", \"c\": null, \"d\": { \"e\": \"f\" }, \"g\": \"fine\", \"h\": [\"x\"], \"i\": true }", errors);

            Assert.Equal(2, stored);
            Assert.Equal(6, errors.Count);
            Assert.True(errors.Any(e => e.Contains("'b'") && e.Contains("must be a string") && e.Contains("number")), string.Join("; ", errors));
            Assert.True(errors.Any(e => e.Contains("empty key")));
            Assert.True(errors.Any(e => e.Contains("'c'") && e.Contains("null")));
            Assert.True(errors.Any(e => e.Contains("'d'") && e.Contains("object")));
            Assert.True(errors.Any(e => e.Contains("'h'") && e.Contains("array")));
            Assert.True(errors.Any(e => e.Contains("'i'") && e.Contains("boolean")));
            Assert.True(errors.All(e => e.StartsWith("f.json:", StringComparison.Ordinal)));
            Assert.True(tables.Contains("ru", "a"));
            Assert.True(tables.Contains("ru", "g"));
            Assert.False(tables.Contains("ru", "b"));
        }

        [Test]
        public void Tables_NotAnObjectAddsNothing()
        {
            var tables = new L10nTables();
            var errors = new List<string>();

            Assert.Equal(0, tables.AddTable("ru", "f.json", new JsonArray().Add("x"), errors));
            Assert.Equal(0, tables.AddTable("ru", "f.json", null, errors));
            Assert.Equal(0, tables.AddTable("ru", "f.json", JsonValue.Of("text"), errors));
            Assert.Equal(3, errors.Count);
            Assert.True(errors.All(e => e.Contains("must be a JSON object")));
            Assert.Equal(0, tables.Count("ru"));
        }

        [Test]
        public void Tables_SyntaxErrorHasLineAndColumn()
        {
            var tables = new L10nTables();
            var errors = new List<string>();

            int stored = tables.AddJson("ru", "l10n/ru.json", "{\n  \"a\": \"x\",\n  \"b\": \n}", errors);

            Assert.Equal(0, stored);
            Assert.Equal(1, errors.Count);
            Assert.True(errors[0].StartsWith("l10n/ru.json: invalid JSON at line 4", StringComparison.Ordinal), errors[0]);
            Assert.Equal(0, tables.Count("ru"));
            Assert.Equal(0, tables.AddJson("ru", "f", null, errors));
        }

        [Test]
        public void Tables_InvalidLanguageIsRejected()
        {
            var tables = new L10nTables();
            var errors = new List<string>();

            Assert.Equal(0, tables.AddJson("", "f", "{ \"a\": \"1\" }", errors));
            Assert.Equal(0, tables.AddJson(null, "f", "{ \"a\": \"1\" }", errors));
            Assert.Equal(0, tables.AddJson("r u", "f", "{ \"a\": \"1\" }", errors));
            Assert.Equal(0, tables.AddJson("x", "f", "{ \"a\": \"1\" }", errors));
            Assert.Equal(0, tables.AddJson("../ru", "f", "{ \"a\": \"1\" }", errors));
            Assert.Equal(5, errors.Count);
            Assert.True(errors.All(e => e.Contains("invalid language name")));
            Assert.Equal(0, tables.Languages.Count);
        }

        [Test]
        public void Tables_SetAndMissingLookups()
        {
            var tables = new L10nTables();

            Assert.True(tables.Set("en", "k", "v"));
            Assert.False(tables.Set("en", "", "v"));
            Assert.False(tables.Set("en", null, "v"));
            Assert.False(tables.Set("en", "k2", null));
            Assert.False(tables.Set("", "k2", "v"));
            Assert.True(tables.Set("en", "k", "v2", "src"));

            string text;
            Assert.True(tables.TryGet("en", "k", out text));
            Assert.Equal("v2", text);
            Assert.Equal("src", tables.SourceOf("en", "k"));
            Assert.False(tables.TryGet("de", "k", out text));
            Assert.Null(text);
            Assert.False(tables.TryGet("en", null, out text));
            Assert.False(tables.TryGet(null, "k", out text));
            Assert.False(tables.Contains("en", null));
            Assert.Equal(0, tables.Count("de"));
            Assert.Equal(0, tables.Keys("de").Count);
        }

        [Test]
        public void Tables_KeysAndLanguagesAreSorted()
        {
            var tables = new L10nTables();
            tables.AddJson("ru", "f", "{ \"b\": \"1\", \"a\": \"2\", \"c.d\": \"3\" }", null);
            tables.AddJson("en", "f", "{ \"z\": \"1\" }", null);

            Assert.Equal(new[] { "a", "b", "c.d" }, tables.Keys("ru").ToArray());
            Assert.Equal(new[] { "en", "ru" }, tables.Languages.ToArray());
        }

        [Test]
        public void Tables_TextIsKeptVerbatim()
        {
            var tables = new L10nTables();
            tables.AddJson("ru", "f", "{ \"a\": \"  {0} день|{0} дня|{0} дней  \", \"b\": \"line1\\nline2 \\\"q\\\" \\u00e9\" }", null);

            string text;
            tables.TryGet("ru", "a", out text);
            Assert.Equal("  {0} день|{0} дня|{0} дней  ", text);
            tables.TryGet("ru", "b", out text);
            Assert.Equal("line1\nline2 \"q\" é", text);
        }

        [Test]
        public void TablePath_BuiltInAndOverrideNames()
        {
            AssertPath("l10n/ru.json", "ru", null);
            AssertPath("ru.json", "ru", null);
            AssertPath("l10n/en.json", "en", null);
            AssertPath("l10n/ru/event-system.json", "ru", "event-system");
            AssertPath("l10n/en/framework-sample.json", "en", "framework-sample");
            AssertPath("en\\event-system.json", "en", "event-system");
            AssertPath("./l10n/RU/Event-System.JSON", "ru", "Event-System");
            AssertPath("  L10N\\ru.json  ", "ru", null);

            AssertNotPath(null);
            AssertNotPath("");
            AssertNotPath("l10n/ru.txt");
            AssertNotPath("l10n/ru");
            AssertNotPath("l10n/.json");
            AssertNotPath("l10n/ru/a/b.json");
            AssertNotPath("l10n/r u.json");
            AssertNotPath("l10n/ru/../x.json");
            AssertNotPath("l10n/ru/.hidden.json");
            AssertNotPath("l10n/ru/bad name.json");
            AssertNotPath("l10n//x.json");
        }

        private static void AssertPath(string path, string language, string feature)
        {
            string lang, feat;
            Assert.True(L10nTables.TryParseTablePath(path, out lang, out feat), "should parse: " + path);
            Assert.Equal(language, lang, path);
            Assert.Equal(feature, feat, path);
        }

        private static void AssertNotPath(string path)
        {
            string lang, feat;
            Assert.False(L10nTables.TryParseTablePath(path, out lang, out feat), "should not parse: " + path);
            Assert.Null(lang);
            Assert.Null(feat);
        }

        [Test]
        public void SplitPluralKey_BaseAndForm()
        {
            string baseKey;
            PluralForm form;
            Assert.True(L10nTables.TrySplitPluralKey("core.mail.daysLeft.few", out baseKey, out form));
            Assert.Equal("core.mail.daysLeft", baseKey);
            Assert.Equal(PluralForm.Few, form);

            Assert.False(L10nTables.TrySplitPluralKey("core.mail.daysLeft", out baseKey, out form));
            Assert.False(L10nTables.TrySplitPluralKey(".one", out baseKey, out form));
            Assert.False(L10nTables.TrySplitPluralKey("a.", out baseKey, out form));
            Assert.False(L10nTables.TrySplitPluralKey("one", out baseKey, out form));
            Assert.False(L10nTables.TrySplitPluralKey("a.One", out baseKey, out form));
            Assert.False(L10nTables.TrySplitPluralKey(null, out baseKey, out form));
        }

        // ---- ru/en key comparison ----

        [Test]
        public void Compare_KeysInOneLanguageOnly()
        {
            var tables = new L10nTables();
            tables.AddJson("ru", "ru.json", "{ \"shared\": \"1\", \"ru.only\": \"2\", \"a.ru\": \"3\", \"both\": \"4\" }", null);
            tables.AddJson("en", "en.json", "{ \"shared\": \"1\", \"en.only\": \"2\", \"both\": \"4\", \"z.en\": \"5\" }", null);

            L10nMismatchReport report = tables.Compare("ru", "en");

            Assert.Equal("ru", report.LanguageA);
            Assert.Equal("en", report.LanguageB);
            Assert.Equal(new[] { "a.ru", "ru.only" }, report.MissingInB.ToArray());
            Assert.Equal(new[] { "en.only", "z.en" }, report.MissingInA.ToArray());
            Assert.Equal(0, report.IncompletePlurals.Count);
            Assert.Equal(4, report.Count);
            Assert.False(report.IsClean);

            IReadOnlyList<string> lines = report.ToLines();
            Assert.Equal(4, lines.Count);
            Assert.Equal("key 'a.ru' exists in 'ru' but is missing in 'en'", lines[0]);
            Assert.Equal("key 'ru.only' exists in 'ru' but is missing in 'en'", lines[1]);
            Assert.Equal("key 'en.only' exists in 'en' but is missing in 'ru'", lines[2]);
            Assert.Equal("key 'z.en' exists in 'en' but is missing in 'ru'", lines[3]);
        }

        [Test]
        public void Compare_IdenticalKeySetsAreClean()
        {
            var tables = new L10nTables();
            Assert.True(tables.Compare("ru", "en").IsClean, "two empty languages");
            Assert.Equal(0, tables.Compare("ru", "en").ToLines().Count);

            tables.AddJson("ru", "f", "{ \"a\": \"1\", \"b\": \"2\" }", null);
            tables.AddJson("en", "f", "{ \"b\": \"2\", \"a\": \"1\" }", null);
            Assert.True(tables.Compare("ru", "en").IsClean);
            Assert.True(tables.Compare("en", "ru").IsClean);
        }

        [Test]
        public void Compare_PluralGroupsAreComparedAsOneKey()
        {
            // ru has one/few/many, en has one/other: the same logical key, no difference.
            var tables = new L10nTables();
            tables.AddJson("ru", "ru.json",
                "{ \"core.mail.daysLeft.one\": \"a\", \"core.mail.daysLeft.few\": \"b\", \"core.mail.daysLeft.many\": \"c\", \"x\": \"1\" }", null);
            tables.AddJson("en", "en.json",
                "{ \"core.mail.daysLeft.one\": \"a\", \"core.mail.daysLeft.other\": \"c\", \"x\": \"1\" }", null);

            L10nMismatchReport report = tables.Compare("ru", "en");

            Assert.True(report.IsClean, string.Join("; ", report.ToLines()));
        }

        [Test]
        public void Compare_PluralGroupMissingInOneLanguage()
        {
            var tables = new L10nTables();
            tables.AddJson("ru", "ru.json", "{ \"days.one\": \"a\", \"days.few\": \"b\", \"days.many\": \"c\" }", null);
            tables.AddJson("en", "en.json", "{ }", null);

            L10nMismatchReport report = tables.Compare("ru", "en");

            Assert.Equal(new[] { "days" }, report.MissingInB.ToArray());
            Assert.Equal(0, report.MissingInA.Count);
            Assert.Equal(0, report.IncompletePlurals.Count);
        }

        [Test]
        public void Compare_IncompletePluralForms()
        {
            var tables = new L10nTables();
            tables.AddJson("ru", "ru.json", "{ \"days.one\": \"a\", \"days.many\": \"c\" }", null);
            tables.AddJson("en", "en.json", "{ \"days.one\": \"a\" }", null);

            L10nMismatchReport report = tables.Compare("ru", "en");

            Assert.Equal(0, report.MissingInA.Count);
            Assert.Equal(0, report.MissingInB.Count);
            Assert.Equal(new[] { "ru: days.few", "en: days.other" }, report.IncompletePlurals.ToArray());
            Assert.Equal("plural form is missing: ru: days.few", report.ToLines()[0]);
            Assert.False(report.IsClean);
        }

        [Test]
        public void Compare_ManyAndOtherServeEachOther()
        {
            var tables = new L10nTables();
            // ru written with "other" instead of "many"; en written with "many" instead of "other": both read fine.
            tables.AddJson("ru", "ru.json", "{ \"d.one\": \"a\", \"d.few\": \"b\", \"d.other\": \"c\" }", null);
            tables.AddJson("en", "en.json", "{ \"d.one\": \"a\", \"d.many\": \"c\" }", null);

            L10nMismatchReport report = tables.Compare("ru", "en");

            Assert.True(report.IsClean, string.Join("; ", report.ToLines()));
        }

        [Test]
        public void Compare_LoneSuffixKeyIsAnOrdinaryKey()
        {
            var tables = new L10nTables();
            tables.AddJson("ru", "ru.json", "{ \"menu.other\": \"Другое\", \"item.one\": \"Один\" }", null);
            tables.AddJson("en", "en.json", "{ \"menu.other\": \"Other\", \"item.one\": \"One\" }", null);
            Assert.True(tables.Compare("ru", "en").IsClean);

            tables.AddJson("en", "en.json", "{ \"menu.other\": \"Other\" }", null);
            tables.AddJson("ru", "ru.json", "{ \"extra.other\": \"x\" }", null);
            L10nMismatchReport report = tables.Compare("ru", "en");
            Assert.Equal(new[] { "extra.other" }, report.MissingInB.ToArray());
            Assert.Equal(0, report.IncompletePlurals.Count);
        }

        [Test]
        public void Compare_PipeFormMatchesSuffixForm()
        {
            var tables = new L10nTables();
            tables.AddJson("ru", "ru.json", "{ \"x\": \"{0} день|{0} дня|{0} дней\" }", null);
            tables.AddJson("en", "en.json", "{ \"x.one\": \"{0} day\", \"x.other\": \"{0} days\" }", null);

            Assert.True(tables.Compare("ru", "en").IsClean);
        }

        [Test]
        public void Compare_UnknownLanguageCountsAsEmpty()
        {
            var tables = new L10nTables();
            tables.AddJson("ru", "ru.json", "{ \"a\": \"1\", \"b.one\": \"x\", \"b.few\": \"y\", \"b.many\": \"z\" }", null);

            L10nMismatchReport report = tables.Compare("ru", "kk");

            Assert.Equal(new[] { "a", "b" }, report.MissingInB.ToArray());
            Assert.Equal(0, report.MissingInA.Count);
            Assert.True(tables.Compare("de", "kk").IsClean);
        }

        [Test]
        public void Compare_SampleTablesDifferInKnownKeysOnly()
        {
            L10nMismatchReport report = SampleTables().Compare("ru", "en");

            // en lacks ru-only keys and has english-only ones; the t.days group (ru one/few/many, en one/other) is not among the differences.
            Assert.False(report.MissingInB.Contains("t.days"));
            Assert.False(report.MissingInA.Contains("t.days"));
            Assert.True(report.MissingInA.Contains("t.enonly"));
            Assert.True(report.MissingInA.Contains("t.apples"));
            Assert.True(report.MissingInB.Contains("t.ruonly"));
            Assert.True(report.MissingInB.Contains("t.items"));
            Assert.Equal(new[] { "ru: t.nofew.few" }, report.IncompletePlurals.ToArray());
        }

        // ---- LocTextResolver: chain and formatting ----

        [Test]
        public void Chain_CurrentLanguageWins()
        {
            LocTextResolver ru = SampleResolver();
            Assert.Equal("Привет, Мир!", ru.L("t.hello", "Мир"));

            LocTextResolver en = SampleResolver(language: "en");
            Assert.Equal("Hello, World!", en.L("t.hello", "World"));
        }

        [Test]
        public void Chain_RuMissingFallsBackToEn()
        {
            var warnings = new List<string>();
            LocTextResolver resolver = SampleResolver(warnings);

            Assert.Equal("english only", resolver.L("t.enonly"));
            Assert.Equal(0, warnings.Count, "a key found by fallback is not a warning");
        }

        [Test]
        public void Chain_EnMissingFallsBackToRu()
        {
            var warnings = new List<string>();
            LocTextResolver resolver = SampleResolver(warnings, "en");

            Assert.Equal("только русский", resolver.L("t.ruonly"));
            Assert.Equal(0, warnings.Count);
        }

        [Test]
        public void Chain_NothingFoundGivesBracketedKeyAndOneWarning()
        {
            var warnings = new List<string>();
            LocTextResolver resolver = SampleResolver(warnings);

            Assert.Equal("[t.missing]", resolver.L("t.missing"));
            Assert.Equal("[t.missing]", resolver.L("t.missing"));
            Assert.Equal("[t.missing]", resolver.L("t.missing", 1, 2));
            Assert.Equal(1, warnings.Count, "one warning per key and session");
            Assert.True(warnings[0].Contains("t.missing"));

            Assert.Equal("[t.other.missing]", resolver.L("t.other.missing"));
            Assert.Equal(2, warnings.Count);
            Assert.Equal("[t.other.missing]", resolver.L("t.other.missing"));
            Assert.Equal(2, warnings.Count);
        }

        [Test]
        public void Chain_FollowsTheCurrentLanguage()
        {
            LocTextResolver resolver = SampleResolver();
            CollectionEqual(new[] { "ru", "en" }, resolver.FallbackChain);

            resolver.Language = "en";
            CollectionEqual(new[] { "en", "ru" }, resolver.FallbackChain);

            resolver.Language = "kk";
            CollectionEqual(new[] { "kk", "en", "ru" }, resolver.FallbackChain);
            // Kazakh has no table: the english text is next.
            Assert.Equal("Hello, X!", resolver.L("t.hello", "X"));
            Assert.Equal("только русский", resolver.L("t.ruonly"));
        }

        private static void CollectionEqual(string[] expected, IReadOnlyList<string> actual)
        {
            Assert.Equal(expected, actual.ToArray());
        }

        [Test]
        public void Format_ArgumentsAreInvariantCulture()
        {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
                LocTextResolver resolver = SampleResolver();

                Assert.Equal("Курс 1.5", resolver.L("t.rate", 1.5));
                Assert.Equal("Курс 1234567.25", resolver.L("t.rate", 1234567.25));
                Assert.Equal("Курс 7", resolver.L("t.rate", 7));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void Format_NoArgumentsMeansNoFormatting()
        {
            var warnings = new List<string>();
            LocTextResolver resolver = SampleResolver(warnings);

            Assert.Equal("Формула {x}", resolver.L("t.brace"));
            Assert.Equal("Нужно {0} и {1}", resolver.L("t.badformat"));
            Assert.Equal(0, warnings.Count);
        }

        [Test]
        public void Format_BadFormatReturnsTemplateAndWarnsOnce()
        {
            var warnings = new List<string>();
            LocTextResolver resolver = SampleResolver(warnings);

            Assert.Equal("Нужно {0} и {1}", resolver.L("t.badformat", "a"));
            Assert.Equal("Нужно {0} и {1}", resolver.L("t.badformat", "b"));
            Assert.Equal(1, warnings.Count);
            Assert.True(warnings[0].Contains("t.badformat"));

            Assert.Equal("Нужно a и b", resolver.L("t.badformat", "a", "b"));
            Assert.Equal("Формула {x}", resolver.L("t.brace", "z"));
            Assert.Equal(2, warnings.Count, "a brace that is not a placeholder is a format error too");
        }

        [Test]
        public void Lookup_EmptyKeyDoesNotThrow()
        {
            var warnings = new List<string>();
            LocTextResolver resolver = SampleResolver(warnings);

            Assert.Equal("[]", resolver.L(null));
            Assert.Equal("[]", resolver.L(string.Empty));
            Assert.Equal("[]", resolver.Plural(null, 3));
            Assert.Equal(2, warnings.Count, "one warning for L and one for Plural, not one per call");
        }

        [Test]
        public void Lookup_WorksWithoutAWarningSink()
        {
            var resolver = new LocTextResolver(SampleTables());

            Assert.Equal("[t.missing]", resolver.L("t.missing"));
            Assert.Equal("Нужно {0} и {1}", resolver.L("t.badformat", "a"));
        }

        [Test]
        public void Lookup_ThrowingWarningSinkIsContained()
        {
            var resolver = new LocTextResolver(SampleTables(), message => { throw new InvalidOperationException("sink failed"); });

            Assert.Equal("[t.missing]", resolver.L("t.missing"));
            Assert.Equal("Нужно {0} и {1}", resolver.L("t.badformat", "a"));
        }

        [Test]
        public void Constructor_RequiresTables()
        {
            Assert.Throws<ArgumentNullException>(() => new LocTextResolver(null));
        }

        // ---- LocTextResolver: plural ----

        [Test]
        public void Plural_Ac81_PipeForm()
        {
            LocTextResolver resolver = SampleResolver();

            Assert.Equal("22 дня", resolver.Plural("core.mail.daysLeft", 22));
            Assert.Equal("1 день", resolver.Plural("core.mail.daysLeft", 1));
            Assert.Equal("5 дней", resolver.Plural("core.mail.daysLeft", 5));
            Assert.Equal("11 дней", resolver.Plural("core.mail.daysLeft", 11));
            Assert.Equal("21 день", resolver.Plural("core.mail.daysLeft", 21));
            Assert.Equal("111 дней", resolver.Plural("core.mail.daysLeft", 111));
        }

        [Test]
        public void Plural_RuSuffixForms()
        {
            LocTextResolver resolver = SampleResolver();

            Assert.Equal("Остался 1 день", resolver.Plural("t.days", 1));
            Assert.Equal("Осталось 2 дня", resolver.Plural("t.days", 2));
            Assert.Equal("Осталось 5 дней", resolver.Plural("t.days", 5));
            Assert.Equal("Осталось 11 дней", resolver.Plural("t.days", 11));
            Assert.Equal("Остался 21 день", resolver.Plural("t.days", 21));
            Assert.Equal("Осталось 22 дня", resolver.Plural("t.days", 22));
            Assert.Equal("Осталось 111 дней", resolver.Plural("t.days", 111));
            Assert.Equal("Осталось 0 дней", resolver.Plural("t.days", 0));
        }

        [Test]
        public void Plural_EnSuffixForms()
        {
            LocTextResolver resolver = SampleResolver(language: "en");

            Assert.Equal("1 day left", resolver.Plural("t.days", 1));
            Assert.Equal("2 days left", resolver.Plural("t.days", 2));
            Assert.Equal("0 days left", resolver.Plural("t.days", 0));
            Assert.Equal("22 days left", resolver.Plural("t.days", 22));
            Assert.Equal("111 days left", resolver.Plural("t.days", 111));
        }

        [Test]
        public void Plural_FallbackLanguageUsesItsOwnRules()
        {
            // ru lacks t.apples: the english table answers and english rules pick the form (21 is "one" in ru, "other" in en).
            LocTextResolver resolver = SampleResolver();

            Assert.Equal("21 apples", resolver.Plural("t.apples", 21));
            Assert.Equal("1 apple", resolver.Plural("t.apples", 1));
            Assert.Equal("2 apples", resolver.Plural("t.apples", 2));
        }

        [Test]
        public void Plural_MissingFormUsesTheNearestOne()
        {
            LocTextResolver resolver = SampleResolver();

            // t.nofew has only .one and .many: "few" falls back to "many".
            Assert.Equal("одна штука 1", resolver.Plural("t.nofew", 1));
            Assert.Equal("штук 2", resolver.Plural("t.nofew", 2));
            Assert.Equal("штук 5", resolver.Plural("t.nofew", 5));
        }

        [Test]
        public void Plural_NumberIsFirstArgumentAndExtrasFollow()
        {
            LocTextResolver resolver = SampleResolver();

            Assert.Equal("1 предмет (синий)", resolver.Plural("t.items", 1, "синий"));
            Assert.Equal("3 предмета (синий)", resolver.Plural("t.items", 3, "синий"));
            Assert.Equal("12 предметов (красный)", resolver.Plural("t.items", 12, "красный"));
        }

        [Test]
        public void Plural_PipeFormsWithFewerParts()
        {
            LocTextResolver resolver = SampleResolver();

            Assert.Equal("один 1", resolver.Plural("t.pair2", 1));
            Assert.Equal("много 2", resolver.Plural("t.pair2", 2));
            Assert.Equal("много 5", resolver.Plural("t.pair2", 5));
            Assert.Equal("всегда 1", resolver.Plural("t.pair1", 1));
            Assert.Equal("всегда 22", resolver.Plural("t.pair1", 22));
            Assert.Equal("Привет, 5!", resolver.Plural("t.hello", 5), "a bare key without pipes serves every number");
        }

        [Test]
        public void Plural_MissingKeyAndExtremes()
        {
            var warnings = new List<string>();
            LocTextResolver resolver = SampleResolver(warnings);

            Assert.Equal("[t.nope]", resolver.Plural("t.nope", 3));
            Assert.Equal("[t.nope]", resolver.Plural("t.nope", 4));
            Assert.Equal(1, warnings.Count);

            Assert.Equal("Осталось " + long.MinValue.ToString(CultureInfo.InvariantCulture) + " дней", resolver.Plural("t.days", long.MinValue));
            Assert.Equal("Осталось -2 дня", resolver.Plural("t.days", -2));
        }

        // ---- LocTextResolver: LocText ----

        [Test]
        public void Resolve_InlineTextByLanguage()
        {
            LocText text = LocText.Of("Проверка модов", "Mod check");
            LocTextResolver resolver = SampleResolver();

            Assert.Equal("Проверка модов", resolver.Resolve(text));
            resolver.Language = "en";
            Assert.Equal("Mod check", resolver.Resolve(text));
            resolver.Language = "kk";
            Assert.Equal("Mod check", resolver.Resolve(text), "unknown language: en first");
        }

        [Test]
        public void Resolve_InlineFallsBackAcrossLanguages()
        {
            LocTextResolver resolver = SampleResolver();

            Assert.Equal("only english", resolver.Resolve(new LocText(null, null, "only english")));
            Assert.Equal("только русский", resolver.Resolve(new LocText(null, "только русский", null)));
            resolver.Language = "en";
            Assert.Equal("только русский", resolver.Resolve(new LocText(null, "только русский", null)));
            Assert.Equal(string.Empty, resolver.Resolve(new LocText()));
            Assert.Equal(string.Empty, resolver.Resolve(null));
        }

        [Test]
        public void Resolve_KeyThroughTheTables()
        {
            var warnings = new List<string>();
            LocTextResolver resolver = SampleResolver(warnings);
            LocText text = LocText.FromKey("t.hello");

            Assert.Equal("Привет, Мир!", resolver.Resolve(text, "Мир"));
            resolver.Language = "en";
            Assert.Equal("Hello, World!", resolver.Resolve(text, "World"));
            Assert.Equal("только русский", resolver.Resolve(LocText.FromKey("t.ruonly")));
            Assert.Equal(0, warnings.Count);
        }

        [Test]
        public void Resolve_MissingKeyIsBracketedWithOneWarning()
        {
            var warnings = new List<string>();
            LocTextResolver resolver = SampleResolver(warnings);

            Assert.Equal("[t.gone]", resolver.Resolve(LocText.FromKey("t.gone")));
            Assert.Equal("[t.gone]", resolver.Resolve(LocText.FromKey("t.gone")));
            Assert.Equal(1, warnings.Count);
        }

        [Test]
        public void Resolve_KeyWithInlineFallback()
        {
            var warnings = new List<string>();
            LocTextResolver resolver = SampleResolver(warnings);
            var text = new LocText("t.gone", "запасной", "fallback");

            // The key is in no table: the inline text of the current language is used, no warning.
            Assert.Equal("запасной", resolver.Resolve(text));
            resolver.Language = "en";
            Assert.Equal("fallback", resolver.Resolve(text));
            Assert.Equal(0, warnings.Count);

            // A table entry in the current language beats inline text of the same language.
            var both = new LocText("t.hello", "встроенный", "inline");
            Assert.Equal("Hello, X!", resolver.Resolve(both, "X"));
        }

        [Test]
        public void Resolve_FormatsInlineText()
        {
            LocTextResolver resolver = SampleResolver();
            var text = LocText.Of("Осталось {0} дн.", "{0} days left");

            Assert.Equal("Осталось 3 дн.", resolver.Resolve(text, 3));
            Assert.Equal("Осталось {0} дн.", resolver.Resolve(text));
        }

        // ---- LocTextResolver: language and Has ----

        [Test]
        public void Language_DefaultIsRuAndSetterNormalizes()
        {
            LocTextResolver resolver = SampleResolver();
            Assert.Equal("ru", resolver.Language);
            Assert.Equal("ru", LocTextResolver.DefaultLanguage);
            Assert.Equal("en", LocTextResolver.FallbackLanguage);

            resolver.Language = " EN ";
            Assert.Equal("en", resolver.Language);
            resolver.Language = null;
            Assert.Equal("ru", resolver.Language);
            resolver.Language = "en";
            resolver.Language = "   ";
            Assert.Equal("ru", resolver.Language);
        }

        [Test]
        public void Language_ChangeRaisesTheEventOnlyWhenItChanges()
        {
            LocTextResolver resolver = SampleResolver();
            int raised = 0;
            resolver.LanguageChanged += () => raised++;

            resolver.Language = "ru";
            Assert.Equal(0, raised);
            resolver.Language = "en";
            Assert.Equal(1, raised);
            resolver.Language = "EN";
            Assert.Equal(1, raised);
            resolver.Language = "";
            Assert.Equal(2, raised);
        }

        [Test]
        public void Language_ThrowingSubscriberDoesNotStopTheOthers()
        {
            var warnings = new List<string>();
            LocTextResolver resolver = SampleResolver(warnings);
            int before = 0, after = 0;
            resolver.LanguageChanged += () => before++;
            resolver.LanguageChanged += () => { throw new InvalidOperationException("boom"); };
            resolver.LanguageChanged += () => after++;

            resolver.Language = "en";

            Assert.Equal(1, before);
            Assert.Equal(1, after);
            Assert.Equal("en", resolver.Language);
            Assert.Equal(1, warnings.Count);
            Assert.True(warnings[0].Contains("boom"));
        }

        [Test]
        public void Has_KeysAndPluralGroups()
        {
            LocTextResolver resolver = SampleResolver();

            Assert.True(resolver.Has("t.hello"));
            Assert.True(resolver.Has("t.enonly"), "found in the fallback language");
            Assert.True(resolver.Has("t.days"), "a plural group counts");
            Assert.True(resolver.Has("t.apples"));
            Assert.True(resolver.Has("t.days.one"));
            Assert.False(resolver.Has("t.nope"));
            Assert.False(resolver.Has(null));
            Assert.False(resolver.Has(string.Empty));
        }

        // ---- LocText ----

        [Test]
        public void LocText_Basics()
        {
            Assert.True(new LocText().IsEmpty);
            Assert.False(LocText.FromKey("k").IsEmpty);
            Assert.False(LocText.Of("a", null).IsEmpty);
            Assert.True(new LocText("", "", "").IsEmpty);

            var text = new LocText("k", "ру", "en");
            Assert.Equal("k", text.Key);
            Assert.Equal("ру", text.Ru);
            Assert.Equal("en", text.En);
            Assert.Equal("ру", text.InlineFor("ru"));
            Assert.Equal("ру", text.InlineFor("RU"));
            Assert.Equal("en", text.InlineFor("en"));
            Assert.Null(text.InlineFor("de"));
            Assert.Null(text.InlineFor(null));
            Assert.Null(LocText.Of("", "x").InlineFor("ru"));

            Assert.Equal("k", text.ToString());
            Assert.Equal("ру", LocText.Of("ру", "en").ToString());
            Assert.Equal("en", LocText.Of(null, "en").ToString());
            Assert.Equal(string.Empty, new LocText().ToString());
        }

        [Test]
        public void LocText_FromJsonObjectForms()
        {
            var errors = new List<string>();

            LocText inline = LocText.FromJson(Parse("{ \"ru\": \"Проверка модов\", \"en\": \"Mod check\" }"), "$.title", errors);
            Assert.NotNull(inline);
            Assert.Null(inline.Key);
            Assert.Equal("Проверка модов", inline.Ru);
            Assert.Equal("Mod check", inline.En);

            LocText byKey = LocText.FromJson(Parse("{ \"key\": \"es.patent_troll.title\" }"), "$.title", errors);
            Assert.Equal("es.patent_troll.title", byKey.Key);
            Assert.Null(byKey.Ru);

            LocText mixed = LocText.FromJson(Parse("{ \"key\": \"k\", \"ru\": \"р\", \"extra\": 5 }"), "$.title", errors);
            Assert.Equal("k", mixed.Key);
            Assert.Equal("р", mixed.Ru);
            Assert.Null(mixed.En);

            LocText oneLanguage = LocText.FromJson(Parse("{ \"ru\": \"р\", \"en\": \"\", \"key\": null }"), "$.title", errors);
            Assert.Equal("р", oneLanguage.Ru);
            Assert.Null(oneLanguage.En, "an empty string counts as absent");
            Assert.Null(oneLanguage.Key);

            Assert.Equal(0, errors.Count);
        }

        [Test]
        public void LocText_FromJsonAbsentIsNullWithoutError()
        {
            var errors = new List<string>();

            Assert.Null(LocText.FromJson(null, "$.t", errors));
            Assert.Null(LocText.FromJson(JsonValue.Null, "$.t", errors));
            Assert.Equal(0, errors.Count);
        }

        [Test]
        public void LocText_FromJsonErrorsNameThePath()
        {
            var errors = new List<string>();

            Assert.Null(LocText.FromJson(JsonValue.Of("plain"), "$.records[2].title", errors));
            Assert.Null(LocText.FromJson(JsonValue.Of(5), "$.records[2].title", errors));
            Assert.Null(LocText.FromJson(Parse("[1]"), "$.records[2].title", errors));
            Assert.Null(LocText.FromJson(Parse("{ }"), "$.records[2].title", errors));
            Assert.Null(LocText.FromJson(Parse("{ \"ru\": \"\", \"en\": \"\" }"), "$.records[2].title", errors));
            Assert.Null(LocText.FromJson(Parse("{ \"ru\": 5 }"), "$.records[2].title", errors));
            Assert.Null(LocText.FromJson(Parse("{ \"ru\": [\"x\"], \"en\": true }"), "$.records[2].title", errors));

            Assert.Equal(8, errors.Count);
            Assert.True(errors.All(e => e.StartsWith("$.records[2].title", StringComparison.Ordinal)), string.Join("; ", errors));
            Assert.True(errors.Any(e => e.Contains("expected an object") && e.Contains("a string")));
            Assert.True(errors.Any(e => e.Contains("none of 'key', 'ru', 'en'")));
            Assert.True(errors.Any(e => e.StartsWith("$.records[2].title.ru:", StringComparison.Ordinal) && e.Contains("a number")));
            Assert.True(errors.Any(e => e.StartsWith("$.records[2].title.en:", StringComparison.Ordinal) && e.Contains("a boolean")));
        }

        [Test]
        public void LocText_FromJsonWithoutErrorListOrPathDoesNotThrow()
        {
            Assert.Null(LocText.FromJson(JsonValue.Of(1), null, null));
            Assert.Null(LocText.FromJson(Parse("{ }"), string.Empty, null));
            Assert.NotNull(LocText.FromJson(Parse("{ \"ru\": \"x\" }"), null, null));

            var errors = new List<string>();
            LocText.FromJson(JsonValue.Of(1), null, errors);
            Assert.True(errors[0].StartsWith("$:", StringComparison.Ordinal));
        }

        [Test]
        public void LocText_ToJsonRoundTrip()
        {
            var text = new LocText("es.title", "Заголовок \"в кавычках\"", "Title");

            JsonObject json = text.ToJson();
            Assert.Equal(new[] { "key", "ru", "en" }, json.Keys.ToArray());

            var errors = new List<string>();
            LocText back = LocText.FromJson(Parse(ModJson.Write(json, false)), "$", errors);
            Assert.Equal(0, errors.Count);
            Assert.Equal(text.Key, back.Key);
            Assert.Equal(text.Ru, back.Ru);
            Assert.Equal(text.En, back.En);

            Assert.Equal("{\"ru\":\"р\"}", ModJson.Write(LocText.Of("р", null).ToJson(), false));
            Assert.Equal("{}", ModJson.Write(new LocText().ToJson(), false));
        }

        private static JsonNode Parse(string json)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(json, out node, out error), json);
            return node;
        }
    }
}
