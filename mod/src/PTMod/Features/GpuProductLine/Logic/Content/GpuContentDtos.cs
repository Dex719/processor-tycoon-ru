using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.GpuProductLine
{
    // DTOs of the content files of gpu-product-line (design.md, "Контент-JSON"; FR-10). Plain classes with public fields and List<T>
    // instead of arrays and dictionaries, for ModJson.Bind (decision 0006). The default values below are what a record gets for a field
    // the JSON does not mention; the fields a record must name are checked by GpuContentDb (GpuContentDb.Required*).
    // Years are calendar years; money is in game money; ids are local to this feature (arch.*, vram.*, cool.*, vendor ids).

    /// <summary>A value at a year: the control point of a piecewise function (<c>vramReq</c>, <c>maxDieByYear</c>).</summary>
    public sealed class GpuYearValue
    {
        public int year;
        public float v;
    }

    /// <summary>The process node that is current from a year (<c>timeline.nodeByYear</c>); <c>node</c> is a <c>ProcessNode.Name</c> of the game.</summary>
    public sealed class GpuYearNode
    {
        public int year;
        public string node;
    }

    /// <summary>The features a segment demands from a year on (<c>segments.featureReqs</c>).</summary>
    public sealed class GpuYearFeats
    {
        public int year;
        public List<string> f = new List<string>();
    }

    /// <summary>A text given inline in both languages (<c>vendors.name</c>).</summary>
    public sealed class GpuText
    {
        public string ru, en;
    }

    /// <summary>A GPU architecture (<c>architectures.json</c>): what a die of it can do (F2-F9, F13).</summary>
    public sealed class GpuArchitectureDto : IContentRecord
    {
        public string id, nameKey;
        public string Id => id;
        public int year;
        public float transistorsPerUnit, opsPerClock, clockRatio, bytesPerOp, powerCoef, complexity, dieMaterialCost, projectCost;
        public int projectDays;
        public List<string> features = new List<string>();
    }

    /// <summary>A video memory type (<c>memory.json</c>).</summary>
    public sealed class GpuMemoryDto : IContentRecord
    {
        public string id, nameKey;
        public string Id => id;
        public int year;
        public float dataRateMTs, costPerMB, costDecayPerYear = 1f, costFloor = 1f, maxVramMB, projectCost;
        public List<int> busWidths = new List<int>();
        public int projectDays;
    }

    /// <summary>A cooler (<c>coolers.json</c>).</summary>
    public sealed class GpuCoolerDto : IContentRecord
    {
        public string id, nameKey;
        public string Id => id;
        public int year;
        public float thermalResistance, cost;
    }

    /// <summary>A node of the research tree of the GPU laboratory (<c>research.json</c>); <c>unlocks</c> is the id of the record it opens in the file of its branch.</summary>
    public sealed class GpuResearchNodeDto : IContentRecord
    {
        public string id, branch /* architecture|memory|cooling */, unlocks;
        public string Id => id;
        public int year, baseDays;
        public float monthlyCost;
        public List<string> requiresNodes = new List<string>(), requiresGameTechs = new List<string>();
    }

    /// <summary>A market segment (<c>segments.json</c>); <c>mode</c> says in which market mode (<c>single</c> or <c>eras</c>) it exists.</summary>
    public sealed class GpuSegmentDto : IContentRecord
    {
        public string id, mode /* single|eras */, nameKey;
        public string Id => id;
        public int opensYear, closesYear /* 0 = never */;
        public float baseSize;
        public List<float> sizeByDecade = new List<float>(), budgetByDecade = new List<float>();   // 7 values: 1970s..2030s
        public float consumptionTarget = 99f, wPerf = 100f, wUnder = 3f, wOver = 1f, wObs = 3f, wCons, missingFeatureFactor = 0.35f;
        public List<GpuYearFeats> featureReqs = new List<GpuYearFeats>();
        public List<GpuYearValue> vramReq = new List<GpuYearValue>();
        public float rivalDieShare = 0.5f, rivalVramFactor = 1f;
        public int rivalBusTier;
    }

    /// <summary>An era of the GPU market (<c>eras.json</c>, stage 2).</summary>
    public sealed class GpuEraDto : IContentRecord
    {
        public string id, nameKey, mailKey;
        public string Id => id;
        public int startYear;
        public List<string> hintNodes = new List<string>();
    }

    /// <summary>A GPU vendor, virtual or bound to an AI company (<c>vendors.json</c>, stage 2).</summary>
    public sealed class GpuVendorDto : IContentRecord
    {
        public string id;
        public string Id => id;
        public GpuText name;                 // fallback name: a bound vendor takes its name from the game (ICompany.get_Name)
        public string series;                // series for product names: "<series> <launchCount>"
        public string bindTo;                // "" (virtual) or a ModCompanies key "ai:<UniqueID>"
        public bool fallbackVirtual;
        public int entryYear, exitYear, mergeYear;
        public string mergeInto;
        public float techLag, priceFactor = 1f, capacityShare, strength;
        public int designIntervalMonths = 12;
        public List<string> segments = new List<string>();
        public string color;
    }

    /// <summary>A console maker's order offer (<c>consoles.json</c>, stage 3, FR-19).</summary>
    public sealed class GpuConsoleDto : IContentRecord
    {
        public string id, clientKey /* L10n key gpu.console.client.<...> */;
        public string Id => id;
        public int offerYear, termYears;
    }

    /// <summary><c>timeline.json</c>: the process node and the largest die by year.</summary>
    public sealed class GpuTimelineDto
    {
        public List<GpuYearNode> nodeByYear = new List<GpuYearNode>();
        public List<GpuYearValue> maxDieByYear = new List<GpuYearValue>();
    }

    /// <summary>
    /// <c>balance.json</c>: every number of the economy of the feature. The initializers are the shipped values, so a missing field (or a
    /// missing file) falls back to the same numbers; <see cref="GpuContentDb"/> checks the ranges and puts the shipped value back for a bad one.
    /// </summary>
    public sealed class GpuBalanceDto
    {
        public string marketMode = "single";   // single | eras
        public MarketBlock market = new MarketBlock();
        public FoundingBlock founding = new FoundingBlock();
        public ResearchBlock research = new ResearchBlock();
        public DesignBlock design = new DesignBlock();
        public ProductionBlock production = new ProductionBlock();
        public PopularityBlock popularity = new PopularityBlock();
        public BackgroundBlock background = new BackgroundBlock();
        public RivalsBlock rivals = new RivalsBlock();
        public SynergyBlock synergy = new SynergyBlock();
        public NewsBlock news = new NewsBlock();
        public ConsoleBlock console = new ConsoleBlock();
        public LimitsBlock limits = new LimitsBlock();

        public sealed class MarketBlock
        {
            public int opensYear = 1981;
        }

        public sealed class FoundingBlock
        {
            public float cost = 25000000f;
            public int catchUpLagYears = 3;
        }

        public sealed class ResearchBlock
        {
            public float penaltyPerYear = 3.5f, bonusPerYear = 0.15f, maxBonus = 0.75f, fundingMin = 0.1f, fundingMax = 1f;
            public int queueMax = 5;
        }

        public sealed class DesignBlock
        {
            public float minDie = 20f, clockMinShare = 0.25f, bandwidthExponent = 0.7f, bandwidthRatioMin = 0.05f;
            public float densityExponent = -0.25f, basePowerW = 0.5f, ambientC = 35f, safeTempC = 95f, frTempK = 5f, frClockK = 15f;
            public float boardBase = 20f, boardCostPerBit = 0.08f, nodeProjectShare = 0.5f;
            public int maxParallelProjects = 3, nameMaxLength = 32;
        }

        public sealed class ProductionBlock
        {
            public float autoDemandFactor = 1.1f, autoMaxShare = 0.5f, storageCostPerUnitMonth = 0.5f, liquidationShare = 0.1f, fablessYieldProgress = 0.75f;
        }

        public sealed class PopularityBlock
        {
            public float start = 0.05f, floor = 0.05f, cap = 1f, gainBase = 0.025f, baselineGain = 0.00005f;
            public List<int> eraYears = new List<int> { 1990, 2010 };
            public List<float> eraGain = new List<float> { 1f, 2.5f, 5f };
            public List<float> eraBaseline = new List<float> { 1f, 1.5f, 3f };
            public float oldDecay = 0.0005f;
            public int oldYears = 5, oldYearsBefore1990 = 7;
            public float brandCarry = 0.5f;
        }

        public sealed class BackgroundBlock
        {
            public bool enabled = true;
            public float techLagYears = 2f, popularity = 0.8f;   // TSK-045: 0.35 in the design; the others must keep 15 % of the market before 2000 (NFR-7)
            public int refreshMonths = 12;
            public float priceFactor = 0.8f;                      // TSK-045: 1.0 in the design (the price of the others is below the budget)
        }

        public sealed class RivalsBlock
        {
            public int maxProductsPerSegment = 2, intervalJitterMonths = 3;
            public float priceFloorMarkup = 1.3f, targetTempC = 85f, clockShare = 0.95f, dieJitter = 0.05f;
        }

        public sealed class SynergyBlock
        {
            public float cpuMax = 0.08f, cpuK = 0.2f, gpuMax = 0.10f, gpuK = 0.25f;
            public int shareWindowDays = 30;
            public float gpuLearningShare = 1f;
            public int coDevYears = 2;
            public float coDevFactor = 0.85f;
        }

        public sealed class NewsBlock
        {
            public int leaderCooldownMonths = 3;
        }

        public sealed class ConsoleBlock
        {
            public float unitsShare = 0.15f, minPerfShare = 0.6f, priceFactor = 0.55f, priceFloorMarkup = 1.15f;   // unitsShare: 0.25 in the design, 0.15 after TSK-045 (a 1998 order is then about the 2000 a day of AC-19.3)
            public string segment = "gaming.mid", fallbackSegment = "consumer";
            public int offerTimeoutDays = 60, revalidateDays = 7, graceDays = 90;
            public float failCancelShare = 0.25f, reputationPenalty = 3f;
        }

        public sealed class LimitsBlock
        {
            public int products = 500, vendors = 32, ledgerMonths = 600, consoleOrders = 32;
        }
    }
}
