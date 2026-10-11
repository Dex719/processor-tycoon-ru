using System;
using System.Collections.Generic;
using System.Reflection;
using PTMod.Core;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Json;
using PTMod.Features.PressReviews;

namespace PTMod.Tests.PressReviews
{
    /// <summary>
    /// The readers and checks of the content (FR-3, FR-10, TSK-002): a broken outlet is skipped, weights are cut and normalized, the numbers keep their
    /// ranges, an unknown kind of note is dropped, the coverage of the segments by the outlets over the years is checked, the segment rule is not part
    /// of the content.
    /// </summary>
    [Covers(typeof(PressContentValidator), typeof(ValidationReport), typeof(Outlet), typeof(NoteTemplate), typeof(NoteCondition), typeof(NoteConditionType),
        typeof(ReviewConfig), typeof(PeerConfig), typeof(EraRule), typeof(TrendConfig), typeof(CurveConfig), typeof(ThermalConfig), typeof(CapConfig),
        typeof(VerdictRule), typeof(HypeConfig), typeof(RepDeltaConfig), typeof(ReputationEffectConfig), typeof(AwardsConfig), typeof(ReputationLevel),
        typeof(DigestConfig), typeof(ArchiveConfig), typeof(AdjusterLimits), typeof(FlawRevealConfig))]
    public class ContentValidationTests
    {
        private static JsonObject Obj(string json)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(json, out node, out error), "bad test JSON: " + error + "\n" + json);
            return (JsonObject)node;
        }

        private const string GoodOutlet =
            @"{ ""id"": ""kilobyte"", ""name"": { ""ru"": ""КилоБайт"", ""en"": ""KiloByte"" }, ""years"": [1976, 2030],
                ""segments"": [""HighEnd"", ""MidRange""], ""weightShift"": { ""performance"": 0.1, ""value"": -0.1 },
                ""bias"": 2, ""noiseSigma"": 3, ""fallback"": { ""from"": 1993, ""to"": 1996 } }";

        private static Outlet ReadOutlet(string json, out List<string> messages)
        {
            messages = new List<string>();
            return PressContentValidator.ParseOutlet(Obj(json), messages);
        }

        // ---- outlets ----

        [Test]
        public void ParseOutlet_AGoodRecord_IsRead()
        {
            List<string> messages;
            Outlet outlet = ReadOutlet(GoodOutlet, out messages);

            Assert.NotNull(outlet, string.Join("; ", messages));
            Assert.Equal(0, messages.Count);
            Assert.Equal("kilobyte", outlet.Id);
            Assert.Equal(1976, outlet.YearFrom);
            Assert.Equal(2030, outlet.YearTo);
            Assert.Equal(new List<Segment> { Segment.HighEnd, Segment.MidRange }, outlet.Segments);
            Assert.Near(0.1, outlet.WeightShift[(int)Axis.Performance], 1e-6);
            Assert.Near(-0.1, outlet.WeightShift[(int)Axis.Value], 1e-6);
            Assert.Near(0.0, outlet.WeightShift[(int)Axis.Thermal], 1e-6);
            Assert.Near(2.0, outlet.Bias, 1e-6);
            Assert.Near(3.0, outlet.NoiseSigma, 1e-6);
            Assert.True(outlet.HasFallback);
            Assert.Equal(1993, outlet.FallbackFrom);
            Assert.Equal(1996, outlet.FallbackTo);
            Assert.Equal("КилоБайт", outlet.Name.Ru);
            Assert.True(outlet.IsActiveFor(1990, Segment.HighEnd));
            Assert.False(outlet.IsActiveFor(1990, Segment.LowEnd), "a segment it does not review");
            Assert.False(outlet.IsActiveFor(1975, Segment.HighEnd), "before it opened");
            Assert.True(outlet.IsFallbackIn(1995));
            Assert.False(outlet.IsFallbackIn(1997));
        }

        [Test]
        public void ParseOutlet_AMinimalRecord_GetsTheDefaults()
        {
            List<string> messages;
            Outlet outlet = ReadOutlet(@"{ ""id"": ""min"", ""name"": { ""en"": ""Min"" }, ""years"": [1971, 2030], ""segments"": [""LowEnd""] }", out messages);

            Assert.NotNull(outlet, string.Join("; ", messages));
            Assert.Near(0.0, outlet.Bias, 0.0);
            Assert.Near(3.0, outlet.NoiseSigma, 0.0);
            Assert.False(outlet.HasFallback);
            Assert.Equal(new float[] { 0f, 0f, 0f, 0f }, outlet.WeightShift);
        }

        [Test]
        public void ParseOutlet_ABrokenRecord_IsRefusedWithAReason()
        {
            string[] broken =
            {
                @"{ ""name"": { ""en"": ""X"" }, ""years"": [1971, 2030], ""segments"": [""LowEnd""] }",                        // no id
                @"{ ""id"": ""a/b"", ""name"": { ""en"": ""X"" }, ""years"": [1971, 2030], ""segments"": [""LowEnd""] }",         // an id that is not a plain name
                @"{ ""id"": ""x"", ""years"": [1971, 2030], ""segments"": [""LowEnd""] }",                                        // no name
                @"{ ""id"": ""x"", ""name"": { }, ""years"": [1971, 2030], ""segments"": [""LowEnd""] }",                         // an empty name
                @"{ ""id"": ""x"", ""name"": { ""en"": ""X"" }, ""segments"": [""LowEnd""] }",                                    // no years
                @"{ ""id"": ""x"", ""name"": { ""en"": ""X"" }, ""years"": [1971], ""segments"": [""LowEnd""] }",                 // one year
                @"{ ""id"": ""x"", ""name"": { ""en"": ""X"" }, ""years"": [2030, 1971], ""segments"": [""LowEnd""] }",           // backwards
                @"{ ""id"": ""x"", ""name"": { ""en"": ""X"" }, ""years"": [""a"", ""b""], ""segments"": [""LowEnd""] }",         // not numbers
                @"{ ""id"": ""x"", ""name"": { ""en"": ""X"" }, ""years"": [1971.5, 2030], ""segments"": [""LowEnd""] }",         // not a whole year
                @"{ ""id"": ""x"", ""name"": { ""en"": ""X"" }, ""years"": [1971, 2030] }",                                       // no segments
                @"{ ""id"": ""x"", ""name"": { ""en"": ""X"" }, ""years"": [1971, 2030], ""segments"": [] }",                     // none listed
                @"{ ""id"": ""x"", ""name"": { ""en"": ""X"" }, ""years"": [1971, 2030], ""segments"": [""Cheap""] }",            // unknown segment
                @"{ ""id"": ""x"", ""name"": { ""en"": ""X"" }, ""years"": [1971, 2030], ""segments"": [3] }",                    // not a name
                @"{ ""id"": ""x"", ""name"": { ""en"": ""X"" }, ""years"": [1971, 2030], ""segments"": [""LowEnd""], ""bias"": ""high"" }",
                @"{ ""id"": ""x"", ""name"": { ""en"": ""X"" }, ""years"": [1971, 2030], ""segments"": [""LowEnd""], ""weightShift"": 5 }",
                @"{ ""id"": ""x"", ""name"": { ""en"": ""X"" }, ""years"": [1971, 2030], ""segments"": [""LowEnd""], ""fallback"": { ""from"": 1990 } }",
                @"{ ""id"": ""x"", ""name"": { ""en"": ""X"" }, ""years"": [1971, 2030], ""segments"": [""LowEnd""], ""fallback"": { ""from"": 2000, ""to"": 1990 } }",
                @"{ ""id"": ""x"", ""name"": { ""en"": ""X"" }, ""years"": [1971, 2030], ""segments"": [""LowEnd""], ""fallback"": 5 }",
            };

            for (int i = 0; i < broken.Length; i++)
            {
                List<string> messages;
                Outlet outlet = ReadOutlet(broken[i], out messages);
                Assert.Null(outlet, "record " + i + " must be refused:\n" + broken[i]);
                Assert.True(messages.Count > 0, "record " + i + " must say why");
            }
        }

        [Test]
        public void ParseOutlet_ARecordThatIsNotAnObject_IsRefused()
        {
            var messages = new List<string>();
            Assert.Null(PressContentValidator.ParseOutlet(null, messages));
            Assert.True(messages.Count > 0);
        }

        [Test]
        public void ParseOutlet_ValuesOutOfRange_AreClippedWithAWarning()
        {
            List<string> messages;
            Outlet outlet = ReadOutlet(@"{ ""id"": ""x"", ""name"": { ""en"": ""X"" }, ""years"": [1971, 2030], ""segments"": [""LowEnd""],
                                       ""weightShift"": { ""performance"": 5, ""thermal"": -9 }, ""bias"": 99, ""noiseSigma"": -4 }", out messages);

            Assert.NotNull(outlet, string.Join("; ", messages));
            Assert.Near(1.0, outlet.WeightShift[(int)Axis.Performance], 1e-6, "a shift is at most 1");
            Assert.Near(-1.0, outlet.WeightShift[(int)Axis.Thermal], 1e-6);
            Assert.Near(30.0, outlet.Bias, 1e-6, "a bias is at most 30");
            Assert.Near(0.0, outlet.NoiseSigma, 1e-6, "a sigma is not negative");
            Assert.True(messages.Count >= 4, "each clip is a warning: " + string.Join("; ", messages));
        }

        [Test]
        public void ParseOutlet_UnknownMembersAndAxes_AreWarnings()
        {
            List<string> messages;
            Outlet outlet = ReadOutlet(@"{ ""id"": ""x"", ""name"": { ""en"": ""X"" }, ""years"": [1971, 2030], ""segments"": [""LowEnd""],
                                       ""colour"": ""red"", ""weightShift"": { ""charm"": 0.2 } }", out messages);

            Assert.NotNull(outlet);
            Assert.Equal(2, messages.Count, string.Join("; ", messages));
        }

        [Test]
        public void ParseOutlet_SegmentNames_IgnoreTheCase_AndDuplicatesAreMerged()
        {
            List<string> messages;
            Outlet outlet = ReadOutlet(@"{ ""id"": ""x"", ""name"": { ""en"": ""X"" }, ""years"": [1971, 2030], ""segments"": [""highend"", ""HighEnd"", ""INDUSTRIES""] }", out messages);

            Assert.NotNull(outlet, string.Join("; ", messages));
            Assert.Equal(new List<Segment> { Segment.HighEnd, Segment.Industries }, outlet.Segments);
        }

        [Test]
        public void ABrokenOutlet_IsSkipped_TheOthersLoad()
        {
            string text = @"{ ""schema"": 1, ""records"": [
                " + GoodOutlet + @",
                { ""id"": ""backwards"", ""name"": { ""en"": ""B"" }, ""years"": [2030, 1971], ""segments"": [""LowEnd""] },
                { ""id"": ""nameless"", ""years"": [1971, 2030], ""segments"": [""LowEnd""] },
                { ""id"": ""second"", ""name"": { ""en"": ""S"" }, ""years"": [1971, 2030], ""segments"": [""Industries""] } ] }";

            ContentLoad<Outlet> load = ContentMerger.MergeRecords<Outlet>("press-reviews", "outlets.json", text, null, PressContentValidator.ParseOutlet, null, 1);

            Assert.Equal(2, load.Records.Count);
            Assert.Equal("kilobyte", load.Records[0].Id);
            Assert.Equal("second", load.Records[1].Id);
            Assert.Equal(2, load.Report.Skipped);
            Assert.Equal(2, load.Report.Errors.Count);
        }

        // ---- the config ----

        [Test]
        public void ParseConfig_NegativeWeights_AreCutAtZeroAndTheRestNormalized()
        {
            var report = new ValidationReport();
            ReviewConfig cfg = PressContentValidator.ParseConfig(Obj(
                @"{ ""weights"": { ""HighEnd"": { ""performance"": -0.5, ""value"": 1, ""efficiency"": 1, ""thermal"": 2 } } }"), report);

            float[] w = cfg.WeightsFor(Segment.HighEnd);
            Assert.Near(0.0, w[0], 1e-6, "negative cut to 0");
            Assert.Near(0.25, w[1], 1e-6);
            Assert.Near(0.25, w[2], 1e-6);
            Assert.Near(0.5, w[3], 1e-6);
            Assert.Near(1.0, w[0] + w[1] + w[2] + w[3], 1e-6);
            Assert.True(report.Warnings.Count >= 2, "the clip and the normalization are reported: " + string.Join("; ", report.Warnings));
            Assert.Equal(0, report.Errors.Count);
            Assert.Equal(ReviewConfig.CreateDefault().WeightsFor(Segment.MidRange), cfg.WeightsFor(Segment.MidRange), "the other segments keep their defaults");
        }

        [Test]
        public void ParseConfig_WeightsThatDoNotAddUpToOne_AreNormalized()
        {
            var report = new ValidationReport();
            ReviewConfig cfg = PressContentValidator.ParseConfig(Obj(
                @"{ ""weights"": { ""LowEnd"": { ""performance"": 2, ""value"": 2, ""efficiency"": 2, ""thermal"": 2 } } }"), report);

            foreach (float weight in cfg.WeightsFor(Segment.LowEnd)) Assert.Near(0.25, weight, 1e-6);
        }

        [Test]
        public void ParseConfig_WeightsThatAddUpToZero_KeepTheDefaultsAndAreAnError()
        {
            var report = new ValidationReport();
            ReviewConfig cfg = PressContentValidator.ParseConfig(Obj(
                @"{ ""weights"": { ""Industries"": { ""performance"": 0, ""value"": -1, ""efficiency"": 0, ""thermal"": 0 } } }"), report);

            Assert.Equal(ReviewConfig.CreateDefault().WeightsFor(Segment.Industries), cfg.WeightsFor(Segment.Industries));
            Assert.True(report.Errors.Count > 0);
        }

        [Test]
        public void ParseConfig_GTauAndStrictness_KeepTheirRanges()
        {
            var report = new ValidationReport();
            ReviewConfig cfg = PressContentValidator.ParseConfig(Obj(
                @"{ ""trend"": { ""tau"": 3, ""eras"": [ { ""from"": 1971, ""g"": 5 }, { ""from"": 1990, ""g"": 0.5 } ] }, ""strictness"": [0.1, 1, 3] }"), report);

            Assert.Near(1.5, cfg.Trend.Tau, 1e-6, "tau at most 1.5");
            Assert.Equal(2, cfg.Trend.Eras.Count);
            Assert.Near(2.0, cfg.Trend.Eras[0].G, 1e-6, "g at most 2");
            Assert.Near(1.0, cfg.Trend.Eras[1].G, 1e-6, "g at least 1");
            Assert.Equal(new float[] { 0.5f, 1f, 2f }, cfg.Strictness, "strictness within 0.5 ... 2");
            Assert.True(report.Warnings.Count >= 5, string.Join("; ", report.Warnings));

            report = new ValidationReport();
            Assert.Near(0.0, PressContentValidator.ParseConfig(Obj(@"{ ""trend"": { ""tau"": -1 } }"), report).Trend.Tau, 1e-6, "tau at least 0");
        }

        [Test]
        public void ParseConfig_ErasAreSortedByTheirStart()
        {
            var report = new ValidationReport();
            ReviewConfig cfg = PressContentValidator.ParseConfig(Obj(
                @"{ ""trend"": { ""eras"": [ { ""from"": 2000, ""g"": 1.3 }, { ""from"": 1971, ""g"": 1.35 }, { ""from"": 1985, ""g"": 1.45 } ] } }"), report);

            Assert.Equal(1971, cfg.Trend.Eras[0].From);
            Assert.Equal(1985, cfg.Trend.Eras[1].From);
            Assert.Equal(2000, cfg.Trend.Eras[2].From);
            Assert.Near(1.45, ReviewMath.EraGrowth(1990, cfg), 1e-6);
        }

        [Test]
        public void ParseConfig_AMemberOfTheWrongKind_KeepsTheDefaultAndIsAnError()
        {
            var report = new ValidationReport();
            ReviewConfig cfg = PressContentValidator.ParseConfig(Obj(
                @"{ ""curve"": 5, ""thermal"": { ""safeCap"": ""hot"" }, ""cap"": { ""gap"": [1] }, ""delayDays"": [7], ""noAnalogPerformance"": ""x"", ""verdicts"": ""many"", ""strictness"": [1, ""a""] }"), report);

            ReviewConfig defaults = ReviewConfig.CreateDefault();
            Assert.Near(defaults.Curve.Parity, cfg.Curve.Parity, 0.0);
            Assert.Near(defaults.Thermal.SafeCap, cfg.Thermal.SafeCap, 0.0);
            Assert.Near(defaults.Cap.Gap, cfg.Cap.Gap, 0.0);
            Assert.Equal(defaults.DelayMinDays, cfg.DelayMinDays);
            Assert.Near(defaults.NoAnalogPerformance, cfg.NoAnalogPerformance, 0.0);
            Assert.Equal(defaults.Verdicts.Count, cfg.Verdicts.Count);
            Assert.Equal(defaults.Strictness, cfg.Strictness);
            Assert.True(report.Errors.Count >= 6, string.Join("; ", report.Errors));
        }

        [Test]
        public void ParseConfig_AnEmptyFile_GivesTheDefaultsAndAnError()
        {
            var report = new ValidationReport();
            ReviewConfig cfg = PressContentValidator.ParseConfig(new JsonObject(), report);

            Assert.NotNull(cfg);
            Assert.Equal(1, report.Errors.Count);
            Assert.Near(65.0, cfg.Curve.Parity, 0.0);

            Assert.NotNull(PressContentValidator.ParseConfig(null, null), "no report asked for, no crash");
        }

        [Test]
        public void ParseConfig_TheSegmentRuleAndTheDriftBelongToTheCore()
        {
            var report = new ValidationReport();
            ReviewConfig cfg = PressContentValidator.ParseConfig(Obj(@"{ ""segmentPriceBand"": 1.5, ""drift"": { ""rate"": 0.1 }, ""curve"": { ""parity"": 60 } }"), report);

            Assert.Near(60.0, cfg.Curve.Parity, 0.0, "the rest of the file is read");
            Assert.Equal(2, report.Warnings.Count, string.Join("; ", report.Warnings));
            Assert.True(report.Warnings[0].Contains("segmentPriceBand"));
            Assert.True(report.Warnings[1].Contains("drift"));

            foreach (MemberInfo member in typeof(ReviewConfig).GetMembers(BindingFlags.Public | BindingFlags.Instance))
            {
                string name = member.Name.ToLowerInvariant();
                Assert.False(name.Contains("priceband") || name.Contains("drift"), "ReviewConfig." + member.Name + " would duplicate a rule of the core");
            }
        }

        [Test]
        public void ParseConfig_RangesOfTheOtherNumbers_AreKept()
        {
            var report = new ValidationReport();
            ReviewConfig cfg = PressContentValidator.ParseConfig(Obj(@"{
                ""peers"": { ""count"": 50, ""periodMonths"": 0, ""candidates"": 2 },
                ""curve"": { ""parity"": 200, ""span"": 0, ""ratioAtTop"": 1 },
                ""effects"": { ""hype"": { ""strength"": 4, ""min"": 0, ""max"": 9, ""durationDays"": 0 },
                               ""reputationPerReview"": { ""min"": -500, ""max"": 500 } },
                ""archive"": { ""maxFull"": 3000, ""maxTotal"": 200 },
                ""adjusterLimits"": { ""axis"": -1, ""overall"": 900 },
                ""flawReveal"": { ""chance"": 2 },
                ""delayDays"": [30, 7] }"), report);

            Assert.Equal(10, cfg.Peers.Count);
            Assert.Equal(1, cfg.Peers.PeriodMonths);
            Assert.Equal(10, cfg.Peers.Candidates, "candidates are at least the number of peers");
            Assert.Near(100.0, cfg.Curve.Parity, 0.0);
            Assert.Near(1.0, cfg.Curve.Span, 0.0);
            Assert.Near(1.05, cfg.Curve.RatioAtTop, 1e-6, "the curve divides by ln(ratioAtTop)");
            Assert.Near(1.0, cfg.Hype.Strength, 0.0);
            Assert.Near(0.1, cfg.Hype.Min, 1e-6);
            Assert.Near(3.0, cfg.Hype.Max, 0.0);
            Assert.Equal(1, cfg.Hype.DurationDays);
            Assert.Equal(-50, cfg.ReputationPerReview.Min);
            Assert.Equal(50, cfg.ReputationPerReview.Max);
            Assert.Equal(3000, cfg.Archive.MaxTotal, "the total is at least the number of full records");
            Assert.Near(0.0, cfg.AdjusterLimits.Axis, 0.0);
            Assert.Near(100.0, cfg.AdjusterLimits.Overall, 0.0);
            Assert.Near(1.0, cfg.FlawReveal.Chance, 0.0);
            Assert.Equal(7, cfg.DelayMinDays, "delayDays [30, 7] is refused: the defaults stay");
            Assert.Equal(21, cfg.DelayMaxDays);
        }

        [Test]
        public void ParseConfig_VerdictsAndLevels_AreSortedFromTheTop_AndBadEntriesSkipped()
        {
            var report = new ValidationReport();
            ReviewConfig cfg = PressContentValidator.ParseConfig(Obj(@"{
                ""verdicts"": [ { ""min"": 0, ""key"": ""lo"" }, { ""min"": 70, ""key"": ""hi"" }, { ""min"": 30 }, 5 ],
                ""reputationLevels"": [ { ""min"": 0, ""key"": ""bad"" }, { ""min"": 50, ""key"": ""good"" } ] }"), report);

            Assert.Equal(2, cfg.Verdicts.Count);
            Assert.Equal("hi", cfg.Verdicts[0].Key);
            Assert.Equal("lo", cfg.Verdicts[1].Key);
            Assert.Equal("good", cfg.ReputationLevels[0].Key);
            Assert.Equal(2, report.Errors.Count, "the two bad verdicts: " + string.Join("; ", report.Errors));
        }

        [Test]
        public void ParseConfig_Quotes_ArePoolsPerAxisAndSide()
        {
            var report = new ValidationReport();
            ReviewConfig cfg = PressContentValidator.ParseConfig(Obj(@"{ ""quotes"": {
                ""performance"": { ""high"": [ ""a"", ""b"", ""a"" ], ""low"": [ ""c"" ] },
                ""thermal"": { ""high"": [ ""d"", 5 ] },
                ""humour"": { } } }"), report);

            Assert.Equal(new List<string> { "a", "b" }, new List<string>(cfg.QuotesFor(Axis.Performance, true)), "a repeated key is kept once");
            Assert.Equal(new List<string> { "c" }, new List<string>(cfg.QuotesFor(Axis.Performance, false)));
            Assert.Equal(new List<string> { "d" }, new List<string>(cfg.QuotesFor(Axis.Thermal, true)));
            Assert.Equal(0, cfg.QuotesFor(Axis.Value, true).Count);
            Assert.Equal(1, report.Errors.Count, "the number 5 is not a quote key");
            Assert.True(report.Warnings.Exists(w => w.Contains("humour")), "an unknown axis is a warning");
        }

        [Test]
        public void ParseConfig_UnknownTopLevelMembers_AreWarnings()
        {
            var report = new ValidationReport();
            PressContentValidator.ParseConfig(Obj(@"{ ""schema"": 1, ""curve"": { ""parity"": 65 }, ""mystery"": 1 }"), report);

            Assert.Equal(1, report.Warnings.Count, string.Join("; ", report.Warnings));
            Assert.True(report.Warnings[0].Contains("mystery"));
        }

        [Test]
        public void ReviewConfig_Lookups_AreSafe()
        {
            var cfg = ReviewConfig.CreateDefault();
            Assert.Equal(cfg.WeightsFor(Segment.MidRange), cfg.WeightsFor(Segment.Any), "Any uses Mid Range");
            Assert.Near(0.85, cfg.StrictnessFor(0), 1e-6);
            Assert.Near(1.25, cfg.StrictnessFor(5), 1e-6);
            Assert.Near(1.25, cfg.StrictnessFor(99), 1e-6);
            Assert.Near(0.85, cfg.StrictnessFor(-1), 1e-6);

            cfg.Strictness = new float[0];
            Assert.Near(1.0, cfg.StrictnessFor(2), 1e-6, "no table: neutral");
            cfg.QuotePools = null;
            Assert.Equal(0, cfg.QuotesFor(Axis.Performance, true).Count);
        }

        // ---- notes ----

        private static NoteTemplate Note(string json, out List<string> messages)
        {
            messages = new List<string>();
            return PressContentValidator.ParseNote(Obj(json), messages);
        }

        [Test]
        public void ParseNote_AGoodNote_IsRead()
        {
            List<string> messages;
            NoteTemplate note = Note(@"{ ""id"": ""perf_bar_highend"", ""condition"": { ""type"": ""leaderGrowth"", ""segment"": ""HighEnd"", ""min"": 0.25 },
                ""textKey"": ""press.note.perfBar"", ""args"": [""segment"", ""qualitativeGrowth""], ""cooldownQuarters"": 4, ""weight"": 3 }", out messages);

            Assert.NotNull(note, string.Join("; ", messages));
            Assert.Equal(0, messages.Count);
            Assert.Equal("perf_bar_highend", note.Id);
            Assert.Equal(NoteConditionType.LeaderGrowth, note.Condition.Type);
            Assert.Equal(Segment.HighEnd, note.Condition.Segment);
            Assert.Near(0.25, note.Condition.Min.Value, 1e-6);
            Assert.False(note.Condition.Max.HasValue);
            Assert.Equal("press.note.perfBar", note.TextKey);
            Assert.Equal(new List<string> { "segment", "qualitativeGrowth" }, note.Args);
            Assert.Equal(4, note.CooldownQuarters);
            Assert.Near(3.0, note.Weight, 1e-6);
        }

        [Test]
        public void ParseNote_AnUnknownKindOfCondition_IsDropped()
        {
            List<string> messages;
            NoteTemplate note = Note(@"{ ""id"": ""x"", ""condition"": { ""type"": ""moonPhase"", ""min"": 1 }, ""textKey"": ""press.note.x"" }", out messages);

            Assert.Null(note);
            Assert.True(messages.Exists(m => m.Contains("moonPhase")), string.Join("; ", messages));
        }

        [Test]
        public void ParseNote_EveryKnownKindOfCondition_Parses()
        {
            string[] good =
            {
                @"{ ""type"": ""leaderGrowth"", ""segment"": ""HighEnd"", ""min"": 0.25 }",
                @"{ ""type"": ""medianPriceChange"", ""segment"": ""LowEnd"", ""max"": -0.1 }",
                @"{ ""type"": ""pgaShift"" }",
                @"{ ""type"": ""segmentFocus"", ""segment"": ""Industries"" }",
                @"{ ""type"": ""reputationTrend"", ""min"": 8 }",
                @"{ ""type"": ""newEntrant"" }",
            };
            for (int i = 0; i < good.Length; i++)
            {
                List<string> messages;
                NoteTemplate note = Note(@"{ ""id"": ""n" + i + @""", ""condition"": " + good[i] + @", ""textKey"": ""press.note.x"" }", out messages);
                Assert.NotNull(note, good[i] + ": " + string.Join("; ", messages));
                Assert.Equal((NoteConditionType)i, note.Condition.Type);
                Assert.Equal(NoteCondition.NameOf((NoteConditionType)i), ConditionNames[i]);
            }
        }

        private static readonly string[] ConditionNames = { "leaderGrowth", "medianPriceChange", "pgaShift", "segmentFocus", "reputationTrend", "newEntrant" };

        [Test]
        public void ParseNote_BrokenNotes_AreRefused()
        {
            string[] broken =
            {
                @"{ ""condition"": { ""type"": ""pgaShift"" }, ""textKey"": ""k"" }",                                                                  // no id
                @"{ ""id"": ""x"", ""textKey"": ""k"" }",                                                                                              // no condition
                @"{ ""id"": ""x"", ""condition"": { ""type"": ""pgaShift"" } }",                                                                       // no text
                @"{ ""id"": ""x"", ""condition"": { ""min"": 1 }, ""textKey"": ""k"" }",                                                               // no type
                @"{ ""id"": ""x"", ""condition"": 7, ""textKey"": ""k"" }",                                                                            // not an object
                @"{ ""id"": ""x"", ""condition"": { ""type"": ""leaderGrowth"", ""min"": 0.2 }, ""textKey"": ""k"" }",                                  // no segment
                @"{ ""id"": ""x"", ""condition"": { ""type"": ""leaderGrowth"", ""segment"": ""Cheap"", ""min"": 0.2 }, ""textKey"": ""k"" }",           // unknown segment
                @"{ ""id"": ""x"", ""condition"": { ""type"": ""leaderGrowth"", ""segment"": ""HighEnd"" }, ""textKey"": ""k"" }",                       // no bound
                @"{ ""id"": ""x"", ""condition"": { ""type"": ""reputationTrend"" }, ""textKey"": ""k"" }",                                            // no bound
                @"{ ""id"": ""x"", ""condition"": { ""type"": ""medianPriceChange"", ""segment"": ""LowEnd"", ""min"": 1, ""max"": 0 }, ""textKey"": ""k"" }", // min above max
                @"{ ""id"": ""x"", ""condition"": { ""type"": ""pgaShift"" }, ""textKey"": ""k"", ""args"": [""nonsense""] }",                          // unknown value name
                @"{ ""id"": ""x"", ""condition"": { ""type"": ""pgaShift"" }, ""textKey"": ""k"", ""args"": [5] }",
                @"{ ""id"": ""x"", ""condition"": { ""type"": ""pgaShift"" }, ""textKey"": ""k"", ""weight"": ""heavy"" }",
            };

            for (int i = 0; i < broken.Length; i++)
            {
                List<string> messages;
                NoteTemplate note = Note(broken[i], out messages);
                Assert.Null(note, "note " + i + " must be refused:\n" + broken[i]);
                Assert.True(messages.Count > 0, "note " + i + " must say why");
            }
        }

        [Test]
        public void ParseNote_CooldownAndWeight_KeepTheirRanges()
        {
            List<string> messages;
            NoteTemplate note = Note(@"{ ""id"": ""x"", ""condition"": { ""type"": ""pgaShift"" }, ""textKey"": ""k"", ""cooldownQuarters"": -3, ""weight"": 5000 }", out messages);

            Assert.NotNull(note);
            Assert.Equal(0, note.CooldownQuarters);
            Assert.Near(100.0, note.Weight, 0.0);
            Assert.Equal(2, messages.Count);
        }

        [Test]
        public void ParseNote_DefaultsWhenOmitted()
        {
            List<string> messages;
            NoteTemplate note = Note(@"{ ""id"": ""x"", ""condition"": { ""type"": ""pgaShift"" }, ""textKey"": ""k"" }", out messages);

            Assert.NotNull(note);
            Assert.Equal(4, note.CooldownQuarters, "the default cooldown is 4 quarters");
            Assert.Near(1.0, note.Weight, 0.0);
            Assert.Equal(0, note.Args.Count);
        }

        [Test]
        public void ANoteWithAnUnknownKind_IsSkippedWhenTheFileLoads_TheOthersStay()
        {
            string text = @"{ ""schema"": 1, ""records"": [
                { ""id"": ""a"", ""condition"": { ""type"": ""pgaShift"" }, ""textKey"": ""press.note.pgaShift"" },
                { ""id"": ""b"", ""condition"": { ""type"": ""solarFlare"" }, ""textKey"": ""press.note.x"" },
                { ""id"": ""c"", ""condition"": { ""type"": ""newEntrant"" }, ""textKey"": ""press.note.newEntrant"" } ] }";

            ContentLoad<NoteTemplate> load = ContentMerger.MergeRecords<NoteTemplate>("press-reviews", "notes.json", text, null, PressContentValidator.ParseNote, null, 1);
            Assert.Equal(2, load.Records.Count);
            Assert.Equal(1, load.Report.Skipped);
        }

        // ---- the coverage of the years ----

        [Test]
        public void CoverageHoles_TheBuiltinOutlets_HaveNone()
        {
            List<Outlet> outlets = PressContentFixture.Outlets();
            Assert.Equal(0, PressContentValidator.FindCoverageHoles(outlets).Count);
            Assert.Equal(0, PressContentValidator.FindCoverageHoles(outlets, 1971, 2030).Count);
        }

        [Test]
        public void CoverageHoles_AreListedByYearAndSegment()
        {
            // One outlet that works until 1992 and has no fallback after it: every segment is uncovered from 1993 on.
            Outlet only = PressContentValidator.StubOutlet();
            only.Id = "only";
            only.YearFrom = 1971;
            only.YearTo = 1992;
            only.HasFallback = false;

            List<string> holes = PressContentValidator.FindCoverageHoles(new List<Outlet> { only });
            Assert.Equal((2030 - 1993 + 1) * 4, holes.Count);
            Assert.Equal("1993 HighEnd", holes[0]);
            Assert.Equal("1993 MidRange", holes[1]);
            Assert.Equal("1993 LowEnd", holes[2]);
            Assert.Equal("1993 Industries", holes[3]);
            Assert.Equal("1994 HighEnd", holes[4]);
            Assert.Equal("2030 Industries", holes[holes.Count - 1]);
        }

        [Test]
        public void CoverageHoles_AFallbackOutletFillsTheGaps()
        {
            Outlet only = PressContentValidator.StubOutlet();
            only.Id = "only";
            only.YearFrom = 1971;
            only.YearTo = 1992;
            only.FallbackFrom = 1971;
            only.FallbackTo = 2030;   // stands in for every uncovered year

            Assert.Equal(0, PressContentValidator.FindCoverageHoles(new List<Outlet> { only }).Count);
        }

        [Test]
        public void CoverageHoles_ASegmentThatNoOutletListsIsAHole()
        {
            Outlet embedded = PressContentValidator.StubOutlet();
            embedded.Id = "e";
            embedded.HasFallback = false;
            embedded.Segments = new List<Segment> { Segment.Industries };

            List<string> holes = PressContentValidator.FindCoverageHoles(new List<Outlet> { embedded }, 2000, 2000);
            Assert.Equal(new List<string> { "2000 HighEnd", "2000 MidRange", "2000 LowEnd" }, holes);
        }

        [Test]
        public void CoverageHoles_NoOutlets_EveryYearIsAHole()
        {
            Assert.Equal(60 * 4, PressContentValidator.FindCoverageHoles(new List<Outlet>()).Count);
            Assert.Equal(60 * 4, PressContentValidator.FindCoverageHoles(null).Count);
        }

        // ---- the whole set ----

        [Test]
        public void Validate_TheBuiltinContent_IsClean()
        {
            var configReport = new ValidationReport();
            ReviewConfig cfg = PressContentFixture.Config(configReport);
            ValidationReport report = PressContentValidator.Validate(PressContentFixture.Outlets(), cfg, PressContentFixture.LoadNotes().Records);

            Assert.Equal(0, configReport.Errors.Count, string.Join("; ", configReport.Errors));
            Assert.Equal(0, configReport.Warnings.Count, string.Join("; ", configReport.Warnings));
            Assert.True(report.IsClean, string.Join("; ", report.Errors) + string.Join("; ", report.CoverageHoles));
            Assert.Equal(0, report.Warnings.Count, string.Join("; ", report.Warnings));
        }

        [Test]
        public void Validate_FindsDuplicatesMissingPartsAndHoles()
        {
            Outlet a = PressContentValidator.StubOutlet();
            Outlet b = PressContentValidator.StubOutlet();   // the same id
            var cfg = ReviewConfig.CreateDefault();
            cfg.Verdicts.Clear();
            cfg.ReputationLevels.Clear();
            cfg.Weights[2] = new[] { 0.5f, 0.5f, 0.5f, 0.5f };

            var note = new NoteTemplate { Id = "n" };
            ValidationReport report = PressContentValidator.Validate(new List<Outlet> { a, b }, cfg, new List<NoteTemplate> { note, note });

            Assert.False(report.IsClean);
            Assert.True(report.Errors.Exists(e => e.Contains("press_stub")), "the repeated outlet id");
            Assert.True(report.Errors.Exists(e => e.Contains("verdict")));
            Assert.True(report.Errors.Exists(e => e.Contains("reputation level")));
            Assert.True(report.Errors.Exists(e => e.Contains("'n'")), "the repeated note id");
            Assert.True(report.Warnings.Exists(w => w.Contains("LowEnd")), "weights that do not add up to 1");
            Assert.True(report.Warnings.Exists(w => w.Contains("quotes.performance.high")), "the empty quote pools of the default config");
        }

        [Test]
        public void Validate_NoOutletsAndNoConfig_AreErrors()
        {
            ValidationReport report = PressContentValidator.Validate(null, null, null);
            Assert.True(report.Errors.Count >= 2);
            Assert.True(report.CoverageHoles.Count > 0);
        }

        [Test]
        public void StubOutlet_ReviewsEverySegmentInEveryYear()
        {
            Outlet stub = PressContentValidator.StubOutlet();
            Assert.Equal(0, PressContentValidator.FindCoverageHoles(new List<Outlet> { stub }).Count);
            Assert.Equal(4, stub.Segments.Count);
        }

        [Test]
        public void TheDefaultConfig_IsTheBuiltinFile()
        {
            // The defaults in the code and Content/press-reviews/review-config.json must not drift apart.
            ReviewConfig file = PressContentFixture.Config();
            ReviewConfig code = ReviewConfig.CreateDefault();

            Assert.Equal(code.DelayMinDays, file.DelayMinDays);
            Assert.Equal(code.DelayMaxDays, file.DelayMaxDays);
            Assert.Equal(code.Peers.Count, file.Peers.Count);
            Assert.Equal(code.Peers.PeriodMonths, file.Peers.PeriodMonths);
            Assert.Equal(code.Peers.Candidates, file.Peers.Candidates);
            Assert.Near(code.Trend.Tau, file.Trend.Tau, 1e-6);
            Assert.Equal(code.Trend.Eras.Count, file.Trend.Eras.Count);
            for (int i = 0; i < code.Trend.Eras.Count; i++)
            {
                Assert.Equal(code.Trend.Eras[i].From, file.Trend.Eras[i].From);
                Assert.Near(code.Trend.Eras[i].G, file.Trend.Eras[i].G, 1e-6);
            }
            Assert.Near(code.Curve.Parity, file.Curve.Parity, 1e-6);
            Assert.Near(code.Curve.Span, file.Curve.Span, 1e-6);
            Assert.Near(code.Curve.RatioAtTop, file.Curve.RatioAtTop, 1e-6);
            Assert.Equal(code.Strictness, file.Strictness);
            Assert.Near(code.Thermal.SafeCap, file.Thermal.SafeCap, 1e-6);
            Assert.Near(code.Thermal.FullMarksBelowSafe, file.Thermal.FullMarksBelowSafe, 1e-6);
            Assert.Near(code.Thermal.ZeroAboveSafe, file.Thermal.ZeroAboveSafe, 1e-6);
            for (int s = 0; s < 4; s++) Assert.Equal(code.Weights[s], file.Weights[s], "weights of segment " + s);
            Assert.Near(code.Cap.Gap, file.Cap.Gap, 1e-6);
            Assert.Near(code.Cap.MinWeight, file.Cap.MinWeight, 1e-6);
            Assert.Near(code.NoAnalogPerformance, file.NoAnalogPerformance, 1e-6);
            Assert.Equal(code.Verdicts.Count, file.Verdicts.Count);
            for (int i = 0; i < code.Verdicts.Count; i++)
            {
                Assert.Equal(code.Verdicts[i].Min, file.Verdicts[i].Min);
                Assert.Equal(code.Verdicts[i].Key, file.Verdicts[i].Key);
            }
            Assert.Near(code.Hype.Strength, file.Hype.Strength, 1e-6);
            Assert.Near(code.Hype.Min, file.Hype.Min, 1e-6);
            Assert.Near(code.Hype.Max, file.Hype.Max, 1e-6);
            Assert.Equal(code.Hype.DurationDays, file.Hype.DurationDays);
            Assert.Near(code.ReputationPerReview.Divisor, file.ReputationPerReview.Divisor, 1e-6);
            Assert.Equal(code.ReputationPerReview.Min, file.ReputationPerReview.Min);
            Assert.Equal(code.ReputationPerReview.Max, file.ReputationPerReview.Max);
            Assert.Near(code.ReputationEffect.Strength, file.ReputationEffect.Strength, 1e-6);
            Assert.Near(code.ReputationEffect.Neutral, file.ReputationEffect.Neutral, 1e-6);
            Assert.Equal(code.ReputationEffect.AffectsAI, file.ReputationEffect.AffectsAI);
            Assert.Equal(code.Awards.CpuOfYearMin, file.Awards.CpuOfYearMin);
            Assert.Equal(code.Awards.CpuOfYearRep, file.Awards.CpuOfYearRep);
            Assert.Equal(code.Awards.CompanyOfYearMinReviews, file.Awards.CompanyOfYearMinReviews);
            Assert.Equal(code.Awards.CompanyOfYearRep, file.Awards.CompanyOfYearRep);
            Assert.Equal(code.Awards.DisappointmentMax, file.Awards.DisappointmentMax);
            Assert.Equal(code.Awards.DisappointmentRep, file.Awards.DisappointmentRep);
            Assert.Equal(code.ReputationLevels.Count, file.ReputationLevels.Count);
            for (int i = 0; i < code.ReputationLevels.Count; i++)
            {
                Assert.Equal(code.ReputationLevels[i].Min, file.ReputationLevels[i].Min);
                Assert.Equal(code.ReputationLevels[i].Key, file.ReputationLevels[i].Key);
            }
            Assert.Equal(code.Digest.MaxLines, file.Digest.MaxLines);
            Assert.Equal(code.Digest.NotableHigh, file.Digest.NotableHigh);
            Assert.Equal(code.Digest.NotableLow, file.Digest.NotableLow);
            Assert.Equal(code.Archive.MaxFull, file.Archive.MaxFull);
            Assert.Equal(code.Archive.MaxTotal, file.Archive.MaxTotal);
            Assert.Near(code.AdjusterLimits.Axis, file.AdjusterLimits.Axis, 1e-6);
            Assert.Near(code.AdjusterLimits.Overall, file.AdjusterLimits.Overall, 1e-6);
            Assert.Near(code.FlawReveal.Chance, file.FlawReveal.Chance, 1e-6);
        }
    }
}
