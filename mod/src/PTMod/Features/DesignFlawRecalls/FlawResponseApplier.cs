using System;
using System.Collections.Generic;
using PTMod.Core;
using GameCompany = ProcessorTycoon.CompanySystem.ICompany;
using GameContract = ProcessorTycoon.ContractSystem.Contract;
using GameContractHelper = ProcessorTycoon.ContractSystem.ContractHelper;
using GameContractManager = ProcessorTycoon.ContractSystem.ContractManager;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameDataFinder = ProcessorTycoon.DataFinder;
using GameExpenseOrigin = ProcessorTycoon.ExpenseOrigin;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The reactions to a flaw on the game's side (FR-6..FR-10, design.md Component 10): the money (<c>ICompany.SpendMoney</c>), the reputation
    /// (<c>ModReputation.Add</c>), the popularity of the CPUs (<c>CpuBase.Popularities</c>) and the contracts (<c>ContractManager.RemoveContract</c>). The rules are in
    /// the pure <see cref="FlawResponseEngine"/>; this class is its <see cref="IFlawResponseHost"/> and the entry the effect handlers, the monitor and the journal
    /// call. Nothing here patches the game.
    /// </summary>
    internal sealed class FlawResponseApplier : IFlawResponseHost
    {
        private readonly DesignFlawRecallsFeature _feature;
        private readonly FlawResponseEngine _engine;

        public FlawResponseApplier(DesignFlawRecallsFeature feature, FlawChannels channels)
        {
            _feature = feature;
            _engine = new FlawResponseEngine(channels.Book, this);
        }

        /// <summary>The pure engine (the dispatcher applies the default choice through it).</summary>
        internal FlawResponseEngine Engine
        {
            get { return _engine; }
        }

        // ---- the entries of the other parts of the feature ----

        /// <summary>The owner's answer to a flaw that waits for it: a recall, a revision, a patch or an ignore. <c>false</c> when the record does not wait or the answer is not possible.</summary>
        internal bool Apply(FlawRecord record, FlawResponse response, bool automatic)
        {
            return _engine.Apply(record, response, automatic);
        }

        /// <summary>A hidden flaw was found: the brand pays at once when it became public.</summary>
        internal void OnDiscovered(FlawRecord record)
        {
            _engine.OnDiscovered(record);
        }

        /// <summary>A cover-up leaked: the claims of a quiet revision or the consequences of an ignore, the reputation with the cover-up multiplier.</summary>
        internal void OnLeak(FlawRecord record)
        {
            _engine.OnLeak(record);
        }

        /// <summary>The revision is finished: residual claims of a public one, the entries of a waiting patch are taken away.</summary>
        internal void CompleteStepping(FlawRecord record)
        {
            _engine.CompleteStepping(record);
        }

        /// <summary>The term of the patch is over.</summary>
        internal void ExpireMitigation(FlawRecord record)
        {
            _engine.ExpireMitigation(record);
        }

        /// <summary>The player pays for a revision that replaces the patch (the button of the journal, FR-8 item 5).</summary>
        internal bool FundSteppingForMitigated(FlawRecord record)
        {
            return _engine.FundSteppingForMitigated(record);
        }

        /// <summary>One month of the drain of the reputation of a publicly ignored flaw.</summary>
        internal void IgnoreDrain(FlawRecord record)
        {
            _engine.IgnoreDrain(record);
        }

        /// <summary>The class action of a publicly ignored flaw.</summary>
        internal void ClassActionFiled(FlawRecord record)
        {
            _engine.ClassActionFiled(record);
        }

        // ---- IFlawResponseHost ----

        public int Today
        {
            get { return FlawDays.FromDate(ModTicks.Today); }
        }

        public FlawBalance Balance
        {
            get { return _feature.Balance; }
        }

        public IModRng Rng
        {
            get { return ModRandom.Stream(DesignFlawRecallsFeature.FeatureId + "/response"); }
        }

        public void Spend(FlawRecord record, float amount, FlawSpendKind kind)
        {
            GameCompany owner = ModCompanies.Resolve(record.CompanyKey);
            if (owner == null) throw new InvalidOperationException("the owner " + record.CompanyKey + " of " + record + " is not in the world");

            owner.SpendMoney(amount, kind == FlawSpendKind.Fines ? GameExpenseOrigin.Fines : GameExpenseOrigin.Development);
        }

        public void AddReputation(FlawRecord record, int delta, string reasonKey)
        {
            ApplyReputation(record, delta, reasonKey);
        }

        public void HitPopularity(FlawRecord record, float hit)
        {
            ApplyPopularityHit(record, hit);
        }

        /// <summary>
        /// <c>ModReputation.Add(owner, delta, reasonKey)</c> (FR-10 items 1, 2, 4): the reason is the key of the table of the feature (<c>dfr.rep.*</c>), the id of the
        /// record goes to the log, not into the reason. Written whether <c>press-reviews</c> is on or off; the drift to 50 is the core's.
        /// </summary>
        internal void ApplyReputation(FlawRecord record, int delta, string reasonKey)
        {
            GameCompany owner = ModCompanies.Resolve(record.CompanyKey);
            if (owner == null) return;

            ModReputation.Add(owner, delta, reasonKey);
            ModLog.Info(DesignFlawRecallsFeature.FeatureId, "reputation " + delta + " for " + record.CompanyKey + " (" + reasonKey + ", flaw #" + record.Id + ")");
        }

        /// <summary>
        /// Multiplies <c>CpuBase.Popularities</c> of every CPU of the record by <c>(1 - hit)</c>, never below 0 (FR-10 item 3). The index of the list is the index of the
        /// segment (<c>(int)Segment</c>: 0 HighEnd, 1 MidRange, 2 LowEnd, 3 Industries). The game lifts a value under 0.05 back to 0.05 the next day for a CPU that is not old.
        /// </summary>
        internal void ApplyPopularityHit(FlawRecord record, float hit)
        {
            if (record.Data.cpuIds == null) return;

            foreach (int cpuId in record.Data.cpuIds)
            {
                GameCpu cpu = GameDataFinder.FindCpu(cpuId);
                if (cpu != null) FlawPopularity.Apply(cpu.Popularities, hit);
            }
        }

        public int CancelContracts(FlawRecord record, float chance)
        {
            GameContractManager manager = GameContractManager.Instance;
            if (manager == null || record.Data.cpuIds == null || !(chance > 0f)) return 0;

            IModRng rng = Rng;
            int ended = 0;
            foreach (int cpuId in record.Data.cpuIds)
            {
                GameCpu cpu = GameDataFinder.FindCpu(cpuId);
                if (cpu == null) continue;

                // ContractsWithCpu returns a new list of the active contracts, so removing from the manager while walking it is safe.
                List<GameContract> contracts = GameContractHelper.ContractsWithCpu(cpu);
                foreach (GameContract contract in contracts)
                {
                    if (!rng.Chance(chance)) continue;

                    manager.RemoveContract(contract);
                    ended++;
                }
            }
            return ended;
        }

        public void StateChanged(FlawRecord record, FlawState previous)
        {
            FlawViews.Announce(record, Today);
        }

        public void Log(string line)
        {
            ModLog.Info(DesignFlawRecallsFeature.FeatureId, line);
        }

        public void Warn(string line)
        {
            ModLog.Warn(DesignFlawRecallsFeature.FeatureId, line);
        }

        public void Error(string line, Exception exception)
        {
            ModLog.Error(DesignFlawRecallsFeature.FeatureId, line, exception);
        }
    }
}
