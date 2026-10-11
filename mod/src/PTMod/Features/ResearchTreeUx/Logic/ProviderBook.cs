using System;
using System.Collections.Generic;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>What <see cref="ProviderBook{T}.Add"/> did with a registration.</summary>
    public enum AddOutcome
    {
        /// <summary>The provider is in the book.</summary>
        Added = 0,

        /// <summary>Its key is taken by another provider or reserved (a built-in tab); the provider is not in the book (FR-7: a duplicate <c>TabId</c> is rejected).</summary>
        KeyTaken = 1,

        /// <summary>This very object is in the book already; the second registration changes nothing.</summary>
        AlreadyRegistered = 2,

        /// <summary>A null provider, or an empty key where a key was asked for.</summary>
        Invalid = 3,

        /// <summary>The book holds <see cref="ProviderBook{T}.MaxEntries"/> providers already.</summary>
        Full = 4,
    }

    /// <summary>
    /// One provider in a <see cref="ProviderBook{T}"/> with what was read from it once, when it was registered: the key (the tab id), the caption
    /// key, the order, and the feature that owns the code of the provider (the one a failure is reported on, decision D11).
    /// </summary>
    public sealed class ProviderEntry<T> where T : class
    {
        internal ProviderEntry(T provider, string key, string caption, int order, string owner, int sequence)
        {
            Provider = provider;
            Key = key;
            Caption = caption;
            Order = order;
            Owner = owner;
            Sequence = sequence;
        }

        /// <summary>The object the other feature registered.</summary>
        public T Provider { get; private set; }

        /// <summary>The unique key (the tab id), or null for a provider that has none.</summary>
        public string Key { get; private set; }

        /// <summary>A second text kept with the provider (the <c>ModL10n</c> key of a tab caption); may be null.</summary>
        public string Caption { get; private set; }

        /// <summary>Position among the providers of the book, ascending.</summary>
        public int Order { get; private set; }

        /// <summary>The id of the feature that owns the code of the provider; faults are counted against it.</summary>
        public string Owner { get; private set; }

        /// <summary>The number of the registration: providers of the same <see cref="Order"/> keep the order in which they were added.</summary>
        public int Sequence { get; private set; }

        /// <summary>The provider failed once and is not asked again until the game restarts (FR-7 item 3).</summary>
        public bool Disabled { get; internal set; }

        public override string ToString()
        {
            return (Key ?? "provider") + " of " + Owner + (Disabled ? " (off)" : string.Empty);
        }
    }

    /// <summary>
    /// The registry behind one kind of provider of <c>ResearchTreeUxApi</c> (FR-7), without the game: the tabs, the sources of compare rows, of
    /// modifier lines, of tooltip lines and of lab occupancy each have a book. Registrations are kept whatever the state of the feature
    /// <c>research-tree-ux</c> is (item 6), so a provider registered before the window is built, while the feature is off, or after it, is
    /// found by the next <see cref="Collect"/>. The book knows the rules and nothing of the way a provider is called:
    /// <list type="bullet">
    /// <item><b>A key is unique.</b> A tab id that a built-in tab or another provider holds is refused (<see cref="AddOutcome.KeyTaken"/>); a provider
    /// without a key (<c>key == null</c>) is never refused for that.</item>
    /// <item><b>The same object is added once.</b> A second registration of it is <see cref="AddOutcome.AlreadyRegistered"/>.</item>
    /// <item><b>A provider that failed is switched off</b> for good (<see cref="Disable"/>), and the others go on (AC-7.2).</item>
    /// <item><b>A provider of a feature that is not active is not asked</b> (<see cref="Collect"/> takes the test as a function), and is asked again as
    /// soon as its feature is switched on (AC-7.3).</item>
    /// </list>
    /// Not thread safe by itself; the registry that owns a book takes a lock.
    /// </summary>
    public sealed class ProviderBook<T> where T : class
    {
        /// <summary>At most this many providers are kept in a book; the rest are refused (a defence against a loop that registers without end).</summary>
        public const int MaxEntries = 64;

        private readonly List<ProviderEntry<T>> _entries = new List<ProviderEntry<T>>();
        private readonly HashSet<string> _reserved;
        private int _sequence;

        public ProviderBook()
            : this(null)
        {
        }

        /// <summary>A book whose keys <paramref name="reservedKeys"/> belong to someone else (the ids of the built-in tabs): a provider cannot take them.</summary>
        public ProviderBook(IEnumerable<string> reservedKeys)
        {
            _reserved = new HashSet<string>(StringComparer.Ordinal);
            if (reservedKeys == null) return;

            foreach (string key in reservedKeys)
            {
                if (!string.IsNullOrEmpty(key)) _reserved.Add(key);
            }
        }

        /// <summary>How many providers are in the book, the switched-off ones included.</summary>
        public int Count
        {
            get { return _entries.Count; }
        }

        /// <summary>
        /// Puts a provider into the book. <paramref name="key"/> is compared exactly (case counts); pass null for a provider that has no key.
        /// <paramref name="owner"/> is the feature that owns the code of the provider. Nothing is changed unless the result is
        /// <see cref="AddOutcome.Added"/>.
        /// </summary>
        public AddOutcome Add(T provider, string key, string caption, int order, string owner)
        {
            if (provider == null) return AddOutcome.Invalid;
            if (key != null && key.Length == 0) return AddOutcome.Invalid;

            foreach (ProviderEntry<T> entry in _entries)
            {
                if (ReferenceEquals(entry.Provider, provider)) return AddOutcome.AlreadyRegistered;
            }

            if (key != null)
            {
                if (_reserved.Contains(key)) return AddOutcome.KeyTaken;
                foreach (ProviderEntry<T> entry in _entries)
                {
                    if (string.Equals(entry.Key, key, StringComparison.Ordinal)) return AddOutcome.KeyTaken;
                }
            }

            if (_entries.Count >= MaxEntries) return AddOutcome.Full;

            _entries.Add(new ProviderEntry<T>(provider, key, caption, order, owner, _sequence++));
            return AddOutcome.Added;
        }

        /// <summary>The entry of a registered object (switched off or not), or null.</summary>
        public ProviderEntry<T> EntryOf(T provider)
        {
            if (provider == null) return null;

            foreach (ProviderEntry<T> entry in _entries)
            {
                if (ReferenceEquals(entry.Provider, provider)) return entry;
            }
            return null;
        }

        /// <summary>The entry that holds a key, or null. A switched-off provider is found too: its key stays taken.</summary>
        public ProviderEntry<T> EntryByKey(string key)
        {
            if (key == null) return null;

            foreach (ProviderEntry<T> entry in _entries)
            {
                if (string.Equals(entry.Key, key, StringComparison.Ordinal)) return entry;
            }
            return null;
        }

        /// <summary>The provider failed: it is not asked again. False when it is not in the book or was off already.</summary>
        public bool Disable(T provider)
        {
            return Disable(EntryOf(provider));
        }

        /// <summary>The same for an entry of this book.</summary>
        public bool Disable(ProviderEntry<T> entry)
        {
            if (entry == null || entry.Disabled) return false;
            entry.Disabled = true;
            return true;
        }

        /// <summary>
        /// Fills <paramref name="into"/> (cleared first) with the providers to ask now: not switched off, and, when <paramref name="ownerActive"/> is
        /// given, owned by a feature it accepts. In the order of <see cref="ProviderEntry{T}.Order"/>, ties in the order of registration. Returns the number.
        /// </summary>
        public int Collect(Func<string, bool> ownerActive, List<ProviderEntry<T>> into)
        {
            if (into == null) throw new ArgumentNullException("into");

            into.Clear();
            foreach (ProviderEntry<T> entry in _entries)
            {
                if (entry.Disabled) continue;
                if (ownerActive != null && !ownerActive(entry.Owner)) continue;
                into.Add(entry);
            }

            if (into.Count > 1) into.Sort(CompareEntries);
            return into.Count;
        }

        // List.Sort is not stable, so the sequence number breaks every tie.
        private static int CompareEntries(ProviderEntry<T> a, ProviderEntry<T> b)
        {
            int byOrder = a.Order.CompareTo(b.Order);
            return byOrder != 0 ? byOrder : a.Sequence.CompareTo(b.Sequence);
        }
    }
}
