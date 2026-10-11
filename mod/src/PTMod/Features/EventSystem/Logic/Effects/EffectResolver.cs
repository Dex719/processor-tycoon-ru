using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// Computes the numbers of effects once (D1, FR-5 items 1-5, 9): when an instance is created every effect of every choice and of the event itself is
    /// resolved here and nowhere else. For one list (a choice, or the event's own effects) the chances and the exclusive groups are rolled first, one number of the
    /// instance's generator per effect (<see cref="ChanceRoller"/>); then each handler's <c>Resolve</c> runs with the pack's intensity for a <c>scalable</c>
    /// effect (D15; the handler holds a multiplier to the limits of its channel and marks it <c>clamped</c>). The engine fills the id
    /// <c>&lt;instance&gt;/&lt;choice|common&gt;/&lt;index in the definition&gt;</c>, the company (<c>global</c> for <c>scope: "Global"</c>), the chance, the
    /// state <c>Pending</c> and the text from <c>Describe</c>, which sees only the resolved effect (NFR-5). A handler that is missing or throws, or a number
    /// that is not finite, gives a <c>Failed</c> effect; the other effects and the day go on (FR-5 item 8).
    /// </summary>
    public sealed class EffectResolver
    {
        /// <summary>The line of an effect that could not be resolved or described.</summary>
        public const string FailedTextKey = "es.fx.failed";

        private readonly IEffectTable _table;
        private readonly IEffectModifiers _contributions;
        private readonly Func<string, PackOptions> _packOptions;
        private readonly Action<string, Exception> _onFault;

        /// <param name="table">The handlers by type.</param>
        /// <param name="contributions">The aggregator (limits of the channels) handed to the handlers.</param>
        /// <param name="packOptions">The options of a pack by id (its <c>Severity</c>, read at every use); null or a null answer - intensity 1.</param>
        /// <param name="onFault">A handler failed: the site and the exception.</param>
        public EffectResolver(IEffectTable table, IEffectModifiers contributions, Func<string, PackOptions> packOptions, Action<string, Exception> onFault)
        {
            if (table == null) throw new ArgumentNullException("table");
            _table = table;
            _contributions = contributions;
            _packOptions = packOptions ?? (pack => null);
            _onFault = onFault ?? ((site, error) => { });
        }

        /// <summary>
        /// Resolves the effects of a new instance: <c>options</c> - one entry per choice of the definition in its order, <c>common</c> - the event's own effects
        /// this instance carries (<paramref name="planned"/>; null - all of them). The lists are rolled in that order with <paramref name="rng"/>.
        /// </summary>
        public void ResolveInstance(EventInstanceDto instance, EventDefinition definition, PlannedInstance planned, int today, IModRng rng)
        {
            if (instance == null) throw new ArgumentNullException("instance");
            if (definition == null) throw new ArgumentNullException("definition");
            instance.options = new List<ChoiceOptionDto>();
            instance.common = new List<AppliedEffect>();

            foreach (EventChoice choice in definition.choices ?? new EventChoice[0])
            {
                if (choice == null) continue;
                var option = new ChoiceOptionDto { choiceId = choice.id };
                EventEffect[] effects = choice.effects ?? new EventEffect[0];
                option.effects.AddRange(ResolveList(effects, Indexes(effects.Length), choice.id, instance, definition, today, rng));
                instance.options.Add(option);
            }

            var common = new List<EventEffect>();
            var indexes = new List<int>();
            EventEffect[] own = definition.effects ?? new EventEffect[0];
            for (int i = 0; i < own.Length; i++)
            {
                if (own[i] == null || !Carries(planned, own[i])) continue;
                common.Add(own[i]);
                indexes.Add(i);
            }
            instance.common.AddRange(ResolveList(common, indexes, ContentNames.CommonChoice, instance, definition, today, rng));
        }

        /// <summary>
        /// Resolves one list: the chance of every effect first (one number each), then the handlers in order. <paramref name="indexes"/> are the places of the
        /// effects in the definition (they go into the ids); <paramref name="listId"/> is the choice id or <c>common</c>.
        /// </summary>
        public List<AppliedEffect> ResolveList(IList<EventEffect> specs, IList<int> indexes, string listId, EventInstanceDto instance, EventDefinition definition,
            int today, IModRng rng)
        {
            var result = new List<AppliedEffect>();
            if (specs == null || specs.Count == 0) return result;

            bool[] hits = ChanceRoller.Roll(specs, rng);
            for (int i = 0; i < specs.Count; i++)
            {
                EventEffect spec = specs[i];
                int index = indexes != null && i < indexes.Count ? indexes[i] : i;
                string id = EffectId(instance.id, listId, index);
                string companyKey = EffectScope.IsGlobal(spec) ? EventCompanyKeys.Global : instance.companyKey;
                result.Add(ResolveOne(spec, id, hits[i], CallFor(definition, instance, spec, companyKey, today, rng)));
            }
            return result;
        }

        /// <summary>The call a handler gets for an effect of the instance: the pack's intensity when the effect is <c>scalable</c>.</summary>
        public EffectCall CallFor(EventDefinition definition, EventInstanceDto instance, EventEffect spec, string companyKey, int today, IModRng rng)
        {
            PackOptions pack = null;
            try
            {
                pack = definition != null ? _packOptions(definition.packId) : null;
            }
            catch (Exception ex)
            {
                _onFault("effects/pack", ex);
            }

            return new EffectCall
            {
                Definition = definition,
                Instance = instance,
                Spec = spec,
                CompanyKey = companyKey,
                Today = today,
                Rng = rng,
                Severity = EffectMath.SeverityOf(spec, pack),
                Contributions = _contributions,
            };
        }

        /// <summary>One effect: its handler's <c>Resolve</c> (once), the engine's fields, the text. Never throws.</summary>
        public AppliedEffect ResolveOne(EventEffect spec, string id, bool hit, EffectCall call)
        {
            string type = spec != null ? spec.type : null;
            IEffectHandlerPort handler = type != null ? _table.Get(type) : null;

            AppliedEffect effect = null;
            bool failed = false;
            if (handler == null)
            {
                failed = true;
                _onFault("effects/resolve/" + (type ?? "?"), new InvalidOperationException("no handler is registered for the effect type '" + type + "'"));
            }
            else
            {
                try
                {
                    effect = handler.Resolve(spec, call);
                }
                catch (Exception ex)
                {
                    failed = true;
                    _onFault("effects/resolve/" + type, ex);
                }
            }

            if (effect == null) effect = new AppliedEffect();
            if (float.IsNaN(effect.value) || float.IsInfinity(effect.value))
            {
                failed = true;
                _onFault("effects/resolve/" + type, new InvalidOperationException("the effect '" + id + "' resolved to a number that is not finite"));
                effect.value = 0f;
            }

            effect.id = id;
            effect.instanceId = call != null && call.Instance != null ? call.Instance.id : 0;
            effect.type = type;
            if (string.IsNullOrEmpty(effect.companyKey)) effect.companyKey = call != null ? call.CompanyKey : null;
            if (effect.channel == null) effect.channel = string.Empty;
            if (effect.durationDays <= 0 && spec != null && handler != null && handler.IsTimed) effect.durationDays = Math.Max(0, spec.durationDays);
            if (effect.durationDays < 0) effect.durationDays = 0;
            effect.startDay = 0;
            effect.endDay = 0;
            effect.chance = ChanceRoller.ChanceOf(spec);
            effect.hit = hit;
            effect.group = ChanceRoller.GroupOf(spec);
            effect.state = failed ? AppliedEffectStates.Failed : AppliedEffectStates.Pending;

            Describe(effect);
            return effect;
        }

        /// <summary>Fills <c>textKey</c> / <c>textArgs</c> from the handler's <c>Describe</c> of the resolved numbers (a failure gives <see cref="FailedTextKey"/>).</summary>
        public void Describe(AppliedEffect effect)
        {
            if (effect == null) return;
            IEffectHandlerPort handler = effect.type != null ? _table.Get(effect.type) : null;
            EffectText text = new EffectText(FailedTextKey, effect.type ?? string.Empty);
            if (handler != null && !string.Equals(effect.state, AppliedEffectStates.Failed, StringComparison.Ordinal))
            {
                try
                {
                    text = handler.Describe(effect);
                }
                catch (Exception ex)
                {
                    _onFault("effects/describe/" + effect.type, ex);
                    text = new EffectText(FailedTextKey, effect.type);
                }
            }

            effect.textKey = string.IsNullOrEmpty(text.Key) ? FailedTextKey : text.Key;
            effect.textArgs = Copy(text.Args);
        }

        /// <summary><c>&lt;instanceId&gt;/&lt;choiceId|common&gt;/&lt;index&gt;</c>.</summary>
        public static string EffectId(int instanceId, string listId, int index)
        {
            return instanceId.ToString(CultureInfo.InvariantCulture) + "/" + (listId ?? ContentNames.CommonChoice) + "/" + index.ToString(CultureInfo.InvariantCulture);
        }

        private static bool Carries(PlannedInstance planned, EventEffect effect)
        {
            if (planned == null) return true;
            foreach (EventEffect own in planned.TargetEffects)
            {
                if (ReferenceEquals(own, effect)) return true;
            }
            foreach (EventEffect own in planned.GlobalEffects)
            {
                if (ReferenceEquals(own, effect)) return true;
            }
            return false;
        }

        private static List<int> Indexes(int count)
        {
            var list = new List<int>(count);
            for (int i = 0; i < count; i++) list.Add(i);
            return list;
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
