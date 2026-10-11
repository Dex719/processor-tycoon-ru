using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The section <c>research-tree-ux</c> of the side save file without the game (design: Component 5, NFR-4): the holder of the
    /// <see cref="NewTechTracker"/> that the core fills on a load and empties on a new game. It is the pure half of
    /// <c>ResearchUxSaveParticipant</c>, which only supplies the number of technologies and the log, so the tests can run it inside the
    /// real <see cref="SideFileModel"/>.
    /// <list type="bullet">
    /// <item><b>No version field.</b> <c>_schema</c> is written and read by the core: <see cref="Read"/> never sees it and
    /// <see cref="Write"/> never returns it (D20, NFR-4). <see cref="SchemaVersion"/> is the only thing said about it.</item>
    /// <item><b>No section, a section newer than this mod, a new game</b> (<see cref="ResetToDefaults"/>): the tracker is empty and
    /// <see cref="NeedsInitialization"/> is true, which is the signal of FR-3 items 5 and 7: when the game is ready everything available counts
    /// as seen and <see cref="MarkInitialized"/> is called. The core keeps a newer section and writes it back as it was.</item>
    /// <item><b>A section that cannot be used</b> (it has no list of ids, or the member is not an array) says nothing about what the player has
    /// seen: it is treated like a missing section, with a warning, instead of marking every available technology as new.</item>
    /// <item><b>Damaged values</b> are dropped one by one with a warning (entries that are not whole numbers, ids outside the technologies,
    /// repeats, more than <see cref="ResearchUxSave.MaxSeen"/>); the section is never lost, the next save writes what is left.</item>
    /// <item><b>Nothing to store</b> until the available technologies have been counted: <see cref="Write"/> returns null, the section is left
    /// out, and the next load treats the save as an old one (every available technology counts as seen, never as new).</item>
    /// </list>
    /// </summary>
    public class UxSaveSection : ISideFileParticipant
    {
        /// <summary>The key of the section in <c>features</c>: the id of the feature.</summary>
        public const string Id = "research-tree-ux";

        /// <summary>Version 1 of the section.</summary>
        public const int Schema = 1;

        /// <summary>The name of the list of seen ids in the section.</summary>
        public const string SeenKey = "seenAvailable";

        private readonly Func<int> _technologyCount;
        private readonly Action<string> _warn;

        /// <param name="technologyCount">The number of technologies of the game (their ids are 0...count-1), 0 or less when it cannot be told yet; null means unknown.</param>
        /// <param name="warn">Receives one line per kind of damage met while reading; null drops them.</param>
        public UxSaveSection(Func<int> technologyCount = null, Action<string> warn = null)
        {
            _technologyCount = technologyCount;
            _warn = warn;
            Tracker = new NewTechTracker();
            NeedsInitialization = true;
        }

        /// <summary>The ids seen so far. Never null; the object is replaced on a load and on a reset, so read the property each time.</summary>
        public NewTechTracker Tracker { get; private set; }

        /// <summary>True when the last load found a usable section (even one with an empty list); false after a reset: a new game, an old save, a newer section.</summary>
        public bool HasStoredSection { get; private set; }

        /// <summary>
        /// True from a reset until <see cref="MarkInitialized"/>: the available technologies have not been counted as seen yet (FR-3 items 5
        /// and 7), and the marks "new" must not be drawn. False after a load that found a usable section.
        /// </summary>
        public bool NeedsInitialization { get; private set; }

        /// <summary>The available technologies were counted as seen: the marks may be drawn now.</summary>
        public void MarkInitialized()
        {
            NeedsInitialization = false;
        }

        public string SectionId
        {
            get { return Id; }
        }

        public int SchemaVersion
        {
            get { return Schema; }
        }

        public void ResetToDefaults()
        {
            Tracker = new NewTechTracker();
            HasStoredSection = false;
            NeedsInitialization = true;
        }

        public void Read(JsonObject section, int schema)
        {
            if (section == null)
            {
                ResetToDefaults();
                return;
            }

            JsonNode node;
            JsonArray list = section.TryGetValue(SeenKey, out node) ? node as JsonArray : null;
            if (list == null)
            {
                if (section.ContainsKey(SeenKey)) Warn(SeenKey + ": expected an array of technology ids, the section is read as missing");
                ResetToDefaults();
                return;
            }

            var save = new ResearchUxSave();
            int notNumbers = 0;
            foreach (JsonNode item in list)
            {
                var value = item as JsonValue;
                int id;
                if (value == null || value.Kind != JsonKind.Number || !value.TryGetInt32(out id))
                {
                    notNumbers++;
                    continue;
                }
                save.seenAvailable.Add(id);
            }

            var problems = new List<string>();
            if (notNumbers > 0) problems.Add(SeenKey + ": " + notNumbers + " entr" + (notNumbers == 1 ? "y" : "ies") + " that are not whole numbers were dropped");
            int count = TechnologyCount();
            save.Sanitize(count, problems);
            foreach (string problem in problems) Warn(problem);

            Tracker = NewTechTracker.FromSave(save, count);
            HasStoredSection = true;
            NeedsInitialization = false;
        }

        public JsonObject Write()
        {
            if (NeedsInitialization) return null;   // nothing is known as seen yet: leave the section out, the next load starts as from an old save
            return ModJson.From(Tracker.ToSave()) as JsonObject;
        }

        private int TechnologyCount()
        {
            if (_technologyCount == null) return 0;

            try
            {
                return _technologyCount();
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private void Warn(string line)
        {
            if (_warn == null) return;

            try
            {
                _warn(line);
            }
            catch (Exception)
            {
                // A logger that throws must not cost the player the section.
            }
        }
    }
}
