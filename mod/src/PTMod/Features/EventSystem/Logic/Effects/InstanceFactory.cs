using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Companies;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// Turns a firing into instances (design.md "Дневной цикл", D8): one instance per target of the plan, the instances of an <c>AllCompanies</c> group share the
    /// id of the first as <c>groupId</c>; the event's own global-scope effects sit on the first instance only, the choices on every one. Every effect is resolved
    /// here, once, with the generator of the instance (<c>hash32(WorldSeed, "inst", id)</c>). Nothing is applied: that is the runtime's step.
    /// </summary>
    public sealed class InstanceFactory
    {
        private readonly EffectResolver _resolver;
        private readonly EventRandom _random;

        public InstanceFactory(EffectResolver resolver, EventRandom random)
        {
            if (resolver == null) throw new ArgumentNullException("resolver");
            if (random == null) throw new ArgumentNullException("random");
            _resolver = resolver;
            _random = random;
        }

        /// <summary>
        /// The instances of the plan, resolved. <paramref name="nextId"/> hands out the ids; <paramref name="chainDepth"/> is the depth of the chain the firing
        /// continues (0 - none). Choices: state <c>AwaitingChoice</c> and the provisional deadline <c>firedDay + timerDays</c> (D2; the mail's delivery moves it).
        /// </summary>
        public IList<EventInstanceDto> Build(FiringPlan plan, Func<int> nextId, int chainDepth)
        {
            if (plan == null) throw new ArgumentNullException("plan");
            if (nextId == null) throw new ArgumentNullException("nextId");

            EventDefinition definition = plan.Definition;
            var result = new List<EventInstanceDto>();
            int groupId = 0;
            foreach (PlannedInstance planned in plan.Expand())
            {
                int id = nextId();
                if (groupId == 0) groupId = id;

                bool choices = planned.HasChoices;
                var instance = new EventInstanceDto
                {
                    id = id,
                    groupId = groupId,
                    kind = EventInstanceKinds.Event,
                    eventId = definition.id,
                    companyKey = planned.CompanyKey,
                    firedDay = plan.Today,
                    deliveredDay = 0,
                    deadlineDay = choices ? plan.Today + Math.Max(1, definition.timerDays) : 0,
                    state = choices ? EventInstanceStates.AwaitingChoice : EventInstanceStates.Resolved,
                    parentId = Math.Max(0, plan.ParentId),
                    args = Copy(plan.Args),
                    chainDepth = Math.Max(0, chainDepth),
                };
                _resolver.ResolveInstance(instance, definition, planned, plan.Today, _random.ForInstance(id));
                result.Add(instance);
            }
            return result;
        }

        /// <summary>
        /// The effect of the definition an applied effect was resolved from, by its id (<c>&lt;instance&gt;/&lt;choice|common&gt;/&lt;index&gt;</c>); null when the
        /// definition no longer has it.
        /// </summary>
        public static EventEffect SpecOf(EventDefinition definition, AppliedEffect effect)
        {
            if (definition == null || effect == null || string.IsNullOrEmpty(effect.id)) return null;
            string[] parts = effect.id.Split('/');
            int index;
            if (parts.Length != 3 || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out index)) return null;

            EventEffect[] list = null;
            if (string.Equals(parts[1], ContentNames.CommonChoice, StringComparison.Ordinal))
            {
                list = definition.effects;
            }
            else if (definition.choices != null)
            {
                foreach (EventChoice choice in definition.choices)
                {
                    if (choice != null && string.Equals(choice.id, parts[1], StringComparison.Ordinal)) list = choice.effects;
                }
            }

            if (list == null || index < 0 || index >= list.Length) return null;
            EventEffect spec = list[index];
            return spec != null && string.Equals(spec.type, effect.type, StringComparison.Ordinal) ? spec : null;
        }

        /// <summary>The instance belongs to the player.</summary>
        public static bool IsPlayer(EventInstanceDto instance)
        {
            return instance != null && CompanyKey.IsPlayer(instance.companyKey);
        }

        private static string[] Copy(string[] args)
        {
            if (args == null) return new string[0];
            var copy = new string[args.Length];
            for (int i = 0; i < args.Length; i++) copy[i] = args[i] ?? string.Empty;
            return copy;
        }
    }
}
