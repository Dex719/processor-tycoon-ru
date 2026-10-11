using System;
using System.Collections.Generic;

namespace PTMod.Features.PressReviews
{
    /// <summary>What <see cref="AdjusterBook{T}.Register"/> did with an adjuster.</summary>
    public enum AdjusterRegistration
    {
        /// <summary>Not taken: no adjuster, no id, an id that cannot be read, or the book is full.</summary>
        Rejected = 0,

        /// <summary>A new id: the adjuster joined the end of the list.</summary>
        Added = 1,

        /// <summary>The id was there already: the new adjuster took the place of the old one.</summary>
        Replaced = 2,
    }

    /// <summary>
    /// The adjusters other features registered (<c>PressReviewsApi.RegisterAdjuster</c>), by id (FR-11 item 3). The id is read once, when the adjuster is
    /// registered. An id is held by one adjuster: registering it again replaces the old one in its place (a feature that registers on every start has one
    /// entry, not two). The order is the order of the first registration. Pure and generic, so the rules are tested without the game's CPU the real
    /// interface takes.
    /// </summary>
    public sealed class AdjusterBook<T> where T : class
    {
        /// <summary>The most adjusters the book holds.</summary>
        public const int MaxAdjusters = 32;

        private sealed class Entry
        {
            public string Id;
            public T Adjuster;
        }

        private readonly Func<T, string> idOf;
        private readonly List<Entry> entries = new List<Entry>();

        public AdjusterBook(Func<T, string> idOf)
        {
            if (idOf == null) throw new ArgumentNullException("idOf");
            this.idOf = idOf;
        }

        public int Count
        {
            get { return entries.Count; }
        }

        public AdjusterRegistration Register(T adjuster)
        {
            if (adjuster == null) return AdjusterRegistration.Rejected;

            string id = ReadId(adjuster);
            if (string.IsNullOrWhiteSpace(id)) return AdjusterRegistration.Rejected;

            int at = IndexOf(id);
            if (at >= 0)
            {
                entries[at] = new Entry { Id = id, Adjuster = adjuster };
                return AdjusterRegistration.Replaced;
            }

            if (entries.Count >= MaxAdjusters) return AdjusterRegistration.Rejected;
            entries.Add(new Entry { Id = id, Adjuster = adjuster });
            return AdjusterRegistration.Added;
        }

        /// <summary>Takes the adjuster with this id out; false when there was none.</summary>
        public bool Unregister(string id)
        {
            int at = IndexOf(id);
            if (at < 0) return false;

            entries.RemoveAt(at);
            return true;
        }

        public bool Contains(string id)
        {
            return IndexOf(id) >= 0;
        }

        /// <summary>The adjusters in the order of registration, with their ids (a copy: registering during a review changes nothing for it).</summary>
        public List<KeyValuePair<string, T>> Snapshot()
        {
            var copy = new List<KeyValuePair<string, T>>(entries.Count);
            for (int i = 0; i < entries.Count; i++) copy.Add(new KeyValuePair<string, T>(entries[i].Id, entries[i].Adjuster));
            return copy;
        }

        public void Clear()
        {
            entries.Clear();
        }

        private int IndexOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].Id, id, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        private string ReadId(T adjuster)
        {
            try
            {
                return idOf(adjuster);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
