#if PTMOD_DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using UnityEngine;
using GameAiCompany = ProcessorTycoon.CompanySystem.AICompany;
using GameBusinessContract = ProcessorTycoon.CompanySystem.Business.BusinessContract;
using GameBusinessContracts = ProcessorTycoon.CompanySystem.Business.BusinessContractManager;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameCpuData = ProcessorTycoon.Hardware.CpuDataProvider;
using GameDataFinder = ProcessorTycoon.DataFinder;
using GamePlayer = ProcessorTycoon.Player;
using GameProductionWindow = ProcessorTycoon.Production.UI.ProductionWindow;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;
using ICompanyOwner = ProcessorTycoon.CompanySystem.ICompanyOwner;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The spike TSK-003 (design.md, "Результаты спайков"; tasks.md): does a CPU family move from an AI company to the player the way steps 5 and 8 of the design say, and does a
    /// supply contract of a target that is a client keep working when it is handed to the player? The agent never starts the game, so this is the tool the owner checks it with
    /// <b>on a copy of a save</b>. It is written apart from the executor on purpose (the plain calls of the design, nothing of the deal around them), so the answer is about
    /// the game and not about our code. Every result is a line of the log of the feature marked <c>debug: spike 003</c>, with a short note on the screen.
    /// <list type="number">
    /// <item><b>Transfer the family</b> of the chosen CPU of the target: step 5 (it leaves the three lists of the AI), step 8 (the base first: <c>Cpu.Company</c>, the maker
    /// caches dropped by reflection, <c>Player.AddCpu</c>, <c>ProductionWindow.AddCpu</c>, <c>CpuDataProvider.OnNewCpu</c>, <c>ModEvents.RaiseCpuTransferred</c>). The log tells,
    /// for each member, whether it is in the player's list, whose it is, who makes it and at what markup; and how many <c>CpuTransferred</c> and <c>CpuReleased</c> events
    /// came (expected: one per member and none).</item>
    /// <item><b>Check the player's CPUs</b> - after "save, then load": every variant has its base in the player's list, nothing is listed twice, and the members of the last
    /// transfer (kept by <c>SaveID</c> in memory, which survives a load) are all there with the base they had. A variant whose base was not found when the game read the save
    /// has <c>Base == null</c>: the line says so.</item>
    /// <item><b>Contract of the target as a client to the player</b> (run it after the transfer): the first active foundry contract in which the target is the client and the
    /// provider is a third company is handed to the player (<c>BusinessContract.Initialize(provider, player)</c>); the log lists the player's CPUs that provider makes with the
    /// markup they pay and the markup of the contract (a CPU without a working contract pays 2.0). Whether the provider then produces the CPU is seen in the production window
    /// after a day (the owner's note).</item>
    /// </list>
    /// A temporary tool: the owner deletes this file after the results are written into design.md. The whole file is the debug build's (<c>PTMOD_DEBUG</c>) and is on the page
    /// only with <c>debug.acqSpikes</c>; the spike is not marked done by this file.
    /// </summary>
    internal static class SpikeCpuTransfer
    {
        private const string Feature = RivalAcquisitionsFeature.FeatureId;
        private const int MaxChain = 32;

        /// <summary>What the last transfer moved: the id, the base's id (-1 for a base) and the name, to check the family after a load.</summary>
        private sealed class Moved
        {
            public int SaveId;
            public int BaseSaveId;
            public string Name;
        }

        private static readonly List<Moved> LastFamily = new List<Moved>();
        private static int _familyBase = -1;

        // ---- the controls ----

        /// <summary>Puts the controls of the spike into the debug block (the chosen target is the block's).</summary>
        internal static void Build(RectTransform list)
        {
            ModUI.Text(list, ModL10n.L("acq.spike.cpu.heading"), true);
            ModUI.Text(list, ModL10n.L("acq.spike.cpu.hint"));

            GameAiCompany target = AcquisitionsDebugActions.SelectedTarget();
            List<GameCpu> bases = target == null || target.IsBankrupt ? new List<GameCpu>() : ActiveBases(target);
            if (bases.Count == 0)
            {
                ModUI.Text(list, ModL10n.L("acq.spike.cpu.noFamilies"));
            }
            else
            {
                int index = 0;
                var labels = new List<string>();
                for (int i = 0; i < bases.Count; i++)
                {
                    if (bases[i].SaveID == _familyBase) index = i;
                    labels.Add(AcqUi.Plain(bases[i].Name) + " (" + bases[i].Year.ToString(CultureInfo.InvariantCulture) + ")");
                }
                _familyBase = bases[index].SaveID;

                ModUI.Dropdown(list, ModL10n.L("acq.spike.cpu.family"), labels, index, chosen =>
                {
                    if (chosen >= 0 && chosen < bases.Count) _familyBase = bases[chosen].SaveID;
                });
                ModUI.Button(list, ModL10n.L("acq.spike.cpu.transfer"), TransferFamily);
            }

            ModUI.Button(list, ModL10n.L("acq.spike.cpu.check"), CheckPlayerCpus);
            ModUI.Button(list, ModL10n.L("acq.spike.cpu.repoint"), RepointClientContract);
        }

        // ---- 1: the family moves (steps 5 and 8) ----

        private static void TransferFamily()
        {
            ModHooks.Guard(Feature, "debug/spike003/transfer", () =>
            {
                GameAiCompany target = AcquisitionsDebugActions.SelectedTarget();
                GamePlayer owner = GamePlayer.Instance;
                ICompany player = owner != null ? owner.Company : null;
                ICompanyOwner giver = target != null ? target.Owner : null;
                List<GameCpu> cpus = giver != null ? giver.GetCpus() : null;
                GameCpu root = FindCpu(cpus, _familyBase);
                if (target == null || target.IsBankrupt || player == null || root == null || root.Base != null)
                {
                    Refuse("transfer: needs a target that is not bankrupt, the player and the root CPU of a family (target " + AcquisitionsDebugActions.Describe(target) + ", player "
                        + (player != null ? "found" : "missing") + ", root CPU save " + Count(_familyBase) + " " + (root != null && root.Base == null ? "found" : "missing or has a base") + ")", "acq.spike.fail.setup");
                    return;
                }

                List<GameCpu> family = FamilyOf(cpus, root);
                Log("transfer of the family of " + Name(root) + " (" + Count(family.Count) + " CPU) from " + AcquisitionsDebugActions.Describe(target) + " to the player");
                foreach (GameCpu cpu in family) Log("  before: " + Line(cpu));

                int transferred = 0;
                int released = 0;
                Action<ICompany, ICompany, GameCpu> onTransferred = (source, destination, cpu) => transferred++;
                Action<ICompany, GameCpu, bool, bool> onReleased = (company, cpu, variant, flag) => released++;
                ModEvents.CpuTransferred += onTransferred;
                ModEvents.CpuReleased += onReleased;
                try
                {
                    // Step 5: the family leaves the three lists of the AI (before its bankruptcy would retire everything it still lists).
                    foreach (GameCpu cpu in family)
                    {
                        cpus.Remove(cpu);
                        if (giver.GetBaseCpus() != null) giver.GetBaseCpus().Remove(cpu);
                        if (giver.CpusOrderedByProfit() != null) giver.CpusOrderedByProfit().Remove(cpu);
                    }

                    // Step 8: the base first, then the variants (the list is in that order).
                    GameProductionWindow window = GameProductionWindow.Instance;
                    LastFamily.Clear();
                    foreach (GameCpu cpu in family)
                    {
                        LastFamily.Add(new Moved { SaveId = cpu.SaveID, BaseSaveId = cpu.Base != null ? cpu.Base.SaveID : -1, Name = cpu.Name ?? "" });

                        cpu.Company = player;
                        if (cpu.OutsourcedID == player.SaveID) cpu.OutsourcedID = -1;   // the player made it for the target: own production now
                        bool dropped = GameAccess.ResetProductionCaches(cpu);
                        owner.AddCpu(cpu);
                        if (!cpu.IsRetired && window != null) window.AddCpu(cpu);
                        GameCpuData data = GameCpuData.Instance;
                        Action onNewCpu = data != null ? data.OnNewCpu : null;
                        if (onNewCpu != null) onNewCpu();
                        ModEvents.RaiseCpuTransferred(Feature, target, player, cpu);
                        if (!dropped) Log("  the maker caches of " + Name(cpu) + " could not be dropped (reflection)");
                    }
                }
                finally
                {
                    ModEvents.CpuTransferred -= onTransferred;
                    ModEvents.CpuReleased -= onReleased;
                }

                List<GameCpu> mine = owner.GetCpus();
                int missing = 0;
                int foreign = 0;
                foreach (GameCpu cpu in family)
                {
                    bool listed = mine.Contains(cpu);
                    bool theirs = ReferenceEquals(cpu.Company, player);
                    bool baseOk = cpu.Base == null ? owner.GetBaseCpus().Contains(cpu) : mine.Contains(cpu.Base);
                    bool findable = ReferenceEquals(GameDataFinder.FindCpu(cpu.SaveID), cpu);
                    if (!listed) missing++;
                    if (!theirs) foreign++;
                    Log("  after: " + Line(cpu) + "; in the player's list " + YesNo(listed) + ", the player's " + YesNo(theirs) + ", base in place " + YesNo(baseOk)
                        + ", found by DataFinder " + YesNo(findable));
                }

                bool events = transferred == family.Count && released == 0;
                Log("events: CpuTransferred " + Count(transferred) + " (expected " + Count(family.Count) + "), CpuReleased " + Count(released) + " (expected 0): " + (events ? "as designed" : "DIFFERENT"));
                Log("verdict of the transfer: " + (missing == 0 && foreign == 0 && events ? "as designed" : "DIFFERENT (see the lines above)")
                    + ". Next: open the production window (the family is listed, sells, the OEM contracts pay the player), save, load, press \"check the player's CPUs\"");
                AcquisitionsDebugActions.Say("acq.spike.done.transfer", Count(family.Count), Count(transferred), Count(released));
            });
        }

        // ---- 2: the check after a load ----

        private static void CheckPlayerCpus()
        {
            ModHooks.Guard(Feature, "debug/spike003/check", () =>
            {
                GamePlayer owner = GamePlayer.Instance;
                ICompany player = owner != null ? owner.Company : null;
                List<GameCpu> mine = owner != null ? owner.GetCpus() : null;
                if (player == null || mine == null)
                {
                    Refuse("check: the player or the list of the player's CPUs is not available", "acq.spike.fail.setup");
                    return;
                }

                int variants = 0;
                int broken = 0;
                int foreign = 0;
                int repeated = 0;
                var seen = new HashSet<int>();
                foreach (GameCpu cpu in mine)
                {
                    if (cpu == null) continue;
                    if (!seen.Add(cpu.SaveID)) repeated++;
                    if (!ReferenceEquals(cpu.Company, player)) foreign++;
                    if (cpu.Base == null) continue;
                    variants++;
                    if (!mine.Contains(cpu.Base))
                    {
                        broken++;
                        Log("  the base of " + Name(cpu) + " (" + Name(cpu.Base) + ") is not in the player's list");
                    }
                }

                int lost = 0;
                foreach (Moved moved in LastFamily)
                {
                    GameCpu cpu = FindCpu(mine, moved.SaveId);
                    if (cpu == null)
                    {
                        lost++;
                        Log("  MISSING after the load: " + moved.Name + " (" + Count(moved.SaveId) + ")");
                        continue;
                    }

                    bool baseOk = moved.BaseSaveId == -1 ? cpu.Base == null : cpu.Base != null && cpu.Base.SaveID == moved.BaseSaveId;
                    if (!baseOk) lost++;
                    Log("  " + Line(cpu) + "; Cpu.Base " + (cpu.Base != null ? Name(cpu.Base) : "none") + ", expected " + (moved.BaseSaveId == -1 ? "none" : Count(moved.BaseSaveId)) + ": " + (baseOk ? "found" : "NOT FOUND"));
                }

                Log("the player's CPUs: " + Count(mine.Count) + ", variants " + Count(variants) + ", with the base missing from the list " + Count(broken) + ", of another owner " + Count(foreign)
                    + ", listed twice " + Count(repeated) + "; of the last transfer (" + Count(LastFamily.Count) + ") not as moved " + Count(lost) + ": "
                    + (broken == 0 && foreign == 0 && repeated == 0 && lost == 0 ? "as designed" : "DIFFERENT"));
                AcquisitionsDebugActions.Say("acq.spike.done.check", Count(mine.Count), Count(broken), Count(lost));
            });
        }

        // ---- 3: the contract of a target that is a client ----

        private static void RepointClientContract()
        {
            ModHooks.Guard(Feature, "debug/spike003/contract", () =>
            {
                GameAiCompany target = AcquisitionsDebugActions.SelectedTarget();
                GamePlayer owner = GamePlayer.Instance;
                ICompany player = owner != null ? owner.Company : null;
                GameBusinessContracts manager = GameBusinessContracts.Instance;
                if (target == null || player == null || manager == null)
                {
                    Refuse("contract: needs a target, the player and the contract manager (target " + AcquisitionsDebugActions.Describe(target) + ", player " + (player != null ? "found" : "missing")
                        + ", manager " + (manager != null ? "found" : "missing") + ")", "acq.spike.fail.setup");
                    return;
                }

                GameBusinessContract contract = null;
                foreach (GameBusinessContract each in manager.GetContracts())
                {
                    if (each != null && each.ContractID == 0 && each.ClientID == target.SaveID && each.ProviderID != player.SaveID && each.IsActive)
                    {
                        contract = each;
                        break;
                    }
                }
                if (contract == null)
                {
                    Refuse("no active foundry-services contract in which " + AcquisitionsDebugActions.Describe(target) + " is the client", "acq.spike.fail.noContract");
                    return;
                }

                ICompany provider = contract.Provider;
                Log("contract before: " + Contract(contract));
                contract.Initialize(provider, player);
                Log("contract after Initialize(provider, player): " + Contract(contract));

                int made = 0;
                foreach (GameCpu cpu in owner.GetCpus())
                {
                    if (cpu == null || cpu.OutsourcedID != provider.SaveID) continue;
                    made++;
                    GameAccess.ResetProductionCaches(cpu);
                    float markup = cpu.MarkupPrice;
                    bool works = Math.Abs(markup - contract.MarkupPrice()) < 0.0001f;
                    Log("  the player's " + Name(cpu) + " made by " + Name(provider) + ": markup " + markup.ToString("0.###", CultureInfo.InvariantCulture) + ", the contract's "
                        + contract.MarkupPrice().ToString("0.###", CultureInfo.InvariantCulture) + (works ? ": the contract is found" : ": NO CONTRACT FOUND (2.0 is the price without one)"));
                }

                if (made == 0) Log("no CPU of the player is made by " + Name(provider) + " yet: transfer the family of the target first, then repeat");
                Log("Next: pass a day and watch the production window: is the CPU produced by " + Name(provider) + " under this contract?");
                AcquisitionsDebugActions.Say("acq.spike.done.repoint", AcqUi.Plain(Name(provider)));
            });
        }

        // ---- helpers ----

        private static List<GameCpu> ActiveBases(GameAiCompany target)
        {
            var result = new List<GameCpu>();
            List<GameCpu> cpus = target.Owner != null ? target.Owner.GetCpus() : null;
            if (cpus == null) return result;
            foreach (GameCpu cpu in cpus)
            {
                if (cpu != null && cpu.Base == null && !cpu.IsRetired) result.Add(cpu);
            }
            return result;
        }

        /// <summary>The root first, then every CPU of the list whose chain of bases ends in the root (retired members go too: a family moves whole).</summary>
        private static List<GameCpu> FamilyOf(List<GameCpu> cpus, GameCpu root)
        {
            var family = new List<GameCpu> { root };
            foreach (GameCpu cpu in cpus)
            {
                if (cpu == null || ReferenceEquals(cpu, root)) continue;
                if (ReferenceEquals(RootOf(cpu), root)) family.Add(cpu);
            }
            return family;
        }

        private static GameCpu RootOf(GameCpu cpu)
        {
            GameCpu current = cpu;
            for (int i = 0; i < MaxChain && current.Base != null; i++) current = current.Base;
            return current;
        }

        private static GameCpu FindCpu(List<GameCpu> cpus, int saveId)
        {
            if (cpus == null) return null;
            foreach (GameCpu cpu in cpus)
            {
                if (cpu != null && cpu.SaveID == saveId) return cpu;
            }
            return null;
        }

        private static string Line(GameCpu cpu)
        {
            ICompany maker = cpu.Manufacturer;
            return Name(cpu) + " [" + Count(cpu.SaveID) + (cpu.Base != null ? ", variant of " + Count(cpu.Base.SaveID) : ", base") + (cpu.IsRetired ? ", retired" : "")
                   + ", owner " + (cpu.Company != null ? cpu.Company.Name : "?") + ", maker " + (maker != null ? maker.Name : "?") + ", markup " + cpu.MarkupPrice.ToString("0.###", CultureInfo.InvariantCulture) + "]";
        }

        private static string Contract(GameBusinessContract contract)
        {
            return "provider " + Name(contract.Provider) + ", client " + Name(contract.Client) + ", markup " + contract.MarkupPrice().ToString("0.###", CultureInfo.InvariantCulture)
                   + ", lines " + (contract.LinesPercentage() * 100f).ToString("0.#", CultureInfo.InvariantCulture) + " %, active " + YesNo(contract.IsActive);
        }

        private static string Name(ICompany company)
        {
            return company == null ? "?" : company.Name ?? "?";
        }

        private static string Name(GameCpu cpu)
        {
            return cpu == null ? "?" : cpu.Name ?? "?";
        }

        private static string Count(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string YesNo(bool value)
        {
            return value ? "yes" : "NO";
        }

        private static void Log(string text)
        {
            AcquisitionsDebugActions.Journal("spike 003: " + text);
        }

        /// <summary>A refused click: a line of the log (marked as the other lines of the spike), then the note that sends the owner to it.</summary>
        private static void Refuse(string reason, string key)
        {
            Log("refused: " + reason);
            AcquisitionsDebugActions.Say(key);
        }
    }
}
#endif
