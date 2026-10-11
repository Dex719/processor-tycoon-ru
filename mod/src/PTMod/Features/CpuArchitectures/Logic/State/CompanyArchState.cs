using System;
using System.Collections.Generic;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>Why the research of an architecture stands still (FR-2 items 5-6, FR-7 item 4, FR-9 item 2). Saved as a word: see <c>ArchSaveNames</c>.</summary>
    public enum PauseReason
    {
        None = 0,

        /// <summary>The player chose a technology in the shared lab; the last choice wins.</summary>
        TechnologyChosen = 1,

        /// <summary>The company is in default (the player) or has negative money (an AI company).</summary>
        Default = 2,

        /// <summary>The money for the monthly cost ran out.</summary>
        NoFunds = 3,

        /// <summary>The feature was switched off in the hub.</summary>
        FeatureDisabled = 4,
    }

    /// <summary>
    /// The architectures of one company (design.md, Component 3): the family it designs on (<see cref="Active"/>), the family occupying the shared lab
    /// (<see cref="CurrentResearch"/>), whether that research is paused and why, the technology it displaced, and the state of every family it knows.
    /// A company is identified by <c>ICompany.SaveID</c>. Plain data, changed by the services of the feature; the registry owns the objects.
    /// </summary>
    public sealed class CompanyArchState
    {
        public CompanyArchState(int companyId, bool isPlayer)
        {
            CompanyId = companyId;
            IsPlayer = isPlayer;
        }

        /// <summary><c>ICompany.SaveID</c>.</summary>
        public readonly int CompanyId;

        public readonly bool IsPlayer;

        /// <summary>The active family (AI) or the family last chosen in the designer (player); null before the seed.</summary>
        public string Active;

        /// <summary>The family occupying the shared lab; null for none.</summary>
        public string CurrentResearch;

        public bool Paused;

        public PauseReason PauseReason;

        /// <summary><c>Technology.ID</c> of the technology the architecture displaced from the lab; -1 for none.</summary>
        public int ResumeTechnologyId = -1;

        public readonly Dictionary<string, FamilyState> Families = new Dictionary<string, FamilyState>(StringComparer.Ordinal);

        /// <summary>The state of a family, or null when the company has none for it.</summary>
        public FamilyState GetFamily(string id)
        {
            FamilyState state;
            return id != null && Families.TryGetValue(id, out state) ? state : null;
        }

        /// <summary>The state of a family; an empty one is added when there is none.</summary>
        public FamilyState GetOrAddFamily(string id)
        {
            if (id == null) throw new ArgumentNullException("id");

            FamilyState state;
            if (!Families.TryGetValue(id, out state))
            {
                state = new FamilyState(id);
                Families[id] = state;
            }
            return state;
        }

        public bool IsResearched(string id)
        {
            FamilyState state = GetFamily(id);
            return state != null && state.Researched;
        }
    }
}
