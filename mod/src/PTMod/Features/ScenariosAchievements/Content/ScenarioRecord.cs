using PTMod.Core;
using PTMod.Features.ScenariosAchievements.Logic;

namespace PTMod.Features.ScenariosAchievements.Content
{
    /// <summary>A record of <c>scenarios.json</c> as <c>ModContent</c> sees it: the identity (the key of an override) around the definition.</summary>
    internal sealed class ScenarioRecord : IContentRecord
    {
        public ScenarioRecord(ScenarioDefinition definition)
        {
            Definition = definition;
        }

        public ScenarioDefinition Definition { get; }

        public string Id
        {
            get { return Definition.Id; }
        }
    }
}
