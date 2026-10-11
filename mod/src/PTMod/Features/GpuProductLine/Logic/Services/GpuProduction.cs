using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The production of the player's cards (design.md, "GpuProduction и FactoryAccess"; FR-5; F12, F17, F18, F28, F28', F29). Every day on
    /// <c>TickPhase.PreContractSellTick</c> - before the game produces any CPU that day - it works one of two ways, by the type of the company that day:
    /// <list type="bullet">
    /// <item><b>Own factory</b> (TSK-016). It adds up the lines of the cards that are not taken off the market (the automatic ones get their lines from F29), cuts
    /// them to what the factory has free (F28: capacity minus the lines of the foundry clients minus what is given out already; F28' with "CPU first", read every day:
    /// minus the lines of the CPUs too) by the largest remainders, writes the reserve into the private counters of <c>Factory</c> (<c>totalAllocatedLines</c>,
    /// <c>totalDemandedLines</c>), so the operating cost of the day and the monthly report of the capacity see the cards, and produces on those lines what
    /// <c>Factory.ProduceWafers</c> would: 50 wafers a line, the dies of the wafer of the company at the yield of the node, the failure rate of the design. The
    /// material of a wafer is asked from the game every day (<c>ProductionMath.WaferMaterialCost</c>), so a shock of <c>production.waferCost</c> reaches the cards;
    /// video memory, board and cooler are priced by the year (F12) with <c>gpu.vramCost</c> and <c>production.unitCost</c>, and put on the cards that work only;
    /// <c>production.output</c> multiplies the cards put in stock. The money goes out under <c>ExpenseOrigin.Factories</c>. A card that got fewer lines than it
    /// asked for carries the warning "Не хватает линий: N из M" for the day (<see cref="IsShort"/>). The lines the player set are never rewritten: when the
    /// factory shrinks (lines sold), the cut of the very next day follows the new capacity and the cards keep their numbers.</item>
    /// <item><b>Fabless</b> (TSK-017; F18). Nothing is reserved in a factory. The player orders cards a day (<c>unitsPerDay</c>; the automatic mode orders the mean
    /// demand of seven days); a card costs its unit cost with the die made by a foundry - <c>CpuMath.RealUnitCost</c> with the markup
    /// <c>ProductionMath.BaseMarkupPrice</c> (2.0) at the yield <c>Lerp(min, max, fablessYieldProgress)</c> of the node - and the order is paid under
    /// <c>ExpenseOrigin.Outsourcing</c>; <c>units x (1 - FR) x production.output</c> cards go into the stock. The royalty of the sales
    /// (<see cref="RoyaltyRate"/>, <c>ExpenseOrigin.Royalties</c>) is taken by the market when it pays the sales.</item>
    /// </list>
    /// When the company changes its type between two days (it sold its manufacturing division, or got a factory back), the cards are carried over the same day:
    /// lines become cards a day (the dies those lines gave on the last day of the own factory) and back.
    /// </summary>
    public sealed class GpuProduction
    {
        /// <summary>The warning of a card that got fewer lines than it asked for ({0} granted, {1} asked).</summary>
        public const string ShortageKey = "gpu.lineup.shortage";

        /// <summary>The most cards a stock holds (the game caps a CPU stock the same way).</summary>
        public const int MaxStock = 2000000000;

        /// <summary><c>ProductionMath.BaseRoyalties</c>: the share of the sales of a fabless company's cards that goes to royalties (F18).</summary>
        public const float RoyaltyRate = 0.05f;

        private const int DemandWindowDays = 7;

        private readonly GpuContentDb _content;
        private readonly GpuState _state;
        private readonly IGpuGame _game;
        private readonly Func<bool> _cpuPriority;
        private readonly Func<bool> _available;
        private readonly Action<string> _warn;

        private readonly Dictionary<string, int> _granted = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _requested = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _byNode = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> _unitCost = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<int>> _demand = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _contractDemand = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _lastDpw = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);
        private DateTime _day = DateTime.MinValue;
        private bool? _fablessSeen;

        /// <param name="cpuPriority">The switch "CPU first" (<c>ModSettings</c>), read every day.</param>
        /// <param name="available">The ability <see cref="GpuCapability.Production"/> survived the self-check.</param>
        /// <param name="warn">English lines for the log (a card on a process node the game does not know, a change of the type of the company); may be null.</param>
        public GpuProduction(GpuContentDb content, GpuState state, IGpuGame game, Func<bool> cpuPriority, Func<bool> available, Action<string> warn)
        {
            if (content == null) throw new ArgumentNullException("content");
            if (state == null) throw new ArgumentNullException("state");
            if (game == null) throw new ArgumentNullException("game");
            _content = content;
            _state = state;
            _game = game;
            _cpuPriority = cpuPriority ?? (() => false);
            _available = available ?? (() => true);
            _warn = warn;
        }

        /// <summary>The lines the player's cards got today, after the cut.</summary>
        public int ReservedToday { get; private set; }

        /// <summary>The lines the player's cards asked for today.</summary>
        public int RequestedToday { get; private set; }

        /// <summary>The lines the cards were allowed to take today (F28 / F28').</summary>
        public int AvailableToday { get; private set; }

        /// <summary>The cards a fabless company ordered from the foundry today (F18).</summary>
        public int OutsourcedToday { get; private set; }

        /// <summary>The company was fabless on the last day of production (no reserve in a factory; the cards are ordered by the piece).</summary>
        public bool FablessToday { get; private set; }

        private GpuBalanceDto Balance
        {
            get { return _content.Balance ?? new GpuBalanceDto(); }
        }

        private bool IsToday
        {
            get { return _day == _game.Today.Date; }
        }

        /// <summary>The lines the cards on this process node got today (the shared experience of the node, F26, patch GP2); 0 on another day.</summary>
        public int ReservedOn(string nodeName)
        {
            int lines;
            return IsToday && nodeName != null && _byNode.TryGetValue(nodeName, out lines) ? lines : 0;
        }

        /// <summary>The card got fewer lines than it asked for today: <paramref name="granted"/> of <paramref name="requested"/> (the warning of the lineup).</summary>
        public bool IsShort(string productId, out int granted, out int requested)
        {
            granted = 0;
            requested = 0;
            if (!IsToday || productId == null || !_requested.TryGetValue(productId, out requested)) return false;
            _granted.TryGetValue(productId, out granted);
            return granted < requested;
        }

        /// <summary>The lines the card got today (0 on another day); for a fabless company, the cards ordered today.</summary>
        public int GrantedLines(string productId)
        {
            int lines;
            return IsToday && productId != null && _granted.TryGetValue(productId, out lines) ? lines : 0;
        }

        /// <summary>The cost of one card as of the last day of production (F12: die, video memory, board, cooler, with the channels; with the foundry's markup when fabless); 0 before the first day.</summary>
        public float UnitCostOf(string productId)
        {
            float cost;
            return productId != null && _unitCost.TryGetValue(productId, out cost) ? cost : 0f;
        }

        /// <summary>
        /// The demand of a card on a day (sold plus missed sales), for the automatic mode (F29: the mean of the last seven days). The market (TSK-019) records it;
        /// the window is not saved - after a load the automatic mode keeps the lines it had until the first day of sales.
        /// </summary>
        public void RecordDemand(string productId, int sold, int missed)
        {
            if (string.IsNullOrEmpty(productId)) return;
            List<int> days;
            if (!_demand.TryGetValue(productId, out days))
            {
                days = new List<int>(DemandWindowDays);
                _demand[productId] = days;
            }
            int contract;
            _contractDemand.TryGetValue(productId, out contract);
            long sum = (long)Math.Max(0, sold) + Math.Max(0, missed) + Math.Max(0, contract);
            days.Add(sum > int.MaxValue ? int.MaxValue : (int)sum);
            if (days.Count > DemandWindowDays) days.RemoveAt(0);
        }

        /// <summary>
        /// The cards a day the console orders ask of this card (FR-19): they are part of its demand for the automatic mode (F29), because they take the stock before the shop does.
        /// The number is written every day by <see cref="GpuConsoleOrders"/> (the sum of the orders tied to the card, 0 when they end) and added to the day the market records.
        /// </summary>
        public void SetContractDemand(string productId, int unitsPerDay)
        {
            if (string.IsNullOrEmpty(productId)) return;
            if (unitsPerDay <= 0) _contractDemand.Remove(productId);
            else _contractDemand[productId] = unitsPerDay;
        }

        /// <summary>The cards a day the console orders ask of this card (see <see cref="SetContractDemand"/>); 0 for none.</summary>
        public int ContractDemandOf(string productId)
        {
            int units;
            return productId != null && _contractDemand.TryGetValue(productId, out units) ? units : 0;
        }

        /// <summary>The mean demand of the recorded days (at most seven), or -1 when nothing is recorded.</summary>
        public float AverageDemand(string productId)
        {
            List<int> days;
            if (productId == null || !_demand.TryGetValue(productId, out days) || days.Count == 0) return -1f;
            double sum = 0;
            foreach (int day in days) sum += day;
            return (float)(sum / days.Count);
        }

        /// <summary>Forgets what belongs to a session (the day's reserve, the costs, the demand windows, the type of the company): a save was loaded or a new game started.</summary>
        public void Reset()
        {
            _granted.Clear();
            _requested.Clear();
            _byNode.Clear();
            _unitCost.Clear();
            _demand.Clear();
            _contractDemand.Clear();
            _lastDpw.Clear();
            _warned.Clear();
            _day = DateTime.MinValue;
            _fablessSeen = null;
            ReservedToday = 0;
            RequestedToday = 0;
            AvailableToday = 0;
            OutsourcedToday = 0;
            FablessToday = false;
        }

        /// <summary>One day of production (<c>TickPhase.PreContractSellTick</c>): on the own factory reserve the lines, produce, pay; fabless order from the foundry.</summary>
        public void ProduceDay()
        {
            DateTime today = _game.Today.Date;
            _day = today;
            _granted.Clear();
            _requested.Clear();
            _byNode.Clear();
            ReservedToday = 0;
            RequestedToday = 0;
            AvailableToday = 0;
            OutsourcedToday = 0;

            if (!_state.Data.divisionFounded) return;
            string player = _game.PlayerKey;
            bool fabless = _game.IsFabless(player);
            if (_fablessSeen.HasValue && _fablessSeen.Value != fabless) SwitchType(player, fabless);
            _fablessSeen = fabless;
            FablessToday = fabless;

            if (!_available()) return;
            if (fabless)
            {
                ProduceOutsourced(player, today);
                return;
            }
            ProduceOnFactory(player, today);
        }

        // ---- the own factory (F17, F28, F28', F29) ----

        private void ProduceOnFactory(string player, DateTime today)
        {
            IGpuFactory factory = _game.FactoryOf(player);
            if (factory == null) return;

            List<GpuProductSave> products = Producing(false);
            if (products.Count == 0) return;

            float waferDiameter = _game.WaferDiameter(player);
            var days = new ProductDay[products.Count];
            var requested = new int[products.Count];
            for (int i = 0; i < products.Count; i++)
            {
                days[i] = Prepare(products[i], player, waferDiameter, false);
                int asked = RequestLines(products[i], days[i], factory.Capacity);
                _requested[products[i].id] = asked;
                requested[i] = days[i].Ready ? asked : 0;   // a card the game cannot make (unknown node) takes no line from the others
                RequestedToday += requested[i];
            }
            if (RequestedToday <= 0) return;

            bool cpuFirst = _cpuPriority();
            AvailableToday = GpuProductionMath.AvailableLines(factory.Capacity, factory.ClientLines, factory.Allocated, cpuFirst ? factory.CpuLinesInUse : 0, cpuFirst);
            int[] granted = GpuProductionMath.ScaleLines(requested, AvailableToday);
            int total = 0;
            foreach (int lines in granted) total += lines;
            if (!factory.Reserve(total, RequestedToday)) return;
            ReservedToday = total;

            float waferMaterial = _game.WaferMaterialCost();
            float maintenance = factory.MaintenancePerLine;
            float output = _game.Channels.Output(player);
            int month = GpuLedgerBook.MonthIndex(today);
            int ledgerMonths = Balance.limits.ledgerMonths;
            for (int i = 0; i < products.Count; i++)
            {
                GpuProductSave product = products[i];
                _granted[product.id] = granted[i];
                if (granted[i] <= 0 || !days[i].Ready) continue;
                AddToNode(product.node, granted[i]);

                ProductDay day = days[i];
                GpuProductionMath.DailyOutput result = GpuProductionMath.Produce(granted[i], GpuDesignMath.WafersPerLine, day.DpwFull, day.DpwYield, day.FailureRate,
                    day.DieMaterial, day.PerGoodExtras, maintenance, waferMaterial, output);
                AddStock(product, result.Good);
                _game.Spend(player, result.Cost, GpuExpense.Factories);
                GpuLedgerBook.AddProduction(product.ledger ?? (product.ledger = new GpuLedgerSave()), month, result.Good, result.Failed, result.Cost, ledgerMonths);
            }
        }

        // ---- fabless (F18) ----

        private void ProduceOutsourced(string player, DateTime today)
        {
            List<GpuProductSave> products = Producing(true);
            if (products.Count == 0) return;

            float waferDiameter = _game.WaferDiameter(player);
            float output = _game.Channels.Output(player);
            int month = GpuLedgerBook.MonthIndex(today);
            int ledgerMonths = Balance.limits.ledgerMonths;
            foreach (GpuProductSave product in products)
            {
                ProductDay day = Prepare(product, player, waferDiameter, true);
                int units = RequestUnits(product, day);
                _requested[product.id] = units;
                _granted[product.id] = day.Ready && day.DpwYield >= 1 ? units : 0;
                if (!day.Ready || day.DpwYield < 1 || units <= 0) continue;

                GpuProductionMath.DailyOutput result = GpuProductionMath.ProduceOutsourced(units, day.FailureRate, day.UnitCost, output);
                AddStock(product, result.Good);
                _game.Spend(player, result.Cost, GpuExpense.Outsourcing);
                GpuLedgerBook.AddProduction(product.ledger ?? (product.ledger = new GpuLedgerSave()), month, result.Good, result.Failed, result.Cost, ledgerMonths);
                OutsourcedToday = (int)Math.Min(int.MaxValue, (long)OutsourcedToday + units);
            }
        }

        // The automatic mode of a fabless card: the mean demand of the week, the failed cards included; the cards a day it has without sales recorded yet.
        private int RequestUnits(GpuProductSave p, ProductDay day)
        {
            if (!p.auto) return Math.Max(0, p.unitsPerDay);
            float demand = AverageDemand(p.id);
            if (demand < 0f) return Math.Max(0, p.unitsPerDay);
            int units = GpuProductionMath.AutoUnits(demand, day.FailureRate, Balance.production.autoDemandFactor);
            p.unitsPerDay = units;
            return units;
        }

        // ---- the change of the type of the company ----

        /// <summary>
        /// The company became fabless (lines become cards a day: what the lines gave on the last day of the own factory, or at today's wafer and the foundry's yield
        /// when there was no such day) or got its own factory again (cards a day become the lines that make them at the factory's wafer and the node's yield).
        /// Only the number of the other kind is written; the automatic mode stays as it was.
        /// </summary>
        private void SwitchType(string player, bool toFabless)
        {
            List<GpuProductSave> products = _state.Data.products;
            if (products == null) return;
            float waferDiameter = _game.WaferDiameter(player);
            int moved = 0;
            foreach (GpuProductSave p in products)
            {
                if (p == null || string.IsNullOrEmpty(p.id) || p.owner != GpuState.OwnerPlayer || p.status == GpuState.StatusRetired) continue;
                if (toFabless ? p.lines <= 0 : p.unitsPerDay <= 0) continue;

                int dpw;
                if (!toFabless || !_lastDpw.TryGetValue(p.id, out dpw))
                {
                    NodeSpec node;
                    if (string.IsNullOrEmpty(p.node) || !_game.TryGetNode(p.node, out node)) continue;
                    float yield = toFabless ? GpuProductionMath.FablessYield(node.MinYield, node.MaxYield, Balance.production.fablessYieldProgress) : _game.NodeYield(player, node);
                    dpw = _game.DiesPerWafer(p.die, waferDiameter, yield);
                }
                if (dpw < 1) continue;

                if (toFabless) p.unitsPerDay = GpuProductionMath.UnitsForLines(p.lines, GpuDesignMath.WafersPerLine, dpw);
                else p.lines = GpuProductionMath.LinesForUnits(p.unitsPerDay, GpuDesignMath.WafersPerLine, dpw);
                moved++;
            }
            if (_warn != null)
            {
                _warn("production: the company became " + (toFabless ? "fabless, the lines of " : "a maker with its own factory, the cards a day of ")
                      + moved.ToString(CultureInfo.InvariantCulture) + " card(s) were carried over");
            }
        }

        // ---- a card's day ----

        // The cards of the player that are produced: not taken off the market (ready or selling); with lines (own factory) or cards a day (fabless), or automatic.
        private List<GpuProductSave> Producing(bool fabless)
        {
            var list = new List<GpuProductSave>();
            List<GpuProductSave> products = _state.Data.products;
            if (products == null) return list;
            foreach (GpuProductSave p in products)
            {
                if (p == null || string.IsNullOrEmpty(p.id) || p.owner != GpuState.OwnerPlayer || p.status == GpuState.StatusRetired) continue;
                if ((fabless ? p.unitsPerDay : p.lines) <= 0 && !p.auto) continue;
                list.Add(p);
            }
            return list;
        }

        private struct ProductDay
        {
            public bool Ready;
            public int DpwFull, DpwYield;
            public float FailureRate, DieMaterial, PerGoodExtras, UnitCost;
        }

        // What a day of the card needs from the game: the node, the dies per wafer, the costs of the year with the channels (F12). Also the unit cost of the day:
        // on the own factory the die is the company's (markup 1), fabless it is the foundry's (outsourced, markup 2.0, the foundry's yield).
        private ProductDay Prepare(GpuProductSave p, string player, float waferDiameter, bool outsourced)
        {
            var day = new ProductDay();
            GpuStatsSave stats = p.stats ?? new GpuStatsSave();
            day.FailureRate = Math.Min(1f, Math.Max(0f, Finite(stats.failureRate)));

            NodeSpec node;
            if (string.IsNullOrEmpty(p.node) || !_game.TryGetNode(p.node, out node))
            {
                if (_warn != null && _warned.Add(p.id)) _warn("production: the card " + p.id + " is on the process node '" + p.node + "' the game does not know; it produces nothing");
                return day;
            }

            float yield = outsourced
                ? GpuProductionMath.FablessYield(node.MinYield, node.MaxYield, Balance.production.fablessYieldProgress)
                : _game.NodeYield(player, node);
            day.DpwFull = _game.DiesPerWafer(p.die, waferDiameter, 1f);
            day.DpwYield = _game.DiesPerWafer(p.die, waferDiameter, yield);
            if (!outsourced && day.DpwYield >= 1) _lastDpw[p.id] = day.DpwYield;

            IGpuChannels channels = _game.Channels;
            float unitCostMul = channels.UnitCost(player);
            float vramMul = channels.VramCost(player);
            GpuBalanceDto.DesignBlock rules = Balance.design;
            GpuDesignSave design = FindDesign(p.designId);
            GpuMemoryDto mem = design != null ? _content.Mem(design.memId) : null;
            GpuCoolerDto cooler = design != null ? _content.Cooler(design.coolerId) : null;
            GpuArchitectureDto arch = design != null ? _content.Arch(design.archId) : null;
            float material = arch != null ? arch.dieMaterialCost : Math.Max(0f, Finite(stats.dieMaterial));

            float costPerMB = 0f;
            if (mem != null && cooler != null)
            {
                costPerMB = GpuDesignMath.MemoryCostPerMB(mem, _game.Today.Year);
                day.PerGoodExtras = GpuDesignMath.PerGoodExtras(p.vramMB, costPerMB, vramMul, p.busWidth, rules.boardBase, rules.boardCostPerBit, cooler.cost, unitCostMul);
            }
            else
            {
                // the parts left the content: the card lives on its snapshot (D14), the channel of the unit cost still acts
                day.PerGoodExtras = Math.Max(0f, Finite(stats.perGoodExtras)) * GpuChannelReader.Usable(unitCostMul);
            }
            day.DieMaterial = material * GpuChannelReader.Usable(unitCostMul);
            day.Ready = true;

            // F12: the cost of one card today (the lineup and the market show it, the rivals and the console orders price by it; fabless it is what an order costs)
            if (day.DpwYield >= 1)
            {
                float markup = outsourced ? GpuDesigner.FablessMarkup : 1f;
                float dieCost = Math.Max(0f, Finite(_game.DieUnitCost(player, p.die, material, day.DpwYield, day.FailureRate, outsourced, markup)));
                float unitCost = mem != null && cooler != null
                    ? GpuDesignMath.UnitCost(dieCost, p.vramMB, costPerMB, vramMul, p.busWidth, rules.boardBase, rules.boardCostPerBit, cooler.cost, unitCostMul)
                    : (dieCost + Math.Max(0f, Finite(stats.perGoodExtras))) * GpuChannelReader.Usable(unitCostMul);
                _unitCost[p.id] = unitCost;
                day.UnitCost = unitCost;
            }
            return day;
        }

        // F29 for an automatic card (the lines are written back to the card, so the lineup shows them); the lines of the player otherwise.
        private int RequestLines(GpuProductSave p, ProductDay day, int capacity)
        {
            if (!p.auto) return Math.Max(0, p.lines);
            float demand = AverageDemand(p.id);
            if (demand < 0f) return Math.Max(0, p.lines);   // no sales recorded yet: keep the lines it has
            GpuBalanceDto.ProductionBlock rules = Balance.production;
            int lines = GpuProductionMath.AutoLines(demand, GpuDesignMath.WafersPerLine, day.DpwYield, day.FailureRate, capacity, rules.autoDemandFactor, rules.autoMaxShare);
            p.lines = lines;
            return lines;
        }

        private GpuDesignSave FindDesign(string designId)
        {
            List<GpuDesignSave> designs = _state.Data.designs;
            return designs == null || string.IsNullOrEmpty(designId) ? null : designs.Find(d => d != null && d.id == designId);
        }

        private void AddToNode(string nodeName, int lines)
        {
            if (string.IsNullOrEmpty(nodeName)) return;
            int sum;
            _byNode.TryGetValue(nodeName, out sum);
            _byNode[nodeName] = sum + lines;
        }

        private static void AddStock(GpuProductSave product, int good)
        {
            product.stock = (int)Math.Min(MaxStock, (long)Math.Max(0, product.stock) + Math.Max(0, good));
        }

        private static float Finite(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }
    }
}
