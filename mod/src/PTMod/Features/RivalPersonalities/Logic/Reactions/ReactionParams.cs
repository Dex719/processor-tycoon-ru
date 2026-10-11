using System;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The numbers of a reaction (design.md, Component 9: "Параметры удара обработчики берут из архетипа соперника и config.json, а не из события, так
    /// что одно описание события подходит всем архетипам"): the default is in <c>config.json</c> (<c>reactions.&lt;Type&gt;</c>), an archetype may set its
    /// own in <c>reactionParams</c> (<c>price_warrior</c>: <c>PriceCut.depth</c> 0.15). The result is held to the range the content validator allows,
    /// so a damaged value cannot reach the game. A missing archetype or config gives the defaults of the design. Pure.
    /// </summary>
    public static class ReactionParams
    {
        /// <summary>
        /// The factor of the <c>OemPush</c> on the chance of the rival's tenders (<c>contract.signChance</c> x1.5 for 365 days). The effect is a standard
        /// <c>ModifierTimed</c> of <c>events.json</c>; the number is here so the intelligence panel can show the size of the effect. A test keeps the two equal.
        /// </summary>
        public const float OemPushSignChance = 1.5f;

        /// <summary>The share of the price taken off by a <c>PriceCut</c> (0.08-0.15).</summary>
        public static float PriceCutDepth(Archetype archetype, RivalConfig config)
        {
            RivalConfig cfg = config ?? RivalConfig.Defaults();
            return Clip(Of(archetype, ReactionType.PriceCut, "depth", cfg.reactions.PriceCut.depth), 0f, 0.5f, 0.08f);
        }

        /// <summary>The price cut keeps the price at least at <c>RealUnitCost × (1 + this)</c> (0.08).</summary>
        public static float PriceCutFloorMargin(Archetype archetype, RivalConfig config)
        {
            RivalConfig cfg = config ?? RivalConfig.Defaults();
            return Clip(Of(archetype, ReactionType.PriceCut, "floorMargin", cfg.reactions.PriceCut.floorMargin), 0f, 0.5f, 0.08f);
        }

        /// <summary>The multiplier of the design rating of a designer of the segment during a <c>CounterLaunch</c> (1.35).</summary>
        public static float ApprovalBoost(Archetype archetype, RivalConfig config)
        {
            RivalConfig cfg = config ?? RivalConfig.Defaults();
            return Clip(Of(archetype, ReactionType.CounterLaunch, "approvalBoost", cfg.reactions.CounterLaunch.approvalBoost), 1f, 3f, 1.35f);
        }

        /// <summary>The multiplier of the research funding cap during a <c>CounterLaunch</c> (1.25).</summary>
        public static float FundingBoost(Archetype archetype, RivalConfig config)
        {
            RivalConfig cfg = config ?? RivalConfig.Defaults();
            return Clip(Of(archetype, ReactionType.CounterLaunch, "fundingBoost", cfg.reactions.CounterLaunch.fundingBoost), 1f, 3f, 1.25f);
        }

        /// <summary>The <c>research.speed</c> of the rival after it took the engineers of the player (1.10).</summary>
        public static float PoachRivalResearchSpeed(Archetype archetype, RivalConfig config)
        {
            RivalConfig cfg = config ?? RivalConfig.Defaults();
            return Clip(Of(archetype, ReactionType.Poach, "rivalResearchSpeed", cfg.reactions.Poach.rivalResearchSpeed), 1f, 2f, 1.10f);
        }

        /// <summary>How long the bonus of the rival lasts, in game days (120).</summary>
        public static int PoachRivalBoostDays(Archetype archetype, RivalConfig config)
        {
            RivalConfig cfg = config ?? RivalConfig.Defaults();
            float days = Clip(Of(archetype, ReactionType.Poach, "rivalBoostDays", cfg.reactions.Poach.rivalBoostDays), 1f, 720f, 120f);
            return (int)Math.Round(days);
        }

        private static float Of(Archetype archetype, ReactionType reaction, string name, float fallback)
        {
            return archetype != null ? archetype.ParamOr(reaction, name, fallback) : fallback;
        }

        private static float Clip(float value, float min, float max, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return fallback;
            if (value < min) return min;
            return value > max ? max : value;
        }
    }
}
