using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using PTMod.Core.Logic.Json;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>The knobs of a run of the simulation (TSK-028): the years, the player, the factory.</summary>
    internal sealed class SimPlan
    {
        public string Name = "reference";

        /// <summary>The first and the last day of the run: 1 January of the first year to 31 December of the last.</summary>
        public int StartYear = 1981, EndYear = 2010;

        /// <summary>The bot founds the division on 1 January of this year (0: never) and starts a card every <see cref="ReleaseEveryYears"/> years from then.</summary>
        public int FoundYear = 1995, ReleaseEveryYears = 2;

        /// <summary>The own factory of the player: its lines, and the lines the CPUs take every day after the reserve of the cards.</summary>
        public int CapacityLines = 60, CpuLines = 20;

        /// <summary>
        /// The bot takes the previous card off the market when the new one is released (a generation replaces the old one; the stock is liquidated). Off: it keeps every card
        /// until it is outdated (the rule of the game, 5 years) and then retires it.
        /// </summary>
        public bool ReplaceOnRelease = true;

        /// <summary>The switch "CPU first" (D18): the cards take only the lines the CPUs leave.</summary>
        public bool CpuFirst;

        /// <summary>The seed of the stream of the background.</summary>
        public uint Seed = 42;

        /// <summary>The company sells its factory and becomes fabless on 1 January of this year (0: it keeps the factory); the cards then go to the foundry (F18).</summary>
        public int FablessFromYear;

        /// <summary>The content to run on; the shipped content when null.</summary>
        public GpuContentDb Content;

        /// <summary>Called when the market of the start year is seeded, before the first day: a test puts its own cards on the market here.</summary>
        public Action<GpuSimulation> AfterSeed;

        /// <summary>The vendors of the content take part (the switch "Соперники"); AMD (<c>ai:1</c>) and Intel (<c>ai:0</c>) are companies of the world unless left out below.</summary>
        public bool Rivals;

        /// <summary>The AI company of a bound vendor is not in the game (AC-12.4).</summary>
        public bool WithoutAmd, WithoutIntel;

        /// <summary>Called on the 1st of every month, after the monthly handlers and the bot (the date is <see cref="FakeGpuGame.Date"/>): a test looks at the state in a month it names.</summary>
        public Action<GpuSimulation> OnMonth;

        /// <summary>Called before the first day, after the world is made and before the market is seeded: a test sets the world up (the money of a company, a bankruptcy).</summary>
        public Action<GpuSimulation> BeforeSeed;

        /// <summary>
        /// The segments the bot keeps a card in (the balance reference of NFR-7: a company with a lineup, not a single card). Null is the bot of stage 1: one project at a time, for the
        /// mainstream segment. With ids, the bot starts a project for each of the segments that is open and has no card of its own on the way, every <see cref="ReleaseEveryYears"/> years, prices
        /// the card by the budget of its segment (<see cref="Lineup"/>'s 0.9) and takes the card of the previous generation of that segment off the market.
        /// </summary>
        public string[] Lineup;
    }

    /// <summary>One month of the run, as of its 1st: the shares of the last thirty days and the cards on sale.</summary>
    internal struct SimMonth
    {
        public int Year, Month;
        public float BackgroundShare, PlayerShare;
        public int PlayerCardsOnSale;
        public long PlayerStock;
    }

    /// <summary>A card the bot of the run released: what it cost to develop, when it came out, the price against the budget of its segment (the balance reference, NFR-7).</summary>
    internal sealed class SimCard
    {
        public string Id, Name, SegmentId;
        public DateTime Released;

        /// <summary>The money the project took from the start to the day it was ready (the daily costs with the inflation of each day, F14).</summary>
        public double DevCost;

        public float Price, Budget;
    }

    /// <summary>The share of an owner in the cards sold in a segment over thirty days, as of the 1st of a month (NFR-7).</summary>
    internal struct SimSegmentShare
    {
        public int Year, Month;
        public string Segment, Owner;
        public float Share;
        public int Sold;
    }

    /// <summary>
    /// The integration simulation of stage 1 (TSK-028; FR-5, FR-6, NFR-1, NFR-2): the services of the feature on the shipped content, the fake game and the fake factory
    /// - no Unity, no game. A bot plays the player: it founds the division in <see cref="SimPlan.FoundYear"/>, keeps the laboratory busy, starts a card every two years from
    /// the parts it has researched (the best valid design of a few tries), releases it at the proposed price with the automatic lines, takes the outdated cards off the
    /// market. Every game day goes through the handlers in the order of the core (the reserve, the CPUs, the laboratory and the projects, the market, the end of the day,
    /// and on the 1st of a month the month) and each handler is timed by the same <see cref="GpuPerfMeter"/> the game uses. The run checks the invariants of NFR-2 on its way.
    /// </summary>
    internal sealed class GpuSimulation
    {
        public readonly SimPlan Plan;
        public readonly GpuWorld World;
        public readonly FakeGpuGame Game;

        /// <summary>The timer of the handlers (the same class the game uses); its lines of the months are in <see cref="PerfLines"/>.</summary>
        public readonly GpuPerfMeter Perf;

        public readonly List<string> PerfLines = new List<string>();
        public readonly List<SimMonth> Months = new List<SimMonth>();
        public readonly List<string> Events = new List<string>();
        public readonly List<string> Violations = new List<string>();
        public readonly List<string> Releases = new List<string>();

        /// <summary>The events of the vendors in the order they happened: <c>1996-01-01 entered 3dfx</c>, <c>2001-01-01 merged 3dfx into nvidia</c>, ...</summary>
        public readonly List<string> VendorEvents = new List<string>();

        /// <summary>Every card a vendor launched (not the seed): <c>2003-03-01 nvidia gaming.high GeForce 12 perf 1200 price 499</c>; the fingerprint of the determinism.</summary>
        public readonly List<string> VendorLaunches = new List<string>();

        /// <summary>The sales of the month by segment and owner over thirty days, as of the 1st: the figures of NFR-7.</summary>
        public readonly List<SimSegmentShare> SegmentShares = new List<SimSegmentShare>();

        /// <summary>The performance of the card of the others at the first and at the last 1st of a month of the run: the others get better with the years.</summary>
        public double FirstBackgroundPerf, LastBackgroundPerf;

        /// <summary>The most lines the cards of the player reserved on one day.</summary>
        public int MaxReserved { get; private set; }

        public int Days { get; private set; }
        public int CardsReleased { get; private set; }
        public int CardsRetired { get; private set; }

        /// <summary>The lowest share of the background ("other makers") over thirty days, with the date, from the 31st day of the run up to 31 December 1999.</summary>
        public float MinBackgroundShareBefore2000 = 1f;

        public DateTime MinBackgroundShareDate;

        /// <summary>The highest share of the player over thirty days and the date.</summary>
        public float MaxPlayerShare;

        public DateTime MaxPlayerShareDate;

        private readonly List<string> _ready = new List<string>();
        private DateTime _lastProject = DateTime.MinValue;
        private int _projectsStarted;

        /// <summary>The player's income from the cards by calendar year, money of the day (the articles <c>Sales</c> and <c>Contracts</c> of the fake game); the balance reference compares it with the CPU business.</summary>
        public readonly Dictionary<int, double> SalesByYear = new Dictionary<int, double>();

        /// <summary>The player's spending on the cards by calendar year and article (<see cref="GpuExpense"/>): the development, the research, the factory, the royalties.</summary>
        public readonly Dictionary<int, double[]> SpentByYear = new Dictionary<int, double[]>();

        private int _earnedAt, _spentAt;

        /// <summary>The cards the bot released, in the order of the releases.</summary>
        public readonly List<SimCard> CardLog = new List<SimCard>();

        private readonly Dictionary<string, double> _devCost = new Dictionary<string, double>(StringComparer.Ordinal);   // design id -> the money its project took so far
        private readonly Dictionary<string, string> _segmentOfDesign = new Dictionary<string, string>(StringComparer.Ordinal);   // design id -> the segment the bot designed it for
        private readonly Dictionary<string, string> _segmentOfCard = new Dictionary<string, string>(StringComparer.Ordinal);     // card id -> the same
        private readonly Dictionary<string, DateTime> _lastStart = new Dictionary<string, DateTime>(StringComparer.Ordinal);     // segment id -> when the bot last started a project for it

        // the figures of the game the bot follows: the frequency of the industry in MHz by year, the wafer by year, the technologies of the game by year
        private static readonly int[] FreqYears = { 1981, 1985, 1990, 1993, 1995, 1997, 1999, 2001, 2003, 2005, 2010 };
        private static readonly float[] FreqMHz = { 4.77f, 12f, 33f, 66f, 120f, 233f, 600f, 1500f, 3000f, 3400f, 3600f };
        private static readonly KeyValuePair<string, int>[] GameTechs =
        {
            new KeyValuePair<string, int>("SDR", 1993), new KeyValuePair<string, int>("250nm", 1997), new KeyValuePair<string, int>("DDR", 1998),
            new KeyValuePair<string, int>("130nm", 2001), new KeyValuePair<string, int>("DDR2", 2003), new KeyValuePair<string, int>("90nm", 2004),
            new KeyValuePair<string, int>("DDR3", 2007), new KeyValuePair<string, int>("45nm", 2008),
        };

        public GpuSimulation(SimPlan plan)
        {
            Plan = plan;
            World = new GpuWorld(new DateTime(plan.StartYear, 1, 1), plan.Seed, plan.Content);
            Game = World.Game;
            Perf = new GpuPerfMeter(() => true, Stopwatch.GetTimestamp, Stopwatch.Frequency, (line, over) => PerfLines.Add(line));
            Game.AddTimelineNodes();
            Game.Factory.CapacityLines = plan.CapacityLines;
            Game.Factory.CpuLines = plan.CpuLines;   // the lines the CPUs use: what "CPU first" leaves the cards is the capacity less these
            World.CpuFirst = plan.CpuFirst;
            World.RivalsOn = plan.Rivals;
            if (plan.Rivals)
            {
                if (!plan.WithoutAmd) Game.AddCompany("ai:1", "AMD");
                if (!plan.WithoutIntel) Game.AddCompany("ai:0", "Intel");
                World.Rivals.Entered += v => VendorEvents.Add(Stamp() + " entered " + v.Id);
                World.Rivals.Exited += v => VendorEvents.Add(Stamp() + " exited " + v.Id);
                World.Rivals.Merged += (v, into) => VendorEvents.Add(Stamp() + " merged " + v.Id + " into " + into.Id);
                World.Rivals.AI.Launched += (v, p) => VendorLaunches.Add(Stamp() + " " + v.Id + " " + p.segIds[0] + " " + p.name + " perf "
                    + p.stats.effectivePerf.ToString("0", CultureInfo.InvariantCulture) + " price " + p.price.ToString("0", CultureInfo.InvariantCulture));
            }
        }

        private string Stamp()
        {
            return Game.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        public GpuState State
        {
            get { return World.State; }
        }

        // ---- the run ----

        /// <summary>A new game from 1 January of the first year to 31 December of the last.</summary>
        public void Run()
        {
            Game.Date = new DateTime(Plan.StartYear, 1, 1);
            UpdateWorld();
            World.Projects.ReadyToRelease += id =>
            {
                _ready.Add(id);
                GpuProductSave product = State.Data.products.Find(p => p.id == id);
                if (product != null && _segmentOfDesign.ContainsKey(product.designId)) _segmentOfCard[id] = _segmentOfDesign[product.designId];
            };
            World.Research.Completed += node => Events.Add(Game.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " research " + node.id);

            if (Plan.BeforeSeed != null) Plan.BeforeSeed(this);

            // the order of a new game: the fast-forward is over (the market of the start year is seeded), then the game is ready (the windows start anew)
            World.Market.SeedForYear(Plan.StartYear);
            World.Market.StartSession();
            World.Rivals.StartSession();
            if (Plan.AfterSeed != null) Plan.AfterSeed(this);

            var last = new DateTime(Plan.EndYear, 12, 31);
            while (Game.Date <= last) Day();
            Perf.Flush();
        }

        /// <summary>One game day in the order of the core, then the date moves; on the 1st of a month the monthly handlers run.</summary>
        public void Day()
        {
            DateTime today = Game.Date;
            Days++;
            FakeFactory factory = Game.Factory;

            Timed(GpuPerfMeter.Day, "production", World.Production.ProduceDay);            // PreContractSellTick
            Require(factory.AllocatedLines <= factory.CapacityLines, "the reserve of the cards is above the capacity: " + factory.AllocatedLines + " > " + factory.CapacityLines);
            MaxReserved = Math.Max(MaxReserved, World.Production.ReservedToday);
            factory.ProduceCpus(Plan.CpuLines);                                // the CPUs take what is left
            Require(factory.AllocatedLines <= factory.CapacityLines, "the lines in use are above the capacity after the CPUs");
            Timed(GpuPerfMeter.Day, "research", World.Research.DailyTick);               // Tick
            foreach (GpuProjectSave project in World.Projects.Active)
            {
                if (project.paused) continue;
                double sum;
                _devCost.TryGetValue(project.designId, out sum);
                _devCost[project.designId] = sum + World.Projects.DailyCost(project);   // what the day of the projects below will take (F14)
            }
            Timed(GpuPerfMeter.Day, "projects", World.Projects.DailyTick);
            Timed(GpuPerfMeter.Day, "rivals.stock", World.Rivals.RefillStock);              // MarketSellTick, before the market (priority)
            Timed(GpuPerfMeter.Day, "market.sell", World.Market.SellDay);                   // MarketSellTick
            Timed(GpuPerfMeter.Day, "market.day", World.Market.DayTick);                   // DayPassed
            Timed(GpuPerfMeter.Day, "lineup.day", World.Lineup.DayTick);
            Timed(GpuPerfMeter.Day, "synergy.day", World.Synergy.DayTick);                 // after the market (priority -10)
            Timed(GpuPerfMeter.Day, "rivals.day", World.Rivals.CheckBoundCompanies);
            factory.ResetDay();

            SampleShares(today);
            TrackMoney(today.Year);
            ReleaseReady();

            Game.Date = today.AddDays(1);
            if (Game.Date.Day == 1 && Game.Date.Year <= Plan.EndYear) Month();   // the run ends on the last day: the 1st of the next January is not played
        }

        private void Month()
        {
            UpdateWorld();
            Timed(GpuPerfMeter.Month, "eras.month", World.Eras.MonthTick);                   // MonthPassed, in the order of the priorities of the feature
            Timed(GpuPerfMeter.Month, "rivals.month", World.Rivals.MonthTick);
            Timed(GpuPerfMeter.Month, "market.month", World.Market.MonthTick);
            Timed(GpuPerfMeter.Month, "lineup.month", World.Lineup.MonthTick);
            Timed(GpuPerfMeter.Month, "synergy.month", World.Synergy.MonthTick);           // the last (priority -30)
            RecordMonth();
            CheckState();
            BotMonth();
            if (Plan.OnMonth != null) Plan.OnMonth(this);
        }

        /// <summary>The time each handler took over the whole run, in milliseconds by name: where a day goes (for the log of a run that is over its budget).</summary>
        public readonly Dictionary<string, double> HandlerMs = new Dictionary<string, double>(StringComparer.Ordinal);

        private void Timed(string kind, string site, Action body)
        {
            long started = Perf.Begin();
            body();
            long finished = Stopwatch.GetTimestamp();
            Perf.End(kind, started, Game.Date);
            double ms = (finished - started) * 1000.0 / Stopwatch.Frequency;
            double sum;
            HandlerMs.TryGetValue(site, out sum);
            HandlerMs[site] = sum + ms;
        }

        private void Require(bool condition, string message)
        {
            if (condition || Violations.Count >= 25) return;
            Violations.Add(Game.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ": " + message);
        }

        // ---- the game around the player ----

        /// <summary>The figures the game itself moves: the frequency of the industry, the wafer, the technologies, the inflation, the money of the CPU business.</summary>
        private void UpdateWorld()
        {
            DateTime d = Game.Date;
            float year = d.Year + (d.Month - 1) / 12f;
            float khz = Interpolate(FreqYears, FreqMHz, year) * 1000f;
            Game.MaxFreqKHz = khz;
            Game.FrontierKHz = khz;
            Game.Wafer = d.Year < 1985 ? 100f : d.Year < 1990 ? 125f : d.Year < 1996 ? 150f : d.Year < 2002 ? 200f : 300f;
            Game.InflationValue = (float)Math.Pow(1.03, year - 1981f);
            foreach (KeyValuePair<string, int> tech in GameTechs)
            {
                if (tech.Value <= d.Year) Game.Techs.Add(tech.Key);
            }
            if (Plan.FablessFromYear > 0 && d.Year >= Plan.FablessFromYear) Game.Fabless = true;   // CompanyHelper.SellManufacturingDivision
            // the CPU business pays for everything: the money never runs out, so a run shows the services and not the finances of the player
            Game.Cash = Math.Max(Game.Cash, 2e10f);
            string newest = World.Content.NodeNameFor(year);
            foreach (KeyValuePair<string, FakeCompany> pair in Game.Companies)
            {
                // the AI companies of the world keep up with the industry; their money is not the point of a run (the unit tests of the finances set it themselves)
                pair.Value.MaxFreqKHz = khz;
                if (newest != null && Game.Nodes.ContainsKey(newest)) pair.Value.NodeName = newest;
                pair.Value.Cash = Math.Max(pair.Value.Cash, 5e10f);
            }
        }

        private static float Interpolate(int[] years, float[] values, float year)
        {
            if (year <= years[0]) return values[0];
            for (int i = 1; i < years.Length; i++)
            {
                if (year > years[i]) continue;
                float t = (year - years[i - 1]) / (years[i] - years[i - 1]);
                return values[i - 1] + (values[i] - values[i - 1]) * t;
            }
            return values[values.Length - 1];
        }

        // ---- what the run records ----

        private void SampleShares(DateTime day)
        {
            if (Days <= GpuMarketService.ShareWindowDays) return;
            float background = World.Market.ShareOf(MarketItem.BackgroundOwner);
            float player = World.Market.ShareOf(MarketItem.PlayerOwner);
            if (day.Year < 2000 && background < MinBackgroundShareBefore2000)
            {
                MinBackgroundShareBefore2000 = background;
                MinBackgroundShareDate = day;
            }
            if (player > MaxPlayerShare)
            {
                MaxPlayerShare = player;
                MaxPlayerShareDate = day;
            }
        }

        // The money the player's cards moved today, by year: read from the books of the fake game from where the last day stopped.
        private void TrackMoney(int year)
        {
            List<MoneyMove> earned = Game.Earned;
            for (; _earnedAt < earned.Count; _earnedAt++)
            {
                if (earned[_earnedAt].Company != FakeGpuGame.Player) continue;
                double sum;
                SalesByYear.TryGetValue(year, out sum);
                SalesByYear[year] = sum + earned[_earnedAt].Value;
            }
            List<MoneyMove> spent = Game.Spent;
            for (; _spentAt < spent.Count; _spentAt++)
            {
                if (spent[_spentAt].Company != FakeGpuGame.Player) continue;
                double[] row;
                if (!SpentByYear.TryGetValue(year, out row)) SpentByYear[year] = row = new double[5];
                int origin = spent[_spentAt].Origin;
                if (origin >= 0 && origin < row.Length) row[origin] += spent[_spentAt].Value;
            }
        }

        private void RecordMonth()
        {
            var row = new SimMonth
            {
                Year = Game.Date.Year,
                Month = Game.Date.Month,
                BackgroundShare = World.Market.ShareOf(MarketItem.BackgroundOwner),
                PlayerShare = World.Market.ShareOf(MarketItem.PlayerOwner),
            };
            foreach (GpuProductSave p in State.Data.products)
            {
                if (p.owner != GpuState.OwnerPlayer || p.status != GpuState.StatusSelling) continue;
                row.PlayerCardsOnSale++;
                row.PlayerStock += p.stock;
            }
            Months.Add(row);
            RecordSegmentShares();
        }

        // NFR-7: who sells how much of each open segment. The cards of the vendors and of the background belong to one segment, so their sales over thirty days are the
        // sales of that segment; the cards of the player (in every segment) are not split, so the figures are meant for runs without the player when they are compared.
        private void RecordSegmentShares()
        {
            foreach (GpuSegmentDto seg in World.Market.OpenSegmentsAt(Game.Date))
            {
                var sold = new Dictionary<string, int>(StringComparer.Ordinal);
                long total = 0;
                foreach (GpuMarketEntry entry in World.Market.TopProducts(seg.id, int.MaxValue))
                {
                    int have;
                    sold.TryGetValue(entry.OwnerKey, out have);
                    sold[entry.OwnerKey] = have + entry.Sold30;
                    total += entry.Sold30;
                }
                if (total <= 0) continue;
                foreach (KeyValuePair<string, int> pair in sold)
                {
                    if (pair.Value <= 0) continue;
                    SegmentShares.Add(new SimSegmentShare
                    {
                        Year = Game.Date.Year, Month = Game.Date.Month, Segment = seg.id, Owner = pair.Key, Sold = pair.Value, Share = (float)pair.Value / total,
                    });
                }
            }
        }

        /// <summary>
        /// The longest run of months, from the 31st day of the run, in which one vendor held more than <paramref name="share"/> of one segment (NFR-7: more than 70 % for
        /// more than 36 months is a failure); <paramref name="who"/> says which vendor, where and from when.
        /// </summary>
        public int LongestMonthsAbove(float share, out string who)
        {
            who = string.Empty;
            int best = 0;
            var runs = new Dictionary<string, int>(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int lastKey = -1;
            foreach (SimSegmentShare row in SegmentShares)
            {
                int key = row.Year * 12 + row.Month;
                if (key != lastKey)
                {
                    // a month is over: whoever was not above the share in it starts again
                    var gone = new List<string>();
                    foreach (string k in runs.Keys)
                    {
                        if (!seen.Contains(k)) gone.Add(k);
                    }
                    foreach (string k in gone) runs.Remove(k);
                    seen.Clear();
                    lastKey = key;
                }
                if (!row.Owner.StartsWith(MarketItem.VendorPrefix, StringComparison.Ordinal) || row.Share <= share) continue;
                string id = row.Segment + "|" + row.Owner;
                int length;
                runs.TryGetValue(id, out length);
                runs[id] = length + 1;
                seen.Add(id);
                if (length + 1 > best)
                {
                    best = length + 1;
                    who = row.Owner + " in " + row.Segment + " until " + row.Year + "-" + row.Month.ToString("00", CultureInfo.InvariantCulture) + " (" + (row.Share * 100f).ToString("0", CultureInfo.InvariantCulture) + " %)";
                }
            }
            return best;
        }

        /// <summary>The mean share of an owner (<c>vendor:nvidia</c>, <c>background</c>) in a segment over the months of a year; 0 when it sold nothing there.</summary>
        public float MeanShare(int year, string segment, string owner)
        {
            double sum = 0;
            int months = 0;
            foreach (SimMonth row in Months)
            {
                if (row.Year == year) months++;
            }
            foreach (SimSegmentShare row in SegmentShares)
            {
                if (row.Year == year && row.Segment == segment && row.Owner == owner) sum += row.Share;
            }
            return months == 0 ? 0f : (float)(sum / months);
        }

        /// <summary>NFR-2 and the sanity of the state: finite numbers, no negative stock, popularity within its bounds, the lists within their limits.</summary>
        private void CheckState()
        {
            Require(!float.IsNaN(Game.Cash) && !float.IsInfinity(Game.Cash), "the money of the player is not a number");
            GpuSaveSection data = State.Data;
            Require(data.products.Count <= World.Content.Balance.limits.products, "more products than the limit: " + data.products.Count);
            foreach (GpuProductSave p in data.products)
            {
                if (p.owner == GpuState.OwnerBackground && p.released != null)
                {
                    int age = GpuMarketService.MonthsBetween(p.released, Game.Date);
                    Require(age <= World.Content.Balance.background.refreshMonths + 1, p.id + ": the card of the others is " + age + " months old");
                    if (FirstBackgroundPerf == 0d) FirstBackgroundPerf = p.stats.effectivePerf;
                    LastBackgroundPerf = p.stats.effectivePerf;
                }
                Require(p.stock >= 0, p.id + ": negative stock " + p.stock);
                Require(IsFinite(p.price) && p.price >= 0f, p.id + ": price " + p.price);
                Require(p.stats != null && IsFinite((float)p.stats.effectivePerf) && p.stats.effectivePerf >= 0d, p.id + ": the performance is not a number");
                Require(p.lines >= 0 && p.lines <= Plan.CapacityLines, p.id + ": lines " + p.lines);
                Require(p.segIds.Count == p.pop.Count, p.id + ": a popularity for every segment");
                foreach (float pop in p.pop) Require(IsFinite(pop) && pop >= 0f && pop <= 1f, p.id + ": popularity " + pop);
                if (p.ledger != null) Require(p.ledger.income.Count <= World.Content.Balance.limits.ledgerMonths, p.id + ": the books are too long");
            }
            float background = World.Market.ShareOf(MarketItem.BackgroundOwner);
            float player = World.Market.ShareOf(MarketItem.PlayerOwner);
            Require(background + player <= 1.0001f, "the shares add up to more than the market: " + (background + player));
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        // ---- the bot ----

        private void BotMonth()
        {
            if (Plan.FoundYear <= 0 || Game.Date.Year < Plan.FoundYear) return;
            if (!World.Division.IsFounded)
            {
                bool founded = World.Division.Found();
                Require(founded, "the division cannot be founded");
                if (!founded) return;
                Events.Add(Game.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " division founded");
            }
            FillLaboratory();
            MaybeStartProject();
            TendLineup();
        }

        /// <summary>The laboratory is never idle: every node of a year not after this one that is not done is put in the queue (the ones that cannot be - a game technology is missing, the queue is full - wait for next month).</summary>
        private void FillLaboratory()
        {
            foreach (GpuResearchNodeDto node in World.Content.ResearchNodes)
            {
                if (node.year > Game.Date.Year) continue;
                if (World.Research.IsResearched(node.id) || World.Research.IsPlanned(node.id)) continue;
                World.Research.Enqueue(node.id);
            }
        }

        private void MaybeStartProject()
        {
            if (Plan.Lineup != null)
            {
                StartLineupProjects();
                return;
            }
            if (World.Projects.Active.Count > 0) return;
            foreach (GpuProductSave p in State.Data.products)
            {
                if (p.owner == GpuState.OwnerPlayer && p.status == GpuState.StatusReady) return;   // a card waits for its release
            }
            DateTime due = _lastProject == DateTime.MinValue ? Game.Date : _lastProject.AddYears(Math.Max(1, Plan.ReleaseEveryYears));
            if (Game.Date < due) return;

            GpuDesignInput input;
            if (!TryDesign(TargetSegment(), out input)) return;
            input.Name = "Sim " + Game.Date.Year;
            string reason;
            string id = World.Projects.Start(input, out reason);
            Require(id != null, "the project of the bot is refused: " + reason);
            if (id == null) return;
            _lastProject = Game.Date;
            _projectsStarted++;
        }

        // The lineup bot: for every segment of the plan that is open, a project every ReleaseEveryYears years (the first as soon as the segment is open and the parts are researched).
        private void StartLineupProjects()
        {
            foreach (string segmentId in Plan.Lineup)
            {
                if (World.Projects.Active.Count >= World.Content.Balance.design.maxParallelProjects) return;
                GpuSegmentDto seg = World.Market.OpenSegmentsAt(Game.Date).Find(x => x.id == segmentId);
                if (seg == null) continue;
                bool onTheWay = false;
                foreach (GpuProjectSave project in World.Projects.Active)
                {
                    string target;
                    if (_segmentOfDesign.TryGetValue(project.designId, out target) && target == segmentId) onTheWay = true;
                }
                foreach (string id in _ready)
                {
                    string target;
                    if (_segmentOfCard.TryGetValue(id, out target) && target == segmentId) onTheWay = true;
                }
                if (onTheWay) continue;
                DateTime last;
                if (_lastStart.TryGetValue(segmentId, out last) && Game.Date < last.AddYears(Math.Max(1, Plan.ReleaseEveryYears))) continue;

                GpuDesignInput input;
                if (!TryDesign(seg, out input)) continue;
                input.Name = "Sim " + segmentId + " " + Game.Date.Year;
                string reason;
                string projectId = World.Projects.Start(input, out reason);
                Require(projectId != null, "the project of the bot is refused: " + reason);
                if (projectId == null) continue;
                _segmentOfDesign[World.Projects.Find(projectId).designId] = segmentId;
                _lastStart[segmentId] = Game.Date;
                _projectsStarted++;
            }
        }

        /// <summary>
        /// The design of the bot: the newest researched parts, the node of the year, the video memory of the segment, and the best of a few tries - three buses, three dies,
        /// two clocks, the weakest researched cooler that holds 85 degrees - by performance among those the card of which costs at most 55 % of the budget.
        /// </summary>
        private bool TryDesign(GpuSegmentDto seg, out GpuDesignInput chosen)
        {
            chosen = default(GpuDesignInput);
            DateTime today = Game.Date;
            GpuContentDb db = World.Content;

            GpuArchitectureDto arch = null;
            foreach (GpuArchitectureDto a in db.Architectures)
            {
                if (World.Research.IsResearched(a.id) && (arch == null || a.year >= arch.year)) arch = a;
            }
            GpuMemoryDto mem = null;
            foreach (GpuMemoryDto m in db.Memories)
            {
                if (World.Research.IsResearched(m.id) && (mem == null || m.year >= mem.year)) mem = m;
            }
            var coolers = new List<GpuCoolerDto>();
            foreach (GpuCoolerDto c in db.Coolers)
            {
                if (World.Research.IsResearched(c.id)) coolers.Add(c);
            }
            if (arch == null || mem == null || coolers.Count == 0) return false;
            if (Plan.Lineup != null)
            {
                // the lineup bot designs for a segment only with an architecture that has what the segment asks for this year: it waits for the laboratory instead of sending a card the segment rates x0.35
                foreach (string need in GpuMarketMath.RequiredFeatures(seg.featureReqs, today.Year))
                {
                    if (!arch.features.Contains(need)) return false;
                }
            }
            coolers.Sort((a, b) => b.thermalResistance.CompareTo(a.thermalResistance));   // the weakest first

            float year = today.Year + (today.Month - 1) / 12f;
            NodeSpec node;
            if (!Game.TryGetNode(db.NodeNameFor(year), out node)) return false;

            float budget = World.Market.BudgetOf(seg, today.Year);
            float maxClock = GpuDesignMath.MaxClockMHz(Game.MaxFrequencyKHz(FakeGpuGame.Player), arch.clockRatio);
            float maxDie = db.MaxDie(today.Year);
            float vram = GpuRivalGenerator.PowerOfTwoAtLeast(GpuMarketMath.VramRequirement(seg.vramReq, year), mem.maxVramMB);

            bool haveBest = false, haveCheap = false;
            GpuDesignInput best = default(GpuDesignInput), cheap = default(GpuDesignInput);
            double bestPerf = -1d;
            float cheapCost = float.MaxValue;
            foreach (int bus in mem.busWidths)
            {
                foreach (float dieShare in new[] { 0.3f, 0.5f, 0.7f })
                {
                    foreach (float clockShare in new[] { 0.8f, 0.95f })
                    {
                        foreach (GpuCoolerDto cooler in coolers)
                        {
                            var input = new GpuDesignInput
                            {
                                ArchId = arch.id, MemId = mem.id, CoolerId = cooler.id, NodeName = node.Name,
                                DieMm2 = Math.Max(World.Content.Balance.design.minDie, dieShare * maxDie),
                                ClockMHz = maxClock * clockShare, VramMB = vram, BusWidthBits = bus,
                            };
                            GpuStats stats = World.Designer.Evaluate(input);
                            if (!stats.Valid || stats.TempC > 85f) continue;
                            if (stats.UnitCost <= 0.55f * budget && stats.EffectivePerf > bestPerf)
                            {
                                best = input;
                                bestPerf = stats.EffectivePerf;
                                haveBest = true;
                            }
                            if (stats.UnitCost < cheapCost)
                            {
                                cheap = input;
                                cheapCost = stats.UnitCost;
                                haveCheap = true;
                            }
                            break;   // the weakest cooler that holds 85 degrees is the one
                        }
                    }
                }
            }
            if (haveBest) chosen = best;
            else if (haveCheap) chosen = cheap;
            return haveBest || haveCheap;
        }

        // The segment the bot designs for: the mainstream gaming segment when the market has it, otherwise the first one that is open.
        private GpuSegmentDto TargetSegment()
        {
            List<GpuSegmentDto> open = World.Market.OpenSegmentsAt(Game.Date);
            foreach (GpuSegmentDto seg in open)
            {
                if (seg.id == "gaming.mid") return seg;
            }
            return open.Count > 0 ? open[0] : World.Content.Segments[0];
        }

        private void ReleaseReady()
        {
            if (_ready.Count == 0) return;
            foreach (string id in _ready.ToArray())
            {
                string reason;
                string target;
                bool lineup = _segmentOfCard.TryGetValue(id, out target);
                float price = lineup ? PriceFor(target) : World.Lineup.SuggestedPrice(id);
                bool released = World.Lineup.Release(id, price, true, 2, out reason);
                Require(released, "the release of " + id + " is refused: " + reason);
                if (!released) continue;
                World.Lineup.SetAuto(id, true);
                CardsReleased++;
                GpuSegmentDto fit = lineup ? World.Content.Segment(target) : World.Lineup.FitSegment(id);
                GpuProductSave card = World.Lineup.Find(id);
                double dev;
                _devCost.TryGetValue(card.designId, out dev);
                CardLog.Add(new SimCard
                {
                    Id = id, Name = card.name, Released = Game.Date, DevCost = dev, Price = price,
                    SegmentId = fit != null ? fit.id : string.Empty, Budget = fit != null ? World.Market.BudgetOf(fit, Game.Date.Year) : 0f,
                });
                if (Plan.ReplaceOnRelease) RetireOthers(id);
                Releases.Add(Game.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " " + card.name + " perf " + card.stats.effectivePerf.ToString("0", CultureInfo.InvariantCulture)
                             + " price " + price.ToString("0", CultureInfo.InvariantCulture) + " cost " + card.stats.projectCost.ToString("0.0e0", CultureInfo.InvariantCulture));
            }
            _ready.Clear();
        }

        // The price of a card the lineup bot makes for a segment: the proposal of the release dialog, 0.9 of the budget of the segment.
        private float PriceFor(string segmentId)
        {
            GpuSegmentDto seg = World.Content.Segment(segmentId);
            return seg == null ? 0f : GpuLineup.SuggestedPriceShare * World.Market.BudgetOf(seg, Game.Date.Year);
        }

        // The generation before the new card leaves the market (the lineup bot: the generation of the same segment).
        private void RetireOthers(string newId)
        {
            string segment;
            bool lineup = _segmentOfCard.TryGetValue(newId, out segment);
            foreach (GpuProductSave p in State.Data.products.ToArray())
            {
                if (p.id == newId || p.owner != GpuState.OwnerPlayer || p.status != GpuState.StatusSelling) continue;
                string other;
                if (lineup && _segmentOfCard.TryGetValue(p.id, out other) && other != segment) continue;
                float paid = World.Lineup.Retire(p.id);
                Require(paid >= 0f, "the retirement of " + p.id + " is refused");
                CardsRetired++;
            }
        }

        /// <summary>Every month: the price follows the budget of the segment, an outdated card goes off the market when a newer one is on sale.</summary>
        private void TendLineup()
        {
            DateTime today = Game.Date;
            var newest = new Dictionary<string, GpuProductSave>(StringComparer.Ordinal);   // by segment of the lineup bot; one key for the single-card bot
            foreach (GpuProductSave p in State.Data.products)
            {
                if (p.owner != GpuState.OwnerPlayer || p.status != GpuState.StatusSelling) continue;
                string key = GroupOf(p);
                GpuProductSave have;
                if (!newest.TryGetValue(key, out have) || DateOfRelease(p) > DateOfRelease(have)) newest[key] = p;
            }
            foreach (GpuProductSave p in State.Data.products.ToArray())
            {
                if (p.owner != GpuState.OwnerPlayer || p.status != GpuState.StatusSelling) continue;
                if (newest[GroupOf(p)] != p && World.Lineup.IsOld(p, today))
                {
                    float paid = World.Lineup.Retire(p.id);
                    Require(paid >= 0f, "the retirement of " + p.id + " is refused");
                    CardsRetired++;
                    continue;
                }
                string target;
                World.Lineup.SetPrice(p.id, _segmentOfCard.TryGetValue(p.id, out target) ? PriceFor(target) : World.Lineup.SuggestedPrice(p.id));
            }
        }

        private string GroupOf(GpuProductSave p)
        {
            string segment;
            return Plan.Lineup != null && _segmentOfCard.TryGetValue(p.id, out segment) ? segment : string.Empty;
        }

        private static DateTime DateOfRelease(GpuProductSave p)
        {
            return p.released == null ? DateTime.MinValue : new DateTime(p.released.y, p.released.m, p.released.d);
        }

        // ---- what the tests read ----

        /// <summary>
        /// The months from the release of a card until what it earned, less what it cost to make and to keep (the books of the card), covers the money its development took; the month
        /// of the release is the first. 0 when it never does within the run. The card of a run that goes on in the next years is counted as far as the run goes.
        /// </summary>
        public int PaybackMonths(SimCard card)
        {
            GpuProductSave product = State.Data.products.Find(p => p.id == card.Id);
            if (product == null || product.ledger == null) return 0;
            GpuLedgerSave ledger = product.ledger;
            int first = GpuLedgerBook.MonthIndex(card.Released);
            double cumulative = 0d;
            for (int slot = Math.Max(0, first - ledger.startMonthIndex); slot < ledger.income.Count; slot++)
            {
                cumulative += ledger.income[slot] - ledger.cost[slot];
                if (cumulative >= card.DevCost) return ledger.startMonthIndex + slot - first + 1;
            }
            return 0;
        }

        /// <summary>What the card made over the first <paramref name="months"/> months from its release, in money: the income less the cost, from the books of the card.</summary>
        public double ProfitOver(SimCard card, int months)
        {
            GpuProductSave product = State.Data.products.Find(p => p.id == card.Id);
            if (product == null || product.ledger == null) return 0d;
            GpuLedgerSave ledger = product.ledger;
            int first = GpuLedgerBook.MonthIndex(card.Released);
            double total = 0d;
            for (int slot = Math.Max(0, first - ledger.startMonthIndex); slot < ledger.income.Count && ledger.startMonthIndex + slot < first + months; slot++)
                total += ledger.income[slot] - ledger.cost[slot];
            return total;
        }

        /// <summary>The income of the player from the cards in a calendar year; 0 for a year without any.</summary>
        public double SalesIn(int year)
        {
            double value;
            return SalesByYear.TryGetValue(year, out value) ? value : 0d;
        }

        public int ProjectsStarted
        {
            get { return _projectsStarted; }
        }

        /// <summary>The player's cards that are on the market now.</summary>
        public int PlayerCardsOnSale()
        {
            int n = 0;
            foreach (GpuProductSave p in State.Data.products)
            {
                if (p.owner == GpuState.OwnerPlayer && p.status == GpuState.StatusSelling) n++;
            }
            return n;
        }

        /// <summary>The mean of the share of the background over the months of a year (thirty days at each 1st).</summary>
        public float MeanBackgroundShare(int year)
        {
            double sum = 0;
            int n = 0;
            foreach (SimMonth row in Months)
            {
                if (row.Year != year) continue;
                sum += row.BackgroundShare;
                n++;
            }
            return n == 0 ? 0f : (float)(sum / n);
        }

        public float MeanPlayerShare(int year)
        {
            double sum = 0;
            int n = 0;
            foreach (SimMonth row in Months)
            {
                if (row.Year != year) continue;
                sum += row.PlayerShare;
                n++;
            }
            return n == 0 ? 0f : (float)(sum / n);
        }

        /// <summary>The section of the save as the core writes it, compact: the fingerprint of a run and its size.</summary>
        public string SectionText()
        {
            return ModJson.Write(State.Write(), false);
        }

        /// <summary>A few lines for the log of the run: the years with the shares, the cards, the money, then the timing.</summary>
        public string Report(bool full = true)
        {
            var sb = new StringBuilder();
            sb.Append("  sim   ").Append(Plan.Name).Append(": ").Append(Plan.StartYear).Append('-').Append(Plan.EndYear).Append(", ").Append(Days).Append(" days, division from ")
              .Append(Plan.FoundYear).Append(", ").Append(Plan.CapacityLines).Append(" lines (").Append(Plan.CpuLines).Append(" for the CPUs), ")
              .Append(CardsReleased).Append(" cards released, ").Append(CardsRetired).Append(" retired, ").Append(PlayerCardsOnSale()).Append(" on sale at the end\n");
            foreach (int year in new[] { 1985, 1990, 1995, 1996, 1997, 1998, 1999, 2000, 2005, 2010 })
            {
                if (!full || year < Plan.StartYear || year > Plan.EndYear) continue;
                sb.Append("  sim     ").Append(year).Append(": other makers ").Append((MeanBackgroundShare(year) * 100f).ToString("0.0", CultureInfo.InvariantCulture))
                  .Append(" %, player ").Append((MeanPlayerShare(year) * 100f).ToString("0.0", CultureInfo.InvariantCulture)).Append(" %\n");
            }
            sb.Append("  sim     the other makers keep at least ").Append((MinBackgroundShareBefore2000 * 100f).ToString("0.0", CultureInfo.InvariantCulture)).Append(" % before 2000 (lowest on ")
              .Append(MinBackgroundShareDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("); the player's best is ")
              .Append((MaxPlayerShare * 100f).ToString("0.0", CultureInfo.InvariantCulture)).Append(" % on ").Append(MaxPlayerShareDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
              .Append("; the cards reserved up to ").Append(MaxReserved).Append(" lines a day\n");
            if (full) sb.Append("  sim     the card of the others: performance ").Append(FirstBackgroundPerf.ToString("0", CultureInfo.InvariantCulture)).Append(" at the start, ")
              .Append(LastBackgroundPerf.ToString("0", CultureInfo.InvariantCulture)).Append(" at the end\n");
            sb.Append("  perf    ").Append(TimingText());
            return sb.ToString();
        }

        /// <summary>The milliseconds per day each handler took on average, largest first: <c>market.sell 0.4123, production 0.2210, ...</c>.</summary>
        public string BreakdownText()
        {
            var list = new List<KeyValuePair<string, double>>(HandlerMs);
            list.Sort((x, y) => y.Value.CompareTo(x.Value));
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, double> pair in list)
            {
                bool month = pair.Key.EndsWith(".month", StringComparison.Ordinal);
                int units = month ? Perf.Samples(GpuPerfMeter.Month) : Days;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(pair.Key).Append(' ').Append((pair.Value / Math.Max(1, units)).ToString("0.0000", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        public string TimingText()
        {
            return "day n=" + Perf.Samples(GpuPerfMeter.Day) + " avg " + Perf.AverageMs(GpuPerfMeter.Day).ToString("0.0000", CultureInfo.InvariantCulture) + " ms max "
                   + Perf.MaxMs(GpuPerfMeter.Day).ToString("0.0000", CultureInfo.InvariantCulture) + " ms (budget 0.5); month n=" + Perf.Samples(GpuPerfMeter.Month) + " avg "
                   + Perf.AverageMs(GpuPerfMeter.Month).ToString("0.0000", CultureInfo.InvariantCulture) + " ms max " + Perf.MaxMs(GpuPerfMeter.Month).ToString("0.0000", CultureInfo.InvariantCulture)
                   + " ms (budget 5)";
        }
    }
}
