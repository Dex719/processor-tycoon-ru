using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The view preferences of the player (design: "ModProfile: предпочтения вида"): the last tab, tree or list, the filters and the sort of the
    /// list, the width factor of the side panel. Global, not part of a save. This is the pure model: it reads itself from the JSON object of the
    /// feature's section of the profile (<c>ModProfile.Section("research-tree-ux")</c>) and writes itself back; the adapter
    /// <c>ResearchUxPrefs</c> owns the section and the saving. Every value is validated on reading: a bad one is replaced by the default and
    /// described in <c>problems</c>, never an exception. Unknown members of the section are left alone when writing.
    /// </summary>
    public sealed class UxPrefsModel
    {
        public const string ViewTree = "tree";
        public const string ViewList = "list";
        public const string SortYear = "year";
        public const string SortEta = "eta";
        public const string SortCost = "cost";

        /// <summary>The factor range of the side panel as saved; <c>ui.json</c> may narrow it (<c>sidebar.widthMin</c>, <c>widthMax</c>).</summary>
        public const float WidthFactorMin = 1.0f;

        public const float WidthFactorMax = 2.0f;

        private const int MaxTabIdLength = 64;

        public const string KeyLastTab = "lastTab";
        public const string KeyViewMode = "viewMode";
        public const string KeyOnlyAvailable = "listOnlyAvailable";
        public const string KeyHideResearched = "listHideResearched";
        public const string KeyHideUnnecessary = "listHideUnnecessary";
        public const string KeySort = "listSort";
        public const string KeySidebarWidthFactor = "sidebarWidthFactor";

        /// <summary>The id of the last chosen tab (a built-in one or the id of another feature's tab).</summary>
        public string LastTab = TabDefinition.AllTabId;

        /// <summary><see cref="ViewTree"/> or <see cref="ViewList"/>.</summary>
        public string ViewMode = ViewTree;

        public bool ListOnlyAvailable = true;
        public bool ListHideResearched = true;
        public bool ListHideUnnecessary;

        /// <summary><see cref="SortYear"/>, <see cref="SortEta"/> or <see cref="SortCost"/>.</summary>
        public string ListSort = SortYear;

        public float SidebarWidthFactor = 1.0f;

        /// <summary>The defaults of the spec.</summary>
        public static UxPrefsModel Defaults()
        {
            return new UxPrefsModel();
        }

        /// <summary>
        /// Reads the preferences from the section. A null section (or a member of the wrong type) gives the defaults for what is missing;
        /// each replaced value adds a line to <paramref name="problems"/> (a member that is simply absent adds none).
        /// </summary>
        public static UxPrefsModel Read(JsonObject section, List<string> problems)
        {
            var prefs = new UxPrefsModel();
            if (section == null) return prefs;

            string lastTab;
            if (TryGetString(section, KeyLastTab, problems, out lastTab))
            {
                if (lastTab.Length > 0 && lastTab.Length <= MaxTabIdLength) prefs.LastTab = lastTab;
                else Report(problems, KeyLastTab + ": empty or too long, '" + TabDefinition.AllTabId + "' is used");
            }

            string viewMode;
            if (TryGetString(section, KeyViewMode, problems, out viewMode))
            {
                if (viewMode == ViewTree || viewMode == ViewList) prefs.ViewMode = viewMode;
                else Report(problems, KeyViewMode + ": '" + viewMode + "' is not '" + ViewTree + "' or '" + ViewList + "', '" + ViewTree + "' is used");
            }

            prefs.ListOnlyAvailable = ReadBool(section, KeyOnlyAvailable, prefs.ListOnlyAvailable, problems);
            prefs.ListHideResearched = ReadBool(section, KeyHideResearched, prefs.ListHideResearched, problems);
            prefs.ListHideUnnecessary = ReadBool(section, KeyHideUnnecessary, prefs.ListHideUnnecessary, problems);

            string sort;
            if (TryGetString(section, KeySort, problems, out sort))
            {
                if (sort == SortYear || sort == SortEta || sort == SortCost) prefs.ListSort = sort;
                else Report(problems, KeySort + ": '" + sort + "' is not a known sort, '" + SortYear + "' is used");
            }

            JsonNode factorNode;
            if (section.TryGetValue(KeySidebarWidthFactor, out factorNode))
            {
                var factor = factorNode as JsonValue;
                if (factor != null && factor.Kind == JsonKind.Number && !double.IsNaN(factor.NumberValue) && !double.IsInfinity(factor.NumberValue))
                {
                    prefs.SidebarWidthFactor = ClampWidthFactor((float)factor.NumberValue);
                }
                else
                {
                    Report(problems, KeySidebarWidthFactor + ": not a number, 1 is used");
                }
            }

            return prefs;
        }

        /// <summary>Writes every value into <paramref name="section"/> (members of other versions stay). A null section is ignored.</summary>
        public void Write(JsonObject section)
        {
            if (section == null) return;
            section.Set(KeyLastTab, string.IsNullOrEmpty(LastTab) ? TabDefinition.AllTabId : LastTab);
            section.Set(KeyViewMode, ViewMode == ViewList ? ViewList : ViewTree);
            section.Set(KeyOnlyAvailable, ListOnlyAvailable);
            section.Set(KeyHideResearched, ListHideResearched);
            section.Set(KeyHideUnnecessary, ListHideUnnecessary);
            section.Set(KeySort, ListSort == SortEta || ListSort == SortCost ? ListSort : SortYear);
            section.Set(KeySidebarWidthFactor, ClampWidthFactor(SidebarWidthFactor));
        }

        /// <summary>A factor inside <see cref="WidthFactorMin"/>...<see cref="WidthFactorMax"/>; 1 for a number that is none.</summary>
        public static float ClampWidthFactor(float factor)
        {
            if (float.IsNaN(factor) || float.IsInfinity(factor)) return 1.0f;
            return Math.Max(WidthFactorMin, Math.Min(WidthFactorMax, factor));
        }

        public UxPrefsModel Clone()
        {
            return (UxPrefsModel)MemberwiseClone();
        }

        private static bool TryGetString(JsonObject section, string key, List<string> problems, out string value)
        {
            value = null;
            JsonNode node;
            if (!section.TryGetValue(key, out node)) return false;

            var scalar = node as JsonValue;
            if (scalar == null || scalar.Kind != JsonKind.String)
            {
                Report(problems, key + ": not a string, the default is used");
                return false;
            }
            value = scalar.StringValue ?? string.Empty;
            return true;
        }

        private static bool ReadBool(JsonObject section, string key, bool fallback, List<string> problems)
        {
            JsonNode node;
            if (!section.TryGetValue(key, out node)) return fallback;

            var scalar = node as JsonValue;
            if (scalar == null || scalar.Kind != JsonKind.Bool)
            {
                Report(problems, key + ": not true or false, the default is used");
                return fallback;
            }
            return scalar.BoolValue;
        }

        private static void Report(List<string> problems, string message)
        {
            if (problems != null) problems.Add(message);
        }
    }
}
