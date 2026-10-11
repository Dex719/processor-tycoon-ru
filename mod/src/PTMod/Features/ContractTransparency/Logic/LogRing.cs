using System;
using System.Collections.Generic;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// A ring buffer with a fixed capacity (design.md, "LogRing"): the log keeps the newest <see cref="Capacity"/> entries, and adding to a full ring pushes the oldest
    /// one out. Index 0 is the oldest item, <c>Count - 1</c> the newest. Adding is O(1); nothing is allocated after the buffer has grown to its capacity.
    /// </summary>
    public sealed class LogRing<T>
    {
        private T[] _items;
        private int _head;     // index of the oldest item
        private int _count;
        private int _capacity;

        /// <param name="capacity">How many items the ring holds, at least 1.</param>
        public LogRing(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException("capacity", "the capacity of a ring is at least 1");
            _capacity = capacity;
            _items = new T[Math.Min(capacity, 16)];
        }

        /// <summary>The most items the ring holds.</summary>
        public int Capacity
        {
            get { return _capacity; }
        }

        /// <summary>How many items it holds now.</summary>
        public int Count
        {
            get { return _count; }
        }

        /// <summary>The item at the position <paramref name="index"/> counted from the oldest (0).</summary>
        public T this[int index]
        {
            get
            {
                if (index < 0 || index >= _count) throw new ArgumentOutOfRangeException("index");
                return _items[(_head + index) % _items.Length];
            }
        }

        /// <summary>Adds an item as the newest. Returns true when the ring was full and the oldest item was pushed out.</summary>
        public bool Add(T item)
        {
            if (_count == _capacity)
            {
                _items[_head] = item;
                _head = (_head + 1) % _items.Length;
                return true;
            }

            if (_count == _items.Length) Grow();
            _items[(_head + _count) % _items.Length] = item;
            _count++;
            return false;
        }

        /// <summary>Empties the ring; the capacity stays.</summary>
        public void Clear()
        {
            Array.Clear(_items, 0, _items.Length);
            _head = 0;
            _count = 0;
        }

        /// <summary>
        /// Changes the capacity. A smaller one pushes out the oldest items that no longer fit; the number of items lost is returned.
        /// </summary>
        public int Resize(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException("capacity", "the capacity of a ring is at least 1");

            int keep = Math.Min(_count, capacity);
            var items = new T[Math.Min(capacity, Math.Max(keep, 16))];
            int skip = _count - keep;
            for (int i = 0; i < keep; i++) items[i] = this[skip + i];

            _items = items;
            _head = 0;
            _count = keep;
            _capacity = capacity;
            return skip;
        }

        /// <summary>The items from the oldest to the newest, in a new list.</summary>
        public List<T> ToList()
        {
            var list = new List<T>(_count);
            for (int i = 0; i < _count; i++) list.Add(this[i]);
            return list;
        }

        private void Grow()
        {
            int size = Math.Min(_capacity, Math.Max(_items.Length * 2, 16));
            var items = new T[size];
            for (int i = 0; i < _count; i++) items[i] = this[i];
            _items = items;
            _head = 0;
        }
    }
}
