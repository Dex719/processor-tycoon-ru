using System;
using PTMod.Core;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The one place where the feature talks to <c>ModModifiers</c> (design.md, "GpuSynergy и GpuModifierBridge"; FR-18, FR-15). It reads the channels the core
    /// declares for the GPU in <c>core.json</c> - <c>gpu.demand</c>, the family <c>gpu.demand.*</c>, <c>gpu.rating</c>, <c>gpu.vramCost</c> - and the standard
    /// <c>production.unitCost</c>, <c>production.output</c>, <c>popularity.gain</c>; the names come from <c>ModChannels</c> (the feature has no constants of its
    /// own and declares no channel). A question carries the owner company and its key and nothing else (no CPU, any segment); no company is the global question
    /// (virtual vendors, the background). It registers the two synergy providers, once: on <c>market.rating.cpu</c> and on <c>gpu.rating</c>, each a dictionary
    /// lookup (<see cref="GpuSynergyTable"/>) that answers x1.0 while the switch <c>synergy</c> is off or the company has no bonus; the core ignores the same
    /// delegate registered again and skips the providers while the feature is off.
    /// </summary>
    internal sealed class GpuModifierBridge : IGpuChannels
    {
        private readonly GpuChannelReader _reader;
        private Func<ModifierContext, ProviderResult> _cpuProvider;
        private Func<ModifierContext, ProviderResult> _gpuProvider;

        public GpuModifierBridge()
        {
            _reader = new GpuChannelReader(GetByKey, new GpuChannelNames
            {
                UnitCost = ModChannels.ProductionUnitCost,
                Output = ModChannels.ProductionOutput,
                VramCost = ModChannels.GpuVramCost,
                Rating = ModChannels.GpuRating,
                PopularityGain = ModChannels.PopularityGain,
                Demand = ModChannels.GpuDemand,
            });
        }

        /// <summary>The providers of the synergy are registered.</summary>
        public bool ProvidersRegistered
        {
            get { return _cpuProvider != null; }
        }

        /// <summary><c>ModModifiers.Get(channel, { Company = company, CompanyKey = key })</c>: 1.0 without contributions, the product of them otherwise; null - the global question.</summary>
        public float Get(string channel, ICompany company)
        {
            if (string.IsNullOrEmpty(channel)) return 1f;
            return GpuChannelReader.Usable(ModModifiers.Get(channel, ContextOf(company)));
        }

        /// <summary>The breakdown behind <see cref="Get"/> for the tooltips of the market tab.</summary>
        public ModifierBreakdown Explain(string channel, ICompany company)
        {
            return ModModifiers.Explain(channel, ContextOf(company));
        }

        /// <summary>
        /// The breakdown of a channel for the tab "Рынок" as plain data (<see cref="GpuExplainView"/>): every multiplier with its feature and reason, the value the game uses and
        /// whether the balance range cut the product. Never cached (<c>ModModifiers.Explain</c>); the tab asks once a game day while it is open, and only for the two channels
        /// the synergy writes into.
        /// </summary>
        public GpuExplainView ExplainView(string channel, ICompany company)
        {
            ModifierBreakdown breakdown = Explain(channel, company);
            var view = new GpuExplainView();
            if (breakdown == null) return view;
            view.Total = breakdown.Total;
            view.Capped = breakdown.IsBalanceCapped;
            foreach (AppliedModifier item in breakdown)
            {
                view.Rows.Add(new GpuExplainRow
                {
                    FeatureId = item.FeatureId ?? string.Empty,
                    ReasonKey = item.ReasonKey ?? string.Empty,
                    ReasonArgs = item.ReasonArgs ?? new string[0],
                    Multiplier = item.Multiplier,
                });
            }
            return view;
        }

        // ---- IGpuChannels: the services ask by company key ----

        public float UnitCost(string companyKey)
        {
            return _reader.UnitCost(companyKey);
        }

        public float Output(string companyKey)
        {
            return _reader.Output(companyKey);
        }

        public float VramCost(string companyKey)
        {
            return _reader.VramCost(companyKey);
        }

        public float Rating(string companyKey)
        {
            return _reader.Rating(companyKey);
        }

        public float PopularityGain(string companyKey)
        {
            return _reader.PopularityGain(companyKey);
        }

        /// <summary>F19: <c>gpu.demand</c> x <c>gpu.demand.&lt;segmentId&gt;</c>, both global.</summary>
        public float Demand(string segmentId)
        {
            return _reader.Demand(segmentId);
        }

        // ---- synergy ----

        /// <summary>
        /// Registers the two providers of the synergy (FR-15) once; a second call does nothing (and the core would ignore the same delegate anyway).
        /// <paramref name="lookup"/> gives the bonuses of a company key, <paramref name="enabled"/> is the switch <c>synergy</c>.
        /// </summary>
        public void RegisterSynergyProviders(Func<string, SynergyBonus> lookup, Func<bool> enabled)
        {
            if (lookup == null || _cpuProvider != null) return;
            Func<bool> on = enabled ?? (() => true);
            _cpuProvider = ctx => GpuSynergyTable.CpuResult(lookup(ctx.CompanyKey), on());
            _gpuProvider = ctx => GpuSynergyTable.GpuResult(lookup(ctx.CompanyKey), on());
            ModModifiers.RegisterReasonedProvider(GpuFeature.FeatureId, ModChannels.MarketRatingCpu, _cpuProvider);
            ModModifiers.RegisterReasonedProvider(GpuFeature.FeatureId, ModChannels.GpuRating, _gpuProvider);
        }

        /// <summary>The synergy changed (the monthly recount, a load, the switch): the cached values of both channels are dropped.</summary>
        public void InvalidateSynergy()
        {
            ModModifiers.Invalidate(ModChannels.MarketRatingCpu);
            ModModifiers.Invalidate(ModChannels.GpuRating);
        }

        // ---- the question ----

        private static ModifierContext ContextOf(ICompany company)
        {
            if (company == null) return default(ModifierContext);
            return new ModifierContext { Company = company, CompanyKey = ModCompanies.KeyOf(company) };
        }

        // A key from the services: the company is looked up (a dictionary of ModCompanies) so the question carries both, as Get(channel, company) does.
        private static float GetByKey(string channel, string companyKey)
        {
            ModifierContext context = companyKey == null
                ? default(ModifierContext)
                : new ModifierContext { Company = ModCompanies.Resolve(companyKey), CompanyKey = companyKey };
            return ModModifiers.Get(channel, context);
        }
    }
}
