using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.EventSystem;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.MarketShocks
{
    /// <summary>
    /// The effect type <c>InventoryLoss</c> (FR-7 item 3, design.md, "Типы эффектов"): a share of the finished CPUs in the target's warehouses is lost (a
    /// contaminated batch, a flooded depot). A one-off effect: <c>Apply</c> subtracts, nothing lasts. The pure part is <see cref="LossSpec"/>,
    /// <see cref="InventoryLossMath"/> and <see cref="IStockBook"/>; the game part is <see cref="GameStockBook"/>.
    /// <list type="bullet">
    /// <item><b>Params:</b> <c>fraction</c> (a number or a pair <c>[lo, hi]</c> in [0, 0.5]) and the common <c>scope</c> and <c>scalable</c>.</item>
    /// <item><b>Resolve</b> (once, D1): takes exactly one number from the generator of the instance, rolls the share (scaled by the intensity of the pack, at
    /// most 0.5), and for every CPU that is made by the target (<c>Cpu.get_Manufacturer()</c>; any company for a global effect), is not a variant
    /// (<c>Base == null</c>) and is not retired works out <c>floor(Stock · f)</c>. The effect keeps <c>curve = [saveId, lost, saveId, lost, ...]</c> and
    /// <c>value = Σ lost</c>.</item>
    /// <item><b>Apply</b> takes <c>min(lost, Stock)</c> from <c>CpuBase.MarketData.Stock</c> of each CPU found by <c>CpuDataProvider.GetCpu</c>; a CPU that is
    /// not found is passed over, the others are still written off. The stock never goes below 0.</item>
    /// </list>
    /// </summary>
    public sealed class InventoryLossEffect : IEventEffectHandler
    {
        /// <summary>The type name as it is written in <c>shocks.json</c>.</summary>
        public const string TypeName = LossSpec.EffectName;

        private const string Site = "InventoryLoss/";

        private readonly Func<float> _severity;

        /// <summary>An effect type that reads the intensity from the options of the pack (<c>ShockPack.Options</c>).</summary>
        public InventoryLossEffect()
            : this(null)
        {
        }

        /// <summary>An effect type with its own source of the intensity (tests, the balance simulator).</summary>
        public InventoryLossEffect(Func<float> severity)
        {
            _severity = severity ?? CurrentSeverity;
        }

        public string Type
        {
            get { return TypeName; }
        }

        public bool Validate(EventEffect spec, IList<string> errors)
        {
            IList<string> problems = errors ?? new List<string>();
            try
            {
                LossSpec loss;
                return LossSpec.TryRead(RawParams(spec), problems, out loss);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(ShockPack.PackId, Site + "Validate", ex);
                problems.Add(TypeName + ": internal error while checking the parameters (" + ex.Message + ")");
                return false;
            }
        }

        public AppliedEffect Resolve(EventEffect spec, EventContext ctx)
        {
            var problems = new List<string>();
            LossSpec loss;
            if (!LossSpec.TryRead(RawParams(spec), problems, out loss))
                throw new InvalidOperationException(TypeName + " has invalid parameters: " + string.Join("; ", problems.ToArray()));

            // One number from the generator of the instance, whatever the data (D5).
            float draw = ctx != null && ctx.Rng != null ? ctx.Rng.Value() : 0.5f;
            float fraction = loss.Roll(draw, _severity());

            string companyKey = ctx == null || string.IsNullOrEmpty(ctx.CompanyKey) ? EventCompanyKeys.Global : ctx.CompanyKey;
            int total = 0;
            float[] plan = new float[0];
            if (fraction > 0f && TryFindTarget(ctx, companyKey, out ICompany manufacturer))
                plan = InventoryLossMath.Plan(GameStockBook.Collect(manufacturer), fraction, out total);

            var effect = new AppliedEffect
            {
                instanceId = ctx != null && ctx.Instance != null && ctx.Instance.Data != null ? ctx.Instance.Data.id : 0,
                type = TypeName,
                companyKey = companyKey,
                channel = string.Empty,
                value = total,
                curve = plan,
                chance = 1f,
                hit = true,
                state = AppliedEffectStates.Pending,
            };

            EffectText text = Describe(effect);
            effect.textKey = text.Key;
            effect.textArgs = text.Args;
            return effect;
        }

        public void Apply(AppliedEffect effect, EventContext ctx)
        {
            int missing;
            int removed = InventoryLossMath.ApplyPlan(effect.curve, GameStockBook.Instance, out missing);
            if (missing > 0)
                ModLog.Debug(ShockPack.PackId, TypeName + " " + effect.id + ": " + missing + " CPU(s) of the plan were not found and were passed over; " + removed + " unit(s) written off");
        }

        public EffectText Describe(AppliedEffect effect)
        {
            try
            {
                return DescribeCore(effect);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(ShockPack.PackId, Site + "Describe", ex);
                return new EffectText("ms.fx.inventory.units", "0");
            }
        }

        private static EffectText DescribeCore(AppliedEffect effect)
        {
            int units = InventoryLossMath.TotalOf(effect.curve);
            string count = ShockFormat.Count(units, ShockText.IsRussian);

            float worth;
            if (units > 0 && GameStockBook.TryPrice(effect.curve, out worth))
                return new EffectText("ms.fx.inventory", count, ShockText.Money(worth));

            return new EffectText("ms.fx.inventory.units", count);
        }

        /// <summary>The company whose warehouses are hit: null for every company (a global effect), otherwise the target; false when the target cannot be found.</summary>
        private static bool TryFindTarget(EventContext ctx, string companyKey, out ICompany manufacturer)
        {
            manufacturer = null;
            if (string.Equals(companyKey, EventCompanyKeys.Global, StringComparison.Ordinal)) return true;

            manufacturer = ctx != null && !ReferenceEquals(ctx.Company, null) ? ctx.Company : ModCompanies.Resolve(companyKey);
            return !ReferenceEquals(manufacturer, null);
        }

        private static JsonObject RawParams(EventEffect spec)
        {
            return spec == null || spec.@params == null ? null : spec.@params.Raw;
        }

        private static float CurrentSeverity()
        {
            return ShockPack.Severity;
        }
    }
}
