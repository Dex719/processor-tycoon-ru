using System.Collections.Generic;

namespace PTMod.Features.GpuProductLine
{
    // DTOs of the section "gpu-product-line" of the side save file <save>.ptmod.json (design.md, "Состояние и секция ModSave").
    // Plain classes with public fields and List<T>, for ModJson.Bind / ModJson.From (decision 0006); no [Serializable], no JsonUtility.
    // There is NO version field here: "_schema" is written and read by the core (IModSaveParticipant.SchemaVersion, FR-9 item 1).
    // Companies are keys of ModCompanies ("player", "ai:<UniqueID>", "" for a virtual vendor); the numeric SaveID never enters the section (FR-9 item 8).
    // Strings the section can leave out are "" or null, the checks of GpuState tell which; the initializers are the values of a new game.

    /// <summary>A calendar date as three numbers (<c>{ "y": 1995, "m": 3, "d": 1 }</c>).</summary>
    public sealed class GpuDate
    {
        public int y, m, d;
    }

    /// <summary>The whole section.</summary>
    public sealed class GpuSaveSection
    {
        public bool divisionFounded;
        public GpuDate foundedOn;
        public string marketModeApplied = "single";
        public long nextId = 1;
        public GpuResearchSave research = new GpuResearchSave();
        public List<GpuDesignSave> designs = new List<GpuDesignSave>();
        public List<GpuProjectSave> projects = new List<GpuProjectSave>();
        public List<GpuProductSave> products = new List<GpuProductSave>();
        public List<GpuVendorSave> vendors = new List<GpuVendorSave>();
        public GpuMarketSave market = new GpuMarketSave();
        public GpuSynergySave synergy = new GpuSynergySave();
        public GpuNewsSave news = new GpuNewsSave();
        public List<GpuConsoleOrderSave> consoleOrders = new List<GpuConsoleOrderSave>();   // Could, FR-19
    }

    /// <summary>The GPU laboratory: finished nodes, the current one, the queue and the funding.</summary>
    public sealed class GpuResearchSave
    {
        public List<string> completed = new List<string>();
        public string currentId = "";
        public float currentProgress;
        public List<string> queue = new List<string>();
        public float funding = 1f;
    }

    /// <summary>The snapshot of the characteristics of a design (F1-F13); kept in the product, so content changes do not rewrite it (D14).</summary>
    public sealed class GpuStatsSave
    {
        public double transistors, computeMops, effectivePerf;
        public int units;
        public float maxClockMHz, bandwidthMBs, bandwidthRatio, powerW, tempC, failureRate, dieMaterial, perGoodExtras, projectCost;

        /// <summary>The cost of a card of the maker when it was made (F12); kept for the cards of the vendors, whose cost is not recounted every day (F24). 0: not known.</summary>
        public float unitCost;

        public int projectDays;
        public List<string> features = new List<string>();
    }

    /// <summary>A saved design of the player.</summary>
    public sealed class GpuDesignSave
    {
        public string id, name, archId, memId, coolerId, node;
        public float die, clockMHz, vramMB;
        public int busWidth;
        public GpuStatsSave stats;
    }

    /// <summary>A development project in progress.</summary>
    public sealed class GpuProjectSave
    {
        public string id, designId;
        public float progress, totalCost, inflationAtStart;
        public int days;
        public bool paused;
    }

    /// <summary>The monthly books of a product: units sold, produced and failed, income and cost (rings, oldest first).</summary>
    public sealed class GpuLedgerSave
    {
        public int startMonthIndex;
        public List<int> sold = new List<int>(), produced = new List<int>(), failed = new List<int>();
        public List<float> income = new List<float>(), cost = new List<float>();
    }

    /// <summary>A card on the market: the player's, a vendor's or the background's.</summary>
    public sealed class GpuProductSave
    {
        public string id, designId, owner /* player|vendor|background */, vendorId, name, status /* ready|selling|retired */;
        public string companyKey = "";             // ModCompanies key: "player" | "ai:<UniqueID>" | "" for a virtual vendor and the background
        public float price;
        public bool sell = true, auto;
        public int lines, unitsPerDay, stock;
        public GpuDate released;
        public GpuStatsSave stats;
        public string node;
        public float die;
        public int busWidth;
        public float vramMB;                       // needed for the daily production and the VRAM factor
        public List<string> segIds = new List<string>();
        public List<float> pop = new List<float>();
        public GpuLedgerSave ledger = new GpuLedgerSave();
    }

    /// <summary>The last launch of a vendor (or the last leader mail) in a segment.</summary>
    public sealed class GpuSegLaunch
    {
        public string seg;
        public GpuDate date;

        /// <summary>The months drawn on top of <c>designIntervalMonths</c> for the next launch of the vendor in this segment (0-3, FR-13 item 1); 0 for a leader mail.</summary>
        public int extraMonths;
    }

    /// <summary>The state of a vendor of the content (<c>vendors.json</c>).</summary>
    public sealed class GpuVendorSave
    {
        public string id, state /* pending|active|exited|merged */, mergedInto = "" /* vendor id or "player" */;
        public string companyKey = "";
        public int launchCount;
        public List<GpuSegLaunch> lastLaunch = new List<GpuSegLaunch>();
    }

    /// <summary>The market: seeded flag, open segments with their potential sales, the rings of the player's share.</summary>
    public sealed class GpuMarketSave
    {
        public bool seeded;
        public List<string> openSegIds = new List<string>();
        public List<float> potentialSales = new List<float>();
        public GpuDate lastBackgroundRefresh;

        /// <summary>The date the segments and eras were last compared with the calendar (<c>GpuEras</c>): what opened or closed since is announced once.</summary>
        public GpuDate erasCheckedOn;

        public List<float> playerShareDaily = new List<float>();   // ring, 30 days
        public List<int> playerSoldMonthly = new List<int>();      // 24 months
    }

    /// <summary>The synergy of one company: the two bonuses and the rings they are computed from.</summary>
    public sealed class GpuSynergyEntry
    {
        public string companyKey;
        public float sCpu, sGpu;
        public List<float> cpuShareDaily = new List<float>(), gpuShareDaily = new List<float>();
    }

    public sealed class GpuSynergySave
    {
        public List<GpuSynergyEntry> entries = new List<GpuSynergyEntry>();
    }

    public sealed class GpuNewsSave
    {
        public List<GpuSegLaunch> lastLeaderMail = new List<GpuSegLaunch>();
    }

    /// <summary>A console order (Could, FR-19); <c>mailId</c> is the id of the offer mail that <c>ModMail.Send</c> returned.</summary>
    public sealed class GpuConsoleOrderSave
    {
        public string id /* record of consoles.json */, status /* offered|active|ended|cancelled|declined */, productId = "";
        public int mailId;
        public GpuDate offeredOn, startedOn, endsOn;
        public int unitsPerDay;
        public float price;
        public List<int> deliveredWindow = new List<int>(), missedWindow = new List<int>();   // ring of graceDays
    }
}
