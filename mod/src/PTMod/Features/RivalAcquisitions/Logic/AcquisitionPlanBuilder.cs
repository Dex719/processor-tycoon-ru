using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// <c>AcquisitionPlanBuilder</c> (FR-7…FR-9, FR-12, design.md "AcquisitionPlan"): the pure function that turns a quote, the snapshots and the
    /// player's choices into the plan of a deal. "Plan, then execution": nothing here touches the game, the executor carries the plan out step by step.
    /// <list type="bullet">
    /// <item><b>Technologies</b>: the gap, shallowest dependency first; the partial progress; the process-node experience the player lacks.</item>
    /// <item><b>Lines</b>: kept (moved to the same wafer size, or to a smaller one the player knows when the player cannot have it) or sold for their sale cost.
    /// "Sell" is refused when the target makes CPUs of the player.</item>
    /// <item><b>CPUs</b>: in families, the base first, a retired base included. A player who does not develop CPUs gets none (they are retired). CPUs made in the
    /// target's own factory are retired when the lines are sold.</item>
    /// <item><b>Contracts</b>: as a provider they pass with the lines; as a client of a third party they pass with the CPUs made there, unless an exclusive
    /// contract would give the player a second one; a contract between the two is not taken over (the game ends it).</item>
    /// <item><b>Consequences</b> for the historical companies (Intel, AMD, GlobalFoundries) and the change of the player's kind; they need the tick.</item>
    /// </list>
    /// Everything is deterministic: lists are ordered, ties break by id.
    /// </summary>
    public static class AcquisitionPlanBuilder
    {
        // The historical companies of the game (CompanyHelper): Inlet = Intel, EMD = AMD, SolarFoundries = GlobalFoundries.
        public const int IntelId = 0;
        public const int AmdId = 1;
        public const int GlobalFoundriesId = 8;

        /// <summary>AMD spins GlobalFoundries off on this day if it has not done so before (<c>CompanyEventHandler.CheckSolarFoundriesSpawnDate</c>).</summary>
        public static readonly DateTime GlobalFoundriesDate = new DateTime(2009, 3, 2);

        public static AcquisitionPlan Build(DealQuote q, CompanySnapshot t, PlayerSnapshot p, PlanOptions o)
        {
            var plan = new AcquisitionPlan { Type = q.Type };
            if (o == null) o = new PlanOptions();
            plan.KeepLines = o.KeepLines;

            if (t == null || p == null)
            {
                Block(plan, AcqKeys.ReasonUnsupported, null);
                return plan;
            }
            if (q.Type != DealType.BankruptAssets && q.Type != DealType.Distressed && q.Type != DealType.Hostile)
            {
                Block(plan, AcqKeys.PlanNotPurchase, null);
                return plan;
            }
            if (!q.Available) Block(plan, q.ReasonKey ?? AcqKeys.ReasonUnsupported, q.ReasonArg);

            bool keep = o.KeepLines;
            plan.KeepLinesRequired = t.PlayerCpusMadeByTarget > 0;
            if (!keep && plan.KeepLinesRequired) Block(plan, AcqKeys.PlanMustKeepLines, null);

            PlanTechnologies(plan, t);
            PlanLines(plan, t, p, keep);
            List<CpuSnapshot> living = PlanCpus(plan, t, p, keep);
            PlanContracts(plan, t, p, keep, living);

            plan.EnableFoundryServices = keep && t.HadFoundryServices && !p.FoundryServicesEnabled;
            if (keep) plan.PlayerCpusToInHouse.AddRange(t.PlayerCpuIdsMadeByTarget ?? new List<int>());

            PlanConsequences(plan, t, p, keep);

            if (plan.HasConsequencesToAcknowledge() && !o.ConsequencesAcknowledged) Block(plan, AcqKeys.PlanAckRequired, null);
            return plan;
        }

        // ---- technologies and process nodes (FR-7 items 1, 2) ----

        private static void PlanTechnologies(AcquisitionPlan plan, CompanySnapshot t)
        {
            if (t.TechGap != null)
            {
                var gap = new List<TechSnapshot>();
                var seen = new HashSet<int>();
                for (int i = 0; i < t.TechGap.Count; i++)
                {
                    TechSnapshot tech = t.TechGap[i];
                    if (tech != null && seen.Add(tech.Id)) gap.Add(tech);
                }
                gap.Sort(CompareTechnologies);
                plan.TechUnlocks = gap;
                for (int i = 0; i < gap.Count; i++) plan.TechUnlockIds.Add(gap[i].Id);
            }

            if (t.PartialTech != null)
            {
                var unlocked = new HashSet<int>(plan.TechUnlockIds);
                for (int i = 0; i < t.PartialTech.Count; i++)
                {
                    TechProgressSnapshot progress = t.PartialTech[i];
                    // A technology that is unlocked anyway needs no partial progress.
                    if (progress != null && progress.TargetProgress > progress.PlayerProgress && !unlocked.Contains(progress.Id)) plan.TechProgress.Add(progress);
                }
                plan.TechProgress.Sort((a, b) => a.Id.CompareTo(b.Id));
            }

            if (t.Nodes != null)
            {
                for (int i = 0; i < t.Nodes.Count; i++)
                {
                    NodeSnapshot node = t.Nodes[i];
                    if (node != null && node.Gap > 0.0) plan.NodeRaises.Add(node);
                }
                plan.NodeRaises.Sort((a, b) => a.NodeId != b.NodeId ? a.NodeId.CompareTo(b.NodeId) : a.VariantId.CompareTo(b.VariantId));
            }
        }

        private static int CompareTechnologies(TechSnapshot a, TechSnapshot b)
        {
            return a.Depth != b.Depth ? a.Depth.CompareTo(b.Depth) : a.Id.CompareTo(b.Id);
        }

        // ---- lines (FR-7 items 3, 4) ----

        private static void PlanLines(AcquisitionPlan plan, CompanySnapshot t, PlayerSnapshot p, bool keep)
        {
            if (t.Lines == null) return;

            var lines = new List<WaferLinesSnapshot>();
            for (int i = 0; i < t.Lines.Count; i++)
            {
                WaferLinesSnapshot w = t.Lines[i];
                if (w != null && w.Lines > 0) lines.Add(w);
            }
            lines.Sort((a, b) => a.Value != b.Value ? a.Value.CompareTo(b.Value) : a.WaferId.CompareTo(b.WaferId));

            var unlocking = new HashSet<int>(plan.TechUnlockIds);
            double perLine = Positive(p.OperatingCostPerLine);

            foreach (WaferLinesSnapshot w in lines)
            {
                if (!keep)
                {
                    plan.LineSales.Add(new LineSale { WaferId = w.WaferId, Value = w.Value, Lines = w.Lines, SaleCost = Positive(w.SaleCost) });
                    continue;
                }

                WaferLinesSnapshot to;
                if (p.KnowsWafer(w.WaferId) || unlocking.Contains(w.WaferId)) to = w;
                else to = SmallerKnownWafer(p, w.Value);

                if (to == null)
                {
                    Block(plan, AcqKeys.PlanNoWafer, null);
                    continue;
                }

                var move = new LineMove { FromWaferId = w.WaferId, ToWaferId = to.WaferId, Lines = w.Lines, FromValue = w.Value, ToValue = to.Value };
                plan.LineMoves.Add(move);
                if (move.Remapped)
                {
                    plan.Warnings.Add(Note(AcqKeys.WarnLinesRemapped, false, Format(w.Value), Format(to.Value)));
                }
                plan.OpexDeltaPerMonth += perLine * move.Lines * Multiplier(to);
            }
        }

        /// <summary>The largest wafer size the player knows that is not larger than <paramref name="value"/>; when all are larger, the smallest of them; null for none.</summary>
        private static WaferLinesSnapshot SmallerKnownWafer(PlayerSnapshot p, double value)
        {
            WaferLinesSnapshot best = null;
            WaferLinesSnapshot smallest = null;
            if (p.Wafers == null) return null;

            for (int i = 0; i < p.Wafers.Count; i++)
            {
                WaferLinesSnapshot known = p.Wafers[i];
                if (known == null) continue;
                if (smallest == null || known.Value < smallest.Value) smallest = known;
                if (known.Value <= value && (best == null || known.Value > best.Value)) best = known;
            }
            return best ?? smallest;
        }

        private static double Multiplier(WaferLinesSnapshot wafer)
        {
            return wafer.MaintenanceMultiplier > 0.0 && !double.IsInfinity(wafer.MaintenanceMultiplier) ? wafer.MaintenanceMultiplier : 1.0;
        }

        // ---- CPUs (FR-8) ----

        /// <summary>Plans the CPUs; returns the ones that go on living under the player (the contracts depend on them).</summary>
        private static List<CpuSnapshot> PlanCpus(AcquisitionPlan plan, CompanySnapshot t, PlayerSnapshot p, bool keep)
        {
            var living = new List<CpuSnapshot>();
            if (t.Cpus == null || t.Cpus.Count == 0) return living;

            bool bankrupt = t.IsBankrupt;
            bool takesCpus = p.IsCpuCompany;

            var cpus = new List<CpuSnapshot>();
            for (int i = 0; i < t.Cpus.Count; i++)
            {
                if (t.Cpus[i] != null) cpus.Add(t.Cpus[i]);
            }
            cpus.Sort((a, b) => a.SaveId.CompareTo(b.SaveId));

            // The fate of every CPU and, per family root, who is in it.
            var fates = new Dictionary<int, CpuFate>();
            var families = new SortedDictionary<int, List<CpuSnapshot>>();
            var byId = new Dictionary<int, CpuSnapshot>();
            bool anyActiveLost = false;

            foreach (CpuSnapshot cpu in cpus)
            {
                if (byId.ContainsKey(cpu.SaveId)) continue;   // a repeated id is ignored, the first wins
                byId[cpu.SaveId] = cpu;

                bool wanted = bankrupt ? cpu.ActiveAtBankruptcy : !cpu.IsRetired;
                bool made = takesCpus && wanted && (keep || !cpu.MadeInHouseByTarget);

                CpuFate fate;
                if (made) fate = bankrupt ? CpuFate.Revive : CpuFate.Live;
                else if (!cpu.IsRetired) fate = CpuFate.Retire;
                else fate = CpuFate.StaysRetired;
                fates[cpu.SaveId] = fate;

                if (!takesCpus && fate == CpuFate.Retire) anyActiveLost = true;

                int root = cpu.IsBase ? cpu.SaveId : cpu.BaseSaveId;
                List<CpuSnapshot> family;
                if (!families.TryGetValue(root, out family))
                {
                    family = new List<CpuSnapshot>();
                    families[root] = family;
                }
                family.Add(cpu);
            }

            if (!takesCpus && anyActiveLost) plan.Warnings.Add(Note(AcqKeys.WarnNoCpuDivision, false));

            foreach (KeyValuePair<int, List<CpuSnapshot>> pair in families)
            {
                bool anyLives = false;
                foreach (CpuSnapshot cpu in pair.Value)
                {
                    CpuFate fate = fates[cpu.SaveId];
                    if (fate == CpuFate.Live || fate == CpuFate.Revive) anyLives = true;
                }

                if (!anyLives)
                {
                    // The family stays with the target: whatever is on sale now is retired there.
                    foreach (CpuSnapshot cpu in pair.Value)
                    {
                        if (fates[cpu.SaveId] == CpuFate.Retire) plan.RetireCpuIds.Add(cpu.SaveId);
                    }
                    continue;
                }

                var family = new CpuFamily { BaseSaveId = pair.Key };
                CpuSnapshot root;
                if (byId.TryGetValue(pair.Key, out root))
                {
                    family.Members.Add(MemberOf(root, fates[root.SaveId]));
                }
                else
                {
                    string name = "";
                    foreach (CpuSnapshot cpu in pair.Value)
                    {
                        name = cpu.Name;
                        break;
                    }
                    Block(plan, AcqKeys.PlanFamilyBase, name);
                }

                foreach (CpuSnapshot cpu in pair.Value)
                {
                    if (cpu.SaveId == pair.Key) continue;
                    family.Members.Add(MemberOf(cpu, fates[cpu.SaveId]));
                }

                foreach (FamilyMember member in family.Members)
                {
                    if (member.Fate == CpuFate.Retire) plan.RetireCpuIds.Add(member.SaveId);
                    if (member.Fate == CpuFate.Live || member.Fate == CpuFate.Revive) living.Add(byId[member.SaveId]);
                }
                plan.TransferFamilies.Add(family);
            }

            plan.RetireCpuIds.Sort();
            living.Sort((a, b) => a.SaveId.CompareTo(b.SaveId));
            return living;
        }

        private static FamilyMember MemberOf(CpuSnapshot cpu, CpuFate fate)
        {
            return new FamilyMember { SaveId = cpu.SaveId, Name = cpu.Name ?? "", Fate = fate, ToInHouse = cpu.MadeByPlayer, OemContracts = Math.Max(0, cpu.OemContracts) };
        }

        // ---- contracts (FR-9) ----

        private static void PlanContracts(AcquisitionPlan plan, CompanySnapshot t, PlayerSnapshot p, bool keep, List<CpuSnapshot> living)
        {
            // Contracts of the target as the provider: they go with the lines.
            if (t.ProviderContracts != null)
            {
                var ordered = new List<ContractSnapshot>();
                for (int i = 0; i < t.ProviderContracts.Count; i++)
                {
                    ContractSnapshot contract = t.ProviderContracts[i];
                    // A contract with the player is not taken over: the game ends it without a fine when the target is gone (FR-9 item 5).
                    if (contract != null && contract.ClientId != p.SaveId) ordered.Add(contract);
                }
                ordered.Sort((a, b) => a.Id.CompareTo(b.Id));
                foreach (ContractSnapshot contract in ordered)
                {
                    if (keep) plan.RepointProviderContractIds.Add(contract.Id);
                    else plan.DroppedProviderContractIds.Add(contract.Id);
                }
            }

            // Contracts of the target as a client of a third party: they follow the CPUs made at that provider.
            var repointedMakers = new HashSet<int>();
            if (t.ClientContracts != null)
            {
                var live = MakersOf(living);
                bool exclusiveHeld = p.HasExclusiveClientContract;

                var ordered = new List<ContractSnapshot>();
                for (int i = 0; i < t.ClientContracts.Count; i++)
                {
                    ContractSnapshot contract = t.ClientContracts[i];
                    if (contract != null && contract.ProviderId != p.SaveId) ordered.Add(contract);
                }
                ordered.Sort((a, b) => a.Id.CompareTo(b.Id));

                foreach (ContractSnapshot contract in ordered)
                {
                    if (!live.Contains(contract.ProviderId))
                    {
                        plan.DroppedClientContractIds.Add(contract.Id);
                    }
                    else if (contract.IsExclusive && exclusiveHeld)
                    {
                        plan.DroppedClientContractIds.Add(contract.Id);
                    }
                    else
                    {
                        plan.RepointClientContractIds.Add(contract.Id);
                        repointedMakers.Add(contract.ProviderId);
                        if (contract.IsExclusive) exclusiveHeld = true;
                    }
                }
            }

            // A CPU that lives on but is made by a foundry whose contract does not pass is made at the foundry's base markup (or not at all).
            foreach (CpuSnapshot cpu in living)
            {
                if (cpu.MadeByPlayer || !cpu.IsOutsourced || cpu.OutsourcedId < 0 || repointedMakers.Contains(cpu.OutsourcedId)) continue;
                plan.Warnings.Add(Note(AcqKeys.WarnNoContract, false, cpu.Name ?? "", cpu.MakerName ?? ""));
            }
        }

        /// <summary>The <c>SaveID</c>s of the third-party makers of the CPUs that live on under the player (the CPUs the client contracts are for).</summary>
        private static HashSet<int> MakersOf(List<CpuSnapshot> living)
        {
            var makers = new HashSet<int>();
            foreach (CpuSnapshot cpu in living)
            {
                if (cpu.IsOutsourced && !cpu.MadeByPlayer && cpu.OutsourcedId >= 0) makers.Add(cpu.OutsourcedId);
            }
            return makers;
        }

        // ---- consequences (FR-12) ----

        private static void PlanConsequences(AcquisitionPlan plan, CompanySnapshot t, PlayerSnapshot p, bool keep)
        {
            if (t.UniqueId == IntelId || t.UniqueId == AmdId)
            {
                plan.Consequences.Add(Note(AcqKeys.ConsequenceX86, true));
            }

            if (t.UniqueId == AmdId && !p.GlobalFoundriesSpawned && p.Today < GlobalFoundriesDate)
            {
                plan.Consequences.Add(Note(AcqKeys.ConsequenceGlobalFoundries, true));
            }

            if (t.UniqueId == IntelId && !t.HadFoundryServices && !t.HasOtherFoundry)
            {
                plan.Consequences.Add(Note(AcqKeys.ConsequenceIntelFoundry, true));
            }

            if (t.UniqueId == GlobalFoundriesId && keep && t.ProviderContracts != null)
            {
                foreach (ContractSnapshot contract in t.ProviderContracts)
                {
                    if (contract != null && contract.IsExclusive && plan.RepointProviderContractIds.Contains(contract.Id))
                    {
                        plan.Consequences.Add(Note(AcqKeys.ConsequenceGfAmdContract, false, contract.ClientName ?? ""));
                        break;
                    }
                }
            }

            if (keep && p.IsFabless && plan.LinesMoved > 0)
            {
                plan.Consequences.Add(Note(AcqKeys.ConsequenceBecomesIdm, true));
            }
        }

        // ---- helpers ----

        private static PlanNote Note(string key, bool requiresAck, params string[] args)
        {
            return new PlanNote { Key = key, RequiresAck = requiresAck, Args = args ?? new string[0] };
        }

        private static void Block(AcquisitionPlan plan, string key, string arg)
        {
            plan.BlockingErrors.Add(key);
            plan.BlockingArgs.Add(arg);
        }

        private static double Positive(double value)
        {
            return value > 0.0 && !double.IsInfinity(value) ? value : 0.0;
        }

        private static string Format(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
