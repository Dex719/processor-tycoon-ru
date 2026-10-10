using System;
using System.Collections.Generic;
using System.Text;

namespace PTMod.Core.Logic.Features
{
    /// <summary>A feature as the graph sees it: its id and the ids of the features it hard-depends on (<c>IModFeature.DependsOn</c>).</summary>
    public sealed class FeatureNode
    {
        /// <param name="id">Feature id (the slug of its spec).</param>
        /// <param name="dependsOn">Hard dependencies in declaration order; null, blank and repeated entries are dropped.</param>
        public FeatureNode(string id, IEnumerable<string> dependsOn = null)
        {
            Id = id ?? string.Empty;

            var list = new List<string>();
            if (dependsOn != null)
            {
                foreach (string dependency in dependsOn)
                {
                    if (string.IsNullOrWhiteSpace(dependency) || list.Contains(dependency)) continue;
                    list.Add(dependency);
                }
            }
            DependsOn = list;
        }

        public string Id { get; }

        public IReadOnlyList<string> DependsOn { get; }
    }

    /// <summary>
    /// Dependency graph of the features (FR-3): a deterministic topological order (dependencies first),
    /// dependency cycles, dependencies on features that do not exist, and the mapping from a type's
    /// namespace to the owning feature. Immutable; built once at startup from the discovered features.
    /// <para>
    /// The framework id <see cref="CoreFeatureId"/> is not a node: a dependency on it is always satisfied and
    /// is ignored by ordering. Bad input never throws: duplicate and blank ids are skipped and reported in
    /// <see cref="Problems"/>.
    /// </para>
    /// </summary>
    public sealed class FeatureGraph
    {
        /// <summary>Id of the framework itself; the owner of every type outside <c>PTMod.Features.&lt;Name&gt;</c>.</summary>
        public const string CoreFeatureId = "mod-framework";

        private const string FeaturesNamespace = "PTMod.Features.";

        private readonly Dictionary<string, FeatureNode> nodes = new Dictionary<string, FeatureNode>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> positions = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> dependents = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> missing = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyList<string>> cycleOf = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        private readonly List<string> order = new List<string>();
        private readonly List<IReadOnlyList<string>> cycles = new List<IReadOnlyList<string>>();
        private readonly List<string> problems = new List<string>();

        private static readonly IReadOnlyList<string> NoIds = new string[0];

        public FeatureGraph(IEnumerable<FeatureNode> features)
        {
            if (features == null) throw new ArgumentNullException(nameof(features));

            foreach (FeatureNode node in features)
            {
                if (node == null || string.IsNullOrWhiteSpace(node.Id))
                {
                    problems.Add("feature with an empty id is ignored");
                    continue;
                }
                if (nodes.ContainsKey(node.Id))
                {
                    problems.Add("duplicate feature id '" + node.Id + "' is ignored");
                    continue;
                }
                nodes[node.Id] = node;
            }

            var ids = new List<string>(nodes.Keys);
            ids.Sort(StringComparer.Ordinal);

            // Edges point from a feature to its dependencies that exist; the others are reported.
            var edges = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (string id in ids)
            {
                var existing = new List<string>();
                foreach (string dependency in nodes[id].DependsOn)
                {
                    if (dependency == CoreFeatureId) continue;
                    if (nodes.ContainsKey(dependency))
                    {
                        existing.Add(dependency);
                        List<string> list;
                        if (!dependents.TryGetValue(dependency, out list)) dependents[dependency] = list = new List<string>();
                        list.Add(id);
                    }
                    else
                    {
                        List<string> list;
                        if (!missing.TryGetValue(id, out list)) missing[id] = list = new List<string>();
                        list.Add(dependency);
                        problems.Add("feature '" + id + "' depends on unknown feature '" + dependency + "'");
                    }
                }
                existing.Sort(StringComparer.Ordinal);
                edges[id] = existing;
            }

            BuildOrder(ids, edges);
        }

        /// <summary>
        /// Every feature, dependencies before dependents. Deterministic: independent features and the members
        /// of one cycle go in ordinal id order. Members of a cycle are included (they exist, but are unavailable).
        /// </summary>
        public IReadOnlyList<string> Order
        {
            get { return order; }
        }

        public int Count
        {
            get { return order.Count; }
        }

        /// <summary>Groups of features that depend on each other (including a feature that depends on itself), members in ordinal order.</summary>
        public IReadOnlyList<IReadOnlyList<string>> Cycles
        {
            get { return cycles; }
        }

        /// <summary>Human-readable (English) structural problems for the log: duplicates, unknown dependencies, cycles.</summary>
        public IReadOnlyList<string> Problems
        {
            get { return problems; }
        }

        public bool Contains(string id)
        {
            return id != null && nodes.ContainsKey(id);
        }

        /// <summary>Position in <see cref="Order"/>, or -1 for an unknown id.</summary>
        public int IndexOf(string id)
        {
            int position;
            return id != null && positions.TryGetValue(id, out position) ? position : -1;
        }

        /// <summary>Declared dependencies of a feature, in declaration order (including unknown ones and the framework id). Empty for an unknown id.</summary>
        public IReadOnlyList<string> DependenciesOf(string id)
        {
            FeatureNode node;
            return id != null && nodes.TryGetValue(id, out node) ? node.DependsOn : NoIds;
        }

