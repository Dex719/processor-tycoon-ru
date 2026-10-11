using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PTMod.Core;
using GameFormatter = ProcessorTycoon.StringFormatter;
using GamePlayer = ProcessorTycoon.Player;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// The review screen before a manual offer (FR-3), as far as the gate needs it. <see cref="OfferGate"/> hands the analysed offer to <see cref="Open"/> and holds the original call back when
    /// it returns true. The screen is the modal window <see cref="OfferReviewDialog"/>; when that cannot be shown (a template of the scene is missing), the fallback is a plain question of the core
    /// (<c>ModUI.Confirm</c>) that lists the risks found: "Send offer" sends it (<see cref="OfferGate.ConfirmAndSend"/>), "Cancel" or the cross sends nothing and writes the line "cancelled on the review screen"
    /// (<see cref="OfferGate.RecordCancelled"/>).
    /// <list type="bullet">
    /// <item><b>It cannot get stuck.</b> A question that cannot be shown (no template in the scene) is answered "no" by the core inside the call; the screen then reports <c>false</c> and the gate
    /// lets the offer go the vanilla way, so the click is never lost.</item>
    /// <item><b>Texts</b> come from <c>ct.review.*</c> and <c>ct.risk.*</c>; names from the game are cut of markup (<see cref="TextSafe.EscapeMarkup"/>).</item>
    /// </list>
    /// </summary>
    internal static class OfferReviewScreen
    {
        /// <summary>Opens the screen for an analysed offer; true when it is shown (the call of the game is held back then). The window first, the plain question when there is no window.</summary>
        internal static Func<OfferEvaluation, bool> Opener = OpenWindowOrQuestion;

        private static bool OpenWindowOrQuestion(OfferEvaluation evaluation)
        {
            if (OfferReviewDialog.Open(evaluation)) return true;
            return OpenQuestion(evaluation);
        }

        /// <summary>Opens the screen through <see cref="Opener"/>. False for no opener or an opener that threw.</summary>
        internal static bool Open(OfferEvaluation evaluation)
        {
            Func<OfferEvaluation, bool> opener = Opener;
            if (opener == null || evaluation == null) return false;

            try
            {
                return opener(evaluation);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(ContractParamSpecs.FeatureId, "OfferReviewScreen/open", ex);
                return false;
            }
        }

        private static bool OpenQuestion(OfferEvaluation evaluation)
        {
            var contract = evaluation.Contract;
            var cpu = evaluation.Cpu;
            int cpuId = cpu.SaveID;
            string message = ModL10n.L(
                "ct.review.message",
                TextSafe.EscapeMarkup(ContractInspector.ContractLabel(contract)),
                TextSafe.EscapeMarkup(cpu.Name),
                RiskText(evaluation));

            bool answeredInside = true;   // the core answers "no" inside Confirm when it cannot show the question
            bool shown = true;
            ModUI.Confirm(
                "ct.review.title",
                message,
                () => OfferGate.ConfirmAndSend(contract, cpuId, OfferSource.Manual),
                () =>
                {
                    if (answeredInside) shown = false;
                    else OfferGate.RecordCancelled(contract, cpuId);
                },
                "ct.btn.send",
                "core.common.cancel");
            answeredInside = false;
            return shown;
        }

        /// <summary>The risks of the offer, one sentence a line (the window lays them out as a list of warnings; this is the same list as text).</summary>
        internal static string RiskText(OfferEvaluation evaluation)
        {
            CommitmentReport report = evaluation.Commitments;
            if (report.Risks == RiskFlags.None) return ModL10n.L("ct.review.noRisks");

            GamePlayer player = GamePlayer.Instance;
            double cash = evaluation.Cpu.Company == null ? 0.0 : evaluation.Cpu.Company.MoneyAmount;
            List<PanelLine> lines = RiskLines.Build(report, evaluation.Odds.CandidateActual, cash, player == null ? 0 : player.ContractProfitMargin);

            GameArgFormatter formatter = GameArgFormatter.Current();
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append("- ").Append(UiKit.PlainText(lines[i], formatter));
            }
            return sb.ToString();
        }
    }
}
