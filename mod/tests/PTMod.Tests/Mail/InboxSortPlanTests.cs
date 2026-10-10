using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core.Logic.Mail;

namespace PTMod.Tests.Mail
{
    /// <summary>
    /// The moves that put the mail list in date order (FR-14 item 11, AC-14.10, TSK-062): the sibling-index calls the sorter makes on the game's
    /// list. The list is simulated with the semantics of Unity's <c>Transform.SetSiblingIndex</c> (the child ends up at the index, the ones in
    /// between shift), so the tests check what the player sees: the mod letters glued on top after a load, the vanilla "Year Summary" rows (id 0),
    /// equal dates, rows already in order, foreign children among the rows.
    /// </summary>
    [Covers(typeof(InboxSortPlan))]
    public class InboxSortPlanTests
    {
        private sealed class Item
        {
            public bool Letter;
            public DateTime Date;
            public int Id;
            public string Name;
        }

        private static Item Row(string name, int year, int month, int day, int id)
        {
            return new Item { Letter = true, Name = name, Date = new DateTime(year, month, day), Id = id };
        }

        private static string Names(IEnumerable<Item> items)
        {
            return string.Join(",", items.Select(x => x.Name));
        }

        /// <summary>The child ends up at this index (clamped), as in Unity.</summary>
        private static void SetSibling(List<Item> list, Item item, int index)
        {
            list.Remove(item);
            if (index < 0) index = 0;
            if (index > list.Count) index = list.Count;
            list.Insert(index, item);
        }

        /// <summary>The sorter on a simulated list: the letter rows are read in child order, planned, and the moves made in the order given.</summary>
        private static List<Item> Sort(List<Item> children, out InboxMove[] moves)
        {
            var rows = new List<Item>();
            var siblings = new List<int>();
            for (int i = 0; i < children.Count; i++)
            {
                if (!children[i].Letter) continue;
                rows.Add(children[i]);
                siblings.Add(i);
            }

            moves = InboxSortPlan.Compute(siblings, rows.Select(r => r.Date).ToList(), rows.Select(r => r.Id).ToList());
            var list = new List<Item>(children);
            foreach (InboxMove move in moves) SetSibling(list, rows[move.Row], move.Slot);
            return list;
        }

        // ---- AC-14.10 ----

        [Test]
        public void ModLettersGluedOnTop_AreMergedByDate()
        {
            // As the game leaves the list after a load: the vanilla rows were loaded in saved order (each put first), then the mod letters
            // were restored oldest first (each put first), so both mod letters sit above everything.
            var vanilla = new[] { Row("YS1981", 1981, 1, 1, 0), Row("YS1983", 1983, 1, 1, 0), Row("YS1986", 1986, 1, 1, 0) };
            var mods = new[] { Row("mod1980", 1980, 6, 1, 100000), Row("mod1985", 1985, 6, 1, 100001) };
            var list = new List<Item>();
            foreach (Item v in vanilla) list.Insert(0, v);
            foreach (Item m in mods) list.Insert(0, m);
            Assert.Equal("mod1985,mod1980,YS1986,YS1983,YS1981", Names(list), "the list as the game leaves it");

            InboxMove[] moves;
            List<Item> sorted = Sort(list, out moves);

            Assert.Equal("YS1986,mod1985,YS1983,YS1981,mod1980", Names(sorted), "by date, newest on top, the mod letters among the vanilla ones");
            Assert.True(moves.Length > 0);
        }

        [Test]
        public void SameDay_TheLargerIdIsAbove()
        {
            var list = new List<Item> { Row("vanilla", 1990, 1, 1, 0), Row("mod", 1990, 1, 1, 100005) };

            InboxMove[] moves;
            Assert.Equal("mod,vanilla", Names(Sort(list, out moves)));
        }

        [Test]
        public void EqualKeys_KeepTheirOrder()
        {
            var list = new List<Item> { Row("a", 1990, 1, 1, 0), Row("b", 1990, 1, 1, 0), Row("c", 1991, 1, 1, 0) };

            InboxMove[] moves;
            Assert.Equal("c,a,b", Names(Sort(list, out moves)), "two Year Summaries of one date never swap");
        }

        // ---- no work when there is none ----

        [Test]
        public void ListInOrder_NeedsNoMoves()
        {
            var list = new List<Item> { Row("a", 1990, 1, 1, 5), Row("b", 1989, 1, 1, 0), Row("c", 1988, 1, 1, 0) };

            InboxMove[] moves;
            Sort(list, out moves);

            Assert.Equal(0, moves.Length);
            Assert.Equal(0, InboxSortPlan.Compute(new int[0], new DateTime[0], new int[0]).Length, "empty list");
            Assert.Equal(0, InboxSortPlan.Compute(new[] { 0 }, new[] { DateTime.MinValue }, new[] { 0 }).Length, "one row");
        }

