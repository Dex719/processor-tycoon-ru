using System;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The rules of a chain of events (D9, NFR-7; design.md "Цепочка FireEvent"): how deep it may go, whom the next event hits, and the flags a chain leaves
    /// for <c>es.eventFlag</c>.
    /// <list type="bullet">
    /// <item><b>Depth.</b> An instance not started by a chain has depth 0; an instance started by a <c>FireEvent</c> of an instance of depth <c>d</c> has
    /// <c>d + 1</c>. A <c>FireEvent</c> that would start depth 9 or more is not applied (<c>Skipped</c>): at most <see cref="MaxDepth"/> steps.</item>
    /// <item><b>Target</b> (<c>params.target</c>): <c>Same</c> (default) - the company of the instance; <c>Player</c> - the player; <c>Definition</c> - the
    /// <c>targets</c> of the next event decide. A global instance has no company of its own: <c>Same</c> then works as <c>Definition</c>.</item>
    /// <item><b>Flags.</b> A <c>FireEvent</c> may name a <c>flag</c>: when the effect is applied (its chance hit) the flag is set for the company of the instance,
    /// on that day; <c>{ "type": "es.eventFlag", "flag": ... }</c> reads it. The name follows the rule of content names (letters, digits, <c>_ - .</c>, at most 40).</item>
    /// </list>
    /// </summary>
    public static class ChainFlags
    {
        /// <summary>The deepest a chain may go (NFR-7).</summary>
        public const int MaxDepth = 8;

        /// <summary>The optional parameter of <c>FireEvent</c> that sets a flag.</summary>
        public const string FlagParam = "flag";

        public const string TargetSame = "Same";
        public const string TargetPlayer = "Player";
        public const string TargetDefinition = "Definition";

        /// <summary>A valid flag name.</summary>
        public static bool IsFlagName(string flag)
        {
            return ContentNames.IsName(flag, ContentNames.MaxShortLength);
        }

        /// <summary>The depth of the instance a <c>FireEvent</c> of an instance at <paramref name="parentDepth"/> would start, or -1 when that is too deep.</summary>
        public static int NextDepth(int parentDepth)
        {
            int next = Math.Max(0, parentDepth) + 1;
            return next > MaxDepth ? -1 : next;
        }

        /// <summary>The company a chained event is fired for; null - the <c>targets</c> of the definition decide.</summary>
        public static string TargetOf(string target, string instanceCompany)
        {
            if (string.Equals(target, TargetPlayer, StringComparison.OrdinalIgnoreCase)) return PTMod.Core.Logic.Companies.CompanyKey.Player;
            if (string.Equals(target, TargetDefinition, StringComparison.OrdinalIgnoreCase)) return null;
            if (string.IsNullOrEmpty(instanceCompany) || string.Equals(instanceCompany, EventCompanyKeys.Global, StringComparison.Ordinal)) return null;
            return instanceCompany;
        }
    }
}
