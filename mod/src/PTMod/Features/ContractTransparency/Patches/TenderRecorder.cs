using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using GameContract = ProcessorTycoon.ContractSystem.Contract;
using GameContractHelper = ProcessorTycoon.ContractSystem.ContractHelper;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameCpuProvider = ProcessorTycoon.Hardware.CpuDataProvider;
using GamePlayer = ProcessorTycoon.Player;
using GameProductionMath = ProcessorTycoon.Production.Math.ProductionMath;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>What the prefix of <c>Contract.ChooseCpu</c> saw, kept for the postfix (<c>__state</c>): the offers before the game's check of production lines.</summary>
    internal sealed class ChooseCpuCapture
    {
        /// <summary>The offers in the order of <c>OffersIDs</c>, with the figures of the check for each.</summary>
        internal readonly List<TenderOffer> Before = new List<TenderOffer>();

        /// <summary><c>Player.AutomateRejection</c> at the time: whether the game checks the lines of the player's offers.</summary>
        internal bool AutomateRejection;
    }

    /// <summary>
    /// <c>Contract.ChooseCpu()</c> (private), prefix and postfix (design.md, Component 8; FR-5, FR-7, NFR-1): the result of every tender the player took part in. The game
    /// draws the winner of a contract on its review date, almost always on 1 January (D3).
    /// <list type="bullet">
    /// <item><b>Prefix</b> copies the offers before the game removes those whose producer has no free lines, and takes the figures of the check (lines needed, lines taken,
    /// the capacity, the foundry) for each. It does nothing for a contract without an offer of the player, during the new-game fast-forward and under the loading screen
    /// (FR-5 item 5).</item>
    /// <item><b>Postfix</b> reads <c>ChosenCpuID</c> and the offers that are left, asks the game for the chances of the draw with a second call of the private
    /// <c>GetBestOffers(OffersIDs)</c> (<see cref="ContractInspector.GameChances"/>: the method does not change the contract and does not touch the random numbers, so the draw is the
    /// same with and without the mod, and it runs under the core's own patch of that method, so the chances are those of the draw), and hands everything to
    /// <see cref="TenderAssembler"/>. The result goes to the log; the player's withdrawn offers get lines of their own (FR-7).</item>
    /// <item>No patch on <c>GetBestOffers</c>: it is reserved by the core (P23).</item>
    /// <item>A failure is counted by <c>ModHooks.Guard</c>, nothing is recorded for that contract and the draw goes on as in the game.</item>
    /// </list>
    /// To check in the game (the owner, M6 and M7 of tasks.md): after 1 January a letter "Tender results" and a log of results for the contracts the player offered to;
    /// with "cancel offers automatically" on and too few lines a line "withdrawn by auto-cancel: need N, taken M of K".
    /// </summary>
    internal static class TenderRecorder
    {
        private const string Feature = ContractParamSpecs.FeatureId;
        private const string PrefixSite = "TenderRecorder/ChooseCpu.prefix";
        private const string PostfixSite = "TenderRecorder/ChooseCpu.postfix";

        [ModPatch(typeof(GameContract), "ChooseCpu", PatchKind.Prefix)]
        private static void ChooseCpuPrefix(GameContract __instance, out ChooseCpuCapture __state)
        {
            ChooseCpuCapture capture = null;
            using (PerfTimer.Start(TimingSites.TenderPrefix))
            {
                ContractTransparencyFeature.Guarded(PrefixSite, () => { capture = Capture(__instance); });
            }
            __state = capture;
        }

        [ModPatch(typeof(GameContract), "ChooseCpu", PatchKind.Postfix)]
        private static void ChooseCpuPostfix(GameContract __instance, ChooseCpuCapture __state)
        {
            if (__state == null) return;
            using (PerfTimer.Start(TimingSites.TenderPostfix))
            {
                ContractTransparencyFeature.Guarded(PostfixSite, () => Finish(__instance, __state));
            }
        }

        // ---- before the draw ----

        /// <summary>The copy of the offers, or null when there is nothing to record (no offer of the player, fast-forward, loading).</summary>
        private static ChooseCpuCapture Capture(GameContract contract)
        {
            if (ModTicks.IsFastForwarding || ModTicks.IsLoading) return null;
            if (contract == null || contract.OffersIDs == null || contract.OffersIDs.Count == 0) return null;
            if (GameCpuProvider.Instance == null) return null;

            var capture = new ChooseCpuCapture();
            bool anyPlayer = false;
            List<int> ids = contract.OffersIDs;
            for (int i = 0; i < ids.Count; i++)
            {
                GameCpu cpu = GameCpuProvider.Instance.GetCpu(ids[i]);
                if (cpu == null)
                {
                    // The game does not guard the draw against this: with no usable offer it would draw for ever. The mod reports it and does not touch it (E6).
                    ModLog.Warn(Feature, "tender: the offer " + ids[i].ToString(CultureInfo.InvariantCulture) + " of " + ContractInspector.ContractKey(contract) +
                                         " names a CPU that does not exist; the draw of the game can hang on it");
                    continue;
                }

                TenderOffer offer = ToOffer(cpu, contract);
                if (offer.IsPlayer) anyPlayer = true;
                capture.Before.Add(offer);
            }

            if (!anyPlayer) return null;
            capture.AutomateRejection = GamePlayer.Instance != null && GamePlayer.Instance.AutomateRejection;
            return capture;
        }

        /// <summary>One offer with the figures the game's check of lines uses. A figure the game cannot give stays 0 (the rest of the offer is still recorded).</summary>
        private static TenderOffer ToOffer(GameCpu cpu, GameContract contract)
        {
            ICompany company = cpu.Company;
            ICompany maker = cpu.Manufacturer;
            var offer = new TenderOffer
            {
                CpuId = cpu.SaveID,
                CpuName = cpu.Name ?? string.Empty,
                CompanyKey = ContractInspector.CompanyKeyOf(company),
                CompanyName = ContractInspector.CompanyNameOf(company),
                IsPlayer = company != null && company.IsPlayer,
                Ips = cpu.Ips,
                IsRetired = cpu.IsRetired,
                MakerName = cpu.IsOutsourced && maker != null ? (maker.Name ?? string.Empty) : string.Empty,
            };

            try
            {
                offer.RequiredLines = GameProductionMath.RequiredProductionLines(cpu, contract.Units);
                if (maker != null)
                {
                    offer.CapacityLines = maker.Factory == null ? 0 : maker.Factory.ProductionCapacity;
                    if (maker.Owner != null) offer.ContractedLines = GameContractHelper.ContractedProductionLines(maker.Owner);
                }
            }
            catch (Exception ex)
            {
                ModLog.Debug(Feature, "tender: the figures of the check of lines for CPU " + offer.CpuId.ToString(CultureInfo.InvariantCulture) + " are not available: " + ex.GetType().Name);
            }
            return offer;
        }

        // ---- after the draw ----

        private static void Finish(GameContract contract, ChooseCpuCapture capture)
        {
            ContractTransparencyFeature feature = ContractTransparencyFeature.Instance;
            if (feature == null || feature.Log == null) return;
            if (ModTicks.IsFastForwarding || ModTicks.IsLoading) return;

            DateTime today = ModTicks.Today;
            if (today == default(DateTime)) return;

            var snapshot = new TenderSnapshot
            {
                ContractKey = ContractInspector.ContractKey(contract),
                ContractLabel = ContractInspector.ContractLabel(contract),
                Date = today,
                ChosenCpuId = contract.ChosenCpuID,
                AutomateRejection = capture.AutomateRejection,
            };
            snapshot.Before.AddRange(capture.Before);
            if (contract.OffersIDs != null) snapshot.AfterIds.AddRange(contract.OffersIDs);

            // The chances the draw used: the same private method of the game, called again on the offers that were left.
            if (snapshot.AfterIds.Count > 0) snapshot.DrawChances = feature.Inspector.GameChances(contract, new List<int>(snapshot.AfterIds));

            TenderRecord record = TenderAssembler.Build(snapshot);
            if (record == null) return;

            ContractLogModel model = feature.Log.Model;
            foreach (LogEntry line in record.Withdrawn) model.Add(line);
            bool kept = model.AddTender(record.Result);
            ModLog.Debug(Feature, "tender: " + snapshot.ContractKey + " -> " + record.Result.outcome + " (" + record.Result.reasonKey + ")");

            // FR-14 item 2: the result is in the log and the counter of won tenders is up; subscribers get a copy. Not for a result the log refused. Last, so nothing of ours waits for a subscriber.
            if (kept) ContractTransparencyEvents.RaiseTenderResolved(record.Result);
        }
    }
}
