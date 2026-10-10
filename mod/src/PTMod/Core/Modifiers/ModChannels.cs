using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Features;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Modifiers;

namespace PTMod.Core
{
    /// <summary>
    /// The names of the channels the core declares (FR-12 items 1 and 15; the bounds are in <c>core.json</c>, 27 entries) and the reading of that
    /// list. Use the constants instead of retyping the strings: the channel namespace is the registry's, not a feature's (<c>research.speed</c>,
    /// not <c>my-feature.research.speed</c>).
    /// <list type="bullet">
    /// <item>The standard channels are read by the core's patches (P1-P11, P19); the channels of features (<c>production.memoryCost</c>,
    /// <c>gpu.*</c>, <c>acquisition.price</c>) are declared by the core in advance (D21) and read by the code of the owner feature.</item>
    /// <item>The demand of a market segment is one channel per segment (<see cref="MarketDemand"/>); the GPU segments are the family
    /// <c>gpu.demand.*</c> (<see cref="GpuDemandSegment"/>).</item>
    /// </list>
    /// </summary>
    public static class ModChannels
    {
        public const string ResearchSpeed = "research.speed";
        public const string ResearchCost = "research.cost";
        public const string ProductionUnitCost = "production.unitCost";
        public const string ProductionOutput = "production.output";
        public const string FactoryOperatingCost = "factory.operatingCost";
        public const string MarketRatingCpu = "market.rating.cpu";
        public const string PopularityGain = "popularity.gain";
        public const string CpuFailureRate = "cpu.failureRate";
        public const string ContractReward = "contract.reward";
        public const string ContractSignChance = "contract.signChance";
        public const string BankInterest = "bank.interest";
        public const string BankCreditLimit = "bank.creditLimit";
        public const string Taxes = "taxes";
        public const string ConstructionCost = "construction.cost";
        public const string DevelopmentCost = "development.cost";

        /// <summary>P19: <c>CpuMath.ProjectCost</c>, the context carries a <see cref="DesignContext"/>; never cached.</summary>
        public const string DevelopmentCostDesign = "development.cost.design";

        public const string MarketDemandHighEnd = "market.demand.highend";
        public const string MarketDemandMidRange = "market.demand.midrange";
        public const string MarketDemandLowEnd = "market.demand.lowend";
        public const string MarketDemandIndustries = "market.demand.industries";

        // Channels of features, declared by the core in advance (D21); the owner feature reads them.
        public const string ProductionMemoryCost = "production.memoryCost";   // market-shocks
        public const string ProductionWaferCost = "production.waferCost";     // market-shocks
        public const string GpuDemand = "gpu.demand";                         // gpu-product-line
        public const string GpuRating = "gpu.rating";                         // gpu-product-line
        public const string GpuVramCost = "gpu.vramCost";                     // gpu-product-line
        public const string AcquisitionPrice = "acquisition.price";           // rival-acquisitions

        /// <summary>The channel of the demand of one CPU segment (<c>market.demand.highend</c> and so on); null for <see cref="Segment.Any"/>.</summary>
        public static string MarketDemand(Segment segment)
        {
            return ChannelFamilies.MarketDemand(segment);
        }

        /// <summary>The channel of one GPU segment, <c>gpu.demand.&lt;segmentId&gt;</c> (a member of the family <c>gpu.demand.*</c>); null for an empty or malformed id.</summary>
        public static string GpuDemandSegment(string segmentId)
        {
            return ChannelFamilies.GpuDemand(segmentId);
        }

        // ---- patch P1: the seven values of ModifierType ----

        /// <summary>How many values <c>ModifierType</c> has (0-6).</summary>
        internal const int ModifierTypeCount = 7;

        // Index = the game's ModifierType: ResearchSpeed 0, MaintainanceCost 1, ConstructionCost 2, DevelopmentCost 3, ResearchCost 4, Taxes 5, Interest 6.
        private static readonly string[] ByModifierType =
        {
            ResearchSpeed, FactoryOperatingCost, ConstructionCost, DevelopmentCost, ResearchCost, Taxes, BankInterest,
        };

        /// <summary>The channel behind a value of the game's <c>ModifierType</c> (patch P1); null for a value the game does not have.</summary>
        internal static string OfModifierType(int modifierType)
        {
            return modifierType >= 0 && modifierType < ByModifierType.Length ? ByModifierType[modifierType] : null;
        }

        // ---- core.json ----

        private const string OwnerCore = FeatureGraph.CoreFeatureId;

