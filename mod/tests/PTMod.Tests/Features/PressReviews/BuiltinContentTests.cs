using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.L10n;
using PTMod.Features.PressReviews;

namespace PTMod.Tests.PressReviews
{
    /// <summary>
    /// The content that ships with the feature (TSK-003): six fictional outlets, the numbers of the review formulas, at least eight quarterly notes and the
    /// quotes, with the values of design.md; the whole of it loads without a single error, every segment is covered in every year, and every text key the
    /// content names exists in both languages.
    /// </summary>
    public class BuiltinContentTests
    {
        // ---- outlets ----

        [Test]
        public void Outlets_LoadWithoutErrors_SixOfThem()
        {
            ContentLoad<Outlet> load = PressContentFixture.LoadOutlets();

            Assert.Equal(0, load.Report.Errors.Count, string.Join("; ", load.Report.Errors));
            Assert.Equal(0, load.Report.Warnings.Count, string.Join("; ", load.Report.Warnings));
            Assert.Equal(0, load.Report.Skipped);
            Assert.Equal(6, load.Records.Count);
            Assert.Equal(new List<string> { "electronics_today", "embedded_digest", "kilobyte", "business_pc", "overclock_world", "silicon_review" },
                new List<string>(Ids(load.Records)));
        }

        private static IEnumerable<string> Ids(IReadOnlyList<Outlet> outlets)
        {
            foreach (Outlet outlet in outlets) yield return outlet.Id;
        }

        [Test]
        public void Outlets_HaveTheValuesOfTheDesign()
        {
            List<Outlet> outlets = PressContentFixture.Outlets();

            CheckOutlet(outlets, "electronics_today", 1971, 1992, "HighEnd,MidRange,LowEnd,Industries", 0, 0, 0, 0, 0, 3, 1971, 1992);
            CheckOutlet(outlets, "embedded_digest", 1971, 2030, "LowEnd,Industries", -0.10f, 0, 0.10f, 0, 1, 3, 0, 0);
            CheckOutlet(outlets, "kilobyte", 1976, 2030, "HighEnd,MidRange,LowEnd", 0, 0, 0, 0, 0, 3, 1993, 1996);
            CheckOutlet(outlets, "business_pc", 1982, 2030, "MidRange,LowEnd", -0.15f, 0.15f, 0, 0, 2, 3, 0, 0);
            CheckOutlet(outlets, "overclock_world", 1988, 2030, "HighEnd,MidRange", 0.15f, -0.10f, 0, -0.05f, -3, 4, 0, 0);
            CheckOutlet(outlets, "silicon_review", 1997, 2030, "HighEnd,MidRange,LowEnd,Industries", 0, 0, 0, 0, -1, 2, 1997, 2030);
        }

        private static void CheckOutlet(List<Outlet> outlets, string id, int from, int to, string segments,
            float performance, float value, float efficiency, float thermal, float bias, float sigma, int fallbackFrom, int fallbackTo)
        {
            Outlet o = PressContentFixture.OutletById(outlets, id);
            Assert.Equal(from, o.YearFrom, id + " first year");
            Assert.Equal(to, o.YearTo, id + " last year");

            var names = new List<string>();
            foreach (Segment segment in o.Segments) names.Add(PressContentValidator.SegmentName(segment));
            Assert.Equal(segments, string.Join(",", names), id + " segments");

            Assert.Near(performance, o.WeightShift[(int)Axis.Performance], 1e-6, id + " performance shift");
            Assert.Near(value, o.WeightShift[(int)Axis.Value], 1e-6, id + " value shift");
            Assert.Near(efficiency, o.WeightShift[(int)Axis.Efficiency], 1e-6, id + " efficiency shift");
            Assert.Near(thermal, o.WeightShift[(int)Axis.Thermal], 1e-6, id + " thermal shift");
            Assert.Near(bias, o.Bias, 1e-6, id + " bias");
            Assert.Near(sigma, o.NoiseSigma, 1e-6, id + " sigma");

            Assert.Equal(fallbackFrom != 0, o.HasFallback, id + " has a fallback interval");
            if (fallbackFrom != 0)
            {
                Assert.Equal(fallbackFrom, o.FallbackFrom, id + " fallback from");
                Assert.Equal(fallbackTo, o.FallbackTo, id + " fallback to");
            }
        }

