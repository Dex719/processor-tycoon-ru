using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.BankruptcyRecovery.Logic;
using GameContractHelper = ProcessorTycoon.ContractSystem.ContractHelper;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameManageLines = ProcessorTycoon.Production.UI.ManageLinesWindow;
using GamePlayer = ProcessorTycoon.Player;
using GameProductionMath = ProcessorTycoon.Production.Math.ProductionMath;
using GameRevenueOrigin = ProcessorTycoon.RevenueOrigin;
using GameStorageMath = ProcessorTycoon.Production.Math.StorageMath;
using GameWafer = ProcessorTycoon.Hardware.WaferSize;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>The production lines of one wafer size as the Assets tab lists them.</summary>
    internal struct WaferLinesView
    {
        public GameWafer Wafer;

        /// <summary>The name of the wafer size the game shows (<c>WaferSize.NormalName</c>).</summary>
        public string Name;

        /// <summary>The lines of this size (<c>Factory.AmountOfLines</c>).</summary>
        public int Lines;

        /// <summary>How many of them may be sold (0 when none).</summary>
        public int MaxSellable;

        /// <summary>Null when something can be sold, otherwise the key of the reason.</summary>
        public string BlockReason;
    }

    /// <summary>The preview of a sale of production lines (FR-3): what the player gets and what is left.</summary>
    internal struct LineSaleQuote
    {
        public GameWafer Wafer;

        /// <summary>The lines to sell, held to what may be sold.</summary>
        public int Lines;

        /// <summary><c>ProductionMath.SaleCost(Lines, Wafer)</c>: the same price as the game's own window.</summary>
        public double Revenue;

        public int CapacityBefore;
        public int CapacityAfter;

        /// <summary>The lines the active OEM contracts need (<c>ContractHelper.ContractedProductionLines</c>).</summary>
        public int ContractedLines;

        /// <summary>The capacity after the sale is below <see cref="ContractedLines"/>: deliveries may be missed and fines paid.</summary>
        public bool ContractsAtRisk;

        /// <summary>The balance after the sale.</summary>
        public double BalanceAfter;

        /// <summary>Null when the sale can be made, otherwise the key of the reason.</summary>
        public string BlockReason;
    }

    /// <summary>One CPU of the player as the Assets tab lists it (FR-4).</summary>
    internal struct CpuAssetView
    {
        public GameCpu Cpu;
        public string Name;

        /// <summary>The CPUs in the warehouse.</summary>
        public int Stock;

        /// <summary>What the whole stock brings (<c>StorageMath.Income</c>).</summary>
        public double StockIncome;

        /// <summary>The fine for the contracts of this CPU that would be broken (<c>ContractHelper.FinesFromContracts</c>).</summary>
        public double Fine;

        /// <summary>The margin of a day, <c>(price − real unit cost) × sold yesterday</c>.</summary>
        public double DailyMargin;
    }

    /// <summary>The preview of selling the stock of one CPU.</summary>
    internal struct StockQuote
    {
        public GameCpu Cpu;
        public int Stock;
        public double Income;
        public double BalanceAfter;

        /// <summary>Null when there is stock to sell.</summary>
        public string BlockReason;
    }

    /// <summary>The preview of taking a CPU out of production (FR-4 item 2): the fine and the income of the stock.</summary>
    internal struct RetireQuote
    {
        public GameCpu Cpu;
        public double Fine;
        public double StockIncome;

        /// <summary>The income of the stock minus the fine.</summary>
        public double Net;

        public double BalanceAfter;
    }

    /// <summary>The preview of the sale of the whole production (FR-5).</summary>
    internal struct DivisionQuote
    {
        /// <summary><c>ProductionMath.ManufacturingDivisionValue</c>: what the game's own sale would bring.</summary>
        public double Value;

        /// <summary>Null when the sale is open, otherwise the key of the reason.</summary>
        public string BlockReason;
    }

    /// <summary>
    /// The sale of assets in the game (design.md, <c>AssetSaleService</c>; FR-3 to FR-5): production lines, the warehouse, taking a CPU out of production
    /// and the way to the game's own sale of the whole production. The feature adds no prices and no rules of its own: it asks the game's functions for the price
    /// (<c>ProductionMath.SaleCost</c>, <c>StorageMath.Income</c>, <c>ContractHelper.FinesFromContracts</c>), and the small rules the game's window has
    /// (how many lines may go, when the whole sale is closed) are <see cref="AssetMath"/>'s. Player only; every sale is written to the journal.
    /// <list type="bullet">
    /// <item><b>Lines</b>: the price is taken before the lines are removed, like the game's <c>ManageLinesWindow.SellLines</c>; the income goes through
    /// <c>CapitalLedger.Revenue</c> in the item "Sales", then <c>ManageLinesWindow.UpdateWafers</c> refreshes the game's window.</item>
    /// <item><b>Stock</b>: <c>StorageMath.SellStock</c> does the income itself; the journal gets the same sum.</item>
    /// <item><b>Retiring a CPU</b>: <c>Cpu.TriggerRetirement</c> breaks the contracts (the fine goes to the report as the game does it) and sells the stock; the
    /// journal gets what the balance really changed by.</item>
    /// <item><b>The whole production</b>: only opens the game's <c>ManageLinesWindow</c>, where the sale goes through its own confirmation.</item>
    /// </list>
    /// A preview is a snapshot; the sale asks the game again and refuses when the numbers changed meanwhile (a window stays open while the game is paused,
    /// so they normally do not).
    /// </summary>
    internal sealed class AssetSaleService
    {
        private readonly RecoveryEnvironment _env;

        public AssetSaleService(RecoveryEnvironment env)
        {
            _env = env;
        }

        // ---- production lines ----

        /// <summary>The wafer sizes of the player with the lines of each, in the order of the game (empty without a company).</summary>
        public IReadOnlyList<WaferLinesView> ListWafers()
        {
            ICompany company = PlayerFinanceAdapter.PlayerCompany();
            var views = new List<WaferLinesView>();
            if (company == null || company.Factory == null || company.WaferSizes == null) return views;

            foreach (GameWafer wafer in company.WaferSizes)
            {
                int lines = company.Factory.AmountOfLines(wafer);
                int max = AssetMath.MaxSellableLines(lines, company.Factory.ProductionCapacity, MinLinesLeft, company.IsFabless);
                var view = new WaferLinesView();
                view.Wafer = wafer;
                view.Name = wafer.NormalName;
                view.Lines = lines;
                view.MaxSellable = max;
                view.BlockReason = AssetMath.LinesBlock(lines, max);
                views.Add(view);
            }
            return views;
        }

        /// <summary>The preview of selling <paramref name="lines"/> lines of a wafer size (FR-3 items 1, 3, 4); the number is held to what may be sold.</summary>
        public LineSaleQuote QuoteLines(GameWafer wafer, int lines)
        {
            var quote = new LineSaleQuote();
            quote.Wafer = wafer;
            quote.BlockReason = AssetMath.ReasonNoLines;

            ICompany company = PlayerFinanceAdapter.PlayerCompany();
            if (wafer == null || company == null || company.Factory == null) return quote;

            int onWafer = company.Factory.AmountOfLines(wafer);
            int capacity = company.Factory.ProductionCapacity;
            int max = AssetMath.MaxSellableLines(onWafer, capacity, MinLinesLeft, company.IsFabless);

            quote.Lines = AssetMath.ClampLines(lines, max);
            quote.BlockReason = AssetMath.LinesBlock(onWafer, max);
            quote.CapacityBefore = capacity;
            quote.CapacityAfter = AssetMath.CapacityAfter(capacity, quote.Lines);
            quote.Revenue = quote.Lines > 0 ? GameProductionMath.SaleCost(quote.Lines, wafer) : 0.0;
            quote.ContractedLines = GameContractHelper.ContractedProductionLines(GamePlayer.Instance);
            quote.ContractsAtRisk = AssetMath.ContractsAtRisk(quote.CapacityAfter, quote.ContractedLines);
            quote.BalanceAfter = company.MoneyAmount + quote.Revenue;
            return quote;
        }

        /// <summary>
        /// Sells the lines of the preview (FR-3 item 2): the price is taken first, the lines are removed, the income is credited in the item "Sales", and the
        /// game's window is refreshed. False when nothing was sold (nothing to sell, the numbers changed, the ledger is not there).
        /// </summary>
        public bool SellLines(LineSaleQuote quote)
        {
            ICompany company = PlayerFinanceAdapter.PlayerCompany();
            if (company == null || quote.Wafer == null || quote.Lines <= 0 || !CapitalLedger.IsAvailable) return false;

            LineSaleQuote fresh = QuoteLines(quote.Wafer, quote.Lines);
            if (fresh.BlockReason != null || fresh.Lines != quote.Lines) return false;

            float revenue = GameProductionMath.SaleCost(fresh.Lines, quote.Wafer);   // before the removal, as the game does
            company.RemoveProductionLines(fresh.Lines, quote.Wafer);
            CapitalLedger.Revenue(RecoveryApi.FeatureId, revenue, GameRevenueOrigin.Sales, "lines.sell",
                fresh.Lines.ToString(System.Globalization.CultureInfo.InvariantCulture) + " x " + quote.Wafer.NormalName);

            RefreshLinesWindow();
            return true;
        }

        // ---- the warehouse and the CPUs ----

        /// <summary>
        /// The player's CPUs that are on sale (released and not retired), the least profitable first (FR-4 items 3 and 4): by the margin of a day,
        /// <c>(price − real unit cost) × sold yesterday</c>, ascending.
        /// </summary>
        public IReadOnlyList<CpuAssetView> ListCpus()
        {
            var views = new List<CpuAssetView>();
            GamePlayer player = GamePlayer.Instance;
            if (player == null) return views;

            List<GameCpu> cpus = player.GetCpus();
            if (cpus == null) return views;

            foreach (GameCpu cpu in cpus)
            {
                if (cpu == null || !cpu.IsReleased || cpu.IsRetired) continue;
                views.Add(ViewOf(cpu));
            }

            // a stable order: equal margins keep the order of the game's list
            var ordered = new List<CpuAssetView>(views.Count);
            var keys = new List<KeyValuePair<double, int>>(views.Count);
            for (int i = 0; i < views.Count; i++) keys.Add(new KeyValuePair<double, int>(views[i].DailyMargin, i));
            keys.Sort((a, b) =>
            {
                int byMargin = a.Key.CompareTo(b.Key);
                return byMargin != 0 ? byMargin : a.Value.CompareTo(b.Value);
            });
            foreach (KeyValuePair<double, int> key in keys) ordered.Add(views[key.Value]);
            return ordered;
        }

        /// <summary>The preview of selling the stock of a CPU (FR-4 item 1).</summary>
        public StockQuote QuoteStock(GameCpu cpu)
        {
            var quote = new StockQuote();
            quote.Cpu = cpu;
            quote.BlockReason = "rec.cpu.noStock";
            ICompany company = PlayerFinanceAdapter.PlayerCompany();
            if (cpu == null || cpu.Market == null || company == null) return quote;

            quote.Stock = cpu.Market.Stock;
            quote.Income = quote.Stock > 0 ? GameStorageMath.Income(cpu) : 0.0;
            quote.BalanceAfter = company.MoneyAmount + quote.Income;
            quote.BlockReason = quote.Stock > 0 ? null : "rec.cpu.noStock";
            return quote;
        }

        /// <summary>Sells the stock of a CPU at the game's price (FR-4 item 1). False when there was nothing to sell.</summary>
        public bool SellStock(GameCpu cpu)
        {
            StockQuote quote = QuoteStock(cpu);
            if (quote.BlockReason != null || !_env.Finance.IsAvailable) return false;

            GameStorageMath.SellStock(cpu);   // credits the income itself, in the item "Sales"
            _env.Journal.Record("stock.sell", quote.Income, cpu.Name);
            return true;
        }

        /// <summary>The preview of taking a CPU out of production (FR-4 item 2): the fine of its contracts and the income of its stock.</summary>
        public RetireQuote QuoteRetire(GameCpu cpu)
        {
            var quote = new RetireQuote();
            quote.Cpu = cpu;
            ICompany company = PlayerFinanceAdapter.PlayerCompany();
            if (cpu == null || cpu.Market == null || company == null) return quote;

            quote.Fine = GameContractHelper.FinesFromContracts(cpu);
            quote.StockIncome = cpu.Market.Stock > 0 ? GameStorageMath.Income(cpu) : 0.0;
            quote.Net = quote.StockIncome - quote.Fine;
            quote.BalanceAfter = company.MoneyAmount + quote.Net;
            return quote;
        }

        /// <summary>
        /// Takes a CPU out of production (FR-4 item 2): the game breaks its contracts (the fine is an expense of the report) and sells its stock. The journal
        /// gets the change the balance really had. False when the CPU is gone or already retired.
        /// </summary>
        public bool Retire(GameCpu cpu)
        {
            if (cpu == null || cpu.IsRetired || !_env.Finance.IsAvailable) return false;

            double before = _env.Finance.Money;
            cpu.TriggerRetirement();
            _env.Journal.Record("cpu.retire", _env.Finance.Money - before, cpu.Name);
            return true;
        }

        // ---- the whole production ----

        /// <summary>The value of the manufacturing division and whether it can be sold (FR-5 item 2): the same conditions as the game's button.</summary>
        public DivisionQuote QuoteDivision()
        {
            var quote = new DivisionQuote();
            ICompany company = PlayerFinanceAdapter.PlayerCompany();
            GamePlayer player = GamePlayer.Instance;
            if (company == null || player == null || company.Factory == null)
            {
                quote.BlockReason = AssetMath.ReasonNoProduction;
                return quote;
            }

            quote.Value = GameProductionMath.ManufacturingDivisionValue(company);
            quote.BlockReason = AssetMath.DivisionBlock(company.IsFoundry, company.Factory.ProductionCapacity, player.DifficultyLevel);
            return quote;
        }

        /// <summary>
        /// Opens the game's window of the production lines (FR-5 item 1); the sale of the whole production goes through its own confirmation there.
        /// False when the sale is closed or the window is not in the scene.
        /// </summary>
        public bool OpenManageLines()
        {
            if (QuoteDivision().BlockReason != null) return false;
            GameManageLines window = GameManageLines.Instance;
            if (window == null) return false;

            window.gameObject.SetActive(true);
            return true;
        }

        // ---- internals ----

        private int MinLinesLeft
        {
            get { return _env.Balance.assets.minLinesLeft; }
        }

        private static CpuAssetView ViewOf(GameCpu cpu)
        {
            var view = new CpuAssetView();
            view.Cpu = cpu;
            view.Name = cpu.Name;
            view.Stock = cpu.Market == null ? 0 : cpu.Market.Stock;
            view.StockIncome = view.Stock > 0 ? GameStorageMath.Income(cpu) : 0.0;
            view.Fine = GameContractHelper.FinesFromContracts(cpu);

            // The real unit cost needs the manufacturer; one CPU that cannot answer must not hide the others.
            try
            {
                view.DailyMargin = AssetMath.DailyMargin(cpu.Price, cpu.RealUnitCost, cpu.SoldLastDay);
            }
            catch (Exception)
            {
                view.DailyMargin = 0.0;
            }
            return view;
        }

        // The game's window shows the lines it read when it opened; this makes it read them again.
        private static void RefreshLinesWindow()
        {
            try
            {
                GameManageLines window = GameManageLines.Instance;
                if (window != null) window.UpdateWafers();
            }
            catch (Exception e)
            {
                ModLog.Warn(RecoveryApi.FeatureId, "assets: the window of the production lines could not be refreshed: " + e.Message);
            }
        }
    }
}
