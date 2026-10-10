using System;
using System.Collections.Generic;

namespace PTMod.Core.Logic.Modifiers
{
    /// <summary>
    /// Cache of channel values for one game day (FR-12: "result is cached until the end of the game day"). The key is
    /// the channel and <see cref="Key"/>: company, CPU, segment, contract id. The registry decides what may be cached
    /// (never a design query, never a channel with <c>Cacheable = false</c>) and clears the cache on a new day, on a
    /// change of entries, providers or feature states, and on <c>Invalidate</c>. Not thread-safe: main thread only.
    /// </summary>
    public sealed class ModifierCache
    {
        /// <summary>Bound on cached values of one channel. A channel that reaches it is emptied: a day has far fewer distinct keys.</summary>
        public const int MaxEntriesPerChannel = 50000;

        /// <summary>Everything besides the channel that a cached value depends on (<see cref="ModifierQuery"/> without the design flag and the host context).</summary>
        public readonly struct Key : IEquatable<Key>
        {
            public Key(string companyKey, int cpuSaveId, Segment segment, int contractId)
            {
                CompanyKey = companyKey;
                CpuSaveId = cpuSaveId;
                Segment = segment;
                ContractId = contractId;
            }

            public string CompanyKey { get; }

            public int CpuSaveId { get; }

            public Segment Segment { get; }

            public int ContractId { get; }

            public static Key Of(ModifierQuery query)
            {
                return new Key(query.CompanyKey, query.CpuSaveId, query.Segment, query.ContractId);
            }

            public bool Equals(Key other)
            {
                return CpuSaveId == other.CpuSaveId
                       && ContractId == other.ContractId
                       && Segment == other.Segment
                       && string.Equals(CompanyKey, other.CompanyKey, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is Key && Equals((Key)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = CompanyKey == null ? 0 : CompanyKey.GetHashCode();
                    hash = (hash * 397) ^ CpuSaveId;
                    hash = (hash * 397) ^ (int)Segment;
                    hash = (hash * 397) ^ ContractId;
                    return hash;
                }
            }
        }

        private readonly Dictionary<string, Dictionary<Key, float>> channels =
            new Dictionary<string, Dictionary<Key, float>>(StringComparer.Ordinal);

        /// <summary>Lookups that found a value since creation (diagnostics and tests).</summary>
        public int Hits { get; private set; }

        /// <summary>Lookups that found nothing.</summary>
        public int Misses { get; private set; }

        /// <summary>Number of cached values in all channels.</summary>
        public int Count
        {
            get
            {
                int total = 0;
                foreach (Dictionary<Key, float> values in channels.Values) total += values.Count;
                return total;
            }
        }

        /// <summary>Number of cached values of one channel.</summary>
        public int CountOf(string channel)
        {
            Dictionary<Key, float> values;
            return channel != null && channels.TryGetValue(channel, out values) ? values.Count : 0;
        }

        public bool TryGet(string channel, Key key, out float value)
        {
            Dictionary<Key, float> values;
            if (channel != null && channels.TryGetValue(channel, out values) && values.TryGetValue(key, out value))
            {
                Hits++;
                return true;
            }
            Misses++;
            value = 0f;
            return false;
        }

        public void Store(string channel, Key key, float value)
        {
            if (channel == null) return;

            Dictionary<Key, float> values;
            if (!channels.TryGetValue(channel, out values))
            {
                values = new Dictionary<Key, float>();
                channels[channel] = values;
            }
            else if (values.Count >= MaxEntriesPerChannel && !values.ContainsKey(key))
            {
                values.Clear();
            }
            values[key] = value;
        }

        /// <summary>Drops every value of one channel.</summary>
        public void Invalidate(string channel)
        {
            if (channel != null) channels.Remove(channel);
        }

        /// <summary>
        /// Drops the values of one company (key null: the values asked without a company) in one channel, or in every channel
        /// when <paramref name="channel"/> is null. Other companies keep their values.
        /// </summary>
        public void Invalidate(string channel, string companyKey)
        {
            if (channel != null)
            {
                Dictionary<Key, float> values;
                if (channels.TryGetValue(channel, out values)) RemoveCompany(values, companyKey);
                return;
            }
            foreach (Dictionary<Key, float> values in channels.Values) RemoveCompany(values, companyKey);
        }

        /// <summary>Drops everything (a new day, a change of feature states).</summary>
        public void Clear()
        {
            channels.Clear();
        }

        private static void RemoveCompany(Dictionary<Key, float> values, string companyKey)
        {
            List<Key> doomed = null;
            foreach (Key key in values.Keys)
            {
                if (!string.Equals(key.CompanyKey, companyKey, StringComparison.Ordinal)) continue;
                if (doomed == null) doomed = new List<Key>();
                doomed.Add(key);
            }
            if (doomed == null) return;
            foreach (Key key in doomed) values.Remove(key);
        }
    }
}
