using System;
using System.Collections.Generic;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The rush projects that have been started and have not produced their CPU yet (FR-20 item 3, design.md Component 15). The <c>SaveID</c> of a CPU is given when its
    /// project ends (<c>CreatorCpu.CreateCpu</c> closure, then <c>ProjectCompletedHandler.AddCpu</c>), so what is known when the player presses "Create" is the name of the
    /// CPU and the day the project is planned to end. That pair waits here, in the saved data (<see cref="FlawSaveData.rushProjects"/>, so a save and a load in the middle
    /// of a project lose nothing), and becomes the mark of the CPU (<c>rushCpuIds</c>, through <see cref="FlawStore.MarkRush"/>) when a CPU of that name arrives within
    /// <see cref="ToleranceDays"/> days of the planned end. Pure: no game types, no clock (the day is given by the caller). Main thread only.
    /// <para>
    /// A project that never ends (it cannot be cancelled in the game, but a save can be edited or the mod switched off meanwhile) must not stay in the save for ever:
    /// <see cref="Prune"/> drops the ones that are more than <see cref="StaleAfterDays"/> days past the planned end, and the list never holds more than
    /// <see cref="MaxPending"/> entries.
    /// </para>
    /// </summary>
    public sealed class RushProjectBook
    {
        /// <summary>The CPU may arrive this many days before or after the planned end and still be the project (FR-20: plus or minus 3 days).</summary>
        public const int ToleranceDays = 3;

        /// <summary>A pending project this many days past its planned end is forgotten (the tolerance is 3 days; the rest is a margin for a delayed project).</summary>
        public const int StaleAfterDays = 60;

        /// <summary>The most projects kept at once (a player has a handful; the cap protects a save from growing without bound).</summary>
        public const int MaxPending = 64;

        /// <summary>The key of the only company that rushes (the AI designs its CPUs on its own).</summary>
        public const string PlayerKey = "player";

        private readonly FlawStore _store;

        public RushProjectBook(FlawStore store)
        {
            if (store == null) throw new ArgumentNullException("store");
            _store = store;
        }

        /// <summary>The number of projects waiting for their CPU.</summary>
        public int Count
        {
            get { return Projects().Count; }
        }

        /// <summary>A copy of the waiting projects, the oldest first.</summary>
        public List<RushProjectDto> Snapshot()
        {
            var copy = new List<RushProjectDto>();
            foreach (RushProjectDto project in Projects())
            {
                if (project != null) copy.Add(new RushProjectDto { companyKey = project.companyKey, cpuName = project.cpuName, plannedEndDay = project.plannedEndDay });
            }
            return copy;
        }

        /// <summary>
        /// The player started a rushed project (<c>CreatorCpu.CreateCpu</c> with the switch on): remembers its name and the planned end. A project with the same name and
        /// the same end day is not added twice. <c>false</c> (nothing is stored) for an empty name or an end day before the start.
        /// </summary>
        /// <param name="cpuName">The name typed into the designer (the CPU gets the same one).</param>
        /// <param name="plannedEndDay">Today plus the length of the (already rushed) project.</param>
        /// <param name="today">Today as a day number; stale projects are dropped by it.</param>
        public bool Register(string cpuName, int plannedEndDay, int today)
        {
            if (string.IsNullOrEmpty(cpuName) || plannedEndDay <= 0 || plannedEndDay < today) return false;

            Prune(today);
            List<RushProjectDto> projects = Projects();
            foreach (RushProjectDto existing in projects)
            {
                if (existing != null && existing.plannedEndDay == plannedEndDay && string.Equals(existing.cpuName, cpuName, StringComparison.Ordinal)
                    && string.Equals(existing.companyKey, PlayerKey, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            while (projects.Count >= MaxPending) projects.RemoveAt(0);   // the oldest is the least likely to ever finish
            projects.Add(new RushProjectDto { companyKey = PlayerKey, cpuName = cpuName, plannedEndDay = plannedEndDay });
            return true;
        }

        /// <summary>
        /// A project of the player ended (<c>ProjectCompletedHandler.AddCpu</c>): when a waiting project has the name of the CPU and its planned end is within
        /// <see cref="ToleranceDays"/> days of <paramref name="today"/>, the project is taken off the list and the CPU is marked as rushed. Of several candidates the nearest
        /// end wins (the first in the list on a tie). <c>false</c> when nothing matches: an ordinary project, or a rush that was lost.
        /// </summary>
        public bool Complete(string cpuName, int cpuSaveId, int today)
        {
            if (string.IsNullOrEmpty(cpuName)) return false;

            List<RushProjectDto> projects = Projects();
            int best = -1;
            int bestDistance = int.MaxValue;
            for (int i = 0; i < projects.Count; i++)
            {
                RushProjectDto project = projects[i];
                if (project == null || !string.Equals(project.companyKey, PlayerKey, StringComparison.Ordinal)) continue;
                if (!string.Equals(project.cpuName, cpuName, StringComparison.Ordinal)) continue;

                int distance = Math.Abs(project.plannedEndDay - today);
                if (distance > ToleranceDays || distance >= bestDistance) continue;
                best = i;
                bestDistance = distance;
            }

            if (best < 0)
            {
                Prune(today);
                return false;
            }

            projects.RemoveAt(best);
            _store.MarkRush(cpuSaveId);
            Prune(today);
            return true;
        }

        /// <summary>Drops the projects that ended more than <see cref="StaleAfterDays"/> days ago (and the broken entries). Returns how many were dropped.</summary>
        public int Prune(int today)
        {
            if (today <= 0) return 0;

            List<RushProjectDto> projects = Projects();
            int dropped = 0;
            for (int i = projects.Count - 1; i >= 0; i--)
            {
                RushProjectDto project = projects[i];
                if (project != null && !string.IsNullOrEmpty(project.cpuName) && project.plannedEndDay + StaleAfterDays >= today) continue;

                projects.RemoveAt(i);
                dropped++;
            }
            return dropped;
        }

        private List<RushProjectDto> Projects()
        {
            FlawSaveData data = _store.Data;
            if (data.rushProjects == null) data.rushProjects = new List<RushProjectDto>();
            return data.rushProjects;
        }
    }
}
