using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using GameContract = ProcessorTycoon.ContractSystem.Contract;
using GameContractManager = ProcessorTycoon.ContractSystem.ContractManager;
using GameContractWindow = ProcessorTycoon.ContractSystem.UI.ContractWindow;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameCpuProvider = ProcessorTycoon.Hardware.CpuDataProvider;
using GamePlayer = ProcessorTycoon.Player;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>What the prefix of <c>Contract.AddOffer</c> hands to its postfix (<c>__state</c>): whether the call is to be recorded and what the offers looked like before it.</summary>
    internal struct OfferTrack
    {
        /// <summary>The postfix records the call when the offer was really added.</summary>
        public bool Track;

        /// <summary><c>Contract.OffersIDs.Count</c> before the call.</summary>
        public int CountBefore;

        /// <summary>Who sends the offer.</summary>
        public OfferSource Source;

        /// <summary>The key of the sentence of the log line (<c>ct.reason.sentManual</c>, <c>ct.reason.autoConfirmedSafe</c>, ...).</summary>
        public string ReasonKey;

        /// <summary>The risk analysis was made and its figures follow.</summary>
        public bool HasAnalysis;

        /// <summary>The chance the game gives the offer, percent.</summary>
        public int Chance;

        /// <summary>The probability the draw picks the offer, 0..1.</summary>
        public double Actual;

        /// <summary><see cref="RiskFlags"/> as a number.</summary>
        public int Risks;
    }

    /// <summary>
    /// The risk analysis of an offer (<see cref="ContractInspector.BuildOdds"/> and <see cref="ContractInspector.BuildCommitments"/>), made once and kept for the screen
    /// that may follow: the prefix asks for the risks in the mode "only risky", the review screen shows the same figures.
    /// </summary>
    internal sealed class OfferEvaluation
    {
        internal OfferEvaluation(GameContract contract, GameCpu cpu, ContractInspector inspector)
        {
            Contract = contract;
            Cpu = cpu;
            _inspector = inspector;
        }

        private readonly ContractInspector _inspector;
        private bool _done;

        internal GameContract Contract { get; private set; }
        internal GameCpu Cpu { get; private set; }
        internal OddsReport Odds { get; private set; }
        internal CommitmentReport Commitments { get; private set; }

        /// <summary>Makes the analysis (once).</summary>
        internal void Ensure()
        {
            if (_done) return;
            Odds = _inspector.BuildOdds(Contract, Cpu);
            Commitments = _inspector.BuildCommitments(Contract, Cpu, Odds.CandidateActual);
            _done = true;
        }

        internal bool IsDone
        {
            get { return _done; }
        }

        /// <summary>The risks found (the analysis is made if it was not).</summary>
        internal RiskFlags Risks()
        {
            Ensure();
            return Commitments.Risks;
        }
    }

    /// <summary>
    /// P: <c>Contract.AddOffer(int)</c>, prefix and postfix (design.md, Component 7; FR-3, FR-4, FR-14, NFR-2). The one place where the feature changes the flow of the
    /// game, and only for the offer the player sends by hand when a risk was found or the mode is "always": the call is held back and the review screen opens (D1).
    /// <list type="bullet">
    /// <item><b>Prefix</b> (<c>Priority.First</c>, before other mods' prefixes): the rule is <see cref="OfferGatePolicy"/>. The AI's offers, the automation's (<see cref="AutomationScope"/>)
    /// and the mod's own confirmed call (<see cref="BypassScope"/>) go through unchanged; so does a call the game would ignore (already offered, fails the requirements). Any
    /// failure gives <c>true</c>: the offer goes the vanilla way (NFR-2).</item>
    /// <item><b>Postfix</b> records only a call that really added an offer (<c>OffersIDs</c> grew): <see cref="LogTypes.OfferSent"/> after the screen, <see cref="LogTypes.OfferAutoConfirmed"/>
    /// without one; the automation writes its own lines. Every offer of the player counts for the predicate <c>ct.offersSent</c> and raises the event
    /// <see cref="ContractTransparencyEvents.OfferSent"/> once (FR-14).</item>
    /// <item><b>Confirmed send</b> (<see cref="ConfirmAndSend"/>): the screen's "Send offer" calls <c>AddOffer</c> under <see cref="BypassScope"/> and refreshes the contract window.</item>
    /// </list>
    /// To check in the game (the owner, M2 and M3 of tasks.md): with the mode "only risky" a contract with too few free lines opens the screen and "Cancel" sends nothing; with
    /// "Send offer" the button of the window turns into "Cancel offer" and the log has "sent by hand".
    /// </summary>
    internal static class OfferGate
    {
        private const string Feature = ContractParamSpecs.FeatureId;
        private const string PrefixSite = "OfferGate/AddOffer.prefix";
        private const string PostfixSite = "OfferGate/AddOffer.postfix";
        private const string SendSite = "OfferGate/ConfirmAndSend";
        private const string CancelSite = "OfferGate/RecordCancelled";

        /// <summary>The mod's own call of <c>AddOffer</c> is under way (the player confirmed the screen): the prefix lets it through.</summary>
        [ThreadStatic]
        internal static bool BypassScope;

        /// <summary>Who the bypassing call is for (<see cref="OfferSource.Manual"/> or <see cref="OfferSource.Api"/>).</summary>
        [ThreadStatic]
        internal static OfferSource BypassSource;

        /// <summary>The contract automation is running (set by <see cref="AutomationLogger"/>, cleared by its finalizer).</summary>
        internal static bool AutomationScope;

        // ---- the patches ----

        [ModPatch(typeof(GameContract), "AddOffer", PatchKind.Prefix, Priority = 800, ArgTypes = new[] { typeof(int) })]
        private static bool Prefix(GameContract __instance, int cpuID, out OfferTrack __state)
        {
            OfferTrack track = default(OfferTrack);
            bool run = true;
            using (PerfTimer.Start(TimingSites.OfferGatePrefix))
            {
                bool ok = ContractTransparencyFeature.Guarded(PrefixSite, () => { run = Decide(__instance, cpuID, out track); });
                if (!ok)
                {
                    // The vanilla way, as if the patch were not there (NFR-2).
                    track = default(OfferTrack);
                    run = true;
                }
            }
            __state = track;
            return run;
        }

        [ModPatch(typeof(GameContract), "AddOffer", PatchKind.Postfix, ArgTypes = new[] { typeof(int) })]
        private static void Postfix(GameContract __instance, int cpuID, OfferTrack __state)
        {
            if (!__state.Track) return;
            using (PerfTimer.Start(TimingSites.OfferGatePostfix))
            {
                ContractTransparencyFeature.Guarded(PostfixSite, () => Record(__instance, cpuID, __state));
            }
        }

        // ---- the decision ----

        /// <summary>The body of the prefix: true lets the original run. The cheap questions come first, so the AI's calls (thousands in a year) cost a lookup of the CPU and a flag test.</summary>
        private static bool Decide(GameContract contract, int cpuId, out OfferTrack track)
        {
            track = default(OfferTrack);
            ContractTransparencyFeature feature = ContractTransparencyFeature.Instance;
            if (feature == null || contract == null || contract.OffersIDs == null) return true;

            GameCpu cpu = PlayerCpu(cpuId);
            var input = new GateInput
            {
                Bypass = BypassScope,
                AutomationRunning = AutomationScope,
                IsPlayerCpu = cpu != null,
            };

            if (!input.IsPlayerCpu && !input.Bypass && !input.AutomationRunning) return true;   // the AI: nothing to look at

            if (input.IsPlayerCpu && !input.Bypass && !input.AutomationRunning)
            {
                input.AlreadyOffered = contract.HasReceivedOffer(cpuId);
                input.FailsRequirements = contract.FailsAtAnyRequirement(cpu);
                input.Mode = ContractTransparencySettings.ConfirmMode;
                if (!input.AlreadyOffered && !input.FailsRequirements && !feature.Inspector.IsAvailable) return true;   // no analysis without the private members (FR-12 item 3)
            }

            OfferEvaluation evaluation = input.IsPlayerCpu && cpu != null ? new OfferEvaluation(contract, cpu, feature.Inspector) : null;
            GateDecision decision = OfferGatePolicy.Decide(input, () => evaluation == null ? RiskFlags.None : evaluation.Risks());

            switch (decision.Action)
            {
                case GateAction.PassThrough:
                    return true;

                case GateAction.Review:
                    evaluation.Ensure();
                    if (OfferReviewScreen.Open(evaluation)) return false;   // held back: the screen decides
                    // The screen cannot be shown: the offer goes through as if the mode were "never ask".
                    track = Track(contract, OfferSource.ManualAutoConfirmed, "ct.reason.autoConfirmedNoScreen", evaluation);
                    return true;

                default:
                    OfferSource source = input.Bypass ? BypassSource : decision.Source;
                    track = Track(contract, source, ReasonOf(source, input.Mode), evaluation);
                    return true;
            }
        }

        /// <summary>
        /// The player's CPU with this id, or null for anyone else's. The player's CPUs are a short list; the game's own <c>CpuDataProvider.GetCpu</c> walks every CPU of the world, and the
        /// AI makes thousands of offers a year.
        /// </summary>
        private static GameCpu PlayerCpu(int cpuId)
        {
            GamePlayer player = GamePlayer.Instance;
            List<GameCpu> cpus = player == null ? null : player.GetCpus();
            if (cpus == null) return null;

            for (int i = 0; i < cpus.Count; i++)
            {
                GameCpu cpu = cpus[i];
                if (cpu != null && cpu.SaveID == cpuId) return cpu;
            }
            return null;
        }

        private static OfferTrack Track(GameContract contract, OfferSource source, string reasonKey, OfferEvaluation evaluation)
        {
            var track = new OfferTrack { Track = true, CountBefore = contract.OffersIDs.Count, Source = source, ReasonKey = reasonKey };
            if (evaluation != null && evaluation.IsDone)
            {
                track.HasAnalysis = true;
                track.Chance = evaluation.Odds.CandidateGameChance;
                track.Actual = evaluation.Odds.CandidateActual;
                track.Risks = (int)evaluation.Commitments.Risks;
            }
            return track;
        }

        private static string ReasonOf(OfferSource source, ConfirmMode mode)
        {
            switch (source)
            {
                case OfferSource.ManualAutoConfirmed:
                    return mode == ConfirmMode.Never ? "ct.reason.autoConfirmedNever" : "ct.reason.autoConfirmedSafe";
                case OfferSource.Api:
                    return "ct.reason.sentApi";
                default:
                    return "ct.reason.sentManual";
            }
        }

        // ---- the record ----

        /// <summary>
        /// The body of the postfix: the offer was added when the list grew. The counter of the predicate <c>ct.offersSent</c> goes up first, then the line of the log is written (the automation writes
        /// its own lines, FR-6), and last the event <see cref="ContractTransparencyEvents.OfferSent"/> is raised, once, so a subscriber sees the offer in the contract, in the log and in the
        /// counter (FR-14 item 1, AC-14.1).
        /// </summary>
        private static void Record(GameContract contract, int cpuId, OfferTrack track)
        {
            ContractTransparencyFeature feature = ContractTransparencyFeature.Instance;
            if (feature == null || feature.Log == null || contract.OffersIDs == null) return;
            if (!OfferEventRules.WasSent(track.CountBefore, contract.OffersIDs.Count)) return;   // nothing was added: a duplicate, or the game turned the offer down

            ContractLogModel model = feature.Log.Model;
            model.CountOfferSent();
            if (track.Source != OfferSource.Automation) WriteEntry(model, contract, cpuId, track);

            ContractTransparencyEvents.RaiseOfferSent(ContractInspector.ContractKey(contract), cpuId, track.Source);
        }

        /// <summary>The line "offer sent" of the log (a manual offer, with or without the review screen, or through the API).</summary>
        private static void WriteEntry(ContractLogModel model, GameContract contract, int cpuId, OfferTrack track)
        {
            DateTime today = ModTicks.Today;
            if (today == default(DateTime)) return;

            string type = track.Source == OfferSource.ManualAutoConfirmed ? LogTypes.OfferAutoConfirmed : LogTypes.OfferSent;
            string source = track.Source == OfferSource.Api ? LogSources.Api : LogSources.Manual;
            LogEntry entry = NewEntry(LogDates.Write(today), type, source, contract, cpuId);
            entry.reasonKey = track.ReasonKey ?? "ct.reason.sentManual";
            entry.Put(LogDataKeys.Units, contract.Units);
            entry.Put(LogDataKeys.Payment, contract.PaymentPerUnit);
            if (track.HasAnalysis)
            {
                entry.Put(LogDataKeys.Chance, track.Chance);
                entry.Put(LogDataKeys.Actual, track.Actual);
                entry.Put(LogDataKeys.Risks, track.Risks);
            }
            model.Add(entry);
        }

        /// <summary>The player cancelled the review screen: nothing was sent, the log says so (FR-3 item 5).</summary>
        internal static void RecordCancelled(GameContract contract, int cpuId)
        {
            ContractTransparencyFeature.Guarded(CancelSite, () =>
            {
                ContractTransparencyFeature feature = ContractTransparencyFeature.Instance;
                DateTime today = ModTicks.Today;
                if (feature == null || feature.Log == null || contract == null || today == default(DateTime)) return;

                LogEntry entry = NewEntry(LogDates.Write(today), LogTypes.OfferCancelledOnReview, LogSources.Manual, contract, cpuId);
                entry.reasonKey = "ct.reason.cancelledOnReview";
                feature.Log.Model.Add(entry);
            });
        }

        private static LogEntry NewEntry(string day, string type, string source, GameContract contract, int cpuId)
        {
            GameCpu cpu = GameCpuProvider.Instance == null ? null : GameCpuProvider.Instance.GetCpu(cpuId);
            LogEntry entry = LogEntry.Create(day, type, source);
            entry.contractKey = ContractInspector.ContractKey(contract);
            entry.contractLabel = ContractInspector.ContractLabel(contract);
            entry.cpuId = cpuId;
            entry.cpuName = cpu == null ? string.Empty : cpu.Name;
            return entry;
        }

        // ---- the confirmed send ----

        /// <summary>
        /// Sends the offer the player has confirmed: <c>Contract.AddOffer</c> under <see cref="BypassScope"/> (the prefix lets it through and the postfix records it as
        /// <paramref name="source"/>), then the contract window is redrawn with its public <c>TriggerUIUpdate</c> so it shows "Cancel offer". False when the contract is gone
        /// (it was drawn while the screen was open, which the pause should prevent, E10) or the call failed.
        /// </summary>
        internal static bool ConfirmAndSend(GameContract contract, int cpuId, OfferSource source)
        {
            bool sent = false;
            bool ok = ContractTransparencyFeature.Guarded(SendSite, () =>
            {
                if (contract == null) return;
                if (GameContractManager.Instance != null && !GameContractManager.Instance.ContractExists(contract))
                {
                    ModLog.Info(Feature, "offer: the contract is gone, the offer is not sent");
                    return;
                }

                bool wasBypass = BypassScope;
                OfferSource wasSource = BypassSource;
                BypassScope = true;
                BypassSource = source == OfferSource.Api ? OfferSource.Api : OfferSource.Manual;
                try
                {
                    contract.AddOffer(cpuId);
                }
                finally
                {
                    BypassScope = wasBypass;
                    BypassSource = wasSource;
                }

                RefreshContractWindow();
                sent = true;
            });
            return ok && sent;
        }

        /// <summary>The contract window shows the new state of the offer buttons (its <c>TriggerUIUpdate</c> is public).</summary>
        private static void RefreshContractWindow()
        {
            GameContractWindow window = UnityEngine.Object.FindFirstObjectByType<GameContractWindow>();
            if (window != null) window.TriggerUIUpdate();
        }
    }
}
