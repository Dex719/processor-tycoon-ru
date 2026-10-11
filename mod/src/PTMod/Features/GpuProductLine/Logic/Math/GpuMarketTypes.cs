namespace PTMod.Features.GpuProductLine
{
    // The two runtime mirrors the market math works on (design.md, "GpuMarketService / GpuMarketMath"). The market service builds them once per
    // game day from the products of the player, the vendors and the background, calls GpuMarketMath.SellDay and writes the results back.

    /// <summary>
    /// A product on the market for one day. Input fields are set by the caller; <see cref="Overall"/> and <see cref="PotentialBuyers"/> are scratch values
    /// of the segment being rated, the <c>...Today</c> fields are the day's results. Arrays are indexed by <see cref="SegmentRuntime.Index"/>; a missing
    /// entry (a short array, or none) means the product does not compete in that segment.
    /// </summary>
    public sealed class MarketItem
    {
        /// <summary><see cref="OwnerKey"/> of the products of the player.</summary>
        public const string PlayerOwner = "player";

        /// <summary>Prefix of <see cref="OwnerKey"/> of a vendor's products (<c>vendor:&lt;id&gt;</c>).</summary>
        public const string VendorPrefix = "vendor:";

        /// <summary><see cref="OwnerKey"/> of the background products: their popularity is fixed and their stock never runs out.</summary>
        public const string BackgroundOwner = "background";

        public string ProductId;
        public string OwnerKey;                 // "player" | "vendor:<id>" | "background"; the products of one owner share their buyers (dilution)
        public string CompanyKey;               // ModCompanies key of the owner company ("player", "ai:<UniqueID>"), null for virtual owners

        public double Perf;                     // effective performance (F6)
        public float PowerW;                    // power in watts (F7)
        public float Price;
        public int Stock;
        public bool InfiniteStock;              // background products
        public bool Selling;                    // on sale; a product that is not takes no part
        public bool IsOld;                      // see GpuMarketMath.IsOld
        public float[] Popularity;              // per segment index
        public float[] SegmentFactor;           // FeatureFactor x VramFactor per segment index
        public float RatingMul = 1f;            // channel gpu.rating of the owner company (1 for virtual owners), read once a day per owner

        public float Overall;
        public int PotentialBuyers;

        public int SoldToday, MissedToday;
        public int[] SoldBySegment;             // per segment index
        public float IncomeToday;

        /// <summary>True for a background product.</summary>
        public bool IsBackground
        {
            get { return OwnerKey == BackgroundOwner; }
        }
    }

    /// <summary>A market segment on one day: its buyers, budget and the weights of the rating (<c>Market.Population</c> with <c>RatingWeights</c>).</summary>
    public sealed class SegmentRuntime
    {
        public string Id;
        public int Index;                       // the position of the segment in the arrays of MarketItem
        public float DailyBuyers;               // buyers for the day (F19)
        public float Remaining;                 // buyers not yet served; SellDay sets it to DailyBuyers
        public float Budget;
        public float ConsTarget;
        public float WPerf, WUnder, WOver, WObs, WCons;

        /// <summary>A runtime segment with the weights of a content record; the day's buyers and budget are set by the caller.</summary>
        public static SegmentRuntime FromDto(GpuSegmentDto dto, int index)
        {
            return new SegmentRuntime
            {
                Id = dto.id,
                Index = index,
                ConsTarget = dto.consumptionTarget,
                WPerf = dto.wPerf,
                WUnder = dto.wUnder,
                WOver = dto.wOver,
                WObs = dto.wObs,
                WCons = dto.wCons,
            };
        }
    }
}
