using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core.Logic.Json;
using PTMod.Features.MarketShocks;
using static PTMod.Tests.Features.MarketShocks.ShockTestData;

namespace PTMod.Tests.Features.MarketShocks
{
    /// <summary>
    /// market-shocks, FR-7 and NFR-3 (TSK-004, TSK-005, TSK-008): the pure part of the effect types <c>ModifierRamp</c> and <c>ModifierWave</c> - the checks
    /// of the parameters against the ranges of design.md, <c>Resolve</c> with the intensity and the limits of the channel (AC-7.4), the numbers a resolved effect keeps
    /// and the checks they pass when they come back from a save, the day-by-day work (<c>Tick</c>, <c>Restore</c>: the same multiplier on the same day; damaged numbers
    /// drop the effect), the shared phase of a <c>phaseGroup</c>, and the figures of the lines about the effects.
    /// </summary>
    [Covers(typeof(RampSpec), typeof(RampCurve), typeof(WaveSpec), typeof(WaveCurve), typeof(WavePhase), typeof(WavePhaseBook),
        typeof(CurveRuntime), typeof(IContributionSink), typeof(ShockFormat))]
    public class EffectTypeTests
    {
        private const double Eps = 1e-5;

        private static List<string> RampErrors(string json, Func<string, bool> known = null)
        {
            var errors = new List<string>();
            RampSpec spec;
            bool ok = RampSpec.TryRead(Params(json), known ?? AnyChannel, errors, out spec);
            Assert.Equal(errors.Count == 0, ok, "TryRead says " + ok + " with " + errors.Count + " error(s)");
            Assert.Equal(ok, spec != null, "a spec comes out exactly when it is valid");
            return errors;
        }

        private static List<string> WaveErrors(string json, Func<string, bool> known = null)
        {
            var errors = new List<string>();
            WaveSpec spec;
            bool ok = WaveSpec.TryRead(Params(json), known ?? AnyChannel, errors, out spec);
            Assert.Equal(errors.Count == 0, ok, "TryRead says " + ok + " with " + errors.Count + " error(s)");
            Assert.Equal(ok, spec != null, "a spec comes out exactly when it is valid");
            return errors;
        }

        private static string RampJson(string peak = "1.6", string rampIn = "30", string hold = "90", string rampOut = "90")
        {
            return "{'channel':'production.memoryCost','peak':" + peak + ",'rampIn':" + rampIn + ",'hold':" + hold + ",'rampOut':" + rampOut + "}";
        }

        private static string WaveJson(string amplitude = "0.08", string period = "1825", string extra = "")
        {
            return "{'channel':'market.demand.highend','amplitude':" + amplitude + ",'period':" + period + extra + "}";
        }

        // ---- ModifierRamp: the checks of the parameters (FR-7 item 5) ----

        [Test]
        public void RampSpec_DesignExample_IsValid()
        {
            RampSpec spec = RampOf(DramSpike);

            Assert.Equal("production.memoryCost", spec.Channel);
            Assert.Equal(1.6f, spec.Peak);
            Assert.Equal(30, spec.RampIn);
            Assert.Equal(90, spec.Hold);
            Assert.Equal(90, spec.RampOut);
            Assert.Equal(210, spec.TotalDays);
            Assert.True(spec.Scalable);
            Assert.Equal("ModifierRamp", RampSpec.EffectName);
        }

        [Test]
        public void RampSpec_Scalable_DefaultsToTrue_AndCanBeSwitchedOff()
        {
            Assert.True(RampOf(RampJson()).Scalable, "the intensity applies to every effect of the pack unless the content says otherwise");
            Assert.False(RampOf("{'channel':'c','peak':1.2,'rampIn':1,'hold':0,'rampOut':1,'scalable':false}").Scalable);
        }

        [Test]
        public void RampSpec_PeakRange_IsZeroPointThreeToThree()
        {
            Assert.Equal(0, RampErrors(RampJson(peak: "0.3")).Count);
            Assert.Equal(0, RampErrors(RampJson(peak: "3")).Count);
            Assert.Equal(1, RampErrors(RampJson(peak: "0.29")).Count);
            Assert.Equal(1, RampErrors(RampJson(peak: "3.01")).Count);
            Assert.Equal(1, RampErrors(RampJson(peak: "-1")).Count);
            Assert.Equal(1, RampErrors(RampJson(peak: "'1.6'")).Count, "a number in quotes is not a number");
            Assert.Equal(1, RampErrors(RampJson(peak: "null")).Count);
        }

        [Test]
        public void RampSpec_Lengths_RampInAndRampOutAtLeastOne_HoldAtLeastZero()
        {
            Assert.Equal(0, RampErrors(RampJson(rampIn: "1", hold: "0", rampOut: "1")).Count);
            Assert.Equal(1, RampErrors(RampJson(rampIn: "0")).Count);
            Assert.Equal(1, RampErrors(RampJson(rampOut: "0")).Count);
            Assert.Equal(1, RampErrors(RampJson(hold: "-1")).Count);
            Assert.Equal(1, RampErrors(RampJson(rampIn: "30.5")).Count, "days are whole");
            Assert.Equal(1, RampErrors(RampJson(hold: "'90'")).Count);
        }

        [Test]
        public void RampSpec_TotalLength_IsOneToFifteenHundredDays()
        {
            Assert.Equal(0, RampErrors(RampJson(rampIn: "500", hold: "500", rampOut: "500")).Count, "1500 is allowed");
            List<string> tooLong = RampErrors(RampJson(rampIn: "500", hold: "500", rampOut: "501"));
            Assert.Equal(1, tooLong.Count);
            Assert.True(tooLong[0].Contains("1501"), "the message names the length: " + tooLong[0]);
            Assert.Equal(2, RampErrors(RampJson(rampIn: "2000", hold: "0", rampOut: "2000")).Count, "each of the two parts is over the limit; no sum is reported for a part that failed");
        }

