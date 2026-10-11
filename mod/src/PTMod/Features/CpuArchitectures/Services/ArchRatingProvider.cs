using System;
using System.Collections.Generic;
using PTMod.Core;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The provider of the standard channel <c>market.rating.cpu</c> (D5, FR-6; design.md Component 5): one provider for every segment, which the
    /// core's patch P4 (<c>Market.CalculateRatings</c>) asks with the CPU and the segment in the context. The value is
    /// <see cref="ArchRatingMath.Contribution"/> of the family of the CPU (<see cref="ArchRegistry.ResolveCpu"/>): the affinity with the segment, the
    /// ageing of this year, at stage 2 support, ecosystem (<c>f_eco</c>, <see cref="EcosystemService"/>) and extensions (<c>f_ext</c>), then the strength knob of the hub. The reason is <c>arch.reason.rating</c>
    /// with the name of the family, so <c>ModModifiers.Explain</c> shows the contribution of the architecture.
    /// <list type="bullet">
    /// <item>1.0 while the feature is neutral (off or fast-forwarding), for <see cref="Segment.Any"/>, for a context without a CPU and for a family
    /// the catalog does not know (FR-6 items 1, 4).</item>
    /// <item>The core caches the value per day and key (company, CPU, segment); the feature drops the cache when the value may change within a
    /// day or a month: a CPU bound, a company's state changed, the knob moved, a new month (<see cref="InvalidateAll"/>, <see cref="InvalidateCompany"/>).</item>
    /// <item>Registered once in <c>Register</c>; <c>OnEnabled</c>/<c>OnDisabled</c> do not touch it (the core skips the provider of a disabled feature).
    /// No channel is declared by the feature. O(1) per call: three dictionary lookups, no allocation (the reason arguments are kept per family).</item>
    /// </list>
    /// </summary>
    internal sealed class ArchRatingProvider
    {
        /// <summary>The <c>ModL10n</c> key of the reason ("Архитектура {0}").</summary>
        public const string ReasonKey = "arch.reason.rating";

        private const string ProvideSite = "rating/provide";

        private readonly ArchitecturesFeature _feature;
        private readonly Dictionary<string, string[]> _reasonArgs = new Dictionary<string, string[]>(StringComparer.Ordinal);
        private readonly RatingInputs _inputs = new RatingInputs();   // reused: the provider runs on the main thread only
        private bool _registered;
        private float _strength = 1f;

        public ArchRatingProvider(ArchitecturesFeature feature)
        {
            if (feature == null) throw new ArgumentNullException("feature");
            _feature = feature;
        }

        /// <summary>Registers the provider on <c>market.rating.cpu</c>; a second call does nothing.</summary>
        public void Register()
        {
            if (_registered) return;
            _registered = true;
            ReadStrength();
            ModModifiers.RegisterReasonedProvider(ArchitecturesFeature.FeatureId, ModChannels.MarketRatingCpu, Provide);
        }

        /// <summary>Reads the knob of the hub again (<c>ratingStrength</c>); the next values use it.</summary>
        public void ReadStrength()
        {
            float strength = _feature.RatingStrength;
            _strength = float.IsNaN(strength) ? 1f : strength;
        }

        /// <summary>The names of the families in the reasons follow the language: the kept arguments are made again.</summary>
        public void ResetReasons()
        {
            _reasonArgs.Clear();
        }

        // ---- the provider ----

        private ProviderResult Provide(ModifierContext ctx)
        {
            if (!ModHooks.CanRun(ArchitecturesFeature.FeatureId, ProvideSite)) return 1f;
            long started = ArchPerf.Begin();   // NFR-1: all the calls of a game day together take 0.2 ms
            try
            {
                return Compute(ctx);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(ArchitecturesFeature.FeatureId, ProvideSite, ex);
                return 1f;
            }
            finally
            {
                ArchPerf.End(ArchPerfMeter.Rating, started);
            }
        }

        private ProviderResult Compute(ModifierContext ctx)
        {
            if (_feature.IsNeutralNow || (_feature.Parts & ArchParts.Market) == 0) return 1f;

            Segment segment = ctx.Segment;
            GameCpu cpu = ctx.Cpu;
            if (segment == Segment.Any || cpu == null) return 1f;

            ArchRegistry registry = _feature.Registry;
            ArchCatalog catalog = _feature.Catalog;
            if (registry == null || catalog == null) return 1f;

            ArchFamily family = catalog.Get(registry.ResolveCpu(cpu));
            if (family == null) return 1f;

            float multiplier = ContributionOf(family, cpu, segment);
            return new ProviderResult(multiplier, ReasonKey, ReasonArgs(family));
        }

        /// <summary>
        /// The contribution of <paramref name="family"/> to the rating of <paramref name="cpu"/> in <paramref name="segment"/> this year, as the provider
        /// answers it (<see cref="RatingForecast.One"/>): the same number the market gets, used by the compare rows too (FR-5 item 5). Not asked
        /// whether the feature is neutral - the callers do that. Allocation-free (a shared <see cref="RatingInputs"/>).
        /// </summary>
        public float ContributionOf(ArchFamily family, GameCpu cpu, Segment segment)
        {
            DateTime today = ModTicks.Today;
            int year = today == default(DateTime) ? cpu.Year : today.Year;
            BalanceConfig balance = _feature.Balance;

            // The support of the OWNER of the CPU (not of the factory that makes it); stage 1 does not read it.
            float support = 0f;
            if (balance.IsFullStage)
            {
                ICompany owner = cpu.Company;
                CompanyArchState state = ReferenceEquals(owner, null) ? null : _feature.Registry.GetOrNull(owner.SaveID);
                FamilyState familyState = state == null ? null : state.GetFamily(family.Id);
                support = familyState == null ? 0f : familyState.Support;
            }

            // The ecosystem of the family (stage 2, FR-11): its share in the sales of the last twelve months; not measured (or stage 1): negative, which means x1.
            return RatingForecast.One(_inputs, family, segment, year, balance, support, _feature.EcosystemOf(family.Id), cpu.CoreCount, cpu.Smt, _strength);
        }

        private string[] ReasonArgs(ArchFamily family)
        {
            string[] args;
            if (!_reasonArgs.TryGetValue(family.Id, out args))
            {
                args = new[] { ModL10n.L(family.NameKey) };
                _reasonArgs[family.Id] = args;
            }
            return args;
        }

        // ---- the cache of the core ----

        /// <summary>Drops every cached value of <c>market.rating.cpu</c> (a new month, the knob, the feature switched).</summary>
        public void InvalidateAll()
        {
            ModModifiers.Invalidate(ModChannels.MarketRatingCpu);
        }

        /// <summary>Drops the cached values of one company (a CPU of it was bound or moved, its state changed); every company for an unknown one.</summary>
        public void InvalidateCompany(ICompany company)
        {
            string key = ModCompanies.KeyOf(company);
            if (key == null) InvalidateAll();
            else ModModifiers.Invalidate(ModChannels.MarketRatingCpu, key);
        }

        /// <summary><see cref="InvalidateCompany"/> by <c>ICompany.SaveID</c> (<see cref="ArchRegistry.OnCompanyStateChanged"/>).</summary>
        public void InvalidateCompanyId(int companyId)
        {
            IReadOnlyList<ICompany> companies = ModCompanies.All;
            for (int i = 0; i < companies.Count; i++)
            {
                ICompany company = companies[i];
                if (!ReferenceEquals(company, null) && company.SaveID == companyId)
                {
                    InvalidateCompany(company);
                    return;
                }
            }
            InvalidateAll();
        }
    }
}
