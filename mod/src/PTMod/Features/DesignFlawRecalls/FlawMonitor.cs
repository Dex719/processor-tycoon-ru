using System;
using System.Collections.Generic;
using PTMod.Core;
using GameBankruptcyHandler = ProcessorTycoon.Bank.BankruptcyHandler;
using GameCompany = ProcessorTycoon.CompanySystem.ICompany;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameDataFinder = ProcessorTycoon.DataFinder;
using GameExpenseOrigin = ProcessorTycoon.ExpenseOrigin;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The monthly and daily monitor (FR-4, FR-7, FR-8, FR-9; design.md Component 6): the game side of <see cref="FlawCycle"/>. It is subscribed to
    /// <c>TickPhase.MonthPassed</c> and <c>TickPhase.DayPassed</c> (<c>ctx.OnTick</c>), to the bankruptcy of companies (once per scene, from
    /// <c>ModTicks.GameReady</c>) and to the switching on of the feature. It does not look at <c>IsBatchRun</c> and does not patch
    /// <c>DateController.NextMonth</c>: a batch run calls the ticks day by day and the core holds the mail back (FR-4 item 7).
    /// <para>
    /// It implements <see cref="IFlawCycleHost"/>: the clock, the facts about the CPUs, the owner's state and the money of a revision, and it hands the
    /// consequences to the parts that own them: <see cref="FlawResponseApplier"/> (the reputation, the claims, the drain, the lawsuit), <see cref="FlawDecisionDispatcher"/>
    /// (the window of the player, and the answer of an AI company by its policy), <see cref="FlawChannels"/> (the entries of a closed record).
    /// </para>
    /// </summary>
    internal sealed class FlawMonitor : IFlawCycleHost
    {
        private readonly DesignFlawRecallsFeature _feature;
        private readonly FlawCycle _cycle;
        private readonly Action<GameCompany> _onBankruptcy;

        private Dictionary<int, GameCpu> _cpuIndex;

        public FlawMonitor(DesignFlawRecallsFeature feature)
        {
            _feature = feature;
            _cycle = new FlawCycle(feature.Store, this);
            _onBankruptcy = OnCompanyWentBankrupt;
        }

        /// <summary>The cycle (the public API reveals a flaw through it).</summary>
        internal FlawCycle Cycle
        {
            get { return _cycle; }
        }

        // ---- the subscriptions ----

        /// <summary>
        /// <c>TickPhase.MonthPassed</c>: the first of a month. A failing record is handled inside the cycle (it is skipped, three failures in a row close it); the cycle and the sweep
        /// of the decision windows are guarded each on its own, so a failure of the one never stops the other (NFR-2, FR-15).
        /// </summary>
        internal void OnMonth()
        {
            ModHooks.Guard(DesignFlawRecallsFeature.FeatureId, "monitor/month", _cycle.OnMonth);
            _feature.Dispatcher.Sweep();
        }

        /// <summary><c>TickPhase.DayPassed</c>: the revisions are paid, the revisions and patches whose term has come are ended. Guarded: a failure is counted against the feature, not the tick.</summary>
        internal void OnDay()
        {
            ModHooks.Guard(DesignFlawRecallsFeature.FeatureId, "monitor/day", _cycle.OnDay);
        }

        /// <summary>
        /// The game scene is ready (<c>ModTicks.GameReady</c>) or the feature was switched on in a running scene: subscribes to the bankruptcies of this
        /// scene's handler (the game drops its events with the scene) and, when asked, moves the revisions by the length of the pause.
        /// </summary>
        internal void OnSceneReady(bool resume)
        {
            AttachBankruptcy();
            if (resume) _cycle.OnResume();
        }

        private void AttachBankruptcy()
        {
            GameBankruptcyHandler handler = GameBankruptcyHandler.Instance;
            if (handler == null) return;

            handler.OnCompanyWentBankrupt -= _onBankruptcy;
            handler.OnCompanyWentBankrupt += _onBankruptcy;
        }

        /// <summary><c>BankruptcyHandler.OnCompanyWentBankrupt</c>: the records of the company are closed without consequences (FR-4 item 5).</summary>
        private void OnCompanyWentBankrupt(GameCompany company)
        {
            ModHooks.Guard(DesignFlawRecallsFeature.FeatureId, "monitor/bankruptcy", () =>
            {
                if (!ModFeatures.IsActive(DesignFlawRecallsFeature.FeatureId)) return;   // the next monthly pass closes them: the owner is not alive then

                string key = ModCompanies.KeyOf(company);
                if (key != null) _cycle.CloseCompany(key, "the owner went bankrupt");
            });
        }

        // ---- IFlawCycleHost: the clock and the numbers ----

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
            get { return ModRandom.Stream(DesignFlawRecallsFeature.FeatureId + "/monitor"); }
        }

        public void BeginPass()
        {
            // One scan of the companies for the whole pass instead of one per record (the lookup by SaveID walks every company).
            var index = new Dictionary<int, GameCpu>();
            List<GameCpu> all = GameDataFinder.FindAllCpus();
            if (all != null)
            {
                foreach (GameCpu cpu in all)
                {
                    if (cpu != null) index[cpu.SaveID] = cpu;
                }
            }
            _cpuIndex = index;
        }

        public void EndPass()
        {
            _cpuIndex = null;
        }

        // ---- IFlawCycleHost: the game ----

        public bool OwnerIsAlive(FlawRecord record)
        {
            GameCompany owner = ModCompanies.Resolve(record.CompanyKey);
            return owner != null && !owner.IsBankrupt;
        }

        public FlawFacts ReadFacts(FlawRecord record, bool forQuote)
        {
            GameCompany owner = ModCompanies.Resolve(record.CompanyKey);
            return GameCpuView.ReadFacts(record, FindCpu, owner, forQuote, Balance.Discovery.ExpireNoSalesMonths);
        }

        private GameCpu FindCpu(int saveId)
        {
            if (_cpuIndex == null) return GameDataFinder.FindCpu(saveId);

            GameCpu cpu;
            return _cpuIndex.TryGetValue(saveId, out cpu) ? cpu : null;
        }

        public void ChargeStepping(FlawRecord record, float amount)
        {
            if (amount <= 0f) return;

            GameCompany owner = ModCompanies.Resolve(record.CompanyKey);
            if (owner != null) owner.SpendMoney(amount, GameExpenseOrigin.Development);
        }

        // ---- IFlawCycleHost: the consequences ----

        public void Discovered(FlawRecord record)
        {
            // The brand pays at once for a flaw that became public (FR-10 item 1); then the owner is asked: the player by the window of event-system
            // (FR-5), an AI company by its policy at once (FR-11: FlawDecisionDispatcher.DispatchAi, the news for the player included).
            ModHooks.Guard(DesignFlawRecallsFeature.FeatureId, "monitor/discovered", () => _feature.Applier.OnDiscovered(record));
            _feature.Dispatcher.Dispatch(record);
        }

        public void Leaked(FlawRecord record)
        {
            // The claims of a quiet revision or the consequences of the ignore, the reputation with the cover-up multiplier; then a new choice without
            // "ignore" (FR-7 item 5, FR-9 item 5).
            ModHooks.Guard(DesignFlawRecallsFeature.FeatureId, "monitor/leaked", () => _feature.Applier.OnLeak(record));
            _feature.Dispatcher.Dispatch(record);
        }

        public void SteppingCompleted(FlawRecord record)
        {
            ModHooks.Guard(DesignFlawRecallsFeature.FeatureId, "monitor/steppingCompleted", () => _feature.Applier.CompleteStepping(record));
        }

        public void MitigationExpired(FlawRecord record)
        {
            ModHooks.Guard(DesignFlawRecallsFeature.FeatureId, "monitor/mitigationExpired", () => _feature.Applier.ExpireMitigation(record));
        }

        public void IgnoreDrain(FlawRecord record)
        {
            ModHooks.Guard(DesignFlawRecallsFeature.FeatureId, "monitor/ignoreDrain", () => _feature.Applier.IgnoreDrain(record));
        }

        public void ClassActionFiled(FlawRecord record)
        {
            ModHooks.Guard(DesignFlawRecallsFeature.FeatureId, "monitor/classAction", () => _feature.Applier.ClassActionFiled(record));
        }

        public void StateChanged(FlawRecord record, FlawState previous)
        {
            // A closed record (the owner is bankrupt, the CPU is gone) gives up its entries of ModModifiers and its place in the queue of decisions.
            if (record.State == FlawState.Closed)
            {
                ModHooks.Guard(DesignFlawRecallsFeature.FeatureId, "monitor/closed", () => _feature.Channels.RemoveAllOfRecord(record));
                _feature.Dispatcher.Forget(record);
            }

            FlawViews.Announce(record, Today);
        }

        public void Log(string message)
        {
            ModLog.Info(DesignFlawRecallsFeature.FeatureId, message);
        }

        public void Error(string message, Exception exception)
        {
            ModLog.Error(DesignFlawRecallsFeature.FeatureId, message, exception);
        }
    }
}
