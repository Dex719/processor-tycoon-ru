using System;
using System.Collections.Generic;
using PTMod.Features.ResearchTreeUx;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// FR-6 items 2, 6 and 7 on the choice of the two sides (TSK-012, TSK-013): a side is a company key and a <c>SaveID</c>, "Swap" swaps them,
    /// and a side whose company went bankrupt (AC-6.3) or whose processor the game dropped is reset to "none" without an exception.
    /// </summary>
    [Covers(typeof(CompareSelection), typeof(CpuRef))]
    public class CompareSelectionTests
    {
        private static bool Always(string key)
        {
            return true;
        }

        // ---- the sides ----

        [Test]
        public void ANewSelection_HasNoSides()
        {
            var selection = new CompareSelection();

            Assert.False(selection.A.IsSet);
            Assert.False(selection.B.IsSet);
            Assert.Equal(CpuRef.None, selection.A);
        }

        [Test]
        public void ASide_IsACompanyKeyAndASaveId()
        {
            var selection = new CompareSelection();

            selection.SetA("player", 7);
            selection.SetB("ai:3", 12);

            Assert.True(selection.A.IsSet);
            Assert.Equal("player", selection.A.CompanyKey);
            Assert.Equal(7, selection.A.SaveId);
            Assert.Equal(new CpuRef("ai:3", 12), selection.B);
        }

        [Test]
        public void ASideWithoutACompanyOrWithANegativeId_IsNone()
        {
            var selection = new CompareSelection();

            selection.SetA(null, 5);
            selection.SetB("player", -1);

            Assert.False(selection.A.IsSet);
            Assert.False(selection.B.IsSet);
            Assert.Equal(CpuRef.None, selection.B, "all the invalid ones are the same none");
        }

        [Test]
        public void ClearingASide_LeavesTheOtherAlone()
        {
            var selection = new CompareSelection();
            selection.SetA("player", 1);
            selection.SetB("ai:1", 2);

            selection.ClearB();

            Assert.True(selection.A.IsSet);
            Assert.False(selection.B.IsSet);

            selection.ClearA();
            Assert.False(selection.A.IsSet);
        }

        [Test]
        public void ARefIsComparedByValue()
        {
            Assert.True(new CpuRef("player", 3).Equals(new CpuRef("player", 3)));
            Assert.False(new CpuRef("player", 3).Equals(new CpuRef("player", 4)));
            Assert.False(new CpuRef("player", 3).Equals(new CpuRef("ai:1", 3)));
            Assert.False(new CpuRef("player", 3).Equals("player"));
            Assert.Equal(new CpuRef("player", 3).GetHashCode(), new CpuRef("player", 3).GetHashCode());
            Assert.True(new CpuRef().Equals(default(CpuRef)));
        }

        // ---- swap (item 6) ----

        [Test]
        public void Swap_ExchangesTheSides()
        {
            var selection = new CompareSelection();
            selection.SetA("player", 1);
            selection.SetB("ai:4", 9);

            selection.Swap();

            Assert.Equal(new CpuRef("ai:4", 9), selection.A);
            Assert.Equal(new CpuRef("player", 1), selection.B);

            selection.Swap();
            Assert.Equal(new CpuRef("player", 1), selection.A, "twice is the start again");
        }

        [Test]
        public void Swap_WithOneSideEmpty_MovesTheOtherAcross()
        {
            var selection = new CompareSelection();
            selection.SetA("player", 1);

            selection.Swap();

            Assert.False(selection.A.IsSet);
            Assert.Equal(new CpuRef("player", 1), selection.B);
        }

        // ---- a side that stopped existing (item 7, AC-6.3) ----

        [Test]
        public void ACompanyThatWentBankrupt_ResetsItsSide_AndOnlyThat()
        {
            // AC-6.3: B is a processor of a company that went bankrupt the next day; the window is opened again.
            var selection = new CompareSelection();
            selection.SetA("player", 1);
            selection.SetB("ai:5", 40);

            bool reset = selection.Validate(key => key != "ai:5", (key, id) => true);

            Assert.True(reset);
            Assert.False(selection.B.IsSet, "B is back to 'none'");
            Assert.Equal(new CpuRef("player", 1), selection.A, "A stays");
        }

        [Test]
        public void AProcessorTheGameDropped_ResetsItsSide()
        {
            var selection = new CompareSelection();
            selection.SetA("player", 1);
            selection.SetB("ai:5", 40);

            bool reset = selection.Validate(Always, (key, id) => !(key == "player" && id == 1));

            Assert.True(reset);
            Assert.False(selection.A.IsSet);
            Assert.True(selection.B.IsSet);
        }

        [Test]
        public void WhenEverythingStillExists_NothingIsReset()
        {
            var selection = new CompareSelection();
            selection.SetA("player", 1);
            selection.SetB("ai:5", 40);

            Assert.False(selection.Validate(Always, (key, id) => true));
            Assert.True(selection.A.IsSet && selection.B.IsSet);
        }

        [Test]
        public void ASideThatIsNone_NeverAsksTheGame()
        {
            var selection = new CompareSelection();
            int asked = 0;

            selection.Validate(key => { asked++; return false; }, (key, id) => { asked++; return false; });

            Assert.Equal(0, asked);
        }

        [Test]
        public void ACheckThatThrows_CountsAsGone_AndTheExceptionDoesNotLeave()
        {
            var selection = new CompareSelection();
            selection.SetA("player", 1);
            selection.SetB("ai:5", 40);

            bool reset = selection.Validate(key => { if (key == "ai:5") throw new InvalidOperationException("the company object is gone"); return true; },
                (key, id) => true);

            Assert.True(reset);
            Assert.True(selection.A.IsSet);
            Assert.False(selection.B.IsSet);
        }

        [Test]
        public void WithoutChecks_EverythingCountsAsExisting()
        {
            var selection = new CompareSelection();
            selection.SetA("player", 1);

            Assert.False(selection.Validate(null, null));
            Assert.True(selection.A.IsSet);
        }

        [Test]
        public void ACompanyThatIsGone_IsNotAskedAboutItsProcessor()
        {
            var selection = new CompareSelection();
            selection.SetB("ai:5", 40);
            var askedCpu = new List<string>();

            selection.Validate(key => false, (key, id) => { askedCpu.Add(key); return true; });

            Assert.Equal(0, askedCpu.Count, "a dead company has no list to look in");
            Assert.False(selection.B.IsSet);
        }
    }
}
