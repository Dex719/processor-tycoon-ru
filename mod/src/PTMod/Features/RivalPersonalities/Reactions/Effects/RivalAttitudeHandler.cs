using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using PTMod.Features.EventSystem;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The effect <c>RivalAttitude</c> (design.md, Component 9): an event changes the attitude of the AI company it is aimed at to the player by
    /// <c>params.delta</c> (-100..+100), with the sensitivity of the archetype, the clip and the history of <see cref="AttitudeLedger.Change"/>; the reason
    /// in the history is <c>event:&lt;event id&gt;</c>. Other packs may use it in their content (the type works in every pack). A target that is not an AI
    /// company, a bankrupt one and one without a character are skipped: the ledger does not keep them.
    /// </summary>
    internal sealed class RivalAttitudeHandler : IEventEffectHandler
    {
        private const string Name = "RivalAttitude";
        private const float MaxDelta = 100f;

        public string Type
        {
            get { return ReactionTags.EffectAttitude; }
        }

        public bool Validate(EventEffect spec, IList<string> errors)
        {
            IEffectParams p = spec != null ? spec.@params : null;
            if (p == null || !p.Has("delta"))
            {
                if (errors != null) errors.Add(Name + ": the parameter 'delta' (the change of the attitude, -100..100) is required");
                return false;
            }

            float delta = p.GetFloat("delta", float.NaN);
            if (float.IsNaN(delta) || float.IsInfinity(delta) || delta < -MaxDelta || delta > MaxDelta)
            {
                if (errors != null) errors.Add(Name + ": 'delta' must be a number from -100 to 100");
                return false;
            }
            return true;
        }

        public AppliedEffect Resolve(EventEffect spec, EventContext ctx)
        {
            IEffectParams p = spec != null ? spec.@params : null;
            float delta = p != null ? p.GetFloat("delta", 0f) : 0f;
            delta = float.IsNaN(delta) || float.IsInfinity(delta) ? 0f : Math.Max(-MaxDelta, Math.Min(MaxDelta, delta));

            EffectArgs args = EffectSupport.ResolveArgs(ctx);
            if (string.IsNullOrEmpty(args.RivalKey) && ctx != null) args.RivalKey = ctx.CompanyKey;
            args.RivalName = AttitudeNotifier.NameOf(args.UniqueId);

            return new AppliedEffect
            {
                type = Type,
                value = delta,
                textArgs = args.ToSaved(),
            };
        }

        public void Apply(AppliedEffect effect, EventContext ctx)
        {
            RivalPersonalitiesFeature feature = EffectSupport.Instance;
            var rival = ctx != null ? ctx.Company as ProcessorTycoon.CompanySystem.AICompany : null;
            if (feature == null || ReferenceEquals(rival, null) || rival == null)
            {
                EffectSupport.Skip(effect, Name, "the target is not an AI company");
                return;
            }

            string eventId = ctx.Definition != null ? ctx.Definition.id : (ctx.Instance != null && ctx.Instance.Data != null ? ctx.Instance.Data.eventId : null);
            string reason = "event:" + (eventId ?? "?");
            bool taken = feature.Attitudes.Change(rival.UniqueID, effect.value, reason);
            if (!taken)
            {
                EffectSupport.Skip(effect, Name, "the attitude of ai:" + rival.UniqueID.ToString(CultureInfo.InvariantCulture) + " is not kept (no character or bankrupt)");
            }
        }

        public EffectText Describe(AppliedEffect effect)
        {
            EffectArgs args = EffectArgs.FromSaved(effect != null ? effect.textArgs : null);
            float delta = effect != null ? effect.value : 0f;
            string signed = (delta > 0f ? "+" : string.Empty) + EffectArgs.Number(delta);
            return new EffectText("rival.fx.attitude", args.ToSaved(signed));
        }
    }
}
