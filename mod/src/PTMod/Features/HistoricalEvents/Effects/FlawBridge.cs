using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Features.DesignFlawRecalls;
using PTMod.Features.EventSystem;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The effect type <c>ForceDesignFlaw</c> (design.md, Component 8; FR-12): the bridge from the calendar to the flaw mechanic of <c>design-flaw-recalls</c>. The event of the entry
    /// (<c>hist.1994.fdiv</c>, <c>hist.2018.meltdown</c>) is fired for each company of the role; its effect picks the CPU of that company by the rule <c>pick</c>
    /// (<c>topSellingHighEnd24m</c> or <c>perCompanyAboveAvgIps60m</c>), takes the severity (<c>severityLeader</c> for the leader of HighEnd, <c>severityOthers</c> for the rest) and calls
    /// <c>DesignFlawRecallsFeature.Registry.ForceFlaw(cpuSaveId, typeId, severity, discovery)</c>; the window of the decision, the press and the recall are that feature's. When
    /// the feature is off or the company has no CPU for the rule, <c>fallbackEventId</c> is fired for the company the next day (<see cref="FlawBridgeCore"/> does the work and is tested
    /// without the game; here is only the wiring).
    /// <para>
    /// <b>Resolve</b> changes nothing: it works out what would happen now for the line of the letter (<c>curve</c>: severity, discovery, type, number of CPUs, 1 when the fallback is what
    /// will happen). <b>Apply</b> picks again and forces the flaw: the pick depends on the sales of the day it happens, not on the day the event was made.
    /// </para>
    /// </summary>
    internal sealed class FlawBridge : HistEffectBase
    {
        private const string FlawKey = "hist.fx.flaw";
        private const string FallbackKey = "hist.fx.flaw.fallback";

        private readonly HistServices _services;

        internal FlawBridge(HistServices services)
            : base(HistEffectTypes.ForceDesignFlaw)
        {
            _services = services;
        }

        protected override string FallbackTextKey
        {
            get { return FlawKey; }
        }

        protected override AppliedEffect ResolveCore(EventEffect spec, EventContext ctx)
        {
            ForceFlawParams p = ForceFlawParams.Read(EffectSpecs.Raw(spec), null);
            string company = CompanyOf(ctx);
            FlawPlan plan = _services.Flaws.Plan(p, company);

            float[] curve =
            {
                FlawSeverities.CodeOf(plan.Severity),
                FlawDiscoveries.CodeOf(p.Discovery),
                FlawTypeNames.CodeOf(p.TypeId),
                plan.Cpus.Count,
                plan.NeedsFallback ? 1f : 0f,
            };
            return NewEffect(ctx, company, plan.NeedsFallback ? FallbackKey : FlawKey, 0f, curve);
        }

        protected override void ApplyCore(AppliedEffect effect, EventContext ctx)
        {
            EventEffect spec = EffectSpecs.Find(ctx == null ? null : ctx.Definition, effect);
            if (spec == null)
            {
                ModLog.Warn(HistIds.Feature, "ForceDesignFlaw " + (effect == null ? "?" : effect.id) + ": the effect is not in the definition of its event; nothing is applied");
                return;
            }

            string company = effect.companyKey;
            if (!CompanyKey.IsValid(company))
            {
                ModLog.Warn(HistIds.Feature, "ForceDesignFlaw " + effect.id + ": the effect acts on '" + company + "', which is not a company; give it a company scope");
                return;
            }

            ForceFlawParams p = ForceFlawParams.Read(EffectSpecs.Raw(spec), null);
            string eventId = ctx.Definition != null ? ctx.Definition.id : null;
            FlawResult result = _services.Flaws.Run(p, company, ArgsOf(ctx), eventId);
            ModLog.Debug(HistIds.Feature, "ForceDesignFlaw " + effect.id + " for " + company + ": picked " + result.Plan.Cpus.Count + ", forced " + result.Forced
                + (result.FallbackAsked ? ", fallback " + p.FallbackEventId + " " + result.Fallback : string.Empty));
        }

        protected override EffectText DescribeCore(AppliedEffect effect)
        {
            if (string.Equals(effect.textKey, FallbackKey, System.StringComparison.Ordinal)) return new EffectText(FallbackKey);

            string type = FlawTypeNames.FromCode((int)At(effect, 2));
            string typeKey = "hist.flaw.type." + (type.Length > 0 ? type : "other");
            string severity = ModL10n.L("hist.flaw.severity." + FlawSeverities.FromCode((int)At(effect, 0)));
            string discovery = ModL10n.L("hist.flaw.discovery." + FlawDiscoveries.FromCode((int)At(effect, 1)));
            return new EffectText(FlawKey, ModL10n.L(typeKey), severity, discovery);
        }
    }

    /// <summary>
    /// The game side of <see cref="IFlawGate"/>: <c>DesignFlawRecallsFeature.Registry</c> (the public API of design-flaw-recalls, a null object while that feature is off). The names
    /// of the effect are mapped to the enums of the API here and nowhere else.
    /// </summary>
    internal sealed class GameFlawGate : IFlawGate
    {
        internal static readonly GameFlawGate Instance = new GameFlawGate();

        private GameFlawGate()
        {
        }

        public bool IsAvailable
        {
            get { return DesignFlawRecallsApi.IsAvailable; }
        }

        public bool Force(int cpuSaveId, string typeId, string severity, string discovery)
        {
            return DesignFlawRecallsFeature.Registry.ForceFlaw(cpuSaveId, typeId, SeverityOf(severity), DiscoveryOf(discovery));
        }

        private static FlawSeverity SeverityOf(string severity)
        {
            switch (severity)
            {
                case FlawSeverities.Minor: return FlawSeverity.Minor;
                case FlawSeverities.Critical: return FlawSeverity.Critical;
                default: return FlawSeverity.Major;
            }
        }

        private static DiscoverySource DiscoveryOf(string discovery)
        {
            switch (discovery)
            {
                case FlawDiscoveries.Internal: return DiscoverySource.Internal;
                case FlawDiscoveries.Api: return DiscoverySource.Api;
                default: return DiscoverySource.External;
            }
        }
    }
}
