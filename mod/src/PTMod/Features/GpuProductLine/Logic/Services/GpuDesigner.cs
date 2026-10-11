using System;
using System.Collections.Generic;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The designer without its window (design.md, "GpuDesignMath - модель конструктора"; FR-3, FR-4): it fills <see cref="GpuDesignContext"/> from the content and from
    /// the game for the player's company and evaluates the design with <see cref="GpuDesignMath.Evaluate"/>. The project (<see cref="GpuProjects"/>) and the tab of the
    /// designer (TSK-024) ask the same method, so the price the player sees is the price the project gets. From the game: the transistors
    /// (<c>CpuMath.TransistorCount</c>, F1), the clock of the company (F3), the wafer and the yield of the node (F10), the cost of a die (<c>CpuMath.RealUnitCost</c>,
    /// F11, so a shock of the wafer material reaches it), the development modifier <c>Modifiers.GetModifier(DevelopmentCost)</c> (F13, so the fabless discount and
    /// <c>development.cost</c> act; <c>CpuMath.ProjectCost</c> is not asked and <c>development.cost.design</c> does not apply), and the channels
    /// <c>production.unitCost</c> and <c>gpu.vramCost</c> (F12). The co-development discount (F27, <see cref="CoDevFor"/>) is in the context, so the window, the project
    /// and the price on the button see the same number.
    /// </summary>
    public sealed class GpuDesigner
    {
        private readonly GpuContentDb _content;
        private readonly IGpuGame _game;

        public GpuDesigner(GpuContentDb content, IGpuGame game)
        {
            if (content == null) throw new ArgumentNullException("content");
            if (game == null) throw new ArgumentNullException("game");
            _content = content;
            _game = game;
        }

        private GpuBalanceDto Balance
        {
            get { return _content.Balance ?? new GpuBalanceDto(); }
        }

        /// <summary>The context of the design for the player today; <see cref="GpuDesignContext.DieUnitCost"/> is filled by <see cref="Evaluate"/> (it needs the failure rate of the design).</summary>
        public GpuDesignContext ContextFor(GpuDesignInput input)
        {
            string player = _game.PlayerKey;
            DateTime today = _game.Today;
            var c = new GpuDesignContext
            {
                Arch = _content.Arch(input.ArchId),
                Mem = _content.Mem(input.MemId),
                Cooler = _content.Cooler(input.CoolerId),
                CompanyMaxFreqKHz = _game.MaxFrequencyKHz(player),
                WaferDiameter = _game.WaferDiameter(player),
                DevModifier = _game.Modifier(GpuModifierKind.DevelopmentCost, player),
                CoDevFactor = CoDevFor(player, input.NodeName, today.Year),
                UnitCostMul = _game.Channels.UnitCost(player),
                VramCostMul = _game.Channels.VramCost(player),
                Year = today.Year,
                MaxDieMm2 = _content.MaxDie(today.Year),
            };

            NodeSpec node;
            if (!string.IsNullOrEmpty(input.NodeName) && _game.TryGetNode(input.NodeName, out node))
            {
                c.Node = node;
                c.Transistors = _game.Transistors(player, node, input.DieMm2);
                c.Yield = YieldOf(player, node);
            }
            return c;
        }

        /// <summary>
        /// F27, FR-17: the factor of the cost of the project on <paramref name="nodeName"/> - <c>synergy.coDevFactor</c> (0.85) when the company has a released CPU of
        /// <c>synergy.coDevYears</c> (2) years ago or newer on the same process node, otherwise 1. The CPUs are read from the company when the designer asks, once per
        /// recount (a change of a choice in the window, the start of a project): there is no subscription to the release of a CPU.
        /// </summary>
        public float CoDevFor(string companyKey, string nodeName, int year)
        {
            if (string.IsNullOrEmpty(nodeName)) return 1f;
            GpuBalanceDto.SynergyBlock rules = Balance.synergy;
            if (rules == null || !(rules.coDevFactor > 0f) || rules.coDevFactor >= 1f) return 1f;
            return GpuDesignMath.CoDevFactor(_game.CpusOf(companyKey), nodeName, year, rules.coDevYears, rules.coDevFactor);
        }

        /// <summary>
        /// Everything the designer shows for the input (F1-F13). Two passes: the first gives the failure rate and the dies per wafer, the second puts the cost of a die
        /// (<c>CpuMath.RealUnitCost</c>, a markup of <c>ProductionMath.BaseMarkupPrice</c> for a fabless company) into the unit cost.
        /// </summary>
        public GpuStats Evaluate(GpuDesignInput input)
        {
            GpuDesignContext context = ContextFor(input);
            return Evaluate(input, context);
        }

        /// <summary>The same with a context the caller already has (the tab keeps it while only the sliders move).</summary>
        public GpuStats Evaluate(GpuDesignInput input, GpuDesignContext context)
        {
            GpuBalanceDto balance = Balance;
            GpuStats first = GpuDesignMath.Evaluate(input, context, balance);
            if (context.Arch == null || first.DpwYield < 1) return first;

            bool fabless = _game.IsFabless(_game.PlayerKey);
            context.DieUnitCost = _game.DieUnitCost(_game.PlayerKey, input.DieMm2, context.Arch.dieMaterialCost, first.DpwYield, first.FailureRate, fabless,
                fabless ? FablessMarkup : 1f);
            return GpuDesignMath.Evaluate(input, context, balance);
        }

        /// <summary><c>ProductionMath.BaseMarkupPrice</c>: the markup of a die a foundry makes for a fabless company (F11, F18).</summary>
        public const float FablessMarkup = 2.0f;

        /// <summary>
        /// The snapshot of a design that lives in the design and in its products (D14): the characteristics of <paramref name="stats"/> and the raw costs of a card -
        /// the material of a die (<c>arch.dieMaterialCost</c>) and the video memory, board and cooler of the year without the channels (<c>perGoodExtras</c>);
        /// production multiplies them by the channels of the day.
        /// </summary>
        public GpuStatsSave Snapshot(GpuDesignInput input, GpuStats stats)
        {
            GpuBalanceDto.DesignBlock rules = Balance.design ?? new GpuBalanceDto.DesignBlock();
            GpuArchitectureDto arch = _content.Arch(input.ArchId);
            GpuMemoryDto mem = _content.Mem(input.MemId);
            GpuCoolerDto cooler = _content.Cooler(input.CoolerId);
            float extras = 0f;
            if (mem != null && cooler != null)
            {
                extras = GpuDesignMath.PerGoodExtras(input.VramMB, GpuDesignMath.MemoryCostPerMB(mem, _game.Today.Year), 1f, input.BusWidthBits, rules.boardBase,
                    rules.boardCostPerBit, cooler.cost, 1f);
            }
            return new GpuStatsSave
            {
                transistors = stats.Transistors,
                computeMops = stats.ComputeMops,
                effectivePerf = stats.EffectivePerf,
                units = stats.Units,
                maxClockMHz = stats.MaxClockMHz,
                bandwidthMBs = stats.BandwidthMBs,
                bandwidthRatio = stats.BandwidthRatio,
                powerW = stats.PowerW,
                tempC = stats.TempC,
                failureRate = stats.FailureRate,
                dieMaterial = arch != null ? arch.dieMaterialCost : 0f,
                perGoodExtras = extras,
                projectCost = stats.ProjectCost,
                projectDays = stats.ProjectDays,
                features = stats.Features != null ? new List<string>(stats.Features) : new List<string>(),
            };
        }

        /// <summary>
        /// F10: the yield of the node for the player: <c>ICompany.CurrentNodeYield</c> on an own factory; for a fabless company
        /// <c>Lerp(MinimumYieldRate, MaximumYieldRate, production.fablessYieldProgress)</c>.
        /// </summary>
        public float YieldOf(string companyKey, NodeSpec node)
        {
            if (_game.IsFabless(companyKey)) return GpuProductionMath.FablessYield(node.MinYield, node.MaxYield, Balance.production.fablessYieldProgress);
            return _game.NodeYield(companyKey, node);
        }
    }
}