        [Test]
        public void RampSpec_MissingKeys_AreNamed()
        {
            foreach (string key in new[] { "channel", "peak", "rampIn", "hold", "rampOut" })
            {
                JsonObject p = Params(RampJson());
                p.Remove(key);
                var errors = new List<string>();
                RampSpec spec;
                Assert.False(RampSpec.TryRead(p, AnyChannel, errors, out spec), key);
                Assert.Null(spec);
                Assert.Equal(1, errors.Count, key + ": " + string.Join("; ", errors));
                Assert.True(errors[0].Contains("ModifierRamp.params." + key + " is required"), errors[0]);
            }
        }

        [Test]
        public void RampSpec_UnknownChannel_IsRefused_WhenTheCoreDoesNotKnowIt()
        {
            Func<string, bool> known = Channels("production.memoryCost", "production.waferCost");

            Assert.Equal(0, RampErrors(RampJson(), known).Count);
            List<string> errors = RampErrors("{'channel':'production.nonsense','peak':1.2,'rampIn':10,'hold':0,'rampOut':10}", known);
            Assert.Equal(1, errors.Count);
            Assert.True(errors[0].Contains("production.nonsense"), errors[0]);

            var noQuestion = new List<string>();
            RampSpec spec;
            Assert.True(RampSpec.TryRead(Params("{'channel':'production.nonsense','peak':1.2,'rampIn':10,'hold':0,'rampOut':10}"), null, noQuestion, out spec),
                "without the question to the core (null) the channel is not checked");
            Assert.NotNull(spec);
        }

        [Test]
        public void RampSpec_BadChannelAndScalable_AreReported()
        {
            Assert.Equal(1, RampErrors("{'channel':'','peak':1.2,'rampIn':10,'hold':0,'rampOut':10}").Count);
            Assert.Equal(1, RampErrors("{'channel':7,'peak':1.2,'rampIn':10,'hold':0,'rampOut':10}").Count);
            Assert.Equal(1, RampErrors("{'channel':'c','peak':1.2,'rampIn':10,'hold':0,'rampOut':10,'scalable':'yes'}").Count);
        }

        [Test]
        public void RampSpec_AllProblemsAreReportedAtOnce_NotJustTheFirst()
        {
            List<string> errors = RampErrors("{'peak':9,'rampIn':0,'hold':-1,'rampOut':0}");
            Assert.Equal(5, errors.Count, string.Join("; ", errors));
            Assert.True(errors.All(e => e.StartsWith("ModifierRamp.params")), "every message names the type and the key");
        }

        [Test]
        public void RampSpec_NoParams_IsAnError_NotACrash()
        {
            var errors = new List<string>();
            RampSpec spec;
            Assert.False(RampSpec.TryRead(null, AnyChannel, errors, out spec));
            Assert.Null(spec);
            Assert.Equal(1, errors.Count);

            Assert.False(RampSpec.TryRead(null, AnyChannel, null, out spec), "the list of errors is optional");
        }

        // ---- ModifierRamp: Resolve (AC-7.4, FR-7 item 6) ----

        [Test]
        public void RampSpec_Resolve_IntensityHalfOneAndAHalfTwo_ScalesTheDeviation()
        {
            RampSpec spec = RampOf(DramSpike);

            RampCurve mild = spec.Resolve(0.5f, 0.5f, 2.0f);
            RampCurve usual = spec.Resolve(1f, 0.5f, 2.0f);
            RampCurve harsh = spec.Resolve(1.5f, 0.5f, 2.0f);
            RampCurve chaos = spec.Resolve(2f, 0.5f, 2.0f);

            Assert.Near(1.30, mild.Peak, Eps);
            Assert.Near(1.60, usual.Peak, Eps);
            Assert.Near(1.90, harsh.Peak, Eps);
            Assert.Near(2.00, chaos.Peak, Eps);
            Assert.False(mild.Clamped || usual.Clamped || harsh.Clamped);
            Assert.True(chaos.Clamped, "only chaos reaches the limit");
        }

        [Test]
        public void RampSpec_Resolve_Ac74_ChaosOnMemorySpike_PeakIsTheLimit_NotTwoPointTwo()
        {
            RampCurve chaos = RampOf(DramSpike).Resolve(2f, 0.5f, 2.0f);

            Assert.Equal(2.0f, chaos.Peak);
            Assert.True(chaos.Clamped);
            Assert.Equal("+100 %", ShockFormat.SignedPercent(chaos.Peak, false), "the letter says 'до +100 %'");
            Assert.Equal(210, chaos.Total, "the length is the content's, not the intensity's");
        }

        [Test]
        public void RampSpec_Resolve_FactoryFireStyleDrop_ChaosIsCutAtTheLowerLimit()
        {
            RampSpec spec = RampOf("{'channel':'production.output','peak':0.55,'rampIn':1,'hold':10,'rampOut':1}");

            RampCurve chaos = spec.Resolve(2f, 0.4f, 1.5f);

            Assert.Equal(0.4f, chaos.Peak);
            Assert.True(chaos.Clamped);
        }