        [Test]
        public void Outlets_TheDefaultFallbacks_AreElectronicsTodayKiloByteAndSiliconReview()
        {
            // FR-3 item 3: Electronics Today in 1971-1992, KiloByte in 1993-1996, Silicon Review from 1997.
            List<Outlet> outlets = PressContentFixture.Outlets();
            Assert.True(PressContentFixture.OutletById(outlets, "electronics_today").IsFallbackIn(1971));
            Assert.True(PressContentFixture.OutletById(outlets, "electronics_today").IsFallbackIn(1992));
            Assert.False(PressContentFixture.OutletById(outlets, "electronics_today").IsFallbackIn(1993));
            Assert.True(PressContentFixture.OutletById(outlets, "kilobyte").IsFallbackIn(1993));
            Assert.True(PressContentFixture.OutletById(outlets, "kilobyte").IsFallbackIn(1996));
            Assert.False(PressContentFixture.OutletById(outlets, "kilobyte").IsFallbackIn(1997));
            Assert.True(PressContentFixture.OutletById(outlets, "silicon_review").IsFallbackIn(1997));
            Assert.True(PressContentFixture.OutletById(outlets, "silicon_review").IsFallbackIn(2030));

            for (int year = 1971; year <= 2030; year++)
            {
                int count = 0;
                foreach (Outlet outlet in outlets) if (outlet.IsFallbackIn(year)) count++;
                Assert.Equal(1, count, "exactly one fallback outlet in " + year);
            }
        }

        [Test]
        public void Outlets_EverySegmentIsCoveredInEveryYear_WithoutNeedingTheFallback()
        {
            List<Outlet> outlets = PressContentFixture.Outlets();
            Assert.Equal(0, PressContentValidator.FindCoverageHoles(outlets).Count);

            // The fallback is insurance for overridden content: the built-in outlets cover every pair on their own.
            for (int year = 1971; year <= 2030; year++)
            {
                for (int s = 0; s < 4; s++)
                {
                    bool covered = false;
                    foreach (Outlet outlet in outlets) if (outlet.IsActiveFor(year, (Segment)s)) covered = true;
                    Assert.True(covered, year + " " + (Segment)s + " is not covered by a working outlet");
                }
            }
        }

        // ---- the config ----

        [Test]
        public void Config_LoadsWithoutErrorsOrWarnings()
        {
            var report = new ValidationReport();
            PressContentFixture.Config(report);
            Assert.Equal(0, report.Errors.Count, string.Join("; ", report.Errors));
            Assert.Equal(0, report.Warnings.Count, string.Join("; ", report.Warnings));

            ConfigLoad load = ContentMerger.MergeConfig("press-reviews", "review-config.json", PressContentFixture.ContentText("review-config.json"), null, 1);
            Assert.Equal(0, load.Report.Errors.Count, string.Join("; ", load.Report.Errors));
        }

        [Test]
        public void Config_HasTheNumbersOfTheDesign()
        {
            ReviewConfig cfg = PressContentFixture.Config();

            Assert.Equal(7, cfg.DelayMinDays);
            Assert.Equal(21, cfg.DelayMaxDays);
            Assert.Equal(5, cfg.Peers.Count);
            Assert.Equal(3, cfg.Peers.PeriodMonths);
            Assert.Equal(15, cfg.Peers.Candidates);
            Assert.Near(0.6, cfg.Trend.Tau, 1e-6);
            Assert.Near(1.35, ReviewMath.EraGrowth(1984, cfg), 1e-6);
            Assert.Near(1.45, ReviewMath.EraGrowth(1985, cfg), 1e-6);
            Assert.Near(1.30, ReviewMath.EraGrowth(2000, cfg), 1e-6);
            Assert.Near(1.15, ReviewMath.EraGrowth(2010, cfg), 1e-6);
            Assert.Near(65.0, cfg.Curve.Parity, 1e-6);
            Assert.Near(35.0, cfg.Curve.Span, 1e-6);
            Assert.Near(1.5, cfg.Curve.RatioAtTop, 1e-6);
            Assert.Equal(new[] { 0.85f, 0.92f, 1.00f, 1.08f, 1.16f, 1.25f }, cfg.Strictness);
            Assert.Near(95.0, cfg.Thermal.SafeCap, 1e-6);
            Assert.Near(35.0, cfg.Thermal.FullMarksBelowSafe, 1e-6);
            Assert.Near(5.0, cfg.Thermal.ZeroAboveSafe, 1e-6);
            Assert.Equal(new[] { 0.50f, 0.20f, 0.15f, 0.15f }, cfg.WeightsFor(Segment.HighEnd));
            Assert.Equal(new[] { 0.35f, 0.35f, 0.15f, 0.15f }, cfg.WeightsFor(Segment.MidRange));
            Assert.Equal(new[] { 0.25f, 0.45f, 0.15f, 0.15f }, cfg.WeightsFor(Segment.LowEnd));
            Assert.Equal(new[] { 0.20f, 0.30f, 0.40f, 0.10f }, cfg.WeightsFor(Segment.Industries));
            Assert.Near(30.0, cfg.Cap.Gap, 1e-6);
            Assert.Near(0.15, cfg.Cap.MinWeight, 1e-6);
            Assert.Near(70.0, cfg.NoAnalogPerformance, 1e-6);
        }

