using System;
using System.Collections.Generic;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The characteristics of a GPU design (TSK-006, FR-3, F2-F9): the four calibration examples of design.md ("Калибровочные примеры", tolerance 0.5 %), AC-3.1 and
    /// AC-3.2, the edge cases (at least one block, the bandwidth ratio floor, the temperature term above 95 degrees, no clock) and the validity of a design in
    /// <see cref="GpuDesignMath.Evaluate"/>. The records are the shipped content, so a balance edit of a calibrated number fails here.
    /// </summary>
    [Covers(typeof(GpuDesignMath), typeof(GpuDesignInput), typeof(GpuDesignContext), typeof(GpuStats), typeof(NodeSpec), typeof(GpuCpuRef))]
    public class GpuDesignMathTests
    {
        // ---- helpers ----

        private static GpuContentDb Shipped()
        {
            var problems = new List<string>();
            GpuContentDb db = GpuFiles.Load(GpuFiles.Shipped(), problems);
            Assert.Equal(0, problems.Count, "the shipped content has problems:\n  " + string.Join("\n  ", problems.ToArray()));
            return db;
        }

        /// <summary>One calibration row of design.md: the parts, the node of the year, the sliders and the CPU the clock comes from.</summary>
        private sealed class Row
        {
            public string Arch, Mem, Cooler, NodeName;
            public float Density, Die, Clock, VramMB, CpuKHz;
            public int Bus;
            // expected
            public double T, C, B, Ratio, P, W, Temp, FrPercent;
            public int S;
        }

        private static readonly Row Y1985 = new Row
        {
            Arch = "arch.2d", Mem = "vram.dram", Cooler = "cool.passive", NodeName = "1.5µm", Density = 1000f, Die = 60f, Clock = 8f, VramMB = 0.25f, CpuKHz = 16000f, Bus = 16,
            T = 60000, S = 1, C = 8, B = 8, Ratio = 0.5, P = 4.92, W = 2.21, Temp = 39.4, FrPercent = 6.57,   // TSK-045: arch.2d has 450000 transistors a block (design: 15000: S = 4, C = 32, ratio 0.125, P = 7.47)
        };

        private static readonly Row Y1997 = new Row
        {
            Arch = "arch.3d", Mem = "vram.sgram", Cooler = "cool.passive", NodeName = "350nm", Density = 16000f, Die = 80f, Clock = 100f, VramMB = 4f, CpuKHz = 250000f, Bus = 128,
            T = 1.28e6, S = 3, C = 300, B = 1600, Ratio = 0.667, P = 225.9, W = 6.19, Temp = 47.4, FrPercent = 8.49,
        };

        private static readonly Row Y2005 = new Row
        {
            Arch = "arch.shader", Mem = "vram.gddr3", Cooler = "cool.fan", NodeName = "90nm", Density = 3e5f, Die = 300f, Clock = 510f, VramMB = 256f, CpuKHz = 3400000f, Bus = 256,
            T = 9.0e7, S = 30, C = 122400, B = 51200, Ratio = 1.67, P = 122400, W = 78.95, Temp = 74.5, FrPercent = 11.42,
        };

        private static readonly Row Y2022 = new Row
        {
            Arch = "arch.tensor", Mem = "vram.gddr6", Cooler = "cool.vapor", NodeName = "5nm", Density = 5.25e7f, Die = 600f, Clock = 2250f, VramMB = 24576f, CpuKHz = 5000000f, Bus = 384,
            T = 3.15e10, S = 21000, C = 1.89e8, B = 672000, Ratio = 0.889, P = 1.74e8, W = 254.3, Temp = 63.0, FrPercent = 12.3,
        };

        private static GpuDesignInput InputOf(Row r)
        {
            return new GpuDesignInput
            {
                ArchId = r.Arch, MemId = r.Mem, CoolerId = r.Cooler, NodeName = r.NodeName, Name = "Test", DieMm2 = r.Die, ClockMHz = r.Clock, VramMB = r.VramMB, BusWidthBits = r.Bus,
            };
        }

        private static GpuDesignContext ContextOf(GpuContentDb db, Row r, int year)
        {
            return new GpuDesignContext
            {
                Arch = db.Arch(r.Arch),
                Mem = db.Mem(r.Mem),
                Cooler = db.Cooler(r.Cooler),
                Node = new NodeSpec { Name = r.NodeName, Density = r.Density, ProjectCost = 5e7f, MinYield = 0.5f, MaxYield = 0.95f },
                Transistors = GpuDesignMath.TransistorsPort(r.Density, 1f, r.Die),
                CompanyMaxFreqKHz = r.CpuKHz,
                WaferDiameter = 300f,
                Yield = 0.9f,
                DieUnitCost = 100f,
                DevModifier = 1f,
                CoDevFactor = 1f,
                UnitCostMul = 1f,
                VramCostMul = 1f,
                Year = year,
            };
        }

        private static void AssertRel(double expected, double actual, double relative, string what)
        {
            Assert.Near(expected, actual, Math.Abs(expected) * relative, what + ": expected " + expected + ", actual " + actual);
        }

        private static GpuStats CheckRow(string name, Row r, int year)
        {
            GpuContentDb db = Shipped();
            GpuStats s = GpuDesignMath.Evaluate(InputOf(r), ContextOf(db, r, year), db.Balance);
            string at = name + ": ";
            Assert.True(s.Valid, at + "the design is valid, reason " + s.InvalidReasonKey);
            Assert.Null(s.InvalidReasonKey);
            AssertRel(r.T, s.Transistors, 0.005, at + "transistors");
            Assert.Equal(r.S, s.Units, at + "blocks");
            AssertRel(r.C, s.ComputeMops, 0.005, at + "compute");
            AssertRel(r.B, s.BandwidthMBs, 0.005, at + "bandwidth");
            AssertRel(r.Ratio, s.BandwidthRatio, 0.005, at + "bandwidth ratio");
            AssertRel(r.P, s.EffectivePerf, 0.005, at + "effective performance");
            AssertRel(r.W, s.PowerW, 0.005, at + "power");
            AssertRel(r.Temp, s.TempC, 0.005, at + "temperature");
            AssertRel(r.FrPercent / 100.0, s.FailureRate, 0.005, at + "failure rate");
            AssertFinite(s, at);
            return s;
        }

        private static void AssertFeatures(GpuStats s, params string[] expected)
        {
            Assert.NotNull(s.Features);
            Assert.Equal(expected, s.Features, "the features of the architecture");
        }

        private static void AssertFinite(GpuStats s, string at)
        {
            foreach (double v in new[]
            {
                s.Transistors, s.ComputeMops, s.EffectivePerf, s.MaxClockMHz, s.BandwidthMBs, s.BandwidthRatio, s.PowerW, s.TempC, s.FailureRate, s.DieCost, s.UnitCost, s.ProjectCost,
            })
            {
                Assert.False(double.IsNaN(v) || double.IsInfinity(v), at + "a statistic is not finite: " + v);
            }
        }

        // ---- the calibration examples of design.md ----

        [Test]
        public void Calibration1985_TwoDAccelerator()
        {
            GpuStats s = CheckRow("1985", Y1985, 1985);
            AssertFeatures(s, "2d");
        }

        [Test]
        public void Calibration1997_ThreeD()
        {
            GpuStats s = CheckRow("1997", Y1997, 1997);
            AssertFeatures(s, "2d", "gui", "3d");
        }

        [Test]
        public void Calibration2005_Shader_IsAc31()
        {
            GpuStats s = CheckRow("2005", Y2005, 2005);
            // AC-3.1: 30 blocks, compute 122 400, bandwidth 51 200 MB/s, no memory limit, about 79 W, 74 C, failures about 11.4 %.
            Assert.Equal(30, s.Units);
            Assert.Near(122400, s.ComputeMops, 1e-3);
            Assert.Near(51200, s.BandwidthMBs, 1e-3);
            Assert.True(s.BandwidthRatio >= 1f, "no memory bottleneck");
            Assert.Near(122400, s.EffectivePerf, 1e-3, "no loss to memory");
            Assert.Near(79, s.PowerW, 0.5);
            Assert.Near(74, s.TempC, 0.6);
            Assert.Near(0.114, s.FailureRate, 0.0005);
            Assert.Near(510, s.MaxClockMHz, 1e-3, "the clock of the example is the maximum");
        }

        [Test]
        public void Calibration2022_Tensor_FloatNoiseInTransistorsStillGives21000Blocks()
        {
            GpuStats s = CheckRow("2022", Y2022, 2022);
            Assert.Equal(21000, s.Units, "5.25e7 x 600 is not exact in single precision (31 499 999 232): the blocks must not drop to 20 999");
            Assert.Near(2250, s.MaxClockMHz, 1e-3);
        }

        [Test]
        public void Ac32_NarrowerBus_HitsTheMemoryLimit()
        {
            GpuContentDb db = Shipped();
            Row r = new Row
            {
                Arch = Y2005.Arch, Mem = Y2005.Mem, Cooler = Y2005.Cooler, NodeName = Y2005.NodeName, Density = Y2005.Density, Die = Y2005.Die, Clock = Y2005.Clock,
                VramMB = Y2005.VramMB, CpuKHz = Y2005.CpuKHz, Bus = 128,
            };
            GpuStats s = GpuDesignMath.Evaluate(InputOf(r), ContextOf(db, r, 2005), db.Balance);
            Assert.True(s.Valid, s.InvalidReasonKey);
            Assert.Near(25600, s.BandwidthMBs, 1e-3);
            Assert.Near(0.837, s.BandwidthRatio, 0.001, "25 600 / (122 400 x 0.25)");
            Assert.Near(Math.Pow(0.8366, 0.7), s.EffectivePerf / 122400.0, 0.001, "the multiplier 0.837^0.7 is about 0.883");
            Assert.Near(108000, s.EffectivePerf, 600, "about 108 000");
            double loss = 1.0 - s.EffectivePerf / s.ComputeMops;
            Assert.Near(0.12, loss, 0.005, "the window shows 'memory-bound: -12 %'");
        }

        // ---- F2: blocks ----

        [Test]
        public void Units_AtLeastOne_AndFloorOfTheRatio()
        {
            Assert.Equal(1, GpuDesignMath.Units(10d, 15000f), "a die smaller than one block still has one");
            Assert.Equal(1, GpuDesignMath.Units(0d, 15000f));
            Assert.Equal(1, GpuDesignMath.Units(-5d, 15000f));
            Assert.Equal(1, GpuDesignMath.Units(double.NaN, 15000f));
            Assert.Equal(1, GpuDesignMath.Units(60000d, 0f), "no block size is read as no data");
            Assert.Equal(1, GpuDesignMath.Units(29999d, 15000f));
            Assert.Equal(2, GpuDesignMath.Units(30000d, 15000f));
            Assert.Equal(4, GpuDesignMath.Units(60000d, 15000f));
            Assert.Equal(4, GpuDesignMath.Units(74999d, 15000f));
            Assert.Equal(21000, GpuDesignMath.Units(31499999232d, 1500000f), "float noise of the transistor count");
            Assert.Equal(int.MaxValue, GpuDesignMath.Units(1e300, 1f), "no overflow");
        }

        // ---- F3, F4, F5 ----

        [Test]
        public void Clock_Compute_AndBandwidth_AreTheFormulas()
        {
            Assert.Near(8, GpuDesignMath.MaxClockMHz(16000f, 0.5f), 1e-4, "16 MHz CPU x 0.5");
            Assert.Near(2250, GpuDesignMath.MaxClockMHz(5000000f, 0.45f), 1e-3, "5 GHz CPU x 0.45");
            Assert.Near(562.5, GpuDesignMath.MinClockMHz(2250f, 0.25f), 1e-3, "the low end of the slider is 25 %");
            Assert.Near(0, GpuDesignMath.MaxClockMHz(float.NaN, 0.5f), 0, "no NaN");
            Assert.Equal(32d, GpuDesignMath.ComputeMops(4, 8f, 1f));
            Assert.Equal(122400d, GpuDesignMath.ComputeMops(30, 510f, 8f));
            Assert.Equal(0d, GpuDesignMath.ComputeMops(-3, 510f, 8f));
            Assert.Near(8, GpuDesignMath.BandwidthMBs(16, 4f), 1e-6);
            Assert.Near(51200, GpuDesignMath.BandwidthMBs(256, 1600f), 1e-3);
            Assert.Near(672000, GpuDesignMath.BandwidthMBs(384, 14000f), 1e-2);
        }

        // ---- F6 ----

        [Test]
        public void EffectivePerf_RatioIsFlooredAtTheBalanceValue_AndCapsAtOne()
        {
            float ratio;
            // compute 1000, 1 byte per op: need 1000 MB/s. Bandwidth 1 MB/s is a ratio of 0.001, below the 0.05 floor.
            double perf = GpuDesignMath.EffectivePerf(1000d, 1f, 1f, 0.7f, 0.05f, out ratio);
            Assert.Near(0.001, ratio, 1e-6, "the raw ratio is reported");
            AssertRel(1000d * Math.Pow(0.05, 0.7), perf, 1e-5, "but the multiplier uses the floor");

            // plenty of bandwidth: ratio above 1 gives the full compute
            perf = GpuDesignMath.EffectivePerf(1000d, 5000f, 1f, 0.7f, 0.05f, out ratio);
            Assert.Near(5.0, ratio, 1e-6);
            Assert.Near(1000d, perf, 1e-9);

            // in between
            perf = GpuDesignMath.EffectivePerf(1000d, 500f, 1f, 0.7f, 0.05f, out ratio);
            Assert.Near(0.5, ratio, 1e-6);
            AssertRel(1000d * Math.Pow(0.5, 0.7), perf, 1e-5, "r = 0.5");

            // the floor itself is a balance value
            perf = GpuDesignMath.EffectivePerf(1000d, 1f, 1f, 0.7f, 0.2f, out ratio);
            AssertRel(1000d * Math.Pow(0.2, 0.7), perf, 1e-5, "a floor of 0.2");
        }

        [Test]
        public void EffectivePerf_NothingToFeed_IsZeroWithoutNaN()
        {
            float ratio;
            Assert.Equal(0d, GpuDesignMath.EffectivePerf(0d, 100f, 2f, 0.7f, 0.05f, out ratio));
            Assert.Equal(1f, ratio);
            Assert.Equal(0d, GpuDesignMath.EffectivePerf(double.NaN, 100f, 2f, 0.7f, 0.05f, out ratio));
            Assert.Equal(1f, ratio);
            // no bytes per operation: memory cannot limit anything
            Assert.Equal(500d, GpuDesignMath.EffectivePerf(500d, 0f, 0f, 0.7f, 0.05f, out ratio));
            Assert.Equal(1f, ratio);
            // no bandwidth at all: the floor, not a division by zero
            double perf = GpuDesignMath.EffectivePerf(500d, 0f, 2f, 0.7f, 0.05f, out ratio);
            AssertRel(500d * Math.Pow(0.05, 0.7), perf, 1e-5, "the floor");
            Assert.Equal(0f, ratio);
        }

        // ---- F7, F8, F9 ----

        [Test]
        public void Power_Temperature_AreTheFormulas()
        {
            // 0.020 x 60 x 8 x 1000^-0.25 + 0.5
            Assert.Near(0.020 * 60 * 8 * Math.Pow(1000, -0.25) + 0.5, GpuDesignMath.PowerW(0.020f, 60f, 8f, 1000f, -0.25f, 0.5f), 1e-3);
            Assert.Near(2.21, GpuDesignMath.PowerW(0.020f, 60f, 8f, 1000f, -0.25f, 0.5f), 0.005, "design.md: 1985 is about 2 W");
            Assert.Near(0.5, GpuDesignMath.PowerW(0.020f, 60f, 0f, 1000f, -0.25f, 0.5f), 1e-6, "no clock: only the base power");
            Assert.Near(0.5, GpuDesignMath.PowerW(0.020f, 0f, 8f, 1000f, -0.25f, 0.5f), 1e-6, "no die: only the base power");
            Assert.True(GpuDesignMath.PowerW(0.020f, 60f, 8f, 0f, -0.25f, 0.5f) > 0.5f, "a node without density is read as 1, not as infinity");
            Assert.Near(39.42, GpuDesignMath.TemperatureC(2.21f, 2.0f, 35f), 0.01);
            Assert.Near(35, GpuDesignMath.TemperatureC(0f, 2.0f, 35f), 1e-6);
        }

        [Test]
        public void FailureRate_BelowSafeTemperature_HasNoDegreeTerm()
        {
            // (5 x 80 / 95 + 15 x 0.5 x 1) / 100
            float fr = GpuDesignMath.FailureRate(80f, 95f, 0.5f, 100f, 100f, 5f, 15f);
            Assert.Near((5.0 * 80 / 95 + 15 * 0.5) / 100.0, fr, 1e-6);
            // exactly at the safe temperature still no extra term
            fr = GpuDesignMath.FailureRate(95f, 95f, 0.5f, 100f, 100f, 5f, 15f);
            Assert.Near((5.0 * 95 / 95 + 15 * 0.5) / 100.0, fr, 1e-6);
        }

        [Test]
        public void FailureRate_AboveSafeTemperature_AddsPiTimesTheExcess()
        {
            float fr = GpuDesignMath.FailureRate(100f, 95f, 0.5f, 100f, 100f, 5f, 15f);
            Assert.Near((5.0 * 100 / 95 + 15 * 0.5 + Math.PI * 5) / 100.0, fr, 1e-5);
            float below = GpuDesignMath.FailureRate(95f, 95f, 0.5f, 100f, 100f, 5f, 15f);
            Assert.True(fr - below > 0.15f, "the degree term jumps: pi x 5 / 100 = 0.157 extra, got " + (fr - below));
            float hot = GpuDesignMath.FailureRate(150f, 95f, 0.5f, 100f, 100f, 5f, 15f);
            Assert.True(hot > 1f, "not capped here: every die fails above 1 (callers cap)");
        }

        [Test]
        public void FailureRate_ZeroMaxClock_IsNotNaN()
        {
            float fr = GpuDesignMath.FailureRate(50f, 95f, 0.5f, 100f, 0f, 5f, 15f);
            Assert.False(float.IsNaN(fr) || float.IsInfinity(fr));
            Assert.Near(5.0 * 50 / 95 / 100.0, fr, 1e-6, "no clock share when there is no maximum");
            fr = GpuDesignMath.FailureRate(50f, 0f, 0.5f, 0f, 0f, 5f, 15f);
            Assert.False(float.IsNaN(fr) || float.IsInfinity(fr), "a safe temperature of 0 falls back to 95");
        }

        [Test]
        public void FailureRate_RisesWithClock_AndWithComplexity()
        {
            float slow = GpuDesignMath.FailureRate(60f, 95f, 0.4f, 100f, 400f, 5f, 15f);
            float fast = GpuDesignMath.FailureRate(60f, 95f, 0.4f, 400f, 400f, 5f, 15f);
            float complex = GpuDesignMath.FailureRate(60f, 95f, 0.6f, 400f, 400f, 5f, 15f);
            Assert.True(slow < fast && fast < complex, slow + " < " + fast + " < " + complex);
        }

        // ---- Evaluate: validity ----

        [Test]
        public void Evaluate_ZeroClock_IsInvalid_WithoutNaN()
        {
            GpuContentDb db = Shipped();
            GpuDesignInput d = InputOf(Y2005);
            d.ClockMHz = 0f;
            GpuStats s = GpuDesignMath.Evaluate(d, ContextOf(db, Y2005, 2005), db.Balance);
            Assert.False(s.Valid);
            Assert.Equal(GpuDesignMath.ReasonClock, s.InvalidReasonKey);
            AssertFinite(s, "zero clock: ");
            Assert.Equal(0d, s.ComputeMops);
            Assert.Equal(0d, s.EffectivePerf);
            Assert.Near(0.5, s.PowerW, 1e-3, "only the base power");
        }

        [Test]
        public void Evaluate_ClockAboveTheMaximum_AndNotANumber_AreInvalid()
        {
            GpuContentDb db = Shipped();
            GpuDesignInput d = InputOf(Y2005);
            d.ClockMHz = 520f;
            GpuStats s = GpuDesignMath.Evaluate(d, ContextOf(db, Y2005, 2005), db.Balance);
            Assert.Equal(GpuDesignMath.ReasonClock, s.InvalidReasonKey, "10 MHz above the maximum");

            d.ClockMHz = 510f;
            Assert.True(GpuDesignMath.Evaluate(d, ContextOf(db, Y2005, 2005), db.Balance).Valid, "the maximum itself is allowed");

            d.ClockMHz = float.NaN;
            s = GpuDesignMath.Evaluate(d, ContextOf(db, Y2005, 2005), db.Balance);
            Assert.False(s.Valid);
            Assert.Equal(GpuDesignMath.ReasonNumbers, s.InvalidReasonKey);
            AssertFinite(s, "NaN clock: ");
        }

        [Test]
        public void Evaluate_DieThatDoesNotFitTheWafer_IsInvalid()
        {
            GpuContentDb db = Shipped();
            GpuDesignInput d = InputOf(Y2005);
            d.DieMm2 = 1200f;
            GpuDesignContext c = ContextOf(db, Y2005, 2005);
            c.WaferDiameter = 100f;   // DiesPerWafer(1200, 100, 1) = 0
            c.Transistors = 0d;
            GpuStats s = GpuDesignMath.Evaluate(d, c, db.Balance);
            Assert.False(s.Valid);
            Assert.Equal(GpuDesignMath.ReasonDie, s.InvalidReasonKey);
            Assert.Equal(0, s.DpwFull);
            Assert.True(s.Transistors > 0d, "without a transistor count in the context, density x die is used");
            AssertFinite(s, "does not fit: ");

            // a die that fits (300 mm2 on 300 mm) reports its dies per wafer
            GpuStats ok = GpuDesignMath.Evaluate(InputOf(Y2005), ContextOf(db, Y2005, 2005), db.Balance);
            Assert.Equal(197, ok.DpwFull);
            Assert.Equal(177, ok.DpwYield);
        }

        [Test]
        public void Evaluate_RangesOfTheParts_AreChecked()
        {
            GpuContentDb db = Shipped();
            GpuDesignContext c = ContextOf(db, Y2005, 2005);

            GpuDesignInput d = InputOf(Y2005);
            d.DieMm2 = 10f;   // minDie is 20
            Assert.Equal(GpuDesignMath.ReasonDieSize, GpuDesignMath.Evaluate(d, c, db.Balance).InvalidReasonKey);

            d = InputOf(Y2005);
            GpuDesignContext limited = c;
            limited.MaxDieMm2 = 250f;
            Assert.Equal(GpuDesignMath.ReasonMaxDie, GpuDesignMath.Evaluate(d, limited, db.Balance).InvalidReasonKey, "300 mm2 above the largest die of the year");
            limited.MaxDieMm2 = 0f;
            Assert.True(GpuDesignMath.Evaluate(d, limited, db.Balance).Valid, "0 means no limit");

            d = InputOf(Y2005);
            d.BusWidthBits = 192;   // GDDR3: 128, 256, 384
            Assert.Equal(GpuDesignMath.ReasonBus, GpuDesignMath.Evaluate(d, c, db.Balance).InvalidReasonKey);

            d = InputOf(Y2005);
            d.VramMB = 2048f;   // GDDR3 allows 1024
            Assert.Equal(GpuDesignMath.ReasonVram, GpuDesignMath.Evaluate(d, c, db.Balance).InvalidReasonKey);
            d.VramMB = 0f;
            Assert.Equal(GpuDesignMath.ReasonVram, GpuDesignMath.Evaluate(d, c, db.Balance).InvalidReasonKey);
            d.VramMB = 1024f;
            Assert.True(GpuDesignMath.Evaluate(d, c, db.Balance).Valid, "the largest allowed memory");
        }

        [Test]
        public void Evaluate_MissingParts_AndBrokenNumbers_AreInvalid()
        {
            GpuContentDb db = Shipped();
            GpuDesignContext c = ContextOf(db, Y2005, 2005);

            GpuDesignContext noArch = c;
            noArch.Arch = null;
            GpuStats s = GpuDesignMath.Evaluate(InputOf(Y2005), noArch, db.Balance);
            Assert.False(s.Valid);
            Assert.Equal(GpuDesignMath.ReasonParts, s.InvalidReasonKey);
            Assert.NotNull(s.Features);

            GpuDesignContext noCooler = c;
            noCooler.Cooler = null;
            Assert.Equal(GpuDesignMath.ReasonParts, GpuDesignMath.Evaluate(InputOf(Y2005), noCooler, db.Balance).InvalidReasonKey);

            GpuDesignContext noDensity = c;
            noDensity.Node.Density = 0f;
            Assert.Equal(GpuDesignMath.ReasonNode, GpuDesignMath.Evaluate(InputOf(Y2005), noDensity, db.Balance).InvalidReasonKey);

            GpuDesignContext infiniteCost = c;
            infiniteCost.DieUnitCost = float.PositiveInfinity;   // every die fails: RealUnitCost divides by zero
            s = GpuDesignMath.Evaluate(InputOf(Y2005), infiniteCost, db.Balance);
            Assert.Equal(GpuDesignMath.ReasonNumbers, s.InvalidReasonKey);
            AssertFinite(s, "infinite die cost: ");

            // a missing balance is the shipped numbers (the defaults of the DTO), not a crash
            Assert.True(GpuDesignMath.Evaluate(InputOf(Y2005), c, null).Valid);
        }

        [Test]
        public void Evaluate_Overheating_ShowsTheHotNumbers_AndCapsTheFailureRate()
        {
            GpuContentDb db = Shipped();
            Row hot = new Row
            {
                Arch = Y2022.Arch, Mem = Y2022.Mem, Cooler = "cool.passive", NodeName = Y2022.NodeName, Density = Y2022.Density, Die = Y2022.Die, Clock = Y2022.Clock,
                VramMB = Y2022.VramMB, CpuKHz = Y2022.CpuKHz, Bus = Y2022.Bus,
            };
            GpuStats s = GpuDesignMath.Evaluate(InputOf(hot), ContextOf(db, hot, 2022), db.Balance);
            Assert.True(s.TempC > 95f, "a 254 W chip under a passive heatsink: " + s.TempC);
            Assert.Near(35 + 254.255 * 2.0, s.TempC, 1.0);
            Assert.Equal(1f, s.FailureRate, "capped at 100 %");
            Assert.True(s.Valid, "overheating is a warning, not a block");
        }

        [Test]
        public void Evaluate_ReadsTheBalanceNumbers()
        {
            GpuContentDb db = Shipped();
            var balance = new GpuBalanceDto();
            balance.design.ambientC = 55f;   // +20 degrees over the shipped 35
            balance.design.bandwidthExponent = 1.0f;
            GpuStats shipped = GpuDesignMath.Evaluate(InputOf(Y1997), ContextOf(db, Y1997, 1997), db.Balance);
            GpuStats changed = GpuDesignMath.Evaluate(InputOf(Y1997), ContextOf(db, Y1997, 1997), balance);
            Assert.Near(shipped.TempC + 20, changed.TempC, 1e-3);
            Assert.True(changed.EffectivePerf < shipped.EffectivePerf, "exponent 1.0 hurts a memory-bound card more than 0.7");
            Assert.Near(300 * 0.6667, changed.EffectivePerf, 0.5, "r^1.0");
        }

        [Test]
        public void Evaluate_IsPure_SameInputSameStats()
        {
            GpuContentDb db = Shipped();
            GpuStats a = GpuDesignMath.Evaluate(InputOf(Y2005), ContextOf(db, Y2005, 2005), db.Balance);
            GpuStats b = GpuDesignMath.Evaluate(InputOf(Y2005), ContextOf(db, Y2005, 2005), db.Balance);
            Assert.Equal(a.EffectivePerf, b.EffectivePerf);
            Assert.Equal(a.UnitCost, b.UnitCost);
            Assert.Equal(a.ProjectCost, b.ProjectCost);
            Assert.Equal(a.Units, b.Units);
        }

        [Test]
        public void EveryReasonKey_IsAKeyOfTheStringTables_AndTheStatsNameThem()
        {
            // The text of each key is checked by GpuFeatureSourceTests (every "gpu.*" literal of the code is in both languages); here only that the keys are distinct and prefixed.
            var keys = new[]
            {
                GpuDesignMath.ReasonParts, GpuDesignMath.ReasonNumbers, GpuDesignMath.ReasonNode, GpuDesignMath.ReasonDieSize, GpuDesignMath.ReasonMaxDie,
                GpuDesignMath.ReasonDie, GpuDesignMath.ReasonClock, GpuDesignMath.ReasonBus, GpuDesignMath.ReasonVram,
            };
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string key in keys)
            {
                Assert.True(key.StartsWith("gpu.design.block.", StringComparison.Ordinal), key);
                Assert.True(seen.Add(key), "duplicate " + key);
            }
        }
    }
}
