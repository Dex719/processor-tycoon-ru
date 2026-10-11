using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using GameContract = ProcessorTycoon.ContractSystem.Contract;
using GameContractAutomation = ProcessorTycoon.ContractSystem.Automation.ContractAutomation;
using GameContractManager = ProcessorTycoon.ContractSystem.ContractManager;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GamePlayer = ProcessorTycoon.Player;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>The decisions of the automation for every pair of the player's CPU and an available contract, with the contracts the rows are about (for the check after the run).</summary>
    internal sealed class AutomationPlan
    {
        /// <summary>The rows in the order the game visits the pairs: CPU by CPU, contract by contract.</summary>
        internal readonly List<AutomationRow> Rows = new List<AutomationRow>();

        /// <summary>The contract of each row (parallel to <see cref="Rows"/>); null for a row about a CPU alone or for "automation off".</summary>
        internal readonly List<GameContract> Contracts = new List<GameContract>();

        /// <summary><c>Player.AutomateContracts</c> at the time.</summary>
        internal bool AutomationOn;

        /// <summary><c>Player.ContractProfitMargin</c> at the time, percent.</summary>
        internal int MarginPercent;

        internal void Add(AutomationRow row, GameContract contract)
        {
            Rows.Add(row);
            Contracts.Add(contract);
        }
    }

    /// <summary>What the preview of the automation shows (FR-9): the decisions now, whether the switch is on, and when the next run is.</summary>
    internal sealed class AutomationPreview
    {
        /// <summary>The switch "Auto-sign contracts" is on. When it is off the decisions are those of a run with the switch on, and the screen says nothing will be sent.</summary>
        internal bool AutomationOn;

        /// <summary>The threshold of the slider, percent.</summary>
        internal int MarginPercent;

        /// <summary>The 31 December of the next run; <c>default(DateTime)</c> outside a game.</summary>
        internal DateTime NextRun;

        /// <summary>Days from today to <see cref="NextRun"/> (0 outside a game).</summary>
        internal int DaysUntilRun;

        /// <summary>The decision for every pair and for the CPUs the automation skips whatever the contract.</summary>
        internal List<AutomationRow> Rows = new List<AutomationRow>();
    }

    /// <summary>
    /// Reads the game and applies <see cref="AutomationAdvisor"/> to every pair "CPU of the player x available contract" in the order <c>ContractAutomation.SendOffers</c> visits them.
    /// One planner serves the log (the plan made just before a run) and the preview (the plan made on demand): the two cannot disagree.
    /// A CPU that is retired or not for sale on contracts is skipped for every contract, so it gets one row, not one per contract (a player with a hundred old CPUs would
    /// otherwise fill the log with a thousand lines of nothing).
    /// </summary>
    internal static class AutomationPlanner
    {
        /// <summary>
        /// The plan for now. <paramref name="forRun"/>: a run of the automation; a switch that is off gives the single row "automation off". Not for a run (the preview): the pairs are
        /// decided as if the switch were on.
        /// </summary>
        internal static AutomationPlan Plan(bool forRun)
        {
            var plan = new AutomationPlan();
            GamePlayer player = GamePlayer.Instance;
            if (player == null) return plan;

            plan.AutomationOn = player.AutomateContracts;
            plan.MarginPercent = player.ContractProfitMargin;
            float threshold = AutomationAdvisor.Threshold(plan.MarginPercent);

            if (forRun && !plan.AutomationOn)
            {
                plan.Add(new AutomationRow { Decision = AutomationDecision.AutomationOff, Threshold = threshold }, null);
                return plan;
            }

            List<GameCpu> cpus = player.GetCpus();
            if (cpus == null) return plan;
            List<GameContract> contracts = GameContractManager.Instance == null ? null : GameContractManager.Instance.GetAvailableContracts();

            // The key and the label of a contract are made once, not once per CPU.
            string[] keys = new string[contracts == null ? 0 : contracts.Count];
            string[] labels = new string[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i] = ContractInspector.ContractKey(contracts[i]);
                labels[i] = ContractInspector.ContractLabel(contracts[i]);
            }

            for (int c = 0; c < cpus.Count; c++)
            {
                GameCpu cpu = cpus[c];
                if (cpu == null) continue;

                float ignored;
                AutomationDecision skip = AutomationAdvisor.Decide(
                    new AutomationInput { AutomateContracts = true, CpuRetired = cpu.IsRetired, CpuSellOnContracts = cpu.SellOnContracts }, out ignored);
                if (skip == AutomationDecision.SkippedRetired || skip == AutomationDecision.SkippedNotForContracts)
                {
                    plan.Add(new AutomationRow { CpuId = cpu.SaveID, CpuName = cpu.Name ?? string.Empty, Decision = skip, Threshold = threshold }, null);
                    continue;
                }

                for (int i = 0; i < keys.Length; i++)
                {
                    GameContract contract = contracts[i];
                    var input = new AutomationInput
                    {
                        AutomateContracts = true,
                        CpuRetired = false,
                        CpuSellOnContracts = true,
                        AlreadyOffered = contract.HasReceivedOffer(cpu.SaveID),
                        FailsRequirements = contract.FailsAtAnyRequirement(cpu),
                        PaymentPerUnit = contract.PaymentPerUnit,
                        RealUnitCostAtPayment = cpu.RealUnitCostWithNewPrice(contract.PaymentPerUnit),
                        MarginPercent = plan.MarginPercent,
                    };

                    float margin;
                    AutomationDecision decision = AutomationAdvisor.Decide(input, out margin);
                    var row = new AutomationRow
                    {
                        CpuId = cpu.SaveID,
                        CpuName = cpu.Name ?? string.Empty,
                        ContractKey = keys[i],
                        ContractLabel = labels[i],
                        Decision = decision,
                        Margin = margin,
                        Threshold = threshold,
                        Payment = input.PaymentPerUnit,
                        UnitCost = input.RealUnitCostAtPayment,
                        OfferedBefore = input.AlreadyOffered,
                    };
                    if (decision == AutomationDecision.RejectedByRequirements) row.Failed = FailedRequirements(contract, cpu);
                    plan.Add(row, contract);
                }
            }
            return plan;
        }

        /// <summary>Which requirements the CPU fails, <c>ips,power,package</c> (those that apply); empty when the analysis cannot be made.</summary>
        private static string FailedRequirements(GameContract contract, GameCpu cpu)
        {
            try
            {
                ContractTransparencyFeature feature = ContractTransparencyFeature.Instance;
                if (feature == null) return string.Empty;

                RequirementReport report = feature.Inspector.BuildRequirements(contract, cpu);
                var failed = new List<string>(3);
                if (!report.Ips.Passed) failed.Add("ips");
                if (!report.Power.Passed) failed.Add("power");
                if (!report.Package.Passed) failed.Add("package");
                return string.Join(",", failed.ToArray());
            }
            catch (Exception ex)
            {
                ModLog.Debug(ContractParamSpecs.FeatureId, "automation: the failed requirements could not be named: " + ex.GetType().Name);
                return string.Empty;
            }
        }
    }

    /// <summary>What the prefix of <c>ContractAutomation.SendOffers</c> keeps for the finalizer (<c>__state</c>).</summary>
    internal sealed class AutomationRun
    {
        /// <summary>The plan made before the run; null when nothing is logged (the setting is off, fast-forward, loading).</summary>
        internal AutomationPlan Plan;
    }

    /// <summary>
    /// <c>ContractAutomation.SendOffers()</c> (private, runs on 31 December), prefix and finalizer (design.md, Component 9; FR-6, FR-9, NFR-1): the log of what the "Auto-sign contracts"
    /// switch of the game did and why.
    /// <list type="bullet">
    /// <item><b>Prefix</b> raises <see cref="OfferGate.AutomationScope"/> (the gate lets the automation's <c>AddOffer</c> calls through and never asks) and, when the log of the
    /// automation is on, makes the plan: the decision of <see cref="AutomationAdvisor"/> for every pair, before the game touches anything.</item>
    /// <item><b>Finalizer</b> lowers the flag first, whatever happened, also when <c>SendOffers</c> threw (the exception is handed on unchanged, the mod never hides an error of the
    /// game); then compares the plan with the offers that are really in the contracts and writes the lines (<see cref="AutomationReportBuilder"/>): a line for each decision with
    /// its numbers, a <see cref="LogTypes.Mismatch"/> line where the game did something else, and the summary. A switch that is off gives one line.</item>
    /// <item>Nothing is written during the new-game fast-forward or under the loading screen (FR-5 item 5 applies to the whole feature), nor when the setting "automation log" is off.</item>
    /// <item><see cref="Preview"/> is the same plan without sending anything (FR-9).</item>
    /// </list>
    /// To check in the game (the owner, M5): the preview and the log of 31 December agree; the number of "sent" equals the number of new offers of the player in the contract window.
    /// </summary>
    internal static class AutomationLogger
    {
        private const string Feature = ContractParamSpecs.FeatureId;
        private const string PrefixSite = "AutomationLogger/SendOffers.prefix";
        private const string FinalizerSite = "AutomationLogger/SendOffers.finalizer";

        [ModPatch(typeof(GameContractAutomation), "SendOffers", PatchKind.Prefix)]
        private static void SendOffersPrefix(out AutomationRun __state)
        {
            var run = new AutomationRun();
            using (PerfTimer.Start(TimingSites.AutomationPrefix))
            {
                ContractTransparencyFeature.Guarded(PrefixSite, () =>
                {
                    OfferGate.AutomationScope = true;
                    if (!ContractTransparencySettings.LogAutomation) return;
                    if (ModTicks.IsFastForwarding || ModTicks.IsLoading) return;
                    run.Plan = AutomationPlanner.Plan(true);
                });
            }
            __state = run;
        }

        [ModPatch(typeof(GameContractAutomation), "SendOffers", PatchKind.Finalizer)]
        private static Exception SendOffersFinalizer(Exception __exception, AutomationRun __state)
        {
            // First of all, with no guard that could skip it: the flag must never stay up (it would let the player's manual offers through unasked).
            OfferGate.AutomationScope = false;

            if (__state != null && __state.Plan != null)
            {
                AutomationPlan plan = __state.Plan;
                using (PerfTimer.Start(TimingSites.AutomationFinalizer))
                {
                    ContractTransparencyFeature.Guarded(FinalizerSite, () => Finish(plan));
                }
            }
            return __exception;
        }

        private static void Finish(AutomationPlan plan)
        {
            ContractTransparencyFeature feature = ContractTransparencyFeature.Instance;
            DateTime today = ModTicks.Today;
            if (feature == null || feature.Log == null || today == default(DateTime)) return;

            // What the game really did: the pair is "sent" when the CPU is among the offers now and was not before.
            for (int i = 0; i < plan.Rows.Count; i++)
            {
                GameContract contract = plan.Contracts[i];
                if (contract == null) continue;
                AutomationRow row = plan.Rows[i];
                row.Sent = !row.OfferedBefore && contract.HasReceivedOffer(row.CpuId);
            }

            AutomationReport report = AutomationReportBuilder.Build(plan.Rows, today);
            foreach (string line in report.MismatchLines) ModLog.Warn(Feature, line);

            ContractLogModel model = feature.Log.Model;
            foreach (LogEntry entry in report.Entries) model.Add(entry);
            ModLog.Debug(Feature, "automation: " + plan.Rows.Count.ToString(CultureInfo.InvariantCulture) + " decision(s), sent " +
                                  report.Sent.ToString(CultureInfo.InvariantCulture) + ", skipped " + report.Skipped.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// The decisions the automation would take now (FR-9), without sending anything and without touching the log. Safe to call at any time in a game; empty outside one.
        /// </summary>
        internal static AutomationPreview Preview()
        {
            var preview = new AutomationPreview();
            AutomationPlan plan;
            using (PerfTimer.Start(TimingSites.AutomationPreview))
            {
                plan = AutomationPlanner.Plan(false);
            }
            preview.AutomationOn = plan.AutomationOn;
            preview.MarginPercent = plan.MarginPercent;
            preview.Rows = plan.Rows;

            DateTime today = ModTicks.Today;
            if (today != default(DateTime))
            {
                preview.NextRun = AutomationAdvisor.NextRun(today);
                preview.DaysUntilRun = (int)(preview.NextRun.Date - today.Date).TotalDays;
            }
            return preview;
        }
    }
}