        [Test]
        public void Config_TheVerdictsAndTheReputationLevels_AreTheDesignOnes()
        {
            ReviewConfig cfg = PressContentFixture.Config();

            int[] verdictMins = { 90, 80, 70, 55, 40, 0 };
            string[] verdictKeys = { "masterpiece", "excellent", "good", "average", "weak", "flop" };
            Assert.Equal(6, cfg.Verdicts.Count);
            for (int i = 0; i < 6; i++)
            {
                Assert.Equal(verdictMins[i], cfg.Verdicts[i].Min);
                Assert.Equal("press.verdict." + verdictKeys[i], cfg.Verdicts[i].Key);
            }

            int[] levelMins = { 80, 65, 36, 21, 0 };
            string[] levelKeys = { "cult", "respected", "ordinary", "dubious", "toxic" };
            Assert.Equal(5, cfg.ReputationLevels.Count);
            for (int i = 0; i < 5; i++)
            {
                Assert.Equal(levelMins[i], cfg.ReputationLevels[i].Min);
                Assert.Equal("press.rep." + levelKeys[i], cfg.ReputationLevels[i].Key);
            }
        }

        [Test]
        public void Config_TheEffectsAwardsDigestAndLimits_AreTheDesignOnes()
        {
            ReviewConfig cfg = PressContentFixture.Config();

            Assert.Near(65.0, cfg.Hype.Parity, 1e-6);
            Assert.Near(0.5, cfg.Hype.Strength, 1e-6);
            Assert.Near(0.6, cfg.Hype.Min, 1e-6);
            Assert.Near(1.5, cfg.Hype.Max, 1e-6);
            Assert.Equal(365, cfg.Hype.DurationDays);
            Assert.Near(65.0, cfg.ReputationPerReview.Parity, 1e-6);
            Assert.Near(5.0, cfg.ReputationPerReview.Divisor, 1e-6);
            Assert.Equal(-6, cfg.ReputationPerReview.Min);
            Assert.Equal(6, cfg.ReputationPerReview.Max);
            Assert.Near(0.3, cfg.ReputationEffect.Strength, 1e-6);
            Assert.Near(50.0, cfg.ReputationEffect.Neutral, 1e-6);
            Assert.True(cfg.ReputationEffect.AffectsAI);

            Assert.Equal(75, cfg.Awards.CpuOfYearMin);
            Assert.Equal(4, cfg.Awards.CpuOfYearRep);
            Assert.Equal(2, cfg.Awards.CompanyOfYearMinReviews);
            Assert.Equal(3, cfg.Awards.CompanyOfYearRep);
            Assert.Equal(45, cfg.Awards.DisappointmentMax);
            Assert.Equal(-3, cfg.Awards.DisappointmentRep);

            Assert.Equal(10, cfg.Digest.MaxLines);
            Assert.Equal(85, cfg.Digest.NotableHigh);
            Assert.Equal(40, cfg.Digest.NotableLow);
            Assert.Equal(65, cfg.Digest.Parity);

            Assert.Equal(2000, cfg.Archive.MaxFull);
            Assert.Equal(5000, cfg.Archive.MaxTotal);
            Assert.Near(30.0, cfg.AdjusterLimits.Axis, 1e-6);
            Assert.Near(20.0, cfg.AdjusterLimits.Overall, 1e-6);
            Assert.Near(0.10, cfg.FlawReveal.Chance, 1e-6);
        }

