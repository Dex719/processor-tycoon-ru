using System;
using System.Collections.Generic;
using PTMod.Core;
using TMPro;
using UnityEngine;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;
using SliderUI = ProcessorTycoon.UI.SliderUI;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The dialog "Выпуск GPU" (design.md, "Диалог «Выпуск GPU»"; FR-4 items 6-8, AC-4.3; TSK-025): a modal window (<c>Window.Modal</c>, so the time stands while it is
    /// open) that opens when a development project is finished. It shows the name and the characteristics of the new card, the price it proposes (0.9 of the budget of the
    /// segment) with an input field and two step buttons, "Продавать на рынке" and the lines (or the cards a day when fabless). "Выпустить" puts the card on the market
    /// (<see cref="GpuLineup.Release"/>, AC-4.3: the card is on sale); "Позже" - or the cross of the window - leaves the card ready, and the tab "Линейка" offers the button
    /// "Выпустить" (FR-4 item 8). When several projects finish together, their cards wait in a queue and the dialog shows them one after the other. What the dialog
    /// shows is the pure <see cref="GpuReleaseModel"/>.
    /// </summary>
    internal static class GpuReleaseDialog
    {
        private const string Feature = GpuFeature.FeatureId;
        private const string WindowId = Feature + ".release";
        private const float BodyHeight = 440f;
        private const float BodyMinHeight = 200f;
        private const float ButtonHeight = 38f;
        private const float ButtonWidth = 220f;

        private static readonly Vector2 WindowSize = new Vector2(640f, 640f);

        private static ModWindow _window;
        private static RectTransform _body;
        private static ButtonUI _releaseButton;
        private static GpuReleaseModel _model;
        private static string _current;
        private static readonly Queue<string> Pending = new Queue<string>();

        private static TMP_Text _priceLabel, _priceNote, _margin, _notice;
        private static TMP_InputField _priceField;
        private static SliderUI _lines;

        /// <summary>The dialog is shown.</summary>
        public static bool IsOpen
        {
            get { return _window != null && _window.IsAlive && _window.IsOpen; }
        }

        /// <summary>
        /// A finished project: its card waits for the release (one frame later, so the tick that finished it stays short). The dialog shows it now or - if it is busy with
        /// another card - after that one.
        /// </summary>
        public static void OnReady(string productId)
        {
            GpuUi.Defer(() => Open(productId));
        }

        /// <summary>
        /// Opens the dialog for a card that is ready (the button "Выпустить" of the lineup, a finished project). Does nothing for a card that is not ready, while the feature
        /// is not active or the templates of the scene are not ready. Never throws.
        /// </summary>
        public static void Open(string productId)
        {
            try
            {
                GpuFeature feature = GpuFeature.Instance;
                if (feature == null || string.IsNullOrEmpty(productId) || !ModFeatures.IsActive(Feature)) return;
                if (!ModUI.IsReady)
                {
                    ModLog.Warn(Feature, "ui: the release dialog cannot be made yet, the templates of the scene are not ready");
                    return;
                }

                if (IsOpen && _current == productId)
                {
                    _window.Open();   // only brings it to the front
                    return;
                }
                if (!Pending.Contains(productId)) Pending.Enqueue(productId);
                if (!IsOpen) ShowNext();
            }
            catch (Exception ex)
            {
                ModLog.Error(Feature, "ui: cannot open the release dialog", ex);
            }
        }

        /// <summary>Closes the dialog and forgets the cards waiting for it (the feature was switched off, the scene is left). The cards stay ready. Never throws.</summary>
        public static void Close()
        {
            try
            {
                Pending.Clear();
                _current = null;
                if (_window != null && _window.IsAlive && _window.IsOpen) _window.Close();
            }
            catch (Exception ex)
            {
                ModLog.Error(Feature, "ui: cannot close the release dialog", ex);
            }
        }

        // ---- the queue ----

        private static void ShowNext()
        {
            while (Pending.Count > 0)
            {
                string id = Pending.Dequeue();
                if (TryShow(id)) return;
            }
        }

        private static bool TryShow(string productId)
        {
            GpuFeature feature = GpuFeature.Instance;
            if (feature == null) return false;

            GpuReleaseModel model = GpuReleaseModel.Build(productId, feature.Lineup, feature.Projects, feature.Designer, feature.Market, feature.Content, feature.Game, GpuUi.Text);
            if (model == null) return false;   // released meanwhile, retired, or lost with a load: nothing to ask about
            if (!EnsureWindow()) return false;

            _model = model;
            _current = productId;
            Rebuild();
            _window.Open();
            return true;
        }

        // ---- the frame ----

        private static bool EnsureWindow()
        {
            if (_window != null && _window.IsAlive && _body != null && _releaseButton != null) return true;
            _window = null;
            _body = null;
            _releaseButton = null;

            ModWindow made = ModUI.CreateWindow(WindowId, WindowTemplate.Modal, "gpu.release.window", WindowSize);
            if (made == null) return false;   // ModUI has logged why

            try
            {
                GpuUi.Clear(made.Content);
                RectTransform scroll = GpuUi.ScrollBody(made.Content, BodyHeight, BodyMinHeight);
                RectTransform buttons = GpuUi.Row(made.Content, "PTModGpuReleaseButtons", ButtonHeight, 12f, true);
                ButtonUI release = ModUI.Button(buttons, ModL10n.L("gpu.release.button"), OnRelease, ButtonStyle.CallToAction, ModL10n.L("gpu.release.button.tip"));
                ButtonUI later = ModUI.Button(buttons, ModL10n.L("gpu.release.later"), OnLater, ButtonStyle.Standard, ModL10n.L("gpu.release.later.tip"));
                if (scroll == null || release == null || later == null) throw new InvalidOperationException("a part of the dialog could not be made (is a widget template of the scene missing?)");

                GpuUi.SetWidth(release, ButtonWidth, 1f);
                GpuUi.SetWidth(later, ButtonWidth, 1f);
                made.Closed += OnWindowClosed;
                _window = made;
                _body = scroll;
                _releaseButton = release;
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Error(Feature, "ui: cannot build the release dialog", ex);
                GpuUi.Clear(made.Content);
                return false;
            }
        }

        // The window went away - by "Выпустить", by "Позже" or by its cross: the next card waits its turn one frame later. A card not released stays ready.
        private static void OnWindowClosed()
        {
            _current = null;
            _model = null;
            if (Pending.Count > 0) GpuUi.Defer(ShowNext);
        }

        // ---- the content ----

        private static void Rebuild()
        {
            if (_body == null || _model == null || _window == null || !_window.IsAlive) return;

            GpuUi.Clear(_body);
            _body.anchoredPosition = Vector2.zero;
            _window.SetTitle(_model.Title);

            GpuUi.Line(_body, ModL10n.L("gpu.release.intro"));
            GpuUi.Rule(_body);
            foreach (GpuFact fact in _model.Facts)
            {
                GpuStatLineView line = GpuStatLineView.Create(_body, fact.Label, fact.Value, GpuTone.Normal, null);
                if (!string.IsNullOrEmpty(fact.Tip)) ModUI.Tooltip(line.Root, fact.Label, fact.Tip);
            }
            GpuUi.Rule(_body);

            GpuUi.Tint(GpuUi.Line(_body, _model.BudgetText), GpuTone.Muted);
            DrawPrice();
            DrawSell();
            DrawLines();

            _notice = GpuUi.Line(_body, string.Empty, true);
            ShowNotice(string.Empty, GpuTone.Normal);
            RefreshPriceTexts();
        }

        private static void DrawPrice()
        {
            RectTransform row = GpuUi.Row(_body, "PTModGpuReleasePrice", 32f, 8f, true);
            TMP_Text label = GpuUi.Line(row, ModL10n.L("gpu.release.price"), true);
            GpuUi.SetWidth(label, 90f, 0f);
            _priceField = GpuInput.Create(row, _model.PriceText, 12, ModL10n.L("gpu.lineup.price.hint"), 130f, OnPriceEdited);
            float step = _model.PriceStep;
            ButtonUI minus = ModUI.Button(row, ModL10n.L("gpu.lineup.price.minus"), () => OnPriceStep(-1), ButtonStyle.Standard, ModL10n.L("gpu.lineup.price.minus.tip", GpuUi.Text.Money(step)));
            ButtonUI plus = ModUI.Button(row, ModL10n.L("gpu.lineup.price.plus"), () => OnPriceStep(1), ButtonStyle.Standard, ModL10n.L("gpu.lineup.price.plus.tip", GpuUi.Text.Money(step)));
            GpuUi.SetWidth(minus, 60f, 0f);
            GpuUi.SetWidth(plus, 60f, 0f);
            _priceLabel = GpuUi.Line(row, string.Empty, true);
            GpuUi.SetWidth(_priceLabel, 0f, 1f);

            _priceNote = GpuUi.Line(_body, string.Empty);
            GpuUi.Tint(_priceNote, GpuTone.Warn);
            _margin = GpuUi.Line(_body, string.Empty);
            GpuUi.Tint(_margin, GpuTone.Muted);
        }

        private static void DrawSell()
        {
            ModUI.Toggle(_body, ModL10n.L("gpu.lineup.sell"), _model.Sell, on => { if (_model != null) _model.Sell = on; }, ModL10n.L("gpu.release.sell.tip"));
        }

        private static void DrawLines()
        {
            GpuSliderRange range = _model.LinesRange;
            if (range.Max <= range.Min)
            {
                GpuUi.Tint(GpuUi.Line(_body, ModL10n.L(_model.Fabless ? "gpu.release.noUnits" : "gpu.release.noLines")), GpuTone.Muted);
                _lines = null;
                return;
            }

            _lines = ModUI.Slider(_body, _model.LinesLabel, range.Min, range.Max, range.Step, _model.Lines, v =>
            {
                if (_model == null) return;
                _model.Lines = (int)Math.Round(v);
                if (_lines != null) _lines.Text = GpuUi.Text.Integer(_model.Lines);   // after ModUI wrote the raw number
            }, ModL10n.L(_model.Fabless ? "gpu.release.unitsPerDay.tip" : "gpu.release.lines.tip"));
            if (_lines != null) _lines.Text = GpuUi.Text.Integer(_model.Lines);
        }

        // The words under the price: the price in money, a note when it will not sell or is below the cost, the cost and the margin.
        private static void RefreshPriceTexts()
        {
            if (_model == null) return;
            GpuUi.Set(_priceLabel, GpuUi.Text.Money(_model.Price));
            GpuUi.Set(_priceNote, _model.PriceNote);
            if (_priceNote != null) _priceNote.gameObject.SetActive(_model.PriceNote.Length > 0);
            GpuUi.Set(_margin, _model.MarginText);
            if (_margin != null) _margin.gameObject.SetActive(_model.MarginText.Length > 0);
        }

        private static void ShowNotice(string text, GpuTone tone)
        {
            if (_notice == null) return;
            _notice.text = text ?? string.Empty;
            _notice.gameObject.SetActive(!string.IsNullOrEmpty(text));
            GpuUi.Retint(_notice, tone);
        }

        // ---- the player's moves ----

        private static void OnPriceEdited(string typed)
        {
            if (_model == null) return;
            if (!_model.SetPriceText(typed)) ShowNotice(ModL10n.L("gpu.lineup.price.bad"), GpuTone.Bad);
            else ShowNotice(string.Empty, GpuTone.Normal);
            if (_priceField != null) _priceField.text = _model.PriceText;
            RefreshPriceTexts();
        }

        private static void OnPriceStep(int direction)
        {
            if (_model == null) return;
            _model.StepPrice(direction);
            if (_priceField != null) _priceField.text = _model.PriceText;
            ShowNotice(string.Empty, GpuTone.Normal);
            RefreshPriceTexts();
        }

        private static void OnRelease()
        {
            if (_model == null) return;

            string reason;
            if (_model.Release(out reason))
            {
                _current = null;   // released: the closing below must not read as "later"
                GpuWindow.RebuildLater();   // the lineup shows the card on sale
                if (_window != null && _window.IsOpen) _window.Close();
                return;
            }
            ShowNotice(reason.Length > 0 ? reason : ModL10n.L("gpu.lineup.block.notReady"), GpuTone.Bad);
        }

        // "Позже": the card stays ready; the lineup offers the button "Выпустить".
        private static void OnLater()
        {
            if (_window != null && _window.IsOpen) _window.Close();
        }
    }
}
