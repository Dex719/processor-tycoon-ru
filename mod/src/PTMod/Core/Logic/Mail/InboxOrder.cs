using System;
using System.Collections.Generic;

namespace PTMod.Core.Logic.Mail
{
    /// <summary>
    /// The order of the mail list (FR-14 item 11, AC-14.10), as a pure function: newest date first; for equal dates the mail with the
    /// larger <c>EmailID</c> is higher (so a mod mail, id >= 100000, sits above a vanilla one of the same day and later mails above earlier
    /// ones); items that are equal in both keep their current relative order (the sort is stable, so two Year Summaries, id 0, never swap).
    /// The game puts every new mail button first (<c>SetAsFirstSibling</c>), so after the saved mod mails are restored they would sit above
    /// everything; <c>InboxSorter</c> (TSK-062) reads the date and id of every button, asks this class for the order and applies it with
    /// <c>SetSiblingIndex</c>. Position 0 is the top of the list. The time part of a date is ignored.
    /// </summary>
    public static class InboxOrder
    {
        /// <summary>Negative when mail A goes above mail B, positive when below, 0 when the keys are equal.</summary>
        public static int Compare(DateTime dateA, int idA, DateTime dateB, int idB)
        {
            int byDate = dateB.Date.CompareTo(dateA.Date);   // later date first
            if (byDate != 0) return byDate;
            return idB.CompareTo(idA);                       // larger id first
        }

        /// <summary>
        /// The new order: element <c>k</c> of the result is the index (in the input lists) of the mail that belongs at position <c>k</c>.
        /// Both lists have one entry per mail, in the current order of the list.
        /// </summary>
        /// <exception cref="ArgumentNullException">A list is null.</exception>
        /// <exception cref="ArgumentException">The lists differ in length.</exception>
        public static int[] Order(IReadOnlyList<DateTime> dates, IReadOnlyList<int> ids)
        {
            CheckLists(dates, ids);
            var order = new int[dates.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            if (order.Length < 2) return order;

            Array.Sort(order, delegate (int a, int b)
            {
                int c = Compare(dates[a], ids[a], dates[b], ids[b]);
                return c != 0 ? c : a.CompareTo(b);   // stable: the current order breaks ties
            });
            return order;
        }

        /// <summary>
        /// The inverse of <see cref="Order"/>: element <c>i</c> of the result is the position that mail <c>i</c> (in the input lists) gets,
        /// ready for <c>SetSiblingIndex</c>.
        /// </summary>
        public static int[] Positions(IReadOnlyList<DateTime> dates, IReadOnlyList<int> ids)
        {
            int[] order = Order(dates, ids);
            var positions = new int[order.Length];
            for (int position = 0; position < order.Length; position++) positions[order[position]] = position;
            return positions;
        }

        /// <summary>True when the list is already in the right order (the sorter then skips the work and leaves the list alone).</summary>
        public static bool IsOrdered(IReadOnlyList<DateTime> dates, IReadOnlyList<int> ids)
        {
            CheckLists(dates, ids);
            for (int i = 1; i < dates.Count; i++)
            {
                if (Compare(dates[i - 1], ids[i - 1], dates[i], ids[i]) > 0) return false;
            }
            return true;
        }

        private static void CheckLists(IReadOnlyList<DateTime> dates, IReadOnlyList<int> ids)
        {
            if (dates == null) throw new ArgumentNullException("dates");
            if (ids == null) throw new ArgumentNullException("ids");
            if (dates.Count != ids.Count) throw new ArgumentException("dates and ids must have the same length");
        }
    }
}
