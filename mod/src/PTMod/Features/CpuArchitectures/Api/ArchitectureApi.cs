using System;
using PTMod.Core;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The public face of <c>cpu-architectures</c> for the other features (design.md Component 15): which architecture a CPU or a company is on, what the architecture does to the rating of a
    /// CPU, the transfer of the state of an absorbed company to its buyer (<c>rival-acquisitions</c>, called by name through reflection), the weights that <c>rival-personalities</c> puts over
    /// the liking of an AI company for a family, and two events. Static and safe: with the feature off, or before it was registered, every query gives the neutral answer (no family, ×1,
    /// nothing done) and nothing throws; a handler of an event that throws is counted against its own feature by <c>ModHooks</c> and the next handler still runs.
    /// Main thread only, like the rest of the game.
    /// </summary>
    public static class ArchitectureApi
    {
        private const string FeatureId = ArchitecturesFeature.FeatureId;
        private const string EventSite = "api/event";
        private const string CallSite = "api/call";

        /// <summary>The feature is on and has its state: the answers below are real ones.</summary>
        public static bool IsActive
        {
            get
            {
                ArchitecturesFeature feature = ArchitecturesFeature.Instance;
                return feature != null && feature.Registry != null && ModFeatures.IsActive(FeatureId);
            }
        }

        /// <summary>The id of the family of a CPU (<see cref="ArchRegistry.ResolveCpu"/>: its binding, or the default of its owner and year); null for a null CPU and while the feature is off.</summary>
        public static string GetArchitectureId(GameCpu cpu)
        {
            if (cpu == null || !IsActive) return null;
            try
            {
                return ArchitecturesFeature.Instance.Registry.ResolveCpu(cpu);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(FeatureId, CallSite, ex);
                return null;
            }
        }

        /// <summary>The family a company designs on (the player: the family he picked last); null while the feature is off and for a company without a state.</summary>
        public static string GetActiveArchitectureId(ICompany company)
        {
            if (ReferenceEquals(company, null) || !IsActive) return null;
            try
            {
                CompanyArchState state = ArchitecturesFeature.Instance.Registry.GetOrNull(company.SaveID);
                return state == null ? null : state.Active;
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(FeatureId, CallSite, ex);
                return null;
            }
        }

        /// <summary>
        /// The multiplier the architecture of a CPU gives its rating in a segment this year - this feature's own contribution to <c>market.rating.cpu</c> and nothing of the other providers
        /// (affinity, ageing and at stage 2 support, ecosystem and extensions, held to the corridor, with the strength knob of the hub). 1 while the feature is off, for a segment of
        /// <see cref="Segment.Any"/>, for a null CPU and for a family the catalog does not know.
        /// </summary>
        public static float GetRatingMultiplier(GameCpu cpu, Segment segment)
        {
            if (cpu == null || segment == Segment.Any || !IsActive) return 1f;
            try
            {
                ArchitecturesFeature feature = ArchitecturesFeature.Instance;
                if (feature.IsNeutralNow || feature.Rating == null || (feature.Parts & ArchParts.Market) == 0) return 1f;

                ArchFamily family = feature.Catalog.Get(feature.Registry.ResolveCpu(cpu));
                return family == null ? 1f : feature.Rating.ContributionOf(family, cpu, segment);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(FeatureId, CallSite, ex);
                return 1f;
            }
        }

        /// <summary>
        /// The architectures of an absorbed company go to its buyer (<c>rival-acquisitions</c>): every family <paramref name="from"/> researched is researched by <paramref name="to"/>, with
        /// the better support of the two when <paramref name="mergeKeepBest"/> is true; the rest of the buyer's state stays his (<see cref="StateTransfer.Merge"/>). The bindings of the CPUs
        /// are by their ids and do not change, so the rating of the CPUs the buyer now owns is asked anew. Does nothing while the feature is off.
        /// </summary>
        public static void TransferCompanyState(ICompany from, ICompany to, bool mergeKeepBest)
        {
            if (ReferenceEquals(from, null) || ReferenceEquals(to, null) || !IsActive) return;
            try
            {
                ArchitecturesFeature feature = ArchitecturesFeature.Instance;
                CompanyArchState source = feature.Registry.GetOrNull(from.SaveID);
                if (source == null) return;

                CompanyArchState target = feature.Registry.GetOrCreate(to.SaveID, to.IsPlayer);
                int changed = StateTransfer.Merge(source, target, mergeKeepBest);
                ModLog.Info(FeatureId, "api: the architectures of company " + from.SaveID + " went to company " + to.SaveID + ", " + changed + " family(ies) changed");
                feature.Registry.NotifyCompanyChanged(target.CompanyId);
                if (feature.Rating != null) feature.Rating.InvalidateAll();
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(FeatureId, CallSite, ex);
            }
        }

        /// <summary>
        /// Puts a weight over the liking of an AI company for a family (FR-14; <c>rival-personalities</c>): the company decides on the record of <c>ai-preferences.json</c> with this weight
        /// in place of the record's, for the family it researches next and for the family it designs on. A weight outside 0..3 is held to it; null takes the override away. The override is
        /// kept in the save. Does nothing for the player (his architecture is his own choice), for an unknown family id, while the feature is off.
        /// </summary>
        public static void SetAiWeightOverride(ICompany company, string familyId, float? weight)
        {
            if (ReferenceEquals(company, null) || company.IsPlayer || !IsActive) return;
            try
            {
                ArchRegistry registry = ArchitecturesFeature.Instance.Registry;
                AiOverrideResult result = AiOverrides.Set(registry.AiOverrides, company.SaveID, familyId, weight, ArchRegistry.MaxOverrides);
                ModLog.Debug(FeatureId, "api: weight of " + familyId + " for company " + company.SaveID + " -> " + (weight.HasValue ? weight.Value.ToString("0.##") : "none") + ": " + result);
                if (result == AiOverrideResult.BadId || result == AiOverrideResult.BadWeight || result == AiOverrideResult.Full)
                {
                    ModLog.Warn(FeatureId, "api: SetAiWeightOverride was refused (" + result + ") for company " + company.SaveID + " and family '" + familyId + "'");
                }
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(FeatureId, CallSite, ex);
            }
        }

        /// <summary>A company finished researching a family (company, family id). Raised once per family.</summary>
        public static event Action<ICompany, string> OnFamilyResearched;

        /// <summary>An AI company changed the family it designs on (company, the earlier family or null, the new one).</summary>
        public static event Action<ICompany, string, string> OnActiveChanged;

        // ---- raised by the feature ----

        internal static void RaiseFamilyResearched(ICompany company, string familyId)
        {
            Action<ICompany, string> handlers = OnFamilyResearched;
            if (handlers == null) return;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<ICompany, string>)handler)(company, familyId);
                }
                catch (Exception ex)
                {
                    ModHooks.ReportFault(FeatureId, EventSite, ex);
                }
            }
        }

        internal static void RaiseActiveChanged(ICompany company, string previous, string current)
        {
            Action<ICompany, string, string> handlers = OnActiveChanged;
            if (handlers == null) return;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<ICompany, string, string>)handler)(company, previous, current);
                }
                catch (Exception ex)
                {
                    ModHooks.ReportFault(FeatureId, EventSite, ex);
                }
            }
        }
    }
}
