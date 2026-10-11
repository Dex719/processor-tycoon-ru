using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using ExpenseOrigin = ProcessorTycoon.ExpenseOrigin;
using RevenueOrigin = ProcessorTycoon.RevenueOrigin;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// <see cref="IContentEnvironment"/> over the running mod: <c>ModConditions</c> parses the conditions, the table of effect handlers says which types exist
    /// and validates their parameters, <c>ModModifiers</c> knows the channels, the game's enums give the origins of money.
    /// </summary>
    internal sealed class CoreContentEnvironment : IContentEnvironment
    {
        private const string ValidateSite = "content/validate-effect";

        private readonly Func<string, IEventEffectHandler> _handlerOf;

        /// <param name="handlerOf">The handler of an effect type, or null (the engine's own table; <c>EventEffects.Get</c> when null is given).</param>
        public CoreContentEnvironment(Func<string, IEventEffectHandler> handlerOf)
        {
            _handlerOf = handlerOf ?? EventEffects.Get;
        }

        public ICondition ParseCondition(JsonNode node, List<string> errors, string path)
        {
            return ModConditions.Parse(node, errors, path);
        }

        public bool IsEffectTypeRegistered(string type)
        {
            return !string.IsNullOrEmpty(type) && _handlerOf(type) != null;
        }

        /// <summary>
        /// The handler's own check. It belongs to the feature that wrote the handler (<c>market-shocks</c> for <c>ModifierRamp</c>), so an exception is counted
        /// against that feature, and the effect is rejected: a handler that cannot judge its parameters must not be given them.
        /// </summary>
        public bool ValidateWithHandler(EventEffect effect, IList<string> errors)
        {
            IEventEffectHandler handler = _handlerOf(effect.type);
            if (handler == null)
            {
                errors.Add("no handler is registered for the effect type '" + effect.type + "'");
                return false;
            }

            string owner = ModFeatures.FeatureOfType(handler.GetType()) ?? EventSystemApi.FeatureId;
            return ModHooks.Guard(owner, ValidateSite, () => handler.Validate(effect, errors), false);
        }

        public bool IsChannelDeclared(string channel)
        {
            return !string.IsNullOrEmpty(channel) && ModModifiers.IsDeclared(channel);
        }

        public bool IsOriginName(bool spend, string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return Enum.IsDefined(spend ? typeof(ExpenseOrigin) : typeof(RevenueOrigin), name);
        }
    }
}
