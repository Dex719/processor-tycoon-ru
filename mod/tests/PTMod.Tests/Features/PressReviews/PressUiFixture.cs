using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.L10n;
using PTMod.Features.PressReviews;

namespace PTMod.Tests.PressReviews
{
    /// <summary>
    /// Builders for the tests of the card, the letter, the list and the tables: the strings of the feature as a <see cref="PressText"/> (a key that is not in the
    /// table fails the test, so a string the code asks for is proved to exist), and reviews of every kind. Internal, so the runner never discovers it.
    /// </summary>
    internal static class PressUiFixture
    {
        /// <summary>The strings of <c>L10n/&lt;language&gt;/press-reviews.json</c> as the lookup the pure builders take; arguments fill <c>{0}</c> … like <c>ModL10n.L</c>.</summary>
        public static PressText Text(string language)
        {
            L10nTables tables = PressContentFixture.Tables(language);
            return Text(tables, language, null);
        }

        /// <summary>The same with extra strings (the keys of other features, which are not in the table of this one) that win over the table.</summary>
        public static PressText Text(string language, IDictionary<string, string> extra)
        {
            return Text(PressContentFixture.Tables(language), language, extra);
        }

        private static PressText Text(L10nTables tables, string language, IDictionary<string, string> extra)
        {
            return (key, args) =>
            {
                string text;
                if (extra != null && extra.TryGetValue(key, out text)) return string.Format(CultureInfo.InvariantCulture, text, args);

                Assert.True(tables.TryGet(language, key, out text), "the string '" + key + "' is not in the " + language + " table of press-reviews");
                return string.Format(CultureInfo.InvariantCulture, text, args);
            };
        }

        public static string OutletName(string id)
        {
            switch (id)
            {
                case "kilobyte": return "КилоБайт";
                case "business_pc": return "Деловой ПК";
                case "overclock_world": return "Мир разгона";
                default: return id;
            }
        }

        /// <summary>A full review as the scheduler files it: the axes of AC-2.1, three outlets, three reference CPUs, +1 reputation, a hype of 1.04.</summary>
        public static ReviewRecord Review(int cpuSaveId = 7, string cpuName = "K6-300", string companyKey = "ai:1", string companyName = "AMD",
            int metascore = 68, string publishedOn = "1995-03-15", int segment = 1)
        {
            return new ReviewRecord
            {
                cpuSaveId = cpuSaveId, cpuName = cpuName, companyKey = companyKey, companyName = companyName,
                releasedOn = "1995-03-01", publishedOn = publishedOn, segment = segment,
                axes = new[] { 73.2f, 65.0f, 55.9f, 62.5f }, metascore = metascore, verdictKey = VerdictOf(metascore), noAnalog = false,
                outlets = new List<OutletLine>
                {
                    new OutletLine { outletId = "kilobyte", score = 69, quoteKey = "press.quote.performance.high.2" },
                    new OutletLine { outletId = "business_pc", score = 71, quoteKey = "press.quote.value.high.1" },
                    new OutletLine { outletId = "overclock_world", score = 64, quoteKey = "press.quote.efficiency.low.1" },
                },
                peerSaveIds = new List<int> { 4410, 4502, 4777 }, notes = new List<string>(),
                reputationDelta = 1, hypeAtPublish = 1.04f, mailId = 0, compact = false,
            };
        }

        /// <summary>The short form of an old record: no axes, no marks of the outlets, no reference CPUs.</summary>
        public static ReviewRecord CompactReview(int cpuSaveId = 3000, int metascore = 81)
        {
            return new ReviewRecord
            {
                cpuSaveId = cpuSaveId, cpuName = "Old-1", companyKey = "player", companyName = "Me", releasedOn = "1990-01-10", publishedOn = "1990-01-24",
                segment = 0, axes = new float[0], metascore = metascore, verdictKey = VerdictOf(metascore), compact = true,
            };
        }

        public static string VerdictOf(int metascore)
        {
            return OutletPanel.VerdictKey(metascore, ReviewConfig.CreateDefault());
        }

        /// <summary>The context of a card: the real strings, the outlet names above, the reference CPUs named like the game does (<c>P5-90 (Intel)</c>).</summary>
        public static ReviewCardContext Context(string language = "ru", ReputationStep step = null)
        {
            return new ReviewCardContext
            {
                Text = Text(language),
                OutletName = OutletName,
                PeerName = id => id == 4410 ? "P5-90 (Intel)" : id == 4502 ? "K5-100 (AMD)" : id == 4777 ? "6x86 (Cyrix)" : null,
                Reputation = step,
                Config = ReviewConfig.CreateDefault(),
            };
        }
    }
}
