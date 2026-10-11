using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using GameBusinessContract = ProcessorTycoon.CompanySystem.Business.BusinessContract;
using GameBusinessContracts = ProcessorTycoon.CompanySystem.Business.BusinessContractManager;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameCpuData = ProcessorTycoon.Hardware.CpuDataProvider;
using GameProductionWindow = ProcessorTycoon.Production.UI.ProductionWindow;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;
using ICompanyOwner = ProcessorTycoon.CompanySystem.ICompanyOwner;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// Part 2 of the executor (TSK-011; FR-8, FR-9; design.md steps 5, 7 and 8): the CPU families, the production links and the contracts. Checked against
    /// the decompiled game by the spike TSK-003 (<c>mod/requests/rival-acquisitions.md</c>, item 3).
    /// <list type="bullet">
    /// <item><b>Step 5</b> (before the target goes bankrupt, whose <c>TriggerBankruptcy</c> retires every CPU still in its lists): the CPUs of
    /// <c>RetireCpuIds</c> are retired while the target still owns them (its OEM contracts are broken and its stock sold at its own account); then each
    /// family leaves the three lists of the AI (<c>GetCpus</c>, <c>GetBaseCpus</c>, <c>CpusOrderedByProfit</c>). A family whose base is not found stays
    /// with the target whole: a variant must never load without its base.</item>
    /// <item><b>Step 7</b>: with the lines kept, the supply contracts of the target name the player as the provider
    /// (<c>BusinessContract.Initialize(player, client)</c>, the term, markup, fines and exclusivity unchanged), the clients' CPUs the target made are made by
    /// the player (<c>Cpu.SetNewManufacturer</c>), the player's CPUs the target made become the player's own production (<c>OutsourcedID = -1</c>), and the
    /// foundry-services flag and the toggle of the production window are switched on when the target had them. The contracts of the target as a client of a
    /// third party pass to the player whatever happens to the lines (<c>Initialize(provider, player)</c>). The dropped contracts are left alone: the game
    /// removes them when the target is bankrupt.</item>
    /// <item><b>Step 8</b>: the families join the player, the base first: <c>Cpu.Company</c>, the production caches dropped (reflection), a CPU of a bankrupt's
    /// snapshot back on sale (<c>IsRetired = false</c>), <c>Player.AddCpu</c>, <c>ProductionWindow.AddCpu</c> for the CPUs on sale,
    /// <c>CpuDataProvider.OnNewCpu</c>, then <c>ModEvents.RaiseCpuTransferred</c> (the core does not raise it while a save loads; <c>CpuReleased</c> is never
    /// raised for a transfer). <c>Company.AddCpu</c> is not called: it would announce a new release (and the core's P20 would raise <c>CpuReleased</c>).</item>
    /// </list>
    /// The contracts of a plan are named by their place in the copy of <c>BusinessContractManager.GetContracts()</c> the snapshot was made from; they are
    /// looked up in a copy taken in the same call and checked against the provider and the client of the snapshot.
    /// </summary>
    internal sealed partial class AcquisitionExecutor
    {
        private readonly Dictionary<int, GameBusinessContract> _contracts = new Dictionary<int, GameBusinessContract>();
        private readonly Dictionary<int, GameCpu> _detached = new Dictionary<int, GameCpu>();

        /// <summary>The live contracts behind the ids of the snapshot, each checked against its provider and client.</summary>
        private void ResolveContracts(CompanySnapshot t)
        {
            _contracts.Clear();
            GameBusinessContracts manager = GameBusinessContracts.Instance;
            if (manager == null) return;

            List<GameBusinessContract> live = manager.GetContracts();
            var wanted = new List<ContractSnapshot>(t.ProviderContracts);
            wanted.AddRange(t.ClientContracts);
            foreach (ContractSnapshot contract in wanted)
            {
                if (contract == null || contract.Id < 0 || contract.Id >= live.Count) continue;
                GameBusinessContract game = live[contract.Id];
                if (game != null && game.ProviderID == contract.ProviderId && game.ClientID == contract.ClientId) _contracts[contract.Id] = game;
            }
        }

        // ---- step 5: the families leave the AI ----

        public void DetachCpus(AcquisitionPlan plan, DealLog log)
        {
            if (plan.TransferFamilies.Count == 0 && plan.RetireCpuIds.Count == 0) return;

            ICompanyOwner owner = _target.Owner;
            List<GameCpu> cpus = owner != null ? owner.GetCpus() : null;
            if (cpus == null)
            {
                log.Fail("cpus: the target has no list of CPUs");
                return;
            }

            foreach (int id in plan.RetireCpuIds)
            {
                GameCpu cpu = FindCpu(cpus, id);
                if (cpu == null) log.Fail("cpus: the CPU " + Id(id) + " to retire is not the target's");
                else if (!cpu.IsRetired) cpu.TriggerRetirement();
            }

            foreach (CpuFamily family in plan.TransferFamilies)
            {
                if (family.Members.Count == 0) continue;
                GameCpu root = FindCpu(cpus, family.Members[0].SaveId);
                if (root == null || family.Members[0].SaveId != family.BaseSaveId)
                {
                    log.Fail("cpus: the base of the family of " + family.Members[0].Name + " is not found; the family stays with the target");
                    continue;
                }

                foreach (FamilyMember member in family.Members)
                {
                    GameCpu cpu = FindCpu(cpus, member.SaveId);
                    if (cpu == null)
                    {
                        log.Fail("cpus: " + member.Name + " (" + Id(member.SaveId) + ") is not among the target's CPUs");
                        continue;
                    }
                    cpus.Remove(cpu);
                    if (owner.GetBaseCpus() != null) owner.GetBaseCpus().Remove(cpu);
                    if (owner.CpusOrderedByProfit() != null) owner.CpusOrderedByProfit().Remove(cpu);
                    _detached[member.SaveId] = cpu;
                }
            }
        }

        // ---- step 7: production links and contracts ----

        public void MoveContracts(AcquisitionPlan plan, DealLog log)
        {
            if (plan.KeepLines)
            {
                foreach (int id in plan.RepointProviderContractIds)
                {
                    GameBusinessContract contract = ContractOf(id, log);
                    if (contract != null) contract.Initialize(_player, contract.Client);
                }

                MoveClientsCpus(log);

                foreach (int id in plan.PlayerCpusToInHouse)
                {
                    GameCpu cpu = _owner != null ? FindCpu(_owner.GetCpus(), id) : null;
                    if (cpu == null)
                    {
                        log.Fail("contracts: the player's CPU " + Id(id) + " is not found");
                        continue;
                    }
                    cpu.OutsourcedID = -1;
                    if (!GameAccess.ResetProductionCaches(cpu)) log.Note("contracts: the maker cache of " + (cpu.Name ?? Id(id)) + " could not be dropped");
                }

                if (plan.EnableFoundryServices) EnableFoundryServices(log);
            }

            foreach (int id in plan.RepointClientContractIds)
            {
                GameBusinessContract contract = ContractOf(id, log);
                if (contract != null) contract.Initialize(contract.Provider, _player);
            }
        }

        /// <summary>The CPUs of the target's clients that the target made: the player makes them from now on (FR-9 item 1).</summary>
        private void MoveClientsCpus(DealLog log)
        {
            GameCpuData data = GameCpuData.Instance;
            if (data == null)
            {
                log.Fail("contracts: CpuDataProvider is not in the scene; the clients' CPUs keep their maker");
                return;
            }

            List<GameCpu> made = data.GetOutsourcedCpus(new List<GameCpu>(), _target.SaveID, false);
            foreach (GameCpu cpu in made)
            {
                if (cpu == null || ReferenceEquals(cpu.Company, _target) || ReferenceEquals(cpu.Company, _player)) continue;   // the player's own: own production above
                cpu.SetNewManufacturer(_player);
                GameAccess.ResetProductionCaches(cpu);
            }
        }

        private void EnableFoundryServices(DealLog log)
        {
            if (_player.Divisions == null)
            {
                log.Fail("contracts: the player has no divisions to switch foundry services on");
                return;
            }
            _player.Divisions.FoundryServices = true;

            GameProductionWindow window = GameProductionWindow.Instance;
            if (window != null) window.SetFoundryServicesToggle(true);
            else log.Note("contracts: the production window is not in the scene; its foundry toggle follows on the next load");
        }

        private GameBusinessContract ContractOf(int id, DealLog log)
        {
            GameBusinessContract contract;
            if (_contracts.TryGetValue(id, out contract) && contract != null) return contract;
            log.Fail("contracts: the contract #" + Id(id) + " of the snapshot is not found in the game any more");
            return null;
        }

        // ---- step 8: the families join the player ----

        public void AttachCpus(AcquisitionPlan plan, DealLog log)
        {
            if (plan.TransferFamilies.Count == 0) return;
            if (_owner == null)
            {
                log.Fail("cpus: there is no player to give the CPUs to");
                return;
            }

            GameProductionWindow window = GameProductionWindow.Instance;
            foreach (CpuFamily family in plan.TransferFamilies)
            {
                foreach (FamilyMember member in family.Members)
                {
                    GameCpu cpu;
                    if (!_detached.TryGetValue(member.SaveId, out cpu)) continue;   // step 5 said why
                    try
                    {
                        AttachOne(cpu, member, window);
                    }
                    catch (Exception e)
                    {
                        ModLog.Error(Feature, "deal: the CPU " + (member.Name ?? Id(member.SaveId)) + " could not join the player", e);
                        log.Fail("cpus: " + member.Name + " could not join the player (" + e.GetType().Name + ")");
                    }
                }
            }
        }

        private void AttachOne(GameCpu cpu, FamilyMember member, GameProductionWindow window)
        {
            cpu.Company = _player;
            if (member.ToInHouse || cpu.OutsourcedID == _player.SaveID) cpu.OutsourcedID = -1;   // the player made it for the target: own production now
            GameAccess.ResetProductionCaches(cpu);
            if (member.Fate == CpuFate.Revive) cpu.IsRetired = false;

            _owner.AddCpu(cpu);   // Player.AddCpu: the lists and the sales UI, no "new release"
            if (!cpu.IsRetired && window != null) window.AddCpu(cpu);

            GameCpuData data = GameCpuData.Instance;
            Action onNewCpu = data != null ? data.OnNewCpu : null;
            if (onNewCpu != null) onNewCpu();

            ModEvents.RaiseCpuTransferred(Feature, _target, _player, cpu);
        }

        // ---- helpers ----

        private static GameCpu FindCpu(List<GameCpu> cpus, int saveId)
        {
            if (cpus == null) return null;
            foreach (GameCpu cpu in cpus)
            {
                if (cpu != null && cpu.SaveID == saveId) return cpu;
            }
            return null;
        }

        private static string Id(int id)
        {
            return id.ToString(CultureInfo.InvariantCulture);
        }
    }
}
