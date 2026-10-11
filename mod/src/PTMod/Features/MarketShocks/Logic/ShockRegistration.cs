using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Ui;

namespace PTMod.Features.MarketShocks
{
    /// <summary>
    /// What the entry class of the feature (<c>MarketShocksFeature</c>) declares about itself and registers (FR-1, design.md, "Фича"): the id and the
    /// dependency, the keys of the name and the description, the effect types and the predicates it hands to the event system, the line the log
    /// carries when the pack is up (AC-1.1) and the two parameters of the settings (FR-14 item 1). Pure data and checks, so that the numbers of the
    /// registration can be tested without the game; the class that does the registering reads its lists from here, so a type added to the list
    /// is registered and counted in the log line at once.
    /// </summary>
    public static class ShockRegistration
    {
        /// <summary>The slug of the feature, the pack, the content folder and the namespace <c>PTMod.Features.MarketShocks</c>.</summary>
        public const string FeatureId = "market-shocks";

        /// <summary>The one hard dependency: the engine that runs the pack. Without it the core makes the feature Unavailable and no patch is applied (AC-1.2).</summary>
        public const string EventSystemId = "event-system";

        public const string NameKey = "ms.feature.name";
        public const string DescriptionKey = "ms.feature.desc";

        /// <summary>The events file of the pack: <c>Content/market-shocks/shocks.json</c>.</summary>
        public const string EventsFile = "shocks";

        /// <summary>The records of <c>shocks.json</c>: 19 shocks, two of them with a warning letter (FR-1 item 2).</summary>
        public const int ExpectedEvents = 21;

        /// <summary>News to the player about the incidents of rivals only for the N AI companies with the biggest share of the market (FR-2 item 5, FR-11 item 6); the journal keeps all.</summary>
        public const int RivalNewsTopN = 5;

        /// <summary>How many classes of patches the feature declares: the material cost of a CPU, the two wafer formulas, the description of the memory.</summary>
        public const int PatchClassCount = 3;

        /// <summary>The effect types of the pack, in the order they are registered with <c>EventEffects.Register</c>.</summary>
        public static readonly IList<string> EffectTypes = new List<string> { RampSpec.EffectName, WaveSpec.EffectName, LossSpec.EffectName }.AsReadOnly();

        /// <summary>The predicates of the pack, in the order they are registered with <c>EventConditions.Register</c>.</summary>
        public static readonly IList<string> PredicateNames =
            new List<string> { ShockPredicateLogic.SellsBelowCostName, ShockPredicateLogic.HasRecentCpuName }.AsReadOnly();

        /// <summary>The features the entry class lists in <c>DependsOn</c>. A fresh array each time, as the interface hands it out.</summary>
        public static string[] DependsOn()
        {
            return new[] { EventSystemId };
        }

        /// <summary>
        /// The text after "<c>[PTMod] [market-shocks] INFO </c>" when the pack is up: <c>pack registered: 21 events, 3 effect types, 2 predicates</c> (AC-1.1).
        /// A negative count of events is taken as none.
        /// </summary>
        public static string PackRegisteredLine(int events)
        {
            return "pack registered: " + Math.Max(0, events) + " events, " + EffectTypes.Count + " effect types, " + PredicateNames.Count + " predicates";
        }
    }

    /// <summary>
    /// The two parameters of the feature in the hub (FR-14 item 1, design.md, "Фича" item 5): <c>intensity</c> (0.5 "mild", 1 "normal", 1.5 "harsh",
    /// 2 "chaos") and <c>frequency</c> (0.5, 1, 1.5). The feature's own on/off switch is the hub's, so there is no <c>enabled</c> here. Only the
    /// declarations and the safe reading of a value live here; the values themselves are <c>ModSettings</c>'. The labels are keys of
    /// <c>l10n/&lt;lang&gt;/market-shocks.json</c>: <c>ms.settings.&lt;key&gt;</c> and <c>ms.settings.&lt;key&gt;.tooltip</c>.
    /// </summary>
    public static class ShockParamSpecs
    {
        /// <summary>The size of the deviations and of the sums (<c>PackOptions.Severity</c>).</summary>
        public const string Intensity = "intensity";