        [Test]
        public void RampSpec_Resolve_NotScalable_IgnoresTheIntensity_ButNotTheLimits()
        {
            RampSpec spec = RampOf("{'channel':'c','peak':1.6,'rampIn':10,'hold':10,'rampOut':10,'scalable':false}");

            Assert.Near(1.6, spec.Resolve(2f, 0.5f, 2.0f).Peak, Eps);
            Assert.Near(1.6, spec.Resolve(0.5f, 0.5f, 2.0f).Peak, Eps);

            RampCurve cut = spec.Resolve(2f, 0.5f, 1.4f);
            Assert.Near(1.4, cut.Peak, Eps);
            Assert.True(cut.Clamped, "a channel limit applies to every effect");
        }

        [Test]
        public void RampSpec_Resolve_NoLimitsKnown_ScalesWithoutACut()
        {
            RampCurve chaos = RampOf(DramSpike).Resolve(2f, float.NaN, float.NaN);

            Assert.Near(2.2, chaos.Peak, Eps);
            Assert.False(chaos.Clamped);
        }

        // ---- ModifierRamp: the numbers an effect keeps ----

        [Test]
        public void RampCurve_ToArray_IsPeakAndThreeLengths_PlusAMarkWhenCut()
        {
            RampCurve usual = RampOf(DramSpike).Resolve(1f, 0.5f, 2.0f);
            float[] four = usual.ToArray();
            Assert.Equal(new[] { 1.6f, 30f, 90f, 90f }, four, "design.md: curve = [peak', rampIn, hold, rampOut]");

            RampCurve cut = RampOf(DramSpike).Resolve(2f, 0.5f, 2.0f);
            float[] five = cut.ToArray();
            Assert.Equal(new[] { 2.0f, 30f, 90f, 90f, 1f }, five);
        }

        [Test]
        public void RampCurve_TryRead_RoundTripsWhatToArrayWrote()
        {
            foreach (float severity in new[] { 0.5f, 1f, 1.5f, 2f })
            {
                RampCurve written = RampOf(DramSpike).Resolve(severity, 0.5f, 2.0f);
                RampCurve read;
                string problem;

                Assert.True(RampCurve.TryRead(written.ToArray(), out read, out problem), "intensity " + severity + ": " + problem);
                Assert.Null(problem);
                Assert.Equal(written.Peak, read.Peak);
                Assert.Equal(written.RampIn, read.RampIn);
                Assert.Equal(written.Hold, read.Hold);
                Assert.Equal(written.RampOut, read.RampOut);
                Assert.Equal(written.Clamped, read.Clamped);
            }
        }

        [Test]
        public void RampCurve_ValueAt_Ac71_ByTheSavedNumbers()
        {
            RampCurve curve = RampOf(DramSpike).Resolve(1f, 0.5f, 2.0f);

            Assert.Near(1.3, curve.ValueAt(15), Eps);
            Assert.Near(1.6, curve.ValueAt(30), Eps);
            Assert.Near(1.6, curve.ValueAt(120), Eps);
            Assert.Near(1.3, curve.ValueAt(165), Eps);
            Assert.Equal(1f, curve.ValueAt(210));
        }

        [Test]
        public void RampCurve_TryRead_DamagedNumbers_AreRefused()
        {
            // FR-7 item 4, design.md "Security": a hand-edited side file cannot push a bad multiplier or an endless ramp into a channel.
            var damaged = new Dictionary<string, float[]>
            {
                { "peak 10", new[] { 10f, 30f, 90f, 90f } },
                { "negative peak", new[] { -0.5f, 30f, 90f, 90f } },
                { "NaN peak", new[] { float.NaN, 30f, 90f, 90f } },
                { "infinite length", new[] { 1.6f, float.PositiveInfinity, 90f, 90f } },
                { "rampIn 0", new[] { 1.6f, 0f, 90f, 90f } },
                { "rampOut 0", new[] { 1.6f, 30f, 90f, 0f } },
                { "negative hold", new[] { 1.6f, 30f, -1f, 90f } },
                { "fractional days", new[] { 1.6f, 30.5f, 90f, 90f } },
                { "over 1500 days", new[] { 1.6f, 800f, 800f, 800f } },
                { "one number", new[] { 1.6f } },
                { "three numbers", new[] { 1.6f, 30f, 90f } },
                { "six numbers", new[] { 1.6f, 30f, 90f, 90f, 1f, 0f } },
                { "mark 2", new[] { 1.6f, 30f, 90f, 90f, 2f } },
                { "empty", new float[0] },
            };

            foreach (KeyValuePair<string, float[]> pair in damaged)
            {
                RampCurve read;
                string problem;
                Assert.False(RampCurve.TryRead(pair.Value, out read, out problem), pair.Key);
                Assert.NotNull(problem, pair.Key + " has a reason");
            }

            RampCurve none;
            string reason;
            Assert.False(RampCurve.TryRead(null, out none, out reason), "no numbers at all");
            Assert.NotNull(reason);
        }

        [Test]
        public void RampCurve_TryRead_TheEdgesOfTheRanges_AreAccepted()
        {
            RampCurve read;
            string problem;
            Assert.True(RampCurve.TryRead(new[] { 0f, 1f, 0f, 1f }, out read, out problem), problem);
            Assert.True(RampCurve.TryRead(new[] { RampCurve.MaxSavedPeak, 500f, 500f, 500f }, out read, out problem), problem);
            Assert.True(RampCurve.TryRead(new[] { 2.0f, 30f, 90f, 90f, 1f }, out read, out problem), problem);
            Assert.True(read.Clamped);
            Assert.True(RampCurve.TryRead(new[] { 2.0f, 30f, 90f, 90f, 0f }, out read, out problem), problem);
            Assert.False(read.Clamped);
        }

        // ---- ModifierWave: the checks of the parameters ----

