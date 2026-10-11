using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Tests.Architecture;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// Cost and project of a GPU design and the ports of the formulas of the game (TSK-007, FR-3, FR-4, FR-17; F1, F10-F14, F27). The ports are checked against
    /// hand calculations of <c>CpuMath.TransistorCount</c>, <c>ProductionMath.DiesPerWafer</c> and <c>CpuMath.RealUnitCost</c> (the values were worked out outside the code
    /// under test, from the formulas in CODEMAP.md); the self-check of the game side repeats the comparison against the real methods. Also the cost of a project
    /// (AC-4.1, AC-4.4), the fall of video memory prices (F12), co-development (F27) and that the project does not use the CPU's own project formula.
    /// </summary>
    [Covers(typeof(GpuDesignMath))]
    public class PortEquivalenceTests
    {
        private static GpuContentDb Shipped()
        {
            var problems = new List<string>();
            GpuContentDb db = GpuFiles.Load(GpuFiles.Shipped(), problems);
            Assert.Equal(0, problems.Count, "the shipped content has problems:\n  " + string.Join("\n  ", problems.ToArray()));
            return db;
        }

        private static void AssertRel(double expected, double actual, double relative, string what)
        {
            Assert.Near(expected, actual, Math.Abs(expected) * relative, what + ": expected " + expected + ", actual " + actual);
        }

        // ---- F1: CpuMath.TransistorCount ----

        [Test]
        public void TransistorsPort_IsDensityTimesClampedIncreaseTimesDie()
        {
            Assert.Equal(60000d, GpuDesignMath.TransistorsPort(1000f, 1f, 60f));
            Assert.Equal(1.35e8, GpuDesignMath.TransistorsPort(3e5f, 1.5f, 300f), "90nm, +50 % density");
            Assert.Equal(60000d, GpuDesignMath.TransistorsPort(1000f, 0.2f, 60f), "an increase below 1 is read as 1 (clamp(x, 1, 999999))");
            Assert.Equal(60000d, GpuDesignMath.TransistorsPort(1000f, 0f, 60f));
            AssertRel(1000d * 999999d * 60d, GpuDesignMath.TransistorsPort(1000f, 2e6f, 60f), 1e-6, "and above 999 999 it stops");
            Assert.Equal(0d, GpuDesignMath.TransistorsPort(1000f, 1f, 0f));
        }

        [Test]
        public void TransistorsPort_IsSinglePrecision_LikeTheGame()
        {
            // The game multiplies floats. 5.25e7 x 600 = 3.15e10 is not a float: the nearest one is 31 499 999 232.
            Assert.Equal(31499999232d, GpuDesignMath.TransistorsPort(5.25e7f, 1f, 600f));
        }

        // ---- F10: ProductionMath.DiesPerWafer ----

        [Test]
        public void DiesPerWaferPort_MatchesTheControlValues()
        {
            // DPW(300, 300, 1.0) = 197 is the value of CODEMAP / design.md; the others are round((pi D^2 / (4 A) - pi D / sqrt(2 A)) x yield) worked out by hand.
            Assert.Equal(197, GpuDesignMath.DiesPerWaferPort(300f, 300f, 1.0f));
            Assert.Equal(177, GpuDesignMath.DiesPerWaferPort(300f, 300f, 0.9f));
            Assert.Equal(243, GpuDesignMath.DiesPerWaferPort(100f, 200f, 0.9f));
            Assert.Equal(1092, GpuDesignMath.DiesPerWaferPort(60f, 300f, 1.0f));
            Assert.Equal(91, GpuDesignMath.DiesPerWaferPort(600f, 300f, 1.0f));
            Assert.Equal(82, GpuDesignMath.DiesPerWaferPort(600f, 300f, 0.9f));
            Assert.Equal(257, GpuDesignMath.DiesPerWaferPort(80f, 200f, 0.75f));
            Assert.Equal(359, GpuDesignMath.DiesPerWaferPort(140f, 300f, 0.8f));
            Assert.Equal(270, GpuDesignMath.DiesPerWaferPort(100f, 200f, 1.0f));
            Assert.Equal(216, GpuDesignMath.DiesPerWaferPort(100f, 200f, 0.8f));
            Assert.Equal(449, GpuDesignMath.DiesPerWaferPort(140f, 300f, 1.0f));
        }

        [Test]
        public void DiesPerWaferPort_BigDies_GiveZeroOrLess_AndNothingGivesZero()
        {
            Assert.Equal(2, GpuDesignMath.DiesPerWaferPort(800f, 100f, 1.0f), "the formula still counts two dies of 800 mm2 on 100 mm");
            Assert.Equal(0, GpuDesignMath.DiesPerWaferPort(1200f, 100f, 1.0f));
            Assert.Equal(-1, GpuDesignMath.DiesPerWaferPort(2000f, 100f, 1.0f), "like the game: the edge term wins and the count goes below zero");
            Assert.Equal(0, GpuDesignMath.DiesPerWaferPort(0f, 300f, 1.0f), "the game would divide by zero");
            Assert.Equal(0, GpuDesignMath.DiesPerWaferPort(300f, 0f, 1.0f));
            Assert.Equal(0, GpuDesignMath.DiesPerWaferPort(float.NaN, 300f, 1.0f));
            Assert.Equal(0, GpuDesignMath.DiesPerWaferPort(300f, 300f, 0f));
        }

        [Test]
        public void DiesPerWaferPort_AgreesWithTheFormulaInDoublePrecision_OnAGrid()
        {
            int checkedCount = 0;
            foreach (float wafer in new[] { 50f, 100f, 150f, 200f, 300f, 450f })
            {
                foreach (float yield in new[] { 1.0f, 0.9f, 0.75f, 0.5f })
                {
                    for (float die = 20f; die <= 900f; die += 17.5f)
                    {
                        double raw = (Math.PI * wafer * wafer / (4 * die) - Math.PI * wafer / Math.Sqrt(2 * die)) * yield;
                        int port = GpuDesignMath.DiesPerWaferPort(die, wafer, yield);
                        // single and double precision can part only at a half (x.5), by one die
                        Assert.True(Math.Abs(port - Math.Round(raw)) <= 1, "die " + die + ", wafer " + wafer + ", yield " + yield + ": port " + port + ", formula " + raw);
                        checkedCount++;
                    }
                }
            }
            Assert.True(checkedCount > 1000, "grid size " + checkedCount);
        }

        [Test]
        public void RoundToInt_RoundsHalvesToEven_LikeMathfRoundToInt()
        {
            Assert.Equal(2, GpuDesignMath.RoundToInt(2.5f));
            Assert.Equal(4, GpuDesignMath.RoundToInt(3.5f));
            Assert.Equal(-2, GpuDesignMath.RoundToInt(-2.5f));
            Assert.Equal(3, GpuDesignMath.RoundToInt(2.6f));
            Assert.Equal(0, GpuDesignMath.RoundToInt(float.NaN));
            Assert.Equal(int.MaxValue, GpuDesignMath.RoundToInt(float.PositiveInfinity));
            Assert.Equal(int.MinValue, GpuDesignMath.RoundToInt(float.NegativeInfinity));
        }

        // ---- F11: CpuMath.RealUnitCost ----

        [Test]
        public void RealUnitCostPort_MatchesTheHandCalculation()
        {
            // unit = ((M x 50 x DPW(1.0) + 50 x 2000 + maintenance) x markup) / G + royalties,  G = round(50 x dpwYield - round(50 x dpwYield x FR))
            // 300 mm2 die on a 300 mm wafer: DPW(1.0) = 197, DPW(0.9) = 177; 50 x 177 = 8850, 8850 x 0.10 = 885, G = 7965.
            AssertRel(85.09102322661644, GpuDesignMath.RealUnitCostPort(430000f, 197, 15f, 177f, 0.10f, 1f, 0f), 1e-5, "own factory");
            // fabless: markup 2.0 doubles the numerator, royalties are added after the division
            AssertRel(170.18204645323289, GpuDesignMath.RealUnitCostPort(430000f, 197, 15f, 177f, 0.10f, 2f, 0f), 1e-5, "markup 2");
            AssertRel(177.68204645323289, GpuDesignMath.RealUnitCostPort(430000f, 197, 15f, 177f, 0.10f, 2f, 7.5f), 1e-5, "markup 2 and royalties 7.5");
            // 100 mm2 on 200 mm, yield 0.8 (216 dies), failure 6.57 %: DPW(1.0) = 270; 50 x 216 = 10800; 10800 x 0.0657 = 709.6 -> 710; G = 10090
            AssertRel(56.18275520317146, GpuDesignMath.RealUnitCostPort(358884f, 270, 8f, 216f, 0.0657f, 1f, 0f), 1e-5, "small die");
            // 140 mm2 on 300 mm, yield 0.8 (359), failure 12.3 %, fabless: G = 15742
            AssertRel(211.05958582136958, GpuDesignMath.RealUnitCostPort(1.0e6f, 449, 25f, 359f, 0.123f, 2f, 0f), 1e-5, "fabless");
            // the 2022 reference of design.md: 600 mm2 on 300 mm at yield 0.9 (91 and 82 dies), failure 12.3 %: about 180
            float die2022 = GpuDesignMath.RealUnitCostPort(430000f, 91, 25f, 82f, 0.123f, 1f, 0f);
            AssertRel(179.01835372636262, die2022, 1e-5, "2022 die");
            Assert.Near(180, die2022, 2, "design.md: dieCost is about 180");
        }

        [Test]
        public void RealUnitCostPort_WaferMaterialIsAParameter_SoAShockReachesTheCost()
        {
            float normal = GpuDesignMath.RealUnitCostPort(430000f, 197, 15f, 177f, 0.10f, 1f, 0f);
            float defaultArg = GpuDesignMath.RealUnitCostPort(430000f, 197, 15f, 177f, 0.10f, 1f, 0f, GpuDesignMath.DefaultWaferMaterialCost);
            Assert.Equal(normal, defaultArg, "the default is 2000, the value of ProductionMath.WaferMaterialCost(1.0)");
            float shocked = GpuDesignMath.RealUnitCostPort(430000f, 197, 15f, 177f, 0.10f, 1f, 0f, 3000f);
            AssertRel(91.36848713119899, shocked, 1e-5, "wafer material 3000");
            AssertRel(50.0 * 1000.0 / 7965.0, shocked - normal, 1e-3, "50 wafers x 1000 more, spread over the good dies");
        }

        [Test]
        public void RealUnitCostPort_NoGoodDies_IsInfinity_AsInTheGame()
        {
            Assert.True(float.IsPositiveInfinity(GpuDesignMath.RealUnitCostPort(430000f, 197, 15f, 177f, 1.0f, 1f, 0f)), "every die fails");
            Assert.True(float.IsPositiveInfinity(GpuDesignMath.RealUnitCostPort(430000f, 0, 15f, 0f, 0.1f, 1f, 0f)), "no dies fit");
        }

        // ---- F12: memory price and unit cost ----

        [Test]
        public void MemoryCost_FallsEveryYear_ToTheFloor_AndNotBeforeTheRelease()
        {
            GpuContentDb db = Shipped();
            GpuMemoryDto sgram = db.Mem("vram.sgram");   // 1994, 4 per MB, x0.7 a year, floor 0.2
            Assert.Near(4.0 * Math.Pow(0.7, 4), GpuDesignMath.MemoryCostPerMB(sgram, 1998), 1e-6, "sgram in 1998: 4 x 0.7^4");
            Assert.Near(0.9604, GpuDesignMath.MemoryCostPerMB(sgram, 1998), 1e-4);
            Assert.Near(4.0, GpuDesignMath.MemoryCostPerMB(sgram, 1994), 1e-6, "the year of release");
            Assert.Near(4.0, GpuDesignMath.MemoryCostPerMB(sgram, 1990), 1e-6, "before the release the price does not rise");
            Assert.Near(4.0 * 0.2, GpuDesignMath.MemoryCostPerMB(sgram, 2030), 1e-6, "the floor: 20 % of the first price");
            Assert.Near(4.0 * Math.Pow(0.7, 3), GpuDesignMath.MemoryCostPerMB(sgram, 1997), 1e-5, "three years: 4 x 0.7^3");
            Assert.Near(4.0 * 0.2, GpuDesignMath.MemoryCostPerMB(sgram, 1999), 1e-5, "0.7^5 = 0.168 is under the floor of 0.2: the floor holds from 1999");
            Assert.Equal(0f, GpuDesignMath.MemoryCostPerMB(null, 2000));
        }

        [Test]
        public void UnitCost_IsTheSumOfDieMemoryBoardAndCooler_TimesTheChannel()
        {
            // F12 by hand: dieCost + vram x pricePerMB x vramMul + boardBase + bus x perBit + cooler, then x unitCostMul
            float u = GpuDesignMath.UnitCost(100f, 256f, 0.5f, 1f, 256, 20f, 0.08f, 25f, 1f);
            Assert.Near(100 + 256 * 0.5 + 20 + 256 * 0.08 + 25, u, 1e-3);
            Assert.Near(293.48, u, 0.01);

            float vramDearer = GpuDesignMath.UnitCost(100f, 256f, 0.5f, 1.5f, 256, 20f, 0.08f, 25f, 1f);
            Assert.Near(u + 256 * 0.5 * 0.5, vramDearer, 1e-3, "gpu.vramCost moves the memory part only");

            float dearer = GpuDesignMath.UnitCost(100f, 256f, 0.5f, 1f, 256, 20f, 0.08f, 25f, 1.1f);
            Assert.Near(u * 1.1, dearer, 1e-3, "production.unitCost moves everything");

            Assert.Near(u, GpuDesignMath.UnitCost(100f, 256f, 0.5f, 0f, 256, 20f, 0.08f, 25f, 0f), 1e-3, "a multiplier of 0 is 'no data', as in a context nobody filled");
            Assert.Near(100, GpuDesignMath.UnitCost(100f, 0f, 0.5f, 1f, 0, 0f, 0f, 0f, 1f), 1e-6);
        }

        [Test]
        public void UnitCost_2022Reference_IsAbout405()
        {
            GpuContentDb db = Shipped();
            // design.md: 2022, 300 mm wafer, yield 0.9, maintenance about 4.3e5 a line: dieCost about 180, 24 GB of GDDR6 about 129, board 20 + 384 x 0.08 = 51, cooler 45.
            float dieCost = GpuDesignMath.RealUnitCostPort(430000f, 91, db.Arch("arch.tensor").dieMaterialCost, 82f, 0.123f, 1f, 0f);
            float perMb = GpuDesignMath.MemoryCostPerMB(db.Mem("vram.gddr6"), 2022);
            Assert.Near(0.008 * Math.Pow(0.9, 4), perMb, 1e-6);
            Assert.Near(129, 24576 * perMb, 0.5, "24 GB of GDDR6");
            float u = GpuDesignMath.UnitCost(dieCost, 24576f, perMb, 1f, 384, db.Balance.design.boardBase, db.Balance.design.boardCostPerBit, db.Cooler("cool.vapor").cost, 1f);
            AssertRel(405, u, 0.005, "U is about 405");

            float extras = GpuDesignMath.PerGoodExtras(24576f, perMb, 1f, 384, db.Balance.design.boardBase, db.Balance.design.boardCostPerBit, db.Cooler("cool.vapor").cost, 1f);
            Assert.Near(u - dieCost, extras, 1e-2, "the extras are the cost without the die");
            float extrasMul = GpuDesignMath.PerGoodExtras(24576f, perMb, 1f, 384, 20f, 0.08f, 45f, 1.2f);
            float extrasOne = GpuDesignMath.PerGoodExtras(24576f, perMb, 1f, 384, 20f, 0.08f, 45f, 1f);
            Assert.Near(extrasOne * 1.2, extrasMul, 1e-2, "the unit cost channel applies to the extras too");
        }

        // ---- F13: project ----

        [Test]
        public void ProjectCost_Ac41_IsOnePointFourFiveE8()
        {
            // AC-4.1: (8e7 + 5e7 x 0.5 + 4e7) x 1.0
            Assert.Near(1.45e8, GpuDesignMath.ProjectCost(8e7f, 5e7f, 0.5f, 4e7f, 1f, 1f), 1.0);
            Assert.Equal(420, GpuDesignMath.ProjectDays(220, 200));
        }

        [Test]
        public void ProjectCost_Ac44_FablessPaysTwoThirds()
        {
            // AC-4.4: 1.45e8 x 0.67 = 9.715e7 (about 9.72e7)
            AssertRel(9.72e7, GpuDesignMath.ProjectCost(8e7f, 5e7f, 0.5f, 4e7f, 0.67f, 1f), 0.001, "fabless");
        }

        [Test]
        public void Evaluate_ProjectOfTheShippedShaderGddr3Card_IsAc41()
        {
            GpuContentDb db = Shipped();
            GpuDesignInput d = new GpuDesignInput
            {
                ArchId = "arch.shader", MemId = "vram.gddr3", CoolerId = "cool.fan", NodeName = "90nm", Name = "AC41", DieMm2 = 300f, ClockMHz = 510f, VramMB = 256f, BusWidthBits = 256,
            };
            GpuDesignContext c = new GpuDesignContext
            {
                Arch = db.Arch("arch.shader"), Mem = db.Mem("vram.gddr3"), Cooler = db.Cooler("cool.fan"),
                Node = new NodeSpec { Name = "90nm", Density = 3e5f, ProjectCost = 5e7f },   // ProjectCost of 90nm is 5e7
                Transistors = 9e7, CompanyMaxFreqKHz = 3400000f, WaferDiameter = 300f, Yield = 0.9f, DieUnitCost = 60f,
                DevModifier = 1f, CoDevFactor = 1f, UnitCostMul = 1f, VramCostMul = 1f, Year = 2005,
            };
            GpuStats s = GpuDesignMath.Evaluate(d, c, db.Balance);
            Assert.True(s.Valid, s.InvalidReasonKey);
            Assert.Near(1.45e8, s.ProjectCost, 1.0, "AC-4.1");
            Assert.Equal(420, s.ProjectDays, "220 + 200 days");

            c.DevModifier = 0.67f;
            AssertRel(9.72e7, GpuDesignMath.Evaluate(d, c, db.Balance).ProjectCost, 0.001, "AC-4.4");

            c.DevModifier = 1f;
            c.CoDevFactor = 0.85f;
            AssertRel(1.45e8 * 0.85, GpuDesignMath.Evaluate(d, c, db.Balance).ProjectCost, 1e-4, "co-development");

            c.CoDevFactor = 1f;
            c.Node.ProjectCost = 0f;
            Assert.Near(1.2e8, GpuDesignMath.Evaluate(d, c, db.Balance).ProjectCost, 1.0, "the node share is 0.5 of the node's own project cost");
        }

        [Test]
        public void Evaluate_UnitCost_UsesTheYearOfTheMemoryPrice_AndTheChannels()
        {
            GpuContentDb db = Shipped();
            GpuDesignInput d = new GpuDesignInput
            {
                ArchId = "arch.3d", MemId = "vram.sgram", CoolerId = "cool.passive", NodeName = "350nm", Name = "Voodoo", DieMm2 = 80f, ClockMHz = 100f, VramMB = 8f, BusWidthBits = 128,
            };
            GpuDesignContext c = new GpuDesignContext
            {
                Arch = db.Arch("arch.3d"), Mem = db.Mem("vram.sgram"), Cooler = db.Cooler("cool.passive"),
                Node = new NodeSpec { Name = "350nm", Density = 16000f, ProjectCost = 2e6f },
                Transistors = 1.28e6, CompanyMaxFreqKHz = 250000f, WaferDiameter = 200f, Yield = 0.8f, DieUnitCost = 20f,
                DevModifier = 1f, CoDevFactor = 1f, UnitCostMul = 1f, VramCostMul = 1f, Year = 1998,
            };
            // 20 + 8 MB x 4 x 0.7^4 + 20 + 128 x 0.08 + 2
            double expected = 20 + 8 * 4 * Math.Pow(0.7, 4) + 20 + 128 * 0.08 + 2;
            Assert.Near(expected, GpuDesignMath.Evaluate(d, c, db.Balance).UnitCost, 1e-3, "sgram in 1998");

            c.Year = 1994;
            double dearer = 20 + 8 * 4.0 + 20 + 128 * 0.08 + 2;
            Assert.Near(dearer, GpuDesignMath.Evaluate(d, c, db.Balance).UnitCost, 1e-3, "the year of release: full price");

            c.Year = 1998;
            c.UnitCostMul = 1.2f;
            Assert.Near(expected * 1.2, GpuDesignMath.Evaluate(d, c, db.Balance).UnitCost, 1e-3, "production.unitCost");

            c.UnitCostMul = 1f;
            c.VramCostMul = 2f;
            Assert.Near(expected + 8 * 4 * Math.Pow(0.7, 4), GpuDesignMath.Evaluate(d, c, db.Balance).UnitCost, 1e-3, "gpu.vramCost");
        }

        // ---- F14: the daily cost of a project ----

        [Test]
        public void DailyProjectCost_FollowsInflationSinceTheStart()
        {
            Assert.Near(1.45e8 / 420.0, GpuDesignMath.DailyProjectCost(1.45e8f, 420, 1f, 1f), 1.0);
            Assert.Near(1.45e8 / 420.0 * 1.05, GpuDesignMath.DailyProjectCost(1.45e8f, 420, 1.05f, 1f), 1.0, "5 % inflation");
            Assert.Near(1.45e8 / 420.0 * 1.05 / 1.02, GpuDesignMath.DailyProjectCost(1.45e8f, 420, 1.05f, 1.02f), 1.0, "relative to the inflation at the start");
            Assert.Near(1.45e8, GpuDesignMath.DailyProjectCost(1.45e8f, 0, 1f, 1f), 1.0, "no days: the whole cost, not infinity");
            Assert.Near(1.45e8 / 420.0, GpuDesignMath.DailyProjectCost(1.45e8f, 420, 1f, 0f), 1.0, "a start inflation of 0 is read as 1");
        }

        // ---- F27: co-development ----

        [Test]
        public void CoDevFactor_NeedsTheSameNodeAndACpuNoOlderThanTwoYears()
        {
            var cpus = new List<GpuCpuRef> { new GpuCpuRef(2004, "90nm"), new GpuCpuRef(2001, "130nm") };
            Assert.Equal(0.85f, GpuDesignMath.CoDevFactor(cpus, "90nm", 2005, 2, 0.85f), "a CPU of last year on the same node");
            Assert.Equal(1f, GpuDesignMath.CoDevFactor(cpus, "65nm", 2005, 2, 0.85f), "another node");
            Assert.Equal(1f, GpuDesignMath.CoDevFactor(cpus, "130nm", 2005, 2, 0.85f), "the 130nm CPU is four years old");
            Assert.Equal(1f, GpuDesignMath.CoDevFactor(cpus, "90NM", 2005, 2, 0.85f), "names compare exactly");
        }

        [Test]
        public void CoDevFactor_AgeLimitIsInclusive()
        {
            var cpus = new List<GpuCpuRef> { new GpuCpuRef(2003, "90nm") };
            Assert.Equal(0.85f, GpuDesignMath.CoDevFactor(cpus, "90nm", 2005, 2, 0.85f), "exactly two years old counts");
            Assert.Equal(1f, GpuDesignMath.CoDevFactor(cpus, "90nm", 2006, 2, 0.85f), "three years old does not");
            Assert.Equal(0.85f, GpuDesignMath.CoDevFactor(cpus, "90nm", 2006, 3, 0.85f), "the limit is a balance value");
            Assert.Equal(1f, GpuDesignMath.CoDevFactor(cpus, "90nm", 2005, 0, 0.85f), "with a limit of 0 only this year counts: 2003 is not this year");
            Assert.Equal(1f, GpuDesignMath.CoDevFactor(cpus, "90nm", 2005, -4, 0.85f), "a negative limit is read as 0");
            var thisYear = new List<GpuCpuRef> { new GpuCpuRef(2005, "90nm") };
            Assert.Equal(0.85f, GpuDesignMath.CoDevFactor(thisYear, "90nm", 2005, 0, 0.85f), "a CPU of this year counts at a limit of 0");
        }

        [Test]
        public void CoDevFactor_NoData_NoDiscount()
        {
            Assert.Equal(1f, GpuDesignMath.CoDevFactor(null, "90nm", 2005, 2, 0.85f));
            Assert.Equal(1f, GpuDesignMath.CoDevFactor(new List<GpuCpuRef>(), "90nm", 2005, 2, 0.85f));
            var cpus = new List<GpuCpuRef> { new GpuCpuRef(2005, "90nm") };
            Assert.Equal(1f, GpuDesignMath.CoDevFactor(cpus, null, 2005, 2, 0.85f));
            Assert.Equal(1f, GpuDesignMath.CoDevFactor(cpus, "", 2005, 2, 0.85f));
            Assert.Equal(1f, GpuDesignMath.CoDevFactor(cpus, "90nm", 2005, 2, 1.0f), "a factor of 1 is no discount");
            Assert.Equal(1f, GpuDesignMath.CoDevFactor(cpus, "90nm", 2005, 2, 0f), "a factor of 0 is not a free project");
            Assert.Equal(1f, GpuDesignMath.CoDevFactor(cpus, "90nm", 2005, 2, 1.5f), "and no surcharge");
            Assert.Equal(1f, GpuDesignMath.CoDevFactor(cpus, "90nm", 2005, 2, float.NaN));
        }

        // ---- the project does not use the CPU formulas ----

        [Test]
        public void TheDesignMath_DoesNotUseTheCpuProjectFormula_OrItsChannel()
        {
            string path = Path.Combine(GpuFiles.FeatureSourceDir, "Logic", "Math", "GpuDesignMath.cs");
            string code = SourceScan.Strip(SourceScan.ReadText(path), false);
            Assert.False(Regex.IsMatch(code, @"\bCpuMath\b"), "GpuDesignMath computes the project itself (D3): no CpuMath");
            Assert.False(code.Contains("development.cost.design"), "the channel of the CPU designs does not apply to GPU projects");
            Assert.False(Regex.IsMatch(code, @"\bModModifiers\b"), "the channels are read by the game side and arrive as numbers");
        }
    }
}
