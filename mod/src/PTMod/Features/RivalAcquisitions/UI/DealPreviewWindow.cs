using System;
using PTMod.Core;
using TMPro;
using UnityEngine;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;
using GameAiCompany = ProcessorTycoon.CompanySystem.AICompany;
using GameDataFinder = ProcessorTycoon.DataFinder;
using GamePlayer = ProcessorTycoon.Player;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The preview of a purchase of a whole company (TSK-016; FR-3, FR-4, FR-5, FR-7, FR-8, FR-9, FR-12, NFR-5; design.md "Превью сделки"): the blocks "Valuation" (with the
    /// note when the cash floor set the price), "Money", "What passes", "What is retired or terminated", "Consequences", the choice "Keep / Sell the lines" (locked
    /// when the lines are required) and the tick "I understand the consequences" that "Buy" waits for. "Buy" asks <c>ModUI.Confirm</c> (no dialog of its own); a
    /// confirmed deal goes to <see cref="AcquisitionExecutor.Execute"/> with the price and the status the player saw, and the buttons stay locked while it runs. What the
    /// window shows is the pure <see cref="DealPreviewModel"/>; what may be pressed is the pure <see cref="PreviewGate"/>.
    /// <para>
    /// A modal window of its own over the window "Deals" (the time stands anyway). The snapshots are read when it opens and again after a refusal, the plan is built
    /// anew each time a choice changes.
    /// </para>
    /// </summary>
    internal static class DealPreviewWindow
    {
        private const string Feature = RivalAcquisitionsFeature.FeatureId;
        private const string WindowId = Feature + ".preview";
        private const float BodyHeight = 540f;
        private const float BodyMinHeight = 220f;
        private const float ButtonHeight = 38f;
        private const float ButtonWidth = 200f;

        private static readonly Vector2 WindowSize = new Vector2(800f, 700f);

        private static ModWindow window;
        private static RectTransform body;
        private static ButtonUI buyButton;

        private static PreviewGate gate = new PreviewGate();
        private static DealType type;
        private static int uniqueId;
        private static int saveId;
        private static double channel = 1.0;
        private static CompanySnapshot target;
        private static PlayerSnapshot player;
        private static DealQuote quote;
        private static AcquisitionPlan plan;
        private static DealPreviewModel model;
        private static RelatedStatePlan related;
        private static string problem;
        private static bool asking;

        /// <summary>The preview is on the screen.</summary>
        internal static bool IsOpen
        {
            get { return window != null && window.IsAlive && window.IsOpen; }
        }

        /// <summary>
        /// Opens the preview of a purchase of the company (made on first use, filled anew each time). Does nothing while the feature is not active or the
        /// templates of the scene are not ready. Never throws.
        /// </summary>
        internal static void Open(DealType dealType, int targetUniqueId, int targetSaveId)
        {
            ModHooks.Guard(Feature, "ui/preview", () => OpenCore(dealType, targetUniqueId, targetSaveId));
        }

        internal static void Close()
        {
            if (window != null && window.IsAlive && window.IsOpen) window.Close();
        }

        private static void OpenCore(DealType dealType, int targetUniqueId, int targetSaveId)
        {
            if (!ModFeatures.IsActive(Feature)) return;
            if (!ModUI.IsReady)
            {
                ModLog.Warn(Feature, "ui: the preview cannot be made yet, the templates of the scene are not ready");
                return;
            }
            if (!EnsureWindow()) return;

            type = dealType;
            uniqueId = targetUniqueId;
            saveId = targetSaveId;
            gate = new PreviewGate();
            problem = null;

            if (Load()) Rebuild();
            else ShowGone();
            window.Open();
        }

        // ---- the window ----

        private static bool EnsureWindow()
        {
            if (window != null && window.IsAlive && body != null && buyButton != null) return true;

            window = null;
            body = null;
            buyButton = null;
            ModWindow made = ModUI.CreateWindow(WindowId, WindowTemplate.Modal, "acq.preview.title", WindowSize);
            if (made == null) return false;   // ModUI has logged why

            try
            {
                AcqUi.Clear(made.Content);   // the id is unique per scene: a window made before (a frame that failed halfway) comes back as it was
                RectTransform scroll = AcqUi.ScrollBody(made.Content, BodyHeight, BodyMinHeight);
                RectTransform buttons = AcqUi.Row(made.Content, "PTAcqPreviewButtons", ButtonHeight, 12f);
                ButtonUI buy = ModUI.Button(buttons, ModL10n.L("acq.preview.buy"), OnBuy, ButtonStyle.CallToAction);
                ButtonUI cancel = ModUI.Button(buttons, ModL10n.L("core.common.cancel"), Close);
                if (scroll == null || buy == null || cancel == null) throw new InvalidOperationException("a part of the window could not be made (is a widget template of the scene missing?)");

                AcqUi.SetWidth(buy, ButtonWidth, 1f);
                AcqUi.SetWidth(cancel, ButtonWidth, 1f);
                window = made;
                body = scroll;
                buyButton = buy;
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Error(Feature, "ui: cannot build the preview window", ex);
                AcqUi.Clear(made.Content);
                return false;
            }
        }

        // ---- the data ----

        /// <summary>Reads the player and the target anew. False when the company is not the one the player chose (it is gone or another one has its place).</summary>
        private static bool Load()
        {
            target = null;
            RivalAcquisitionsFeature feature = RivalAcquisitionsFeature.Instance;
            var ai = GameDataFinder.FindCompany(saveId) as GameAiCompany;
            if (feature == null || ai == null || ai.UniqueID != uniqueId) return false;

            player = GameSnapshotAdapter.Player();
            if (player == null) return false;

            target = GameSnapshotAdapter.Of(ai, player, feature.State, feature.Balance.Valuation.CpuIncomeMonths);
            channel = GameSnapshotAdapter.PriceChannelOf(ai);

            // What step 9 will pass on from cpu-architectures and gpu-product-line (FR-16); a failure there only leaves these lines out.
            related = null;
            ModHooks.Guard(Feature, "ui/preview.related", () =>
            {
                GamePlayer owner = GamePlayer.Instance;
                if (owner != null) related = RelatedStatePlan.Of(type, new GameRelatedFeatures(ai, owner.Company));
            });
            return true;
        }

        private static void Rebuild()
        {
            RivalAcquisitionsFeature feature = RivalAcquisitionsFeature.Instance;
            if (feature == null || target == null || body == null || window == null || !window.IsAlive) return;

            quote = DealRules.Quote(type, target, player, feature.State, feature.Balance, channel);
            plan = AcquisitionPlanBuilder.Build(quote, target, player, gate.Options());
            model = DealPreviewModel.Build(quote, plan, target, player, AcqUi.Format, related);
            window.SetTitle(AcqUi.Plain(model.Title));
            Draw();
            UpdateButtons();
        }

        private static void RebuildLater()
        {
            AcqUi.Defer(() =>
            {
                if (IsOpen) Rebuild();
            });
        }

        private static void ShowGone()
        {
            AcqUi.Clear(body);
            body.anchoredPosition = Vector2.zero;
            TMP_Text text = ModUI.Text(body, ModL10n.L(AcqKeys.ReasonTargetChanged), true);
            AcqUi.Tint(text, ViewTone.Negative);
            window.SetTitle(ModL10n.L("acq.preview.title"));
            model = null;
            plan = null;
            UpdateButtons();
        }

        // ---- drawing ----

        private static void Draw()
        {
            AcqUi.Clear(body);
            body.anchoredPosition = Vector2.zero;   // a rebuilt preview starts at the top

            if (!string.IsNullOrEmpty(problem))
            {
                TMP_Text refused = ModUI.Text(body, problem, true);
                AcqUi.Tint(refused, ViewTone.Negative);
            }
            foreach (PreviewLine line in model.Problems)
            {
                TMP_Text text = ModUI.Text(body, line.Text, true);
                AcqUi.Tint(text, line.Tone);
            }

            if (model.HasLines) DrawLinesChoice();

            foreach (PreviewSection section in model.Sections)
            {
                ModUI.Text(body, section.Title, true);
                foreach (PreviewLine line in section.Lines) AcqUi.Line(body, line);
            }

            if (model.NeedsAck)
            {
                ModUI.Toggle(body, ModL10n.L("acq.consequence.ack"), gate.Acknowledged, on =>
                {
                    gate.SetAcknowledged(on);
                    RebuildLater();
                });
            }
        }

        private static void DrawLinesChoice()
        {
            ViewFormat f = AcqUi.Format;
            ModUI.Text(body, ModL10n.L("acq.preview.linesChoice"), true);
            RectTransform row = AcqUi.Row(body, "PTAcqLines", ButtonHeight, 8f);

            bool canChoose = gate.CanChooseLines(model.HasLines);
            ButtonUI keep = ModUI.Button(row, ModL10n.L("acq.preview.keepLines"), () => ChooseLines(true), gate.KeepLines ? ButtonStyle.CallToAction : ButtonStyle.Standard);
            ButtonUI sell = ModUI.Button(row, ModL10n.L("acq.preview.sellLines", f.M(model.LinesSaleValue)), () => ChooseLines(false),
                gate.KeepLines ? ButtonStyle.Standard : ButtonStyle.CallToAction);

            AcqUi.SetWidth(keep, ButtonWidth, 1f);
            AcqUi.SetWidth(sell, ButtonWidth, 1f);
            if (keep != null) keep.Interactable = canChoose;
            if (sell != null)
            {
                sell.Interactable = canChoose && !model.KeepLinesRequired;
                if (model.KeepLinesRequired) AcqUi.Tip(sell, ModL10n.L("acq.tip.closed"), ModL10n.L(AcqKeys.PlanMustKeepLines));
            }
        }

        private static void ChooseLines(bool keep)
        {
            if (plan == null) return;
            if (gate.SetKeepLines(keep, plan.KeepLinesRequired)) RebuildLater();
        }

        private static void UpdateButtons()
        {
            if (buyButton == null) return;

            bool can = plan != null && gate.CanBuy(plan, AcquisitionExecutor.IsBusy);
            buyButton.Interactable = can;
            if (can)
            {
                AcqUi.Tip(buyButton, ModL10n.L("acq.preview.buy"), ModL10n.L("acq.tip.buy"));
                return;
            }

            string reason = ModL10n.L(AcqKeys.ReasonUnsupported);
            if (plan != null && plan.BlockingErrors.Count > 0)
            {
                string arg = plan.BlockingArgs.Count > 0 ? plan.BlockingArgs[0] : null;
                reason = ReasonText.Of(AcqUi.Format, plan.BlockingErrors[0], arg);
            }
            else if (gate.Executing)
            {
                reason = ModL10n.L(AcqKeys.ReasonBusy);
            }
            AcqUi.Tip(buyButton, ModL10n.L("acq.tip.closed"), reason);
        }

        // ---- buying ----

        /// <summary>"Buy": the first of the two steps - the question of <c>ModUI.Confirm</c>; the deal itself is the second.</summary>
        private static void OnBuy()
        {
            if (plan == null || model == null || !gate.CanBuy(plan, AcquisitionExecutor.IsBusy))
            {
                UpdateButtons();
                return;
            }

            if (asking) return;   // the question is up already
            asking = true;
            ModUI.Confirm(model.ConfirmTitle, model.ConfirmMessage, OnConfirmed, OnDeclined, "acq.preview.confirm.yes", "core.common.cancel");
        }

        private static void OnDeclined()
        {
            asking = false;
        }

        private static void OnConfirmed()
        {
            asking = false;
            if (plan == null || target == null || !gate.TryBegin(plan, AcquisitionExecutor.IsBusy)) return;   // a second press while the deal runs does nothing

            UpdateButtons();   // locked from here on
            DealOutcome outcome = null;
            try
            {
                outcome = AcquisitionExecutor.Execute(type, uniqueId, saveId, gate.Options(), quote.Price, target.Status);
            }
            finally
            {
                gate.End(outcome != null && outcome.Executed);
            }

            Finish(outcome);
        }

        private static void Finish(DealOutcome outcome)
        {
            if (outcome != null && outcome.Executed)
            {
                bool partial = outcome.Record != null && outcome.Record.partial;
                string name = AcqUi.Plain(target != null ? target.Name : string.Empty);
                AcquisitionsWindow.SetNotice(ModL10n.L(partial ? "acq.notice.donePartial" : "acq.notice.done", name), partial ? ViewTone.Warning : ViewTone.Positive);
                Close();
                AcquisitionsWindow.Reload();
                return;
            }

            // Cancelled before any change: say why, read the game again (it is what changed) and show the deal as it stands now.
            string key = outcome != null ? outcome.CancelReasonKey : null;
            string arg = outcome != null ? outcome.CancelReasonArg : null;
            problem = ReasonText.Of(AcqUi.Format, key, arg);
            if (Load()) Rebuild();
            else ShowGone();
            AcquisitionsWindow.Reload();
        }
    }
}