        [Test]
        public void WaveSpec_DesignExample_IsValid()
        {
            WaveSpec spec = WaveOf("{'channel':'market.demand.highend','amplitude':0.08,'period':1825,'phase':'random','phaseGroup':'silicon','scope':'Global','scalable':true}");

            Assert.Equal("market.demand.highend", spec.Channel);
            Assert.Equal(0.08f, spec.Amplitude);
            Assert.Equal(1825, spec.Period);
            Assert.Equal(1, spec.Cycles);
            Assert.Equal(WavePhase.Random, spec.Phase);
            Assert.Equal("silicon", spec.PhaseGroup);
            Assert.True(spec.Scalable);
            Assert.Equal(1825, spec.TotalDays);
            Assert.Equal("ModifierWave", WaveSpec.EffectName);
        }

        [Test]
        public void WaveSpec_Defaults_OneCycle_PhaseZero_NoGroup_Scalable()
        {
            WaveSpec spec = WaveOf(WaveJson());

            Assert.Equal(1, spec.Cycles);
            Assert.Equal(WavePhase.Rising, spec.Phase);
            Assert.Null(spec.PhaseGroup);
            Assert.True(spec.Scalable);
        }

        [Test]
        public void WaveSpec_Duration_IsPeriodTimesCycles()
        {
            Assert.Equal(1460, WaveOf(WaveJson("0.15", "1460")).TotalDays);
            Assert.Equal(1460 * 3, WaveOf(WaveJson("0.15", "1460", ",'cycles':3")).TotalDays);
            Assert.Equal(2, WaveOf(WaveJson("0.15", "1460", ",'cycles':2")).Cycles);
        }

        [Test]
        public void WaveSpec_AmplitudeRange_IsZeroToHalf()
        {
            Assert.Equal(0, WaveErrors(WaveJson("0")).Count);
            Assert.Equal(0, WaveErrors(WaveJson("0.5")).Count);
            Assert.Equal(1, WaveErrors(WaveJson("0.51")).Count);
            Assert.Equal(1, WaveErrors(WaveJson("-0.01")).Count);
            Assert.Equal(1, WaveErrors(WaveJson("'0.1'")).Count);
        }

        [Test]
        public void WaveSpec_PeriodRange_IsThirtyTo3650Days()
        {
            Assert.Equal(0, WaveErrors(WaveJson(period: "30")).Count);
            Assert.Equal(0, WaveErrors(WaveJson(period: "3650")).Count);
            Assert.Equal(1, WaveErrors(WaveJson(period: "29")).Count);
            Assert.Equal(1, WaveErrors(WaveJson(period: "3651")).Count);
            Assert.Equal(1, WaveErrors(WaveJson(period: "0")).Count);
            Assert.Equal(1, WaveErrors(WaveJson(period: "1825.5")).Count);
        }

        [Test]
        public void WaveSpec_CyclesRange_IsOneToThree()
        {
            Assert.Equal(0, WaveErrors(WaveJson(extra: ",'cycles':1")).Count);
            Assert.Equal(0, WaveErrors(WaveJson(extra: ",'cycles':3")).Count);
            Assert.Equal(1, WaveErrors(WaveJson(extra: ",'cycles':0")).Count);
            Assert.Equal(1, WaveErrors(WaveJson(extra: ",'cycles':4")).Count);
            Assert.Equal(1, WaveErrors(WaveJson(extra: ",'cycles':1.5")).Count);
        }

        [Test]
        public void WaveSpec_Phase_ZeroOr180OrRandom()
        {
            Assert.Equal(WavePhase.Rising, WaveOf(WaveJson(extra: ",'phase':0")).Phase);
            Assert.Equal(WavePhase.Falling, WaveOf(WaveJson(extra: ",'phase':180")).Phase);
            Assert.Equal(WavePhase.Random, WaveOf(WaveJson(extra: ",'phase':'random'")).Phase);
            Assert.Equal(WavePhase.Random, WaveOf(WaveJson(extra: ",'phase':'Random'")).Phase, "the word is not case sensitive");
            Assert.Equal(WavePhase.Falling, WaveOf(WaveJson(extra: ",'phase':'180'")).Phase);

            Assert.Equal(1, WaveErrors(WaveJson(extra: ",'phase':90")).Count);
            Assert.Equal(1, WaveErrors(WaveJson(extra: ",'phase':-180")).Count);
            Assert.Equal(1, WaveErrors(WaveJson(extra: ",'phase':'up'")).Count);
            Assert.Equal(1, WaveErrors(WaveJson(extra: ",'phase':true")).Count);
            Assert.Equal(1, WaveErrors(WaveJson(extra: ",'phase':null")).Count);
        }

        [Test]
        public void WaveSpec_PhaseGroup_IsOptionalNonEmptyText()
        {
            Assert.Equal("silicon", WaveOf(WaveJson(extra: ",'phaseGroup':'silicon'")).PhaseGroup);
            Assert.Equal(1, WaveErrors(WaveJson(extra: ",'phaseGroup':''")).Count);
            Assert.Equal(1, WaveErrors(WaveJson(extra: ",'phaseGroup':3")).Count);
        }

        [Test]
        public void WaveSpec_MissingKeysAndUnknownChannel_AreNamed()
        {
            foreach (string key in new[] { "channel", "amplitude", "period" })
            {
                JsonObject p = Params(WaveJson());
                p.Remove(key);
                var errors = new List<string>();
                WaveSpec spec;
                Assert.False(WaveSpec.TryRead(p, AnyChannel, errors, out spec), key);
                Assert.Equal(1, errors.Count, key);
                Assert.True(errors[0].Contains("ModifierWave.params." + key + " is required"), errors[0]);
            }

            List<string> unknown = WaveErrors(WaveJson(), Channels("production.memoryCost"));
            Assert.Equal(1, unknown.Count);
            Assert.True(unknown[0].Contains("market.demand.highend"), unknown[0]);

            var none = new List<string>();
            WaveSpec absent;
            Assert.False(WaveSpec.TryRead(null, AnyChannel, none, out absent));
            Assert.Equal(1, none.Count);
        }

