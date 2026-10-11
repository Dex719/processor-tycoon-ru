using PTMod.Core;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The family a CPU is designed on (FR-4; design.md Component 6): the player's choice in the list "Архитектура" of the designer
    /// (<see cref="PlayerSelectedFamily"/>), the copy of it taken when the player presses "Create" (<see cref="PendingForCreate"/>, read by the binder
    /// when the project starts) and the family of any company (<see cref="FamilyFor"/>, <see cref="ForCompany"/>: the player's choice, the active
    /// family of an AI company). The multipliers of the design (TSK-010) and the project's binding (TSK-008) both ask here, so the CPU is bound to the
    /// family whose multipliers it got.
    /// <para>
    /// Not to be confused with <c>PTMod.Core.DesignContext</c>, the context of the channel <c>development.cost.design</c> (P19), which carries the parts
    /// of the CPU being designed; inside this namespace the name means this class.
    /// </para>
    /// </summary>
    internal static class DesignContext
    {
        /// <summary>The design of a CPU with no family to apply: IPC, power and development cost ×1.</summary>
        public static readonly DesignDef Neutral = new DesignDef();

        /// <summary>The family picked in the list of the designer (set by <c>DesignerIntegration</c>); null before the list was built.</summary>
        public static string PlayerSelectedFamily;

        /// <summary>The player's family captured by the prefix of <c>CreatorCpu.CreateCpu</c> and cleared by its postfix: the family of the project that starts.</summary>
        public static string PendingForCreate;

        /// <summary>The cores of the design being made in the designer (<c>CreatorCpu.currentCoreCount</c>, noted by <see cref="ExtensionPatches"/>): 1 until the designer was seen.</summary>
        public static int CoreCount = 1;

        /// <summary>SMT of the design being made (<c>CreatorCpu.smtEnabled</c>).</summary>
        public static bool Smt;

        /// <summary>
        /// Notes the cores and SMT of the design the designer shows now (every frame, two comparisons). True when either changed since the last call, so
        /// that the lines of the designer about the gates of the extensions are written again (FR-13 item 2).
        /// </summary>
        public static bool NoteDesign(int coreCount, bool smt)
        {
            if (coreCount == CoreCount && smt == Smt) return false;
            CoreCount = coreCount;
            Smt = smt;
            return true;
        }

        /// <summary>
        /// The family the player designs on now: <see cref="PlayerSelectedFamily"/> when the feature is not neutral, the designer part works, and the
        /// family is in the catalog and counts as researched for the player (no list allocated: called every frame by the designer's postfixes).
        /// </summary>
        public static string SelectedForPlayer()
        {
            ArchitecturesFeature feature = ArchitecturesFeature.Instance;
            string selected = PlayerSelectedFamily;
            if (feature == null || selected == null || feature.IsNeutralNow || (feature.Parts & ArchParts.Designer) == 0) return null;

            ArchCatalog catalog = feature.Catalog;
            ArchRegistry registry = feature.Registry;
            if (catalog == null || registry == null) return null;

            ArchFamily family = catalog.Get(selected);
            CompanyArchState player = registry.Player;
            return ArchResearchMath.CountsAsResearched(family, player == null ? null : player.Families) ? selected : null;
        }

        /// <summary>
        /// The family a company designs on (FR-4 items 2, 5): the player - <see cref="SelectedForPlayer"/>; an AI company - its active family when the
        /// catalog knows it. Null while the feature is neutral (the design is vanilla then) and for a company without one.
        /// </summary>
        public static string FamilyFor(ICompany company)
        {
            ArchitecturesFeature feature = ArchitecturesFeature.Instance;
            if (feature == null || ReferenceEquals(company, null) || feature.IsNeutralNow || feature.Registry == null) return null;
            if ((feature.Parts & ArchParts.Designer) == 0) return null;
            if (company.IsPlayer) return SelectedForPlayer();

            CompanyArchState state = feature.Registry.GetOrNull(company.SaveID);
            string active = state == null ? null : state.Active;
            return active != null && feature.Catalog.Contains(active) ? active : null;
        }

        /// <summary>The multipliers of the design of a company (<see cref="FamilyFor"/>); <see cref="Neutral"/> when there is no family to apply.</summary>
        public static DesignDef ForCompany(ICompany company)
        {
            return DesignOf(FamilyFor(company));
        }

        /// <summary>
        /// The multipliers of the design the player is making now (<see cref="SelectedForPlayer"/>); <see cref="Neutral"/> when there is none. The
        /// postfixes of the designer ask this every frame: no allocation, a few dictionary lookups.
        /// </summary>
        public static DesignDef ForPlayer()
        {
            return DesignOf(SelectedForPlayer());
        }

        /// <summary>
        /// The surcharge of the project cost for the immature tools of a young architecture (FR-12 item 3, stage 2): <c>0.25 × (1 − months / 12)</c> for the first twelve
        /// months after the company researched the family (<see cref="ImmatureTools"/>), 0 at stage 1, while the feature is neutral, for a family given at the start
        /// and for a company without a state. The provider of <c>development.cost.design</c> and the line of the designer ask this, so they never disagree.
        /// </summary>
        public static float ImmatureSurcharge(ICompany company, string familyId)
        {
            ArchitecturesFeature feature = ArchitecturesFeature.Instance;
            if (feature == null || ReferenceEquals(company, null) || familyId == null || feature.Registry == null) return 0f;
            if (feature.IsNeutralNow || !feature.Balance.IsFullStage) return 0f;

            CompanyArchState state = feature.Registry.GetOrNull(company.SaveID);
            return state == null ? 0f : ImmatureTools.Surcharge(state.GetFamily(familyId), ModTicks.Today, feature.Balance.migration);
        }

        /// <summary><c>1 + <see cref="ImmatureSurcharge"/></c>: the factor the immature tools put on the cost of a project (1 when the tools are mature).</summary>
        public static float ImmatureCostFactor(ICompany company, string familyId)
        {
            return 1f + ImmatureSurcharge(company, familyId);
        }

        private static DesignDef DesignOf(string familyId)
        {
            ArchitecturesFeature feature = ArchitecturesFeature.Instance;
            ArchFamily family = familyId == null || feature == null || feature.Catalog == null ? null : feature.Catalog.Get(familyId);
            return family == null || family.Design == null ? Neutral : family.Design;
        }

        /// <summary>Forgets the choice (a new scene: the next designer builds its list from the saved <c>CompanyArchState.Active</c>).</summary>
        public static void Reset()
        {
            PlayerSelectedFamily = null;
            PendingForCreate = null;
            CoreCount = 1;
            Smt = false;
        }
    }
}
