using System.Collections.Generic;
using PTMod.Features.PressReviews;

namespace PTMod.Tests.PressReviews
{
    /// <summary>The pure part of the choice of reference CPUs (FR-2 item 2, TSK-006): which of the game's best sellers count, and how the sales of the last full months are summed.</summary>
    [Covers(typeof(PeerFilter), typeof(PeerCandidate))]
    public class PeerSelectionLogicTests
    {
        private static PeerCandidate Candidate(int saveId, string company, int sold, bool retired = false)
        {
            return new PeerCandidate { SaveId = saveId, CompanyKey = company, SoldInSegment = sold, IsRetired = retired };
        }

        [Test]
        public void Select_KeepsOtherCompaniesThatAreAliveAndSelling_InTheOrderOfTheGame()
        {
            var ranked = new List<PeerCandidate>
            {
                Candidate(10, "player", 900),          // the reviewed company itself
                Candidate(11, "ai:1", 800),            // a peer
                Candidate(12, "ai:2", 0),              // sold nothing in the segment
                Candidate(13, "ai:3", 700, true),      // retired
                Candidate(14, "ai:2", 600),            // a peer
                Candidate(15, "ai:4", 500),            // a peer
            };

            List<int> chosen = PeerFilter.Select(ranked, "player", 99, 5);
            Assert.Equal(new List<int> { 1, 4, 5 }, chosen);
        }

        [Test]
        public void Select_TakesAtMostFive()
        {
            var ranked = new List<PeerCandidate>();
            for (int i = 0; i < 12; i++) ranked.Add(Candidate(100 + i, "ai:" + i, 1000 - i));

            List<int> chosen = PeerFilter.Select(ranked, "player", 1, 5);
            Assert.Equal(new List<int> { 0, 1, 2, 3, 4 }, chosen);
        }

        [Test]
        public void Select_SkipsTheReviewedCpuItself()
        {
            var ranked = new List<PeerCandidate> { Candidate(7, "ai:1", 500), Candidate(8, "ai:2", 400) };
            Assert.Equal(new List<int> { 1 }, PeerFilter.Select(ranked, "player", 7, 5));
        }

        [Test]
        public void Select_AnotherCpuOfTheSameCompany_IsNotAReference()
        {
            var ranked = new List<PeerCandidate> { Candidate(1, "ai:1", 500), Candidate(2, "ai:1", 400), Candidate(3, "ai:2", 300) };
            Assert.Equal(new List<int> { 2 }, PeerFilter.Select(ranked, "ai:1", 99, 5), "a company is not compared with itself");
        }

        [Test]
        public void Select_WithoutACompanyKey_DoesNotFilterByCompany()
        {
            var ranked = new List<PeerCandidate> { Candidate(1, "ai:1", 500), Candidate(2, "ai:1", 400) };
            Assert.Equal(new List<int> { 0, 1 }, PeerFilter.Select(ranked, null, 99, 5));
        }

        [Test]
        public void Select_SalesOfOneAreEnough_ButNoSalesAreNot()
        {
            var ranked = new List<PeerCandidate> { Candidate(1, "ai:1", 1), Candidate(2, "ai:2", 0), Candidate(3, "ai:3", -4) };
            Assert.Equal(new List<int> { 0 }, PeerFilter.Select(ranked, "player", 99, 5));
        }

        [Test]
        public void Select_NothingToChooseFrom()
        {
            Assert.Equal(0, PeerFilter.Select(null, "player", 1, 5).Count, "null");
            Assert.Equal(0, PeerFilter.Select(new List<PeerCandidate>(), "player", 1, 5).Count, "empty");
            Assert.Equal(0, PeerFilter.Select(new List<PeerCandidate> { Candidate(1, "ai:1", 5) }, "player", 99, 0).Count, "a limit of 0");
            Assert.Equal(0, PeerFilter.Select(new List<PeerCandidate> { Candidate(1, "ai:1", 5, true) }, "player", 99, 5).Count, "only a retired CPU");
        }

        [Test]
        public void SoldOverFullMonths_SumsTheMonthsBeforeTheCurrentOne()
        {
            // The last element is the month in progress: it is not a full month and does not count.
            Assert.Equal(60, PeerFilter.SoldOverFullMonths(new List<int> { 5, 10, 20, 30, 40 }, 3), "30 + 20 + 10");
            Assert.Equal(65, PeerFilter.SoldOverFullMonths(new List<int> { 5, 10, 20, 30, 40 }, 5), "only four full months exist: 30 + 20 + 10 + 5");
            Assert.Equal(30, PeerFilter.SoldOverFullMonths(new List<int> { 5, 10, 20, 30, 40 }, 1), "the last full month");
        }

        [Test]
        public void SoldOverFullMonths_ACpuSellingOnlyThisMonth_HasNoFullMonth()
        {
            Assert.Equal(0, PeerFilter.SoldOverFullMonths(new List<int> { 0, 0, 0, 50 }, 3));
            Assert.Equal(0, PeerFilter.SoldOverFullMonths(new List<int> { 50 }, 3), "a single month, the one in progress");
            Assert.Equal(0, PeerFilter.SoldOverFullMonths(new List<int>(), 3));
            Assert.Equal(0, PeerFilter.SoldOverFullMonths(null, 3));
            Assert.Equal(0, PeerFilter.SoldOverFullMonths(new List<int> { 1, 2, 3 }, 0), "no months asked for");
        }

        [Test]
        public void SoldOverFullMonths_DoesNotOverflow()
        {
            Assert.Equal(int.MaxValue, PeerFilter.SoldOverFullMonths(new List<int> { int.MaxValue, int.MaxValue, int.MaxValue, 0 }, 3));
        }

        [Test]
        public void TheDecisionOfTheSelector_ACpuThatSoldOnlyInTheLastMonths_IsAPeerOnlyThroughFullMonths()
        {
            // Sales of one CPU in the segment: nothing in the full months, 80 this month. The selector sums the full months (0) and the filter drops it.
            int sold = PeerFilter.SoldOverFullMonths(new List<int> { 0, 0, 0, 0, 80 }, 3);
            var ranked = new List<PeerCandidate> { Candidate(1, "ai:1", sold), Candidate(2, "ai:2", PeerFilter.SoldOverFullMonths(new List<int> { 0, 40, 40, 40, 10 }, 3)) };

            Assert.Equal(new List<int> { 1 }, PeerFilter.Select(ranked, "player", 99, 5));
        }
    }
}