        // ---- ModifierWave: Resolve and the numbers an effect keeps ----

        [Test]
        public void WaveSpec_Resolve_ScalesTheAmplitude_AndHoldsItToTheRoomOfTheChannel()
        {
            WaveSpec spec = WaveOf("{'channel':'market.demand.highend','amplitude':0.08,'period':1825}");

            Assert.Near(0.04, spec.Resolve(0.5f, 0.6f, 1.6f, 0f).Amplitude, Eps);
            Assert.Near(0.08, spec.Resolve(1f, 0.6f, 1.6f, 0f).Amplitude, Eps);
            Assert.Near(0.12, spec.Resolve(1.5f, 0.6f, 1.6f, 0f).Amplitude, Eps);
            WaveCurve chaos = spec.Resolve(2f, 0.6f, 1.6f, 0f);
            Assert.Near(0.16, chaos.Amplitude, Eps);
            Assert.False(chaos.Clamped, "0.16 fits between 0.6 and 1.6");

            WaveSpec wide = WaveOf("{'channel':'market.demand.highend','amplitude':0.5,'period':1825}");
            WaveCurve cut = wide.Resolve(2f, 0.6f, 1.6f, 0f);
            Assert.Near(0.4, cut.Amplitude, Eps);
            Assert.True(cut.Clamped, "the wave would go down to 0 and below the limit of 0.6");
        }

        [Test]
        public void WaveSpec_Resolve_NotScalable_KeepsTheContentAmplitude()
        {
            WaveSpec spec = WaveOf("{'channel':'c','amplitude':0.1,'period':400,'scalable':false}");

            Assert.Near(0.1, spec.Resolve(2f, 0.6f, 1.6f, 0f).Amplitude, Eps);
        }

        [Test]
        public void WaveCurve_ToArray_RoundTrips_AndKeepsThePhase()
        {
            WaveSpec spec = WaveOf("{'channel':'c','amplitude':0.15,'period':1460}");

            WaveCurve rising = spec.Resolve(1f, 0.5f, 2f, 0f);
            WaveCurve falling = spec.Resolve(1f, 0.5f, 2f, ShockCurves.HalfTurn);
            Assert.Equal(3, rising.ToArray().Length, "design.md: curve = [A', period, phaseRad]");

            WaveCurve read;
            string problem;
            Assert.True(WaveCurve.TryRead(falling.ToArray(), out read, out problem), problem);
            Assert.Equal(falling.Amplitude, read.Amplitude);
            Assert.Equal(1460, read.Period);
            Assert.Equal(ShockCurves.HalfTurn, read.PhaseRad);
            Assert.False(read.RisesFirst);

            Assert.True(rising.RisesFirst);
            Assert.False(falling.RisesFirst);
        }

        [Test]
        public void WaveCurve_ValueAt_Ac72_ByTheSavedNumbers()
        {
            WaveCurve curve = WaveOf("{'channel':'c','amplitude':0.15,'period':1460}").Resolve(1f, 0.5f, 2f, 0f);

            Assert.Near(1.15, curve.ValueAt(365), Eps);
            Assert.Near(1.00, curve.ValueAt(730), Eps);
            Assert.Near(0.85, curve.ValueAt(1095), Eps);
        }

        [Test]
        public void WaveCurve_TryRead_DamagedNumbers_AreRefused()
        {
            var damaged = new Dictionary<string, float[]>
            {
                { "period 0", new[] { 0.15f, 0f, 0f } },
                { "period 29", new[] { 0.15f, 29f, 0f } },
                { "period 3651", new[] { 0.15f, 3651f, 0f } },
                { "fractional period", new[] { 0.15f, 1460.5f, 0f } },
                { "amplitude 5", new[] { 5f, 1460f, 0f } },
                { "negative amplitude", new[] { -0.1f, 1460f, 0f } },
                { "NaN amplitude", new[] { float.NaN, 1460f, 0f } },
                { "phase 10", new[] { 0.15f, 1460f, 10f } },
                { "negative phase", new[] { 0.15f, 1460f, -1f } },
                { "NaN phase", new[] { 0.15f, 1460f, float.NaN } },
                { "two numbers", new[] { 0.15f, 1460f } },
                { "five numbers", new[] { 0.15f, 1460f, 0f, 1f, 0f } },
                { "mark 3", new[] { 0.15f, 1460f, 0f, 3f } },
            };

            foreach (KeyValuePair<string, float[]> pair in damaged)
            {
                WaveCurve read;
                string problem;
                Assert.False(WaveCurve.TryRead(pair.Value, out read, out problem), pair.Key);
                Assert.NotNull(problem, pair.Key);
            }

            WaveCurve none;
            string reason;
            Assert.False(WaveCurve.TryRead(null, out none, out reason));
        }

        // ---- Tick and Restore: the same multiplier on the same day (FR-7 item 4) ----

