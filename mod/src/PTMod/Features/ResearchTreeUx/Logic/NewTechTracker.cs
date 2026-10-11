using System;
using System.Collections.Generic;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The "new" marks of FR-3 without the game: which available technologies the player has not looked at yet. A technology is <i>new</i>
    /// while it is available (not researched, not locked) and its id is not in the set of the seen ones. The set is what the side save keeps
    /// (<see cref="ResearchUxSave.seenAvailable"/>); the adapters (<c>NewBadges</c>, <c>TabStrip</c>, the save participant) only move it in and
    /// out and draw the answers.
    /// <list type="bullet">
    /// <item>A technology is seen when the player leaves the tab it was shown on, closes the window or holds the cursor on it
    /// (<see cref="MarkSeen(int)"/>); the ids of the technologies that are no longer available stay in the set, which is harmless (at most one
    /// id per technology of the game).</item>
    /// <item>A new game, and a save without the section, start with everything that is available counted as seen
    /// (<see cref="InitializeAllAvailableAsSeen"/>, FR-3 items 5 and 7), so the first screen of the tree has no marks.</item>
    /// <item>Reading from the save drops what the spec says (ids outside the technologies, repeats, more than 512) and never throws.</item>
    /// </list>
    /// </summary>
    public sealed class NewTechTracker
    {
        private readonly SortedSet<int> _seen = new SortedSet<int>();

        /// <summary>How many ids are in the set of the seen ones.</summary>
        public int SeenCount
        {
            get { return _seen.Count; }
        }

        /// <summary>The seen ids in ascending order (the live set, do not change it from outside).</summary>
        public IReadOnlyCollection<int> Seen
        {
            get { return _seen; }
        }

        /// <summary>True when the id is in the set of the seen ones.</summary>
        public bool IsSeen(int technologyId)
        {
            return _seen.Contains(technologyId);
        }

        /// <summary>The technology is available and not seen: it carries the mark "new" and counts in its tab. A null technology is not new.</summary>
        public bool IsNew(TechSnapshot tech)
        {
            return tech != null && tech.IsAvailable && !_seen.Contains(tech.Id);
        }

        /// <summary>Adds an id to the seen ones; false when it was there already or is not an id (below zero).</summary>
        public bool MarkSeen(int technologyId)
        {
            if (technologyId < 0) return false;
            return _seen.Add(technologyId);
        }

        /// <summary>Adds the ids to the seen ones and returns how many were new to the set. A null sequence adds nothing.</summary>
        public int MarkSeen(IEnumerable<int> technologyIds)
        {
            if (technologyIds == null) return 0;

            int added = 0;
            foreach (int id in technologyIds)
            {
                if (MarkSeen(id)) added++;
            }
            return added;
        }

        /// <summary>The ids of the new technologies, in the order of <paramref name="techs"/>. A null list or a null entry gives nothing for it.</summary>
        public IReadOnlyList<int> Unseen(IReadOnlyList<TechSnapshot> techs)
        {
            var ids = new List<int>();
            if (techs == null) return ids;

            for (int i = 0; i < techs.Count; i++)
            {
                if (IsNew(techs[i])) ids.Add(techs[i].Id);
            }
            return ids;
        }

        /// <summary>
        /// Like <see cref="Unseen"/>, but fills <paramref name="into"/> (cleared first) with the snapshots and allocates nothing: the call of the
        /// refresh after each game day. Returns the number found.
        /// </summary>
        public int CollectUnseen(IReadOnlyList<TechSnapshot> techs, List<TechSnapshot> into)
        {
            if (into == null) throw new ArgumentNullException("into");

            into.Clear();
            if (techs == null) return 0;

            for (int i = 0; i < techs.Count; i++)
            {
                TechSnapshot tech = techs[i];
                if (IsNew(tech)) into.Add(tech);
            }
            return into.Count;
        }

        /// <summary>Counts every available technology as seen (a new game, a save without the section). Returns how many ids were added.</summary>
        public int InitializeAllAvailableAsSeen(IReadOnlyList<TechSnapshot> techs)
        {
            if (techs == null) return 0;

            int added = 0;
            for (int i = 0; i < techs.Count; i++)
            {
                TechSnapshot tech = techs[i];
                if (tech != null && tech.IsAvailable && MarkSeen(tech.Id)) added++;
            }
            return added;
        }

        /// <summary>Forgets everything seen (the set of a game that is over).</summary>
        public void Clear()
        {
            _seen.Clear();
        }

        /// <summary>The set as the section of the side save: ascending, without repeats, at most <see cref="ResearchUxSave.MaxSeen"/>. A new object each call.</summary>
        public ResearchUxSave ToSave()
        {
            var save = new ResearchUxSave { seenAvailable = new List<int>(_seen) };
            save.Sanitize(0, null);
            return save;
        }

        /// <summary>
        /// The tracker of a loaded section. A null section gives an empty set. Ids below zero or at <paramref name="technologyCount"/> and over
        /// (when the count is known: pass 0 or less when it is not), repeats and everything past 512 are dropped; the section object itself is
        /// not changed.
        /// </summary>
        public static NewTechTracker FromSave(ResearchUxSave save, int technologyCount)
        {
            var tracker = new NewTechTracker();
            if (save == null) return tracker;

            ResearchUxSave copy = save.Clone();
            copy.Sanitize(technologyCount, null);
            tracker.MarkSeen(copy.seenAvailable);
            return tracker;
        }
    }
}
