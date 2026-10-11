using System.Collections.Generic;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The section <c>research-tree-ux</c> of the side save file (design: "ModSave: секция"), the DTO for <c>ModJson.Bind</c> and
    /// <c>ModJson.From</c>. It holds the ids of the technologies that were available and have been seen (FR-3); everything available and not in
    /// the list is "new". There is deliberately no version field: the core writes and reads <c>_schema</c> of the section (NFR-4).
    /// The member is lower case because it is the JSON name.
    /// </summary>
    public sealed class ResearchUxSave
    {
        /// <summary>The longest list that is kept; the game has about 100 technologies, a longer list is a damaged one.</summary>
        public const int MaxSeen = 512;

        /// <summary><c>Technology.ID</c> of every technology that is available and has been seen, ascending, without repeats.</summary>
        public List<int> seenAvailable = new List<int>();

        /// <summary>
        /// Brings <see cref="seenAvailable"/> to the form the spec requires: no negative id, no id outside <c>0...technologyCount-1</c> (when the
        /// count is known: pass 0 or less when it is not), no repeats, ascending, at most <see cref="MaxSeen"/>. One line per kind of defect
        /// is added to <paramref name="problems"/>. Returns the number of entries that were dropped.
        /// </summary>
        public int Sanitize(int technologyCount, List<string> problems)
        {
            if (seenAvailable == null) seenAvailable = new List<int>();

            int before = seenAvailable.Count;
            int outOfRange = 0;
            int repeated = 0;

            var kept = new SortedSet<int>();
            foreach (int id in seenAvailable)
            {
                if (id < 0 || (technologyCount > 0 && id >= technologyCount))
                {
                    outOfRange++;
                    continue;
                }
                if (!kept.Add(id)) repeated++;
            }

            int tooMany = 0;
            var result = new List<int>(kept);
            if (result.Count > MaxSeen)
            {
                tooMany = result.Count - MaxSeen;
                result.RemoveRange(MaxSeen, tooMany);
            }
            seenAvailable = result;

            if (problems != null)
            {
                if (outOfRange > 0) problems.Add("seenAvailable: " + outOfRange + " id(s) outside the technologies were dropped");
                if (repeated > 0) problems.Add("seenAvailable: " + repeated + " repeated id(s) were merged");
                if (tooMany > 0) problems.Add("seenAvailable: more than " + MaxSeen + " ids, " + tooMany + " were dropped");
            }
            return before - result.Count;
        }

        /// <summary>An independent copy.</summary>
        public ResearchUxSave Clone()
        {
            return new ResearchUxSave { seenAvailable = seenAvailable == null ? new List<int>() : new List<int>(seenAvailable) };
        }
    }
}