        [Test]
        public void Config_HasNeitherTheSegmentRuleNorTheDrift()
        {
            JsonObject raw = PressContentFixture.ContentJson("review-config.json");
            Assert.False(raw.ContainsKey("segmentPriceBand"), "the rule that puts a CPU into a segment belongs to the core (segments.priceBand)");
            Assert.False(raw.ContainsKey("drift"), "the drift of the reputation belongs to the core");
            Assert.True(raw.ContainsKey("digest"), "the digest of the rivals' reviews is configured here");
            Assert.Equal(1, raw.GetInt32("schema", 0));
        }

        // ---- the quotes ----

        [Test]
        public void Quotes_EveryAxisHasAtLeastThreeHighAndThreeLow()
        {
            ReviewConfig cfg = PressContentFixture.Config();
            string[] axes = { "performance", "value", "efficiency", "thermal" };

            for (int a = 0; a < 4; a++)
            {
                IList<string> high = cfg.QuotesFor((Axis)a, true);
                IList<string> low = cfg.QuotesFor((Axis)a, false);
                Assert.True(high.Count >= PressContentValidator.MinQuotesPerPool, axes[a] + ": at least 3 high quotes, got " + high.Count);
                Assert.True(low.Count >= PressContentValidator.MinQuotesPerPool, axes[a] + ": at least 3 low quotes, got " + low.Count);
                foreach (string key in high) Assert.True(key.StartsWith("press.quote." + axes[a] + ".high."), "'" + key + "' is not a high quote of " + axes[a]);
                foreach (string key in low) Assert.True(key.StartsWith("press.quote." + axes[a] + ".low."), "'" + key + "' is not a low quote of " + axes[a]);
            }
        }

        // ---- the notes ----

        [Test]
        public void Notes_LoadWithoutErrors_AtLeastEightOfThem()
        {
            ContentLoad<NoteTemplate> load = PressContentFixture.LoadNotes();

            Assert.Equal(0, load.Report.Errors.Count, string.Join("; ", load.Report.Errors));
            Assert.Equal(0, load.Report.Warnings.Count, string.Join("; ", load.Report.Warnings));
            Assert.Equal(0, load.Report.Skipped);
            Assert.True(load.Records.Count >= 8, "at least 8 notes, got " + load.Records.Count);
        }

        [Test]
        public void Notes_CoverEveryKindOfCondition_AndTheNotesOfTheDesignExist()
        {
            IReadOnlyList<NoteTemplate> notes = PressContentFixture.LoadNotes().Records;

            var kinds = new HashSet<NoteConditionType>();
            var ids = new HashSet<string>();
            foreach (NoteTemplate note in notes)
            {
                kinds.Add(note.Condition.Type);
                ids.Add(note.Id);
            }
            Assert.Equal(6, kinds.Count, "every kind of condition has at least one note");
            foreach (string id in new[] { "perf_bar_highend", "price_pressure_low", "industries_efficiency", "pga_shift", "player_rep_up" })
                Assert.True(ids.Contains(id), "the note '" + id + "' of design.md");

            foreach (NoteTemplate note in notes)
            {
                if (note.Id == "perf_bar_highend")
                {
                    Assert.Equal(NoteConditionType.LeaderGrowth, note.Condition.Type);
                    Assert.Equal(Segment.HighEnd, note.Condition.Segment);
                    Assert.Near(0.25, note.Condition.Min.Value, 1e-6);
                    Assert.Equal("press.note.perfBar", note.TextKey);
                    Assert.Equal(new List<string> { "segment", "qualitativeGrowth" }, note.Args);
                    Assert.Equal(4, note.CooldownQuarters);
                    Assert.Near(3.0, note.Weight, 1e-6);
                }
                if (note.Id == "pga_shift")
                {
                    Assert.Equal(NoteConditionType.PgaShift, note.Condition.Type);
                    Assert.Equal(40, note.CooldownQuarters);
                    Assert.Near(5.0, note.Weight, 1e-6);
                }
            }
        }

        // ---- the strings ----

        private static readonly string[] Languages = { "ru", "en" };

