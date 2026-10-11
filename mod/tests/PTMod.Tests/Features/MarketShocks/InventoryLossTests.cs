using System.Collections.Generic;
using PTMod.Core.Logic.Json;
using PTMod.Features.MarketShocks;
using static PTMod.Tests.Features.MarketShocks.ShockTestData;

namespace PTMod.Tests.Features.MarketShocks
{
    /// <summary>
    /// market-shocks, FR-7 item 3 (TSK-006, TSK-008): <c>InventoryLoss</c> - the checks of <c>fraction</c>, the share rolled and scaled, the loss per CPU
    /// (AC-7.3: stocks 10 000 and 3 000 at 0.1 lose 1 000 and 300, "Списано 1 300 шт."), the CPUs that do not take part (variants, retired, other makers),
    /// the plan kept as <c>[saveId, lost, ...]</c> pairs, and its application: never below 0, a CPU that is not found is passed over.
    /// </summary>
    [Covers(typeof(LossSpec), typeof(InventoryLossMath), typeof(StockCandidate), typeof(IStockBook))]
    public class InventoryLossTests
    {
        private static StockCandidate Cpu(int saveId, int stock, bool variant = false, bool retired = false, bool byTarget = true)
        {
            return new StockCandidate(saveId, stock, variant, retired, byTarget);
        }

        private static List<string> LossErrors(string json)
        {
            var errors = new List<string>();
            LossSpec spec;
            bool ok = LossSpec.TryRead(Params(json), errors, out spec);
            Assert.Equal(errors.Count == 0, ok);
            Assert.Equal(ok, spec != null);
            return errors;
        }

        // ---- AC-7.3 ----

        [Test]
        public void Ac73_TwoCpusWithTenThousandAndThreeThousand_AtOneTenth_LoseThirteenHundred()
        {
            var cpus = new List<StockCandidate> { Cpu(11, 10000), Cpu(12, 3000) };

            int total;
            float[] plan = InventoryLossMath.Plan(cpus, 0.1f, out total);

            Assert.Equal(new[] { 11f, 1000f, 12f, 300f }, plan, "pairs [saveId, lost]");
            Assert.Equal(1300, total);
            Assert.Equal(1300, InventoryLossMath.TotalOf(plan));
            Assert.Equal("1 300", ShockFormat.Count(total, true), "the journal: 'Списано 1 300 шт.'");

            var book = new FakeStockBook().With(11, 10000).With(12, 3000);
            int missing;
            int removed = InventoryLossMath.ApplyPlan(plan, book, out missing);

            Assert.Equal(9000, book.Stock[11]);
            Assert.Equal(2700, book.Stock[12]);
            Assert.Equal(1300, removed);
            Assert.Equal(0, missing);
        }

        [Test]
        public void Ac73_TheNumbersAreTheOnesOfTheResolve_NotRecomputedAtApply()
        {
            // D1: the plan is made once; a warehouse that grew in between loses what the plan says, not a share of the new number.
            float[] plan = InventoryLossMath.Plan(new List<StockCandidate> { Cpu(11, 10000) }, 0.1f, out _);

            var book = new FakeStockBook().With(11, 50000);
            int missing;
            InventoryLossMath.ApplyPlan(plan, book, out missing);

            Assert.Equal(49000, book.Stock[11]);
        }

        // ---- the share of one warehouse ----

        [Test]
        public void LostUnits_IsTheFloorOfStockTimesFraction()
        {
            Assert.Equal(9, InventoryLossMath.LostUnits(99, 0.1f), "9.9 -> 9");
            Assert.Equal(1000, InventoryLossMath.LostUnits(10000, 0.1f));
            Assert.Equal(300, InventoryLossMath.LostUnits(3000, 0.1f));
            Assert.Equal(0, InventoryLossMath.LostUnits(5, 0.1f), "0.5 -> 0: a small warehouse loses nothing");
            Assert.Equal(1, InventoryLossMath.LostUnits(3, 0.5f), "1.5 -> 1");
        }

