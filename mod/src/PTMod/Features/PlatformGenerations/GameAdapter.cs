using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using PTMod.Core;
using UnityEngine;
using GameContract = ProcessorTycoon.ContractSystem.Contract;
using GameContracts = ProcessorTycoon.ContractSystem.ContractManager;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameCpus = ProcessorTycoon.Hardware.CpuDataProvider;
using GameCustomHardware = ProcessorTycoon.Hardware.CustomHardwareHelper;
using GameCustomSocket = ProcessorTycoon.Hardware.CustomSocket;
using GameDates = ProcessorTycoon.TimeSystem.DateController;
using GamePackage = ProcessorTycoon.Hardware.IPackage;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// The one place where platform-generations reads the game (design, "Adapter"): the date, the CPUs of the market with their sockets and
    /// ledgers, the contracts. Everything else of the feature works on the plain structures it builds here (<see cref="CpuLedgerView"/>,
    /// <see cref="ReconCpu"/>, <see cref="SocketId"/>, <see cref="GameDate"/>). The game's classes are touched only in the small methods marked
    /// <c>NoInlining</c>, each behind a try/catch that logs once and answers "unknown": a game that does not match makes the feature see
    /// nothing, never throw into the game (NFR-2). Main thread only.
    /// </summary>
    internal static class GameAdapter
    {
        private const string Feature = PlatformGenerationsFeature.FeatureId;

        private static readonly HashSet<string> Reported = new HashSet<string>(StringComparer.Ordinal);
        private static readonly ConditionalWeakTable<GameContract, string> ContractKeys = new ConditionalWeakTable<GameContract, string>();

        // ---- the date ----

        /// <summary>Today in the game (<c>DateController.CurrentDate</c>); <see cref="GameDate.None"/> outside the game scene.</summary>
        public static GameDate Today()
        {
            return GameDate.FromDateTime(ModTicks.Today);
        }

        /// <summary>
        /// The last complete month of the ledgers, <c>DateController.MonthsSinceStart − 1</c> (the lists of the game hold the month in progress as
        /// the last element). False when there is no date controller.
        /// </summary>
        public static bool TryLastFullMonth(out int month)
        {
            month = -1;
            try
            {
                return ReadLastFullMonth(out month);
            }
            catch (Exception ex)
            {
                Failed("DateController.MonthsSinceStart", ex);
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ReadLastFullMonth(out int month)
        {
            GameDates dates = GameDates.Instance;
            if (dates == null)
            {
                month = -1;
                return false;
            }
            month = dates.MonthsSinceStart - 1;
            return true;
        }

        // ---- CPUs ----

        /// <summary>All CPUs of the game, retired ones included (<c>CpuDataProvider.GetAllCpus</c>), into <paramref name="into"/>. False when the provider does not exist.</summary>
        public static bool CollectCpus(List<GameCpu> into)
        {
            into.Clear();
            try
            {
                return ReadCpus(into);
            }
            catch (Exception ex)
            {
                Failed("CpuDataProvider.GetAllCpus", ex);
                into.Clear();
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ReadCpus(List<GameCpu> into)
        {
            GameCpus provider = GameCpus.Instance;
            if (provider == null) return false;
            provider.GetAllCpus(into);
            return true;
        }

        /// <summary>
        /// The socket of a CPU (FR-4 item 4): <c>pkg:&lt;Package.Name&gt;</c> for a standard package, <c>custom:&lt;CustomHardwareID&gt;</c> with the name of the
        /// base package for a customized one. False - the CPU is outside the generations (NFR-2) - when the package is missing, or the game cannot
        /// say what a custom socket is built on (<c>CustomSocket.BasePackage</c> throws when its <c>CustomHardware</c> is gone).
        /// </summary>
        public static bool TryGetSocket(GameCpu cpu, out SocketId socket)
        {
            socket = default(SocketId);
            if (ReferenceEquals(cpu, null)) return false;

            try
            {
                return ReadSocket(cpu, out socket);
            }
            catch (Exception ex)
            {
                socket = default(SocketId);
                Failed("the socket of a CPU (CustomSocket.BasePackage?)", ex);
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ReadSocket(GameCpu cpu, out SocketId socket)
        {
            socket = default(SocketId);
            GamePackage package = cpu.Package;
            if (ReferenceEquals(package, null)) return false;

            if (package.IsCustomized)
            {
                var custom = package as GameCustomSocket;
                if (custom == null) return false;
                socket = SocketIdentity.Custom(custom.CustomHardwareID, custom.BasePackage.Name);
            }
            else
            {
                socket = SocketIdentity.Standard(package.Name);
            }
            return socket.IsValid;
        }

        /// <summary>The name of the memory standard of the CPU (<c>Cpu.Memory.Name</c>); null when it has none.</summary>
        public static string MemoryNameOf(GameCpu cpu)
        {
            try
            {
                return ReadMemoryName(cpu);
            }
            catch (Exception ex)
            {
                Failed("Cpu.Memory.Name", ex);
                return null;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string ReadMemoryName(GameCpu cpu)
        {
            var memory = cpu.Memory;
            return memory == null ? null : memory.Name;
        }

        /// <summary>The CPU is a variant of an earlier design (<c>Cpu.Base</c> is set); false for an original design and for a CPU that cannot be read.</summary>
        public static bool IsVariant(GameCpu cpu)
        {
            if (ReferenceEquals(cpu, null)) return false;
            try
            {
                return ReadIsVariant(cpu);
            }
            catch (Exception ex)
            {
                Failed("Cpu.Base", ex);
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ReadIsVariant(GameCpu cpu)
        {
            return !ReferenceEquals(cpu.Base, null);
        }

        /// <summary>The key of the company that owns the CPU now (<c>player</c>, <c>ai:&lt;UniqueID&gt;</c>); null when the owner is not a company of the game.</summary>
        public static string CompanyKeyOf(GameCpu cpu)
        {
            try
            {
                return ModCompanies.KeyOf(OwnerOf(cpu));
            }
            catch (Exception ex)
            {
                Failed("Cpu.Company", ex);
                return null;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ICompany OwnerOf(GameCpu cpu)
        {
            return cpu.Company;
        }

        /// <summary>
        /// The ledgers of the CPUs for <see cref="InstalledBaseLedger"/> (FR-4): the owner, the socket and generation, the monthly sales. A CPU whose
        /// socket cannot be read stays in the list without a socket (it counts in the totals only); a CPU that cannot be read at all is left out.
        /// </summary>
        public static void BuildLedgerViews(IReadOnlyList<GameCpu> cpus, GenerationCatalog catalog, List<CpuLedgerView> into)
        {
            into.Clear();
            for (int i = 0; i < cpus.Count; i++)
            {
                try
                {
                    CpuLedgerView view;
                    if (ReadView(cpus[i], catalog, out view)) into.Add(view);
                }
                catch (Exception ex)
                {
                    Failed("the ledger of a CPU (CpuBase.Market)", ex);
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ReadView(GameCpu cpu, GenerationCatalog catalog, out CpuLedgerView view)
        {
            view = default(CpuLedgerView);
            if (ReferenceEquals(cpu, null)) return false;

            view.CpuId = cpu.SaveID;
            view.CompanyKey = ModCompanies.KeyOf(cpu.Company);

            SocketId socket;
            if (TryGetSocket(cpu, out socket))
            {
                view.SocketKey = socket.Key;
                view.BasePackageName = socket.BasePackageName;
                GenerationDef gen = catalog == null ? null : catalog.BySocket(socket);
                view.GenId = gen == null ? null : gen.id;
            }

            var market = cpu.Market;
            if (!ReferenceEquals(market, null))
            {
                view.Sold = market.Sold;
                view.SoldByContracts = market.SoldByContracts;
            }
            return true;
        }

        /// <summary>
        /// The CPUs of the market for <see cref="Reconstruction"/> (FR-12 item 3): the year, the owner, the socket and generation. A variant
        /// (<c>Cpu.Base</c> is set) is left out: it is not a first CPU of anything, the base design says it all (D3 of the release handler).
        /// </summary>
        public static void BuildReconCpus(IReadOnlyList<GameCpu> cpus, GenerationCatalog catalog, List<ReconCpu> into)
        {
            into.Clear();
            for (int i = 0; i < cpus.Count; i++)
            {
                try
                {
                    ReconCpu item;
                    if (ReadRecon(cpus[i], catalog, out item)) into.Add(item);
                }
                catch (Exception ex)
                {
                    Failed("the year of a CPU (CpuBase.Year)", ex);
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ReadRecon(GameCpu cpu, GenerationCatalog catalog, out ReconCpu item)
        {
            item = default(ReconCpu);
            if (ReferenceEquals(cpu, null) || !ReferenceEquals(cpu.Base, null)) return false;

            SocketId socket;
            if (!TryGetSocket(cpu, out socket)) return false;
            GenerationDef gen = catalog == null ? null : catalog.BySocket(socket);
            if (gen == null) return false;

            item = new ReconCpu
            {
                CpuId = cpu.SaveID,
                Year = cpu.Year,
                CompanyKey = ModCompanies.KeyOf(cpu.Company),
                SocketKey = socket.Key,
                GenId = gen.id,
            };
            return true;
        }

        // ---- contracts ----

        /// <summary>The contract manager of the scene, as a plain behaviour; null when there is none (the menu, a scene not ready).</summary>
        public static MonoBehaviour FindContractManager()
        {
            try
            {
                return ReadContractManager();
            }
            catch (Exception ex)
            {
                Failed("ContractManager.Instance", ex);
                return null;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static MonoBehaviour ReadContractManager()
        {
            return GameContracts.Instance;
        }

        /// <summary>Subscribes (or unsubscribes) <paramref name="handler"/> to <c>ContractManager.OnNewContracts</c> of the manager. False when it could not.</summary>
        public static bool SetNewContractsHandler(MonoBehaviour manager, Action handler, bool subscribe)
        {
            if (manager == null || handler == null) return false;
            try
            {
                ChangeNewContractsHandler(manager, handler, subscribe);
                return true;
            }
            catch (Exception ex)
            {
                Failed("ContractManager.OnNewContracts", ex);
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ChangeNewContractsHandler(MonoBehaviour manager, Action handler, bool subscribe)
        {
            var contracts = (GameContracts)manager;
            if (subscribe) contracts.OnNewContracts += handler;
            else contracts.OnNewContracts -= handler;
        }

        /// <summary>The contracts that exist: the available ones and the active ones (FR-8 item 8). False when the manager is not there - then nothing may be pruned.</summary>
        public static bool CollectContracts(List<GameContract> into)
        {
            into.Clear();
            try
            {
                return ReadContracts(into);
            }
            catch (Exception ex)
            {
                Failed("ContractManager.GetAvailableContracts", ex);
                into.Clear();
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ReadContracts(List<GameContract> into)
        {
            GameContracts manager = GameContracts.Instance;
            if (manager == null) return false;

            List<GameContract> available = manager.GetAvailableContracts();
            if (available != null) into.AddRange(available);
            List<GameContract> active = manager.GetActiveContracts();
            if (active != null) into.AddRange(active);
            return true;
        }

        /// <summary>
        /// The key of a contract (the game gives it no id): client, review month, units, payment, type (<see cref="ContractKeyBuilder"/>). Made
        /// once per contract object and remembered, so asking again builds no string. Null for no contract.
        /// </summary>
        public static string ContractKey(GameContract contract)
        {
            if (ReferenceEquals(contract, null)) return null;

            string key;
            if (ContractKeys.TryGetValue(contract, out key)) return key;

            key = BuildContractKey(contract);
            try
            {
                ContractKeys.Add(contract, key);
            }
            catch (ArgumentException)
            {
                // Added by someone else in the meantime: the same text.
            }
            return key;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string BuildContractKey(GameContract contract)
        {
            var review = contract.AcceptanceDate;
            int year = review == null ? 0 : review.Year;
            int month = review == null ? 0 : review.Month;
            return ContractKeyBuilder.Build(contract.Company, year, month, contract.Units, contract.PaymentPerUnit, (int)contract.ContractType);
        }

        /// <summary><c>ContractType</c> of the contract: Calculator 0, Computer 1, Console 2.</summary>
        public static int ContractTypeOf(GameContract contract)
        {
            return ReadContractType(contract);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int ReadContractType(GameContract contract)
        {
            return (int)contract.ContractType;
        }

        /// <summary>
        /// The month the contract was made, <c>AcceptanceDate − 12 months</c> (FR-8 item 3; the game puts the review a year after the offer);
        /// <see cref="GameDate.None"/> when the contract has no review date.
        /// </summary>
        public static GameDate CreatedOn(GameContract contract)
        {
            try
            {
                return ReadCreatedOn(contract);
            }
            catch (Exception ex)
            {
                Failed("Contract.AcceptanceDate", ex);
                return GameDate.None;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static GameDate ReadCreatedOn(GameContract contract)
        {
            var review = contract.AcceptanceDate;
            if (review == null || !GameDate.IsValid(review.Year, review.Month, 1)) return GameDate.None;
            return new GameDate(review.Year, review.Month, 1).AddMonths(-12);
        }

        // ---- names and the player's CPUs (the window "Платформы", the notifications) ----

        /// <summary>The name of a company by its key (<c>ICompany.Name</c>); null when the company is not in the world (or has no name).</summary>
        public static string CompanyNameOf(string companyKey)
        {
            if (string.IsNullOrEmpty(companyKey)) return null;
            try
            {
                return ReadCompanyName(companyKey);
            }
            catch (Exception ex)
            {
                Failed("ICompany.Name", ex);
                return null;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string ReadCompanyName(string companyKey)
        {
            ICompany company = ModCompanies.Resolve(companyKey);
            return ReferenceEquals(company, null) ? null : company.Name;
        }

        /// <summary>
        /// The name of a socket as the player knows it (FR-9 item 3): <c>Package.Name</c> for a standard package, <c>CustomSocket.Name</c> for a
        /// custom one (found by its <c>CustomHardwareID</c>). Null when the game cannot say; the caller falls back to the name of the base package.
        /// </summary>
        public static string SocketNameOf(SocketId socket)
        {
            if (!socket.IsValid) return null;
            if (socket.IsStandard) return socket.BasePackageName;

            try
            {
                return ReadCustomSocketName(socket.Key);
            }
            catch (Exception ex)
            {
                Failed("CustomSocket.Name (CustomHardwareHelper)", ex);
                return null;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string ReadCustomSocketName(string socketKey)
        {
            int id;
            if (socketKey == null || !socketKey.StartsWith(SocketIdentity.CustomPrefix, StringComparison.Ordinal)) return null;
            if (!int.TryParse(socketKey.Substring(SocketIdentity.CustomPrefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) return null;

            GameCustomHardware helper = GameCustomHardware.Instance;
            if (helper == null) return null;
            var hardware = helper.GetCustomHardware(id);
            return hardware == null ? null : hardware.Name;
        }

        /// <summary>
        /// The player's released CPUs that are not retired, for the tab "Мои CPU" (FR-9 item 4): the name, the socket (invalid when it cannot be read:
        /// the CPU is outside the generations) and the memory standard. Variants included.
        /// </summary>
        public static void CollectPlayerCpus(IReadOnlyList<GameCpu> cpus, List<MyCpuInput> into)
        {
            into.Clear();
            for (int i = 0; i < cpus.Count; i++)
            {
                try
                {
                    MyCpuInput item;
                    if (ReadPlayerCpu(cpus[i], out item)) into.Add(item);
                }
                catch (Exception ex)
                {
                    Failed("a CPU of the player (name, memory)", ex);
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ReadPlayerCpu(GameCpu cpu, out MyCpuInput item)
        {
            item = default(MyCpuInput);
            if (ReferenceEquals(cpu, null) || !ModCompanies.IsPlayer(cpu.Company)) return false;
            if (!cpu.IsReleased || cpu.IsRetired) return false;

            SocketId socket;
            TryGetSocket(cpu, out socket);   // an unreadable socket stays invalid: the row says "outside the generations"
            item = new MyCpuInput
            {
                Name = cpu.Name,
                Socket = socket,
                MemoryName = MemoryNameOf(cpu),
            };
            return true;
        }

        /// <summary>The contracts on offer now (<c>ContractManager.GetAvailableContracts</c>) with their keys and types, for the OEM view. False when the manager is not there.</summary>
        public static bool CollectOemContracts(List<OemContractInput> into)
        {
            into.Clear();
            try
            {
                return ReadOemContracts(into);
            }
            catch (Exception ex)
            {
                Failed("ContractManager.GetAvailableContracts", ex);
                into.Clear();
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ReadOemContracts(List<OemContractInput> into)
        {
            GameContracts manager = GameContracts.Instance;
            if (manager == null) return false;

            List<GameContract> available = manager.GetAvailableContracts();
            if (available == null) return true;
            for (int i = 0; i < available.Count; i++)
            {
                GameContract contract = available[i];
                if (ReferenceEquals(contract, null)) continue;
                into.Add(new OemContractInput { Key = ContractKey(contract), ContractType = (int)contract.ContractType });
            }
            return true;
        }

        // ---- the log ----

        /// <summary>One warning for each kind of failure: a game that does not match fails on every call, the log must not grow.</summary>
        private static void Failed(string what, Exception ex)
        {
            if (!Reported.Add(what)) return;
            ModLog.Warn(Feature, "adapter: cannot read " + what + " (" + ex.GetType().Name + ": " + ex.Message + "); the feature goes on without it");
        }
    }
}