        private static void AssertKeysExist(IEnumerable<string> keys, string what)
        {
            foreach (string language in Languages)
            {
                L10nTables tables = PressContentFixture.Tables(language);
                foreach (string key in keys)
                    Assert.True(tables.Contains(language, key), what + ": the key '" + key + "' is missing in " + language);
            }
        }

        [Test]
        public void Strings_TheFeatureHasANameADescriptionAndItsParameters()
        {
            var keys = new List<string> { "press.feature.name", "press.feature.desc" };
            foreach (string param in new[] { "instantReviews", "rivalDigest", "reputationAffectsAI", "debugVerbose" })
            {
                keys.Add("press.param." + param);
                keys.Add("press.param." + param + ".tooltip");
            }
            AssertKeysExist(keys, "feature");
        }

        [Test]
        public void Strings_EveryKeyTheContentNames_ExistsInBothLanguages()
        {
            ReviewConfig cfg = PressContentFixture.Config();
            var keys = new List<string>();
            foreach (VerdictRule rule in cfg.Verdicts) keys.Add(rule.Key);
            foreach (ReputationLevel level in cfg.ReputationLevels) keys.Add(level.Key);
            for (int a = 0; a < 4; a++)
            {
                keys.AddRange(cfg.QuotesFor((Axis)a, true));
                keys.AddRange(cfg.QuotesFor((Axis)a, false));
            }
            foreach (string axis in new[] { "performance", "value", "efficiency", "thermal" }) keys.Add("press.axis." + axis);
            foreach (Outlet outlet in PressContentFixture.Outlets())
            {
                Assert.NotNull(outlet.Name, outlet.Id + " has a name");
                keys.Add(outlet.Name.Key);
            }
            foreach (NoteTemplate note in PressContentFixture.LoadNotes().Records) keys.Add(note.TextKey);

            foreach (string key in keys) Assert.True(key != null && key.StartsWith("press."), "'" + key + "' must start with press.");
            AssertKeysExist(keys, "content");
        }

        [Test]
        public void Strings_TheOutletNames_AreTheSameInTheContentAndInTheTables()
        {
            // The names are written inline in outlets.json and also in the tables; both must say the same thing.
            L10nTables ru = PressContentFixture.Tables("ru");
            L10nTables en = PressContentFixture.Tables("en");
            foreach (Outlet outlet in PressContentFixture.Outlets())
            {
                string text;
                Assert.True(ru.TryGet("ru", outlet.Name.Key, out text));
                Assert.Equal(outlet.Name.Ru, text, outlet.Id + " in Russian");
                Assert.True(en.TryGet("en", outlet.Name.Key, out text));
                Assert.Equal(outlet.Name.En, text, outlet.Id + " in English");
            }
        }

        [Test]
        public void Strings_RussianAndEnglish_HaveTheSameKeys()
        {
            L10nTables ru = PressContentFixture.Tables("ru");
            L10nTables en = PressContentFixture.Tables("en");
            var ruKeys = new List<string>(ru.Keys("ru"));

            Assert.True(ruKeys.Count >= 70, "the strings of the content and the parameters are there, got " + ruKeys.Count);

            // One table with both languages, compared as the core compares them: a plural group (ru one/few/many, en one/other) is one logical key.
            foreach (string key in en.Keys("en"))
            {
                string text;
                Assert.True(en.TryGet("en", key, out text));
                ru.Set("en", key, text);
            }
            var lines = new List<string>(ru.Compare("ru", "en").ToLines());
            Assert.True(lines.Count == 0, "the string files differ:\n  " + string.Join("\n  ", lines));
        }

        [Test]
        public void Strings_FormatWithTheirArguments()
        {
            string[] positional = { "press.note.perfBar", "press.note.pricePressure", "press.note.priceRise", "press.note.playerRepUp", "press.note.playerRepDown", "press.note.newEntrant" };
            foreach (string language in Languages)
            {
                L10nTables tables = PressContentFixture.Tables(language);
                foreach (string key in positional)
                {
                    string text;
                    Assert.True(tables.TryGet(language, key, out text), key);
                    string formatted = string.Format(System.Globalization.CultureInfo.InvariantCulture, text, "FIRST", "SECOND");
                    Assert.True(formatted.Contains("FIRST"), language + ":" + key + " uses {0}");
                }
            }
        }
    }
}
