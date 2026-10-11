using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.EventSystem;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The one effect of the pack that is not a button: the line about a public flaw of an AI company in the event journal (FR-11 item 4, design.md Component 13). It is applied
    /// only through <c>EventEffects.ApplyDirect</c> by <see cref="FlawNews"/>, with the five texts of the news as the arguments of the firing (the company, the CPU, the type, the
    /// severity, the reaction). <see cref="Apply"/> does nothing: the AI company has answered already (<see cref="FlawAiEngine"/>); the instance exists to put the line into the
    /// journal, which keeps the numbers (text key and arguments) in the save, like for any effect.
    /// </summary>
    internal sealed class FlawNewsEffect : IEventEffectHandler
    {
        private const string Site = "effect/FlawNews/";

        public string Type
        {
            get { return FlawEffectTypes.News; }
        }

        /// <summary>No parameters.</summary>
        public bool Validate(EventEffect spec, IList<string> errors)
        {
            return true;
        }

        public AppliedEffect Resolve(EventEffect spec, EventContext ctx)
        {
            EventInstanceDto data = ctx != null && ctx.Instance != null ? ctx.Instance.Data : null;
            string companyKey = ctx != null && !string.IsNullOrEmpty(ctx.CompanyKey) ? ctx.CompanyKey : ModCompanies.PlayerKey;

            return new AppliedEffect
            {
                instanceId = data == null ? 0 : data.id,
                type = Type,
                companyKey = companyKey,
                channel = string.Empty,
                value = 0f,
                chance = 1f,
                hit = true,
                state = AppliedEffectStates.Pending,
                textKey = FlawEffectModel.NewsKey,
                textArgs = Copy(data == null ? null : data.args),
            };
        }

        /// <summary>Nothing to apply: the incident was handled by the policy of the AI company; this effect is the journal line.</summary>
        public void Apply(AppliedEffect effect, EventContext ctx)
        {
        }

        public EffectText Describe(AppliedEffect effect)
        {
            try
            {
                string key = effect == null || string.IsNullOrEmpty(effect.textKey) ? FlawEffectModel.NewsKey : effect.textKey;
                return new EffectText(key, Copy(effect == null ? null : effect.textArgs));
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(DesignFlawRecallsFeature.FeatureId, Site + "Describe", e);
                return new EffectText(FlawEffectModel.NewsKey, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
            }
        }

        /// <summary>The texts of the line, always <see cref="FlawNewsPlanner.ArgCount"/> of them (a missing one is empty), so the table never meets an unknown placeholder.</summary>
        private static string[] Copy(string[] args)
        {
            var result = new string[FlawNewsPlanner.ArgCount];
            for (int i = 0; i < result.Length; i++) result[i] = args != null && i < args.Length && args[i] != null ? args[i] : string.Empty;
            return result;
        }
    }
}
