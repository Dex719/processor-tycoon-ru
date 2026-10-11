using System;
using System.Collections.Generic;
using PTMod.Core;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The provider of the channel <c>development.cost.design</c> (FR-4 items 2 and 5, D13; design.md Component 6): the price of a CPU project on the
    /// architecture. The core patches <c>CpuMath.ProjectCost</c> itself (P19) and multiplies the game's cost by the product of the providers of the
    /// channel; this feature has no postfix on that method (it is reserved). The value is <see cref="DesignMath.CostFactor"/> of the design of the
    /// company's family:
    /// <list type="bullet">
    /// <item>the player pays for the family picked in the list of the designer (<see cref="DesignContext.SelectedForPlayer"/>), an AI company for its
    /// active family - one price for both (AC-4.3 in terms of money: 50M on x86 is 57.5M for Intel as well as for the player, AC-4.1);</item>
    /// <item>1.0 while the feature is neutral (off, the fast-forward of a new game), without a company, without a family and for a neutral design;</item>
    /// <item>the immaturity of tools (stage 2, FR-12 item 3) is a surcharge on top, <c>0.25 × (1 − months / 12)</c> for the first year after the company researched the
    /// family (<see cref="DesignContext.ImmatureSurcharge"/>): AC-12.2, ARM three months old costs <c>0.85 × 1.1875</c>; 0 at stage 1.</item>
    /// </list>
    /// The core never caches the channel (the context carries a design; the slider of the designer moves the cost every frame), so the provider is asked
    /// on every call, up to a thousand times a month for each AI company: O(1), a few dictionary lookups, no allocation (the reason arguments are kept per
    /// family). Registered once in <c>Register</c>; <c>OnEnabled</c>/<c>OnDisabled</c> do not touch it (the core skips the provider of a disabled feature).
    /// </summary>
    internal sealed class DesignCostProvider
    {
        /// <summary>The <c>ModL10n</c> key of the reason ("Проект на архитектуре {0}").</summary>
        public const string ReasonKey = "arch.reason.devCost";

        private const string Site = "design/cost";

        private readonly ArchitecturesFeature _feature;
        private readonly Dictionary<string, string[]> _reasonArgs = new Dictionary<string, string[]>(StringComparer.Ordinal);
        private bool _registered;

        public DesignCostProvider(ArchitecturesFeature feature)
        {
            if (feature == null) throw new ArgumentNullException("feature");
            _feature = feature;
        }

        /// <summary>Registers the provider on <c>development.cost.design</c>; a second call does nothing. The channel is declared by the core.</summary>
        public void Register()
        {
            if (_registered) return;
            _registered = true;
            ModModifiers.RegisterReasonedProvider(ArchitecturesFeature.FeatureId, ModChannels.DevelopmentCostDesign, Provide);
        }

        /// <summary>The names of the families in the reasons follow the language: the kept arguments are made again.</summary>
        public void ResetReasons()
        {
            _reasonArgs.Clear();
        }

        /// <summary>The answer for one question of P19 (<c>ctx.Company</c> is the company that designs; <c>ctx.Design</c> the parts). Never throws: 1.0 on a failure.</summary>
        public ProviderResult Provide(ModifierContext ctx)
        {
            if (!ModHooks.CanRun(ArchitecturesFeature.FeatureId, Site)) return 1f;
            long started = ArchPerf.Begin();   // NFR-1: one call takes 5 microseconds
            try
            {
                return Compute(ctx);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(ArchitecturesFeature.FeatureId, Site, ex);
                return 1f;
            }
            finally
            {
                ArchPerf.End(ArchPerfMeter.Cost, started);
            }
        }

        private ProviderResult Compute(ModifierContext ctx)
        {
            ICompany company = ctx.Company;
            if (ReferenceEquals(company, null) || _feature.IsNeutralNow) return 1f;

            string familyId = DesignContext.FamilyFor(company);   // also 1.0 for a feature whose designer part did not pass the self-check
            if (familyId == null) return 1f;

            ArchFamily family = _feature.Catalog.Get(familyId);
            if (family == null) return 1f;

            // Stage 2: the tools of a young architecture are immature (FR-12 item 3); stage 1 has none (the surcharge is 0).
            float factor = DesignMath.CostFactor(family.Design, DesignContext.ImmatureSurcharge(company, familyId));
            if (factor == 1f) return 1f;
            return new ProviderResult(factor, ReasonKey, ReasonArgs(family));
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
    }
}
