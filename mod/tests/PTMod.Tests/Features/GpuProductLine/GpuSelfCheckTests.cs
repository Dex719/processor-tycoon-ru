using System.Collections.Generic;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The book of the self-check (TSK-011, NFR-2): the line of the log <c>self-check OK (patches 2/2, fields 6/6, calls 12/12, ports 3/3)</c>, the abilities a
    /// missing thing switches off (without <c>Factory.totalAllocatedLines</c> only <see cref="GpuCapability.Production"/>), the comparison of a port with the game,
    /// and the three sets of numbers of the ports (the first is the reference of the CODEMAP, DPW(300, 300, 1.0) = 197).
    /// </summary>
    [Covers(typeof(GpuSelfCheckBook), typeof(GpuSelfCheckSamples), typeof(GpuCapability))]
    public class GpuSelfCheckTests
    {
        private static GpuSelfCheckBook Complete()
        {
            var book = new GpuSelfCheckBook();
            book.Patch("GP1", true, true, GpuCapability.ProductionUi);
            book.Patch("GP2", true, true, GpuCapability.SharedLearning);
            for (int i = 0; i < 6; i++) book.Field("field" + i, true, GpuCapability.Production);
            for (int i = 0; i < 12; i++) book.Call("call" + i, true, null);
            for (int i = 0; i < 3; i++) book.Port("port" + i, new[] { 1.0, 2.0, 3.0 }, new[] { 1.0, 2.0, 3.0 });
            return book;
        }

        [Test]
        public void EverythingFound_GivesTheLineOfTheTask()
        {
            GpuSelfCheckBook book = Complete();
            Assert.True(book.Ok);
            Assert.Equal("self-check OK (patches 2/2, fields 6/6, calls 12/12, ports 3/3)", book.Summary());
            Assert.Equal(0, book.Problems.Count);
            foreach (GpuCapability capability in new[] { GpuCapability.Production, GpuCapability.ProductionUi, GpuCapability.SharedLearning })
                Assert.True(book.IsAvailable(capability), capability.ToString());
        }

        [Test]
        public void WithoutTotalAllocatedLines_OnlyProductionIsOff()
        {
            var book = new GpuSelfCheckBook();
            book.Patch("GP1", true, true, GpuCapability.ProductionUi);
            book.Patch("GP2", true, true, GpuCapability.SharedLearning);
            book.Field("Factory.totalAllocatedLines", false, GpuCapability.Production);
            for (int i = 0; i < 5; i++) book.Field("field" + i, true, GpuCapability.ProductionUi);

            Assert.False(book.IsAvailable(GpuCapability.Production));
            Assert.True(book.IsAvailable(GpuCapability.ProductionUi));
            Assert.True(book.IsAvailable(GpuCapability.SharedLearning));
            Assert.False(book.Ok);
            Assert.Equal(5, book.FieldsFound);
            Assert.True(book.Summary().StartsWith("self-check: ", System.StringComparison.Ordinal), book.Summary());
            Assert.True(book.Summary().Contains("fields 5/6"), book.Summary());
            Assert.True(book.Problems[0].Contains("Factory.totalAllocatedLines"), book.Problems[0]);
        }

        [Test]
        public void AMissingPatch_SwitchesOffItsAbility()
        {
            var book = new GpuSelfCheckBook();
            book.Patch("GP1", true, false, GpuCapability.ProductionUi);
            book.Patch("GP2", true, true, GpuCapability.SharedLearning);
            Assert.False(book.IsAvailable(GpuCapability.ProductionUi));
            Assert.True(book.IsAvailable(GpuCapability.Production), "the production itself goes on");
            Assert.Equal(1, book.PatchesApplied);
            Assert.Equal(2, book.PatchesDeclared);
            Assert.Equal(1, book.Problems.Count, "a declared patch that failed is a problem");
        }

        [Test]
        public void APatchNotDeclaredYet_SwitchesOffItsAbility_Quietly()
        {
            // stage 1 before TSK-021 and TSK-041: neither GP1 nor GP2 exists; the count of the patches is of the declared ones
            var book = new GpuSelfCheckBook();
            book.Patch("GP1", false, false, GpuCapability.ProductionUi);
            book.Patch("GP2", false, false, GpuCapability.SharedLearning);
            for (int i = 0; i < 6; i++) book.Field("field" + i, true, GpuCapability.Production);
            for (int i = 0; i < 12; i++) book.Call("call" + i, true, null);
            for (int i = 0; i < 3; i++) book.Port("port" + i, new[] { 1.0 }, new[] { 1.0 });

            Assert.False(book.IsAvailable(GpuCapability.ProductionUi));
            Assert.False(book.IsAvailable(GpuCapability.SharedLearning));
            Assert.True(book.IsAvailable(GpuCapability.Production));
            Assert.True(book.Ok, "nothing failed");
            Assert.Equal("self-check OK (patches 0/0, fields 6/6, calls 12/12, ports 3/3)", book.Summary());
            Assert.Equal(2, GpuSelfCheckBook.AllPatches);
        }

        [Test]
        public void AMissingCall_SwitchesOffWhatItServes_OrNothing()
        {
            var book = new GpuSelfCheckBook();
            book.Call("DateHelper.DecadeTransitionValue", false, null);
            Assert.True(book.IsAvailable(GpuCapability.Production));
            book.Call("ProductionMath.DiesPerWafer", false, GpuCapability.Production);
            Assert.False(book.IsAvailable(GpuCapability.Production));
            Assert.Equal(2, book.Problems.Count);
            Assert.Equal(0, book.CallsFound);
        }

        [Test]
        public void APort_MatchesWithinTheTolerance_SetBySet()
        {
            var book = new GpuSelfCheckBook();
            Assert.True(book.Port("F10", new double[] { 197, 300, 12 }, new double[] { 197, 300, 12 }));
            Assert.True(book.Port("F11", new[] { 1234.5, 1e6, double.PositiveInfinity }, new[] { 1234.5001, 1e6 * (1 + 5e-5), double.PositiveInfinity }),
                "single precision of the game; no good dies is infinity on both sides");
            Assert.False(book.Port("F1", new[] { 100.0, 200.0, 300.0 }, new[] { 100.0, 201.0, 300.0 }));
            Assert.Equal(2, book.PortsMatched);
            Assert.True(book.PortsRan);
            Assert.True(book.Problems[0].Contains("set 2"), book.Problems[0]);
            Assert.False(book.Port("F1", new double[0], new double[0]), "nothing compared is not a match");
            Assert.False(book.Port("F1", new[] { 1.0 }, new[] { 1.0, 2.0 }), "a set missing on one side");

            Assert.True(GpuSelfCheckBook.Matches(0.0, 0.00005));
            Assert.False(GpuSelfCheckBook.Matches(0.0, 0.001));
            Assert.True(GpuSelfCheckBook.Matches(double.NaN, double.NaN));
            Assert.False(GpuSelfCheckBook.Matches(1.0, double.NaN));
            Assert.False(GpuSelfCheckBook.Matches(1.0, double.PositiveInfinity));
        }

        [Test]
        public void SkippedPorts_AreNotCountedAndSayWhy()
        {
            var book = new GpuSelfCheckBook();
            book.PortSkipped("F1/F10/F11", "the game is not ready yet");
            book.Problem("ids");
            book.Problem(null);
            Assert.False(book.PortsRan);
            Assert.Equal(0, book.PortsMatched);
            Assert.Equal(2, book.Problems.Count);
            Assert.Equal("patches 0/0, fields 0/6, calls 0/12, ports 0/3", book.Counts());
        }

        [Test]
        public void TheSamples_AreThreeSets_TheFirstIsTheReferenceOfTheCodemap()
        {
            Assert.Equal(3, GpuSelfCheckSamples.Sets.Length);
            GpuSelfCheckSamples.Sample first = GpuSelfCheckSamples.Sets[0];
            Assert.Equal(197, GpuDesignMath.DiesPerWaferPort(first.Die, first.Wafer, first.Yield), "DPW(300, 300, 1.0) = 197");

            var seen = new HashSet<string>();
            foreach (GpuSelfCheckSamples.Sample s in GpuSelfCheckSamples.Sets)
            {
                Assert.True(s.Die > 0f && s.Wafer > 0f && s.Yield > 0f && s.Yield <= 1f, "a die that fits");
                Assert.True(GpuDesignMath.DiesPerWaferPort(s.Die, s.Wafer, s.Yield) >= 1, "at least one die: the cost of a die is finite");
                Assert.True(s.FailureRate > 0f && s.FailureRate < 1f);
                Assert.True(seen.Add(s.Die + "/" + s.Wafer + "/" + s.Yield), "three different sets");
            }
        }

        [Test]
        public void TheAbilities_AreTheThreeOfTheDesign()
        {
            Assert.Equal(0, (int)GpuCapability.Production);
            Assert.Equal(1, (int)GpuCapability.ProductionUi);
            Assert.Equal(2, (int)GpuCapability.SharedLearning);
            Assert.Equal(3, System.Enum.GetValues(typeof(GpuCapability)).Length);
            Assert.True(new GpuSelfCheckBook().IsAvailable((GpuCapability)99), "an unknown ability is not switched off");
        }
    }
}
