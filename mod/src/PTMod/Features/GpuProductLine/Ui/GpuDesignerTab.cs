using System;
using System.Collections.Generic;
using System.Diagnostics;
using PTMod.Core;
using TMPro;
using UnityEngine;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;
using DropdownUI = ProcessorTycoon.UI.DropdownUI;
using SliderUI = ProcessorTycoon.UI.SliderUI;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The tab "Конструктор" (design.md, "Вкладка «Конструктор»"; FR-3, FR-4, FR-17; TSK-024). Left: the dropdowns of the architecture, the process node, the memory
    /// type, the bus and the cooler, the sliders of the die area, the clock (a share of the highest clock of the architecture) and the video memory (powers of two),
    /// the name of the card. Right: the characteristics, each with a tooltip that says the formula in plain words. Below: the warnings ("Упор в память", "Перегрев",
    /// the reason the design cannot be built), the table "Оценка по сегментам", the line of the co-development discount, and the button "Разработать" that starts the
    /// project. Everything is recomputed on the events of the controls (<c>OnValueChanged</c>) and never from a timer or a frame (NFR-1): the pure
    /// <see cref="GpuDesignerModel"/> does the work, this class sets its inputs and draws what it answers. The model outlives the tab, so a design in progress is still
    /// there when the player comes back from another tab; the memory type changes the bus and the video memory on offer, so that choice rebuilds the tab.
    /// </summary>
    internal sealed class GpuDesignerTab : IGpuTabView
    {
        private const float ColumnSpacing = 18f;
        private const float RowHeight = 24f;

        // the design in progress, kept while the window is open (and across tabs)
        private static GpuDesignerModel _kept;
        private static GpuFeature _keptFor;

        private readonly RectTransform _body;
        private readonly GpuFeature _feature;
        private readonly GpuDesignerModel _model;
        private readonly Dictionary<string, GpuStatLineView> _stats = new Dictionary<string, GpuStatLineView>(StringComparer.Ordinal);
        private readonly string _parts;

        private SliderUI _dieSlider, _clockSlider, _vramSlider;
        private TMP_Text _coDev, _notice;
        private RectTransform _warnings, _segments;
        private ButtonUI _developButton;
        private TMP_InputField _nameField;
        private int _measured;
        private string _segmentShape = string.Empty;
        private readonly List<TMP_Text[]> _segmentCells = new List<TMP_Text[]>();

        private GpuDesignerTab(RectTransform body, GpuFeature feature, GpuDesignerModel model)
        {
            _body = body;
            _feature = feature;
            _model = model;
            _parts = model.PartsSignature;
        }

        /// <summary>Builds the tab in the (emptied) body; the view that refreshes it each day.</summary>
        public static IGpuTabView Build(RectTransform body, GpuFeature feature)
        {
            if (_kept == null || !ReferenceEquals(_keptFor, feature))
            {
                _kept = new GpuDesignerModel(feature.Content, feature.Research, feature.Designer, feature.Projects, feature.Market, feature.Game, GpuUi.Text);
                _keptFor = feature;
            }
            else
            {
                _kept.RefreshParts();
            }

            var tab = new GpuDesignerTab(body, feature, _kept);
            tab.Draw();
            return tab;
        }

        /// <summary>The design in progress is dropped (a save was loaded, the feature was switched off).</summary>
        internal static void Forget()
        {
            _kept = null;
            _keptFor = null;
        }

        public void RefreshDaily()
        {
            // Nothing is recomputed on a timer: only the shape is checked - a part researched, a new year - and the state of the button.
            if (_model.PeekPartsSignature() != _parts)
            {
                GpuWindow.RebuildLater();
                return;
            }
            _model.RefreshDevelopState();
            UpdateDevelopButton();
        }

        // ---- drawing ----

        private void Draw()
        {
            if (!_model.HasParts)
            {
                GpuUi.Tint(GpuUi.Line(_body, _model.MissingPartsText, true), GpuTone.Warn);
                return;
            }

            RectTransform[] columns;
            GpuUi.Columns(_body, "PTModGpuDesigner", 2, ColumnSpacing, out columns);
            DrawInputs(columns[0]);
            DrawStats(columns[1]);

            _warnings = GpuUi.Stack(_body, "PTModGpuWarnings", 2f, 0);
            _coDev = GpuUi.Line(_body, string.Empty);
            GpuUi.Tint(_coDev, GpuTone.Good);

            GpuUi.Heading(_body, ModL10n.L("gpu.design.segments"));
            _segments = GpuUi.Stack(_body, "PTModGpuSegments", 2f, 0);

            DrawCreate();
            UpdateOutputs();
        }

        private void DrawInputs(RectTransform column)
        {
            GpuUi.Line(column, ModL10n.L("gpu.design.inputs"), true);

            ModUI.Dropdown(column, ModL10n.L("gpu.design.arch"), _model.ArchLabels(), _model.ArchIndex, i =>
            {
                Timed(() => _model.SelectArch(i));
                UpdateOutputs();
            }, ModL10n.L("gpu.design.arch.tip"));

            ModUI.Dropdown(column, ModL10n.L("gpu.design.node"), _model.NodeLabels(), _model.NodeIndex, i =>
            {
                Timed(() => _model.SelectNode(i));
                UpdateOutputs();
            }, ModL10n.L("gpu.design.node.tip"));

            // the memory type decides the buses and the video memory on offer: the tab is built again with the new lists
            ModUI.Dropdown(column, ModL10n.L("gpu.design.memory"), _model.MemoryLabels(), _model.MemoryIndex, i =>
            {
                Timed(() => _model.SelectMemory(i));
                GpuWindow.RebuildLater();
            }, ModL10n.L("gpu.design.memory.tip"));

            ModUI.Dropdown(column, ModL10n.L("gpu.design.bus"), _model.BusLabels(), _model.BusIndex, i =>
            {
                Timed(() => _model.SelectBus(i));
                UpdateOutputs();
            }, ModL10n.L("gpu.design.bus.tip"));

            ModUI.Dropdown(column, ModL10n.L("gpu.design.cooler"), _model.CoolerLabels(), _model.CoolerIndex, i =>
            {
                Timed(() => _model.SelectCooler(i));
                UpdateOutputs();
            }, ModL10n.L("gpu.design.cooler.tip"));

            GpuSliderRange die = _model.DieRange;
            _dieSlider = ModUI.Slider(column, ModL10n.L("gpu.design.die"), die.Min, die.Max, die.Step, _model.Die, v =>
            {
                Timed(() => _model.SetDie(v));
                UpdateOutputs();
            }, ModL10n.L("gpu.design.die.tip"));

            _clockSlider = ModUI.Slider(column, ModL10n.L("gpu.design.clock"), _model.ClockMinPercent, 100f, 1f, _model.ClockPercent, v =>
            {
                Timed(() => _model.SetClockPercent((int)Math.Round(v)));
                UpdateOutputs();
            }, ModL10n.L("gpu.design.clock.tip"));

            IReadOnlyList<float> vram = _model.VramValues;
            _vramSlider = ModUI.Slider(column, ModL10n.L("gpu.design.vram"), 0f, Math.Max(0, vram.Count - 1), 1f, _model.VramIndex, v =>
            {
                Timed(() => _model.SetVramIndex((int)Math.Round(v)));
                UpdateOutputs();
            }, ModL10n.L("gpu.design.vram.tip"));
            if (_vramSlider != null && vram.Count <= 1) _vramSlider.Interactable = false;

            RectTransform nameRow = GpuUi.Row(column, "PTModGpuNameRow", 30f, 8f, true);
            TMP_Text nameLabel = ModUI.Text(nameRow, ModL10n.L("gpu.design.name"));
            GpuUi.SetWidth(nameLabel, 0f, 1f);
            _nameField = GpuInput.Create(nameRow, _model.Name, _model.NameMaxLength, ModL10n.L("gpu.design.name.hint", _model.NameMaxLength), 250f, OnNameEdited);
        }

        private void DrawStats(RectTransform column)
        {
            GpuUi.Line(column, ModL10n.L("gpu.design.stats"), true);
            foreach (GpuStatLine line in _model.Lines)
            {
                string key = line.Key;
                GpuStatLineView view = GpuStatLineView.Create(column, line.Label, line.Value, line.Tone, () => TipOf(key));
                _stats[key] = view;
            }
        }

        private ModUI.TooltipText TipOf(string key)
        {
            foreach (GpuStatLine line in _model.Lines)
            {
                if (line.Key == key) return new ModUI.TooltipText(line.TipHeader, line.TipBody);
            }
            return new ModUI.TooltipText(string.Empty, string.Empty);
        }

        private void DrawCreate()
        {
            _notice = GpuUi.Line(_body, string.Empty, true);
            RectTransform row = GpuUi.Row(_body, "PTModGpuCreateRow", 38f, 12f, true);
            _developButton = ModUI.Button(row, ModL10n.L("gpu.design.create"), OnDevelop, ButtonStyle.CallToAction);
            if (_developButton != null) GpuUi.SetWidth(_developButton, 260f, 0f);
            ShowNotice(string.Empty, GpuTone.Normal);
        }

        // ---- showing the answers of the model ----

        // The answers of the model on the screen: the sliders' captions, the characteristics, the warnings, the table, the line of the discount, the button.
        private void UpdateOutputs()
        {
            UpdateSliderCaptions();

            foreach (GpuStatLine line in _model.Lines)
            {
                GpuStatLineView view;
                if (_stats.TryGetValue(line.Key, out view)) view.Set(line.Value, line.Tone);
            }

            GpuUi.Clear(_warnings);
            foreach (GpuWarningLine warning in _model.Warnings)
            {
                TMP_Text text = GpuUi.Line(_warnings, warning.Text, true);
                GpuUi.Tint(text, warning.Tone);
            }

            if (_coDev != null)
            {
                _coDev.text = _model.CoDevText;
                _coDev.gameObject.SetActive(_model.CoDevText.Length > 0);
            }

            DrawSegmentTable();
            UpdateDevelopButton();
        }

        // ModUI writes the raw number under a slider after each move; the caption of the tab (the value with its unit) is put over it afterwards.
        private void UpdateSliderCaptions()
        {
            if (_dieSlider != null) _dieSlider.Text = ModL10n.L("gpu.design.val.mm2", GpuUiMath.Plain(_model.Die));
            if (_clockSlider != null) _clockSlider.Text = ModL10n.L("gpu.design.val.clock", GpuUi.Text.Frequency(_model.ClockMHz), _model.ClockPercent);
            if (_vramSlider != null) _vramSlider.Text = ModL10n.L("gpu.design.val.mb", GpuUiMath.Plain(_model.VramMB));
        }

        // The table by segment is drawn once for a set of segments and written over on each change of an input: dragging a slider makes no new text objects.
        private void DrawSegmentTable()
        {
            string shape = ShapeOfSegments();
            if (shape == _segmentShape && _segmentCells.Count == _model.Segments.Count)
            {
                for (int i = 0; i < _segmentCells.Count; i++) WriteSegmentRow(_segmentCells[i], _model.Segments[i]);
                return;
            }

            GpuUi.Clear(_segments);
            _segmentCells.Clear();
            _segmentShape = shape;
            if (_model.Segments.Count == 0)
            {
                GpuUi.Tint(GpuUi.Line(_segments, ModL10n.L("gpu.design.segments.none")), GpuTone.Muted);
                return;
            }

            RectTransform head = GpuUi.Row(_segments, "PTModGpuSegHead", RowHeight, 8f, false);
            Columns(head, ModL10n.L("gpu.design.seg.col.name"), ModL10n.L("gpu.design.seg.col.rating"), ModL10n.L("gpu.design.seg.col.features"),
                ModL10n.L("gpu.design.seg.col.vram"), ModL10n.L("gpu.design.seg.col.budget"), true);
            foreach (GpuSegmentRow row in _model.Segments)
            {
                RectTransform line = GpuUi.Row(_segments, "PTModGpuSegRow", RowHeight, 8f, false);
                TMP_Text[] cells = Columns(line, row.Name, row.RatingText, row.FeaturesText, row.VramText, row.BudgetText, false);
                _segmentCells.Add(cells);
                WriteSegmentRow(cells, row);
            }
        }

        private string ShapeOfSegments()
        {
            var shape = new System.Text.StringBuilder();
            foreach (GpuSegmentRow row in _model.Segments) shape.Append(row.SegmentId).Append('|');
            return shape.ToString();
        }

        private static void WriteSegmentRow(TMP_Text[] cells, GpuSegmentRow row)
        {
            GpuUi.Set(cells[0], row.Name);
            GpuUi.Set(cells[1], row.RatingText);
            GpuUi.Retint(cells[1], row.RatingTone);
            GpuUi.Set(cells[2], row.FeaturesText);
            GpuUi.Retint(cells[2], row.FeaturesTone);
            GpuUi.Set(cells[3], row.VramText);
            GpuUi.Retint(cells[3], row.VramTone);
            GpuUi.Set(cells[4], row.BudgetText);
        }

        private static TMP_Text[] Columns(Transform row, string name, string rating, string features, string vram, string budget, bool bold)
        {
            TMP_Text[] cells = new TMP_Text[5];
            cells[0] = GpuUi.Line(row, name, bold);
            GpuUi.SetWidth(cells[0], 0f, 1f);
            cells[1] = GpuUi.Cell(row, rating, 130f, bold, false);
            cells[2] = GpuUi.Cell(row, features, 170f, bold, false);
            cells[3] = GpuUi.Cell(row, vram, 190f, bold, false);
            cells[4] = GpuUi.Cell(row, budget, 90f, bold, true);
            return cells;
        }

        private void UpdateDevelopButton()
        {
            if (_developButton == null) return;

            _developButton.Interactable = _model.CanDevelop;
            string body = _model.CanDevelop
                ? ModL10n.L("gpu.design.create.tip", GpuUi.Text.Money(_model.Stats.ProjectCost), GpuUiMath.Span(GpuUi.Text, _model.Stats.ProjectDays))
                : _model.DevelopBlockText;
            GpuUi.Tip(_developButton, ModL10n.L("gpu.design.create"), body);
        }

        private void ShowNotice(string text, GpuTone tone)
        {
            if (_notice == null) return;
            _notice.text = text ?? string.Empty;
            _notice.gameObject.SetActive(!string.IsNullOrEmpty(text));
            GpuUi.Retint(_notice, tone);
        }

        // ---- the player's moves ----

        private void OnNameEdited(string typed)
        {
            _model.SetName(typed);
            if (_nameField != null && _nameField.text != _model.Name) _nameField.text = _model.Name;   // what was kept: no tags, no more than the limit
        }

        private void OnDevelop()
        {
            string reason;
            string id = _model.Develop(out reason);
            if (id == null)
            {
                ShowNotice(reason, GpuTone.Bad);
                UpdateDevelopButton();
                return;
            }

            GpuProjectSave project = _feature.Projects.Find(id);
            GpuDesignSave design = project != null ? _feature.Projects.Design(project.designId) : null;
            string name = design != null ? design.name : id;
            string cost = project != null ? GpuUi.Text.Money(project.totalCost) : string.Empty;
            string span = project != null ? GpuUiMath.Span(GpuUi.Text, project.days) : string.Empty;

            _model.SetName(string.Empty);
            if (_nameField != null) _nameField.text = string.Empty;
            ShowNotice(ModL10n.L("gpu.design.started", name, cost, span), GpuTone.Good);
            UpdateDevelopButton();
        }

        // The recomputation is measured: NFR-1 wants a change of an input to cost no more than a millisecond. The figure goes to the log of the verbose mode only.
        private void Timed(Action change)
        {
            var watch = Stopwatch.StartNew();
            change();
            watch.Stop();

            _measured++;
            if (watch.Elapsed.TotalMilliseconds > 1.0 || _measured % 50 == 1)
                ModLog.Debug(GpuFeature.FeatureId, "designer: a change of an input took " + watch.Elapsed.TotalMilliseconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " ms");
        }
    }
}
