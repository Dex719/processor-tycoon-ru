using System;
using System.Collections.Generic;
using System.Text;

namespace PTMod.Core.Logic.Conditions
{
    /// <summary>
    /// Conjunction: true when every child is true. An empty list is true (the neutral element), so
    /// <c>{"all": []}</c> is the "always" condition. Children are evaluated in order and evaluation stops at the first false.
    /// </summary>
    public sealed class AllCondition : ICondition
    {
        private readonly ICondition[] children;

        public AllCondition(IEnumerable<ICondition> children)
        {
            this.children = ConditionNodeHelper.CopyChildren(children, "AllCondition");
        }

        public IReadOnlyList<ICondition> Children
        {
            get { return children; }
        }

        public bool Evaluate(ConditionContext ctx)
        {
            for (int i = 0; i < children.Length; i++)
            {
                if (!children[i].Evaluate(ctx)) return false;
            }
            return true;
        }

        public string Describe()
        {
            return ConditionNodeHelper.DescribeGroup("all", children);
        }
    }

    /// <summary>
    /// Disjunction: true when at least one child is true. An empty list is false (nothing can be true), so
    /// <c>{"any": []}</c> is the "never" condition. Evaluation stops at the first true child.
    /// </summary>
    public sealed class AnyCondition : ICondition
    {
        private readonly ICondition[] children;

        public AnyCondition(IEnumerable<ICondition> children)
        {
            this.children = ConditionNodeHelper.CopyChildren(children, "AnyCondition");
        }

        public IReadOnlyList<ICondition> Children
        {
            get { return children; }
        }

        public bool Evaluate(ConditionContext ctx)
        {
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].Evaluate(ctx)) return true;
            }
            return false;
        }

        public string Describe()
        {
            return ConditionNodeHelper.DescribeGroup("any", children);
        }
    }

    /// <summary>
    /// Negation. Conditions are two-valued: a predicate whose data source is unavailable is false, so
    /// <c>not</c> of it is true. Use a positive predicate when "unknown" must not fire.
    /// </summary>
    public sealed class NotCondition : ICondition
    {
        public NotCondition(ICondition inner)
        {
            if (inner == null) throw new ArgumentNullException(nameof(inner));
            Inner = inner;
        }

        public ICondition Inner { get; }

        public bool Evaluate(ConditionContext ctx)
        {
            return !Inner.Evaluate(ctx);
        }

        public string Describe()
        {
            return "not(" + Inner.Describe() + ")";
        }
    }

    /// <summary>
    /// A condition backed by a function: the building block of every core predicate, and the easy way for a feature
    /// to return something from its <c>ModConditions.Register</c> factory. The function must not throw for ordinary
    /// "no data" situations (return false); an exception is caught by <see cref="ConditionEvaluator"/>.
    /// </summary>
    public sealed class DelegateCondition : ICondition
    {
        private readonly Func<ConditionContext, bool> evaluate;
        private readonly string description;

        /// <param name="description">Text for <see cref="Describe"/>, usually <c>name(param=value, ...)</c>.</param>
        /// <param name="evaluate">The test; never called with a null context.</param>
        public DelegateCondition(string description, Func<ConditionContext, bool> evaluate)
        {
            if (evaluate == null) throw new ArgumentNullException(nameof(evaluate));
            this.description = string.IsNullOrEmpty(description) ? "predicate" : description;
            this.evaluate = evaluate;
        }

        public bool Evaluate(ConditionContext ctx)
        {
            return ctx != null && evaluate(ctx);
        }

        public string Describe()
        {
            return description;
        }
    }

    /// <summary>
    /// The only entry point that guarantees the "no exception" contract of FR-11 for a whole tree: a condition
    /// that throws (a feature predicate, a failing <see cref="IGameState"/>) is false, and the problem goes to the callback.
    /// A thrown condition makes the entire tree false, including inside <c>any</c> and <c>not</c>, because a half-evaluated
    /// tree would give an answer nobody intended.
    /// </summary>
    public static class ConditionEvaluator
    {
        /// <param name="condition">The tree; null (for example after a parse error) is false.</param>
        /// <param name="ctx">The context; null is false.</param>
        /// <param name="onError">Receives one English line per caught exception; may be null.</param>
        public static bool Evaluate(ICondition condition, ConditionContext ctx, Action<string> onError = null)
        {
            if (condition == null || ctx == null) return false;
            try
            {
                return condition.Evaluate(ctx);
            }
            catch (Exception ex)
            {
                if (onError != null)
                {
                    string where;
                    try
                    {
                        where = condition.Describe();
                    }
                    catch (Exception)
                    {
                        where = condition.GetType().Name;
                    }
                    onError("condition " + where + " threw " + ex.GetType().Name + ": " + ex.Message);
                }
                return false;
            }
        }
    }

    internal static class ConditionNodeHelper
    {
        public static ICondition[] CopyChildren(IEnumerable<ICondition> children, string owner)
        {
            if (children == null) throw new ArgumentNullException(nameof(children));
            var list = new List<ICondition>(children);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null) throw new ArgumentException(owner + " child " + i + " is null", nameof(children));
            }
            return list.ToArray();
        }

        public static string DescribeGroup(string name, ICondition[] children)
        {
            var sb = new StringBuilder(name).Append('(');
            for (int i = 0; i < children.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(children[i].Describe());
            }
            return sb.Append(')').ToString();
        }
    }
}
