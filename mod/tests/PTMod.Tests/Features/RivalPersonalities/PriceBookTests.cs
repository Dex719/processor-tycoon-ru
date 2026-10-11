using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core;
using PTMod.Features.RivalPersonalities;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The price book (TSK-009; design.md Component 5; FR-7 item 3, FR-10 item 4, FR-13 item 6) on plain CPUs: the cut, the daily hold of the target
    /// price, the way back, the external cuts that expire after 400 days, and the daily path without allocation.
    /// </summary>
    [Covers(typeof(PriceBookCore<>), typeof(IPriceCpuAccess<>), typeof(ListPool<>))]
    public class PriceBookTests
    {
        private const int Amd = 1;
        private static readonly DateTime Day = new DateTime(1990, 5, 1);

        /// <summary>A CPU of the game as the book sees it.</summary>
        private sealed class FakeCpu
        {
            public int SaveId;
            public float Price;
            public float TargetPrice;
            public float RealUnitCost;
            public int Market;
            public bool Retired;
            public bool SellOnMarket = true;
        }

        private sealed class FakeWorld : IPriceCpuAccess<FakeCpu>
        {
            public readonly List<FakeCpu> All = new List<FakeCpu>();

            public int SaveIdOf(FakeCpu cpu) { return cpu.SaveId; }
            public float PriceOf(FakeCpu cpu) { return cpu.Price; }
            public void SetPrice(FakeCpu cpu, float price) { cpu.Price = price; }
            public float TargetPriceOf(FakeCpu cpu) { return cpu.TargetPrice; }
            public void SetTargetPrice(FakeCpu cpu, float targetPrice) { cpu.TargetPrice = targetPrice; }
            public float RealUnitCostOf(FakeCpu cpu) { return cpu.RealUnitCost; }
            public int TargetMarketOf(FakeCpu cpu) { return cpu.Market; }
            public bool IsSoldOnMarket(FakeCpu cpu) { return !cpu.Retired && cpu.SellOnMarket; }

            public FakeCpu FindCpu(int saveId)
            {
                for (int i = 0; i < All.Count; i++)
                {
                    if (All[i].SaveId == saveId) return All[i];
                }
                return null;
            }
        }

        private sealed class Rig
        {
            public readonly RivalState State = new RivalState();
            public readonly FakeWorld World = new FakeWorld();
            public readonly List<FakeCpu> Amd = new List<FakeCpu>();
            public readonly List<string> Warnings = new List<string>();
            public readonly PriceBookCore<FakeCpu> Book;

            public Rig()
            {
                Book = new PriceBookCore<FakeCpu>(() => State, World, Warnings.Add);
            }

            public FakeCpu AddAmd(int saveId, Segment segment, float price, float target, float unitCost)
            {
                var cpu = new FakeCpu { SaveId = saveId, Price = price, TargetPrice = target, RealUnitCost = unitCost, Market = (int)segment };
                World.All.Add(cpu);
                Amd.Add(cpu);
                return cpu;
            }

            public int Cut(string id, Segment segment = Segment.MidRange, float depth = 0.15f, float floor = 0.08f, bool bankrupt = false, DateTime? today = null)
            {
                return Book.ApplyCut(id, PriceBookTests.Amd, Amd, segment, depth, floor, bankrupt, today ?? Day);
            }
        }

        // ---- the cut ----

        [Test]
        public void ApplyCut_CutsTheCpusOfTheSegment_AndReturnsHowMany()
        {
            var rig = new Rig();
            FakeCpu mid = rig.AddAmd(10, Segment.MidRange, 239f, 239f, 120f);
            FakeCpu mid2 = rig.AddAmd(11, Segment.MidRange, 249f, 229f, 110f);
            FakeCpu high = rig.AddAmd(12, Segment.HighEnd, 499f, 499f, 200f);

            int affected = rig.Cut("r-1");

            Assert.Equal(2, affected);
            Assert.Near(203.15, mid.Price, 1e-3, "formula 4: 239 -> 203.15");
            Assert.Near(203.15, mid.TargetPrice, 1e-3);
            Assert.Near(211.65, mid2.Price, 1e-3, "249 x 0.85");
            Assert.Near(211.65, mid2.TargetPrice, 1e-3, "the target 229 goes down to the cut");
            Assert.Equal(499f, high.Price, "another segment is not touched");
            Assert.Equal(2, rig.State.priceBook.Count, "one entry per CPU");
            PriceBookEntry entry = rig.State.priceBook.Single(e => e.cpuSaveId == 11);
            Assert.Equal(229f, entry.baseTargetPrice, "the original target price is written down");
            Assert.Equal(Amd, entry.uid);
            Assert.Equal("r-1", entry.reactionId);
            Assert.Equal("1990-05-01", entry.startedOn);
        }

        [Test]
        public void ApplyCut_SkipsRetiredUnsoldAndUnpricedCpus()
        {
            var rig = new Rig();
            rig.AddAmd(10, Segment.MidRange, 239f, 239f, 120f).Retired = true;
            rig.AddAmd(11, Segment.MidRange, 239f, 239f, 120f).SellOnMarket = false;
            rig.AddAmd(12, Segment.MidRange, 0f, -1f, 120f);
            rig.AddAmd(13, Segment.MidRange, 200f, 200f, 199f);   // the floor 214.9 is above the price: nothing to cut

            Assert.Equal(0, rig.Cut("r-1"));
            Assert.Equal(0, rig.State.priceBook.Count);
        }

        [Test]
        public void ApplyCut_GivesZero_ForABankruptOrDormantRival()
        {
            var rig = new Rig();
            FakeCpu cpu = rig.AddAmd(10, Segment.MidRange, 239f, 239f, 120f);

            Assert.Equal(0, rig.Cut("r-1", bankrupt: true), "bankrupt");
            rig.State.dormant.Add(Amd);
            Assert.Equal(0, rig.Cut("r-2"), "dormant");
            Assert.Equal(239f, cpu.Price);
            Assert.Equal(0, rig.State.priceBook.Count);
        }

        [Test]
        public void ApplyCut_RefusesBadIdsAndSegments()
        {
            var rig = new Rig();
            rig.AddAmd(10, Segment.MidRange, 239f, 239f, 120f);

            Assert.Equal(0, rig.Cut(""));
            Assert.Equal(0, rig.Cut(null));
            Assert.Equal(0, rig.Cut(new string('x', 65)));
            Assert.Equal(0, rig.Cut("r-1", Segment.Any));
            Assert.True(rig.Warnings.Count >= 4, "each refusal is one line for the log");
            Assert.Equal(0, rig.State.priceBook.Count);
        }

        [Test]
        public void ApplyCut_Twice_DoesNotCutAgain_ButTakesNewCpus()
        {
            var rig = new Rig();
            FakeCpu first = rig.AddAmd(10, Segment.MidRange, 239f, 239f, 120f);
            rig.Cut("r-1");
            rig.AddAmd(11, Segment.MidRange, 249f, 249f, 120f);

            Assert.Equal(2, rig.Cut("r-1"), "the cut holds both CPUs now");
            Assert.Near(203.15, first.Price, 1e-3, "the first CPU is not cut twice");
            Assert.Equal(2, rig.State.priceBook.Count);
        }

        [Test]
        public void ExternalCuts_AreLimitedTo20AtOnce()
        {
            var rig = new Rig();
            rig.AddAmd(10, Segment.MidRange, 500f, 500f, 10f);
            for (int i = 0; i < PriceBookCore<FakeCpu>.MaxExternalCuts; i++)
            {
                Assert.Equal(1, rig.Cut("test:war-" + i, depth: 0.01f, floor: 0f), "cut " + i);
            }

            Assert.Equal(20, rig.Book.ExternalCount);
            Assert.Equal(0, rig.Cut("test:war-21", depth: 0.01f, floor: 0f), "the 21st external cut is refused");
            Assert.Equal(1, rig.Cut("r-5", depth: 0.01f, floor: 0f), "the feature's own cuts are not limited");
        }

        // ---- the daily hold ----

        [Test]
        public void Clamp_HoldsTheTargetPriceNotAboveTheCut()
        {
            var rig = new Rig();
            FakeCpu cpu = rig.AddAmd(10, Segment.MidRange, 239f, 239f, 120f);
            rig.Cut("r-1");

            cpu.TargetPrice = 245f;   // the game raised it (HandleUnprofitableCpus, a shortage)
            Assert.Equal(1, rig.Book.Clamp(Amd, rig.Amd));
            Assert.Near(203.15, cpu.TargetPrice, 1e-3);

            cpu.TargetPrice = 190f;   // a lower target of the game stays
            Assert.Equal(0, rig.Book.Clamp(Amd, rig.Amd));
            Assert.Equal(190f, cpu.TargetPrice);

            cpu.Price = 260f;         // the price itself is the game's: the hold is on the target only
            rig.Book.Clamp(Amd, rig.Amd);
            Assert.Equal(260f, cpu.Price);
        }

        [Test]
        public void Clamp_DropsTheEntryOfACpuThatNoLongerExists()
        {
            var rig = new Rig();
            FakeCpu cpu = rig.AddAmd(10, Segment.MidRange, 239f, 239f, 120f);
            rig.AddAmd(11, Segment.MidRange, 239f, 239f, 120f);
            rig.Cut("r-1");
            Assert.Equal(2, rig.State.priceBook.Count);

            rig.Amd.Remove(cpu);
            rig.World.All.Remove(cpu);
            rig.Book.Clamp(Amd, rig.Amd);

            Assert.Equal(1, rig.State.priceBook.Count);
            Assert.Equal(11, rig.State.priceBook[0].cpuSaveId);
        }

        [Test]
        public void Clamp_GivesTheOriginalBack_WhenTheCpuMovedToAnotherCompany()
        {
            var rig = new Rig();
            FakeCpu cpu = rig.AddAmd(10, Segment.MidRange, 239f, 239f, 120f);
            rig.Cut("r-1");

            rig.Amd.Remove(cpu);   // still in the world, no longer AMD's
            rig.Book.Clamp(Amd, rig.Amd);

            Assert.Equal(239f, cpu.TargetPrice);
            Assert.Equal(0, rig.State.priceBook.Count);
        }

        [Test]
        public void Clamp_TouchesOnlyTheGivenRival()
        {
            var rig = new Rig();
            FakeCpu cpu = rig.AddAmd(10, Segment.MidRange, 239f, 239f, 120f);
            rig.Cut("r-1");
            cpu.TargetPrice = 250f;

            Assert.Equal(0, rig.Book.Clamp(7, new List<FakeCpu>()));
            Assert.Equal(250f, cpu.TargetPrice);
            Assert.Equal(1, rig.State.priceBook.Count, "the entry of another rival stays");
        }

        [Test]
        public void Clamp_AllocatesNothing()
        {
            var rig = new Rig();
            for (int i = 0; i < 20; i++) rig.AddAmd(100 + i, (Segment)(i % 4), 300f, 300f, 100f);
            rig.Cut("r-1");
            rig.Cut("r-2", Segment.HighEnd);
            Assert.True(rig.State.priceBook.Count >= 10);

            Action day = () =>
            {
                for (int n = 0; n < 1000; n++)
                {
                    rig.Amd[0].TargetPrice = 300f;
                    rig.Book.Clamp(Amd, rig.Amd);
                }
            };
            day();
            long before = GC.GetAllocatedBytesForCurrentThread();
            day();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            // One object per call would be 24 KB and more for the 1000 calls; the counter of the runtime may show a few bytes of its own.
            Assert.True(allocated < 1024, "the daily hold allocates: " + allocated + " bytes for 1000 calls");
        }

        // ---- the way back ----

        [Test]
        public void Restore_GivesTheOriginalTargetPriceBack_AndDropsTheEntries()
        {
            var rig = new Rig();
            FakeCpu cpu = rig.AddAmd(10, Segment.MidRange, 249f, 229f, 120f);
            rig.Cut("r-1");
            Assert.True(cpu.TargetPrice < 229f);

            Assert.Equal(1, rig.Book.Restore("r-1"));

            Assert.Equal(229f, cpu.TargetPrice, "the target price before the cut");
            Assert.Equal(0, rig.State.priceBook.Count);
            Assert.Equal(0, rig.Book.Restore("r-1"), "a second restore does nothing");
            Assert.Equal(0, rig.Book.Restore("never-applied"), "an unknown id does nothing");
        }

        [Test]
        public void Restore_DropsTheEntryOfAMissingCpu()
        {
            var rig = new Rig();
            FakeCpu cpu = rig.AddAmd(10, Segment.MidRange, 239f, 239f, 120f);
            rig.Cut("r-1");
            rig.World.All.Remove(cpu);
            rig.Amd.Remove(cpu);

            Assert.Equal(0, rig.Book.Restore("r-1"), "no CPU got a price back");
            Assert.Equal(0, rig.State.priceBook.Count, "the entry is gone anyway");
        }

        [Test]
        public void TwoCuts_OnOneCpu_EndAtThePriceBeforeBoth()
        {
            var rig = new Rig();
            FakeCpu cpu = rig.AddAmd(10, Segment.MidRange, 300f, 300f, 100f);

            rig.Cut("r-1", depth: 0.10f);          // 270
            rig.Cut("hist:war", depth: 0.20f);    // 270 x 0.8 = 216
            Assert.Near(216.0, cpu.TargetPrice, 1e-3);
            Assert.True(rig.State.priceBook.All(e => e.baseTargetPrice == 300f), "both remember the original price");

            rig.Book.Restore("hist:war");
            Assert.Near(270.0, cpu.TargetPrice, 1e-3, "the cut that is left still holds");

            rig.Book.Restore("r-1");
            Assert.Equal(300f, cpu.TargetPrice, "the original");
        }

        [Test]
        public void RestoreRival_EndsEveryCutOfTheRival()
        {
            var rig = new Rig();
            FakeCpu cpu = rig.AddAmd(10, Segment.MidRange, 300f, 300f, 100f);
            rig.Cut("r-1");
            rig.Cut("hist:war", depth: 0.2f);

            Assert.Equal(2, rig.Book.RestoreRival(Amd));
            Assert.Equal(300f, cpu.TargetPrice);
            Assert.Equal(0, rig.State.priceBook.Count);
        }

        [Test]
        public void ReleaseAll_GivesThePricesBack_AndKeepsTheEntries()
        {
            // FR-10 item 4: the feature is switched off; the section stays so that the cut goes on when it is switched on again
            var rig = new Rig();
            FakeCpu cpu = rig.AddAmd(10, Segment.MidRange, 300f, 300f, 100f);
            rig.Cut("r-1");
            rig.Cut("hist:war", depth: 0.2f);

            Assert.Equal(1, rig.Book.ReleaseAll(), "once per CPU");
            Assert.Equal(300f, cpu.TargetPrice);
            Assert.Equal(2, rig.State.priceBook.Count);

            rig.Book.Clamp(Amd, rig.Amd);
            Assert.True(cpu.TargetPrice < 300f, "switched on again: the hold goes on");
        }

        // ---- external cuts ----

        [Test]
        public void ExpireExternal_EndsExternalCutsAfter400Days_AndLeavesTheFeaturesOwn()
        {
            var rig = new Rig();
            FakeCpu cpu = rig.AddAmd(10, Segment.MidRange, 300f, 300f, 100f);
            FakeCpu other = rig.AddAmd(11, Segment.LowEnd, 150f, 150f, 50f);
            rig.Cut("hist:price-war-1985", today: Day);
            rig.Cut("r-3", Segment.LowEnd, today: Day);

            Assert.Equal(0, rig.Book.ExpireExternal(Day.AddDays(399), 400).Count, "day 399");
            Assert.True(rig.Book.HasCut("hist:price-war-1985"));

            List<string> expired = rig.Book.ExpireExternal(Day.AddDays(400), 400);
            Assert.Equal(new[] { "hist:price-war-1985" }, expired.ToArray());
            Assert.Equal(300f, cpu.TargetPrice, "the external cut is taken back");
            Assert.False(rig.Book.HasCut("hist:price-war-1985"));
            Assert.True(rig.Book.HasCut("r-3"), "r-<n> is ended by the event system, not here");
            Assert.True(other.TargetPrice < 150f);
        }

        [Test]
        public void ExpireExternal_StartsCountingAnEntryWithoutADate()
        {
            var rig = new Rig();
            rig.AddAmd(10, Segment.MidRange, 300f, 300f, 100f);
            rig.Cut("hist:war", today: default(DateTime));
            Assert.Equal(string.Empty, rig.State.priceBook[0].startedOn);

            Assert.Equal(0, rig.Book.ExpireExternal(Day, 400).Count);
            Assert.Equal("1990-05-01", rig.State.priceBook[0].startedOn, "counted from the first monthly pass");
            Assert.Equal(1, rig.Book.ExpireExternal(Day.AddDays(400), 400).Count);
        }

        [Test]
        public void TheBook_LivesInTheSection_AndFollowsAReplacedState()
        {
            RivalState state = new RivalState();
            var world = new FakeWorld();
            var cpus = new List<FakeCpu> { new FakeCpu { SaveId = 1, Price = 200f, TargetPrice = 200f, RealUnitCost = 50f, Market = 1 } };
            world.All.AddRange(cpus);
            var book = new PriceBookCore<FakeCpu>(() => state, world);

            book.ApplyCut("r-1", Amd, cpus, Segment.MidRange, 0.1f, 0.08f, false, Day);
            Assert.Equal(1, state.priceBook.Count);

            state = new RivalState();   // a load replaced the section
            Assert.False(book.HasCut("r-1"));
            Assert.Equal(0, book.Clamp(Amd, cpus));
        }

        // ---- the pool of the market patch ----

        [Test]
        public void ListPool_ReusesItsLists()
        {
            var pool = new ListPool<FakeCpu>(4);
            List<FakeCpu> a = pool.Rent();
            a.Add(new FakeCpu());
            pool.Return(a);
            Assert.Equal(0, a.Count, "a returned list is cleared");

            List<FakeCpu> b = pool.Rent();
            Assert.True(ReferenceEquals(a, b), "the same list again");
            List<FakeCpu> c = pool.Rent();
            Assert.False(ReferenceEquals(b, c), "a nested rent gets another list");
            pool.Return(c);
            pool.Return(b);
            pool.Return(b);
            Assert.Equal(2, pool.FreeCount, "a list given back twice is kept once");
            pool.Return(null);

            for (int i = 0; i < 100; i++)
            {
                List<FakeCpu> day = pool.Rent();
                pool.Return(day);
            }
            Assert.Equal(2, pool.Created, "the daily path makes no new list");
        }
    }
}
