using System;
using PTMod.Core;
using TMPro;
using UnityEngine;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The dialog of the founding (design.md, "Окно «Видеокарты»", "До основания"; FR-1 items 3-6 and 9; TSK-022): a modal window (<c>Window.Modal</c>, so the time stands
    /// while it is open) that shows what the division costs (<c>founding.cost x inflation</c>), the money the player would be left with, the parts of the tree that
    /// open at once, and - when the button is locked - the first reason: the market opens in a later year (AC-1.3), the credit limit would be crossed (AC-1.4), the
    /// company is a pure foundry (AC-1.5). The button asks <c>ModUI.Confirm</c> and then founds the division (<see cref="GpuDivision.Found"/>); the window "Видеокарты"
    /// opens on success. What the dialog shows is the pure <see cref="GpuFoundModel"/>.
    /// </summary>
    internal static class GpuFoundDialog
    {
        private const string Feature = GpuFeature.FeatureId;
        private const string WindowId = Feature + ".found";
        private const float BodyHeight = 400f;
        private const float BodyMinHeight = 160f;
        private const float ButtonHeight = 38f;
        private const float ButtonWidth = 220f;

        private static readonly Vector2 WindowSize = new Vector2(580f, 580f);

        private static ModWindow _window;
        private static RectTransform _body;
        private static ButtonUI _foundButton;
        private static GpuFoundModel _model;
        private static GpuTab _then = GpuTab.Research;
        private static bool _asking;

        /// <summary>The dialog is shown.</summary>
        public static bool IsOpen
        {
            get { return _window != null && _window.IsAlive && _window.IsOpen; }
        }

        /// <summary>
        /// Opens the dialog; the tab to open the window on after the founding is <paramref name="thenOpen"/> (<see cref="GpuTab.Auto"/> gives the first tab: nothing was open
        /// before). Does nothing while the feature is not active or the templates of the scene are not ready. Never throws.
        /// </summary>
        public static void Open(GpuTab thenOpen = GpuTab.Auto)
        {
            try
            {
                GpuFeature feature = GpuFeature.Instance;
                if (feature == null || !ModFeatures.IsActive(Feature)) return;
                if (!ModUI.IsReady)
                {
                    ModLog.Warn(Feature, "ui: the dialog cannot be made yet, the templates of the scene are not ready");
                    return;
                }
                if (!EnsureWindow()) return;

                _then = thenOpen == GpuTab.Auto ? GpuTab.Research : thenOpen;
                Rebuild(feature);
                _window.Open();
            }
            catch (Exception ex)
            {
                ModLog.Error(Feature, "ui: cannot open the dialog of the founding", ex);
            }
        }

        /// <summary>Closes the dialog when it is open. Never throws.</summary>
        public static void Close()
        {
            try
            {
                if (_window != null && _window.IsAlive && _window.IsOpen) _window.Close();
            }
            catch (Exception ex)
            {
                ModLog.Error(Feature, "ui: cannot close the dialog of the founding", ex);
            }
        }

        private static bool EnsureWindow()
        {
            if (_window != null && _window.IsAlive && _body != null && _foundButton != null) return true;
            _window = null;
            _body = null;
            _foundButton = null;

            ModWindow made = ModUI.CreateWindow(WindowId, WindowTemplate.Modal, "gpu.found.title", WindowSize);
            if (made == null) return false;   // ModUI has logged why

            try
            {
                GpuUi.Clear(made.Content);
                RectTransform scroll = GpuUi.ScrollBody(made.Content, BodyHeight, BodyMinHeight);
                RectTransform buttons = GpuUi.Row(made.Content, "PTModGpuFoundButtons", ButtonHeight, 12f, true);
                ButtonUI found = ModUI.Button(buttons, ModL10n.L("gpu.found.button"), OnFound, ButtonStyle.CallToAction);
                ButtonUI later = ModUI.Button(buttons, ModL10n.L("gpu.found.later"), Close);
                if (scroll == null || found == null || later == null) throw new InvalidOperationException("a part of the dialog could not be made (is a widget template of the scene missing?)");

                GpuUi.SetWidth(found, ButtonWidth, 1f);
                GpuUi.SetWidth(later, ButtonWidth, 1f);
                _window = made;
                _body = scroll;
                _foundButton = found;
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Error(Feature, "ui: cannot build the dialog of the founding", ex);
                GpuUi.Clear(made.Content);
                return false;
            }
        }

        private static void Rebuild(GpuFeature feature)
        {
            if (_body == null || _window == null || !_window.IsAlive) return;

            _model = GpuFoundModel.Build(feature.Division, feature.Content, feature.Game, GpuUi.Text);
            GpuUi.Clear(_body);
            _body.anchoredPosition = Vector2.zero;

            GpuUi.Line(_body, _model.Intro);
            GpuUi.Rule(_body);
            GpuUi.Line(_body, _model.CostLine, true);
            GpuUi.Line(_body, _model.MoneyLine);
            if (_model.ReasonText.Length > 0)
            {
                TMP_Text reason = GpuUi.Line(_body, _model.ReasonText, true);
                GpuUi.Tint(reason, GpuTone.Bad);
            }
            GpuUi.Rule(_body);
            GpuUi.Line(_body, _model.NodesTitle, true);
            if (_model.NodeLines.Count == 0)
            {
                GpuUi.Tint(GpuUi.Line(_body, ModL10n.L("gpu.found.nodesNone")), GpuTone.Muted);
            }
            foreach (string line in _model.NodeLines) GpuUi.Line(_body, line);

            if (_foundButton != null)
            {
                _foundButton.Interactable = _model.CanFound;
                string body = _model.CanFound ? ModL10n.L("gpu.found.button.tip") : _model.ReasonText;
                GpuUi.Tip(_foundButton, _model.Title, body);
            }
        }

        // The button asks first (AC-1.1: "нажимает и подтверждает"), then founds.
        private static void OnFound()
        {
            GpuFeature feature = GpuFeature.Instance;
            if (feature == null || _model == null || !_model.CanFound || _asking) return;

            _asking = true;
            ModUI.Confirm(_model.ConfirmTitle, _model.ConfirmMessage, () =>
            {
                _asking = false;
                Found();
            }, () => _asking = false, "gpu.found.button", "core.common.cancel");
        }

        private static void Found()
        {
            GpuFeature feature = GpuFeature.Instance;
            if (feature == null) return;

            if (feature.Division.Found())
            {
                Close();
                GpuWindow.Open(_then);
                return;
            }
            Rebuild(feature);   // something changed while the question was open: the dialog says why the founding is locked now
        }
    }
}
