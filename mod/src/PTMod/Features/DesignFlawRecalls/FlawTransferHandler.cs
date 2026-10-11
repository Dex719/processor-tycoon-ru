using System;
using PTMod.Core;
using GameCompany = ProcessorTycoon.CompanySystem.ICompany;
using GameCpu = ProcessorTycoon.Hardware.Cpu;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// A flaw follows its CPU when a company takes the CPU over (FR-19, design.md Component 15a): the subscriber of <c>ModEvents.CpuTransferred(from, to, cpu)</c>, which
    /// <c>rival-acquisitions</c> raises through the core for the base CPU and its variants, one by one, before the taken-over company leaves the game. The rules are in the pure
    /// <see cref="FlawTransferEngine"/>; this class reads the keys of the companies and the state of the old owner from the game and is the host of the engine: it asks the
    /// cycle for new prices (<see cref="FlawCycle.Requote"/>), the decision engine for the choice of the new owner (<see cref="FlawDecisionEngine.Reoffer"/>), and tells the
    /// public API about the new owner. The body is guarded: an exception never reaches the game. The core skips the handler while the feature is not enabled.
    /// <para>
    /// <c>CpuReleased</c> is not raised for a transfer, so there is no roll here; <c>rolledCpuIds</c> is never written by this class.
    /// </para>
    /// </summary>
    internal sealed class FlawTransferHandler : IFlawTransferHost
    {
        private const string Site = "transfer/cpuTransferred";

        private readonly DesignFlawRecallsFeature _feature;
        private readonly FlawTransferEngine _engine;

        public FlawTransferHandler(DesignFlawRecallsFeature feature)
        {
            _feature = feature;
            _engine = new FlawTransferEngine(feature.Store, feature.Channels.Book, this);
        }

        /// <summary>The pure engine.</summary>
        internal FlawTransferEngine Engine
        {
            get { return _engine; }
        }

        /// <summary>The handler of <c>ModEvents.CpuTransferred(from, to, cpu)</c>.</summary>
        internal void OnCpuTransferred(GameCompany from, GameCompany to, GameCpu cpu)
        {
            if (cpu == null) return;

            int saveId = cpu.SaveID;
            ModHooks.Guard(DesignFlawRecallsFeature.FeatureId, Site, () =>
            {
                try
                {
                    // IsBankrupt of the old owner: the core's contract is "first the transfer, then the company leaves"; a company that is out already means the order was reversed.
                    bool fromBankrupt = from != null && from.IsBankrupt;
                    _engine.OnTransfer(saveId, cpu.Base != null, ModCompanies.KeyOf(from), ModCompanies.KeyOf(to), fromBankrupt);
                }
                catch (Exception e)
                {
                    // Guard counts it against the feature and logs it; the SaveID is the context.
                    throw new InvalidOperationException("transfer of cpu " + saveId + " failed", e);
                }
            });
        }

        // ---- IFlawTransferHost ----

        public void Offer(FlawRecord record)
        {
            // New prices by the new owner's books (the recall is capped by its revenue), then the choice: its window or the policy of the AI company.
            _feature.Monitor.Cycle.Requote(record);
            _feature.Dispatcher.Reoffer(record);
        }

        public void Close(FlawRecord record, string reason)
        {
            _feature.Monitor.Cycle.CloseRecord(record, reason);
        }

        public void Announce(FlawRecord record, FlawState previous)
        {
            FlawViews.Announce(record, FlawDays.FromDate(ModTicks.Today));
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
