using System;
using System.Collections.Generic;

namespace PTMod.Core.Logic.Mail
{
    /// <summary>One <c>SetSiblingIndex</c> call of the sort: the row (an index into the lists given to <see cref="InboxSortPlan.Compute"/>) goes to this sibling index.</summary>
    internal readonly struct InboxMove
    {
        public InboxMove(int row, int slot)
        {
            Row = row;
            Slot = slot;
        }

        public int Row { get; }

        public int Slot { get; }
    }

    /// <summary>
    /// Turns the right order of the mail list (<see cref="InboxOrder"/>) into the calls that make it so. The rows are the letter rows among the
    /// children of the list, in the order of their sibling indices; in the game the list has no other children, so the sibling indices are 0..n-1,
    /// but a gap (a foreign child) is kept: the right order is laid over the same set of places.
    /// <para>
    /// The moves are to be made one after the other, in the order given: row <c>order[k]</c> goes to the k-th place (the k-th smallest sibling index).
    /// Moving a child in Unity shifts the ones between its old and new place, so a move made in this order never disturbs the places already
    /// filled (they are all before the one being filled). The rows in front of the first one that is out of place are not touched.
    /// </para>
    /// </summary>
    internal static class InboxSortPlan
    {
        private static readonly InboxMove[] NoMoves = new InboxMove[0];

        /// <summary>
        /// The moves that put the rows in date order; empty when they are in order already (the sorter then leaves the list alone).
        /// </summary>
        /// <param name="siblings">The sibling index of each row, strictly increasing (the rows in the order of the list).</param>
        /// <param name="dates">The date of each row's letter (the time part is ignored).</param>
        /// <param name="ids">The <c>EmailID</c> of each row's letter.</param>
        /// <exception cref="ArgumentNullException">A list is null.</exception>
        /// <exception cref="ArgumentException">The lists differ in length.</exception>
        public static InboxMove[] Compute(IReadOnlyList<int> siblings, IReadOnlyList<DateTime> dates, IReadOnlyList<int> ids)
        {
            if (siblings == null) throw new ArgumentNullException("siblings");
            if (dates == null) throw new ArgumentNullException("dates");
            if (ids == null) throw new ArgumentNullException("ids");
            if (siblings.Count != dates.Count || dates.Count != ids.Count) throw new ArgumentException("siblings, dates and ids must have the same length");

            if (InboxOrder.IsOrdered(dates, ids)) return NoMoves;

            int[] order = InboxOrder.Order(dates, ids);
            int first = 0;
            while (first < order.Length && order[first] == first) first++;

            var moves = new InboxMove[order.Length - first];
            for (int k = first; k < order.Length; k++) moves[k - first] = new InboxMove(order[k], siblings[k]);
            return moves;
        }
    }
}
