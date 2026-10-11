using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Conditions;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// What the choices need to know about the world (the game side implements it over <c>CompanyDirectory</c>, <c>GameFacade</c> and <c>ModCredit</c>; the tests give
    /// a table). Every member answers without throwing: a company that is gone reads as not alive, with no money.
    /// </summary>
    public interface IChoiceWorld
    {
        /// <summary><c>ModTicks.IsBatchRun</c>: <c>DateController.NextMonth</c> is running (FR-13 item 2).</summary>
        bool InBatch { get; }

        /// <summary>The company exists and is not bankrupt.</summary>
        bool IsAlive(string companyKey);

        /// <summary><c>ICompany.MoneyAmount</c>; 0 for a company that is gone.</summary>
        float Money(string companyKey);

        /// <summary><c>ModCredit.ThresholdOf</c>: the balance below which the company defaults, a negative number (0 - no credit at all).</summary>
        float DefaultThreshold(string companyKey);

        /// <summary>A condition of the content evaluated with the company as the subject (<c>ModConditions.Evaluate</c>); null is true.</summary>
        bool Check(ICondition condition, string companyKey);
    }

    /// <summary>Why a choice is grey.</summary>
    public enum ChoiceBlock
    {
        /// <summary>The choice can be taken.</summary>
        None = 0,

        /// <summary>The <c>requires</c> of the choice is false for the company.</summary>
        Requirement = 1,

        /// <summary>Paying for the choice would take the balance below the default threshold (FR-4 item 5).</summary>
        CreditLimit = 2,
    }

    /// <summary>One choice of an instance as the player sees it now: can it be taken, why not, what does it cost and how deep into the red does it go.</summary>
    public sealed class ChoiceState
    {
        public string ChoiceId;

        public bool Enabled = true;

        public ChoiceBlock Block;

        /// <summary>The key of the reason (<c>es.reason.requires</c>, <c>es.reason.credit_limit</c>) while the choice is grey, otherwise null.</summary>
        public string ReasonKey;

        /// <summary>The arguments of <see cref="ReasonKey"/>, ready for the player.</summary>
        public string[] ReasonArgs = new string[0];

        /// <summary>The up-front spending of the choice in dollars (0 for a free choice).</summary>
        public float Cost;

        /// <summary>Above 0: the balance after paying is this far below zero, still above the default threshold (the choice is open, with a warning, AC-4.2).</summary>
        public float InTheRed;

        /// <summary>Above 0: the balance after paying would be this far below the default threshold (the choice is grey, AC-4.6).</summary>
        public float ShortBy;
    }

    /// <summary>
    /// The words of a condition that is not met, for "Нужно: {0}" under a grey button (FR-4 item 4, AC-4.4): the text of the first unmet part of an <c>all</c>, the
    /// alternatives of an <c>any</c>, otherwise the table text <c>es.req.&lt;predicate&gt;</c> or, without one, the condition as the core describes it.
    /// </summary>
    public sealed class RequirementText
    {
        /// <summary>The prefix of the keys that name a predicate in words (<c>es.req.researchActive</c>).</summary>
        public const string KeyPrefix = "es.req.";

        private readonly Func<string, string> _lookup;

        /// <param name="lookup">The text of a key in the current language, or null when there is none.</param>
        public RequirementText(Func<string, string> lookup)
        {
            _lookup = lookup ?? (key => null);
        }

        /// <summary>The words for a condition; <paramref name="evaluate"/> tells which parts are met (it is how the first unmet part of an <c>all</c> is found).</summary>
        public string Describe(ICondition condition, Func<ICondition, bool> evaluate)
        {
            if (condition == null) return string.Empty;

            var all = condition as AllCondition;
            if (all != null && evaluate != null)
            {
                foreach (ICondition child in all.Children)
                {
                    if (!evaluate(child)) return Describe(child, evaluate);
                }
            }

            var any = condition as AnyCondition;
            if (any != null && any.Children.Count > 0)
            {
                var parts = new List<string>();
                foreach (ICondition child in any.Children) parts.Add(Describe(child, evaluate));
                return string.Join(" / ", parts.ToArray());
            }

            return Leaf(condition.Describe());
        }

        private string Leaf(string description)
        {
            if (string.IsNullOrEmpty(description)) return string.Empty;

            int bracket = description.IndexOf('(');
            string name = bracket > 0 ? description.Substring(0, bracket) : description;
            string text = null;
            try
            {
                text = _lookup(KeyPrefix + name);
            }
            catch (Exception)
            {
                // a failing lookup falls back to the plain description
            }
            return string.IsNullOrEmpty(text) ? description : text;
        }
    }

    /// <summary>
    /// Which choices of a waiting instance the company can take now (design.md "Доступность варианта", FR-4 items 4-5):
    /// <list type="bullet">
    /// <item>a false <c>requires</c> greys the choice with the reason "Нужно: ..." (AC-4.4);</item>
    /// <item>spending above the balance does not block it: while the balance after paying stays above the default threshold the choice is open and carries the
    /// warning "уйдёте в минус на ..." (<see cref="ChoiceState.InTheRed"/>, AC-4.2);</item>
    /// <item>paying that would take the balance below the default threshold greys the choice, "Не хватит кредитного лимита на ..." (AC-4.6);</item>
    /// <item>the <c>defaultChoice</c> is never greyed by money, only warned about.</item>
    /// </list>
    /// </summary>
    public sealed class ChoiceAvailability
    {
        public const string RequiresKey = "es.reason.requires";
        public const string CreditLimitKey = "es.reason.credit_limit";

        private readonly IChoiceWorld _world;
        private readonly RequirementText _requirements;

        public ChoiceAvailability(IChoiceWorld world, RequirementText requirements)
        {
            if (world == null) throw new ArgumentNullException("world");
            _world = world;
            _requirements = requirements ?? new RequirementText(null);
        }

        /// <summary>The state of every choice of the instance, in the order of its options.</summary>
        public IList<ChoiceState> Evaluate(EventInstanceDto instance, EventDefinition definition)
        {
            var states = new List<ChoiceState>();
            if (instance == null || instance.options == null) return states;

            foreach (ChoiceOptionDto option in instance.options)
            {
                if (option == null || string.IsNullOrEmpty(option.choiceId)) continue;
                states.Add(StateOf(instance, definition, option));
            }
            return states;
        }

        /// <summary>The state of one choice; null when the instance has no such choice.</summary>
        public ChoiceState StateOf(EventInstanceDto instance, EventDefinition definition, string choiceId)
        {
            if (instance == null || instance.options == null) return null;
            foreach (ChoiceOptionDto option in instance.options)
            {
                if (option != null && string.Equals(option.choiceId, choiceId, StringComparison.Ordinal)) return StateOf(instance, definition, option);
            }
            return null;
        }

        private ChoiceState StateOf(EventInstanceDto instance, EventDefinition definition, ChoiceOptionDto option)
        {
            string key = instance.companyKey;
            EventChoice choice = ChoiceOf(definition, option.choiceId);
            var state = new ChoiceState { ChoiceId = option.choiceId, Cost = EffectMath.ChoiceCost(option) };

            if (choice != null && choice.requires != null && !_world.Check(choice.requires, key))
            {
                state.Enabled = false;
                state.Block = ChoiceBlock.Requirement;
                state.ReasonKey = RequiresKey;
                state.ReasonArgs = new[] { _requirements.Describe(choice.requires, condition => _world.Check(condition, key)) };
                return state;
            }

            if (state.Cost <= 0f) return state;

            bool isDefault = definition != null && string.Equals(definition.defaultChoice, option.choiceId, StringComparison.Ordinal);
            double after = (double)_world.Money(key) - state.Cost;
            double threshold = _world.DefaultThreshold(key);
            if (!isDefault && after < threshold)
            {
                state.Enabled = false;
                state.Block = ChoiceBlock.CreditLimit;
                state.ShortBy = (float)(threshold - after);
                state.ReasonKey = CreditLimitKey;
                state.ReasonArgs = new[] { EffectFormat.Money(state.ShortBy) };
            }
            else if (after < 0.0)
            {
                state.InTheRed = (float)-after;
            }
            return state;
        }

        private static EventChoice ChoiceOf(EventDefinition definition, string choiceId)
        {
            if (definition == null || definition.choices == null) return null;
            foreach (EventChoice choice in definition.choices)
            {
                if (choice != null && string.Equals(choice.id, choiceId, StringComparison.Ordinal)) return choice;
            }
            return null;
        }
    }
}
