using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.FirstYearOnboarding.Logic;

namespace PTMod.Features.FirstYearOnboarding
{
    /// <summary>
    /// The game side of <see cref="IStepValidationContext"/> (TSK-002): conditions are parsed by <c>ModConditions.Parse</c> (so only the
    /// predicates of the core and the registered <c>onb.*</c> ones are known), strings are looked up in <c>ModL10n</c>. Never throws.
    /// <para>
    /// The raw Russian text (with its <c>{0}</c> placeholders) comes from <c>ModL10n.TryGetText</c>; when the tables cannot be read, the checks
    /// that need it (placeholder indexes, mentor prefix, length) are skipped.
    /// </para>
    /// </summary>
    internal sealed class GameStepValidationContext : IStepValidationContext
    {
        public bool ValidateCondition(JsonNode node, IList<string> errors, string path)
        {
            var local = new List<string>();
            ICondition condition = ModConditions.Parse(node, local, path);
            if (errors != null)
            {
                foreach (string message in local) errors.Add(message);
            }
            return condition != null && local.Count == 0;
        }

        public bool HasString(string key)
        {
            return !string.IsNullOrEmpty(key) && ModL10n.Has(key);
        }

        public string GetRussianText(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            string text;
            return ModL10n.TryGetText("ru", key, out text) ? text : null;
        }

        /// <summary>
        /// The raw Russian table of the core can be read. The self-check asks before it relies on <see cref="HasRussianText"/>: with false, a key
        /// looks missing whether or not it is there.
        /// </summary>
        internal bool CanReadRussian
        {
            get
            {
                try
                {
                    return ModL10n.Keys("ru").Count > 0;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>The key is in the Russian table (the main language of the tutorial). False for every key while <see cref="CanReadRussian"/> is false.</summary>
        internal bool HasRussianText(string key)
        {
            return GetRussianText(key) != null;
        }
    }
}
