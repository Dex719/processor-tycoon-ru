using System.Collections.Generic;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The border between the public rows of the API (<see cref="CompareRow"/>, what other features return from
    /// <see cref="ICompareRowProvider"/>) and the pure rows of the model (<see cref="CompareLine"/>): the pure logic cannot see the API folder
    /// (<c>mod/requests/research-tree-ux.md</c>, item 1), so a row is copied across here, field by field, and a row that makes no sense is dropped.
    /// </summary>
    internal static class CompareRows
    {
        /// <summary>
        /// Copies the rows of a provider. A null row or a row without an id is dropped. The difference and the verdict of a row that has numbers are
        /// worked out by the model (<see cref="CompareLine.DeltaText"/> is left empty); a provider that gave them keeps its own.
        /// </summary>
        public static List<CompareLine> FromApi(IEnumerable<CompareRow> rows)
        {
            var result = new List<CompareLine>();
            if (rows == null) return result;

            foreach (CompareRow row in rows)
            {
                if (row == null || string.IsNullOrEmpty(row.Id)) continue;

                result.Add(new CompareLine
                {
                    Id = row.Id,
                    LabelKey = row.LabelKey ?? string.Empty,
                    ValueA = row.ValueA,
                    ValueB = row.ValueB,
                    NumA = row.NumA,
                    NumB = row.NumB,
                    Better = DirectionOf(row.Better),
                    Order = row.Order,
                    DeltaText = row.DeltaText,
                    Winner = row.Winner,
                });
            }
            return result;
        }

        /// <summary>The direction of the public enum as the model's own.</summary>
        public static CompareDirection DirectionOf(Better better)
        {
            switch (better)
            {
                case Better.Higher: return CompareDirection.Higher;
                case Better.Lower: return CompareDirection.Lower;
                default: return CompareDirection.None;
            }
        }
    }
}
