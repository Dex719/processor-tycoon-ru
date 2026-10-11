using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PTMod.Core.Logic.L10n;
using PTMod.Features.ResearchTreeUx;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// What the tests of the modifiers report and the compare table share (TSK-010...TSK-013): the shipped Russian and English string tables of the
    /// feature together with the core's (the line "(capped)" is a key of the core), the words and the formatters of the game stood in for by simple
    /// ones. Not a test class.
    /// </summary>
    internal static class UxTestKit
    {
        private const string Slug = "research-tree-ux";

        /// <summary>The resolver of a language over the tables of the feature and of the core, read from the source tree.</summary>
        public static LocTextResolver Resolver(string language)
        {
            string root = Path.Combine(SourceScan.SrcDirOrFail(), "L10n");
            var tables = new L10nTables();
            var problems = new List<string>();
            foreach (string lang in new[] { "ru", "en" })
            {
                string feature = Path.Combine(root, lang, Slug + ".json");
                Assert.True(File.Exists(feature), "missing " + feature);
                tables.AddJson(lang, "l10n/" + lang + "/" + Slug + ".json", SourceScan.ReadText(feature), problems);

                string core = Path.Combine(root, lang + ".json");
                Assert.True(File.Exists(core), "missing " + core);
                tables.AddJson(lang, "l10n/" + lang + ".json", SourceScan.ReadText(core), problems);
            }
            Assert.Equal(0, problems.Count, string.Join("; ", problems));

            var resolver = new LocTextResolver(tables);
            resolver.Language = language;
            return resolver;
        }

        /// <summary>The words of a language and simple formatters: "N дн." / "N days" for a time, "$N" for money, "N MHz" for a frequency.</summary>
        public static TipFormat Format(string language)
        {
            LocTextResolver resolver = Resolver(language);
            bool ru = language == "ru";
            return new TipFormat
            {
                L = (key, args) => resolver.L(key, args),
                Duration = days => days + (ru ? " дн." : " days"),
                Money = amount => "$" + ((long)Math.Round(amount)),
                Frequency = frequency => ((long)Math.Round(frequency)).ToString(CultureInfo.InvariantCulture) + " MHz",
                Has = key => resolver.Has(key),
            };
        }

        /// <summary>A formatter for the compare table that writes plain, predictable texts and takes the words from the shipped tables.</summary>
        public static IFormatter Formatter(string language)
        {
            return new FakeFormatter(Resolver(language));
        }

        /// <summary>The words of a language as a function, for the code that wants only <c>L</c>.</summary>
        public static Func<string, object[], string> Words(string language)
        {
            LocTextResolver resolver = Resolver(language);
            return (key, args) => resolver.L(key, args);
        }
    }

    /// <summary>
    /// The compare formatter of the tests: numbers as invariant text with their unit, so a row reads "65 W" or "3000"; the words come from the
    /// string tables like the game's.
    /// </summary>
    internal sealed class FakeFormatter : IFormatter
    {
        private readonly LocTextResolver _resolver;

        public FakeFormatter(LocTextResolver resolver)
        {
            _resolver = resolver;
        }

        public string Ips(float ips)
        {
            return "ips:" + Number(ips);
        }

        public string Frequency(float frequency)
        {
            return Number(frequency) + " MHz";
        }

        public string Money(float amount)
        {
            return "$" + Number(amount);
        }

        public string Percent(float fraction)
        {
            return Number(fraction * 100f) + "%";
        }

        public string Cores(int coreCount)
        {
            return coreCount == 1 ? "1 core" : coreCount + " cores";
        }

        public string Cache(int level, int steps)
        {
            return "L" + level + ":" + steps;
        }

        public string Number(float value, string unit)
        {
            return Number(value) + (unit ?? string.Empty);
        }

        public string Integer(float value)
        {
            return Number(value);
        }

        public string Text(string key, params object[] args)
        {
            return _resolver.L(key, args);
        }

        private static string Number(float value)
        {
            return Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
