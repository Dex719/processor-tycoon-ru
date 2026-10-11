using System.Collections.Generic;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// A pool of lists for the daily path of the patches (TSK-009: no allocation per game day): the prefix of <c>AIBehaviourMarket.OnTick</c> rents a
    /// list for the CPUs without a price and the postfix gives it back. A list is cleared when it is returned; a few lists are kept (nested calls
    /// rent more than one). Main thread only.
    /// </summary>
    public sealed class ListPool<T>
    {
        /// <summary>The most lists kept for reuse; more are left to the garbage collector.</summary>
        public const int MaxKept = 8;

        private readonly Stack<List<T>> _free = new Stack<List<T>>(MaxKept);
        private readonly int _capacity;

        /// <param name="capacity">The capacity of a new list.</param>
        public ListPool(int capacity = 8)
        {
            _capacity = capacity < 0 ? 0 : capacity;
        }

        /// <summary>How many lists the pool has made (a test of "no allocation" checks that it stops growing).</summary>
        public int Created { get; private set; }

        /// <summary>How many lists wait for reuse.</summary>
        public int FreeCount
        {
            get { return _free.Count; }
        }

        /// <summary>An empty list: a reused one when there is one, otherwise a new one.</summary>
        public List<T> Rent()
        {
            if (_free.Count > 0) return _free.Pop();
            Created++;
            return new List<T>(_capacity);
        }

        /// <summary>Gives a list back (cleared here). Null is ignored; a list given back twice is kept once.</summary>
        public void Return(List<T> list)
        {
            if (list == null) return;
            list.Clear();
            if (_free.Count >= MaxKept || _free.Contains(list)) return;
            _free.Push(list);
        }
    }
}
