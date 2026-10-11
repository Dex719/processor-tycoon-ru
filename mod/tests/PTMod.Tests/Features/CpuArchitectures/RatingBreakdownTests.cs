using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.L10n;
using PTMod.Features.CpuArchitectures;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// The formula behind a multiplier, as the tooltips of the cards and of the designer show it (NFR-6, TSK-032): "Высокий сегмент: ×1,05 сродство ×1,04 поддержка ×1,03 экосистема =
    /// ×1,12". The factors are the ones the provider of the rating multiplies - the decomposition gives the same multiplier and the same contribution as <see cref="RatingForecast.One"/>
    /// on a grid of everything - the example of the spec comes out as written, the corridor and the knob are noted, and the lines are composed with the real Russian and English tables.
    /// </summary>
    [Covers(typeof(RatingBreakdown), typeof(RatingTerms), typeof(RatingTerm), typeof(RatingTermValue))]
    public class RatingBreakdownTests
    {
        private static BalanceConfig Stage(int stage)
        {
            BalanceConfig balance = ArchTestKit.BuiltInBalance();
            balance.stage = stage;
            return balance;
        }

        private static L10nTables Tables(string language)
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "L10n", language, ArchTestKit.FeatureId + ".json");
            var tables = new L10nTables();
            var problems = new List<string>();
            tables.AddJson(language, "l10n/" + language + "/" + ArchTestKit.FeatureId + ".json", SourceScan.ReadText(path), problems);
            Assert.True(problems.Count == 0, "the string table " + language + " has problems:\n  " + string.Join("\n  ", problems));
            return tables;
        }

        /// <summary>What <c>ModL10n.L</c> does with a key and its arguments, over the real table of a language.</summary>
        private static Func<string, object[], string> Localizer(string language)
        {
            L10nTables tables = Tables(language);
            return (key, args) =>
            {
                string template;
                Assert.True(tables.TryGet(language, key, out template), language + " lacks " + key);
                return string.Format(CultureInfo.InvariantCulture, template, args);
            };
        }

        // ---- the spec's example ----

        [Test]
        public void TheExampleOfTheSpec_X86InTheHighSegment_RoundsToTheNumbersOfNfr6()
        {
            // 1.05 affinity x 1.04 support (S = 70) x 1.03 ecosystem (E = 0.64) = 1.12 - the example under NFR-6.
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            RatingTerms terms = RatingBreakdown.Of(catalog.Get("x86"), Segment.HighEnd, 1990, Stage(BalanceConfig.Stage2), 70f, 0.64f, 1, false, 1f);

            Assert.Equal("1,05", RatingForecast.Format(terms.Affinity, "ru"));
            Assert.Equal("1,04", RatingForecast.Format(terms.Support, "ru"));
            Assert.Equal("1,03", RatingForecast.Format(terms.Ecosystem, "ru"));
            Assert.Equal("1,12", RatingForecast.Format(terms.Multiplier, "ru"));
            Assert.Equal("1,12", RatingForecast.Format(terms.Contribution, "ru"));
            Assert.False(terms.Clamped);
            Assert.True(terms.StageFull);

            string line = RatingBreakdown.Line(terms, "ru", Localizer("ru"));
            Assert.Equal("Высокий сегмент: ×1,05 сродство ×1,04 поддержка ×1,03 экосистема = ×1,12", line);

            string english = RatingBreakdown.Line(terms, "en", Localizer("en"));
            Assert.Equal("High-end segment: ×1.05 affinity ×1.04 support ×1.03 ecosystem = ×1.12", english);
        }

        [Test]
        public void AtStageOne_OnlyTheAffinityAndTheAgeingAreInTheFormula()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            BalanceConfig stageOne = Stage(BalanceConfig.Stage1);

            RatingTerms x86 = RatingBreakdown.Of(catalog.Get("x86"), Segment.MidRange, 1990, stageOne, 70f, 0.64f, 4, true, 1f);
            Assert.False(x86.StageFull);
            Assert.Near(1.0, x86.Support, 1e-9, "the support is not played at stage 1");
            Assert.Near(1.0, x86.Ecosystem, 1e-9);
            Assert.Near(1.0, x86.Extensions, 1e-9);
            Assert.Near(1.08, x86.Product, 1e-6);
            Assert.Equal("Средний сегмент: ×1,08 сродство = ×1,08", RatingBreakdown.Line(x86, "ru", Localizer("ru")));

            // The default family ages in the consumer segments from 1985 on (D6): in 1990 it is x0.95 and the formula says so.
            RatingTerms proprietary = RatingBreakdown.Of(catalog.Default, Segment.LowEnd, 1990, stageOne, 0f, 0f, 1, false, 1f);
            Assert.Near(0.95, proprietary.Fade, 1e-6);
            Assert.Equal("Низкий сегмент: ×1,00 сродство ×0,95 возраст = ×0,95", RatingBreakdown.Line(proprietary, "ru", Localizer("ru")));

            // ... and not in industry.
            Assert.Equal("Промышленность: ×1,00 сродство = ×1,00", RatingBreakdown.Line(RatingBreakdown.Of(catalog.Default, Segment.Industries, 1990, stageOne, 0f, 0f, 1, false, 1f), "ru", Localizer("ru")));
        }

        [Test]
        public void AtStageTwo_TheExtensionsAreInTheFormulaOnlyWhenTheyCostSomething()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            BalanceConfig stageTwo = Stage(BalanceConfig.Stage2);

            RatingTerms single = RatingBreakdown.Of(catalog.Get("x86"), Segment.HighEnd, 1995, stageTwo, 20f, 0.5f, 1, false, 1f);
            Assert.Equal(new[] { RatingTerm.Affinity, RatingTerm.Support, RatingTerm.Ecosystem }, RatingBreakdown.Shown(single).Select(v => v.Term).ToArray());

            // Four cores at support 20: the gate of the multicore costs 4 % (AC-13.1), and the formula says so.
            RatingTerms quad = RatingBreakdown.Of(catalog.Get("x86"), Segment.HighEnd, 1995, stageTwo, 20f, 0.5f, 4, false, 1f);
            Assert.Near(0.96, quad.Extensions, 1e-6);
            Assert.Equal(new[] { RatingTerm.Affinity, RatingTerm.Support, RatingTerm.Ecosystem, RatingTerm.Extensions }, RatingBreakdown.Shown(quad).Select(v => v.Term).ToArray());
            Assert.True(RatingBreakdown.Line(quad, "ru", Localizer("ru")).Contains("×0,96 расширения"), RatingBreakdown.Line(quad, "ru", Localizer("ru")));

            // With the support the gate asks for, the extension costs nothing and is left out.
            RatingTerms fine = RatingBreakdown.Of(catalog.Get("x86"), Segment.HighEnd, 1995, stageTwo, 60f, 0.5f, 4, false, 1f);
            Assert.False(RatingBreakdown.Shown(fine).Any(v => v.Term == RatingTerm.Extensions));
        }

        [Test]
        public void TheCorridor_IsNotedWhenItCutsTheProduct_AndTheKnobGetsALineOfItsOwn()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            BalanceConfig stageTwo = Stage(BalanceConfig.Stage2);

            // x86-64 in the high segment with the best support and the whole market: 1.10 x 1.10 x 1.05 = 1.27, cut to 1.20.
            RatingTerms top = RatingBreakdown.Of(catalog.Get("x86_64"), Segment.HighEnd, 2010, stageTwo, 100f, 1f, 1, false, 1f);
            Assert.Near(1.27, top.Product, 0.01);
            Assert.Near(1.20, top.Multiplier, 1e-6);
            Assert.True(top.Clamped);
            string line = RatingBreakdown.Line(top, "ru", Localizer("ru"));
            Assert.True(line.EndsWith("= ×1,20 (предел вклада архитектуры)", StringComparison.Ordinal), line);
            Assert.True(RatingBreakdown.Line(top, "en", Localizer("en")).EndsWith("= ×1.20 (the limit of an architecture's effect)", StringComparison.Ordinal));

            // The knob at 150 % widens the deviation (1.20 -> 1.30); the line of the formula keeps the multiplier, the next line says what the market gets.
            RatingTerms[] all = RatingBreakdown.All(catalog.Get("x86_64"), 2010, stageTwo, 100f, 1f, 1, false, 1.5f);
            Assert.Near(1.30, all[0].Contribution, 1e-6);
            string[] lines = RatingBreakdown.Lines(all, 1.5f, "ru", Localizer("ru")).Split('\n');
            Assert.Equal(5, lines.Length, "four segments and the knob");
            Assert.True(lines[0].StartsWith("Высокий сегмент:", StringComparison.Ordinal) && lines[0].Contains("= ×1,20"), lines[0]);
            Assert.True(lines[1].StartsWith("Средний сегмент:", StringComparison.Ordinal) && lines[2].StartsWith("Низкий сегмент:", StringComparison.Ordinal)
                        && lines[3].StartsWith("Промышленность:", StringComparison.Ordinal));
            Assert.True(lines[4].StartsWith("Ручка силы в хабе — 150 %: рынок получает ВС ×1,30", StringComparison.Ordinal), lines[4]);

            // At 100 % there is no such line, and at 0 % there is: the market gets x1.
            Assert.Equal(4, RatingBreakdown.Lines(RatingBreakdown.All(catalog.Get("x86_64"), 2010, stageTwo, 100f, 1f, 1, false, 1f), 1f, "ru", Localizer("ru")).Split('\n').Length);
            string[] off = RatingBreakdown.Lines(RatingBreakdown.All(catalog.Get("x86_64"), 2010, stageTwo, 100f, 1f, 1, false, 0f), 0f, "en", Localizer("en")).Split('\n');
            Assert.Equal(5, off.Length);
            Assert.True(off[4].StartsWith("Strength of the effect in the hub: 0%, the market gets", StringComparison.Ordinal), off[4]);
            Assert.True(off[4].Contains("×1.00") && !off[4].Contains("×1.20"), "with the knob at 0 % the market gets x1: " + off[4]);
        }

        // ---- the same numbers as the provider ----

        [Test]
        public void TheDecomposition_GivesTheMultiplierAndTheContributionOfTheProvider_OnAGridOfEverything()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            var inputs = new RatingInputs();
            long checks = 0;

            foreach (int stage in new[] { BalanceConfig.Stage1, BalanceConfig.Stage2 })
            {
                BalanceConfig balance = Stage(stage);
                foreach (ArchFamily family in catalog.Families)
                {
                    for (int year = 1971; year <= 2040; year += 7)
                    {
                        for (int s = 0; s < 4; s++)
                        {
                            foreach (float support in new[] { 0f, 25f, 40f, 70f, 100f })
                            {
                                foreach (float eco in new[] { -1f, 0f, 0.2f, 0.64f, 1f })
                                {
                                    foreach (int cores in new[] { 1, 2, 8 })
                                    {
                                        foreach (float strength in new[] { 0f, 1f, 1.5f })
                                        {
                                            RatingTerms terms = RatingBreakdown.Of(family, (Segment)s, year, balance, support, eco, cores, cores > 1, strength);
                                            float contribution = RatingForecast.One(inputs, family, (Segment)s, year, balance, support, eco, cores, cores > 1, strength);
                                            Assert.Near(contribution, terms.Contribution, 1e-6, family.Id + " " + (Segment)s + " " + year + " stage " + stage);

                                            // The corridor and the product.
                                            float clamped = Math.Max(0.80f, Math.Min(1.20f, terms.Product));
                                            Assert.Near(clamped, terms.Multiplier, 1e-5, "multiplier " + family.Id);
                                            Assert.Equal(Math.Abs(terms.Product - terms.Multiplier) > 1e-5f, terms.Clamped);
                                            if (stage == BalanceConfig.Stage1)
                                            {
                                                Assert.Near(terms.Affinity * terms.Fade, terms.Product, 1e-6, "stage 1: affinity x ageing");
                                                Assert.Near(1.0, terms.Support * terms.Ecosystem * terms.Extensions, 1e-9);
                                            }
                                            checks++;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            Assert.True(checks > 20000, "the grid has " + checks + " points");
        }

        [Test]
        public void ANeutralInput_GivesTheNeutralTerms_NeverAnException()
        {
            RatingTerms none = RatingBreakdown.Of(null, Segment.HighEnd, 1990, Stage(BalanceConfig.Stage2), 50f, 0.5f, 1, false, 1f);
            Assert.Near(1.0, none.Contribution, 1e-9);
            Assert.Near(1.0, none.Multiplier, 1e-9);
            Assert.False(none.Clamped);

            ArchFamily x86 = ArchTestKit.BuiltInCatalog().Get("x86");
            Assert.Near(1.0, RatingBreakdown.Of(x86, Segment.Any, 1990, Stage(BalanceConfig.Stage2), 50f, 0.5f, 1, false, 1f).Contribution, 1e-9, "no segment, no effect");
            Assert.Near(1.0, RatingBreakdown.Of(x86, Segment.HighEnd, 1990, null, 50f, 0.5f, 1, false, 1f).Contribution, 1e-9, "no balance, no effect");

            Assert.Equal(0, RatingBreakdown.Shown(null).Count);
            Assert.Equal(string.Empty, RatingBreakdown.Lines(null, 1f, "ru", Localizer("ru")));
            Assert.Throws<ArgumentNullException>(() => RatingBreakdown.Line(none, "ru", null));
            Assert.Throws<ArgumentNullException>(() => RatingBreakdown.Line(null, "ru", Localizer("ru")));
            Assert.Throws<ArgumentNullException>(() => RatingBreakdown.Lines(new RatingTerms[0], 1f, "ru", null));
        }

        [Test]
        public void TheKeys_OfTheFactorsAndTheSegments_AreTheOnesOfTheTables()
        {
            Assert.Equal("arch.card.tip.affinity", RatingBreakdown.KeyOf(RatingTerm.Affinity));
            Assert.Equal("arch.card.tip.fade", RatingBreakdown.KeyOf(RatingTerm.Fade));
            Assert.Equal("arch.card.tip.support", RatingBreakdown.KeyOf(RatingTerm.Support));
            Assert.Equal("arch.card.tip.eco", RatingBreakdown.KeyOf(RatingTerm.Ecosystem));
            Assert.Equal("arch.card.tip.ext", RatingBreakdown.KeyOf(RatingTerm.Extensions));
            Assert.Equal("arch.segment.high", RatingBreakdown.SegmentKey(Segment.HighEnd));
            Assert.Equal("arch.segment.mid", RatingBreakdown.SegmentKey(Segment.MidRange));
            Assert.Equal("arch.segment.low", RatingBreakdown.SegmentKey(Segment.LowEnd));
            Assert.Equal("arch.segment.industry", RatingBreakdown.SegmentKey(Segment.Industries));

            // Every one of them is in both tables, with the arguments the composition passes: one number for a factor, three for the line, none for a segment and the limit.
            foreach (string language in new[] { "ru", "en" })
            {
                L10nTables tables = Tables(language);
                foreach (string key in new[] { "arch.card.tip.affinity", "arch.card.tip.fade", "arch.card.tip.support", "arch.card.tip.eco", "arch.card.tip.ext" })
                {
                    Assert.Equal("{0}", Placeholders(tables, language, key), language + " " + key);
                }
                Assert.Equal("{0}{1}{2}", Placeholders(tables, language, "arch.card.tip.line"), language + " line");
                Assert.Equal("{0}{1}", Placeholders(tables, language, "arch.card.tip.strength"), language + " strength");
                Assert.Equal("{0}", Placeholders(tables, language, "arch.card.tip.assumed"), language + " assumed");
                foreach (string key in new[] { "arch.segment.high", "arch.segment.mid", "arch.segment.low", "arch.segment.industry", "arch.card.tip.rating", "arch.card.tip.limit" })
                {
                    Assert.Equal("", Placeholders(tables, language, key), language + " " + key);
                }
            }
        }

        private static string Placeholders(L10nTables tables, string language, string key)
        {
            string text;
            Assert.True(tables.TryGet(language, key, out text), language + " lacks " + key);
            var found = new SortedSet<string>(Regex.Matches(text, @"\{(\d+)(?::[^}]*)?\}").Cast<Match>().Select(m => "{" + m.Groups[1].Value + "}"), StringComparer.Ordinal);
            return string.Concat(found.ToArray());
        }

        // ---- the wiring ----

        [Test]
        public void TheCardAndTheDesigner_ShowTheFormulaFromTheSameFunction()
        {
            string root = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "CpuArchitectures");
            string card = SourceScan.Strip(SourceScan.ReadText(Path.Combine(root, "UI", "FamilyCard.cs")), false);
            string designer = SourceScan.Strip(SourceScan.ReadText(Path.Combine(root, "Designer", "DesignerIntegration.cs")), false);
            string text = SourceScan.Strip(SourceScan.ReadText(Path.Combine(root, "UI", "RatingTooltipText.cs")), false);

            Assert.True(card.Contains("RatingBreakdown.All(_family, year, balance, support, _feature.EcosystemOf(_family.Id), 1, false, strength)"), "the card: the factors of the provider for a CPU without extensions");
            Assert.True(card.Contains("RatingTooltipText.Lines(terms, strength, ModL10n.Language)"));
            Assert.True(card.Contains("SupportMath.StartSupport(_family, families, _feature.Catalog, balance.support)"), "an unresearched family is shown with the support it would start with");
            Assert.True(card.Contains("ArchParts.Market"), "the formula is shown only while the rating works");

            Assert.True(designer.Contains("ModUI.Tooltip(line.gameObject, RatingTooltip);"), "the line of the designer carries the formula");
            Assert.True(designer.Contains("RatingBreakdown.All(family, year, balance, support, _feature.EcosystemOf(family.Id), DesignContext.CoreCount, DesignContext.Smt, _feature.RatingStrength)"),
                        "the designer: the cores and SMT of the design on the screen");
            Assert.True(text.Contains("RatingBreakdown.Lines(terms, strength, language, Localize)") && text.Contains("ModL10n.L(key, args)"));
        }
    }
}
