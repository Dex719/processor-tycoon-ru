using System;
using System.Collections.Generic;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The list "Архитектура" of the CPU designer (FR-4 item 1): which families the player may design on and which one is picked when the window
    /// opens. Pure: <c>DesignerIntegration</c> turns the ids into the names of the dropdown and writes the pick back to <c>CompanyArchState.Active</c>.
    /// </summary>
    public static class DesignerChoices
    {
        /// <summary>
        /// The families of the list, in the order of the catalog: every family the company has researched, plus the families researched at once
        /// (<c>proprietary</c>, FR-2 item 9). A company without a state (not sown yet) gets the families researched at once. Never empty for a
        /// catalog with the default family.
        /// </summary>
        public static List<string> Options(ArchCatalog catalog, CompanyArchState state)
        {
            var options = new List<string>();
            if (catalog == null) return options;

            IReadOnlyDictionary<string, FamilyState> families = state == null ? null : state.Families;
            foreach (ArchFamily family in catalog.Families)
            {
                if (ArchResearchMath.CountsAsResearched(family, families)) options.Add(family.Id);
            }
            if (options.Count == 0 && catalog.Default != null) options.Add(catalog.Default.Id);
            return options;
        }

        /// <summary>
        /// The entry picked when the list is built: <paramref name="preferred"/> (the family chosen last, <c>CompanyArchState.Active</c>) when it is
        /// in the list, otherwise the newest family of the list (the largest year; of two of the same year the later one in the catalog).
        /// -1 for an empty list.
        /// </summary>
        public static int DefaultIndex(ArchCatalog catalog, IList<string> options, string preferred)
        {
            if (options == null || options.Count == 0) return -1;

            if (preferred != null)
            {
                for (int i = 0; i < options.Count; i++)
                {
                    if (string.Equals(options[i], preferred, StringComparison.Ordinal)) return i;
                }
            }

            int best = 0;
            int bestYear = int.MinValue;
            for (int i = 0; i < options.Count; i++)
            {
                ArchFamily family = catalog == null ? null : catalog.Get(options[i]);
                int year = family == null ? int.MinValue : family.Year;
                if (year >= bestYear)
                {
                    best = i;
                    bestYear = year;
                }
            }
            return best;
        }

        /// <summary>The two lists hold the same ids in the same order (the dropdown is rebuilt only when they differ).</summary>
        public static bool SameOptions(IList<string> a, IList<string> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            }
            return true;
        }

        /// <summary>The family the player designs on: <paramref name="selected"/> when it is one of the options, otherwise null (nothing to apply or bind).</summary>
        public static string Validate(IList<string> options, string selected)
        {
            if (options == null || selected == null) return null;
            for (int i = 0; i < options.Count; i++)
            {
                if (string.Equals(options[i], selected, StringComparison.Ordinal)) return selected;
            }
            return null;
        }
    }
}
