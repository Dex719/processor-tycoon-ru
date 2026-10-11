using PTMod.Core;
using PTMod.Features.ScenariosAchievements.Logic;

namespace PTMod.Features.ScenariosAchievements.Content
{
    /// <summary>A record of <c>achievements.json</c> as <c>ModContent</c> sees it: the identity (the key of an override) around the definition.</summary>
    internal sealed class AchievementRecord : IContentRecord
    {
        public AchievementRecord(AchievementDefinition definition)
        {
            Definition = definition;
        }

        public AchievementDefinition Definition { get; }

        public string Id
        {
            get { return Definition.Id; }
        }
    }
}
