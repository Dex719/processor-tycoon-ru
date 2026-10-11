using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using PTMod.Features.BankruptcyRecovery;
using GameAiCompany = ProcessorTycoon.CompanySystem.AICompany;
using GameCompanyTypes = ProcessorTycoon.CompanySystem.CompanyTypeUpdater;
using GameDataFinder = ProcessorTycoon.DataFinder;
using GameNodeOptimization = ProcessorTycoon.Hardware.NodeOptimization;
using GamePlayer = ProcessorTycoon.Player;
using GameProcessNode = ProcessorTycoon.Hardware.ProcessNode;
using GameProductionMath = ProcessorTycoon.Production.Math.ProductionMath;
using GameResearch = ProcessorTycoon.ResearchSystem.ResearchSector;
using GameResearchData = ProcessorTycoon.ResearchSystem.ResearchDataProvider;
using GameTechnology = ProcessorTycoon.ResearchSystem.Technology;
using GameWafer = ProcessorTycoon.Hardware.WaferSize;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;
using RevenueOrigin = ProcessorTycoon.RevenueOrigin;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// <c>AcquisitionExecutor</c> (design.md "AcquisitionExecutor"; TSK-010…TSK-012): the game side of a purchase of a whole company. The order of the
    /// steps, the partial deals and the rule "the payment last" are <see cref="DealExecution"/>'s; this class does each step on the game objects
    /// (<see cref="IDealWorld"/>). One object per deal: it holds the target, the player and what the steps found on the way.
    /// <list type="bullet">
    /// <item><b>This file</b> (part 1): the entry point, the preparation (fresh snapshots, quote and plan right before the deal), the recheck of the
    /// target, and the steps 3 (technologies without pop-ups), 4 (process-node experience) and 6 (lines: moved to the same wafer size, or sold for
    /// their sale cost; <c>CompanyTypeUpdater.TriggerUpdate(true)</c> at the end).</item>
    /// <item><c>AcquisitionExecutor.Cpus.cs</c> (part 2): the steps 5, 7 and 8 - CPU families, production links, contracts, the foundry flag.</item>
    /// <item><c>AcquisitionExecutor.Finish.cs</c> (part 3): the steps 9 and 11-13 - the target out, the payment, the events, the mail.</item>
    /// </list>
    /// Every step runs under <c>ModHooks.Guard</c> (passed to the pipeline); a step that cannot do all it should says so in the <see cref="DealLog"/>
    /// and the deal is recorded as partial. The <c>AICompany</c> object of the target, its <c>UniqueID</c>, <c>SaveID</c> and <c>Name</c> are never
    /// changed (D1). Main thread only; one deal at a time.
    /// </summary>
    internal sealed partial class AcquisitionExecutor : IDealWorld
    {
        private const string Feature = RivalAcquisitionsFeature.FeatureId;
        private const string SiteExecute = "AcquisitionExecutor.Execute";

        private static bool _busy;

        private readonly RivalAcquisitionsFeature _feature;
        private GameAiCompany _target;
        private GamePlayer _owner;
        private ICompany _player;
        private DealRequest _request;

        private AcquisitionExecutor(RivalAcquisitionsFeature feature)
        {
            _feature = feature;
        }

        /// <summary>A deal is being carried out now (the window keeps its buttons inactive meanwhile).</summary>
        internal static bool IsBusy
        {
            get { return _busy; }
        }

        // ---- the entry point ----

        /// <summary>
        /// Carries out a purchase of the AI company <paramref name="uniqueId"/> / <paramref name="saveId"/> the player confirmed: builds the snapshots,
        /// the quote and the plan anew (the contract ids of a plan are only valid in the call that built it), checks them against what the window
        /// showed (<paramref name="shownPrice"/>, <paramref name="expectedStatus"/>; 0 / null: not checked) and runs the steps. Never throws; a
        /// cancelled deal changes nothing and says why (<see cref="DealOutcome.CancelReasonKey"/>).
        /// </summary>
        internal static DealOutcome Execute(DealType type, int uniqueId, int saveId, PlanOptions options, double shownPrice = 0.0, CompanyStatus? expectedStatus = null)
        {
            RivalAcquisitionsFeature feature = RivalAcquisitionsFeature.Instance;
            if (feature == null || !ModFeatures.IsActive(Feature)) return Cancelled(AcqKeys.ReasonUnsupported);
            if (_busy) return Cancelled(AcqKeys.ReasonBusy);

            DealOutcome outcome = null;
            _busy = true;
            try
            {
                ModHooks.Guard(Feature, SiteExecute, () =>
                {
                    var executor = new AcquisitionExecutor(feature);
                    string reason;
                    DealRequest request = executor.Prepare(type, uniqueId, saveId, options, shownPrice, expectedStatus, out reason);
                    outcome = request == null
                        ? Cancelled(reason)
                        : DealExecution.Run(executor, request, feature.State, (site, body) => ModHooks.Guard(Feature, site, body));
                    executor.Report(type, outcome);
                });
            }
            finally
            {
                _busy = false;
                if (feature.ExecutingTarget != null) feature.ExecutingTarget = null;
            }
            return outcome ?? Cancelled(AcqKeys.ReasonUnsupported);
        }

        private static DealOutcome Cancelled(string reason)
        {
            return new DealOutcome { Executed = false, CancelReasonKey = reason ?? AcqKeys.ReasonUnsupported };
        }

        /// <summary>The fresh request of the deal, or null with the reason (the target is not the one the player chose, no player, no snapshot).</summary>
        private DealRequest Prepare(DealType type, int uniqueId, int saveId, PlanOptions options, double shownPrice, CompanyStatus? expectedStatus, out string reason)
        {
            reason = null;
            _target = GameDataFinder.FindCompany(saveId) as GameAiCompany;
            if (_target == null || _target.UniqueID != uniqueId)
            {
                reason = AcqKeys.ReasonTargetChanged;
                return null;
            }

            _owner = GamePlayer.Instance;
            _player = _owner != null ? _owner.Company : null;
            PlayerSnapshot p = GameSnapshotAdapter.Player();
            if (_player == null || p == null)
            {
                reason = AcqKeys.ReasonUnsupported;
                return null;
            }

            AcquisitionsBalance b = _feature.Balance;
            AcquisitionsState s = _feature.State;
            CompanySnapshot t = GameSnapshotAdapter.Of(_target, p, s, b.Valuation.CpuIncomeMonths);
            double channel = GameSnapshotAdapter.PriceChannelOf(_target);
            DealQuote quote = DealRules.Quote(type, t, p, s, b, channel);
            AcquisitionPlan plan = AcquisitionPlanBuilder.Build(quote, t, p, options ?? new PlanOptions());
            ResolveContracts(t);

            _request = new DealRequest
            {
                Type = type,
                Quote = quote,
                Plan = plan,
                Target = t,
                Player = p,
                ExpectedUniqueId = uniqueId,
                ExpectedSaveId = saveId,
                ExpectedStatus = expectedStatus,
                ShownPrice = shownPrice,
                Today = p.Today,
            };

            ModLog.Debug(Feature, "deal: " + type + " " + Describe(t) + ": price " + Money(quote.Price) + ", cash " + Money(quote.AssumedCash)
                                  + ", technologies " + plan.TechUnlockIds.Count + ", lines " + (plan.KeepLines ? "moved " + plan.LinesMoved : "sold " + plan.LinesSold)
                                  + ", CPUs " + plan.TransferredCpuIds().Count + ", contracts " + plan.RepointProviderContractIds.Count + "+" + plan.RepointClientContractIds.Count
                                  + (plan.CanExecute ? "" : ", blocked: " + string.Join(", ", plan.BlockingErrors.ToArray())));
            return _request;
        }

        // ---- step 1 and the start ----

        public string Recheck(DealRequest request)
        {
            ICompany again = GameDataFinder.FindCompany(request.Target.SaveId);
            if (!ReferenceEquals(again, _target) || _target.UniqueID != request.Target.UniqueId || _target.IsPlayer) return AcqKeys.ReasonTargetChanged;

            GamePlayer owner = GamePlayer.Instance;
            if (owner == null || !ReferenceEquals(owner.Company, _player)) return AcqKeys.ReasonUnsupported;
            if (_player.IsInDefault || _player.IsBankrupt) return AcqKeys.ReasonPlayerDefault;

            // The status the quote was made for is still the status of the company.
            bool bankrupt = _target.IsBankrupt;
            bool fits;
            switch (request.Type)
            {
                case DealType.BankruptAssets: fits = bankrupt; break;
                case DealType.Distressed: fits = !bankrupt && _target.IsInDefault; break;
                case DealType.Hostile: fits = !bankrupt && !_target.IsInDefault; break;
                default: fits = false; break;
            }
            if (!fits) return AcqKeys.ReasonStatusChanged;

            if (!CapitalLedger.IsAvailable) return AcqKeys.ReasonLedgerMissing;
            if (_feature.ExecutingTarget != null) return AcqKeys.ReasonBusy;
            return null;
        }

        public void Begin(DealRequest request)
        {
            _feature.ExecutingTarget = _target;
        }

        public void End()
        {
            _feature.ExecutingTarget = null;
        }

        // ---- step 3: technologies ----

        public void UnlockTechnologies(AcquisitionPlan plan, DealLog log)
        {
            if (plan.TechUnlocks.Count == 0 && plan.TechProgress.Count == 0) return;

            GameResearch research = _player.ResearchSector;
            if (research == null)
            {
                log.Fail("technologies: the player has no research sector");
                return;
            }

            Dictionary<int, GameTechnology> technologies = Technologies();
            bool quiet = GameAccess.SetSuppressPopup(research, true);
            if (!quiet) log.Note("technologies: the research windows could not be suppressed");
            try
            {
                // A pass of Update(true) completes what is unlocked and opens the next level of dependencies (spike TSK-004): the gap is
                // ordered by depth, so one pass per level is enough; the extra passes only catch what the data did not foresee.
                int i = 0;
                while (i < plan.TechUnlocks.Count)
                {
                    int depth = plan.TechUnlocks[i].Depth;
                    for (; i < plan.TechUnlocks.Count && plan.TechUnlocks[i].Depth == depth; i++)
                    {
                        GameResearch.Research r = ResearchOf(research, technologies, plan.TechUnlocks[i].Id);
                        if (r != null && !r.IsResearched) r.Progress = 1f;
                    }
                    research.Update(true);
                }

                int maxPasses = Math.Max(1, _feature.Balance.TechUnlockMaxPasses);
                for (int pass = 0; pass < maxPasses && NotUnlocked(research, technologies, plan).Count > 0; pass++) research.Update(true);

                List<string> missing = NotUnlocked(research, technologies, plan);
                if (missing.Count > 0)
                {
                    log.Fail("technologies: " + missing.Count + " of " + plan.TechUnlocks.Count + " not unlocked (" + string.Join(", ", missing.ToArray()) + ")");
                }

                foreach (TechProgressSnapshot partial in plan.TechProgress)
                {
                    GameResearch.Research r = ResearchOf(research, technologies, partial.Id);
                    if (r == null || r.IsResearched) continue;
                    // Below 1, so a partial progress is never a free technology that the price did not include.
                    float progress = (float)Math.Min(Math.Max(partial.TargetProgress, 0.0), 0.999);
                    if (r.Progress < progress) r.Progress = progress;
                }
            }
            finally
            {
                if (quiet) GameAccess.SetSuppressPopup(research, false);
            }
        }

        private static List<string> NotUnlocked(GameResearch research, Dictionary<int, GameTechnology> technologies, AcquisitionPlan plan)
        {
            var missing = new List<string>();
            foreach (TechSnapshot tech in plan.TechUnlocks)
            {
                GameResearch.Research r = ResearchOf(research, technologies, tech.Id);
                if (r == null || !r.IsResearched) missing.Add(string.IsNullOrEmpty(tech.Name) ? "#" + tech.Id.ToString(CultureInfo.InvariantCulture) : tech.Name);
            }
            return missing;
        }

        private static GameResearch.Research ResearchOf(GameResearch research, Dictionary<int, GameTechnology> technologies, int id)
        {
            GameTechnology tech;
            return technologies.TryGetValue(id, out tech) && tech != null ? research.TechnologyAsResearch(tech) : null;
        }

        private static Dictionary<int, GameTechnology> Technologies()
        {
            var result = new Dictionary<int, GameTechnology>();
            GameResearchData data = GameResearchData.Instance;
            List<GameTechnology> all = data != null ? data.GetAllTechnologies() : GameDataFinder.FindAllTechnologies();
            if (all == null) return result;
            foreach (GameTechnology tech in all)
            {
                if (tech != null) result[tech.ID] = tech;
            }
            return result;
        }

        // ---- step 4: process-node experience ----

        public void RaiseNodeExperience(AcquisitionPlan plan, DealLog log)
        {
            foreach (NodeSnapshot node in plan.NodeRaises)
            {
                GameNodeOptimization theirs = NodeOf(_target, node.NodeId, node.VariantId);
                GameProcessNode processNode = theirs != null ? theirs.ProcessNode : null;
                if (processNode == null)
                {
                    log.Note("node " + node.Name + ": the target has no experience with it any more");
                    continue;
                }
                if (!HasNode(_player, processNode))
                {
                    log.Fail("node " + node.Name + ": the player does not have the process node");
                    continue;
                }

                float gap = theirs.Progress - _player.CurrentNodeLearningProgress(processNode);
                if (gap > 0f) _player.OptimizeNode(processNode, gap);
            }
        }

        private static GameNodeOptimization NodeOf(ICompany company, int nodeId, int variantId)
        {
            if (company.NodeOptimizations == null) return null;
            GameNodeOptimization any = null;
            foreach (GameNodeOptimization optimization in company.NodeOptimizations)
            {
                if (optimization == null || optimization.NodeID != nodeId) continue;
                if (optimization.VariantID == variantId) return optimization;
                if (any == null) any = optimization;
            }
            return any;
        }

        private static bool HasNode(ICompany company, GameProcessNode node)
        {
            if (company.NodeOptimizations == null) return false;
            foreach (GameNodeOptimization optimization in company.NodeOptimizations)
            {
                if (optimization != null && optimization.ProcessNode != null && optimization.ProcessNode.Equals(node)) return true;
            }
            return false;
        }

        // ---- step 6: lines ----

        public void MoveLines(AcquisitionPlan plan, DealLog log)
        {
            if (plan.KeepLines)
            {
                foreach (LineMove move in plan.LineMoves) MoveOne(move, log);
            }
            else
            {
                foreach (LineSale sale in plan.LineSales) SellOne(sale, log);
            }

            // The kinds of the companies (fabless / IDM / foundry) follow the factories: the target without lines, the player with them.
            GameCompanyTypes updater = GameCompanyTypes.Instance;
            if (updater != null) updater.TriggerUpdate(true);
            else log.Note("lines: CompanyTypeUpdater is not in the scene");
        }

        private void MoveOne(LineMove move, DealLog log)
        {
            GameWafer from = WaferOf(_target, move.FromWaferId);
            int lines = LinesToTake(from, move.Lines, move.FromWaferId, log);
            if (lines <= 0) return;

            GameWafer to = WaferOf(_player, move.ToWaferId) ?? LargestKnownUpTo(_player, from.Value);
            if (to == null)
            {
                log.Fail("lines: the player knows no wafer size for the " + Mm(from) + " lines; they stay with the target");
                return;
            }
            if (to.ID != move.ToWaferId) log.Note("lines: " + Mm(from) + " lines go to " + Mm(to) + " (the planned wafer size is not known to the player)");

            _target.RemoveProductionLines(lines, from);
            int before = _player.Factory.AmountOfLines(to);
            _player.ExpandFactory(lines, 0f, to.ID);
            int added = _player.Factory.AmountOfLines(to) - before;
            if (added != lines) log.Fail("lines: the player's factory got " + added + " of " + lines + " lines on " + Mm(to));
        }

        private void SellOne(LineSale sale, DealLog log)
        {
            GameWafer wafer = WaferOf(_target, sale.WaferId);
            int lines = LinesToTake(wafer, sale.Lines, sale.WaferId, log);
            if (lines <= 0) return;

            float income = GameProductionMath.SaleCost(lines, wafer);
            _target.RemoveProductionLines(lines, wafer);
            if (income > 0f) CapitalLedger.Revenue(Feature, income, RevenueOrigin.Sales, "acq.lines.sale", (_target.Name ?? "") + ": " + lines + " x " + Mm(wafer));
        }

        /// <summary>How many lines of <paramref name="wafer"/> the target gives: the planned number, or fewer when it has fewer (a partial step).</summary>
        private int LinesToTake(GameWafer wafer, int planned, int waferId, DealLog log)
        {
            if (wafer == null || _target.Factory == null)
            {
                log.Fail("lines: the target has no wafer size " + waferId.ToString(CultureInfo.InvariantCulture));
                return 0;
            }
            int have = _target.Factory.AmountOfLines(wafer);
            int lines = Math.Min(planned, have);
            if (lines < planned) log.Fail("lines: the target has " + have + " lines on " + Mm(wafer) + ", " + planned + " were planned");
            return lines;
        }

        private static GameWafer WaferOf(ICompany company, int waferId)
        {
            if (company == null || company.WaferSizes == null) return null;
            foreach (GameWafer wafer in company.WaferSizes)
            {
                if (wafer != null && wafer.ID == waferId) return wafer;
            }
            return null;
        }

        /// <summary>The largest wafer size the company knows that is not larger than <paramref name="value"/>; the smallest when all are larger (edge case 2).</summary>
        private static GameWafer LargestKnownUpTo(ICompany company, float value)
        {
            if (company == null || company.WaferSizes == null) return null;
            GameWafer best = null;
            GameWafer smallest = null;
            foreach (GameWafer wafer in company.WaferSizes)
            {
                if (wafer == null) continue;
                if (smallest == null || wafer.Value < smallest.Value) smallest = wafer;
                if (wafer.Value <= value && (best == null || wafer.Value > best.Value)) best = wafer;
            }
            return best ?? smallest;
        }

        // ---- text helpers ----

        private static string Mm(GameWafer wafer)
        {
            return wafer == null ? "?" : wafer.Value.ToString("0.##", CultureInfo.InvariantCulture) + " mm";
        }

        private static string Money(double value)
        {
            return value.ToString("F0", CultureInfo.InvariantCulture);
        }

        private static string Describe(CompanySnapshot t)
        {
            return "'" + t.Name + "' (ai:" + t.UniqueId.ToString(CultureInfo.InvariantCulture) + ", save " + t.SaveId.ToString(CultureInfo.InvariantCulture) + ")";
        }
    }
}
