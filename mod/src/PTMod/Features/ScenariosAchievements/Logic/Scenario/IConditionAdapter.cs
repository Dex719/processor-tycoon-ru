namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// How the logic evaluates a <c>condition</c> check (an expression of <c>ModConditions</c>): the game-side layer implements it over the
    /// parsed <c>ICondition</c> of the core, the tests bring a fake. The logic does not parse or compile the expression.
    /// </summary>
    public interface IConditionAdapter
    {
        /// <summary>
        /// Evaluates the condition now. True with the result in <paramref name="value"/>; false when it cannot be evaluated (the owner of a
        /// predicate is not active, the expression did not compile) - the check is then simply not true.
        /// </summary>
        bool TryEvaluate(ConditionRef condition, out bool value);
    }

    /// <summary>The adapter of a run that has none: no condition can be evaluated, so every <c>condition</c> check is not true.</summary>
    public sealed class NullConditionAdapter : IConditionAdapter
    {
        public static readonly NullConditionAdapter Instance = new NullConditionAdapter();

        public bool TryEvaluate(ConditionRef condition, out bool value)
        {
            value = false;
            return false;
        }
    }
}
