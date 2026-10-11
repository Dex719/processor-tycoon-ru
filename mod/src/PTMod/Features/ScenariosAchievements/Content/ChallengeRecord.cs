using PTMod.Core;
using PTMod.Features.ScenariosAchievements.Logic;

namespace PTMod.Features.ScenariosAchievements.Content
{
    /// <summary>A record of <c>challenges.json</c> as <c>ModContent</c> sees it: the identity (the key of an override) around the definition.</summary>
    internal sealed class ChallengeRecord : IContentRecord
    {
        public ChallengeRecord(ChallengeDefinition definition)
        {
            Definition = definition;
        }

        public ChallengeDefinition Definition { get; }

        public string Id
        {
            get { return Definition.Id; }
        }
    }
}