        [Test]
        public void LostUnits_ForgivesTheCrumbOfFloatArithmetic()
        {
            // 0.08f is a hair under 0.08: 100 * 0.08f = 7.9999998; the owner of the content meant 8.
            Assert.Equal(8, InventoryLossMath.LostUnits(100, 0.08f));
            Assert.Equal(15, InventoryLossMath.LostUnits(100, 0.15f));
            Assert.Equal(150000, InventoryLossMath.LostUnits(1000000, 0.15f));
            Assert.Equal(80000, InventoryLossMath.LostUnits(1000000, 0.08f));
        }

        [Test]
        public void LostUnits_NeverMoreThanTheStock_NeverNegative_NeverFromNothing()
        {
            Assert.Equal(0, InventoryLossMath.LostUnits(0, 0.5f));
            Assert.Equal(0, InventoryLossMath.LostUnits(-100, 0.5f));
            Assert.Equal(0, InventoryLossMath.LostUnits(1000, 0f));
            Assert.Equal(0, InventoryLossMath.LostUnits(1000, -0.2f));
            Assert.Equal(0, InventoryLossMath.LostUnits(1000, float.NaN));
            Assert.Equal(1000, InventoryLossMath.LostUnits(1000, 7f), "a share above 1 takes everything and no more");
            Assert.Equal(InventoryLossMath.MaxLostPerCpu, InventoryLossMath.LostUnits(int.MaxValue, 0.5f), "a float keeps whole numbers up to 2^24");
        }

        // ---- who takes part ----

        [Test]
        public void Plan_VariantsRetiredAndOtherMakers_AreLeftOut()
        {
            var cpus = new List<StockCandidate>
            {
                Cpu(1, 1000),
                Cpu(2, 1000, variant: true),
                Cpu(3, 1000, retired: true),
                Cpu(4, 1000, byTarget: false),
                Cpu(5, 2000),
            };

            int total;
            float[] plan = InventoryLossMath.Plan(cpus, 0.1f, out total);

            Assert.Equal(new[] { 1f, 100f, 5f, 200f }, plan, "only the CPUs 1 and 5");
            Assert.Equal(300, total);
        }

        [Test]
        public void Plan_EmptyWarehousesAndZeroLosses_ProduceNoPairs()
        {
            var cpus = new List<StockCandidate> { Cpu(1, 0), Cpu(2, 5), Cpu(3, -4), Cpu(-1, 1000) };

            int total;
            float[] plan = InventoryLossMath.Plan(cpus, 0.1f, out total);

            Assert.Equal(0, plan.Length);
            Assert.Equal(0, total);
        }

        [Test]
        public void Plan_NoCandidates_IsAnEmptyPlan_NotAnException()
        {
            int total;
            Assert.Equal(0, InventoryLossMath.Plan(null, 0.1f, out total).Length);
            Assert.Equal(0, total);
            Assert.Equal(0, InventoryLossMath.Plan(new List<StockCandidate>(), 0.1f, out total).Length);
            Assert.Equal(0, InventoryLossMath.Plan(new List<StockCandidate> { Cpu(1, 1000) }, 0f, out total).Length, "a share of 0 loses nothing");
        }

        [Test]
        public void Plan_KeepsTheOrderOfTheCandidates()
        {
            var cpus = new List<StockCandidate> { Cpu(30, 1000), Cpu(10, 1000), Cpu(20, 1000) };

            float[] plan = InventoryLossMath.Plan(cpus, 0.1f, out _);

            Assert.Equal(new[] { 30f, 100f, 10f, 100f, 20f, 100f }, plan);
            Assert.Equal(new List<int> { 30, 10, 20 }, InventoryLossMath.SaveIdsOf(plan));
        }

        // ---- Apply ----

        [Test]
        public void ApplyPlan_NeverDrivesAWarehouseBelowZero()
        {
            // The plan was made when the warehouse held 1 000; by the time the player decides it holds 100.
            var book = new FakeStockBook().With(1, 100).With(2, 0);
            int missing;

            int removed = InventoryLossMath.ApplyPlan(new[] { 1f, 500f, 2f, 300f }, book, out missing);

            Assert.Equal(0, book.Stock[1], "min(lost, stock)");
            Assert.Equal(0, book.Stock[2]);
            Assert.Equal(100, removed, "only what was there is counted");
            Assert.Equal(0, missing);
            Assert.Equal(new List<int> { 1 }, book.Writes, "an empty warehouse is not written to");
        }

