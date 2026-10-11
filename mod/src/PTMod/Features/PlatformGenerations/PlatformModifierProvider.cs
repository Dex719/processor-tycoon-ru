using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PTMod.Core;
using GameContract = ProcessorTycoon.ContractSystem.Contract;
using GameCpu = ProcessorTycoon.Hardware.Cpu;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// The two providers of the feature (design, Component 6): the multiplier of the rating of a CPU for the channel <c>market.rating.cpu</c> and
    /// the multiplier of the signing chance of an offer for <c>contract.signChance</c>. The only way the feature changes the game's formulas
    /// (D1): both are registered with <c>ModModifiers</c> once, in <see cref="Register"/>, and are never registered again - the core calls a
    /// provider of a feature that is not Enabled never, and the same provider again when it is.
    /// <para>
    /// <b>Rating</b> (hot path, the market tick): a lookup in a cache of one entry per CPU, rebuilt once a month (<see cref="RebuildCache"/>) and
    /// for one CPU when it is released or moves to another owner (<see cref="UpdateCpu"/>); no list is walked, nothing is allocated, the entry of a
    /// CPU that appeared between two rebuilds is made on its first question. <b>Chance</b>: "per contract" when the question carries a contract
    /// (its preference, given on the spot if it has none), otherwise the "market" average; the reason and its arguments come from
    /// <see cref="PlatformReasonArgs"/>, prepared ahead.
    /// </para>
    /// Both answer 1 while the new game's fast-forward runs and while their switch is off (<see cref="RefreshSettings"/>); a failure inside is
    /// caught, counted against the feature, and answers 1 (NFR-2). The same code serves the player's and the AI's CPUs (FR-5 item 6).
    /// </summary>
    internal sealed class PlatformModifierProvider
    {
        /// <summary>The fixed reason of the rating channel (<c>ModModifiers.Explain</c> shows it next to the multiplier).</summary>
        public const string RatingReasonKey = "pg.reason.rating";

        private const string Feature = PlatformGenerationsFeature.FeatureId;
        private const string RatingSite = "provider/rating";
        private const string ChanceSite = "provider/chance";
        private const string CacheSite = "provider/cache";

        /// <summary>One CPU's answers, made once a month. A class so the dictionary holds a reference and a rebuild changes it in place.</summary>
        private sealed class CpuEffect
        {
            public float Consumer = 1f;
            public float Industries = 1f;
            public int GenOrder = -1;
            public string GenId;
            public bool FullPlatform;
            public double EffShare;
            public int Stamp;
        }

        private sealed class ReferenceComparer : IEqualityComparer<GameCpu>
        {
            public bool Equals(GameCpu x, GameCpu y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(GameCpu cpu)
            {
                return RuntimeHelpers.GetHashCode(cpu);
            }
        }

        private readonly PlatformGenerationsFeature _feature;
        private readonly PlatformEffectEngine _engine = new PlatformEffectEngine();
        private readonly PlatformReasonArgs _reasons = new PlatformReasonArgs();
        private readonly Dictionary<GameCpu, CpuEffect> _cpus = new Dictionary<GameCpu, CpuEffect>(new ReferenceComparer());
        private readonly List<GameCpu> _snapshot = new List<GameCpu>();
        private readonly List<GameCpu> _gone = new List<GameCpu>();
        private readonly List<GenAdoptionInput> _inputs = new List<GenAdoptionInput>();

        private bool _registered;
        private bool _ready;
        private bool _ratingOn = true;
        private bool _oemOn = true;
        private int _stamp;
        private int _rebuiltCount;   // the CPUs the last rebuild worked on (for the timer)
        private readonly Func<GenerationDef, string> _nameOf = gen => ModL10n.Resolve(gen.name);

        public PlatformModifierProvider(PlatformGenerationsFeature feature)
        {
            _feature = feature;
        }

        /// <summary>The cache has been built (the generations were loaded and a rebuild ran); before that every answer is 1.</summary>
        public bool IsReady
        {
            get { return _ready; }
        }

        /// <summary>The two providers were registered with <c>ModModifiers</c> (<see cref="Register"/> ran); the self-check looks at it.</summary>
        public bool IsRegistered
        {
            get { return _registered; }
        }

        /// <summary>How many CPUs have an entry (diagnostics and tests).</summary>
        public int CachedCount
        {
            get { return _cpus.Count; }
        }

        /// <summary>The generations of the day with their adoption, newest first, as the last rebuild saw them (for the window and the diagnostics).</summary>
        public IReadOnlyList<GenAdoptionInput> Inputs
        {
            get { return _inputs; }
        }

        /// <summary>
        /// Registers the two providers - once for the session, from <c>Register</c> of the feature: <c>market.rating.cpu</c> with the fixed
        /// reason <see cref="RatingReasonKey"/>, <c>contract.signChance</c> with the reasons of its answers. A second call does nothing.
        /// </summary>
        public void Register()
        {
            if (_registered) return;
            _registered = true;

            ModModifiers.RegisterProvider(Feature, ModChannels.MarketRatingCpu, Rating, RatingReasonKey);
            ModModifiers.RegisterReasonedProvider(Feature, ModChannels.ContractSignChance, Chance);
        }

        /// <summary>
        /// Reads the two switches of the hub (FR-13 item 2) into fields, so that the hot path does not look them up, and drops the cached values
        /// of the channels whose switch changed (the effect appears at once). Called from <c>Register</c> and on <c>ModSettings.Changed</c>.
        /// </summary>
        public void RefreshSettings()
        {
            bool rating = PlatformSettings.RatingEffects;
            bool oem = PlatformSettings.OemEffects;
            bool ratingChanged = rating != _ratingOn;
            bool oemChanged = oem != _oemOn;
            _ratingOn = rating;
            _oemOn = oem;

            if (ratingChanged) ModModifiers.Invalidate(ModChannels.MarketRatingCpu);
            if (oemChanged) ModModifiers.Invalidate(ModChannels.ContractSignChance);
        }

        /// <summary>Prepares the names of the generations again (the language was changed).</summary>
        public void RefreshReasons()
        {
            _reasons.Rebuild(_feature.Catalog, _inputs, _nameOf);
        }

        // ---- the providers ----

        /// <summary>
        /// <c>market.rating.cpu</c> (FR-5): the multiplier of the CPU in the segment of the question, <c>ConsumerMult</c> for HighEnd, MidRange, LowEnd
        /// and without a segment, <c>IndustriesMult</c> for <see cref="Segment.Industries"/>. 1 while the fast-forward runs, with the switch off,
        /// for a CPU outside the generations or without a company, and until the cache is ready. Called only for an Enabled feature (the core
        /// skips the others). No allocation on a CPU that has an entry.
        /// </summary>
        public float Rating(ModifierContext ctx)
        {
            try
            {
                _feature.Perf.RatingCalls++;
                if (!_ratingOn || ModTicks.IsFastForwarding) return 1f;

                GameCpu cpu = ctx.Cpu;
                CpuEffect effect;
                if (ReferenceEquals(cpu, null) || !TryGet(cpu, out effect)) return 1f;

                return ctx.Segment == Segment.Industries ? effect.Industries : effect.Consumer;
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(Feature, RatingSite, ex);
                return 1f;
            }
        }

        /// <summary>
        /// <c>contract.signChance</c> (FR-8): the chance multiplier of the CPU for the offer, with the reason. The mode follows the question: a
        /// contract in it (patches P8 and P23) means "per contract" - its preference, given now if it has none - and none means the "market" average
        /// (the setting <c>oem.mode = marketAverage</c> forces it). 1 while the fast-forward runs and with the switch off.
        /// </summary>
        public ProviderResult Chance(ModifierContext ctx)
        {
            try
            {
                _feature.Perf.ChanceCalls++;
                if (!_oemOn || ModTicks.IsFastForwarding) return new ProviderResult(1f);

                GameCpu cpu = ctx.Cpu;
                CpuEffect effect;
                if (ReferenceEquals(cpu, null) || !TryGet(cpu, out effect)) return new ProviderResult(1f);

                PlatformBalance balance = _feature.Balance;
                var contract = new EffectContract();
                GameContract offered = ctx.Contract;
                if (!ReferenceEquals(offered, null))
                {
                    contract.Present = true;
                    contract.Key = GameAdapter.ContractKey(offered);
                    contract.ContractType = GameAdapter.ContractTypeOf(offered);
                    if (balance.Oem.Mode == OemMode.Auto) _feature.ContractPrefs.Ensure(offered, contract.Key, contract.ContractType);
                }

                var info = new EffectCpu
                {
                    GenOrder = effect.GenOrder >= 0 ? (int?)effect.GenOrder : null,
                    GenId = effect.GenId,
                    FullPlatform = effect.FullPlatform,
                    EffShare = effect.EffShare,
                };
                ChanceResult result = _engine.Chance(info, contract, balance.Oem.Mode, _feature.Preferences, _inputs, balance);
                return _reasons.Of(result);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(Feature, ChanceSite, ex);
                return new ProviderResult(1f);
            }
        }

        // ---- the cache ----

        private bool TryGet(GameCpu cpu, out CpuEffect effect)
        {
            if (_cpus.TryGetValue(cpu, out effect)) return true;

            // A CPU that came after the last rebuild (a variant, a CPU of a company that was just founded): its entry is made now, once.
            if (!_ready || !_feature.CatalogReady || ModTicks.IsFastForwarding) return false;

            effect = new CpuEffect();
            Fill(cpu, effect, GameAdapter.Today());
            effect.Stamp = _stamp;
            _cpus.Add(cpu, effect);
            return true;
        }

        /// <summary>
        /// Rebuilds the entry of every CPU of the market (FR-5 item 5): once a month, at the end of the fast-forward, on <c>GameReady</c>, when
        /// the chronology changed under the CPUs (a launch, an early adopter, a new socket). Also prepares the adoption of the generations on the
        /// day and the arguments of the reasons, and drops the cached values of both channels. Does nothing until the generations are loaded.
        /// </summary>
        public void RebuildCache()
        {
            if (!_feature.CatalogReady) return;

            long started = PlatformPerf.Now();
            RebuildCacheCore();
            _feature.Perf.Stop(PerfOp.RebuildCache, started, _rebuiltCount);
            _feature.ReportPerf(PerfOp.RebuildCache);
        }

        private void RebuildCacheCore()
        {
            PlatformGenerationsFeature feature = _feature;
            _rebuiltCount = 0;

            GameDate today = GameAdapter.Today();
            OemAdoptionModel.BuildInputs(feature.Catalog, feature.Timeline, feature.InstalledBase, today, _inputs);
            RefreshReasons();

            if (!GameAdapter.CollectCpus(_snapshot))
            {
                _cpus.Clear();
                _ready = true;
                return;
            }

            int stamp = ++_stamp;
            int outOfRange = 0;
            PlatformBalance.RatingBalance rating = feature.Balance.Rating;
            for (int i = 0; i < _snapshot.Count; i++)
            {
                GameCpu cpu = _snapshot[i];
                if (ReferenceEquals(cpu, null)) continue;

                CpuEffect effect;
                if (!_cpus.TryGetValue(cpu, out effect))
                {
                    effect = new CpuEffect();
                    _cpus.Add(cpu, effect);
                }

                try
                {
                    Fill(cpu, effect, today);
                }
                catch (Exception ex)
                {
                    ModHooks.ReportFault(Feature, CacheSite, ex);
                    Neutralize(effect);
                }
                effect.Stamp = stamp;

                if (!(effect.Consumer >= rating.MinMult && effect.Consumer <= rating.MaxMult)
                    || !(effect.Industries >= rating.MinMult && effect.Industries <= rating.MaxMult))
                {
                    outOfRange++;
                }
            }

            _rebuiltCount = _snapshot.Count;
            if (_cpus.Count > _snapshot.Count)
            {
                _gone.Clear();
                foreach (KeyValuePair<GameCpu, CpuEffect> pair in _cpus)
                {
                    if (pair.Value.Stamp != stamp) _gone.Add(pair.Key);
                }
                for (int i = 0; i < _gone.Count; i++) _cpus.Remove(_gone[i]);
                _gone.Clear();
            }
            _snapshot.Clear();
            _ready = true;

            if (outOfRange > 0) ModLog.Error(Feature, "provider: " + outOfRange + " rating multiplier(s) outside [minMult; maxMult] after a rebuild");

            ModModifiers.Invalidate(ModChannels.MarketRatingCpu);
            ModModifiers.Invalidate(ModChannels.ContractSignChance);
        }

        /// <summary>
        /// Makes the entry of one CPU again (FR-5 item 8): it was released, or it moved to another owner. The cached values of the CPU's company
        /// (and of <paramref name="alsoCompanyKey"/>, the previous owner) are dropped in both channels, so the next question gets the new answer.
        /// </summary>
        public void UpdateCpu(GameCpu cpu, string alsoCompanyKey = null)
        {
            if (ReferenceEquals(cpu, null) || !_ready || !_feature.CatalogReady) return;

            CpuEffect effect;
            if (!_cpus.TryGetValue(cpu, out effect))
            {
                effect = new CpuEffect();
                _cpus.Add(cpu, effect);
            }
            Fill(cpu, effect, GameAdapter.Today());
            effect.Stamp = _stamp;

            string key = GameAdapter.CompanyKeyOf(cpu);
            InvalidateCompany(key);
            if (alsoCompanyKey != null && !string.Equals(alsoCompanyKey, key, StringComparison.Ordinal)) InvalidateCompany(alsoCompanyKey);
        }

        /// <summary>Forgets the cache (a new game, a load, leaving the scene: the CPU objects are gone). The next answer is 1 until a rebuild.</summary>
        public void Clear()
        {
            _cpus.Clear();
            _snapshot.Clear();
            _inputs.Clear();
            _reasons.Clear();
            _ready = false;
        }

        private static void InvalidateCompany(string companyKey)
        {
            ModModifiers.Invalidate(ModChannels.MarketRatingCpu, companyKey);
            ModModifiers.Invalidate(ModChannels.ContractSignChance, companyKey);
        }

        /// <summary>The numbers of one CPU: the generation of its socket, the full platform, the multipliers of the rating and the share behind them.</summary>
        private void Fill(GameCpu cpu, CpuEffect effect, GameDate today)
        {
            Neutralize(effect);

            PlatformGenerationsFeature feature = _feature;
            SocketId socket;
            if (!GameAdapter.TryGetSocket(cpu, out socket)) return;

            GenerationDef gen = feature.Catalog.BySocket(socket);
            if (gen == null) return;

            string company = GameAdapter.CompanyKeyOf(cpu);
            if (company == null) return;

            effect.GenOrder = gen.order;
            effect.GenId = gen.id;
            effect.FullPlatform = feature.Catalog.IsFullPlatform(gen, GameAdapter.MemoryNameOf(cpu));

            RatingBreakdown rating = _engine.RatingFor(company, socket, gen.id, feature.InstalledBase, feature.Timeline, today, feature.Balance);
            effect.Consumer = rating.ConsumerMult;
            effect.Industries = rating.IndustriesMult;
            effect.EffShare = rating.EffShare;
        }

        private static void Neutralize(CpuEffect effect)
        {
            effect.Consumer = 1f;
            effect.Industries = 1f;
            effect.GenOrder = -1;
            effect.GenId = null;
            effect.FullPlatform = false;
            effect.EffShare = 0;
        }
    }
}
