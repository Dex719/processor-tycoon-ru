using System;

namespace PTMod.Core.Logic.Modifiers
{
    /// <summary>
    /// Channel names and families (FR-12 items 1, 15). A family is declared with a name ending in ".*"
    /// (<c>gpu.demand.*</c>) and covers every channel that starts with the text before the star
    /// (<c>gpu.demand.mobile</c>) with the bounds of the family; an exact declaration of the same name
    /// (<c>gpu.demand</c>, <c>gpu.demand.mobile</c>) always wins. Names are case-sensitive and compared by ordinal.
    /// </summary>
    public static class ChannelFamilies
    {
        /// <summary>The suffix that makes a name a family.</summary>
        public const string Wildcard = ".*";

        /// <summary>Family of the GPU market segments (owner: gpu-product-line).</summary>
        public const string GpuDemandFamily = "gpu.demand.*";

        private const int MaxNameLength = 128;

        /// <summary>True for a non-empty prefix followed by ".*" and no other star: "gpu.demand.*".</summary>
        public static bool IsFamilyName(string name)
        {
            return name != null
                   && name.Length > Wildcard.Length
                   && name.EndsWith(Wildcard, StringComparison.Ordinal)
                   && name.IndexOf('*') == name.Length - 1
                   && IsValidBody(name, name.Length - 2);
        }

        /// <summary>The text a family's channels start with ("gpu.demand.*" gives "gpu.demand."); null if it is not a family name.</summary>
        public static string PrefixOf(string familyName)
        {
            return IsFamilyName(familyName) ? familyName.Substring(0, familyName.Length - 1) : null;
        }

        /// <summary>True when <paramref name="channel"/> is a plain name below the family: the prefix and then at least one more character.</summary>
        public static bool Matches(string familyName, string channel)
        {
            string prefix = PrefixOf(familyName);
            if (prefix == null || channel == null) return false;
            return channel.Length > prefix.Length
                   && channel.StartsWith(prefix, StringComparison.Ordinal)
                   && IsValidChannelName(channel, false);
        }

        /// <summary>
        /// Syntax of a channel name: letters, digits, '.', '_' and '-', no empty parts ("a..b", ".a", "a."), at most
        /// 128 characters. With <paramref name="allowFamily"/> a trailing ".*" is accepted too.
        /// </summary>
        public static bool IsValidChannelName(string name, bool allowFamily)
        {
            if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength) return false;
            if (allowFamily && name.EndsWith(Wildcard, StringComparison.Ordinal)) return IsFamilyName(name);
            return IsValidBody(name, name.Length);
        }

        /// <summary>Channel of the demand for one CPU segment: "market.demand.highend" and so on; null for <see cref="Segment.Any"/> and unknown values.</summary>
        public static string MarketDemand(Segment segment)
        {
            switch (segment)
            {
                case Segment.HighEnd: return "market.demand.highend";
                case Segment.MidRange: return "market.demand.midrange";
                case Segment.LowEnd: return "market.demand.lowend";
                case Segment.Industries: return "market.demand.industries";
                default: return null;
            }
        }

        /// <summary>
        /// Channel of one GPU market segment, "gpu.demand.&lt;segmentId&gt;" (a member of the <see cref="GpuDemandFamily"/>
        /// family); null if the id is empty or has characters that a channel name cannot have.
        /// </summary>
        public static string GpuDemand(string segmentId)
        {
            string name = "gpu.demand." + segmentId;
            return !string.IsNullOrEmpty(segmentId) && IsValidChannelName(name, false) ? name : null;
        }

        private static bool IsValidBody(string name, int length)
        {
            if (length == 0) return false;
            bool previousDot = true; // a leading dot is as wrong as a double one
            for (int i = 0; i < length; i++)
            {
                char c = name[i];
                if (c == '.')
                {
                    if (previousDot) return false;
                    previousDot = true;
                    continue;
                }

                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!ok) return false;
                previousDot = false;
            }
            return !previousDot; // a trailing dot
        }
    }
}
