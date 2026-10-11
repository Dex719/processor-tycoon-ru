using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Features.FirstYearOnboarding.Logic;
using Cpu = ProcessorTycoon.Hardware.Cpu;
using GameContract = ProcessorTycoon.ContractSystem.Contract;
using GameContracts = ProcessorTycoon.ContractSystem.ContractManager;
using GamePlayer = ProcessorTycoon.Player;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;
using ICompanyOwner = ProcessorTycoon.CompanySystem.ICompanyOwner;
using Project = ProcessorTycoon.ProjectSystem.Project;
using TimerScheduler = ProcessorTycoon.TimeSystem.TimerScheduler;

namespace PTMod.Features.FirstYearOnboarding
{
    /// <summary>
    /// The game side of <see cref="IOnboardingFacts"/> (TSK-003): every fact reads the public state of the game named in the table of predicates
    /// of design.md, with no patches. The game's classes are touched only inside <c>GameAccess</c> (no inlining), so a class that does not match
    /// the game fails there, in a method this class guards. A fact that cannot be read now (a singleton that is not set up in the menu, an object
    /// that is not ready) is false; the first such failure of a source is one warning in the log, a real error is counted against the feature.
    /// Main thread only; never throws.
    /// </summary>
    internal sealed class GameOnboardingFacts : IOnboardingFacts
    {
        private const string SitePrefix = "facts/";

        private static readonly HashSet<string> Warned = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Where <c>onb.stepDone</c> gets its answer: the state of the machine of the tutorial. The director sets it when the machine exists;
        /// until then (and for a game without a tutorial) no step is done.
        /// </summary>
        internal Func<string, bool> StepDoneSource { get; set; }

        public bool CpuProjectActive()
        {
            try
            {
                return GameAccess.PlayerHasProject(false);
            }
            catch (Exception ex)
            {
                Failed("TimerScheduler.GetTimedObjectsOfType<Project>", ex);
                return false;
            }
        }

        public bool FactoryProjectActive()
        {
            try
            {
                return GameAccess.PlayerHasProject(true);
            }
            catch (Exception ex)
            {
                Failed("TimerScheduler.GetTimedObjectsOfType<Project>", ex);
                return false;
            }
        }

        public bool ProducedToday()
        {
            try
            {
                return GameAccess.AnyCpu(PlayerCpus(), CpuTest.Produced);
            }
            catch (Exception ex)
            {
                Failed("Cpu.ProducedLastDay", ex);
                return false;
            }
        }

        public bool SoldToday()
        {
            try
            {
                return GameAccess.AnyCpu(PlayerCpus(), CpuTest.Sold);
            }
            catch (Exception ex)
            {
                Failed("Cpu.SoldLastDay", ex);
                return false;
            }
        }

        public bool MissedOverSold()
        {
            try
            {
                return GameAccess.MissedOverSold(PlayerCpus());
            }
            catch (Exception ex)
            {
                Failed("Cpu.MissedSalesLastDay", ex);
                return false;
            }
        }

        public bool AnyOldCpu()
        {
            try
            {
                return GameAccess.AnyCpu(PlayerCpus(), CpuTest.Old);
            }
            catch (Exception ex)
            {
                Failed("Cpu.IsOld", ex);
                return false;
            }
        }

        public bool OemAvailable()
        {
            try
            {
                return GameAccess.OemAvailable();
            }
            catch (Exception ex)
            {
                Failed("ContractManager.GetAvailableContracts", ex);
                return false;
            }
        }

        public bool Automate()
        {
            try
            {
                return GameAccess.Automate();
            }
            catch (Exception ex)
            {
                Failed("Player.AutomateContracts", ex);
                return false;
            }
        }

        public bool FoundryServices()
        {
            try
            {
                ICompany player = ModCompanies.Resolve(CompanyKey.Player);
                if (ReferenceEquals(player, null))
                {
                    Missing("Player.Instance (the player's company)");
                    return false;
                }
                return GameAccess.FoundryServices(player);
            }
            catch (Exception ex)
            {
                Failed("Divisions.FoundryServices", ex);
                return false;
            }
        }

        public bool IsStepDone(string stepId)
        {
            Func<string, bool> source = StepDoneSource;
            if (source == null || string.IsNullOrEmpty(stepId)) return false;

            try
            {
                return source(stepId);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(OnboardingFeature.FeatureId, SitePrefix + "IsStepDone", ex);
                return false;
            }
        }

