using System;
using System.Collections.Generic;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// A list ordered by descending priority (D14, FR-6 item 7): a larger number goes first, equal priorities keep the order of adding. Adding an item that is
    /// already there moves it to its new priority. The list of <c>EventAi.AddChooser</c>.
    /// </summary>
    public sealed class PriorityList<T> where T : class
    {
        private readonly List<KeyValuePair<int, T>> _items = new List<KeyValuePair<int, T>>();

        public int Count
        {
            get { return _items.Count; }
        }

        /// <summary>The items in the order they are asked (a copy).</summary>
        public IList<T> Items
        {
            get
            {
                var list = new List<T>(_items.Count);
                foreach (KeyValuePair<int, T> item in _items) list.Add(item.Value);
                return list.AsReadOnly();
            }
        }

        public void Add(T item, int priority)
        {
            if (item == null) return;
            Remove(item);

            int index = _items.Count;
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Key < priority)
                {
                    index = i;
                    break;
                }
            }
            _items.Insert(index, new KeyValuePair<int, T>(priority, item));
        }

        public bool Remove(T item)
        {
            if (item == null) return false;
            return _items.RemoveAll(entry => ReferenceEquals(entry.Value, item)) > 0;
        }
    }

    /// <summary>
    /// How an AI company decides on the choices of an instance (D14, AC-6.4): the choosers are asked in the order of the chain; one that does not handle the
    /// event (<c>IEventAiChooserFilter.Handles</c> false) is skipped without being asked; the first answer that names an available choice wins and nobody after it
    /// is asked; null, an unknown or unavailable choice and an exception (counted against the chooser) pass the decision on. Null when nobody decided: the
    /// default policy decides then.
    /// </summary>
    public static class AiChooserChain
    {
        /// <param name="choosers">The chain in its order.</param>
        /// <param name="handles">Whether a chooser handles the event (null - every chooser does).</param>
        /// <param name="ask">The answer of a chooser.</param>
        /// <param name="isAvailable">Whether a choice id is a choice of the instance that the company may take.</param>
        /// <param name="onFault">A chooser threw (in <paramref name="handles"/> or <paramref name="ask"/>).</param>
        public static string Decide<T>(IList<T> choosers, Func<T, bool> handles, Func<T, string> ask, Func<string, bool> isAvailable, Action<T, Exception> onFault)
        {
            if (choosers == null || ask == null) return null;

            foreach (T chooser in choosers)
            {
                if (chooser == null) continue;
                string answer;
                try
                {
                    if (handles != null && !handles(chooser)) continue;
                    answer = ask(chooser);
                }
                catch (Exception ex)
                {
                    if (onFault != null) onFault(chooser, ex);
                    continue;
                }

                if (string.IsNullOrEmpty(answer)) continue;
                if (isAvailable != null && !isAvailable(answer)) continue;
                return answer;
            }
            return null;
        }
    }
}
