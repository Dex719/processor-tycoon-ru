using System;
using PTMod.Core;
using GameCompany = ProcessorTycoon.CompanySystem.ICompany;
using GameCpu = ProcessorTycoon.Hardware.Cpu;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The roll at the release of a CPU (FR-1, design.md Component 3): the subscriber of <c>ModEvents.CpuReleased</c>. The feature has no patch on
    /// <c>Company.AddCpu</c> or <c>AICompany.AddCpu</c>; the core raises the event once per CPU object, never while a save loads, and skips the handler
    /// of a feature that is not enabled. The rules are in the pure <see cref="FlawRollEngine"/>; this class reads the game for it and provides the
    /// settings, the random stream and the clock. The body is guarded: an exception never reaches the game.
    /// </summary>
    internal sealed class FlawRoller : IFlawRollHost
    {
        private const string Site = "roll/released";

        private readonly DesignFlawRecallsFeature _feature;
        private readonly FlawRollEngine _engine;

        public FlawRoller(DesignFlawRecallsFeature feature)
        {
            _feature = feature;
            _engine = new FlawRollEngine(feature.Store, this);
        }

        /// <summary>The handler of <c>ModEvents.CpuReleased(owner, cpu, isVariant, duringFastForward)</c>.</summary>
        internal void OnCpuReleased(GameCompany owner, GameCpu cpu, bool isVariant, bool duringFastForward)
        {
            if (cpu == null || owner == null) return;

            int saveId = cpu.SaveID;
            ModHooks.Guard(DesignFlawRecallsFeature.FeatureId, Site, () =>
            {
                try
                {
                    Handle(owner, cpu, isVariant, duringFastForward);
                }
                catch (Exception e)
                {
                    // Guard counts it against the feature and logs it; the SaveID is the context. The CPU stays without a mark.
                    throw new InvalidOperationException("roll of cpu " + saveId + " failed", e);
                }
            });
        }

        private void Handle(GameCompany owner, GameCpu cpu, bool isVariant, bool duringFastForward)
        {
            string companyKey = ModCompanies.KeyOf(owner);
            if (companyKey == null) return;

            int baseId = isVariant && cpu.Base != null ? cpu.Base.SaveID : -1;
            if (isVariant && baseId < 0) isVariant = false;   // a variant whose base is not there rolls like a design of its own

            _engine.OnRelease(companyKey, !ModCompanies.IsPlayer(owner), cpu.SaveID, baseId, isVariant, duringFastForward,
                () => GameCpuView.ReadRisk(owner, cpu));
        }

        // ---- IFlawRollHost ----

        public FlawBalance Balance
        {
            get { return _feature.Balance; }
        }

        public IModRng Rng
        {
            get { return ModRandom.Stream(DesignFlawRecallsFeature.FeatureId + "/roll"); }
        }

        public float FlawFrequency
        {
            get { return FlawSettings.FlawFrequency; }
        }

        public bool AiFlawsEnabled
        {
            get { return FlawSettings.AiFlaws; }
        }

        public bool SaveLocked
        {
            get { return _feature.SaveLocked; }
        }

        public int Today
        {
            get { return FlawDays.FromDate(ModTicks.Today); }
        }

        public float Inflation
        {
            get { return GameCpuView.Inflation(); }
        }

        public void Log(string message)
        {
            ModLog.Info(DesignFlawRecallsFeature.FeatureId, message);
        }
    }
}
