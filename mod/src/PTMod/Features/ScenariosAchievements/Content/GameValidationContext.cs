using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.ScenariosAchievements.Logic;

namespace PTMod.Features.ScenariosAchievements.Content
{
    /// <summary>
    /// The registries of the game as the validator sees them (<see cref="IValidationContext"/>): the vocabulary of the feature, the modifier
    /// channels of the core (<c>ModModifiers.IsDeclared</c>), the condition parser of the core (<c>ModConditions.Parse</c>) and the string
    /// tables (<c>ModL10n.Has</c>). The challenges that were loaded are given by the catalog.
    /// </summary>
    internal sealed class GameValidationContext : IValidationContext
    {
        private readonly HashSet<string> _challenges;

        public GameValidationContext(IEnumerable<string> knownChallenges)
        {
            _challenges = new HashSet<string>(knownChallenges ?? new string[0], System.StringComparer.Ordinal);
        }

        public bool IsKnownMetric(string id)
        {
            return ContentVocabulary.IsKnownMetric(id);
        }

        public bool IsKnownEvent(string id)
        {
            return ContentVocabulary.IsKnownEvent(id);
        }

        public bool IsKnownChannel(string channel)
        {
            return !string.IsNullOrEmpty(channel) && ModModifiers.IsDeclared(channel);
        }

        public bool ValidateCondition(JsonNode node, IList<string> errors, string path)
        {
            var reasons = new List<string>();
            var parsed = ModConditions.Parse(node, reasons, path);
            if (errors != null)
            {
                foreach (string reason in reasons) errors.Add(reason);
            }
            return parsed != null && reasons.Count == 0;
        }

        public bool HasString(string key)
        {
            return ModL10n.Has(key);
        }

        public bool IsKnownChallenge(string id)
        {
            return id != null && _challenges.Contains(id);
        }
    }
}
