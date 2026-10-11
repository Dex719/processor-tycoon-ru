using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.EventSystem;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The base of the four effect types of the pack (FR-5..FR-9, design.md Component 9): a button of a decision window is one effect, <c>FlawRecall</c>,
    /// <c>FlawStepping</c>, <c>FlawSoftwarePatch</c> or <c>FlawIgnore</c>. The effect holds no parameters of its own: the id of the record is the first argument of the
    /// firing (<c>Args[0]</c>), and every amount is read from the quote saved in the record (D9), so the number under the button is the number that is applied.
    /// <list type="bullet">
    /// <item><b>Resolve</b> (once, when the window is made, for every button): the numbers of <see cref="FlawEffectModel"/> from the quote. Changes nothing in the game.</item>
    /// <item><b>Apply</b> (the button that was chosen): <see cref="FlawResponseApplier.Apply"/>. A second call for a record that no longer waits does nothing (a warning in
    /// the log). An exception is counted and goes on to the engine, which marks the effect <c>Failed</c>; the record stays waiting.</item>
    /// <item><b>Describe</b>: the line under the button, made from the saved numbers only.</item>
    /// </list>
    /// The rules of the arithmetic are in the pure <see cref="FlawEffectModel"/> and <see cref="FlawResponseEngine"/>; this class only connects them to the engine.
    /// </summary>
    internal abstract class FlawEffectBase : IEventEffectHandler
    {
        private const string Site = "effect/";

        private readonly FlawResponse _response;

        protected FlawEffectBase(FlawResponse response)
        {
            _response = response;
        }

        public string Type
        {
            get { return FlawEffectTypes.TypeOf(_response); }
        }

        /// <summary>The effects have no parameters: whatever is written in <c>params</c> is ignored.</summary>
        public bool Validate(EventEffect spec, IList<string> errors)
        {
            return true;
        }

        public AppliedEffect Resolve(EventEffect spec, EventContext ctx)
        {
            EventInstanceDto data = ctx != null && ctx.Instance != null ? ctx.Instance.Data : null;
            int instanceId = data == null ? 0 : data.id;
            string companyKey = ctx != null && !string.IsNullOrEmpty(ctx.CompanyKey) ? ctx.CompanyKey : ModCompanies.PlayerKey;

            FlawEffectNumbers numbers;
            try
            {
                int recordId;
                FlawDecisionPlanner.TryRecordId(data == null ? null : data.args, out recordId);
                FlawRecord record = FindRecord(recordId);
                if (record == null) ModLog.Warn(DesignFlawRecallsFeature.FeatureId, Type + ": no flaw record " + recordId + " for the window; the amount is 0");

                numbers = FlawEffectModel.Resolve(_response, recordId, record == null ? null : record.Quote, FlawFormat.Instance);
            }
            catch (Exception e)
            {
                // The window is made anyway, with nothing to pay under this button; Apply will say that there is no record.
                ModHooks.ReportFault(DesignFlawRecallsFeature.FeatureId, Site + Type + "/Resolve", e);
                numbers = FlawEffectModel.Resolve(_response, 0, null, new PlainFlawFormat());
            }

            return new AppliedEffect
            {
                instanceId = instanceId,
                type = Type,
                companyKey = companyKey,
                channel = string.Empty,
                value = numbers.Value,
                curve = numbers.Curve,
                chance = 1f,
                hit = true,
                state = AppliedEffectStates.Pending,
                textKey = numbers.TextKey,
                textArgs = numbers.TextArgs,
            };
        }

        public void Apply(AppliedEffect effect, EventContext ctx)
        {
            try
            {
                int recordId;
                if (!FlawEffectModel.TryRecordId(effect == null ? null : effect.curve, out recordId))
                {
                    EventInstanceDto data = ctx != null && ctx.Instance != null ? ctx.Instance.Data : null;
                    FlawDecisionPlanner.TryRecordId(data == null ? null : data.args, out recordId);
                }

                FlawRecord record = FindRecord(recordId);
                if (record == null)
                {
                    ModLog.Warn(DesignFlawRecallsFeature.FeatureId, Type + ": no flaw record " + recordId + "; nothing is applied");
                    return;
                }

                bool automatic = ctx != null && ctx.Instance != null && ctx.Instance.Data != null && ctx.Instance.Data.auto;
                DesignFlawRecallsFeature.Instance.Applier.Apply(record, _response, automatic);
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(DesignFlawRecallsFeature.FeatureId, Site + Type + "/Apply", e);
                throw;
            }
        }

        public EffectText Describe(AppliedEffect effect)
        {
            try
            {
                string key;
                string[] args;
                FlawEffectModel.DescribeCore(_response, effect == null ? 0f : effect.value, effect == null ? null : effect.curve, FlawFormat.Instance, out key, out args);
                return new EffectText(key, args);
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(DesignFlawRecallsFeature.FeatureId, Site + Type + "/Describe", e);
                return new EffectText(FlawEffectModel.RecallKey, "0");
            }
        }

        private static FlawRecord FindRecord(int recordId)
        {
            DesignFlawRecallsFeature feature = DesignFlawRecallsFeature.Instance;
            return feature == null || recordId <= 0 ? null : feature.Store.Find(recordId);
        }
    }
}
