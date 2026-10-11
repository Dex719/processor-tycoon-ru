using System;
using System.Collections.Generic;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// A dependency of the game as the matcher needs it: <see cref="DependencyId"/> is required by <see cref="TechId"/>, and the line the
    /// game draws for the pair runs from the exit point of the dependency to the enter point of the technology (the game places its middle at
    /// the half of the way: <c>ResearchTreeWindow.CreateDependencyLines</c>). The points are in any one plane; all the pairs and all the
    /// lines of a call must use the same one.
    /// </summary>
    public readonly struct DependencyPair
    {
        public readonly int DependencyId;
        public readonly int TechId;
        public readonly float ExitX;
        public readonly float ExitY;
        public readonly float EnterX;
        public readonly float EnterY;

        public DependencyPair(int dependencyId, int techId, float exitX, float exitY, float enterX, float enterY)
        {
            DependencyId = dependencyId;
            TechId = techId;
            ExitX = exitX;
            ExitY = exitY;
            EnterX = enterX;
            EnterY = enterY;
        }

        /// <summary>The x of the middle of the segment where the game puts the line.</summary>
        public float MidX
        {
            get { return (ExitX + EnterX) * 0.5f; }
        }

        /// <summary>The y of the middle of the segment where the game puts the line.</summary>
        public float MidY
        {
            get { return (ExitY + EnterY) * 0.5f; }
        }

        public override string ToString()
        {
            return "pair " + DependencyId + " -> " + TechId;
        }
    }

    /// <summary>One dependency line of the game: its index in <c>dependencyLines</c> and the position of its middle (the game puts the middle of the segment at the position of the line).</summary>
    public readonly struct LineGeom
    {
        public readonly int Index;
        public readonly float X;
        public readonly float Y;

        public LineGeom(int index, float x, float y)
        {
            Index = index;
            X = x;
            Y = y;
        }

        public override string ToString()
        {
            return "line " + Index + " at (" + X + "; " + Y + ")";
        }
    }

    /// <summary>What <see cref="LineMatcher.MatchAuto"/> found: the links, what is left over and the shift between the two planes.</summary>
    public sealed class LineMatchResult
    {
        private static readonly IReadOnlyList<LineLink> NoLinks = new LineLink[0];
        private static readonly IReadOnlyList<int> NoIndices = new int[0];
        private static readonly IReadOnlyList<DependencyPair> NoPairs = new DependencyPair[0];

        internal LineMatchResult(IReadOnlyList<LineLink> links, IReadOnlyList<int> unmatchedLines, IReadOnlyList<DependencyPair> unmatchedPairs,
            float shiftX, float shiftY)
        {
            Links = links ?? NoLinks;
            UnmatchedLineIndices = unmatchedLines ?? NoIndices;
            UnmatchedPairs = unmatchedPairs ?? NoPairs;
            ShiftX = shiftX;
            ShiftY = shiftY;
        }

        /// <summary>The matched lines, ordered by the line index.</summary>
        public IReadOnlyList<LineLink> Links { get; private set; }

        /// <summary>The indices of the lines no pair was found for (they stay visible in every tab, NFR-2), ascending.</summary>
        public IReadOnlyList<int> UnmatchedLineIndices { get; private set; }

        /// <summary>The pairs no line was found for, in the order they were given.</summary>
        public IReadOnlyList<DependencyPair> UnmatchedPairs { get; private set; }

        /// <summary>
        /// The offset that was added to the middles of the pairs before they were compared with the lines: the constant difference between
        /// the plane of the pairs and the plane of the lines (0 when the planes agree).
        /// </summary>
        public float ShiftX { get; private set; }

        public float ShiftY { get; private set; }
    }

    /// <summary>
    /// Matches the dependency lines the game drew to the pairs of technologies that caused them (design: D9, Component 2): the middle of a
    /// line is the middle of its segment, so the line of a pair is the one whose position is within the tolerance of the middle of the pair.
    /// Pure geometry, no Unity: the adapter (<c>TreeIndex</c>) supplies the numbers. The game does not keep the pair of a line and the order
    /// of <c>FindObjectsOfType</c> is not guaranteed, so the geometry is the only reliable key.
    /// <para>
    /// A line is taken by one pair at most and a pair takes one line at most. When several pairs and lines are within the tolerance, the
    /// closest ones are matched first (a tie goes to the earlier pair and the earlier line), so the answer does not depend on luck.
    /// </para>
    /// </summary>
    public static class LineMatcher
    {
        /// <summary>The tolerance of design D9 in pixels.</summary>
        public const float DefaultTolerance = 0.5f;

        // How many of the most voted shifts are tried by MatchAuto (a vote may be split between two cells next to each other).
        private const int ShiftCandidates = 4;

        /// <summary>
        /// Matches the pairs to the lines whose middle is within <paramref name="tolerancePx"/> of the middle of the pair (Euclidean distance).
        /// Returns the links ordered by the line index; a pair or a line without a partner is simply absent (use <see cref="MatchAuto"/> to get
        /// the leftovers). Null or empty inputs give an empty list.
        /// </summary>
        public static IReadOnlyList<LineLink> Match(IReadOnlyList<DependencyPair> pairs, IReadOnlyList<LineGeom> lines, float tolerancePx = DefaultTolerance)
        {
            bool[] pairTaken;
            bool[] lineTaken;
            return Assign(pairs, lines, tolerancePx, 0.0, 0.0, out pairTaken, out lineTaken);
        }

        /// <summary>
        /// Like <see cref="Match"/>, but first looks for a constant shift between the plane of the pairs and the plane of the lines. The game
        /// puts each line into the coordinate system of another object (<c>linesParent</c>) and moves the exit and enter points of the
        /// buttons around after it has drawn the lines, so the adapter may only be able to give positions that differ by one fixed offset;
        /// every true pair then has the same difference to its line, and that difference is easy to see among all the differences. The shift
        /// with the most matches wins (no shift wins a tie), then the matching is done with it.
        /// </summary>
        public static LineMatchResult MatchAuto(IReadOnlyList<DependencyPair> pairs, IReadOnlyList<LineGeom> lines, float tolerancePx = DefaultTolerance)
        {
            int pairCount = pairs == null ? 0 : pairs.Count;
            int lineCount = lines == null ? 0 : lines.Count;
            if (pairCount == 0 || lineCount == 0)
            {
                return Build(pairs, lines, new LineLink[0], new bool[pairCount], new bool[lineCount], 0.0, 0.0);
            }

            double bestShiftX = 0.0;
            double bestShiftY = 0.0;
            bool[] bestPairTaken;
            bool[] bestLineTaken;
            IReadOnlyList<LineLink> best = Assign(pairs, lines, tolerancePx, 0.0, 0.0, out bestPairTaken, out bestLineTaken);

            if (best.Count < pairCount || best.Count < lineCount)
            {
                foreach (double[] candidate in ShiftsByVotes(pairs, lines, tolerancePx))
                {
                    bool[] pairTaken;
                    bool[] lineTaken;
                    IReadOnlyList<LineLink> links = Assign(pairs, lines, tolerancePx, candidate[0], candidate[1], out pairTaken, out lineTaken);
                    if (links.Count > best.Count)
                    {
                        best = links;
                        bestShiftX = candidate[0];
                        bestShiftY = candidate[1];
                        bestPairTaken = pairTaken;
                        bestLineTaken = lineTaken;
                    }
                }
            }

            return Build(pairs, lines, best, bestPairTaken, bestLineTaken, bestShiftX, bestShiftY);
        }

        private static LineMatchResult Build(IReadOnlyList<DependencyPair> pairs, IReadOnlyList<LineGeom> lines, IReadOnlyList<LineLink> links,
            bool[] pairTaken, bool[] lineTaken, double shiftX, double shiftY)
        {
            var unmatchedLines = new List<int>();
            for (int i = 0; i < lineTaken.Length; i++)
            {
                if (!lineTaken[i]) unmatchedLines.Add(lines[i].Index);
            }
            unmatchedLines.Sort();

            var unmatchedPairs = new List<DependencyPair>();
            for (int i = 0; i < pairTaken.Length; i++)
            {
                if (!pairTaken[i]) unmatchedPairs.Add(pairs[i]);
            }

            return new LineMatchResult(links, unmatchedLines, unmatchedPairs, (float)shiftX, (float)shiftY);
        }

        // The matching itself: every pair/line couple within the tolerance is a candidate; the closest couples are taken first.
        private static IReadOnlyList<LineLink> Assign(IReadOnlyList<DependencyPair> pairs, IReadOnlyList<LineGeom> lines, float tolerancePx,
            double shiftX, double shiftY, out bool[] pairTaken, out bool[] lineTaken)
        {
            int pairCount = pairs == null ? 0 : pairs.Count;
            int lineCount = lines == null ? 0 : lines.Count;
            pairTaken = new bool[pairCount];
            lineTaken = new bool[lineCount];
            var links = new List<LineLink>();
            if (pairCount == 0 || lineCount == 0) return links;

            double tolerance = double.IsNaN(tolerancePx) || tolerancePx < 0f ? 0.0 : tolerancePx;
            double toleranceSquared = tolerance * tolerance;

            var candidates = new List<Candidate>();
            for (int p = 0; p < pairCount; p++)
            {
                double midX = pairs[p].MidX + shiftX;
                double midY = pairs[p].MidY + shiftY;
                for (int l = 0; l < lineCount; l++)
                {
                    double dx = lines[l].X - midX;
                    double dy = lines[l].Y - midY;
                    double distanceSquared = dx * dx + dy * dy;
                    if (distanceSquared <= toleranceSquared) candidates.Add(new Candidate(p, l, distanceSquared));
                }
            }

            candidates.Sort(CompareCandidates);
            foreach (Candidate candidate in candidates)
            {
                if (pairTaken[candidate.Pair] || lineTaken[candidate.Line]) continue;
                pairTaken[candidate.Pair] = true;
                lineTaken[candidate.Line] = true;
                links.Add(new LineLink(lines[candidate.Line].Index, pairs[candidate.Pair].DependencyId, pairs[candidate.Pair].TechId));
            }

            links.Sort((a, b) => a.LineIndex.CompareTo(b.LineIndex));
            return links;
        }

        private struct Candidate
        {
            public readonly int Pair;
            public readonly int Line;
            public readonly double DistanceSquared;

            public Candidate(int pair, int line, double distanceSquared)
            {
                Pair = pair;
                Line = line;
                DistanceSquared = distanceSquared;
            }
        }

        private static int CompareCandidates(Candidate a, Candidate b)
        {
            int byDistance = a.DistanceSquared.CompareTo(b.DistanceSquared);
            if (byDistance != 0) return byDistance;
            int byPair = a.Pair.CompareTo(b.Pair);
            return byPair != 0 ? byPair : a.Line.CompareTo(b.Line);
        }

        private readonly struct CellKey : IEquatable<CellKey>
        {
            private readonly long _x;
            private readonly long _y;

            public CellKey(long x, long y)
            {
                _x = x;
                _y = y;
            }

            public bool Equals(CellKey other)
            {
                return _x == other._x && _y == other._y;
            }

            public override bool Equals(object obj)
            {
                return obj is CellKey && Equals((CellKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (int)(_x * 397L) ^ (int)(_y * 31L) ^ (int)((_x >> 32) * 17L);
                }
            }
        }

        private sealed class Vote
        {
            public int Count;
            public double SumX;
            public double SumY;
        }

        // The differences "line - pair middle" of all couples, grouped into cells of the tolerance's size; the cells with the most votes are
        // the likely shifts (a true pair contributes the same difference as every other true pair). Each shift is the mean of its cell.
        private static List<double[]> ShiftsByVotes(IReadOnlyList<DependencyPair> pairs, IReadOnlyList<LineGeom> lines, float tolerancePx)
        {
            double cell = Math.Max(0.25, double.IsNaN(tolerancePx) ? 0.0 : tolerancePx);
            var votes = new Dictionary<CellKey, Vote>();
            for (int p = 0; p < pairs.Count; p++)
            {
                double midX = pairs[p].MidX;
                double midY = pairs[p].MidY;
                for (int l = 0; l < lines.Count; l++)
                {
                    double dx = lines[l].X - midX;
                    double dy = lines[l].Y - midY;
                    if (double.IsNaN(dx) || double.IsNaN(dy) || double.IsInfinity(dx) || double.IsInfinity(dy)) continue;

                    var key = new CellKey((long)Math.Floor(dx / cell), (long)Math.Floor(dy / cell));
                    Vote vote;
                    if (!votes.TryGetValue(key, out vote))
                    {
                        vote = new Vote();
                        votes[key] = vote;
                    }
                    vote.Count++;
                    vote.SumX += dx;
                    vote.SumY += dy;
                }
            }

            var ranked = new List<Vote>(votes.Values);
            ranked.Sort((a, b) =>
            {
                int byCount = b.Count.CompareTo(a.Count);
                if (byCount != 0) return byCount;
                // The same number of votes: the smaller shift first (the answer stays the same from run to run).
                double sizeA = Math.Abs(a.SumX / a.Count) + Math.Abs(a.SumY / a.Count);
                double sizeB = Math.Abs(b.SumX / b.Count) + Math.Abs(b.SumY / b.Count);
                int bySize = sizeA.CompareTo(sizeB);
                if (bySize != 0) return bySize;
                int byX = (a.SumX / a.Count).CompareTo(b.SumX / b.Count);
                return byX != 0 ? byX : (a.SumY / a.Count).CompareTo(b.SumY / b.Count);
            });

            var result = new List<double[]>();
            for (int i = 0; i < ranked.Count && i < ShiftCandidates; i++)
            {
                result.Add(new[] { ranked[i].SumX / ranked[i].Count, ranked[i].SumY / ranked[i].Count });
            }
            return result;
        }
    }
}
