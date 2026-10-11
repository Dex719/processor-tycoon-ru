using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>How the calendar treats the history of the game being played (FR-17 item 1).</summary>
    public enum HistMode
    {
        /// <summary>The roles are looked for in the world as it is, with fallbacks: the history adapts to the game.</summary>
        Adaptive = 0,

        /// <summary>The history is told as it was: a role that is not there makes the event "not happen in this history"; no fallbacks.</summary>
        Strict = 1,
    }

    /// <summary>
    /// The parameters of the feature in the hub (FR-17 item 1): the mode, the intensity, a switch for each of the twelve categories, and three
    /// more switches. The feature's own on/off switch is the hub's, so there is no <c>enabled</c> here. Only the declarations live in this pure
    /// file (so that a test can check them); reading the values is <c>HistSettings</c>. The labels are keys of
    /// <c>l10n/&lt;lang&gt;/historical-events.json</c>: <c>hist.param.&lt;key&gt;.label</c> and <c>.tooltip</c>.
    /// </summary>
    public static class HistParamSpecs
    {
        /// <summary>0 = <see cref="HistMode.Adaptive"/> (default), 1 = <see cref="HistMode.Strict"/>.</summary>
        public const string Mode = "mode";

        /// <summary>The size of the effects (0.5 to 1.5, 1.0 by default): the severity of the pack of the event system.</summary>
        public const string Intensity = "intensity";

        /// <summary>The letters carry a "how it was in reality" note (on by default).</summary>
        public const string HistoryNotes = "historyNotes";

        /// <summary>The chronicle shows what is still to come (off by default).</summary>
        public const string Spoilers = "spoilers";

        /// <summary>The debug page: fire an entry now, make the player the leader (off by default).</summary>
        public const string Debug = "debug";

        public const float IntensityMin = 0.5f;
        public const float IntensityMax = 1.5f;
        public const float IntensityDefault = 1.0f;

        /// <summary>All the declarations in the order of the settings page: mode, intensity, the categories, the notes, the spoilers, the debug switch. Fresh objects on every call.</summary>
        public static ParamSpec[] Create(string featureId)
        {
            var specs = new List<ParamSpec>
            {
                new ParamSpec
                {
                    FeatureId = featureId,
                    Key = Mode,
                    LabelKey = LabelKey(Mode),
                    TooltipKey = TooltipKey(Mode),
                    Type = ParamType.Int,
                    Default = (int)HistMode.Adaptive,
                    Min = (int)HistMode.Adaptive,
                    Max = (int)HistMode.Strict,
                    Step = 1,
                },
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
                    Step = 0.05,
                },
            };

            foreach (string category in HistCategories.All)
            {
                specs.Add(Switch(featureId, category, HistCategories.DefaultEnabled(category)));
            }

            specs.Add(Switch(featureId, HistoryNotes, true));
            specs.Add(Switch(featureId, Spoilers, false));
            specs.Add(Switch(featureId, Debug, false));
            return specs.ToArray();
        }

        public static string LabelKey(string key)
        {
            return "hist.param." + key + ".label";
        }

        public static string TooltipKey(string key)
        {
            return "hist.param." + key + ".tooltip";
        }

        private static ParamSpec Switch(string featureId, string key, bool defaultOn)
        {
            return new ParamSpec
            {
                FeatureId = featureId,
                Key = key,
                LabelKey = LabelKey(key),
                TooltipKey = TooltipKey(key),
                Type = ParamType.Bool,
                Default = defaultOn ? 1 : 0,
                Min = 0,
                Max = 1,
                Step = 1,
                RequiresReload = false,
            };
        }
    }
}
