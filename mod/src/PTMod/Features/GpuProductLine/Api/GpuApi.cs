using System;
using System.Collections.Generic;
using PTMod.Core;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The public face of <c>gpu-product-line</c> for the other features (design.md, "GpuApi"; FR-18 items 3 and 5): whether a company has a GPU division, its cards, its share of the
    /// GPU market, its platform synergy, the segments that are open, a node of the research tree, the transfer of a vendor's lineup to the player when <c>rival-acquisitions</c> buys its
    /// company (called by name through reflection: <c>IsDivisionFounded(ICompany)</c> and <c>TransferVendorProducts(ICompany, ICompany)</c>), and one event. Static and safe: with
    /// the feature off, or before it was registered, every query gives the neutral answer (false, 0, an empty list) and nothing throws; a handler of the event that throws is counted
    /// against its own feature by <c>ModHooks</c> and the next handler still runs. Read-only, except for the transfer. Main thread only, like the rest of the game. The same four
    /// answers are the predicates <c>gpu.divisionFounded</c>, <c>gpu.hasProductOnMarket</c>, <c>gpu.marketShareAtLeast</c>, <c>gpu.researched</c> of <c>ModConditions</c>.
    /// </summary>
    public static class GpuApi
    {
        private const string FeatureId = GpuFeature.FeatureId;
        private const string CallSite = "api/call";
        private const string EventSite = "api/event";

        /// <summary>A card came to the market: the player released one, or a vendor launched one (not at the seed of a new market).</summary>
        public static event Action<GpuProductView> ProductReleased;

        /// <summary>The feature is on and its content is usable: the answers below are real ones.</summary>
        public static bool IsAvailable
        {
            get
            {
                GpuFeature feature = GpuFeature.Instance;
                return feature != null && feature.Api != null && feature.Api.IsAvailable && ModFeatures.IsActive(FeatureId);
            }
        }

        /// <summary>The company has a GPU division: the player's, once founded; for an AI company, a vendor of the market is bound to it. False while the feature is off.</summary>
        public static bool IsDivisionFounded(ICompany company)
        {
            return Ask(company, false, (api, key) => api.IsDivisionFounded(key));
        }

        /// <summary>The cards of the company that are not retired (the player's, or those of the vendors bound to an AI company); empty while the feature is off.</summary>
        public static IReadOnlyList<GpuProductView> ProductsOf(ICompany company)
        {
            return Ask(company, (IReadOnlyList<GpuProductView>)new List<GpuProductView>(), (api, key) => api.ProductsOf(key));
        }

        /// <summary>The share of the company in the GPU market over the last <paramref name="days"/> days (30 by default), in [0, 1].</summary>
        public static float MarketShare(ICompany company, int days = 30)
        {
            return Ask(company, 0f, (api, key) => api.MarketShare(key, days));
        }

        /// <summary>The platform synergy of the company now: the bonuses to the rating of its CPUs and cards, the shares they come from, whether it is active.</summary>
        public static GpuSynergyView Synergy(ICompany company)
        {
            return Ask(company, new GpuSynergyView(), (api, key) => api.Synergy(key));
        }

        /// <summary>The ids of the segments of the GPU market that are open today; empty while the feature is off.</summary>
        public static IReadOnlyList<string> OpenSegmentIds()
        {
            return Ask((IReadOnlyList<string>)new List<string>(), api => api.OpenSegmentIds());
        }

        /// <summary>The node of the research tree of the GPU (<c>arch.3d</c>, <c>vram.gddr3</c>...) is researched by the player.</summary>
        public static bool IsResearched(string nodeId)
        {
            return Ask(false, api => api.IsResearched(nodeId));
        }

        /// <summary>
        /// For <c>rival-acquisitions</c>: the cards of the vendors bound to the AI company <paramref name="from"/> pass to the player <paramref name="to"/> with their stock and
        /// popularity, on sale with 0 lines, and the vendors are merged; a player without a GPU division gets one for nothing on the date of the deal. False (nothing changed) while the
        /// feature is off, when <paramref name="to"/> is not the player or when no vendor is bound to <paramref name="from"/>.
        /// </summary>
        public static bool TransferVendorProducts(ICompany from, ICompany to)
        {
            if (ReferenceEquals(from, null) || ReferenceEquals(to, null)) return false;
            string fromKey = ModCompanies.KeyOf(from), toKey = ModCompanies.KeyOf(to);
            if (fromKey == null || toKey == null) return false;
            return Ask(false, api => api.TransferVendorProducts(fromKey, toKey));
        }

        /// <summary>Raises <see cref="ProductReleased"/> handler by handler (the service of the feature calls it).</summary>
        internal static void RaiseProductReleased(GpuProductView view)
        {
            ModHooks.Raise(EventSite, ProductReleased, view);
        }

        // ---- the guarded call ----

        private static T Ask<T>(ICompany company, T neutral, Func<GpuApiService, string, T> query)
        {
            if (ReferenceEquals(company, null)) return neutral;
            string key = ModCompanies.KeyOf(company);
            return key == null ? neutral : Ask(neutral, api => query(api, key));
        }

        private static T Ask<T>(T neutral, Func<GpuApiService, T> query)
        {
            GpuFeature feature = GpuFeature.Instance;
            if (feature == null || feature.Api == null || !feature.Api.IsAvailable || !ModFeatures.IsActive(FeatureId)) return neutral;
            try
            {
                return query(feature.Api);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(FeatureId, CallSite, ex);
                return neutral;
            }
        }
    }
}