        [Test]
        public void RowsInFrontThatAreInOrder_AreNotTouched()
        {
            var list = new List<Item> { Row("a", 1990, 1, 1, 0), Row("b", 1989, 1, 1, 0), Row("c", 1980, 1, 1, 0), Row("d", 1988, 1, 1, 0), Row("e", 1987, 1, 1, 0) };

            InboxMove[] moves;
            List<Item> sorted = Sort(list, out moves);

            Assert.Equal("a,b,d,e,c", Names(sorted));
            Assert.Equal(3, moves.Length, "the first two rows are not touched");
            Assert.Equal(2, moves[0].Slot, "the first move fills place 2");
        }

        // ---- random lists ----

        [Test]
        public void RandomLists_EndExactlyInOrder()
        {
            var rng = new Random(12345);
            for (int trial = 0; trial < 3000; trial++)
            {
                int n = rng.Next(0, 26);
                var list = new List<Item>();
                for (int i = 0; i < n; i++)
                {
                    int id = rng.Next(4) == 0 ? 0 : 100000 + rng.Next(6);
                    list.Add(new Item { Letter = true, Name = "r" + i, Date = new DateTime(1985, 1, 1).AddDays(rng.Next(5)).AddHours(rng.Next(24)), Id = id });
                }

                InboxMove[] moves;
                List<Item> sorted = Sort(list, out moves);

                // The right order: stable sort by date (newest first, time ignored), then id (larger first).
                List<Item> expected = list.Select((x, i) => new { x, i })
                    .OrderByDescending(p => p.x.Date.Date).ThenByDescending(p => p.x.Id).ThenBy(p => p.i)
                    .Select(p => p.x).ToList();
                Assert.True(sorted.SequenceEqual(expected), "trial " + trial + ": " + Names(list) + " -> " + Names(sorted) + ", expected " + Names(expected));

                bool alreadyInOrder = InboxOrder.IsOrdered(list.Select(x => x.Date).ToList(), list.Select(x => x.Id).ToList());
                if (alreadyInOrder) Assert.Equal(0, moves.Length, "trial " + trial + ": a list in order gets no moves");
            }
        }

        [Test]
        public void ForeignChildrenAmongTheRows_RowsEndInOrderAndForeignOnesKeepTheirOrder()
        {
            var rng = new Random(777);
            for (int trial = 0; trial < 3000; trial++)
            {
                int n = rng.Next(2, 20);
                var list = new List<Item>();
                for (int i = 0; i < n; i++)
                {
                    if (rng.Next(5) == 0) list.Add(new Item { Letter = false, Name = "x" + i });
                    else list.Add(new Item { Letter = true, Name = "r" + i, Date = new DateTime(1985, 1, 1).AddDays(rng.Next(5)), Id = rng.Next(3) == 0 ? 0 : 100000 + rng.Next(5) });
                }

                InboxMove[] moves;
                List<Item> sorted = Sort(list, out moves);

                List<Item> letters = sorted.Where(x => x.Letter).ToList();
                Assert.True(InboxOrder.IsOrdered(letters.Select(x => x.Date).ToList(), letters.Select(x => x.Id).ToList()), "trial " + trial + ": " + Names(sorted));
                Assert.True(sorted.Where(x => !x.Letter).SequenceEqual(list.Where(x => !x.Letter)), "trial " + trial + ": foreign children keep their order");
                Assert.Equal(list.Count, sorted.Count, "nothing lost");
            }
        }

        // ---- arguments ----

        [Test]
        public void Arguments_AreChecked()
        {
            Assert.Throws<ArgumentNullException>(() => InboxSortPlan.Compute(null, new DateTime[0], new int[0]));
            Assert.Throws<ArgumentNullException>(() => InboxSortPlan.Compute(new int[0], null, new int[0]));
            Assert.Throws<ArgumentNullException>(() => InboxSortPlan.Compute(new int[0], new DateTime[0], null));
            Assert.Throws<ArgumentException>(() => InboxSortPlan.Compute(new[] { 0 }, new DateTime[0], new int[0]));
            Assert.Throws<ArgumentException>(() => InboxSortPlan.Compute(new[] { 0 }, new[] { DateTime.MinValue }, new int[0]));
        }
    }
}