        [Test]
        public void PushRamp_RestoreInTheMiddleOfTheCurve_GivesTheValueOfAnUninterruptedRun()
        {
            float[] curve = RampOf(DramSpike).Resolve(1f, 0.5f, 2f).ToArray();

            // An effect that has been ticking every day since day 0.
            var running = new RecordingSink();
            string problem;
            for (int age = 0; age <= 40; age++) Assert.True(CurveRuntime.PushRamp(curve, age, running, out problem), problem);

            // The same effect restored after a load on day 40.
            var restored = new RecordingSink();
            Assert.True(CurveRuntime.PushRamp(curve, 40, restored, out problem), problem);

            Assert.Equal(running.Last, restored.Last, "the same multiplier on the same day");
            Assert.Near(1.6, restored.Last, Eps);
            Assert.Equal(41, running.Sets);
            Assert.Equal(1, restored.Sets);
        }

        [Test]
        public void PushRamp_EveryDayOfTheCurve_RestoreAndTickAgree_AndFollowTheShape()
        {
            RampCurve ramp = RampOf(DramSpike).Resolve(1.5f, 0.5f, 2f);
            float[] curve = ramp.ToArray();

            for (int age = -3; age <= 215; age++)
            {
                var tick = new RecordingSink();
                var restore = new RecordingSink();
                string problem;
                Assert.True(CurveRuntime.PushRamp(curve, age, tick, out problem), problem);
                Assert.True(CurveRuntime.PushRamp(curve, age, restore, out problem), problem);

                Assert.Equal(tick.Last, restore.Last, "day " + age);
                Assert.Equal(ShockCurves.Ramp(age, ramp.Peak, 30, 90, 90, 1f), tick.Last, "day " + age);
            }
        }

        [Test]
        public void PushRamp_DamagedNumbers_DropTheEffect_NothingIsSet_TheContributionIsTakenOut()
        {
            // AC: Restore with damaged numbers (peak 10) discards the effect.
            var sink = new RecordingSink();
            string problem;

            Assert.False(CurveRuntime.PushRamp(new[] { 10f, 30f, 90f, 90f }, 40, sink, out problem));

            Assert.NotNull(problem);
            Assert.Equal(0, sink.Sets, "a multiplier of 10 never reaches the channel");
            Assert.Equal(1, sink.Clears, "whatever the effect put there before is taken out");
            Assert.True(float.IsNaN(sink.Last));
        }

        [Test]
        public void PushRamp_NullCurve_OrNullSink_DoNotCrash()
        {
            var sink = new RecordingSink();
            string problem;
            Assert.False(CurveRuntime.PushRamp(null, 5, sink, out problem));
            Assert.Equal(1, sink.Clears);

            Assert.True(CurveRuntime.PushRamp(new[] { 1.6f, 30f, 90f, 90f }, 40, null, out problem), "no sink: nothing to write, the curve is fine");
            Assert.False(CurveRuntime.PushRamp(null, 5, null, out problem));
        }

        [Test]
        public void PushWave_RestoreInTheMiddleOfTheCurve_GivesTheValueOfAnUninterruptedRun()
        {
            float[] curve = WaveOf("{'channel':'c','amplitude':0.15,'period':1460}").Resolve(1f, 0.5f, 2f, 0f).ToArray();

            var running = new RecordingSink();
            string problem;
            for (int age = 0; age <= 365; age++) Assert.True(CurveRuntime.PushWave(curve, 1460, age, running, out problem), problem);

            var restored = new RecordingSink();
            Assert.True(CurveRuntime.PushWave(curve, 1460, 365, restored, out problem), problem);

            Assert.Equal(running.Last, restored.Last);
            Assert.Near(1.15, restored.Last, Eps);
        }

        [Test]
        public void PushWave_DamagedNumbers_DropTheEffect()
        {
            // period 0 would divide by zero in the sine.
            var sink = new RecordingSink();
            string problem;

            Assert.False(CurveRuntime.PushWave(new[] { 0.15f, 0f, 0f }, 1460, 100, sink, out problem));

            Assert.NotNull(problem);
            Assert.Equal(0, sink.Sets);
            Assert.Equal(1, sink.Clears);
        }

        [Test]
        public void PushWave_AfterTheLastDay_PutsOne_NotAStrayHalfWave()
        {
            float[] curve = WaveOf("{'channel':'c','amplitude':0.15,'period':1460}").Resolve(1f, 0.5f, 2f, 0f).ToArray();
            var sink = new RecordingSink();
            string problem;

            Assert.True(CurveRuntime.PushWave(curve, 1460, 1460, sink, out problem), problem);
            Assert.Equal(1f, sink.Last, "the engine closes the effect on its end day; a call on that day must not set a swing");

            Assert.True(CurveRuntime.PushWave(curve, 1460, 1459, sink, out problem), problem);
            Assert.Near(1.0, sink.Last, 1e-3, "the last day is the end of the whole period, a value back at 1");

            Assert.True(CurveRuntime.PushWave(curve, 0, 365, sink, out problem), problem);
            Assert.Near(1.15, sink.Last, Eps, "an unknown duration (0) does not cut the wave short");
        }

        [Test]
        public void IntensityHalfAndOneAndAHalfAndTwo_ScaleTheDeviationOfARunningRamp_ChaosIsCut()
        {
            // The deviation at the top of the memory spike: 0.30, 0.60, 0.90, and the limit of the channel for chaos (1.0, not 1.2).
            float[] expected = { 0.30f, 0.60f, 0.90f, 1.00f };
            float[] severities = { 0.5f, 1f, 1.5f, 2f };
            RampSpec spec = RampOf(DramSpike);

            for (int i = 0; i < severities.Length; i++)
            {
                var sink = new RecordingSink();
                string problem;
                Assert.True(CurveRuntime.PushRamp(spec.Resolve(severities[i], 0.5f, 2f).ToArray(), 60, sink, out problem), problem);
                Assert.Near(1.0 + expected[i], sink.Last, Eps, "intensity " + severities[i]);
            }
        }

