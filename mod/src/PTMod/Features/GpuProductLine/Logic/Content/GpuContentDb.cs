using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The content of gpu-product-line, loaded and checked (FR-10, design.md "GpuContentDb и DTO контента", "Правила валидации контента"):
    /// architectures, memory, coolers, research nodes, segments, eras, vendors, console offers, the timeline and the balance.
    /// <para>
    /// <see cref="Load"/> reads every file through an <see cref="IModContentSource"/>. A broken record is skipped (its source says why); what
    /// only the whole set can tell is checked here and goes to <c>problems</c>: references between files (<c>unlocks</c>, <c>requiresNodes</c>,
    /// <c>mergeInto</c>, vendor segments, era hints), cycles, the ranges of the balance and the timeline. A balance number out of range is put
    /// back to the shipped value, a research node or a vendor with a broken reference is skipped together with the ones that depend on it.
    /// The database is immutable after <see cref="Load"/>; <see cref="IsUsable"/> is false when a required set is empty (FR-10 item 3).
    /// </para>
    /// </summary>
    public sealed class GpuContentDb
    {
        /// <summary>The <c>schema</c> of the content files this version reads (not the version of the save section).</summary>
        public const int ContentSchema = 1;

        public const string FileBalance = "balance";
        public const string FileArchitectures = "architectures";
        public const string FileMemory = "memory";
        public const string FileCoolers = "coolers";
        public const string FileResearch = "research";
        public const string FileSegments = "segments";
        public const string FileEras = "eras";
        public const string FileVendors = "vendors";
        public const string FileConsoles = "consoles";
        public const string FileTimeline = "timeline";

        public const string ModeSingle = "single";
        public const string ModeEras = "eras";

        public const string BranchArchitecture = "architecture";
        public const string BranchMemory = "memory";
        public const string BranchCooling = "cooling";

        /// <summary>Years the content may name (design.md: "годы в [1971, 2040]").</summary>
        public const int MinYear = 1971, MaxYear = 2040;

        /// <summary>Number of values of <c>sizeByDecade</c> and <c>budgetByDecade</c>: the 1970s .. the 2030s.</summary>
        public const int DecadeCount = 7;

        private const string KeyPrefix = "gpu.";

        private static readonly string[] SchemaOnly = { "schema" };
        private static readonly string[] DisabledOnly = { "disabled" };

        private readonly List<GpuArchitectureDto> _architectures = new List<GpuArchitectureDto>();
        private readonly List<GpuMemoryDto> _memories = new List<GpuMemoryDto>();
        private readonly List<GpuCoolerDto> _coolers = new List<GpuCoolerDto>();
        private readonly List<GpuResearchNodeDto> _nodes = new List<GpuResearchNodeDto>();
        private readonly List<GpuSegmentDto> _allSegments = new List<GpuSegmentDto>();
        private readonly List<GpuSegmentDto> _segments = new List<GpuSegmentDto>();
        private readonly List<GpuEraDto> _eras = new List<GpuEraDto>();
        private readonly List<GpuVendorDto> _vendors = new List<GpuVendorDto>();
        private readonly List<GpuConsoleDto> _consoles = new List<GpuConsoleDto>();

        private readonly Dictionary<string, GpuArchitectureDto> _archById = new Dictionary<string, GpuArchitectureDto>(StringComparer.Ordinal);
        private readonly Dictionary<string, GpuMemoryDto> _memById = new Dictionary<string, GpuMemoryDto>(StringComparer.Ordinal);
        private readonly Dictionary<string, GpuCoolerDto> _coolerById = new Dictionary<string, GpuCoolerDto>(StringComparer.Ordinal);
        private readonly Dictionary<string, GpuResearchNodeDto> _nodeById = new Dictionary<string, GpuResearchNodeDto>(StringComparer.Ordinal);
        private readonly Dictionary<string, GpuSegmentDto> _segmentById = new Dictionary<string, GpuSegmentDto>(StringComparer.Ordinal);
        private readonly Dictionary<string, GpuSegmentDto> _anySegmentById = new Dictionary<string, GpuSegmentDto>(StringComparer.Ordinal);
        private readonly Dictionary<string, GpuEraDto> _eraById = new Dictionary<string, GpuEraDto>(StringComparer.Ordinal);
        private readonly Dictionary<string, GpuVendorDto> _vendorById = new Dictionary<string, GpuVendorDto>(StringComparer.Ordinal);
        private readonly Dictionary<string, GpuConsoleDto> _consoleById = new Dictionary<string, GpuConsoleDto>(StringComparer.Ordinal);

        private GpuContentDb()
        {
            Balance = new GpuBalanceDto();
            Timeline = new GpuTimelineDto();
        }

        // ---- the data ----

        public GpuBalanceDto Balance { get; private set; }

        public IReadOnlyList<GpuArchitectureDto> Architectures
        {
            get { return _architectures; }
        }

        public IReadOnlyList<GpuMemoryDto> Memories
        {
            get { return _memories; }
        }

        public IReadOnlyList<GpuCoolerDto> Coolers
        {
            get { return _coolers; }
        }

        public IReadOnlyList<GpuResearchNodeDto> ResearchNodes
        {
            get { return _nodes; }
        }

        /// <summary>The segments of the current market mode (<see cref="GpuBalanceDto.marketMode"/>).</summary>
        public IReadOnlyList<GpuSegmentDto> Segments
        {
            get { return _segments; }
        }

        /// <summary>The segments of both modes (the mode switch of stage 2 needs the ones that are not current).</summary>
        public IReadOnlyList<GpuSegmentDto> AllSegments
        {
            get { return _allSegments; }
        }

        public IReadOnlyList<GpuEraDto> Eras
        {
            get { return _eras; }
        }

        public IReadOnlyList<GpuVendorDto> Vendors
        {
            get { return _vendors; }
        }

        public IReadOnlyList<GpuConsoleDto> Consoles
        {
            get { return _consoles; }
        }

        public GpuTimelineDto Timeline { get; private set; }

        /// <summary>At least one architecture, memory, cooler and segment (of the current mode) survived the checks (FR-10 item 3).</summary>
        public bool IsUsable
        {
            get { return UnusableReason == null; }
        }

        /// <summary>Why <see cref="IsUsable"/> is false (English, for the log and the notification), or null.</summary>
        public string UnusableReason
        {
            get
            {
                var missing = new List<string>();
                if (_architectures.Count == 0) missing.Add("architectures");
                if (_memories.Count == 0) missing.Add("memory types");
                if (_coolers.Count == 0) missing.Add("coolers");
                if (_segments.Count == 0) missing.Add("segments of the market mode '" + Balance.marketMode + "'");
                return missing.Count == 0 ? null : "no " + string.Join(", no ", missing.ToArray());
            }
        }

        // ---- lookups ----

        public GpuArchitectureDto Arch(string id)
        {
            return Find(_archById, id);
        }

        public GpuMemoryDto Mem(string id)
        {
            return Find(_memById, id);
        }

        public GpuCoolerDto Cooler(string id)
        {
            return Find(_coolerById, id);
        }

        public GpuResearchNodeDto Node(string id)
        {
            return Find(_nodeById, id);
        }

        /// <summary>A segment of the current market mode, or null.</summary>
        public GpuSegmentDto Segment(string id)
        {
            return Find(_segmentById, id);
        }

        /// <summary>A segment of either market mode, or null.</summary>
        public GpuSegmentDto AnySegment(string id)
        {
            return Find(_anySegmentById, id);
        }

        public GpuEraDto Era(string id)
        {
            return Find(_eraById, id);
        }

        public GpuVendorDto Vendor(string id)
        {
            return Find(_vendorById, id);
        }

        public GpuConsoleDto ConsoleEntry(string id)
        {
            return Find(_consoleById, id);
        }

        private static T Find<T>(Dictionary<string, T> index, string id) where T : class
        {
            T value;
            return id != null && index.TryGetValue(id, out value) ? value : null;
        }

        /// <summary>The newest architecture whose year is not after <paramref name="techYear"/> (the later record wins a tie), or null before the first one.</summary>
        public GpuArchitectureDto LatestArch(float techYear)
        {
            GpuArchitectureDto best = null;
            foreach (GpuArchitectureDto arch in _architectures)
            {
                if (arch.year <= techYear && (best == null || arch.year >= best.year)) best = arch;
            }
            return best;
        }

        /// <summary>The newest memory type whose year is not after <paramref name="techYear"/>, or null before the first one.</summary>
        public GpuMemoryDto LatestMem(float techYear)
        {
            GpuMemoryDto best = null;
            foreach (GpuMemoryDto mem in _memories)
            {
                if (mem.year <= techYear && (best == null || mem.year >= best.year)) best = mem;
            }
            return best;
        }

        /// <summary>The newest cooler whose year is not after <paramref name="techYear"/>, or null before the first one.</summary>
        public GpuCoolerDto LatestCooler(float techYear)
        {
            GpuCoolerDto best = null;
            foreach (GpuCoolerDto cooler in _coolers)
            {
                if (cooler.year <= techYear && (best == null || cooler.year >= best.year)) best = cooler;
            }
            return best;
        }

        /// <summary>
        /// The name of the process node that is current at <paramref name="techYear"/> (<c>timeline.nodeByYear</c>): the last entry that is not
        /// after the year; the first entry for a year before the table; null when the table is empty.
        /// </summary>
        public string NodeNameFor(float techYear)
        {
            List<GpuYearNode> table = Timeline.nodeByYear;
            if (table.Count == 0) return null;
            string name = table[0].node;
            foreach (GpuYearNode entry in table)
            {
                if (entry.year <= techYear) name = entry.node;
                else break;
            }
            return name;
        }

        /// <summary>
        /// The largest die in mm2 at a year (<c>timeline.maxDieByYear</c>), linear between the control points, the first or the last value
        /// outside them; 0 when the table is empty.
        /// </summary>
        public float MaxDie(int year)
        {
            List<GpuYearValue> table = Timeline.maxDieByYear;
            if (table.Count == 0) return 0f;
            if (year <= table[0].year) return table[0].v;
            for (int i = 1; i < table.Count; i++)
            {
                GpuYearValue next = table[i];
                if (year > next.year) continue;
                GpuYearValue prev = table[i - 1];
                float span = next.year - prev.year;
                return span <= 0f ? next.v : prev.v + (next.v - prev.v) * ((year - prev.year) / span);
            }
            return table[table.Count - 1].v;
        }

        /// <summary>The segments of a vendor that exist in the current market mode, in the order of the vendor's list (the others are skipped, FR-12).</summary>
        public List<string> ActiveSegmentIds(GpuVendorDto vendor)
        {
            var result = new List<string>();
            if (vendor == null || vendor.segments == null) return result;
            foreach (string id in vendor.segments)
            {
                if (id != null && _segmentById.ContainsKey(id) && !result.Contains(id)) result.Add(id);
            }
            return result;
        }

        // ---- loading ----

        /// <summary>
        /// Loads and checks all files. Never throws: whatever cannot be used is left out and explained in <paramref name="problems"/>
        /// (cross-file checks, the balance, the timeline); the per-record reasons are the source's business (<c>ModContent</c> logs them itself).
        /// </summary>
        public static GpuContentDb Load(IModContentSource src, IList<string> problems)
        {
            var db = new GpuContentDb();
            var found = new List<string>();
            if (src == null)
            {
                found.Add("content: no content source");
            }
            else
            {
                try
                {
                    db.LoadAll(src, found);
                }
                catch (Exception ex)
                {
                    found.Add("content: loading failed: " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            if (problems != null)
            {
                foreach (string problem in found) problems.Add(problem);
            }
            return db;
        }

        private void LoadAll(IModContentSource src, List<string> problems)
        {
            Balance = LoadBalance(src, problems);
            Timeline = LoadTimeline(src, problems);

            _architectures.AddRange(Records(src, FileArchitectures, Mapper<GpuArchitectureDto>(RequiredArch), ValidateArchitecture, problems));
            _memories.AddRange(Records(src, FileMemory, Mapper<GpuMemoryDto>(RequiredMemory), ValidateMemory, problems));
            _coolers.AddRange(Records(src, FileCoolers, Mapper<GpuCoolerDto>(RequiredCooler), ValidateCooler, problems));
            _nodes.AddRange(Records(src, FileResearch, Mapper<GpuResearchNodeDto>(RequiredNode), ValidateNode, problems));
            _allSegments.AddRange(Records(src, FileSegments, Mapper<GpuSegmentDto>(RequiredSegment), ValidateSegment, problems));
            _eras.AddRange(Records(src, FileEras, Mapper<GpuEraDto>(RequiredEra), ValidateEra, problems));
            _vendors.AddRange(Records(src, FileVendors, Mapper<GpuVendorDto>(RequiredVendor), ValidateVendor, problems));
            _consoles.AddRange(Records(src, FileConsoles, Mapper<GpuConsoleDto>(RequiredConsole), ValidateConsole, problems));

            foreach (GpuSegmentDto segment in _allSegments)
            {
                if (segment.mode == Balance.marketMode) _segments.Add(segment);
            }

            Index(_architectures, _archById);
            Index(_memories, _memById);
            Index(_coolers, _coolerById);

            CheckResearch(problems);
            CheckEras(problems);
            Index(_allSegments, _anySegmentById);
            Index(_segments, _segmentById);
            CheckVendors(problems);

            Index(_nodes, _nodeById);
            Index(_eras, _eraById);
            Index(_vendors, _vendorById);
            Index(_consoles, _consoleById);
        }

        private static void Index<T>(List<T> list, Dictionary<string, T> index) where T : class, IContentRecord
        {
            index.Clear();
            foreach (T item in list) index[item.Id] = item;
        }

        private static List<T> Records<T>(IModContentSource src, string file, Func<JsonObject, List<string>, T> map,
            Func<T, List<string>, bool> validate, List<string> problems) where T : class, IContentRecord
        {
            try
            {
                IReadOnlyList<T> loaded = src.LoadRecords(file, map, validate);
                return loaded == null ? new List<T>() : new List<T>(loaded);
            }
            catch (Exception ex)
            {
                problems.Add(file + ".json: loading failed: " + ex.GetType().Name + ": " + ex.Message);
                return new List<T>();
            }
        }

        // ---- the mapper of a record ----

        private static readonly string[] RequiredArch =
        {
            "id", "year", "transistorsPerUnit", "opsPerClock", "clockRatio", "bytesPerOp", "powerCoef", "complexity", "dieMaterialCost", "projectCost", "projectDays",
        };

        private static readonly string[] RequiredMemory =
        {
            "id", "year", "dataRateMTs", "costPerMB", "maxVramMB", "busWidths", "projectCost", "projectDays",
        };

        private static readonly string[] RequiredCooler = { "id", "year", "thermalResistance", "cost" };
        private static readonly string[] RequiredNode = { "id", "branch", "unlocks", "year", "baseDays", "monthlyCost" };
        private static readonly string[] RequiredSegment = { "id", "mode", "opensYear", "baseSize", "sizeByDecade", "budgetByDecade" };
        private static readonly string[] RequiredEra = { "id", "startYear" };
        private static readonly string[] RequiredVendor = { "id", "entryYear", "segments" };
        private static readonly string[] RequiredConsole = { "id", "clientKey", "offerYear", "termYears" };

        /// <summary>
        /// Builds the mapper of a record type: the required fields must be present, the JSON must bind without errors; fields the DTO does not
        /// have are noted (they become warnings when the record is accepted).
        /// </summary>
        private static Func<JsonObject, List<string>, T> Mapper<T>(string[] required) where T : class, IContentRecord, new()
        {
            return (json, messages) =>
            {
                var errors = new List<string>();
                foreach (string key in required)
                {
                    JsonNode node = json[key];
                    if (node == null || node.IsNull) errors.Add("missing required field '" + key + "'");
                }

                T dto = ModJson.Bind<T>(json, errors);
                if (errors.Count > 0 || dto == null)
                {
                    messages.AddRange(errors);
                    return null;
                }

                GpuJsonShape.CollectUnknown(json, typeof(T), DisabledOnly, messages);
                return dto;
            };
        }

        // ---- validation of a record ----

        private static bool ValidateArchitecture(GpuArchitectureDto d, List<string> messages)
        {
            var c = new Check();
            c.SafeId("id", d.id);
            c.LocKey("nameKey", d.nameKey);
            c.Year("year", d.year);
            c.Positive("transistorsPerUnit", d.transistorsPerUnit);
            c.Range("opsPerClock", d.opsPerClock, 1f, 1000f);
            c.Range("clockRatio", d.clockRatio, 0.001f, 2f);
            c.Positive("bytesPerOp", d.bytesPerOp);
            c.Positive("powerCoef", d.powerCoef);
            c.Range("complexity", d.complexity, 0f, 10f);
            c.NonNegative("dieMaterialCost", d.dieMaterialCost);
            c.NonNegative("projectCost", d.projectCost);
            c.IntRange("projectDays", d.projectDays, 1, 3650);
            if (d.features == null) d.features = new List<string>();
            c.Names("features", d.features);
            return c.Finish(messages);
        }

        private static bool ValidateMemory(GpuMemoryDto d, List<string> messages)
        {
            var c = new Check();
            c.SafeId("id", d.id);
            c.LocKey("nameKey", d.nameKey);
            c.Year("year", d.year);
            c.Positive("dataRateMTs", d.dataRateMTs);
            c.NonNegative("costPerMB", d.costPerMB);
            c.Range("costDecayPerYear", d.costDecayPerYear, 0.001f, 1f);
            c.Range("costFloor", d.costFloor, 0f, 1f);
            c.Positive("maxVramMB", d.maxVramMB);
            c.NonNegative("projectCost", d.projectCost);
            c.IntRange("projectDays", d.projectDays, 1, 3650);
            if (d.busWidths == null) d.busWidths = new List<int>();
            if (d.busWidths.Count == 0) c.Error("busWidths must not be empty");
            foreach (int width in d.busWidths)
            {
                if (width <= 0) c.Error("busWidths has " + width.ToString(CultureInfo.InvariantCulture) + ", every width must be above 0");
            }
            return c.Finish(messages);
        }

        private static bool ValidateCooler(GpuCoolerDto d, List<string> messages)
        {
            var c = new Check();
            c.SafeId("id", d.id);
            c.LocKey("nameKey", d.nameKey);
            c.Year("year", d.year);
            c.Positive("thermalResistance", d.thermalResistance);
            c.NonNegative("cost", d.cost);
            return c.Finish(messages);
        }

        private static bool ValidateNode(GpuResearchNodeDto d, List<string> messages)
        {
            var c = new Check();
            c.SafeId("id", d.id);
            if (d.branch != BranchArchitecture && d.branch != BranchMemory && d.branch != BranchCooling)
                c.Error("branch must be '" + BranchArchitecture + "', '" + BranchMemory + "' or '" + BranchCooling + "', got '" + d.branch + "'");
            c.SafeId("unlocks", d.unlocks);
            c.Year("year", d.year);
            c.IntRange("baseDays", d.baseDays, 1, 3650);
            c.Range("monthlyCost", d.monthlyCost, 0f, 1e12f);
            if (d.requiresNodes == null) d.requiresNodes = new List<string>();
            if (d.requiresGameTechs == null) d.requiresGameTechs = new List<string>();
            c.Names("requiresNodes", d.requiresNodes);
            c.Names("requiresGameTechs", d.requiresGameTechs);
            return c.Finish(messages);
        }

        private static bool ValidateSegment(GpuSegmentDto d, List<string> messages)
        {
            var c = new Check();
            c.SafeId("id", d.id);
            if (d.mode != ModeSingle && d.mode != ModeEras) c.Error("mode must be '" + ModeSingle + "' or '" + ModeEras + "', got '" + d.mode + "'");
            c.LocKey("nameKey", d.nameKey);
            c.Year("opensYear", d.opensYear);
            if (d.closesYear != 0 && (d.closesYear < d.opensYear || d.closesYear > MaxYear))
                c.Error("closesYear must be 0 (never) or in [opensYear, " + MaxYear.ToString(CultureInfo.InvariantCulture) + "], got " + d.closesYear.ToString(CultureInfo.InvariantCulture));
            c.NonNegative("baseSize", d.baseSize);

            if (d.sizeByDecade == null) d.sizeByDecade = new List<float>();
            if (d.budgetByDecade == null) d.budgetByDecade = new List<float>();
            c.Decades("sizeByDecade", d.sizeByDecade, false);
            c.Decades("budgetByDecade", d.budgetByDecade, true);

            c.Positive("consumptionTarget", d.consumptionTarget);
            c.NonNegative("wPerf", d.wPerf);
            c.NonNegative("wUnder", d.wUnder);
            c.NonNegative("wOver", d.wOver);
            c.NonNegative("wObs", d.wObs);
            c.NonNegative("wCons", d.wCons);
            c.Range("missingFeatureFactor", d.missingFeatureFactor, 0f, 1f);
            c.Range("rivalDieShare", d.rivalDieShare, 0f, 1f);
            c.Positive("rivalVramFactor", d.rivalVramFactor);
            c.IntRange("rivalBusTier", d.rivalBusTier, 0, 16);

            if (d.featureReqs == null) d.featureReqs = new List<GpuYearFeats>();
            d.featureReqs.RemoveAll(entry => entry == null);
            foreach (GpuYearFeats entry in d.featureReqs)
            {
                c.Year("featureReqs.year", entry.year);
                if (entry.f == null) entry.f = new List<string>();
                c.Names("featureReqs[" + entry.year.ToString(CultureInfo.InvariantCulture) + "].f", entry.f);
            }
            d.featureReqs = d.featureReqs.OrderBy(entry => entry.year).ToList();
            c.DistinctYears("featureReqs", d.featureReqs.Select(entry => entry.year));

            if (d.vramReq == null) d.vramReq = new List<GpuYearValue>();
            d.vramReq.RemoveAll(entry => entry == null);
            foreach (GpuYearValue entry in d.vramReq)
            {
                c.Year("vramReq.year", entry.year);
                c.Positive("vramReq[" + entry.year.ToString(CultureInfo.InvariantCulture) + "].v", entry.v);
            }
            d.vramReq = d.vramReq.OrderBy(entry => entry.year).ToList();
            c.DistinctYears("vramReq", d.vramReq.Select(entry => entry.year));
            return c.Finish(messages);
        }

        private static bool ValidateEra(GpuEraDto d, List<string> messages)
        {
            var c = new Check();
            c.SafeId("id", d.id);
            c.LocKey("nameKey", d.nameKey);
            c.LocKey("mailKey", d.mailKey);
            c.Year("startYear", d.startYear);
            if (d.hintNodes == null) d.hintNodes = new List<string>();
            c.Names("hintNodes", d.hintNodes);
            return c.Finish(messages);
        }

        private static bool ValidateVendor(GpuVendorDto d, List<string> messages)
        {
            var c = new Check();
            c.SafeId("id", d.id);
            string bind = d.bindTo ?? string.Empty;
            if (bind.Length > 0 && !CompanyKey.IsAi(bind))
                c.Error("bindTo must be empty or ai:<number>, got '" + bind + "'");
            if (bind.Length == 0 && d.fallbackVirtual) c.Warn("fallbackVirtual means nothing without bindTo");

            bool virtualCapable = bind.Length == 0 || d.fallbackVirtual;
            bool hasName = d.name != null && (!string.IsNullOrWhiteSpace(d.name.ru) || !string.IsNullOrWhiteSpace(d.name.en));
            if (virtualCapable && !hasName) c.Error("name (ru or en) is required for a vendor that can be virtual");

            c.Year("entryYear", d.entryYear);
            c.YearOrZero("exitYear", d.exitYear, d.entryYear);
            c.YearOrZero("mergeYear", d.mergeYear, d.entryYear);
            if (string.IsNullOrEmpty(d.mergeInto) && d.mergeYear != 0) c.Warn("mergeYear is ignored without mergeInto");
            if (!string.IsNullOrEmpty(d.mergeInto) && d.mergeYear == 0) c.Warn("mergeInto is ignored without mergeYear");
            if (d.mergeInto == null) d.mergeInto = string.Empty;
            if (d.bindTo == null) d.bindTo = string.Empty;

            c.Range("techLag", d.techLag, 0f, 30f);
            c.Range("priceFactor", d.priceFactor, 0.01f, 100f);
            c.Range("capacityShare", d.capacityShare, 0f, 1f);
            c.Range("strength", d.strength, 0f, 10f);
            c.IntRange("designIntervalMonths", d.designIntervalMonths, 1, 240);
            if (d.segments == null) d.segments = new List<string>();
            c.Names("segments", d.segments);
            if (!string.IsNullOrEmpty(d.color) && !IsColor(d.color)) c.Error("color must look like #RRGGBB, got '" + d.color + "'");
            return c.Finish(messages);
        }

        private static bool ValidateConsole(GpuConsoleDto d, List<string> messages)
        {
            var c = new Check();
            c.SafeId("id", d.id);
            if (!HasKeyPrefix(d.clientKey)) c.Error("clientKey must be an L10n key with the prefix 'gpu.', got '" + d.clientKey + "'");
            c.IntRange("offerYear", d.offerYear, 1981, MaxYear);
            c.IntRange("termYears", d.termYears, 1, 15);
            return c.Finish(messages);
        }

        private static bool IsColor(string text)
        {
            if (text.Length != 7 || text[0] != '#') return false;
            for (int i = 1; i < text.Length; i++)
            {
                char ch = text[i];
                bool hex = (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        /// <summary>An L10n key of this feature: the prefix <c>gpu.</c> and something after it, no blanks (FR-25).</summary>
        private static bool HasKeyPrefix(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length <= KeyPrefix.Length || !key.StartsWith(KeyPrefix, StringComparison.Ordinal)) return false;
            foreach (char ch in key)
            {
                if (char.IsWhiteSpace(ch)) return false;
            }
            return true;
        }

        // ---- checks across files ----

        private void CheckResearch(List<string> problems)
        {
            var skipped = new HashSet<string>(StringComparer.Ordinal);

            // unlocks: an existing record of the node's branch
            foreach (GpuResearchNodeDto node in _nodes.ToArray())
            {
                bool known = node.branch == BranchArchitecture ? _archById.ContainsKey(node.unlocks)
                    : node.branch == BranchMemory ? _memById.ContainsKey(node.unlocks)
                    : _coolerById.ContainsKey(node.unlocks);
                if (known) continue;
                problems.Add(RecordProblem(FileResearch, node.id, "unlocks '" + node.unlocks + "', but there is no such record in the " + BranchFileOf(node.branch) + " file; the node is skipped"));
                skipped.Add(node.id);
                _nodes.Remove(node);
            }

            // requiresNodes: existing nodes, no cycles; whatever depends on a skipped node goes too
            var ids = _nodes.Select(node => node.id).ToList();
            var byId = _nodes.ToDictionary(node => node.id, StringComparer.Ordinal);
            Dictionary<string, string> broken = FindUnresolvable(ids, id => byId[id].requiresNodes, skipped);
            if (broken.Count > 0)
            {
                foreach (GpuResearchNodeDto node in _nodes.ToArray())
                {
                    string reason;
                    if (!broken.TryGetValue(node.id, out reason)) continue;
                    problems.Add(RecordProblem(FileResearch, node.id, "requiresNodes: " + reason + "; the node is skipped"));
                    _nodes.Remove(node);
                }
            }

            // a record nobody unlocks cannot be researched
            var unlocked = new HashSet<string>(_nodes.Select(node => node.branch + "|" + node.unlocks), StringComparer.Ordinal);
            foreach (GpuArchitectureDto arch in _architectures)
            {
                if (!unlocked.Contains(BranchArchitecture + "|" + arch.id))
                    problems.Add(RecordProblem(FileArchitectures, arch.id, "no research node unlocks it; it can only be used when a research node is added"));
            }
            foreach (GpuMemoryDto mem in _memories)
            {
                if (!unlocked.Contains(BranchMemory + "|" + mem.id))
                    problems.Add(RecordProblem(FileMemory, mem.id, "no research node unlocks it; it can only be used when a research node is added"));
            }
            foreach (GpuCoolerDto cooler in _coolers)
            {
                if (!unlocked.Contains(BranchCooling + "|" + cooler.id))
                    problems.Add(RecordProblem(FileCoolers, cooler.id, "no research node unlocks it; it can only be used when a research node is added"));
            }
        }

        private static string BranchFileOf(string branch)
        {
            return branch == BranchArchitecture ? FileArchitectures + ".json" : branch == BranchMemory ? FileMemory + ".json" : FileCoolers + ".json";
        }

        private void CheckEras(List<string> problems)
        {
            var nodeIds = new HashSet<string>(_nodes.Select(node => node.id), StringComparer.Ordinal);
            foreach (GpuEraDto era in _eras)
            {
                for (int i = era.hintNodes.Count - 1; i >= 0; i--)
                {
                    if (nodeIds.Contains(era.hintNodes[i])) continue;
                    problems.Add(RecordProblem(FileEras, era.id, "hintNodes names '" + era.hintNodes[i] + "', which is not a research node; the hint is dropped"));
                    era.hintNodes.RemoveAt(i);
                }
            }
        }

        private void CheckVendors(List<string> problems)
        {
            // mergeInto: an existing vendor, no cycles (a vendor that merges into itself is a cycle of one)
            var ids = _vendors.Select(vendor => vendor.id).ToList();
            var byId = _vendors.ToDictionary(vendor => vendor.id, StringComparer.Ordinal);
            Dictionary<string, string> broken = FindUnresolvable(ids,
                id => string.IsNullOrEmpty(byId[id].mergeInto) ? new List<string>() : new List<string> { byId[id].mergeInto },
                new HashSet<string>(StringComparer.Ordinal));
            if (broken.Count > 0)
            {
                foreach (GpuVendorDto vendor in _vendors.ToArray())
                {
                    string reason;
                    if (!broken.TryGetValue(vendor.id, out reason)) continue;
                    problems.Add(RecordProblem(FileVendors, vendor.id, "mergeInto: " + reason + "; the vendor is skipped"));
                    _vendors.Remove(vendor);
                }
            }

            // segments: unknown in both modes is a mistake; a segment of the other mode is fine, it is skipped while that mode is not current
            foreach (GpuVendorDto vendor in _vendors)
            {
                for (int i = vendor.segments.Count - 1; i >= 0; i--)
                {
                    if (_anySegmentById.ContainsKey(vendor.segments[i])) continue;
                    problems.Add(RecordProblem(FileVendors, vendor.id, "segments names '" + vendor.segments[i] + "', which is no segment of either market mode; it is dropped"));
                    vendor.segments.RemoveAt(i);
                }
            }
        }

        private static string RecordProblem(string file, string id, string message)
        {
            return file + ".json record '" + id + "': " + message;
        }

        /// <summary>
        /// The ids that cannot be resolved in a graph where each id lists the ids it needs: needing an id that does not exist, needing one that was
        /// skipped (<paramref name="skippedBefore"/> or found here), or being on a cycle or behind one. The value is the reason (English).
        /// </summary>
        private static Dictionary<string, string> FindUnresolvable(List<string> ids, Func<string, List<string>> requires, HashSet<string> skippedBefore)
        {
            var reasons = new Dictionary<string, string>(StringComparer.Ordinal);
            var alive = new HashSet<string>(ids, StringComparer.Ordinal);

            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (string id in ids)
                {
                    if (!alive.Contains(id)) continue;
                    foreach (string need in requires(id))
                    {
                        if (alive.Contains(need)) continue;
                        reasons[id] = skippedBefore.Contains(need) || reasons.ContainsKey(need)
                            ? "'" + need + "' is skipped"
                            : "'" + need + "' does not exist";
                        alive.Remove(id);
                        changed = true;
                        break;
                    }
                }
            }

            // what can be put in order (everything it needs is in order) is fine; the rest is on a cycle or behind one
            var resolved = new HashSet<string>(StringComparer.Ordinal);
            bool progress = true;
            while (progress)
            {
                progress = false;
                foreach (string id in ids)
                {
                    if (!alive.Contains(id) || resolved.Contains(id)) continue;
                    bool ready = true;
                    foreach (string need in requires(id))
                    {
                        if (!resolved.Contains(need))
                        {
                            ready = false;
                            break;
                        }
                    }
                    if (!ready) continue;
                    resolved.Add(id);
                    progress = true;
                }
            }

            var stuck = ids.Where(id => alive.Contains(id) && !resolved.Contains(id)).ToList();
            var stuckSet = new HashSet<string>(stuck, StringComparer.Ordinal);
            foreach (string id in stuck)
            {
                reasons[id] = OnCycle(id, requires, stuckSet) ? "'" + id + "' is part of a dependency cycle" : "it needs an id that is part of a dependency cycle";
            }
            return reasons;
        }

        private static bool OnCycle(string start, Func<string, List<string>> requires, HashSet<string> within)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var stack = new Stack<string>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                string id = stack.Pop();
                foreach (string need in requires(id))
                {
                    if (need == start) return true;
                    if (within.Contains(need) && seen.Add(need)) stack.Push(need);
                }
            }
            return false;
        }

        // ---- timeline ----

        private static GpuTimelineDto LoadTimeline(IModContentSource src, List<string> problems)
        {
            JsonObject json = ReadConfig(src, FileTimeline, problems);
            var timeline = new GpuTimelineDto();
            if (json.Count == 0)
            {
                problems.Add(FileTimeline + ".json: no data; there is no process node and no die size limit");
                return timeline;
            }

            var errors = new List<string>();
            GpuTimelineDto bound = ModJson.Bind<GpuTimelineDto>(json, errors);
            foreach (string error in errors) problems.Add(FileTimeline + ".json: " + error);
            if (bound != null) timeline = bound;

            var notes = new List<string>();
            GpuJsonShape.CollectUnknown(json, typeof(GpuTimelineDto), SchemaOnly, notes);
            foreach (string note in notes) problems.Add(FileTimeline + ".json: " + note);

            if (timeline.nodeByYear == null) timeline.nodeByYear = new List<GpuYearNode>();
            if (timeline.maxDieByYear == null) timeline.maxDieByYear = new List<GpuYearValue>();

            timeline.nodeByYear = CleanTable(timeline.nodeByYear, entry => entry == null ? 0 : entry.year, entry =>
            {
                if (entry.year < MinYear || entry.year > MaxYear) return "year " + entry.year.ToString(CultureInfo.InvariantCulture) + " is outside [" + MinYear.ToString(CultureInfo.InvariantCulture) + ", " + MaxYear.ToString(CultureInfo.InvariantCulture) + "]";
                if (string.IsNullOrWhiteSpace(entry.node)) return "node is empty";
                return null;
            }, "nodeByYear", problems);

            timeline.maxDieByYear = CleanTable(timeline.maxDieByYear, entry => entry == null ? 0 : entry.year, entry =>
            {
                if (entry.year < MinYear || entry.year > MaxYear) return "year " + entry.year.ToString(CultureInfo.InvariantCulture) + " is outside [" + MinYear.ToString(CultureInfo.InvariantCulture) + ", " + MaxYear.ToString(CultureInfo.InvariantCulture) + "]";
                if (float.IsNaN(entry.v) || float.IsInfinity(entry.v) || entry.v <= 0f) return "v must be above 0";
                return null;
            }, "maxDieByYear", problems);

            if (timeline.nodeByYear.Count == 0) problems.Add(FileTimeline + ".json: nodeByYear is empty; there is no process node by year");
            if (timeline.maxDieByYear.Count == 0) problems.Add(FileTimeline + ".json: maxDieByYear is empty; the designer has no die size limit");
            return timeline;
        }

        /// <summary>Drops the entries that are null or fail <paramref name="problemOf"/>, sorts the rest by year and drops a repeated year (the first stays).</summary>
        private static List<T> CleanTable<T>(List<T> table, Func<T, int> yearOf, Func<T, string> problemOf, string name, List<string> problems) where T : class
        {
            var kept = new List<T>();
            foreach (T entry in table)
            {
                if (entry == null)
                {
                    problems.Add(FileTimeline + ".json: " + name + " has an empty entry; it is dropped");
                    continue;
                }
                string problem = problemOf(entry);
                if (problem != null)
                {
                    problems.Add(FileTimeline + ".json: " + name + " entry " + yearOf(entry).ToString(CultureInfo.InvariantCulture) + ": " + problem + "; it is dropped");
                    continue;
                }
                kept.Add(entry);
            }

            kept = kept.OrderBy(yearOf).ToList();
            for (int i = kept.Count - 1; i > 0; i--)
            {
                if (yearOf(kept[i]) != yearOf(kept[i - 1])) continue;
                problems.Add(FileTimeline + ".json: " + name + " names the year " + yearOf(kept[i]).ToString(CultureInfo.InvariantCulture) + " twice; the later entry is dropped");
                kept.RemoveAt(i);
            }
            return kept;
        }

        private static JsonObject ReadConfig(IModContentSource src, string file, List<string> problems)
        {
            try
            {
                return src.LoadConfig(file) ?? new JsonObject();
            }
            catch (Exception ex)
            {
                problems.Add(file + ".json: loading failed: " + ex.GetType().Name + ": " + ex.Message);
                return new JsonObject();
            }
        }

        // ---- balance ----

        private static GpuBalanceDto LoadBalance(IModContentSource src, List<string> problems)
        {
            JsonObject json = ReadConfig(src, FileBalance, problems);
            if (json.Count == 0)
            {
                problems.Add(FileBalance + ".json: no data; the shipped defaults are used");
                return new GpuBalanceDto();
            }

            var errors = new List<string>();
            GpuBalanceDto balance = ModJson.Bind<GpuBalanceDto>(json, errors);
            foreach (string error in errors) problems.Add(FileBalance + ".json: " + error);
            if (balance == null) balance = new GpuBalanceDto();

            var notes = new List<string>();
            GpuJsonShape.CollectUnknown(json, typeof(GpuBalanceDto), SchemaOnly, notes);
            foreach (string note in notes) problems.Add(FileBalance + ".json: " + note);

            CheckBalance(balance, problems);
            return balance;
        }

        /// <summary>Puts the shipped value back for every number outside its range (and every missing block), saying so in <paramref name="problems"/>.</summary>
        private static void CheckBalance(GpuBalanceDto b, List<string> problems)
        {
            var d = new GpuBalanceDto();
            var f = new Fixer(problems);

            if (b.marketMode != ModeSingle && b.marketMode != ModeEras)
            {
                problems.Add(FileBalance + ".json: marketMode must be '" + ModeSingle + "' or '" + ModeEras + "', got '" + b.marketMode + "'; '" + d.marketMode + "' is used");
                b.marketMode = d.marketMode;
            }

            if (b.market == null) b.market = Missing(f, "market", d.market);
            if (b.founding == null) b.founding = Missing(f, "founding", d.founding);
            if (b.research == null) b.research = Missing(f, "research", d.research);
            if (b.design == null) b.design = Missing(f, "design", d.design);
            if (b.production == null) b.production = Missing(f, "production", d.production);
            if (b.popularity == null) b.popularity = Missing(f, "popularity", d.popularity);
            if (b.background == null) b.background = Missing(f, "background", d.background);
            if (b.rivals == null) b.rivals = Missing(f, "rivals", d.rivals);
            if (b.synergy == null) b.synergy = Missing(f, "synergy", d.synergy);
            if (b.news == null) b.news = Missing(f, "news", d.news);
            if (b.console == null) b.console = Missing(f, "console", d.console);
            if (b.limits == null) b.limits = Missing(f, "limits", d.limits);

            f.I("market.opensYear", ref b.market.opensYear, MinYear, MaxYear, d.market.opensYear);

            f.F("founding.cost", ref b.founding.cost, 0f, 1e12f, d.founding.cost);
            f.I("founding.catchUpLagYears", ref b.founding.catchUpLagYears, 0, 50, d.founding.catchUpLagYears);

            f.F("research.penaltyPerYear", ref b.research.penaltyPerYear, 0f, 100f, d.research.penaltyPerYear);
            f.F("research.bonusPerYear", ref b.research.bonusPerYear, 0f, 1f, d.research.bonusPerYear);
            f.F("research.maxBonus", ref b.research.maxBonus, 0f, 0.99f, d.research.maxBonus);
            f.F("research.fundingMin", ref b.research.fundingMin, 0.01f, 1f, d.research.fundingMin);
            f.F("research.fundingMax", ref b.research.fundingMax, 0.01f, 1f, d.research.fundingMax);
            f.I("research.queueMax", ref b.research.queueMax, 1, 50, d.research.queueMax);
            if (b.research.fundingMin > b.research.fundingMax)
            {
                problems.Add(FileBalance + ".json: research.fundingMin is above research.fundingMax; the shipped values are used");
                b.research.fundingMin = d.research.fundingMin;
                b.research.fundingMax = d.research.fundingMax;
            }

            f.F("design.minDie", ref b.design.minDie, 1f, 5000f, d.design.minDie);
            f.F("design.clockMinShare", ref b.design.clockMinShare, 0.01f, 1f, d.design.clockMinShare);
            f.F("design.bandwidthExponent", ref b.design.bandwidthExponent, 0f, 3f, d.design.bandwidthExponent);
            f.F("design.bandwidthRatioMin", ref b.design.bandwidthRatioMin, 0.001f, 1f, d.design.bandwidthRatioMin);
            f.F("design.densityExponent", ref b.design.densityExponent, -2f, 2f, d.design.densityExponent);
            f.F("design.basePowerW", ref b.design.basePowerW, 0f, 1000f, d.design.basePowerW);
            f.F("design.ambientC", ref b.design.ambientC, -50f, 100f, d.design.ambientC);
            f.F("design.safeTempC", ref b.design.safeTempC, 1f, 300f, d.design.safeTempC);
            f.F("design.frTempK", ref b.design.frTempK, 0f, 1000f, d.design.frTempK);
            f.F("design.frClockK", ref b.design.frClockK, 0f, 1000f, d.design.frClockK);
            f.F("design.boardBase", ref b.design.boardBase, 0f, 1e6f, d.design.boardBase);
            f.F("design.boardCostPerBit", ref b.design.boardCostPerBit, 0f, 1e4f, d.design.boardCostPerBit);
            f.F("design.nodeProjectShare", ref b.design.nodeProjectShare, 0f, 10f, d.design.nodeProjectShare);
            f.I("design.maxParallelProjects", ref b.design.maxParallelProjects, 1, 50, d.design.maxParallelProjects);
            f.I("design.nameMaxLength", ref b.design.nameMaxLength, 4, 128, d.design.nameMaxLength);
            if (b.design.safeTempC <= b.design.ambientC)
            {
                problems.Add(FileBalance + ".json: design.safeTempC must be above design.ambientC; the shipped values are used");
                b.design.safeTempC = d.design.safeTempC;
                b.design.ambientC = d.design.ambientC;
            }

            f.F("production.autoDemandFactor", ref b.production.autoDemandFactor, 0f, 10f, d.production.autoDemandFactor);
            f.F("production.autoMaxShare", ref b.production.autoMaxShare, 0f, 1f, d.production.autoMaxShare);
            f.F("production.storageCostPerUnitMonth", ref b.production.storageCostPerUnitMonth, 0f, 1e4f, d.production.storageCostPerUnitMonth);
            f.F("production.liquidationShare", ref b.production.liquidationShare, 0f, 1f, d.production.liquidationShare);
            f.F("production.fablessYieldProgress", ref b.production.fablessYieldProgress, 0f, 1f, d.production.fablessYieldProgress);

            f.F("popularity.start", ref b.popularity.start, 0f, 1f, d.popularity.start);
            f.F("popularity.floor", ref b.popularity.floor, 0f, 1f, d.popularity.floor);
            f.F("popularity.cap", ref b.popularity.cap, 0.01f, 1f, d.popularity.cap);
            f.F("popularity.gainBase", ref b.popularity.gainBase, 0f, 1f, d.popularity.gainBase);
            f.F("popularity.baselineGain", ref b.popularity.baselineGain, 0f, 1f, d.popularity.baselineGain);
            f.F("popularity.oldDecay", ref b.popularity.oldDecay, 0f, 1f, d.popularity.oldDecay);
            f.I("popularity.oldYears", ref b.popularity.oldYears, 1, 50, d.popularity.oldYears);
            f.I("popularity.oldYearsBefore1990", ref b.popularity.oldYearsBefore1990, 1, 50, d.popularity.oldYearsBefore1990);
            f.F("popularity.brandCarry", ref b.popularity.brandCarry, 0f, 1f, d.popularity.brandCarry);
            if (b.popularity.floor > b.popularity.cap)
            {
                problems.Add(FileBalance + ".json: popularity.floor is above popularity.cap; the shipped values are used");
                b.popularity.floor = d.popularity.floor;
                b.popularity.cap = d.popularity.cap;
            }
            if (b.popularity.eraYears == null || b.popularity.eraYears.Count != 2 || b.popularity.eraYears[0] < MinYear
                || b.popularity.eraYears[0] > b.popularity.eraYears[1] || b.popularity.eraYears[1] > MaxYear)
            {
                problems.Add(FileBalance + ".json: popularity.eraYears must be two ascending years; the shipped values are used");
                b.popularity.eraYears = new List<int>(d.popularity.eraYears);
            }
            f.Floats("popularity.eraGain", ref b.popularity.eraGain, 3, 0f, 100f, d.popularity.eraGain);
            f.Floats("popularity.eraBaseline", ref b.popularity.eraBaseline, 3, 0f, 100f, d.popularity.eraBaseline);

            f.F("background.techLagYears", ref b.background.techLagYears, 0f, 30f, d.background.techLagYears);
            f.F("background.popularity", ref b.background.popularity, 0f, 1f, d.background.popularity);
            f.I("background.refreshMonths", ref b.background.refreshMonths, 1, 120, d.background.refreshMonths);
            f.F("background.priceFactor", ref b.background.priceFactor, 0.01f, 100f, d.background.priceFactor);

            f.I("rivals.maxProductsPerSegment", ref b.rivals.maxProductsPerSegment, 1, 20, d.rivals.maxProductsPerSegment);
            f.I("rivals.intervalJitterMonths", ref b.rivals.intervalJitterMonths, 0, 60, d.rivals.intervalJitterMonths);
            f.F("rivals.priceFloorMarkup", ref b.rivals.priceFloorMarkup, 1f, 100f, d.rivals.priceFloorMarkup);
            f.F("rivals.targetTempC", ref b.rivals.targetTempC, 0f, 300f, d.rivals.targetTempC);
            f.F("rivals.clockShare", ref b.rivals.clockShare, 0.01f, 1.5f, d.rivals.clockShare);
            f.F("rivals.dieJitter", ref b.rivals.dieJitter, 0f, 0.5f, d.rivals.dieJitter);

            f.F("synergy.cpuMax", ref b.synergy.cpuMax, 0f, 1f, d.synergy.cpuMax);
            f.F("synergy.cpuK", ref b.synergy.cpuK, 0f, 100f, d.synergy.cpuK);
            f.F("synergy.gpuMax", ref b.synergy.gpuMax, 0f, 1f, d.synergy.gpuMax);
            f.F("synergy.gpuK", ref b.synergy.gpuK, 0f, 100f, d.synergy.gpuK);
            f.I("synergy.shareWindowDays", ref b.synergy.shareWindowDays, 1, 366, d.synergy.shareWindowDays);
            f.F("synergy.gpuLearningShare", ref b.synergy.gpuLearningShare, 0f, 1f, d.synergy.gpuLearningShare);
            f.I("synergy.coDevYears", ref b.synergy.coDevYears, 0, 20, d.synergy.coDevYears);
            f.F("synergy.coDevFactor", ref b.synergy.coDevFactor, 0.01f, 1f, d.synergy.coDevFactor);

            f.I("news.leaderCooldownMonths", ref b.news.leaderCooldownMonths, 0, 120, d.news.leaderCooldownMonths);

            f.F("console.unitsShare", ref b.console.unitsShare, 0f, 1f, d.console.unitsShare);
            f.F("console.minPerfShare", ref b.console.minPerfShare, 0f, 1f, d.console.minPerfShare);
            f.F("console.priceFactor", ref b.console.priceFactor, 0.01f, 100f, d.console.priceFactor);
            f.F("console.priceFloorMarkup", ref b.console.priceFloorMarkup, 1f, 100f, d.console.priceFloorMarkup);
            f.S("console.segment", ref b.console.segment, d.console.segment);
            f.S("console.fallbackSegment", ref b.console.fallbackSegment, d.console.fallbackSegment);
            f.I("console.offerTimeoutDays", ref b.console.offerTimeoutDays, 1, 3650, d.console.offerTimeoutDays);
            f.I("console.revalidateDays", ref b.console.revalidateDays, 1, 365, d.console.revalidateDays);
            f.I("console.graceDays", ref b.console.graceDays, 1, 365, d.console.graceDays);
            f.F("console.failCancelShare", ref b.console.failCancelShare, 0f, 1f, d.console.failCancelShare);
            f.F("console.reputationPenalty", ref b.console.reputationPenalty, 0f, 100f, d.console.reputationPenalty);

            f.I("limits.products", ref b.limits.products, 1, 10000, d.limits.products);
            f.I("limits.vendors", ref b.limits.vendors, 1, 256, d.limits.vendors);
            f.I("limits.ledgerMonths", ref b.limits.ledgerMonths, 1, 2400, d.limits.ledgerMonths);
            f.I("limits.consoleOrders", ref b.limits.consoleOrders, 1, 256, d.limits.consoleOrders);
        }

        private static T Missing<T>(Fixer fixer, string block, T fallback)
        {
            fixer.Note("block '" + block + "' is missing or null; the shipped values are used");
            return fallback;
        }

        /// <summary>Range checks of the balance: a value that is not a finite number in range is replaced by the shipped one, with a line in the problems.</summary>
        private sealed class Fixer
        {
            private readonly List<string> _problems;

            public Fixer(List<string> problems)
            {
                _problems = problems;
            }

            public void Note(string message)
            {
                _problems.Add(FileBalance + ".json: " + message);
            }

            public void F(string name, ref float value, float min, float max, float fallback)
            {
                if (!float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max) return;
                Note(name + " = " + value.ToString(CultureInfo.InvariantCulture) + " is outside [" + Fmt(min) + ", " + Fmt(max) + "]; " + Fmt(fallback) + " is used");
                value = fallback;
            }

            public void I(string name, ref int value, int min, int max, int fallback)
            {
                if (value >= min && value <= max) return;
                Note(name + " = " + value.ToString(CultureInfo.InvariantCulture) + " is outside [" + min.ToString(CultureInfo.InvariantCulture) + ", " + max.ToString(CultureInfo.InvariantCulture) + "]; "
                     + fallback.ToString(CultureInfo.InvariantCulture) + " is used");
                value = fallback;
            }

            public void S(string name, ref string value, string fallback)
            {
                if (!string.IsNullOrWhiteSpace(value)) return;
                Note(name + " is empty; '" + fallback + "' is used");
                value = fallback;
            }

            public void Floats(string name, ref List<float> value, int count, float min, float max, List<float> fallback)
            {
                bool ok = value != null && value.Count == count;
                if (ok)
                {
                    foreach (float item in value)
                    {
                        if (float.IsNaN(item) || float.IsInfinity(item) || item < min || item > max) ok = false;
                    }
                }
                if (ok) return;
                Note(name + " must be " + count.ToString(CultureInfo.InvariantCulture) + " numbers in [" + Fmt(min) + ", " + Fmt(max) + "]; the shipped values are used");
                value = new List<float>(fallback);
            }

            private static string Fmt(float value)
            {
                return value.ToString("R", CultureInfo.InvariantCulture);
            }
        }

        // ---- the checks of one record ----

        /// <summary>Collects the problems of one record: errors reject it, warnings are kept for an accepted one.</summary>
        private sealed class Check
        {
            private readonly List<string> _errors = new List<string>();
            private readonly List<string> _warnings = new List<string>();

            public void Error(string message)
            {
                _errors.Add(message);
            }

            public void Warn(string message)
            {
                _warnings.Add(message);
            }

            public bool Finish(List<string> messages)
            {
                if (_errors.Count > 0)
                {
                    messages.AddRange(_errors);
                    return false;
                }
                messages.AddRange(_warnings);
                return true;
            }

            public void SafeId(string name, string value)
            {
                if (string.IsNullOrEmpty(value) || value.Length > 64)
                {
                    Error(name + " must be 1-64 characters, got '" + value + "'");
                    return;
                }
                foreach (char ch in value)
                {
                    bool ok = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '.' || ch == '_' || ch == '-';
                    if (ok) continue;
                    Error(name + " '" + value + "' may only contain letters, digits, '.', '_' and '-'");
                    return;
                }
            }

            public void LocKey(string name, string value)
            {
                if (string.IsNullOrEmpty(value)) return;
                if (!HasKeyPrefix(value)) Error(name + " must be an L10n key with the prefix 'gpu.', got '" + value + "'");
            }

            public void Year(string name, int value)
            {
                if (value < MinYear || value > MaxYear)
                    Error(name + " must be in [" + MinYear.ToString(CultureInfo.InvariantCulture) + ", " + MaxYear.ToString(CultureInfo.InvariantCulture) + "], got " + value.ToString(CultureInfo.InvariantCulture));
            }

            /// <summary>0 (none) or a year in [<paramref name="notBefore"/>, MaxYear].</summary>
            public void YearOrZero(string name, int value, int notBefore)
            {
                if (value == 0) return;
                if (value < MinYear || value > MaxYear || value < notBefore)
                    Error(name + " must be 0 or a year in [" + Math.Max(MinYear, notBefore).ToString(CultureInfo.InvariantCulture) + ", " + MaxYear.ToString(CultureInfo.InvariantCulture)
                          + "], got " + value.ToString(CultureInfo.InvariantCulture));
            }

            public void Positive(string name, float value)
            {
                if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f) Error(name + " must be above 0, got " + value.ToString(CultureInfo.InvariantCulture));
            }

            public void NonNegative(string name, float value)
            {
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) Error(name + " must not be negative, got " + value.ToString(CultureInfo.InvariantCulture));
            }

            public void Range(string name, float value, float min, float max)
            {
                if (float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max)
                    Error(name + " must be in [" + min.ToString(CultureInfo.InvariantCulture) + ", " + max.ToString(CultureInfo.InvariantCulture) + "], got " + value.ToString(CultureInfo.InvariantCulture));
            }

            public void IntRange(string name, int value, int min, int max)
            {
                if (value < min || value > max)
                    Error(name + " must be in [" + min.ToString(CultureInfo.InvariantCulture) + ", " + max.ToString(CultureInfo.InvariantCulture) + "], got " + value.ToString(CultureInfo.InvariantCulture));
            }

            /// <summary>Names in a list (features, node ids, game technologies): none empty; a repeated one is a warning.</summary>
            public void Names(string name, List<string> values)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (string value in values)
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        Error(name + " has an empty entry");
                        continue;
                    }
                    if (!seen.Add(value)) Warn(name + " names '" + value + "' twice");
                }
            }

            /// <summary>Exactly <see cref="DecadeCount"/> numbers, each finite and not negative (strictly positive when <paramref name="positive"/>).</summary>
            public void Decades(string name, List<float> values, bool positive)
            {
                if (values.Count != DecadeCount)
                {
                    Error(name + " must have " + DecadeCount.ToString(CultureInfo.InvariantCulture) + " values (1970s..2030s), got " + values.Count.ToString(CultureInfo.InvariantCulture));
                    return;
                }
                for (int i = 0; i < values.Count; i++)
                {
                    float v = values[i];
                    bool bad = float.IsNaN(v) || float.IsInfinity(v) || (positive ? v <= 0f : v < 0f);
                    if (bad) Error(name + "[" + i.ToString(CultureInfo.InvariantCulture) + "] must be " + (positive ? "above 0" : "0 or more") + ", got " + v.ToString(CultureInfo.InvariantCulture));
                }
            }

            public void DistinctYears(string name, IEnumerable<int> sortedYears)
            {
                int? previous = null;
                foreach (int year in sortedYears)
                {
                    if (previous.HasValue && previous.Value == year) Error(name + " names the year " + year.ToString(CultureInfo.InvariantCulture) + " twice");
                    previous = year;
                }
            }
        }
    }
}
