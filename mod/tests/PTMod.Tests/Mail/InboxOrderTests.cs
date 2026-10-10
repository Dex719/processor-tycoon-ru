using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core.Logic.Mail;

namespace PTMod.Tests.Mail
{
    /// <summary>
    /// The order of the mail list (FR-14 item 11, AC-14.10, TSK-061): newest date first, a larger EmailID above a smaller one on the same
    /// day, equal keys keep their order; mod mails (id >= 100000) among vanilla ones (Year Summary, id 0); the inverse permutation for
    /// SetSiblingIndex; argument checks.
    /// </summary>
    [Covers(typeof(InboxOrder))]
    public class InboxOrderTests
    {
        private static DateTime D(int year, int month, int day)
        {
            return new DateTime(year, month, day);
        }

        private static int[] Order(DateTime[] dates, int[] ids)
        {
            return InboxOrder.Order(dates, ids);
        }

        // ---- Compare ----

        [Test]
        public void Compare_LaterDateIsAbove()
        {
            Assert.True(InboxOrder.Compare(D(1985, 3, 2), 1, D(1985, 3, 1), 99) < 0, "the later date goes first even with a smaller id");
            Assert.True(InboxOrder.Compare(D(1985, 3, 1), 99, D(1985, 3, 2), 1) > 0);
        }

        [Test]
        public void Compare_SameDay_LargerIdIsAbove()
        {
            Assert.True(InboxOrder.Compare(D(1985, 3, 1), 100002, D(1985, 3, 1), 100001) < 0);
            Assert.True(InboxOrder.Compare(D(1985, 3, 1), 100001, D(1985, 3, 1), 100002) > 0);
            Assert.True(InboxOrder.Compare(D(1985, 3, 1), 100001, D(1985, 3, 1), 0) < 0, "a mod mail is above a vanilla one of the same day");
        }

        [Test]
        public void Compare_EqualKeys_IsZero()
        {
            Assert.Equal(0, InboxOrder.Compare(D(1985, 3, 1), 0, D(1985, 3, 1), 0));
        }

        [Test]
        public void Compare_IgnoresTimeOfDay()
        {
            var morning = new DateTime(1985, 3, 1, 8, 0, 0);
            var evening = new DateTime(1985, 3, 1, 20, 30, 0);

            Assert.Equal(0, InboxOrder.Compare(morning, 5, evening, 5));
            Assert.True(InboxOrder.Compare(morning, 6, evening, 5) < 0, "the same date, so the id decides");
            Assert.True(InboxOrder.Compare(evening, 5, new DateTime(1985, 3, 2, 0, 0, 1), 5) > 0, "a later date wins whatever the time");
        }

        // ---- Order ----

        [Test]
        public void Ac1410_ModMailsAmongVanillaOnes_ByDate()
        {
            // As the game leaves the list after RestoreInbox: every restored mod mail was put first, vanilla Year Summaries are behind.
            DateTime[] dates = { D(1985, 6, 1), D(1980, 2, 10), D(1990, 1, 1), D(1976, 1, 1), D(1983, 1, 1) };
            int[] ids = { 100002, 100001, 0, 0, 0 };

            int[] order = Order(dates, ids);

            // 1990 Year Summary, 1985 mod, 1983 Year Summary, 1980 mod, 1976 Year Summary.
            Assert.Equal(new[] { 2, 0, 4, 1, 3 }, order);
        }

        [Test]
        public void Ac1410_ModMailsDoNotStickAtTheTop()
        {
            DateTime[] dates = { D(1980, 1, 5), D(1985, 1, 5), D(1990, 1, 1), D(1995, 1, 1) };
            int[] ids = { 100001, 100002, 0, 0 };

            int[] order = Order(dates, ids);

            Assert.Equal(new[] { 3, 2, 1, 0 }, order);
            Assert.False(InboxOrder.IsOrdered(dates, ids));
        }

        [Test]
        public void Order_SameDay_LargerIdFirst()
        {
            DateTime[] dates = { D(1985, 3, 1), D(1985, 3, 1), D(1985, 3, 1), D(1985, 3, 1) };
            int[] ids = { 0, 100001, 100003, 100002 };

            Assert.Equal(new[] { 2, 3, 1, 0 }, Order(dates, ids));
        }

        [Test]
        public void Order_EqualKeys_KeepCurrentOrder()
        {
            // Two Year Summaries on one day have id 0 both: they must never swap.
            DateTime[] dates = { D(1990, 1, 1), D(1990, 1, 1), D(1991, 1, 1), D(1990, 1, 1) };
            int[] ids = { 0, 0, 0, 0 };

            Assert.Equal(new[] { 2, 0, 1, 3 }, Order(dates, ids));
        }