        [Test]
        public void ApplyPlan_ACpuThatIsNotFound_IsPassedOver_TheRestIsStillWrittenOff()
        {
            // design.md, Error Handling: CpuDataProvider.GetCpu -> null for one CPU, the others are written off.
            var book = new FakeStockBook().With(1, 1000).With(3, 2000);
            int missing;

            int removed = InventoryLossMath.ApplyPlan(new[] { 1f, 100f, 2f, 200f, 3f, 300f }, book, out missing);

            Assert.Equal(900, book.Stock[1]);
            Assert.Equal(1700, book.Stock[3]);
            Assert.False(book.Stock.ContainsKey(2), "the missing CPU was not invented");
            Assert.Equal(1, missing);
            Assert.Equal(400, removed);
        }

        [Test]
        public void ApplyPlan_DamagedPairs_AreSkipped()
        {
            var book = new FakeStockBook().With(1, 1000).With(2, 1000);
            int missing;

            // fractional id, negative loss, NaN, an odd number at the end, zero loss
            int removed = InventoryLossMath.ApplyPlan(new[] { 1.5f, 100f, 2f, -100f, float.NaN, 5f, 2f, 0f, 1f, 50f, 7f }, book, out missing);

            Assert.Equal(950, book.Stock[1], "the one good pair");
            Assert.Equal(1000, book.Stock[2]);
            Assert.Equal(50, removed);
            Assert.Equal(0, missing);
        }

        [Test]
        public void ApplyPlan_NullPlanOrBook_DoNothing()
        {
            int missing;
            Assert.Equal(0, InventoryLossMath.ApplyPlan(null, new FakeStockBook(), out missing));
            Assert.Equal(0, InventoryLossMath.ApplyPlan(new[] { 1f, 5f }, null, out missing));
            Assert.Equal(0, InventoryLossMath.ApplyPlan(new float[0], new FakeStockBook(), out missing));
        }

        [Test]
        public void TotalOf_CountsOnlyValidPairs()
        {
            Assert.Equal(0, InventoryLossMath.TotalOf(null));
            Assert.Equal(0, InventoryLossMath.TotalOf(new float[0]));
            Assert.Equal(1300, InventoryLossMath.TotalOf(new[] { 11f, 1000f, 12f, 300f }));
            Assert.Equal(1000, InventoryLossMath.TotalOf(new[] { 11f, 1000f, 12f, -300f, 13f }), "a damaged pair and a loose number do not count");
        }

        // ---- the parameters ----

        [Test]
        public void LossSpec_FractionAsANumber_IsAnIntervalOfOnePoint()
        {
            LossSpec spec = LossOf("{'fraction':0.1}");

            Assert.Equal(0.1f, spec.FractionLo);
            Assert.Equal(0.1f, spec.FractionHi);
            Assert.True(spec.Scalable, "scalable defaults to true in this pack");
            Assert.Equal("InventoryLoss", LossSpec.EffectName);
        }

        [Test]
        public void LossSpec_FractionAsAPair_IsAnInterval()
        {
            LossSpec spec = LossOf("{'fraction':[0.08,0.15],'scalable':false}");

            Assert.Equal(0.08f, spec.FractionLo);
            Assert.Equal(0.15f, spec.FractionHi);
            Assert.False(spec.Scalable);
        }

        [Test]
        public void LossSpec_FractionRange_IsZeroToHalf()
        {
            Assert.Equal(0, LossErrors("{'fraction':0}").Count);
            Assert.Equal(0, LossErrors("{'fraction':0.5}").Count);
            Assert.Equal(0, LossErrors("{'fraction':[0,0.5]}").Count);
            Assert.Equal(1, LossErrors("{'fraction':0.51}").Count);
            Assert.Equal(1, LossErrors("{'fraction':-0.01}").Count);
            Assert.Equal(1, LossErrors("{'fraction':[0.1,0.6]}").Count);
            Assert.Equal(1, LossErrors("{'fraction':[-0.1,0.2]}").Count);
            Assert.Equal(1, LossErrors("{'fraction':[0.2,0.1]}").Count, "lo above hi");
        }

