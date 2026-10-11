using System;
using PTMod.Core;
using ResearchDataProvider = ProcessorTycoon.ResearchSystem.ResearchDataProvider;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The holder of the section <c>research-tree-ux</c> of the side save file (design: Component 5, "ModSave: секция", NFR-4) that the feature
    /// registers with <c>ctx.SaveParticipant</c>: the ids of the available technologies the player has seen. The core calls it on a load, after
    /// each save and on a new game. All the rules are in the pure <see cref="UxSaveSection"/> (the tests run it inside the core's side file
    /// model); this class adds the two things only the game can say: how many technologies there are, and where a warning goes.
    /// <list type="bullet">
    /// <item>The version of the section (<c>_schema</c>) is written and read by the core. The participant never touches it: there is no version
    /// field in <see cref="ResearchUxSave"/> (D20).</item>
    /// <item>A new game, a save without the section, a section newer than this version, or a section that holds no list of ids: the tracker is
    /// empty and <see cref="UxSaveSection.NeedsInitialization"/> is true, which is the signal of FR-3 items 5 and 7: on <c>ModTicks.GameReady</c>
    /// everything available counts as seen.</item>
    /// <item>Damaged data is dropped value by value with a warning; the section is never lost, the next save writes what is left.</item>
    /// </list>
    /// </summary>
    internal sealed class ResearchUxSaveParticipant : UxSaveSection, IModSaveParticipant
    {
        public ResearchUxSaveParticipant()
            : base(TechnologyCount, WarnInLog)
        {
        }

        private static void WarnInLog(string line)
        {
            ModLog.Warn(ResearchTreeUxFeature.FeatureId, "save: section " + Id + ": " + line);
        }

        /// <summary>The number of technologies of the game (their ids are 0...count-1), or 0 when it cannot be told yet.</summary>
        private static int TechnologyCount()
        {
            try
            {
                ResearchDataProvider provider = ResearchDataProvider.Instance;
                if (provider == null) return 0;
                var all = provider.GetAllTechnologies();
                return all == null ? 0 : all.Count;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
