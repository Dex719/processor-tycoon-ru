using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The state of gpu-product-line that lives in the save: the section <see cref="Data"/> and the rules that keep it sane (FR-9, NFR-3, NFR-8;
    /// design.md "Правила проверки при загрузке"). Pure logic: no game, no Unity. The services of the feature change <see cref="Data"/> in place;
    /// the core's participant (<see cref="GpuSectionParticipant"/>) calls <see cref="Read"/>, <see cref="Write"/> and <see cref="ResetToDefaults"/>.
    /// <para>
    /// <b>Read</b> binds the JSON (<c>ModJson.Bind</c>; a field of the wrong type keeps its default), migrates a section of an older schema step by
    /// step (<c>Migrate_n_to_n+1</c>) and then <see cref="Sanitize"/>s it: a number that is not finite goes back to its default, a negative stock or
    /// price becomes 0, lists are cut to the limits of <c>balance.limits</c> (500 products, 32 vendors, 600 months of books, 32 console orders),
    /// unknown ids of the content are dropped where the thing cannot live without them (research nodes, a project's design) and kept where it can
    /// (a product lives on its snapshot), company keys must be <c>player</c>, <c>ai:&lt;digits&gt;</c> or empty. Every change is explained in
    /// <see cref="Warnings"/> and reported through the callback (the game logs it as <c>[gpu-product-line]</c>). Nothing throws on bad data.
    /// </para>
    /// <para>
    /// The section has no version field: <c>_schema</c> is the core's. A section that is newer than <see cref="SchemaVersion"/> never reaches
    /// <see cref="Read"/> (the core gives defaults and keeps the stored text), and if it does, it is not read.
    /// </para>
    /// </summary>
    public sealed class GpuState
    {
        /// <summary>The version of the section this code writes; the core stores it as <c>_schema</c>.</summary>
        public const int SchemaVersion = 1;

        /// <summary>The key of the section in <c>features</c> of the side file: the id of the feature.</summary>
        public const string SectionId = "gpu-product-line";

        public const string OwnerPlayer = "player", OwnerVendor = "vendor", OwnerBackground = "background";
        public const string StatusReady = "ready", StatusSelling = "selling", StatusRetired = "retired";
        public const string VendorPending = "pending", VendorActive = "active", VendorExited = "exited", VendorMerged = "merged";
        public const string OrderOffered = "offered", OrderActive = "active", OrderEnded = "ended", OrderCancelled = "cancelled", OrderDeclined = "declined";

        /// <summary>Longest list of launch dates (per vendor, and of the leader mails): a segment has one entry, so this is generous.</summary>
        private const int MaxLaunchEntries = 64;

        /// <summary>The most months a vendor's launch may be put off on top of its interval (the content draws 0-3; a hand-edited number is cut to this).</summary>
        private const int MaxExtraMonths = 24;

        /// <summary>Most companies a synergy table keeps.</summary>
        private const int MaxSynergyEntries = 256;

        private readonly GpuContentDb _content;
        private readonly Action<string> _warn;
        private readonly List<string> _warnings = new List<string>();
        private readonly GpuBalanceDto _defaultBalance = new GpuBalanceDto();

        /// <param name="content">The content, to check ids against (research nodes, architectures, console offers); null skips those checks and uses the shipped limits.</param>
        /// <param name="warn">Called for every change the checks make (the game logs it); may be null.</param>
        public GpuState(GpuContentDb content = null, Action<string> warn = null)
        {
            _content = content;
            _warn = warn;
            Data = new GpuSaveSection();
            MigratedFrom = -1;
        }

        /// <summary>The live section. Never null; replaced as a whole by <see cref="Read"/> and <see cref="ResetToDefaults"/>.</summary>
        public GpuSaveSection Data { get; private set; }

        /// <summary>What the last <see cref="Read"/> changed or dropped, one English line each; empty when the section was clean.</summary>
        public IReadOnlyList<string> Warnings
        {
            get { return _warnings; }
        }

        /// <summary>The schema the last <see cref="Read"/> migrated from, or -1 when it did not migrate.</summary>
        public int MigratedFrom { get; private set; }

        private GpuBalanceDto Balance
        {
            get { return _content != null && _content.Balance != null ? _content.Balance : _defaultBalance; }
        }

        // ---- the three calls of the core ----

        /// <summary>A new game, a save without the section, a section newer than this code: the empty state (the division is not founded, the market is not seeded).</summary>
        public void ResetToDefaults()
        {
            Data = new GpuSaveSection();
            MigratedFrom = -1;
            _warnings.Clear();
        }

        /// <summary>
        /// Reads the section the core gives (without <c>_schema</c>; <paramref name="schema"/> is the stored version, 1 when the field was missing).
        /// The object is not modified. Returns false when the section was not used and the state was reset instead.
        /// </summary>
        public bool Read(JsonObject section, int schema)
        {
            _warnings.Clear();
            MigratedFrom = -1;

            if (section == null)
            {
                Warn("section: there is no data, the empty state is used");
                Data = new GpuSaveSection();
                return false;
            }
            if (schema > SchemaVersion)
            {
                Warn("section: schema " + schema.ToString(CultureInfo.InvariantCulture) + " is newer than " + SchemaVersion.ToString(CultureInfo.InvariantCulture)
                     + ", it is not read (the core keeps it as it is)");
                Data = new GpuSaveSection();
                return false;
            }

            JsonObject source = section;
            if (schema < SchemaVersion)
            {
                int from = schema < 0 ? 0 : schema;
                var notes = new List<string>();
                source = Migrate(section, from, SchemaVersion, notes);
                MigratedFrom = from;
                foreach (string note in notes) Warn("section: " + note);
            }

            var errors = new List<string>();
            GpuSaveSection bound = ModJson.Bind<GpuSaveSection>(source, errors);
            foreach (string error in errors) Warn("section " + error);

            Data = Sanitize(bound ?? new GpuSaveSection());
            return true;
        }

        /// <summary>The section for the side file, without <c>_schema</c> (the core adds it). A fresh object each call.</summary>
        public JsonObject Write()
        {
            var errors = new List<string>();
            JsonNode node = ModJson.From(Data, errors);
            foreach (string error in errors) Report("section write: " + error);

            var result = node as JsonObject ?? new JsonObject();
            result.Remove("_schema");
            return result;
        }

        /// <summary>
        /// The next id of a thing the player makes (<c>d-17</c>, <c>p-17</c>, <c>g-9</c>): the prefix, a hyphen and a counter shared by all kinds.
        /// <see cref="Sanitize"/> lifts the counter above every id already in the section, so a new id never repeats an old one.
        /// </summary>
        public string NextId(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) prefix = "x";
            long number = Data.nextId < 1 ? 1 : Data.nextId;
            Data.nextId = number + 1;
            return prefix + "-" + number.ToString(CultureInfo.InvariantCulture);
        }

        // ---- migration ----

        /// <summary>
        /// Brings a section of schema <paramref name="from"/> to <paramref name="to"/>, one step at a time (<c>Migrate_0_to_1</c>, <c>Migrate_1_to_2</c>...);
        /// works on a copy. A step this code does not know leaves the section as it is and says so in <paramref name="notes"/>.
        /// </summary>
        internal static JsonObject Migrate(JsonObject section, int from, int to, List<string> notes)
        {
            var current = (JsonObject)section.DeepClone();
            for (int version = from; version < to; version++)
            {
                switch (version)
                {
                    case 0:
                        Migrate_0_to_1(current, notes);
                        break;
                    default:
                        notes.Add("there is no migration from schema " + version.ToString(CultureInfo.InvariantCulture) + " to "
                                  + (version + 1).ToString(CultureInfo.InvariantCulture) + ", the section is read as it is");
                        break;
                }
            }
            return current;
        }

        /// <summary>
        /// Schema 0 is a section that carried no usable version (a hand-made or very early file). Its fields are the same as schema 1, so the step changes
        /// nothing; it exists so that such a section is read and checked like any other instead of being dropped.
        /// </summary>
        private static void Migrate_0_to_1(JsonObject section, List<string> notes)
        {
            notes.Add("migrated from schema 0 to 1");
        }

        // ---- the checks ----

        /// <summary>
        /// Brings a section to the rules of design.md "Правила проверки при загрузке" and returns it (the same object). Everything it changes is
        /// explained in <see cref="Warnings"/>. Used by <see cref="Read"/>; public so that a service can check a section it built.
        /// </summary>
        public GpuSaveSection Sanitize(GpuSaveSection s)
        {
            if (s == null) s = new GpuSaveSection();
            GpuBalanceDto balance = Balance;
            GpuBalanceDto.LimitsBlock limits = balance.limits ?? _defaultBalance.limits;

            if (s.marketModeApplied != GpuContentDb.ModeSingle && s.marketModeApplied != GpuContentDb.ModeEras)
            {
                Warn("marketModeApplied '" + s.marketModeApplied + "' is unknown, '" + GpuContentDb.ModeSingle + "' is used");
                s.marketModeApplied = GpuContentDb.ModeSingle;
            }
            s.foundedOn = CleanDate("foundedOn", s.foundedOn);

            s.research = CleanResearch(s.research, balance);
            s.designs = CleanDesigns(s.designs, balance, limits);
            s.projects = CleanProjects(s.projects, s.designs, limits);
            s.products = CleanProducts(s.products, balance, limits);
            s.vendors = CleanVendors(s.vendors, limits);
            s.market = CleanMarket(s.market, balance, limits);
            s.synergy = CleanSynergy(s.synergy, balance);
            s.news = CleanNews(s.news);
            s.consoleOrders = CleanOrders(s.consoleOrders, s.products, balance, limits);

            LiftNextId(s);
            return s;
        }

        private void LiftNextId(GpuSaveSection s)
        {
            if (s.nextId < 1)
            {
                Warn("nextId " + s.nextId.ToString(CultureInfo.InvariantCulture) + " is below 1, 1 is used");
                s.nextId = 1;
            }

            long highest = 0;
            foreach (GpuDesignSave d in s.designs) highest = Math.Max(highest, NumberOf(d.id));
            foreach (GpuProjectSave p in s.projects) highest = Math.Max(highest, NumberOf(p.id));
            foreach (GpuProductSave p in s.products) highest = Math.Max(highest, NumberOf(p.id));
            if (s.nextId <= highest)
            {
                Warn("nextId " + s.nextId.ToString(CultureInfo.InvariantCulture) + " is not above the ids in use, " + (highest + 1).ToString(CultureInfo.InvariantCulture) + " is used");
                s.nextId = highest + 1;
            }
        }

        /// <summary>The number after the last hyphen of an id (<c>g-9</c> gives 9), or 0 when there is none.</summary>
        internal static long NumberOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            int dash = id.LastIndexOf('-');
            if (dash < 0 || dash == id.Length - 1) return 0;
            long number = 0;
            for (int i = dash + 1; i < id.Length; i++)
            {
                char ch = id[i];
                if (ch < '0' || ch > '9' || number > (long.MaxValue - 9) / 10) return 0;
                number = number * 10 + (ch - '0');
            }
            return number;
        }

        // ---- research ----

        private GpuResearchSave CleanResearch(GpuResearchSave r, GpuBalanceDto balance)
        {
            if (r == null) r = new GpuResearchSave();
            GpuBalanceDto.ResearchBlock rules = balance.research ?? _defaultBalance.research;

            var completed = new List<string>();
            foreach (string id in r.completed ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (completed.Contains(id)) continue;
                if (_content != null && _content.Node(id) == null)
                {
                    Warn("research.completed: '" + id + "' is not a research node of the content, skipped");
                    continue;
                }
                completed.Add(id);
            }
            r.completed = completed;

            string current = r.currentId ?? string.Empty;
            if (current.Length > 0)
            {
                if (_content != null && _content.Node(current) == null)
                {
                    Warn("research.currentId: '" + current + "' is not a research node of the content, the research is dropped");
                    current = string.Empty;
                }
                else if (completed.Contains(current))
                {
                    current = string.Empty;
                }
            }
            r.currentId = current;
            r.currentProgress = current.Length == 0 ? 0f : Number("research.currentProgress", r.currentProgress, 0f, 0f, 1f);

            var queue = new List<string>();
            foreach (string id in r.queue ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(id) || id == current || completed.Contains(id) || queue.Contains(id)) continue;
                if (_content != null && _content.Node(id) == null)
                {
                    Warn("research.queue: '" + id + "' is not a research node of the content, skipped");
                    continue;
                }
                queue.Add(id);
            }
            if (queue.Count > rules.queueMax)
            {
                Warn("research.queue has " + queue.Count.ToString(CultureInfo.InvariantCulture) + " nodes, the limit is " + rules.queueMax.ToString(CultureInfo.InvariantCulture) + ", the rest is cut");
                queue.RemoveRange(rules.queueMax, queue.Count - rules.queueMax);
            }
            r.queue = queue;

            r.funding = Number("research.funding", r.funding, 1f, rules.fundingMin, rules.fundingMax);
            return r;
        }

        // ---- designs and projects ----

        private List<GpuDesignSave> CleanDesigns(List<GpuDesignSave> designs, GpuBalanceDto balance, GpuBalanceDto.LimitsBlock limits)
        {
            int nameMax = (balance.design ?? _defaultBalance.design).nameMaxLength;
            var result = new List<GpuDesignSave>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (GpuDesignSave d in designs ?? new List<GpuDesignSave>())
            {
                if (d == null) continue;
                string id = (d.id ?? string.Empty).Trim();
                if (id.Length == 0)
                {
                    Warn("designs: a design without an id is dropped");
                    continue;
                }
                if (!seen.Add(id))
                {
                    Warn("designs: '" + id + "' is listed twice, the second one is dropped");
                    continue;
                }
                if (result.Count >= limits.products)
                {
                    Warn("designs: more than " + limits.products.ToString(CultureInfo.InvariantCulture) + ", the rest is cut");
                    break;
                }

                string path = "designs[" + id + "]";
                d.id = id;
                d.name = CleanName(d.name, nameMax);
                d.archId = d.archId ?? string.Empty;
                d.memId = d.memId ?? string.Empty;
                d.coolerId = d.coolerId ?? string.Empty;
                d.node = d.node ?? string.Empty;
                d.die = NonNegative(path + ".die", d.die);
                d.clockMHz = NonNegative(path + ".clockMHz", d.clockMHz);
                d.vramMB = NonNegative(path + ".vramMB", d.vramMB);
                d.busWidth = NonNegative(path + ".busWidth", d.busWidth);
                d.stats = CleanStats(path + ".stats", d.stats);

                if (_content != null)
                {
                    if (d.archId.Length > 0 && _content.Arch(d.archId) == null)
                        Warn(path + ".archId '" + d.archId + "' is not in the content; the products keep their snapshot, a project of it would be cancelled");
                    if (d.memId.Length > 0 && _content.Mem(d.memId) == null)
                        Warn(path + ".memId '" + d.memId + "' is not in the content; the products keep their snapshot, a project of it would be cancelled");
                    if (d.coolerId.Length > 0 && _content.Cooler(d.coolerId) == null)
                        Warn(path + ".coolerId '" + d.coolerId + "' is not in the content; the products keep their snapshot, a project of it would be cancelled");
                }
                result.Add(d);
            }
            return result;
        }

        private List<GpuProjectSave> CleanProjects(List<GpuProjectSave> projects, List<GpuDesignSave> designs, GpuBalanceDto.LimitsBlock limits)
        {
            var result = new List<GpuProjectSave>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (GpuProjectSave p in projects ?? new List<GpuProjectSave>())
            {
                if (p == null) continue;
                string id = (p.id ?? string.Empty).Trim();
                if (id.Length == 0)
                {
                    Warn("projects: a project without an id is dropped");
                    continue;
                }
                if (!seen.Add(id))
                {
                    Warn("projects: '" + id + "' is listed twice, the second one is dropped");
                    continue;
                }
                if (result.Count >= limits.products)
                {
                    Warn("projects: more than " + limits.products.ToString(CultureInfo.InvariantCulture) + ", the rest is cut");
                    break;
                }

                GpuDesignSave design = designs.Find(d => d.id == p.designId);
                if (design == null)
                {
                    Warn("projects[" + id + "]: the design '" + p.designId + "' is not in the section, the project is cancelled");
                    continue;
                }
                if (_content != null && (_content.Arch(design.archId) == null || _content.Mem(design.memId) == null || _content.Cooler(design.coolerId) == null))
                {
                    Warn("projects[" + id + "]: the design '" + design.id + "' uses a part that is not in the content (" + design.archId + ", " + design.memId + ", "
                         + design.coolerId + "), the project is cancelled");
                    continue;
                }

                string path = "projects[" + id + "]";
                p.id = id;
                p.progress = Number(path + ".progress", p.progress, 0f, 0f, 1f);
                p.totalCost = NonNegative(path + ".totalCost", p.totalCost);
                p.days = NonNegative(path + ".days", p.days);
                p.inflationAtStart = Positive(path + ".inflationAtStart", p.inflationAtStart, 1f);
                result.Add(p);
            }
            return result;
        }

        // ---- products ----

        private List<GpuProductSave> CleanProducts(List<GpuProductSave> products, GpuBalanceDto balance, GpuBalanceDto.LimitsBlock limits)
        {
            int nameMax = (balance.design ?? _defaultBalance.design).nameMaxLength;
            float popFloor = (balance.popularity ?? _defaultBalance.popularity).floor;
            int ledgerMonths = limits.ledgerMonths;

            var result = new List<GpuProductSave>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (GpuProductSave p in products ?? new List<GpuProductSave>())
            {
                if (p == null) continue;
                string id = (p.id ?? string.Empty).Trim();
                if (id.Length == 0)
                {
                    Warn("products: a product without an id is dropped");
                    continue;
                }
                if (!seen.Add(id))
                {
                    Warn("products: '" + id + "' is listed twice, the second one is dropped");
                    continue;
                }
                if (p.owner != OwnerPlayer && p.owner != OwnerVendor && p.owner != OwnerBackground)
                {
                    Warn("products[" + id + "].owner '" + p.owner + "' is unknown, the product is dropped");
                    continue;
                }
                if (result.Count >= limits.products)
                {
                    Warn("products: more than " + limits.products.ToString(CultureInfo.InvariantCulture) + ", the rest is cut");
                    break;
                }

                string path = "products[" + id + "]";
                p.id = id;
                p.designId = p.designId ?? string.Empty;
                p.vendorId = p.vendorId ?? string.Empty;
                p.name = CleanName(p.name, nameMax);
                if (p.status != StatusReady && p.status != StatusSelling && p.status != StatusRetired)
                {
                    Warn(path + ".status '" + p.status + "' is unknown, '" + StatusRetired + "' is used");
                    p.status = StatusRetired;
                }
                p.companyKey = CleanCompanyKey(path + ".companyKey", p.companyKey);
                if (p.owner == OwnerPlayer && p.companyKey.Length == 0) p.companyKey = CompanyKey.Player;

                p.price = NonNegative(path + ".price", p.price);
                p.stock = NonNegative(path + ".stock", p.stock);
                p.lines = NonNegative(path + ".lines", p.lines);
                p.unitsPerDay = NonNegative(path + ".unitsPerDay", p.unitsPerDay);
                p.released = CleanDate(path + ".released", p.released);
                p.stats = CleanStats(path + ".stats", p.stats);
                p.node = p.node ?? string.Empty;
                p.die = NonNegative(path + ".die", p.die);
                p.busWidth = NonNegative(path + ".busWidth", p.busWidth);
                p.vramMB = NonNegative(path + ".vramMB", p.vramMB);
                CleanPopularity(path, p, popFloor);
                p.ledger = CleanLedger(path + ".ledger", p.ledger, ledgerMonths);
                result.Add(p);
            }
            return result;
        }

        /// <summary>One popularity per segment: the lists are aligned (missing values start at the floor), empty and repeated segments are dropped, values are in [0, 1].</summary>
        private void CleanPopularity(string path, GpuProductSave p, float popFloor)
        {
            List<string> segIds = p.segIds ?? new List<string>();
            List<float> pops = p.pop ?? new List<float>();
            var ids = new List<string>();
            var values = new List<float>();
            for (int i = 0; i < segIds.Count; i++)
            {
                string seg = segIds[i];
                if (string.IsNullOrWhiteSpace(seg) || ids.Contains(seg))
                {
                    Warn(path + ".segIds[" + i.ToString(CultureInfo.InvariantCulture) + "]: an empty or repeated segment is dropped");
                    continue;
                }
                float value = i < pops.Count ? pops[i] : popFloor;
                ids.Add(seg);
                values.Add(Number(path + ".pop[" + i.ToString(CultureInfo.InvariantCulture) + "]", value, popFloor, 0f, 1f));
            }
            if (pops.Count != segIds.Count)
                Warn(path + ": pop has " + pops.Count.ToString(CultureInfo.InvariantCulture) + " values for " + segIds.Count.ToString(CultureInfo.InvariantCulture) + " segments, they are aligned");
            p.segIds = ids;
            p.pop = values;
        }

        private GpuLedgerSave CleanLedger(string path, GpuLedgerSave ledger, int months)
        {
            if (ledger == null) ledger = new GpuLedgerSave();
            ledger.startMonthIndex = NonNegative(path + ".startMonthIndex", ledger.startMonthIndex);
            ledger.sold = CleanInts(path + ".sold", ledger.sold, months);
            ledger.produced = CleanInts(path + ".produced", ledger.produced, months);
            ledger.failed = CleanInts(path + ".failed", ledger.failed, months);
            ledger.income = CleanFloats(path + ".income", ledger.income, months, 0f, float.MaxValue);
            ledger.cost = CleanFloats(path + ".cost", ledger.cost, months, 0f, float.MaxValue);
            return ledger;
        }

        private GpuStatsSave CleanStats(string path, GpuStatsSave st)
        {
            if (st == null) return new GpuStatsSave();
            st.transistors = NonNegative(path + ".transistors", st.transistors);
            st.computeMops = NonNegative(path + ".computeMops", st.computeMops);
            st.effectivePerf = NonNegative(path + ".effectivePerf", st.effectivePerf);
            st.units = NonNegative(path + ".units", st.units);
            st.maxClockMHz = NonNegative(path + ".maxClockMHz", st.maxClockMHz);
            st.bandwidthMBs = NonNegative(path + ".bandwidthMBs", st.bandwidthMBs);
            st.bandwidthRatio = NonNegative(path + ".bandwidthRatio", st.bandwidthRatio);
            st.powerW = NonNegative(path + ".powerW", st.powerW);
            st.tempC = NonNegative(path + ".tempC", st.tempC);
            st.failureRate = NonNegative(path + ".failureRate", st.failureRate);
            st.dieMaterial = NonNegative(path + ".dieMaterial", st.dieMaterial);
            st.perGoodExtras = NonNegative(path + ".perGoodExtras", st.perGoodExtras);
            st.projectCost = NonNegative(path + ".projectCost", st.projectCost);
            st.unitCost = NonNegative(path + ".unitCost", st.unitCost);
            st.projectDays = NonNegative(path + ".projectDays", st.projectDays);
            st.features = CleanNames(st.features);
            return st;
        }

        // ---- vendors, market, synergy, news ----

        private List<GpuVendorSave> CleanVendors(List<GpuVendorSave> vendors, GpuBalanceDto.LimitsBlock limits)
        {
            var result = new List<GpuVendorSave>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (GpuVendorSave v in vendors ?? new List<GpuVendorSave>())
            {
                if (v == null) continue;
                string id = (v.id ?? string.Empty).Trim();
                if (id.Length == 0)
                {
                    Warn("vendors: a vendor without an id is dropped");
                    continue;
                }
                if (!seen.Add(id))
                {
                    Warn("vendors: '" + id + "' is listed twice, the second one is dropped");
                    continue;
                }
                if (result.Count >= limits.vendors)
                {
                    Warn("vendors: more than " + limits.vendors.ToString(CultureInfo.InvariantCulture) + ", the rest is cut");
                    break;
                }

                string path = "vendors[" + id + "]";
                v.id = id;
                if (v.state != VendorPending && v.state != VendorActive && v.state != VendorExited && v.state != VendorMerged)
                {
                    Warn(path + ".state '" + v.state + "' is unknown, '" + VendorPending + "' is used");
                    v.state = VendorPending;
                }
                v.mergedInto = v.mergedInto ?? string.Empty;
                v.companyKey = CleanCompanyKey(path + ".companyKey", v.companyKey);
                v.launchCount = NonNegative(path + ".launchCount", v.launchCount);
                v.lastLaunch = CleanLaunches(path + ".lastLaunch", v.lastLaunch);
                result.Add(v);
            }
            return result;
        }

        private GpuMarketSave CleanMarket(GpuMarketSave m, GpuBalanceDto balance, GpuBalanceDto.LimitsBlock limits)
        {
            if (m == null) m = new GpuMarketSave();
            var ids = new List<string>();
            var sales = new List<float>();
            List<string> openIds = m.openSegIds ?? new List<string>();
            List<float> potential = m.potentialSales ?? new List<float>();
            for (int i = 0; i < openIds.Count; i++)
            {
                string seg = openIds[i];
                if (string.IsNullOrWhiteSpace(seg) || ids.Contains(seg))
                {
                    Warn("market.openSegIds[" + i.ToString(CultureInfo.InvariantCulture) + "]: an empty or repeated segment is dropped");
                    continue;
                }
                ids.Add(seg);
                sales.Add(NonNegative("market.potentialSales[" + i.ToString(CultureInfo.InvariantCulture) + "]", i < potential.Count ? potential[i] : 0f));
            }
            if (potential.Count != openIds.Count)
                Warn("market: potentialSales has " + potential.Count.ToString(CultureInfo.InvariantCulture) + " values for " + openIds.Count.ToString(CultureInfo.InvariantCulture) + " segments, they are aligned");
            m.openSegIds = ids;
            m.potentialSales = sales;
            m.lastBackgroundRefresh = CleanDate("market.lastBackgroundRefresh", m.lastBackgroundRefresh);
            m.erasCheckedOn = CleanDate("market.erasCheckedOn", m.erasCheckedOn);

            int window = Math.Max(30, (balance.synergy ?? _defaultBalance.synergy).shareWindowDays);
            m.playerShareDaily = CleanFloats("market.playerShareDaily", m.playerShareDaily, window, 0f, 1f);
            m.playerSoldMonthly = CleanInts("market.playerSoldMonthly", m.playerSoldMonthly, limits.ledgerMonths);
            return m;
        }

        private GpuSynergySave CleanSynergy(GpuSynergySave s, GpuBalanceDto balance)
        {
            if (s == null) s = new GpuSynergySave();
            int window = Math.Max(30, (balance.synergy ?? _defaultBalance.synergy).shareWindowDays);
            var result = new List<GpuSynergyEntry>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (GpuSynergyEntry e in s.entries ?? new List<GpuSynergyEntry>())
            {
                if (e == null) continue;
                string key = e.companyKey ?? string.Empty;
                if (key.Length == 0 || !CompanyKey.IsValid(key))
                {
                    Warn("synergy.entries: the company key '" + key + "' is not player or ai:<number>, the entry is dropped");
                    continue;
                }
                if (!seen.Add(key))
                {
                    Warn("synergy.entries: '" + key + "' is listed twice, the second one is dropped");
                    continue;
                }
                if (result.Count >= MaxSynergyEntries)
                {
                    Warn("synergy.entries: more than " + MaxSynergyEntries.ToString(CultureInfo.InvariantCulture) + ", the rest is cut");
                    break;
                }

                string path = "synergy[" + key + "]";
                e.sCpu = Number(path + ".sCpu", e.sCpu, 0f, 0f, 1f);
                e.sGpu = Number(path + ".sGpu", e.sGpu, 0f, 0f, 1f);
                e.cpuShareDaily = CleanFloats(path + ".cpuShareDaily", e.cpuShareDaily, window, 0f, 1f);
                e.gpuShareDaily = CleanFloats(path + ".gpuShareDaily", e.gpuShareDaily, window, 0f, 1f);
                result.Add(e);
            }
            s.entries = result;
            return s;
        }

        private GpuNewsSave CleanNews(GpuNewsSave n)
        {
            if (n == null) n = new GpuNewsSave();
            n.lastLeaderMail = CleanLaunches("news.lastLeaderMail", n.lastLeaderMail);
            return n;
        }

        private List<GpuSegLaunch> CleanLaunches(string path, List<GpuSegLaunch> launches)
        {
            var result = new List<GpuSegLaunch>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (GpuSegLaunch launch in launches ?? new List<GpuSegLaunch>())
            {
                if (launch == null || string.IsNullOrWhiteSpace(launch.seg))
                {
                    Warn(path + ": an entry without a segment is dropped");
                    continue;
                }
                if (!seen.Add(launch.seg))
                {
                    Warn(path + ": the segment '" + launch.seg + "' is listed twice, the second one is dropped");
                    continue;
                }
                launch.extraMonths = Math.Min(MaxExtraMonths, NonNegative(path + "." + launch.seg + ".extraMonths", launch.extraMonths));
                launch.date = CleanDate(path + "." + launch.seg + ".date", launch.date);
                if (launch.date == null)
                {
                    Warn(path + ": the entry of '" + launch.seg + "' has no valid date, it is dropped");
                    continue;
                }
                if (result.Count >= MaxLaunchEntries)
                {
                    Warn(path + ": more than " + MaxLaunchEntries.ToString(CultureInfo.InvariantCulture) + " entries, the rest is cut");
                    break;
                }
                result.Add(launch);
            }
            return result;
        }

        // ---- console orders (Could) ----

        private List<GpuConsoleOrderSave> CleanOrders(List<GpuConsoleOrderSave> orders, List<GpuProductSave> products, GpuBalanceDto balance, GpuBalanceDto.LimitsBlock limits)
        {
            int grace = (balance.console ?? _defaultBalance.console).graceDays;
            var result = new List<GpuConsoleOrderSave>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (GpuConsoleOrderSave o in orders ?? new List<GpuConsoleOrderSave>())
            {
                if (o == null) continue;
                string id = (o.id ?? string.Empty).Trim();
                if (id.Length == 0)
                {
                    Warn("consoleOrders: an order without an id is dropped");
                    continue;
                }
                if (!seen.Add(id))
                {
                    Warn("consoleOrders: '" + id + "' is listed twice, the second one is dropped");
                    continue;
                }
                if (result.Count >= limits.consoleOrders)
                {
                    Warn("consoleOrders: more than " + limits.consoleOrders.ToString(CultureInfo.InvariantCulture) + ", the rest is cut");
                    break;
                }

                string path = "consoleOrders[" + id + "]";
                o.id = id;
                if (o.status != OrderOffered && o.status != OrderActive && o.status != OrderEnded && o.status != OrderCancelled && o.status != OrderDeclined)
                {
                    Warn(path + ".status '" + o.status + "' is unknown, '" + OrderCancelled + "' is used");
                    o.status = OrderCancelled;
                }
                o.productId = o.productId ?? string.Empty;
                o.mailId = NonNegative(path + ".mailId", o.mailId);
                o.offeredOn = CleanDate(path + ".offeredOn", o.offeredOn);
                o.startedOn = CleanDate(path + ".startedOn", o.startedOn);
                o.endsOn = CleanDate(path + ".endsOn", o.endsOn);
                o.unitsPerDay = NonNegative(path + ".unitsPerDay", o.unitsPerDay);
                o.price = NonNegative(path + ".price", o.price);
                o.deliveredWindow = CleanInts(path + ".deliveredWindow", o.deliveredWindow, grace);
                o.missedWindow = CleanInts(path + ".missedWindow", o.missedWindow, grace);

                bool live = o.status == OrderOffered || o.status == OrderActive;
                if (live && _content != null && _content.ConsoleEntry(id) == null)
                {
                    Warn(path + ": '" + id + "' is not an offer of the content, the order is cancelled without a penalty");
                    o.status = OrderCancelled;
                }
                else if (live && o.productId.Length > 0 && !products.Exists(p => p.id == o.productId && p.owner == OwnerPlayer))
                {
                    Warn(path + ": the product '" + o.productId + "' is not one of the player's, the order is cancelled without a penalty");
                    o.status = OrderCancelled;
                }
                else if (o.status == OrderActive && o.productId.Length == 0)
                {
                    Warn(path + ": an active order without a product is cancelled without a penalty");
                    o.status = OrderCancelled;
                }
                result.Add(o);
            }
            return result;
        }

        // ---- small checks ----

        /// <summary>A company key as the section stores it: <c>player</c>, <c>ai:&lt;digits&gt;</c> or empty (a virtual vendor). Anything else (a numeric SaveID, text) becomes empty.</summary>
        private string CleanCompanyKey(string path, string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            if (CompanyKey.IsValid(key)) return key;
            Warn(path + " '" + key + "' is not player or ai:<number>, it is cleared");
            return string.Empty;
        }

        private GpuDate CleanDate(string path, GpuDate date)
        {
            if (date == null) return null;
            bool valid = date.y >= 1900 && date.y <= 2200 && date.m >= 1 && date.m <= 12 && date.d >= 1 && date.d <= DateTime.DaysInMonth(date.y, date.m);
            if (valid) return date;
            Warn(path + " " + date.y.ToString(CultureInfo.InvariantCulture) + "-" + date.m.ToString(CultureInfo.InvariantCulture) + "-" + date.d.ToString(CultureInfo.InvariantCulture)
                 + " is not a date, it is cleared");
            return null;
        }

        private static List<string> CleanNames(List<string> names)
        {
            var result = new List<string>();
            if (names == null) return result;
            foreach (string name in names)
            {
                if (!string.IsNullOrWhiteSpace(name) && !result.Contains(name)) result.Add(name);
            }
            return result;
        }

        /// <summary>A finite number clamped to [min, max]; not a number gives <paramref name="fallback"/>.</summary>
        private float Number(string path, float value, float fallback, float min, float max)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                Warn(path + " is not a finite number, " + fallback.ToString(CultureInfo.InvariantCulture) + " is used");
                return fallback;
            }
            if (value < min)
            {
                Warn(path + " " + value.ToString(CultureInfo.InvariantCulture) + " is below " + min.ToString(CultureInfo.InvariantCulture) + ", " + min.ToString(CultureInfo.InvariantCulture) + " is used");
                return min;
            }
            if (value > max)
            {
                Warn(path + " " + value.ToString(CultureInfo.InvariantCulture) + " is above " + max.ToString(CultureInfo.InvariantCulture) + ", " + max.ToString(CultureInfo.InvariantCulture) + " is used");
                return max;
            }
            return value;
        }

        /// <summary>A finite number above 0; anything else (0, negative, not a number) gives <paramref name="fallback"/>.</summary>
        private float Positive(string path, float value, float fallback)
        {
            if (!float.IsNaN(value) && !float.IsInfinity(value) && value > 0f) return value;
            Warn(path + " " + value.ToString(CultureInfo.InvariantCulture) + " is not above 0, " + fallback.ToString(CultureInfo.InvariantCulture) + " is used");
            return fallback;
        }

        private float NonNegative(string path, float value)
        {
            return Number(path, value, 0f, 0f, float.MaxValue);
        }

        private double NonNegative(string path, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                Warn(path + " is not a finite number, 0 is used");
                return 0.0;
            }
            if (value < 0.0)
            {
                Warn(path + " " + value.ToString(CultureInfo.InvariantCulture) + " is negative, 0 is used");
                return 0.0;
            }
            return value;
        }

        private int NonNegative(string path, int value)
        {
            if (value >= 0) return value;
            Warn(path + " " + value.ToString(CultureInfo.InvariantCulture) + " is negative, 0 is used");
            return 0;
        }

        /// <summary>A ring of whole numbers: negatives become 0, only the newest <paramref name="max"/> (the tail) stay.</summary>
        private List<int> CleanInts(string path, List<int> values, int max)
        {
            var result = new List<int>();
            if (values == null) return result;
            foreach (int value in values) result.Add(value < 0 ? 0 : value);
            if (HasNegative(values)) Warn(path + " has negative numbers, they are replaced with 0");
            return Tail(path, result, max);
        }

        private List<float> CleanFloats(string path, List<float> values, int max, float min, float top)
        {
            var result = new List<float>();
            if (values == null) return result;
            for (int i = 0; i < values.Count; i++)
            {
                result.Add(Number(path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", values[i], min, min, top));
            }
            return Tail(path, result, max);
        }

        private List<T> Tail<T>(string path, List<T> values, int max)
        {
            if (values.Count <= max) return values;
            Warn(path + " has " + values.Count.ToString(CultureInfo.InvariantCulture) + " entries, the limit is " + max.ToString(CultureInfo.InvariantCulture) + ", the oldest are cut");
            values.RemoveRange(0, values.Count - max);
            return values;
        }

        private static bool HasNegative(List<int> values)
        {
            foreach (int value in values)
            {
                if (value < 0) return true;
            }
            return false;
        }

        /// <summary>
        /// A name the player typed or the content made, safe for the game's text: control characters and rich-text tags (<c>&lt;b&gt;</c>, <c>&lt;color=#f00&gt;</c>)
        /// are removed, the ends are trimmed and the length is cut to <paramref name="maxLength"/> (0 or less: no limit). Null gives an empty string.
        /// </summary>
        public static string CleanName(string name, int maxLength)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;

            var text = new StringBuilder(name.Length);
            int i = 0;
            while (i < name.Length)
            {
                char ch = name[i];
                if (ch == '<')
                {
                    int close = name.IndexOf('>', i);
                    if (close < 0) break;   // an unclosed tag takes the rest of the text with it
                    i = close + 1;
                    continue;
                }
                if (ch < ' ' || ch == '\u007f')
                {
                    i++;
                    continue;
                }
                text.Append(ch);
                i++;
            }

            string result = text.ToString().Trim();
            if (maxLength > 0 && result.Length > maxLength)
            {
                int length = maxLength;
                if (char.IsHighSurrogate(result[length - 1])) length--;   // do not cut a character in two
                result = result.Substring(0, length).TrimEnd();
            }
            return result;
        }

        // ---- messages ----

        private void Warn(string message)
        {
            _warnings.Add(message);
            Report(message);
        }

        private void Report(string message)
        {
            if (_warn == null) return;
            try
            {
                _warn(message);
            }
            catch (Exception)
            {
                // The log must never break loading a save.
            }
        }
    }
}
