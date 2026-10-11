using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The development of the player's cards (design.md, "GpuProjects и выпуск"; FR-4). A project is not a <c>Project</c> of the game (the game saves every
    /// timer of that type into its own save): it lives in the section of the feature. <see cref="Start"/> evaluates the design with <see cref="GpuDesigner"/>
    /// - the cost is F13, <c>(arch.projectCost + node.ProjectCost x nodeProjectShare + mem.projectCost) x Modifiers.GetModifier(DevelopmentCost)</c>, the time
    /// <c>arch.projectDays + mem.projectDays</c> - keeps the design with its snapshot and opens the project at 0 %. Every day (<c>TickPhase.Tick</c>) a project that is
    /// not paused moves by <c>1 / days</c> and costs <c>cost / days x inflation now / inflation at the start</c> (F14, <c>ExpenseOrigin.Development</c>); at 100 % it
    /// becomes a product with the status <c>ready</c> and <see cref="ReadyToRelease"/> is raised (the release dialog opens on it). At most
    /// <c>design.maxParallelProjects</c> projects at a time; a cancelled project gives no money back.
    /// </summary>
    public sealed class GpuProjects
    {
        public const string ReasonNotFounded = "gpu.project.block.notFounded";
        public const string ReasonLimit = "gpu.project.block.limit";
        public const string NotifyDone = "gpu.notify.projectDone";

        private readonly GpuContentDb _content;
        private readonly GpuState _state;
        private readonly IGpuGame _game;
        private readonly GpuDesigner _designer;

        public GpuProjects(GpuContentDb content, GpuState state, IGpuGame game, GpuDesigner designer)
        {
            if (content == null) throw new ArgumentNullException("content");
            if (state == null) throw new ArgumentNullException("state");
            if (game == null) throw new ArgumentNullException("game");
            if (designer == null) throw new ArgumentNullException("designer");
            _content = content;
            _state = state;
            _game = game;
            _designer = designer;
        }

        /// <summary>A project was finished: the id of the new product (status <c>ready</c>).</summary>
        public event Action<string> ReadyToRelease;

        private GpuBalanceDto Balance
        {
            get { return _content.Balance ?? new GpuBalanceDto(); }
        }

        private List<GpuProjectSave> Projects
        {
            get
            {
                GpuSaveSection section = _state.Data;
                if (section.projects == null) section.projects = new List<GpuProjectSave>();
                return section.projects;
            }
        }

        /// <summary>The projects in development, paused ones included.</summary>
        public IReadOnlyList<GpuProjectSave> Active
        {
            get { return Projects; }
        }

        /// <summary>The design of a project (or of a product), or null.</summary>
        public GpuDesignSave Design(string designId)
        {
            List<GpuDesignSave> designs = _state.Data.designs;
            return designs == null || string.IsNullOrEmpty(designId) ? null : designs.Find(d => d != null && d.id == designId);
        }

        /// <summary>FR-4 item 3: a new project may start (the division is founded and fewer than <c>maxParallelProjects</c> projects run).</summary>
        public bool CanStart(out string reasonKey)
        {
            if (!_state.Data.divisionFounded)
            {
                reasonKey = ReasonNotFounded;
                return false;
            }
            if (Projects.Count >= Math.Max(1, Balance.design.maxParallelProjects))
            {
                reasonKey = ReasonLimit;
                return false;
            }
            reasonKey = null;
            return true;
        }

        /// <summary>Starts the development of the design; the id of the project, or null (see the other overload for the reason).</summary>
        public string Start(GpuDesignInput input)
        {
            string reasonKey;
            return Start(input, out reasonKey);
        }

        /// <summary>
        /// FR-3 item 7, FR-4 item 1: evaluates the design for the player today, keeps it with its snapshot (<c>designs</c>) and opens the project at 0 %. Null, with the
        /// reason key, when no project may start or the design cannot be developed (<see cref="GpuStats.InvalidReasonKey"/>).
        /// </summary>
        public string Start(GpuDesignInput input, out string reasonKey)
        {
            if (!CanStart(out reasonKey)) return null;
            GpuStats stats = _designer.Evaluate(input);
            if (!stats.Valid)
            {
                reasonKey = stats.InvalidReasonKey ?? GpuDesignMath.ReasonNumbers;
                return null;
            }

            GpuSaveSection section = _state.Data;
            if (section.designs == null) section.designs = new List<GpuDesignSave>();
            var design = new GpuDesignSave
            {
                id = _state.NextId("d"),
                archId = input.ArchId,
                memId = input.MemId,
                coolerId = input.CoolerId,
                node = input.NodeName,
                die = input.DieMm2,
                clockMHz = input.ClockMHz,
                vramMB = input.VramMB,
                busWidth = input.BusWidthBits,
                stats = _designer.Snapshot(input, stats),
            };
            design.name = NameOf(input.Name, design.id);
            section.designs.Add(design);

            float inflation = _game.Inflation;
            var project = new GpuProjectSave
            {
                id = _state.NextId("p"),
                designId = design.id,
                progress = 0f,
                totalCost = stats.ProjectCost,
                days = Math.Max(1, stats.ProjectDays),
                inflationAtStart = inflation > 0f && !float.IsInfinity(inflation) ? inflation : 1f,
                paused = false,
            };
            Projects.Add(project);
            reasonKey = null;
            return project.id;
        }

        /// <summary>FR-4 item 4: a paused project neither moves nor costs; unpaused it goes on from the same progress.</summary>
        public void SetPaused(string projectId, bool paused)
        {
            GpuProjectSave project = Find(projectId);
            if (project != null) project.paused = paused;
        }

        /// <summary>FR-4 item 5: removes the project; the money spent on it is not given back. The design stays (it can be the base of a new one).</summary>
        public bool Cancel(string projectId)
        {
            GpuProjectSave project = Find(projectId);
            if (project == null) return false;
            Projects.Remove(project);
            return true;
        }

        /// <summary>The project with this id, or null.</summary>
        public GpuProjectSave Find(string projectId)
        {
            return string.IsNullOrEmpty(projectId) ? null : Projects.Find(p => p != null && p.id == projectId);
        }

        /// <summary>The money the project costs today (F14).</summary>
        public float DailyCost(GpuProjectSave project)
        {
            if (project == null) return 0f;
            return GpuDesignMath.DailyProjectCost(project.totalCost, project.days, _game.Inflation, project.inflationAtStart);
        }

        /// <summary>One day of development (<c>TickPhase.Tick</c>; FR-4 item 2).</summary>
        public void DailyTick()
        {
            if (!_state.Data.divisionFounded) return;
            List<GpuProjectSave> projects = Projects;
            if (projects.Count == 0) return;

            string player = _game.PlayerKey;
            foreach (GpuProjectSave project in projects.ToArray())
            {
                if (project == null || project.paused) continue;
                int days = Math.Max(1, project.days);
                _game.Spend(player, DailyCost(project), GpuExpense.Development);
                project.progress = Math.Min(1f, Math.Max(0f, project.progress) + 1f / days);

                // A quarter of a day of slack: the sum of days x (1 / days) in single precision may stop a hair below 1.
                if (project.progress >= 1f - 0.25f / days) Finish(project);
            }
        }

        private void Finish(GpuProjectSave project)
        {
            Projects.Remove(project);
            GpuDesignSave design = Design(project.designId);
            if (design == null) return;   // GpuState drops such a project on load; nothing to release

            GpuSaveSection section = _state.Data;
            if (section.products == null) section.products = new List<GpuProductSave>();
            MakeRoom(section.products);
            var product = new GpuProductSave
            {
                id = _state.NextId("g"),
                designId = design.id,
                owner = GpuState.OwnerPlayer,
                vendorId = string.Empty,
                companyKey = _game.PlayerKey,
                name = design.name,
                status = GpuState.StatusReady,
                price = 0f,
                sell = true,
                auto = false,
                lines = 0,
                unitsPerDay = 0,
                stock = 0,
                stats = Copy(design.stats),
                node = design.node,
                die = design.die,
                busWidth = design.busWidth,
                vramMB = design.vramMB,
            };
            section.products.Add(product);

            _game.Notify(NotifyDone, product.name, false);
            Action<string> handlers = ReadyToRelease;
            if (handlers != null) handlers(product.id);
        }

        // The products are limited (limits.products): the oldest product taken off the market gives its place; with none, the list grows (the load cuts it).
        private void MakeRoom(List<GpuProductSave> products)
        {
            int limit = Math.Max(1, Balance.limits.products);
            if (products.Count < limit) return;
            int index = products.FindIndex(p => p != null && p.status == GpuState.StatusRetired);
            if (index >= 0) products.RemoveAt(index);
        }

        private string NameOf(string name, string designId)
        {
            int max = Math.Max(1, Balance.design.nameMaxLength);
            string clean = GpuState.CleanName(name, max);
            if (!string.IsNullOrEmpty(clean)) return clean;
            long number = GpuState.NumberOf(designId);
            return "GPU " + (number > 0 ? number.ToString(CultureInfo.InvariantCulture) : designId);
        }

        private static GpuStatsSave Copy(GpuStatsSave s)
        {
            if (s == null) return new GpuStatsSave();
            return new GpuStatsSave
            {
                transistors = s.transistors,
                computeMops = s.computeMops,
                effectivePerf = s.effectivePerf,
                units = s.units,
                maxClockMHz = s.maxClockMHz,
                bandwidthMBs = s.bandwidthMBs,
                bandwidthRatio = s.bandwidthRatio,
                powerW = s.powerW,
                tempC = s.tempC,
                failureRate = s.failureRate,
                dieMaterial = s.dieMaterial,
                perGoodExtras = s.perGoodExtras,
                projectCost = s.projectCost,
                projectDays = s.projectDays,
                features = s.features != null ? new List<string>(s.features) : new List<string>(),
            };
        }
    }
}