        [Test]
        public void LossSpec_BadShapes_AreReported()
        {
            List<string> missing = LossErrors("{}");
            Assert.Equal(1, missing.Count);
            Assert.True(missing[0].Contains("InventoryLoss.params.fraction is required"), missing[0]);

            Assert.Equal(1, LossErrors("{'fraction':'0.1'}").Count);
            Assert.Equal(1, LossErrors("{'fraction':[0.1]}").Count);
            Assert.Equal(1, LossErrors("{'fraction':[0.1,0.2,0.3]}").Count);
            Assert.Equal(1, LossErrors("{'fraction':['a','b']}").Count);
            Assert.Equal(1, LossErrors("{'fraction':null}").Count);
            Assert.Equal(1, LossErrors("{'fraction':{'lo':0.1}}").Count);
            Assert.Equal(1, LossErrors("{'fraction':0.1,'scalable':1}").Count);

            var errors = new List<string>();
            LossSpec spec;
            Assert.False(LossSpec.TryRead(null, errors, out spec));
            Assert.Null(spec);
            Assert.Equal(1, errors.Count);
            Assert.False(LossSpec.TryRead(null, null, out spec), "the list of errors is optional");
        }

        // ---- the roll ----

        [Test]
        public void LossSpec_Roll_PicksAPointOfTheInterval()
        {
            LossSpec spec = LossOf("{'fraction':[0.08,0.15]}");

            Assert.Near(0.08, spec.Roll(0f, 1f), 1e-6);
            Assert.Near(0.115, spec.Roll(0.5f, 1f), 1e-6);
            Assert.Near(0.15, spec.Roll(1f, 1f), 1e-6);
            Assert.Near(0.08, spec.Roll(-3f, 1f), 1e-6, "a draw outside [0, 1) is held to it");
            Assert.Near(0.15, spec.Roll(3f, 1f), 1e-6);
            Assert.Near(0.08, spec.Roll(float.NaN, 1f), 1e-6);
        }

        [Test]
        public void LossSpec_Roll_IntensityScalesTheShare_NotAboveHalf()
        {
            LossSpec spec = LossOf("{'fraction':0.15}");

            Assert.Near(0.075, spec.Roll(0.3f, 0.5f), 1e-6);
            Assert.Near(0.15, spec.Roll(0.3f, 1f), 1e-6);
            Assert.Near(0.225, spec.Roll(0.3f, 1.5f), 1e-6);
            Assert.Near(0.3, spec.Roll(0.3f, 2f), 1e-6, "chaos");

            Assert.Equal(LossSpec.MaxFraction, LossOf("{'fraction':0.4}").Roll(0.3f, 2f), "0.8 would empty the depot: held to 0.5");
        }

        [Test]
        public void LossSpec_Roll_NotScalable_IgnoresTheIntensity()
        {
            LossSpec spec = LossOf("{'fraction':0.15,'scalable':false}");

            Assert.Near(0.15, spec.Roll(0.3f, 2f), 1e-6);
            Assert.Near(0.15, spec.Roll(0.3f, 0.5f), 1e-6);
        }

        [Test]
        public void LossSpec_Roll_BadIntensity_CountsAsOne_ZeroFractionLosesNothing()
        {
            Assert.Near(0.15, LossOf("{'fraction':0.15}").Roll(0.3f, float.NaN), 1e-6);
            Assert.Near(0.15, LossOf("{'fraction':0.15}").Roll(0.3f, -2f), 1e-6);
            Assert.Equal(0f, LossOf("{'fraction':0}").Roll(0.3f, 2f));
        }

        [Test]
        public void ARollAndAPlan_FromTheDesignExample_LoseBetweenEightAndFifteenPercent()
        {
            // ms.cleanroom_contamination: InventoryLoss f in [0.08; 0.15]; one CPU of 100 000 units.
            LossSpec spec = LossOf("{'fraction':[0.08,0.15]}");
            var cpus = new List<StockCandidate> { Cpu(1, 100000) };

            for (float draw = 0f; draw < 1f; draw += 0.125f)
            {
                int total;
                InventoryLossMath.Plan(cpus, spec.Roll(draw, 1f), out total);
                Assert.InRange(total, 8000, 15000, "draw " + draw);
            }
        }
    }
}
