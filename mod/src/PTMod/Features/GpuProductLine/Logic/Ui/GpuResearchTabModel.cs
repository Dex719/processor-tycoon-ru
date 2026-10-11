using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>Where a node of the tree stands for the window: finished, being researched, waiting in the queue, open, or locked.</summary>
    public enum GpuNodeState
    {
        Researched = 0,
        Current = 1,
        Queued = 2,
        Available = 3,
        Locked = 4,
    }

    /// <summary>A node of the tree as the tab draws it: the caption, the state, whether a click can plan it, and the tooltip (FR-2, AC-2.2, AC-2.3).</summary>
    public sealed class GpuNodeView
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string Branch = string.Empty;
        public int Year;
        public GpuNodeState State;

        /// <summary>A click puts the node in the plan (it starts at once when the laboratory is idle, otherwise it joins the queue).</summary>
        public bool CanQueue;

        /// <summary>Why the node cannot be planned (empty when it can, or when it is finished or already planned).</summary>
        public string BlockText = string.Empty;

        /// <summary>"Name · year".</summary>
        public string Caption = string.Empty;

        public string TipHeader = string.Empty;
        public string TipBody = string.Empty;

        /// <summary>0 for the node being researched, 1... for its place in the queue, -1 when it is not planned.</summary>
        public int QueueIndex = -1;
    }

    /// <summary>A branch of the tree: "Архитектура", "Видеопамять" or "Охлаждение" with its nodes, the earliest first.</summary>
    public sealed class GpuBranchView
    {
        public string Branch = string.Empty;
        public string Title = string.Empty;
        public List<GpuNodeView> Nodes = new List<GpuNodeView>();
    }

    /// <summary>The research in progress: the node, the progress, the time left and the money a month at a funding (the slider recomputes it, FR-2 item 7).</summary>
    public sealed class GpuCurrentResearchView
    {
        public string NodeId = string.Empty;
        public string Name = string.Empty;

        /// <summary>0..1.</summary>
        public float Progress;

        public string ProgressText = string.Empty;
        public int DaysLeft;

        /// <summary>"Осталось ≈ N дн."</summary>
        public string EtaText = string.Empty;

        public float CostPerMonth;

        /// <summary>"Расход в месяц: ..."</summary>
        public string CostText = string.Empty;

        public float Funding;
        public string FundingText = string.Empty;

        /// <summary>"Опережает время: срок ×k" or an empty string.</summary>
        public string AheadText = string.Empty;
    }

    /// <summary>A node waiting in the queue.</summary>
    public sealed class GpuQueueItemView
    {
        public string NodeId = string.Empty;
        public string Name = string.Empty;

        /// <summary>The whole time of the node at the present funding.</summary>
        public string DurationText = string.Empty;
    }

    /// <summary>
    /// The tab "Исследования" as data (design.md, "Вкладка «Исследования»"; FR-2): three branches of nodes with their states and tooltips, the research in progress
    /// with its progress and the time and money it takes at the chosen funding, the funding limits and the queue of up to five nodes. Built from the laboratory
    /// (<see cref="GpuResearch"/>) on every change of the plan and once a day; the slider only asks <see cref="CurrentAt"/> again.
    /// </summary>
    public sealed class GpuResearchTabModel
    {
        /// <summary>The funding slider moves in steps of this size (5 %).</summary>
        public const float FundingStep = 0.05f;

        public bool Founded;
        public List<GpuBranchView> Branches = new List<GpuBranchView>();

        /// <summary>The research in progress, or null when the laboratory is idle.</summary>
        public GpuCurrentResearchView Current;

        public List<GpuQueueItemView> Queue = new List<GpuQueueItemView>();
        public float Funding, FundingMin, FundingMax;
        public int QueueMax;

        /// <summary>The nodes that still fit in the queue.</summary>
        public int QueueFree;

        /// <summary>Builds the tab; never null. A division that is not founded gives the tree with every node locked.</summary>
        public static GpuResearchTabModel Build(GpuContentDb content, GpuResearch research, GpuState state, IGpuText text)
        {
            var model = new GpuResearchTabModel();
            GpuBalanceDto.ResearchBlock rules = (content.Balance ?? new GpuBalanceDto()).research ?? new GpuBalanceDto.ResearchBlock();
            model.Founded = state.Data.divisionFounded;
            model.Funding = research.Funding;
            model.FundingMin = rules.fundingMin;
            model.FundingMax = rules.fundingMax;
            model.QueueMax = Math.Max(0, rules.queueMax);
            model.QueueFree = Math.Max(0, model.QueueMax - research.Queue.Count);

            foreach (string branch in new[] { GpuContentDb.BranchArchitecture, GpuContentDb.BranchMemory, GpuContentDb.BranchCooling })
            {
                var view = new GpuBranchView { Branch = branch, Title = text.L(GpuNames.BranchKey(branch)) };
                foreach (GpuResearchNodeDto node in content.ResearchNodes)
                {
                    if (node.branch == branch) view.Nodes.Add(NodeView(content, research, text, node, model));
                }
                view.Nodes.Sort((a, b) => a.Year != b.Year ? a.Year.CompareTo(b.Year) : string.CompareOrdinal(a.Id, b.Id));
                model.Branches.Add(view);
            }

            model.Current = research.Current != null ? CurrentAt(content, research, text, research.Funding) : null;
            for (int i = 0; i < research.Queue.Count; i++)
            {
                string id = research.Queue[i];
                GpuResearchQuote quote = research.Quote(id, research.Funding);
                model.Queue.Add(new GpuQueueItemView
                {
                    NodeId = id,
                    Name = GpuNames.Node(content, text, id),
                    DurationText = GpuUiMath.Span(text, quote.DaysLeft),
                });
            }
            return model;
        }

        /// <summary>
        /// The research in progress at a funding (FR-2 item 7: the slider shows the new time and cost at once): the time left at that pace and the money a month.
        /// Null when the laboratory is idle.
        /// </summary>
        public static GpuCurrentResearchView CurrentAt(GpuContentDb content, GpuResearch research, IGpuText text, float funding)
        {
            GpuResearchNodeDto node = research.Current;
            if (node == null) return null;

            GpuResearchQuote quote = research.Quote(node.id, funding);
            float shown = float.IsNaN(funding) ? research.Funding : funding;
            var view = new GpuCurrentResearchView
            {
                NodeId = node.id,
                Name = GpuNames.Node(content, text, node.id),
                Progress = Math.Min(1f, Math.Max(0f, research.Progress)),
                DaysLeft = quote.DaysLeft,
                CostPerMonth = quote.CostPerMonth,
                Funding = shown,
            };
            view.ProgressText = text.L("gpu.research.progress", Math.Round(view.Progress * 100f).ToString("0", CultureInfo.InvariantCulture));
            view.EtaText = text.L("gpu.research.eta", GpuUiMath.Span(text, quote.DaysLeft));
            view.CostText = text.L("gpu.research.cost", text.Money(quote.CostPerMonth));
            view.FundingText = text.L("gpu.research.fundingValue", Math.Round(shown * 100f).ToString("0", CultureInfo.InvariantCulture));
            view.AheadText = AheadText(text, quote.AheadFactor);
            return view;
        }

        private static string AheadText(IGpuText text, float factor)
        {
            return factor > 1.01f ? text.L("gpu.research.ahead", GpuUiMath.Plain((float)Math.Round(factor, 1))) : string.Empty;
        }

        private static GpuNodeView NodeView(GpuContentDb content, GpuResearch research, IGpuText text, GpuResearchNodeDto node, GpuResearchTabModel model)
        {
            string name = GpuNames.Node(content, text, node.id);
            var view = new GpuNodeView { Id = node.id, Name = name, Branch = node.branch, Year = node.year };
            view.Caption = text.L("gpu.research.nodeCaption", name, node.year);

            bool current = research.Current != null && research.Current.id == node.id;
            int queued = IndexIn(research.Queue, node.id);
            List<string> missingNodes = research.MissingNodes(node.id);
            List<string> missingTechs = research.MissingGameTechs(node.id);

            if (research.IsResearched(node.id))
            {
                view.State = GpuNodeState.Researched;
            }
            else if (current)
            {
                view.State = GpuNodeState.Current;
                view.QueueIndex = 0;
            }
            else if (queued >= 0)
            {
                view.State = GpuNodeState.Queued;
                view.QueueIndex = queued + 1;
            }
            else
            {
                view.BlockText = BlockText(content, research, text, node, missingNodes, missingTechs, model);
                view.CanQueue = model.Founded && view.BlockText.Length == 0;
                view.State = view.CanQueue ? GpuNodeState.Available : GpuNodeState.Locked;
                if (!model.Founded) view.BlockText = text.L(GpuResearch.ReasonNotFounded);
            }

            view.TipHeader = name;
            view.TipBody = TipBody(content, research, text, node, view, missingNodes, missingTechs);
            return view;
        }

        // The rule of GpuResearch.Enqueue without doing it: the technologies of the game first, then the nodes of the tree that are neither finished nor planned, then the queue.
        private static string BlockText(GpuContentDb content, GpuResearch research, IGpuText text, GpuResearchNodeDto node, List<string> missingNodes, List<string> missingTechs,
            GpuResearchTabModel model)
        {
            if (missingTechs.Count > 0) return text.L(GpuResearch.ReasonTech, string.Join(", ", missingTechs.ToArray()));

            var unplanned = new List<string>();
            foreach (string need in missingNodes)
            {
                if (!research.IsPlanned(need)) unplanned.Add(need);
            }
            if (unplanned.Count > 0) return text.L(GpuResearch.ReasonNodes, GpuNames.Nodes(content, text, unplanned));

            bool startsNow = research.Current == null && missingNodes.Count == 0;
            if (!startsNow && research.Queue.Count >= model.QueueMax) return text.L(GpuResearch.ReasonQueueFull, model.QueueMax);
            return string.Empty;
        }

        private static string TipBody(GpuContentDb content, GpuResearch research, IGpuText text, GpuResearchNodeDto node, GpuNodeView view, List<string> missingNodes, List<string> missingTechs)
        {
            var lines = new List<string>();
            lines.Add(text.L("gpu.research.tip.year", node.year));

            GpuResearchQuote quote = research.Quote(node.id, research.Funding);
            if (view.State != GpuNodeState.Researched)
            {
                lines.Add(text.L("gpu.research.tip.duration", GpuUiMath.Span(text, quote.DaysLeft), GpuUiMath.Plain((float)Math.Round(quote.EffectiveDays))));
                lines.Add(text.L("gpu.research.tip.cost", text.Money(quote.CostPerMonth)));
                string ahead = AheadText(text, quote.AheadFactor);
                if (ahead.Length > 0) lines.Add(ahead);
            }

            string needs = NeedsText(content, text, node);
            if (needs.Length > 0) lines.Add(text.L("gpu.research.needs", needs));

            switch (view.State)
            {
                case GpuNodeState.Researched:
                    lines.Add(text.L("gpu.research.state.done"));
                    break;
                case GpuNodeState.Current:
                    lines.Add(text.L("gpu.research.state.current", Math.Round(research.Progress * 100f).ToString("0", CultureInfo.InvariantCulture)));
                    break;
                case GpuNodeState.Queued:
                    lines.Add(text.L("gpu.research.state.queued", view.QueueIndex));
                    break;
                case GpuNodeState.Available:
                    lines.Add(text.L("gpu.research.state.available"));
                    break;
                default:
                    if (view.BlockText.Length > 0) lines.Add(view.BlockText);
                    break;
            }
            return string.Join("\n", lines.ToArray());
        }

        // The nodes of the tree and the technologies of the game a node needs, in one line ("Нужно: Ускоритель интерфейса, DDR2").
        private static string NeedsText(GpuContentDb content, IGpuText text, GpuResearchNodeDto node)
        {
            var parts = new List<string>();
            if (node.requiresNodes != null)
            {
                foreach (string need in node.requiresNodes)
                {
                    if (!string.IsNullOrEmpty(need)) parts.Add(GpuNames.Node(content, text, need));
                }
            }
            if (node.requiresGameTechs != null)
            {
                foreach (string tech in node.requiresGameTechs)
                {
                    if (!string.IsNullOrEmpty(tech)) parts.Add(tech);
                }
            }
            return string.Join(", ", parts.ToArray());
        }

        private static int IndexIn(IReadOnlyList<string> list, string id)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == id) return i;
            }
            return -1;
        }
    }
}
