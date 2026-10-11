using System;
using System.Collections.Generic;
using System.Text;
using PTMod.Core;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// Where the aggregator hands its product to the game: <c>ModModifiers</c> in the game (provider of the feature <c>event-system</c> with a reason, and
    /// <c>ModModifiers.Invalidate</c>), the core's own <c>ChannelRegistry</c> or a recorder in the tests.
    /// </summary>
    public interface IModifierChannelSink
    {
        /// <summary>
        /// Registers the provider of a channel: <c>ModModifiers.RegisterReasonedProvider("event-system", channel, ctx =&gt; provider(ctx.CompanyKey))</c>. The
        /// aggregator calls it once per channel and session.
        /// </summary>
        void RegisterProvider(string channel, Func<string, ProviderResult> provider);

        /// <summary>Drops the cached values of the channel for one company; <paramref name="companyKey"/> null - for every company (a global contribution changed).</summary>
        void Invalidate(string channel, string companyKey);
    }

    /// <summary>
    /// The contributions of the events to the channels of <c>ModModifiers</c> (D4, D13; design.md "Агрегатор модификаторов"). A timed effect sets its current
    /// multiplier for a company or for <c>global</c> (<see cref="Set"/>), the aggregator multiplies the contributions and holds the product to the limits of the
    /// channel from the <c>channelLimits</c> of the packs (the narrowest template, <c>popularity.gain</c> [0.7; 1.6]):
    /// <code>Current(channel, key) = clamp( Π m_global × Π m_key, LimitMin(channel), LimitMax(channel) )</code>
    /// <list type="bullet">
    /// <item><b>One provider per channel</b>, registered the first time an effect uses the channel and never removed: its reason is <c>es.reason.events</c>
    /// with the names of the events behind it, so <c>ModModifiers.Explain</c> shows one line "События: ...".</item>
    /// <item><b>Every change of a contribution</b> (a new value, a removal, a pack frozen or thawed, everything dropped) invalidates the cache of the channel for
    /// that company, or for all companies for <c>global</c> (AC-5.4): a choice made on pause works at once.</item>
    /// <item><b>Freezing</b> (D10): <see cref="Suspend"/> takes the contributions of a pack out of the product until <see cref="Resume"/>. Switching the whole
    /// feature off needs nothing here: the core skips the provider of a feature that is not Enabled, and the contributions stay.</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    public sealed class ModifierAggregator : IEffectModifiers
    {
        /// <summary>The reason of the provider in <c>ModModifiers.Explain</c>: "События: {0}".</summary>
        public const string ReasonKey = "es.reason.events";

        private readonly IModifierChannelSink _sink;
        private readonly Func<EventConfig> _config;
        private readonly Func<AppliedEffect, string> _packOf;
        private readonly Func<AppliedEffect, string> _nameOf;

        private readonly Dictionary<string, Contribution> _byKey = new Dictionary<string, Contribution>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Contribution>> _byChannel = new Dictionary<string, List<Contribution>>(StringComparer.Ordinal);
        private readonly HashSet<string> _registered = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _registeredOrder = new List<string>();
        private readonly HashSet<string> _suspended = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _reasons = new Dictionary<string, string>(StringComparer.Ordinal);

        private sealed class Contribution
        {
            public string Key;
            public AppliedEffect Effect;
            public string EffectId;
            public string Channel;
            public string CompanyKey;
            public float Multiplier;
            public string PackId;
        }

        /// <param name="sink">Where the provider goes and what is invalidated.</param>
        /// <param name="config">The merged <c>config.json</c> of the packs (the limits of the channels), read at every use.</param>
        /// <param name="packOf">The pack of an effect (by its instance), for <see cref="Suspend"/>; null or empty - the effect belongs to no pack.</param>
        /// <param name="nameOf">The name of the event of an effect as the player reads it, for the reason of the provider.</param>
        public ModifierAggregator(IModifierChannelSink sink, Func<EventConfig> config, Func<AppliedEffect, string> packOf = null, Func<AppliedEffect, string> nameOf = null)
        {
            if (sink == null) throw new ArgumentNullException("sink");
            _sink = sink;
            _config = config ?? (() => EventConfig.Defaults);
            _packOf = packOf ?? (effect => null);
            _nameOf = nameOf ?? (effect => effect != null ? effect.id : string.Empty);
        }

        /// <summary>The channels whose provider is registered, in the order of registration.</summary>
        public IList<string> Channels
        {
            get { return _registeredOrder.AsReadOnly(); }
        }

        /// <summary>Contributions now held (frozen ones included).</summary>
        public int Count
        {
            get { return _byKey.Count; }
        }

        // ---- IEffectModifiers ----

        public void Set(AppliedEffect effect, string channel, string companyKey, float multiplier)
        {
            if (effect == null || string.IsNullOrEmpty(effect.id) || string.IsNullOrEmpty(channel)) return;
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier < 0f) return;
            string company = string.IsNullOrEmpty(companyKey) ? EventCompanyKeys.Global : companyKey;

            EnsureProvider(channel);

            string key = effect.id + "\n" + channel + "\n" + company;
            Contribution existing;
            if (_byKey.TryGetValue(key, out existing))
            {
                existing.Effect = effect;
                if (existing.Multiplier == multiplier) return;   // nothing changed, nothing to invalidate
                existing.Multiplier = multiplier;
                Changed(existing);
                return;
            }

            var contribution = new Contribution
            {
                Key = key,
                Effect = effect,
                EffectId = effect.id,
                Channel = channel,
                CompanyKey = company,
                Multiplier = multiplier,
                PackId = SafePackOf(effect),
            };
            _byKey.Add(key, contribution);
            List<Contribution> list;
            if (!_byChannel.TryGetValue(channel, out list))
            {
                list = new List<Contribution>();
                _byChannel.Add(channel, list);
            }
            list.Add(contribution);
            Changed(contribution);
        }

        public void Clear(AppliedEffect effect)
        {
            if (effect == null || string.IsNullOrEmpty(effect.id)) return;

            List<Contribution> removed = null;
            foreach (Contribution contribution in _byKey.Values)
            {
                if (!string.Equals(contribution.EffectId, effect.id, StringComparison.Ordinal)) continue;
                if (removed == null) removed = new List<Contribution>();
                removed.Add(contribution);
            }
            if (removed == null) return;

            removed.Sort(ByChannelAndCompany);
            foreach (Contribution contribution in removed) Remove(contribution);
            foreach (Contribution contribution in removed) Changed(contribution);
        }

        public bool WasClamped(string channel, string companyKey)
        {
            bool any;
            double raw = Raw(channel, companyKey, out any);
            if (!any) return false;
            return raw < LimitMin(channel) || raw > LimitMax(channel);
        }

        public float LimitMin(string channel)
        {
            EventConfig config = _config() ?? EventConfig.Defaults;
            return config.LimitMin(channel);
        }

        public float LimitMax(string channel)
        {
            EventConfig config = _config() ?? EventConfig.Defaults;
            return config.LimitMax(channel);
        }

        // ---- the provider ----

        /// <summary>
        /// The product of the contributions on the channel for the company (its own and the global ones; null - the global ones only), not frozen, held to the
        /// limits of the channel. 1 when there is none.
        /// </summary>
        public float Current(string channel, string companyKey)
        {
            bool any;
            double raw = Raw(channel, companyKey, out any);
            if (!any) return 1f;

            double lo = LimitMin(channel), hi = LimitMax(channel);
            if (raw < lo) raw = lo;
            if (raw > hi) raw = hi;
            return (float)raw;
        }

        /// <summary>The names of the events behind the product (comma separated, each once) for the reason of <c>Explain</c>.</summary>
        public string ReasonArgs(string channel, string companyKey)
        {
            string cacheKey = channel + "\n" + (companyKey ?? string.Empty);
            string text;
            if (_reasons.TryGetValue(cacheKey, out text)) return text;

            var names = new List<string>();
            List<Contribution> list;
            if (channel != null && _byChannel.TryGetValue(channel, out list))
            {
                foreach (Contribution contribution in list)
                {
                    if (!Applies(contribution, companyKey)) continue;
                    string name = SafeNameOf(contribution.Effect);
                    if (!string.IsNullOrEmpty(name) && !names.Contains(name)) names.Add(name);
                }
            }

            var builder = new StringBuilder();
            foreach (string name in names)
            {
                if (builder.Length > 0) builder.Append(", ");
                builder.Append(name);
            }
            text = builder.ToString();
            _reasons[cacheKey] = text;
            return text;
        }

        /// <summary>The names of the events are built anew (the language changed).</summary>
        public void RefreshNames()
        {
            _reasons.Clear();
        }

        // ---- freezing ----

        /// <summary>Takes the contributions of the pack out of the products until <see cref="Resume"/> (D10); they are kept, nothing is unregistered.</summary>
        public void Suspend(string packId)
        {
            if (string.IsNullOrEmpty(packId) || !_suspended.Add(packId)) return;
            InvalidatePack(packId);
        }

        /// <summary>Puts the contributions of the pack back.</summary>
        public void Resume(string packId)
        {
            if (string.IsNullOrEmpty(packId) || !_suspended.Remove(packId)) return;
            InvalidatePack(packId);
        }

        public bool IsSuspended(string packId)
        {
            return !string.IsNullOrEmpty(packId) && _suspended.Contains(packId);
        }

        /// <summary>
        /// Drops every contribution (another save is loaded, a new game): the channels that had one are invalidated. The providers stay registered (the core
        /// keeps them for the session) and the frozen packs stay frozen (that is the state of their switches, not of the save).
        /// </summary>
        public void ClearAll()
        {
            if (_byKey.Count == 0) return;

            var touched = new List<Contribution>(_byKey.Values);
            touched.Sort(ByChannelAndCompany);
            _byKey.Clear();
            _byChannel.Clear();
            _reasons.Clear();

            var done = new HashSet<string>(StringComparer.Ordinal);
            foreach (Contribution contribution in touched)
            {
                string key = contribution.Channel + "\n" + contribution.CompanyKey;
                if (done.Add(key)) Invalidate(contribution.Channel, contribution.CompanyKey);
            }
        }

        // ---- helpers ----

        private double Raw(string channel, string companyKey, out bool any)
        {
            any = false;
            double product = 1.0;
            List<Contribution> list;
            if (channel == null || !_byChannel.TryGetValue(channel, out list)) return product;

            foreach (Contribution contribution in list)
            {
                if (!Applies(contribution, companyKey)) continue;
                product *= contribution.Multiplier;
                any = true;
            }
            return product;
        }

        private bool Applies(Contribution contribution, string companyKey)
        {
            if (contribution.PackId != null && _suspended.Contains(contribution.PackId)) return false;
            if (string.Equals(contribution.CompanyKey, EventCompanyKeys.Global, StringComparison.Ordinal)) return true;
            return companyKey != null && string.Equals(contribution.CompanyKey, companyKey, StringComparison.Ordinal);
        }

        private void EnsureProvider(string channel)
        {
            if (!_registered.Add(channel)) return;
            _registeredOrder.Add(channel);
            string name = channel;
            _sink.RegisterProvider(name, companyKey => new ProviderResult(Current(name, companyKey), ReasonKey, new[] { ReasonArgs(name, companyKey) }));
        }

        private void Remove(Contribution contribution)
        {
            _byKey.Remove(contribution.Key);
            List<Contribution> list;
            if (_byChannel.TryGetValue(contribution.Channel, out list))
            {
                list.Remove(contribution);
                if (list.Count == 0) _byChannel.Remove(contribution.Channel);
            }
        }

        private void Changed(Contribution contribution)
        {
            Invalidate(contribution.Channel, contribution.CompanyKey);
        }

        private void Invalidate(string channel, string companyKey)
        {
            DropReasons(channel);
            bool global = string.Equals(companyKey, EventCompanyKeys.Global, StringComparison.Ordinal);
            _sink.Invalidate(channel, global ? null : companyKey);
        }

        private void InvalidatePack(string packId)
        {
            var done = new HashSet<string>(StringComparer.Ordinal);
            var all = new List<Contribution>(_byKey.Values);
            all.Sort(ByChannelAndCompany);
            foreach (Contribution contribution in all)
            {
                if (!string.Equals(contribution.PackId, packId, StringComparison.Ordinal)) continue;
                string key = contribution.Channel + "\n" + contribution.CompanyKey;
                if (done.Add(key)) Invalidate(contribution.Channel, contribution.CompanyKey);
            }
        }

        private void DropReasons(string channel)
        {
            if (_reasons.Count == 0) return;
            var stale = new List<string>();
            string prefix = channel + "\n";
            foreach (string key in _reasons.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal)) stale.Add(key);
            }
            foreach (string key in stale) _reasons.Remove(key);
        }

        /// <summary>A fixed order for the invalidations of several contributions: by channel, then by company.</summary>
        private static int ByChannelAndCompany(Contribution a, Contribution b)
        {
            int byChannel = string.CompareOrdinal(a.Channel, b.Channel);
            return byChannel != 0 ? byChannel : string.CompareOrdinal(a.CompanyKey, b.CompanyKey);
        }

        private string SafePackOf(AppliedEffect effect)
        {
            try
            {
                string pack = _packOf(effect);
                return string.IsNullOrEmpty(pack) ? null : pack;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private string SafeNameOf(AppliedEffect effect)
        {
            try
            {
                return _nameOf(effect);
            }
            catch (Exception)
            {
                return effect != null ? effect.id : null;
            }
        }
    }
}
