using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using UnityEngine;
using UnityEngine.UI;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;
using GameContract = ProcessorTycoon.ContractSystem.Contract;
using GameContractManager = ProcessorTycoon.ContractSystem.ContractManager;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GamePlayer = ProcessorTycoon.Player;
using ToggleUI = ProcessorTycoon.UI.ToggleUI;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// The modal review screen before a manual offer (design.md, Component 11; FR-3, FR-4; NFR-5): the contract and the offer side by side, the warnings under them, "Send offer" and "Cancel".
    /// <list type="bullet">
    /// <item><b>Window.</b> <c>WindowTemplate.Modal</c>: the time stands while it is open (the pause trigger of the template). 600 x 540, no scrolling: two columns of at most seven short lines,
    /// the warnings (at most seven) and the buttons fit 1280 x 720.</item>
    /// <item><b>Answers.</b> "Send offer" closes the window and gives the offer to <see cref="OfferGate.ConfirmAndSend"/> (after a check that the contract still exists); "Cancel", the cross of the
    /// window and the end of the scene write "cancelled on the review screen" (<see cref="OfferGate.RecordCancelled"/>) and send nothing. Whatever closes the window answers exactly once.</item>
    /// <item><b>"Don't ask again for safe offers"</b> is shown in the mode "always" only (in the other modes the screen opens for a risk alone): checking it sets the mode to "only risky" at once,
    /// unchecking it takes the mode back.</item>
    /// <item><b>Switching off.</b> The core closes the windows of a feature that stops; the close is a cancel, nothing is sent (FR-13 item 4).</item>
    /// </list>
    /// <see cref="Open"/> returns false when the window cannot be shown; <see cref="OfferReviewScreen"/> then falls back to the plain question and, failing that, the offer goes the vanilla way.
    /// To check in the game (the owner, M2, M3): the warnings are those of the contract; "Cancel" leaves "Send offer" in the contract window and writes the line in the log; "Send offer" turns the button
    /// into "Cancel offer"; the time stands while the screen is up.
    /// </summary>
    internal static class OfferReviewDialog
    {
        private const string Feature = ContractParamSpecs.FeatureId;
        private const string WindowId = Feature + ".review";
        private const string ClosedSite = "OfferReviewDialog/Closed";
        private const string SendSite = "OfferReviewDialog/Send";
        private const string ThemeSite = "OfferReviewDialog/ThemeChanged";

        private static readonly Vector2 WindowSize = new Vector2(600f, 540f);
        private const float ColumnWidth = 270f;
        private const float ButtonHeight = 36f;

        /// <summary>The offer the open screen asks about; null once it has been answered.</summary>
        private sealed class Pending
        {
            internal GameContract Contract;
            internal int CpuId;
            internal OfferEvaluation Evaluation;
        }

        private static ModWindow _window;
        private static RectTransform _left;
        private static RectTransform _right;
        private static RectTransform _risks;
        private static GameObject _dontAskRoot;
        private static ToggleUI _dontAsk;
        private static Pending _pending;
        private static bool _modeChangedHere;
        private static bool _themeHooked;

        /// <summary>The screen is on the screen.</summary>
        internal static bool IsOpen
        {
            get { return _window != null && _window.IsAlive && _window.IsOpen; }
        }

        /// <summary>
        /// Shows the screen for an analysed offer. True when it is up (the call of the game is held back until the player answers); false when it could not be shown, and nothing was left open.
        /// </summary>
        internal static bool Open(OfferEvaluation evaluation)
        {
            if (evaluation == null || !ModUI.IsReady) return false;
            if (!EnsureWindow()) return false;

            CancelPending();   // a screen cannot be up twice (the time stands), but a leftover would send nothing and say nothing

            evaluation.Ensure();
            _pending = new Pending { Contract = evaluation.Contract, CpuId = evaluation.Cpu.SaveID, Evaluation = evaluation };
            try
            {
                FillLines(evaluation);
                ResetSwitch();
                _window.Open();
            }
            catch (Exception ex)
            {
                _pending = null;
                ModLog.Error(Feature, "ui: the review screen could not be filled", ex);
                if (_window.IsOpen) _window.Close();
                return false;
            }

            if (_window.IsOpen) return true;
            _pending = null;   // the window refused to open
            return false;
        }

        /// <summary>Closes the screen when it is open; that is a cancel (nothing is sent).</summary>
        internal static void Close()
        {
            if (_window != null && _window.IsAlive && _window.IsOpen) _window.Close();
        }

        // ---- the window ----

        private static bool EnsureWindow()
        {
            if (_window != null && _window.IsAlive && _left != null) return true;
            _window = null;
            _left = null;
            _right = null;
            _risks = null;
            _dontAskRoot = null;
            _dontAsk = null;

            ModWindow window = ModUI.CreateWindow(WindowId, WindowTemplate.Modal, "ct.review.title", WindowSize);
            if (window == null) return false;   // ModUI has said why

            try
            {
                RectTransform content = window.Content;
                UiKit.Clear(content);   // the window of an earlier attempt that failed halfway is the same object: start from an empty frame
                RectTransform columns = UiKit.Row(content, 0f, 14f, TextAnchor.UpperLeft);
                _left = Column(columns);
                _right = Column(columns);
                _risks = ModUI.VerticalList(content, 3f);

                ToggleUI toggle = ModUI.Toggle(content, ModL10n.L("ct.review.dontAsk"), false, OnDontAskChanged);
                _dontAsk = toggle;
                _dontAskRoot = UiKit.RootOf(toggle, content);

                RectTransform buttons = UiKit.Row(content, ButtonHeight, 12f, TextAnchor.MiddleCenter);
                ButtonUI send = ModUI.Button(buttons, ModL10n.L("ct.btn.send"), OnSend, ButtonStyle.CallToAction);
                ButtonUI cancel = ModUI.Button(buttons, ModL10n.L("core.common.cancel"), OnCancel);
                if (_left == null || _right == null || _risks == null || send == null || cancel == null)
                    throw new InvalidOperationException("a part of the screen could not be made (is a widget template of the scene missing?)");
                UiKit.SetWidth(send, 160f, 1f);
                UiKit.SetWidth(cancel, 160f, 1f);
            }
            catch (Exception ex)
            {
                ModLog.Error(Feature, "ui: the review screen could not be built", ex);
                _left = null;
                if (window.IsOpen) window.Close();
                return false;
            }

            window.Closed += OnWindowClosed;
            _window = window;
            if (!_themeHooked)
            {
                _themeHooked = true;
                ModUI.ThemeChanged += OnThemeChanged;
            }
            return true;
        }

        private static RectTransform Column(Transform columns)
        {
            RectTransform list = ModUI.VerticalList(columns, 4f);
            if (list == null) return null;

            LayoutElement layout = list.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.minWidth = 0f;
                layout.preferredWidth = ColumnWidth;
                layout.flexibleWidth = 1f;
            }
            return list;
        }

        // ---- the contents ----

        private static void FillLines(OfferEvaluation evaluation)
        {
            UiKit.Clear(_left);
            UiKit.Clear(_right);
            UiKit.Clear(_risks);

            ReviewPlan plan = ReviewLines.Build(InputOf(evaluation));
            GameArgFormatter formatter = GameArgFormatter.Current();
            UiKit.AddLines(_left, plan.Contract, formatter);
            UiKit.AddLines(_right, plan.Offer, formatter);
            UiKit.AddLines(_risks, plan.Risks, formatter);
        }

        private static void ResetSwitch()
        {
            // The switch makes sense where every offer is asked about: in the other modes the screen opens for a risk, and the safe ones go through already.
            bool show = ConfirmPolicy.DontAskIsShown(ContractTransparencySettings.ConfirmMode);
            if (_dontAskRoot != null) _dontAskRoot.SetActive(show);
            if (_dontAsk != null) ModUI.SetWithoutNotify(_dontAsk, false);
            _modeChangedHere = false;
        }

        private static ReviewInput InputOf(OfferEvaluation evaluation)
        {
            GameContract contract = evaluation.Contract;
            GameCpu cpu = evaluation.Cpu;
            CommitmentReport report = evaluation.Commitments;
            GamePlayer player = GamePlayer.Instance;

            var input = new ReviewInput
            {
                ContractLabel = ContractInspector.ContractLabel(contract),
                CpuName = cpu.Name,
                Units = contract.Units,
                PaymentPerUnit = contract.PaymentPerUnit,
                FinePerUnit = contract.FinePerUnit,
                ReviewDateText = contract.AcceptanceDate == null ? string.Empty : UiKit.MonthYearText((DateTime)contract.AcceptanceDate),
                EndDateText = contract.TerminationDate == null ? string.Empty : UiKit.MonthYearText((DateTime)contract.TerminationDate),
                TermMonths = TermMonths(contract),
                Commitments = report,
                ThresholdPercent = player == null ? 0 : player.ContractProfitMargin,
                GameChance = evaluation.Odds.CandidateGameChance,
                Actual = evaluation.Odds.CandidateActual,
                CompanyCash = cpu.Company == null ? 0.0 : cpu.Company.MoneyAmount,
            };

            return input;
        }

        private static int TermMonths(GameContract contract)
        {
            if (contract.AcceptanceDate == null || contract.TerminationDate == null) return 0;
            var accept = (DateTime)contract.AcceptanceDate;
            var end = (DateTime)contract.TerminationDate;
            return Math.Max(0, (end.Year - accept.Year) * 12 + (end.Month - accept.Month));
        }

        // ---- the answers ----

        private static void OnSend()
        {
            ContractTransparencyFeature.Guarded(SendSite, () =>
            {
                Pending answered = _pending;
                if (answered == null) return;
                _pending = null;   // answered: closing the window must not count as a cancel
                if (_window != null && _window.IsOpen) _window.Close();

                GameContractManager manager = GameContractManager.Instance;
                if (manager != null && !manager.ContractExists(answered.Contract))
                {
                    ModNotify.ShowKey(Feature, "ct.review.contractGone");
                    return;
                }
                if (!OfferGate.ConfirmAndSend(answered.Contract, answered.CpuId, OfferSource.Manual))
                    ModNotify.ShowKey(Feature, "ct.review.notSent");
            });
        }

        private static void OnCancel()
        {
            if (_window != null && _window.IsOpen) _window.Close();   // the Closed event does the rest, as for the cross
        }

        /// <summary>The window closed by the cancel button, the cross, the end of the scene or the switching off of the feature: the offer was not confirmed.</summary>
        private static void OnWindowClosed()
        {
            ContractTransparencyFeature.Guarded(ClosedSite, CancelPending);
        }

        private static void CancelPending()
        {
            Pending cancelled = _pending;
            if (cancelled == null) return;
            _pending = null;
            OfferGate.RecordCancelled(cancelled.Contract, cancelled.CpuId);
        }

        private static void OnDontAskChanged(bool on)
        {
            ConfirmMode current = ContractTransparencySettings.ConfirmMode;
            bool changedHere;
            ConfirmMode next = ConfirmPolicy.AfterDontAsk(current, on, _modeChangedHere, out changedHere);
            _modeChangedHere = changedHere;
            if (next != current) ContractTransparencySettings.SetConfirmMode(next);
        }

        private static void OnThemeChanged()
        {
            if (!IsOpen || _pending == null) return;
            Pending pending = _pending;
            ContractTransparencyFeature.Guarded(ThemeSite, () => FillLines(pending.Evaluation));
        }
    }
}