        private static readonly string[] KnownMembers =
        {
            "name", "min", "max", "balanceMin", "balanceMax", "global", "perCompany", "perCpu", "perSegment", "cacheable", "owner", "descriptionKey",
        };

        /// <summary>
        /// Builds the declarations from the <c>channels</c> array of <c>core.json</c>. The keys are lowercase camel case, the members of
        /// <see cref="ChannelSpec"/> are not, so the mapping is explicit: <c>name</c>, <c>min</c>, <c>max</c> (required), <c>balanceMin</c>,
        /// <c>balanceMax</c> (default 0 = no balance range), <c>global</c>, <c>perCompany</c>, <c>perCpu</c>, <c>perSegment</c> (default false),
        /// <c>cacheable</c> (default true), <c>owner</c> (default <c>mod-framework</c>), <c>descriptionKey</c>. An element that is not an object, lacks a
        /// field or does not pass <see cref="ChannelSpec.Validate"/> is left out with a line in <paramref name="problems"/>; an unknown member is only
        /// reported (a typo such as <c>balanceMn</c> would otherwise silently remove a balance range). Pure: no logging, no game.
        /// </summary>
        internal static List<ChannelSpec> ReadSpecs(JsonArray channels, List<string> problems)
        {
            var specs = new List<ChannelSpec>();
            if (channels == null) return specs;

            for (int i = 0; i < channels.Count; i++)
            {
                string where = "channels[" + i + "]";
                var item = channels[i] as JsonObject;
                if (item == null)
                {
                    Report(problems, where + ": expected an object; skipped");
                    continue;
                }

                ChannelSpec spec = ReadSpec(item, where, problems);
                if (spec != null) specs.Add(spec);
            }
            return specs;
        }

        private static ChannelSpec ReadSpec(JsonObject item, string where, List<string> problems)
        {
            foreach (string member in item.Keys)
            {
                if (Array.IndexOf(KnownMembers, member) < 0) Report(problems, where + ": unknown member '" + member + "' is ignored");
            }

            string name;
            if (!item.TryGetString("name", out name) || string.IsNullOrEmpty(name))
            {
                Report(problems, where + ": 'name' is missing or not a string; skipped");
                return null;
            }
            where += " (" + name + ")";

            double min, max;
            if (!TryNumber(item, "min", out min) || !TryNumber(item, "max", out max))
            {
                Report(problems, where + ": 'min' and 'max' are required numbers; skipped");
                return null;
            }

            double balanceMin = 0, balanceMax = 0;
            if (!OptionalNumber(item, "balanceMin", ref balanceMin, where, problems) || !OptionalNumber(item, "balanceMax", ref balanceMax, where, problems)) return null;

            string owner;
            if (!item.TryGetString("owner", out owner) || string.IsNullOrEmpty(owner)) owner = OwnerCore;
            string descriptionKey;
            if (!item.TryGetString("descriptionKey", out descriptionKey) || string.IsNullOrEmpty(descriptionKey)) descriptionKey = null;

            var spec = new ChannelSpec
            {
                Name = name,
                Min = (float)min,
                Max = (float)max,
                BalanceMin = (float)balanceMin,
                BalanceMax = (float)balanceMax,
                Global = item.GetBool("global", false),
                PerCompany = item.GetBool("perCompany", false),
                PerCpu = item.GetBool("perCpu", false),
                PerSegment = item.GetBool("perSegment", false),
                Cacheable = item.GetBool("cacheable", true),
                OwnerFeature = owner,
                DescriptionKey = descriptionKey,
            };

            string error;
            if (!spec.Validate(out error))
            {
                Report(problems, where + ": " + error + "; skipped");
                return null;
            }
            return spec;
        }

        /// <summary>A finite number that also fits a float (the registry works in floats).</summary>
        private static bool TryNumber(JsonObject item, string key, out double value)
        {
            return item.TryGetDouble(key, out value) && !double.IsNaN(value) && !double.IsInfinity(value) && Math.Abs(value) <= float.MaxValue;
        }

        /// <summary>An optional number: absent keeps the value; present but not a number is a problem (false).</summary>
        private static bool OptionalNumber(JsonObject item, string key, ref double value, string where, List<string> problems)
        {
            JsonNode node;
            if (!item.TryGetValue(key, out node) || node.IsNull) return true;

            double read;
            if (!TryNumber(item, key, out read))
            {
                Report(problems, where + ": '" + key + "' is not a number; skipped");
                return false;
            }
            value = read;
            return true;
        }

        private static void Report(List<string> problems, string text)
        {
            if (problems != null) problems.Add(text);
        }
    }
}
