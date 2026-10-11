using System;
using System.Runtime.CompilerServices;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Features.ScenariosAchievements.Logic;
using GameBankruptcyHandler = ProcessorTycoon.Bank.BankruptcyHandler;
using GameBusinessContract = ProcessorTycoon.CompanySystem.Business.BusinessContract;
using GameBusinessContracts = ProcessorTycoon.CompanySystem.Business.BusinessContractManager;
using GameContract = ProcessorTycoon.ContractSystem.Contract;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameCpuData = ProcessorTycoon.Hardware.CpuDataProvider;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.ScenariosAchievements.Integration
{
    /// <summary>
    /// The events of the game that reach the run (TSK-017; design.md "Подписки на события"; FR-5 item 6, FR-6, FR-7 item 1, FR-12):
    /// <list type="bullet">
    /// <item><b>The core's events</b> <c>ModEvents.CpuReleased</c> (ignored during the new game's fast-forward; the core does not raise it while a save loads) and
    /// <c>ModEvents.CpuTransferred</c> (a CPU that moved to the player goes into <c>transferredCpuIds</c> of the run). The feature has no patch on
    /// <c>Company.AddCpu</c> or <c>AICompany.AddCpu</c>: they are the core's.</item>
    /// <item><b>The events of the scene</b> <c>BankruptcyHandler.OnCompanyWentBankrupt</c> (the player: a loss; an AI company: a rival that went bankrupt, unless
    /// the player absorbed it) and <c>BusinessContractManager.OnPlayerSignedContract</c>: the game clears them with the scene, so they are subscribed
    /// anew every time the game is ready (<see cref="Attach"/>) and let go when the scene goes (<see cref="Detach"/>). They are ignored while a save loads and
    /// while the new game fast-forwards (E13).</item>
    /// <item><b>An OEM contract</b> (the postfix <c>ScenarioPatches</c>): <see cref="OnContractPromoted"/>.</item>
    /// </list>
    /// Every entry runs in <c>ModHooks.Guard</c>. Main thread only.
    /// </summary>
    internal static class GameEventHooks
    {
        private static object _bankruptcyHandler;
        private static object _contractManager;
        private static Action<ICompany> _onBankrupt;
        private static Action<GameBusinessContract> _onSigned;

        /// <summary>Subscribes to the events of the core (once per session).</summary>
        internal static void Install()
        {
            ModEvents.CpuReleased += OnCpuReleased;
            ModEvents.CpuTransferred += OnCpuTransferred;
        }

        // ---- the core's events ----

        private static void OnCpuReleased(ICompany owner, GameCpu cpu, bool isVariant, bool duringFastForward)
        {
            if (duringFastForward) return;   // FR-6 item 4: the releases of the fast-forward are not part of the game of the player
            ModHooks.Guard(ScenariosFeature.FeatureId, "GameEventHooks.CpuReleased", () =>
            {
                RunDirector director = RunDirector.Instance;
                if (director == null || cpu == null) return;

                DateTime today = ModTicks.Today;
                CpuInfo info = GameCpus.ReadInfo(cpu, GameCpus.TypeOf(owner), today);
                director.OnCpuReleased(info, ModCompanies.IsPlayer(owner), GameCpus.NameOf(owner), today);
            });
        }

        private static void OnCpuTransferred(ICompany from, ICompany to, GameCpu cpu)
        {
            if (ModTicks.IsFastForwarding) return;   // NFR-1: the fast-forward of a new game costs the run nothing
            ModHooks.Guard(ScenariosFeature.FeatureId, "GameEventHooks.CpuTransferred", () =>
            {
                RunDirector director = RunDirector.Instance;
                if (director == null || cpu == null || !ModCompanies.IsPlayer(to)) return;
                director.OnCpuTransferredToPlayer(cpu.SaveID);
            });
        }

        // ---- the postfix on ContractManager.PromoteContractToActive ----

        /// <summary>An OEM contract became active (the postfix): if the CPU it names is the player's, that is an OEM contract signed.</summary>
        internal static void OnContractPromoted(GameContract contract)
        {
            RunDirector director = RunDirector.Instance;
            if (director == null || contract == null) return;
            if (!IsPlayersCpu(contract.ChosenCpuID)) return;
            director.OnOemSigned();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool IsPlayersCpu(int cpuSaveId)
        {
            GameCpuData provider = GameCpuData.Instance;
            GameCpu cpu = provider == null ? null : provider.GetCpu(cpuSaveId);
            return cpu != null && !ReferenceEquals(cpu.Company, null) && cpu.Company.IsPlayer;
        }

        // ---- the events of the scene ----

        /// <summary>Subscribes to the bankruptcy handler and the business contracts of the scene that is up (and lets go of the previous ones). Returns false when the scene lacks either.</summary>
        internal static bool Attach()
        {
            Detach();
            bool both = AttachBankruptcy();
            both &= AttachContracts();
            return both;
        }

        /// <summary>Lets go of the events of the scene.</summary>
        internal static void Detach()
        {
            DetachBankruptcy();
            DetachContracts();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool AttachBankruptcy()
        {
            GameBankruptcyHandler handler = GameBankruptcyHandler.Instance;
            if (handler == null) return false;

            _onBankrupt = company => ModHooks.Guard(ScenariosFeature.FeatureId, "GameEventHooks.Bankrupt", () => OnCompanyBankrupt(company));
            handler.OnCompanyWentBankrupt += _onBankrupt;
            _bankruptcyHandler = handler;
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DetachBankruptcy()
        {
            var handler = _bankruptcyHandler as GameBankruptcyHandler;
            if (handler != null && _onBankrupt != null) handler.OnCompanyWentBankrupt -= _onBankrupt;
            _bankruptcyHandler = null;
            _onBankrupt = null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool AttachContracts()
        {
            GameBusinessContracts manager = GameBusinessContracts.Instance;
            if (manager == null) return false;

            _onSigned = contract => ModHooks.Guard(ScenariosFeature.FeatureId, "GameEventHooks.BusinessSigned", OnBusinessSigned);
            manager.OnPlayerSignedContract += _onSigned;
            _contractManager = manager;
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DetachContracts()
        {
            var manager = _contractManager as GameBusinessContracts;
            if (manager != null && _onSigned != null) manager.OnPlayerSignedContract -= _onSigned;
            _contractManager = null;
            _onSigned = null;
        }

        /// <summary>
        /// A company went bankrupt (the game's event). The player: the scenario is lost. An AI company: one rival less, unless the player took it out of the game
        /// (<see cref="AcquisitionBridge.IsAbsorbedByPlayer"/>, true already inside this very event for a takeover). Not while a save loads or the new game fast-forwards.
        /// </summary>
        private static void OnCompanyBankrupt(ICompany company)
        {
            if (ModTicks.IsLoading || ModTicks.IsFastForwarding) return;
            RunDirector director = RunDirector.Instance;
            if (director == null || ReferenceEquals(company, null)) return;

            if (ModCompanies.IsPlayer(company))
            {
                director.OnPlayerBankrupt();
                return;
            }

            int uniqueId;
            if (!CompanyKey.TryGetAiId(ModCompanies.KeyOf(company), out uniqueId)) return;
            director.OnRivalBankrupt(AcquisitionBridge.IsAbsorbedByPlayer(uniqueId));
        }

        private static void OnBusinessSigned()
        {
            if (ModTicks.IsLoading || ModTicks.IsFastForwarding) return;
            RunDirector director = RunDirector.Instance;
            if (director != null) director.OnBusinessContractSigned();
        }
    }
}
