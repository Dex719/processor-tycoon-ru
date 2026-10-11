using System;
using System.Collections.Generic;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>Where a node of the GPU tree stands for the player (the research tab colours it by this).</summary>
    public enum NodeAvailability
    {
        /// <summary>Finished: its part is in the designer.</summary>
        Researched,

        /// <summary>Every node and every technology of the game it needs is there: it can be researched.</summary>
        Available,

        /// <summary>A node of the GPU tree it needs is not researched yet (or the id is unknown).</summary>
        LockedByNodes,

        /// <summary>A technology of the game it needs is not researched by the company (AC-2.3: "Нужно: DDR2").</summary>
        LockedByGameTech,
    }

    /// <summary>What a node takes now: its time this year (F15), the days left at the pace of the funding, the money a month, and how far ahead of its time it is.</summary>
    public struct GpuResearchQuote
    {
        public float EffectiveDays;
        public float DailyProgress;
        public int DaysLeft;
        public float CostPerDay;
        public float CostPerMonth;
        public float AheadFactor;
    }

    /// <summary>
    /// The GPU laboratory (design.md, "GpuResearch / GpuResearchMath"; FR-2): the current node, a queue of up to <c>research.queueMax</c> (5) nodes, the funding
    /// (10-100 %), a day of research on <c>TickPhase.Tick</c> and the catch-up of the founding. It works on <see cref="GpuState"/> (the section of the save) and
    /// asks the game through <see cref="IGpuGame"/>: the research speed (<c>ResearchMath.ResearchSpeed</c>, so <c>research.speed</c> acts), the cost modifier
    /// (<c>Modifiers.GetModifier(ResearchCost)</c>, so <c>research.cost</c> acts), the technologies of the game a node needs (the core's predicate
    /// <c>techResearched</c>). The money goes out under <c>ExpenseOrigin.Research</c>; a finished node raises <see cref="Completed"/>, shows a notification and
    /// the next node of the queue starts the same day. Nothing moves while the division is not founded; the tick of the core does not run during the
    /// fast-forward of a new game or the loading of a save (the subscription has no <c>RunDuringFastForward</c>).
    /// </summary>
    public sealed class GpuResearch
    {
        public const string ReasonNotFounded = "gpu.research.block.notFounded";
        public const string ReasonUnknown = "gpu.research.block.unknown";
        public const string ReasonDone = "gpu.research.block.done";
        public const string ReasonQueued = "gpu.research.block.queued";
        public const string ReasonNodes = "gpu.research.block.nodes";
        public const string ReasonTech = "gpu.research.block.tech";
        public const string ReasonQueueFull = "gpu.research.block.queueFull";
        public const string NotifyDone = "gpu.notify.researchDone";

        private readonly GpuContentDb _content;
        private readonly GpuState _state;
        private readonly IGpuGame _game;

        public GpuResearch(GpuContentDb content, GpuState state, IGpuGame game)
        {
            if (content == null) throw new ArgumentNullException("content");
            if (state == null) throw new ArgumentNullException("state");
            if (game == null) throw new ArgumentNullException("game");
            _content = content;
            _state = state;
            _game = game;
        }

        /// <summary>A node was finished (the id is in <c>research.completed</c> already).</summary>
        public event Action<GpuResearchNodeDto> Completed;

        private GpuResearchSave Data
        {
            get
            {
                GpuSaveSection section = _state.Data;
                if (section.research == null) section.research = new GpuResearchSave();
                GpuResearchSave r = section.research;
                if (r.completed == null) r.completed = new List<string>();
                if (r.queue == null) r.queue = new List<string>();
                if (r.currentId == null) r.currentId = string.Empty;
                return r;
            }
        }

        private GpuBalanceDto.ResearchBlock Rules
        {
            get { return (_content.Balance ?? new GpuBalanceDto()).research ?? new GpuBalanceDto.ResearchBlock(); }
        }

        /// <summary>The node being researched, or null.</summary>
        public GpuResearchNodeDto Current
        {
            get
            {
                string id = Data.currentId;
                return string.IsNullOrEmpty(id) ? null : _content.Node(id);
            }
        }

        /// <summary>The progress of the current node, 0..1.</summary>
        public float Progress
        {
            get { return string.IsNullOrEmpty(Data.currentId) ? 0f : Data.currentProgress; }
        }

        /// <summary>The queue after the current node, in order.</summary>
        public IReadOnlyList<string> Queue
        {
            get { return Data.queue; }
        }

        /// <summary>The finished nodes.</summary>
        public IReadOnlyList<string> CompletedIds
        {
            get { return Data.completed; }
        }

        /// <summary>The funding, a share of the full pace in [<c>fundingMin</c>, <c>fundingMax</c>] (0.1..1.0).</summary>
        public float Funding
        {
            get { return ClampFunding(Data.funding); }
        }

        /// <summary>The node is finished.</summary>
        public bool IsResearched(string nodeId)
        {
            return !string.IsNullOrEmpty(nodeId) && Data.completed.Contains(nodeId);
        }

        /// <summary>The node is the current one or waits in the queue.</summary>
        public bool IsPlanned(string nodeId)
        {
            GpuResearchSave r = Data;
            return !string.IsNullOrEmpty(nodeId) && (r.currentId == nodeId || r.queue.Contains(nodeId));
        }

        /// <summary>Where the node stands: finished, open, waiting for a node of the tree, waiting for a technology of the game.</summary>
        public NodeAvailability Availability(string nodeId)
        {
            GpuResearchNodeDto node = _content.Node(nodeId);
            if (node == null) return NodeAvailability.LockedByNodes;
            if (IsResearched(node.id)) return NodeAvailability.Researched;
            if (MissingNodes(node).Count > 0) return NodeAvailability.LockedByNodes;
            if (MissingGameTechs(node.id).Count > 0) return NodeAvailability.LockedByGameTech;
            return NodeAvailability.Available;
        }

        /// <summary>The nodes of the tree the node needs that are not finished, in the order of the content.</summary>
        public List<string> MissingNodes(string nodeId)
        {
            return MissingNodes(_content.Node(nodeId));
        }

        /// <summary>The technologies of the game the node needs that the player's company has not researched (the tooltip "Нужно: DDR2").</summary>
        public List<string> MissingGameTechs(string nodeId)
        {
            var missing = new List<string>();
            GpuResearchNodeDto node = _content.Node(nodeId);
            if (node == null || node.requiresGameTechs == null) return missing;
            foreach (string tech in node.requiresGameTechs)
            {
                if (string.IsNullOrEmpty(tech)) continue;
                if (!_game.TechResearched(_game.PlayerKey, tech)) missing.Add(tech);
            }
            return missing;
        }

        /// <summary>Starts the node at once when the laboratory is idle and the node is open, or puts it at the end of the queue; see the other overload.</summary>
        public bool Enqueue(string nodeId)
        {
            string reasonKey;
            string[] reasonArgs;
            return Enqueue(nodeId, out reasonKey, out reasonArgs);
        }

        /// <summary>
        /// FR-2 item 2: a node whose technologies of the game are researched and whose nodes of the tree are finished or planned before it (current or earlier in the
        /// queue) becomes the current research when the laboratory is idle and the node is open, otherwise goes to the end of the queue (at most
        /// <c>research.queueMax</c> nodes wait). False, with the reason key and its arguments, when it cannot.
        /// </summary>
        public bool Enqueue(string nodeId, out string reasonKey, out string[] reasonArgs)
        {
            reasonArgs = null;
            if (!_state.Data.divisionFounded)
            {
                reasonKey = ReasonNotFounded;
                return false;
            }
            GpuResearchNodeDto node = _content.Node(nodeId);
            if (node == null)
            {
                reasonKey = ReasonUnknown;
                return false;
            }
            GpuResearchSave r = Data;
            if (IsResearched(node.id))
            {
                reasonKey = ReasonDone;
                return false;
            }
            if (IsPlanned(node.id))
            {
                reasonKey = ReasonQueued;
                return false;
            }
            List<string> techs = MissingGameTechs(node.id);
            if (techs.Count > 0)
            {
                reasonKey = ReasonTech;
                reasonArgs = new[] { string.Join(", ", techs.ToArray()) };
                return false;
            }
            var unplanned = new List<string>();
            foreach (string need in MissingNodes(node))
            {
                if (!IsPlanned(need)) unplanned.Add(need);
            }
            if (unplanned.Count > 0)
            {
                reasonKey = ReasonNodes;
                reasonArgs = new[] { string.Join(", ", unplanned.ToArray()) };
                return false;
            }

            if (string.IsNullOrEmpty(r.currentId) && MissingNodes(node).Count == 0)
            {
                r.currentId = node.id;
                r.currentProgress = 0f;
                reasonKey = null;
                return true;
            }
            if (r.queue.Count >= Math.Max(0, Rules.queueMax))
            {
                reasonKey = ReasonQueueFull;
                reasonArgs = new[] { Rules.queueMax.ToString(System.Globalization.CultureInfo.InvariantCulture) };
                return false;
            }
            r.queue.Add(node.id);
            reasonKey = null;
            return true;
        }

        /// <summary>
        /// Takes the node out of the queue; the current node is stopped (its progress is lost, as when the game's research is cancelled) and the next open node of the
        /// queue starts. The waiting nodes that needed it, and nothing else plans what they need, leave the queue with it.
        /// </summary>
        public void Dequeue(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return;
            GpuResearchSave r = Data;
            r.queue.Remove(nodeId);
            if (r.currentId == nodeId)
            {
                r.currentId = string.Empty;
                r.currentProgress = 0f;
            }
            DropOrphans();
            StartNext();
        }

        // Removes the waiting nodes that need a node which is neither finished nor planned (again, until nothing changes: one removal can orphan another).
        private void DropOrphans()
        {
            GpuResearchSave r = Data;
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = r.queue.Count - 1; i >= 0; i--)
                {
                    foreach (string need in MissingNodes(_content.Node(r.queue[i])))
                    {
                        if (IsPlanned(need)) continue;
                        r.queue.RemoveAt(i);
                        changed = true;
                        break;
                    }
                }
            }
        }

        /// <summary>FR-2 item 7: the funding the next day of research uses, held to [<c>fundingMin</c>, <c>fundingMax</c>]; the tab shows the new pace at once (<see cref="Quote"/>).</summary>
        public void SetFunding(float funding)
        {
            Data.funding = ClampFunding(funding);
        }

        /// <summary>The time, the pace and the cost of the node today at <paramref name="funding"/> (the slider of the tab; <see cref="Funding"/> when it is not a number).</summary>
        public GpuResearchQuote Quote(string nodeId, float funding)
        {
            var quote = new GpuResearchQuote { DaysLeft = -1, AheadFactor = 1f };
            GpuResearchNodeDto node = _content.Node(nodeId);
            if (node == null) return quote;
            float f = float.IsNaN(funding) ? Funding : ClampFunding(funding);
            GpuBalanceDto.ResearchBlock rules = Rules;
            string player = _game.PlayerKey;

            quote.EffectiveDays = GpuResearchMath.EffectiveDays(node.baseDays, node.year, _game.Today, rules.penaltyPerYear, rules.bonusPerYear, rules.maxBonus);
            quote.AheadFactor = GpuResearchMath.AheadFactor(node.baseDays, quote.EffectiveDays);
            quote.DailyProgress = GpuResearchMath.DailyProgress(_game.ResearchSpeed(f, player), quote.EffectiveDays);
            float progress = Data.currentId == node.id ? Data.currentProgress : 0f;
            quote.DaysLeft = GpuResearchMath.DaysLeft(progress, quote.DailyProgress);
            quote.CostPerDay = GpuResearchMath.DailyCost(node.monthlyCost, _game.Modifier(GpuModifierKind.ResearchCost, player), f);
            quote.CostPerMonth = quote.CostPerDay * 30f;
            return quote;
        }

        /// <summary>
        /// The catch-up of the founding (FR-1 item 6): every node of a year up to <paramref name="year"/> is finished at once, whatever it needs; such nodes leave
        /// the queue (and the current place). Returns the ids that were opened now, in the order of the content.
        /// </summary>
        public List<string> UnlockUpToYear(int year)
        {
            var opened = new List<string>();
            GpuResearchSave r = Data;
            foreach (GpuResearchNodeDto node in _content.ResearchNodes)
            {
                if (node.year > year || r.completed.Contains(node.id)) continue;
                r.completed.Add(node.id);
                r.queue.Remove(node.id);
                if (r.currentId == node.id)
                {
                    r.currentId = string.Empty;
                    r.currentProgress = 0f;
                }
                opened.Add(node.id);
            }
            if (string.IsNullOrEmpty(r.currentId)) StartNext();
            return opened;
        }

        /// <summary>The nodes <see cref="UnlockUpToYear"/> would open now (the founding screen lists them).</summary>
        public List<string> NodesUpToYear(int year)
        {
            var nodes = new List<string>();
            foreach (GpuResearchNodeDto node in _content.ResearchNodes)
            {
                if (node.year <= year && !IsResearched(node.id)) nodes.Add(node.id);
            }
            return nodes;
        }

        /// <summary>
        /// One day of research (<c>TickPhase.Tick</c>; FR-2 items 4, 5, 8): the progress grows by <c>ResearchSpeed(funding) / T_eff</c>, the day costs
        /// <c>monthlyCost x Modifier(ResearchCost) / 30 x funding</c> (<c>ExpenseOrigin.Research</c>); at 1 the node is finished, the notification is shown and the
        /// next open node of the queue becomes current.
        /// </summary>
        public void DailyTick()
        {
            if (!_state.Data.divisionFounded) return;
            GpuResearchSave r = Data;
            if (string.IsNullOrEmpty(r.currentId) && !StartNext()) return;

            GpuResearchNodeDto node = _content.Node(r.currentId);
            if (node == null)
            {
                r.currentId = string.Empty;
                r.currentProgress = 0f;
                StartNext();
                return;
            }

            string player = _game.PlayerKey;
            float funding = ClampFunding(r.funding);
            GpuBalanceDto.ResearchBlock rules = Rules;
            float days = GpuResearchMath.EffectiveDays(node.baseDays, node.year, _game.Today, rules.penaltyPerYear, rules.bonusPerYear, rules.maxBonus);
            float step = GpuResearchMath.DailyProgress(_game.ResearchSpeed(funding, player), days);
            float cost = GpuResearchMath.DailyCost(node.monthlyCost, _game.Modifier(GpuModifierKind.ResearchCost, player), funding);
            _game.Spend(player, cost, GpuExpense.Research);

            r.currentProgress = Math.Min(1f, Math.Max(0f, r.currentProgress) + step);
            if (r.currentProgress >= 1f) Finish(node);
        }

        private void Finish(GpuResearchNodeDto node)
        {
            GpuResearchSave r = Data;
            if (!r.completed.Contains(node.id)) r.completed.Add(node.id);
            r.currentId = string.Empty;
            r.currentProgress = 0f;
            r.queue.Remove(node.id);

            string nameKey = NameKeyOf(node);
            _game.Notify(NotifyDone, nameKey ?? node.id, nameKey != null);
            Action<GpuResearchNodeDto> handlers = Completed;
            if (handlers != null) handlers(node);
            StartNext();
        }

        /// <summary>The text key of the part the node opens (the name of the architecture, memory or cooler), or null.</summary>
        public string NameKeyOf(GpuResearchNodeDto node)
        {
            if (node == null || string.IsNullOrEmpty(node.unlocks)) return null;
            GpuArchitectureDto arch = _content.Arch(node.unlocks);
            if (arch != null && !string.IsNullOrEmpty(arch.nameKey)) return arch.nameKey;
            GpuMemoryDto mem = _content.Mem(node.unlocks);
            if (mem != null && !string.IsNullOrEmpty(mem.nameKey)) return mem.nameKey;
            GpuCoolerDto cooler = _content.Cooler(node.unlocks);
            return cooler != null && !string.IsNullOrEmpty(cooler.nameKey) ? cooler.nameKey : null;
        }

        // The first open node of the queue becomes current. A node that waits for a node nobody plans any more is dropped; a node that waits for a technology
        // of the game, or for a node planned before it, keeps its place.
        private bool StartNext()
        {
            GpuResearchSave r = Data;
            if (!string.IsNullOrEmpty(r.currentId)) return true;
            for (int i = 0; i < r.queue.Count;)
            {
                string id = r.queue[i];
                GpuResearchNodeDto node = _content.Node(id);
                if (node == null || IsResearched(id))
                {
                    r.queue.RemoveAt(i);
                    continue;
                }
                List<string> missing = MissingNodes(node);
                if (missing.Count == 0 && MissingGameTechs(id).Count == 0)
                {
                    r.queue.RemoveAt(i);
                    r.currentId = id;
                    r.currentProgress = 0f;
                    return true;
                }
                bool orphan = false;
                foreach (string need in missing)
                {
                    if (!r.queue.Contains(need)) orphan = true;
                }
                if (orphan)
                {
                    r.queue.RemoveAt(i);
                    continue;
                }
                i++;
            }
            return false;
        }

        private List<string> MissingNodes(GpuResearchNodeDto node)
        {
            var missing = new List<string>();
            if (node == null || node.requiresNodes == null) return missing;
            foreach (string need in node.requiresNodes)
            {
                if (!string.IsNullOrEmpty(need) && !IsResearched(need)) missing.Add(need);
            }
            return missing;
        }

        private float ClampFunding(float funding)
        {
            GpuBalanceDto.ResearchBlock rules = Rules;
            if (float.IsNaN(funding) || float.IsInfinity(funding)) return rules.fundingMax;
            return Math.Min(rules.fundingMax, Math.Max(rules.fundingMin, funding));
        }
    }
}
