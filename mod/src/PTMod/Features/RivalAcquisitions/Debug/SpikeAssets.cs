#if PTMOD_DEBUG
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using GameResearch = ProcessorTycoon.ResearchSystem.ResearchSector;
using GameResearchData = ProcessorTycoon.ResearchSystem.ResearchDataProvider;
using GameTechnology = ProcessorTycoon.ResearchSystem.Technology;
using GameWafer = ProcessorTycoon.Hardware.WaferSize;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The spike TSK-004 (design.md, "Результаты спайков"; tasks.md): do the technologies, the lines, the supply contracts and the signal of a bankruptcy behave the way the
    /// executor relies on? The agent never starts the game, so this is the tool the owner checks it with <b>on a copy of a save</b>. Like <see cref="SpikeCpuTransfer"/> it is
    /// written apart from the executor (the plain calls of the design). Every result is a line of the log of the feature marked <c>debug: spike 004</c>, with a short note on the
    /// screen.
    /// <list type="number">
    /// <item><b>Open N technologies of the target</b> that the player lacks, with their dependencies, with <c>temporarilySupressPopup</c> on, by <c>Research.Progress = 1</c> and repeated
    /// <c>ResearchSector.Update(true)</c>. There must be no "Research completed" windows (the owner looks); the log tells how many passes the chain took.</item>
    /// <item><b>ExpandFactory on a wafer size the player knows</b>: +1 line, the numbers before and after, then the line is taken back. <b>On a wafer size the player does not have</b>: the
    /// game throws <c>InvalidOperationException</c> after it has already raised the capacity - the capacity is put back and the log says what was left behind.</item>
    /// <item><b>Supply contract of the target to the player</b>: the first active foundry contract in which the target is the provider is handed to the player
    /// (<c>BusinessContract.Initialize(player, client)</c>), the CPUs of the client that the target made are made by the player (<c>Cpu.SetNewManufacturer</c>, the caches dropped). The two
    /// buttons of the <b>foundry-services flag</b> switch <c>Divisions.FoundryServices</c> and the toggle of the production window; <b>the report</b> lists the CPUs of the clients the player makes
    /// with what they produced the day before - run it a day after, with the flag on and off (on two copies), and write down whether the flag matters.</item>
    /// <item><b>OnCompanyWentBankrupt for the target</b>: the handlers of the game's event are called (only they - the target does not go bankrupt). For Intel the game's own chain ends in
    /// "Cyrix has no patent problems any more"; the owner looks for the toast.</item>
    /// </list>
    /// The prefix on <c>AICompany.BankruptcyWarning</c> (P3) is not repeated here: it is the real patch of the feature, and the scenario M5 (a stake, a rescue order, the actions of
    /// <see cref="AcquisitionsDebugActions"/>) puts it through the live <c>TickBankruptcies</c>.
    /// A temporary tool: the owner deletes this file after the results are written into design.md. The whole file is the debug build's (<c>PTMOD_DEBUG</c>) and is on the page only with
    /// <c>debug.acqSpikes</c>; the spike is not marked done by this file.
    /// </summary>
    internal static class SpikeAssets
    {
        private const string Feature = RivalAcquisitionsFeature.FeatureId;
        private const int MaxPasses = 40;
        private const int MaxDepth = 64;
        private const int MinTechs = 1, MaxTechs = 40;

        private static int _techCount = 6;

        // ---- the controls ----

        /// <summary>Puts the controls of the spike into the debug block (the chosen target is the block's).</summary>
        internal static void Build(RectTransform list)
        {
            ModUI.Text(list, ModL10n.L("acq.spike.assets.heading"), true);
            ModUI.Text(list, ModL10n.L("acq.spike.assets.hint"));

            ModUI.Slider(list, ModL10n.L("acq.spike.assets.techCount"), MinTechs, MaxTechs, 1f, _techCount, value => _techCount = (int)Math.Round(value));
            ModUI.Button(list, ModL10n.L("acq.spike.assets.techs"), UnlockTechnologies);
            ModUI.Button(list, ModL10n.L("acq.spike.assets.expandKnown"), ExpandKnownWafer);
            ModUI.Button(list, ModL10n.L("acq.spike.assets.expandUnknown"), ExpandUnknownWafer);
            ModUI.Button(list, ModL10n.L("acq.spike.assets.supplier"), RepointSupplierContract);
            ModUI.Button(list, ModL10n.L("acq.spike.assets.flagOn"), () => SetFoundryFlag(true));
            ModUI.Button(list, ModL10n.L("acq.spike.assets.flagOff"), () => SetFoundryFlag(false));
            ModUI.Button(list, ModL10n.L("acq.spike.assets.report"), ReportClientProduction);
            ModUI.Button(list, ModL10n.L("acq.spike.assets.bankrupt"), SignalBankruptcy, ButtonStyle.Standard, ModL10n.L("acq.spike.assets.bankrupt.tip"));
        }

        // ---- 1: technologies without windows ----

        private static void UnlockTechnologies()
        {
            ModHooks.Guard(Feature, "debug/spike004/techs", () =>
            {
                GameAiCompany target = AcquisitionsDebugActions.SelectedTarget();
                GamePlayer owner = GamePlayer.Instance;
                ICompany player = owner != null ? owner.Company : null;
                GameResearch mine = player != null ? player.ResearchSector : null;
                GameResearch theirs = target != null ? target.ResearchSector : null;
                if (mine == null || theirs == null)
                {
                    Refuse("techs: needs the research sectors of the player and of the target (player " + (mine != null ? "found" : "missing") + ", target " + AcquisitionsDebugActions.Describe(target)
                        + (theirs != null ? "" : ", no research sector") + ")", "acq.spike.fail.setup");
                    return;
                }

                GameResearchData data = GameResearchData.Instance;
                List<GameTechnology> all = data != null ? data.GetAllTechnologies() : GameDataFinder.FindAllTechnologies();
                var gap = new List<GameTechnology>();
                if (all != null)
                {
                    foreach (GameTechnology tech in all)
                    {
                        if (tech == null) continue;
                        GameResearch.Research theirResearch = theirs.TechnologyAsResearch(tech);
                        GameResearch.Research myResearch = mine.TechnologyAsResearch(tech);
                        if (theirResearch != null && myResearch != null && theirResearch.IsResearched && !myResearch.IsResearched) gap.Add(tech);
                    }
                }

                // The dependencies come first (a smaller depth), so the first N are closed under "needs": what they need is among them or the player has it already.
                var depths = new Dictionary<int, int>();
                gap.Sort((a, b) =>
                {
                    int byDepth = DepthOf(a, depths, 0).CompareTo(DepthOf(b, depths, 0));
                    return byDepth != 0 ? byDepth : a.ID.CompareTo(b.ID);
                });
                int wanted = Math.Max(MinTechs, Math.Min(MaxTechs, _techCount));
                List<GameTechnology> picked = gap.GetRange(0, Math.Min(wanted, gap.Count));
                if (picked.Count == 0)
                {
                    Log("the target has no technology that the player lacks (" + AcquisitionsDebugActions.Describe(target) + ")");
                    AcquisitionsDebugActions.Say("acq.spike.done.techs", "0", "0", "0");
                    return;
                }

                int deepest = DepthOf(picked[picked.Count - 1], depths, 0);
                Log("the gap of " + AcquisitionsDebugActions.Describe(target) + ": " + Count(gap.Count) + " technologies; unlocking the first " + Count(picked.Count)
                    + " (depth 0.." + Count(deepest) + ") with the popups suppressed");

                var watch = Stopwatch.StartNew();
                bool quiet = GameAccess.SetSuppressPopup(mine, true);
                if (!quiet) Log("temporarilySupressPopup could not be set (reflection): the windows will appear");
                int passes = 0;
                int done = 0;
                try
                {
                    for (; passes < MaxPasses; passes++)
                    {
                        int pending = 0;
                        foreach (GameTechnology tech in picked)
                        {
                            GameResearch.Research research = mine.TechnologyAsResearch(tech);
                            if (research == null || research.IsResearched) continue;
                            pending++;
                            if (!research.IsLocked) research.Progress = 1f;
                        }
                        if (pending == 0) break;
                        mine.Update(true);
                    }

                    foreach (GameTechnology tech in picked)
                    {
                        GameResearch.Research research = mine.TechnologyAsResearch(tech);
                        if (research != null && research.IsResearched) done++;
                    }
                }
                finally
                {
                    if (quiet) GameAccess.SetSuppressPopup(mine, false);
                    watch.Stop();
                }

                Log("unlocked " + Count(done) + " of " + Count(picked.Count) + " in " + Count(passes) + " pass(es) of Update(true), " + watch.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)
                    + " ms; the longest chain among them is " + Count(deepest + 1) + " (one level opens per pass). Look: no \"Research completed\" window must have appeared. TechUnlockMaxPasses of the content: "
                    + Count(RivalAcquisitionsFeature.Instance != null ? RivalAcquisitionsFeature.Instance.Balance.TechUnlockMaxPasses : 0));
                AcquisitionsDebugActions.Say("acq.spike.done.techs", Count(done), Count(picked.Count), Count(passes));
            });
        }

        /// <summary>The depth of a technology in the tree of dependencies (0 without any); a cycle or a very deep chain stops at <see cref="MaxDepth"/>.</summary>
        private static int DepthOf(GameTechnology tech, Dictionary<int, int> memo, int level)
        {
            int known;
            if (memo.TryGetValue(tech.ID, out known)) return known;

            int depth = 0;
            List<GameTechnology> dependencies = tech.Dependencies;
            if (dependencies != null && level < MaxDepth)
            {
                foreach (GameTechnology dependency in dependencies)
                {
                    if (dependency != null) depth = Math.Max(depth, DepthOf(dependency, memo, level + 1) + 1);
                }
            }
            memo[tech.ID] = depth;
            return depth;
        }

        // ---- 2: ExpandFactory ----

        private static void ExpandKnownWafer()
        {
            ModHooks.Guard(Feature, "debug/spike004/expand", () =>
            {
                GamePlayer owner = GamePlayer.Instance;
                ICompany player = owner != null ? owner.Company : null;
                GameWafer wafer = player != null ? SmallestWafer(player) : null;
                if (player == null || player.Factory == null || wafer == null)
                {
                    Refuse("expand: the player has no factory or knows no wafer size", "acq.spike.fail.setup");
                    return;
                }

                int lines0 = player.Factory.AmountOfLines(wafer);
                int capacity0 = player.Factory.ProductionCapacity;
                float cost0 = player.Factory.OperatingCost;

                player.ExpandFactory(1, 0f, wafer.ID);
                int lines1 = player.Factory.AmountOfLines(wafer);
                int capacity1 = player.Factory.ProductionCapacity;
                float cost1 = player.Factory.OperatingCost;

                player.RemoveProductionLines(1, wafer);
                int lines2 = player.Factory.AmountOfLines(wafer);
                int capacity2 = player.Factory.ProductionCapacity;
                float cost2 = player.Factory.OperatingCost;

                bool added = lines1 == lines0 + 1 && capacity1 == capacity0 + 1;
                bool back = lines2 == lines0 && capacity2 == capacity0;
                Log("ExpandFactory(1, 0, " + Count(wafer.ID) + ") on the " + wafer.Value.ToString("0.##", CultureInfo.InvariantCulture) + " mm wafer the player knows: lines " + Count(lines0) + " -> " + Count(lines1)
                    + " -> " + Count(lines2) + " (taken back), capacity " + Count(capacity0) + " -> " + Count(capacity1) + " -> " + Count(capacity2) + ", operating cost "
                    + Money(cost0) + " -> " + Money(cost1) + " -> " + Money(cost2) + ": " + (added ? "a line was added" : "DIFFERENT") + (back ? ", taken back whole" : ", NOT taken back whole"));
                AcquisitionsDebugActions.Say("acq.spike.done.expand", Count(lines0), Count(lines1), Count(lines2));
            });
        }

        private static void ExpandUnknownWafer()
        {
            ModHooks.Guard(Feature, "debug/spike004/expandUnknown", () =>
            {
                GamePlayer owner = GamePlayer.Instance;
                ICompany player = owner != null ? owner.Company : null;
                if (player == null || player.Factory == null)
                {
                    Refuse("expandUnknown: the player has no factory", "acq.spike.fail.setup");
                    return;
                }

                int capacity0 = player.Factory.ProductionCapacity;
                string outcome;
                try
                {
                    player.ExpandFactory(1, 0f, int.MaxValue);
                    outcome = "no exception";
                }
                catch (InvalidOperationException)
                {
                    outcome = "InvalidOperationException";
                }
                catch (Exception e)
                {
                    outcome = e.GetType().Name;
                }

                int capacity1 = player.Factory.ProductionCapacity;
                string restored = "";
                if (capacity1 != capacity0)
                {
                    player.Factory.ProductionCapacity = capacity0;   // the game raised it before it failed to find the wafer; the spike puts it back
                    restored = "; the capacity was raised before the failure and has been put back by the spike";
                }
                Log("ExpandFactory(1, 0, an id the player does not have): " + outcome + ", capacity " + Count(capacity0) + " -> " + Count(capacity1) + restored
                    + ". The plan therefore maps a missing wafer to the largest smaller one before it calls the game");
                AcquisitionsDebugActions.Say("acq.spike.done.expandUnknown", outcome, Count(capacity0), Count(capacity1));
            });
        }

        private static GameWafer SmallestWafer(ICompany company)
        {
            GameWafer best = null;
            if (company.WaferSizes == null) return null;
            foreach (GameWafer wafer in company.WaferSizes)
            {
                if (wafer != null && (best == null || wafer.Value < best.Value)) best = wafer;
            }
            return best;
        }

        // ---- 3: the supply contract and the foundry flag ----

        private static void RepointSupplierContract()
        {
            ModHooks.Guard(Feature, "debug/spike004/supplier", () =>
            {
                GameAiCompany target = AcquisitionsDebugActions.SelectedTarget();
                GamePlayer owner = GamePlayer.Instance;
                ICompany player = owner != null ? owner.Company : null;
                GameBusinessContracts manager = GameBusinessContracts.Instance;
                GameCpuData cpuData = GameCpuData.Instance;
                if (target == null || player == null || manager == null)
                {
                    Refuse("supplier: needs a target, the player and the contract manager (target " + AcquisitionsDebugActions.Describe(target) + ", player " + (player != null ? "found" : "missing")
                        + ", manager " + (manager != null ? "found" : "missing") + ")", "acq.spike.fail.setup");
                    return;
                }

                var handed = new List<GameBusinessContract>();
                foreach (GameBusinessContract each in manager.GetContracts())
                {
                    if (each != null && each.ContractID == 0 && each.ProviderID == target.SaveID && each.ClientID != player.SaveID && each.IsActive) handed.Add(each);
                }
                if (handed.Count == 0)
                {
                    Refuse("no active foundry-services contract in which " + AcquisitionsDebugActions.Describe(target) + " is the provider", "acq.spike.fail.noContract");
                    return;
                }

                foreach (GameBusinessContract contract in handed)
                {
                    Log("contract before: client " + Name(contract.Client) + ", markup " + contract.MarkupPrice().ToString("0.###", CultureInfo.InvariantCulture) + ", lines "
                        + (contract.LinesPercentage() * 100f).ToString("0.#", CultureInfo.InvariantCulture) + " % of the provider");
                    contract.Initialize(player, contract.Client);
                    Log("contract after Initialize(player, client): provider " + Name(contract.Provider) + ", client " + Name(contract.Client) + ", lines "
                        + (contract.LinesPercentage() * 100f).ToString("0.#", CultureInfo.InvariantCulture) + " % of the player's factory");
                }

                int moved = 0;
                if (cpuData != null)
                {
                    foreach (GameCpu cpu in cpuData.GetOutsourcedCpus(new List<GameCpu>(), target.SaveID, false))
                    {
                        if (cpu == null || ReferenceEquals(cpu.Company, target) || ReferenceEquals(cpu.Company, player)) continue;
                        cpu.SetNewManufacturer(player);
                        GameAccess.ResetProductionCaches(cpu);
                        moved++;
                        Log("  " + Name(cpu.Company) + "'s " + (cpu.Name ?? "?") + " is made by the player now; markup " + cpu.MarkupPrice.ToString("0.###", CultureInfo.InvariantCulture));
                    }
                }

                bool flag = player.Divisions != null && player.Divisions.FoundryServices;
                Log(Count(handed.Count) + " contract(s) handed to the player, " + Count(moved) + " CPU(s) of the clients now made by the player; the foundry-services flag of the player is " + (flag ? "on" : "off")
                    + ". Next: pass a day, press the report; repeat on another copy with the flag switched the other way");
                AcquisitionsDebugActions.Say("acq.spike.done.supplier", Count(handed.Count), Count(moved));
            });
        }

        private static void SetFoundryFlag(bool on)
        {
            ModHooks.Guard(Feature, "debug/spike004/flag", () =>
            {
                GamePlayer owner = GamePlayer.Instance;
                ICompany player = owner != null ? owner.Company : null;
                if (player == null || player.Divisions == null)
                {
                    Refuse("flag: the player has no divisions", "acq.spike.fail.setup");
                    return;
                }

                player.Divisions.FoundryServices = on;
                GameProductionWindow window = GameProductionWindow.Instance;
                if (window != null) window.SetFoundryServicesToggle(on);
                Log("the foundry-services flag of the player is " + (on ? "on" : "off") + " (Divisions.FoundryServices"
                    + (window != null ? " and the toggle of the production window)" : "; the production window is not in the scene)"));
                AcquisitionsDebugActions.Say("acq.spike.done.flag", on ? "on" : "off");
            });
        }

        private static void ReportClientProduction()
        {
            ModHooks.Guard(Feature, "debug/spike004/report", () =>
            {
                GamePlayer owner = GamePlayer.Instance;
                ICompany player = owner != null ? owner.Company : null;
                GameCpuData cpuData = GameCpuData.Instance;
                if (player == null || cpuData == null)
                {
                    Refuse("report: needs the player and the data of the CPUs", "acq.spike.fail.setup");
                    return;
                }

                int count = 0;
                int produced = 0;
                foreach (GameCpu cpu in cpuData.GetOutsourcedCpus(new List<GameCpu>(), player.SaveID, false))
                {
                    if (cpu == null || ReferenceEquals(cpu.Company, player)) continue;
                    count++;
                    produced += cpu.ProducedLastDay;
                    Log("  " + Name(cpu.Company) + "'s " + (cpu.Name ?? "?") + ": produced the day before " + Count(cpu.ProducedLastDay) + ", sold " + Count(cpu.SoldLastDay) + ", lines now "
                        + Count(cpu.CurrentProductionLines) + ", allocated " + Count(cpu.LinesActuallyAllocated) + ", missed priority lines " + Count(cpu.MissedPriorityLines)
                        + ", markup " + cpu.MarkupPrice.ToString("0.###", CultureInfo.InvariantCulture));
                }

                bool flag = player.Divisions != null && player.Divisions.FoundryServices;
                Log("CPUs of the clients made by the player: " + Count(count) + ", produced the day before " + Count(produced) + ", the foundry-services flag is " + (flag ? "on" : "off")
                    + (count > 0 && produced == 0 ? ": nothing was produced - pass a day after the transfer and look again" : ""));
                AcquisitionsDebugActions.Say("acq.spike.done.report", Count(count), Count(produced));
            });
        }

        // ---- 4: the signal of a bankruptcy ----

        private static void SignalBankruptcy()
        {
            ModHooks.Guard(Feature, "debug/spike004/bankrupt", () =>
            {
                GameAiCompany target = AcquisitionsDebugActions.SelectedTarget();
                if (target == null || target.IsBankrupt)
                {
                    Refuse("bankrupt signal: needs a target that is not bankrupt (" + AcquisitionsDebugActions.Describe(target) + ")", "acq.spike.fail.setup");
                    return;
                }

                int called = GameAccess.NotifyCompanyWentBankrupt(target);
                Log("OnCompanyWentBankrupt called for " + AcquisitionsDebugActions.Describe(target) + ": " + (called < 0 ? "the member or the handler object is missing" : Count(called) + " handler(s)")
                    + ". For Intel (ai:0) or AMD (ai:1) the game must show \"Cyrix has no patent problems any more\" (look for the toast); the company itself did not go bankrupt");
                AcquisitionsDebugActions.Say("acq.spike.done.bankrupt", Count(called));
            });
        }

        // ---- helpers ----

        private static string Name(ICompany company)
        {
            return company == null ? "?" : company.Name ?? "?";
        }

        private static string Count(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string Money(float value)
        {
            return value.ToString("F0", CultureInfo.InvariantCulture);
        }

        private static void Log(string text)
        {
            AcquisitionsDebugActions.Journal("spike 004: " + text);
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