        [Test]
        public void Order_EmptyAndSingle()
        {
            Assert.Equal(new int[0], Order(new DateTime[0], new int[0]));
            Assert.Equal(new[] { 0 }, Order(new[] { D(1985, 1, 1) }, new[] { 7 }));
            Assert.True(InboxOrder.IsOrdered(new DateTime[0], new int[0]));
            Assert.True(InboxOrder.IsOrdered(new[] { D(1985, 1, 1) }, new[] { 7 }));
        }

        [Test]
        public void Order_AlreadyOrdered_IsIdentity()
        {
            DateTime[] dates = { D(1990, 1, 1), D(1985, 1, 1), D(1985, 1, 1), D(1980, 1, 1) };
            int[] ids = { 0, 100005, 100004, 0 };

            Assert.True(InboxOrder.IsOrdered(dates, ids));
            Assert.Equal(new[] { 0, 1, 2, 3 }, Order(dates, ids));
        }

        [Test]
        public void Order_DoesNotChangeItsInput()
        {
            DateTime[] dates = { D(1980, 1, 1), D(1990, 1, 1) };
            int[] ids = { 1, 2 };

            Order(dates, ids);

            Assert.Equal(new[] { D(1980, 1, 1), D(1990, 1, 1) }, dates);
            Assert.Equal(new[] { 1, 2 }, ids);
        }

        [Test]
        public void Order_MatchesAStableLinqSort_OnManyItems()
        {
            // A fixed pseudo-random list with many repeated dates and ids (LINQ ordering is stable).
            int count = 400;
            var dates = new DateTime[count];
            var ids = new int[count];
            uint state = 12345;
            for (int i = 0; i < count; i++)
            {
                state = state * 1664525u + 1013904223u;
                dates[i] = D(1980, 1, 1).AddDays((state >> 16) % 20);
                state = state * 1664525u + 1013904223u;
                ids[i] = (state >> 16) % 4 == 0 ? 0 : 100000 + (int)((state >> 8) % 6);
            }

            int[] expected = Enumerable.Range(0, count)
                .OrderByDescending(i => dates[i])
                .ThenByDescending(i => ids[i])
                .ToArray();

            Assert.Equal(expected, Order(dates, ids));
        }

        // ---- Positions ----

        [Test]
        public void Positions_AreTheInverseOfOrder()
        {
            DateTime[] dates = { D(1985, 6, 1), D(1980, 2, 10), D(1990, 1, 1), D(1976, 1, 1), D(1983, 1, 1) };
            int[] ids = { 100002, 100001, 0, 0, 0 };

            int[] order = Order(dates, ids);
            int[] positions = InboxOrder.Positions(dates, ids);

            Assert.Equal(new[] { 1, 3, 0, 4, 2 }, positions);
            for (int position = 0; position < order.Length; position++)
                Assert.Equal(position, positions[order[position]]);
        }

        [Test]
        public void Positions_Empty()
        {
            Assert.Equal(new int[0], InboxOrder.Positions(new DateTime[0], new int[0]));
        }

        // ---- IsOrdered ----

        [Test]
        public void IsOrdered_DetectsEveryAdjacentInversion()
        {
            Assert.True(InboxOrder.IsOrdered(new[] { D(1990, 1, 1), D(1985, 1, 1) }, new[] { 0, 0 }));
            Assert.False(InboxOrder.IsOrdered(new[] { D(1985, 1, 1), D(1990, 1, 1) }, new[] { 0, 0 }));
            Assert.False(InboxOrder.IsOrdered(new[] { D(1985, 1, 1), D(1985, 1, 1) }, new[] { 1, 2 }), "same day, larger id must be above");
            Assert.True(InboxOrder.IsOrdered(new[] { D(1985, 1, 1), D(1985, 1, 1) }, new[] { 2, 1 }));
            Assert.True(InboxOrder.IsOrdered(new[] { D(1985, 1, 1), D(1985, 1, 1) }, new[] { 0, 0 }), "equal keys are in order");
        }

        // ---- arguments ----

        [Test]
        public void BadArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => InboxOrder.Order(null, new int[0]));
            Assert.Throws<ArgumentNullException>(() => InboxOrder.Order(new DateTime[0], null));
            Assert.Throws<ArgumentException>(() => InboxOrder.Order(new DateTime[1], new int[2]));
            Assert.Throws<ArgumentException>(() => InboxOrder.Positions(new DateTime[2], new int[1]));
            Assert.Throws<ArgumentNullException>(() => InboxOrder.IsOrdered(null, null));
            Assert.Throws<ArgumentException>(() => InboxOrder.IsOrdered(new DateTime[1], new int[0]));
        }
    }
}