        [Test]
        public void AgeAtApply_IsZeroUntilTheEngineGaveTheEffectAnEndDay()
        {
            Assert.Equal(0, CurveRuntime.AgeAtApply(0, 0, 6440), "no end day yet: the effect starts today");
            Assert.Equal(0, CurveRuntime.AgeAtApply(6400, 0, 6440));
            Assert.Equal(40, CurveRuntime.AgeAtApply(6400, 6610, 6440), "the engine has set both days: the age is today - start");
            Assert.Equal(0, CurveRuntime.AgeAtApply(6500, 6710, 6440), "a start in the future never gives a negative age");
        }

        [Test]
        public void ValueAt_ForTheListOfActiveEffects_UsesTheSameCurve_AndFallsBackOnDamagedNumbers()
        {
            float[] ramp = RampOf(DramSpike).Resolve(1f, 0.5f, 2f).ToArray();
            Assert.Near(1.3, CurveRuntime.RampValue(ramp, 15, 1.6f), Eps);
            Assert.Equal(1.6f, CurveRuntime.RampValue(new[] { 10f, 30f, 90f, 90f }, 15, 1.6f), "damaged: the saved value is shown");

            float[] wave = WaveOf("{'channel':'c','amplitude':0.15,'period':1460}").Resolve(1f, 0.5f, 2f, 0f).ToArray();
            Assert.Near(1.15, CurveRuntime.WaveValue(wave, 1460, 365, 1f), Eps);
            Assert.Equal(1f, CurveRuntime.WaveValue(wave, 1460, 1460, 1f), "over: 1");
            Assert.Equal(0.9f, CurveRuntime.WaveValue(new[] { 0.15f, 0f, 0f }, 1460, 100, 0.9f), "damaged");
        }

        // ---- the phase of the waves of one firing (FR-7 item 2) ----

        private static WaveSpec Phased(string phase, string group)
        {
            string extra = ",'phase':" + phase + (group == null ? string.Empty : ",'phaseGroup':'" + group + "'");
            return WaveOf(WaveJson(extra: extra));
        }

        [Test]
        public void WavePhaseBook_FixedPhases_IgnoreTheDraw()
        {
            var book = new WavePhaseBook();

            Assert.Equal(0f, book.PhaseOf(Phased("0", null), 0.99f, 1));
            Assert.Equal(ShockCurves.HalfTurn, book.PhaseOf(Phased("180", null), 0.01f, 1));
        }

        [Test]
        public void WavePhaseBook_Random_IsRolledFromTheDraw_HalfAndAboveFalls()
        {
            var book = new WavePhaseBook();

            Assert.Equal(0f, book.PhaseOf(Phased("'random'", null), 0.0f, 1));
            Assert.Equal(0f, book.PhaseOf(Phased("'random'", null), 0.4999f, 1));
            Assert.Equal(ShockCurves.HalfTurn, book.PhaseOf(Phased("'random'", null), 0.5f, 1));
            Assert.Equal(ShockCurves.HalfTurn, book.PhaseOf(Phased("'random'", null), 0.99f, 1));
        }

        [Test]
        public void WavePhaseBook_SamePhaseGroupInOneFiring_SharesTheFirstPhase()
        {
            // ms.silicon_cycle: four demand waves with phaseGroup "silicon" must rise and fall together, whatever each of them drew.
            var book = new WavePhaseBook();
            WaveSpec silicon = Phased("'random'", "silicon");

            float first = book.PhaseOf(silicon, 0.9f, 77);
            Assert.Equal(ShockCurves.HalfTurn, first);
            Assert.Equal(first, book.PhaseOf(silicon, 0.1f, 77));
            Assert.Equal(first, book.PhaseOf(silicon, 0.3f, 77));
            Assert.Equal(first, book.PhaseOf(silicon, 0.0f, 77));
            Assert.Equal(1, book.GroupCount);
        }

        [Test]
        public void WavePhaseBook_DifferentGroups_AndNoGroup_AreIndependent()
        {
            var book = new WavePhaseBook();

            Assert.Equal(ShockCurves.HalfTurn, book.PhaseOf(Phased("'random'", "a"), 0.9f, 5));
            Assert.Equal(0f, book.PhaseOf(Phased("'random'", "b"), 0.1f, 5), "another group has its own phase");
            Assert.Equal(0f, book.PhaseOf(Phased("'random'", null), 0.2f, 5), "a wave without a group rolls for itself");
            Assert.Equal(ShockCurves.HalfTurn, book.PhaseOf(Phased("'random'", null), 0.8f, 5));
            Assert.Equal(2, book.GroupCount, "only named groups are remembered");
        }

        [Test]
        public void WavePhaseBook_AnotherFiring_StartsAFreshBook()
        {
            var book = new WavePhaseBook();
            WaveSpec silicon = Phased("'random'", "silicon");

            Assert.Equal(ShockCurves.HalfTurn, book.PhaseOf(silicon, 0.9f, 1));
            Assert.Equal(0f, book.PhaseOf(silicon, 0.1f, 2), "another instance (or day): the group is not carried over");
            Assert.Equal(0f, book.PhaseOf(silicon, 0.9f, 2), "and now it is that firing's own");
            Assert.Equal(ShockCurves.HalfTurn, book.PhaseOf(silicon, 0.9f, 3), "and a third firing starts afresh again");
        }

