using System.Collections.Generic;
using System.Linq;
using PTMod.Features.ResearchTreeUx;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>The reverse dependencies of the tooltip ("Opens: ...", FR-4) and of the snapshots. TSK-002.</summary>
    [Covers(typeof(UnlocksIndex))]
    public class UnlocksIndexTests
    {
        private static TechSnapshot Tech(int id, params int[] dependencies)
        {
            return new TechSnapshot { Id = id, Name = "T" + id, DependencyIds = dependencies };
        }

        [Test]
        public void Build_InvertsTheDependencies()
        {
            // 1 <- 2 <- 3, 1 <- 4, 2 <- 4
            List<TechSnapshot> techs = new List<TechSnapshot> { Tech(1), Tech(2, 1), Tech(3, 2), Tech(4, 1, 2) };
            UnlocksIndex index = UnlocksIndex.Build(techs);

            Assert.Equal(new[] { 2, 4 }, index.UnlocksOf(1).ToArray());
            Assert.Equal(new[] { 3, 4 }, index.UnlocksOf(2).ToArray());
            Assert.Equal(0, index.UnlocksOf(3).Count, "nothing needs 3");
            Assert.Equal(0, index.UnlocksOf(4).Count);
            Assert.Equal(2, index.Count, "technologies that open something: 1 and 2");
        }

        [Test]
        public void Build_ListsAreAscending_WhateverTheInputOrder()
        {
            var techs = new List<TechSnapshot> { Tech(9, 1), Tech(5, 1), Tech(7, 1), Tech(1) };
            Assert.Equal(new[] { 5, 7, 9 }, UnlocksIndex.Build(techs).UnlocksOf(1).ToArray());
        }

        [Test]
        public void Build_IgnoresSelfRepeatsAndNulls()
        {
            var techs = new List<TechSnapshot>
            {
                Tech(1, 1),          // requires itself
                Tech(2, 1, 1, 1),    // a repeated dependency
                null,
                new TechSnapshot { Id = 3, DependencyIds = null },
            };
            UnlocksIndex index = UnlocksIndex.Build(techs);
            Assert.Equal(new[] { 2 }, index.UnlocksOf(1).ToArray());
            Assert.Equal(1, index.Count);
        }

        [Test]
        public void Build_KnowsADependencyThatHasNoSnapshot()
        {
            UnlocksIndex index = UnlocksIndex.Build(new[] { Tech(20, 99) });
            Assert.Equal(new[] { 20 }, index.UnlocksOf(99).ToArray());
        }

        [Test]
        public void Build_NullOrEmpty_GivesAnEmptyIndex()
        {
            Assert.Equal(0, UnlocksIndex.Build(null).Count);
            Assert.Equal(0, UnlocksIndex.Build(new List<TechSnapshot>()).Count);
            Assert.NotNull(UnlocksIndex.Build(null).UnlocksOf(5), "never null");
        }

        [Test]
        public void ApplyTo_FillsUnlocksIds_AndClearsTheOldOnes()
        {
            var techs = new List<TechSnapshot> { Tech(1), Tech(2, 1), Tech(3, 1) };
            techs[1].UnlocksIds = new[] { 77 };   // stale data from an earlier read
            UnlocksIndex.Build(techs).ApplyTo(techs);

            Assert.Equal(new[] { 2, 3 }, techs[0].UnlocksIds);
            Assert.Equal(0, techs[1].UnlocksIds.Length, "a technology that opens nothing gets an empty list");
            Assert.Equal(0, techs[2].UnlocksIds.Length);
        }

        [Test]
        public void ApplyTo_GivesEachSnapshotItsOwnArray()
        {
            var techs = new List<TechSnapshot> { Tech(1), Tech(2, 1) };
            UnlocksIndex index = UnlocksIndex.Build(techs);
            index.ApplyTo(techs);
            techs[0].UnlocksIds[0] = 123;   // a caller changing its copy must not change the index

            Assert.Equal(new[] { 2 }, index.UnlocksOf(1).ToArray());
            index.ApplyTo(null);            // a null list is ignored
        }
    }
}
