using System;
using System.Collections.Generic;
using PTMod.Core;
using TMPro;
using UnityEngine;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;
using GameAiCompany = ProcessorTycoon.CompanySystem.AICompany;
using GameDataFinder = ProcessorTycoon.DataFinder;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The dialog of a stake (TSK-017; FR-6; design.md "Диалог доли"): the stakes the player can still buy in a company (a dropdown of the shares, each with its price),
    /// what the price means - it goes into the company's account as capital -, the share after the purchase, the dividend by last month's profit, and the switch and
    /// limit of the rescue order (<see cref="RescueOrderControls"/>). "Buy a stake" asks <c>ModUI.Confirm</c> and then <c>StakeService.Buy</c>. What it shows is the pure
    /// <see cref="StakeDialogModel"/>. A modal window of its own over the window "Deals".
    /// </summary>
    internal static class StakeDialog
    {
        private const string Feature = RivalAcquisitionsFeature.FeatureId;
        private const string WindowId = Feature + ".stake";
        private const float BodyHeight = 460f;
        private const float BodyMinHeight = 200f;
        private const float ButtonHeight = 38f;
        private const float ButtonWidth = 200f;

        private static readonly Vector2 WindowSize = new Vector2(640f, 640f);

        private static ModWindow window;
        private static RectTransform body;
        private static ButtonUI buyButton;

        private static int uniqueId;
        private static int saveId;
        private static StakeDialogModel model;
        private static int optionIndex;
        private static string notice;
        private static ViewTone noticeTone;
        private static bool asking;

        internal static bool IsOpen
        {
            get { return window != null && window.IsAlive && window.IsOpen; }
        }

        /// <summary>Opens the dialog of a stake in the company. Does nothing while the feature is not active or the templates of the scene are not ready. Never throws.</summary>
        internal static void Open(int targetUniqueId, int targetSaveId)
        {
            ModHooks.Guard(Feature, "ui/stake", () => OpenCore(targetUniqueId, targetSaveId));
        }

        internal static void Close()
        {
            if (window != null && window.IsAlive && window.IsOpen) window.Close();
        }

        private static void OpenCore(int targetUniqueId, int targetSaveId)
        {
            if (!ModFeatures.IsActive(Feature)) return;
            if (!ModUI.IsReady)
            {
                ModLog.Warn(Feature, "ui: the dialog cannot be made yet, the templates of the scene are not ready");
                return;
            }
            if (!EnsureWindow()) return;

            uniqueId = targetUniqueId;
            saveId = targetSaveId;
            optionIndex = 0;
            notice = null;
            Rebuild();
            window.Open();
        }

        private static bool EnsureWindow()
        {
            if (window != null && window.IsAlive && body != null && buyButton != null) return true;

            window = null;
            body = null;
            buyButton = null;
            ModWindow made = ModUI.CreateWindow(WindowId, WindowTemplate.Modal, "acq.stake.title", WindowSize);
            if (made == null) return false;   // ModUI has logged why

            try
            {
                AcqUi.Clear(made.Content);
                RectTransform scroll = AcqUi.ScrollBody(made.Content, BodyHeight, BodyMinHeight);
                RectTransform buttons = AcqUi.Row(made.Content, "PTAcqStakeDialogButtons", ButtonHeight, 12f);
                ButtonUI buy = ModUI.Button(buttons, ModL10n.L("acq.stake.buy"), OnBuy, ButtonStyle.CallToAction);
                ButtonUI close = ModUI.Button(buttons, ModL10n.L("core.common.close"), Close);
                if (scroll == null || buy == null || close == null) throw new InvalidOperationException("a part of the dialog could not be made (is a widget template of the scene missing?)");

                AcqUi.SetWidth(buy, ButtonWidth, 1f);
                AcqUi.SetWidth(close, ButtonWidth, 1f);
                window = made;
                body = scroll;
                buyButton = buy;
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Error(Feature, "ui: cannot build the stake dialog", ex);
                AcqUi.Clear(made.Content);
                return false;
            }
        }

        // ---- the data ----

        private static StakeDialogModel Read()
        {
            RivalAcquisitionsFeature feature = RivalAcquisitionsFeature.Instance;
            var ai = GameDataFinder.FindCompany(saveId) as GameAiCompany;
            if (feature == null || ai == null || ai.UniqueID != uniqueId) return null;

            PlayerSnapshot player = GameSnapshotAdapter.Player();
            if (player == null) return null;

            CompanySnapshot target = GameSnapshotAdapter.Of(ai, player, feature.State, feature.Balance.Valuation.CpuIncomeMonths);
            double channel = GameSnapshotAdapter.PriceChannelOf(ai);
            return StakeDialogModel.Build(target, player, feature.State, feature.Balance, channel, StakeService.LastMonthProfit(ai));
        }

        private static void Rebuild()
        {
            if (body == null || window == null || !window.IsAlive) return;

            model = Read();
            AcqUi.Clear(body);
            body.anchoredPosition = Vector2.zero;

            if (model == null)
            {
                window.SetTitle(ModL10n.L("acq.stake.title"));
                TMP_Text gone = ModUI.Text(body, ModL10n.L(AcqKeys.ReasonTargetChanged), true);
                AcqUi.Tint(gone, ViewTone.Negative);
                UpdateButton();
                return;
            }

            window.SetTitle(ModL10n.L("acq.stake.titleFor", AcqUi.Plain(model.Name)));
            if (optionIndex >= model.Options.Count) optionIndex = Math.Max(0, model.Options.Count - 1);

            if (!string.IsNullOrEmpty(notice))
            {
                TMP_Text line = ModUI.Text(body, notice, true);
                AcqUi.Tint(line, noticeTone);
            }

            DrawStake();
            DrawRescue();
            UpdateButton();
        }

        private static void RebuildLater()
        {
            AcqUi.Defer(() =>
            {
                if (IsOpen) Rebuild();
            });
        }

        // ---- the stake ----

        private static void DrawStake()
        {
            ViewFormat f = AcqUi.Format;
            ModUI.Text(body, ModL10n.L("acq.stake.section"), true);

            ModUI.Text(body, ModL10n.L("acq.stake.company", f.M(model.TargetMoney), f.P(model.OwnedShare)));

            if (!model.CanBuy)
            {
                TMP_Text closed = ModUI.Text(body, ReasonText.Of(f, model.ReasonKey, model.ReasonArg));
                AcqUi.Tint(closed, ViewTone.Warning);
                return;
            }

            var labels = new List<string>();
            foreach (StakeOption option in model.Options) labels.Add(ModL10n.L("acq.stake.option", f.P(option.Share), f.M(option.Price)));
            ModUI.Dropdown(body, ModL10n.L("acq.stake.share"), labels, optionIndex, index =>
            {
                optionIndex = index;
                RebuildLater();
            });

            StakeOption chosen = model.Options[optionIndex];
            ModUI.Info(body, ModL10n.L("acq.stake.price"), f.M(chosen.Price), ModL10n.L("acq.stake.price.tip"));
            ModUI.Info(body, ModL10n.L("acq.stake.balanceAfter"), f.M(chosen.MoneyAfter));
            ModUI.Info(body, ModL10n.L("acq.stake.shareAfter"), f.P(chosen.ShareAfter));
            ModUI.Info(body, ModL10n.L("acq.stake.expectedDividend"), f.M(chosen.ExpectedDividend), ModL10n.L("acq.stake.expectedDividend.tip", f.M(model.LastMonthProfit)));

            if (model.InDefault)
            {
                TMP_Text cheaper = ModUI.Text(body, ModL10n.L("acq.stake.inDefault"));
                AcqUi.Tint(cheaper, ViewTone.Warning);
            }

            if (!chosen.Affordable)
            {
                TMP_Text noCash = ModUI.Text(body, ReasonText.Of(f, chosen.ReasonKey, chosen.ReasonArg));
                AcqUi.Tint(noCash, ViewTone.Negative);
            }
        }

        private static void DrawRescue()
        {
            ModUI.Text(body, ModL10n.L("acq.stake.rescueSection"), true);
            if (!model.OrderAvailable)
            {
                TMP_Text closed = ModUI.Text(body, ReasonText.Of(AcqUi.Format, model.OrderReasonKey, null));
                AcqUi.Tint(closed, ViewTone.Warning);
                return;
            }

            RescueOrderControls.Build(body, model.UniqueId, model.SaveId, model.CreditLimit, model.OrderActive, model.OrderMaxAmount, true, null);
        }

        private static void UpdateButton()
        {
            if (buyButton == null) return;

            StakeOption chosen = model != null && model.CanBuy && optionIndex < model.Options.Count ? model.Options[optionIndex] : null;
            bool can = chosen != null && chosen.Affordable && !AcquisitionExecutor.IsBusy;
            buyButton.Interactable = can;

            if (can) AcqUi.Tip(buyButton, ModL10n.L("acq.stake.buy"), ModL10n.L("acq.tip.buyStake"));
            else if (chosen != null) AcqUi.Tip(buyButton, ModL10n.L("acq.tip.closed"), ReasonText.Of(AcqUi.Format, chosen.ReasonKey, chosen.ReasonArg));
            else if (model != null) AcqUi.Tip(buyButton, ModL10n.L("acq.tip.closed"), ReasonText.Of(AcqUi.Format, model.ReasonKey, model.ReasonArg));
        }

        // ---- buying ----

        private static void OnBuy()
        {
            if (model == null || !model.CanBuy || optionIndex >= model.Options.Count) return;

            StakeOption chosen = model.Options[optionIndex];
            if (!chosen.Affordable) return;

            ViewFormat f = AcqUi.Format;
            string message = ModL10n.L("acq.stake.confirm.message", f.P(chosen.Share), AcqUi.Plain(model.Name), f.M(chosen.Price), f.M(chosen.MoneyAfter));
            if (asking) return;   // the question is up already: a second press must not queue a second purchase
            asking = true;
            double share = chosen.Share;
            ModUI.Confirm(ModL10n.L("acq.stake.confirm.title"), message, () =>
            {
                asking = false;
                OnConfirmed(share);
            }, () => asking = false, "acq.stake.confirm.yes", "core.common.cancel");
        }

        private static void OnConfirmed(double share)
        {
            RivalAcquisitionsFeature feature = RivalAcquisitionsFeature.Instance;
            var ai = GameDataFinder.FindCompany(saveId) as GameAiCompany;
            if (feature == null || feature.Stakes == null || ai == null || ai.UniqueID != uniqueId)
            {
                notice = ModL10n.L(AcqKeys.ReasonTargetChanged);
                noticeTone = ViewTone.Negative;
                Rebuild();
                return;
            }

            StakeOutcome outcome = feature.Stakes.Buy(ai, share);
            if (outcome.Ok)
            {
                string text = ModL10n.L("acq.notice.stakeBought", AcqUi.Plain(ai.Name), AcqUi.Format.P(outcome.ShareNow));
                notice = text;
                noticeTone = ViewTone.Positive;
                AcquisitionsWindow.SetNotice(text, ViewTone.Positive);
                optionIndex = 0;
            }
            else
            {
                notice = ReasonText.Of(AcqUi.Format, outcome.ReasonKey, outcome.ReasonArg);
                noticeTone = ViewTone.Negative;
            }

            Rebuild();
            AcquisitionsWindow.Reload();
        }
    }
}
