using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.EventSystem;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// Finds the effect of an event definition that an applied effect came from. The engine gives <c>Apply</c> and <c>Describe</c> only the applied effect (the numbers); the
    /// parameters that were not turned into numbers (the case's whole set of odds, the rule's segments) are read again from the definition by the id of the applied effect
    /// (<c>&lt;instanceId&gt;/&lt;choiceId|common&gt;/&lt;index&gt;</c>), so what is applied is what the content says.
    /// </summary>
    internal static class EffectSpecs
    {
        private const string CommonChoice = "common";

        /// <summary>The effect of the definition behind the applied effect, or null (a direct instance has no definition, an id of another shape, a changed definition).</summary>
        public static EventEffect Find(EventDefinition definition, AppliedEffect effect)
        {
            if (definition == null || effect == null || string.IsNullOrEmpty(effect.id)) return null;

            string[] parts = effect.id.Split('/');
            if (parts.Length < 3) return null;

            int index;
            if (!int.TryParse(parts[parts.Length - 1], out index) || index < 0) return null;

            string choiceId = parts[parts.Length - 2];
            EventEffect[] effects = null;
            if (choiceId == CommonChoice)
            {
                effects = definition.effects;
            }
            else if (definition.choices != null)
            {
                foreach (EventChoice choice in definition.choices)
                {
                    if (choice != null && string.Equals(choice.id, choiceId, StringComparison.Ordinal))
                    {
                        effects = choice.effects;
                        break;
                    }
                }
            }
            return effects != null && index < effects.Length ? effects[index] : null;
        }

        /// <summary>The first effect of the event itself (not of a choice) of this type, or null: the common effect that opens the process the choices refer to.</summary>
        public static EventEffect FindCommon(EventDefinition definition, string type)
        {
            if (definition == null || definition.effects == null) return null;
            foreach (EventEffect effect in definition.effects)
            {
                if (effect != null && string.Equals(effect.type, type, StringComparison.Ordinal)) return effect;
            }
            return null;
        }

        /// <summary>The <c>params</c> object of an effect (never null).</summary>
        public static JsonObject Raw(EventEffect spec)
        {
            JsonObject raw = spec == null || spec.@params == null ? null : spec.@params.Raw;
            return raw ?? new JsonObject();
        }
    }

    /// <summary>
    /// The base of the eight effect types of the feature (the six services, the bridge to the flaw mechanic and the flag) (design.md, Component 8). The pattern is that of every handler of the event system: <b>Validate</b> when the
    /// content is loaded (the ranges of <see cref="HistEffectRules"/>), <b>Resolve</b> once when the instance is made, for every choice, so that the player sees the exact
    /// numbers before deciding (changes nothing in the game), <b>Apply</b> when the effect takes place (carries out what <c>Resolve</c> worked out and recomputes nothing),
    /// <b>Describe</b> from the saved numbers only. Every call is guarded: a failure is counted against the feature and goes on to the engine, which marks the effect
    /// <c>Failed</c> and lets the day go on; <c>Describe</c> answers a neutral line instead.
    /// <para>
    /// The numbers an effect keeps live in the applied effect: <c>value</c> is the money paid when the choice is applied (negative, which is how the AI chooser reads the
    /// cost), <c>curve</c> holds the rest in an order that belongs to the type.
    /// </para>
    /// </summary>
    internal abstract class HistEffectBase : IEventEffectHandler
    {
        private readonly string _type;

        protected HistEffectBase(string type)
        {
            _type = type;
        }

        public string Type
        {
            get { return _type; }
        }

        /// <summary>The key of the line shown when the real one cannot be made.</summary>
        protected abstract string FallbackTextKey { get; }

        /// <summary>Extra checks that need the game (the registry of channels); a line added here rejects the event. Nothing by default.</summary>
        protected virtual void ValidateInGame(JsonObject raw, List<string> lines)
        {
        }

        protected abstract AppliedEffect ResolveCore(EventEffect spec, EventContext ctx);

        protected abstract void ApplyCore(AppliedEffect effect, EventContext ctx);

        protected abstract EffectText DescribeCore(AppliedEffect effect);

        public bool Validate(EventEffect spec, IList<string> errors)
        {
            try
            {
                var lines = new List<string>();
                JsonObject raw = EffectSpecs.Raw(spec);
                bool ok = HistEffectRules.Validate(_type, raw, lines);

                // What only the running game knows (a channel of ModModifiers).
                int before = lines.Count;
                ValidateInGame(raw, lines);
                if (lines.Count != before) ok = false;

                if (errors != null)
                {
                    foreach (string line in lines) errors.Add(_type + ": " + line);
                }
                return ok;
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(HistIds.Feature, _type + "/Validate", ex);
                if (errors != null) errors.Add(_type + ": internal error while checking the parameters (" + ex.Message + ")");
                return false;
            }
        }

        public AppliedEffect Resolve(EventEffect spec, EventContext ctx)
        {
            try
            {
                return ResolveCore(spec, ctx);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(HistIds.Feature, _type + "/Resolve", ex);
                throw;
            }
        }

        public void Apply(AppliedEffect effect, EventContext ctx)
        {
            try
            {
                ApplyCore(effect, ctx);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(HistIds.Feature, _type + "/Apply", ex);
                throw;
            }
        }

        public EffectText Describe(AppliedEffect effect)
        {
            try
            {
                return DescribeCore(effect);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(HistIds.Feature, _type + "/Describe", ex);
                return new EffectText(FallbackTextKey);
            }
        }

        // ---- helpers for the types ----

        /// <summary>The positional arguments of the firing (<see cref="HistArgs"/>); never null.</summary>
        protected static string[] ArgsOf(EventContext ctx)
        {
            string[] args = ctx == null || ctx.Instance == null || ctx.Instance.Data == null ? null : ctx.Instance.Data.args;
            return args ?? new string[0];
        }

        /// <summary>The company the effect acts on (<c>player</c> when the engine names none).</summary>
        protected static string CompanyOf(EventContext ctx)
        {
            return ctx == null || string.IsNullOrEmpty(ctx.CompanyKey) ? ModCompanies.PlayerKey : ctx.CompanyKey;
        }

        /// <summary>A new applied effect with the numbers worked out: the engine fills in its id and day.</summary>
        protected AppliedEffect NewEffect(EventContext ctx, string companyKey, string textKey, float value, float[] curve)
        {
            return new AppliedEffect
            {
                instanceId = ctx != null && ctx.Instance != null && ctx.Instance.Data != null ? ctx.Instance.Data.id : 0,
                type = _type,
                companyKey = companyKey,
                channel = string.Empty,
                value = value,
                curve = curve,
                chance = 1f,
                hit = true,
                state = AppliedEffectStates.Pending,
                textKey = textKey,
            };
        }

        /// <summary>The number at <paramref name="index"/> of the curve; 0 when the curve is missing or short (a saved effect from an older shape).</summary>
        protected static float At(AppliedEffect effect, int index)
        {
            return effect != null && effect.curve != null && index >= 0 && index < effect.curve.Length ? effect.curve[index] : 0f;
        }

        protected static HistServices Services
        {
            get
            {
                HistoricalEventsFeature feature = HistoricalEventsFeature.Instance;
                HistServices services = feature == null ? null : feature.Services;
                if (services == null) throw new InvalidOperationException("the services of historical-events are not there yet");
                return services;
            }
        }

        protected static bool IsPlayer(string companyKey)
        {
            return string.Equals(companyKey, ModCompanies.PlayerKey, StringComparison.Ordinal);
        }
    }
}
