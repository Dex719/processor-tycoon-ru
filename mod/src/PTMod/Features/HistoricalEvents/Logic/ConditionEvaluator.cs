using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Conditions;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// Evaluates the conditions of an entry (and the <c>onlyIf</c> of a fire) in the roles of that entry (design.md, Component 5; FR-3). The conditions
    /// are trees of <c>ModConditions</c> (the predicates of the core and of the engine, <c>all</c>, <c>any</c>, <c>not</c>, and the <c>hist.*</c> predicates of
    /// <see cref="HistPredicates"/>); the tree is run by whoever the caller gives (<c>ModConditions.Evaluate</c> in the game), and this class only
    /// puts the roles into the scope (<see cref="HistRoleScope"/>) around the run, so that <c>hist.ShareAtLeast</c> with <c>"role": "target"</c> knows who
    /// the target is. The scope goes away when the run ends, whatever happens. A condition that throws is false and is reported once.
    /// <para>
    /// (This type shares its name with the core's <c>PTMod.Core.Logic.Conditions.ConditionEvaluator</c>, which only guards one tree; in this namespace the
    /// name is this class, and the core's is written in full where it is needed.)
    /// </para>
    /// </summary>
    public sealed class ConditionEvaluator
    {
        private readonly Func<ICondition, string, bool> _run;
        private readonly Action<string> _onError;

        /// <param name="run">Runs a condition for the company with the given key: <c>(condition, subjectKey) => bool</c>.</param>
        /// <param name="onError">Receives the line about a condition that threw; may be null.</param>
        public ConditionEvaluator(Func<ICondition, string, bool> run, Action<string> onError = null)
        {
            if (run == null) throw new ArgumentNullException("run");
            _run = run;
            _onError = onError;
        }

        /// <summary>
        /// A runner for the tests and for code without the game: evaluates with a context made by the core (<c>ConditionContext</c> with the given view
        /// of the game, which may be null when only <c>hist.*</c> predicates and the structure are used).
        /// </summary>
        public static ConditionEvaluator For(IGameState state, DateTime today, Action<string> onError = null)
        {
            return new ConditionEvaluator(
                (condition, subject) => PTMod.Core.Logic.Conditions.ConditionEvaluator.Evaluate(condition, new ConditionContext(state, today, subject), onError),
                onError);
        }

        /// <summary>
        /// Evaluates one condition for <paramref name="subjectKey"/> (the player when null) with the given roles, world, shares and state in scope.
        /// A null condition is "no condition": true.
        /// </summary>
        public bool Evaluate(ICondition condition, RoleResult roles, IWorldView world, MarketShareCache shares, HistState state, string subjectKey = null)
        {
            if (condition == null) return true;

            string subject = string.IsNullOrEmpty(subjectKey) ? CompanyKey.Player : subjectKey;
            try
            {
                using (HistRoleScope.Push(new HistScopeFrame(roles, world, shares, state)))
                {
                    return _run(condition, subject);
                }
            }
            catch (Exception ex)
            {
                if (_onError != null) _onError("condition " + Describe(condition) + " threw " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>The conditions of an entry are a conjunction: all of them must hold. An empty list holds.</summary>
        public bool EvaluateAll(IReadOnlyList<ICondition> conditions, RoleResult roles, IWorldView world, MarketShareCache shares, HistState state, string subjectKey = null)
        {
            if (conditions == null) return true;
            for (int i = 0; i < conditions.Count; i++)
            {
                if (!Evaluate(conditions[i], roles, world, shares, state, subjectKey)) return false;
            }
            return true;
        }

        private static string Describe(ICondition condition)
        {
            try
            {
                return condition.Describe();
            }
            catch (Exception)
            {
                return condition.GetType().Name;
            }
        }
    }
}
