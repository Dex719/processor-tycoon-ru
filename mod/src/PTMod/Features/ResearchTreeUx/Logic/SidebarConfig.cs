using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The section <c>sidebar</c> of <c>Content/research-tree-ux/ui.json</c> (FR-10 item 1): the range of the width factor of the side panel,
    /// <c>widthMin</c> and <c>widthMax</c> times the original width. Both must lie in <see cref="SidebarWidthRule.AbsoluteMin"/>...<see cref="SidebarWidthRule.AbsoluteMax"/>
    /// (not narrower than the game made the panel, not above twice its width) and the minimum may not exceed the maximum. A value that is missing keeps its default
    /// silently; one that is not a number or is out of range keeps it with a line for the log (decision 0006); a pair in the wrong order gives both defaults.
    /// </summary>
    public sealed class SidebarConfig
    {
        /// <summary>The smallest factor; 1 lets the player return to the original width.</summary>
        public float WidthMin = SidebarWidthRule.DefaultMin;

        /// <summary>The largest factor.</summary>
        public float WidthMax = SidebarWidthRule.DefaultMax;

        /// <summary>The values of the spec: 1.0 and 1.6.</summary>
        public static SidebarConfig Defaults()
        {
            return new SidebarConfig();
        }

        /// <summary>Reads the <c>sidebar</c> section of the merged ui.json. A null file or a missing section gives the defaults. Never throws.</summary>
        public static SidebarConfig FromConfig(JsonObject config, List<string> problems)
        {
            var result = new SidebarConfig();
            if (config == null) return result;

            JsonNode node;
            if (!config.TryGetValue("sidebar", out node)) return result;

            var section = node as JsonObject;
            if (section == null)
            {
                Report(problems, "sidebar: expected an object, the defaults are used");
                return result;
            }

            result.WidthMin = ReadFactor(section, "widthMin", result.WidthMin, problems);
            result.WidthMax = ReadFactor(section, "widthMax", result.WidthMax, problems);
            if (result.WidthMin > result.WidthMax)
            {
                Report(problems, "sidebar: widthMin " + result.WidthMin + " is above widthMax " + result.WidthMax + ", the defaults "
                    + SidebarWidthRule.DefaultMin + " and " + SidebarWidthRule.DefaultMax + " are used");
                result.WidthMin = SidebarWidthRule.DefaultMin;
                result.WidthMax = SidebarWidthRule.DefaultMax;
            }
            return result;
        }

        /// <summary>The factor of the player's choice held inside this range (a factor that is none gives the original width).</summary>
        public float Clamp(float factor)
        {
            return SidebarWidthRule.Clamp(factor, WidthMin, WidthMax);
        }

        private static float ReadFactor(JsonObject section, string key, float fallback, List<string> problems)
        {
            double value;
            if (!section.TryGetDouble(key, out value))
            {
                if (section.ContainsKey(key)) Report(problems, "sidebar." + key + ": not a number, " + fallback + " is used");
                return fallback;
            }

            if (double.IsNaN(value) || double.IsInfinity(value) || value < SidebarWidthRule.AbsoluteMin || value > SidebarWidthRule.AbsoluteMax)
            {
                Report(problems, "sidebar." + key + ": expected a number from " + SidebarWidthRule.AbsoluteMin + " to " + SidebarWidthRule.AbsoluteMax
                    + ", " + fallback + " is used");
                return fallback;
            }
            return (float)value;
        }

        private static void Report(List<string> problems, string message)
        {
            if (problems != null) problems.Add(message);
        }
    }
}