        [Test]
        public void WavePhaseBook_TheFirstWaveOfAGroupDecides_EvenOverAFixedPhase()
        {
            var book = new WavePhaseBook();

            Assert.Equal(0f, book.PhaseOf(Phased("0", "g"), 0.9f, 9));
            Assert.Equal(0f, book.PhaseOf(Phased("180", "g"), 0.9f, 9), "one phase for the group (FR-7 item 2)");
        }

        [Test]
        public void WavePhaseBook_ClearAndNullSpec_AreSafe()
        {
            var book = new WavePhaseBook();
            Assert.Equal(0f, book.PhaseOf(null, 0.9f, 1));

            Assert.Equal(ShockCurves.HalfTurn, book.PhaseOf(Phased("'random'", "g"), 0.9f, 1));
            book.Clear();
            Assert.Equal(0, book.GroupCount);
            Assert.Equal(0f, book.PhaseOf(Phased("'random'", "g"), 0.1f, 1));
        }

        [Test]
        public void WavePhase_ThePhaseTheBookGives_BecomesTheSavedPhaseOfTheCurve()
        {
            var book = new WavePhaseBook();
            WaveSpec spec = Phased("'random'", "silicon");

            float phase = book.PhaseOf(spec, 0.9f, 3);
            WaveCurve curve = spec.Resolve(1f, 0.6f, 1.6f, phase);

            Assert.False(curve.RisesFirst);
            Assert.Near(1.0 - 0.08, curve.ValueAt(456), 1e-3, "a quarter period in: down first");
        }

        // ---- the figures of the lines about the effects (ms.fx.*) ----

        [Test]
        public void SignedPercent_ShowsTheDeviationFromOne()
        {
            Assert.Equal("+60 %", ShockFormat.SignedPercent(1.6f, false));
            Assert.Equal("+30 %", ShockFormat.SignedPercent(1.3f, false));
            Assert.Equal("-20 %", ShockFormat.SignedPercent(0.8f, false));
            Assert.Equal("-60 %", ShockFormat.SignedPercent(0.4f, true));
            Assert.Equal("0 %", ShockFormat.SignedPercent(1f, false));
            Assert.Equal("+12.5 %", ShockFormat.SignedPercent(1.125f, false));
            Assert.Equal("+12,5 %", ShockFormat.SignedPercent(1.125f, true), "the Russian text has a decimal comma");
            Assert.Equal("0 %", ShockFormat.SignedPercent(float.NaN, false));
            Assert.Equal("0 %", ShockFormat.SignedPercent(1.0001f, false), "a deviation that rounds to nothing has no sign");
        }

        [Test]
        public void Percent_ShowsAShare()
        {
            Assert.Equal("15 %", ShockFormat.Percent(0.15f, false));
            Assert.Equal("8 %", ShockFormat.Percent(0.08f, true));
            Assert.Equal("7.5 %", ShockFormat.Percent(0.075f, false));
            Assert.Equal("7,5 %", ShockFormat.Percent(0.075f, true));
            Assert.Equal("0 %", ShockFormat.Percent(0f, false));
            Assert.Equal("0 %", ShockFormat.Percent(-1f, false));
            Assert.Equal("0 %", ShockFormat.Percent(float.PositiveInfinity, false));
        }

        [Test]
        public void Years_ConvertsDaysAtThreeSixtyFive()
        {
            Assert.Equal("4", ShockFormat.Years(1460, false));
            Assert.Equal("5", ShockFormat.Years(1825, false));
            Assert.Equal("2", ShockFormat.Years(730, true));
            Assert.Equal("1.1", ShockFormat.Years(400, false));
            Assert.Equal("1,1", ShockFormat.Years(400, true));
            Assert.Equal("0.1", ShockFormat.Years(30, false));
        }

        [Test]
        public void Count_GroupsDigitsByThree_SpaceInRussian_CommaInEnglish()
        {
            Assert.Equal("1 300", ShockFormat.Count(1300, true), "AC-7.3: 'Списано 1 300 шт.'");
            Assert.Equal("1,300", ShockFormat.Count(1300, false));
            Assert.Equal("999", ShockFormat.Count(999, true));
            Assert.Equal("1 000", ShockFormat.Count(1000, true));
            Assert.Equal("1 234 567", ShockFormat.Count(1234567, true));
            Assert.Equal("0", ShockFormat.Count(0, true));
            Assert.Equal("-1 300", ShockFormat.Count(-1300, true));
            Assert.Equal("-2,147,483,648", ShockFormat.Count(int.MinValue, false));
        }

        [Test]
        public void Days_CountFromNineteenSeventyOne()
        {
            Assert.Equal(new DateTime(1971, 1, 1), ShockFormat.Epoch);
            Assert.Equal(new DateTime(1971, 1, 1), ShockFormat.DateOfDay(0));
            Assert.Equal(new DateTime(1971, 1, 2), ShockFormat.DateOfDay(1));
            Assert.Equal(new DateTime(1972, 1, 1), ShockFormat.DateOfDay(365));

            Assert.Equal(0, ShockFormat.DayOf(new DateTime(1971, 1, 1)));
            Assert.Equal(366, ShockFormat.DayOf(new DateTime(1972, 1, 2)));
            Assert.Equal(0, ShockFormat.DayOf(new DateTime(1960, 5, 5)), "before the epoch: 0");
            Assert.Equal(1000, ShockFormat.DayOf(ShockFormat.DateOfDay(1000).AddHours(15)), "the time of day does not matter");

            Assert.Equal(new DateTime(1971, 1, 1), ShockFormat.DateOfDay(-5), "a negative day is the epoch");
            Assert.Equal(new DateTime(1971, 1, 1), ShockFormat.DateOfDay(int.MaxValue), "a day past the calendar is the epoch, not an exception");
        }
    }
}