        /// <summary>The multiplier of the daily chance of the categories (<c>PackOptions.Frequency</c>).</summary>
        public const string Frequency = "frequency";

        /// <summary>The key of the title of the feature's page in the hub: "Настройки: рыночные шоки".</summary>
        public const string PageTitleKey = "ms.settings.title";

        /// <summary>The key of the line of the page that says where the sliders stand: <c>{0}</c> the name of the intensity level, <c>{1}</c> its multiplier, <c>{2}</c> the frequency.</summary>
        public const string PageNowKey = "ms.settings.now";

        public const float IntensityMin = 0.5f;
        public const float IntensityMax = 2f;
        public const float IntensityDefault = 1f;
        public const float IntensityStep = 0.5f;

        public const float FrequencyMin = 0.5f;
        public const float FrequencyMax = 1.5f;
        public const float FrequencyDefault = 1f;
        public const float FrequencyStep = 0.5f;

        /// <summary>The declarations in the order of the settings page. Fresh objects on every call (the core takes a copy).</summary>
        public static ParamSpec[] Create(string featureId)
        {
            return new[]
            {
                new ParamSpec
                {
                    FeatureId = featureId,
                    Key = Intensity,
                    LabelKey = LabelKey(Intensity),
                    TooltipKey = TooltipKey(Intensity),
                    Type = ParamType.Float,
                    Default = IntensityDefault,
                    Min = IntensityMin,
                    Max = IntensityMax,
                    Step = IntensityStep,
                },
                new ParamSpec
                {
                    FeatureId = featureId,
                    Key = Frequency,
                    LabelKey = LabelKey(Frequency),
                    TooltipKey = TooltipKey(Frequency),
                    Type = ParamType.Float,
                    Default = FrequencyDefault,
                    Min = FrequencyMin,
                    Max = FrequencyMax,
                    Step = FrequencyStep,
                },
            };
        }

        public static string LabelKey(string key)
        {
            return "ms.settings." + key;
        }

        public static string TooltipKey(string key)
        {
            return "ms.settings." + key + ".tooltip";
        }

        /// <summary>The intensity as the pack uses it: a number outside the slider (or not a number) falls back to the nearest end or to the default.</summary>
        public static float UsableIntensity(float value)
        {
            return Usable(value, IntensityMin, IntensityMax, IntensityDefault);
        }

        /// <summary>The frequency as the pack uses it, with the same safety.</summary>
        public static float UsableFrequency(float value)
        {
            return Usable(value, FrequencyMin, FrequencyMax, FrequencyDefault);
        }

        /// <summary>
        /// The intensity the pack runs on: held to the slider (<see cref="UsableIntensity"/>) and moved to the nearest of its four levels, halves up. The hub's
        /// slider already works in steps; the snap is for a value that did not come from it (a hand-edited <c>ptmod_settings.json</c>: 1.3 runs as ×1.5).
        /// </summary>
        public static float SnapIntensity(float value)
        {
            return SliderMath.Snap(UsableIntensity(value), IntensityMin, IntensityMax, IntensityStep);
        }

        /// <summary>The frequency the pack runs on, snapped to the levels 0.5, 1 and 1.5 the same way.</summary>
        public static float SnapFrequency(float value)
        {
            return SliderMath.Snap(UsableFrequency(value), FrequencyMin, FrequencyMax, FrequencyStep);
        }

        /// <summary>
        /// The key of the name of a level of the intensity slider: <c>ms.settings.intensity.mild</c> (up to ×0.74), <c>.normal</c> (up to ×1.24),
        /// <c>.harsh</c> (up to ×1.74) and <c>.chaos</c> (above).
        /// </summary>
        public static string IntensityLevelKey(float intensity)
        {
            float value = UsableIntensity(intensity);
            string level = value < 0.75f ? "mild" : value < 1.25f ? "normal" : value < 1.75f ? "harsh" : "chaos";
            return "ms.settings.intensity." + level;
        }

        private static float Usable(float value, float min, float max, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return fallback;
            return value < min ? min : value > max ? max : value;
        }
    }
}
