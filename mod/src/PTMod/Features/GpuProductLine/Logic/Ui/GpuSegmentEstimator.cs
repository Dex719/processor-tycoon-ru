using System;
using System.Collections.Generic;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>One row of the table "Оценка по сегментам" of the designer (FR-3 items 2 and 6, design.md "Вкладка «Конструктор»").</summary>
    public sealed class GpuSegmentEstimate
    {
        public string SegmentId = string.Empty;
        public string Name = string.Empty;

        /// <summary>The rating of the new card against the best card on sale in the segment, in percent (100 = as good; 0 = no buyers at all).</summary>
        public float RatingPercent;

        /// <summary>There are cards on the market in the segment to compare with.</summary>
        public bool HasCompetitors;

        /// <summary>The multiplier for the architecture's features the segment asks for (1 = all there).</summary>
        public float FeatureFactor = 1f;

        /// <summary>The multiplier for the video memory against the requirement of the year (1 = enough).</summary>
        public float VramFactor = 1f;

        public float VramRequirementMB;
        public float Budget;

        /// <summary>The price the rating was computed at: 0.9 of the budget (FR-4 item 6).</summary>
        public float Price;

        public string[] MissingFeatures = new string[0];
    }

    /// <summary>
    /// The estimate of a card that is only being designed, against the market of today (FR-3 item 2: "оценку рейтинга в каждом открытом сегменте относительно текущего
    /// рынка"). The card is put among the cards on sale as one more card of the player, priced at <c>0.9 x</c> the budget of the segment and well known
    /// (the rating of the market does not depend on the popularity but on the quality and the price, F20), and rated by the port of <c>Market.CalculateRatings</c> and
    /// <c>NormalizeRatings</c>; the answer is its rating over the best rating among the cards already on sale. The dilution among the cards of one owner is left out:
    /// it belongs to the player's other cards, not to this design.
    /// </summary>
    public static class GpuSegmentEstimator
    {
        /// <summary>The id the pretend card carries among the items of the market.</summary>
        public const string DesignProductId = "~design";

        /// <summary>The estimate for every open segment, from the services of the feature.</summary>
        public static List<GpuSegmentEstimate> Estimate(GpuContentDb content, GpuMarketService market, IGpuGame game, GpuStats stats, float vramMB, IGpuText text)
        {
            DateTime date = game.Today;
            List<SegmentRuntime> segments = market.BuildSegments(date);
            if (segments.Count == 0) return new List<GpuSegmentEstimate>();

            List<MarketItem> onSale = market.BuildItems(segments, date);
            var views = new Dictionary<string, GpuSegmentView>(StringComparer.Ordinal);
            foreach (GpuSegmentView view in market.OpenSegments) views[view.Id] = view;
            return Rate(content, segments, onSale, views, stats, vramMB, game.Channels.Rating(game.PlayerKey), text);
        }

        /// <summary>The estimate for the given segments and the cards on sale (they are rated in place, so pass items you can lose).</summary>
        public static List<GpuSegmentEstimate> Rate(GpuContentDb content, IList<SegmentRuntime> segments, IList<MarketItem> onSale, IDictionary<string, GpuSegmentView> views,
            GpuStats stats, float vramMB, float ratingMultiplier, IGpuText text)
        {
            var result = new List<GpuSegmentEstimate>();
            string[] have = stats.Features ?? new string[0];

            foreach (SegmentRuntime segment in segments)
            {
                GpuSegmentDto dto = content.Segment(segment.Id);
                GpuSegmentView view;
                if (views == null || !views.TryGetValue(segment.Id, out view)) view = new GpuSegmentView { Id = segment.Id };

                string[] required = view.RequiredFeatures ?? new string[0];
                float missingFactor = dto != null ? dto.missingFeatureFactor : 1f;
                var estimate = new GpuSegmentEstimate
                {
                    SegmentId = segment.Id,
                    Name = GpuNames.Segment(content, text, segment.Id),
                    Budget = segment.Budget,
                    VramRequirementMB = view.VramRequirementMB,
                    FeatureFactor = GpuMarketMath.FeatureFactor(have, required, missingFactor),
                    VramFactor = GpuMarketMath.VramFactor(vramMB, view.VramRequirementMB),
                    Price = GpuLineup.SuggestedPriceShare * segment.Budget,
                };
                estimate.MissingFeatures = Missing(have, required);

                var items = new List<MarketItem>(onSale);
                var card = new MarketItem
                {
                    ProductId = DesignProductId,
                    OwnerKey = MarketItem.PlayerOwner,
                    Perf = stats.EffectivePerf,
                    PowerW = stats.PowerW,
                    Price = estimate.Price,
                    InfiniteStock = true,
                    Selling = true,
                    Popularity = new float[Math.Max(1, segment.Index + 1)],
                    SegmentFactor = new float[Math.Max(1, segment.Index + 1)],
                    RatingMul = ratingMultiplier,
                };
                card.Popularity[segment.Index] = 1f;
                card.SegmentFactor[segment.Index] = estimate.FeatureFactor * estimate.VramFactor;
                items.Add(card);

                double perfSum = 0d;
                foreach (MarketItem item in items)
                {
                    if (item != null && item.Selling) perfSum += Math.Max(0d, item.Perf);
                }
                GpuMarketMath.CalculateRatings(segment, items, perfSum, false);
                GpuMarketMath.Normalize(segment, items);

                float best = 0f;
                foreach (MarketItem item in onSale)
                {
                    if (item != null && item.Overall > best) best = item.Overall;
                }
                estimate.HasCompetitors = best > 0f;
                if (card.Overall > 0f) estimate.RatingPercent = best > 0f ? card.Overall / best * 100f : 100f;
                else estimate.RatingPercent = 0f;
                result.Add(estimate);
            }
            return result;
        }

        private static string[] Missing(string[] have, string[] required)
        {
            var missing = new List<string>();
            foreach (string need in required)
            {
                if (Array.IndexOf(have, need) < 0) missing.Add(need);
            }
            return missing.ToArray();
        }
    }
}
