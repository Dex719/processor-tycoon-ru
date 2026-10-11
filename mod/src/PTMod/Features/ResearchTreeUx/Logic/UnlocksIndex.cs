using System;
using System.Collections.Generic;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The reverse dependencies (design: Component 2/4): the game stores for every technology what it requires, the tooltip also shows what it
    /// opens. Built once from the snapshots (<see cref="Build"/>) and applied to them with <see cref="ApplyTo"/>. A technology that requires
    /// itself is ignored, a repeated dependency counts once, a dependency that is not among the technologies still gets its entry (the
    /// technologies it opens are known even if the snapshot of it is missing). The ids of every list are in ascending order.
    /// </summary>
    public sealed class UnlocksIndex
    {
        private static readonly int[] NoIds = new int[0];

        private readonly Dictionary<int, int[]> _unlocks;

        private UnlocksIndex(Dictionary<int, int[]> unlocks)
        {
            _unlocks = unlocks;
        }

        /// <summary>How many technologies open at least one other technology.</summary>
        public int Count
        {
            get { return _unlocks.Count; }
        }

        /// <summary>The ids of the technologies that require <paramref name="technologyId"/>, ascending; empty when none. Never null.</summary>
        public IReadOnlyList<int> UnlocksOf(int technologyId)
        {
            int[] ids;
            return _unlocks.TryGetValue(technologyId, out ids) ? ids : NoIds;
        }

        /// <summary>Inverts the <see cref="TechSnapshot.DependencyIds"/> of <paramref name="techs"/>. A null sequence or a null entry is ignored.</summary>
        public static UnlocksIndex Build(IEnumerable<TechSnapshot> techs)
        {
            var sets = new Dictionary<int, SortedSet<int>>();
            if (techs != null)
            {
                foreach (TechSnapshot tech in techs)
                {
                    if (tech == null || tech.DependencyIds == null) continue;
                    foreach (int dependencyId in tech.DependencyIds)
                    {
                        if (dependencyId == tech.Id) continue;

                        SortedSet<int> set;
                        if (!sets.TryGetValue(dependencyId, out set))
                        {
                            set = new SortedSet<int>();
                            sets[dependencyId] = set;
                        }
                        set.Add(tech.Id);
                    }
                }
            }

            var result = new Dictionary<int, int[]>(sets.Count);
            foreach (KeyValuePair<int, SortedSet<int>> pair in sets)
            {
                var ids = new int[pair.Value.Count];
                pair.Value.CopyTo(ids);
                result[pair.Key] = ids;
            }
            return new UnlocksIndex(result);
        }

        /// <summary>Sets <see cref="TechSnapshot.UnlocksIds"/> of every snapshot (an empty array for a technology that opens nothing).</summary>
        public void ApplyTo(IEnumerable<TechSnapshot> techs)
        {
            if (techs == null) return;
            foreach (TechSnapshot tech in techs)
            {
                if (tech == null) continue;
                int[] ids;
                tech.UnlocksIds = _unlocks.TryGetValue(tech.Id, out ids) ? (int[])ids.Clone() : new int[0];
            }
        }
    }
}
