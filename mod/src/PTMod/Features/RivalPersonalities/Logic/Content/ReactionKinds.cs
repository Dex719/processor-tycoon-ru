using System;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// What the player did that a rival may answer (FR-5, design.md Component 7). The names are the keys of <c>reactions</c> in
    /// <c>archetypes.json</c> and of <c>triggerDeltas</c> in <c>config.json</c>, so they must not change.
    /// </summary>
    public enum TriggerType
    {
        /// <summary>The player released a CPU clearly stronger than the best CPU of the rival in the segment.</summary>
        StrongLaunch = 0,

        /// <summary>The player sells about as good a CPU for a much lower price.</summary>
        PriceUndercut = 1,

        /// <summary>The player's market share in a segment grew while the rival's fell.</summary>
        ShareGain = 2,

        /// <summary>The player won an OEM tender that the rival offered on.</summary>
        OemLoss = 3,

        /// <summary>The player researched a leading technology.</summary>
        TechLead = 4,
    }

    /// <summary>
    /// What a rival does about it (FR-6, FR-7). The names are the <c>type</c> of the rules in <c>archetypes.json</c>, the keys of
    /// <c>reactionParams</c> and the type in the saved reactions, so they must not change. <see cref="Partnership"/> is the friendly offer of
    /// FR-12: it is not an answer to a trigger and never appears in the reaction table of an archetype.
    /// </summary>
    public enum ReactionType
    {
        /// <summary>The rival cuts the prices of its CPUs in a segment.</summary>
        PriceCut = 0,

        /// <summary>The rival's designers answer the player's CPU with a new one.</summary>
        CounterLaunch = 1,

        /// <summary>The rival pushes its offers to OEM customers (chance of the rival's tenders x1.5).</summary>
        OemPush = 2,

        /// <summary>The rival makes an offer to the player's engineers.</summary>
        Poach = 3,

        /// <summary>The rival leaks damaging rumours (reputation of the player -3).</summary>
        Smear = 4,

        /// <summary>A friendly offer of a rival with a good attitude (FR-12).</summary>
        Partnership = 5,
    }

    /// <summary>
    /// Names and simple facts about <see cref="TriggerType"/> and <see cref="ReactionType"/>. The names are matched exactly (the same spelling as
    /// in the content and in the save), not as numbers and not case-insensitively.
    /// </summary>
    public static class ReactionKinds
    {
        private static readonly string[] TriggerNameList = { "StrongLaunch", "PriceUndercut", "ShareGain", "OemLoss", "TechLead" };

        private static readonly string[] ReactionNameList = { "PriceCut", "CounterLaunch", "OemPush", "Poach", "Smear", "Partnership" };

        /// <summary>The names of the triggers in the order of <see cref="TriggerType"/>. A copy.</summary>
        public static string[] TriggerNames()
        {
            return (string[])TriggerNameList.Clone();
        }

        /// <summary>The names of the reactions in the order of <see cref="ReactionType"/>. A copy.</summary>
        public static string[] ReactionNames()
        {
            return (string[])ReactionNameList.Clone();
        }

        /// <summary>The trigger with this exact name; false for null, an empty text, another spelling or a number.</summary>
        public static bool TryParseTrigger(string name, out TriggerType trigger)
        {
            for (int i = 0; i < TriggerNameList.Length; i++)
            {
                if (string.Equals(name, TriggerNameList[i], StringComparison.Ordinal))
                {
                    trigger = (TriggerType)i;
                    return true;
                }
            }
            trigger = TriggerType.StrongLaunch;
            return false;
        }

        /// <summary>The reaction with this exact name; false for null, an empty text, another spelling or a number.</summary>
        public static bool TryParseReaction(string name, out ReactionType reaction)
        {
            for (int i = 0; i < ReactionNameList.Length; i++)
            {
                if (string.Equals(name, ReactionNameList[i], StringComparison.Ordinal))
                {
                    reaction = (ReactionType)i;
                    return true;
                }
            }
            reaction = ReactionType.PriceCut;
            return false;
        }

        /// <summary>The name of a trigger as the content spells it; an empty text for a value outside the enum.</summary>
        public static string NameOf(TriggerType trigger)
        {
            int index = (int)trigger;
            return index >= 0 && index < TriggerNameList.Length ? TriggerNameList[index] : string.Empty;
        }

        /// <summary>The name of a reaction as the content and the save spell it; an empty text for a value outside the enum.</summary>
        public static string NameOf(ReactionType reaction)
        {
            int index = (int)reaction;
            return index >= 0 && index < ReactionNameList.Length ? ReactionNameList[index] : string.Empty;
        }

        /// <summary>
        /// A hostile reaction is subject to the limits of FR-6 (grace period, cooldown, two at a time, four a year, the attitude gate). Every
        /// reaction except the friendly <see cref="ReactionType.Partnership"/> is hostile.
        /// </summary>
        public static bool IsHostile(ReactionType reaction)
        {
            return reaction != ReactionType.Partnership;
        }

        /// <summary>The reaction may stand in the reaction table of an archetype (an answer to a trigger): everything but the friendly offer.</summary>
        public static bool IsTableReaction(ReactionType reaction)
        {
            return reaction != ReactionType.Partnership;
        }
    }
}
