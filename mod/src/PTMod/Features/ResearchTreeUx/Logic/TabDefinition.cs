using System;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// A built-in tab of the research tree (FR-1): its id, the <c>ModL10n</c> key of the caption, the technology types it shows and its place
    /// in the strip. The built-in tabs come from <c>Content/research-tree-ux/ui.json</c> (<see cref="TabCatalog"/>); the tabs of other
    /// features are <c>IResearchTabProvider</c>s and are not described by this class.
    /// </summary>
    public sealed class TabDefinition
    {
        /// <summary>The id of the tab that shows every technology and returns the tree to the state of the game (FR-1 item 3).</summary>
        public const string AllTabId = "all";

        /// <summary>Unique among all tabs (the built-in ones and those of other features); also the value saved as the last tab.</summary>
        public string Id;

        /// <summary><c>ModL10n</c> key of the caption.</summary>
        public string TitleKey;

        /// <summary>The values of the game's <c>TechnologyType</c> this tab shows. Never null.</summary>
        public int[] Types;

        /// <summary>Position in the strip, ascending.</summary>
        public int Order;

        public TabDefinition()
        {
            Types = new int[0];
        }

        public TabDefinition(string id, string titleKey, int[] types, int order)
        {
            Id = id;
            TitleKey = titleKey;
            Types = types ?? new int[0];
            Order = order;
        }

        /// <summary>True for the tab "All": it shows everything, also a technology type this version does not know.</summary>
        public bool IsAll
        {
            get { return string.Equals(Id, AllTabId, StringComparison.Ordinal); }
        }

        /// <summary>The tab lists this technology type among its own.</summary>
        public bool Contains(int technologyType)
        {
            int[] types = Types;
            if (types == null) return false;
            for (int i = 0; i < types.Length; i++)
            {
                if (types[i] == technologyType) return true;
            }
            return false;
        }

        public override string ToString()
        {
            return "Tab " + Id + " (order " + Order + ", " + (Types == null ? 0 : Types.Length) + " types)";
        }
    }
}
