using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Features.ResearchTreeUx;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// Matching the game's dependency lines to pairs of technologies by geometry (design D9, FR-1, NFR-2): an exact match, the 0.5 px
    /// tolerance, lines and pairs left over, ties, and the constant shift between the plane of the pairs and the plane of the lines. TSK-003.
    /// </summary>
    [Covers(typeof(LineMatcher), typeof(DependencyPair), typeof(LineGeom), typeof(LineMatchResult))]
    public class LineMatcherTests
    {
        // A pair whose middle is (mx, my): the exit point is left of the middle, the enter point right of it.
        private static DependencyPair PairAt(int dependency, int tech, float mx, float my)
        {
            return new DependencyPair(dependency, tech, mx - 40f, my - 10f, mx + 40f, my + 10f);
        }

        private static LineGeom LineAt(int index, float x, float y)
        {
            return new LineGeom(index, x, y);
        }

        // ---- exact ----

        [Test]
        public void Match_ExactPositions_LinkEveryLine()
        {
            var pairs = new[] { PairAt(1, 2, 100f, 200f), PairAt(2, 3, 300f, 200f), PairAt(1, 4, 500f, -200f) };
            var lines = new[] { LineAt(0, 100f, 200f), LineAt(1, 300f, 200f), LineAt(2, 500f, -200f) };

            IReadOnlyList<LineLink> links = LineMatcher.Match(pairs, lines);

            Assert.Equal(3, links.Count);
            Assert.Equal(new LineLink(0, 1, 2), links[0]);
            Assert.Equal(new LineLink(1, 2, 3), links[1]);
            Assert.Equal(new LineLink(2, 1, 4), links[2]);
        }

        [Test]
        public void Match_UsesTheMiddleOfExitAndEnter()
        {
            // The game puts the line in the middle of the segment from the exit of the dependency to the enter of the technology.
            var pair = new DependencyPair(7, 9, 100f, 0f, 300f, 80f);
            Assert.Near(200.0, pair.MidX, 1e-6);
            Assert.Near(40.0, pair.MidY, 1e-6);

            IReadOnlyList<LineLink> links = LineMatcher.Match(new[] { pair }, new[] { LineAt(5, 200f, 40f) });
            Assert.Equal(1, links.Count);
            Assert.Equal(new LineLink(5, 7, 9), links[0]);
        }

        [Test]
        public void Match_LinksAreOrderedByLineIndex_WhateverTheOrderOfThePairs()
        {
            var pairs = new[] { PairAt(3, 4, 900f, 0f), PairAt(1, 2, 100f, 0f), PairAt(2, 3, 500f, 0f) };
            var lines = new[] { LineAt(0, 100f, 0f), LineAt(1, 500f, 0f), LineAt(2, 900f, 0f) };

            Assert.Equal(new[] { 0, 1, 2 }, LineMatcher.Match(pairs, lines).Select(l => l.LineIndex).ToArray());
            Assert.Equal(new[] { 2, 3, 4 }, LineMatcher.Match(pairs, lines).Select(l => l.ToTechId).ToArray());
        }

        // ---- tolerance ----

        [Test]
        public void Match_WithinHalfAPixel_Matches()
        {
            var pairs = new[] { PairAt(1, 2, 100f, 100f) };
            Assert.Equal(1, LineMatcher.Match(pairs, new[] { LineAt(0, 100.4f, 100f) }).Count, "0.4 px along x");
            Assert.Equal(1, LineMatcher.Match(pairs, new[] { LineAt(0, 100f, 99.6f) }).Count, "0.4 px along y");
            Assert.Equal(1, LineMatcher.Match(pairs, new[] { LineAt(0, 100.3f, 100.3f) }).Count, "diagonal 0.42 px");
            Assert.Equal(1, LineMatcher.Match(pairs, new[] { LineAt(0, 100.5f, 100f) }).Count, "exactly the tolerance");
        }

        [Test]
        public void Match_BeyondHalfAPixel_DoesNotMatch()
        {
            var pairs = new[] { PairAt(1, 2, 100f, 100f) };
            Assert.Equal(0, LineMatcher.Match(pairs, new[] { LineAt(0, 100.6f, 100f) }).Count, "0.6 px");
            Assert.Equal(0, LineMatcher.Match(pairs, new[] { LineAt(0, 100.4f, 100.4f) }).Count, "diagonal 0.57 px is beyond a Euclidean tolerance");
            Assert.Equal(0, LineMatcher.Match(pairs, new[] { LineAt(0, 140f, 100f) }).Count);
        }

        [Test]
        public void Match_TheToleranceIsAParameter()
        {
            var pairs = new[] { PairAt(1, 2, 0f, 0f) };
            var lines = new[] { LineAt(0, 2f, 0f) };
            Assert.Equal(0, LineMatcher.Match(pairs, lines).Count);
            Assert.Equal(1, LineMatcher.Match(pairs, lines, 2.5f).Count);
            Assert.Equal(0, LineMatcher.Match(pairs, lines, 0f).Count, "zero tolerance wants the exact position");
            Assert.Equal(1, LineMatcher.Match(pairs, new[] { LineAt(0, 0f, 0f) }, 0f).Count);
            Assert.Equal(0, LineMatcher.Match(pairs, lines, -3f).Count, "a negative tolerance is treated as zero");
            Assert.Equal(0, LineMatcher.Match(pairs, lines, float.NaN).Count, "NaN is treated as zero");
            Assert.Equal(0.5f, LineMatcher.DefaultTolerance);
        }

        // ---- leftovers ----

        [Test]
        public void MatchAuto_LinesWithoutAPair_AreReported()
        {
            var pairs = new[] { PairAt(1, 2, 100f, 0f) };
            var lines = new[] { LineAt(3, 100f, 0f), LineAt(8, 777f, 777f), LineAt(5, -50f, 20f) };

            LineMatchResult result = LineMatcher.MatchAuto(pairs, lines);

            Assert.Equal(1, result.Links.Count);
            Assert.Equal(new LineLink(3, 1, 2), result.Links[0]);
            Assert.Equal(new[] { 5, 8 }, result.UnmatchedLineIndices.ToArray(), "ascending line indices");
            Assert.Equal(0, result.UnmatchedPairs.Count);
        }

        [Test]
        public void MatchAuto_PairsWithoutALine_AreReported()
        {
            var pairs = new[] { PairAt(1, 2, 100f, 0f), PairAt(2, 3, 5000f, 0f) };
            var lines = new[] { LineAt(0, 100f, 0f) };

            LineMatchResult result = LineMatcher.MatchAuto(pairs, lines);

            Assert.Equal(1, result.Links.Count);
            Assert.Equal(0, result.UnmatchedLineIndices.Count);
            Assert.Equal(1, result.UnmatchedPairs.Count);
            Assert.Equal(2, result.UnmatchedPairs[0].DependencyId);
            Assert.Equal(3, result.UnmatchedPairs[0].TechId);
        }

        [Test]
        public void Match_EmptyOrNullInputs_GiveNoLinks()
        {
            var one = new[] { PairAt(1, 2, 0f, 0f) };
            Assert.Equal(0, LineMatcher.Match(null, null).Count);
            Assert.Equal(0, LineMatcher.Match(one, new LineGeom[0]).Count);
            Assert.Equal(0, LineMatcher.Match(new DependencyPair[0], new[] { LineAt(0, 0f, 0f) }).Count);

            LineMatchResult result = LineMatcher.MatchAuto(new DependencyPair[0], new[] { LineAt(4, 1f, 1f), LineAt(2, 2f, 2f) });
            Assert.Equal(0, result.Links.Count);
            Assert.Equal(new[] { 2, 4 }, result.UnmatchedLineIndices.ToArray(), "with no pairs every line is a leftover");

            LineMatchResult nothing = LineMatcher.MatchAuto(null, null);
            Assert.Equal(0, nothing.Links.Count);
            Assert.Equal(0, nothing.UnmatchedLineIndices.Count);
            Assert.Equal(0f, nothing.ShiftX);
        }

        // ---- ties ----

        [Test]
        public void Match_ALineIsTakenByOnePairOnly_TheClosestOneWins()
        {
            // Two pairs with the middles 0.2 px and 0.1 px from one line: the closer pair gets it, the other stays without a line.
            var pairs = new[] { PairAt(1, 2, 100.2f, 0f), PairAt(3, 4, 100.1f, 0f) };
            var lines = new[] { LineAt(0, 100f, 0f) };

            IReadOnlyList<LineLink> links = LineMatcher.Match(pairs, lines);

            Assert.Equal(1, links.Count);
            Assert.Equal(new LineLink(0, 3, 4), links[0]);
        }

        [Test]
        public void Match_APairTakesOneLineOnly_TheClosestOneWins()
        {
            var pairs = new[] { PairAt(1, 2, 100f, 0f) };
            var lines = new[] { LineAt(4, 100.3f, 0f), LineAt(9, 99.9f, 0f) };

            IReadOnlyList<LineLink> links = LineMatcher.Match(pairs, lines);

            Assert.Equal(1, links.Count);
            Assert.Equal(9, links[0].LineIndex);
        }

        [Test]
        public void Match_IdenticalMiddles_GoByTheOrderOfThePairsAndLines()
        {
            // Two different pairs whose middles coincide cannot be told apart by geometry: the first pair takes the first line, deterministically.
            var pairs = new[] { PairAt(1, 3, 200f, 0f), PairAt(2, 2, 200f, 0f) };
            var lines = new[] { LineAt(0, 200f, 0f), LineAt(1, 200f, 0f) };

            for (int round = 0; round < 3; round++)
            {
                IReadOnlyList<LineLink> links = LineMatcher.Match(pairs, lines);
                Assert.Equal(2, links.Count);
                Assert.Equal(new LineLink(0, 1, 3), links[0]);
                Assert.Equal(new LineLink(1, 2, 2), links[1]);
            }
        }

        [Test]
        public void Match_TheGameKeepsTheLineIndexNotThePosition()
        {
            // The index of the link is LineGeom.Index, not the position in the list (the adapter may skip a destroyed line).
            var pairs = new[] { PairAt(1, 2, 10f, 10f) };
            var lines = new[] { LineAt(40, 500f, 500f), LineAt(17, 10f, 10f) };
            Assert.Equal(17, LineMatcher.Match(pairs, lines)[0].LineIndex);
        }

        // ---- shift ----

        [Test]
        public void MatchAuto_ExactPlanes_NeedNoShift()
        {
            var pairs = new[] { PairAt(1, 2, 100f, 200f), PairAt(2, 3, 300f, 200f) };
            var lines = new[] { LineAt(0, 100f, 200f), LineAt(1, 300f, 200f) };

            LineMatchResult result = LineMatcher.MatchAuto(pairs, lines);

            Assert.Equal(2, result.Links.Count);
            Assert.Equal(0f, result.ShiftX);
            Assert.Equal(0f, result.ShiftY);
        }

        [Test]
        public void MatchAuto_FindsAConstantShiftBetweenThePlanes()
        {
            // The lines live in another coordinate system: every line is 37.25 more in x and 12 less in y than the middle of its pair.
            var rng = new Random(2024);
            var pairs = new List<DependencyPair>();
            var lines = new List<LineGeom>();
            for (int i = 0; i < 60; i++)
            {
                float x = (float)(rng.NextDouble() * 6000.0 - 1000.0);
                float y = (float)(rng.NextDouble() * 3000.0 - 1500.0);
                pairs.Add(PairAt(i, i + 100, x, y));
                lines.Add(LineAt(i, x + 37.25f, y - 12f));
            }

            LineMatchResult result = LineMatcher.MatchAuto(pairs, lines);

            Assert.Equal(60, result.Links.Count, "all lines matched after the shift");
            Assert.Near(37.25, result.ShiftX, 0.01, "the shift is what is added to the middles of the pairs");
            Assert.Near(-12.0, result.ShiftY, 0.01);
            for (int i = 0; i < 60; i++)
            {
                Assert.Equal(new LineLink(i, i, i + 100), result.Links[i]);
            }
            Assert.Equal(0, LineMatcher.Match(pairs, lines).Count, "without the shift nothing matches");
        }

        [Test]
        public void MatchAuto_ARegularGrid_StillFindsTheTrueShift()
        {
            // Chains of equally spaced technologies: many wrong shifts (by a whole step) match almost as many lines as the true one.
            var pairs = new List<DependencyPair>();
            var lines = new List<LineGeom>();
            int id = 0;
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 12; column++)
                {
                    float x = 150f + column * 250f;
                    float y = row * 200f;
                    pairs.Add(PairAt(id, id + 1000, x, y));
                    lines.Add(LineAt(id, x + 18f, y + 6.5f));
                    id++;
                }
            }

            LineMatchResult result = LineMatcher.MatchAuto(pairs, lines);

            Assert.Equal(pairs.Count, result.Links.Count);
            Assert.Near(18.0, result.ShiftX, 0.01);
            Assert.Near(6.5, result.ShiftY, 0.01);
            for (int i = 0; i < pairs.Count; i++) Assert.Equal(new LineLink(i, i, i + 1000), result.Links[i], "line " + i);
        }

        [Test]
        public void MatchAuto_APartialMatchWithoutShift_IsKept_WhenNoShiftDoesBetter()
        {
            var pairs = new[] { PairAt(1, 2, 100f, 0f), PairAt(2, 3, 400f, 0f), PairAt(3, 4, 900f, 0f) };
            var lines = new[] { LineAt(0, 100f, 0f), LineAt(1, 400f, 0f), LineAt(2, 9000f, 0f) };

            LineMatchResult result = LineMatcher.MatchAuto(pairs, lines);

            Assert.Equal(2, result.Links.Count);
            Assert.Equal(0f, result.ShiftX);
            Assert.Equal(new[] { 2 }, result.UnmatchedLineIndices.ToArray());
            Assert.Equal(1, result.UnmatchedPairs.Count);
            Assert.Equal(4, result.UnmatchedPairs[0].TechId);
        }

        [Test]
        public void MatchAuto_IsRepeatable()
        {
            var rng = new Random(7);
            var pairs = new List<DependencyPair>();
            var lines = new List<LineGeom>();
            for (int i = 0; i < 40; i++)
            {
                float x = (float)(rng.NextDouble() * 4000.0);
                float y = (float)(rng.NextDouble() * 2000.0);
                pairs.Add(PairAt(i, i + 1, x, y));
                lines.Add(LineAt(i, x + 5f, y + 5f));
            }

            LineMatchResult first = LineMatcher.MatchAuto(pairs, lines);
            LineMatchResult second = LineMatcher.MatchAuto(pairs, lines);

            Assert.Equal(first.Links.ToArray(), second.Links.ToArray());
            Assert.Equal(first.ShiftX, second.ShiftX);
            Assert.Equal(first.ShiftY, second.ShiftY);
        }

        [Test]
        public void Models_DescribeThemselves()
        {
            Assert.True(PairAt(1, 2, 0f, 0f).ToString().Contains("1 -> 2"));
            Assert.True(LineAt(3, 1f, 2f).ToString().Contains("3"));
        }
    }
}
