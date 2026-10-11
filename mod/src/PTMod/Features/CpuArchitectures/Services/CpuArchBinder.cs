using System;
using PTMod.Core;
using CpuProject = ProcessorTycoon.Save.SaveObject.CpuProject;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// Binds every CPU to a family (D4, FR-5; design.md Component 7). A project of a CPU has no stable object and its CPU no id until it is
    /// finished, so the family chosen at the start waits under the project's fingerprint (company, name, year, IPS) in <see cref="ArchRegistry.Pending"/>,
    /// which goes to the save:
    /// <list type="number">
    /// <item><b>Start</b> (<c>Project.SetProjectType(SaveObject.CpuProject)</c>, postfix, not while a save loads): the player's project waits with the family
    /// of the designer (<see cref="DesignContext.PendingForCreate"/>), an AI's with its active family.</item>
    /// <item><b>Player's CPU finished</b> (<c>ProjectCompletedHandler.AddCpu</c>, postfix; also for a project restored from a save): the oldest waiting
    /// project with the same fingerprint gives its family to <c>Cpu.SaveID</c>.</item>
    /// <item><b>AI's CPU released</b> (<c>ModEvents.CpuReleased</c> of the core; not while a save loads, at most once per CPU): the same; a CPU
    /// released during the fast-forward of a new game is left alone (it gets the default lazily, D7, D8).</item>
    /// <item><b>No waiting project</b>: the lazy default of <see cref="ArchRegistry.ResolveCpu"/> (owner and year). A variant (<c>Cpu.Base</c> not null)
    /// takes the family of its base CPU.</item>
    /// <item><b>Transfer</b> (<c>ModEvents.CpuTransferred</c>): the binding stays (it is by <c>SaveID</c>); the rating cache of both companies is dropped.</item>
    /// </list>
    /// Nothing is bound while the feature is neutral; the patches on <c>Company.AddCpu</c>/<c>AICompany.AddCpu</c> belong to the core (P20).
    /// </summary>
    internal sealed class CpuArchBinder
    {
        private readonly ArchitecturesFeature _feature;
        private readonly ArchRatingProvider _rating;

        public CpuArchBinder(ArchitecturesFeature feature, ArchRatingProvider rating)
        {
            if (feature == null) throw new ArgumentNullException("feature");
            if (rating == null) throw new ArgumentNullException("rating");
            _feature = feature;
            _rating = rating;
        }

        /// <summary>The fingerprint of a project: <c>CompanyID</c>, <c>Name</c>, <c>Year</c>, <c>Ips</c> of <c>SaveObject.CpuProject</c>.</summary>
        public static CpuFingerprint FingerprintOf(CpuProject project)
        {
            return new CpuFingerprint(project.CompanyID, project.Name, project.Year, project.Ips);
        }

        /// <summary>The fingerprint of a CPU: <c>Company.SaveID</c> (−1 without a company), <c>Name</c>, <c>Year</c>, <c>Ips</c>.</summary>
        public static CpuFingerprint FingerprintOf(GameCpu cpu)
        {
            ICompany company = cpu.Company;
            return new CpuFingerprint(ReferenceEquals(company, null) ? -1 : company.SaveID, cpu.Name, cpu.Year, cpu.Ips);
        }

        private ArchRegistry Registry
        {
            get { return _feature.Registry; }
        }

        private bool CanBind
        {
            get { return _feature.Registry != null && !_feature.IsNeutralNow; }
        }

        // ---- the hooks ----

        /// <summary>Postfix of <c>Project.SetProjectType(SaveObject.CpuProject)</c>: the new project waits with its family (FR-5 item 1).</summary>
        public void OnProjectTyped(CpuProject project)
        {
            if (project == null || !CanBind || ModTicks.IsLoading) return;   // a loaded project's family is in the save already

            string family = FamilyOfNewProject(project.CompanyID);
            if (family == null) return;   // nothing chosen: the CPU gets the default when it comes out

            CpuFingerprint fingerprint = FingerprintOf(project);
            if (Registry.AddPending(fingerprint, family)) ModLog.Debug(ArchitecturesFeature.FeatureId, "binder: " + fingerprint + " waits with " + family);
            else ModLog.Debug(ArchitecturesFeature.FeatureId, "binder: " + fingerprint + " cannot wait (unusable fingerprint)");
        }

        /// <summary>Postfix of <c>ProjectCompletedHandler.AddCpu</c>: the player's CPU is finished and has its <c>SaveID</c> (FR-5 item 2).</summary>
        public void OnPlayerCpuCompleted(GameCpu cpu)
        {
            if (cpu == null || !CanBind) return;
            BindNew(cpu);
        }

        /// <summary>
        /// <c>ModEvents.CpuReleased</c>: an AI company released a CPU (FR-5 item 2); for the player a safety net for a CPU that has no binding yet.
        /// Skipped during the fast-forward of a new game (the core does not raise it while a save loads).
        /// </summary>
        public void OnCpuReleased(ICompany owner, GameCpu cpu, bool isVariant, bool duringFastForward)
        {
            if (duringFastForward || cpu == null || !CanBind) return;

            string bound;
            if (Registry.TryGetCpuArch(cpu.SaveID, out bound)) return;   // the player's CPU was bound when it was finished
            BindNew(cpu);
        }

        /// <summary><c>ModEvents.CpuTransferred</c> (FR-5 item 8): the binding stays with the CPU; the rating of both companies is asked anew.</summary>
        public void OnCpuTransferred(ICompany from, ICompany to, GameCpu cpu)
        {
            if (_feature.Registry == null) return;
            _rating.InvalidateCompany(from);
            _rating.InvalidateCompany(to);
        }

        // ---- binding ----

        /// <summary>The family of a project that starts now: the designer's for the player, the active one for an AI company; null for none.</summary>
        private string FamilyOfNewProject(int companyId)
        {
            ICompany player = ModCompanies.Resolve(ModCompanies.PlayerKey);
            if (!ReferenceEquals(player, null) && player.SaveID == companyId) return DesignContext.PendingForCreate;

            CompanyArchState state = Registry.GetOrNull(companyId);
            string active = state == null ? null : state.Active;
            return active != null && _feature.Catalog.Contains(active) ? active : null;
        }

        /// <summary>
        /// A new CPU: the family of its waiting project, bound now; a variant follows its base and any other CPU without a project gets the lazy
        /// default (<see cref="ArchRegistry.ResolveCpu"/>: kept once the state has been sown). The waiting project is taken out of the queue in every
        /// case, so that a variant does not leave it behind.
        /// </summary>
        private void BindNew(GameCpu cpu)
        {
            ArchRegistry registry = Registry;
            string waiting = registry.TakePending(FingerprintOf(cpu));

            GameCpu baseCpu = cpu.Base;
            bool variant = baseCpu != null && !ReferenceEquals(baseCpu, cpu);

            string family;
            if (!variant && waiting != null && ArchRegistry.IsBindableCpuId(cpu.SaveID) && registry.Bind(cpu.SaveID, waiting)) family = waiting;
            else family = registry.ResolveCpu(cpu);

            string source = variant ? "variant" : waiting != null ? "project" : "default";
            ModLog.Debug(ArchitecturesFeature.FeatureId, "binder: CPU " + cpu.SaveID + " '" + cpu.Name + "' is " + family + " (" + source + ")");
            _rating.InvalidateCompany(cpu.Company);
        }
    }
}
