using System;
using System.Collections.Generic;
using PTMod.Core;
using GameCompany = ProcessorTycoon.CompanySystem.ICompany;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameDataFinder = ProcessorTycoon.DataFinder;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The store behind the public <see cref="FlawRegistry"/> (FR-13, design.md Component 4): the game's side of <see cref="FlawApiEngine"/>. The registry (the facade
    /// in <c>Api/</c>) answers like a null object while the feature is not enabled; while it is, it calls this class, which turns the company of the caller into a key,
    /// reads the CPU from the game and hands the rest to the pure engine. The feature attaches it when it is enabled and detaches it when it is disabled. The
    /// registry wraps every call in try/catch, so an exception here is counted against the feature and the caller gets the null-object answer.
    /// </summary>
    internal sealed class FlawApiBackend : IFlawRegistryBackend, IFlawApiHost
    {
        private static readonly IReadOnlyList<FlawRecordView> None = new FlawRecordView[0];

        private readonly DesignFlawRecallsFeature _feature;
        private readonly FlawApiEngine _engine;

        public FlawApiBackend(DesignFlawRecallsFeature feature)
        {
            _feature = feature;
            _engine = new FlawApiEngine(feature.Store, feature.Monitor.Cycle, this);
        }

        /// <summary>The pure engine.</summary>
        internal FlawApiEngine Engine
        {
            get { return _engine; }
        }

        // ---- IFlawRegistryBackend ----

        public bool TryReveal(int cpuSaveId, DiscoverySource source)
        {
            return _engine.TryReveal(cpuSaveId, source);
        }

        public bool ForceFlaw(int cpuSaveId, string typeId, FlawSeverity severity, DiscoverySource discovery)
        {
            return _engine.ForceFlaw(cpuSaveId, typeId, severity, discovery);
        }

        public IReadOnlyList<FlawRecordView> GetKnownFlaws(GameCompany company)
        {
            string key = ModCompanies.KeyOf(company);
            return key == null ? None : _engine.Known(key);
        }

        public IReadOnlyList<FlawRecordView> GetAllKnownFlaws()
        {
            return _engine.AllKnown();
        }

        public FlawRecordView FindByCpu(int cpuSaveId)
        {
            return _engine.FindByCpu(cpuSaveId);
        }

        // ---- IFlawApiHost ----

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
            get { return ModRandom.Stream(DesignFlawRecallsFeature.FeatureId + "/api"); }
        }

        public float FlawFrequency
        {
            get { return FlawSettings.FlawFrequency; }
        }

        public bool AiFlawsEnabled
        {
            get { return FlawSettings.AiFlaws; }
        }

        public float Inflation
        {
            get { return GameCpuView.Inflation(); }
        }

        public bool SaveLocked
        {
            get { return _feature.SaveLocked; }
        }

        public bool OwnerIsAlive(string companyKey)
        {
            GameCompany owner = ModCompanies.Resolve(companyKey);
            return owner != null && !owner.IsBankrupt;
        }

        public bool CpuExists(int cpuSaveId)
        {
            return GameDataFinder.FindCpu(cpuSaveId) != null;
        }

        public void Log(string line)
        {
            ModLog.Info(DesignFlawRecallsFeature.FeatureId, line);
        }

        /// <summary>
        /// What the game says about the CPU with this <c>SaveID</c>: the base CPU and the owner, whether it is released and on the market, the numbers of the
        /// design (the same the roll at the release reads) and the variants the base CPU has now. <c>null</c> when there is no such CPU.
        /// </summary>
        public FlawCpuInfo FindCpu(int cpuSaveId)
        {
            GameCpu cpu = GameDataFinder.FindCpu(cpuSaveId);
            if (cpu == null) return null;

            GameCpu baseCpu = cpu.Base ?? cpu;
            GameCompany owner = baseCpu.Company ?? cpu.Company;
            string ownerKey = owner == null ? null : ModCompanies.KeyOf(owner);

            var info = new FlawCpuInfo
            {
                SaveId = cpu.SaveID,
                BaseSaveId = baseCpu.SaveID,
                IsVariant = !ReferenceEquals(baseCpu, cpu),
                OwnerKey = ownerKey,
                IsReleased = baseCpu.IsReleased,
                IsRetired = cpu.IsRetired && baseCpu.IsRetired,
                ReleaseDay = ReleaseDayOf(baseCpu),
                VariantSaveIds = VariantsOf(baseCpu),
            };

            if (owner != null)
            {
                try
                {
                    info.Risk = GameCpuView.ReadRisk(owner, baseCpu);
                    info.Risk.Rush = ownerKey != null && ModCompanies.IsPlayer(owner) && _feature.Store.IsRush(baseCpu.SaveID);
                    info.HasRisk = true;
                }
                catch (Exception e)
                {
                    // The type is then chosen without the weights of the design, and the stored probability is 0.
                    ModLog.Debug(DesignFlawRecallsFeature.FeatureId, "api: the design numbers of cpu " + cpuSaveId + " could not be read: " + e.GetType().Name);
                }
            }
            return info;
        }

        /// <summary>The game keeps the year of a CPU only: the middle of that year, never later than today.</summary>
        private int ReleaseDayOf(GameCpu cpu)
        {
            if (cpu.Year < 1971 || cpu.Year > 9000) return 0;
            return Math.Min(FlawDays.FromDate(new DateTime(cpu.Year, 7, 1)), Today);
        }

        private static List<int> VariantsOf(GameCpu baseCpu)
        {
            var ids = new List<int>();
            List<GameCpu> all = GameDataFinder.FindAllCpus();
            if (all == null) return ids;

            foreach (GameCpu candidate in all)
            {
                if (candidate != null && ReferenceEquals(candidate.Base, baseCpu)) ids.Add(candidate.SaveID);
            }
            return ids;
        }
    }
}