        /// <summary>Features that declare a direct dependency on <paramref name="id"/>, in ordinal order.</summary>
        public IReadOnlyList<string> DependentsOf(string id)
        {
            List<string> list;
            return id != null && dependents.TryGetValue(id, out list) ? list : NoIds;
        }

        /// <summary>Declared dependencies of a feature that are not registered features, in declaration order.</summary>
        public IReadOnlyList<string> MissingDependenciesOf(string id)
        {
            List<string> list;
            return id != null && missing.TryGetValue(id, out list) ? list : NoIds;
        }

        /// <summary>All members of the dependency cycle the feature belongs to (ordinal order), or an empty list.</summary>
        public IReadOnlyList<string> CycleOf(string id)
        {
            IReadOnlyList<string> members;
            return id != null && cycleOf.TryGetValue(id, out members) ? members : NoIds;
        }

        public bool IsInCycle(string id)
        {
            return CycleOf(id).Count > 0;
        }

        /// <summary>
        /// Feature that owns code in the given namespace: <c>PTMod.Features.&lt;Name&gt;[.…]</c> gives the slug of
        /// &lt;Name&gt; (<c>EventSystem</c> becomes <c>event-system</c>); the rest of <c>PTMod</c> (<c>PTMod.Core…</c>,
        /// <c>PTMod.Features</c> itself) is the framework, <see cref="CoreFeatureId"/>; a namespace outside
        /// <c>PTMod</c> (or null) has no owner and gives null. Pure function over strings; the
        /// runtime <c>ModFeatures.FeatureOfType</c> passes <c>type.Namespace</c>.
        /// </summary>
        public static string FeatureOfNamespace(string ns)
        {
            if (string.IsNullOrEmpty(ns)) return null;
            if (ns == "PTMod") return CoreFeatureId;
            if (!ns.StartsWith("PTMod.", StringComparison.Ordinal)) return null;
            if (!ns.StartsWith(FeaturesNamespace, StringComparison.Ordinal)) return CoreFeatureId;

            int start = FeaturesNamespace.Length;
            int dot = ns.IndexOf('.', start);
            string name = dot < 0 ? ns.Substring(start) : ns.Substring(start, dot - start);
            return name.Length == 0 ? CoreFeatureId : PascalToSlug(name);
        }

        /// <summary>
        /// Folder/namespace name to feature slug: a hyphen goes before an upper-case letter that follows a
        /// lower-case letter or digit (and before the last capital of an acronym), everything is lower-cased.
        /// <c>ResearchTreeUx</c> gives <c>research-tree-ux</c>, <c>XMLParser</c> gives <c>xml-parser</c>.
        /// </summary>
        public static string PascalToSlug(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;

            var sb = new StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsUpper(c))
                {
                    if (i > 0)
                    {
                        char previous = name[i - 1];
                        bool afterLowerOrDigit = char.IsLower(previous) || char.IsDigit(previous);
                        bool acronymEnd = char.IsUpper(previous) && i + 1 < name.Length && char.IsLower(name[i + 1]);
                        if (afterLowerOrDigit || acronymEnd) sb.Append('-');
                    }
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        // ---- ordering: Tarjan's strongly connected components over "feature -> dependency" edges ----

        /// <summary>
        /// Tarjan emits a component only after every component reachable from it, that is after its dependencies,
        /// so the emission order is already "dependencies first". Visiting in ordinal order makes it deterministic.
        /// </summary>
        private void BuildOrder(List<string> ids, Dictionary<string, List<string>> edges)
        {
            var discovery = new Dictionary<string, int>(StringComparer.Ordinal);
            var low = new Dictionary<string, int>(StringComparer.Ordinal);
            var onStack = new HashSet<string>(StringComparer.Ordinal);
            var stack = new Stack<string>();
            int counter = 0;

            Action<string> visit = null;
            visit = v =>
            {
                counter++;
                discovery[v] = counter;
                low[v] = counter;
                stack.Push(v);
                onStack.Add(v);

                foreach (string w in edges[v])
                {
                    if (!discovery.ContainsKey(w))
                    {
                        visit(w);
                        low[v] = Math.Min(low[v], low[w]);
                    }
                    else if (onStack.Contains(w))
                    {
                        low[v] = Math.Min(low[v], discovery[w]);
                    }
                }

                if (low[v] != discovery[v]) return;

                var component = new List<string>();
                string member;
                do
                {
                    member = stack.Pop();
                    onStack.Remove(member);
                    component.Add(member);
                }
                while (member != v);
                component.Sort(StringComparer.Ordinal);

                foreach (string id in component)
                {
                    positions[id] = order.Count;
                    order.Add(id);
                }

                bool selfLoop = component.Count == 1 && edges[v].Contains(v);
                if (component.Count > 1 || selfLoop)
                {
                    IReadOnlyList<string> members = component;
                    cycles.Add(members);
                    foreach (string id in component) cycleOf[id] = members;
                    problems.Add("dependency cycle between features: " + string.Join(", ", component));
                }
            };

            foreach (string id in ids)
            {
                if (!discovery.ContainsKey(id)) visit(id);
            }
        }
    }
}
