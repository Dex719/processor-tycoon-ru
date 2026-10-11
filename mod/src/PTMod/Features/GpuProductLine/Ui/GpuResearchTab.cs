using System;
using System.Text;
using PTMod.Core;
using TMPro;
using UnityEngine;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;
using SliderUI = ProcessorTycoon.UI.SliderUI;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The tab "Исследования" (design.md, "Вкладка «Исследования»"; FR-2; TSK-023): the research in progress with its progress bar, the time left and the money a
    /// month, the slider of the funding (10-100 %: the time and the cost change at once), the queue of up to five nodes with a button to remove each, and the tree in three
    /// columns - the architecture, the video memory, the cooling. A node is a button of the game: finished ones are green, the one in progress is the call-to-action
    /// button, waiting ones are amber with their place, locked ones are dimmed; its tooltip has the year, the time at the present funding, the money a month, what it
    /// needs ("Нужно: DDR2") and "опережает время ×k" for a node of a later year. A click on an open node starts it or puts it in the queue. What the tab shows is the
    /// pure <see cref="GpuResearchTabModel"/>; once a game day it is refreshed in place (progress, time), and rebuilt when the plan itself changed.
    /// </summary>
    internal sealed class GpuResearchTab : IGpuTabView
    {
        private const float ColumnSpacing = 10f;
        private const float NodeHeight = 30f;

        private readonly RectTransform _body;
        private readonly GpuFeature _feature;
        private GpuResearchTabModel _model;
        private string _signature = string.Empty;

        private TMP_Text _notice;
        private TMP_Text _progressText, _eta, _cost, _ahead;
        private GpuBar _bar;
        private SliderUI _funding;
        private string _lastNotice = string.Empty;

        private GpuResearchTab(RectTransform body, GpuFeature feature)
        {
            _body = body;
            _feature = feature;
        }

        /// <summary>Builds the tab in the (emptied) body; the view that refreshes it each day.</summary>
        public static IGpuTabView Build(RectTransform body, GpuFeature feature)
        {
            var tab = new GpuResearchTab(body, feature);
            tab.Draw();
            return tab;
        }

        public void RefreshDaily()
        {
            GpuResearchTabModel model = ModelNow();
            if (Signature(model) != _signature)
            {
                GpuWindow.RebuildLater();   // a node was finished or the plan changed: the states of the tree are new
                return;
            }
            _model = model;
            UpdateCurrent(model.Current);
        }

        private GpuResearchTabModel ModelNow()
        {
            return GpuResearchTabModel.Build(_feature.Content, _feature.Research, _feature.State, GpuUi.Text);
        }

        // The shape of the tab: which node is current, what waits, the state of every node. Equal shapes are refreshed in place.
        private static string Signature(GpuResearchTabModel model)
        {
            var shape = new StringBuilder();
            shape.Append(model.Founded ? 'f' : 'n').Append('|').Append(model.Current != null ? model.Current.NodeId : string.Empty);
            foreach (GpuQueueItemView item in model.Queue) shape.Append('|').Append(item.NodeId);
            shape.Append('#');
            foreach (GpuBranchView branch in model.Branches)
            {
                foreach (GpuNodeView node in branch.Nodes) shape.Append((int)node.State).Append(node.CanQueue ? '+' : '-');
            }
            return shape.ToString();
        }

        // ---- drawing ----

        private void Draw()
        {
            _model = ModelNow();
            _signature = Signature(_model);

            _notice = GpuUi.Line(_body, string.Empty, true);
            GpuUi.Tint(_notice, GpuTone.Bad);
            ShowNotice(_lastNotice);

            DrawFunding();
            DrawCurrent();
            DrawQueue();
            DrawTree();
        }

        private void DrawFunding()
        {
            GpuResearchTabModel m = _model;
            _funding = ModUI.Slider(_body, ModL10n.L("gpu.research.funding"), m.FundingMin * 100f, m.FundingMax * 100f, FundingStepPercent(), m.Funding * 100f, OnFunding,
                ModL10n.L("gpu.research.funding.tip"));
            if (_funding != null) _funding.Text = FundingCaption(m.Funding);
        }

        private static float FundingStepPercent()
        {
            return GpuResearchTabModel.FundingStep * 100f;
        }

        private static string FundingCaption(float funding)
        {
            return ModL10n.L("gpu.research.fundingValue", Math.Round(funding * 100f).ToString("0", System.Globalization.CultureInfo.InvariantCulture));
        }

        private void DrawCurrent()
        {
            GpuCurrentResearchView current = _model.Current;
            RectTransform card = GpuUi.Card(_body, "PTModGpuResearchCurrent");
            if (current == null)
            {
                GpuUi.Tint(GpuUi.Line(card, ModL10n.L("gpu.research.idle")), GpuTone.Muted);
                return;
            }

            GpuUi.Line(card, ModL10n.L("gpu.research.current", current.Name), true);
            _bar = GpuBar.Create(card, 12f);
            RectTransform info = GpuUi.Row(card, "PTModGpuResearchInfo", 0f, 16f, false);
            _progressText = GpuUi.Line(info, current.ProgressText);
            GpuUi.SetWidth(_progressText, 0f, 1f);
            _eta = GpuUi.Line(info, current.EtaText);
            GpuUi.SetWidth(_eta, 0f, 1f);
            _cost = GpuUi.Line(info, current.CostText);
            GpuUi.SetWidth(_cost, 0f, 1f);
            _ahead = GpuUi.Line(card, current.AheadText);
            GpuUi.Tint(_ahead, GpuTone.Warn);

            string nodeId = current.NodeId;
            ButtonUI stop = ModUI.Button(card, ModL10n.L("gpu.research.stop"), () => OnStop(nodeId), ButtonStyle.Standard, ModL10n.L("gpu.research.stop.tip"));
            if (stop != null) GpuUi.SetWidth(stop, 220f, 0f);
            UpdateCurrent(current);
        }

        // The progress, the time left, the money a month and the funding of the node in progress (the slider asks for them again at each step).
        private void UpdateCurrent(GpuCurrentResearchView current)
        {
            if (current == null) return;
            if (_bar != null) _bar.SetFraction(current.Progress);
            GpuUi.Set(_progressText, current.ProgressText);
            GpuUi.Set(_eta, current.EtaText);
            GpuUi.Set(_cost, current.CostText);
            if (_ahead != null)
            {
                _ahead.text = current.AheadText;
                _ahead.gameObject.SetActive(current.AheadText.Length > 0);
            }
        }

        private void DrawQueue()
        {
            GpuResearchTabModel m = _model;
            GpuUi.Heading(_body, ModL10n.L("gpu.research.queue", m.Queue.Count, m.QueueMax));
            if (m.Queue.Count == 0)
            {
                GpuUi.Tint(GpuUi.Line(_body, ModL10n.L("gpu.research.queueEmpty")), GpuTone.Muted);
                return;
            }

            for (int i = 0; i < m.Queue.Count; i++)
            {
                GpuQueueItemView item = m.Queue[i];
                RectTransform row = GpuUi.Row(_body, "PTModGpuQueueRow", NodeHeight, 8f, true);
                TMP_Text name = GpuUi.Line(row, ModL10n.L("gpu.research.queueItem", i + 1, item.Name, item.DurationText));
                GpuUi.SetWidth(name, 0f, 1f);
                string id = item.NodeId;
                ButtonUI remove = ModUI.Button(row, ModL10n.L("gpu.research.remove"), () => OnRemove(id), ButtonStyle.Standard, ModL10n.L("gpu.research.remove.tip"));
                if (remove != null) GpuUi.SetWidth(remove, 130f, 0f);
            }
        }

        private void DrawTree()
        {
            GpuUi.Heading(_body, ModL10n.L("gpu.research.tree"));
            RectTransform[] columns;
            GpuUi.Columns(_body, "PTModGpuTree", _model.Branches.Count, ColumnSpacing, out columns);
            for (int c = 0; c < _model.Branches.Count; c++)
            {
                GpuBranchView branch = _model.Branches[c];
                GpuUi.Line(columns[c], branch.Title, true);
                foreach (GpuNodeView node in branch.Nodes) DrawNode(columns[c], node);
            }
        }

        private void DrawNode(Transform column, GpuNodeView node)
        {
            string id = node.Id;
            ButtonStyle style = node.State == GpuNodeState.Current ? ButtonStyle.CallToAction : ButtonStyle.Standard;
            ButtonUI button = ModUI.Button(column, node.Caption, () => OnNodeClick(id), style);
            if (button == null) return;

            GpuUi.StyleListButton(button);
            GpuUi.Caption(button, CaptionOf(node), ToneOf(node.State));
            button.Interactable = node.CanQueue;
            GpuUi.TipLive(button, () =>
            {
                GpuNodeView now = FindNode(id);
                return now == null ? new ModUI.TooltipText(string.Empty, string.Empty) : new ModUI.TooltipText(now.TipHeader, now.TipBody);
            });
        }

        private static string CaptionOf(GpuNodeView node)
        {
            switch (node.State)
            {
                case GpuNodeState.Current: return ModL10n.L("gpu.research.nodeCurrent", node.Caption);
                case GpuNodeState.Queued: return ModL10n.L("gpu.research.nodeQueued", node.Caption, node.QueueIndex);
                default: return node.Caption;
            }
        }

        private static GpuTone ToneOf(GpuNodeState state)
        {
            switch (state)
            {
                case GpuNodeState.Researched: return GpuTone.Good;
                case GpuNodeState.Queued: return GpuTone.Warn;
                case GpuNodeState.Locked: return GpuTone.Muted;
                default: return GpuTone.Normal;
            }
        }

        private GpuNodeView FindNode(string id)
        {
            if (_model == null) return null;
            foreach (GpuBranchView branch in _model.Branches)
            {
                foreach (GpuNodeView node in branch.Nodes)
                {
                    if (node.Id == id) return node;
                }
            }
            return null;
        }

        // ---- the player's moves ----

        private void OnFunding(float percent)
        {
            float funding = percent / 100f;
            _feature.Research.SetFunding(funding);
            _model = ModelNow();   // the tooltips of the nodes ask the model: the time at the new funding
            UpdateCurrent(GpuResearchTabModel.CurrentAt(_feature.Content, _feature.Research, GpuUi.Text, funding));
            if (_funding != null) _funding.Text = FundingCaption(funding);   // after ModUI wrote the raw number
        }

        private void OnNodeClick(string id)
        {
            string reasonKey;
            string[] reasonArgs;
            if (_feature.Research.Enqueue(id, out reasonKey, out reasonArgs))
            {
                _lastNotice = string.Empty;
                GpuWindow.RebuildLater();
                return;
            }

            _lastNotice = reasonKey == null ? string.Empty : ModL10n.L(reasonKey, ArgsOf(reasonArgs));
            ShowNotice(_lastNotice);
        }

        private void OnRemove(string id)
        {
            _feature.Research.Dequeue(id);
            _lastNotice = string.Empty;
            GpuWindow.RebuildLater();
        }

        // Stopping the node in progress loses its progress (as cancelling a research of the game does), so it asks first.
        private void OnStop(string id)
        {
            string name = GpuNames.Node(_feature.Content, GpuUi.Text, id);
            string percent = Math.Round(_feature.Research.Progress * 100f).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            ModUI.Confirm(ModL10n.L("gpu.research.stop.title"), ModL10n.L("gpu.research.stop.message", name, percent), () =>
            {
                _feature.Research.Dequeue(id);
                _lastNotice = string.Empty;
                GpuWindow.RebuildLater();
            }, null, "gpu.research.stop.yes", "core.common.cancel");
        }

        private void ShowNotice(string text)
        {
            if (_notice == null) return;
            _notice.text = text ?? string.Empty;
            _notice.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

        private static object[] ArgsOf(string[] args)
        {
            if (args == null) return new object[0];
            var objects = new object[args.Length];
            for (int i = 0; i < args.Length; i++) objects[i] = args[i];
            return objects;
        }
    }
}
