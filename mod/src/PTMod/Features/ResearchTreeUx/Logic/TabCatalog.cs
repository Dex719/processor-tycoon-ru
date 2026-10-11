using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The list of built-in tabs: the defaults compiled into the code (the same numbers as <c>Content/research-tree-ux/ui.json</c>) and the
    /// reading of the <c>tabs</c> array of that file with the validation of design.md ("Контент"): <c>types</c> are 0...6, the ids are
    /// unique, <c>order</c> is a whole number. A broken record is skipped and described in <c>problems</c> (the adapter writes them to the
    /// log, decision 0006); when nothing usable is left the defaults are used. The tab "All" always exists, because the saved last tab and
    /// the "back to the vanilla tree" behaviour rely on it.
    /// </summary>
    public static class TabCatalog
    {
        /// <summary>The lowest and the highest value of the game's <c>TechnologyType</c>.</summary>
        public const int MinType = 0;

        public const int MaxType = 6;

        /// <summary>The built-in tabs as ui.json describes them: "All" and the seven categories, ordered by <see cref="TabDefinition.Order"/>. A new list each call.</summary>
        public static IReadOnlyList<TabDefinition> Defaults()
        {
            return new List<TabDefinition>
            {
                new TabDefinition(TabDefinition.AllTabId, "rtux.tab.all", AllTypes(), 0),
                new TabDefinition("package", "rtux.tab.package", new[] { 0 }, 10),
                new TabDefinition("lithography", "rtux.tab.lithography", new[] { 1 }, 20),
                new TabDefinition("memory", "rtux.tab.memory", new[] { 2 }, 30),
                new TabDefinition("frequency", "rtux.tab.frequency", new[] { 3 }, 40),
                new TabDefinition("cache", "rtux.tab.cache", new[] { 4 }, 50),
                new TabDefinition("wafer", "rtux.tab.wafer", new[] { 5 }, 60),
                new TabDefinition("multicore", "rtux.tab.multicore", new[] { 6 }, 70),
            };
        }

        /// <summary>
        /// Reads the <c>tabs</c> array of the merged ui.json. <paramref name="config"/> may be null or lack the array (the defaults are
        /// returned, with a note in <paramref name="problems"/> when the array was expected but unusable). Never null, never empty, ordered
        /// by <c>order</c> and then by position in the file.
        /// </summary>
        public static IReadOnlyList<TabDefinition> FromConfig(JsonObject config, List<string> problems)
        {
            if (config == null) return Defaults();

            JsonNode node;
            if (!config.TryGetValue("tabs", out node))
            {
                return Defaults();   // no "tabs" in the file: the built-in values stand, nothing to report
            }

            var tabs = node as JsonArray;
            if (tabs == null)
            {
                Report(problems, "tabs: expected an array, the default tabs are used");
                return Defaults();
            }

            var result = new List<TabDefinition>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int index = 0;
            foreach (JsonNode item in tabs)
            {
                string path = "tabs[" + index + "]";
                index++;

                TabDefinition tab = ReadTab(item as JsonObject, path, problems);
                if (tab == null) continue;
                if (!seen.Add(tab.Id))
                {
                    Report(problems, path + ": the id '" + tab.Id + "' is used already, the record is skipped");
                    continue;
                }
                result.Add(tab);
            }

            if (result.Count == 0)
            {
                Report(problems, "tabs: no usable record, the default tabs are used");
                return Defaults();
            }

            if (!seen.Contains(TabDefinition.AllTabId))
            {
                Report(problems, "tabs: the tab '" + TabDefinition.AllTabId + "' is missing, the built-in one is added");
                result.Insert(0, Defaults()[0]);
            }

            return SortByOrder(result);
        }

        private static TabDefinition ReadTab(JsonObject record, string path, List<string> problems)
        {
            if (record == null)
            {
                Report(problems, path + ": expected an object, the record is skipped");
                return null;
            }

            string id = record.GetString("id");
            if (string.IsNullOrEmpty(id) || id.Trim().Length == 0)
            {
                Report(problems, path + ": 'id' is missing or empty, the record is skipped");
                return null;
            }

            string titleKey = record.GetString("titleKey");
            if (string.IsNullOrEmpty(titleKey))
            {
                Report(problems, path + " (" + id + "): 'titleKey' is missing, the record is skipped");
                return null;
            }

            int order;
            if (!record.TryGetInt32("order", out order))
            {
                Report(problems, path + " (" + id + "): 'order' must be a whole number, the record is skipped");
                return null;
            }

            JsonArray typeNodes = record.GetArray("types");
            if (typeNodes == null || typeNodes.Count == 0)
            {
                Report(problems, path + " (" + id + "): 'types' must be a non-empty array, the record is skipped");
                return null;
            }

            var types = new List<int>(typeNodes.Count);
            foreach (JsonNode typeNode in typeNodes)
            {
                var value = typeNode as JsonValue;
                int type;
                if (value == null || !value.TryGetInt32(out type) || type < MinType || type > MaxType)
                {
                    Report(problems, path + " (" + id + "): 'types' may hold only the whole numbers " + MinType + "..." + MaxType + ", the record is skipped");
                    return null;
                }
                if (!types.Contains(type)) types.Add(type);
            }

            return new TabDefinition(id, titleKey, types.ToArray(), order);
        }

        private static IReadOnlyList<TabDefinition> SortByOrder(List<TabDefinition> tabs)
        {
            // A stable sort: the position in the file breaks a tie, List.Sort is not stable.
            var indexed = new List<KeyValuePair<int, TabDefinition>>(tabs.Count);
            for (int i = 0; i < tabs.Count; i++) indexed.Add(new KeyValuePair<int, TabDefinition>(i, tabs[i]));
            indexed.Sort((a, b) =>
            {
                int byOrder = a.Value.Order.CompareTo(b.Value.Order);
                return byOrder != 0 ? byOrder : a.Key.CompareTo(b.Key);
            });

            var sorted = new List<TabDefinition>(tabs.Count);
            foreach (KeyValuePair<int, TabDefinition> pair in indexed) sorted.Add(pair.Value);
            return sorted;
        }

        private static int[] AllTypes()
        {
            var types = new int[MaxType - MinType + 1];
            for (int i = 0; i < types.Length; i++) types[i] = MinType + i;
            return types;
        }

        private static void Report(List<string> problems, string message)
        {
            if (problems != null) problems.Add(message);
        }
    }
}