        // ---- helpers ----

        /// <summary>The CPUs of the player's company (the live list of its owner); null when the company is not available.</summary>
        private static List<Cpu> PlayerCpus()
        {
            ICompany player = ModCompanies.Resolve(CompanyKey.Player);
            if (ReferenceEquals(player, null))
            {
                Missing("Player.Instance (the player's company)");
                return null;
            }
            return GameAccess.CpusOf(player);
        }

        private static void Missing(string source)
        {
            if (Warned.Add(source))
                ModLog.Warn(OnboardingFeature.FeatureId, "predicates: " + source + " is not available now (no game loaded yet?); the predicates that read it are false until it is");
        }

        private static void Failed(string source, Exception ex)
        {
            if (ex is NullReferenceException)
            {
                Missing(source + " (an object of the game is not set up)");
                return;
            }
            ModHooks.ReportFault(OnboardingFeature.FeatureId, SitePrefix + source, ex);
        }

        private enum CpuTest
        {
            Produced,
            Sold,
            Old
        }

        /// <summary>The only place that touches the classes of the game.</summary>
        private static class GameAccess
        {
            /// <summary>The player has a project of the kind in the scheduler: a CPU design or a factory expansion.</summary>
            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static bool PlayerHasProject(bool factoryExpansion)
            {
                TimerScheduler scheduler = TimerScheduler.Instance;
                if (scheduler == null)
                {
                    Missing("TimerScheduler.Instance");
                    return false;
                }

                List<Project> projects = scheduler.GetTimedObjectsOfType<Project>();
                if (ReferenceEquals(projects, null)) return false;

                for (int i = 0; i < projects.Count; i++)
                {
                    Project project = projects[i];
                    if (project == null) continue;

                    ICompany owner = project.Company;
                    if (ReferenceEquals(owner, null) || !owner.IsPlayer) continue;

                    // GetCpuProject / GetFactoryExpansionProject copy Progress and IsPaused into the project's save object: idempotent, changes nothing in the game.
                    object kind = factoryExpansion ? (object)project.GetFactoryExpansionProject() : project.GetCpuProject();
                    if (!ReferenceEquals(kind, null)) return true;
                }
                return false;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static List<Cpu> CpusOf(ICompany company)
            {
                ICompanyOwner owner = company.Owner;
                if (ReferenceEquals(owner, null))
                {
                    Missing("ICompany.Owner");
                    return null;
                }
                return owner.GetCpus();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static bool AnyCpu(List<Cpu> cpus, CpuTest test)
            {
                if (ReferenceEquals(cpus, null)) return false;

                for (int i = 0; i < cpus.Count; i++)
                {
                    Cpu cpu = cpus[i];
                    if (cpu == null) continue;

                    switch (test)
                    {
                        case CpuTest.Produced:
                            if (cpu.ProducedLastDay > 0) return true;
                            break;
                        case CpuTest.Sold:
                            if (cpu.SoldLastDay > 0) return true;
                            break;
                        case CpuTest.Old:
                            if (cpu.IsReleased && !cpu.IsRetired && cpu.IsOld(-1, -1)) return true;
                            break;
                    }
                }
                return false;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static bool MissedOverSold(List<Cpu> cpus)
            {
                if (ReferenceEquals(cpus, null)) return false;

                long missed = 0;
                long sold = 0;
                for (int i = 0; i < cpus.Count; i++)
                {
                    Cpu cpu = cpus[i];
                    if (cpu == null) continue;
                    missed += cpu.MissedSalesLastDay;
                    sold += cpu.SoldLastDay;
                }
                return missed > sold;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static bool OemAvailable()
            {
                GameContracts manager = GameContracts.Instance;
                if (manager == null)
                {
                    Missing("ContractManager.Instance");
                    return false;
                }

                List<GameContract> available = manager.GetAvailableContracts();
                return !ReferenceEquals(available, null) && available.Count > 0;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static bool Automate()
            {
                GamePlayer player = GamePlayer.Instance;
                if (player == null)
                {
                    Missing("Player.Instance");
                    return false;
                }
                return player.AutomateContracts;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static bool FoundryServices(ICompany company)
            {
                var divisions = company.Divisions;
                if (ReferenceEquals(divisions, null))
                {
                    Missing("ICompany.Divisions");
                    return false;
                }
                return divisions.FoundryServices;
            }
        }
    }
}
