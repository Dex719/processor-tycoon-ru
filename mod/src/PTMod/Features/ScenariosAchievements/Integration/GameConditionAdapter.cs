using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.ScenariosAchievements.Logic;

namespace PTMod.Features.ScenariosAchievements.Integration
{
    /// <summary>
    /// Evaluates the <c>condition</c> checks of the content (goals, fail rules, achievements) with the conditions of the core (TSK-019, FR-1, D6): the
    /// expression of the record is parsed once by <c>ModConditions.Parse</c> and the tree is kept in <see cref="ConditionRef.Compiled"/>, then evaluated for the
    /// player's company on every pass. A condition that cannot be parsed is reported once and is never true. The core guarantees that evaluating does not throw;
    /// an owner feature that is off makes its predicates false.
    /// </summary>
    internal sealed class GameConditionAdapter : IConditionAdapter
    {
        private readonly HashSet<ConditionRef> _reported = new HashSet<ConditionRef>();

        public bool TryEvaluate(ConditionRef condition, out bool value)
        {
            value = false;
            if (condition == null) return false;

            var compiled = condition.Compiled as ICondition;
            if (compiled == null)
            {
                var errors = new List<string>();
                compiled = ModConditions.Parse(condition.Node, errors);
                if (compiled == null || errors.Count > 0)
                {
                    if (_reported.Add(condition))
                    {
                        ModLog.Warn(ScenariosFeature.FeatureId, "a condition of the content cannot be evaluated: "
                                                                + (errors.Count > 0 ? errors[0] : "it is empty"));
                    }
                    return false;
                }
                condition.Compiled = compiled;
            }

            value = ModConditions.Evaluate(compiled);
            return true;
        }
    }
}
