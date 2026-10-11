using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>A company that the random mode gives a character: its <c>UniqueID</c> and whether the game calls it a foundry (<c>AICompany.IsFoundry</c>).</summary>
    public struct AssignmentCandidate
    {
        public AssignmentCandidate(int uniqueId, bool isFoundry)
        {
            UniqueId = uniqueId;
            IsFoundry = isFoundry;
        }

        public int UniqueId;
        public bool IsFoundry;
    }

    /// <summary>
    /// The random characters (design.md, Component 3; FR-11 item 1, TSK-021): when the player switches "Random characters" on, the archetypes are dealt
    /// among the companies at the end of the new game's fast-forward instead of being read from <c>assignments.json</c>.
    /// <list type="bullet">
    /// <item>A foundry always gets <see cref="FoundryArchetypeId"/> (the foundry partner does not design anything; no other company gets it).</item>
    /// <item>Every other archetype goes to at most <see cref="MaxPerArchetype"/> companies. Each company in turn takes a random one of the archetypes
    /// that still have room; when none has (more companies than 3 × the archetypes, a modded game) the least used ones are open again, so every company
    /// gets a character.</item>
    /// <item>The companies are dealt in the order of their <c>UniqueID</c> and each one costs exactly one draw of the stream, so the same world seed deals the
    /// same characters (<see cref="StreamId"/>, saved with the game by <c>ModRandom</c>).</item>
    /// </list>
    /// The result is only a plan; <c>PersonalityBook.Assign</c> saves it. Later a switch of the mode deals nothing again (FR-11 item 4).
    /// </summary>
    public static class RandomAssignment
    {
        /// <summary>Most companies that may share one archetype (FR-11 item 1).</summary>
        public const int MaxPerArchetype = 3;

        /// <summary>The id of the archetype of the foundries (<c>archetypes.json</c>, <c>assignments.json</c>).</summary>
        public const string FoundryArchetypeId = "foundry_partner";

        /// <summary>The stream of <c>ModRandom</c> the deal draws from.</summary>
        public const string StreamId = "rival-personalities/assign";

        /// <summary>
        /// Deals the archetypes. Returns one entry per company that got a character, in the order of the <c>UniqueID</c>; a company the plan cannot serve
        /// (a foundry when the catalog has no foundry archetype, any company when it has no other archetype) is left out and keeps the default of
        /// <c>assignments.json</c>. A duplicated or negative <c>UniqueID</c> is ignored.
        /// </summary>
        /// <param name="companies">The companies that have no character yet.</param>
        /// <param name="archetypeIds">The ids of every archetype of the catalog, in the order of the catalog.</param>
        /// <param name="rng">The stream <see cref="StreamId"/>; null deals the first archetype that has room (no randomness).</param>
        /// <param name="maxPerArchetype">The limit per archetype; below 1 counts as <see cref="MaxPerArchetype"/>.</param>
        public static List<AssignmentEntry> Plan(IList<AssignmentCandidate> companies, IList<string> archetypeIds, IModRng rng, int maxPerArchetype = MaxPerArchetype)
        {
            var result = new List<AssignmentEntry>();
            if (companies == null || companies.Count == 0 || archetypeIds == null) return result;
            int cap = maxPerArchetype < 1 ? MaxPerArchetype : maxPerArchetype;

            bool hasFoundryArchetype = false;
            var pool = new List<string>();
            foreach (string id in archetypeIds)
            {
                if (string.IsNullOrEmpty(id) || pool.Contains(id)) continue;
                if (string.Equals(id, FoundryArchetypeId, StringComparison.Ordinal)) hasFoundryArchetype = true;
                else pool.Add(id);
            }

            var ordered = new List<AssignmentCandidate>();
            var seen = new HashSet<int>();
            foreach (AssignmentCandidate company in companies)
            {
                if (company.UniqueId >= 0 && seen.Add(company.UniqueId)) ordered.Add(company);
            }
            ordered.Sort((a, b) => a.UniqueId.CompareTo(b.UniqueId));

            var used = new int[pool.Count];
            var eligible = new List<int>(pool.Count);
            foreach (AssignmentCandidate company in ordered)
            {
                if (company.IsFoundry)
                {
                    if (hasFoundryArchetype) result.Add(new AssignmentEntry { uid = company.UniqueId, archetype = FoundryArchetypeId });
                    continue;
                }
                if (pool.Count == 0) continue;

                int pick = Pick(used, cap, eligible, rng);
                used[pick]++;
                result.Add(new AssignmentEntry { uid = company.UniqueId, archetype = pool[pick] });
            }
            return result;
        }

        /// <summary>The index of the archetype the next company takes: one of those with room, or of the least used when none has room.</summary>
        private static int Pick(int[] used, int cap, List<int> eligible, IModRng rng)
        {
            eligible.Clear();
            for (int i = 0; i < used.Length; i++)
            {
                if (used[i] < cap) eligible.Add(i);
            }

            if (eligible.Count == 0)
            {
                int least = int.MaxValue;
                for (int i = 0; i < used.Length; i++)
                {
                    if (used[i] < least) least = used[i];
                }
                for (int i = 0; i < used.Length; i++)
                {
                    if (used[i] == least) eligible.Add(i);
                }
            }

            int index = rng != null ? rng.Range(0, eligible.Count) : 0;
            if (index < 0 || index >= eligible.Count) index = 0;
            return eligible[index];
        }
    }
}
