using PTMod.Core.Logic.Json;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// An achievement of the profile (design.md, "Контент: достижения"; FR-12). Built from a record of <c>achievements.json</c> by
    /// <see cref="ContentMapper"/>; the 37 standard ones are the table of design.md.
    /// </summary>
    public sealed class AchievementDefinition
    {
        /// <summary><c>[a-z0-9-]{3,40}</c>, unique; the key of an override in <c>achievements.json</c>.</summary>
        public string Id;

        /// <summary>One of <see cref="ContentVocabulary.Categories"/>.</summary>
        public string Category;

        /// <summary>Shown as "???" in the window until it is earned.</summary>
        public bool Hidden;

        /// <summary>Points of the achievement, 0..10 000.</summary>
        public int Points;

        public string TitleKey, DescKey;

        public CheckSpec Check;

        /// <summary>Ignore the runs that were born from a save made without the mod (<c>legacy</c>): their history before the load is unknown.</summary>
        public bool CountsOnlyNewGames;

        /// <summary>Computed on load (first 8 hex digits of the SHA-1 of the canonical JSON of the record), never written in the file.</summary>
        public string ContentHash;

        /// <summary>The record differs from the built-in one with the same id, or there is no built-in one. Computed on load.</summary>
        public bool IsCustom;

        /// <summary>A copy of the JSON record this definition was mapped from; null for a definition built by hand.</summary>
        public JsonObject Source;
    }
}
