using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Core.Logic.Modifiers
{
    /// <summary>One row of the "Channels" block of the diagnostics tab: a declared channel (or family) or an undeclared name that has entries or providers.</summary>
    public sealed class ChannelInfo
    {
        internal ChannelInfo(string name, ChannelSpec spec, int entryCount, int providerCount)
        {
            Name = name;
            Spec = spec;
            EntryCount = entryCount;
            ProviderCount = providerCount;
        }

        /// <summary>The declared name (for a family, the family name such as "gpu.demand.*"), or the undeclared channel name.</summary>
        public string Name { get; }

        /// <summary>A copy of the declaration, or null for a channel without one.</summary>
        public ChannelSpec Spec { get; }

        public bool IsDeclared
        {
            get { return Spec != null; }
        }

        public bool HasBalanceRange
        {
            get { return Spec != null && Spec.HasBalanceRange; }
        }

        /// <summary>Entries on the channel (all features, applied or not); for a family, on all its members.</summary>
        public int EntryCount { get; }

        public int ProviderCount { get; }
    }

    /// <summary>
    /// The channel engine of <c>ModModifiers</c> (FR-12) without any game type: channel declarations, saved entries,
    /// providers, evaluation, <c>Explain</c>, the one-day cache. The game facade turns its <c>ModifierContext</c> (company,
    /// CPU, contract, design) into a <see cref="ModifierQuery"/> and back.
    /// <para>
    /// <b>Value.</b> <c>Get = clamp(clamp(product, BalanceMin, BalanceMax), Min, Max)</c>, where the product runs over the entries
    /// whose scope matches the query and over the providers, both only of features that are active; the balance clamp applies
    /// only if the channel has a balance range (a product of 0 is raised to BalanceMin like any other too small product). A channel
    /// without entries and providers returns 1 without any work or allocation.
    /// </para>
    /// <para>
    /// <b>Lazy binding (D21).</b> Entries and providers are stored by channel name. A channel that is not declared yet
    /// (or never is) takes entries and providers and is read with the default fuse [0.1; 10]; a later
    /// <see cref="Declare"/> rebinds it and drops its cache. Names covered by a declared family (<c>gpu.demand.*</c>) use the
    /// bounds of the family. <see cref="UndeclaredChannels"/> is what the self-check warns about.
    /// </para>
    /// <para>
    /// <b>Time and cache.</b> The registry has no clock: the host calls <see cref="SetToday"/> on load and on every
    /// <c>DayPassed</c>, which removes the entries that ended and empties the cache. Values are cached per (channel,
    /// company, CPU, segment, contract) until the day changes; design queries and channels with <c>Cacheable = false</c> are not.
    /// Everything that can change a result (<c>Add</c>, <c>Remove</c>, <c>RegisterProvider</c>, <see cref="Invalidate(string)"/>,
    /// <see cref="FeatureStateChanged"/>) drops the cache of the affected channel and raises <see cref="ChannelChanged"/>.
    /// A feature whose provider depends on state that changes inside a day calls <see cref="Invalidate(string)"/> itself.
    /// </para>
    /// <para>
    /// <b>Failures.</b> A provider that throws, or returns NaN, an infinity or a negative number, counts as x1; the failure is
    /// counted per feature (<see cref="ProviderFaults"/>) and reported through <see cref="ProviderFailed"/> so that the host can log
    /// it and trip the feature's circuit breaker. Invalid input to <c>Add</c> and <c>RegisterProvider</c> is rejected and reported
    /// through <see cref="Warning"/>. Handlers never break the registry. A provider that reads its own channel is cut off at a
    /// nesting depth of <see cref="MaxEvaluationDepth"/> instead of overflowing the stack.
    /// </para>
    /// Main thread only: the registry is not thread-safe. Providers and handlers may call back into the registry; entries and
    /// providers are held in copy-on-write arrays, so changing them during an evaluation is safe.
    /// </summary>
    public sealed class ChannelRegistry
    {
        /// <summary>Lower bound of the fuse of an undeclared channel.</summary>
        public const float DefaultFuseMin = 0.1f;

        /// <summary>Upper bound of the fuse of an undeclared channel.</summary>
        public const float DefaultFuseMax = 10f;

        /// <summary>How deep evaluations may nest (a provider asking for another channel is fine, asking for its own is not).</summary>
        public const int MaxEvaluationDepth = 8;

        /// <summary>Longest duration of a temporary entry, in days.</summary>
        public const int MaxDurationDays = 100000;

        private static readonly ChannelSpec UndeclaredSpec = new ChannelSpec { Name = "(undeclared)", Min = DefaultFuseMin, Max = DefaultFuseMax };
        private static readonly ModifierEntry[] NoEntries = new ModifierEntry[0];
        private static readonly ProviderRecord[] NoProviders = new ProviderRecord[0];
        private static readonly string[] NoArgs = new string[0];

        private sealed class ProviderRecord
        {
            public string FeatureId;
            public string Channel;
            public Func<ModifierQuery, float> Plain;
            public Func<ModifierQuery, ProviderResult> Reasoned;
            public string ReasonKey;
            public object Identity;
        }

        private sealed class Bucket
        {
            public readonly string Name;
            public ModifierEntry[] Entries = NoEntries;
            public ProviderRecord[] Providers = NoProviders;
            public ChannelSpec Spec;     // resolved declaration; null when there is none (valid only if SpecVersion is current)
            public int SpecVersion = -1;

            public Bucket(string name)
            {
                Name = name;
            }

            public bool IsEmpty
            {
                get { return Entries.Length == 0 && Providers.Length == 0; }
            }
        }

        private readonly Dictionary<string, ChannelSpec> specs = new Dictionary<string, ChannelSpec>(StringComparer.Ordinal);
        private readonly List<ChannelSpec> families = new List<ChannelSpec>(); // longest prefix first
        private readonly Dictionary<string, Bucket> buckets = new Dictionary<string, Bucket>(StringComparer.Ordinal);
        private readonly List<ModifierEntry> all = new List<ModifierEntry>();
        private readonly Dictionary<string, ModifierEntry> byId = new Dictionary<string, ModifierEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> faults = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly ModifierCache cache = new ModifierCache();

        private Func<string, bool> featureActive;
        private DateTime today;
        private int specVersion;
        private int version;
        private int nextId = 1;
        private int depth;
        private int providerCount;

        /// <param name="isFeatureActive">
        /// Tells whether a feature is enabled (its entries and providers apply); null means every feature is. Called
        /// for every entry and provider on a cache miss, so it must be cheap (<c>ModFeatures.IsActive</c> is a dictionary lookup).
        /// </param>
        /// <param name="today">The game date at creation; the host moves it with <see cref="SetToday"/>.</param>
        public ChannelRegistry(Func<string, bool> isFeatureActive = null, DateTime today = default(DateTime))
        {
            featureActive = isFeatureActive;
            this.today = today.Date;
        }

        // ---- events ----

        /// <summary>
        /// The result of a channel may have changed: an entry or a provider came or went, an entry ended, the channel was
        /// declared, a feature changed state, <see cref="Invalidate(string)"/> was called. The argument is the channel name.
        /// For a whole-registry reset one event per channel that has entries or providers is raised.
        /// </summary>
        public event Action<string> ChannelChanged;

        /// <summary>A rejected call or an anomaly, as (feature id or null, English text). The host writes it to ModLog.</summary>
        public event Action<string, string> Warning;

        /// <summary>A provider threw or returned an unusable number: (feature id, channel, exception). The value used was x1.</summary>
        public event Action<string, string, Exception> ProviderFailed;

        // ---- state ----

        /// <summary>The game date the registry works with (time of day dropped).</summary>
        public DateTime Today
        {
            get { return today; }
        }

        /// <summary>Counter that grows with every change that can alter a result; lets the host keep fast-path flags and rebuild them only when it moved.</summary>
        public int Version
        {
            get { return version; }
        }

        /// <summary>The cache, for diagnostics and tests.</summary>
        public ModifierCache Cache
        {
            get { return cache; }
        }

        /// <summary>Same predicate as the constructor's; set it when the feature registry appears after the modifier registry. Changing it empties the cache.</summary>
        public Func<string, bool> FeatureActive
        {
            get { return featureActive; }
            set
            {
                featureActive = value;
                version++;
                cache.Clear();
            }
        }

        /// <summary>Number of saved entries (including those of disabled features).</summary>
        public int EntryCount
        {
            get { return all.Count; }
        }

        /// <summary>Number of registered providers.</summary>
        public int ProviderCount
        {
            get { return providerCount; }
        }

        // ---- channels ----

        /// <summary>
        /// Declares a channel or a family. Accepted when the declaration is valid and the name is new; a repeat with the same
        /// bounds is accepted and changes nothing (returns true); a repeat with other bounds is rejected with a warning
        /// (returns false). Entries and providers that already wait on the name are rebound at once.
        /// </summary>
        public bool Declare(ChannelSpec spec)
        {
            if (spec == null)
            {
                Warn(null, "channel declaration rejected: it is null");
                return false;
            }

            string error;
            if (!spec.Validate(out error))
            {
                Warn(spec.OwnerFeature, "channel declaration rejected: " + error);
                return false;
            }

            ChannelSpec existing;
            if (specs.TryGetValue(spec.Name, out existing))
            {
                if (existing.SameBounds(spec)) return true;
                Warn(spec.OwnerFeature, "channel '" + spec.Name + "' is already declared with other bounds " + existing + "; declaration " + spec + " rejected");
                return false;
            }

            ChannelSpec copy = spec.Clone();
            specs[copy.Name] = copy;
            if (copy.IsFamily) InsertFamily(copy);
            specVersion++;
            version++;

            // Channels that were waiting for this declaration now have other bounds.
            List<string> affected = null;
            foreach (Bucket bucket in buckets.Values)
            {
                if (bucket.IsEmpty) continue;
                bool hit = copy.IsFamily ? ChannelFamilies.Matches(copy.Name, bucket.Name) : string.Equals(copy.Name, bucket.Name, StringComparison.Ordinal);
                if (!hit) continue;
                if (affected == null) affected = new List<string>();
                affected.Add(bucket.Name);
            }
            if (affected != null)
            {
                foreach (string name in affected)
                {
                    cache.Invalidate(name);
                    RaiseChanged(name);
                }
            }
            return true;
        }

        /// <summary>True when a declaration covers the name: the exact name or a family.</summary>
        public bool IsDeclared(string channel)
        {
            return channel != null && LookupSpec(channel) != null;
        }

        /// <summary>A copy of the declaration that covers the name (for a family member, the family's), or null.</summary>
        public ChannelSpec SpecOf(string channel)
        {
            ChannelSpec spec = channel == null ? null : LookupSpec(channel);
            return spec == null ? null : spec.Clone();
        }

        /// <summary>Copies of all declarations (families included), by name.</summary>
        public IReadOnlyList<ChannelSpec> DeclaredChannels()
        {
            var result = new List<ChannelSpec>(specs.Count);
            foreach (ChannelSpec spec in specs.Values) result.Add(spec.Clone());
            result.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return result;
        }

        /// <summary>Names that have entries or providers but no declaration (a typo or a feature that never declared its channel), sorted.</summary>
        public IReadOnlyList<string> UndeclaredChannels()
        {
            var result = new List<string>();
            foreach (Bucket bucket in buckets.Values)
            {
                if (!bucket.IsEmpty && LookupSpec(bucket.Name) == null) result.Add(bucket.Name);
            }
            result.Sort(string.CompareOrdinal);
            return result;
        }

        /// <summary>Rows for the diagnostics tab: every declared channel with its numbers, plus the undeclared names that are in use; sorted by name.</summary>
        public IReadOnlyList<ChannelInfo> Describe()
        {
            var counts = new Dictionary<string, int[]>(StringComparer.Ordinal);
            foreach (string name in specs.Keys) counts[name] = new int[2];

            foreach (Bucket bucket in buckets.Values)
            {
                if (bucket.IsEmpty) continue;
                ChannelSpec spec = LookupSpec(bucket.Name);
                string row = spec == null ? bucket.Name : spec.Name;
                int[] numbers;
                if (!counts.TryGetValue(row, out numbers))
                {
                    numbers = new int[2];
                    counts[row] = numbers;
                }
                numbers[0] += bucket.Entries.Length;
                numbers[1] += bucket.Providers.Length;
            }

            var rows = new List<ChannelInfo>(counts.Count);
            foreach (KeyValuePair<string, int[]> pair in counts)
            {
                ChannelSpec spec;
                specs.TryGetValue(pair.Key, out spec);
                rows.Add(new ChannelInfo(pair.Key, spec == null ? null : spec.Clone(), pair.Value[0], pair.Value[1]));
            }
            rows.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return rows;
        }

        // ---- entries ----

        /// <summary>
        /// Adds a saved entry. <paramref name="durationDays"/> 0 means unlimited; otherwise the entry stops applying on
        /// <c>Today + durationDays</c>. Returns the entry, or null (with a <see cref="Warning"/>) when the feature id, the
        /// channel name, the scope, the multiplier (finite, 0 or more) or the duration is invalid.
        /// </summary>
        public ModifierEntry Add(string featureId, string channel, ModifierScope scope, float multiplier,
            int durationDays = 0, string reasonKey = null, string[] reasonArgs = null)
        {
            string problem = CheckEntry(featureId, channel, scope, multiplier);
            DateTime? expires = null;
            if (problem == null)
            {
                if (durationDays < 0 || durationDays > MaxDurationDays)
                {
                    problem = "the duration must be between 0 and " + MaxDurationDays.ToString(CultureInfo.InvariantCulture) + " days";
                }
                else if (durationDays > 0)
                {
                    expires = today.AddDays(durationDays);
                }
            }
            if (problem != null)
            {
                Warn(featureId, "modifier on '" + channel + "' rejected: " + problem);
                return null;
            }

            var entry = new ModifierEntry(NewId(), featureId, channel, scope, multiplier, expires, reasonKey, reasonArgs);
            Insert(entry);
            return entry;
        }

        /// <summary>
        /// Puts back an entry read from a save, with its own id and end date. Returns null when the data is invalid or the id is
        /// taken (with a <see cref="Warning"/>), and also, silently, when the end date is not after <see cref="Today"/> (the entry is over).
        /// Ids of the form "m&lt;number&gt;" move the counter of new ids past the number, so new entries never reuse a restored id.
        /// </summary>
        public ModifierEntry Restore(string id, string featureId, string channel, ModifierScope scope, float multiplier,
            DateTime? expiresOn, string reasonKey = null, string[] reasonArgs = null)
        {
            string problem = string.IsNullOrEmpty(id) ? "the entry has no id" : CheckEntry(featureId, channel, scope, multiplier);
            if (problem == null && byId.ContainsKey(id)) problem = "the id '" + id + "' is already used";
            if (problem != null)
            {
                Warn(featureId, "saved modifier '" + id + "' on '" + channel + "' dropped: " + problem);
                return null;
            }
            if (expiresOn.HasValue && expiresOn.Value.Date <= today) return null;

            NoteRestoredId(id);
            var entry = new ModifierEntry(id, featureId, channel, scope, multiplier, expiresOn, reasonKey, reasonArgs);
            Insert(entry);
            return entry;
        }

        /// <summary>Removes one entry by id; false if there is none.</summary>
        public bool Remove(string entryId)
        {
            ModifierEntry entry;
            if (entryId == null || !byId.TryGetValue(entryId, out entry)) return false;
            Detach(entry);
            Touch(entry.Channel);
            return true;
        }

        /// <summary>Removes the entries of a feature, all of them or those of one channel; returns how many.</summary>
        public int RemoveAll(string featureId, string channel = null)
        {
            if (featureId == null) return 0;

            List<ModifierEntry> doomed = null;
            foreach (ModifierEntry entry in all)
            {
                if (!string.Equals(entry.FeatureId, featureId, StringComparison.Ordinal)) continue;
                if (channel != null && !string.Equals(entry.Channel, channel, StringComparison.Ordinal)) continue;
                if (doomed == null) doomed = new List<ModifierEntry>();
                doomed.Add(entry);
            }
            if (doomed == null) return 0;

            RemoveEntries(doomed);
            return doomed.Count;
        }

        /// <summary>Removes every entry (a new game or a save is about to be loaded); providers and declarations stay. The id counter starts again at 1.</summary>
        public int ClearEntries()
        {
            if (all.Count == 0)
            {
                nextId = 1;
                return 0;
            }

            var doomed = new List<ModifierEntry>(all);
            RemoveEntries(doomed);
            nextId = 1;
            return doomed.Count;
        }

        /// <summary>The entry with this id, or null.</summary>
        public ModifierEntry Find(string entryId)
        {
            ModifierEntry entry;
            return entryId != null && byId.TryGetValue(entryId, out entry) ? entry : null;
        }

        /// <summary>A snapshot of all entries in the order they were added, the disabled features' and the not yet removed expired ones included: this is what is saved.</summary>
        public IReadOnlyList<ModifierEntry> Entries
        {
            get { return all.ToArray(); }
        }

        /// <summary>Entries of one channel (a snapshot, in the order they were added).</summary>
        public IReadOnlyList<ModifierEntry> EntriesOf(string channel)
        {
            Bucket bucket;
            return channel != null && buckets.TryGetValue(channel, out bucket) ? (ModifierEntry[])bucket.Entries.Clone() : NoEntries;
        }

        // ---- providers ----

        /// <summary>
        /// Registers a provider: a function of the query that returns a multiplier. Not saved; the feature registers it again at
        /// start. <paramref name="reasonKey"/> is the fixed reason shown by Explain. A repeated registration of the same provider
        /// by the same feature on the same channel is ignored (returns false); <paramref name="identity"/>, when given, is what is
        /// compared instead of the delegate (for a facade that wraps the feature's delegate in a new one). The result of a provider
        /// is cached with the channel's value for the rest of the game day, so a provider that depends on state which changes
        /// inside a day, or on another channel, needs <see cref="Invalidate(string)"/> from its feature when that state changes.
        /// </summary>
        public bool RegisterProvider(string featureId, string channel, Func<ModifierQuery, float> provider, string reasonKey = null, object identity = null)
        {
            if (provider == null)
            {
                Warn(featureId, "provider on '" + channel + "' rejected: it is null");
                return false;
            }
            return AddProvider(new ProviderRecord
            {
                FeatureId = featureId,
                Channel = channel,
                Plain = provider,
                ReasonKey = string.IsNullOrEmpty(reasonKey) ? null : reasonKey,
                Identity = identity ?? provider
            });
        }

        /// <summary>Registers a provider that returns a reason with every call (<see cref="ProviderResult"/>). The rest is as for <see cref="RegisterProvider"/>.</summary>
        public bool RegisterReasonedProvider(string featureId, string channel, Func<ModifierQuery, ProviderResult> provider, object identity = null)
        {
            if (provider == null)
            {
                Warn(featureId, "provider on '" + channel + "' rejected: it is null");
                return false;
            }
            return AddProvider(new ProviderRecord { FeatureId = featureId, Channel = channel, Reasoned = provider, Identity = identity ?? provider });
        }

        /// <summary>Removes the providers of a feature, all of them or those of one channel; returns how many. Needed by tests and by hot reload, not by enabling and disabling.</summary>
        public int RemoveProviders(string featureId, string channel = null)
        {
            if (featureId == null) return 0;

            var touched = new List<string>();
            int removed = 0;
            foreach (Bucket bucket in new List<Bucket>(buckets.Values))
            {
                if (channel != null && !string.Equals(bucket.Name, channel, StringComparison.Ordinal)) continue;

                var keep = new List<ProviderRecord>(bucket.Providers.Length);
                foreach (ProviderRecord record in bucket.Providers)
                {
                    if (string.Equals(record.FeatureId, featureId, StringComparison.Ordinal)) removed++;
                    else keep.Add(record);
                }
                if (keep.Count == bucket.Providers.Length) continue;

                bucket.Providers = keep.Count == 0 ? NoProviders : keep.ToArray();
                touched.Add(bucket.Name);
            }

            providerCount -= removed;
            foreach (string name in touched) Touch(name);
            return removed;
        }

        /// <summary>Number of providers a feature has registered, on all channels or on one.</summary>
        public int ProvidersOf(string featureId, string channel = null)
        {
            int count = 0;
            foreach (Bucket bucket in buckets.Values)
            {
                if (channel != null && !string.Equals(bucket.Name, channel, StringComparison.Ordinal)) continue;
                foreach (ProviderRecord record in bucket.Providers)
                {
                    if (string.Equals(record.FeatureId, featureId, StringComparison.Ordinal)) count++;
                }
            }
            return count;
        }

        /// <summary>How often providers of a feature failed (threw or returned an unusable number) since start.</summary>
        public int ProviderFaults(string featureId)
        {
            int count;
            return featureId != null && faults.TryGetValue(featureId, out count) ? count : 0;
        }

        // ---- evaluation ----

        /// <summary>True when something is registered on the channel (an entry or a provider of any feature, applying to this query or not): the fast path of the patches.</summary>
        public bool HasAny(string channel)
        {
            Bucket bucket;
            return channel != null && buckets.TryGetValue(channel, out bucket) && !bucket.IsEmpty;
        }

        /// <summary>
        /// The multiplier of a channel for a query (see the class remarks). 1 for a channel with nothing registered, without
        /// allocating. Never throws for provider errors.
        /// </summary>
        public float Get(string channel, ModifierQuery query)
        {
            Bucket bucket;
            if (channel == null || !buckets.TryGetValue(channel, out bucket) || bucket.IsEmpty) return 1f;

            ChannelSpec spec = SpecFor(bucket) ?? UndeclaredSpec;
            bool cacheable = spec.Cacheable && !query.IsDesign;

            ModifierCache.Key key = default(ModifierCache.Key);
            if (cacheable)
            {
                key = ModifierCache.Key.Of(query);
                float cached;
                if (cache.TryGet(channel, key, out cached)) return cached;
            }

            int stamp = version;
            float raw;
            bool balanceCapped, fuseCapped;
            float total = Compute(bucket, spec, query, null, out raw, out balanceCapped, out fuseCapped);

            // A provider may have changed the registry while it ran; then the value is not stored.
            if (cacheable && stamp == version) cache.Store(channel, key, total);
            return total;
        }

        /// <summary>
        /// The same computation as <see cref="Get"/> with the list of multipliers behind it (those equal to 1 are left out), the
        /// product before the limits and the flags of the balance range and the fuse. Never cached, calls the providers.
        /// <see cref="ModifierBreakdown.Total"/> equals what <see cref="Get"/> returns for the same query.
        /// </summary>
        public ModifierBreakdown Explain(string channel, ModifierQuery query)
        {
            ChannelSpec declared = channel == null ? null : LookupSpec(channel);
            ChannelSpec spec = declared ?? UndeclaredSpec;

            Bucket bucket;
            if (channel == null || !buckets.TryGetValue(channel, out bucket) || bucket.IsEmpty)
            {
                return new ModifierBreakdown(channel, null, 1f, 1f, spec.BalanceMin, spec.BalanceMax, false, false, spec.Min, spec.Max, declared != null);
            }

            var trace = new List<AppliedModifier>();
            float raw;
            bool balanceCapped, fuseCapped;
            float total = Compute(bucket, spec, query, trace, out raw, out balanceCapped, out fuseCapped);
            return new ModifierBreakdown(channel, trace.ToArray(), raw, total, spec.BalanceMin, spec.BalanceMax,
                balanceCapped, fuseCapped, spec.Min, spec.Max, declared != null);
        }

        // ---- time and invalidation ----

        /// <summary>
        /// Moves the registry to a game day: removes the entries whose end date has come (returned, so that the host can log them)
        /// and empties the cache. Call it when a save is loaded and on every <c>DayPassed</c>. The same day twice changes nothing.
        /// </summary>
        public IReadOnlyList<ModifierEntry> SetToday(DateTime day)
        {
            day = day.Date;
            bool changed = day != today;
            today = day;

            List<ModifierEntry> expired = null;
            foreach (ModifierEntry entry in all)
            {
                if (!entry.IsExpiredOn(today)) continue;
                if (expired == null) expired = new List<ModifierEntry>();
                expired.Add(entry);
            }

            if (expired != null) RemoveEntries(expired);
            if (changed)
            {
                version++;
                cache.Clear();
            }
            return expired == null ? (IReadOnlyList<ModifierEntry>)NoEntries : expired;
        }

        /// <summary>Drops the cached values of one channel, or of all channels when null (FR-12 item 17), and raises <see cref="ChannelChanged"/>.</summary>
        public void Invalidate(string channel = null)
        {
            version++;
            if (channel != null)
            {
                cache.Invalidate(channel);
                RaiseChanged(channel);
                return;
            }

            cache.Clear();
            RaiseForAllInUse();
        }

        /// <summary>Drops the cached values of one company in a channel (channel null: in all channels). Other companies keep theirs.</summary>
        public void Invalidate(string channel, string companyKey)
        {
            version++;
            cache.Invalidate(channel, companyKey);
            if (channel != null) RaiseChanged(channel);
            else RaiseForAllInUse();
        }

        /// <summary>
        /// A feature was enabled, disabled, faulted or became unavailable (<c>ModFeatures.StateChanged</c>): drops the cached values of
        /// the channels where the feature has entries or providers (all channels when the id is null) and raises <see cref="ChannelChanged"/> for them.
        /// </summary>
        public void FeatureStateChanged(string featureId = null)
        {
            version++;
            if (featureId == null)
            {
                cache.Clear();
                RaiseForAllInUse();
                return;
            }

            List<string> affected = null;
            foreach (Bucket bucket in buckets.Values)
            {
                if (!BucketHasFeature(bucket, featureId)) continue;
                if (affected == null) affected = new List<string>();
                affected.Add(bucket.Name);
            }
            if (affected == null) return;

            foreach (string name in affected)
            {
                cache.Invalidate(name);
                RaiseChanged(name);
            }
        }

        // ---- internals: evaluation ----

        private float Compute(Bucket bucket, ChannelSpec spec, ModifierQuery query, List<AppliedModifier> trace,
            out float raw, out bool balanceCapped, out bool fuseCapped)
        {
            if (depth >= MaxEvaluationDepth)
            {
                Warn(null, "evaluation of '" + bucket.Name + "' is nested too deeply (does a provider read its own channel?); treated as x1");
                raw = 1f;
                balanceCapped = false;
                fuseCapped = false;
                return 1f;
            }

            depth++;
            try
            {
                BalanceRange balance = BalanceRange.FromSpec(spec);
                double product = 1.0;

                ModifierEntry[] entries = bucket.Entries; // the arrays are replaced, never edited: safe against changes by providers
                for (int i = 0; i < entries.Length; i++)
                {
                    ModifierEntry entry = entries[i];
                    if (entry.IsExpiredOn(today) || !IsActive(entry.FeatureId) || !entry.Scope.Matches(query)) continue;

                    float m = entry.Multiplier;
                    product *= m;
                    if (trace != null && m != 1f)
                    {
                        trace.Add(new AppliedModifier(entry.FeatureId, entry.Id, entry.ReasonKey, entry.ReasonArgs, m, entry.ExpiresOn));
                    }
                }

                ProviderRecord[] providers = bucket.Providers;
                for (int i = 0; i < providers.Length; i++)
                {
                    ProviderRecord provider = providers[i];
                    if (!IsActive(provider.FeatureId)) continue;

                    float m;
                    string reasonKey;
                    string[] reasonArgs;
                    if (!TryInvoke(provider, query, out m, out reasonKey, out reasonArgs)) continue;

                    product *= m;
                    if (trace != null && m != 1f) trace.Add(new AppliedModifier(provider.FeatureId, null, reasonKey, reasonArgs, m, null));
                }

                if (double.IsNaN(product)) product = 1.0; // 0 x infinity: cannot happen with finite factors, but never leak NaN
                raw = (float)product;

                float balanced = balance.Clamp(raw);
                balanceCapped = balance.IsCapped(raw);
                float total = Clamp(balanced, spec.Min, spec.Max);
                fuseCapped = total != balanced;
                return total;
            }
            finally
            {
                depth--;
            }
        }

        private bool TryInvoke(ProviderRecord provider, ModifierQuery query, out float multiplier, out string reasonKey, out string[] reasonArgs)
        {
            multiplier = 1f;
            reasonKey = provider.ReasonKey;
            reasonArgs = NoArgs;

            try
            {
                if (provider.Plain != null)
                {
                    multiplier = provider.Plain(query);
                }
                else
                {
                    ProviderResult result = provider.Reasoned(query);
                    multiplier = result.Multiplier;
                    if (!string.IsNullOrEmpty(result.ReasonKey))
                    {
                        reasonKey = result.ReasonKey;
                        reasonArgs = result.ReasonArgs ?? NoArgs;
                    }
                }
            }
            catch (Exception ex)
            {
                multiplier = 1f;
                RecordFault(provider, ex);
                return false;
            }

            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier < 0f)
            {
                string bad = multiplier.ToString("R", CultureInfo.InvariantCulture);
                multiplier = 1f;
                RecordFault(provider, new InvalidOperationException("the provider returned the unusable multiplier " + bad));
                return false;
            }
            return true;
        }

        private void RecordFault(ProviderRecord provider, Exception ex)
        {
            int count;
            faults.TryGetValue(provider.FeatureId, out count);
            faults[provider.FeatureId] = count + 1;

            Action<string, string, Exception> handler = ProviderFailed;
            if (handler == null) return;
            try
            {
                handler(provider.FeatureId, provider.Channel, ex);
            }
            catch (Exception)
            {
                // The report must not turn into a second failure of the formula.
            }
        }

        private bool IsActive(string featureId)
        {
            Func<string, bool> predicate = featureActive;
            return predicate == null || predicate(featureId);
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        // ---- internals: declarations ----

        private ChannelSpec SpecFor(Bucket bucket)
        {
            if (bucket.SpecVersion != specVersion)
            {
                bucket.Spec = LookupSpec(bucket.Name);
                bucket.SpecVersion = specVersion;
            }
            return bucket.Spec;
        }

        private ChannelSpec LookupSpec(string channel)
        {
            ChannelSpec spec;
            if (specs.TryGetValue(channel, out spec)) return spec;

            for (int i = 0; i < families.Count; i++)
            {
                if (ChannelFamilies.Matches(families[i].Name, channel)) return families[i];
            }
            return null;
        }

        private void InsertFamily(ChannelSpec family)
        {
            int length = family.Name.Length;
            int index = 0;
            while (index < families.Count && families[index].Name.Length >= length) index++;
            families.Insert(index, family);
        }

        // ---- internals: entries and providers ----

        private static string CheckEntry(string featureId, string channel, ModifierScope scope, float multiplier)
        {
            if (string.IsNullOrEmpty(featureId)) return "the feature id is empty";
            if (!ChannelFamilies.IsValidChannelName(channel, false)) return "'" + channel + "' is not a valid channel name";

            string error;
            if (!scope.Validate(out error)) return error;
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier < 0f)
            {
                return "the multiplier must be a finite number of 0 or more (got " + multiplier.ToString("R", CultureInfo.InvariantCulture) + ")";
            }
            return null;
        }

        private string NewId()
        {
            string id;
            do
            {
                id = "m" + nextId.ToString(CultureInfo.InvariantCulture);
                nextId++;
            }
            while (byId.ContainsKey(id));
            return id;
        }

        private void NoteRestoredId(string id)
        {
            int number;
            if (id.Length > 1 && id[0] == 'm' && int.TryParse(id.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out number)
                && number >= nextId && number < int.MaxValue)
            {
                nextId = number + 1;
            }
        }

        private Bucket BucketFor(string channel)
        {
            Bucket bucket;
            if (!buckets.TryGetValue(channel, out bucket))
            {
                bucket = new Bucket(channel);
                buckets[channel] = bucket;
            }
            return bucket;
        }

        private void Insert(ModifierEntry entry)
        {
            all.Add(entry);
            byId[entry.Id] = entry;

            Bucket bucket = BucketFor(entry.Channel);
            var grown = new ModifierEntry[bucket.Entries.Length + 1];
            Array.Copy(bucket.Entries, grown, bucket.Entries.Length);
            grown[grown.Length - 1] = entry;
            bucket.Entries = grown;

            Touch(entry.Channel);
        }

        /// <summary>Takes the entry out of the lists without any notification.</summary>
        private void Detach(ModifierEntry entry)
        {
            all.Remove(entry);
            byId.Remove(entry.Id);

            Bucket bucket;
            if (!buckets.TryGetValue(entry.Channel, out bucket)) return;

            int index = Array.IndexOf(bucket.Entries, entry);
            if (index < 0) return;
            if (bucket.Entries.Length == 1)
            {
                bucket.Entries = NoEntries;
                return;
            }

            var shrunk = new ModifierEntry[bucket.Entries.Length - 1];
            Array.Copy(bucket.Entries, 0, shrunk, 0, index);
            Array.Copy(bucket.Entries, index + 1, shrunk, index, shrunk.Length - index);
            bucket.Entries = shrunk;
        }

        private void RemoveEntries(List<ModifierEntry> doomed)
        {
            var channels = new List<string>();
            foreach (ModifierEntry entry in doomed)
            {
                Detach(entry);
                if (!channels.Contains(entry.Channel)) channels.Add(entry.Channel);
            }
            foreach (string channel in channels) Touch(channel);
        }

        private bool AddProvider(ProviderRecord record)
        {
            if (string.IsNullOrEmpty(record.FeatureId))
            {
                Warn(null, "provider on '" + record.Channel + "' rejected: the feature id is empty");
                return false;
            }
            if (!ChannelFamilies.IsValidChannelName(record.Channel, false))
            {
                Warn(record.FeatureId, "provider on '" + record.Channel + "' rejected: it is not a valid channel name");
                return false;
            }

            Bucket bucket = BucketFor(record.Channel);
            foreach (ProviderRecord existing in bucket.Providers)
            {
                if (string.Equals(existing.FeatureId, record.FeatureId, StringComparison.Ordinal) && Equals(existing.Identity, record.Identity)) return false;
            }

            var grown = new ProviderRecord[bucket.Providers.Length + 1];
            Array.Copy(bucket.Providers, grown, bucket.Providers.Length);
            grown[grown.Length - 1] = record;
            bucket.Providers = grown;
            providerCount++;

            Touch(record.Channel);
            return true;
        }

        private static bool BucketHasFeature(Bucket bucket, string featureId)
        {
            foreach (ModifierEntry entry in bucket.Entries)
            {
                if (string.Equals(entry.FeatureId, featureId, StringComparison.Ordinal)) return true;
            }
            foreach (ProviderRecord provider in bucket.Providers)
            {
                if (string.Equals(provider.FeatureId, featureId, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        // ---- internals: notifications ----

        /// <summary>The content of a channel changed: new version, no cached values, event.</summary>
        private void Touch(string channel)
        {
            version++;
            cache.Invalidate(channel);
            RaiseChanged(channel);
        }

        private void RaiseForAllInUse()
        {
            List<string> names = null;
            foreach (Bucket bucket in buckets.Values)
            {
                if (bucket.IsEmpty) continue;
                if (names == null) names = new List<string>();
                names.Add(bucket.Name);
            }
            if (names == null) return;
            names.Sort(string.CompareOrdinal);
            foreach (string name in names) RaiseChanged(name);
        }

        private void RaiseChanged(string channel)
        {
            Action<string> handlers = ChannelChanged;
            if (handlers == null) return;

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<string>)handler)(channel);
                }
                catch (Exception ex)
                {
                    Warn(null, "a ChannelChanged handler failed for '" + channel + "': " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        private void Warn(string featureId, string message)
        {
            Action<string, string> handler = Warning;
            if (handler == null) return;
            try
            {
                handler(featureId, message);
            }
            catch (Exception)
            {
                // A broken log sink must not break the formula that triggered the warning.
            }
        }
    }
}
